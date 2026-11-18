using System.Text.Json.Nodes;
using Xunit;

namespace Mini.Tests;

/// <summary>Assertions for the append-only transcript invariant.</summary>
// Python test helper: tests/test_mini.py assert_append_only
public static class TranscriptAssert
{
    /// <summary>Each request must repeat the previous one's system, tools and messages unchanged (compared as JSON text).</summary>
    public static void AssertAppendOnly(IReadOnlyList<JsonObject> requests)
    {
        for (var n = 0; n + 1 < requests.Count; n++)
        {
            JsonObject a = requests[n], b = requests[n + 1];
            Assert.Equal(a["system"]?.ToJsonString(), b["system"]?.ToJsonString());
            Assert.Equal(a["tools"]?.ToJsonString(), b["tools"]?.ToJsonString());
            var before = a["messages"]!.AsArray();
            var after = b["messages"]!.AsArray();
            Assert.True(after.Count >= before.Count, $"request {n + 1} has fewer messages ({after.Count}) than request {n} ({before.Count})");
            for (var i = 0; i < before.Count; i++)
                Assert.Equal(before[i]?.ToJsonString(), after[i]?.ToJsonString());
        }
    }
}
