namespace SignaturePlugin;

/// <summary>
/// Holds the displayed signature still until a *different* number proves itself, so one misread digit
/// cannot swap the named ore under the player.
/// </summary>
/// <remarks>
/// Same philosophy as <see cref="SignatureAbsenceDebouncer"/>, applied to the value rather than to its
/// presence: the first reading is trusted immediately — the overlay must still appear the moment a scan
/// completes — and only a *change* has to earn it, by repeating on <see cref="ChangeConfirmTicks"/>
/// consecutive ticks.
/// </remarks>
/// <remarks>
/// This filter is about *stability*, not accuracy: it guarantees the displayed ore stops changing
/// under a reading that wobbles, and guarantees nothing at all about a reading that is steadily wrong.
/// Nothing at this layer could — the derived grid has totals as little as 5 apart (Corundum x3 is
/// 12675, Quantanium x4 is 12680), so a stable misread can land exactly on a neighbour with a delta
/// of zero and is then indistinguishable from a correct reading. Tightening the tolerance does not
/// help there either; only a better crop or a second ROI would.
/// </remarks>
/// <remarks>
/// Consensus deliberately runs on the parsed NUMBER, upstream of <see cref="SignatureTable"/>. The
/// number is the atomic OCR fact; the ore name is a derived interpretation of it, and one wrong digit
/// moves that interpretation to a confident, plausible, wrong answer. A real reading of 17200 is
/// Ice x4 exactly, while 18200 — a single 7→8 slip, the same glyph confusion <see cref="Rois.Counter"/>
/// documents for this HUD font — lands 200 off Bexalite x5. Filtering after the match would compare
/// those as two legitimate candidates; filtering before it means the slip never reaches the table.
/// </remarks>
internal sealed class SignatureConsensus
{
    /// <summary>
    /// Consecutive identical readings a new number needs before it replaces the accepted one. Two is
    /// enough to reject the single-tick slips that cause the flip, and costs one scan interval (500 ms
    /// by default) of lag when the player genuinely scans a different rock.
    /// </summary>
    /// <remarks>
    /// Deliberately not raised to match <see cref="SignatureAbsenceDebouncer.ConfirmTicks"/>, which
    /// guards a different thing. This filter defends against a reading that *flickers* — the reported
    /// symptom, where the same rock alternated between Ice and Bexalite — and two ticks is all that
    /// takes. It cannot defend against a misread that is stable, and no threshold can: a persistently
    /// wrong number that happens to land on a real cluster total is indistinguishable from a correct
    /// one, because the number is the only evidence there is. Raising this would buy nothing against
    /// that case and would cost latency on every genuine rock change.
    /// </remarks>
    internal const int ChangeConfirmTicks = 2;

    private bool _hasAccepted;
    private double _accepted;
    private double _candidate;
    private int _candidateStreak;

    /// <summary>
    /// Feeds one tick's parsed number and returns the number the rest of the plugin should act on —
    /// which is the previously accepted one whenever <paramref name="read"/> is an unconfirmed change.
    /// </summary>
    public double Observe(double read)
    {
        if (!_hasAccepted)
        {
            _hasAccepted = true;
            _accepted = read;
            _candidateStreak = 0;
            return _accepted;
        }

        // Exact equality on purpose: both sides came out of SignatureParser, which only ever yields
        // finite doubles, and two OCR passes over the same glyphs produce the same digits or different
        // ones — there is no rounding error to absorb here. Near-misses (17200 vs 17240) are meant to
        // read as a change and be held, not silently averaged in.
        if (read == _accepted)
        {
            _candidateStreak = 0; // the incumbent reasserted itself; any pending challenger is noise
            return _accepted;
        }

        if (_candidateStreak > 0 && read == _candidate)
        {
            if (++_candidateStreak >= ChangeConfirmTicks)
            {
                _accepted = read;
                _candidateStreak = 0;
            }

            return _accepted;
        }

        _candidate = read;
        _candidateStreak = 1;
        return _accepted;
    }

    /// <summary>
    /// The crop yielded no number this tick, so any pending challenger loses its streak.
    /// </summary>
    /// <remarks>
    /// Without this, "consecutive" would quietly mean "consecutive ticks that parsed", and a
    /// challenger could accumulate its confirmations either side of a blank frame — which is exactly
    /// the shape a flickering digit has. The incumbent is untouched: a blank tick is an absence of
    /// evidence, and deciding what it means about the badge is
    /// <see cref="SignatureAbsenceDebouncer"/>'s job, not this one's.
    /// </remarks>
    public void NoReading() => _candidateStreak = 0;

    /// <summary>
    /// The overlay was cleared, so there is no incumbent left to defend. The next reading is a fresh
    /// sighting and is accepted on the spot — without this, the first number of the next scan would be
    /// held for a tick against a value nothing is displaying any more.
    /// </summary>
    public void Reset()
    {
        _hasAccepted = false;
        _accepted = 0;
        _candidate = 0;
        _candidateStreak = 0;
    }
}
