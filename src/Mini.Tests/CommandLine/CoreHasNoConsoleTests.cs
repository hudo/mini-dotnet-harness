namespace Mini.Tests.CommandLine;

/// <summary>The core never touches the terminal: it shows its work through <see cref="IAgentOutput"/> and asks through <see cref="IUserInteraction"/>.</summary>
public class CoreHasNoConsoleTests
{
    // C#-only: REFACTOR-PLAN D2 (the compiler-visible form: no reference to System.Console is compiled into Mini.Core)
    [Fact]
    public void MiniCoreDoesNotReferenceSystemConsole()
    {
        var references = typeof(Agent).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("System.Console", references);
    }
}
