using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SaccFlightAndVehicles;
using ACO;

/// 특수무장 카탈로그: 무장 정의 프리팹(ACO_SpecialWeaponDef, Assets/ACO_Addon/SpecialWeapons) → 격납고 목록 + 기체 모듈.
///  - 예제(4AAM, UGB, LGB, RKT, LGR, 4AGM, LAGM, LASM, SCM, MMP, SASM, LAAM, QAAM, HVAA, HPAA, FLAK, CCAA, ADAM, ECM —값은 F/A-18 설정 기준, 미사일 모델 포함): 프리팹 생성 창의 "플레이어 특수무장"(폭탄·로켓 탄체와 함께 — 있는 예제도 다시 만든다).
///    메뉴 ACO → Weapons → Add Missing Special Weapon Examples는 없는 예제만 만든다(손본 예제는 그대로). 기체 셋업은 카탈로그가 비어 있으면 없는 것만 만든다.
///  - 격납고 목록: Bake마다 정의를 격납고(ACO_Loadout) 목록에 맞춘다(없는 ID는 끝에 더하고, 이름·설명·가격·아이콘은 정의 값).
///  - 기체 모듈: 기체 셋업(ACO_PlayerMissileSetup)과 셋업 검사 [고치기]가 특수무장 슬롯에 같은 ID 모듈이 없으면 정의의 모듈을 복제해 넣고
///    기체에 묶인 칸(발사 지점·소리·잔탄 글자·HUD 막대)을 채운다. 예전 SAV 폭탄/로켓 모듈(같은 ID)은 ACO 모듈로 바꾼다.
/// 새 무장: 예제 프리팹을 복제(Ctrl+D) → 루트의 Id/이름/설명/가격/아이콘, 자식 Module의 성능 값을 바꾼다 → 셋업 검사 [고치기](또는 기체 셋업).
public static class ACO_SpecialWeaponCatalog
{
    public const string Folder = "Assets/ACO_Addon/SpecialWeapons";

    private struct Example
    {
        public string Id, Name, Desc, Icon, Projectile;
        public int Price, Order;
        public System.Action<ACO_DFUNC_Missile> Preset;
        public bool Ccip;
    }

    // 미사일 모델(탄 프리팹). 없으면 비워 둔다 → 설치할 때 그 기체의 일반 미사일 모델을 쓴다.
    private const string P = "Assets/ACO_Addon/Prefabs/";

    // 격납고 설명(6-151 — 부제 \n 한두 문장, 브리핑식, 정확한 수치는 쓰지 않는다). 정의를 새로 만들 때와 Setup Check hangar-texts-1이 쓴다
    private static readonly System.Collections.Generic.Dictionary<string, string> Descriptions = new System.Collections.Generic.Dictionary<string, string>
    {
        { "4AAM", "MULTI-TARGET AAM\nLOCKS UP TO FOUR AIRCRAFT AT ONCE AND FIRES ONE MISSILE AT EACH. SHORT LOCK RANGE." },
        { "UGB", "UNGUIDED BOMB\nFREE-FALL BOMBS. AIM WITH THE CCIP PIPPER." },
        { "RKT", "ROCKET LAUNCHER\nUNGUIDED ROCKET SALVOS FOR GROUND TARGETS. DEEP MAGAZINE, QUICK RELOAD." },
        { "LGB", "LASER-GUIDED BOMB\nLOCK A GROUND TARGET AND RELEASE. THE BOMB STEERS ITSELF ONTO IT WITH A WIDE BLAST." },
        { "LGR", "LASER-GUIDED ROCKETS\nLOCKED ROCKETS CORRECT IN FLIGHT AND HIT HARDER THAN PLAIN ROCKETS. SLOWER TO RELOAD." },
        { "4AGM", "MULTI-TARGET AGM\nLOCKS UP TO FOUR GROUND TARGETS AND FIRES ONE MISSILE AT EACH." },
        { "LAGM", "LONG-RANGE AGM\nHEAVY MISSILE THAT HITS GROUND TARGETS FROM OUTSIDE THEIR AIR DEFENSE RANGE. NARROW, SLOW LOCK." },
        { "LASM", "LONG-RANGE ANTI-SHIP MISSILE\nSEA-SKIMS INTO SHIPS AT HIGH SPEED WITH A HEAVY WARHEAD. SHIP CIWS CANNOT STOP IT." },
        { "SCM", "STAND-OFF CLUSTER MISSILE\nOPENS ABOVE THE TARGET AND SCATTERS BOMBLETS OVER A WIDE AREA. FEW ROUNDS, LONG RELOAD." },
        { "MMP", "MICRO-MISSILE POD\nLOCKS UP TO EIGHT AIR OR GROUND TARGETS AND SPLITS 16 MICRO-MISSILES BETWEEN THEM. LIGHT WARHEADS, SHORT REACH." },
        { "RGP", "RAIL GUN POD\nHYPERSONIC SLUG THAT HITS HARD AT VERY LONG RANGE. A NARROW INSTANT LOCK AIMS IT. LONG CHARGE BETWEEN SHOTS." },
        { "FLAK", "PROXIMITY-FUSED ROCKETS\nHOLD TO FIRE UNGUIDED ROCKETS THAT BURST NEAR ANY ENEMY AIRCRAFT." },
        { "SASM", "AERIAL SUPPRESSION AAM\nLARGE PROXIMITY BLAST THAT ALSO CATCHES NEARBY AIRCRAFT. SHORT LOCK RANGE." },
        { "LAAM", "LONG-RANGE AAM\nLOCKS AIRCRAFT FROM BEYOND VISUAL RANGE. NARROW, SLOW LOCK." },
        { "QAAM", "QUICK MANEUVER AAM\nWIDE, FAST LOCK AND EXTREME TURN RATE FOR CLOSE DOGFIGHTS. SHORT RANGE." },
        { "HVAA", "HIGH-VELOCITY AAM\nVERY FAST AND HARD TO OUTRUN, BUT TURNS WIDE." },
        { "CCAA", "COUNTER-COUNTERMEASURE AAM\nIGNORES FLARES. FAST AND HARD-HITTING, BUT SHORT-RANGED, SLOW TO LOCK AND TURNS WIDE." },
        { "HPAA", "HIGH-POWER AAM\nOVERSIZED WARHEAD WITH A WIDE PROXIMITY FUSE." },
        { "ADAM", "AREA DENIAL AIR MUNITION\nDROPS, THEN HUNTS THE NEAREST AIRCRAFT. HUGE BLAST, FEW ROUNDS." },
        { "ECM", "ELECTRONIC WARFARE POD\nBRIEFLY JAMS THE AREA AROUND YOU. BREAKS ENEMY LOCKS AND MISSILES, THROWS OFF AA GUNS." },
    };

    public static string DescriptionFor(string id, string fallback) { return Descriptions.TryGetValue(id, out string d) ? d : fallback; }

    /// Setup Check hangar-texts-1: 무장 정의 프리팹의 설명을 새 문구로(정의에 없는 ID는 그대로). 고친 뒤 Bake(격납고 목록이 따라간다).
    public static void RewriteDescriptions()
    {
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ACO_SpecialWeaponDef d = root.GetComponent<ACO_SpecialWeaponDef>();
                if (d == null || !Descriptions.TryGetValue(d.Id, out string text) || d.Description == text) { continue; }
                d.Description = text;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                n++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Debug.Log($"[ACO] 특수무장 설명: 정의 {n}개를 새 문구로 바꿨습니다. Bake 하세요.");
    }

