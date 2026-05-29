# Story 16: Table Column Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture per-column metadata (column name + inferred type) for every step parameter typed as `Table` / `DataTable`. Source 1 is C# `table.CreateSet<T>()` / `CreateInstance<T>()` calls inside the binding (same-file `T` only). Source 2 is observed usage in feature files. The two are merged in `IdGenerator` with the C# declaration winning.

**Architecture:** Add an optional `Columns` field to `ParamRecord` and a new `ColumnRecord` model. The extractor attaches a C#-declared baseline when `CreateSet<T>`/`CreateInstance<T>` is found and `T` is defined in the same `.cs` file. A new `TableColumnAggregator` walks feature files (in parallel with `UsageCounter`) and produces observed columns keyed by step pattern. `IdGenerator.AssignIds` merges the two — declared wins, observed-only columns appended.

**Tech Stack:** .NET 8, C# 12, `Microsoft.CodeAnalysis.CSharp` 4.9.2 (Roslyn), Gherkin 29.0.0, xUnit 2.9.3, FluentAssertions 6.12.0, JsonSchema.Net 7.2.3.

**Prerequisite:** All prior stories complete (134 tests passing).

**V1 scope limitation:** Each step is treated as having at most one Table parameter. Multi-Table steps emit columns from the first Table parameter only. Documented as a known limitation; multi-Table support is a future enhancement.

---

## File structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Delta.DocGen/Model/ColumnRecord.cs` | Create | `(string Name, string Type)` record with `JsonPropertyName` attrs |
| `Delta.DocGen/Model/ParamRecord.cs` | Modify | Add optional `Columns: IReadOnlyList<ColumnRecord>?` |
| `Delta.DocGen/Model/ParamTypes.cs` | Modify | Add `Table = "table"`, `Bool = "bool"`, `Date = "date"` constants |
| `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json` | Modify | Make `params[].columns` optional; widen `params[].type` enum |
| `Delta.DocGen/Scanner/Gherkin/TableColumnAggregator.cs` | Create | Observed columns + inferred types per step pattern, across all feature files |
| `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs` | Modify | Detect Table param, set `type="table"`, walk for `CreateSet<T>`/`CreateInstance<T>` same-file resolution |
| `Delta.DocGen/Pipeline/IdGenerator.cs` | Modify | `AssignIds` accepts observed columns; merge declared + observed per param |
| `Delta.DocGen/Pipeline/PipelineRunner.cs` | Modify | Invoke `TableColumnAggregator`; thread result into `AssignIds` |
| `Delta.DocGen.Tests/...` | Create or modify | New tests per stage |
| `docs/developer-guide.md` | Modify | Mark Story 16 ✅; bump test count; add Table-column section to §6/§7 |

---

## Key existing types (do NOT modify their public surface beyond what is called out)

```csharp
public sealed record ParamRecord(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("example")] string Example);
// Story 16 adds the optional Columns field at the end (default null).

public static class ParamTypes
{
    public const string Int       = "int";
    public const string Decimal   = "decimal";
    public const string String    = "string";
    public const string DocString = "docstring";
    // Story 16 adds Table, Bool, Date.
}

// StepDefinitionExtractor extracts Table/DataTable params today as type="string", example="".
// Story 16 changes that to type="table" and attaches Columns (when known).

// UsageCounter.Count(steps, relativePath, root, logger) → IReadOnlyDictionary<string, int>
// TableColumnAggregator is a sibling — takes ALL feature files at once, not per-file.
```

---

## Task 1: Model — `ColumnRecord`, `ParamRecord.Columns`, `ParamTypes` additions

**Files:**
- Create: `Delta.DocGen/Model/ColumnRecord.cs`
- Modify: `Delta.DocGen/Model/ParamRecord.cs`
- Modify: `Delta.DocGen/Model/ParamTypes.cs`

- [ ] **Step 1: Create `ColumnRecord.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

public sealed record ColumnRecord(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type);
```

- [ ] **Step 2: Add `Columns` to `ParamRecord.cs`**

Read the file first. The current shape is:
```csharp
public sealed record ParamRecord(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("example")] string Example);
```

Change to:
```csharp
public sealed record ParamRecord(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("example")] string Example,
    [property: JsonPropertyName("columns")] IReadOnlyList<ColumnRecord>? Columns = null);
```

- [ ] **Step 3: Add new `ParamTypes` constants**

Open `Delta.DocGen/Model/ParamTypes.cs` and add three new constants alongside the existing ones:
```csharp
public const string Table = "table";
public const string Bool  = "bool";
public const string Date  = "date";
```

- [ ] **Step 4: Build and run existing suite**

```
dotnet build --no-incremental
dotnet test --no-build -q
```

