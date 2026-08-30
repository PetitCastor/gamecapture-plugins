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

    // 19200 is Savrilium x6 and Aslarite x5 alike. It used to return no match, which the plugin then
    // scored as the badge having vanished — so scanning one of those rocks actively hid the overlay.
    // Both candidates are now reported and the caller shows both.
    [Fact]
    public void LoadEmbedded_ReportsBothCandidatesForAnOverlappingClusterTotal()
    {
        var table = SignatureTable.LoadEmbedded();

        Assert.True(table.TryMatch(19200, 0, out var match));
        Assert.Equal("Savrilium", match.Name);
        Assert.Equal(6, match.Count);
        Assert.Equal("Aslarite", match.AlternateName);
        Assert.Equal(5, match.AlternateCount);
        Assert.Equal("Savrilium x6 / Aslarite x5", match.Cluster);
        Assert.Equal("Aslarite x5", match.Alternate);
    }

    [Fact]
    public void LoadEmbedded_LeavesAnUnambiguousMatchWithNoAlternate()
    {
        var table = SignatureTable.LoadEmbedded();

        Assert.True(table.TryMatch(17200, 40, out var match));
        Assert.Null(match.AlternateName);
        Assert.Equal(0, match.AlternateCount);
        Assert.Equal("Ice x4", match.Cluster);
        Assert.Equal("", match.Alternate);
    }

    // Reporting both candidates is not a licence to skip the tolerance: a tied pair still has to be
    // close enough to be that total at all. (Built from a synthetic table because the shipped one's
    // only tie is exact — stepping away from 19200 moves one candidate strictly closer and breaks it.)
    [Fact]
    public void TryMatch_AnAmbiguousTotalStillRespectsTheTolerance()
    {
        var table = LoadTable("{\"entries\":[{\"name\":\"First\",\"signature\":100.0},{\"name\":\"Second\",\"signature\":102.0}]}");

        Assert.True(table.TryMatch(101, 1, out var within));
        Assert.Equal("Second", within.AlternateName);

        Assert.False(table.TryMatch(101, 0.5, out _));
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
    [InlineData(500.0, 10, true, "Alpha", 5)]
    [InlineData(509.9, 10, true, "Alpha", 5)]
    [InlineData(510.0, 10, true, "Alpha", 5)]
    [InlineData(510.01, 10, false, "", 0)]
    public void TryMatch_AppliesAbsoluteTolerance(
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

    // The tolerance used to be a FRACTION of the cluster total, so the window grew with the count while
    // the derived grid got denser — the readings least able to identify an ore were judged the most
    // leniently. The same absolute slack must now apply at one ore and at six.
    [Theory]
    [InlineData(110.0, 1)]
    [InlineData(610.0, 6)]
    public void TryMatch_ToleranceDoesNotWidenWithClusterCount(double signature, int expectedCount)
    {
        var table = LoadTable("{\"entries\":[{\"name\":\"Alpha\",\"signature\":100.0}]}");

        Assert.True(table.TryMatch(signature, 10, out var match));
        Assert.Equal(expectedCount, match.Count);
        Assert.False(table.TryMatch(signature + 0.01, 10, out _));
    }

    // Regression for the reported Ice/Bexalite flip: 17200 is Ice x4 exactly, and 18200 is one 7→8
    // slip away from it. Under the old 2% window 18200 landed inside ±360 of Bexalite x5 and was
    // reported as a confident, wrong ore.
    [Fact]
    public void LoadEmbedded_RejectsASlipThatTheRelativeToleranceUsedToAccept()
    {
        var table = SignatureTable.LoadEmbedded();

        Assert.True(table.TryMatch(17200, 40, out var ice));
        Assert.Equal("Ice", ice.Name);
        Assert.Equal(4, ice.Count);

        Assert.False(table.TryMatch(18200, 40, out _));
    }

    // Characterisation, not an endorsement: this pins the honest limit of identifying a rock from one
    // number. Corundum x3 (12675) and Quantanium x4 (12680) are five apart, so a slipped digit lands on
    // the neighbour with a delta of ZERO and no tolerance can tell it from a correct reading. The fix
    // for the reported flip is SignatureConsensus, which rejects a slip that does not repeat — a slip
    // that repeats steadily is not detectable here at all, and shrinking the tolerance would not help.
    [Fact]
    public void LoadEmbedded_CannotDistinguishNeighbouringTotalsFromTheNumberAlone()
    {
        var table = SignatureTable.LoadEmbedded();

        Assert.True(table.TryMatch(12675, 40, out var corundum));
        Assert.Equal("Corundum", corundum.Name);
        Assert.Equal(3, corundum.Count);

        Assert.True(table.TryMatch(12680, 40, out var quantanium));
        Assert.Equal("Quantanium", quantanium.Name);
        Assert.Equal(4, quantanium.Count);
        Assert.Equal(0, quantanium.Delta); // exact, and therefore indistinguishable from a real reading
    }

    [Fact]
    public void TryMatch_EqualCandidatesReportBoth()
    {
        var table = LoadTable("{\"entries\":[{\"name\":\"First\",\"signature\":100.0},{\"name\":\"Second\",\"signature\":102.0}]}");

        Assert.True(table.TryMatch(101, 10, out var match));

        // Which of the two is the "winner" is table order and nothing more — the pair is unordered.
        Assert.Equal("First", match.Name);
        Assert.Equal("Second", match.AlternateName);
        Assert.Equal(1, match.AlternateCount);
    }

    [Fact]
    public void TryMatch_EqualDecimalCandidatesReportBoth()
    {
        var table = LoadTable("{\"entries\":[{\"name\":\"First\",\"signature\":100.1},{\"name\":\"Second\",\"signature\":100.3}]}");

        Assert.True(table.TryMatch(100.2, 1, out var match));
        Assert.Equal("First", match.Name);
        Assert.Equal("Second", match.AlternateName);
        Assert.Equal(1, match.AlternateCount);
    }

    // A candidate that is strictly closer than an earlier tie discards it, rather than carrying a
    // runner-up that is no longer equally good.
    [Fact]
    public void TryMatch_ABetterCandidateLaterInTheWalkDiscardsAnEarlierTie()
    {
        var table = LoadTable(
            "{\"entries\":[{\"name\":\"First\",\"signature\":100.0}," +
            "{\"name\":\"Second\",\"signature\":102.0}," +
            "{\"name\":\"Exact\",\"signature\":101.0}]}");

        Assert.True(table.TryMatch(101, 10, out var match));
        Assert.Equal("Exact", match.Name);
        Assert.Null(match.AlternateName);
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
