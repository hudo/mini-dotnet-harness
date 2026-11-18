// Python: none (C#-only: Python's Path.resolve(), a library call there)
namespace Mini.Core.Security;

/// <summary>Path resolution that follows symlinks, so security checks see the real path. Pure: no state.</summary>
public static class PathResolver
{
    /// <summary>Python's Path.resolve(): every symlinked component is followed, so security checks see the real path.
    /// A worklist of pending parts: a link's raw target is pushed in front of the parts still to come, and `..` is applied
    /// to the already-resolved directory, so `link/..` leaves the link's target, not the link's folder. A target that
    /// doesn't exist (a dangling link, or a path not made yet) is kept as it is. More than 40 links on the way fail
    /// closed with an IOException (a loop, or a chain nobody wrote by hand).</summary>
    public static string RealPath(string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
        var seps = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var root = Path.GetPathRoot(full) ?? "";
        var current = root;
        var work = new Stack<string>();
        void Push(string rest)
        {
            var pieces = rest.Split(seps, StringSplitOptions.RemoveEmptyEntries);
            for (var i = pieces.Length - 1; i >= 0; i--) work.Push(pieces[i]); // the first is popped first
        }
        Push(full[root.Length..]);
        for (var links = 0; work.Count > 0;)
        {
            var part = work.Pop();
            if (part == ".")
                continue;
            if (part == "..")
            {
                current = Path.GetDirectoryName(current) ?? root; // at the root it stays there, as on disk
                continue;
            }
            var next = Path.Join(current, part);
            var target = new FileInfo(next).LinkTarget; // raw text, set for dangling links and for folders too
            if (target is null)
            {
                current = next;
                continue;
            }
            if (++links > 40)
                throw new IOException($"Too many levels of symbolic links: {path}");
            if (Path.IsPathRooted(target)) // an absolute target starts over; a relative one starts at the link's folder
            {
                root = Path.GetPathRoot(target) ?? "";
                if (OperatingSystem.IsWindows() && !root.Contains(':') && !root.StartsWith(@"\\", StringComparison.Ordinal))
                    root = Path.GetPathRoot(current) ?? root; // \x on Windows means the current drive
                current = root;
                Push(target[(Path.GetPathRoot(target) ?? "").Length..]);
            }
            else
            {
                Push(target);
            }
        }
        return current;
    }
}
