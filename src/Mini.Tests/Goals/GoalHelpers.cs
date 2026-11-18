using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Mini.Tests.Goals;

/// <summary>Helpers shared by the goal, reviewer and subagent tests.</summary>
internal static class GoalHelpers
{
    /// <summary>Python's <c>agent.set_goal(objective, verify=, max_turns=, budget=)</c>: max_turns defaults to 25, budget to none.</summary>
    public static Task<Goal> SetGoal(Agent agent, string objective, string? verify = null, int maxTurns = 25, double? budget = null) =>
        agent.SetGoalAsync(objective, verify, maxTurns, budget, TestContext.Current.CancellationToken);

    /// <summary>The messages of a request body, as a list.</summary>
    public static List<JsonObject> Messages(JsonObject request) => request["messages"]!.AsArray().Select(m => m!.AsObject()).ToList();

    /// <summary>The system-role messages of a request, in order.</summary>
    public static List<JsonObject> SystemMessages(JsonObject request) =>
        Messages(request).Where(m => Json.Str(m["role"]) == "system").ToList();

    /// <summary>The user messages whose content is a plain string (not tool results).</summary>
    public static List<JsonObject> StringUserMessages(JsonObject request) =>
        Messages(request).Where(m => Json.Str(m["role"]) == "user" && m["content"] is JsonValue).ToList();

    /// <summary>The tool names of a request body.</summary>
    public static List<string?> ToolNames(JsonObject request) => request["tools"]!.AsArray().Select(t => Json.Str(t!["name"])).ToList();

    /// <summary>A goal as Python's tests spell one: not yet run.</summary>
    public static Goal PausedGoal(string objective, GoalStatus status = GoalStatus.Paused, int turns = 0) =>
        new(objective, status, turns, maxTurns: 5);

    /// <summary>Is the process (or a process of that id) still there? <c>kill -0</c> succeeds for any live process we may signal.</summary>
    public static bool Alive(int pid)
    {
        using var p = Process.Start(new ProcessStartInfo("kill", ["-0", pid.ToString()])
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })!;
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    /// <summary>Python's <c>gone</c>: has the process ended (orphans are reaped by init, so give it a moment)?</summary>
    public static async Task<bool> Gone(int pid, double withinSeconds = 5)
    {
        var end = Stopwatch.GetTimestamp() + (long)(withinSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            if (!Alive(pid)) return true;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        return false;
    }
}
