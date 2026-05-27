using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Scanner.CSharp;
using FluentAssertions;

namespace Delta.DocGen.Tests.Scanner.CSharp;

public sealed class StepDefinitionExtractorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public StepDefinitionExtractorTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return relativePath;
    }

    [Fact]
    public void ExtractsGivenStepWithStringParam()
    {
        var path = WriteFile("Auth/AuthSteps.cs", """
            using TechTalk.SpecFlow;
            public class AuthSteps
            {
                [Given("I am logged in as {string}")]
                public void GivenIAmLoggedInAs(string username) { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        var step = steps[0];
        step.Type.Should().Be("Given");
        step.Pattern.Should().Be("I am logged in as {string}");
        step.File.Should().Be(path);
        step.Params.Should().ContainSingle();
        step.Params[0].Name.Should().Be("username");
        step.Params[0].Type.Should().Be("string");
        step.Params[0].Example.Should().Be("");
    }

    [Fact]
    public void ExtractsWhenAndThenTypes()
    {
        var path = WriteFile("Steps/WhenThenSteps.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [When("I click the button")]
                public void WhenIClickTheButton() { }

                [Then("the page should show {string}")]
                public void ThenThePageShouldShow(string text) { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().HaveCount(2);
        steps[0].Type.Should().Be("When");
        steps[0].Params.Should().BeEmpty();
        steps[1].Type.Should().Be("Then");
        steps[1].Params.Should().ContainSingle(p => p.Type == "string");
    }

    [Fact]
    public void MapsIntAndDecimalParamTypes()
    {
        var path = WriteFile("Steps/TypedSteps.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("I have {int} items costing {decimal} each")]
                public void GivenItems(int count, decimal price) { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Params.Should().HaveCount(2);
        steps[0].Params[0].Name.Should().Be("count");
        steps[0].Params[0].Type.Should().Be("int");
        steps[0].Params[0].Example.Should().Be("0");
        steps[0].Params[1].Name.Should().Be("price");
        steps[0].Params[1].Type.Should().Be("decimal");
        steps[0].Params[1].Example.Should().Be("0.00");
    }

    [Fact]
    public void DetectsDocStringParam()
    {
        var path = WriteFile("Steps/DocStringSteps.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("I send the request")]
                public void GivenISendTheRequest(string body) { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Params.Should().ContainSingle();
        steps[0].Params[0].Name.Should().Be("body");
        steps[0].Params[0].Type.Should().Be("DocString");
        steps[0].Params[0].Example.Should().Be("");
    }

    [Fact]
    public void DistinguishesStringAndDocStringParams()
    {
        var path = WriteFile("Steps/MixedSteps.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("I am {string} with payload")]
                public void GivenIAmWithPayload(string name, string body) { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Params.Should().HaveCount(2);
        steps[0].Params[0].Type.Should().Be("string");
        steps[0].Params[1].Type.Should().Be("DocString");
    }
}
