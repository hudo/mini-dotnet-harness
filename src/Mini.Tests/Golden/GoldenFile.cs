using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Mini.Tests;

/// <summary>Golden-master files under <c>tests/Mini.Tests/Golden/</c> in the source tree, compared byte for byte.</summary>
public static class GoldenFile
{
    static string Dir([CallerFilePath] string here = "") => System.IO.Path.GetDirectoryName(here)!; // this file lives in Golden/

    /// <summary>
    /// Fails unless <paramref name="actual"/> equals the file <paramref name="name"/> (UTF-8, no BOM). With
    /// <c>MINI_UPDATE_GOLDEN=1</c> the file is (re)written instead and the assertion passes.
    /// </summary>
    public static void Assert(string name, string actual)
    {
        var path = System.IO.Path.Combine(Dir(), name);
        var bytes = new UTF8Encoding(false).GetBytes(actual);
        if (Environment.GetEnvironmentVariable("MINI_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return;
        }
        if (!File.Exists(path))
            Xunit.Assert.Fail($"Golden file {name} does not exist. Run with MINI_UPDATE_GOLDEN=1 to create it from the current output.");
        var expected = File.ReadAllBytes(path);
        if (expected.AsSpan().SequenceEqual(bytes))
            return;
        var want = Encoding.UTF8.GetString(expected).Split('\n');
        var got = actual.Split('\n');
        var line = 0;
        while (line < want.Length && line < got.Length && want[line] == got[line]) line++;
        string Show(string[] lines) => line < lines.Length ? lines[line] : "<end of text>";
        Xunit.Assert.Fail($"Golden file {name} differs at line {line + 1}:\n  expected: {Show(want)}\n  actual:   {Show(got)}\n"
            + "If the change is intended, rerun with MINI_UPDATE_GOLDEN=1 and review the diff.");
    }
}
