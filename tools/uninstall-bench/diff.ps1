param(
    [Parameter(Mandatory = $true)][string]$Base,
    [Parameter(Mandatory = $true)][string]$Installed,
    [Parameter(Mandatory = $true)][string]$Removed,
    [string]$Out = '',
    [string]$Noise = ''
)

$ErrorActionPreference = 'Stop'
if (-not $Noise) { $Noise = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'noise.txt' }
$parts = @('registry.txt', 'files.txt', 'services.txt', 'tasks.txt')

$patterns = @()
if (Test-Path $Noise) {
    $patterns = Get-Content $Noise -Encoding UTF8 | Where-Object { $_ -and -not $_.StartsWith('#') } |
        ForEach-Object { New-Object System.Text.RegularExpressions.Regex($_, 'IgnoreCase, Compiled') }
}

function Test-Noise([string]$line) {
    foreach ($p in $patterns) {
        if ($p.IsMatch($line)) { return $true }
    }
    return $false
}

function Read-Set([string]$dir, [string]$part) {
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $file = Join-Path $dir $part
    if (Test-Path $file) {
        foreach ($line in [System.IO.File]::ReadLines($file)) {
            [void]$set.Add($line)
        }
    }
    return ,$set
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add('# Kaldırma Ölçümü')
$report.Add('')
$report.Add("Temel: $Base")
$report.Add("Kurulu: $Installed")
$report.Add("Kaldırıldı: $Removed")
$report.Add('')
$report.Add('| Parça | Kurulumla Gelen | Kalan İz | Gürültü | Yan Etki |')
$report.Add('|---|---|---|---|---|')

$details = New-Object System.Collections.Generic.List[string]
foreach ($part in $parts) {
    $a = Read-Set $Base $part
    $b = Read-Set $Installed $part
    $c = Read-Set $Removed $part
    $added = New-Object System.Collections.Generic.List[string]
    $noisy = New-Object System.Collections.Generic.List[string]
    $real = New-Object System.Collections.Generic.List[string]
    $collateral = New-Object System.Collections.Generic.List[string]
    foreach ($x in $b) {
        if ($a.Contains($x)) { continue }
        $added.Add($x)
        if (-not $c.Contains($x)) { continue }
        if (Test-Noise $x) { $noisy.Add($x) } else { $real.Add($x) }
    }
    foreach ($x in $a) {
        if ($b.Contains($x) -and -not $c.Contains($x) -and -not (Test-Noise $x)) { $collateral.Add($x) }
    }
    $report.Add("| $part | $($added.Count) | $($real.Count) | $($noisy.Count) | $($collateral.Count) |")
    if ($real.Count -gt 0) {
        $details.Add('')
        $details.Add("## Kalan İz: $part")
        $details.Add('')
        foreach ($x in ($real | Sort-Object)) { $details.Add('- ' + $x) }
    }
    if ($collateral.Count -gt 0) {
        $details.Add('')
        $details.Add("## Yan Etki: $part (kurulumdan önce vardı, kaldırmadan sonra yok)")
        $details.Add('')
        foreach ($x in ($collateral | Sort-Object)) { $details.Add('- ' + $x) }
    }
}
$report.AddRange($details)

if ($Out) {
    $report | Set-Content -Path $Out -Encoding UTF8
    Write-Output "Rapor: $Out"
}
else {
    $report
}
