// Python: cli.py print_goal
namespace Mini.Cli.Commands;

/// <summary>Prints a goal's status, with control characters taken out of what the model wrote.</summary>
public static class GoalPrinter
{
    /// <summary>"No goal." for null.</summary>
    // Python: cli.py print_goal
    public static void Print(TextWriter output, Goal? g) => output.WriteLine(g is null ? "No goal."
        : Printable.Control.Replace($"Goal [{g.Status.Wire()}] {g.Objective}\n  {g.Turns} turns ({g.MaxTurns} per run)"
            + (g.Note.Length > 0 ? $" · {g.Note}" : ""), ""));
}
