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
            m.color = new Color(0.13f, 0.13f, 0.14f, 1f);
            m.SetFloat("_Glossiness", 0.2f);
        }
        else
        {
            m.color = new Color(0.46f, 0.46f, 0.47f, 1f);
            m.SetFloat("_Glossiness", 0.12f);
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }
}
