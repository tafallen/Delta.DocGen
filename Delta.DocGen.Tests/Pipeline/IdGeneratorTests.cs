using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Pipeline;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class IdGeneratorTests
{
    [Fact]
    public void GenerateMapsRawStepFieldsToStepRecord()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in",
                [new("username", "string", "")],
                "Auth/AuthSteps.cs", 5, "source text", "Auth")
        };
        var usageCounts = new Dictionary<string, int> { ["I am logged in"] = 3 };
        var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

        var (records, _) = IdGenerator.Generate(steps, usageCounts, rules, "General", NullDocGenLogger.Instance);

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

        var (records, _) = IdGenerator.Generate(
            steps, new Dictionary<string, int>(), [], "General", NullDocGenLogger.Instance);

        records[0].Used.Should().Be(0);
    }
}
