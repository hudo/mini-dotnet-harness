namespace Mini.Tests.Configuration;

/// <summary>Reading and merging models.json, and the two options that override it.</summary>
public class ModelCatalogLoaderTests
{
    static string Shipped => Path.Combine(AppContext.BaseDirectory, "models.json");

    static string TempHome() => Directory.CreateTempSubdirectory("mini-tests-catalog-").FullName;

    // C#-only: MINI_MODEL replaces "main"; the sub-agent models stay
    [Fact]
    public void TheModelOptionReplacesTheMainModel()
    {
        var shipped = ModelCatalogLoader.Load(new MiniOptions { NetHome = TempHome() }, Shipped);
        var chosen = ModelCatalogLoader.Load(new MiniOptions { NetHome = TempHome(), Model = "deepseek-flash" }, Shipped);

        Assert.Equal("deepseek-flash", chosen.Main);
        Assert.NotEqual("deepseek-flash", shipped.Main);
        Assert.Equal(shipped.SubModel, chosen.SubModel);
        Assert.Equal(shipped.CheapModel, chosen.CheapModel);
    }

    // C#-only: MINI_COMPACT_AT replaces the Claude default
    [Fact]
    public void TheCompactAtOptionReplacesTheClaudeDefault()
    {
        var shipped = ModelCatalogLoader.Load(new MiniOptions { NetHome = TempHome() }, Shipped);
        var chosen = ModelCatalogLoader.Load(new MiniOptions { NetHome = TempHome(), CompactAt = 77 }, Shipped);

        Assert.Equal(77, chosen.CompactAt);
        Assert.NotEqual(77, shipped.CompactAt);
    }

    // C#-only: the user's models.json under the net home adds models and changes only the fields it names
    [Fact]
    public void TheUsersModelsJsonIsMergedOverTheShippedOne()
    {
        var home = TempHome();
        File.WriteAllText(Path.Combine(home, "models.json"),
            """{"subagent": "mine", "models": {"claude-haiku-5-5": {"window": 12345}, "mine": {"window": 7}}}""");

        var catalog = ModelCatalogLoader.Load(new MiniOptions { NetHome = home }, Shipped);

        Assert.Equal("mine", catalog.SubModel);
        Assert.Equal(12345, catalog.For("claude-haiku-5-5").Window);
        Assert.NotEmpty(catalog.For("claude-haiku-5-5").Prices); // not named: kept
        Assert.Equal(7, catalog.For("mine").Window);
    }

    // C#-only: the messages of a models.json that is not usable
    [Fact]
    public void AModelsJsonWithoutWhatTheHarnessNeedsSaysWhat()
    {
        var dir = TempHome();
        var path = Path.Combine(dir, "models.json");
        var options = new MiniOptions { NetHome = TempHome() };

        File.WriteAllText(path, "[]");
        Assert.Equal($"{path}: expected a JSON object", Assert.Throws<ModelConfigurationException>(() => ModelCatalogLoader.Load(options, path)).Message);
        File.WriteAllText(path, "{}");
        Assert.Equal("models.json has no models object", Assert.Throws<ModelConfigurationException>(() => ModelCatalogLoader.Load(options, path)).Message);
        File.WriteAllText(path, """{"models": {}}""");
        Assert.Equal("models.json: missing main", Assert.Throws<ModelConfigurationException>(() => ModelCatalogLoader.Load(options, path)).Message);
        File.WriteAllText(path, """{"main": "a", "subagent": "a", "subagent_cheap": "a", "models": {}}""");
        Assert.Equal("models.json has no claude-* default", Assert.Throws<ModelConfigurationException>(() => ModelCatalogLoader.Load(options, path)).Message);
    }
}
