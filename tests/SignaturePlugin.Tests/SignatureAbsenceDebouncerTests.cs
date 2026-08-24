using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// Offline tests for <see cref="SignatureAbsenceDebouncer"/> — the pure seam that guards the overlay's
/// disappearance against OCR flicker on the fragile signature-counter crop.
/// </summary>
public class SignatureAbsenceDebouncerTests
{
    private static bool Blank(SignatureAbsenceDebouncer d) => d.Observe(SignatureReading.Blank);
    private static bool Unmatched(SignatureAbsenceDebouncer d) => d.Observe(SignatureReading.Unmatched);
    private static bool Matched(SignatureAbsenceDebouncer d) => d.Observe(SignatureReading.Matched);

    /// <summary>Blank ticks one short of confirming, so the next one fires.</summary>
    private static void BlankToTheBrink(SignatureAbsenceDebouncer d)
    {
        for (var i = 0; i < SignatureAbsenceDebouncer.ConfirmTicks - 1; i++)
            Assert.False(Blank(d));
    }

    [Fact]
    public void BlankBeforeAnyMatch_IsNoop()
    {
        var d = new SignatureAbsenceDebouncer();
        Assert.False(Blank(d));
    }

    [Fact]
    public void AMatchNeverConfirmsAnAbsence()
    {
        var d = new SignatureAbsenceDebouncer();
        Assert.False(Matched(d));
    }

    [Fact]
    public void StreakResetsOnAnyMatch()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);

        BlankToTheBrink(d);
        Matched(d); // resets the streak — badge never really left

        BlankToTheBrink(d);
        Assert.True(Blank(d)); // confirms only against the fresh streak
    }

    // The heart of the overlay-vanishing bug: a legible number that resolves to no ore cluster is
    // proof the badge is still drawn. It used to count toward the disappearance, so a run of misread
    // digits hid an overlay the player was still looking at.
    [Fact]
    public void UnmatchedReadings_PastTheBlankConfirmWindow_DoNotConfirmAnAbsence()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);

        // Twice the blank-confirm window: under the old collapse of "blank" and "unmatched" into one
        // "missing" branch, this run would have hidden the overlay twice over.
        Assert.True(SignatureAbsenceDebouncer.ConfirmTicks * 2 < SignatureAbsenceDebouncer.StaleTicks);
        for (var i = 0; i < SignatureAbsenceDebouncer.ConfirmTicks * 2; i++)
            Assert.False(Unmatched(d));
    }

    [Fact]
    public void AnUnmatchedReadingResetsTheBlankStreak()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);

        BlankToTheBrink(d);
        Assert.False(Unmatched(d)); // digits are back — the badge is still there

        BlankToTheBrink(d);
        Assert.True(Blank(d)); // needs a whole fresh run of blanks
    }

    // The counterweight to trusting unmatched readings: they hold the overlay, but they cannot hold it
    // indefinitely, or a crop that never matches again would pin a dead value on screen forever.
    [Fact]
    public void UnmatchedReadingsStillGoStaleEventually()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);

        for (var i = 0; i < SignatureAbsenceDebouncer.StaleTicks - 1; i++)
            Assert.False(Unmatched(d));

        Assert.True(Unmatched(d)); // StaleTicks without a single match
    }

    [Fact]
    public void AMatchResetsTheStaleCounter()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);

        for (var i = 0; i < SignatureAbsenceDebouncer.StaleTicks - 1; i++)
            Assert.False(Unmatched(d));

        Matched(d);

        for (var i = 0; i < SignatureAbsenceDebouncer.StaleTicks - 1; i++)
            Assert.False(Unmatched(d));
    }

    [Fact]
    public void ResetStreaks_ReopensTheGraceWindow()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);

        BlankToTheBrink(d);
        d.ResetStreaks(); // e.g. a dropped-ticks gap — not evidence of absence

        BlankToTheBrink(d);
        Assert.True(Blank(d));
    }

    [Fact]
    public void ResetStreaks_AlsoReopensTheStaleWindow()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);

        for (var i = 0; i < SignatureAbsenceDebouncer.StaleTicks - 1; i++)
            Assert.False(Unmatched(d));

        d.ResetStreaks();

        for (var i = 0; i < SignatureAbsenceDebouncer.StaleTicks - 1; i++)
            Assert.False(Unmatched(d));
    }

    [Fact]
    public void ConfirmationFiresOnceNotOnEverySubsequentBlankTick()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);

        BlankToTheBrink(d);
        Assert.True(Blank(d)); // ConfirmTicks reached

        Assert.False(Blank(d)); // already confirmed — no repeat signal
        Assert.False(Blank(d));
    }

    [Fact]
    public void MarkCleared_SuppressesAFurtherConfirmation_FromAPartialAwayStreak()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);

        BlankToTheBrink(d);
        d.MarkCleared(); // an out-of-band clear happens (e.g. a lost session)

        // Without MarkCleared resetting _visible too, one more blank would confirm the leftover streak
        // and fire a second, redundant clear.
        for (var i = 0; i < SignatureAbsenceDebouncer.ConfirmTicks; i++)
            Assert.False(Blank(d));
    }

    [Fact]
    public void MarkCleared_ThenMatch_IsTrackedAsAFreshSighting()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);
        Blank(d);
        d.MarkCleared();

        Matched(d); // re-sighting after the clear

        BlankToTheBrink(d);
        Assert.True(Blank(d)); // confirms against the fresh streak
    }

    [Fact]
    public void AChangedMatch_MidAwayStreak_ResetsTheStreakLikeAnyOtherMatch()
    {
        // Observe(Matched) only signals "a settled reading resolved this tick" — it has no notion of
        // which value, so a different value interrupting a partial away-streak must reset it exactly
        // like a repeat of the original value would.
        var d = new SignatureAbsenceDebouncer();
        Matched(d); // e.g. "3600"

        Blank(d);
        Matched(d); // e.g. "7200" — a different value, still a match

        BlankToTheBrink(d);
        Assert.True(Blank(d));
    }

    [Fact]
    public void AfterConfirmedAbsence_NewMatchIsTrackedFresh()
    {
        var d = new SignatureAbsenceDebouncer();
        Matched(d);
        BlankToTheBrink(d);
        Assert.True(Blank(d)); // confirmed absent

        Matched(d); // badge shown again

        BlankToTheBrink(d);
        Assert.True(Blank(d)); // confirms again
    }
}
