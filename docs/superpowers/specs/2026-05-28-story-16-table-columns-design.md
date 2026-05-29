# Story 16 — Table Column Metadata Design

**Date:** 2026-05-28
**Status:** Design approved — awaiting implementation plan

## Goal

For every step parameter typed as `Table` / `DataTable`, capture the columns the step uses — column name and inferred type — and emit them as a new optional `columns` field on `ParamRecord`. Sources: (1) feature-file observed usage and (2) C# `table.CreateSet<T>()` / `CreateInstance<T>()` calls when present, with the C# declaration winning when both agree on a column.

## Schema change

`ParamRecord` gains an optional field; a new `ColumnRecord` type is introduced.

```csharp
public sealed record ParamRecord(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("example")] string Example,
    [property: JsonPropertyName("columns")] IReadOnlyList<ColumnRecord>? Columns = null);

public sealed record ColumnRecord(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type);
```

JSON Schema is updated:
- `params[].columns` is **optional**, an array of `{ name, type }` objects.
- The `type` value on a Table parameter changes from `"string"` to `"table"` (a new `ParamTypes.Table` constant).
- `JsonSerializerOptions.DefaultIgnoreCondition = WhenWritingNull` (already set) omits `columns` when not applicable.

This is **backward-compatible** — readers ignoring unknown fields, or expecting `columns` to be absent, continue to work.

## Type vocabulary

| Source | Vocabulary |
|--------|-----------|
| C# declaration (`CreateSet<T>` / `CreateInstance<T>`) | The C# property type verbatim — e.g. `int`, `decimal`, `Guid`, `DateTime`, `DateTimeOffset`, `string`, `MyEnumType`. |
| Feature-file observed values | `int`, `decimal`, `bool`, `date`, `string` (the narrowest type that fits all observed non-empty values; mixed → `string`). |

Less-than-2 non-empty observations for a column → `string`.

## Pipeline impact

### Stage 3 — `StepDefinitionExtractor`

When a `Table` / `DataTable` parameter is detected:

1. Walk the method body for `<identifier>.CreateSet<T>()` or `<identifier>.CreateInstance<T>()` calls where `<identifier>` matches the Table parameter name (or any parameter — first match wins).
2. If found *and* the referenced type `T` is declared in the same `.cs` file:
   - Enumerate `T`'s public instance properties.
   - Produce a `ColumnRecord` per property (column name = property name; column type = the C# property type verbatim).
3. Attach as the **C#-declared baseline** on the Table `ParamRecord`.

Cross-file resolution of `T` is **out of scope** for this story — it requires a Roslyn `Compilation` shared across all extracted files, which is a meaningful architecture change. Cross-file references get the same fallback as no-annotation cases (observed-only).

### Stage 4b — new `TableColumnAggregator`

Separate pass over feature files alongside the existing `UsageCounter`. For each step usage with a `DataTable` block:

1. Match the step text against extracted step patterns (reusing the matching logic from `UsageCounter`).
2. Capture the header row and each cell.
3. Group observations by `(step pattern, parameter index)`.
4. Produce `IReadOnlyDictionary<string, IReadOnlyList<ColumnRecord>>` keyed by step pattern.

Type inference rules:
- For each column, collect all non-empty observed values across every scenario that used the step.
- The column type is the **narrowest** type that all observed values parse to: `int` → `decimal` → `bool` → `date` → `string`.
- Empty cells are ignored for inference.
- Fewer than 2 non-empty observations → fall back to `string` (insufficient signal).

### Stage 6 — `IdGenerator`

`IdGenerator.AssignIds` accepts the new aggregator output and merges declared + observed columns into the final `ParamRecord.Columns` per Table parameter:

| C# declared | Feature observed | Result |
|-------------|------------------|--------|
| Present | Any | Emit C# declared columns. If feature files reference extra column names not in `T`, append them as observed-string with a Verbose log. |
| Absent | Present | Emit observed columns from feature files. |
| Absent | Absent (Table param never used in a feature) | Emit `columns: []`. |

The merge order preserves declared property order; observed-but-not-declared columns are appended.

## Module structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Delta.DocGen/Model/ColumnRecord.cs` | Create | New record. |
| `Delta.DocGen/Model/ParamRecord.cs` | Modify | Add `Columns` optional field. |
| `Delta.DocGen/Model/ParamTypes.cs` | Modify | Add `Table` constant. |
| `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs` | Modify | Detect `CreateSet<T>` / `CreateInstance<T>`; resolve same-file `T`; attach declared `Columns`. |
| `Delta.DocGen/Scanner/Gherkin/TableColumnAggregator.cs` | Create | Observed headers + value-typed columns per step pattern. |
| `Delta.DocGen/Pipeline/IdGenerator.cs` | Modify | Merge declared + observed into final `Columns`. |
| `Delta.DocGen/Pipeline/PipelineRunner.cs` | Modify | Invoke `TableColumnAggregator`; thread results into `AssignIds`. |
| `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json` | Modify | Add optional `columns` property. |
| Tests for each of the above | Create or modify | TDD per stage. |

## Edge cases

- Step has a Table param but no feature file uses it → emit `columns: []`.
- Step has a Table param, no C# annotation, multiple feature files use it with different headers → **union** of all observed headers; column type narrowed across all observations.
- Feature file row has fewer cells than headers → ignore that row for inference.
- Step has multiple `Table` parameters → each indexed separately; aggregator keys by `(pattern, paramIndex)`.
- `CreateSet<T>` / `CreateInstance<T>` where `T` is cross-file → treat as no-annotation (observed-only); log Verbose noting the cross-file ref.
- `CreateSet<T>` referenced via a fully-qualified namespace (e.g. `myTable.CreateSet<Demo.Contract>()`) → match by simple type name.

## Testing strategy

Each component is independently testable:

- **`StepDefinitionExtractor`** — new tests for `CreateSet<T>` resolution (same-file `T`), `CreateInstance<T>`, cross-file `T` (falls back to no-annotation), and no-annotation Table param.
- **`TableColumnAggregator`** — new tests for header capture, value-type inference for each vocabulary entry (`int`, `decimal`, `bool`, `date`, `string`), mixed types → `string`, multi-scenario union, empty cells ignored.
- **`IdGenerator`** — merge precedence tests (declared wins; observed extra columns appended with Verbose log; both empty).
- **Schema validation** — `columns` optional; valid when present with required `name` + `type`.
- **End-to-end** — a fixture step with a Table param + matching feature file; assert final JSON includes the expected `columns` list.

## Scope check

This is **one story**. Roughly 6–8 implementation tasks (one per modified file + an end-to-end test). No dependencies on Stories 1–15 beyond the existing schema and pipeline shape. No breaking change to existing output (the new field is optional).

## Out of scope (deferred to a future story)

- Cross-file resolution of `T` in `CreateSet<T>` (requires Roslyn `Compilation` across the extracted file set).
- Recognising custom Reqnroll/SpecFlow table-transform attributes (e.g. `[StepArgumentTransformation]` on a method that returns the typed model).
- Inferring column constraints (nullable, length, enum members).
- Auto-detecting `Dictionary<string, string>` and other Table-shaped types beyond `CreateSet<T>` / `CreateInstance<T>`.
