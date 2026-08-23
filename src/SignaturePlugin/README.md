# SignaturePlugin

A [GameCapture](https://github.com/PetitCastor/gamecapture-engine) plugin: a console process that
declares screen regions (ROIs) and what to do with the OCR result each time a tick carrying them
arrives. It never captures a frame, never runs OCR, and never speaks gRPC; `GameCapturePluginHost`
(from `GameCapture.Sdk`) owns connecting, subscribing, reconnecting, and shutdown.

## Calibrate The Signature ROI

`SignaturePlugin.cs` ships with one placeholder region (`Rois.Counter`) for the mining-mode RS
signature number. Before trusting replay parity, calibrate that rectangle against your own capture:

1. Get an engine running with `--save-frames`, either a
   [release zip](https://github.com/PetitCastor/gamecapture-engine/releases)
   (`GameCapture.Engine-vX.Y.Z-win-x64.zip`) or a clone of the engine repo built locally.
2. Set `"saveDebugFrames": true` in `%LOCALAPPDATA%\GameCapture\SignaturePlugin\config.json`. The engine saves full replay frames;
   `saveDebugFrames` saves the cropped ROI dumps used to tune the rectangle.
3. Run the plugin with `--verbose` against the running engine. In game, open scan mode with a
   known ore, asteroid, or debris signature visible.
4. Press the engine's capture hotkey (`engine-config.json`'s `hotkey`, default `Ctrl+Shift+F12`).
5. Compare the dumped PNG path printed by `--verbose` against `Rois.Counter`'s rectangle. It is
   declared in reference space, 2560x1440, always. Nudge `RoiRect(x, y, width, height)` and `Scale`
   until the crop lands on the number. Small UI text usually needs a `Scale` of 2-4.
6. Keep `saveDebugFrames` enabled until manual ticks reliably dump only the signature number.

Full walkthrough: ROI kinds, scale, error handling, session events, testing, config, and CLI are in
the hosted plugin-authoring guide:
[`docs/PLUGIN-AUTHORING.md`](https://github.com/PetitCastor/gamecapture-engine/blob/master/docs/PLUGIN-AUTHORING.md).

## Replay Corpus

`tests/SignaturePlugin.Tests/ReplayParityTests.cs` contains the carried integration gate for this
plugin. It is intentionally skipped until a real corpus exists.

Capture known frames with an active engine:

1. Calibrate `Rois.Counter` with `saveDebugFrames` as above.
2. Start the engine with `--save-frames` so each hotkey press writes a full-frame PNG replay source.
3. Leave the game in scan mode with only the RS signature number inside the ROI.
4. Press the engine hotkey once per known ore, asteroid, or debris signature. A screenshot that
   contains only the number inside the ROI is enough; the manifest supplies the label.
5. Copy the engine-saved PNGs into `tests/fixtures/corpus/scan-signature/`.
6. Add `tests/fixtures/corpus/scan-signature/manifest.json`:

```json
{
  "frames": [
    { "file": "0001-bexalite.png", "name": "Bexalite", "kind": "ore" }
  ]
}
```

The test project links those files to `Fixtures/Replay/scan-signature/` in the test output. Point
`GAMECAPTURE_ENGINE_PATH` at a built or unpacked `GameCapture.Engine.exe`, remove the `Skip`, and
run the `Integration` test. The test loads the embedded default table, replays each
manifest-labelled PNG as its own one-frame corpus, and asserts the emitted `{ name, kind }`
observation against the manifest. It also checks that every copied PNG has exactly one manifest
entry.

On first launch, the plugin creates a user-editable default table at
`%LOCALAPPDATA%\GameCapture\SignaturePlugin\signature-table.json`. It preserves that file on
later launches, so edit it when captured measurements disagree with the shipped defaults. Update
`Resources/signature-table.json` in the same change only when the new values should become the
default for future users; note the source or patch context in the PR body.

## Output

The default config created at `%LOCALAPPDATA%\GameCapture\SignaturePlugin\config.json` appends every
signature observation to `captures/signatures.jsonl`, beside that config file. Each observation line is one GameCapture
record whose `rawText` is the structured signature JSON emitted by this plugin. Repeated unchanged
observations are de-duplicated; the cleared record is retained as `kind: "Cleared"` with empty
`rawText` so a consumer can remove stale state.

It also configures an `overlay` sink, so a matched signature is drawn on screen as
`{name} x{count}` and hidden again when the signature clears. `Program.cs` registers
`OverlaySinkFactory` through `PluginHostOptions.OverlayFactory`, which is what makes that entry
live: an `overlay` output with no factory registered is silently a no-op. The overlay draws only
from `CaptureRecord.Fields`, so a template placeholder that is not one of `name`, `kind`,
`signature`, `count`, or `delta` falls back to printing the raw JSON.

No data leaves the machine: the JSON sink is a local file and the overlay is a local window.
Change the `outputs` array in that local `config.json` to disable either (`[]`) or choose another supported sink;
the [plugin-authoring guide's output section](https://github.com/PetitCastor/gamecapture-engine/blob/master/docs/PLUGIN-AUTHORING.md#outputs-sinks)
lists the JSON, CSV, HTTP, and optional overlay configurations.

Matched observations show the configured template and `EmitCleared` hides stale text after the
signature becomes unknown or disappears. The plugin also
preserves that clear across dropped ticks, so a gap cannot leave an old signature stuck on screen.

## Build & Test

```powershell
dotnet build
dotnet test tests/SignaturePlugin.Tests/SignaturePlugin.Tests.csproj --filter "Category!=Integration"
```

The one test tagged `Integration` is skipped until you have the `scan-signature` replay corpus and
manifest described above.

## Run

```powershell
dotnet run --project . -- --verbose
```

Needs a `GameCapture.Engine` running first, listening on the pipe name in `%LOCALAPPDATA%\GameCapture\SignaturePlugin\config.json`
(`pipeName`, must match the engine's).
