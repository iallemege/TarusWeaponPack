using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace KMO2
{
    /// <summary>
    /// 5 kt G-element burst: shatter-in-radius like the G Factor Bomb, shockwave VFX only.
    /// </summary>
    internal static class GBurst
    {
        internal const float NukeYield5kt = 5000000f;
        internal const float VisualYieldKt = 5f;

        private static readonly FieldInfo WarheadField =
            AccessTools.Field(typeof(Missile), "warhead");
        private static readonly FieldInfo DetonatedField =
            AccessTools.Field(typeof(Missile.Warhead), "detonated");
        private static readonly FieldInfo WhArmedField =
            AccessTools.Field(typeof(Missile.Warhead), "Armed");
        private static readonly FieldInfo WhAirField =
            AccessTools.Field(typeof(Missile.Warhead), "airEffect");
        private static readonly FieldInfo WhArmorField =
            AccessTools.Field(typeof(Missile.Warhead), "armorEffect");
        private static readonly FieldInfo WhTerrainField =
            AccessTools.Field(typeof(Missile.Warhead), "terrainEffect");
        private static readonly FieldInfo WhUnderField =
            AccessTools.Field(typeof(Missile.Warhead), "underwaterEffect");
        private static readonly FieldInfo WhWaterField =
            AccessTools.Field(typeof(Missile.Warhead), "waterSurfaceEffect");
        private static readonly FieldInfo ShockYieldField =
            AccessTools.Field(typeof(Shockwave), "yieldKilotons");
        private static readonly FieldInfo ShockVaporGoField =
            AccessTools.Field(typeof(Shockwave), "vaporCloud");
        private static readonly FieldInfo ShockVaporMatField =
            AccessTools.Field(typeof(Shockwave), "vaporCloudMat");

        private static readonly Color ShockAlbedo = new Color(0.82f, 0.66f, 1f, 1f);
        private static readonly Color ShockEmit = new Color(7.2f, 3.8f, 14.5f, 1f);
        private static readonly MaterialPropertyBlock ShockMpb = new MaterialPropertyBlock();
        private static readonly HashSet<int> PurpleShockIds = new HashSet<int>();
        private static readonly HashSet<int> TintedRendererIds = new HashSet<int>();

        private static GameObject _nukeFx;
        private static float _nextWarm;
        private static bool _shattering;
        private static Vector3 _shatterOrigin;
        private static float _shatterRadiusSq;
        private static float _shatterUntil;
        private static int _skipPostfixFrame = -1;

        internal static float ShockRadiusM()
        {
            return Mathf.Pow(VisualYieldKt * 1000000f, 1f / 3f) * 13f;
        }

        internal static void Warm()
        {
            if (_nukeFx != null)
                return;
            if (Time.unscaledTime < _nextWarm)
                return;
            _nextWarm = Time.unscaledTime + 2f;
            ResolveNukeFx();
        }

        internal static void PrepareMissile(Missile missile)
        {
            if (missile == null)
                return;
            try { missile.Arm(); }
            catch { }
            object wh = null;
            if (WarheadField != null)
            {
                try { wh = WarheadField.GetValue(missile); }
                catch { wh = null; }
            }
            if (wh == null)
                return;
            if (WhArmedField != null)
            {
                try { WhArmedField.SetValue(wh, true); }
                catch { }
            }
            GameObject fx = ResolveNukeFx();
            if (fx == null)
                return;
            if (WhAirField != null)
            {
                try { WhAirField.SetValue(wh, fx); }
                catch { }
            }
            if (WhArmorField != null)
            {
                try { WhArmorField.SetValue(wh, fx); }
                catch { }
            }
            if (WhTerrainField != null)
            {
                try { WhTerrainField.SetValue(wh, fx); }
                catch { }
            }
            if (WhUnderField != null)
            {
                try { WhUnderField.SetValue(wh, fx); }
                catch { }
            }
            if (WhWaterField != null)
            {
                try
                {
                    if (WhWaterField.GetValue(wh) == null)
                        WhWaterField.SetValue(wh, fx);
                }
                catch { }
            }
        }

        internal static bool ConsumeOursDetonate(Missile.Warhead warhead, Rigidbody rb,
            PersistentID ownerID, Vector3 position, bool armed)
        {
            Missile missile = MissileFromRb(rb);
            if (!Munition.IsMissile(missile))
                return false;
            if (!armed)
                return false;
            if (DetonatedField != null)
            {
                try { DetonatedField.SetValue(warhead, true); }
                catch { }
            }
            SpawnShockOnly(position, ownerID);
            NoteShatter(position, ShockRadiusM());
            _skipPostfixFrame = Time.frameCount;
            return true;
        }

        internal static void AfterVanillaDetonate(Rigidbody rb, PersistentID ownerID, Vector3 position)
        {
            if (Time.frameCount == _skipPostfixFrame)
                return;
            Missile missile = MissileFromRb(rb);
            if (!Munition.IsMissile(missile))
                return;
            AdoptNearbyShock(position, ownerID);
            NoteShatter(position, ShockRadiusM());
        }

        internal static bool IsOursShock(Shockwave sw)
        {
            if (sw == null)
                return false;
            try
            {
                if (sw.GetComponentInParent<Kmo2ShockMark>() != null)
                    return true;
            }
            catch { }
            int id = 0;
            try { id = sw.GetInstanceID(); }
            catch { return false; }
            return PurpleShockIds.Contains(id);
        }

        internal static void PrepareShockwave(Shockwave sw)
        {
            if (sw == null)
                return;
            if (ShockYieldField != null)
            {
                try { ShockYieldField.SetValue(sw, VisualYieldKt); }
                catch { }
            }
            GameObject root = null;
            try
            {
                Kmo2ShockMark mark = sw.GetComponentInParent<Kmo2ShockMark>();
                if (mark != null)
                    root = mark.gameObject;
            }
            catch { }
            if (root == null && sw.transform != null)
            {
                if (sw.transform.root != null)
                    root = sw.transform.root.gameObject;
                else
                    root = sw.gameObject;
            }
            KeepOnlyShockwave(root);
            MarkPurple(sw);
        }

        internal static void TintIfOurs(Shockwave sw)
        {
            if (!IsOursShock(sw))
                return;
            ApplyShockTint(sw);
        }

        private static Missile MissileFromRb(Rigidbody rb)
        {
            if (rb == null)
                return null;
            Missile m = null;
            try { m = rb.GetComponent<Missile>(); }
            catch { m = null; }
            if (m != null)
                return m;
            try { return rb.GetComponentInParent<Missile>(); }
            catch { return null; }
        }

        private static void SpawnShockOnly(Vector3 position, PersistentID ownerID)
        {
            GameObject prefab = ResolveNukeFx();
            if (prefab == null)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("KMO-2: nuke FX prefab missing, shatter only");
                return;
            }
            GameObject fx = null;
            try { fx = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity); }
            catch { fx = null; }
            if (fx == null)
                return;
            Plugin.TryAddBehaviour<Kmo2ShockMark>(fx);
            KeepOnlyShockwave(fx);
            Shockwave[] waves = null;
            try { waves = fx.GetComponentsInChildren<Shockwave>(true); }
            catch { waves = null; }
            if (waves == null)
                return;
            for (int i = 0; i < waves.Length; i++)
            {
                Shockwave sw = waves[i];
                if (sw == null)
                    continue;
                sw.enabled = true;
                if (ShockYieldField != null)
                {
                    try { ShockYieldField.SetValue(sw, VisualYieldKt); }
                    catch { }
                }
                try { sw.SetOwner(ownerID, VisualYieldKt); }
                catch { }
                MarkPurple(sw);
            }
        }

        private static void AdoptNearbyShock(Vector3 position, PersistentID ownerID)
        {
            Shockwave[] waves = null;
            try { waves = UnityEngine.Object.FindObjectsOfType<Shockwave>(); }
            catch { waves = null; }
            if (waves == null)
                return;
            for (int i = 0; i < waves.Length; i++)
            {
                Shockwave sw = waves[i];
                if (sw == null || sw.transform == null)
                    continue;
                if ((sw.transform.position - position).sqrMagnitude > 2500f)
                    continue;
                GameObject root = sw.gameObject;
                try
                {
                    if (sw.transform.root != null)
                        root = sw.transform.root.gameObject;
                }
                catch { }
                Plugin.TryAddBehaviour<Kmo2ShockMark>(root);
                KeepOnlyShockwave(root);
                if (ShockYieldField != null)
                {
                    try { ShockYieldField.SetValue(sw, VisualYieldKt); }
                    catch { }
                }
                try { sw.SetOwner(ownerID, VisualYieldKt); }
                catch { }
                MarkPurple(sw);
            }
        }

        private static void KeepOnlyShockwave(GameObject root)
        {
            if (root == null)
                return;
            MushroomCloud[] clouds = null;
            try { clouds = root.GetComponentsInChildren<MushroomCloud>(true); }
            catch { clouds = null; }
            if (clouds != null)
            {
                for (int i = 0; i < clouds.Length; i++)
                {
                    MushroomCloud mc = clouds[i];
                    if (mc == null)
                        continue;
                    try { mc.enabled = false; }
                    catch { }
                    if (!KeepShockVisual(mc.transform))
                    {
                        try { mc.gameObject.SetActive(false); }
                        catch { }
                    }
                }
            }
            ParticleSystem[] parts = null;
            try { parts = root.GetComponentsInChildren<ParticleSystem>(true); }
            catch { parts = null; }
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    ParticleSystem ps = parts[i];
                    if (ps == null || KeepShockVisual(ps.transform))
                        continue;
                    try { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
                    catch { }
                    try { ps.gameObject.SetActive(false); }
                    catch { }
                }
            }
            Light[] lights = null;
            try { lights = root.GetComponentsInChildren<Light>(true); }
            catch { lights = null; }
            if (lights != null)
            {
                for (int i = 0; i < lights.Length; i++)
                {
                    Light lt = lights[i];
                    if (lt == null || KeepShockVisual(lt.transform))
                        continue;
                    try { lt.enabled = false; }
                    catch { }
                    try { lt.intensity = 0f; }
                    catch { }
                }
            }
            Renderer[] rends = null;
            try { rends = root.GetComponentsInChildren<Renderer>(true); }
            catch { rends = null; }
            if (rends != null)
            {
                for (int i = 0; i < rends.Length; i++)
                {
                    Renderer r = rends[i];
                    if (r == null || KeepShockVisual(r.transform))
                        continue;
                    try { r.enabled = false; }
                    catch { }
                }
            }
        }

        private static bool KeepShockVisual(Transform t)
        {
            if (UnderShockwave(t))
                return true;
            if (t == null)
                return false;
            string n = t.name;
            if (string.IsNullOrEmpty(n))
                return false;
            if (n.IndexOf("shock", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("vapor", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static bool UnderShockwave(Transform t)
        {
            if (t == null)
                return false;
            try
            {
                if (t.GetComponent<Shockwave>() != null)
                    return true;
                return t.GetComponentInParent<Shockwave>() != null;
            }
            catch
            {
                return false;
            }
        }

        private static void NoteShatter(Vector3 pos, float radiusM)
        {
            if (_shattering)
                return;
            if (Time.unscaledTime < _shatterUntil
                && (_shatterOrigin - pos).sqrMagnitude < 400f)
                return;
            float r = radiusM;
            if (r < 200f)
                r = ShockRadiusM();
            _shatterOrigin = pos;
            _shatterRadiusSq = r * r;
            _shatterUntil = Time.unscaledTime + 3f;
            ShatterRadius(pos, r);
        }

        private static void ShatterRadius(Vector3 pos, float radiusM)
        {
            if (_shattering)
                return;
            _shattering = true;
            try
            {
                float rSq = radiusM * radiusM;
                List<Unit> all = null;
                try { all = UnitRegistry.allUnits; }
                catch { all = null; }
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Unit u = all[i];
                        if (u == null || u.transform == null)
                            continue;
                        if ((u.transform.position - pos).sqrMagnitude > rSq)
                            continue;
                        ShatterUnit(u, pos);
                    }
                }
                Missile[] missiles = null;
                try { missiles = UnityEngine.Object.FindObjectsOfType<Missile>(); }
                catch { missiles = null; }
                if (missiles != null)
                {
                    for (int i = 0; i < missiles.Length; i++)
                    {
                        Missile m = missiles[i];
                        if (m == null || m.transform == null)
                            continue;
                        if ((m.transform.position - pos).sqrMagnitude > rSq)
                            continue;
                        ShatterUnit(m, pos);
                    }
                }
            }
            finally
            {
                _shattering = false;
            }
        }

        private static void ShatterUnit(Unit u, Vector3 origin)
        {
            if (u == null)
                return;
            Vector3 away = Vector3.up;
            try
            {
                away = u.transform.position - origin;
                if (away.sqrMagnitude < 0.25f)
                    away = Vector3.up;
                else
                    away.Normalize();
            }
            catch { }
            Vector3 vel = away * 120f;
            UnitPart[] parts = null;
            try { parts = u.GetComponentsInChildren<UnitPart>(true); }
            catch { parts = null; }
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    UnitPart p = parts[i];
                    if (p == null)
                        continue;
                    try { p.hitPoints = 0f; }
                    catch { }
                    try { p.ApplyDamage(0f, 1e9f, 0f, 2e6f); }
                    catch { }
                    try { p.SpawnFragments(); }
                    catch { }
                    try { p.Detach(vel, origin); }
                    catch { }
                }
            }
            Wreckage[] wrecks = null;
            try { wrecks = u.GetComponentsInChildren<Wreckage>(true); }
            catch { wrecks = null; }
            if (wrecks != null)
            {
                for (int i = 0; i < wrecks.Length; i++)
                {
                    if (wrecks[i] == null)
                        continue;
                    try { wrecks[i].Disintegrate(); }
                    catch { }
                }
            }
            Aircraft ac = u as Aircraft;
            if (ac != null && ac.pilots != null)
            {
                for (int i = 0; i < ac.pilots.Length; i++)
                {
                    Pilot pl = ac.pilots[i];
                    if (pl == null)
                        continue;
                    try { pl.ApplyDamage(0f, 1e9f, 0f, 1e9f); }
                    catch { }
                }
            }
            try { u.DisableUnit(); }
            catch { }
        }

        private static void MarkPurple(Shockwave sw)
        {
            if (sw == null)
                return;
            try { PurpleShockIds.Add(sw.GetInstanceID()); }
            catch { }
            ApplyShockTint(sw);
            if (PurpleShockIds.Count > 48)
                PurpleShockIds.Clear();
            if (TintedRendererIds.Count > 96)
                TintedRendererIds.Clear();
        }

        private static void ApplyShockTint(Shockwave sw)
        {
            if (sw == null)
                return;
            GameObject vapor = null;
            try
            {
                if (ShockVaporGoField != null)
                    vapor = ShockVaporGoField.GetValue(sw) as GameObject;
            }
            catch { vapor = null; }
            if (vapor != null)
            {
                Renderer vr = null;
                try { vr = vapor.GetComponent<Renderer>(); }
                catch { vr = null; }
                PaintShockRenderer(vr);
            }
            Material vaporMat = null;
            try
            {
                if (ShockVaporMatField != null)
                    vaporMat = ShockVaporMatField.GetValue(sw) as Material;
            }
            catch { vaporMat = null; }
            if (vaporMat != null)
            {
                if (vaporMat.name == null || vaporMat.name.IndexOf("Kmo2G", StringComparison.Ordinal) < 0)
                {
                    Material inst = null;
                    try { inst = UnityEngine.Object.Instantiate(vaporMat); }
                    catch { inst = null; }
                    if (inst != null)
                    {
                        inst.name = "Kmo2GShock";
                        vaporMat = inst;
                        try { ShockVaporMatField.SetValue(sw, inst); }
                        catch { }
                        if (vapor != null)
                        {
                            Renderer vr2 = null;
                            try { vr2 = vapor.GetComponent<Renderer>(); }
                            catch { vr2 = null; }
                            if (vr2 != null)
                            {
                                try { vr2.sharedMaterial = inst; }
                                catch { }
                            }
                        }
                    }
                }
                PaintShockMat(vaporMat);
            }
            Renderer[] rs = null;
            try { rs = sw.GetComponentsInChildren<Renderer>(true); }
            catch { rs = null; }
            if (rs == null)
                return;
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || r.gameObject == null)
                    continue;
                string nm = r.gameObject.name;
                if (string.IsNullOrEmpty(nm))
                    continue;
                if (nm.IndexOf("shock", StringComparison.OrdinalIgnoreCase) < 0
                    && nm.IndexOf("vapor", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                PaintShockRenderer(r);
            }
        }

        private static void PaintShockRenderer(Renderer r)
        {
            if (r == null)
                return;
            int id = 0;
            try { id = r.GetInstanceID(); }
            catch { return; }
            if (!TintedRendererIds.Contains(id))
            {
                Material[] mats = null;
                try { mats = r.materials; }
                catch { mats = null; }
                if (mats != null)
                {
                    for (int i = 0; i < mats.Length; i++)
                        PaintShockMat(mats[i]);
                    try { r.materials = mats; }
                    catch { }
                }
                TintedRendererIds.Add(id);
            }
            try
            {
                r.GetPropertyBlock(ShockMpb);
                ShockMpb.SetColor("_EmissionColor", ShockEmit);
                ShockMpb.SetColor("_Color", ShockAlbedo);
                ShockMpb.SetColor("_BaseColor", ShockAlbedo);
                r.SetPropertyBlock(ShockMpb);
            }
            catch { }
        }

        private static void PaintShockMat(Material mat)
        {
            if (mat == null)
                return;
            try { mat.SetColor("_EmissionColor", ShockEmit); }
            catch { }
            try { mat.SetColor("_Color", ShockAlbedo); }
            catch { }
            try { mat.SetColor("_BaseColor", ShockAlbedo); }
            catch { }
        }

        private static GameObject ResolveNukeFx()
        {
            if (_nukeFx != null)
                return _nukeFx;
            GameObject five = null;
            GameObject one = null;
            GameObject twenty = null;
            Shockwave[] waves = null;
            try { waves = Resources.FindObjectsOfTypeAll<Shockwave>(); }
            catch { waves = null; }
            if (waves != null)
            {
                for (int i = 0; i < waves.Length; i++)
                {
                    Shockwave sw = waves[i];
                    GameObject go = RootFx(sw);
                    if (go == null)
                        continue;
                    string n = go.name;
                    bool isFive = n == "explosion_5kt" || n == "explosion_5kt(Clone)";
                    bool isOne = n == "explosion_1kt" || n == "explosion_1kt(Clone)";
                    bool isTwenty = n == "explosion_20kt" || n == "explosion_20kt(Clone)";
                    if (!isFive && !isOne && !isTwenty)
                        continue;
                    bool prefab = true;
                    try { prefab = !go.scene.IsValid(); }
                    catch { prefab = true; }
                    if (isFive)
                    {
                        if (prefab)
                        {
                            _nukeFx = go;
                            return _nukeFx;
                        }
                        if (five == null)
                            five = go;
                    }
                    else if (isOne)
                    {
                        if (prefab && one == null)
                            one = go;
                        else if (one == null)
                            one = go;
                    }
                    else if (isTwenty)
                    {
                        if (prefab && twenty == null)
                            twenty = go;
                        else if (twenty == null)
                            twenty = go;
                    }
                }
            }
            if (five != null)
                _nukeFx = five;
            else if (one != null)
                _nukeFx = one;
            else
                _nukeFx = twenty;
            if (_nukeFx == null)
                _nukeFx = FxFromNuclearMissile();
            if (_nukeFx != null && Plugin.Log != null)
                Plugin.Log.LogInfo("KMO-2 shock FX: " + _nukeFx.name);
            return _nukeFx;
        }

        private static GameObject RootFx(Shockwave sw)
        {
            if (sw == null || sw.transform == null)
                return null;
            Transform t = sw.transform;
            while (t.parent != null)
                t = t.parent;
            return t.gameObject;
        }

        private static GameObject FxFromNuclearMissile()
        {
            if (WarheadField == null || WhAirField == null)
                return null;
            Missile[] missiles = null;
            try { missiles = Resources.FindObjectsOfTypeAll<Missile>(); }
            catch { missiles = null; }
            if (missiles == null)
                return null;
            FieldInfo blast = AccessTools.Field(typeof(Missile), "blastYield");
            for (int i = 0; i < missiles.Length; i++)
            {
                Missile m = missiles[i];
                if (m == null)
                    continue;
                float y = 0f;
                if (blast != null)
                {
                    try { y = (float)blast.GetValue(m); }
                    catch { continue; }
                }
                if (y < 1000000f)
                    continue;
                object wh = null;
                try { wh = WarheadField.GetValue(m); }
                catch { continue; }
                if (wh == null)
                    continue;
                GameObject fx = null;
                try { fx = WhAirField.GetValue(wh) as GameObject; }
                catch { fx = null; }
                if (fx != null)
                    return fx;
            }
            return null;
        }
    }

    public sealed class Kmo2ShockMark : MonoBehaviour
    {
    }

    [HarmonyPatch(typeof(Missile.Warhead), "Detonate")]
    internal static class Patch_Warhead_Detonate_Kmo2
    {
        private static bool Prefix(Missile.Warhead __instance, Rigidbody rb, PersistentID ownerID,
            Vector3 position, Vector3 normal, bool armed, float blastYield, bool hitArmor, bool hitTerrain)
        {
            return !GBurst.ConsumeOursDetonate(__instance, rb, ownerID, position, armed);
        }

        private static void Postfix(Rigidbody rb, PersistentID ownerID, Vector3 position)
        {
            GBurst.AfterVanillaDetonate(rb, ownerID, position);
        }
    }

    [HarmonyPatch(typeof(Shockwave), "Start")]
    internal static class Patch_Shockwave_Start_Kmo2
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Shockwave __instance)
        {
            if (!GBurst.IsOursShock(__instance))
                return;
            GBurst.PrepareShockwave(__instance);
        }

        private static void Postfix(Shockwave __instance)
        {
            GBurst.TintIfOurs(__instance);
        }
    }

    [HarmonyPatch(typeof(Shockwave), "Update")]
    internal static class Patch_Shockwave_Update_Kmo2
    {
        private static void Postfix(Shockwave __instance)
        {
            GBurst.TintIfOurs(__instance);
        }
    }
}
