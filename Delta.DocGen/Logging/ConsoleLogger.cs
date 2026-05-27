namespace Delta.DocGen.Logging;

/// <summary>
/// Verbosity levels:
///   silent  — Error + Summary only
///   normal  — Info + Warn + Error + Summary  (default)
///   verbose — all levels
/// </summary>
public sealed class ConsoleLogger(string verbosity) : IDocGenLogger
{
    private readonly bool _silent  = verbosity == "silent";
    private readonly bool _verbose = verbosity == "verbose";

    public void Info(string message)
    {
        if (_silent) return;
        Console.WriteLine($"[INFO]  {message}");
    }

    public void Verbose(string message)
    {
        if (!_verbose) return;
        Console.WriteLine($"[VERB]  {message}");
    }

    public void Warn(string message)
    {
        if (_silent) return;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN]  {message}");
        Console.ResetColor();
    }

    public void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] {message}");
        Console.ResetColor();
    }

    public void Summary(string message)
    {
        Console.WriteLine($"[DONE]  {message}");
    }
}
