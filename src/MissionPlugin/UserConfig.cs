using GameCapture.Sdk;

internal sealed class MissionPluginConfig : PluginConfig;

internal static class UserConfig
{
    private const string ResourceName = "MissionPlugin.config.json";

    public static string Ensure() =>
        ConfigSeed.EnsureInLocalAppData(typeof(UserConfig).Assembly, ResourceName, "MissionPlugin");
}
