using System.Text.Json.Serialization;

namespace Mini.Core.Agents.Goals;

/// <summary>Where a goal stands; the session file keeps the lowercase name.</summary>
// Python: loop.py Agent.set_goal, Agent.run_goal (the goal's "status" strings)
[JsonConverter(typeof(JsonStringEnumConverter<GoalStatus>))]
public enum GoalStatus
{
    [JsonStringEnumMemberName("active")] Active,
    [JsonStringEnumMemberName("paused")] Paused,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("cleared")] Cleared,
}
