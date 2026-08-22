using Xunit;

namespace SignaturePlugin.Tests;

public class SignatureTableTests
{
    [Fact]
    public void LoadEmbedded_MatchesListedOreEntries()
    {
        var table = SignatureTable.LoadEmbedded();

        Assert.True(table.TryMatch(3600, 0, out var ore));
        Assert.Equal("Bexalite", ore.Name);
        Assert.Equal("ore", ore.Kind);
        Assert.Equal(1, ore.Count);

        Assert.True(table.TryMatch(9510, 0, out var cluster));
        Assert.Equal("Quantanium", cluster.Name);
        Assert.Equal("ore", cluster.Kind);
        Assert.Equal(3, cluster.Count);
    }

    [Fact]
    public void LoadEmbedded_DoesNotInferUnlistedSignatureMultiples()
    {
        var table = SignatureTable.LoadEmbedded();

        Assert.False(table.TryMatch(31700, 0, out _));
    }

    [Fact]
    public void LoadFrom_MatchesListedSignature()
    {
        var path = WriteTable("{\"entries\":[{\"name\":\"First\",\"signature\":200.0,\"count\":2}]}");
        try
        {
            var table = SignatureTable.LoadFrom(path);

            Assert.True(table.TryMatch(200, 0, out var match));
            Assert.Equal("First", match.Name);
            Assert.Equal(2, match.Count);
            Assert.Equal(200, match.TableSignature);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFrom_MalformedJsonThrowsClearException()
    {
        var path = WriteTable("{\"entries\":[");
        try
        {
            var exception = Assert.Throws<InvalidDataException>(() => SignatureTable.LoadFrom(path));

            Assert.Contains("malformed JSON", exception.Message);
            Assert.Contains(path, exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"entries\":[]}")]
    [InlineData("{\"entries\":[{\"name\":\"\",\"signature\":100,\"count\":1}]}")]
    [InlineData("{\"entries\":[{\"name\":\"Alpha\",\"signature\":0,\"count\":1}]}")]
    [InlineData("{\"entries\":[{\"name\":\"Alpha\",\"signature\":100,\"count\":0}]}")]
    [InlineData("{\"entries\":[{\"name\":\"Alpha\",\"signature\":\"100\",\"count\":1}]}")]
    public void LoadFrom_InvalidTableDataThrows(string json)
    {
        var path = WriteTable(json);
        try
        {
            Assert.Throws<InvalidDataException>(() => SignatureTable.LoadFrom(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(100.0, 0.02, true, "Alpha", 1)]
    [InlineData(101.9, 0.02, true, "Alpha", 1)]
    [InlineData(102.0, 0.02, true, "Alpha", 1)]
    [InlineData(102.01, 0.02, false, "", 0)]
    public void TryMatch_AppliesRelativeTolerance(
        double signature, double tolerance, bool expectedMatch, string expectedName, int expectedCount)
    {
        var table = LoadTable("{\"entries\":[{\"name\":\"Alpha\",\"signature\":100.0,\"count\":1},{\"name\":\"Beta\",\"signature\":200.0,\"count\":2}]}");

        var matched = table.TryMatch(signature, tolerance, out var match);

        Assert.Equal(expectedMatch, matched);
        if (expectedMatch)
        {
            Assert.Equal(expectedName, match.Name);
            Assert.Equal(expectedCount, match.Count);
        }
    }

    [Fact]
    public void TryMatch_EqualCandidatesReturnUnknown()
    {
        var table = LoadTable("{\"entries\":[{\"name\":\"First\",\"signature\":100.0,\"count\":1},{\"name\":\"Second\",\"signature\":102.0,\"count\":1}]}");

        Assert.False(table.TryMatch(101, 0.02, out _));
    }

    private static SignatureTable LoadTable(string json)
    {
        var path = WriteTable(json);
        try
        {
            return SignatureTable.LoadFrom(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTable(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"signature-table-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
