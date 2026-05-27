namespace Delta.DocGen.Logging;

public static class LogPhrases
{
    /// <summary>
    /// Prefix used by UsageCounter when warning about a feature step that did not
    /// match any extracted step pattern. PipelineRunner counts occurrences of this
    /// phrase to populate <see cref="Pipeline.PipelineResult.UnmatchedStepCount"/>.
    /// </summary>
    public const string UnmatchedStep = "Unmatched step in";
}
