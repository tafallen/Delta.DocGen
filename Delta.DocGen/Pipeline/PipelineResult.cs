namespace Delta.DocGen.Pipeline;

/// <summary>
/// Outcome of a pipeline run. All count fields are non-negative by contract;
/// negative values indicate a bug in the producing stage.
/// </summary>
public sealed record PipelineResult(
    bool            Success,
    int             StepCount,
    int             DomainCount,
    int             CsFileCount,
    int             FeatureFileCount,
    int             UnmatchedStepCount,
    string?         OutputPath,
    string?         SchemaPath,
    string?         Digest,
    long            ElapsedMs,
    string?         ErrorMessage,
    FailureCategory FailureCategory = FailureCategory.None);
