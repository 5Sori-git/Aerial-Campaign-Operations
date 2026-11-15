using System;
using System.Collections.Generic;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;
using Object = UnityEngine.Object;

/// 잠수함 보스 NPC_Submarine(LEVIATHAN, 설계 6-202 · 6-204). 모델은 Models/Submarine/ACO_Submarine.fbx(Tools/aco_submarine_model.py — 3동선체, 길이 약 265m, 폭 약 80m).
/// 원점은 부상했을 때의 수면, 선체 가운데다. 무장 자리는 모델 스크립트의 표와 같아야 한다(갑판 높이 · 받침).
/// 구조(루트 = 함선 규칙 — 선체 SaccTarget · 히트박스 · Animator는 루트):
///  - 히트박스: 루트에 상자 4개(중앙 동체 · 함교 · 보조 동체 둘) — 물속이면 ACO_Submarine이 끈다
///  - Model(메시만, 충돌 없음 — 물속 깊으면 ACO_Submarine이 끈다): SB_Hull + 스크류 SB_Screw_R/L(ACO_Submarine이 돌린다)
///  - LockMarker(지상 레이어 30 — "AAMTarget"이 아닌 이름: SAV Target 애니메이터가 그 이름을 다룬다) · MarkerPoint(함교 위)
///  - Parts(7): 앞 갑판 16셀 긴 VLS 2 · 함교 꼭대기 CIWS 1 · 함교 뒤 갑판 Flak 1 + CIWS 1 · 보조 동체 위 소형 레일건 2. 물속이면 ACO_Submarine이 오브젝트를 끈다.
///  - MiniFx: 소형 레일건 탄 · 공중 폭발(두 문이 함께 쓴다 — 랜스 포대 값)
///  - Slcms: SLCM 풀(일제 발사 칸 SubSalvoSlots × 묶음 SubSlcmBanks). 각자 SaccTarget(일반 미사일 2발) · 공중 락온 마커 · 탄체 · 연기 · 저공 물보라.
///  - Fx: 발사 물기둥 · 물보라(어뢰 항적 · 부상) · 발사 소리 · 소나 원 · 소나 마커 / CarrierFire(항모 화재 3단계) / DeathEffects / FortressBlast / 항적(ACO_WaterFx)
public static partial class ACO_PrefabBuilder
{
    public const string SubmarinePrefab = "NPC_Submarine";
    private const string SubmarineFbx = "Assets/ACO_Addon/Models/Submarine/ACO_Submarine.fbx";
    private const string SubmarineAlbedo = "Assets/ACO_Addon/Models/Submarine/ACO_Submarine_Albedo.png";
    private const int SubSlcmBanks = 3;
    private const int SubSalvoSlots = 6;
    private const float SubHullHealth = 12000f;   // 6-205: 6000의 2배(3배는 너무 길었다) — 20% 아래에서 충각 돌격
    private const float SubBowOffset = 131f;      // 뱃머리 끝 z(모델)
    // 무장 자리(모델 기준 — aco_submarine_model.py의 VLS_Z · SAIL_CIWS · REAR_FLAK_Z · REAR_CIWS_Z · SIDE_GUN_Z와 같다)
    private static readonly Vector3[] SubVlsSpots = { new Vector3(0f, 8.92f, 80f), new Vector3(0f, 8.92f, 46f) };   // 앞 갑판 가운데 앞뒤로(갑판 높이)
    private const float SubVlsScale = 1.4f;                                         // 16셀 긴 VLS 7 x 27m
    private static readonly Vector3 SubSailCiws = new Vector3(0f, 30f, 12f);       // 함교 지붕
    private static readonly Vector3 SubRearFlak = new Vector3(0f, 9.72f, -32f);    // 함교 뒤 갑판 받침 위
    private static readonly Vector3 SubRearCiws = new Vector3(0f, 8.77f, -66f);
    private static readonly Vector3 SubSideGun = new Vector3(32f, 5.74f, -52f);    // 보조 동체 윗면(오른쪽 — 왼쪽은 x 반대)
    private const float SubGunScale = 1.3f;
    private const float SubMiniScale = 1.5f;
    private static readonly Vector3 SubScrew = new Vector3(32f, 0f, -127f);        // 스크류 축 중심(오른쪽)
    private static readonly Vector3 SubMarkerPoint = new Vector3(0f, 18f, 10f);
    // 무인기 사출구(6-217 — aco_submarine_model.py의 DRONE_HATCH_*와 같다): 오른쪽 사출구 가운데 · 덮개 경첩(바깥 가장자리). 왼쪽은 x 반대
    private static readonly Vector3 SubDroneHatch = new Vector3(4.3f, 8.69f, -49f);
    private static readonly Vector3 SubDroneHinge = new Vector3(5.8f, 8.69f, -49f);
    private const float SubDroneLaunchUp = 4f;      // 무인기가 나타나는 높이(사출구 위, m)
    private const float SubDroneLaunchYaw = 35f;    // 바깥으로 비스듬히(도)
    public const int SonarRingLayer = 5;            // 소나 원 = UI 레이어(6-238)

