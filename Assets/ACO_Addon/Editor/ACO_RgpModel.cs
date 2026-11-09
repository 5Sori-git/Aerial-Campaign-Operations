using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using ACO;

/// RGP(레일건 포드): 파일런 외형 두 개 — 왼쪽 파일런의 축전기 포드 Models/RGP/ACO_RGP_Cap.fbx, 오른쪽 파일런의 레일건 포드 ACO_RGP_Rail.fbx(Tools/aco_rgp_pod.py).
/// 탄은 초고속 단발 ACO_Missile_RGP(SRAAM 프리팹을 복사해 모델을 빛나는 작은 탄체로 바꾸고 궤적을 빛줄기로).
///  - 포드 외형은 기체 프리팹의 파일런 아래에 Transform 0으로 직접 놓는다(MMP와 같다). 충전 표시창·레일 안쪽 판(_Bar0~5, _Glow)은 늘 켜진 빛나는 재질이다.
///  - 셋업 검사 [고치기]: 레일건 포드 포구에 발사 지점(RGP_Muzzle — 발사음 AudioSource와 포구 섬광 RGP_Flash 포함)을 만들고 그 기체의 RGP 모듈에 넣는다.
///    재질이 비어 있는 포드 렌더러엔 재질을 넣는다. 포구 자리(RgpMuzzle)는 aco_rgp_pod.py가 찍는 MUZZLE 줄과 같아야 한다(파일런 로컬: +y 기수, +z 위).
public static partial class ACO_PrefabBuilder
{
    public const string RgpRailFbx = "Assets/ACO_Addon/Models/RGP/ACO_RGP_Rail.fbx";
    public const string RgpCapFbx = "Assets/ACO_Addon/Models/RGP/ACO_RGP_Cap.fbx";
    private const string RgpRailAlbedo = "Assets/ACO_Addon/Models/RGP/ACO_RGP_Rail_Albedo.png";
    private const string RgpCapAlbedo = "Assets/ACO_Addon/Models/RGP/ACO_RGP_Cap_Albedo.png";
    private const string RgpRailMesh = "ACO_RGP_Rail";
    private const string RgpCapMesh = "ACO_RGP_Cap";
    private const string RgpMuzzleName = "RGP_Muzzle";
    private const string RgpFireClip = "Assets/ACO_Addon/Audio/Generated/ACO_Railgun_MiniFire.wav";
    public const string RgpId = "RGP";
    private static readonly Vector3 RgpMuzzle = new Vector3(0f, 2.805f, -0.625f);
    private static readonly Color RgpGlowColor = new Color(0.62f, 0.88f, 1f, 1f);

    // ───────────── 탄 ─────────────

