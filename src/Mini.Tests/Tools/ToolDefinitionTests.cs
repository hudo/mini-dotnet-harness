using Mini.Tests.Fakes;
using static Mini.Tests.Tools.ToolsHelpers;

namespace Mini.Tests.Tools;

public class ToolDefinitionTests
{
    // Python test: test_local_tools_are_strict_and_explain_their_pitfalls
    [Fact]
    public void LocalToolsAreStrictAndExplainTheirPitfalls()
    {
        using var ws = new Workspace();
        var (agent, _) = ws.Make([]);
        var tools = agent.ToolsJson().ToDictionary(t => Json.Str(t!["name"])!, t => t!.AsObject());

        foreach (var name in new[] { "read", "write", "edit", "bash", "task", "goal_done" })
        {
            Assert.True(tools[name]["strict"]!.GetValue<bool>(), name);
            var schema = tools[name]["input_schema"]!;
            Assert.False(schema["additionalProperties"]!.GetValue<bool>(), name);
            var props = schema["properties"]!.AsObject().Select(p => p.Key).ToHashSet();
            Assert.Subset(props, schema["required"]!.AsArray().Select(r => Json.Str(r)!).ToHashSet());
        }
        Assert.Contains("line-number", Json.Str(tools["edit"]["description"]));
        Assert.Contains("can't see this conversation", Json.Str(tools["task"]["description"]));
        Assert.Contains("no input", Json.Str(tools["bash"]["description"]));
    }

    // Python test: test_windows_aliases_of_protected_files_ask
    // Only the case-insensitive half runs off Windows: `package.json.`, `package.json ` (trailing dot or space), `notes.txt:hidden`
    // (an NTFS stream) and `GIT~1` (an 8.3 name) are rejected by an OperatingSystem.IsWindows() branch in LocalTools.IsInside,
    // so on macOS and Linux those are ordinary file names and write freely (see the next test). The Windows half is not written.
    [Theory]
    [InlineData("sub/PACKAGE.JSON")]
    [InlineData("sub/Package.Json")]
    [InlineData("GNUMAKEFILE")]
    [InlineData("SUB/.GIT/hooks/pre-commit")]
    public async Task CaseAliasesOfProtectedFilesAsk(string alias)
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Path("sub"));

        var e = await Assert.ThrowsAsync<ToolException>(() => Headless(ws).WriteAsync(alias, "x", TestContext.Current.CancellationToken));

        Assert.Matches("denied", e.Message);
        Assert.False(File.Exists(ws.Path(alias)));
    }

    // C#-only: the characterization of the guard above: off Windows these names are not aliases of anything, so they write freely.
    [Theory]
    [InlineData("sub/package.json.")]
    [InlineData("notes.txt:hidden")]
    [InlineData("GIT~1/notes.txt")]
    public async Task WindowsAliasNamesAreOrdinaryNamesOffWindows(string name)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "off Windows only");
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Path("sub"));

        await Headless(ws).WriteAsync(name, "x", TestContext.Current.CancellationToken);

        Assert.Equal("x", File.ReadAllText(ws.Path(name)));
    }
}
