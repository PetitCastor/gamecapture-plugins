using System.Text.Json;

namespace SignaturePlugin;

/// <summary>Ordered, data-driven mapping from mining RS signatures to ore or debris names.</summary>
public sealed class SignatureTable
{
    private const string ResourceName = "SignaturePlugin.Resources.signature-table.json";
    private readonly IReadOnlyList<(string Name, string Kind, double BaseSignature, int MaxCount)> _entries;

    private SignatureTable(IReadOnlyList<(string Name, string Kind, double BaseSignature, int MaxCount)> entries)
        => _entries = entries;

    /// <summary>Loads the checked-in community-reference table embedded in the plugin.</summary>
    public static SignatureTable LoadEmbedded()
    {
        var assembly = typeof(SignatureTable).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded signature table '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), "embedded table");
    }

    /// <summary>Loads a user-editable table from JSON. Invalid table data throws InvalidDataException.</summary>
    public static SignatureTable LoadFrom(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>
    /// Matches the closest base-signature multiple. Equal-distance candidates are ambiguous and
    /// therefore return false rather than silently selecting an ore over debris (or vice versa).
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
        string? bestKind = null;
        var bestTableSignature = 0.0;
        var bestCount = 0;

        foreach (var entry in _entries)
        {
            for (var count = 1; count <= entry.MaxCount; count++)
            {
                var tableSignature = entry.BaseSignature * count;
                var delta = signature - tableSignature;
                var absoluteDelta = Math.Abs(delta);

                if (absoluteDelta < bestDelta)
                {
                    found = true;
                    ambiguous = false;
                    bestDelta = absoluteDelta;
                    bestName = entry.Name;
                    bestKind = entry.Kind;
                    bestTableSignature = tableSignature;
                    bestCount = count;
                }
                else if (absoluteDelta == bestDelta)
                {
                    ambiguous = true;
                }
            }
        }

        if (!found || ambiguous || bestName is null || bestKind is null ||
            bestDelta > tolerance * bestTableSignature)
        {
            return false;
        }

        match = new SignatureMatch(bestName, bestKind, bestTableSignature, bestCount,
            signature - bestTableSignature);
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

            var entries = new List<(string Name, string Kind, double BaseSignature, int MaxCount)>();
            foreach (var element in entriesElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("name", out var nameElement) ||
                    !element.TryGetProperty("kind", out var kindElement) ||
                    !element.TryGetProperty("baseSignature", out var baseElement) ||
                    !element.TryGetProperty("maxCount", out var maxCountElement) ||
                    nameElement.ValueKind != JsonValueKind.String ||
                    kindElement.ValueKind != JsonValueKind.String ||
                    baseElement.ValueKind != JsonValueKind.Number ||
                    maxCountElement.ValueKind != JsonValueKind.Number ||
                    !baseElement.TryGetDouble(out var baseSignature) ||
                    !maxCountElement.TryGetInt32(out var maxCount))
                {
                    throw new InvalidDataException(
                        $"Each entry in the {source} must contain name, kind, baseSignature, and maxCount.");
                }

                var name = nameElement.GetString();
                var kind = kindElement.GetString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind) ||
                    !double.IsFinite(baseSignature) || baseSignature <= 0 || maxCount <= 0)
                {
                    throw new InvalidDataException(
                        $"Each entry in the {source} must have non-empty text, a positive baseSignature, and a positive maxCount.");
                }

                entries.Add((name, kind, baseSignature, maxCount));
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
