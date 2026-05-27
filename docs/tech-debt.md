# Delta.DocGen — Technical Debt Register

**Date:** 2026-05-27  
**Scope:** Stories 1–6 (all completed code)  
**Formula:** Priority = (Impact + Risk) × (6 − Effort) — higher = more urgent

---

## Summary

| Phase | Items | Theme |
|-------|-------|-------|
| 🔴 Before Story 7 | 3 | Design contradictions that will block or derail the next story |
| 🟠 Quick wins | 10 | Effort-1 fixes with real risk payoff |
| 🟡 Medium work | 8 | Effort-2 correctness and robustness improvements |
| 🟢 Deferred | 15 | Low-risk polish, nice-to-haves |

---

## 🔴 Phase 1 — Before Story 7 (blockers)

These must be resolved before Story 7 begins or the implementor will make a wrong design choice.

### TD-32 · Priority 35 — Stage 4 description contradicts immutable model
**Category:** Documentation debt  
**File:** `docs/developer-guide.md` §6 Stage 4  

Stage 4 says "Increment `used` counter on the matching `RawStep`" — but `RawStep` is an immutable record. The data-flow diagram says Stage 4 outputs `Dictionary<pattern, count>`. Both are in the same document; one is wrong. A Story 7 implementor reading this will either attempt to mutate `RawStep` (breaking the architecture) or produce a dictionary and not know how it connects to Stage 5. **Decide and reconcile before Story 7 is specced.**

*Suggested fix:* Update Stage 4 to say it produces `Dictionary<string, int>` (pattern → count). Stage 5 (DomainAssigner) receives `RawStep[]` + this dictionary and merges them when producing `StepRecord[]`.

---

### TD-07 · Priority 28 — `RawStep` immutability vs. usage counting
**Category:** Architecture debt  
**File:** `Delta.DocGen/Model/RawStep.cs`  

`RawStep` has no `Used` field. `StepRecord` has `Used`. The architecture must decide: does the usage counter operate on `RawStep` (requiring it to either become mutable, or be replaced with a new record) or does it produce a separate dictionary later merged into `StepRecord`? This is the same root cause as TD-32 but manifests in the model. **Both must be fixed together.**

---

### TD-33 · Priority 28 — Canonical JSON signing spec is incomplete
**Category:** Documentation debt  
**File:** `docs/developer-guide.md` §6 Stage 7; `docs/data-format-requirements.md`  

Stage 7 says "serialise without the `signature` field using canonical rules". It does not specify:
- Whether the `$schema` field is included (its `$` prefix sorts before all letters — a developer might exclude it assuming it is metadata)
- The exact field inclusion list

The viewer must replicate the exact same canonical form to verify signatures. If generator and viewer make different assumptions, signature verification fails silently. **Story 10 (serialisation/signing) cannot be specced until this is explicit.**

*Suggested fix:* Add to Stage 7: "The canonical input includes all Envelope fields except `signature`. The `$schema` field is included. Key order: alphabetical by JSON property name at every nesting level, `$` sorts before all letters."

---

## 🟠 Phase 2 — Quick wins (Effort ≤ 1, Priority ≥ 20)

Fix these alongside Story 7 as they each take under an hour.

| ID | Priority | File | Problem | Fix |
|----|----------|------|---------|-----|
| **TD-18** | 25 | `Discoverer.cs:26–29` | Duplicate glob patterns (`*.cs` + `**/*.cs`) — no comment explaining why both are needed; future dev will remove one and break root-level discovery | Add inline comment citing the FileSystemGlobbing behaviour |
| **TD-01** | 24 | `ConfigLoader.cs` + `ConsoleLogger.cs` | Verbosity values duplicated as magic strings in 4 locations; no compile-time enforcement | Extract to `static class LogVerbosity` with string constants, or use an enum |
| **TD-10** | 24 | `RawStep.cs`, `StepRecord.cs` | `Type` is `string`; only valid values are `"Given"`, `"When"`, `"Then"`; no compile-time guard | Introduce `enum StepType { Given, When, Then }` with `JsonStringEnumConverter` |
| **TD-02** | 20 | `ConfigLoader.cs` | Verbosity values are case-sensitive but field names are case-insensitive — `"Normal"` fails with a confusing error | Add `.ToLowerInvariant()` before validation |
| **TD-11** | 20 | `ParamRecord.cs` | `Type` is a free-form `string`; valid values (`"string"`, `"int"`, `"decimal"`, `"DocString"`) are undocumented in code; capitalisation inconsistency | Enum or `static class ParamTypes` with string constants |
| **TD-19** | 20 | `Output/Schema/Resources/step-library.v1.schema.json` | Placeholder embedded resource compiles into assembly now; any code accessing it before Story 11 gets silently invalid schema | Note in Story 11 spec that this file must be replaced AND the embedded resource verified |
| **TD-23** | 20 | `ConfigLoaderTests.cs:128`, `DiscovererTests.cs:71` | `/nonexistent/...` is a Unix-style path; unreliable on Windows CI | Replace with `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "...")` |
| **TD-24** | 20 | `ConfigLoaderTests.cs` | No test for config with JSON comments, despite `ReadCommentHandling.Skip` being set | Add one test with `// comment` in the config JSON |
| **TD-26** | 20 | `StepDefinitionExtractorTests.cs` | No test for `[TechTalk.SpecFlow.Given("...")]` (fully qualified SpecFlow namespace) | Add one test mirroring `ExtractsReqnrollQualifiedAttributeByName` |
| **TD-28** | 20 | `StepDefinitionExtractorTests.cs` | No test for `Extract` with a non-existent file path — unclear if `FileNotFoundException` propagation is intentional | Add test asserting the exception propagates; or add a guard with a friendlier message |

---

