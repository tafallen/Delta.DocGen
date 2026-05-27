using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Scanner.Gherkin;
using Delta.DocGen.Tests.Logging;
using FluentAssertions;

namespace Delta.DocGen.Tests.Scanner.Gherkin;

public sealed class UsageCounterTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public UsageCounterTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteFeatureFile(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return relativePath;
    }

    [Fact]
    public void EmptyFeatureFileReturnsZeroCountForEachPattern()
    {
        var path = WriteFeatureFile("Features/Empty.feature", """
            Feature: Empty
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts.Should().ContainKey("I am logged in");
        counts["I am logged in"].Should().Be(0);
    }

    [Fact]
    public void MatchesLiteralStep()
    {
        var path = WriteFeatureFile("Features/Auth.feature", """
            Feature: Auth

              Scenario: Login
                Given I am logged in
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts["I am logged in"].Should().Be(1);
    }

    [Fact]
    public void StepNotUsedInFeatureFileHasCountZero()
    {
        var path = WriteFeatureFile("Features/Auth.feature", """
            Feature: Auth

              Scenario: Login
                Given I am logged in
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, ""),
            new(StepType.When,  "I click the button", [], "Auth/AuthSteps.cs", 5, "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts["I am logged in"].Should().Be(1);
        counts["I click the button"].Should().Be(0);
    }

    [Fact]
    public void MatchesIntPlaceholder()
    {
        var path = WriteFeatureFile("Features/Shop.feature", """
            Feature: Shop

              Scenario: Add items
                Given I have 5 items
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I have {int} items", [], "Shop/ShopSteps.cs", 1, "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts["I have {int} items"].Should().Be(1);
    }

    [Fact]
    public void MatchesStringPlaceholder()
    {
        var path = WriteFeatureFile("Features/Auth.feature", """
            Feature: Auth

              Scenario: Login as admin
                Given I am logged in as "admin"
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in as {string}", [], "Auth/AuthSteps.cs", 1, "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts["I am logged in as {string}"].Should().Be(1);
    }

    [Fact]
    public void MatchesDecimalPlaceholder()
    {
        var path = WriteFeatureFile("Features/Shop.feature", """
            Feature: Shop

              Scenario: Pricing
                Given a product costs 9.99
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "a product costs {decimal}", [], "Shop/ShopSteps.cs", 1, "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts["a product costs {decimal}"].Should().Be(1);
    }
}
