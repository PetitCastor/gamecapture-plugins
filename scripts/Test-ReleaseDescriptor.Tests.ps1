# Fixture-driven checks for scripts/ReleaseDescriptor.Validate.ps1's gating rules. No Pester
# dependency (the repo pins none for PowerShell): plain pass/throw assertions, run directly with
# `powershell -File scripts/Test-ReleaseDescriptor.Tests.ps1`.

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ReleaseDescriptor.Validate.ps1"

$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Valid {
    param([string]$Name, [hashtable]$Manifest, [string]$Tag)
    try {
        Test-ReleaseDescriptorManifest -Manifest ([pscustomobject]$Manifest) -Tag $Tag
    }
    catch {
        $failures.Add("$Name : expected valid, but got error: $($_.Exception.Message)")
    }
}

function Assert-Invalid {
    param([string]$Name, [hashtable]$Manifest, [string]$Tag)
    try {
        Test-ReleaseDescriptorManifest -Manifest ([pscustomobject]$Manifest) -Tag $Tag
        $failures.Add("$Name : expected an error, but validation passed.")
    }
    catch {
        # Expected.
    }
}

Assert-Valid 'stable tag with one plugin' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'release notes'; plugins = @('SignaturePlugin') }

Assert-Valid 'stable tag with multiple plugins' `
    -Tag 'v2.0.0' `
    -Manifest @{ tag = 'v2.0.0'; channel = 'stable'; notes = 'release notes'; plugins = @('MissionPlugin', 'RefineryPlugin') }

Assert-Valid 'preview tag matching plugin and stage' `
    -Tag 'v1.2.4-mission-alpha.1' `
    -Manifest @{ tag = 'v1.2.4-mission-alpha.1'; channel = 'preview'; notes = 'preview notes'; plugins = @('MissionPlugin') }

Assert-Invalid 'tag mismatch between descriptor and pushed tag' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v9.9.9'; channel = 'stable'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'unknown channel' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'nightly'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'empty notes' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = '  '; plugins = @('SignaturePlugin') }

Assert-Invalid 'no plugins selected' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'n'; plugins = @() }

Assert-Invalid 'duplicate plugin in selection' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'n'; plugins = @('SignaturePlugin', 'SignaturePlugin') }

Assert-Invalid 'unknown plugin name' `
    -Tag 'v1.2.3' `
    -Manifest @{ tag = 'v1.2.3'; channel = 'stable'; notes = 'n'; plugins = @('NotAPlugin') }

Assert-Invalid 'stable tag with a prerelease suffix' `
    -Tag 'v1.2.3-rc.1' `
    -Manifest @{ tag = 'v1.2.3-rc.1'; channel = 'stable'; notes = 'n'; plugins = @('SignaturePlugin') }

Assert-Invalid 'preview release selecting more than one plugin' `
    -Tag 'v1.2.4-mission-alpha.1' `
    -Manifest @{ tag = 'v1.2.4-mission-alpha.1'; channel = 'preview'; notes = 'n'; plugins = @('MissionPlugin', 'RefineryPlugin') }

Assert-Invalid 'preview tag naming the wrong plugin' `
    -Tag 'v1.2.4-refinery-alpha.1' `
    -Manifest @{ tag = 'v1.2.4-refinery-alpha.1'; channel = 'preview'; notes = 'n'; plugins = @('MissionPlugin') }

Assert-Invalid 'preview tag with an unrecognized stage' `
    -Tag 'v1.2.4-mission-nightly.1' `
    -Manifest @{ tag = 'v1.2.4-mission-nightly.1'; channel = 'preview'; notes = 'n'; plugins = @('MissionPlugin') }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    throw "$($failures.Count) release-descriptor validation check(s) failed."
}

Write-Host 'All release-descriptor validation checks passed.'
