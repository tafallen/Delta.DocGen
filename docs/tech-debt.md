# Delta.DocGen — Technical Debt Register

**Date:** 2026-05-27 (updated after Stories 12–13 review: PipelineRunner, CLI)  
**Scope:** Stories 1–13 + all prior fixes  
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

**Story 7 (UsageCounter) — 7 items found; 6 quick wins resolved (TD-B01–TD-B03, TD-B05–TD-B07):**

| ID | Status |
|----|--------|
| TD-B01 | ✅ Test added for Gherkin parse failure path |
| TD-B02 | ✅ Test added for old-style regex patterns (`^`-prefixed) |
| TD-B03 | ✅ Test added for `{word}` placeholder |
| TD-B04 | 🟡 Background steps not counted (correctness gap) |
| TD-B05 | ✅ `{string}` regex now matches both `"..."` and `'...'` |
| TD-B06 | ✅ First-match-wins behaviour tested |
| TD-B07 | ✅ `WriteFeatureFile` now passes `Encoding.UTF8` |

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
| 🟠 Quick wins (Stories 1–7) | ~~10~~ ~~6~~ 0 | ✅ All resolved |
| 🟠 Quick wins (Stories 8–10) | ~~6~~ 0 | ✅ All resolved (TD-C01, C02, C04, C05, C07, C08) |
| 🟠 Quick wins (Stories 12–13) | ~~6~~ 0 | ✅ All resolved (TD-D01, D03, D04, D06, D07, D11) |
| 🟡 Medium work | 5 + ~~2~~ 0 + ~~5~~ 0 | TD-C09/C10 ✅; original 5 deferred; TD-D02/D05/D08/D13 ✅, TD-D12 documented (broken Reqnroll input — not fixed) |
| 🟢 Deferred | 12 + 7 + 11 | Low-risk polish, nice-to-haves (incl. TD-D09, D10, D14..D23) |

**Stories 8–10 review summary:** 17 items added (TD-C01–TD-C19). All 6 quick wins and both mediums (C09, C10) resolved. Two items from the initial review (canonical-on-disk and signature-subobject ordering) were dismissed after confirming the design spec mandates pretty-print on disk with re-canonicalisation by the verifier (developer-guide §7, line 316).

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

## ✅ Phase 2 — Quick wins (pre-Story-7, all resolved)

---

## ✅ Phase 2b — Quick wins (Story 7, all resolved)

### TD-B01 · ✅ Resolved — Test added for Gherkin parse failure path
**File:** `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs:34–42`

The `try/catch (ParserException)` path — warn + return zero-counts — has no test. A refactor could silently break it.

*Fix:* Write malformed feature content, call `Count`, assert `WarnMessages` contains the parse error and all counts are 0.

---

### TD-B02 · ✅ Resolved — Test added for old-style regex patterns
**File:** `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs:92–93`

`BuildMatchRegex` bypasses Cucumber Expression translation for patterns starting with `^`. Branch is entirely untested.

*Fix:* One test with a `^`-prefixed pattern (e.g. `"^I have \\d+ items$"`) matching a feature file step.

---

### TD-B03 · ✅ Resolved — Test added for `{word}` placeholder
**File:** `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs:106`

`{int}`, `{string}`, and `{decimal}` have dedicated tests; `{word}` does not.

*Fix:* One test: pattern `"I select {word} option"`, step `"I select large option"`, expect count = 1.

---

### TD-B05 · ✅ Resolved — `{string}` regex now matches `"..."` and `'...'`
**File:** `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs:105`

Cucumber Expressions spec: `{string}` matches `"..."` or `'...'`. Current regex `"[^"]*"` rejects single-quoted strings, producing a spurious unmatched-step warning.

*Fix:* Replace with `(?:\"[^\"]*\"|'[^']*')`.

---

### TD-B06 · ✅ Resolved — First-match-wins behaviour tested
**File:** `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs:74–82`

First-match-wins is the defined behaviour but has no test. A regression here (e.g. wrong break placement) would silently produce wrong counts.

*Fix:* One test with two patterns both capable of matching the same step text; assert the first-defined pattern gets the count.

---

### TD-B07 · ✅ Resolved — `WriteFeatureFile` now passes `Encoding.UTF8`
**File:** `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs:21`

Production code uses `Encoding.UTF8` explicitly (per TD-17). The test helper doesn't. Harmless in practice but inconsistent.

*Fix:* Add `Encoding.UTF8` to `File.WriteAllText` in `WriteFeatureFile`.

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

### TD-B04 · Priority 20 — Background steps are not counted
**File:** `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs:46–58`

