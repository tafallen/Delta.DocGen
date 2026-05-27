# Story 11: JSON Schema + SchemaWriter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder JSON Schema at `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json` with a real schema describing the envelope, and implement `SchemaWriter.Write(outputDir, logger)` to extract that embedded resource and write it to `<outputDir>/schema/v1/step-library.schema.json`.

**Architecture:** The schema is registered as an embedded resource in `Delta.DocGen.csproj` (already done). `SchemaWriter` reads the resource stream from the assembly and writes its bytes verbatim to disk. Tests cover (1) the schema validates the canonical envelope produced by Story 10, and (2) `SchemaWriter.Write` creates the expected output file with byte-identical content.

**Tech Stack:** .NET 8, `System.Reflection` for embedded resource access, `Json.Schema` (`JsonSchema.Net` NuGet) for validation tests, xUnit 2.9.3, FluentAssertions 6.12.0.

**Prerequisite:** Story 10 (Envelope + Signer) complete — the schema must validate the real on-disk envelope shape.

---

## File structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json` | Replace | Real JSON Schema for the envelope |
| `Delta.DocGen/Output/Schema/SchemaWriter.cs` | Create | Extracts embedded schema, writes to `<outputDir>/schema/v1/step-library.schema.json` |
| `Delta.DocGen.Tests/Output/SchemaWriterTests.cs` | Create | Tests for `SchemaWriter` |
| `Delta.DocGen.Tests/Output/SchemaValidationTests.cs` | Create | Validates real-shape envelope against the schema |
| `Delta.DocGen.Tests/Delta.DocGen.Tests.csproj` | Modify | Add `JsonSchema.Net` package reference for validation tests |
| `docs/developer-guide.md` | Modify | Mark Story 11 ✅; update test count; update "What's next" to Story 12 |

---

## Key existing types (do NOT modify)

```csharp
// Envelope — Stage 7 output shape
public sealed record Envelope(
    [property: JsonPropertyName("$schema")]           string Schema,
    [property: JsonPropertyName("version")]           string Version,
    [property: JsonPropertyName("generatedAt")]       string GeneratedAt,
    [property: JsonPropertyName("generatorVersion")]  string GeneratorVersion,
    [property: JsonPropertyName("enriched")]          bool Enriched,
    [property: JsonPropertyName("domains")]           IReadOnlyList<DomainRecord> Domains,
    [property: JsonPropertyName("steps")]             IReadOnlyList<StepRecord> Steps,
    [property: JsonPropertyName("signature")]         SignatureRecord? Signature);
```

The embedded resource is registered in `Delta.DocGen.csproj`:

```xml
<EmbeddedResource Include="Output\Schema\Resources\step-library.v1.schema.json"/>
```

The resource's logical name within the assembly is therefore `Delta.DocGen.Output.Schema.Resources.step-library.v1.schema.json` (dots replace path separators).

---

## Task 1: Write the real JSON Schema

**Files:**
- Replace: `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json`

