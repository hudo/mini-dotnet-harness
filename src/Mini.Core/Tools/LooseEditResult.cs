namespace Mini.Core.Tools;

/// <summary>The outcome of a whitespace-insensitive edit: the edited text if exactly one place matched (else null), and how many places matched.</summary>
// Python: tools.py loose_edit (the (text, hits) it returns)
public sealed record LooseEditResult(string? Text, int Hits);
