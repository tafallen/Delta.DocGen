namespace Delta.DocGen.Logging;

/// <summary>No-op logger for use in unit tests.</summary>
public sealed class NullDocGenLogger : IDocGenLogger
{
    public static readonly NullDocGenLogger Instance = new();

    public void Info(string message) { }
    public void Verbose(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
    public void Error(string message, Exception ex) { }
    public void Summary(string message) { }
}
