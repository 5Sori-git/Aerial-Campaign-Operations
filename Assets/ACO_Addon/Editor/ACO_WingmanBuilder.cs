using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using ACO;

/// 아군 편대(6-273 — 지원 호출 WINGMEN) 프리팹과 풀.
///  - NPC_Wingman.prefab: NPC_Fighter를 복사해 아군으로 바꾼다 — 노리는 대상은 적 공중(29) · 지상(30), 락온 마커는 아군 공중(25 — 적이 노리고 플레이어 미사일은 잠그지 않는다),
///    명중 판정 · 점수는 오너(부른 사람), 플레이어 기총은 맞지 않는다(SaccTarget.DisableBulletHitEvent), 회청색 도색, ACO_Wingman.
///    처음에 NPC_Fighter를 복사하고, 다시 만들면 그 프리팹에 설정만 다시 넣는다(모델을 NPC_Fighter에 맞추려면 NPC_Wingman을 직접 지우고 다시 만든다).
///  - 풀: 지원 관리자 아래 Wingmen에 WingmanPool기(두 기씩 한 편대). Setup Check가 없으면 만든다.
public static partial class ACO_PrefabBuilder
{
    public const string WingmanPrefab = "NPC_Wingman";
    public const int WingmanPool = 14;   // 7편대 — 맨 끝 두 편대는 브레이킹 포인트(6-333) 전용(10 → 12 → 14, 2026-11-20 — 사용자 "풀을 늘려두면 되지 않을까").   // 5편대 — 플레이어 기체 4대가 모두 동시에 부르고 CIC 지역 편대(6-275)도 뜰 수 있게(8 → 10, 2026-11-18)
    private const int LayerAirFriendly = 25;
    private static readonly Color WingmanTint = new Color(0.62f, 0.72f, 0.9f);

