using System.Globalization;
using System.Text.Json;
using GameCapture.Sdk;
using SignaturePluginRois = global::SignaturePlugin.Rois;

namespace SignaturePlugin;

public sealed class SignaturePlugin : IGameCapturePlugin
{
    private const double MatchTolerance = 0.02;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SignatureTable _table;
    private readonly SignatureAbsenceDebouncer _absence = new();
    private IPluginServices? _services;
    private string? _lastObservation;

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
        // Force the first post-gap observation through, but keep the active-state flag until an
        // invalid reading can clear it. Otherwise an object that disappeared during the dropped
        // frames would remain stale in configured sinks forever. The missed frames themselves are not
        // evidence the badge vanished, so they must not count toward the absence debounce either.
        if (evt is SessionEvent.TicksDropped)
        {
            _lastObservation = null;
            _absence.ResetStreak();
        }

        // The session dropped, so the on-screen value is no longer being confirmed by anything —
        // lingerMs is 0, so nothing else will hide a stale reading. _lastObservation is only ever
        // non-null once a tick has set _services, so the null-conditional below is belt-and-suspenders.
        if (evt is SessionEvent.Reconnecting && _lastObservation is not null)
        {
            _services?.EmitCleared(DateTime.UtcNow, Name);
            _lastObservation = null;

            // This clear bypasses the confirm-tick gate, so the debouncer must be told directly —
            // otherwise it still believes something is visible, and a partial away-streak from before
            // the disconnect would survive to fire a second, redundant clear a few ticks later.
            _absence.MarkCleared();
        }
    }

    public IEnumerable<string> SummaryLines() => [$"  last signature: {_lastObservation ?? "none"}"];

    private void EmitObservation(TickContext ctx, string text, TriggerKind trigger, bool force)
    {
        _services = ctx.Services;
        var value = text.Trim();
        if (!SignatureParser.TryParse(value, out var signature) || !_table.TryMatch(signature, MatchTolerance, out var match))
        {
            // A single momentary OCR miss on this fragile crop is expected and must not be mistaken
            // for the badge actually vanishing — only a confirmed absence clears the overlay. Within
            // the grace window, publish nothing at all: the overlay keeps its current content.
            if (_absence.ObserveMissing())
            {
                ctx.Services.EmitCleared(ctx.Tick.Timestamp, Name);
                _lastObservation = null;
            }
            return;
        }

        // Counts as proof of presence even when the reading is unchanged and nothing is emitted below
        // — a stable value must keep the overlay's disappearance debounce from ever tripping.
        _absence.ObserveMatch();

        var observation = JsonSerializer.Serialize(new SignatureEvent(match.Name, match.Kind, signature, match.Count, match.Delta), JsonOptions);
        if (!force && observation == _lastObservation) return;

        // Fields carry the same match as named strings so an overlay template can interpolate
        // {name}/{count} instead of falling back to RawText, which is the whole JSON blob. Formatted
        // invariant: these are display and column values, not a locale-sensitive presentation.
        ctx.Services.Emit(new CaptureRecord(ctx.Tick.Timestamp, Name, trigger, observation)
        {
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = match.Name,
                ["kind"] = match.Kind,
                ["signature"] = signature.ToString(CultureInfo.InvariantCulture),
                ["count"] = match.Count.ToString(CultureInfo.InvariantCulture),
                ["delta"] = match.Delta.ToString(CultureInfo.InvariantCulture),
            },
        });
        _lastObservation = observation;
    }

    private sealed record SignatureEvent(string? Name, string? Kind, double? Signature, int Count, double? Delta);
}
