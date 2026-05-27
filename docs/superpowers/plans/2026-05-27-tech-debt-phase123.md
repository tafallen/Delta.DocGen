# Tech Debt Phase 1–3 Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all Phase 1, 2, and 3 tech debt items from `docs/tech-debt.md` (21 items: TD-05, TD-07, TD-09, TD-10, TD-11, TD-12, TD-13, TD-15, TD-16, TD-18, TD-19, TD-20, TD-23, TD-24, TD-26, TD-27, TD-28, TD-32, TD-33, TD-01, TD-02).

**Architecture:** No new top-level components. Changes span: developer guide documentation, model types (new `StepType` enum, `Domain` on `RawStep`, `ParamTypes` constants), config/logging constants, extractor logic improvements, and test coverage additions. Tasks are ordered so each compiles cleanly against the previous.

**Tech Stack:** .NET 8 / C# 12, Roslyn, xUnit 2.9, FluentAssertions 6.12, no mocking frameworks, `TreatWarningsAsErrors` enabled.

---

### Task 1: Documentation and comment fixes

**Covers:** TD-32 (Stage 4 doc contradiction), TD-33 (signing spec incomplete), TD-15 (Source field doc), TD-18 (Discoverer duplicate-glob comment), TD-19 (schema placeholder note), TD-20 (CommandLine beta warning code), TD-31 (layout table stale)

**Files:**
- Modify: `docs/developer-guide.md`
- Modify: `Delta.DocGen/Pipeline/Discoverer.cs`

No code logic changes — documentation and comments only. No test run needed.

- [ ] **Step 1: Fix Stage 4 description — remove mutation claim (TD-32)**

In `docs/developer-guide.md`, find:

```
### Stage 4 — Feature file parsing (Gherkin)

- Parse each `.feature` file with the official Gherkin library.
- Walk every step line in every scenario.
- Match step text against extracted patterns using regex:
  - Cucumber Expression `{string}` → `"[^"]*"`
  - Cucumber Expression `{int}` → `\d+`
  - Cucumber Expression `{decimal}` → `[\d.]+`
  - Old-style regex patterns used as-is.
- Increment `used` counter on the matching `RawStep`.
- Unmatched step lines → warning log.
```

Replace with:

```
### Stage 4 — Feature file parsing (Gherkin)

- Parse each `.feature` file with the official Gherkin library.
- Walk every step line in every scenario.
- Match step text against extracted patterns using regex:
  - Cucumber Expression `{string}` → `"[^"]*"`
  - Cucumber Expression `{int}` → `\d+`
  - Cucumber Expression `{decimal}` → `[\d.]+`
  - Old-style regex patterns used as-is.
- Produces `IReadOnlyDictionary<string, int>` (step pattern → use count). `RawStep` is immutable and is not mutated.
- Unmatched step lines → warning log.

The usage dictionary is passed **directly to Stage 6** (IdGenerator), bypassing Stage 5. Stage 5 (DomainAssigner) only receives `RawStep[]`.
```

- [ ] **Step 2: Update data flow diagram to reflect Stage 4 → Stage 6 bypass (TD-32)**

In `docs/developer-guide.md`, find the data flow diagram block that contains:

```
┌─────────────────┐   ┌───────────────────────────────┐
│  Stage 3:       │   │  Stage 4:                     │
│  C# parsing     │   │  Feature file parsing         │
│  (Roslyn)       │   │  (Gherkin)                    │
│  → RawStep[]    │   │  → Dictionary<pattern, count> │
└────────┬────────┘   └──────────────┬────────────────┘
         │                           │
         └────────────┬──────────────┘
                      │ RawStep[] + usage counts
                      ▼
┌─────────────────────────────────────────────────────┐
│  Stage 5: Domain assignment                         │
│  DomainAssigner → RawStep[] (domain populated)      │
└───────────────────────────┬─────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────┐
│  Stage 6: ID generation                             │
│  IdGenerator → StepRecord[]                         │
└───────────────────────────┬─────────────────────────┘
```

Replace with:

```
┌─────────────────┐   ┌───────────────────────────────┐
│  Stage 3:       │   │  Stage 4:                     │
│  C# parsing     │   │  Feature file parsing         │
│  (Roslyn)       │   │  (Gherkin)                    │
│  → RawStep[]    │   │  → IReadOnlyDictionary        │
└────────┬────────┘   │    <string, int>              │
         │            └──────────────┬────────────────┘
         │                           │ (bypasses Stage 5)
         ▼                           │
