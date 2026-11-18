using System.IO.Enumeration;

// Python: tools.py LocalTools.find (the os.walk and fnmatch part)
namespace Mini.Core.Tools.Files;

/// <summary>os.walk for the find tool: folders in sorted order, each with the files matching a glob.</summary>
public static class DirectoryWalker
{
    // The names a folder holds, split the way os.walk does: subfolders worth entering (sorted; not dot-folders,
    // dependency folders or reparse points, which are symlinked folders) and files matching the glob (sorted).
    // shortcut: only * and ? in `name`, no fnmatch [seq] / [!seq] classes (FileSystemName has none); upgrade when models
    // pass character classes and get "no matches" for them.
    // Python: tools.py LocalTools.find (the os.walk and fnmatch part)
    private static FolderListing Scan(string dir, string name)
    {
        List<string> dirs = [], files = [];
        try
        {
            foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if (e.Attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!e.Attributes.HasFlag(FileAttributes.ReparsePoint) && !e.Name.StartsWith('.') && e.Name is not ("node_modules" or "__pycache__"))
                        dirs.Add(e.Name);
                }
                else if (FileSystemName.MatchesSimpleExpression(name, e.Name, ignoreCase: OperatingSystem.IsWindows()))
                {
                    files.Add(e.Name);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // unreadable: skip it, keep the rest
        dirs.Sort(StringComparer.Ordinal);
        files.Sort(StringComparer.Ordinal);
        return new(dirs, files);
    }

    // Python: tools.py LocalTools.find (the os.walk part)
    private static IEnumerable<FolderFiles> WalkDir(string dir, string name)
    {
        var (dirs, files) = Scan(dir, name);
        yield return new(dir, files);
        foreach (var d in dirs)
        {
            foreach (var r in WalkDir(Path.Combine(dir, d), name))
                yield return r;
        }
    }

    /// <summary>Every folder under root (or the one file, if root is a file) with its matching files.</summary>
    // Python: tools.py LocalTools.find (the os.walk part, and a root that is a file)
    public static IEnumerable<FolderFiles> Walk(string root, string name)
    {
        if (Directory.Exists(root)) return WalkDir(root, name);
        var f = Path.GetFileName(root);
        var files = new List<string>();
        if (FileSystemName.MatchesSimpleExpression(name, f, ignoreCase: OperatingSystem.IsWindows())) files.Add(f);
        return new[] { new FolderFiles(Path.GetDirectoryName(root)!, files) };
    }
}
