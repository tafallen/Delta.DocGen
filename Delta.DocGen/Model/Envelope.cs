using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

public sealed record SignatureRecord(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("digest")]    string Digest
);

public sealed record Envelope(
    [property: JsonPropertyName("$schema")]          string Schema,
    [property: JsonPropertyName("version")]          string Version,
    [property: JsonPropertyName("generatedAt")]      string GeneratedAt,
    [property: JsonPropertyName("generatorVersion")] string GeneratorVersion,
    [property: JsonPropertyName("enriched")]         bool Enriched,
    [property: JsonPropertyName("domains")]          IReadOnlyList<DomainRecord> Domains,
    [property: JsonPropertyName("steps")]            IReadOnlyList<StepRecord> Steps,
    [property: JsonPropertyName("signature")]        SignatureRecord? Signature
);
