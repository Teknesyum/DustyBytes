param(
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

$registryRoots = @(
    'HKLM\SOFTWARE',
    'HKCU\Software',
    'HKLM\SYSTEM\CurrentControlSet\Services'
)

$fileRoots = @(
    $env:ProgramFiles,
    ${env:ProgramFiles(x86)},
    $env:ProgramData,
    $env:APPDATA,
    $env:LOCALAPPDATA,
    (Join-Path (Split-Path $env:LOCALAPPDATA) 'LocalLow'),
    [Environment]::GetFolderPath('Desktop'),
    [Environment]::GetFolderPath('CommonDesktopDirectory'),
    (Join-Path $env:windir 'System32\Tasks'),
    (Join-Path $env:windir 'System32\drivers')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

function Convert-RegExport([string]$regFile, [System.IO.StreamWriter]$writer) {
    $key = ''
    $pending = ''
    foreach ($raw in [System.IO.File]::ReadLines($regFile, [System.Text.Encoding]::Unicode)) {
        $line = $pending + $raw.TrimStart()
        if ($line.EndsWith('\')) {
            $pending = $line.Substring(0, $line.Length - 1)
            continue
        }
        $pending = ''
        if ($line.Length -eq 0) { continue }
        if ($line.StartsWith('[') -and $line.EndsWith(']')) {
            $key = $line.Substring(1, $line.Length - 2)
            $writer.WriteLine("K`t$key")
            continue
        }
        if ($key.Length -gt 0) {
            $writer.WriteLine("V`t$key`t$line")
        }
    }
}

$regOut = Join-Path $Out 'registry.txt'
$writer = New-Object System.IO.StreamWriter($regOut, $false, (New-Object System.Text.UTF8Encoding($false)))
try {
    foreach ($root in $registryRoots) {
        $tmp = Join-Path $Out ('export-' + ($root -replace '[\\ ]', '_') + '.reg')
        & reg.exe export $root $tmp /y | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Dışa aktarılamadı: $root"
            continue
        }
        Convert-RegExport $tmp $writer
        Remove-Item $tmp -Force
    }
}
finally {
    $writer.Dispose()
}

$filesOut = Join-Path $Out 'files.txt'
Remove-Item $filesOut -Force -ErrorAction SilentlyContinue
foreach ($root in $fileRoots) {
    & cmd.exe /d /c "dir /s /b /a `"$root`" 2>nul" | Add-Content -Path $filesOut -Encoding UTF8
}

$servicesOut = Join-Path $Out 'services.txt'
Get-CimInstance Win32_Service | ForEach-Object { "{0}`t{1}`t{2}" -f $_.Name, $_.StartMode, $_.PathName } |
    Sort-Object | Set-Content -Path $servicesOut -Encoding UTF8
Get-CimInstance Win32_SystemDriver | ForEach-Object { "{0}`t{1}`t{2}" -f $_.Name, $_.StartMode, $_.PathName } |
    Sort-Object | Add-Content -Path $servicesOut -Encoding UTF8

$tasksOut = Join-Path $Out 'tasks.txt'
Get-ScheduledTask | ForEach-Object { $_.TaskPath + $_.TaskName } | Sort-Object | Set-Content -Path $tasksOut -Encoding UTF8

$counts = @(
    "zaman`t$stamp",
    "kullanici`t$env:USERNAME",
    "kayit`t" + (Get-Content $regOut | Measure-Object -Line).Lines,
    "dosya`t" + (Get-Content $filesOut | Measure-Object -Line).Lines,
    "servis`t" + (Get-Content $servicesOut | Measure-Object -Line).Lines,
    "gorev`t" + (Get-Content $tasksOut | Measure-Object -Line).Lines
)
$counts | Set-Content -Path (Join-Path $Out 'meta.txt') -Encoding UTF8
$counts
