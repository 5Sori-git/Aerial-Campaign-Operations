using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 랜스 포대 NPC_Railgun(지상 보스): 모델과 재질은 ACO_LanceModel.cs가 입힌다(ACO_Lance.fbx). 기초(Pad)만 코드로 만든다.
/// 구조(루트 = 함선 규칙 — 코어 SaccTarget·히트박스·Animator는 루트, 포구 방향 +Z, 바닥 y = 0):
///  - Visual_Base(기초·포탑 받침) · Visual_Mount(포신 받침·벽) : 움직이지 않는 모델 — 지우고 다른 모델을 넣어도 된다.
///  - BarrelPivot(포신 회전축) → Visual_Barrel(모델), ChargeStrip(충전 빛), Muzzle(포구)
///  - Armor_L·Armor_R(장갑 회전축)·Armor_Rear(고정) → Visual(모델). 좌우 장갑은 벽과 지붕이 한 덩어리라 바깥으로 눕으면 위쪽도 열린다 — 탑어택 무장이 코어로 내리꽂는다.
///  - Core(노심 + 원자로 구조물, 마커), Hull(충돌 — 자체 SaccTarget이 피해를 흡수), Parts(방공 12곳, 냉각기 4곳).
/// 다른 모델로 바꿀 때: Visual_* 자식만 갈아 끼우고, 크기가 다르면 Hull 상자와 BarrelPivot·Muzzle·Core 자리를 맞춘다. 로직은 이 Transform들만 쓴다.
public static partial class ACO_PrefabBuilder
{
    private const string RailgunTexDir = "Assets/ACO_Addon/Textures/Railgun";
    private const string RailgunGeneratedDir = "Assets/ACO_Addon/Generated";

    private static readonly Vector3 RailgunCorePos = new Vector3(0f, 46f, -68f);
    /// 포대 배치 판 번호 — 프리팹이 이보다 낮으면 Bake가 기초를 넓히고 포대를 벌린다.
    private const int RailgunLayoutVersion = 2;
    private const float RailgunPadRadius = 530f;

    /// 방공 포대 자리의 반경: 안팎 두 줄로 엇갈려 놓는다 — 폭탄 한 발에 여러 곳이 쓸리지 않게.
    private static float RailgunRingRadius(int i) { return i % 2 == 0 ? 363f : 462f; }

