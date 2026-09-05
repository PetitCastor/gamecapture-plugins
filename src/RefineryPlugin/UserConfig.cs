using Ocrx.Sdk;

internal static class UserConfig
{
    private const string ResourceName = "RefineryPlugin.config.json";

    public static string Ensure() =>
        ConfigSeed.EnsureInLocalAppData(typeof(UserConfig).Assembly, ResourceName, "RefineryPlugin");
}
