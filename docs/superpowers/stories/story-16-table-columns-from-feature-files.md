# Story 16: Table column extraction from feature files

**Type:** Enhancement  
**Priority:** High (V1.1)  
**Approach:** A — extract column names from Gherkin AST during feature file parsing

---

## User story

*As a Delta.DocView viewer developer, I can see the column names of any data table associated with a step, so that the viewer can display meaningful table structure rather than an empty params list.*

---

## Background

When a step definition accepts a `Table` parameter (Reqnroll/SpecFlow), the C# method signature carries no column information — the columns only exist in the Gherkin feature file. For example:

```gherkin
Given I place the following orders
  | Symbol | Quantity | Price |
  | LLOY   | 1000     | 52p   |
```

```csharp
[Given("I place the following orders")]
public void GivenIPlaceTheFollowingOrders(Table table) { }
```

Currently the `params` array for this step is empty `[]`. After this story, it will be:

```jsonc
"params": [
  {
    "name": "table",
    "type": "Table",
    "example": "",
    "columns": ["Symbol", "Quantity", "Price"]
  }
]
```

---

## Acceptance criteria

1. A `ParamRecord` with `type: "Table"` gains an optional `columns` field containing the ordered list of column header strings observed in the first matching feature file usage.
2. If the same Table step appears in multiple feature files with different column sets, the **union** of all column names is stored (ordered by first appearance).
3. If a Table step appears in feature files but with no data table (step text matched but no table rows follow), `columns` is an empty array `[]`.
4. Steps with `Table`/`DataTable` parameters that are **never used** in any feature file have `columns: []`.
5. The output JSON schema is updated: `params[].columns` is an optional `array` of `string` (MINOR version bump — backwards compatible).
6. The schema `version` field is bumped from `1.0.0` to `1.1.0`.
7. All existing tests continue to pass. New tests cover: single-file column extraction, multi-file union, unmatched table step, step with no table argument.

---

## Schema change (MINOR — backwards compatible)

### `ParamRecord` — add optional `columns` field

```csharp
public sealed record ParamRecord(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("example")] string Example,
    [property: JsonPropertyName("columns")] IReadOnlyList<string>? Columns = null
);
```

`Columns` is `null` for non-Table params (omitted from JSON output via `JsonIgnore(Condition = WhenWritingNull)`), and an array for `Table` params (may be empty).

---

## Implementation plan

### Files to modify

| File | Change |
|------|--------|
| `Delta.DocGen/Model/ParamRecord.cs` | Add `IReadOnlyList<string>? Columns` with `[JsonIgnore(Condition = WhenWritingNull)]` |
| `Delta.DocGen/Model/ParamTypes.cs` | Add `public const string Table = "Table"` |
| `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs` | Emit `type: "Table"` (not `"string"`) for `Table`/`DataTable` params; `Columns` is `[]` at extraction time |
| `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs` | When a matched step has a `Table` param, capture `step.Argument` as `DataTable` and harvest header row column names; return alongside usage counts |
| `Delta.DocGen/Pipeline/PipelineRunner.cs` | After `UsageCounter`, merge table column data back onto `RawStep.Params` |
| `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json` | Add `columns` to param schema; bump version to `1.1.0` |
| `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs` | Add table column tests |
| `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs` | Add test: `Table` param emits `type: "Table"` |

### Step-by-step

#### Step 1: Add `Table` to `ParamTypes`

```csharp
public static class ParamTypes
{
    public const string String    = "string";
    public const string Int       = "int";
    public const string Decimal   = "decimal";
    public const string DocString = "DocString";
    public const string Table     = "Table";      // ← new
}
```

#### Step 2: Update `ParamRecord`

```csharp
using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

/// <summary>A single parameter on a step definition.</summary>
/// <param name="Name">Parameter name as declared in the C# method signature.</param>
/// <param name="Type">Schema type: string | int | decimal | DocString | Table.</param>
/// <param name="Example">Default example value; empty until LLM enrichment (v2).</param>
/// <param name="Columns">Column headers for Table params; null for all other types.</param>
public sealed record ParamRecord(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("example")] string Example,
    [property: JsonPropertyName("columns"),
     property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Columns = null
);
```

#### Step 3: Update `StepDefinitionExtractor` — emit `Table` type

In the `switch` on `csType`, replace:

```csharp
case "Table":
case "DataTable":
case "ScenarioContext":
    schemaType = ParamTypes.String;
    example = "";
    break;
```

With:

```csharp
case "Table":
case "DataTable":
    schemaType = ParamTypes.Table;
    example = "";
    // Columns are populated later by UsageCounter
    result.Add(new ParamRecord(name, schemaType, example, []));
    continue;
case "ScenarioContext":
    schemaType = ParamTypes.String;
    example = "";
    break;
```

#### Step 4: Update `UsageCounter` — return table columns alongside counts

Change the return type to a new result record:

```csharp
public sealed record UsageResult(
    IReadOnlyDictionary<string, int> Counts,
    // Maps pattern → ordered union of column names seen across all usages
    IReadOnlyDictionary<string, IReadOnlyList<string>> TableColumns
);
```

In `MatchSteps`, when a step matches and `step.Argument` is a `DataTable`:

```csharp
if (step.Argument is DataTable dt)
{
    var headers = dt.Rows.First().Cells.Select(c => c.Value).ToList();
    // merge into tableColumns[pattern] union
}
```

#### Step 5: Update `PipelineRunner` — merge columns onto params

After `UsageCounter` returns, for each `RawStep` whose pattern appears in `TableColumns`, update any `ParamRecord` with `type == "Table"` to set `Columns` to the union list.

#### Step 6: Update JSON Schema

- Add `"columns"` as optional array of strings to the param `$defs`
- Bump `version` default/example to `1.1.0`

#### Step 7: Write tests

**`UsageCounterTests`:**
- Table step matched with header row → `TableColumns` contains column names
- Table step matched across two feature files → union of columns returned
- Table step matched but no data table argument → `TableColumns[pattern]` is `[]`
- Non-table step → not present in `TableColumns`

**`StepDefinitionExtractorTests`:**
- Method with `Table table` param → `ParamRecord` has `type: "Table"`, `Columns: []`

#### Step 8: Commit

```bash
git commit -m "feat: extract table column names from feature files (Story 16)

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

## Output example

```jsonc
{
  "id": "ord-f3a1b2c4",
  "type": "Given",
  "pattern": "I place the following orders",
  "params": [
    {
      "name": "table",
      "type": "Table",
      "example": "",
      "columns": ["Symbol", "Quantity", "Price"]
    }
  ],
  "file": "StepDefinitions/OrderSteps.cs",
  "line": 14,
  "domain": "Orders",
  "tags": [],
  "used": 3,
  "description": "",
  "source": "...",
  "suggestsNext": []
}
```

---

## Version bump

`step-library.v1.schema.json` version: `1.0.0` → `1.1.0`

This is a MINOR bump — `columns` is optional and absent on non-Table params. Viewers that ignore unknown fields (as required by the versioning spec) will continue to work without modification.
