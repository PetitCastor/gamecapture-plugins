# Dot-sourced by Test-ReleaseDescriptor.ps1 (the CI entry point), release.yml's "Publish selected
# plugins" step, and Test-ReleaseDescriptor.Tests.ps1 (fixture-driven checks with no file I/O) — so
# the plugin allowlist and the validation rules built on it are pinned in exactly one place.

# Every entry here is also a `src/<name>` project directory by convention; release.yml relies on
# that to build its publish path map without hand-duplicating this list.
$script:AllowedPlugins = @('MissionPlugin', 'RefineryPlugin', 'SignaturePlugin')

function Test-ReleaseDescriptorManifest {
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)] [string]$Tag
    )

    if ($Manifest.tag -cne $Tag) { throw "Descriptor tag '$($Manifest.tag)' does not match '$Tag'." }
    if ($Manifest.channel -notin @('stable', 'preview')) { throw "channel must be 'stable' or 'preview'." }
    if ([string]::IsNullOrWhiteSpace($Manifest.notes)) { throw 'notes must be non-empty.' }

    $plugins = @($Manifest.plugins)
    if ($plugins.Count -eq 0) { throw 'plugins must contain at least one plugin.' }
    if (($plugins | Select-Object -Unique).Count -ne $plugins.Count) { throw 'plugins must not contain duplicates.' }
    if (($plugins | Where-Object { $_ -notin $script:AllowedPlugins }).Count -gt 0) { throw 'plugins contains an unknown plugin.' }

    if ($Manifest.channel -eq 'stable') {
        if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Stable tags must be v<major>.<minor>.<patch>.' }
    }
    else {
        if ($plugins.Count -ne 1) { throw 'A preview release must select exactly one plugin.' }
        $shortNames = @{ MissionPlugin = 'mission'; RefineryPlugin = 'refinery'; SignaturePlugin = 'signature' }
        $expected = '^v\d+\.\d+\.\d+-' + $shortNames[$plugins[0]] + '-(alpha|beta|rc)\.\d+$'
        if ($Tag -notmatch $expected) { throw "Preview tag '$Tag' must identify the selected plugin and stage." }
    }
}
