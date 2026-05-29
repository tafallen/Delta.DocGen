using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

/// <summary>A single parameter on a step definition.</summary>
/// <param name="Name">Parameter name as declared in the C# method signature.</param>
/// <param name="Type">Schema type: string | int | decimal | bool | date | docstring | table.</param>
/// <param name="Example">Default example value; empty until LLM enrichment (v2).</param>
/// <param name="Columns">For Table parameters, the list of column name+type pairs (declared from
/// <c>CreateSet&lt;T&gt;</c>/<c>CreateInstance&lt;T&gt;</c> when same-file, else inferred from observed
/// feature-file usage). <c>null</c> for non-Table parameters; serialised when present.</param>
public sealed record ParamRecord(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("example")] string Example,
    [property: JsonPropertyName("columns")] IReadOnlyList<ColumnRecord>? Columns = null
);
