using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UdonSharpEditor;
using ACO;

/// 뭉게구름 + 비 + 날씨 (씬 오브젝트 ACO_Sky: ACO_CloudField + ACO_Weather + 구름 풀 "Clouds" + "Rain"(ACO_Rain)).
/// 구름 메시: 덩어리(타원체 몇 개, 바닥은 평평)의 겉껍질에 퍼프를 뿌린다 — 퍼프마다 정점 4개가 중심에 겹쳐 있고 셰이더(ACO/CloudPuffs)가 펼친다.
///   정점 색에 차폐(위는 밝고 아래·안쪽은 어둡게)를, 법선에 덩어리 바깥 방향을 굽는다. 모양 변형 VariantCount개를 Generated/Clouds에 저장하고
///   풀(PoolSize개)이 돌아가며 쓴다. 덩어리마다 속 타원체(퍼프 중심 범위의 85%)를 구워 ACO_CloudField가 구름 속 안개 판정에 쓴다.
/// 비 메시: 단위 상자 안 빗방울 RainDrops개(정점 4개가 한 점에) — 셰이더(ACO/Rain)가 카메라 둘레에 반복시킨다.
/// 다시 만들면 메시·풀은 새로 만들고, 머티리얼과 스크립트 값(덮개·고도·비 세기 등)은 그대로 둔다.
public static class ACO_SkyBuilder
{
    public const string SkyName = "ACO_Sky";
    private const string GeneratedDir = "Assets/ACO_Addon/Generated";
    private const string CloudMeshDir = "Assets/ACO_Addon/Generated/Clouds";
    private const string CloudMaterialPath = "Assets/ACO_Addon/Generated/ACO_Clouds.mat";
    private const string RainMaterialPath = "Assets/ACO_Addon/Generated/ACO_Rain.mat";
    private const string RainMeshPath = "Assets/ACO_Addon/Generated/ACO_RainDrops.asset";
    private const string PuffTexturePath = "Assets/ACO_Addon/Textures/Clouds/ACO_CloudPuffs.png";
    private const int VariantCount = 10;
    private const int PoolSize = 64;
    private const int RainDrops = 4000;

    private struct Shape
    {
        public float Radius, Height, PuffMin, PuffMax;
        public int Blobs, Puffs;
        public bool Flat;
        public Shape(float radius, float height, int blobs, int puffs, float puffMin, float puffMax, bool flat)
        {
            Radius = radius; Height = height; Blobs = blobs; Puffs = puffs; PuffMin = puffMin; PuffMax = puffMax; Flat = flat;
        }
    }

    // 작은 적운, 중간 적운, 큰(높은) 적운, 납작한 층적운
    private static readonly Shape[] Shapes =
    {
        new Shape(420f, 380f, 3, 40, 120f, 220f, false),
        new Shape(650f, 560f, 5, 60, 150f, 290f, false),
        new Shape(420f, 420f, 4, 45, 120f, 220f, false),
        new Shape(820f, 900f, 6, 80, 180f, 350f, false),
        new Shape(1000f, 230f, 5, 70, 150f, 280f, true),
    };

    private struct Blob { public Vector3 C; public float R, Ry; }

