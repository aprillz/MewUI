<#
.SYNOPSIS
    Pushes a prepared release and tags it.

.DESCRIPTION
    Re-checks what Prepare-Release.ps1 produced, then pushes main before creating and pushing the tag.
    The order matters: pushing a tag carries its objects but leaves origin/main behind, which is how a
    release ends up ahead of the branch it claims to come from.

.PARAMETER Version
    Version to publish, without the leading v. Must match the version already in the repository.

.EXAMPLE
    ./tools/release/Publish-Release.ps1 -Version 0.20.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$tag = "v$Version"

Write-Step "Checking the repository"

Assert-ReleasableWorktree -Tag $tag
Invoke-Git fetch origin main --quiet | Out-Null

# main may be ahead of origin here, which is the whole point, but it must not be behind or the push
# would need a merge that nobody reviewed.
$behind = (Invoke-Git rev-list --count HEAD..origin/main).Trim()
if ($behind -ne '0') {
    throw "main is $behind commits behind origin/main. Pull and prepare again."
}

$ahead = (Invoke-Git rev-list --count origin/main..HEAD).Trim()
Write-Host "  main is $ahead commits ahead of origin/main"

Write-Step "Checking what was prepared"

$declared = Get-DeclaredVersion
if ($declared -ne $Version) {
    throw "MewUIVersion is $declared but $Version is being published. Run Prepare-Release.ps1 first."
}
Write-Host "  MewUIVersion is $Version"

if (-not (Test-FbaGalleryCurrent)) {
    throw "samples/FBASample/fba_gallery.cs is not what the gallery generates. Run Prepare-Release.ps1 again."
}
Write-Host "  the file-based gallery sample matches the gallery"

# Not about whether the sizes were re-measured: this only asks whether the committed SVGs still match
# the data they were drawn from, which a release should never carry broken.
& (Join-Path $script:SizeToolDir 'Update-ReleaseSizeAssets.ps1') -Check
Write-Host "  the release size chart matches its data"

Write-Step "Pushing"

Invoke-Git push origin main | Out-Null
Write-Host "  pushed main"
Invoke-Git tag $tag | Out-Null
Invoke-Git push origin $tag | Out-Null
Write-Host "  pushed $tag"

Write-Host ""
Write-Host "Published $tag. The Release workflow runs on it." -ForegroundColor Green
