# Story 17: Table column extraction from C# method bodies (Roslyn)

**Type:** Enhancement  
**Priority:** Medium (V1.2)  
**Approach:** B — infer column names from Roslyn AST analysis of the method body  
**Depends on:** Story 16 (Table param type and `columns` field must exist)

---

## User story

*As a Delta.DocView viewer developer, I can see table column names even for steps that are defined but not yet used in any feature file, so that the viewer shows meaningful structure for all steps including unused ones.*

---

## Background

Story 16 extracts column names from feature file data tables. However, this only works for steps that are **actually used** in at least one `.feature` file. Steps that exist in step-definition files but have no corresponding feature file usage will have `columns: []`.

This story fills that gap by using Roslyn to analyse the C# method body and infer column names from how the `Table` object is accessed. This is a best-effort approach — it works for common patterns but cannot cover all possible coding styles.

---

## Patterns to detect

### Pattern 1: `table.GetString("ColumnName")` / `table.GetInt(...)` etc.

```csharp
[Given("I place the following orders")]
public void GivenIPlaceOrders(Table table)
{
    var symbol = table.GetString("Symbol");
    var qty = table.GetInt("Quantity");
}
```
→ columns: `["Symbol", "Quantity"]`

### Pattern 2: `table["ColumnName"]` indexer

```csharp
var price = row["Price"];
```
→ column: `"Price"`

### Pattern 3: `table.CreateSet<T>()` / `table.CreateInstance<T>()`

```csharp
var orders = table.CreateSet<OrderDto>();
```

Resolve `OrderDto` to find its public settable properties → use property names as columns. This requires Roslyn semantic analysis (symbol resolution), which requires loading project references — **defer this sub-pattern to a future story** if it proves complex.

### Pattern 4: `table.Rows` iteration with column name access

```csharp
foreach (var row in table.Rows)
    var val = row["ColumnName"];
```
→ column: `"ColumnName"`

---

## Acceptance criteria

1. For any `Table` param on a step with no feature-file-derived columns (i.e. `columns` is still `[]` after Story 16 runs), Roslyn analysis of the method body is attempted.
2. String literal arguments to `GetString`, `GetInt`, `GetDecimal`, `GetDouble`, `GetBool`, `GetLong` calls on the table parameter are extracted as column names.
3. String literal indexer accesses (`table["col"]`, `row["col"]`) on the table parameter or its row iteration variables are extracted as column names.
4. Columns inferred from Roslyn are merged with any columns already found from feature files (union, preserving order — feature file columns first).
5. A new `columnsSource` field (optional, `"feature" | "roslyn" | "both"`) is added to `ParamRecord` to indicate provenance — useful for the viewer to signal confidence.
6. If no column names can be inferred from either source, `columns: []` is emitted (unchanged from Story 16 behaviour).
7. `CreateSet<T>()` / `CreateInstance<T>()` with a concrete DTO type: property names are extracted **without** loading project references — using Roslyn's syntax tree only (look up the type declaration within the same file or same compilation unit). Full semantic resolution (cross-file DTOs) is deferred.
8. All Story 16 tests continue to pass. New tests cover each pattern above.

---

## Schema change (MINOR — backwards compatible)

### Add `columnsSource` to `ParamRecord`

```csharp
public sealed record ParamRecord(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("example")] string Example,
    [property: JsonPropertyName("columns"),
     property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Columns = null,
    [property: JsonPropertyName("columnsSource"),
     property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ColumnsSource = null   // "feature" | "roslyn" | "both"
);
```

Schema version bump: `1.1.0` → `1.2.0`

---

## Implementation plan

### Files to modify

| File | Change |
|------|--------|
| `Delta.DocGen/Model/ParamRecord.cs` | Add `string? ColumnsSource` |
| `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs` | After extracting params, perform Roslyn body analysis for `Table` params; populate `Columns` and `ColumnsSource` |
| `Delta.DocGen/Scanner/CSharp/TableColumnInferrer.cs` | **New file** — isolated Roslyn walker for table column inference |
| `Delta.DocGen/Pipeline/PipelineRunner.cs` | Merge Roslyn-inferred columns with feature-file columns; set `ColumnsSource` |
| `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json` | Add `columnsSource` to param schema; bump to `1.2.0` |
| `Delta.DocGen.Tests/Scanner/CSharp/TableColumnInferrerTests.cs` | **New file** — unit tests for each inference pattern |

### New file: `TableColumnInferrer.cs`

```csharp
namespace Delta.DocGen.Scanner.CSharp;

/// <summary>
/// Analyses a Roslyn method body to infer data table column names
/// from common Reqnroll/SpecFlow Table access patterns.
/// </summary>
public static class TableColumnInferrer
{
    /// <summary>
    /// Returns ordered column names inferred from the method body,
    /// or an empty list if none can be determined.
    /// </summary>
    public static IReadOnlyList<string> Infer(
        MethodDeclarationSyntax method,
        string tableParamName,
        IDocGenLogger logger)
    { ... }
}
```

### Inference approach

Use a `CSharpSyntaxWalker` to visit the method body. Collect string literal arguments from:

1. `<tableParam>.GetString(...)`, `GetInt(...)`, etc. — `InvocationExpressionSyntax` where receiver is the table param name and method name starts with `Get`
2. `<tableParam>["..."]` — `ElementAccessExpressionSyntax` with string literal argument
3. `foreach (var row in <tableParam>.Rows)` then `row["..."]` — track the loop variable name, then collect indexer accesses on it
4. `<tableParam>.CreateSet<T>()` / `CreateInstance<T>()` — extract `T`, search for its class declaration in the same syntax tree, collect public settable property names

Return deduplicated list in order of first appearance.

### Merging in `PipelineRunner`

```
feature-file columns + roslyn columns → union (feature first)
columnsSource:
  - only feature-file columns → "feature"
  - only roslyn columns       → "roslyn"
  - both                      → "both"
  - neither                   → null (omitted)
```

### Tests

```csharp
// Pattern 1: GetString/GetInt
[Fact]
public void InfersColumnsFromGetterCalls()

// Pattern 2: indexer on table param
[Fact]
public void InfersColumnsFromIndexerAccess()

// Pattern 3: foreach row indexer
[Fact]
public void InfersColumnsFromRowIteration()

// Pattern 4: CreateSet<T> with local DTO
[Fact]
public void InfersColumnsFromCreateSetWithLocalDto()

// No table access in body
[Fact]
public void ReturnsEmptyWhenNoTableAccess()

// Deduplication
[Fact]
public void DeduplicatesColumnNames()
```

---

## Output example

```jsonc
{
  "name": "table",
  "type": "Table",
  "example": "",
  "columns": ["Symbol", "Quantity", "Price"],
  "columnsSource": "both"
}
```

---

## Limitations and deferred work

| Limitation | Reason deferred |
|---|---|
| `CreateSet<T>()` with DTO defined in another file | Requires loading project references into Roslyn — expensive, complex |
| Dynamic column names (`row[variableName]`) | Not statically analysable |
| Columns accessed only in called helper methods | Would require inter-procedural analysis |
| Column names from `ObjectContainer` / DI-injected helpers | Out of scope |

These limitations should be documented in the viewer so users understand that `columns` is best-effort for unused steps.

---

## Version bump

`step-library.v1.schema.json` version: `1.1.0` → `1.2.0`

MINOR bump — `columnsSource` is optional and absent when null. Backwards compatible.
