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

    [Fact]
    public void EmptyStepListReturnsEmptyList()
    {
        var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

        var result = DomainAssigner.Assign([], rules, "General", NullDocGenLogger.Instance);

        result.Should().BeEmpty();
    }

    [Fact]
    public void WindowsBackslashPathStillMatchesForwardSlashPattern()
    {
        // Defensive: even though the discoverer normalises paths to '/', this guards
        // against future regressions in upstream code on Windows hosts.
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], @"Auth\AuthSteps.cs", 1, "")
        };
        var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

        var result = DomainAssigner.Assign(steps, rules, "General", NullDocGenLogger.Instance);

        result[0].Domain.Should().Be("Auth");
    }

    [Fact]
    public void EmptyRulesEmitsSingleConsolidatedWarning()
    {
        var logger = new CapturingDocGenLogger();
        var steps = new List<RawStep>
        {
            new(StepType.Given, "step one", [], "Auth/AuthSteps.cs",  1, ""),
            new(StepType.When,  "step two", [], "Forms/FormSteps.cs", 5, ""),
        };

        DomainAssigner.Assign(steps, [], "General", logger);

        logger.WarnMessages.Should().ContainSingle()
            .Which.Should().Contain("No domain rules").And.Contain("2 step");
    }

    [Fact]
    public void EmptyRulesAndEmptyStepsEmitsNoWarning()
    {
        var logger = new CapturingDocGenLogger();

        DomainAssigner.Assign([], [], "General", logger);

        logger.WarnMessages.Should().BeEmpty();
    }

    [Fact]
    public void EmptyRulesAssignsFallbackToAllSteps()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, ""),
            new(StepType.When,  "I click submit",  [], "Forms/FormSteps.cs", 5, "")
        };

        var result = DomainAssigner.Assign(steps, [], "General", NullDocGenLogger.Instance);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(s => s.Domain.Should().Be("General"));
    }
}
