using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using ACO;

/// MMP(마이크로미사일 포드): 파일런 외형 Models/MMP/ACO_MMP.fbx(Tools/aco_mmp_pod.py가 만든다)와 작은 미사일 탄체 ACO_Missile_MMP
/// (맥주캔 같은 원통 Models/MMP/ACO_MMP_Missile.fbx — Tools/aco_mmp_missile.py, 지름 9.5cm·길이 26cm, 노즐 끝 z -0.13).
///  - 포드 외형은 기체 프리팹의 파일런 아래에 Transform 0으로 직접 놓는다(SCM과 같다). 재질은 ACO_MMP.mat.
///  - 셋업 검사 [고치기]: 포드마다 아랫면 셀 8개 자리에 발사 지점(MMP_Launch_0~7)을 만들고, 그 기체의 MMP 모듈 LaunchPoints에 포드를 번갈아 넣는다.
///    셀 자리(MmpLaunch)는 aco_mmp_pod.py가 찍는 LP 줄과 같아야 한다(파일런 로컬: +y 기수, +z 위).
public static partial class ACO_PrefabBuilder
{
    public const string MmpPodFbx = "Assets/ACO_Addon/Models/MMP/ACO_MMP.fbx";
    private const string MmpAlbedo = "Assets/ACO_Addon/Models/MMP/ACO_MMP_Albedo.png";
    private const string MmpMeshName = "ACO_MMP";
    private const string MmpLaunchPrefix = "MMP_Launch_";
    public const string MmpId = "MMP";
    private const string MmpMissileFbx = "Assets/ACO_Addon/Models/MMP/ACO_MMP_Missile.fbx";
    private const string MmpMissileAlbedo = "Assets/ACO_Addon/Models/MMP/ACO_MMP_Missile_Albedo.png";
    private const string MmpMissileName = "ACO_MMP_Missile";
    private const float MmpNozzleZ = -0.135f;    // 노즐 끝(탄체 로컬) — 엔진 불꽃·궤적 자리
    private const float MmpFlameScale = 0.35f;  // SRAAM 엔진 불꽃 대비

    // aco_mmp_pod.py의 LP 0~7(앞줄부터 오른쪽·왼쪽 번갈아). 발사 지점의 앞 = 기수, 위 = 위 — 사출 방향은 모듈의 EjectDirection(아래·조금 앞)
    private static readonly Vector3[] MmpLaunch =
    {
        new Vector3(0.064f, 0.58f, -0.77f), new Vector3(-0.064f, 0.58f, -0.77f),
        new Vector3(0.064f, 0.40f, -0.77f), new Vector3(-0.064f, 0.40f, -0.77f),
        new Vector3(0.064f, 0.22f, -0.77f), new Vector3(-0.064f, 0.22f, -0.77f),
        new Vector3(0.064f, 0.04f, -0.77f), new Vector3(-0.064f, 0.04f, -0.77f),
    };

