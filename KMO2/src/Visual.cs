using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace KMO2
{
    internal static class Visual
    {
        private const string VisualChildName = "KMO2_Visual";
        private const string MarkerName = "KMO2_Applied";
        // Air-launched UAV. Fuselage length in world metres (6.5 was too big, 3.2 too small).
        private const float TargetLengthMeters = 4.0f;
        // OBJ: fuselage along X (nose -X), span along Z, up Y.
        // Yaw +90 maps tail +X off +Z; user saw -90 as reversed, so nose +X → +Z.
        private static readonly Quaternion MuzzleFix = Quaternion.Euler(0f, 90f, 0f);

        private static Mesh _mesh;
        private static Material _material;
        private static Texture2D _albedo;
        private static bool _loadAttempted;
        private static string _lastError;

        private static Mesh _donorPylonMesh;
        private static Material[] _donorPylonMats;
        private static bool _donorPylonTried;

        internal static void ApplyToMissile(Missile missile)
        {
            if (missile == null)
                return;
            ApplyToRoot(missile.gameObject);
        }

        internal static void ApplyToHangarRack(GameObject rackRoot)
        {
            if (rackRoot == null)
                return;
            EnsureRackPylon(rackRoot);
            Weapon[] weapons = rackRoot.GetComponentsInChildren<Weapon>(true);
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon w = weapons[i];
                if (w == null || w is Gun)
                    continue;
                ApplyToRoot(w.gameObject);
            }
        }

        internal static void ApplyToRoot(GameObject root)
        {
            if (root == null)
                return;
            if (root.transform.Find(MarkerName) != null)
            {
                Transform vis = root.transform.Find(VisualChildName);
                if (vis != null)
                    FitToMissileSize(vis.gameObject, root.GetComponent<Missile>() != null);
                return;
            }
            if (!EnsureLoaded())
            {
                if (Plugin.Log != null && !string.IsNullOrEmpty(_lastError))
                    Plugin.Log.LogWarning("KMO2 visual: " + _lastError);
                return;
            }

            try
            {
                Shader donorShader = null;
                MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < mrs.Length; i++)
                {
                    MeshRenderer mr = mrs[i];
                    if (mr == null || mr.transform == null || IsKeepVisibleRenderer(mr.transform.name))
                        continue;
                    if (donorShader == null && mr.sharedMaterial != null
                        && mr.sharedMaterial.renderQueue < (int)RenderQueue.Transparent)
                        donorShader = mr.sharedMaterial.shader;
                }

                Material useMat = BuildOpaqueMaterial(donorShader);

                GameObject marker = new GameObject(MarkerName);
                marker.transform.SetParent(root.transform, false);

                GameObject vis = new GameObject(VisualChildName);
                vis.transform.SetParent(root.transform, false);
                vis.transform.localPosition = Vector3.zero;
                vis.transform.localRotation = MuzzleFix;
                vis.transform.localScale = Vector3.one;

                MeshFilter filter = vis.AddComponent<MeshFilter>();
                filter.sharedMesh = _mesh;
                MeshRenderer renderer = vis.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = useMat != null ? useMat : _material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                bool inFlight = root.GetComponent<Missile>() != null;
                FitToMissileSize(vis, inFlight);

                for (int i = 0; i < mrs.Length; i++)
                {
                    MeshRenderer mr = mrs[i];
                    if (mr == null || mr.transform == null || IsKeepVisibleRenderer(mr.transform.name))
                        continue;
                    mr.enabled = false;
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("KMO2 visual apply failed: " + ex.Message);
            }
        }

        private static void FitToMissileSize(GameObject visual, bool inFlight)
        {
            if (visual == null || _mesh == null)
                return;
            Vector3 ms = _mesh.bounds.size;
            float src = ms.x;
            if (src < 0.01f)
                src = Mathf.Max(ms.y, ms.z);
            if (src < 0.01f)
                return;
            float parentS = 1f;
            if (visual.transform.parent != null)
            {
                Vector3 ls = visual.transform.parent.lossyScale;
                parentS = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                if (parentS < 0.0001f)
                    parentS = 1f;
            }
            float s = TargetLengthMeters / (src * parentS);
            if (s > 80f)
                s = 80f;
            if (s < 0.00005f)
                s = 0.00005f;
            visual.transform.localScale = new Vector3(s, s, s);
            if (visual.transform.parent != null)
            {
                Vector3 ls = visual.transform.parent.lossyScale;
                int neg = 0;
                if (ls.x < 0f)
                    neg++;
                if (ls.y < 0f)
                    neg++;
                if (ls.z < 0f)
                    neg++;
                if ((neg % 2) == 1)
                    visual.transform.localScale = new Vector3(s, s, -s);
            }
            if (inFlight)
            {
                visual.transform.localPosition = Vector3.zero;
                return;
            }
            visual.transform.localPosition = new Vector3(0f, -_mesh.bounds.extents.y * s * 0.35f, -0.85f);
        }

        private static bool IsKeepVisibleRenderer(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.IndexOf(VisualChildName, StringComparison.Ordinal) >= 0)
                return true;
            if (name.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Exhaust", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("pylon", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("plug", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("rail", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static void EnsureRackPylon(GameObject rackRoot)
        {
            if (rackRoot == null)
                return;
            Transform existingPylon = FindNamedChild(rackRoot.transform, "pylon");
            if (existingPylon != null)
            {
                NudgeWeaponsAft(existingPylon);
                return;
            }
            string rn = rackRoot.name != null ? rackRoot.name : string.Empty;
            if (rn.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            Mesh mesh;
            Material[] mats;
            if (!TryGetDonorPylon(out mesh, out mats) || mesh == null)
                return;

            GameObject pylon = new GameObject("pylon");
            pylon.transform.SetParent(rackRoot.transform, false);
            pylon.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            pylon.transform.localRotation = Quaternion.identity;
            pylon.transform.localScale = new Vector3(0.9f, 1f, 1f);
            MeshFilter mf = pylon.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = pylon.AddComponent<MeshRenderer>();
            if (mats != null && mats.Length > 0)
                mr.sharedMaterials = mats;

            Weapon[] weapons = rackRoot.GetComponentsInChildren<Weapon>(true);
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon w = weapons[i];
                if (w == null || w is Gun || w.transform == null)
                    continue;
                if (object.ReferenceEquals(w.gameObject, rackRoot))
                    continue;
                if (w.transform.parent != rackRoot.transform)
                    continue;
                w.transform.SetParent(pylon.transform, false);
                w.transform.localPosition = new Vector3(0f, -0.12f, -0.40f);
                w.transform.localRotation = Quaternion.identity;
            }
        }

        private static void NudgeWeaponsAft(Transform pylon)
        {
            if (pylon == null)
                return;
            Weapon[] weapons = pylon.GetComponentsInChildren<Weapon>(true);
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon w = weapons[i];
                if (w == null || w is Gun || w.transform == null)
                    continue;
                w.transform.localPosition = new Vector3(0f, -0.12f, -0.40f);
            }
        }

        private static bool TryGetDonorPylon(out Mesh mesh, out Material[] mats)
        {
            mesh = _donorPylonMesh;
            mats = _donorPylonMats;
            if (mesh != null)
                return true;
            if (_donorPylonTried)
                return false;
            _donorPylonTried = true;
            try
            {
                WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
                if (all == null)
                    return false;
                for (int i = 0; i < all.Length; i++)
                {
                    WeaponMount m = all[i];
                    if (m == null || m.prefab == null || string.IsNullOrEmpty(m.jsonKey))
                        continue;
                    if (!string.Equals(m.jsonKey, "AGM_heavy_single", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(m.jsonKey, "AAM4_single", StringComparison.OrdinalIgnoreCase))
                        continue;
                    Transform pylonXf = FindNamedChild(m.prefab.transform, "pylon");
                    if (pylonXf == null)
                        continue;
                    MeshFilter mf = pylonXf.GetComponent<MeshFilter>();
                    MeshRenderer rend = pylonXf.GetComponent<MeshRenderer>();
                    if (mf == null || mf.sharedMesh == null)
                        continue;
                    _donorPylonMesh = mf.sharedMesh;
                    _donorPylonMats = rend != null ? rend.sharedMaterials : null;
                    mesh = _donorPylonMesh;
                    mats = _donorPylonMats;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static Transform FindNamedChild(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindNamedChild(root.GetChild(i), name);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        private static bool EnsureLoaded()
        {
            if (_mesh != null && _material != null)
                return true;
            if (_loadAttempted)
                return _mesh != null && _material != null;
            _loadAttempted = true;

            string objPath = ResolveAssetPath("KMO2.obj");
            if (string.IsNullOrEmpty(objPath))
            {
                _lastError = "KMO2.obj not found (expected BepInEx/plugins/KMO2Assets/)";
                return false;
            }

            try
            {
                _mesh = ObjMeshLoader.Load(objPath);
                if (_mesh == null)
                {
                    _lastError = "OBJ parse failed: " + objPath;
                    return false;
                }
                _mesh.name = "KMO2";
                CenterMesh(_mesh);
                _mesh.RecalculateNormals();
                _mesh.RecalculateBounds();
                try { _mesh.RecalculateTangents(); }
                catch { }

                string texPath = ResolveAssetPath("KMO2.png");
                if (!string.IsNullOrEmpty(texPath) && File.Exists(texPath))
                    _albedo = LoadTexture(texPath);

                _material = BuildOpaqueMaterial(null);
                if (_material == null)
                {
                    _lastError = "No usable opaque shader found";
                    return false;
                }

                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("KMO2 visual loaded verts=" + _mesh.vertexCount
                        + " from " + objPath);
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        private static void CenterMesh(Mesh mesh)
        {
            if (mesh == null)
                return;
            Vector3[] verts = mesh.vertices;
            if (verts == null || verts.Length == 0)
                return;
            Vector3 c = mesh.bounds.center;
            for (int i = 0; i < verts.Length; i++)
                verts[i] -= c;
            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }

        private static void ReverseWindingAndNormals(Mesh mesh)
        {
            if (mesh == null)
                return;
            int[] tris = mesh.triangles;
            if (tris == null || tris.Length < 3)
                return;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int tmp = tris[i + 1];
                tris[i + 1] = tris[i + 2];
                tris[i + 2] = tmp;
            }
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            try { mesh.RecalculateTangents(); }
            catch { }
        }

        private static Material BuildOpaqueMaterial(Shader preferredShader)
        {
            Material mat = null;
            try
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = preferredShader;
                if (shader == null)
                    shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Diffuse");
                if (shader == null)
                    return _material;
                mat = new Material(shader);
            }
            catch
            {
                return _material;
            }

            mat.name = "KMO2_Mat";
            Color tint = _albedo != null
                ? Color.white
                : new Color(0.45f, 0.48f, 0.42f, 1f);
            try
            {
                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 0f);
                if (mat.HasProperty("_ZWrite"))
                    mat.SetFloat("_ZWrite", 1f);
                if (mat.HasProperty("_Metallic"))
                    mat.SetFloat("_Metallic", 0f);
                if (mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", 0.12f);
                if (mat.HasProperty("_Glossiness"))
                    mat.SetFloat("_Glossiness", 0.12f);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = (int)RenderQueue.Geometry;
                if (mat.HasProperty("_Cull"))
                    mat.SetFloat("_Cull", 2f);
                if (mat.HasProperty("_CullMode"))
                    mat.SetFloat("_CullMode", 2f);
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", tint);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", tint);
                mat.color = tint;
            }
            catch { }

            if (_albedo != null)
            {
                mat.mainTexture = _albedo;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", _albedo);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", _albedo);
            }
            return mat;
        }

        private static Texture2D LoadTexture(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        internal static string ResolveAssetPath(string fileName)
        {
            try
            {
                string p = Path.Combine(Paths.PluginPath, "TarusWeaponPackAssets", fileName);
                if (File.Exists(p))
                    return p;
            }
            catch { }
            try
            {
                string p = Path.Combine(Paths.PluginPath, "KMO2Assets", fileName);
                if (File.Exists(p))
                    return p;
            }
            catch { }
            try
            {
                string p = Path.Combine(Paths.PluginPath, "WeXonAssets", fileName);
                if (File.Exists(p))
                    return p;
            }
            catch { }
            try
            {
                string asm = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asm))
                {
                    string dir = Path.GetDirectoryName(asm);
                    string p1 = Path.Combine(dir, "KMO2Assets", fileName);
                    if (File.Exists(p1))
                        return p1;
                    string p2 = Path.Combine(dir, "assets", fileName);
                    if (File.Exists(p2))
                        return p2;
                }
            }
            catch { }
            return null;
        }
    }

    internal static class ObjMeshLoader
    {
        public static Mesh Load(string path)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            List<Vector3> positions = new List<Vector3>(8192);
            List<Vector2> uvs = new List<Vector2>(8192);
            List<Vector3> normals = new List<Vector3>(8192);
            List<Vector3> outPos = new List<Vector3>(16384);
            List<Vector2> outUv = new List<Vector2>(16384);
            List<Vector3> outNrm = new List<Vector3>(16384);
            List<int> tris = new List<int>(32768);
            Dictionary<string, int> remap = new Dictionary<string, int>(16384);
            bool anyUv = false;
            bool anyNrm = false;

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                if (string.IsNullOrEmpty(line) || line[0] == '#')
                    continue;
                if (line.Length < 2)
                    continue;
                if (line[0] == 'v' && line[1] == ' ')
                {
                    string[] p = SplitWs(line);
                    if (p.Length >= 4)
                        positions.Add(new Vector3(ParseF(p[1]), ParseF(p[2]), ParseF(p[3])));
                }
                else if (line.StartsWith("vt ", StringComparison.Ordinal))
                {
                    string[] p = SplitWs(line);
                    if (p.Length >= 3)
                        uvs.Add(new Vector2(ParseF(p[1]), ParseF(p[2])));
                }
                else if (line.StartsWith("vn ", StringComparison.Ordinal))
                {
                    string[] p = SplitWs(line);
                    if (p.Length >= 4)
                        normals.Add(new Vector3(ParseF(p[1]), ParseF(p[2]), ParseF(p[3])));
                }
                else if (line[0] == 'f' && line[1] == ' ')
                {
                    string[] p = SplitWs(line);
                    if (p.Length < 4)
                        continue;
                    int[] idx = new int[p.Length - 1];
                    for (int i = 1; i < p.Length; i++)
                    {
                        bool gotUv;
                        bool gotNrm;
                        idx[i - 1] = AddVertex(p[i], positions, uvs, normals,
                            outPos, outUv, outNrm, remap, out gotUv, out gotNrm);
                        if (gotUv)
                            anyUv = true;
                        if (gotNrm)
                            anyNrm = true;
                    }
                    for (int i = 1; i + 1 < idx.Length; i++)
                    {
                        tris.Add(idx[0]);
                        tris.Add(idx[i]);
                        tris.Add(idx[i + 1]);
                    }
                }
            }

            if (outPos.Count == 0 || tris.Count == 0)
                return null;
            while (outUv.Count < outPos.Count)
                outUv.Add(Vector2.zero);
            while (outNrm.Count < outPos.Count)
                outNrm.Add(Vector3.up);

            Mesh mesh = new Mesh();
            if (outPos.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = outPos.ToArray();
            if (anyUv)
                mesh.uv = outUv.ToArray();
            if (anyNrm)
                mesh.normals = outNrm.ToArray();
            mesh.triangles = tris.ToArray();
            if (!anyNrm)
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            try { mesh.RecalculateTangents(); }
            catch { }
            return mesh;
        }

        private static int AddVertex(
            string token,
            List<Vector3> positions,
            List<Vector2> uvs,
            List<Vector3> normals,
            List<Vector3> outPos,
            List<Vector2> outUv,
            List<Vector3> outNrm,
            Dictionary<string, int> remap,
            out bool gotUv,
            out bool gotNrm)
        {
            gotUv = false;
            gotNrm = false;
            int existing;
            if (remap.TryGetValue(token, out existing))
                return existing;
            int vi = -1;
            int ti = -1;
            int ni = -1;
            string[] bits = token.Split('/');
            if (bits.Length > 0 && bits[0].Length > 0)
                vi = ParseIndex(bits[0], positions.Count);
            if (bits.Length > 1 && bits[1].Length > 0)
                ti = ParseIndex(bits[1], uvs.Count);
            if (bits.Length > 2 && bits[2].Length > 0)
                ni = ParseIndex(bits[2], normals.Count);
            outPos.Add((vi >= 0 && vi < positions.Count) ? positions[vi] : Vector3.zero);
            if (ti >= 0 && ti < uvs.Count)
            {
                outUv.Add(uvs[ti]);
                gotUv = true;
            }
            else
                outUv.Add(Vector2.zero);
            if (ni >= 0 && ni < normals.Count)
            {
                outNrm.Add(normals[ni]);
                gotNrm = true;
            }
            else
                outNrm.Add(Vector3.up);
            int id = outPos.Count - 1;
            remap[token] = id;
            return id;
        }

        private static int ParseIndex(string s, int count)
        {
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return -1;
            if (v < 0)
                return count + v;
            return v - 1;
        }

        private static float ParseF(string s)
        {
            float f;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                return f;
            return 0f;
        }

        private static string[] SplitWs(string line)
        {
            return line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
