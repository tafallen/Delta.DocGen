# Story 7: UsageCounter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `UsageCounter`, which parses Gherkin `.feature` files and counts how many times each known step pattern is referenced, producing `IReadOnlyDictionary<string, int>` keyed by pattern string.

**Architecture:** `UsageCounter.Count(steps, relativePath, root, logger)` reads one `.feature` file, parses it with the Gherkin library, converts each `RawStep` pattern to a compiled `Regex` (translating Cucumber Expression placeholders like `{int}` → `\d+`), walks every step line in every scenario, and increments the count for the first matching pattern. Unmatched step lines produce a warning. The method is called once per feature file by the pipeline runner (Story 12); counts are accumulated externally. The output dictionary always contains every known pattern (with 0 for unmatched ones).

**Tech Stack:** .NET 8, Gherkin 29.0.0 (already in `Delta.DocGen.csproj`), `Gherkin.Ast` namespace, `System.Text.RegularExpressions`, xUnit 2.9.3, FluentAssertions 6.12.0

---

## File structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs` | Create | Parse one feature file; match steps against patterns; return counts |
| `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs` | Create | All tests for `UsageCounter` |
| `docs/developer-guide.md` | Modify | Mark Story 7 ✅; update test count |

---

## Gherkin 29 API quick reference

```csharp
using Gherkin;        // Parser, ParserException
using Gherkin.Ast;    // GherkinDocument, Feature, FeatureChild, Scenario, Step, Rule, RuleChild

var doc = new Parser().Parse(new StringReader(featureFileText));
// doc.Feature is Feature? — null for completely empty files
// doc.Feature.Children is IEnumerable<FeatureChild>
// FeatureChild has .Scenario (Scenario?), .Background (Background?), .Rule (Rule?)
// Scenario has .Steps (IEnumerable<Step>)
// Step has .Text (string — step text without keyword)
// Rule has .Children (IEnumerable<RuleChild>)
// RuleChild has .Scenario (Scenario?), .Background (Background?)
```

A `Scenario Outline` is represented as a `Scenario` with non-empty `.Examples`. Its `.Steps` contain the template text (e.g., `"I have <count> items"`). Template placeholders like `<count>` will not match `{int}` patterns — this is expected behaviour for Story 7; outline substitution is a future enhancement.

---

## Cucumber Expression → Regex conversion

| Cucumber Expression | Regex fragment |
|--------------------|----------------|
| `{int}` | `\d+` |
| `{decimal}` | `[\d.]+` |
| `{float}` | `[\d.]+` |
| `{bigdecimal}` | `[\d.]+` |
| `{string}` | `"[^"]*"` |
| `{word}` | `\S+` |
| `{anything_else}` | `.+` |
| (literal text) | `Regex.Escape(text)` |

Algorithm: walk the pattern left-to-right; escape each literal segment; replace each `{type}` with its regex fragment; anchor with `^...$`.

---

## Task 1: Scaffold + empty feature file

**Files:**
- Create: `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs`
- Create: `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs`

- [ ] **Step 1: Create the stub implementation**

```csharp
// Delta.DocGen/Scanner/Gherkin/UsageCounter.cs
using System.Text;
using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Scanner.Gherkin;

public static class UsageCounter
{
    private static readonly Regex CucumberPlaceholder =
        new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, int> Count(
        IReadOnlyList<RawStep> steps,
        string relativePath,
        string root,
        IDocGenLogger logger)
    {
        throw new NotImplementedException();
    }

    private static Regex BuildMatchRegex(string cucumberPattern)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 2: Create the test file with the empty-feature test**

```csharp
// Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Scanner.Gherkin;
using Delta.DocGen.Tests.Logging;
using FluentAssertions;

namespace Delta.DocGen.Tests.Scanner.Gherkin;

