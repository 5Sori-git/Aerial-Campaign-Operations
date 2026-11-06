using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ACO;

/// 페이즈 모듈(ACO_PhaseModule 상속)을 미션 관리자 아래에 만든다. 모듈 목록 연결은 Bake(ACO_SceneBaker.BakePhaseModules)가 한다.
///  - Create Default Phase Modules: 기본 모듈 10개(타격·방공망·할당량·시한 표적·제공권·보스·호위·지상 호위·요격·보너스 슈퍼에이스)를 기본값과 기본 대사로.
///    이름이 같은 모듈이 이미 있으면 건너뛴다(새 씬, 지운 모듈 되살리기). 값·대사는 만든 뒤 모듈 인스펙터에서 바꾼다.
///  - Add Amphibious Assault Phase: 상륙전.
public static class ACO_PhaseModuleSetup
{
    [MenuItem("ACO/Missions/Add Amphibious Assault Phase")]
    public static void AddAmphibious()
    {
        ACO_MissionManager manager = FindManager();
        if (manager == null) { return; }
        if (manager.GetComponentInChildren<ACO_PhaseAmphibious>(true) != null)
        {
            EditorUtility.DisplayDialog("ACO", "상륙전 페이즈 모듈이 이미 있습니다.", "확인");
            return;
        }
        ACO_PhaseAmphibious m = Create<ACO_PhaseAmphibious>(manager, "Phase_Amphibious");
        m.PhaseName = "AMPHIBIOUS ASSAULT";
        m.Description = "PROTECT THE LANDING CRAFT UNTIL THEY REACH THE BEACH";
        m.FailText = "LANDING CRAFT LOST";
        m.StartText = "LCACs are away. Gunships are moving in ahead of them. Clear the beach and keep those hovercraft alive.";
        m.SuccessText = "The landing craft are on the beach. Troops are going ashore.";
        m.FailChatterText = "All landing craft are down. The assault has failed.";
        m.ClearBonus = 1500;
        m.GrantTravelTime = true;
        m.MinPhase = 2;
        Finish(m);
        Selection.activeGameObject = m.gameObject;
        Debug.Log("[ACO] 상륙전 페이즈 모듈을 미션 관리자 아래에 만들었습니다. 생성기 유닛 풀에 LCAC·아군 공격헬기·강습상륙함·호위함(프리팹 생성 창 → 상륙전 부대)을 두고 Bake하세요.", m);
    }

