namespace SignaturePlugin;

/// <summary>The table entry and cluster count selected for an observed mining signature.</summary>
/// <param name="AlternateName">
/// The ore of an equally good reading of the same total, or null when the reading is unambiguous.
/// Two entries can derive the identical cluster total — 19200 is Savrilium x6 and Aslarite x5 alike —
/// and nothing in the number distinguishes them. That case used to return no match at all, which the
/// plugin then scored as the badge having vanished; carrying the runner-up instead lets the overlay
/// say what it actually knows.
/// </param>
/// <param name="AlternateCount">
/// The cluster count of <paramref name="AlternateName"/>, or 0 when there is no alternate. It is not
/// the same as <paramref name="Count"/>: the two candidates reach the same total precisely because
/// they multiply different unit signatures by different counts.
/// </param>
/// <remarks>
/// Only one alternate is carried. A three-way tie is possible in principle from a user-edited table
/// and would report the first two in table order; the shipped table has exactly one tie, and it is
/// two-way.
/// </remarks>
public readonly record struct SignatureMatch(
    string Name, string Kind, double TableSignature, int Count, double Delta,
    string? AlternateName = null, int AlternateCount = 0)
{
    /// <summary>
    /// The whole reading as one display string — <c>"Ice x4"</c>, or <c>"Savrilium x6 / Aslarite x5"</c>
    /// when the total is ambiguous. This is what the overlay's <c>{cluster}</c> placeholder renders:
    /// a template built from <c>{name}</c> and <c>{count}</c> separately cannot express a tie, and
    /// showing only the winner of a coin-flip would be a confident wrong answer.
    /// </summary>
    public string Cluster => AlternateName is null
        ? $"{Name} x{Count}"
        : $"{Name} x{Count} / {AlternateName} x{AlternateCount}";

    /// <summary>The runner-up as a display string, or an empty string when the reading is unambiguous.</summary>
    public string Alternate => AlternateName is null ? "" : $"{AlternateName} x{AlternateCount}";
}
