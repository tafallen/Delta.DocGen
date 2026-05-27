# Delta.DocGen

A .NET 8 console tool that scans SpecFlow/Reqnroll step-definition files and Gherkin feature files, extracts structured BDD step data, and emits a versioned, SHA-256-signed JSON file for consumption by the [Delta.DocView](https://github.com/tafallen) viewer.

## Status

**V1 in active development** — Stories 1–5 of 15 complete.

## What it does

1. Walks a directory tree of `.cs` and `.feature` files
2. Extracts every `[Given]`, `[When]`, `[Then]` step definition using Roslyn
3. Counts how many times each step is used across your feature files
4. Assigns steps to configurable domains (e.g. Auth, Checkout, Payments)
5. Generates stable, deterministic IDs for each step
6. Emits a signed JSON file with a co-located JSON Schema

The output JSON is the sole data contract between this tool and Delta.DocView. It is versioned (semver) and SHA-256 signed for tamper-evidence.

## Usage

```bash
docgen --config ./docgen.config.json
```

```
Options:
  --config <path>      Path to config file (default: ./docgen.config.json)
  --root <path>        Root directory to scan (overrides config)
  --output <path>      Output file path (overrides config)
  --exclude <pattern>  Add an exclude glob (repeatable; additive with config)
  --verbosity <level>  silent | normal | verbose (default: normal)
  --dry-run            Scan and report but do not write output file
```

## Configuration

Create a `docgen.config.json` in your project root:

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
  "fallbackDomain": "General",
  "domains": [
    { "pattern": "Auth/**",      "domain": "Auth",     "label": "Auth & Identity" },
    { "pattern": "Checkout/**",  "domain": "Checkout", "label": "Checkout" }
  ]
}
```

Domain rules are evaluated in declaration order; first match wins. Steps that match no rule are assigned `fallbackDomain` and reported as warnings.

## Output

```jsonc
{
  "$schema": "./schema/v1/step-library.schema.json",
  "version": "1.0.0",
  "generatedAt": "2026-05-27T09:00:00Z",
  "generatorVersion": "1.0.0",
  "enriched": false,
  "domains": [
    { "id": "Auth", "label": "Auth & Identity" }
  ],
  "steps": [
    {
      "id": "aut-a3f2",
      "type": "Given",
      "pattern": "I am logged in as {string}",
      "params": [{ "name": "username", "type": "string", "example": "" }],
      "file": "Auth/AuthenticationSteps.cs",
      "line": 42,
      "domain": "Auth",
      "tags": [],
      "used": 3,
      "description": "",
      "source": "public void GivenIAmLoggedInAs(string username) { ... }",
      "suggestsNext": []
    }
  ],
  "signature": {
    "algorithm": "SHA-256",
    "digest": "a3f2c1..."
  }
}
```

Both SpecFlow (`TechTalk.SpecFlow`) and Reqnroll (`Reqnroll`) attribute namespaces are supported.

## Prerequisites

- .NET SDK 8.0.419+

## Building

```bash
git clone https://github.com/tafallen/Delta.DocGen.git
cd Delta.DocGen
dotnet build Delta.DocGen.sln
dotnet test Delta.DocGen.sln
```

## Documentation

- [Developer Guide](docs/developer-guide.md) — architecture, pipeline, coding conventions, implementation progress
- [Design Specification](docs/superpowers/specs/2026-05-26-delta-docgen-design.md) — full technical design
- [Data Format Requirements](docs/data-format-requirements.md) — schema, versioning, and tamper-evidence requirements

## Tech stack

| | |
|---|---|
| Runtime | .NET 8 / C# 12 |
| C# parsing | Roslyn (`Microsoft.CodeAnalysis.CSharp` 4.9.2) |
| Gherkin parsing | `Gherkin` 29.0.0 |
| Glob matching | `Microsoft.Extensions.FileSystemGlobbing` 8.0.0 |
| CLI | `System.CommandLine` 2.0.0-beta4 |
| JSON | `System.Text.Json` (in-box) |
| Signing | `System.Security.Cryptography.SHA256` (in-box) |

## Roadmap

**V1 (current):** Structural extraction only. `description`, `tags`, `example`, and `suggestsNext` fields are present in the output but empty (`enriched: false`).

**V2:** LLM enrichment to populate those fields using a locally-hosted model, co-occurrence analysis for `suggestsNext`, and private key signing for authorship proof.
