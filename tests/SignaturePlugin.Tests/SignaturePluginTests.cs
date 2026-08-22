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

    [Fact]
    public async Task Emits_structured_observation_once_per_change()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "3600").Build(), services), default);
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "3600").Build(), services), default);
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "7200").Build(), services), default);

        Assert.Equal(2, services.Emitted.Count);
        Assert.Contains("\"name\":\"Bexalite\"", services.Emitted[0].RawText);
        Assert.Contains("\"count\":2", services.Emitted[1].RawText);
    }

    [Fact]
    public async Task Invalid_after_observation_emits_one_clear()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "3600").Build(), services), default);
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "nothing").Build(), services), default);
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "nothing").Build(), services), default);

        Assert.Single(services.Emitted);
        var clear = Assert.Single(services.Cleared);
        Assert.Equal("SignaturePlugin", clear.Plugin);
        Assert.Equal(RecordKind.Cleared, clear.Kind);
    }

    [Fact]
    public async Task Dropped_ticks_then_invalid_read_emits_a_clear()
    {
        var plugin = new SignaturePlugin();
        var services = new FakePluginServices();

        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "3600").Build(), services), default);
        plugin.OnSessionEvent(new SessionEvent.TicksDropped(1));
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "nothing").Build(), services), default);

        Assert.Single(services.Emitted);
        Assert.Single(services.Cleared);
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
