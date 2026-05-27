namespace Delta.DocGen.Model;

/// <summary>
/// Intermediate step record produced by the C# scanner.
/// Domain and Id are assigned in later pipeline stages.
/// </summary>
/// <param name="Type">Given | When | Then</param>
/// <param name="Pattern">Raw string from the attribute argument.</param>
/// <param name="Params">Parameters extracted from the C# method signature.</param>
/// <param name="File">Relative path to the .cs file containing this step.</param>
/// <param name="Line">1-based line number of the step attribute.</param>
/// <param name="Source">Verbatim C# method body text.</param>
public sealed record RawStep(
    string Type,
    string Pattern,
    IReadOnlyList<ParamRecord> Params,
    string File,
    int Line,
    string Source
);
