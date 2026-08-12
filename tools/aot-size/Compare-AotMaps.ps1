[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaselineMap,
    [Parameter(Mandatory)] [string] $CurrentMap,
    [int] $Top = 40
)

$ErrorActionPreference = 'Stop'

function Read-Map([string] $path) {
    $nodes = @{}
    Get-Content -LiteralPath (Resolve-Path $path) | ForEach-Object {
        if ($_ -match '^\s*<([A-Za-z0-9]+) Name="([^"]+)" Length="(\d+)"') {
            $nodes["$($Matches[1])`0$($Matches[2])"] = [pscustomobject]@{
                kind = $Matches[1]
                name = $Matches[2]
                bytes = [int64]$Matches[3]
            }
        }
    }
    return $nodes
}

$baseline = Read-Map $BaselineMap
$current = Read-Map $CurrentMap
$changes = foreach ($key in $current.Keys) {
    $oldBytes = if ($baseline.ContainsKey($key)) { $baseline[$key].bytes } else { 0 }
    $node = $current[$key]
    $delta = $node.bytes - $oldBytes
    if ($delta -gt 0) {
        [pscustomobject]@{
            deltaBytes = $delta
            currentBytes = $node.bytes
            kind = $node.kind
            name = $node.name
            isNew = -not $baseline.ContainsKey($key)
        }
    }
}

$changes | Sort-Object deltaBytes -Descending | Select-Object -First $Top |
    Format-Table deltaBytes, currentBytes, kind, isNew, name -AutoSize
