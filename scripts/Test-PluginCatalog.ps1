$ErrorActionPreference = 'Stop'

# Validates plugins.json and plugins.preview.json against the shape the engine's PluginCatalog
# actually enforces (see gamecapture-engine's PluginCatalog.TryParse / CatalogEntry). A catalog PR
# is hand-edited and never runs the engine's own tests, so this is the only gate standing between a
# malformed entry and every user's plugin manager.
#
# The engine treats a preview catalog entry with no (or a wrong) "channel" value as belonging to the
# wrong channel and rejects the WHOLE preview catalog fetch — so plugins.preview.json entries MUST
# carry "channel": "preview" explicitly. plugins.json entries must omit "channel" or say "stable".

function Test-CatalogFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$ExpectedChannel
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Path is missing."
    }

    try {
        $parsed = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "$Path is not valid JSON."
    }

    # ConvertFrom-Json turns a JSON "[]" into $null, not an empty array — @($null) would wrap that
    # single $null into a one-element array and fail every field check below.
    $entries = if ($null -eq $parsed) { @() } else { @($parsed) }

    $ids = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($entry in $entries) {
        foreach ($field in @('id', 'name', 'clientName', 'description', 'downloadUrl')) {
            if ([string]::IsNullOrWhiteSpace($entry.$field)) {
                throw "$($Path): an entry is missing required field '$field'."
            }
        }

        if ($entry.id -notmatch '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$' -or $entry.id.Length -gt 64) {
            throw "$($Path): id '$($entry.id)' is not a valid kebab-case slug."
        }

        if (-not $ids.Add($entry.id)) {
            throw "$($Path): duplicate id '$($entry.id)'."
        }

        $channel = if ($null -eq $entry.channel) { 'stable' } else { $entry.channel }
        if ($channel -ne $ExpectedChannel) {
            throw "$($Path): entry '$($entry.id)' has channel '$channel', expected '$ExpectedChannel'. " +
                  "A preview entry must set `"channel`": `"preview`" explicitly, or the engine rejects the whole preview catalog."
        }

        if ($ExpectedChannel -eq 'preview' -and $entry.downloadUrl -match '/releases/latest/download/') {
            throw "$($Path): entry '$($entry.id)' uses the mutable 'latest' URL; previews have no latest alias and must pin an exact release tag."
        }
    }

    return $ids
}

$stableIds = Test-CatalogFile -Path 'plugins.json' -ExpectedChannel 'stable'
$previewIds = Test-CatalogFile -Path 'plugins.preview.json' -ExpectedChannel 'preview'

$collisions = $previewIds | Where-Object { $stableIds.Contains($_) }
if ($collisions) {
    throw "plugins.preview.json duplicates stable id(s): $($collisions -join ', '). A promoted plugin must be removed from the preview catalog in the same PR."
}

Write-Host "plugins.json ($($stableIds.Count) entries) and plugins.preview.json ($($previewIds.Count) entries) are valid."
