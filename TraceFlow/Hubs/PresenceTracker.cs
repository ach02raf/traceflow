using System.Collections.Concurrent;
namespace TraceFlow.Hubs;

public class PresenceTracker
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _users = new();

    /// <returns>true if this is the user's FIRST connection (= a real "login", not a 2nd tab)</returns>
    public bool Add(string user, string connId)
    {
        var first = false;
        _users.AddOrUpdate(user,
            _ => { first = true; return new HashSet<string> { connId }; },
            (_, s) => { lock (s) s.Add(connId); return s; });
        return first;
    }

    /// <returns>true if that was the user's LAST connection</returns>
    public bool Remove(string user, string connId)
    {
        if (!_users.TryGetValue(user, out var s)) return false;
        lock (s)
        {
            s.Remove(connId);
            if (s.Count == 0) { _users.TryRemove(user, out _); return true; }
        }
        return false;
    }

    public bool IsOnline(string user) => _users.ContainsKey(user);
    public string[] Online() => _users.Keys.OrderBy(x => x).ToArray();
}
