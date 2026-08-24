namespace SignaturePlugin;

/// <summary>
/// Debounces the signature overlay's *disappearance* so a single OCR-flicker tick can't hide a badge
/// that is still on screen — modelled directly on <see cref="RefineryPlugin.SetupDepartureDebouncer"/>,
/// which exists for this same OCR-flicker problem.
/// </summary>
/// <remarks>
/// A match is trusted immediately (not debounced): a single good read shows the overlay right away,
/// same as the refinery debouncer's entry edge. Only *absence* requires proof — <see cref="ConfirmTicks"/>
/// consecutive missing ticks with no matching read in between. Any matching read resets the away-streak
/// to zero, so a value that blips out for a tick or two and comes right back is treated as never having
/// left.
/// </remarks>
/// <remarks>
/// A count of confirming FRAMES, deliberately not a wall-clock window. Replay runs the scan loop flat
/// out rather than sleeping the real scan interval between frames, so a wall-clock window would turn
/// "N confirming frames" into "however many frames elapse ≥1.5 s of real OCR time", which depends on
/// machine speed and makes replay non-deterministic. A frame count is invariant across live and replay,
/// which is the property the debounce needs.
/// </remarks>
internal sealed class SignatureAbsenceDebouncer
{
    /// <summary>Consecutive missing ticks required before an absence is trusted.</summary>
    internal const int ConfirmTicks = 3;

    private bool _visible;
    private int _awayStreak;

    /// <summary>A match was read this tick.</summary>
    public void ObserveMatch()
    {
        _visible = true;
        _awayStreak = 0;
    }

    /// <summary>No usable reading this tick. Returns true exactly on the tick absence is confirmed.</summary>
    public bool ObserveMissing()
    {
        if (!_visible) return false;

        if (++_awayStreak < ConfirmTicks) return false;

        _visible = false;
        _awayStreak = 0;
        return true;
    }

    /// <summary>Frames were missed; they are not evidence of absence.</summary>
    public void ResetStreak() => _awayStreak = 0;

    /// <summary>
    /// An out-of-band clear happened outside the confirm-tick gate (a lost session, not a missing OCR
    /// reading). Without this, a stale <c>_visible</c> would demand another confirmed absence before
    /// the next real disappearance can clear anything, and a partial away-streak from before the clear
    /// would silently carry over and fire a second, redundant clear a few ticks later.
    /// </summary>
    public void MarkCleared()
    {
        _visible = false;
        _awayStreak = 0;
    }
}
