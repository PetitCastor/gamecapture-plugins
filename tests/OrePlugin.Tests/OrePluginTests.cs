using GameCapture.Sdk;
using GameCapture.Sdk.Testing;
using Xunit;

namespace OrePlugin.Tests;

// Named independently of the project: sourceName substitution would
// otherwise splice a dotted project name (`-n Acme.MyPlugin`) straight into this declaration.
public class OrePluginTests
{
    private static TickContext Tick(TickData tick, FakePluginServices services)
        => TickContext.ForTesting(tick, services);

    [Fact]
    public async Task Emits_once_per_change()
    {
        var plugin = new OrePlugin();
        var services = new FakePluginServices();

        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "3/8").Build(), services), default);
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "3/8").Build(), services), default);
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "4/8").Build(), services), default);

        Assert.Equal(["3/8", "4/8"], services.Emitted.Select(r => r.RawText));
    }

    [Fact]
    public async Task Failed_roi_emits_nothing()
    {
        var plugin = new OrePlugin();
        var services = new FakePluginServices();
        var tick = new TickDataBuilder().Errored("counter", "region outside frame").Build();

        await plugin.OnTickAsync(Tick(tick, services), default);

        Assert.Empty(services.Emitted);
        Assert.Equal(RoiStatus.Failed, tick.Status("counter"));
    }
}
