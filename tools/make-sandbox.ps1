# Build a sandbox "game folder" for UI tests and screenshots: the app runs against it with --game <root>
# and never touches the real game. Copies one save file and the exported game data tables.
# Usage: powershell -File tools\make-sandbox.ps1 -Game <real game root> -Out <sandbox root> [-SaveFile <.sav>] [-SteamIdDir <name>]
#   -SaveFile:   save to copy (default: the newest ArchiveSaveFile.*.sav under the real SaveGames)
#   -SteamIdDir: folder name used instead of the real SteamID (default 76561190000000001) so screenshots show no real ID
# Refuses to write into a folder that looks like a real game install.
# NOTE: keep ASCII-only (Windows PowerShell 5.1).
param(
    [Parameter(Mandatory = $true)][string]$Game,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$SaveFile = '',
    [string]$SteamIdDir = '76561190000000001'
)
$ErrorActionPreference = 'Stop'
if (Test-Path (Join-Path $Out 'b1\Content')) { throw "$Out looks like a real game folder (b1\Content exists)" }
$realB1 = Join-Path $Game 'b1'
if (-not $SaveFile) {
    $SaveFile = (Get-ChildItem (Join-Path $realB1 'Saved\SaveGames') -Recurse -Filter 'ArchiveSaveFile.*.sav' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
$b1 = Join-Path $Out 'b1'
$win64 = Join-Path $b1 'Binaries\Win64'
$saves = Join-Path $b1 ('Saved\SaveGames\' + $SteamIdDir)
$data = Join-Path $b1 'ModManager\GameData'
foreach ($d in @($win64, $saves, $data)) { New-Item -ItemType Directory -Force $d | Out-Null }
$exe = Join-Path $win64 'b1-Win64-Shipping.exe'
if (-not (Test-Path $exe)) { [System.IO.File]::WriteAllBytes($exe, [byte[]]@()) }
Copy-Item -LiteralPath $SaveFile -Destination (Join-Path $saves (Split-Path $SaveFile -Leaf)) -Force
$share = Join-Path (Split-Path $SaveFile -Parent) 'ShareArchiveSaveFile.sav'
if (Test-Path -LiteralPath $share) { Copy-Item -LiteralPath $share -Destination $saves -Force }
Get-ChildItem (Join-Path $realB1 'ModManager\GameData') -Filter '*.json' | Copy-Item -Destination $data -Force
"sandbox ready: $Out (save $(Split-Path $SaveFile -Leaf) -> $SteamIdDir)"
