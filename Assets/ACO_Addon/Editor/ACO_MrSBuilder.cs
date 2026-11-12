using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using UdonSharpEditor;
using ACO;

/// 태양 에이스 MR. S(NPC_MrS) — 설계 6-167.
/// 모델: Models/XFW/ACO_XFW.fbx(Tools/aco_xfw_model.py — YF-23풍 동체 · W자 주익 · 오각 카나드 · 배 밑 레일건, 회색 두 톤 + 주황 날개 끝, 텍스처 4096).
///  - NPC_MrS: 일반 NPC 전투기 + 체력 2000(배율 없음)·플레어 8번 + 자식 ACO_MrS. 평소 무장은 AI의 일반 미사일·기총.
///    레일건은 따로 둔 런처(RailLauncher — LUNA와 같은 곧게 나는 초고속 탄 ACO_Missile_LunaRail, 단발)를 ACO_MrS가 반전 때 쏜다.
///    포구(RailMuzzle)는 배 밑 레일 끝, 충전 빛(RailCharge 파티클)·충전 소리가 포구에 붙는다.
public static partial class ACO_PrefabBuilder
{
    public const string MrSPrefab = "NPC_MrS";
    public const string MrSFbx = "Assets/ACO_Addon/Models/XFW/ACO_XFW.fbx";
    private const string MrSAlbedo = "Assets/ACO_Addon/Models/XFW/ACO_XFW_Albedo.png";
    // Tools/aco_xfw_model.py의 자리(유니티 좌표, 기체 기준 — 앞 +Z): 레일 끝(RAIL_TIP 4.7) 두 레일 사이(위 레일 아래 -1.12 · 아래 레일 위 -1.26)
    private static readonly Vector3 MrSMuzzle = new Vector3(0f, -1.19f, 4.75f);

    public static GameObject BuildMrS()
    {
        EnsurePrefabFolder();
        GameObject slug = LoadOrBuildLunaRail();
        GameObject root = CreateFighterObject(MrSPrefab, LoadOrBuildMissile());
        ACO_NPCFighterAI ai = root.GetComponent<ACO_NPCFighterAI>();
        SaccTarget health = root.GetComponent<SaccTarget>();
        health.Health = 6000f;
        ai.BaseHealth = 6000f;   // 숙련도 배율을 받지 않는다
        ai.Flares.FlareSalvos = 8;
        ai.AceFlareChance = 0.6f;
        ai.DetectRange = 6000f;
        ai.LoseTargetRange = 8000f;
        if (ai.Flight != null) { ai.Flight.MaxBoost = 2.8f; }   // 태양 이탈 급가속(ACO_MrS.SunBoost) 상한 — 동기화 예측도 이 값까지 본다

        // 기체 모델(메시 하나 · 재질 두 칸: 몸체 · 유리, 텍스처 한 장)
        Transform model = root.transform.Find("Model");
        bool hasFbx = AssetDatabase.LoadAssetAtPath<GameObject>(MrSFbx) != null;
        if (!hasFbx) { Debug.LogError($"[ACO] {MrSFbx}가 없습니다 — Tools/aco_xfw_model.py를 실행하세요. 상자 모델로 만듭니다."); }
        if (model != null && hasFbx)
        {
            ConfigureModelImporter(MrSFbx);
            model.localScale = Vector3.one;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.GetComponent<MeshFilter>().sharedMesh = FbxMesh(MrSFbx, "ACO_XFW");
            model.GetComponent<MeshRenderer>().sharedMaterials = new[] { MrSBodyMaterial(), MrSGlassMaterial() };
        }
        if (hasFbx)
        {
            // 판정: 동체 상자 + 주익 상자(길이 약 20m, 폭 15.6m)
            BoxCollider bodyBox = root.GetComponent<BoxCollider>();
            bodyBox.size = new Vector3(4.2f, 2.6f, 19.6f);
            bodyBox.center = new Vector3(0f, 0.2f, 0.1f);
            BoxCollider wingBox = root.AddComponent<BoxCollider>();
            wingBox.size = new Vector3(15.6f, 0.8f, 6.4f);
            wingBox.center = new Vector3(0f, 0.35f, -3.8f);
            ai.DisableCollidersOnDeath = new Collider[] { bodyBox, wingBox };
            Transform gun = root.transform.Find("Gun");
            if (gun != null) { gun.localPosition = new Vector3(0.9f, 0.45f, 6.0f); }
            if (ai.Flares != null && ai.Flares.FlareParticles != null) { ai.Flares.FlareParticles.transform.localPosition = new Vector3(0f, 0.15f, -9.6f); }
            Transform marker = root.transform.Find("AAMTarget");
            if (marker != null)
            {
                SphereCollider mc = marker.GetComponent<SphereCollider>();
                if (mc != null) { mc.radius = 6f; }
            }
        }

        CopyAceFlareStyle(ai, new Vector3(0f, -0.6f, -4f));

        // 레일건 런처(단발 — ACO_MrS가 쏜다): 포구 한 곳, 충전 빛·소리
        var railGo = new GameObject("RailLauncher");
        railGo.transform.SetParent(root.transform, false);
        Transform muzzle = NewChild(railGo, "RailMuzzle", MrSMuzzle);
        ACO_MissileLauncher normal = ai.MissileLauncher;
        ACO_MissileLauncher rail = railGo.AddUdonSharpComponent<ACO_MissileLauncher>();
        rail.EntityControl = normal != null ? normal.EntityControl : ai.EntityControl;
        rail.InheritVelocityFrom = normal != null ? normal.InheritVelocityFrom : ai.Flight;
        rail.AAM = slug != null ? slug : (normal != null ? normal.AAM : null);
        rail.LaunchPoints = new Transform[] { muzzle };
        rail.LockTime = 1f;
        rail.NumMissiles = 4;
        rail.NumPreInstantiated = 2;
        rail.FireInterval = 2f;
        rail.ReloadTime = 8f;
        rail.BurstCount = 1;
        rail.RailFlyby = true;   // 스침 소리(6-156)
        rail.LaunchSound = CreateRailgunSound(railGo.transform, "RailSound", "ACO_Railgun_MiniFire", 150f, 6000f, false);
        AudioSource charge = CreateRailgunSound(muzzle, "ChargeSound", "ACO_Railgun_Charge_Loop", 40f, 2500f, true);
        charge.pitch = 1.6f;
        charge.volume = 0.8f;
        ParticleSystem chargeFx = BuildRailChargeFx(muzzle);

        var aceGo = new GameObject("ACO_MrS");
        aceGo.transform.SetParent(root.transform, false);
        ACO_MrS ace = aceGo.AddUdonSharpComponent<ACO_MrS>();
        ace.AI = ai;
        ace.TargetHealth = health;
        ace.RailLauncher = rail;
        ace.Muzzle = muzzle;
        ace.ChargeFx = chargeFx;
        ace.ChargeSound = charge;
        ai.Ace = ace;
        ACO_VoiceLines.ApplyMrS(ace);   // 대사 음성(있으면)

        ACO_AceWreck.Build(root, MrSPrefab);   // 격추 추락(6-145)
        GameObject saved = SaveAndCleanup(root, $"{PrefabDir}/{MrSPrefab}.prefab");
        ACO_EngineSounds.EnsureOnPrefab(saved);
        return saved;
    }

