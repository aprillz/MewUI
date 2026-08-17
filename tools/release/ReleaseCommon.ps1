<#
    Shared helpers for the two release steps. Dot-sourced, not run on its own.
#>

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:SettingsPath = Join-Path $env:LOCALAPPDATA 'MewUI\release.json'

# What a release writes, and so what it stages. A change here is either about to be made again or was
# left by a run that stopped partway, which is why the worktree check ignores it.
$script:ReleasePaths = @(
    'build/MewUI.Common.props',
    'samples/FBASample',
    'tools/aot-size/release-sizes.json',
    'docs/assets')
$script:PropsPath = Join-Path $script:RepoRoot 'build\MewUI.Common.props'
$script:SizeToolDir = Join-Path $script:RepoRoot 'tools\aot-size'
$script:FbaSyncProject = Join-Path $script:RepoRoot 'tools\fba-sync\FbaSync.csproj'
$script:FbaGalleryPath = Join-Path $script:RepoRoot 'samples\FBASample\fba_gallery.cs'

function Write-Step {
    param([string] $Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

# git writes progress to stderr even when it succeeds. Windows PowerShell wraps a redirected native
# stderr line in an ErrorRecord, which a Stop preference turns into a failure, so the stream is left
# to reach the console on its own and only the exit code decides.
function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)
    $output = & git -C $script:RepoRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
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

# Edited as text rather than as XML: saving a parsed document rewrites the whole file, which drops the
# blank lines it is laid out with and turns its LF endings into CRLF.
function Set-DeclaredVersion {
    param([string] $Version)
    $text = [IO.File]::ReadAllText($script:PropsPath)
    $updated = $text -replace '<MewUIVersion>[^<]*</MewUIVersion>', "<MewUIVersion>$Version</MewUIVersion>"
    if ($updated -eq $text) {
        throw "MewUIVersion is missing from $script:PropsPath."
    }
    [IO.File]::WriteAllText($script:PropsPath, $updated, [Text.UTF8Encoding]::new($false))
}

<#
    What a release needs of this machine: main, and no tag of that name. Nothing here reaches origin,
    so preparing works offline. Whether anything is uncommitted is the publish step's question, since
    preparing is what writes and commits in the first place.
#>
function Assert-ReleasableWorktree {
    param([string] $Tag)

    $branch = (Invoke-Git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'main') {
        throw "Releases are cut from main; this worktree is on $branch."
    }

    $existing = & git -C $script:RepoRoot tag --list $Tag
    if ($existing) {
        throw "$Tag already exists here. Delete it or pick another version."
    }
}

<#
    What a release needs of origin, asked only when something is about to be pushed. Being ahead is
    normal, since the release commit was made here; being behind is not, because the push would then
    need a merge that nobody reviewed.
#>
function Assert-OriginReady {
    param([string] $Tag)

    Invoke-Git fetch origin main --quiet | Out-Null

    $behind = (Invoke-Git rev-list --count HEAD..origin/main).Trim()
    if ($behind -ne '0') {
        throw "main is $behind commits behind origin/main. Pull first."
    }
    $ahead = (Invoke-Git rev-list --count origin/main..HEAD).Trim()
    Write-Host "  main is $ahead commits ahead of origin/main"

    # Asked of origin directly rather than fetched: one local tag that disagrees with the remote makes
    # a --tags fetch fail outright, which would block a release for an unrelated old tag.
    $remote = & git -C $script:RepoRoot ls-remote --tags origin "refs/tags/$Tag"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not reach origin to look for $Tag."
    }
    if ($remote) {
        throw "$Tag already exists on origin."
    }
}

<#
    The machines this release measures on, read from the user's local directory rather than the
    repository: a host name and a checkout path belong to whoever is releasing, not to the branch.
    Returns the arguments for Update-ReleaseSizes.ps1, or null when there is no such file.

    Only what has no usable default belongs in it. The Mac user, its checkout and dotnet path, the
    sandbox it builds in, and the WSL checkout all derive from the machine or the repository already.
    The port is here because the measurement passes it to ssh and scp itself, which overrides whatever
    an ssh config alias says.

    %LOCALAPPDATA%\MewUI\release.json
    {
        "WslDistribution": "Ubuntu-24.04",
        "MacHost": "mac.local",
        "MacPort": 22
    }
#>
function Get-ReleaseSettings {
    if (-not (Test-Path -LiteralPath $script:SettingsPath)) {
        return $null
    }

    $json = Get-Content -LiteralPath $script:SettingsPath -Raw | ConvertFrom-Json
    $settings = @{}
    foreach ($name in 'WslDistribution', 'WslRepo', 'MacHost', 'MacPort', 'MacUser', 'MacRepo', 'MacDotNet', 'MacSandbox') {
        $value = $json.$name
        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string] $value)) {
            $settings[$name] = $value
        }
    }
    return $settings
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
