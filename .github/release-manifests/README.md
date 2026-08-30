# Release descriptors

Each public plugin release has one committed descriptor named `<tag>.json`. Create it in the
release PR, merge the PR, then create the matching tag from the merged commit. The release workflow
refuses to run without this file or when its tag/channel/plugin selection does not match the ref.

```json
{
  "tag": "v1.0.17-mission-alpha.1",
  "channel": "preview",
  "plugins": ["MissionPlugin"],
  "notes": "MissionPlugin alpha preview. Known issue: replay corpus coverage is incomplete."
}
```

Stable tags use `v<major>.<minor>.<patch>`. Preview tags use
`v<major>.<minor>.<patch>-<plugin>-<alpha|beta|rc>.<n>` and select exactly one plugin.
