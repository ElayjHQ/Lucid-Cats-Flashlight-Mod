# Lucid Cats Flashlight mod - uninstaller
# Removes the mod files and returns the game to vanilla.

$ErrorActionPreference = 'Stop'

Write-Host "== Lucid Cats Flashlight uninstaller ==" -ForegroundColor Cyan

$steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
$libraries = @()
if ($steamPath) {
    $libraries += $steamPath
    $vdf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"').Matches |
            ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' } |
            ForEach-Object { $libraries += $_ }
    }
}
$libraries = $libraries | Sort-Object -Unique

$gameDir = $null
foreach ($lib in $libraries) {
    if (Test-Path (Join-Path $lib 'steamapps\appmanifest_4778540.acf')) {
        $gameDir = Join-Path $lib 'steamapps\common\Lucid Cats'
        break
    }
}

if (-not $gameDir -or -not (Test-Path (Join-Path $gameDir 'LucidCats.exe'))) {
    $gameDir = Read-Host "Could not auto-detect. Paste the full path of your Lucid Cats folder"
    $gameDir = $gameDir.Trim('"')
}

if (-not (Test-Path (Join-Path $gameDir 'LucidCats.exe'))) {
    Write-Host "[X] Not a Lucid Cats folder: $gameDir" -ForegroundColor Red
    exit 1
}

foreach ($target in @('BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) {
    $path = Join-Path $gameDir $target
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "  removed $target"
    }
}

Write-Host ""
Write-Host "[OK] Mod removed. Your game is back to vanilla." -ForegroundColor Green