Expected: 134 passing (existing tests still work; the new `Columns` field defaults to null and is omitted via `WhenWritingNull`).

- [ ] **Step 5: Commit**

```
git add Delta.DocGen/Model/ColumnRecord.cs Delta.DocGen/Model/ParamRecord.cs Delta.DocGen/Model/ParamTypes.cs
git commit -m "feat: ColumnRecord model + optional ParamRecord.Columns + Table/Bool/Date type constants (Story 16, task 1)"
```

---

## Task 2: JSON Schema update — `columns` optional, `type` widened

**Files:**
- Modify: `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json`
- Modify: `Delta.DocGen.Tests/Output/SchemaValidationTests.cs`

- [ ] **Step 1: Update the embedded schema**

Open `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json`. Find the `params` definition inside `$defs.step`. Currently:

```json
"params":      {
  "type": "array",
  "items": {
    "type": "object",
    "required": ["name", "type", "example"],
    "additionalProperties": false,
    "properties": {
      "name":    { "type": "string", "minLength": 1 },
      "type":    { "type": "string" },
      "example": { "type": "string" }
    }
  }
}
```

Change to:

```json
"params":      {
  "type": "array",
  "items": {
    "type": "object",
    "required": ["name", "type", "example"],
    "additionalProperties": false,
    "properties": {
      "name":    { "type": "string", "minLength": 1 },
      "type":    { "type": "string" },
      "example": { "type": "string" },
      "columns": {
        "type": "array",
        "items": {
          "type": "object",
          "required": ["name", "type"],
          "additionalProperties": false,
          "properties": {
            "name": { "type": "string", "minLength": 1 },
            "type": { "type": "string", "minLength": 1 }
          }
        }
      }
    }
  }
}
```

`columns` is NOT in `required` — it's optional.

- [ ] **Step 2: Add a validation test for `columns`**

Append to `Delta.DocGen.Tests/Output/SchemaValidationTests.cs`:

```csharp
[Fact]
public void EnvelopeWithTableParameterAndColumnsValidates()
{
    var brokenJson = System.Text.Json.Nodes.JsonNode.Parse("""
        {
          "$schema": "./schema/v1/step-library.schema.json",
          "version": "1.0.0",
          "generatedAt": "2026-05-28T09:00:00Z",
          "generatorVersion": "1.0.0",
          "enriched": false,
          "domains": [{ "id": "Auth", "label": "Auth" }],
          "steps": [{
            "id": "auth-a1b2c3d4",
            "type": "Given",
            "pattern": "the contracts exist",
            "params": [{
              "name": "contracts",
              "type": "table",
              "example": "",
              "columns": [
                { "name": "Id",     "type": "int" },
                { "name": "Symbol", "type": "string" }
              ]
            }],
            "file": "Auth/AuthSteps.cs",
            "line": 1,
            "domain": "Auth",
            "tags": [],
            "used": 0,
            "description": "",
            "source": "",
            "suggestsNext": []
          }],
          "signature": { "algorithm": "SHA-256", "digest": "0000000000000000000000000000000000000000000000000000000000000000" }
        }
        """);

    var schema = LoadSchema();
    var result = schema.Evaluate(brokenJson);

    result.IsValid.Should().BeTrue();
}

[Fact]
public void EnvelopeWithEmptyColumnsArrayValidates()
{
    var brokenJson = System.Text.Json.Nodes.JsonNode.Parse("""
        {
          "$schema": "./schema/v1/step-library.schema.json",
          "version": "1.0.0",
          "generatedAt": "2026-05-28T09:00:00Z",
          "generatorVersion": "1.0.0",
          "enriched": false,
          "domains": [{ "id": "Auth", "label": "Auth" }],
          "steps": [{
            "id": "auth-a1b2c3d4",
            "type": "Given",
            "pattern": "the contracts exist",
            "params": [{ "name": "t", "type": "table", "example": "", "columns": [] }],
            "file": "Auth/AuthSteps.cs",
            "line": 1,
            "domain": "Auth",
            "tags": [],
            "used": 0,
            "description": "",
            "source": "",
            "suggestsNext": []
          }],
          "signature": { "algorithm": "SHA-256", "digest": "0000000000000000000000000000000000000000000000000000000000000000" }
        }
        """);

    var schema = LoadSchema();
    var result = schema.Evaluate(brokenJson);

    result.IsValid.Should().BeTrue();
}
```

- [ ] **Step 3: Build and test**

```
dotnet build --no-incremental
dotnet test --no-build -q
```

