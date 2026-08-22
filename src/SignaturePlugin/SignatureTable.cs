using System.Text.Json;

namespace SignaturePlugin;

/// <summary>Data-driven mapping from observed mining RS signatures to ore clusters.</summary>
public sealed class SignatureTable
{
    private const string ResourceName = "SignaturePlugin.Resources.signature-table.json";
    private readonly IReadOnlyList<(string Name, double Signature, int Count)> _entries;

    private SignatureTable(IReadOnlyList<(string Name, double Signature, int Count)> entries)
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
    /// Matches the closest listed signature. Equal-distance candidates are ambiguous and therefore
    /// return false rather than silently selecting one ore cluster over another.
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
        var bestTableSignature = 0.0;
        var bestCount = 0;

        foreach (var entry in _entries)
        {
            var delta = signature - entry.Signature;
            var absoluteDelta = Math.Abs(delta);

            if (absoluteDelta < bestDelta)
            {
                found = true;
                ambiguous = false;
                bestDelta = absoluteDelta;
                bestName = entry.Name;
                bestTableSignature = entry.Signature;
                bestCount = entry.Count;
            }
            else if (absoluteDelta == bestDelta)
            {
                ambiguous = true;
            }
        }

        if (!found || ambiguous || bestName is null ||
            bestDelta > tolerance * bestTableSignature)
        {
            return false;
        }

        match = new SignatureMatch(bestName, "ore", bestTableSignature, bestCount,
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

            var entries = new List<(string Name, double Signature, int Count)>();
            foreach (var element in entriesElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("name", out var nameElement) ||
                    !element.TryGetProperty("signature", out var signatureElement) ||
                    !element.TryGetProperty("count", out var countElement) ||
                    nameElement.ValueKind != JsonValueKind.String ||
                    signatureElement.ValueKind != JsonValueKind.Number ||
                    countElement.ValueKind != JsonValueKind.Number ||
                    !signatureElement.TryGetDouble(out var signature) ||
                    !countElement.TryGetInt32(out var count))
                {
                    throw new InvalidDataException(
                        $"Each entry in the {source} must contain name, signature, and count.");
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name) || !double.IsFinite(signature) ||
                    signature <= 0 || count <= 0)
                {
                    throw new InvalidDataException(
                        $"Each entry in the {source} must have a non-empty name, a positive signature, and a positive count.");
                }

                entries.Add((name, signature, count));
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
