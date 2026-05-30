using Delta.DocGen.Scanner.CSharp;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.DocGen.Tests.Scanner.CSharp;

public sealed class TableColumnInferrerTests
{
    private static MethodDeclarationSyntax ParseMethod(string body)
    {
        var src = $"class C {{ {body} }}";
        return CSharpSyntaxTree.ParseText(src)
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();
    }

    [Fact]
    public void InfersColumnsFromGetStringCalls()
    {
        var method = ParseMethod(@"
            [Given(""x"")]
            public void Step(Table table) {
                var a = table.GetString(""Symbol"");
                var b = table.GetInt(""Quantity"");
            }");
        var cols = TableColumnInferrer.Infer(method, "table");
        cols.Should().Equal("Symbol", "Quantity");
    }

    [Fact]
    public void InfersColumnsFromTableIndexer()
    {
        var method = ParseMethod(@"
            [Given(""x"")]
            public void Step(Table table) {
                var v = table[""Price""];
            }");
        var cols = TableColumnInferrer.Infer(method, "table");
        cols.Should().Equal("Price");
    }

    [Fact]
    public void InfersColumnsFromRowIteration()
    {
        var method = ParseMethod(@"
            [Given(""x"")]
            public void Step(Table table) {
                foreach (var row in table.Rows) {
                    var s = row[""Symbol""];
                    var q = row[""Qty""];
                }
            }");
        var cols = TableColumnInferrer.Infer(method, "table");
        cols.Should().Equal("Symbol", "Qty");
    }

    [Fact]
    public void DeduplicatesColumns()
    {
        var method = ParseMethod(@"
            [Given(""x"")]
            public void Step(Table table) {
                var a = table.GetString(""Name"");
                var b = table[""Name""];
            }");
        var cols = TableColumnInferrer.Infer(method, "table");
        cols.Should().Equal("Name");
    }

    [Fact]
    public void ReturnsEmptyWhenNoTableAccess()
    {
        var method = ParseMethod(@"
            [Given(""x"")]
            public void Step(Table table) {
                // nothing
            }");
        var cols = TableColumnInferrer.Infer(method, "table");
        cols.Should().BeEmpty();
    }

    [Fact]
    public void IgnoresNonTableParams()
    {
        var method = ParseMethod(@"
            [Given(""x"")]
            public void Step(Table table, string other) {
                var v = other[""NotAColumn""];
            }");
        var cols = TableColumnInferrer.Infer(method, "table");
        cols.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotMatchRowsCollectionProperty()
    {
        var method = ParseMethod(@"
            [Given(""x"")]
            public void Step(Table table) {
                foreach (var row in table.RowsCollection) {
                    var v = row[""Col""];
                }
            }");
        // RowsCollection is not table.Rows — should not track loop var
        var cols = TableColumnInferrer.Infer(method, "table");
        cols.Should().BeEmpty();
    }

    [Fact]
    public void MixedPatternsUnionedInOrder()
    {
        var method = ParseMethod(@"
            [Given(""x"")]
            public void Step(Table table) {
                var a = table.GetString(""A"");
                foreach (var row in table.Rows) {
                    var b = row[""B""];
                }
                var c = table[""C""];
            }");
        var cols = TableColumnInferrer.Infer(method, "table");
        cols.Should().Equal("A", "B", "C");
    }
}
