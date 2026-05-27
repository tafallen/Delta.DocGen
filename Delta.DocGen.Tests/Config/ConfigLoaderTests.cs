using Delta.DocGen.Config;
using FluentAssertions;

namespace Delta.DocGen.Tests.Config;

public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public ConfigLoaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LoadsMinimalConfigFile()
    {
        var json = """
            {
              "root": "./tests",
              "output": "./dist/step-library.json"
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var config = ConfigLoader.Load(path, overrides: new ConfigOverrides());

        // Root and Output are resolved to absolute paths relative to the config file's directory
        config.Root.Should().Be(Path.GetFullPath("./tests", _dir));
        config.Output.Should().Be(Path.GetFullPath("./dist/step-library.json", _dir));
        config.Exclude.Should().BeEmpty();
        config.LogVerbosity.Should().Be("normal");
        config.FallbackDomain.Should().Be("General");
        config.Domains.Should().BeEmpty();
    }

    [Fact]
    public void LoadsDomainRules()
    {
        var json = """
            {
              "root": "./tests",
              "output": "./out.json",
              "domains": [
                { "pattern": "Auth/**", "domain": "Auth", "label": "Auth & Identity" }
              ]
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var config = ConfigLoader.Load(path, overrides: new ConfigOverrides());

        config.Domains.Should().HaveCount(1);
        config.Domains[0].Pattern.Should().Be("Auth/**");
        config.Domains[0].Domain.Should().Be("Auth");
        config.Domains[0].Label.Should().Be("Auth & Identity");
    }

    [Fact]
    public void DomainLabelDefaultsToDomainWhenBlank()
    {
        var json = """
            {
              "root": "./tests",
              "output": "./out.json",
              "domains": [
                { "pattern": "Auth/**", "domain": "Auth" }
              ]
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var config = ConfigLoader.Load(path, overrides: new ConfigOverrides());

        config.Domains[0].Label.Should().Be("Auth");
    }

    [Fact]
    public void CliOverridesRootAndOutput()
    {
        var json = """{ "root": "./tests", "output": "./out.json" }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var overrides = new ConfigOverrides { Root = "./other", Output = "./other/out.json" };
        var config = ConfigLoader.Load(path, overrides);

        config.Root.Should().Be(Path.GetFullPath("./other", _dir));
        config.Output.Should().Be(Path.GetFullPath("./other/out.json", _dir));
    }

    [Fact]
    public void CliVerbosityOverrideIsApplied()
    {
        var json = """{ "root": "./tests", "output": "./out.json", "logVerbosity": "silent" }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var overrides = new ConfigOverrides { LogVerbosity = "verbose" };
        var config = ConfigLoader.Load(path, overrides);

        config.LogVerbosity.Should().Be("verbose");
    }

    [Fact]
    public void CliExcludesAreAdditiveWithConfigExcludes()
    {
        var json = """
            {
              "root": "./tests",
              "output": "./out.json",
              "exclude": ["**/meta/**"]
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var overrides = new ConfigOverrides { AdditionalExcludes = ["**/generated/**"] };
        var config = ConfigLoader.Load(path, overrides);

        config.Exclude.Should().BeEquivalentTo(["**/meta/**", "**/generated/**"]);
    }

    [Fact]
    public void ThrowsIfConfigFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "docgen.config.json");
        var act = () => ConfigLoader.Load(missing, new ConfigOverrides());
        act.Should().Throw<FileNotFoundException>();
    }

    [Theory]
    [InlineData("root")]
    [InlineData("output")]
    public void ThrowsIfRequiredFieldIsWhitespaceOnly(string field)
    {
        var json = field == "root"
            ? """{ "root": "   ", "output": "./out.json" }"""
            : """{ "root": "./tests", "output": "   " }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var act = () => ConfigLoader.Load(path, new ConfigOverrides());
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{field}*");
    }

    [Fact]
    public void ThrowsIfRootMissingFromBothFileAndOverrides()
    {
        var json = """{ "output": "./out.json" }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var act = () => ConfigLoader.Load(path, new ConfigOverrides());
        act.Should().Throw<InvalidOperationException>().WithMessage("*root*");
    }

    [Fact]
    public void ThrowsIfOutputMissingFromBothFileAndOverrides()
    {
        var json = """{ "root": "./tests" }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var act = () => ConfigLoader.Load(path, new ConfigOverrides());
        act.Should().Throw<InvalidOperationException>().WithMessage("*output*");
    }

    [Fact]
    public void ThrowsOnInvalidLogVerbosity()
    {
        var json = """{ "root": "./tests", "output": "./out.json", "logVerbosity": "verboze" }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var act = () => ConfigLoader.Load(path, new ConfigOverrides());
        act.Should().Throw<InvalidOperationException>().WithMessage("*verboze*");
    }

    [Fact]
    public void VerbosityIsCaseInsensitive()
    {
        var json = """{ "root": "./tests", "output": "./out.json", "logVerbosity": "Normal" }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var config = ConfigLoader.Load(path, new ConfigOverrides());

        config.LogVerbosity.Should().Be("normal");
    }

    [Fact]
    public void ThrowsIfDomainRuleHasBlankPattern()
    {
        var json = """
            {
              "root": "./tests",
              "output": "./out.json",
              "domains": [
                { "pattern": "", "domain": "Auth", "label": "Auth" }
              ]
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var act = () => ConfigLoader.Load(path, new ConfigOverrides());
        act.Should().Throw<InvalidOperationException>().WithMessage("*pattern*");
    }

    [Fact]
    public void ThrowsIfDomainRuleHasBlankDomain()
    {
        var json = """
            {
              "root": "./tests",
              "output": "./out.json",
              "domains": [
                { "pattern": "Auth/**", "domain": "", "label": "Auth" }
              ]
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var act = () => ConfigLoader.Load(path, new ConfigOverrides());
        act.Should().Throw<InvalidOperationException>().WithMessage("*domain*");
    }

    [Fact]
    public void FallbackDomainDefaultsToGeneral()
    {
        var json = """{ "root": "./tests", "output": "./out.json" }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var config = ConfigLoader.Load(path, new ConfigOverrides());

        config.FallbackDomain.Should().Be("General");
    }

    [Fact]
    public void LoadsConfigWithJsonComments()
    {
        var json = """
            {
              // project root
              "root": "./tests",
              "output": "./out.json" // generated file
            }
            """;
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var config = ConfigLoader.Load(path, new ConfigOverrides());

        config.Root.Should().Be(Path.GetFullPath("./tests", _dir));
    }
}
