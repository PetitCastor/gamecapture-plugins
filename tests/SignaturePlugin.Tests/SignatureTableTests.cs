using Xunit;

namespace SignaturePlugin.Tests;

public class SignatureTableTests
{
    [Fact]
    public void LoadEmbedded_MatchesOresAndDerivedClusterCounts()
    {
        var table = SignatureTable.LoadEmbedded();

        Assert.True(table.TryMatch(3600, 0, out var ore));
        Assert.Equal("Bexalite", ore.Name);
        Assert.Equal("ore", ore.Kind);
        Assert.Equal(1, ore.Count);

        Assert.True(table.TryMatch(21600, 0, out var cluster));
        Assert.Equal("Bexalite", cluster.Name);
        Assert.Equal("ore", cluster.Kind);
        Assert.Equal(6, cluster.Count);
    }

    [Fact]
    public void LoadEmbedded_DerivesCountsThroughSix()
    {
        var table = SignatureTable.LoadEmbedded();

        Assert.True(table.TryMatch(19020, 0, out var quantanium));
        Assert.Equal("Quantanium", quantanium.Name);
        Assert.Equal(6, quantanium.Count);

        Assert.False(table.TryMatch(31700, 0, out _));
    }

    [Fact]
    public void LoadEmbedded_LeavesOverlappingClusterTotalsUnknown()
    {
        var table = SignatureTable.LoadEmbedded();

        Assert.False(table.TryMatch(19200, 0, out _));
    }

    [Fact]
    public void LoadOrCreate_WritesEmbeddedDefaultsOnFirstRun()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"signature-table-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "signature-table.json");
        try
        {
            var table = SignatureTable.LoadOrCreate(path);

            Assert.True(File.Exists(path));
            Assert.True(table.TryMatch(3600, 0, out var match));
            Assert.Equal("Bexalite", match.Name);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadOrCreate_PreservesAnExistingUserTable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"signature-table-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"entries\":[{\"name\":\"Custom\",\"signature\":200.0}]}");

            var table = SignatureTable.LoadOrCreate(path);

            Assert.True(table.TryMatch(200, 0, out var match));
            Assert.Equal("Custom", match.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFrom_MatchesDerivedSignature()
    {
        var path = WriteTable("{\"entries\":[{\"name\":\"First\",\"signature\":200.0}]}");
        try
        {
            var table = SignatureTable.LoadFrom(path);

            Assert.True(table.TryMatch(1000, 0, out var match));
            Assert.Equal("First", match.Name);
            Assert.Equal(5, match.Count);
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
    [InlineData("{\"entries\":[{\"name\":\"\",\"signature\":100}]}")]
    [InlineData("{\"entries\":[{\"name\":\"Alpha\",\"signature\":0}]}")]
    [InlineData("{\"entries\":[{\"name\":\"Alpha\",\"signature\":\"100\"}]}")]
    [InlineData("{\"entries\":[{\"name\":\"Alpha\",\"signature\":100,\"count\":1}]}")]
    [InlineData("{\"entries\":[{\"name\":\"Alpha\",\"signature\":100},{\"name\":\"Beta\",\"signature\":100}]}")]
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
    [InlineData(500.0, 0.02, true, "Alpha", 5)]
    [InlineData(509.9, 0.02, true, "Alpha", 5)]
    [InlineData(510.0, 0.02, true, "Alpha", 5)]
    [InlineData(510.01, 0.02, false, "", 0)]
    public void TryMatch_AppliesRelativeTolerance(
        double signature, double tolerance, bool expectedMatch, string expectedName, int expectedCount)
    {
        var table = LoadTable("{\"entries\":[{\"name\":\"Alpha\",\"signature\":100.0}]}");

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
        var table = LoadTable("{\"entries\":[{\"name\":\"First\",\"signature\":100.0},{\"name\":\"Second\",\"signature\":102.0}]}");

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
