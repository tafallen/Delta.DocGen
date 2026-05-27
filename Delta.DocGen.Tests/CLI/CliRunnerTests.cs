using System.Text.Json;
using Delta.DocGen.CLI;
using Delta.DocGen.Tests.Pipeline.Fixtures;
using FluentAssertions;

namespace Delta.DocGen.Tests.CLI;

public sealed class CliRunnerTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _root;
    private readonly string _output;
    private readonly string _configPath;

    public CliRunnerTests()
    {
        _workspace  = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _root       = Path.Combine(_workspace, "src");
        _output     = Path.Combine(_workspace, "dist", "step-library.json");
        _configPath = Path.Combine(_workspace, "docgen.config.json");
        Directory.CreateDirectory(_root);
        PipelineFixture.WriteFixture(_root);

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

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public void SuccessfulRunReturnsZero()
    {
        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false));

        exitCode.Should().Be(0);
        File.Exists(_output).Should().BeTrue();
    }

    [Fact]
    public void DryRunReturnsZeroAndWritesNoOutputFile()
    {
        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: true));

        exitCode.Should().Be(0);
        File.Exists(_output).Should().BeFalse();
    }

    [Fact]
    public void MissingConfigFileReturnsOne()
    {
        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: Path.Combine(_workspace, "missing.json"),
            Root: null, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false));

        exitCode.Should().Be(1);
    }

    [Fact]
    public void CliRootOverrideTakesPrecedenceOverConfig()
    {
        // Point --root at a different but valid fixture dir.
        var altRoot = Path.Combine(_workspace, "alt-src");
        Directory.CreateDirectory(altRoot);
        PipelineFixture.WriteFixture(altRoot);

        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: altRoot, Output: null, Excludes: [],
            Verbosity: "silent", DryRun: false));

        exitCode.Should().Be(0);
        File.Exists(_output).Should().BeTrue();
    }

    [Fact]
    public void AdditionalExcludesAreAppliedOnTopOfConfig()
    {
        // Exclude one domain via CLI; pipeline still succeeds.
        var exitCode = CliRunner.Run(new CliArgs(
            ConfigPath: _configPath,
            Root: null, Output: null, Excludes: ["**/Forms/**"],
            Verbosity: "silent", DryRun: true));

        exitCode.Should().Be(0);
    }
}
