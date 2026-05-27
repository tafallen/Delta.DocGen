using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Pipeline;
using Delta.DocGen.Tests.Logging;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class IdGeneratorTests
{
    [Fact]
    public void AssignIdsMapsRawStepFieldsToStepRecord()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in",
                [new("username", "string", "")],
                "Auth/AuthSteps.cs", 5, "source text", "Auth")
        };
        var usageCounts = new Dictionary<string, int> { ["I am logged in"] = 3 };

        var records = IdGenerator.AssignIds(steps, usageCounts, NullDocGenLogger.Instance);

        records.Should().ContainSingle();
        var r = records[0];
        r.Type.Should().Be(StepType.Given);
        r.Pattern.Should().Be("I am logged in");
        r.Params.Should().ContainSingle(p => p.Name == "username" && p.Type == "string");
        r.File.Should().Be("Auth/AuthSteps.cs");
        r.Line.Should().Be(5);
        r.Domain.Should().Be("Auth");
        r.Source.Should().Be("source text");
        r.Used.Should().Be(3);
        r.Tags.Should().BeEmpty();
        r.Description.Should().BeEmpty();
        r.SuggestsNext.Should().BeEmpty();
    }

    [Fact]
    public void MissingUsageCountDefaultsToZero()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "", "Auth")
        };

        var records = IdGenerator.AssignIds(steps, new Dictionary<string, int>(), NullDocGenLogger.Instance);

        records[0].Used.Should().Be(0);
    }

    [Fact]
    public void IdMatchesDomainPrefixAndEightCharHexPattern()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "", "Auth")
        };

        var records = IdGenerator.AssignIds(steps, new Dictionary<string, int>(), NullDocGenLogger.Instance);

        records[0].Id.Should().MatchRegex(@"^auth-[0-9a-f]{8}$");
    }

    [Fact]
    public void IdIsStableRegardlessOfFileOrLineNumber()
    {
        // ID is based on domain + pattern only — file moves and line changes must not affect it.
        var step1 = new RawStep(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs",    1,  "", "Auth");
        var step2 = new RawStep(StepType.Given, "I am logged in", [], "Auth/NewAuthSteps.cs", 99, "", "Auth");

        var r1 = IdGenerator.AssignIds([step1], new Dictionary<string, int>(), NullDocGenLogger.Instance);
        var r2 = IdGenerator.AssignIds([step2], new Dictionary<string, int>(), NullDocGenLogger.Instance);

        r1[0].Id.Should().Be(r2[0].Id);
    }

    [Fact]
    public void DuplicatePatternInSameDomainThrowsInvalidOperationException()
    {
        // Same pattern, same domain = same ID = collision.
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs",  1, "", "Auth"),
            new(StepType.When,  "I am logged in", [], "Auth/AuthSteps.cs", 10, "", "Auth"),
        };

        var act = () => IdGenerator.AssignIds(steps, new Dictionary<string, int>(), NullDocGenLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*collision*");
    }

    [Fact]
    public void EmptyDomainPrefixLogsWarningAndUsesUnknown()
    {
        var logger = new CapturingDocGenLogger();
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Other/OtherSteps.cs", 1, "", "認証")
        };

        var records = IdGenerator.AssignIds(steps, new Dictionary<string, int>(), logger);

        records[0].Id.Should().StartWith("unknown-");
        logger.WarnMessages.Should().ContainSingle(m => m.Contains("認証") && m.Contains("unknown"));
    }

    [Fact]
    public void NfcAndNfdFormsOfSamePatternProduceSameId()
    {
        // "café" can be encoded as NFC (é = U+00E9) or NFD (e + U+0301).
        // NFC normalisation in PatternHash ensures the IDs match.
        var nfc = "café login";          // é precomposed
        var nfd = "café login";          // e + combining acute

        var stepNfc = new RawStep(StepType.Given, nfc, [], "Auth/AuthSteps.cs", 1, "", "Auth");
        var stepNfd = new RawStep(StepType.Given, nfd, [], "Auth/AuthSteps.cs", 2, "", "Auth");

        var r1 = IdGenerator.AssignIds([stepNfc], new Dictionary<string, int>(), NullDocGenLogger.Instance);
        var r2 = IdGenerator.AssignIds([stepNfd], new Dictionary<string, int>(), NullDocGenLogger.Instance);

        r1[0].Id.Should().Be(r2[0].Id);
    }
}
