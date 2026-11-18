# mini (.NET): refactor to idiomatic C#

`MIGRATION-PLAN.md` describes a deliberate 1:1 port. Its rules were: one Python function becomes one C# method,
`JsonObject` for all API data, static classes for modules, and no interfaces, DI or tests. This plan replaces those
conventions with ordinary .NET design. The behavior stays the same. `MIGRATION-PLAN.md` stays as the history of the
port and as the list of accepted gaps.

## 1. Where the code is now

There are about 4,000 lines in one project and one namespace. The design problems, grouped:

| Smell | Where | Why it hurts |
|---|---|---|
| **God class.** `Agent` is a `partial class` split over 6 files (~1,000 lines). It owns the transcript, request building, stream retries, tool dispatch, `ask_user` console I/O, subagents, compaction, pricing, the judge's HTTP client, the goal state machine and the prompt texts | `Agent/*.cs` | SRP. The partial files split the text, not the responsibilities. Every part reaches every private field |
| **Static mutable state** | `Shell.Running/Jobs`, `Agent.MaxTokens`, `Config` (static ctor that reads files and env), `Paths.Home`, `Cli.running/atPrompt/activeHub`, `Api.frozen/captured` | Hidden coupling, untestable, order-dependent initialization |
| **Stringly typed** | Stop reasons, goal status, permission mode, verdicts, tool names, event kinds, block types: `Json.Str(b["type"]) == "tool_use"` appears everywhere | No compiler help. A typo is a silent bug |
| **Tuples as contracts** | `(int? Code, string Output)`, `(string Verdict, string Why)`, `(List<JsonObject>, bool)`, `GoalDefaults` | Unnamed at call sites, can't grow |
| **Public mutable fields** | `Agent.Model/System/Messages/Cost…`, `Approver.Auto/Reason/Judge`, `Ui.Indent`, `Session.Path`, `Job.*` | No encapsulation. `Messages` can be changed from outside, so the append-only rule is only a convention |
| **Temporal coupling and a cycle** | `approver.Judge = agent.JudgeAsync` after construction; `Approver.Reason` is written in `OkAsync` and read by the caller afterwards | Approver and Agent depend on each other. The result travels through a side channel |
| **switch/if dispatch** | `LocalTools.CallAsync`, `Agent.DispatchAsync`, `Cli.CommandAsync` | OCP: every new tool or command means editing a switch. A tool's schema lives in `Schemas.cs`/`ExtraSchemas`, apart from its code |
| **Console everywhere** | `Approver.OkAsync`, `Agent.DispatchAsync` (ask_user), `Cli`, `Ui` | The core logic can't run without a terminal, so it can't be tested |
| **Ambient environment and clock** | `Environment.GetEnvironmentVariable` in 8 files, `DateTime.Now/UtcNow` in 5 | Configuration is scattered; time can't be faked (the off-peak price tests need that) |
| **Duplicated helpers** | Code-point `Head/Cut/Tail` exist 5 times (`Ui`, `Agent.Cost`, `LocalTools`, `McpHub`, `Context.ReadIf`) | DRY |
| **Python-isms** | `PyFloat`, `Dumps` (json.dumps clone), `Truthy`, `Repr`, `kw`, `u`, `Cfg` | Reads like a translation. Some of these matter for output parity and must stay, but named for what they do. (The `// loop.py:` breadcrumbs are *not* a smell: they stay, in a stricter format, see §5) |
| **Exception naming and use** | `ToolError` (no `Exception` suffix), `InvalidOperationException` used for "missing API key" and "bad config" | Callers catch a broad type to mean a narrow thing (`Cli.cs:226`) |
| **No tests** | — | Every step below changes structure. Without a safety net, this refactor is guesswork |

These parts are good and should be kept: the raw-JSON stream accumulator (`Api.Snapshot`), the credential guard in `Api.For`,
`Paths.RealPath`, the write-before-memory rule, and the `// shortcut:` comments.

## 2. Parity contract: what must not change

A refactor changes structure, not behavior. These must stay **byte-identical**, and Phase 0 pins them with golden tests:

1. **What the model sees:** the system prompt, every tool definition (name, description, schema, key order), the harness
   prompts (`GOAL_*`, `REVIEW`, `CUT_OFF`, `SUMMARIZE`, `Dream`), tool result texts and error strings, and the approval statement.
2. **What gets replayed:** the session JSONL event shapes and the field order of messages. Content blocks are re-sent
   exactly as the API returned them (thinking binding depends on it). Existing `~/.mini-net` sessions must still resume.
3. **What scripts read:** CLI flags, exit codes and the headless `--json` line (`smoke-test/run.py` drives the binary).
4. **Security behavior:** what needs approval, what counts as secret or protected, credential isolation between
   providers, and Ctrl-C semantics.

Human-only text (REPL banners, `/sessions` layout) may be cleaned up but stays as it is by default.

### Invariants (from AGENTS.md; after this refactor the types enforce them, not convention)

- The transcript is append-only. Only `Transcript` can add messages; it exposes `IReadOnlyList<Message>`.
- Write to the session first, then to memory. `Transcript.Append*` is the only code path that does either.
- Every `tool_use` gets a `tool_result` in the next user message. `ToolExecutor` returns exactly one result per call, and the type makes that hard to break.
- The system prompt and tools are frozen per session (`SessionManifest`, an immutable record).
- Model blocks are appended as returned. `ContentBlock` wraps the raw JSON it came from, and serializing writes that raw JSON back.

## 3. Decisions (confirmed by the user before Phase 0)

| # | Decision | Default | Why / alternative |
|---|---|---|---|
| D1 | **Dependencies** | Add `Microsoft.Extensions.DependencyInjection`, `.Options`, `.Http`. Tests: `xunit.v3` + `Microsoft.NET.Test.Sdk` | **No Generic Host:** `ConsoleLifetime` takes over Ctrl-C and SIGTERM, which conflicts with the hand-tuned cancellation. No MediatR, AutoMapper, Polly or Serilog |
| D2 | **Projects** | `Mini.Core` (class library, no `Console`), `Mini.Cli` (exe, `AssemblyName=mini`), `Mini.Tests` | The compiler then enforces that the core never touches the terminal (DIP) |
| D3 | **API data model** | **DTOs and records, not raw JSON parsing** (confirmed). Session events, requests, usage, tool arguments, goal state and every block the harness creates are typed records serialized by STJ in today's key order. The single exception: a content block *the API returned* keeps its original JSON in a private field and writes that back on serialization; callers only see its typed properties (`ToolUseBlock.Name`, `ThinkingBlock.Signature`, …). Unknown block types become `UnknownBlock` | Thinking binding needs model-returned blocks replayed exactly, including fields this code has no model for. `[JsonExtensionData]` alone would reorder those fields, so the raw copy stays, but hidden inside the record. No code outside `Conversation/` and `Models/StreamAccumulator` reads a `JsonObject` |
| D4 | **Size** | Expect ~5,500–6,500 lines plus tests (from ~4,000) | AGENTS.md's size budget is for the Python `src/mini/`. This port's goal is now idiomatic C#, not minimal size |
| D5 | **Python references** | **Keep them in the code**, in one fixed, greppable format (§5), on every refactored file, type and member that ports Python logic. `docs/PYTHON-MAP.md` is generated from those tags | When `src/mini/` changes, a grep lists the C# code to update. Keep every *why* comment as an XML doc or `//` too |
| D6 | **Filesystem** | No `IFileSystem` abstraction; tests use temp directories | YAGNI. The path-security code must be tested against real symlinks anyway |
| D7 | **Branching** | `dotnet-migration` is the trunk for all .NET work; **nothing goes to `main`**. Each phase (or parallel lane) gets a branch or worktree off the trunk, named `dotnet-p<N>-<topic>`, merged back green | Each phase is releasable on its own |

