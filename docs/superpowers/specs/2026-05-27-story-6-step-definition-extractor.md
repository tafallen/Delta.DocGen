# Story 6 — StepDefinitionExtractor Design Specification

**Date:** 2026-05-27
**Status:** Approved
**Parent spec:** [Delta.DocGen Design](2026-05-26-delta-docgen-design.md)

---

## 1. Purpose

Implement `StepDefinitionExtractor` — the Roslyn-based component that scans a single `.cs` file and returns every `[Given]`, `[When]`, and `[Then]` step definition as a list of `RawStep` records. This is Stage 3 of the processing pipeline.

---

## 2. Public API

```csharp
// Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs
namespace Delta.DocGen.Scanner.CSharp;

public static class StepDefinitionExtractor
{
    public static IReadOnlyList<RawStep> Extract(
        string relativePath,
        string root,
        IDocGenLogger logger)
}
```

**Parameters:**
- `relativePath` — forward-slash relative path to the `.cs` file (as returned by `Discoverer`)
- `root` — absolute path to the scan root (used to build the full file path)
- `logger` — verbosity-aware logger; use `NullDocGenLogger.Instance` in tests

**Returns:** `IReadOnlyList<RawStep>` — one entry per step attribute found; empty list if none.

---

## 3. Implementation approach

Use LINQ over Roslyn syntax nodes — `root.DescendantNodes().OfType<MethodDeclarationSyntax>()`. No `CSharpSyntaxWalker` subclass; no semantic model or project references. This matches the existing static-class pattern (`Discoverer`) and keeps the implementation self-contained.

---

## 4. Processing flow

1. Build absolute path: `Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))`
2. Read file text: `File.ReadAllText(fullPath)`
3. Parse: `CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot()`
4. Walk: `compilationUnit.DescendantNodes().OfType<MethodDeclarationSyntax>()`
5. For each method, iterate `AttributeLists` → `Attributes`
6. Keep attributes whose unqualified name is `Given`, `When`, or `Then`. Extract the unqualified name as the last `.`-separated segment of `attr.Name.ToString()` (e.g. both `[Given]` and `[TechTalk.SpecFlow.Given]` yield `"Given"`). String comparison only — no namespace resolution required; attribute names are identical across SpecFlow and Reqnroll.
7. For each matching attribute:
   a. Extract pattern string from the first string-literal argument
   b. Build `ParamRecord[]` from method `ParameterList` (see §5)
   c. Compute line number: `attribute.GetLocation().GetLineSpan().StartLinePosition.Line + 1` (1-based)
   d. Extract source: `method.ToString()` (includes all attribute lists, signature, and body)
   e. Emit `RawStep(type, pattern, params, relativePath, line, source)`
8. Log `Verbose` per step found; `Info` per file (step count); `Warn` if an attribute has no string-literal argument (step skipped)

**Multiple step attributes on one method** produce one `RawStep` per attribute. A method with `[Given("x")]` and `[Given("y")]` yields two `RawStep`s.

---

## 5. Parameter mapping

Iterate the method's `ParameterList.Parameters` in declaration order. For each parameter:

| C# type keyword | Condition | Schema type | Example default |
|---|---|---|---|
| `int` | — | `"int"` | `"0"` |
| `decimal` | — | `"decimal"` | `"0.00"` |
| `string` | Has a remaining unclaimed `{…}` placeholder in the pattern | `"string"` | `""` |
| `string` | No remaining unclaimed `{…}` placeholder | `"DocString"` | `""` |

**Placeholder matching:** use `Regex.Matches(pattern, @"\{[^}]+\}")` to extract placeholders in order. Walk params and placeholders in parallel; a `string` param that has no corresponding placeholder is typed `"DocString"`.

Any C# type not in the table above is treated as `"string"` (defensive default).

---

## 6. Source field

`method.ToString()` returns the full method text including:
- All attribute lists (e.g. `[Given("I am logged in as {string}")]`)
- Method signature
- Method body

This is the value stored in `RawStep.Source`.

---

## 7. Error handling

- **File not found:** let `File.ReadAllText` throw naturally — `PipelineRunner` handles top-level errors.
- **Parse errors:** Roslyn parses permissively; extraction continues on any methods it can reach. No special handling required.
- **Attribute with no string-literal argument:** log `Warn` and skip that attribute.
- **Unknown C# parameter type:** treat as `"string"` (defensive default, no warning).

---

## 8. Logging

| Condition | Level |
|---|---|
| Each step found: `[Given] I am logged in as {string} at Auth/AuthSteps.cs:42` | Verbose |
| Per file summary: `Auth/AuthSteps.cs: 3 step(s)` | Info |
| Attribute with no pattern argument (step skipped) | Warn |

---

## 9. Tests

**File:** `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

Test cases (write C# snippet to temp file, call `Extract`, assert on `RawStep` fields):

| # | Scenario | Key assertions |
|---|---|---|
| 1 | Single `[Given]` with one `{string}` param | Type=Given, Pattern correct, Params=[{name, type=string}], Line correct |
| 2 | `[When]` and `[Then]` attributes | Correct types extracted |
| 3 | `{int}` and `{decimal}` params | Schema types = "int" / "decimal", examples = "0" / "0.00" |
| 4 | `string` param with no placeholder (DocString) | Type = "DocString" |
| 5 | Multiple step attributes on one method | Two `RawStep`s returned |
| 6 | Method with no step attributes | Returns empty list |
| 7 | File with multiple step-bearing methods | All steps returned |
| 8 | Reqnroll namespace (`[Reqnroll.Given(...)]`) | Extracted correctly (name match only) |
| 9 | Source field | Contains attribute text, signature, and body |
| 10 | Line number | 1-based, matches attribute position in file |

Use `NullDocGenLogger.Instance`. Create temp files in `IDisposable` test class; delete in `Dispose()`.