    /// RGP 탄: SRAAM 프리팹을 복사해 모델을 빛나는 작은 탄체로 바꾸고, 엔진 불꽃·불빛·소리를 끄고, 궤적을 빛줄기로 바꾼다. 늘 SRAAM에서 새로 만든다.
    public static GameObject BuildRgpSlug()
    {
        EnsurePrefabFolder();
        string source = PathOf("ACO_Missile_SRAAM");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
        {
            Debug.LogError($"[ACO] RGP 탄: 원본 {source}가 없습니다.");
            return null;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(source);
        try
        {
            ACO_Missile missile = root.GetComponent<ACO_Missile>();
            if (missile == null)
            {
                Debug.LogError($"[ACO] RGP 탄: {source} 루트에 ACO_Missile이 없습니다.");
                return null;
            }
            root.name = "ACO_Missile_RGP";
            ApplyRgpSlugModel(root, missile);
            missile.LaunchSpeed = 4000f;
            missile.MaxSpeed = 4000f;
            missile.FlyStraightTime = 0.05f;
            EditorUtility.SetDirty(missile);
            ACO_FxBuilder.StyleWeapon(root);
            ACO_FxBuilder.StyleRailSlug(root);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PathOf("ACO_Missile_RGP"));
            Debug.Log($"[ACO] RGP 탄 프리팹 생성 완료: {PathOf("ACO_Missile_RGP")}");
            return prefab;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 탄체: Model(SRAAM 메시)의 렌더러를 끄고 그 아래에 빛나는 쌍뿔 탄체(길이 0.9m)를 탄 축 그대로 둔다. 엔진 불꽃·불빛·엔진 소리는 끈다(궤적 파티클은 둔다).
    private static void ApplyRgpSlugModel(GameObject root, ACO_Missile missile)
    {
        Transform model = root.transform.Find("Model");
        if (model == null) { throw new System.InvalidOperationException("[ACO] RGP 탄: SRAAM 프리팹에 Model이 없습니다."); }
        MeshRenderer old = model.GetComponent<MeshRenderer>();
        if (old != null) { old.enabled = false; }
        Transform slug = model.Find("ACO_RGP_Slug");
        if (slug == null)
        {
            var go = new GameObject("ACO_RGP_Slug");
            go.layer = root.layer;
            go.transform.SetParent(model, false);
            slug = go.transform;
        }
        slug.SetPositionAndRotation(root.transform.position, root.transform.rotation);
        Vector3 ms = model.lossyScale;
        slug.localScale = new Vector3(1f / ms.x, 1f / ms.y, 1f / ms.z);
        MeshFilter mf = slug.GetComponent<MeshFilter>();
        if (mf == null) { mf = slug.gameObject.AddComponent<MeshFilter>(); }
        mf.sharedMesh = RgpSlugMesh();
        MeshRenderer r = slug.GetComponent<MeshRenderer>();
        if (r == null) { r = slug.gameObject.AddComponent<MeshRenderer>(); }
        r.sharedMaterial = RgpGlowMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var keep = new HashSet<ParticleSystem>();
        if (missile.TrailParticles != null) { foreach (ParticleSystem ps in missile.TrailParticles) { if (ps != null) { keep.Add(ps); } } }
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Trail") { t.position = root.transform.TransformPoint(0f, 0f, -0.45f); }
            if (t.name != "Effects") { continue; }
            foreach (Renderer rr in t.GetComponentsInChildren<Renderer>(true))
            {
                var psr = rr as ParticleSystemRenderer;
                if (psr != null && keep.Contains(psr.GetComponent<ParticleSystem>())) { continue; }
                rr.enabled = false;
            }
            foreach (Light l in t.GetComponentsInChildren<Light>(true)) { l.enabled = false; }
            foreach (AudioSource a in t.GetComponentsInChildren<AudioSource>(true)) { a.enabled = false; }
        }
    }

    /// 탄체 메시(Generated/ACO_RGP_Slug.asset): 앞뒤로 뾰족한 육각 쌍뿔, 길이 0.9m, 반지름 0.07m. 없으면 만든다.
    private static Mesh RgpSlugMesh()
    {
        string path = $"{RailgunGeneratedDir}/ACO_RGP_Slug.asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) { return mesh; }
        const int n = 6;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        Vector3 nose = new Vector3(0f, 0f, 0.45f);
        Vector3 tail = new Vector3(0f, 0f, -0.45f);
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n;
            float a1 = (i + 1) * Mathf.PI * 2f / n;
            Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.07f, Mathf.Sin(a0) * 0.07f, 0.1f);
            Vector3 p1 = new Vector3(Mathf.Cos(a1) * 0.07f, Mathf.Sin(a1) * 0.07f, 0.1f);
            int b = verts.Count;
            verts.Add(nose); verts.Add(p1); verts.Add(p0);
            verts.Add(tail); verts.Add(p0); verts.Add(p1);
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b + 3); tris.Add(b + 4); tris.Add(b + 5);
        }
        mesh = new Mesh { name = "ACO_RGP_Slug" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // ───────────── 재질 ─────────────

    /// 빛나는 재질(Generated/ACO_RGP_Glow.mat): 탄체, 충전 표시창, 레일 안쪽 판.
    private static Material RgpGlowMaterial()
    {
        string path = $"{RailgunGeneratedDir}/ACO_RGP_Glow.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_RGP_Glow" };
            m.SetColor("_Color", RgpGlowColor);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Glossiness", 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", RgpGlowColor * 2.5f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    private static Material RgpPodMaterial(string name, string albedo)
    {
        var importer = AssetImporter.GetAtPath(albedo) as TextureImporter;
        if (importer != null && importer.maxTextureSize != 4096)
        {
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }
        string path = $"{RailgunGeneratedDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = name };
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Metallic", 0.15f);
            m.SetFloat("_Glossiness", 0.5f);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.GetTexture("_MainTex") == null)
        {
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            EditorUtility.SetDirty(m);
        }
        return m;
    }

    /// 메시 이름으로 포드 렌더러의 재질. RGP 메시가 아니면 null.
    private static Material RgpMaterialFor(string mesh)
    {
        if (mesh == null || !mesh.StartsWith("ACO_RGP_")) { return null; }
        if (mesh.Contains("_Glow") || mesh.Contains("_Bar")) { return RgpGlowMaterial(); }
        if (mesh == RgpRailMesh) { return RgpPodMaterial("ACO_RGP_Rail", RgpRailAlbedo); }
        if (mesh == RgpCapMesh) { return RgpPodMaterial("ACO_RGP_Cap", RgpCapAlbedo); }
        return null;
    }

    // ───────────── 발사 지점 (셋업 검사) ─────────────

    private struct RgpSite
    {
        public ACO_DFUNC_Missile Module;
        public SaccEntity Entity;
        public List<MeshFilter> Rails;
        public List<MeshFilter> Parts;   // 두 포드의 모든 메시(몸통·빛나는 부분)
    }

    private static List<RgpSite> FindRgpSites()
    {
        return FindRgpSites(SceneManager.GetActiveScene().GetRootGameObjects());
    }

    private static List<RgpSite> FindRgpSites(GameObject[] roots)
    {
        var sites = new List<RgpSite>();
        foreach (GameObject root in roots)
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                int k = special.ModuleIds != null ? System.Array.IndexOf(special.ModuleIds, RgpId) : -1;
                if (k < 0 || special.Modules == null || k >= special.Modules.Length) { continue; }
                var module = special.Modules[k] as ACO_DFUNC_Missile;
                SaccEntity entity = special.GetComponentInParent<SaccEntity>(true);
                if (module == null || entity == null) { continue; }
                var rails = new List<MeshFilter>();
                var parts = new List<MeshFilter>();
                foreach (MeshFilter mf in entity.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.name.StartsWith("ACO_RGP_")) { continue; }
                    parts.Add(mf);
                    if (mf.sharedMesh.name == RgpRailMesh) { rails.Add(mf); }
                }
                sites.Add(new RgpSite { Module = module, Entity = entity, Rails = rails, Parts = parts });
            }
        }
        return sites;
    }

    /// 레일건 포드 메시가 aco_rgp_pod.py 좌표 그대로 들어왔는가(포구 끝 y 2.805, 바닥 z -0.85).
    private static bool RgpRailAxesOk(MeshFilter pod)
    {
        Bounds b = pod.sharedMesh.bounds;
        return Mathf.Abs(b.max.y - 2.805f) < 0.05f && Mathf.Abs(b.min.z + 0.85f) < 0.03f;
    }

    private static bool RgpSiteOk(RgpSite s)
    {
        if (s.Rails.Count == 0) { return true; }
        Transform muzzle = s.Rails[0].transform.Find(RgpMuzzleName);
        if (muzzle == null || s.Module.LaunchFlash == null || s.Module.LaunchSound == null) { return false; }
        if (s.Module.LaunchPoints == null || s.Module.LaunchPoints.Length != 1 || s.Module.LaunchPoints[0] != muzzle) { return false; }
        foreach (MeshFilter mf in s.Parts)
        {
            MeshRenderer r = mf.GetComponent<MeshRenderer>();
            if (r != null && r.sharedMaterial == null) { return false; }
        }
        return true;
    }

    /// 셋업 검사: 포구 발사 지점·재질을 고쳐야 하는 RGP 모듈 수(레일건 포드가 있는 기체만).
    public static int RgpLaunchPointsMissing()
    {
        int n = 0;
        foreach (RgpSite s in FindRgpSites()) { if (!RgpSiteOk(s)) { n++; } }
        return n;
    }

    /// 셋업 검사용: RGP 모듈이 있는데 레일건 포드 외형이 없는 기체 이름(쉼표로). 없으면 빈 문자열.
    public static string RgpVehiclesWithoutPods()
    {
        var names = new List<string>();
        foreach (RgpSite s in FindRgpSites()) { if (s.Rails.Count == 0) { names.Add(s.Entity.name); } }
        return string.Join(", ", names);
    }

    /// [고치기]: 레일건 포드 포구에 발사 지점(발사음·섬광 포함)을 만들고 모듈에 넣는다. 재질이 빈 포드 렌더러엔 재질을 넣는다.
    /// 기체가 프리팹이면 기체 프리팹 파일을 고친다. 프리팹이 아닌 씬 기체만 씬에서 고친다.
    public static void InstallRgpLaunchPoints()
    {
        ConfigureModelImporter(RgpRailFbx);
        ConfigureModelImporter(RgpCapFbx);
        var prefabs = new List<string>();
        var sceneSites = new List<RgpSite>();
        foreach (RgpSite s in FindRgpSites())
        {
            if (s.Parts.Count == 0) { continue; }
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(s.Entity.gameObject);
            if (string.IsNullOrEmpty(path)) { sceneSites.Add(s); }
            else if (!prefabs.Contains(path)) { prefabs.Add(path); }
        }
        foreach (string path in prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (FixRgpSites(FindRgpSites(new[] { root }), false) > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[ACO] RGP 포구 발사 지점을 기체 프리팹에 넣었습니다: {path}");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        if (FixRgpSites(sceneSites, true) > 0) { EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); }
    }

    private static int FixRgpSites(List<RgpSite> sites, bool inScene)
    {
        int fixedCount = 0;
        foreach (RgpSite s in sites)
        {
            bool changed = false;
            foreach (MeshFilter mf in s.Parts)
            {
                MeshRenderer r = mf.GetComponent<MeshRenderer>();
                if (r == null || r.sharedMaterial != null) { continue; }
                Material mat = RgpMaterialFor(mf.sharedMesh.name);
                if (mat == null) { continue; }
                if (inScene) { Undo.RecordObject(r, "ACO RGP material"); }
                r.sharedMaterial = mat;
                if (inScene) { PrefabUtility.RecordPrefabInstancePropertyModifications(r); }
                changed = true;
            }
            MeshFilter rail = null;
            foreach (MeshFilter mf in s.Rails)
            {
                if (RgpRailAxesOk(mf)) { rail = mf; break; }
                Bounds b = mf.sharedMesh.bounds;
                Debug.LogWarning($"[ACO] '{s.Entity.name}/{mf.name}': 레일건 포드 메시의 축이나 크기가 예상과 다릅니다(범위 {b.min} ~ {b.max}) — 발사 지점을 만들지 않았습니다. "
                    + "Tools/aco_rgp_pod.py로 다시 만들고 임포트 Scale Factor 1인지 보세요.", mf);
            }
            if (rail != null)
            {
                Transform muzzle = rail.transform.Find(RgpMuzzleName);
                if (muzzle == null)
                {
                    var go = new GameObject(RgpMuzzleName);
                    go.layer = rail.gameObject.layer;
                    go.transform.SetParent(rail.transform, false);
                    if (inScene) { Undo.RegisterCreatedObjectUndo(go, "ACO RGP muzzle"); }
                    muzzle = go.transform;
                    changed = true;
                }
                if (inScene) { Undo.RecordObject(muzzle, "ACO RGP muzzle"); }
                muzzle.localPosition = RgpMuzzle;
                muzzle.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);   // 앞 = 포드 +y(기수), 위 = 포드 +z
                muzzle.localScale = Vector3.one;

                AudioSource audio = muzzle.GetComponent<AudioSource>();
                if (audio == null)
                {
                    audio = inScene ? Undo.AddComponent<AudioSource>(muzzle.gameObject) : muzzle.gameObject.AddComponent<AudioSource>();
                    changed = true;
                }
                audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(RgpFireClip);
                audio.playOnAwake = false;
                audio.loop = false;
                audio.spatialBlend = 1f;
                audio.rolloffMode = AudioRolloffMode.Logarithmic;
                audio.minDistance = 40f;
                audio.maxDistance = 4000f;
                audio.dopplerLevel = 0f;
                audio.volume = 1f;

                ParticleSystem flash = ACO_FxBuilder.BuildRailMuzzleFlash(muzzle);
                if (s.Module.LaunchPoints == null || s.Module.LaunchPoints.Length != 1 || s.Module.LaunchPoints[0] != muzzle
                    || s.Module.LaunchSound != audio || s.Module.LaunchFlash != flash)
                {
                    if (inScene) { Undo.RecordObject(s.Module, "ACO RGP launch point"); }
                    s.Module.LaunchPoints = new[] { muzzle };
                    s.Module.LaunchSound = audio;
                    s.Module.LaunchFlash = flash;
                    if (inScene) { PrefabUtility.RecordPrefabInstancePropertyModifications(s.Module); }
                    EditorUtility.SetDirty(s.Module);
                    changed = true;
                }
            }
            if (!changed) { continue; }
            fixedCount++;
            Debug.Log($"[ACO] '{s.Entity.name}': RGP 포구 발사 지점·재질을 넣었습니다.");
        }
        return fixedCount;
    }
}
