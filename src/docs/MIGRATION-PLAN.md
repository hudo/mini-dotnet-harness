# mini → C# / .NET 10 migration plan

A port of `src/mini/` (Python, ~1,400 lines) to a .NET 10 console app in `dotnet/`. It is a learning
prototype, so readability matters more than cleverness, and there are **no tests**. Each step is checked by
`dotnet build` and a short manual run.

## Feasibility: go

Every piece has a .NET equivalent. Nothing is missing.

| Python dependency / feature | .NET replacement | Status |
|---|---|---|
| `anthropic` SDK ≥ 2.3 (API version `2026-10-21`) | **`Anthropic` 13.4.0** (official, Stainless-generated; also sends `2026-10-21`) | ✅ Has `client.Beta.Messages.CreateStreaming`, `BetaMessageContentAggregator`, `CountTokens`, `Models.List`, typed exceptions, `ClientOptions { BaseUrl, ApiKey, MaxRetries }`. Every model keeps its raw JSON (`RawData`), and params can be built from raw JSON (`FromRawUnchecked`). |
| `mcp` SDK (stdio client) | **`ModelContextProtocol.Core` 2.3.1** (official) | ✅ `StdioClientTransport`, `McpClient.CreateAsync`, `ListToolsAsync`, `CallToolAsync`, stderr callback. Natively async, so the Python background event-loop thread goes away. |
| `argparse` | **`System.CommandLine` 2.0.14** (stable since .NET 10) | ✅ |
| `json` (dicts) | `System.Text.Json.Nodes` (`JsonObject`, `JsonArray`, `JsonNode`) | ✅ Insertion order is kept, as with Python dicts. |
| `subprocess`, `signal`, `atexit` | `System.Diagnostics.Process`, `PosixSignalRegistration`, `AppDomain.ProcessExit` | ⚠️ No `start_new_session`. You chose **simple mode**; the accepted gaps are listed below. |
| `urllib.request` (judge) | `HttpClient` | ✅ |
| `hashlib`, `secrets`, `base64` | `SHA256`, `RandomNumberGenerator.GetHexString`, `Convert.ToBase64String` | ✅ |
| `fnmatch` | `System.IO.Enumeration.FileSystemName.MatchesSimpleExpression` | ✅ (`*` and `?` only; see gaps) |
| `shlex.split` / `shlex.join` | **none**: hand-written `ShellWords` (~40 lines) | ✅ small |
| `difflib.get_close_matches` | **none**: hand-written Levenshtein ratio (~20 lines) | ✅ small |
| `os.walk`, `Path.resolve()` | own `Walk()` and `RealPath()` helpers (~15 lines each) | ✅ `RealPath` must resolve every symlink: it is a security check |
| `readline` | `Console.ReadLine` | ✅ (no history on Unix; accepted) |
| Windows `ctypes` console mode | `[LibraryImport]` of `GetConsoleMode`/`SetConsoleMode` | ✅ |

Package choice: the official `Anthropic` package (≥ 10). Not `Anthropic.SDK` (tghamm, unofficial) and not `tryAGI.Anthropic`.

## Decisions (from your answers)

