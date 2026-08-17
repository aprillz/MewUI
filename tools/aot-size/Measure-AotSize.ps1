[CmdletBinding()]
param(
    [ValidateSet('Empty', 'Text', 'Button', 'Image')]
    [string[]] $Probe = @('Empty', 'Text', 'Button', 'Image'),

    [string] $RuntimeIdentifier = 'win-x64',

    [ValidateSet('Gdi', 'Direct2D', 'MewVG')]
    [string] $Backend = 'Gdi',

    [string] $OutputRoot,

    [string] $BaselinePath,

    [int64] $AllowedGrowthBytes = 16384
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $PSScriptRoot 'MewUI.AotSizeProbe\MewUI.AotSizeProbe.csproj'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot ".artifacts\aot-size\$RuntimeIdentifier-$($Backend.ToLowerInvariant())"
} elseif (-not [IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot $OutputRoot
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)

$platformDefine = if ($RuntimeIdentifier.StartsWith('win-')) {
    'MEWUI_PLATFORM_WIN32'
} elseif ($RuntimeIdentifier.StartsWith('linux-')) {
    'MEWUI_PLATFORM_LINUX'
} elseif ($RuntimeIdentifier.StartsWith('osx-')) {
    'MEWUI_PLATFORM_MACOS'
} else {
    throw "Unsupported runtime identifier '$RuntimeIdentifier'."
}

if (-not $RuntimeIdentifier.StartsWith('win-') -and $Backend -ne 'MewVG') {
    throw "Runtime '$RuntimeIdentifier' supports only the MewVG backend. Pass -Backend MewVG."
}

$backendDefine = if ($Backend -eq 'Direct2D') {
    'MEWUI_BACKEND_DIRECT2D'
} elseif ($Backend -eq 'MewVG') {
    'MEWUI_BACKEND_MEWVG'
} else {
    'MEWUI_BACKEND_GDI'
}

$backendProject = if ($RuntimeIdentifier.StartsWith('win-')) {
    Join-Path $repoRoot "src\MewUI.Backend.$Backend\MewUI.Backend.$Backend.csproj"
} elseif ($RuntimeIdentifier.StartsWith('linux-')) {
    Join-Path $repoRoot 'src\MewUI.Backend.MewVG.X11\MewUI.Backend.MewVG.X11.csproj'
} else {
    Join-Path $repoRoot 'src\MewUI.Backend.MewVG.MacOS\MewUI.Backend.MewVG.MacOS.csproj'
}

& dotnet build $backendProject -c Release -f net10.0 -p:PublishAot=true -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) {
    throw "Failed to prepare the '$Backend' backend for NativeAOT probes."
}

# Restored once here rather than by each publish below, which passes --no-restore so that four probes
# of one configuration share the work. The runtime identifier and the backend are what select the
# probe's project references, so they have to be the ones the publishes will use.
& dotnet restore $project -r $RuntimeIdentifier "-p:MewUIBackend=$Backend"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to restore the NativeAOT probe project."
}

$results = @()
foreach ($probeName in $Probe) {
    $probeRoot = Join-Path $OutputRoot $probeName.ToLowerInvariant()
    $publishDir = Join-Path $probeRoot 'publish'
    $arguments = @(
        'publish', $project,
        '-c', 'Release',
        '-r', $RuntimeIdentifier,
        '--self-contained', 'true',
        '--no-restore',
        '-p:BuildProjectReferences=false',
        '-p:UseSharedCompilation=false',
        "-p:AotSizeProbe=$probeName",
        "-p:MewUIBackend=$Backend",
        "-p:AotSizePlatformDefine=$platformDefine",
        "-p:AotSizeBackendDefine=$backendDefine",
        "-p:PublishDir=$publishDir\"
    )

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "NativeAOT publish failed for probe '$probeName'."
    }

    $executable = Get-ChildItem $publishDir -File |
        Where-Object { $_.Extension -in @('.exe', '') -and $_.Name -notlike '*.dbg' } |
        Sort-Object Length -Descending |
        Select-Object -First 1
    if ($null -eq $executable) {
        throw "No executable was produced for probe '$probeName'."
    }

    $mapRoot = Join-Path $PSScriptRoot "MewUI.AotSizeProbe\obj\Release\net10.0\$RuntimeIdentifier\native"
    $map = Get-ChildItem $mapRoot -Filter '*.map.xml' -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $map) {
        throw "No NativeAOT map was produced for probe '$probeName'."
    }

    $mapCopy = Join-Path $probeRoot "$probeName.map.xml"
    Copy-Item -LiteralPath $map.FullName -Destination $mapCopy -Force
    $mapSummary = & (Join-Path $PSScriptRoot 'Measure-AotMap.ps1') -MapPath $mapCopy -AsObject

    $results += [pscustomobject]@{
        probe = $probeName
        executableBytes = $executable.Length
        methodCodeBytes = $mapSummary.methodCodeBytes
        constructedTypeBytes = $mapSummary.constructedTypeBytes
        metadataBytes = $mapSummary.metadataBytes
        methodCount = $mapSummary.methodCount
        constructedTypeCount = $mapSummary.constructedTypeCount
        executable = $executable.FullName.Substring($repoRoot.Length + 1)
        map = $mapCopy.Substring($repoRoot.Length + 1)
    }
}

$report = [ordered]@{
    schemaVersion = 1
    measuredAtUtc = [DateTime]::UtcNow.ToString('O')
    commit = (& git -C $repoRoot rev-parse HEAD).Trim()
    dotnetSdk = (& dotnet --version).Trim()
    runtimeIdentifier = $RuntimeIdentifier
    backend = $Backend
    publish = [ordered]@{
        targetFramework = 'net10.0'
        selfContained = $true
        publishAot = $true
        trimMode = 'full'
        optimization = 'Size'
        invariantGlobalization = $true
    }
    probes = $results
}

$reportPath = Join-Path $OutputRoot 'report.json'
$reportJson = $report | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($reportPath, $reportJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "NativeAOT size report: $reportPath"
$results | Format-Table probe, executableBytes, methodCodeBytes, constructedTypeBytes, metadataBytes

if (-not [string]::IsNullOrWhiteSpace($BaselinePath)) {
    $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
    $failures = @()
    foreach ($result in $results) {
        $expected = $baseline.probes | Where-Object probe -eq $result.probe | Select-Object -First 1
        if ($null -eq $expected) {
            $failures += "No baseline exists for probe '$($result.probe)'."
            continue
        }

        $growth = $result.executableBytes - $expected.executableBytes
        if ($growth -gt $AllowedGrowthBytes) {
            $failures += "Probe '$($result.probe)' grew by $growth bytes; allowed growth is $AllowedGrowthBytes bytes."
        }
    }

    if ($failures.Count -ne 0) {
        throw ($failures -join [Environment]::NewLine)
    }
}
