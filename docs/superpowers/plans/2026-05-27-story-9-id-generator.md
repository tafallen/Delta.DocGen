# Story 9: IdGenerator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `IdGenerator.Generate`, which converts domain-assigned `RawStep` records into fully-resolved `StepRecord` records (with stable IDs and usage counts) and a deduplicated `DomainRecord` list for the envelope.

**Architecture:** For each step, a stable ID is computed as `<domain-prefix>-<8-hex-chars>`, where the hex is the first 8 characters of the SHA-256 hash of the normalised pattern (`pattern.Trim().ToLowerInvariant()`). The domain prefix is the domain ID lowercased with non-alphanumeric characters replaced by hyphens. Collisions (two steps producing the same ID) cause a fatal `InvalidOperationException`. Domain records are built from distinct domains in first-occurrence order; labels come from the domain rules list (fallback: use the domain ID itself as the label). Returns a tuple of steps + domains.

**Tech Stack:** .NET 8, `System.Security.Cryptography.SHA256` (in-box), xUnit 2.9.3, FluentAssertions 6.12.0

**Prerequisite:** Story 8 (DomainAssigner) complete — steps arrive with `Domain` already populated.

---

## File structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Delta.DocGen/Pipeline/IdGenerator.cs` | Create | Build `StepRecord[]` + `DomainRecord[]` from domain-assigned `RawStep[]` |
| `Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs` | Create | All tests for `IdGenerator` |
| `docs/developer-guide.md` | Modify | Mark Story 9 ✅; update test count |

---

## Key types (already exist — do not modify)

```csharp
// Delta.DocGen/Model/RawStep.cs
public sealed record RawStep(
    StepType Type, string Pattern, IReadOnlyList<ParamRecord> Params,
    string File, int Line, string Source, string Domain = "");

// Delta.DocGen/Model/StepRecord.cs
public sealed record StepRecord(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("type")]        StepType Type,
    [property: JsonPropertyName("pattern")]     string Pattern,
    [property: JsonPropertyName("params")]      IReadOnlyList<ParamRecord> Params,
    [property: JsonPropertyName("file")]        string File,
    [property: JsonPropertyName("line")]        int Line,
    [property: JsonPropertyName("domain")]      string Domain,
    [property: JsonPropertyName("tags")]        IReadOnlyList<string> Tags,
    [property: JsonPropertyName("used")]        int Used,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("source")]      string Source,
    [property: JsonPropertyName("suggestsNext")]IReadOnlyList<string> SuggestsNext);

// Delta.DocGen/Model/DomainRecord.cs
public sealed record DomainRecord(
    [property: JsonPropertyName("id")]    string Id,
    [property: JsonPropertyName("label")] string Label);

// Delta.DocGen/Config/DomainRule.cs
public sealed record DomainRule(string Pattern, string Domain, string Label);
```

---

## Task 1: Scaffold + field-mapping test

**Files:**
- Create: `Delta.DocGen/Pipeline/IdGenerator.cs`
- Create: `Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Pipeline;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class IdGeneratorTests
{
    [Fact]
    public void GenerateMapsRawStepFieldsToStepRecord()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in",
                [new("username", "string", "")],
                "Auth/AuthSteps.cs", 5, "source text", "Auth")
        };
        var usageCounts = new Dictionary<string, int> { ["I am logged in"] = 3 };
        var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

        var (records, _) = IdGenerator.Generate(steps, usageCounts, rules, "General", NullDocGenLogger.Instance);

        records.Should().ContainSingle();
        var r = records[0];
        r.Type.Should().Be(StepType.Given);
        r.Pattern.Should().Be("I am logged in");
        r.Params.Should().ContainSingle(p => p.Name == "username" && p.Type == "string");
        r.File.Should().Be("Auth/AuthSteps.cs");
        r.Line.Should().Be(5);
        r.Domain.Should().Be("Auth");
        r.Source.Should().Be("source text");
        r.Used.Should().Be(3);
        r.Tags.Should().BeEmpty();
        r.Description.Should().BeEmpty();
        r.SuggestsNext.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```
dotnet test --filter "GenerateMapsRawStepFieldsToStepRecord" -q
```

Expected: FAIL — `IdGenerator` not found.

- [ ] **Step 3: Create the full implementation**

```csharp
// Delta.DocGen/Pipeline/IdGenerator.cs
using System.Security.Cryptography;
using System.Text;
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Pipeline;

