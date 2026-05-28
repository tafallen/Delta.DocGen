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
    public void CrossMethodCollisionLogsErrorAndSkipsDuplicate()
    {
        // Same pattern + domain + type across two DIFFERENT methods (different lines) is a
        // genuine ambiguous-binding defect in the user's Reqnroll project. The doc tool
        // logs Error, skips the second binding, and continues — operators see every
        // collision in one pass instead of fixing them one-at-a-time.
        var logger = new CapturingDocGenLogger();
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs",  1, "", "Auth"),
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 10, "", "Auth"),
        };

        var records = IdGenerator.AssignIds(steps, new Dictionary<string, int>(), logger);

        records.Should().ContainSingle();
        logger.ErrorMessages.Should().ContainSingle(m =>
            m.Contains("Ambiguous step binding")
            && m.Contains("Auth/AuthSteps.cs:1")
            && m.Contains("Auth/AuthSteps.cs:10"));
    }

    [Fact]
    public void SamePatternDifferentTypeProducesDistinctIds()
    {
        // A single Reqnroll method with [Given] + [Then] attrs on the same pattern
        // is a valid pattern — both should appear in the output with distinct IDs.
        var steps = new List<RawStep>
        {
            new(StepType.Given, "the contract does not exist", [], "Auth/AuthSteps.cs", 1, "", "Auth"),
            new(StepType.Then,  "the contract does not exist", [], "Auth/AuthSteps.cs", 1, "", "Auth"),
        };

        var records = IdGenerator.AssignIds(steps, new Dictionary<string, int>(), NullDocGenLogger.Instance);

        records.Should().HaveCount(2);
        records[0].Id.Should().NotBe(records[1].Id);
        records[0].Type.Should().Be(StepType.Given);
        records[1].Type.Should().Be(StepType.Then);
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
    public void ExactDuplicateAttributeOnSameMethodLogsWarnAndSkips()
    {
        // Two identical [Then] attributes on a single method — copy-paste duplicate.
        // Same Type + Pattern + File + Line as the original means this is dead code, not a
        // real binding collision. Log a warning and skip the duplicate.
        var logger = new CapturingDocGenLogger();
        var steps = new List<RawStep>
        {
            new(StepType.Then, "a credit contract called \"X\" exists", [], "Auth/AuthSteps.cs", 46, "", "Auth"),
            new(StepType.Then, "a credit contract called \"X\" exists", [], "Auth/AuthSteps.cs", 46, "", "Auth"),
        };

        var records = IdGenerator.AssignIds(steps, new Dictionary<string, int>(), logger);

        records.Should().ContainSingle();
        logger.WarnMessages.Should().ContainSingle(m =>
            m.Contains("Duplicate step attribute") && m.Contains("Auth/AuthSteps.cs:46"));
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
