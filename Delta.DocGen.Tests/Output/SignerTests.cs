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

    [Fact]
    public void DigestIsDeterministic()
    {
        var envelope = MakeEnvelope();

        var signed1 = Signer.Sign(envelope);
        var signed2 = Signer.Sign(envelope);

        signed1.Signature!.Digest.Should().Be(signed2.Signature!.Digest);
    }

    [Fact]
    public void DigestChangesWhenStepsChange()
    {
        var step = new StepRecord(
            "auth-a1b2c3d4", StepType.Given, "I am logged in", [],
            "Auth/AuthSteps.cs", 1, "Auth", [], 0, "", "", []);
        var emptyEnvelope = MakeEnvelope(steps: []);
        var filledEnvelope = MakeEnvelope(steps: [step]);

        var signedEmpty  = Signer.Sign(emptyEnvelope);
        var signedFilled = Signer.Sign(filledEnvelope);

        signedEmpty.Signature!.Digest.Should().NotBe(signedFilled.Signature!.Digest);
    }

    [Fact]
    public void SignatureFieldIsExcludedFromHashedContent()
    {
        // Sign the envelope. Then recompute: strip signature, serialise canonically,
        // hash — must match the stored digest.
        var envelope = MakeEnvelope();
        var signed = Signer.Sign(envelope);

        var unsigned = signed with { Signature = null };
        var canonical = CanonicalJson.Serialise(unsigned);
        var expectedDigest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();

        signed.Signature!.Digest.Should().Be(expectedDigest);
    }
}
