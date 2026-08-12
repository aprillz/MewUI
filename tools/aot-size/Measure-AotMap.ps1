[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $MapPath,

    [switch] $AsObject
)

$ErrorActionPreference = 'Stop'
$resolvedMap = (Resolve-Path $MapPath).Path
$summary = [ordered]@{
    map = $resolvedMap
    methodCodeBytes = [int64]0
    constructedTypeBytes = [int64]0
    metadataBytes = [int64]0
    methodCount = 0
    constructedTypeCount = 0
}
$largestMethods = [Collections.Generic.List[object]]::new()

Get-Content -LiteralPath $resolvedMap | ForEach-Object {
    if ($_ -match '<MethodCode Name="([^"]+)" Length="(\d+)"') {
        $bytes = [int64]$Matches[2]
        $summary.methodCodeBytes += $bytes
        $summary.methodCount++
        $largestMethods.Add([pscustomobject]@{ bytes = $bytes; name = $Matches[1] })
    } elseif ($_ -match '<ConstructedEEType Name="[^"]+" Length="(\d+)"') {
        $summary.constructedTypeBytes += [int64]$Matches[1]
        $summary.constructedTypeCount++
    } elseif ($_ -match '<Metadata Name="__embedded_metadata" Length="(\d+)"') {
        $summary.metadataBytes = [int64]$Matches[1]
    }
}

$result = [pscustomobject]@{
    map = $summary.map
    methodCodeBytes = $summary.methodCodeBytes
    constructedTypeBytes = $summary.constructedTypeBytes
    metadataBytes = $summary.metadataBytes
    methodCount = $summary.methodCount
    constructedTypeCount = $summary.constructedTypeCount
    largestMethods = @($largestMethods | Sort-Object bytes -Descending | Select-Object -First 30)
}

if ($AsObject) {
    return $result
}

$result | ConvertTo-Json -Depth 5
