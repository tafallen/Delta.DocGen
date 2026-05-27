# Story 8: DomainAssigner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `DomainAssigner.Assign`, which takes a list of `RawStep` records (all with `Domain = ""`) and a list of glob rules from config, and returns a new list with the `Domain` field populated on each step.

**Architecture:** One `Matcher` (from `Microsoft.Extensions.FileSystemGlobbing`) is compiled per domain rule at call time. Steps are iterated; the first rule whose matcher reports `HasMatches` against `step.File` wins. Unmatched steps receive `fallbackDomain` and a `Warn` log entry. `RawStep` is an immutable record — assignment uses `step with { Domain = ... }`.

**Tech Stack:** .NET 8, `Microsoft.Extensions.FileSystemGlobbing` 8.0.0 (already in `Delta.DocGen.csproj`), xUnit 2.9.3, FluentAssertions 6.12.0

---

## File structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Delta.DocGen/Pipeline/DomainAssigner.cs` | Create | Glob-match each step's `.cs` file path against config rules; assign domain |
| `Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs` | Create | All tests for `DomainAssigner` |
| `docs/developer-guide.md` | Modify | Mark Story 8 ✅; update test count |

---

## Key types (already exist — do not modify)

```csharp
// Delta.DocGen/Model/RawStep.cs
public sealed record RawStep(
    StepType Type, string Pattern, IReadOnlyList<ParamRecord> Params,
    string File, int Line, string Source, string Domain = "");

// Delta.DocGen/Config/DomainRule.cs
public sealed record DomainRule(string Pattern, string Domain, string Label);
```

`step.File` is a forward-slash relative path (e.g. `"Auth/AuthSteps.cs"`). The glob rule `Pattern` uses the same forward-slash convention (e.g. `"Auth/**"`).

`Microsoft.Extensions.FileSystemGlobbing.Matcher` usage:
```csharp
var matcher = new Matcher();
matcher.AddInclude("Auth/**");
bool matched = matcher.Match("Auth/AuthSteps.cs").HasMatches; // true
```

---

## Task 1: Scaffold + step matching a rule

**Files:**
- Create: `Delta.DocGen/Pipeline/DomainAssigner.cs`
- Create: `Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Pipeline;
using Delta.DocGen.Tests.Logging;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class DomainAssignerTests
{
    [Fact]
    public void StepMatchingRuleIsAssignedDomain()
    {
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };
        var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

        var result = DomainAssigner.Assign(steps, rules, "General", NullDocGenLogger.Instance);

        result.Should().ContainSingle();
        result[0].Domain.Should().Be("Auth");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```
dotnet test --filter "StepMatchingRuleIsAssignedDomain" -q
```

Expected: FAIL — `DomainAssigner` not found.

- [ ] **Step 3: Create the stub implementation**

```csharp
// Delta.DocGen/Pipeline/DomainAssigner.cs
using Microsoft.Extensions.FileSystemGlobbing;
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Pipeline;

public static class DomainAssigner
{
    public static IReadOnlyList<RawStep> Assign(
        IReadOnlyList<RawStep> steps,
        IReadOnlyList<DomainRule> rules,
        string fallbackDomain,
        IDocGenLogger logger)
    {
        var matchers = rules
            .Select(r => (Rule: r, Matcher: BuildMatcher(r.Pattern)))
            .ToList();

        var result = new List<RawStep>(steps.Count);
        foreach (var step in steps)
        {
            var matched = false;
            foreach (var (rule, matcher) in matchers)
            {
                if (matcher.Match(step.File).HasMatches)
                {
                    result.Add(step with { Domain = rule.Domain });
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                logger.Warn($"Step in {step.File} matched no domain rule; assigned '{fallbackDomain}'.");
                result.Add(step with { Domain = fallbackDomain });
            }
        }
        logger.Info($"Domain assignment complete: {result.Count} step(s) assigned.");
        return result.AsReadOnly();
    }

    private static Matcher BuildMatcher(string pattern)
    {
        var matcher = new Matcher();
        matcher.AddInclude(pattern);
        return matcher;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

```
dotnet test --filter "StepMatchingRuleIsAssignedDomain" -q
```

Expected: PASS

- [ ] **Step 5: Run full suite**

```
dotnet test -q
```

Expected: 64 passing, 0 failing.

- [ ] **Step 6: Commit**

```
git add Delta.DocGen/Pipeline/DomainAssigner.cs Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs
git commit -m "feat: scaffold DomainAssigner with first matching test (Story 8, task 1)"
```

---

## Task 2: Fallback domain and warning logging

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs`

- [ ] **Step 1: Add three tests**

```csharp
[Fact]
public void StepMatchingNoRuleIsAssignedFallbackDomain()
{
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I do something", [], "Other/OtherSteps.cs", 1, "")
    };
    var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

    var result = DomainAssigner.Assign(steps, rules, "General", NullDocGenLogger.Instance);

    result[0].Domain.Should().Be("General");
}

[Fact]
public void UnmatchedStepLogsWarning()
{
    var logger = new CapturingDocGenLogger();
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I do something", [], "Other/OtherSteps.cs", 1, "")
    };
    var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

    DomainAssigner.Assign(steps, rules, "General", logger);

    logger.WarnMessages.Should().ContainSingle(m => m.Contains("Other/OtherSteps.cs"));
}

[Fact]
public void MatchedStepDoesNotLogWarning()
{
    var logger = new CapturingDocGenLogger();
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
    };
    var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

