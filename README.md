# gamecapture-plugins

[![CI](https://github.com/PetitCastor/gamecapture-plugins/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/PetitCastor/gamecapture-plugins/actions/workflows/ci.yml)
[![Release](https://github.com/PetitCastor/gamecapture-plugins/actions/workflows/release.yml/badge.svg)](https://github.com/PetitCastor/gamecapture-plugins/releases)

Star Citizen trackers built as pure [GameCapture](https://github.com/PetitCastor/gamecapture-engine)
SDK consumers: **MissionPlugin** (mission-board parsing) and **RefineryPlugin** (refinery order
tracking). Each is a separate process that owns nothing but its own parsing and state — see the
engine repo's README for the capture-engine/plugin split this repo is the plugin half of.

This repo does not build or run the engine itself. It references `GameCapture.Sdk` /
`GameCapture.Contracts` / `GameCapture.Sdk.Testing` from nuget.org, and CI pulls a released engine
binary (pinned in [`engine-version.txt`](engine-version.txt)) to run the two plugins' replay-parity
suites against.

## Layout

```
src/MissionPlugin/          RefineryPlugin/       — one process, one plugin, each
tests/MissionPlugin.Tests/  RefineryPlugin.Tests/ — unit tests + replay-parity tests
```

## Building

```powershell
dotnet restore GameCapturePlugins.slnx
dotnet build GameCapturePlugins.slnx -c Release
dotnet test GameCapturePlugins.slnx -c Release
```

Replay-parity tests (`ReplayParityTests` in each `.Tests` project) need
`GAMECAPTURE_ENGINE_PATH` pointed at a built or downloaded `GameCapture.Engine.exe` — see
[`GameCapture.Sdk.Testing`'s `EngineLocator`](https://github.com/PetitCastor/gamecapture-engine/blob/master/docs/REPLAY.md)
for how to build one locally, or download a release per `engine-version.txt` the way `ci.yml` does.

## Local dev loop

**Running a plugin against a live engine**, whether debugging a plugin change or capturing a new
corpus (see [engine repo `docs/REPLAY.md`](https://github.com/PetitCastor/gamecapture-engine/blob/master/docs/REPLAY.md#capturing-a-corpus-in-game)),
needs an engine process running first — this repo has no engine source, so you get one one of two ways:

- **From a release zip** (matches what CI/release actually run against): download and unzip the
  version named in `engine-version.txt`, e.g. `gh release download v1.0.0 -R PetitCastor/gamecapture-engine
  -p "GameCapture.Engine-*-win-x64.zip"`, then run the extracted `GameCapture.Engine.exe` directly.
- **From a side-by-side clone** (for testing against an unreleased engine change): clone
  `gamecapture-engine` next to this repo and `dotnet run --project src\GameCapture.Engine`. Not a
  git submodule — the two repos version independently (nuget.org packages vs. a pinned release tag),
  so a submodule pointer would fight that rather than help it. Just two sibling directories.

Either way, run the plugin in a second terminal/process — `dotnet run --project src\MissionPlugin`
or `src\RefineryPlugin` — pointed at the engine's named pipe the way `config.json` already expects.
There is no multi-startup-project `.sln` here to press one F5 for both: Visual Studio's "multiple
startup projects" only works within one solution, and the engine now lives in a different repo/solution
entirely. The VS equivalent of the two-terminal loop above is two VS instances (or one VS + one
terminal) — open `gamecapture-engine`'s solution in one, this repo's `GameCapturePlugins.slnx` in the
other, and F5 each independently.

**Testing an SDK-in-progress change** before it's published to nuget.org: pack the engine repo
locally (`dotnet pack GameCaptureEngine.slnx -c Release -o ../local-feed` from a `gamecapture-engine`
checkout) and point this repo at that folder instead of nuget.org —
`dotnet nuget add source ../local-feed --name local-sdk-feed` in a `nuget.config` here, then bump the
plugin `.csproj` `PackageReference` versions to whatever MinVer derived for the packed build. Revert
both before committing; this is a scratch loop, not something that ships (same scaffolding TASK-21's
rehearsal used, not a repo convention).

**Bumping the pinned engine version**: edit `engine-version.txt` to the new `gamecapture-engine` tag,
open it as its own one-line PR (deliberately manual, see "Compatible engine version" below), and let
CI's "Download pinned engine release" step confirm the new version's parity suites still pass before
merging.

## Mission parity is skipped — how to turn it on

`MissionPlugin.Tests`'s `ReplayParityTests` is `Skip`-attributed: there is no
`tests/fixtures/corpus/mission-accept/` corpus to replay. RefineryPlugin's two corpora
(`refinery-confirm`, `refinery-ice-rename`) run for real, here and in CI. This is inherited debt, not
a regression from the repo split — the mono-repo this was extracted from never captured the mission
corpus either.

**What it means for a green CI run:** only one of the two plugins' parity gates actually executed.
Read the skip count, not just the exit code.

Clearing it is an in-game capture, and no code or csproj change:

1. Run an engine against the live game with frame saving on — `GameCapture.Engine.exe --save-frames`
   from a release zip, or `dotnet run --project src\GameCapture.Engine -- --save-frames` from an
   engine clone.
2. Accept one mission in-game, pressing the engine's hotkey (`engine-config.json`'s `hotkey`,
   default `Ctrl+Shift+F12`, logged at startup) at each stage worth a frame — board open, mission
   selected, post-accept. Roughly 5-8 frames.
3. Copy the PNGs the engine wrote (its `outputDir`, `captures/` by default, printed as `Dumps:` on
   startup) into `tests/fixtures/corpus/mission-accept/`. No renaming — the timestamped names
   already sort in capture order, and `MissionPlugin.Tests.csproj`'s `<None Include>` glob copies
   the directory into the test output already.
4. Drop the `Skip = ...` from the `[Fact]` in
   [`tests/MissionPlugin.Tests/ReplayParityTests.cs`](tests/MissionPlugin.Tests/ReplayParityTests.cs)
   and run `dotnet test GameCapturePlugins.slnx` with `GAMECAPTURE_ENGINE_PATH` set.

The test asserts **exactly one** `Auto` record from plugin `missions` over the whole corpus. If a
capture yields more, the corpus spans more than one accept — recapture it rather than loosening the
assertion, which is what defines "parity" here.

## Compatible engine version

[`engine-version.txt`](engine-version.txt) names the `gamecapture-engine` release this repo's CI and
release pipeline test and package against. Bumping it is a deliberate manual one-line PR, not
something Dependabot opens — see [`.github/dependabot.yml`](.github/dependabot.yml).
