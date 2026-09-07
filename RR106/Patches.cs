using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RR106
{
    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    internal static class Patch_Hardpoint_SpawnMount
    {
        private static void Postfix(WeaponMount weaponMount, GameObject __result)
        {
            if (__result == null || !Rifle.IsOurs(weaponMount))
                return;
            Rifle.LightenSpawned(__result);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class Patch_WeaponManager_Awake
    {
        private static void Postfix(WeaponManager __instance)
        {
            if (!Rifle.Injected)
                return;
            Rifle.InjectIntoWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponSelector), "Initialize")]
    [HarmonyPatch(new Type[] { typeof(Aircraft), typeof(HardpointSet), typeof(FactionHQ), typeof(Airbase) })]
    internal static class Patch_WeaponSelector_Initialize
    {
        private static void Prefix(Aircraft aircraft)
        {
            Rifle.Ensure();
            if (aircraft != null && aircraft.weaponManager != null)
                Rifle.InjectIntoWeaponManager(aircraft.weaponManager);
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "VetWeapon")]
    internal static class Patch_VetWeapon
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WeaponMount requestedMount, HardpointSet hardpointSet, ref bool __result, ref string failReason, ref int failCost)
        {
            if (!Rifle.IsOurs(requestedMount))
                return true;
            if (Plugin.IsNavalHardpoint(hardpointSet))
            {
                __result = false;
                failReason = "106mm recoilless is not for naval cells";
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
            if (!Rifle.IsOurs(mount))
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
            Rifle.Ensure();
            Rifle.FilterAvailable(hardpointSet, outAvailable, HaveScratch);
        }
    }

    [HarmonyPatch(typeof(Gun), "Fire")]
    [HarmonyPatch(new Type[] { typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition) })]
    internal static class Patch_Gun_Fire
    {
        private static bool Prefix(Gun __instance, Unit firingUnit, WeaponStation weaponStation)
        {
            if (weaponStation == null || weaponStation.WeaponInfo == null)
                return true;
            if (!Rifle.IsOurs(weaponStation.WeaponInfo) && !Rifle.IsOurs(__instance))
                return true;
            Aircraft ac = Rifle.AircraftOf(firingUnit);
            PowerSupply ps = Rifle.PowerOf(firingUnit);
            Rifle.EnsureCapacitor(ac, ps);
            Rifle.TopUpForShot(ac, ps);
            Rifle.BindGun(__instance);
            Rifle.ApplyGuidedFromStation(weaponStation);
            return true;
        }
    }

    [HarmonyPatch(typeof(Gun), "SpawnBullet")]
    [HarmonyPatch(new Type[] { typeof(float) })]
    internal static class Patch_Gun_SpawnBullet
    {
        private static bool Prefix(Gun __instance)
        {
            if (!Rifle.IsOurs(__instance))
                return true;
            try
            {
                Unit owner = __instance.attachedUnit;
                Aircraft ac = Rifle.AircraftOf(owner);
                PowerSupply ps = Rifle.PowerOf(owner);
                Rifle.EnsureCapacitor(ac, ps);
                Rifle.TopUpForShot(ac, ps);
                Rifle.BindGun(__instance);
                Rifle.TryConsumeShot(ac, ps);
                Rifle.ForceVisibleShot(__instance);
                Rifle.ApplyGuidedProjectile(__instance);
                Rifle.PushLockTarget(__instance);
                Rifle.ApplyShotBallistics(__instance);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("RR106 SpawnBullet prefix: " + ex.Message);
            }
            return true;
        }

        private static void Postfix(Gun __instance)
        {
            if (__instance == null || !Rifle.IsOurs(__instance))
                return;
            Rifle.RestoreShotBallistics(__instance);
        }
    }

    [HarmonyPatch(typeof(ChargeIndicator), "ChargeIndicator_OnSetAircraft")]
    internal static class Patch_ChargeHud
    {
        private static void Postfix(ChargeIndicator __instance, CombatHUD sender)
        {
            if (__instance == null || sender == null || sender.aircraft == null)
                return;
            if (!Rifle.CurrentStationIsOurs(sender.aircraft))
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

    [HarmonyPatch(typeof(Missile), "Detonate")]
    [HarmonyPatch(new Type[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    internal static class Patch_Missile_Detonate
    {
        private static void Prefix(Missile __instance)
        {
            if (__instance == null || !Rifle.IsReconMissile(__instance))
                return;
            try
            {
                Rifle.PaintRecon(__instance);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(Missile), "OnEnable")]
    internal static class Patch_Missile_OnEnable_RR106
    {
        private static void Postfix(Missile __instance)
        {
            Rifle.ApplyToSpawnedShell(__instance);
        }
    }

    [HarmonyPatch(typeof(Spawner), "SpawnMissile", new Type[]
    {
        typeof(MissileDefinition),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(Vector3),
        typeof(Unit),
        typeof(Unit)
    })]
    internal static class Patch_Spawner_SpawnMissile_RR106
    {
        private static void Prefix(MissileDefinition missile)
        {
            Rifle.WakeShellPrefab(missile);
        }

        private static void Postfix(MissileDefinition missile, Missile __result)
        {
            Rifle.ApplyToSpawnedShell(__result, missile);
        }
    }
}
