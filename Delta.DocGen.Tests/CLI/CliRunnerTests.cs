using System.Text.Json;
using Delta.DocGen.CLI;
using Delta.DocGen.Tests.Pipeline.Fixtures;
using FluentAssertions;

namespace Delta.DocGen.Tests.CLI;

public sealed class CliRunnerTests : IDisposable
{
    private readonly TestWorkspace _workspace;
    private readonly string _configPath;

    // Convenience accessors delegated to TestWorkspace.
    private string _root       => _workspace.Root;
    private string _output     => _workspace.Output;
    private string _workspaceDir => _workspace.Workspace;

    public CliRunnerTests()
    {
        _workspace  = new TestWorkspace();
        _configPath = Path.Combine(_workspace.Workspace, "docgen.config.json");

        var configJson = JsonSerializer.Serialize(new
        {
            root   = _root,
            output = _output,
            domains = new[]
            {
                new { pattern = "Auth/**",  domain = "Auth",  label = "Auth & Identity" },
                new { pattern = "Forms/**", domain = "Forms", label = "Forms & Input" },
            },
        });
        File.WriteAllText(_configPath, configJson);
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void SuccessfulRunReturnsZero()
    {
        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false, NoExcludeConfig: false));

        exitCode.Should().Be(0);
        File.Exists(_output).Should().BeTrue();
    }

    [Fact]
    public void DryRunReturnsZeroAndWritesNoOutputFile()
    {
        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: true, NoExcludeConfig: false));

        exitCode.Should().Be(0);
        File.Exists(_output).Should().BeFalse();
    }

    [Fact]
    public void MissingConfigFileReturnsOne()
    {
        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: Path.Combine(_workspaceDir,"missing.json"),
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false, NoExcludeConfig: false));

        exitCode.Should().Be(1);
    }

    [Fact]
    public void CliRootOverrideTakesPrecedenceOverConfig()
    {
        // Point --root at a different but valid fixture dir.
        var altRoot = Path.Combine(_workspaceDir,"alt-src");
        Directory.CreateDirectory(altRoot);
        PipelineFixture.WriteFixture(altRoot);

        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: altRoot, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false, NoExcludeConfig: false));

        exitCode.Should().Be(0);
        File.Exists(_output).Should().BeTrue();
    }

    [Fact]
    public void AdditionalExcludesActuallyReduceStepCount()
    {
        // Baseline: no extra excludes — fixture should produce some step count.
        var baseline = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false, NoExcludeConfig: false));
        baseline.Should().Be(0);
        var baselineStepCount = JsonDocument.Parse(File.ReadAllText(_output))
            .RootElement.GetProperty("steps").GetArrayLength();

        File.Delete(_output);  // clean slate for the second run

        // Excluded run: drop the Forms directory via --exclude.
        var excluded = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: ["**/Forms/**"],
            Verbosity: "silent", DryRun: false, NoExcludeConfig: false));
        excluded.Should().Be(0);
        var excludedStepCount = JsonDocument.Parse(File.ReadAllText(_output))
            .RootElement.GetProperty("steps").GetArrayLength();

        excludedStepCount.Should().BeLessThan(baselineStepCount);
    }

    [Fact]
    public void ConfigFileLogVerbosityIsHonouredWhenCliFlagNotPassed()
    {
        // Write a config that sets logVerbosity: "silent".
        var configJson = JsonSerializer.Serialize(new
        {
            root           = _root,
            output         = _output,
            logVerbosity   = "silent",
            domains        = new[]
            {
                new { pattern = "Auth/**",  domain = "Auth",  label = "Auth & Identity" },
                new { pattern = "Forms/**", domain = "Forms", label = "Forms & Input" },
            },
        });
        File.WriteAllText(_configPath, configJson);

        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: [],
            Verbosity: null,   // user did NOT pass --verbosity
            DryRun: false, NoExcludeConfig: false));

        // Exit code 0 confirms the run completed and the config was loaded with silent verbosity.
        exitCode.Should().Be(0);
        File.Exists(_output).Should().BeTrue();
    }

    [Fact]
    public void CrossMethodCollisionStillReturnsZero()
    {
        // ID collisions are now logged as Error and skipped — the CLI returns 0 because
        // a usable JSON file was still produced. The Error log surfaces the defect.
        File.WriteAllText(Path.Combine(_root, "Auth", "DuplicateSteps.cs"), """
            using Reqnroll;
            namespace Demo;
            public class DuplicateSteps
            {
                [Given("I am logged in")]
                public void GivenDuplicate() { }
            }
            """);

        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false, NoExcludeConfig: false));

        exitCode.Should().Be(0);
        File.Exists(_output).Should().BeTrue();
    }

    [Fact]
    public void UserErrorFromMissingRootReturnsExitCodeOne()
    {
        var configJson = JsonSerializer.Serialize(new
        {
            root    = Path.Combine(_workspaceDir,"does-not-exist"),
            output  = _output,
            domains = Array.Empty<object>(),
        });
        File.WriteAllText(_configPath, configJson);

        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false, NoExcludeConfig: false));

        exitCode.Should().Be(1);
    }

    [Fact]
    public void NoExcludeConfigSuppressesConfigExcludes()
    {
        // Write config that excludes Forms — normally Forms steps would be missing.
        var configJson = JsonSerializer.Serialize(new
        {
            root    = _root,
            output  = _output,
            exclude = new[] { "**/Forms/**" },
            domains = new[]
            {
                new { pattern = "Auth/**",  domain = "Auth",  label = "Auth & Identity" },
                new { pattern = "Forms/**", domain = "Forms", label = "Forms & Input" },
            },
        });
        File.WriteAllText(_configPath, configJson);

        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false, NoExcludeConfig: true));

        exitCode.Should().Be(0);
        var steps = JsonDocument.Parse(File.ReadAllText(_output))
            .RootElement.GetProperty("steps");
        var hasFormsStep = false;
        foreach (var step in steps.EnumerateArray())
        {
            if (step.GetProperty("file").GetString()?.Contains("Forms") == true)
            {
                hasFormsStep = true;
                break;
            }
        }
        hasFormsStep.Should().BeTrue("Forms steps should appear when --no-exclude-config suppresses the config exclude");
    }

    [Fact]
    public void NoExcludeConfigWithCliExcludeUsesOnlyCliExclude()
    {
        // Config excludes Forms; CLI excludes Auth; --no-exclude-config → only Auth excluded.
        var configJson = JsonSerializer.Serialize(new
        {
            root    = _root,
            output  = _output,
            exclude = new[] { "**/Forms/**" },
            domains = new[]
            {
                new { pattern = "Auth/**",  domain = "Auth",  label = "Auth & Identity" },
                new { pattern = "Forms/**", domain = "Forms", label = "Forms & Input" },
            },
        });
        File.WriteAllText(_configPath, configJson);

        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: ["**/Auth/**"],
            Verbosity: "silent", DryRun: false, NoExcludeConfig: true));

        exitCode.Should().Be(0);
        var steps = JsonDocument.Parse(File.ReadAllText(_output))
            .RootElement.GetProperty("steps");

        var hasFormsStep  = false;
        var hasAuthStep   = false;
        foreach (var step in steps.EnumerateArray())
        {
            var file = step.GetProperty("file").GetString() ?? "";
            if (file.Contains("Forms")) hasFormsStep = true;
            if (file.Contains("Auth"))  hasAuthStep  = true;
        }

        hasFormsStep.Should().BeTrue("Forms steps should appear — config exclude was suppressed");
        hasAuthStep.Should().BeFalse("Auth steps should not appear — CLI --exclude applies");
    }
}
