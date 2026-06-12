namespace Delta.DocGen.CLI;

public sealed record CliArgs(
    string                ConfigPath,
    string?               Root,
    string?               Output,
    IReadOnlyList<string> Excludes,
    string?               Verbosity,
    bool                  DryRun,
    bool                  NoExcludeConfig);
