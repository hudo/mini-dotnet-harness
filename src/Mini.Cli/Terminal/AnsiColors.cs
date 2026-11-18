namespace Mini.Cli.Terminal;

/// <summary>The escape codes the console output uses.</summary>
// Python: cli.py DIM, YEL, CYA, RED, OFF
public static class AnsiColors
{
    public const string Dim = "\u001b[2m", Yel = "\u001b[33m", Cya = "\u001b[36m", Red = "\u001b[31m", Off = "\u001b[0m";
}
