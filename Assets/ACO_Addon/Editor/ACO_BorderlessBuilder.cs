using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using UdonSharpEditor;
using ACO;

/// 레이저 에이스(NPC_Borderless)와 탄도미사일 사일로 연출(ACO_IcbmSite).
///  - NPC_Borderless: 일반 NPC 전투기 + 체력 5000(플레이어가 많아도 금방 끝나지 않게 — 절반 위에서 한 번에 0이 되지 않는다)·플레어 12번 + 자식 ACO_Borderless
///    (기수 렌즈, 빔 LineRenderer, 불티, 소리). 락온 마커 이름을 AceLockMarker로 바꿔 SAV 애니메이터가 건드리지 않게 하고 에이스 스크립트가 켜고 끈다.
///    모델은 Models/LaserAce/ACO_LaserAce.fbx(Tools/aco_laser_ace_model.py — 동체·포드 앞머리·렌즈 구, 길이 약 23.5m). 렌즈는 렌즈 포탑(ACO_AAGunSite —
///    폭격기 방어 기총처럼 붙인 부위, 좌우 한계 30도)이 돌린다. 판정은 동체 상자 + 날개 상자. 미션 생성기 풀(UnitPoolRoot) 아래에 하나 둔다.
///  - IcbmSite: 레이저 에이스 페이즈 모듈의 자식. 사일로(받침·문 둘)·미사일 메시(Models/Icbm/ACO_Icbm.fbx — Tools/aco_icbm_model.py)와 불꽃·연기·자폭·지평선 섬광·소리.
public static partial class ACO_PrefabBuilder
{
    public const string BorderlessPrefab = "NPC_Borderless";
    private const string BorderlessFbx = "Assets/ACO_Addon/Models/LaserAce/ACO_LaserAce.fbx";
    private const string BorderlessAlbedo = "Assets/ACO_Addon/Models/LaserAce/ACO_LaserAce_Albedo.png";
    // Tools/aco_laser_ace_model.py의 POD_Y·POD_Z1·HEAD_LEN과 같아야 한다
    private static readonly Vector3 BorderlessHeadPos = new Vector3(0f, 1.95f, 1.9f);
    private const float BorderlessHeadToLens = 0.9f;
    private const string IcbmFbx = "Assets/ACO_Addon/Models/Icbm/ACO_Icbm.fbx";
    private const string IcbmAlbedo = "Assets/ACO_Addon/Models/Icbm/ACO_Icbm_Albedo.png";

