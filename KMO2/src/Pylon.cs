using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace KMO2
{
    /// <summary>
    /// Outer-wing pylons only. Forced onto every aircraft, including those
    /// whose hangar catalog never listed KMO-2. If KMO-2 is on a station,
    /// the nearest same-side hardpoint must stay empty.
    /// </summary>
    internal static class Pylon
    {
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(WeaponManager), "aircraft");
        private static readonly Dictionary<HardpointSet, WeaponManager> HsToWm =
            new Dictionary<HardpointSet, WeaponManager>(new HsRefEq());
        internal static WeaponManager HangarWm;

        private sealed class HsRefEq : IEqualityComparer<HardpointSet>
        {
            public bool Equals(HardpointSet x, HardpointSet y)
            {
                return object.ReferenceEquals(x, y);
            }

            public int GetHashCode(HardpointSet obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        internal static void Remember(WeaponManager wm)
        {
            if (wm == null || wm.hardpointSets == null)
                return;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                HardpointSet hs = wm.hardpointSets[i];
                if (hs == null)
                    continue;
                HsToWm[hs] = wm;
            }
        }

        internal static void RememberSet(HardpointSet hs, WeaponManager wm)
        {
            if (hs == null || wm == null)
                return;
            HsToWm[hs] = wm;
        }

        internal static void RememberAll()
        {
            WeaponManager[] all = Resources.FindObjectsOfTypeAll<WeaponManager>();
            if (all == null)
                return;
            for (int i = 0; i < all.Length; i++)
                Remember(all[i]);
        }

        internal static Aircraft AircraftOf(WeaponManager wm)
        {
            if (wm == null || AircraftField == null)
                return null;
            try
            {
                return AircraftField.GetValue(wm) as Aircraft;
            }
            catch
            {
                return null;
            }
        }

        internal static WeaponManager ManagerOf(HardpointSet hs)
        {
            if (hs == null)
                return null;
            WeaponManager cached;
            if (HsToWm.TryGetValue(hs, out cached) && cached != null)
                return cached;
            if (HangarWm != null)
                return HangarWm;
            WeaponManager[] all = Resources.FindObjectsOfTypeAll<WeaponManager>();
            if (all == null)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponManager wm = all[i];
                if (wm == null || wm.hardpointSets == null)
                    continue;
                for (int h = 0; h < wm.hardpointSets.Length; h++)
                {
                    if (object.ReferenceEquals(wm.hardpointSets[h], hs))
                    {
                        Remember(wm);
                        return wm;
                    }
                }
            }
            return null;
        }

        internal static int IndexOf(WeaponManager wm, HardpointSet hs)
        {
            if (wm == null || wm.hardpointSets == null || hs == null)
                return -1;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                if (object.ReferenceEquals(wm.hardpointSets[i], hs))
                    return i;
            }
            return -1;
        }

        internal static bool IsInternal(HardpointSet hs)
        {
            if (hs == null || string.IsNullOrEmpty(hs.name))
                return false;
            string n = hs.name;
            if (n.IndexOf("Internal", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Bay", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Centerline", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Centreline", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Fuselage", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Belly", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Chin", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool NameSaysInner(HardpointSet hs)
        {
            if (hs == null || string.IsNullOrEmpty(hs.name))
                return false;
            string n = hs.name;
            if (n.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Middle", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Inboard", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("MidWing", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool LooksLikeGunOrSensor(HardpointSet hs)
        {
            if (hs == null || string.IsNullOrEmpty(hs.name))
                return false;
            string n = hs.name;
            if (n.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Cannon", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Sensor", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Fuel", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Probe", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("EOTS", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool NameSaysPylon(HardpointSet hs)
        {
            if (hs == null || string.IsNullOrEmpty(hs.name))
                return false;
            string n = hs.name;
            if (n.IndexOf("Wing", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Pylon", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Hardpoint", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Station", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Rail", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool NameSaysOuter(HardpointSet hs)
        {
            if (hs == null || string.IsNullOrEmpty(hs.name))
                return false;
            if (NameSaysInner(hs))
                return false;
            string n = hs.name;
            if (n.IndexOf("Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Wingtip", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Outboard", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Tip", StringComparison.OrdinalIgnoreCase) >= 0
                && n.IndexOf("Wing", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool ClearlyNotKmoStation(HardpointSet hs)
        {
            if (hs == null)
                return true;
            if (Plugin.IsNavalHardpoint(hs) || IsInternal(hs))
                return true;
            if (NameSaysInner(hs) || LooksLikeGunOrSensor(hs))
                return true;
            return false;
        }

        internal static HardpointSet TwinOn(WeaponManager wm, HardpointSet hs)
        {
            if (hs == null)
                return null;
            if (wm == null || wm.hardpointSets == null)
                return hs;
            int idx = IndexOf(wm, hs);
            if (idx >= 0)
                return hs;
            if (string.IsNullOrEmpty(hs.name))
                return hs;
            HardpointSet named = null;
            int hits = 0;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                HardpointSet other = wm.hardpointSets[i];
                if (other == null || string.IsNullOrEmpty(other.name))
                    continue;
                if (!string.Equals(other.name, hs.name, StringComparison.Ordinal))
                    continue;
                named = other;
                hits++;
            }
            if (hits == 1)
                return named;
            return hs;
        }

        internal static bool CanTakeKmo(HardpointSet hs)
        {
            return IsOuter(hs, ManagerOf(hs));
        }

        internal static bool CanTakeKmo(HardpointSet hs, WeaponManager wm)
        {
            return IsOuter(hs, wm);
        }

        internal static bool IsOuter(HardpointSet hs)
        {
            return IsOuter(hs, ManagerOf(hs));
        }

        internal static bool IsOuter(HardpointSet hs, WeaponManager wm)
        {
            if (hs == null)
                return false;
            if (ClearlyNotKmoStation(hs))
                return false;
            if (NameSaysOuter(hs))
                return true;
            if (wm == null)
                wm = ManagerOf(hs);
            if (wm == null)
                wm = HangarWm;
            if (wm != null && wm.hardpointSets != null)
            {
                HardpointSet probe = TwinOn(wm, hs);
                float x;
                if (TryLocalX(probe, wm, out x))
                {
                    if (Mathf.Abs(x) < 0.4f)
                        return false;
                    float side = Mathf.Sign(x);
                    float best = 0f;
                    bool any = false;
                    for (int i = 0; i < wm.hardpointSets.Length; i++)
                    {
                        HardpointSet other = wm.hardpointSets[i];
                        if (other == null || ClearlyNotKmoStation(other))
                            continue;
                        float ox;
                        if (!TryLocalX(other, wm, out ox))
                            continue;
                        if (Mathf.Abs(ox) < 0.4f)
                            continue;
                        if (Mathf.Sign(ox) != side)
                            continue;
                        float a = Mathf.Abs(ox);
                        if (!any || a > best)
                        {
                            best = a;
                            any = true;
                        }
                    }
                    if (!any)
                        return Mathf.Abs(x) >= 0.4f;
                    return Mathf.Abs(x) >= best * 0.97f;
                }
                if (NameSaysPylon(probe) || NameSaysPylon(hs))
                    return true;
                return false;
            }
            if (NameSaysPylon(hs))
                return true;
            return true;
        }

        internal static HardpointSet FindNearestSameSide(HardpointSet hs, WeaponManager wm)
        {
            if (hs == null || wm == null || wm.hardpointSets == null)
                return null;
            float x;
            if (!TryLocalX(hs, wm, out x))
                return null;
            float side = Mathf.Sign(x);
            if (Mathf.Abs(x) < 0.15f)
                side = 0f;
            Vector3 p = AverageWorld(hs);
            HardpointSet best = null;
            float bestD = 1e12f;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                HardpointSet other = wm.hardpointSets[i];
                if (other == null || object.ReferenceEquals(other, hs))
                    continue;
                if (Plugin.IsNavalHardpoint(other) || IsInternal(other))
                    continue;
                float ox;
                if (!TryLocalX(other, wm, out ox))
                    continue;
                if (side != 0f && Mathf.Abs(ox) >= 0.15f && Mathf.Sign(ox) != side)
                    continue;
                float d = (AverageWorld(other) - p).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = other;
                }
            }
            return best;
        }

        internal static bool NeighborOccupied(HardpointSet hs, Loadout loadout)
        {
            WeaponManager wm = ManagerOf(hs);
            HardpointSet near = FindNearestSameSide(hs, wm);
            if (near == null)
                return false;
            return SlotHasMount(wm, near, loadout);
        }

        internal static bool NeighborHasOurs(HardpointSet hs, Loadout loadout)
        {
            WeaponManager wm = ManagerOf(hs);
            HardpointSet near = FindNearestSameSide(hs, wm);
            if (near == null)
                return false;
            return SlotIsOurs(wm, near, loadout);
        }

        internal static bool NeighborOccupied(HardpointSet hs)
        {
            WeaponManager wm = ManagerOf(hs);
            Aircraft ac = AircraftOf(wm);
            Loadout loadout = ac != null ? ac.loadout : null;
            return NeighborOccupied(hs, loadout);
        }

        internal static bool NeighborHasOurs(HardpointSet hs)
        {
            WeaponManager wm = ManagerOf(hs);
            Aircraft ac = AircraftOf(wm);
            Loadout loadout = ac != null ? ac.loadout : null;
            return NeighborHasOurs(hs, loadout);
        }

        /// <summary>
        /// True when this station is the nearest same-side neighbor of a KMO-2.
        /// That station must be forced Empty and grayed out.
        /// </summary>
        internal static bool BlockedByKmo(HardpointSet hs, Loadout loadout)
        {
            if (hs == null)
                return false;
            WeaponManager wm = ManagerOf(hs);
            if (wm == null || wm.hardpointSets == null)
                return false;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                HardpointSet other = wm.hardpointSets[i];
                if (other == null || object.ReferenceEquals(other, hs))
                    continue;
                if (!Munition.IsOurs(MountAt(wm, other, loadout)))
                    continue;
                HardpointSet near = FindNearestSameSide(other, wm);
                if (near != null && object.ReferenceEquals(near, hs))
                    return true;
            }
            return false;
        }

        internal static bool BlockedByKmo(HardpointSet hs)
        {
            WeaponManager wm = ManagerOf(hs);
            Aircraft ac = AircraftOf(wm);
            Loadout loadout = ac != null ? ac.loadout : null;
            return BlockedByKmo(hs, loadout);
        }

        private static bool SlotHasMount(WeaponManager wm, HardpointSet hs, Loadout loadout)
        {
            WeaponMount m = MountAt(wm, hs, loadout);
            return m != null;
        }

        private static bool SlotIsOurs(WeaponManager wm, HardpointSet hs, Loadout loadout)
        {
            return Munition.IsOurs(MountAt(wm, hs, loadout));
        }

        private static WeaponMount MountAt(WeaponManager wm, HardpointSet hs, Loadout loadout)
        {
            int idx = IndexOf(wm, hs);
            if (idx >= 0 && loadout != null && loadout.weapons != null && idx < loadout.weapons.Count)
                return loadout.weapons[idx];
            if (hs != null && hs.weaponMount != null)
                return hs.weaponMount;
            return null;
        }

        private static bool TryLocalX(HardpointSet hs, WeaponManager wm, out float x)
        {
            x = 0f;
            Vector3 world = AverageWorld(hs);
            if (world.sqrMagnitude < 0.0001f && (hs.hardpoints == null || hs.hardpoints.Count == 0))
                return false;
            Aircraft ac = AircraftOf(wm);
            Transform root = null;
            if (ac != null)
                root = ac.transform;
            else if (wm != null)
                root = wm.transform;
            if (root == null)
            {
                x = world.x;
                return true;
            }
            x = root.InverseTransformPoint(world).x;
            return true;
        }

        private static Vector3 AverageWorld(HardpointSet hs)
        {
            if (hs == null || hs.hardpoints == null)
                return Vector3.zero;
            Vector3 s = Vector3.zero;
            int n = 0;
            for (int i = 0; i < hs.hardpoints.Count; i++)
            {
                Hardpoint hp = hs.hardpoints[i];
                if (hp == null)
                    continue;
                Transform t = hp.transform;
                if (t == null)
                    continue;
                s += t.position;
                n++;
            }
            if (n <= 0)
                return Vector3.zero;
            return s / n;
        }

        internal static void StripBlocked(HardpointSet hs, List<WeaponMount> list)
        {
            if (list == null)
                return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null)
                    continue;
                list.RemoveAt(i);
            }
        }
    }
}