    private static Texture2D LoadRailgunTexture(string file, bool normal)
    {
        string path = $"{RailgunTexDir}/{file}.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null && System.IO.File.Exists(path))
        {
            AssetDatabase.ImportAsset(path);
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
        }
        if (importer != null && normal && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material RailgunMaterial(string name, string albedo, bool normal, Color color, float metallic, float smoothness)
    {
        string path = $"{RailgunGeneratedDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) { return m; }
        m = new Material(Shader.Find("Standard")) { name = name };
        m.SetColor("_Color", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        if (albedo != null) { m.SetTexture("_MainTex", LoadRailgunTexture(albedo, false)); }
        if (normal)
        {
            m.SetTexture("_BumpMap", LoadRailgunTexture("ACO_Railgun_Steel_N", true));
            m.EnableKeyword("_NORMALMAP");
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// 레이저는 경고라 안개·구름에 가려지면 안 된다(ACO/Laser).
    private static Material RailgunBeamMaterial(string name, Color color)
    {
        string path = $"{RailgunGeneratedDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) { return m; }
        m = new Material(Shader.Find("ACO/Laser")) { name = name };
        m.SetColor("_Color", color);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// 옆면이 깊다 — 기초는 가장 높은 땅에 얹히므로 낮은 쪽 틈을 가린다(FindRailgunSite)
    private static Mesh BuildRailgunPadMesh()
    {
        var bag = new MeshBag { UvScale = 40f };
        bag.Prism(new Vector3(0f, 0f, 0f), RailgunPadRadius - 20f, RailgunPadRadius, -90f, 8f, 48);
        bag.Prism(new Vector3(0f, 0f, 0f), 140f, 150f, 8f, 16f, 24);
        return bag.ToMesh("ACO_Railgun_Pad");
    }

    private static ACO_FortressPart AddRailgunCooler(Transform parts, string name, Vector3 pos)
    {
        var root = new GameObject(name);
        root.layer = LayerHitbox;
        root.transform.SetParent(parts, false);
        root.transform.localPosition = pos;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(50f, 62f, 50f);
        box.center = new Vector3(0f, 31f, 0f);
        box.enabled = false;
        GameObject visual = AddFortressMesh(root.transform, "Visual", null, null, Vector3.zero);
        AddFortressMesh(visual.transform, "Hazard", null, null, Vector3.zero);
        GameObject smoke = CreateFortressSmoke(root.transform, "Smoke", new Vector3(0f, 30f, 0f), 18f, 60f);
        smoke.SetActive(false);
        GameObject marker = NewLockMarker(root.transform, PartMarkerName, LayerEnemyGround, 26f);
        marker.transform.localPosition = new Vector3(0f, 30f, 0f);
        marker.SetActive(false);
        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        AddTargetAnimator(root);
        SaccTarget health = root.AddUdonSharpComponent<SaccTarget>();
        ACO_FortressPart part = root.AddUdonSharpComponent<ACO_FortressPart>();
        health.Health = 700f;
        health.ArmorStrength = 1f;
        health.RespawnDelay = 0f;
        health.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { part };
        part.TargetHealth = health;
        part.ScoreValue = 800;
        part.MarkerLabel = "CLR";
        part.LockMarkers = new[] { marker };
        part.Hitboxes = new Collider[] { box };
        part.HideOnDeath = new[] { visual };
        part.ShowOnDeath = new[] { smoke };
        return part;
    }

    /// 원형 기초에 내접하는 정사각형 변 — 모서리가 기초 밖으로 나가지 않는다. 둘을 45도 엇갈려 둘레를 덮는다.
    private const float RailgunPadBox = 730f;

    private static void AddRailgunPadDiagonal(Transform hull)
    {
        var go = new GameObject("PadDiagonal");
        go.layer = LayerHitbox;
        go.transform.SetParent(hull, false);
        go.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, -20f, 0f);
        box.size = new Vector3(RailgunPadBox, 56f, RailgunPadBox);
    }

    private static ACO_AAGunSite AddRailgunGun(Transform parts, string name, string prefab, Vector3 pos, float yaw, List<UdonSharp.UdonSharpBehaviour> list)
    {
        ACO_AAGunSite aa = NewTurret(prefab, name, parts, pos, Quaternion.Euler(0f, yaw, 0f), 2.5f).GetComponent<ACO_AAGunSite>();
        list.Add(aa);
        return aa;
    }

    private static LineRenderer CreateRailgunBeam(Transform parent, string name, Color color, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = width;
        line.endWidth = width;
        line.sharedMaterial = RailgunBeamMaterial(name == "WarnBeam" ? "ACO_RailgunWarn" : "ACO_RailgunBeam", color);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private static AudioSource CreateRailgunSound(Transform parent, string name, string clip, float minDistance, float maxDistance, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        AudioSource a = go.AddComponent<AudioSource>();
        a.clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/Generated/{clip}.wav");
        a.playOnAwake = false;
        a.loop = loop;
        a.spatialBlend = 1f;
        a.dopplerLevel = 0f;
        a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = minDistance;
        a.maxDistance = maxDistance;
        a.priority = 16;
        return a;
    }

    public static GameObject BuildRailgun()
    {
        EnsurePrefabFolder();
        GameObject vlsMissile = LoadOrBuildVlsMissile();
        Material concrete = RailgunMaterial("ACO_RailgunConcrete", "ACO_Railgun_Concrete", false, Color.white, 0f, 0.15f);
        Material hazard = RailgunMaterial("ACO_RailgunHazard", "ACO_Railgun_Hazard", false, Color.white, 0.2f, 0.3f);
        Mesh padMesh = SaveFortressMesh(BuildRailgunPadMesh());

        var root = new GameObject("NPC_Railgun");
        root.layer = LayerHitbox;

        // 코어 히트박스(장갑이 열렸을 때만). SaccTarget 규칙상 루트에.
        BoxCollider coreCol = root.AddComponent<BoxCollider>();
        coreCol.size = LanceCoreHitSize;
        coreCol.center = RailgunCorePos;
        coreCol.enabled = false;

        // 고정 모델 — Visual_* 아래만 바꾸면 다른 모델이 된다
        AddFortressMesh(root.transform, "Visual_Base", padMesh, concrete, Vector3.zero);
        GameObject mount = AddFortressMesh(root.transform, "Visual_Mount", null, null, Vector3.zero);
        var hazardRing = new MeshBag { UvScale = 10f };
        hazardRing.Prism(Vector3.zero, 141f, 143f, 16f, 17f, 16);
        AddFortressMesh(mount.transform, "HazardRing", SaveFortressMesh(hazardRing.ToMesh("ACO_Railgun_HazardRing")), hazard, Vector3.zero);

        // 포신
        var pivot = new GameObject("BarrelPivot").transform;
        pivot.SetParent(root.transform, false);
        pivot.localPosition = new Vector3(0f, 50f, 38f);
        AddFortressMesh(pivot, "Visual_Barrel", null, null, Vector3.zero);
        GameObject strip = AddFortressMesh(pivot, "ChargeStrip", null, null, Vector3.zero);
        Transform muzzle = NewChild(pivot.gameObject, "Muzzle", new Vector3(0f, 0f, 295f));

        // 코어 + 장갑(좌우는 벽과 지붕이 한 덩어리 — 바깥으로 누우면 위도 열린다)
        var coreRoot = new GameObject("Core").transform;
        coreRoot.SetParent(root.transform, false);
        coreRoot.localPosition = RailgunCorePos;
        AddFortressMesh(coreRoot, "CoreSphere", null, null, Vector3.zero);
        AddFortressMesh(coreRoot, "Visual_Frame", null, null, Vector3.zero);
        GameObject coreMarker = NewLockMarker(coreRoot, "AAMTarget", LayerEnemyGround, 26f);
        coreMarker.SetActive(false);

        var armorL = new GameObject("Armor_L").transform;
        armorL.SetParent(root.transform, false);
        armorL.localPosition = LanceHingeL;
        AddFortressMesh(armorL, "Visual", null, null, Vector3.zero);
        var armorR = new GameObject("Armor_R").transform;
        armorR.SetParent(root.transform, false);
        armorR.localPosition = LanceHingeR;
        AddFortressMesh(armorR, "Visual", null, null, Vector3.zero);
        var armorRear = new GameObject("Armor_Rear").transform;
        armorRear.SetParent(root.transform, false);
        armorRear.localPosition = new Vector3(0f, 28f, -122f);
        AddFortressMesh(armorRear, "Visual", null, null, Vector3.zero);

        // 충돌: 자체 SaccTarget이 피해를 흡수한다. 코어 칸 위는 비워 두고, 닫힌 동안만 ArmorClosed가 덮는다.
        var hull = new GameObject("Hull");
        hull.layer = LayerHitbox;
        hull.transform.SetParent(root.transform, false);
        var hullCols = new List<Collider>();
        foreach (Vector3[] b in new[]
        {
            new[] { new Vector3(0f, -20f, 0f), new Vector3(RailgunPadBox, 56f, RailgunPadBox) },
            new[] { new Vector3(0f, 22f, 0f), new Vector3(230f, 14f, 230f) },
            new[] { new Vector3(-40f, 45f, 40f), new Vector3(18f, 34f, 100f) },
            new[] { new Vector3(40f, 45f, 40f), new Vector3(18f, 34f, 100f) },
            new[] { LanceBulkheadCenter, LanceBulkheadSize },
        })
        {
            BoxCollider c = hull.AddComponent<BoxCollider>();
            c.center = b[0];
            c.size = b[1];
            hullCols.Add(c);
        }
        AddRailgunPadDiagonal(hull.transform);
        var closed = new GameObject("ArmorClosed");
        closed.layer = LayerHitbox;
        closed.transform.SetParent(hull.transform, false);
        BoxCollider closedCol = closed.AddComponent<BoxCollider>();
        closedCol.center = LanceClosedCenter;
        closedCol.size = LanceClosedSize;
        hullCols.Add(closedCol);
        AddTargetAnimator(hull);
        SaccTarget armor = hull.AddUdonSharpComponent<SaccTarget>();
        armor.Health = 1000000f;
        armor.RespawnDelay = 0f;
        armor.DisableBulletHitEvent = true;

        // 부위: 방공 12곳(기초 둘레, 앞쪽은 포신 자리라 비운다) + 냉각기 4곳
        var body = new GameObject("Parts").transform;
        body.SetParent(root.transform, false);
        var defense = new List<UdonSharp.UdonSharpBehaviour>();
        float[] ringAngles = { 40f, 66f, 92f, 118f, 144f, 170f, 190f, 216f, 242f, 268f, 294f, 320f };
        for (int i = 0; i < ringAngles.Length; i++)
        {
            float a = ringAngles[i] * Mathf.Deg2Rad;
            var p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * RailgunRingRadius(i) + Vector3.up * 8f;
            if (i % 3 == 0) { AddRailgunGun(body, "Flak_" + i, TurretFlak, p, ringAngles[i], defense); }
            else if (i % 3 == 1) { AddRailgunGun(body, "CIWS_" + i, TurretCiws, p, ringAngles[i], defense); }
            else { defense.Add(NewTurret(TurretVls, "VLS_" + i, body, p, Quaternion.identity, 3f).GetComponent<ACO_SAMSite>()); }
        }
        var coolers = new ACO_FortressPart[LanceCoolerNames.Length];
        for (int i = 0; i < coolers.Length; i++) { coolers[i] = AddRailgunCooler(body, LanceCoolerNames[i], LanceCoolerPlaces[i]); }
        var partList = new List<UdonSharp.UdonSharpBehaviour>(defense);
        partList.AddRange(coolers);

        // 격파 연출·소리·빔
        var effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        var deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        CreateFortressSmoke(deathFx.transform, "Fire_Core", RailgunCorePos, 40f, 120f);
        deathFx.SetActive(false);
        LineRenderer warn = CreateRailgunBeam(root.transform, "WarnBeam", new Color(1f, 0.05f, 0.03f), 5f);
        LineRenderer beam = CreateRailgunBeam(root.transform, "FireBeam", new Color(0.75f, 0.95f, 1f), 1f);
        ParticleSystem flash = CreateBlastSystem(muzzle, "MuzzleFlash", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 500f), 0f, 2, new Color(0.85f, 0.95f, 1f), new Color(0.3f, 0.6f, 1f), 1f);
        SetBlastShape(flash, ParticleSystemShapeType.Sphere, 0.1f);

        AddTargetAnimator(root);
        SaccTarget core = root.AddUdonSharpComponent<SaccTarget>();
        ACO_Warship ship = root.AddUdonSharpComponent<ACO_Warship>();
        ACO_Railgun railgun = root.AddUdonSharpComponent<ACO_Railgun>();

        core.Health = 3000f;
        core.ArmorStrength = 1f;
        core.RespawnDelay = 0f;
        core.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { ship, railgun };

        ship.TargetHealth = core;
        ship.Parts = partList.ToArray();
        ship.TargetName = "Lance Battery";
        ship.MarkerLabel = "LNC";
        ship.ScoreValue = 5000;
        ship.HighValue = true;
        ship.PartHullDamage = 0f;
        ship.CoreSplashImmune = true;   // 보스 코어는 직접 맞혀야만(범위 피해 면역)
        ship.CruiseSpeed = 0f;
        ship.CruiseRadius = 0f;
        ship.BobAngle = 0f;
        ship.ExternalPose = true;
        ship.PoseOwner = railgun;
        ship.ShowOnDeath = new[] { deathFx };
        ship.HideOnDeath = new GameObject[0];
        hullCols.Add(coreCol);
        ship.DisableCollidersOnDeath = hullCols.ToArray();

        railgun.LayoutVersion = RailgunLayoutVersion;
        railgun.Warship = ship;
        railgun.TargetHealth = core;
        railgun.AirDefense = defense.ToArray();
        railgun.Coolers = coolers;
        railgun.BarrelPivot = pivot;
        railgun.Muzzle = muzzle;
        railgun.ChargeGlow = strip.GetComponent<MeshRenderer>();
        railgun.ArmorColliders = new Collider[] { closedCol };
        railgun.CoreHitbox = coreCol;
        railgun.CoreMarkers = new[] { coreMarker };
        railgun.Core = coreRoot;
        railgun.WarnBeam = warn;
        railgun.FireBeam = beam;
        railgun.MuzzleFlash = flash;
        railgun.DamageSource = ((ACO_AAGunSite)defense[0]).gameObject;
        railgun.ChargeSound = CreateRailgunSound(root.transform, "ChargeSound", "ACO_Railgun_Charge_Loop", 600f, 18000f, true);
        railgun.FireSound = CreateRailgunSound(root.transform, "FireSound", "ACO_Railgun_Fire", 4000f, 40000f, false);
        railgun.Blast = CreateFortressBlast(root.transform);
        AddRailgunHorizon(root.transform, railgun);
        AddRailgunMinis(root.transform, railgun, ship);
        ACO_FxBuilder.StyleRailgun(railgun);
        ApplyLanceModel(root);

        return SaveAndCleanup(root, $"{PrefabDir}/NPC_Railgun.prefab");
    }

    private static ParticleSystem CreateHorizonSystem(Transform parent, string name, float life, float sizeX, float sizeY, Color c0, Color c1, Material mat)
    {
        ParticleSystem ps = CreateBlastSystem(parent, name, new Vector2(life, life), Vector2.zero, new Vector2(sizeX, sizeX), 0f, 4, c0, c1, 1f);
        ParticleSystem.MainModule main = ps.main;
        main.startSize3D = true;
        main.startSizeX = sizeX;
        main.startSizeY = sizeY;
        main.startSizeZ = sizeX;
        main.startRotation = 0f;
        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.maxParticleSize = 1000f;   // 판이 화면보다 커져도 줄이지 않는다
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// 지평선 섬광: 땅 위로 넓게 퍼지는 흰 빛과 오래 남는 주황 빛, 멀리서 오는 폭음.
    private static void AddRailgunHorizon(Transform root, ACO_Railgun railgun)
    {
        string path = $"{RailgunGeneratedDir}/ACO_HorizonFlash.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("ACO/HorizonFlash")) { name = "ACO_HorizonFlash" };
            AssetDatabase.CreateAsset(mat, path);
        }
        var group = new GameObject("Horizon").transform;
        group.SetParent(root, false);
        railgun.HorizonFlash = CreateHorizonSystem(group, "HorizonFlash", 0.9f, 14000f, 5000f, new Color(1f, 0.97f, 0.9f), new Color(1f, 0.7f, 0.4f), mat);
        railgun.HorizonGlow = CreateHorizonSystem(group, "HorizonGlow", 5f, 9000f, 2600f, new Color(1f, 0.55f, 0.2f), new Color(0.5f, 0.12f, 0.04f), mat);
        AudioSource boom = CreateRailgunSound(group, "HorizonSound", "ACO_Distant_Boom", 1f, 100f, false);
        boom.spatialBlend = 0f;   // 멀리서 울리는 소리 — 어디서 들어도 같다
        boom.volume = 0.85f;
        railgun.HorizonSound = boom;
    }

    private const float MiniShellStretch = 0.04f;

    private static void AddRailgunMinis(Transform root, ACO_Railgun railgun, ACO_Warship ship)
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
        Transform parts = root.Find("Parts");
        var minis = new List<ACO_MiniRailgun>();
        var defense = new List<UdonSharp.UdonSharpBehaviour>(railgun.AirDefense);
        var all = new List<UdonSharp.UdonSharpBehaviour>(ship.Parts);
        for (int i = 0; i < LanceMiniPlaces.Length; i++)
        {
            GameObject part = NewTurret(TurretMini, "MiniRailgun_" + i, parts, LanceMiniPlaces[i], Quaternion.identity, 3f);
            ACO_MiniRailgun mini = part.GetComponent<ACO_MiniRailgun>();
            LinkMiniFx(mini, shell, burst, flash);
            minis.Add(mini);
            defense.Add(mini.Site);
            all.Add(mini.Site);
        }
        railgun.MiniGuns = minis.ToArray();
        railgun.AirDefense = defense.ToArray();
        ship.Parts = all.ToArray();
    }

    /// 탄·폭발 파티클은 소형 레일건끼리 함께 쓴다 — 랜스 포대의 MiniFx에 있어 인스턴스에서 잇는다.
    private static void LinkMiniFx(ACO_MiniRailgun mini, ParticleSystem shell, ParticleSystem burst, ParticleSystem flash)
    {
        mini.ShellParticle = shell;
        mini.BurstParticle = burst;
        mini.BurstFlash = flash;
        PrefabUtility.RecordPrefabInstancePropertyModifications(mini);
    }

    /// 소형 레일건 프리팹: 대공포 틀(탐지·조준·체력)에서 기총을 뺀 것 + 레이저·소리.
    private static GameObject BuildMiniTurret()
    {
        var spec = new GunSpec
        {
            BodySize = new Vector3(3f, 1.4f, 3f), BarrelLength = 10f, BarrelThickness = 0.55f,
            Health = 260f, Armor = 1f, Score = 350, Label = "RGN",
            DetectRange = 6500f, LoseTargetRange = 7500f, YawRate = 50f, PitchRate = 40f, TrackingLag = 0.2f, AimError = 0.2f,
        };
        GameObject part = CreateAAGunObject(TurretMini, spec);
        ACO_AAGunSite aa = part.GetComponent<ACO_AAGunSite>();
        // 기총을 뺀다 — 사격은 ACO_MiniRailgun이 한다
        Object.DestroyImmediate(aa.Gun.gameObject);
        aa.Gun = null;
        if (aa.TargetHealth != null) { aa.BaseHealth = aa.TargetHealth.Health; }
        ACO_MiniRailgun mini = part.AddUdonSharpComponent<ACO_MiniRailgun>();
        mini.Site = aa;
        aa.AimLeadTime = mini.LeadTime;
        mini.Muzzle = NewChild(aa.TurretPitch.gameObject, "Muzzle", new Vector3(0f, 0f, spec.BarrelLength));
        mini.FireSound = CreateRailgunSound(part.transform, "FireSound", "ACO_Railgun_MiniFire", 300f, 7000f, false);
        mini.BurstSound = CreateRailgunSound(part.transform, "BurstSound", "ACO_Explosion_Small", 400f, 6000f, false);
        mini.Laser = CreateMiniLaser(part.transform);
        AddZoneShells(part.transform, mini);
        ApplyMiniModel(part);
        return SaveAndCleanup(part, PathOf(TurretMini));
    }

    private const int MiniZoneCount = 6;   // ACO_MiniRailgun.MAX_ZONES와 같다
    private const string ZoneShellMaterialPath = "Assets/ACO_Addon/Generated/ACO_ZoneShell.mat";

    /// 폭발 지역 껍질 머티리얼(ACO/ZoneShell) — 없으면 만든다. 값은 셰이더 기본값(색·가장자리·일렁임은 머티리얼에서 고친다).
    private static Material ZoneShellMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(ZoneShellMaterialPath);
        if (m != null) { return m; }
        Shader sh = Shader.Find("ACO/ZoneShell");
        if (sh == null) { throw new System.InvalidOperationException("[ACO] 셰이더 ACO/ZoneShell을 찾지 못했습니다 — 셰이더 컴파일 오류를 확인하세요."); }
        m = new Material(sh) { name = "ACO_ZoneShell" };
        AssetDatabase.CreateAsset(m, ZoneShellMaterialPath);
        return m;
    }

    /// 소형 레일건에 폭발 지역 껍질(지름 1 구 × 지역 칸 수)을 단다. 이미 있으면 그대로 다시 잇는다(여러 번 불러도 같다).
    private static bool AddZoneShells(Transform root, ACO_MiniRailgun mini)
    {
        Transform holder = root.Find("ZoneShells");
        bool changed = false;
        if (holder == null)
        {
            holder = new GameObject("ZoneShells").transform;
            holder.SetParent(root, false);
            changed = true;
        }
        Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        Material mat = ZoneShellMaterial();
        var shells = new Transform[MiniZoneCount];
        for (int k = 0; k < MiniZoneCount; k++)
        {
            Transform s = holder.Find("Shell_" + k);
            if (s == null)
            {
                s = new GameObject("Shell_" + k).transform;
                s.SetParent(holder, false);
                s.gameObject.AddComponent<MeshFilter>();
                MeshRenderer nr = s.gameObject.AddComponent<MeshRenderer>();
                nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                nr.receiveShadows = false;
                changed = true;
            }
            s.GetComponent<MeshFilter>().sharedMesh = sphere;
            s.GetComponent<MeshRenderer>().sharedMaterial = mat;
            s.gameObject.SetActive(false);
            shells[k] = s;
        }
        if (mini.ZoneShells == null || mini.ZoneShells.Length != MiniZoneCount || System.Array.IndexOf(mini.ZoneShells, null) >= 0) { changed = true; }
        mini.ZoneShells = shells;
        EditorUtility.SetDirty(mini);
        return changed;
    }

    /// Setup Check: 소형 레일건 프리팹(NPC_Turret_RailgunMini)에 폭발 지역 껍질을 단다 — 랜스 포대·육상전함 주포는 그 프리팹의 인스턴스라 함께 바뀐다.
    public static void PlaceMiniZoneShells()
    {
        string path = PathOf(TurretMini);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        EditLancePrefab(path, root =>
        {
            ACO_MiniRailgun mini = root.GetComponent<ACO_MiniRailgun>();
            if (mini == null) { return; }
            AddZoneShells(root.transform, mini);
            Debug.Log("[ACO] 소형 레일건: 폭발 지역 껍질 6개를 달았습니다(랜스 포대·육상전함 주포 함께).");
        });
    }

    /// 먼저 만든 소형 레일건 넷을 프리팹 인스턴스로 바꾼다. 자리와 크기는 그대로 둔다.
    public static void ConvertRailgunMinis()
    {
        string path = PathOf("NPC_Railgun");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_Railgun railgun = root.GetComponent<ACO_Railgun>();
            ACO_Warship ship = root.GetComponent<ACO_Warship>();
            if (railgun == null || ship == null || railgun.MiniGuns == null || railgun.MiniGuns.Length == 0) { return; }
            ACO_MiniRailgun first = railgun.MiniGuns[0];
            if (first == null || PrefabUtility.IsPartOfPrefabInstance(first.gameObject)) { return; }
            ParticleSystem shell = first.ShellParticle;
            ParticleSystem burst = first.BurstParticle;
            ParticleSystem flash = first.BurstFlash;
            if (shell != null) { shell.GetComponent<ParticleSystemRenderer>().velocityScale = MiniShellStretch; }
            var defense = new List<UdonSharp.UdonSharpBehaviour>(railgun.AirDefense);
            var parts = new List<UdonSharp.UdonSharpBehaviour>(ship.Parts);
            for (int i = 0; i < railgun.MiniGuns.Length; i++)
            {
                ACO_MiniRailgun old = railgun.MiniGuns[i];
                Transform t = old.transform;
                GameObject part = NewTurret(TurretMini, old.name, t.parent, t.localPosition, t.localRotation, t.localScale.x);
                part.transform.SetSiblingIndex(t.GetSiblingIndex());
                ACO_MiniRailgun fresh = part.GetComponent<ACO_MiniRailgun>();
                LinkMiniFx(fresh, shell, burst, flash);
                int d = defense.IndexOf(old.Site);
                if (d >= 0) { defense[d] = fresh.Site; }
                int p = parts.IndexOf(old.Site);
                if (p >= 0) { parts[p] = fresh.Site; }
                if (old.Laser != null) { Object.DestroyImmediate(old.Laser.gameObject); }
                if (old.FireSound != null) { Object.DestroyImmediate(old.FireSound.gameObject); }
                if (old.BurstSound != null) { Object.DestroyImmediate(old.BurstSound.gameObject); }
                Object.DestroyImmediate(old.gameObject);
                railgun.MiniGuns[i] = fresh;
            }
            railgun.AirDefense = defense.ToArray();
            ship.Parts = parts.ToArray();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[ACO] 소형 레일건을 {TurretMini} 프리팹의 인스턴스로 바꿨습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static LineRenderer CreateMiniLaser(Transform parent)
    {
        LineRenderer line = CreateRailgunBeam(parent, "WarnBeam", new Color(1f, 0.05f, 0.03f), 1f);
        line.gameObject.name = "Laser";
        return line;
    }

}
