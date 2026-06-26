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
    [BepInPlugin("com.earthling.datadrivesupremacy", "DataDriveSupremacy", "1.2.0")]
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

    // Patch 1 — narrative-device hardpoints can only produce data drives (or helmets).
    //
    // ML_FoundNarrativeDevices_RandomRotation and ML_FoundNarrativeDevices_NoRotation are the
    // ModuleListAssets where bunny, data tablet, data drive, and helmet compete. We zero the
    // bunny and data tablet so only the data drive (and the rare ghost-ship helmet, left alone)
    // can win. We also stash the data-drive prefab reference here so Patch 3 can re-spawn one
    // when swapping out a credit drive.
    [HarmonyPatch(typeof(ModuleListAsset), "GenerateRandomModulesAsync")]
    class Patch_ForceDataDriveOnly
    {
        private static readonly string[] sTargetContainerNames =
        {
            "ML_FoundNarrativeDevices_RandomRotation",
            "ML_FoundNarrativeDevices_NoRotation"
        };

        // Cache instance IDs already processed so we don't redo work on every roll.
        private static readonly HashSet<int> sAlreadyPatched = new HashSet<int>();

        // The data-drive prefab reference, captured the first time we see it. Patch 3 uses it
        // to instantiate a data drive when swapping out a credit drive.
        internal static AssetReferenceGameObject sDataDriveRef;

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
                else if (isDataDrive && sDataDriveRef == null)
                {
                    sDataDriveRef = entry.ModuleDefRef;
                    Plugin.Log.LogInfo($"[DataDriveSupremacy] Data-drive prefab ref captured for swaps: {path}");
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

    // Patch 2 — records the credit-drive prefab name(s) during catalogue generation so Patch 3
    // knows what to look for in a spawned ship. Generation always runs before spawn, so the names
    // are ready in time.
    [HarmonyPatch(typeof(ModuleListAsset), "GenerateRandomModulesAsync")]
    class Patch_RecordCreditDrivePrefabs
    {
        private const string kTargetList = "ML_CreditDrives";

        // Prefab file names (no extension) of every credit-drive entry seen. Shared with Patch 3.
        internal static readonly HashSet<string> sCreditDrivePrefabNames = new HashSet<string>();

        private static readonly FieldInfo sDataField =
            typeof(ModuleListAsset).GetField("m_Data", BindingFlags.NonPublic | BindingFlags.Instance);

        static void Prefix(ModuleListAsset __instance)
        {
            if (__instance.name != kTargetList) return;
            var data = sDataField.GetValue(__instance) as ModuleListData;
            if (data?.ModuleEntryContainer == null) return;

            for (int i = 0; i < data.ModuleEntryContainer.Count; i++)
            {
                var entry = data.ModuleEntryContainer[i] as ModuleEntryDefinition;
                if (entry?.ModuleDefRef == null) continue;

                string key = entry.ModuleDefRef.RuntimeKey as string;
                if (string.IsNullOrEmpty(key)) continue;

                string path = ResolvePath(key);
                if (string.IsNullOrEmpty(path)) continue;

                string prefabName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(prefabName) && sCreditDrivePrefabNames.Add(prefabName))
                    Plugin.Log.LogInfo($"[DataDriveSupremacy] Credit-drive prefab registered: \"{prefabName}\"");
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

    // Patch 3 — the core behaviour. At spawn-complete (postfix of RefreshSpawnableEntries, which
    // has just rebuilt the lore state for THIS ship), if lore is still available we replace every
    // active credit drive with a data drive in the same spot. If lore is exhausted we leave the
    // credit drive as the reward.
    //
    // Credit drives and data drives are mutually exclusive per ship, so a credit drive showing up
    // on a lore-available ship is exactly the case we want to convert. The data drive collects and
    // banks lore normally (note: a swapped drive lacks the first-person grab animation — purely
    // cosmetic, since collection runs through the prefab's NarrativeItemComponent).
    [HarmonyPatch(typeof(NarrativeItemSystem), "RefreshSpawnableEntries")]
    class Patch_SwapCreditDriveForData
    {
        private static readonly FieldInfo sTotalWeightField =
            typeof(NarrativeItemSystem).GetField("mCurrentTotalCategoryWeight",
                BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo sSpawnableEntriesField =
            typeof(NarrativeItemSystem).GetField("mCurrentlySpawnableEntries",
                BindingFlags.NonPublic | BindingFlags.Instance);

        static void Postfix(NarrativeItemSystem __instance, ShipRoot shipRoot)
        {
            try
            {
                if (shipRoot == null) return;

                bool loreAvailable = LoreAvailable(__instance);
                var dataRef = Patch_ForceDataDriveOnly.sDataDriveRef;

                foreach (var t in shipRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.gameObject.activeInHierarchy) continue;

                    string clean = t.gameObject.name.Replace("(Clone)", "").Trim();
                    if (!Patch_RecordCreditDrivePrefabs.sCreditDrivePrefabNames.Contains(clean)) continue;

                    if (!loreAvailable)
                    {
                        // No lore left for this ship — the credit drive stays as the reward.
                        Plugin.Log.LogInfo("[DataDriveSupremacy] Lore exhausted for this ship — kept the credit drive.");
                        continue;
                    }

                    if (dataRef == null) continue;

                    // Drop a data drive where the credit drive was, then remove the credit drive.
                    Vector3 pos = t.position;
                    Quaternion rot = t.rotation;
                    Transform parent = t.parent != null ? t.parent : shipRoot.transform;

                    try
                    {
                        Addressables.InstantiateAsync(dataRef, pos, rot, parent, true);
                        DestroyObjectsSystem.TryMarkObjectForDestruction(t.gameObject, 0f);
                        Plugin.Log.LogInfo("[DataDriveSupremacy] Lore available — swapped a credit drive for a data drive.");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[DataDriveSupremacy] Credit->data swap failed: {ex.Message}. Left the credit drive.");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[DataDriveSupremacy] Swap pass failed: {ex.Message}");
            }
        }

        // Mirrors NarrativeItemSystem.GetRandomValidNarrativeEntry's own null check.
        private static bool LoreAvailable(NarrativeItemSystem system)
        {
            try
            {
                float totalWeight = (float)(sTotalWeightField?.GetValue(system) ?? 0f);
                var dict = sSpawnableEntriesField?.GetValue(system) as System.Collections.IDictionary;
                return totalWeight > 0f && dict != null && dict.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
