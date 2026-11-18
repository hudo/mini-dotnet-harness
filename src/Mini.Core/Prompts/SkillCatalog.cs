// Python: ext.py find_skills
namespace Mini.Core.Prompts;

/// <summary>Agent Skills (SKILL.md + YAML header), in mini's folders and in the ones Claude Code and Codex use.</summary>
// Python: ext.py find_skills
public static class SkillCatalog
{
    // Python: ext.py find_skills
    public static List<Skill> Find(string cwd, MiniPaths paths)
    {
        string[] roots = [Path.Combine(cwd, "skills"), Path.Combine(cwd, ".claude", "skills"), Path.Combine(cwd, ".agents", "skills"), Path.Combine(paths.Home, "skills")];
        var skills = new List<Skill>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            // Sorted by folder name, as sorted(root.glob("*/SKILL.md")) compares path parts.
            foreach (var dir in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
            {
                var file = Path.Combine(dir, "SKILL.md");
                if (PromptFile.ReadIf(file, 4000) is not { } text)
                    continue;
                var fields = Frontmatter.Parse(text);
                skills.Add(new Skill(fields.GetValueOrDefault("name") ?? Path.GetFileName(dir), fields.GetValueOrDefault("description") ?? "", file));
            }
        }
        return skills;
    }
}
