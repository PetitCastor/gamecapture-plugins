# SignaturePlugin

A [OCRX](https://ocrx.org) plugin: a console process that
declares screen regions (ROIs) and what to do with the OCR result each time a tick carrying them
arrives. It never captures a frame, never runs OCR, and never speaks gRPC; `OcrxPluginHost`
(from `Ocrx.Sdk`) owns connecting, subscribing, reconnecting, and shutdown.

## Calibrate The Signature ROI

> **Calibration status — `Rois.Counter` uses scale 6.0.** The checked-in corpus currently contains
> one four-digit reference frame, so it is not replay proof for five- or six-digit readings. Capture
> representative wide-number frames with `--save-frames` before changing the ROI or trusting a new
> game patch; the parser and overlay fail safely on partial OCR, but that is not a substitute for
> calibration.

`SignaturePlugin.cs` subscribes to `Rois.Counter`, the mining-mode RS signature number. Before
trusting replay parity, calibrate that rectangle against your own capture:

1. Get an engine running with `--save-frames`, either a
   [release zip](https://github.com/PetitCastor/ocrx-releases/releases)
   (`Ocrx.Engine-vX.Y.Z-win-x64.zip`) or a clone of the engine repo built locally.
2. Set `"saveDebugFrames": true` in `%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json`. The engine saves full replay frames;
   `saveDebugFrames` saves the cropped ROI dumps used to tune the rectangle.
3. Run the plugin with `--verbose` against the running engine. In game, open scan mode with a
   known ore, asteroid, or debris signature visible.
4. Press the engine's capture hotkey (`engine-config.json`'s `hotkey`, default `Ctrl+Shift+F12`).
5. Compare the dumped PNG path printed by `--verbose` against `Rois.Counter`'s rectangle. It is
   declared in reference space, 2560x1440, always. Nudge `RoiRect(x, y, width, height)` and `Scale`
   until the crop lands on the number. This target currently uses a `Scale` of 6.0; do not lower it
   without comparing representative captures.
6. Keep `saveDebugFrames` enabled until manual ticks reliably dump only the signature number.

Full walkthrough: ROI kinds, scale, error handling, session events, testing, config, and CLI are in
the hosted plugin-authoring guide:
[`docs/PLUGIN-AUTHORING.md`](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/PLUGIN-AUTHORING.md).

## Replay Corpus

`tests/SignaturePlugin.Tests/ReplayParityTests.cs` contains the carried integration gate for this
plugin. It runs against the `scan-signature` corpus below; a plugin with no corpus yet should keep
this test `Skip`-attributed rather than deleting it.

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
    { "file": "0001-bexalite.png", "name": "Bexalite", "kind": "ore", "signature": 3600, "count": 1 }
  ]
}
```

`signature` and `count` are optional: add them when you know the exact reading a frame should
produce (from the source table, not a guess) so the test also pins the emitted `signature`/`count`
fields, not just `name`/`kind` — those two alone let a misread that lands on a different cluster
count of the *same* ore (e.g. a doubled signature) pass unnoticed, since matching searches unit
signature x count 1-6. Omit them for a hand-labelled frame where only the ore name is known.

The test project links those files to `Fixtures/Replay/scan-signature/` in the test output. Point
`OCRX_ENGINE_PATH` at a built or unpacked `Ocrx.Engine.exe` and run the `Integration`
test. It loads the embedded default table, replays each manifest-labelled PNG as its own one-frame
corpus, and asserts the emitted observation against the manifest. It also checks that every copied
PNG has exactly one manifest entry.

On first launch, the plugin creates a user-editable default table at
`%LOCALAPPDATA%\OCRX\SignaturePlugin\signature-table.json`. It preserves that file on
later launches, so edit it when captured measurements disagree with the shipped defaults. Update
`Resources/signature-table.json` in the same change only when the new values should become the
default for future users; note the source or patch context in the PR body.

## Output

The default config created at `%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json` appends every
signature observation to `captures/signatures.jsonl`, beside that config file. Each observation line is one OCRX
record whose `rawText` is the structured signature JSON emitted by this plugin. Repeated unchanged
observations are de-duplicated; the cleared record is retained as `kind: "Cleared"` with empty
`rawText` so a consumer can remove stale state.

It also configures an `overlay` sink, so a matched signature is drawn on screen as `{cluster}` —
`Ice x4` — and hidden again when the signature clears. `Program.cs` registers `OverlaySinkFactory`
through `PluginHostOptions.OverlayFactory`, which is what makes that entry live: an `overlay` output
with no factory registered is silently a no-op. The overlay draws only from `CaptureRecord.Fields`,
so a template placeholder that is not one of `cluster`, `alternate`, `name`, `kind`, `signature`,
`count`, or `delta` falls back to printing the raw JSON.

`{cluster}` rather than `{name} x{count}` because two table entries can derive the same cluster
total: 19200 is Savrilium x6 and Aslarite x5 alike, and nothing in the number tells them apart. Such
a reading used to resolve to nothing at all, which the plugin then counted as the badge having
vanished — so scanning one of those rocks actively hid the overlay. It now renders as
`Savrilium x6 / Aslarite x5`, and `{alternate}` carries the runner-up on its own (empty string when
the reading is unambiguous). `name` and `count` still hold the primary candidate alone, so existing
JSONL consumers are unaffected — but a template built from those two cannot express the tie, and
printing only the winner of it would be a confident wrong answer.

No data leaves the machine: the JSON sink is a local file and the overlay is a local window.
Change the `outputs` array in that local `config.json` to disable either (`[]`) or choose another supported sink;
the [plugin-authoring guide's output section](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/PLUGIN-AUTHORING.md#outputs-sinks)
lists the JSON, CSV, HTTP, and optional overlay configurations.

A config file written before the overlay existed does not have that entry, and earlier versions
only ever wrote `config.json` when it was missing — so upgrading left the overlay silently absent.
It is now added on the next run, once. Whatever you do to it afterwards stands: delete the entry, or
empty `outputs` entirely, and it stays that way through later upgrades.

Matched observations show the configured template and `EmitCleared` hides stale text after the
signature disappears. The overlay is deliberately hard to dislodge, because OCR on this crop is
fragile and every wobble used to read as "the badge is gone":

- **Only a blank crop is evidence of absence.** A legible number that resolves to no ore cluster
  means the badge is still drawn — one misread digit is enough to land in a gap in the table's
  derived grid, and that must not hide anything. Six consecutive *blank* ticks (3 s at the default
  500 ms scan interval) are required before the overlay is cleared.
- **A partial read is rejected, not truncated.** `SignatureParser` refuses any token that leaves
  digits behind, so the captured misread `21/425` fails outright instead of yielding `21`. It also
  folds `/` to `,`, which is how Windows OCR renders this HUD font's thousands separator about half
  the time. Returning a prefix was worse than returning nothing: `21` parses, matches no cluster,
  and was then defended as if it were a real reading.
- **A value that matches nothing is never defended.** The consensus only protects a reading that is
  actually on screen. Without that rule an accepted-but-unmatchable value blocks its own replacement,
  and because the misread producing it recurs, it kept resetting the true reading's confirmation
  streak — one captured run sat on a stale ore for sixteen seconds that way.
- **A changed value has to repeat before it is believed.** The first reading of a scan shows
  instantly, but replacing it takes two consecutive identical readings. Blank ticks are neutral and
  do not restart the challenger, because this crop reads blank frequently while the badge remains on
  screen. This stops a single slipped digit from renaming the ore mid-scan — 17200 is
  Ice x4 exactly, while 18200 is one 7→8 slip away and sits close to Bexalite x5.
- **Cluster matching uses an absolute tolerance,** not a percentage of the cluster total. A relative
  window widened exactly where the derived grid is densest, so six-ore clusters were the readings
  most likely to be named as the wrong ore.
- **A brief reconnect does not clear.** Only a sustained outage (roughly two seconds of failed
  dials) hides the overlay; a stream that drops and comes straight back says nothing about what is
  on screen.

As a backstop, the overlay cannot outlive its last real match by more than 20 ticks (~10 s),
whatever the crop is yielding — otherwise a rect that has drifted off the badge, or a table gone
stale against a game patch, would pin a dead value on screen indefinitely. The plugin also preserves
its clear across dropped ticks and a lost session, so a gap cannot leave an old signature stuck.

None of this can rescue a reading that is *steadily* wrong. The derived grid is dense — Corundum x3
is 12675 and Quantanium x4 is 12680, five apart — so a persistent misread can land exactly on a
neighbouring cluster with a delta of zero, indistinguishable from a correct reading. The debounce
and the consensus buy stability against wobble; accuracy against a stable misread would need a
better crop or a second ROI, not a tighter threshold.

Running with `--verbose` prints one line per tick tracing the whole chain the overlay depends on:
the raw OCR text, the number parsed from it, the value actually being acted on (`held=` appears
exactly on the ticks a change is being refused), and what the table made of it. That is the log to
capture when the overlay behaves oddly — the failures worth diagnosing are about how often an
unchanged reading is reported as something else, which is invisible in a log that only records
changes.

**Existing installs:** `ConfigSeed`'s merge adds missing *entries* but never changes a value you
already have, so both of the defaults below reach **new** installs only. If you ran this plugin
before, edit `%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json` by hand:

- `overlay.lingerMs` must be `0`. A non-zero linger auto-hides on a timer, and since an unchanged
  observation is emitted only once, nothing ever refreshes it — the overlay vanishes that many
  milliseconds after it appears and does not come back for that rock, whatever the debounce decides.
  This one silently defeats every other fix above, so check it first.
- `overlay.template` should be `{cluster}`. The older `{name} x{count}` still renders, but it prints
  only the primary candidate for an ambiguous total, so a 19200 rock shows `Savrilium x6` with no
  hint that `Aslarite x5` is equally likely.

## Build & Test

```powershell
dotnet build
dotnet test tests/SignaturePlugin.Tests/SignaturePlugin.Tests.csproj --filter "Category!=Integration"
```

The one test tagged `Integration` needs `OCRX_ENGINE_PATH` and a Windows OCR language pack;
run with `dotnet test tests/SignaturePlugin.Tests/SignaturePlugin.Tests.csproj` (no filter) once
both are available.

## Run

```powershell
dotnet run --project . -- --verbose
```

Needs `Ocrx.Engine.exe` running first, listening on the `OCRX.Engine` pipe configured in `%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json`
(`pipeName`, must match the engine's).
