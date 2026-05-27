# Story 12: PipelineRunner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `PipelineRunner.Run(config, logger, dryRun)`, which orchestrates pipeline stages 2–8 end-to-end and returns a `PipelineResult` summarising the outcome. Stage 1 (config loading) happens before the runner is invoked — the runner receives a fully-resolved `DocGenConfig`.

**Architecture:** A single static `Run` method composes the existing stages in the order defined by §6 of the developer guide. The runner does not own per-stage logging beyond a top-level "starting" / "complete" summary — each stage already logs its own summary. The runner catches `Exception` to convert fatal failures (e.g. ID collisions) into a failed `PipelineResult` with the error message attached, rather than letting them escape to the CLI; the original exception is also logged via `logger.Error`. A `dryRun` flag short-circuits the two `Write` calls (envelope JSON + schema) — everything else runs.

**Tech Stack:** .NET 8, `System.Diagnostics.Stopwatch` for elapsed-time measurement, xUnit 2.9.3, FluentAssertions 6.12.0.

**Prerequisite:** Story 11 (SchemaWriter) complete.

---

## File structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Delta.DocGen/Pipeline/PipelineResult.cs` | Create | Record summarising the outcome of a pipeline run |
| `Delta.DocGen/Pipeline/PipelineRunner.cs` | Create | Static orchestrator that composes stages 2–8 |
| `Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs` | Create | End-to-end tests using real temp-dir fixtures |
| `Delta.DocGen.Tests/Pipeline/Fixtures/` | Create | Helper `WriteFixture(...)` that builds a representative `.cs` + `.feature` tree under a temp dir |
| `docs/developer-guide.md` | Modify | Mark Story 12 ✅; update test count; update "What's next" |

---

## Key existing types (do NOT modify)

```csharp
// DocGenConfig — resolved at Stage 1 with absolute paths
public sealed record DocGenConfig(
    string Root,
    string Output,
    IReadOnlyList<string> Excludes,
    string LogVerbosity,
    string FallbackDomain,
    IReadOnlyList<DomainRule> Domains);

// Envelope — to be built by the runner
public sealed record Envelope(
    string Schema, string Version, string GeneratedAt, string GeneratorVersion,
    bool Enriched, IReadOnlyList<DomainRecord> Domains,
    IReadOnlyList<StepRecord> Steps, SignatureRecord? Signature);
```

Pipeline stage entry points (already implemented):

| Stage | Call |
|-------|------|
| 2 | `Discoverer.Discover(root, excludes, logger)` → `DiscoveryResult` |
| 3 | `StepDefinitionExtractor.Extract(filePath, logger)` (called per file) |
| 4 | `UsageCounter.Count(featureFiles, patterns, logger)` |
| 5 | `DomainAssigner.Assign(steps, rules, fallbackDomain, logger)` |
| 6 | `IdGenerator.AssignIds(steps, usageCounts, logger)` + `DomainBuilder.Build(steps, rules)` |
| 7 | `CanonicalJson.Serialise(envelope)` + `Signer.Sign(envelope)` |
| 8a | `CanonicalJson.Write(envelope, outputPath)` |
| 8b | `SchemaWriter.Write(outputDir, logger)` |

---

## Task 1: Create PipelineResult record

**Files:**
- Create: `Delta.DocGen/Pipeline/PipelineResult.cs`

- [ ] **Step 1: Create the record**

```csharp
namespace Delta.DocGen.Pipeline;

public sealed record PipelineResult(
    bool                Success,
    int                 StepCount,
    int                 DomainCount,
    int                 CsFileCount,
    int                 FeatureFileCount,
    int                 UnmatchedStepCount,
    string?             OutputPath,
    string?             SchemaPath,
    string?             Digest,
    long                ElapsedMs,
    string?             ErrorMessage);
```

- [ ] **Step 2: Build to confirm it compiles**

```
dotnet build --no-incremental
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```
git add Delta.DocGen/Pipeline/PipelineResult.cs
git commit -m "feat: PipelineResult record summarising pipeline outcomes (Story 12, task 1)"
```

---

## Task 2: Create PipelineRunner happy-path scaffold

**Files:**
- Create: `Delta.DocGen/Pipeline/PipelineRunner.cs`
- Create: `Delta.DocGen.Tests/Pipeline/Fixtures/PipelineFixture.cs`
- Create: `Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs`

- [ ] **Step 1: Create the fixture helper**

```csharp
// Delta.DocGen.Tests/Pipeline/Fixtures/PipelineFixture.cs
namespace Delta.DocGen.Tests.Pipeline.Fixtures;

