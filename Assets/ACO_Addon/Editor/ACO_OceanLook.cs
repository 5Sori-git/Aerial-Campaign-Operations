using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// 바다 머티리얼(셰이더 ACO/Ocean + Textures/Water — Tools/aco_water_tex.py가 만든다).
/// 적용: 씬에서 물 레이어(4)에 있는 큰 평면(가로 5km 이상) 렌더러에 머티리얼을 넣는다(콜라이더·레이어는 그대로 — SAV 착수/추락 판정 유지).
/// 셰이더가 월드 좌표로 무늬를 깔아서 평면의 크기·UV와 상관없다. 평면이 작아 수평선 앞에서 끊기면 크기를 키우라고 알린다.
/// 가까운 너울: 촘촘한 원판 메시 오브젝트(ACO_OceanSwell, 충돌 없음)를 씬에 두고 너울 머티리얼(바다 머티리얼 복사 + Mesh 역할)을 넣는다 —
/// 셰이더가 원판을 카메라 쪽으로 옮겨 반경 안에서 출렁이고, 바다 평면(Hole 역할)은 그 원 안을 비운다. 값은 바다 머티리얼에서 바꾸고 다시 적용하면 너울에 복사된다.
public static class ACO_OceanLook
{
    public const string SwellMaterialPath = "Assets/ACO_Addon/Generated/ACO_OceanSwell.mat";
    public const string SwellMeshPath = "Assets/ACO_Addon/Generated/ACO_OceanSwellDisk.asset";
    public const string SwellObjectName = "ACO_OceanSwell";
    // 원판: 고리 수 x 둘레 칸 수(정점 약 1.15만). 고리 반지름 = 반경 x (i/고리 수)^RingPower — 가운데가 촘촘(약 1m), 가장자리가 성기다(약 30m)
    private const int SwellRings = 72;
    private const int SwellSegments = 160;
    private const float RingPower = 1.7f;
    public const string TextureDir = "Assets/ACO_Addon/Textures/Water";
    public const string MaterialPath = "Assets/ACO_Addon/Generated/ACO_Ocean.mat";
    private const string ShaderName = "ACO/Ocean";
    private const int WaterLayer = 4;
    private const float MinSeaSize = 5000f;
    /// 이보다 작으면 고공에서 바다 끝이 보인다(지형 반경 20km + 시야).
    private const float RecommendedSeaSize = 120000f;

