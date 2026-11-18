using System.Text.RegularExpressions;

// Python: loop.py Agent.verify
namespace Mini.Core.Agents.Goals;

/// <summary>The worker doesn't grade itself: the verify command must pass, then a reviewer with a fresh context agrees.</summary>
/// <param name="goal">The goal being checked.</param>
/// <param name="claim">What the model claimed in goal_done.</param>
/// <param name="shell">Runs the verify command.</param>
/// <param name="cwd">Where the command runs.</param>
/// <param name="subagents">Runs the reviewer.</param>
/// <param name="ui">Where "verifying" is noted.</param>
/// <param name="timeoutSeconds">How long the verify command may run (<see cref="AgentOptions.GoalVerifyTimeoutSeconds"/>: 600).</param>
public sealed partial class GoalVerifier(GoalSlot goal, GoalClaimSlot claim, IShellRunner shell, string cwd, ISubagentRunner subagents, IAgentOutput ui,
    int timeoutSeconds = 600)
{
    // Python: loop.py Agent.verify
    public async Task<GoalVerification> VerifyAsync(CancellationToken ct = default)
    {
        var g = goal.Value!;
        var cmd = g.Verify;
        if (!string.IsNullOrEmpty(cmd))
        {
            ui.Note($"verifying: {cmd}");
            int? code;
            string output;
            try
            {
                (code, output) = await shell.RunAsync(cmd, cwd, timeoutSeconds, keep: false, ct); // killed at the timeout: a verdict needs the command to end
            }
            catch (TimeoutException)
            {
                return new GoalVerification(false, $"`{cmd}` timed out after {timeoutSeconds}s.");
            }
            if (code != 0)
                return new GoalVerification(false, $"The harness ran `{cmd}` and it failed:\n{output.TakeLastScalars(4000)}");
        }
        ui.Note("a fresh reviewer is checking the goal");
        var review = await subagents.RunAsync(HarnessPrompts.Review.Render(("objective", g.Objective), ("claim", (claim.Value ?? "").TakeScalars(4000)),
            ("verified", string.IsNullOrEmpty(cmd) ? "" : $" The harness already ran `{cmd}` and it passed.")), null, ct);
        var lines = LineSplitter.Split(review.Trim());
        var last = VerdictNoise().Replace(lines.Count > 0 ? lines[^1] : "", " ").Trim().ToUpperInvariant();
        return new GoalVerification(last == "VERDICT: DONE", $"A reviewer checked and disagrees:\n{review.TakeLastScalars(4000)}");
    }

    // Markdown around the reviewer's last line (**VERDICT: DONE**, `VERDICT: DONE`, runs of whitespace) is not part of the verdict.
    [GeneratedRegex(@"[*_`\s]+")]
    private static partial Regex VerdictNoise();
}
