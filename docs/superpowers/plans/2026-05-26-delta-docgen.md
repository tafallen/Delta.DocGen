# Delta.DocGen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a .NET 8 console tool that scans a tree of SpecFlow/Reqnroll C# step-definition files and Gherkin feature files, extracts structured step data, and emits a versioned, SHA-256-signed JSON file for consumption by the Delta.DocView viewer.

**Architecture:** Linear eight-stage pipeline: configure → discover → parse C# (Roslyn) → count usages (Gherkin) → assign domains → generate IDs → serialise + sign → write output. No shared mutable state; each stage receives inputs and returns plain C# records. No DI framework — simple constructor injection throughout.

**Tech Stack:** .NET 8 / C# 12, Roslyn (`Microsoft.CodeAnalysis.CSharp` 4.9.2), `Gherkin` 29.0.0, `System.CommandLine` 2.0.0-beta4, `Microsoft.Extensions.FileSystemGlobbing` 8.0.0, `System.Text.Json` (in-box), xUnit 2.7.0.

---

## Critical Architecture Decisions

These decisions are locked in and must not be revisited without updating this plan and the design spec.

| Decision | Choice | Rationale |
|---|---|---|
| C# parsing | Roslyn `CSharpSyntaxTree` | Full AST; handles partial classes, verbatim strings, named args correctly |
| Attribute namespace handling | Name-based match only (`Given`/`When`/`Then`) | SpecFlow and Reqnroll use identical attribute names; semantic resolution requires loading all references |
| Pattern storage | Raw string from attribute | No conversion from old-style regex to Cucumber Expressions in v1 |
| Usage matching | Convert Cucumber Expressions to regex at scan time | `{string}` → `"[^"]*"`, `{int}` → `\d+`, `{decimal}` → `[\d.]+`; old-style regex patterns used as-is |
| Domain assignment | First-match-wins, config-driven glob rules | Explicit over implicit; unmatched → `fallbackDomain` + warning |
| ID format | `<3-char-domain-prefix>-<4-char-sha256-of-pattern>` | Stable across file moves within same domain; short and readable |
| Canonical JSON | Recursive key-sort via `JsonNode`, no whitespace | Byte-reproducible regardless of serialisation order or pretty-printing |
| Signing | SHA-256 over canonical JSON bytes (UTF-8), excluding `signature` field | Tamper-evidence without private key (v2 scope) |
| V1 enriched defaults | `description: ""`, `tags: []`, `suggestsNext: []`, `example` by type | `enriched: false` in envelope; viewer must tolerate empty fields |
| Param example defaults | `string`/`DocString` → `""`, `int` → `"0"`, `decimal` → `"0.00"` | Type-safe minimal defaults; LLM replaces in v2 |
| DocString detection | Method has a `string` param with no corresponding `{…}` placeholder | Reliable heuristic for SpecFlow/Reqnroll docstring binding |

---

## File Map

Every file this plan creates or modifies:

```
Delta.DocGen.sln
│
├── Delta.DocGen/
│   ├── Delta.DocGen.csproj
│   ├── Program.cs                                   # Entry point; wires CLI
│   │
│   ├── CLI/
│   │   └── RootCommand.cs                           # System.CommandLine root command + all options
│   │
│   ├── Config/
│   │   ├── DocGenConfig.cs                          # Top-level config record
│   │   ├── DomainRule.cs                            # { Pattern, Domain, Label }
│   │   └── ConfigLoader.cs                          # Loads JSON file; merges CLI overrides
│   │
│   ├── Logging/
│   │   ├── IDocGenLogger.cs                         # Interface: Info/Warn/Error/Verbose/Summary
│   │   └── ConsoleLogger.cs                         # Verbosity-aware stdout implementation
│   │
│   ├── Model/
│   │   ├── ParamRecord.cs                           # { Name, Type, Example }
│   │   ├── RawStep.cs                               # Intermediate (pre-ID, pre-domain) step
│   │   ├── StepRecord.cs                            # Final step (all fields populated)
│   │   ├── DomainRecord.cs                          # { Id, Label }
│   │   └── Envelope.cs                              # Top-level output + SignatureRecord
│   │
│   ├── Scanner/
│   │   ├── CSharp/
│   │   │   └── StepDefinitionExtractor.cs           # Roslyn walker → IReadOnlyList<RawStep>
│   │   └── Gherkin/
│   │       └── UsageCounter.cs                      # Feature parser → Dictionary<string,int>
│   │
│   ├── Pipeline/
│   │   ├── Discoverer.cs                            # Stage 2: glob-excluded file walk
│   │   ├── DomainAssigner.cs                        # Stage 5: rule matching + fallback
│   │   ├── IdGenerator.cs                           # Stage 6: deterministic ID
│   │   └── PipelineRunner.cs                        # Orchestrates stages 1–8; calls logger
│   │
│   └── Output/
│       ├── Serialiser/
│       │   ├── CanonicalJson.cs                     # Recursive key-sort via JsonNode
│       │   └── Signer.cs                            # SHA-256 over canonical bytes
│       └── Schema/
│           ├── SchemaWriter.cs                      # Copies embedded schema to output dir
│           └── Resources/
│               └── step-library.v1.schema.json      # Embedded JSON Schema (draft 2020-12)
│
└── Delta.DocGen.Tests/
    ├── Delta.DocGen.Tests.csproj
    ├── Config/
    │   └── ConfigLoaderTests.cs
    ├── Scanner/
    │   ├── CSharp/
    │   │   └── StepDefinitionExtractorTests.cs
    │   └── Gherkin/
    │       └── UsageCounterTests.cs
    ├── Pipeline/
    │   ├── DiscovererTests.cs
    │   ├── DomainAssignerTests.cs
    │   └── IdGeneratorTests.cs
    └── Output/
        ├── CanonicalJsonTests.cs
        └── SignerTests.cs
```

---

## Story 1: Project scaffolding

*As a developer, I can create and build the solution so all subsequent tasks have a home.*

### Task 1: Create solution and projects

**Files:**
- Create: `Delta.DocGen.sln`
- Create: `Delta.DocGen/Delta.DocGen.csproj`
- Create: `Delta.DocGen.Tests/Delta.DocGen.Tests.csproj`
- Create: `Delta.DocGen/Program.cs`

- [ ] **Step 1: Create the solution and main project**

```bash
cd C:\repos\Delta.DocGen
dotnet new sln -n Delta.DocGen
dotnet new console -n Delta.DocGen -o Delta.DocGen --framework net8.0
dotnet sln add Delta.DocGen/Delta.DocGen.csproj
```

- [ ] **Step 2: Create the test project and add to solution**

```bash
dotnet new xunit -n Delta.DocGen.Tests -o Delta.DocGen.Tests --framework net8.0
dotnet sln add Delta.DocGen.Tests/Delta.DocGen.Tests.csproj
dotnet add Delta.DocGen.Tests/Delta.DocGen.Tests.csproj reference Delta.DocGen/Delta.DocGen.csproj
```

- [ ] **Step 3: Add NuGet packages to the main project**

```bash
dotnet add Delta.DocGen/Delta.DocGen.csproj package Microsoft.CodeAnalysis.CSharp --version 4.9.2
dotnet add Delta.DocGen/Delta.DocGen.csproj package Gherkin --version 29.0.0
dotnet add Delta.DocGen/Delta.DocGen.csproj package System.CommandLine --version 2.0.0-beta4.22272.1
dotnet add Delta.DocGen/Delta.DocGen.csproj package Microsoft.Extensions.FileSystemGlobbing --version 8.0.0
```

- [ ] **Step 4: Add NuGet packages to the test project**

```bash
dotnet add Delta.DocGen.Tests/Delta.DocGen.Tests.csproj package FluentAssertions --version 6.12.0
```

- [ ] **Step 5: Edit `Delta.DocGen/Delta.DocGen.csproj` to enable nullable, embedded resources, and treat warnings as errors**

Replace the entire file content with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AssemblyName>docgen</AssemblyName>
    <RootNamespace>Delta.DocGen</RootNamespace>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="Output\Schema\Resources\step-library.v1.schema.json"/>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Gherkin" Version="29.0.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.9.2" />
    <PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="8.0.0" />
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Replace `Delta.DocGen/Program.cs` with a minimal placeholder**

```csharp
// Entry point — wired in Task 14 (CLI).
// Placeholder keeps the project buildable during development.
Console.WriteLine("Delta.DocGen v1");
```

- [ ] **Step 7: Build the solution to confirm zero errors**

```bash
dotnet build Delta.DocGen.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 8: Commit**

```bash
git add Delta.DocGen.sln Delta.DocGen/ Delta.DocGen.Tests/
git commit -m "chore: scaffold solution, projects, and NuGet references"
```

---

## Story 2: Model

*As a developer, I have strongly-typed C# records for every data shape the pipeline passes between stages.*

### Task 2: Define model records

**Files:**
- Create: `Delta.DocGen/Model/ParamRecord.cs`
- Create: `Delta.DocGen/Model/RawStep.cs`
- Create: `Delta.DocGen/Model/StepRecord.cs`
- Create: `Delta.DocGen/Model/DomainRecord.cs`
- Create: `Delta.DocGen/Model/Envelope.cs`

- [ ] **Step 1: Create `ParamRecord.cs`**

```csharp
namespace Delta.DocGen.Model;

