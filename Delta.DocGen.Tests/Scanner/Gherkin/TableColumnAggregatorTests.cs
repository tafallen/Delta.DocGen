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
        // String-only inference in this task — Task 4 widens this.
        captured.Should().AllSatisfy(c => c.Type.Should().Be(ParamTypes.String));
    }
}