┌─────────────────────────────────────────────────────┐
│  Stage 5: Domain assignment                         │
│  DomainAssigner(RawStep[]) → RawStep[]              │
│  (Domain field populated via `with` expression)     │
└───────────────────────────┬─────────────────────────┘
                            │ RawStep[] (domain filled)
                            │   + IReadOnlyDictionary<string, int>
                            ▼
┌─────────────────────────────────────────────────────┐
│  Stage 6: ID generation                             │
│  IdGenerator(RawStep[], counts) → StepRecord[]      │
└───────────────────────────┬─────────────────────────┘
```

- [ ] **Step 3: Add canonical signing spec to Stage 7 (TD-33)**

In `docs/developer-guide.md`, find:

```
### Stage 7 — Serialisation and signing

1. Build `Envelope` record (without `signature`).
2. Serialise to canonical JSON: **all object keys sorted alphabetically at every nesting level, no whitespace**.
3. Compute SHA-256 over UTF-8 bytes of the canonical string.
4. Encode as lowercase hex → set `signature.digest`, `signature.algorithm = "SHA-256"`.
5. Write final file (pretty-printed for readability).
6. Write JSON Schema file alongside output.
```

Replace with:

```
### Stage 7 — Serialisation and signing

1. Build `Envelope` record (without `signature`).
2. Serialise to **canonical JSON** for signing:
   - **Field inclusion:** all `Envelope` fields **except** `signature`. The `$schema` field **is included** (`$` sorts before all letters).
   - **Key order:** alphabetical by JSON property name at every nesting level. `$` sorts before all letters — `$schema` is first.
   - **Format:** no whitespace, no indentation.
3. Compute SHA-256 over UTF-8 bytes of the canonical string.
4. Encode as lowercase hex → set `signature.digest`, `signature.algorithm = "SHA-256"`.
5. Write final file (pretty-printed for readability).
6. Write JSON Schema file alongside output.

> The viewer must replicate this exact canonical form to verify signatures: include `$schema`, alphabetical key order at all nesting levels, no whitespace. Any deviation causes silent verification failure.
```

- [ ] **Step 4: Update layout table — mark StepDefinitionExtractorTests as done (TD-31)**

In `docs/developer-guide.md`, find:

```
│   │   └── StepDefinitionExtractorTests.cs     ⬜ Story 6
```

Replace with:

```
│   │   └── StepDefinitionExtractorTests.cs     ✅ done (11 tests)
```

- [ ] **Step 5: Add comment to Discoverer.cs explaining both glob patterns (TD-18)**

In `Delta.DocGen/Pipeline/Discoverer.cs`, find:

```csharp
        // Include root-level files and all nested files
        matcher.AddInclude("*.cs");
        matcher.AddInclude("*.feature");
        matcher.AddInclude("**/*.cs");
        matcher.AddInclude("**/*.feature");
```

Replace with:

```csharp
        // Both root-level and recursive patterns are required.
        // FileSystemGlobbing interprets "**/*.cs" as "one or more path segments then *.cs",
        // so it does NOT match files directly in the root directory. "*.cs" catches those.
        matcher.AddInclude("*.cs");
        matcher.AddInclude("*.feature");
        matcher.AddInclude("**/*.cs");
        matcher.AddInclude("**/*.feature");
