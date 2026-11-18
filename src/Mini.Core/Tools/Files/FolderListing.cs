namespace Mini.Core.Tools.Files;

/// <summary>The subfolders worth entering and the files matching the glob in one folder, each sorted.</summary>
// Python: none (C#-only: the (dirs, files) pair os.walk yields, named)
public sealed record FolderListing(List<string> Dirs, List<string> Files);