`Count` walks `Scenario` nodes only. Gherkin's `Background` is a sibling of `Scenario` in `feature.Children` — its steps run before every scenario but are stored separately in the AST. A step used only in a Background will show count 0, producing silently wrong usage data. Same gap applies to Background inside Rule blocks.

*Fix:* Add a `child is Background bg` arm to the feature-children loop (and `ruleChild is Background` inside Rule). Walk `bg.Steps` the same way `MatchScenario` walks scenario steps.

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

## Recommended action before Story 8

All quick wins resolved. ✅ TD-B04 (Background steps not counted) is the only open item below Priority 16 — address alongside Story 9.

---

## ✅ Phase 2c — Quick wins (Stories 8–10, all resolved)

| ID | Resolution |
|----|-----------|
| TD-C01 | ✅ Test renamed `OutputContainsNoInsignificantWhitespace`; asserts on `": "`/`", "`/newlines and confirms space inside string values is preserved |
| TD-C02 | ✅ Three ordering tests rewritten using `JsonNode.Parse` AST enumeration |
| TD-C04 | ✅ `step.File.Replace('\\', '/')` applied before `matcher.Match(...)`; regression test added |
| TD-C05 | ✅ `DomainPrefix` now logs a Warn naming the offending domain when prefix sanitises to empty |
| TD-C07 | ✅ `PatternHash` applies `Normalize(NormalizationForm.FormC)` before lowercasing; NFC≡NFD test added |
| TD-C08 | ✅ Empty `rules` list now emits one consolidated Warn instead of N per-step Warns |

### TD-C01 · ✅ Resolved — Canonical JSON whitespace test was too literal
**File:** `Delta.DocGen.Tests/Output/CanonicalJsonTests.cs:59-68`

`OutputContainsNoWhitespace` asserts the JSON contains no space/`\n`/`\r` anywhere. Any string value containing a space (e.g. `"Auth & Identity"`) would fail it. The contract is "no insignificant whitespace between tokens", not "no whitespace at all". Test passes today only because the fixture uses single-word values.

*Fix:* Replace literal `NotContain(" ")` with a structural check: parse the JSON, re-serialise unindented, compare to the canonical output. Or assert the JSON contains no `": "` / `", "` / `"{\n"` separators.

---

### TD-C02 · Priority 16 — Key-order tests use `IndexOf` instead of parsing
**File:** `Delta.DocGen.Tests/Output/CanonicalJsonTests.cs:14-56`

`KeysSortedAlphabeticallyAtTopLevel`, `NestedObjectKeysAreSorted`, and `ArrayElementOrderIsPreserved` assert ordering via substring `IndexOf`. Fragile to any value containing the key name. Tests pass only because fixtures are carefully chosen.

*Fix:* Parse with `JsonNode.Parse`, enumerate keys/elements in document order, assert sequence equality.

---

### TD-C04 · Priority 16 — Windows backslash paths may not match forward-slash globs
**File:** `Delta.DocGen/Pipeline/DomainAssigner.cs:24`

`Matcher.Match(step.File).HasMatches` behaviour for Windows-style paths (`Auth\AuthSteps.cs`) against forward-slash glob patterns is undocumented. Today's tests use only forward-slash inputs, but the extractor doesn't normalise — on Windows a future change could silently break matching.

*Fix:* Normalise `step.File.Replace('\\', '/')` before `matcher.Match(...)`. Add a regression test with a backslash file path.

---

### TD-C05 · Priority 12 — `DomainPrefix` silently returns `"unknown"` for non-ASCII domains
**File:** `Delta.DocGen/Pipeline/IdGenerator.cs:53-63`

A domain like `"認証"` or `"Café"` strips to `""` or `"caf"`, producing `unknown-xxxxxxxx` IDs. No warning, no signal to the operator that their config produced a meaningless prefix.

*Fix:* If the sanitised prefix is empty (falls back to `"unknown"`), log a Warn naming the offending domain. Considered: detecting "significantly shortened" — but defining "significant" adds magic; empty is the bright line.

---

### TD-C07 · Priority 12 — Pattern hash lacks Unicode normalisation
**File:** `Delta.DocGen/Pipeline/IdGenerator.cs:65-70`

`pattern.Trim().ToLowerInvariant()` does not normalise Unicode form. Two visually identical patterns differing only by NFC vs NFD encoding produce different hashes and therefore different IDs; collision detection won't fire. Unlikely in practice but a latent footgun.

*Fix:* Apply `.Normalize(NormalizationForm.FormC)` before lowercasing. Add a test asserting NFC and NFD forms of the same character produce the same ID.