```

- [ ] **Step 6: Expand System.CommandLine beta note with specific warning code (TD-20)**

In `docs/developer-guide.md`, find:

```
> **Note:** `System.CommandLine` 2.0.0-beta4 emits `[Experimental]` attributes on some APIs. If `TreatWarningsAsErrors` causes build failures in the CLI story, suppress the specific warning code rather than disabling `TreatWarningsAsErrors` globally.
```

Replace with:

```
> **Note:** `System.CommandLine` 2.0.0-beta4.22272.1 is a 2022 pre-release. It emits `[Experimental]` attributes on some APIs. If `TreatWarningsAsErrors` causes build failures in Story 13 (CLI), suppress warning `SYSLIB0050` (or whichever code the compiler reports) at the `Delta.DocGen.csproj` level rather than disabling `TreatWarningsAsErrors` globally.
```

- [ ] **Step 7: Add schema placeholder warning to module responsibilities (TD-19)**

In `docs/developer-guide.md`, find the module responsibilities table row:

```
| `Output/Schema/` | Write embedded JSON Schema to output directory |
```

Replace with:

```
| `Output/Schema/` | Write embedded JSON Schema to output directory. **Story 11 note:** `step-library.v1.schema.json` is currently a placeholder — it must be replaced with the real schema before Story 11 is closed, and the embedded resource registration must be verified. |
```

- [ ] **Step 8: Fix Source field doc comment alignment with spec (TD-15)**

In `docs/developer-guide.md`, Stage 3 says "extract verbatim source body" which is imprecise — Source includes the attribute lists, not just the body. Find:

```
- Extract per step: type, pattern string, params (name + inferred type), file, line, verbatim source body.
```

Replace with:

```
- Extract per step: type, pattern string, params (name + inferred type), file, line, full method text (all attribute lists + signature + body) as `Source`.
```

- [ ] **Step 9: Commit**

```powershell
git add docs/developer-guide.md Delta.DocGen/Pipeline/Discoverer.cs
git commit -m "docs: fix Stage 4/7 contradictions, signing spec, glob comment, layout table (TD-15, TD-18, TD-19, TD-20, TD-31, TD-32, TD-33)"
```

Expected: clean commit, no build artifacts touched.

---

### Task 2: Logging constants and case-insensitive verbosity

**Covers:** TD-01 (verbosity magic strings), TD-02 (verbosity case-sensitivity)

**Files:**
- Create: `Delta.DocGen/Logging/LogVerbosity.cs`
- Modify: `Delta.DocGen/Logging/ConsoleLogger.cs`
- Modify: `Delta.DocGen/Config/ConfigLoader.cs`
- Modify: `Delta.DocGen.Tests/Config/ConfigLoaderTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `Delta.DocGen.Tests/Config/ConfigLoaderTests.cs` (inside the class, after `ThrowsOnInvalidLogVerbosity`):

```csharp
[Fact]
public void VerbosityIsCaseInsensitive()
{
    var json = """{ "root": "./tests", "output": "./out.json", "logVerbosity": "Normal" }""";
    var path = Path.Combine(_dir, "docgen.config.json");
    File.WriteAllText(path, json);

    var config = ConfigLoader.Load(path, new ConfigOverrides());

    config.LogVerbosity.Should().Be("normal");
}
```

- [ ] **Step 2: Run the failing test**

```powershell
dotnet test Delta.DocGen.sln --filter "FullyQualifiedName~VerbosityIsCaseInsensitive" -v minimal
```

Expected: FAIL — `ConfigLoader` rejects `"Normal"` with case-sensitive validation.

- [ ] **Step 3: Create LogVerbosity constants**

Create `Delta.DocGen/Logging/LogVerbosity.cs`:

```csharp
namespace Delta.DocGen.Logging;

public static class LogVerbosity
{
    public const string Silent  = "silent";
    public const string Normal  = "normal";
    public const string Verbose = "verbose";
}
```

- [ ] **Step 4: Update ConsoleLogger to use LogVerbosity constants**

In `Delta.DocGen/Logging/ConsoleLogger.cs`, change:

```csharp
        if (verbosity is not ("silent" or "normal" or "verbose"))
            throw new ArgumentException(
                $"Unknown verbosity '{verbosity}'. Expected: silent | normal | verbose.",
                nameof(verbosity));

        _silent  = verbosity == "silent";
        _verbose = verbosity == "verbose";
```

To:

```csharp
        if (verbosity is not (LogVerbosity.Silent or LogVerbosity.Normal or LogVerbosity.Verbose))
            throw new ArgumentException(
                $"Unknown verbosity '{verbosity}'. Expected: silent | normal | verbose.",
                nameof(verbosity));

        _silent  = verbosity == LogVerbosity.Silent;
        _verbose = verbosity == LogVerbosity.Verbose;
```

- [ ] **Step 5: Update ConfigLoader — use LogVerbosity constants and add ToLowerInvariant**

Add `using Delta.DocGen.Logging;` at the top of `Delta.DocGen/Config/ConfigLoader.cs` (after `using System.Text.Json;`).

Then change:

```csharp
    private static readonly HashSet<string> _validVerbosities = ["silent", "normal", "verbose"];
```

To:

```csharp
    private static readonly HashSet<string> _validVerbosities =
        [LogVerbosity.Silent, LogVerbosity.Normal, LogVerbosity.Verbose];
```

