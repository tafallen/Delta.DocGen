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

    [Fact]
    public void SameStepUsedInMultipleScenariosCumulatesCount()
    {
        var path = WriteFeatureFile("Features/Auth.feature", """
            Feature: Auth

              Scenario: First login
                Given I am logged in

              Scenario: Second login
                Given I am logged in
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts["I am logged in"].Should().Be(2);
    }

    [Fact]
    public void MultipleDistinctStepsInOneScenarioEachCountedOnce()
    {
        var path = WriteFeatureFile("Features/Auth.feature", """
            Feature: Auth

              Scenario: Login flow
                Given I am on the login page
                When I submit valid credentials
                Then I should be redirected to the dashboard
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am on the login page",                 [], "Auth/AuthSteps.cs", 1,  ""),
            new(StepType.When,  "I submit valid credentials",             [], "Auth/AuthSteps.cs", 5,  ""),
            new(StepType.Then,  "I should be redirected to the dashboard",[], "Auth/AuthSteps.cs", 9,  "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts["I am on the login page"].Should().Be(1);
        counts["I submit valid credentials"].Should().Be(1);
        counts["I should be redirected to the dashboard"].Should().Be(1);
    }

    [Fact]
    public void UnmatchedStepLogsWarning()
    {
        var logger = new CapturingDocGenLogger();
        var path = WriteFeatureFile("Features/Unknown.feature", """
            Feature: Unknown

              Scenario: Mystery
                Given something nobody has defined
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };

        UsageCounter.Count(steps, path, _root, logger);

        logger.WarnMessages.Should().ContainSingle(m => m.Contains("something nobody has defined"));
    }

    [Fact]
    public void MatchedStepDoesNotLogWarning()
    {
        var logger = new CapturingDocGenLogger();
        var path = WriteFeatureFile("Features/Auth.feature", """
            Feature: Auth

              Scenario: Login
                Given I am logged in
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };

        UsageCounter.Count(steps, path, _root, logger);

        logger.WarnMessages.Should().BeEmpty();
    }

    [Fact]
    public void ScenarioOutlineStepCountedOnceNotPerExampleRow()
    {
        // The outline template step "I am on the shop page" appears once in the AST.
        // It should be counted as 1 regardless of how many Example rows exist.
        var path = WriteFeatureFile("Features/Shop.feature", """
            Feature: Shop

              Scenario Outline: Browse products
                Given I am on the shop page

              Examples:
                | product |
                | apple   |
                | banana  |
                | cherry  |
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am on the shop page", [], "Shop/ShopSteps.cs", 1, "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts["I am on the shop page"].Should().Be(1);
    }

    [Fact]
    public void ThrowsFileNotFoundForMissingFeatureFile()
    {
        var missing = "Features/DoesNotExist.feature";
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };

        var act = () => UsageCounter.Count(steps, missing, _root, NullDocGenLogger.Instance);

        act.Should().Throw<System.IO.IOException>();
    }
}
