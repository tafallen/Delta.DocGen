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

        config.Root.Should().Be("./tests");
        config.Output.Should().Be("./dist/step-library.json");
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
    public void CliOverridesRootAndOutput()
    {
        var json = """{ "root": "./tests", "output": "./out.json" }""";
        var path = Path.Combine(_dir, "docgen.config.json");
        File.WriteAllText(path, json);

        var overrides = new ConfigOverrides { Root = "./other", Output = "./other/out.json" };
        var config = ConfigLoader.Load(path, overrides);

        config.Root.Should().Be("./other");
        config.Output.Should().Be("./other/out.json");
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
        var act = () => ConfigLoader.Load("/nonexistent/docgen.config.json", new ConfigOverrides());
        act.Should().Throw<FileNotFoundException>();
    }
}
