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

    // Every string below came out of Windows OCR on the real counter crop during one live mining run.
    // The slash is this HUD font's thousands comma; the leading punctuation is the pin icon's edge
    // bleeding into the crop.
    [Theory]
    [InlineData("21/425", 21425.0)]
    [InlineData("21/125", 21125.0)]
    [InlineData("21,425", 21425.0)]
    [InlineData("- 21,425", 21425.0)]
    [InlineData("- 21425", 21425.0)]
    [InlineData(". 21425", 21425.0)]
    [InlineData("17,200", 17200.0)]
    [InlineData("19,600", 19600.0)]
    public void TryParse_ReadsCapturedLiveOcrText(string ocrText, double expected)
    {
        Assert.True(SignatureParser.TryParse(ocrText, out var signature));
        Assert.Equal(expected, signature);
    }

    /// <summary>
    /// The regression this parser exists to prevent. A token that leaves digits behind is a
    /// truncation, and returning the prefix is far worse than returning nothing: <c>21/425</c> used to
    /// yield 21, a number that parses cleanly, matches no cluster, and that the caller's consensus
    /// filter then defends as though it were a real observation. Because the same misread recurred on
    /// roughly every other tick of a live run, that deadlocked the overlay on a stale ore for sixteen
    /// seconds — the true reading could never land two confirmations in a row.
    /// </summary>
    [Theory]
    [InlineData("21k25")]   // captured live; before the fix this returned 21
    [InlineData("21x425")]
    [InlineData("1,620x5")]
    [InlineData("3,400k2")]
    public void TryParse_RejectsATruncatedReadRatherThanReturningThePrefix(string ocrText)
    {
        Assert.False(SignatureParser.TryParse(ocrText, out var signature));
        Assert.Equal(0, signature);
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
    // Captured live. Folding '/' to ',' must not turn a malformed group into some other number:
    // these become "21,05" / "21,+25", which the group validator still rejects.
    [InlineData("21/05")]
    [InlineData("21/+25")]
    [InlineData("21/2")]
    [InlineData("b,20Q")]
    [InlineData("n,200")]
    [InlineData("3,40Q")]
    [InlineData("u\".")]
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