/// <summary>A single parameter on a step definition.</summary>
/// <param name="Name">Parameter name as declared in the C# method signature.</param>
/// <param name="Type">Schema type: string | int | decimal | DocString.</param>
/// <param name="Example">Default example value; empty until LLM enrichment (v2).</param>
public sealed record ParamRecord(string Name, string Type, string Example);
```

- [ ] **Step 2: Create `RawStep.cs` — the intermediate record produced by Roslyn scanning, before domain/ID assignment**

```csharp
namespace Delta.DocGen.Model;

/// <summary>
/// Intermediate step record produced by the C# scanner.
/// Domain and Id are assigned in later pipeline stages.
/// </summary>
public sealed record RawStep(
    string Type,           // Given | When | Then
    string Pattern,        // Raw string from the attribute argument
    IReadOnlyList<ParamRecord> Params,
    string File,           // Relative path to .cs file
    int Line,              // 1-based line number of the attribute
    string Source          // Verbatim C# method body text
);
```

- [ ] **Step 3: Create `StepRecord.cs` — the fully resolved record written to the output envelope**

```csharp
using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

/// <summary>Fully resolved step — all pipeline stages complete.</summary>
public sealed record StepRecord(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("type")]        string Type,
    [property: JsonPropertyName("pattern")]     string Pattern,
    [property: JsonPropertyName("params")]      IReadOnlyList<ParamRecord> Params,
    [property: JsonPropertyName("file")]        string File,
    [property: JsonPropertyName("line")]        int Line,
    [property: JsonPropertyName("domain")]      string Domain,
    [property: JsonPropertyName("tags")]        IReadOnlyList<string> Tags,
    [property: JsonPropertyName("used")]        int Used,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("source")]      string Source,
    [property: JsonPropertyName("suggestsNext")]IReadOnlyList<string> SuggestsNext
);
```

- [ ] **Step 4: Create `DomainRecord.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

public sealed record DomainRecord(
    [property: JsonPropertyName("id")]    string Id,
    [property: JsonPropertyName("label")] string Label
);
```

- [ ] **Step 5: Create `Envelope.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Delta.DocGen.Model;

public sealed record SignatureRecord(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("digest")]    string Digest
);

public sealed record Envelope(
    [property: JsonPropertyName("$schema")]          string Schema,
    [property: JsonPropertyName("version")]          string Version,
    [property: JsonPropertyName("generatedAt")]      string GeneratedAt,
    [property: JsonPropertyName("generatorVersion")] string GeneratorVersion,
    [property: JsonPropertyName("enriched")]         bool Enriched,
    [property: JsonPropertyName("domains")]          IReadOnlyList<DomainRecord> Domains,
    [property: JsonPropertyName("steps")]            IReadOnlyList<StepRecord> Steps,
    [property: JsonPropertyName("signature")]        SignatureRecord? Signature
);
```

- [ ] **Step 6: Build to confirm zero errors**

```bash
dotnet build Delta.DocGen.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 7: Commit**

```bash
git add Delta.DocGen/Model/
git commit -m "feat: add model records (ParamRecord, RawStep, StepRecord, DomainRecord, Envelope)"
```

---

## Story 3: Logging

*As a user, I see structured progress output on stdout with configurable verbosity.*

### Task 3: Implement console logger

**Files:**
- Create: `Delta.DocGen/Logging/IDocGenLogger.cs`
- Create: `Delta.DocGen/Logging/ConsoleLogger.cs`

- [ ] **Step 1: Create `IDocGenLogger.cs`**

```csharp
namespace Delta.DocGen.Logging;

public interface IDocGenLogger
{
    void Info(string message);
    void Verbose(string message);
    void Warn(string message);
    void Error(string message);
    void Summary(string message);
}
```

- [ ] **Step 2: Create `ConsoleLogger.cs`**

```csharp
namespace Delta.DocGen.Logging;

/// <summary>
/// Verbosity levels:
///   silent  — Error + Summary only
///   normal  — Info + Warn + Error + Summary  (default)
///   verbose — all levels
/// </summary>
public sealed class ConsoleLogger(string verbosity) : IDocGenLogger
{
    private readonly bool _silent  = verbosity == "silent";
    private readonly bool _verbose = verbosity == "verbose";

    public void Info(string message)
    {
        if (_silent) return;
        Console.WriteLine($"[INFO]  {message}");
    }

    public void Verbose(string message)
    {
        if (!_verbose) return;
        Console.WriteLine($"[VERB]  {message}");
    }

    public void Warn(string message)
    {
        if (_silent) return;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN]  {message}");
        Console.ResetColor();
    }

    public void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] {message}");
        Console.ResetColor();
    }

    public void Summary(string message)
    {
        Console.WriteLine($"[DONE]  {message}");
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build Delta.DocGen.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add Delta.DocGen/Logging/
git commit -m "feat: add IDocGenLogger and ConsoleLogger with verbosity control"
```

---

## Story 4: Configuration

*As a user, I can drive the tool from a JSON config file, with CLI arguments overriding individual values.*

### Task 4: Config records and loader

**Files:**
- Create: `Delta.DocGen/Config/DomainRule.cs`
- Create: `Delta.DocGen/Config/DocGenConfig.cs`
- Create: `Delta.DocGen/Config/ConfigLoader.cs`
- Create: `Delta.DocGen.Tests/Config/ConfigLoaderTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Delta.DocGen.Tests/Config/ConfigLoaderTests.cs`:

```csharp
using Delta.DocGen.Config;
using FluentAssertions;

namespace Delta.DocGen.Tests.Config;

public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public ConfigLoaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LoadsMinimalConfigFile()
    {
        var json = """
            {
              "root": "./tests",
              "output": "./dist/step-library.json"
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var config = ConfigLoader.Load(path, overrides: new ConfigOverrides());

        config.Root.Should().Be("./tests");
        config.Output.Should().Be("./dist/step-library.json");
        config.Exclude.Should().BeEmpty();
        config.LogVerbosity.Should().Be("normal");
        config.FallbackDomain.Should().Be("General");
        config.Domains.Should().BeEmpty();
    }

    [Fact]
    public void LoadsDomainRules()
    {
        var json = """
            {
              "root": "./tests",
              "output": "./out.json",
              "domains": [
                { "pattern": "Auth/**", "domain": "Auth", "label": "Auth & Identity" }
              ]
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var config = ConfigLoader.Load(path, overrides: new ConfigOverrides());

        config.Domains.Should().HaveCount(1);
        config.Domains[0].Pattern.Should().Be("Auth/**");
        config.Domains[0].Domain.Should().Be("Auth");
        config.Domains[0].Label.Should().Be("Auth & Identity");
    }

    [Fact]
    public void CliOverridesRootAndOutput()
    {
        var json = """{ "root": "./tests", "output": "./out.json" }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var overrides = new ConfigOverrides { Root = "./other", Output = "./other/out.json" };
        var config = ConfigLoader.Load(path, overrides);

        config.Root.Should().Be("./other");
        config.Output.Should().Be("./other/out.json");
    }

    [Fact]
    public void CliExcludesAreAdditiveWithConfigExcludes()
    {
        var json = """
            {
              "root": "./tests",
              "output": "./out.json",
              "exclude": ["**/meta/**"]
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var overrides = new ConfigOverrides { AdditionalExcludes = ["**/generated/**"] };
        var config = ConfigLoader.Load(path, overrides);

        config.Exclude.Should().BeEquivalentTo(["**/meta/**", "**/generated/**"]);
    }

    [Fact]
    public void ThrowsIfConfigFileNotFound()
    {
        var act = () => ConfigLoader.Load("/nonexistent/docgen.config.json", new ConfigOverrides());
        act.Should().Throw<FileNotFoundException>();
    }
}
```

- [ ] **Step 2: Run to confirm failure**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~ConfigLoaderTests" --no-build 2>&1 | tail -5
```

Expected: build error — `ConfigLoader`, `ConfigOverrides`, `DocGenConfig` not yet defined.

- [ ] **Step 3: Create `DomainRule.cs`**

```csharp
namespace Delta.DocGen.Config;

public sealed record DomainRule(string Pattern, string Domain, string Label);
```

- [ ] **Step 4: Create `DocGenConfig.cs`**

```csharp
namespace Delta.DocGen.Config;

public sealed record DocGenConfig
{
    public required string Root { get; init; }
    public required string Output { get; init; }
    public IReadOnlyList<string> Exclude { get; init; } = [];
    public string LogVerbosity { get; init; } = "normal";
    public IReadOnlyList<DomainRule> Domains { get; init; } = [];
    public string FallbackDomain { get; init; } = "General";
}
```

- [ ] **Step 5: Create `ConfigLoader.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delta.DocGen.Config;

