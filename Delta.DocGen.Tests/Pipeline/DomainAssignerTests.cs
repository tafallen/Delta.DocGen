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
}
