namespace Mini.Core.Configuration;

/// <summary>models.json (or a model's entry in it) is missing something the harness needs.</summary>
// Python: none (C#-only: Python lets the KeyError or RuntimeError of a bad models.json escape)
public sealed class ModelConfigurationException(string message) : MiniException(message);