public static class PipelineFixture
{
    /// <summary>
    /// Writes a small but realistic fixture tree under <paramref name="root"/>:
    /// - Auth/AuthSteps.cs       (1 [Given], 1 [When])
    /// - Forms/FormSteps.cs      (1 [Then])
    /// - Features/auth.feature   (uses the Given and Then twice each)
    /// </summary>
    public static void WriteFixture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Auth"));
        Directory.CreateDirectory(Path.Combine(root, "Forms"));
        Directory.CreateDirectory(Path.Combine(root, "Features"));

        File.WriteAllText(Path.Combine(root, "Auth", "AuthSteps.cs"), """
            using Reqnroll;
            namespace Demo;
            public class AuthSteps
            {
                [Given("I am logged in")]
                public void GivenLoggedIn() { }

                [When("I sign out")]
                public void WhenSignOut() { }
            }
            """);

        File.WriteAllText(Path.Combine(root, "Forms", "FormSteps.cs"), """
            using Reqnroll;
            namespace Demo;
            public class FormSteps
            {
                [Then("the form is submitted")]
                public void ThenFormSubmitted() { }
            }
            """);

        File.WriteAllText(Path.Combine(root, "Features", "auth.feature"), """
            Feature: Auth
              Scenario: First
                Given I am logged in
                Then the form is submitted

              Scenario: Second
                Given I am logged in
                Then the form is submitted
            """);
    }
}
```

- [ ] **Step 2: Create the runner stub**

```csharp
// Delta.DocGen/Pipeline/PipelineRunner.cs
using System.Diagnostics;
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Output.Schema;
using Delta.DocGen.Output.Serialiser;
using Delta.DocGen.Scanner.CSharp;
using Delta.DocGen.Scanner.Gherkin;

namespace Delta.DocGen.Pipeline;

public static class PipelineRunner
{
    private const string SchemaRelativeRef = "./schema/v1/step-library.schema.json";
    private const string EnvelopeVersion   = "1.0.0";
    private const string GeneratorVersion  = "1.0.0";

    public static PipelineResult Run(DocGenConfig config, IDocGenLogger logger, bool dryRun = false)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Stage 2: discovery
            var discovery = Discoverer.Discover(config.Root, config.Excludes, logger);

            // Stage 3: C# extraction (one file at a time)
            var rawSteps = new List<RawStep>();
            foreach (var csFile in discovery.CsFiles)
                rawSteps.AddRange(
                    StepDefinitionExtractor.Extract(Path.Combine(config.Root, csFile), logger));

            // Stage 4: usage counting
            var patterns = rawSteps.Select(s => s.Pattern).Distinct().ToList();
            var featureFiles = discovery.FeatureFiles
                .Select(f => Path.Combine(config.Root, f)).ToList();
            var usageCounts = UsageCounter.Count(featureFiles, patterns, logger);

            // Stage 5: domain assignment
            var domainAssigned = DomainAssigner.Assign(
                rawSteps, config.Domains, config.FallbackDomain, logger);

            // Stage 6: id generation + domain records
            var stepRecords = IdGenerator.AssignIds(domainAssigned, usageCounts, logger);
            var domainRecords = DomainBuilder.Build(domainAssigned, config.Domains);

            // Stage 7: build + sign envelope
            var envelope = new Envelope(
                Schema:           SchemaRelativeRef,
                Version:          EnvelopeVersion,
                GeneratedAt:      DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                GeneratorVersion: GeneratorVersion,
                Enriched:         false,
                Domains:          domainRecords,
                Steps:            stepRecords,
                Signature:        null);
            var signed = Signer.Sign(envelope);

            // Stage 8: file output
            string? outputPath = null;
            string? schemaPath = null;
            if (!dryRun)
            {
                CanonicalJson.Write(signed, config.Output);
                outputPath = config.Output;
                var outputDir = Path.GetDirectoryName(config.Output)
                    ?? throw new InvalidOperationException(
                        $"Cannot determine output directory from '{config.Output}'.");
                schemaPath = SchemaWriter.Write(outputDir, logger);
            }

