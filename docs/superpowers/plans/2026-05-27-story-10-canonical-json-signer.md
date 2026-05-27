# Story 10: CanonicalJson + Signer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `CanonicalJson` (key-sorted compact serialisation + pretty-print file write) and `Signer` (SHA-256 digest of the canonical form, inserted as the `signature` field), producing a tamper-evident JSON output file.

**Architecture:** `CanonicalJson.Serialise` converts any object to a `JsonNode` tree via `JsonSerializer.SerializeToNode`, recursively re-builds it with all `JsonObject` keys sorted by ordinal, then calls `ToJsonString()` for compact (no-whitespace) output. `Signer.Sign` removes the `Signature` field (sets to null), calls `CanonicalJson.Serialise` (null fields omitted by `WhenWritingNull`), SHA-256s the UTF-8 bytes, hex-encodes, and returns the envelope with `Signature` populated. `CanonicalJson.Write` serialises the final signed envelope as pretty-printed JSON and writes to disk.

**Tech Stack:** .NET 8, `System.Text.Json` (in-box), `System.Text.Json.Nodes` (in-box), `System.Security.Cryptography.SHA256` (in-box), xUnit 2.9.3, FluentAssertions 6.12.0

**Prerequisite:** Story 9 (IdGenerator) complete — `StepRecord[]`, `DomainRecord[]`, and `Envelope` model all exist.

---

## File structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Delta.DocGen/Output/Serialiser/CanonicalJson.cs` | Create | Key-sorted compact serialisation; pretty-print file write |
| `Delta.DocGen/Output/Serialiser/Signer.cs` | Create | SHA-256 signing of envelope |
| `Delta.DocGen.Tests/Output/CanonicalJsonTests.cs` | Create | Tests for `CanonicalJson` |
| `Delta.DocGen.Tests/Output/SignerTests.cs` | Create | Tests for `Signer` |
| `docs/developer-guide.md` | Modify | Mark Story 10 ✅; update test count |

---

## Key types (already exist — do not modify)

```csharp
// Delta.DocGen/Model/Envelope.cs
public sealed record Envelope(
    [property: JsonPropertyName("$schema")]          string Schema,
    [property: JsonPropertyName("version")]          string Version,
    [property: JsonPropertyName("generatedAt")]      string GeneratedAt,
    [property: JsonPropertyName("generatorVersion")] string GeneratorVersion,
    [property: JsonPropertyName("enriched")]         bool Enriched,
    [property: JsonPropertyName("domains")]          IReadOnlyList<DomainRecord> Domains,
    [property: JsonPropertyName("steps")]            IReadOnlyList<StepRecord> Steps,
    [property: JsonPropertyName("signature")]        SignatureRecord? Signature);

public sealed record SignatureRecord(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("digest")]    string Digest);
```

All output model records already carry `[JsonPropertyName]` attributes. `StepType` has `[JsonConverter(typeof(JsonStringEnumConverter))]`.

---

## Canonical serialisation rules (from design spec §8)

1. All object keys sorted **alphabetically** (ordinal) at **every** nesting level.
2. No insignificant whitespace (compact output).
3. `null` fields **omitted** (so `"signature": null` never appears in the canonical string).
4. Arrays preserve their element order.

The `"$schema"` key sorts before all letter-keyed fields because `$` (ASCII 36) < `a` (ASCII 97).

---

## Task 1: CanonicalJson.Serialise — top-level key sorting

**Files:**
- Create: `Delta.DocGen/Output/Serialiser/CanonicalJson.cs`
- Create: `Delta.DocGen.Tests/Output/CanonicalJsonTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Delta.DocGen.Tests/Output/CanonicalJsonTests.cs
using Delta.DocGen.Output.Serialiser;
using FluentAssertions;

namespace Delta.DocGen.Tests.Output;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void KeysSortedAlphabeticallyAtTopLevel()
    {
        var obj = new { zebra = 1, apple = 2, mango = 3 };

        var json = CanonicalJson.Serialise(obj);

        // Keys must appear in alphabetical order
        var applePos = json.IndexOf("apple", StringComparison.Ordinal);
        var mangoPos = json.IndexOf("mango", StringComparison.Ordinal);
        var zebraPos = json.IndexOf("zebra", StringComparison.Ordinal);
        applePos.Should().BeLessThan(mangoPos);
        mangoPos.Should().BeLessThan(zebraPos);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```