    private static Example[] Examples()
    {
        return new[]
        {
            new Example { Id = "4AAM", Name = "4AAM", Order = 10, Price = 6000, Icon = "weapon_4aam", Preset = ACO_PlayerMissileSetup.Apply4AAMPreset,
                Projectile = P + "ACO_Missile_MRAAM.prefab", Desc = "MULTI-TARGET AAM\nLOCKS UP TO 4 AIR TARGETS - ONE MISSILE EACH" },
            new Example { Id = "UGB", Name = "UGB", Order = 20, Price = 8000, Icon = "weapon_ugb", Preset = ACO_PlayerMissileSetup.ApplyBombPreset,
                Projectile = P + "ACO_Bomb.prefab", Ccip = true, Desc = "UNGUIDED BOMB\nFREE-FALL BOMBS FOR GROUND TARGETS" },
            new Example { Id = "RKT", Name = "RKT", Order = 30, Price = 8000, Icon = "weapon_rkt", Preset = ACO_PlayerMissileSetup.ApplyRocketPreset,
                Projectile = P + "ACO_Rocket.prefab", Desc = "ROCKET LAUNCHER\nUNGUIDED ROCKETS FOR GROUND TARGETS" },
            new Example { Id = "LGB", Name = "LGB", Order = 110, Price = 11000, Icon = "weapon_lgb", Preset = ApplyLGB,
                Projectile = P + "ACO_Bomb.prefab", Ccip = true, Desc = "LASER-GUIDED BOMB\nLOCK A GROUND TARGET AND DROP - STEERS ONTO IT, TIGHTER BLAST" },
            new Example { Id = "LGR", Name = "LGR", Order = 120, Price = 11000, Icon = "weapon_lgr", Preset = ApplyLGR,
                Projectile = P + "ACO_Rocket.prefab", Desc = "LASER-GUIDED ROCKETS\nLOCKED ROCKETS STEER ONTO THE TARGET - SLOWER RELOAD" },
            new Example { Id = "4AGM", Name = "4AGM", Order = 40, Price = 9000, Icon = "weapon_4agm", Preset = Apply4AGM,
                Projectile = P + "ACO_Missile_AGM.prefab", Desc = "MULTI-TARGET AGM\nLOCKS UP TO 4 GROUND TARGETS - ONE MISSILE EACH" },
            new Example { Id = "LAGM", Name = "LAGM", Order = 50, Price = 10000, Icon = "weapon_lagm", Preset = ApplyLAGM,
                Projectile = P + "ACO_Missile_LAGM.prefab", Desc = "LONG-RANGE AGM\nSTRIKES GROUND TARGETS FROM STANDOFF RANGE" },
            new Example { Id = "LASM", Name = "LASM", Order = 130, Price = 12000, Icon = "weapon_lasm", Preset = ApplyLASM,
                Projectile = P + "ACO_Missile_LAGM.prefab", Desc = "LONG-RANGE ANTI-SHIP MISSILE\nSEA-SKIMS TO SHIPS - CANNOT BE INTERCEPTED BY CIWS" },
            new Example { Id = "ECM", Name = "ECM", Order = 180, Price = 15000, Icon = "weapon_ecm", Preset = ApplyECM,
                Projectile = null, Desc = "ELECTRONIC WARFARE POD\n5KM JAMMING FOR 10S - BREAKS ENEMY MISSILES AND LOCKS, DISRUPTS AA GUNS" },
            new Example { Id = "SCM", Name = "SCM", Order = 170, Price = 14000, Icon = "weapon_scm", Preset = ApplySCM,
                Projectile = P + "ACO_Missile_SCM.prefab", Desc = "STAND-OFF CLUSTER MISSILE\nBURSTS ABOVE THE TARGET - SCATTERS BOMBLETS OVER A WIDE AREA" },
            new Example { Id = "MMP", Name = "MMP", Order = 190, Price = 17000, Icon = "weapon_mmp", Preset = ApplyMMP,
                Projectile = P + "ACO_Missile_MMP.prefab", Desc = "MICRO-MISSILE POD\nLOCKS UP TO 8 AIR OR GROUND TARGETS - 16 MISSILES SPLIT AMONG THEM" },
            new Example { Id = "RGP", Name = "RGP", Order = 200, Price = 18000, Icon = "weapon_rgp", Preset = ApplyRGP,
                Projectile = P + "ACO_Missile_RGP.prefab", Desc = "RAIL GUN POD\nHYPERSONIC SLUG - INSTANT NARROW LOCK AIMS IT, 10 KM, RECHARGES IN 8 S" },
            new Example { Id = "FLAK", Name = "FLAK", Order = 140, Price = 9000, Icon = "weapon_flak", Preset = ApplyFLAK,
                Projectile = P + "ACO_Rocket.prefab", Desc = "PROXIMITY-FUSED AIR-TO-AIR ROCKETS\nUNGUIDED - BURSTS NEAR ANY ENEMY AIRCRAFT" },
            new Example { Id = "SASM", Name = "SASM", Order = 70, Price = 10000, Icon = "weapon_sasm", Preset = ApplySASM,
                Projectile = P + "ACO_Missile_SRAAM.prefab", Desc = "SHORT-RANGE AERIAL SUPPRESSION AAM\nHUGE PROXIMITY BLAST - ALSO HITS NEARBY AIRCRAFT" },
            new Example { Id = "LAAM", Name = "LAAM", Order = 60, Price = 12000, Icon = "weapon_laam", Preset = ApplyLAAM,
                Projectile = P + "ACO_Missile_AAM.prefab", Desc = "LONG-RANGE AAM\nLOCKS AIR TARGETS FROM BEYOND VISUAL RANGE" },
            new Example { Id = "QAAM", Name = "QAAM", Order = 80, Price = 15000, Icon = "weapon_qaam", Preset = ApplyQAAM,
                Projectile = P + "ACO_Missile_SRAAM.prefab", Desc = "QUICK MANEUVER AAM\nWIDE SEEKER, EXTREME TURN RATE FOR DOGFIGHTS" },
            new Example { Id = "HVAA", Name = "HVAA", Order = 90, Price = 13000, Icon = "weapon_hvaa", Preset = ApplyHVAA,
                Projectile = P + "ACO_Missile_AAM.prefab", Desc = "HIGH-VELOCITY AAM\nVERY FAST - HARD TO OUTRUN, LESS AGILE" },
            new Example { Id = "CCAA", Name = "CCAA", Order = 150, Price = 16000, Icon = "weapon_ccaa", Preset = ApplyCCAA,
                Projectile = P + "ACO_Missile_AAM.prefab", Desc = "COUNTER-COUNTERMEASURE AAM\nIGNORES FLARES AND CHAFF - SLOW LOCK, LESS AGILE" },
            new Example { Id = "HPAA", Name = "HPAA", Order = 100, Price = 14000, Icon = "weapon_hpaa", Preset = ApplyHPAA,
                Projectile = P + "ACO_Missile_AAM.prefab", Desc = "HIGH-POWER AAM\nHUGE WARHEAD AND WIDE PROXIMITY FUSE" },
            new Example { Id = "ADAM", Name = "ADAM", Order = 160, Price = 18000, Icon = "weapon_adam", Preset = ApplyADAM,
                Projectile = P + "ACO_Missile_AAM.prefab", Desc = "AREA DENIAL AIR MUNITION\nDROPS, THEN HUNTS THE NEAREST AIRCRAFT - HUGE BLAST" },
        };
    }

    // ───────────── 예제 프리팹 ─────────────

    /// 예제 프리팹 경로(프리팹 생성 창의 존재 확인용).
    public static string[] ExamplePaths()
    {
        Example[] ex = Examples();
        var paths = new string[ex.Length];
        for (int i = 0; i < ex.Length; i++) { paths[i] = $"{Folder}/ACO_Special_{ex[i].Id}.prefab"; }
        return paths;
    }

    /// 카탈로그가 비었으면 예제를 모두 만들고, 아니면 '새' 예제만 만든다 — 정의 파일이 없고 씬의 어느 격납고 목록에도 없는 ID(코드에 새로 더해진 예제).
    /// 일부러 지운 예제는 격납고 목록에 ID가 남아 있어 다시 만들지 않는다. 기체 셋업·Bake 전에 부른다.
    public static void EnsureExamples()
    {
        if (LoadDefs().Count == 0) { CreateExamples(false); return; }
        CreateExamples(false, KnownLoadoutIds());
    }

