using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Python: tools.py LocalTools.read (the `p.is_file()` check and its "Not a regular file." error)
namespace Mini.Core.Tools.Files;

/// <summary>Reads a file only if it is a regular one, so a device or a FIFO can't hang or fill memory.</summary>
public static class RegularFileReader
{
    /// <summary>Opens a regular file for reading, else throws ToolException("Not a regular file."). The type is checked on the
    /// opened descriptor, which is then read, so the file can't be swapped in between. O_NONBLOCK so a FIFO opens at once
    /// instead of waiting for a writer. Windows has no FIFOs here: it opens as before.</summary>
    // Python: tools.py LocalTools.read (the `p.is_file()` check and its "Not a regular file." error)
    public static FileStream OpenRegular(string p)
    {
        if (OperatingSystem.IsWindows())
            return new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // O_RDONLY | O_NONBLOCK | O_CLOEXEC; the flags differ between macOS and Linux
        var fd = NativeMethods.Open(p, OperatingSystem.IsMacOS() ? 0x4 | 0x1000000 : 0x800 | 0x80000);
        if (fd < 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            throw errno == 13 ? new UnauthorizedAccessException($"Access to '{p}' is denied.") // EACCES
                : errno == 2 ? new FileNotFoundException($"Could not find file '{p}'.") : new IOException($"Can't open '{p}' (errno {errno}).");
        }
        var handle = new SafeFileHandle(fd, ownsHandle: true);
        unsafe
        {
            var status = stackalloc byte[256];
            if (NativeMethods.FStat(fd, status) != 0 || (*(int*)(status + 4) & 0xF000) != 0x8000) // S_IFMT, S_IFREG
            {
                handle.Dispose();
                throw new ToolException("Not a regular file.");
            }
        }
        return new FileStream(handle, FileAccess.Read);
    }

    /// <summary>At most `cap` bytes, else null: a device or a file that grows can't fill memory.</summary>
    public static byte[]? ReadCapped(string p, long cap)
    {
        using var f = OpenRegular(p);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = f.Read(buf, 0, buf.Length)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > cap) return null;
        }
        return ms.ToArray();
    }
}
