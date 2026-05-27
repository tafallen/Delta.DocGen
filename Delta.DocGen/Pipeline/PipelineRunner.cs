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
    private const string SchemaRelativeRef = "./schema/v1/step-library.schema.json";
    private const string EnvelopeVersion   = "1.0.0";
    private const string GeneratorVersion  = "1.0.0";

    private sealed class UnmatchedCountingLogger : IDocGenLogger
    {
        private readonly IDocGenLogger _inner;
        private const string MatchPhrase = "Unmatched step in";
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

    public static PipelineResult Run(DocGenConfig config, IDocGenLogger logger, bool dryRun = false)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var unmatchedCounter = new UnmatchedCountingLogger(logger);

            // Stage 2: discovery
            var discovery = Discoverer.Discover(config.Root, config.Exclude);

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

            // Stage 5: domain assignment
            var domainAssigned = DomainAssigner.Assign(
                rawSteps, config.Domains, config.FallbackDomain, unmatchedCounter);

            // Stage 6: id generation + domain records
            var stepRecords = IdGenerator.AssignIds(domainAssigned, totalUsage, unmatchedCounter);
            var domainRecords = DomainBuilder.Build(domainAssigned, config.Domains);

            // Stage 7: build + sign envelope
            var envelope = new Envelope(
                Schema:           SchemaRelativeRef,
                Version:          EnvelopeVersion,
                GeneratedAt:      DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                GeneratorVersion: GeneratorVersion,
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
                CanonicalJson.Write(signed, config.Output);
                outputPath = config.Output;
                var outputDir = Path.GetDirectoryName(config.Output)
                    ?? throw new InvalidOperationException(
                        $"Cannot determine output directory from '{config.Output}'.");
                schemaPath = SchemaWriter.Write(outputDir, unmatchedCounter);
            }

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
                ErrorMessage:       null);

            logger.Summary(
                $"Pipeline complete: {result.StepCount} step(s), {result.DomainCount} domain(s), " +
                $"{result.CsFileCount} C# file(s), {result.FeatureFileCount} feature file(s), " +
                $"elapsed {result.ElapsedMs}ms.");
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.Error($"Pipeline failed: {ex.Message}");
            return new PipelineResult(
                Success: false, StepCount: 0, DomainCount: 0, CsFileCount: 0, FeatureFileCount: 0,
                UnmatchedStepCount: 0, OutputPath: null, SchemaPath: null, Digest: null,
                ElapsedMs: stopwatch.ElapsedMilliseconds, ErrorMessage: ex.Message);
        }
    }
}
