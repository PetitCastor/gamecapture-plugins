param(
    [Parameter(Mandatory)]
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseDescriptor.Validate.ps1"

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

Test-ReleaseDescriptorManifest -Manifest $manifest -Tag $Tag
