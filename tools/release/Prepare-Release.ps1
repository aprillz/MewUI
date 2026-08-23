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
    Skips everything about size: the release numbers, the charts, and the NativeAOT probes. The probes
    publish four self-contained apps and take several minutes, and a patch release that does not
    re-measure keeps the previous release's numbers on purpose.

.NOTES
    The release numbers are re-measured only when %LOCALAPPDATA%\MewUI\release.json names the machines
    to measure Linux and macOS on. See Get-ReleaseSettings in ReleaseCommon.ps1 for its shape.

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
    # The release numbers are re-measured only where the machines to measure on are known, since Linux
    # and macOS are reached from here. Without that file the recorded numbers stand, which is what a
    # release that does not re-measure wants, and only the charts having drifted from them is checked.
    $settings = Get-ReleaseSettings
    if ($null -eq $settings) {
        Write-Step "Checking the release size charts"
        Write-Host "  no $script:SettingsPath, so the recorded numbers stand"
        & (Join-Path $script:SizeToolDir 'Update-ReleaseSizeAssets.ps1') -Check
    } else {
        Write-Step "Measuring the release sizes"
        & (Join-Path $script:SizeToolDir 'Update-ReleaseSizes.ps1') @settings
    }

    # Measured against the committed baseline, which this never writes.
    Write-Step "Measuring the NativeAOT probes"
    & (Join-Path $script:SizeToolDir 'Measure-AotSize.ps1') `
        -BaselinePath (Join-Path $script:SizeToolDir 'baselines\win-x64-gdi.json')
}

Write-Step "Committing what the tag has to contain"

if (Invoke-Git status --porcelain --untracked-files=no) {
    # -u stages what actually changed among tracked files and nothing else. Naming a path outright
    # would also fail on build/MewUI.Common.props, which sits under the rule that ignores build output.
    Invoke-Git add -u -- @script:ReleasePaths | Out-Null
    if (Invoke-Git diff --cached --name-only) {
        Invoke-Git commit -m "Bump MewUIVersion to $Version" | Out-Null
        Write-Host "  committed $((Invoke-Git rev-parse --short HEAD).Trim())"
    }
    # Left where they are rather than refused: nothing uncommitted reaches the tag, so an unrelated
    # edit is the releaser's business. Listed so a generated file that landed outside the release
    # paths is still seen.
    $leftover = Invoke-Git status --porcelain --untracked-files=no
    if ($leftover) {
        Write-Host "  left uncommitted outside the release paths:"
        foreach ($line in $leftover) {
            Write-Host "    $line"
        }
    }
} else {
    Write-Host "  nothing changed"
}

# The half of the version check that preparing owns: the commit a release will tag has to declare the
# requested version, so publishing has something committed to verify rather than a file on disk.
$committed = Get-CommittedVersion
if ($committed -ne $Version) {
    throw "HEAD declares MewUIVersion $committed, not $Version. The bump was not committed."
}
Write-Host "  HEAD declares MewUIVersion $Version"

Write-Host ""
Write-Host "Prepared $tag locally. Review it, then run:" -ForegroundColor Green
Write-Host "  ./tools/release/Publish-Release.ps1 -Version $Version"
