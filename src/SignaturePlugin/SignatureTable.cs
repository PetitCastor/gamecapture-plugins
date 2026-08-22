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
    /// Equal-distance candidates are ambiguous and therefore return false rather than silently
    /// selecting one ore cluster over another.
    /// </summary>
    public bool TryMatch(double signature, double tolerance, out SignatureMatch match)
    {
        match = default;
        if (!double.IsFinite(signature) || !double.IsFinite(tolerance) || tolerance < 0)
            return false;

        var found = false;
        var ambiguous = false;
        var bestDelta = double.PositiveInfinity;
        string? bestName = null;
        var bestUnitSignature = 0.0;
        var bestResolvedSignature = 0.0;
        var bestCount = 0;

        foreach (var entry in _entries)
        {
            for (var count = 1; count <= MaximumClusterCount; count++)
            {
                var resolvedSignature = entry.UnitSignature * count;
                var absoluteDelta = Math.Abs(signature - resolvedSignature);

                if (absoluteDelta < bestDelta)
                {
                    found = true;
                    ambiguous = false;
                    bestDelta = absoluteDelta;
                    bestName = entry.Name;
                    bestUnitSignature = entry.UnitSignature;
                    bestResolvedSignature = resolvedSignature;
                    bestCount = count;
                }
                else if (absoluteDelta == bestDelta)
                {
                    ambiguous = true;
                }
            }
        }

        if (!found || ambiguous || bestName is null ||
            bestDelta > tolerance * bestResolvedSignature)
        {
            return false;
        }

        match = new SignatureMatch(bestName, "ore", bestUnitSignature, bestCount,
            signature - bestResolvedSignature);
        return true;
    }

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
