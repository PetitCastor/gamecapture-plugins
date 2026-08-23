using GameCapture.Sdk;
using GameCapture.Sdk.Overlay;

// The whole lifecycle — connect, subscribe, feed ticks, reconnect, summarise — is the host's; this
// process only supplies the plugin. The host loads config.json itself (PluginHostOptions.ConfigFileName
// defaults to "config.json") for the two settings every plugin has: pipeName and saveDebugFrames.
var table = SignaturePlugin.SignatureTable.LoadUserFile();
var config = PluginConfig.Load<SignaturePluginConfig>(UserConfig.Ensure());

// The overlay sink lives in the opt-in GameCapture.Sdk.Overlay package, so the core SDK cannot
// construct it: an "overlay" output whose factory was never registered here silently routes to a
// no-op sink. Registering it is what makes the config.json entry actually draw. The factory itself
// degrades to a no-op off Windows, so this stays safe on any platform.
var options = new PluginHostOptions { Config = config, OverlayFactory = new OverlaySinkFactory() };

return await GameCapturePluginHost.RunAsync(new SignaturePlugin.SignaturePlugin(table), args, options);