- [ ] **Step 1: Replace the placeholder with the real schema**

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://delta.docgen/schema/v1/step-library.schema.json",
  "title": "Delta.DocGen Step Library",
  "type": "object",
  "required": ["$schema", "version", "generatedAt", "generatorVersion", "enriched", "domains", "steps", "signature"],
  "additionalProperties": false,
  "properties": {
    "$schema":          { "type": "string" },
    "version":          { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+$" },
    "generatedAt":      { "type": "string", "format": "date-time" },
    "generatorVersion": { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+$" },
    "enriched":         { "type": "boolean" },
    "domains": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id", "label"],
        "additionalProperties": false,
        "properties": {
          "id":    { "type": "string", "minLength": 1 },
          "label": { "type": "string" }
        }
      }
    },
    "steps": {
      "type": "array",
      "items": { "$ref": "#/$defs/step" }
    },
    "signature": {
      "type": "object",
      "required": ["algorithm", "digest"],
      "additionalProperties": false,
      "properties": {
        "algorithm": { "type": "string", "enum": ["SHA-256"] },
        "digest":    { "type": "string", "pattern": "^[0-9a-f]{64}$" }
      }
    }
  },
  "$defs": {
    "step": {
      "type": "object",
      "required": ["id", "type", "pattern", "params", "file", "line", "domain", "tags", "used", "description", "source", "suggestsNext"],
      "additionalProperties": false,
      "properties": {
        "id":          { "type": "string", "pattern": "^[a-z0-9-]+-[0-9a-f]{8}$" },
        "type":        { "type": "string", "enum": ["Given", "When", "Then", "StepDefinition"] },
        "pattern":     { "type": "string", "minLength": 1 },
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
        },
        "file":         { "type": "string", "minLength": 1 },
        "line":         { "type": "integer", "minimum": 1 },
        "domain":       { "type": "string", "minLength": 1 },
        "tags":         { "type": "array", "items": { "type": "string" } },
        "used":         { "type": "integer", "minimum": 0 },
        "description":  { "type": "string" },
        "source":       { "type": "string" },
        "suggestsNext": { "type": "array", "items": { "type": "string" } }
      }
    }
  }
}
```

- [ ] **Step 2: Verify the resource is still registered**

Open `Delta.DocGen/Delta.DocGen.csproj` and confirm the line:
```xml
<EmbeddedResource Include="Output\Schema\Resources\step-library.v1.schema.json"/>
```
is still present. (No changes required — already there from earlier scaffolding.)

- [ ] **Step 3: Build and confirm no warnings**

```
dotnet build --no-incremental
```

Expected: build succeeds, 0 warnings (TreatWarningsAsErrors is on, so any warning will fail the build).

- [ ] **Step 4: Commit**

```
git add Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json
git commit -m "feat: replace placeholder JSON Schema with real envelope schema (Story 11, task 1)"
```

---

## Task 2: Add JsonSchema.Net package and write validation test

**Files:**
- Modify: `Delta.DocGen.Tests/Delta.DocGen.Tests.csproj`
- Create: `Delta.DocGen.Tests/Output/SchemaValidationTests.cs`

- [ ] **Step 1: Add the JsonSchema.Net package reference**

Add to `Delta.DocGen.Tests/Delta.DocGen.Tests.csproj` inside the existing `<ItemGroup>` containing `<PackageReference>` entries:

```xml
<PackageReference Include="JsonSchema.Net" Version="7.2.3"/>
```

Then run:
```
dotnet restore
```

Expected: package restores cleanly. Lock file updates.

- [ ] **Step 2: Write the validation test**

Create `Delta.DocGen.Tests/Output/SchemaValidationTests.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Delta.DocGen.Model;
using Delta.DocGen.Output.Serialiser;
using FluentAssertions;
using Json.Schema;

namespace Delta.DocGen.Tests.Output;

