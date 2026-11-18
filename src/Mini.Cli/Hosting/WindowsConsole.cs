using System.Runtime.InteropServices;
using System.Text;

// Python: cli.py main (the Windows console branch)
namespace Mini.Cli.Hosting;

/// <summary>The classic Windows console shows color codes as text until asked to interpret them.</summary>
public static partial class WindowsConsole
{
    /// <summary>Turns on virtual-terminal processing for stdout and stderr, and UTF-8 output. Does nothing elsewhere.</summary>
    public static void EnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows()) return;
        Console.OutputEncoding = new UTF8Encoding(false); // a pipe on Windows defaults to the locale's code page
        foreach (var handle in new[] { GetStdHandle(-11), GetStdHandle(-12) }) // stdout, stderr
            if (GetConsoleMode(handle, out var mode)) SetConsoleMode(handle, mode | 0x4); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
    }

    [LibraryImport("kernel32")]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);
}
