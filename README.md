# Data Drive Supremacy

A small BepInEx mod for **Hardspace: Shipbreaker** that fixes the most disheartening loot roll in the game: cutting open a ship, finding the one narrative pickup, and it's a pink bunny.

There are over a hundred data drives to collect, and you only get one shot at lore per ship. Getting a bunny instead just means re-rolling on a different ship rather than actually engaging with the ship you're on. This mod tips the scales so that slot is (almost) always a data drive instead.

---

## What it actually does

Every ship spawns one narrative pickup somewhere in its layout — normally a weighted random pick between a data drive, a data tablet, a pink bunny, or (on rare ghost ships) a helmet.

This mod zeroes out the spawn weight of the bunny and the data tablet in that roll, so data drives win basically every time. Helmets are deliberately left alone, since they're tied to a handful of rare ghost ship encounters and there's no reason to risk missing out on those.

Credit chips are **not** affected — they come from a completely separate loot pool and were never part of this problem.

## Installation

1. Install [BepInEx 5.4.23.5+](https://github.com/BepInEx/BepInEx/releases) for Hardspace: Shipbreaker if you haven't already.
2. Drop `DataDriveSupremacy.dll` into `BepInEx/plugins/`.
3. Launch the game.

## Known behavior / limitations

- **Once you've collected every data drive for a ship archetype**, a credit drive will spawn instead.
- **Helmets are untouched** and will still appear on the rare ghost ships that spawn them.
- This mod edits the spawn weight of a shared in-memory asset at runtime — it doesn't touch your save files or any game files on disk. Uninstalling is as simple as removing the DLL.

## Compatibility

This patches `ModuleListAsset.GenerateRandomModulesAsync`. Other mods that touch the same method, or that modify the `ML_FoundNarrativeDevices_RandomRotation` / `ML_FoundNarrativeDevices_NoRotation` module lists directly, may conflict. Nothing else should be affected.

Built with [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX).

## Building from source

Requires the .NET SDK and references to:

- `BepInEx.dll`, `0Harmony.dll` (from your `BepInEx/core/` folder)
- `Assembly-CSharp.dll`, `BBI.Unity.Game.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `Unity.Addressables.dll`, `Unity.ResourceManager.dll` (from `Shipbreaker_Data/Managed/`)

```
dotnet build -c Release
```

The output DLL goes in `bin/Release/netstandard2.0/`.

## Wishlist / future ideas
 
- **Spawn a bunny once everything's collected.** If every data drive across all ship archetypes has already been found, the hardpoint should spawn a bunny instead of nothing. Right now an exhausted pool just means an empty slot — a bunny would actually signal "you've got it all," and bump your Roadtrip Friends counter instead of wasting the slot entirely.
- **Confirm the credit drive fallback claim.** It's possible (based on play experience, not confirmed in code/logs during dev) that an exhausted data drive pool already falls back to a credit drive on its own via vanilla logic. This needs to actually be traced through TryMarkObjectForDestruction and whatever runs after it before it's stated as fact anywhere more public than this git repo.

## License

GPL v3