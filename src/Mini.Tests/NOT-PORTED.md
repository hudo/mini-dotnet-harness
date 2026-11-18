# Python tests not ported, or only partly ported (Phase 0; updated in Phases 2 and 7)

The Python test suite is `tests/test_mini.py`. Each C# test that ports one carries a `// Python test: test_<name>` comment. To list the ports that are missing:

```
grep -rhoE "// Python test: test_[a-z0-9_]+" dotnet/tests | sed 's|// Python test: ||' | sort -u > /tmp/ported
grep -oE "^def (test_[a-z0-9_]+)" tests/test_mini.py | sed 's/def //' | sort -u | comm -23 - /tmp/ported
```

C#-only tests carry a `// C#-only: <why>` comment instead.

Phase 2 ported `test_deepseek_prices_and_search`, `test_deepseek_off_peak_halves_tokens_not_search` and `test_deepseek_peak_hours_at_their_boundaries`
in full: `PriceSchedule` takes a `TimeProvider`, so they run at the Python test's fixed instants (`CostTests`, `PriceScheduleTests`).

Phase 7 ported the four that P5/P6 unblocked (`MiniAppTests`: the headless goal exit code, the refused headless run and the judge default, through
`MiniApp.RunAsync` and `MiniCommand` with a scripted model client injected as an `IAgentFactory`; `GoalTests`: the verify timeout, now
`AgentOptions.GoalVerifyTimeoutSeconds`). It also completed the judge tests (`JevJudgeTests`, through a fake `HttpMessageHandler`) and added the
REPL signal cases of the left-running test (`RealMiniProcessTests`, a real `mini` process on a local server that plays the API).
Of the 182 Python tests, 176 are ported (172 before Phase 7), and 6 are not (below).

## Not ported

| Python test | Reason | Unblocked by |
|---|---|---|
| `test_under_nohup_mini_keeps_running_through_a_hangup` | Accepted gap: the C# code always installs its SIGHUP handler, even under nohup (`ProcessSignals`). The real-process harness exists now (`RealMiniProcessTests`). | Accepted gap |
| `test_kill_9_of_a_jobs_shell_takes_what_ignores_sigterm_with_it` | Accepted gap: there is no process group, so a dead shell's group can't be killed. | Accepted gap P5 |
| `test_windows_bash_is_git_bash_and_takes_its_paths` | Windows-only. | A Windows run |
| `test_background_jobs_say_they_need_posix` | Windows-only. | A Windows run |
| `test_on_windows_a_timeout_kills_the_command_and_leaves_no_log` | Windows-only. | A Windows run |
| `test_on_windows_the_timeout_kills_the_tree_with_taskkill` | Python monkeypatches `WINDOWS` and `subprocess.run`, and `Shell.Windows` has no seam. | P4, `IShellLauncher` |

## Ported in part

| Python test | What is missing | Unblocked by |
|---|---|---|
| `test_windows_aliases_of_protected_files_ask` | Only the case-insensitive half is ported. The `name.`, trailing-space, `:stream` and `GIT~1` aliases are Windows-only. | A Windows run |
| `test_what_a_timeout_left_running_ends_with_mini_but_background_jobs_stay` | `return`, `exit` (in process) and the REPL cases `repl`, `sigterm`, `sighup` (a real process) are ported. The `ctrl-c` case, a KeyboardInterrupt out of a script that runs tools directly, has no counterpart: C# runs tools only through the model. | Nothing: covered by the REPL cases |
| `test_at_exit_the_whole_group_dies_even_if_its_shell_goes_first_or_the_wait_is_cut_short` | The in-process cases (what ignores SIGTERM dies with the shell's tree) are ported. The second-signal cases (`SIGINT`, `SIGTERM` during the cleanup wait) are moot: `JobRegistry.StopAll` kills at once, so there is no wait to cut short, and orphans that left the shell's tree need process groups. | Accepted gap P5 |

## Skipped as accepted gaps (`Skip = "C# differs from Python: …"`)

- `AnEndedJobThatLeftSomethingRunningSaysSo`: the C# code has no process group, so it never reports that what the job started is still running (MIGRATION-PLAN, accepted gap P4).
- `AtExitWhatWasLeftRunningGetsSigtermFirstAndSigkillIfItStays`: `JobRegistry.StopAll` kills at once, with no SIGTERM first (MIGRATION-PLAN §325, §533).
