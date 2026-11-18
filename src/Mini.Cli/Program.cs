using Microsoft.Extensions.DependencyInjection;
using Mini.Cli.Hosting;

// Python: none (C#-only: the entry point and composition root; Python's is __main__.py, which calls main)

MiniOptions options;
try
{
    options = MiniOptionsBinder.FromEnvironment();
}
catch (FormatException e) // MINI_COMPACT_AT is not a number
{
    Console.Error.WriteLine($"MINI_COMPACT_AT: {e.Message}");
    return 1;
}

await using var services = new ServiceCollection().AddMiniCore(options).AddMiniCli().BuildServiceProvider();
return await MiniApp.RunAsync(args, services);
