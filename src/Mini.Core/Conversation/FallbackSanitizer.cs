// Python: loop.py clean
namespace Mini.Core.Conversation;

/// <summary>
/// The one place a model's blocks are not appended as returned (AGENTS.md): after a mid-stream fallback, the declined model's
/// unpaired calls and thinking before the boundary are dropped, as Anthropic's fallback guide requires.
/// </summary>
public static class FallbackSanitizer
{
    public static List<ContentBlock> Clean(IReadOnlyList<ContentBlock> blocks)
    {
        var cut = -1;
        for (var i = 0; i < blocks.Count; i++)
            if (blocks[i] is FallbackBlock) cut = i;
        var answered = blocks.Select(b => b.Field("tool_use_id")).OfType<string>().ToHashSet();
        bool Keep(ContentBlock b) =>
            b is TextBlock || b.Type.Value.EndsWith("_tool_result", StringComparison.Ordinal)
            || (b is ServerToolUseBlock { Id: { } id } && answered.Contains(id));
        // The fallback marker is optional to echo (the guide says keep or drop); dropping it keeps the history plain.
        return blocks.Take(Math.Max(cut, 0)).Where(Keep).Concat(blocks.Skip(cut + 1)).ToList();
    }
}
