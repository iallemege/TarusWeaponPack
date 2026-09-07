using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RR106
{
    internal static class Rifle
    {
        internal const string MountKey = "gun_106mm_rr";
        internal const string InfoName = "RR106_Info";
        internal const string DisplayName = "106mm Recoilless Rifle";
        internal const string ShortName = "RR 106MM";
        internal const string Description =
            "This is a compact recoilless weapon developed by Tarus Electronautic in 2072. "
            + "It fires 106mm ammunition. A full capacitor dump scales muzzle velocity "
            + "and kinetic energy with stored charge, and it can also fire on metered pulses. "
            + "The gun also has guided, recon, and charge-guided munitions.";
        internal const string DonorMountKey = "gun_57mm_pod";
        internal const string DonorInfoName = "Railgun1";
        internal const float ShotEnergyKJ = 50f;
        internal const float BasePierce = 1500f;
        internal const float BaseBlast = 6f;
        internal const float BaseMuzzle = 1920f;
        internal const float ModelScale = 1f;
        internal const float MaxSpeedMul = 8f;
        internal const float LongRangeM = 100000f;
        internal const float NormalRangeM = 20000f;
        internal const string ReconKey = "RR106_Recon";
        internal const string GuidedKey = "RR106_Guided100";
        internal const float ReconRadius = 15000f;

        internal enum RifleFireMode
        {
            Normal = 0,
            Charge = 1,
            Guided = 2,
            Recon = 3,
            ChargeGuided = 4
        }

        internal static bool Injected;
        internal static WeaponMount Mount;
        internal static WeaponInfo Info;
        internal static RifleFireMode Mode;
        internal static float LastShotKJ;

        internal static bool Ready
        {
            get { return Injected; }
        }

        internal static bool ChargeMode
        {
            get { return Mode == RifleFireMode.Charge; }
        }

        internal static bool GuidedMode
        {
            get { return Mode == RifleFireMode.Guided; }
        }

        internal static bool ReconMode
        {
            get { return Mode == RifleFireMode.Recon; }
        }

        internal static bool ChargeGuidedMode
        {
            get { return Mode == RifleFireMode.ChargeGuided; }
        }

        internal static bool DumpsCapacitor
        {
            get { return ChargeMode || ChargeGuidedMode; }
        }

        internal static bool UsesGuided
        {
            get { return GuidedMode || ReconMode || ChargeGuidedMode; }
        }

        internal static bool LongRangeMode
        {
            get { return UsesGuided; }
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
        private static readonly FieldInfo GunMuzzlesField =
            AccessTools.Field(typeof(Gun), "muzzles");
        private static readonly FieldInfo GunParticlesField =
            AccessTools.Field(typeof(Gun), "muzzleParticles");
        private static readonly FieldInfo GunTracerSeedField =
            AccessTools.Field(typeof(Gun), "tracerSeed");
        private static readonly FieldInfo GunProximityField =
            AccessTools.Field(typeof(Gun), "proximityTimer");
        private static readonly FieldInfo WeaponMountField =
            AccessTools.Field(typeof(Weapon), "mount");
        private static readonly FieldInfo MissileDefField =
            AccessTools.Field(typeof(Unit), "definition");
        private static readonly FieldInfo SeekerRadiusField =
            AccessTools.Field(typeof(OpticalSeekerShell), "searchRadius");
        private static readonly FieldInfo SeekerMaxSpdField =
            AccessTools.Field(typeof(OpticalSeekerShell), "maxTargetSpeed");
        private static readonly FieldInfo MissileGLimitField =
            AccessTools.Field(typeof(Missile), "gLimit");
        private static readonly FieldInfo MissileImpactFuseField =
            AccessTools.Field(typeof(Missile), "impactFuse");
        private static readonly FieldInfo HqDiscoverField =
            AccessTools.Field(typeof(FactionHQ), "onDiscoverUnit");
        private static MissileDefinition _guidedShell;
        private static bool _guidedLogged;
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
            if (Info != null && object.ReferenceEquals(info, Info))
                return true;
            if (info.name == InfoName)
                return true;
            if (info.weaponName == DisplayName)
                return true;
            if (info.shortName == ShortName)
                return true;
            if (info.weaponName != null
                && info.weaponName.IndexOf("106mm Recoilless", StringComparison.OrdinalIgnoreCase) >= 0)
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
            if (mount != null && string.Equals(mount.jsonKey, MountKey, StringComparison.OrdinalIgnoreCase))
                return true;
            if (IsOurs(gun.info))
                return true;
            return IsOurs(mount);
        }

        internal static void BindGun(Gun gun)
        {
            if (gun == null)
                return;
            if (Info != null)
                gun.info = Info;
        }

        internal static void CycleMode()
        {
            int n = (int)Mode + 1;
            if (n > 4)
                n = 0;
            Mode = (RifleFireMode)n;
        }

        internal static string ModeLabel()
        {
            if (Mode == RifleFireMode.Charge)
                return "CHARGE";
            if (Mode == RifleFireMode.Guided)
                return "GUIDED";
            if (Mode == RifleFireMode.Recon)
                return "RECON";
            if (Mode == RifleFireMode.ChargeGuided)
                return "CHG+GUIDE";
            return "NORMAL";
        }

        internal static Color ModeColor()
        {
            if (Mode == RifleFireMode.Charge)
                return new Color(1f, 0.78f, 0.25f, 1f);
            if (Mode == RifleFireMode.Guided)
                return new Color(0.45f, 0.92f, 1f, 1f);
            if (Mode == RifleFireMode.Recon)
                return new Color(0.95f, 0.95f, 0.45f, 1f);
            if (Mode == RifleFireMode.ChargeGuided)
                return new Color(1f, 0.55f, 0.95f, 1f);
            return new Color(0.7f, 1f, 0.75f, 1f);
        }

        internal static Color ModePipColor()
        {
            if (Mode == RifleFireMode.Charge)
                return new Color(1f, 0.72f, 0.2f, 0.95f);
            if (Mode == RifleFireMode.Guided)
                return new Color(0.4f, 0.9f, 1f, 0.95f);
            if (Mode == RifleFireMode.Recon)
                return new Color(0.95f, 0.95f, 0.4f, 0.95f);
            if (Mode == RifleFireMode.ChargeGuided)
                return new Color(1f, 0.5f, 0.95f, 0.95f);
            return new Color(0.4f, 1f, 0.55f, 0.95f);
        }

        internal static float PeekRangeKm()
        {
            if (LongRangeMode)
                return LongRangeM / 1000f;
            return NormalRangeM / 1000f;
        }

        internal static void StampCurrentGun(Aircraft aircraft)
        {
            ApplyModeStats(Info);
            Gun ours = FindOurGun(aircraft);
            BindGun(ours);
            if (ours != null && ours.info != null)
                ApplyModeStats(ours.info);
            ApplyGuidedProjectile(ours);
            PushLockTarget(ours);
            if (Mount != null && Mount.prefab != null)
            {
                Gun catalog = Mount.prefab.GetComponentInChildren<Gun>(true);
                BindGun(catalog);
                ApplyGuidedProjectile(catalog);
            }
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
            if (!UsesGuided)
            {
                GunGuidedField.SetValue(gun, null);
                if (GunProximityField != null)
                {
                    try { GunProximityField.SetValue(gun, false); }
                    catch { }
                }
                return;
            }
            MissileDefinition shell = ReconMode ? EnsureReconShell() : EnsureGuidedShell();
            if (shell == null || shell.unitPrefab == null)
            {
                GunGuidedField.SetValue(gun, null);
                return;
            }
            PrepareShellPrefab(shell);
            if (shell.unitPrefab.GetComponent<Rigidbody>() == null)
            {
                GunGuidedField.SetValue(gun, null);
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("RR106: guided prefab has no Rigidbody, firing unguided");
                return;
            }
            GunGuidedField.SetValue(gun, shell);
            if (GunProximityField != null)
            {
                try { GunProximityField.SetValue(gun, true); }
                catch { }
            }
            PushLockTarget(gun);
        }

        internal static void PrepareShellPrefab(MissileDefinition def)
        {
            if (def == null || def.unitPrefab == null)
                return;
            try
            {
                if (!def.unitPrefab.activeSelf)
                    def.unitPrefab.SetActive(true);
            }
            catch
            {
            }
        }

        internal static bool IsOurShellKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return string.Equals(key, GuidedKey, StringComparison.Ordinal)
                || string.Equals(key, ReconKey, StringComparison.Ordinal);
        }

        internal static void WakeShellPrefab(MissileDefinition def)
        {
            PrepareShellPrefab(def);
        }

        internal static void PushLockTarget(Gun gun)
        {
            if (gun == null || !UsesGuided)
                return;
            Unit t = null;
            Aircraft ac = AircraftOf(gun.attachedUnit);
            if (ac != null && ac.weaponManager != null)
            {
                try
                {
                    List<Unit> list = ac.weaponManager.GetTargetList();
                    if (list != null && list.Count > 0)
                        t = list[0];
                }
                catch
                {
                    t = null;
                }
            }
            try { gun.SetTarget(t); }
            catch { }
        }

        private static MissileDefinition _reconShell;

        internal static MissileDefinition EnsureReconShell()
        {
            if (_reconShell != null)
                return _reconShell;
            MissileDefinition d = CloneLongShell(ReconKey, "106mm Recon Shell", true);
            if (d != null)
                _reconShell = d;
            return _reconShell;
        }

        internal static MissileDefinition EnsureGuidedShell()
        {
            if (_guidedShell != null)
                return _guidedShell;
            MissileDefinition d = CloneLongShell(GuidedKey, "106mm Guided Shell", false);
            if (d != null)
                _guidedShell = d;
            return _guidedShell;
        }

        private static MissileDefinition CloneLongShell(string key, string unitName, bool recon)
        {
            MissileDefinition src = Find76mmGuidedShell();
            if (src == null || src.unitPrefab == null)
            {
                if (!_guidedLogged && Plugin.Log != null)
                {
                    _guidedLogged = true;
                    Plugin.Log.LogWarning("RR106: 76mm guided shell not found yet");
                }
                return null;
            }
            MissileDefinition def = UnityEngine.Object.Instantiate(src);
            def.name = key;
            def.jsonKey = key;
            def.unitName = unitName;
            def.unitPrefab = src.unitPrefab;
            def.hideFlags = HideFlags.DontUnloadUnusedAsset;
            PrepareShellPrefab(def);
            RegisterMissileDef(def);
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("RR106: " + unitName + " uses "
                    + (src.unitName != null ? src.unitName : src.jsonKey)
                    + " prefab");
            return def;
        }

        private static void TuneLongShell(MissileDefinition def, bool recon)
        {
            if (def == null || def.unitPrefab == null)
                return;
            ApplySeekerTune(def.unitPrefab, recon);
            ScaleShellVisual(def.unitPrefab, 8f);
        }

        internal static bool IsOursShell(Missile missile)
        {
            if (missile == null)
                return false;
            if (IsReconMissile(missile) || IsGuidedMissile(missile))
                return true;
            try
            {
                if (missile.GetComponent<RR106ShellMark>() != null)
                    return true;
            }
            catch
            {
            }
            return false;
        }

        internal static bool IsGuidedMissile(Missile missile)
        {
            if (missile == null || missile.definition == null)
                return false;
            return string.Equals(missile.definition.jsonKey, GuidedKey, StringComparison.Ordinal);
        }

        internal static void ApplyToSpawnedShell(Missile missile)
        {
            ApplyToSpawnedShell(missile, null);
        }

        internal static void ApplyToSpawnedShell(Missile missile, MissileDefinition fromDef)
        {
            if (missile == null)
                return;
            try
            {
                if (missile.transform != null && missile.transform.position.y < -7000f)
                    return;
            }
            catch
            {
                return;
            }
            bool recon = false;
            bool guided = false;
            if (fromDef != null && !string.IsNullOrEmpty(fromDef.jsonKey))
            {
                recon = string.Equals(fromDef.jsonKey, ReconKey, StringComparison.Ordinal);
                guided = string.Equals(fromDef.jsonKey, GuidedKey, StringComparison.Ordinal);
            }
            if (!recon && !guided)
            {
                recon = IsReconMissile(missile);
                guided = IsGuidedMissile(missile);
            }
            if (!recon && !guided)
            {
                string n = missile.name != null ? missile.name : string.Empty;
                if (n.IndexOf(ReconKey, StringComparison.Ordinal) >= 0)
                    recon = true;
                else if (n.IndexOf(GuidedKey, StringComparison.Ordinal) >= 0)
                    guided = true;
                else
                    return;
            }
            RR106ShellMark mark = missile.GetComponent<RR106ShellMark>();
            if (mark == null)
                mark = Plugin.TryAddBehaviour<RR106ShellMark>(missile.gameObject);
            if (mark != null)
                mark.Recon = recon;
            if (mark != null && mark.Tuned)
                return;
            if (mark != null)
                mark.Tuned = true;
            if (missile.gameObject != null && !missile.gameObject.activeInHierarchy)
            {
                try { missile.gameObject.SetActive(true); }
                catch { }
            }
            try
            {
                if (!missile.enabled)
                    missile.enabled = true;
            }
            catch
            {
            }
            MissileDefinition def = recon ? EnsureReconShell() : EnsureGuidedShell();
            if (def != null && MissileDefField != null)
            {
                try { MissileDefField.SetValue(missile, def); }
                catch { }
            }
            ApplySeekerTune(missile.gameObject, recon);
            ScaleShellVisual(missile.gameObject, 8f);
            if (MissileGLimitField != null)
            {
                try { MissileGLimitField.SetValue(missile, 25f); }
                catch { }
            }
            if (recon && MissileImpactFuseField != null)
            {
                try { MissileImpactFuseField.SetValue(missile, true); }
                catch { }
            }
            AddShellTrail(missile.gameObject);
        }

        private static void ApplySeekerTune(GameObject root, bool recon)
        {
            if (root == null)
                return;
            OpticalSeekerShell[] seekers = null;
            try { seekers = root.GetComponentsInChildren<OpticalSeekerShell>(true); }
            catch { seekers = null; }
            if (seekers != null)
            {
                for (int i = 0; i < seekers.Length; i++)
                {
                    OpticalSeekerShell sk = seekers[i];
                    if (sk == null)
                        continue;
                    if (SeekerRadiusField != null)
                    {
                        try { SeekerRadiusField.SetValue(sk, 8000f); }
                        catch { }
                    }
                    if (SeekerMaxSpdField != null)
                    {
                        try { SeekerMaxSpdField.SetValue(sk, 500f); }
                        catch { }
                    }
                    try { sk.proximityFuse = true; }
                    catch { }
                }
            }
            if (!recon)
                return;
            Missile missile = root.GetComponent<Missile>();
            if (missile == null)
                return;
            FieldInfo by = AccessTools.Field(typeof(Missile), "blastYield");
            if (by != null)
            {
                try { by.SetValue(missile, 0.4f); }
                catch { }
            }
            FieldInfo pd = AccessTools.Field(typeof(Missile), "pierceDamage");
            if (pd != null)
            {
                try { pd.SetValue(missile, 0f); }
                catch { }
            }
        }

        private static void ScaleShellVisual(GameObject root, float mul)
        {
            if (root == null || mul < 1.01f)
                return;
            Transform t = root.transform;
            if (t.localScale.x > mul * 0.5f)
                return;
            t.localScale = new Vector3(mul, mul, mul);
        }

        private static void AddShellTrail(GameObject root)
        {
            if (root == null)
                return;
            if (root.GetComponent<TrailRenderer>() != null)
                return;
            TrailRenderer tr = null;
            try { tr = root.AddComponent<TrailRenderer>(); }
            catch { return; }
            if (tr == null)
                return;
            tr.time = 0.45f;
            tr.startWidth = 0.55f;
            tr.endWidth = 0.02f;
            tr.minVertexDistance = 1.5f;
            tr.autodestruct = false;
            Color c = new Color(1f, 0.82f, 0.2f, 1f);
            tr.startColor = c;
            tr.endColor = new Color(c.r, c.g, c.b, 0f);
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null)
                sh = Shader.Find("Unlit/Color");
            if (sh != null)
            {
                Material mat = new Material(sh);
                mat.color = c;
                tr.material = mat;
            }
        }

        private static void RegisterMissileDef(MissileDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.jsonKey))
                return;
            try
            {
                Encyclopedia enc = Plugin.GetEncyclopedia();
                if (enc != null && enc.missiles != null && !enc.missiles.Contains(def))
                    enc.missiles.Add(def);
            }
            catch
            {
            }
            try
            {
                if (Encyclopedia.Lookup != null && !Encyclopedia.Lookup.ContainsKey(def.jsonKey))
                    Encyclopedia.Lookup.Add(def.jsonKey, def);
            }
            catch
            {
            }
        }

        internal static bool IsReconMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                RR106ShellMark mark = missile.GetComponent<RR106ShellMark>();
                if (mark != null && mark.Recon)
                    return true;
            }
            catch
            {
            }
            try
            {
                if (missile.definition != null
                    && string.Equals(missile.definition.jsonKey, ReconKey, StringComparison.Ordinal))
                    return true;
            }
            catch
            {
            }
            string n = missile.name;
            if (n != null && n.IndexOf(ReconKey, StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        internal static void PaintRecon(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                if (missile.transform != null && missile.transform.position.y < -7000f)
                    return;
            }
            catch
            {
                return;
            }
            FactionHQ hq = ResolveHq(missile);
            if (hq == null)
                return;
            GlobalPosition origin;
            try { origin = missile.GlobalPosition(); }
            catch { return; }
            List<Unit> units = new List<Unit>(256);
            bool gotGrid = false;
            try
            {
                BattlefieldGrid.GetUnitsInRangeNonAlloc(origin, ReconRadius, units);
                gotGrid = true;
            }
            catch
            {
                units.Clear();
            }
            if (!gotGrid || units.Count == 0)
                CollectUnitsFallback(origin, units);
            int n = 0;
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (!ShouldSpot(u, missile, hq, origin))
                    continue;
                if (SpotUnit(hq, u))
                    n++;
            }
            if (n > 0 && Plugin.Log != null)
                Plugin.Log.LogInfo("RR106 recon painted " + n + " contacts");
        }

        private static FactionHQ ResolveHq(Missile missile)
        {
            try
            {
                if (missile.owner != null && missile.owner.NetworkHQ != null)
                    return missile.owner.NetworkHQ;
            }
            catch
            {
            }
            try
            {
                if (missile.NetworkHQ != null)
                    return missile.NetworkHQ;
            }
            catch
            {
            }
            try
            {
                FactionHQ local;
                if (GameManager.GetLocalHQ(out local) && local != null)
                    return local;
            }
            catch
            {
            }
            return null;
        }

        private static void CollectUnitsFallback(GlobalPosition origin, List<Unit> units)
        {
            if (units == null)
                return;
            units.Clear();
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { all = null; }
            if (all == null)
                return;
            for (int i = 0; i < all.Count; i++)
            {
                Unit u = all[i];
                if (u == null || u.disabled)
                    continue;
                try
                {
                    if (FastMath.InRange(origin, u.GlobalPosition(), ReconRadius))
                        units.Add(u);
                }
                catch
                {
                }
            }
        }

        private static bool ShouldSpot(Unit u, Missile missile, FactionHQ hq, GlobalPosition origin)
        {
            if (u == null || u.disabled || u == missile)
                return false;
            if (u is Missile)
                return false;
            try
            {
                if (missile.owner != null && u == missile.owner)
                    return false;
            }
            catch
            {
            }
            try
            {
                if (u.NetworkHQ != null && u.NetworkHQ == hq)
                    return false;
            }
            catch
            {
            }
            try
            {
                if (!FastMath.InRange(origin, u.GlobalPosition(), ReconRadius))
                    return false;
            }
            catch
            {
                return false;
            }
            return true;
        }

        private static bool SpotUnit(FactionHQ hq, Unit u)
        {
            if (hq == null || u == null)
                return false;
            PersistentID id = u.persistentID;
            bool local = false;
            try
            {
                if (hq.trackingDatabase != null)
                {
                    TrackingInfo existing;
                    if (hq.trackingDatabase.TryGetValue(id, out existing) && existing != null)
                    {
                        existing.UpdateInfo(u.GlobalPosition());
                        local = true;
                    }
                    else if (!hq.trackingDatabase.ContainsKey(id))
                    {
                        TrackingInfo item = new TrackingInfo(u);
                        hq.trackingDatabase.Add(id, item);
                        local = true;
                        try
                        {
                            if (DynamicMap.IsFactionMode(hq, FactionMode.Spectator | FactionMode.Friendly)
                                && SceneSingleton<DynamicMap>.i != null)
                                SceneSingleton<DynamicMap>.i.AddIcon(id);
                        }
                        catch
                        {
                        }
                        try
                        {
                            if (HqDiscoverField != null)
                            {
                                MulticastDelegate ev = HqDiscoverField.GetValue(hq) as MulticastDelegate;
                                if (ev != null)
                                    ev.DynamicInvoke(id);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
            try
            {
                hq.RpcUpdateTrackingInfo(id);
            }
            catch
            {
            }
            return local;
        }

        private static MissileDefinition Find76mmGuidedShell()
        {
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
                        if (CanSpawnGuided(d.unitPrefab))
                            return d;
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
                            if (CanSpawnGuided(gp.unitPrefab))
                                return gp;
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
                    if (!Looks76Guided(d.unitName) && !Looks76Guided(d.jsonKey) && !Looks76(d.unitName))
                        continue;
                    if (CanSpawnGuided(d.unitPrefab))
                        return d;
                }
            }
            return null;
        }

        private static bool CanSpawnGuided(GameObject prefab)
        {
            return HasOpticalShell(prefab) && HasRootRigidbody(prefab);
        }

        private static bool HasRootRigidbody(GameObject prefab)
        {
            if (prefab == null)
                return false;
            try
            {
                return prefab.GetComponent<Rigidbody>() != null;
            }
            catch
            {
                return false;
            }
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
            return ReadKJ(ac, ps) > 0.05f;
        }

        internal static void TopUpForShot(Aircraft ac, PowerSupply ps)
        {
            EnsureCapacitor(ac, ps);
            if (ReadKJ(ac, ps) < 0.05f)
                WriteKJ(ac, ps, SeedKJ);
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
            if (kj < 0.05f)
            {
                LastShotKJ = ShotEnergyKJ;
                return false;
            }
            float take;
            if (DumpsCapacitor)
                take = kj;
            else if (kj < ShotEnergyKJ)
                take = kj;
            else
                take = ShotEnergyKJ;
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
                    Plugin.Log.LogInfo("RR106: adopted existing " + MountKey);
                return;
            }

            WeaponMount donor = FindMount(enc, DonorMountKey);
            if (donor == null || donor.prefab == null)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("RR106: waiting for donor " + DonorMountKey);
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
            clone.ammo = 45;
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
                    Plugin.Log.LogWarning("RR106: prefab clone failed");
                return;
            }
            clone.prefab = prefab;
            clone.info = info;
            info.weaponPrefab = prefab;

            ApplyGunTuning(prefab, info);

            Mount = clone;
            Info = info;
            if (enc.weaponMounts != null && !enc.weaponMounts.Contains(clone))
                enc.weaponMounts.Add(clone);
            RegisterLookups(enc, clone);
            Injected = true;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("RR106: created " + DisplayName + " (" + MountKey + ")");
        }

        private static void ApplyStats(WeaponMount mount, WeaponInfo info, GameObject prefab)
        {
            if (mount != null)
            {
                mount.mountName = DisplayName;
                mount.ammo = 45;
                mount.emptyCost = 2.5f;
                mount.emptyMass = 1f;
                mount.mass = 1f;
                if (EventContentField != null)
                    EventContentField.SetValue(mount, false);
            }
            ApplyWeaponInfo(info);
            ApplyGunTuning(prefab, info);
            LightenPhysics(prefab);
            Visual.Apply(prefab);
            ApplyModelScale(prefab);
        }

        private static void ApplyWeaponInfo(WeaponInfo info)
        {
            if (info == null)
                return;
            info.weaponName = DisplayName;
            info.shortName = ShortName;
            info.description = Description;
            info.fireInterval = 0.1f;
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
            ApplyModeStats(info);
            Icon.Apply(info);
        }

        internal static void ApplyModeStats(WeaponInfo info)
        {
            if (info == null)
                return;
            TargetRequirements tr = info.targetRequirements;
            if (LongRangeMode)
            {
                tr.maxRange = LongRangeM;
                tr.minRange = 800f;
                tr.lineOfSight = false;
                info.overHorizon = true;
                info.gravMult = 0.06f;
                info.dragCoef = 0.002f;
                info.maxSpeed = 2200f;
            }
            else
            {
                tr.maxRange = NormalRangeM;
                tr.minRange = 0f;
                tr.lineOfSight = true;
                info.overHorizon = false;
                info.gravMult = 1f;
                info.dragCoef = 0.02f;
                info.maxSpeed = -1f;
            }
            info.targetRequirements = tr;
            info.energy = true;
            info.laserGuided = false;
            info.muzzleVelocity = BaseMuzzle;
        }

        private static GameObject BuildPrefab(GameObject donorPrefab, WeaponInfo info)
        {
            if (donorPrefab == null)
                return null;
            if (_holder == null)
            {
                _holder = new GameObject("RR106_Catalog");
                UnityEngine.Object.DontDestroyOnLoad(_holder);
                _holder.hideFlags = HideFlags.HideAndDontSave;
                _holder.SetActive(false);
            }
            GameObject pod = UnityEngine.Object.Instantiate(donorPrefab);
            pod.name = MountKey;
            pod.hideFlags = HideFlags.DontUnloadUnusedAsset;
            pod.transform.SetParent(_holder.transform, false);

            LightenPhysics(pod);
            Visual.Apply(pod);
            ApplyModelScale(pod);

            Gun gun = pod.GetComponentInChildren<Gun>(true);
            if (gun != null)
            {
                gun.info = info;
                gun.attachedUnit = null;
                gun.velocityInherit = null;
                SetObj(gun, "recoilSound", null);
            }
            return pod;
        }

        internal static void LightenSpawned(GameObject spawned)
        {
            LightenPhysics(spawned);
            Visual.Apply(spawned);
            ApplyModelScale(spawned);
            ApplyGunTuning(spawned, Info);
            if (spawned != null)
            {
                Gun g = spawned.GetComponentInChildren<Gun>(true);
                BindGun(g);
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
            SetObj(gun, "tracerColor", new Color(400f, 280f, 40f, 1f));
            SetFloat(gun, "tracerSize", 22f);
            SetInt(gun, "tracerRatio", 0);
            if (GunTracerSeedField != null)
            {
                try { GunTracerSeedField.SetValue(gun, 1); }
                catch { }
            }
            EnsureMuzzleArrays(gun);
            SetFloat(gun, "bulletSelfDestruct", 90f);
            SetFloat(gun, "bulletSpread", 0f);
            SetFloat(gun, "fireRate", 600f);
            SetFloat(gun, "fireInterval", 0.1f);
            SetFloat(gun, "reloadTime", 4f);
            SetInt(gun, "magazineCapacity", 1);
            SetInt(gun, "magazines", 54);
            SetFloat(gun, "recoilImpulse", 400f);
            SetFloat(gun, "muzzleVelocity", BaseMuzzle);
        }

        internal static void ForceVisibleShot(Gun gun)
        {
            if (gun == null)
                return;
            SetObj(gun, "tracerColor", new Color(400f, 280f, 40f, 1f));
            SetFloat(gun, "tracerSize", 22f);
            SetInt(gun, "tracerRatio", 0);
            if (GunTracerSeedField != null)
            {
                try { GunTracerSeedField.SetValue(gun, 1); }
                catch { }
            }
            EnsureMuzzleArrays(gun);
        }

        private static void EnsureMuzzleArrays(Gun gun)
        {
            if (gun == null)
                return;
            if (GunMuzzlesField != null)
            {
                Transform[] muzzles = null;
                try { muzzles = GunMuzzlesField.GetValue(gun) as Transform[]; }
                catch { muzzles = null; }
                bool empty = muzzles == null || muzzles.Length == 0;
                if (!empty)
                {
                    empty = true;
                    for (int i = 0; i < muzzles.Length; i++)
                    {
                        if (muzzles[i] != null)
                        {
                            empty = false;
                            break;
                        }
                    }
                }
                if (empty)
                {
                    try { GunMuzzlesField.SetValue(gun, new Transform[] { gun.transform }); }
                    catch { }
                }
            }
            if (GunParticlesField != null)
            {
                ParticleSystem[] parts = null;
                try { parts = GunParticlesField.GetValue(gun) as ParticleSystem[]; }
                catch { parts = null; }
                if (parts == null)
                {
                    try { GunParticlesField.SetValue(gun, new ParticleSystem[0]); }
                    catch { }
                }
                else
                {
                    bool anyNull = false;
                    int keep = 0;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i] != null)
                            keep++;
                        else
                            anyNull = true;
                    }
                    if (anyNull)
                    {
                        ParticleSystem[] trimmed = new ParticleSystem[keep];
                        int w = 0;
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i] == null)
                                continue;
                            trimmed[w] = parts[i];
                            w++;
                        }
                        try { GunParticlesField.SetValue(gun, trimmed); }
                        catch { }
                    }
                }
            }
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
                Plugin.Log.LogInfo("RR106: added to " + added + " aircraft hardpoints");
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

    public sealed class RR106ShellMark : MonoBehaviour
    {
        public bool Tuned;
        public bool Recon;
        private float _nextPaint;

        private void FixedUpdate()
        {
            if (!Recon)
                return;
            if (Time.timeSinceLevelLoad < _nextPaint)
                return;
            _nextPaint = Time.timeSinceLevelLoad + 0.25f;
            Missile missile = GetComponent<Missile>();
            if (missile == null || missile.disabled)
                return;
            try { Rifle.PaintRecon(missile); }
            catch { }
        }

        private void OnDisable()
        {
            if (!Recon)
                return;
            Missile missile = GetComponent<Missile>();
            if (missile == null)
                return;
            try { Rifle.PaintRecon(missile); }
            catch { }
        }
    }
}
