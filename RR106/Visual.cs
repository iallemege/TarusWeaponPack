using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace RR106
{
    internal static class Visual
    {
        internal const string VisualChildName = "RR106_Visual";
        private const string MarkerName = "RR106_Applied";
        private const float FitCapMeters = 3.4f;
        // Pitch Y-up barrel to +Z, yaw 180 so muzzle is forward, then roll 180 so the cradle faces up.
        private static readonly Quaternion MuzzleFix =
            Quaternion.Euler(0f, 0f, 180f) * Quaternion.Euler(-90f, 180f, 0f);
        private static Texture2D _albedo;

        private static Mesh _mesh;
        private static MtlDef[] _defs;
        private static bool _loadAttempted;
        private static string _lastError;

        internal static void Apply(GameObject root)
        {
            if (root == null)
                return;
            if (root.transform.Find(MarkerName) != null)
                return;
            if (!EnsureLoaded())
            {
                if (Plugin.Log != null && !string.IsNullOrEmpty(_lastError))
                    Plugin.Log.LogWarning("RR106 visual: " + _lastError);
                return;
            }

            try
            {
                Transform sizeRef = FindNamed(root.transform, "gun");
                if (sizeRef == null)
                    sizeRef = FindNamed(root.transform, "gunpod");
                if (sizeRef == null)
                    sizeRef = root.transform;

                Shader donorShader = FindDonorShader(root);

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
                renderer.sharedMaterials = BuildRuntimeMaterials(donorShader);
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;

                FitToMesh(vis, sizeRef != null ? sizeRef.gameObject : root);
                HideDonorMeshes(root);

                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("RR106 visual applied on " + root.name);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("RR106 visual apply failed: " + ex.Message);
            }
        }

        private static void HideDonorMeshes(GameObject root)
        {
            MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
            if (mrs == null)
                return;
            for (int i = 0; i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || mr.transform == null)
                    continue;
                if (IsKeepVisible(mr.transform))
                    continue;
                mr.enabled = false;
            }
        }

        private static bool IsKeepVisible(Transform t)
        {
            while (t != null)
            {
                string n = t.name;
                if (!string.IsNullOrEmpty(n))
                {
                    if (n.IndexOf(VisualChildName, StringComparison.Ordinal) >= 0)
                        return true;
                    if (n.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Muzzle", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                t = t.parent;
            }
            return false;
        }

        private static Shader FindDonorShader(GameObject root)
        {
            MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
            if (mrs == null)
                return null;
            for (int i = 0; i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || mr.sharedMaterial == null || mr.sharedMaterial.shader == null)
                    continue;
                if (mr.sharedMaterial.renderQueue < (int)RenderQueue.Transparent)
                    return mr.sharedMaterial.shader;
            }
            return null;
        }

        private static void FitToMesh(GameObject visual, GameObject sizeRef)
        {
            if (visual == null || _mesh == null)
                return;
            Vector3 ms = _mesh.bounds.size;
            float src = Mathf.Max(ms.x, Mathf.Max(ms.y, ms.z));
            if (src < 0.01f)
                return;
            float dest = DonorSize(sizeRef);
            if (dest < 0.05f)
                dest = FitCapMeters;
            if (dest > FitCapMeters)
                dest = FitCapMeters;
            float s = dest / src;
            if (s > 8f)
                s = 8f;
            if (s < 0.002f)
                s = 0.002f;
            visual.transform.localScale = new Vector3(s, s, s);
        }

        private static float DonorSize(GameObject go)
        {
            if (go == null)
                return 0f;
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs == null || rs.Length == 0)
                return 0f;
            Bounds b = default(Bounds);
            bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || !rs[i].enabled)
                    continue;
                if (IsKeepVisible(rs[i].transform) && rs[i].transform.name.IndexOf(VisualChildName, StringComparison.Ordinal) >= 0)
                    continue;
                if (rs[i].transform != null && rs[i].transform.name == VisualChildName)
                    continue;
                if (!any)
                {
                    b = rs[i].bounds;
                    any = true;
                }
                else
                    b.Encapsulate(rs[i].bounds);
            }
            if (!any)
                return 0f;
            return Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        }

        private static bool EnsureLoaded()
        {
            if (_mesh != null && _defs != null && _defs.Length > 0)
                return true;
            if (_loadAttempted)
                return _mesh != null;
            _loadAttempted = true;

            string objPath = ResolveAssetPath("106mmRR.obj");
            if (string.IsNullOrEmpty(objPath))
            {
                _lastError = "106mmRR.obj not found (expected BepInEx/plugins/RR106Assets/)";
                return false;
            }

            try
            {
                Dictionary<string, MtlDef> mtl = MtlLoader.Load(Path.ChangeExtension(objPath, ".mtl"));
                Mesh mesh;
                string[] matNames;
                if (!ObjMeshLoader.Load(objPath, out mesh, out matNames) || mesh == null)
                {
                    _lastError = "OBJ parse failed: " + objPath;
                    return false;
                }
                mesh.name = "106mmRR";
                ReverseWindingAndNormals(mesh);
                _mesh = mesh;
                _albedo = LoadAlbedo(ResolveAssetPath("106mmRR.png"));
                _defs = new MtlDef[matNames.Length];
                for (int i = 0; i < matNames.Length; i++)
                {
                    MtlDef def;
                    if (matNames[i] != null && mtl != null && mtl.TryGetValue(matNames[i], out def))
                        _defs[i] = def;
                    else
                        _defs[i] = MtlDef.Default;
                }
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("RR106 OBJ verts=" + _mesh.vertexCount
                        + " submeshes=" + _mesh.subMeshCount
                        + " from " + objPath);
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        private static Material[] BuildRuntimeMaterials(Shader preferredShader)
        {
            if (_defs == null || _defs.Length == 0)
                return new Material[] { MakeMaterial(preferredShader, MtlDef.Default) };
            Material[] mats = new Material[_defs.Length];
            for (int i = 0; i < _defs.Length; i++)
                mats[i] = MakeMaterial(preferredShader, _defs[i] != null ? _defs[i] : MtlDef.Default);
            return mats;
        }

        private static Material MakeMaterial(Shader preferred, MtlDef def)
        {
            Shader shader = preferred;
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Diffuse");
            if (shader == null)
                return null;
            Material mat = new Material(shader);
            mat.name = "RR106_" + (def.name != null ? def.name : "mat");
            Texture2D map = TextureFor(def);
            Color albedo = map != null && map != WhiteTex() ? Color.white : def.kd;
            albedo.a = def.opacity;
            try
            {
                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 0f);
                if (mat.HasProperty("_ZWrite"))
                    mat.SetFloat("_ZWrite", 1f);
                if (mat.HasProperty("_Cull"))
                    mat.SetFloat("_Cull", 2f);
                if (mat.HasProperty("_Metallic"))
                    mat.SetFloat("_Metallic", 0f);
                if (mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", 0.22f);
                if (mat.HasProperty("_Glossiness"))
                    mat.SetFloat("_Glossiness", 0.22f);
                if (mat.HasProperty("_GlossMapScale"))
                    mat.SetFloat("_GlossMapScale", 0.22f);
                if (mat.HasProperty("_SpecularHighlights"))
                    mat.SetFloat("_SpecularHighlights", 0f);
                if (mat.HasProperty("_EnvironmentReflections"))
                    mat.SetFloat("_EnvironmentReflections", 0f);
                if (mat.HasProperty("_SpecColor"))
                    mat.SetColor("_SpecColor", Color.black);
                if (mat.HasProperty("_Specular"))
                    mat.SetColor("_Specular", Color.black);
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                mat.DisableKeyword("_SPECULAR_SETUP");
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = (int)RenderQueue.Geometry;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", albedo);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", albedo);
                mat.color = albedo;
                BindBaseMap(mat, map);
                if (def.ke.r + def.ke.g + def.ke.b > 0.05f)
                {
                    Color emit = def.ke;
                    if (emit.maxColorComponent > 2f)
                        emit = emit / emit.maxColorComponent * 2f;
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", emit);
                    }
                }
            }
            catch
            {
            }
            return mat;
        }

        private static Texture2D _white;

        private static Texture2D TextureFor(MtlDef def)
        {
            if (def != null && !string.IsNullOrEmpty(def.mapKd))
            {
                Texture2D mapped = LoadAlbedo(ResolveAssetPath(Path.GetFileName(def.mapKd)));
                if (mapped != null)
                    return mapped;
            }
            if (_albedo != null)
                return _albedo;
            return WhiteTex();
        }

        private static Texture2D WhiteTex()
        {
            if (_white != null)
                return _white;
            Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
            t.SetPixel(0, 0, Color.white);
            t.Apply(false, true);
            t.name = "RR106_White";
            _white = t;
            return _white;
        }

        private static void BindBaseMap(Material mat, Texture2D tex)
        {
            if (mat == null)
                return;
            Texture2D map = tex != null ? tex : WhiteTex();
            mat.mainTexture = map;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", map);
                mat.SetTextureScale("_BaseMap", Vector2.one);
                mat.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", map);
                mat.SetTextureScale("_MainTex", Vector2.one);
                mat.SetTextureOffset("_MainTex", Vector2.zero);
            }
            try { mat.EnableKeyword("_BASEMAP"); }
            catch { }
        }

        internal static string ResolveAssetPath(string fileName)
        {
            try
            {
                string p = Path.Combine(Paths.PluginPath, "TarusWeaponPackAssets", fileName);
                if (File.Exists(p))
                    return p;
            }
            catch
            {
            }
            try
            {
                string p = Path.Combine(Paths.PluginPath, "RR106Assets", fileName);
                if (File.Exists(p))
                    return p;
            }
            catch
            {
            }
            try
            {
                string asm = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asm))
                {
                    string dir = Path.GetDirectoryName(asm);
                    string p1 = Path.Combine(dir, "RR106Assets", fileName);
                    if (File.Exists(p1))
                        return p1;
                    string p2 = Path.Combine(dir, "assets", fileName);
                    if (File.Exists(p2))
                        return p2;
                }
            }
            catch
            {
            }
            return null;
        }

        private static Texture2D LoadAlbedo(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.name = "106mmRR_albedo";
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            catch
            {
                return null;
            }
        }

        private static void ReverseWindingAndNormals(Mesh mesh)
        {
            if (mesh == null)
                return;
            int sub = mesh.subMeshCount;
            for (int s = 0; s < sub; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                if (tris == null || tris.Length < 3)
                    continue;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    int tmp = tris[i + 1];
                    tris[i + 1] = tris[i + 2];
                    tris[i + 2] = tmp;
                }
                mesh.SetTriangles(tris, s, true);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            try
            {
                mesh.RecalculateTangents();
            }
            catch
            {
            }
        }

        private static Transform FindNamed(Transform root, string name)
        {
            if (root == null)
                return null;
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform f = FindNamed(root.GetChild(i), name);
                if (f != null)
                    return f;
            }
            return null;
        }
    }

    internal sealed class MtlDef
    {
        internal static readonly MtlDef Default = MakeDefault();

        internal string name;
        internal Color kd = new Color(0.18f, 0.18f, 0.18f, 1f);
        internal Color ke = Color.black;
        internal float ns = 80f;
        internal float opacity = 1f;
        internal string mapKd;

        private static MtlDef MakeDefault()
        {
            MtlDef d = new MtlDef();
            d.name = "default";
            return d;
        }
    }

    internal static class MtlLoader
    {
        internal static Dictionary<string, MtlDef> Load(string path)
        {
            Dictionary<string, MtlDef> map = new Dictionary<string, MtlDef>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return map;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            MtlDef cur = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line) || line[0] == '#')
                    continue;
                string[] p = SplitWs(line);
                if (p.Length == 0)
                    continue;
                if (p[0] == "newmtl")
                {
                    cur = new MtlDef();
                    cur.name = p.Length > 1 ? p[1] : ("mat" + map.Count);
                    map[cur.name] = cur;
                }
                else if (cur == null)
                    continue;
                else if (p[0] == "Kd" && p.Length >= 4)
                    cur.kd = new Color(ParseF(p[1]), ParseF(p[2]), ParseF(p[3]), 1f);
                else if (p[0] == "Ke" && p.Length >= 4)
                    cur.ke = new Color(ParseF(p[1]), ParseF(p[2]), ParseF(p[3]), 1f);
                else if (p[0] == "Ns" && p.Length >= 2)
                    cur.ns = ParseF(p[1]);
                else if (p[0] == "d" && p.Length >= 2)
                    cur.opacity = ParseF(p[1]);
                else if (p[0] == "map_Kd" && p.Length >= 2)
                    cur.mapKd = p[p.Length - 1];
            }
            return map;
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

    internal static class ObjMeshLoader
    {
        public static bool Load(string path, out Mesh mesh, out string[] materialNames)
        {
            mesh = null;
            materialNames = null;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            List<Vector3> positions = new List<Vector3>(8192);
            List<Vector2> uvs = new List<Vector2>(8192);
            List<Vector3> normals = new List<Vector3>(8192);
            List<Vector3> outPos = new List<Vector3>(16384);
            List<Vector2> outUv = new List<Vector2>(16384);
            List<Vector3> outNrm = new List<Vector3>(16384);
            Dictionary<string, int> remap = new Dictionary<string, int>(16384);
            List<string> matOrder = new List<string>();
            Dictionary<string, List<int>> trisByMat = new Dictionary<string, List<int>>();
            string curMat = "_default";
            EnsureMat(curMat, matOrder, trisByMat);
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
                else if (line.StartsWith("usemtl", StringComparison.Ordinal))
                {
                    string[] p = SplitWs(line);
                    curMat = p.Length > 1 && !string.IsNullOrEmpty(p[1]) ? p[1] : "_default";
                    EnsureMat(curMat, matOrder, trisByMat);
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
                    List<int> tris = trisByMat[curMat];
                    for (int i = 1; i + 1 < idx.Length; i++)
                    {
                        tris.Add(idx[0]);
                        tris.Add(idx[i]);
                        tris.Add(idx[i + 1]);
                    }
                }
            }

            if (outPos.Count == 0)
                return false;
            while (outUv.Count < outPos.Count)
                outUv.Add(Vector2.zero);
            while (outNrm.Count < outPos.Count)
                outNrm.Add(Vector3.up);

            List<string> used = new List<string>();
            for (int i = 0; i < matOrder.Count; i++)
            {
                List<int> t = trisByMat[matOrder[i]];
                if (t != null && t.Count >= 3)
                    used.Add(matOrder[i]);
            }
            if (used.Count == 0)
                return false;

            Mesh m = new Mesh();
            if (outPos.Count > 65535)
                m.indexFormat = IndexFormat.UInt32;
            m.vertices = outPos.ToArray();
            if (anyUv)
                m.uv = outUv.ToArray();
            if (anyNrm)
                m.normals = outNrm.ToArray();
            m.subMeshCount = used.Count;
            for (int i = 0; i < used.Count; i++)
                m.SetTriangles(trisByMat[used[i]].ToArray(), i, true);
            if (!anyNrm)
                m.RecalculateNormals();
            m.RecalculateBounds();
            try
            {
                m.RecalculateTangents();
            }
            catch
            {
            }
            mesh = m;
            materialNames = used.ToArray();
            return true;
        }

        private static void EnsureMat(string name, List<string> order, Dictionary<string, List<int>> tris)
        {
            if (tris.ContainsKey(name))
                return;
            order.Add(name);
            tris[name] = new List<int>(1024);
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
