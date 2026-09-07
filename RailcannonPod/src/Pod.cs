using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RailcannonPod
{
    internal static class Pod
    {
        internal const string MountKey = "gun_155mm_pod_P";
        internal const string InfoName = "Railgun1_P";
        internal const string DisplayName = "57mm Railgun";
        internal const string ShortName = "57MM RAILGUN";
        internal const string Description =
            "This is a compact electromagnetic weapon developed by Tarus Electronautic in 2072. "
            + "It fires 57mm APFSDS ammunition. A full capacitor dump scales muzzle velocity "
            + "and kinetic energy with stored charge, and it can also fire on metered pulses. "
            + "The gun also has guided munitions.";
        internal const string DonorMountKey = "gun_57mm_pod";
        internal const string DonorInfoName = "Railgun1";
        internal const float ShotEnergyKJ = 20f;
        internal const float BasePierce = 2000f;
        internal const float BaseBlast = 20f;
        internal const float BaseMuzzle = 2400f;
        internal const float ModelScale = 1f;
        internal const float MaxSpeedMul = 8f;

        internal enum RailFireMode
        {
            Normal = 0,
            Charge = 1,
            Guided = 2
        }

        internal static bool Injected;
        internal static WeaponMount Mount;
        internal static WeaponInfo Info;
        internal static RailFireMode Mode;
        internal static float LastShotKJ;

        internal static bool ChargeMode
        {
            get { return Mode == RailFireMode.Charge; }
        }

        internal static bool GuidedMode
        {
            get { return Mode == RailFireMode.Guided; }
        }

        private static bool _running;
        private static float _menuReadyAt = -1f;
        private static GameObject _holder;
        private static bool _ballisticsOn;
        private static float _savedPierce;
        private static float _savedBlast;
        private static float _savedInfoMuzzle;
        private static float _savedGunMuzzle;
        private static readonly FieldInfo EventContentField =
            AccessTools.Field(typeof(WeaponMount), "isEventContent");
        private static readonly FieldInfo ChargeField =
            AccessTools.Field(typeof(PowerSupply), "charge");
        private static readonly FieldInfo ChargeChangedField =
            AccessTools.Field(typeof(PowerSupply), "onChargeChanged");
        private static readonly FieldInfo GunMuzzleField =
            AccessTools.Field(typeof(Gun), "muzzleVelocity");
        private static readonly FieldInfo GunGuidedField =
            AccessTools.Field(typeof(Gun), "guidedProjectile");
        private static readonly FieldInfo WeaponMountField =
            AccessTools.Field(typeof(Weapon), "mount");
        private static MissileDefinition _guidedShell;
        private static bool _guidedLogged;
        private static float _nextGuidedSearch;
        private static readonly Dictionary<int, float> FakeBatt = new Dictionary<int, float>();
        private static readonly HashSet<int> CapReady = new HashSet<int>();
        private static readonly FieldInfo MaxChargeField =
            AccessTools.Field(typeof(PowerSupply), "maxCharge");
        private const float FakeMaxKJ = 400f;
        private const float FakeRegenPerSec = 20f;
        private const float MinCapKJ = 400f;
        private const float SeedKJ = 80f;

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
            if (info.name == "RR106_Info")
                return false;
            if (info.shortName == "RR 106MM")
                return false;
            if (info.weaponName != null
                && info.weaponName.IndexOf("106mm Recoilless", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (Info != null && object.ReferenceEquals(info, Info))
                return true;
            if (info.name == InfoName)
                return true;
            if (info.weaponName == DisplayName)
                return true;
            if (info.weaponName == "40mm Railcannon")
                return true;
            if (info.shortName == ShortName)
                return true;
            if (info.weaponName != null
                && (info.weaponName.IndexOf("57mm Railgun", StringComparison.OrdinalIgnoreCase) >= 0
                    || info.weaponName.IndexOf("57mmRailgun", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            return false;
        }

        internal static bool IsOurs(Gun gun)
        {
            if (gun == null)
                return false;
            WeaponMount mount = null;
            if (WeaponMountField != null)
            {
                try { mount = WeaponMountField.GetValue(gun) as WeaponMount; }
                catch { mount = null; }
            }
            if (mount != null && string.Equals(mount.jsonKey, "gun_106mm_rr", StringComparison.OrdinalIgnoreCase))
                return false;
            if (IsOurs(gun.info))
                return true;
            return IsOurs(mount);
        }

        internal static void CycleMode()
        {
            int n = (int)Mode + 1;
            if (n > 2)
                n = 0;
            Mode = (RailFireMode)n;
        }

        internal static string ModeLabel()
        {
            if (Mode == RailFireMode.Charge)
                return "CHARGE";
            if (Mode == RailFireMode.Guided)
                return "GUIDED";
            return "NORMAL";
        }

        internal static Color ModeColor()
        {
            if (Mode == RailFireMode.Charge)
                return new Color(1f, 0.78f, 0.25f, 1f);
            if (Mode == RailFireMode.Guided)
                return new Color(0.45f, 0.92f, 1f, 1f);
            return new Color(0.7f, 1f, 0.75f, 1f);
        }

        internal static Color ModePipColor()
        {
            if (Mode == RailFireMode.Charge)
                return new Color(1f, 0.72f, 0.2f, 0.95f);
            if (Mode == RailFireMode.Guided)
                return new Color(0.4f, 0.9f, 1f, 0.95f);
            return new Color(0.4f, 1f, 0.55f, 0.95f);
        }

        internal static void StampCurrentGun(Aircraft aircraft)
        {
            ApplyGuidedProjectile(FindOurGun(aircraft));
            if (Mount != null && Mount.prefab != null)
                ApplyGuidedProjectile(Mount.prefab.GetComponentInChildren<Gun>(true));
        }

        internal static void ApplyGuidedFromStation(WeaponStation station)
        {
            if (station == null || station.Weapons == null)
                return;
            List<Weapon> list = station.Weapons;
            for (int i = 0; i < list.Count; i++)
                ApplyGuidedProjectile(list[i] as Gun);
        }

        internal static void ApplyGuidedProjectile(Gun gun)
        {
            if (gun == null || GunGuidedField == null)
                return;
            if (Mode != RailFireMode.Guided)
            {
                GunGuidedField.SetValue(gun, null);
                return;
            }
            MissileDefinition shell = EnsureGuidedShell();
            GunGuidedField.SetValue(gun, shell);
        }

        internal static MissileDefinition EnsureGuidedShell()
        {
            if (_guidedShell != null)
                return _guidedShell;
            if (Time.unscaledTime < _nextGuidedSearch)
                return null;
            _nextGuidedSearch = Time.unscaledTime + 2f;
            MissileDefinition src = Find76mmGuidedShell();
            if (src == null || src.unitPrefab == null)
            {
                if (!_guidedLogged && Plugin.Log != null)
                {
                    _guidedLogged = true;
                    Plugin.Log.LogWarning("RailcannonPod: 76mm guided shell not found yet");
                }
                return null;
            }
            _guidedShell = src;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("RailcannonPod: guided ammo = "
                    + (src.unitName != null ? src.unitName : src.jsonKey)
                    + " key=" + src.jsonKey);
            return _guidedShell;
        }

        private static MissileDefinition Find76mmGuidedShell()
        {
            MissileDefinition named = null;
            Encyclopedia enc = Plugin.GetEncyclopedia();
            if (enc != null && enc.missiles != null)
            {
                for (int i = 0; i < enc.missiles.Count; i++)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d == null || d.unitPrefab == null)
                        continue;
                    if (Looks76Guided(d.unitName) || Looks76Guided(d.jsonKey) || Looks76Guided(d.name))
                    {
                        if (HasOpticalShell(d.unitPrefab))
                            return d;
                        if (named == null)
                            named = d;
                    }
                }
            }
            if (GunGuidedField != null)
            {
                Gun[] guns = Resources.FindObjectsOfTypeAll<Gun>();
                if (guns != null)
                {
                    for (int i = 0; i < guns.Length; i++)
                    {
                        Gun gun = guns[i];
                        if (gun == null || IsOurs(gun))
                            continue;
                        MissileDefinition gp = GunGuidedField.GetValue(gun) as MissileDefinition;
                        if (gp == null || gp.unitPrefab == null)
                            continue;
                        string infoName = gun.info != null ? gun.info.weaponName : null;
                        if (Looks76Guided(gp.unitName) || Looks76Guided(gp.jsonKey)
                            || Looks76(infoName) || Looks76(gun.name))
                        {
                            if (HasOpticalShell(gp.unitPrefab))
                                return gp;
                            if (named == null)
                                named = gp;
                        }
                    }
                }
            }
            OpticalSeekerShell[] seekers = null;
            try { seekers = Resources.FindObjectsOfTypeAll<OpticalSeekerShell>(); }
            catch { }
            if (seekers != null)
            {
                for (int i = 0; i < seekers.Length; i++)
                {
                    OpticalSeekerShell seeker = seekers[i];
                    if (seeker == null)
                        continue;
                    Missile missile = seeker.GetComponentInParent<Missile>();
                    if (missile == null || missile.definition == null)
                        continue;
                    MissileDefinition d = missile.definition as MissileDefinition;
                    if (d == null || d.unitPrefab == null)
                        continue;
                    if (Looks76Guided(d.unitName) || Looks76Guided(d.jsonKey) || Looks76(d.unitName))
                        return d;
                }
            }
            return named;
        }

        private static bool HasOpticalShell(GameObject prefab)
        {
            if (prefab == null)
                return false;
            try
            {
                return prefab.GetComponentInChildren<OpticalSeekerShell>(true) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool Looks76Guided(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            if (s.IndexOf("76mm", StringComparison.Ordinal) < 0
                && s.IndexOf("76 mm", StringComparison.Ordinal) < 0)
                return false;
            return s.IndexOf("guided", StringComparison.Ordinal) >= 0
                || s.IndexOf("shell", StringComparison.Ordinal) >= 0;
        }

        private static bool Looks76(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            return s.IndexOf("76mm", StringComparison.Ordinal) >= 0
                || s.IndexOf("76 mm", StringComparison.Ordinal) >= 0;
        }

        internal static bool HasShotEnergy(Aircraft ac, PowerSupply ps)
        {
            float kj = ReadKJ(ac, ps);
            if (ChargeMode)
                return kj > 0.05f;
            return kj >= ShotEnergyKJ;
        }

        internal static float PeekShotCost(Aircraft ac, PowerSupply ps)
        {
            if (ChargeMode)
                return ReadKJ(ac, ps);
            return ShotEnergyKJ;
        }

        internal static float PeekMuzzle(Aircraft ac, PowerSupply ps)
        {
            return BaseMuzzle * SpeedMulForKj(PeekShotCost(ac, ps));
        }

        internal static void EnsureCapacitor(Aircraft ac, PowerSupply ps)
        {
            if (ac == null || ps == null)
                return;
            int id = ac.GetInstanceID();
            try
            {
                ps.enabled = true;
            }
            catch
            {
            }
            float max = 0f;
            if (MaxChargeField != null)
            {
                try
                {
                    object v = MaxChargeField.GetValue(ps);
                    if (v is float)
                        max = (float)v;
                }
                catch
                {
                }
            }
            if (max < MinCapKJ)
            {
                try
                {
                    ps.ModifyCapacitance(MinCapKJ - max);
                }
                catch
                {
                    if (MaxChargeField != null)
                        MaxChargeField.SetValue(ps, MinCapKJ);
                }
            }
            bool first = !CapReady.Contains(id);
            float have = 0f;
            try
            {
                have = ps.GetChargeKJ();
            }
            catch
            {
            }
            if (first && have < SeedKJ)
                WriteKJ(ac, ps, SeedKJ);
            if (first)
            {
                CapReady.Add(id);
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("capacitor ready ac=" + ac.name
                        + " was=" + have.ToString("F0")
                        + " now=" + ReadKJ(ac, ps).ToString("F0"));
            }
        }

        internal static void TickBattery(Aircraft ac, PowerSupply ps, float dt)
        {
            if (ps != null)
            {
                try
                {
                    ps.enabled = true;
                }
                catch
                {
                }
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
            if (ChargeMode)
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
                try
                {
                    return ps.GetChargeKJ();
                }
                catch
                {
                }
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
                try
                {
                    ps.enabled = true;
                }
                catch
                {
                }
                try
                {
                    if (ChargeChangedField != null)
                    {
                        Action<PowerSupply> ev = ChargeChangedField.GetValue(ps) as Action<PowerSupply>;
                        if (ev != null)
                            ev(ps);
                    }
                }
                catch
                {
                }
                return;
            }
            if (ac != null)
                FakeBatt[ac.GetInstanceID()] = kj;
        }

        internal static void ApplyShotBallistics(Gun gun)
        {
            RestoreShotBallistics(gun);
            if (gun == null)
                return;
            float take = LastShotKJ;
            if (take < 0.05f)
                take = ShotEnergyKJ;
            float speedMul = SpeedMulForKj(take);
            float eMul = speedMul * speedMul;
            float v = BaseMuzzle * speedMul;
            WeaponInfo info = gun.info;
            if (info != null)
            {
                _savedPierce = info.pierceDamage;
                _savedBlast = info.blastDamage;
                _savedInfoMuzzle = info.muzzleVelocity;
                info.pierceDamage = BasePierce * eMul;
                info.blastDamage = BaseBlast * eMul;
                info.muzzleVelocity = v;
            }
            if (GunMuzzleField != null)
            {
                object cur = GunMuzzleField.GetValue(gun);
                _savedGunMuzzle = cur is float ? (float)cur : BaseMuzzle;
                GunMuzzleField.SetValue(gun, v);
            }
            _ballisticsOn = true;
        }

        internal static void RestoreShotBallistics(Gun gun)
        {
            if (!_ballisticsOn)
                return;
            _ballisticsOn = false;
            WeaponInfo info = gun != null ? gun.info : Info;
            if (info != null)
            {
                info.pierceDamage = _savedPierce > 0.1f ? _savedPierce : BasePierce;
                info.blastDamage = _savedBlast > 0.1f ? _savedBlast : BaseBlast;
                info.muzzleVelocity = _savedInfoMuzzle > 1f ? _savedInfoMuzzle : BaseMuzzle;
            }
            if (gun != null && GunMuzzleField != null)
            {
                float restore = _savedGunMuzzle > 1f ? _savedGunMuzzle : BaseMuzzle;
                GunMuzzleField.SetValue(gun, restore);
            }
        }

        internal static PowerSupply PowerOf(Unit unit)
        {
            Aircraft ac = unit as Aircraft;
            if (ac == null && unit != null)
                ac = unit.GetComponentInParent<Aircraft>();
            if (ac == null)
                return null;
            PowerSupply ps = null;
            try
            {
                ps = ac.GetPowerSupply();
            }
            catch
            {
            }
            if (ps == null)
                ps = ac.GetComponentInChildren<PowerSupply>(true);
            return ps;
        }

        internal static Aircraft AircraftOf(Unit unit)
        {
            Aircraft ac = unit as Aircraft;
            if (ac == null && unit != null)
                ac = unit.GetComponentInParent<Aircraft>();
            return ac;
        }

        internal static void Ensure()
        {
            if (_running)
                return;
            if (!MenuReady())
                return;
            _running = true;
            try
            {
                if (!Injected)
                    CreateIfNeeded();
                if (Injected)
                    InjectHardpoints();
                EnsureGuidedShell();
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
            if (Plugin.AddToAllHardpoints == null || !Plugin.AddToAllHardpoints.Value)
                return 0;
            if (Plugin.IsShipWeaponManager(wm))
                return 0;
            int added = 0;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                HardpointSet hs = wm.hardpointSets[i];
                if (hs == null)
                    continue;
                if (Plugin.IsNavalHardpoint(hs))
                    continue;
                if (hs.hardpoints == null || hs.hardpoints.Count == 0)
                    continue;
                if (hs.weaponOptions == null)
                    hs.weaponOptions = new List<WeaponMount>();
                if (hs.weaponOptions.Contains(Mount))
                    continue;
                hs.weaponOptions.Add(Mount);
                added++;
            }
            return added;
        }

        internal static void FilterAvailable(HardpointSet hs, List<WeaponMount> list, HashSet<WeaponMount> have)
        {
            if (list == null || Mount == null || Mount.prefab == null)
                return;
            if (Plugin.AddToAllHardpoints == null || !Plugin.AddToAllHardpoints.Value)
                return;
            if (Plugin.IsNavalHardpoint(hs))
                return;
            if (have != null && !have.Add(Mount))
                return;
            if (list.Contains(Mount))
                return;
            list.Add(Mount);
        }

        internal static bool AircraftHasPod(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            Gun[] guns = aircraft.GetComponentsInChildren<Gun>(true);
            if (guns != null)
            {
                for (int i = 0; i < guns.Length; i++)
                {
                    if (IsOurs(guns[i]))
                        return true;
                }
            }
            WeaponManager wm = null;
            try
            {
                wm = aircraft.weaponManager;
            }
            catch
            {
            }
            if (wm == null)
                wm = aircraft.GetComponent<WeaponManager>();
            if (wm == null)
                wm = aircraft.GetComponentInChildren<WeaponManager>(true);
            if (wm != null)
            {
                try
                {
                    if (wm.currentWeaponStation != null && IsOurs(wm.currentWeaponStation.WeaponInfo))
                        return true;
                }
                catch
                {
                }
                if (wm.hardpointSets != null)
                {
                    for (int i = 0; i < wm.hardpointSets.Length; i++)
                    {
                        HardpointSet hs = wm.hardpointSets[i];
                        if (hs == null)
                            continue;
                        if (IsOurs(hs.weaponMount))
                            return true;
                    }
                }
            }
            try
            {
                if (aircraft.weaponStations != null)
                {
                    for (int i = 0; i < aircraft.weaponStations.Count; i++)
                    {
                        WeaponStation st = aircraft.weaponStations[i];
                        if (st == null)
                            continue;
                        if (IsOurs(st.WeaponInfo))
                            return true;
                        if (st.Weapons == null)
                            continue;
                        for (int j = 0; j < st.Weapons.Count; j++)
                        {
                            if (IsOurs(st.Weapons[j] as Gun))
                                return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        internal static Gun FindOurGun(Aircraft aircraft)
        {
            if (aircraft == null)
                return null;
            try
            {
                WeaponManager wm = aircraft.weaponManager;
                if (wm != null && wm.currentWeaponStation != null
                    && wm.currentWeaponStation.Weapons != null)
                {
                    List<Weapon> list = wm.currentWeaponStation.Weapons;
                    for (int i = 0; i < list.Count; i++)
                    {
                        Gun g = list[i] as Gun;
                        if (IsOurs(g))
                            return g;
                    }
                }
            }
            catch
            {
            }
            Gun[] guns = aircraft.GetComponentsInChildren<Gun>(true);
            if (guns == null)
                return null;
            for (int i = 0; i < guns.Length; i++)
            {
                if (IsOurs(guns[i]))
                    return guns[i];
            }
            return null;
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
                    if (IsOurs(list[i] as Gun))
                        return true;
                }
            }
            catch
            {
            }
            return false;
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
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("Main menu loaded; delaying railcannon inject.");
                return false;
            }
            return Time.unscaledTime >= _menuReadyAt;
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
                ApplyStats(existing, existing.info, existing.prefab);
                RegisterLookups(enc, existing);
                Injected = true;
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("RailcannonPod: adopted existing " + MountKey);
                return;
            }

            WeaponMount donor = FindMount(enc, DonorMountKey);
            if (donor == null || donor.prefab == null)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("RailcannonPod: waiting for donor " + DonorMountKey);
                return;
            }
            WeaponInfo rail = FindInfo(DonorInfoName);
            if (rail == null && donor.info != null)
                rail = donor.info;

            WeaponMount clone = UnityEngine.Object.Instantiate(donor);
            clone.name = "WeaponMount_" + MountKey;
            clone.jsonKey = MountKey;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            clone.mountName = DisplayName;
            clone.ammo = 55;
            clone.emptyCost = 2.5f;
            clone.emptyMass = 1f;
            clone.mass = 1f;
            if (EventContentField != null)
                EventContentField.SetValue(clone, false);

            WeaponInfo info = UnityEngine.Object.Instantiate(rail != null ? rail : donor.info);
            info.name = InfoName;
            info.hideFlags = HideFlags.DontUnloadUnusedAsset;
            ApplyWeaponInfo(info);

            GameObject prefab = BuildPrefab(donor.prefab, info);
            if (prefab == null)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("RailcannonPod: prefab clone failed");
                return;
            }
            clone.prefab = prefab;
            clone.info = info;
            info.weaponPrefab = prefab;

            ApplyGunTuning(prefab, info);
            Icon.Apply(info);

            Mount = clone;
            Info = info;
            if (enc.weaponMounts != null && !enc.weaponMounts.Contains(clone))
                enc.weaponMounts.Add(clone);
            RegisterLookups(enc, clone);
            Injected = true;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("RailcannonPod: created " + DisplayName + " (" + MountKey + ")");
        }

        private static void ApplyStats(WeaponMount mount, WeaponInfo info, GameObject prefab)
        {
            if (mount != null)
            {
                mount.mountName = DisplayName;
                mount.ammo = 55;
                mount.emptyCost = 2.5f;
                mount.emptyMass = 1f;
                mount.mass = 1f;
                if (EventContentField != null)
                    EventContentField.SetValue(mount, false);
            }
            ApplyWeaponInfo(info);
            ApplyGunTuning(prefab, info);
            LightenPhysics(prefab);
            HaloVisual.Apply(prefab);
            ApplyModelScale(prefab);
        }

        private static void ApplyWeaponInfo(WeaponInfo info)
        {
            if (info == null)
                return;
            info.weaponName = DisplayName;
            info.shortName = ShortName;
            Icon.Apply(info);
            info.description = Description;
            info.fireInterval = 1f;
            info.pierceDamage = BasePierce;
            info.blastDamage = BaseBlast;
            info.muzzleVelocity = BaseMuzzle;
            info.costPerRound = 0.02f;
            info.massPerRound = 0f;
            info.energy = true;
            info.boresight = true;
            info.gun = true;
            info.missile = false;
            info.nuclear = false;
            TargetRequirements tr = info.targetRequirements;
            tr.maxRange = 20000f;
            info.targetRequirements = tr;
        }

        private static GameObject BuildPrefab(GameObject donorPrefab, WeaponInfo info)
        {
            if (donorPrefab == null)
                return null;
            if (_holder == null)
            {
                _holder = new GameObject("RailcannonPod_Catalog");
                UnityEngine.Object.DontDestroyOnLoad(_holder);
                _holder.hideFlags = HideFlags.HideAndDontSave;
                _holder.SetActive(false);
            }
            GameObject pod = UnityEngine.Object.Instantiate(donorPrefab);
            pod.name = MountKey;
            pod.hideFlags = HideFlags.DontUnloadUnusedAsset;
            pod.transform.SetParent(_holder.transform, false);

            LightenPhysics(pod);
            HaloVisual.Apply(pod);
            ApplyModelScale(pod);

            Gun gun = pod.GetComponentInChildren<Gun>(true);
            if (gun != null)
            {
                gun.info = info;
                gun.attachedUnit = null;
                gun.velocityInherit = null;
                SetObj(gun, "recoilSound", null);
                LaunchFx.Prepare(gun);
            }
            return pod;
        }

        internal static void LightenSpawned(GameObject spawned)
        {
            LightenPhysics(spawned);
            HaloVisual.Apply(spawned);
            ApplyModelScale(spawned);
            if (spawned != null)
            {
                Gun g = spawned.GetComponentInChildren<Gun>(true);
                LaunchFx.Prepare(g);
                ApplyGuidedProjectile(g);
            }
        }

        private static void ApplyModelScale(GameObject go)
        {
            if (go == null)
                return;
            go.transform.localScale = new Vector3(ModelScale, ModelScale, ModelScale);
        }

        private static void LightenPhysics(GameObject go)
        {
            if (go == null)
                return;
            StripShipBits(go);
            Joint[] joints = go.GetComponentsInChildren<Joint>(true);
            if (joints != null)
            {
                for (int i = 0; i < joints.Length; i++)
                    DestroyGo(joints[i]);
            }
            Rigidbody[] rbs = go.GetComponentsInChildren<Rigidbody>(true);
            if (rbs != null)
            {
                for (int i = 0; i < rbs.Length; i++)
                    DestroyGo(rbs[i]);
            }
            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                    DestroyGo(cols[i]);
            }
            UnitPart[] parts = go.GetComponentsInChildren<UnitPart>(true);
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == null)
                        continue;
                    parts[i].mass = 0f;
                    DestroyGo(parts[i]);
                }
            }
            AeroPart[] aeros = go.GetComponentsInChildren<AeroPart>(true);
            if (aeros != null)
            {
                for (int i = 0; i < aeros.Length; i++)
                    DestroyGo(aeros[i]);
            }
        }

        private static void DestroyGo(UnityEngine.Object obj)
        {
            if (obj == null)
                return;
            try
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
            catch
            {
                UnityEngine.Object.Destroy(obj);
            }
        }

        private static void StripShipBits(GameObject go)
        {
            if (go == null)
                return;
            MonoBehaviour[] comps = go.GetComponentsInChildren<MonoBehaviour>(true);
            if (comps == null)
                return;
            for (int i = 0; i < comps.Length; i++)
            {
                MonoBehaviour c = comps[i];
                if (c == null || c is Gun)
                    continue;
                string n = c.GetType().Name;
                if (n.IndexOf("Turret", StringComparison.Ordinal) >= 0
                    || n.IndexOf("Ship", StringComparison.Ordinal) >= 0
                    || n.IndexOf("Aimable", StringComparison.Ordinal) >= 0
                    || n == "Unit")
                    DestroyGo(c);
            }
        }

        private static void ApplyGunTuning(GameObject prefab, WeaponInfo info)
        {
            if (prefab == null)
                return;
            Gun gun = prefab.GetComponentInChildren<Gun>(true);
            if (gun == null)
                return;
            if (info != null)
                gun.info = info;
            SetObj(gun, "tracerColor", new Color(400f, 200f, 500f, 1f));
            SetFloat(gun, "tracerSize", 10f);
            SetFloat(gun, "bulletSelfDestruct", 30f);
            SetFloat(gun, "bulletSpread", 0f);
            SetFloat(gun, "fireRate", 600f);
            SetFloat(gun, "reloadTime", 4f);
            SetInt(gun, "magazineCapacity", 1);
            SetInt(gun, "magazines", 54);
            SetFloat(gun, "recoilImpulse", 400f);
            SetFloat(gun, "muzzleVelocity", BaseMuzzle);
        }

        private static void RegisterLookups(Encyclopedia enc, WeaponMount mount)
        {
            if (mount == null || string.IsNullOrEmpty(mount.jsonKey))
                return;
            try
            {
                mount.Initialize();
            }
            catch
            {
            }
            if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(mount.jsonKey))
                Encyclopedia.WeaponLookup.Add(mount.jsonKey, mount);
            if (enc != null && enc.IndexLookup != null)
            {
                INetworkDefinition net = mount;
                if (!enc.IndexLookup.Contains(net))
                {
                    net.LookupIndex = enc.IndexLookup.Count;
                    enc.IndexLookup.Add(net);
                }
            }
        }

        private static void InjectHardpoints()
        {
            if (Plugin.AddToAllHardpoints == null || !Plugin.AddToAllHardpoints.Value)
                return;
            WeaponManager[] managers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            if (managers == null)
                return;
            int added = 0;
            for (int i = 0; i < managers.Length; i++)
            {
                WeaponManager wm = managers[i];
                if (wm == null)
                    continue;
                added += InjectIntoWeaponManager(wm);
            }
            if (added > 0 && Plugin.Log != null)
                Plugin.Log.LogInfo("RailcannonPod: added to " + added + " aircraft hardpoints");
        }

        private static WeaponMount FindMount(Encyclopedia enc, string key)
        {
            if (enc != null && enc.weaponMounts != null)
            {
                for (int i = 0; i < enc.weaponMounts.Count; i++)
                {
                    WeaponMount m = enc.weaponMounts[i];
                    if (m != null && string.Equals(m.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                        return m;
                }
            }
            if (Encyclopedia.WeaponLookup != null && Encyclopedia.WeaponLookup.ContainsKey(key))
                return Encyclopedia.WeaponLookup[key];
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            if (all == null)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount m = all[i];
                if (m != null && string.Equals(m.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        private static WeaponInfo FindInfo(string assetName)
        {
            WeaponInfo[] all = Resources.FindObjectsOfTypeAll<WeaponInfo>();
            if (all == null)
                return null;
            WeaponInfo named = null;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponInfo w = all[i];
                if (w == null)
                    continue;
                if (w.name == assetName)
                    return w;
                if (named == null && w.weaponName != null
                    && w.weaponName.IndexOf("Railgun", StringComparison.OrdinalIgnoreCase) >= 0)
                    named = w;
            }
            return named;
        }

        private static void SetFloat(object obj, string field, float value)
        {
            FieldInfo f = AccessTools.Field(obj.GetType(), field);
            if (f != null)
                f.SetValue(obj, value);
        }

        private static void SetInt(object obj, string field, int value)
        {
            FieldInfo f = AccessTools.Field(obj.GetType(), field);
            if (f != null)
                f.SetValue(obj, value);
        }

        private static void SetObj(object obj, string field, object value)
        {
            FieldInfo f = AccessTools.Field(obj.GetType(), field);
            if (f != null)
                f.SetValue(obj, value);
        }
    }
}
