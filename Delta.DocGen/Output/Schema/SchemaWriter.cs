using Delta.DocGen.Logging;

namespace Delta.DocGen.Output.Schema;

public static class SchemaWriter
{
    private const string ResourceName = "Delta.DocGen.Output.Schema.Resources.step-library.v1.schema.json";
    private const string RelativeOutputPath = "schema/v1/step-library.schema.json";

    public static string Write(string outputDir, IDocGenLogger logger)
    {
        var assembly = typeof(SchemaWriter).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. " +
                "Verify the <EmbeddedResource> entry in Delta.DocGen.csproj.");

        var destination = Path.Combine(outputDir, RelativeOutputPath.Replace('/', Path.DirectorySeparatorChar));
        var destinationDir = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"Cannot determine directory for '{destination}'.");
        Directory.CreateDirectory(destinationDir);

        using var output = File.Create(destination);
        stream.CopyTo(output);

        logger.Info($"Schema written to {destination}.");
        return destination;
    }
}
