using System.Text.Json.Nodes;
using Delta.DocGen.Model;
using Delta.DocGen.Output.Serialiser;
using FluentAssertions;

namespace Delta.DocGen.Tests.Output;

public sealed class CanonicalJsonTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public CanonicalJsonTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* Windows handle race — ignore */ }
    }

    [Fact]
    public void KeysSortedAlphabeticallyAtTopLevel()
    {
        var obj = new { zebra = 1, apple = 2, mango = 3 };

        var json = CanonicalJson.Serialise(obj);

        var keys = JsonNode.Parse(json)!.AsObject().Select(p => p.Key).ToList();
        keys.Should().Equal("apple", "mango", "zebra");
    }

    [Fact]
    public void NestedObjectKeysAreSorted()
    {
        var obj = new { outer = new { z = 1, a = 2 } };

        var json = CanonicalJson.Serialise(obj);

        var innerKeys = JsonNode.Parse(json)!["outer"]!.AsObject().Select(p => p.Key).ToList();
        innerKeys.Should().Equal("a", "z");
    }

    [Fact]
    public void ArrayElementOrderIsPreserved()
    {
        var obj = new { items = new[] { "charlie", "alpha", "bravo" } };

        var json = CanonicalJson.Serialise(obj);

        var items = JsonNode.Parse(json)!["items"]!.AsArray()
            .Select(e => e!.GetValue<string>()).ToList();
        items.Should().Equal("charlie", "alpha", "bravo");
    }

    [Fact]
    public void OutputContainsNoInsignificantWhitespace()
    {
        // Canonical form must have no whitespace between tokens — but whitespace
        // inside string values is preserved verbatim.
        var obj = new { greeting = "hello world", number = 42 };

        var json = CanonicalJson.Serialise(obj);

        json.Should().NotContain(": ");      // no space after colons
        json.Should().NotContain(", ");      // no space after commas
        json.Should().NotContain("\n");
        json.Should().NotContain("\r");
        json.Should().Contain("hello world"); // value space preserved
    }

    [Fact]
    public void WriteCreatesFileWithPrettyPrintedJson()
    {
        var envelope = MakeEnvelope();
        var outputPath = Path.Combine(_dir, "output", "step-library.json");

        CanonicalJson.Write(envelope, outputPath);

        File.Exists(outputPath).Should().BeTrue();
        var content = File.ReadAllText(outputPath);
        content.Should().Contain("\n");         // pretty-printed
        content.Should().Contain("1.0.0");      // version present
        content.Should().Contain("\"steps\"");  // steps key present
    }

    [Fact]
    public void CanonicalOutputForKnownEnvelopeIsByteStable()
    {
        // Snapshot test: pins canonical JSON byte-for-byte. If this fails after a runtime
        // or library upgrade, every existing consumer's signature has shifted — review carefully.
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
            Signature:        null);

        var json = CanonicalJson.Serialise(envelope);

        const string expected =
            "{\"$schema\":\"./schema/v1/step-library.schema.json\"," +
            "\"domains\":[{\"id\":\"Auth\",\"label\":\"Auth \\u0026 Identity\"}]," +
            "\"enriched\":false," +
            "\"generatedAt\":\"2026-05-27T09:00:00Z\"," +
            "\"generatorVersion\":\"1.0.0\"," +
            "\"steps\":[{\"description\":\"\",\"domain\":\"Auth\",\"file\":\"Auth/AuthSteps.cs\"," +
            "\"id\":\"auth-a1b2c3d4\",\"line\":1,\"params\":[],\"pattern\":\"I am logged in\"," +
            "\"source\":\"\",\"suggestsNext\":[],\"tags\":[],\"type\":\"Given\",\"used\":0}]," +
            "\"version\":\"1.0.0\"}";

        json.Should().Be(expected);
    }

    private static Envelope MakeEnvelope() => new(
        Schema:           "./schema/v1/step-library.schema.json",
        Version:          "1.0.0",
        GeneratedAt:      "2026-05-27T09:00:00Z",
        GeneratorVersion: "1.0.0",
        Enriched:         false,
        Domains:          [],
        Steps:            [],
        Signature:        null);
}