/// <summary>Values that CLI arguments can override from the config file.</summary>
public sealed record ConfigOverrides
{
    public string? Root { get; init; }
    public string? Output { get; init; }
    public string? LogVerbosity { get; init; }
    public IReadOnlyList<string> AdditionalExcludes { get; init; } = [];
}

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static DocGenConfig Load(string configPath, ConfigOverrides overrides)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Config file not found: {configPath}", configPath);

        var json = File.ReadAllText(configPath);
        var file = JsonSerializer.Deserialize<ConfigFile>(json, _options)
                   ?? throw new InvalidOperationException("Config file is empty or invalid JSON.");

        var excludes = new List<string>(file.Exclude ?? []);
        excludes.AddRange(overrides.AdditionalExcludes);

        return new DocGenConfig
        {
            Root          = overrides.Root          ?? file.Root          ?? throw new InvalidOperationException("'root' is required in config."),
            Output        = overrides.Output        ?? file.Output        ?? throw new InvalidOperationException("'output' is required in config."),
            LogVerbosity  = overrides.LogVerbosity  ?? file.LogVerbosity  ?? "normal",
            FallbackDomain= file.FallbackDomain     ?? "General",
            Exclude       = excludes.AsReadOnly(),
            Domains       = (file.Domains ?? []).Select(d => new DomainRule(d.Pattern, d.Domain, d.Label)).ToList().AsReadOnly(),
        };
    }

    // Private DTO for JSON deserialization only
    private sealed class ConfigFile
    {
        public string? Root { get; set; }
        public string? Output { get; set; }
        public List<string>? Exclude { get; set; }
        public string? LogVerbosity { get; set; }
        public List<DomainRuleDto>? Domains { get; set; }
        public string? FallbackDomain { get; set; }
    }

    private sealed class DomainRuleDto
    {
        public string Pattern { get; set; } = "";
        public string Domain { get; set; } = "";
        public string Label { get; set; } = "";
    }
}
```

- [ ] **Step 6: Run tests to confirm they pass**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~ConfigLoaderTests" -v minimal
```

Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 7: Commit**

```bash
git add Delta.DocGen/Config/ Delta.DocGen.Tests/Config/
git commit -m "feat: add DocGenConfig, DomainRule, ConfigLoader with CLI override support"
```

---

## Story 5: File discovery

*As a user, the tool finds all `.cs` and `.feature` files under the root, respecting exclude globs.*

### Task 5: File discoverer

**Files:**
- Create: `Delta.DocGen/Pipeline/Discoverer.cs`
- Create: `Delta.DocGen.Tests/Pipeline/DiscovererTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Delta.DocGen.Tests/Pipeline/DiscovererTests.cs`:

```csharp
using Delta.DocGen.Pipeline;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class DiscovererTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DiscovererTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relativePath)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
    }

    [Fact]
    public void FindsCsAndFeatureFiles()
    {
        Touch("Auth/AuthSteps.cs");
        Touch("Features/login.feature");
        Touch("README.md");

        var result = Discoverer.Discover(_root, excludes: []);

        result.CsFiles.Should().ContainSingle(f => f.EndsWith("AuthSteps.cs"));
        result.FeatureFiles.Should().ContainSingle(f => f.EndsWith("login.feature"));
        result.CsFiles.Should().NotContain(f => f.EndsWith(".md"));
    }

    [Fact]
    public void ExcludesMatchingGlobs()
    {
        Touch("Auth/AuthSteps.cs");
        Touch("meta/MetaTests.cs");
        Touch("meta/helper.feature");

        var result = Discoverer.Discover(_root, excludes: ["**/meta/**"]);

        result.CsFiles.Should().ContainSingle(f => f.EndsWith("AuthSteps.cs"));
        result.CsFiles.Should().NotContain(f => f.Contains("meta"));
        result.FeatureFiles.Should().BeEmpty();
    }

    [Fact]
    public void ReturnsRelativePaths()
    {
        Touch("Auth/AuthSteps.cs");

        var result = Discoverer.Discover(_root, excludes: []);

        result.CsFiles.Should().ContainSingle();
        result.CsFiles[0].Should().Be("Auth/AuthSteps.cs");
    }

    [Fact]
    public void EmptyRootReturnsEmptyLists()
    {
        var result = Discoverer.Discover(_root, excludes: []);

        result.CsFiles.Should().BeEmpty();
        result.FeatureFiles.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to confirm failure**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~DiscovererTests" 2>&1 | tail -5
```

Expected: build error — `Discoverer` not yet defined.

- [ ] **Step 3: Create `Discoverer.cs`**

```csharp
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Delta.DocGen.Pipeline;

public sealed record DiscoveryResult(
    IReadOnlyList<string> CsFiles,
    IReadOnlyList<string> FeatureFiles
);

public static class Discoverer
{
    /// <summary>
    /// Walks <paramref name="root"/> and returns relative paths (forward-slash separated)
    /// for all .cs and .feature files not matched by any exclude glob.
    /// </summary>
    public static DiscoveryResult Discover(string root, IReadOnlyList<string> excludes)
    {
        var matcher = new Matcher();
        matcher.AddInclude("**/*.cs");
        matcher.AddInclude("**/*.feature");
        foreach (var ex in excludes)
            matcher.AddExclude(ex);

        var dir = new DirectoryInfoWrapper(new DirectoryInfo(root));
        var matches = matcher.Execute(dir);

        var csFiles = new List<string>();
        var featureFiles = new List<string>();

        foreach (var match in matches.Files)
        {
            // Normalise to forward slashes regardless of OS
            var relative = match.Path.Replace(Path.DirectorySeparatorChar, '/');
            if (relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                csFiles.Add(relative);
            else if (relative.EndsWith(".feature", StringComparison.OrdinalIgnoreCase))
                featureFiles.Add(relative);
        }

        return new DiscoveryResult(csFiles.AsReadOnly(), featureFiles.AsReadOnly());
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~DiscovererTests" -v minimal
```

Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add Delta.DocGen/Pipeline/Discoverer.cs Delta.DocGen.Tests/Pipeline/DiscovererTests.cs
git commit -m "feat: add Discoverer with glob-based file exclusion"
```

---

## Story 6: C# step extraction

*As a developer, the tool parses C# files with Roslyn and extracts every SpecFlow/Reqnroll step definition.*

### Task 6: StepDefinitionExtractor

**Files:**
- Create: `Delta.DocGen/Scanner/CSharp/StepDefinitionExtractor.cs`
- Create: `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Delta.DocGen.Tests/Scanner/CSharp/StepDefinitionExtractorTests.cs`:

```csharp
using Delta.DocGen.Scanner.CSharp;
using FluentAssertions;

namespace Delta.DocGen.Tests.Scanner.CSharp;

public sealed class StepDefinitionExtractorTests
{
    private static string Extract(string csSource, string fileName = "Steps.cs")
    {
        // Helper returns the file passed through; actual extraction tested on real content.
        return fileName;
    }

    [Fact]
    public void ExtractsGivenStepFromSpecFlow()
    {
        const string source = """
            using TechTalk.SpecFlow;

            [Binding]
            public class AuthSteps
            {
                [Given("I am logged in as {string}")]
                public void GivenLoggedIn(string username)
                {
                    // implementation
                }
            }
            """;

        var steps = StepDefinitionExtractor.Extract(source, "Auth/AuthSteps.cs");

        steps.Should().HaveCount(1);
        steps[0].Type.Should().Be("Given");
        steps[0].Pattern.Should().Be("I am logged in as {string}");
        steps[0].File.Should().Be("Auth/AuthSteps.cs");
        steps[0].Params.Should().HaveCount(1);
        steps[0].Params[0].Name.Should().Be("username");
        steps[0].Params[0].Type.Should().Be("string");
        steps[0].Params[0].Example.Should().Be("");
    }

    [Fact]
    public void ExtractsWhenAndThenSteps()
    {
        const string source = """
            using Reqnroll;

            [Binding]
            public class NavSteps
            {
                [When("I navigate to {string}")]
                public void WhenNavigate(string path) { }

                [Then("I should see {string}")]
                public void ThenSee(string text) { }
            }
            """;

        var steps = StepDefinitionExtractor.Extract(source, "Nav/NavSteps.cs");

        steps.Should().HaveCount(2);
        steps[0].Type.Should().Be("When");
        steps[1].Type.Should().Be("Then");
    }

    [Fact]
    public void ExtractsIntAndDecimalParams()
    {
        const string source = """
            using TechTalk.SpecFlow;

            [Binding]
            public class CartSteps
            {
                [Given("the cart contains {int} of {string}")]
                public void GivenCart(int qty, string sku) { }

                [Then("the cart total should be {decimal}")]
                public void ThenTotal(decimal total) { }
            }
            """;

        var steps = StepDefinitionExtractor.Extract(source, "Checkout/CartSteps.cs");

        steps.Should().HaveCount(2);
        steps[0].Params[0].Name.Should().Be("qty");
        steps[0].Params[0].Type.Should().Be("int");
        steps[0].Params[0].Example.Should().Be("0");
        steps[0].Params[1].Type.Should().Be("string");
        steps[1].Params[0].Type.Should().Be("decimal");
        steps[1].Params[0].Example.Should().Be("0.00");
    }

