# Story 14: Full Test Suite Validation & Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the repository ready for the Story 15 end-to-end smoke test. Fix one remaining correctness gap (Background steps not counted), tighten CLI ergonomics, add XML docs to public APIs, audit the whole suite for cleanliness, and clear the deferred items that are easy enough to fix in passing.

**Architecture:** No new modules. This is a polish + correctness pass focused on items that have real value (debt items flagged with priority ≥ 10) but were deferred opportunistically. Each task is independent and small.

**Tech Stack:** Existing — no new dependencies.

**Prerequisites:** Story 13 (CLI) complete. 126 tests passing.

---

## File structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs` | Modify | Count `Background` step usage (TD-B04) |
| `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs` | Modify | Background test cases |
| `Delta.DocGen/CLI/CliRootCommand.cs` | Modify | `--verbosity` `FromAmong` validation (TD-D16) |
| `Delta.DocGen.Tests/CLI/RootCommandTests.cs` | Modify | Reject invalid `--verbosity`; pin unknown-option behaviour (TD-D16, TD-D23) |
| `Delta.DocGen/Pipeline/PipelineRunner.cs` | Modify | XML doc on `Run` (TD-D09) |
| `Delta.DocGen/Pipeline/DomainAssigner.cs` | Modify | XML doc explaining "first match wins" (TD-C18) |
| `Delta.DocGen.Tests/Logging/CapturingDocGenLogger.cs` | Modify | Add `Clear()` method (TD-A14) |
| `Delta.DocGen.Tests/CLI/CliRunnerTests.cs` | Modify | Strengthen `AdditionalExcludesAreApplied` to verify exclude actually took effect (TD-D17) |
| `docs/developer-guide.md` | Modify | Mark Story 14 ✅; final test count; What's next → Story 15 |

---

## Task 1: Count Background steps (TD-B04)

**Files:**
- Modify: `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs`
- Modify: `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs`

The Gherkin AST puts `Background` as a sibling of `Scenario` inside `Feature.Children` (and inside `Rule.Children`). The current loop only counts step usage in `Scenario` nodes — steps used in a `Background` show count 0 despite running before every scenario.

- [ ] **Step 1: Read `UsageCounter.cs:44-58` to see the current loop structure**

The current shape is (approximately):
```csharp
if (doc.Feature is { } feature)
{
    foreach (var child in feature.Children)
    {
        if (child is Scenario scenario)
            MatchScenario(scenario, counts, regexes, relativePath, logger);
        else if (child is Rule rule)
            foreach (var ruleChild in rule.Children)
                if (ruleChild is Scenario rs)
                    MatchScenario(rs, counts, regexes, relativePath, logger);
    }
}
```

- [ ] **Step 2: Extract `MatchScenario`'s step-walking logic into a helper if not already**

The existing code already iterates `scenario.Steps`. Add a sibling helper:

```csharp
private static void MatchBackground(
    Background background,
    Dictionary<string, int> counts,
    IReadOnlyDictionary<string, Regex> regexes,
    string relativePath,
    IDocGenLogger logger)
{
    foreach (var step in background.Steps)
        MatchStep(step, counts, regexes, relativePath, logger);
}
```

If `MatchScenario` does the step-walking inline, refactor the per-step matching into a `MatchStep(Step, …)` method that both helpers call. (Look at the existing code to decide the smallest reasonable shape.)

- [ ] **Step 3: Add Background arms to both loops**

```csharp
foreach (var child in feature.Children)
{
    switch (child)
    {
        case Scenario scenario:
            MatchScenario(scenario, counts, regexes, relativePath, logger);
            break;
        case Background background:
            MatchBackground(background, counts, regexes, relativePath, logger);
            break;
        case Rule rule:
            foreach (var ruleChild in rule.Children)
            {
                switch (ruleChild)
                {
                    case Scenario rs:
                        MatchScenario(rs, counts, regexes, relativePath, logger);
                        break;
                    case Background rbg:
                        MatchBackground(rbg, counts, regexes, relativePath, logger);
                        break;
                }
            }
            break;
    }
}
```

Add `using Gherkin.Ast;` if not already imported (for `Background`).

- [ ] **Step 4: Add tests**

