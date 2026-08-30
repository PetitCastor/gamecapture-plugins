# GameCapture plugins

Plugins add Star Citizen trackers to
[GameCapture](https://github.com/PetitCastor/gamecapture-engine). A plugin is a
small `net10.0` console app: it asks the engine to scan a few screen regions,
reads the results on each tick, and emits records.

The engine does the capture, OCR, connection, and reconnecting. A plugin only
defines what to read and what to do with it.

| Plugin | What it tracks |
| --- | --- |
| `MissionPlugin` | Mission-board accepts |
| `RefineryPlugin` | Refinery work orders |
| `SignaturePlugin` | Scan signatures |

To use a released plugin, download its `win-x64` zip from
[Releases](https://github.com/PetitCastor/gamecapture-plugins/releases), unzip
it, and run the executable while GameCapture Engine is running.

The [`plugins.json`](plugins.json) catalog lists stable plugins only. Preview builds are public,
opt-in GitHub prereleases; see [RELEASING.md](RELEASING.md) for their release and testing policy.

## Create a plugin

Start with the published template:
```powershell
dotnet new install GameCapture.Plugin.Template
dotnet new gamecapture-plugin -n MyPlugin
```

To add it to this repository, keep the application in `src/MyPlugin/` and its
tests in `tests/MyPlugin.Tests/`, then add both projects to
`GameCapturePlugins.slnx`.

```powershell
dotnet sln GameCapturePlugins.slnx add src/MyPlugin/MyPlugin.csproj
dotnet sln GameCapturePlugins.slnx add tests/MyPlugin.Tests/MyPlugin.Tests.csproj
```

Use the SDK packages from NuGet. Do not add a `ProjectReference` to a local
engine clone.

```xml
<ItemGroup>
  <PackageReference Include="GameCapture.Contracts" Version="1.*" />
  <PackageReference Include="GameCapture.Sdk" Version="1.*" />
</ItemGroup>
```

The entry point can stay this small:

```csharp
using GameCapture.Sdk;

return await GameCapturePluginHost.RunAsync(new MyPlugin.MyPlugin(), args);
```

## The smallest useful plugin

First declare the part of the screen to read. Coordinates use GameCapture's
`2560x1440` reference space; the engine scales them to the live frame.

```csharp
// Rois.cs
using GameCapture.Contracts;
using GameCapture.Sdk;

namespace MyPlugin;

public static class Rois
{
    public static readonly RoiSubscription Counter =
        new("counter", new RoiRect(1000, 110, 420, 100), 3.0, RoiKind.Text);

    public static readonly IReadOnlyList<RoiSubscription> All = [Counter];
}
```

Then read that region. This example emits a record only when its text changes.

```csharp
// MyPlugin.cs
using GameCapture.Contracts;
using GameCapture.Sdk;

namespace MyPlugin;

public sealed class MyPlugin : IGameCapturePlugin
{
    private string? _lastValue;

    public string Name => "my-plugin";
    public IReadOnlyList<RoiSubscription> Rois => global::MyPlugin.Rois.All;

    public Task OnTickAsync(TickContext ctx, CancellationToken ct)
    {
        if (!ctx.Tick.TryGetText(Rois.Counter.Id, out var text))
            return Task.CompletedTask;

        var value = text.Trim();
        if (value.Length == 0 || value == _lastValue)
            return Task.CompletedTask;

        _lastValue = value;
        ctx.Services.Emit(new CaptureRecord(
            ctx.Tick.Timestamp, Name, TriggerKind.Auto, value));
        return Task.CompletedTask;
    }
}
```

Use `TryGetText`, `TryGetOcr`, or `TryGetPixels`: a failed region is different
from a region that successfully read as blank. Ticks arrive one at a time, so
ordinary plugin state does not need locks.

## Build and test

From this repository's root:

```powershell
dotnet build GameCapturePlugins.slnx
dotnet test GameCapturePlugins.slnx --filter "Category!=Integration"
```

The excluded integration tests replay captured PNGs through a real engine. Run
them too when the Windows OCR language pack and an engine binary are available:

```powershell
$env:GAMECAPTURE_ENGINE_PATH = "C:\tools\gamecapture\GameCapture.Engine.exe"
dotnet test GameCapturePlugins.slnx
```

`engine-version.txt` pins the released engine used by CI. Update it only after
that engine release exists. The plugin projects must continue to consume the
published SDK packages, not the side-by-side engine source.

## Need more detail?

- [Plugin authoring guide](https://github.com/PetitCastor/gamecapture-engine/blob/master/docs/PLUGIN-AUTHORING.md)
  - configuration, manual ticks, error policies, ROI calibration, and replay tests.
- [Compatibility guide](https://github.com/PetitCastor/gamecapture-engine/blob/master/docs/COMPATIBILITY.md)
  - plugin and engine protocol compatibility.
- [SignaturePlugin guide](src/SignaturePlugin/README.md) - a concrete plugin with
  calibration and output details.