## 4. Target architecture

```
dotnet/
  Mini.slnx
  Directory.Build.props        net10.0, LangVersion latest, Nullable, TreatWarningsAsErrors,
                               AnalysisLevel latest-recommended, EnforceCodeStyleInBuild
  Directory.Packages.props     central package versions
  .editorconfig                naming (_camelCase fields), var, expression bodies, file-scoped namespaces
  docs/ARCHITECTURE.md
  docs/PYTHON-MAP.md           generated from the `// Python:` tags; starts with the Python sync commit
  docs/python-not-ported.txt   Python names with no C# counterpart, each with a reason
  scripts/python-map.sh        regenerates PYTHON-MAP.md
  src/Mini.Core/
    Configuration/  MiniOptions, MiniPaths, ModelCatalog, ModelSettings, PriceSchedule
    Conversation/   Message, Role, ContentBlock (+ Text/Thinking/ToolUse/ToolResult/ServerToolUse/Image/Compaction/
                    Fallback/Unknown), StopReason, Usage, ModelResponse, Transcript, PendingNotes
    Sessions/       ISessionStore, JsonlSessionStore, SessionEvent (+ kinds), SessionManifest, SessionSnapshot,
                    SessionCatalog (create/find/list), SessionSummary
    Models/         IModelClient, AnthropicModelClient, RetryingModelClient, StreamAccumulator, IStreamObserver,
                    MessageRequest, MessageRequestBuilder, IProviderProfile (AnthropicProfile, CompatibleProfile),
                    TransientErrorClassifier, AnthropicClientFactory (owns the header-freeze guard)
    Tools/          ITool, ToolDefinition, ToolCall, ToolOutput, ToolException, ToolRegistry, ToolExecutor, ToolArgs
      Files/        ReadFileTool, WriteFileTool, EditFileTool, FindTool, FileReadTracker, ImageBudget,
                    RegularFileReader (+ NativeMethods), DirectoryWalker
      Shell/        BashTool, IShellRunner, ShellRunner, ShellLocator, IShellLauncher (BashLauncher, CmdLauncher),
                    ShellResult, BackgroundJob, JobRegistry
      Agentic/      TaskTool, AskUserTool, GoalDoneTool
      Mcp/          McpHub, McpTool
      WebSearchTool (server tool: definition only)
    Security/       PathResolver (RealPath), WorkspacePolicy, SecretClassifier, CommandPathInspector
    Approval/       IApprovalService, ApprovalPipeline, IApprovalStep (+ steps), ApprovalRequest, ApprovalDecision,
                    PermissionMode, CommandSafetyRules, IApprovalJudge, JevJudge, ApprovalStatement
    Agents/         Agent (facade), IAgentFactory, AgentFactory, TurnRunner, SubagentRunner
      Compaction/   ICompactionStrategy, ServerCompaction, SummaryCompaction, Compactor, CompactionPolicy
      Costs/        PriceCalculator, UsageTracker
      Goals/        Goal, GoalStatus, GoalRunner, GoalVerifier, GoalLimits
    Prompts/        SystemPromptBuilder, PromptTemplate, HarnessPrompts, SkillCatalog, Frontmatter
    Interaction/    IAgentOutput, IUserInteraction        (ports the CLI implements)
    Text/           CodePointExtensions, LineSplitter, ShellWords, Printable, FuzzyLineMatcher, LooseEditor,
                    PythonJson (Dumps/Repr: kept for model-visible parity), ImageSniffer, PngValidator
  src/Mini.Cli/
    Program.cs                 composition root (ServiceCollection), returns exit code
    Hosting/ServiceRegistration.cs, ConsoleCancellation.cs, ProcessSignals.cs, WindowsConsole.cs
    Commands/  CliOptions, RootCommandFactory, HeadlessRunner, Repl, ISlashCommand + one class per /command,
               SlashCommandRouter, ContextReport
    Console/   ConsoleAgentOutput (was Ui), ConsoleUserInteraction (approval prompt, ask_user), AnsiColors
  tests/Mini.Tests/
    Fakes/ FakeModelClient (port of tests/fake.py), ScriptedUserInteraction, RecordingOutput, FakeTimeProvider
    Golden/ system-prompt.txt, tools.json, approval-*.txt   (generated once from the current code)
    …one test class per production area
```

### Key abstractions (sketches; the final signatures may differ)

```csharp
// Tools: Strategy + Registry. Each tool owns its definition, so ToolRegistry has no switch.
public interface ITool
{
    ToolDefinition Definition { get; }          // frozen into the SessionManifest
    Task<ToolOutput> InvokeAsync(ToolCall call, ToolContext context, CancellationToken ct);
}

// Approval: Chain of Responsibility. Each step returns a decision or passes. No mutable Reason field.
public interface IApprovalStep { ValueTask<ApprovalDecision?> TryDecideAsync(ApprovalRequest request, CancellationToken ct); }
public sealed record ApprovalDecision(bool Allowed, string Reason);
// Order: AutoApprove → SafeCommand → McpAllowList → TooLongToReview → Judge → Headless → AskUser

// Model transport: Decorator for mid-stream retries; Observer instead of passing Ui + Action onStart.
public interface IModelClient
{
    Task<ModelResponse> StreamAsync(MessageRequest request, IStreamObserver observer, CancellationToken ct);
    Task<ModelResponse> CreateAsync(MessageRequest request, CancellationToken ct);
    Task<long> CountTokensAsync(MessageRequest request, CancellationToken ct);
}
public interface IStreamObserver { void OnStarted(); void OnText(string delta); void OnThinking(string delta); }

// Provider differences: Strategy, not `if (Other is not null)` scattered through the code.
public interface IProviderProfile
{
    void Shape(MessageRequestBuilder request);   // thinking, betas, fallbacks
    ICompactionStrategy Compaction { get; }      // server-side vs ask-for-a-summary
    bool CanCountTokens { get; }
}

// Open set of API values: smart enum, not a C# enum (the API adds stop reasons).
public readonly record struct StopReason(string Value)
{
    public static readonly StopReason ToolUse = new("tool_use"), MaxTokens = new("max_tokens"), PauseTurn = new("pause_turn"),
        Refusal = new("refusal"), Interrupted = new("interrupted"), Error = new("error");
}
```

`Agent` becomes a thin facade over `Transcript`, `TurnRunner`, `Compactor`, `GoalRunner` and `UsageTracker`. The CLI
talks only to it. `AgentFactory` replaces the constructor's two hidden code paths (resume, new session) with
`CreateNewAsync` and `ResumeAsync`.

## 5. Python source references (applies to every phase)

The C# port follows the Python, which keeps changing (1.0.5 arrived after the port). Every C# file this refactor
creates or changes must say which Python code it ports. Then a Python change maps to the C# code to update with one grep.
Today's breadcrumbs (`// loop.py: turn`) have no fixed format, and the refactor moves code away from them, so they
are rewritten into the format below while each file is refactored. Never just delete one.

### Format

One prefix, `// Python: `, so `grep -rn "// Python: "` finds every reference:

```
// Python: <file>.py <Name>[, <Name>…] [(<note>)]
```

- `<file>` is relative to `src/mini/`: `loop.py`, `tools.py`, `session.py`, `context.py`, `ext.py`, `cli.py`, `models.json`.
- `<Name>` is the Python qualified name, exactly as it is in the source: `Agent.turn`, `LocalTools._path`, `still_running`, `SCHEMAS["bash"]`, `GOAL_RULES`.
- `(<note>)` is for partial ports: `(the retry loop only)`, `(Windows branch)`, `(the strings; the logic is in TurnRunner)`.
- Several Python files: one line each.