    [MenuItem("ACO/Missions/Create Default Phase Modules")]
    public static void CreateDefaults()
    {
        ACO_MissionManager mgr = FindManager();
        if (mgr == null) { return; }
        var log = new System.Text.StringBuilder();
        int made = 0;

        if (!HasChild(mgr, "Phase_Strike"))
        {
            ACO_PhaseDestroy m = Create<ACO_PhaseDestroy>(mgr, "Phase_Strike");
            m.Kind = ACO_PhaseDestroy.KIND_STRIKE;
            m.Count = 2;
            Setup(m, "STRIKE", "", 1, false, "Primary target located. All aircraft, destroy the enemy facility.", "Target destroyed. Good work, all aircraft.", "");
            made++; log.Append("\n  타격");
        }
        if (!HasChild(mgr, "Phase_SEAD"))
        {
            ACO_PhaseDestroy m = Create<ACO_PhaseDestroy>(mgr, "Phase_SEAD");
            m.Kind = ACO_PhaseDestroy.KIND_SEAD;
            m.Count = 4;
            m.CountPerPhase = 1;
            Setup(m, "SEAD", "", 1, false, "Enemy air defense network detected. Suppress the SAM and AA sites.", "Enemy air defenses are down. The skies are ours.", "");
            made++; log.Append("\n  방공망 제압");
        }
        if (!HasChild(mgr, "Phase_ScoreAttack"))
        {
            ACO_PhaseQuota m = Create<ACO_PhaseQuota>(mgr, "Phase_ScoreAttack");
            Setup(m, "SCORE ATTACK", "", 1, false, "Free hunting zone. Engage any target and meet the score quota.", "Score quota met. Outstanding hunting.", "");
            made++; log.Append("\n  점수 할당량");
        }
        if (!HasChild(mgr, "Phase_TimeSensitive"))
        {
            ACO_PhaseTimeSensitive m = Create<ACO_PhaseTimeSensitive>(mgr, "Phase_TimeSensitive");
            Setup(m, "TIME SENSITIVE", "MISSILE LAUNCHED", 2, false, "Ballistic missile launchers are setting up! Stop every launch!", "All launchers are down. No missiles got through.",
                "The missile is out of reach. We failed. All aircraft, return to base.");
            made++; log.Append("\n  시한 표적");
        }
        if (!HasChild(mgr, "Phase_AirSuperiority"))
        {
            ACO_PhaseDestroy m = Create<ACO_PhaseDestroy>(mgr, "Phase_AirSuperiority");
            m.Kind = ACO_PhaseDestroy.KIND_AIR;
            m.Count = 4;
            m.CountPerPhase = 1;
            Setup(m, "AIR SUPERIORITY", "", 1, false, "Multiple bogeys inbound. Engage and take control of the airspace.", "Airspace secured. No more contacts.", "");
            made++; log.Append("\n  제공권");
        }
        if (!HasChild(mgr, "Phase_AceSquadron"))
        {
            ACO_PhaseDestroy m = Create<ACO_PhaseDestroy>(mgr, "Phase_AceSquadron");
            m.Kind = ACO_PhaseDestroy.KIND_BOSS;
            m.Count = 4;
            Setup(m, "ACE SQUADRON", "", 3, false, "Caution - an enemy ace squadron has been sighted. Stay sharp.", "The ace squadron has been shot down. Incredible.", "");
            made++; log.Append("\n  보스 편대");
        }
        if (!HasChild(mgr, "Phase_Escort"))
        {
            ACO_PhaseEscort m = Create<ACO_PhaseEscort>(mgr, "Phase_Escort");
            Setup(m, "ESCORT", "VIP LOST", 1, true, "The transport is taking off. Escort it to the drop zone.", "The transport has reached the drop zone. Delivery confirmed.",
                "We lost the transport. Mission failed. Return to base.");
            made++; log.Append("\n  호위");
        }
        if (!HasChild(mgr, "Phase_Convoy"))
        {
            ACO_PhaseConvoy m = Create<ACO_PhaseConvoy>(mgr, "Phase_Convoy");
            Setup(m, "CONVOY", "CONVOY LOST", 1, true, "The convoy is rolling out. Cover it from the ambush.", "The convoy made it through. Good cover.",
                "The convoy is gone. Mission failed. Return to base.");
            made++; log.Append("\n  지상 호위");
        }
        if (!HasChild(mgr, "Phase_Intercept"))
        {
            ACO_PhaseIntercept m = Create<ACO_PhaseIntercept>(mgr, "Phase_Intercept");
            Setup(m, "INTERCEPT", "CITY BOMBED", 1, false, "Enemy bombers are heading for the city! Intercept them before they arrive!", "All bombers down! The city is safe.",
                "The city has been bombed... Mission failed. All units, withdraw.");
            made++; log.Append("\n  폭격기 요격");
        }
        if (!HasChild(mgr, "Phase_Bonus_SuperAce"))
        {
            ACO_PhaseSuperAce m = Create<ACO_PhaseSuperAce>(mgr, "Phase_Bonus_SuperAce");
            Setup(m, "BONUS - {ACE}", "TARGET RETREATED", 1, false, "Unidentified fast mover... it's an ace. Everyone, watch yourselves.", "The ace is going down! I can't believe it!",
                "The ace is withdrawing... he got away.");
            m.BonusOnly = true;
            m.BonusTimeLimit = 240f;
            m.ClearBonus = 15000;
            Finish(m);
            made++; log.Append("\n  보너스(슈퍼에이스)");
        }
        ACO_SceneBaker.BakePhaseModulesNow();
        Debug.Log(made > 0 ? "[ACO] 기본 페이즈 모듈을 만들었습니다(" + made + "개):" + log + "\n  값·대사는 모듈 인스펙터에서 바꾸고 씬을 저장하세요."
                           : "[ACO] 기본 페이즈 모듈 10개가 이미 모두 있습니다.", mgr);
    }

    /// 공중 요새 페이즈(없으면 만든다 — Bake가 부른다). 값·대사는 모듈 인스펙터, 나오는 때는 ACO → Missions → Phase Weights.
    /// 이전 기본값(보너스 전용, 3페이즈부터)으로 만든 모듈은 5번째 페이즈 기본값으로 한 번 옮긴다(직접 고친 값은 건드리지 않는다).
    public static void EnsureFortress(ACO_MissionManager manager)
    {
        ACO_PhaseFortress existing = manager.GetComponentInChildren<ACO_PhaseFortress>(true);
        if (existing != null)
        {
            bool oldBonus = existing.BonusOnly && existing.PhaseName == "BONUS - FLYING FORTRESS";
            bool oldNormal = !existing.BonusOnly && existing.MinPhase == 3 && (existing.PhaseWeights == null || existing.PhaseWeights.Length == 0);
            if (!oldBonus && !oldNormal) { return; }
            existing.PhaseName = "FLYING FORTRESS";
            FortressDefaults(existing);
            EditorUtility.SetDirty(existing);
            Debug.Log("[ACO] 공중 요새 페이즈를 5번째 페이즈 기본값으로 옮겼습니다.", existing);
            return;
        }
        ACO_PhaseFortress m = Create<ACO_PhaseFortress>(manager, "Phase_Fortress");
        Setup(m, "FLYING FORTRESS", "CITY DESTROYED", 1, false,
            "A massive contact is circling the city. It's a flying fortress! Take out its air defenses first!",
            "The core is gone! The fortress is going down! Outstanding work!",
            "The fortress has hit the city... We've lost it. All units, withdraw.");
        FortressDefaults(m);
        m.ClearBonus = 8000;
        Debug.Log("[ACO] 공중 요새 페이즈 모듈 Phase_Fortress를 미션 관리자 아래에 만들었습니다.", m);
    }

