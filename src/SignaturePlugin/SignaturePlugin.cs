using System.Globalization;
using System.Text.Json;
using GameCapture.Sdk;
using SignaturePluginRois = global::SignaturePlugin.Rois;

namespace SignaturePlugin;

public sealed class SignaturePlugin : IGameCapturePlugin
{
    /// <summary>
    /// How far an observed signature may sit from a derived cluster total and still be that cluster,
    /// as an ABSOLUTE figure — not a fraction of the total, which is what it used to be.
    /// </summary>
    /// <remarks>
    /// A relative tolerance is exactly backwards for this table. The derived grid gets *denser* with
    /// cluster count (entries 15 apart become totals 90 apart at x6) while 2% of the total grows to
    /// ±500, so the readings least able to identify an ore were the ones judged most leniently: 18200
    /// was accepted as Bexalite x5 (200 off) when it was really a misread of Ice x4 at 17200.
    /// <para>
    /// 40 is slack for table drift against a game patch, not for OCR error: it is wide enough that
    /// every one of the 156 exact grid points in the shipped table still resolves to itself, and tight
    /// enough that the reported 18200 slip (200 off Bexalite x5) no longer resolves to anything.
    /// </para>
    /// <para>
    /// It is not, and cannot be, a defence against misreading in general. The derived grid is dense —
    /// Corundum x3 is 12675 and Quantanium x4 is 12680, five apart — so a slipped digit can land
    /// exactly on a neighbouring cluster with a delta of zero, and no tolerance distinguishes that from
    /// a correct reading. What this constant buys is that a slip has to be *lucky* to be believed
    /// instead of merely close; <see cref="SignatureConsensus"/> covers the separate case of a slip
    /// that does not repeat.
    /// </para>
    /// </remarks>
    private const double MatchTolerance = 40;

    /// <summary>
    /// Reconnect attempts to ride out before a lost session is allowed to hide the overlay.
    /// </summary>
    /// <remarks>
    /// Attempts are paced by <c>PluginHostOptions.ReconnectDelay</c> (500 ms, no backoff), so this is
    /// roughly two seconds of genuine outage. A stream that drops and comes back on the first dial says
    /// nothing whatsoever about what is on screen, and clearing on it was one of the ways the overlay
    /// appeared and vanished in the same breath.
    /// </remarks>
    private const int ReconnectClearAttempt = 4;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SignatureTable _table;
    private readonly SignatureAbsenceDebouncer _absence = new();
    private readonly SignatureConsensus _consensus = new();
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
        //
        // The consensus deliberately survives the gap: its accepted value is still the best estimate
        // of what is on screen, and re-arming it would let the first post-gap read — as likely to be a
        // misread as any other — swap the named ore with no confirmation at all.
        if (evt is SessionEvent.TicksDropped)
        {
            _lastObservation = null;
            _absence.ResetStreaks();
        }