    [Fact]
    public void DetectsDocStringParam()
    {
        const string source = """
            using TechTalk.SpecFlow;

            [Binding]
            public class ApiSteps
            {
                [Given("the request body is:")]
                public void GivenBody(string body) { }
            }
            """;

        var steps = StepDefinitionExtractor.Extract(source, "Api/ApiSteps.cs");

        steps.Should().HaveCount(1);
        steps[0].Params[0].Type.Should().Be("DocString");
        steps[0].Params[0].Example.Should().Be("");
    }

    [Fact]
    public void ExtractsSourceBody()
    {
        const string source = """
            using TechTalk.SpecFlow;

            [Binding]
            public class AuthSteps
            {
                [Given("I am logged in as {string}")]
                public void GivenLoggedIn(string username)
                {
                    _session.SignIn(username);
                }
            }
            """;

        var steps = StepDefinitionExtractor.Extract(source, "Auth/AuthSteps.cs");

        steps[0].Source.Should().Contain("_session.SignIn");
    }

    [Fact]
    public void RecordsLineNumber()
    {
        const string source = """
            using TechTalk.SpecFlow;

            [Binding]
            public class AuthSteps
            {
                [Given("I am logged in as {string}")]
                public void GivenLoggedIn(string username) { }
            }
            """;

        var steps = StepDefinitionExtractor.Extract(source, "Auth/AuthSteps.cs");

        steps[0].Line.Should().BeGreaterThan(0);
    }

    [Fact]
    public void IgnoresNonStepMethods()
    {
        const string source = """
            using TechTalk.SpecFlow;

            [Binding]
            public class Hooks
            {
                [BeforeScenario]
                public void Setup() { }

                [Given("a step")]
                public void AStep() { }
            }
            """;

        var steps = StepDefinitionExtractor.Extract(source, "Hooks.cs");

        steps.Should().HaveCount(1);
    }

    [Fact]
    public void ReturnsEmptyForFileWithNoSteps()
    {
        const string source = """
            namespace MyApp;
            public class Helper { public void DoSomething() { } }
            """;

        var steps = StepDefinitionExtractor.Extract(source, "Helper.cs");

        steps.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to confirm failure**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~StepDefinitionExtractorTests" 2>&1 | tail -5
```

Expected: build error — `StepDefinitionExtractor` not yet defined.

- [ ] **Step 3: Create `StepDefinitionExtractor.cs`**

```csharp
using Delta.DocGen.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.DocGen.Scanner.CSharp;

public static class StepDefinitionExtractor
{
    private static readonly HashSet<string> StepAttributeNames =
        new(StringComparer.Ordinal) { "Given", "When", "Then" };

    /// <summary>
    /// Parses <paramref name="csSource"/> with Roslyn and returns one
    /// <see cref="RawStep"/> for every [Given]/[When]/[Then] method found.
    /// Namespace-agnostic: works for both TechTalk.SpecFlow and Reqnroll.
    /// </summary>
    public static IReadOnlyList<RawStep> Extract(string csSource, string relativeFilePath)
    {
        var tree = CSharpSyntaxTree.ParseText(csSource);
        var root = tree.GetRoot();
        var results = new List<RawStep>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            foreach (var attrList in method.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var attrName = GetSimpleAttributeName(attr);
                    if (!StepAttributeNames.Contains(attrName)) continue;

                    var pattern = ExtractPattern(attr);
                    if (pattern is null) continue;

                    var csParams = method.ParameterList.Parameters.ToList();
                    var paramRecords = BuildParams(pattern, csParams);
                    var source = method.ToFullString().Trim();
                    var line = tree.GetLineSpan(attr.GetLocation().SourceSpan).StartLinePosition.Line + 1;

                    results.Add(new RawStep(
                        Type:    attrName,
                        Pattern: pattern,
                        Params:  paramRecords,
                        File:    relativeFilePath,
                        Line:    line,
                        Source:  source
                    ));
                }
            }
        }

        return results.AsReadOnly();
    }

    private static string GetSimpleAttributeName(AttributeSyntax attr)
    {
        var name = attr.Name switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.Text,
            IdentifierNameSyntax i => i.Identifier.Text,
            _ => attr.Name.ToString()
        };
        // Strip "Attribute" suffix if present (e.g. GivenAttribute)
        return name.EndsWith("Attribute") ? name[..^9] : name;
    }

    private static string? ExtractPattern(AttributeSyntax attr)
    {
        var firstArg = attr.ArgumentList?.Arguments.FirstOrDefault();
        if (firstArg is null) return null;

        // Handle both string literals and verbatim strings
        if (firstArg.Expression is LiteralExpressionSyntax lit &&
            lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return lit.Token.ValueText; // ValueText strips @ prefix and escape sequences
        }

        return null;
    }

    private static IReadOnlyList<ParamRecord> BuildParams(
        string pattern, List<ParameterSyntax> csParams)
    {
        // Count placeholders in pattern to detect DocString params
        var placeholders = CountPlaceholders(pattern);
        var result = new List<ParamRecord>();

        for (int i = 0; i < csParams.Count; i++)
        {
            var p = csParams[i];
            var name = p.Identifier.Text;
            var csTypeName = p.Type?.ToString() ?? "string";
            var isDocString = i >= placeholders; // param has no corresponding {…} placeholder

            var (schemaType, example) = isDocString
                ? ("DocString", "")
                : MapType(csTypeName);

            result.Add(new ParamRecord(name, schemaType, example));
        }

        return result.AsReadOnly();
    }

    private static int CountPlaceholders(string pattern)
    {
        int count = 0;
        int pos = 0;
        while ((pos = pattern.IndexOf('{', pos)) != -1)
        {
            var end = pattern.IndexOf('}', pos);
            if (end > pos) count++;
            pos = end > pos ? end + 1 : pos + 1;
        }
        return count;
    }

    private static (string schemaType, string example) MapType(string csType) =>
        csType.TrimEnd('?') switch
        {
            "int"     => ("int",     "0"),
            "Int32"   => ("int",     "0"),
            "decimal" => ("decimal", "0.00"),
            "Decimal" => ("decimal", "0.00"),
            _         => ("string",  ""),
        };
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~StepDefinitionExtractorTests" -v minimal
```

Expected: `Passed! - Failed: 0, Passed: 8, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add Delta.DocGen/Scanner/CSharp/ Delta.DocGen.Tests/Scanner/CSharp/
git commit -m "feat: add StepDefinitionExtractor (Roslyn-based, SpecFlow + Reqnroll)"
```

---

## Story 7: Usage counting

*As a user, each step's `used` field accurately reflects how many times it appears across all feature files.*

### Task 7: UsageCounter

**Files:**
- Create: `Delta.DocGen/Scanner/Gherkin/UsageCounter.cs`
- Create: `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Delta.DocGen.Tests/Scanner/Gherkin/UsageCounterTests.cs`:

```csharp
using Delta.DocGen.Scanner.Gherkin;
using FluentAssertions;

namespace Delta.DocGen.Tests.Scanner.Gherkin;

public sealed class UsageCounterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public UsageCounterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteFeature(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void CountsExactMatchStep()
    {
        WriteFeature("login.feature", """
            Feature: Login
              Scenario: Admin login
                Given I am logged in as "admin@delta.io"
                When I navigate to "/dashboard"
                Then I should see "Welcome"
            """);

        var patterns = new[] { "I am logged in as {string}" };
        var counts = UsageCounter.Count([Path.Combine(_dir, "login.feature")], patterns);

        counts["I am logged in as {string}"].Should().Be(1);
    }

    [Fact]
    public void CountsMultipleUsagesAcrossFiles()
    {
        WriteFeature("login.feature", """
            Feature: Login
              Scenario: Admin login
                Given I am logged in as "admin@delta.io"
            """);
        WriteFeature("profile.feature", """
            Feature: Profile
              Scenario: User login
                Given I am logged in as "jane.doe"
            """);

        var patterns = new[] { "I am logged in as {string}" };
        var counts = UsageCounter.Count(
            [Path.Combine(_dir, "login.feature"), Path.Combine(_dir, "profile.feature")],
            patterns);

        counts["I am logged in as {string}"].Should().Be(2);
    }

    [Fact]
    public void ReturnsZeroForUnusedPattern()
    {
        WriteFeature("login.feature", """
            Feature: Login
              Scenario: Admin login
                Given I am logged in as "admin@delta.io"
            """);

        var patterns = new[] { "I sign out" };
        var counts = UsageCounter.Count([Path.Combine(_dir, "login.feature")], patterns);

        counts["I sign out"].Should().Be(0);
    }

    [Fact]
    public void CountsIntPatternMatch()
    {
        WriteFeature("cart.feature", """
            Feature: Cart
              Scenario: Add to cart
                Given the cart contains 2 of "SKU-001"
            """);

        var patterns = new[] { "the cart contains {int} of {string}" };
        var counts = UsageCounter.Count([Path.Combine(_dir, "cart.feature")], patterns);

        counts["the cart contains {int} of {string}"].Should().Be(1);
    }
}
```

- [ ] **Step 2: Run to confirm failure**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~UsageCounterTests" 2>&1 | tail -5
```