dotnet test --filter "KeysSortedAlphabetically" -q
```

Expected: FAIL — `CanonicalJson` not found.

- [ ] **Step 3: Create the full implementation**

```csharp
// Delta.DocGen/Output/Serialiser/CanonicalJson.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Delta.DocGen.Model;

namespace Delta.DocGen.Output.Serialiser;

public static class CanonicalJson
{
    private static readonly JsonSerializerOptions _canonicalOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions _prettyOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialise(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, _canonicalOptions);
        var sorted = SortKeys(node)!;
        return sorted.ToJsonString();
    }

    public static void Write(Envelope envelope, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (dir is not null && dir.Length > 0)
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(envelope, _prettyOptions);
        File.WriteAllText(outputPath, json, Encoding.UTF8);
    }

    private static JsonNode? SortKeys(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var sorted = new JsonObject();
            foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                sorted[kvp.Key] = SortKeys(kvp.Value);
            return sorted;
        }
        if (node is JsonArray arr)
        {
            var result = new JsonArray();
            foreach (var item in arr)
                result.Add(SortKeys(item));
            return result;
        }
        return node?.DeepClone();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

```
dotnet test --filter "KeysSortedAlphabetically" -q
```

Expected: PASS.

- [ ] **Step 5: Run full suite**

```
dotnet test -q
```

Expected: 78 passing, 0 failing.

- [ ] **Step 6: Commit**

```
git add Delta.DocGen/Output/Serialiser/CanonicalJson.cs Delta.DocGen.Tests/Output/CanonicalJsonTests.cs
git commit -m "feat: scaffold CanonicalJson with key-sorting test (Story 10, task 1)"
```

---

## Task 2: Nested key sorting, array order, and no whitespace

**Files:**
- Modify: `Delta.DocGen.Tests/Output/CanonicalJsonTests.cs`

- [ ] **Step 1: Add three tests**

```csharp
[Fact]
public void NestedObjectKeysAreSorted()
{
    var obj = new { outer = new { z = 1, a = 2 } };

    var json = CanonicalJson.Serialise(obj);

    // Inside "outer", "a" must appear before "z"
    var outerPos = json.IndexOf("outer", StringComparison.Ordinal);
    var aPos     = json.IndexOf("\"a\"",  StringComparison.Ordinal);
    var zPos     = json.IndexOf("\"z\"",  StringComparison.Ordinal);
    aPos.Should().BeGreaterThan(outerPos);
    aPos.Should().BeLessThan(zPos);
}

[Fact]
public void ArrayElementOrderIsPreserved()
{
    var obj = new { items = new[] { "charlie", "alpha", "bravo" } };

    var json = CanonicalJson.Serialise(obj);

    var charliePos = json.IndexOf("charlie", StringComparison.Ordinal);
    var alphaPos   = json.IndexOf("alpha",   StringComparison.Ordinal);
    var bravoPos   = json.IndexOf("bravo",   StringComparison.Ordinal);
    charliePos.Should().BeLessThan(alphaPos);
    alphaPos.Should().BeLessThan(bravoPos);
}

[Fact]
public void OutputContainsNoWhitespace()
{
    var obj = new { key = "value", number = 42 };

    var json = CanonicalJson.Serialise(obj);

    json.Should().NotContain(" ");
    json.Should().NotContain("\n");
    json.Should().NotContain("\r");
}
```

- [ ] **Step 2: Run new tests**

```
dotnet test --filter "NestedObject|ArrayElement|OutputContains" -q
```

