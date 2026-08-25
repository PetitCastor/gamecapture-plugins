namespace SignaturePlugin;

/// <summary>
/// Debounces the signature overlay's *disappearance* so OCR flicker on this fragile crop can't hide a
/// badge that is still on screen — modelled directly on <see cref="RefineryPlugin.SetupDepartureDebouncer"/>,
/// which exists for this same OCR-flicker problem.
/// </summary>
/// <remarks>
/// A match is trusted immediately (not debounced): a single settled read shows the overlay right away,
/// same as the refinery debouncer's entry edge. Only *absence* requires proof, and only
/// <see cref="SignatureReading.Blank"/> counts as that proof — see <see cref="SignatureReading"/> for
/// why an unmatched number is presence, not absence. <see cref="ConfirmTicks"/> consecutive blank ticks
/// are required, and any non-blank reading resets the streak to zero.
/// </remarks>
/// <remarks>
/// <see cref="StaleTicks"/> is the counterweight to trusting unmatched readings. Holding the overlay on
/// every reading that isn't blank means a crop that somehow keeps yielding unmatchable digits — a HUD
/// element drifting under the rect, a table that has gone stale against a game patch — would pin a dead
/// value on screen forever, since <c>lingerMs</c> is 0 and nothing else ever hides it. This bounds that:
/// however the ticks are shaped, the overlay cannot outlive its last real match by more than
/// <see cref="StaleTicks"/> ticks.
/// </remarks>
/// <remarks>
/// Both are counts of confirming FRAMES, deliberately not wall-clock windows. Replay runs the scan loop
/// flat out rather than sleeping the real scan interval between frames, so a wall-clock window would
/// turn "N confirming frames" into "however many frames elapse ≥N s of real OCR time", which depends on
/// machine speed and makes replay non-deterministic. A frame count is invariant across live and replay,
/// which is the property the debounce needs.
/// </remarks>
internal sealed class SignatureAbsenceDebouncer
{
    /// <summary>
    /// Consecutive blank ticks required before an absence is trusted. At the engine's default 500 ms
    /// scan interval this is a 3 s grace window. The previous 3 (1.5 s) was chosen on the assumption
    /// that misreads are independent per frame; they are not — the same font under the same lighting
    /// misreads the same way for as long as the shot holds, so three in a row was routine and the
    /// overlay kept dying mid-scan.
    /// </summary>
    internal const int ConfirmTicks = 6;

    /// <summary>
    /// Ticks without a single matched reading after which the overlay is cleared regardless of what
    /// the crop has been yielding. ~10 s at the default scan interval — long enough that it never
    /// competes with <see cref="ConfirmTicks"/> during normal flicker, short enough to be a backstop
    /// rather than a leak.
    /// </summary>
    internal const int StaleTicks = 20;

    private bool _visible;
    private int _blankStreak; // consecutive Blank ticks since the last non-blank reading, while visible
    private int _sinceMatch;  // ticks since the last Matched reading, while visible

    /// <summary>
    /// Feeds one tick's reading. Returns true exactly on the tick an absence is confirmed — never on a
    /// <see cref="SignatureReading.Matched"/> tick, and never twice for the same disappearance.
    /// </summary>
    public bool Observe(SignatureReading reading)
    {
        if (reading == SignatureReading.Matched)
        {
            _visible = true;
            _blankStreak = 0;
            _sinceMatch = 0;
            return false;
        }

        if (!_visible) return false;

        // Unmatched proves the badge is still drawn, so it resets the blank streak exactly as a match
        // would — but it does NOT reset _sinceMatch, which is what stops it from holding forever.
        _blankStreak = reading == SignatureReading.Blank ? _blankStreak + 1 : 0;
        _sinceMatch++;

        if (_blankStreak < ConfirmTicks && _sinceMatch < StaleTicks) return false;

        MarkCleared();
        return true;
    }

    /// <summary>Frames were missed; they are not evidence of absence, nor of staleness.</summary>
    public void ResetStreaks()
    {
        _blankStreak = 0;
        _sinceMatch = 0;
    }

    /// <summary>
    /// An out-of-band clear happened outside the confirm-tick gate (a lost session, not a missing OCR
    /// reading). Without this, a stale <c>_visible</c> would demand another confirmed absence before
    /// the next real disappearance can clear anything, and a partial away-streak from before the clear
    /// would silently carry over and fire a second, redundant clear a few ticks later.
    /// </summary>
    public void MarkCleared()
    {
        _visible = false;
        _blankStreak = 0;
        _sinceMatch = 0;
    }
}
