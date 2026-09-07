using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RailcannonPod
{
    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    internal static class Patch_Hardpoint_SpawnMount
    {
        private static void Postfix(WeaponMount weaponMount, GameObject __result)
        {
            if (__result == null || !Pod.IsOurs(weaponMount))
                return;
            Pod.LightenSpawned(__result);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class Patch_WeaponManager_Awake
    {
        private static void Postfix(WeaponManager __instance)
        {
            if (!Pod.Injected)
                return;
            Pod.InjectIntoWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponSelector), "Initialize")]
    [HarmonyPatch(new Type[] { typeof(Aircraft), typeof(HardpointSet), typeof(FactionHQ), typeof(Airbase) })]
    internal static class Patch_WeaponSelector_Initialize
    {
        private static void Prefix(Aircraft aircraft)
        {
            Pod.Ensure();
            if (aircraft != null && aircraft.weaponManager != null)
                Pod.InjectIntoWeaponManager(aircraft.weaponManager);
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "VetWeapon")]
    internal static class Patch_VetWeapon
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WeaponMount requestedMount, HardpointSet hardpointSet, ref bool __result, ref string failReason, ref int failCost)
        {
            if (!Pod.IsOurs(requestedMount))
                return true;
            if (Plugin.IsNavalHardpoint(hardpointSet))
            {
                __result = false;
                failReason = "Railcannon is not for naval cells";
                failCost = 0;
                return false;
            }
            __result = true;
            failReason = null;
            failCost = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedHardpoint")]
    internal static class Patch_MountAllowedHardpoint
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WeaponMount mount, HardpointSet hardpointSet, ref bool __result)
        {
            if (!Pod.IsOurs(mount))
                return true;
            __result = !Plugin.IsNavalHardpoint(hardpointSet);
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "GetAvailableWeaponsNonAlloc")]
    internal static class Patch_GetAvailableWeapons
    {
        private static readonly HashSet<WeaponMount> HaveScratch = new HashSet<WeaponMount>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HardpointSet hardpointSet, List<WeaponMount> outAvailable)
        {
            if (outAvailable == null)
                return;
            HaveScratch.Clear();
            for (int i = 0; i < outAvailable.Count; i++)
            {
                if (outAvailable[i] != null)
                    HaveScratch.Add(outAvailable[i]);
            }
            Pod.Ensure();
            Pod.FilterAvailable(hardpointSet, outAvailable, HaveScratch);
        }
    }

    [HarmonyPatch]
    internal static class Patch_Gun_Fire
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Gun), "Fire", new Type[]
            {
                typeof(Unit),
                typeof(Unit),
                typeof(Vector3),
                typeof(WeaponStation),
                typeof(GlobalPosition)
            });
        }

        private static bool Prefix(Unit firingUnit, WeaponStation weaponStation)
        {
            if (weaponStation == null || weaponStation.WeaponInfo == null)
                return true;
            if (!Pod.IsOurs(weaponStation.WeaponInfo))
                return true;
            Aircraft ac = Pod.AircraftOf(firingUnit);
            PowerSupply ps = Pod.PowerOf(firingUnit);
            Pod.EnsureCapacitor(ac, ps);
            if (!Pod.HasShotEnergy(ac, ps))
                return false;
            Pod.ApplyGuidedFromStation(weaponStation);
            return true;
        }
    }

    [HarmonyPatch(typeof(Gun), "SpawnBullet")]
    [HarmonyPatch(new Type[] { typeof(float) })]
    internal static class Patch_Gun_SpawnBullet
    {
        private static bool Prefix(Gun __instance)
        {
            if (!Pod.IsOurs(__instance))
                return true;
            try
            {
                Unit owner = __instance.attachedUnit;
                Aircraft ac = Pod.AircraftOf(owner);
                PowerSupply ps = Pod.PowerOf(owner);
                Pod.TryConsumeShot(ac, ps);
                LaunchFx.Prepare(__instance);
                Pod.ApplyGuidedProjectile(__instance);
                Pod.ApplyShotBallistics(__instance);
            }
            catch
            {
            }
            return true;
        }

        private static void Postfix(Gun __instance)
        {
            if (__instance == null || !Pod.IsOurs(__instance))
                return;
            try { LaunchFx.Burst(__instance); }
            catch { }
            Pod.RestoreShotBallistics(__instance);
        }
    }

    [HarmonyPatch(typeof(ChargeIndicator), "ChargeIndicator_OnSetAircraft")]
    internal static class Patch_ChargeHud
    {
        private static void Postfix(ChargeIndicator __instance, CombatHUD sender)
        {
            if (__instance == null || sender == null || sender.aircraft == null)
                return;
            if (!Pod.CurrentStationIsOurs(sender.aircraft))
                return;
            __instance.enabled = true;
            if (__instance.gameObject != null)
                __instance.gameObject.SetActive(true);
        }
    }

    [HarmonyPatch]
    internal static class Patch_CombatHUD_LateUpdate
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CombatHUD), "LateUpdate");
        }

        private static void Postfix(CombatHUD __instance)
        {
            try
            {
                Hud.OnCombatHud(__instance);
            }
            catch
            {
            }
        }
    }
}
