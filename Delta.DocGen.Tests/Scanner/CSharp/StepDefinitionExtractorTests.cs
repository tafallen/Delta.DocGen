using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Scanner.CSharp;
using Delta.DocGen.Tests.Logging;
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
        step.Type.Should().Be(StepType.Given);
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
        steps[0].Type.Should().Be(StepType.When);
        steps[0].Params.Should().BeEmpty();
        steps[1].Type.Should().Be(StepType.Then);
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

    [Fact]
    public void ProducesOneRawStepPerAttribute()
    {
        var path = WriteFile("Steps/MultiAttrSteps.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("I am on the home page")]
                [Given("I navigate to the home page")]
                public void GivenOnHomePage() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().HaveCount(2);
        steps[0].Pattern.Should().Be("I am on the home page");
        steps[1].Pattern.Should().Be("I navigate to the home page");
    }

    [Fact]
    public void ReturnsEmptyForFileWithNoStepAttributes()
    {
        var path = WriteFile("Steps/PlainSteps.cs", """
            public class PlainClass
            {
                public void SomeMethod() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().BeEmpty();
    }

    [Fact]
    public void ExtractsStepsFromMultipleMethods()
    {
        var path = WriteFile("Steps/MultiMethodSteps.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("step one")]
                public void StepOne() { }

                [When("step two")]
                public void StepTwo() { }

                [Then("step three")]
                public void StepThree() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().HaveCount(3);
        steps.Select(s => s.Type).Should().BeEquivalentTo([StepType.Given, StepType.When, StepType.Then]);
    }

    [Fact]
    public void ExtractsReqnrollQualifiedAttributeByName()
    {
        var path = WriteFile("Steps/ReqnrollSteps.cs", """
            using Reqnroll;
            public class MySteps
            {
                [Reqnroll.Given("I use reqnroll")]
                public void GivenIUseReqnroll() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Type.Should().Be(StepType.Given);
        steps[0].Pattern.Should().Be("I use reqnroll");
    }

    [Fact]
    public void SourceContainsFullMethodText()
    {
        var path = WriteFile("Steps/SourceSteps.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("I am on the home page")]
                public void GivenOnHomePage()
                {
                    // body comment
                }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Source.Should().Contain("[Given(");
        steps[0].Source.Should().Contain("GivenOnHomePage");
        steps[0].Source.Should().Contain("// body comment");
    }

    [Fact]
    public void LineNumberIsOneBasedAndMatchesAttribute()
    {
        var path = WriteFile("Steps/LineSteps.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("step one")]
                public void StepOne() { }
            }
            """);
        // line 1: using TechTalk.SpecFlow;
        // line 2: public class MySteps
        // line 3: {
        // line 4:     [Given("step one")]

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Line.Should().Be(4);
    }

    [Fact]
    public void ExtractsStepDefinitionAttribute()
    {
        var path = WriteFile("Steps/UniversalStep.cs", """
            using Reqnroll;
            public class MySteps
            {
                [StepDefinition("I do something universal")]
                public void DoSomethingUniversal() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Type.Should().Be(StepType.StepDefinition);
        steps[0].Pattern.Should().Be("I do something universal");
    }

    [Fact]
    public void TableParamMapsToStringWithoutConsumingPlaceholder()
    {
        var path = WriteFile("Steps/TableStep.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("I have the following users")]
                public void GivenUsers(Table table) { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Params.Should().ContainSingle(p => p.Type == ParamTypes.Table && p.Name == "table");
    }

    [Fact]
    public void WarnsAndSkipsAttributeWithNoStringArgument()
    {
        var logger = new CapturingDocGenLogger();
        var path = WriteFile("Steps/NoArgStep.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given]
                public void GivenNoPattern() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, logger);

        steps.Should().BeEmpty();
        logger.WarnMessages.Should().ContainSingle(m => m.Contains("has no arguments"));
    }

    [Fact]
    public void WarnsForUnrecognisedParamType()
    {
        var logger = new CapturingDocGenLogger();
        var path = WriteFile("Steps/CustomTypeStep.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("I have something")]
                public void GivenSomething(MyCustomType custom) { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, logger);

        steps.Should().ContainSingle();
        logger.WarnMessages.Should().ContainSingle(m => m.Contains("MyCustomType"));
    }

    [Fact]
    public void ExtractsSpecFlowQualifiedAttributeByName()
    {
        var path = WriteFile("Steps/SpecFlowQualifiedSteps.cs", """
            public class MySteps
            {
                [TechTalk.SpecFlow.Given("I use specflow fully qualified")]
                public void GivenSpecFlowQualified() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Type.Should().Be(StepType.Given);
        steps[0].Pattern.Should().Be("I use specflow fully qualified");
    }

    [Fact]
    public void InterleavedTableDoesNotConsumeIntPlaceholderSlot()
    {
        // [Given("{int} items")] + (int count, Table table, string body)
        // Table is framework-injected — must not consume the {int} placeholder slot.
        // After count takes slot 0 ({int}), no more placeholders remain, so body → DocString.
        var path = WriteFile("Steps/InterleavedStep.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("I have {int} items")]
                public void GivenItems(int count, Table table, string body) { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        steps[0].Params.Should().HaveCount(3);
        steps[0].Params[0].Name.Should().Be("count");
        steps[0].Params[0].Type.Should().Be(ParamTypes.Int);
        steps[0].Params[1].Name.Should().Be("table");
        steps[0].Params[1].Type.Should().Be(ParamTypes.Table);
        steps[0].Params[2].Name.Should().Be("body");
        steps[0].Params[2].Type.Should().Be(ParamTypes.DocString);
    }

    [Fact]
    public void EmptyFileReturnsEmptyListWithoutException()
    {
        var path = WriteFile("Steps/Empty.cs", "");

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().BeEmpty();
    }

    [Fact]
    public void MixedGivenAndWhenOnSameMethodProducesTwoSteps()
    {
        var path = WriteFile("Steps/MultiTypeStep.cs", """
            using TechTalk.SpecFlow;
            public class MySteps
            {
                [Given("I have logged in")]
                [When("I am already logged in")]
                public void LoggedIn() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().HaveCount(2);
        steps[0].Type.Should().Be(StepType.Given);
        steps[0].Pattern.Should().Be("I have logged in");
        steps[1].Type.Should().Be(StepType.When);
        steps[1].Pattern.Should().Be("I am already logged in");
    }

    [Fact]
    public void ThrowsFileNotFoundForMissingFile()
    {
        var missing = "Steps.cs"; // directory (_root) exists, but file does not
        var act = () => StepDefinitionExtractor.Extract(missing, _root, NullDocGenLogger.Instance);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void DuplicateAttributeOnSameMethodIsDedupedWithWarn()
    {
        // Two identical [Then(...)] attributes stacked on the same method —
        // copy-paste duplicate. Emit one RawStep + Warn.
        var logger = new CapturingDocGenLogger();
        var path = WriteFile("DupStacked.cs", """
            using Reqnroll;
            public class DupStacked
            {
                [Then(@"a thing exists")]
                [Then(@"a thing exists")]
                public void ThenAThingExists() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, logger);

        steps.Should().ContainSingle()
            .Which.Pattern.Should().Be("a thing exists");
        logger.WarnMessages.Should().ContainSingle(m =>
            m.Contains("Duplicate step attribute") && m.Contains("DupStacked.cs"));
    }

    [Fact]
    public void DistinctTypesWithSamePatternOnSameMethodAreBothKept()
    {
        // [Given]+[Then] same pattern → two distinct RawSteps (different types).
        var logger = new CapturingDocGenLogger();
        var path = WriteFile("GivenAndThen.cs", """
            using Reqnroll;
            public class GivenAndThen
            {
                [Given(@"a thing exists")]
                [Then(@"a thing exists")]
                public void Method() { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, logger);

        steps.Should().HaveCount(2);
        steps.Should().Contain(s => s.Type == StepType.Given);
        steps.Should().Contain(s => s.Type == StepType.Then);
        logger.WarnMessages.Should().BeEmpty();
    }

    [Fact]
    public void TableParamWithoutAnnotationGetsTypeTableAndNullColumns()
    {
        var path = WriteFile("Table.cs", """
            using Reqnroll;
            public class Steps
            {
                [Given("the contracts exist")]
                public void GivenContractsExist(Table contracts) { }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        steps.Should().ContainSingle();
        var p = steps[0].Params.Single();
        p.Name.Should().Be("contracts");
        p.Type.Should().Be(ParamTypes.Table);
        p.Columns.Should().BeNull();  // no annotation → no declared columns
    }

    [Fact]
    public void TableParamWithCreateSetResolvesSameFileType()
    {
        var path = WriteFile("CreateSet.cs", """
            using System;
            using Reqnroll;

            public sealed class Contract
            {
                public int Id { get; set; }
                public string Symbol { get; set; } = "";
                public DateTime Occurred { get; set; }
            }

            public class Steps
            {
                [Given("the contracts exist")]
                public void GivenContractsExist(Table contracts)
                {
                    var rows = contracts.CreateSet<Contract>();
                }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        var p = steps[0].Params.Single();
        p.Type.Should().Be(ParamTypes.Table);
        p.Columns.Should().NotBeNull();
        p.Columns!.Should().HaveCount(3);
        p.Columns![0].Name.Should().Be("Id");
        p.Columns![0].Type.Should().Be("int");
        p.Columns![1].Name.Should().Be("Symbol");
        p.Columns![1].Type.Should().Be("string");
        p.Columns![2].Name.Should().Be("Occurred");
        p.Columns![2].Type.Should().Be("DateTime");
    }

    [Fact]
    public void TableParamWithCreateInstanceResolvesSameFileType()
    {
        var path = WriteFile("CreateInstance.cs", """
            using Reqnroll;

            public sealed class Order { public decimal Amount { get; set; } }

            public class Steps
            {
                [Given("an order exists")]
                public void GivenAnOrderExists(Table order)
                {
                    var instance = order.CreateInstance<Order>();
                }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        var p = steps[0].Params.Single();
        p.Type.Should().Be(ParamTypes.Table);
        p.Columns!.Should().ContainSingle();
        p.Columns![0].Name.Should().Be("Amount");
        p.Columns![0].Type.Should().Be("decimal");
    }

    [Fact]
    public void TableParamWithCrossFileCreateSetFallsBackToNullColumns()
    {
        var path = WriteFile("CrossFile.cs", """
            using Reqnroll;
            public class Steps
            {
                [Given("the cross-file items exist")]
                public void GivenItemsExist(Table items)
                {
                    // CrossFileType is not declared in this file.
                    var rows = items.CreateSet<CrossFileType>();
                }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        var p = steps[0].Params.Single();
        p.Type.Should().Be(ParamTypes.Table);
        p.Columns.Should().BeNull();
    }

    [Fact]
    public void TableParamWithGetStringCallsGetsRoslynSource()
    {
        var path = WriteFile("RoslynInfer.cs", """
            using Reqnroll;
            public class Steps
            {
                [Given("the following trades exist")]
                public void GivenTradesExist(Table table)
                {
                    foreach (var row in table.Rows)
                    {
                        var sym = row["Symbol"];
                        var qty = row["Quantity"];
                    }
                }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        var p = steps[0].Params.Single();
        p.Type.Should().Be(ParamTypes.Table);
        p.ColumnsSource.Should().Be("roslyn");
        p.Columns.Should().NotBeNull();
        p.Columns!.Select(c => c.Name).Should().Equal("Symbol", "Quantity");
    }

    [Fact]
    public void TableParamWithNoBodyAccessHasNullColumnsSource()
    {
        var path = WriteFile("NoAccess.cs", """
            using Reqnroll;
            public class Steps
            {
                [Given("some table step")]
                public void GivenSomeTable(Table table)
                {
                    // does nothing with table
                }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        var p = steps[0].Params.Single();
        p.Type.Should().Be(ParamTypes.Table);
        p.ColumnsSource.Should().BeNull();
        p.Columns.Should().BeNull();
    }

    [Fact]
    public void TableParamWithCreateSetHasDeclaredSource()
    {
        var path = WriteFile("DeclaredSource.cs", """
            using Reqnroll;

            public sealed class Item { public string Name { get; set; } = ""; }

            public class Steps
            {
                [Given("items exist")]
                public void GivenItemsExist(Table table)
                {
                    var items = table.CreateSet<Item>();
                }
            }
            """);

        var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

        var p = steps[0].Params.Single();
        p.Type.Should().Be(ParamTypes.Table);
        p.ColumnsSource.Should().Be("declared");
    }

    [Fact]
    public void UnknownPlaceholderTypeWarns()
    {
        var logger = new CapturingDocGenLogger();
        var path = WriteFile("Steps/Garbage.cs", """
            using TechTalk.SpecFlow;
            public class GarbageSteps
            {
                [Given("I have {garbage} items")]
                public void GivenIHave(string value) { }
            }
            """);

        StepDefinitionExtractor.Extract(path, _root, logger);

        logger.WarnMessages.Should().Contain(m => m.Contains("Unknown Cucumber placeholder type") && m.Contains("garbage"));
    }

    [Fact]
    public void KnownPlaceholderTypesDoNotWarn()
    {
        var logger = new CapturingDocGenLogger();
        var path = WriteFile("Steps/Known.cs", """
            using TechTalk.SpecFlow;
            public class KnownSteps
            {
                [Given("I have {int} items of type {string}")]
                public void GivenIHave(int count, string type) { }
            }
            """);

        StepDefinitionExtractor.Extract(path, _root, logger);

        logger.WarnMessages.Should().NotContain(m => m.Contains("Unknown Cucumber placeholder type"));
    }

    [Fact]
    public void NonLiteralPatternArgumentLogsDescriptiveWarn()
    {
        var logger = new CapturingDocGenLogger();
        var path = WriteFile("Steps/Constant.cs", """
            using TechTalk.SpecFlow;
            public static class Patterns { public const string Login = "I log in"; }
            public class ConstantSteps
            {
                [Given(Patterns.Login)]
                public void GivenLogin() { }
            }
            """);

        StepDefinitionExtractor.Extract(path, _root, logger);

        logger.WarnMessages.Should().Contain(m =>
            m.Contains("argument is not a string literal") && m.Contains("Given"));
    }
}