public sealed class UsageCounterTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public UsageCounterTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteFeatureFile(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return relativePath;
    }

    [Fact]
    public void EmptyFeatureFileReturnsZeroCountForEachPattern()
    {
        var path = WriteFeatureFile("Features/Empty.feature", """
            Feature: Empty
            """);
        var steps = new List<RawStep>
        {
            new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
        };

        var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

        counts.Should().ContainKey("I am logged in");
        counts["I am logged in"].Should().Be(0);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```
dotnet test --filter "EmptyFeatureFileReturnsZeroCountForEachPattern" -v minimal
```

Expected: FAIL — `NotImplementedException`

- [ ] **Step 4: Implement `Count` and `BuildMatchRegex`**

```csharp
public static IReadOnlyDictionary<string, int> Count(
    IReadOnlyList<RawStep> steps,
    string relativePath,
    string root,
    IDocGenLogger logger)
{
    var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    var text = File.ReadAllText(fullPath, Encoding.UTF8);

    // Deduplicate patterns; initialise all counts to 0
    var counts = steps
        .GroupBy(s => s.Pattern, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, _ => 0, StringComparer.Ordinal);

    var regexes = counts.Keys.ToDictionary(
        p => p,
        p => BuildMatchRegex(p),
        StringComparer.Ordinal);

    GherkinDocument doc;
    try
    {
        doc = new Parser().Parse(new StringReader(text));
    }
    catch (ParserException ex)
    {
        logger.Warn($"Could not parse feature file {relativePath}: {ex.Message}");
        return counts;
    }

    if (doc.Feature is { } feature)
    {
        foreach (var child in feature.Children)
        {
            MatchScenario(child.Scenario, counts, regexes, relativePath, logger);
            if (child.Rule is { } rule)
            {
                foreach (var ruleChild in rule.Children)
                    MatchScenario(ruleChild.Scenario, counts, regexes, relativePath, logger);
            }
        }
    }

    logger.Info($"  {relativePath}: feature file processed");
    return counts;
}

private static void MatchScenario(
    Scenario? scenario,
    Dictionary<string, int> counts,
    Dictionary<string, Regex> regexes,
    string relativePath,
    IDocGenLogger logger)
{
    if (scenario is null) return;
    foreach (var step in scenario.Steps)
    {
        var matched = false;
        foreach (var (pattern, regex) in regexes)
        {
            if (regex.IsMatch(step.Text))
            {
                counts[pattern]++;
                matched = true;
                break;
            }
        }
        if (!matched)
            logger.Warn($"  Unmatched step in {relativePath}: \"{step.Text}\"");
    }
}

private static Regex BuildMatchRegex(string cucumberPattern)
{
    // Patterns starting with '^' are old-style regex — used as-is per spec
    if (cucumberPattern.StartsWith('^'))
        return new Regex(cucumberPattern, RegexOptions.Compiled);

    var sb = new StringBuilder("^");
    var lastIndex = 0;
    foreach (Match m in CucumberPlaceholder.Matches(cucumberPattern))
    {
        sb.Append(Regex.Escape(cucumberPattern[lastIndex..m.Index]));
        sb.Append(m.Groups[1].Value switch
        {
            "int"        => @"\d+",
            "decimal"    => @"[\d.]+",
            "float"      => @"[\d.]+",
            "bigdecimal" => @"[\d.]+",
            "string"     => "\"[^\"]*\"",
            "word"       => @"\S+",
            _            => @".+",
        });
        lastIndex = m.Index + m.Length;
    }
    sb.Append(Regex.Escape(cucumberPattern[lastIndex..]));
    sb.Append('$');
    return new Regex(sb.ToString(), RegexOptions.Compiled);
}
```

- [ ] **Step 5: Run the test to verify it passes**

```
dotnet test --filter "EmptyFeatureFileReturnsZeroCountForEachPattern" -v minimal
```

Expected: PASS

- [ ] **Step 6: Run the full suite**

```
dotnet test -q
```

Expected: all 46 existing tests + 1 new = 47 passing, 0 failing

- [ ] **Step 7: Commit**

```
git add Delta.DocGen/Scanner/Gherkin/UsageCounter.cs Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs
git commit -m "feat: scaffold UsageCounter with empty-feature test (Story 7, task 1)"
```

---

## Task 2: Literal step matching

**Files:**
- Modify: `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs`

- [ ] **Step 1: Add the literal-step test**

Add this test to `UsageCounterTests`:

```csharp
[Fact]
public void MatchesLiteralStep()
{
    var path = WriteFeatureFile("Features/Auth.feature", """
        Feature: Auth

          Scenario: Login
            Given I am logged in
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
    };

    var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

    counts["I am logged in"].Should().Be(1);
}

[Fact]
public void StepNotUsedInFeatureFileHasCountZero()
{
    var path = WriteFeatureFile("Features/Auth.feature", """
        Feature: Auth

          Scenario: Login
            Given I am logged in
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, ""),
        new(StepType.When,  "I click the button", [], "Auth/AuthSteps.cs", 5, "")
    };

    var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

    counts["I am logged in"].Should().Be(1);
    counts["I click the button"].Should().Be(0);
}
```

- [ ] **Step 2: Run new tests to verify they pass**

```
dotnet test --filter "MatchesLiteralStep|StepNotUsedInFeatureFileHasCountZero" -v minimal
```

Expected: both PASS (no code change needed — implementation is complete from Task 1)

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 49 passing, 0 failing

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs
git commit -m "test: literal step matching for UsageCounter (Story 7, task 2)"
```

---

## Task 3: Cucumber Expression placeholder matching

**Files:**
- Modify: `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs`

- [ ] **Step 1: Add placeholder-matching tests**

Add these tests to `UsageCounterTests`:

```csharp
[Fact]
public void MatchesIntPlaceholder()
{
    var path = WriteFeatureFile("Features/Shop.feature", """
        Feature: Shop

          Scenario: Add items
            Given I have 5 items
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I have {int} items", [], "Shop/ShopSteps.cs", 1, "")
    };

    var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

    counts["I have {int} items"].Should().Be(1);
}

[Fact]
public void MatchesStringPlaceholder()
{
    var path = WriteFeatureFile("Features/Auth.feature", """
        Feature: Auth

          Scenario: Login as admin
            Given I am logged in as "admin"
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in as {string}", [], "Auth/AuthSteps.cs", 1, "")
    };

    var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

    counts["I am logged in as {string}"].Should().Be(1);
}

[Fact]
public void MatchesDecimalPlaceholder()
{
    var path = WriteFeatureFile("Features/Shop.feature", """
        Feature: Shop

          Scenario: Pricing
            Given a product costs 9.99
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "a product costs {decimal}", [], "Shop/ShopSteps.cs", 1, "")
    };

    var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

    counts["a product costs {decimal}"].Should().Be(1);
}
```

- [ ] **Step 2: Run new tests to verify they pass**

```
dotnet test --filter "MatchesIntPlaceholder|MatchesStringPlaceholder|MatchesDecimalPlaceholder" -v minimal
```

Expected: all PASS (no code change needed)

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 52 passing, 0 failing

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs
git commit -m "test: Cucumber Expression placeholder matching for UsageCounter (Story 7, task 3)"
```

---

## Task 4: Multiple scenarios — cumulative counts

**Files:**
- Modify: `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs`

- [ ] **Step 1: Add cumulation tests**

Add these tests to `UsageCounterTests`:

```csharp
[Fact]
public void SameStepUsedInMultipleScenariosCumulatesCount()
{
    var path = WriteFeatureFile("Features/Auth.feature", """
        Feature: Auth

          Scenario: First login
            Given I am logged in

          Scenario: Second login
            Given I am logged in
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
    };

    var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

    counts["I am logged in"].Should().Be(2);
}

[Fact]
public void MultipleDistinctStepsInOneScenarioEachCountedOnce()
{
    var path = WriteFeatureFile("Features/Auth.feature", """
        Feature: Auth

          Scenario: Login flow
            Given I am on the login page
            When I submit valid credentials
            Then I should be redirected to the dashboard
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am on the login page",                 [], "Auth/AuthSteps.cs", 1,  ""),
        new(StepType.When,  "I submit valid credentials",             [], "Auth/AuthSteps.cs", 5,  ""),
        new(StepType.Then,  "I should be redirected to the dashboard",[], "Auth/AuthSteps.cs", 9,  "")
    };

    var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

    counts["I am on the login page"].Should().Be(1);
    counts["I submit valid credentials"].Should().Be(1);
    counts["I should be redirected to the dashboard"].Should().Be(1);
}
```

- [ ] **Step 2: Run new tests to verify they pass**

```
dotnet test --filter "SameStepUsedInMultipleScenarios|MultipleDistinctSteps" -v minimal
```

Expected: both PASS (no code change needed)

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 54 passing, 0 failing

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs
git commit -m "test: cumulative step counts across multiple scenarios (Story 7, task 4)"
```

---

## Task 5: Unmatched steps produce a warning

**Files:**
- Modify: `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs`

- [ ] **Step 1: Add unmatched-step tests**

Add these tests to `UsageCounterTests`:

```csharp
[Fact]
public void UnmatchedStepLogsWarning()
{
    var logger = new CapturingDocGenLogger();
    var path = WriteFeatureFile("Features/Unknown.feature", """
        Feature: Unknown

          Scenario: Mystery
            Given something nobody has defined
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
    };

    UsageCounter.Count(steps, path, _root, logger);

    logger.WarnMessages.Should().ContainSingle(m => m.Contains("something nobody has defined"));
}

[Fact]
public void MatchedStepDoesNotLogWarning()
{
    var logger = new CapturingDocGenLogger();
    var path = WriteFeatureFile("Features/Auth.feature", """
        Feature: Auth

          Scenario: Login
            Given I am logged in
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
    };

    UsageCounter.Count(steps, path, _root, logger);

    logger.WarnMessages.Should().BeEmpty();
}
```

- [ ] **Step 2: Run new tests to verify they pass**

```
dotnet test --filter "UnmatchedStepLogsWarning|MatchedStepDoesNotLogWarning" -v minimal
```

Expected: both PASS (no code change needed)

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 56 passing, 0 failing

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs
git commit -m "test: unmatched step warning behaviour for UsageCounter (Story 7, task 5)"
```

---

## Task 6: Scenario Outline step counted once + file-not-found

**Files:**
- Modify: `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs`

- [ ] **Step 1: Add Scenario Outline and file-not-found tests**

Add these tests to `UsageCounterTests`:

```csharp
[Fact]
public void ScenarioOutlineStepCountedOnceNotPerExampleRow()
{
    // The outline template step "I am on the shop page" appears once in the AST.
    // It should be counted as 1 regardless of how many Example rows exist.
    var path = WriteFeatureFile("Features/Shop.feature", """
        Feature: Shop

          Scenario Outline: Browse products
            Given I am on the shop page

          Examples:
            | product |
            | apple   |
            | banana  |
            | cherry  |
        """);
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am on the shop page", [], "Shop/ShopSteps.cs", 1, "")
    };

    var counts = UsageCounter.Count(steps, path, _root, NullDocGenLogger.Instance);

    counts["I am on the shop page"].Should().Be(1);
}

[Fact]
public void ThrowsFileNotFoundForMissingFeatureFile()
{
    var missing = "Features/DoesNotExist.feature";
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
    };

    var act = () => UsageCounter.Count(steps, missing, _root, NullDocGenLogger.Instance);

    act.Should().Throw<FileNotFoundException>();
}
```

- [ ] **Step 2: Run new tests to verify they pass**

```
dotnet test --filter "ScenarioOutlineStepCounted|ThrowsFileNotFoundForMissingFeatureFile" -v minimal
```

Expected: both PASS (no code change needed)

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 58 passing, 0 failing

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs
git commit -m "test: scenario outline counting and file-not-found for UsageCounter (Story 7, task 6)"
```

---

## Task 7: Update developer guide

**Files:**
- Modify: `docs/developer-guide.md`

- [ ] **Step 1: Update the story status table**

In `docs/developer-guide.md`, find the story-by-story status table and update two lines:

Change the overview summary from:
```
| 1–5 | ✅ Complete, merged to master, pushed to GitHub |
| 6–15 | ⬜ Not started |
```
to:
```
| 1–7 | ✅ Complete, merged to master, pushed to GitHub |
| 8–15 | ⬜ Not started |
```

Change the Story 7 row from:
```
| 7 | Usage counting | `UsageCounter` (Gherkin) + tests | ⬜ |
```
to:
```
| 7 | Usage counting | `UsageCounter` (Gherkin) + tests | ✅ |
```

- [ ] **Step 2: Update the test count**

Find the line:
```
**Test count:** 33 passing (13 config, 9 discoverer, 11 extractor)
```

Replace with (count will be the actual passing count — run `dotnet test -q` to confirm):
```
**Test count:** 58 passing (13 config, 9 discoverer, 16 extractor, 10 usage-counter)
```

Note: `16 extractor` reflects the addition of tests from Phase 2 debt remediation. Adjust numbers to match actual output from `dotnet test -q`.

- [ ] **Step 3: Update the "What's next" section**

Find:
```
### What's next — Story 7: Feature file usage counting
```

Replace the heading and body with:

```markdown
### What's next — Story 8: Domain assignment

The next story implements `DomainAssigner`. Key points:

- Input: `RawStep[]` (all steps from Stage 3)
- Output: `RawStep[]` with `Domain` field populated (via `with` expression — `RawStep` is immutable)
- Evaluates domain rules from `DocGenConfig.Domains` in declaration order; first match wins
- Each rule's `pattern` is a glob matched against the step's relative `.cs` file path using `Microsoft.Extensions.FileSystemGlobbing`
- Steps matching no rule → `config.FallbackDomain` + `logger.Warn`
```

- [ ] **Step 4: Run the full suite to confirm all tests still pass**

```
dotnet test -q
```

Expected: 58 passing, 0 failing

- [ ] **Step 5: Commit**

```
git add docs/developer-guide.md
git commit -m "docs: mark Story 7 complete, update test count and what's-next (Story 7, task 7)"
```
