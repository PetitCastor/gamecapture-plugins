using GameCapture.Sdk;
using GameCapture.Sdk.Testing;
using Xunit;

namespace SignaturePlugin.Tests;

// Named independently of the project: sourceName substitution would
// otherwise splice a dotted project name (`-n Acme.MyPlugin`) straight into this declaration.
public class SignaturePluginTests
{
    private static TickContext Tick(TickData tick, FakePluginServices services)
        => TickContext.ForTesting(tick, services);

    private static async Task Read(SignaturePlugin plugin, FakePluginServices services, string text, int times = 1)
    {
        for (var i = 0; i < times; i++)
            await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", text).Build(), services), default);
    }

    /// <summary>Blank ticks one short of confirming an absence, so the next one clears.</summary>
    private static Task BlankToTheBrink(SignaturePlugin plugin, FakePluginServices services)
        => Read(plugin, services, "nothing", SignatureAbsenceDebouncer.ConfirmTicks - 1);

    /// <summary>A settled reading: enough repeats to clear <see cref="SignatureConsensus"/>.</summary>
    private static Task Settled(SignaturePlugin plugin, FakePluginServices services, string text)
        => Read(plugin, services, text, SignatureConsensus.ChangeConfirmTicks);

    [Fact]
    public async Task Emits_structured_observation_once_per_change()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600", 2);
        await Settled(plugin, services, "7200");