1. **Platforms:** macOS, Linux and Windows. Port every `WINDOWS` branch.
2. **Processes, simple mode:** `Process` + `Kill(entireProcessTree: true)`. No P/Invoke `setsid`.
3. **Data model:** API data (messages, content blocks, tools, usage) stays as `JsonObject`/`JsonNode`, as the Python dicts do. The SDK is only the transport: raw JSON goes in through `FromRawUnchecked`, and the response comes out as JSON. Data the harness owns (the goal, model config, session rows) gets small typed records.
4. **Dependencies:** `Anthropic`, `ModelContextProtocol.Core`, `System.CommandLine`. Nothing else.
5. **Location:** `dotnet/` in this repo. It reads the same `src/mini/models.json` (linked into the project, not copied).
6. **Separate home:** `~/.mini-net`, overridable with **`MINI_NET_HOME`** (not `MINI_HOME`, so a shell exported for Python can't mix the two). Skills and `mcp.json` in the home move there too. The project-local paths (`./skills`, `.claude/skills`, `.agents/skills`, `./mcp.json`, `./.mcp.json`) stay the same. The session format may change freely, but it stays the same JSONL event shape because that is the simplest choice.

## Accepted gaps (simple process mode and stdlib differences)

Agents must write each one as a `// shortcut: <limit>, <when to upgrade>` comment where it applies.

| Gap | Effect | Cheap mitigation (do it) |
|---|---|---|
| Children share mini's process group (POSIX) | Terminal Ctrl-C sends SIGINT to bash commands, background jobs and MCP servers | Shell wrapper starts with `trap '' INT;`, so the command and its children ignore SIGINT. The harness kills foreground commands itself on cancel. MCP servers are started through `/bin/sh -c 'trap "" INT; exec "$0" "$@"' <command> <args…>`. |
| No `killpg` | `&` jobs that outlive their shell (orphans) are not killed on timeout, on Ctrl-C or at exit | none: document it |
| Reaper can't kill a dead shell's group | Python's "shell was killed, so stop its group too" branch is dropped | none |
| `SIGHUP` handler can't tell if it is ignored (nohup) | Always installs it | none |
| `Console.ReadLine` can't be interrupted | Ctrl-C at an approval or `ask_user` prompt takes effect after Enter | Check the token right after `ReadLine` returns |
| `fnmatch` `[abc]` classes | Not supported in `find`'s `name` | none |
| `str.splitlines()` exotic separators (`\f`, `\v`, `\x1c`, `\u2028`…) | Only `\r\n`, `\n`, `\r` split lines | none |
| Job stop wording (audit #6, decided 2026-11-17) | The model is told "pgrep -P PID, then kill PID" instead of Python's `kill -- -PID`, because simple mode has no per-job groups. Only an exact `kill PID` of a listed job skips approval. The "ended, but what it started still runs" note (P4) is not given | none; upgrade with setsid together with P4 |
| `difflib` ratio | Levenshtein ratio (cutoff 0.6) replaces Ratcliff/Obershelp; only affects the "Closest:" hint | none |
| Process exit codes | .NET already reports a signal death as 128+N, so Python's `128 - code` conversion is dropped | none |

## Invariants every agent must keep (copied from AGENTS.md)

1. **Append-only transcript.** Never edit, reorder or remove anything in `Agent.Messages` or the session file. Changes during a session go in as `{"role":"system"}` messages via `PendingSystem`.
2. **Every `tool_use` gets a `tool_result`** in the very next user message, all in one message, with `is_error: true` for failures, denials and interrupts (cancellation included).
3. **Append the model's blocks as returned** (null fields stripped, the equivalent of `to_dict(exclude_none=True)`), never just the text. The one exception is `Clean()`.
4. **System prompt and tools are frozen** per session and replayed from the `meta` event on resume.
5. Write to the session file **before** updating the in-memory list (`Add()`).

## Global conventions for all agents

> **Superseded (2026-11-18).** These were the conventions of the first 1:1 port. The refactor replaced them: the current rules
> (DI, one type per file, `// Python:` tags, DTOs over raw JSON) are in `REFACTOR-PLAN.md` §3 and §5, and the structure is in
> `docs/ARCHITECTURE.md`. The list below is kept as history.

- `net10.0`, C# 14, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. File-scoped `namespace Mini;`.
- **Mirror the Python 1:1.** One Python function becomes one C# method with the PascalCase name (`run_shell` → `RunShellAsync`, `loose_edit` → `LooseEdit`). Keep the order. Keep every Python *why* comment and docstring as a `//` comment or `/// <summary>`. Add a `// tools.py: run_shell` breadcrumb above each method, so you can read the two side by side.
- **Prompt and tool-description strings stay byte-identical**, as C# raw string literals (`"""…"""`). Exceptions: `"Python regex"` → `".NET regex"` in the `find` schema, and the home folder name.
- **Async all the way** (`Task`, `ct` as the `CancellationToken` name). Python `KeyboardInterrupt` becomes `OperationCanceledException`. `time.sleep` becomes `await Task.Delay(…, ct)`.
- Python's ad-hoc attributes on `Popen` (`.log`, `.listed`, `.stopping`) become a `Job` class.
- No interfaces, DI, logging frameworks, `IOptions`, or "for later" code. Static classes for module-level functions, concrete classes for state.
- Formatting: `{n:N0}` with `CultureInfo.InvariantCulture` for `{n:,}`, `{x:P1}` for `{x:.1%}`, `{x:F2}` for `{x:.2f}`. Set `CultureInfo.DefaultThreadCurrentCulture = InvariantCulture` in `Main`.
- Write files as UTF-8 **without a BOM** (`new UTF8Encoding(false)`). Read text as `Encoding.UTF8.GetString(File.ReadAllBytes(p))`, which keeps a BOM as `\uFEFF` like Python does, where `File.ReadAllText` would silently strip it.
- Each agent owns only the files listed for its track. Need a change in someone else's file? Leave a `// TODO(track X):` note and report it. Don't edit it.
- **Done for a track** means `dotnet build dotnet/Mini` passes with no warnings, the manual check listed for that track was run, and the output was pasted into the hand-off note.
- **Manual checks** use a throwaway .NET 10 file-based script outside the repo, for example `/tmp/check-a.cs`, starting with `#:project <repo>/dotnet/Mini/Mini.csproj` and run with `dotnet run /tmp/check-a.cs`. No test project and no self-check flags in `mini`.

## Target layout

```
dotnet/
  MIGRATION-PLAN.md
  Mini/
    Mini.csproj            AssemblyName=mini; links ../../src/mini/models.json (CopyToOutputDirectory)
    Program.cs             top-level: return await Cli.MainAsync(args);
    Paths.cs               Home (~/.mini-net), ProjectDir(cwd)            ← session.py HOME/project_dir
    Config.cs              models.json load + merge, ModelConfig, Rate()  ← loop.py lines 27-52
    Json.cs                JsonNode helpers: Obj(), N(), StripNulls(), ToElements(), Str()
    Text.cs                Control, Printable, Truncate, ShellWords, Closest, Lines helpers
    ImageInfo.cs           image_info
    Session.cs             session.py
    Context.cs             context.py
    Skills.cs              ext.py: frontmatter, find_skills, safe_name
    McpHub.cs              ext.py: MCPHub
    Ui.cs                  cli.py: UI class + colors + Windows VT enable
    Api.cs                 the ONLY file that touches the Anthropic SDK
    Tools/
      Shell.cs             find_bash, spawn, stop, reap, run_shell, start_job, still_running, stop_running, Job
      Approver.cs          Approver, approval(), GATED
      LocalTools.cs        LocalTools (read, write, edit, find, bash, path checks), ToolError
      Schemas.cs           schema(), SCHEMAS, WEB_SEARCH
    Agent/
      Agent.cs             ctor/resume, transcript (Add, AddUser, Placeholder, Repair, FinalText), constants
      Agent.Turn.cs        RequestArgs, CallAsync, TurnAsync, RunToolsAsync, DispatchAsync, Clean, ExtraSchemas
      Agent.Cost.cs        Track, Price, JudgeAsync
      Agent.Compact.cs     CompactAsync, ServerSummaryAsync, SummarizeAsync, CompactAt, ShouldCompact, MaybeCompactAsync
      Agent.Sub.cs         RunSubagentAsync
      Agent.Goal.cs        Goal record, GoalRules, SaveGoal, SetGoalAsync, ClearGoal, RunGoalAsync, VerifyAsync
    Cli.cs                 MainAsync (System.CommandLine), Repl, Command, ContextReport, PrintGoal, HELP, DREAM
```

`Agent` is a `partial class` split across six files, so several agents can work on it at once without
conflicts. Estimated size: about 2,300–2,700 lines of C#.

## Phase graph

```
Phase 0  Scaffold + contracts (1 agent)                      ── gate
            │
Phase 1  ┌─ A Text+Image ─┐ ┌─ B Shell ─┐ ┌─ C Session+Context+Skills ─┐
(7 in    │  D McpHub      │ │  E Api    │ │  F Ui                       │  G Approver+Schemas
parallel)└────────────────┘ └───────────┘ └─────────────────────────────┘
            │  (signatures exist from Phase 0, so nobody waits on code to compile)
Phase 2  ┌─ H LocalTools ─┐ ┌─ I Agent core ─┐ ┌─ J Agent extras ─┐ ┌─ K Cli ─┐
(4 in    │ needs A,B,G    │ │ needs E,C,G,F  │ │ needs E,C,B      │ │ needs all│
parallel)└────────────────┘ └────────────────┘ └──────────────────┘ └─────────┘
            │
Phase 3  Integration + live checks (1 agent)                 ── done
```

**Hard dependency** means the track needs the other track's *working code* for its manual check.
**Soft dependency** means it only needs the *signatures*, which Phase 0 creates. Every Phase 2 track
depends softly on Phase 1, so Phase 2 may **start** as soon as Phase 0 is done, and only its manual check
waits for its hard dependencies. If you have enough agents, run Phases 1 and 2 together and sequence only
the final checks.

---

## Phase 0: Scaffold and contracts (1 agent, blocking)

**Goal:** a compiling skeleton in which every type and method in the layout exists with its final
signature. Bodies are `throw new NotImplementedException("track X")`, except the small foundations below,
which are implemented fully here.

Steps:
1. `dotnet new console -n Mini -o dotnet/Mini -f net10.0`. Set the csproj properties from the conventions. Add the packages `Anthropic` 13.4.0, `ModelContextProtocol.Core` 2.3.1 and `System.CommandLine` 2.0.14. Link `../../src/mini/models.json` as `<Content … CopyToOutputDirectory="PreserveNewest" Link="models.json" />`.
2. Implement **`Paths.cs`** fully. `Home` = `MINI_NET_HOME` env or `~/.mini-net`. `ProjectDir(cwd)` = `Home/sessions/<first 12 hex of SHA256(RealPath(cwd))>`, created on use. `RealPath(path)` resolves **every** symlinked component (walk the parts and call `ResolveLinkTarget(true)` on each), then `Path.GetFullPath`. Python's `resolve()` does this, and `LocalTools` relies on it for its security checks.
3. Implement **`Json.cs`** fully, all small and static:
   - `JsonObject Obj(params (string, JsonNode?)[] kv)` for readable literals.
   - `long N(JsonNode? x)` (null → 0), the Python `n()`.
   - `string? Str(JsonNode? x)`.
   - `JsonNode StripNulls(JsonNode)`: drops properties whose value is null, recursively (`exclude_none=True`).
   - `Dictionary<string, JsonElement> ToElements(JsonObject)`: converts a body for the SDK's `FromRawUnchecked`.
   - `JsonSerializerOptions Relaxed`: `UnsafeRelaxedJsonEscaping`, used for session lines.
4. Implement **`Config.cs`** fully (`loop.py` lines 27–52). Load the linked `models.json` from `AppContext.BaseDirectory`, merge `Home/models.json` into it per model and per field (as JSON objects, like the Python dict merge), and expose `Main`, `SubModel`, `CheapModel`, `CompactAt` (env `MINI_COMPACT_AT`), `Models` (the merged `JsonObject`), `Other` (entries with `base_url`), and `ModelConfig For(string model)`. `ModelConfig` is a record deserialized with `JsonNamingPolicy.SnakeCaseLower`: `Window, CompactAt?, Search, Fallbacks, Efforts, Prices, PricesOver100k?, MaxOutput?, BaseUrl?, Key?, PeakUtc?, OffPeak`. Also `double Rate(ModelConfig)`: Python's `tm_wday < 5` (Mon–Fri) becomes `DayOfWeek is not (Saturday or Sunday)` in UTC.
5. Stub every other file with these **contracts** (copy them exactly):

```csharp
// Text.cs
public static partial class Text {
  public static readonly Regex Control;                    // [\x00-\x08\x0b-\x1f\x7f-\x9f]
  public static string Printable(string text, bool lines = false);
  public static string Truncate(string text);              // MAX_OUT = 30_000
  public const int MaxOut = 30_000;
  public static List<string> SplitLinesKeepEnds(string text);
  public static List<string> SplitLines(string text);     // like str.splitlines()
  public static int Count(string text, string sub);
  public static string ReplaceFirst(string text, string old, string @new);
  public static (string? Text, int Hits) LooseEdit(string text, string old, string @new);
  public static string Closest(string text, string old);
}
public static class ShellWords {
  public static List<string> Split(string command);        // throws FormatException on bad quoting
  public static string Join(IEnumerable<string> words);    // shlex.join
}
// ImageInfo.cs
public static class ImageInfo { public static (string Kind, int W, int H)? Read(byte[] data); }

// Tools/Shell.cs
public sealed class Job { public required Process Proc; public required string Log; public bool Listed = true; public bool Stopping; }
public static class Shell {
  public static readonly string? Bash; public static bool Windows => OperatingSystem.IsWindows();
  public static readonly List<Job> Running, Jobs;
  public static Task<(int? Code, string Output)> RunShellAsync(string command, string cwd, int timeoutSec, bool keep, CancellationToken ct);
  public static string StartJob(string command, string cwd);
  public static string StillRunning(IReadOnlyList<Job> earlier);
  public static void Stop(Job job);
  public static void StopRunning();                         // registered on ProcessExit
}

// Tools/Approver.cs
public sealed class Approver(bool auto = false, bool interactive = true, IEnumerable<string>? allow = null, bool useJudge = false) {
  public bool Auto, Interactive, UseJudge; public string Reason = "";
  public Func<string, string, Task<(string Verdict, string Why)>>? Judge;
  public List<List<string>> Allow;
  public string Mode { get; }
  public bool PreApproved(string command, bool isPrivate = false);
  public Task<bool> OkAsync(string kind, string detail, bool isPrivate = false, bool secret = false);
  public static string Approval(Approver ap);               // tools.py approval()
}

// Tools/Schemas.cs
public static class Schemas {
  public static JsonObject Schema(JsonObject props, string[] required);
  public static readonly Dictionary<string, JsonObject> Local;   // SCHEMAS
  public static readonly JsonObject WebSearch;
}

// Tools/LocalTools.cs
public sealed class ToolError(string message) : Exception(message);
public sealed class LocalTools(string cwd, Approver approver) {
  public long Held;                                          // base64 image bytes in transcript
  public Task<JsonNode> CallAsync(string name, JsonObject args, CancellationToken ct); // dispatch by name; bad args → ToolError("Bad arguments: …")
  public Task<JsonNode> ReadAsync(string path, int offset = 1, int limit = 2000);     // string JsonValue, or JsonArray (text + image)
  public Task<string> WriteAsync(string path, string content);
  public Task<string> EditAsync(string path, string old, string @new, bool replaceAll = false);
  public Task<string> FindAsync(string name = "*", string contains = "", string path = ".");
  public Task<string> BashAsync(string command, int timeout = 120, bool background = false, CancellationToken ct = default);
}

// Session.cs
public sealed record SessionState(JsonObject? Meta, List<JsonObject> Messages, JsonObject? Goal, double Cost, long Context);
public sealed record SessionRow(string Id, int Turns, double Cost, string Title, DateTime Updated);
public sealed class Session(string path) {
  public string Path; public string Id;
  public static Session Create(string cwd, string? name = null);
  public static Session? Find(string cwd, string? prefix = null);
  public void Append(string kind, JsonObject data);
  public List<JsonObject> Events();
  public SessionState Load();
  public static List<SessionRow> List(string cwd, int limit = 15);
}

// Context.cs
public static class Context { public static string BuildSystem(string cwd, IReadOnlyList<Skill> skills, IReadOnlyList<string> mcpErrors, bool sub, string model, bool @delegate, string approval); }

// Skills.cs
public sealed record Skill(string Name, string Description, string Path);
public static class Skills { public static Dictionary<string,string> Frontmatter(string text); public static List<Skill> Find(string cwd); public static string SafeName(string s); }

// McpHub.cs
public sealed class McpHub : IAsyncDisposable {
  public List<JsonObject> Schemas; public List<string> Errors; public bool Has(string apiName);
  public static Task<McpHub> StartAsync(string cwd);
  public Task<(string Text, bool IsError)> CallAsync(string apiName, JsonObject args, CancellationToken ct);
}

// Ui.cs
public sealed class Ui(string indent = "", bool quiet = false, bool silent = false) {
  public void Text(string s); public void Thinking(string s); public void EndMessage();
  public void ToolCall(string name, JsonObject args); public void ToolResult(string output, bool err);
  public void Note(string s); public void Warn(string s); public Ui Child();
  public static void EnableWindowsVt();
}

// Api.cs
public sealed class Api {
  public static Api For(string model, ModelConfig? other);   // null other = Anthropic
  public Task<JsonObject> StreamAsync(JsonObject body, Ui ui, CancellationToken ct); // final message as JSON, nulls stripped
  public Task<JsonObject> CreateAsync(JsonObject body, CancellationToken ct);
  public Task<long> CountTokensAsync(JsonObject body);
  public static Task<List<string>> ListModelsAsync();
  public static bool IsTransient(Exception e);
}

// Agent/*.cs  (partial class Agent)
public sealed record Goal { Objective, Status, Turns, MaxTurns, Verify, Budget, Spent, Note }   // snake_case JSON
public sealed partial class Agent {
  public Agent(string cwd, Session session, Ui ui, Approver approver, McpHub? hub = null, IReadOnlyList<Skill>? skills = null,
               string? model = null, string effort = "high", bool sub = false, Api? api = null);
  public string Model, System, Effort; public JsonArray Tools; public List<JsonObject> Messages; public List<string> PendingSystem;
  public double Cost; public long LastContext; public int Window; public IReadOnlyList<string> Efforts; public Goal? Goal;
  public ModelConfig? Other; public Session Session; public Approver Approver; public Api Api;
  public (string? Verify, int MaxTurns, double? Budget) GoalDefaults;
  public static int MaxTokens;                                       // 0 = model's max_output
  public Task<string> TurnAsync(JsonNode content, bool harness = false, CancellationToken ct = default);
  public string FinalText();
  public Task<bool> CompactAsync(CancellationToken ct = default);
  public Task MaybeCompactAsync(CancellationToken ct = default);
  public int CompactAt();
  public Task<(string Verdict, string Why)> JudgeAsync(string kind, string detail);
  public Task<Goal> SetGoalAsync(string objective, string? verify, int maxTurns, double? budget, CancellationToken ct = default);
  public Task<Goal> RunGoalAsync(string first = "Resume work toward the goal.", CancellationToken ct = default);
  public void ClearGoal();
}

// Cli.cs
public static class Cli { public static Task<int> MainAsync(string[] args); }
```

**Done when:** `dotnet build` passes, and `dotnet run --project dotnet/Mini -- --help` doesn't crash
before it reaches `NotImplementedException`.

---

## Phase 1: Leaf modules (7 tracks, all in parallel)

### Track A: Text helpers and image sniffing
**Files:** `Text.cs`, `ImageInfo.cs` · **Ports:** `tools.py` `CONTROL`, `printable`, `truncate`, `loose_edit`, `closest`, `image_info` · **Hard deps:** none

- `Printable`: the same three regexes (`" {16,}"` / `" {3,}"`, blank-line runs). Then walk by `Rune`: keep a rune if it is ASCII, printable (not `char.IsControl`) and not `\`, or `\n` when `lines`. Escape anything else the way Python's `unicode_escape` does: `\\`, `\t`, `\n`, `\r`, `\xNN` (< 0x100), `\uNNNN`, `\UNNNNNNNN`.
- `ShellWords.Split`: POSIX `shlex.split` semantics (whitespace, `'…'`, `"…"` with `\"`, `\\`, `\$` and `` \` `` escapes, a bare `\` escapes the next char). An unclosed quote throws `FormatException`, which callers treat like Python's `ValueError`. `Join` quotes words like `shlex.quote` (wrap in `'…'` unless the word matches `^[\w@%+=:,./-]+$`).
- `Closest`: Levenshtein ratio `1 - dist/max(len)`, best line ≥ 0.6. Same output text. Add a `// shortcut:` comment about difflib.
- `LooseEdit`: port line for line on top of `SplitLinesKeepEnds`.
- `ImageInfo.Read`: use `BinaryPrimitives` for little- and big-endian reads. Same four formats, same JPEG segment walk.

**Manual check:** a scratch script that prints `Printable("a\u202eb   c")`,
`ShellWords.Split("git log --format='%h x'")`, and `ImageInfo.Read` of a PNG and a JPEG. Paste the output.

### Track B: Shell and processes (simple mode)
**Files:** `Tools/Shell.cs` · **Ports:** `tools.py` `find_bash`, `spawn`, `stop`, `reap`, `RUNNING`/`JOBS`, `EVAL`, `stop_running`, `run_shell`, `start_job`, `still_running` · **Hard deps:** none

- `Bash`: POSIX uses the absolute path of `bash` found by walking `PATH` (skip relative entries). Windows uses `MINI_BASH`, then `%ProgramFiles%\Git\bin\bash.exe`, then `%LOCALAPPDATA%\Programs\Git\bin\bash.exe`. Accept absolute existing files only.
- `Spawn(argsOrCommand, cwd, command)`: log path `Paths.Home/jobs/<yyyyMMdd-HHmmss>-<hex12>.log`. `ProcessStartInfo` with `UseShellExecute=false`, `RedirectStandardInput=true` (then close stdin at once, which is Python's DEVNULL), and env `PAGER=cat`, `PYTHONUNBUFFERED=1`, `MINI_COMMAND`, `MINI_LOG`. **Output goes to the log through the shell, not through .NET pipes**, so a detached job keeps writing after mini exits. The bash wrapper is:
  `exec >>"$MINI_LOG" 2>&1; trap '' INT; eval "unset MINI_COMMAND MINI_LOG;$MINI_COMMAND"`
  (background jobs: `…; (eval "…"); echo "[exit $?]"`). The Windows `cmd.exe` fallback (no Git Bash) is `cmd /d /s /c "(<command>) >>"<log>" 2>&1"`. If starting fails, delete the log (Python's `except BaseException: unlink`).
- `Stop(job)` = `Proc.Kill(entireProcessTree: true)`, ignoring `InvalidOperationException` (already gone). `// shortcut:` comment: no process groups, so orphaned `&` children survive.
- Reaper: `_ = Task.Run(async () => { await p.WaitForExitAsync(); append "[exit N]" to the log; })`. Only for jobs left running (`keep`), as in Python.
- `RunShellAsync`: `WaitForExitAsync` linked with a timeout CTS and `ct`.
  - On timeout with `keep`: add to `Running`, start the reaper, and return `(null, note + log so far)`. Use Python's note text.
  - On cancel (`ct`) or timeout without `keep`: `Stop`, wait, delete the log (Windows: retry the delete 60× at 50 ms), and rethrow (`TimeoutException` for a timeout).
  - Otherwise: read the log (UTF-8, replace invalid bytes), delete it, and return `(ExitCode, text)`.
  - `keep` is never true on Windows (the caller decides).
- `StartJob`: on Windows, throw `ToolError` with the same text. Otherwise spawn, add to `Jobs`, start the reaper without a log, and return the same text.
- `StillRunning`: port it exactly, using `Job.Listed` and `Proc.HasExited`.
- `StopRunning` (wired to `AppDomain.ProcessExit` by `Cli`): Kill-tree every live `Running` job. No SIGTERM-then-SIGKILL step, because there is no group signal in simple mode (`// shortcut:` comment).

**Manual check:** throwaway console calls: `RunShellAsync("echo hi; exit 3")` → `(3, "hi\n")`;
`RunShellAsync("sleep 5", timeout 1, keep true)` → still-running note, and the log ends with `[exit 0]`;
`StartJob("sleep 2; echo done")`, then `StillRunning` twice. Paste the output.

### Track C: Session, Context, Skills
**Files:** `Session.cs`, `Context.cs`, `Skills.cs` · **Ports:** `session.py`, `context.py`, `ext.py` `frontmatter`/`find_skills`/`safe_name` · **Hard deps:** none

- `Session.Append`: open with `FileMode.Append`/`ReadWrite`. If the file isn't empty, read the last byte, and if it isn't `\n`, write one first (repairs a line cut by a crash). Then write `{"t": unix seconds as double, "kind": kind, …data}` + `\n`, serialized with `Json.Relaxed`.
- `Events()`: skip lines that fail to parse (`JsonException`).
- `Load()` / `List()`: port directly. Newest first by `LastWriteTimeUtc`. Exclude stems containing `-sub`. Truncate titles to 50 chars with `…`.
- `Create`: id `yyyyMMdd-HHmmss-<hex4>`.
- `Context.BuildSystem`: same strings and order. `platform.system()` → `"Darwin"`, `"Linux"` or `"Windows"`, matching Python's output (use `OperatingSystem.Is*`). The timestamp format is `yyyy-MM-dd HH:mm` + `TimeZoneInfo.Local.StandardName`. Shell label from `Shell.Bash`/`Shell.Windows`.
- `Skills.Find`: roots `cwd/skills`, `cwd/.claude/skills`, `cwd/.agents/skills`, `Paths.Home/skills`. For each, sort `*/SKILL.md`, read the first 4,000 chars, and parse the frontmatter with the same two regexes.

**Manual check:** create a session in a temp dir, append `meta` and `msg`, append a deliberately cut line
plus another event, then `Load()`. The cut line is skipped and the rest is intact.

### Track D: MCP hub
**Files:** `McpHub.cs` · **Ports:** `ext.py` `MCPHub` · **Hard deps:** none

- Config lookup order: `cwd/mcp.json`, `cwd/.mcp.json`, `Paths.Home/mcp.json`. Use the same validation and error strings.
- For each server, **one after the other**: build a `StdioClientTransport`. On POSIX use `Command="/bin/sh"`, `Arguments=["-c", "trap '' INT; exec \"$0\" \"$@\"", command, ...args]` (SIGINT mitigation). On Windows, pass `command`/`args` straight through. `EnvironmentVariables` comes from `spec.env`, and `StandardErrorLines` appends to `Path.GetTempPath()/mini-mcp.log`. Run `McpClient.CreateAsync` and `ListToolsAsync` under a 30 s CTS. A failure adds `"{name}: {e}"[:200]` to `Errors` and moves on.
- Tool names: `mcp__{SafeName(server)}__{SafeName(tool)}`, cut to 128 chars. On a clash, skip the tool and record an error. The schema is `{"name", "description": (desc ?? "")[:1024], "input_schema": InputSchema ?? {"type":"object"}}`. Read `InputSchema` from the protocol tool's JSON. Verify the property path in the SDK, likely `tool.ProtocolTool.InputSchema` (a `JsonElement`).
- `CallAsync`: convert the args `JsonObject` to `Dictionary<string, object?>` holding `JsonElement` values. Call with a 300 s CTS linked to `ct`. Text = the text content blocks joined with `\n`, other block types as `[{type} content]`, `"[no output]"` if empty. Return it with `IsError`.
- Windows: check whether the SDK already wraps `npx` and other non-`.exe` commands in `cmd /c`. If it doesn't, wrap them yourself.
- `DisposeAsync`: dispose every client, each under a 5 s timeout, swallowing errors.

**Manual check:** point an `mcp.json` at `tests/echo_server.py` (`"command": "uv", "args": ["run", "python", "tests/echo_server.py"]`). List the schemas and call the echo tool.

### Track E: Api (the SDK boundary, and the riskiest piece)
**Files:** `Api.cs` · **Ports:** the SDK-facing parts of `loop.py` `call`, `transient`, `server_summary`/`summarize` transport, `need_key` client construction, and `cli.py` `--models` and `count_tokens` · **Hard deps:** none · Live checks cost a few cents.

The rest of the program hands `Api` a `JsonObject` body shaped like Python's `request_args` kwargs (including a `"betas"` array) and gets back the response as a `JsonObject`. Inside:

1. **Request:** remove `"betas"` from the body and send it as the `anthropic-beta` header (comma-joined). Build `Anthropic.Models.Beta.Messages.MessageCreateParams.FromRawUnchecked(headers, query: empty, body: Json.ToElements(body))`. **Verify** that unknown or new body fields (`compaction`, `fallbacks`, `thinking.block_binding`, top-level `cache_control`, `output_config`) reach the wire unchanged. Turn on SDK request logging or point `BaseUrl` at a local `nc -l` once.
2. **Streaming:** `client.Beta.Messages.CreateStreaming(params, ct).CollectAsync(aggregator)`. For each event, if it is `content_block_delta` with `text_delta`, call `ui.Text`; with `thinking_delta`, call `ui.Thinking`. Use the event's `TryPick*` helpers, or read the raw JSON if that is simpler. Then take `aggregator.Message()`. If there are no events or no `stop_reason`, throw `StreamCutException` (a transient error with Python's message).
3. **Response to JSON:** serialize the final `BetaMessage` to a `JsonObject` (through its raw data or `JsonSerializer.SerializeToNode`), then `Json.StripNulls`. **Verify** that `usage.iterations`, `input_transformations`, `stop_details`, and `compaction`/`fallback`/`server_tool_use`/`web_search_tool_result` content blocks all survive. If the aggregator drops or mangles any of them, accumulate the final message yourself from the raw events (≈60 lines: `message_start`, `content_block_start`/`delta`/`stop`, `message_delta`).
4. **Retries inside the stream:** keep the loop in `Agent.CallAsync` (Track I). `Api` throws, and `IsTransient` classifies:
   - transient: `AnthropicApiException` with status 429, status ≥ 500, or `ErrorType` in `overloaded_error`, `rate_limit_error`, `api_error`, `timeout_error`; an SSE error event (check whether it surfaces as `AnthropicApiException` or `AnthropicSseException`); `AnthropicIOException`; `HttpRequestException`; `StreamCutException`.
   - not transient: validation, auth, our own `IOException`.
   - The SDK's `MaxRetries` handles failures before the stream starts.
5. **Clients:** Anthropic = `new AnthropicClient()` (env `ANTHROPIC_API_KEY`/`ANTHROPIC_AUTH_TOKEN`). Another provider = `new AnthropicClient(new ClientOptions { BaseUrl = other.BaseUrl, ApiKey = env[other.Key] })`. **Security check:** confirm that this client sends **no** Anthropic credential and none of the `ANTHROPIC_*` env headers to the other host. List the env vars the C# SDK reads by default; if it reads `ANTHROPIC_AUTH_TOKEN` or custom headers, clear them explicitly. Mirror `need_key`'s errors.
6. `CreateAsync` (used for compaction) and `CountTokensAsync` (Beta `CountTokens`, same raw approach). `ListModelsAsync` pages through `client.Models.List()`.
7. **Fallback if step 1 or 3 can't be made to work through the SDK:** replace the SDK internals of `Api.cs` with `HttpClient` + a manual SSE reader (`POST {base}/v1/messages`, headers `anthropic-version: 2026-10-21`, `authorization: Bearer`, `anthropic-beta`). About 120 lines. The public contract stays the same, so no other track is affected. Report which path you took.

**Manual check:** a throwaway program that streams "say hi" on `claude-haiku-5-5` with the full Python
`request_args` body (betas, adaptive thinking, block binding, `output_config`, fallbacks), prints the final
JSON, and confirms `stop_reason`, `usage` and the content blocks. If a `DEEPSEEK_API_KEY` is available, run
the same against `deepseek-flash`.

### Track F: Terminal UI
**Files:** `Ui.cs` · **Ports:** `cli.py` `UI`, color constants · **Hard deps:** none

- Port line for line. Use `Console.Out.Write` + `Flush` and `Console.Error.WriteLine` for `Warn`. The model's text goes through `Text.Control.Replace(s, "")`.
- `ToolCall(name, args)`: the brief shows `command`, `path` or `prompt`, otherwise the JSON cut to 120 chars. For `find` it is always the JSON.
- `EnableWindowsVt()`: `[LibraryImport("kernel32")]` for `GetStdHandle(-11/-12)`, `GetConsoleMode`, `SetConsoleMode(mode | 0x4)`. Set `Console.OutputEncoding = UTF8`.

**Manual check:** stream fake text and thinking chunks, then a tool call and result, and look at the output.

### Track G: Approver and schemas
**Files:** `Tools/Approver.cs`, `Tools/Schemas.cs` · **Ports:** `tools.py` `Approver`, `approval`, `GATED`, `schema`, `SCHEMAS`, `WEB_SEARCH` · **Hard deps:** A (`ShellWords`, `Printable`)

- `PreApproved`: same `SAFE`, `GIT` and `SHELL` sets. A `ShellWords.Split` `FormatException` → false.
- `OkAsync`: same order of checks and the same reason strings. The prompt UI uses `Console.WindowWidth`/`WindowHeight` (fall back to 80×24 if they throw), the exact escape sequence prefix, `│` rows, and `Console.ReadLine()`.
- `Schemas.Local`: build with `JsonNode.Parse` of raw string literals, or with `Json.Obj`. **Descriptions must be byte-identical**, including the `WINDOWS` branches in the `bash` description, apart from `.NET regex`.

**Manual check:** print `Approver.Approval(...)` for each mode, print `PreApproved` for `ls -la`, `git log -p`,
`git show HEAD:secret`, `cat a | sh`, and print the `bash` schema JSON.

---

## Phase 2: Composition (4 tracks, in parallel; may start right after Phase 0)

### Track H: LocalTools
**Files:** `Tools/LocalTools.cs` · **Ports:** `tools.py` `LocalTools` · **Hard deps:** A, B, G

- `_path`: on Windows, `/c/x` → `c:/x`. Expand `~` and `~/…` (`~user` → `Path.Combine("/", p)`, as in Python's fallback). Resolve against cwd, then `Paths.RealPath`.
- `IsUnder(child, parent)`: compare path parts with `OrdinalIgnoreCase` on Windows and `Ordinal` elsewhere. Python's `Path` containment does the same.
- `_inside`, `_secret` (same regex and set), `_private`, `_words`, `_names_private`, `_ask`. "Executable" = `File.GetUnixFileMode(p)` has any execute bit (POSIX), or the extension is in `WIN_RUNS` (Windows).
- `seen`: `Dictionary<string, DateTime>` of full path → `File.GetLastWriteTimeUtc`.
- `read` / `write` / `edit` / `find` / `bash`: port line for line, with the same error strings.
  - `find` uses its own `Walk(root)`: sorted subfolders, skipping dot-folders, `node_modules`, `__pycache__`, and reparse points (symlinked folders).
  - `FileSystemName.MatchesSimpleExpression(name, file, ignoreCase: OperatingSystem.IsWindows())`.
  - `Regex(contains)` with a parse error → `ToolError`. Python raised `re.error`, which became a generic error in `run_tools`; same message style.
  - `bash`: `told`/`Stopping` logic on `Shell.Running`. `keep: !Shell.Windows`. A timeout on Windows → `ToolError("Timed out after …")`.
- `CallAsync(name, args)`: dispatch by name, reading typed args from the `JsonObject`. A missing required arg → `ToolError("Bad arguments: …")`.

**Manual check:** in a temp project: read a file, edit it, write a new file, `find` with a regex, run `bash`
with `ls`, and try to read `~/.ssh/config`. The last must go to approval.

### Track I: Agent core
**Files:** `Agent/Agent.cs`, `Agent/Agent.Turn.cs` · **Ports:** `loop.py` lines 90–249 and 250–449 (`EXTRA_SCHEMAS`, `need_key` call site, `n`, `clean`, `Agent.__init__`, transcript, `request_args`, `call`, `turn`, `run_tools`, `dispatch`) · **Hard deps:** C, E, F, G (H for the live check)

- **Constructor:** port the resume and new-session branches exactly, including the goal pause on resume, the restored `GOAL_OFF`, the "Approval in this session" re-statement, and the `Held` recount of image bytes. On a new session, append `meta` with `model`, `cwd`, `system`, `tools`.
- **`AddUser`:** de-duplicate `PendingSystem`, keeping the latest position (`list(dict.fromkeys(reversed(x)))[::-1]` = keep each item's last occurrence, in order).
- **`RequestArgs`:** build the same `JsonObject` as Python. Copy `Messages` and `paused` into a **new** `JsonArray` with `DeepClone()`, because a `JsonNode` can only have one parent.
- **`CallAsync`:** waits `2, 4, 8, 0`. `started` becomes true at the first event; to know that, `Api.StreamAsync` must surface it, for example through an `Action onStart` parameter or by throwing a wrapper. Agree the mechanism with Track E. Retry only `started && Api.IsTransient(e)`, with `ui.EndMessage()`, the warning text, and `Task.Delay(wait, ct)`.
- **`TurnAsync`:** port `turn` exactly (paused list, `pause_turn` limit 5, refusal/empty → placeholder, `msgs` event written as one line before the in-memory append, cut-off counting, the compaction check after `tool_use`, return the stop reason).
  - `OperationCanceledException` → placeholder "interrupted by the user", return `"interrupted"`.
  - Other exceptions → placeholder `request failed: {TypeName}`, return `"error"`.
- **`RunToolsAsync`:** results in order. A cancellation during one tool marks it `"Interrupted by the user."` and every later tool `"Not run: interrupted by the user."`; all get `is_error`. `ToolError` → its message. Other exceptions → `"{Type}: {msg}"`. Content = `Text.Truncate(str)` for a string, or the array as is.
- **`DispatchAsync`:** `task`, `ask_user` (prompt via `Console.ReadLine` with `Text.Control` stripping, numeric pick, an `answer` session event only for typed answers), `goal_done`, `mcp__*` (approval, then `Hub.CallAsync`), otherwise `ToolError`.
- **`Clean`:** port exactly; it is the one sanctioned exception to "append as returned".

### Track J: Agent extras
**Files:** `Agent/Agent.Cost.cs`, `Agent/Agent.Compact.cs`, `Agent/Agent.Sub.cs`, `Agent/Agent.Goal.cs` · **Ports:** `loop.py` `track`, `price`, `judge`, `run_subagent`, `compact`/`server_summary`/`summarize`/`compact_at`/`should_compact`/`maybe_compact`, every `/goal` method, `verify`, and the `SUMMARIZE`/`JUDGE`/`GOAL_*`/`REVIEW`/`CUT_OFF` strings · **Hard deps:** B, C, E (I for the live check)

- `Track`/`Price`: operate on the response `JsonObject` (`usage.iterations`, `server_tool_use.web_search_requests`, `input_transformations`). Use `Config.For(model)` when `Config.Models` contains the iteration's model, otherwise this session's config.
- `JudgeAsync`: a static `HttpClient`, POST to `JUDGE_URL` with a 20 s timeout, bearer `OPENROUTER_API_KEY`. Fail closed with the same strings. `json.dumps(detail)` → `JsonSerializer.Serialize(detail)`.
- `RunSubagentAsync`: the child session path is `<session dir>/<id>-sub-<hex6>.jsonl`, sharing the same `Api` instance. `"interrupted"` → throw `OperationCanceledException`.
- Compaction: `CompactAsync` swaps in a **new** `Messages` list and appends a `compact` event. That is the one place the in-memory list is replaced, and on disk it is still an append.
- Goal: the `Goal` record replaces the dict, and `SaveGoal` serializes it into the `goal` event. `RunGoalAsync` keeps Python's `try/except BaseException/finally` as `try/catch { paused; throw; } finally { SaveGoal; append GOAL_OFF }`. `VerifyAsync` runs `Shell.RunShellAsync(cmd, cwd, 600, keep: false)`. A `TimeoutException` gives the same text. The verdict regex is `[*_`\s]+`.

### Track K: CLI
**Files:** `Cli.cs`, `Program.cs` · **Ports:** `cli.py` `main`, `repl`, `command`, `context_report`, `print_goal`, `HELP`, `DREAM` · **Hard deps:** all (start from the stubs)

- **Flags (identical names, so `smoke-test/run.py` can drive the C# binary unchanged):** `-p/--prompt`, `-c/--continue`, `-r/--resume`, `--model`, `--effort` (default `high`), `--models`, `--permissions ask|judge|yes`, `--allow` (repeatable), `--goal`, `--verify`, `--max-turns` (default 25), `--budget`, `--json`. Use the same help texts.
- **Startup:**
  - `CultureInfo` invariant; `Console.OutputEncoding = UTF8`; `Ui.EnableWindowsVt()` on Windows.
  - `AppDomain.ProcessExit += Shell.StopRunning`.
  - `PosixSignalRegistration` for `SIGTERM` and `SIGHUP` → `Environment.Exit(128 + n)`.
- **Ctrl-C:** one `Console.CancelKeyPress` handler. While a turn or command runs, set `e.Cancel = true` and cancel that command's `CancellationTokenSource`. At the idle prompt, let it exit, as Python's `KeyboardInterrupt` at `input()` does.
- **Headless:** the same JSON result line `{"stop"|"status","note","turns", "session","cost","answer"}`, and exit 1 under the same conditions.
- **`ContextReport`:** measured through `Api.CountTokensAsync` for Claude, with the same `~4 chars/token` estimate on any exception.
- **REPL:** `Console.ReadLine` with the cyan `›` prompt. `null` (EOF) → exit. Per-command errors are printed and the loop continues.
- `--models`: `Api.ListModelsAsync()` (only with `ANTHROPIC_API_KEY`) plus the `Config.Other` keys.

---

## Phase 3: Integration and live checks (1 agent)

1. `dotnet build -c Release` with no warnings. Read through every `// TODO(track …)` and resolve it.
2. **Invariant review** (read the code, no tests): every path out of `TurnAsync`/`RunToolsAsync` either pairs every `tool_use` with a `tool_result` or appends a placeholder; nothing writes to `Messages` except `Add`, `TurnAsync`'s `msgs` append, and `CompactAsync`; the session write happens before the memory append.
3. **Quick live check** (a few cents):
   `cd $(mktemp -d) && dotnet run --project <repo>/dotnet/Mini -- --model claude-haiku-5-5 -p "list the files here"`
   Then try `--model deepseek-flash` if that key is available.
4. **Interactive check:** a REPL session that reads, edits and runs bash; Ctrl-C during a `sleep 30`; `/sessions`, `/resume`, `/context`, `/compact`, `/goal <x>` then `/goal status`; a subagent `task`; an MCP echo server.
5. **Smoke test, unchanged Python runner:** `dotnet publish dotnet/Mini -c Release -o /tmp/mini-net`, put `/tmp/mini-net` first on `PATH` (the binary is named `mini`), then `uv run python smoke-test/run.py --harness mini-deepseek` (≈2¢) or `--harness mini` (≈$0.60). All five tasks should pass. Note: this appends rows to `smoke-test/results.jsonl` labeled `mini`. Revert them, or tell the user, before committing.
6. Windows pass, if a Windows machine is available: Git Bash present and absent, the `cmd.exe` fallback, a `/c/...` path, VT colors.

## Risks, ranked

1. **Raw JSON through the C# SDK (Track E).** If `FromRawUnchecked` or the aggregator drops new beta fields or blocks, thinking binding or compaction breaks quietly. *Mitigation:* the explicit checks in E.1 and E.3, and the HttpClient fallback behind the same contract.
2. **Credential leak to another provider (Track E.5).** This is a security boundary. Confirm it explicitly, never assume it.
3. **Symlink resolution in `RealPath` (Phase 0).** A weak version lets writes or reads escape the project without approval.
4. **Ctrl-C semantics (Tracks B, I, K).** These are the accepted gaps above. Cancellation must still always leave a valid transcript.
5. **Size creep.** The goal is readability, not features. Anything that isn't in the Python code is out of scope.

---

## Post-migration audit (2026-11-17)

A read-only, function-by-function comparison of `src/mini/` against `dotnet/Mini/`, run after the last Python change (1.0.4, 2026-11-06).

**Result:** every module, function, constant, prompt string, tool description, CLI flag and slash command has a C# counterpart. `dotnet build` gives 0 errors. There is no `NotImplementedException` and no `TODO(track …)` left. Prompt and help texts are byte-identical (the only change is the planned `.NET regex`). `models.json` is linked.

**Not verified:** no live call to the Anthropic API, nothing run on Windows, and the interactive Phase 3 checks were not run. `smoke-test/results.jsonl` has no new rows, so either the smoke test was never run or its rows were reverted.

### Open items: fix or confirm

| # | Item | Python | C# | Fix |
|---|---|---|---|---|
| 1 | Claude path sends `Authorization: Bearer <key>`, where Python sends `x-api-key` | SDK default | `Api.cs:40-65` | Confirm with the Phase 3 live check. If the API rejects it, send `x-api-key` explicitly |
| 2 | Ctrl-C at the idle REPL prompt kills the process (exit 130, no newline, MCP hub not disposed; `keep` jobs may survive) | `cli.py:204-209` (prints a newline, returns 0, `hub.close()`) | `Cli.cs:33-37` | At the idle prompt, cancel the read and return 0 through the normal cleanup path |
| 3 | The judge ignores Ctrl-C. A cancel becomes "deny" only after up to 20 s, and an "allow" can still start the command | `loop.py:330` (KeyboardInterrupt passes through) | `Agent.Cost.cs:105,153`, `Approver.cs:65` | Pass `ct` through the `Judge` delegate and `JudgeAsync`. Catch with `when (!ct.IsCancellationRequested)` |
| 4 | After a successful non-`keep` command, its leftover `&` children are not killed (affects the `/goal` verify command) | `tools.py:139-140` | `Shell.cs:146-148` | `Stop(job)` after exit when `!keep` |
| 5 | Bash output keeps `\r`. Python's text mode turns `\r\n` and `\r` into `\n` | `tools.py:141,149` | `Shell.cs:188-192` (`ReadLog`) | Normalize line endings in `ReadLog` |
| 6 | Stop wording says `kill PID` instead of `kill -- -PID`. If the model follows Python's wording, the command now goes to approval. "Ended" reflects only the shell's exit | `tools.py:154-187`, `490-505` | `Shell.cs:154-157,206-208,221-223`, `LocalTools.cs:449-450` | **Decided:** keep the .NET wording; recorded in **Accepted gaps** |
| 7 | FIFOs and other non-regular files block `read` and `find` | `tools.py:382-383,477-478` | `LocalTools.cs:209-221,241-242,416` | Reject or skip files that aren't regular files, as Python does |
| 8 | An unhandled exception other than `InvalidOperationException` aborts the process (exit 134) | `cli.py:192-193` (traceback, exit 1) | `Cli.cs:212-215` | Top-level catch-all: print the error, exit 1 |

**Status (2026-11-17):**
- **Fixed:** #2, #3, #4, #5, #7 and #8.
- **Decided:** #6 is an accepted gap.
- **Still open:** #1, which needs a live API call.
  - **2026-11-18 (refactor P7):** a local capture server shows the Claude path sends `Authorization: Bearer <ANTHROPIC_API_KEY>`
    and no `x-api-key`. `ANTHROPIC_AUTH_TOKEN` also goes as Bearer, plus the `oauth-2025-04-20` beta. The SDK documents this
    (Anthropic 13.4.0, `ClientOptions.ApiKey`: "sent as `Authorization: Bearer`"), so it is the official SDK's design, not a
    port bug. Whether api.anthropic.com accepts it is still unconfirmed: there is no Anthropic key to test with.

How the fixes work:
- **#2:** at the idle prompt, `CancelKeyPress` prints a newline, disposes the hub, runs `Shell.StopRunning` and exits 0.
- **#3:** a `CancellationToken` runs through `Approver.Judge`, `OkAsync` and `JudgeAsync`. A late "allow" is dropped once Ctrl-C is pressed.
- **#4:** a non-keep POSIX command runs under `trap 'kill -9 $(jobs -p)' EXIT`. Shortcut: this covers only that shell's top-level `&` jobs.
- **#5:** `ReadLog` normalizes `\r\n` and `\r` to `\n`.
- **#7:**
  - `read` opens with `O_NONBLOCK` and runs `fstat` on the same descriptor. It then raises `ToolError("Not a regular file.")`, the same text as Python. `find` skips such files.
  - Shortcut: the type comes from the runtime's internal `SystemNative_FStat`.
- **#8:** a top-level catch-all prints the exception and exits 1.

Checked:
- **macOS:** script checks for #4, #5, #7 and #8, and a pty Ctrl-C run for #2 (exit 0).
- **Not run:** #3 (code review only). #7 on Linux: the `libc` P/Invoke name is the documented form, but it was not run there.

### Minor differences (accept or fix cheaply)

- **Headless `--json`:** whole-number `cost` prints as `1`, not `1.0`. `ToJsonString()` escapes `' < > & +` and backticks. The values are the same (`Cli.cs:191-197`).
- **SIGTERM/SIGHUP:** `Environment.Exit` skips the `await using` disposal of the MCP hub (`Cli.cs:57-58`).
- **System prompt `%Z`:** `TimeZoneInfo.Local.StandardName` probably gives a long name, not `CET` (`Context.cs:58`).
- **Argparse:** prefix abbreviations (`--perm`) are not accepted. The `usage:` and error text differ.
- **Session file:**
  - Non-ASCII is written raw.
  - `t` has millisecond resolution.
  - Invalid UTF-8 is replaced silently.
  - Non-object lines are skipped.
- **MCP:**
  - Error texts use `e.Message`, not `repr`.
  - A missing command goes through `/bin/sh` (exit 127).
  - Servers are closed in parallel, not one by one.
- **`Stop` and exit cleanup:**
  - `stop_running` uses SIGKILL only, with no SIGTERM and no 1 s grace. `group_alive` is not ported.
  - The wait after a kill is bounded at 5 s; Python waits without a limit.
- **Text handling:**
  - `edit` has a new, clearer message for files that aren't valid UTF-8.
  - `find` has a 2 s regex timeout with a new message.
  - `Closest` ties go to the first line, not the larger one as in difflib.
  - Length caps count UTF-16 units.
  - Writes replace lone surrogates instead of raising.
- **Approval:**
  - The prompt size ignores `COLUMNS`/`LINES`.
  - The executable check uses mode bits, not `os.access(X_OK)`.
  - `Job.Stopping` is written but never read (`Shell.cs:125`, `LocalTools.cs:452`).
- **Agent:**
  - `dispatch` edge cases differ: a missing `task.prompt`, non-string `ask_user` options, and null `goal_done` evidence.
  - Tool JSON that doesn't parse at the end of a stream ends the turn as `"error"` (`Api.cs:342-349`).
  - Budget floats print as `1E-05` (`Agent.Goal.cs:170-175`).
  - A `null` override in `~/.mini-net/models.json` throws (`Config.cs:94-96`).
  - A 10-minute stall timer was added to the stream (`Api.cs:29-31`).
  - A provider without `key` gets its own error (`Api.cs:50`).

### Verified as fine

- **Credential isolation:** an Anthropic credential never reaches another provider. `ANTHROPIC_CUSTOM_HEADERS` does reach the wire, so the guard at `Api.cs:52-58` is required and correct.
- **Betas and version:** betas go out as `anthropic-beta`, and `anthropic-version` is `2026-10-21`.
- **Append-only:** every session write happens before the matching in-memory change.

### Corrections to this document

- The `str.splitlines()` row in **Accepted gaps** is out of date. `Text.cs:67-73` handles the exotic separators, matching Python.

### Python changes after the port: 1.0.5 (`e4da828`, pulled 2026-11-17)

Only `src/mini/tools.py` changed. The other new commits touch the README, benchmarks, the smoke-test results, tests and the version bump. `models.json`, `loop.py`, `cli.py`, `session.py`, `context.py` and `ext.py` did not change.

**Status (2026-11-17):**
- **Ported:**
  - **P1:** `ImageInfo.PngDamage` with a hand-written CRC-32, checked in `LocalTools.ReadAsync`. Its output matched Python's `png_damage` on four test PNGs: valid, bad CRC, no `IEND`, and truncated.
  - **P2:** the `bash` description is byte-identical to Python's (checked with `cmp`).
  - **P3:** the wait-hint suffix.
- **Recorded as a `// shortcut:` in `Shell.StillRunning`:** P4.
- **Not ported:** P5, as planned.
- **Build:** `dotnet build` gives 0 warnings and 0 errors.

| # | Change | Python | Port to | How |
|---|---|---|---|---|
| P1 | **Damaged PNG refused before it enters the transcript.** `png_damage(data)` walks the chunks from byte 8. It fails a chunk whose CRC-32 (over type + data) doesn't match, or a file with no `IEND`. `read` raises `ToolError("This PNG is damaged ({why}), so the model's API would refuse it. Fix what wrote it, or convert the source again another way, and read that.")`, after the size check and before base64 | `tools.py` `png_damage`, `LocalTools.read` | `ImageInfo.cs` (`PngDamage`), `LocalTools.cs` `ReadAsync` | Write CRC-32 by hand (~10 lines, table-driven, polynomial `0xEDB88320`), because `System.IO.Hashing` is a new package and the plan allows no new dependencies. Use `BinaryPrimitives.ReadUInt32BigEndian`. The chunk type is decoded as Latin-1. Use the same message texts. **Must do:** a refused image breaks every later request, because the transcript is never edited |
| P2 | **`bash` tool description:** adds the sentence "Output piped into tail, grep or less appears only when the command ends, so to watch a long one, send its output to a file and read that." before "Commands other than plain reads…" | `SCHEMAS["bash"]` | `Schemas.cs` | Byte-identical copy (descriptions must match) |
| P3 | **Wait hint in `still_running`:** "…wait with: while kill -0 PID 2>/dev/null; do sleep 5; done**, in a call whose timeout is long enough**]" | `still_running` | `Shell.cs:221-223` | Append the same suffix to the C# text |
| P4 | **An ended job that left something running says so:** "[ended since the last call: pid N, log L; what it started is still running, in process group N: stop it with kill -- -N]". This is checked once, when the job is announced, and skipped if a live entry has the same pid | `still_running`, `group_running` | `Shell.cs` `StillRunning` | **Accepted gap (simple mode):** there are no per-job process groups, so a group probe would see mini's own group. Keep the plain "ended" line, and add a `// shortcut:` comment. Upgrade path: `setsid` via P/Invoke, then port `group_running` |
| P5 | **`group_alive` → `group_running(pgid)`.** On Linux it reads `/proc/*/stat` and counts zombies as gone, so in a container whose init never reaps, a finished process no longer looks alive. On macOS it keeps `killpg(pgid, 0)` | `group_running`, `stop_running` | none | Not ported. It belongs to the same gap as `group_alive`, which is not ported either (see minor differences) |

Related open item: #6 (stop wording). Python 1.0.5 now tells the model `kill -- -PID` in two places, so the gap between the two wordings has grown. Decide #6 and P4 together.

### C# better than Python (port back)

- `find` checks privacy on the symlink's resolved target (`LocalTools.cs:406-411`). Python checks only the link name (`tools.py:473-476`), so `find` with `contains` can read lines from a secret file through a project symlink.
