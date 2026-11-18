using System.CommandLine;

// Python: cli.py main (argparse)
namespace Mini.Cli.Commands;

/// <summary>Builds the command line's grammar and runs a handler on what it parsed. The flags, help texts and exit codes are argparse's.</summary>
public static class RootCommandFactory
{
    /// <summary>The root command; its action binds the options to a <see cref="CliOptions"/> and calls <paramref name="run"/>.</summary>
    public static RootCommand Create(Func<CliOptions, Task<int>> run)
    {
        var prompt = new Option<string?>("--prompt", "-p") { Description = "run one prompt headless and print the answer" };
        var cont = new Option<bool>("--continue", "-c") { Description = "continue the latest session" };
        var resume = new Option<string?>("--resume", "-r") { Description = "resume a session by id prefix" };
        var model = new Option<string?>("--model") { Description = "main model (default from models.json)" };
        var effort = new Option<string>("--effort")
        {
            Description = "low, medium, high, xhigh or max (DeepSeek: low, high, max)",
            DefaultValueFactory = _ => "high",
        };
        var models = new Option<bool>("--models") { Description = "list the models you can use, then exit" };
        var permissions = new Option<string?>("--permissions")
        {
            Description = "who approves risky calls: you, Jev (a decision model; needs OPENROUTER_API_KEY), or nobody. "
                + "Default: judge when that key is set, else ask; headless runs default to ask",
        };
        permissions.AcceptOnlyFromAmong("ask", "judge", "yes");
        var allow = new Option<string[]>("--allow")
        {
            Description = "pre-approve bash commands starting with these words, or MCP tools starting with PREFIX "
                + "(repeatable). Allowing a test runner lets the model run code it wrote",
            HelpName = "PREFIX",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false, // one value per occurrence, like argparse's action="append"; `--allow a b` leaves b unmatched (an error)
        };
        var goal = new Option<string?>("--goal") { Description = "headless: work until this objective is done" };
        var verify = new Option<string?>("--verify") { Description = "with --goal: command that must pass before the goal counts as done" };
        var maxTurns = new Option<int>("--max-turns") { DefaultValueFactory = _ => 25 };
        var budget = new Option<double>("--budget") { Description = "with --goal: stop after this many dollars" };
        var json = new Option<bool>("--json") { Description = "headless: print a JSON result line at the end" };

        var root = new RootCommand("A minimal agent harness.");
        foreach (var o in new Option[] { prompt, cont, resume, model, effort, models, permissions, allow, goal, verify, maxTurns, budget, json })
            root.Options.Add(o);
        if (root.Options.OfType<VersionOption>().FirstOrDefault() is { } version) root.Options.Remove(version); // argparse has none
        root.SetAction((p, _) => run(new CliOptions(
            Prompt: p.GetValue(prompt), Continue: p.GetValue(cont), Resume: p.GetValue(resume), Model: p.GetValue(model),
            Effort: p.GetValue(effort)!, ListModels: p.GetValue(models), Permissions: p.GetValue(permissions),
            Allow: p.GetValue(allow) ?? [], Goal: p.GetValue(goal), Verify: p.GetValue(verify), MaxTurns: p.GetValue(maxTurns),
            Budget: p.GetResult(budget) is null ? null : p.GetValue(budget), Json: p.GetValue(json))));
        return root;
    }

    /// <summary>Parses <paramref name="argv"/> and runs <paramref name="run"/>; a parse error prints argparse's line and gives 2.</summary>
    public static async Task<int> InvokeAsync(string[] argv, Func<CliOptions, Task<int>> run)
    {
        // argparse takes "@x" literally; the default would read it as a response file
        var parsed = Create(run).Parse(argv, new ParserConfiguration { ResponseFileTokenReplacer = null });
        if (parsed.Errors.Count > 0) // argparse: message on stderr, exit 2
        {
            foreach (var e in parsed.Errors) Console.Error.WriteLine($"mini: error: {e.Message}");
            return 2;
        }
        return await parsed.InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null }); // our handler is the only one
    }
}
