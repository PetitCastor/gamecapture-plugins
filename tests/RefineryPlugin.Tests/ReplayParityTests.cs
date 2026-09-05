using System.Text;
using RefineryPlugin.Orders;
using Ocrx.Sdk;
using Ocrx.Sdk.Testing;
using Xunit;
using Xunit.Abstractions;

namespace RefineryPlugin.Tests;

/// <summary>
/// A spawned engine is a process-wide resource (a named pipe, a Windows OCR engine instance), so
/// running two of these at once would have them competing for both while claiming to measure a
/// deterministic replay.
/// </summary>
[CollectionDefinition("ReplayParity", DisableParallelization = true)]
public class ReplayParityCollection;

/// <summary>
/// The acceptance gate for the engine/plugin split: the engine replaying the monolith's own PNG
/// corpora through RefineryPlugin must land the same ledger the monolith's integration tests
/// asserted on (RefineryTrackerReplayTests). Every assertion below is a restatement of one of
/// theirs — same corpus, same expectation — so a divergence anywhere in the split (ROI geometry,
/// the wire mapping, the scan loop's tick construction, the ported logic) fails here.
/// </summary>
/// <remarks>
/// Runs through <see cref="ReplayHarness"/> — a real, separately spawned <c>Ocrx.Engine.exe</c> —
/// rather than hosting the engine in-proc: this suite has to survive the repo split (TASK-13), where
/// this project can no longer take a <c>ProjectReference</c> on the engine, only on the SDK and its
/// testing companion. Real Windows OCR under the hood, so this is tagged Integration.
/// </remarks>
[Collection("ReplayParity")]
[Trait("Category", "Integration")]
public class ReplayParityTests(ITestOutputHelper output)
{
    /// <summary>The monolith's corpora, linked into this assembly's output by the csproj.</summary>
    private const string FixturesRoot = "Fixtures/Replay";

    [Fact]
    public async Task RefineryConfirm_corpus_produces_baseline_ledger()
    {
        var ledger = await RunCorpusAsync("refinery-confirm");

        // Baseline: RefineryTrackerReplayTests.FullConfirmSequence_ProducesOneCollectedOrder.
        Verify(ledger, () =>
        {
            var order = Assert.Single(ledger.All);
            Assert.Equal(OrderState.Collected, order.State);
            Assert.Equal(Completeness.Complete, order.Completeness);

            // TASK-RFN-01: assert the four real rows are present and correct, not just the
            // aggregate verdict — this corpus regressed silently through exactly that gap (one
            // material row got dropped by a parser bug, and the remaining checksum still fell
            // within ChecksumTolerance's slack, so a Completeness-only assertion kept passing
            // while a row went missing). Expected values confirmed against a diagnostic
            // OcrPipeline.ReadRegionDetailedAsync probe of the real engine output for this corpus
            // (tasks/TASK-RFN-01-*.md).
            //
            // Deliberately NOT asserting order.Materials.Count: this corpus's SETUP panel also has
            // two unrelated, pre-existing OCR misreads of its CORUNDUM row (garbled two different
            // ways across its two sightings, e.g. ")ORUNDIJM (RAW)" vs. "-'ORUNDUM (RAW)" — no
            // shared name token with each other or with "CORUNDUM", so OrderMatcher.SameMaterial
            // can't merge them and they survive as extra, separately-tracked rows). That's a real
            // OCR-quality limitation with no general, reliable pattern to filter on — a known
            // limitation tracked in tasks/TASK-RFN-01-*.md, not something to paper over here.
            AssertYield(FindMaterial(order, "TORITE", 262), 50);
            AssertYield(FindMaterial(order, "TORITE", 785), 70);
            AssertYield(FindMaterial(order, "CORUNDUM", 665), 110);
            AssertYield(FindMaterial(order, "ALUMINUM", 318), 71);
        });
    }

    // FindMaterial's predicate already pins Name and Quality, so only YieldCscu is left to check.
    private static OrderMaterial FindMaterial(WorkOrder order, string name, int quality)
        => Assert.Single(order.Materials, m => m.Name == name && m.Quality == quality);

    private static void AssertYield(OrderMaterial material, int yieldCscu)
    {
        Assert.Equal(yieldCscu, material.YieldCscu);
    }

