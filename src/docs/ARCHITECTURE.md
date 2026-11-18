# Architecture of the C# port

`dotnet/` is a port of the Python harness in `src/mini/`. Python is the reference: the C# code behaves the same way and carries
`// Python: <file>.py <Name>` tags so a Python change can be traced to its C# counterpart (`docs/PYTHON-MAP.md`, generated
by `scripts/python-map.sh`). Gaps that were accepted on purpose are listed in `MIGRATION-PLAN.md`; the design decisions and
the history of the refactor are in `REFACTOR-PLAN.md`.

## Projects and layers

```
Mini.Cli  ──►  Mini.Core  ──►  Anthropic SDK, ModelContextProtocol.Core, MS.Extensions (DI, Options, Http)
   │               ▲
   └ console, signals, flags      Mini.Tests (xUnit v3) uses both, with fakes for the ports
```

- **Mini.Core** holds all the behavior and never touches the console (a test checks that the assembly doesn't reference
  `System.Console`). The environment is read in exactly one place, `MiniOptionsBinder`, and the clock comes from an injected
  `TimeProvider`. `BannedSymbols.txt` enforces both (RS0030).
- **Mini.Cli** is the composition root (`Program.cs` → `MiniApp.RunAsync`) and the terminal: option parsing, the headless runner,
  the REPL, the slash commands, Ctrl-C and signal handling, and the console implementations of Core's ports.

## Mini.Core by folder

| Folder | What it owns |
|---|---|
| `Configuration/` | `MiniOptions` (env, read once), `MiniPaths`, `ModelCatalog` (`models.json`), `ModelConfig`, `PriceSchedule` |
| `Conversation/` | `Message`, the `ContentBlock` records, `StopReason`, `Usage`, `ModelResponse`, `Transcript`, `PendingNotes` |
| `Sessions/` | the JSONL store, typed `SessionEvent`s, `SessionReplayer`, `SessionManifest` (frozen system prompt and tools), `SessionCatalog` |
| `Models/` | `IModelClient` → `RetryingModelClient` (decorator, 2/4/8 s) → `AnthropicModelClient`; `MessageRequestBuilder`; `IProviderProfile` (`AnthropicProfile`, `CompatibleProfile`); `AnthropicClientFactory` |
| `Tools/` | `ITool` per tool, `ToolRegistry`, `ToolExecutor`; `Files/`, `Shell/` (`ShellRunner`, `JobRegistry`), `Agentic/` (task, ask_user, goal_done), `Mcp/` (`McpHub`) |
| `Security/` | `PathResolver`, `WorkspacePolicy`, `SecretClassifier`, `CommandPathInspector` |
| `Approval/` | `ApprovalPipeline`, a chain of `IApprovalStep`s: auto-approve → safe command → MCP allow-list → too long to review → judge (`JevJudge`) → headless → ask the user |
| `Agents/` | `Agent` (facade), `AgentFactory` (`CreateNew`, `Resume`), `TurnRunner`, `SubagentRunner`; `Compaction/`, `Costs/`, `Goals/` |
| `Prompts/` | `HarnessPrompts` (every model-visible text), `PromptTemplate`, `SystemPromptBuilder`, `SkillCatalog` |
| `Interaction/` | the ports the CLI implements: `IAgentOutput`, `IUserInteraction`; `OutputStreamObserver` adapts the output to `IStreamObserver` |
| `Common/` | text helpers with Python semantics: `PythonJson`, `PythonString`, `LineSplitter`, `FuzzyLineMatcher`, `ShellWords`, … |

`Mini.Cli` has `Hosting/` (`MiniApp`, `ConsoleCancellation` with the states `Idle`, `AtPrompt`, `Running`, `ProcessSignals`,
`WindowsConsole`), `Commands/` (`CliOptions`, `RootCommandFactory`, `HeadlessRunner`, `Repl`, `SlashCommandRouter` and one
`ISlashCommand` per command) and `Terminal/` (`ConsoleAgentOutput`, `ConsoleUserInteraction`, `TerminalSize`, `AnsiColors`).

## How a turn runs

1. The CLI asks `IAgentFactory` for an `Agent`: `CreateNew` builds the system prompt and the tool list and freezes them into the
   session's `meta` event; `Resume` replays the session file and takes both from that event.
2. `Agent.TurnAsync` hands the work to `TurnRunner`: append the user message, build a `MessageRequest`, let the provider profile
   shape it, stream it through the retrying client, append the returned blocks as they are, run the tool calls through
   `ToolExecutor` (each guarded by the approval pipeline), append one message with all the `tool_result`s, and repeat until
   the model stops.
3. `UsageTracker` prices each call; `Compactor` replaces the history with a summary when the context passes the threshold
   (server-side on Claude, an ask-for-a-summary request elsewhere). `GoalRunner` drives `/goal` and `GoalVerifier` checks it.

## Invariants

- **Append-only transcript.** A message is never edited, reordered or removed once it is in the `Transcript` or the session
  file. The system prompt and tools are frozen at session start; anything that changes later is appended as a system note
  (`PendingNotes`). Model-returned blocks keep their raw JSON so they replay byte for byte.
- **Every `tool_use` gets a `tool_result`** in the next user message, with `is_error` for failures, denials and interrupts.
- **Parity with Python.** Model-visible text, request bodies and the system prompt are pinned by the golden files in
  `tests/Mini.Tests/Golden/`; a change there is a behavior change and needs a reason.
- **No console in Core, no environment or clock outside the binder and `TimeProvider`.**

## Tests

`cd dotnet && dotnet test --solution Mini.slnx`. The tests use a scripted `FakeModelClient`, `FakeTimeProvider` and recording
fakes for the ports. `PythonReferenceTests` checks that every type carries a Python tag; `tests/Mini.Tests/NOT-PORTED.md` lists
the Python tests that have no C# port yet, with the reason. Live checks and the smoke test are described in `REFACTOR-PLAN.md`.
