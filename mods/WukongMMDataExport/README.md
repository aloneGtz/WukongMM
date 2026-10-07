# WukongMMDataExport

A read-only [CSharpLoader](https://github.com/czastack/B1CSharpLoader) mod bundled with WukongMM. After the player character is initialised (a save is loaded) it exports, once per game start:

| File (`<b1>\ModManager\GameData\`) | Content |
|---|---|
| `items.json`, `items.<culture>.json` | item ID → name (current game language), type, carry limit, level-item flag, consume type |
| `levels.json` | level table: EXP to next level, Sparks per level-up |
| `usage.json` | where items are used: weapon forging, armor enhancement, alchemy, gourd/drink/spirit upgrades, shop exchanges, seed outputs |

`items.json` is only overwritten while the game runs in Chinese (or if it does not exist yet), so playing once in English adds `items.en.json` without replacing the Chinese names.
It never changes game data. Install it from WukongMM's Settings page, or copy the DLL to `<b1>\Binaries\Win64\CSharpLoader\Mods\WukongMMDataExport\WukongMMDataExport.dll`.

## Building
Needs the game's managed assemblies and CSharpLoader's `CSharpModBase.dll` / `0Harmony.dll`, which are **not** included in this repository.
Point the build at a folder that has them (for example a checkout of [xyzkljl1/MyBlackMythWukongMods](https://github.com/xyzkljl1/MyBlackMythWukongMods)'s `CSharpMods` folder):
```
dotnet build mods/WukongMMDataExport -c Release -p:ModSdk=<folder with 0Harmony.dll and CSharpModBase.dll>\ -p:GameDllDir=<folder with the game DLLs>\
```
Output: `bin/Release/net472/WukongMMDataExport.dll`. When it exists, building `src/WukongMM.App` embeds it so the Settings page can install it.
