# mini-dotnet-harness

A C# / .NET 10 port of `mini`, a small Python coding-agent harness. The harness runs an Anthropic (or compatible) model in a loop. It lets the model call tools (files, shell, MCP, sub-agents) and asks for approval before risky actions. It also keeps an append-only session transcript.

- `src-original/` is the **Python reference** (`cli.py`, `loop.py`, `tools.py`, `session.py`, …).
- `src/` is the **C# port**.

## How it is ported

Python is the source of truth. The C# code is meant to behave the same way, not to be a redesign.

- **Same behavior.** Model-visible text, request bodies and the system prompt are pinned by golden files in `src/Mini.Tests/Golden/`. A change there is a behavior change.
- **Traceable.** Every C# type carries a `// Python: <file>.py <Name>` tag, so a Python change can be traced to its C# counterpart. `src/docs/PYTHON-MAP.md` lists the mapping. A test (`PythonReferenceTests`) checks that every type has a tag.
- **Python idioms replaced by .NET equivalents.**

  | Python | .NET |
  |---|---|
  | `anthropic` SDK | official `Anthropic` NuGet package |
  | `mcp` (stdio client) | `ModelContextProtocol.Core` |
  | `argparse` | `System.CommandLine` |
  | `json` dicts | `System.Text.Json.Nodes` |
  | `subprocess`, `signal` | `Process`, `PosixSignalRegistration` |
  | `shlex`, `difflib`, `fnmatch` | small hand-written helpers in `Mini.Core/Common` |

- **Split into layers.** The single Python package became two projects:
  - `Mini.Core` holds all the behavior. It never touches the console, reads the environment in one place (`MiniOptionsBinder`) and gets time from an injected `TimeProvider`.
  - `Mini.Cli` is the composition root and the terminal: option parsing, the REPL, slash commands, signal handling.
- **Tested with fakes.** The C# port adds an xUnit v3 suite (`Mini.Tests`) that uses a scripted fake model client. Python tests that have no C# port yet are listed in `src/Mini.Tests/NOT-PORTED.md`.
- **Known gaps are deliberate.** For example, there is no `start_new_session` for child processes, no readline history on Unix, and `fnmatch` supports only `*` and `?`. They are listed in `src/docs/MIGRATION-PLAN.md`.

## Layout

```
src-original/   Python reference implementation
src/
  Mini.Core/    behavior: agent loop, tools, approval, sessions, models
  Mini.Cli/     console app (assembly name: mini)
  Mini.Tests/   xUnit v3 tests and golden files
  docs/         ARCHITECTURE, MIGRATION-PLAN, REFACTOR-PLAN, PYTHON-MAP
```

## Build and test

Requires the .NET 10 SDK.

```sh
cd src
dotnet build Mini.slnx
dotnet test --solution Mini.slnx
dotnet run --project Mini.Cli
```

## Further reading

- [Architecture](src/docs/ARCHITECTURE.md): projects, layers, how a turn runs, invariants
- [Migration plan](src/docs/MIGRATION-PLAN.md): feasibility, decisions, accepted gaps
- [Refactor plan](src/docs/REFACTOR-PLAN.md): design decisions and history
- [Python map](src/docs/PYTHON-MAP.md): Python name → C# type
