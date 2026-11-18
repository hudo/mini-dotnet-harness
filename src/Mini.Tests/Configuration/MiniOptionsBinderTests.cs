namespace Mini.Tests.Configuration;

/// <summary>The one place the environment is read: each variable becomes its option.</summary>
// C#-only: Python reads os.environ where it needs a value; here the binder reads it once
public class MiniOptionsBinderTests
{
    static MiniOptions Bind(params (string Name, string? Value)[] vars)
    {
        var env = vars.ToDictionary(v => v.Name, v => v.Value);
        return MiniOptionsBinder.Bind(name => env.GetValueOrDefault(name), "/profile");
    }

    // C#-only: nothing set is every default
    [Fact]
    public void AnEmptyEnvironmentGivesTheDefaults()
    {
        var o = Bind();

        Assert.Null(o.NetHome);
        Assert.Null(o.Model);
        Assert.Null(o.CompactAt);
        Assert.False(o.NoTask);
        Assert.False(o.NoWeb);
        Assert.Null(o.Bash);
        Assert.False(o.HasAnthropicApiKey);
        Assert.Null(o.OpenRouterApiKey);
        Assert.False(o.HasOpenRouterApiKey);
        Assert.Null(o.Home);
        Assert.Null(o.UserProfile);
        Assert.Equal("/profile", o.UserProfileFolder);
        Assert.Null(o.SearchPath);
    }

    // C#-only: each variable lands in its option
    [Fact]
    public void EachVariableLandsInItsOption()
    {
        var o = Bind(("MINI_NET_HOME", "/h"), ("MINI_MODEL", "m"), ("MINI_COMPACT_AT", "500"), ("MINI_NO_TASK", "1"), ("MINI_NO_WEB", "1"),
            ("MINI_BASH", "/b"), ("ANTHROPIC_API_KEY", "k"), ("OPENROUTER_API_KEY", "or"), ("HOME", "/home/u"), ("USERPROFILE", "C:\\u"),
            ("PATH", "/a:/b"), ("ProgramFiles", "C:\\pf"), ("LOCALAPPDATA", "C:\\la"), ("SystemRoot", "C:\\win"));

        Assert.Equal("/h", o.NetHome);
        Assert.Equal("m", o.Model);
        Assert.Equal(500, o.CompactAt);
        Assert.True(o.NoTask);
        Assert.True(o.NoWeb);
        Assert.Equal("/b", o.Bash);
        Assert.True(o.HasAnthropicApiKey);
        Assert.Equal("or", o.OpenRouterApiKey);
        Assert.True(o.HasOpenRouterApiKey);
        Assert.Equal("/home/u", o.Home);
        Assert.Equal("C:\\u", o.UserProfile);
        Assert.Equal("/a:/b", o.SearchPath);
        Assert.Equal("C:\\pf", o.ProgramFiles);
        Assert.Equal("C:\\la", o.LocalAppData);
        Assert.Equal("C:\\win", o.SystemRoot);
    }

    // C#-only: MINI_NO_TASK and MINI_NO_WEB switch on only for exactly "1", as the loop compared them
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("true", false)]
    public void TheSwitchesMeanExactlyOne(string value, bool on)
    {
        var o = Bind(("MINI_NO_TASK", value), ("MINI_NO_WEB", value));

        Assert.Equal(on, o.NoTask);
        Assert.Equal(on, o.NoWeb);
    }

    // C#-only: an empty key is not a key (the CLI tested for a non-empty one)
    [Fact]
    public void AnEmptyKeyCountsAsNone()
    {
        var o = Bind(("ANTHROPIC_API_KEY", ""), ("OPENROUTER_API_KEY", ""));

        Assert.False(o.HasAnthropicApiKey);
        Assert.False(o.HasOpenRouterApiKey);
    }

    // C#-only: an empty MINI_MODEL stays "", as Config kept it (only an unset variable falls back to models.json)
    [Fact]
    public void AnEmptyModelIsKept() => Assert.Equal("", Bind(("MINI_MODEL", "")).Model);

    // C#-only: a bad MINI_COMPACT_AT fails at startup, as Python's int() does at import
    [Fact]
    public void ACompactAtThatIsNotANumberFails() =>
        Assert.Throws<FormatException>(() => Bind(("MINI_COMPACT_AT", "soon")));

    // C#-only: the options never show the OpenRouter key when printed
    [Fact]
    public void ToStringHidesTheKey()
    {
        var text = Bind(("OPENROUTER_API_KEY", "sk-or-secret")).ToString();

        Assert.DoesNotContain("sk-or-secret", text);
    }

    // C#-only: FromEnvironment reads the real process variables (the one test that sets any)
    [Fact]
    public void FromEnvironmentReadsTheProcessVariables()
    {
        using var env = new EnvScope(("MINI_MODEL", "from-env"), ("MINI_COMPACT_AT", "123"), ("MINI_NO_WEB", "1"));

        var o = MiniOptionsBinder.FromEnvironment();

        Assert.Equal("from-env", o.Model);
        Assert.Equal(123, o.CompactAt);
        Assert.True(o.NoWeb);
        Assert.Equal(TestEnvironment.Home, o.NetHome); // TestEnvironment set it for the run
    }
}
