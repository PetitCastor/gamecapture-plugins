using System.Text.Json;
using GameCapture.Sdk;
using SignaturePluginRois = global::SignaturePlugin.Rois;

namespace SignaturePlugin;

public sealed class SignaturePlugin : IGameCapturePlugin
{
    private const double MatchTolerance = 0.02;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SignatureTable _table;
    private string? _lastObservation;
    private bool _hasObservation;

    public SignaturePlugin(SignatureTable? table = null) => _table = table ?? SignatureTable.LoadEmbedded();
    public string Name => "SignaturePlugin";
    public IReadOnlyList<RoiSubscription> Rois => SignaturePluginRois.All;
    public RoiErrorPolicy ErrorPolicy => RoiErrorPolicy.AbortTick;

    public Task OnTickAsync(TickContext ctx, CancellationToken ct)
    {
        if (ctx.Tick.TryGetText(SignaturePluginRois.Counter.Id, out var text))
            EmitObservation(ctx, text, TriggerKind.Auto, false);
        return Task.CompletedTask;
    }

    public async Task OnManualTickAsync(TickContext ctx, CancellationToken ct)
    {
        if (!ctx.Tick.TryGetText(SignaturePluginRois.Counter.Id, out var text)) return;
        EmitObservation(ctx, text, TriggerKind.Manual, true);
        try
        {
            var png = await ctx.Services.DumpFrameAsync(SignaturePluginRois.Counter.Rect, "counter", ct);
            if (png is not null) ctx.Services.LogVerbose($"signature read '{text.Trim()}' — frame dumped to {png}");
        }
        catch (OperationCanceledException) { throw; }
        catch { }
    }

    public void OnSessionEvent(SessionEvent evt)
    {
        if (evt is SessionEvent.TicksDropped) { _lastObservation = null; _hasObservation = false; }
    }

    public IEnumerable<string> SummaryLines() => [$"  last signature: {_lastObservation ?? "none"}"];

    private void EmitObservation(TickContext ctx, string text, TriggerKind trigger, bool force)
    {
        var value = text.Trim();
        if (!SignatureParser.TryParse(value, out var signature) || !_table.TryMatch(signature, MatchTolerance, out var match))
        {
            if (_hasObservation)
            {
                ctx.Services.EmitCleared(ctx.Tick.Timestamp, Name);
                _hasObservation = false; _lastObservation = null;
            }
            return;
        }

        var observation = JsonSerializer.Serialize(new SignatureEvent(match.Name, match.Kind, signature, match.Count, match.Delta), JsonOptions);
        if (!force && observation == _lastObservation) return;
        ctx.Services.Emit(new CaptureRecord(ctx.Tick.Timestamp, Name, trigger, observation));
        _lastObservation = observation; _hasObservation = true;
    }

    private sealed record SignatureEvent(string? Name, string? Kind, double? Signature, int Count, double? Delta);
}
