namespace Delta.DocGen.Pipeline;

public sealed record PipelineResult(
    bool    Success,
    int     StepCount,
    int     DomainCount,
    int     CsFileCount,
    int     FeatureFileCount,
    int     UnmatchedStepCount,
    string? OutputPath,
    string? SchemaPath,
    string? Digest,
    long    ElapsedMs,
    string? ErrorMessage);