---

### TD-C08 · Priority 10 — Empty rules list produces N warnings instead of one
**File:** `Delta.DocGen/Pipeline/DomainAssigner.cs:33-39`

When `rules` is empty, every step emits an unmatched-rule warning — potentially thousands of identical messages. A configuration error producing zero rules should surface as a single clear warning, not per-step noise.

*Fix:* If `rules.Count == 0`, emit one Warn at the top of `Assign` and skip the per-step Warn loop. Keep per-step warns when rules exist but a specific step doesn't match.

---

## ✅ Phase 3b — Medium work (Stories 8–10, all resolved)

| ID | Resolution |
|----|-----------|
| TD-C09 | ✅ `CanonicalOutputForKnownEnvelopeIsByteStable` snapshot test added, pins the canonical form of a small fixed envelope byte-for-byte |
| TD-C10 | ✅ `IdGenerator.Generate` split into `IdGenerator.AssignIds` (steps only) and new `DomainBuilder.Build` (domain records); tests moved to `DomainBuilderTests.cs` |

### TD-C09 · ✅ Resolved — No snapshot test pinned the canonical output bytes
**File:** `Delta.DocGen.Tests/Output/CanonicalJsonTests.cs`

Canonical JSON is the input to signing. Any change to `JsonSerializerOptions` (a new converter, a default casing change, a runtime upgrade) could silently shift digests across every existing consumer with no test failure. There is no byte-for-byte regression test pinning the canonical form.

*Fix:* Add a snapshot test with a small fixed envelope (one domain, one step, all fields populated) and assert `CanonicalJson.Serialise(envelope)` equals a checked-in expected string. Acts as a load-bearing regression guard for signature stability.

---

### TD-C10 · Priority 15 — `IdGenerator.Generate` couples ID assignment and domain-record building
**File:** `Delta.DocGen/Pipeline/IdGenerator.cs:11-48`

`Generate` returns a tuple `(Steps, Domains)`. Domain-record building is a separate Stage 6 responsibility per the developer guide; bundling it with ID generation enlarges the test surface and prevents either piece from being reused independently.

*Fix:* Split into `IdGenerator.AssignIds(steps, usageCounts, logger)` → `IReadOnlyList<StepRecord>` and a new `DomainBuilder.Build(steps, rules, fallbackDomain)` → `IReadOnlyList<DomainRecord>`. Pipeline runner composes them.

---

## 🟢 Phase 4b — Deferred (Stories 8–10)

| ID | File | Issue |
|----|------|-------|
| TD-C11 | `CanonicalJson.cs:11-21` | `JsonSerializerOptions` instances are not explicitly `MakeReadOnly()` — .NET 8 freezes on first use anyway |
| TD-C12 | `IdGenerator.cs:62` | Magic string `"unknown"` should be a named constant alongside `ConfigDefaults` |
| TD-C13 | `IdGenerator.cs:69` | SHA-256 truncated to 8 hex chars (32 bits): ~50% collision risk at ~77k steps (birthday bound). No comment justifies the length |
| TD-C14 | `DomainAssigner.cs:39` | Info log uses `"step(s)"` — minor wording inconsistency with other stages |
| TD-C15 | `SignerTests.cs:81` | Uses fully-qualified `System.Security.Cryptography.SHA256` and `System.Text.Encoding.UTF8` instead of `using` directives |
| TD-C16 | `IdGeneratorTests.cs` | No test for an empty domain string (would produce `"unknown"` prefix) |
| TD-C17 | `CanonicalJsonTests.cs:13` | `Dispose` doesn't swallow `IOException` — flaky on Windows if a previous handle is held |
| TD-C18 | `DomainAssigner.cs` | No XML doc comment on the public static class explaining "first match wins" |
| TD-C19 | `IdGenerator.cs:18` | `seenIds` value (the existing pattern) is only used for the exception message — could be `HashSet<string>` if the message dropped it; minor

---

## ✅ Phase 2d — Quick wins (Stories 12–13, all resolved)

| ID | Resolution |
|----|-----------|
| TD-D01 | ✅ `CliRunner` rebuilds `ConsoleLogger` from `config.LogVerbosity` when `--verbosity` not passed |
| TD-D03 | ✅ `SchemaConstants` class added; `PipelineRunner` references `SchemaConstants.{EnvelopeVersion, GeneratorVersion, SchemaRelativeRef, SchemaVersion}` |
| TD-D04 | ✅ Schema written *before* envelope in Stage 8 |
| TD-D06 | ✅ `System.CommandLine`'s built-in `--version` confirmed and pinned by test |
| TD-D07 | ✅ `Func<DateTime>? clock` parameter added to `PipelineRunner.Run`; `DryRunStillComputesDigest` test uses fixed clock |
| TD-D11 | ✅ `Delta.DocGen.CLI.RootCommand` renamed to `CliRootCommand`; alias dropped |

