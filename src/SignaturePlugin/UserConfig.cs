using Ocrx.Sdk;

internal sealed class SignaturePluginConfig : PluginConfig;

internal static class UserConfig
{
    private const string ResourceName = "SignaturePlugin.config.json";

    public static string Ensure() =>
        ConfigSeed.EnsureInLocalAppData(typeof(UserConfig).Assembly, ResourceName, "SignaturePlugin");
}