Expected: all PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 81 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Output/CanonicalJsonTests.cs
git commit -m "test: nested sorting, array order, no whitespace for CanonicalJson (Story 10, task 2)"
```

---

## Task 3: CanonicalJson.Write — file output

**Files:**
- Modify: `Delta.DocGen.Tests/Output/CanonicalJsonTests.cs`

- [ ] **Step 1: Add the test**

The test creates a temp directory, writes an envelope to it, then asserts the file exists and is valid JSON.

```csharp
public sealed class CanonicalJsonTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public CanonicalJsonTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);
    // ... existing tests above ...

    [Fact]
    public void WriteCreatesFileWithPrettyPrintedJson()
    {
        var envelope = MakeEnvelope();
        var outputPath = Path.Combine(_dir, "output", "step-library.json");

        CanonicalJson.Write(envelope, outputPath);

        File.Exists(outputPath).Should().BeTrue();
        var content = File.ReadAllText(outputPath);
        content.Should().Contain("\n");         // pretty-printed
        content.Should().Contain("1.0.0");      // version present
        content.Should().Contain("\"steps\"");  // steps key present
    }

    private static Envelope MakeEnvelope() => new(
        Schema:           "./schema/v1/step-library.schema.json",
        Version:          "1.0.0",
        GeneratedAt:      "2026-05-27T09:00:00Z",
        GeneratorVersion: "1.0.0",
        Enriched:         false,
        Domains:          [],
        Steps:            [],
        Signature:        null);
}
```

**Important:** The test class needs `IDisposable` for temp directory cleanup. Add the `IDisposable` interface to the class declaration and the `_dir` field + constructor + `Dispose` shown above. The existing `[Fact]` tests (which don't use a temp directory) work unchanged alongside this.

- [ ] **Step 2: Run new test**

```
dotnet test --filter "WriteCreatesFile" -q
```

Expected: PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 82 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Output/CanonicalJsonTests.cs
git commit -m "test: Write method creates pretty-printed JSON file (Story 10, task 3)"
```

---

## Task 4: Signer.Sign — digest format and algorithm

**Files:**
- Create: `Delta.DocGen/Output/Serialiser/Signer.cs`
- Create: `Delta.DocGen.Tests/Output/SignerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// Delta.DocGen.Tests/Output/SignerTests.cs
using Delta.DocGen.Model;
using Delta.DocGen.Output.Serialiser;
using FluentAssertions;

namespace Delta.DocGen.Tests.Output;

public sealed class SignerTests
{
    private static Envelope MakeEnvelope(IReadOnlyList<StepRecord>? steps = null) => new(
        Schema:           "./schema/v1/step-library.schema.json",
        Version:          "1.0.0",
        GeneratedAt:      "2026-05-27T09:00:00Z",
        GeneratorVersion: "1.0.0",
        Enriched:         false,
        Domains:          [],
        Steps:            steps ?? [],
        Signature:        null);

    [Fact]
    public void SignedEnvelopeHasNonEmptyDigest()
    {
        var signed = Signer.Sign(MakeEnvelope());

        signed.Signature.Should().NotBeNull();
        signed.Signature!.Digest.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AlgorithmIsSHA256()
    {
        var signed = Signer.Sign(MakeEnvelope());

        signed.Signature!.Algorithm.Should().Be("SHA-256");
    }

    [Fact]
    public void DigestIsLowercaseHexadecimal()
    {
        var signed = Signer.Sign(MakeEnvelope());

        signed.Signature!.Digest.Should().MatchRegex("^[0-9a-f]+$");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test --filter "SignedEnvelopeHas|AlgorithmIs|DigestIsLowercase" -q
```

Expected: FAIL — `Signer` not found.

- [ ] **Step 3: Create the implementation**

```csharp
// Delta.DocGen/Output/Serialiser/Signer.cs
using System.Security.Cryptography;
using System.Text;
using Delta.DocGen.Model;

namespace Delta.DocGen.Output.Serialiser;

public static class Signer
{
    public static Envelope Sign(Envelope envelope)
    {
        var unsigned = envelope with { Signature = null };
        var canonical = CanonicalJson.Serialise(unsigned);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        var digest = Convert.ToHexString(hash).ToLowerInvariant();
        return envelope with { Signature = new SignatureRecord("SHA-256", digest) };
    }
}
```

- [ ] **Step 4: Run to verify tests pass**

```
dotnet test --filter "SignedEnvelopeHas|AlgorithmIs|DigestIsLowercase" -q
```

Expected: all PASS.

- [ ] **Step 5: Run full suite**

```
dotnet test -q
```

Expected: 85 passing, 0 failing.

- [ ] **Step 6: Commit**

```
git add Delta.DocGen/Output/Serialiser/Signer.cs Delta.DocGen.Tests/Output/SignerTests.cs
git commit -m "feat: scaffold Signer with digest format tests (Story 10, task 4)"
```

