namespace Mini.Tests.Prompts;

// C#-only: the substitution rules the goal and review prompts depend on (Python's str.format on GOAL_RULES, REVIEW, GOAL_OFF)
public class PromptTemplateTests
{
    [Fact]
    public void FillsEverySlotOfAName() => Assert.Equal("a-1 b-2 a-1", new PromptTemplate("{a}-{x} {b}-{y} {a}-{x}").Render(("a", "a"), ("x", "1"), ("b", "b"), ("y", "2")));

    [Fact]
    public void ASlotNobodyFillsStaysAsWritten() => Assert.Equal("hi {name}, {n}", new PromptTemplate("hi {name}, {n}").Render(("other", "x")));

    [Fact]
    public void ARepeatedNameTakesTheFirstValue() => Assert.Equal("one", new PromptTemplate("{a}").Render(("a", "one"), ("a", "two")));

    [Fact]
    public void ValuesAreNotScannedAgain() =>
        // one pass: the "{status}" inside the objective stays literal, and braces in a value are left alone
        Assert.Equal("{status} {x} done", new PromptTemplate("{objective} {x} {status}").Render(("objective", "{status}"), ("x", "{x}"), ("status", "done")));

    [Fact]
    public void LiteralBracesThatAreNotSlotsStay() => Assert.Equal("{} { a} {a b} {1x}{{", new PromptTemplate("{} { a} {a b} {1x}{{").Render(("a", "A"), ("a b", "B")));

    [Fact]
    public void RendersTheHarnessTemplates()
    {
        Assert.Equal("Goal mode is off (status: done). Earlier goal instructions no longer apply unless the user resumes the goal.",
            HarnessPrompts.GoalOff.Render(("status", "done")));
    }
}
