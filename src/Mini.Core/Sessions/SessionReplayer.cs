using System.Runtime.ExceptionServices;

// Python: session.py Session.load
namespace Mini.Core.Sessions;

/// <summary>Rebuilds a conversation's state from its log. A compaction starts a new segment: everything before the latest one is skipped.</summary>
public static class SessionReplayer
{
    /// <summary>Replays the store's whole log.</summary>
    public static SessionSnapshot Replay(ISessionStore store) => Replay(store.Read());

    // Python: session.py Session.load
    public static SessionSnapshot Replay(IEnumerable<SessionEvent> events)
    {
        MetaEvent? meta = null;
        Goal? goal = null;
        var messages = new List<Message>();
        double cost = 0;
        long context = 0;
        foreach (var e in events)
        {
            switch (e)
            {
                case UnreadableEvent u: // a conversation rebuilt without a line would silently be a different one
                    ExceptionDispatchInfo.Capture(u.Error).Throw();
                    break;
                case MetaEvent m:
                    meta = m;
                    break;
                case MessageEvent m:
                    messages.Add(m.Message);
                    break;
                case MessagesEvent m:
                    messages.AddRange(m.Messages);
                    break;
                case CompactEvent c:
                    messages = [.. c.Messages];
                    context = 0;
                    break;
                case GoalEvent g:
                    goal = g.Goal.Status != GoalStatus.Cleared ? g.Goal : null;
                    break;
                case UsageEvent u:
                    cost += u.Cost;
                    if (u.Context is { } size)
                        context = size;
                    break;
            }
        }
        return new SessionSnapshot(meta, messages, goal, cost, context);
    }
}