    /// Setup Check laser-warn-tone-off-1: 이미 있는 NPC_Borderless에서 비춰질 때 도는 경보음(LaserWarn)을 뺀다 — 음성 경고만 남긴다.
    public static void RemoveLaserWarnTone()
    {
        string path = $"{PrefabDir}/{BorderlessPrefab}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            bool changed = false;
            foreach (ACO_Borderless ace in root.GetComponentsInChildren<ACO_Borderless>(true))
            {
                if (ace.WarnSound == null) { continue; }
                GameObject go = ace.WarnSound.gameObject;
                ace.WarnSound = null;
                EditorUtility.SetDirty(ace);
                if (go.name == "LaserWarn" && go.GetComponents<Component>().Length <= 2) { Object.DestroyImmediate(go); }   // 빌더가 만든 경보음 오브젝트만
                changed = true;
            }
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[ACO] NPC_Borderless: 비춰질 때 경보음을 뺐습니다(조종석 음성 LASER WARNING만).");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static GameObject BuildBorderless()
    {
        EnsurePrefabFolder();
        GameObject missile = LoadOrBuildMissile();
        GameObject root = CreateFighterObject(BorderlessPrefab, missile);
        ACO_NPCFighterAI ai = root.GetComponent<ACO_NPCFighterAI>();
        SaccTarget health = root.GetComponent<SaccTarget>();
        health.Health = 5000f;
        ai.BaseHealth = 5000f;   // 숙련도 배율을 받지 않는다
        ai.Flares.FlareSalvos = 12;
        ai.AceFlareChance = 1f;
        ai.Flight.MaxBoost = 2.2f;   // 2단계 급가속 이탈(ACO_Borderless.EscapeBoost) 상한 — 동기화 예측도 이 값까지 본다

        // 기체 모델(Models/LaserAce/ACO_LaserAce.fbx — Tools/aco_laser_ace_model.py): 동체 메시를 Model에, 포드 앞머리(고정)는 그 자식.
        // 렌즈는 렌즈 포탑(아래)의 TurretPitch에 붙는다. Model은 HideOnDeath에 들어 있어 격추되면 앞머리·렌즈 포탑도 함께 숨는다.
        Transform model = root.transform.Find("Model");
        Material mat = null;
        bool hasFbx = AssetDatabase.LoadAssetAtPath<GameObject>(BorderlessFbx) != null;
        if (!hasFbx) { Debug.LogError($"[ACO] {BorderlessFbx}가 없습니다 — Tools/aco_laser_ace_model.py를 실행하세요. 상자 모델로 만듭니다."); }
        if (model != null && hasFbx)
        {
            ConfigureModelImporter(BorderlessFbx);
            mat = BorderlessMaterial();
            model.localScale = Vector3.one;
            model.GetComponent<MeshFilter>().sharedMesh = FbxMesh(BorderlessFbx, "LaserAce_Body");
            model.GetComponent<MeshRenderer>().sharedMaterial = mat;
            BorderlessPart(model, "LaserAce_Head", FbxMesh(BorderlessFbx, "LaserAce_Head"), BorderlessHeadPos, mat);
        }

        // 판정: 동체(나셀·가운데 몸통 포함) 상자 + 날개 상자. SaccTarget과 같은 루트에 둔다(SAV 직격 판정이 부모를 타지 않는다)
        BoxCollider bodyBox = root.GetComponent<BoxCollider>();
        bodyBox.size = new Vector3(5.4f, 2.4f, 23f);
        bodyBox.center = new Vector3(0f, -0.2f, 0.9f);
        BoxCollider wingBox = root.AddComponent<BoxCollider>();
        wingBox.size = new Vector3(17.6f, 0.5f, 7f);
        wingBox.center = new Vector3(0f, 0.1f, -4.6f);
        ai.DisableCollidersOnDeath = new Collider[] { bodyBox, wingBox };

        // 기관포(오른쪽 LERX 뿌리), 미사일(날개 아래), 플레어(꼬리 아래)
        Transform gun = root.transform.Find("Gun");
        if (gun != null) { gun.localPosition = new Vector3(1.05f, 0.3f, 3.4f); }
        Transform lpL = root.transform.Find("MissileLauncher/LaunchPoint_L");
        Transform lpR = root.transform.Find("MissileLauncher/LaunchPoint_R");
        if (lpL != null) { lpL.localPosition = new Vector3(-5.2f, -0.15f, -3.2f); }
        if (lpR != null) { lpR.localPosition = new Vector3(5.2f, -0.15f, -3.2f); }
        if (ai.Flares != null && ai.Flares.FlareParticles != null) { ai.Flares.FlareParticles.transform.localPosition = new Vector3(0f, -0.3f, -10.6f); }

        // 락온 마커: SAV 애니메이터가 이름(AAMTarget)으로 켜고 끄므로 이름을 바꾼다 — 에이스 스크립트가 맡는다(ECM 정면 락온은 플레이어 무장이 묻는다)
        Transform marker = root.transform.Find("AAMTarget");
        if (marker != null)
        {
            marker.name = "AceLockMarker";
            SphereCollider mc = marker.GetComponent<SphereCollider>();
            if (mc != null) { mc.radius = 6f; }   // 큰 기체(길이 약 23m)
        }

        var aceGo = new GameObject("ACO_Borderless");
        aceGo.transform.SetParent(root.transform, false);
        ACO_Borderless ace = aceGo.AddUdonSharpComponent<ACO_Borderless>();
        ace.AI = ai;
        ace.TargetHealth = health;
        ace.LockMarker = marker != null ? marker.gameObject : null;
        ai.Ace = ace;
        ACO_VoiceLines.ApplyBorderless(ace);   // 등장·이탈·격추 음성(있으면)

        // ECM: 락온 제한(정면 원뿔 ±15도) — 루트(락온 마커의 부모)에 둔다. 플레이어 무장이 마커에서 부모 쪽으로 찾는다. 켜고 끄는 건 에이스 스크립트(2단계)
        ACO_LockGate gate = root.AddUdonSharpComponent<ACO_LockGate>();
        gate.Mode = ACO_LockGate.MODE_FRONT_CONE;
        gate.HalfAngle = 15f;
        gate.Facing = root.transform;
        gate.Active = false;
        ace.LockGate = gate;

        // 렌즈 포탑: 렌즈 구 중심(앞머리에서 0.9m 앞). 모델이 없으면 상자 기수 끝 조금 아래
        Transform turretParent = model != null ? model : aceGo.transform;
        Vector3 lensPos = hasFbx ? BorderlessHeadPos + new Vector3(0f, 0f, BorderlessHeadToLens) : new Vector3(0f, -0.2f, 3.2f);
        ACO_AAGunSite turret = CreateLaserTurret(turretParent, lensPos);
        ace.Turret = turret;
        if (hasFbx) { BorderlessPart(turret.TurretPitch, "LaserAce_Lens", FbxMesh(BorderlessFbx, "LaserAce_Head/LaserAce_Lens"), Vector3.zero, mat); }
        // 빛나는 렌즈 유리: 렌즈 원판(반지름 0.34m, 렌즈 중심 앞 0.52~0.57m) 앞에 납작한 발광 원판
        var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "LensGlow";
        Object.DestroyImmediate(glow.GetComponent<Collider>());
        glow.transform.SetParent(turret.TurretPitch, false);
        glow.transform.localPosition = hasFbx ? new Vector3(0f, 0f, 0.56f) : Vector3.zero;
        glow.transform.localScale = hasFbx ? new Vector3(0.62f, 0.62f, 0.08f) : Vector3.one * 0.5f;
        glow.GetComponent<MeshRenderer>().sharedMaterial = LaserLensMaterial();
        glow.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ConfigureBorderlessGun(turret, null);
        // 비춰질 때 경보음은 넣지 않는다 — 조종석 음성 "LASER WARNING"으로 충분하다(경보 루프가 시끄러웠다)

        return SaveAndCleanup(root, $"{PrefabDir}/{BorderlessPrefab}.prefab");
    }

    /// 렌즈 포탑의 레이저 기총: 기총(ACO_NPCGun)에 레이저 부품(빨강, 3.6km, 지형에서 끊김·불티)을 단다. 피해는 기총 규칙 —
    /// 조준이 1.2도 안에 0.2초 머물면 0.25초마다 6(예전 빔 축 판정과 비슷한 세기), 끊지 않고 계속 쏜다, 탄속을 아주 빠르게 해 리드 없이 표적 자체를 겨눈다.
    /// 쏘는 동안 도는 소리는 hum(없으면 새로). 이미 있으면 값만 다시 맞춘다.
    private static void ConfigureBorderlessGun(ACO_AAGunSite turret, AudioSource hum)
    {
        ACO_NPCGun gun = turret.Gun;
        if (gun == null)
        {
            var gunObj = new GameObject("Gun");
            gunObj.transform.SetParent(turret.TurretPitch, false);
            gunObj.transform.localPosition = new Vector3(0f, 0f, 0.6f);
            gun = gunObj.AddUdonSharpComponent<ACO_NPCGun>();
            gun.Muzzle = gunObj.transform;
            turret.Gun = gun;
            EditorUtility.SetDirty(turret);
        }
        gun.EntityControl = turret.GetComponent<SaccEntity>();
        gun.ShooterFlight = null;
        gun.InheritShooterVelocity = false;
        gun.MaxRange = 3500f;
        gun.MuzzleVelocity = 1000000f;
        gun.FireStartAngle = 8f;
        gun.EffectiveAngle = 1.2f;
        gun.DwellTime = 0.2f;
        gun.BurstDuration = 0f;
        gun.BurstCooldown = 0f;
        gun.DamagePerTick = 6f;
        gun.MinDamageInterval = 0.25f;
        gun.MaxDamageInterval = 0.25f;
        gun.ShellHitRadius = 0f;
        gun.Spread = 0f;
        if (hum == null) { hum = LaserSound(gun.transform, "LaserHum", "ACO_Railgun_Charge_Loop", true, 1f, 40f, 3000f, 1.7f); }
        gun.FireSound = hum;
        AttachLaserModule(gun, new Color(1f, 0.85f, 0.75f), new Color(1f, 0.3f, 0.2f), 1.6f, 3600f, 1, true);
        EditorUtility.SetDirty(gun);
    }

    /// Setup Check laser-gun-module-1: 이미 있는 NPC_Borderless — 렌즈 포탑에 레이저 기총(레이저 부품)을 단다. 예전 에이스 스크립트의 빔(LineRenderer)은 지우고 소리는 기총으로 옮긴다.
    public static void MigrateBorderlessGun()
    {
        string path = $"{PrefabDir}/{BorderlessPrefab}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            bool changed = false;
            foreach (ACO_Borderless ace in root.GetComponentsInChildren<ACO_Borderless>(true))
            {
                if (ace.Turret == null) { continue; }
                LineRenderer oldBeam = ace.GetComponent<LineRenderer>();
                if (oldBeam != null) { Object.DestroyImmediate(oldBeam); }
                Transform oldFx = ace.transform.Find("LaserFx");
                if (oldFx != null) { Object.DestroyImmediate(oldFx.gameObject); }
                Transform oldHum = ace.transform.Find("LaserHum");
                AudioSource hum = oldHum != null ? oldHum.GetComponent<AudioSource>() : null;
                ConfigureBorderlessGun(ace.Turret, null);
                if (hum != null) { Object.DestroyImmediate(hum.gameObject); }   // 소리는 기총 쪽에 새로(포구에서 난다)
                EditorUtility.SetDirty(ace);
                changed = true;
            }
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[ACO] NPC_Borderless: 렌즈 포탑에 레이저 기총(ACO_NPCGun + ACO_NPCLaser)을 달았습니다 — 빔·소리·피해를 기총이 맡습니다.");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 렌즈 포탑: 대공포(ACO_AAGunSite)를 폭격기 방어 기총처럼 기체에 붙인다 — 콜라이더·락온 마커·상자 모델은 지운다(맞으면 에이스 본체가 맞는다). 기총은 레이저 기총으로(ConfigureBorderlessGun).
    /// 기체 기준으로 재고(MountRelativeAim), 좌우 30도·고각 ±15도(기수 앞 원뿔) 안의 표적만 노리며 천천히 돈다(12도/초 — 옆으로 크게 움직이면 빔이 뒤처진다).
    /// 리드 없이 표적 자체를 겨누고, 조준 흔들림(AimError)이 빔 흔들림이다(플레이어 ECM 구역에선 더 흔들린다). 켜고 끄는 건 ACO_Borderless가 한다.
    private static ACO_AAGunSite CreateLaserTurret(Transform parent, Vector3 localPos)
    {
        var spec = new GunSpec
        {
            BodySize = new Vector3(0.2f, 0.2f, 0.2f), BarrelLength = 0.5f, BarrelThickness = 0.1f,
            Health = 100f, Armor = 1f, Score = 0, Label = "LSR",
            DetectRange = 3500f, LoseTargetRange = 3600f, YawRate = 12f, PitchRate = 12f, TrackingLag = 0f, AimError = 1.5f,
            MaxRange = 3500f, MuzzleVelocity = 900f, FireStartAngle = 8f, EffectiveAngle = 2.5f, DwellTime = 0.5f,
            BurstDuration = 1f, BurstCooldown = 1f, DamagePerTick = 0f, MinDamageInterval = 0.15f, MaxDamageInterval = 0.5f,
            Spread = 0f, TracerRate = 0f,
        };
        GameObject go = CreateAAGunObject("LaserTurret", spec);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        ACO_AAGunSite aa = go.GetComponent<ACO_AAGunSite>();
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        foreach (string child in new[] { "AAMTarget", "Model" })
        {
            Transform c = go.transform.Find(child);
            if (c != null) { Object.DestroyImmediate(c.gameObject); }
        }
        Transform barrel = aa.TurretPitch.Find("Barrel");
        if (barrel != null) { Object.DestroyImmediate(barrel.gameObject); }
        if (aa.Gun != null) { aa.Gun.transform.localPosition = new Vector3(0f, 0f, 0.6f); }   // 렌즈 앞(기총은 ConfigureBorderlessGun이 레이저로)
        aa.HideOnDeath = new GameObject[0];
        aa.DisableCollidersOnDeath = new Collider[0];
        aa.TurretYaw.localPosition = Vector3.zero;
        aa.TurretPitch.localRotation = Quaternion.identity;
        aa.MountRelativeAim = true;
        aa.YawArc = 30f;
        aa.MinPitch = -15f;
        aa.MaxPitch = 15f;
        aa.MinTargetAltitude = 0f;
        aa.AimLeadTime = 0f;
        aa.AimErrorFrequency = 0.6f;
        aa.ScanInterval = 0.25f;
        aa.ReturnToRestDelay = 0.6f;
        return aa;
    }

    private static Transform BorderlessPart(Transform parent, string name, Mesh mesh, Vector3 pos, Material mat)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    /// 기체 재질: 아틀라스 텍스처(흰 동체 무늬 + 색 칸). 값은 언제나 이 함수가 정한다.
    private static Material BorderlessMaterial()
    {
        var importer = AssetImporter.GetAtPath(BorderlessAlbedo) as TextureImporter;
        if (importer != null && (importer.maxTextureSize < 2048 || importer.filterMode != FilterMode.Trilinear || importer.anisoLevel < 4))
        {
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
        string path = "Assets/ACO_Addon/Generated/ACO_LaserAceBody.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_LaserAceBody" };
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = Color.white;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BorderlessAlbedo));
        m.SetFloat("_Metallic", 0.15f);
        m.SetFloat("_Glossiness", 0.45f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material LaserLensMaterial()
    {
        string path = "Assets/ACO_Addon/Generated/ACO_LaserLens.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_LaserLens", color = new Color(1f, 0.35f, 0.25f) };
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, 0.3f, 0.2f) * 3f);
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    private static AudioSource LaserSound(Transform parent, string name, string clip, bool loop, float spatial, float minDistance, float maxDistance, float pitch)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        AudioSource a = go.AddComponent<AudioSource>();
        a.clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/Generated/{clip}.wav");
        a.playOnAwake = false;
        a.loop = loop;
        a.spatialBlend = spatial;
        a.dopplerLevel = 0f;
        a.rolloffMode = AudioRolloffMode.Logarithmic;
        a.minDistance = minDistance;
        a.maxDistance = maxDistance;
        a.pitch = pitch;
        return a;
    }

    // ───────────── 사일로·탄도미사일 ─────────────

    /// 페이즈 모듈 아래에 IcbmSite를 만든다(있으면 지우고 다시). 미사일 메시가 없으면 null.
    public static ACO_IcbmSite BuildIcbmSite(Transform parent)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(IcbmFbx) == null)
        {
            Debug.LogError($"[ACO] {IcbmFbx}가 없습니다 — Tools/aco_icbm_model.py를 실행하세요.");
            return null;
        }
        ConfigureModelImporter(IcbmFbx);
        Transform old = parent.Find("IcbmSite");
        if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); }
        var root = new GameObject("IcbmSite");
        Undo.RegisterCreatedObjectUndo(root, "ACO ICBM site");
        root.transform.SetParent(parent, false);
        ACO_IcbmSite site = UdonSharpUndo.AddComponent<ACO_IcbmSite>(root);
        Material mat = IcbmMaterial();

        var silo = new GameObject("SiloRoot");
        silo.transform.SetParent(root.transform, false);
        IcbmPart(silo.transform, "Icbm_Pad", Vector3.zero, mat);
        site.DoorL = IcbmPart(silo.transform, "Icbm_DoorL", new Vector3(-5f, 1.2f, 0f), mat);
        site.DoorR = IcbmPart(silo.transform, "Icbm_DoorR", new Vector3(5f, 1.2f, 0f), mat);
        site.SiloRoot = silo.transform;

        site.Missile = IcbmPart(root.transform, "Icbm_Missile", Vector3.zero, mat);
        ACO_FxBuilder.BuildIcbmFx(site);
        if (site.Flame != null) { ACO_EngineSounds.AddRocket(site.Flame.transform); }   // 불꽃이 켜진 동안(상승 중)만 — 10km까지(6-156)

        string flashPath = $"{RailgunGeneratedDir}/ACO_HorizonFlash.mat";
        Material flashMat = AssetDatabase.LoadAssetAtPath<Material>(flashPath);
        if (flashMat == null)
        {
            flashMat = new Material(Shader.Find("ACO/HorizonFlash")) { name = "ACO_HorizonFlash" };
            AssetDatabase.CreateAsset(flashMat, flashPath);
        }
        var horizon = new GameObject("Horizon").transform;
        horizon.SetParent(root.transform, false);
        site.HorizonFlash = CreateHorizonSystem(horizon, "HorizonFlash", 1.2f, 20000f, 9000f, new Color(1f, 0.97f, 0.9f), new Color(1f, 0.7f, 0.4f), flashMat);
        site.HorizonGlow = CreateHorizonSystem(horizon, "HorizonGlow", 8f, 14000f, 6000f, new Color(1f, 0.55f, 0.2f), new Color(0.5f, 0.12f, 0.04f), flashMat);

        AudioSource launch = CreateRailgunSound(root.transform, "LaunchSound", "ACO_Explosion_Big", 200f, 20000f, false);
        launch.pitch = 0.55f;
        site.LaunchSound = launch;
        AudioSource burst = CreateRailgunSound(root.transform, "SkyBurstSound", "ACO_Distant_Boom", 1f, 100f, false);
        burst.spatialBlend = 0f;
        site.SkyBurstSound = burst;
        AudioSource boom = CreateRailgunSound(root.transform, "HorizonSound", "ACO_Distant_Boom", 1f, 100f, false);
        boom.spatialBlend = 0f;
        boom.pitch = 0.7f;
        site.HorizonSound = boom;

        silo.SetActive(false);
        site.Missile.gameObject.SetActive(false);
        // 자식도 실행 취소에 올린다 — 안 올리면 Ctrl+Z 때 자식만 남고(dangling) 모듈의 Site 연결이 끊긴다
        foreach (Transform child in root.transform) { Undo.RegisterCreatedObjectUndo(child.gameObject, "ACO ICBM site"); }
        EditorUtility.SetDirty(site);
        return site;
    }

    private static Transform IcbmPart(Transform parent, string mesh, Vector3 pos, Material mat)
    {
        var go = new GameObject(mesh);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = FbxMesh(IcbmFbx, mesh);
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        return go.transform;
    }

    private static Material IcbmMaterial()
    {
        var importer = AssetImporter.GetAtPath(IcbmAlbedo) as TextureImporter;
        if (importer != null && importer.filterMode != FilterMode.Point)
        {
            importer.filterMode = FilterMode.Point;   // 색 칸이 번지지 않게
            importer.SaveAndReimport();
        }
        string path = $"{RailgunGeneratedDir}/ACO_Icbm.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_Icbm" };
            m.SetFloat("_Metallic", 0.1f);
            m.SetFloat("_Glossiness", 0.35f);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.GetTexture("_MainTex") == null)
        {
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(IcbmAlbedo));
            EditorUtility.SetDirty(m);
        }
        return m;
    }
}
