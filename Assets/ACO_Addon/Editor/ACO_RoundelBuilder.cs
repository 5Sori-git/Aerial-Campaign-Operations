using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ACO;

/// 라운델 판(6-191): 기체 표면에 맞춰 구운 메시 판 4장(고정 자리) — 동체 양옆, 왼쪽 바깥 날개 윗면, 오른쪽 바깥 날개 아랫면(접히는 날개 본에 붙는다).
/// 판은 평판이 아니라 격자(9 × 9)의 각 점을 기체 메시 표면에 쏘아 붙여 곡면을 따라간다(뜨거나 묻히지 않는다 — 셰이더 ACO/MarkImage가 조금 더 당긴다).
/// 메시는 기체 프리팹의 스킨 메시를 쉬는 자세로 구워 CPU로 광선을 쏴서 만든다(프리팹 편집 장면에서도 된다). 같은 모델이면 메시를 같이 쓴다(Generated/Roundel).
/// 기체: ACO_Decals(기체 기준점 자식) 아래 Roundel_*, ACO_VehicleDecal.Roundels에 잇는다. 격납고 샘플: SampleRoot 아래 ACO_Roundels, 스튜디오 PreviewRoundels.
/// 이미지는 실행 중에 데칼처럼 받아 넣는다 — 여기서는 판(꺼진 상태)만 만든다.
public static class ACO_RoundelBuilder
{
    private const string Dir = "Assets/ACO_Addon/Generated/Roundel";
    private const string MaterialPath = "Assets/ACO_Addon/Generated/ACO_Roundel.mat";
    private const int Grid = 9;
    private const float Lift = 0.004f;

    private struct Spot
    {
        public string Name;
        public Vector3 Center;   // 기체 기준점(SaccEntity) 좌표 — 이 근처 표면을 찾는다
        public Vector3 Normal;   // 바깥쪽(표면을 찾는 방향의 반대)
        public Vector3 Up;       // 이미지 위쪽(표면에 눕힌다)
        public float Size;       // 한 변(m)
        public string Bone;      // 붙일 본(접히는 날개 — 함께 움직인다). 비면 기체 데칼 묶음 아래
        public Spot(string n, Vector3 c, Vector3 nrm, Vector3 up, float s, string bone) { Name = n; Center = c; Normal = nrm; Up = up; Size = s; Bone = bone; }
    }