    /// 씬의 격납고 목록에 있는 특수무장 ID 전부.
    private static HashSet<string> KnownLoadoutIds()
    {
        var known = new HashSet<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Loadout l in root.GetComponentsInChildren<ACO_Loadout>(true))
            {
                if (l.SpecialIds == null) { continue; }
                foreach (string id in l.SpecialIds) { known.Add(id); }
            }
        }
        return known;
    }

    /// 셋업 검사용: 코드에 새로 더해져 아직 정의 파일이 없는 예제 ID(정의 파일도 없고 어느 격납고 목록에도 없는 것 — EnsureExamples가 만들 것).
    /// 카탈로그가 비어 있으면 빈 목록(그땐 기체 셋업이 전부 만든다).
    public static List<string> NewExampleIds()
    {
        var ids = new List<string>();
        if (LoadDefs().Count == 0) { return ids; }
        HashSet<string> known = KnownLoadoutIds();
        foreach (Example e in Examples())
        {
            if (known.Contains(e.Id)) { continue; }
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/ACO_Special_{e.Id}.prefab") != null) { continue; }
            ids.Add(e.Id);
        }
        return ids;
    }

    /// 예제 정의를 만든다. overwrite면 있는 예제도 다시 만든다(손본 값 초기화 — 복제해 만든 무장은 건드리지 않는다). skipIds의 예제는 만들지 않는다.
    /// 폭탄·로켓 탄체 프리팹이 없으면 같이 만든다.
    public static void CreateExamples(bool overwrite, HashSet<string> skipIds = null)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "SpecialWeapons"); }
        if (!ACO_PrefabBuilder.Exists("ACO_Bomb")) { ACO_PrefabBuilder.BuildBomb(); }
        if (!ACO_PrefabBuilder.Exists("ACO_Rocket")) { ACO_PrefabBuilder.BuildRocket(); }
        if (!ACO_PrefabBuilder.Exists("ACO_Missile_SCM")) { ACO_PrefabBuilder.BuildScmMissile(); }
        if (!ACO_PrefabBuilder.Exists("ACO_Missile_MMP")) { ACO_PrefabBuilder.BuildMmpMissile(); }
        if (!ACO_PrefabBuilder.Exists("ACO_Missile_RGP")) { ACO_PrefabBuilder.BuildRgpSlug(); }
        int made = 0;
        foreach (Example e in Examples())
        {
            string path = $"{Folder}/ACO_Special_{e.Id}.prefab";
            if (skipIds != null && skipIds.Contains(e.Id)) { continue; }
            if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) { continue; }

            var root = new GameObject("ACO_Special_" + e.Id);
            ACO_SpecialWeaponDef def = root.AddComponent<ACO_SpecialWeaponDef>();
            def.Id = e.Id;
            def.DisplayName = e.Name;
            def.Description = DescriptionFor(e.Id, e.Desc);
            def.Price = e.Price;
            def.SortOrder = e.Order;
            def.Icon = ACO_HUDBuilder.LoadIcon(e.Icon);
            if (e.Id == "ECM" || e.Id == "MMP" || e.Id == "RGP") { def.Category = ACO_WeaponCategory.Other; }

            var modGo = new GameObject("Module");
            modGo.transform.SetParent(root.transform, false);
            ACO_DFUNC_Missile m = modGo.AddUdonSharpComponent<ACO_DFUNC_Missile>();
            e.Preset(m);
            m.LaunchCall = LaunchCallFor(e.Id);
            m.Missile = e.Projectile != null ? AssetDatabase.LoadAssetAtPath<GameObject>(e.Projectile) : null;   // 없으면 비움 → 기체의 일반 미사일 모델
            m.TargetIndicator = null;
            m.WeaponAnimator = null;
            if (e.Ccip) { m.CcipReticle = ACO_HUDBuilder.CreateCcipReticle(modGo.transform); }
            def.Module = m;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            made++;
        }
        AssetDatabase.SaveAssets();
        if (made > 0) { Debug.Log($"[ACO] 특수무장 정의 프리팹 {made}개를 {Folder}에 만들었습니다. 복제해 새 무장을 만들고 셋업 검사 [고치기] 또는 기체 셋업으로 넣으세요."); }
    }

    // 예제 성능 = F/A-18(Assets/Pit-bool/FA-18E Main_ACO.prefab)에 맞춘 특수무장 모듈 값(2026-11 기준 — 기체에서 다시 맞추면 여기도 고친다).
    // 대지(AGM)는 레이어 30 + 터진 자리 범위 피해, 대공(AAM)은 레이어 29. 유도는 모두 직접 추적(0). 강화는 특수 대지/대공 탭.
    // FLAK·CCAA·ADAM은 F/A-18에 아직 없어 같은 기준(피해 200~400, 속력 900, 락온 2km·45도·1.5초)에 맞춰 정했다.
    private static void Guided(ACO_DFUNC_Missile m, bool ground, int lockTargets, int slots, int ammo, float reload,
        float range, float angle, float lockTime, float damage, float speed, float accel, float turn, float proximity)
    {
        m.UpgradeFamily = ground ? ACO_UpgradeFamily.SpecialAGM : ACO_UpgradeFamily.SpecialAAM;
        m.CrossLockIndicator = true;
        m.TargetLayers = ground ? 1 << 30 : 1 << 29;
        m.MaxLockTargets = lockTargets;
        m.OneMissilePerTarget = true;
        m.SlotCount = slots;
        m.MaxAmmo = ammo;
        m.SlotReloadTime = reload;
        m.LockRange = range;
        m.LockAngle = angle;
        m.LockTime = lockTime;
        m.Damage = damage;
        m.MaxSpeed = speed;
        m.Acceleration = accel;
        m.TurnRate = turn;
        m.ProximityRadius = proximity;
        m.GuidanceMode = 0;
        m.AllowUnguidedFire = false;
        m.AllowFiringWhenGrounded = false;
        m.InheritVelocity = true;
        m.MissileSplash = ground;
        m.SplashRadius = 10f; m.MinSplashFraction = 0.25f;
        m.MissileType = ground ? 2 : 1;
        m.NumPreInstantiated = 2;   // 풀은 모자라면 늘어난다 — 시작 비용을 줄인다
    }

    /// 4AGM: 지상 표적 4개 동시 락온, 일제 발사. 슬롯 4 / 16발 / 12초, 넓은 탐색(60도).
    private static void Apply4AGM(ACO_DFUNC_Missile m)
    {
        Guided(m, true, 4, 4, 16, 12f, 3500f, 60f, 1.5f, 400f, 900f, 250f, 90f, 6f);
        m.SplashRadius = 5f; m.MinSplashFraction = 0.3f;
    }

    /// LAGM: 먼 거리 대지(6.5km). 락온 2초, 큰 피해와 넓은 범위(100m).
    private static void ApplyLAGM(ACO_DFUNC_Missile m)
    {
        Guided(m, true, 1, 2, 12, 10f, 6500f, 30f, 2f, 400f, 900f, 300f, 90f, 6f);
        m.SplashRadius = 100f; m.MinSplashFraction = 0.6f;
    }

    /// LGB(Laser-Guided Bomb): UGB와 같은 투하·피해(150)인데 락온한 지상 표적으로 속도 방향을 돌려 떨어진다(초당 10도, 시커 90도).
    /// 대신 장전이 느리고(10초, UGB 4초) 범위가 좁다(60m, UGB 200m). 락온 없이 떨어뜨리면 UGB처럼 자유 낙하(CCIP 표시).
    private static void ApplyLGB(ACO_DFUNC_Missile m)
    {
        ACO_PlayerMissileSetup.ApplyBombPreset(m);
        GuidedOrdnance(m, 5000f, 45f, 10f, 5f);
        m.SlotReloadTime = 10f;
        m.SplashRadius = 60f;
    }

    /// LGR(Laser-Guided Rocket): RKT와 같은 연사·피해(100)인데 락온한 지상 표적으로 휘어 날아간다(초당 30도). 누르고 있으면 락온한 한 표적에 연달아.
    /// 대신 장전이 느리고(8초, RKT 3초) 범위가 좁다(15m, RKT 50m). 락온 없이 쏘면 RKT처럼 곧게.
    private static void ApplyLGR(ACO_DFUNC_Missile m)
    {
        ACO_PlayerMissileSetup.ApplyRocketPreset(m);
        GuidedOrdnance(m, 3000f, 30f, 30f, 4f);
        m.FireInterval = 0.15f;
        m.SlotReloadTime = 8f;
        m.SplashRadius = 15f;
    }

    /// 유도 폭탄·로켓 공통: 지상 표적(레이어 30) 락온 1초, 락온 없이도 쏜다(무유도), 표적 마커 중심 근접 폭발 반경.
    private static void GuidedOrdnance(ACO_DFUNC_Missile m, float range, float angle, float turn, float proximity)
    {
        m.TargetLayers = 1 << 30;
        m.CrossLockIndicator = true;
        m.MaxLockTargets = 1;
        m.LockRange = range;
        m.LockAngle = angle;
        m.LockTime = 1f;
        m.TurnRate = turn;
        m.ProximityRadius = proximity;
        m.ProximityAnyEnemy = false;
        m.AllowUnguidedFire = true;
    }

    /// LASM(Long-range Anti-Ship Missile): LAGM과 같은 사거리·피해, 대신 범위 피해가 작다(10m — LAGM은 100m, 지상 무리용).
    /// 함선을 노리면 해면 위 8m로 깔려 가다 500m 안에서 꽂히고, 그동안 CIWS 요격을 받지 않는다. 함선이 아니면 LAGM처럼 날아간다.
    private static void ApplyLASM(ACO_DFUNC_Missile m)
    {
        Guided(m, true, 1, 2, 12, 10f, 6500f, 30f, 2f, 400f, 900f, 300f, 90f, 6f);
        m.SplashRadius = 10f; m.MinSplashFraction = 0.3f;
        m.FullPartSplash = true;   // 함선 부위에도 범위 피해를 그대로(다른 무기는 0.2배)
        m.SeaSkim = true;
        m.SkimAltitude = 8f;
        m.SkimTerminalDistance = 500f;
    }

    /// ECM(전자전 포드): 미사일 대신 재밍. 10초 켜고 20초 재충전, 반경 5km(ACO_RadarContacts.EcmRange). 슬롯 하나, 탄 없음.
    private static void ApplyECM(ACO_DFUNC_Missile m)
    {
        m.EcmMode = true;
        m.EcmDuration = 10f;
        m.SlotReloadTime = 20f;
        m.SlotCount = 1;
        m.MaxAmmo = 1;
        m.UpgradeFamily = ACO_UpgradeFamily.None;
        m.TargetLayers = 0;
        m.MaxLockTargets = 1;
        m.AllowUnguidedFire = true;
        m.AllowFiringWhenGrounded = false;
        m.NumPreInstantiated = 1;
    }

    /// SCM(Stand-off Cluster Missile): 멀리서(6km) 락온해 쏘면 느리게(550) 날아가 표적 위로 올라갔다 내리꽂으며, 표적 420m 앞에서 터져 자탄 16발을 뿌린다(ACO_Dispenser — 탄체 프리팹 값).
    /// 자탄마다 범위 피해 110 / 30m라 한 표적에 몇 발 겹칠 수 있고, 뿌려진 넓이(약 220 × 180m)로 호송대·기지처럼 모인 표적을 친다. 단일 표적엔 LAGM이 낫다. 슬롯 2 / 8발 / 14초.
    /// 올라가는 동안 표적이 시커 밖으로 나가도 놓치지 않게 시커 180도. 강화는 특수 대지 탭(피해·폭발 범위는 자탄마다 적용).
    private static void ApplySCM(ACO_DFUNC_Missile m)
    {
        Guided(m, true, 1, 2, 8, 14f, 6000f, 30f, 2f, 110f, 550f, 200f, 60f, 6f);
        m.SplashRadius = 30f; m.MinSplashFraction = 0.4f;
        m.SeekerAngle = 180f;
        m.MissileLifetime = 30f;
    }

    /// MMP(Micro-Missile Pod): 공중·지상 표적을 8개까지 락온(0.8초씩, 3.5km·40도). 쏘면 장전된 16발이 0.07초 간격으로 락온한 표적들에 차례로 나뉘어 나간다
    /// (8개면 2발씩, 1개면 16발 모두). 미사일은 포드 아랫면 셀에서 아래로 사출(14m/s)돼 0.35초 떨어지며 표적 쪽으로 기수를 돌린 뒤 점화한다.
    /// 한 발은 작다(피해 60, 범위 8m) — 전투기는 2발, 무리 지은 지상 표적을 한 번에. 16슬롯 / 64발 / 15초. 락온 없이는 쏘지 않는다.
    /// 강화를 받지 않는다(UpgradeFamily None — 16발이 다 강화되면 너무 세다). 격납고 분류는 OTHER.
    /// 발사 지점은 포드 외형의 셀(셋업 검사 [고치기]) — 포드가 없으면 날개 파일런에서 나간다.
    private static void ApplyMMP(ACO_DFUNC_Missile m)
    {
        Guided(m, false, 8, 16, 64, 15f, 3500f, 40f, 0.8f, 60f, 800f, 450f, 140f, 8f);
        m.TargetLayers = (1 << 29) | (1 << 30);
        m.OneMissilePerTarget = false;
        m.MissileSplash = true;
        m.SplashRadius = 8f; m.MinSplashFraction = 0.5f;
        m.DropTime = 0.35f;
        m.EjectSpeed = 14f;
        m.DropTurnRate = 200f;
        m.SalvoInterval = 0.07f;
        m.MissileLifetime = 7f;
        m.NumPreInstantiated = 8;
        m.UpgradeFamily = ACO_UpgradeFamily.None;
        m.TakesExtraAmmo = true;   // EXTRA AMMO 소모품은 받는다(16 → 24발)
    }

    /// RGP(레일건 포드): 초고속 단발탄(4000m/s, 10km를 2.5초). 락온은 좁고(2.5도) 즉시 — 락온하면 발사 순간 표적의 예상 자리로 겨눠 쏘고(SnapAimAtLaunch),
    /// 날아가는 동안은 약하게만 보정한다(6도/초 — 급기동하면 빗나간다). 락온이 없으면 기수 정면으로 곧게. 플레어·채프에 속지 않는다.
    /// 공중·지상 모두. 한 발 피해 350(전투기는 한 발), 범위는 작다(6m). 16발, 한 발 쏠 때마다 8초 재충전. 강화 없음, 격납고 OTHER.
    private static void ApplyRGP(ACO_DFUNC_Missile m)
    {
        Guided(m, false, 1, 1, 16, 8f, 10000f, 2.5f, 0.05f, 350f, 4000f, 0f, 6f, 6f);
        m.TargetLayers = (1 << 29) | (1 << 30);
        m.GuidanceMode = 1;               // 예측 조준(리드)
        m.AllowUnguidedFire = true;
        m.IgnoreCountermeasures = true;
        m.ProximityAnyEnemy = true;       // 락온 없이 쏴도 곁을 지나는 적기에 터진다
        m.MissileSplash = true;
        m.SplashRadius = 6f; m.MinSplashFraction = 0.5f;
        m.SnapAimAtLaunch = true;
        m.MuzzleSpeed = 4000f;
        m.InheritVelocity = false;
        m.MissileLifetime = 3f;
        m.NumPreInstantiated = 2;
        m.UpgradeFamily = ACO_UpgradeFamily.None;
        m.TakesExtraAmmo = true;   // EXTRA AMMO 소모품은 받는다(16 → 24발)
    }

    /// Setup Check(mmp-other-1): 이미 만든 MMP를 격납고 OTHER 분류·강화 없음으로 — 정의 프리팹, 기체 프리팹의 Module_MMP, 씬에서 덮어쓴 값, 격납고 목록.
    public static void TuneMmpOther()
    {
        string defPath = $"{Folder}/ACO_Special_MMP.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(defPath) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(defPath);
            try
            {
                ACO_SpecialWeaponDef def = root.GetComponent<ACO_SpecialWeaponDef>();
                if (def != null)
                {
                    def.Category = ACO_WeaponCategory.Other;
                    if (def.Module != null) { def.Module.UpgradeFamily = ACO_UpgradeFamily.None; }
                    PrefabUtility.SaveAsPrefabAsset(root, defPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var prefabs = new List<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(special.gameObject);
                if (!string.IsNullOrEmpty(path) && !prefabs.Contains(path)) { prefabs.Add(path); }
            }
        }
        foreach (string path in prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
                {
                    ACO_DFUNC_Missile m = SlotModule(special, "MMP");
                    if (m != null && m.UpgradeFamily != ACO_UpgradeFamily.None) { m.UpgradeFamily = ACO_UpgradeFamily.None; changed = true; }
                }
                if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        // 씬에서 값을 덮어썼거나 프리팹이 아닌 기체
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                ACO_DFUNC_Missile m = SlotModule(special, "MMP");
                if (m == null || m.UpgradeFamily == ACO_UpgradeFamily.None) { continue; }
                Undo.RecordObject(m, "ACO MMP other");
                m.UpgradeFamily = ACO_UpgradeFamily.None;
                PrefabUtility.RecordPrefabInstancePropertyModifications(m);
                EditorUtility.SetDirty(m);
            }
        }
        SyncLoadouts();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// Setup Check(other-extra-ammo-1): 이미 만든 MMP·RGP가 EXTRA AMMO 소모품(탄수 ×1.5)을 받게 한다 — 정의 프리팹, 기체 프리팹의 슬롯 모듈, 씬에서 덮어쓴 값.
    /// 강화 계열(None)과 다른 값은 그대로.
    public static void TuneOtherExtraAmmo()
    {
        string[] ids = { "MMP", "RGP" };
        foreach (string id in ids)
        {
            string defPath = $"{Folder}/ACO_Special_{id}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(defPath) == null) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(defPath);
            try
            {
                ACO_SpecialWeaponDef def = root.GetComponent<ACO_SpecialWeaponDef>();
                if (def != null && def.Module != null && !def.Module.TakesExtraAmmo)
                {
                    def.Module.TakesExtraAmmo = true;
                    PrefabUtility.SaveAsPrefabAsset(root, defPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var prefabs = new List<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(special.gameObject);
                if (!string.IsNullOrEmpty(path) && !prefabs.Contains(path)) { prefabs.Add(path); }
            }
        }
        foreach (string path in prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
                {
                    foreach (string id in ids)
                    {
                        ACO_DFUNC_Missile m = SlotModule(special, id);
                        if (m != null && !m.TakesExtraAmmo) { m.TakesExtraAmmo = true; changed = true; }
                    }
                }
                if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); Debug.Log($"[ACO] {path}: MMP·RGP가 EXTRA AMMO를 받게 했습니다."); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        // 씬에서 값을 덮어썼거나 프리팹이 아닌 기체
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                foreach (string id in ids)
                {
                    ACO_DFUNC_Missile m = SlotModule(special, id);
                    if (m == null || m.TakesExtraAmmo) { continue; }
                    Undo.RecordObject(m, "ACO other extra ammo");
                    m.TakesExtraAmmo = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(m);
                    EditorUtility.SetDirty(m);
                }
            }
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// 예제 무장의 발사 무전 코드. 대공 단거리 Fox Two, 장거리·4AAM Fox Three, 4AGM Rifle, LAGM Magnum, SCM Greyhound, LASM Bruiser,
    /// 폭탄 Pickle, 로켓 Rockets, RGP Spear, ECM Music, MMP Drive. 모르는 무장(복제해 만든 것)은 없음.
    public static ACO_LaunchCall LaunchCallFor(string id)
    {
        switch (id)
        {
            case "QAAM": case "SASM": case "HPAA": case "CCAA": return ACO_LaunchCall.FoxTwo;
            case "4AAM": case "LAAM": case "HVAA": case "ADAM": return ACO_LaunchCall.FoxThree;
            case "MMP": return ACO_LaunchCall.Drive;
            case "4AGM": return ACO_LaunchCall.Rifle;
            case "LAGM": return ACO_LaunchCall.Magnum;
            case "SCM": return ACO_LaunchCall.Greyhound;
            case "LASM": return ACO_LaunchCall.Bruiser;
            case "UGB": case "LGB": return ACO_LaunchCall.Pickle;
            case "RKT": case "LGR": case "FLAK": return ACO_LaunchCall.Rockets;
            case "RGP": return ACO_LaunchCall.Spear;
            case "ECM": return ACO_LaunchCall.Music;
        }
        return ACO_LaunchCall.None;
    }

    /// Setup Check(launch-call-1): 발사 무전 코드를 넣는다 — 특수무장 정의 프리팹, 기체 프리팹의 슬롯 모듈(정의 Id로), 기체의 일반 미사일(Fox Two),
    /// 씬에서 덮어쓴 값. 이미 코드가 있는 무장은 그대로.
    public static void TuneLaunchCalls()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ACO_SpecialWeaponDef probe = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<ACO_SpecialWeaponDef>();
            if (probe == null || probe.Module == null || probe.Module.LaunchCall != ACO_LaunchCall.None || LaunchCallFor(probe.Id) == ACO_LaunchCall.None) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ACO_SpecialWeaponDef def = root.GetComponent<ACO_SpecialWeaponDef>();
                if (def != null && def.Module != null)
                {
                    def.Module.LaunchCall = LaunchCallFor(def.Id);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var prefabs = new List<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(special.gameObject);
                if (!string.IsNullOrEmpty(path) && !prefabs.Contains(path)) { prefabs.Add(path); }
            }
        }
        int n = 0;
        foreach (string path in prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int c = ApplyLaunchCalls(root);
                if (c > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); n += c; }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        // 씬에서 값을 덮어썼거나 프리팹이 아닌 기체
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (m.LaunchCall != ACO_LaunchCall.None) { continue; }
                ACO_LaunchCall want = WantedLaunchCall(m);
                if (want == ACO_LaunchCall.None) { continue; }
                Undo.RecordObject(m, "ACO launch call");
                m.LaunchCall = want;
                PrefabUtility.RecordPrefabInstancePropertyModifications(m);
                EditorUtility.SetDirty(m);
                n++;
            }
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 발사 무전: 무장 모듈 {n}개에 코드를 넣었습니다(정의 프리팹 포함 — 이미 있던 코드는 그대로).");
    }

    /// 한 기체(프리팹 내용)의 무장 모듈에 코드를 넣는다. 넣은 수.
    private static int ApplyLaunchCalls(GameObject root)
    {
        int n = 0;
        foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
        {
            if (m.LaunchCall != ACO_LaunchCall.None) { continue; }
            ACO_LaunchCall want = WantedLaunchCall(m);
            if (want == ACO_LaunchCall.None) { continue; }
            m.LaunchCall = want;
            EditorUtility.SetDirty(m);
            n++;
        }
        return n;
    }

    /// 이 모듈이 받을 코드: 특수무장 슬롯 모듈이면 그 슬롯의 정의 Id로, 아니면 일반 미사일 계열(Missile)은 Fox Two.
    private static ACO_LaunchCall WantedLaunchCall(ACO_DFUNC_Missile m)
    {
        ACO_DFUNC_Special special = m.GetComponentInParent<ACO_DFUNC_Special>(true);
        if (special != null && special.Modules != null && special.ModuleIds != null)
        {
            int k = System.Array.IndexOf(special.Modules, m);
            if (k >= 0 && k < special.ModuleIds.Length) { return LaunchCallFor(special.ModuleIds[k]); }
        }
        if (m.EcmMode) { return ACO_LaunchCall.Music; }
        return m.UpgradeFamily == ACO_UpgradeFamily.Missile ? ACO_LaunchCall.FoxTwo : ACO_LaunchCall.None;
    }

    private static ACO_DFUNC_Missile SlotModule(ACO_DFUNC_Special special, string id)
    {
        int k = special.ModuleIds != null ? System.Array.IndexOf(special.ModuleIds, id) : -1;
        return k >= 0 && special.Modules != null && k < special.Modules.Length ? special.Modules[k] as ACO_DFUNC_Missile : null;
    }

    /// SASM(Short-range Aerial Suppression Missile): 근거리 대공. 근접 신관이 매우 커서(45m) 스치기만 해도 터지고,
    /// 범위 피해(50m)로 편대의 옆 기체까지 친다. 강화는 특수 대공 탭(폭발 범위 강화는 없다).
    private static void ApplySASM(ACO_DFUNC_Missile m)
    {
        Guided(m, false, 1, 2, 16, 9f, 2000f, 45f, 1.5f, 200f, 900f, 350f, 90f, 45f);
        m.MissileSplash = true;
        m.SplashRadius = 50f; m.MinSplashFraction = 0.6f;
    }

    /// LAAM: 먼 거리 대공(10km). 락온 2초, 빠르다(1200).
    private static void ApplyLAAM(ACO_DFUNC_Missile m)
    {
        Guided(m, false, 1, 2, 16, 10f, 10000f, 45f, 2f, 200f, 1200f, 320f, 90f, 12f);
    }

    /// QAAM: 근접전. 넓은 탐색(60도), 빠른 락온(0.5초), 매우 큰 선회율(360).
    private static void ApplyQAAM(ACO_DFUNC_Missile m)
    {
        Guided(m, false, 1, 2, 16, 8f, 2000f, 60f, 0.5f, 200f, 900f, 400f, 360f, 9f);
    }

    /// HVAA: 고속(1900). 매우 빠르지만 덜 돌고 피해가 작다.
    private static void ApplyHVAA(ACO_DFUNC_Missile m)
    {
        Guided(m, false, 1, 2, 16, 10f, 2000f, 45f, 1.5f, 130f, 1900f, 700f, 70f, 10f);
    }

    /// HPAA: 고위력(400). 넓은 근접 신관(20m), 장전이 느리다.
    private static void ApplyHPAA(ACO_DFUNC_Missile m)
    {
        Guided(m, false, 1, 2, 12, 14f, 2000f, 45f, 1.5f, 400f, 800f, 300f, 90f, 20f);
    }

    /// FLAK: 무유도 대공 로켓. 누르고 있으면 0.12초마다 연사, 적 항공기 마커 중심이 12m 안을 지나면 터져 범위(20m)로 맞힌다.
    /// 락온이 없어 기총처럼 앞을 겨눠 쏜다 — 편대·폭격기·헬기 상대. 강화는 특수 대공 탭(피해·탄수·장전).
    private static void ApplyFLAK(ACO_DFUNC_Missile m)
    {
        m.UpgradeFamily = ACO_UpgradeFamily.SpecialAAM;
        m.TargetLayers = 0;
        m.MaxLockTargets = 1;
        m.AllowUnguidedFire = true;
        m.HoldToFire = true;
        m.FireInterval = 0.12f;
        m.SlotCount = 6;
        m.MaxAmmo = 60;
        m.SlotReloadTime = 3f;
        m.Damage = 90f;
        m.SplashRadius = 20f;
        m.MinSplashFraction = 0.4f;
        m.MaxSpeed = 900f;
        m.Acceleration = 900f;
        m.ProximityRadius = 12f;
        m.ProximityAnyEnemy = true;
        m.InheritVelocity = true;
        m.AllowFiringWhenGrounded = false;
        m.NumPreInstantiated = 8;
    }

    /// CCAA(Counter-Countermeasure AAM): 대응책 대응 시커라 플레어·채프·노칭이 통하지 않는다. 대신 락온이 느리고(2.5초) 좁으며(30도) 덜 돈다(70).
    private static void ApplyCCAA(ACO_DFUNC_Missile m)
    {
        Guided(m, false, 1, 2, 12, 12f, 2000f, 30f, 2.5f, 200f, 900f, 300f, 70f, 9f);
        m.IgnoreCountermeasures = true;
    }

    /// ADAM(Area Denial Air Munition): 투하(0.8초) 뒤 점화해 느리게(최고 300m/s — 적기보다 조금 빠름) 가장 가까운 적을 쫓는다(락온했으면 그 표적, 전방위 시커).
    /// 표적이 아니어도 적 항공기가 35m 안을 지나면 터지고, 범위 피해 120m로 편대를 한꺼번에 친다. 표적이 먼저 격추되면 가장 가까운 다른 적으로 옮긴다.
    /// 플레어에 속아도 플레어 곁에서 터지므로 범위 안의 표적은 맞는다. 슬롯 1 / 6발 / 18초.
    private static void ApplyADAM(ACO_DFUNC_Missile m)
    {
        Guided(m, false, 1, 1, 6, 18f, 4000f, 45f, 1.5f, 400f, 300f, 80f, 60f, 35f);
        m.MissileSplash = true;
        m.SplashRadius = 120f; m.MinSplashFraction = 0.5f;
        m.ProximityAnyEnemy = true;
        m.AutoTargetNearest = true;
        m.DropTime = 0.8f;
        m.SeekerAngle = 180f;
        m.MissileLifetime = 35f;
    }

    /// 없는 예제만 만든다(이미 있는 예제·복제한 무장은 건드리지 않는다). 새 예제 무장을 기존 카탈로그에 더할 때.
    [MenuItem("ACO/Weapons/Add Missing Special Weapon Examples")]
    public static void AddMissingExamples()
    {
        CreateExamples(false);
        Debug.Log($"[ACO] 없는 특수무장 예제를 {Folder}에 만들었습니다(있던 것은 그대로). 셋업 검사 [고치기] 또는 기체 셋업으로 기체에 넣으세요.");
    }

    // ───────────── 설치 ─────────────

    /// 폴더의 무장 정의(목록 순서 → ID 순).
    public static List<ACO_SpecialWeaponDef> LoadDefs()
    {
        var defs = new List<ACO_SpecialWeaponDef>();
        if (!AssetDatabase.IsValidFolder(Folder)) { return defs; }
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
        {
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            ACO_SpecialWeaponDef def = go != null ? go.GetComponent<ACO_SpecialWeaponDef>() : null;
            if (def != null) { defs.Add(def); }
        }
        defs.Sort((a, b) => a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : string.CompareOrdinal(a.Id, b.Id));
        return defs;
    }

    /// 쓸 수 있는 정의(ID 빈칸·중복·모듈 없음은 오류를 남기고 뺀다).
    public static List<ACO_SpecialWeaponDef> LoadValidDefs()
    {
        var seen = new HashSet<string>();
        var valid = new List<ACO_SpecialWeaponDef>();
        foreach (ACO_SpecialWeaponDef d in LoadDefs())
        {
            if (string.IsNullOrEmpty(d.Id)) { Debug.LogError($"[ACO] 특수무장 정의 '{d.name}'의 Id가 비어 있어 건너뜁니다.", d); continue; }
            if (!seen.Add(d.Id)) { Debug.LogError($"[ACO] 특수무장 ID '{d.Id}'가 둘 이상입니다('{d.name}') — 건너뜁니다.", d); continue; }
            if (d.Module == null) { Debug.LogError($"[ACO] 특수무장 정의 '{d.name}'에 Module(자식의 ACO_DFUNC_Missile)이 없어 건너뜁니다.", d); continue; }
            valid.Add(d);
        }
        return valid;
    }

    /// 셋업 검사 [고치기]: 씬의 모든 특수무장 슬롯에 없는 모듈을 넣고 격납고 목록을 맞춘 뒤 Bake.
    [MenuItem("ACO/Weapons/Install Special Weapons")]
    public static void Install()
    {
        int modules = InstallModules();
        InstallEcm();
        ACO_SceneBaker.BakeNeighbors();   // Bake가 격납고 목록도 맞춘다(SyncLoadouts)
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 특수무장 카탈로그: 기체 모듈 {modules}개를 넣고 격납고 목록을 맞췄습니다.");
    }

    private const string EcmLoopPath = "Assets/ACO_Addon/Audio/Generated/ACO_Ecm_Loop.wav";

    private static ACO_RadarContacts FindEcmHub()
    {
        foreach (ACO_DFUNC_Missile m in Object.FindObjectsOfType<ACO_DFUNC_Missile>(true))
        {
            if (m.EcmMode) { return Object.FindObjectOfType<ACO_RadarContacts>(true); }
        }
        return null;
    }

    /// ECM 모듈이 있는데 구역 소리가 없는가.
    public static bool EcmSoundMissing()
    {
        ACO_RadarContacts contacts = FindEcmHub();
        return contacts != null && contacts.transform.Find("EcmSound") == null;
    }

    /// ECM 설치: 먼저 들어간 모듈의 미사일을 비우고, 구역 안에서 들리는 2D 루프를 만든다. 연결은 Bake가 한다.
    private static void InstallEcm()
    {
        ACO_RadarContacts contacts = FindEcmHub();
        if (contacts == null) { return; }
        foreach (ACO_DFUNC_Missile m in Object.FindObjectsOfType<ACO_DFUNC_Missile>(true))
        {
            if (!m.EcmMode || m.Missile == null) { continue; }
            Undo.RecordObject(m, "ACO ECM");
            m.Missile = null;
            PrefabUtility.RecordPrefabInstancePropertyModifications(m);
            EditorUtility.SetDirty(m);
        }
        Transform soundObj = contacts.transform.Find("EcmSound");
        if (soundObj == null)
        {
            soundObj = new GameObject("EcmSound").transform;
            soundObj.SetParent(contacts.transform, false);
            Undo.RegisterCreatedObjectUndo(soundObj.gameObject, "ACO ECM sound");
        }
        AudioSource sound = soundObj.GetComponent<AudioSource>();
        if (sound == null) { sound = soundObj.gameObject.AddComponent<AudioSource>(); }
        if (sound.clip != null) { return; }
        sound.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(EcmLoopPath);
        sound.loop = true;
        sound.playOnAwake = false;
        sound.spatialBlend = 0f;
        sound.volume = 0.45f;
        EditorUtility.SetDirty(sound);
    }


    /// 씬의 모든 특수무장 슬롯에 없는 모듈을 넣는다(새 예제 정의부터 만든다). 넣은 수.
    /// 기체가 프리팹이면 기체 프리팹 파일에 넣는다 — 씬의 기체는 프리팹을 따라간다(슬롯 배열을 씬에서 덮어쓴 기체는 되돌린다: SyncSceneSlots).
    /// 프리팹이 아닌 씬 기체만 씬에 넣는다.
    private static int InstallModules()
    {
        EnsureExamples();
        int modules = 0;
        var prefabs = new List<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                SaccEntity entity = special.GetComponentInParent<SaccEntity>(true);
                string path = entity != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(entity.gameObject) : null;
                if (!string.IsNullOrEmpty(path))
                {
                    if (!prefabs.Contains(path)) { prefabs.Add(path); }
                    continue;
                }
                ACO_DFUNC_Missile primary = FindPrimary(entity);
                if (entity == null || primary == null)
                {
                    Debug.LogWarning($"[ACO] 특수무장 슬롯 '{special.name}': 기체 또는 일반 미사일(ACO_DFUNC_Missile)을 찾지 못해 모듈을 넣지 못했습니다.", special);
                    continue;
                }
                modules += InstallVehicle(entity, special, primary, null);
            }
        }
        foreach (string path in prefabs)
        {
            modules += InstallInPrefab(path);
            SyncSceneSlots(path);
        }
        return modules;
    }

    /// 기체 프리팹 파일 하나의 특수무장 슬롯에 없는 모듈을 넣고 저장한다. 넣은 수.
    private static int InstallInPrefab(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int n = 0;
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                SaccEntity entity = special.GetComponentInParent<SaccEntity>(true);
                ACO_DFUNC_Missile primary = FindPrimary(entity);
                if (entity == null || primary == null)
                {
                    Debug.LogWarning($"[ACO] {path}의 특수무장 슬롯 '{special.name}': 기체 또는 일반 미사일을 찾지 못해 모듈을 넣지 못했습니다.");
                    continue;
                }
                n += InstallVehicle(entity, special, primary, null);
            }
            if (n > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] 특수무장 모듈 {n}개를 기체 프리팹에 넣었습니다: {path}");
            }
            return n;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static readonly string[] SlotArrays = { "Modules", "ModuleIds", "ModuleVisuals", "ModuleHudLabels", "ModuleMfdLabels", "ModuleDialIcons" };

    /// 씬의 이 프리팹 기체들: 특수무장 슬롯 배열을 씬에서 덮어쓴 것(예전에 씬에 설치하던 때 남은 것)은 프리팹 값으로 되돌린다 —
    /// 그대로 두면 덮어쓴 배열 길이에 막혀 프리팹에 새로 넣은 모듈이 씬 기체에 안 나온다. 되돌려 슬롯에서 빠진 씬 전용 모듈(같은 ID가 프리팹에 있는 중복)은 끈다.
    public static void SyncSceneSlots(string path)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(special.gameObject) != path) { continue; }
                var so = new SerializedObject(special);
                bool reverted = false;
                foreach (string name in SlotArrays)
                {
                    SerializedProperty p = so.FindProperty(name);
                    if (p == null || !p.prefabOverride) { continue; }
                    PrefabUtility.RevertPropertyOverride(p, InteractionMode.UserAction);
                    reverted = true;
                }
                if (!reverted) { continue; }
                SaccEntity entity = special.GetComponentInParent<SaccEntity>(true);
                Debug.Log($"[ACO] '{(entity != null ? entity.name : special.name)}': 씬에서 덮어쓴 특수무장 슬롯 배열을 프리팹 값으로 되돌렸습니다.", special);
                foreach (ACO_DFUNC_Missile m in special.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
                {
                    if (!PrefabUtility.IsAddedGameObjectOverride(m.gameObject) || !m.gameObject.activeSelf) { continue; }
                    if (special.Modules != null && System.Array.IndexOf(special.Modules, (UdonSharpBehaviour)m) >= 0) { continue; }
                    Undo.RecordObject(m.gameObject, "ACO special slot sync");
                    m.gameObject.SetActive(false);
                    Debug.Log($"[ACO] '{m.name}': 슬롯에 없는 씬 전용 중복 모듈이라 껐습니다(지우지 않음 — 필요 없으면 직접 지우세요).", m);
                }
            }
        }
    }

    /// 셋업 검사용: 이 씬에서 특수무장 슬롯 배열을 덮어쓴 프리팹 기체 수.
    public static int CountSceneSlotOverrides()
    {
        int n = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(special)) { continue; }
                var so = new SerializedObject(special);
                foreach (string name in SlotArrays)
                {
                    SerializedProperty p = so.FindProperty(name);
                    if (p != null && p.prefabOverride) { n++; break; }
                }
            }
        }
        return n;
    }

    /// 셋업 검사 [고치기]: 씬 기체의 슬롯 배열 덮어쓰기를 프리팹 값으로 되돌린다(Bake로 다시 잇는다).
    public static void RevertSceneSlotOverrides()
    {
        var paths = new List<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(special.gameObject);
                if (!string.IsNullOrEmpty(path) && !paths.Contains(path)) { paths.Add(path); }
            }
        }
        foreach (string path in paths) { SyncSceneSlots(path); }
        ACO_SceneBaker.BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// Bake가 부른다: 씬의 격납고 목록을 정의에 맞춘다(바뀐 게 있을 때만 기록). 정의가 없으면 아무것도 안 한다.
    public static void SyncLoadouts()
    {
        List<ACO_SpecialWeaponDef> defs = LoadValidDefs();
        if (defs.Count == 0) { return; }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Loadout loadout in root.GetComponentsInChildren<ACO_Loadout>(true))
            {
                int added = AddToLoadout(loadout, defs);
                if (added > 0) { Debug.Log($"[ACO] 격납고 '{loadout.name}' 목록에 특수무장 {added}개를 더했습니다.", loadout); }
            }
        }
    }

    /// 기체 하나: 정의마다 슬롯에 같은 ID 모듈이 없으면 넣는다. 같은 ID가 SAV 기능 모듈(예전 방식)이면 빼고 ACO 모듈로 바꾼다. 넣은 수.
    public static int InstallVehicle(SaccEntity entity, ACO_DFUNC_Special special, ACO_DFUNC_Missile primary, Text fallbackAmmo)
    {
        int n = 0;
        foreach (ACO_SpecialWeaponDef d in LoadValidDefs())
        {
            int existing = special.ModuleIds != null ? System.Array.IndexOf(special.ModuleIds, d.Id) : -1;
            DFUNC_Bomb savSource = null;
            if (existing >= 0)
            {
                UdonSharpBehaviour old = special.Modules != null && existing < special.Modules.Length ? special.Modules[existing] : null;
                if (old is ACO_DFUNC_Missile) { continue; }
                Undo.RecordObject(special, "ACO special module");
                special.Modules = ACO_PlayerMissileSetup.RemoveAt(special.Modules, existing);
                special.ModuleIds = ACO_PlayerMissileSetup.RemoveAt(special.ModuleIds, existing);
                if (special.ModuleVisuals != null && existing < special.ModuleVisuals.Length) { special.ModuleVisuals = ACO_PlayerMissileSetup.RemoveAt(special.ModuleVisuals, existing); }
                if (special.ModuleHudLabels != null && existing < special.ModuleHudLabels.Length) { special.ModuleHudLabels = ACO_PlayerMissileSetup.RemoveAt(special.ModuleHudLabels, existing); }
                if (special.ModuleMfdLabels != null && existing < special.ModuleMfdLabels.Length) { special.ModuleMfdLabels = ACO_PlayerMissileSetup.RemoveAt(special.ModuleMfdLabels, existing); }
                if (special.ModuleDialIcons != null && existing < special.ModuleDialIcons.Length) { special.ModuleDialIcons = ACO_PlayerMissileSetup.RemoveAt(special.ModuleDialIcons, existing); }
                savSource = old as DFUNC_Bomb;
                if (old != null) { ACO_PlayerMissileSetup.UdonBehaviourEnabled(old, false); }
                Debug.Log($"[ACO] '{entity.name}': 예전 SAV 모듈 {d.Id}('{(old != null ? old.name : "-")}')을 ACO 모듈로 바꿉니다.", special);
            }
            if (InstallModule(entity, special, primary, d, fallbackAmmo, savSource)) { n++; }
        }
        return n;
    }

    /// 격납고 목록: 없는 ID는 더하고, 있는 ID는 이름·설명·가격·아이콘을 정의 값으로 맞춘 뒤 정의 순서(SortOrder)로 정렬한다. 새로 더한 수. 그대로면 기록하지 않는다.
    private static int AddToLoadout(ACO_Loadout loadout, List<ACO_SpecialWeaponDef> defs)
    {
        var ids = new List<string>(loadout.SpecialIds ?? new string[0]);
        int n = ids.Count;
        var names = Pad(loadout.SpecialNames, n, i => ids[i]);
        var descs = Pad(loadout.SpecialDescriptions, n, i => "");
        var prices = Pad(loadout.SpecialPrices, n, i => 0);
        var icons = Pad(loadout.SpecialIcons, n, i => ACO_HUDBuilder.SpecialIcon(ids[i]));
        var cats = Pad(loadout.SpecialCategories, n, i => ACO_Loadout.CAT_OTHER);
        int added = 0;
        foreach (ACO_SpecialWeaponDef d in defs)
        {
            int k = ids.IndexOf(d.Id);
            if (k < 0)
            {
                ids.Add(d.Id); names.Add(""); descs.Add(""); prices.Add(0); icons.Add(null); cats.Add(ACO_Loadout.CAT_OTHER);
                k = ids.Count - 1;
                added++;
            }
            names[k] = string.IsNullOrEmpty(d.DisplayName) ? d.Id : d.DisplayName;
            descs[k] = d.Description;
            prices[k] = Mathf.Max(d.Price, 0);
            icons[k] = d.Icon != null ? d.Icon : ACO_HUDBuilder.SpecialIcon(d.Id);
            cats[k] = CategoryOf(d);
        }
        // 순서: 정의 순서(SortOrder — 격납고 패널 기준)로 맞춘다 — 예전엔 패널마다 없는 ID를 끝에 따로 붙여 격납고 · 브리핑실 순서가 달라졌다. 정의에 없는 ID는 끝에 원래 순서로
        var order = new List<int>();
        for (int i = 0; i < ids.Count; i++) { order.Add(i); }
        order.Sort((x, y) =>
        {
            int dx = defs.FindIndex(d => d.Id == ids[x]);
            int dy = defs.FindIndex(d => d.Id == ids[y]);
            if (dx < 0) { dx = 100000 + x; }
            if (dy < 0) { dy = 100000 + y; }
            return dx.CompareTo(dy);
        });
        ids = order.ConvertAll(i => ids[i]);
        names = order.ConvertAll(i => names[i]);
        descs = order.ConvertAll(i => descs[i]);
        prices = order.ConvertAll(i => prices[i]);
        icons = order.ConvertAll(i => icons[i]);
        cats = order.ConvertAll(i => cats[i]);
        if (Same(loadout.SpecialIds, ids) && Same(loadout.SpecialNames, names) && Same(loadout.SpecialDescriptions, descs)
            && Same(loadout.SpecialPrices, prices) && Same(loadout.SpecialIcons, icons) && Same(loadout.SpecialCategories, cats)) { return 0; }
        Undo.RecordObject(loadout, "ACO special catalog");
        loadout.SpecialIds = ids.ToArray();
        loadout.SpecialNames = names.ToArray();
        loadout.SpecialDescriptions = descs.ToArray();
        loadout.SpecialPrices = prices.ToArray();
        loadout.SpecialIcons = icons.ToArray();
        loadout.SpecialCategories = cats.ToArray();
        PrefabUtility.RecordPrefabInstancePropertyModifications(loadout);
        EditorUtility.SetDirty(loadout);
        return added;
    }

    /// 정의의 격납고 분류(ACO_Loadout.CAT_*). Auto면 모듈의 강화 계열로.
    public static int CategoryOf(ACO_SpecialWeaponDef d)
    {
        switch (d.Category)
        {
            case ACO_WeaponCategory.AirToAir: return ACO_Loadout.CAT_AAM;
            case ACO_WeaponCategory.AirToGround: return ACO_Loadout.CAT_AGM;
            case ACO_WeaponCategory.Bomb: return ACO_Loadout.CAT_BOMB;
            case ACO_WeaponCategory.Other: return ACO_Loadout.CAT_OTHER;
        }
        if (d.Module == null) { return ACO_Loadout.CAT_OTHER; }
        switch (d.Module.UpgradeFamily)
        {
            case ACO_UpgradeFamily.SpecialAAM: return ACO_Loadout.CAT_AAM;
            case ACO_UpgradeFamily.SpecialAGM: return ACO_Loadout.CAT_AGM;
            case ACO_UpgradeFamily.SpecialBomb: return ACO_Loadout.CAT_BOMB;
        }
        return ACO_Loadout.CAT_OTHER;
    }

    private static bool Same<T>(T[] a, List<T> b)
    {
        if (a == null || a.Length != b.Count) { return false; }
        for (int i = 0; i < a.Length; i++) { if (!Equals(a[i], b[i])) { return false; } }
        return true;
    }

    private static List<T> Pad<T>(T[] array, int n, System.Func<int, T> fill)
    {
        var list = new List<T>();
        for (int i = 0; i < n; i++) { list.Add(array != null && i < array.Length ? array[i] : fill(i)); }
        return list;
    }

    /// 기체의 일반 미사일(특수무장 슬롯 밖의 ACO_DFUNC_Missile).
    private static ACO_DFUNC_Missile FindPrimary(SaccEntity entity)
    {
        if (entity == null) { return null; }
        foreach (ACO_DFUNC_Missile m in entity.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
        {
            if (m.GetComponentInParent<ACO_DFUNC_Special>(true) == null) { return m; }
        }
        return null;
    }

    /// 정의의 모듈을 슬롯 아래에 복제하고 기체에 묶인 칸을 채운다.
    private static bool InstallModule(SaccEntity entity, ACO_DFUNC_Special special, ACO_DFUNC_Missile primary, ACO_SpecialWeaponDef def,
        Text fallbackAmmo, DFUNC_Bomb savSource)
    {
        GameObject go = Object.Instantiate(def.Module.gameObject, special.transform, false);
        go.name = "Module_" + def.Id;
        Undo.RegisterCreatedObjectUndo(go, "ACO special module");
        ACO_DFUNC_Missile m = go.GetComponent<ACO_DFUNC_Missile>();
        if (m == null) { Object.DestroyImmediate(go); return false; }

        bool ordnance = m.Missile != null && m.Missile.GetComponent<ACO_Ordnance>() != null;
        if (m.Missile == null && !m.EcmMode) { m.Missile = primary.Missile; }   // ECM은 쏠 것이 없다
        m.SAVControl = primary.SAVControl;
        m.HUDControl = primary.HUDControl;
        m.TargetingTransform = primary.TargetingTransform;
        m.FireKey = primary.FireKey;
        m.ObstacleLayers = primary.ObstacleLayers;
        m.WorldParent = primary.WorldParent;
        m.TargetIndicator = null;   // 락온 표시는 표적 마커가 한다
        m.WeaponAnimator = null;

        // 같은 슬롯의 다른 모듈: 폭탄·로켓이면 그 발사 지점·소리, 잔탄 글자는 슬롯 모듈끼리 같이 쓴다(장착된 하나만 쓴다).
        ACO_DFUNC_Missile ordnanceSource = null;
        ACO_DFUNC_Missile textSource = null;
        if (special.Modules != null)
        {
            foreach (UdonSharpBehaviour b in special.Modules)
            {
                var other = b as ACO_DFUNC_Missile;
                if (other == null) { continue; }
                if (textSource == null && (other.AmmoText != null || other.AmmoTextTMP != null || other.AmmoTextTMPUGUI != null)) { textSource = other; }
                if (ordnanceSource == null && other.Missile != null && other.Missile.GetComponent<ACO_Ordnance>() != null
                    && other.LaunchPoints != null && other.LaunchPoints.Length > 0) { ordnanceSource = other; }
            }
        }
        // 폭탄·로켓인데 슬롯에 ACO 폭탄/로켓이 없으면 기체의 SAV 폭탄/로켓(DFUNC_Bomb — 로켓은 이름에 rocket)에서 가져오고, 그 SAV 기능은 다이얼에서 빼고 끈다.
        if (ordnance && ordnanceSource == null)
        {
            DFUNC_Bomb sav = savSource != null ? savSource : FindSavOrdnance(entity, m.Acceleration > 0f);
            if (sav != null)
            {
                if (sav.BombLaunchPoints != null && sav.BombLaunchPoints.Length > 0) { m.LaunchPoints = sav.BombLaunchPoints; }
                if (sav.LaunchSound != null) { m.LaunchSound = sav.LaunchSound; }
                if (sav.WorldParent != null) { m.WorldParent = sav.WorldParent; }
                if (fallbackAmmo == null) { fallbackAmmo = sav.HUDText_Bomb_ammo; }
                Undo.RecordObject(entity, "ACO special module");
                ACO_PlayerMissileSetup.ClearFrom(entity.Dial_Functions_L, sav);
                ACO_PlayerMissileSetup.ClearFrom(entity.Dial_Functions_R, sav);
                PrefabUtility.RecordPrefabInstancePropertyModifications(entity);
                EditorUtility.SetDirty(entity);
                ACO_PlayerMissileSetup.UdonBehaviourEnabled(sav, false);
            }
        }
        if (ordnance && ordnanceSource != null)
        {
            m.LaunchPoints = ordnanceSource.LaunchPoints;
            m.LaunchSound = ordnanceSource.LaunchSound;
        }
        else if (ordnance && m.LaunchPoints != null && m.LaunchPoints.Length > 0) { }   // SAV 폭탄/로켓에서 가져왔다
        else
        {
            m.LaunchPoints = primary.LaunchPoints;
            m.LaunchSound = primary.LaunchSound;
        }
        bool guided = m.TargetLayers.value != 0;
        m.IdleSound = guided ? primary.IdleSound : null;
        m.LockingSound = guided ? primary.LockingSound : null;
        m.LockedSound = guided ? primary.LockedSound : null;
        if (textSource != null)
        {
            m.AmmoText = textSource.AmmoText;
            m.AmmoTextTMP = textSource.AmmoTextTMP;
            m.AmmoTextTMPUGUI = textSource.AmmoTextTMPUGUI;
        }
        else if (fallbackAmmo != null) { m.AmmoText = fallbackAmmo; }
        else { Debug.LogWarning($"[ACO] '{entity.name}': {def.Id} 모듈의 잔탄 글자를 찾지 못했습니다(슬롯에 다른 모듈이 없음). 모듈의 AmmoText를 직접 넣으세요.", m); }

        Undo.RecordObject(special, "ACO special module");
        special.Modules = Append(special.Modules, (UdonSharpBehaviour)m);
        special.ModuleIds = Append(special.ModuleIds, def.Id);
        // 표시 배열은 모듈 수에 맞춰 늘린다(새 칸은 비움 — 인스펙터에서 채운다)
        special.ModuleVisuals = ACO_DFUNC_SpecialEditor.Fit(special.ModuleVisuals, special.Modules.Length);
        special.ModuleHudLabels = ACO_DFUNC_SpecialEditor.Fit(special.ModuleHudLabels, special.Modules.Length);
        special.ModuleMfdLabels = ACO_DFUNC_SpecialEditor.Fit(special.ModuleMfdLabels, special.Modules.Length);
        special.ModuleDialIcons = ACO_DFUNC_SpecialEditor.Fit(special.ModuleDialIcons, special.Modules.Length);
        PrefabUtility.RecordPrefabInstancePropertyModifications(special);
        EditorUtility.SetDirty(special);
        EditorUtility.SetDirty(m);
        if (m.AmmoText != null || m.AmmoTextTMPUGUI != null) { ACO_PlayerMissileSetup.BuildSlotBars(m); }
        Debug.Log($"[ACO] '{entity.name}': 특수무장 모듈 {def.Id}를 넣었습니다(정의 '{def.name}').", m);
        return true;
    }

    private static DFUNC_Bomb FindSavOrdnance(SaccEntity entity, bool rocket)
    {
        foreach (DFUNC_Bomb b in entity.GetComponentsInChildren<DFUNC_Bomb>(true))
        {
            if (b.name.ToLowerInvariant().Contains("rocket") == rocket) { return b; }
        }
        return null;
    }

    private static T[] Append<T>(T[] array, T item)
    {
        var list = new List<T>(array ?? new T[0]) { item };
        return list.ToArray();
    }

    /// 셋업 검사용: 설치가 필요한 정의 수(격납고 목록이나 어떤 기체 슬롯에 없는 ID).
    public static int CountNotInstalled()
    {
        List<ACO_SpecialWeaponDef> defs = LoadDefs();
        if (defs.Count == 0) { return 0; }
        var loadouts = new List<ACO_Loadout>();
        var specials = new List<ACO_DFUNC_Special>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            loadouts.AddRange(root.GetComponentsInChildren<ACO_Loadout>(true));
            specials.AddRange(root.GetComponentsInChildren<ACO_DFUNC_Special>(true));
        }
        int missing = 0;
        foreach (ACO_SpecialWeaponDef d in defs)
        {
            bool ok = true;
            foreach (ACO_Loadout l in loadouts) { if (l.SpecialIds == null || System.Array.IndexOf(l.SpecialIds, d.Id) < 0) { ok = false; } }
            foreach (ACO_DFUNC_Special s in specials) { if (s.ModuleIds == null || System.Array.IndexOf(s.ModuleIds, d.Id) < 0) { ok = false; } }
            if (!ok) { missing++; }
        }
        return missing;
    }
}