### Where the tags go

1. **File header**, the first lines after the `using`s. List every Python name the file ports, so the file can be
   checked as a whole:
   ```csharp
   // Python: tools.py LocalTools._inside, LocalTools._secret, LocalTools._private, LocalTools._ask
   // Python: tools.py SECRET, SECRET_DIRS, PROTECTED
   namespace Mini.Core.Security;
   ```
2. **Each type and member** that ports Python logic, directly above the declaration and below its XML doc:
   ```csharp
   /// <summary>Free to write: inside the working directory, and not a file something else runs later.</summary>
   // Python: tools.py LocalTools._inside
   public bool IsInside(string path) { … }
   ```
   When one Python function is split across classes (`Agent.turn` into `TurnRunner`, `Transcript` and `RetryingModelClient`),
   **every** piece carries the tag, each with a note saying which part it holds.
3. **C#-only code** (DI registration, interfaces, adapters, `PythonJson`) has no tag on its members. A type with no Python
   counterpart says so once: `// Python: none (C#-only: <why>)`. Then the coverage check can tell "not ported" from "not needed".
4. **Strings that must stay byte-identical** (prompts, tool descriptions, error texts) carry the tag of the Python constant they copy, even inside a larger method.

### Keeping track of Python changes

- `docs/PYTHON-MAP.md` starts with a **sync line**: `In sync with src/mini at <commit> (<version>)`; today that is `e4da828` (1.0.5).
  Below it is a table from each Python name to its C# locations, generated from the tags (`scripts/python-map.sh`, a grep and a sort, ~20 lines).
- **When Python changes:**
  1. Run `git diff <sync-commit>..HEAD -- src/mini/` to see the changed functions.
  2. Run `grep -rn "// Python: tools.py LocalTools.read" dotnet/src` to find the C# code that ports each one.
  3. Port the change, then move the sync line forward.
- **`PythonReferenceTests`** in `Mini.Tests` (added in P1, runs on every `dotnet test`):
  - every `// Python:` tag must name a `def`, `class` or top-level constant that exists in `src/mini/` (simple regexes). A Python rename or removal fails the test, so a stale reference can't go unnoticed;
  - every top-level function, method and constant in `src/mini/*.py` must be tagged somewhere in C#, or be listed in `docs/python-not-ported.txt` with a reason (for example `group_running`: accepted gap P5).
- Every phase's review checks the tags: each new or changed file has a header, and each split function has its tag on every piece.

### Conventions decided in P1

The spec left these open; `PythonReferenceTests` encodes them.

- **What counts as a Python name** (the coverage test requires each to be tagged or listed): every top-level `def`/`async def`/`class`,
  every method of a top-level class, every top-level `UPPER_CASE` constant (also each name of a tuple assignment such as
  `DIM, YEL, CYA, RED, OFF = …`), and every `UPPER_CASE` class attribute.
- **Class-level constants are qualified:** `LocalTools.SECRET`, `Approver.SHELL`, not `SECRET`. (The sketch above shows `SECRET, SECRET_DIRS, PROTECTED`.)
- **Dunders:** `__init__` is a name (`Agent.__init__`, the constructor); other dunder methods are skipped (there are none today).
- **Not names:** functions nested in functions (`reap`'s `wait`, `main`'s `new`), the `if __name__ == "__main__"` guard, `__main__.py`
  and `__init__.py`. They are covered by their enclosing function's tag; `Program.cs` carries `// Python: none (C#-only: …)`.
- **Lower-case class attributes** (`Agent.max_tokens`) may be tagged but need not be.
- **Subscripts** (`SCHEMAS["bash"]`, `EXTRA_SCHEMAS["task"]`) are checked by their base name; list one per entry that copies text.
- **`models.json`** names are dotted key paths, checked against the file: `main`, `models`, `models.claude-*.compact_at`. Model names have no dots.
- **Position:** the tag goes below the XML doc and above the declaration (attributes included). A C#-only tag goes there too.
  A file with a single type whose tag sits directly under the `namespace` line needs no separate header. `GlobalUsings.cs` has no tag.
- **Tag lines are whole lines.** A trailing `// Python: …` after code is not a tag. In prose comments never write `Python:`
  (with a colon); write "Python's …" or "in Python, …". `TagsAreWellFormed` rejects a line that looks like a tag but is not one,
  and the old `// loop.py: turn` style.
- **Notes carry the gap** of a partial port, for example `// Python: tools.py stop_running (SIGKILL only; no SIGTERM first)`. A name
  that is only partly ported stays tagged; `docs/python-not-ported.txt` is only for names with no C# code at all (today `group_running`).
- **Method names are qualified:** the old breadcrumbs wrote `loop.py: turn`, but `turn`, `call`, `judge`, `run_subagent`… are `Agent` methods.

## 6. Phases

Each phase ends with: `dotnet build` (0 warnings), `dotnet test` green (including `PythonReferenceTests`), golden tests
unchanged, every touched file tagged per §5, and a short note in this file. Phases 3 and 4 can run in parallel; everything else runs in order.

```
P0 Safety net ─► P1 Hygiene ─► P2 Composition root ─┬─► P3 Domain + sessions ─┬─► P5 Agent ─► P6 CLI ─► P7 Hardening
                                                    └─► P4 Tools + approval ──┘
```

### Phase 0: Safety net (blocking, ~25% of the effort)

The goal is to make a behavior change visible before any structure changes.

1. Create `Mini.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` (as suggestions at first) and `tests/Mini.Tests`.
2. Add **one seam only**: extract `IModelClient` from `Api` (signatures as they are today) so a fake can stand in for it. Change nothing else.
3. Port `tests/fake.py` to `FakeModelClient`: scripted responses and stream events, with a record of every request.
4. **Golden-master tests**, generated from the current build and committed:
   system prompt (fixed clock, cwd and model), the tools array of a new session (all on/off variants: sub, headless,
   `MINI_NO_TASK`, `MINI_NO_WEB`, other provider), the approval statement for each mode, and `ContextReport` output.
5. **Characterization tests**: port the cases of `tests/test_mini.py` that apply (~140 of 180). Start with
   these groups, which are also the riskiest parts of later phases:
   - transcript: `assert_append_only`, tool_result pairing, interrupt backfill, pause_turn, refusal, max_tokens cut-offs, repair on resume;
   - security: `WorkspacePolicy` (protected files, dot-folders, home-as-cwd, Windows aliases), secrets, `PreApproved`, symlinks through `RealPath`;
   - stream accumulation: recorded SSE event sequences into `Snapshot` (thinking signature, citations, compaction delta, fallback, cut stream);
   - sessions: cut lines, compact segments, goal restore, listing;
   - shell (POSIX): exit codes, timeout keep, background jobs, nothing left running at exit;
   - judge and approval prompt rendering (with a fake console).
6. Write down the baseline: the result of one live `-p` run and one smoke-test run (`--harness mini-deepseek`, ~2¢).

**Exit:** at least 140 green tests; golden files committed; coverage report for `Agent*`, `LocalTools`, `Approver`, `Shell`, `Session`.

#### Phase 0 notes (2026-11-17)

- **Tooling:** xUnit v3 runs on Microsoft.Testing.Platform (`dotnet/global.json`), because the .NET 10+ SDK no longer runs
  it through VSTest. Run the tests with `cd dotnet && dotnet test --solution Mini.slnx`. Coverage comes from
  `Microsoft.Testing.Extensions.CodeCoverage`, not coverlet. They run serially, because statics, the environment and
  `Console` are process-wide; Phase 2 removes the statics.