    /// MMP 탄체: SRAAM 프리팹을 복사해 모델을 맥주캔형 탄체로 바꾸고(SRAAM 메시는 끈다), 엔진 불꽃·궤적을 캔 꼬리 노즐로 옮긴다.
    /// 궤적 폭·폭발 크기는 이름(MMP)으로 ACO_FxBuilder가 작게 정한다. 늘 SRAAM에서 새로 만든다(여러 번 불러도 같다).
    public static GameObject BuildMmpMissile()
    {
        EnsurePrefabFolder();
        string source = PathOf("ACO_Missile_SRAAM");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
        {
            Debug.LogError($"[ACO] MMP 탄체: 원본 {source}가 없습니다.");
            return null;
        }
        MmpMaterial();
        GameObject root = PrefabUtility.LoadPrefabContents(source);
        try
        {
            if (root.GetComponent<ACO_Missile>() == null)
            {
                Debug.LogError($"[ACO] MMP 탄체: {source} 루트에 ACO_Missile이 없습니다.");
                return null;
            }
            root.name = "ACO_Missile_MMP";
            ApplyMmpMissileModel(root);
            ApplyMmpWander(root.GetComponent<ACO_Missile>());
            ACO_FxBuilder.StyleWeapon(root);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PathOf("ACO_Missile_MMP"));
            Debug.Log($"[ACO] MMP 탄체 프리팹 생성 완료: {PathOf("ACO_Missile_MMP")}");
            return prefab;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 탄체 모델: Model(SRAAM 메시)의 렌더러를 끄고 그 아래에 캔 메시를 탄체 축 그대로(Model의 회전·크기를 되돌려) 둔다 — 터질 때 Model과 함께 꺼진다.
    /// 엔진 불꽃(Effects — 불꽃 메시와 불빛)과 궤적(Trail)은 노즐 끝으로 옮기고 불꽃 메시는 줄인다.
    private static void ApplyMmpMissileModel(GameObject root)
    {
        ConfigureModelImporter(MmpMissileFbx);
        Mesh mesh = FbxMesh(MmpMissileFbx, MmpMissileName);
        Bounds b = mesh.bounds;
        if (Mathf.Abs(b.min.z + 0.13f) > 0.01f || Mathf.Abs(b.max.z - 0.127f) > 0.01f || b.size.x > 0.13f)
        {
            throw new System.InvalidOperationException($"[ACO] ACO_MMP_Missile.fbx의 축이나 크기가 예상과 다릅니다(범위 {b.min} ~ {b.max}). 탄체 프리팹은 만들지 않았습니다.");
        }
        Transform model = root.transform.Find("Model");
        if (model == null) { throw new System.InvalidOperationException("[ACO] MMP 탄체: SRAAM 프리팹에 Model이 없습니다."); }
        MeshRenderer old = model.GetComponent<MeshRenderer>();
        if (old != null) { old.enabled = false; }
        Transform can = model.Find(MmpMissileName);
        if (can == null)
        {
            var go = new GameObject(MmpMissileName);
            go.layer = root.layer;
            go.transform.SetParent(model, false);
            can = go.transform;
        }
        // 탄체(루트) 축 그대로: Model의 자세를 되돌린다
        can.SetPositionAndRotation(root.transform.position, root.transform.rotation);
        Vector3 ms = model.lossyScale;
        can.localScale = new Vector3(1f / ms.x, 1f / ms.y, 1f / ms.z);
        MeshFilter mf = can.GetComponent<MeshFilter>();
        if (mf == null) { mf = can.gameObject.AddComponent<MeshFilter>(); }
        mf.sharedMesh = mesh;
        MeshRenderer r = can.GetComponent<MeshRenderer>();
        if (r == null) { r = can.gameObject.AddComponent<MeshRenderer>(); }
        r.sharedMaterial = MmpMissileMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector3 nozzle = root.transform.TransformPoint(0f, 0f, MmpNozzleZ);
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Effects" || t.name == "Trail") { t.position = nozzle; }
            if (t.name == "EngineFire")
            {
                t.localPosition *= MmpFlameScale;
                t.localScale *= MmpFlameScale;
            }
        }
    }