    [Fact]
    public async Task RefineryIceRename_corpus_produces_baseline_ledger()
    {
        var ledger = await RunCorpusAsync("refinery-ice-rename");

        // Baseline: RefineryTrackerReplayTests.RawToRefinedRename_MergesIntoOneOrder. The refinery
        // renames the raw input to its refined product between panels (SETUP "ICE (RAW)" ->
        // PROCESSING/COMPLETED "PRESSURIZED ICE"); quality is stable across the rename, so the two
        // panels must resolve to ONE order with ONE material rather than splitting into a yield-less
        // SETUP order plus an orphaned COMPLETED one. This corpus was captured through COMPLETED
        // only, so it reaches Ready/Complete — the Collected transition is the other test's job.
        Verify(ledger, () =>
        {
            var order = Assert.Single(ledger.All);
            Assert.True(order.State >= OrderState.Ready, $"expected Ready or later, got {order.State}");
            Assert.Equal(Completeness.Complete, order.Completeness);
            var material = Assert.Single(order.Materials);
            Assert.Equal(714, material.Quality);
            Assert.True(material.YieldCscu > 0, "refined yield must merge onto the material");
            Assert.NotNull(order.TotalYieldCscu);
        });
    }

    /// <summary>
    /// The regression this corpus exists for: a COMPLETED panel filled to its 10-row capacity.
    /// </summary>
    /// <remarks>
    /// Both monolith corpora top out at four material rows, so both stayed green while
    /// <see cref="Rois"/>' <c>YieldList</c> height covered only the first five rows of the panel's
    /// list container — the plugin reported five rows and a "scroll the list" nudge about a list
    /// that was fully on screen. Asserting all ten rows (rather than a Completeness verdict) is the
    /// point: the ROI regression is invisible to an aggregate check, exactly as TASK-RFN-01 found
    /// for the dropped-row bug in the refinery-confirm baseline.
    /// </remarks>
    [Fact]
    public async Task RefineryFullList_corpus_reads_every_visible_row()
    {
        var ledger = await RunCorpusAsync("refinery-full-list");

        Verify(ledger, () =>
        {
            var order = Assert.Single(ledger.All);
            Assert.True(order.State >= OrderState.Ready, $"expected Ready or later, got {order.State}");

            // All ten rows reached the ledger — the count is the regression guard, since the old
            // height stopped at five.
            Assert.Equal(10, order.RowsSeen);

            // Each row by its (name, quality) identity, with the yield OCR actually returns. Four of
            // this order's five material names appear twice at different qualities (only GOLD and
            // TUNGSTEN are singletons), which is why FindMaterial keys on the pair and not the name.
            AssertYield(FindMaterial(order, "BEXALITE", 302), 3);
            AssertYield(FindMaterial(order, "BEXALITE", 597), 12);
            AssertYield(FindMaterial(order, "TORITE", 262), 167);
            AssertYield(FindMaterial(order, "TORITE", 785), 54);
            AssertYield(FindMaterial(order, "LINDINIUM", 305), 23);
            AssertYield(FindMaterial(order, "TUNGSTEN", 530), 24);
            AssertYield(FindMaterial(order, "ALUMINUM", 318), 117);
            AssertYield(FindMaterial(order, "ALUMINUM", 511), 8);

            // The two rows whose YIELD cell Windows OCR does not return a word for at all: GOLD#553
            // (screen shows 0) and LINDINIUM#585 (screen shows 27). Both reach the ledger as the 0
            // "unknown" sentinel, so only their presence is asserted, not their value.
            //
            // Deliberately not chased here, because it is not this rect's doing: a probe of
            // OcrPipeline's exact crop/scale/red-channel path over this frame drops both cells at the
            // OLD 210 height too (and at 300, and over a 150-tall crop of those rows alone), so the
            // miss is ROI-independent. The one lever that recovers LINDINIUM#585 is ListScale 3.5-4.0,
            // which is shared with SetupList and starts surfacing the per-row icon glyph as stray "e"
            // name tokens — a change that has to be justified against every corpus, not smuggled in
            // behind a geometry fix. GOLD#553's 0 is not recovered at any scale from 2.0 to 5.0.
            Assert.Contains(order.Materials, m => m.Name == "GOLD" && m.Quality == 553);
            Assert.Contains(order.Materials, m => m.Name == "LINDINIUM" && m.Quality == 585);

            // The checksum line is read from its own ROI, below the list; pinned so a future
            // YieldList height that swallowed the divider would show up here as well as in the rows.
            Assert.Equal(440, order.TotalYieldCscu);

            // Not Complete, and correctly so — but the arithmetic is worth stating exactly, because
            // the obvious reading of it is wrong. The ten rows AS SHOWN sum to 435 against a 440
            // total, and that 5 cSCU gap is real: this order holds more materials than the container
            // can display at once. But 5 is INSIDE ChecksumTolerance(10) — a truncation worth less
            // than about a cSCU per visible row is invisible to that check by construction, so the
            // hidden rows alone would not fail it. What actually fails here is the two 0-sentinel
            // yields above: they drop the plugin's own sum to 408 (a gap of 32), and independently
            // fail the !materials.Any(YieldCscu == 0) half of the clean check. Contrast the ROI bug
            // this corpus was added for, which faked this same verdict on a fully visible list.
            Assert.Equal(Completeness.Partial, order.Completeness);
        });
    }

