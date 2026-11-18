namespace Mini.Core.Models;

/// <summary>A model's API key (or the environment it needs to be safe to use) is not there.</summary>
// Python: none (C#-only: Python raises a bare RuntimeError from need_key)
public sealed class MissingCredentialsException(string message) : MiniException(message);
