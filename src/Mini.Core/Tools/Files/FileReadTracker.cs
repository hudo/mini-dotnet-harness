// Python: tools.py LocalTools.__init__ (self.seen)
namespace Mini.Core.Tools.Files;

/// <summary>When the model last read or wrote each file (by its modification time), so a write or edit over a version it never saw is refused.</summary>
public sealed class FileReadTracker
{
    // path -> mtime when the model last read or wrote it
    private readonly Dictionary<string, DateTime> _seen = new(PathParts.Comparer);

    public bool TryGet(string path, out DateTime mtime) => _seen.TryGetValue(path, out mtime);

    public void Record(string path, DateTime mtime) => _seen[path] = mtime;

    /// <summary>Records the file's current modification time.</summary>
    public void RecordCurrent(string path) => _seen[path] = MtimeOf(path);

    public static DateTime MtimeOf(string path) => File.GetLastWriteTimeUtc(path);
}
