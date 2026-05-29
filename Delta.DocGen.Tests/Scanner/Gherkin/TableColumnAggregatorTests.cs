using System.Text;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Scanner.Gherkin;
using FluentAssertions;

namespace Delta.DocGen.Tests.Scanner.Gherkin;

public sealed class TableColumnAggregatorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public TableColumnAggregatorTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteFeatureFile(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, Encoding.UTF8);
        return relativePath;
    }

    [Fact]
    public void CapturesHeadersForStepWithDataTable()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "the contracts exist", [], "Auth/Steps.cs", 1, "")
        };
        WriteFeatureFile("contracts.feature", """
            Feature: Contracts
              Scenario: Sample
                Given the contracts exist
                  | Id  | Symbol |
                  | 1   | AAPL   |
                  | 2   | MSFT   |
            """);

        var columns = TableColumnAggregator.Aggregate(
            steps, ["contracts.feature"], _root, NullDocGenLogger.Instance);

        columns.Should().ContainKey("the contracts exist");
        var captured = columns["the contracts exist"];
        captured.Should().HaveCount(2);
        captured[0].Name.Should().Be("Id");
        captured[1].Name.Should().Be("Symbol");
        captured[0].Type.Should().Be(ParamTypes.Int);    // Id: 1, 2
        captured[1].Type.Should().Be(ParamTypes.String);  // Symbol: AAPL, MSFT
    }

    [Fact]
    public void InfersIntFromAllNumericValues()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "the records exist", [], "Steps.cs", 1, "")
        };
        WriteFeatureFile("ints.feature", """
            Feature: Ints
              Scenario: Sample
                Given the records exist
                  | Id | Count |
                  | 1  | 100   |
                  | 2  | 200   |
            """);

        var columns = TableColumnAggregator.Aggregate(
            steps, ["ints.feature"], _root, NullDocGenLogger.Instance);

        columns["the records exist"].Should().AllSatisfy(c => c.Type.Should().Be(ParamTypes.Int));
    }

    [Fact]
    public void InfersDecimalWhenAnyValueHasFractionalPart()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "the prices exist", [], "Steps.cs", 1, "")
        };
        WriteFeatureFile("decimals.feature", """
            Feature: Decimals
              Scenario: Sample
                Given the prices exist
                  | Symbol | Price  |
                  | AAPL   | 150.25 |
                  | MSFT   | 300    |
            """);

        var columns = TableColumnAggregator.Aggregate(
            steps, ["decimals.feature"], _root, NullDocGenLogger.Instance);

        var price = columns["the prices exist"].Single(c => c.Name == "Price");
        price.Type.Should().Be(ParamTypes.Decimal);
    }

    [Fact]
    public void InfersBoolFromTrueFalseValues()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "the flags exist", [], "Steps.cs", 1, "")
        };
        WriteFeatureFile("bools.feature", """
            Feature: Bools
              Scenario: Sample
                Given the flags exist
                  | Name    | Enabled |
                  | Feat A  | true    |
                  | Feat B  | false   |
            """);

        var columns = TableColumnAggregator.Aggregate(
            steps, ["bools.feature"], _root, NullDocGenLogger.Instance);

        var enabled = columns["the flags exist"].Single(c => c.Name == "Enabled");
        enabled.Type.Should().Be(ParamTypes.Bool);
    }

    [Fact]
    public void InfersDateFromIso8601Values()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "the events exist", [], "Steps.cs", 1, "")
        };
        WriteFeatureFile("dates.feature", """
            Feature: Dates
              Scenario: Sample
                Given the events exist
                  | Name | Occurred   |
                  | A    | 2026-01-01 |
                  | B    | 2026-02-15 |
            """);

        var columns = TableColumnAggregator.Aggregate(
            steps, ["dates.feature"], _root, NullDocGenLogger.Instance);

        var occurred = columns["the events exist"].Single(c => c.Name == "Occurred");
        occurred.Type.Should().Be(ParamTypes.Date);
    }

    [Fact]
    public void MixedTypesInOneColumnFallBackToString()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "the records exist", [], "Steps.cs", 1, "")
        };
        WriteFeatureFile("mixed.feature", """
            Feature: Mixed
              Scenario: Sample
                Given the records exist
                  | Value |
                  | 100   |
                  | abc   |
            """);

        var columns = TableColumnAggregator.Aggregate(
            steps, ["mixed.feature"], _root, NullDocGenLogger.Instance);

        columns["the records exist"].Single().Type.Should().Be(ParamTypes.String);
    }

    [Fact]
    public void EmptyCellsAreIgnoredForInference()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "the records exist", [], "Steps.cs", 1, "")
        };
        WriteFeatureFile("empty.feature", """
            Feature: Empty
              Scenario: Sample
                Given the records exist
                  | Id |
                  | 1  |
                  |    |
                  | 2  |
            """);

        var columns = TableColumnAggregator.Aggregate(
            steps, ["empty.feature"], _root, NullDocGenLogger.Instance);

        columns["the records exist"].Single().Type.Should().Be(ParamTypes.Int);
    }

    [Fact]
    public void HeaderUnionAcrossMultipleScenarios()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "the rows exist", [], "Steps.cs", 1, "")
        };
        WriteFeatureFile("union.feature", """
            Feature: Union
              Scenario: First
                Given the rows exist
                  | A | B |
                  | 1 | 2 |

              Scenario: Second
                Given the rows exist
                  | A | C |
                  | 3 | 4 |
            """);

        var columns = TableColumnAggregator.Aggregate(
            steps, ["union.feature"], _root, NullDocGenLogger.Instance);

        columns["the rows exist"].Select(c => c.Name).Should().Equal("A", "B", "C");
    }
}
