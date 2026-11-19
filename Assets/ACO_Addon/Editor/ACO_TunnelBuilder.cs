using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ACO;

/// 터널 랜드마크 프리팹(6-266): Tools/aco_tunnel_model.py가 만든 Models/Tunnel/ACO_Tunnel.fbx(메시 이름 = "ACO_Tunnel__재질")로
/// 재질마다 자식 렌더러 하나인 프리팹(Prefabs/ACO_Tunnel)을 만든다.
///  - rock(바위산 겉면): 지형 머티리얼(육각 기둥과 같은 ACO/HexTerrain — 월드 좌표로 색을 정해 기둥과 이어 보인다), 충돌 MeshCollider.
///  - concrete(터널 안쪽 · 포털): 회색 콘크리트, 충돌 MeshCollider. asphalt(바닥 도로)·glow(유도등 — 자체 발광): 충돌 없음, 그림자 없음.
///  - 레이어 Default(지형 기둥과 같다 — 기체가 닿으면 지형 충돌).
///  - LOD: 가까이는 전부, 멀리서는 산 겉면만(작은 부품은 그리지 않는다 — 멀리 있는 유닛·지형 저폴리 원칙).
/// 원점 = 칸 윗면 가운데, 터널 축 = +Z. ACO_TerrainFeatures가 같은 높이 3칸 일렬의 가운데 칸에 일렬 방향으로 놓는다.
public static class ACO_TunnelBuilder
{
    public const string Fbx = "Assets/ACO_Addon/Models/Tunnel/ACO_Tunnel.fbx";
    public const string PrefabPath = "Assets/ACO_Addon/Prefabs/ACO_Tunnel.prefab";
    private const string MatDir = "Assets/ACO_Addon/Generated/Tunnel";
    /// 프리팹 이름: 직선 1 · 2 · 3칸, 곡선(한쪽 활 — 3판) 2 · 3칸. 1칸은 처음 만든 이름 그대로(씬 참조 유지).
    public static readonly string[] Names = { "ACO_Tunnel", "ACO_Tunnel2", "ACO_Tunnel3", "ACO_Tunnel2C", "ACO_Tunnel3C" };

    /// 1칸 터널 프리팹.
    public static GameObject Load() { return Load(1); }
    /// cells칸(1 · 2 · 3) 직선 터널 프리팹. 없으면 null.
    public static GameObject Load(int cells) { return Load(cells, false); }
    /// cells칸 터널 프리팹(curved = 곡선형 — 2 · 3칸만). 없으면 null.
    public static GameObject Load(int cells, bool curved)
    {
        if (cells < 1 || cells > 3 || (curved && cells < 2)) { return null; }
        string name = curved ? Names[cells + 1] : Names[cells - 1];
        return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ACO_Addon/Prefabs/{name}.prefab");
    }

    /// 1칸 프리팹을 (다시) 만든다.
    public static GameObject Build() { return Build(Names[0]); }

    /// 프리팹을 모두 (다시) 만든다. Names 순서.
    public static GameObject[] BuildAll()
    {
        var list = new GameObject[Names.Length];
        for (int i = 0; i < Names.Length; i++) { list[i] = Build(Names[i]); }
        return list;
    }

