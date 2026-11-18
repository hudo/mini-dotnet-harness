// Python: tools.py LocalTools (the class: each of its methods is the tool that ports it; the registry is the lookup that was getattr)
namespace Mini.Core.Tools;

/// <summary>The tools of one agent, by name.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    /// <param name="tools">Every tool the agent can run, whether or not its definition is in the session's tool list.</param>
    public ToolRegistry(IEnumerable<ITool> tools)
    {
        foreach (var tool in tools)
            _tools.Add(tool.Definition.Name, tool);
    }

    public bool TryGet(string name, out ITool tool) => _tools.TryGetValue(name, out tool!);

    /// <summary>The tool with that name; throws if there is none.</summary>
    public ITool Get(string name) => _tools[name];
}