    public static Material GetOrCreateMaterial()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null) { Debug.LogWarning($"[ACO] 셰이더 {ShaderName}를 찾지 못했습니다(Shaders/ACO_Ocean.shader)."); return null; }
        Texture2D normal = Import($"{TextureDir}/ACO_WaterNormal.png", true);
        Texture2D foam = Import($"{TextureDir}/ACO_WaterFoam.png", false);
        if (normal == null || foam == null)
        {
            Debug.LogWarning($"[ACO] 바다 텍스처가 {TextureDir}에 없습니다 — python Tools/aco_water_tex.py {TextureDir} 로 만드세요.");
            return null;
        }
        Material m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (m == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
            m = new Material(shader) { name = "ACO_Ocean" };
            AssetDatabase.CreateAsset(m, MaterialPath);
        }
        if (m.shader != shader) { m.shader = shader; }
        m.SetTexture("_NormalTex", normal);
        m.SetTexture("_FoamTex", foam);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Texture2D Import(string path, bool isNormal)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { return null; }
        bool changed = false;
        TextureImporterType type = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != type) { ti.textureType = type; changed = true; }
        if (!isNormal && ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
        if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
        if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
        if (ti.anisoLevel < 8) { ti.anisoLevel = 8; changed = true; }   // 수평선 쪽으로 비스듬히 보는 바다가 흐려지지 않게
        if (changed) { ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// 씬의 바다 평면(물 레이어, 큰 렌더러)에 바다 머티리얼을 넣는다.
    [MenuItem("ACO/Terrain/Apply Ocean Material")]
    public static void Apply()
    {
        Material m = GetOrCreateMaterial();
        if (m == null) { return; }
        int applied = 0;
        float seaY = 0f;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsSea(r)) { continue; }
                Undo.RecordObject(r, "ACO ocean");
                r.sharedMaterial = m;
                // 바다는 그림자를 드리울 일이 없다(깊이 텍스처에도 안 들어가게)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                EditorUtility.SetDirty(r);
                applied++;
                seaY = r.bounds.center.y;
                float size = Mathf.Min(r.bounds.size.x, r.bounds.size.z);
                if (size < RecommendedSeaSize)
                {
                    Debug.LogWarning($"[ACO] 바다 '{r.name}'의 크기가 {size / 1000f:0}km입니다 — 고공에서 바다 끝이 보이면 가로·세로를 {RecommendedSeaSize / 1000f:0}km 이상으로 키우세요"
                        + "(카메라 Far Clip도 그만큼). 무늬는 월드 좌표라 크기를 바꿔도 그대로입니다.", r);
                }
            }
        }
        if (applied > 0) { EnsureSwell(m, seaY); }
        AssetDatabase.SaveAssets();
        if (applied == 0) { Debug.LogWarning($"[ACO] 물 레이어(4)에 가로 {MinSeaSize / 1000f:0}km 이상인 바다 평면이 없습니다 — 바다 오브젝트에 {MaterialPath}를 직접 넣으세요.", m); }
        else { Debug.Log($"[ACO] 바다 머티리얼 적용: {applied}개 — 색·물결·거품은 {MaterialPath}에서. 섬 둘레 얕은 물·해안 거품은 'Use depth texture'(그림자를 켠 실시간 방향광 필요).", m); }
    }

    public static bool IsApplied()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsSea(r)) { return r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == ShaderName; }
            }
        }
        return false;
    }

    // ───────────── 가까운 너울 ─────────────

    /// 너울 원판 오브젝트·메시·머티리얼을 만들거나 고치고, 바다 머티리얼을 Hole 역할로 바꾼다.
    private static void EnsureSwell(Material sea, float seaY)
    {
        float radius = sea.GetFloat("_SwellRadius");
        sea.SetFloat("_SeaY", seaY);
        SetMode(sea, "HOLE", 1f);

        Material swell = AssetDatabase.LoadAssetAtPath<Material>(SwellMaterialPath);
        if (swell == null)
        {
            swell = new Material(sea.shader) { name = "ACO_OceanSwell" };
            AssetDatabase.CreateAsset(swell, SwellMaterialPath);
        }
        swell.shader = sea.shader;
        swell.CopyPropertiesFromMaterial(sea);   // 색·물결·거품·너울 값은 바다 머티리얼이 원본
        SetMode(swell, "MESH", 2f);
        EditorUtility.SetDirty(swell);

        Mesh disk = AssetDatabase.LoadAssetAtPath<Mesh>(SwellMeshPath);
        float wantOuter = radius * 1.03f / Mathf.Cos(Mathf.PI / SwellSegments);
        if (disk == null || Mathf.Abs(disk.vertices[disk.vertexCount - 1].magnitude - wantOuter) > 1f)
        {
            if (disk != null) { AssetDatabase.DeleteAsset(SwellMeshPath); }
            disk = BuildDisk(wantOuter);
            AssetDatabase.CreateAsset(disk, SwellMeshPath);
        }

        GameObject go = GameObject.Find(SwellObjectName);
        if (go == null)
        {
            go = new GameObject(SwellObjectName);
            Undo.RegisterCreatedObjectUndo(go, "ACO ocean swell");
        }
        go.layer = WaterLayer;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null) { mf = go.AddComponent<MeshFilter>(); }
        mf.sharedMesh = disk;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr == null) { mr = go.AddComponent<MeshRenderer>(); }
        mr.sharedMaterial = swell;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        EditorUtility.SetDirty(go);
        Debug.Log($"[ACO] 가까운 너울: 반경 {radius:0}m 원판(정점 {disk.vertexCount}) — 카메라 높이 {sea.GetFloat("_SwellFadeHeight"):0}m 위에선 잦아든다. 끄려면 ACO → Terrain → Remove Ocean Swell.", go);
    }

    /// 너울을 끈다: 원판 오브젝트를 지우고 바다 머티리얼을 원 비우기 없이(Off) 되돌린다.
    [MenuItem("ACO/Terrain/Remove Ocean Swell")]
    public static void RemoveSwell()
    {
        GameObject go = GameObject.Find(SwellObjectName);
        if (go != null) { Undo.DestroyObjectImmediate(go); }
        Material sea = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (sea != null) { SetMode(sea, "OFF", 0f); EditorUtility.SetDirty(sea); }
        AssetDatabase.SaveAssets();
        Debug.Log("[ACO] 가까운 너울을 껐습니다(바다는 평평한 노멀 물결만).");
    }

    public static bool HasSwell() { return GameObject.Find(SwellObjectName) != null; }

    private static void SetMode(Material m, string mode, float value)
    {
        m.SetFloat("_SwellMode", value);
        foreach (string k in new[] { "OFF", "HOLE", "MESH" }) { m.DisableKeyword("_SWELLMODE_" + k); }
        m.EnableKeyword("_SWELLMODE_" + mode);
    }

    /// 원판: 가운데 정점 + 고리(반지름 = outer x (i/고리 수)^RingPower). 로컬 XZ = 원 중심에서의 m, 위가 앞면.
    /// 셰이더가 카메라 쪽으로 옮기므로 경계 상자를 아주 크게 둔다(절두체 컬링으로 사라지지 않게).
    private static Mesh BuildDisk(float outer)
    {
        int count = 1 + SwellRings * SwellSegments;
        var v = new Vector3[count];
        var n = new Vector3[count];
        var t = new System.Collections.Generic.List<int>(SwellRings * SwellSegments * 6);
        v[0] = Vector3.zero;
        for (int i = 1; i <= SwellRings; i++)
        {
            float r = outer * Mathf.Pow(i / (float)SwellRings, RingPower);
            for (int s = 0; s < SwellSegments; s++)
            {
                float a = (s + (i % 2) * 0.5f) / SwellSegments * Mathf.PI * 2f;   // 고리마다 반 칸 엇갈려 삼각형 모양을 고르게
                v[1 + (i - 1) * SwellSegments + s] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
        }
        for (int k = 0; k < count; k++) { n[k] = Vector3.up; }
        // 가운데 부채꼴
        for (int s = 0; s < SwellSegments; s++)
        {
            int a = 1 + s, b = 1 + (s + 1) % SwellSegments;
            t.Add(0); t.Add(b); t.Add(a);
        }
        // 고리 사이 띠
        for (int i = 1; i < SwellRings; i++)
        {
            int inner = 1 + (i - 1) * SwellSegments;
            int outerRow = 1 + i * SwellSegments;
            for (int s = 0; s < SwellSegments; s++)
            {
                int s1 = (s + 1) % SwellSegments;
                int a = inner + s, b = inner + s1, c = outerRow + s, d = outerRow + s1;
                t.Add(a); t.Add(b); t.Add(c);
                t.Add(b); t.Add(d); t.Add(c);
            }
        }
        // 위가 앞면이 되게(삼각형마다 확인 — 엇갈린 고리에서도)
        int[] tri = t.ToArray();
        for (int k = 0; k < tri.Length; k += 3)
        {
            Vector3 f = Vector3.Cross(v[tri[k + 1]] - v[tri[k]], v[tri[k + 2]] - v[tri[k]]);
            if (f.y < 0f) { int tmp = tri[k + 1]; tri[k + 1] = tri[k + 2]; tri[k + 2] = tmp; }
        }
        var mesh = new Mesh { name = "ACO_OceanSwellDisk", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = v;
        mesh.normals = n;
        var tangents = new Vector4[count];
        for (int k = 0; k < count; k++) { tangents[k] = new Vector4(1f, 0f, 0f, -1f); }
        mesh.tangents = tangents;
        mesh.triangles = tri;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e5f, 1e6f));
        return mesh;
    }

    private static bool IsSea(MeshRenderer r)
    {
        if (r.gameObject.name == SwellObjectName) { return false; }
        return r.gameObject.layer == WaterLayer && r.bounds.size.x >= MinSeaSize && r.bounds.size.z >= MinSeaSize;
    }
}
