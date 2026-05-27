namespace Delta.DocGen.Model;

/// <summary>
/// Intermediate step record produced by Stage 3 (C# scanner).
/// <see cref="Domain"/> starts empty and is populated by Stage 5 (DomainAssigner) via a <c>with</c> expression.
/// </summary>
/// <param name="Type">Step attribute type.</param>
/// <param name="Pattern">Raw Cucumber Expression string from the attribute argument.</param>
/// <param name="Params">Parameters extracted from the C# method signature.</param>
/// <param name="File">Forward-slash relative path to the .cs file containing this step.</param>
/// <param name="Line">1-based line number of the step attribute.</param>
/// <param name="Source">Full method text: all attribute lists + signature + body, as returned by Roslyn's method.ToString().</param>
/// <param name="Domain">Domain assigned by Stage 5; empty string until then.</param>
public sealed record RawStep(
    StepType Type,
    string Pattern,
    IReadOnlyList<ParamRecord> Params,
    string File,
    int Line,
    string Source,
    string Domain = ""
);