    // FA-18E 기준(렌더로 맞춘 자리): 동체 = 캐노피 아래 앞동체, 기수 헐넘버 뒤 · LEX 앞전 아래(실기 사진처럼 — 사용자 요청, 바뀜: 처음엔 흡입구 덕트 옆),
    // 날개 = 접히는 바깥 날개(Wing.L · Wing.R 본 — 앞전 플랩 · 에일러론 사이 고정 판) 가운데, 앞전 뒤젖힘(약 21°)에 맞춰 비스듬히(사용자 요청).
    // 이미지 위쪽: 동체는 하늘, 날개는 앞전 쪽 — 밖(위·아래·옆)에서 보면 바로 읽힌다(좌우 판도 거울상이 아니다).
    // 자리는 여기서만 정한다 — Install이 판 메시 · 자세를 이 표대로 다시 굽는다.
    private const float WingSweep = 21f;
    private static readonly Spot[] Spots =
    {
        new Spot("FuseL", new Vector3(-0.55f, 2.05f, 4.7f), Vector3.left, Vector3.up, 0.52f, null),
        new Spot("FuseR", new Vector3(0.55f, 2.05f, 4.7f), Vector3.right, Vector3.up, 0.52f, null),
        new Spot("WingTopL", new Vector3(-5.43f, 2.3f, -2.75f), Vector3.up, Quaternion.AngleAxis(-WingSweep, Vector3.up) * Vector3.forward, 1.0f, "Wing.L"),
        new Spot("WingBotR", new Vector3(5.43f, 2.2f, -2.75f), Vector3.down, Quaternion.AngleAxis(WingSweep, Vector3.up) * Vector3.forward, 1.0f, "Wing.R"),
    };

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) { if (t.name == name) { return t; } }
        return null;
    }

    /// 라운델 판이 빠졌거나 연결이 안 된 플레이어 기체 · 격납고 샘플이 있는가(Setup Check).
    public static bool Missing()
    {
        foreach (ACO_VehicleDecal vd in Object.FindObjectsOfType<ACO_VehicleDecal>(true))
        {
            if (vd.Roundels == null || vd.Roundels.Length == 0) { return true; }
        }
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        return studio != null && studio.SampleRoot != null && (studio.PreviewRoundels == null || studio.PreviewRoundels.Length == 0);
    }

    /// roundel-1: 씬 플레이어 기체의 프리팹(또는 씬 기체)과 격납고 샘플에 라운델 판을 넣는다(빠진 것만).
    public static void Install() { Install(false); }

    /// roundel-2: 자리 표(Spots)를 바꾼 뒤 — 있는 판도 다시 굽는다(동체 판을 캐노피 아래로 옮겼다).
    public static void Rebuild() { Install(true); }

    private static void Install(bool rebuild)
    {
        var done = new List<string>();
        foreach (ACO_VehicleDecal vd in Object.FindObjectsOfType<ACO_VehicleDecal>(true))
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(vd.gameObject);
            if (string.IsNullOrEmpty(path))
            {
                if (Ensure(vd, rebuild)) { EditorUtility.SetDirty(vd); }
                continue;
            }
            if (done.Contains(path)) { continue; }
            done.Add(path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (ACO_VehicleDecal p in root.GetComponentsInChildren<ACO_VehicleDecal>(true)) { changed |= Ensure(p, rebuild); }
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[ACO] 라운델 판 4장을 넣었습니다: {path} (ACO_Decals/Roundel_*)");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        EnsurePreview(rebuild);
        AssetDatabase.SaveAssets();
    }

    /// 기체 데칼 하나(ACO_Decals)에 라운델 판을 갖추고 잇는다. 바꿨으면 true.
    public static bool Ensure(ACO_VehicleDecal vd) { return Ensure(vd, false); }

    public static bool Ensure(ACO_VehicleDecal vd, bool rebuild)
    {
        var entity = vd.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
        Transform vehicle = entity != null ? entity.transform : vd.transform.parent;
        if (vehicle == null) { return false; }
        Renderer[] plates = EnsurePlates(vehicle, vehicle, vd.transform, "Roundel_", rebuild);
        if (plates == null) { return false; }
        if (SameArray(vd.Roundels, plates)) { return rebuild; }
        vd.Roundels = plates;
        EditorUtility.SetDirty(vd);
        return true;
    }

    /// 격납고 샘플(SampleRoot — 실기체 기준점과 같은 자리 · 모델)에 같은 판을 둔다.
    public static void EnsurePreview(bool rebuild)
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        if (studio == null || studio.SampleRoot == null) { return; }
        Transform holder = studio.SampleRoot.Find("ACO_Roundels");
        if (holder == null)
        {
            holder = new GameObject("ACO_Roundels").transform;
            Undo.RegisterCreatedObjectUndo(holder.gameObject, "ACO roundels");
            holder.SetParent(studio.SampleRoot, false);
        }
        Renderer[] plates = EnsurePlates(studio.SampleRoot, studio.SampleRoot, holder, "Roundel_", rebuild);
        if (plates == null || SameArray(studio.PreviewRoundels, plates)) { return; }
        Undo.RecordObject(studio, "ACO roundels");
        studio.PreviewRoundels = plates;
        PrefabUtility.RecordPrefabInstancePropertyModifications(studio);
        EditorUtility.SetDirty(studio);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(studio.gameObject.scene);
    }

    private static bool SameArray(Renderer[] a, Renderer[] b)
    {
        if (a == null || a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
    }

    /// 판 4장을 parent 아래에 갖춘다(없으면 만든다 — 메시는 처음 한 번 굽고 같이 쓴다). rebuild면 있는 판도 자리 표대로 다시 굽는다. 기체 메시가 없으면 null.
    private static Renderer[] EnsurePlates(Transform vehicle, Transform frame, Transform parent, string prefix, bool rebuild)
    {
        var list = new List<Renderer>();
        List<Vector3> tris = null;
        Material mat = EnsureMaterial();
        foreach (Spot s in Spots)
        {
            Transform bone = string.IsNullOrEmpty(s.Bone) ? null : FindDeep(vehicle, s.Bone);
            Transform at = bone != null ? bone : parent;
            Transform t = at.Find(prefix + s.Name);
            if (!rebuild && t != null && t.GetComponent<MeshRenderer>() != null && t.GetComponent<MeshFilter>() != null && t.GetComponent<MeshFilter>().sharedMesh != null)
            {
                list.Add(t.GetComponent<MeshRenderer>());
                continue;
            }
            if (tris == null)
            {
                tris = SurfaceTriangles(vehicle, frame);
                if (tris.Count == 0) { Debug.LogWarning($"[ACO] 라운델: '{vehicle.name}'에서 스킨 메시를 찾지 못했습니다."); return null; }
            }
            Pose pose;
            Mesh mesh = BuildMesh(tris, s, out pose);
            if (mesh == null) { Debug.LogWarning($"[ACO] 라운델: '{vehicle.name}'의 {s.Name} 자리에서 표면을 찾지 못했습니다."); continue; }
            if (t == null)
            {
                t = new GameObject(prefix + s.Name).transform;
                Undo.RegisterCreatedObjectUndo(t.gameObject, "ACO roundels");
                t.SetParent(at, false);
            }
            // pose는 frame(기체 기준점) 좌표 — parent가 기준점과 다른 자리여도 맞게
            Vector3 world = frame.TransformPoint(pose.position);
            Quaternion rot = frame.rotation * pose.rotation;
            t.SetPositionAndRotation(world, rot);
            t.localScale = Vector3.one;
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf == null) { mf = t.gameObject.AddComponent<MeshFilter>(); }   // Unity 가짜 null — ??를 쓰지 않는다
            MeshRenderer mr = t.GetComponent<MeshRenderer>();
            if (mr == null) { mr = t.gameObject.AddComponent<MeshRenderer>(); }
            mf.sharedMesh = mesh;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.enabled = false;   // 이미지를 받으면 켠다
            list.Add(mr);
        }
        return list.ToArray();
    }

    private static Material EnsureMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (m != null) { return m; }
        Shader sh = Shader.Find("ACO/MarkImage");
        m = new Material(sh) { name = "ACO_Roundel" };
        AssetDatabase.CreateAsset(m, MaterialPath);
        return m;
    }

    /// 기체의 스킨 메시(쉬는 자세)를 frame 좌표 삼각형 목록으로(3개씩). 무장 외형 · 조종석 안 메시는 뺀다.
    internal static List<Vector3> SurfaceTriangles(Transform vehicle, Transform frame)
    {
        var tris = new List<Vector3>();
        foreach (SkinnedMeshRenderer smr in vehicle.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null || !smr.enabled || !smr.gameObject.activeInHierarchy) { continue; }   // 꺼 둔 F형 부품 제외(6-343)
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            // BakeMesh(useScale)는 크기를 정점에 넣는다 — 위치 · 회전만 곱한다
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            Matrix4x4 toFrame = frame.worldToLocalMatrix * toWorld;
            Vector3[] v = baked.vertices;
            int[] idx = baked.triangles;
            for (int i = 0; i < idx.Length; i += 3)
            {
                tris.Add(toFrame.MultiplyPoint3x4(v[idx[i]]));
                tris.Add(toFrame.MultiplyPoint3x4(v[idx[i + 1]]));
                tris.Add(toFrame.MultiplyPoint3x4(v[idx[i + 2]]));
            }
            Object.DestroyImmediate(baked);
        }
        return tris;
    }

    /// 가장 가까운 교점(o + d * t, 0 < t < maxT). 없으면 false.
    internal static bool Ray(List<Vector3> tris, List<int> near, Vector3 o, Vector3 d, float maxT, out Vector3 hit, out Vector3 normal)
    {
        float best = maxT;
        hit = Vector3.zero;
        normal = Vector3.up;
        bool found = false;
        foreach (int i in near)
        {
            Vector3 a = tris[i], b = tris[i + 1], c = tris[i + 2];
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-8f) { continue; }
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) { continue; }
            Vector3 q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(d, q) * inv;
            if (w < 0f || u + w > 1f) { continue; }
            float t = Vector3.Dot(e2, q) * inv;
            if (t <= 0f || t >= best) { continue; }
            best = t;
            hit = o + d * t;
            normal = Vector3.Cross(e1, e2).normalized;
            if (Vector3.Dot(normal, d) > 0f) { normal = -normal; }   // 광선 쪽(바깥)을 보게
            found = true;
        }
        return found;
    }

    /// 자리 하나의 판 메시(판 좌표)와 판의 자세(frame 좌표).
    private static Mesh BuildMesh(List<Vector3> tris, Spot s, out Pose pose)
    {
        pose = new Pose(s.Center, Quaternion.identity);
        // 이 자리 근처 삼각형만
        float r = s.Size + 1.6f;
        var near = new List<int>();
        for (int i = 0; i < tris.Count; i += 3)
        {
            Vector3 m = (tris[i] + tris[i + 1] + tris[i + 2]) / 3f;
            if ((m - s.Center).sqrMagnitude < r * r) { near.Add(i); }
        }
        Vector3 hit, n;
        if (!Ray(tris, near, s.Center + s.Normal * 1.5f, -s.Normal, 3f, out hit, out n)) { return null; }
        Vector3 up = Vector3.ProjectOnPlane(s.Up, n).normalized;
        Vector3 right = Vector3.Cross(up, -n);
        pose = new Pose(hit, Quaternion.LookRotation(-n, up));
        Quaternion inv = Quaternion.Inverse(pose.rotation);
        var verts = new Vector3[Grid * Grid];
        var uv = new Vector2[Grid * Grid];
        for (int j = 0; j < Grid; j++)
        {
            for (int i = 0; i < Grid; i++)
            {
                float u = i / (float)(Grid - 1), v = j / (float)(Grid - 1);
                Vector3 p = hit + right * ((u - 0.5f) * s.Size) + up * ((v - 0.5f) * s.Size);
                Vector3 h2, n2;
                Vector3 q = Ray(tris, near, p + n * 0.5f, -n, 1.2f, out h2, out n2) ? h2 + n * Lift : p + n * Lift;
                verts[j * Grid + i] = inv * (q - hit);
                uv[j * Grid + i] = new Vector2(u, v);
            }
        }
        var idx = new List<int>();
        for (int j = 0; j < Grid - 1; j++)
        {
            for (int i = 0; i < Grid - 1; i++)
            {
                int a = j * Grid + i, b = a + 1, c = a + Grid, d = c + 1;
                idx.AddRange(new[] { a, c, b, b, c, d });
            }
        }
        var mesh = new Mesh { name = "ACO_Roundel_" + s.Name };
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.triangles = idx.ToArray();
        mesh.RecalculateNormals();
        // 판의 앞면(바깥, 판 좌표 -Z)을 보게
        if (mesh.normals[Grid * Grid / 2].z > 0f)
        {
            for (int k = 0; k < idx.Count; k += 3) { int t = idx[k + 1]; idx[k + 1] = idx[k + 2]; idx[k + 2] = t; }
            mesh.triangles = idx.ToArray();
            mesh.RecalculateNormals();
        }
        mesh.RecalculateBounds();
        return SaveMesh(mesh);
    }

    private static Mesh SaveMesh(Mesh mesh)
    {
        if (!AssetDatabase.IsValidFolder(Dir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "Roundel"); }
        string path = $"{Dir}/{mesh.name}.asset";
        Mesh old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, old);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(old);
        return old;
    }
}