And change:

```csharp
        var verbosity = overrides.LogVerbosity ?? file.LogVerbosity ?? "normal";
        if (!_validVerbosities.Contains(verbosity))
```

To:

```csharp
        var verbosity = (overrides.LogVerbosity ?? file.LogVerbosity ?? LogVerbosity.Normal)
                        .ToLowerInvariant();
        if (!_validVerbosities.Contains(verbosity))
```

- [ ] **Step 6: Run all tests**

```powershell
dotnet test Delta.DocGen.sln -v minimal
```

Expected: all tests pass including `VerbosityIsCaseInsensitive`.

- [ ] **Step 7: Commit**

```powershell
git add Delta.DocGen/Logging/LogVerbosity.cs Delta.DocGen/Logging/ConsoleLogger.cs Delta.DocGen/Config/ConfigLoader.cs Delta.DocGen.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat: LogVerbosity constants and case-insensitive validation (TD-01, TD-02)"
```

---

### Task 3: StepType enum, ParamTypes constants, Domain on RawStep, GeneratedAt doc

**Covers:** TD-07 (RawStep needs Domain), TD-09 (GeneratedAt doc comment), TD-10 (StepType enum), TD-11 (ParamTypes constants)

**Files:**
- Create: `Delta.DocGen/Model/StepType.cs`
- Create: `Delta.DocGen/Model/ParamTypes.cs`
- Modify: `Delta.DocGen/Model/RawStep.cs`
- Modify: `Delta.DocGen/Model/StepRecord.cs`
- Modify: `Delta.DocGen/Model/Envelope.cs`
- Modify: `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs`
- Modify: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

- [ ] **Step 1: Create StepType enum**

Create `Delta.DocGen/Model/StepType.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepType
{
    Given,
    When,
    Then,
    /// <summary>SpecFlow/Reqnroll [StepDefinition] — universal attribute matching any Given/When/Then context.</summary>
    StepDefinition,
}
```

- [ ] **Step 2: Create ParamTypes constants**

Create `Delta.DocGen/Model/ParamTypes.cs`:

```csharp
namespace Delta.DocGen.Model;

public static class ParamTypes
{
    public const string String    = "string";
    public const string Int       = "int";
    public const string Decimal   = "decimal";
    public const string DocString = "DocString";
}
```

- [ ] **Step 3: Update RawStep — change Type to StepType, add Domain parameter**

Replace the entire content of `Delta.DocGen/Model/RawStep.cs`:

```csharp
namespace Delta.DocGen.Model;

/// <summary>
/// Intermediate step record produced by Stage 3 (C# scanner).
/// <see cref="Domain"/> starts empty and is populated by Stage 5 (DomainAssigner) via a <c>with</c> expression.
/// </summary>
/// <param name="Type">Step attribute type.</param>
/// <param name="Pattern">Raw Cucumber Expression string from the attribute argument.</param>
/// <param name="Params">Parameters extracted from the C# method signature.</param>
/// <param name="File">Forward-slash relative path to the .cs file containing this step.</param>
/// <param name="Line">1-based line number of the step attribute.</param>
/// <param name="Source">Full method text: all attribute lists + signature + body, as returned by Roslyn's method.ToString().</param>
/// <param name="Domain">Domain assigned by Stage 5; empty string until then.</param>
public sealed record RawStep(
    StepType Type,
    string Pattern,
    IReadOnlyList<ParamRecord> Params,
    string File,
    int Line,
    string Source,
    string Domain = ""
);
```

- [ ] **Step 4: Update StepRecord — change Type from string to StepType**

In `Delta.DocGen/Model/StepRecord.cs`, change:

```csharp
    [property: JsonPropertyName("type")]        string Type,
```

To:

```csharp
    [property: JsonPropertyName("type")]        StepType Type,
```

The `[JsonConverter(typeof(JsonStringEnumConverter))]` attribute on the `StepType` enum handles JSON serialisation automatically — no property-level attribute needed.

- [ ] **Step 5: Add GeneratedAt doc comment to Envelope (TD-09)**

