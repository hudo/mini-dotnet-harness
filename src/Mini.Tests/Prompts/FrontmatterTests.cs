namespace Mini.Tests.Prompts;

// Python: ext.py frontmatter (the SKILL.md header; the cases test_mini.py doesn't reach)
public class FrontmatterTests
{
    [Fact]
    public void ReadsKeyValueLinesAndStripsQuotes()
    {
        var f = Frontmatter.Parse("---\nname: \"release-notes\"\ndescription: 'Write notes'\n---\nbody: not a field");
        Assert.Equal(new Dictionary<string, string> { ["name"] = "release-notes", ["description"] = "Write notes" }, f);
    }

    [Theory]
    [InlineData(">-")]
    [InlineData("|")]
    [InlineData(">")]
    [InlineData("|-")]
    public void FoldedBlocksDropTheIndicatorAndJoinLinesWithSpaces(string indicator)
    {
        var f = Frontmatter.Parse($"---\nname: x\ndescription: {indicator}\n  Review a diff.\n\tUse before merging.\n---\n");
        Assert.Equal(" Review a diff. Use before merging.".Trim(), f["description"]);
    }

    [Fact]
    public void AWrappedPlainValueKeepsItsFirstLine() => Assert.Equal("one two", Frontmatter.Parse("---\ndescription: one\n  two\n---\n")["description"]);

    [Fact]
    public void WorksWithCrLfAndWithoutAHeader()
    {
        Assert.Equal("x", Frontmatter.Parse("---\r\nname: x\r\n---\r\n")["name"]);
        Assert.Empty(Frontmatter.Parse("# no header\nname: x"));
        Assert.Empty(Frontmatter.Parse("---\nname: x\n")); // never closed
    }
}