    /// 일반 페이즈, 5번째 페이즈에서만 나온다(다른 종류 가중치 1 기준 3배 — 표에서 바꾼다).
    private static void FortressDefaults(ACO_PhaseFortress m)
    {
        m.BonusOnly = false;
        m.MinPhase = 1;
        m.Weight = 1f;
        m.PhaseWeights = new[] { 0f, 0f, 0f, 0f, 3f };
    }

    /// 함대전 지원 페이즈(없으면 만든다 — Bake가 부른다). 4번째 페이즈 위주. 값·대사는 모듈 인스펙터, 나오는 때는 ACO → Missions → Phase Weights.
    public static void EnsureFleet(ACO_MissionManager manager)
    {
        if (manager.GetComponentInChildren<ACO_PhaseFleet>(true) != null) { return; }
        ACO_PhaseFleet m = Create<ACO_PhaseFleet>(manager, "Phase_Fleet");
        Setup(m, "FLEET ACTION", "FLAGSHIP LOST", 1, false,
            "Enemy carrier group ahead. Our fleet is closing to gun range. Support them and sink that carrier!",
            "The carrier is going down! The enemy fleet is breaking off!",
            "The flagship is lost... All units, withdraw.");
        m.PhaseWeights = new[] { 0f, 0f, 0f, 2f, 0.5f };
        m.ClearBonus = 5000;
        m.EnemyName = "CARRIER GROUP";
        Debug.Log("[ACO] 함대전 페이즈 모듈 Phase_Fleet을 미션 관리자 아래에 만들었습니다.", m);
    }

    /// 랜스 포대 페이즈(없으면 만든다 — Bake가 부른다). 5번째 페이즈의 보스 — 요새와 같은 가중치라 지형이 허락하면 반반으로 나온다.
    public static void EnsureRailgun(ACO_MissionManager manager)
    {
        if (manager.GetComponentInChildren<ACO_PhaseRailgun>(true) != null) { return; }
        ACO_PhaseRailgun m = Create<ACO_PhaseRailgun>(manager, "Phase_Railgun");
        Setup(m, "LANCE BATTERY", "BASE DESTROYED", 1, false,
            "Strategic railgun detected, and it is locked on our base! Bring air-to-ground ordnance!",
            "The Lance is down. All aircraft, good kill.",
            "The base is gone. All units, withdraw.");
        m.PhaseWeights = new[] { 0f, 0f, 0f, 0f, 3f };
        m.ClearBonus = 8000;
        m.EnemyName = "LANCE COMMAND";
        Debug.Log("[ACO] 랜스 포대 페이즈 모듈 Phase_Railgun을 미션 관리자 아래에 만들었습니다.", m);
    }

    /// 모듈 이름별 페이즈 가중치 기본값(1~5). 뒤로 갈수록 어려운 임무 — 4는 함대전, 5는 공중 요새 위주.
    private static readonly string[] WeightNames = { "Phase_Strike", "Phase_SEAD", "Phase_AirSuperiority", "Phase_ScoreAttack", "Phase_TimeSensitive", "Phase_Escort", "Phase_Convoy", "Phase_Amphibious", "Phase_Intercept", "Phase_AceSquadron" };
    private static readonly float[][] WeightValues =
    {
        new[] { 3f, 2f, 0.5f, 0.2f, 0.1f },
        new[] { 2f, 2f, 0.5f, 0.2f, 0.1f },
        new[] { 3f, 2f, 0.5f, 0.2f, 0.1f },
        new[] { 0f, 2f, 1f, 0.2f, 0.1f },
        new[] { 0f, 2f, 1.5f, 0.3f, 0.1f },
        new[] { 0f, 2f, 2f, 0.3f, 0.1f },
        new[] { 0f, 0.5f, 3f, 0.5f, 0.1f },
        new[] { 0f, 0f, 3f, 0.5f, 0.1f },
        new[] { 0f, 0.5f, 2f, 0.5f, 0.1f },
        new[] { 0f, 0f, 2f, 1f, 0.3f },
    };

