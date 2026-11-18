namespace Mini.Core.Models;

/// <summary>The "thinking" key of a request: adaptive thinking with a summarized display and the thinking binding controls.</summary>
/// <param name="Type">The kind of thinking ("adaptive").</param>
/// <param name="Display">How thinking is shown ("summarized").</param>
/// <param name="PrefixMismatchBehavior">What the API does with a thinking block that no longer matches its prefix ("drop_block").</param>
// Python: loop.py Agent.request_args (the "thinking" key)
public sealed record ThinkingOptions(string Type, string Display, string PrefixMismatchBehavior)
{
    /// <summary>What every Claude request asks for.</summary>
    public static ThinkingOptions Adaptive { get; } = new("adaptive", "summarized", "drop_block");
}
