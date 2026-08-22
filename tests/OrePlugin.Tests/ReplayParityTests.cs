using GameCapture.Sdk;
using GameCapture.Sdk.Testing;
using Xunit;

namespace OrePlugin.Tests;

/// <summary>
/// A spawned engine owns a named pipe and a Windows OCR instance, so two of these must never run
/// at once. This is what actually serializes them — the <c>[Collection]</c> attribute on
/// <see cref="ReplayParityTests"/> alone only groups; without <c>DisableParallelization</c> the
/// group still runs beside every other collection in the assembly. Keep this even with only one
/// test below: it is the thing that stops the second test you add here from racing the first one
/// for the same pipe.
/// </summary>
[Collection("ReplayParity")]
public class ReplayParityTests
{
    /// <summary>
    /// Parity smoke test: spawns a real GameCapture.Engine.exe replaying a PNG corpus and drives
    /// this plugin through its real GameCapturePluginHost path — public SDK plus an engine binary,
    /// no in-proc shortcuts. Skipped until you have both a corpus and an engine to point at; see
    /// the calibration workflow in README.md and docs/REPLAY.md for how to capture one, then:
    ///   1. Copy the captured PNGs into Fixtures/Replay/my-corpus/ and add them to this csproj:
    ///      &lt;None Include="Fixtures\Replay\my-corpus\**\*.png" CopyToOutputDirectory="PreserveNewest" /&gt;
    ///   2. Point GAMECAPTURE_ENGINE_PATH at the engine you built or unpacked.
    ///   3. Remove the Skip.
    /// </summary>
    [Fact(Skip = "needs corpus + GAMECAPTURE_ENGINE_PATH")]
    [Trait("Category", "Integration")]
    public async Task Corpus_emits_one_record()
    {
        var corpusDir = ReplayCorpus.Resolve("Fixtures/Replay/my-corpus");
        Assert.True(Directory.Exists(corpusDir), $"corpus not copied to the test output: {corpusDir}");

        var result = await ReplayHarness.RunAsync(new ReplayOptions
        {
            EnginePath = EngineLocator.Resolve(),
            CorpusDir = corpusDir,
            Plugin = new OrePlugin(),
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StreamEndReason.ReplayCompleted, result.Reason);

        var record = Assert.Single(result.Records);
        Assert.Equal("OrePlugin", record.Plugin);
        Assert.Equal(TriggerKind.Auto, record.Trigger);
    }
}