Expected: 136 passing (134 + 2 new schema tests). The existing snapshot test `CanonicalOutputForKnownEnvelopeIsByteStable` continues to pass because the new `Columns` field is null and omitted via `WhenWritingNull`.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json Delta.DocGen.Tests/Output/SchemaValidationTests.cs
git commit -m "feat: JSON Schema allows optional params[].columns array (Story 16, task 2)"
```

---

## Task 3: `TableColumnAggregator` — header capture + string-only inference

**Files:**
- Create: `Delta.DocGen/Scanner/Gherkin/TableColumnAggregator.cs`
- Create: `Delta.DocGen.Tests/Scanner/Gherkin/TableColumnAggregatorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Delta.DocGen.Tests/Scanner/Gherkin/TableColumnAggregatorTests.cs
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
        // Type inference comes in Task 4 — for now everything is "string".
        captured.Should().AllSatisfy(c => c.Type.Should().Be(ParamTypes.String));
    }
}
```

- [ ] **Step 2: Run the test and verify it fails**

```
dotnet test --filter "CapturesHeadersForStepWithDataTable" -q
```

Expected: FAIL — `TableColumnAggregator` doesn't exist.

- [ ] **Step 3: Create the aggregator (string-only mode)**

```csharp
// Delta.DocGen/Scanner/Gherkin/TableColumnAggregator.cs
using System.Text;
using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Scanner.Gherkin;

