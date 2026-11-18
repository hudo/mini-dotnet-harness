// Python: loop.py Agent.__init__ (self.effort)
namespace Mini.Core.Agents;

/// <summary>The effort the next requests ask for. One setting, shared by the agent (the CLI changes it), its turn runner (which sends it) and
/// its subagent runner (which works one level below the lead's).</summary>
public sealed class EffortSetting(string value)
{
    public string Value { get; set; } = value;
}