Replace the entire content of `Delta.DocGen/Model/Envelope.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

public sealed record SignatureRecord(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("digest")]    string Digest
);

/// <summary>Top-level output envelope written by Stage 7.</summary>
/// <param name="GeneratedAt">
/// ISO 8601 UTC timestamp in round-trip format, e.g. <c>2026-05-27T09:00:00Z</c>.
/// Must use the <c>Z</c> suffix (not <c>+00:00</c>) so the canonical signing form and
/// the viewer's verification agree on the exact byte sequence.
/// Produce with <c>DateTimeOffset.UtcNow.ToString("O")</c> — the round-trip specifier
/// always emits <c>+00:00</c>, so call <c>.Replace("+00:00", "Z")</c> afterwards.
/// </param>
public sealed record Envelope(
    [property: JsonPropertyName("$schema")]          string Schema,
    [property: JsonPropertyName("version")]          string Version,
    [property: JsonPropertyName("generatedAt")]      string GeneratedAt,
    [property: JsonPropertyName("generatorVersion")] string GeneratorVersion,
    [property: JsonPropertyName("enriched")]         bool Enriched,
    [property: JsonPropertyName("domains")]          IReadOnlyList<DomainRecord> Domains,
    [property: JsonPropertyName("steps")]            IReadOnlyList<StepRecord> Steps,
    [property: JsonPropertyName("signature")]        SignatureRecord? Signature
);
```

- [ ] **Step 6: Update StepDefinitionExtractor — emit StepType enum and use ParamTypes constants**

In `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs`:

**Change A** — update `steps.Add` to parse `name` into `StepType`. Find:

```csharp
                    steps.Add(new RawStep(name, pattern, @params, relativePath, line, source));
```

Replace with:

```csharp
                    var stepType = Enum.Parse<StepType>(name);
                    steps.Add(new RawStep(stepType, pattern, @params, relativePath, line, source));
```

**Change B** — update `ExtractParams` to use `ParamTypes` constants. Find the switch inside `ExtractParams`:

```csharp
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
```

Replace with:

```csharp
                case "int":
                    schemaType = ParamTypes.Int;
                    example = "0";
                    placeholderIndex++;
                    break;
                case "decimal":
                    schemaType = ParamTypes.Decimal;
                    example = "0.00";
                    placeholderIndex++;
                    break;
                default:
                    schemaType = placeholderIndex < placeholders.Count
                        ? ParamTypes.String
                        : ParamTypes.DocString;
                    example = "";
                    placeholderIndex++;
                    break;
```

Also add `using Delta.DocGen.Model;` at the top if it is not already present (check — it likely already has it since `RawStep` and `ParamRecord` are used).

- [ ] **Step 7: Build to surface compilation errors**

```powershell
dotnet build Delta.DocGen.sln
```

Expected: build errors in `StepDefinitionExtractorTests.cs` — the `Type` assertions compare against string literals like `"Given"` which no longer match `StepType`. All other files should compile cleanly.

- [ ] **Step 8: Update StepDefinitionExtractorTests — fix Type assertions to use StepType enum**

In `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`, ensure the file has:

```csharp
using Delta.DocGen.Model;
```

(It already has this — verify, and add if absent.)

Make the following replacements (each is a distinct `.Type.Should().Be(...)` call):

**In `ExtractsGivenStepWithStringParam`:**
```csharp
        step.Type.Should().Be("Given");
```
→
```csharp
        step.Type.Should().Be(StepType.Given);
```

**In `ExtractsWhenAndThenTypes`:**
```csharp
        steps[0].Type.Should().Be("When");
```
→
```csharp
        steps[0].Type.Should().Be(StepType.When);
```

```csharp
        steps[1].Type.Should().Be("Then");
```
→
```csharp
        steps[1].Type.Should().Be(StepType.Then);
```

**In `ExtractsStepsFromMultipleMethods`:**
```csharp
        steps.Select(s => s.Type).Should().BeEquivalentTo(["Given", "When", "Then"]);
```
→
```csharp
        steps.Select(s => s.Type).Should().BeEquivalentTo([StepType.Given, StepType.When, StepType.Then]);
```

**In `ExtractsReqnrollQualifiedAttributeByName`:**
```csharp
        steps[0].Type.Should().Be("Given");
```
→
```csharp
        steps[0].Type.Should().Be(StepType.Given);
```

- [ ] **Step 9: Run all tests**

```powershell
dotnet test Delta.DocGen.sln -v minimal
```

Expected: all 33 existing tests pass.

- [ ] **Step 10: Commit**

