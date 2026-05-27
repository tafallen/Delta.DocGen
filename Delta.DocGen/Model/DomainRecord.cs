using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

/// <summary>A resolved domain grouping steps by functional area.</summary>
public sealed record DomainRecord(
    [property: JsonPropertyName("id")]    string Id,
    [property: JsonPropertyName("label")] string Label
);
