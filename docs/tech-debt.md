# Delta.DocGen — Technical Debt Register

**Date:** 2026-05-27 (updated after TD-A01 + TD-A02 resolved)  
**Scope:** Stories 1–6 + Phase 1–3 fixes + pre-Story-7 blockers  
**Formula:** Priority = (Impact + Risk) × (6 − Effort) — higher = more urgent

---

## What changed since the last register

**Phase 1–3 (21 items) fully resolved:**

| ID | Resolution |
|----|-----------|
| TD-01 | ✅ `LogVerbosity` constants class created; `ConfigLoader` + `ConsoleLogger` use them |
| TD-02 | ✅ `.ToLowerInvariant()` added to verbosity validation in `ConfigLoader` |
| TD-05 | ✅ Thread-safety comment added to `ConsoleLogger` |
| TD-07 | ✅ `Domain = ""` added to `RawStep`; doc comment updated |
| TD-09 | ✅ `Envelope.GeneratedAt` doc comment added with Z-suffix and format guidance |
| TD-10 | ✅ `StepType` enum created; `RawStep.Type` and `StepRecord.Type` now `StepType` |
| TD-11 | ✅ `ParamTypes` constants class created; extractor uses them |
| TD-12 | ✅ Explicit `Table`/`DataTable`/`ScenarioContext` arms added; `Warn` on unknown types |
| TD-13 | ✅ `"StepDefinition"` added to `StepAttributeNames` + `StepType.StepDefinition` enum value |
| TD-15 | ✅ `RawStep.Source` doc comment corrected: "full method text (attribute lists + signature + body)" |
| TD-16 | ✅ `ExtractParams` rewritten with explicit `string` arm and `logger` parameter; `Table` arms don't consume placeholder index |
| TD-18 | ✅ Discoverer glob comment explains why both `*.cs` and `**/*.cs` are needed |
| TD-19 | ✅ Story 11 note added to module responsibilities table |
| TD-20 | ✅ `System.CommandLine` note updated with version, `SYSLIB0050`, Story 13 |
| TD-23 | ✅ Unix paths replaced with `Path.Combine(Path.GetTempPath(), Guid.NewGuid(), ...)` in all tests |
| TD-24 | ✅ JSON comments test added to `ConfigLoaderTests` |
| TD-26 | ✅ `TechTalk.SpecFlow.Given(...)` qualified attribute test added |
| TD-27 | ✅ `CapturingDocGenLogger` created with all 6 `IDocGenLogger` method implementations |
| TD-28 | ✅ File-not-found test added to `StepDefinitionExtractorTests` |
| TD-32 | ✅ Stage 4 doc corrected: produces `IReadOnlyDictionary<string, int>`, not mutates `RawStep`; data flow diagram updated |
| TD-33 | ✅ Canonical signing spec added: `$schema` included, alphabetical order, no whitespace, viewer warning added; Stage 7 two-phase serialisation clarified |

**TD-34 resolved:** Developer guide now correctly states "roll forward: latestMinor" — no longer contradicts `global.json`.

**Pre-Story-7 blockers resolved:**

| ID | Resolution |
|----|-----------|
| TD-A01 | ✅ `Enum.Parse<StepType>` replaced with `Enum.TryParse`; warn + skip if name has no matching enum value |
| TD-A02 | ✅ Null-suppressor `!` on `GetDirectoryName` replaced with null-coalescing `InvalidOperationException` |
| TD-A03 | ✅ Theory test added asserting `InvalidOperationException` on whitespace-only `root` and `output` values |
| TD-A04 | ✅ Test added verifying `Table` injection does not consume a placeholder slot; trailing `string` becomes `DocString` |
| TD-A11 | ✅ Developer guide §4 updated: commits go directly to `master`; stale worktree instructions removed |
| TD-A05 | ✅ `RestorePackagesWithLockFile` added to `Directory.Build.props`; lock files committed for both projects |
| TD-A06 | ✅ `global.json` rollForward pinned to `latestPatch` |
| TD-A07 | ✅ Test added for empty `.cs` file — Roslyn returns empty step list without exception |
| TD-29  | ✅ Test added for mixed `[Given]` + `[When]` on same method — both produce correct `RawStep` entries |
| TD-A09 | ✅ `DocGenConfig` defaults use `LogVerbosity.Normal` and `ConfigDefaults.FallbackDomain` constants; `ConfigLoader` updated to match |
| TD-A10 | ✅ `Discoverer.excludes` is now non-nullable; null-coalescing guard and null-passing test removed |
| TD-17  | ✅ `File.ReadAllText` now passes `Encoding.UTF8` explicitly |

---

## Summary

| Phase | Items | Theme |
|-------|-------|-------|
| 🔴 Before Story 7 | ~~2~~ 0 | ✅ Both resolved |
| 🟠 Quick wins | ~~10~~ 0 | ✅ All resolved |
| 🟡 Medium work | 4 | Design improvements alongside Stories 8–10 |
| 🟢 Deferred | 12 | Low-risk polish, nice-to-haves |

---

## ✅ Phase 1 — Before Story 7 (resolved)

### TD-A01 · ✅ Resolved — `Enum.Parse<StepType>` replaced with `Enum.TryParse`
**File:** `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs`  
Warn + skip if attribute name has no matching `StepType` value. Prevents crash if `StepAttributeNames` and the enum ever diverge.

