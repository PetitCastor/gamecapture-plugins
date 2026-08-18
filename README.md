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

## Known-carried debt: Mission parity is skipped

`MissionPlugin.Tests`'s `ReplayParityTests` is `Skip`-attributed pending an in-game corpus capture
under `tests/fixtures/corpus/mission-accept/` — this is not a regression from the split, it was
already skipped in the mono-repo this was extracted from (TASK-13's **[USER ACTION]**). RefineryPlugin's
two parity corpora (`refinery-confirm`, `refinery-ice-rename`) run for real, in this repo and in CI.
A green CI run here therefore does not mean both plugins' parity suites executed — check the skip
count, not just the exit code. Landing the mission-accept corpus unskips the test with no further
code or csproj change (see the `<None Include>` glob in `MissionPlugin.Tests.csproj`).

## Compatible engine version

[`engine-version.txt`](engine-version.txt) names the `gamecapture-engine` release this repo's CI and
release pipeline test and package against. Bumping it is a deliberate manual one-line PR, not
something Dependabot opens — see [`.github/dependabot.yml`](.github/dependabot.yml).
