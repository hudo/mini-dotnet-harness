using System.Runtime.InteropServices;

// Python: none (C#-only: .NET has no public regular-file test or O_NONBLOCK open; Python's Path.is_file() and open() have them)
namespace Mini.Core.Tools.Files;

/// <summary>The two libc/runtime exports <see cref="RegularFileReader"/> needs. POSIX only; never called on Windows.</summary>
internal static partial class NativeMethods
{
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int Open(string path, int flags);

    // shortcut: .NET has no public regular-file test (FileInfo.Attributes is Normal for a FIFO and for /dev/null), so the type
    // comes from the runtime's own libSystem.Native export, whose status struct is the same on every Unix (flags, then mode).
    // An internal export that could move; upgrade to a libc fstat per OS if it does.
    [LibraryImport("libSystem.Native", EntryPoint = "SystemNative_FStat")]
    internal static unsafe partial int FStat(nint fd, byte* status);
}