```csharp
[Fact]
public void BackgroundStepIsCountedOncePerScenario()
{
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
    };
    var feature = """
        Feature: Test
          Background:
            Given I am logged in

          Scenario: First
            When I do something

          Scenario: Second
            When I do something
        """;
    var path = WriteFeatureFile("background.feature", feature);

    var counts = UsageCounter.Count(steps, "background.feature", _root, NullDocGenLogger.Instance);

    // Background runs before each scenario, but the Gherkin AST only stores it once.
    // Spec decision: count Background step occurrences as written (1), not multiplied.
    counts["I am logged in"].Should().Be(1);
}

[Fact]
public void BackgroundInsideRuleIsCounted()
{
    var steps = new List<RawStep>
    {
        new(StepType.Given, "I am logged in", [], "Auth/AuthSteps.cs", 1, "")
    };
    var feature = """
        Feature: Test
          Rule: AuthRule
            Background:
              Given I am logged in

            Scenario: Sample
              When I do something
        """;
    var path = WriteFeatureFile("rule-background.feature", feature);

    var counts = UsageCounter.Count(steps, "rule-background.feature", _root, NullDocGenLogger.Instance);

    counts["I am logged in"].Should().Be(1);
}
```

Use whichever helper name the existing test class already exposes for writing feature files (`WriteFeatureFile` or similar — read the file first to confirm).

- [ ] **Step 5: Build and test**

```
dotnet build --no-incremental
dotnet test --no-build --filter "Background" -q
dotnet test --no-build -q
```

Expected: 128 passing (126 + 2). Both new tests PASS.

- [ ] **Step 6: Commit**

```
git add Delta.DocGen/Scanner/Gherkin/UsageCounter.cs Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs
git commit -m "fix(UsageCounter): count Background steps (incl. inside Rule blocks) (TD-B04)"
```

---

## Task 2: `--verbosity` value validation + unknown-option pinning

**Files:**
- Modify: `Delta.DocGen/CLI/CliRootCommand.cs`
- Modify: `Delta.DocGen.Tests/CLI/RootCommandTests.cs`

- [ ] **Step 1: Restrict `--verbosity` to known values (TD-D16)**

In `CliRootCommand.Build`, change the verbosity option:

```csharp
var verbosityOption = new Option<string?>(
    aliases: ["--verbosity"],
    description: "silent | normal | verbose (default: normal)");
verbosityOption.FromAmong(LogVerbosity.Silent, LogVerbosity.Normal, LogVerbosity.Verbose);
```

Add `using Delta.DocGen.Logging;` if not already present.

If `FromAmong` doesn't exist as an extension in this `System.CommandLine` beta, use:
```csharp
verbosityOption.AddValidator(result =>
{
    var v = result.GetValueForOption(verbosityOption);
    if (v is not null && v is not (LogVerbosity.Silent or LogVerbosity.Normal or LogVerbosity.Verbose))
        result.ErrorMessage = $"Invalid verbosity '{v}'. Expected: silent | normal | verbose.";
});
```

- [ ] **Step 2: Add tests**

```csharp
[Fact]
public void InvalidVerbosityValueFailsParsing()
{
    var handlerCalled = false;
    var cmd = CliRootCommand.Build(_ => { handlerCalled = true; return 0; });

    var exitCode = cmd.Invoke(["--verbosity", "loud"]);

    exitCode.Should().NotBe(0);
    handlerCalled.Should().BeFalse();
}

[Fact]
public void UnknownOptionFailsParsing()
{
    // TD-D23: pin behaviour for unrecognised flags.
    var handlerCalled = false;
    var cmd = CliRootCommand.Build(_ => { handlerCalled = true; return 0; });

    var exitCode = cmd.Invoke(["--frobnicate"]);

    exitCode.Should().NotBe(0);
    handlerCalled.Should().BeFalse();
}
```

- [ ] **Step 3: Build and test**

```
dotnet build --no-incremental
dotnet test --no-build --filter "InvalidVerbosity|UnknownOption" -q
dotnet test --no-build -q
```

Expected: 130 passing (128 + 2).

- [ ] **Step 4: Commit**

```
git add Delta.DocGen/CLI/CliRootCommand.cs Delta.DocGen.Tests/CLI/RootCommandTests.cs
git commit -m "feat(CLI): validate --verbosity values; pin unknown-option behaviour (TD-D16, TD-D23)"
```

---

## Task 3: XML docs on public static APIs

**Files:**
- Modify: `Delta.DocGen/Pipeline/PipelineRunner.cs`
- Modify: `Delta.DocGen/Pipeline/DomainAssigner.cs`

