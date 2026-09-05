using Ocrx.Sdk;

var config = PluginConfig.Load<MissionPluginConfig>(UserConfig.Ensure());
return await OcrxPluginHost.RunAsync(new MissionPlugin.MissionPlugin(), args,
    new PluginHostOptions { Config = config });
