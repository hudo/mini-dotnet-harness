using System.Globalization;

// Python: context.py build_system
namespace Mini.Core.Prompts;

/// <summary>What one system prompt is built from.</summary>
/// <param name="Cwd">The working directory.</param>
/// <param name="Skills">The skills found for this directory.</param>
/// <param name="McpErrors">MCP servers that failed to start, with their errors.</param>
/// <param name="Sub">This is a subagent's prompt.</param>
/// <param name="Model">The model the session runs on.</param>
/// <param name="Delegate">The task tool is on: add the delegation paragraph.</param>
/// <param name="Approval">The sentence about approval in this session (<see cref="ApprovalStatement.Describe"/>).</param>
// Python: context.py build_system (its parameters)
public sealed record SystemPromptRequest(string Cwd, IReadOnlyList<Skill> Skills, IReadOnlyList<string> McpErrors, bool Sub, string Model,
    bool Delegate, string Approval);

/// <summary>
/// The system prompt, built once per session and then frozen.
///
/// Thinking blocks are bound to the exact system prompt and tool set they were
/// produced under. So the prompt is assembled at session start, stored in the
/// session log, and replayed byte for byte on resume. Anything that changes
/// later goes in as an appended role:"system" message instead.
/// </summary>
// Python: context.py build_system
public sealed class SystemPromptBuilder(ShellLocator shell, TimeProvider time)
{
    // Python: context.py build_system
    public string Build(SystemPromptRequest r)
    {
        var shellName = OperatingSystem.IsWindows() ? (shell.Bash is not null ? "Git Bash" : "cmd.exe") : shell.Bash is not null ? "bash" : "/bin/sh, not bash";
        // platform.system() spelled the way Python prints it.
        var system = OperatingSystem.IsMacOS() ? "Darwin" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsWindows() ? "Windows" : "";
        var started = time.GetLocalNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " " + time.LocalTimeZone.StandardName;
        var parts = new List<string>
        {
            r.Sub ? HarnessPrompts.Sub : HarnessPrompts.Base + (r.Delegate ? "\n\n" + HarnessPrompts.Delegate : ""),
            $"Environment: {system} (the bash tool runs {shellName}) · model {r.Model} · cwd {PathResolver.RealPath(r.Cwd)} · session started {started}\n{r.Approval}".Trim(),
        };
        if (PromptFile.ReadIf(Path.Combine(r.Cwd, "AGENTS.md")) is { Length: > 0 } agents)
            parts.Add($"<project_instructions file=\"AGENTS.md\">\n{agents}\n</project_instructions>");
        if (r.Sub)
            return string.Join("\n\n", parts);
        var index = PromptFile.ReadIf(Path.Combine(r.Cwd, "memory", "INDEX.md")); // without one, say so: models otherwise go looking for it
        parts.Add(HarnessPrompts.Memory + (index is { Length: > 0 }
            ? $"\n<memory_index file=\"memory/INDEX.md\">\n{index}\n</memory_index>"
            : "\nThere is no memory/ folder yet; create memory/INDEX.md when there is something to keep."));
        if (r.Skills.Count > 0)
        {
            var lines = string.Join("\n", r.Skills.Select(s => $"- {s.Name}: {s.Description} ({s.Path})"));
            parts.Add($"Skills: when a task matches one, read its SKILL.md first and follow it.\n{lines}");
        }
        if (r.McpErrors.Count > 0)
            parts.Add("MCP servers that failed to start: " + string.Join("; ", r.McpErrors));
        return string.Join("\n\n", parts);
    }
}
