namespace Mini.Tests.GoldenMaster;

/// <summary>Parity contract item 1: the approval statement the model is told, for each mode.</summary>
public class ApprovalGoldenTests
{
    /// <summary>The approver for a named mode; the same names label the golden files.</summary>
    public static ApprovalSettings Make(string mode) => mode switch
    {
        "ask" => new ApprovalSettings(),
        "ask-allow" => new ApprovalSettings(allow: ["git commit", "npm test", "make 'test all'"]),
        "judge" => new ApprovalSettings(useJudge: true),
        "judge-headless" => new ApprovalSettings(useJudge: true, interactive: false),
        "judge-allow" => new ApprovalSettings(useJudge: true, allow: ["git commit"]),
        "yes" => new ApprovalSettings(auto: true),
        "yes-allow" => new ApprovalSettings(auto: true, allow: ["git commit"]), // auto wins: --allow is not mentioned
        "headless" => new ApprovalSettings(interactive: false),
        "headless-allow" => new ApprovalSettings(interactive: false, allow: ["git commit", "npm test"]),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    // C#-only: the Approval text per mode, one golden file each
    [Theory]
    [InlineData("ask")]
    [InlineData("ask-allow")]
    [InlineData("judge")]
    [InlineData("judge-headless")]
    [InlineData("judge-allow")]
    [InlineData("yes")]
    [InlineData("yes-allow")]
    [InlineData("headless")]
    [InlineData("headless-allow")]
    public void ApprovalText(string mode) =>
        GoldenHelpers.Assert($"approval.{mode}.txt", GoldenHelpers.WithNewline(ApprovalStatement.Describe(Make(mode))));
}
