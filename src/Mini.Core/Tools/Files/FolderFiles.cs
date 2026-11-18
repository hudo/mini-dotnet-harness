namespace Mini.Core.Tools.Files;

/// <summary>One folder of a walk and the files in it that match the glob.</summary>
// Python: none (C#-only: the (dir, files) pair os.walk yields, named)
public sealed record FolderFiles(string Dir, List<string> Files);
