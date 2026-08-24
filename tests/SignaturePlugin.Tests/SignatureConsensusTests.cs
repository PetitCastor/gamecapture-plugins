using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// Offline tests for <see cref="SignatureConsensus"/> — the seam that keeps one misread digit from
/// swapping the named ore under the player.
/// </summary>
public class SignatureConsensusTests
{
    [Fact]
    public void TheFirstReadingIsAcceptedImmediately()
    {
        var c = new SignatureConsensus();

        // The overlay has to appear the moment a scan completes; only a *change* has to earn it.
        Assert.Equal(17200, c.Observe(17200));
    }

    [Fact]
    public void ARepeatedReadingStaysAccepted()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        Assert.Equal(17200, c.Observe(17200));
        Assert.Equal(17200, c.Observe(17200));
    }

    // The reported bug: 17200 is Ice x4 exactly, and 18200 — one 7→8 slip — sits 200 off Bexalite x5,
    // close enough that the table used to accept it outright. A single tick of it must not win.
    [Fact]
    public void ASingleTickSlipIsHeldOff()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        Assert.Equal(17200, c.Observe(18200));
        Assert.Equal(17200, c.Observe(17200));
    }

    [Fact]
    public void AnAlternatingSlipNeverWins()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(17200, c.Observe(18200));
            Assert.Equal(17200, c.Observe(17200));
        }
    }

    [Fact]
    public void AChangeIsAdoptedOnceItRepeats()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        Assert.Equal(17200, c.Observe(14400)); // challenger, 1 of ChangeConfirmTicks
        Assert.Equal(14400, c.Observe(14400)); // confirmed — a genuinely different rock
        Assert.Equal(14400, c.Observe(14400));
    }

    [Fact]
    public void ChangeConfirmTicks_IsTheNumberOfConsecutiveReadingsRequired()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        for (var i = 0; i < SignatureConsensus.ChangeConfirmTicks - 1; i++)
            Assert.Equal(17200, c.Observe(14400));

        Assert.Equal(14400, c.Observe(14400));
    }

    [Fact]
    public void TwoDifferentChallengersDoNotAddUp()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        Assert.Equal(17200, c.Observe(18200));
        Assert.Equal(17200, c.Observe(14400)); // a different challenger restarts the count
        Assert.Equal(17200, c.Observe(18200));
    }

    [Fact]
    public void TheIncumbentReassertingItselfDropsAPendingChallenger()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        Assert.Equal(17200, c.Observe(14400)); // challenger, 1 of 2
        Assert.Equal(17200, c.Observe(17200)); // incumbent back — challenger is noise
        Assert.Equal(17200, c.Observe(14400)); // so this is 1 of 2 again, not the confirming tick
    }

    // Near-misses are changes, not rounding error to absorb: 17240 and 17200 both resolve to Ice x4,
    // and holding the value that is actually displayed keeps the emitted signature field stable.
    [Fact]
    public void ANearMissIsTreatedAsAChangeLikeAnyOther()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        Assert.Equal(17200, c.Observe(17240));
        Assert.Equal(17240, c.Observe(17240));
    }

    // "Consecutive" has to mean consecutive TICKS, not consecutive ticks that happened to parse.
    // Otherwise a challenger accumulates its confirmations either side of a blank frame, which is
    // exactly the shape a flickering digit has.
    [Fact]
    public void ABlankTickBreaksAChallengersRun()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        Assert.Equal(17200, c.Observe(18200)); // challenger, 1 of 2
        c.NoReading();                         // crop read nothing — the run is broken
        Assert.Equal(17200, c.Observe(18200)); // so this is 1 of 2 again, not the confirming tick
    }

    [Fact]
    public void ABlankTickDoesNotUnseatTheIncumbent()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);

        c.NoReading();
        c.NoReading();

        // A blank says nothing about the value; deciding what it means about the badge being on
        // screen is the absence debouncer's job, not this one's.
        Assert.Equal(17200, c.Observe(17200));
    }

    [Fact]
    public void Reset_MakesTheNextReadingAFreshSighting()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);
        c.Reset();

        Assert.Equal(14400, c.Observe(14400));
    }

    [Fact]
    public void Reset_AlsoDropsAPendingChallenger()
    {
        var c = new SignatureConsensus();
        c.Observe(17200);
        c.Observe(14400); // challenger, 1 of 2
        c.Reset();

        Assert.Equal(18200, c.Observe(18200)); // fresh sighting wins outright
        Assert.Equal(18200, c.Observe(14400)); // and the old challenger has no standing
    }
}