    /// <summary>
    /// Replays one corpus through the real engine (spawned) → pipe → SDK → plugin path and hands
    /// back the ledger it produced. The temp ledger is deleted before returning: what the assertions
    /// read is the in-memory state, which is authoritative either way, and no test may touch a real
    /// ledger.
    /// </summary>
    /// <remarks>
    /// Drives the plugin through the public <see cref="OcrxPluginHost.RunAsync"/> surface — the
    /// same entry point Program uses — via <see cref="ReplayHarness"/>, rather than reaching into
    /// RefineryLogic over an InternalsVisibleTo grant (killed in TASK-11). The ledger the host's
    /// plugin opens is captured through <see cref="RefineryPlugin"/>'s test seam. An explicit
    /// <c>--ledger</c> override (via the plugin's ledger-override closure) points the replay at a
    /// file this method can delete afterwards.
    /// </remarks>
    private async Task<OrderLedger> RunCorpusAsync(string corpus)
    {
        var corpusDir = ReplayCorpus.Resolve(Path.Combine(FixturesRoot, corpus));
        Assert.True(Directory.Exists(corpusDir), $"corpus not copied to the test output: {corpusDir}");

        var frameCount = Directory.EnumerateFiles(corpusDir, "*.png").Count();
        // Non-empty first, as ScanLoopTests does: Directory.Exists is satisfied by an empty
        // directory, so a corpus that failed to copy would otherwise reach the baselines as the
        // same empty ledger a genuine parity break produces.
        Assert.NotEqual(0, frameCount);

        var ledgerDir = Path.Combine(Path.GetTempPath(), $"sc-parity-{Guid.NewGuid():N}");
        var ledgerPath = Path.Combine(ledgerDir, "orders.jsonl");

        OrderLedger? captured = null;

        try
        {
            var plugin = new RefineryPlugin(
                new RefineryConfig(),
                ledgerOverride: () => ledgerPath,
                onLedgerOpened: l => captured = l);

            // Timeout left at ReplayOptions' own default (5 min) — real OCR over these corpora
            // measures 1-2s each, so anything near it means something is stuck rather than slow.
            var result = await ReplayHarness.RunAsync(new ReplayOptions
            {
                EnginePath = EngineLocator.Resolve(),
                CorpusDir = corpusDir,
                Plugin = plugin,
            });

            output.WriteLine($"{corpus}: {frameCount} frame(s) replayed, exit {result.ExitCode}, " +
                $"reason {result.Reason}, {result.Records.Count} record(s), " +
                $"{captured?.All.Count ?? 0} order(s)");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(StreamEndReason.ReplayCompleted, result.Reason);

            // The host opens the ledger on its first connect; a corpus that never let it connect is
            // a failure of the harness, not a parity result.
            Assert.True(captured is not null, $"{corpus}: the plugin never opened a ledger (never connected)");
            Assert.NotEmpty(captured.All);

            // A second parity surface pinned for free: the plugin's own tee to services.Emit, kept
            // in step with the ledger it built from the same run.
            Assert.NotEmpty(result.Records);

            return captured;
        }
        finally
        {
            if (Directory.Exists(ledgerDir))
                Directory.Delete(ledgerDir, recursive: true);
        }
    }

    /// <summary>
    /// Runs the baseline assertions, dumping the ledger it actually produced before letting a
    /// failure through. A parity failure means some layer of the split disagrees with the monolith,
    /// and "Assert.Single() Failure" alone says nothing about which — the records do.
    /// </summary>
    private void Verify(OrderLedger ledger, Action assertions)
    {
        try
        {
            assertions();
        }
        catch (Exception)
        {
            output.WriteLine($"--- ledger: {ledger.All.Count} order(s) ---");
            foreach (var order in ledger.All)
                output.WriteLine(Describe(order));
            throw;
        }
    }

    private static string Describe(WorkOrder order)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{order.Id} [{order.State}, {order.Completeness}] " +
            $"station={order.Station} process={order.Process} cost={order.Cost} eta={order.Eta}");
        sb.AppendLine($"  key={order.Key}");
        sb.AppendLine($"  sources={string.Join(",", order.Sources)} rowsSeen={order.RowsSeen} " +
            $"total={order.TotalYieldCscu?.ToString() ?? "null"}");
        foreach (var m in order.Materials)
            sb.AppendLine($"  material name={m.Name} quality={m.Quality} qty={m.QtyCscu} " +
                $"yield={m.YieldCscu} refine={m.RefineOn}");
        return sb.ToString().TrimEnd();
    }
}
