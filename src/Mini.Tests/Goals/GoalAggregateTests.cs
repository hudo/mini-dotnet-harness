using System.Text.Json;

namespace Mini.Tests.Goals;

/// <summary>C#-only: the goal's own state changes and its session-file shape, on the type that now owns them.</summary>
public class GoalAggregateTests
{
    [Fact]
    public void AGoalStartsPausedWithNothingSpent()
    {
        var goal = new Goal("ship it", maxTurns: 7, verify: "make test", budget: 1.5);

        Assert.Equal((GoalStatus.Paused, 0, 7, 0.0, ""), (goal.Status, goal.Turns, goal.MaxTurns, goal.Spent, goal.Note));
        Assert.Equal(("make test", 1.5), (goal.Verify, goal.Budget));
    }

    [Fact]
    public void ActivatePauseCompleteAndClearMoveTheStatusAndKeepItsNoteInStep()
    {
        var goal = new Goal("o");
        goal.Pause("blocked: waiting for the user");
        Assert.Equal((GoalStatus.Paused, "blocked: waiting for the user"), (goal.Status, goal.Note));

        goal.Activate();
        Assert.Equal((GoalStatus.Active, ""), (goal.Status, goal.Note)); // a run starting forgets why the last one stopped

        goal.Complete(new string('é', 600));
        Assert.Equal(GoalStatus.Done, goal.Status);
        Assert.Equal(500, goal.Note.CodePointLength()); // the note keeps the start of the evidence

        goal.Clear();
        Assert.Equal((GoalStatus.Cleared, 500), (goal.Status, goal.Note.CodePointLength())); // clearing leaves the note as it was
    }

    [Fact]
    public void RecordTurnCountsTheTurnAndSetsTheSpendAndTheBudgetIsPerRun()
    {
        var goal = new Goal("o", spent: 2.0, budget: 1.0);

        goal.RecordTurn(2.4);
        Assert.Equal((1, 2.4), (goal.Turns, goal.Spent));
        Assert.False(goal.IsOverBudget(spentAtStart: 2.0)); // 0.4 of 1.0 this run
        goal.RecordSpent(3.0); // a review's cost
        Assert.Equal((1, 3.0), (goal.Turns, goal.Spent));
        Assert.True(goal.IsOverBudget(spentAtStart: 2.0));
        Assert.False(new Goal("o", budget: 0).IsOverBudget(0)); // zero means no limit
        Assert.False(new Goal("o").IsOverBudget(0));
    }

    [Fact]
    public void AGoalSerializesInTheFilesKeyOrderAndReadsBackWithEveryField()
    {
        var goal = new Goal("ship it", GoalStatus.Active, turns: 3, maxTurns: 5, verify: "true", budget: 2.5, spent: 0.125, note: "n");

        var json = JsonSerializer.Serialize(goal);

        Assert.Equal("""{"objective":"ship it","status":"active","turns":3,"max_turns":5,"verify":"true","budget":2.5,"spent":0.125,"note":"n"}""", json);
        var back = JsonSerializer.Deserialize<Goal>(json)!;
        Assert.Equal(json, JsonSerializer.Serialize(back));
        Assert.Equal((GoalStatus.Active, 3, 5, "true", 2.5, 0.125, "n"), (back.Status, back.Turns, back.MaxTurns, back.Verify, back.Budget, back.Spent, back.Note));
    }

    [Fact]
    public void AGoalLineWithMissingFieldsGetsTheDefaultsAnUnknownStatusIsRejected()
    {
        var sparse = JsonSerializer.Deserialize<Goal>("""{"objective":"o"}""")!;

        Assert.Equal((GoalStatus.Paused, 0, 25, null, null, 0.0, ""), (sparse.Status, sparse.Turns, sparse.MaxTurns, sparse.Verify, sparse.Budget, sparse.Spent, sparse.Note));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Goal>("""{"objective":"o","status":"running"}"""));
    }
}