    /// 프리팹 하나를 (다시) 만든다. 경로가 같아 GUID가 유지된다.
    public static GameObject Build(string name)
    {
        string Prefix = name + "__";
        string path = $"Assets/ACO_Addon/Prefabs/{name}.prefab";
        ACO_PrefabBuilder.ConfigureModelImporter(Fbx);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        if (model == null) { throw new InvalidOperationException($"[ACO] {Fbx}가 없습니다. Tools/aco_tunnel_model.py를 Blender로 실행하세요."); }
        Material terrain = TerrainMaterial();
        var root = new GameObject(name);
        try
        {
            var all = new List<Renderer>();
            var far = new List<Renderer>();
            foreach (Transform part in model.transform)
            {
                if (!part.name.StartsWith(Prefix, StringComparison.Ordinal)) { continue; }
                MeshFilter src = part.GetComponent<MeshFilter>();
                if (src == null || src.sharedMesh == null) { continue; }
                string mat = part.name.Substring(Prefix.Length);
                var go = new GameObject(mat);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat == "rock" && terrain != null ? terrain : PartMaterial(mat);
                bool solid = mat == "rock" || mat == "concrete";
                if (solid) { go.AddComponent<MeshCollider>().sharedMesh = src.sharedMesh; }
                else { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                all.Add(r);
                if (mat == "rock") { far.Add(r); }
            }
            if (all.Count == 0) { throw new InvalidOperationException($"[ACO] {Fbx}에 {Prefix}* 메시가 없습니다."); }
            LODGroup lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.03f, all.ToArray()), new LOD(0f, far.ToArray()) });
            lod.RecalculateBounds();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[ACO] 터널 랜드마크 프리팹을 만들었습니다: {path} (부품 {all.Count}개, 지형 머티리얼 {(terrain != null ? terrain.name : "없음 — 회색")})");
            return prefab;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    /// 지형 기둥의 머티리얼(씬의 육각 지형 ColumnPrefab).
    private static Material TerrainMaterial()
    {
        ACO_HexTerrain hex = UnityEngine.Object.FindObjectOfType<ACO_HexTerrain>(true);
        if (hex != null && hex.ColumnPrefab != null)
        {
            MeshRenderer r = hex.ColumnPrefab.GetComponentInChildren<MeshRenderer>(true);
            if (r != null && r.sharedMaterial != null) { return r.sharedMaterial; }
        }
        foreach (string guid in AssetDatabase.FindAssets("ACO_HexTerrainNatural t:Material"))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null) { return m; }
        }
        return null;
    }

    /// 터널 도로 텍스처(6-308): 가로 = 도로 폭 49.8m(u 0 ~ 1), 세로 = 20m 한 장(v 반복 — 모델 스크립트 LANE_PERIOD).
    /// 가운데 노란 겹선, 방향마다 4차로(차로 폭 4m — 흰 점선 10m 칠 · 10m 빈칸), 바깥 흰 실선(가운데에서 16.5m), 그 밖은 갓길. 아스팔트는 옅은 얼룩.
    public const float RoadWidth = 49.8f;
    public const float LanePeriod = 20f;
    private static Texture2D RoadTexture()
    {
        string path = $"{MatDir}/ACO_Tunnel_road.png";
        const int W = 2048, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        var px = new Color[W * H];
        Color asphalt = new Color(0.13f, 0.13f, 0.14f);
        Color white = new Color(0.82f, 0.82f, 0.8f);
        Color yellow = new Color(0.85f, 0.62f, 0.08f);
        float mPerU = RoadWidth / W, mPerV = LanePeriod / H;
        var rnd = new System.Random(6308);
        for (int y = 0; y < H; y++)
        {
            float along = y * mPerV;   // 0 ~ 20m
            bool dashOn = along < 10f;
            for (int x = 0; x < W; x++)
            {
                float lat = (x + 0.5f) * mPerU - RoadWidth * 0.5f;   // 가운데 0
                float a = Mathf.Abs(lat);
                // 아스팔트 얼룩(저주파 + 알갱이)
                float n = Mathf.PerlinNoise(x * 0.004f, y * 0.03f) * 0.05f - 0.025f + (float)(rnd.NextDouble() - 0.5) * 0.02f;
                Color c = new Color(asphalt.r + n, asphalt.g + n, asphalt.b + n);
                if (a >= 0.15f && a <= 0.35f) { c = yellow; }                                   // 가운데 노란 겹선(선 폭 20cm, 사이 30cm)
                else if (a >= 16.4f && a <= 16.6f) { c = white; }                               // 바깥 흰 실선
                else if (dashOn && (Mathf.Abs(a - 4.2f) <= 0.08f || Mathf.Abs(a - 8.2f) <= 0.08f || Mathf.Abs(a - 12.2f) <= 0.08f)) { c = white; }   // 차로 점선
                px[y * W + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapModeU = TextureWrapMode.Clamp;
        imp.wrapModeV = TextureWrapMode.Repeat;
        imp.anisoLevel = 16;   // 길게 뻗은 도로를 낮은 각도로 본다
        imp.mipmapEnabled = true;
        imp.maxTextureSize = 2048;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// 터널 안쪽 텍스처(6-310): 가로 u = 둘레(왼쪽 벽 아래 → 천장 → 오른쪽 벽 아래, 약 108.5m), 세로 v = 라이닝 한 칸 12m(모델 스크립트 RING).
    /// v 경계(0 · 1)에 둥근 고리 줄눈(어두운 홈 + 한쪽 밝은 모서리), 콘크리트 얼룩(저주파), 천장에서 벽으로 흘러내린 물자국 몇 줄, 벽 아래 1.5m 때.
    /// 대비는 낮게(멀리서 줄이 촘촘해 어지럽지 않게).
    private static Texture2D LiningTexture()
    {
        string path = $"{MatDir}/ACO_Tunnel_lining.png";
        const int W = 2048, H = 512;
        const float Per = 108.54f, Ring = 12f;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        var px = new Color[W * H];
        Color baseC = new Color(0.47f, 0.47f, 0.46f);
        var rnd = new System.Random(6310);
        // 물자국: (가로 위치 m, 세로 위치 v, 길이 m, 폭 m, 진하기) — 천장에서 양쪽 벽으로
        int streaks = 14;
        var sx = new float[streaks]; var sv = new float[streaks]; var sl = new float[streaks]; var sw = new float[streaks]; var sa = new float[streaks];
        for (int k = 0; k < streaks; k++)
        {
            bool left = k % 2 == 0;
            sx[k] = left ? 15f + 30f + (float)rnd.NextDouble() * 10f : 15f + 78.5f - 30f - (float)rnd.NextDouble() * 10f;   // 천장 어깨쯤에서 시작
            sv[k] = (float)rnd.NextDouble();
            sl[k] = 18f + (float)rnd.NextDouble() * 25f;
            sw[k] = 0.25f + (float)rnd.NextDouble() * 0.5f;
            sa[k] = 0.03f + (float)rnd.NextDouble() * 0.04f;
        }
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H;
            float alongM = v * Ring;
            for (int x = 0; x < W; x++)
            {
                float s = (x + 0.5f) / W * Per;
                float n = Mathf.PerlinNoise(x * 0.006f, y * 0.012f) * 0.07f + Mathf.PerlinNoise(x * 0.05f, y * 0.1f) * 0.025f - 0.05f + (float)(rnd.NextDouble() - 0.5) * 0.015f;
                float c = n;
                // 고리 줄눈(이음매 — v 0 · 1 경계, 폭 약 5cm 어둡게 + 바로 옆 3cm 밝게)
                float dj = Mathf.Min(alongM, Ring - alongM);
                if (dj < 0.05f) { c -= 0.13f; }
                else if (alongM > Ring - 0.08f) { c += 0.04f; }
                // 물자국: 천장 어깨에서 벽 쪽으로(왼쪽은 s가 줄어드는 쪽, 오른쪽은 늘어나는 쪽)
                for (int k = 0; k < streaks; k++)
                {
                    float dv = Mathf.Abs(alongM - sv[k] * Ring);
                    dv = Mathf.Min(dv, Ring - dv);
                    if (dv > sw[k]) { continue; }
                    bool left = k % 2 == 0;
                    float t = left ? sx[k] - s : s - sx[k];
                    if (t < 0f || t > sl[k]) { continue; }
                    c -= sa[k] * (1f - dv / sw[k]) * (1f - t / sl[k] * 0.6f);
                }
                // 벽 아래 1.5m 때
                float floorD = Mathf.Min(s, Per - s);
                if (floorD < 1.5f) { c -= 0.06f * (1f - floorD / 1.5f); }
                px[y * W + x] = new Color(baseC.r + c, baseC.g + c, baseC.b + c);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapModeU = TextureWrapMode.Clamp;
        imp.wrapModeV = TextureWrapMode.Repeat;
        imp.anisoLevel = 16;
        imp.mipmapEnabled = true;
        imp.maxTextureSize = 2048;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material PartMaterial(string name)
    {
        if (!AssetDatabase.IsValidFolder(MatDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "Tunnel"); }
        string path = $"{MatDir}/ACO_Tunnel_{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        if (name == "glow")
        {
            // 유도등: 어두운 터널에서도 벽이 보이게(그림자 거리 150m 안에선 천장 그림자로 어둡다)
            m.color = new Color(0.2f, 0.15f, 0.05f, 1f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.3f, 1f) * 2.2f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetFloat("_Glossiness", 0.1f);
        }
        else if (name == "asphalt")
        {
            // 차선(6-308): 색은 텍스처에(아스팔트 + 차선), 머티리얼 색은 흰색
            m.color = Color.white;
            m.mainTexture = RoadTexture();
            m.SetFloat("_Glossiness", 0.2f);
        }
        else
        {
            // 터널 안쪽 · 포털(6-310): 색은 텍스처에(콘크리트 + 라이닝 이음매 · 얼룩)
            m.color = Color.white;
            m.mainTexture = LiningTexture();
            m.SetFloat("_Glossiness", 0.12f);
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }
}
