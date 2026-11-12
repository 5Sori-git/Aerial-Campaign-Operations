using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SaccFlightAndVehicles;

/// 루프 경계 격자 벽(6-170): 플레이어 기체의 SAV 루프(RepeatingWorld — 원점에서 한 축이 RepeatingWorldDistance를 넘으면 그 축만 반대편으로) 경계,
/// 곧 ±반거리 정사각형의 네 면에 판을 세운다. 셰이더 ACO/BoundaryWall이 눈에서 가까운 부분만 격자로 그린다(스크립트 · 동기화 없음, 드로우콜 1).
/// 높이는 해수면 아래 조금부터 비행 최고 고도 위까지. 충돌 없음(보이기만 한다).
/// 설치: Setup Check "루프 경계 격자 벽" [고치기] — 반거리가 기체 값과 다르면 다시 만든다. 메시 · 재질은 Generated/.
public static class ACO_BoundaryBuilder
{
    public const string ObjectName = "ACO_BoundaryWall";
    private const string MeshPath = "Assets/ACO_Addon/Generated/ACO_BoundaryWall.asset";
    private const string MaterialPath = "Assets/ACO_Addon/Generated/ACO_BoundaryWall.mat";
    private const float Bottom = -300f;
    private const float Top = 12600f;
    private const float DefaultHalf = 30000f;

    /// 플레이어 기체 SAV 루프 반거리(가장 큰 값). 루프를 쓰는 기체가 없으면 0.
    public static float LoopHalfSize()
    {
        float half = 0f;
        foreach (SaccAirVehicle sav in Object.FindObjectsOfType<SaccAirVehicle>(true))
        {
            if (sav != null && sav.RepeatingWorld) { half = Mathf.Max(half, sav.RepeatingWorldDistance); }
        }
        return half;
    }

    public static GameObject Find()
    {
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == ObjectName) { return root; }
        }
        return null;
    }

    /// 0 = 맞음, 1 = 없음, 2 = 반거리가 기체 루프와 다름.
    public static int Status()
    {
        GameObject go = Find();
        if (go == null) { return 1; }
        MeshFilter mf = go.GetComponent<MeshFilter>();
        float half = LoopHalfSize();
        if (half <= 0f) { half = DefaultHalf; }
        if (mf == null || mf.sharedMesh == null || Mathf.Abs(mf.sharedMesh.bounds.extents.x - half) > 1f) { return 2; }
        return 0;
    }

    [MenuItem("ACO/World/Build Boundary Wall")]
    public static void Build()
    {
        float half = LoopHalfSize();
        if (half <= 0f) { half = DefaultHalf; }
        Mesh mesh = BuildMesh(half);
        Material mat = EnsureMaterial();

        GameObject go = Find();
        if (go == null)
        {
            go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "ACO boundary wall");
        }
        Undo.RecordObject(go.transform, "ACO boundary wall");
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        go.isStatic = false;
        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null) { mf = Undo.AddComponent<MeshFilter>(go); }
        mf.sharedMesh = mesh;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr == null) { mr = Undo.AddComponent<MeshRenderer>(go); }
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.allowOcclusionWhenDynamic = false;
        EditorUtility.SetDirty(mf);
        EditorUtility.SetDirty(mr);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log($"[ACO] 루프 경계 격자 벽: ±{half:0} m 정사각형 네 면(높이 {Bottom:0}~{Top:0} m).", go);
    }

    /// 네 장의 판(노멀 = 바깥). 셰이더는 양면으로 그린다.
    private static Mesh BuildMesh(float h)
    {
        Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        bool create = m == null;
        if (create) { m = new Mesh(); }
        m.Clear();
        m.name = "ACO_BoundaryWall";
        var v = new Vector3[16];
        var n = new Vector3[16];
        var t = new int[24];
        Vector3[] normals = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        for (int s = 0; s < 4; s++)
        {
            Vector3 nn = normals[s];
            Vector3 c = nn * h;
            Vector3 along = new Vector3(nn.z, 0f, -nn.x);   // 판을 따라가는 축
            int b = s * 4;
            v[b + 0] = c - along * h + Vector3.up * Bottom;
            v[b + 1] = c + along * h + Vector3.up * Bottom;
            v[b + 2] = c + along * h + Vector3.up * Top;
            v[b + 3] = c - along * h + Vector3.up * Top;
            for (int k = 0; k < 4; k++) { n[b + k] = nn; }
            int ti = s * 6;
            t[ti + 0] = b; t[ti + 1] = b + 2; t[ti + 2] = b + 1;
            t[ti + 3] = b; t[ti + 4] = b + 3; t[ti + 5] = b + 2;
        }
        m.vertices = v;
        m.normals = n;
        m.triangles = t;
        m.RecalculateBounds();
        if (create) { AssetDatabase.CreateAsset(m, MeshPath); }
        else { EditorUtility.SetDirty(m); }
        return m;
    }

    private static Material EnsureMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("ACO/BoundaryWall")) { name = "ACO_BoundaryWall" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        else if (mat.shader == null || mat.shader.name != "ACO/BoundaryWall")
        {
            mat.shader = Shader.Find("ACO/BoundaryWall");
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }
}
