using System.Text.Json.Nodes;
using Ocrx.Sdk;
using Xunit;

namespace SignaturePlugin.Tests;

/// <summary>
/// The shipped <c>config.json</c> is what actually reaches a user, so the claims made about it are
/// worth pinning here rather than only against the SDK's own stand-in fixtures. An overlay output
/// that never lands in a real user's file is indistinguishable from one that was never wired.
/// </summary>
public class ConfigDefaultsTests : IDisposable
{
    private const string ResourceName = "SignaturePlugin.config.json";

    private readonly string _dir =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"sig-cfg-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Seed(string? existing = null)
    {
        var path = Path.Combine(_dir, "config.json");
        if (existing is not null)
            File.WriteAllText(path, existing);

        return ConfigSeed.Ensure(typeof(SignaturePlugin).Assembly, ResourceName, path);
    }

    private static string[] OutputTypes(string path)
    {
        var outputs = JsonNode.Parse(File.ReadAllText(path))!["outputs"] as JsonArray ?? new JsonArray();
        return [.. outputs.Select(node => node!["type"]!.GetValue<string>())];
    }

    /// <summary>
    /// The config this repo shipped before the overlay existed, which is what anyone who ran the
    /// plugin before it was wired still has on disk.
    /// </summary>
    private const string LegacyConfig = """
        {
          "pipeName": "OCRX.Engine",
          "saveDebugFrames": false,
          "outputs": [
            { "type": "json", "path": "captures/signatures.jsonl", "dedupeOnChange": true, "recordClears": true }
          ]
        }
        """;

    [Fact]
    public void FirstRun_ShipsBothSinks()
    {
        var path = Seed();

        Assert.Equal(["json", "overlay"], OutputTypes(path));
    }

    [Fact]
    public void FirstRun_DoesNotLeakTheAddedInBookkeeping()
    {
        var path = Seed();

        Assert.DoesNotContain("addedIn", File.ReadAllText(path));
    }

    /// <summary>The whole reason the seeder exists: a pre-overlay config has to gain the overlay.</summary>
    [Fact]
    public void LegacyConfig_GainsTheOverlay()
    {
        var path = Seed(LegacyConfig);

        Assert.Equal(["json", "overlay"], OutputTypes(path));
    }

    [Fact]
    public void LegacyConfig_KeepsItsOwnJsonSinkSettings()
    {
        var path = Seed(LegacyConfig);

        var json = (JsonNode.Parse(File.ReadAllText(path))!["outputs"] as JsonArray)![0]!;
        Assert.Equal("captures/signatures.jsonl", json["path"]!.GetValue<string>());
        Assert.True(json["recordClears"]!.GetValue<bool>());
    }

    /// <summary>
    /// Someone who already reset their config by hand must not end up with the overlay twice.
    /// </summary>
    [Fact]
    public void ConfigThatAlreadyHasTheOverlay_IsOnlyStamped()
    {
        var path = Seed();
        var before = OutputTypes(path);

        ConfigSeed.Ensure(typeof(SignaturePlugin).Assembly, ResourceName, path);

        Assert.Equal(before, OutputTypes(path));
    }

    [Fact]
    public void TheOverlayItShips_RendersTheResolvedNameNotRawJson()
    {
        var path = Seed();

        var overlay = (JsonNode.Parse(File.ReadAllText(path))!["outputs"] as JsonArray)!
            .First(node => node!["type"]!.GetValue<string>() == "overlay")!;

        // The key must exist in CaptureRecord.Fields or OverlayRecordSink falls back to dumping the
        // entire raw JSON record on screen — see the plugins repo's own Fields contract test.
        //
        // {cluster} rather than the older "{name} x{count}": those two rendered separately cannot
        // express an ambiguous total (19200 is Savrilium x6 and Aslarite x5 alike), and printing only
        // the winner of that coin flip would be a confident wrong answer.
        Assert.Equal("{cluster}", overlay["overlay"]!["template"]!.GetValue<string>());
    }

    /// <summary>
    /// Load-bearing: visibility is driven by observation/clear edges (the absence debouncer,
    /// <c>Reconnecting</c>) rather than a timer. A nonzero linger would race that edge-driven clear
    /// and hide a still-current reading before its time.
    /// </summary>
    [Fact]
    public void TheOverlayItShips_HasNoLinger()
    {
        var path = Seed();

        var overlay = (JsonNode.Parse(File.ReadAllText(path))!["outputs"] as JsonArray)!
            .First(node => node!["type"]!.GetValue<string>() == "overlay")!;

        Assert.Equal(0, overlay["overlay"]!["lingerMs"]!.GetValue<int>());
    }

    [Fact]
    public void TheShippedDefault_LoadsThroughPluginConfig()
    {
        var path = Seed();

        var config = PluginConfig.Load<SignatureConfigForTest>(path);

        Assert.Equal(2, config.ConfigVersion);
        Assert.Equal(["json", "overlay"], config.Outputs.Select(output => output.Type).ToArray());
    }

    private sealed class SignatureConfigForTest : PluginConfig;
}
