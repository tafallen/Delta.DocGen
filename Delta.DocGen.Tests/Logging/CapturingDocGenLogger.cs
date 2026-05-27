using Delta.DocGen.Logging;

namespace Delta.DocGen.Tests.Logging;

/// <summary>Test logger that stores all messages for assertion.</summary>
public sealed class CapturingDocGenLogger : IDocGenLogger
{
    public List<string> InfoMessages    { get; } = [];
    public List<string> VerboseMessages { get; } = [];
    public List<string> WarnMessages    { get; } = [];
    public List<string> ErrorMessages   { get; } = [];
    public List<string> SummaryMessages { get; } = [];

    public void Info(string message)    => InfoMessages.Add(message);
    public void Verbose(string message) => VerboseMessages.Add(message);
    public void Warn(string message)    => WarnMessages.Add(message);
    public void Error(string message)   => ErrorMessages.Add(message);
    public void Error(string message, Exception ex) => ErrorMessages.Add($"{message}: {ex.Message}");
    public void Summary(string message) => SummaryMessages.Add(message);
}
