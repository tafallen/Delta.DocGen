namespace Delta.DocGen.Model;

/// <summary>
/// Intermediate step record produced by the C# scanner.
/// Domain and Id are assigned in later pipeline stages.
/// </summary>
public sealed record RawStep(
    string Type,           // Given | When | Then
    string Pattern,        // Raw string from the attribute argument
    IReadOnlyList<ParamRecord> Params,
    string File,           // Relative path to .cs file
    int Line,              // 1-based line number of the attribute
    string Source          // Verbatim C# method body text
);
