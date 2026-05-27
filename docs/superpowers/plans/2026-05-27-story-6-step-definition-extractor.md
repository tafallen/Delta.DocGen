# Story 6 — StepDefinitionExtractor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `StepDefinitionExtractor` — a static class that reads a `.cs` file and returns all `[Given]`/`[When]`/`[Then]` step definitions as `RawStep` records using Roslyn syntax analysis.

**Architecture:** LINQ over Roslyn nodes (`DescendantNodes().OfType<MethodDeclarationSyntax>()`). No semantic model, no project references, no `SyntaxWalker` subclass. Static class matching the existing `Discoverer` pattern. One `RawStep` per step attribute found.

**Tech Stack:** `Microsoft.CodeAnalysis.CSharp` 4.9.2 (already in project), xUnit + FluentAssertions for tests.

---

## File Map

| Action | Path |
|--------|------|
| Create | `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs` |
| Create | `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs` |

---

## Task 1: Scaffold — stub implementation and first failing test

**Files:**
- Create: `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs`
- Create: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

- [ ] **Step 1: Create the stub implementation**

```csharp
// Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Scanner.CSharp;

public static class StepDefinitionExtractor
{
    private static readonly HashSet<string> StepAttributeNames =
        new(StringComparer.Ordinal) { "Given", "When", "Then" };

    private static readonly Regex PlaceholderPattern =
        new(@"\{[^}]+\}", RegexOptions.Compiled);

    public static IReadOnlyList<RawStep> Extract(
        string relativePath, string root, IDocGenLogger logger)
    {
        logger.Info($"  {relativePath}: 0 step(s)");
        return Array.Empty<RawStep>();
    }
}
```

- [ ] **Step 2: Create the test file with the first test**

```csharp
// Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs
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
}
```

- [ ] **Step 3: Verify the build succeeds and the test fails**

```
dotnet build Delta.DocGen.sln
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~ExtractsGivenStepWithStringParam" -v minimal
```

Expected: build succeeds; test fails with `Expected collection to contain 1 item(s), but found 0.`

---

## Task 2: Implement core extraction (Given/When/Then, string params)

**Files:**
- Modify: `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs`

- [ ] **Step 1: Replace the stub body with the full implementation**

```csharp
// Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Scanner.CSharp;

public static class StepDefinitionExtractor
{
    private static readonly HashSet<string> StepAttributeNames =
        new(StringComparer.Ordinal) { "Given", "When", "Then" };

    private static readonly Regex PlaceholderPattern =
        new(@"\{[^}]+\}", RegexOptions.Compiled);

    public static IReadOnlyList<RawStep> Extract(
        string relativePath, string root, IDocGenLogger logger)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(fullPath);
        var compilationUnit = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();

        var steps = new List<RawStep>();

        foreach (var method in compilationUnit.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            foreach (var attrList in method.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var name = GetUnqualifiedName(attr);
                    if (!StepAttributeNames.Contains(name)) continue;

                    var pattern = ExtractPattern(attr);
                    if (pattern is null)
                    {
                        logger.Warn($"[{name}] at {relativePath} has no string argument — skipping.");
                        continue;
                    }

                    var @params = ExtractParams(method.ParameterList, pattern);
                    var line = attr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var source = method.ToString();

                    steps.Add(new RawStep(name, pattern, @params, relativePath, line, source));
                    logger.Verbose($"  [{name}] {pattern} at {relativePath}:{line}");
                }
            }
        }

        logger.Info($"  {relativePath}: {steps.Count} step(s)");
        return steps.AsReadOnly();
    }

    private static string GetUnqualifiedName(AttributeSyntax attr)
    {
        var fullName = attr.Name.ToString();
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }

    private static string? ExtractPattern(AttributeSyntax attr)
    {
        if (attr.ArgumentList is null) return null;
        foreach (var arg in attr.ArgumentList.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax lit &&
                lit.IsKind(SyntaxKind.StringLiteralExpression))
                return lit.Token.ValueText;
        }
        return null;
    }

    private static IReadOnlyList<ParamRecord> ExtractParams(
        ParameterListSyntax paramList, string pattern)
    {
        var placeholders = PlaceholderPattern.Matches(pattern);
        var placeholderIndex = 0;
        var result = new List<ParamRecord>();

        foreach (var param in paramList.Parameters)
        {
            var csType = param.Type?.ToString() ?? "string";
            var name = param.Identifier.Text;
            string schemaType;
            string example;

            switch (csType)
            {
                case "int":
                    schemaType = "int";
                    example = "0";
                    placeholderIndex++;
                    break;
                case "decimal":
                    schemaType = "decimal";
                    example = "0.00";
                    placeholderIndex++;
                    break;
                default:
                    schemaType = placeholderIndex < placeholders.Count ? "string" : "DocString";
                    example = "";
                    placeholderIndex++;
                    break;
            }

            result.Add(new ParamRecord(name, schemaType, example));
        }

        return result.AsReadOnly();
    }
}
```