## ✅ Phase 3c — Medium work (Stories 12–13, all resolved)

| ID | Resolution |
|----|-----------|
| TD-D02 | ✅ `FailureCategory` enum added to `PipelineResult`; narrow catches map exceptions to `UserError` / `InternalError` |
| TD-D05 | ✅ `ConsoleLogger.Warn` routes to `Console.Error`; `CliRunner` maps `FailureCategory` to exit codes (0/1/2) |
| TD-D08 | ✅ `LogPhrases.UnmatchedStep` constant shared between `UsageCounter` and `PipelineRunner` |
| TD-D12 | 📝 Documented: same-pattern-different-domain represents broken Reqnroll input (ambiguous-step error at runtime); tool behaviour matches the input |
| TD-D13 | ✅ `PipelineRunner` emits Verbose during dry-run noting the `$schema` ref will not resolve on disk |

### Detail — original items kept for traceability

## 🟠 Phase 2d — Quick wins (Stories 12–13)

### TD-D01 · Priority 36 — Config-file `logVerbosity` ignored
**File:** `Delta.DocGen/CLI/CliRunner.cs:11`

`ConsoleLogger` is built from `args.Verbosity ?? LogVerbosity.Normal` *before* `ConfigLoader.Load` runs. A `logVerbosity: "silent"` value in `docgen.config.json` is silently ignored when no `--verbosity` flag is passed.

*Fix:* After successful config load, if `args.Verbosity is null` rebuild the logger using `config.LogVerbosity`. Add a test pinning the behaviour.

---

### TD-D03 · Priority 30 — Envelope/generator versions are magic strings
**File:** `Delta.DocGen/Pipeline/PipelineRunner.cs:14-16`

`EnvelopeVersion`, `GeneratorVersion`, `SchemaRelativeRef` are private constants with no link to the schema file's `v1/` directory or the assembly version.

*Fix:* Extract a `Delta.DocGen.Output.Schema.SchemaConstants` class with `EnvelopeVersion`, `GeneratorVersion`, `SchemaRelativeRef`, `SchemaVersion`. Wire `SchemaWriter` and `PipelineRunner` to it.

---

### TD-D04 · Priority 28 — Envelope written before schema; failed schema write leaves orphan
**File:** `Delta.DocGen/Pipeline/PipelineRunner.cs:90-95`

Output order is envelope-then-schema. If `SchemaWriter` throws, the envelope is already on disk referencing a missing schema. The collision test verifies "no envelope written" only for failures that throw *before* either write.

*Fix:* Write the schema first (idempotent + tiny), then the envelope.

---

### TD-D06 · Priority 25 — `--version` not wired
**File:** `Delta.DocGen/CLI/RootCommand.cs:38-45`

`docgen --version` returns "Unrecognized command or argument". `System.CommandLine` does not add `--version` by default in this configuration.

*Fix:* Add a `--version` option that prints `SchemaConstants.GeneratorVersion` (from TD-D03) and exits 0 without running the pipeline.

---

### TD-D07 · Priority 24 — `DryRunStillComputesDigest` self-documents as flaky
**File:** `Delta.DocGen/Pipeline/PipelineRunner.cs:77`, `Delta.DocGen.Tests/Pipeline/PipelineRunnerTests.cs:88-96`

`DateTime.UtcNow.ToString(...)` baked into the runner with no seam. The test acknowledges that two back-to-back runs across a second boundary produce different digests.

*Fix:* Add `Func<DateTime>? clock = null` parameter on `Run`, default `() => DateTime.UtcNow`. Rewrite the test to pass a fixed clock.

---

### TD-D11 · Priority 20 — `Delta.DocGen.CLI.RootCommand` shadows `System.CommandLine.RootCommand`
**File:** `Delta.DocGen/CLI/RootCommand.cs:7`, `Delta.DocGen/Program.cs:2`

The naming collision forces a `DocGenRootCommand` alias in `Program.cs` and in tests. A future file will forget the alias.

*Fix:* Rename the static class to `CliRootCommand`. Drop the alias.

---

## 🟡 Phase 3c — Medium work (Stories 12–13)

### TD-D02 · Priority 32 — `catch (Exception)` is too broad and conflates failure kinds
**File:** `Delta.DocGen/Pipeline/PipelineRunner.cs:118`

