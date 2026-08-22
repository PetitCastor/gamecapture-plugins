using Xunit;

namespace SignaturePlugin.Tests;

public class SignatureParserTests
{
    [Theory]
    [InlineData("1620", 1620.0)]
    [InlineData("1,620", 1620.0)]
    [InlineData("1 620", 1620.0)]
    [InlineData("  1 620  ", 1620.0)]
    [InlineData("1620.75", 1620.75)]
    [InlineData(".5", 0.5)]
    [InlineData("Signature: 1,620.5%", 1620.5)]
    [InlineData("Signature 1 620 units", 1620.0)]
    public void TryParse_ExtractsSupportedNumberFormats(string ocrText, double expected)
    {
        Assert.True(SignatureParser.TryParse(ocrText, out var signature));
        Assert.Equal(expected, signature);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no signature")]
    [InlineData("OolI")]
    [InlineData("1,2")]
    [InlineData("12,34")]
    [InlineData("1 2")]
    [InlineData("12,34 kg")]
    [InlineData("1 2 units")]
    [InlineData("1.2.3")]
    public void TryParse_RejectsEmptyAndGarbage(string ocrText)
    {
        Assert.False(SignatureParser.TryParse(ocrText, out var signature));
        Assert.Equal(0, signature);
    }

    [Fact]
    public void TryParse_NullInputDoesNotThrow()
    {
        var exception = Record.Exception(() => SignatureParser.TryParse(null!, out var signature));

        Assert.Null(exception);
    }
}
