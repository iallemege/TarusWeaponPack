using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KMO2
{
    internal static class Munition
    {
        internal const string MountKey = "KMO2_single";
        internal const string InfoName = "KMO2_Info";
        internal const string DisplayName = "KMO-2";
        internal const string ShortName = "KMO-2";
        internal const string Description =
            "This is a compact air-launched missile developed by Tarus Electronautic in 2072. "
            + "It is a loitering kinetic munition with a 5 kt G-element warhead that produces a shockwave only. "
            + "A full capacitor dump scales motor velocity "
            + "and kinetic energy with stored charge, and it can also launch on metered pulses. "
            + "The missile also has guided and charge-guided modes.";
        internal const float ShotEnergyKJ = 20f;
        internal const float BasePierce = 2550f;
        internal const float BaseBlast = GBurst.NukeYield5kt;
        internal const float BaseMuzzle = 1020f;
        internal const float MaxSpeedMul = 8f;
        internal const float GuidedRangeM = 15000f;
        internal const float ChargeRangeM = 100000f;
        internal const float MountMass = 130f;
        internal const float GLimit = 40f;
        internal const float FireInterval = 0.4f;

        internal enum FireMode
        {
            Guided = 0,
            ChargeGuided = 1
        }

        internal static bool Injected;
        internal static WeaponMount Mount;
        internal static WeaponInfo Info;
        internal static FireMode Mode;
        internal static float LastShotKJ;

        internal static bool Ready
        {
            get { return Injected; }
        }

        internal static bool GuidedMode
        {
            get { return Mode == FireMode.Guided; }
        }

        internal static bool ChargeGuidedMode
        {
            get { return Mode == FireMode.ChargeGuided; }
        }

        internal static bool DumpsCapacitor
        {
            get { return ChargeGuidedMode; }
        }

        private static bool _running;
        private static float _menuReadyAt = -1f;
        private static MissileDefinition _encyclopediaDef;
        private static float _nextHardpointInject;
        private static readonly FieldInfo EventContentField =
            AccessTools.Field(typeof(WeaponMount), "isEventContent");
        private static readonly FieldInfo ChargeField =
            AccessTools.Field(typeof(PowerSupply), "charge");
        private static readonly FieldInfo ChargeChangedField =
            AccessTools.Field(typeof(PowerSupply), "onChargeChanged");
        private static readonly FieldInfo MaxChargeField =
            AccessTools.Field(typeof(PowerSupply), "maxCharge");
        private static readonly FieldInfo MissileInfoField =
            AccessTools.Field(typeof(Missile), "info");
        private static readonly FieldInfo MotorsField =
            AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo GLimitField =
            AccessTools.Field(typeof(Missile), "gLimit");
        private static readonly FieldInfo BlastYieldField =
            AccessTools.Field(typeof(Missile), "blastYield");
        private static readonly FieldInfo PierceField =
            AccessTools.Field(typeof(Missile), "pierceDamage");
        private static readonly Dictionary<int, float> FakeBatt = new Dictionary<int, float>();
        private static readonly HashSet<int> CapReady = new HashSet<int>();
        private const float FakeMaxKJ = 400f;
        private const float FakeRegenPerSec = 20f;
        private const float MinCapKJ = 400f;
        private const float SeedKJ = 80f;

        private sealed class PendingFire
        {
            public Unit owner;
            public float time;
            public WeaponInfo info;
            public float kj;
            public bool dumpCap;
        }

        private static readonly List<PendingFire> PendingFires = new List<PendingFire>();

        internal static bool IsOurs(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (string.Equals(mount.jsonKey, MountKey, StringComparison.OrdinalIgnoreCase))
                return true;
            return IsOurs(mount.info);
        }

        internal static bool IsOurs(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (Info != null && object.ReferenceEquals(info, Info))
                return true;
            if (info.name == InfoName)
                return true;
            if (info.weaponName == DisplayName || info.shortName == ShortName)
                return true;
            if (info.weaponName != null
                && info.weaponName.IndexOf("KMO-2", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool IsOurs(Weapon weapon)
        {
            if (weapon == null)
                return false;
            if (IsOurs(weapon.info))
                return true;
            return IsOurs(GetMount(weapon));
        }

        internal static bool IsMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.GetComponent<Kmo2Mark>() != null)
                    return true;
            }
            catch
            {
            }
            return IsOurs(GetMissileInfo(missile));
        }

        internal static void CycleMode()
        {
            int n = (int)Mode + 1;
            if (n > 1)
                n = 0;
            Mode = (FireMode)n;
            ApplyModeStats(Info);
        }

        internal static string ModeLabel()
        {
            if (ChargeGuidedMode)
                return "CHG+GUIDE";
            return "GUIDED";
        }

        internal static Color ModeColor()
        {
            if (ChargeGuidedMode)
                return new Color(1f, 0.55f, 0.95f, 1f);
            return new Color(0.45f, 0.92f, 1f, 1f);
        }

        internal static float PeekRangeKm()
        {
            return (ChargeGuidedMode ? ChargeRangeM : GuidedRangeM) / 1000f;
        }

        internal static void StampCurrent(Aircraft aircraft)
        {
            ApplyModeStats(Info);
            if (Info == null)
                return;
            PowerSupply ps = aircraft != null ? PowerOf(aircraft) : null;
            float v = PeekMuzzle(aircraft, ps);
            Info.muzzleVelocity = v;
            Info.maxSpeed = v;
        }

        internal static void Ensure()
        {
            if (_running)
                return;
            _running = true;
            try
            {
                if (!MenuReady())
                    return;
                if (!Injected)
                    CreateIfNeeded();
                if (Injected)
                    InjectHardpoints();
            }
            finally
            {
                _running = false;
            }
        }

        internal static int InjectIntoWeaponManager(WeaponManager wm)
        {
            if (!Injected || Mount == null || wm == null || wm.hardpointSets == null)
                return 0;
            if (Plugin.IsShipWeaponManager(wm))
                return 0;
            Pylon.Remember(wm);
            int added = 0;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                HardpointSet hs = wm.hardpointSets[i];
                if (hs == null)
                    continue;
                if (hs.weaponOptions == null)
                    hs.weaponOptions = new List<WeaponMount>();
                if (!Pylon.CanTakeKmo(hs, wm))
                {
                    added += StripFrom(hs);
                    continue;
                }
                if (hs.weaponOptions.Contains(Mount))
                    continue;
                hs.weaponOptions.Add(Mount);
                added++;
            }
            return added;
        }

        private static int StripFrom(HardpointSet hs)
        {
            if (hs == null || hs.weaponOptions == null || Mount == null)
                return 0;
            int n = 0;
            for (int i = hs.weaponOptions.Count - 1; i >= 0; i--)
            {
                if (!IsOurs(hs.weaponOptions[i]))
                    continue;
                hs.weaponOptions.RemoveAt(i);
                n++;
            }
            return n;
        }

        internal static void FilterAvailable(HardpointSet hs, List<WeaponMount> list, HashSet<WeaponMount> have)
        {
            if (list == null)
                return;
            Pylon.RememberAll();
            if (Pylon.BlockedByKmo(hs))
            {
                Pylon.StripBlocked(hs, list);
                return;
            }
            if (Mount == null || Mount.prefab == null)
                return;
            if (!Pylon.CanTakeKmo(hs))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (IsOurs(list[i]))
                        list.RemoveAt(i);
                }
                return;
            }
            if (have != null && !have.Add(Mount))
                return;
            if (list.Contains(Mount))
                return;
            list.Add(Mount);
        }

        internal static bool AircraftHasOurs(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            Weapon[] weapons = aircraft.GetComponentsInChildren<Weapon>(true);
            if (weapons != null)
            {
                for (int i = 0; i < weapons.Length; i++)
                {
                    if (weapons[i] != null && !(weapons[i] is Gun) && IsOurs(weapons[i]))
                        return true;
                }
            }
            WeaponManager wm = null;
            try { wm = aircraft.weaponManager; }
            catch { }
            if (wm == null || wm.hardpointSets == null)
                return false;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                HardpointSet hs = wm.hardpointSets[i];
                if (hs != null && IsOurs(hs.weaponMount))
                    return true;
            }
            return false;
        }

        internal static bool CurrentStationIsOurs(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            try
            {
                WeaponManager wm = aircraft.weaponManager;
                if (wm == null || wm.currentWeaponStation == null)
                    return false;
                if (IsOurs(wm.currentWeaponStation.WeaponInfo))
                    return true;
                if (wm.currentWeaponStation.Weapons == null)
                    return false;
                List<Weapon> list = wm.currentWeaponStation.Weapons;
                for (int i = 0; i < list.Count; i++)
                {
                    Weapon w = list[i];
                    if (w == null || w is Gun)
                        continue;
                    if (IsOurs(w))
                        return true;
                }
            }
            catch
            {
            }
            return false;
        }

        internal static Aircraft AircraftOf(Unit unit)
        {
            if (unit == null)
                return null;
            Aircraft ac = unit as Aircraft;
            if (ac != null)
                return ac;
            try
            {
                return unit.GetComponentInParent<Aircraft>();
            }
            catch
            {
                return null;
            }
        }

        internal static PowerSupply PowerOf(Unit unit)
        {
            Aircraft ac = AircraftOf(unit);
            if (ac == null)
                return null;
            try
            {
                return ac.GetComponentInChildren<PowerSupply>(true);
            }
            catch
            {
                return null;
            }
        }

        internal static bool HasShotEnergy(Aircraft ac, PowerSupply ps)
        {
            float kj = ReadKJ(ac, ps);
            if (DumpsCapacitor)
                return kj > 0.05f;
            return kj >= ShotEnergyKJ;
        }

        internal static float PeekShotCost(Aircraft ac, PowerSupply ps)
        {
            if (DumpsCapacitor)
                return ReadKJ(ac, ps);
            return ShotEnergyKJ;
        }

        internal static float PeekMuzzle(Aircraft ac, PowerSupply ps)
        {
            return BaseMuzzle * SpeedMulForKj(PeekShotCost(ac, ps));
        }

        internal static void EnsureCapacitor(Aircraft ac, PowerSupply ps)
        {
            if (ac == null)
                return;
            int id = ac.GetInstanceID();
            if (CapReady.Contains(id))
                return;
            CapReady.Add(id);
            if (ps == null)
                return;
            try { ps.enabled = true; }
            catch { }
            float max = 0f;
            if (MaxChargeField != null)
            {
                try
                {
                    object v = MaxChargeField.GetValue(ps);
                    if (v is float)
                        max = (float)v;
                }
                catch { }
            }
            if (max < MinCapKJ)
            {
                try { ps.ModifyCapacitance(MinCapKJ - max); }
                catch
                {
                    if (MaxChargeField != null)
                        MaxChargeField.SetValue(ps, MinCapKJ);
                }
            }
            float have = 0f;
            try { have = ps.GetChargeKJ(); }
            catch { }
            if (have < SeedKJ)
                WriteKJ(ac, ps, SeedKJ);
        }

        internal static void TickBattery(Aircraft ac, PowerSupply ps, float dt)
        {
            if (ps != null)
            {
                try { ps.enabled = true; }
                catch { }
                return;
            }
            if (ac == null || dt <= 0f)
                return;
            int id = ac.GetInstanceID();
            float v = ReadFake(id);
            v += FakeRegenPerSec * dt;
            if (v > FakeMaxKJ)
                v = FakeMaxKJ;
            FakeBatt[id] = v;
        }

        internal static bool TryConsumeShot(Aircraft ac, PowerSupply ps)
        {
            float kj = ReadKJ(ac, ps);
            float take;
            if (DumpsCapacitor)
            {
                if (kj < 0.05f)
                    return false;
                take = kj;
            }
            else
            {
                if (kj < ShotEnergyKJ)
                    return false;
                take = ShotEnergyKJ;
            }
            WriteKJ(ac, ps, kj - take);
            LastShotKJ = take;
            return true;
        }

        internal static float SpeedMulForKj(float kj)
        {
            float e = kj / ShotEnergyKJ;
            if (e < 0.05f)
                e = 0.05f;
            float s = Mathf.Sqrt(e);
            if (s > MaxSpeedMul)
                s = MaxSpeedMul;
            return s;
        }

        internal static float ReadKJ(Aircraft ac, PowerSupply ps)
        {
            if (ps != null)
            {
                try { return ps.GetChargeKJ(); }
                catch { }
            }
            if (ac == null)
                return 0f;
            return ReadFake(ac.GetInstanceID());
        }

        private static float ReadFake(int id)
        {
            float v;
            if (FakeBatt.TryGetValue(id, out v))
                return v;
            FakeBatt[id] = FakeMaxKJ;
            return FakeMaxKJ;
        }

        private static void WriteKJ(Aircraft ac, PowerSupply ps, float kj)
        {
            if (kj < 0f)
                kj = 0f;
            if (ps != null && ChargeField != null)
            {
                ChargeField.SetValue(ps, kj);
                try { ps.enabled = true; }
                catch { }
                try
                {
                    if (ChargeChangedField != null)
                    {
                        object ev = ChargeChangedField.GetValue(ps);
                        if (ev != null)
                        {
                            Delegate d = ev as Delegate;
                            if (d != null)
                                d.DynamicInvoke();
                        }
                    }
                }
                catch { }
            }
            if (ac != null)
                FakeBatt[ac.GetInstanceID()] = kj;
        }

        internal static void NoteFire(Weapon weapon)
        {
            if (weapon == null || !IsOurs(weapon))
                return;
            Unit owner = weapon.attachedUnit;
            Aircraft ac = AircraftOf(owner);
            PowerSupply ps = PowerOf(owner);
            PendingFire pf = new PendingFire();
            pf.owner = owner;
            pf.time = Time.time;
            pf.info = weapon.info;
            pf.kj = LastShotKJ > 0.05f ? LastShotKJ : PeekShotCost(ac, ps);
            pf.dumpCap = ChargeGuidedMode;
            PendingFires.Add(pf);
        }

        internal static bool TryBoostChargeLaunch(Unit owner, Quaternion rotation, ref Vector3 velocity)
        {
            float kj;
            bool dumpCap;
            if (!TryPeekPending(owner, out kj, out dumpCap) || !dumpCap)
                return false;
            float want = BaseMuzzle * SpeedMulForKj(kj);
            Vector3 fwd = rotation * Vector3.forward;
            if (fwd.sqrMagnitude < 0.0001f)
                fwd = Vector3.forward;
            else
                fwd.Normalize();
            Vector3 inherit = velocity;
            if (owner != null && owner.rb != null)
                inherit = owner.rb.velocity;
            velocity = inherit + fwd * want;
            return true;
        }

        internal static bool TryConsumeFrom(Unit owner)
        {
            Aircraft ac = AircraftOf(owner);
            PowerSupply ps = PowerOf(owner);
            EnsureCapacitor(ac, ps);
            if (!HasShotEnergy(ac, ps))
                return false;
            return TryConsumeShot(ac, ps);
        }

        internal static void OnSpawned(Missile missile, Unit owner)
        {
            if (missile == null)
                return;
            WeaponInfo pendingInfo;
            float kj;
            bool dumpCap;
            bool pending = ConsumePending(missile, owner, out pendingInfo, out kj, out dumpCap);
            bool ours = pending || IsMissile(missile) || IsOurs(GetMissileInfo(missile));
            if (!ours)
                return;
            ApplySpawnIdentity(missile, pendingInfo, kj, pending && dumpCap);
        }

        internal static void SyncFromMount(Weapon weapon, WeaponMount mount)
        {
            if (weapon == null || !IsOurs(mount))
                return;
            RestoreMountIdentity(mount);
            try
            {
                if (Info != null)
                    weapon.info = Info;
                else if (mount.info != null)
                    weapon.info = mount.info;
            }
            catch { }
            Visual.ApplyToRoot(weapon.gameObject);
        }

        /// <summary>
        /// Left+right KMO-2 share one WeaponInfo. If the first rail already swapped
        /// off the donor info, the second rail used to open a second 1-round station.
        /// </summary>
        internal static void MergeSplitStations(WeaponManager wm)
        {
            Aircraft ac = Pylon.AircraftOf(wm);
            if (ac == null || ac.weaponStations == null || ac.weaponStations.Count < 2)
                return;
            WeaponStation keep = null;
            List<WeaponStation> extra = null;
            for (int i = 0; i < ac.weaponStations.Count; i++)
            {
                WeaponStation st = ac.weaponStations[i];
                if (st == null || !StationIsOurs(st))
                    continue;
                if (keep == null)
                {
                    keep = st;
                    continue;
                }
                if (extra == null)
                    extra = new List<WeaponStation>(2);
                extra.Add(st);
            }
            if (keep == null || extra == null || extra.Count == 0)
                return;
            for (int i = 0; i < extra.Count; i++)
            {
                WeaponStation st = extra[i];
                if (st == null || st.Weapons == null)
                    continue;
                for (int w = 0; w < st.Weapons.Count; w++)
                {
                    Weapon weapon = st.Weapons[w];
                    if (weapon == null)
                        continue;
                    if (!keep.Weapons.Contains(weapon))
                        keep.Weapons.Add(weapon);
                    try { weapon.SetWeaponStation(keep); }
                    catch { }
                }
                st.Weapons.Clear();
                ac.weaponStations.Remove(st);
            }
            if (Info != null)
                keep.WeaponInfo = Info;
            keep.AccountAmmo();
            for (int i = 0; i < ac.weaponStations.Count; i++)
            {
                if (ac.weaponStations[i] != null)
                    ac.weaponStations[i].Number = (byte)i;
            }
            if (wm != null && extra != null)
            {
                for (int i = 0; i < extra.Count; i++)
                {
                    if (object.ReferenceEquals(wm.currentWeaponStation, extra[i]))
                    {
                        wm.currentWeaponStation = keep;
                        break;
                    }
                }
            }
        }

        internal static bool StationIsOurs(WeaponStation st)
        {
            if (st == null)
                return false;
            if (IsOurs(st.WeaponInfo))
                return true;
            if (st.Weapons == null)
                return false;
            for (int i = 0; i < st.Weapons.Count; i++)
            {
                if (IsOurs(st.Weapons[i]))
                    return true;
            }
            return false;
        }

        internal static bool StationHasShotEnergy(WeaponStation st)
        {
            if (st == null || st.Weapons == null || st.Weapons.Count == 0)
                return true;
            Unit owner = null;
            for (int i = 0; i < st.Weapons.Count; i++)
            {
                Weapon w = st.Weapons[i];
                if (w == null)
                    continue;
                owner = w.attachedUnit;
                if (owner != null)
                    break;
            }
            Aircraft ac = AircraftOf(owner);
            return HasShotEnergy(ac, PowerOf(owner));
        }

        internal static void RestoreMountIdentity(WeaponMount mount)
        {
            if (mount == null || !IsOurs(mount))
                return;
            if (Info != null)
                mount.info = Info;
            mount.jsonKey = MountKey;
            mount.mountName = DisplayName;
            mount.ammo = 1;
            mount.mass = MountMass;
            ApplyWeaponInfo(mount.info);
            Icon.Apply(mount.info);
        }

        internal static void ApplyModeStats(WeaponInfo info)
        {
            if (info == null)
                return;
            try
            {
                TargetRequirements tr = info.targetRequirements;
                tr.maxRange = ChargeGuidedMode ? ChargeRangeM : GuidedRangeM;
                tr.minRange = 400f;
                tr.lineOfSight = false;
                info.targetRequirements = tr;
                info.overHorizon = true;
            }
            catch { }
        }

        private static void ApplyWeaponInfo(WeaponInfo info)
        {
            if (info == null)
                return;
            info.weaponName = DisplayName;
            info.shortName = ShortName;
            info.description = Description;
            info.pierceDamage = BasePierce;
            info.blastDamage = BaseBlast;
            info.muzzleVelocity = BaseMuzzle;
            info.massPerRound = MountMass;
            info.missile = true;
            info.gun = false;
            info.nuclear = false;
            info.strategic = false;
            info.rearmShip = false;
            info.fireInterval = FireInterval;
            ApplyModeStats(info);
            Icon.Apply(info);
        }

        private static void CreateIfNeeded()
        {
            Encyclopedia enc = Plugin.GetEncyclopedia();
            if (enc == null || enc.weaponMounts == null || enc.weaponMounts.Count < 8)
                return;

            WeaponMount existing = FindMount(enc, MountKey);
            if (existing != null && existing.prefab != null && existing.info != null)
            {
                Mount = existing;
                Info = existing.info;
                ApplyWeaponInfo(Info);
                RestoreMountIdentity(existing);
                RegisterLookups(enc, existing);
                EnsureEncyclopediaDef(enc);
                Injected = true;
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("KMO2: adopted existing " + MountKey);
                return;
            }

            WeaponMount donor = FindDonor(enc);
            if (donor == null || donor.prefab == null || donor.info == null)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("KMO2: waiting for AGM/AAM single donor");
                return;
            }

            WeaponMount clone = UnityEngine.Object.Instantiate(donor);
            clone.name = "KMO2_Mount";
            clone.jsonKey = MountKey;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            clone.ammo = 1;
            clone.mass = MountMass;
            if (EventContentField != null)
            {
                try { EventContentField.SetValue(clone, false); }
                catch { }
            }

            WeaponInfo infoClone = UnityEngine.Object.Instantiate(donor.info);
            infoClone.name = InfoName;
            infoClone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            clone.prefab = donor.prefab;
            if (donor.info.weaponPrefab != null)
                infoClone.weaponPrefab = donor.info.weaponPrefab;
            clone.info = infoClone;
            clone.mountName = DisplayName;
            ApplyWeaponInfo(infoClone);

            Mount = clone;
            Info = infoClone;
            RegisterLookups(enc, clone);
            EnsureEncyclopediaDef(enc);
            Injected = true;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("KMO2: cloned " + donor.jsonKey + " as " + MountKey);
        }

        internal static void TickInject()
        {
            if (!Injected || Mount == null)
                return;
            InjectHardpoints();
        }

        private static void InjectHardpoints()
        {
            if (Time.unscaledTime < _nextHardpointInject)
                return;
            _nextHardpointInject = Time.unscaledTime + 1f;
            WeaponManager[] managers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            if (managers == null)
                return;
            for (int i = 0; i < managers.Length; i++)
                InjectIntoWeaponManager(managers[i]);
        }

        private static void RegisterLookups(Encyclopedia enc, WeaponMount mount)
        {
            if (enc != null && enc.weaponMounts != null && !enc.weaponMounts.Contains(mount))
                enc.weaponMounts.Add(mount);
            if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(MountKey))
                Encyclopedia.WeaponLookup[MountKey] = mount;
            try
            {
                if (enc != null && enc.IndexLookup != null && !enc.IndexLookup.Contains(mount))
                {
                    enc.IndexLookup.Add(mount);
                    INetworkDefinition nd = mount;
                    nd.LookupIndex = enc.IndexLookup.Count - 1;
                }
            }
            catch { }
        }

        private static void EnsureEncyclopediaDef(Encyclopedia enc)
        {
            if (enc == null || enc.missiles == null)
                return;
            if (_encyclopediaDef != null)
            {
                _encyclopediaDef.description = Description;
                _encyclopediaDef.unitName = DisplayName;
                Icon.ApplyToDefinition(_encyclopediaDef);
                return;
            }
            MissileDefinition src = null;
            for (int i = 0; i < enc.missiles.Count; i++)
            {
                MissileDefinition d = enc.missiles[i];
                if (d == null)
                    continue;
                string k = d.jsonKey != null ? d.jsonKey : string.Empty;
                if (k.IndexOf("AGM_heavy", StringComparison.OrdinalIgnoreCase) >= 0
                    || k.StartsWith("AGM1", StringComparison.OrdinalIgnoreCase)
                    || k.StartsWith("AAM4", StringComparison.OrdinalIgnoreCase))
                {
                    src = d;
                    if (k.IndexOf("AGM_heavy", StringComparison.OrdinalIgnoreCase) >= 0)
                        break;
                }
            }
            if (src == null)
                return;
            MissileDefinition clone = UnityEngine.Object.Instantiate(src);
            clone.name = "MissileDef_KMO2";
            clone.jsonKey = MountKey;
            clone.code = DisplayName;
            clone.unitName = DisplayName;
            clone.description = Description;
            clone.dontAutomaticallyAddToEncyclopedia = false;
            Icon.ApplyToDefinition(clone);
            if (!enc.missiles.Contains(clone))
                enc.missiles.Add(clone);
            if (Encyclopedia.Lookup != null && !Encyclopedia.Lookup.ContainsKey(MountKey))
                Encyclopedia.Lookup[MountKey] = clone;
            _encyclopediaDef = clone;
        }

        private static WeaponMount FindMount(Encyclopedia enc, string key)
        {
            if (enc == null || enc.weaponMounts == null || string.IsNullOrEmpty(key))
                return null;
            for (int i = 0; i < enc.weaponMounts.Count; i++)
            {
                WeaponMount m = enc.weaponMounts[i];
                if (m != null && string.Equals(m.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        private static WeaponMount FindDonor(Encyclopedia enc)
        {
            WeaponMount agm = FindMount(enc, "AGM_heavy_single");
            if (agm != null && agm.prefab != null)
                return agm;
            agm = FindMount(enc, "AGM1_single");
            if (agm != null && agm.prefab != null)
                return agm;
            WeaponMount aam = FindMount(enc, "AAM4_single");
            if (aam != null && aam.prefab != null)
                return aam;
            if (enc == null || enc.weaponMounts == null)
                return null;
            for (int i = 0; i < enc.weaponMounts.Count; i++)
            {
                WeaponMount m = enc.weaponMounts[i];
                if (m == null || m.info == null || m.prefab == null)
                    continue;
                if (!m.info.missile)
                    continue;
                string k = m.jsonKey != null ? m.jsonKey : string.Empty;
                if (k.IndexOf("single", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (k.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (k.StartsWith("AGM", StringComparison.OrdinalIgnoreCase)
                    || k.StartsWith("AAM", StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        private static WeaponMount GetMount(Weapon weapon)
        {
            if (weapon == null)
                return null;
            try
            {
                FieldInfo f = AccessTools.Field(typeof(Weapon), "mount");
                if (f != null)
                    return f.GetValue(weapon) as WeaponMount;
            }
            catch { }
            return null;
        }

        private static WeaponInfo GetMissileInfo(Missile missile)
        {
            if (missile == null || MissileInfoField == null)
                return null;
            try { return MissileInfoField.GetValue(missile) as WeaponInfo; }
            catch { return null; }
        }

        private static bool TryPeekPending(Unit owner, out float kj, out bool dumpCap)
        {
            kj = LastShotKJ;
            dumpCap = ChargeGuidedMode;
            if (owner == null)
                return false;
            float now = Time.time;
            for (int i = 0; i < PendingFires.Count; i++)
            {
                PendingFire pf = PendingFires[i];
                if (pf == null || now - pf.time > 8f)
                    continue;
                if (!OwnerMatches(pf.owner, owner))
                    continue;
                kj = pf.kj > 0.05f ? pf.kj : LastShotKJ;
                dumpCap = pf.dumpCap;
                return true;
            }
            return false;
        }

        private static bool ConsumePending(Missile missile, Unit spawnOwner, out WeaponInfo info, out float kj, out bool dumpCap)
        {
            info = null;
            kj = LastShotKJ;
            dumpCap = ChargeGuidedMode;
            float now = Time.time;
            for (int i = PendingFires.Count - 1; i >= 0; i--)
            {
                PendingFire pf = PendingFires[i];
                if (pf == null || now - pf.time > 8f)
                    PendingFires.RemoveAt(i);
            }
            Unit owner = spawnOwner != null ? spawnOwner : (missile != null ? missile.owner : null);
            int pick = -1;
            for (int i = 0; i < PendingFires.Count; i++)
            {
                PendingFire pf = PendingFires[i];
                if (pf != null && OwnerMatches(pf.owner, owner))
                {
                    pick = i;
                    break;
                }
            }
            if (pick < 0)
                return false;
            PendingFire taken = PendingFires[pick];
            PendingFires.RemoveAt(pick);
            info = taken != null ? taken.info : null;
            kj = taken != null ? taken.kj : LastShotKJ;
            dumpCap = taken != null && taken.dumpCap;
            return true;
        }

        private static bool OwnerMatches(Unit pendingOwner, Unit spawnOwner)
        {
            if (pendingOwner == null || spawnOwner == null)
                return false;
            if (object.ReferenceEquals(spawnOwner, pendingOwner))
                return true;
            return object.ReferenceEquals(AircraftOf(pendingOwner), AircraftOf(spawnOwner))
                && AircraftOf(pendingOwner) != null;
        }

        private static void ApplySpawnIdentity(Missile missile, WeaponInfo sourceInfo, float kj, bool dumpCap)
        {
            WeaponInfo info = sourceInfo;
            if (info == null || !IsOurs(info))
                info = GetMissileInfo(missile);
            try
            {
                missile.NetworkunitName = DisplayName;
                missile.name = DisplayName;
            }
            catch { }
            if (info != null && MissileInfoField != null)
            {
                try { MissileInfoField.SetValue(missile, info); }
                catch { }
            }
            if (BlastYieldField != null)
            {
                try { BlastYieldField.SetValue(missile, GBurst.NukeYield5kt); }
                catch { }
            }
            if (PierceField != null)
            {
                try { PierceField.SetValue(missile, 0f); }
                catch { }
            }
            GBurst.PrepareMissile(missile);
            Kmo2Mark mark = missile.GetComponent<Kmo2Mark>();
            if (mark == null)
                mark = Plugin.TryAddBehaviour<Kmo2Mark>(missile.gameObject);
            if (mark == null || !mark.Boosted)
            {
                ApplyKinematics(missile, kj, dumpCap);
                if (mark != null)
                    mark.Boosted = true;
            }
            Visual.ApplyToMissile(missile);
        }

        private static void ApplyKinematics(Missile missile, float kj, bool dumpCap)
        {
            if (missile == null)
                return;
            float speedMul = SpeedMulForKj(kj);
            float eMul = speedMul * speedMul;
            float wantSpeed = BaseMuzzle * speedMul;
            float range = dumpCap ? ChargeRangeM : GuidedRangeM;
            float minBurn = range / Mathf.Max(wantSpeed, 50f);
            if (minBurn < 8f)
                minBurn = 8f;
            try
            {
                if (GLimitField != null)
                    GLimitField.SetValue(missile, GLimit);
            }
            catch { }
            WeaponInfo info = GetMissileInfo(missile);
            if (info != null)
            {
                info.muzzleVelocity = wantSpeed;
                info.maxSpeed = wantSpeed;
            }
            if (MotorsField != null)
            {
                try
                {
                    Array motors = MotorsField.GetValue(missile) as Array;
                    if (motors != null)
                    {
                        for (int i = 0; i < motors.Length; i++)
                        {
                            object motor = motors.GetValue(i);
                            if (motor == null)
                                continue;
                            Type mt = motor.GetType();
                            FieldInfo fTop = mt.GetField("topSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            FieldInfo fThrust = mt.GetField("thrust", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            FieldInfo fBurn = mt.GetField("burnTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (fTop != null)
                                fTop.SetValue(motor, wantSpeed);
                            if (fThrust != null)
                            {
                                float thrust = Convert.ToSingle(fThrust.GetValue(motor));
                                if (thrust < 50f)
                                    thrust = 30000f * eMul;
                                else
                                    thrust = thrust * eMul;
                                fThrust.SetValue(motor, thrust);
                            }
                            if (fBurn != null)
                            {
                                float burn = Convert.ToSingle(fBurn.GetValue(motor));
                                if (burn < minBurn)
                                    fBurn.SetValue(motor, minBurn);
                            }
                        }
                    }
                }
                catch { }
            }
            if (!dumpCap)
                return;
            try
            {
                Vector3 fwd = missile.transform.forward;
                if (fwd.sqrMagnitude < 0.0001f)
                    fwd = Vector3.forward;
                else
                    fwd.Normalize();
                Vector3 inherit = Vector3.zero;
                Unit owner = missile.owner;
                if (owner != null && owner.rb != null)
                    inherit = owner.rb.velocity;
                Vector3 boosted = inherit + fwd * wantSpeed;
                if (missile.rb != null)
                    missile.rb.velocity = boosted;
                missile.NetworkstartingVelocity = boosted;
            }
            catch { }
        }

        private static bool MenuReady()
        {
            try
            {
                if (MainMenu.State != MainMenu.LoadingState.Loaded)
                    return false;
            }
            catch
            {
                return false;
            }
            if (_menuReadyAt < 0f)
            {
                _menuReadyAt = Time.unscaledTime + 1.5f;
                return false;
            }
            return Time.unscaledTime >= _menuReadyAt;
        }
    }

    public sealed class Kmo2Mark : MonoBehaviour
    {
        public bool Boosted;
    }
}
