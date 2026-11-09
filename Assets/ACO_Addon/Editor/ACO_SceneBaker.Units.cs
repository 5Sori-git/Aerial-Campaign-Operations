using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using ACO;

/// NPC 유닛 — 전체 순서는 ACO_SceneBaker.BakeNeighbors.
public partial class ACO_SceneBaker
{
    /// SaccTarget 애니메이터(Target.controller)는 'normal' 상태가 클립 없이 Write Defaults로 모델·락온 표적·콜라이더를 되살린다.
    /// 그런데 격추된 채(TargetExplode — 모델 끔, 파괴 모델 켬, 락온 표적 끔) 풀에서 꺼졌다가 다음 미션에 다시 켜지면 애니메이터가
    /// 다시 바인딩하면서 그 '파괴된 모습'을 기본값으로 잡는다 → 부활해도 파괴 모델이 보이고 락온이 안 되는 유닛이 된다.
    /// Keep Animator State On Disable을 켜면 다시 켤 때 바인딩·상태를 그대로 두므로 처음(살아 있을 때) 잡은 기본값이 유지된다.
    private static void BakeTargetAnimators()
    {
        int fixedCount = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (SaccFlightAndVehicles.SaccTarget t in root.GetComponentsInChildren<SaccFlightAndVehicles.SaccTarget>(true))
            {
                Animator a = t.GetComponent<Animator>();
                if (a == null || a.keepAnimatorStateOnDisable) { continue; }
                a.keepAnimatorStateOnDisable = true;
                MarkChanged(a);
                fixedCount++;
            }
        }
        if (fixedCount > 0) { Debug.Log($"[ACO] SaccTarget 애니메이터 {fixedCount}개에 Keep Animator State On Disable을 켰습니다(풀 재사용 때 파괴된 모습이 기본값으로 잡히지 않게)."); }
    }

    /// 슈퍼에이스 ↔ 같은 기체의 AI(AI.SuperAce가 있어야 생성기가 일반 배치에서 뺀다). 생성기 풀에 없으면 알린다.
    private static void BakeSuperAces()
    {
        var generators = new List<ACO_MissionGenerator>();
        var aces = new List<ACO_SuperAce>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            generators.AddRange(root.GetComponentsInChildren<ACO_MissionGenerator>(true));
            aces.AddRange(root.GetComponentsInChildren<ACO_SuperAce>(true));
        }
        foreach (ACO_SuperAce ace in aces)
        {
            ACO_NPCFighterAI ai = ace.GetComponentInParent<ACO_NPCFighterAI>(true);
            if (ai == null) { Debug.LogWarning($"[ACO] 슈퍼에이스 '{ace.name}'이 NPC 전투기(ACO_NPCFighterAI) 안에 있지 않습니다.", ace); continue; }
            if (ai.SuperAce != ace) { ai.SuperAce = ace; MarkChanged(ai); }
            if (ace.AI != ai) { ace.AI = ai; MarkChanged(ace); }
            bool pooled = false;
            foreach (ACO_MissionGenerator g in generators) { if (g.Fighters != null && System.Array.IndexOf(g.Fighters, ai) >= 0) { pooled = true; } }
            if (!pooled) { Debug.LogWarning($"[ACO] 슈퍼에이스 '{ai.name}'이 미션 생성기 풀(Fighters)에 없습니다 — 생성기의 UnitPoolRoot 아래로 옮기세요(분신 에이스 페이즈가 안 나온다).", ai); }
        }
        // 쌍둥이 규칙·교차 기동: 슈퍼에이스끼리 짝을 잇는다(차례로 둘씩 — 형제 2기면 서로). 남는 하나는 짝 없음.
        // 교차를 정하는 쪽은 형(TAILS가 아닌 쪽 — 둘 다 아니면 앞의 것)
        for (int i = 0; i < aces.Count; i++)
        {
            ACO_SuperAce want = (i % 2 == 0) ? (i + 1 < aces.Count ? aces[i + 1] : null) : aces[i - 1];
            if (aces[i].Partner != want) { aces[i].Partner = want; MarkChanged(aces[i]); }
            bool leader = false;
            if (want != null)
            {
                bool meTails = aces[i].AceName == "TAILS", otherTails = want.AceName == "TAILS";
                leader = meTails != otherTails ? !meTails : (i % 2 == 0);
            }
            LinkCross(aces[i].Cross, want != null ? want.Cross : null, leader, aces[i].GetComponentInParent<ACO_NPCFighterAI>(true));
        }
    }

    /// 교차 기동 부품의 짝·정하는 쪽·AI를 넣는다.
    private static void LinkCross(ACO_CrossManeuver cross, ACO_CrossManeuver partner, bool leader, ACO_NPCFighterAI ai)
    {
        if (cross == null) { return; }
        if (cross.Partner != partner) { cross.Partner = partner; MarkChanged(cross); }
        if (cross.Leader != leader) { cross.Leader = leader; MarkChanged(cross); }
        if (ai != null && cross.AI != ai) { cross.AI = ai; MarkChanged(cross); }
    }

    /// SHADE 편대: AI ↔ SHADE, 풀 순서로 번호 1~(4), 편대 목록, 교차 짝(1·2, 3·4 — 앞 번호가 정한다). 생성기 풀에 없으면 알린다.
    private static void BakeShades()
    {
        var generators = new List<ACO_MissionGenerator>();
        var shades = new List<ACO_ShadeAce>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            generators.AddRange(root.GetComponentsInChildren<ACO_MissionGenerator>(true));
            shades.AddRange(root.GetComponentsInChildren<ACO_ShadeAce>(true));
        }
        ACO_ShadeAce[] squad = shades.ToArray();
        for (int i = 0; i < shades.Count; i++)
        {
            ACO_ShadeAce s = shades[i];
            ACO_NPCFighterAI ai = s.GetComponentInParent<ACO_NPCFighterAI>(true);
            if (ai == null) { Debug.LogWarning($"[ACO] SHADE '{s.name}'이 NPC 전투기(ACO_NPCFighterAI) 안에 있지 않습니다.", s); continue; }
            if (ai.Shade != s) { ai.Shade = s; MarkChanged(ai); }
            if (s.AI != ai) { s.AI = ai; MarkChanged(s); }
            if (s.Number != i + 1) { s.Number = i + 1; MarkChanged(s); }
            if (!SameShades(s.Squad, squad)) { s.Squad = squad; MarkChanged(s); }
            ACO_ShadeAce partner = (i % 2 == 0) ? (i + 1 < shades.Count ? shades[i + 1] : null) : shades[i - 1];
            LinkCross(s.Cross, partner != null ? partner.Cross : null, i % 2 == 0 && partner != null, ai);
            bool pooled = false;
            foreach (ACO_MissionGenerator g in generators) { if (g.Fighters != null && System.Array.IndexOf(g.Fighters, ai) >= 0) { pooled = true; } }
            if (!pooled) { Debug.LogWarning($"[ACO] SHADE '{ai.name}'이 미션 생성기 풀(Fighters)에 없습니다 — 생성기의 UnitPoolRoot 아래로 옮기세요(SHADE 편대 페이즈가 안 나온다).", ai); }
        }
    }

    private static bool SameShades(ACO_ShadeAce[] a, ACO_ShadeAce[] b)
    {
        if (a == null || a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
    }

    /// 레이저 에이스 ↔ 같은 기체의 AI(AI.LaserAce가 있어야 생성기가 일반 배치에서 뺀다), 페이즈 모듈·무전·내 기체, 사일로 연출 ↔ 모듈·구름.
    private static void BakeLaserAces()
    {
        ACO_PhaseLaserAce phase = Object.FindObjectOfType<ACO_PhaseLaserAce>(true);
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
        ACO_CloudField clouds = Object.FindObjectOfType<ACO_CloudField>();
        var generators = new List<ACO_MissionGenerator>();
        var aces = new List<ACO_LaserAce>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            generators.AddRange(root.GetComponentsInChildren<ACO_MissionGenerator>(true));
            aces.AddRange(root.GetComponentsInChildren<ACO_LaserAce>(true));
        }
        foreach (ACO_LaserAce ace in aces)
        {
            ACO_NPCFighterAI ai = ace.GetComponentInParent<ACO_NPCFighterAI>(true);
            if (ai == null) { Debug.LogWarning($"[ACO] 레이저 에이스 '{ace.name}'이 NPC 전투기(ACO_NPCFighterAI) 안에 있지 않습니다.", ace); continue; }
            if (ai.LaserAce != ace) { ai.LaserAce = ace; MarkChanged(ai); }
            if (ace.AI != ai || ace.Phase != phase || ace.Chatter != chatter || ace.Contacts != contacts)
            {
                ace.AI = ai;
                ace.Phase = phase;
                ace.Chatter = chatter;
                ace.Contacts = contacts;
                MarkChanged(ace);
            }
            bool pooled = false;
            foreach (ACO_MissionGenerator g in generators) { if (g.Fighters != null && System.Array.IndexOf(g.Fighters, ai) >= 0) { pooled = true; } }
            if (!pooled) { Debug.LogWarning($"[ACO] 레이저 에이스 '{ai.name}'이 미션 생성기 풀(Fighters)에 없습니다 — 생성기의 UnitPoolRoot 아래로 옮기세요(레이저 에이스 페이즈가 안 나온다).", ai); }
        }
        if (phase == null) { return; }
        if (phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }
        ACO_IcbmSite site = phase.Site != null ? phase.Site : phase.GetComponentInChildren<ACO_IcbmSite>(true);
        if (phase.Site != site) { phase.Site = site; MarkChanged(phase); }
        if (site != null && (site.Phase != phase || site.Clouds != clouds)) { site.Phase = phase; site.Clouds = clouds; MarkChanged(site); }
    }

    /// CIWS 미사일 요격: 요격 CIWS 목록(InterceptProtectLayers가 있는 것)을 모든 무장·NPC 런처에 굽는다. 요격 수치는 ACO_PrefabTuning이 프리팹에 넣는다.
    private static void BakeCiws()
    {
        var sites = new List<ACO_AAGunSite>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_AAGunSite aa in root.GetComponentsInChildren<ACO_AAGunSite>(true))
            {
                if (aa.InterceptProtectLayers.value != 0) { sites.Add(aa); }
            }
        }
        ACO_AAGunSite[] list = sites.ToArray();
        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Missile c in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (SameSites(c.InterceptSites, list)) { continue; }
                c.InterceptSites = list; MarkChanged(c); linked++;
            }
            foreach (ACO_MissileLauncher c in root.GetComponentsInChildren<ACO_MissileLauncher>(true))
            {
                if (SameSites(c.InterceptSites, list)) { continue; }
                c.InterceptSites = list; MarkChanged(c); linked++;
            }
        }
        if (linked > 0) { Debug.Log($"[ACO] 미사일 요격: 요격 CIWS {list.Length}문을 무장·런처 {linked}곳에 연결했습니다."); }
    }

    private static bool SameSites(ACO_AAGunSite[] a, ACO_AAGunSite[] b)
    {
        if (a == null || a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
    }

    /// 요격 기본값(프리팹 빌더·Bake 공용). 함선 CIWS(MarkerLabel "CIWS") = 적 요격: 적 항공기·지상(29, 30)을 노린 미사일, 사거리 1.2km, 방어 반경 600m,
    /// 다른 함선 ×2. 아군 호위 유닛(ACO_EscortUnit, Hostile 아님)의 대공포(이름에 "AAGun") = 아군 요격: 아군 항공기·지상(25, 26), 사거리·반경 1.5km, 배수 1.
    /// 그 밖(지상 대공포·함포 등)은 요격하지 않는다. 기본값을 넣었으면 true. 어느 쪽이든 InterceptDefaultsApplied를 켠다.
    /// 적 CIWS 요격 수치의 판 번호. 수치를 바꾸면 올린다 — 씬·프리팹의 먼저 만든 CIWS에 Bake가 한 번 다시 넣는다.
    public const int CiwsTuneVersion = 1;

    /// 적 CIWS 요격 수치: AAM 최고 속도(600m/s)면 사거리를 1.5초에 지나 혼자서는 겨우 맞출 수 있고, 다음 발까지 쉰다 — 일제 사격이 뚫는다.
    public static void ApplyEnemyCiwsTune(ACO_AAGunSite aa)
    {
        aa.InterceptRange = 900f;
        aa.InterceptReactionTime = 0.5f;
        aa.InterceptKillTime = 1f;
        aa.InterceptCooldown = 1.5f;
        aa.InterceptTuneVersion = CiwsTuneVersion;
    }

    /// 대공포 탄 도착 판정 반경: 아직 없으면 넣는다. CIWS는 조밀한 탄, 그 밖의 포는 큰 탄이라 넓다. 넣었으면 true.
    public static bool ApplyShellRadius(ACO_AAGunSite aa)
    {
        if (aa.Gun == null || aa.Gun.ShellHitRadius > 0f) { return false; }
        aa.Gun.ShellHitRadius = aa.MarkerLabel == "CIWS" ? 20f : 45f;
        return true;
    }

    public static bool ApplyInterceptDefaults(ACO_AAGunSite aa)
    {
        aa.InterceptDefaultsApplied = true;
        if (aa.InterceptProtectLayers.value != 0) { return false; }   // 이미 직접 정했다 — 덮어쓰지 않는다
        ACO_EscortUnit escort = aa.GetComponentInParent<ACO_EscortUnit>(true);
        if (aa.MarkerLabel == "CIWS")
        {
            aa.InterceptProtectLayers = (1 << 29) | (1 << 30);
            aa.DefenseRadius = 600f;
            aa.OtherDefenseFactor = 2f;
            ApplyEnemyCiwsTune(aa);
            return true;
        }
        else if (escort != null && !escort.Hostile && aa.name.Contains("AAGun"))
        {
            aa.InterceptProtectLayers = (1 << 25) | (1 << 26);
            aa.InterceptRange = 1500f;
            aa.DefenseRadius = 1500f;
            aa.OtherDefenseFactor = 1f;
        }
        else { return false; }
        aa.InterceptReactionTime = 0.5f;
        aa.InterceptKillTime = 0.5f;
        return true;
    }

    private const int TelPoolSize = 3;

    /// 유닛 풀에 없는 필수 유닛(프리팹 이름). Setup Check가 보여 주고 [고치기]가 AddPoolUnits로 넣는다.
    public static List<string> MissingPoolUnits(Transform poolRoot)
    {
        var missing = new List<string>();
        if (poolRoot.GetComponentInChildren<ACO_FlyingFortress>(true) == null) { missing.Add("NPC_Fortress"); }
        int tels = poolRoot.GetComponentsInChildren<ACO_TelUnit>(true).Length;
        for (int i = tels; i < TelPoolSize; i++) { missing.Add("NPC_TEL"); }
        bool hasFlagship = false;
        foreach (ACO_EscortUnit e in poolRoot.GetComponentsInChildren<ACO_EscortUnit>(true)) { if (e.Role == ACO_EscortUnit.ROLE_FLAGSHIP) { hasFlagship = true; } }
        if (!hasFlagship) { missing.Add("NPC_Flagship"); }
        bool hasCarrier = false;
        foreach (ACO_Warship s in poolRoot.GetComponentsInChildren<ACO_Warship>(true)) { if (s.ReservedForFleet) { hasCarrier = true; } }
        if (!hasCarrier) { missing.Add("NPC_Carrier"); }
        if (poolRoot.GetComponentInChildren<ACO_Railgun>(true) == null) { missing.Add("NPC_Railgun"); }
        if (poolRoot.GetComponentInChildren<ACO_LandShip>(true) == null) { missing.Add(ACO_PrefabBuilder.LandShipPrefab); }
        return missing;
    }

    public static void AddPoolUnits(Transform poolRoot)
    {
        foreach (string prefabName in MissingPoolUnits(poolRoot))
        {
            GameObject prefab = ACO_PrefabBuilder.Exists(prefabName)
                ? AssetDatabase.LoadAssetAtPath<GameObject>(ACO_PrefabBuilder.PathOf(prefabName))
                : BuildPoolUnit(prefabName);
            if (prefab == null) { continue; }
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, poolRoot);
            Undo.RegisterCreatedObjectUndo(go, "ACO pool unit");
            go.SetActive(false);
            MarkChanged(go);
            Debug.Log($"[ACO] {prefabName}을 유닛 풀에 넣었습니다.", go);
        }
    }

    private static GameObject BuildPoolUnit(string prefabName)
    {
        switch (prefabName)
        {
            case "NPC_Fortress": return ACO_PrefabBuilder.BuildFortress();
            case "NPC_TEL": return ACO_PrefabBuilder.BuildTel();
            case "NPC_Flagship": return ACO_PrefabBuilder.BuildFlagship();
            case "NPC_Carrier": return ACO_PrefabBuilder.BuildCarrier();
            case "NPC_Railgun": return ACO_PrefabBuilder.BuildRailgun();
            case ACO_PrefabBuilder.LandShipPrefab: return ACO_PrefabBuilder.BuildLandShip();
        }
        return null;
    }


    private static void BakeRailgun()
    {
        ACO_Railgun gun = Object.FindObjectOfType<ACO_Railgun>(true);
        ACO_PhaseRailgun phase = Object.FindObjectOfType<ACO_PhaseRailgun>(true);
        if (gun == null) { return; }
        if (phase == null) { Debug.LogWarning("[ACO] 랜스 포대: 페이즈 모듈(ACO_PhaseRailgun)이 없습니다.", gun); return; }
        ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
        ACO_CloudField clouds = Object.FindObjectOfType<ACO_CloudField>();
        if (gun.Phase != phase || gun.Contacts != contacts || gun.Clouds != clouds) { gun.Phase = phase; gun.Contacts = contacts; gun.Clouds = clouds; MarkChanged(gun); }
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        if (phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }
        if (gun.MiniGuns != null)
        {
            foreach (ACO_MiniRailgun mini in gun.MiniGuns)
            {
                if (mini == null || (mini.Phase == phase && mini.Contacts == contacts)) { continue; }
                mini.Phase = phase;
                mini.Contacts = contacts;
                MarkChanged(mini);
            }
        }
    }

    /// 육상전함 ↔ 보스 페이즈(동기화된 항로·단계), 지면 레이어·해수면(생성기 값), 소형 레일건 레이더, 프리팹에 직접 놓은 VLS·CIWS를 부위로.
    private static void BakeLandShip()
    {
        ACO_LandShip ship = Object.FindObjectOfType<ACO_LandShip>(true);
        ACO_PhaseLandShip phase = Object.FindObjectOfType<ACO_PhaseLandShip>(true);
        if (ship == null) { return; }
        if (phase == null) { Debug.LogWarning("[ACO] 육상전함: 페이즈 모듈(ACO_PhaseLandShip)이 없습니다.", ship); return; }
        if (ship.Phase != phase) { ship.Phase = phase; MarkChanged(ship); }
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        if (phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }
        int hullBit = 1 << ACO_PrefabBuilder.LayerFortressHull;
        ACO_MissionGenerator generator = Object.FindObjectOfType<ACO_MissionGenerator>(true);
        if (generator != null)
        {
            if ((generator.GroundLayers.value & hullBit) != 0) { generator.GroundLayers = generator.GroundLayers.value & ~hullBit; MarkChanged(generator); }
            int ground = generator.GroundLayers.value & ~hullBit;
            if (ship.GroundLayers.value != ground || !Mathf.Approximately(ship.SeaLevel, generator.SeaLevel))
            {
                ship.GroundLayers = ground;
                ship.SeaLevel = generator.SeaLevel;
                MarkChanged(ship);
            }
        }
        ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
        foreach (ACO_MiniRailgun mini in ship.GetComponentsInChildren<ACO_MiniRailgun>(true))
        {
            if (mini.Contacts == contacts) { continue; }
            mini.Contacts = contacts;
            MarkChanged(mini);
        }
        if (ACO_PrefabBuilder.LinkLandShipWeapons(ship)) { MarkChanged(ship.Warship); }
    }

    /// 지상 유닛 숙련도 체력의 기준(BaseHealth, 베테랑)을 SaccTarget 체력으로 맞춘다 — 동기화되는 Health는 게임 중 믿을 수 없다(늦게 들어온 클라이언트에겐 깎인 값).
    private static void BakeTierHealth()
    {
        int changed = 0;
        foreach (ACO_AAGunSite u in Object.FindObjectsOfType<ACO_AAGunSite>(true))
        {
            if (u.TargetHealth != null && u.BaseHealth != u.TargetHealth.Health) { u.BaseHealth = u.TargetHealth.Health; MarkChanged(u); changed++; }
        }
        foreach (ACO_SAMSite u in Object.FindObjectsOfType<ACO_SAMSite>(true))
        {
            if (u.TargetHealth != null && u.BaseHealth != u.TargetHealth.Health) { u.BaseHealth = u.TargetHealth.Health; MarkChanged(u); changed++; }
        }
        foreach (ACO_MobileUnit u in Object.FindObjectsOfType<ACO_MobileUnit>(true))
        {
            if (u.TargetHealth != null && u.BaseHealth != u.TargetHealth.Health) { u.BaseHealth = u.TargetHealth.Health; MarkChanged(u); changed++; }
        }
        foreach (ACO_Warship u in Object.FindObjectsOfType<ACO_Warship>(true))
        {
            if (u.TargetHealth != null && u.BaseHealth != u.TargetHealth.Health) { u.BaseHealth = u.TargetHealth.Health; MarkChanged(u); changed++; }
        }
        if (changed > 0) { Debug.Log($"[ACO] 지상 유닛 {changed}곳의 기준 체력(BaseHealth, 베테랑)을 채웠습니다."); }
    }

    /// 함대전: 무전 연결. 함선 수치는 ACO_PrefabTuning이 프리팹에 넣는다.
    private static void BakeFleet()
    {
        ACO_PhaseFleet phase = Object.FindObjectOfType<ACO_PhaseFleet>(true);
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        if (phase != null && phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }
    }


    /// 시한 표적 ↔ TEL: 발사·상승 시간(모듈 값), 끝 고도(씬 SAV 최대 고도 — 대기가 0이 되는 높이), 무전.
    private static void BakeTimeSensitive()
    {
        ACO_PhaseTimeSensitive phase = Object.FindObjectOfType<ACO_PhaseTimeSensitive>(true);
        if (phase == null) { return; }
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        if (phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }
        const string oldStart = "High-value targets are on the move. Hit each one before it escapes!";
        if (phase.StartText == oldStart)
        {
            phase.StartText = "Ballistic missile launchers are setting up! Stop every launch!";
            phase.SuccessText = "All launchers are down. No missiles got through.";
            phase.FailText = "MISSILE LAUNCHED";
            phase.FailChatterText = "The missile is out of reach. We failed. All aircraft, return to base.";
            MarkChanged(phase);
        }
        SaccFlightAndVehicles.SaccAirVehicle sav = Object.FindObjectOfType<SaccFlightAndVehicles.SaccAirVehicle>(true);
        float ceiling = sav != null ? sav.AtmosphereThinningEnd + sav.SeaLevel : 19812f;
        foreach (ACO_TelUnit tel in Object.FindObjectsOfType<ACO_TelUnit>(true))
        {
            float flight = Mathf.Max(phase.TimeLimit - phase.LaunchDelay, 1f);
            if (Mathf.Approximately(tel.LaunchDelay, phase.LaunchDelay) && Mathf.Approximately(tel.FlightTime, flight) && Mathf.Approximately(tel.CeilingAltitude, ceiling)) { continue; }
            tel.LaunchDelay = phase.LaunchDelay;
            tel.FlightTime = flight;
            tel.CeilingAltitude = ceiling;
            MarkChanged(tel);
        }
    }

    /// 공중 요새 ↔ 보스 페이즈(동기화된 단계·기준값).
    private static void BakeFortress()
    {
        ACO_FlyingFortress fortress = Object.FindObjectOfType<ACO_FlyingFortress>(true);
        ACO_PhaseFortress phase = Object.FindObjectOfType<ACO_PhaseFortress>(true);
        if (fortress == null) { return; }
        if (phase == null) { Debug.LogWarning("[ACO] 공중 요새: 페이즈 모듈(ACO_PhaseFortress)이 없습니다.", fortress); return; }
        if (fortress.Phase != phase) { fortress.Phase = phase; MarkChanged(fortress); }
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        if (phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }

        // 땅을 재는 쪽은 요새 외피 레이어를 뺀다 — 위에서 쏘는 레이가 선체에 맞으면 유닛이 그 위에 놓인다
        int hullBit = 1 << ACO_PrefabBuilder.LayerFortressHull;
        foreach (ACO_MissionGenerator g in Object.FindObjectsOfType<ACO_MissionGenerator>(true))
        {
            if ((g.GroundLayers.value & hullBit) != 0) { g.GroundLayers = g.GroundLayers.value & ~hullBit; MarkChanged(g); }
        }
        foreach (ACO_MobileUnit u in Object.FindObjectsOfType<ACO_MobileUnit>(true))
        {
            if ((u.GroundLayers.value & hullBit) != 0) { u.GroundLayers = u.GroundLayers.value & ~hullBit; MarkChanged(u); }
        }
        foreach (ACO_EscortUnit u in Object.FindObjectsOfType<ACO_EscortUnit>(true))
        {
            if ((u.GroundLayers.value & hullBit) != 0) { u.GroundLayers = u.GroundLayers.value & ~hullBit; MarkChanged(u); }
        }
    }
}
