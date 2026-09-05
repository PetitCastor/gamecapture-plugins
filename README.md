# OCRX plugins

Plugins add Star Citizen trackers to
[OCRX](https://ocrx.org). A plugin is a
small `net10.0` console app: it asks the engine to scan a few screen regions,
reads the results on each tick, and emits records.

The engine does the capture, OCR, connection, and reconnecting. A plugin only
defines what to read and what to do with it.

| Stable beta plugin | What it tracks |
| --- | --- |
| `SignaturePlugin` | Scan signatures |

MissionPlugin and RefineryPlugin source remain in the repository for development and regression
coverage, but they are not listed in the stable catalog or promoted as available OCRX 2 plugins.

To use a released plugin, download its `win-x64` zip from
[Releases](https://github.com/PetitCastor/ocrx-plugins/releases), unzip
it, and run the executable while OCRX Engine is running.

The [`plugins.json`](plugins.json) catalog lists stable plugins only. Release selection is explicit
in the [release manifests](.github/release-manifests/README.md).

## Create a plugin

Start with the published template:
```powershell
dotnet new install Ocrx.Plugin.Template --version 2.0.0
dotnet new ocrx-plugin -n MyPlugin
```

To add it to this repository, keep the application in `src/MyPlugin/` and its
tests in `tests/MyPlugin.Tests/`, then add both projects to
`OcrxPlugins.slnx`.

```powershell
dotnet sln OcrxPlugins.slnx add src/MyPlugin/MyPlugin.csproj
dotnet sln OcrxPlugins.slnx add tests/MyPlugin.Tests/MyPlugin.Tests.csproj
```

Use the SDK packages from NuGet. Do not add a `ProjectReference` to a local
engine clone.

```xml
<ItemGroup>
  <PackageReference Include="Ocrx.Contracts" Version="2.0.0" />
  <PackageReference Include="Ocrx.Sdk" Version="2.0.0" />
</ItemGroup>
```

The entry point can stay this small:

```csharp
using Ocrx.Sdk;

return await OcrxPluginHost.RunAsync(new MyPlugin.MyPlugin(), args);
```

## The smallest useful plugin

First declare the part of the screen to read. Coordinates use OCRX's
`2560x1440` reference space; the engine scales them to the live frame.

```csharp
// Rois.cs
using Ocrx.Contracts;
using Ocrx.Sdk;

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
using Ocrx.Contracts;
using Ocrx.Sdk;

namespace MyPlugin;

public sealed class MyPlugin : IOcrxPlugin
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
dotnet build OcrxPlugins.slnx
dotnet test OcrxPlugins.slnx --filter "Category!=Integration"
```

The excluded integration tests replay captured PNGs through a real engine. Run
them too when the Windows OCR language pack and an engine binary are available:

```powershell
$env:OCRX_ENGINE_PATH = "C:\tools\ocrx\Ocrx.Engine.exe"
dotnet test OcrxPlugins.slnx
```

`engine-version.txt` pins the released engine used by CI. Update it only after
that engine release exists. The plugin projects must continue to consume the
published SDK packages, not the side-by-side engine source.

## Need more detail?

- [Plugin authoring guide](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/PLUGIN-AUTHORING.md)
  - configuration, manual ticks, error policies, ROI calibration, and replay tests.
- [Compatibility guide](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/COMPATIBILITY.md)
  - plugin and engine protocol compatibility.
- [SignaturePlugin guide](src/SignaturePlugin/README.md) - a concrete plugin with
  calibration and output details.
