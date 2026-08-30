using System.Text.Json;

namespace SignaturePlugin;

/// <summary>Data-driven mapping from one-ore RS signatures to ore clusters.</summary>
public sealed class SignatureTable
{
    private const string ConfigDirectoryName = "GameCapture";
    private const string PluginDirectoryName = "SignaturePlugin";
    private const string FileName = "signature-table.json";
    private const string ResourceName = "SignaturePlugin.Resources.signature-table.json";
    private const int MaximumClusterCount = 6;
    // JSON decimal values are represented as doubles. This admits the rounding noise from two
    // equivalent subtractions without confusing genuinely distinct cluster totals for a tie.
    private const double TieRelativeTolerance = 1e-12;
    private readonly IReadOnlyList<(string Name, double UnitSignature)> _entries;

    private SignatureTable(IReadOnlyList<(string Name, double UnitSignature)> entries)
        => _entries = entries;

    /// <summary>Loads the checked-in community-reference table embedded in the plugin.</summary>
    public static SignatureTable LoadEmbedded()
    {
        return Parse(ReadEmbeddedJson(), "embedded table");
    }

    /// <summary>Returns the per-user table location used by the plugin.</summary>
    public static string GetUserFilePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ConfigDirectoryName,
            PluginDirectoryName,
            FileName);

    /// <summary>
    /// Loads the user-editable table, creating it from the embedded defaults on first run.
    /// Existing files are never rewritten, including files that contain invalid JSON.
    /// </summary>
    public static SignatureTable LoadUserFile() => LoadOrCreate(GetUserFilePath());

    /// <summary>
    /// Loads a table from <paramref name="path"/>, creating that file from the embedded defaults
    /// when it does not yet exist.
    /// </summary>
    public static SignatureTable LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, ReadEmbeddedJson());
        }

        return LoadFrom(path);
    }

    /// <summary>Loads a user-editable table from JSON. Invalid table data throws InvalidDataException.</summary>
    public static SignatureTable LoadFrom(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), path);
    }

    private static string ReadEmbeddedJson()
    {
        var assembly = typeof(SignatureTable).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded signature table '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Matches the closest one-to-six-ore cluster derived from the listed unit signatures.
    /// </summary>
    /// <remarks>
    /// An equal-distance runner-up does not fail the match; it is reported alongside the winner in
    /// <see cref="SignatureMatch.AlternateName"/>, and the caller shows both rather than picking one.
    /// Failing outright was worse than it sounds: 19200 is a legitimate reading (Savrilium x6 and
    /// Aslarite x5 derive the same total) and the plugin scored the failure as the badge having
    /// vanished, so scanning one of those rocks hid the overlay. Which of two tied candidates is
    /// reported as the winner is table order, and nothing more — treat the pair as unordered.
    /// </remarks>
    /// <param name="tolerance">
    /// The largest ABSOLUTE distance from a derived cluster total that still counts as that cluster.
    /// It was once a fraction of the total, which widened the window precisely where the derived grid
    /// is densest — at six ores, totals sit 90 apart while 2% of the total is ±500 — so high-count
    /// clusters were the readings most likely to be identified as the wrong ore.
    /// <c>SignaturePlugin.MatchTolerance</c> carries the value the plugin passes and why.
    /// </param>
    public bool TryMatch(double signature, double tolerance, out SignatureMatch match)
    {
        match = default;
        if (!double.IsFinite(signature) || !double.IsFinite(tolerance) || tolerance < 0)
            return false;

        var found = false;
        var bestDelta = double.PositiveInfinity;
        string? bestName = null;
        var bestUnitSignature = 0.0;
        var bestResolvedSignature = 0.0;
        var bestCount = 0;

        // The runner-up is only kept while it ties the current best; a strictly better
        // candidate later in the walk discards it, which is why it is cleared in that branch too.
        string? tiedName = null;
        var tiedCount = 0;

        foreach (var entry in _entries)
        {
            for (var count = 1; count <= MaximumClusterCount; count++)
            {
                var resolvedSignature = entry.UnitSignature * count;
                var absoluteDelta = Math.Abs(signature - resolvedSignature);

                if (absoluteDelta < bestDelta && !AreEqualDeltas(absoluteDelta, bestDelta))
                {
                    found = true;
                    bestDelta = absoluteDelta;
                    bestName = entry.Name;
                    bestUnitSignature = entry.UnitSignature;
                    bestResolvedSignature = resolvedSignature;
                    bestCount = count;
                    tiedName = null;
                    tiedCount = 0;
                }
                else if (AreEqualDeltas(absoluteDelta, bestDelta) && tiedName is null)
                {
                    tiedName = entry.Name;
                    tiedCount = count;
                }
            }
        }

        if (!found || bestName is null || bestDelta > tolerance)
        {
            return false;
        }

        match = new SignatureMatch(bestName, "ore", bestUnitSignature, bestCount,
            signature - bestResolvedSignature, tiedName, tiedCount);
        return true;
    }

    private static bool AreEqualDeltas(double left, double right)
        => double.IsFinite(left) && double.IsFinite(right)
            && Math.Abs(left - right) <= Math.Max(1, Math.Max(left, right)) * TieRelativeTolerance;

    private static SignatureTable Parse(string json, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("entries", out var entriesElement) ||
                entriesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"The {source} must contain an 'entries' JSON array.");
            }

            var entries = new List<(string Name, double UnitSignature)>();
            var signatures = new HashSet<double>();
            foreach (var element in entriesElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("name", out var nameElement) ||
                    !element.TryGetProperty("signature", out var signatureElement) ||
                    nameElement.ValueKind != JsonValueKind.String ||
                    signatureElement.ValueKind != JsonValueKind.Number ||
                    !signatureElement.TryGetDouble(out var signature) ||
                    element.TryGetProperty("count", out _))
                {
                    throw new InvalidDataException(
                        $"Each entry in the {source} must contain name and signature, without count.");
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name) || !double.IsFinite(signature) ||
                    signature <= 0)
                {
                    throw new InvalidDataException(
                        $"Each entry in the {source} must have a non-empty name and a positive signature.");
                }

                if (!signatures.Add(signature))
                {
                    throw new InvalidDataException(
                        $"Each entry in the {source} must have a unique signature; duplicate signature {signature} was found.");
                }

                entries.Add((name, signature));
            }

            if (entries.Count == 0)
                throw new InvalidDataException($"The {source} must contain at least one signature entry.");

            return new SignatureTable(entries);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The signature table '{source}' is malformed JSON.", ex);
        }
    }
}
