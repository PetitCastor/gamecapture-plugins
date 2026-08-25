using System.Globalization;
using System.Text.RegularExpressions;

namespace SignaturePlugin;

/// <summary>Parses the numeric signature from the OCR text of the scan-mode panel.</summary>
public static partial class SignatureParser
{
    /// <summary>
    /// Windows OCR renders this HUD font's thousands comma as a forward slash often enough that it
    /// dominates: in a captured live run, roughly half of all non-blank readings of 21,425 came back
    /// as <c>21/425</c>. Folded before matching rather than admitted as a separator inside the
    /// pattern, so <see cref="ValidNumberToken"/> still gets to insist on a well-formed group — a
    /// fold that produces nonsense (<c>21/05</c> → <c>21,05</c>) is then rejected outright instead of
    /// being read as some other number.
    /// </summary>
    /// <remarks>
    /// This is the first OCR fold this parser has taken, and it is here because a corpus proved it,
    /// not on suspicion. Other confusions in the same capture (<c>21k25</c>, <c>u".</c>) are left to
    /// fail as unreadable: they are rarer, and folding them would guess at digits rather than at a
    /// separator.
    /// </remarks>
    private const char CommaLookalike = '/';

    // The OCR region may include a label or a unit. Commas and spaces are accepted as thousands
    // separators; the decimal separator is deliberately invariant-culture '.'.
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

        ocrText = ocrText.Trim().Replace(CommaLookalike, ',');
        var match = NumberToken().Match(ocrText);
        if (!match.Success)
            return false;

        var token = match.Groups["number"].Value;
        if (!ValidNumberToken().IsMatch(token))
            return false;

        // A token that leaves digits behind is a truncation, not a reading, and returning the prefix
        // is far worse than returning nothing. '21/425' used to yield 21 — a number that parses, that
        // no cluster matches, and that the caller's consensus filter would then defend as though it
        // were a real observation. In a captured live run that enthroned 21 as the accepted value and
        // deadlocked the overlay on a stale ore for sixteen seconds, because every other tick misread
        // the same way and kept re-asserting it against the true reading.
        //
        // Assumes a number-only crop, which is what Rois.Counter is calibrated to. A region that
        // deliberately included a digit-bearing label would need this comparison scoped to the token's
        // surroundings instead of the whole string.
        if (CountDigits(token) != CountDigits(ocrText))
            return false;

        var normalized = string.Concat(token.Where(c => c != ',' && !char.IsWhiteSpace(c)));
        return double.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out signature)
            && double.IsFinite(signature);
    }

    private static int CountDigits(string text)
    {
        var digits = 0;
        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c))
                digits++;
        }

        return digits;
    }
}
