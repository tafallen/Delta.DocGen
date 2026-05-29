using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

public sealed record ColumnRecord(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type);
