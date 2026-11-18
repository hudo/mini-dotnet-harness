using System.Text.RegularExpressions;

namespace Mini.Core.Prompts;

/// <summary>
/// A prompt with <c>{name}</c> slots, filled like str.format for our prompts: one pass, so a value containing
/// "{status}" is never substituted again; a slot nobody fills stays as written; a repeated name takes the first value.
/// Not string.Format: the texts hold literal braces.
/// </summary>
// Python: none (C#-only: Python's str.format, which GOAL_RULES, REVIEW and GOAL_OFF go through)
public sealed partial record PromptTemplate(string Text)
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Slot();

    public string Render(params (string Name, string Value)[] values) =>
        Slot().Replace(Text, m =>
        {
            foreach (var (name, value) in values)
                if (name == m.Groups[1].Value) return value;
            return m.Value;
        });

    public override string ToString() => Text;
}
