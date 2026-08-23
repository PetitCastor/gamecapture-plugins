using System.Text.Json;
using GameCapture.Sdk;
using GameCapture.Sdk.Testing;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// A spawned engine owns a named pipe and a Windows OCR instance, so two of these must never run
/// at once. This is what actually serializes them: the <c>[Collection]</c> attribute on
/// <see cref="ReplayParityTests"/> alone only groups; without <c>DisableParallelization</c> the
/// group still runs beside every other collection in the assembly. Keep this even with only one
/// test below: it is the thing that stops the second test you add here from racing the first one
/// for the same pipe.
/// </summary>
[Collection("ReplayParity")]
public class ReplayParityTests
{
    private const string Corpus = "Fixtures/Replay/scan-signature";
    private const string Manifest = "manifest.json";

    /// <summary>
    /// Parity smoke test: spawns a real GameCapture.Engine.exe replaying a PNG corpus and drives
    /// this plugin through its real GameCapturePluginHost path. Needs GAMECAPTURE_ENGINE_PATH
    /// pointed at a built or unpacked GameCapture.Engine.exe and a Windows OCR language pack.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ScanSignature_corpus_matches_manifest_records()
    {
        var corpusDir = ReplayCorpus.Resolve(Corpus);
        Assert.True(Directory.Exists(corpusDir), $"corpus not copied to the test output: {corpusDir}");

        var expectedFrames = ReadManifest(Path.Combine(corpusDir, Manifest));
        Assert.NotEmpty(expectedFrames);

        AssertManifestLabelsEveryPng(corpusDir, expectedFrames);

        foreach (var expected in expectedFrames)
        {
            var oneFrameCorpus = CreateOneFrameCorpus(corpusDir, expected.File);
            try
            {
                var result = await ReplayHarness.RunAsync(new ReplayOptions
                {
                    EnginePath = EngineLocator.Resolve(),
                    CorpusDir = oneFrameCorpus,
                    Plugin = new SignaturePlugin(SignatureTable.LoadEmbedded()),
                });

                Assert.Equal(0, result.ExitCode);
                Assert.Equal(StreamEndReason.ReplayCompleted, result.Reason);

                var record = Assert.Single(result.Records);
                Assert.Equal("SignaturePlugin", record.Plugin);
                Assert.Equal(TriggerKind.Auto, record.Trigger);
                Assert.Equal(RecordKind.Observation, record.Kind);

                using var json = JsonDocument.Parse(record.RawText);
                Assert.Equal(expected.Name, json.RootElement.GetProperty("name").GetString());
                Assert.Equal(expected.Kind, json.RootElement.GetProperty("kind").GetString());
            }
            finally
            {
                Directory.Delete(oneFrameCorpus, recursive: true);
            }
        }
    }

    private static IReadOnlyList<(string File, string Name, string Kind)> ReadManifest(string path)
    {
        Assert.True(File.Exists(path), $"manifest not copied to the test output: {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(
            document.RootElement.TryGetProperty("frames", out var frames) &&
            frames.ValueKind == JsonValueKind.Array,
            "manifest must contain a 'frames' array");

        var expected = new List<(string File, string Name, string Kind)>();
        foreach (var frame in frames.EnumerateArray())
        {
            var file = frame.GetProperty("file").GetString() ?? string.Empty;
            var name = frame.GetProperty("name").GetString() ?? string.Empty;
            var kind = frame.GetProperty("kind").GetString() ?? string.Empty;

            Assert.False(string.IsNullOrWhiteSpace(file), "manifest frame file is required");
            Assert.False(string.IsNullOrWhiteSpace(name), "manifest frame name is required");
            Assert.False(string.IsNullOrWhiteSpace(kind), "manifest frame kind is required");

            expected.Add((file, name, kind));
        }

        return expected;
    }

    private static void AssertManifestLabelsEveryPng(
        string corpusDir,
        IReadOnlyList<(string File, string Name, string Kind)> frames)
    {
        var manifestFiles = frames
            .Select(f => NormalizeManifestPath(f.File))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(frames.Count, manifestFiles.Count);

        var pngFiles = Directory.EnumerateFiles(corpusDir, "*.png", SearchOption.AllDirectories)
            .Select(path => NormalizeManifestPath(Path.GetRelativePath(corpusDir, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            manifestFiles.OrderBy(file => file, StringComparer.OrdinalIgnoreCase),
            pngFiles.OrderBy(file => file, StringComparer.OrdinalIgnoreCase));
    }

    private static string CreateOneFrameCorpus(string sourceCorpusDir, string manifestFile)
    {
        var source = Path.Combine(sourceCorpusDir, manifestFile);
        var tempCorpus = Path.Combine(Path.GetTempPath(), $"signature-parity-{Guid.NewGuid():N}");
        var target = Path.Combine(tempCorpus, Path.GetFileName(manifestFile));

        Directory.CreateDirectory(tempCorpus);
        File.Copy(source, target);
        return tempCorpus;
    }

    private static string NormalizeManifestPath(string path) => path.Replace('\\', '/');
}
