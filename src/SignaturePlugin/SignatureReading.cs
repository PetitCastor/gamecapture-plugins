namespace SignaturePlugin;

/// <summary>
/// What one tick's counter crop yielded, graded by what it actually proves about the badge being
/// on screen.
/// </summary>
/// <remarks>
/// The distinction between <see cref="Blank"/> and <see cref="Unmatched"/> is the whole point of
/// this enum. The plugin used to collapse both into a single "missing" branch, which meant a
/// perfectly legible number that happened to resolve to no ore cluster was scored as *the badge
/// vanished* and, three ticks later, hid the overlay while the player was still looking at the
/// number. One misread digit is enough to land in a gap in the table's derived grid, so that path
/// fired constantly.
/// </remarks>
internal enum SignatureReading
{
    /// <summary>
    /// No number at all — nothing in the crop parsed as a figure. The only reading that is evidence
    /// the badge has actually left the screen: once the pin is gone the crop sits over the game
    /// world, and OCR over terrain returns nothing far more often than it invents digits.
    /// </summary>
    Blank,

    /// <summary>
    /// Digits were read, but they resolve to no ore cluster. Evidence the badge is still *there* —
    /// something drew glyphs in that crop — and no evidence at all that it left.
    /// </summary>
    Unmatched,

    /// <summary>A settled value that resolved to an ore cluster.</summary>
    Matched,
}
