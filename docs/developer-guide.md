# Delta.DocGen — Developer Guide

**Last updated:** 2026-05-27  
**Implementation status:** Stories 1–6 complete (6 of 15 tasks done)

---

## Table of Contents

1. [What is Delta.DocGen?](#1-what-is-delta-docgen)
2. [Repository layout](#2-repository-layout)
3. [Tech stack](#3-tech-stack)
4. [Getting started](#4-getting-started)
5. [Architecture](#5-architecture)
6. [Processing pipeline](#6-processing-pipeline)
7. [Output format](#7-output-format)
8. [Configuration reference](#8-configuration-reference)
9. [Implementation progress](#9-implementation-progress)
10. [Coding conventions](#10-coding-conventions)
11. [Testing approach](#11-testing-approach)
12. [V2 deferred features](#12-v2-deferred-features)

---

## 1. What is Delta.DocGen?

Delta.DocGen is a standalone .NET 8 console tool that scans a directory tree of SpecFlow/Reqnroll C# step-definition files and Gherkin feature files, extracts structured data about every BDD step, and emits a single versioned, SHA-256-signed JSON file.

That JSON file is the **sole data contract** between this generator and the Delta.DocView viewer (a separate application developed independently). Delta.DocGen has no dependency on the viewer; the viewer has no dependency on the generator beyond the agreed JSON schema.

Each run is a full regeneration — there is no persistent state between runs.

---

## 2. Repository layout

```
Delta.DocGen/
├── docs/
│   ├── developer-guide.md                          ← this file
│   ├── data-format-requirements.md                 ← REQ-01/02/03 formal requirements
│   └── superpowers/
│       ├── specs/
│       │   └── 2026-05-26-delta-docgen-design.md   ← full design specification
│       └── plans/
│           └── 2026-05-26-delta-docgen.md          ← task-by-task implementation plan
│
├── Delta.DocGen/                                   ← main project
│   ├── Config/
│   │   ├── ConfigLoader.cs                         ✅ done
│   │   ├── DocGenConfig.cs                         ✅ done
│   │   └── DomainRule.cs                           ✅ done
│   ├── Logging/
│   │   ├── IDocGenLogger.cs                        ✅ done
│   │   ├── ConsoleLogger.cs                        ✅ done
│   │   └── NullDocGenLogger.cs                     ✅ done
│   ├── Model/
│   │   ├── ParamRecord.cs                          ✅ done
│   │   ├── RawStep.cs                              ✅ done
│   │   ├── StepRecord.cs                           ✅ done
│   │   ├── DomainRecord.cs                         ✅ done
│   │   └── Envelope.cs                             ✅ done
│   ├── Pipeline/
│   │   ├── Discoverer.cs                           ✅ done
│   │   ├── DomainAssigner.cs                       ⬜ Story 8
│   │   ├── IdGenerator.cs                          ⬜ Story 9
│   │   └── PipelineRunner.cs                       ⬜ Story 12
│   ├── Scanner/
│   │   ├── CSharp/
│   │   │   └── StepDefinitionExtractor.cs          ✅ done
│   │   └── Gherkin/
│   │       └── UsageCounter.cs                     ⬜ Story 7
│   ├── Output/
│   │   ├── Serialiser/
│   │   │   ├── CanonicalJson.cs                    ⬜ Story 10
│   │   │   └── Signer.cs                           ⬜ Story 10
│   │   └── Schema/
│   │       ├── SchemaWriter.cs                     ⬜ Story 11
│   │       └── Resources/
│   │           └── step-library.v1.schema.json     ⬜ Story 11
│   ├── CLI/
│   │   └── RootCommand.cs                          ⬜ Story 13
│   └── Program.cs                                  ⬜ Story 13 (placeholder exists)
│
├── Delta.DocGen.Tests/                             ← test project
│   ├── Config/
│   │   └── ConfigLoaderTests.cs                    ✅ done (13 tests)
│   ├── Pipeline/
│   │   ├── DiscovererTests.cs                      ✅ done (9 tests)
│   │   ├── DomainAssignerTests.cs                  ⬜ Story 8
│   │   └── IdGeneratorTests.cs                     ⬜ Story 9
│   ├── Scanner/
│   │   ├── CSharp/
│   │   │   └── StepDefinitionExtractorTests.cs     ✅ done (11 tests)
│   │   └── Gherkin/
│   │       └── UsageCounterTests.cs                ⬜ Story 7
│   └── Output/
│       ├── CanonicalJsonTests.cs                   ⬜ Story 10
│       └── SignerTests.cs                          ⬜ Story 10
│
├── Directory.Build.props                           ← shared MSBuild settings
├── global.json                                     ← SDK version pin
└── Delta.DocGen.sln
```

---

## 3. Tech stack

| Concern | Choice | Version |
|---------|--------|---------|
| Runtime | .NET / C# | net8.0 / C# 12 |
| C# parsing | Roslyn | `Microsoft.CodeAnalysis.CSharp` 4.9.2 |
| Gherkin parsing | Official Cucumber parser | `Gherkin` 29.0.0 |
| Glob matching | Microsoft file system globbing | `Microsoft.Extensions.FileSystemGlobbing` 8.0.0 |
| CLI parsing | System.CommandLine | `System.CommandLine` 2.0.0-beta4.22272.1 |
| JSON | In-box | `System.Text.Json` (net8.0) |
| Hashing | In-box | `System.Security.Cryptography.SHA256` |
| Test framework | xUnit | 2.9.3 |
| Test assertions | FluentAssertions | 6.12.0 |

**Build settings** (all projects, via `Directory.Build.props`):

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<TargetFramework>net8.0</TargetFramework>
```

SDK pinned to `8.0.419` (roll forward: `latestMinor`) via `global.json`.

> **Note:** `System.CommandLine` 2.0.0-beta4.22272.1 is a 2022 pre-release. It emits `[Experimental]` attributes on some APIs. If `TreatWarningsAsErrors` causes build failures in Story 13 (CLI), suppress warning `SYSLIB0050` (or whichever code the compiler reports) at the `Delta.DocGen.csproj` level rather than disabling `TreatWarningsAsErrors` globally.

---

## 4. Getting started

### Prerequisites

- .NET SDK 8.0.419 or later minor (see `global.json`)
- Git

### Build

```bash
git clone https://github.com/tafallen/Delta.DocGen.git
cd Delta.DocGen
dotnet build Delta.DocGen.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

### Run tests

```bash
dotnet test Delta.DocGen.sln
```

### Development workflow

The project uses Git worktrees for isolated feature development. Active feature branch: `feature/v1-implementation`, worktree at `.worktrees/feature-v1`.

```bash
# Work in the feature worktree
cd .worktrees/feature-v1
dotnet build Delta.DocGen.sln
dotnet test Delta.DocGen.sln
```

---

## 5. Architecture

### Guiding principles

- **Linear pipeline** — 8 discrete stages, no loops, no back-references between stages.
- **No shared mutable state** — each stage receives inputs and returns plain C# records.
- **No DI framework** — simple constructor injection throughout.
- **No async** — the tool is single-threaded; file I/O is synchronous.
- **Plain records everywhere** — model types are C# `sealed record` types with no behaviour.

### Data flow

```
CLI args
    │
    ▼
┌─────────────────────────────────────────────────────┐
│  Stage 1: Configuration                             │
│  ConfigLoader.Load(path, overrides) → DocGenConfig  │
└───────────────────────────┬─────────────────────────┘
                            │ DocGenConfig
                            ▼
┌─────────────────────────────────────────────────────┐
│  Stage 2: Discovery                                 │
│  Discoverer.Discover(root, excludes) → DiscoveryResult │
│  (.cs files, .feature files — relative paths)       │
└───────────────┬─────────────────┬───────────────────┘
                │ CsFiles         │ FeatureFiles
                ▼                 ▼
┌─────────────────┐   ┌───────────────────────────────┐
│  Stage 3:       │   │  Stage 4:                     │
│  C# parsing     │   │  Feature file parsing         │
│  (Roslyn)       │   │  (Gherkin)                    │
│  → RawStep[]    │   │  → IReadOnlyDictionary        │
└────────┬────────┘   │    <string, int>              │
         │            └──────────────┬────────────────┘
         │                           │ (bypasses Stage 5)
         ▼                           │
┌─────────────────────────────────────────────────────┐
│  Stage 5: Domain assignment                         │
│  DomainAssigner(RawStep[]) → RawStep[]              │
│  (Domain field populated via `with` expression)     │
└───────────────────────────┬─────────────────────────┘
                            │ RawStep[] (domain filled)
                            │   + IReadOnlyDictionary<string, int>
                            ▼
┌─────────────────────────────────────────────────────┐
│  Stage 6: ID generation                             │
│  IdGenerator(RawStep[], counts) → StepRecord[]      │
└───────────────────────────┬─────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────┐
│  Stage 7: Serialisation + signing                   │
│  CanonicalJson + Signer → signed Envelope JSON      │
└───────────────────────────┬─────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────┐
│  Stage 8: File output + summary                     │
│  Write JSON + schema file; print summary to stdout  │
└─────────────────────────────────────────────────────┘
```

### Module responsibilities

| Module | Responsibility |
|--------|---------------|
| `Config/` | Load and validate `docgen.config.json`; merge CLI overrides; resolve paths to absolute |
| `Logging/` | Verbosity-aware structured stdout logging |
| `Model/` | Plain data records passed between pipeline stages |
| `Pipeline/Discoverer` | Walk directory tree; apply glob excludes; return relative file paths |
| `Scanner/CSharp/` | Roslyn AST walk; extract step definitions as `RawStep` records |
| `Scanner/Gherkin/` | Parse feature files; count how many times each pattern is used |
| `Pipeline/DomainAssigner` | Match each step's file path against domain glob rules; assign domain |
| `Pipeline/IdGenerator` | Generate stable deterministic IDs: `<domain-prefix>-<pattern-hash>` |
| `Output/Serialiser/` | Canonical JSON serialisation (sorted keys, no whitespace); SHA-256 signing |
| `Output/Schema/` | Write embedded JSON Schema to output directory. **Story 11 note:** `step-library.v1.schema.json` is currently a placeholder — it must be replaced with the real schema before Story 11 is closed, and the embedded resource registration must be verified. |
| `CLI/` | `System.CommandLine` root command; bind options to `ConfigOverrides` |

---

## 6. Processing pipeline

### Stage 1 — Configuration

- `ConfigLoader.Load(configPath, overrides)` reads `docgen.config.json`.
- CLI arguments win over file values (null = use file).
- `root` and `output` are resolved to **absolute paths relative to the config file's directory** — not the process working directory.
- `logVerbosity` is validated at load time (`silent | normal | verbose`).
- Domain rules must have non-empty `pattern` and `domain`; `label` defaults to `domain` if blank.

### Stage 2 — Discovery

- `Discoverer.Discover(root, excludes)` uses `Microsoft.Extensions.FileSystemGlobbing`.
- Includes: `*.cs`, `*.feature`, `**/*.cs`, `**/*.feature` (root-level + nested).
- Returns forward-slash relative paths, sorted ordinally for deterministic output.
- Splits results into `CsFiles` and `FeatureFiles`.

### Stage 3 — C# parsing (Roslyn)

- Parse each `.cs` file with `CSharpSyntaxTree.ParseText`.
- Walk all method declarations looking for `[Given]`, `[When]`, `[Then]` attributes.
- Both `TechTalk.SpecFlow` and `Reqnroll` namespaces are supported — matched by attribute name only (no namespace resolution required; names are identical).
- Extract per step: type, pattern string, params (name + inferred type), file, line, full method text (all attribute lists + signature + body) as `Source`.
- **DocString detection:** a `string` parameter with no corresponding `{…}` placeholder in the pattern is treated as a DocString parameter.

### Stage 4 — Feature file parsing (Gherkin)

- Parse each `.feature` file with the official Gherkin library.
- Walk every step line in every scenario.
- Match step text against extracted patterns using regex:
  - Cucumber Expression `{string}` → `"[^"]*"`
  - Cucumber Expression `{int}` → `\d+`
  - Cucumber Expression `{decimal}` → `[\d.]+`
  - Old-style regex patterns used as-is.
- Produces `IReadOnlyDictionary<string, int>` (step pattern → use count). `RawStep` is immutable and is not mutated.
- Unmatched step lines → warning log.

The usage dictionary is passed **directly to Stage 6** (IdGenerator), bypassing Stage 5. Stage 5 (DomainAssigner) only receives `RawStep[]`.

### Stage 5 — Domain assignment

- Evaluate domain rules in declaration order; **first match wins**.
- Each rule's `pattern` is a glob matched against the step's relative `.cs` file path.
- Steps matching no rule → `fallbackDomain` + warning.

### Stage 6 — ID generation

- ID format: `<3-char-domain-prefix>-<4-char-sha256-of-normalised-pattern>`
- The prefix is the first 3 characters of the domain ID (lowercase).
- The hash is computed from the normalised pattern, so IDs survive file moves within the same domain.
- Collisions cause a fatal error with a diagnostic.

### Stage 7 — Serialisation and signing

1. Build `Envelope` record (without `signature`).
2. Serialise to **canonical JSON** for signing:
   - **Field inclusion:** all `Envelope` fields **except** `signature`. The `$schema` field **is included** (`$` sorts before all letters).
   - **Key order:** alphabetical by JSON property name at every nesting level. `$` sorts before all letters — `$schema` is first.
   - **Format:** no whitespace, no indentation.
3. Compute SHA-256 over UTF-8 bytes of the canonical string.
4. Encode as lowercase hex → set `signature.digest`, `signature.algorithm = "SHA-256"`.
5. Write final file (pretty-printed for readability).
6. Write JSON Schema file alongside output.

> The viewer must replicate this exact canonical form to verify signatures: include `$schema`, alphabetical key order at all nesting levels, no whitespace. Any deviation causes silent verification failure.

### Stage 8 — Summary

Print to stdout: step count, feature count, unmatched step warnings, domain breakdown, output path + size, digest, elapsed time.

---

## 7. Output format

### Envelope

```jsonc
{
  "$schema": "./schema/v1/step-library.schema.json",
  "version": "1.0.0",
  "generatedAt": "2026-05-26T09:00:00Z",
  "generatorVersion": "1.0.0",
  "enriched": false,
  "domains": [
    { "id": "Auth", "label": "Auth & Identity" }
  ],
  "steps": [ /* see below */ ],
  "signature": {
    "algorithm": "SHA-256",
    "digest": "a3f2c1..."
  }
}
```

### Step object

```jsonc
{
  "id": "aut-a3f2",
  "type": "Given",
  "pattern": "I am logged in as {string}",
  "params": [
    { "name": "username", "type": "string", "example": "" }
  ],
  "file": "Auth/AuthenticationSteps.cs",
  "line": 42,
  "domain": "Auth",
  "tags": [],
  "used": 3,
  "description": "",
  "source": "public void GivenIAmLoggedInAs(string username) { ... }",
  "suggestsNext": []
}
```

### V1 field defaults

| Field | V1 value | Populated in |
|-------|----------|-------------|
| `description` | `""` | V2 (LLM) |
| `tags` | `[]` | V2 (LLM) |
| `params[].example` | `""` / `"0"` / `"0.00"` | V2 (LLM) |
| `suggestsNext` | `[]` | V2 (LLM / co-occurrence) |
| `enriched` | `false` | Always false in V1 |

### Versioning rules

The `version` field follows Semantic Versioning and describes the **schema version**, not the generator version.

| Change | Bump | Viewer action |
|--------|------|---------------|
| Field removed, renamed, or type changed | MAJOR | Reject — show user error |
| New optional field added | MINOR | Accept; ignore unknown fields |
| Doc correction only | PATCH | No action |

Viewers must reject files where `version.major` exceeds the highest supported major.

### Tamper-evidence verification

The viewer must:
1. Parse the file.
2. Extract and discard the `signature` block.
3. Re-serialise using the same canonical rules (alphabetical key sort, no whitespace).
4. Compute SHA-256 and compare to `signature.digest`.
5. Mismatch or unknown `algorithm` → refuse to import.

---

## 8. Configuration reference

### `docgen.config.json`

```jsonc
{
  "root": "./tests",              // Required. Directory to scan.
  "output": "./dist/step-library.json",  // Required. Output file path.
  "exclude": [                    // Optional. Glob patterns to exclude.
    "**/meta/**",
    "**/TestsForTests/**",
    "**/*.generated.cs"
  ],
  "logVerbosity": "normal",       // Optional. silent | normal | verbose. Default: normal.
  "fallbackDomain": "General",    // Optional. Domain for unmatched steps. Default: General.
  "domains": [                    // Optional. First-match-wins domain rules.
    { "pattern": "Auth/**",           "domain": "Auth",     "label": "Auth & Identity" },
    { "pattern": "Checkout/Payment*", "domain": "Checkout", "label": "Checkout" }
  ]
}
```

**Notes:**
- `root` and `output` are resolved relative to the config file's location, not the working directory.
- Domain rule `label` defaults to `domain` value if omitted.
- Domain rule `pattern` and `domain` are required and must be non-empty.
- `logVerbosity` is validated at load time — invalid values throw at startup.

### CLI options

```
docgen [options]

Options:
  --config <path>      Path to config file (default: ./docgen.config.json)
  --root <path>        Root directory to scan (overrides config)
  --output <path>      Output file path (overrides config)
  --exclude <pattern>  Add an exclude glob (repeatable; additive with config excludes)
  --verbosity <level>  silent | normal | verbose (default: normal)
  --dry-run            Scan and report but do not write output file
```

CLI arguments always take precedence over config file values. `--exclude` is additive (does not replace config excludes).

---

## 9. Implementation progress

### Summary

| Stories | Status |
|---------|--------|
| 1–5 | ✅ Complete, merged to master, pushed to GitHub |
| 6–15 | ⬜ Not started |

**Test count:** 33 passing (13 config, 9 discoverer, 11 extractor)

### Story-by-story status

| # | Story | Key deliverables | Status |
|---|-------|-----------------|--------|
| 1 | Project scaffolding | Solution, projects, NuGet refs, `Directory.Build.props`, `global.json` | ✅ |
| 2 | Model records | `ParamRecord`, `RawStep`, `StepRecord`, `DomainRecord`, `Envelope`, `SignatureRecord` | ✅ |
| 3 | Logging | `IDocGenLogger`, `ConsoleLogger` (3 verbosity levels), `NullDocGenLogger` | ✅ |
| 4 | Configuration | `DocGenConfig`, `DomainRule`, `ConfigLoader`, 13 tests | ✅ |
| 5 | File discovery | `Discoverer`, `DiscoveryResult`, 9 tests | ✅ |
| 6 | C# step extraction | `StepDefinitionExtractor` (Roslyn) + tests | ✅ |
| 7 | Usage counting | `UsageCounter` (Gherkin) + tests | ⬜ |
| 8 | Domain assignment | `DomainAssigner` + tests | ⬜ |
| 9 | ID generation | `IdGenerator` + tests | ⬜ |
| 10 | Canonical JSON + signing | `CanonicalJson`, `Signer` + tests | ⬜ |
| 11 | JSON Schema | `step-library.v1.schema.json`, `SchemaWriter` | ⬜ |
| 12 | Pipeline runner | `PipelineRunner` (orchestrates stages 1–8) | ⬜ |
| 13 | CLI wiring | `RootCommand.cs`, `Program.cs` | ⬜ |
| 14 | Full test suite | All tests green, zero warnings | ⬜ |
| 15 | End-to-end smoke test | Real fixture files, full run, output verified | ⬜ |

### What's next — Story 7: Feature file usage counting

The next story implements `UsageCounter` using the Gherkin library. Key points:

- Input: a `.feature` file path (relative) + the absolute root path + the list of `RawStep` records from Stage 3
- Output: the same `RawStep` list with `Used` counts incremented
- Uses the official `Gherkin` NuGet package (already in project)
- Parses each `.feature` file, walks every step line in every scenario
- Matches step text against extracted patterns using regex (Cucumber Expressions → regex)
- Increments `used` counter on the matching `RawStep`
- Unmatched step lines → warning log

---

## 10. Coding conventions

### General

- All types are `sealed` unless inheritance is explicitly required.
- Prefer `sealed record` for data, `sealed class` for behaviour.
- No `public` mutable state — use `init`-only properties or primary constructors.
- `IReadOnlyList<T>` for all collections returned from public APIs.
- Return `.AsReadOnly()` on `List<T>` rather than exposing `List<T>` directly.

### Naming

- Namespaces mirror folder structure: `Delta.DocGen.Config`, `Delta.DocGen.Pipeline`, etc.
- Test classes are named `<Subject>Tests` in the matching namespace under `Delta.DocGen.Tests`.

### Null safety

- `Nullable` is enabled. All reference types are non-nullable by default.
- Use `string?` / `T?` only where null is a meaningful distinct state.
- Null-coalesce defensively at boundaries (e.g. CLI input, JSON deserialization).

### Error handling

- Validate eagerly at startup (config load) rather than deep in pipeline stages.
- Throw typed exceptions (`FileNotFoundException`, `ArgumentException`, `InvalidOperationException`) with clear messages that include the offending value.
- Pipeline stages do not catch exceptions — `PipelineRunner` handles top-level error reporting.

### Logging

Inject `IDocGenLogger` via constructor. Use `NullDocGenLogger.Instance` in unit tests.

```csharp
// Normal progress
_logger.Info($"Scanning {csFiles.Count} C# files...");

// Detail only shown in verbose mode
_logger.Verbose($"  Found step: [{step.Type}] {step.Pattern}");

// Non-fatal problem — continues processing
_logger.Warn($"Unmatched step line: {stepText}");

// Fatal — will be caught by PipelineRunner
_logger.Error("Duplicate step ID detected", ex);
```

---

## 11. Testing approach

### Philosophy

- **TDD from Story 4 onwards** — write the failing test first, implement to green.
- Unit tests cover logic; thin I/O wrappers (logger, schema file writer) are covered by end-to-end tests in Story 15.
- No mocking frameworks — use `NullDocGenLogger.Instance`, temp directories (`Path.GetTempPath()`), and in-memory strings.
- `IDisposable` on test classes that create temp directories — always clean up in `Dispose()`.

### Test file placement

Test files mirror the production layout:

```
Delta.DocGen.Tests/
  Config/ConfigLoaderTests.cs       ← tests for Delta.DocGen/Config/ConfigLoader.cs
  Pipeline/DiscovererTests.cs       ← tests for Delta.DocGen/Pipeline/Discoverer.cs
  Scanner/CSharp/...                ← tests for StepDefinitionExtractor
  ...
```

### Running tests

```bash
# All tests
dotnet test Delta.DocGen.sln

# Single test class
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~ConfigLoaderTests" -v minimal

# Single test
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~ConfigLoaderTests.LoadsMinimalConfigFile"
```

---

## 12. V2 deferred features

These are **explicitly out of scope for V1**. Do not implement them ahead of time.

| Feature | Notes |
|---------|-------|
| LLM enrichment | Populate `description`, `tags`, realistic `example` values using a locally-hosted LLM. `enriched` flag becomes `true`. |
| `suggestsNext` co-occurrence | Analyse which steps follow each other within scenarios. May be superseded by LLM suggestions. |
| Private key (asymmetric) signing | ECDSA or similar alongside the SHA-256 digest, to prove authorship. Key distribution strategy TBD. |

The V1 output format is **deliberately designed** to accommodate V2 enrichment without a breaking schema change. The `enriched: false` flag and empty-string/empty-array defaults are the sentinel values the viewer uses to detect un-enriched output.