    [MenuItem("ACO/Build Wingman Prefab (6-273)")]
    public static GameObject BuildWingmanPrefab()
    {
        string src = PathOf("NPC_Fighter");
        string dst = PathOf(WingmanPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null) { Debug.LogError("[ACO] 아군 편대: NPC_Fighter 프리팹이 없습니다."); return null; }
        // 처음에만 NPC_Fighter를 복사한다. 이미 있으면 그 프리팹에 설정만 다시 넣는다(GUID 유지 — 씬 풀 인스턴스가 끊기지 않게)
        if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) == null && !AssetDatabase.CopyAsset(src, dst)) { Debug.LogError("[ACO] 아군 편대: 프리팹 복사 실패"); return null; }

        GameObject root = PrefabUtility.LoadPrefabContents(dst);
        try
        {
            root.name = WingmanPrefab;
            ACO_NPCFighterAI ai = root.GetComponent<ACO_NPCFighterAI>();
            SaccEntity entity = root.GetComponent<SaccEntity>();
            SaccTarget health = root.GetComponent<SaccTarget>();
            ACO_NPCFighterNet net = root.GetComponent<ACO_NPCFighterNet>();
            ACO_NPCFlightController flight = root.GetComponent<ACO_NPCFlightController>();

            int targets = (1 << LayerEnemyAir) | (1 << LayerEnemyGround);
            entity.AAMTargetsLayer = targets;   // 빌드 때 SAV가 적 공중 · 지상 마커로 AAMTargets를 굽는다
            ai.TargetLayers = targets;
            ai.AirTargetBonus = 3000f;           // 공중 먼저 — 3km 안쪽 적 전투기는 어떤 지상 표적보다 앞선다
            ai.AirPriorityLayers = 1 << LayerEnemyAir;
            ai.SkipCityVehicles = true;
            ai.MarkerLabel = "WING";
            ai.DetectRange = 3000f;
            ai.LoseTargetRange = 4500f;
            ai.Director = null;
            ai.GroundAttacker = false;
            ai.Drone = false;
            ai.Ace = null;
            ai.BaseHealth = 400f;
            health.Health = 400f;
            health.DisableBulletHitEvent = true;   // 플레이어 기총(SAV 파티클)은 맞지 않는다
            if (net) { net.HandoffToEngagedPlayer = false; }   // 부른 사람이 계속 계산한다
            if (ai.Gun) { ai.Gun.CreditOwner = true; }
            if (ai.MissileLauncher)
            {
                GameObject missile = BuildWingmanMissile();
                if (missile != null) { ai.MissileLauncher.AAM = missile; }
                ai.MissileLauncher.CreditOwner = true;
                ai.MissileLauncher.SendLockWarning = false;   // 적 표적에 플레이어용 락온 경고를 보내지 않는다
            }
            // 플레어(2026-11-17 — 사용자 "윙맨에 플레어좀 붙여줘라", 확률 60%): 적 전투기는 Setup Check가 씬에서 붙이지만 아군 편대 풀은 그 검사 밖이라 프리팹에 넣는다.
            // 아군 표시(무전이 아군 플레어 대사), 출격마다 6회(AI 스폰 때 다시 채움), 항상 베테랑이라 확률은 VeteranFlareChance — 셋 다 0.6으로 둔다
            ACO_FlareDispenser flares = root.GetComponent<ACO_FlareDispenser>();
            if (flares == null) { flares = root.AddUdonSharpComponent<ACO_FlareDispenser>(); }
            flares.TargetHealth = health;
            flares.Flight = flight;
            flares.Friendly = true;
            flares.RadioCallout = false;   // 아군 플레어 대사는 호위 수송기 목소리 · TRANSPORT 화자라 윙맨은 말하지 않는다(6-289, 사용자 "지금은 윙맨 대사를 빼자")
            flares.FlareSalvos = 6;
            if (flares.FlareParticles == null)
            {
                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.name.ToLowerInvariant().Contains("flare")) { flares.FlareParticles = ps; break; }
                }
            }
            // 나오는 자리: Jet_02(길이 약 20.6m, 기수 +Z) 꼬리 쪽 배 아래 — 예전 상자 모델 자리(z −2.8)는 동체 가운데였다
            if (flares.FlareParticles != null && flares.FlareParticles.transform.parent == root.transform) { flares.FlareParticles.transform.localPosition = new Vector3(0f, -1.0f, -7.5f); }
            ai.Flares = flares;
            ai.RookieFlareChance = 0.6f;
            ai.VeteranFlareChance = 0.6f;
            ai.AceFlareChance = 0.6f;

            // 락온 마커: 적 표적 층(29 · 30) → 아군 공중(25). 격추되면 꺼서 적이 잔해를 계속 노리지 않게 한다(HideOnDeath)
            int moved = 0;
            var hide = new List<GameObject>();
            if (ai.HideOnDeath != null) { foreach (GameObject g in ai.HideOnDeath) { if (g != null && !hide.Contains(g)) { hide.Add(g); } } }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer == LayerEnemyAir || t.gameObject.layer == LayerEnemyGround) { t.gameObject.layer = LayerAirFriendly; moved++; }
                if (t != root.transform && t.gameObject.layer == LayerAirFriendly && !hide.Contains(t.gameObject)) { hide.Add(t.gameObject); }
            }
            ai.HideOnDeath = hide.ToArray();

            ACO_Wingman wing = root.GetComponent<ACO_Wingman>();
            if (wing == null) { wing = root.AddUdonSharpComponent<ACO_Wingman>(); }
            wing.AI = ai;
            wing.Flight = flight;
            var explode = new List<UdonSharp.UdonSharpBehaviour>();
            if (health.ExplodeOther != null) { foreach (UdonSharp.UdonSharpBehaviour b in health.ExplodeOther) { if (b != null && b != wing) { explode.Add(b); } } }
            explode.Add(wing);
            health.ExplodeOther = explode.ToArray();

            int jet = ApplyWingmanJet(root.transform.Find("Model"));
            TintWingman(root.transform.Find("Model"));
            if (jet > 0) { Debug.Log($"[ACO] 아군 편대: Jet_02 모델 {jet}곳에 도장 메시 · 재질(ACO_WingmanJet)을 넣었습니다."); }
            PrefabUtility.SaveAsPrefabAsset(root, dst);
            Debug.Log($"[ACO] 아군 편대 프리팹 {dst}: 대상 적 공중 · 지상, 마커 {moved}개 → 아군 공중(25), 회청색 도색. Setup Check로 풀을 만들고 Bake 하세요.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(dst);
    }

    public const string WingmanMissilePrefab = "ACO_Missile_Wingman";

    /// 아군 편대 전용 미사일: 적 미사일(ACO_Missile_AAM)의 프리팹 변형 — 겉모습 · 소리는 원본을 따르고 성능만 플레이어 기본 MSL(강화 전) 수준.
    /// 피해 100 · 선회 60°/초 · 최고 속도 900 · 근접신관 9m. 플레어에는 그대로 속는다. 이미 있으면 값만 다시 넣는다(GUID 유지).
    public static GameObject BuildWingmanMissile()
    {
        EnsurePrefabFolder();
        string dst = $"{PrefabDir}/{WingmanMissilePrefab}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) == null)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/ACO_Missile_AAM.prefab");
            if (source == null) { Debug.LogError("[ACO] 아군 편대 미사일: ACO_Missile_AAM 프리팹이 없습니다."); return null; }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = WingmanMissilePrefab;
                PrefabUtility.SaveAsPrefabAsset(instance, dst);
            }
            finally { Object.DestroyImmediate(instance); }
        }
        GameObject root = PrefabUtility.LoadPrefabContents(dst);
        try
        {
            ACO_Missile m = root.GetComponent<ACO_Missile>();
            if (m == null) { Debug.LogError("[ACO] 아군 편대 미사일: ACO_Missile이 없습니다."); return null; }
            m.Damage = 100f;
            m.TurnRate = 60f;
            m.MaxSpeed = 900f;
            m.ProximityRadius = 9f;
            m.SendMissileIncoming = false;   // 표적이 NPC뿐 — 플레이어 경고 이벤트가 필요 없다
            PrefabUtility.SaveAsPrefabAsset(root, dst);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(dst);
    }

    // ───────────── Jet_02 도장(UV를 다시 편 메시 + 직접 그린 텍스처 — Tools/aco_wingman_jet.py) ─────────────
    private const string WingmanJetDir = "Assets/ACO_Addon/Models/Wingman";
    private const string WingmanJetJson = WingmanJetDir + "/ACO_WingmanJet_Mesh.json";
    private const string WingmanJetTex = WingmanJetDir + "/ACO_WingmanJet_Albedo.png";
    private const string WingmanJetMesh = WingmanJetDir + "/ACO_WingmanJet.asset";
    private const string WingmanJetMat = WingmanJetDir + "/ACO_Wingman_Jet.mat";   // 이름이 ACO_Wingman_으로 시작 — 회청색 곱하기를 건너뛴다(색은 텍스처에 칠했다)

    [System.Serializable]
    private class WingmanJetData { public float[] positions; public float[] normals; public float[] uvs; public int[] triangles; }

    /// Blender가 쓴 JSON(FBX 노드 로컬 좌표) → Unity 메시. Unity가 FBX를 가져올 때와 같게 x를 뒤집고 0.1배(파일 0.01 × 가져오기 10),
    /// 거울상이라 삼각형 감는 방향도 뒤집는다. 원본 Jet_02_Body 꼭짓점과 오차 0으로 맞춘 변환이다. 이미 있으면 같은 에셋에 다시 쓴다(GUID 유지).
    public static Mesh BuildWingmanJetMesh()
    {
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(WingmanJetJson);
        if (ta == null) { return null; }
        var d = JsonUtility.FromJson<WingmanJetData>(ta.text);
        int n = d.positions.Length / 3;
        var v = new Vector3[n];
        var nr = new Vector3[n];
        var uv = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            v[i] = new Vector3(-d.positions[i * 3], d.positions[i * 3 + 1], d.positions[i * 3 + 2]) * 0.1f;
            nr[i] = new Vector3(-d.normals[i * 3], d.normals[i * 3 + 1], d.normals[i * 3 + 2]).normalized;
            uv[i] = new Vector2(d.uvs[i * 2], d.uvs[i * 2 + 1]);
        }
        var t = new int[d.triangles.Length];
        for (int i = 0; i < t.Length; i += 3) { t[i] = d.triangles[i]; t[i + 1] = d.triangles[i + 2]; t[i + 2] = d.triangles[i + 1]; }
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WingmanJetMesh);
        bool created = mesh == null;
        if (created) { mesh = new Mesh(); }
        mesh.Clear();
        mesh.name = "ACO_WingmanJet";
        mesh.vertices = v;
        mesh.normals = nr;
        mesh.uv = uv;
        mesh.triangles = t;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        if (created) { AssetDatabase.CreateAsset(mesh, WingmanJetMesh); }
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    public static Material BuildWingmanJetMaterial()
    {
        var ti = AssetImporter.GetAtPath(WingmanJetTex) as TextureImporter;
        if (ti == null) { return null; }
        if (ti.maxTextureSize != 2048 || ti.anisoLevel != 4 || !ti.sRGBTexture)
        {
            ti.maxTextureSize = 2048;
            ti.anisoLevel = 4;
            ti.sRGBTexture = true;
            ti.SaveAndReimport();
        }
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(WingmanJetMat);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard")) { name = "ACO_Wingman_Jet" };
            AssetDatabase.CreateAsset(mat, WingmanJetMat);
        }
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(WingmanJetTex);
        mat.color = Color.white;
        mat.SetFloat("_Glossiness", 0.38f);
        mat.SetFloat("_Metallic", 0.12f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// 모델 아래 Jet_02_Body(원본 메시 또는 이미 바꾼 도장 메시)를 도장 메시 · 재질로 바꾼다. 바꾼 곳 수(Jet_02가 아니면 0).
    private static int ApplyWingmanJet(Transform model)
    {
        if (model == null) { return 0; }
        var targets = new List<MeshFilter>();
        foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh != null && (mf.sharedMesh.name == "Jet_02_Body" || mf.sharedMesh.name == "ACO_WingmanJet")) { targets.Add(mf); }
        }
        if (targets.Count == 0) { return 0; }
        Mesh mesh = BuildWingmanJetMesh();
        Material mat = BuildWingmanJetMaterial();
        if (mesh == null || mat == null) { Debug.LogWarning("[ACO] 아군 편대: Models/Wingman의 도장 메시(JSON) · 텍스처가 없습니다 — Tools/aco_wingman_jet.py를 먼저 돌리세요."); return 0; }
        foreach (MeshFilter mf in targets)
        {
            mf.sharedMesh = mesh;
            var r = mf.GetComponent<MeshRenderer>();
            if (r) { r.sharedMaterials = new[] { mat }; }
        }
        return targets.Count;
    }

    /// 모델 재질을 회청색으로 곱한 복사본으로 바꾼다(원본 재질은 그대로 — 적 전투기와 같은 텍스처, 색만 다르게).
    private static void TintWingman(Transform model)
    {
        if (model == null) { return; }
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
        var made = new Dictionary<Material, Material>();
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) { continue; }
                if (m.name.StartsWith("ACO_Wingman_")) { continue; }   // 다시 만들 때 이미 칠한 재질을 또 칠하지 않는다
                if (!made.TryGetValue(m, out Material tinted))
                {
                    string path = $"Assets/ACO_Addon/Generated/ACO_Wingman_{m.name}.mat";
                    tinted = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (tinted == null)
                    {
                        tinted = new Material(m) { name = $"ACO_Wingman_{m.name}" };
                        AssetDatabase.CreateAsset(tinted, path);
                    }
                    if (tinted.HasProperty("_Color")) { tinted.color = m.HasProperty("_Color") ? m.color * WingmanTint : WingmanTint; }
                    EditorUtility.SetDirty(tinted);
                    made[m] = tinted;
                }
                mats[i] = tinted;
                changed = true;
            }
            if (changed) { r.sharedMaterials = mats; }
        }
    }

    /// 지원 관리자 아래 아군 편대 풀(WingmanPool기). 이미 있고 빈 칸이 없으면 그대로.
    public static bool EnsureWingmenPool(ACO_SupportManager m)
    {
        if (m == null) { return false; }
        if (m.Wingmen != null && m.Wingmen.Length >= WingmanPool && !HasNull(m.Wingmen)) { return false; }
        if (!Exists(WingmanPrefab)) { BuildWingmanPrefab(); }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(WingmanPrefab));
        if (prefab == null) { return false; }
        Undo.RecordObject(m, "ACO wingmen pool");
        Transform old = m.transform.Find("Wingmen");
        if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); }
        var pool = new GameObject("Wingmen").transform;
        pool.SetParent(m.transform, false);
        m.Wingmen = new ACO_Wingman[WingmanPool];
        for (int i = 0; i < WingmanPool; i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pool);
            go.name = $"Wingman_{i}";
            go.transform.localPosition = new Vector3(i * 30f, 0f, 0f);
            m.Wingmen[i] = go.GetComponent<ACO_Wingman>();
            m.Wingmen[i].Manager = m;
            go.SetActive(false);
        }
        Undo.RegisterCreatedObjectUndo(pool.gameObject, "ACO wingmen pool");
        PrefabUtility.RecordPrefabInstancePropertyModifications(m);
        EditorUtility.SetDirty(m);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 아군 편대 풀 {WingmanPool}기를 '{m.name}/Wingmen'에 만들었습니다. Bake 하세요.", m);
        return true;
    }
}