            stopwatch.Stop();
            var result = new PipelineResult(
                Success:            true,
                StepCount:          stepRecords.Count,
                DomainCount:        domainRecords.Count,
                CsFileCount:        discovery.CsFiles.Count,
                FeatureFileCount:   discovery.FeatureFiles.Count,
                UnmatchedStepCount: 0,  // populated in Task 4 below
                OutputPath:         outputPath,
                SchemaPath:         schemaPath,
                Digest:             signed.Signature?.Digest,
                ElapsedMs:          stopwatch.ElapsedMilliseconds,
                ErrorMessage:       null);

            logger.Summary(
                $"Pipeline complete: {result.StepCount} step(s), {result.DomainCount} domain(s), " +
                $"{result.CsFileCount} C# file(s), {result.FeatureFileCount} feature file(s), " +
                $"elapsed {result.ElapsedMs}ms.");
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.Error($"Pipeline failed: {ex.Message}");
            return new PipelineResult(
                Success: false, StepCount: 0, DomainCount: 0, CsFileCount: 0, FeatureFileCount: 0,
                UnmatchedStepCount: 0, OutputPath: null, SchemaPath: null, Digest: null,
                ElapsedMs: stopwatch.ElapsedMilliseconds, ErrorMessage: ex.Message);
        }
    }
}
```

- [ ] **Step 3: Write the first happy-path test**

```csharp
// Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Pipeline;
using Delta.DocGen.Tests.Logging;
using Delta.DocGen.Tests.Pipeline.Fixtures;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class PipelineRunnerTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _root;
    private readonly string _output;

    public PipelineRunnerTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _root      = Path.Combine(_workspace, "src");
        _output    = Path.Combine(_workspace, "dist", "step-library.json");
        Directory.CreateDirectory(_root);
        PipelineFixture.WriteFixture(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private DocGenConfig BuildConfig() => new(
        Root:           _root,
        Output:         _output,
        Excludes:       [],
        LogVerbosity:   "normal",
        FallbackDomain: "General",
        Domains:        [
            new("Auth/**",  "Auth",  "Auth & Identity"),
            new("Forms/**", "Forms", "Forms & Input"),
        ]);

    [Fact]
    public void RunFixtureProducesSuccessResultWithExpectedCounts()
    {
        var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

        result.Success.Should().BeTrue();
        result.StepCount.Should().Be(3);          // 1 Given + 1 When + 1 Then
        result.DomainCount.Should().Be(2);        // Auth, Forms
        result.CsFileCount.Should().Be(2);
        result.FeatureFileCount.Should().Be(1);
        result.OutputPath.Should().Be(_output);
        result.SchemaPath.Should().NotBeNullOrEmpty();
        result.Digest.Should().MatchRegex("^[0-9a-f]{64}$");
        result.ElapsedMs.Should().BeGreaterOrEqualTo(0);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void RunWritesEnvelopeAndSchemaFilesToDisk()
    {
        PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

        File.Exists(_output).Should().BeTrue();
        var schemaPath = Path.Combine(Path.GetDirectoryName(_output)!, "schema", "v1", "step-library.schema.json");
        File.Exists(schemaPath).Should().BeTrue();
    }
}
```

- [ ] **Step 4: Run new tests**

```
dotnet test --filter "PipelineRunnerTests" -q
```

Expected: 2 passing.

- [ ] **Step 5: Run full suite**

```
dotnet test -q
```

Expected: 104 passing (102 baseline + 2 new).

- [ ] **Step 6: Commit**

```
git add Delta.DocGen/Pipeline/PipelineRunner.cs Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs Delta.DocGen.Tests/Pipeline/Fixtures/PipelineFixture.cs
git commit -m "feat: PipelineRunner scaffold with end-to-end fixture test (Story 12, task 2)"
```

---

## Task 3: Dry-run mode skips file output

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs`

- [ ] **Step 1: Add tests**

```csharp
[Fact]
public void DryRunReturnsSuccessButWritesNoFiles()
{
    var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance, dryRun: true);

    result.Success.Should().BeTrue();
    result.StepCount.Should().Be(3);
    result.Digest.Should().MatchRegex("^[0-9a-f]{64}$");
    result.OutputPath.Should().BeNull();
    result.SchemaPath.Should().BeNull();
    File.Exists(_output).Should().BeFalse();
}

[Fact]
public void DryRunStillComputesDigest()
{
    // Same input run twice (once dry, once wet) should produce the same digest.
    var dry = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance, dryRun: true);
    var wet = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance, dryRun: false);

    dry.Digest.Should().Be(wet.Digest);
}
```

- [ ] **Step 2: Run new tests**

```
dotnet test --filter "DryRun" -q
```

Expected: 2 passing. Note: `DryRunStillComputesDigest` depends on `generatedAt` being identical between runs — but the runner uses `DateTime.UtcNow`. The signature is computed over the canonical form *including* `generatedAt`, so if the two runs cross a second boundary the digests differ.

If the test is flaky for that reason, freeze the clock: introduce an internal `Func<DateTime>? clock` parameter on `Run` defaulting to `() => DateTime.UtcNow`, and have the test pass a fixed timestamp. Adjust the test accordingly.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 106 passing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs Delta.DocGen/Pipeline/PipelineRunner.cs
git commit -m "test: dry-run skips file output (Story 12, task 3)"
```

---

## Task 4: Unmatched step count populated from logger

**Files:**
- Modify: `Delta.DocGen/Pipeline/PipelineRunner.cs`
- Modify: `Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs`

Background: `UsageCounter` already logs a Warn per unmatched step. Rather than thread a count through every stage, the runner can intercept warnings via a `CountingDocGenLogger` wrapper that delegates to the caller's logger while counting warnings matching a specific prefix. Simpler: ask `UsageCounter` to return both the dictionary and an unmatched count. Since modifying `UsageCounter`'s signature touches Story 7, prefer the wrapper approach.

- [ ] **Step 1: Add a CountingLogger wrapper inside `PipelineRunner.cs`**

Add a private `sealed class CountingLogger : IDocGenLogger` inside `PipelineRunner` (or as a file-private class) that wraps an inner logger, forwards everything, and increments a counter every time `Warn` is called with a message containing `"matched no step pattern"` (or whatever phrase `UsageCounter` uses — verify by reading the source first).

Read `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs` to find the exact phrase logged for unmatched steps, then write:

```csharp
private sealed class UnmatchedCountingLogger : IDocGenLogger
{
    private readonly IDocGenLogger _inner;
    private readonly string _matchPhrase;
    public int Count { get; private set; }

    public UnmatchedCountingLogger(IDocGenLogger inner, string matchPhrase)
    {
        _inner = inner;
        _matchPhrase = matchPhrase;
    }

    public void Info(string m)    => _inner.Info(m);
    public void Verbose(string m) => _inner.Verbose(m);
    public void Warn(string m)
    {
        if (m.Contains(_matchPhrase, StringComparison.Ordinal)) Count++;
        _inner.Warn(m);
    }
    public void Error(string m)   => _inner.Error(m);
    public void Summary(string m) => _inner.Summary(m);
}
```

Wire it into `Run`: build the wrapper once (`var counter = new UnmatchedCountingLogger(logger, "<phrase>");`), pass it to `UsageCounter.Count(...)`, and put `counter.Count` into the `PipelineResult.UnmatchedStepCount` field.

- [ ] **Step 2: Add test**

```csharp
[Fact]
public void UnmatchedFeatureStepsAreCountedInResult()
{
    // Add a feature file with a step that no [Given]/[When]/[Then] matches.
    File.WriteAllText(Path.Combine(_root, "Features", "extra.feature"), """
        Feature: Extra
          Scenario: Mystery
            Given I do something nobody coded
        """);

    var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

    result.UnmatchedStepCount.Should().BeGreaterOrEqualTo(1);
}
```

- [ ] **Step 3: Run tests**

```
dotnet test -q
```

Expected: 107 passing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen/Pipeline/PipelineRunner.cs Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs
git commit -m "feat: PipelineRunner counts unmatched feature steps via logger wrapper (Story 12, task 4)"
```

---

## Task 5: Fatal pipeline failure converted to PipelineResult

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs`

- [ ] **Step 1: Add tests**

```csharp
[Fact]
public void PipelineCatchesIdCollisionAndReturnsFailedResult()
{
    // Two identical [Given] attributes in different files — ID collision.
    File.WriteAllText(Path.Combine(_root, "Auth", "DuplicateSteps.cs"), """
        using Reqnroll;
        namespace Demo;
        public class DuplicateSteps
        {
            [Given("I am logged in")]
            public void GivenDuplicate() { }
        }
        """);

    var logger = new CapturingDocGenLogger();
    var result = PipelineRunner.Run(BuildConfig(), logger);

    result.Success.Should().BeFalse();
    result.ErrorMessage.Should().NotBeNullOrEmpty();
    result.ErrorMessage.Should().Contain("collision");
    logger.ErrorMessages.Should().ContainSingle(m => m.Contains("collision"));
    File.Exists(_output).Should().BeFalse();
}

[Fact]
public void RunOnNonExistentRootDirectoryReturnsFailedResult()
{
    var config = BuildConfig() with { Root = Path.Combine(_workspace, "does-not-exist") };

    var result = PipelineRunner.Run(config, NullDocGenLogger.Instance);

    result.Success.Should().BeFalse();
    result.ErrorMessage.Should().NotBeNullOrEmpty();
}
```

- [ ] **Step 2: Run new tests**

```
dotnet test --filter "PipelineCatchesIdCollision|RunOnNonExistentRoot" -q
```

Expected: both PASS.

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 109 passing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs
git commit -m "test: PipelineRunner converts fatal stage failures to failed results (Story 12, task 5)"
```

---

## Task 6: Summary log content

**Files:**
- Modify: `Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs`

- [ ] **Step 1: Add test**

```csharp
[Fact]
public void RunEmitsSummaryLogWithKeyCounts()
{
    var logger = new CapturingDocGenLogger();

    PipelineRunner.Run(BuildConfig(), logger);

    logger.SummaryMessages.Should().ContainSingle(m =>
        m.Contains("3 step") && m.Contains("2 domain") && m.Contains("2 C#") && m.Contains("1 feature"));
}
```

- [ ] **Step 2: Run new test**

```
dotnet test --filter "RunEmitsSummaryLog" -q
```

Expected: PASS (the runner's `logger.Summary(...)` call from Task 2 already produces the right shape).

- [ ] **Step 3: Run full suite**

```
dotnet test -q
```

Expected: 110 passing.

- [ ] **Step 4: Commit**

```
git add Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs
git commit -m "test: PipelineRunner summary log includes step, domain, file counts (Story 12, task 6)"
```

---

## Task 7: Update developer guide

**Files:**
- Modify: `docs/developer-guide.md`

- [ ] **Step 1: Mark Story 12 complete**

Change `| 12 | Pipeline runner | … | ⬜ |` → `✅`.

- [ ] **Step 2: Update overview status range**

```
| 1–12 | ✅ Complete, merged to master, pushed to GitHub |
| 13–15 | ⬜ Not started |
```

- [ ] **Step 3: Update test count**

Confirm via `dotnet test -q`; update to:
```
**Test count:** 110 passing (Stories 1–12 + TD-C01..C10 debt fixes)
```

- [ ] **Step 4: Update "What's next" section**

Replace with:

```markdown
### What's next — Story 13: CLI wiring

The next story wires the CLI:

- `Delta.DocGen/CLI/RootCommand.cs` builds a `System.CommandLine` root command with options matching the spec: `--config`, `--root`, `--output`, `--exclude` (repeatable), `--verbosity`, `--dry-run`
- `Program.cs` parses args, calls `ConfigLoader.Load(...)` with the resolved overrides, instantiates a `ConsoleLogger`, calls `PipelineRunner.Run(...)`, and exits with code 0 on success, 1 on failure
- The CLI does NOT itself contain pipeline logic — it is a thin adapter
```

- [ ] **Step 5: Run full suite to confirm**

```
dotnet test -q
```

- [ ] **Step 6: Commit**

```
git add docs/developer-guide.md
git commit -m "docs: mark Story 12 complete, update test count and what's-next (Story 12, task 7)"
```