public sealed class SchemaValidationTests
{
    private static JsonSchema LoadSchema()
    {
        var assembly = typeof(Delta.DocGen.Output.Schema.SchemaWriter).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Delta.DocGen.Output.Schema.Resources.step-library.v1.schema.json")!;
        return JsonSchema.FromStream(stream).AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void RealEnvelopeValidatesAgainstSchema()
    {
        var envelope = new Envelope(
            Schema:           "./schema/v1/step-library.schema.json",
            Version:          "1.0.0",
            GeneratedAt:      "2026-05-27T09:00:00Z",
            GeneratorVersion: "1.0.0",
            Enriched:         false,
            Domains:          [new DomainRecord("Auth", "Auth & Identity")],
            Steps:            [new StepRecord(
                "auth-a1b2c3d4", StepType.Given, "I am logged in", [],
                "Auth/AuthSteps.cs", 1, "Auth", [], 0, "", "", [])],
            Signature:        new SignatureRecord("SHA-256", new string('a', 64)));

        var json = JsonSerializer.Serialize(envelope);
        var node = JsonNode.Parse(json);
        var schema = LoadSchema();

        var result = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

        result.IsValid.Should().BeTrue(
            "schema validation failed: " + string.Join("; ",
                result.Details.Where(d => !d.IsValid).Select(d => $"{d.InstanceLocation}: {string.Join(",", d.Errors?.Values ?? [])}")));
    }

    [Fact]
    public void EnvelopeWithMissingRequiredFieldFailsValidation()
    {
        // Build a deliberately broken envelope (missing 'steps').
        var brokenJson = JsonNode.Parse("""
            {
              "$schema": "./schema/v1/step-library.schema.json",
              "version": "1.0.0",
              "generatedAt": "2026-05-27T09:00:00Z",
              "generatorVersion": "1.0.0",
              "enriched": false,
              "domains": [],
              "signature": { "algorithm": "SHA-256", "digest": "0000000000000000000000000000000000000000000000000000000000000000" }
            }
            """);

        var schema = LoadSchema();
        var result = schema.Evaluate(brokenJson);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void StepWithInvalidIdFormatFailsValidation()
    {
        var brokenJson = JsonNode.Parse("""
            {
              "$schema": "./schema/v1/step-library.schema.json",
              "version": "1.0.0",
              "generatedAt": "2026-05-27T09:00:00Z",
              "generatorVersion": "1.0.0",
              "enriched": false,
              "domains": [{ "id": "Auth", "label": "Auth" }],
              "steps": [{
                "id": "BAD-ID-FORMAT",
                "type": "Given",
                "pattern": "I am logged in",
                "params": [],
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

        result.IsValid.Should().BeFalse();
    }
}
```

Note: `LoadSchema` references `Delta.DocGen.Output.Schema.SchemaWriter` which doesn't exist yet (Task 3 creates it). For now, replace that line with `var assembly = typeof(Delta.DocGen.Model.Envelope).Assembly;` so the test compiles — we'll update it in Task 3.

- [ ] **Step 3: Run the new tests**

```
dotnet test --filter "SchemaValidation" -q
```

Expected: 3 passing.

- [ ] **Step 4: Run full suite**

```
dotnet test -q
```

Expected: 97 passing (94 baseline + 3 new).

- [ ] **Step 5: Commit**

```
git add Delta.DocGen.Tests/Delta.DocGen.Tests.csproj Delta.DocGen.Tests/Output/SchemaValidationTests.cs Delta.DocGen.Tests/packages.lock.json
git commit -m "test: validate envelope shape against JSON Schema (Story 11, task 2)"
```

---

## Task 3: Create SchemaWriter

**Files:**
- Create: `Delta.DocGen/Output/Schema/SchemaWriter.cs`
- Modify: `Delta.DocGen.Tests/Output/SchemaValidationTests.cs` (point assembly reference back at `SchemaWriter`)

- [ ] **Step 1: Create `SchemaWriter.cs`**

```csharp
using System.Reflection;
using Delta.DocGen.Logging;

namespace Delta.DocGen.Output.Schema;

public static class SchemaWriter
{
    private const string ResourceName = "Delta.DocGen.Output.Schema.Resources.step-library.v1.schema.json";
    private const string RelativeOutputPath = "schema/v1/step-library.schema.json";

    public static string Write(string outputDir, IDocGenLogger logger)
    {
        var assembly = typeof(SchemaWriter).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. " +
                "Verify the <EmbeddedResource> entry in Delta.DocGen.csproj.");

        var destination = Path.Combine(outputDir, RelativeOutputPath.Replace('/', Path.DirectorySeparatorChar));
        var destinationDir = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"Cannot determine directory for '{destination}'.");
        Directory.CreateDirectory(destinationDir);

        using var output = File.Create(destination);
        stream.CopyTo(output);

        logger.Info($"Schema written to {destination}.");
        return destination;
    }
}
```

- [ ] **Step 2: Restore the `typeof(SchemaWriter)` reference in `SchemaValidationTests`**

In `SchemaValidationTests.cs`, change the `LoadSchema` line:
```csharp
var assembly = typeof(Delta.DocGen.Model.Envelope).Assembly;
```
back to:
```csharp
var assembly = typeof(Delta.DocGen.Output.Schema.SchemaWriter).Assembly;
```

- [ ] **Step 3: Build to confirm it compiles**

```
dotnet build --no-incremental
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen/Output/Schema/SchemaWriter.cs Delta.DocGen.Tests/Output/SchemaValidationTests.cs
git commit -m "feat: SchemaWriter extracts embedded schema to output dir (Story 11, task 3)"
```

---

## Task 4: Tests for SchemaWriter

**Files:**
- Create: `Delta.DocGen.Tests/Output/SchemaWriterTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using System.Reflection;
using Delta.DocGen.Logging;
using Delta.DocGen.Output.Schema;
using Delta.DocGen.Tests.Logging;
using FluentAssertions;

namespace Delta.DocGen.Tests.Output;

public sealed class SchemaWriterTests : IDisposable
{
    private readonly string _outputDir;

    public SchemaWriterTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    [Fact]
    public void WriteCreatesSchemaFileAtExpectedRelativePath()
    {
        var path = SchemaWriter.Write(_outputDir, NullDocGenLogger.Instance);

        var expected = Path.Combine(_outputDir, "schema", "v1", "step-library.schema.json");
        path.Should().Be(expected);
        File.Exists(expected).Should().BeTrue();
    }

    [Fact]
    public void WriteOutputMatchesEmbeddedResourceByteForByte()
    {
        var path = SchemaWriter.Write(_outputDir, NullDocGenLogger.Instance);
        var onDisk = File.ReadAllBytes(path);

        var assembly = typeof(SchemaWriter).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Delta.DocGen.Output.Schema.Resources.step-library.v1.schema.json")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        onDisk.Should().Equal(ms.ToArray());
    }

    [Fact]
    public void WriteCreatesNestedDirectoriesIfAbsent()
    {
        var nestedDir = Path.Combine(_outputDir, "deep", "nested", "path");

        var path = SchemaWriter.Write(nestedDir, NullDocGenLogger.Instance);

        File.Exists(path).Should().BeTrue();
        Directory.Exists(Path.Combine(nestedDir, "schema", "v1")).Should().BeTrue();
    }

    [Fact]
    public void WriteOverwritesExistingSchemaFile()
    {
        var first = SchemaWriter.Write(_outputDir, NullDocGenLogger.Instance);
        File.WriteAllText(first, "stale content");

        var second = SchemaWriter.Write(_outputDir, NullDocGenLogger.Instance);

        File.ReadAllText(second).Should().NotBe("stale content");
        File.ReadAllText(second).Should().Contain("\"$schema\"");
    }

    [Fact]
    public void WriteLogsInfoMessageWithDestinationPath()
    {
        var logger = new CapturingDocGenLogger();

        var path = SchemaWriter.Write(_outputDir, logger);

        logger.InfoMessages.Should().ContainSingle(m => m.Contains(path));
    }
}
```

- [ ] **Step 2: Run new tests**

```
dotnet test --filter "SchemaWriterTests" -q
```

Expected: 5 passing.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 102 passing (97 + 5).

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Output/SchemaWriterTests.cs
git commit -m "test: SchemaWriter file output, overwrite, nested dirs, logging (Story 11, task 4)"
```

---

## Task 5: Update developer guide

**Files:**
- Modify: `docs/developer-guide.md`

- [ ] **Step 1: Mark Story 11 complete**

Change `| 11 | JSON Schema | … | ⬜ |` → `✅`.

- [ ] **Step 2: Update overview status range**

```
| 1–11 | ✅ Complete, merged to master, pushed to GitHub |
| 12–15 | ⬜ Not started |
```

- [ ] **Step 3: Update test count**

Confirm via `dotnet test -q`; update the line to:
```
**Test count:** 102 passing (Stories 1–11 + TD-C01..C10 debt fixes)
```

- [ ] **Step 4: Update "What's next" section**

Replace the Story 11 What's-next section with:

```markdown
### What's next — Story 12: Pipeline runner

The next story implements `PipelineRunner`, orchestrating stages 1–8 end-to-end. Key points:

- Input: a fully-loaded `DocGenConfig` and an `IDocGenLogger`
- Output: `PipelineResult` record summarising step count, domain count, output path, digest, elapsed time, unmatched-step count
- Sequence: Discoverer → StepDefinitionExtractor (over all `.cs` files) → UsageCounter (over all `.feature` files) → DomainAssigner → IdGenerator + DomainBuilder → CanonicalJson + Signer → CanonicalJson.Write + SchemaWriter.Write → summary log
- Honours `--dry-run`: runs all stages but skips the two final `Write` calls
- Catches and logs fatal pipeline exceptions (e.g. ID collisions); returns a failed `PipelineResult`
```

- [ ] **Step 5: Confirm tests still 102**

```
dotnet test -q
```

- [ ] **Step 6: Commit**

```
git add docs/developer-guide.md
git commit -m "docs: mark Story 11 complete, update test count and what's-next (Story 11, task 5)"
```
