<#
    Shared helpers for the two release steps. Dot-sourced, not run on its own.
#>

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:PropsPath = Join-Path $script:RepoRoot 'build\MewUI.Common.props'
$script:SizeToolDir = Join-Path $script:RepoRoot 'tools\aot-size'
$script:FbaSyncProject = Join-Path $script:RepoRoot 'tools\fba-sync\FbaSync.csproj'
$script:FbaGalleryPath = Join-Path $script:RepoRoot 'samples\FBASample\fba_gallery.cs'

function Write-Step {
    param([string] $Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)
    $output = & git -C $script:RepoRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $output"
    }
    return $output
}

function Get-NumericVersion {
    param([string] $Text)
    $core = $Text.Split('-')[0]
    $parsed = $null
    if (-not [version]::TryParse($core, [ref] $parsed)) {
        throw "'$Text' is not a version this script can compare."
    }
    return $parsed
}

function Get-DeclaredVersion {
    $props = [xml](Get-Content -LiteralPath $script:PropsPath -Raw)
    $node = $props.SelectSingleNode('/Project/PropertyGroup/MewUIVersion')
    if ($null -eq $node) {
        throw "MewUIVersion is missing from $script:PropsPath."
    }
    return $node.InnerText.Trim()
}

function Set-DeclaredVersion {
    param([string] $Version)
    $props = [xml](Get-Content -LiteralPath $script:PropsPath -Raw)
    $node = $props.SelectSingleNode('/Project/PropertyGroup/MewUIVersion')
    $node.InnerText = $Version
    $props.Save($script:PropsPath)
}

<#
    The checks both steps share. A release is cut from main with nothing uncommitted, and the tag must
    not exist yet.
#>
function Assert-ReleasableWorktree {
    param([string] $Tag)

    $branch = (Invoke-Git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'main') {
        throw "Releases are cut from main; this worktree is on $branch."
    }

    $dirty = Invoke-Git status --porcelain --untracked-files=no
    if ($dirty) {
        throw "The working tree has uncommitted changes:`n$dirty"
    }

    $existing = & git -C $script:RepoRoot tag --list $Tag
    if ($existing) {
        throw "$Tag already exists. Delete it or pick another version."
    }
}

<#
    Rebuilds the file-based gallery sample into a scratch file and reports whether the committed one
    still matches, which is how the publish step tells that nothing drifted after preparation.
#>
function Test-FbaGalleryCurrent {
    $scratch = Join-Path ([IO.Path]::GetTempPath()) "fba_gallery_$([Guid]::NewGuid().ToString('N')).cs"
    try {
        & dotnet run --project $script:FbaSyncProject -c Release -- $scratch | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "fba-sync failed."
        }
        $generated = Get-Content -LiteralPath $scratch -Raw
        $committed = Get-Content -LiteralPath $script:FbaGalleryPath -Raw
        return $generated -eq $committed
    } finally {
        Remove-Item -LiteralPath $scratch -ErrorAction SilentlyContinue
    }
}
