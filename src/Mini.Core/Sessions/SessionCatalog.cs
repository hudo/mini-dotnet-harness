using System.Globalization;
using System.Security.Cryptography;

// Python: session.py Session.create, Session.find, sessions, list_sessions
namespace Mini.Core.Sessions;

/// <summary>The sessions of one project directory: make a new one, find one to resume, list them.</summary>
public sealed class SessionCatalog(MiniPaths paths, TimeProvider time)
{
    // Python: session.py Session.create
    public ISessionStore Create(string cwd, string? name = null)
    {
        var sid = string.IsNullOrEmpty(name)
            ? time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + RandomNumberGenerator.GetHexString(4, lowercase: true)
            : name;
        return new JsonlSessionStore(Path.Combine(paths.ProjectDir(cwd), sid + ".jsonl"), time);
    }

    /// <summary>The session file at <paramref name="path"/> (a subagent's log, or one to resume).</summary>
    // Python: session.py Session.__init__
    public ISessionStore Open(string path) => new JsonlSessionStore(path, time);

    /// <summary>Latest session, or the latest one whose id starts with prefix.</summary>
    // Python: session.py Session.find
    public ISessionStore? Find(string cwd, string? prefix = null)
    {
        var files = Files(cwd)
            .Where(f => Path.GetFileNameWithoutExtension(f).StartsWith(prefix ?? "", StringComparison.Ordinal))
            .ToList();
        return files.Count > 0 ? Open(files[0]) : null;
    }

    /// <summary>Top-level sessions for this directory, newest first (subagent logs excluded).</summary>
    // Python: session.py sessions
    List<string> Files(string cwd) =>
        Directory.EnumerateFiles(paths.ProjectDir(cwd), "*.jsonl")
            .Where(p => !Path.GetFileNameWithoutExtension(p).Contains("-sub", StringComparison.Ordinal))
            .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
            .ToList();

    /// <summary>(id, prompts, cost, title from the first prompt, last updated, UTC), newest first.</summary>
    // Python: session.py list_sessions
    public List<SessionSummary> List(string cwd, int limit = 15)
    {
        var rows = new List<SessionSummary>();
        foreach (var p in Files(cwd).Take(limit))
        {
            var (first, turns, cost) = ("", 0, 0.0);
            foreach (var e in Open(p).Read())
            {
                if (e is MessageEvent { Message: { Text: { } c } message, ByHarness: false } && message.Role == Role.User)
                {
                    turns++;
                    first = first.Length > 0 ? first : c;
                }
                else if (e is UsageEvent u)
                {
                    cost += u.Cost;
                }
            }
            var title = string.Join(" ", first.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            // Count code points like Python's len(), so an emoji is never cut in half.
            if (title.CodePointLength() > 50)
                title = title.TakeScalars(49).TrimEnd() + "…";
            rows.Add(new SessionSummary(Path.GetFileNameWithoutExtension(p), turns, cost, title, File.GetLastWriteTimeUtc(p)));
        }
        return rows;
    }
}