        Assert.Equal(2, services.Emitted.Count);
        Assert.Contains("\"name\":\"Bexalite\"", services.Emitted[0].RawText);
        Assert.Contains("\"count\":2", services.Emitted[1].RawText);
    }

    // The overlay template reads Fields, not RawText: OverlayRecordSink falls back to the whole
    // JSON blob the moment a {placeholder} misses, so these key names are a display contract.
    [Fact]
    public async Task Observation_carries_fields_for_overlay_templates()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "7200");

        var fields = Assert.Single(services.Emitted).Fields;
        Assert.NotNull(fields);
        Assert.Equal("Bexalite", fields["name"]);
        Assert.Equal("ore", fields["kind"]);
        Assert.Equal("2", fields["count"]);
        Assert.Equal("7200", fields["signature"]);
    }

    // The first reading must still show instantly — the complaint was that the overlay vanished too
    // eagerly, not that it was slow to appear.
    [Fact]
    public async Task First_reading_emits_on_the_very_first_tick()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600");

        Assert.Single(services.Emitted);
    }

    [Fact]
    public async Task Blank_after_observation_emits_one_clear_only_once_confirmed()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();
        await Read(plugin, services, "3600");

        await BlankToTheBrink(plugin, services);
        Assert.Empty(services.Cleared);

        await Read(plugin, services, "nothing");
        var clear = Assert.Single(services.Cleared);
        Assert.Equal("SignaturePlugin", clear.Plugin);
        Assert.Equal(RecordKind.Cleared, clear.Kind);

        // Once confirmed, further blank reads must not fire a second clear.
        await Read(plugin, services, "nothing");
        Assert.Single(services.Cleared);

        Assert.Single(services.Emitted);
    }

    // The reported bug. A legible number that resolves to no ore cluster is proof the badge is still
    // drawn, and used to be scored as the badge having vanished — so a run of misread digits hid an
    // overlay the player was still looking at, "before the number leaves the screen".
    [Fact]
    public async Task A_legible_but_unmatchable_number_does_not_hide_the_overlay()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();
        await Read(plugin, services, "3600");

        await Settled(plugin, services, "12345");
        await Read(plugin, services, "12345", SignatureAbsenceDebouncer.ConfirmTicks);

        Assert.Empty(services.Cleared);
        Assert.Single(services.Emitted);
    }

    // ...but it cannot hold it forever, or a crop that never matches again would pin a dead value on
    // screen, since lingerMs is 0 and nothing else ever hides it.
    [Fact]
    public async Task An_unmatchable_number_still_goes_stale_eventually()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();
        await Read(plugin, services, "3600");

        // The consensus spends its first ticks holding 3600, so the stale window only starts counting
        // once 12345 is the accepted value.
        await Read(plugin, services, "12345",
            SignatureConsensus.ChangeConfirmTicks + SignatureAbsenceDebouncer.StaleTicks);

        Assert.Single(services.Cleared);
    }

    [Fact]
    public async Task Dropped_ticks_then_confirmed_blank_reads_emits_a_clear()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600");
        plugin.OnSessionEvent(new SessionEvent.TicksDropped(1));

        // The gap itself is not evidence of absence — still need ConfirmTicks blank reads after it.
        await BlankToTheBrink(plugin, services);
        Assert.Empty(services.Cleared);

        await Read(plugin, services, "nothing");

        Assert.Single(services.Emitted);
        Assert.Single(services.Cleared);
    }

    [Fact]
    public async Task Single_tick_OCR_misses_do_not_blink_the_overlay()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600");
        for (var i = 0; i < SignatureAbsenceDebouncer.ConfirmTicks; i++)
        {
            await Read(plugin, services, "nothing");
            await Read(plugin, services, "3600");
        }

        Assert.Empty(services.Cleared);
        Assert.Single(services.Emitted);
    }

    // The Ice/Bexalite flip, end to end: 17200 is Ice x4 exactly and 18200 is one 7→8 slip away from
    // it. The slip must never reach the overlay, however many times it recurs, as long as it does not
    // repeat on consecutive ticks.
    [Fact]
    public async Task A_single_tick_misread_never_changes_the_named_ore()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "17200");
        for (var i = 0; i < 5; i++)
        {
            await Read(plugin, services, "18200");
            await Read(plugin, services, "17200");
        }

        var emitted = Assert.Single(services.Emitted);
        Assert.Contains("\"name\":\"Ice\"", emitted.RawText);
        Assert.Empty(services.Cleared);
    }

    [Fact]
    public async Task Changed_signature_emits_once_the_change_repeats()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600");
        await Read(plugin, services, "7200", SignatureConsensus.ChangeConfirmTicks - 1);
        Assert.Single(services.Emitted); // still holding the incumbent

        await Read(plugin, services, "7200");

        Assert.Equal(2, services.Emitted.Count);
        Assert.Empty(services.Cleared);
    }

    // A brief drop says nothing about what is on screen. Clearing on the first attempt is why the
    // overlay could appear and vanish in the same breath.
    [Fact]
    public async Task Reconnecting_briefly_does_not_clear()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600");
        plugin.OnSessionEvent(new SessionEvent.Reconnecting(1));

        Assert.Empty(services.Cleared);
    }

    [Fact]
    public async Task Reconnecting_for_a_sustained_outage_emits_a_clear()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600");
        for (var attempt = 1; attempt <= 4; attempt++)
            plugin.OnSessionEvent(new SessionEvent.Reconnecting(attempt));

        var clear = Assert.Single(services.Cleared);
        Assert.Equal("SignaturePlugin", clear.Plugin);
        Assert.Equal(RecordKind.Cleared, clear.Kind);
    }

    [Fact]
    public async Task Reconnecting_with_no_observation_emits_nothing()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "nothing");
        for (var attempt = 1; attempt <= 8; attempt++)
            plugin.OnSessionEvent(new SessionEvent.Reconnecting(attempt));

        Assert.Empty(services.Cleared);
    }

    // Regression: Reconnecting's clear used to leave a partial away-streak in the debouncer, so
    // further misses after reconnecting would confirm an absence and clear a second time even though
    // the overlay was already hidden by the Reconnecting clear.
    [Fact]
    public async Task Reconnecting_mid_away_streak_does_not_double_clear()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600");
        await BlankToTheBrink(plugin, services);
        for (var attempt = 1; attempt <= 4; attempt++)
            plugin.OnSessionEvent(new SessionEvent.Reconnecting(attempt));
        Assert.Single(services.Cleared);

        await Read(plugin, services, "nothing", SignatureAbsenceDebouncer.ConfirmTicks * 2);

        Assert.Single(services.Cleared);
    }

    // After the overlay is hidden there is no incumbent left to defend, so the next scan must show
    // immediately rather than spend a tick being held against a value nothing is displaying.
    [Fact]
    public async Task A_new_scan_after_a_clear_emits_on_its_first_tick()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600");
        await Read(plugin, services, "nothing", SignatureAbsenceDebouncer.ConfirmTicks);
        Assert.Single(services.Cleared);

        await Read(plugin, services, "7200");

        Assert.Equal(2, services.Emitted.Count);
        Assert.Contains("\"count\":2", services.Emitted[1].RawText);
    }

    // Blank ticks are neutral for the consensus. Breaking a challenger's run on one was tried and
    // reverted: captured live runs read blank on roughly four ticks in ten with the badge plainly on
    // screen, so treating that as a signal only made every genuine rock change slower to adopt.
    [Fact]
    public async Task A_blank_tick_between_challenger_reads_does_not_restart_the_count()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "3600");
        await Read(plugin, services, "7200");
        await Read(plugin, services, "nothing");
        await Read(plugin, services, "7200");

        Assert.Equal(2, services.Emitted.Count);
        Assert.Contains("\"count\":2", services.Emitted[1].RawText);
        Assert.Empty(services.Cleared);
    }

    // The sixteen-second deadlock from a captured live run, in miniature. OCR truncated "21/425" to
    // 21 on roughly every other tick; 21 got accepted, matched nothing, and then kept re-asserting
    // itself against the true reading, resetting its confirmation streak every time. Two independent
    // guards now break this: the parser rejects the truncation outright, and an accepted value that
    // matches nothing is not defended.
    [Fact]
    public async Task A_recurring_misread_cannot_deadlock_the_accepted_value()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "19500");     // Torite x5 on screen
        await Read(plugin, services, "12345", 2);  // a parseable misread wins the incumbency...

        // ...but it resolves to nothing, so it is not defended: the very next reading that does
        // resolve is shown at once, rather than needing to out-confirm the garbage.
        await Read(plugin, services, "21425");

        Assert.Equal(2, services.Emitted.Count);
        Assert.Contains("\"name\":\"Aluminum\"", services.Emitted[1].RawText);
        Assert.Empty(services.Cleared);
    }

    // The other half of that deadlock: a truncating misread must not reach the consensus as a number
    // at all. "21/425" is the captured live text for 21,425.
    [Fact]
    public async Task A_slash_for_comma_misread_reads_as_the_real_number()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "21/425");

        var fields = Assert.Single(services.Emitted).Fields;
        Assert.NotNull(fields);
        Assert.Equal("Aluminum x5", fields["cluster"]);
    }

    // 19200 is Savrilium x6 and Aslarite x5 alike. It used to resolve to nothing, which counted as the
    // badge having vanished — so scanning one of those rocks hid the overlay outright.
    [Fact]
    public async Task An_ambiguous_total_shows_both_candidates_instead_of_hiding()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "19200", SignatureAbsenceDebouncer.ConfirmTicks + 1);

        var fields = Assert.Single(services.Emitted).Fields;
        Assert.NotNull(fields);
        Assert.Equal("Savrilium x6 / Aslarite x5", fields["cluster"]);
        Assert.Equal("Aslarite x5", fields["alternate"]);
        Assert.Empty(services.Cleared);
    }

    // The shipped overlay template is "{cluster}", and OverlayRecordSink dumps the entire raw JSON
    // record on screen the moment a placeholder misses — so this key is a display contract.
    [Fact]
    public async Task Observation_carries_a_cluster_field_for_the_shipped_template()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await Read(plugin, services, "7200");

        var fields = Assert.Single(services.Emitted).Fields;
        Assert.NotNull(fields);
        Assert.Equal("Bexalite x2", fields["cluster"]);
        Assert.Equal("", fields["alternate"]);
    }

    [Fact]
    public async Task Failed_roi_emits_nothing()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();
        var tick = new TickDataBuilder().Errored("counter", "region outside frame").Build();

        await plugin.OnTickAsync(Tick(tick, services), default);

        Assert.Empty(services.Emitted);
        Assert.Equal(RoiStatus.Failed, tick.Status("counter"));
    }
}
