using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

public sealed record DomainRecord(
    [property: JsonPropertyName("id")]    string Id,
    [property: JsonPropertyName("label")] string Label
);
