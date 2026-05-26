# Delta.DocGen — Design Specification

**Date:** 2026-05-26
**Status:** Approved

---

## 1. Purpose

Delta.DocGen is a standalone tool that scans a directory tree containing SpecFlow and/or Reqnroll step-definition files (C#) and Gherkin feature files, extracts structured data about every BDD step, and emits a single versioned, signed JSON file. That file is the sole contract between this generator and the Delta.DocView viewer (a separate application developed independently).

This document records all scope decisions, architecture, processing pipeline, output format, configuration, and versioning/security requirements agreed during design.

---

## 2. Scope

### 2.1 V1 — in scope

- Discover all `.feature` and `.cs` step-definition files under a configurable root directory.
- Apply configurable exclude patterns (glob-based) to skip meta-tests, generated files, or any other subtrees.
- Parse step definitions from both **SpecFlow** (`TechTalk.SpecFlow` namespace) and **Reqnroll** (`Reqnroll` namespace) attribute syntax — the attribute form is identical; only the namespace differs.
- Extract per-step: `type`, `pattern`, `params` (name + type; example defaulted by type), `file`, `line`, `source` (verbatim C# method body), `domain` (from config rules), `used` (count from feature file scan).
- Assign stable, deterministic step IDs (domain prefix + pattern hash) so re-runs do not shuffle IDs on the viewer side.
- Emit a versioned, signed JSON envelope (see §5).
- Write a co-located JSON Schema file to the output directory.
- Comprehensive stdout logging with configurable verbosity.
- `description`, `tags`, `example` (realistic), and `suggestsNext` fields are present in the output but empty/defaulted; `enriched: false` in the envelope.

### 2.2 V2 — deferred (fast follower)

- **LLM enrichment:** populate `description`, `tags`, realistic `example` values, and `suggestsNext` using a locally-hosted LLM. The `enriched` flag in the envelope will be `true`.
- **Co-occurrence analysis:** derive `suggestsNext` by analysing which steps follow each other within scenarios across the feature file corpus. Held with v2 as it may be superseded or supplemented by LLM suggestions.
- **Private key signing:** asymmetric signature (e.g. ECDSA) alongside or replacing the SHA-256 digest, to prove authorship in addition to tamper-evidence.

### 2.3 Explicit exclusions (all versions)

- No database or persistent state between runs. Each invocation is a full regeneration.
- No web host or daemon mode.
- No encryption of the output file. Access control is the deployment environment's responsibility.
- No binary output formats (MessagePack, Protobuf, etc.). JSON readability is a requirement.
- No streaming or incremental output. The file is written and read as a single unit.

---

## 3. Technology choices

| Concern | Choice | Rationale |
|---|---|---|
| Language / runtime | C# 12 / .NET 8 console app | Matches existing stack; Roslyn is the authoritative C# parser |
| C# parsing | Roslyn (`Microsoft.CodeAnalysis.CSharp`) | Full AST — handles attributes, partial classes, inheritance correctly |
| Gherkin parsing | Official `Gherkin` NuGet package | Cucumber-maintained; handles all Gherkin dialects |
| JSON serialisation | `System.Text.Json` | In-box, fast, supports source generation |
| CLI argument parsing | `System.CommandLine` | In-box for .NET 8; supports subcommands and option binding |
| Hashing | `System.Security.Cryptography.SHA256` | In-box; no external dependency required |

---

## 4. Architecture

```
Delta.DocGen/
├── CLI/                  # Entry point, argument parsing, progress output
├── Scanner/
│   ├── CSharp/           # Roslyn-based step definition extractor
│   └── Gherkin/          # Feature file parser and usage counter
├── Model/                # StepRecord, ParamRecord, DomainRecord, Envelope (plain C# records)
├── Output/
│   ├── Serialiser/       # Canonical JSON serialisation + SHA-256 signing
│   └── Schema/           # Embedded JSON Schema resource (v1)
├── Config/               # Configuration loading (file + CLI overrides)
└── Logging/              # Structured stdout logging, verbosity control
```

No shared mutable state. The pipeline (§5) is linear; each stage receives its inputs and returns outputs as plain objects.

---

## 5. Processing pipeline

Each stage logs progress to stdout at the appropriate verbosity level.

### Stage 1 — Configuration

- Parse CLI arguments.
- Load `docgen.config.json` (or path from `--config`).
- CLI arguments take precedence over config file values.
- Validate: root directory must exist. Output directory is created if absent.
- Write startup summary (root, output path, exclude count, verbosity) to stdout.

### Stage 2 — Discovery

- Walk the directory tree from root.
- Apply exclude glob patterns; skip matching paths entirely.
- Bucket results: `.cs` files → step definition candidates; `.feature` files → usage scanner input.
- Log: total files found, excluded files, `.cs` count, `.feature` count.

### Stage 3 — C# parsing (Roslyn)

- For each `.cs` file, parse into a `CSharpSyntaxTree`.
- Walk all method declarations.
- Identify methods carrying one or more of: `[Given]`, `[When]`, `[Then]` from either `TechTalk.SpecFlow` or `Reqnroll` namespaces.
- Extract: step type, pattern string, parameter names and types (from method signature), file (relative path), line number, source body.
- Log (verbose): each step found. Log (normal): per-file step count, any files that could not be parsed.

### Stage 4 — Feature file parsing

- For each `.feature` file, parse with the Gherkin library.
- Walk every step line in every scenario.
- Match the step text against extracted patterns; increment `used` on the matching step.
- Log (normal): unmatched step lines as warnings — these indicate stale or out-of-scope steps.

### Stage 5 — Domain assignment

- For each step, evaluate the domain rule list in order (first match wins).
- Rules are glob patterns matched against the step's relative `.cs` file path.
- Steps that match no rule are assigned `fallbackDomain` and logged as warnings.
- Domain `label` is taken from the matching rule.

### Stage 6 — ID generation

- Assign a stable, deterministic ID per step: `<domain-prefix>-<truncated-pattern-hash>`.
- The hash is computed from the normalised pattern string so IDs survive file moves within the same domain.
- IDs are unique within a run; collisions (extremely unlikely) cause a fatal error with a diagnostic.

### Stage 7 — Serialisation and signing

- Build the complete envelope object (see §6).
- Serialise **without** the `signature` field using canonical rules (keys sorted alphabetically at all levels, no whitespace).
- Compute SHA-256 over the UTF-8 bytes of the canonical string.
- Encode as lowercase hex; insert as `signature.digest`.
- Write the final file to the output path (pretty-printed for readability).
- Write the JSON Schema file to `<output-dir>/schema/v1/step-library.schema.json`.

### Stage 8 — Summary

Print to stdout:

- Steps found / features scanned / unmatched step warnings
- Domain breakdown (step count per domain)
- Output file path and size
- SHA-256 digest
- Elapsed time

---

## 6. Output format

### 6.1 Envelope

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
  "steps": [ /* see §6.2 */ ],
  "signature": {
    "algorithm": "SHA-256",
    "digest": "a3f2c1..."
  }
}
```

### 6.2 Step object

```jsonc
{
  "id": "auth-a3f2",
  "type": "Given",
  "pattern": "I am logged in as {string}",
  "params": [
    { "name": "username", "type": "string", "example": "" }
  ],
  "file": "Auth/AuthenticationSteps.cs",
  "line": 42,
  "domain": "Auth",
  "tags": [],
  "used": 0,
  "description": "",
  "source": "[Given(...)]\npublic void GivenIAmLoggedInAs(string username) { ... }",
  "suggestsNext": []
}
```

### 6.3 V1 field defaults

| Field | V1 default |
|---|---|
| `description` | `""` |
| `tags` | `[]` |
| `params[].example` | `""` for string/DocString; `"0"` for int; `"0.00"` for decimal |
| `suggestsNext` | `[]` |
| `enriched` | `false` |

### 6.4 Param types (enumerated)

`string` | `int` | `decimal` | `DocString`

### 6.5 Step types (enumerated)

`Given` | `When` | `Then`

---

## 7. Versioning

### 7.1 Schema version (`version` field)

Follows Semantic Versioning. The `version` field in the envelope identifies the **schema version** of the file, independent of the generator tool version.

| Change type | Version bump | Viewer behaviour |
|---|---|---|
| Field removed, renamed, or type changed | MAJOR | Viewer must reject — display user-visible error |
| New optional field added | MINOR | Viewer must accept; ignore unknown fields |
| Schema doc correction only | PATCH | No behavioural change |

### 7.2 Generator version (`generatorVersion` field)

Records which version of Delta.DocGen produced the file. Informational only — the viewer must not use this field to make load/reject decisions.

### 7.3 Viewer enforcement rules

1. Read `version` before processing any other content.
2. Reject if major version > highest supported major, with a clear error message.
3. Accept minor/patch versions higher or lower than expected.

### 7.4 Schema file versioning

Schema files live at versioned paths: `schema/v<major>/step-library.schema.json`. When a breaking change occurs, the previous schema file is preserved and a new directory is created. The `$schema` field in the envelope points to the correct versioned path.

---

## 8. Integrity and tamper-evidence

### 8.1 Generation

1. Build the complete JSON document in memory without the `signature` field.
2. Serialise using canonical rules: all object keys sorted alphabetically at every nesting level; no insignificant whitespace.
3. Compute SHA-256 over the UTF-8 bytes of the canonical string.
4. Encode as lowercase hex → insert as `signature.digest` with `signature.algorithm = "SHA-256"`.
5. Write the final file (pretty-printed).

### 8.2 Verification (viewer responsibility)

1. Parse the file.
2. Extract and discard the `signature` block.
3. Re-serialise the remaining document using the same canonical rules.
4. Compute SHA-256 and compare to the stored digest.
5. Mismatch → refuse to import; display a clear error indicating possible corruption or tampering.
6. Unknown `algorithm` value → refuse to import.

### 8.3 Scope of protection

The digest covers the entire envelope: `version`, `generatedAt`, `generatorVersion`, `enriched`, `domains`, and `steps`. It guarantees the file has not changed since the generator wrote it. It does not prove authorship (no private key is involved — that is v2 scope).

### 8.4 Canonical serialisation note

Because verification re-canonicalises before hashing, the viewer may reformat or re-indent the JSON without invalidating the signature, provided it applies the same canonical rules before verifying.

---

## 9. Configuration

### 9.1 Config file (`docgen.config.json`)

```jsonc
{
  "root": "./tests",
  "output": "./dist/step-library.json",
  "exclude": [
    "**/meta/**",
    "**/TestsForTests/**",
    "**/*.generated.cs"
  ],
  "logVerbosity": "normal",
  "domains": [
    { "pattern": "Auth/**",            "domain": "Auth",     "label": "Auth & Identity" },
    { "pattern": "Checkout/Payment*",  "domain": "Checkout", "label": "Checkout" },
    { "pattern": "Checkout/**",        "domain": "Checkout", "label": "Checkout" }
  ],
  "fallbackDomain": "General"
}
```

Domain rules are evaluated in declaration order; first match wins. Steps matching no rule are assigned `fallbackDomain` and logged as warnings.

### 9.2 CLI

```
docgen [options]

Options:
  --config <path>      Path to config file (default: ./docgen.config.json)
  --root <path>        Root directory to scan (overrides config)
  --output <path>      Output file path (overrides config)
  --exclude <pattern>  Add an exclude glob pattern (repeatable; additive with config)
  --verbosity <level>  silent | normal | verbose (default: normal)
  --dry-run            Scan and report but do not write output file
```

CLI arguments take precedence over config file values.

### 9.3 Log verbosity

| Level | Output |
|---|---|
| `silent` | Errors and final summary only |
| `normal` | Per-file step counts, warnings (unmatched steps, undetermined domains), final summary |
| `verbose` | Every step found, every pattern match, full diagnostics |

---

## 10. Open questions / future decisions

- ID generation collision strategy: fatal error is specified for v1 but a fallback (e.g. appending a counter suffix) may be preferable — revisit before implementation.
- Whether `--dry-run` should still write the schema file to the output directory.
- V2: choice of local LLM runtime (Ollama, llama.cpp, etc.) and prompt strategy.
- V2: asymmetric signing key distribution and rotation strategy.