- [ ] **Step 1: Add an XML doc comment to `PipelineRunner.Run` (TD-D09)**

Place above the `Run` method:

```csharp
/// <summary>
/// Runs pipeline stages 2–8 against a fully-resolved <see cref="DocGenConfig"/> and
/// returns a <see cref="PipelineResult"/>. Stage 1 (config load) is the caller's
/// responsibility — see <see cref="Config.ConfigLoader.Load"/>.
/// </summary>
/// <param name="config">Resolved configuration with absolute <c>Root</c> and <c>Output</c> paths.</param>
/// <param name="logger">Logger used by every stage; warning messages matching
/// <see cref="Logging.LogPhrases.UnmatchedStep"/> contribute to
/// <see cref="PipelineResult.UnmatchedStepCount"/>.</param>
/// <param name="dryRun">When <c>true</c>, runs every stage but does not write the
/// envelope or schema files.</param>
/// <param name="clock">Optional clock for deterministic <c>generatedAt</c> timestamps in tests.
/// Defaults to <see cref="DateTime.UtcNow"/>.</param>
/// <returns>A <see cref="PipelineResult"/>. On failure, <see cref="PipelineResult.Success"/> is
/// <c>false</c> and <see cref="PipelineResult.FailureCategory"/> indicates whether the cause was
/// user input (<see cref="FailureCategory.UserError"/>) or an internal invariant
/// (<see cref="FailureCategory.InternalError"/>).</returns>
```

- [ ] **Step 2: Add an XML doc on `DomainAssigner` (TD-C18)**

Place above the `public static class DomainAssigner`:

```csharp
/// <summary>
/// Assigns each <see cref="Model.RawStep"/> a <c>Domain</c> by matching its <c>File</c> path
/// against a list of glob rules. Evaluation is <b>first-match-wins</b> in declaration order;
/// steps matching no rule receive the supplied fallback domain and a Warn.
/// </summary>
```

