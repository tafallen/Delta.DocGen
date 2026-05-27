using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

/// <summary>Fully resolved step — all pipeline stages complete.</summary>
public sealed record StepRecord(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("type")]        StepType Type,
    [property: JsonPropertyName("pattern")]     string Pattern,
    [property: JsonPropertyName("params")]      IReadOnlyList<ParamRecord> Params,
    [property: JsonPropertyName("file")]        string File,
    [property: JsonPropertyName("line")]        int Line,
    [property: JsonPropertyName("domain")]      string Domain,
    [property: JsonPropertyName("tags")]        IReadOnlyList<string> Tags,
    [property: JsonPropertyName("used")]        int Used,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("source")]      string Source,
    [property: JsonPropertyName("suggestsNext")]IReadOnlyList<string> SuggestsNext
);
