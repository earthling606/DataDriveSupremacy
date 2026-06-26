# Data Drive Supremacy

A small BepInEx mod for **Hardspace: Shipbreaker** that fixes the most disheartening loot roll in the game: cutting open a ship, finding the one narrative pickup, and it's a pink bunny — or worse, a credit drive with no lore at all.

There are over a hundred data drives to collect, and you only get one shot at lore per ship. Getting anything else just means re-rolling on a different ship rather than actually engaging with the one you're on. This mod tips the scales so that slot is (almost) always a data drive instead.

---

## What it actually does

Every ship has a single narrative slot. In vanilla it resolves to one of two things:

- a **narrative pickup** — a weighted random pick between a data drive, a data tablet, a pink bunny, or (on rare ghost ships) a helmet, **or**
- a **credit drive** instead — a straight cash pickup with no lore attached.

You only ever get one of these per ship (they're mutually exclusive), so any result that isn't a data drive is a missed chance at lore. This mod closes both gaps:

1. **In the narrative pickup roll**, it zeroes out the spawn weight of the bunny and the data tablet, so a data drive wins basically every time. Helmets are deliberately left alone — they're tied to a handful of rare ghost ship encounters and there's no reason to risk missing those.
2. **When a ship rolls a credit drive but still has lore left to find for it**, the mod swaps the credit drive for a data drive in the same spot. It collects and banks into your Data Miner exactly like a normal one.

Once a ship genuinely has **no lore left**, the credit drive is left untouched, so you keep the payout. Net result: as long as there's lore to collect, you get a data drive — on just about every ship.

## Installation

1. Install [BepInEx 5.4.23.5+](https://github.com/BepInEx/BepInEx/releases) for Hardspace: Shipbreaker if you haven't already.
2. Drop `DataDriveSupremacy.dll` into `BepInEx/plugins/`.
3. Launch the game.

## Known behavior / limitations

- **Credit drives only appear once a ship is out of lore.** While any applicable lore remains, the credit drive is swapped for a data drive; when that ship's lore is fully collected, the credit drive stays as the reward.
- **Swapped data drives don't play the first-person hand-reach animation** when you collect them. It's purely cosmetic — the drive and its lore work exactly like a normal one. Naturally-spawned data drives are unaffected.
- **Helmets are untouched** and will still appear on the rare ghost ships that spawn them.
- This mod adjusts in-memory assets and spawns pickups at runtime — it doesn't touch your save files or any game files on disk. Uninstalling is as simple as removing the DLL.

## Compatibility

This mod patches two methods:

- `ModuleListAsset.GenerateRandomModulesAsync` — to adjust the narrative pickup roll and record the data-drive / credit-drive prefabs.
- `NarrativeItemSystem.RefreshSpawnableEntries` — to perform the credit-drive → data-drive swap once a ship finishes spawning.

Other mods that touch the same methods, or that modify the `ML_FoundNarrativeDevices_RandomRotation` / `ML_FoundNarrativeDevices_NoRotation` / `ML_CreditDrives` module lists directly, may conflict. Nothing else should be affected.

Built with [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX).

## Building from source

Requires the .NET SDK and references to:

- `BepInEx.dll`, `0Harmony.dll` (from your `BepInEx/core/` folder)
- `Assembly-CSharp.dll`, `BBI.Unity.Game.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `Unity.Addressables.dll`, `Unity.Entities.dll`, `Unity.ResourceManager.dll` (from `Shipbreaker_Data/Managed/`)

See `DataDriveSupremacy.csproj` for the exact hint paths (they point at a default Steam install).

```
dotnet build -c Release
```

The output DLL goes in `bin/Release/netstandard2.0/`.

## Changelog

- **v1.2** — Credit drives are now swapped for data drives on any ship that still has lore to collect. Credit drives are kept once a ship's lore is exhausted. (Previously only bunnies and tablets were suppressed, so a credit-drive ship meant no lore at all.)
- **v1.1** — Zero out the bunny and data tablet spawn weights so the narrative slot resolves to a data drive.

## Wishlist / future ideas

- **Spawn a bunny once everything's collected.** When a ship is fully out of lore the credit drive currently stays — but spawning a bunny instead could bump your Roadtrip Friends counter and signal "you've got it all" rather than just paying out.
- **Restore the grab animation on swapped drives.** The swap uses a lightweight runtime instantiate that skips part of the game's spawn pipeline, which is why the hand-reach animation doesn't play. Routing through the full pipeline would fix it, at the cost of more complexity and risk.

## License

GPL v3