The top-level catch absorbs everything — `OutOfMemoryException`, future `OperationCanceledException`, and ordinary user-input errors all produce exit code 1.

*Fix:* Add a `FailureCategory` enum (`UserError | InternalError`) to `PipelineResult`. Catch known recoverable types (`InvalidOperationException`, `IOException`, `DirectoryNotFoundException`, `JsonException`); let others propagate. `CliRunner` maps category → exit code (1 user, 2 internal).

---

### TD-D05 · Priority 28 — Errors go to stdout; non-zero exit codes don't discriminate
**File:** `Delta.DocGen/Logging/ConsoleLogger.cs`, `Delta.DocGen/CLI/CliRunner.cs:26-31`

`ConsoleLogger.Error` writes to `Console.Out`. Both "config not found" and "pipeline failed" return exit 1.

*Fix:* Route `Error` and `Warn` to `Console.Error` in `ConsoleLogger`. With TD-D02's `FailureCategory`, map to distinct exit codes (1 user error, 2 pipeline failure).

---

### TD-D08 · Priority 24 — `UnmatchedCountingLogger` substring-sniffs log messages
**File:** `Delta.DocGen/Pipeline/PipelineRunner.cs:21`

`"Unmatched step in"` is hardcoded in two places (producer in `UsageCounter.cs:85` and consumer in `PipelineRunner`). Any wording change zeroes the count silently.

*Fix:* Hoist the phrase into a shared `LogPhrases` constant referenced from both files. Pin the phrase with a test.

---

### TD-D12 · Priority 24 — Usage counts keyed by pattern, not (domain, pattern)
**File:** `Delta.DocGen/Pipeline/PipelineRunner.cs:54-63`

Same pattern in two domains would share a count. **Verified against the spec — Reqnroll throws an ambiguous-step error at runtime if two bindings share a pattern, so this case represents broken user code rather than a real correctness gap.** Documented for awareness; not fixing.

*Status:* Documented — same-pattern-multiple-domains is broken Reqnroll code; tool behaviour matches the input.

---

### TD-D13 · Priority 21 — `$schema` ref set during dry-run despite no schema being written
**File:** `Delta.DocGen/Pipeline/PipelineRunner.cs:74-96`

Currently inert (dry-run writes nothing). If dry-run later emits envelope to stdout, the `$schema` ref points nowhere.

*Fix:* During dry-run, log a Verbose note that the `$schema` reference is unresolved on disk.

---

## 🟢 Phase 4c — Deferred (Stories 12–13)

| ID | File | Issue |
|----|------|-------|
| TD-D09 | `PipelineRunner.cs:38` | No XML doc comment on `Run` clarifying that stages 2–8 are owned here, Stage 1 (config) by the caller |
| TD-D10 | `Program.cs:1` | `using System.CommandLine;` looks unused but is needed for `InvokeAsync` extension — add a comment so it isn't deleted in cleanup |
| TD-D14 | `PipelineRunner.cs:14` | `SchemaRelativeRef` hardcoded forward-slashes — fine for JSON, inconsistent with Windows path handling elsewhere (resolved by TD-D03's SchemaConstants) |
| TD-D15 | `RootCommand.cs:24` | `--exclude` description says "additive" — no `--no-exclude-config` to suppress config excludes |
| TD-D16 | `RootCommand.cs:30` | `--verbosity` accepts any string; `System.CommandLine` supports `FromAmong("silent","normal","verbose")` for early validation |
| TD-D17 | `CliRunnerTests.cs:96-105` | `AdditionalExcludesAreAppliedOnTopOfConfig` only asserts exit 0 — does not actually verify the exclude took effect |
| TD-D18 | `PipelineRunnerTests.cs:113-135` | `PipelineCatchesIdCollisionAndReturnsFailedResult` checks `File.Exists` but not `result.OutputPath`/`SchemaPath` are null |
| TD-D19 | `PipelineFixture.cs` | Fixture used by both pipeline + CLI tests; workspace/config creation duplicated between the two test classes |
| TD-D20 | `PipelineResult.cs` | All counts typed as `int`; no validation that they're non-negative |
| TD-D21 | `PipelineRunner.cs:21` | `MatchPhrase` constant duplicated between producer and consumer (resolved alongside TD-D08) |
| TD-D22 | `CliRunner.cs:11` | `ConsoleLogger` is not disposed — minor on the buffered colours path |
| TD-D23 | `RootCommandTests.cs` | No test for unknown options (`--frobnicate`); `System.CommandLine` behaviour not pinned |

