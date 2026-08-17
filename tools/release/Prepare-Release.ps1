<#
.SYNOPSIS
    Prepares a release on main and leaves the commit for review.

.DESCRIPTION
    Sets the version, regenerates what a release has to carry, and checks the NativeAOT size against
    its baseline. It commits but does not push or tag, so the result can be read before it leaves the
    machine, and undone with a reset if it should not. Publish-Release.ps1 takes it from there.

    The regression baselines under tools/aot-size/baselines are never written here. A baseline change
    needs measurements and review, which is not something a release can decide.

.PARAMETER Version
    Version to release, without the leading v.

.PARAMETER SkipSizeChecks
    Skips both size checks: the NativeAOT probe measurement and the release size chart check. The
    probes publish four self-contained apps and take several minutes, and a patch release that does
    not re-measure keeps the previous release's numbers on purpose.

.EXAMPLE
    ./tools/release/Prepare-Release.ps1 -Version 0.20.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [switch] $SkipSizeChecks
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$tag = "v$Version"

Write-Step "Checking the repository"

Assert-ReleasableWorktree -Tag $tag

# origin has to be reachable and level with main before anything is generated: preparing on a branch
# that is behind produces a commit that cannot be pushed without a merge.
Invoke-Git fetch origin main --quiet | Out-Null
$local = (Invoke-Git rev-parse HEAD).Trim()
$remote = (Invoke-Git rev-parse origin/main).Trim()
if ($local -ne $remote) {
    $ahead = (Invoke-Git rev-list --count origin/main..HEAD).Trim()
    $behind = (Invoke-Git rev-list --count HEAD..origin/main).Trim()
    throw "main and origin/main differ ($ahead ahead, $behind behind). Push or pull before preparing."
}

Write-Host "  main is $local and matches origin/main"

Write-Step "Checking the version"

$declared = Get-DeclaredVersion
if ((Get-NumericVersion $declared) -gt (Get-NumericVersion $Version)) {
    throw "MewUIVersion is $declared, which is newer than the requested $Version."
}

if ($declared -ne $Version) {
    Set-DeclaredVersion -Version $Version
    Write-Host "  MewUIVersion $declared -> $Version"
} else {
    Write-Host "  MewUIVersion is already $Version"
}

Write-Step "Generating the file-based gallery sample"

& dotnet run --project $script:FbaSyncProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "fba-sync failed."
}

if ($SkipSizeChecks) {
    Write-Step "Skipping the size checks"
} else {
    # The chart is checked, not rewritten: release-sizes.json carries the version its numbers were
    # measured for, so a release that does not re-measure keeps the previous one on purpose. Only the
    # SVGs having drifted from that data is a problem. The probes are then measured against the
    # committed baseline, which this never writes.
    Write-Step "Checking the sizes"
    & (Join-Path $script:SizeToolDir 'Update-ReleaseSizeAssets.ps1') -Check
    & (Join-Path $script:SizeToolDir 'Measure-AotSize.ps1') `
        -BaselinePath (Join-Path $script:SizeToolDir 'baselines\win-x64-gdi.json')
}

Write-Step "Committing what the tag has to contain"

if (Invoke-Git status --porcelain --untracked-files=no) {
    Invoke-Git add build/MewUI.Common.props samples/FBASample | Out-Null
    if (Invoke-Git diff --cached --name-only) {
        Invoke-Git commit -m "Bump MewUIVersion to $Version" | Out-Null
        Write-Host "  committed $((Invoke-Git rev-parse --short HEAD).Trim())"
    }
    $leftover = Invoke-Git status --porcelain --untracked-files=no
    if ($leftover) {
        throw "Generated changes outside the release paths remain:`n$leftover"
    }
} else {
    Write-Host "  nothing changed"
}

Write-Host ""
Write-Host "Prepared $tag locally. Review it, then run:" -ForegroundColor Green
Write-Host "  ./tools/release/Publish-Release.ps1 -Version $Version"