---

### TD-A02 · ✅ Resolved — Null suppression `!` on `GetDirectoryName` replaced
**File:** `Delta.DocGen/Config/ConfigLoader.cs`  
Null-coalescing `InvalidOperationException` now thrown instead of `NullReferenceException` on root-level config paths.

---

## ✅ Phase 2 — Quick wins (all resolved)

---

## 🟡 Phase 3 — Medium work (alongside Stories 8–10)

### TD-A12 · Priority 16 — Discoverer has no logging
**File:** `Delta.DocGen/Pipeline/Discoverer.cs`

Stage 2 is completely silent. A caller receives a `DiscoveryResult` with no log output — the user has no visibility into how many files were found or how long discovery took. All other pipeline stages log at least an `Info` summary. The logger is not part of `Discover()`'s signature, which prevents adding logging without an API change.

*Suggested fix:* Add `IDocGenLogger logger` parameter to `Discover()`. Log `Info` at completion: `"{csFiles.Count} C# file(s), {featureFiles.Count} feature file(s) discovered under {root}"`.

---

### TD-A13 · Priority 16 — Unknown Cucumber placeholder types silently accepted
**File:** `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs:79`

`PlaceholderPattern` matches any `{...}` token. A pattern like `"I have {garbage} items"` is accepted without warning; the extractor infers that the `string` parameter maps to a placeholder named `{garbage}`, producing a `string`-typed `ParamRecord`. Valid Cucumber Expression types are `{int}`, `{decimal}`, `{float}`, `{string}`, `{word}`, `{bigdecimal}`. Unknown types will still be handled by Reqnroll (as long as a step binding converter exists), but the emitted schema type will be wrong.

*Suggested fix:* After extracting placeholders, warn for any `{...}` token whose inner name is not in the set of known Cucumber types.

---

### TD-14 · Priority 16 — `ExtractPattern` doesn't handle verbatim strings or constant references
**File:** `StepDefinitionExtractor.cs` (original TD-14)

`ExtractPattern` looks for a `StringLiteralExpression`. A pattern defined as `[Given(MyConstants.LoginPattern)]` (a constant reference) returns `null` from `ExtractPattern`, and the step is skipped with a misleading Warn: "has no string argument" — when in fact it has an argument, just not a literal. Similarly, `[Given(@"I have \d+ items")]` (verbatim string) works because Roslyn extracts verbatim string values correctly, but there's no test for it.

*Suggested fix:* Distinguish "no argument" from "argument is not a string literal" in the Warn message. Optionally: attempt to resolve simple constant-field references via the syntax tree.

---

### TD-A14 · Priority 10 — `CapturingDocGenLogger` has no `Clear()` method
**File:** `Delta.DocGen.Tests/Logging/CapturingDocGenLogger.cs`

Tests that call `Extract()` multiple times on a single logger instance accumulate messages from all calls. There is no `Clear()` method to reset state between assertion phases. This forces tests to either create a new logger per call (slightly wordy) or assert on accumulated messages (fragile).

*Suggested fix:* Add `public void Clear() { InfoMessages.Clear(); VerboseMessages.Clear(); WarnMessages.Clear(); ErrorMessages.Clear(); SummaryMessages.Clear(); }`

---

## 🟢 Phase 4 — Deferred (low risk or low value)

Address opportunistically when touching the relevant file.

| ID | File | Issue |
|----|------|-------|
| TD-03 | `ConfigLoader.cs` | `"General"` fallback domain is a magic string — extract to a constant alongside `LogVerbosity` |
| TD-04 | `ConfigLoader.cs` | Private `ConfigFile` and `DomainRuleDto` DTOs use `{ get; set; }` instead of `{ get; init; }` |
| TD-06 | `ConsoleLogger.cs` | Inconsistent log prefix spacing: `[INFO]    ` 4 spaces, `[VERBOSE] ` 1 space, `[WARN]    ` 4 spaces |
| TD-08 | `StepRecord.cs` | `Used` is typed as `int` — semantically non-negative; no validation at construction |
| TD-21 | `Delta.DocGen.csproj` | Roslyn version (4.9.2) skews ahead of the .NET 8 SDK's bundled version; consider pinning to avoid API drift |
| TD-22 | `Delta.DocGen.Tests.csproj` | FluentAssertions v6 pin has no comment explaining why v7 was avoided (licence change) |
| TD-25 | `ConfigLoaderTests.cs` | No explicit test for the zero-addition `AdditionalExcludes` path (no CLI excludes, config has excludes) |
| TD-30 | `Program.cs` | Placeholder prints `"Delta.DocGen v1"` with no hint it is not yet functional |
| TD-35 | `ConfigLoader.cs` | Two-argument `Path.GetFullPath(path, basePath)` use has no comment explaining the base-path overload is needed |
| TD-36 | `DiscovererTests.cs` | Several `ContainSingle(predicate)` calls don't assert total collection count is exactly 1 |
| TD-A15 | `StepDefinitionExtractorTests.cs` | No test for verbatim string literal patterns (`@"I have \d+ items"`) |
| TD-A16 | `StepDefinitionExtractorTests.cs` | No test for `[StepDefinition]` attribute without a namespace qualification |

---

## Recommended action before Story 7

All pre-Story-7 recommendations resolved. ✅
