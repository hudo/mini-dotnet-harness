namespace Mini.Tests.Fakes;

/// <summary>Redirects Console.In (scripted lines), Console.Out and Console.Error for its lifetime; restores them on dispose.</summary>
public sealed class ConsoleCapture : IDisposable
{
    readonly TextReader _in = Console.In;
    readonly TextWriter _out = Console.Out, _err = Console.Error;
    readonly StringWriter _outText = new(), _errText = new();

    /// <summary>Starts capturing; each input line is returned by Console.ReadLine in turn, then end of input (null).</summary>
    public ConsoleCapture(params string[] inputLines)
    {
        Console.SetIn(new StringReader(string.Concat(inputLines.Select(l => l + "\n"))));
        Console.SetOut(_outText);
        Console.SetError(_errText);
    }

    /// <summary>Everything written to Console.Out so far.</summary>
    public string Out => _outText.ToString();

    /// <summary>Everything written to Console.Error so far.</summary>
    public string Err => _errText.ToString();

    /// <summary>Puts the original streams back.</summary>
    public void Dispose()
    {
        Console.SetIn(_in);
        Console.SetOut(_out);
        Console.SetError(_err);
    }
}