---

## Task 5: Signer.Sign — determinism and content sensitivity

**Files:**
- Modify: `Delta.DocGen.Tests/Output/SignerTests.cs`

- [ ] **Step 1: Add two tests**

```csharp
[Fact]
public void DigestIsDeterministic()
{
    var envelope = MakeEnvelope();

    var signed1 = Signer.Sign(envelope);
    var signed2 = Signer.Sign(envelope);

    signed1.Signature!.Digest.Should().Be(signed2.Signature!.Digest);
}

[Fact]
public void DigestChangesWhenStepsChange()
{
    var step = new StepRecord(
        "auth-a1b2c3d4", StepType.Given, "I am logged in", [],
        "Auth/AuthSteps.cs", 1, "Auth", [], 0, "", "", []);
    var emptyEnvelope = MakeEnvelope(steps: []);
    var filledEnvelope = MakeEnvelope(steps: [step]);

    var signedEmpty  = Signer.Sign(emptyEnvelope);
    var signedFilled = Signer.Sign(filledEnvelope);

    signedEmpty.Signature!.Digest.Should().NotBe(signedFilled.Signature!.Digest);
}
```

- [ ] **Step 2: Run new tests**

```
dotnet test --filter "DigestIsDeterministic|DigestChangesWhen" -q
```

Expected: both PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 87 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Output/SignerTests.cs
git commit -m "test: digest determinism and content sensitivity for Signer (Story 10, task 5)"
```

---

## Task 6: Signer — signature field excluded from hash

**Files:**
- Modify: `Delta.DocGen.Tests/Output/SignerTests.cs`

- [ ] **Step 1: Add test**

This test verifies the two-phase contract: sign once, then verify by re-computing the digest from the unsigned form.

```csharp
[Fact]
public void SignatureFieldIsExcludedFromHashedContent()
{
    // Sign the envelope. Then recompute: strip signature, serialise canonically,
    // hash — must match the stored digest.
    var envelope = MakeEnvelope();
    var signed = Signer.Sign(envelope);

    var unsigned = signed with { Signature = null };
    var canonical = CanonicalJson.Serialise(unsigned);
    var expectedDigest = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical)))
        .ToLowerInvariant();

    signed.Signature!.Digest.Should().Be(expectedDigest);
}
```

- [ ] **Step 2: Run new test**

```
dotnet test --filter "SignatureFieldIsExcluded" -q
```

Expected: PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 88 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Output/SignerTests.cs
git commit -m "test: signature field excluded from hash for Signer (Story 10, task 6)"
```

---

## Task 7: Update developer guide

**Files:**
- Modify: `docs/developer-guide.md`

- [ ] **Step 1: Mark Story 10 complete**

Find the story table row:
```
| 10 | Canonical JSON + signing | `CanonicalJson`, `Signer` + tests | ⬜ |
```
Change `⬜` to `✅`.

- [ ] **Step 2: Update overview status range**

Find:
```
| 1–9 | ✅ Complete, merged to master, pushed to GitHub |
| 10–15 | ⬜ Not started |
```
Change to:
```
| 1–10 | ✅ Complete, merged to master, pushed to GitHub |
| 11–15 | ⬜ Not started |
```

- [ ] **Step 3: Update test count**

Run `dotnet test -q` to get the exact count, then update the `**Test count:**` line to match.

- [ ] **Step 4: Update "What's next" section**

Find:
```
### What's next — Story 10: Canonical JSON and signing
```
Replace heading and body with:
```markdown
### What's next — Story 11: JSON Schema

The next story implements the JSON Schema file and `SchemaWriter`. Key points:

- The schema file is embedded as a resource: `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json`
- `SchemaWriter.Write(string outputDir)` extracts the embedded resource and writes it to `<outputDir>/schema/v1/step-library.schema.json`
- The schema validates the envelope structure (version, domains, steps array, each step's required fields)
- Story 11 only creates and validates the schema — the pipeline runner (Story 12) will call SchemaWriter
```

- [ ] **Step 5: Run full suite to confirm**

```
dotnet test -q
```

- [ ] **Step 6: Commit**

```
git add docs/developer-guide.md
git commit -m "docs: mark Story 10 complete, update test count and what's-next (Story 10, task 7)"
```
