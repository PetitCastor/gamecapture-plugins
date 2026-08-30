# Releasing plugins

`SignaturePlugin` is the stable plugin channel. `MissionPlugin` and `RefineryPlugin` are published
only as public preview releases until they meet the stable bar.

## Prepare and publish a release

1. Add `.github/release-manifests/<tag>.json` in the release PR. Its tag, channel, selected plugins,
   and non-empty release notes are validated by CI when the tag is pushed.
2. Merge the PR into `master`.
3. Create the descriptor's exact tag on the merged commit. Tags, not pull-request merges, create
   GitHub releases. Stable tags are `v1.2.3`; preview tags are
   `v1.2.4-mission-alpha.1`, `v1.2.4-refinery-beta.2`, or the equivalent Signature form.
4. After a successful **stable** release, send a follow-up catalog PR that changes only that
   plugin's versioned URL in `plugins.json`. Do not use `releases/latest`: it is repository-wide,
   not plugin-wide. Preview releases belong in `plugins.preview.json` only after an engine version
   that supports the opt-in preview catalog has shipped.

## Preview testers

Preview builds are public GitHub prereleases, unsigned, opt-in, and may change incompatibly. Testers
download the exact release asset from the GitHub prerelease page and should report the full release
tag, pinned `engine-version.txt` value, and reproduction details. Plugin previews are executable
release assets; they do not require prerelease NuGet SDK packages.
