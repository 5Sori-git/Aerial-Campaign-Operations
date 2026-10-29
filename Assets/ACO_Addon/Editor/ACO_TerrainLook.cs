using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using ACO;

/// 육각 지형 자연 머티리얼(셰이더 ACO/HexTerrain + Textures/Terrain의 노이즈 텍스처) + 나무 묶음(셰이더 ACO/HexTrees, 기둥 자식 "Trees").
/// 색은 월드 높이·경사로 셰이더가 정하므로 머티리얼 하나를 모든 기둥이 같이 쓴다 — ACO_HexTerrain.HeightMaterials는 비운다(높이대별 교체 안 함).
/// 텍스처는 Tools/aco_terrain_tex.py가 만든다(데이터 텍스처라 sRGB 끔, 노멀은 Normal map으로 가져온다).
public static class ACO_TerrainLook
{
    public const string TextureDir = "Assets/ACO_Addon/Textures/Terrain";
    public const string MaterialPath = "Assets/ACO_Addon/Generated/ACO_HexTerrainNatural.mat";
    private const string ShaderName = "ACO/HexTerrain";

    /// 자연 머티리얼을 찾거나 만든다(텍스처 가져오기 설정도 맞춘다). 셰이더나 텍스처가 없으면 null.
    public static Material GetOrCreateMaterial()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null) { Debug.LogWarning($"[ACO] 셰이더 {ShaderName}를 찾지 못했습니다(Shaders/ACO_HexTerrain.shader)."); return null; }
        Texture2D detail = ImportTexture($"{TextureDir}/ACO_TerrainDetail.png", false);
        Texture2D normal = ImportTexture($"{TextureDir}/ACO_TerrainDetailNormal.png", true);
        Texture2D macro = ImportTexture($"{TextureDir}/ACO_TerrainMacro.png", false);
        if (detail == null || normal == null || macro == null)
        {
            Debug.LogWarning($"[ACO] 지형 텍스처가 {TextureDir}에 없습니다 — python Tools/aco_terrain_tex.py {TextureDir} 로 만드세요.");
            return null;
        }

        Material m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (m == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
            m = new Material(shader) { name = "ACO_HexTerrainNatural" };
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, MaterialPath);
        }
        // 셰이더·텍스처는 늘 다시 잇는다(색·높이 같은 값은 사용자가 바꾼 대로 둔다).
        if (m.shader != shader) { m.shader = shader; }
        m.SetTexture("_DetailTex", detail);
        m.SetTexture("_DetailNormal", normal);
        m.SetTexture("_MacroTex", macro);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Texture2D ImportTexture(string path, bool isNormal)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { return null; }
        bool changed = false;
        TextureImporterType type = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != type) { ti.textureType = type; changed = true; }
        if (!isNormal && ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }   // 노이즈 값(데이터)
        if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
        if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
        if (ti.anisoLevel < 4) { ti.anisoLevel = 4; changed = true; }   // 비스듬히 내려다보는 지면이 흐려지지 않게
        if (!isNormal && ti.alphaSource != TextureImporterAlphaSource.FromInput) { ti.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
        if (!isNormal && ti.alphaIsTransparency) { ti.alphaIsTransparency = false; changed = true; }
        if (changed) { ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// 육각 기둥 프리팹에 자연 머티리얼과 나무 묶음(자식 "Trees")을 넣고, 씬의 육각 지형 HeightMaterials를 비운다(높이대별 교체를 끈다).
    /// 지면 머티리얼 값(해수면·높이·숲 양 등)을 나무 머티리얼에 복사하므로, 지면 머티리얼을 바꾼 뒤에도 다시 누르면 나무가 숲 색에 맞춰진다.
    [MenuItem("ACO/Terrain/Apply Natural Terrain Material")]
    public static void Apply()
    {
        Material m = GetOrCreateMaterial();
        if (m == null) { return; }

        int terrains = 0;
        bool seaSynced = false;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_HexTerrain hex in root.GetComponentsInChildren<ACO_HexTerrain>(true))
            {
                // 기둥 높이는 월드 Y 그대로라 지형의 해수면을 셰이더 해수면으로 쓴다(모래·젖은 모래·물속 어둡게의 기준).
                if (!seaSynced)
                {
                    seaSynced = true;
                    if (!Mathf.Approximately(m.GetFloat("_SeaLevel"), hex.SeaLevel)) { m.SetFloat("_SeaLevel", hex.SeaLevel); EditorUtility.SetDirty(m); }
                }
                if (hex.HeightMaterials != null && hex.HeightMaterials.Length == 0) { continue; }
                Undo.RecordObject(hex, "ACO natural terrain");
                hex.HeightMaterials = new Material[0];
                PrefabUtility.RecordPrefabInstancePropertyModifications(hex);
                EditorUtility.SetDirty(hex);
                terrains++;
            }
        }

        Material treeMat = GetOrCreateTreeMaterial(m);
        string path = ACO_PrefabBuilder.PathOf("ACO_HexColumn");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { ACO_PrefabBuilder.BuildHexColumn(); }
        else
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            MeshRenderer r = contents.GetComponent<MeshRenderer>();
            if (r != null) { r.sharedMaterial = m; }
            if (treeMat != null) { EnsureTrees(contents, treeMat, ACO_PrefabBuilder.HexRadius); }
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 육각 지형 자연 머티리얼 적용: {MaterialPath} + 나무 {TreeMaterialPath} → ACO_HexColumn 프리팹, 씬 육각 지형 {terrains}개의 높이대 머티리얼을 비웠습니다. "
            + "색·높이 경계·숲 양은 지면 머티리얼에서 바꾸고 다시 적용하면 나무도 맞춰집니다.", m);
    }

    /// 셋업 검사용: 기둥 프리팹이 자연 머티리얼을 쓰고 나무 묶음이 있는지.
    public static bool IsApplied()
    {
        GameObject column = AssetDatabase.LoadAssetAtPath<GameObject>(ACO_PrefabBuilder.PathOf("ACO_HexColumn"));
        MeshRenderer r = column != null ? column.GetComponent<MeshRenderer>() : null;
        return r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == ShaderName
            && column.transform.Find("Trees") != null;
    }

    // ───────────── 나무 ─────────────

    public const string TreeMaterialPath = "Assets/ACO_Addon/Generated/ACO_HexTrees.mat";
    public const string TreeMeshPath = "Assets/ACO_Addon/Generated/ACO_TreePatch.asset";
    private const string TreeShaderName = "ACO/HexTrees";
    /// 기둥 윗면 하나에 까는 나무 자리 수(정점 = 26 × 이 값). 셰이더가 숲인 곳에서만 세운다.
    public const int TreesPerColumn = 500;
    /// 나무 묶음 LOD: 화면 높이 비율이 이보다 작으면(VR 기준 약 3km 밖) 아예 그리지 않는다. 셰이더는 그 전에(1.8~2.6km) 작아지며 사라진다.
    public const float TreeLodHeight = 0.12f;

    // 지면 머티리얼과 나무 머티리얼이 같이 쓰는 값(숲 분포를 맞춘다)
    private static readonly string[] SharedFloats = { "_DetailScale2", "_MacroScale", "_SeaLevel", "_SandHeight", "_DirtHeight", "_BlendNoise", "_BlendWidth", "_ForestAmount", "_DryAmount" };
    private static readonly string[] SharedTextures = { "_DetailTex", "_MacroTex" };

    /// 나무 머티리얼을 찾거나 만들고 지면 머티리얼의 공유 값을 복사한다. 셰이더가 없으면 null.
    public static Material GetOrCreateTreeMaterial(Material terrain)
    {
        Shader shader = Shader.Find(TreeShaderName);
        if (shader == null) { Debug.LogWarning($"[ACO] 셰이더 {TreeShaderName}를 찾지 못했습니다(Shaders/ACO_HexTrees.shader)."); return null; }
        Material t = AssetDatabase.LoadAssetAtPath<Material>(TreeMaterialPath);
        if (t == null)
        {
            t = new Material(shader) { name = "ACO_HexTrees" };
            t.enableInstancing = true;
            AssetDatabase.CreateAsset(t, TreeMaterialPath);
        }
        if (t.shader != shader) { t.shader = shader; }
        if (terrain != null)
        {
            foreach (string p in SharedFloats) { if (terrain.HasProperty(p)) { t.SetFloat(p, terrain.GetFloat(p)); } }
            foreach (string p in SharedTextures) { if (terrain.HasProperty(p)) { t.SetTexture(p, terrain.GetTexture(p)); } }
        }
        t.enableInstancing = true;
        EditorUtility.SetDirty(t);
        return t;
    }

    /// 기둥 루트에 나무 묶음 자식 "Trees"를 만들거나 고친다: 메시(공유 에셋) + 렌더러(그림자 끔) + LOD Group(멀면 안 그림). 충돌 없음.
    public static void EnsureTrees(GameObject column, Material treeMat, float hexRadius)
    {
        Mesh mesh = GetOrCreateTreeMesh(hexRadius);
        Transform t = column.transform.Find("Trees");
        GameObject go;
        if (t == null)
        {
            go = new GameObject("Trees");
            go.transform.SetParent(column.transform, false);
        }
        else { go = t.gameObject; }
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null) { mf = go.AddComponent<MeshFilter>(); }
        mf.sharedMesh = mesh;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr == null) { mr = go.AddComponent<MeshRenderer>(); }
        mr.sharedMaterial = treeMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        LODGroup lg = go.GetComponent<LODGroup>();
        if (lg == null) { lg = go.AddComponent<LODGroup>(); }
        lg.fadeMode = LODFadeMode.None;
        lg.SetLODs(new[] { new LOD(TreeLodHeight, new Renderer[] { mr }) });
        lg.localReferencePoint = Vector3.zero;
        lg.size = hexRadius * 2f;
    }

    /// 나무 묶음 메시(에셋 하나를 모든 기둥이 같이 쓴다). 반지름이 바뀌면 다시 만든다.
    public static Mesh GetOrCreateTreeMesh(float hexRadius)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(TreeMeshPath);
        if (mesh != null && Mathf.Abs(mesh.bounds.size.x - (hexRadius * 2f + 40f)) < 1f) { return mesh; }
        if (mesh != null) { AssetDatabase.DeleteAsset(TreeMeshPath); }
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
        mesh = BuildTreePatchMesh(hexRadius, TreesPerColumn, 1234);
        AssetDatabase.CreateAsset(mesh, TreeMeshPath);
        return mesh;
    }

    // 나무 한 그루 정점 구조(두 모양이 같은 구조): 0 = 수관 밑 가운데, 1~6 = 아래 고리, 7~12 = 가운데 고리(30° 돌림), 13~18 = 위 고리,
    // 19 = 꼭대기, 20~22 = 줄기 밑, 23~25 = 줄기 위. 높이 1 기준 — 셰이더가 실제 높이를 곱한다.
    private const int TreeVerts = 26;
    // (높이, 반지름): 밑 가운데, 아래 고리, 가운데 고리, 위 고리, 꼭대기
    private static readonly Vector2[] ConiferProfile = { new Vector2(0.18f, 0f), new Vector2(0.18f, 0.32f), new Vector2(0.46f, 0.22f), new Vector2(0.72f, 0.11f), new Vector2(1f, 0f) };
    private static readonly Vector2[] BroadleafProfile = { new Vector2(0.26f, 0f), new Vector2(0.28f, 0.24f), new Vector2(0.55f, 0.42f), new Vector2(0.83f, 0.3f), new Vector2(0.98f, 0f) };
    private const float TrunkRadius = 0.035f;
    private const float TrunkTop = 0.3f;

    private static Vector3[] TreeShape(Vector2[] prof)
    {
        var p = new Vector3[TreeVerts];
        p[0] = new Vector3(0f, prof[0].x, 0f);
        for (int ring = 0; ring < 3; ring++)
        {
            float offset = ring == 1 ? 30f : 0f;
            for (int k = 0; k < 6; k++)
            {
                float a = (k * 60f + offset) * Mathf.Deg2Rad;
                p[1 + ring * 6 + k] = new Vector3(Mathf.Cos(a) * prof[ring + 1].y, prof[ring + 1].x, Mathf.Sin(a) * prof[ring + 1].y);
            }
        }
        p[19] = new Vector3(0f, prof[4].x, 0f);
        for (int k = 0; k < 3; k++)
        {
            float a = k * 120f * Mathf.Deg2Rad;
            p[20 + k] = new Vector3(Mathf.Cos(a) * TrunkRadius, 0f, Mathf.Sin(a) * TrunkRadius);
            p[23 + k] = new Vector3(Mathf.Cos(a) * TrunkRadius, TrunkTop, Mathf.Sin(a) * TrunkRadius);
        }
        return p;
    }

    /// 한 그루 삼각형(앞면이 바깥을 보게 — 기준 모양 conifer로 방향을 맞춘다).
    private static int[] TreeTriangles(Vector3[] p)
    {
        var t = new System.Collections.Generic.List<int>();
        for (int k = 0; k < 6; k++)
        {
            int n = (k + 1) % 6;
            int r0k = 1 + k, r0n = 1 + n, r1k = 7 + k, r1n = 7 + n, r2k = 13 + k, r2n = 13 + n;
            Tri(t, p, 0, r0k, r0n, Vector3.down);
            Tri(t, p, r0k, r0n, r1k, Outward(p, r0k, r0n, r1k));
            Tri(t, p, r1k, r0n, r1n, Outward(p, r1k, r0n, r1n));
            Tri(t, p, r2k, r2n, r1k, Outward(p, r2k, r2n, r1k));
            Tri(t, p, r1k, r2n, r1n, Outward(p, r1k, r2n, r1n));
            Tri(t, p, r2k, r2n, 19, Outward(p, r2k, r2n, 19));
        }
        for (int k = 0; k < 3; k++)
        {
            int n = (k + 1) % 3;
            Vector3 o = Outward(p, 20 + k, 20 + n, 23 + k);
            Tri(t, p, 20 + k, 20 + n, 23 + n, o);
            Tri(t, p, 20 + k, 23 + n, 23 + k, o);
        }
        return t.ToArray();
    }

    private static Vector3 Outward(Vector3[] p, int a, int b, int c)
    {
        Vector3 m = (p[a] + p[b] + p[c]) / 3f;
        m.y = 0f;
        return m.sqrMagnitude > 1e-8f ? m.normalized : Vector3.up;
    }

    private static void Tri(System.Collections.Generic.List<int> t, Vector3[] p, int a, int b, int c, Vector3 expected)
    {
        if (Vector3.Dot(Vector3.Cross(p[b] - p[a], p[c] - p[a]), expected) < 0f) { int tmp = b; b = c; c = tmp; }
        t.Add(a); t.Add(b); t.Add(c);
    }

    private static Vector3[] SmoothNormals(Vector3[] p, int[] tris)
    {
        var n = new Vector3[p.Length];
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 f = Vector3.Cross(p[tris[i + 1]] - p[tris[i]], p[tris[i + 2]] - p[tris[i]]);
            n[tris[i]] += f; n[tris[i + 1]] += f; n[tris[i + 2]] += f;
        }
        for (int i = 0; i < n.Length; i++) { n[i] = n[i].sqrMagnitude > 1e-12f ? n[i].normalized : Vector3.up; }
        return n;
    }

    /// 나무 묶음 메시: 육각 윗면(꼭짓점 ±Z, 꼭짓점 반지름 hexRadius) 안에 count그루 자리를 고르게(최소 간격) 흩는다.
    /// 정점 = 밑동 + 침엽수 모양(높이 1), UV0 = 밑동 XZ, UV2/UV3 = 활엽수 모양/노멀, 정점 색 r = 아래쪽 어둡게, a = 잎(1)/줄기(0).
    public static Mesh BuildTreePatchMesh(float hexRadius, int count, int seed)
    {
        Vector3[] con = TreeShape(ConiferProfile);
        Vector3[] bro = TreeShape(BroadleafProfile);
        int[] tris = TreeTriangles(con);
        Vector3[] conN = SmoothNormals(con, tris);
        Vector3[] broN = SmoothNormals(bro, tris);

        // 자리: 육각형 안(가장자리에서 margin 안쪽), 최소 간격을 두고 다트 던지기
        var rng = new System.Random(seed);
        float margin = 18f;
        float inner = hexRadius * 0.8660254f - margin;
        float area = 2.598076f * hexRadius * hexRadius;
        float minDist = Mathf.Sqrt(area / count) * 0.75f;
        var bases = new System.Collections.Generic.List<Vector2>();
        int tries = 0;
        while (bases.Count < count && tries < count * 200)
        {
            tries++;
            var q = new Vector2(((float)rng.NextDouble() * 2f - 1f) * hexRadius, ((float)rng.NextDouble() * 2f - 1f) * hexRadius);
            bool inside = true;
            for (int k = 0; k < 3 && inside; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                if (Mathf.Abs(q.x * Mathf.Cos(a) + q.y * Mathf.Sin(a)) > inner) { inside = false; }
            }
            if (!inside) { continue; }
            bool far = true;
            foreach (Vector2 b in bases) { if ((b - q).sqrMagnitude < minDist * minDist) { far = false; break; } }
            if (far) { bases.Add(q); }
        }

        int n = bases.Count;
        var verts = new Vector3[n * TreeVerts];
        var norms = new Vector3[n * TreeVerts];
        var colors = new Color[n * TreeVerts];
        var uv0 = new System.Collections.Generic.List<Vector2>(n * TreeVerts);
        var uv2 = new System.Collections.Generic.List<Vector3>(n * TreeVerts);
        var uv3 = new System.Collections.Generic.List<Vector3>(n * TreeVerts);
        var indices = new int[n * tris.Length];
        for (int i = 0; i < n; i++)
        {
            Vector3 b = new Vector3(bases[i].x, 0f, bases[i].y);
            int o = i * TreeVerts;
            for (int v = 0; v < TreeVerts; v++)
            {
                verts[o + v] = b + con[v];
                norms[o + v] = conN[v];
                bool trunk = v >= 20;
                colors[o + v] = trunk ? new Color(0.8f, 0f, 0f, 0f) : new Color(Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(0.18f, 1f, con[v].y)), 0f, 0f, 1f);
                uv0.Add(new Vector2(b.x, b.z));
                uv2.Add(bro[v]);
                uv3.Add(broN[v]);
            }
            for (int k = 0; k < tris.Length; k++) { indices[i * tris.Length + k] = o + tris[k]; }
        }

        var mesh = new Mesh { name = "ACO_TreePatch" };
        if (verts.Length > 65000) { mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.colors = colors;
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(2, uv2);
        mesh.SetUVs(3, uv3);
        mesh.triangles = indices;
        // 셰이더가 나무를 키우므로(최대 약 30m) 경계를 넉넉히 — 컬링에서 잘리지 않게
        mesh.bounds = new Bounds(new Vector3(0f, 20f, 0f), new Vector3(hexRadius * 2f + 40f, 60f, hexRadius * 2f + 40f));
        Debug.Log($"[ACO] 나무 묶음 메시: {n}그루, 정점 {verts.Length}");
        return mesh;
    }
}
