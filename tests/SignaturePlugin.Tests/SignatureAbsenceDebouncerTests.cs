using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// Offline tests for <see cref="SignatureAbsenceDebouncer"/> — the pure seam that guards the overlay's
/// disappearance against single-tick OCR flicker on the fragile signature-counter crop.
/// </summary>
public class SignatureAbsenceDebouncerTests
{
    [Fact]
    public void MissingBeforeAnyMatch_IsNoop()
    {
        var d = new SignatureAbsenceDebouncer();
        Assert.False(d.ObserveMissing());
    }

    [Fact]
    public void StreakResetsOnAnyMatch()
    {
        var d = new SignatureAbsenceDebouncer();
        d.ObserveMatch();

        d.ObserveMissing(); // away 1
        d.ObserveMissing(); // away 2
        d.ObserveMatch();   // resets the streak — badge never really left

        Assert.False(d.ObserveMissing()); // away 1 again — not yet confirmed
        Assert.False(d.ObserveMissing()); // away 2 again — still short of ConfirmTicks (3)
    }

    [Fact]
    public void ResetStreak_ReopensTheGraceWindow()
    {
        var d = new SignatureAbsenceDebouncer();
        d.ObserveMatch();

        d.ObserveMissing(); // away 1
        d.ObserveMissing(); // away 2
        d.ResetStreak();    // e.g. a dropped-ticks gap — not evidence of absence

        Assert.False(d.ObserveMissing()); // away 1 again since the reset
        Assert.False(d.ObserveMissing()); // away 2 again — still short of ConfirmTicks (3)
    }

    [Fact]
    public void ConfirmationFiresOnceNotOnEverySubsequentMissingTick()
    {
        var d = new SignatureAbsenceDebouncer();
        d.ObserveMatch();

        d.ObserveMissing(); // away 1
        d.ObserveMissing(); // away 2
        var confirmed = d.ObserveMissing(); // away 3 — ConfirmTicks reached

        Assert.True(confirmed);
        Assert.False(d.ObserveMissing()); // already confirmed — no repeat signal
        Assert.False(d.ObserveMissing());
    }

    [Fact]
    public void MarkCleared_SuppressesAFurtherConfirmation_FromAPartialAwayStreak()
    {
        var d = new SignatureAbsenceDebouncer();
        d.ObserveMatch();

        d.ObserveMissing(); // away 1
        d.ObserveMissing(); // away 2
        d.MarkCleared();    // an out-of-band clear happens (e.g. a lost session)

        // Without MarkCleared resetting _visible too, one more miss would confirm the leftover streak
        // and fire a second, redundant clear.
        Assert.False(d.ObserveMissing());
        Assert.False(d.ObserveMissing());
        Assert.False(d.ObserveMissing());
    }

    [Fact]
    public void MarkCleared_ThenMatch_IsTrackedAsAFreshSighting()
    {
        var d = new SignatureAbsenceDebouncer();
        d.ObserveMatch();
        d.ObserveMissing();
        d.MarkCleared();

        d.ObserveMatch(); // re-sighting after the clear

        d.ObserveMissing(); // away 1
        d.ObserveMissing(); // away 2
        Assert.True(d.ObserveMissing()); // away 3 — confirms against the fresh streak
    }

    [Fact]
    public void AChangedMatch_MidAwayStreak_ResetsTheStreakLikeAnyOtherMatch()
    {
        // ObserveMatch only signals "a reading was read this tick" — it has no notion of which value,
        // so a different value interrupting a partial away-streak must reset it exactly like a repeat
        // of the original value would.
        var d = new SignatureAbsenceDebouncer();
        d.ObserveMatch(); // e.g. "3600"

        d.ObserveMissing(); // away 1
        d.ObserveMatch();   // e.g. "7200" — a different value, still a match

        Assert.False(d.ObserveMissing()); // away 1 of a brand-new streak, not away 2
        Assert.False(d.ObserveMissing()); // away 2 — still short of ConfirmTicks (3)
    }

    [Fact]
    public void AfterConfirmedAbsence_NewMatchIsTrackedFresh()
    {
        var d = new SignatureAbsenceDebouncer();
        d.ObserveMatch();
        d.ObserveMissing();
        d.ObserveMissing();
        d.ObserveMissing(); // confirmed absent

        d.ObserveMatch(); // badge shown again

        Assert.False(d.ObserveMissing()); // away 1 of a brand-new streak
        Assert.False(d.ObserveMissing()); // away 2
        Assert.True(d.ObserveMissing());  // away 3 — confirms again
    }
}
