# gamecapture-plugins
[![CI](https://github.com/PetitCastor/gamecapture-plugins/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/PetitCastor/gamecapture-plugins/actions/workflows/ci.yml)
[![Release](https://github.com/PetitCastor/gamecapture-plugins/actions/workflows/release.yml/badge.svg)](https://github.com/PetitCastor/gamecapture-plugins/releases)
Star Citizen trackers built as pure
[GameCapture](https://github.com/PetitCastor/gamecapture-engine) SDK consumers.
This repo is the place to build and maintain plugins. A plugin is a plain console
process that declares the screen regions it cares about, receives OCR/pixel ticks
from a running engine, and emits records. It does not capture frames, run OCR, or
talk gRPC directly; `GameCapturePluginHost` from `GameCapture.Sdk` owns the
engine connection, subscription, reconnect loop, cancellation, and summary.
Current plugins:
| Plugin | Purpose |
| --- | --- |
| `MissionPlugin` | Watches the mission board and emits mission acceptance captures. |
| `RefineryPlugin` | Tracks refinery work-order state from refinery UI panels. |
| `SignaturePlugin` | Matches scan signature values to ore, asteroid, or debris metadata. |
## Getting Plugins
Each plugin ships as a self-contained `win-x64` zip on this repo's
[GitHub Releases](https://github.com/PetitCastor/gamecapture-plugins/releases). [`plugins.json`](plugins.json)
at the repo root is a small catalog — id, name, description, and a stable
`/releases/latest/download/...` link for each one — so you don't have to hunt the releases page to
see what's available.
To use one: download its zip, unzip it, and run the exe next to (or pointed at) a running engine.
No manual version matching is needed — the engine and plugin negotiate a protocol version at
connect time and the engine rejects an incompatible plugin automatically (see
[`COMPATIBILITY.md`](https://github.com/PetitCastor/gamecapture-engine/blob/master/docs/COMPATIBILITY.md)
in the engine repo).
## First Rules
- Keep each plugin as a plain `net10.0` console app. The Windows TFM and capture
  stack stop at the engine.
- Reference `GameCapture.Sdk` and `GameCapture.Contracts` from nuget.org. Do not
  add a `ProjectReference` to the side-by-side engine clone.
- Keep parsing, ROI declarations, state, config, and tests inside the plugin's own
  `src/<PluginName>/` and `tests/<PluginName>.Tests/` folders.
- Declare ROI rectangles in reference space, `2560x1440`, and let the engine scale
  them to the live frame.
- Prefer `TryGetText`, `TryGetOcr`, and `TryGetPixels` over value-only accessors so
  failed ROIs do not look like blank readings.
- Treat replay parity as the integration gate. Unit tests prove parser/state logic;
  replay tests prove the plugin works through a real engine binary.
## Add A Plugin Here
For a standalone plugin repo, use the published template:
```powershell
dotnet new install GameCapture.Plugin.Template
dotnet new gamecapture-plugin -n MyPlugin
```
Inside this repository, create the same shape manually or scaffold in a scratch
directory and move the useful files into place:
```text
src/MyPlugin/
  MyPlugin.csproj
  Program.cs
  Rois.cs
  MyTrackerPlugin.cs
  config.json
tests/MyPlugin.Tests/
  MyPlugin.Tests.csproj
  MyPluginTests.cs
  ReplayParityTests.cs
  ReplayParityCollection.cs
```
Add both projects to `GameCapturePlugins.slnx`:
```xml
<Project Path="src/MyPlugin/MyPlugin.csproj" />
<Project Path="tests/MyPlugin.Tests/MyPlugin.Tests.csproj" />
```
Start the plugin project as a normal executable:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>MyPlugin</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="GameCapture.Contracts" Version="1.*" />
    <PackageReference Include="GameCapture.Sdk" Version="1.*" />
  </ItemGroup>
  <ItemGroup>
    <None Update="config.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="MyPlugin.Tests" />
  </ItemGroup>
</Project>
```
The entry point should stay small:
```csharp
using GameCapture.Sdk;
return await GameCapturePluginHost.RunAsync(new MyPlugin.MyTrackerPlugin(), args);
```
Use `config.json` for host-level settings. The pipe name must match the engine:
```json
{
  "pipeName": "GameCapture.Engine",
  "saveDebugFrames": false
}
```
If the plugin needs its own settings, derive from `PluginConfig`, load the derived
type in `Program.cs`, and pass it through `PluginHostOptions.Config`. Resolve
relative paths against the config file location, not the shell's working directory.
## Implement The Plugin
Every plugin implements `IGameCapturePlugin`:
```csharp
using GameCapture.Contracts;
using GameCapture.Sdk;
namespace MyPlugin;
public static class Rois
{
    public static readonly RoiSubscription Counter =
        new("counter", new RoiRect(1000, 110, 420, 100), 3.0, RoiKind.Text);
    public static readonly IReadOnlyList<RoiSubscription> All = [Counter];
}
public sealed class MyTrackerPlugin : IGameCapturePlugin
{
    private string? _last;
    public string Name => "my-plugin";
    public IReadOnlyList<RoiSubscription> Rois => MyPlugin.Rois.All;
    public RoiErrorPolicy ErrorPolicy => RoiErrorPolicy.AbortTick;
    public Task OnTickAsync(TickContext ctx, CancellationToken ct)
    {
        if (!ctx.Tick.TryGetText(MyPlugin.Rois.Counter.Id, out var text))
            return Task.CompletedTask;
        var value = text.Trim();
        if (value.Length == 0 || value == _last)
            return Task.CompletedTask;
        _last = value;
        ctx.Services.Emit(new CaptureRecord(ctx.Tick.Timestamp, Name, TriggerKind.Auto, value));
        return Task.CompletedTask;
    }
}
```
Use this as the starting point, then add only the behavior the tracker needs:
- Put all ROI ids in one place, usually `Rois.cs`.
- Keep state changes in `OnTickAsync`; the host delivers ticks sequentially, so
  normal plugin state does not need locks.
- Override `OnManualTickAsync` when the engine hotkey should force a different
  behavior than the normal tick path.
- Implement `OnSessionEvent` when dropped ticks or reconnects change what your
  state machine can safely infer.
- Emit `CaptureRecord` values through `ctx.Services.Emit`; do not duplicate the
  same event with console logging.
- Use `LogVerbose` for per-tick diagnostics so normal runs stay quiet.
The full SDK guide is in the engine repo:
[`docs/PLUGIN-AUTHORING.md`](https://github.com/PetitCastor/gamecapture-engine/blob/master/docs/PLUGIN-AUTHORING.md).
## Calibrate ROIs
The practical calibration loop is:
1. Run an engine with frame saving enabled.
2. Set `"saveDebugFrames": true` in the plugin's `config.json`.
3. Run the plugin with `--verbose`.
4. Put the game UI in the state the plugin should read.
5. Press the engine hotkey, default `Ctrl+Shift+F12`.
6. Compare the dumped crop with the intended UI region.
7. Nudge `RoiRect(x, y, width, height)` and `Scale`, then repeat.
Use `RoiKind.Text` for plain text regions, `RoiKind.Detailed` when word positions
matter, and `RoiKind.Pixels` for small color probes. Do not use large pixel
regions as a substitute for OCR; they are bounded by the engine's pixel payload
budget after scaling to the live frame size.
`DumpFrameAsync` and `ReadRoiAsync` are calibration aids. Do not use them as the
source of truth for normal decisions because they read the engine's most recently
scanned frame, not necessarily the same frame as the current tick.
## Test A Plugin
Unit tests use `GameCapture.Sdk.Testing` and do not need a running engine:
```csharp
using GameCapture.Sdk;
using GameCapture.Sdk.Testing;
using Xunit;
namespace MyPlugin.Tests;
public class MyPluginTests
{
    private static TickContext Tick(TickData tick, FakePluginServices services)
        => TickContext.ForTesting(tick, services);
    [Fact]
    public async Task Emits_once_per_change()
    {
        var plugin = new MyTrackerPlugin();
        var services = new FakePluginServices();
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "3/8").Build(), services), default);
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "3/8").Build(), services), default);
        await plugin.OnTickAsync(Tick(new TickDataBuilder().Text("counter", "4/8").Build(), services), default);
        Assert.Equal(["3/8", "4/8"], services.Emitted.Select(r => r.RawText));
    }
}
```
Replay parity tests spawn a real `GameCapture.Engine.exe`, replay a PNG corpus,
and drive the plugin through its real host path. Put captured PNGs under
`tests/fixtures/corpus/<name>/`, link them into the test output from the test
project, and point `GAMECAPTURE_ENGINE_PATH` at the engine binary:
```powershell
$env:GAMECAPTURE_ENGINE_PATH = "C:\tools\gamecapture\GameCapture.Engine.exe"
dotnet test GameCapturePlugins.slnx --filter "Category=Integration"
```
Replay tests that do not yet have a real corpus should be explicitly skipped.
When CI is green, still check the skip count; a skipped parity test did not prove
the plugin against the engine.
## Build And Run
From this repo root:
```powershell
dotnet restore GameCapturePlugins.slnx
dotnet build GameCapturePlugins.slnx -c Release
dotnet test GameCapturePlugins.slnx -c Release
```
On a machine without the Windows OCR language pack, filter out replay parity:
```powershell
dotnet test GameCapturePlugins.slnx -c Release --filter "Category!=Integration"
```
To run a plugin live, start an engine first. Use the released engine named by
`engine-version.txt`, or a side-by-side engine clone only when deliberately testing
an unreleased engine change:
```powershell
gh release download v1.1.0 -R PetitCastor/gamecapture-engine -p "GameCapture.Engine-*-win-x64.zip"
dotnet run --project src/MyPlugin -- --verbose
```
The plugin waits on `config.json`'s `pipeName`. If it waits forever, the plugin
and engine are using different pipe names.
## Engine Version
`engine-version.txt` names the released engine this repo's CI downloads for
replay parity. Bump it in its own small PR after the engine release exists, then
let CI prove the existing plugins still pass against that binary.
The package references intentionally use `1.*`. The plugins consume the published
SDK packages, while replay parity pins the real engine executable. Do not solve a
plugins build by wiring in the local engine source tree.
## Existing Plugin Notes
- `MissionPlugin` has its parity test skipped until
  `tests/fixtures/corpus/mission-accept/` contains a real mission acceptance
  corpus.
- `RefineryPlugin` has live replay corpora under
  `tests/fixtures/corpus/refinery-confirm/` and
  `tests/fixtures/corpus/refinery-ice-rename/`.
- `SignaturePlugin` has a plugin-specific README at
  [`src/SignaturePlugin/README.md`](src/SignaturePlugin/README.md) covering its ROI
  calibration, manifest format, output sink, and skipped corpus gate.