    DomainAssigner.Assign(steps, rules, "General", logger);

    logger.WarnMessages.Should().BeEmpty();
}
```

- [ ] **Step 2: Run new tests**

```
dotnet test --filter "StepMatchingNoRule|UnmatchedStep|MatchedStepDoesNot" -q
```

Expected: all PASS (no code changes needed).

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 67 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs
git commit -m "test: fallback domain and warning logging for DomainAssigner (Story 8, task 2)"
```

---

## Task 3: First-match-wins (rule order)

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs`

- [ ] **Step 1: Add test**

```csharp
[Fact]
public void FirstMatchingRuleWins()
{
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
    };
    var rules = new List<DomainRule>
    {
        new("Auth/**", "Auth",    "Auth & Identity"),
        new("**",      "General", "General"),            // catch-all — must NOT win
    };

    var result = DomainAssigner.Assign(steps, rules, "General", NullDocGenLogger.Instance);

    result[0].Domain.Should().Be("Auth");
}
```

- [ ] **Step 2: Run new test**

```
dotnet test --filter "FirstMatchingRuleWins" -q
```

Expected: PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 68 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs
git commit -m "test: first-match-wins rule order for DomainAssigner (Story 8, task 3)"
```

---

## Task 4: Edge cases — empty steps and empty rules

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs`

- [ ] **Step 1: Add two tests**

```csharp
[Fact]
public void EmptyStepListReturnsEmptyList()
{
    var rules = new List<DomainRule> { new("Auth/**", "Auth", "Auth & Identity") };

    var result = DomainAssigner.Assign([], rules, "General", NullDocGenLogger.Instance);

    result.Should().BeEmpty();
}

[Fact]
public void EmptyRulesAssignsFallbackToAllSteps()
{
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, ""),
        new(StepType.When,  "I click submit",  [], "Forms/FormSteps.cs", 5, "")
    };

    var result = DomainAssigner.Assign(steps, [], "General", NullDocGenLogger.Instance);

    result.Should().HaveCount(2);
    result.Should().AllSatisfy(s => s.Domain.Should().Be("General"));
}
```

- [ ] **Step 2: Run new tests**

```
dotnet test --filter "EmptyStepList|EmptyRules" -q
```

Expected: both PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 70 passing, 0 failing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs
git commit -m "test: edge cases for DomainAssigner (Story 8, task 4)"
```

---

## Task 5: Update developer guide

**Files:**
- Modify: `docs/developer-guide.md`

- [ ] **Step 1: Mark Story 8 complete**

Find the story table row:
```
| 8 | Domain assignment | `DomainAssigner` + tests | ⬜ |
```
Change `⬜` to `✅`.

- [ ] **Step 2: Update overview status range**

Find:
```
| 1–7 | ✅ Complete, merged to master, pushed to GitHub |
| 8–15 | ⬜ Not started |
```
Change to:
```
| 1–8 | ✅ Complete, merged to master, pushed to GitHub |
| 9–15 | ⬜ Not started |
```

- [ ] **Step 3: Update test count**

Find the `**Test count:**` line; update to:
```
**Test count:** 70 passing (13 config, 9 discoverer, 16 extractor, 10 usage-counter, 7 domain-assigner, 5 story-7-extras)
```

Note: run `dotnet test -q` to confirm the exact count, then adjust the line to match.

- [ ] **Step 4: Update "What's next" section**

Find:
```
### What's next — Story 8: Domain assignment
```
Replace the heading and its body with:
```markdown
### What's next — Story 9: ID generation

The next story implements `IdGenerator`. Key points:

- Input: domain-assigned `RawStep[]` + `IReadOnlyDictionary<string, int>` usage counts + domain rules + fallback domain
- Output: `(IReadOnlyList<StepRecord> Steps, IReadOnlyList<DomainRecord> Domains)`
- ID format: `<domain-prefix>-<8-hex-chars>` where the hex is the first 8 chars of SHA-256 of `pattern.Trim().ToLowerInvariant()`
- Domain prefix: domain ID lowercased, non-alphanumeric chars replaced with hyphens
- Collisions cause a fatal `InvalidOperationException` with both conflicting patterns in the message
- `DomainRecord` list: distinct domains in first-occurrence order; label from matching domain rule (or domain ID as label for the fallback domain)
- `StepRecord` V1 defaults: `Tags = []`, `Description = ""`, `SuggestsNext = []`
```

- [ ] **Step 5: Run full suite to confirm still 70 passing**

```
dotnet test -q
```

- [ ] **Step 6: Commit**

```
git add docs/developer-guide.md
git commit -m "docs: mark Story 8 complete, update test count and what's-next (Story 8, task 5)"
```
