using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace ModManagerCore
{
    [Serializable]
    public class UpgradeDef
    {
        public string id;              // don't need namespace it will "modid:" for you
        public string familiar;        // name of enum FamiliarManager.allFamiliars e.g., cat
        public string stat;            // name of enum FamiliarStat e.g., MoveSpeed
        public string modification;    // name of enum Modifier e.g., PercentageInc
        public float value;
        public string descriptionKey;
    }

    [Serializable]
    class UpgradeFile
    {
        public UpgradeDef[] upgrades = new UpgradeDef[0];
    }

    public static class UpgradeRegistry
    {
        class Pending
        {
            public string ModId;
            public UpgradeDef Def;
            public FamiliarUpgrade Asset; // created ScriptableObject, null until injected
        }

        static readonly List<Pending> pending = new List<Pending>();

        // ---------- read upgrades.json ของมอด ----------
        public static void LoadFrom(string modId, string folder)
        {
            var path = Path.Combine(folder, "upgrades.json");
            if (!File.Exists(path)) return;

            try
            {
                var file = JsonUtility.FromJson<UpgradeFile>(File.ReadAllText(path));
                foreach (var def in file.upgrades)
                {
                    if (string.IsNullOrEmpty(def.id))
                    {
                        ModManagerPlugin.Log.LogWarning("[" + modId + "] has upgrade without id, skipping");
                        continue;
                    }
                    pending.Add(new Pending { ModId = modId, Def = def });
                }
                ModManagerPlugin.Log.LogInfo("[" + modId + "] read " + file.upgrades.Length + " upgrades");
            }
            catch (Exception e)
            {
                ModManagerPlugin.Log.LogError("[" + modId + "] failed to read upgrades.json: " + e.Message);
            }
        }

        // ---------- patch และฉีดทันทีถ้าเกมสร้าง storage  ----------
        public static void Install()
        {
            if (pending.Count == 0) return;

            new Harmony(ModManagerPlugin.Guid + ".upgrades").PatchAll(typeof(StorageInitPatch));

            if (FamiliarStorage.Instance)
                InjectInto(FamiliarStorage.Instance);
        }

        internal static void InjectInto(FamiliarStorage storage)
        {
            var map = Traverse.Create(storage).Field("_upgrades")
                .GetValue<Dictionary<FamiliarManager.allFamiliars, List<FamiliarUpgrade>>>();
            if (map == null)
            {
                ModManagerPlugin.Log.LogError("Failed to find _upgrades in FamiliarStorage (game may have updated)");
                return;
            }

            foreach (var p in pending)
            {
                try
                {
                    if (!p.Asset) p.Asset = Build(p);

                    var familiar = p.Asset.TargetFamiliar;
                    List<FamiliarUpgrade> list;
                    if (!map.TryGetValue(familiar, out list))
                    {
                        list = new List<FamiliarUpgrade>();
                        map[familiar] = list;
                    }

                    if (list.Exists(u => u && u.Id == p.Asset.Id)) continue;
                    list.Add(p.Asset);
                    ModManagerPlugin.Log.LogInfo("Injected upgrade " + p.Asset.Id + " into " + familiar);
                }
                catch (Exception e)
                {
                    ModManagerPlugin.Log.LogError("[" + p.ModId + "] failed to inject '" + p.Def.id + "': " + e.Message);
                }
            }
        }

        // ---------- create FamiliarUpgrade (ScriptableObject) from JSON ----------
        static FamiliarUpgrade Build(Pending p)
        {
            var d = p.Def;
            var fullId = d.id.Contains(":") ? d.id : p.ModId + ":" + d.id;

            var up = ScriptableObject.CreateInstance<FamiliarUpgrade>();
            up.name = fullId;
            up.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var t = Traverse.Create(up);
            t.Field("upgradeId").SetValue(fullId);
            t.Field("targetFamiliar").SetValue(ParseEnum(typeof(FamiliarManager.allFamiliars), d.familiar, "familiar"));
            t.Field("targetStat").SetValue(ParseEnum(FieldType("targetStat"), d.stat, "stat"));
            t.Field("modificationType").SetValue(ParseEnum(FieldType("modificationType"), d.modification, "modification"));
            t.Field("value").SetValue(d.value);
            t.Field("descriptionKey").SetValue(d.descriptionKey ?? "");

            // place = free unlock (still don't know the ingredient structure)
            var ingType = FieldType("ingredients");
            t.Field("ingredients").SetValue(Array.CreateInstance(ingType.GetElementType(), 0));

            return up;
        }

        static Type FieldType(string name)
        {
            return AccessTools.Field(typeof(FamiliarUpgrade), name).FieldType;
        }

        static object ParseEnum(Type type, string text, string what)
        {
            try { return Enum.Parse(type, text ?? "", true); }
            catch
            {
                throw new Exception(what + " '" + text + "' is invalid. Valid values are: " +
                                    string.Join(", ", Enum.GetNames(type)));
            }
        }

        // ---------- for testing: unlock all injected upgrades (must be in the hub) ----------
        public static void UnlockAllInjected()
        {
            var storage = FamiliarStorage.Instance;
            if (!storage) return;
            foreach (var p in pending)
                if (p.Asset) storage.UnlockFamiliarUpgrade(p.Asset);
        }
    }

    [HarmonyPatch(typeof(FamiliarStorage), "Initialize")]
    static class StorageInitPatch
    {
        static void Postfix(FamiliarStorage __instance)
        {
            UpgradeRegistry.InjectInto(__instance);
        }
    }
}
