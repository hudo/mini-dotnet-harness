using System.Text.Json.Nodes;
using Mini.Tests.Fakes;

namespace Mini.Tests.Extensions;

/// <summary>Skills and the MCP stdio hub: ports of the "extensions" tests in tests/test_mini.py.</summary>
public class ExtensionsTests
{
    // Python test: test_skills_are_listed_in_system_prompt
    [Fact]
    public void SkillsAreListedInSystemPrompt()
    {
        using var ws = new Workspace();
        var file = ws.Write("skills/release-notes/SKILL.md",
            "---\nname: release-notes\ndescription: Write release notes from git log\n---\nSteps...");

        var skills = ws.FindSkills();

        Assert.Equal([new Skill("release-notes", "Write release notes from git log", file)], skills);
        var (agent, _) = ws.Make([], skills: skills);
        Assert.Contains("release-notes: Write release notes from git log", agent.System);
    }

    // Python test: test_mcp_stdio_server_round_trip
    [Fact]
    public async Task McpStdioServerRoundTrip()
    {
        using var ws = new Workspace();
        var (command, args) = ExtensionsHelpers.EchoServer();
        ws.Write("mcp.json", Json.Obj(("mcpServers", Json.Obj(("echo", Json.Obj(("command", command),
            ("args", new JsonArray(args.Select(a => (JsonNode)a).ToArray()))))))).ToJsonString());

        await using var hub = await ws.StartMcpAsync();

        Assert.Empty(hub.Errors);
        Assert.True(hub.Has("mcp__echo__shout"));
        var (text, isError) = await hub.CallAsync("mcp__echo__shout", Json.Obj(("text", "hi")), TestContext.Current.CancellationToken);
        Assert.Equal("HI", text);
        Assert.False(isError);
    }

    // Python test: test_broken_mcp_server_does_not_stop_startup
    [Fact]
    public async Task BrokenMcpServerDoesNotStopStartup()
    {
        using var ws = new Workspace();
        ws.Write("mcp.json", """{"mcpServers": {"nope": {"command": "/does/not/exist"}}}""");

        await using var hub = await ws.StartMcpAsync();

        Assert.NotEmpty(hub.Errors);
        Assert.StartsWith("nope", hub.Errors[0]);
    }

    // Python test: test_malformed_mcp_json_is_reported
    [Fact]
    public async Task MalformedMcpJsonIsReported()
    {
        using var ws = new Workspace();
        ws.Write("mcp.json", "{not json");

        await using var hub = await ws.StartMcpAsync();

        Assert.NotEmpty(hub.Errors);
        Assert.Contains("not a valid MCP config", hub.Errors[0]);
    }

    // Python test: test_wrongly_shaped_mcp_json_is_reported
    [Fact]
    public async Task WronglyShapedMcpJsonIsReported()
    {
        using var ws = new Workspace();
        ws.Write("mcp.json", """{"mcpServers": []}""");

        await using var hub = await ws.StartMcpAsync();

        Assert.Contains("must be an object", hub.Errors[0]);
    }

    // Python test: test_skills_follow_the_agent_skills_format_and_claude_code_folders
    [Fact]
    public void SkillsFollowTheAgentSkillsFormatAndClaudeCodeFolders()
    {
        using var ws = new Workspace();
        ws.Write(".claude/skills/release-notes/SKILL.md",
            "---\nname: release-notes\ndescription: >-\n  Write release notes from merged PRs.\n"
            + "  Use when the user asks for a changelog.\nlicense: MIT\n---\n# Steps\n");
        ws.Write("skills/plain/SKILL.md", "---\nname: plain\ndescription: \"Quoted one-liner\"\n---\n");

        var found = ws.FindSkills().ToDictionary(s => s.Name);

        Assert.Equal("Write release notes from merged PRs. Use when the user asks for a changelog.", found["release-notes"].Description);
        Assert.Equal("Quoted one-liner", found["plain"].Description);
    }

    // Python test: test_remote_mcp_server_is_reported_not_hung
    [Fact]
    public async Task RemoteMcpServerIsReportedNotHung()
    {
        using var ws = new Workspace();
        ws.Write(".mcp.json", """{"mcpServers": {"linear": {"type": "http", "url": "https://mcp.linear.app/mcp"}}}""");

        await using var hub = await ws.StartMcpAsync();

        Assert.NotEmpty(hub.Errors);
        Assert.Contains("mcp-remote", hub.Errors[0]);
        Assert.Empty(hub.Schemas);
    }
}