## 🟡 Phase 3 — Medium work (Effort 2, alongside Stories 8–10)

These require design decisions or more than one file to change.

### TD-12 · Priority 24 — `ExtractParams` silently misclassifies unknown C# types
**File:** `StepDefinitionExtractor.cs:88–107`  
The `switch` `default` arm handles both `string` (correctly) and any other type (`bool`, `Table`, `DataTable`, `ScenarioContext`, custom types) as `"string"` or `"DocString"`. A Reqnroll `Table` parameter is silently emitted as `"string"` with no warning. Add explicit arms for common SpecFlow/Reqnroll types and a `logger.Warn` for anything unrecognised.

### TD-13 · Priority 24 — `[StepDefinition]` attribute silently ignored
**File:** `StepDefinitionExtractor.cs:12–13`  
Both SpecFlow and Reqnroll support `[StepDefinition]` as a "universal" step attribute (matches Given/When/Then). Steps using it are silently dropped from output. Add `"StepDefinition"` to `StepAttributeNames`; decide and document what `Type` value to emit for it.

### TD-16 · Priority 24 — `placeholderIndex` logic in `ExtractParams` is brittle
**File:** `StepDefinitionExtractor.cs:79–106`  
`placeholderIndex` is incremented in every case arm before the DocString check, meaning the count can be off for mixed-type signatures. Rewrite to explicitly pair parameters with placeholders by position, making the logic easier to reason about and test.

### TD-09 · Priority 16 — `GeneratedAt` typed as `string`
**File:** `Envelope.cs`  
`GeneratedAt` should be `DateTimeOffset` with a `JsonConverter` using `"O"` format. Currently any string can be stored, including malformed timestamps.

### TD-15 · Priority 16 — `Source` includes all attribute decorators, not just step attributes
**File:** `StepDefinitionExtractor.cs:45`  
`method.ToString()` captures the method including `[Obsolete]`, `[TestCategory]`, etc. alongside the step attribute. Developer guide example shows only the step attribute and method. Either the implementation or the documentation is wrong — decide and align them.

### TD-20 · Priority 18 — `System.CommandLine` is a 2022 pre-release beta
**File:** `Delta.DocGen.csproj:21`  
`2.0.0-beta4.22272.1` is four years old. The beta API emits `[Experimental]` attributes that will cause build failures with `TreatWarningsAsErrors`. Address before Story 13. The `.csproj` comment should at minimum name the warning code to suppress (`SYSLIB0050` or the specific one for the beta).

### TD-27 · Priority 16 — No way to test `logger.Warn` without a capturing logger
**File:** `StepDefinitionExtractorTests.cs`  
Warning-path behaviour (step attribute with no string argument) is untested. `NullDocGenLogger` swallows warnings silently. Add a `CapturingDocGenLogger` test helper (small class, 10 lines, no mocking framework) that stores log messages for assertion.

### TD-05 · Priority 16 — `ConsoleLogger` color operations are not thread-safe
**File:** `ConsoleLogger.cs:40–50`  
Safe now (single-threaded), but if parallel file scanning is ever introduced, interleaved `ForegroundColor` sets will produce garbled output. Add a `lock` or use ANSI escape codes to make it safe.

---

## 🟢 Phase 4 — Deferred (low risk or low value)

Address opportunistically when touching the relevant file.

| ID | File | Issue |
|----|------|-------|
| TD-03 | `ConfigLoader.cs` | `"General"` fallback domain magic string — extract to a constant |
| TD-04 | `ConfigLoader.cs` | Private DTO classes use `{ get; set; }` instead of `init` |
| TD-06 | `ConsoleLogger.cs` | Inconsistent log prefix spacing (`[VERBOSE]` has 1 trailing space, others have 4) |
| TD-08 | `StepRecord.cs` | `Used` typed as `int` — semantically non-negative; no validation |
| TD-14 | `StepDefinitionExtractor.cs` | `ExtractPattern` doesn't handle verbatim strings or constant references; warning message is misleading |
| TD-17 | `StepDefinitionExtractor.cs` | `File.ReadAllText` without explicit encoding — specify `Encoding.UTF8` |
| TD-21 | `Delta.DocGen.csproj` | Roslyn version (4.9.2) skews ahead of .NET 8 SDK's bundled version |
| TD-22 | `Delta.DocGen.Tests.csproj` | FluentAssertions v6 pin has no comment explaining why v7 was avoided (license) |
| TD-25 | `ConfigLoaderTests.cs` | No test for zero-addition `AdditionalExcludes` path |
| TD-29 | `StepDefinitionExtractorTests.cs` | No test for mixed `[Given]` + `[When]` on the same method |
| TD-30 | `Program.cs` | Placeholder prints `"Delta.DocGen v1"` with no hint it is not yet functional |
| TD-31 | `developer-guide.md` | Layout table still shows `StepDefinitionExtractorTests.cs` as `⬜ Story 6` |
| TD-34 | `global.json` | `rollForward: "latestMinor"` contradicts guide's claim of pinned SDK |
| TD-35 | `ConfigLoader.cs` | Two-argument `Path.GetFullPath` use has no comment explaining why |
| TD-36 | `DiscovererTests.cs:29` | `ContainSingle(predicate)` doesn't assert total count of 1 |

---

## Recommended action before Story 7

1. Resolve **TD-32** and **TD-07** together — decide the `RawStep` / usage count architecture and update the developer guide. This takes 30 minutes but prevents a full story's worth of rework.
2. Resolve **TD-33** — write the exact canonical JSON spec into the developer guide before Story 10 is planned.
3. Fix **TD-23** (cross-platform test paths) — 5 minutes; prevents spurious CI failures.
4. Fix **TD-18** (document the duplicate glob pattern) — 2 minutes; prevents the next developer from "cleaning up" a load-bearing hack.
