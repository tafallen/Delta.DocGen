namespace Delta.DocGen.Model;

/// <summary>A single parameter on a step definition.</summary>
/// <param name="Name">Parameter name as declared in the C# method signature.</param>
/// <param name="Type">Schema type: string | int | decimal | DocString.</param>
/// <param name="Example">Default example value; empty until LLM enrichment (v2).</param>
public sealed record ParamRecord(string Name, string Type, string Example);
