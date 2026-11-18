using System.Runtime.InteropServices;

// Python: cli.py main (the exit cleanup: atexit; SIGTERM and SIGHUP)
namespace Mini.Cli.Hosting;

/// <summary>A kill or a closed terminal exits the normal way, so <c>ProcessExit</c> stops what bash left running.</summary>
public sealed class ProcessSignals : IDisposable
{
    readonly List<PosixSignalRegistration> _registrations = [];

    /// <summary>Stops the background jobs when the process ends, and turns SIGTERM and SIGHUP into an exit with code 128 + the signal number.</summary>
    public ProcessSignals(JobRegistry jobs)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => jobs.StopAll(); // what bash left running ends with mini
        // shortcut: unlike Python, SIGHUP is taken over even under nohup; upgrade if nohup runs must survive a hangup.
        if (!OperatingSystem.IsWindows())
        {
            foreach (var (signal, n) in new[] { (PosixSignal.SIGTERM, 15), (PosixSignal.SIGHUP, 1) })
                _registrations.Add(PosixSignalRegistration.Create(signal, ctx => { ctx.Cancel = true; Environment.Exit(128 + n); }));
        }
    }

    /// <summary>Gives the signals back to the runtime.</summary>
    public void Dispose()
    {
        foreach (var r in _registrations) r.Dispose();
    }
}
