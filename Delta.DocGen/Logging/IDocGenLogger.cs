namespace Delta.DocGen.Logging;

public interface IDocGenLogger
{
    void Info(string message);
    void Verbose(string message);
    void Warn(string message);
    void Error(string message);
    void Error(string message, Exception ex);
    void Summary(string message);
}
