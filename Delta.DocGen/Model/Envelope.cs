using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

public sealed record SignatureRecord(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("digest")]    string Digest
);

/// <summary>Top-level output envelope written by Stage 7.</summary>
/// <param name="GeneratedAt">
/// ISO 8601 UTC timestamp in round-trip format, e.g. <c>2026-05-27T09:00:00Z</c>.
/// Must use the <c>Z</c> suffix (not <c>+00:00</c>) so the canonical signing form and
/// the viewer's verification agree on the exact byte sequence.
/// Produce with <c>DateTimeOffset.UtcNow.ToString("O")</c> — the round-trip specifier
/// always emits <c>+00:00</c>, so call <c>.Replace("+00:00", "Z")</c> afterwards.
/// </param>
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
