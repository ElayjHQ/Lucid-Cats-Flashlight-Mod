# Lucid Cats Flashlight mod - installer
# Finds the Lucid Cats install via Steam and drops the mod files in.

$ErrorActionPreference = 'Stop'

$payload = Join-Path $PSScriptRoot 'LucidCatsFlashlightMod'
if (-not (Test-Path (Join-Path $payload 'winhttp.dll'))) {
    Write-Host "[X] Payload folder 'LucidCatsFlashlightMod' missing next to this script." -ForegroundColor Red
    exit 1
}

Write-Host "== Lucid Cats Flashlight installer ==" -ForegroundColor Cyan

# 1) Find Steam's libraries
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

# 2) Find the library that has Lucid Cats installed
$gameDir = $null
foreach ($lib in $libraries) {
    $manifest = Join-Path $lib 'steamapps\appmanifest_4778540.acf'
    if (Test-Path $manifest) {
        $gameDir = Join-Path $lib 'steamapps\common\Lucid Cats'
        break
    }
}

# 3) Fall back to asking the user
if (-not $gameDir -or -not (Test-Path (Join-Path $gameDir 'LucidCats.exe'))) {
    Write-Host "Could not auto-detect the game folder." -ForegroundColor Yellow
    $gameDir = Read-Host "Paste the full path of your Lucid Cats folder (e.g. C:\Program Files (x86)\Steam\steamapps\common\Lucid Cats)"
    $gameDir = $gameDir.Trim('"')
}

if (-not (Test-Path (Join-Path $gameDir 'LucidCats.exe'))) {
    Write-Host "[X] '$gameDir' does not look like the Lucid Cats folder (no LucidCats.exe)." -ForegroundColor Red
    exit 1
}

# 4) Copy the mod in
try {
    Copy-Item (Join-Path $payload 'winhttp.dll') $gameDir -Force
    Copy-Item (Join-Path $payload 'doorstop_config.ini') $gameDir -Force
    Copy-Item (Join-Path $payload '.doorstop_version') $gameDir -Force
    Copy-Item (Join-Path $payload 'BepInEx') $gameDir -Recurse -Force
} catch {
    Write-Host "[X] Copy failed: $_" -ForegroundColor Red
    Write-Host "    Is the game running? Close it and try again." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "[OK] Flashlight mod installed to:" -ForegroundColor Green
Write-Host "     $gameDir"
Write-Host ""
Write-Host "Launch the game through Steam as usual and press F in-game"
Write-Host "to toggle your flashlight. Have fun!"
