using Delta.DocGen.Output.Serialiser;
using FluentAssertions;

namespace Delta.DocGen.Tests.Output;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void KeysSortedAlphabeticallyAtTopLevel()
    {
        var obj = new { zebra = 1, apple = 2, mango = 3 };

        var json = CanonicalJson.Serialise(obj);

        // Keys must appear in alphabetical order
        var applePos = json.IndexOf("apple", StringComparison.Ordinal);
        var mangoPos = json.IndexOf("mango", StringComparison.Ordinal);
        var zebraPos = json.IndexOf("zebra", StringComparison.Ordinal);
        applePos.Should().BeLessThan(mangoPos);
        mangoPos.Should().BeLessThan(zebraPos);
    }
}