    [MenuItem("ACO/Environment/Create Clouds and Rain")]
    public static void Create()
    {
        EnsureFolder(GeneratedDir);
        EnsureFolder(CloudMeshDir);
        Material cloudMat = GetOrCreateCloudMaterial();
        Material rainMat = GetOrCreateMaterial(RainMaterialPath, "ACO/Rain", "ACO_Rain");
        if (cloudMat == null || rainMat == null) { return; }

        // 구름 메시 변형
        var meshes = new Mesh[VariantCount];
        var coreCenters = new Vector3[VariantCount];
        var coreExtents = new Vector3[VariantCount];
        for (int v = 0; v < VariantCount; v++)
        {
            string path = $"{CloudMeshDir}/ACO_Cloud_{v:00}.asset";
            Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = m == null;
            if (isNew) { m = new Mesh(); }
            m.Clear();
            m.name = $"ACO_Cloud_{v:00}";
            BuildCloud(m, Shapes[v % Shapes.Length], 1000 + v * 7919, out coreCenters[v], out coreExtents[v]);
            if (isNew) { AssetDatabase.CreateAsset(m, path); } else { EditorUtility.SetDirty(m); }
            meshes[v] = m;
        }

        // 비 메시
        Mesh rain = AssetDatabase.LoadAssetAtPath<Mesh>(RainMeshPath);
        bool rainNew = rain == null;
        if (rainNew) { rain = new Mesh(); }
        rain.Clear();
        rain.name = "ACO_RainDrops";
        BuildRain(rain);
        if (rainNew) { AssetDatabase.CreateAsset(rain, RainMeshPath); } else { EditorUtility.SetDirty(rain); }
        AssetDatabase.SaveAssets();

        // 씬 오브젝트
        GameObject sky = FindRoot(SkyName);
        bool created = sky == null;
        if (created)
        {
            sky = new GameObject(SkyName);
            Undo.RegisterCreatedObjectUndo(sky, "ACO sky");
        }
        ACO_CloudField field = sky.GetComponent<ACO_CloudField>();
        if (field == null) { field = sky.AddUdonSharpComponent<ACO_CloudField>(); }

        Transform cloudsRoot = sky.transform.Find("Clouds");
        if (cloudsRoot != null) { Undo.DestroyObjectImmediate(cloudsRoot.gameObject); }
        cloudsRoot = new GameObject("Clouds").transform;
        Undo.RegisterCreatedObjectUndo(cloudsRoot.gameObject, "ACO sky");
        cloudsRoot.SetParent(sky.transform, false);
        var pool = new Transform[PoolSize];
        var centers = new Vector3[PoolSize];
        var extents = new Vector3[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            int v = i % VariantCount;
            var go = new GameObject($"Cloud_{i:00}");
            go.transform.SetParent(cloudsRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = meshes[v];
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = cloudMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            go.SetActive(false);
            pool[i] = go.transform;
            centers[i] = coreCenters[v];
            extents[i] = coreExtents[v];
        }
        Undo.RecordObject(field, "ACO sky");
        field.Clouds = pool;
        field.CloudCoreCenters = centers;
        field.CloudCoreExtents = extents;
        ACO_HexTerrain terrain = Object.FindObjectOfType<ACO_HexTerrain>(true);
        if (terrain != null)
        {
            field.Terrain = terrain;
            if (created) { field.FieldRadius = terrain.FieldRadius * 0.9f; }
        }
        EditorUtility.SetDirty(field);

        // 비
        Transform rainT = sky.transform.Find("Rain");
        if (rainT == null)
        {
            rainT = new GameObject("Rain").transform;
            Undo.RegisterCreatedObjectUndo(rainT.gameObject, "ACO sky");
            rainT.SetParent(sky.transform, false);
        }
        MeshFilter rmf = rainT.GetComponent<MeshFilter>();
        if (rmf == null) { rmf = rainT.gameObject.AddComponent<MeshFilter>(); }
        rmf.sharedMesh = rain;
        MeshRenderer rmr = rainT.GetComponent<MeshRenderer>();
        if (rmr == null) { rmr = rainT.gameObject.AddComponent<MeshRenderer>(); }
        rmr.sharedMaterial = rainMat;
        rmr.shadowCastingMode = ShadowCastingMode.Off;
        rmr.receiveShadows = false;
        rmr.lightProbeUsage = LightProbeUsage.Off;
        rmr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        rmr.enabled = false;
        ACO_Rain rainFx = rainT.GetComponent<ACO_Rain>();
        if (rainFx == null) { rainFx = rainT.gameObject.AddUdonSharpComponent<ACO_Rain>(); }
        Undo.RecordObject(rainFx, "ACO sky");
        rainFx.RainRenderer = rmr;
        if (rainFx.ShelterBoxes == null || rainFx.ShelterBoxes.Length == 0)
        {
            Transform shelter = EnsureHangarShelter(sky.transform);
            if (shelter != null) { rainFx.ShelterBoxes = new[] { shelter }; }
        }
        EditorUtility.SetDirty(rainFx);

        // 날씨: 지형 시드로 맑음/구름 많음/안개/비를 고른다
        ACO_Weather weather = sky.GetComponent<ACO_Weather>();
        if (weather == null) { weather = sky.AddUdonSharpComponent<ACO_Weather>(); }
        Undo.RecordObject(weather, "ACO sky");
        weather.Clouds = field;
        weather.Rain = rainFx;
        weather.Terrain = terrain;
        if (weather.Sun == null) { weather.Sun = FindSun(); }
        EditorUtility.SetDirty(weather);

        if (!RenderSettings.fog)
        {
            Debug.LogWarning("[ACO] 씬 안개가 꺼져 있어 구름 속 안개가 보이지 않습니다 — 조명 설정(ACO 도구 창)을 하거나 Lighting 창에서 Fog를 켜세요(Exponential Squared).");
        }
        EditorSceneManagerMarkDirty(sky.scene);
        Selection.activeGameObject = sky;
        Debug.Log($"[ACO] 하늘: 구름 메시 {VariantCount}종 + 풀 {PoolSize}개, 빗방울 {RainDrops}개, 날씨(태양: {(weather.Sun != null ? weather.Sun.name : "없음")}). "
            + "구름 자리와 날씨(맑음/구름 많음/안개/비)는 육각 지형 시드로 정해진다 — 리롤하면 바뀐다. 테스트는 ACO_Weather.ForcedWeather.", sky);
    }

    public static bool Exists() { return FindRoot(SkyName) != null; }

    /// 씬의 태양: Lighting 설정의 Sun, 없으면 가장 밝은 디렉셔널 라이트.
    public static Light FindSun()
    {
        if (RenderSettings.sun != null) { return RenderSettings.sun; }
        Light best = null;
        foreach (Light l in Object.FindObjectsOfType<Light>(true))
        {
            if (l.type == LightType.Directional && (best == null || l.intensity > best.intensity)) { best = l; }
        }
        return best;
    }

    // ───────────── 구름 메시 ─────────────

    private static void BuildCloud(Mesh mesh, Shape s, int seed, out Vector3 coreCenter, out Vector3 coreExtent)
    {
        var rnd = new System.Random(seed);
        float R() => (float)rnd.NextDouble();

        var blobs = new Blob[s.Blobs];
        for (int b = 0; b < s.Blobs; b++)
        {
            float a = R() * Mathf.PI * 2f;
            float d = b == 0 ? 0f : s.Radius * (0.25f + 0.4f * R());
            float r = b == 0 ? s.Radius * (s.Flat ? 0.6f : 0.55f) : s.Radius * (s.Flat ? 0.45f + 0.25f * R() : 0.38f + 0.22f * R());
            float ry = s.Flat ? s.Height * (0.7f + 0.3f * R()) : s.Height * (0.55f + 0.35f * R()) * (b == 0 ? 1f : 0.75f);
            float cy = ry * 0.45f + (s.Flat ? 0f : s.Height * 0.12f * R());
            blobs[b] = new Blob { C = new Vector3(Mathf.Cos(a) * d, cy, Mathf.Sin(a) * d), R = r, Ry = ry };
        }

        // 겉껍질 깊이(m, 안쪽이 양수): 타원체 합집합, 바닥(y = 0)은 평평
        float Depth(Vector3 p)
        {
            float best = -1e9f;
            foreach (Blob bl in blobs)
            {
                Vector3 q = p - bl.C;
                float len = Mathf.Sqrt(q.x * q.x / (bl.R * bl.R) + q.y * q.y / (bl.Ry * bl.Ry) + q.z * q.z / (bl.R * bl.R));
                best = Mathf.Max(best, (1f - len) * Mathf.Min(bl.R, bl.Ry));
            }
            return Mathf.Min(best, p.y);
        }

        float xzMax = 0f, top = 0f;
        foreach (Blob bl in blobs)
        {
            xzMax = Mathf.Max(xzMax, new Vector2(bl.C.x, bl.C.z).magnitude + bl.R);
            top = Mathf.Max(top, bl.C.y + bl.Ry);
        }
        float puffMean = (s.PuffMin + s.PuffMax) * 0.5f;
        float shell = puffMean * 0.5f;

        var centers = new List<Vector3>();
        var radii = new List<float>();
        int tries = s.Puffs * 400;
        for (int t = 0; t < tries && centers.Count < s.Puffs; t++)
        {
            var p = new Vector3((R() * 2f - 1f) * xzMax, R() * top, (R() * 2f - 1f) * xzMax);
            float dep = Depth(p);
            if (dep <= 0f || dep > shell) { continue; }
            float r = Mathf.Lerp(s.PuffMin, s.PuffMax, Mathf.Pow(R(), 0.7f)) * Mathf.Lerp(0.8f, 1.2f, p.y / top);
            bool crowded = false;
            for (int k = 0; k < centers.Count; k++)
            {
                if ((centers[k] - p).sqrMagnitude < Sq((r + radii[k]) * 0.4f)) { crowded = true; break; }
            }
            if (crowded) { continue; }
            centers.Add(p);
            radii.Add(r);
        }

        var verts = new List<Vector3>(centers.Count * 4);
        var normals = new List<Vector3>(centers.Count * 4);
        var colors = new List<Color>(centers.Count * 4);
        var uv0 = new List<Vector2>(centers.Count * 4);
        var uv1 = new List<Vector4>(centers.Count * 4);
        var tris = new List<int>(centers.Count * 6);
        Vector2[] corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        Vector3 min = Vector3.one * 1e9f, max = Vector3.one * -1e9f;
        float maxR = 0f;
        const float e = 10f;
        for (int i = 0; i < centers.Count; i++)
        {
            Vector3 p = centers[i];
            var n = new Vector3(Depth(p - Vector3.right * e) - Depth(p + Vector3.right * e),
                Depth(p - Vector3.up * e) - Depth(p + Vector3.up * e),
                Depth(p - Vector3.forward * e) - Depth(p + Vector3.forward * e));
            n = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
            float h = p.y / top;
            float ao = Mathf.Clamp01(0.5f + 0.5f * h) * (1f - 0.25f * Mathf.Clamp01(Depth(p) / shell));
            float bottom = Mathf.Clamp01(1f - h / 0.25f);
            var data = new Vector4(radii[i], R() * Mathf.PI * 2f, R(), Mathf.Floor(R() * 4f));
            int baseIndex = verts.Count;
            for (int c = 0; c < 4; c++)
            {
                verts.Add(p);
                normals.Add(n);
                colors.Add(new Color(ao, bottom, 0f, 1f));
                uv0.Add(corners[c]);
                uv1.Add(data);
            }
            tris.Add(baseIndex); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
            tris.Add(baseIndex); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            maxR = Mathf.Max(maxR, radii[i]);
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetTriangles(tris, 0);
        // 정점이 퍼프 중심에 모여 있으므로 펼친 크기(일렁임·거리 LOD로 커지는 것 포함)만큼 넓힌다
        var bounds = new Bounds((min + max) * 0.5f, max - min);
        bounds.Expand(maxR * 2f * 1.7f);
        mesh.bounds = bounds;

        coreCenter = (min + max) * 0.5f;
        coreExtent = Vector3.Max((max - min) * 0.5f * 0.85f, Vector3.one * 60f);
    }

    private static float Sq(float v) { return v * v; }

    // ───────────── 비 메시 ─────────────

    private static void BuildRain(Mesh mesh)
    {
        var rnd = new System.Random(4242);
        var verts = new List<Vector3>(RainDrops * 4);
        var uv0 = new List<Vector2>(RainDrops * 4);
        var uv1 = new List<Vector2>(RainDrops * 4);
        var tris = new List<int>(RainDrops * 6);
        Vector2[] corners = { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1, 1) };
        for (int i = 0; i < RainDrops; i++)
        {
            var p = new Vector3((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble());
            float r = (float)rnd.NextDouble();
            int b = verts.Count;
            for (int c = 0; c < 4; c++)
            {
                verts.Add(p);
                uv0.Add(corners[c]);
                uv1.Add(new Vector2(r, 0f));
            }
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }
        mesh.indexFormat = IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetTriangles(tris, 0);
        // 셰이더가 카메라 둘레로 옮기므로 잘리지 않게 크게
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
    }

    // ───────────── 머티리얼 / 씬 ─────────────

    private static Material GetOrCreateCloudMaterial()
    {
        Material m = GetOrCreateMaterial(CloudMaterialPath, "ACO/CloudPuffs", "ACO_Clouds");
        if (m == null) { return null; }
        TextureImporter ti = AssetImporter.GetAtPath(PuffTexturePath) as TextureImporter;
        if (ti == null)
        {
            Debug.LogWarning($"[ACO] 구름 텍스처가 없습니다 — python Tools/aco_cloud_tex.py Assets/ACO_Addon/Textures/Clouds 로 만드세요({PuffTexturePath}).");
            return null;
        }
        bool changed = false;
        if (ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
        if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }
        if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
        // 알파 투 커버리지: 밉맵에서 가장자리가 얇아져 멀리서 구름이 사라지지 않게
        if (!ti.mipMapsPreserveCoverage || Mathf.Abs(ti.alphaTestReferenceValue - 0.45f) > 0.001f)
        {
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = 0.45f;
            changed = true;
        }
        if (changed) { ti.SaveAndReimport(); }
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PuffTexturePath));
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material GetOrCreateMaterial(string path, string shaderName, string name)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null) { Debug.LogWarning($"[ACO] 셰이더 {shaderName}를 찾지 못했습니다."); return null; }
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.shader != shader) { m.shader = shader; }
        return m;
    }

    /// 격납고 반사 프로브(조명 설정이 만든 ACO_HangarLighting/ReflectionProbe) 크기로 비 가림 상자를 만든다.
    private static Transform EnsureHangarShelter(Transform sky)
    {
        GameObject hangar = FindRoot("ACO_HangarLighting");
        ReflectionProbe rp = hangar != null ? hangar.GetComponentInChildren<ReflectionProbe>(true) : null;
        if (rp == null) { return null; }
        Transform box = sky.Find("RainShelter_Hangar");
        if (box == null)
        {
            box = new GameObject("RainShelter_Hangar").transform;
            Undo.RegisterCreatedObjectUndo(box.gameObject, "ACO sky");
            box.SetParent(sky, false);
        }
        box.position = rp.transform.position + rp.center;
        box.rotation = Quaternion.identity;
        box.localScale = rp.size;
        return box;
    }

    private static GameObject FindRoot(string name)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) { return root; }
        }
        return null;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) { return; }
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    private static void EditorSceneManagerMarkDirty(Scene scene)
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
