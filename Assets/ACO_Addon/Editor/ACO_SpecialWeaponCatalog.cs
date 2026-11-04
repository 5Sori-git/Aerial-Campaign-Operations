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
///  - 예제 17종(4AAM, UGB, LGB, RKT, LGR, 4AGM, LAGM, LASM, SCM, SASM, LAAM, QAAM, HVAA, HPAA, FLAK, CCAA, ADAM —값은 F/A-18 설정 기준, 미사일 모델 포함): 프리팹 생성 창의 "플레이어 특수무장"(폭탄·로켓 탄체와 함께 — 있는 예제도 다시 만든다).
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
            new Example { Id = "LGB", Name = "LGB", Order = 22, Price = 11000, Icon = "weapon_lgb", Preset = ApplyLGB,
                Projectile = P + "ACO_Bomb.prefab", Ccip = true, Desc = "LASER-GUIDED BOMB\nLOCK A GROUND TARGET AND DROP - STEERS ONTO IT, TIGHTER BLAST" },
            new Example { Id = "LGR", Name = "LGR", Order = 32, Price = 11000, Icon = "weapon_lgr", Preset = ApplyLGR,
                Projectile = P + "ACO_Rocket.prefab", Desc = "LASER-GUIDED ROCKETS\nLOCKED ROCKETS STEER ONTO THE TARGET - SLOWER RELOAD" },
            new Example { Id = "4AGM", Name = "4AGM", Order = 40, Price = 9000, Icon = "weapon_4agm", Preset = Apply4AGM,
                Projectile = P + "ACO_Missile_AGM.prefab", Desc = "MULTI-TARGET AGM\nLOCKS UP TO 4 GROUND TARGETS - ONE MISSILE EACH" },
            new Example { Id = "LAGM", Name = "LAGM", Order = 50, Price = 10000, Icon = "weapon_lagm", Preset = ApplyLAGM,
                Projectile = P + "ACO_Missile_LAGM.prefab", Desc = "LONG-RANGE AGM\nSTRIKES GROUND TARGETS FROM STANDOFF RANGE" },
            new Example { Id = "LASM", Name = "LASM", Order = 55, Price = 12000, Icon = "weapon_lasm", Preset = ApplyLASM,
                Projectile = P + "ACO_Missile_LAGM.prefab", Desc = "LONG-RANGE ANTI-SHIP MISSILE\nSEA-SKIMS TO SHIPS - CANNOT BE INTERCEPTED BY CIWS" },
            new Example { Id = "SCM", Name = "SCM", Order = 58, Price = 14000, Icon = "weapon_scm", Preset = ApplySCM,
                Projectile = P + "ACO_Missile_SCM.prefab", Desc = "STAND-OFF CLUSTER MISSILE\nBURSTS ABOVE THE TARGET - SCATTERS BOMBLETS OVER A WIDE AREA" },
            new Example { Id = "FLAK", Name = "FLAK", Order = 65, Price = 9000, Icon = "weapon_flak", Preset = ApplyFLAK,
                Projectile = P + "ACO_Rocket.prefab", Desc = "PROXIMITY-FUSED AIR-TO-AIR ROCKETS\nUNGUIDED - BURSTS NEAR ANY ENEMY AIRCRAFT" },
            new Example { Id = "SASM", Name = "SASM", Order = 75, Price = 10000, Icon = "weapon_sasm", Preset = ApplySASM,
                Projectile = P + "ACO_Missile_SRAAM.prefab", Desc = "SHORT-RANGE AERIAL SUPPRESSION AAM\nHUGE PROXIMITY BLAST - ALSO HITS NEARBY AIRCRAFT" },
            new Example { Id = "LAAM", Name = "LAAM", Order = 70, Price = 12000, Icon = "weapon_laam", Preset = ApplyLAAM,
                Projectile = P + "ACO_Missile_AAM.prefab", Desc = "LONG-RANGE AAM\nLOCKS AIR TARGETS FROM BEYOND VISUAL RANGE" },
            new Example { Id = "QAAM", Name = "QAAM", Order = 80, Price = 15000, Icon = "weapon_qaam", Preset = ApplyQAAM,
                Projectile = P + "ACO_Missile_SRAAM.prefab", Desc = "QUICK MANEUVER AAM\nWIDE SEEKER, EXTREME TURN RATE FOR DOGFIGHTS" },
            new Example { Id = "HVAA", Name = "HVAA", Order = 90, Price = 13000, Icon = "weapon_hvaa", Preset = ApplyHVAA,
                Projectile = P + "ACO_Missile_AAM.prefab", Desc = "HIGH-VELOCITY AAM\nVERY FAST - HARD TO OUTRUN, LESS AGILE" },
            new Example { Id = "CCAA", Name = "CCAA", Order = 95, Price = 16000, Icon = "weapon_ccaa", Preset = ApplyCCAA,
                Projectile = P + "ACO_Missile_AAM.prefab", Desc = "COUNTER-COUNTERMEASURE AAM\nIGNORES FLARES AND CHAFF - SLOW LOCK, LESS AGILE" },
            new Example { Id = "HPAA", Name = "HPAA", Order = 100, Price = 14000, Icon = "weapon_hpaa", Preset = ApplyHPAA,
                Projectile = P + "ACO_Missile_AAM.prefab", Desc = "HIGH-POWER AAM\nHUGE WARHEAD AND WIDE PROXIMITY FUSE" },
            new Example { Id = "ADAM", Name = "ADAM", Order = 105, Price = 18000, Icon = "weapon_adam", Preset = ApplyADAM,
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
        var known = new HashSet<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Loadout l in root.GetComponentsInChildren<ACO_Loadout>(true))
            {
                if (l.SpecialIds == null) { continue; }
                foreach (string id in l.SpecialIds) { known.Add(id); }
            }
        }
        CreateExamples(false, known);
    }

    /// 예제 정의를 만든다. overwrite면 있는 예제도 다시 만든다(손본 값 초기화 — 복제해 만든 무장은 건드리지 않는다). skipIds의 예제는 만들지 않는다.
    /// 폭탄·로켓 탄체 프리팹이 없으면 같이 만든다.
    public static void CreateExamples(bool overwrite, HashSet<string> skipIds = null)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "SpecialWeapons"); }
        if (!ACO_PrefabBuilder.Exists("ACO_Bomb")) { ACO_PrefabBuilder.BuildBomb(); }
        if (!ACO_PrefabBuilder.Exists("ACO_Rocket")) { ACO_PrefabBuilder.BuildRocket(); }
        if (!ACO_PrefabBuilder.Exists("ACO_Missile_SCM")) { ACO_PrefabBuilder.BuildScmMissile(); }
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
            def.Description = e.Desc;
            def.Price = e.Price;
            def.SortOrder = e.Order;
            def.Icon = ACO_HUDBuilder.LoadIcon(e.Icon);

            var modGo = new GameObject("Module");
            modGo.transform.SetParent(root.transform, false);
            ACO_DFUNC_Missile m = modGo.AddUdonSharpComponent<ACO_DFUNC_Missile>();
            e.Preset(m);
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
        m.SeaSkim = true;
        m.SkimAltitude = 8f;
        m.SkimTerminalDistance = 500f;
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
    public static void Install()
    {
        int modules = InstallModules();
        ACO_SceneBaker.BakeNeighbors();   // Bake가 격납고 목록도 맞춘다(InstallForBake)
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 특수무장 카탈로그: 기체 모듈 {modules}개를 넣고 격납고 목록을 맞췄습니다.");
    }

    /// Bake가 부른다: 새 예제 정의(코드에 더해졌는데 아직 파일이 없는 것)를 만들고, 기체 특수무장 슬롯에 없는 모듈을 넣고, 격납고 목록을 정의에 맞춘다.
    /// 새 무장을 더하고 Bake만 하면 격납고와 기체에 다 들어간다.
    public static void InstallForBake()
    {
        int modules = InstallModules();
        SyncLoadouts();
        if (modules > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[ACO] 특수무장 카탈로그: 기체 모듈 {modules}개를 넣었습니다.");
        }
    }

    /// 씬의 모든 특수무장 슬롯에 없는 모듈을 넣는다(새 예제 정의부터 만든다). 넣은 수.
    private static int InstallModules()
    {
        EnsureExamples();
        int modules = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Special special in root.GetComponentsInChildren<ACO_DFUNC_Special>(true))
            {
                SaccEntity entity = special.GetComponentInParent<SaccEntity>(true);
                ACO_DFUNC_Missile primary = FindPrimary(entity);
                if (entity == null || primary == null)
                {
                    Debug.LogWarning($"[ACO] 특수무장 슬롯 '{special.name}': 기체 또는 일반 미사일(ACO_DFUNC_Missile)을 찾지 못해 모듈을 넣지 못했습니다.", special);
                    continue;
                }
                modules += InstallVehicle(entity, special, primary, null);
            }
        }
        return modules;
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

    /// 격납고 목록: 없는 ID는 끝에 더하고(목록 순서), 있는 ID는 이름·설명·가격·아이콘을 정의 값으로 맞춘다. 새로 더한 수. 그대로면 기록하지 않는다.
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
        if (m.Missile == null) { m.Missile = primary.Missile; }
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