public static class TableColumnAggregator
{
    private static readonly Regex CucumberPlaceholder =
        new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, IReadOnlyList<ColumnRecord>> Aggregate(
        IReadOnlyList<RawStep> steps,
        IReadOnlyList<string> featureRelativePaths,
        string root,
        IDocGenLogger logger)
    {
        // observations[pattern][columnName] -> list of raw cell values across all uses
        var observations = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        var headerOrder  = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var patterns = steps.Select(s => s.Pattern).Distinct(StringComparer.Ordinal).ToList();
        var regexes  = patterns.ToDictionary(p => p, BuildMatchRegex, StringComparer.Ordinal);

        foreach (var featurePath in featureRelativePaths)
        {
            var fullPath = Path.Combine(root, featurePath.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(fullPath, Encoding.UTF8);

            GherkinDocument doc;
            try { doc = new Parser().Parse(new StringReader(text)); }
            catch (ParserException) { continue; }

            if (doc.Feature is not { } feature) continue;

            foreach (var child in feature.Children)
            {
                switch (child)
                {
                    case Scenario sc: ObserveSteps(sc.Steps, patterns, regexes, observations, headerOrder); break;
                    case Background bg: ObserveSteps(bg.Steps, patterns, regexes, observations, headerOrder); break;
                    case Rule rule:
                        foreach (var rc in rule.Children)
                        {
                            if (rc is Scenario rs)     ObserveSteps(rs.Steps, patterns, regexes, observations, headerOrder);
                            else if (rc is Background rbg) ObserveSteps(rbg.Steps, patterns, regexes, observations, headerOrder);
                        }
                        break;
                }
            }
        }

        var result = new Dictionary<string, IReadOnlyList<ColumnRecord>>(StringComparer.Ordinal);
        foreach (var (pattern, cols) in observations)
        {
            var ordered = headerOrder[pattern];
            // String-only inference for now — Task 4 widens this.
            result[pattern] = ordered.Select(name => new ColumnRecord(name, ParamTypes.String)).ToList().AsReadOnly();
        }
        logger.Info($"Table column aggregation complete: {result.Count} step pattern(s) with observed columns.");
        return result;
    }

    private static void ObserveSteps(
        IEnumerable<Step> stepsInScenario,
        IReadOnlyList<string> patterns,
        IReadOnlyDictionary<string, Regex> regexes,
        Dictionary<string, Dictionary<string, List<string>>> observations,
        Dictionary<string, List<string>> headerOrder)
    {
        foreach (var step in stepsInScenario)
        {
            if (step.Argument is not DataTable table) continue;
            var rows = table.Rows.ToList();
            if (rows.Count == 0) continue;
            var headers = rows[0].Cells.Select(c => c.Value).ToList();

            foreach (var pattern in patterns)
            {
                if (!regexes[pattern].IsMatch(step.Text)) continue;

                if (!observations.TryGetValue(pattern, out var perColumn))
                {
                    perColumn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    observations[pattern] = perColumn;
                    headerOrder[pattern] = new List<string>(headers);
                }
                else
                {
                    foreach (var h in headers)
                        if (!headerOrder[pattern].Contains(h, StringComparer.Ordinal))
                            headerOrder[pattern].Add(h);
                }

                for (var rowIdx = 1; rowIdx < rows.Count; rowIdx++)
                {
                    var cells = rows[rowIdx].Cells.ToList();
                    for (var c = 0; c < headers.Count && c < cells.Count; c++)
                    {
                        if (!perColumn.TryGetValue(headers[c], out var values))
                        {
                            values = new List<string>();
                            perColumn[headers[c]] = values;
                        }
                        values.Add(cells[c].Value);
                    }
                }
                break;  // first-match-wins, mirrors UsageCounter
            }
        }
    }

    private static Regex BuildMatchRegex(string pattern)
    {
        if (pattern.StartsWith('^')) return new Regex(pattern);
        var sb = new StringBuilder("^");
        var lastEnd = 0;
        foreach (Match ph in CucumberPlaceholder.Matches(pattern))
        {
            sb.Append(Regex.Escape(pattern[lastEnd..ph.Index]));
            sb.Append(ph.Groups[1].Value switch
            {
                "int"     => @"\d+",
                "decimal" => @"[\d.]+",
                "string"  => @"(?:""[^""]*""|'[^']*')",
                "word"    => @"\w+",
                _         => @".+",
            });
            lastEnd = ph.Index + ph.Length;
        }
        sb.Append(Regex.Escape(pattern[lastEnd..]));
        sb.Append('$');
        return new Regex(sb.ToString());
    }
}
```

- [ ] **Step 4: Run the test and verify it passes**

```
dotnet test --filter "CapturesHeadersForStepWithDataTable" -q
```

Expected: PASS.

- [ ] **Step 5: Run full suite**

```
dotnet test -q
```

Expected: 137 passing.

- [ ] **Step 6: Commit**

```
git add Delta.DocGen/Scanner/Gherkin/TableColumnAggregator.cs Delta.DocGen.Tests/Scanner/Gherkin/TableColumnAggregatorTests.cs
git commit -m "feat: TableColumnAggregator captures DataTable headers per step pattern (string-only) (Story 16, task 3)"
```

---

## Task 4: `TableColumnAggregator` — type inference vocabulary

**Files:**
- Modify: `Delta.DocGen/Scanner/Gherkin/TableColumnAggregator.cs`
- Modify: `Delta.DocGen.Tests/Scanner/Gherkin/TableColumnAggregatorTests.cs`

- [ ] **Step 1: Add inference tests**

Append to `TableColumnAggregatorTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the new tests and verify the inference ones fail**

```
dotnet test --filter "InfersInt|InfersDecimal|InfersBool|InfersDate|MixedTypes|EmptyCells|HeaderUnion" -q
```

Expected: most FAIL (string-only mode). `HeaderUnion` and `EmptyCells` may pass already.

- [ ] **Step 3: Replace the type-inference logic in `TableColumnAggregator.cs`**

Replace the final `result` build loop in `Aggregate` with type inference:

```csharp
var result = new Dictionary<string, IReadOnlyList<ColumnRecord>>(StringComparer.Ordinal);
foreach (var (pattern, perColumn) in observations)
{
    var ordered = headerOrder[pattern];
    var records = new List<ColumnRecord>(ordered.Count);
    foreach (var colName in ordered)
    {
        var values = perColumn.TryGetValue(colName, out var v) ? v : new List<string>();
        var nonEmpty = values.Where(s => !string.IsNullOrEmpty(s)).ToList();
        records.Add(new ColumnRecord(colName, InferType(nonEmpty)));
    }
    result[pattern] = records.AsReadOnly();
}
```

Add the `InferType` helper:

```csharp
private static string InferType(IReadOnlyList<string> values)
{
    if (values.Count < 2) return ParamTypes.String;
    if (values.All(v => int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _)))
        return ParamTypes.Int;
    if (values.All(v => decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _)))
        return ParamTypes.Decimal;
    if (values.All(v => bool.TryParse(v, out _)))
        return ParamTypes.Bool;
    if (values.All(v => DateTime.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)))
        return ParamTypes.Date;
    return ParamTypes.String;
}
```

- [ ] **Step 4: Run all aggregator tests and the full suite**

```
dotnet test --filter "TableColumnAggregator" -q
dotnet test -q
```

Expected: 144 passing (137 + 7 new).

- [ ] **Step 5: Commit**

```
git add Delta.DocGen/Scanner/Gherkin/TableColumnAggregator.cs Delta.DocGen.Tests/Scanner/Gherkin/TableColumnAggregatorTests.cs
git commit -m "feat: TableColumnAggregator infers int/decimal/bool/date column types from observed values (Story 16, task 4)"
```

---

## Task 5: Extractor — recognise Table param and resolve `CreateSet<T>` / `CreateInstance<T>`

**Files:**
- Modify: `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs`
- Modify: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

- [ ] **Step 1: Add tests**

Append to `StepDefinitionExtractorTests.cs`:

```csharp
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
    p.Columns[0].Name.Should().Be("Id");
    p.Columns[0].Type.Should().Be("int");
    p.Columns[1].Name.Should().Be("Symbol");
    p.Columns[1].Type.Should().Be("string");
    p.Columns[2].Name.Should().Be("Occurred");
    p.Columns[2].Type.Should().Be("DateTime");
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
    p.Columns[0].Name.Should().Be("Amount");
    p.Columns[0].Type.Should().Be("decimal");
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
                // CrossFileType is declared in another file we don't write here.
                var rows = items.CreateSet<CrossFileType>();
            }
        }
        """);

    var steps = StepDefinitionExtractor.Extract(path, _root, NullDocGenLogger.Instance);

    var p = steps[0].Params.Single();
    p.Type.Should().Be(ParamTypes.Table);
    p.Columns.Should().BeNull();  // cross-file T → fall back to observed-only later
}
```

- [ ] **Step 2: Run the tests and verify they fail**

```
dotnet test --filter "TableParam" -q
```

Expected: all FAIL — extractor still emits Table as `string`.

- [ ] **Step 3: Update the extractor**

In `StepDefinitionExtractor.cs`, modify the `ExtractParams` method's `case "Table":` branch to:

```csharp
case "Table":
case "DataTable":
    schemaType = ParamTypes.Table;
    example = "";
    break;
```

Then in the per-method loop in `Extract`, after extracting all params for the step, walk the method body to find `CreateSet<T>` / `CreateInstance<T>` calls and resolve same-file `T`. The resolved columns get attached to the FIRST Table param (V1 limitation: one Table per step).

Add this after the `seenInMethod.Add(...)` block, right before `steps.Add(new RawStep(...))`:

```csharp
var declaredColumns = ResolveDeclaredColumns(method, compilationUnit);
if (declaredColumns is not null)
{
    @params = AttachColumnsToFirstTable(@params, declaredColumns);
}
```

And add the new helpers below `ExtractParams`:

```csharp
private static IReadOnlyList<ColumnRecord>? ResolveDeclaredColumns(
    MethodDeclarationSyntax method, CompilationUnitSyntax compilationUnit)
{
    // Look for `<id>.CreateSet<T>()` or `<id>.CreateInstance<T>()` inside the method body.
    if (method.Body is null && method.ExpressionBody is null) return null;

    var generics = method.DescendantNodes()
        .OfType<GenericNameSyntax>()
        .Where(g => g.Identifier.Text is "CreateSet" or "CreateInstance"
                 && g.TypeArgumentList.Arguments.Count == 1)
        .ToList();

    foreach (var generic in generics)
    {
        var typeName = generic.TypeArgumentList.Arguments[0].ToString();
        // Strip namespace qualifier, keep simple name.
        var simpleName = typeName.Contains('.') ? typeName[(typeName.LastIndexOf('.') + 1)..] : typeName;

        var typeDecl = compilationUnit.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == simpleName);
        if (typeDecl is null) continue;

        var columns = typeDecl.Members
            .OfType<PropertyDeclarationSyntax>()
            .Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))
                     || !p.Modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)
                                           || m.IsKind(SyntaxKind.ProtectedKeyword)
                                           || m.IsKind(SyntaxKind.InternalKeyword)))
            .Select(p => new ColumnRecord(p.Identifier.Text, p.Type.ToString()))
            .ToList();
        if (columns.Count > 0) return columns.AsReadOnly();
    }

    return null;
}

private static IReadOnlyList<ParamRecord> AttachColumnsToFirstTable(
    IReadOnlyList<ParamRecord> @params, IReadOnlyList<ColumnRecord> columns)
{
    var result = new List<ParamRecord>(@params.Count);
    var attached = false;
    foreach (var p in @params)
    {
        if (!attached && p.Type == ParamTypes.Table)
        {
            result.Add(p with { Columns = columns });
            attached = true;
        }
        else
        {
            result.Add(p);
        }
    }
    return result.AsReadOnly();
}
```

- [ ] **Step 4: Build and run the new tests**

```
dotnet build --no-incremental
dotnet test --no-build --filter "TableParam" -q
```

Expected: 4 PASS.

- [ ] **Step 5: Run the full suite**

```
dotnet test --no-build -q
```

Expected: 148 passing (144 + 4 new). Existing extractor tests that asserted Table → `string` will need to be updated to `table` (find them by running tests; they will fail). Likely affected tests in `StepDefinitionExtractorTests.cs` — search for `ParamTypes.String` next to `Table` params and update to `ParamTypes.Table`. Then re-run.

If existing tests fail, fix and recommit as part of this task.

- [ ] **Step 6: Commit**

```
git add Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs
git commit -m "feat: Extractor sets Table param type=table; resolves same-file CreateSet<T>/CreateInstance<T> to declared columns (Story 16, task 5)"
```

---

## Task 6: `IdGenerator` — merge declared + observed columns

**Files:**
- Modify: `Delta.DocGen/Pipeline/IdGenerator.cs`
- Modify: `Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs`

- [ ] **Step 1: Add tests**

Append to `IdGeneratorTests.cs`:

```csharp
[Fact]
public void DeclaredColumnsAreEmittedWhenPresent()
{
    var declared = new List<ColumnRecord> { new("Id", "int"), new("Symbol", "string") };
    var tableParam = new ParamRecord("contracts", ParamTypes.Table, "", declared);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "the contracts exist", [tableParam], "Steps.cs", 1, "", "Auth")
    };

    var records = IdGenerator.AssignIds(
        steps,
        new Dictionary<string, int>(),
        new Dictionary<string, IReadOnlyList<ColumnRecord>>(),
        NullDocGenLogger.Instance);

    var p = records[0].Params.Single();
    p.Columns.Should().NotBeNull();
    p.Columns!.Should().HaveCount(2);
    p.Columns[0].Name.Should().Be("Id");
}

[Fact]
public void ObservedColumnsAreEmittedWhenDeclaredAbsent()
{
    var tableParam = new ParamRecord("contracts", ParamTypes.Table, "", Columns: null);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "the contracts exist", [tableParam], "Steps.cs", 1, "", "Auth")
    };
    var observed = new Dictionary<string, IReadOnlyList<ColumnRecord>>
    {
        ["the contracts exist"] = new List<ColumnRecord> { new("Symbol", "string") }.AsReadOnly(),
    };

    var records = IdGenerator.AssignIds(steps, new Dictionary<string, int>(), observed, NullDocGenLogger.Instance);

    var p = records[0].Params.Single();
    p.Columns!.Should().ContainSingle();
    p.Columns[0].Name.Should().Be("Symbol");
}

[Fact]
public void ObservedColumnNotInDeclaredIsAppendedAsString()
{
    var declared = new List<ColumnRecord> { new("Id", "int") };
    var tableParam = new ParamRecord("contracts", ParamTypes.Table, "", declared);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "the contracts exist", [tableParam], "Steps.cs", 1, "", "Auth")
    };
    var observed = new Dictionary<string, IReadOnlyList<ColumnRecord>>
    {
        ["the contracts exist"] = new List<ColumnRecord>
        {
            new("Id",        "int"),
            new("ExtraName", "string"),
        }.AsReadOnly(),
    };

    var logger = new CapturingDocGenLogger();
    var records = IdGenerator.AssignIds(steps, new Dictionary<string, int>(), observed, logger);

    var p = records[0].Params.Single();
    p.Columns!.Should().HaveCount(2);
    p.Columns[0].Name.Should().Be("Id");
    p.Columns[1].Name.Should().Be("ExtraName");
    logger.VerboseMessages.Should().Contain(m => m.Contains("ExtraName"));
}

[Fact]
public void TableParamWithNoColumnSourcesEmitsEmptyColumns()
{
    var tableParam = new ParamRecord("contracts", ParamTypes.Table, "", Columns: null);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "the contracts exist", [tableParam], "Steps.cs", 1, "", "Auth")
    };

    var records = IdGenerator.AssignIds(
        steps,
        new Dictionary<string, int>(),
        new Dictionary<string, IReadOnlyList<ColumnRecord>>(),
        NullDocGenLogger.Instance);

    var p = records[0].Params.Single();
    p.Columns.Should().NotBeNull();
    p.Columns!.Should().BeEmpty();
}
```

- [ ] **Step 2: Run the tests and verify they fail**

```
dotnet test --filter "DeclaredColumns|ObservedColumns|ObservedColumnNotIn|TableParamWithNoColumn" -q
```

Expected: FAIL — `AssignIds` doesn't accept the third dictionary parameter yet.

- [ ] **Step 3: Modify `IdGenerator.AssignIds` signature**

Open `Delta.DocGen/Pipeline/IdGenerator.cs`. Change the signature to:

```csharp
public static IReadOnlyList<StepRecord> AssignIds(
    IReadOnlyList<RawStep> steps,
    IReadOnlyDictionary<string, int> usageCounts,
    IReadOnlyDictionary<string, IReadOnlyList<ColumnRecord>> observedColumns,
    IDocGenLogger logger)
```

After building the `StepRecord` per step but before adding to `records`, transform the params to merge columns. Add a helper near the bottom of the class:

```csharp
private static IReadOnlyList<ParamRecord> MergeColumns(
    IReadOnlyList<ParamRecord> @params,
    IReadOnlyList<ColumnRecord>? observed,
    IDocGenLogger logger)
{
    var result = new List<ParamRecord>(@params.Count);
    var observedAttached = false;
    foreach (var p in @params)
    {
        if (p.Type != ParamTypes.Table)
        {
            result.Add(p);
            continue;
        }

        if (p.Columns is { Count: > 0 } declared)
        {
            if (observed is not null && !observedAttached)
            {
                var declaredNames = new HashSet<string>(declared.Select(c => c.Name), StringComparer.Ordinal);
                var extra = observed.Where(o => !declaredNames.Contains(o.Name)).ToList();
                if (extra.Count > 0)
                {
                    logger.Verbose(
                        $"Observed columns not in declared type: {string.Join(", ", extra.Select(e => e.Name))} — appending as observed-string.");
                    var merged = declared.Concat(extra.Select(e => new ColumnRecord(e.Name, "string"))).ToList();
                    result.Add(p with { Columns = merged.AsReadOnly() });
                    observedAttached = true;
                    continue;
                }
            }
            result.Add(p);  // declared as-is
            observedAttached = true;
        }
        else
        {
            // No declared columns — emit observed (or empty).
            if (observed is not null && !observedAttached)
            {
                result.Add(p with { Columns = observed });
                observedAttached = true;
            }
            else
            {
                result.Add(p with { Columns = Array.Empty<ColumnRecord>() });
            }
        }
    }
    return result.AsReadOnly();
}
```

Then in the main loop, replace the `Params: step.Params,` line with:

```csharp
Params:      MergeColumns(step.Params,
                          observedColumns.TryGetValue(step.Pattern, out var obs) ? obs : null,
                          logger),
```

- [ ] **Step 4: Update existing IdGenerator tests that called AssignIds with the old signature**

There are several existing tests. Update each call site to pass an empty observed-columns dict:

```csharp
IdGenerator.AssignIds(steps, usageCounts, new Dictionary<string, IReadOnlyList<ColumnRecord>>(), logger)
```

Use find-and-replace across `IdGeneratorTests.cs` to fix all call sites.

- [ ] **Step 5: Build and run**

```
dotnet build --no-incremental
dotnet test --no-build -q
```

Expected: 152 passing (148 + 4 new). All existing IdGenerator tests still pass after the signature update.

- [ ] **Step 6: Commit**

```
git add Delta.DocGen/Pipeline/IdGenerator.cs Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs
git commit -m "feat: IdGenerator merges declared + observed table columns (Story 16, task 6)"
```

---

## Task 7: `PipelineRunner` — wire `TableColumnAggregator`

**Files:**
- Modify: `Delta.DocGen/Pipeline/PipelineRunner.cs`
- Modify: `Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs`

- [ ] **Step 1: Add an end-to-end test**

Append to `PipelineRunnerTests.cs`:

```csharp
[Fact]
public void PipelineEmitsTableColumnsFromCreateSetAnnotation()
{
    File.WriteAllText(Path.Combine(_root, "Auth", "TableSteps.cs"), """
        using Reqnroll;

        public sealed class Contract
        {
            public int Id { get; set; }
            public string Symbol { get; set; } = "";
        }

        public class TableSteps
        {
            [Given("the contracts exist")]
            public void GivenContractsExist(Table contracts)
            {
                var rows = contracts.CreateSet<Contract>();
            }
        }
        """);

    var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

    result.Success.Should().BeTrue();
    var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_output));
    var steps = json.RootElement.GetProperty("steps").EnumerateArray()
        .Where(s => s.GetProperty("pattern").GetString() == "the contracts exist")
        .ToList();
    steps.Should().ContainSingle();
    var tableParam = steps[0].GetProperty("params")[0];
    tableParam.GetProperty("type").GetString().Should().Be("table");
    var columns = tableParam.GetProperty("columns").EnumerateArray().ToList();
    columns.Should().HaveCount(2);
    columns[0].GetProperty("name").GetString().Should().Be("Id");
    columns[0].GetProperty("type").GetString().Should().Be("int");
    columns[1].GetProperty("name").GetString().Should().Be("Symbol");
    columns[1].GetProperty("type").GetString().Should().Be("string");
}

[Fact]
public void PipelineEmitsObservedTableColumnsWhenNoAnnotation()
{
    File.WriteAllText(Path.Combine(_root, "Auth", "NoAnnotation.cs"), """
        using Reqnroll;
        public class NoAnnotation
        {
            [Given("the rows exist")]
            public void GivenRowsExist(Table rows) { }
        }
        """);
    File.WriteAllText(Path.Combine(_root, "Features", "rows.feature"), """
        Feature: Rows
          Scenario: Sample
            Given the rows exist
              | Id | Name |
              | 1  | A    |
              | 2  | B    |
        """);

    var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

    result.Success.Should().BeTrue();
    var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_output));
    var step = json.RootElement.GetProperty("steps").EnumerateArray()
        .Single(s => s.GetProperty("pattern").GetString() == "the rows exist");
    var columns = step.GetProperty("params")[0].GetProperty("columns").EnumerateArray().ToList();
    columns.Should().HaveCount(2);
    columns[0].GetProperty("name").GetString().Should().Be("Id");
    columns[0].GetProperty("type").GetString().Should().Be("int");
    columns[1].GetProperty("name").GetString().Should().Be("Name");
    columns[1].GetProperty("type").GetString().Should().Be("string");
}
```

- [ ] **Step 2: Run the tests and verify they fail**

```
dotnet test --filter "PipelineEmitsTableColumns|PipelineEmitsObservedTableColumns" -q
```

Expected: FAIL — runner doesn't invoke the aggregator yet.

- [ ] **Step 3: Wire the aggregator into `PipelineRunner.Run`**

In `Delta.DocGen/Pipeline/PipelineRunner.cs`, after the existing Stage 4 usage-counter block and before Stage 5 (DomainAssigner), insert:

```csharp
// Stage 4b: observed table columns
var observedColumns = TableColumnAggregator.Aggregate(
    rawSteps, discovery.FeatureFiles, config.Root, unmatchedCounter);
```

Change the `IdGenerator.AssignIds` call to pass `observedColumns`:

```csharp
var stepRecords = IdGenerator.AssignIds(domainAssigned, totalUsage, observedColumns, unmatchedCounter);
```

Add `using Delta.DocGen.Scanner.Gherkin;` if not already imported.

- [ ] **Step 4: Build and run**

```
dotnet build --no-incremental
dotnet test --no-build -q
```

Expected: 154 passing (152 + 2 new e2e).

- [ ] **Step 5: Commit**

```
git add Delta.DocGen/Pipeline/PipelineRunner.cs Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs
git commit -m "feat: PipelineRunner threads TableColumnAggregator output into IdGenerator (Story 16, task 7)"
```

---

## Task 8: Developer guide + final smoke test

**Files:**
- Modify: `docs/developer-guide.md`

- [ ] **Step 1: Add Story 16 row**

In §9 story-by-story table, append:

```
| 16 | Table column metadata | `ColumnRecord`, `TableColumnAggregator`, extractor + IdGenerator changes | ✅ |
```

Bump the overview range:
```
| 1–16 | ✅ Complete, merged to master, pushed to GitHub |
```

- [ ] **Step 2: Update the test count**

```
**Test count:** 154 passing (Stories 1–16 + TD-C/D debt fixes + smoke-test-driven fixes)
```

- [ ] **Step 3: Add a `Table columns` subsection to §7 output format**

Insert after the existing param example block:

```markdown
### Table column metadata (Story 16)

A parameter typed as `Table` / `DataTable` carries an additional `columns` array:

```jsonc
{
  "name": "contracts",
  "type": "table",
  "example": "",
  "columns": [
    { "name": "Id",     "type": "int" },
    { "name": "Symbol", "type": "string" }
  ]
}
```

Sources, in precedence order:

1. **C# declared** — if the binding method body calls `<table>.CreateSet<T>()` or `<table>.CreateInstance<T>()` and `T` is declared in the **same `.cs` file**, columns are the public properties of `T` with their declared C# types.
2. **Feature-file observed** — otherwise, columns are the union of headers seen in feature files using the step, with types inferred from observed values (`int` → `decimal` → `bool` → `date` → `string`).

If a Table parameter has neither annotation nor feature-file usage, `columns` is an empty array. Cross-file `CreateSet<T>` references fall back to observed-only.
```

- [ ] **Step 4: Run full suite to confirm**

```
dotnet test -q
```

Expected: 154 passing.

- [ ] **Step 5: Optional — re-run the live smoke test against `C:\dev\triangle\Step Definitions`**

```
dotnet build --no-incremental
dotnet run --project Delta.DocGen --no-build -- --config "C:/dev/Delta.DocGen/smoketest/triangle.docgen.config.json" --verbosity silent 2>&1 | tail -5
```

Expected: pipeline completes; the produced JSON now contains `columns` arrays on Table parameters where `CreateSet<T>` annotations exist with same-file types.

- [ ] **Step 6: Commit and push**

```
git add docs/developer-guide.md
git commit -m "docs: mark Story 16 complete; document table column metadata (Story 16, task 8)"
git push
```
