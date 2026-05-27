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

    [Fact]
    public void NestedObjectKeysAreSorted()
    {
        var obj = new { outer = new { z = 1, a = 2 } };

        var json = CanonicalJson.Serialise(obj);

        // Inside "outer", "a" must appear before "z"
        var outerPos = json.IndexOf("outer", StringComparison.Ordinal);
        var aPos     = json.IndexOf("\"a\"",  StringComparison.Ordinal);
        var zPos     = json.IndexOf("\"z\"",  StringComparison.Ordinal);
        aPos.Should().BeGreaterThan(outerPos);
        aPos.Should().BeLessThan(zPos);
    }

    [Fact]
    public void ArrayElementOrderIsPreserved()
    {
        var obj = new { items = new[] { "charlie", "alpha", "bravo" } };

        var json = CanonicalJson.Serialise(obj);

        var charliePos = json.IndexOf("charlie", StringComparison.Ordinal);
        var alphaPos   = json.IndexOf("alpha",   StringComparison.Ordinal);
        var bravoPos   = json.IndexOf("bravo",   StringComparison.Ordinal);
        charliePos.Should().BeLessThan(alphaPos);
        alphaPos.Should().BeLessThan(bravoPos);
    }

    [Fact]
    public void OutputContainsNoWhitespace()
    {
        var obj = new { key = "value", number = 42 };

        var json = CanonicalJson.Serialise(obj);

        json.Should().NotContain(" ");
        json.Should().NotContain("\n");
        json.Should().NotContain("\r");
    }
}
