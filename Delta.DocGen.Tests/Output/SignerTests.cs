using Delta.DocGen.Model;
using Delta.DocGen.Output.Serialiser;
using FluentAssertions;

namespace Delta.DocGen.Tests.Output;

public sealed class SignerTests
{
    private static Envelope MakeEnvelope(IReadOnlyList<StepRecord>? steps = null) => new(
        Schema:           "./schema/v1/step-library.schema.json",
        Version:          "1.0.0",
        GeneratedAt:      "2026-05-27T09:00:00Z",
        GeneratorVersion: "1.0.0",
        Enriched:         false,
        Domains:          [],
        Steps:            steps ?? [],
        Signature:        null);

    [Fact]
    public void SignedEnvelopeHasNonEmptyDigest()
    {
        var signed = Signer.Sign(MakeEnvelope());

        signed.Signature.Should().NotBeNull();
        signed.Signature!.Digest.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AlgorithmIsSHA256()
    {
        var signed = Signer.Sign(MakeEnvelope());

        signed.Signature!.Algorithm.Should().Be("SHA-256");
    }

    [Fact]
    public void DigestIsLowercaseHexadecimal()
    {
        var signed = Signer.Sign(MakeEnvelope());

        signed.Signature!.Digest.Should().MatchRegex("^[0-9a-f]+$");
    }
}
