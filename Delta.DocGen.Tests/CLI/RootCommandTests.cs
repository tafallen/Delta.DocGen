using System.CommandLine;
using Delta.DocGen.CLI;
using FluentAssertions;

namespace Delta.DocGen.Tests.CLI;

public sealed class RootCommandTests
{
    private static CliArgs Capture(params string[] args)
    {
        CliArgs? captured = null;
        var cmd = CliRootCommand.Build(a => { captured = a; return 0; });
        cmd.Invoke(args);
        return captured ?? throw new InvalidOperationException("Handler was not invoked.");
    }

    [Fact]
    public void DefaultsApplyWhenNoArgsProvided()
    {
        var args = Capture();

        args.ConfigPath.Should().Be("./docgen.config.json");
        args.Root.Should().BeNull();
        args.Output.Should().BeNull();
        args.Excludes.Should().BeEmpty();
        args.Verbosity.Should().BeNull();
        args.DryRun.Should().BeFalse();
    }

    [Fact]
    public void AllOverridesParsedFromArgs()
    {
        var args = Capture(
            "--config", "custom.json",
            "--root", "src",
            "--output", "dist/out.json",
            "--exclude", "**/bin/**",
            "--exclude", "**/obj/**",
            "--verbosity", "verbose",
            "--dry-run");

        args.ConfigPath.Should().Be("custom.json");
        args.Root.Should().Be("src");
        args.Output.Should().Be("dist/out.json");
        args.Excludes.Should().Equal("**/bin/**", "**/obj/**");
        args.Verbosity.Should().Be("verbose");
        args.DryRun.Should().BeTrue();
    }

    [Fact]
    public void HandlerReturnValueBecomesProcessExitCode()
    {
        var cmd = CliRootCommand.Build(_ => 42);
        var exitCode = cmd.Invoke([]);
        exitCode.Should().Be(42);
    }
}
