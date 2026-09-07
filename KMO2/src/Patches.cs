using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace KMO2
{
    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    internal static class Patch_Hardpoint_SpawnMount
    {
        private static void Postfix(Hardpoint __instance, Aircraft aircraft, WeaponMount weaponMount, GameObject __result)
        {
            if (__result == null || weaponMount == null || !Munition.IsOurs(weaponMount))
                return;
            Munition.RestoreMountIdentity(weaponMount);
            Visual.ApplyToHangarRack(__result);
            try
            {
                Weapon[] rails = __result.GetComponentsInChildren<Weapon>(true);
                for (int i = 0; i < rails.Length; i++)
                {
                    Weapon w = rails[i];
                    if (w == null || w is Gun)
                        continue;
                    if (!w.gameObject.activeSelf)
                        w.gameObject.SetActive(true);
                    Munition.SyncFromMount(w, weaponMount);
                }
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class Patch_WeaponManager_Awake
    {
        private static void Postfix(WeaponManager __instance)
        {
            if (!Munition.Injected)
                return;
            Munition.InjectIntoWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "OnEnable")]
    internal static class Patch_WeaponManager_OnEnable
    {
        private static void Postfix(WeaponManager __instance)
        {
            if (!Munition.Injected)
                return;
            Munition.InjectIntoWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponSelector), "Initialize")]
    [HarmonyPatch(new Type[] { typeof(Aircraft), typeof(HardpointSet), typeof(FactionHQ), typeof(Airbase) })]
    internal static class Patch_WeaponSelector_Initialize
    {
        private static void Prefix(Aircraft aircraft, HardpointSet hardpointSet)
        {
            Munition.Ensure();
            if (aircraft == null || aircraft.weaponManager == null)
                return;
            Pylon.HangarWm = aircraft.weaponManager;
            Pylon.Remember(aircraft.weaponManager);
            Pylon.RememberSet(hardpointSet, aircraft.weaponManager);
            Munition.InjectIntoWeaponManager(aircraft.weaponManager);
        }
    }

    [HarmonyPatch]
    internal static class Patch_WeaponSelector_Initialize_Set
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo[] ms = typeof(WeaponSelector).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].Name != "Initialize")
                    continue;
                ParameterInfo[] p = ms[i].GetParameters();
                if (p.Length == 2 && p[0].ParameterType == typeof(HardpointSet))
                    return ms[i];
            }
            return null;
        }

        private static void Prefix(HardpointSet hardpointSet)
        {
            if (hardpointSet == null || Pylon.HangarWm == null)
                return;
            Pylon.RememberSet(hardpointSet, Pylon.HangarWm);
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "VetWeapon")]
    internal static class Patch_VetWeapon
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            WeaponMount requestedMount,
            HardpointSet hardpointSet,
            Loadout requestedLoadout,
            ref bool __result,
            ref string failReason,
            ref int failCost)
        {
            if (Munition.IsOurs(requestedMount))
            {
                if (Plugin.IsNavalHardpoint(hardpointSet) || Pylon.IsInternal(hardpointSet))
                {
                    __result = false;
                    failReason = "KMO-2 is outer-pylon only";
                    failCost = 0;
                    return false;
                }
                if (!Pylon.CanTakeKmo(hardpointSet))
                {
                    __result = false;
                    failReason = "KMO-2 is outer-pylon only";
                    failCost = 0;
                    return false;
                }
                __result = true;
                failReason = null;
                failCost = 0;
                return false;
            }
            if (requestedMount != null && Pylon.BlockedByKmo(hardpointSet, requestedLoadout))
            {
                __result = false;
                failReason = "Blocked by KMO-2 on the nearest pylon";
                failCost = 0;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedHardpoint")]
    internal static class Patch_MountAllowedHardpoint
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WeaponMount mount, HardpointSet hardpointSet, ref bool __result)
        {
            if (!Munition.IsOurs(mount))
                return true;
            __result = Pylon.CanTakeKmo(hardpointSet);
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
            Munition.Ensure();
            Munition.FilterAvailable(hardpointSet, outAvailable, HaveScratch);
        }
    }

    [HarmonyPatch(typeof(HardpointSet), "BlockedByOtherHardpoint")]
    internal static class Patch_BlockedByOtherHardpoint
    {
        private static void Postfix(HardpointSet __instance, Loadout loadout, ref bool __result)
        {
            if (__result || __instance == null)
                return;
            if (Pylon.BlockedByKmo(__instance, loadout))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(MountedMissile), "Fire")]
    internal static class Patch_MountedMissile_Fire
    {
        private static bool Prefix(MountedMissile __instance)
        {
            if (__instance == null || !Munition.IsOurs(__instance))
                return true;
            Unit owner = __instance.attachedUnit;
            if (!Munition.TryConsumeFrom(owner))
                return false;
            Munition.NoteFire(__instance);
            try { LaunchFx.Play(__instance); }
            catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(Spawner))]
    internal static class Patch_Spawner_SpawnMissile
    {
        [HarmonyPrefix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PrefixDef(Quaternion rotation, ref Vector3 velocity, Unit owner)
        {
            Munition.TryBoostChargeLaunch(owner, rotation, ref velocity);
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixDef(Missile __result, Unit owner)
        {
            Munition.OnSpawned(__result, owner);
        }

        [HarmonyPrefix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PrefixGo(Quaternion rotation, ref Vector3 velocity, Unit owner)
        {
            Munition.TryBoostChargeLaunch(owner, rotation, ref velocity);
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixGo(Missile __result, Unit owner)
        {
            Munition.OnSpawned(__result, owner);
        }
    }

    [HarmonyPatch(typeof(Weapon), "AttachToHardpoint")]
    internal static class Patch_Weapon_Attach
    {
        private static void Postfix(Weapon __instance, WeaponMount weaponMount)
        {
            Munition.SyncFromMount(__instance, weaponMount);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "RegisterWeapon")]
    internal static class Patch_WeaponManager_Register
    {
        private static void Prefix(Weapon weapon, WeaponMount weaponMount)
        {
            if (weapon == null || weaponMount == null || weapon is Gun)
                return;
            if (!Munition.IsOurs(weaponMount))
                return;
            try
            {
                if (!weapon.gameObject.activeSelf)
                    weapon.gameObject.SetActive(true);
            }
            catch { }
            Munition.RestoreMountIdentity(weaponMount);
            Munition.SyncFromMount(weapon, weaponMount);
        }
    }

    [HarmonyPatch]
    internal static class Patch_WeaponManager_Organize
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(WeaponManager), "OrganizeWeaponStations");
        }

        private static void Postfix(WeaponManager __instance)
        {
            Munition.MergeSplitStations(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "Ready")]
    internal static class Patch_WeaponStation_Ready
    {
        private static void Postfix(WeaponStation __instance, ref bool __result)
        {
            if (!__result || __instance == null || !Munition.StationIsOurs(__instance))
                return;
            if (!Munition.StationHasShotEnergy(__instance))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "RegisterWeapon")]
    internal static class Patch_WeaponStation_Register
    {
        private static void Postfix(WeaponStation __instance, Weapon weapon, WeaponMount weaponMount)
        {
            if (weapon is Gun)
                return;
            Munition.SyncFromMount(weapon, weaponMount);
            if (__instance != null && weapon != null && weapon.info != null
                && (Munition.IsOurs(weaponMount) || Munition.IsOurs(weapon.info)))
                __instance.WeaponInfo = weapon.info;
        }
    }

    [HarmonyPatch(typeof(ChargeIndicator), "ChargeIndicator_OnSetAircraft")]
    internal static class Patch_ChargeHud
    {
        private static void Postfix(ChargeIndicator __instance, CombatHUD sender)
        {
            if (__instance == null || sender == null || sender.aircraft == null)
                return;
            if (!Munition.CurrentStationIsOurs(sender.aircraft))
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

    [HarmonyPatch]
    internal static class Patch_HardpointPylon_MatchesMount
    {
        private static readonly FieldInfo BoundMountField;

        static Patch_HardpointPylon_MatchesMount()
        {
            Type nested = AccessTools.Inner(typeof(Hardpoint), "HardpointPylon");
            BoundMountField = nested != null ? AccessTools.Field(nested, "mount") : null;
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            Type nested = AccessTools.Inner(typeof(Hardpoint), "HardpointPylon");
            if (nested == null)
                return null;
            return AccessTools.Method(nested, "MatchesMount", new Type[] { typeof(WeaponMount) });
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, WeaponMount mount, ref bool __result)
        {
            if (__result || mount == null || __instance == null || BoundMountField == null)
                return;
            if (!Munition.IsOurs(mount))
                return;
            WeaponMount bound = null;
            try { bound = BoundMountField.GetValue(__instance) as WeaponMount; }
            catch { return; }
            if (bound == null)
                return;
            __result = true;
        }
    }
}
