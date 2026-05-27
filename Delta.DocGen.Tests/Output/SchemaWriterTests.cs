using Delta.DocGen.Logging;
using Delta.DocGen.Output.Schema;
using Delta.DocGen.Tests.Logging;
using FluentAssertions;

namespace Delta.DocGen.Tests.Output;

public sealed class SchemaWriterTests : IDisposable
{
    private readonly string _outputDir;

    public SchemaWriterTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    [Fact]
    public void WriteCreatesSchemaFileAtExpectedRelativePath()
    {
        var path = SchemaWriter.Write(_outputDir, NullDocGenLogger.Instance);

        var expected = Path.Combine(_outputDir, "schema", "v1", "step-library.schema.json");
        path.Should().Be(expected);
        File.Exists(expected).Should().BeTrue();
    }

    [Fact]
    public void WriteOutputMatchesEmbeddedResourceByteForByte()
    {
        var path = SchemaWriter.Write(_outputDir, NullDocGenLogger.Instance);
        var onDisk = File.ReadAllBytes(path);

        var assembly = typeof(SchemaWriter).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Delta.DocGen.Output.Schema.Resources.step-library.v1.schema.json")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        onDisk.Should().Equal(ms.ToArray());
    }

    [Fact]
    public void WriteCreatesNestedDirectoriesIfAbsent()
    {
        var nestedDir = Path.Combine(_outputDir, "deep", "nested", "path");

        var path = SchemaWriter.Write(nestedDir, NullDocGenLogger.Instance);

        File.Exists(path).Should().BeTrue();
        Directory.Exists(Path.Combine(nestedDir, "schema", "v1")).Should().BeTrue();
    }

    [Fact]
    public void WriteOverwritesExistingSchemaFile()
    {
        var first = SchemaWriter.Write(_outputDir, NullDocGenLogger.Instance);
        File.WriteAllText(first, "stale content");

        var second = SchemaWriter.Write(_outputDir, NullDocGenLogger.Instance);

        File.ReadAllText(second).Should().NotBe("stale content");
        File.ReadAllText(second).Should().Contain("\"$schema\"");
    }

    [Fact]
    public void WriteLogsInfoMessageWithDestinationPath()
    {
        var logger = new CapturingDocGenLogger();

        var path = SchemaWriter.Write(_outputDir, logger);

        logger.InfoMessages.Should().ContainSingle(m => m.Contains(path));
    }
}
