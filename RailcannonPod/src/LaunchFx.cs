using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RailcannonPod
{
    /// <summary>
    /// Restores the 57/155mm railgun electromagnetic muzzle burst after Halo
    /// visual swap hid or buried the donor FX. Plays cloned particles at the
    /// Halo barrel tip plus a short additive lightning flash.
    /// </summary>
    internal static class LaunchFx
    {
        private static readonly FieldInfo MuzzleParticlesField =
            AccessTools.Field(typeof(Gun), "muzzleParticles");
        private static readonly FieldInfo MuzzlesField =
            AccessTools.Field(typeof(Gun), "muzzles");
        private static readonly FieldInfo FireSoundsField =
            AccessTools.Field(typeof(Gun), "fireSounds");

        private static GameObject _stash;
        private static GameObject[] _templates;
        private static AudioClip[] _clips;
        private static bool _loggedMissing;
        private static float _nextTry;
        private static Material _add;
        private static bool _matTried;
        private static readonly HashSet<int> Wired = new HashSet<int>();

        internal static void Warm()
        {
            if (_templates != null && _templates.Length > 0)
                return;
            if (Time.unscaledTime < _nextTry)
                return;
            _nextTry = Time.unscaledTime + 1.5f;
            EnsureTemplates();
        }

        internal static void Prepare(Gun gun)
        {
            if (gun == null)
                return;
            GameObject root = FxRoot(gun);
            HaloVisual.RestoreFxRenderers(root);
            Transform anchor = HaloVisual.EnsureMuzzleAnchor(root);
            if (anchor == null)
                anchor = gun.transform;
            if (MuzzlesField != null)
            {
                try { MuzzlesField.SetValue(gun, new Transform[] { anchor }); }
                catch { }
            }

            ParticleSystem[] live = ReadParticles(gun);
            live = Trim(live);
            live = MoveTo(live, anchor);
            if (live == null || live.Length == 0)
                live = AttachClones(anchor);
            if (live == null)
                live = new ParticleSystem[0];
            WriteParticles(gun, live);
        }

        internal static void Burst(Gun gun)
        {
            if (gun == null)
                return;
            GameObject root = FxRoot(gun);
            Transform anchor = HaloVisual.EnsureMuzzleAnchor(root);
            Vector3 pos = anchor != null ? anchor.position : gun.transform.position;
            Vector3 fwd = FireForward(gun, anchor);
            ParticleSystem[] live = ReadParticles(gun);
            bool had = false;
            if (live != null)
            {
                for (int i = 0; i < live.Length; i++)
                {
                    if (live[i] != null)
                    {
                        had = true;
                        break;
                    }
                }
            }
            if (!had)
                PlayClonedAt(pos, Quaternion.LookRotation(fwd));
            FallbackFlash(pos, fwd);
        }

        private static Vector3 FireForward(Gun gun, Transform anchor)
        {
            if (anchor != null && anchor.forward.sqrMagnitude > 0.0001f)
                return anchor.forward.normalized;
            Unit u = gun.attachedUnit;
            if (u != null && u.transform != null)
                return u.transform.forward;
            return gun.transform.forward;
        }

        private static GameObject FxRoot(Gun gun)
        {
            Transform t = gun.transform;
            while (t != null)
            {
                if (t.Find(HaloVisual.VisualChildName) != null
                    || t.Find(HaloVisual.MarkerName) != null
                    || t.Find(HaloVisual.MuzzleName) != null)
                    return t.gameObject;
                t = t.parent;
            }
            return gun.gameObject;
        }

        private static ParticleSystem[] ReadParticles(Gun gun)
        {
            if (MuzzleParticlesField == null || gun == null)
                return null;
            try
            {
                return MuzzleParticlesField.GetValue(gun) as ParticleSystem[];
            }
            catch
            {
                return null;
            }
        }

        private static void WriteParticles(Gun gun, ParticleSystem[] parts)
        {
            if (MuzzleParticlesField == null || gun == null)
                return;
            try { MuzzleParticlesField.SetValue(gun, parts); }
            catch { }
        }

        private static ParticleSystem[] Trim(ParticleSystem[] src)
        {
            if (src == null || src.Length == 0)
                return src;
            int n = 0;
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] != null)
                    n++;
            }
            if (n == src.Length)
                return src;
            ParticleSystem[] dst = new ParticleSystem[n];
            int w = 0;
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == null)
                    continue;
                dst[w] = src[i];
                w++;
            }
            return dst;
        }

        private static ParticleSystem[] MoveTo(ParticleSystem[] src, Transform anchor)
        {
            if (src == null || src.Length == 0 || anchor == null)
                return src;
            for (int i = 0; i < src.Length; i++)
            {
                ParticleSystem ps = src[i];
                if (ps == null || ps.transform == null)
                    continue;
                int id = ps.GetInstanceID();
                if (Wired.Contains(id))
                    continue;
                Wired.Add(id);
                try
                {
                    ps.transform.SetParent(anchor, true);
                    ps.transform.localPosition = Vector3.zero;
                    ps.transform.localRotation = Quaternion.identity;
                    if (!ps.gameObject.activeSelf)
                        ps.gameObject.SetActive(true);
                    ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
                    if (rend != null)
                        rend.enabled = true;
                }
                catch
                {
                }
            }
            return src;
        }

        private static ParticleSystem[] AttachClones(Transform anchor)
        {
            EnsureTemplates();
            if (_templates == null || _templates.Length == 0 || anchor == null)
                return null;
            List<ParticleSystem> list = new List<ParticleSystem>();
            for (int i = 0; i < _templates.Length; i++)
            {
                GameObject src = _templates[i];
                if (src == null)
                    continue;
                GameObject go = UnityEngine.Object.Instantiate(src, anchor, false);
                go.name = src.name;
                go.SetActive(true);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                ParticleSystem[] pss = go.GetComponentsInChildren<ParticleSystem>(true);
                for (int j = 0; j < pss.Length; j++)
                {
                    if (pss[j] == null)
                        continue;
                    ParticleSystem.MainModule main = pss[j].main;
                    main.playOnAwake = false;
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                        main.simulationSpace = ParticleSystemSimulationSpace.World;
                    pss[j].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    list.Add(pss[j]);
                    Wired.Add(pss[j].GetInstanceID());
                }
            }
            if (list.Count == 0)
                return null;
            return list.ToArray();
        }

        private static bool PlayClonedAt(Vector3 pos, Quaternion rot)
        {
            EnsureTemplates();
            if (_templates == null || _templates.Length == 0)
                return false;
            bool any = false;
            for (int i = 0; i < _templates.Length; i++)
            {
                GameObject src = _templates[i];
                if (src == null)
                    continue;
                GameObject go = UnityEngine.Object.Instantiate(src);
                go.name = "Railcannon_MuzzleFx";
                go.SetActive(true);
                go.transform.SetParent(null, true);
                go.transform.position = pos;
                go.transform.rotation = rot;
                ParticleSystem[] pss = go.GetComponentsInChildren<ParticleSystem>(true);
                for (int j = 0; j < pss.Length; j++)
                {
                    ParticleSystem ps = pss[j];
                    if (ps == null)
                        continue;
                    ParticleSystem.MainModule main = ps.main;
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                        main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.playOnAwake = false;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                    any = true;
                }
                UnityEngine.Object.Destroy(go, 4f);
            }
            return any;
        }

        private static void PlaySounds(Vector3 pos)
        {
            if (_clips == null || _clips.Length == 0)
                return;
            AudioClip clip = _clips[UnityEngine.Random.Range(0, _clips.Length)];
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, pos, 1f);
        }

        private static void EnsureTemplates()
        {
            if (_templates != null && _templates.Length > 0)
                return;
            Gun gun = FindDonorGun();
            if (gun == null)
            {
                if (!_loggedMissing && Plugin.Log != null)
                {
                    _loggedMissing = true;
                    Plugin.Log.LogWarning("57mm: no donor muzzle particles; using lightning flash");
                }
                return;
            }
            CaptureFromGun(gun);
            if (_templates != null && _templates.Length > 0 && Plugin.Log != null)
                Plugin.Log.LogInfo("57mm: cached " + _templates.Length + " muzzle FX template(s)");
        }

        private static Gun FindDonorGun()
        {
            Gun best = null;
            int bestScore = 0;
            Encyclopedia enc = Plugin.GetEncyclopedia();
            if (enc != null && enc.weaponMounts != null)
            {
                for (int i = 0; i < enc.weaponMounts.Count; i++)
                {
                    WeaponMount m = enc.weaponMounts[i];
                    if (m == null || m.prefab == null)
                        continue;
                    string key = m.jsonKey != null ? m.jsonKey : string.Empty;
                    bool vanilla57 = string.Equals(key, "gun_57mm_pod", StringComparison.OrdinalIgnoreCase);
                    if (!vanilla57 && Pod.IsOurs(m))
                        continue;
                    bool rail = vanilla57
                        || string.Equals(key, "gun_155mm_pod", StringComparison.OrdinalIgnoreCase)
                        || key.IndexOf("155mm", StringComparison.OrdinalIgnoreCase) >= 0
                        || key.IndexOf("57mm", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!rail)
                        continue;
                    Gun g = m.prefab.GetComponentInChildren<Gun>(true);
                    int s = ScoreGun(g);
                    if (string.Equals(key, "gun_57mm_pod", StringComparison.OrdinalIgnoreCase))
                        s += 12;
                    if (s > bestScore)
                    {
                        best = g;
                        bestScore = s;
                    }
                }
            }
            Gun[] all = Resources.FindObjectsOfTypeAll<Gun>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    Gun g = all[i];
                    if (g == null || Pod.IsOurs(g))
                        continue;
                    int s = ScoreGun(g);
                    if (s > bestScore)
                    {
                        best = g;
                        bestScore = s;
                    }
                }
            }
            return best;
        }

        private static int ScoreGun(Gun gun)
        {
            if (gun == null)
                return 0;
            ParticleSystem[] arr = ReadParticles(gun);
            int n = 0;
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] != null)
                        n++;
                }
            }
            if (n <= 0)
            {
                ParticleSystem[] kids = gun.GetComponentsInChildren<ParticleSystem>(true);
                n = kids != null ? kids.Length : 0;
            }
            if (n <= 0)
                return 0;
            int score = n;
            if (gun.info != null && gun.info.weaponName != null)
            {
                string wn = gun.info.weaponName;
                if (wn.IndexOf("57mm", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 20;
                else if (wn.IndexOf("155mm", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 8;
                else if (wn.IndexOf("Railgun", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 6;
            }
            return score;
        }

        private static void CaptureFromGun(Gun gun)
        {
            List<ParticleSystem> src = new List<ParticleSystem>();
            ParticleSystem[] field = ReadParticles(gun);
            if (field != null)
            {
                for (int i = 0; i < field.Length; i++)
                {
                    if (field[i] != null)
                        src.Add(field[i]);
                }
            }
            if (src.Count == 0)
            {
                ParticleSystem[] kids = gun.GetComponentsInChildren<ParticleSystem>(true);
                if (kids != null)
                {
                    for (int i = 0; i < kids.Length; i++)
                    {
                        if (kids[i] != null)
                            src.Add(kids[i]);
                    }
                }
            }
            if (src.Count == 0)
                return;
            EnsureStash();
            List<GameObject> roots = new List<GameObject>();
            for (int i = 0; i < src.Count; i++)
            {
                ParticleSystem ps = src[i];
                if (ps == null)
                    continue;
                if (IsChildOfOther(ps, src))
                    continue;
                GameObject clone = UnityEngine.Object.Instantiate(ps.gameObject, _stash.transform, false);
                clone.name = ps.gameObject.name;
                clone.SetActive(false);
                ParticleSystem[] cloned = clone.GetComponentsInChildren<ParticleSystem>(true);
                for (int j = 0; j < cloned.Length; j++)
                {
                    if (cloned[j] == null)
                        continue;
                    ParticleSystem.MainModule main = cloned[j].main;
                    main.playOnAwake = false;
                    cloned[j].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                roots.Add(clone);
            }
            if (roots.Count > 0)
                _templates = roots.ToArray();
            CaptureSounds(gun);
        }

        private static bool IsChildOfOther(ParticleSystem ps, List<ParticleSystem> all)
        {
            Transform t = ps.transform;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || all[i] == ps)
                    continue;
                if (t.IsChildOf(all[i].transform))
                    return true;
            }
            return false;
        }

        private static void CaptureSounds(Gun gun)
        {
            if (_clips != null && _clips.Length > 0)
                return;
            if (FireSoundsField == null)
                return;
            try
            {
                AudioClip[] arr = FireSoundsField.GetValue(gun) as AudioClip[];
                if (arr == null || arr.Length == 0)
                    return;
                List<AudioClip> clips = new List<AudioClip>();
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] != null)
                        clips.Add(arr[i]);
                }
                if (clips.Count > 0)
                    _clips = clips.ToArray();
            }
            catch
            {
            }
        }

        private static void EnsureStash()
        {
            if (_stash != null)
                return;
            _stash = new GameObject("Railcannon_MuzzleFxTemplates");
            UnityEngine.Object.DontDestroyOnLoad(_stash);
            _stash.SetActive(false);
        }

        private static void FallbackFlash(Vector3 pos, Vector3 fwd)
        {
            Material add = AddMat();
            float mul = Pod.ChargeMode ? 1.6f : 1f;
            Color core = new Color(0.85f, 0.95f, 1f, 1f);
            Color hot = new Color(0.35f, 0.75f, 1f, 1f);
            Color arc = new Color(0.55f, 0.85f, 1f, 1f);
            SpawnQuad(pos, 0.45f * mul, core, 0.05f, add);
            SpawnQuad(pos, 1.05f * mul, hot, 0.08f, add);
            Flame(pos, pos + fwd * (2.2f * mul), 0.11f * mul, 0.006f, core, 0.06f, add);
            Flame(pos, pos + fwd * (1.35f * mul), 0.2f * mul, 0.02f, hot, 0.05f, add);
            Vector3 right = Vector3.Cross(Mathf.Abs(fwd.y) < 0.92f ? Vector3.up : Vector3.right, fwd);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(fwd, right);
            int n = Pod.ChargeMode ? 10 : 8;
            for (int i = 0; i < n; i++)
            {
                float ang = i * (360f / n) * Mathf.Deg2Rad;
                Vector3 radial = right * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                float len = (0.35f + (i % 3) * 0.12f) * mul;
                Flame(pos, pos + fwd * (0.25f * mul) + radial * len, 0.045f * mul, 0.004f, arc, 0.045f, add);
            }
            try
            {
                GameObject lightGo = new GameObject("Railcannon_MuzzleLight");
                lightGo.transform.position = pos;
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 28f * mul;
                light.intensity = 8f * mul;
                light.color = hot;
                UnityEngine.Object.Destroy(lightGo, 0.12f);
            }
            catch
            {
            }
        }

        private static void SpawnQuad(Vector3 pos, float size, Color color, float life, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Railcannon_Flash";
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                UnityEngine.Object.Destroy(col);
            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (mat != null)
                    r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.material.color = color;
            }
            UnityEngine.Object.Destroy(go, life);
        }

        private static void Flame(Vector3 a, Vector3 b, float startW, float endW, Color c, float life, Material mat)
        {
            GameObject go = new GameObject("Railcannon_Arc");
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = startW;
            lr.endWidth = endW;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (mat != null)
                lr.material = mat;
            lr.startColor = c;
            lr.endColor = new Color(c.r, c.g, c.b, 0f);
            UnityEngine.Object.Destroy(go, life);
        }

        private static Material AddMat()
        {
            if (_matTried)
                return _add;
            _matTried = true;
            Shader sh = Shader.Find("Particles/Additive");
            if (sh == null)
                sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null)
                sh = Shader.Find("Mobile/Particles/Additive");
            if (sh == null)
                sh = Shader.Find("Sprites/Default");
            if (sh == null)
                sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null)
                return null;
            _add = new Material(sh);
            _add.hideFlags = HideFlags.HideAndDontSave;
            _add.color = Color.white;
            try
            {
                _add.SetInt("_SrcBlend", 5);
                _add.SetInt("_DstBlend", 1);
                _add.SetInt("_ZWrite", 0);
                _add.renderQueue = 3100;
            }
            catch
            {
            }
            return _add;
        }
    }
}
