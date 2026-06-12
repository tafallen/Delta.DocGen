using System.Diagnostics;
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;
using Delta.DocGen.Output.Schema;
using Delta.DocGen.Output.Serialiser;
using Delta.DocGen.Scanner.CSharp;
using Delta.DocGen.Scanner.Gherkin;

namespace Delta.DocGen.Pipeline;

public static class PipelineRunner
{
    private sealed class UnmatchedCountingLogger : IDocGenLogger
    {
        private readonly IDocGenLogger _inner;
        private const string MatchPhrase = LogPhrases.UnmatchedStep;
        public int Count { get; private set; }

        public UnmatchedCountingLogger(IDocGenLogger inner) => _inner = inner;

        public void Info(string m)    => _inner.Info(m);
        public void Verbose(string m) => _inner.Verbose(m);
        public void Warn(string m)
        {
            if (m.Contains(MatchPhrase, StringComparison.Ordinal)) Count++;
            _inner.Warn(m);
        }
        public void Error(string m)              => _inner.Error(m);
        public void Error(string m, Exception ex) => _inner.Error(m, ex);
        public void Summary(string m) => _inner.Summary(m);
    }

    /// <summary>
    /// Runs pipeline stages 2–8 against a fully-resolved <see cref="DocGenConfig"/> and
    /// returns a <see cref="PipelineResult"/>. Stage 1 (config load) is the caller's
    /// responsibility — see <see cref="Config.ConfigLoader.Load"/>.
    /// </summary>
    /// <param name="config">Resolved configuration with absolute <c>Root</c> and <c>Output</c> paths.</param>
    /// <param name="logger">Logger used by every stage; warning messages matching
    /// <see cref="Logging.LogPhrases.UnmatchedStep"/> contribute to
    /// <see cref="PipelineResult.UnmatchedStepCount"/>.</param>
    /// <param name="dryRun">When <c>true</c>, runs every stage but does not write the
    /// envelope or schema files.</param>
    /// <param name="clock">Optional clock for deterministic <c>generatedAt</c> timestamps in tests.
    /// Defaults to <see cref="DateTime.UtcNow"/>.</param>
    /// <returns>A <see cref="PipelineResult"/>. On failure, <see cref="PipelineResult.Success"/> is
    /// <c>false</c> and <see cref="PipelineResult.FailureCategory"/> indicates whether the cause was
    /// user input (<see cref="FailureCategory.UserError"/>) or an internal invariant
    /// (<see cref="FailureCategory.InternalError"/>).</returns>
    public static PipelineResult Run(
        DocGenConfig config,
        IDocGenLogger logger,
        bool dryRun = false,
        Func<DateTime>? clock = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var nowFn = clock ?? (() => DateTime.UtcNow);
        try
        {
            var unmatchedCounter = new UnmatchedCountingLogger(logger);

            // Stage 2: discovery
            var discovery = Discoverer.Discover(config.Root, config.Exclude, unmatchedCounter);

            // Stage 3: C# extraction
            var rawSteps = new List<RawStep>();
            foreach (var csFile in discovery.CsFiles)
                rawSteps.AddRange(StepDefinitionExtractor.Extract(csFile, config.Root, unmatchedCounter));

            // Stage 4: usage counting (per-file; accumulate)
            var totalUsage = rawSteps
                .GroupBy(s => s.Pattern, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, _ => 0, StringComparer.Ordinal);
            foreach (var featureFile in discovery.FeatureFiles)
            {
                var fileCounts = UsageCounter.Count(rawSteps, featureFile, config.Root, unmatchedCounter);
                foreach (var (pattern, count) in fileCounts)
                    if (totalUsage.ContainsKey(pattern))
                        totalUsage[pattern] += count;
            }

            // Stage 4b: observed table columns
            var observedColumns = TableColumnAggregator.Aggregate(
                rawSteps, discovery.FeatureFiles, config.Root, unmatchedCounter);

            // Stage 5: domain assignment
            var domainAssigned = DomainAssigner.Assign(
                rawSteps, config.Domains, config.FallbackDomain, unmatchedCounter);

            // Stage 6: id generation + domain records
            var stepRecords = IdGenerator.AssignIds(
                domainAssigned,
                totalUsage,
                observedColumns,
                unmatchedCounter);
            var domainRecords = DomainBuilder.Build(domainAssigned, config.Domains);

            // Stage 7: build + sign envelope
            var envelope = new Envelope(
                Schema:           SchemaConstants.SchemaRelativeRef,
                Version:          SchemaConstants.EnvelopeVersion,
                GeneratedAt:      nowFn().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                GeneratorVersion: SchemaConstants.GeneratorVersion,
                Enriched:         false,
                Domains:          domainRecords,
                Steps:            stepRecords,
                Signature:        null);
            var signed = Signer.Sign(envelope);

            // Stage 8: file output (skipped on dry-run)
            string? outputPath = null;
            string? schemaPath = null;
            if (!dryRun)
            {
                var outputDir = Path.GetDirectoryName(config.Output)
                    ?? throw new InvalidOperationException(
                        $"Cannot determine output directory from '{config.Output}'.");
                schemaPath = SchemaWriter.Write(outputDir, logger);  // schema first
                CanonicalJson.Write(signed, config.Output);          // then envelope
                outputPath = config.Output;
            }

            if (dryRun)
                logger.Verbose($"Dry-run: $schema reference '{SchemaConstants.SchemaRelativeRef}' will not be resolvable on disk.");

            stopwatch.Stop();
            var result = new PipelineResult(
                Success:            true,
                StepCount:          stepRecords.Count,
                DomainCount:        domainRecords.Count,
                CsFileCount:        discovery.CsFiles.Count,
                FeatureFileCount:   discovery.FeatureFiles.Count,
                UnmatchedStepCount: unmatchedCounter.Count,
                OutputPath:         outputPath,
                SchemaPath:         schemaPath,
                Digest:             signed.Signature?.Digest,
                ElapsedMs:          stopwatch.ElapsedMilliseconds,
                ErrorMessage:       null,
                FailureCategory:    FailureCategory.None);

            logger.Summary(
                $"Pipeline complete: {result.StepCount} step(s), {result.DomainCount} domain(s), " +
                $"{result.CsFileCount} C# file(s), {result.FeatureFileCount} feature file(s), " +
                $"elapsed {result.ElapsedMs}ms.");
            return result;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException or System.Text.Json.JsonException)
        {
            return BuildFailedResult(stopwatch, logger, ex.Message, FailureCategory.UserError);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return BuildFailedResult(stopwatch, logger, ex.Message, FailureCategory.InternalError);
        }
    }

    private static PipelineResult BuildFailedResult(
        Stopwatch stopwatch,
        IDocGenLogger logger,
        string message,
        FailureCategory category)
    {
        if (stopwatch.IsRunning) stopwatch.Stop();
        logger.Error($"Pipeline failed: {message}");
        return new PipelineResult(
            Success: false, StepCount: 0, DomainCount: 0, CsFileCount: 0, FeatureFileCount: 0,
            UnmatchedStepCount: 0, OutputPath: null, SchemaPath: null, Digest: null,
            ElapsedMs: stopwatch.ElapsedMilliseconds, ErrorMessage: message,
            FailureCategory: category);
    }
}