public static class IdGenerator
{
    public static (IReadOnlyList<StepRecord> Steps, IReadOnlyList<DomainRecord> Domains) Generate(
        IReadOnlyList<RawStep> steps,
        IReadOnlyDictionary<string, int> usageCounts,
        IReadOnlyList<DomainRule> domainRules,
        string fallbackDomain,
        IDocGenLogger logger)
    {
        var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var records = new List<StepRecord>(steps.Count);

        foreach (var step in steps)
        {
            var id = BuildId(step.Domain, step.Pattern);
            if (seenIds.TryGetValue(id, out var existingPattern))
                throw new InvalidOperationException(
                    $"Step ID collision: '{id}' generated for both '{existingPattern}' and '{step.Pattern}'.");
            seenIds[id] = step.Pattern;

            var used = usageCounts.TryGetValue(step.Pattern, out var count) ? count : 0;
            records.Add(new StepRecord(
                Id:          id,
                Type:        step.Type,
                Pattern:     step.Pattern,
                Params:      step.Params,
                File:        step.File,
                Line:        step.Line,
                Domain:      step.Domain,
                Tags:        [],
                Used:        used,
                Description: "",
                Source:      step.Source,
                SuggestsNext: []));
        }

        logger.Info($"ID generation complete: {records.Count} step(s) processed.");
        var domains = BuildDomains(steps, domainRules, fallbackDomain);
        return (records.AsReadOnly(), domains);
    }

    private static string BuildId(string domain, string pattern)
        => $"{DomainPrefix(domain)}-{PatternHash(pattern)}";

    internal static string DomainPrefix(string domain)
    {
        var sb = new StringBuilder();
        foreach (var ch in domain.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('-');
        }
        var result = sb.ToString().Trim('-');
        return result.Length > 0 ? result : "unknown";
    }

    internal static string PatternHash(string pattern)
    {
        var normalized = pattern.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    private static IReadOnlyList<DomainRecord> BuildDomains(
        IReadOnlyList<RawStep> steps,
        IReadOnlyList<DomainRule> domainRules,
        string fallbackDomain)
    {
        var labelLookup = domainRules
            .GroupBy(r => r.Domain, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DomainRecord>();
        foreach (var step in steps)
        {
            if (!seen.Add(step.Domain)) continue;
            var label = labelLookup.TryGetValue(step.Domain, out var l) ? l : step.Domain;
            result.Add(new DomainRecord(step.Domain, label));
        }
        return result.AsReadOnly();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

```
dotnet test --filter "GenerateMapsRawStepFieldsToStepRecord" -q
```

Expected: PASS.

- [ ] **Step 5: Run full suite**

```
dotnet test -q
```

Expected: 71 passing, 0 failing.

- [ ] **Step 6: Commit**

```
git add Delta.DocGen/Pipeline/IdGenerator.cs Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs
git commit -m "feat: scaffold IdGenerator with field-mapping test (Story 9, task 1)"
```

---

## Task 2: Usage counts — present and absent

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs`

- [ ] **Step 1: Add test**

```csharp
[Fact]
public void MissingUsageCountDefaultsToZero()
{
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "", "Auth")
    };

    var (records, _) = IdGenerator.Generate(
        steps, new Dictionary<string, int>(), [], "General", NullDocGenLogger.Instance);

    records[0].Used.Should().Be(0);
}
```

- [ ] **Step 2: Run new test**

```
dotnet test --filter "MissingUsageCountDefaultsToZero" -q
```

Expected: PASS (no code changes needed).

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 72 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs
git commit -m "test: missing usage count defaults to zero (Story 9, task 2)"
```

---

## Task 3: ID format and stability

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs`

- [ ] **Step 1: Add two tests**

```csharp
[Fact]
public void IdMatchesDomainPrefixAndEightCharHexPattern()
{
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "", "Auth")
    };

    var (records, _) = IdGenerator.Generate(
        steps, new Dictionary<string, int>(), [], "General", NullDocGenLogger.Instance);

    records[0].Id.Should().MatchRegex(@"^auth-[0-9a-f]{8}$");
}

[Fact]
public void IdIsStableRegardlessOfFileOrLineNumber()
{
    // ID is based on domain + pattern only — file moves and line changes must not affect it.
    var step1 = new RawStep(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs",    1,  "", "Auth");
    var step2 = new RawStep(StepType.Given, "I am logged in", [], "Auth/NewAuthSteps.cs", 99, "", "Auth");

    var (r1, _) = IdGenerator.Generate([step1], new Dictionary<string, int>(), [], "General", NullDocGenLogger.Instance);
    var (r2, _) = IdGenerator.Generate([step2], new Dictionary<string, int>(), [], "General", NullDocGenLogger.Instance);

    r1[0].Id.Should().Be(r2[0].Id);
}
```

- [ ] **Step 2: Run new tests**

```
dotnet test --filter "IdMatches|IdIsStable" -q
```

Expected: both PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 74 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs
git commit -m "test: ID format and stability for IdGenerator (Story 9, task 3)"
```

---

## Task 4: Collision detection

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs`

- [ ] **Step 1: Add test**

A collision occurs when two different steps produce the same ID. Since the ID is `domain + pattern`, two steps with the **same pattern and same domain** produce the same ID — this is the realistic collision scenario (duplicate step bindings).

```csharp
[Fact]
public void DuplicatePatternInSameDomainThrowsInvalidOperationException()
{
    // Same pattern, same domain = same ID = collision.
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs",  1, "", "Auth"),
        new(StepType.When,  "I am logged in", [], "Auth/AuthSteps.cs", 10, "", "Auth"),
    };

    var act = () => IdGenerator.Generate(
        steps, new Dictionary<string, int>(), [], "General", NullDocGenLogger.Instance);

    act.Should().Throw<InvalidOperationException>().WithMessage("*collision*");
}
```

- [ ] **Step 2: Run new test**

```
dotnet test --filter "DuplicatePattern" -q
```

Expected: PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 75 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs
git commit -m "test: collision detection for IdGenerator (Story 9, task 4)"
```