    /// 플레어 모양: 손으로 다듬어 둔 에이스 플레어(NPC_Borderless의 'Flares (1)' — 별 재질 · 꼬리 · 비스듬히 뿌림)를 복사해 기본 'Flares'를 바꾼다.
    /// 원본이 없으면 기본 그대로. 뿌리는 방식(이미터 · 개수 · 시간)도 원본 플레어 장치 값을 따른다. 자리(local)는 이 기체에 맞춘다.
    private static void CopyAceFlareStyle(ACO_NPCFighterAI ai, Vector3 localPos)
    {
        if (ai == null || ai.Flares == null) { return; }
        GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(BorderlessPrefab));
        ACO_NPCFighterAI srcAi = src != null ? src.GetComponent<ACO_NPCFighterAI>() : null;
        ACO_FlareDispenser srcF = srcAi != null ? srcAi.Flares : null;
        if (srcF == null || srcF.FlareParticles == null) { Debug.LogWarning("[ACO] MR. S: NPC_Borderless의 플레어가 없어 기본 플레어로 둡니다."); return; }
        ParticleSystem old = ai.Flares.FlareParticles;
        GameObject copy = Object.Instantiate(srcF.FlareParticles.gameObject, ai.transform, false);
        copy.name = srcF.FlareParticles.name;
        copy.transform.localPosition = localPos;
        copy.transform.localRotation = srcF.FlareParticles.transform.localRotation;
        copy.transform.localScale = srcF.FlareParticles.transform.localScale;
        ai.Flares.FlareParticles = copy.GetComponent<ParticleSystem>();
        ai.Flares.FlareUseEmitter = srcF.FlareUseEmitter;
        ai.Flares.FlareParticleCount = srcF.FlareParticleCount;
        ai.Flares.FlareEmitDuration = srcF.FlareEmitDuration;
        if (old != null && old.gameObject != copy) { Object.DestroyImmediate(old.gameObject); }
    }

    /// 레일건 충전 빛: 포구 둘레 1.2m에서 안으로 빨려 드는 하늘색 빛 알갱이(가산 섬광 재질). 꺼 두었다가 ACO_MrS가 충전하는 동안 켠다.
    private static ParticleSystem BuildRailChargeFx(Transform muzzle)
    {
        var go = new GameObject("RailCharge");
        go.transform.SetParent(muzzle, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = 0.35f;
        main.startSpeed = -3.5f;   // 구 표면에서 가운데로
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startColor = new Color(0.55f, 0.9f, 1f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 80;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 70f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1.2f;
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(0.5f, 0.85f, 1f), 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0.6f, 1f) });
        col.color = g;
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = ACO_FxBuilder.FlashMaterial();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// XF-W 몸체 재질(텍스처 4096). 값은 언제나 이 함수가 정한다.
    private static Material MrSBodyMaterial()
    {
        var importer = AssetImporter.GetAtPath(MrSAlbedo) as TextureImporter;
        if (importer != null && (importer.maxTextureSize < 4096 || importer.filterMode != FilterMode.Trilinear || importer.anisoLevel < 4))
        {
            importer.maxTextureSize = 4096;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
        Material m = MrSMaterial("ACO_XFWBody");
        m.color = Color.white;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(MrSAlbedo));
        m.SetFloat("_Metallic", 0.25f);
        m.SetFloat("_Glossiness", 0.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 캐노피 유리(텍스처의 유리 색 그대로 + 매끈하게).
    private static Material MrSGlassMaterial()
    {
        Material m = MrSMaterial("ACO_XFWGlass");
        m.color = Color.white;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(MrSAlbedo));
        m.SetFloat("_Metallic", 0.6f);
        m.SetFloat("_Glossiness", 0.92f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material MrSMaterial(string name)
    {
        string path = $"Assets/ACO_Addon/Generated/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }
}
