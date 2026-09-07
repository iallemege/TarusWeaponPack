using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace KMO2
{
    /// <summary>
    /// Plays the 57mm railgun muzzle burst at the KMO-2 pylon on launch.
    /// MountedMissile has no Gun.muzzleParticles, so this clones that FX.
    /// </summary>
    internal static class LaunchFx
    {
        private static readonly FieldInfo MuzzleParticlesField =
            AccessTools.Field(typeof(Gun), "muzzleParticles");
        private static readonly FieldInfo FireSoundsField =
            AccessTools.Field(typeof(Gun), "fireSounds");
        private static readonly FieldInfo RecoilSoundField =
            AccessTools.Field(typeof(Gun), "recoilSound");

        private static GameObject _stash;
        private static GameObject[] _templates;
        private static AudioClip[] _clips;
        private static bool _loggedMissing;
        private static float _nextTry;
        private static Material _add;
        private static bool _matTried;

        internal static void Warm()
        {
            if (_templates != null && _templates.Length > 0)
                return;
            if (Time.unscaledTime < _nextTry)
                return;
            _nextTry = Time.unscaledTime + 1.5f;
            EnsureTemplates();
        }

        internal static void Play(Weapon weapon)
        {
            if (weapon == null)
                return;
            EnsureTemplates();
            Vector3 pos = weapon.transform.position;
            Vector3 fwd = FireForward(weapon);
            if (fwd.sqrMagnitude < 0.01f)
                fwd = Vector3.forward;
            fwd.Normalize();
            Quaternion rot = Quaternion.LookRotation(fwd);

            bool played = PlayCloned(pos, rot);
            PlaySounds(pos);
            if (!played)
                FallbackFlash(pos, fwd);
        }

        private static Vector3 FireForward(Weapon weapon)
        {
            Unit u = weapon.attachedUnit;
            if (u != null && u.transform != null)
                return u.transform.forward;
            return weapon.transform.forward;
        }

        private static bool PlayCloned(Vector3 pos, Quaternion rot)
        {
            if (_templates == null || _templates.Length == 0)
                return false;
            bool any = false;
            for (int i = 0; i < _templates.Length; i++)
            {
                GameObject src = _templates[i];
                if (src == null)
                    continue;
                GameObject go = UnityEngine.Object.Instantiate(src);
                go.name = "KMO2_MuzzleFx";
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
                return;
            CaptureFromGun(gun);
            if ((_templates == null || _templates.Length == 0) && !_loggedMissing)
            {
                _loggedMissing = true;
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("KMO2: no 57mm muzzle particles; using fallback flash");
            }
            else if (_templates != null && _templates.Length > 0 && Plugin.Log != null)
            {
                Plugin.Log.LogInfo("KMO2: cached " + _templates.Length
                    + " railgun muzzle FX template(s)");
            }
        }

        private static Gun FindDonorGun()
        {
            Gun best = null;
            int bestScore = 0;

#if TARUS_PACK
            try
            {
                if (RailcannonPod.Pod.Mount != null && RailcannonPod.Pod.Mount.prefab != null)
                {
                    Gun fromPack = RailcannonPod.Pod.Mount.prefab.GetComponentInChildren<Gun>(true);
                    int packScore = ScoreGun(fromPack);
                    if (packScore > bestScore)
                    {
                        best = fromPack;
                        bestScore = packScore;
                    }
                }
            }
            catch
            {
            }
#endif

            Encyclopedia enc = Plugin.GetEncyclopedia();
            if (enc != null && enc.weaponMounts != null)
            {
                for (int i = 0; i < enc.weaponMounts.Count; i++)
                {
                    WeaponMount m = enc.weaponMounts[i];
                    if (m == null || m.prefab == null)
                        continue;
                    string key = m.jsonKey;
                    if (string.IsNullOrEmpty(key))
                        continue;
                    bool rail = string.Equals(key, "gun_155mm_pod_P", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "gun_57mm_pod", StringComparison.OrdinalIgnoreCase);
                    if (!rail)
                        continue;
                    Gun g = m.prefab.GetComponentInChildren<Gun>(true);
                    int s = ScoreGun(g);
                    if (string.Equals(key, "gun_155mm_pod_P", StringComparison.OrdinalIgnoreCase))
                        s += 8;
                    else
                        s += 4;
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
                    int s = ScoreGun(all[i]);
                    if (s > bestScore)
                    {
                        best = all[i];
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
            int n = CountMuzzle(gun);
            if (n <= 0)
                return 0;
            int score = n;
            if (gun.info != null && gun.info.weaponName != null)
            {
                string wn = gun.info.weaponName;
                if (wn.IndexOf("57mm Railgun", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 20;
                else if (wn.IndexOf("Railgun", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 10;
            }
            return score;
        }

        private static int CountMuzzle(Gun gun)
        {
            ParticleSystem[] arr = ReadMuzzleArray(gun);
            if (arr != null && arr.Length > 0)
            {
                int n = 0;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] != null)
                        n++;
                }
                if (n > 0)
                    return n;
            }
            return NamedMuzzleSystems(gun).Count;
        }

        private static ParticleSystem[] ReadMuzzleArray(Gun gun)
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

        private static List<ParticleSystem> NamedMuzzleSystems(Gun gun)
        {
            List<ParticleSystem> list = new List<ParticleSystem>();
            ParticleSystem[] all = gun.GetComponentsInChildren<ParticleSystem>(true);
            if (all == null)
                return list;
            for (int i = 0; i < all.Length; i++)
            {
                ParticleSystem ps = all[i];
                if (ps == null || ps.transform == null)
                    continue;
                string n = ps.transform.name;
                if (string.IsNullOrEmpty(n))
                    continue;
                if (n.IndexOf("Muzzle", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Shot", StringComparison.OrdinalIgnoreCase) >= 0)
                    list.Add(ps);
            }
            return list;
        }

        private static void CaptureFromGun(Gun gun)
        {
            List<ParticleSystem> src = new List<ParticleSystem>();
            ParticleSystem[] field = ReadMuzzleArray(gun);
            if (field != null)
            {
                for (int i = 0; i < field.Length; i++)
                {
                    if (field[i] != null)
                        src.Add(field[i]);
                }
            }
            if (src.Count == 0)
                src.AddRange(NamedMuzzleSystems(gun));
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
            List<AudioClip> clips = new List<AudioClip>();
            if (FireSoundsField != null)
            {
                try
                {
                    AudioClip[] arr = FireSoundsField.GetValue(gun) as AudioClip[];
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            if (arr[i] != null)
                                clips.Add(arr[i]);
                        }
                    }
                }
                catch
                {
                }
            }
            if (clips.Count == 0 && RecoilSoundField != null)
            {
                try
                {
                    AudioSource recoil = RecoilSoundField.GetValue(gun) as AudioSource;
                    if (recoil != null && recoil.clip != null)
                        clips.Add(recoil.clip);
                }
                catch
                {
                }
            }
            if (clips.Count > 0)
                _clips = clips.ToArray();
        }

        private static void EnsureStash()
        {
            if (_stash != null)
                return;
            _stash = new GameObject("KMO2_MuzzleFxTemplates");
            UnityEngine.Object.DontDestroyOnLoad(_stash);
            _stash.SetActive(false);
        }

        private static void FallbackFlash(Vector3 pos, Vector3 fwd)
        {
            Material add = AddMat();
            Color hot = new Color(0.75f, 0.92f, 1f, 1f);
            Color core = new Color(1f, 1f, 1f, 1f);
            SpawnQuad(pos, 0.55f, core, 0.05f, add);
            SpawnQuad(pos, 1.15f, hot, 0.08f, add);
            Flame(pos, pos + fwd * 1.8f, 0.09f, 0.008f, core, 0.05f, add);
            Flame(pos, pos + fwd * 1.1f, 0.16f, 0.02f, hot, 0.045f, add);
            Vector3 right = Vector3.Cross(Mathf.Abs(fwd.y) < 0.92f ? Vector3.up : Vector3.right, fwd);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(fwd, right);
            for (int i = 0; i < 6; i++)
            {
                float ang = i * 60f * Mathf.Deg2Rad;
                Vector3 radial = right * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                Flame(pos, pos + fwd * 0.45f + radial * 0.28f, 0.04f, 0.004f, hot, 0.04f, add);
            }
            try
            {
                GameObject lightGo = new GameObject("KMO2_MuzzleLight");
                lightGo.transform.position = pos;
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 22f;
                light.intensity = 6f;
                light.color = hot;
                UnityEngine.Object.Destroy(lightGo, 0.1f);
            }
            catch
            {
            }
        }

        private static void SpawnQuad(Vector3 pos, float size, Color color, float life, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "KMO2_Flash";
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
            GameObject go = new GameObject("KMO2_Flame");
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