```powershell
git add Delta.DocGen/Model/StepType.cs Delta.DocGen/Model/ParamTypes.cs Delta.DocGen/Model/RawStep.cs Delta.DocGen/Model/StepRecord.cs Delta.DocGen/Model/Envelope.cs Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs
git commit -m "feat: StepType enum, ParamTypes constants, Domain on RawStep, GeneratedAt doc (TD-07, TD-09, TD-10, TD-11)"
```

---

### Task 4: Extractor improvements — StepDefinition attr, Table types, placeholder logic, ConsoleLogger thread note

**Covers:** TD-12 (ExtractParams misclassifies Table/DataTable), TD-13 (StepDefinition attribute silently ignored), TD-16 (placeholderIndex logic brittle), TD-05 (ConsoleLogger thread-safety comment)

**Files:**
- Modify: `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs`
- Modify: `Delta.DocGen/Logging/ConsoleLogger.cs`
- Modify: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

- [ ] **Step 1: Write failing test for StepDefinition attribute (TD-13)**

Add to `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

```powershell
dotnet test Delta.DocGen.sln --filter "FullyQualifiedName~ExtractsStepDefinitionAttribute" -v minimal
```

Expected: FAIL — `"StepDefinition"` is not in `StepAttributeNames`.

- [ ] **Step 3: Write test for Table param type (TD-12)**

Add to `StepDefinitionExtractorTests.cs`:

```csharp
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
    steps[0].Params.Should().ContainSingle(p => p.Type == ParamTypes.String && p.Name == "table");
}
```

- [ ] **Step 4: Run to verify current behavior**

```powershell
dotnet test Delta.DocGen.sln --filter "FullyQualifiedName~TableParamMapsToStringWithoutConsumingPlaceholder" -v minimal
```

This may pass already (default arm handles it) but the implementation will be improved in Step 6.

- [ ] **Step 5: Add StepDefinition to StepAttributeNames (TD-13)**

In `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs`, change:

```csharp
    private static readonly HashSet<string> StepAttributeNames =
        new(StringComparer.Ordinal) { "Given", "When", "Then" };
```

To:

```csharp
    private static readonly HashSet<string> StepAttributeNames =
        new(StringComparer.Ordinal) { "Given", "When", "Then", "StepDefinition" };
```

- [ ] **Step 6: Rewrite ExtractParams with explicit arms and logger (TD-12, TD-16)**

The current `ExtractParams` has no `string` explicit arm and conflates unknown types with strings silently. Replace the entire `ExtractParams` method in `StepDefinitionExtractor.cs`.

First, update the call site in `Extract` to pass `logger`:

Find:
```csharp
                    var @params = ExtractParams(method.ParameterList, pattern);
```

Replace with:
```csharp
                    var @params = ExtractParams(method.ParameterList, pattern, logger);
```

Then replace the entire `ExtractParams` method:

```csharp
    private static IReadOnlyList<ParamRecord> ExtractParams(
        ParameterListSyntax paramList, string pattern, IDocGenLogger logger)
    {
        var placeholders = PlaceholderPattern.Matches(pattern);
        var result = new List<ParamRecord>();
        var placeholderIndex = 0;

        foreach (var param in paramList.Parameters)
        {
            var csType = param.Type?.ToString() ?? ParamTypes.String;
            var name = param.Identifier.Text;
            string schemaType;
            string example;

            switch (csType)
            {
                case "int":
                    schemaType = ParamTypes.Int;
                    example = "0";
                    placeholderIndex++;
                    break;
                case "decimal":
                    schemaType = ParamTypes.Decimal;
                    example = "0.00";
                    placeholderIndex++;
                    break;
                case "string":
                    schemaType = placeholderIndex < placeholders.Count
                        ? ParamTypes.String
                        : ParamTypes.DocString;
                    example = "";
                    placeholderIndex++;
                    break;
                case "Table":
                case "DataTable":
                case "ScenarioContext":
                    // Known Reqnroll/SpecFlow injection types passed by the framework, not bound to a placeholder.
                    schemaType = ParamTypes.String;
                    example = "";
                    break;
                default:
                    logger.Warn($"Unrecognised parameter type '{csType}' on '{name}' — defaulting to string.");
                    schemaType = ParamTypes.String;
                    example = "";
                    placeholderIndex++;
                    break;
            }

            result.Add(new ParamRecord(name, schemaType, example));
        }

        return result.AsReadOnly();
    }
