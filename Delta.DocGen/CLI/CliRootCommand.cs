using System.CommandLine;
using Delta.DocGen.Logging;

namespace Delta.DocGen.CLI;

public static class CliRootCommand
{
    public static RootCommand Build(Func<CliArgs, int> handler)
    {
        var configOption = new Option<string>(
            aliases: ["--config"],
            description: "Path to config file",
            getDefaultValue: () => "./docgen.config.json");

        var rootOption = new Option<string?>(
            aliases: ["--root"],
            description: "Root directory to scan (overrides config)");

        var outputOption = new Option<string?>(
            aliases: ["--output"],
            description: "Output file path (overrides config)");

        var excludeOption = new Option<string[]>(
            aliases: ["--exclude"],
            description: "Add an exclude glob (repeatable; additive with config excludes)")
        {
            AllowMultipleArgumentsPerToken = false,
            Arity = ArgumentArity.ZeroOrMore,
        };

        var verbosityOption = new Option<string?>(
            aliases: ["--verbosity"],
            description: "silent | normal | verbose (default: normal)");
        verbosityOption.AddValidator(result =>
        {
            var v = result.GetValueForOption(verbosityOption);
            if (v is not null && v is not (LogVerbosity.Silent or LogVerbosity.Normal or LogVerbosity.Verbose))
                result.ErrorMessage = $"Invalid verbosity '{v}'. Expected: silent | normal | verbose.";
        });

        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run"],
            description: "Scan and report but do not write output file");

        var cmd = new RootCommand(
            "Delta.DocGen — generate a step library from a Reqnroll project");
        cmd.AddOption(configOption);
        cmd.AddOption(rootOption);
        cmd.AddOption(outputOption);
        cmd.AddOption(excludeOption);
        cmd.AddOption(verbosityOption);
        cmd.AddOption(dryRunOption);

        cmd.SetHandler(context =>
        {
            var args = new CliArgs(
                ConfigPath: context.ParseResult.GetValueForOption(configOption) ?? "./docgen.config.json",
                Root:       context.ParseResult.GetValueForOption(rootOption),
                Output:     context.ParseResult.GetValueForOption(outputOption),
                Excludes:   context.ParseResult.GetValueForOption(excludeOption) ?? [],
                Verbosity:  context.ParseResult.GetValueForOption(verbosityOption),
                DryRun:     context.ParseResult.GetValueForOption(dryRunOption));
            context.ExitCode = handler(args);
        });

        return cmd;
    }
}
