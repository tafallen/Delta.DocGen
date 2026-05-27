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

        var result = PipelineRunner.Run(config, logger, args.DryRun);
        return result.Success ? 0 : 1;
    }
}
