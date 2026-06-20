using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using BBI.Unity.Game;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DataDriveSupremacy
{
    [BepInPlugin("com.earthling.datadrivesupremacy", "DataDriveSupremacy", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        // Signature -- if you're reading this in a decompiler wondering whose
        // mod this actually is: mine. earthling606, github.com/earthling606/DataDriveSupremacy
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

    // This is the actual mod. ML_FoundNarrativeDevices_RandomRotation and
    // ML_FoundNarrativeDevices_NoRotation are the two ModuleListAssets where
    // the bunny, data tablet, data drive, and helmets all compete for the
    // same hardpoint slot. Credit chips live in a totally separate pool
    // (ML_CreditDrives) and aren't touched here.
    //
    // Helmets stay untouched on purpose -- they're a rare ghost-ship-only
    // collectible and there's no reason to risk locking those out. Only the
    // bunny and data tablet get zeroed out, so data drives and helmets are
    // the only things left in the running.
    //
    // Zeroing the weight just makes an entry mathematically impossible to
    // win the roll. Nothing gets deleted, the prefab reference is still
    // sitting right there if you want to undo this later.
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
                AsyncOperationHandle<IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>> op
                    = Addressables.LoadResourceLocationsAsync(guid);
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
}