using System.Text.Json;
using System.Text.Json.Nodes;
using Delta.DocGen.Model;
using FluentAssertions;
using Json.Schema;

namespace Delta.DocGen.Tests.Output;

public sealed class SchemaValidationTests
{
    private static JsonSchema LoadSchema()
    {
        var assembly = typeof(Delta.DocGen.Output.Schema.SchemaWriter).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Delta.DocGen.Output.Schema.Resources.step-library.v1.schema.json")!;
        return JsonSchema.FromStream(stream).AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void RealEnvelopeValidatesAgainstSchema()
    {
        var envelope = new Envelope(
            Schema:           "./schema/v1/step-library.schema.json",
            Version:          "1.0.0",
            GeneratedAt:      "2026-05-27T09:00:00Z",
            GeneratorVersion: "1.0.0",
            Enriched:         false,
            Domains:          [new DomainRecord("Auth", "Auth & Identity")],
            Steps:            [new StepRecord(
                "auth-a1b2c3d4", StepType.Given, "I am logged in", [],
                "Auth/AuthSteps.cs", 1, "Auth", [], 0, "", "", [])],
            Signature:        new SignatureRecord("SHA-256", new string('a', 64)));

        var json = JsonSerializer.Serialize(envelope);
        var node = JsonNode.Parse(json);
        var schema = LoadSchema();

        var result = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.Should().BeTrue(
            "schema validation failed: " + string.Join("; ",
                result.Details.Where(d => !d.IsValid).Select(d => $"{d.InstanceLocation}: {string.Join(",", d.Errors?.Values ?? Array.Empty<string>())}")));
    }

    [Fact]
    public void EnvelopeWithMissingRequiredFieldFailsValidation()
    {
        var brokenJson = JsonNode.Parse("""
            {
              "$schema": "./schema/v1/step-library.schema.json",
              "version": "1.0.0",
              "generatedAt": "2026-05-27T09:00:00Z",
              "generatorVersion": "1.0.0",
              "enriched": false,
              "domains": [],
              "signature": { "algorithm": "SHA-256", "digest": "0000000000000000000000000000000000000000000000000000000000000000" }
            }
            """);

        var schema = LoadSchema();
        var result = schema.Evaluate(brokenJson);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void StepWithInvalidIdFormatFailsValidation()
    {
        var brokenJson = JsonNode.Parse("""
            {
              "$schema": "./schema/v1/step-library.schema.json",
              "version": "1.0.0",
              "generatedAt": "2026-05-27T09:00:00Z",
              "generatorVersion": "1.0.0",
              "enriched": false,
              "domains": [{ "id": "Auth", "label": "Auth" }],
              "steps": [{
                "id": "BAD-ID-FORMAT",
                "type": "Given",
                "pattern": "I am logged in",
                "params": [],
                "file": "Auth/AuthSteps.cs",
                "line": 1,
                "domain": "Auth",
                "tags": [],
                "used": 0,
                "description": "",
                "source": "",
                "suggestsNext": []
              }],
              "signature": { "algorithm": "SHA-256", "digest": "0000000000000000000000000000000000000000000000000000000000000000" }
            }
            """);

        var schema = LoadSchema();
        var result = schema.Evaluate(brokenJson);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void EnvelopeWithTableParameterAndColumnsValidates()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "$schema": "./schema/v1/step-library.schema.json",
              "version": "1.0.0",
              "generatedAt": "2026-05-28T09:00:00Z",
              "generatorVersion": "1.0.0",
              "enriched": false,
              "domains": [{ "id": "Auth", "label": "Auth" }],
              "steps": [{
                "id": "auth-a1b2c3d4",
                "type": "Given",
                "pattern": "the contracts exist",
                "params": [{
                  "name": "contracts",
                  "type": "table",
                  "example": "",
                  "columns": [
                    { "name": "Id",     "type": "int" },
                    { "name": "Symbol", "type": "string" }
                  ]
                }],
                "file": "Auth/AuthSteps.cs",
                "line": 1,
                "domain": "Auth",
                "tags": [],
                "used": 0,
                "description": "",
                "source": "",
                "suggestsNext": []
              }],
              "signature": { "algorithm": "SHA-256", "digest": "0000000000000000000000000000000000000000000000000000000000000000" }
            }
            """);

        var schema = LoadSchema();
        var result = schema.Evaluate(json);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EnvelopeWithEmptyColumnsArrayValidates()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "$schema": "./schema/v1/step-library.schema.json",
              "version": "1.0.0",
              "generatedAt": "2026-05-28T09:00:00Z",
              "generatorVersion": "1.0.0",
              "enriched": false,
              "domains": [{ "id": "Auth", "label": "Auth" }],
              "steps": [{
                "id": "auth-a1b2c3d4",
                "type": "Given",
                "pattern": "the contracts exist",
                "params": [{ "name": "t", "type": "table", "example": "", "columns": [] }],
                "file": "Auth/AuthSteps.cs",
                "line": 1,
                "domain": "Auth",
                "tags": [],
                "used": 0,
                "description": "",
                "source": "",
                "suggestsNext": []
              }],
              "signature": { "algorithm": "SHA-256", "digest": "0000000000000000000000000000000000000000000000000000000000000000" }
            }
            """);

        var schema = LoadSchema();
        var result = schema.Evaluate(json);

        result.IsValid.Should().BeTrue();
    }
}