- [ ] **Step 3: Build (XML docs don't change behaviour; verify no warnings)**

```
dotnet build --no-incremental
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Run full suite (sanity)**

```
dotnet test --no-build -q
```

Expected: still 130 passing.

- [ ] **Step 5: Commit**

```
git add Delta.DocGen/Pipeline/PipelineRunner.cs Delta.DocGen/Pipeline/DomainAssigner.cs
git commit -m "docs: XML doc comments on PipelineRunner.Run and DomainAssigner (TD-D09, TD-C18)"
```

---

## Task 4: `CapturingDocGenLogger.Clear()` + strengthen exclude test

**Files:**
- Modify: `Delta.DocGen.Tests/Logging/CapturingDocGenLogger.cs`
- Modify: `Delta.DocGen.Tests/CLI/CliRunnerTests.cs`

- [ ] **Step 1: Read `CapturingDocGenLogger` to confirm the message lists**

It exposes `InfoMessages`, `VerboseMessages`, `WarnMessages`, `ErrorMessages`, `SummaryMessages` — all `List<string>`.

- [ ] **Step 2: Add a `Clear()` method (TD-A14)**

Inside the class, add:

```csharp
public void Clear()
{
    InfoMessages.Clear();
    VerboseMessages.Clear();
    WarnMessages.Clear();
    ErrorMessages.Clear();
    SummaryMessages.Clear();
}
```

- [ ] **Step 3: Strengthen `AdditionalExcludesAreAppliedOnTopOfConfig` (TD-D17)**

Read the current test in `CliRunnerTests.cs`. It currently only asserts exit code 0. Change it to verify the exclude actually dropped some content. Approach: run once without excludes (capture step count via the output JSON), then run again with an exclude that removes the Forms domain, and assert the step count dropped.

Replace the test body with:

```csharp
[Fact]
public void AdditionalExcludesActuallyReduceStepCount()
{
    // Baseline: run with no extra excludes — expect 3 steps (Auth Given/When + Forms Then).
    var baseline = CliRunner.Run(new CliArgs(
        ConfigPath: _configPath,
        Root: null, Output: null, Excludes: [],
        Verbosity: "silent", DryRun: false));
    baseline.Should().Be(0);
    var baselineJson = JsonDocument.Parse(File.ReadAllText(_output));
    var baselineStepCount = baselineJson.RootElement.GetProperty("steps").GetArrayLength();

    File.Delete(_output);  // clean slate for the second run

    // Excluded run: drop the Forms directory via --exclude. Expect 1 fewer step.
    var excluded = CliRunner.Run(new CliArgs(
        ConfigPath: _configPath,
        Root: null, Output: null, Excludes: ["**/Forms/**"],
        Verbosity: "silent", DryRun: false));
    excluded.Should().Be(0);
    var excludedJson = JsonDocument.Parse(File.ReadAllText(_output));
    var excludedStepCount = excludedJson.RootElement.GetProperty("steps").GetArrayLength();

    excludedStepCount.Should().BeLessThan(baselineStepCount);
}
```

Add `using System.Text.Json;` to the test file if it isn't already present (the existing test class already serialises config via `JsonSerializer` so it likely is).

Drop the old `AdditionalExcludesAreAppliedOnTopOfConfig` test — the new one supersedes it.

- [ ] **Step 4: Build and test**

```
dotnet build --no-incremental
dotnet test --no-build -q
```

Expected: 130 passing (one removed, one added — net same). If 131, the count was 130+1 from new tests — verify.

- [ ] **Step 5: Commit**

```
git add Delta.DocGen.Tests/Logging/CapturingDocGenLogger.cs Delta.DocGen.Tests/CLI/CliRunnerTests.cs
git commit -m "test: CapturingDocGenLogger.Clear(); strengthen exclude test to verify step count drops (TD-A14, TD-D17)"
```

---

## Task 5: Final audit + developer guide

**Files:**
- Modify: `docs/developer-guide.md`
- Modify: `docs/tech-debt.md` (mark items resolved)

- [ ] **Step 1: Final clean-build audit**

```
dotnet build --no-incremental
```

Expected: 0 warnings, 0 errors across BOTH projects (`Delta.DocGen` and `Delta.DocGen.Tests`).

If any warning appears (TreatWarningsAsErrors should have failed already, but verify) investigate and fix as a separate commit.

- [ ] **Step 2: Final clean-test audit**

```
dotnet test -q
```

Expected: 130 passing, 0 failed, 0 skipped.

- [ ] **Step 3: Smoke test the built binary end-to-end**

```
dotnet run --project Delta.DocGen --no-build -- --help
dotnet run --project Delta.DocGen --no-build -- --version
```

`--help` lists all six options + `--version`. `--version` prints `1.0.0` and exits 0. If either misbehaves, that's a blocker — fix before proceeding.

- [ ] **Step 4: Update `docs/tech-debt.md` to mark resolved items**

Append to the existing register a short table noting:
- TD-B04 ✅ — Background step counting added
- TD-A14 ✅ — `Clear()` method added
- TD-D09 ✅ — XML doc on `PipelineRunner.Run`
- TD-C18 ✅ — XML doc on `DomainAssigner`
- TD-D16 ✅ — `--verbosity` value validation
- TD-D17 ✅ — Exclude test now verifies step-count drop
- TD-D23 ✅ — Unknown-option behaviour pinned

Also update the summary table accordingly.

- [ ] **Step 5: Update developer guide §9**

- Change `| 14 | Full test suite | All tests green, zero warnings | ⬜ |` → `✅`
- Change overview status from `| 1–13 | ✅ Complete |` / `| 14–15 | ⬜ Not started |` to:
  ```
  | 1–14 | ✅ Complete, merged to master, pushed to GitHub |
  | 15   | ⬜ Not started |
  ```
- Update the test count line to `**Test count:** 130 passing (Stories 1–14 + TD debt fixes)`
- Replace the Story 14 "What's next" with Story 15:

  ```markdown
  ### What's next — Story 15: End-to-end smoke test

  The final story validates the tool against a real (small) Reqnroll project:

  - Create a fixture under `Delta.DocGen.Tests/EndToEnd/Fixtures/` with a representative project (2–4 step classes, 2–3 feature files, mixed Given/When/Then/Background)
  - Run the full CLI (`CliRunner.Run`) against the fixture
  - Assert the produced JSON validates against the embedded schema (re-use `SchemaValidationTests` patterns)
  - Re-canonicalise the envelope (strip signature) and confirm the SHA-256 matches `signature.digest`
  - Assert specific step IDs, usage counts, and domain assignments match expectations
  ```

- [ ] **Step 6: Final commit**

```
git add docs/tech-debt.md docs/developer-guide.md
git commit -m "docs: mark Story 14 complete + clear TD-B04, A14, D09, C18, D16, D17, D23 (Story 14, task 5)"
```