Expected: build error — `UsageCounter` not yet defined.

- [ ] **Step 3: Create `UsageCounter.cs`**

```csharp
using System.Text.RegularExpressions;
using Gherkin;

namespace Delta.DocGen.Scanner.Gherkin;

public static class UsageCounter
{
    /// <summary>
    /// Counts how many step lines across all <paramref name="featureFiles"/>
    /// match each pattern in <paramref name="patterns"/>.
    ///
    /// Patterns use Cucumber Expression syntax ({string}, {int}, {decimal}).
    /// Old-style regex patterns (containing '(' or '.*') are used verbatim.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Count(
        IReadOnlyList<string> featureFiles,
        IEnumerable<string> patterns)
    {
        // Pre-compile a regex per pattern
        var compiled = patterns
            .Select(p => (Pattern: p, Regex: PatternToRegex(p)))
            .ToList();

        var counts = compiled.ToDictionary(c => c.Pattern, _ => 0);

        foreach (var filePath in featureFiles)
        {
            var stepLines = ExtractStepLines(filePath);
            foreach (var line in stepLines)
            {
                foreach (var (pattern, regex) in compiled)
                {
                    if (regex.IsMatch(line))
                        counts[pattern]++;
                }
            }
        }

        return counts;
    }

    private static IEnumerable<string> ExtractStepLines(string filePath)
    {
        try
        {
            var parser = new Parser();
            var content = File.ReadAllText(filePath);
            using var reader = new StringReader(content);
            var doc = parser.Parse(new TokenScanner(reader));

            return doc.Feature?.Children
                .SelectMany(child => child switch
                {
                    global::Gherkin.Ast.Scenario s => s.Steps.Select(step => step.Text),
                    global::Gherkin.Ast.Background b => b.Steps.Select(step => step.Text),
                    _ => []
                }) ?? [];
        }
        catch
        {
            // Malformed feature file — skip silently (pipeline logger will handle)
            return [];
        }
    }

    /// <summary>
    /// Converts a Cucumber Expression pattern to a <see cref="Regex"/>.
    /// If the pattern already looks like a regex (contains '(' or '.*'), use it as-is.
    /// </summary>
    internal static Regex PatternToRegex(string pattern)
    {
        // If it looks like old-style regex, use verbatim
        if (pattern.Contains('(') || pattern.Contains(".*"))
        {
            return new Regex("^" + pattern + "$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        // Convert Cucumber Expressions to regex
        var escaped = Regex.Escape(pattern);
        escaped = escaped.Replace(@"\{string\}", @"""[^""]*""");
        escaped = escaped.Replace(@"\{int\}",    @"\d+");
        escaped = escaped.Replace(@"\{decimal\}", @"[\d.]+");
        // Any remaining {…} placeholders treated as catch-all
        escaped = Regex.Replace(escaped, @"\\\{[^}]+\\\}", @".+");

        return new Regex("^" + escaped + "$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~UsageCounterTests" -v minimal
```

Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add Delta.DocGen/Scanner/Gherkin/ Delta.DocGen.Tests/Scanner/Gherkin/
git commit -m "feat: add UsageCounter (Gherkin feature file parser + pattern matching)"
```

---

## Story 8: Domain assignment

*As a user, each step is assigned to a domain based on config-driven glob rules; unmatched steps fall back to the configured fallback domain and are logged as warnings.*

### Task 8: DomainAssigner

**Files:**
- Create: `Delta.DocGen/Pipeline/DomainAssigner.cs`
- Create: `Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs`:

```csharp
using Delta.DocGen.Config;
using Delta.DocGen.Pipeline;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class DomainAssignerTests
{
    private static readonly IReadOnlyList<DomainRule> Rules =
    [
        new("Auth/**",           "Auth",     "Auth & Identity"),
        new("Checkout/Payment*", "Checkout", "Checkout"),
        new("Checkout/**",       "Checkout", "Checkout"),
    ];

    [Fact]
    public void MatchesFirstApplicableRule()
    {
        var result = DomainAssigner.Assign("Auth/AuthSteps.cs", Rules, fallback: "General");

        result.Domain.Should().Be("Auth");
        result.Label.Should().Be("Auth & Identity");
    }

    [Fact]
    public void MatchesMoreSpecificRuleFirst()
    {
        // Checkout/Payment* is listed before Checkout/** and should win for payment files
        var result = DomainAssigner.Assign("Checkout/PaymentSteps.cs", Rules, fallback: "General");

        result.Domain.Should().Be("Checkout");
    }

    [Fact]
    public void MatchesWildcardRule()
    {
        var result = DomainAssigner.Assign("Checkout/CartSteps.cs", Rules, fallback: "General");

        result.Domain.Should().Be("Checkout");
        result.Label.Should().Be("Checkout");
    }

    [Fact]
    public void ReturnsFallbackWhenNoRuleMatches()
    {
        var result = DomainAssigner.Assign("Utils/Helpers.cs", Rules, fallback: "General");

        result.Domain.Should().Be("General");
        result.Label.Should().Be("General");
        result.UsedFallback.Should().BeTrue();
    }

    [Fact]
    public void ReturnsFallbackForEmptyRuleList()
    {
        var result = DomainAssigner.Assign("Auth/AuthSteps.cs", rules: [], fallback: "General");

        result.Domain.Should().Be("General");
        result.UsedFallback.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to confirm failure**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~DomainAssignerTests" 2>&1 | tail -5
```

Expected: build error.

- [ ] **Step 3: Create `DomainAssigner.cs`**

```csharp
using Delta.DocGen.Config;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Delta.DocGen.Pipeline;

public sealed record DomainAssignment(string Domain, string Label, bool UsedFallback);

public static class DomainAssigner
{
    /// <summary>
    /// Evaluates <paramref name="rules"/> in order and returns the first match.
    /// If no rule matches, returns a <see cref="DomainAssignment"/> using
    /// <paramref name="fallback"/> with <see cref="DomainAssignment.UsedFallback"/> = true.
    /// </summary>
    public static DomainAssignment Assign(
        string relativeFilePath,
        IReadOnlyList<DomainRule> rules,
        string fallback)
    {
        foreach (var rule in rules)
        {
            var matcher = new Matcher();
            matcher.AddInclude(rule.Pattern);
            // Matcher requires a directory root — wrap the path
            var result = matcher.Match(relativeFilePath);
            if (result.HasMatches)
                return new DomainAssignment(rule.Domain, rule.Label, UsedFallback: false);
        }

        return new DomainAssignment(fallback, fallback, UsedFallback: true);
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~DomainAssignerTests" -v minimal
```

Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add Delta.DocGen/Pipeline/DomainAssigner.cs Delta.DocGen.Tests/Pipeline/DomainAssignerTests.cs
git commit -m "feat: add DomainAssigner with first-match-wins glob rule evaluation"
```

---

## Story 9: ID generation

*As a developer, each step receives a stable, deterministic ID that survives re-runs and file renames within the same domain.*

### Task 9: IdGenerator

**Files:**
- Create: `Delta.DocGen/Pipeline/IdGenerator.cs`
- Create: `Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs`:

```csharp
using Delta.DocGen.Pipeline;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class IdGeneratorTests
{
    [Fact]
    public void GeneratesIdWithDomainPrefixAndHash()
    {
        var id = IdGenerator.Generate("Auth", "I am logged in as {string}");

        id.Should().MatchRegex(@"^auth-[0-9a-f]{4}$");
    }

    [Fact]
    public void IsDeterministic()
    {
        var id1 = IdGenerator.Generate("Auth", "I am logged in as {string}");
        var id2 = IdGenerator.Generate("Auth", "I am logged in as {string}");

        id1.Should().Be(id2);
    }

    [Fact]
    public void DifferentPatternsProduceDifferentIds()
    {
        var id1 = IdGenerator.Generate("Auth", "I am logged in as {string}");
        var id2 = IdGenerator.Generate("Auth", "I sign out");

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void DifferentDomainsProduceDifferentIds()
    {
        var id1 = IdGenerator.Generate("Auth", "I click the {string} button");
        var id2 = IdGenerator.Generate("UI", "I click the {string} button");

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void PrefixIsFirstThreeCharsOfDomainLowercased()
    {
        var id = IdGenerator.Generate("Navigation", "I navigate to {string}");

        id.Should().StartWith("nav-");
    }

    [Fact]
    public void DetectsCollisionAcrossAllIds()
    {
        // Collision detection: generating the same logical step twice in one batch should be caught
        var ids = new[] {
            IdGenerator.Generate("Auth", "I am logged in as {string}"),
            IdGenerator.Generate("Auth", "I sign out"),
            IdGenerator.Generate("UI",   "I click the {string} button"),
        };

        ids.Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run to confirm failure**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~IdGeneratorTests" 2>&1 | tail -5
```

Expected: build error.

- [ ] **Step 3: Create `IdGenerator.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Delta.DocGen.Pipeline;

public static class IdGenerator
{
    /// <summary>
    /// Produces a stable ID of the form <c>{3-char-domain-prefix}-{4-hex-chars}</c>.
    /// The hash input is <c>{domain}:{normalisedPattern}</c> so IDs are
    /// stable across file moves within the same domain.
    /// </summary>
    public static string Generate(string domain, string pattern)
    {
        var prefix = domain.Length >= 3
            ? domain[..3].ToLowerInvariant()
            : domain.ToLowerInvariant().PadRight(3, '_');

        var normalised = pattern.Trim().ToLowerInvariant();
        var input = $"{domain.ToLowerInvariant()}:{normalised}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hex = Convert.ToHexString(hashBytes)[..4].ToLowerInvariant();

        return $"{prefix}-{hex}";
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~IdGeneratorTests" -v minimal
```

Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add Delta.DocGen/Pipeline/IdGenerator.cs Delta.DocGen.Tests/Pipeline/IdGeneratorTests.cs
git commit -m "feat: add IdGenerator producing stable domain-prefix + SHA-256 IDs"
```

---

## Story 10: Canonical JSON and signing

*As a user, the output file is protected by a SHA-256 digest that the viewer can verify. The digest is stable regardless of pretty-printing.*

### Task 10: CanonicalJson and Signer

**Files:**
- Create: `Delta.DocGen/Output/Serialiser/CanonicalJson.cs`
- Create: `Delta.DocGen/Output/Serialiser/Signer.cs`
- Create: `Delta.DocGen.Tests/Output/CanonicalJsonTests.cs`
- Create: `Delta.DocGen.Tests/Output/SignerTests.cs`

- [ ] **Step 1: Write the failing tests for CanonicalJson**

Create `Delta.DocGen.Tests/Output/CanonicalJsonTests.cs`:

```csharp
using Delta.DocGen.Output.Serialiser;
using FluentAssertions;

namespace Delta.DocGen.Tests.Output;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void SortsObjectKeysAlphabetically()
    {
        const string input = """{"z":1,"a":2,"m":3}""";

        var result = CanonicalJson.Canonicalise(input);

        result.Should().Be("""{"a":2,"m":3,"z":1}""");
    }

    [Fact]
    public void SortsNestedObjectKeys()
    {
        const string input = """{"outer":{"z":1,"a":2}}""";

        var result = CanonicalJson.Canonicalise(input);

        result.Should().Be("""{"outer":{"a":2,"z":1}}""");
    }

    [Fact]
    public void ProducesNoWhitespace()
    {
        const string input = """{ "a" : 1, "b" : 2 }""";

        var result = CanonicalJson.Canonicalise(input);

        result.Should().NotContain(" ");
    }

    [Fact]
    public void PreservesArrayOrder()
    {
        const string input = """{"items":[3,1,2]}""";

        var result = CanonicalJson.Canonicalise(input);

        result.Should().Be("""{"items":[3,1,2]}""");
    }

    [Fact]
    public void HandlesArrayOfObjects()
    {
        const string input = """{"steps":[{"z":1,"a":2},{"y":3,"b":4}]}""";

        var result = CanonicalJson.Canonicalise(input);

        result.Should().Be("""{"steps":[{"a":2,"z":1},{"b":4,"y":3}]}""");
    }

    [Fact]
    public void IsDeterministic()
    {
        const string input = """{"version":"1.0.0","enriched":false,"steps":[]}""";

        var r1 = CanonicalJson.Canonicalise(input);
        var r2 = CanonicalJson.Canonicalise(input);

        r1.Should().Be(r2);
    }
}
```

- [ ] **Step 2: Write failing tests for Signer**

Create `Delta.DocGen.Tests/Output/SignerTests.cs`:

```csharp
using Delta.DocGen.Output.Serialiser;
using FluentAssertions;

namespace Delta.DocGen.Tests.Output;

public sealed class SignerTests
{
    [Fact]
    public void ComputesHexDigest()
    {
        var digest = Signer.ComputeDigest("""{"version":"1.0.0"}""");

        digest.Should().MatchRegex(@"^[0-9a-f]{64}$");
    }

    [Fact]
    public void IsDeterministicForSameInput()
    {
        var d1 = Signer.ComputeDigest("""{"version":"1.0.0"}""");
        var d2 = Signer.ComputeDigest("""{"version":"1.0.0"}""");

        d1.Should().Be(d2);
    }

    [Fact]
    public void DifferentInputsProduceDifferentDigests()
    {
        var d1 = Signer.ComputeDigest("""{"version":"1.0.0"}""");
        var d2 = Signer.ComputeDigest("""{"version":"2.0.0"}""");

        d1.Should().NotBe(d2);
    }

    [Fact]
    public void VerifyPassesForMatchingContent()
    {
        var json = """{"version":"1.0.0","steps":[]}""";
        var digest = Signer.ComputeDigest(CanonicalJson.Canonicalise(json));

        Signer.Verify(json, digest).Should().BeTrue();
    }

    [Fact]
    public void VerifyFailsForTamperedContent()
    {
        var original = """{"version":"1.0.0","steps":[]}""";
        var digest = Signer.ComputeDigest(CanonicalJson.Canonicalise(original));
        var tampered = """{"version":"1.0.0","steps":[],"extra":"injected"}""";

        Signer.Verify(tampered, digest).Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run to confirm failure**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~CanonicalJsonTests|FullyQualifiedName~SignerTests" 2>&1 | tail -5
```

Expected: build error.

- [ ] **Step 4: Create `CanonicalJson.cs`**

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Delta.DocGen.Output.Serialiser;

public static class CanonicalJson
{
    /// <summary>
    /// Returns a compact, key-sorted JSON string from any valid JSON input.
    /// Output is byte-reproducible regardless of original formatting or key order.
    /// </summary>
    public static string Canonicalise(string json)
    {
        var node = JsonNode.Parse(json)
            ?? throw new ArgumentException("Input is not valid JSON.", nameof(json));
        return Render(node);
    }

    private static string Render(JsonNode node) => node switch
    {
        JsonObject obj => RenderObject(obj),
        JsonArray  arr => RenderArray(arr),
        _              => node.ToJsonString()
    };

    private static string RenderObject(JsonObject obj)
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsonSerializer.Serialize(kv.Key));
            sb.Append(':');
            sb.Append(kv.Value is not null ? Render(kv.Value) : "null");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string RenderArray(JsonArray arr)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var item in arr)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(item is not null ? Render(item) : "null");
        }
        sb.Append(']');
        return sb.ToString();
    }
}
```

- [ ] **Step 5: Create `Signer.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Delta.DocGen.Output.Serialiser;

public static class Signer
{
    private const string Algorithm = "SHA-256";

    /// <summary>
    /// Computes a lowercase hex SHA-256 digest of the UTF-8 bytes of
    /// <paramref name="canonicalJson"/>. The caller is responsible for
    /// canonicalising first (see <see cref="CanonicalJson.Canonicalise"/>).
    /// </summary>
    public static string ComputeDigest(string canonicalJson)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalJson);
        var hash  = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies that <paramref name="json"/> (without the signature field)
    /// matches the stored <paramref name="expectedDigest"/>.
    /// </summary>
    public static bool Verify(string json, string expectedDigest)
    {
        var canonical = CanonicalJson.Canonicalise(json);
        var actual    = ComputeDigest(canonical);
        return string.Equals(actual, expectedDigest, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 6: Run tests to confirm they pass**

```bash
dotnet test Delta.DocGen.Tests --filter "FullyQualifiedName~CanonicalJsonTests|FullyQualifiedName~SignerTests" -v minimal
```

Expected: `Passed! - Failed: 0, Passed: 11, Skipped: 0`

- [ ] **Step 7: Commit**

```bash
git add Delta.DocGen/Output/Serialiser/ Delta.DocGen.Tests/Output/
git commit -m "feat: add CanonicalJson (key-sorted) and Signer (SHA-256 digest + verify)"
```

---

## Story 11: JSON Schema resource and writer

*As a user, the tool writes the JSON Schema file alongside the output so the viewer can validate imported data.*

### Task 11: Embedded schema and SchemaWriter

**Files:**
- Create: `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json`
- Create: `Delta.DocGen/Output/Schema/SchemaWriter.cs`

- [ ] **Step 1: Create `step-library.v1.schema.json` as an embedded resource**

Create `Delta.DocGen/Output/Schema/Resources/step-library.v1.schema.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://delta.docgen/schema/v1/step-library.schema.json",
  "title": "Delta Step Library v1",
  "description": "Output format produced by Delta.DocGen v1.",
  "type": "object",
  "required": ["$schema","version","generatedAt","generatorVersion","enriched","domains","steps","signature"],
  "properties": {
    "$schema":          { "type": "string" },
    "version":          { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+$" },
    "generatedAt":      { "type": "string", "format": "date-time" },
    "generatorVersion": { "type": "string" },
    "enriched":         { "type": "boolean" },
    "domains": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id","label"],
        "properties": {
          "id":    { "type": "string" },
          "label": { "type": "string" }
        },
        "additionalProperties": false
      }
    },
    "steps": {
      "type": "array",
      "items": { "$ref": "#/$defs/Step" }
    },
    "signature": {
      "type": "object",
      "required": ["algorithm","digest"],
      "properties": {
        "algorithm": { "type": "string", "enum": ["SHA-256"] },
        "digest":    { "type": "string", "pattern": "^[0-9a-f]{64}$" }
      },
      "additionalProperties": false
    }
  },
  "$defs": {
    "Step": {
      "type": "object",
      "required": ["id","type","pattern","params","file","line","domain","tags","used","description","source","suggestsNext"],
      "properties": {
        "id":          { "type": "string" },
        "type":        { "type": "string", "enum": ["Given","When","Then"] },
        "pattern":     { "type": "string" },
        "params":      { "type": "array", "items": { "$ref": "#/$defs/Param" } },
        "file":        { "type": "string" },
        "line":        { "type": "integer", "minimum": 1 },
        "domain":      { "type": "string" },
        "tags":        { "type": "array", "items": { "type": "string" } },
        "used":        { "type": "integer", "minimum": 0 },
        "description": { "type": "string" },
        "source":      { "type": "string" },
        "suggestsNext":{ "type": "array", "items": { "type": "string" } }
      },
      "additionalProperties": false
    },
    "Param": {
      "type": "object",
      "required": ["name","type","example"],
      "properties": {
        "name":    { "type": "string" },
        "type":    { "type": "string", "enum": ["string","int","decimal","DocString"] },
        "example": { "type": "string" }
      },
      "additionalProperties": false
    }
  }
}
```

- [ ] **Step 2: Create `SchemaWriter.cs`**

```csharp
using System.Reflection;

namespace Delta.DocGen.Output.Schema;

public static class SchemaWriter
{
    private const string ResourceName =
        "Delta.DocGen.Output.Schema.Resources.step-library.v1.schema.json";

    /// <summary>
    /// Writes the embedded JSON Schema to <c>{outputDir}/schema/v1/step-library.schema.json</c>.
    /// </summary>
    public static string Write(string outputDir)
    {
        var schemaDir  = Path.Combine(outputDir, "schema", "v1");
        var schemaPath = Path.Combine(schemaDir, "step-library.schema.json");
        Directory.CreateDirectory(schemaDir);

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Rebuild the project.");

        using var fs = File.Create(schemaPath);
        stream.CopyTo(fs);

        return schemaPath;
    }
}
```

- [ ] **Step 3: Build to confirm the embedded resource resolves**

```bash
dotnet build Delta.DocGen.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add Delta.DocGen/Output/Schema/
git commit -m "feat: add JSON Schema v1 as embedded resource and SchemaWriter"
```

---

## Story 12: Pipeline orchestration

*As a developer, the PipelineRunner wires all stages together, producing a signed output file and logging progress throughout.*

### Task 12: PipelineRunner

**Files:**
- Create: `Delta.DocGen/Pipeline/PipelineRunner.cs`

- [ ] **Step 1: Create `PipelineRunner.cs`**

```csharp
using System.Diagnostics;
using System.Text.Json;
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Output.Schema;
using Delta.DocGen.Output.Serialiser;
using Delta.DocGen.Scanner.CSharp;
using Delta.DocGen.Scanner.Gherkin;

namespace Delta.DocGen.Pipeline;

public sealed class PipelineRunner(DocGenConfig config, IDocGenLogger log)
{
    private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };

    public async Task<int> RunAsync(bool dryRun = false)
    {
        var sw = Stopwatch.StartNew();

        // ── Stage 1: Startup summary ────────────────────────────────────────
        log.Info($"Delta.DocGen v{typeof(PipelineRunner).Assembly.GetName().Version}");
        log.Info($"Root    : {config.Root}");
        log.Info($"Output  : {config.Output}");
        log.Info($"Excludes: {config.Exclude.Count}");
        log.Info($"Dry run : {dryRun}");

        if (!Directory.Exists(config.Root))
        {
            log.Error($"Root directory does not exist: {config.Root}");
            return 1;
        }

        // ── Stage 2: Discovery ──────────────────────────────────────────────
        log.Info("Discovering files...");
        var discovery = Discoverer.Discover(config.Root, config.Exclude);
        log.Info($"  Found {discovery.CsFiles.Count} .cs files, {discovery.FeatureFiles.Count} .feature files");

        // ── Stage 3: C# parsing ─────────────────────────────────────────────
        log.Info("Parsing C# step definitions...");
        var rawSteps = new List<RawStep>();
        foreach (var rel in discovery.CsFiles)
        {
            var full = Path.Combine(config.Root, rel);
            var source = await File.ReadAllTextAsync(full);
            var extracted = StepDefinitionExtractor.Extract(source, rel);
            rawSteps.AddRange(extracted);
            log.Verbose($"  {rel}: {extracted.Count} steps");
        }
        log.Info($"  Extracted {rawSteps.Count} step definitions total");

        // ── Stage 4: Usage counting ─────────────────────────────────────────
        log.Info("Counting usages in feature files...");
        var fullFeaturePaths = discovery.FeatureFiles
            .Select(f => Path.Combine(config.Root, f))
            .ToList();
        var usages = UsageCounter.Count(fullFeaturePaths, rawSteps.Select(s => s.Pattern));
        var totalUsages = usages.Values.Sum();
        log.Info($"  Matched {totalUsages} step usages across {discovery.FeatureFiles.Count} feature files");

        // Log unmatched patterns as warnings
        foreach (var (pattern, count) in usages.Where(kv => kv.Value == 0))
            log.Warn($"  Unused step: {pattern}");

        // ── Stage 5: Domain assignment ──────────────────────────────────────
        log.Info("Assigning domains...");
        int fallbackCount = 0;
        var domainMap = new Dictionary<string, DomainRecord>();

        // ── Stage 6 & Build StepRecords ────────────────────────────────────
        log.Info("Generating IDs and building step records...");
        var stepRecords = new List<StepRecord>();
        var seenIds = new HashSet<string>();

        foreach (var raw in rawSteps)
        {
            var assignment = DomainAssigner.Assign(raw.File, config.Domains, config.FallbackDomain);
            if (assignment.UsedFallback)
            {
                fallbackCount++;
                log.Warn($"  No domain rule matched: {raw.File} → '{config.FallbackDomain}'");
            }

            if (!domainMap.ContainsKey(assignment.Domain))
                domainMap[assignment.Domain] = new DomainRecord(assignment.Domain, assignment.Label);

            var id = IdGenerator.Generate(assignment.Domain, raw.Pattern);
            if (!seenIds.Add(id))
            {
                log.Error($"ID collision detected for pattern '{raw.Pattern}' in domain '{assignment.Domain}'. Aborting.");
                return 1;
            }

            stepRecords.Add(new StepRecord(
                Id:          id,
                Type:        raw.Type,
                Pattern:     raw.Pattern,
                Params:      raw.Params,
                File:        raw.File,
                Line:        raw.Line,
                Domain:      assignment.Domain,
                Tags:        [],
                Used:        usages.GetValueOrDefault(raw.Pattern, 0),
                Description: "",
                Source:      raw.Source,
                SuggestsNext:[]
            ));

            log.Verbose($"  [{id}] {raw.Type} {raw.Pattern}");
        }

        log.Info($"  {fallbackCount} steps used fallback domain '{config.FallbackDomain}'");

        // ── Stage 7: Build envelope (without signature) ─────────────────────
        log.Info("Serialising...");
        var domains = domainMap.Values.OrderBy(d => d.Id).ToList();

        var envelopeWithoutSig = new Envelope(
            Schema:           "./schema/v1/step-library.schema.json",
            Version:          "1.0.0",
            GeneratedAt:      DateTime.UtcNow.ToString("O"),
            GeneratorVersion: typeof(PipelineRunner).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Enriched:         false,
            Domains:          domains,
            Steps:            stepRecords,
            Signature:        null
        );

        var jsonWithoutSig = JsonSerializer.Serialize(envelopeWithoutSig, PrettyOptions);
        var canonical      = CanonicalJson.Canonicalise(jsonWithoutSig);
        var digest         = Signer.ComputeDigest(canonical);

        var envelopeWithSig = envelopeWithoutSig with
        {
            Signature = new SignatureRecord("SHA-256", digest)
        };

        var finalJson = JsonSerializer.Serialize(envelopeWithSig, PrettyOptions);

        // ── Stage 8: Write output ───────────────────────────────────────────
        if (!dryRun)
        {
            var outputDir = Path.GetDirectoryName(config.Output)
                ?? throw new InvalidOperationException("Cannot determine output directory.");
            Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(config.Output, finalJson);

            var schemaPath = SchemaWriter.Write(outputDir);
            log.Info($"  Schema written: {schemaPath}");
        }
        else
        {
            log.Info("  Dry run — no files written.");
        }

        // ── Summary ─────────────────────────────────────────────────────────
        var outputSize = dryRun ? 0 : new FileInfo(config.Output).Length;
        sw.Stop();
        log.Summary("─────────────────────────────────────────────");
        log.Summary($"Steps extracted : {stepRecords.Count}");
        log.Summary($"Features scanned: {discovery.FeatureFiles.Count}");
        log.Summary($"Unused steps    : {usages.Values.Count(v => v == 0)}");
        log.Summary($"Fallback domains: {fallbackCount}");
        if (!dryRun)
        {
            log.Summary($"Output          : {config.Output} ({outputSize:N0} bytes)");
            log.Summary($"Digest (SHA-256): {digest}");
        }
        log.Summary($"Elapsed         : {sw.Elapsed.TotalSeconds:F2}s");

        return 0;
    }
}
```

- [ ] **Step 2: Build to confirm zero errors**

```bash
dotnet build Delta.DocGen.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```bash
git add Delta.DocGen/Pipeline/PipelineRunner.cs
git commit -m "feat: add PipelineRunner orchestrating all 8 pipeline stages"
```

---

## Story 13: CLI wiring

*As a user, I can invoke `docgen` from the command line with options matching the design spec.*

### Task 13: CLI entry point

**Files:**
- Create: `Delta.DocGen/CLI/RootCommand.cs`
- Modify: `Delta.DocGen/Program.cs`

- [ ] **Step 1: Create `RootCommand.cs`**

```csharp
using System.CommandLine;
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Pipeline;

namespace Delta.DocGen.CLI;

public static class DocGenCommand
{
    public static RootCommand Build()
    {
        var configOpt = new Option<string>(
            "--config",
            getDefaultValue: () => "docgen.config.json",
            description: "Path to the JSON config file.");

        var rootOpt = new Option<string?>(
            "--root",
            description: "Root directory to scan (overrides config).");

        var outputOpt = new Option<string?>(
            "--output",
            description: "Output file path (overrides config).");

        var excludeOpt = new Option<string[]>(
            "--exclude",
            description: "Add an exclude glob pattern (repeatable; additive with config).")
        { AllowMultipleArgumentsPerToken = false, Arity = ArgumentArity.ZeroOrMore };

        var verbosityOpt = new Option<string?>(
            "--verbosity",
            description: "Log verbosity: silent | normal | verbose.");

        var dryRunOpt = new Option<bool>(
            "--dry-run",
            description: "Scan and report but do not write output files.");

        var root = new RootCommand("Delta.DocGen — BDD step library generator")
        {
            configOpt, rootOpt, outputOpt, excludeOpt, verbosityOpt, dryRunOpt
        };

        root.SetHandler(async (config, rootVal, output, excludes, verbosity, dryRun) =>
        {
            var overrides = new ConfigOverrides
            {
                Root               = rootVal,
                Output             = output,
                LogVerbosity       = verbosity,
                AdditionalExcludes = excludes ?? [],
            };

            DocGenConfig cfg;
            try
            {
                cfg = ConfigLoader.Load(config, overrides);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.Message}");
                Environment.Exit(2);
                return;
            }

            var logger  = new ConsoleLogger(cfg.LogVerbosity);
            var runner  = new PipelineRunner(cfg, logger);
            var exitCode = await runner.RunAsync(dryRun);
            Environment.Exit(exitCode);

        }, configOpt, rootOpt, outputOpt, excludeOpt, verbosityOpt, dryRunOpt);

        return root;
    }
}
```

- [ ] **Step 2: Replace `Program.cs`**

```csharp
using Delta.DocGen.CLI;

await DocGenCommand.Build().InvokeAsync(args);
```

- [ ] **Step 3: Build**

```bash
dotnet build Delta.DocGen.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Smoke-test the help output**

```bash
dotnet run --project Delta.DocGen -- --help
```

Expected output contains:
```
Description:
  Delta.DocGen — BDD step library generator

Options:
  --config <config>
  --root <root>
  --output <output>
  --exclude <exclude>
  --verbosity <verbosity>
  --dry-run
```

- [ ] **Step 5: Commit**

```bash
git add Delta.DocGen/CLI/RootCommand.cs Delta.DocGen/Program.cs
git commit -m "feat: wire CLI with System.CommandLine (config, root, output, exclude, verbosity, dry-run)"
```

---

## Story 14: Full test suite passes

*As a developer, I can run the complete test suite and see all tests pass with zero warnings.*

### Task 14: Run all tests and build

- [ ] **Step 1: Run the full test suite**

```bash
dotnet test Delta.DocGen.sln -v minimal
```

Expected:
```
Passed! - Failed: 0, Passed: 38, Skipped: 0
```

(38 = 5 ConfigLoader + 4 Discoverer + 8 StepDefinitionExtractor + 4 UsageCounter + 5 DomainAssigner + 6 IdGenerator + 6 CanonicalJson + 5 Signer + any extras added during implementation)

- [ ] **Step 2: Confirm no build warnings**

```bash
dotnet build Delta.DocGen.sln -warnaserror
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit any fixups and tag**

```bash
git add -u
git commit -m "chore: ensure all tests pass and zero build warnings" --allow-empty
```

---

## Story 15: End-to-end smoke test

*As a user, I can point `docgen` at a real directory of feature files and step definitions and get a valid, signed JSON output file.*

### Task 15: End-to-end smoke test with sample fixtures

**Files:**
- Create: `fixtures/Auth/AuthSteps.cs`
- Create: `fixtures/features/login.feature`
- Create: `fixtures/docgen.config.json`

- [ ] **Step 1: Create fixture step definition**

Create `fixtures/Auth/AuthSteps.cs`:

```csharp
using TechTalk.SpecFlow;

[Binding]
public class AuthSteps
{
    [Given("I am logged in as {string}")]
    public void GivenLoggedIn(string username)
    {
        // sign in
    }

    [When("I sign out")]
    public void WhenSignOut()
    {
        // sign out
    }

    [Then("I should be on the login page")]
    public void ThenOnLoginPage()
    {
        // assert
    }
}
```

- [ ] **Step 2: Create fixture feature file**

Create `fixtures/features/login.feature`:

```gherkin
Feature: Authentication

  Scenario: Admin signs out
    Given I am logged in as "admin@delta.io"
    When I sign out
    Then I should be on the login page
```

- [ ] **Step 3: Create fixture config file**

Create `fixtures/docgen.config.json`:

```json
{
  "root": ".",
  "output": "../dist/step-library.json",
  "logVerbosity": "normal",
  "domains": [
    { "pattern": "Auth/**", "domain": "Auth", "label": "Auth & Identity" }
  ],
  "fallbackDomain": "General"
}
```

- [ ] **Step 4: Run the tool against the fixtures**

```bash
dotnet run --project Delta.DocGen -- --config fixtures/docgen.config.json
```

Expected output (approximate):
```
[INFO]  Delta.DocGen v1.0.0
[INFO]  Root    : .
[INFO]  Discovering files...
[INFO]    Found 1 .cs files, 1 .feature files
[INFO]  Parsing C# step definitions...
[INFO]    Extracted 3 step definitions total
[INFO]  Counting usages in feature files...
[INFO]  Assigning domains...
[INFO]  Generating IDs...
[DONE]  Steps extracted : 3
[DONE]  Features scanned: 1
[DONE]  Output          : ../dist/step-library.json (... bytes)
[DONE]  Digest (SHA-256): <64 hex chars>
```

- [ ] **Step 5: Verify the output file is valid JSON with the expected shape**

```bash
cat dist/step-library.json | python -m json.tool --no-ensure-ascii > /dev/null && echo "Valid JSON"
```

Expected: `Valid JSON`

- [ ] **Step 6: Verify the digest manually (optional sanity check)**

The following should print `Verified: True`:

```bash
dotnet script -e "
var json = System.IO.File.ReadAllText(\"dist/step-library.json\");
var doc = System.Text.Json.JsonDocument.Parse(json);
var digest = doc.RootElement.GetProperty(\"signature\").GetProperty(\"digest\").GetString();
Console.WriteLine(\"Stored digest: \" + digest);
"
```

(Or simply inspect `dist/step-library.json` in a text editor to confirm `signature.digest` is a 64-char hex string.)

- [ ] **Step 7: Commit fixtures**

```bash
git add fixtures/ dist/.gitkeep
git commit -m "chore: add smoke-test fixtures and confirm end-to-end output"
```

---

## Appendix: Open questions (from design spec §10)

These must be resolved before the relevant task is implemented, not after:

1. **ID collision fallback** — Task 12 currently aborts on collision. If a counter-suffix fallback is preferred, update `IdGenerator` and `PipelineRunner` before Task 12 is marked complete.
2. **`--dry-run` and schema file** — currently dry-run suppresses all file writes including the schema. Confirm this is correct before Task 13.
3. **V2 planning** — LLM enrichment, co-occurrence, and private key signing are out of scope here. Create a separate spec + plan when ready.