```

- [ ] **Step 7: Add thread-safety comment to ConsoleLogger (TD-05)**

In `Delta.DocGen/Logging/ConsoleLogger.cs`, find:

```csharp
/// <summary>
/// Verbosity levels:
///   silent  — Error + Summary only
///   normal  — Info + Warn + Error + Summary  (default)
///   verbose — all levels
/// </summary>
```

Replace with:

```csharp
/// <summary>
/// Verbosity levels:
///   silent  — Error + Summary only
///   normal  — Info + Warn + Error + Summary  (default)
///   verbose — all levels
/// </summary>
/// <remarks>
/// Single-threaded invariant: <see cref="Console.ForegroundColor"/> is set and reset within
/// the same synchronous call. Do not call this logger from concurrent threads; introduce a
/// lock or switch to ANSI escape codes before enabling parallel file scanning.
/// </remarks>
```

- [ ] **Step 8: Run all tests**

```powershell
dotnet test Delta.DocGen.sln -v minimal
```

Expected: all tests pass including `ExtractsStepDefinitionAttribute` and `TableParamMapsToStringWithoutConsumingPlaceholder`.

- [ ] **Step 9: Commit**

```powershell
git add Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs Delta.DocGen/Logging/ConsoleLogger.cs Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs
git commit -m "feat: StepDefinition attr, explicit Table/unknown param arms, placeholder rewrite, thread note (TD-05, TD-12, TD-13, TD-16)"
```

---

### Task 5: Test infrastructure and new test coverage

**Covers:** TD-27 (CapturingDocGenLogger), TD-23 (cross-platform Unix paths), TD-24 (JSON comments test), TD-26 (SpecFlow fully-qualified attr test), TD-28 (file-not-found test)

**Files:**
- Create: `Delta.DocGen.Tests/Logging/CapturingDocGenLogger.cs`
- Modify: `Delta.DocGen.Tests/Config/ConfigLoaderTests.cs`
- Modify: `Delta.DocGen.Tests/Pipeline/DiscovererTests.cs`
- Modify: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

- [ ] **Step 1: Create CapturingDocGenLogger (TD-27)**

Create `Delta.DocGen.Tests/Logging/CapturingDocGenLogger.cs`:

```csharp
using Delta.DocGen.Logging;

namespace Delta.DocGen.Tests.Logging;

/// <summary>Test logger that stores all messages for assertion.</summary>
public sealed class CapturingDocGenLogger : IDocGenLogger
{
    public List<string> InfoMessages    { get; } = [];
    public List<string> VerboseMessages { get; } = [];
    public List<string> WarnMessages    { get; } = [];
    public List<string> ErrorMessages   { get; } = [];
    public List<string> SummaryMessages { get; } = [];

    public void Info(string message)    => InfoMessages.Add(message);
    public void Verbose(string message) => VerboseMessages.Add(message);
    public void Warn(string message)    => WarnMessages.Add(message);
    public void Error(string message)   => ErrorMessages.Add(message);
    public void Error(string message, Exception ex) => ErrorMessages.Add($"{message}: {ex.Message}");
    public void Summary(string message) => SummaryMessages.Add(message);
}
```

- [ ] **Step 2: Write and run test for warn-on-no-string-argument using CapturingDocGenLogger**

Add to `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`:

Add `using Delta.DocGen.Tests.Logging;` at the top if not already present.

Add the test:

```csharp
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
    logger.WarnMessages.Should().ContainSingle(m => m.Contains("no string argument"));
}
```

Run:

```powershell
dotnet test Delta.DocGen.sln --filter "FullyQualifiedName~WarnsAndSkipsAttributeWithNoStringArgument" -v minimal
```

Expected: PASS — `logger.Warn($"[{name}] at {relativePath} has no string argument — skipping.")` already fires.

- [ ] **Step 3: Write and run test for warn-on-unknown-param-type**

Add to `StepDefinitionExtractorTests.cs`:

```csharp
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
```

Run:

```powershell
dotnet test Delta.DocGen.sln --filter "FullyQualifiedName~WarnsForUnrecognisedParamType" -v minimal
```

Expected: PASS.

- [ ] **Step 4: Fix cross-platform Unix path in ConfigLoaderTests (TD-23)**

In `Delta.DocGen.Tests/Config/ConfigLoaderTests.cs`, find:

```csharp
    [Fact]
    public void ThrowsIfConfigFileNotFound()
    {
        var act = () => ConfigLoader.Load("/nonexistent/docgen.config.json", new ConfigOverrides());
        act.Should().Throw<FileNotFoundException>();
    }
