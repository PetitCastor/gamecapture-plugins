using System.Globalization;
using System.Text.RegularExpressions;

namespace SignaturePlugin;

/// <summary>Parses the numeric signature from the OCR text of the scan-mode panel.</summary>
public static partial class SignatureParser
{
    // The OCR region may include a label or a unit. Commas and spaces are accepted as thousands
    // separators; the decimal separator is deliberately invariant-culture '.'. No OCR letter
    // folds are applied until a captured corpus proves one is needed.
    [GeneratedRegex(@"(?<![\d.,])(?<number>[+-]?(?:\d[\d,.\s]*?|\.\d+))(?![\d.,]|\s*\d)")]
    private static partial Regex NumberToken();

    [GeneratedRegex(@"^[+-]?(?:(?:\d+|\d{1,3}(?:,\d{3})+|\d{1,3}(?:\s+\d{3})+)(?:\.\d+)?|\.\d+)$")]
    private static partial Regex ValidNumberToken();

    /// <summary>
    /// Extracts and parses one numeric token. Invalid OCR is expected input and never escapes as an
    /// exception; on failure, <paramref name="signature"/> is always zero.
    /// </summary>
    public static bool TryParse(string ocrText, out double signature)
    {
        signature = 0;

        if (string.IsNullOrWhiteSpace(ocrText))
            return false;

        ocrText = ocrText.Trim();
        var match = NumberToken().Match(ocrText);
        if (!match.Success)
            return false;

        var token = match.Groups["number"].Value;
        if (!ValidNumberToken().IsMatch(token))
            return false;

        var normalized = string.Concat(token.Where(c => c != ',' && !char.IsWhiteSpace(c)));
        return double.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out signature)
            && double.IsFinite(signature);
    }
}