- [ ] **Step 2: Run the first test — verify it passes**

```
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~ExtractsGivenStepWithStringParam" -v minimal
```

Expected: `Passed!`

- [ ] **Step 3: Add tests for When/Then types and no-param steps**

Add these tests to `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`:

```csharp
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
```

- [ ] **Step 4: Run new test — verify it passes**

```
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~ExtractsWhenAndThenTypes" -v minimal
```

Expected: `Passed!`

- [ ] **Step 5: Commit**

```
git add Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs
git commit -m "feat: implement StepDefinitionExtractor core extraction (Story 6)"
```

---

## Task 3: int and decimal param types

**Files:**
- Modify: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

The implementation already handles `int` and `decimal` — this task adds tests to confirm.

- [ ] **Step 1: Add the test**

```csharp
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
```

- [ ] **Step 2: Run the test — verify it passes**

```
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~MapsIntAndDecimalParamTypes" -v minimal
```

Expected: `Passed!`

---

## Task 4: DocString param detection

**Files:**
- Modify: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

A `string` parameter with no remaining `{…}` placeholder in the pattern is typed `"DocString"`.

- [ ] **Step 1: Add the test**

```csharp
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
```

- [ ] **Step 2: Add a test for a method with one {string} placeholder AND a DocString param**

This verifies the placeholder consumption logic: the first `string` param gets `"string"`, the second gets `"DocString"`.

```csharp
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
```

- [ ] **Step 3: Run both tests — verify they pass**

```
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~DocString" -v minimal
```

Expected: 2 tests, `Passed!`

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs
git commit -m "test: add int/decimal and DocString param type tests (Story 6)"
```

---

## Task 5: Multiple attributes, no attributes, multiple methods

**Files:**
- Modify: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

- [ ] **Step 1: Add test — multiple step attributes on one method**

One `RawStep` is produced per attribute.

```csharp
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
```

- [ ] **Step 2: Add test — file with no step attributes returns empty**

```csharp
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
```

- [ ] **Step 3: Add test — multiple step-bearing methods**

```csharp
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
    steps.Select(s => s.Type).Should().BeEquivalentTo(["Given", "When", "Then"]);
}
```

- [ ] **Step 4: Run all three tests — verify they pass**

```
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~ProducesOneRawStepPerAttribute|FullyQualifiedName~ReturnsEmptyForFileWithNoStepAttributes|FullyQualifiedName~ExtractsStepsFromMultipleMethods" -v minimal
```

Expected: 3 tests, `Passed!`

---

## Task 6: Reqnroll namespace, source field, and line number

**Files:**
- Modify: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

- [ ] **Step 1: Add test — Reqnroll fully-qualified attribute name**

Extraction matches on the unqualified name only, so `[Reqnroll.Given(...)]` and `[Given(...)]` are treated identically.

```csharp
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
    steps[0].Type.Should().Be("Given");
    steps[0].Pattern.Should().Be("I use reqnroll");
}
```

- [ ] **Step 2: Add test — source contains attribute, signature, and body**

```csharp
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
```

- [ ] **Step 3: Add test — line number is 1-based and points to the attribute**

```csharp
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
```

- [ ] **Step 4: Run all three tests — verify they pass**

```
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~ExtractsReqnrollQualifiedAttributeByName|FullyQualifiedName~SourceContainsFullMethodText|FullyQualifiedName~LineNumberIsOneBasedAndMatchesAttribute" -v minimal
```

Expected: 3 tests, `Passed!`

- [ ] **Step 5: Run the full test suite — verify everything is still green**

```
dotnet test Delta.DocGen.sln -v minimal
```

Expected: all tests pass, 0 warnings (build is `TreatWarningsAsErrors`).

- [ ] **Step 6: Commit**

```
git add Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs
git commit -m "test: complete Story 6 test suite — Reqnroll, source, line number, edge cases"
```

---

## Task 7: Update developer guide progress table

**Files:**
- Modify: `docs/developer-guide.md`

- [ ] **Step 1: Mark Story 6 complete and update test count**

In `docs/developer-guide.md`, update the repository layout section (change `⬜ Story 6` to `✅ done`):

```
│   ├── Scanner/
│   │   ├── CSharp/
│   │   │   └── StepDefinitionExtractor.cs          ✅ done
```

Update the implementation progress table (§9) — Story 6 row:

```
| 6 | C# step extraction | `StepDefinitionExtractor` (Roslyn) + tests | ✅ |
```

Update the test count line (currently `**Test count:** 22 passing (13 config, 9 discoverer)`):

```
**Test count:** 33 passing (13 config, 9 discoverer, 11 extractor)
```

Update `**Implementation status:** Stories 1–5 complete` → `Stories 1–6 complete`.

Update `**Last updated:**` date to `2026-05-27`.

Update `### What's next` section to point to Story 7 (UsageCounter).

- [ ] **Step 2: Commit**

```
git add docs/developer-guide.md
git commit -m "docs: update developer guide — Story 6 complete"
```
