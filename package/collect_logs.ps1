# Собирает журналы WorldBoxMP и игры в один zip на рабочем столе.
# Запускается из СОБРАТЬ_ЛОГИ.bat в папке мода. Можно запускать и при открытой игре.
$ErrorActionPreference = 'Continue'
$mod = Split-Path -Parent $MyInvocation.MyCommand.Path
$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm'
$name = "WorldBoxMP-logs-$env:COMPUTERNAME-$stamp"
$tmp = Join-Path $env:TEMP $name
$desktop = [Environment]::GetFolderPath('Desktop')
$zip = Join-Path $desktop "$name.zip"

# копия файла, который игра держит открытым
function Copy-Shared($src, $dst) {
    try {
        $in = [IO.File]::Open($src, 'Open', 'Read', 'ReadWrite')
        $out = [IO.File]::Create($dst)
        $in.CopyTo($out)
        $out.Close(); $in.Close()
        return $true
    } catch {
        Write-Host "  не удалось скопировать $src : $($_.Exception.Message)"
        return $false
    }
}

New-Item -ItemType Directory -Force $tmp | Out-Null
$count = 0

# журналы сессий мода
$logs = Join-Path $mod 'logs'
if (Test-Path $logs) {
    New-Item -ItemType Directory -Force (Join-Path $tmp 'logs') | Out-Null
    Get-ChildItem $logs -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 30 | ForEach-Object {
        if (Copy-Shared $_.FullName (Join-Path $tmp "logs\$($_.Name)")) { $count++ }
    }
}

# настройки и версия мода
foreach ($f in 'WorldBoxMP.cfg', 'mod.json') {
    $p = Join-Path $mod $f
    if (Test-Path $p) { Copy-Shared $p (Join-Path $tmp $f) | Out-Null }
}

# журнал самой игры (вылеты, ошибки вне сессии)
$unity = Join-Path $env:USERPROFILE 'AppData\LocalLow\mkarpenko\WorldBox'
foreach ($f in 'Player.log', 'Player-prev.log') {
    $p = Join-Path $unity $f
    if (Test-Path $p) { if (Copy-Shared $p (Join-Path $tmp $f)) { $count++ } }
}

# список модов в папке игры
$gameMods = Split-Path -Parent $mod
Get-ChildItem $gameMods -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name | Out-File (Join-Path $tmp 'mods.txt') -Encoding utf8

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $tmp '*') -DestinationPath $zip -Force
Remove-Item $tmp -Recurse -Force

Write-Host ""
Write-Host "Готово, файлов: $count"
Write-Host "Архив: $zip"
Write-Host "Отправьте этот файл хосту (или тому, кто делает мод)."
if (-not $env:WBMP_QUIET) {
    Start-Process explorer.exe "/select,`"$zip`""
    Read-Host "Нажмите Enter, чтобы закрыть"
}
