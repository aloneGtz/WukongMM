# User guide (WukongMM 1.0)

[中文](USER_GUIDE.md)

## 1. Installation
1. Install the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) if you don't have it.
2. Download `WukongMM-<version>-win-x64.zip`, extract it anywhere and run `WukongMM.exe`. No admin rights are needed (unless the game is under `C:\Program Files`).
3. On first start the Steam libraries are scanned for the game. If it is not found, go to **Settings → Game folder** and pick the folder that contains `b1` (e.g. `...\steamapps\common\BlackMythWukong`).
4. The **English / 中文** button at the top right switches the interface language and remembers your choice.

Where data lives: settings and item-name notes in `%APPDATA%\WukongMM`; mod notes, the disabled-mod store and save backups in `<game>\b1\ModManager`.
To uninstall, delete the program folder and those two folders (enable the mods you want to keep first — disabled ones live in the disabled store).

## 2. Mods
- Put mods into the game folder as their authors describe (e.g. paks into `b1\Content\Paks\~mods`) and click **Refresh**.
- **Ticked = enabled, unticked = disabled.** Disabling moves the mod's files into `b1\ModManager\Disabled` (same relative path); nothing is deleted. Ticking moves them back. Not possible while the game runs.
- Detection: files in `~mods` with the same stem (`.pak/.utoc/.ucas/.sig`) form one mod, a sub-folder of `~mods` is one mod; the same for `LogicMods` (needs UE4SS);
  `Binaries\Win64\CSharpLoader\Mods\<name>` is a C# mod (needs CSharpLoader); `Binaries\Win64\ue4ss\Mods\<name>` is a UE4SS mod.
  UE4SS's built-in scripts are hidden by default ("Show built-in framework components").
- **Custom mods**: some mods live in two places (e.g. a UE4SS script plus a pak). Use "New custom mod…" to group them so they are switched together. Deleting a group never deletes files.
- **State "Duplicate"**: the mod exists both in the game folder and in the disabled store (usually installed again while disabled). Open the folders, delete the copy you don't want and refresh.
- **Resource conflicts**: the "Conflicts" column shows how many mods replace the same game files: `⚠ N` = both enabled, conflicting now; a plain number = one side is disabled.
  Select a mod to see the other mods and the shared files on the right; enabling a conflicting mod asks for confirmation. When two mods replace the same data table, only one of them takes effect.
- **-fileopenlog**: many pak mods only load when the game is started with `-fileopenlog`. It is on by default in Settings and the "Launch game" button adds it; you can also put it into the Steam launch options.
- Each mod can have a display name, a source link and notes (stored in `b1\ModManager\mods.json`).

## 3. Save editor
**Quit the game completely before editing.** Every write is backed up first; restore from "Backups" at the bottom left if anything goes wrong.

### 3.1 Install the export mod first (recommended)
The save editor uses the game's own data tables for item names, carry limits and to decide which items are editable. They are exported by the bundled **WukongMMDataExport** (a read-only CSharpLoader mod) after a save is loaded:
1. Install [CSharpLoader](https://github.com/czastack/B1CSharpLoader) (put `version.dll` and the `CSharpLoader` folder into `b1\Binaries\Win64`).
2. Settings → **Install / update the export mod**.
3. Start the game and load a save once (until you can move), then quit. The data goes to `b1\ModManager\GameData`.
4. For English item names, switch the game's text language to English and load a save once more; you can switch back afterwards.
Without exported data the built-in table (matching game 1.0.21) is used, but item names and carry limits are missing.

### 3.2 Character
- **Level**: must be a level in the game's level table (1–342 in 1.0.21). "Grant Sparks for levels gained" adds the Sparks the level-ups would have given (editing the level alone gives none). Lowering the level does not refund Sparks already spent.
- **Will**: keep it at or below 999,999,999 (the game uses int32 and overflows near 2.1 billion).
- **Current EXP**: EXP earned within the current level; it cannot reach the amount needed for the next level. Total EXP is synced automatically.
- **Sparks**: unspent points, at most 9999.
- Odd values already in the save (e.g. from other tools) do not block saving as long as you leave them unchanged; they are only listed in the confirmation.

### 3.3 Items
- Only **crafting, alchemy and upgrade materials, pills and seeds** are listed: Pills, Herbs, Seeds, Weapon materials, Armor materials, Rare materials (boss drops used as key materials, Mind Core) and Upgrade materials (spirit upgrades).
  Story/quest items, exchange-only items (e.g. Bodhisattva's Left/Right Hand) and sell-only gold pieces are not shown and stay untouched in the save.
- Use the **Category** box to show one category only (e.g. only seeds); the search box filters by ID / name / category; "Modified only".
- Double-click Count or Storage to edit. Counts above Limit turn red (the game does not complain, but you cannot pick up or buy the item until it drops below the limit). Pills may exceed the limit; the excess goes to storage.
  Storage is the reserve that refills the count when you rest at a shrine.
- **Add item**: type a name (auto-complete) or an ID, enter a count and click Add. A seed that has an entry in the bag (even with count 0) no longer drops from enemies or chests; you are warned first.
- Double-click Name to set your own note (stored on this PC only).

### 3.4 Saving and restoring
- Click **Save to file**. The confirmation lists every change and any notes; after confirming: automatic backup → write → checksum recalculated → self-check.
- With Steam Cloud on, Steam usually uploads the new local save the next time you start the game; if a conflict prompt appears, choose the local file.
- **Restore**: pick a backup under "Backups" → Restore selected backup (the current save is backed up again first). Backups live in `b1\ModManager\SaveBackups\<SteamID>`.

## 4. FAQ
- **A mod has no effect**: pak mods usually need `-fileopenlog`; C# mods need CSharpLoader; UE4SS / LogicMods need UE4SS (and UE4SS itself must not be disabled). Check the hint shown for each mod.
- **The game says the save is damaged / cannot load it**: restore the latest backup and please report it on GitHub Issues.
- **All buttons are grey**: the game is running (top right shows "Game running"); quit the game.
- **After a game update**: load a save once so the export mod refreshes the data tables. If the save format changed, the editor reports "unrecognized format" — do not edit then.

## 5. Command line (advanced)
`wmm.exe` (built from source): `wmm mods list|enable|disable|conflicts`, `wmm items` (editable items), `wmm info <save>`, `wmm apply <save> ...` (same rules as the UI, automatic backup), `wmm selftest`.