```

Replace with:

```csharp
    [Fact]
    public void ThrowsIfConfigFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "docgen.config.json");
        var act = () => ConfigLoader.Load(missing, new ConfigOverrides());
        act.Should().Throw<FileNotFoundException>();
    }
```

- [ ] **Step 5: Fix cross-platform Unix path in DiscovererTests (TD-23)**

In `Delta.DocGen.Tests/Pipeline/DiscovererTests.cs`, find (at line ~71):

```csharp
    [Fact]
    public void ThrowsIfRootDoesNotExist()
    {
        var act = () => Discoverer.Discover("/nonexistent/path", excludes: []);
        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*does not exist*");
    }
```

Replace with:

```csharp
    [Fact]
    public void ThrowsIfRootDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "root");
        var act = () => Discoverer.Discover(missing, excludes: []);
        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*does not exist*");
    }
```

- [ ] **Step 6: Add JSON comments test to ConfigLoaderTests (TD-24)**

Add to `Delta.DocGen.Tests/Config/ConfigLoaderTests.cs`:

```csharp
[Fact]
public void LoadsConfigWithJsonComments()
{
    var json = """
        {
          // project root
          "root": "./tests",
          "output": "./out.json" // generated file
        }
        """;
    var path = Path.Combine(_dir, "docgen.config.json");
    File.WriteAllText(path, json);

    var config = ConfigLoader.Load(path, new ConfigOverrides());

    config.Root.Should().Be(Path.GetFullPath("./tests", _dir));
}
```

- [ ] **Step 7: Add SpecFlow fully-qualified attribute test (TD-26)**

Add to `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`:

```csharp
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
```

- [ ] **Step 8: Add file-not-found test (TD-28)**

Add to `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`:

```csharp
[Fact]
public void ThrowsFileNotFoundForMissingFile()
{
    var missing = "NonExistent/Steps.cs";
    var act = () => StepDefinitionExtractor.Extract(missing, _root, NullDocGenLogger.Instance);
    act.Should().Throw<FileNotFoundException>();
}
```

- [ ] **Step 9: Run all tests**

```powershell
dotnet test Delta.DocGen.sln -v minimal
```

Expected: all tests pass. Count should have grown from 33 to 41 (8 new tests across the 3 files).

- [ ] **Step 10: Commit**

```powershell
git add Delta.DocGen.Tests/Logging/CapturingDocGenLogger.cs Delta.DocGen.Tests/Config/ConfigLoaderTests.cs Delta.DocGen.Tests/Pipeline/DiscovererTests.cs Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs
git commit -m "test: CapturingDocGenLogger, cross-platform paths, JSON comments, SpecFlow attr, file-not-found (TD-23, TD-24, TD-26, TD-27, TD-28)"
```

---

## Coverage check

| ID | Description | Task |
|----|-------------|------|
| TD-32 | Stage 4 doc mutation claim | Task 1 |
| TD-33 | Signing spec incomplete | Task 1 |
| TD-07 | RawStep needs Domain field | Task 3 |
| TD-01 | Verbosity magic strings | Task 2 |
| TD-02 | Verbosity case-sensitivity | Task 2 |
| TD-10 | StepType enum | Task 3 |
| TD-11 | ParamTypes constants | Task 3 |
| TD-18 | Discoverer duplicate glob comment | Task 1 |
| TD-19 | Schema placeholder note | Task 1 |
| TD-20 | CommandLine beta warning code | Task 1 |
| TD-23 | Cross-platform Unix test paths | Task 5 |
| TD-24 | JSON comments test | Task 5 |
| TD-26 | SpecFlow qualified attr test | Task 5 |
| TD-28 | File-not-found test | Task 5 |
| TD-12 | ExtractParams Table/unknown arms | Task 4 |
| TD-13 | StepDefinition attr | Task 4 |
| TD-16 | Placeholder logic rewrite | Task 4 |
| TD-09 | GeneratedAt doc comment | Task 3 |
| TD-15 | Source field doc fix | Task 1 |
| TD-27 | CapturingDocGenLogger | Task 5 |
| TD-05 | ConsoleLogger thread-safety comment | Task 4 |
