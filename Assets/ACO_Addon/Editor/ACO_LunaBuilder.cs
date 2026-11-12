using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using UdonSharpEditor;
using ACO;

/// UAV 에이스 LUNA(NPC_Luna)와 궤도 폭격 연출(ACO_OrbitalStrike) — 설계 6-137.
/// 모델: Models/Luna/ACO_Luna.fbx(Tools/aco_luna_model.py — 카나드 + 크랭크 델타 블렌디드 외피, 흰색·금색, 배 밑 외장 레일건 포드, 기수 턱 밑 EOTS).
///  Luna_Body = 기체, Luna_Uav = 요격 UAV(날개 밑 거치대에 0.6배로 붙고, 전개하면 1배). 자리(포구·유도 레이저·거치대)는 생성 스크립트의 값과 같다(LunaMuzzle 등).
///  - NPC_Luna: 일반 NPC 전투기 + 체력 2000(배율 없음)·플레어 6번 + 자식 ACO_LunaAce. AI의 미사일 런처가 레일건이다(곧게 나는 초고속 탄 ACO_Missile_LunaRail,
///    발사 원뿔 3도, 락온 2초 = 충전, 사거리 5km). 유도 레이저·조준 레이저·UAV 요격 레이저는 LineRenderer.
///    UAV 4기(Uav_0~3): 기체의 자식(처음엔 꺼 둠). 각자 요격 CIWS(ACO_AAGunSite — 함선 CIWS와 똑같이 포탑·기총으로 요격, 비행기는 노리지 않는다, 방어 반경 210m.
///    연출만 예광탄 대신 파란 레이저 — 기총의 레이저 부품 ACO_NPCLaser)·체력 60·락온 마커(적 항공기 층)·
///    플레어 0번 장치(UAV를 노린 미사일이 LUNA의 플레어에 끌려가지 않게 — 미사일은 마커에서 부모 쪽으로 플레어 장치를 찾는다). 전개 전엔 기체에 단 작은 포드(DockedPods)가 보인다.
///  - OrbitalStrike: LUNA 페이즈 모듈의 자식. 빛줄기 LineRenderer, 대폭발(공중 요새와 같은 ACO_FortressBlast), 지평선 섬광, 내려오는 소리.
public static partial class ACO_PrefabBuilder
{
    public const string LunaPrefab = "NPC_Luna";
    public const string LunaRailPrefab = "ACO_Missile_LunaRail";
    /// 레일건 탄(LUNA · MR. S)이 플레이어 기체에 맞으면 보내는 SAV 확장 이벤트 — SAV 소리 장치의 MissileHit 소리.
    public const string RailHitEvent = "SFEXT_G_MissileHit100";
    public const int LunaUavCount = 4;
    public const string LunaFbx = "Assets/ACO_Addon/Models/Luna/ACO_Luna.fbx";
    private const string LunaAlbedo = "Assets/ACO_Addon/Models/Luna/ACO_Luna_Albedo.png";
    // Tools/aco_luna_model.py가 찍는 자리(유니티 좌표, 기체 기준) — 모델을 고치면 함께 고친다
    private static readonly Vector3 LunaMuzzle = new Vector3(0f, -1.2f, 6.7f);         // 외장 레일건 레일 끝 가운데
    private static readonly Vector3 LunaDesignator = new Vector3(0f, -0.88f, 7.5f);     // 기수 턱 밑 EOTS 창
    private static readonly Vector3[] LunaDocks = { new Vector3(2.9f, -0.33f, -4.0f), new Vector3(-2.9f, -0.33f, -4.0f), new Vector3(4.6f, -0.3f, -5.4f), new Vector3(-4.6f, -0.3f, -5.4f) };
    private const float LunaDockScale = 0.6f;