        // The session dropped, so the on-screen value is no longer being confirmed by anything —
        // lingerMs is 0, so nothing else will hide a stale reading. Held until the outage looks real
        // (see ReconnectClearAttempt); _lastObservation is only ever non-null once a tick has set
        // _services, so the null-conditional below is belt-and-suspenders.
        if (evt is SessionEvent.Reconnecting reconnecting
            && reconnecting.Attempt >= ReconnectClearAttempt
            && _lastObservation is not null)
        {
            _services?.EmitCleared(DateTime.UtcNow, Name);
            _lastObservation = null;

            // This clear bypasses the confirm-tick gate, so the debouncer must be told directly —
            // otherwise it still believes something is visible, and a partial away-streak from before
            // the disconnect would survive to fire a second, redundant clear a few ticks later.
            _absence.MarkCleared();
            _consensus.Reset();
        }
    }

    public IEnumerable<string> SummaryLines() => [$"  last signature: {_lastObservation ?? "none"}"];

    private void EmitObservation(TickContext ctx, string text, TriggerKind trigger, bool force)
    {
        _services = ctx.Services;
        var raw = text.Trim();

        if (!SignatureParser.TryParse(raw, out var read))
        {
            // A blank is deliberately neutral for the consensus: it says nothing about the value, only
            // about the crop. Breaking a challenger's run on one was tried and reverted — captured live
            // runs read blank on roughly four ticks in ten even with the badge plainly on screen, so
            // treating that as a signal just made every genuine rock change slower to adopt.
            ctx.Services.LogVerbose($"signature tick: raw='{raw}' — blank (no number parsed)");
            ObserveAbsence(ctx, SignatureReading.Blank);
            return;
        }

        // Consensus first, matching second: the number is the atomic OCR fact and the ore name is a
        // derived interpretation of it, so a slipped digit has to be caught before the table gets a
        // chance to turn it into a confident, plausible, wrong answer.
        var signature = _consensus.Observe(read);

        if (!_table.TryMatch(signature, MatchTolerance, out var match))
        {
            // Digits in the crop are proof the badge is still drawn — only a blank crop is evidence it
            // left. Conflating the two is what used to hide the overlay mid-scan: any reading that
            // resolved to no cluster counted toward the disappearance.
            LogReading(ctx, raw, read, signature, null);

            // Nothing is displaying this value, so there is nothing here worth defending. The consensus
            // exists to stop a slip from replacing a GOOD reading; an accepted value that resolves to
            // no cluster is not one, and holding it actively blocks recovery — a captured live run had
            // a truncated 21 accepted, and because the same misread recurred every other tick it kept
            // re-asserting itself and resetting the true reading's confirmation streak, pinning the
            // overlay on a stale ore for sixteen seconds. Standing down here means the next reading
            // that actually resolves is shown at once.
            _consensus.Reset();
            ObserveAbsence(ctx, SignatureReading.Unmatched);
            return;
        }

        LogReading(ctx, raw, read, signature, match);
        _absence.Observe(SignatureReading.Matched); // a match never confirms an absence

        var observation = JsonSerializer.Serialize(
            new SignatureEvent(match.Name, match.Kind, signature, match.Count, match.Delta, match.AlternateName, match.AlternateCount),
            JsonOptions);
        if (!force && observation == _lastObservation) return;

        // Fields carry the same match as named strings so an overlay template can interpolate
        // {cluster} instead of falling back to RawText, which is the whole JSON blob. Formatted
        // invariant: these are display and column values, not a locale-sensitive presentation.
        //
        // name/count remain the primary candidate alone, so existing consumers keep working — but a
        // template built from those two cannot express a tie, and rendering only the winner of one
        // would be a confident wrong answer. {cluster} is the placeholder that says what is actually
        // known, which is why the shipped template uses it.
        ctx.Services.Emit(new CaptureRecord(ctx.Tick.Timestamp, Name, trigger, observation)
        {
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = match.Name,
                ["kind"] = match.Kind,
                ["signature"] = signature.ToString(CultureInfo.InvariantCulture),
                ["count"] = match.Count.ToString(CultureInfo.InvariantCulture),
                ["delta"] = match.Delta.ToString(CultureInfo.InvariantCulture),
                ["cluster"] = match.Cluster,
                ["alternate"] = match.Alternate,
            },
        });
        _lastObservation = observation;
    }

    private void ObserveAbsence(TickContext ctx, SignatureReading reading)
    {
        if (!_absence.Observe(reading)) return;

        ctx.Services.EmitCleared(ctx.Tick.Timestamp, Name);
        _lastObservation = null;
        _consensus.Reset();
    }

    /// <summary>
    /// One line per tick under <c>--verbose</c>, tracing the whole chain the overlay depends on: what
    /// OCR returned, what parsed out of it, what the consensus is actually acting on, and what the
    /// table made of that.
    /// </summary>
    /// <remarks>
    /// Deliberately every tick rather than on change. The failures worth diagnosing here — a digit that
    /// flickers between two values, a crop that intermittently reads blank — are invisible in a log
    /// that only records the ticks where something happened, because their whole signature is *how
    /// often* an unchanged reading was reported as something else.
    /// </remarks>
    private static void LogReading(TickContext ctx, string raw, double read, double signature, SignatureMatch? match)
    {
        // "held" appears exactly on the ticks the consensus is refusing an unconfirmed change, which
        // is the single most useful thing in this log: a run of them is a digit flickering.
        var held = signature != read ? $" held={signature.ToString(CultureInfo.InvariantCulture)}" : "";
        var verdict = match is { } m
            ? $"{m.Cluster} (delta {m.Delta.ToString(CultureInfo.InvariantCulture)})"
            : $"no cluster within {MatchTolerance.ToString(CultureInfo.InvariantCulture)}";

        ctx.Services.LogVerbose(
            $"signature tick: raw='{raw}' read={read.ToString(CultureInfo.InvariantCulture)}{held} — {verdict}");
    }

    /// <param name="Alternate">The equally good runner-up ore when the total is ambiguous, else null.
    /// Nullable rather than an empty string so a consumer can test for it without knowing the display
    /// convention.</param>
    private sealed record SignatureEvent(
        string? Name, string? Kind, double? Signature, int Count, double? Delta,
        string? Alternate, int AlternateCount);
}