    /// 보스 모듈이 빠졌는가(요새·함대전·랜스 포대).
    public static List<string> MissingBossModules(ACO_MissionManager manager)
    {
        var missing = new List<string>();
        if (manager.GetComponentInChildren<ACO_PhaseFortress>(true) == null) { missing.Add("Phase_Fortress"); }
        if (manager.GetComponentInChildren<ACO_PhaseFleet>(true) == null) { missing.Add("Phase_Fleet"); }
        if (manager.GetComponentInChildren<ACO_PhaseRailgun>(true) == null) { missing.Add("Phase_Railgun"); }
        return missing;
    }

    /// 가중치가 비어 있어 기본값을 받을 모듈 수.
    public static int CountEmptyWeights(ACO_MissionManager manager)
    {
        int n = 0;
        foreach (ACO_PhaseModule m in manager.GetComponentsInChildren<ACO_PhaseModule>(true))
        {
            if (System.Array.IndexOf(WeightNames, m.gameObject.name) >= 0 && !m.BonusOnly && (m.PhaseWeights == null || m.PhaseWeights.Length == 0)) { n++; }
        }
        return n;
    }

    /// Setup Check [고치기]: 빠진 보스 모듈을 넣고 빈 가중치를 채운다.
    public static void AddBossModules(ACO_MissionManager manager)
    {
        EnsureFortress(manager);
        EnsureFleet(manager);
        EnsureRailgun(manager);
        ApplyDefaultWeights(manager);
    }

    /// 페이즈 가중치가 비어 있는 기본 모듈에 기본값을 넣는다(Bake가 부른다 — 직접 넣은 값은 건드리지 않는다).
    public static void ApplyDefaultWeights(ACO_MissionManager manager)
    {
        int applied = 0;
        foreach (ACO_PhaseModule m in manager.GetComponentsInChildren<ACO_PhaseModule>(true))
        {
            int k = System.Array.IndexOf(WeightNames, m.gameObject.name);
            if (k < 0 || m.BonusOnly || (m.PhaseWeights != null && m.PhaseWeights.Length > 0)) { continue; }
            m.PhaseWeights = (float[])WeightValues[k].Clone();
            PrefabUtility.RecordPrefabInstancePropertyModifications(m);
            EditorUtility.SetDirty(m);
            applied++;
        }
        if (applied > 0) { Debug.Log($"[ACO] 페이즈 가중치 기본값을 모듈 {applied}개에 넣었습니다(ACO → Missions → Phase Weights)."); }
    }

    private static ACO_MissionManager FindManager()
    {
        ACO_MissionManager manager = Object.FindObjectOfType<ACO_MissionManager>(true);
        if (manager == null) { EditorUtility.DisplayDialog("ACO", "씬에 미션 관리자(ACO_MissionManager)가 없습니다.", "확인"); }
        return manager;
    }

    private static bool HasChild(ACO_MissionManager mgr, string name)
    {
        foreach (ACO_PhaseModule m in mgr.GetComponentsInChildren<ACO_PhaseModule>(true)) { if (m.gameObject.name == name) { return true; } }
        return false;
    }

    private static T Create<T>(ACO_MissionManager manager, string name) where T : ACO_PhaseModule
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "ACO phase module");
        go.transform.SetParent(manager.transform, false);
        T m = UdonSharpUndo.AddComponent<T>(go);
        m.Manager = manager;
        m.Generator = manager.Generator;
        return m;
    }

    /// 공통 값: 이름·실패 글자, 최소 페이즈, 이동 시간 보너스, 무전 대사(시작·성공·실패 — 실패는 이 페이즈로 미션이 끝났을 때, 보너스면 물러났을 때).
    private static void Setup(ACO_PhaseModule m, string name, string failText, int minPhase, bool travelTime, string start, string success, string fail)
    {
        m.PhaseName = name;
        m.Description = "";   // 비우면 모듈이 기본 안내를 만든다(목표 수·보스 이름이 들어간다)
        if (failText.Length > 0) { m.FailText = failText; }
        m.MinPhase = Mathf.Max(minPhase, 1);
        m.Weight = 1f;
        m.GrantTravelTime = travelTime;
        m.StartText = start;
        m.SuccessText = success;
        m.FailChatterText = fail;
        Finish(m);
    }

    private static void Finish(ACO_PhaseModule m)
    {
        ACO_SyncModeFixer.FixHierarchy(m.gameObject, false);
        EditorUtility.SetDirty(m);
    }
}
