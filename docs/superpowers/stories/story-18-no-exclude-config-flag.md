# Story 18: `--no-exclude-config` CLI flag

**Type:** Enhancement (CLI ergonomics)
**Priority:** Low (V1.x)
**Depends on:** Nothing (small, self-contained)

---

## User story

*As an operator running the docgen CLI ad-hoc, I can suppress the excludes declared in the config file so that I can do one-shot full scans (including normally-excluded folders like `RegressionTests`) without editing the config file and reverting it afterwards.*

---

## Background

`--exclude` is currently **additive**: any value passed on the CLI is appended to whatever the config file already declares. There is no way from the CLI to say "ignore the config's excludes, use only what I pass." If a config file excludes `**/RegressionTests/**` and the operator wants a one-shot run that *does* include those, the only option today is to edit the config and revert.

This came up during smoke-testing — the typical config excludes regression-test directories, but it's occasionally useful to generate docs across the full tree for comparison or auditing.

---

## Requirements

1. Add a new boolean CLI option `--no-exclude-config`. When present, the excludes loaded from the config file are discarded; only the CLI `--exclude` entries (if any) are applied.
2. The option has no short alias.
3. Default: `false` — preserves current additive behaviour exactly.
4. `--help` lists the flag with a clear one-line description.
5. Behaviour is testable end-to-end through `CliRunner.Run`.

---

## Approach

### Option binding
Add an `Option<bool>("--no-exclude-config")` to `CliRootCommand.Build`. The bound value flows into a new field on `CliArgs`:

```csharp
public sealed record CliArgs(
    string                ConfigPath,
    string?               Root,
    string?               Output,
    IReadOnlyList<string> Excludes,
    string?               Verbosity,
    bool                  DryRun,
    bool                  NoExcludeConfig);
```

### Config merge
`CliRunner.Run` passes the flag through to `ConfigLoader`. The simplest path is a new property on `ConfigOverrides`:

```csharp
public sealed record ConfigOverrides
{
    public string? Root { get; init; }
    public string? Output { get; init; }
    public string? LogVerbosity { get; init; }
    public IReadOnlyList<string> AdditionalExcludes { get; init; } = [];
    public bool SuppressConfigExcludes { get; init; }
}
```

In `ConfigLoader.Load`, when `SuppressConfigExcludes` is true the config's `exclude` array is replaced with an empty list before merging `AdditionalExcludes`.

---

## Acceptance criteria

- [ ] `docgen --help` lists `--no-exclude-config` with its description
- [ ] `docgen --no-exclude-config` against a config that has excludes produces output that includes files previously excluded
- [ ] `docgen --no-exclude-config --exclude "**/foo/**"` uses only `**/foo/**` (config excludes ignored)
- [ ] `docgen` without the flag preserves current behaviour (additive)
- [ ] At least 2 new tests in `CliRunnerTests`: one for "suppress + no CLI exclude → no excludes", one for "suppress + CLI exclude → only CLI exclude"
- [ ] At least 1 new test in `ConfigLoaderTests` for the `ConfigOverrides.SuppressConfigExcludes` semantics
- [ ] At least 1 new test in `RootCommandTests` pinning the option parses correctly (present + absent + with other flags)
- [ ] Developer-guide §8 CLI table updated with the flag

---

## Out of scope

- Per-pattern suppression (e.g. "remove only `**/bin/**` from the config excludes"). The flag is all-or-nothing.
- A short alias.
- Persisting the choice (no env var, no config file shorthand).

---

## Estimate

~30 minutes. One commit if straightforward; two if tests reveal an edge case in `ConfigLoader`.
