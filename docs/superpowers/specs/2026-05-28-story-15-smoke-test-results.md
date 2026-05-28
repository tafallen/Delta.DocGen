# Story 15 — End-to-End Smoke Test Results

**Date:** 2026-05-28
**Status:** ✅ Complete

## What was tested

The fully-built `Delta.DocGen` CLI was run against a real-world Reqnroll codebase at `C:\dev\triangle\Step Definitions` — a 1,256-file step-definition library spanning Allegro, Openlink, and Core trading-system test suites.

## Configuration

Smoke-test config at `smoketest/triangle.docgen.config.json`:

```json
{
  "root": "C:/dev/triangle/Step Definitions",
  "output": "C:/dev/Delta.DocGen/smoketest/triangle-step-library.json",
  "exclude": ["**/bin/**", "**/obj/**", "**/*.generated.cs", "**/AssemblyInfo.cs", "**/*RegressionTests*/**"],
  "logVerbosity": "normal",
  "fallbackDomain": "General",
  "domains": [
    { "pattern": "TTL.StepDefinitions.Allegro/**",  "domain": "Allegro",        "label": "Allegro" },
    { "pattern": "TTL.StepDefinitions.Openlink/**", "domain": "Openlink",       "label": "Openlink" },
    { "pattern": "TTL.StepDefinitions.Core/**",     "domain": "Core",           "label": "Core" },
    { "pattern": "TTL.Common.Allegro/**",           "domain": "CommonAllegro",  "label": "Common — Allegro" },
    { "pattern": "TTL.Common.Openlink/**",          "domain": "CommonOpenlink", "label": "Common — Openlink" },
    { "pattern": "TTL.Common/**",                   "domain": "Common",         "label": "Common" }
  ]
}
```

## Result

```
[DONE]  Pipeline complete: 4106 step(s), 4 domain(s), 1256 C# file(s), 73 feature file(s), elapsed 527811ms.
```

| Metric | Value |
|--------|------:|
| Step records emitted | 4106 |
| Domains | 4 |
| C# files parsed | 1256 |
| Feature files parsed | 73 |
| Elapsed (wall clock) | ~9 min |
| Output JSON size | 5.8 MB |
| Schema written | ✅ `smoketest/schema/v1/step-library.schema.json` |
| Signature computed | ✅ SHA-256 |
| Pipeline exit code | 0 |

## Real bugs surfaced and fixed

The smoke test ran against a real codebase exposed four defects that unit tests had not caught. All four were fixed in flight:

| # | Defect | Fix commit |
|---|--------|-----------|
| 1 | `[Given]+[Then]` on one method → fatal ID collision | `b6a3a21` — include `StepType` in hash |
| 2 | Stacked identical attrs on one method → fatal collision | `e0cf3c8` — extractor dedupes per method with Warn |
| 3 | Ambiguous bindings across methods → fatal pipeline abort | `3ed8891` — Error log + skip, pipeline continues |
| 4 | Output directory created on demand by `CanonicalJson.Write` | Already worked; verified |

## Real findings in the triangle codebase

The tool's Error log surfaced ~30 ambiguous-binding cases in the user's code, all of the form:

```
[ERROR]   Ambiguous step binding: 'openlink-5efc05a7' generated for both
  'I create VolatilityOTC transactions as follows' (.../VolatilityOTCTransactionSteps.cs:154) and
  'I create volatilityotc transactions as follows' (.../VolatilityOTCTransactionSteps.cs:155)
  — skipping the second binding.
```

These are case-only duplicate bindings. SpecFlow's matching is case-insensitive by default, so the tool correctly treats them as the same binding and emits one `StepRecord` per logical step. The user can choose to consolidate them in source at their leisure; the doc tool produces usable output regardless.

## Conclusions

- **End-to-end pipeline works.** Discovery → C# parse → Gherkin parse → domain assignment → ID generation → canonical serialisation → SHA-256 signing → on-disk JSON + schema. Every stage exercised against thousands of real files.
- **Output is valid.** Manual inspection confirms valid envelope structure (`$schema`, `version`, `generatedAt`, `domains`, `steps`, `signature`).
- **Output is signed.** SHA-256 digest present; verifier algorithm documented in dev guide §7.
- **Tool is resilient.** Defects in input code (ambiguous bindings, duplicate attributes) produce loud Error log entries but do not block output.
- **Performance is acceptable for V1.** 9 minutes on a 1,256-file codebase is fine for a build-step doc generator. Parallel file parsing is the obvious next optimisation if needed.

## Closing the project

All 15 stories complete. 134 unit tests passing. Real-world smoke test passed. Tool is V1-ready.
