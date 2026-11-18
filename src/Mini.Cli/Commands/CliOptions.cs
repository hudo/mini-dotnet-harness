// Python: cli.py main (the parsed argparse namespace)
namespace Mini.Cli.Commands;

/// <summary>What the command line asked for.</summary>
/// <param name="Prompt">-p: run one prompt headless.</param>
/// <param name="Continue">-c: continue the latest session.</param>
/// <param name="Resume">-r: resume a session by id prefix.</param>
/// <param name="Model">--model: the main model; null: the catalog's.</param>
/// <param name="Effort">--effort.</param>
/// <param name="ListModels">--models: list the models, then exit.</param>
/// <param name="Permissions">--permissions: ask, judge or yes; null: by default (see <see cref="MiniCommand"/>).</param>
/// <param name="Allow">--allow: pre-approved command or MCP tool prefixes.</param>
/// <param name="Goal">--goal: headless, work until this objective is done.</param>
/// <param name="Verify">--verify: the command that must pass before a goal counts as done.</param>
/// <param name="MaxTurns">--max-turns.</param>
/// <param name="Budget">--budget: dollars; null when not given.</param>
/// <param name="Json">--json: headless, print a JSON result line at the end.</param>
public sealed record CliOptions(string? Prompt, bool Continue, string? Resume, string? Model, string Effort, bool ListModels,
    string? Permissions, IReadOnlyList<string> Allow, string? Goal, string? Verify, int MaxTurns, double? Budget, bool Json)
{
    /// <summary>A prompt or a goal was given: no REPL, and nobody to ask.</summary>
    public bool Headless => !string.IsNullOrEmpty(Prompt) || !string.IsNullOrEmpty(Goal);
}
