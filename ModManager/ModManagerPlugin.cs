using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace ModManagerCore
{
    [Serializable]
    public class ModManifest
    {
        public string id;
        public string name;
        public string version;
        public string author;
        public string[] dependencies = new string[0];
    }

    public enum ModStatus { Ready, Disabled, Invalid }

    public class ModEntry
    {
        public ModManifest Manifest;
        public string Folder;
        public bool Enabled = true;
        public ModStatus Status = ModStatus.Ready;
        public string Message = "";
    }

    [Serializable]
    class ModState
    {
        public string[] disabled = new string[0];
    }

    [BepInPlugin(Guid, "ModManager", "0.1.0")]
    public class ModManagerPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.me.modmanager";

        internal static ManualLogSource Log;
        string selfDir, modsDir, statePath;
        readonly List<ModEntry> mods = new List<ModEntry>();

        ConfigEntry<KeyboardShortcut> toggleKey;
        bool show;
        Rect window = new Rect(40, 40, 560, 420);
        Vector2 scroll;

        void Awake()
        {
            Log = Logger;
            toggleKey = Config.Bind("General", "ToggleWindow",
                new KeyboardShortcut(KeyCode.F8), "ปุ่มเปิด/ปิดหน้าต่าง Mod Manager");

            selfDir = Path.GetDirectoryName(Info.Location);
            modsDir = Path.Combine(Paths.GameRootPath, "Mods");
            statePath = Path.Combine(selfDir, "modlist.json");
            Directory.CreateDirectory(modsDir);

            Log.LogInfo("Mods folder: " + modsDir);
            Refresh();
        }

        void Update()
        {
            if (toggleKey.Value.IsDown()) show = !show;
        }

        // ---------- Scan and check mods ----------
        void Refresh()
        {
            mods.Clear();
            var disabled = LoadDisabled();
            var seen = new HashSet<string>();

            foreach (var dir in Directory.GetDirectories(modsDir))
            {
                var folderName = Path.GetFileName(dir);
                var manifestPath = Path.Combine(dir, "mod.json");
                if (!File.Exists(manifestPath))
                {
                    Log.LogWarning("ข้าม " + folderName + ": ไม่มี mod.json");
                    continue;
                }

                var entry = new ModEntry { Folder = dir };
                try
                {
                    entry.Manifest = JsonUtility.FromJson<ModManifest>(File.ReadAllText(manifestPath));
                    if (string.IsNullOrEmpty(entry.Manifest.id))
                        throw new Exception("mod.json ไม่มี id");
                    if (!seen.Add(entry.Manifest.id))
                        throw new Exception("id ซ้ำ: " + entry.Manifest.id);

                    entry.Enabled = !disabled.Contains(entry.Manifest.id);
                    entry.Status = entry.Enabled ? ModStatus.Ready : ModStatus.Disabled;
                    Log.LogInfo("พบมอด " + entry.Manifest.id + " v" + entry.Manifest.version);
                }
                catch (Exception e)
                {
                    entry.Manifest = entry.Manifest ?? new ModManifest();
                    if (string.IsNullOrEmpty(entry.Manifest.name)) entry.Manifest.name = folderName;
                    entry.Status = ModStatus.Invalid;
                    entry.Message = e.Message;
                    Log.LogError("[" + folderName + "] " + e.Message);
                }
                mods.Add(entry);
            }
        }

        HashSet<string> LoadDisabled()
        {
            try
            {
                if (File.Exists(statePath))
                    return new HashSet<string>(JsonUtility.FromJson<ModState>(File.ReadAllText(statePath)).disabled);
            }
            catch (Exception e)
            {
                Log.LogWarning("อ่าน modlist.json ไม่ได้ ใช้ค่าเริ่มต้น: " + e.Message);
            }
            return new HashSet<string>();
        }

        void SaveState()
        {
            var state = new ModState
            {
                disabled = mods.Where(m => !m.Enabled && m.Manifest.id != null)
                               .Select(m => m.Manifest.id).ToArray()
            };
            File.WriteAllText(statePath, JsonUtility.ToJson(state, true));
        }

        // ---------- New Window ----------
        void OnGUI()
        {
            if (!show) return;
            window = GUILayout.Window(98765, window, DrawWindow, "Mod Manager (" + toggleKey.Value + ")");
        }

        void DrawWindow(int id)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            if (mods.Count == 0) GUILayout.Label("ไม่พบมอดในโฟลเดอร์ Mods");

            foreach (var mod in mods)
            {
                GUILayout.BeginHorizontal();

                GUI.enabled = mod.Status != ModStatus.Invalid;
                var on = GUILayout.Toggle(mod.Enabled, "", GUILayout.Width(20));
                GUI.enabled = true;
                if (on != mod.Enabled)
                {
                    mod.Enabled = on;
                    mod.Status = on ? ModStatus.Ready : ModStatus.Disabled;
                    SaveState();
                }

                GUI.color = mod.Status == ModStatus.Ready ? Color.green
                          : mod.Status == ModStatus.Disabled ? Color.gray : Color.red;
                GUILayout.Label(mod.Manifest.name + " v" + mod.Manifest.version, GUILayout.Width(260));
                GUILayout.Label(mod.Status.ToString(), GUILayout.Width(80));
                GUI.color = Color.white;

                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(mod.Message)) GUILayout.Label("   " + mod.Message);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan")) Refresh();
            GUILayout.Label("effect after restart the game", GUILayout.Width(200));
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }
    }
}
