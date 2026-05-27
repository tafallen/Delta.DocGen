using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Pipeline;
using Delta.DocGen.Tests.Logging;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class DomainAssignerTests
{
    [Fact]
    public void StepMatchingRuleIsAssignedDomain()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };
        var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

        var result = DomainAssigner.Assign(steps, rules, "General", NullDocGenLogger.Instance);

        result.Should().ContainSingle();
        result[0].Domain.Should().Be("Auth");
    }

    [Fact]
    public void StepMatchingNoRuleIsAssignedFallbackDomain()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I do something", [], "Other/OtherSteps.cs", 1, "")
        };
        var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

        var result = DomainAssigner.Assign(steps, rules, "General", NullDocGenLogger.Instance);

        result[0].Domain.Should().Be("General");
    }

    [Fact]
    public void UnmatchedStepLogsWarning()
    {
        var logger = new CapturingDocGenLogger();
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I do something", [], "Other/OtherSteps.cs", 1, "")
        };
        var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

        DomainAssigner.Assign(steps, rules, "General", logger);

        logger.WarnMessages.Should().ContainSingle(m => m.Contains("Other/OtherSteps.cs"));
    }

    [Fact]
    public void MatchedStepDoesNotLogWarning()
    {
        var logger = new CapturingDocGenLogger();
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };
        var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

        DomainAssigner.Assign(steps, rules, "General", logger);

        logger.WarnMessages.Should().BeEmpty();
    }

    [Fact]
    public void FirstMatchingRuleWins()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };
        var rules = new List<DomainRule>
        {
            new("Auth/**", "Auth",    "Auth & Identity"),
            new("**",      "General", "General"),            // catch-all — must NOT win
        };

        var result = DomainAssigner.Assign(steps, rules, "General", NullDocGenLogger.Instance);

        result[0].Domain.Should().Be("Auth");
    }
}
