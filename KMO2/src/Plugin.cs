using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace KMO2
{
    internal static class PackInfo
    {
        public const string GUID = "com.ial.kmo2";
        public const string Name = "KMO2";
        public const string Version = "1.1.4";
        public const string TarusPackGuid = "com.ial.tarusweaponpack";
    }

#if !TARUS_PACK
    [BepInPlugin(PackInfo.GUID, PackInfo.Name, PackInfo.Version)]
    [BepInDependency(PackInfo.TarusPackGuid, BepInDependency.DependencyFlags.SoftDependency)]
#endif
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static ConfigEntry<KeyCode> ChargeToggleKey;
        private static float _nextEnsure;

        private void Awake()
        {
            Log = Logger;
            if (PackOwnsUs())
            {
                enabled = false;
                Log.LogInfo("TarusWeaponPack present — standalone skipped");
                return;
            }
            Instance = this;
            Boot(Logger, Config, "General");
        }

        internal static bool PackOwnsUs()
        {
            try
            {
                if (Chainloader.PluginInfos == null)
                    return false;
                return Chainloader.PluginInfos.ContainsKey(PackInfo.TarusPackGuid);
            }
            catch
            {
                return false;
            }
        }

        internal static void Boot(ManualLogSource log, ConfigFile cfg, string configSection)
        {
            Log = log;
            ChargeToggleKey = cfg.Bind(configSection, "ChargeToggleKey", KeyCode.K,
                "Cycle KMO-2 GUIDED / CHG+GUIDE. Same key as 57mm and 106mm; only the selected station cycles.");
            if (ConflictsWithOritasy(ChargeToggleKey.Value))
                ChargeToggleKey.Value = KeyCode.K;
            Harmony harmony = new Harmony(PackInfo.GUID);
            PatchOwnAssembly(harmony);
            Log.LogInfo(PackInfo.Name + " v" + PackInfo.Version
                + " standalone KMO-2, forced on every aircraft outer pylon, mode key="
                + ChargeToggleKey.Value);
        }

        internal static void TickFrame()
        {
            try
            {
                Hud.Tick();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("KMO2 hud: " + ex.Message);
            }
            try
            {
                GBurst.Warm();
            }
            catch
            {
            }
            try
            {
                Munition.TickInject();
            }
            catch
            {
            }
            if (Time.unscaledTime < _nextEnsure)
                return;
            _nextEnsure = Time.unscaledTime + (Munition.Injected ? 4f : 0.5f);
            try
            {
                Munition.Ensure();
                LaunchFx.Warm();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("KMO2 ensure: " + ex.Message);
            }
        }

        internal static void DrawFrame()
        {
            try
            {
                Hud.DrawImgui();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("KMO2 OnGUI: " + ex.Message);
            }
        }

        internal static bool ConflictsWithOritasy(KeyCode k)
        {
            if (k == KeyCode.None)
                return true;
            if (k == KeyCode.F1 || k == KeyCode.F2 || k == KeyCode.F3 || k == KeyCode.F4
                || k == KeyCode.F5 || k == KeyCode.F6 || k == KeyCode.F7 || k == KeyCode.F8
                || k == KeyCode.F10 || k == KeyCode.F11)
                return true;
            if (k == KeyCode.Backslash || k == KeyCode.BackQuote
                || k == KeyCode.Delete || k == KeyCode.PageDown || k == KeyCode.Insert
                || k == KeyCode.Semicolon || k == KeyCode.Quote
                || k == KeyCode.O || k == KeyCode.P || k == KeyCode.U
                || k == KeyCode.G || k == KeyCode.L || k == KeyCode.Backspace)
                return true;
            return false;
        }

        internal static string ToggleHint()
        {
            if (ChargeToggleKey == null)
                return "[K]";
            return "[" + ChargeToggleKey.Value.ToString() + "]";
        }

        internal static bool IsNavalHardpoint(HardpointSet hs)
        {
            if (hs == null || string.IsNullOrEmpty(hs.name))
                return false;
            string n = hs.name;
            if (n.IndexOf("VLS", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Naval", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Cell", StringComparison.OrdinalIgnoreCase) >= 0
                && (n.IndexOf("Launch", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Mk", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            return false;
        }

        internal static bool IsShipWeaponManager(WeaponManager wm)
        {
            if (wm == null)
                return false;
            try
            {
                if (wm.GetComponentInParent<Aircraft>() != null)
                    return false;
            }
            catch
            {
            }
            return Pylon.AircraftOf(wm) == null;
        }

        private void Update()
        {
            TickFrame();
        }

        private void OnGUI()
        {
            DrawFrame();
        }

        private static void PatchOwnAssembly(Harmony harmony)
        {
            string ownNs = typeof(Plugin).Namespace;
            Type[] types = Assembly.GetExecutingAssembly().GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                Type t = types[i];
                if (t == null || !t.IsClass)
                    continue;
                if (t.Namespace != ownNs)
                    continue;
                object[] onType = t.GetCustomAttributes(typeof(HarmonyPatch), false);
                if (onType == null || onType.Length == 0)
                    continue;
                try
                {
                    MethodInfo tm = t.GetMethod("TargetMethod",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                    if (tm != null && tm.Invoke(null, null) == null)
                    {
                        Log.LogWarning("Skip Harmony " + t.Name + ": TargetMethod=null");
                        continue;
                    }
                    harmony.CreateClassProcessor(t).Patch();
                }
                catch (Exception ex)
                {
                    Log.LogWarning("Harmony " + t.Name + ": " + ex.Message);
                }
            }
        }

        internal static Encyclopedia GetEncyclopedia()
        {
            try
            {
                Encyclopedia via = Encyclopedia.i;
                if (via != null && via.weaponMounts != null && via.weaponMounts.Count > 0)
                    return via;
            }
            catch
            {
            }
            Encyclopedia[] all = Resources.FindObjectsOfTypeAll<Encyclopedia>();
            if (all == null || all.Length == 0)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].weaponMounts != null && all[i].weaponMounts.Count > 0)
                    return all[i];
            }
            return all[0];
        }

        internal static T TryAddBehaviour<T>(GameObject go) where T : MonoBehaviour
        {
            if (go == null)
                return null;
            try
            {
                T existing = go.GetComponent<T>();
                if (existing != null)
                    return existing;
                return go.AddComponent<T>();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("KMO2 AddComponent " + typeof(T).Name + ": " + ex.Message);
                return null;
            }
        }
    }
}
