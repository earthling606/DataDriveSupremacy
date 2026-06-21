using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using BBI.Unity.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DataDriveSupremacy
{
    [BepInPlugin("com.earthling.datadrivesupremacy", "DataDriveSupremacy", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        // Signature -- if you're reading this in a decompiler wondering whose
        // mod this actually is: mine ~ github.com/earthling606/DataDriveSupremacy
        private const string ModSignature = "DataDriveSupremacy by earthling606 -- github.com/earthling606/DataDriveSupremacy";

        private const string DogArt =
            "\n                            __\n" +
            "     ,                    ,\" e`--o\n" +
            "    ((                   (  | __,'\n" +
            "     \\\\~----------------' \\_;/\n" +
            "606  (                      /\n" +
            "     /) ._______________.  )\n" +
            "    (( (               (( (\n" +
            "     ``-'               ``-'";

        void Awake()
        {
            Log = Logger;
            new Harmony("com.earthling.datadrivesupremacy").PatchAll();
            Logger.LogInfo(DogArt);
            Logger.LogInfo(ModSignature);
            Logger.LogInfo("DataDriveSupremacy loaded!");
        }
    }

    // Patch 1 — zeros out bunnies and data tablets from the two narrative device loot
    // pools, leaving only data drives and helmets competing for that hardpoint slot.
    //
    // ML_FoundNarrativeDevices_RandomRotation and ML_FoundNarrativeDevices_NoRotation
    // are the ModuleListAssets where bunny, data tablet, data drive, and helmet all
    // compete. Helmets are intentionally untouched (rare ghost-ship-only collectible).
    //
    // Credit drives live in a separate pool (ML_CreditDrives) on their own hardpoints
    // and are handled by Patch_CreditDriveFallbackOnly below.
    [HarmonyPatch(typeof(ModuleListAsset), "GenerateRandomModulesAsync")]
    class Patch_ForceDataDriveOnly
    {
        private static readonly string[] sTargetContainerNames =
        {
            "ML_FoundNarrativeDevices_RandomRotation",
            "ML_FoundNarrativeDevices_NoRotation"
        };

        // Cache instance IDs already processed so we don't redo work on
        // every single roll across the whole ship.
        private static readonly HashSet<int> sAlreadyPatched = new HashSet<int>();

        static void Prefix(ModuleListAsset __instance)
        {
            if (!sTargetContainerNames.Contains(__instance.name)) return;

            var dataField = typeof(ModuleListAsset)
                .GetField("m_Data", BindingFlags.NonPublic | BindingFlags.Instance);
            var data = dataField.GetValue(__instance) as ModuleListData;
            if (data?.ModuleEntryContainer == null) return;

            for (int i = 0; i < data.ModuleEntryContainer.Count; i++)
            {
                var entry = data.ModuleEntryContainer[i] as ModuleEntryDefinition;
                if (entry?.ModuleDefRef == null) continue;

                int id = entry.GetInstanceID();
                if (sAlreadyPatched.Contains(id)) continue;

                string key = entry.ModuleDefRef.RuntimeKey as string;
                if (string.IsNullOrEmpty(key)) continue;

                string path = ResolvePath(key);
                if (string.IsNullOrEmpty(path)) continue;

                string lower = path.ToLower();
                bool isDataDrive = lower.Contains("data_drive") || lower.Contains("datadrive");
                bool isHelmet = lower.Contains("helmet");

                if (!isDataDrive && !isHelmet)
                {
                    float before = entry.Weight;
                    entry.Weight = 0f;
                    Plugin.Log.LogInfo($"[DataDriveSupremacy] {path}: weight {before} -> {entry.Weight}");
                }

                sAlreadyPatched.Add(id);
            }
        }

        private static string ResolvePath(string guid)
        {
            try
            {
                var op = Addressables.LoadResourceLocationsAsync(guid);
                var locations = op.WaitForCompletion();
                string result = (locations != null && locations.Count > 0) ? locations[0].InternalId : null;
                Addressables.Release(op);
                return result;
            }
            catch
            {
                return null;
            }
        }
    }

    // Patch 2 — gates credit drives so they only spawn when all narrative lore for the
    // current ship archetype has already been collected.
    //
    // How the game handles the credit-drive-vs-data-drive split (from NarrativeItemComponent.cs):
    //
    //   1. The ship generates. The narrative device hardpoint and the credit drive hardpoint
    //      both roll independently — they are separate slots.
    //   2. After the ship finishes spawning, every NarrativeItemComponent on the ship
    //      calls NarrativeItemSystem.GetRandomValidNarrativeEntry(). If that returns null
    //      (no uncollected lore entries match this ship's archetype), the component calls
    //      DestroyObjectsSystem.TryMarkObjectForDestruction on its own parent — the data
    //      drive vanishes. The credit drive from its own hardpoint then remains in the scene.
    //   3. If lore IS still available the data drive stays, and the credit drive was also
    //      already sitting in the scene from its own separate hardpoint.
    //
    // This patch closes that gap: during ship generation (step 1), if lore is still
    // available we zero every entry in ML_CreditDrives so the credit drive slot produces
    // nothing. When lore is exhausted we restore the original weights so the normal
    // property-weighted roll runs and a credit drive spawns as the fallback.
    //
    // Timing caveat: NarrativeItemSystem.mCurrentlySpawnableEntries is refreshed per-ship
    // at spawn-COMPLETE time, but this patch fires DURING generation (before that). So the
    // lore check here reflects the PREVIOUS ship's archetype state, not the current one.
    // In practice this means a one-ship lag at the moment lore transitions from available
    // to exhausted. For steady-state play on a single ship type there is no lag.
    [HarmonyPatch(typeof(ModuleListAsset), "GenerateRandomModulesAsync")]
    class Patch_CreditDriveFallbackOnly
    {
        private const string kTargetList = "ML_CreditDrives";
        private const bool kDryRun = false;

        private static readonly Dictionary<int, float> sOriginalWeights = new Dictionary<int, float>();
        private static string sLastLoggedShipKey = "[[init]]";

        private static readonly FieldInfo sTotalWeightField =
            typeof(NarrativeItemSystem).GetField("mCurrentTotalCategoryWeight",
                BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo sSpawnableEntriesField =
            typeof(NarrativeItemSystem).GetField("mCurrentlySpawnableEntries",
                BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo sDataField =
            typeof(ModuleListAsset).GetField("m_Data",
                BindingFlags.NonPublic | BindingFlags.Instance);

        static void Prefix(ModuleListAsset __instance)
        {
            if (__instance.name != kTargetList) return;
            var data = sDataField.GetValue(__instance) as ModuleListData;
            if (data?.ModuleEntryContainer == null) return;

            for (int i = 0; i < data.ModuleEntryContainer.Count; i++)
            {
                var entry = data.ModuleEntryContainer[i] as ModuleEntryBase;
                if (entry == null) continue;
                int id = entry.GetInstanceID();
                if (!sOriginalWeights.ContainsKey(id))
                    sOriginalWeights[id] = entry.Weight;
            }

            bool loreAvailable = CheckLoreAvailable(out int entryCount, out int categoryCount);
            TryGetShipInfo(out var root, out string archetype, out string shipName);

            // Deduplicate: only log once per ShipRoot instance (i.e. once per ship in the bay)
            string shipKey = root != null ? root.GetInstanceID().ToString() : "[[no-ship]]";
            if (shipKey != sLastLoggedShipKey)
            {
                sLastLoggedShipKey = shipKey;

                if (root == null)
                {
                    Plugin.Log.LogInfo(
                        "[DataDriveSupremacy] No ship currently in bay (first ship of session). " +
                        "NarrativeItemSystem not yet initialized — lore check is unreliable. " +
                        (kDryRun ? "[DRY RUN] " : "") + "Defaulting to: allow credit drives.");
                }
                else
                {
                    Plugin.Log.LogInfo(
                        $"[DataDriveSupremacy] Ship in bay: {archetype} (\"{shipName}\") — " +
                        (loreAvailable
                            ? $"{entryCount} lore entr{(entryCount == 1 ? "y" : "ies")} across " +
                              $"{categoryCount} categor{(categoryCount == 1 ? "y" : "ies")} remaining."
                            : "lore exhausted.") +
                        (kDryRun
                            ? (loreAvailable
                                ? " [DRY RUN] Would suppress credit drives."
                                : " [DRY RUN] Would allow credit drives.")
                            : (loreAvailable
                                ? " Suppressing credit drives."
                                : " Allowing credit drives.")));
                }
            }

            if (!kDryRun)
            {
                for (int i = 0; i < data.ModuleEntryContainer.Count; i++)
                {
                    var entry = data.ModuleEntryContainer[i] as ModuleEntryBase;
                    if (entry == null) continue;
                    if (loreAvailable)
                    {
                        entry.Weight = 0f;
                    }
                    else
                    {
                        int id = entry.GetInstanceID();
                        if (sOriginalWeights.TryGetValue(id, out float original))
                            entry.Weight = original;
                    }
                }
            }
        }

        private static bool CheckLoreAvailable(out int entryCount, out int categoryCount)
        {
            entryCount = 0;
            categoryCount = 0;
            try
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world == null) return false;
                var system = world.GetExistingSystem<NarrativeItemSystem>();
                if (system == null) return false;
                if (sTotalWeightField == null || sSpawnableEntriesField == null) return false;
                float totalWeight = (float)sTotalWeightField.GetValue(system);
                if (totalWeight <= 0f) return false;
                if (!(sSpawnableEntriesField.GetValue(system) is System.Collections.IDictionary entries) || entries.Count == 0)
                    return false;
                categoryCount = entries.Count;
                foreach (object val in entries.Values)
                    if (val is System.Collections.ICollection col)
                        entryCount += col.Count;
                return entryCount > 0;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[DataDriveSupremacy] Lore check failed: {ex.Message}. Defaulting to: allow credit drives.");
                return false;
            }
        }

        private static void TryGetShipInfo(out ShipRoot root, out string archetype, out string shipName)
        {
            root = null;
            archetype = "Unknown";
            shipName = "Unknown";
            try
            {
                root = UnityEngine.Object.FindObjectOfType<ShipRoot>();
                var preview = root?.SourceShipPreview;
                if (preview == null) return;
                archetype = string.IsNullOrEmpty(preview.Archetype) ? "Unknown" : preview.Archetype;
                Main.Instance?.LocalizationService?.TryLocalize(archetype, out archetype);
                shipName = string.IsNullOrEmpty(preview.ShipName) ? "Unknown" : preview.ShipName;
            }
            catch { root = null; }
        }
    }


    [HarmonyPatch(typeof(NarrativeItemSystem), "RefreshSpawnableEntries")]
    class Patch_LogLoreBreakdown
    {
        // Builds up as you play — archetypes you haven't seen yet show as "? (guid...)"
        private static readonly Dictionary<string, string> sGuidToArchetype = new Dictionary<string, string>();

        static void Postfix(ShipRoot shipRoot)
        {
            try
            {
                string guid = shipRoot?.SourceShipPreview?.ConstructionAssetRef?.RuntimeKey as string;
                string archetype = shipRoot?.SourceShipPreview?.Archetype;
                Main.Instance?.LocalizationService?.TryLocalize(archetype, out archetype);
                string shipName = shipRoot?.SourceShipPreview?.ShipName ?? "?";

                if (!string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(archetype))
                    sGuidToArchetype[guid] = archetype;

                var allEntries = Main.Instance.MainSettings.NarrativeSettings.NarrativeAssetList.NarrativeEntries;
                if (allEntries == null || allEntries.Count == 0) return;

                var inv = PlayerProfileService.Instance.Profile.NarrativeInventory;
                var collectedU = inv.CollectedUnidentifiedNarrativeEntries;
                var collectedI = inv.CollectedIdentifiedNarrativeEntries;

                var counts = new Dictionary<string, (int total, int remaining)>();

                foreach (var entry in allEntries)
                {
                    if (!entry.OneTimeCollectible) continue;

                    bool collected =
                        (collectedU.TryGetValue(entry.ID, out var l1) && l1.Count > 0) ||
                        (collectedI.TryGetValue(entry.ID, out var l2) && l2.Count > 0);

                    IEnumerable<string> keys;
                    if (entry.PossibleShipsRef == null || entry.PossibleShipsRef.Length == 0)
                    {
                        keys = new[] { "Any archetype" };
                    }
                    else
                    {
                        keys = entry.PossibleShipsRef
                            .Select(r => r?.RuntimeKey as string)
                            .Where(g => !string.IsNullOrEmpty(g))
                            .Select(g => sGuidToArchetype.TryGetValue(g, out var n) ? n : $"? ({g.Substring(0, 8)})")
                            .Distinct();
                    }

                    foreach (var key in keys)
                    {
                        counts.TryGetValue(key, out var cur);
                        counts[key] = (cur.total + 1, cur.remaining + (collected ? 0 : 1));
                    }
                }

                Plugin.Log.LogInfo($"[DataDriveSupremacy] === Lore status ({archetype ?? "?"} \"{shipName}\" spawned) ===");
                foreach (var kv in counts.OrderByDescending(x => x.Value.remaining).ThenBy(x => x.Key))
                {
                    string status = kv.Value.remaining == 0 ? " [EXHAUSTED]" : "";
                    Plugin.Log.LogInfo($"  {kv.Key}: {kv.Value.remaining}/{kv.Value.total} remaining{status}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[DataDriveSupremacy] Lore breakdown failed: {ex.Message}");
            }
        }
    }
}