    /// LUNA 교전 값(6-137): 도시 위 1,500m·반경 2km 선회, 플레이어 5km 안이면 궤도를 풀고 추격(레일건 사거리), 도시에서 8km 넘으면 복귀.
    /// AI 탐지 6km·놓침 8km — 일반 전투기 기본(2.5km)이면 선회 고도에서 저공 플레이어를 못 본다.
    private static void ApplyLunaEngage(ACO_NPCFighterAI ai, ACO_LunaAce ace)
    {
        if (ai != null)
        {
            ai.DetectRange = 6000f;
            ai.LoseTargetRange = 8000f;
            if (ai.Flight != null) { ai.Flight.MaxBoost = 1.8f; }   // 저격 이탈 급가속(ACO_LunaAce.SnipeBoost) 상한 — 동기화 예측도 이 값까지 본다
        }
        if (ace != null)
        {
            ace.OrbitAltitude = 1500f;
            ace.OrbitRadius = 2000f;
            ace.EngageDistance = 5000f;
            ace.LeashDistance = 8000f;
        }
    }

    /// LUNA 레일건 각도: 조준 경고 15도, 발사 원뿔 8도(숙련도 배율을 받는다), 포구가 돌 수 있는 각도 5도 — 원뿔 안이지만 포구 밖이면 겨누지 못한 채로도 쏜다(위협 사격).
    private static void ApplyLunaRailCone(ACO_NPCFighterAI ai, ACO_LunaAce ace)
    {
        if (ai != null) { ai.MissileFireConeAngle = 8f; }
        if (ace != null)
        {
            ace.GimbalAngle = 5f;
            ace.WarnAngle = 15f;
            ace.AimLaserLength = 6000f;
        }
    }

