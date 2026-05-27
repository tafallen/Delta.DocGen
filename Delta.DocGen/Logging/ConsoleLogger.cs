namespace Delta.DocGen.Logging;

/// <summary>
/// Verbosity levels:
///   silent  — Error + Summary only
///   normal  — Info + Warn + Error + Summary  (default)
///   verbose — all levels
/// </summary>
/// <remarks>
/// Single-threaded invariant: <see cref="Console.ForegroundColor"/> is set and reset within
/// the same synchronous call. Do not call this logger from concurrent threads; introduce a
/// lock or switch to ANSI escape codes before enabling parallel file scanning.
/// </remarks>
public sealed class ConsoleLogger : IDocGenLogger
{
    private readonly bool _silent;
    private readonly bool _verbose;

    public ConsoleLogger(string verbosity)
    {
        if (verbosity is not (LogVerbosity.Silent or LogVerbosity.Normal or LogVerbosity.Verbose))
            throw new ArgumentException(
                $"Unknown verbosity '{verbosity}'. Expected: silent | normal | verbose.",
                nameof(verbosity));

        _silent  = verbosity == LogVerbosity.Silent;
        _verbose = verbosity == LogVerbosity.Verbose;
    }

    public void Info(string message)
    {
        if (_silent) return;
        Console.WriteLine($"[INFO]    {message}");
    }

    public void Verbose(string message)
    {
        if (!_verbose) return;
        Console.WriteLine($"[VERBOSE] {message}");
    }

    public void Warn(string message)
    {
        if (_silent) return;
        Console.ForegroundColor = ConsoleColor.Yellow;
        try { Console.WriteLine($"[WARN]    {message}"); }
        finally { Console.ResetColor(); }
    }

    public void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        try { Console.Error.WriteLine($"[ERROR]   {message}"); }
        finally { Console.ResetColor(); }
    }

    public void Error(string message, Exception ex)
    {
        var detail = _verbose ? ex.ToString() : ex.Message;
        Error($"{message}: {detail}");
    }

    public void Summary(string message)
    {
        Console.WriteLine($"[DONE]    {message}");
    }
}