    // 잠수함 사출 전투 무인기(6-217)
    public const string SubDronePrefab = "NPC_SubDrone";
    public const int SubDronePoolSize = 6;
    private const float SubDroneHealth = 120f;     // 신병 배율 0.66 → 약 80(전투기 200의 40%)
    private const string SubDroneFbx = "Assets/ACO_Addon/Models/SubDrone/ACO_SubDrone.fbx";
    private const string SubDroneAlbedo = "Assets/ACO_Addon/Models/SubDrone/ACO_SubDrone_Albedo.png";
    // 항모 화재 자리(HomeBase 기준 — 함교 옆 · 선미 갑판 · 선수 뱃전)
    private static readonly Vector3[] SubCarrierFire = { new Vector3(14f, 24f, 40f), new Vector3(-20f, 21f, -50f), new Vector3(6f, 12f, 120f) };

    /// U# 스크립트는 프로그램 에셋(Udon/*.asset)이 있어야 컴포넌트에 프로그램이 붙는다. 새 스크립트라 없으면 만들고 컴파일한다.
    public static void EnsureSubmarinePrograms()
    {
        bool made = false;
        made |= EnsureProgram("Assets/ACO_Addon/Scripts/GroundUnits/ACO_Slcm.cs", "Assets/ACO_Addon/Udon/ACO_Slcm.asset");
        made |= EnsureProgram("Assets/ACO_Addon/Scripts/GroundUnits/ACO_Submarine.cs", "Assets/ACO_Addon/Udon/ACO_Submarine.asset");
        made |= EnsureProgram("Assets/ACO_Addon/Scripts/Core/ACO_PhaseSubmarine.cs", "Assets/ACO_Addon/Udon/ACO_PhaseSubmarine.asset");
        if (!made) { return; }
        AssetDatabase.SaveAssets();
        UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
    }