    /// Setup Check luna-rail-cone-2: 이미 있는 NPC_Luna의 레일건 각도(경고 15·원뿔 8·포구 5도)와 조준 레이저 길이.
    public static void ApplyLunaRailConeToPrefab()
    {
        string path = PathOf(LunaPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_NPCFighterAI ai = root.GetComponent<ACO_NPCFighterAI>();
            ACO_LunaAce ace = root.GetComponentInChildren<ACO_LunaAce>(true);
            if (ai == null && ace == null) { return; }
            ApplyLunaRailCone(ai, ace);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] NPC_Luna: 레일건 조준 경고 15도, 발사 원뿔 8도, 포구 5도, 조준 레이저 6km로 맞췄습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// LUNA 저격 빈도: 교전 6초 뒤 처음, 그 뒤 20초마다, 꼬리 위협이면 지난 저격 8초 뒤부터(선회만 하다 공격 기회가 적었다).
    private static void ApplyLunaSnipeTiming(ACO_LunaAce ace)
    {
        if (ace == null) { return; }
        ace.SnipeInterval = 20f;
        ace.SnipeFirstDelay = 6f;
        ace.SnipeTailCooldown = 8f;
    }

    /// Setup Check luna-snipe-often-1: 이미 있는 NPC_Luna의 저격 빈도를 올린다(40·15·15초 → 20·6·8초).
    public static void ApplyLunaSnipeTimingToPrefab()
    {
        string path = PathOf(LunaPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_LunaAce ace = root.GetComponentInChildren<ACO_LunaAce>(true);
            if (ace == null) { return; }
            ApplyLunaSnipeTiming(ace);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] NPC_Luna: 저격을 교전 6초 뒤 처음, 그 뒤 20초마다(꼬리 위협 8초 뒤부터)로 맞췄습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// Setup Check luna-uav-silent-1: 이미 있는 NPC_Luna의 UAV 요격이 무전을 내지 않게 한다.
    public static void ApplyLunaUavSilent()
    {
        string path = PathOf(LunaPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int n = 0;
            foreach (ACO_AAGunSite aa in root.GetComponentsInChildren<ACO_AAGunSite>(true)) { aa.SilentIntercept = true; n++; }
            if (n == 0) { return; }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[ACO] NPC_Luna: UAV {n}기의 요격 무전을 껐습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// Setup Check luna-snipe-1: 이미 있는 NPC_Luna에 저격 이탈 급가속 상한(MaxBoost 1.8)과 저격·플레어 음성을 넣는다.
    public static void ApplyLunaSnipeToPrefab()
    {
        string path = PathOf(LunaPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_NPCFighterAI ai = root.GetComponent<ACO_NPCFighterAI>();
            ACO_LunaAce ace = root.GetComponentInChildren<ACO_LunaAce>(true);
            if (ai != null && ai.Flight != null) { ai.Flight.MaxBoost = 1.8f; }
            if (ace != null) { ACO_VoiceLines.ApplyLuna(ace); }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] NPC_Luna: 저격 이탈 급가속 상한 1.8, 저격·플레어 음성을 넣었습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// Setup Check luna-engage-1: 이미 있는 NPC_Luna에 교전 값을 넣는다(다시 만들지 않는다 — 손으로 고친 다른 값·씬 참조 유지).
    public static void ApplyLunaEngageToPrefab()
    {
        string path = PathOf(LunaPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_NPCFighterAI ai = root.GetComponent<ACO_NPCFighterAI>();
            ACO_LunaAce ace = root.GetComponentInChildren<ACO_LunaAce>(true);
            if (ai == null && ace == null) { return; }
            ApplyLunaEngage(ai, ace);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] NPC_Luna: 선회 1,500m·반경 2km, 교전 5km, 복귀 8km, AI 탐지 6km·놓침 8km로 맞췄습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static GameObject BuildLuna()
    {
        EnsurePrefabFolder();
        GameObject slug = LoadOrBuildLunaRail();
        GameObject root = CreateFighterObject(LunaPrefab, slug != null ? slug : LoadOrBuildMissile());
        ACO_NPCFighterAI ai = root.GetComponent<ACO_NPCFighterAI>();
        SaccTarget health = root.GetComponent<SaccTarget>();
        health.Health = 2000f;
        ai.BaseHealth = 2000f;   // 숙련도 배율을 받지 않는다
        ai.Flares.FlareSalvos = 6;
        ai.AceFlareChance = 0.6f;
        ApplyLunaEngage(ai, null);

        // 레일건 = AI 미사일 런처: 기수 포구 한 곳, 좁은 원뿔·긴 락온(충전 2초)
        ApplyLunaRailCone(ai, null);
        ai.MissileMaxRange = 5000f;
        ai.MissileMinRange = 400f;
        ACO_MissileLauncher rail = ai.MissileLauncher;
        Transform launcherT = rail.transform;
        for (int i = launcherT.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(launcherT.GetChild(i).gameObject); }
        Transform muzzle = NewChild(launcherT.gameObject, "RailMuzzle", launcherT.InverseTransformPoint(root.transform.TransformPoint(LunaMuzzle)));
        rail.LaunchPoints = new Transform[] { muzzle };
        rail.LockTime = 2f;
        rail.NumMissiles = 6;
        rail.NumPreInstantiated = 2;
        rail.FireInterval = 3f;
        rail.ReloadTime = 12f;
        TuneLunaRailBurst(rail);
        AudioSource railSound = CreateRailgunSound(launcherT, "RailSound", "ACO_Railgun_MiniFire", 150f, 6000f, false);
        rail.LaunchSound = railSound;

        // 기체 모델(텍스처 한 장 — 흰 동체 무늬 + 색 칸). 없으면 상자로 둔다
        Transform model = root.transform.Find("Model");
        bool hasFbx = AssetDatabase.LoadAssetAtPath<GameObject>(LunaFbx) != null;
        if (!hasFbx) { Debug.LogError($"[ACO] {LunaFbx}가 없습니다 — Tools/aco_luna_model.py를 실행하세요. 상자 모델로 만듭니다."); }
        Material bodyMat = hasFbx ? LunaModelMaterial() : LunaMaterial("ACO_LunaBody", new Color(0.78f, 0.82f, 0.9f), 0f);
        Mesh uavMesh = null;
        if (model != null)
        {
            if (hasFbx)
            {
                ConfigureModelImporter(LunaFbx);
                model.localScale = Vector3.one;
                model.localPosition = Vector3.zero;
                model.localRotation = Quaternion.identity;
                model.GetComponent<MeshFilter>().sharedMesh = FbxMesh(LunaFbx, "Luna_Body");
                uavMesh = FbxMesh(LunaFbx, "Luna_Uav");
            }
            model.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;
        }
        if (hasFbx)
        {
            // 판정: 동체 상자 + 날개 상자(길이 약 19m, 폭 13m). 레일건 포드는 작아서 동체 상자에 든다
            BoxCollider bodyBox = root.GetComponent<BoxCollider>();
            bodyBox.size = new Vector3(2.6f, 2.4f, 18.6f);
            bodyBox.center = new Vector3(0f, -0.1f, 0.2f);
            BoxCollider wingBox = root.AddComponent<BoxCollider>();
            wingBox.size = new Vector3(13f, 0.6f, 6.5f);
            wingBox.center = new Vector3(0f, 0f, -4.4f);
            ai.DisableCollidersOnDeath = new Collider[] { bodyBox, wingBox };
            Transform gun = root.transform.Find("Gun");
            if (gun != null) { gun.localPosition = new Vector3(0.9f, 0.2f, 5.0f); }
            if (ai.Flares != null && ai.Flares.FlareParticles != null) { ai.Flares.FlareParticles.transform.localPosition = new Vector3(0f, -0.4f, -9.4f); }
            Transform marker = root.transform.Find("AAMTarget");
            if (marker != null)
            {
                SphereCollider mc = marker.GetComponent<SphereCollider>();
                if (mc != null) { mc.radius = 5f; }
            }
        }

        var aceGo = new GameObject("ACO_LunaAce");
        aceGo.transform.SetParent(root.transform, false);
        ACO_LunaAce ace = aceGo.AddUdonSharpComponent<ACO_LunaAce>();
        ace.AI = ai;
        ace.TargetHealth = health;
        ace.Muzzle = muzzle;
        ace.RailRange = ai.MissileMaxRange;
        ApplyLunaEngage(null, ace);
        ApplyLunaRailCone(null, ace);
        ApplyLunaSnipeTiming(ace);
        ai.Ace = ace;

        // 유도 레이저(기체 아래 → 도시), 조준 레이저(포구 → 기수 앞)
        ace.Designator = NewChild(aceGo, "Designator", hasFbx ? LunaDesignator : new Vector3(0f, -0.5f, 1f));
        ace.DesignatorBeam = LunaBeam(aceGo.transform, "DesignatorBeam", new Color(1f, 0.55f, 0.7f), new Color(1f, 0.25f, 0.45f), 3f);
        ace.AimLaser = LunaBeam(aceGo.transform, "AimLaser", new Color(0.8f, 0.95f, 1f), new Color(0.45f, 0.75f, 1f), 0.5f);

        // 전개 전 UAV(날개 밑 거치대에 붙은 작은 UAV 모양) — 격추되면 기체와 함께 숨긴다
        var pods = new GameObject("DockedPods");
        pods.transform.SetParent(root.transform, false);
        Material uavMat = hasFbx ? bodyMat : LunaMaterial("ACO_LunaUav", new Color(0.32f, 0.36f, 0.44f), 0f);
        Material glowMat = LunaMaterial("ACO_LunaGlow", new Color(0.6f, 0.85f, 1f), 3f);
        var podList = new List<GameObject>();
        if (uavMesh != null)
        {
            foreach (Vector3 p in LunaDocks)
            {
                var pod = new GameObject("Pod");
                pod.transform.SetParent(pods.transform, false);
                pod.transform.localPosition = p;
                pod.transform.localScale = Vector3.one * LunaDockScale;
                pod.AddComponent<MeshFilter>().sharedMesh = uavMesh;
                pod.AddComponent<MeshRenderer>().sharedMaterial = uavMat;
                podList.Add(pod);
            }
        }
        else
        {
            Vector3[] podPos = { new Vector3(1.6f, -0.45f, -0.6f), new Vector3(-1.6f, -0.45f, -0.6f), new Vector3(0.7f, -0.5f, -2f), new Vector3(-0.7f, -0.5f, -2f) };
            foreach (Vector3 p in podPos) { podList.Add(LunaBox(pods.transform, "Pod", p, new Vector3(0.9f, 0.25f, 0.7f), uavMat).gameObject); }
        }
        ace.DockedPods = podList.ToArray();
        var hide = new List<GameObject>(ai.HideOnDeath ?? new GameObject[0]);
        hide.Add(pods);
        ai.HideOnDeath = hide.ToArray();

        // UAV 4기
        var uavs = new Transform[LunaUavCount];
        var sites = new ACO_AAGunSite[LunaUavCount];
        for (int k = 0; k < LunaUavCount; k++)
        {
            sites[k] = CreateLunaUav(root.transform, k, uavMat, glowMat, uavMesh);
            uavs[k] = sites[k].transform;
        }
        ace.Uavs = uavs;
        ace.UavSites = sites;
        ACO_VoiceLines.ApplyLuna(ace);   // 등장·격추 음성(있으면)

        return SaveAndCleanup(root, $"{PrefabDir}/{LunaPrefab}.prefab");
    }

    /// 요격 UAV: 대공포(ACO_AAGunSite) 바탕 — 함선 CIWS와 똑같이 요격한다(포탑이 미사일 쪽으로 돌고 기총이 쏜다, 판정은 미사일). 비행기는 노리지 않는다(TargetLayers 0).
    /// 연출: 기총 예광탄 대신 파란 레이저(레이저 부품 ACO_NPCLaser — 포구 정면 1500m, 포탑이 돌면 휘두른다). 포탑은 360도·위아래 85도, 초당 120도.
    /// 방어 반경 210m(본체 약 190m는 덮고 다른 UAV는 못 덮는다), 요격 사거리 900m, 반응 0.4초 + 격추 0.8초, 쏜 뒤 1.5초 쉼(두 발이면 한 발은 뚫린다).
    /// 자기 유닛(UAV 자신)을 노린 미사일과 다른 표적(LUNA)을 노린 미사일을 같은 빠르기로 막는다(OtherDefenseFactor 1).
    private static ACO_AAGunSite CreateLunaUav(Transform parent, int index, Material bodyMat, Material glowMat, Mesh uavMesh = null)
    {
        var spec = new GunSpec
        {
            BodySize = new Vector3(3f, 0.8f, 2.4f), BarrelLength = 0.2f, BarrelThickness = 0.1f,
            Health = 60f, Armor = 1f, Score = 200, Label = "UAV",
            DetectRange = 0f, LoseTargetRange = 0f, YawRate = 120f, PitchRate = 120f, TrackingLag = 0f, AimError = 0f,
            MaxRange = 900f, MuzzleVelocity = 2200f, FireStartAngle = 6f, EffectiveAngle = 2f, DwellTime = 1f,
            BurstDuration = 1f, BurstCooldown = 1f, DamagePerTick = 0f, MinDamageInterval = 0.2f, MaxDamageInterval = 0.5f,
            Spread = 0f, TracerRate = 0f,
        };
        GameObject go = CreateAAGunObject($"Uav_{index}", spec);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        ACO_AAGunSite aa = go.GetComponent<ACO_AAGunSite>();
        Transform barrel = aa.TurretPitch.Find("Barrel");
        if (barrel != null) { Object.DestroyImmediate(barrel.gameObject); }
        // 포탑: UAV 아래 렌즈 자리, 기체 기준으로 360도·위아래 85도
        aa.TurretYaw.localPosition = new Vector3(0f, -0.25f, 0.3f);
        aa.TurretPitch.localRotation = Quaternion.identity;
        aa.MountRelativeAim = true;
        aa.YawArc = 360f;
        aa.MinPitch = -85f;
        aa.MaxPitch = 85f;
        aa.MinTargetAltitude = -100000f;
        aa.ReturnToRestDelay = 0.8f;
        aa.SilentIntercept = true;   // 에이스전엔 일반 요격 무전("Their CIWS shot it down" 등) 없음
        // 기총: 예광탄 대신 파란 레이저(피해 없음 — 요격 연출만), 쏘는 동안 레이저 소리
        ACO_NPCGun gun = aa.Gun;
        if (gun != null)
        {
            gun.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            AttachLaserModule(gun, new Color(0.85f, 0.95f, 1f), new Color(0.25f, 0.55f, 1f), 0.8f, 1500f, 0, false);
            gun.DamagePerTick = 0f;
            AudioSource hum = CreateRailgunSound(gun.transform, "LaserSound", "ACO_Railgun_Charge_Loop", 30f, 1500f, true);
            hum.pitch = 2.2f;
            hum.volume = 0.5f;
            gun.FireSound = hum;
        }

        // 판정 상자는 가운데에(대공포는 받침 위로 올라가 있다), 모델은 납작한 기체 + 빛나는 렌즈
        BoxCollider hitbox = go.GetComponent<BoxCollider>();
        hitbox.size = new Vector3(3f, 0.8f, 2.4f);
        hitbox.center = Vector3.zero;
        Transform oldModel = go.transform.Find("Model");
        if (oldModel != null) { Object.DestroyImmediate(oldModel.gameObject); }
        var model = new GameObject("Model");
        model.layer = go.layer;
        model.transform.SetParent(go.transform, false);
        if (uavMesh != null)
        {
            // UAV 모델(Luna_Uav — 배 밑 포탑 자리가 TurretYaw와 같다)
            var body = new GameObject("Body");
            body.layer = go.layer;
            body.transform.SetParent(model.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = uavMesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = bodyMat;
            aa.TurretYaw.localPosition = new Vector3(0f, -0.19f, 0.3f);
        }
        else
        {
            LunaBox(model.transform, "Body", Vector3.zero, new Vector3(3f, 0.35f, 1.6f), bodyMat);
            LunaBox(model.transform, "Lens", new Vector3(0f, -0.25f, 0.3f), new Vector3(0.5f, 0.3f, 0.5f), glowMat);
        }
        aa.HideOnDeath = new GameObject[] { model };
        aa.DisableCollidersOnDeath = new Collider[] { hitbox };

        // 락온 마커: 적 항공기 층(공대공 미사일이 노린다)
        Transform marker = go.transform.Find("AAMTarget");
        if (marker != null)
        {
            marker.gameObject.layer = LayerEnemyAir;
            marker.localPosition = Vector3.zero;
            SphereCollider mc = marker.GetComponent<SphereCollider>();
            if (mc != null) { mc.radius = 3f; }
        }

        aa.TargetLayers = 0;
        aa.RequireLineOfSight = false;
        aa.SleepMargin = 0f;
        aa.MarkerLabel = "UAV";
        aa.ScoreValue = 200;
        aa.InterceptProtectLayers = 1 << LayerEnemyAir;
        aa.InterceptRange = 900f;
        aa.DefenseRadius = 210f;
        aa.InterceptReactionTime = 0.4f;
        aa.InterceptKillTime = 0.8f;
        aa.InterceptCooldown = 1.5f;
        aa.OtherDefenseFactor = 1f;
        aa.InterceptTuneVersion = ACO_SceneBaker.CiwsTuneVersion;
        aa.InterceptDefaultsApplied = true;

        // 플레어 0번 장치: UAV를 노린 미사일이 부모(LUNA)의 플레어 장치를 찾지 않게 막는다
        ACO_FlareDispenser stop = go.AddUdonSharpComponent<ACO_FlareDispenser>();
        stop.FlareSalvos = 0;
        stop.FlareChance = 0f;
        stop.TargetHealth = go.GetComponent<SaccTarget>();

        go.SetActive(false);   // 전개 전엔 꺼 둔다(ACO_LunaAce가 켠다)
        return aa;
    }

    /// 기총에 레이저 부품(ACO_NPCLaser)을 단다 — 예광탄 파티클은 지우고, 포구 정면으로 빔(LineRenderer)을 그린다. 판정·피해는 기총 그대로.
    /// 레이저 에이스 렌즈(빨강, 지형에서 끊김·불티)와 LUNA UAV(파랑, 연출만)가 쓴다. 이미 있으면 값만 다시 맞춘다.
    public static ACO_NPCLaser AttachLaserModule(ACO_NPCGun gun, Color c0, Color c1, float width, float length, int blockLayers, bool sparks)
    {
        if (gun.VisualParticle != null) { Object.DestroyImmediate(gun.VisualParticle.gameObject); gun.VisualParticle = null; }
        ACO_NPCLaser laser = gun.Laser;
        if (laser == null)
        {
            var go = new GameObject("Laser");
            go.transform.SetParent(gun.transform, false);
            laser = go.AddUdonSharpComponent<ACO_NPCLaser>();
            laser.Beam = LunaBeam(go.transform, "Beam", c0, c1, width);
            gun.Laser = laser;
        }
        laser.Muzzle = gun.Muzzle != null ? gun.Muzzle : gun.transform;
        laser.Length = length;
        laser.Width = width;
        laser.BlockLayers = blockLayers;
        if (sparks && laser.Sparks == null) { laser.Sparks = ACO_FxBuilder.BuildLaserSparks(laser.transform); }
        EditorUtility.SetDirty(laser);
        EditorUtility.SetDirty(gun);
        return laser;
    }

    private static Transform LunaBox(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = parent.gameObject.layer;
        Object.DestroyImmediate(box.GetComponent<Collider>());
        box.transform.SetParent(parent, false);
        box.transform.localPosition = pos;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return box.transform;
    }

    private static LineRenderer LunaBeam(Transform parent, string name, Color c0, Color c1, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        LineRenderer beam = go.AddComponent<LineRenderer>();
        beam.useWorldSpace = true;
        beam.positionCount = 2;
        beam.widthMultiplier = width;
        beam.numCapVertices = 2;
        beam.textureMode = LineTextureMode.Stretch;
        beam.alignment = LineAlignment.View;
        beam.sharedMaterial = ACO_FxBuilder.LaserBeamMaterial();
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 1f) });
        beam.colorGradient = g;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.receiveShadows = false;
        beam.enabled = false;
        return beam;
    }

    /// LUNA 텍스처 재질(기체·UAV 함께 — 텍스처 한 장). 값은 언제나 이 함수가 정한다.
    private static Material LunaModelMaterial()
    {
        var importer = AssetImporter.GetAtPath(LunaAlbedo) as TextureImporter;
        if (importer != null && (importer.maxTextureSize < 2048 || importer.filterMode != FilterMode.Trilinear || importer.anisoLevel < 4))
        {
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
        string path = "Assets/ACO_Addon/Generated/ACO_LunaModel.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_LunaModel" };
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = Color.white;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(LunaAlbedo));
        m.SetFloat("_Metallic", 0.2f);
        m.SetFloat("_Glossiness", 0.55f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 단색(emission > 0이면 빛나는) 재질. 값은 언제나 이 함수가 정한다.
    private static Material LunaMaterial(string name, Color color, float emission)
    {
        string path = $"Assets/ACO_Addon/Generated/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = color;
        m.SetFloat("_Metallic", 0.4f);
        m.SetFloat("_Glossiness", 0.6f);
        if (emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * emission);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    /// LUNA 레일건 탄: RGP 탄(초고속 빛나는 탄체)의 변형 — 유도하지 않고 곧게 난다(FlyStraightTime이 수명보다 길다). 급기동하면 피할 수 있다.
    private static GameObject LoadOrBuildLunaRail()
    {
        GameObject m = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(LunaRailPrefab));
        return m != null ? m : BuildLunaRail();
    }

    public static GameObject BuildLunaRail()
    {
        EnsurePrefabFolder();
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("ACO_Missile_RGP"));
        if (source == null) { source = BuildRgpSlug(); }
        if (source == null) { Debug.LogError("[ACO] LUNA 레일건 탄: ACO_Missile_RGP를 만들 수 없습니다."); return null; }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            instance.name = LunaRailPrefab;
            ACO_Missile missile = instance.GetComponent<ACO_Missile>();
            if (missile != null)
            {
                missile.FlyStraightTime = 10f;
                missile.MaxLifetime = 2.5f;
                missile.Damage = 70f;
                missile.ProximityRadius = 12f;
                missile.HitExtensionEvent = RailHitEvent;   // 맞으면 SAV MissileHit 소리
                EditorUtility.SetDirty(missile);
            }
            return PrefabUtility.SaveAsPrefabAsset(instance, PathOf(LunaRailPrefab));
        }
        finally { Object.DestroyImmediate(instance); }
    }

    // ───────────── 궤도 폭격 ─────────────

    /// 페이즈 모듈 아래에 OrbitalStrike를 만든다(있으면 지우고 다시).
    public static ACO_OrbitalStrike BuildOrbitalStrike(Transform parent)
    {
        Transform old = parent.Find("OrbitalStrike");
        if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); }
        var root = new GameObject("OrbitalStrike");
        Undo.RegisterCreatedObjectUndo(root, "ACO orbital strike");
        root.transform.SetParent(parent, false);
        ACO_OrbitalStrike strike = UdonSharpUndo.AddComponent<ACO_OrbitalStrike>(root);

        strike.Streak = LunaBeam(root.transform, "Streak", new Color(1f, 1f, 1f), new Color(0.55f, 0.75f, 1f), 60f);
        strike.Blast = CreateFortressBlast(root.transform);

        string flashPath = $"{RailgunGeneratedDir}/ACO_HorizonFlash.mat";
        Material flashMat = AssetDatabase.LoadAssetAtPath<Material>(flashPath);
        if (flashMat == null)
        {
            flashMat = new Material(Shader.Find("ACO/HorizonFlash")) { name = "ACO_HorizonFlash" };
            AssetDatabase.CreateAsset(flashMat, flashPath);
        }
        var horizon = new GameObject("Horizon").transform;
        horizon.SetParent(root.transform, false);
        strike.HorizonFlash = CreateHorizonSystem(horizon, "HorizonFlash", 1.5f, 24000f, 11000f, new Color(1f, 0.98f, 0.95f), new Color(0.7f, 0.85f, 1f), flashMat);
        strike.HorizonGlow = CreateHorizonSystem(horizon, "HorizonGlow", 9f, 16000f, 7000f, new Color(1f, 0.6f, 0.25f), new Color(0.5f, 0.14f, 0.05f), flashMat);

        AudioSource fall = CreateRailgunSound(root.transform, "FallSound", "ACO_Railgun_Charge_Loop", 1f, 100f, true);
        fall.spatialBlend = 0f;
        fall.pitch = 0.45f;
        fall.volume = 0.6f;
        strike.FallSound = fall;

        foreach (Transform child in root.transform) { Undo.RegisterCreatedObjectUndo(child.gameObject, "ACO orbital strike"); }
        EditorUtility.SetDirty(strike);
        return strike;
    }

    /// 루나 레일건 3연사(6-155): 0.3초 간격 3발(탄창 6발 = 연사 두 번), 풀은 연사를 다 담게 4발.
    public static void TuneLunaRailBurst(ACO_MissileLauncher rail)
    {
        rail.BurstCount = 3;
        rail.BurstGap = 0.3f;
        rail.RailFlyby = true;   // 스침 소리(6-156)
        rail.NumPreInstantiated = Mathf.Max(rail.NumPreInstantiated, 4);
        EditorUtility.SetDirty(rail);
    }

    /// Setup Check luna-rail-burst-1: 이미 있는 NPC_Luna의 레일건 런처를 3연사로(조준 레이저는 스크립트가 끈다).
    public static void ApplyLunaRailBurst()
    {
        ACO_PrefabTuning.EditPrefabs(root =>
        {
            ACO_LunaAce luna = root.GetComponentInChildren<ACO_LunaAce>(true);
            ACO_NPCFighterAI ai = luna != null ? (luna.AI != null ? luna.AI : root.GetComponent<ACO_NPCFighterAI>()) : null;
            if (ai == null || ai.MissileLauncher == null) { return false; }
            ACO_MissileLauncher rail = ai.MissileLauncher;
            if (rail.BurstCount == 3 && Mathf.Approximately(rail.BurstGap, 0.3f) && rail.NumPreInstantiated >= 4 && rail.RailFlyby) { return false; }
            TuneLunaRailBurst(rail);
            return true;
        });
    }
}
