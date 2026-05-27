using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Pipeline;

namespace Delta.DocGen.CLI;

public static class CliRunner
{
    public static int Run(CliArgs args)
    {
        IDocGenLogger logger = new ConsoleLogger(args.Verbosity ?? LogVerbosity.Normal);

        DocGenConfig config;
        try
        {
            config = ConfigLoader.Load(args.ConfigPath, new ConfigOverrides
            {
                Root               = args.Root,
                Output             = args.Output,
                LogVerbosity       = args.Verbosity,
                AdditionalExcludes = args.Excludes,
            });
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to load config: {ex.Message}");
            return 1;
        }

        // If the user didn't specify --verbosity on the CLI, switch to the level the
        // config file resolved to (which may differ from the bootstrap ConsoleLogger).
        if (args.Verbosity is null && config.LogVerbosity != LogVerbosity.Normal)
            logger = new ConsoleLogger(config.LogVerbosity);

        var result = PipelineRunner.Run(config, logger, args.DryRun);

        if (result.Success) return 0;
        return result.FailureCategory switch
        {
            FailureCategory.UserError     => 1,
            FailureCategory.InternalError => 2,
            _                             => 1,
        };
    }
}