    private static Material SubmarineMaterial()
    {
        var importer = AssetImporter.GetAtPath(SubmarineAlbedo) as TextureImporter;
        if (importer != null && importer.maxTextureSize != 4096)
        {
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }
        string path = $"{RailgunGeneratedDir}/ACO_Submarine.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_Submarine" };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SubmarineAlbedo));
        m.SetColor("_Color", Color.white);
        m.SetFloat("_Metallic", 0.2f);
        m.SetFloat("_Glossiness", 0.4f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static BoxCollider SubHitbox(GameObject root, Vector3 center, Vector3 size)
    {
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
        box.enabled = false;
        return box;
    }

    /// 함선 갑판 포대 하나(받침 기준 조준 — 부상 · 잠항 때 선체와 함께 기운다). yaw = 쉬는 방향.
    private static ACO_AAGunSite SubDeckGun(string prefab, string name, Transform parts, Vector3 pos, float yaw, float scale)
    {
        GameObject gun = NewTurret(prefab, name, parts, pos, Quaternion.Euler(0f, yaw, 0f), scale);
        ACO_AAGunSite aa = gun.GetComponent<ACO_AAGunSite>();
        aa.MountRelativeAim = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(aa);
        return aa;
    }

    public static GameObject BuildSubmarine()
    {
        EnsurePrefabFolder();
        EnsureSubmarinePrograms();
        ConfigureModelImporter(SubmarineFbx);
        Mesh hullMesh = FbxMesh(SubmarineFbx, "SB_Hull");
        Mesh screwMesh = FbxMesh(SubmarineFbx, "SB_Screw_R");
        Bounds hb = hullMesh.bounds;
        if (hb.max.z < 125f || hb.min.z > -125f || hb.max.y < 28f || hb.size.x < 70f)
        {
            throw new InvalidOperationException($"[ACO] ACO_Submarine.fbx의 축이나 크기가 예상과 다릅니다(선체 {hb.min} ~ {hb.max}). Tools/aco_submarine_model.py를 실행하세요. 프리팹은 만들지 않았습니다.");
        }
        var mats = new[] { SubmarineMaterial() };
        string path = PathOf(SubmarinePrefab);

        var root = new GameObject(SubmarinePrefab);
        root.layer = LayerHitbox;
        // 히트박스(루트 — 선체 SaccTarget): 중앙 동체 · 함교 · 보조 동체 둘. 사이(날개 위 빈 곳)는 비어 있다
        var hulls = new List<Collider>
        {
            SubHitbox(root, new Vector3(0f, -3.5f, -1f), new Vector3(28f, 25f, 264f)),
            SubHitbox(root, new Vector3(0f, 19.5f, 10f), new Vector3(9f, 21f, 34f)),
            SubHitbox(root, new Vector3(SubScrew.x, -1f, -67f), new Vector3(18f, 18f, 113f)),
            SubHitbox(root, new Vector3(-SubScrew.x, -1f, -67f), new Vector3(18f, 18f, 113f)),
        };

        // 모델(충돌 없음)
        Transform model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        LsVisual(model, "SB_Hull", hullMesh, mats, Vector3.zero, Quaternion.identity);
        Transform screwR = LsVisual(model, "SB_Screw_R", screwMesh, mats, SubScrew, Quaternion.identity).transform;
        Transform screwL = LsVisual(model, "SB_Screw_L", screwMesh, mats, new Vector3(-SubScrew.x, SubScrew.y, SubScrew.z), Quaternion.Euler(0f, 0f, 25f)).transform;
        // 무인기 사출구 덮개(6-217): 오른쪽 메시를 왼쪽엔 Y 180° 돌려 놓는다(경첩 = 바깥 가장자리) · 무인기가 나타나는 자리
        Mesh doorMesh = FbxMesh(SubmarineFbx, "SB_DroneDoor_R");
        var doors = new Transform[2];
        var launches = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            doors[i] = LsVisual(model, i == 0 ? "SB_DroneDoor_R" : "SB_DroneDoor_L", doorMesh, mats, new Vector3(SubDroneHinge.x * side, SubDroneHinge.y, SubDroneHinge.z),
                i == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f)).transform;
            launches[i] = new GameObject(i == 0 ? "DroneLaunch_R" : "DroneLaunch_L").transform;
            launches[i].SetParent(root.transform, false);
            launches[i].localPosition = new Vector3(SubDroneHatch.x * side, SubDroneHatch.y + SubDroneLaunchUp, SubDroneHatch.z);
            launches[i].localRotation = Quaternion.Euler(0f, SubDroneLaunchYaw * side, 0f);
        }

        GameObject hullMarker = NewLockMarker(root.transform, "LockMarker", LayerEnemyGround, 16f);
        hullMarker.transform.localPosition = SubMarkerPoint;
        hullMarker.SetActive(false);
        Transform markerPoint = new GameObject("MarkerPoint").transform;
        markerPoint.SetParent(root.transform, false);
        markerPoint.localPosition = new Vector3(0f, 34f, 10f);

        // 무장 부위 7
        Transform parts = new GameObject("Parts").transform;
        parts.SetParent(root.transform, false);
        var weapons = new List<UdonSharpBehaviour>();
        for (int i = 0; i < SubVlsSpots.Length; i++)
        {
            GameObject vls = NewTurret(TurretVls16, "VLS16_" + i, parts, SubVlsSpots[i], Quaternion.identity, SubVlsScale);
            weapons.Add(vls.GetComponent<ACO_SAMSite>());
        }
        weapons.Add(SubDeckGun(TurretCiws, "SailCIWS", parts, SubSailCiws, 0f, SubGunScale));
        weapons.Add(SubDeckGun(TurretFlak, "DeckFlak", parts, SubRearFlak, 0f, SubGunScale));
        weapons.Add(SubDeckGun(TurretCiws, "DeckCIWS", parts, SubRearCiws, 180f, SubGunScale));
        var minis = new List<ACO_MiniRailgun>();
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            GameObject gun = NewTurret(TurretMini, i == 0 ? "RailgunR" : "RailgunL", parts, new Vector3(SubSideGun.x * side, SubSideGun.y, SubSideGun.z),
                Quaternion.Euler(0f, 90f * side, 0f), SubMiniScale);
            ACO_MiniRailgun mini = gun.GetComponent<ACO_MiniRailgun>();
            mini.Site.MountRelativeAim = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mini.Site);
            minis.Add(mini);
            weapons.Add(mini.Site);
        }
        SubMiniFx(root.transform, minis);

        // SLCM 풀
        Transform pool = new GameObject("Slcms").transform;
        pool.SetParent(root.transform, false);
        var slcms = new ACO_Slcm[SubSlcmBanks * SubSalvoSlots];
        for (int i = 0; i < slcms.Length; i++) { slcms[i] = BuildSlcm(pool, i); }

        // 연출
        Transform fx = new GameObject("Fx").transform;
        fx.SetParent(root.transform, false);
        Color foamColor = new Color(0.93f, 0.96f, 0.98f);
        ParticleSystem foam = CreateBlastSystem(fx, "Foam", new Vector2(12f, 16f), Vector2.zero, new Vector2(7f, 11f), 0f, 3000, foamColor, foamColor, 0.82f);
        SetBlastShape(foam, ParticleSystemShapeType.Sphere, 3f);
        ACO_FxBuilder.StyleTorpedoFoam(foam);   // 부상 · 잠항 물보라 = 함선 항적 거품
        // 어뢰 항적: 함선 항적과 같은 오브젝트(폭 10m) — 어뢰 기록마다 2칸, 잠수함이 어뢰 머리로 옮긴다
        var torpWakes = new ACO_WaterFx[ACO_PhaseSubmarine.MAX_TORPEDO * 2];
        for (int i = 0; i < torpWakes.Length; i++)
        {
            GameObject w = ACO_WaterFxBuilder.AddWakeSized(fx, 0f, -2f, 4f, 10f);
            w.name = "TorpWake_" + i;
            torpWakes[i] = w.GetComponent<ACO_WaterFx>();
            ACO_FxBuilder.StyleTorpedoWake(torpWakes[i]);   // 초공동 어뢰 빠르기에 맞춘 간격 · 수명(6-212)
            w.SetActive(false);
        }
        // 기존 미사일(LASM) 폭발 · 물기둥, 미사일 격추 파편 — 켤 때 재생, 차례로 돌려 쓴다
        GameObject lasm = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("ACO_Missile_LASM"));
        if (lasm == null) { throw new InvalidOperationException("[ACO] ACO_Missile_LASM 프리팹이 없습니다 — 잠수함 SLCM · 어뢰 연출에 씁니다. 프리팹은 만들지 않았습니다."); }
        GameObject[] blasts = SubFxCopies(fx, lasm.transform.Find("Explosion"), "Blast", 8);
        GameObject[] waters = SubFxCopies(fx, lasm.transform.Find("Fx_Water"), "Water", 8);
        var wrecks = new ParticleSystem[4];
        GameObject wreckPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("ACO_WreckFx_Missile"));
        for (int i = 0; i < wrecks.Length && wreckPrefab != null; i++)
        {
            var w = (GameObject)PrefabUtility.InstantiatePrefab(wreckPrefab, fx);
            w.name = "Wreck_" + i;
            wrecks[i] = w.GetComponent<ParticleSystem>();
        }
        AudioSource launch = CreateBlastSound(fx, "LaunchSound", "Assets/ACO_Addon/Audio/Generated/ACO_Explosion_Small.wav", 400f, 12000f);
        GameObject ring = BuildSonarRing(fx);
        var sonarMarker = new GameObject("SonarMarker");
        sonarMarker.transform.SetParent(fx, false);
        sonarMarker.SetActive(false);

        // 항모 화재(자리는 ACO_Submarine이 모항 기준으로 옮긴다)
        Transform carrier = new GameObject("CarrierFire").transform;
        carrier.SetParent(root.transform, false);
        var fires = new GameObject[SubCarrierFire.Length];
        for (int i = 0; i < fires.Length; i++)
        {
            fires[i] = CreateFortressSmoke(carrier, "Fire_" + i, Vector3.zero, 9f, 60f);
            fires[i].SetActive(false);
        }

        // SAV 격침 애니메이션이 "Effects"를 켠다. 그 밖의 연출은 DeathEffects(ACO_Submarine이 켠다).
        var effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        var deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        CreateFortressSmoke(deathFx.transform, "Fire_Sail", new Vector3(0f, 28f, 10f), 12f, 70f);
        CreateFortressSmoke(deathFx.transform, "Fire_Deck", new Vector3(0f, 10f, 60f), 14f, 70f);
        CreateFortressSmoke(deathFx.transform, "Fire_Stern", new Vector3(0f, 8f, -60f), 12f, 60f);
        CreateFortressSmoke(deathFx.transform, "Fire_EngineR", new Vector3(SubScrew.x, 8f, -100f), 10f, 55f);
        CreateFortressSmoke(deathFx.transform, "Fire_EngineL", new Vector3(-SubScrew.x, 8f, -100f), 10f, 55f);
        deathFx.SetActive(false);

        ACO_FortressBlast blast = CreateFortressBlast(root.transform);
        // 항적: 선미 거품은 두 스크류까지 덮고(폭 80m), 선수 물결은 중앙 동체 폭으로 줄인다
        GameObject wake = ACO_WaterFxBuilder.AddWakeSized(root.transform, 0f, -130f, SubBowOffset, 80f);
        Transform bowWave = wake.transform.Find("BowWave");
        if (bowWave != null)
        {
            bowWave.localPosition = new Vector3(0f, 0.3f, SubBowOffset - 12f);
            ParticleSystem bowPs = bowWave.GetComponent<ParticleSystem>();
            ParticleSystem.ShapeModule bowShape = bowPs.shape;
            bowShape.radius = 14f;
            ParticleSystem.MainModule bowMain = bowPs.main;
            bowMain.startSize = new ParticleSystem.MinMaxCurve(16f, 24f);
        }

        AddTargetAnimator(root);
        SaccTarget core = root.AddUdonSharpComponent<SaccTarget>();
        ACO_Warship ship = root.AddUdonSharpComponent<ACO_Warship>();
        ACO_Submarine sub = root.AddUdonSharpComponent<ACO_Submarine>();
        core.Health = SubHullHealth;
        core.ArmorStrength = 1f;
        core.RespawnDelay = 0f;
        core.ExplodeOther = new UdonSharpBehaviour[] { ship, sub };

        ship.TargetHealth = core;
        ship.Parts = weapons.ToArray();
        ship.TargetName = "Submarine";
        ship.MarkerLabel = "SSGN";
        ship.ScoreValue = 5000;
        ship.HighValue = true;
        ship.PartHullDamage = 0f;   // 무장은 위협만 — 부숴도 선체는 깎이지 않는다
        ship.PartSplashScale = SeaPartSplash;
        ship.CoreSplashImmune = false;
        ship.CruiseSpeed = 0f;
        ship.BobAngle = 0f;
        ship.ExternalPose = true;
        ship.PoseOwner = sub;
        ship.ShowOnDeath = new GameObject[0];
        ship.HideOnDeath = new GameObject[0];
        ship.DisableCollidersOnDeath = hulls.ToArray();

        sub.Warship = ship;
        sub.TargetHealth = core;
        sub.Model = model.gameObject;
        sub.HullColliders = hulls.ToArray();
        sub.HullMarkers = new[] { hullMarker };
        sub.MarkerPoint = markerPoint;
        sub.Weapons = weapons.ToArray();
        // 부위마다 파괴 연출(DeathEffects) — 잠항할 때 같이 꺼서 부상 때 다시 재생되지 않게(6-213)
        sub.WeaponDeathFx = new GameObject[sub.Weapons.Length];
        for (int i = 0; i < sub.Weapons.Length; i++)
        {
            Transform de = sub.Weapons[i] != null ? sub.Weapons[i].transform.Find("DeathEffects") : null;
            if (de == null) { throw new InvalidOperationException("[ACO] 잠수함 무장 부위 " + (sub.Weapons[i] != null ? sub.Weapons[i].name : i.ToString()) + "에 DeathEffects가 없습니다. 프리팹은 만들지 않았습니다."); }
            sub.WeaponDeathFx[i] = de.gameObject;
        }
        sub.Slcms = slcms;
        sub.SalvoSlots = SubSalvoSlots;
        sub.SonarRing = ring.transform;
        sub.SonarMarker = sonarMarker;
        sub.Plume = null;
        sub.FxBlasts = blasts;
        sub.FxWaters = waters;
        sub.FxWrecks = wrecks;
        sub.TorpWakes = torpWakes;
        sub.Foam = foam;
        sub.Blast = blast;
        sub.LaunchSound = launch;
        sub.CarrierFire = fires;
        sub.CarrierFireOffset = (Vector3[])SubCarrierFire.Clone();
        sub.DeathFx = deathFx;
        sub.SlcmInterceptTime = 2.5f;   // 순양함 CIWS는 거드는 정도 — 1.8초는 순양함 쪽에서 오면 3발 중 2발을 막았다
        sub.BowOffset = SubBowOffset;
        sub.Screws = new[] { screwR, screwL };
        sub.DroneDoors = doors;
        sub.DroneLaunchPoints = launches;
        foreach (ACO_Slcm s in slcms) { s.Owner = sub; }

        return SaveAndCleanup(root, path);
    }

    /// 잠수함 사출 전투 무인기(6-217): NPC_Fighter의 프리팹 변형 — 전투기 튜닝(비행 · 무장 · 플레어 · 연출)을 그대로 받고 모델 · 체력 · 판정 상자 · 무장 자리만 무인기로.
    /// 미션 생성기는 Drone 표시가 있는 전투기를 일반 배치에 쓰지 않고 잠수함 사출(OBJ_SUB_DRONE)에만 쓴다. 생성기 풀에 SubDronePoolSize기(Setup Check).
    public static GameObject BuildSubDrone()
    {
        EnsurePrefabFolder();
        GameObject fighter = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("NPC_Fighter"));
        if (fighter == null) { throw new InvalidOperationException("[ACO] NPC_Fighter 프리팹이 없습니다 — 무인기는 그 변형입니다. 프리팹은 만들지 않았습니다."); }
        ConfigureModelImporter(SubDroneFbx);
        Mesh mesh = FbxMesh(SubDroneFbx, "SD_Body");
        Material mat = SubDroneMaterial();
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fighter);
        try
        {
            inst.name = SubDronePrefab;
            ACO_NPCFighterAI ai = inst.GetComponent<ACO_NPCFighterAI>();
            Transform model = inst.transform.Find("Model");
            if (ai == null || model == null) { throw new InvalidOperationException("[ACO] NPC_Fighter에 ACO_NPCFighterAI · Model이 없습니다. 프리팹은 만들지 않았습니다."); }
            foreach (Transform c in model) { if (c.GetComponentInChildren<Renderer>(true) != null) { c.gameObject.SetActive(false); } }   // 전투기 모델(숙련도별 · 함재기)만 끈다 — 엔진 소리 등은 둔다
            GameObject drone = LsVisual(model, "Drone", mesh, new[] { mat }, Vector3.zero, Quaternion.identity);
            drone.layer = model.gameObject.layer;
            var one = new[] { drone };
            ai.RookieVisuals = one;
            ai.VeteranVisuals = one;
            ai.AceVisuals = one;
            ai.CarrierVisuals = new GameObject[0];
            ai.Drone = true;
            ai.MarkerLabel = "UAV";
            ai.BaseHealth = SubDroneHealth;
            if (ai.TargetHealth != null) { ai.TargetHealth.Health = SubDroneHealth; PrefabUtility.RecordPrefabInstancePropertyModifications(ai.TargetHealth); }
            if (ai.MissileLauncher != null)
            {
                ai.MissileLauncher.NumMissiles = 2;
                Transform[] lps = ai.MissileLauncher.LaunchPoints;
                for (int i = 0; lps != null && i < lps.Length; i++) { if (lps[i]) { lps[i].localPosition = new Vector3(i % 2 == 0 ? -1f : 1f, -0.25f, 0.3f); } }
                PrefabUtility.RecordPrefabInstancePropertyModifications(ai.MissileLauncher);
            }
            if (ai.Gun != null) { ai.Gun.transform.localPosition = new Vector3(0f, -0.15f, 3f); }
            BoxCollider box = inst.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.size = new Vector3(4.6f, 1.4f, 6.6f);
                box.center = new Vector3(0f, 0.1f, 0f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(box);
            }
            ai.SpawnFx = BuildDroneBooster(inst.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
            return PrefabUtility.SaveAsPrefabAsset(inst, PathOf(SubDronePrefab));
        }
        finally { Object.DestroyImmediate(inst); }
    }

    /// 사출 부스터(6-217): 꼬리 불꽃(로컬) + LASM 연기(월드) — 부활할 때 AI가 처음부터 튼다(SpawnFx), 1.6초 뒤 멈추고 연기는 수명대로.
    private static ParticleSystem BuildDroneBooster(Transform root)
    {
        Color hot = new Color(1f, 0.75f, 0.35f);
        Color dim = new Color(0.9f, 0.35f, 0.1f);
        ParticleSystem flame = CreateBlastSystem(root, "Booster", new Vector2(0.06f, 0.12f), Vector2.zero, new Vector2(0.9f, 1.5f), 0f, 80, hot, dim, 1f);
        flame.transform.localPosition = new Vector3(0f, 0f, -3.5f);
        ParticleSystem.MainModule fm = flame.main;
        fm.duration = 1.6f;
        fm.loop = false;
        fm.playOnAwake = false;
        fm.simulationSpace = ParticleSystemSimulationSpace.Local;
        ParticleSystem.EmissionModule fe = flame.emission;
        fe.rateOverTime = 60f;
        SetBlastShape(flame, ParticleSystemShapeType.Sphere, 0.2f);
        GameObject lasm = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("ACO_Missile_LASM"));
        Transform srcTrail = lasm != null ? lasm.transform.Find("Trail") : null;
        if (srcTrail != null)
        {
            Transform smoke = Object.Instantiate(srcTrail, flame.transform);
            smoke.name = "Smoke";
            smoke.localPosition = Vector3.zero;
            smoke.localRotation = srcTrail.localRotation;
            ParticleSystem sp = smoke.GetComponent<ParticleSystem>();
            sp.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule sm = sp.main;
            sm.loop = false;
            sm.duration = 1.6f;
            sm.playOnAwake = false;
            // LASM 연기(수명 ~20초 · 크기 ~65m — 대함 미사일 기준)는 무인기 부스터엔 너무 짙고 오래 남았다: 수명 0.3배 · 크기 0.35배 · 방출 25 → 15(사용자 "부스터연기 좀줄이고")
            sm.startLifetimeMultiplier = sm.startLifetimeMultiplier * 0.3f;
            sm.startSizeMultiplier = sm.startSizeMultiplier * 0.35f;
            ParticleSystem.EmissionModule se = sp.emission;
            se.enabled = true;   // LASM 연기는 방출이 꺼진 채 저장돼 있다(미사일 스크립트가 켠다)
            se.rateOverTimeMultiplier = 15f;
        }
        return flame;
    }

    private static Material SubDroneMaterial()
    {
        var importer = AssetImporter.GetAtPath(SubDroneAlbedo) as TextureImporter;
        if (importer != null && (importer.filterMode != FilterMode.Point || importer.mipmapEnabled))
        {
            importer.filterMode = FilterMode.Point;   // 색 견본 칸(16px)이 섞이지 않게
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        string path = $"{RailgunGeneratedDir}/ACO_SubDrone.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_SubDrone" };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SubDroneAlbedo));
        m.SetColor("_Color", Color.white);
        m.SetFloat("_Metallic", 0.3f);
        m.SetFloat("_Glossiness", 0.45f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 소형 레일건 탄 · 공중 폭발 파티클(랜스 포대와 같은 값 — 두 문이 함께 쓴다).
    private static void SubMiniFx(Transform root, List<ACO_MiniRailgun> minis)
    {
        var fx = new GameObject("MiniFx").transform;
        fx.SetParent(root, false);
        Color white = new Color(0.8f, 0.95f, 1f);
        ParticleSystem shell = CreateBlastSystem(fx, "Shell", new Vector2(3f, 3f), Vector2.zero, new Vector2(12f, 12f), 0f, 16, white, white, 1f);
        SetBlastShape(shell, ParticleSystemShapeType.Sphere, 0.1f);
        ParticleSystemRenderer shellRenderer = shell.GetComponent<ParticleSystemRenderer>();
        shellRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        shellRenderer.velocityScale = MiniShellStretch;
        shellRenderer.lengthScale = 1f;
        ParticleSystem burst = CreateBlastSystem(fx, "Burst", new Vector2(3f, 3.8f), new Vector2(4f, 22f), new Vector2(70f, 140f), 0f, 130, new Color(1f, 0.55f, 0.18f), new Color(0.12f, 0.1f, 0.1f), 0.95f);
        SetBlastShape(burst, ParticleSystemShapeType.Sphere, 125f);
        ParticleSystem flash = CreateBlastSystem(fx, "BurstFlash", new Vector2(0.4f, 0.4f), Vector2.zero, new Vector2(420f, 420f), 0f, 6, new Color(1f, 0.95f, 0.8f), new Color(1f, 0.6f, 0.2f), 1f);
        SetBlastShape(flash, ParticleSystemShapeType.Sphere, 0.1f);
        foreach (ACO_MiniRailgun mini in minis) { LinkMiniFx(mini, shell, burst, flash); }
        ACO_FxBuilder.StyleMiniFx(minis.ToArray());
    }

    /// SLCM 한 발: 루트 = 체력 · 히트박스(SaccTarget 규칙), 탄체 · 불꽃 · 연기(Model), 공중 락온 마커, 저공 물보라.
    private static ACO_Slcm BuildSlcm(Transform parent, int index)
    {
        var go = new GameObject("SLCM_" + index);
        go.layer = LayerHitbox;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -60f, 0f);
        CapsuleCollider col = go.AddComponent<CapsuleCollider>();
        col.direction = 2;
        col.radius = 3f;
        col.height = 14f;
        col.enabled = false;

        Transform model = new GameObject("Model").transform;
        model.SetParent(go.transform, false);
        // 기존 대함미사일(LASM)의 탄체 · 엔진 불꽃 · 발사음과 연기 궤적을 그대로 쓴다(크기만 SlcmModelScale배 — 순항미사일)
        GameObject lasm = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("ACO_Missile_LASM"));
        Transform srcMesh = lasm != null ? lasm.transform.Find("Model/Mesh") : null;
        Transform srcTrail = lasm != null ? lasm.transform.Find("Trail") : null;
        if (srcMesh == null || srcTrail == null) { throw new InvalidOperationException("[ACO] ACO_Missile_LASM의 Model/Mesh · Trail이 없습니다. 프리팹은 만들지 않았습니다."); }
        Transform mesh = Object.Instantiate(srcMesh, model);
        mesh.name = "Mesh";
        mesh.localPosition = srcMesh.localPosition * SlcmModelScale;
        mesh.localRotation = Quaternion.identity;   // LASM은 Model(90°) · Mesh(270°)가 서로 상쇄 — 루트 기준 회전이 없다
        mesh.localScale = srcMesh.localScale * SlcmModelScale;
        // 연기 궤적은 탄체(Model) 밖, SLCM 루트 아래 — ACO_Slcm이 나는 동안만 뿜고 멈추면 남은 연기는 수명대로 사라진다
        Transform trail = Object.Instantiate(srcTrail, go.transform);
        trail.name = "Trail";
        trail.localPosition = srcTrail.localPosition * SlcmModelScale;
        trail.localRotation = srcTrail.localRotation;
        // LASM 연기는 방출이 꺼진 채 저장돼 있고 미사일 스크립트가 발사 때 켠다 — SLCM은 방출을 켜 두고 재생(Play · Stop)으로 다룬다(ClientSim: 꺼져 있어 연기가 없었다)
        ParticleSystem trailPs = trail.GetComponent<ParticleSystem>();
        ParticleSystem.EmissionModule trailEmission = trailPs.emission;
        trailEmission.enabled = true;
        ParticleSystem.MainModule trailMain = trailPs.main;
        trailMain.playOnAwake = false;   // 물속에 대기할 때 뿜지 않게
        model.gameObject.SetActive(false);

        GameObject marker = NewLockMarker(go.transform, "LockMarker", LayerEnemyAir, 8f);
        marker.SetActive(false);

        AddTargetAnimator(go);
        SaccTarget hp = go.AddUdonSharpComponent<SaccTarget>();
        ACO_Slcm slcm = go.AddUdonSharpComponent<ACO_Slcm>();
        hp.Health = 200f;   // 일반 미사일(100) 2발
        hp.ArmorStrength = 1f;
        hp.RespawnDelay = 0f;
        hp.ExplodeOther = new UdonSharpBehaviour[] { slcm };
        slcm.TargetHealth = hp;
        slcm.Model = model.gameObject;
        slcm.Trail = trailPs;
        slcm.LockMarker = marker;
        slcm.Hitbox = col;
        ACO_WaterFxBuilder.AddMissileSpray(go.transform);
        return slcm;
    }

    private const float SlcmModelScale = 2f;   // LASM 탄체(약 3.3m) → 약 6.5m

    /// 기존 미사일 연출 오브젝트(폭발 · 물기둥)의 복사 count개(꺼 둠 — 켤 때 재생). 범위 표시 구(ExplodeRadius)는 끈다(미사일 범위 피해 표시라 여기선 크기가 맞지 않는다).
    private static GameObject[] SubFxCopies(Transform parent, Transform source, string name, int count)
    {
        if (source == null) { throw new InvalidOperationException($"[ACO] ACO_Missile_LASM에 {name} 연출이 없습니다. 프리팹은 만들지 않았습니다."); }
        var list = new GameObject[count];
        for (int i = 0; i < count; i++)
        {
            Transform t = Object.Instantiate(source, parent);
            t.name = name + "_" + i;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            Transform radius = t.Find("ExplodeRadius");
            if (radius != null) { radius.gameObject.SetActive(false); }
            t.gameObject.SetActive(false);
            list[i] = t.gameObject;
        }
        return list;
    }

    /// 소나 원: 반경 1m 고리(로컬 XZ, 72점, 선 폭 25m) — ACO_Submarine이 반경만큼 키운다.
    private static GameObject BuildSonarRing(Transform parent)
    {
        var go = new GameObject("SonarRing");
        go.layer = SonarRingLayer;   // 6-238: UI 레이어 — HUD 표시라 관전 카메라 · VRChat 카메라(사진)에 안 찍힌다
        go.transform.SetParent(parent, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        const int n = 72;
        line.positionCount = n;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            line.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
        }
        line.widthMultiplier = 25f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = FortressMaterial("ACO_SonarRing", new Color(1f, 0.55f, 0.15f), 0f, 0f, new Color(2f, 0.9f, 0.2f));
        go.SetActive(false);
        return go;
    }
}