    private static Material MmpMissileMaterial()
    {
        string path = $"{RailgunGeneratedDir}/ACO_MMP_Missile.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_MMP_Missile" };
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Metallic", 0.2f);
            m.SetFloat("_Glossiness", 0.55f);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.GetTexture("_MainTex") == null)
        {
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(MmpMissileAlbedo));
            EditorUtility.SetDirty(m);
        }
        return m;
    }

    /// MMP 비틀거림: 노리는 방향을 표적 쪽에서 최대 30도 흔들고(0.9Hz — 선회 140으로 쫓는다), 표적 400m 안에서 줄여 200m부터 곧게.
    private static void ApplyMmpWander(ACO_Missile m)
    {
        if (m == null) { return; }
        m.WanderAngle = 30f;
        m.WanderFrequency = 0.9f;
        m.WanderFadeDistance = 400f;
    }

    /// Setup Check(mmp-wander-2): 이미 만든 MMP 탄체에 비틀거림 값만 넣는다(다시 만들지 않는다).
    public static void ApplyMmpWanderToPrefab()
    {
        string path = PathOf("ACO_Missile_MMP");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_Missile m = root.GetComponent<ACO_Missile>();
            if (m == null) { return; }
            ApplyMmpWander(m);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] MMP 탄체: 비틀거림(노리는 방향 최대 30도, 0.9Hz, 표적 400m 안에서 줄어듦)을 넣었습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// Setup Check(mmp-missile-model-1): 이미 만든 MMP 탄체를 캔형 모델로 다시 만든다. 없으면 아무것도 안 한다(카탈로그가 처음 만들 때 캔형으로 만든다).
    public static void RebuildMmpMissile()
    {
        if (!Exists("ACO_Missile_MMP")) { return; }
        BuildMmpMissile();
    }

    /// 포드 외형 재질(Generated/ACO_MMP.mat). 표기가 작아 텍스처는 4096 그대로.
    public static Material MmpMaterial()
    {
        var importer = AssetImporter.GetAtPath(MmpAlbedo) as TextureImporter;
        if (importer != null && importer.maxTextureSize != 4096)
        {
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }
        string path = $"{RailgunGeneratedDir}/ACO_MMP.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_MMP" };
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Metallic", 0.15f);
            m.SetFloat("_Glossiness", 0.5f);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.GetTexture("_MainTex") == null)
        {
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(MmpAlbedo));
            EditorUtility.SetDirty(m);
        }
        return m;
    }

    // ───────────── 발사 지점 (셋업 검사) ─────────────

    /// 씬의 MMP 모듈과 그 기체의 포드들.
    private struct MmpSite
    {
        public ACO_DFUNC_Missile Module;
        public SaccEntity Entity;
        public List<MeshFilter> Pods;
    }

    private static List<MmpSite> FindMmpSites()
    {
        return FindMmpSites(SceneManager.GetActiveScene().GetRootGameObjects());
    }

    private static List<MmpSite> FindMmpSites(GameObject[] roots)
    {
        var sites = new List<MmpSite>();
        foreach (GameObject root in roots)
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                int k = special.ModuleIds != null ? System.Array.IndexOf(special.ModuleIds, MmpId) : -1;
                if (k < 0 || special.Modules == null || k >= special.Modules.Length) { continue; }
                var module = special.Modules[k] as ACO_DFUNC_Missile;
                SaccEntity entity = special.GetComponentInParent<SaccEntity>(true);
                if (module == null || entity == null) { continue; }
                var pods = new List<MeshFilter>();
                foreach (MeshFilter mf in entity.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh != null && mf.sharedMesh.name == MmpMeshName) { pods.Add(mf); }
                }
                sites.Add(new MmpSite { Module = module, Entity = entity, Pods = pods });
            }
        }
        return sites;
    }

    /// 포드 메시가 aco_mmp_pod.py 좌표 그대로 들어왔는가(길이 2.0m, 기수 끝 y 1.26, 바닥 z -0.77).
    private static bool MmpPodAxesOk(MeshFilter pod)
    {
        Bounds b = pod.sharedMesh.bounds;
        return Mathf.Abs(b.max.y - 1.26f) < 0.05f && Mathf.Abs(b.min.y + 0.70f) < 0.05f && Mathf.Abs(b.min.z + 0.767f) < 0.03f;
    }

    /// 포드를 번갈아(포드0 셀0, 포드1 셀0, 포드0 셀1 …) — 연사가 양쪽 날개에서 번갈아 나간다. 발사 지점이 없으면 null.
    private static Transform[] MmpLaunchOrder(List<MeshFilter> pods)
    {
        var list = new List<Transform>();
        for (int i = 0; i < MmpLaunch.Length; i++)
        {
            foreach (MeshFilter pod in pods)
            {
                Transform t = pod.transform.Find(MmpLaunchPrefix + i);
                if (t == null) { return null; }
                list.Add(t);
            }
        }
        return list.ToArray();
    }

    /// 셋업 검사: 발사 지점을 고쳐야 하는 MMP 모듈 수(포드가 있는데 발사 지점이 없거나 모듈에 안 들어간 것). 포드가 없는 기체는 세지 않는다(날개 파일런에서 쏜다).
    public static int MmpLaunchPointsMissing()
    {
        int n = 0;
        foreach (MmpSite s in FindMmpSites())
        {
            if (s.Pods.Count == 0) { continue; }
            Transform[] want = MmpLaunchOrder(s.Pods);
            if (want == null || !SameTransforms(s.Module.LaunchPoints, want)) { n++; }
        }
        return n;
    }

    /// 셋업 검사용: MMP 모듈이 있는데 포드 외형이 없는 기체 이름(쉼표로). 없으면 빈 문자열.
    public static string MmpVehiclesWithoutPods()
    {
        var names = new List<string>();
        foreach (MmpSite s in FindMmpSites()) { if (s.Pods.Count == 0) { names.Add(s.Entity.name); } }
        return string.Join(", ", names);
    }

    /// [고치기]: 포드마다 셀 발사 지점을 만들고(있으면 자리만 맞춘다) 모듈 LaunchPoints에 넣는다. 재질이 없는 포드엔 ACO_MMP.mat을 넣는다.
    /// 기체가 프리팹이면 기체 프리팹 파일을 고친다(씬의 기체는 프리팹을 따라간다). 프리팹이 아닌 씬 기체만 씬에서 고친다.
    public static void InstallMmpLaunchPoints()
    {
        ConfigureModelImporter(MmpPodFbx);
        Material mat = MmpMaterial();
        var prefabs = new List<string>();
        var sceneSites = new List<MmpSite>();
        foreach (MmpSite s in FindMmpSites())
        {
            if (s.Pods.Count == 0) { continue; }
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(s.Entity.gameObject);
            if (string.IsNullOrEmpty(path)) { sceneSites.Add(s); }
            else if (!prefabs.Contains(path)) { prefabs.Add(path); }
        }
        foreach (string path in prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (FixMmpSites(FindMmpSites(new[] { root }), mat, false) > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[ACO] MMP 발사 지점을 기체 프리팹에 넣었습니다: {path}");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        if (FixMmpSites(sceneSites, mat, true) > 0) { EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); }
    }

    /// 사이트마다 발사 지점을 만들고 모듈에 넣는다. 고친 모듈 수. inScene이면 Undo·프리팹 인스턴스 기록을 남긴다(프리팹 파일 안에서는 저장이 기록이다).
    private static int FixMmpSites(List<MmpSite> sites, Material mat, bool inScene)
    {
        int fixedCount = 0;
        foreach (MmpSite s in sites)
        {
            if (s.Pods.Count == 0) { continue; }
            var pods = new List<MeshFilter>();
            foreach (MeshFilter pod in s.Pods)
            {
                if (!MmpPodAxesOk(pod))
                {
                    Bounds b = pod.sharedMesh.bounds;
                    Debug.LogWarning($"[ACO] '{s.Entity.name}/{pod.name}': MMP 포드 메시의 축이나 크기가 예상과 다릅니다(범위 {b.min} ~ {b.max}) — 발사 지점을 만들지 않았습니다. "
                        + "Tools/aco_mmp_pod.py로 다시 만들고 임포트 Scale Factor 1인지 보세요.", pod);
                    continue;
                }
                pods.Add(pod);
                for (int i = 0; i < MmpLaunch.Length; i++)
                {
                    Transform t = pod.transform.Find(MmpLaunchPrefix + i);
                    if (t == null)
                    {
                        var go = new GameObject(MmpLaunchPrefix + i);
                        go.layer = pod.gameObject.layer;
                        go.transform.SetParent(pod.transform, false);
                        if (inScene) { Undo.RegisterCreatedObjectUndo(go, "ACO MMP launch point"); }
                        t = go.transform;
                    }
                    if (inScene) { Undo.RecordObject(t, "ACO MMP launch point"); }
                    t.localPosition = MmpLaunch[i];
                    t.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);   // 앞 = 포드 +y(기수), 위 = 포드 +z
                    t.localScale = Vector3.one;
                }
                MeshRenderer r = pod.GetComponent<MeshRenderer>();
                if (r != null && r.sharedMaterial == null)
                {
                    if (inScene) { Undo.RecordObject(r, "ACO MMP material"); }
                    r.sharedMaterial = mat;
                    if (inScene) { PrefabUtility.RecordPrefabInstancePropertyModifications(r); }
                }
            }
            if (pods.Count == 0) { continue; }
            Transform[] order = MmpLaunchOrder(pods);
            if (order == null || SameTransforms(s.Module.LaunchPoints, order)) { continue; }
            if (inScene) { Undo.RecordObject(s.Module, "ACO MMP launch points"); }
            s.Module.LaunchPoints = order;
            if (inScene) { PrefabUtility.RecordPrefabInstancePropertyModifications(s.Module); }
            EditorUtility.SetDirty(s.Module);
            fixedCount++;
            Debug.Log($"[ACO] '{s.Entity.name}': MMP 발사 지점 {order.Length}개(포드 {pods.Count}개)를 넣었습니다.");
        }
        return fixedCount;
    }

    private static bool SameTransforms(Transform[] a, Transform[] b)
    {
        if (a == null || a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
    }
}
