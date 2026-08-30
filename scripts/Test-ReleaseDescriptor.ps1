param(
    [Parameter(Mandatory)]
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
$manifestPath = ".github/release-manifests/$Tag.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "A release descriptor is required at $manifestPath."
}

try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
}
catch {
    throw "Release descriptor is not valid JSON: $manifestPath"
}

if ($manifest.tag -cne $Tag) { throw "Descriptor tag '$($manifest.tag)' does not match '$Tag'." }
if ($manifest.channel -notin @('stable', 'preview')) { throw "channel must be 'stable' or 'preview'." }
if ([string]::IsNullOrWhiteSpace($manifest.notes)) { throw 'notes must be non-empty.' }

$allowedPlugins = @('MissionPlugin', 'RefineryPlugin', 'SignaturePlugin')
$plugins = @($manifest.plugins)
if ($plugins.Count -eq 0) { throw 'plugins must contain at least one plugin.' }
if (($plugins | Select-Object -Unique).Count -ne $plugins.Count) { throw 'plugins must not contain duplicates.' }
if (($plugins | Where-Object { $_ -notin $allowedPlugins }).Count -gt 0) { throw 'plugins contains an unknown plugin.' }

if ($manifest.channel -eq 'stable') {
    if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Stable tags must be v<major>.<minor>.<patch>.' }
}
else {
    if ($plugins.Count -ne 1) { throw 'A preview release must select exactly one plugin.' }
    $shortNames = @{ MissionPlugin = 'mission'; RefineryPlugin = 'refinery'; SignaturePlugin = 'signature' }
    $expected = '^v\d+\.\d+\.\d+-' + $shortNames[$plugins[0]] + '-(alpha|beta|rc)\.\d+$'
    if ($Tag -notmatch $expected) { throw "Preview tag '$Tag' must identify the selected plugin and stage." }
}
