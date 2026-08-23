using GameCapture.Sdk;

var config = PluginConfig.Load<MissionPluginConfig>(UserConfig.Ensure());
return await GameCapturePluginHost.RunAsync(new MissionPlugin.MissionPlugin(), args,
    new PluginHostOptions { Config = config });
