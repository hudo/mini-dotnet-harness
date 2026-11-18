namespace Mini.Core.Models;

/// <summary>Marks an exception that came from our own callbacks (a closed stdout, a broken observer), not the network.</summary>
// Python: none (C#-only: Python's `transient` can tell its own failures apart by type; here the SDK passes the HTTP library's IOException through, so ours are marked)
internal static class OwnFailure
{
    /// <summary>The <see cref="Exception.Data"/> key of a marked exception.</summary>
    internal const string Own = "mini.own";

    /// <summary>Whether <paramref name="e"/> was thrown by a callback of ours.</summary>
    internal static bool IsOurs(Exception e) => e.Data.Contains(Own);

    /// <summary>Run a callback of ours (a UI write, an observer): whatever it throws is our failure, never retried.</summary>
    internal static void Ours(Action call)
    {
        try { call(); }
        catch (Exception e) { e.Data[Own] = true; throw; }
    }
}