- **Baseline before the refactor** (trunk `d0a477a`, Release build, DeepSeek; no Anthropic key was available):
  - `mini --model deepseek-flash --json -p "list the files here"` gave `stop: end_turn` and exit 0.
  - `smoke-test/run.py --harness mini-deepseek` passed 5 of 5 (bugfix, feature, research, goal, compaction) for $0.0095 in total.
    The rows were not added to `results.jsonl`.
- **The smoke-test recipe in MIGRATION-PLAN step 5 tests the Python harness, not the C# one.** `uv run` puts the venv's
  `bin/` first on `PATH`, and `run.py` prepends that same directory again for every task. So `mini` resolves to the Python
  entry point, whatever `PATH` says. To run the C# binary, use a Python that has no `mini` installed:
  ```
  dotnet publish dotnet/src/Mini.Cli -c Release -o /tmp/mini-net
  uv venv /tmp/smoke-venv && uv pip install --python /tmp/smoke-venv/bin/python pytest
  PATH=/tmp/mini-net:$PATH MINI_NET_HOME=/tmp/mini-smoke-home /tmp/smoke-venv/bin/python smoke-test/run.py --harness mini-deepseek
  ```
  Check that the sessions were written under `MINI_NET_HOME`.
- **Parity finding (not fixed in P0):** the headless `--json` line escapes `` ` ``, `<`, `>`, `&`, `'` and `+` as `\uXXXX`
  (System.Text.Json's default encoder). Python's `json.dumps` escapes only non-ASCII characters. Both are valid JSON, so
  `run.py` reads either, but the output is not byte-identical. Fix it in P6 with a `PythonJson` encoder, and pin it with a test.
- **Result:** 355 tests, of which 353 pass and 2 are skipped (both accepted gaps). A run takes about 55 s; most of that is
  real shell timeouts and the 2/4/8 s retry waits, which TimeProvider removes in P2/P5. 40 golden files are in `Golden/`.
  - 172 of the 182 Python tests are ported, 7 of them only in part. 10 can't be ported yet; `tests/Mini.Tests/NOT-PORTED.md`
    gives each one's reason and the phase that unblocks it.
  - Another 73 C#-only tests cover what Python never needed: `Api.Snapshot` stream accumulation, `RealPath` with real
    symlinks, the `PreApproved` command table, the session JSONL round trip, and replay that stays byte-identical across a resume.
- **Coverage** (`dotnet test --solution Mini.slnx -- --coverage --coverage-output-format cobertura`): 84% of lines and 74%
  of branches overall.
  - Above 90% of lines: `Agent.cs`, `Agent.Turn.cs`, `Agent.Compact.cs`, `Agent.Sub.cs`, `Session.cs`, `Tools/Approver.cs`,
    `Tools/LocalTools.cs` and `Tools/Shell.cs`.
  - Lower: `Agent.Goal.cs` 88%, `Api.cs` 74% (the real SDK transport), `Agent.Cost.cs` 68% (the judge's HTTP; P4) and
    `Cli.cs` 44% (P6).
- **Production changes in P0:**
  - the `IModelClient` seam;
  - `JudgeState` extracted verbatim from `JudgeAsync`, so the tests can see what the judge sees;
  - a handful of `private` → `internal` changes for tests. Behavior is unchanged: the golden files were generated from this code.
- **Test seams that would help later:** a fake Anthropic API over HTTP (`Providers/ModelConfigTests` already runs the real
  binary against an `HttpListener`). It would unblock the CLI and process-level tests listed in NOT-PORTED in P6.

### Phase 1: Hygiene (mechanical; parallel lanes by folder)

There are no design changes in this phase; the tests stay green throughout.

- Move to the `src/Mini.Core` + `src/Mini.Cli` split (D2) and put **one type per file** in folder namespaces (`Mini.Core.Tools.Shell`, …).
- Public fields become properties; use `required`/`init` where set once and `private set` where the type owns the state.
- Rename for meaning: `ToolError` → `ToolException`, `Cfg` → `_model`, `kw` → `body`, `Approver.Approval(ap)` → `approver.Describe()`, `Ui` → `ConsoleAgentOutput` (the type moves in P6; only rename now).
- Merge the 5 code-point helpers into `CodePointExtensions` (`TakeCodePoints`, `TakeLastCodePoints`, `CodePointLength`).
- Turn all `new Regex(...)` into `[GeneratedRegex]` and drop the `SYSLIB1045` pragmas.
- Replace tuples with records: `ShellResult`, `JudgeVerdict`, `ToolRunResult`, `GoalLimits`, `LooseEditResult`.
- Add enums: `PermissionMode`, `GoalStatus` (`JsonStringEnumConverter` with snake_case lower, so the session JSON doesn't change), `JudgeChoice`. Add smart enums: `StopReason`, `SessionEventKind`, `BlockType`.
- Add domain exceptions: `MissingCredentialsException`, `ModelConfigurationException` (both derive from `MiniException`), so `Cli` catches what it means.
- Rewrite every existing breadcrumb (`// loop.py: turn`, `// tools.py: _path`, `/// <summary>tools.py: approval()</summary>`) into the §5 format, add the file headers, and add `PythonReferenceTests`, `docs/python-not-ported.txt` (seeded from MIGRATION-PLAN's accepted gaps) and the first generated `docs/PYTHON-MAP.md` with its sync line. From here on, code that moves takes its tags along.
- Rename `PyFloat`/`Dumps`/`Repr` into `PythonJson` with a doc comment that says *why* they exist (model-visible parity).
- Delete dead code: `Job.Stopping` is written but never read (audit, minor differences).

**Exit:** the diff is moves, renames and type changes only; no golden file changes; `PythonReferenceTests` passes, so every Python function is tagged in C# or listed as not ported.

#### Phase 1 notes (2026-11-17)

- **Layout:**
  - `src/Mini.Core` has the folders `Agents`, `Approval`, `Common`, `Configuration`, `Interaction`, `Models`, `Prompts`, `Sessions` and `Tools` (with `Tools/Mcp`).
  - `src/Mini.Cli` holds `Cli.cs` and `Program.cs`.
  - Namespace clashes are avoided: `Common`, not `Text`, because of the `Text` class. `Shell` stays in `Mini.Core.Tools`. Cli keeps `namespace Mini`, because a `Mini.Cli` namespace would shadow the `Cli` class; P6 renames it.
  - `ConsoleAgentOutput` (was `Ui`) is still in Core, because `Agent` writes to it; it moves to Cli in P6.
- **Types added:**
  - `MiniException` (with `ToolException`, `MissingCredentialsException`, `ModelConfigurationException`);
  - `PermissionMode`, `GoalStatus`, `JudgeChoice`/`JudgeVerdict`, `GoalLimits`, `ToolRunResult`, `GoalVerification`, `ShellResult`, `LooseEditResult`;
  - the smart enums `StopReason`, `SessionEventKind` and `BlockType`;
  - `CodePointExtensions` and `PythonJson`.
- **Left for later phases:**
  - `TurnAsync` still returns `string` (StopReason is used inside `Agent`; P5).
  - `Agent.Messages` is still a `List<JsonObject>` with a private setter (P3 `Transcript`).
  - `Approver.Reason` and `Approver.Judge` stay settable (P4).
  - `Cli` catches `MiniException or InvalidOperationException`, because other paths still throw the latter.
- **One deliberate tightening:** a session file whose goal status is not one of `active`, `paused`, `done` or `cleared` no longer loads. The harness never writes other values.
- **Two code-point helper families:** `TakeCodePoints` slices and keeps lone surrogates. `TakeScalars` replaces them with U+FFFD; the judge, goal notes, verify tails and session titles use it. The old copies differed in exactly this way.
- **Python tags:**
  - 259 port tags and 18 `none (C#-only)` tags;
  - `docs/PYTHON-MAP.md` has 181 rows;
  - `docs/python-not-ported.txt` lists only `group_running`;
  - about 140 old breadcrumbs were corrected to qualified names.
- **Verification:**
  - 382 tests: 380 pass, and the same 2 are skipped;
  - the golden files are unchanged;
  - a live DeepSeek `-p` run gave exit 0;
  - the smoke test `--harness mini-deepseek` passed 5 of 5 for $0.0097. The rows were not kept.

### Phase 2: Composition root and removal of static state

- Add a `MiniOptions` record bound once from the environment (`MINI_NET_HOME`, `MINI_MODEL`, `MINI_COMPACT_AT`, `MINI_NO_TASK`,
  `MINI_NO_WEB`, `MINI_BASH`, `OPENROUTER_API_KEY` present, `ANTHROPIC_*` present) and registered with `IOptions<MiniOptions>`.
  No other code reads `Environment` (an analyzer banned-API list enforces this: `BannedSymbols.txt`).
- `Paths` becomes an injected `MiniPaths`, and `RealPath` becomes `PathResolver` (static and pure is fine; it has no state).
- `Config`'s static constructor becomes `ModelCatalog`, built by `ModelCatalogLoader` and registered as a singleton. `Rate()` becomes `PriceSchedule` using `TimeProvider`.
- Add `TimeProvider.System` everywhere time is read (session ids, job log names, system prompt timestamp, off-peak pricing).
- `Shell.Running/Jobs` (static) become a `JobRegistry` singleton. `StopRunning` becomes `JobRegistry.StopAll()`, which `Program` wires to `ProcessExit`.
- `Api`'s static header-freeze state moves into an `AnthropicClientFactory` singleton. **Keep the security logic word for word**, with its tests from P0.
- `Agent.MaxTokens` (static) becomes an `AgentOptions.MaxTokens` option.
- `Program.cs` builds a `ServiceCollection`, with registrations grouped in `AddMiniCore()` / `AddMiniCli()` extension methods.

**Exit:** `grep -r "static .* = new List\|static bool\|Environment.GetEnvironmentVariable" src/` finds nothing outside the options binder; tests use fake options and `FakeTimeProvider`.

#### Phase 2 notes (2026-11-17)

- **New types:**
  - `MiniOptions` and `MiniOptionsBinder`, which has `FromEnvironment`, a pure `Bind`, and `ReadLive`;
  - `MiniPaths`, and `PathResolver` (in `Mini.Core.Security`);
  - `ModelCatalog`, `ModelCatalogLoader` and `PriceSchedule(TimeProvider)`;
  - `JobRegistry`, the `Shell` instance and `AnthropicClientFactory`;
  - `AgentOptions` (`MaxTokens`);
  - `AgentRuntime`, which carries what `Agent` needs as one parameter object until P5 splits `Agent`;
  - `AddMiniCore()` and `AddMiniCli()`. `AddMiniCli()` is empty until P6.
- **Environment:** `MiniOptionsBinder.cs` is the only file that reads it. `BannedSymbols.txt` (RS0030 as error, not applied to tests) bans environment and wall-clock reads everywhere else.
  - `AnthropicClientFactory` reads the live environment through the `ReadLive` delegate. The SDK reads the real process environment, and the provider key names come from models.json.
  - The factory keeps its header-freeze flags static on purpose: the SDK's header table is process-wide, so a second factory must not reset the guard. The guard logic and its messages are unchanged.
- **Read once at startup:** `MINI_COMPACT_AT`, `MINI_NO_TASK`, `MINI_NO_WEB` and `OPENROUTER_API_KEY` are read once, where Python reads them at each call or each `Agent`. Changing them while mini runs has no effect.
- **Failure messages:** a bad `models.json` or `MINI_COMPACT_AT` now prints its message and exits 1. Before, it crashed with a `TypeInitializationException`.
- **`JobRegistry`:** one lock guards the job lists, which fixes a possible "collection modified" error when a bash call finished during exit. Ctrl-C reaches the registry through a static `activeJobs` in `Cli`, because the console handler is static (P6).
- **Tests:** the three clock-blocked pricing tests are ported at fixed instants. Each test gets its own runtime, `FakeTimeProvider` and `JobRegistry`, so the cross-test clean-up of statics is gone.
- **Verification:**
  - 420 tests: 418 pass, and the same 2 are skipped;
  - the golden files are unchanged;
  - a live DeepSeek `-p` run gave exit 0;
  - the smoke test `--harness mini-deepseek` passed 5 of 5 for $0.0091.
- **Open finding (not caused by P2):** in one smoke run, the bugfix task's request after two parallel edits failed locally with `ArgumentException`. The turn stopped with `error`, but the task's checks still passed. Four reruns did not reproduce it. P2 did not change the request path; the `Api.cs` diff only removes code. The exception message went to stderr and was not kept. Follow up in P5 or P7, starting by logging the full exception from `Agent.CallAsync`.

### Phase 3: Conversation domain and sessions (parallel with P4)

- **Records (D3):** `Message(Role, IReadOnlyList<ContentBlock>)`, with no public JSON. `ContentBlock.From(JsonObject)` is a factory that switches on
  `type` and returns `TextBlock`, `ThinkingBlock`, `ToolUseBlock(Id, Name, Input)`, `ToolResultBlock`, `ServerToolUseBlock`, `ImageBlock`,
  `CompactionBlock`, `FallbackBlock` or `UnknownBlock`. Blocks the harness creates go through factory methods (`ToolResultBlock.Create(id, output, isError)`)
  that write their JSON in today's key order (a golden test checks this).
- `ModelResponse` (stop reason, content, `Usage` with its iterations, `InputTransformations`, model) wraps the accumulator's JSON the same way.
- **`Transcript`** owns `Add`, `AddUser`, `Placeholder`, `Repair`, `CommitPaused`, `ReplaceWithSummary` and `FinalText`, plus `PendingNotes`
  (deduplicated system notes). It is the only writer to `ISessionStore` for messages, so the write-before-memory rule is in one place.
  `Clean()` becomes `FallbackSanitizer`, the one documented exception to "as returned".
- **Sessions:** split `Session` into:
  - `ISessionStore` / `JsonlSessionStore` (append and read events, crash-cut repair);
  - `SessionEvent`, a typed hierarchy (`MetaEvent`, `MessageEvent`, `MessagesEvent`, `CompactEvent`, `GoalEvent`, `UsageEvent`, `AnswerEvent`, `TransformEvent`) whose serializer gives today's JSON;
  - `SessionReplayer`, which builds a `SessionSnapshot` (the old `Load`);
  - `SessionCatalog` (create, find, list → `SessionSummary`).
- **Goal** becomes an aggregate with intention-revealing methods (`Activate()`, `Pause(reason)`, `Complete(evidence)`, `Clear()`, `RecordTurn(cost)`)
  and a `GoalStatus` enum. Today's direct `(g.Status, g.Note) = …` assignments disappear.

**Exit:** no `Json.Str(x["type"]) ==` remains outside `Conversation/` and `Models/StreamAccumulator`; the session golden tests and resume of a P0-recorded session pass.

#### Phase 3 notes (2026-11-17)

- **Conversation:** `Message` keeps a plain-string content as a string (`Text`, with an empty `Content`), because the harness's own notes and the user's prompts are written that way. Every `ContentBlock` keeps its JSON in a private field and copies it on the way in and out, so a block the API returned replays byte for byte; the factories write the harness's blocks in today's key order (`ToolResultBlock.Create` and `NotRun` differ, as `run_tools` and `repair` always did). `ToJson()`/`From()` are `internal`: the only JSON that leaves `Conversation/` is what the request body and the session lines need.
- **`IModelClient` still returns `JsonObject`:** `Agent` wraps it in `ModelResponse.From`. Typing the transport is part of P5's `MessageRequest`/`RetryingModelClient` work, and the fakes script raw replies. The `StreamAccumulator` still takes a `ConsoleAgentOutput` (P5 swaps it for `IStreamObserver`).
- **`Transcript`** is the only writer of message events; `Agent.Messages` and `Agent.PendingNotes` are views of it. A compaction swaps the list (`ReplaceWithSummary`), the one place a message leaves the transcript; the append-only tests cover everything else.
- **Sessions:** the typed events write the old lines byte for byte (checked against a session recorded before the change: `Fixtures/session-p34.jsonl`, and a literal-lines test). A line of an unknown kind is read as `UnknownEvent` and ignored. A known kind whose parts cannot be read becomes an `UnreadableEvent`: the session listing skips it (as it always did), and `SessionReplayer` raises the original exception (`KeyNotFoundException`, `InvalidOperationException` or `JsonException`), as the old load did. `Role` is an open struct, so a role the code has no name for loads and replays as written.
- **Goal** is a class with a constructor (`new Goal(objective, status, turns, …)`) for new and restored goals, and private setters; the methods are the only way to change status, note, turns and spend. `Complete` keeps the first 500 code points of the evidence (the call site used to cut it).
- **Left for P5:** `Agent` still has `JsonObject Tools`, and the request bodies are `JsonObject`s built in `Agent.Turn.cs`/`Agent.Compact.cs`; `Usage` events carry the judge's verdict as a `JsonObject`.

### Phase 4: Tools, security and approval (parallel with P3)

- Add `ITool`, `ToolRegistry` and `ToolExecutor`. `ToolExecutor` is the old `RunToolsAsync`; it guarantees one result per call and the interrupt backfill.
- Split `LocalTools` (500 lines) by responsibility:
  - `WorkspacePolicy` (`IsInside`, `IsPrivate`, `NeedsWriteApproval`), `SecretClassifier` and `CommandPathInspector` (`Words`, `NamesPrivate`). Pure and security-critical, so they are the first to be unit-tested;
  - `FileReadTracker` (the old `Seen`), `ImageBudget` (the old `Held`), `RegularFileReader` (the P/Invoke code, in a `NativeMethods` class), `DirectoryWalker`;
  - `ReadFileTool`, `WriteFileTool`, `EditFileTool`, `FindTool`, `BashTool`, each with its own `ToolDefinition`.
    The text in `Schemas.cs` moves into the tools; the golden test proves it is byte-identical.
- **Typed arguments:** each tool declares `sealed record XArgs` and a small `ToolArgs.Bind<T>` that keeps the exact `Bad arguments: …` messages (`Only`/`Req`/`Opt`/`Int`/`Bool` today).
- **Shell:** `IShellRunner`/`ShellRunner` (the old `RunShellAsync`), `IShellLauncher` strategies (`BashLauncher` with its prelude and traps, `CmdLauncher` for Windows without Git Bash), `ShellLocator` (the old `FindBash`) and `BackgroundJob` (the old `Job`, now with `Pid`/`HasExited`, not a public `Process`).
- **Agentic tools:** `TaskTool` (depends on `ISubagentRunner`), `AskUserTool` (depends on `IUserInteraction`) and `GoalDoneTool` (writes the claim to a `GoalClaimSlot` that `GoalRunner` reads, replacing the `GoalClaim` field). `McpTool` is one adapter per MCP tool, so the hub is no longer a special case in dispatch.
- **Approval:** `ApprovalPipeline` with the steps from §4. `IApprovalJudge`/`JevJudge` uses a typed `HttpClient` from `AddHttpClient<JevJudge>` with the 20 s timeout, and reads user messages and answers through an `IJudgeContext` port on the session store, **not the Agent**. That breaks the Approver ↔ Agent cycle and removes the late `Judge =` assignment. The terminal prompt moves behind `IUserInteraction.ConfirmAsync(ApprovalPrompt)`.

**Exit:** `LocalTools`, `Schemas` and `Approver` are deleted; the tool, security and approval tests from P0 pass unchanged.

#### Phase 4 notes (2026-11-17)

- **New folders and types:**
  - `Security/`: `WorkspacePolicy`, `SecretClassifier`, `CommandPathInspector`, `PathParts`, `PathResolver`.
  - `Tools/`: `ITool`, `ToolRegistry`, `ToolExecutor`, `ToolArgs`, `ToolCall`, `ToolDefinition`, `ToolOutput`, `WebSearchTool`.
    - `Tools/Files/`: `ReadFileTool`, `WriteFileTool`, `EditFileTool`, `FindTool`, `FileReadTracker`, `ImageBudget`, `RegularFileReader`, `DirectoryWalker`.
    - `Tools/Shell/`: `BashTool`, `IShellRunner`/`ShellRunner`, `ShellLocator`, `IShellLauncher` (`BashLauncher`, `CmdLauncher`), `BackgroundJob`, `JobRegistry`.
    - `Tools/Agentic/`: `TaskTool`, `AskUserTool`, `GoalDoneTool` with `GoalClaimSlot`, and the ports `ISubagentRunner` and `IAnswerLog`, which `Agent` implements (`Agent.Ports.cs`).
    - `Tools/Mcp/`: `McpTool`, one per MCP tool.
  - `Approval/`: `ApprovalPipeline` with its steps (`AutoApprove`, `SafeCommand`, `McpAllowList`, `TooLongToReview`, `Judge`, `Headless`, `AskUser`), `ApprovalSettings`, `ApprovalRequest`, `ApprovalDecision`, `ApprovalStatement`, `IApprovalJudge`/`JevJudge`, `IJudgeContext`.
  - `Interaction/`: `IUserInteraction` and `ConsoleUserInteraction`.
  - `Common/`: `Text` is split into `LineSplitter`, `Printable`, `OutputLimit`, `PythonString`, `LooseEditor`, `FuzzyLineMatcher`, `ImageSniffer` and `PngValidator`.
  - `LocalTools`, `Schemas`, `Approver` and the `Shell` class are deleted. `Agent.RunToolsAsync` and `Agent.DispatchAsync` are gone; `ToolExecutor` is the old `run_tools` and `dispatch`.
- **Design points:**
  - `IUserInteraction.ConfirmAsync` returns an `ApprovalDecision`, so `Approver.Reason` is gone.
  - There is no `ToolContext`: each tool takes its dependencies in its constructor.
  - An `ApprovalTrail` is made per request, to remember what an earlier step said.
  - The pipeline's step order equals the old `Approver.Ok` order. The golden approval statements are unchanged.
  - `JevJudge` takes a typed `HttpClient` (`AddHttpClient<JevJudge>`, 20 s timeout) and reads the user's words through `IJudgeContext`, so `Approver` and `Agent` no longer depend on each other.
- **One message moved:** a call to a tool the session does not have now says "Tool nope is not available in this session." The old "Unknown tool" text was unreachable.
- **Python behavior that was kept (parity findings, not bugs of the port):**
  - A subagent can call tools that are not in its list: Python's `dispatch` does not check either.
  - `ask_user`'s headless reply ("Nobody can answer") is reachable only when the tool is not advertised.
  - `read` and `find` approvals check secrets but not `IsPrivate`.
  - Each top-level agent has its own approval pipeline; subagents share their lead's.
  - `JevJudge`'s single `HttpClient` lives as long as the singleton.
- **Tests:** 198 new unit tests, including the `JevJudge` HTTP side through a fake handler. After the merge with P3 there are 646 tests (644 pass, the same 2 skipped).
- **Reconciliation with P3:**
  - `IJudgeContext` is implemented by `SessionJudgeContext` over the `ISessionStore`: typed user messages (not the harness's) and `AnswerEvent`s. The goal's objective and the cost callback are the agent's state, passed in as delegates. A subagent shares its lead's pipeline, so it shares its lead's context.
  - Tool calls and results are typed at the `Agent` boundary: `ToolCall.From(ToolUseBlock)` in, a `ToolRunResult` of `ToolResultBlock`s out, `ToolOutput.OfBlocks` takes `ContentBlock`s.
  - The `Agent` still reads raw JSON in four places: the request bodies (`RequestArgs`, the two compaction requests), `Tools`/`HasTool` (a `JsonArray` of definitions), the catalog's `compact_at` in `CompactAt()`, and the `RunToolAsync` test seam. P5 removes them with `MessageRequest`, a typed tool list and `ModelConfig.CompactAt`.

### Phase 5: Breaking up the Agent

- `IModelClient` gets a real implementation, `AnthropicModelClient` (was `Api`; the `Snapshot` type becomes `StreamAccumulator`). `RetryingModelClient` is a
  decorator that holds the 2/4/8 s mid-stream retry, which today sits in `Agent.CallAsync` with a local `started` flag. `TransientErrorClassifier` is the old `IsTransient`.
- `MessageRequestBuilder` (Builder) plus the `IProviderProfile` strategies replace `RequestArgs` and every `Other is not null` branch.
- `TurnRunner` is the `TurnAsync` loop. It depends on `Transcript`, `IModelClient`, `ToolExecutor`, `UsageTracker`, `CompactionPolicy` and `IAgentOutput`.
- `UsageTracker` + `PriceCalculator` replace `Track`/`Price`.
- `Compactor` + `ICompactionStrategy` (`ServerCompaction`, `SummaryCompaction`) + `CompactionPolicy` (threshold and the 20% back-off) replace `Agent.Compact.cs`.
- `SubagentRunner` creates children through `IAgentFactory` (sub mode) and shares the parent's `IModelClient`.
- `GoalRunner` (the run loop and its limits) and `GoalVerifier` (verify command + reviewer subagent) replace `Agent.Goal.cs`.
- Prompt texts move to `HarnessPrompts` and `SystemPromptBuilder`. `Fill` becomes `PromptTemplate.Render(values)`.
- `Agent` is left as a facade: `TurnAsync`, `CompactAsync`, `SetGoalAsync`/`ResumeGoalAsync`/`ClearGoal`, `ChangeEffort`, `ChangePermissions`, and read-only state for the CLI.

**Exit:** no `partial class Agent`; no class over ~250 lines; `Agent` has no `JsonObject` in its public API; the full test suite plus one live `-p` run on Claude and one on DeepSeek pass.

#### Phase 5 notes (2026-11-18)

- **Stage A, run as two parallel lanes:**
  - **Transport:** `AnthropicModelClient` (was `Api`), `RetryingModelClient` (2/4/8 s retries on the injected `TimeProvider`), `TransientErrorClassifier`, `OwnFailure`, and `IStreamObserver` with `ConsoleStreamObserver`. `IStreamObserver` also has `OnRetrying`, because a shared client can't hold a per-call warning sink.
  - **Requests:** `MessageRequest`/`MessageRequestBuilder` (today's key order); `IProviderProfile` (`AnthropicProfile` holds the betas; `CompatibleProfile`) with `CompactionMode` and `CanCountTokens`.
  - **Prompts:** `HarnessPrompts`, `PromptTemplate` (same substitution rules as `Fill`, pinned by tests), `SystemPromptBuilder` with `SystemPromptRequest`, `SkillCatalog`, `Frontmatter`, `PromptFile`.
  - The suite went from 55 s to about 26 s, because the retry tests no longer wait in real time.
- **Stage B:**
  - `TurnRunner`;
  - `Costs/` (`UsageTracker`, `PriceCalculator`);
  - `Compaction/` (`Compactor`, `CompactionPolicy`, `ServerCompaction`, `SummaryCompaction`);
  - `Goals/` (`GoalRunner`, `GoalVerifier`, `GoalSlot`, plus the moved goal types);
  - `SubagentRunner`, `IAgentFactory`/`AgentFactory` (`CreateNew`, `Resume`, `CanResume`), `SessionManifest`, `EffortSetting`.
  - `AgentRuntime` is gone: the factory takes the services directly.
  - `Agent` is a 124-line facade. `TurnAsync` still returns `string` and `Tools` is typed. The only `JsonObject` left is the internal `RunToolAsync` test seam.
- **Left for later phases:**
  - `ConsoleAgentOutput` is still concrete, with no `IAgentOutput`; P6 adds the interface when the output moves to Cli.
  - `Cli.cs` (463 lines) is the only class over 250 lines (P6).
  - `ModelCatalog.Models` is still a raw `JsonObject`.
  - The goal verify command's 600 s timeout is still a literal, so its NOT-PORTED test stays blocked.
  - A summary request sends `system` as a bare string, unlike every other request; that is kept as is (`SystemPrompt.AsPlainString`).
- **Kept in mind:**
  - The model client is now created before the tools. The credentials check still runs before anything is written, and the tool constructors can't throw.
  - A resume replays the session twice: once in `CanResume`, once in `Resume`. It happens only at startup.
- **Verification:**
  - 681 tests: 679 pass, and the same 2 are skipped;
  - the golden files are unchanged since P4;
  - DeepSeek `-p` and `-c -p` (resume) live runs gave exit 0;
  - the smoke test `--harness mini-deepseek` passed 5 of 5 for $0.0200. The cost is twice P3/P4's only because this run fell in DeepSeek's peak hours (09:57 UTC) and that one did not: token counts per task are about the same.
  - Claude live is still untested: there is no Anthropic key.

### Phase 6: CLI and presentation

- Add a `CliOptions` record bound by `RootCommandFactory` (System.CommandLine). Flags, help texts, exit codes and `@x` handling stay the same.
- `HeadlessRunner` (the `--json` line and exit codes) and `Repl`.
- **Command pattern:** `ISlashCommand { string Name; string Help; Task ExecuteAsync(ReplContext, string args, CancellationToken) }`,
  one class per command (`/new`, `/resume`, `/sessions`, `/goal`, `/permissions`, `/effort`, `/context`, `/compact`, `/dream`, `/help`).
  `SlashCommandRouter` builds `/help` from the registered commands.
- `ConsoleCancellation` holds the Ctrl-C state machine (today the statics `running`, `atPrompt`, `activeHub`), with explicit states: `Idle`, `AtPrompt`, `Running(cts)`.
- `ConsoleAgentOutput : IAgentOutput` (was `Ui`; child output for subagents is an indenting decorator), `ConsoleUserInteraction : IUserInteraction`, `AnsiColors`.
- `ContextReport` becomes a service that asks the profile whether it can count tokens.

**Exit:** `Cli.cs` is deleted; the CLI tests (flag parsing, headless JSON, slash commands with a scripted console) pass; a pty Ctrl-C check gives the same results as audit item #2.

#### Phase 6 notes (2026-11-18)

- **Layout:**
  - Core has an `IAgentOutput` port, with `OutputStreamObserver` adapting it to `IStreamObserver`. Core no longer references `System.Console`, and a test checks the assembly for it.
  - `Mini.Cli.Terminal`: `ConsoleAgentOutput`, `ConsoleUserInteraction`, `TerminalSize`, `AnsiColors`. The name is not `Console`, which would clash with `System.Console`.
  - `Mini.Cli.Hosting`: `MiniApp.RunAsync(argv, services)`, `ConsoleCancellation`, `ProcessSignals`, `WindowsConsole`, `AddMiniCli()`.
  - `Mini.Cli.Commands`: `CliOptions`, `RootCommandFactory`, `MiniCommand`, `AgentLauncher`, `HeadlessRunner`/`HeadlessResult`, `Repl`/`ReplContext`, `SlashCommandRouter`, ten `ISlashCommand` classes, `ContextReport`, `GoalPrinter`.
  - `Cli.cs` is deleted.
- **Ctrl-C:** `ConsoleCancellation` has the states `Idle`, `AtPrompt` and `Running(cts)`. Audit #2 was already fixed: at the prompt, Ctrl-C prints a newline, closes the hub, stops the jobs and exits 0. A pty check confirmed it.
  - Ctrl-C during a headless turn exits 1 with stop `interrupted`, as Python does. Exit 130 only happens when the cancel escapes the turn.
- **Intended parity fix:** the `--json` line is now byte-identical to Python's `json.dumps`: `", "`/`": "` separators, only non-ASCII escaped (lowercase hex), and the cost as a float repr (`0.0`). `HeadlessJsonTests` pins the bytes.
- **Small changes:**
  - `AgentSetup.Interaction` is required, so Core no longer creates a console.
  - A subagent shares its lead's `IUserInteraction`. This only shows if a subagent calls `ask_user`, which isn't in its tool list.
  - `Child()` indentation stays inside `ConsoleAgentOutput`, because the indent goes before the already-colored line.
  - `ConsoleCancellation.cs` holds three small types. That is the only exception to one type per file.
- **Verification:**
  - 725 tests: 723 pass, and the same 2 are skipped;
  - the golden files are unchanged;
  - `--help` is identical to P5's;
  - a scripted 15-command REPL session gives the same output as P5;
  - DeepSeek `-p` and `-c` live runs gave exit 0;
  - the smoke test `--harness mini-deepseek` passed 5 of 5 for $0.0109 (peak hours).

### Phase 7: Hardening and verification

- `.editorconfig` severities become errors; turn on `AnalysisMode=Recommended` and `GenerateDocumentationFile` for `Mini.Core`.
- Write `docs/ARCHITECTURE.md` (one page: layers, ports, invariants). Regenerate `docs/PYTHON-MAP.md`, check that every Python function split in P3–P5 has its tag on every piece, and confirm the sync line still names the Python commit the code matches.
- Update `MIGRATION-PLAN.md`'s conventions section to point here.
- Run the full verification:
  - `dotnet test`;
  - live `-p` checks on Claude and DeepSeek;
  - the interactive checklist from MIGRATION-PLAN Phase 3, step 4;
  - `smoke-test/run.py --harness mini-deepseek`, then `--harness mini` (~$0.60). Revert the `results.jsonl` rows or label them;
  - a Windows pass if a machine is available.
- Close open audit item #1 (`x-api-key` vs Bearer) with the live check.

#### Phase 7 notes (2026-11-18)

- **Analyzers:**
  - Every `.editorconfig` style rule and the `_camelCase` naming rule are now `error`, with `EnforceCodeStyleInBuild`. About 90 violations were fixed mechanically with `dotnet format style`.
  - `AnalysisMode=Recommended` is on for `Mini.Core` and `Mini.Cli`. Tests are left out: turning it on there gives 36 hits, mostly CA1305.
  - `GenerateDocumentationFile` is on for `Mini.Core`. CS1591 (missing XML doc) is suppressed there with a comment, because it has 251 hits.
  - CA1716, CA1720 and CA1822 are disabled with a one-line reason each. There are no `#pragma` or `[SuppressMessage]` sites, except the RS0030 one already in `MiniOptionsBinder`.
- **Tests:**
  - Ported:
    - the headless goal exit code and the refused headless run, through `MiniApp.RunAsync` with a fake client factory;
    - the judge default;
    - the goal verify timeout, using the new `AgentOptions.GoalVerifyTimeoutSeconds` (default 600);
    - the judge's HTTP-to-verdict mapping, model and cost;
    - the REPL signal cases of the timeout test, against a real `mini` process. A `FakeApiServer` (an `HttpListener`) stands in for the API there.
  - `NOT-PORTED.md` has 6 left: the Windows-only tests and the accepted process-group and nohup gaps.
  - 745 tests: 743 pass, and the same 2 are skipped.
- **The P2 `ArgumentException`:**
  - Not reproduced. Six regression tests run two parallel `edit`s through the fake client and through the real SDK transport, on the Claude and the DeepSeek profiles.
  - `JsonNode` reuse is ruled out as the cause, because it throws `InvalidOperationException`.
  - The transcript shows only the type name, as Python does. The message itself goes to stderr.
  - A `MINI_DEBUG` option was proposed and declined, because Python has no such option.
- **Audit #1:** a local capture server shows `ANTHROPIC_API_KEY` is sent as `Authorization: Bearer`, which is how the Anthropic 13.4.0 SDK documents it. Acceptance by api.anthropic.com is unconfirmed until a key is available (`MIGRATION-PLAN.md`).
- **Docs:**
  - `docs/ARCHITECTURE.md` is new.
  - The conventions section in `MIGRATION-PLAN.md` is marked as superseded.
  - `ConsoleCancellation.cs` is split, so every file now holds one type.
  - `PYTHON-MAP.md` is regenerated; the sync stays at Python `e4da828`.
- **Verification:**
  - Tests and golden files: green and unchanged.
  - Live runs: DeepSeek `--json -p` and `-c -p` gave exit 0.
  - Smoke test: `--harness mini-deepseek` passed 5 of 5 for $0.0107.
  - Interactive pty checklist (MIGRATION-PLAN Phase 3, step 4), all on DeepSeek, all as expected:
    - read, edit and bash;
    - Ctrl-C during `sleep 30` gives "Interrupted by the user." and returns to the prompt;
    - `/sessions` and `/context`;
    - a subagent `task`;
    - an MCP `shout` against `tests/echo_server.py`;
    - `/compact`;
    - `/goal`, with the reviewer subagent, reaching `done`;
    - `/new` and `/resume`;
    - Ctrl-C at the prompt exits 0 and leaves no MCP server running.
  - **Not run:**
    - Claude live and `--harness mini`: there is no Anthropic key;
    - Windows: there is no machine.

## 7. Risks

| Risk | Mitigation |
|---|---|
| Byte-level drift in replayed blocks breaks thinking binding (silent until the API drops thinking) | D3 raw-backed views; golden tests on request bodies built by `FakeModelClient`; the `transform` event warning stays |
| A security regression while moving path policy and approval code | P0 security tests come first; P4 moves the code before changing it; a reviewer pass on `Security/` and `Approval/` diffs |
| Ctrl-C semantics change under DI | No Generic Host (D1); `ConsoleCancellation` gets explicit state tests; pty check in P6 |
| Credential leak after `Api` is split | The `AnthropicClientFactory` guard moves as is, with its P0 tests; review the P5 diff |
| The refactor stalls halfway, with two styles in the code | Each phase is merged green and releasable; P1 and P2 already give most of the readability gain |
| Over-engineering (interfaces nobody swaps) | An interface only where a test fake or a second implementation exists: `IModelClient`, `ISessionStore`, `IShellRunner`, `IApprovalJudge`, `IUserInteraction`, `IAgentOutput`, `IProviderProfile`, `ICompactionStrategy`, `ITool`, `ISlashCommand`. Everything else stays a concrete sealed class |

## 8. Non-goals

- New features, fixes for the accepted gaps (setsid, cancellable `ReadLine`), or changes to the Python code.
- Changing the session file format, the prompts or the CLI surface.
- Generic Host, a logging framework, a mediator or a resilience library.
