using Delta.DocGen.Config;
using Delta.DocGen.Model;
using Delta.DocGen.Pipeline;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class DomainBuilderTests
{
    [Fact]
    public void DomainRecordsAreDistinctInFirstOccurrenceOrder()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "step one",   [], "Auth/AuthSteps.cs",  1, "", "Auth"),
            new(StepType.Given, "step two",   [], "Forms/FormSteps.cs", 1, "", "Forms"),
            new(StepType.Given, "step three", [], "Auth/AuthSteps2.cs", 5, "", "Auth"),
        };
        var rules = new List<DomainRule>
        {
            new("Auth/**",  "Auth",  "Auth & Identity"),
            new("Forms/**", "Forms", "Forms & Input"),
        };

        var domains = DomainBuilder.Build(steps, rules);

        domains.Should().HaveCount(2);
        domains[0].Id.Should().Be("Auth");
        domains[0].Label.Should().Be("Auth & Identity");
        domains[1].Id.Should().Be("Forms");
        domains[1].Label.Should().Be("Forms & Input");
    }

    [Fact]
    public void DomainWithoutMatchingRuleUsesItsOwnIdAsLabel()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "step one", [], "Other/OtherSteps.cs", 1, "", "General")
        };

        var domains = DomainBuilder.Build(steps, []);

        domains.Should().ContainSingle();
        domains[0].Id.Should().Be("General");
        domains[0].Label.Should().Be("General");
    }
}