---

## Task 5: Domain records

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs`

- [ ] **Step 1: Add two tests**

```csharp
[Fact]
public void DomainRecordsAreDistinctInFirstOccurrenceOrder()
{
    var steps = new List<RawStep>
    {
        new(StepType.Given, "step one",   [], "Auth/AuthSteps.cs",  1, "", "Auth"),
        new(StepType.Given, "step two",   [], "Forms/FormSteps.cs", 1, "", "Forms"),
        new(StepType.Given, "step three", [], "Auth/AuthSteps2.cs", 5, "", "Auth"),  // Auth again
    };
    var rules = new List<DomainRule>
    {
        new("Auth/**",  "Auth",  "Auth & Identity"),
        new("Forms/**", "Forms", "Forms & Input"),
    };

    var (_, domains) = IdGenerator.Generate(
        steps, new Dictionary<string, int>(), rules, "General", NullDocGenLogger.Instance);

    domains.Should().HaveCount(2);
    domains[0].Id.Should().Be("Auth");
    domains[0].Label.Should().Be("Auth & Identity");
    domains[1].Id.Should().Be("Forms");
    domains[1].Label.Should().Be("Forms & Input");
}

[Fact]
public void FallbackDomainUsesItsOwnIdAsLabel()
{
    var steps = new List<RawStep>
    {
        new(StepType.Given, "step one", [], "Other/OtherSteps.cs", 1, "", "General")
    };

    var (_, domains) = IdGenerator.Generate(
        steps, new Dictionary<string, int>(), [], "General", NullDocGenLogger.Instance);

    domains.Should().ContainSingle();
    domains[0].Id.Should().Be("General");
    domains[0].Label.Should().Be("General");
}
```

- [ ] **Step 2: Run new tests**

```
dotnet test --filter "DomainRecordsAreDistinct|FallbackDomain" -q
```

Expected: both PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 77 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs
git commit -m "test: domain records for IdGenerator (Story 9, task 5)"
```

---

## Task 6: Update developer guide

**Files:**
- Modify: `docs/developer-guide.md`

- [ ] **Step 1: Mark Story 9 complete**

Find the story table row:
```
| 9 | ID generation | `IdGenerator` + tests | ⬜ |
```
Change `⬜` to `✅`.

- [ ] **Step 2: Update overview status range**

Find:
```
| 1–8 | ✅ Complete, merged to master, pushed to GitHub |
| 9–15 | ⬜ Not started |
```
Change to:
```
| 1–9 | ✅ Complete, merged to master, pushed to GitHub |
| 10–15 | ⬜ Not started |
```

- [ ] **Step 3: Update test count**

Run `dotnet test -q` to get the exact count, then update:
```
**Test count:** 77 passing (13 config, 9 discoverer, 16 extractor, 10 usage-counter, 7 domain-assigner, 7 id-generator, extras)
```
Adjust numbers to match actual `dotnet test -q` output.

- [ ] **Step 4: Update "What's next" section**

Find:
```
### What's next — Story 9: ID generation
```
Replace heading and body with:
```markdown
### What's next — Story 10: Canonical JSON and signing

The next story implements `CanonicalJson` and `Signer`. Key points:

- `CanonicalJson.Serialise(object)` → `string`: serialise any object to compact JSON with all object keys sorted alphabetically (recursively). Uses `System.Text.Json.Nodes.JsonNode` internally.
- `CanonicalJson.Write(Envelope, string outputPath)`: serialise the signed envelope as pretty-printed JSON and write to file, creating the output directory if absent.
- `Signer.Sign(Envelope)` → `Envelope`: set `Signature = null`, serialise canonically (null fields omitted), SHA-256 hash the UTF-8 bytes, hex-encode, return envelope with `Signature = new SignatureRecord("SHA-256", digest)`.
- The canonical options must use `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` so the `signature` field is absent from the bytes that are hashed.
```

- [ ] **Step 5: Run full suite to confirm**

```
dotnet test -q
```

- [ ] **Step 6: Commit**

```
git add docs/developer-guide.md
git commit -m "docs: mark Story 9 complete, update test count and what's-next (Story 9, task 6)"
```
