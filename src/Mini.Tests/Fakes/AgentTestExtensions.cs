using System.Text.Json.Nodes;

namespace Mini.Tests.Fakes;

/// <summary>How a test reads an agent's frozen tool list as the JSON the API and the session file carry.</summary>
// Python test helper: tests/test_mini.py (agent.tools, the list of dicts)
public static class AgentTestExtensions
{
    public static JsonArray ToolsJson(this Agent agent) => new(agent.Tools.Select(t => (JsonNode?)t.ToJson()).ToArray());
}
