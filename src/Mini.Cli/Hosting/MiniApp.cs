using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

// Python: cli.py main (startup: the Windows console, SIGTERM and SIGHUP, the exit cleanup)
namespace Mini.Cli.Hosting;

/// <summary>The process around the command line: culture and encoding, the console, signals and Ctrl-C, then the parsed command.</summary>
public static class MiniApp
{
    /// <summary>Runs on the services the composition root built; returns the exit code.</summary>
    public static async Task<int> RunAsync(string[] argv, IServiceProvider services)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var utf8 = new UTF8Encoding(false);
        Console.OutputEncoding = utf8; // stdout and stderr; a pipe on Windows defaults to the code page
        Console.InputEncoding = utf8;
        WindowsConsole.EnableVirtualTerminal();
        using var signals = new ProcessSignals(services.GetRequiredService<JobRegistry>());
        using var ctrlC = services.GetRequiredService<ConsoleCancellation>().Install();
        return await RootCommandFactory.InvokeAsync(argv, options => RunCommandAsync(services, options));
    }

    static async Task<int> RunCommandAsync(IServiceProvider services, CliOptions options)
    {
        MiniCommand command;
        try // a models.json that won't load fails here
        {
            command = services.GetRequiredService<MiniCommand>();
        }
        catch (MiniException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        return await command.ExecuteAsync(options);
    }
}
