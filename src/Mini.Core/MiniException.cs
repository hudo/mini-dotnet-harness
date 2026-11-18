namespace Mini.Core;

// Python: none (C#-only: base of the domain exceptions; Python raises RuntimeError and friends)
public class MiniException(string message, Exception? inner = null) : Exception(message, inner);
