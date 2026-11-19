using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using ACO;
using Object = UnityEngine.Object;

/// 프리팹 수치 변경. 씬이 아니라 프리팹 에셋을 고치고, 씬 인스턴스에 남은 같은 값의 오버라이드를 지운다.
/// Setup Check의 "프리팹 수치" 항목이 부른다. 적용하거나 무시한 항목은 ID를 AppliedPath에 적어 다시 묻지 않는다.
/// 수치를 또 바꾸려면 Entries에 새 ID로 항목을 더한다.
public static class ACO_PrefabTuning
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    private const string AppliedPath = "Assets/ACO_Addon/Editor/ACO_AppliedTuning.txt";
    private const int FriendlyShips = 1 << 26;

    public class Entry
    {
        public string Id;
        public string Text;
        public Action Apply;
    }

    /// icbm-ring-1: 씬의 탄도미사일 사이트 연출을 다시 맞춘다(링 추가, 섬광 · 불덩이 크기).
    private static void ApplyIcbmRing()
    {
        int n = 0;
        foreach (ACO.ACO_IcbmSite site in UnityEngine.Object.FindObjectsOfType<ACO.ACO_IcbmSite>(true))
        {
            ACO_FxBuilder.BuildIcbmFx(site);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(site.gameObject.scene);
            n++;
        }
        UnityEngine.Debug.Log($"[ACO] 탄도미사일 자폭 링: 사이트 {n}곳에 링을 넣었습니다." + (n == 0 ? " (레이저 에이스 페이즈 모듈이 씬에 없습니다)" : " 씬을 저장하세요."));
    }

    /// deck-taxi-5kt-1 · deck-taxi-10kt-1: 플레이어 기체 프리팹의 갑판 택싱 최고 속도(지금 10노트 = 5.14m/s — 6-216, 처음엔 5노트 2.6m/s).
    public const float DeckTaxiSpeed = 5.14f;
    public static void TuneDeckTaxi()
    {
        const string path = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int n = 0;
            foreach (ACO_VehicleAutomation a in root.GetComponentsInChildren<ACO_VehicleAutomation>(true))
            {
                if (Mathf.Approximately(a.DeckTaxiSpeed, DeckTaxiSpeed)) { continue; }
                a.DeckTaxiSpeed = DeckTaxiSpeed;
                n++;
            }
            if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            Debug.Log($"[ACO] 갑판 택싱 속도: {path}의 {n}곳을 {DeckTaxiSpeed}m/s(약 {Mathf.RoundToInt(DeckTaxiSpeed * 1.944f)}노트)로 바꿨습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 씬의 보스 페이즈 모듈(공중 요새·랜스 포대) 방공 수리 값. 모듈은 프리팹이 아니라 씬 오브젝트라 씬을 고친다.
    private static void TuneBossRepairs()
    {
        foreach (ACO_PhaseFortress m in Object.FindObjectsOfType<ACO_PhaseFortress>(true))
        {
            Undo.RecordObject(m, "ACO boss repairs");
            m.RepairCount = 3;
            m.MaxRepairs = 6;
            EditorUtility.SetDirty(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
        }
        foreach (ACO_PhaseRailgun m in Object.FindObjectsOfType<ACO_PhaseRailgun>(true))
        {
            Undo.RecordObject(m, "ACO boss repairs");
            m.RepairCount = 3;
            m.MaxRepairs = 6;
            EditorUtility.SetDirty(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
        }
        Debug.Log("[ACO] 보스 방공 수리: 한 번에 3곳, 최대 6곳으로 맞췄습니다. 씬을 저장하세요.");
    }

    /// 랜스 포대 페이즈 모듈: 코어 열림 30초, 첫 충전 대기 최대 180초(직접 바꾼 값 — 25가 아니면 열림 시간은 건드리지 않는다).
    private static void TuneBorderlessStart()
    {
        int n = 0;
        foreach (ACO_PhaseBorderless m in Object.FindObjectsOfType<ACO_PhaseBorderless>(true))
        {
            if (m.StartText != "Sky Eye to all aircraft. A single enemy ace is inbound. Something about that aircraft is not right. Engage with caution.") { continue; }
            Undo.RecordObject(m, "ACO laser ace start line");
            m.StartText = "All aircraft. A single enemy ace is inbound. Something about that aircraft is not right. Engage with caution.";
            EditorUtility.SetDirty(m);
            n++;
        }
        if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] 레이저 에이스 시작 대사: 모듈 {n}개에서 콜사인을 뺐습니다. 씬을 저장하세요.");
    }

    /// 씬의 LUNA 페이즈 모듈: 격추 뒤 5초 있다가 빗나간 폭격이 내려오기 시작한다(MissDelay 뜻이 "착탄까지"에서 "내려오기 시작까지"로 바뀜 — 예전 기본 4만 고친다).
    private static void TuneLunaMissDelay()
    {
        int n = 0;
        foreach (ACO_PhaseLuna m in Object.FindObjectsOfType<ACO_PhaseLuna>(true))
        {
            if (!Mathf.Approximately(m.MissDelay, 4f)) { continue; }
            Undo.RecordObject(m, "ACO luna miss delay");
            m.MissDelay = 5f;
            EditorUtility.SetDirty(m);
            n++;
        }
        if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] LUNA 격추 뒤 폭격: 모듈 {n}개를 5초 기다림으로 맞췄습니다. 씬을 저장하세요.");
    }

    /// 씬의 지원 관리자: 급유 최고 속도 400kt → 1000kt(넉넉하게 — 예전 기본 400만 고친다).
    private static void TuneGuardianForward()
    {
        int n = 0;
        foreach (ACO_SupportManager m in Object.FindObjectsOfType<ACO_SupportManager>(true))
        {
            if (!Mathf.Approximately(m.GuardianOffset.z, 12f)) { continue; }
            Undo.RecordObject(m, "ACO guardian forward");
            m.GuardianOffset = new Vector3(m.GuardianOffset.x, m.GuardianOffset.y, 25f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(m);
            EditorUtility.SetDirty(m);
            n++;
        }
        if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] 가디언 드론 자리: 지원 관리자 {n}개를 앞 25m로 옮겼습니다. 씬을 저장하세요.");
    }

    private static void TuneTankerMaxSpeed()
    {
        int n = 0;
        foreach (ACO_SupportManager m in Object.FindObjectsOfType<ACO_SupportManager>(true))
        {
            if (!Mathf.Approximately(m.MaxSpeedKt, 400f)) { continue; }
            Undo.RecordObject(m, "ACO tanker max speed");
            m.MaxSpeedKt = 1000f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(m);
            EditorUtility.SetDirty(m);
            n++;
        }
        if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] 급유 최고 속도: 지원 관리자 {n}개를 1000kt로 맞췄습니다. 씬을 저장하세요.");
    }

    private static void TuneLunaDropL01()
    {
        int n = 0;
        foreach (ACO_PhaseLuna m in Object.FindObjectsOfType<ACO_PhaseLuna>(true))
        {
            if (m.LineText == null || m.LineText.Length != 16 || m.LineText[0] != "That beam is a targeting laser! She is painting the city for an orbital strike. Shoot her down before it lands!") { continue; }
            Undo.RecordObject(m, "ACO luna drop L01");
            m.LineEvent = Skip0(m.LineEvent);
            m.LineSpeaker = Skip0(m.LineSpeaker);
            m.LineText = Skip0(m.LineText);
            m.LineClip = Skip0(m.LineClip);
            EditorUtility.SetDirty(m);
            n++;
        }
        if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] LUNA 대사 표: 모듈 {n}개에서 첫 줄(L01)을 뺐습니다. 씬을 저장하세요.");
    }

    private static T[] Skip0<T>(T[] a)
    {
        if (a == null || a.Length == 0) { return a; }
        var r = new T[a.Length - 1];
        System.Array.Copy(a, 1, r, 0, r.Length);
        return r;
    }

    private static void RenameToCallsigns()
    {
        int n = 0;
        foreach (ACO_PhaseBorderless m in Object.FindObjectsOfType<ACO_PhaseBorderless>(true)) { n += RenameIf(m.gameObject, "Phase_LaserAce", "Phase_Borderless"); }
        foreach (ACO_PhaseTwins m in Object.FindObjectsOfType<ACO_PhaseTwins>(true)) { if (!m.BonusOnly) { n += RenameIf(m.gameObject, "Phase_SuperAce", "Phase_Twins"); } }
        foreach (ACO_NPCFighterAI ai in Object.FindObjectsOfType<ACO_NPCFighterAI>(true))
        {
            n += RenameIf(ai.gameObject, "NPC_LaserAce", "NPC_Borderless");
            n += RenameIf(ai.gameObject, "NPC_SuperAce_Tails", "NPC_Tails");
            n += RenameIf(ai.gameObject, "NPC_SuperAce", "NPC_Heads");
        }
        if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] 콜사인 이름: 씬 오브젝트 {n}개 이름을 바꿨습니다. 씬을 저장하세요.");
    }

    private static int RenameIf(GameObject go, string from, string to)
    {
        if (go.name != from) { return 0; }
        Undo.RecordObject(go, "ACO callsign name");
        go.name = to;
        EditorUtility.SetDirty(go);
        return 1;
    }

    private static void TuneCountermeasureUpgrade()
    {
        int n = 0;
        foreach (ACO_PlayerProgress p in Object.FindObjectsOfType<ACO_PlayerProgress>(true))
        {
            int i = ACO_PlayerProgress.UP_COUNTERMEASURE;
            Undo.RecordObject(p, "ACO countermeasure upgrade");
            if (p.UpgradeNames != null && i < p.UpgradeNames.Length) { p.UpgradeNames[i] = "COUNTERMEASURES"; }
            if (p.UpgradeSteps != null && i < p.UpgradeSteps.Length) { p.UpgradeSteps[i] = 0f; }   // 배수가 아니라 한 발씩(CountermeasurePerLevel)
            if (p.UpgradeBaseCosts != null && i < p.UpgradeBaseCosts.Length) { p.UpgradeBaseCosts[i] = 1500; }
            EditorUtility.SetDirty(p);
            n++;
        }
        if (n > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] 대응탄 수 강화: 진행도 {n}개의 4번 자리를 바꿨습니다. Bake하면 상점 아이콘도 바뀝니다. 씬을 저장하세요.");
    }

    private static void TuneRailgunArm()
    {
        foreach (ACO_PhaseRailgun m in Object.FindObjectsOfType<ACO_PhaseRailgun>(true))
        {
            Undo.RecordObject(m, "ACO railgun arm");
            if (Mathf.Approximately(m.CoreOpenBefore, 25f)) { m.CoreOpenBefore = 30f; }
            m.ArmMaxWait = 180f;
            EditorUtility.SetDirty(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
        }
        Debug.Log("[ACO] 랜스 포대: 코어 열림 30초, 첫 충전은 공격할 때(최대 180초 대기)로 맞췄습니다. 씬을 저장하세요.");
    }

    /// crew-walk-1(6-229): 갑판 점검요원 걷는 빠르기 3.2 → 2.2m/s — 걷기 클립 보폭과 맞춰 발이 미끄러지지 않게.
    private static void TuneCrewWalk()
    {
        int n = 0;
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_CatapultCrew c in root.GetComponentsInChildren<ACO_CatapultCrew>(true))
            {
                if (Mathf.Approximately(c.CheckWalkSpeed, 2.2f)) { continue; }
                c.CheckWalkSpeed = 2.2f;
                Mark(c);
                any = true;
                n++;
            }
            return any;
        });
        RevertInScene<ACO_CatapultCrew>("CheckWalkSpeed");
        Debug.Log($"[ACO] 갑판 요원 걷는 빠르기: 프리팹 {n}곳을 2.2m/s로.");
    }

    /// ordnance-debuglog-off-1(6-227): 탄 프리팹(ACO_Ordnance — 폭탄 ACO_Bomb · ACO_Bomb(LGB) 등)의 DebugHitLog를 끈다. 공개 전 — 터질 때마다 로그가 쌓였다.
    private static void TuneOrdnanceDebugLogOff()
    {
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { continue; }
            bool on = false;
            foreach (ACO_Ordnance o in asset.GetComponentsInChildren<ACO_Ordnance>(true)) { if (o.DebugHitLog) { on = true; break; } }
            if (!on) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (ACO_Ordnance o in root.GetComponentsInChildren<ACO_Ordnance>(true)) { o.DebugHitLog = false; EditorUtility.SetDirty(o); }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                n++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Debug.Log($"[ACO] 탄 디버그 로그 끄기: 프리팹 {n}개.");
    }

    /// fortress-dive-core-1(6-226): 공중 요새 3단계 — 돌진 약 2분 30초 → 약 3분(DiveSpeed 60 → 49), 코어 체력 4000 → 2000.
    /// 돌진 시간 = 대기 1.5초 + 가속 20초(엔진이 다 부서져 27m/s에서) + 남은 길 ÷ DiveSpeed — 궤도 7km 기준 길 약 8.5km.
    private static void TuneFortressDiveCore()
    {
        string path = PrefabDir + "/NPC_Fortress.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Debug.LogWarning("[ACO] 요새 돌진 · 코어: " + path + "가 없습니다."); return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (ACO_FlyingFortress f in root.GetComponentsInChildren<ACO_FlyingFortress>(true))
            {
                f.DiveSpeed = FortressDiveSpeed;
                if (f.TargetHealth != null) { f.TargetHealth.Health = FortressCoreHealth; EditorUtility.SetDirty(f.TargetHealth); }
                EditorUtility.SetDirty(f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        foreach (ACO_FlyingFortress f in FindInScene<ACO_FlyingFortress>())
        {
            Revert(f, "DiveSpeed");
            if (f.TargetHealth != null) { Revert(f.TargetHealth, "Health"); }
        }
        Debug.Log($"[ACO] 요새 돌진 · 코어: 돌진 속도 {FortressDiveSpeed}m/s(약 3분), 코어 체력 {FortressCoreHealth}.");
    }

    public const float FortressDiveSpeed = 49f;
    public const float FortressCoreHealth = 2000f;

    /// artillery-shell-collider-1(6-225): 야포 포탄(ACO_ArtilleryShell) 모델의 콜라이더를 지운다. 포탄은 매 프레임 이동 구간을 Linecast로 보는데
    /// 바꿔 끼운 박격포탄 모델(SM_Prop_Mortar_Shell_01)의 MeshCollider가 그 구간 안에 있어 무장 시간(0.5초)이 끝나자마자 자기 몸에 맞아 공중에서 터졌다.
    private static void TuneArtilleryShellCollider()
    {
        string path = PrefabDir + "/ACO_ArtilleryShell.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Debug.LogWarning("[ACO] 야포 포탄 콜라이더 지우기: " + path + "가 없습니다."); return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Collider[] cols = root.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in cols) { UnityEngine.Object.DestroyImmediate(c); }
            if (cols.Length > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            Debug.Log($"[ACO] 야포 포탄 콜라이더 지우기: {cols.Length}개를 지웠습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// hud-defaults-1(6-239): HUD 설정 기본값 = 개발자가 맞춰 쓰던 값(대강). 프리팹(ACO_HUD · ACO_FlightHud)에 넣고, 씬 인스턴스에서 바꾼 같은 칸은 지우고,
    /// 씬 오브젝트인 무전 자막(ACO_Chatter)은 직접 넣는다. 배율 항목(패널 · 결과 · 레이더 크기)은 ACO_HudSettings 코드 기본값.
    private static void TuneHudDefaults()
    {
        string hudPath = PrefabDir + "/ACO_HUD.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(hudPath) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(hudPath);
            try
            {
                foreach (ACO_ScoreHUD h in root.GetComponentsInChildren<ACO_ScoreHUD>(true)) { SetHudDefaults(h); }
                foreach (ACO_RWR r in root.GetComponentsInChildren<ACO_RWR>(true)) { SetHudDefaults(r); }
                foreach (ACO_HudRadar r in root.GetComponentsInChildren<ACO_HudRadar>(true)) { SetHudDefaults(r); }
                PrefabUtility.SaveAsPrefabAsset(root, hudPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        else { Debug.LogWarning("[ACO] HUD 기본값: " + hudPath + "가 없습니다."); }
        string flightPath = PrefabDir + "/ACO_FlightHud.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(flightPath) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(flightPath);
            try
            {
                foreach (ACO_FlightHud f in root.GetComponentsInChildren<ACO_FlightHud>(true)) { f.Scale = 1.4f; }
                PrefabUtility.SaveAsPrefabAsset(root, flightPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        // 씬: 프리팹 인스턴스는 같은 칸의 오버라이드를 지우고, 프리팹이 아니면 값을 직접 넣는다
        foreach (ACO_ScoreHUD h in FindInScene<ACO_ScoreHUD>()) { if (PrefabUtility.IsPartOfPrefabInstance(h)) { Revert(h, "HeadOffset", "ResultOffset", "FollowSharpness"); } else { Undo.RecordObject(h, "HUD defaults"); SetHudDefaults(h); } }
        foreach (ACO_RWR r in FindInScene<ACO_RWR>()) { if (PrefabUtility.IsPartOfPrefabInstance(r)) { Revert(r, "ChevronScale", "LockArcScale"); } else { Undo.RecordObject(r, "HUD defaults"); SetHudDefaults(r); } }
        foreach (ACO_HudRadar r in FindInScene<ACO_HudRadar>()) { if (PrefabUtility.IsPartOfPrefabInstance(r)) { Revert(r, "HeadOffset", "Range", "BlipScale", "FollowSharpness"); } else { Undo.RecordObject(r, "HUD defaults"); SetHudDefaults(r); } }
        foreach (ACO_FlightHud f in FindInScene<ACO_FlightHud>()) { if (PrefabUtility.IsPartOfPrefabInstance(f)) { Revert(f, "Scale"); } else { Undo.RecordObject(f, "HUD defaults"); f.Scale = 1.4f; } }
        foreach (ACO_Chatter c in FindInScene<ACO_Chatter>())
        {
            if (PrefabUtility.IsPartOfPrefabInstance(c)) { Revert(c, "HeadOffset", "FollowSharpness"); continue; }
            Undo.RecordObject(c, "HUD defaults");
            c.HeadOffset = new Vector3(0f, 0.2f, 0.9f);
            c.FollowSharpness = 1000f;
            EditorUtility.SetDirty(c);
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[ACO] HUD 기본값(6-239)을 넣었습니다 — 패널 좌 -0.38, 결과 아래 -0.19, 자막 위 0.2, 레이더 (-0.28, -0.16) 5km · 점 1.6, RWR 미사일 0.7 · 락온 호 1.2, 비행 HUD 1.4, 따라오는 지연 없음. 씬 저장");
    }

    private static void SetHudDefaults(ACO_ScoreHUD h)
    {
        h.HeadOffset = new Vector3(-0.38f, 0.16f, h.HeadOffset.z);
        h.ResultOffset = new Vector2(0f, -0.19f);
        h.FollowSharpness = 1000f;
        EditorUtility.SetDirty(h);
    }

    private static void SetHudDefaults(ACO_RWR r)
    {
        r.ChevronScale = 0.7f;
        r.LockArcScale = 1.2f;
        EditorUtility.SetDirty(r);
    }

    private static void SetHudDefaults(ACO_HudRadar r)
    {
        r.HeadOffset = new Vector3(-0.28f, -0.16f, r.HeadOffset.z);
        r.Range = Mathf.Min(5000f, r.MaxRange);
        r.BlipScale = 1.6f;
        r.FollowSharpness = 1000f;
        EditorUtility.SetDirty(r);
    }

    /// hud-step-1cm-1(6-244): HUD 설정 Steps 중 0.02(위치 2cm · 따라오는 지연 0.02초)를 0.01로. 다른 칸은 그대로.
    private static void TuneHudStep1cm()
    {
        int[] items = { ACO_HudSettings.ITEM_PANEL_X, ACO_HudSettings.ITEM_PANEL_Y, ACO_HudSettings.ITEM_RESULT_X, ACO_HudSettings.ITEM_RESULT_Y,
            ACO_HudSettings.ITEM_CHATTER_X, ACO_HudSettings.ITEM_CHATTER_Y, ACO_HudSettings.ITEM_RADAR_X, ACO_HudSettings.ITEM_RADAR_Y, ACO_HudSettings.ITEM_HUD_FOLLOW };
        string path = PrefabDir + "/ACO_HudSettings.prefab";
        int n = 0;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (ACO_HudSettings h in root.GetComponentsInChildren<ACO_HudSettings>(true))
                {
                    if (h.Steps == null) { continue; }
                    foreach (int i in items)
                    {
                        if (i < h.Steps.Length && Mathf.Approximately(h.Steps[i], 0.02f)) { h.Steps[i] = 0.01f; n++; }
                    }
                    EditorUtility.SetDirty(h);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        else { Debug.LogWarning("[ACO] HUD 설정 한 칸: " + path + "가 없습니다."); }
        // 씬: 프리팹 인스턴스는 Steps 오버라이드를 지우고, 프리팹이 아니면 직접
        foreach (ACO_HudSettings h in FindInScene<ACO_HudSettings>())
        {
            if (PrefabUtility.IsPartOfPrefabInstance(h)) { Revert(h, "Steps"); continue; }
            if (h.Steps == null) { continue; }
            Undo.RecordObject(h, "HUD step");
            foreach (int i in items) { if (i < h.Steps.Length && Mathf.Approximately(h.Steps[i], 0.02f)) { h.Steps[i] = 0.01f; n++; } }
            EditorUtility.SetDirty(h);
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] HUD 설정 한 칸 2 → 1(6-244): {n}칸을 바꿨습니다 — 위치 1cm, 따라오는 지연 0.01초. 씬 저장");
    }

    /// fighter-start-health-1(6-241): NPC 전투기 프리팹 뿌리의 SaccTarget.Health(시작 체력 — 동기화 값의 처음)를 BaseHealth(최대 체력)와 같게.
    /// 최대 체력은 각 화면에서 BaseHealth로 바뀌고 남은 체력은 소유자가 채워 보내는데, 그걸 놓친 화면은 시작 체력 그대로라 비율이 깎여 보였다.
    private static void TuneFighterStartHealth()
    {
        int n = 0;
        EditPrefabs(root =>
        {
            ACO_NPCFighterAI ai = root.GetComponent<ACO_NPCFighterAI>();
            SaccTarget t = root.GetComponent<SaccTarget>();
            if (ai == null || t == null || ai.BaseHealth <= 0f || Mathf.Approximately(t.Health, ai.BaseHealth)) { return false; }
            Debug.Log($"[ACO] 시작 체력: {root.name} {t.Health} → {ai.BaseHealth}");
            t.Health = ai.BaseHealth;
            EditorUtility.SetDirty(t);
            n++;
            return true;
        });
        int s = 0;
        foreach (ACO_NPCFighterAI ai in FindInScene<ACO_NPCFighterAI>())
        {
            SaccTarget t = ai.GetComponent<SaccTarget>();
            if (t == null || ai.BaseHealth <= 0f || Mathf.Approximately(t.Health, ai.BaseHealth)) { continue; }
            Revert(t, "Health");
            if (!Mathf.Approximately(t.Health, ai.BaseHealth))
            {
                Undo.RecordObject(t, "ACO start health");
                t.Health = ai.BaseHealth;
                Mark(t);
            }
            s++;
        }
        if (s > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] 시작 체력 = 최대 체력: 프리팹 {n}개, 씬 {s}기를 맞췄습니다.");
    }

    /// 미리 보기 격추(SaccTarget.PredictExplosion)가 켜진 NPC · 표적 수(프리팹 + 씬, 6-248).
    /// 쏜 사람 화면이 소유자 확인 전에 먼저 터뜨리는 기능 — 틀리면 그 화면에서만 투명 · 무적이 되어 모두 끈다(파괴는 소유자 기준).
    /// 빌더가 SaccTarget을 새로 붙이면 SAV 기본값(켜짐)으로 나오므로 Setup Check가 매번 센다.
    public static int CountPredictOn()
    {
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("NPC_ t:Prefab", new[] { PrefabDir }))
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null) { continue; }
            foreach (SaccTarget t in asset.GetComponentsInChildren<SaccTarget>(true)) { if (t.PredictExplosion) { n++; } }
        }
        foreach (SaccTarget t in FindInScene<SaccTarget>()) { if (t.PredictExplosion) { n++; } }
        return n;
    }

    /// 프리팹(포탑 먼저)과 씬의 SaccTarget 미리 보기 격추를 끈다. 씬 인스턴스는 오버라이드를 지우고, 그래도 켜져 있으면 직접 끈다.
    public static void DisablePrediction()
    {
        int n = 0;
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (SaccTarget t in root.GetComponentsInChildren<SaccTarget>(true))
            {
                if (!t.PredictExplosion) { continue; }
                t.PredictExplosion = false;
                EditorUtility.SetDirty(t);
                any = true;
                n++;
            }
            return any;
        });
        int s = 0;
        foreach (SaccTarget t in FindInScene<SaccTarget>())
        {
            if (!t.PredictExplosion) { continue; }
            if (PrefabUtility.IsPartOfPrefabInstance(t)) { Revert(t, "PredictExplosion"); }
            if (t.PredictExplosion)
            {
                Undo.RecordObject(t, "ACO predict off");
                t.PredictExplosion = false;
                Mark(t);
            }
            s++;
        }
        if (s > 0) { UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); }
        Debug.Log($"[ACO] 미리 보기 격추 끔(6-248): 프리팹 {n}곳, 씬 {s}곳. 씬 저장");
    }

    /// sonar-ring-ui-1(6-238): 잠수함 소나 원(SonarRing)을 UI 레이어로 — HUD 표시라 관전 카메라 · VRChat 카메라에 찍히지 않게.
    private static void TuneSonarRingLayer()
    {
        int n = 0;
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_Submarine sub in root.GetComponentsInChildren<ACO_Submarine>(true))
            {
                if (sub.SonarRing == null) { continue; }
                foreach (Transform t in sub.SonarRing.GetComponentsInChildren<Transform>(true))
                {
                    if (t.gameObject.layer == ACO_PrefabBuilder.SonarRingLayer) { continue; }
                    t.gameObject.layer = ACO_PrefabBuilder.SonarRingLayer;
                    any = true;
                    n++;
                }
            }
            return any;
        });
        Debug.Log($"[ACO] 소나 원 UI 레이어: {n}개를 옮겼습니다.");
    }

    /// vls-missile-hot-1(6-215): NPC 프리팹 런처(ACO_MissileLauncher.AAM)가 ACO_Missile_VLS를 가리키면 ACO_Missile_VLS_HOT으로.
    private static void TuneVlsMissileHot()
    {
        GameObject oldVls = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/ACO_Missile_VLS.prefab");
        GameObject hot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + ACO_PrefabBuilder.VlsMissileDefault + ".prefab");
        if (oldVls == null || hot == null) { Debug.LogWarning("[ACO] VLS 미사일 바꾸기: ACO_Missile_VLS 또는 " + ACO_PrefabBuilder.VlsMissileDefault + " 프리팹이 없습니다."); return; }
        int n = 0;
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_MissileLauncher l in root.GetComponentsInChildren<ACO_MissileLauncher>(true))
            {
                if (l.AAM != oldVls) { continue; }
                l.AAM = hot;
                Mark(l);
                any = true;
                n++;
            }
            return any;
        });
        Debug.Log($"[ACO] VLS 미사일 바꾸기: 런처 {n}개를 {ACO_PrefabBuilder.VlsMissileDefault}로 바꿨습니다.");
    }

    /// 해상 유닛(함선·항공모함·구축함·육상전함) 부위의 범위 피해 배율을 0.2로, LASM 무장은 부위에도 그대로 주게 한다.
    /// 공중 요새·랜스 포대는 1 그대로. 무장은 이름이 Module_LASM인 ACO_DFUNC_Missile(기체 프리팹·씬)이다.
    private static void TuneSeaPartSplash()
    {
        EditPrefabs(root =>
        {
            ACO_Warship ship = root.GetComponent<ACO_Warship>();
            if (ship == null || root.name == "NPC_Fortress" || root.name == "NPC_Railgun") { return false; }
            if (Mathf.Approximately(ship.PartSplashScale, ACO_PrefabBuilder.SeaPartSplash)) { return false; }
            ship.PartSplashScale = ACO_PrefabBuilder.SeaPartSplash;
            Mark(ship);
            return true;
        });
        int lasm = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("SaccFlightAndVehicles") || !File.ReadAllText(path).Contains("m_Name: Module_LASM")) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool any = false;
                foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
                {
                    if (m.gameObject.name != "Module_LASM" || m.FullPartSplash) { continue; }
                    m.FullPartSplash = true;
                    Mark(m);
                    any = true;
                    lasm++;
                }
                if (any) { PrefabUtility.SaveAsPrefabAsset(root, path); Debug.Log($"[ACO] 프리팹 수치: {Path.GetFileName(path)}의 LASM이 함선 부위에도 범위 피해를 그대로 줍니다."); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (ACO_DFUNC_Missile m in FindInScene<ACO_DFUNC_Missile>())
        {
            if (m.gameObject.name != "Module_LASM" || m.FullPartSplash) { continue; }
            Undo.RecordObject(m, "ACO LASM part splash");
            m.FullPartSplash = true;
            Mark(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
            lasm++;
        }
        Debug.Log($"[ACO] 해상 유닛 부위 범위 피해 0.2배, LASM 예외 {lasm}곳. 씬이 바뀌었으면 저장하세요.");
    }

    /// 공중 요새 추락 충돌 층에서 Environment를 빼고(선체에 맞아 바로 터지던 것), 랜스 포대 대폭발을 코어 파괴 14초 뒤로.
    private static void TuneBossDeath()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            ACO_FlyingFortress fortress = root.GetComponent<ACO_FlyingFortress>();
            if (fortress != null && fortress.ImpactLayers.value != 17) { fortress.ImpactLayers = 17; Mark(fortress); any = true; }
            ACO_Railgun railgun = root.GetComponent<ACO_Railgun>();
            if (railgun != null && !Mathf.Approximately(railgun.DeathDelay, 14f)) { railgun.DeathDelay = 14f; Mark(railgun); any = true; }
            return any;
        });
    }

    /// 소형 레일건(랜스 포대·육상전함 주포)을 깔아 두는 사격으로: 3.5초 뒤 자리를 노린다(고정하자마자 쏘는 것은 코드).
    /// 폭발 지역 유지 시간: 육상전함 주포 5초(8초에서 줄임), 랜스 포대는 표적이 닿을 때까지 남게 4.5초 이상.
    private static void TuneLandShipMainGunRadius()
    {
        EditPrefabs(root =>
        {
            bool landShip = root.GetComponent<ACO_LandShip>() != null;
            float want = landShip ? ACO_PrefabBuilder.LsMainGunZoneRadius : 255f;
            bool any = false;
            foreach (ACO_MiniRailgun m in root.GetComponentsInChildren<ACO_MiniRailgun>(true))
            {
                if (Mathf.Approximately(m.ZoneRadius, want)) { continue; }
                m.ZoneRadius = want;
                Mark(m);
                any = true;
            }
            return any;
        });
    }

    /// 플레이어 기체 피격음(SAV_SoundController.BulletHit): 첫 소리 자리를 본떠 4개로(Audio/SFX/ACO_BulletHit_0~3) — SAV가 그중 하나를 무작위로 튼다.
    /// 기체 수정은 프리팹에(씬 기체가 프리팹 인스턴스면 그 프리팹, 아니면 씬). 씬 인스턴스가 BulletHit 배열을 덮어쓰고 있으면 알린다.
    private static void TuneBulletHitSound()
    {
        var clips = new List<AudioClip>();
        for (int i = 0; i < 4; i++)
        {
            AudioClip c = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/SFX/ACO_BulletHit_{i}.mp3");
            if (c != null) { clips.Add(c); }
        }
        if (clips.Count == 0) { Debug.LogWarning("[ACO] 피격음: Audio/SFX/ACO_BulletHit_*.mp3가 없습니다."); return; }
        var done = new HashSet<string>();
        foreach (SAV_SoundController sc in Object.FindObjectsOfType<SAV_SoundController>(true))
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sc.gameObject);
            if (string.IsNullOrEmpty(path))
            {
                if (SetBulletHits(sc, clips)) { Undo.RecordObject(sc, "ACO hit sound"); EditorUtility.SetDirty(sc); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sc.gameObject.scene); }
                continue;
            }
            if (done.Contains(path)) { continue; }
            done.Add(path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (SAV_SoundController p in root.GetComponentsInChildren<SAV_SoundController>(true)) { changed |= SetBulletHits(p, clips); }
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[ACO] 피격음: {path}의 BulletHit을 {clips.Count}개로 바꿨습니다.");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        // 씬 인스턴스가 배열을 덮어쓰면 프리팹 값이 가려진다
        foreach (SAV_SoundController sc in Object.FindObjectsOfType<SAV_SoundController>(true))
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(sc)) { continue; }
            foreach (PropertyModification m in PrefabUtility.GetPropertyModifications(sc) ?? new PropertyModification[0])
            {
                if (m.target is SAV_SoundController && m.propertyPath.StartsWith("BulletHit"))
                {
                    Debug.LogWarning($"[ACO] 피격음: 씬의 '{sc.transform.root.name}/{sc.name}'이 BulletHit 배열을 덮어써 프리팹 값이 가려집니다 — 인스펙터에서 BulletHit을 Revert 하세요.", sc);
                    break;
                }
            }
        }
    }

    /// BulletHit을 clips 수만큼(첫 소리 오브젝트를 본떠 복제), 소리마다 클립 하나. 바꿨으면 true.
    private static bool SetBulletHits(SAV_SoundController sc, List<AudioClip> clips)
    {
        AudioSource template = sc.BulletHit != null && sc.BulletHit.Length > 0 ? sc.BulletHit[0] : null;
        if (template == null) { return false; }
        bool same = sc.BulletHit.Length == clips.Count;
        for (int i = 0; same && i < clips.Count; i++) { same = sc.BulletHit[i] != null && sc.BulletHit[i].clip == clips[i]; }
        if (same) { return false; }
        var list = new AudioSource[clips.Count];
        for (int i = 0; i < clips.Count; i++)
        {
            AudioSource src = i < sc.BulletHit.Length && sc.BulletHit[i] != null ? sc.BulletHit[i] : null;
            if (src == null || (i > 0 && src == template))
            {
                GameObject copy = Object.Instantiate(template.gameObject, template.transform.parent);
                copy.name = template.gameObject.name + "_" + i;
                src = copy.GetComponent<AudioSource>();
            }
            src.clip = clips[i];
            src.playOnAwake = false;
            list[i] = src;
        }
        sc.BulletHit = list;
        EditorUtility.SetDirty(sc);
        return true;
    }

    /// 보너스 작전 끄기(6-161): 씬의 미션 관리자마다 페이즈 수별 보너스 확률을 0으로(배열 길이는 그대로 — 비우면 BonusPhaseChance로 넘어간다).
    private static void TuneBonusOff()
    {
        foreach (ACO_MissionManager m in FindInScene<ACO_MissionManager>())
        {
            if (m.BonusChanceByPhases == null || m.BonusChanceByPhases.Length == 0) { m.BonusChanceByPhases = new float[5]; }
            for (int i = 0; i < m.BonusChanceByPhases.Length; i++) { m.BonusChanceByPhases[i] = 0f; }
            Mark(m);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
            Debug.Log($"[ACO] 보너스 작전: '{m.name}'의 페이즈 수별 보너스 확률을 0으로 했습니다.", m);
        }
    }

    /// 캐노피 키 끊기(6-160): 씬의 플레이어 기체 프리팹마다 SAV 키보드 연결에서 DFUNC_Canopy 칸을 비운다(기능은 자동화가 쓰므로 그대로 둔다).
    /// 씬 인스턴스에 덮어쓴 값이 남아 있으면 그것도 비운다.
    private static void TuneCanopyKeyOff()
    {
        var done = new HashSet<string>();
        foreach (SAV_KeyboardControls kb in Object.FindObjectsOfType<SAV_KeyboardControls>(true))
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(kb.gameObject);
            if (!string.IsNullOrEmpty(path) && done.Add(path))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (SAV_KeyboardControls p in root.GetComponentsInChildren<SAV_KeyboardControls>(true)) { changed |= ClearCanopyKey(p); }
                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        Debug.Log($"[ACO] 캐노피 키: {path}의 데스크톱 캐노피 키를 끊었습니다.");
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
        foreach (SAV_KeyboardControls kb in Object.FindObjectsOfType<SAV_KeyboardControls>(true))
        {
            if (ClearCanopyKey(kb)) { Mark(kb); }
        }
    }

    private static bool ClearCanopyKey(SAV_KeyboardControls kb)
    {
        var so = new SerializedObject(kb);
        bool changed = false;
        foreach (string side in new[] { "L", "R" })
        {
            for (int i = 1; i <= 10; i++)
            {
                SerializedProperty f = so.FindProperty($"{side}func{i}");
                if (f == null || !(f.objectReferenceValue is DFUNC_Canopy)) { continue; }
                f.objectReferenceValue = null;
                SerializedProperty k = so.FindProperty($"{side}func{i}key");
                if (k != null) { k.intValue = (int)KeyCode.None; }
                changed = true;
            }
        }
        if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); }
        return changed;
    }

    /// AI 상한 고도(6-157): NPC 프리팹의 비행 컨트롤러 전부 MaxAltitude 12192 · CeilingSettleTime 4.
    private static void TuneAiCeiling()
    {
        EditPrefabs(root =>
        {
            bool changed = false;
            foreach (ACO_NPCFlightController f in root.GetComponentsInChildren<ACO_NPCFlightController>(true))
            {
                if (Mathf.Approximately(f.MaxAltitude, 12192f) && Mathf.Approximately(f.CeilingSettleTime, 4f)) { continue; }
                f.MaxAltitude = 12192f;
                f.CeilingSettleTime = 4f;
                EditorUtility.SetDirty(f);
                changed = true;
            }
            return changed;
        });
    }

    /// rail-hit-sound-1: 레일건 탄 프리팹에 명중 SAV 이벤트(MissileHit 소리)를 넣는다.
    private static void TuneRailHitSound()
    {
        string path = ACO_PrefabBuilder.PathOf(ACO_PrefabBuilder.LunaRailPrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_Missile m = root.GetComponent<ACO_Missile>();
            if (m == null || m.HitExtensionEvent == ACO_PrefabBuilder.RailHitEvent) { return; }
            m.HitExtensionEvent = ACO_PrefabBuilder.RailHitEvent;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] 레일건 탄: 플레이어 기체에 맞으면 SAV MissileHit 소리를 냅니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 레일건 스침 소리(6-156): 씬 BulletFlyby.RailClip + NPC_Luna 레일건 런처 RailFlyby. 레일건 ↔ Flyby 연결은 Bake.
    private static void TuneRailFlyby()
    {
        AudioClip clip = ACO_SceneBaker.RailFlybyClip();
        if (clip == null) { Debug.LogWarning("[ACO] 레일건 스침 소리: Audio/Generated/ACO_Rail_Flyby.wav가 없습니다."); return; }
        foreach (ACO_BulletFlyby flyby in Object.FindObjectsOfType<ACO_BulletFlyby>(true))
        {
            if (flyby.RailClip == clip) { continue; }
            Undo.RecordObject(flyby, "ACO rail flyby");
            flyby.RailClip = clip;
            EditorUtility.SetDirty(flyby);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(flyby.gameObject.scene);
        }
        ACO_PrefabBuilder.ApplyLunaRailBurst();
        Debug.Log("[ACO] 레일건 스침 소리를 넣었습니다 — Bake로 레일건과 잇습니다.");
    }

    private static void TuneMiniRailgunReburst()
    {
        EditPrefabs(root =>
        {
            if (root.GetComponent<ACO_LandShip>() != null) { return false; }
            bool any = false;
            foreach (ACO_MiniRailgun m in root.GetComponentsInChildren<ACO_MiniRailgun>(true))
            {
                if (m.ZoneReburst > 0f) { continue; }
                m.ZoneReburst = 1.2f;
                Mark(m);
                any = true;
            }
            return any;
        });
    }

    private static void TuneMiniRailgunLay()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            bool landShip = root.GetComponent<ACO_LandShip>() != null;
            foreach (ACO_MiniRailgun m in root.GetComponentsInChildren<ACO_MiniRailgun>(true))
            {
                bool dirty = false;
                if (!Mathf.Approximately(m.LeadTime, 3.5f)) { m.LeadTime = 3.5f; dirty = true; }
                if (landShip && !Mathf.Approximately(m.ZoneTime, ACO_PrefabBuilder.LsMainGunZoneTime)) { m.ZoneTime = ACO_PrefabBuilder.LsMainGunZoneTime; dirty = true; }
                else if (!landShip && m.ZoneTime < 4.5f) { m.ZoneTime = 4.5f; dirty = true; }
                if (dirty) { Mark(m); any = true; }
            }
            return any;
        });
    }

    /// 보스 코어(본체)를 범위 피해 면역으로 — 코어를 직접 맞혀야만 깎인다. 처음엔 육상전함만(landship-core-splash-1), 이제 공중 요새·랜스 포대도(boss-core-splash-1).
    private static void TuneLandShipCoreSplash()
    {
        EditPrefabs(root =>
        {
            ACO_Warship ship = root.GetComponent<ACO_Warship>();
            bool boss = root.GetComponent<ACO_LandShip>() != null || root.GetComponent<ACO_FlyingFortress>() != null || root.GetComponent<ACO_Railgun>() != null;
            if (ship == null || !boss || ship.CoreSplashImmune) { return false; }
            ship.CoreSplashImmune = true;
            Mark(ship);
            return true;
        });
    }

    public static readonly Entry[] Entries =
    {
        new Entry { Id = "ciws-1", Text = "NPC 프리팹에 CIWS 요격 기본값과 탄 판정 반경을 넣습니다", Apply = TuneCiws },
        new Entry { Id = "escort-priority-1", Text = "NPC 프리팹에 호위 유닛 공격 우선순위를 넣습니다", Apply = TuneEscortPriority },
        new Entry { Id = "fleet-1", Text = "적 함선 프리팹의 VLS·ASM·함포가 해수면의 아군 함선을 노리게 합니다(순양함 VLS 사거리 5km)", Apply = TuneFleet },
        new Entry { Id = "railgun-mini-1", Text = "랜스 포대 소형 레일건 4문을 NPC_Turret_RailgunMini 프리팹으로 바꿉니다", Apply = ACO_PrefabBuilder.ConvertRailgunMinis },
        new Entry { Id = "railgun-model-1", Text = "랜스 포대와 소형 레일건을 새 모델로 바꿉니다. 옮긴 부위의 충돌도 맞춥니다. 씬 참조는 유지됩니다", Apply = ACO_PrefabBuilder.InstallLanceModel },
        new Entry { Id = "turret-model-1", Text = "Flak, CIWS, VLS 포대를 새 모델로 바꿉니다. 공중 요새의 포대도 함께 바뀝니다. 팩 모델 자식은 프리팹에서 지웁니다", Apply = ACO_PrefabBuilder.InstallTurretModels },
        new Entry { Id = "fortress-turrets-3", Text = "공중 요새 포대를 Flak 4, CIWS 8, VLS 6으로 바꾸고 선체 경사에 맞춰 좌우 대칭으로 놓습니다. 배 쪽 VLS 2문과 후면 해치 CIWS 2문이 늘고 Flak 4문이 빠집니다", Apply = ACO_PrefabBuilder.PlaceFortressTurrets },
        new Entry { Id = "fortress-core-1", Text = "공중 요새 코어를 세운 원자로로 바꾸고, 코어 히트박스와 증원 발진 지점을 실제 자리에 맞춥니다", Apply = ACO_PrefabBuilder.PlaceFortressCore },
        new Entry { Id = "scm-model-1", Text = "SCM 탄체를 날개를 편 새 모델로 바꿉니다. 예전 모델은 프리팹에 꺼 둡니다", Apply = ACO_PrefabBuilder.InstallScmModel },
        new Entry { Id = "fx-tracer-blast-1", Text = "유닛 탄환(적 노랑, 아군 빨강)과 대폭발을 새 텍스처로 바꿉니다. 대폭발은 이 항목을 고쳐야 나옵니다", Apply = ACO_FxBuilder.ApplyToPrefabs },
        new Entry { Id = "fx-tracer-blast-2", Text = "대폭발을 1.5배로 키우고 탄환 색을 바꿉니다(적 노랑, 아군 빨강). 미리보기도 다시 만듭니다", Apply = ACO_FxBuilder.ApplyToPrefabs },
        new Entry { Id = "fx-missile-1", Text = "미사일·폭탄·로켓·포탄 폭발을 새 텍스처로 바꿉니다(공중·땅·물). 예전 파티클은 꺼 둡니다", Apply = ACO_FxBuilder.ApplyToWeapons },
        new Entry { Id = "fx-railgun-1", Text = "레일건 탄, 공중 폭발, 진행 방향 고리를 넣습니다(주포·소형 레일건)", Apply = ACO_FxBuilder.ApplyToRailgun },
        new Entry { Id = "fx-railgun-2", Text = "레일건 고리를 서브 이미터로 바꿉니다. 소형은 탄이, 주포는 보이지 않는 파티클이 궤적에 고리를 남깁니다", Apply = ACO_FxBuilder.ApplyToRailgun },
        new Entry { Id = "fx-railgun-3", Text = "주포 착탄을 50km 수평선, 발사 2초 뒤로 옮기고(크기도 키움) 포구에 거대한 고리를 넣습니다", Apply = ACO_FxBuilder.ApplyToRailgun },
        new Entry { Id = "fx-trail-1", Text = "미사일·로켓·포탄 연기 궤적을 부드러운 곡선과 연기 무늬로 바꿉니다", Apply = ACO_FxBuilder.ApplyToTrails },
        new Entry { Id = "fx-trail-2", Text = "연기 궤적을 막 나온 곳은 좁고 흐리게, 뒤로 갈수록 넓고 선명하게 바꿉니다. 띠가 접히지 않게 점의 무작위 움직임을 없애고 무늬를 부드럽게 합니다", Apply = ACO_FxBuilder.ApplyToTrails },
        new Entry { Id = "fx-trail-3", Text = "연기 궤적을 회색으로 바꾸고 흔들림을 늘립니다. 붉은 구간을 줄이고 끝을 천천히 흐리게 합니다. 로켓·포탄 연기가 터진 뒤 한순간에 사라지지 않게 수명을 맞춥니다", Apply = ACO_FxBuilder.ApplyToTrails },
        new Entry { Id = "fx-trail-4", Text = "ACO_Missile_SRAAM에서 손으로 맞춘 연기 궤적 값을 모든 미사일·로켓·포탄에 넣습니다(무기별 수명과 폭은 그대로입니다)", Apply = ACO_FxBuilder.ApplyToTrails },
        new Entry { Id = "fx-trail-5", Text = "연기 궤적이 탄 뒤로 나가게 돌리고 방출 반경을 SRAAM 값으로 맞춥니다. 대형 미사일 궤적을 조금 키웁니다. 로켓과 포탄의 값, 손으로 돌린 프리팹은 그대로입니다", Apply = ACO_FxBuilder.ApplyTrailAim },
        new Entry { Id = "fx-wreck-1", Text = "전투기·공격기·에이스와 비행 중인 탄도탄이 터지면 진행 방향으로 불붙은 파편이 날아갑니다. 탄도탄은 공중 폭발을 따로 씁니다", Apply = ACO_FxBuilder.ApplyToWreck },
        new Entry { Id = "tel-model-1", Text = "TEL을 8×8 발사 차량과 새 탄도탄 모델로 바꿉니다. 이렉터가 90도까지 서고 유압 실린더가 함께 움직입니다. 예전 팩 모델은 꺼 둡니다", Apply = ACO_PrefabBuilder.InstallTelModel },
        new Entry { Id = "fx-wake-1", Text = "함선 항적을 거품 텍스처로 바꾸고 파티클 수를 줄입니다(선미 거품, 선수 V자 물결)", Apply = ACO_FxBuilder.ApplyToWakes },
        new Entry { Id = "fx-death-1", Text = "유닛 파괴 효과를 프리팹 5종(공중, 지상, 함선, 건물, 부위)으로 만들어 모든 NPC가 파괴 때 켜는 오브젝트(ShowOnDeath)에 넣습니다. 예전 파티클과 소리는 지웁니다(잔해 모델은 남습니다)", Apply = ACO_FxBuilder.ApplyToDeath },
        new Entry { Id = "deco-layer-1", Text = "건물, 크레인, 탑 콜라이더를 Environment로 옮깁니다. 땅을 재는 유닛이 구조물 위로 올라가지 않습니다. 충돌과 무기 판정은 그대로이고 활주로는 땅으로 남습니다", Apply = ACO_DecoBuilder.ApplyColliderLayers },
        new Entry { Id = "landship-turrets-1", Text = "육상전함 갑판 방공을 놓습니다: 16셀 긴 VLS 4(넓은 갑판 양옆), 네 셀 VLS 1, CIWS 3, Flak 4. 프리팹을 다시 만들지 않고 이 포대들만 바꿉니다(주포·부포·손으로 고친 부분은 그대로)", Apply = ACO_PrefabBuilder.PlaceLandShipTurrets },
        new Entry { Id = "landship-route-1", Text = "육상전함 선체 최대 기울기를 8→15도(육각 지형 비탈과 같게)로 넓힙니다(항로 확인은 콘보이와 같은 경사 검사로 바뀌었습니다)", Apply = ACO_PrefabBuilder.TuneLandShipRoute },
        new Entry { Id = "sea-part-splash-1", Text = "해상 유닛(함선·항공모함·구축함·육상전함)의 부위가 범위 피해를 0.2배만 받습니다(탄이 직접 맞은 부위는 그대로). LASM은 예외로 부위에도 그대로 줍니다", Apply = TuneSeaPartSplash },
        new Entry { Id = "landship-lines-1", Text = "씬의 육상전함 페이즈: 이름 LAND DREADNOUGHT, 배너·성공·실패 글자와 상황별 대사를 새로 넣습니다(궤도는 절반 파괴 때 한 번, 방공 수리 대사 추가). 손으로 고친 대사는 덮어씁니다", Apply = ACO_PhaseModuleSetup.TuneLandShipTexts },
        new Entry { Id = "boss-death-1", Text = "공중 요새: 추락 충돌 층에서 Environment를 빼 공중에서 바로 터지지 않고 땅·바다에 닿을 때 터집니다. 랜스 포대: 코어 파괴 14초 뒤 대폭발합니다(그동안 연쇄 폭발)", Apply = TuneBossDeath },
        new Entry { Id = "landship-endtracks-1", Text = "육상전함 앞뒤 끝 궤도 4개: 부서져도 사라지지 않고 남아 연기를 냅니다(배기구가 있는 가운데 궤도는 그대로 사라집니다)", Apply = ACO_PrefabBuilder.PlaceLandShipEndTracks },
        new Entry { Id = "landship-movefx-1", Text = "육상전함: 주포 최소 폭발 거리 1km→2km(폭발 반경은 그대로), 앞뒤 끝 궤도 흙먼지(뭍에서 움직일 때), 물 위 항적(기존 함선 항적)을 넣습니다", Apply = ACO_PrefabBuilder.PlaceLandShipMoveFx },
        new Entry { Id = "landship-movefx-2", Text = "육상전함 궤도 흙먼지를 지상 폭발 흙먼지와 같은 연기 텍스처(플립북)로, 항적을 함선 항적 거품으로 다시 입힙니다", Apply = ACO_PrefabBuilder.PlaceLandShipMoveFx },
        new Entry { Id = "bonus-off-1", Text = "보너스 작전을 끕니다(6-161 — 일단 폐지): 씬 미션 관리자의 페이즈 수별 보너스 확률(BonusChanceByPhases)을 모두 0으로. 보너스 모듈과 코드는 남겨 두어 확률만 되돌리면 다시 켜집니다. 디버그 패널 BONUS 버튼(테스트용 고정 확률)은 그대로", Apply = TuneBonusOff },
        new Entry { Id = "canopy-key-off-1", Text = "플레이어 기체 데스크톱 캐노피 키(Z)를 끊습니다 — 캐노피는 기체 자동화가 여닫습니다(움직이면 닫고 지상에서 멈추면 열기). 기체 프리팹(SAV_KeyboardControls)을 고칩니다", Apply = TuneCanopyKeyOff },
        new Entry { Id = "ai-ceiling-1", Text = "NPC 전투기·에이스 상한 고도 40,000ft(12192m — SAV 공기가 옅어지기 시작하는 고도): 그 근처에서 둥글게 수평이 되고 넘으면 내려옵니다. 방위·추격은 그대로(비행 컨트롤러 MaxAltitude)", Apply = TuneAiCeiling },
        new Entry { Id = "npc-engine-sound-1", Text = "NPC 항공기에 엔진 소리(SAV Thrust 반복 — 음높이·볼륨 고정, 도플러 약하게, 격추되면 꺼짐): 전투기·에이스 2.5km, 폭격기·수송기·지원 폭격기 3.5km, 공격헬기는 프로펠러 소리, 급유기·가디언 드론은 작게 1.2km. ICBM·TEL 탄도탄엔 로켓 소리(날 때만, 10km)", Apply = ACO_EngineSounds.ApplyAll },
        new Entry { Id = "rail-flyby-1", Text = "레일건 탄이 내 곁(맞는 거리 밖 80m 안)을 스치면 가장 가까울 때 스침 소리를 냅니다: 소형 레일건·육상전함 주포·Lance·LUNA. 씬 BulletFlyby에 소리를 넣고 NPC_Luna 런처를 켭니다 — 그 뒤 Bake", Apply = TuneRailFlyby },
        new Entry { Id = "luna-rail-burst-1", Text = "LUNA 레일건: 조준 레이저를 없애고(경고는 조종석 음성 RAILGUN WARNING 그대로) 0.3초 간격 3연사로 쏩니다 — 뒤 발일수록 표적 진행 방향으로 0.6초씩 더 앞을 겨눠 흩뿌립니다(NPC_Luna 런처)", Apply = ACO_PrefabBuilder.ApplyLunaRailBurst },
        new Entry { Id = "bullet-hit-sound-1", Text = "내 기체 피격음을 SaccFlight 기본 Hit.wav에서 금속판에 총알이 맞는 소리 4개(ElevenLabs — 맞을 때마다 무작위, 음높이도 SAV가 조금씩 바꾼다)로 바꿉니다. 기체 프리팹(SoundController.BulletHit)을 고칩니다", Apply = TuneBulletHitSound },
        new Entry { Id = "hangar-texts-1", Text = "특수무장 정의 20종의 설명을 새 문구(부제 + 브리핑식 한두 문장)로 바꿉니다. 뒤에 Bake(격납고 목록·기본 수치 줄이 따라간다). 격납고 패널은 프리팹 판 ACO_Hangar 6으로 다시 만드세요", Apply = ACO_SpecialWeaponCatalog.RewriteDescriptions },
        new Entry { Id = "hangar-texts-2", Text = "특수무장 설명 7종을 지금 수치에 맞게 고칩니다(4AAM 락온 거리, UGB 가장 넓은 폭발, LGB 좁지만 강함, LGR 위력 = 로켓, SASM 스쳐도 피해, CCAA, HPAA 대형 탄두). 뒤에 Bake", Apply = ACO_SpecialWeaponCatalog.RewriteDescriptions },
        new Entry { Id = "carrier-part-models-1", Text = "항모 CIWS · VLS의 임시 외형(상자 · 포신)을 숨깁니다 — 선체 모델에 포대와 해치가 그려져 있습니다. 히트박스 · 락온 · 사격 · 파괴 연출은 그대로", Apply = () => EditPrefabs(root => root.name == "NPC_Carrier" && ACO_PrefabBuilder.HideCarrierPartModels(root)) },
        new Entry { Id = "carrier-vls-1", Text = "항모에 VLS 4개를 붙입니다 — 모델에 그려진 VLS 해치(양현 포대 옆) 자리, 다른 적 함선과 같은 대공 미사일 발사기. 뒤에 Bake", Apply = TuneCarrierVls },
        new Entry { Id = "ciws-intercept-2", Text = "적 CIWS 요격을 빠르게 하고 오래 쉬게 합니다(반응 0.3 · 격추 0.6 · 쉼 5.5초, 사거리 900m 그대로) — 혼자서도 계속 쏘면 뚫리고 일제 사격은 더 쉽게 뚫립니다", Apply = TuneCiws },
        new Entry { Id = "carrier-ciws-arc-1", Text = "항모 CIWS 4개가 바깥쪽 200°만 맡습니다 — 쉬는 자세를 바깥쪽으로 돌리고, 선체 반대편 미사일은 요격하지 않습니다", Apply = TuneCarrierCiwsArc },
        new Entry { Id = "carrier-ciws-1", Text = "항모 CIWS 4개를 모델에 그려진 포대(양현 스폰슨) 자리로 옮깁니다 — 앞쪽 둘이 갑판 밖 허공에 떠 있었습니다", Apply = TuneCarrierCiws },
        new Entry { Id = "lance-cooler-splash-1", Text = "랜스 냉각탑 4개에 범위 피해 저항(0.45)을 넣습니다 — 지원 폭격 한 방에 넷이 다 깨지던 것을 두 번쯤에. 직접 맞힌 탄은 그대로", Apply = TuneLanceCoolerSplash },
        new Entry { Id = "g-limit-psm-1", Text = "플레이어 기체: G 손상을 끄고(최대 50G는 리미터가), G 리미터 시작을 한계 − 10G로(40G면 30G — 덜 끊기게), 기동성 강화 하중 +10G, 조종석 실속음 · 받음각 삐 · G 손상음을 끄고 G 경고음은 ±30G부터 계속(이 기체 전용 클립 복사본). 하이알파 해금(8 · 10단계)은 스크립트 값", Apply = TuneGLimitPsm },
        new Entry { Id = "ace-extend-1", Text = "에이스(트윈즈 · 보더리스 · 루나 · MR. S)의 최대 이탈 시간을 15초 → 6초로 줄입니다(꼬리를 물린 채 곧게 도망가는 시간). 셰이드(10초)와 일반 NPC(15초)는 그대로", Apply = TuneAceExtend },
        new Entry { Id = "premium-price-1", Text = "특전 무기 MMP · RGP 가격을 50,000 CR로 올립니다(이미 산 사람은 그대로). 뒤에 Bake", Apply = ACO_SpecialWeaponCatalog.TunePremiumPrices },
        new Entry { Id = "landship-maingun-radius-1", Text = "소형 레일건 폭발 지역 반경을 1.5배로 넓힙니다 — 육상전함 주포 320m → 480m, 랜스 포대 170m → 255m", Apply = TuneLandShipMainGunRadius },
        new Entry { Id = "mini-railgun-reburst-1", Text = "랜스 포대 소형 레일건 폭발 지역에서도 1.2초마다 불꽃을 다시 뿜어 피해가 끝날 때까지 불이 보이게 합니다(육상전함 주포와 같게 — 예전엔 끝나기 1~2초 전에 불이 꺼졌다)", Apply = TuneMiniRailgunReburst },
        new Entry { Id = "mini-railgun-lay-1", Text = "소형 레일건(랜스 포대·육상전함 주포): 3.5초 뒤 자리를 노리고 고정하자마자 쏩니다 — 폭발이 표적 앞에 먼저 생겨 남습니다. 폭발 지역 유지 시간은 육상전함 주포 5초, 랜스 포대 4.5초", Apply = TuneMiniRailgunLay },
        new Entry { Id = "mini-railgun-shell-1", Text = "소형 레일건(랜스 포대·육상전함 주포) 폭발 지역에 피해 구와 같은 크기의 반투명 불빛 껍질을 띄웁니다 — 피해가 끝나는 순간 사라집니다", Apply = ACO_PrefabBuilder.PlaceMiniZoneShells },
        new Entry { Id = "landship-core-splash-1", Text = "육상전함 코어가 범위 피해를 받지 않습니다 — 코어를 락온해 직접 맞혀야만 깎입니다(LASM 같은 예외 무기도)", Apply = TuneLandShipCoreSplash },
        new Entry { Id = "boss-core-splash-1", Text = "공중 요새·랜스 포대 코어도 범위 피해를 받지 않습니다 — 코어를 직접 맞혀야만 깎입니다(육상전함은 이미 적용)", Apply = TuneLandShipCoreSplash },
        new Entry { Id = "boss-repair-1", Text = "씬의 공중 요새·랜스 포대 페이즈 모듈: 방공 수리를 한 번에 3곳, 페이즈 전체 최대 6곳으로 맞춥니다", Apply = TuneBossRepairs },
        new Entry { Id = "mmp-missile-model-1", Text = "MMP 탄체(ACO_Missile_MMP)를 맥주캔형 마이크로미사일 모델로 다시 만듭니다 — 엔진 불꽃·궤적이 캔 꼬리 노즐에서 나옵니다", Apply = ACO_PrefabBuilder.RebuildMmpMissile },
        new Entry { Id = "mmp-other-1", Text = "MMP를 격납고 OTHER 분류로 옮기고 강화를 받지 않게 합니다(정의 프리팹·기체 프리팹의 Module_MMP·격납고 목록)", Apply = ACO_SpecialWeaponCatalog.TuneMmpOther },
        new Entry { Id = "superace-twins-1", Text = "슈퍼에이스(예전 REAPER)를 분신 에이스 형제의 형 HEADS로 바꾸고(이름·대사·쌍둥이 규칙) 교차 기동 부품을 붙입니다. 프리팹을 다시 만들지 않아 씬 참조는 그대로입니다", Apply = ACO_PrefabBuilder.ApplyHeadsPreset },
        new Entry { Id = "voice-lines-1", Text = "무전 음성(ElevenLabs)을 빈 음성 칸에 넣습니다 — 육상전함·레이저 에이스 페이즈 대사 표와 시작·성공·실패, 형제(HEADS/TAILS)·SHADE·BORDERLESS 고유 대사(프리팹). 손으로 넣은 클립은 그대로 둡니다", Apply = ACO_VoiceLines.ApplyAll },
        new Entry { Id = "twins-cross-1", Text = "분신 에이스 형제의 교차 기동을 덜 하게 합니다 — 90초마다(첫 교차 30초 뒤), 미사일이 다가와 일찍 교차하는 반응은 40초에 한 번(10초였다)", Apply = ACO_PrefabBuilder.ApplyTwinCrossCooldown },
        new Entry { Id = "shade-cobra-launcher-1", Text = "SHADE: 코브라 때 쏘는 전방위 미사일이 세운 기수 방향으로 사출됩니다(코브라 샷) — 런처(ShadeAllAspect)를 기체 모델 아래로 옮깁니다(발사 지점 자리는 그대로)", Apply = ACO_PrefabBuilder.ApplyShadeLauncherOnModel },
        new Entry { Id = "shade-cobra-missile-1", Text = "SHADE: 평소엔 일반 미사일(일반 발사 원뿔), 코브라 동안만 전방위 미사일을 씁니다 — 전방위 런처(ShadeAllAspect, SHADE 콜드런치 탄 2발)를 붙입니다", Apply = ACO_PrefabBuilder.ApplyShadeCobraMissile },
        new Entry { Id = "shade-cobra-incoming-1", Text = "SHADE 코브라: 나를 쫓는 미사일이 꼬리(뒤 ±60도) 1200m 안으로 올 때 합니다. 코브라를 할 수 있는 동안 뒤에서 오는 미사일엔 플레어를 쓰지 않습니다(앞·옆 미사일과 코브라 간격 중엔 플레어)", Apply = ACO_PrefabBuilder.ApplyShadeCobraIncoming },
        new Entry { Id = "laserace-start-1", Text = "씬의 레이저 에이스 페이즈 모듈: 시작 대사에서 콜사인을 뺍니다(\"Sky Eye to all aircraft.\" → \"All aircraft.\" — 음성도 같이 바뀌었습니다). 손으로 고친 대사는 그대로 둡니다", Apply = TuneBorderlessStart },
        new Entry { Id = "ace-death-fx-1", Text = "에이스(레이저 에이스·SHADE·형제)가 격추되면 다른 전투기처럼 공중 폭발(ACO_DeathFx_Air)과 불붙은 파편(ACO_WreckFx_Air)이 나옵니다 — 이름으로 종류를 가려 지상 폭발이 들어가 있었습니다", Apply = ACO_FxBuilder.ApplyToAces },
        new Entry { Id = "shade-tactics-1", Text = "SHADE: 교차를 90초마다로 줄이고(미사일 반응 교차 40초에 한 번), 미사일을 쏘면 바로 거리를 벌리며(800m 안까지 붙어도 2.2km까지 이탈), 코브라는 뒤 1.5km 안에 적이 있고 미사일도 뒤에서 올 때만 합니다(아니면 플레어)", Apply = ACO_PrefabBuilder.ApplyShadeTactics },
        new Entry { Id = "laser-warn-tone-off-1", Text = "레이저 에이스가 나를 비출 때 도는 경보음을 뺍니다 — 조종석 음성 LASER WARNING만 나옵니다", Apply = ACO_PrefabBuilder.RemoveLaserWarnTone },
        new Entry { Id = "laser-gun-module-1", Text = "레이저 에이스 렌즈 포탑에 레이저 기총(기총 + 레이저 부품)을 답니다 — 빔·소리·피해를 기총 규칙으로(조준이 1.2도 안에 0.2초 머물면 0.25초마다 6). 예전 빔 코드는 지웁니다", Apply = ACO_PrefabBuilder.MigrateBorderlessGun },
        new Entry { Id = "luna-drop-l01-1", Text = "씬의 LUNA 페이즈 모듈: 대사 표 첫 줄(AWACS \"That beam is a targeting laser!...\" — 페이즈 시작 대사와 겹친다)을 뺍니다. 손으로 고친 표는 그대로 둡니다", Apply = TuneLunaDropL01 },
        new Entry { Id = "luna-engage-1", Text = "LUNA(NPC_Luna): 도시 위 선회를 3,000m → 1,500m(반경 2.5km → 2km)로 낮추고, 플레이어가 5km 안이면 궤도를 풀고 추격합니다(3km였다 — 레일건 사거리와 같게). 도시에서 8km 넘으면 복귀(6km였다). AI 탐지 거리 6km·놓침 8km(일반 전투기 기본 2.5km라 선회 고도에서 저공 플레이어를 못 봤다)", Apply = ACO_PrefabBuilder.ApplyLunaEngageToPrefab },
        new Entry { Id = "luna-snipe-1", Text = "LUNA(NPC_Luna) 저격: 40초마다(또는 꼬리 1.5km에 붙으면) 급가속(×1.8)으로 4km 벌렸다 돌아서 레일건을 겨눕니다 — 비행 급가속 상한 MaxBoost 1.8과 저격·플레어 대사 음성을 넣습니다. 에이스는 플레어를 쏠 때 일반 적 플레어 무전(남성 목소리)이 나오지 않습니다", Apply = ACO_PrefabBuilder.ApplyLunaSnipeToPrefab },
        new Entry { Id = "luna-uav-silent-1", Text = "LUNA(NPC_Luna) UAV가 미사일을 요격해도 일반 요격 무전(\"Missile intercepted! Their CIWS shot it down.\" 등)이 나오지 않습니다", Apply = ACO_PrefabBuilder.ApplyLunaUavSilent },
        new Entry { Id = "luna-miss-delay-1", Text = "씬의 LUNA 페이즈 모듈: LUNA를 격추하고 5초 있다가 빗나간 궤도 폭격이 하늘에서 내려오기 시작합니다(착탄은 그 4초 뒤 — 예전엔 격추하자마자 내려와 4초 뒤 착탄)", Apply = TuneLunaMissDelay },
        new Entry { Id = "luna-rail-cone-2", Text = "LUNA(NPC_Luna) 레일건: 조준 경고 15도, 발사 원뿔 8도(숙련도 배율을 받는다), 포구가 돌 수 있는 각도 5도 — 원뿔 안이면 포구가 못 겨눠도 쏩니다(위협 사격). 조준 레이저는 착탄 자리에서 끊지 않고 6km 길게 뻗습니다(luna-rail-cone-1의 12·15·15도를 대신)", Apply = ACO_PrefabBuilder.ApplyLunaRailConeToPrefab },
        new Entry { Id = "luna-snipe-often-1", Text = "LUNA(NPC_Luna) 저격(거리를 벌렸다 급선회로 돌아서 레일건)을 더 자주: 교전 6초 뒤 처음(15초였다), 그 뒤 20초마다(40초), 꼬리에 붙으면 지난 저격 8초 뒤부터(15초)", Apply = ACO_PrefabBuilder.ApplyLunaSnipeTimingToPrefab },
        new Entry { Id = "guardian-forward-1", Text = "씬의 지원 관리자: 가디언 드론 자리를 더 앞으로 — 앞 12m → 25m(옆 18m·위 2m는 그대로). 직접 바꾼 값은 그대로 둡니다", Apply = TuneGuardianForward },
        new Entry { Id = "tanker-max-speed-1", Text = "씬의 지원 관리자: 급유 드론 최고 속도 제한을 400kt → 1000kt로 넉넉하게(최저 180kt는 그대로). 직접 바꾼 값은 그대로 둡니다", Apply = TuneTankerMaxSpeed },
        new Entry { Id = "submarine-torpedo-500-1", Text = "씬의 잠수함 페이즈 모듈: 어뢰 속도 150 → 500m/s(초공동 어뢰 — 6~14km면 40~95초 → 12~28초 뒤 명중). 어뢰 항적은 잠수함 프리팹을 다시 만들면 빠르기에 맞춰집니다", Apply = ACO_PhaseModuleSetup.TuneSubmarineTorpedo },
        new Entry { Id = "submarine-lines-4", Text = "잠수함 대사 표 4판(6-211): 시작 대사를 소나 대형 수중 접촉 · 항모로 돌아가 CAP(AWACS)와 잠망경 심도(적)로, 발사 경보(\"All tubes, fire!\" · \"Vampire, vampire!…\")는 첫 일제 발사 때로 옮깁니다. 32줄과 음성. 손으로 고친 대사는 덮어씁니다", Apply = ACO_PhaseModuleSetup.TuneSubmarineTexts },
        new Entry { Id = "submarine-lines-3", Text = "잠수함 대사 표 3판(6-209): SLCM 소진 · 완전 부상 때 항모 함장의 명령(\"Deck crew, clear the flight deck!…\" · \"Helm, hold course!\")과 아군 승무원의 대답(\"Aye aye, sir!…\" — 새 화자 5번 CARRIER CREW)을 넣습니다. 30줄과 음성. 손으로 고친 대사는 덮어씁니다", Apply = ACO_PhaseModuleSetup.TuneSubmarineTexts },
        new Entry { Id = "submarine-ram-30-1", Text = "씬의 잠수함 페이즈 모듈: 충각 돌격을 선체 20% → 30%에서 시작합니다(돌진까지 8400, 돌진 중 남은 3600)", Apply = ACO_PhaseModuleSetup.TuneSubmarineRam30 },
        new Entry { Id = "submarine-cycle-2", Text = "씬의 잠수함 페이즈 모듈: 잠항 25 → 15초(한 주기 80 → 70초), 부상할 때 부서진 무장 중 무작위 3문만 되살림(충각 돌격 수리는 그대로 모두), 격침 뒤 물속으로 다 가라앉은 다음 2초(최대 35초)에 성공 — 예전엔 10초 만에 풀로 거둬 물 위에서 사라졌다", Apply = ACO_PhaseModuleSetup.TuneSubmarineCycle },
        new Entry { Id = "submarine-ram-20-1", Text = "씬의 잠수함 페이즈 모듈: 완전 부상(충각 돌격)을 선체 50% → 20%에서 시작합니다(선체 체력은 프리팹이 6000 → 12000 — Create Prefabs로 NPC_Submarine을 다시 만드세요)", Apply = ACO_PhaseModuleSetup.TuneSubmarineRam },
        new Entry { Id = "submarine-lines-2", Text = "잠수함 대사 표 2판(6-205): 적 함장이 충각 단계부터 광기(\"항모 하나로 전쟁을 끝내 천만 명을 구한다\" — 충각 · 2km 접근 · 충돌 · 수리 · 격침), 항모는 승조원 모두가 조종사를 믿는 목소리(피격 · 위기 · 2km 접근 · 격침 환호). 26줄과 음성(AWACS · 적 · 항모, ElevenLabs v4)을 넣습니다. 손으로 고친 대사는 덮어씁니다", Apply = ACO_PhaseModuleSetup.TuneSubmarineTexts },
        new Entry { Id = "landship-lines-3", Text = "육상전함 대사 표 3판: 적 함장은 원래 대사로 되돌리고(선원·시스템 대사는 뺌) AWACS는 2판 그대로, 총력 복구 \"Reroute reactor power to the batteries. All of them. Now!\" + AWACS \"It's feeding reactor power to its guns!…\", 코어 폭발 \"Keep firing! Keep fir-\" + 비명(말이 끊기는 곳이 대폭발에 맞게 늦춰 나온다). 음성도 넣습니다", Apply = ACO_VoiceLines.ApplyLandShipV3 },
        new Entry { Id = "landship-lines-2", Text = "육상전함 대사 표를 2판으로 바꿉니다: 평범하던 함장이 배가 타들어 가며 선원을 격벽에 가두고, 선원이 무력화되자 자동 사격 통제로 포대를 전부 되살리는 흐름(함장·선원·시스템·AWACS — 배기구는 궤도 안쪽 푸른 빛이라고 알려 줌). 예전 표 칸은 버리고 음성을 넣습니다", Apply = ACO_VoiceLines.ApplyLandShipV2 },
        new Entry { Id = "ace-flare-lines-1", Text = "에이스 플레어 대사(평소 플레어 — 25초에 한 번, 분신·교차 플레어엔 없음): HEADS \"Coin's still in the air.\", TAILS \"Ooh, shiny! Your missile likes those better!\", 레이저 에이스 \"You're aiming at a line that isn't there.\", SHADE \"You locked a shadow.\"(편대 간격). 형제 글자와 음성 칸을 채웁니다(빈 칸만)", Apply = ACO_VoiceLines.ApplyAll },
        new Entry { Id = "strike-bomb-forward-1", Text = "폭격 호출 폭탄(GBU-24)이 진행 방향과 직각으로 누운 채 떨어지던 것을 고칩니다 — 씬 지원 관리자의 폭탄 모델 회전을 원본 프리팹대로(기수가 떨어지는 방향). 고친 뒤 씬 저장", Apply = ACO_PrefabBuilder.ApplyStrikeBombForward },
        new Entry { Id = "airdrop-model-1", Text = "수송기 투하 보급 상자(팔레트·고정 끈, 흰색·주황 낙하산)·공수부대원(국방색 두 톤 낙하산)을 새 모델·텍스처로, 폭격기 투하 폭탄을 폭격 호출과 같은 GBU-24(기수가 떨어지는 방향)로 바꿉니다. 낙하산은 방향이 저마다 다르고 조금 기울어 천천히 흔들립니다. 투하 수·간격·속도는 그대로", Apply = ACO_PrefabBuilder.ApplyAirDropModels },
        new Entry { Id = "hud-sky-1", Text = "하늘·안개 위 녹색 UI(비행 HUD 선·조준 고리·RWR·HUD 레이더·마커 재질)를 순녹색(0, 1, 0)·불투명하게 맞추고 HUD 글자에 은은한 그림자를 켭니다. 마커 테두리 두께·글자 색·HUD COLOR 설정은 프리팹 판 ACO_HUD·ACO_FlightHud·ACO_HudSettings를 다시 만드세요", Apply = ACO_HUDBuilder.ApplySkyGreen },
        new Entry { Id = "ace-wreck-1", Text = "에이스(형제·레이저 에이스·SHADE·LUNA)가 격추되면 기체 메시가 격추 직전 속도로 앞으로 날아가 빙글빙글 돌며 불·연기를 끌고 떨어집니다(파티클 — 땅에 닿거나 8초 뒤 폭발). 동체 메시 하나를 탄 검갈색 재질로 던지고(원래 메시 그대로 — 굽지 않음) 작은 부품은 파편 파티클이 맡습니다. WreckFall을 붙입니다", Apply = ACO_AceWreck.ApplyAll },
        new Entry { Id = "shade-model-1", Text = "SHADE 기체 모델을 바꿉니다: Su-57을 다듬은 ACO_Shade(새 UV·검정 바탕·노란 윤곽선·엠블럼, 곡면 부드럽게)를 NPC_Shade의 Model 아래 같은 자리에 넣고 예전 Su-57은 꺼 둡니다. 몸체·유리 재질을 만듭니다(Models/Shade)", Apply = ACO_ShadeModel.Apply },
        new Entry { Id = "launch-voice-1", Text = "씬 무전(ACO_Chatter)에 발사 무전 음성(관제기 — Fox two·Fox three·Rifle·Magnum·Greyhound·Bruiser·Pickle·Rockets away·Spear!·Music on·Drive! Drive!)을 빈 칸에 넣고, 글자 칸이 모자라면 늘립니다", Apply = ACO_VoiceLines.ApplyLaunchToScene },
        new Entry { Id = "launch-call-1", Text = "무장마다 발사 무전 코드를 넣습니다(쏜 사람만 — 자막 [내 콜사인] 코드, 음성은 관제기): 일반 미사일·단거리 대공 Fox Two, 장거리 대공·4AAM Fox Three, 4AGM Rifle, LAGM Magnum, SCM Greyhound, LASM Bruiser, 폭탄 Pickle, 로켓 Rockets, RGP Spear, ECM Music, MMP Drive. 이미 코드가 있는 무장은 그대로", Apply = ACO_SpecialWeaponCatalog.TuneLaunchCalls },
        new Entry { Id = "other-extra-ammo-1", Text = "OTHER 무장 MMP·RGP도 EXTRA AMMO 소모품을 받습니다(탄수 ×1.5 — 16 → 24발). 강화 트리는 여전히 받지 않고, ECM은 그대로입니다(정의 프리팹·기체 프리팹 슬롯 모듈)", Apply = ACO_SpecialWeaponCatalog.TuneOtherExtraAmmo },
        new Entry { Id = "mmp-wander-2", Text = "MMP 탄체(ACO_Missile_MMP) 비틀거림을 다시 넣습니다: 노리는 방향을 표적 쪽에서 최대 30도·0.9Hz로 흔들어 구불구불 날아갑니다(mmp-wander-1은 유도가 바로잡아 거의 곧았다). 표적 400m 안에서 줄어 200m부터는 곧게 꽂힙니다", Apply = ACO_PrefabBuilder.ApplyMmpWanderToPrefab },
        new Entry { Id = "callsign-names-1", Text = "씬 이름을 콜사인으로: 페이즈 모듈 Phase_LaserAce → Phase_Borderless, Phase_SuperAce → Phase_Twins, 풀의 NPC_LaserAce → NPC_Borderless, NPC_SuperAce → NPC_Heads, NPC_SuperAce_Tails → NPC_Tails(스크립트·프리팹 이름은 이미 바뀌었다). 이름만 — 연결은 그대로", Apply = RenameToCallsigns },
        new Entry { Id = "cm-upgrade-1", Text = "씬의 진행도(ACO_PlayerProgress): 기체 강화 4번 자리를 안정성 → 대응탄 수(COUNTERMEASURES — 플레어 최대 수, 단계마다 한 발씩, 1단계 1500)로 바꿉니다(저장 키도 새로)", Apply = TuneCountermeasureUpgrade },
        new Entry { Id = "railgun-arm-1", Text = "씬의 랜스 포대 페이즈 모듈: 코어 장갑이 발사 30초 전부터 열리고(25초였다), 공격을 시작할 때까지 첫 충전을 기다립니다(공격이 없으면 180초 뒤)", Apply = TuneRailgunArm },
        new Entry { Id = "rail-hit-sound-1", Text = "레일건 탄(ACO_Missile_LunaRail — LUNA · MR. S)이 플레이어 기체에 맞으면 SAV의 MissileHit 소리가 납니다(타고 있는 사람에게)", Apply = TuneRailHitSound },
        new Entry { Id = "screen-fade-sort-1", Text = "화면 암전 구의 그리기 순서를 맨 위(32767)로 — 튜토리얼 버튼 · 팝업 · 애프터버너 불꽃이 암전 위로 비치던 것을 고칩니다. 고친 뒤 씬 저장", Apply = TuneScreenFadeSort },
        new Entry { Id = "generator-collider-1", Text = "미션 생성기 오브젝트의 박스 콜라이더(월드 원점, 1m)를 지웁니다 — 예전 '눌러서 미션 시작'용으로 남아 있던 보이지 않는 충돌 상자. 고친 뒤 씬 저장", Apply = RemoveGeneratorCollider },
        new Entry { Id = "flap-hull-1", Text = "플레이어 기체 뒷전 플랩 윗면에 헐넘버를 넣습니다(실기처럼 — 플랩을 내리면 뒤 · 옆에서 보인다). 왼쪽 플랩은 왼쪽, 오른쪽 플랩은 오른쪽(바깥쪽) 맞춤, 길면 줄어든다. 격납고 샘플에도", Apply = ACO_HUDBuilder.InstallFlapMarks },
        new Entry { Id = "roundel-1", Text = "플레이어 기체 · 격납고 샘플에 라운델 판 4장(동체 양옆, 접히는 왼쪽 날개 윗면 · 오른쪽 날개 아랫면 — 비스듬히)을 넣습니다. 이미지는 데칼 스튜디오 ROUNDEL에서 URL로 — 없으면 안 보입니다", Apply = ACO_RoundelBuilder.Install },
        new Entry { Id = "roundel-2", Text = "동체 라운델을 캐노피 아래 앞동체(기수 헐넘버 뒤, LEX 아래 — 실기 사진처럼)로 옮깁니다. 라운델 판을 자리 표대로 다시 굽습니다", Apply = ACO_RoundelBuilder.Rebuild },
        new Entry { Id = "mark-fit-1", Text = "기수 헐넘버 · 플랩 헐넘버 글자를 기체 표면 곡면에 붙입니다(판 아래 표면을 재서 글자 셰이더가 휜다 — 뜸 · 묻힘 최대 6 · 9cm → 약 1cm, VR에서 떠 보이지 않게)", Apply = ACO_MarkSurfaceFit.FitAll },
        new Entry { Id = "marks-keep-1", Text = "자유 비행에서 내린 빈 기체에도 마지막 조종사의 헐넘버 · 콜사인 · 킬마크가 남습니다(데칼 · 라운델처럼 — 그 사람이 월드를 나가면 숨김). 기체 표시를 데칼에 잇습니다", Apply = ACO_HUDBuilder.InstallFlapMarks },
        new Entry { Id = "canopy-thin-1", Text = "FA-18E 조종석 앞을 가로지르는 캐노피 앞 프레임 · 앞유리 아치를 가늘게(폭 ×0.65 — 15cm → 약 10cm). 원본 FBX는 그대로, 복사본 메시를 끼웁니다(되돌리기: 메뉴 ACO > FA-18E Canopy Frame > Restore Original)", Apply = ACO_CanopyThin.Apply },
        new Entry { Id = "canopy-thin-3", Text = "FA-18E 앞 아치 볼트: 양쪽 대칭(한쪽 자리를 거울로), 짙은 회색 전용 재질(검은 쪽 아치에서도 보인다), 음영은 아치 면과 볼트 면의 중간(옆면이 검게 죽지 않고 입체감). 미리보기 기체에도", Apply = ACO_CanopyThin.Apply },
        new Entry { Id = "canopy-thin-2", Text = "FA-18E 캐노피 앞 프레임 · 아치 다시 만들기: 아치 볼트 옆면이 검게 보이던 것을 아치 면과 같은 밝기로(반투명 HUD 글자 뒤로 육각 테두리가 진하게 비쳤다), 얇게 하며 드러난 앞유리 끝을 프레임 속으로 다시 넣는다(아치를 따라 유리가 떨어져 보였다). 미리보기 기체에도", Apply = ACO_CanopyThin.Apply },
        new Entry { Id = "escort-waves-1", Text = "호위 페이즈 증원(6-265): 콘보이 웨이포인트 매복 · 상륙전 2차 공습(해변 3km) 무전 음성을 씬 모듈에 넣고 무전 연결(페이즈 모듈 Bake). 고친 뒤 씬 저장", Apply = TuneEscortWaves },
        new Entry { Id = "tunnel-landmark-1", Text = "터널 랜드마크(6-266): Models/Tunnel/ACO_Tunnel.fbx로 Prefabs/ACO_Tunnel 프리팹을 만들고 씬 지형 장식(TunnelPrefab)에 넣습니다 — 같은 높이 3칸 일렬의 가운데 칸에 확률 0.6으로 터널 산(폭 50m x 높이 40m 관통 터널). 고친 뒤 씬 저장", Apply = TuneTunnelLandmark },
        new Entry { Id = "tunnel-landmark-2", Text = "터널 랜드마크 2판(6-266): 1 · 2 · 3칸 터널(약 0.65 · 1.35 · 2.0km, 꼭대기 140m) 프리팹을 만들고 씬 지형 장식에 넣습니다 — 놓을 수 있는 가장 긴 것, 확률 0.9. 고친 뒤 씬 저장", Apply = TuneTunnelLandmark },
        new Entry { Id = "wingmen-note-1", Text = "아군 편대 상점 주의 문구에서 '시가지의 차량은 공격하지 않습니다'를 뺍니다(당연한 내용이라). 격납고 프리팹 · 씬 상점의 4번째 주의 문구가 예전 문장일 때만 바꿉니다", Apply = TuneWingmenNote },
        new Entry { Id = "phase-names-1", Text = "페이즈 이름 · 문구 통일(6-300): ACES - Head&Tail → ACES - {ACE}, ACE SQUADRON → ACE SQUADRON - {BOSS}, SHADE SQUADRON → ACE SQUADRON - SHADE, TIME SENSITIVE 실패 INTERCEPT FAILED → MISSILE LAUNCHED, 무전 문구 오타(Tripple A · you broke · guys 마침표 · 공백 두 칸 · 둥근 따옴표). 씬 페이즈 모듈 — 이전 값일 때만. 번역표 영어 칸은 같이 고쳐 두었다. 뒤에 씬 저장", Apply = TunePhaseNames },
        new Entry { Id = "table-terrain-holo-1", Text = "CIC 작전 테이블 지형을 홀로그램처럼(6-319): 지형 렌더러 재질 ACO_MapTerrain → ACO_MapTerrainHolo(반투명 80%, 렌더 큐 2450) — 윗면 육각형만 그리고(스크립트) 섬 밑 바다 격자가 비친다", Apply = ACO_HUDBuilder.TuneTableTerrainHolo },
        new Entry { Id = "table-grid-sea-only-1", Text = "작전 테이블 격자를 바다 위에만(6-318): 격자 머티리얼(ACO_MapGrid) 깊이 판정 Always → LessEqual, 선 높이 1 → 0.5mm — 섬 · 벽 · 사람에 가려진다(옆방 벽 너머로 비치던 것)", Apply = ACO_RadarBuilder.TuneTableGridSeaOnly },
        new Entry { Id = "cic-panel-over-grid-2", Text = "작전 테이블 격자 머티리얼(ACO_MapGrid) 렌더 큐 2600 → 2440(6-317) — CIC 패널 금속(2449, Standard 불투명이 허용하는 끝)보다 앞에 그린다. 지형 · 마커 뒤라 지형 위에 보이는 건 그대로", Apply = ACO_RadarBuilder.TuneTableGridQueue },
        new Entry { Id = "cic-panel-over-grid-1", Text = "CIC 도구 패널이 작전 테이블 격자에 덮여 보이던 것(6-317): CIC 금속 머티리얼(ACO_Cic_Steel)의 렌더 큐 2000 → 2449 — 격자보다 뒤에 그려 패널이 격자를 가린다", Apply = ACO_HUDBuilder.TuneCicSteelQueue },
        new Entry { Id = "cic-map-north-briefing-1", Text = "CIC 테이블 지도의 북쪽을 브리핑실 쪽(서쪽)으로 돌립니다(6-316): 지도 루트(MapRoot) 요 0 → 270° — 지도 · 마커 · 격자 · 북쪽 표시 · CIC 찍기가 같이 돈다. 도구 패널 쪽에 서면 북쪽이 위", Apply = ACO_InteriorBuilder.TuneCicMapNorth },
        new Entry { Id = "slcm-kh35-1", Text = "잠수함 SLCM 탄체를 Kh-35(SSY 미사일 세트)로 바꿉니다(6-314): 잠수함 프리팹(NPC_Submarine)을 다시 만듭니다 — 순항미사일(토마호크 꼴) · LASM(하푼 꼴)과 구별, 엔진 불꽃 · 연기 · 표적 자리 그대로", Apply = () => ACO_PrefabBuilder.BuildSubmarine() },
        new Entry { Id = "tunnel-lining-1", Text = "터널 안쪽 무늬(6-310): Tools/aco_tunnel_model.py로 다시 만든 FBX(안쪽 벽 · 천장 UV)로 터널 프리팹 다섯 개를 다시 만들고, 콘크리트에 라이닝 텍스처(12m마다 고리 줄눈 · 얼룩 · 물자국 · 벽 아래 때)를 입힙니다. 씬 연결 · 확률은 그대로", Apply = () => ACO_TunnelBuilder.BuildAll() },
        new Entry { Id = "tunnel-smooth-1", Text = "터널 천장 부드럽게(6-309): Tools/aco_tunnel_model.py로 다시 만든 FBX(터널 안쪽 벽 · 아치 셰이드 스무스, 30도 넘게 꺾인 모서리는 날카롭게)로 터널 프리팹 다섯 개를 다시 만듭니다. 씬 연결 · 확률은 그대로", Apply = () => ACO_TunnelBuilder.BuildAll() },
        new Entry { Id = "tunnel-lanes-1", Text = "터널 도로 차선(6-308): Tools/aco_tunnel_model.py로 다시 만든 FBX(도로 UV)로 터널 프리팹 다섯 개를 다시 만들고, 도로에 차선 텍스처(가운데 노란 겹선 · 방향마다 4차로 흰 점선 · 바깥 흰 실선)를 입힙니다. 씬 지형 장식의 연결 · 확률은 그대로", Apply = () => ACO_TunnelBuilder.BuildAll() },
        new Entry { Id = "score-attack-x2-1", Text = "스코어 어택 2배(6-307): 목표 1500 → 3000점, 사냥터 지상 8 → 14대, 전투기 2 → 4기(씬 Phase_ScoreAttack — 이전 값일 때만). 뒤에 씬 저장", Apply = TuneScoreAttackX2 },
        new Entry { Id = "city-marker-colors-2", Text = "도시 차량 표시 색(6-304 수정): 식별된 민간(CIV) = 아군과 같은 하늘색, 미확인(?) = 짙은 노랑. 머티리얼 ACO_MarkerCivilian · ACO_MarkerUnknown, ACO_HUD 프리팹의 글자 색(이전 값일 때만), 씬의 덮어쓴 값은 지운다. 뒤에 씬 저장", Apply = TuneCityMarkerColors2 },
        new Entry { Id = "civ-marker-blue-1", Text = "식별된 민간 차량 표시(CIV) 색 초록 → 파랑(6-304): 마커 테두리 머티리얼 ACO_MarkerCivilian, ACO_HUD 프리팹의 CIV 글자 색(이전 값일 때만), 씬의 덮어쓴 값은 지운다. 뒤에 씬 저장", Apply = TuneCivMarkerBlue },
        new Entry { Id = "amph-landing-tank-spread-1", Text = "상륙 지점 · 전차 산개(6-298): 씬 미션 생성기의 전차 간격 TankSpacing 40 → 150m(매복 · 해안 전차는 옆으로 산개 + 앞뒤 ±50m 엇갈림), 상륙 해안 찾기 시도 LandingCoastAttempts 12 → 30(이전 값일 때만). 뒤에 씬 저장", Apply = TuneAmphLandingTankSpread },
        new Entry { Id = "goggles-pilot-view-3", Text = "CIC 고글 조종사 시점을 기체 기준 자리로(6-290 추가): 조종사 머리 기준 → 기체 원점 기준 AircraftViewOffset(0, 4.66, 3.04 — 지금 자리 그대로). 고글 자리 프리팹 ACO_CicGogglesStation의 두 고글, 씬의 덮어쓴 값은 지운다. 뒤에 씬 저장", Apply = TuneGogglesPilotView3 },
        new Entry { Id = "goggles-pilot-view-2", Text = "CIC 고글 조종사 시점 높이 1m 더(6-290 추가): 조종사 머리 기준 위 1.2 → 2.2m(뒤 1.4m 그대로). 고글 자리 프리팹 ACO_CicGogglesStation의 두 고글, 씬의 덮어쓴 값은 지운다. 뒤에 씬 저장", Apply = TuneGogglesPilotView2 },
        new Entry { Id = "goggles-pilot-view-1", Text = "CIC 고글 조종사 시점 자리(6-290): 조종사 머리 기준 위 0.3 · 뒤 0.8m → 위 1.2 · 뒤 1.4m(좌석 등받이에 묻히던 것). 고글 자리 프리팹 ACO_CicGogglesStation의 두 고글, 씬의 덮어쓴 값은 지운다. 뒤에 씬 저장", Apply = TuneGogglesPilotView },
        new Entry { Id = "precision-identify-300-1", Text = "시가지 정밀 타격 식별(6-290): 일반 식별 직선 200 → 300m, CIC 정찰 UAV 식별 수평 반경 300 → 700m(씬 Phase_Precision — 이전 값일 때만). 뒤에 씬 저장", Apply = TunePrecisionIdentify300 },
        new Entry { Id = "wingman-flare-quiet-1", Text = "아군 편대 플레어 무전 끄기(6-289): NPC_Wingman 프리팹 플레어 장치 RadioCallout 끔(아군 플레어 대사가 수송기 목소리 · TRANSPORT라 윙맨이 수송기처럼 들렸다), 씬 편대의 덮어쓴 값은 지운다. 뒤에 씬 저장", Apply = TuneWingmanFlareQuiet },
        new Entry { Id = "map-vip-marker-1", Text = "작전 테이블(6-285): 아군 호위 대상 마커 원본 VipMarker(초록 + 이름표 VIP)를 씬 테이블에 넣습니다. 아군 편대는 이름표 없는 초록 마커. 뒤에 Bake(지원 관리자 연결) · 씬 저장", Apply = TuneMapVipMarker },
        new Entry { Id = "cic-wingmen-cd-40-1", Text = "CIC 지역 편대 쿨타임 20 → 40초(6-284 — 모두가 같이 쓰는 쿨타임, 편대가 돌아간 뒤부터). 씬 지원 관리자 CicWingmenCooldown이 20일 때만 바꿉니다. 조종석 지원 호출 편대 쿨타임(30초)은 그대로", Apply = TuneCicWingmenCooldown },
        new Entry { Id = "cic-wingmen-area-5km-1", Text = "CIC 지역 편대 구역 3 → 5km(6-283): 씬 지원 관리자 CicWingmenRadius(3000일 때만), NPC_Wingman 프리팹 AreaOrbitRadius 1500 → 2500(씬에 남은 덮어쓴 값은 지운다). 탐지 6.5km · 놓침 8km · 복귀 6.5km는 새 칸(스크립트 기본값)", Apply = TuneCicWingmenArea },
        new Entry { Id = "ecm-20s-1", Text = "ECM 재밍 시간 10 → 20초(6-281 — 재충전 20초는 그대로). 플레이어 기체 프리팹의 Module_ECM과 특수무장 정의(ACO_Special_ECM)를 바꾸고 씬에 남은 덮어쓴 값을 지웁니다", Apply = TuneEcmDuration },
        new Entry { Id = "cic-table-exag-1", Text = "CIC 전술 테이블(6-275) 높이 과장 3 → 1.5배 — 지도 바닥을 찍을 때 산 때문에 어긋나는 걸 줄인다. 함내 격실 안의 작전 테이블이 3일 때만 바꿉니다(브리핑실 테이블은 그대로)", Apply = TuneCicTableExaggeration },
        new Entry { Id = "wingmen-cd-30-1", Text = "아군 편대(6-273) 쿨타임 180 → 30초(편대가 돌아간 뒤부터). 씬 진행도의 4번째 쿨타임이 180일 때만 바꿉니다", Apply = TuneWingmenCooldown },
        new Entry { Id = "wingmen-8-180-1", Text = "아군 편대(6-273) 조정: 가격 8,000 → 5,000 CR, 떠 있는 시간 120 → 180초(효과 글자 2 FIGHTERS 180s). 씬 진행도 · 지원 관리자 값을 바꿉니다(예전 값일 때만). 풀 4 → 8기(4편대)는 Setup Check 지원 관리자 [고치기] → Bake", Apply = TuneWingmen8 },
        new Entry { Id = "precision-identify-200-1", Text = "시가지 정밀 타격 식별 거리(6-270): 80m → 200m — 차량에서 직선 200m 안으로 지나가면 적 · 민간이 식별됩니다. 씬 Phase_Precision 값을 바꿉니다(80이 아니게 손으로 바꾼 값은 그대로)", Apply = TunePrecisionIdentify },
        new Entry { Id = "wingmen-call-1", Text = "지원 호출 아군 편대(6-273): 씬에 저장된 호출권 배열(이름 · 효과 · 가격 8,000 · 쿨타임 180초 — ACO_PlayerProgress), 지원 무전 문장(ACO_SupportManager.LineTexts — 합류 · 격추 · 귀환 3줄), 상점 설명 · 주의(격납고 프리팹 · 씬 ACO_Shop)를 4번째 칸까지 늘립니다. 이미 있는 칸은 그대로. 뒤에 Setup Check 지원 관리자(아군 편대 풀) · Bake · 씬 저장", Apply = TuneWingmenCall },
        new Entry { Id = "player-gun-friendly-off-1", Text = "플레이어 기체끼리 기총 피해 끄기(6-272): F/A-18E 프리팹의 SaccEntity.DisableBulletHitEvent를 켭니다 — 아군 기총 탄(SAV 파티클)이 들어가지 않습니다. 적 기총 · 대공포 · 미사일은 직접 피해라 그대로이고, 보급은 트리거라 영향 없습니다(적 전차 탄 파티클이 기본 피해 10을 한 번 더 주던 것도 같이 사라집니다). 씬이 덮어쓴 값은 지웁니다", Apply = TunePlayerGunFriendlyOff },
        new Entry { Id = "tunnel-landmark-3", Text = "터널 랜드마크 3판(6-266): 곡선형 2 · 3칸(한쪽 활 — 휨 50 · 130m, 능선도 같이 휨)을 더해 프리팹 다섯 개를 만들고 씬 지형 장식에 넣습니다 — 2 · 3칸은 반반 곡선형. 고친 뒤 씬 저장", Apply = TuneTunnelLandmark },
        new Entry { Id = "escort-waves-2", Text = "호위 페이즈 증원 대사(6-265): 콘보이 매복 · 상륙전 2차 공습 대사를 무엇이 오는지 말하지 않는 증원 대사로(\"Enemy reinforcements inbound! …\") — 매복이 실제로 놓이지 않아도 틀리지 않게. 씬 모듈 글자를 새 기본값으로(음성은 같은 파일을 새로 만들었다)", Apply = TuneEscortLines },
        new Entry { Id = "canopy-thin-4", Text = "FA-18E 캐노피 앞 프레임 다시 만들기: 프레임 안쪽에 붙은 손잡이 · 거울이 떠 있던 것을 붙은 자리 프레임 안쪽 면과 같이 옮겨 붙임(6-264)", Apply = ACO_CanopyThin.Apply },
        new Entry { Id = "canopy-thin-preview-1", Text = "격납고 미리보기 기체(PreviewRoot)에도 가늘게 만든 캐노피 앞 프레임 · 아치(볼트 포함) 메시를 끼웁니다. 고친 뒤 씬 저장", Apply = ACO_CanopyThin.ApplyPreview },
        new Entry { Id = "cockpit-display-1", Text = "FA-18E 조종석 UFC(스크래치패드) · 연료계 화면 그림을 깔끔하게 다시 그립니다(각진 사각 테두리 · 가는 선). 원본 텍스처는 그대로, 복사본 텍스처 · 머티리얼을 끼웁니다(되돌리기: 메뉴 ACO > FA-18E Cockpit Displays > Restore Original)", Apply = ACO_CockpitTexture.Apply },
        new Entry { Id = "icbm-ring-1", Text = "레이저 에이스 페이즈 탄도미사일: 대기권 밖 자폭 때 거대한 링(반경 15km, 두 겹)을 더하고 섬광 · 불덩이를 키웁니다(씬의 IcbmSite)", Apply = ApplyIcbmRing },
        new Entry { Id = "submarine-lines-7", Text = "잠수함 대사 표 7판(6-236): 등장 대사(소나 접촉 · 항모로 돌아와 CAP)를 페이즈 시작 대사로 옮깁니다 — 공통 기본 시작 대사 대신 나오고, 표는 33줄. 손으로 고친 대사는 덮어씁니다", Apply = ACO_PhaseModuleSetup.TuneSubmarineTexts },
        new Entry { Id = "submarine-lines-6", Text = "잠수함 대사 표 6판(6-219): 무인기 사출 대사는 AWACS만 — 적 \"Drones away. Keep them off my back.\"을 뺍니다. 34줄과 음성. 손으로 고친 대사는 덮어씁니다", Apply = ACO_PhaseModuleSetup.TuneSubmarineTexts },
        new Entry { Id = "submarine-lines-5", Text = "잠수함 대사 표 5판(6-217): 부상 때 무인기 사출 대사(AWACS \"The sub is launching drones!…\" · 적 \"Drones away…\" · 그 뒤 \"More drones from the sub!\")를 더합니다. 35줄과 음성. 손으로 고친 대사는 덮어씁니다", Apply = ACO_PhaseModuleSetup.TuneSubmarineTexts },
        new Entry { Id = "deck-taxi-10kt-1", Text = "갑판 택싱 최고 속도를 약 5노트(2.6m/s) → 10노트(5.14m/s)로 올립니다(6-216) — 플레이어 기체 프리팹", Apply = TuneDeckTaxi },
        new Entry { Id = "crew-walk-1", Text = "갑판 점검요원 걷는 빠르기 3.2 → 2.2m/s(6-229) — 걷기 동작 보폭에 맞춰 발이 미끄러지지 않게. NPC_CatapultCrew 프리팹, 씬의 같은 값 오버라이드는 지운다", Apply = TuneCrewWalk },
        new Entry { Id = "ordnance-debuglog-off-1",Text = "탄 프리팹(폭탄 ACO_Bomb · ACO_Bomb(LGB))의 DebugHitLog를 끕니다(6-227 — 공개 전, 터질 때마다 로그가 쌓이던 것)", Apply = TuneOrdnanceDebugLogOff },
        new Entry { Id = "fortress-dive-core-1",Text = "공중 요새 3단계(6-226): 도시 돌진 약 2분 30초 → 약 3분(돌진 속도 60 → 49m/s), 코어 체력 4000 → 2000 — NPC_Fortress 프리팹", Apply = TuneFortressDiveCore },
        new Entry { Id = "hud-step-1cm-1", Text = "HUD 설정의 −/+ 한 번 바뀌는 양을 2 → 1로 줄입니다(6-244) — 패널 · 결과 · 자막 · 레이더 위치 2cm → 1cm, 따라오는 지연 0.02초 → 0.01초(ACO_HudSettings 프리팹 Steps). 씬 인스턴스에서 바꾼 Steps는 지운다. 고친 뒤 씬 저장", Apply = TuneHudStep1cm },
        new Entry { Id = "fighter-start-health-1", Text = "NPC 전투기 프리팹의 시작 체력(SaccTarget Health)을 최대 체력(BaseHealth)과 같게 맞춥니다(6-241) — 루나 2000 → 5000, 셰이드 300 → 450, 공격기 150 → 250. 체력 동기화를 놓친 화면에서 스폰하자마자 40% 등으로 보이던 것. 씬 인스턴스에서 바꾼 Health는 지운다. 고친 뒤 씬 저장", Apply = TuneFighterStartHealth },
        new Entry { Id = "hud-defaults-1", Text = "HUD 설정 기본값을 바꿉니다(6-239) — 패널 왼쪽 · 크기 0.7, 결과 글자 아래 · 0.9, 무전 자막 위쪽, 레이더 (-0.28, -0.16) · 크기 0.7 · 5km · 점 1.6, RWR 미사일 0.7 · 락온 호 1.2, 비행 HUD 1.4, 따라오는 지연 없음. 저장된 개인 설정은 그대로. 고친 뒤 씬 저장", Apply = TuneHudDefaults },
        new Entry { Id = "sonar-ring-ui-1", Text = "잠수함 소나 원(NPC_Submarine/SonarRing)을 UI 레이어로 옮깁니다(6-238) — 관전 화면 · VRChat 카메라에 찍히지 않게(HUD 마커와 같다)", Apply = TuneSonarRingLayer },
        new Entry { Id = "artillery-shell-collider-1",Text = "야포 포탄(ACO_ArtilleryShell) 모델의 충돌용 메시를 지웁니다(6-225) — 발사 0.5초 뒤 자기 몸에 맞아 공중에서 터지던 것", Apply = TuneArtilleryShellCollider },
        new Entry { Id = "vls-missile-hot-1",Text = "NPC 프리팹의 VLS 미사일이 예전 ACO_Missile_VLS(비례항법)를 가리키면 기본값 ACO_Missile_VLS_HOT으로 바꿉니다(6-215 — 지금은 적 항모 NPC_Carrier의 VLS 4기)", Apply = TuneVlsMissileHot },
        new Entry { Id = "hangar-decor-2", Text = "격납고 꾸미기 다시 만들기(6-214): 거치대 무장끼리 겹치거나 날개가 거치대에 걸리던 것을 — 표면 겹침 검사로 앞뒤로 엇갈려 놓고, 맞는 자리가 없는 무장은 뺍니다. Hanger 아래 HangarDecor를 지우고 다시 만듭니다. 고친 뒤 씬 저장", Apply = ACO_HangarDecor.Build },
        new Entry { Id = "hangar-decor-1", Text = "격납고 패널 쪽을 EEI 지원 장비로 꾸밉니다 — 무장을 올린 거치대 7대, 견인 트랙터(1대는 소품 기체 기수에 물림), 포드 거치대 4개. Hanger 아래 HangarDecor를 지우고 다시 만듭니다. 고친 뒤 씬 저장", Apply = ACO_HangarDecor.Build },
        new Entry { Id = "deck-taxi-5kt-1", Text = "갑판 택싱 최고 속도를 약 5노트(2.6m/s)로 낮춥니다(예전 10m/s = 약 19노트) — 플레이어 기체 프리팹", Apply = TuneDeckTaxi },
        new Entry { Id = "deco-model-1", Text = "도시, 마을, 항구, 비행장, 등대, 레이더, 통신탑, 유적을 새 모델로 다시 만듭니다. 프리팹을 손으로 고쳤다면 그 내용은 사라집니다", Apply = ACO_DecoBuilder.InstallModels },
        new Entry { Id = "lgb-lgr-balance-2", Text = "LGB · LGR 밸런스(6-261): LGB 피해 350 → 500 · 범위 150 → 50m · 탄수 18 → 12(한 발이 가장 센 정밀 폭탄), LGR 탄수 54 → 60 · 재장전 8 → 6초(유도형 짝은 무유도형의 약 5/6 총량 — 풀업 LGB 500/초 · LGR 667/초, 둘 다 30,000). 플레이어 기체 프리팹(lgb-lgr-balance-1은 LGR 40발 · 8초였다)", Apply = TuneLgbLgr },
        new Entry { Id = "aam-balance-1", Text = "공대공 밸런스(6-262): LAAM 가속 320 → 200 · 최고 속도 1200 → 2400 · 선회 90 → 110(느리게 출발해 멀리서 가장 빠름), HVAA 가속 700 → 1500(곧바로 최고 속도), CCAA 락온 2.5 → 1.5초 · 선회 70 → 90(대에이스). 플레이어 기체 프리팹 + 특수무장 정의 프리팹(설명 포함)", Apply = TuneAamBalance },
        new Entry { Id = "flak-damage-1", Text = "FLAK 피해 30 → 50(6-263 — 신관이 범위 가장자리에서 터져 실제 약 25/발, 전투기 8발 · 총 약 1,350). 플레이어 기체 프리팹", Apply = TuneFlakDamage },
        new Entry { Id = "weapon-defs-sync-1", Text = "특수무장 정의 프리팹 = 기체 값(6-263): 정의(새 기체에 설치할 때 복제되는 원본)의 수치 · 켜기 값을 F/A-18 프리팹 모듈과 같게 — 그동안 기체만 바꿔 정의에 옛 값이 남아 있었다(HVAA 최고 속도 800 등)", Apply = SyncWeaponDefsFromAircraft },
        new Entry { Id = "rkt-balance-3", Text = "로켓(RKT) 밸런스(6-260): 탄수 64 → 72, 슬롯 재장전 3 → 4초 — 풀업 지속 화력 약 1330 → 1000/초(범위가 좁은 만큼 UGB 750보다 높게), 총 피해 36,000(UGB와 같게). 한 번에 8발 연사는 그대로. 플레이어 기체 프리팹(rkt-balance-1 · 2는 탄수 48이었다)", Apply = TuneRocketBalance },
        new Entry { Id = "guardian-intercept-2", Text = "가디언 드론 요격 강화(6-258): 반응 0.1초 · 격추 0.45초 · 쉬는 시간 없음 · 사거리 1500m — 미사일 일제 발사를 거의 막고, 레일건 탄(4000m/s)은 너무 빨라 못 막습니다", Apply = TuneGuardianIntercept },
        new Entry { Id = "gun-damage-all-1", Text = "기총 면역 없애기(6-256): 순양함 · 항모 · 벙커 · 구축함의 피해 문턱(NoDamageBelow)을 0으로 — 기총이 모두 먹힙니다(장갑은 그대로라 한 발 2.5, 구축함 4). 아군 호위 대상과 보스 외피는 그대로. 씬이 덮어쓴 값은 지웁니다", Apply = TuneGunDamageAll },
        new Entry { Id = "destroyer-health-2", Text = "구축함(NPC_Destroyer) 본체 체력 → 700(6-255): 순양함보다 약하게 — 풀업 무유도 폭탄 직격 한 발이면 격침, 최고 숙련도만 두 발. 숙련도 기준(BaseHealth)도 같이, 씬이 덮어쓴 값은 지웁니다", Apply = TuneDestroyerHealth },
        new Entry { Id = "ship-splash-resist-1", Text = "함선 · 보스 범위 피해 저항(6-255): 직격이 아닌 범위 피해는 본체가 일부만 받습니다(순양함 · 구축함 · 항모 0.4, 잠수함 0.5). 부위 범위 피해 배율 — 함선 · 잠수함 · 육상전함 · 공중 요새 0.1, 랜스 포대 0.5. 벙커버스터 피해 1500 → 4500(배율을 받는 대신). 씬이 덮어쓴 값은 지웁니다", Apply = TuneShipSplash },
        new Entry { Id = "fighter-net-sync-1", Text = "NPC 전투기 동기화(6-252): 모든 전투기의 보정 시간 0.5 → 0.8초(받는 순간 속도를 이어 보정 — 기총 리드 표시가 덜 튑니다), 에이스(HEADS · TAILS · SHADE · BORDERLESS · LUNA · MR. S)만 더 자주 보냅니다(최소 간격 0.3 → 0.15초, 위치 4 → 2.5m, 방향 5 → 3도). 씬이 덮어쓴 값은 지웁니다", Apply = TuneFighterNetSync },
    };

    public static bool IsApplied(string id)
    {
        if (!File.Exists(AppliedPath)) { return false; }
        return Array.IndexOf(File.ReadAllLines(AppliedPath), id) >= 0;
    }

    public static void MarkApplied(string id)
    {
        if (IsApplied(id)) { return; }
        File.AppendAllText(AppliedPath, id + "\n");
        AssetDatabase.ImportAsset(AppliedPath);
    }

    public static void Apply(Entry entry)
    {
        entry.Apply();
        AssetDatabase.SaveAssets();
        MarkApplied(entry.Id);
        Debug.Log($"[ACO] 프리팹 수치 '{entry.Id}'를 적용했습니다.");
    }

    // ───────────── CIWS ─────────────

    private static void TuneCiws()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_AAGunSite aa in root.GetComponentsInChildren<ACO_AAGunSite>(true))
            {
                bool dirty = false;
                if (!aa.InterceptDefaultsApplied) { ACO_SceneBaker.ApplyInterceptDefaults(aa); dirty = true; }
                if (aa.InterceptTuneVersion < ACO_SceneBaker.CiwsTuneVersion && aa.MarkerLabel == "CIWS" && (aa.InterceptProtectLayers.value & (1 << 29)) != 0)
                {
                    ACO_SceneBaker.ApplyEnemyCiwsTune(aa);
                    dirty = true;
                }
                if (ACO_SceneBaker.ApplyShellRadius(aa)) { Mark(aa.Gun); dirty = true; }
                if (dirty) { Mark(aa); any = true; }
            }
            return any;
        });
        RevertInScene<ACO_AAGunSite>("InterceptProtectLayers", "InterceptRange", "InterceptReactionTime", "InterceptKillTime",
            "InterceptCooldown", "DefenseRadius", "OtherDefenseFactor", "InterceptTuneVersion", "InterceptDefaultsApplied");
        RevertInScene<ACO_NPCGun>("ShellHitRadius");
    }

    // ───────────── 호위 유닛 ─────────────

    private static void TuneEscortPriority()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_EscortUnit e in root.GetComponentsInChildren<ACO_EscortUnit>(true))
            {
                if (e.PriorityDefaultsApplied) { continue; }
                e.PriorityDefaultsApplied = true;
                bool flagship = e.Role == ACO_EscortUnit.ROLE_FLAGSHIP;
                if (flagship) { e.AttackPriority = 2500f; }
                if (flagship || (e.MoveMode == ACO_EscortUnit.MOVE_AIR && !e.Hostile && e.Role == ACO_EscortUnit.ROLE_VIP)) { e.PriorityForSites = true; }
                Mark(e);
                any = true;
            }
            return any;
        });
        RevertInScene<ACO_EscortUnit>("AttackPriority", "PriorityForSites", "PriorityDefaultsApplied");
    }

    // ───────────── 함대전 ─────────────

    private static void TuneFleet()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_Warship ship in root.GetComponentsInChildren<ACO_Warship>(true))
            {
                if (TuneShip(ship)) { any = true; }
            }
            return any;
        });
        RevertInScene<ACO_SAMSite>("TargetLayers", "MinTargetAltitude", "DetectRange", "LoseTargetRange");
        foreach (ACO_AAGunSite gun in FindInScene<ACO_AAGunSite>())
        {
            if (IsMainGun(gun)) { Revert(gun, "TargetLayers", "MinTargetAltitude"); }
        }
    }

    /// 함선 VLS·ASM·함포가 해수면의 아군 함선(26)을 노리게 한다. 두 함대는 3.5km에서 멈추므로 그보다 짧은 사거리만 올린다.
    public static bool TuneShip(ACO_Warship ship)
    {
        if (ship.ExternalPose || ship.Parts == null) { return false; }   // 공중 요새는 함대전에 나오지 않는다
        bool any = false;
        foreach (UdonSharp.UdonSharpBehaviour part in ship.Parts)
        {
            ACO_SAMSite sam = part as ACO_SAMSite;
            if (sam != null)
            {
                bool dirty = false;
                if ((sam.TargetLayers.value & FriendlyShips) == 0) { sam.TargetLayers = sam.TargetLayers.value | FriendlyShips; dirty = true; }
                if (sam.MinTargetAltitude > -100000f) { sam.MinTargetAltitude = -100000f; dirty = true; }
                if (sam.DetectRange < 4500f) { sam.DetectRange = 5000f; dirty = true; }
                if (sam.LoseTargetRange < 4500f) { sam.LoseTargetRange = 6000f; dirty = true; }
                if (AddShipTargets(sam.EntityControl)) { dirty = true; }
                if (dirty) { Mark(sam); any = true; }
            }
            ACO_AAGunSite gun = part as ACO_AAGunSite;
            if (gun != null && IsMainGun(gun))   // CIWS는 대공·요격 그대로
            {
                bool dirty = false;
                if ((gun.TargetLayers.value & FriendlyShips) == 0) { gun.TargetLayers = gun.TargetLayers.value | FriendlyShips; dirty = true; }
                if (gun.MinTargetAltitude > -100000f) { gun.MinTargetAltitude = -100000f; dirty = true; }
                if (AddShipTargets(gun.EntityControl)) { dirty = true; }
                if (dirty) { Mark(gun); any = true; }
            }
        }
        return any;
    }

    private static bool IsMainGun(ACO_AAGunSite gun) { return gun.gameObject.name.Contains("MainGun"); }

    private static bool AddShipTargets(SaccEntity entity)
    {
        if (entity == null || (entity.AAMTargetsLayer.value & FriendlyShips) != 0) { return false; }
        entity.AAMTargetsLayer |= FriendlyShips;
        Mark(entity);
        return true;
    }

    /// ace-extend-1: 에이스(트윈즈 · 보더리스 · 루나 · MR. S)의 최대 이탈 시간 15 → 6초 — 꼬리를 물린 채 곧게 도망가는 시간을 줄인다.
    /// 셰이드(치고 빠지기 10초 — 코브라가 있다)와 일반 NPC(15초)는 그대로. 씬 인스턴스가 덮어쓴 값은 지워 프리팹 값을 따르게.
    private static readonly string[] AceExtendPrefabs = { "NPC_Heads", "NPC_Tails", "NPC_Borderless", "NPC_Luna", "NPC_MrS" };
    private const float AceMaxExtend = 6f;

    public static void TuneAceExtend()
    {
        EditPrefabs(root =>
        {
            if (Array.IndexOf(AceExtendPrefabs, root.name) < 0) { return false; }
            bool changed = false;
            foreach (ACO_NPCFighterAI ai in root.GetComponentsInChildren<ACO_NPCFighterAI>(true))
            {
                if (Mathf.Approximately(ai.MaxExtendDuration, AceMaxExtend)) { continue; }
                ai.MaxExtendDuration = AceMaxExtend;
                changed = true;
            }
            return changed;
        });
        foreach (ACO_NPCFighterAI ai in FindInScene<ACO_NPCFighterAI>())
        {
            GameObject inst = PrefabUtility.GetNearestPrefabInstanceRoot(ai);
            GameObject src = inst != null ? PrefabUtility.GetCorrespondingObjectFromSource(inst) : null;
            if (src != null && Array.IndexOf(AceExtendPrefabs, src.name) >= 0) { Revert(ai, "MaxExtendDuration"); }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// fighter-net-sync-1(6-252): NPC 전투기 동기화. 모든 전투기 — 보정 시간 0.8초, 속도 이어 보정 · 기동 전환 즉시 전송 켬.
    /// 에이스(자식에 ACO_AceBase가 있는 프리팹)만 — 전송 기준 강화(최소 간격 0.15초, 위치 2.5m, 방향 3도). 씬 인스턴스가 덮어쓴 값은 지워 프리팹 값을 따르게.
    public const float NetSmoothTime = 0.8f;
    public const float AceNetMinSendInterval = 0.15f;
    public const float AceNetPositionError = 2.5f;
    public const float AceNetDirectionError = 3f;

    public static void TuneFighterNetSync()
    {
        int fighters = 0, aces = 0, changedPrefabs = 0;
        EditPrefabs(root =>
        {
            bool changed = false;
            foreach (ACO_NPCFighterNet net in root.GetComponentsInChildren<ACO_NPCFighterNet>(true))
            {
                // 에이스: 자식에 에이스 스크립트가 있다(AI.Ace 칸은 Bake가 씬에서 이어 프리팹엔 비어 있을 수 있다)
                bool ace = net.GetComponentInChildren<ACO_AceBase>(true) != null;
                fighters++;
                if (ace) { aces++; }
                bool c = false;
                if (!Mathf.Approximately(net.SmoothTime, NetSmoothTime)) { net.SmoothTime = NetSmoothTime; c = true; }
                if (!net.KeepVelocityOnCorrect) { net.KeepVelocityOnCorrect = true; c = true; }
                if (!net.SendOnManeuverChange) { net.SendOnManeuverChange = true; c = true; }
                if (ace)
                {
                    if (!Mathf.Approximately(net.MinSendInterval, AceNetMinSendInterval)) { net.MinSendInterval = AceNetMinSendInterval; c = true; }
                    if (!Mathf.Approximately(net.PositionErrorThreshold, AceNetPositionError)) { net.PositionErrorThreshold = AceNetPositionError; c = true; }
                    if (!Mathf.Approximately(net.DirectionErrorThreshold, AceNetDirectionError)) { net.DirectionErrorThreshold = AceNetDirectionError; c = true; }
                }
                if (c) { EditorUtility.SetDirty(net); changed = true; }
            }
            if (changed) { changedPrefabs++; }
            return changed;
        });
        int s = 0;
        foreach (ACO_NPCFighterNet net in FindInScene<ACO_NPCFighterNet>())
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(net)) { continue; }
            Revert(net, "SmoothTime", "KeepVelocityOnCorrect", "SendOnManeuverChange", "ManeuverFollowUpDelay",
                "MinSendInterval", "PositionErrorThreshold", "DirectionErrorThreshold");
            s++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 전투기 동기화(6-252): 프리팹 전투기 {fighters}기(에이스 {aces}기) 확인, 프리팹 {changedPrefabs}개 바꿈. 씬 인스턴스 {s}기의 덮어쓴 값을 지웠습니다. 씬을 저장하세요.");
    }

    /// ship-splash-resist-1(6-255): 함선 · 보스 범위 피해 저항. 직격이 아니면 탄을 약 2배 쓰게 — 본체(CoreSplashScale)와 부위(PartSplashScale) 배율.
    /// 풀업 무유도 폭탄(750 · 반경 500m)이 빗나가도 직격과 같고, 한 발이 부위를 모두 쓸던 것. 씬 인스턴스가 덮어쓴 값은 지워 프리팹 값을 따르게.
    /// 벙커버스터(씬 ACO_SupportManager)는 배율을 받는 대신 피해 1500 → 4500.
    private static readonly string[] SplashPrefabs = { "NPC_Warship", "NPC_Destroyer", "NPC_Carrier", "NPC_Submarine", "NPC_Railgun", "NPC_Fortress", "NPC_LandShip" };
    private static readonly float[] SplashPart = { 0.1f, 0.1f, 0.1f, 0.1f, 0.5f, 0.1f, 0.1f };
    private static readonly float[] SplashCore = { 0.4f, 0.4f, 0.4f, 0.5f, 1f, 1f, 1f };   // 랜스 · 요새 · 육상전함 코어는 범위 피해 면역(CoreSplashImmune)
    public const float StrikeDamageWithSplashScale = 4500f;

    public static void TuneShipSplash()
    {
        int changed = 0;
        EditPrefabs(root =>
        {
            int k = Array.IndexOf(SplashPrefabs, root.name);
            if (k < 0) { return false; }
            bool c = false;
            foreach (ACO_Warship w in root.GetComponentsInChildren<ACO_Warship>(true))
            {
                if (!Mathf.Approximately(w.PartSplashScale, SplashPart[k])) { w.PartSplashScale = SplashPart[k]; c = true; }
                if (!Mathf.Approximately(w.CoreSplashScale, SplashCore[k])) { w.CoreSplashScale = SplashCore[k]; c = true; }
                if (c) { EditorUtility.SetDirty(w); }
            }
            if (c) { changed++; }
            return c;
        });
        int s = 0;
        foreach (ACO_Warship w in FindInScene<ACO_Warship>())
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(w)) { continue; }
            Revert(w, "PartSplashScale", "CoreSplashScale");
            s++;
        }
        int m = 0;
        foreach (ACO_SupportManager sup in FindInScene<ACO_SupportManager>())
        {
            if (Mathf.Approximately(sup.StrikeDamage, StrikeDamageWithSplashScale)) { continue; }
            Undo.RecordObject(sup, "ACO strike damage");
            sup.StrikeDamage = StrikeDamageWithSplashScale;
            EditorUtility.SetDirty(sup);
            m++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 함선 범위 피해 저항(6-255): 프리팹 {changed}개 바꿈, 씬 함선 {s}척의 덮어쓴 값을 지움, 벙커버스터 피해 {m}곳 → {StrikeDamageWithSplashScale}. 씬을 저장하세요.");
    }

    /// destroyer-health-2(6-255): 구축함 본체 체력 1500 → 700 — 순양함(1000)보다 약하게, 풀업 무유도 폭탄 직격 한 발(750)이면 격침, 최고 숙련도(×1.3 = 910)만 두 발(destroyer-health-1은 550이었다).
    /// 숙련도 기준(ACO_Warship.BaseHealth — 보통 Bake가 SaccTarget 체력에서 복사)도 같이. 씬 인스턴스가 덮어쓴 값은 지운다.
    public const float DestroyerHealth = 700f;

    public static void TuneDestroyerHealth()
    {
        EditPrefabs(root =>
        {
            if (root.name != "NPC_Destroyer") { return false; }
            bool c = false;
            foreach (ACO_Warship w in root.GetComponentsInChildren<ACO_Warship>(true))
            {
                if (w.TargetHealth != null && !Mathf.Approximately(w.TargetHealth.Health, DestroyerHealth)) { w.TargetHealth.Health = DestroyerHealth; EditorUtility.SetDirty(w.TargetHealth); c = true; }
                if (!Mathf.Approximately(w.BaseHealth, DestroyerHealth)) { w.BaseHealth = DestroyerHealth; EditorUtility.SetDirty(w); c = true; }
            }
            return c;
        });
        int s = 0;
        foreach (ACO_Warship w in FindInScene<ACO_Warship>())
        {
            GameObject inst = PrefabUtility.GetNearestPrefabInstanceRoot(w);
            GameObject src = inst != null ? PrefabUtility.GetCorrespondingObjectFromSource(inst) : null;
            if (src == null || src.name != "NPC_Destroyer") { continue; }
            Revert(w, "BaseHealth");
            if (w.TargetHealth != null) { Revert(w.TargetHealth, "Health"); }
            s++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 구축함 체력(6-255): NPC_Destroyer 본체 · 숙련도 기준 {DestroyerHealth}, 씬 구축함 {s}척의 덮어쓴 값을 지웠습니다. 씬을 저장하세요.");
    }

    /// gun-damage-all-1(6-256): 기총 면역이던 적(순양함 · 항모 본체 문턱 3, 벙커 문턱 20 — 기총 한 발 10 ÷ 장갑 4 = 2.5)과 구축함의 피해 문턱(NoDamageBelow)을 0으로.
    /// 장갑은 그대로라 한 발 2.5(구축함 4). 아군 호위 대상 · 보스 외피(DisableBulletHitEvent)는 그대로. 씬 인스턴스가 덮어쓴 값은 지운다.
    private static readonly string[] GunDamagePrefabs = { "NPC_Warship", "NPC_Carrier", "NPC_Destroyer", "NPC_Bunker" };

    public static void TuneGunDamageAll()
    {
        int changed = 0;
        EditPrefabs(root =>
        {
            if (Array.IndexOf(GunDamagePrefabs, root.name) < 0) { return false; }
            bool c = false;
            foreach (SaccTarget t in root.GetComponentsInChildren<SaccTarget>(true))
            {
                if (t.NoDamageBelow <= 0f) { continue; }
                t.NoDamageBelow = 0f;
                EditorUtility.SetDirty(t);
                c = true;
            }
            if (c) { changed++; }
            return c;
        });
        int s = 0;
        foreach (SaccTarget t in FindInScene<SaccTarget>())
        {
            GameObject inst = PrefabUtility.GetNearestPrefabInstanceRoot(t);
            GameObject src = inst != null ? PrefabUtility.GetCorrespondingObjectFromSource(inst) : null;
            if (src == null || Array.IndexOf(GunDamagePrefabs, src.name) < 0) { continue; }
            Revert(t, "NoDamageBelow");
            if (t.NoDamageBelow > 0f)
            {
                Undo.RecordObject(t, "ACO gun damage");
                t.NoDamageBelow = 0f;
                Mark(t);
            }
            s++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 기총 피해 문턱(6-256): 프리팹 {changed}개, 씬 {s}곳의 문턱을 0으로 — 순양함 · 항모 · 벙커도 기총이 먹힙니다. 씬을 저장하세요.");
    }

    /// rkt-balance-2(6-260): 플레이어 기체 프리팹의 로켓(Module_RKT) 탄수 64 → 48, 슬롯 재장전 3 → 4초 — 풀업 지속 화력 약 1330 → 1000/초(UGB 750보다 높게).
    public static void TuneRocketBalance()
    {
        const string path = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int n = 0;
            foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (m.name != "Module_RKT") { continue; }
                m.MaxAmmo = ACO_PlayerMissileSetup.RocketAmmo;
                m.SlotReloadTime = ACO_PlayerMissileSetup.RocketSlotReload;
                n++;
            }
            if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            Debug.Log($"[ACO] 로켓 밸런스(6-260): {path}의 로켓 모듈 {n}개 — 탄수 {ACO_PlayerMissileSetup.RocketAmmo}, 재장전 {ACO_PlayerMissileSetup.RocketSlotReload}초.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        foreach (ACO_DFUNC_Missile m in FindInScene<ACO_DFUNC_Missile>())
        {
            if (m.name == "Module_RKT" && PrefabUtility.IsPartOfPrefabInstance(m)) { Revert(m, "MaxAmmo", "SlotReloadTime"); }
        }
    }

    /// lgb-lgr-balance-2(6-261): 플레이어 기체 프리팹의 LGB 피해 350 → 500 · 범위 150 → 50m · 탄수 18 → 12(한 발이 가장 센 정밀 폭탄),
    /// LGR 탄수 54 → 60 · 재장전 8 → 6초(유도형 짝은 무유도형의 약 5/6 총량, 짝 안에선 로켓이 화력 높고 총량 같게).
    public static void TuneLgbLgr()
    {
        const string path = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int n = 0;
            foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (m.name == "Module_LGB")
                {
                    m.Damage = ACO_SpecialWeaponCatalog.LgbDamage;
                    m.SplashRadius = ACO_SpecialWeaponCatalog.LgbSplash;
                    m.MaxAmmo = ACO_SpecialWeaponCatalog.LgbAmmo;
                    n++;
                }
                else if (m.name == "Module_LGR")
                {
                    m.MaxAmmo = ACO_SpecialWeaponCatalog.LgrAmmo;
                    m.SlotReloadTime = ACO_SpecialWeaponCatalog.LgrReload;
                    n++;
                }
            }
            if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            Debug.Log($"[ACO] LGB · LGR 밸런스(6-261): {path}의 모듈 {n}개를 바꿨습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        foreach (ACO_DFUNC_Missile m in FindInScene<ACO_DFUNC_Missile>())
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(m)) { continue; }
            if (m.name == "Module_LGB") { Revert(m, "Damage", "SplashRadius", "MaxAmmo"); }
            else if (m.name == "Module_LGR") { Revert(m, "MaxAmmo", "SlotReloadTime"); }
        }
    }

    /// aam-balance-1(6-262): LAAM(느린 가속 · 가장 높은 최고 속도 · 선회 110), HVAA(가속 1500), CCAA(락온 1.5초 · 선회 90).
    /// 플레이어 기체 프리팹의 모듈과 특수무장 정의 프리팹(새 기체에 설치할 때 복제되는 원본 — 설명도)을 같이 고친다. 씬 기체의 덮어쓴 값은 지운다.
    public static void TuneAamBalance()
    {
        int n = 0;
        const string path = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (ApplyAamBalance(m, m.name.StartsWith("Module_") ? m.name.Substring(7) : "")) { n++; }
            }
            if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        int d = 0;
        foreach (string id in new[] { "LAAM", "HVAA", "CCAA" })
        {
            string defPath = "Assets/ACO_Addon/SpecialWeapons/ACO_Special_" + id + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(defPath) == null) { Debug.LogWarning($"[ACO] 공대공 밸런스(6-262): {defPath}가 없습니다."); continue; }
            GameObject defRoot = PrefabUtility.LoadPrefabContents(defPath);
            try
            {
                ACO_SpecialWeaponDef def = defRoot.GetComponent<ACO_SpecialWeaponDef>();
                if (def == null || def.Module == null) { continue; }
                ApplyAamBalance(def.Module, id);
                def.Description = ACO_SpecialWeaponCatalog.DescriptionFor(id, def.Description);
                PrefabUtility.SaveAsPrefabAsset(defRoot, defPath);
                d++;
            }
            finally { PrefabUtility.UnloadPrefabContents(defRoot); }
        }

        int s = 0;
        foreach (ACO_DFUNC_Missile m in FindInScene<ACO_DFUNC_Missile>())
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(m)) { continue; }
            if (m.name == "Module_LAAM" || m.name == "Module_HVAA" || m.name == "Module_CCAA")
            {
                Revert(m, "MaxSpeed", "Acceleration", "TurnRate", "LockTime");
                s++;
            }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 공대공 밸런스(6-262): 기체 프리팹 모듈 {n}개 · 정의 {d}개를 바꾸고 씬 모듈 {s}개의 덮어쓴 값을 지웠습니다. Bake 후 씬을 저장하세요.");
    }

    /// escort-waves-1(6-265): 콘보이 · 상륙전 모듈의 새 무전 음성 칸을 채우고(빈 칸만) 페이즈 모듈 연결(무전 Chatter 포함)을 다시 한다.
    public static void TuneEscortWaves()
    {
        int n = 0;
        foreach (ACO_PhaseModule m in FindInScene<ACO_PhaseModule>())
        {
            if (!(m is ACO_PhaseConvoy) && !(m is ACO_PhaseAmphibious)) { continue; }
            if (ACO_VoiceLines.ApplyPhase(m)) { n++; }
        }
        ACO_SceneBaker.BakePhaseModulesNow();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 호위 페이즈 증원(6-265): 모듈 {n}개에 음성을 넣고 무전을 연결했습니다. 씬을 저장하세요.");
    }

    /// tunnel-landmark-1 · 2(6-266): 터널 프리팹(1 · 2 · 3칸)을 만들고 씬 지형 장식에 넣는다. 2판부터 확률 0.9(놓을 수 있으면 가장 긴 것).
    /// wingmen-note-1: 상점 주의 문구 줄이기(사용자 "도시안의 적은 공격하지않습니다 빼줘").
    private const string WingmenNoteOld = "ONLY DURING AN OPERATION. THEY CAN BE SHOT DOWN AND WILL NOT ATTACK VEHICLES IN THE CITY.";
    private const string WingmenNoteNew = "ONLY DURING AN OPERATION. THEY CAN BE SHOT DOWN.";

    public static void TuneWingmenNote()
    {
        int n = 0;
        const string hangar = "Assets/ACO_Addon/Prefabs/ACO_Hangar.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(hangar) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(hangar);
            try
            {
                bool ch = false;
                foreach (ACO_Shop shop in root.GetComponentsInChildren<ACO_Shop>(true)) { ch |= ShortenWingmenNote(shop); }
                if (ch) { PrefabUtility.SaveAsPrefabAsset(root, hangar); n++; }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (ACO_Shop shop in FindInScene<ACO_Shop>())
        {
            Undo.RecordObject(shop, "ACO wingmen note");
            if (ShortenWingmenNote(shop)) { PrefabUtility.RecordPrefabInstancePropertyModifications(shop); EditorUtility.SetDirty(shop); n++; }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 아군 편대 주의 문구: {n}곳을 바꿨습니다. 씬을 저장하세요.");
    }

    private static bool ShortenWingmenNote(ACO_Shop shop)
    {
        int c = ACO_PlayerProgress.CALL_WINGMEN;
        if (shop.CallCautions == null || shop.CallCautions.Length <= c || shop.CallCautions[c] != WingmenNoteOld) { return false; }
        shop.CallCautions[c] = WingmenNoteNew;
        return true;
    }

    public const float EcmDuration = 20f;

    /// phase-names-1(6-300): 사용자 "미션 이름이랑 목표 대상 명칭 등 이상하거나 통일되지 않은 부분" → "1, 2, 3부터 해 줘".
    public static void TunePhaseNames()
    {
        int n = 0;
        foreach (ACO_PhaseModule m in FindInScene<ACO_PhaseModule>())
        {
            string name = m.PhaseName;
            if (name == "ACES - Head&Tail") { name = "ACES - {ACE}"; }
            else if (name == "ACE SQUADRON") { name = "ACE SQUADRON - {BOSS}"; }
            else if (name == "SHADE SQUADRON") { name = "ACE SQUADRON - SHADE"; }
            string fail = m.FailText == "INTERCEPT FAILED" ? "MISSILE LAUNCHED" : m.FailText;
            string start = FixPhaseText(m.StartText);
            string ok = FixPhaseText(m.SuccessText);
            string fc = FixPhaseText(m.FailChatterText);
            if (name == m.PhaseName && fail == m.FailText && start == m.StartText && ok == m.SuccessText && fc == m.FailChatterText) { continue; }
            Undo.RecordObject(m, "ACO phase names");
            m.PhaseName = name;
            m.FailText = fail;
            m.StartText = start;
            m.SuccessText = ok;
            m.FailChatterText = fc;
            PrefabUtility.RecordPrefabInstancePropertyModifications(m);
            EditorUtility.SetDirty(m);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 페이즈 이름 · 문구 통일(6-300): 페이즈 모듈 {n}개를 바꿨습니다. 씬을 저장하세요.");
    }

    /// 페이즈 무전 문구 오타 · 문장부호(6-300). 번역표 영어 칸도 같은 규칙으로 고친다(무전 자막은 영어 원문으로 번역을 찾는다).
    public static string FixPhaseText(string s)
    {
        if (string.IsNullOrEmpty(s)) { return s; }
        s = s.Replace("Tripple A", "Triple-A").Replace("! you broke", "! You broke").Replace("\u2019", "'");
        while (s.Contains(" \n")) { s = s.Replace(" \n", "\n"); }
        while (s.Contains("  ")) { s = s.Replace("  ", " "); }
        if (s.EndsWith("Hell of a job, guys")) { s += "."; }
        return s;
    }

    /// score-attack-x2-1(6-307): 사용자 "점수 벌기 임무의 점수 지금의 2배" → A안(목표와 사냥터를 같이 늘림).
    public static void TuneScoreAttackX2()
    {
        int n = 0;
        foreach (ACO_PhaseQuota q in FindInScene<ACO_PhaseQuota>())
        {
            bool changed = false;
            Undo.RecordObject(q, "ACO score attack x2");
            if (q.QuotaScore == 1500) { q.QuotaScore = 3000; changed = true; }
            if (q.GroundUnits == 8) { q.GroundUnits = 14; changed = true; }
            if (q.Fighters == 2) { q.Fighters = 4; changed = true; }
            if (!changed) { continue; }
            PrefabUtility.RecordPrefabInstancePropertyModifications(q);
            EditorUtility.SetDirty(q);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 스코어 어택 2배(6-307): 페이즈 모듈 {n}곳을 바꿨습니다. 씬을 저장하세요.");
    }

    /// city-marker-colors-2(6-304 수정): 사용자 "아군 표시랑 똑같이 하늘색으로 해 주고 미확인은 조금 짙은 노랑색으로 해 줘".
    public static void TuneCityMarkerColors2()
    {
        Color civNew = new Color(0.4f, 0.75f, 1f, 0.9f);
        Color unkNew = new Color(0.9f, 0.72f, 0.1f, 0.9f);
        Color[] civOld = { new Color(0.35f, 1f, 0.45f, 0.9f), new Color(0.35f, 0.5f, 1f, 0.9f) };
        Color unkOld = new Color(0.75f, 0.75f, 0.75f, 0.9f);
        Material civMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ACO_Addon/Generated/ACO_MarkerCivilian.mat");
        if (civMat != null) { civMat.color = ACO_HUDBuilder.CivilianBlue; EditorUtility.SetDirty(civMat); }
        Material unkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ACO_Addon/Generated/ACO_MarkerUnknown.mat");
        if (unkMat != null) { unkMat.color = ACO_HUDBuilder.UnknownYellow; EditorUtility.SetDirty(unkMat); }
        const string path = "Assets/ACO_Addon/Prefabs/ACO_HUD.prefab";
        int n = 0;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (ACO_TargetMarkers m in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
                {
                    if (System.Array.IndexOf(civOld, m.CivilianLabelColor) >= 0) { m.CivilianLabelColor = civNew; n++; }
                    if (m.UnknownLabelColor == unkOld) { m.UnknownLabelColor = unkNew; n++; }
                }
                if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int s = 0;
        foreach (ACO_TargetMarkers m in FindInScene<ACO_TargetMarkers>())
        {
            if (PrefabUtility.IsPartOfPrefabInstance(m)) { Revert(m, "CivilianLabelColor", "UnknownLabelColor"); }
            else
            {
                Undo.RecordObject(m, "ACO city marker colors");
                if (System.Array.IndexOf(civOld, m.CivilianLabelColor) >= 0) { m.CivilianLabelColor = civNew; }
                if (m.UnknownLabelColor == unkOld) { m.UnknownLabelColor = unkNew; }
                EditorUtility.SetDirty(m);
            }
            s++;
        }
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 도시 차량 표시 색(6-304): CIV 하늘색 · 미확인 짙은 노랑 — HUD 프리팹 값 {n}개, 씬 마커 {s}개. 씬을 저장하세요.");
    }

    /// civ-marker-blue-1(6-304): 사용자 "CIV 인식되었을 때 녹색 계열이 아니라 푸른색 계열로 해 줘".
    public static void TuneCivMarkerBlue()
    {
        Color oldLabel = new Color(0.35f, 1f, 0.45f, 0.9f);
        Color newLabel = new Color(0.35f, 0.5f, 1f, 0.9f);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ACO_Addon/Generated/ACO_MarkerCivilian.mat");
        if (mat != null) { mat.color = ACO_HUDBuilder.CivilianBlue; EditorUtility.SetDirty(mat); }
        const string path = "Assets/ACO_Addon/Prefabs/ACO_HUD.prefab";
        int n = 0;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (ACO_TargetMarkers m in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
                {
                    if (m.CivilianLabelColor == oldLabel) { m.CivilianLabelColor = newLabel; n++; }
                }
                if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int s = 0;
        foreach (ACO_TargetMarkers m in FindInScene<ACO_TargetMarkers>())
        {
            if (PrefabUtility.IsPartOfPrefabInstance(m)) { Revert(m, "CivilianLabelColor"); }
            else if (m.CivilianLabelColor == oldLabel) { Undo.RecordObject(m, "ACO civ blue"); m.CivilianLabelColor = newLabel; EditorUtility.SetDirty(m); }
            s++;
        }
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] CIV 표시 파랑(6-304): 머티리얼 {(mat != null ? 1 : 0)}개, HUD 프리팹 마커 {n}개, 씬 마커 {s}개. 씬을 저장하세요.");
    }

    /// amph-landing-tank-spread-1(6-298): 사용자 "150m로 하자, 너무 붙여 두니까 폭탄에 몰살당하더라고" + 상륙 정지점 마른 땅 확인으로 버리는 후보가 늘어 시도 수를 올린다.
    public static void TuneAmphLandingTankSpread()
    {
        int n = 0;
        foreach (ACO_MissionGenerator g in FindInScene<ACO_MissionGenerator>())
        {
            bool changed = false;
            Undo.RecordObject(g, "ACO amphibious landing / tank spread");
            if (Mathf.Approximately(g.TankSpacing, 40f)) { g.TankSpacing = 150f; changed = true; }
            if (g.LandingCoastAttempts == 12) { g.LandingCoastAttempts = 30; changed = true; }
            if (!changed) { continue; }
            PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            EditorUtility.SetDirty(g);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 전차 간격 150m · 상륙 해안 시도 30(6-298): 미션 생성기 {n}곳을 바꿨습니다. 씬을 저장하세요.");
    }

    /// goggles-pilot-view-3(6-290 추가): 조종사 머리 기준 → 기체 기준(사용자 "조종사 머리 말고 기체에 상대적 위치로 하자, 이전 위치를 상대적 위치로 해줘").
    public static void TuneGogglesPilotView3() { SetGogglesPilotView(new Vector3(0f, 4.66f, 3.04f)); }

    /// goggles-pilot-view-2(6-290 추가): 조종사 시점 높이 1m 더 — 머리 기준 칸(CockpitOffset)은 없어졌다. goggles-pilot-view-3이 대신한다.
    public static void TuneGogglesPilotView2() { Debug.Log("[ACO] goggles-pilot-view-2는 goggles-pilot-view-3(기체 기준 자리)로 바뀌었습니다 — 할 일 없음."); }

    private static void SetGogglesPilotView(Vector3 want)
    {
        const string path = "Assets/ACO_Addon/Prefabs/ACO_CicGogglesStation.prefab";
        int n = 0;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (ACO_CicGoggles g in root.GetComponentsInChildren<ACO_CicGoggles>(true)) { g.AircraftViewOffset = want; n++; }
                if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int s = 0;
        foreach (ACO_CicGoggles g in FindInScene<ACO_CicGoggles>())
        {
            if (PrefabUtility.IsPartOfPrefabInstance(g)) { Revert(g, "AircraftViewOffset"); }
            else { Undo.RecordObject(g, "ACO goggles pilot view"); g.AircraftViewOffset = want; EditorUtility.SetDirty(g); }
            s++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 고글 조종사 시점(기체 기준): 프리팹 고글 {n}개, 씬 고글 {s}개를 {want}로 맞췄습니다. 씬을 저장하세요.");
    }

    /// goggles-pilot-view-1(6-290): 고글 조종사 시점 카메라를 캐노피 위 뒤로 — 머리 기준 칸(CockpitOffset)은 없어졌다. goggles-pilot-view-3이 대신한다.
    public static void TuneGogglesPilotView() { Debug.Log("[ACO] goggles-pilot-view-1은 goggles-pilot-view-3(기체 기준 자리)로 바뀌었습니다 — 할 일 없음."); }

    /// precision-identify-300-1(6-290): 식별 거리 일반 200 → 300m, UAV 300 → 700m(사용자 "일반 식별 조건은 300m, UAV 식별 조건은 700m로 하자").
    public static void TunePrecisionIdentify300()
    {
        int n = 0;
        foreach (ACO_PhasePrecision p in FindInScene<ACO_PhasePrecision>())
        {
            bool changed = false;
            Undo.RecordObject(p, "ACO precision identify 300");
            if (Mathf.Approximately(p.IdentifyDistance, 200f)) { p.IdentifyDistance = 300f; changed = true; }
            if (Mathf.Approximately(p.DroneIdentifyRadius, 300f)) { p.DroneIdentifyRadius = 700f; changed = true; }
            if (!changed) { continue; }
            PrefabUtility.RecordPrefabInstancePropertyModifications(p);
            EditorUtility.SetDirty(p);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 정밀 타격 식별 300m · UAV 700m(6-290): Phase_Precision {n}곳을 바꿨습니다. 씬을 저장하세요.");
    }

    /// wingman-flare-quiet-1(6-289): 아군 편대 플레어는 무전을 내지 않는다.
    public static void TuneWingmanFlareQuiet()
    {
        const string path = "Assets/ACO_Addon/Prefabs/NPC_Wingman.prefab";
        int n = 0;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (ACO_FlareDispenser f in root.GetComponentsInChildren<ACO_FlareDispenser>(true)) { f.RadioCallout = false; n++; }
                if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int s = 0;
        foreach (ACO_Wingman w in FindInScene<ACO_Wingman>())
        {
            foreach (ACO_FlareDispenser f in w.GetComponentsInChildren<ACO_FlareDispenser>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(f)) { Revert(f, "RadioCallout"); s++; }
            }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 아군 편대 플레어 무전 끔(6-289): 편대 프리팹 플레어 장치 {n}개, 씬 편대 {s}기의 덮어쓴 값을 지웠습니다. 씬을 저장하세요.");
    }

    /// map-vip-marker-1(6-285): 씬의 작전 테이블에 VIP 마커 원본을 넣는다.
    public static void TuneMapVipMarker()
    {
        int n = 0;
        foreach (ACO_TacticalMap m in FindInScene<ACO_TacticalMap>())
        {
            if (ACO_HUDBuilder.EnsureMapVipMarker(m)) { n++; }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 작전 테이블 VIP 마커(6-285): {n}곳에 넣었습니다. Bake · 씬 저장하세요.");
    }

    /// cic-wingmen-cd-40-1(6-284): CIC 지역 편대 쿨타임 40초(사용자 "윙맨 쿨타임 40초하자").
    public static void TuneCicWingmenCooldown()
    {
        int n = 0;
        foreach (ACO_SupportManager m in FindInScene<ACO_SupportManager>())
        {
            if (!Mathf.Approximately(m.CicWingmenCooldown, 20f)) { continue; }
            Undo.RecordObject(m, "ACO CIC wingmen cooldown");
            m.CicWingmenCooldown = 40f;
            Mark(m);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] CIC 편대 쿨타임 40초(6-284): 지원 관리자 {n}곳. 씬을 저장하세요.");
    }

    /// cic-wingmen-area-5km-1(6-283): CIC 지역 편대 구역 5km — 사용자 "3km 너무 짧지 않나" → A안.
    public static void TuneCicWingmenArea()
    {
        int sm = 0;
        foreach (ACO_SupportManager m in FindInScene<ACO_SupportManager>())
        {
            if (!Mathf.Approximately(m.CicWingmenRadius, 3000f)) { continue; }
            Undo.RecordObject(m, "ACO CIC wingmen area");
            m.CicWingmenRadius = 5000f;
            Mark(m);
            sm++;
        }
        const string path = "Assets/ACO_Addon/Prefabs/NPC_Wingman.prefab";
        int n = 0;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (ACO_Wingman w in root.GetComponentsInChildren<ACO_Wingman>(true)) { w.AreaOrbitRadius = 2500f; n++; }
                if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int s = 0;
        foreach (ACO_Wingman w in FindInScene<ACO_Wingman>())
        {
            if (PrefabUtility.IsPartOfPrefabInstance(w)) { Revert(w, "AreaOrbitRadius"); s++; }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] CIC 지역 편대 5km(6-283): 지원 관리자 {sm}곳, 편대 프리팹 {n}기, 씬 편대 {s}기의 덮어쓴 선회 반경을 지웠습니다. 씬을 저장하세요.");
    }

    /// ecm-20s-1(6-281): ECM 재밍 시간 10 → 20초(사용자 "재밍시간을 20초로하자") — 기체 프리팹 Module_ECM, 특수무장 정의, 씬 덮어쓰기.
    public static void TuneEcmDuration()
    {
        const string path = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
        int n = 0;
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (!m.EcmMode) { continue; }
                m.EcmDuration = EcmDuration;
                n++;
            }
            if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        int d = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ACO_Addon/SpecialWeapons" }))
        {
            string defPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject defRoot = PrefabUtility.LoadPrefabContents(defPath);
            try
            {
                ACO_SpecialWeaponDef def = defRoot.GetComponent<ACO_SpecialWeaponDef>();
                if (def == null || def.Module == null || !def.Module.EcmMode) { continue; }
                def.Module.EcmDuration = EcmDuration;
                PrefabUtility.SaveAsPrefabAsset(defRoot, defPath);
                d++;
            }
            finally { PrefabUtility.UnloadPrefabContents(defRoot); }
        }
        int s = 0;
        foreach (ACO_DFUNC_Missile m in FindInScene<ACO_DFUNC_Missile>())
        {
            if (m.EcmMode && PrefabUtility.IsPartOfPrefabInstance(m)) { Revert(m, "EcmDuration"); s++; }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] ECM 재밍 {EcmDuration}초(6-281): 기체 프리팹 모듈 {n}개, 특수무장 정의 {d}개를 바꾸고 씬 모듈 {s}개의 덮어쓴 값을 지웠습니다. 씬을 저장하세요.");
    }

    /// cic-table-exag-1: CIC 테이블 높이 과장 1.5배(사용자 "테이블 높이과장은 지금의 절반정도로 해줘").
    public static void TuneCicTableExaggeration()
    {
        int n = 0;
        ACO_InteriorZone zone = Object.FindObjectOfType<ACO_InteriorZone>(true);
        foreach (ACO_TacticalMap m in FindInScene<ACO_TacticalMap>())
        {
            if (zone == null || !zone.Contains(m.transform.position) || !Mathf.Approximately(m.VerticalExaggeration, 3f)) { continue; }
            Undo.RecordObject(m, "ACO CIC table");
            m.VerticalExaggeration = 1.5f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(m);
            EditorUtility.SetDirty(m);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] CIC 테이블 높이 과장 1.5배: {n}곳을 바꿨습니다. 씬을 저장하세요.");
    }

    /// wingmen-cd-30-1: 아군 편대 쿨타임 30초(사용자 "윙맨호출 쿨타임 30초로해주고").
    public static void TuneWingmenCooldown()
    {
        int n = 0;
        int c = ACO_PlayerProgress.CALL_WINGMEN;
        foreach (ACO_PlayerProgress p in FindInScene<ACO_PlayerProgress>())
        {
            if (p.CallCooldowns == null || p.CallCooldowns.Length <= c || !Mathf.Approximately(p.CallCooldowns[c], 180f)) { continue; }
            Undo.RecordObject(p, "ACO wingmen cooldown");
            p.CallCooldowns[c] = 30f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(p);
            EditorUtility.SetDirty(p);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 아군 편대 쿨타임 30초: {n}곳을 바꿨습니다. 씬을 저장하세요.");
    }

    /// wingmen-8-180-1: 아군 편대 가격 5,000 · 180초(사용자 "한 5000CR로하고", "떠있는시간 180초로하자").
    public static void TuneWingmen8()
    {
        int n = 0;
        int c = ACO_PlayerProgress.CALL_WINGMEN;
        foreach (ACO_PlayerProgress p in FindInScene<ACO_PlayerProgress>())
        {
            Undo.RecordObject(p, "ACO wingmen 8");
            bool ch = false;
            if (p.CallPrices != null && p.CallPrices.Length > c && p.CallPrices[c] == 8000) { p.CallPrices[c] = 5000; ch = true; }
            if (p.CallEffects != null && p.CallEffects.Length > c && p.CallEffects[c] == "2 FIGHTERS 120s") { p.CallEffects[c] = "2 FIGHTERS 180s"; ch = true; }
            if (ch) { PrefabUtility.RecordPrefabInstancePropertyModifications(p); EditorUtility.SetDirty(p); n++; }
        }
        foreach (ACO_SupportManager m in FindInScene<ACO_SupportManager>())
        {
            if (!Mathf.Approximately(m.WingmenDuration, 120f)) { continue; }
            Undo.RecordObject(m, "ACO wingmen 8");
            m.WingmenDuration = 180f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(m);
            EditorUtility.SetDirty(m);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 아군 편대 5,000 CR · 180초: {n}곳을 바꿨습니다. Setup Check 지원 관리자(풀 8기) [고치기] → Bake → 씬 저장.");
    }

    /// precision-identify-200-1: 시가지 정밀 타격 식별 거리 80 → 200m(사용자 "정밀타격 인식 한 200m로하자").
    public static void TunePrecisionIdentify()
    {
        int n = 0;
        foreach (ACO_PhasePrecision p in FindInScene<ACO_PhasePrecision>())
        {
            if (!Mathf.Approximately(p.IdentifyDistance, 80f)) { continue; }
            Undo.RecordObject(p, "ACO precision identify");
            p.IdentifyDistance = 200f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(p);
            EditorUtility.SetDirty(p);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 정밀 타격 식별 거리: Phase_Precision {n}곳을 200m로 바꿨습니다. 씬을 저장하세요.");
    }

    /// wingmen-call-1(6-273): 저장된 배열이 3칸(벙커버스터 · 급유 · 가디언)이면 아군 편대 칸을 더한다. 길면 그대로.
    public static void TuneWingmenCall()
    {
        int n = 0;
        foreach (ACO_PlayerProgress p in FindInScene<ACO_PlayerProgress>())
        {
            int c = ACO_PlayerProgress.CALL_WINGMEN;
            bool ch = false;
            if (p.CallNames == null || p.CallNames.Length <= c) { p.CallNames = GrowStrings(p.CallNames, c + 1, "WINGMEN"); ch = true; }
            if (p.CallEffects == null || p.CallEffects.Length <= c) { p.CallEffects = GrowStrings(p.CallEffects, c + 1, "2 FIGHTERS 180s"); ch = true; }
            if (p.CallPrices == null || p.CallPrices.Length <= c)
            {
                var a = new int[c + 1];
                if (p.CallPrices != null) { System.Array.Copy(p.CallPrices, a, p.CallPrices.Length); }
                a[c] = 5000;
                p.CallPrices = a;
                ch = true;
            }
            if (p.CallCooldowns == null || p.CallCooldowns.Length <= c)
            {
                var a = new float[c + 1];
                if (p.CallCooldowns != null) { System.Array.Copy(p.CallCooldowns, a, p.CallCooldowns.Length); }
                a[c] = 30f;
                p.CallCooldowns = a;
                ch = true;
            }
            if (ch) { EditorUtility.SetDirty(p); n++; }
        }
        foreach (ACO_SupportManager m in FindInScene<ACO_SupportManager>())
        {
            string[] add = { "Wingmen inbound. Forming up on you.", "Wingman down!", "Wingmen are bingo fuel. RTB." };
            int need = ACO_SupportManager.LINE_WINGMEN_END + 1;
            if (m.LineTexts != null && m.LineTexts.Length >= need) { continue; }
            var t = new string[need];
            int had = m.LineTexts != null ? m.LineTexts.Length : 0;
            if (m.LineTexts != null) { System.Array.Copy(m.LineTexts, t, had); }
            for (int i = had; i < need; i++) { int k = i - ACO_SupportManager.LINE_WINGMEN_CALL; t[i] = k >= 0 && k < add.Length ? add[k] : ""; }
            m.LineTexts = t;
            EditorUtility.SetDirty(m);
            n++;
        }
        // 상점 설명 · 주의: 격납고 프리팹(브리핑실 사본 · 격납고 방 패널의 원본)과 씬 인스턴스
        const string hangar = "Assets/ACO_Addon/Prefabs/ACO_Hangar.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(hangar) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(hangar);
            try
            {
                bool ch = false;
                foreach (ACO_Shop shop in root.GetComponentsInChildren<ACO_Shop>(true)) { ch |= GrowShop(shop); }
                if (ch) { PrefabUtility.SaveAsPrefabAsset(root, hangar); n++; }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (ACO_Shop shop in FindInScene<ACO_Shop>()) { if (GrowShop(shop)) { EditorUtility.SetDirty(shop); n++; } }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 아군 편대 호출권(6-273): 배열 {n}곳을 4번째 칸까지 늘렸습니다. Setup Check(지원 관리자 · 아군 편대 풀) → Bake → 씬 저장.");
    }

    private static bool GrowShop(ACO_Shop shop)
    {
        int c = ACO_PlayerProgress.CALL_WINGMEN;
        bool ch = false;
        if (shop.CallDescriptions == null || shop.CallDescriptions.Length <= c)
        {
            shop.CallDescriptions = GrowStrings(shop.CallDescriptions, c + 1, "TWO FRIENDLY AI FIGHTERS JOIN YOU AND ATTACK ENEMY FIGHTERS FIRST, THEN GROUND TARGETS. THEIR KILLS COUNT AS YOURS.");
            ch = true;
        }
        if (shop.CallCautions == null || shop.CallCautions.Length <= c)
        {
            shop.CallCautions = GrowStrings(shop.CallCautions, c + 1, "ONLY DURING AN OPERATION. THEY CAN BE SHOT DOWN.");
            ch = true;
        }
        return ch;
    }

    private static string[] GrowStrings(string[] a, int len, string last)
    {
        var r = new string[len];
        int had = a != null ? Mathf.Min(a.Length, len) : 0;
        if (a != null) { System.Array.Copy(a, r, had); }
        for (int i = had; i < len; i++) { r[i] = i == len - 1 ? last : ""; }
        return r;
    }

    /// player-gun-friendly-off-1(6-272): 플레이어 기체(F/A-18E 프리팹)가 파티클 탄 피해를 받지 않게 — 기총 팀킬 방지.
    public static void TunePlayerGunFriendlyOff()
    {
        const string path = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        int n = 0;
        try
        {
            foreach (SaccEntity e in root.GetComponentsInChildren<SaccEntity>(true))
            {
                if (e.DisableBulletHitEvent) { continue; }
                e.DisableBulletHitEvent = true;
                n++;
            }
            if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        int scene = 0;
        foreach (SaccEntity e in FindInScene<SaccEntity>())
        {
            if (e.GetComponentInChildren<ACO_VehicleUpgrades>(true) == null) { continue; }   // 플레이어 기체만(NPC 값은 그대로)
            Revert(e, "DisableBulletHitEvent");
            if (!e.DisableBulletHitEvent) { e.DisableBulletHitEvent = true; EditorUtility.SetDirty(e); }
            scene++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 기총 팀킬 방지(6-272): {path}의 SaccEntity {n}곳을 바꾸고 씬 플레이어 기체 {scene}대를 확인했습니다. 씬을 저장하세요.");
    }

    public static void TuneTunnelLandmark()
    {
        GameObject[] prefabs = ACO_TunnelBuilder.BuildAll();
        int n = 0;
        foreach (ACO_TerrainFeatures f in FindInScene<ACO_TerrainFeatures>())
        {
            f.TunnelPrefab = prefabs[0];
            f.TunnelPrefab2 = prefabs[1];
            f.TunnelPrefab3 = prefabs[2];
            f.TunnelPrefab2C = prefabs[3];
            f.TunnelPrefab3C = prefabs[4];
            f.TunnelChance = 0.9f;
            if (PrefabUtility.IsPartOfPrefabInstance(f)) { PrefabUtility.RecordPrefabInstancePropertyModifications(f); }
            EditorUtility.SetDirty(f);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 터널 랜드마크(6-266): 지형 장식 {n}곳에 프리팹 1 · 2 · 3칸을 넣고 확률을 0.9로. 씬을 저장하세요.");
    }

    /// escort-waves-2(6-265): 씬 콘보이 · 상륙전 모듈의 매복 · 2차 공습 글자를 스크립트 기본값(증원 대사)으로.
    public static void TuneEscortLines()
    {
        int n = 0;
        foreach (ACO_PhaseConvoy c in FindInScene<ACO_PhaseConvoy>())
        {
            c.AmbushLine = ACO_PhaseConvoy.DefaultAmbushLine;
            c.FinalAmbushLine = ACO_PhaseConvoy.DefaultFinalAmbushLine;
            EditorUtility.SetDirty(c);
            n++;
        }
        foreach (ACO_PhaseAmphibious a in FindInScene<ACO_PhaseAmphibious>())
        {
            a.StrikeLine = ACO_PhaseAmphibious.DefaultStrikeLine;
            EditorUtility.SetDirty(a);
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 호위 페이즈 증원 대사(6-265): 모듈 {n}개의 글자를 바꿨습니다. 씬을 저장하세요.");
    }

    /// flak-damage-1(6-263): 플레이어 기체 프리팹의 FLAK 피해 30 → 50.
    public static void TuneFlakDamage()
    {
        const string path = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
        int n = 0;
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (m.name != "Module_FLAK") { continue; }
                m.Damage = ACO_SpecialWeaponCatalog.FlakDamage;
                n++;
            }
            if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        int s = 0;
        foreach (ACO_DFUNC_Missile m in FindInScene<ACO_DFUNC_Missile>())
        {
            if (m.name == "Module_FLAK" && PrefabUtility.IsPartOfPrefabInstance(m)) { Revert(m, "Damage"); s++; }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] FLAK 피해(6-263): 기체 프리팹 모듈 {n}개를 바꾸고 씬 모듈 {s}개의 덮어쓴 값을 지웠습니다.");
    }

    /// weapon-defs-sync-1(6-263): 특수무장 정의 프리팹의 모듈 수치를 F/A-18 프리팹의 같은 ID 모듈(Module_ + ID)과 같게 한다.
    /// 숫자 · 켜기 · 선택 값(float/int/bool/enum)만 — 참조(발사 지점 · 소리 · 탄 프리팹 · 글자)와 레이어 마스크는 건드리지 않는다.
    /// 기체 쪽 값이 기준(튜닝 항목들이 기체 프리팹을 바꿔 왔다). 같은 일을 다시 하려면 새 ID(weapon-defs-sync-2)로 이 함수를 부른다.
    public static void SyncWeaponDefsFromAircraft()
    {
        const string path = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
        var air = new Dictionary<string, ACO_DFUNC_Missile>();
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (ACO_DFUNC_Missile m in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (m.name.StartsWith("Module_")) { air[m.name.Substring(7)] = m; }
            }
            int defs = 0, fields = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ACO_Addon/SpecialWeapons" }))
            {
                string defPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject defRoot = PrefabUtility.LoadPrefabContents(defPath);
                try
                {
                    ACO_SpecialWeaponDef def = defRoot.GetComponent<ACO_SpecialWeaponDef>();
                    if (def == null || def.Module == null) { continue; }
                    if (!air.TryGetValue(def.Id, out ACO_DFUNC_Missile src))
                    {
                        Debug.LogWarning($"[ACO] 정의 동기화: 기체에 {def.Id} 모듈이 없어 '{defPath}'를 건너뜁니다.");
                        continue;
                    }
                    int changed = CopyScalars(src, def.Module);
                    if (changed == 0) { continue; }
                    PrefabUtility.SaveAsPrefabAsset(defRoot, defPath);
                    defs++;
                    fields += changed;
                }
                finally { PrefabUtility.UnloadPrefabContents(defRoot); }
            }
            Debug.Log($"[ACO] 정의 동기화(6-263): 특수무장 정의 {defs}개의 값 {fields}개를 기체({path}) 값으로 맞췄습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static int CopyScalars(Object src, Object dst)
    {
        var from = new SerializedObject(src);
        var to = new SerializedObject(dst);
        int n = 0;
        SerializedProperty it = from.GetIterator();
        it.NextVisible(true);
        do
        {
            SerializedPropertyType t = it.propertyType;
            if (t != SerializedPropertyType.Float && t != SerializedPropertyType.Integer
                && t != SerializedPropertyType.Boolean && t != SerializedPropertyType.Enum) { continue; }
            SerializedProperty p = to.FindProperty(it.propertyPath);
            if (p == null || p.propertyType != t) { continue; }
            switch (t)
            {
                case SerializedPropertyType.Float:
                    if (p.floatValue != it.floatValue) { p.floatValue = it.floatValue; n++; }
                    break;
                case SerializedPropertyType.Boolean:
                    if (p.boolValue != it.boolValue) { p.boolValue = it.boolValue; n++; }
                    break;
                default:
                    if (p.intValue != it.intValue) { p.intValue = it.intValue; n++; }
                    break;
            }
        } while (it.NextVisible(false));
        if (n > 0) { to.ApplyModifiedPropertiesWithoutUndo(); }
        return n;
    }

    private static bool ApplyAamBalance(ACO_DFUNC_Missile m, string id)
    {
        switch (id)
        {
            case "LAAM":
                m.MaxSpeed = ACO_SpecialWeaponCatalog.LaamSpeed;
                m.Acceleration = ACO_SpecialWeaponCatalog.LaamAccel;
                m.TurnRate = ACO_SpecialWeaponCatalog.LaamTurn;
                return true;
            case "HVAA":
                m.Acceleration = ACO_SpecialWeaponCatalog.HvaaAccel;
                return true;
            case "CCAA":
                m.LockTime = ACO_SpecialWeaponCatalog.CcaaLockTime;
                m.TurnRate = ACO_SpecialWeaponCatalog.CcaaTurn;
                return true;
        }
        return false;
    }

    /// guardian-intercept-2(6-258): 가디언 드론 요격 강화 — 반응 0.4 → 0.1초, 격추 0.8 → 0.45초, 쉼 2 → 0초, 사거리 1200 → 1500m.
    /// 미사일(600m/s)은 사거리 안 2.5초 동안 5발까지 막고, 레일건 탄(4000m/s)은 사거리를 0.375초에 지나 격추(0.45초)보다 빨라 못 막는다.
    public static void TuneGuardianIntercept()
    {
        // 가디언 프리팹은 NPC_ 이름이 아니라 EditPrefabs가 훑지 않는다 — 직접 연다
        string path = PrefabDir + "/" + ACO_PrefabBuilder.GuardianDronePrefab + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int n = 0;
            foreach (ACO_AAGunSite aa in root.GetComponentsInChildren<ACO_AAGunSite>(true))
            {
                aa.InterceptRange = ACO_PrefabBuilder.GuardianInterceptRange;
                aa.InterceptReactionTime = ACO_PrefabBuilder.GuardianReactionTime;
                aa.InterceptKillTime = ACO_PrefabBuilder.GuardianKillTime;
                aa.InterceptCooldown = ACO_PrefabBuilder.GuardianCooldown;
                n++;
            }
            if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            else { Debug.LogWarning($"[ACO] 가디언 드론 요격(6-258): {path}에서 요격 대공포를 찾지 못했습니다."); }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        int s = 0;
        foreach (ACO_AAGunSite aa in FindInScene<ACO_AAGunSite>())
        {
            GameObject inst = PrefabUtility.GetNearestPrefabInstanceRoot(aa);
            GameObject src = inst != null ? PrefabUtility.GetCorrespondingObjectFromSource(inst) : null;
            if (src == null || src.name != ACO_PrefabBuilder.GuardianDronePrefab) { continue; }
            Revert(aa, "InterceptRange", "InterceptReactionTime", "InterceptKillTime", "InterceptCooldown");
            s++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 가디언 드론 요격(6-258): 프리팹 값을 바꾸고 씬 드론 {s}대의 덮어쓴 값을 지웠습니다. 씬을 저장하세요.");
    }

    /// carrier-ciws-1(6-183): 항모 CIWS 4개를 쿠즈네초프 모델에 그려진 포대 자리로 옮긴다(앞쪽 둘이 갑판 밖 허공에 떠 있었다). 씬에서 옮긴 값은 지운다.
    public static void TuneCarrierCiws()
    {
        EditPrefabs(root =>
        {
            if (root.name != "NPC_Carrier") { return false; }
            bool changed = false;
            for (int i = 0; i < ACO_PrefabBuilder.CarrierCiwsNames.Length; i++)
            {
                Transform t = root.transform.Find("Body/" + ACO_PrefabBuilder.CarrierCiwsNames[i]);
                if (t == null || (t.localPosition - ACO_PrefabBuilder.CarrierCiwsSpots[i]).sqrMagnitude < 0.0001f) { continue; }
                t.localPosition = ACO_PrefabBuilder.CarrierCiwsSpots[i];
                changed = true;
            }
            return changed;
        });
        foreach (ACO_Warship ship in FindInScene<ACO_Warship>())
        {
            GameObject inst = PrefabUtility.GetNearestPrefabInstanceRoot(ship);
            GameObject src = inst != null ? PrefabUtility.GetCorrespondingObjectFromSource(inst) : null;
            if (src == null || src.name != "NPC_Carrier") { continue; }
            foreach (string n in ACO_PrefabBuilder.CarrierCiwsNames)
            {
                Transform t = ship.transform.Find("Body/" + n);
                if (t != null) { Revert(t, "m_LocalPosition"); }
            }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// generator-collider-1(6-188): 미션 생성기의 콜라이더를 지운다(누르는 기능을 지웠다 — 원점에 보이지 않는 충돌 상자만 남아 있었다).
    public static void RemoveGeneratorCollider()
    {
        foreach (ACO_MissionGenerator g in FindInScene<ACO_MissionGenerator>())
        {
            foreach (Collider c in g.GetComponents<Collider>()) { Undo.DestroyObjectImmediate(c); }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// screen-fade-sort-1(6-187): 씬 화면 암전 구의 sorting order를 맨 위로.
    public static void TuneScreenFadeSort()
    {
        foreach (ACO_ScreenFader f in FindInScene<ACO_ScreenFader>())
        {
            if (f.FadeRenderer == null || f.FadeRenderer.sortingOrder == ACO_HUDBuilder.ScreenFadeSortingOrder) { continue; }
            Undo.RecordObject(f.FadeRenderer, "Screen fade sorting order");
            f.FadeRenderer.sortingOrder = ACO_HUDBuilder.ScreenFadeSortingOrder;
            EditorUtility.SetDirty(f.FadeRenderer);
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// carrier-ciws-arc-1(6-186): 항모 CIWS를 바깥쪽 200°로 묶는다. 씬에서 바꾼 YawArc · 포탑 회전은 지운다.
    public static void TuneCarrierCiwsArc()
    {
        EditPrefabs(root => root.name == "NPC_Carrier" && ACO_PrefabBuilder.ApplyCarrierCiwsArc(root));
        foreach (ACO_Warship ship in FindInScene<ACO_Warship>())
        {
            GameObject inst = PrefabUtility.GetNearestPrefabInstanceRoot(ship);
            GameObject src = inst != null ? PrefabUtility.GetCorrespondingObjectFromSource(inst) : null;
            if (src == null || src.name != "NPC_Carrier") { continue; }
            foreach (string n in ACO_PrefabBuilder.CarrierCiwsNames)
            {
                Transform t = ship.transform.Find("Body/" + n);
                ACO_AAGunSite aa = t != null ? t.GetComponent<ACO_AAGunSite>() : null;
                if (aa == null) { continue; }
                Revert(aa, "YawArc");
                if (aa.TurretYaw != null) { Revert(aa.TurretYaw, "m_LocalRotation"); }
            }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// carrier-vls-1(6-184): 항모에 모델 해치 자리의 VLS 4개를 붙인다(예전엔 CIWS 4개뿐 — 모델에 그려진 VLS 해치가 비어 있었다).
    public static void TuneCarrierVls()
    {
        EditPrefabs(root => root.name == "NPC_Carrier" && ACO_PrefabBuilder.EnsureCarrierVls(root));
    }

    /// lance-cooler-splash-1(6-182): 랜스 냉각탑 4개에 범위 피해 저항(ACO_FortressPart.SplashScale) — 지원 폭격 한 방에 넷이 다 깨지지 않게.
    public static void TuneLanceCoolerSplash()
    {
        EditPrefabs(root =>
        {
            if (root.name != "NPC_Railgun") { return false; }
            bool changed = false;
            foreach (ACO_FortressPart p in root.GetComponentsInChildren<ACO_FortressPart>(true))
            {
                if (!p.name.StartsWith("Cooler_") || Mathf.Approximately(p.SplashScale, ACO_PrefabBuilder.LanceCoolerSplashScale)) { continue; }
                p.SplashScale = ACO_PrefabBuilder.LanceCoolerSplashScale;
                changed = true;
            }
            return changed;
        });
        foreach (ACO_FortressPart p in FindInScene<ACO_FortressPart>()) { if (p.name.StartsWith("Cooler_")) { Revert(p, "SplashScale"); } }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// g-limit-psm-1(6-179): 플레이어 기체(ACO_VehicleUpgrades가 붙은 프리팹)의 하중 · 경고음.
    ///  - G 손상 끔(GDamage 0), MaxGs 52(강화 하중 상한 = MaxGs − 2 = 50), G 리미터 시작 = 한계 − 10(40G면 30G — 구간을 넓혀 덜 끊기게), 강화 하중 보너스 +10G.
    ///  - 조종석 실속음(StallTone) · 받음각 삐(AoAWarning) · G 손상음(GDamageWarning)은 AudioSource를 꺼 둔다(애니메이터가 오브젝트를 켜도 소리 없음).
    ///  - G 경고음: 원본 클립(Pit-bool, 다른 기체도 쓴다)은 두고 복사본(Generated/ACO_FA18_Gs.anim — ±30G 넘으면 계속)을 오버라이드 컨트롤러로 이 기체에만.
    private const string AcoGsClipPath = "Assets/ACO_Addon/Generated/ACO_FA18_Gs.anim";
    private const string AcoAnimOverridePath = "Assets/ACO_Addon/Generated/ACO_FA18.overrideController";
    private const float GWarnFrom = 30f;
    private static readonly string[] MutedBeeps = { "StallTone", "AoAWarning", "GDamageWarning" };

    /// 씬 플레이어 기체(ACO_VehicleUpgrades가 붙은)의 프리팹 경로들.
    private static List<string> PlayerAircraftPrefabPaths()
    {
        var paths = new List<string>();
        foreach (ACO_VehicleUpgrades u in FindInScene<ACO_VehicleUpgrades>())
        {
            string p = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(u.gameObject);
            if (!string.IsNullOrEmpty(p) && !paths.Contains(p)) { paths.Add(p); }
        }
        return paths;
    }

    /// 6-189: 기체 Rigidbody 아래의 non-convex MeshCollider — 내 기체(non-kinematic)에선 Unity가 무시하고 오류만 남기고,
    /// 남의 기체(kinematic)에선 실제로 작동해 화면마다 충돌 모양이 다르다. 데칼 스튜디오 미리보기 외형(데칼 건용 콜라이더 포함)을
    /// 복사해 넣으면 따라 들어온다. 히트박스(convex)는 건드리지 않는다.
    private static List<MeshCollider> NonConvexUnderBody(GameObject root)
    {
        var list = new List<MeshCollider>();
        foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.convex) { continue; }
            if (mc.GetComponentInParent<Rigidbody>(true) == null) { continue; }
            list.Add(mc);
        }
        return list;
    }

    /// Setup Check: 플레이어 기체 프리팹의 non-convex MeshCollider 수.
    public static int PlayerAircraftNonConvexColliders()
    {
        int n = 0;
        foreach (string path in PlayerAircraftPrefabPaths())
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null) { n += NonConvexUnderBody(asset).Count; }
        }
        return n;
    }

    /// Setup Check [고치기]: 플레이어 기체 프리팹의 non-convex MeshCollider를 지운다(씬 기체는 프리팹을 따른다).
    public static void RemovePlayerAircraftNonConvexColliders()
    {
        foreach (string path in PlayerAircraftPrefabPaths())
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                List<MeshCollider> list = NonConvexUnderBody(root);
                if (list.Count == 0) { continue; }
                foreach (MeshCollider mc in list) { Object.DestroyImmediate(mc); }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] {Path.GetFileName(path)}: 기체 안의 non-convex MeshCollider {list.Count}개를 지웠습니다(무장 외형 · 표시 — 히트박스는 그대로).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    public static void TuneGLimitPsm()
    {
        var paths = new List<string>();
        foreach (ACO_VehicleUpgrades u in FindInScene<ACO_VehicleUpgrades>())
        {
            string p = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(u.gameObject);
            if (!string.IsNullOrEmpty(p) && !paths.Contains(p)) { paths.Add(p); }
        }
        foreach (string path in paths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (SaccAirVehicle sav in root.GetComponentsInChildren<SaccAirVehicle>(true)) { sav.GDamage = 0f; sav.MaxGs = 52f; }
                foreach (DFUNC_Limits lim in root.GetComponentsInChildren<DFUNC_Limits>(true)) { lim.GLimiter_Begin_Pos = lim.GLimiter_Pos - 10f; }
                foreach (ACO_VehicleUpgrades up in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true)) { up.LimiterGBonus = 10f; }
                foreach (AudioSource a in root.GetComponentsInChildren<AudioSource>(true))
                {
                    if (Array.IndexOf(MutedBeeps, a.name) >= 0) { a.enabled = false; }
                }
                foreach (Animator an in root.GetComponentsInChildren<Animator>(true))
                {
                    AnimatorOverrideController ov = EnsureGsOverride(an.runtimeAnimatorController);
                    if (ov != null) { an.runtimeAnimatorController = ov; }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] 하중 · 경고음(6-179): {Path.GetFileName(path)} — G 손상 끔, 리미터 시작 = 한계 − 10G, 강화 +10G, 실속음 · 받음각 삐 · G 손상음 끔, G 경고 ±{GWarnFrom}G부터 계속.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (SaccAirVehicle sav in FindInScene<SaccAirVehicle>()) { Revert(sav, "GDamage", "MaxGs"); }
        foreach (DFUNC_Limits lim in FindInScene<DFUNC_Limits>()) { Revert(lim, "GLimiter_Begin_Pos"); }
        foreach (ACO_VehicleUpgrades up in FindInScene<ACO_VehicleUpgrades>()) { Revert(up, "LimiterGBonus"); }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// 원본 컨트롤러에 G 경고 클립(이름 끝이 _Gs)이 있으면, 그 클립만 ACO 복사본으로 바꾼 오버라이드 컨트롤러를 만든다(이미 오버라이드면 그대로 갱신).
    private static AnimatorOverrideController EnsureGsOverride(RuntimeAnimatorController current)
    {
        if (current == null) { return null; }
        RuntimeAnimatorController baseCtrl = current is AnimatorOverrideController o0 ? o0.runtimeAnimatorController : current;
        AnimationClip src = null;
        foreach (AnimationClip c in baseCtrl.animationClips) { if (c != null && c.name.EndsWith("_Gs")) { src = c; break; } }
        if (src == null) { return null; }

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AcoGsClipPath);
        if (clip == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AcoGsClipPath));
            clip = Object.Instantiate(src);
            AssetDatabase.CreateAsset(clip, AcoGsClipPath);
        }
        WriteGWarnCurves(clip, src.length);
        EditorUtility.SetDirty(clip);

        var ov = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(AcoAnimOverridePath);
        if (ov == null)
        {
            ov = new AnimatorOverrideController(baseCtrl);
            AssetDatabase.CreateAsset(ov, AcoAnimOverridePath);
        }
        ov.runtimeAnimatorController = baseCtrl;
        ov[src] = clip;
        EditorUtility.SetDirty(ov);
        AssetDatabase.SaveAssets();
        return ov;
    }

    /// G 경고 클립: 애니메이터 "Gs" = G × 0.005 + 0.5(정규화 시간). ±GWarnFrom G를 넘으면 G 경고음 계속(볼륨 0.2), G 손상음은 늘 꺼짐.
    private static void WriteGWarnCurves(AnimationClip clip, float length)
    {
        float neg = (0.5f - 0.005f * GWarnFrom) * length;
        float pos = (0.5f + 0.005f * GWarnFrom) * length;
        float eps = length * 0.0005f;
        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
        {
            string obj = b.path.Substring(b.path.LastIndexOf('/') + 1);
            if (obj != "GWarning" && obj != "GDamageWarning") { continue; }
            bool warn = obj == "GWarning";
            bool active = b.propertyName == "m_IsActive";
            float on = active ? 1f : 0.2f;
            AnimationCurve c;
            if (!warn) { c = Stepped(new Keyframe(0f, 0f), new Keyframe(length, 0f)); }
            else
            {
                c = Stepped(new Keyframe(0f, on), new Keyframe(neg, on), new Keyframe(neg + eps, 0f),
                            new Keyframe(pos - eps, 0f), new Keyframe(pos, on), new Keyframe(length, on));
            }
            AnimationUtility.SetEditorCurve(clip, b, c);
        }
    }

    private static AnimationCurve Stepped(params Keyframe[] keys)
    {
        var c = new AnimationCurve(keys);
        for (int i = 0; i < c.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Constant);
        }
        return c;
    }

    // ───────────── 공통 ─────────────

    /// NPC 프리팹을 하나씩 열어 고친다. 포대 프리팹을 먼저 고쳐야 포대를 품은 함선·요새가 새 값을 받는다.
    public static void EditPrefabs(Func<GameObject, bool> edit)
    {
        var paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("NPC_ t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path).StartsWith("NPC_")) { paths.Add(path); }
        }
        paths.Sort((a, b) =>
        {
            int ta = a.Contains("NPC_Turret") ? 0 : 1;
            int tb = b.Contains("NPC_Turret") ? 0 : 1;
            return ta != tb ? ta - tb : string.CompareOrdinal(a, b);
        });
        foreach (string path in paths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!edit(root)) { continue; }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] 프리팹 수치: {Path.GetFileName(path)}를 고쳤습니다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    private static void Mark(Object o)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        EditorUtility.SetDirty(o);
    }

    private static List<T> FindInScene<T>() where T : Component
    {
        var list = new List<T>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            list.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return list;
    }

    private static void RevertInScene<T>(params string[] fields) where T : Component
    {
        foreach (T c in FindInScene<T>()) { Revert(c, fields); }
    }

    /// 씬 인스턴스의 오버라이드를 지운다 — 프리팹 값이 보이게. 프리팹이 아닌 씬 오브젝트는 그대로다.
    private static void Revert(Component c, params string[] fields)
    {
        if (!PrefabUtility.IsPartOfPrefabInstance(c)) { return; }
        var so = new SerializedObject(c);
        foreach (string field in fields)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null && p.prefabOverride) { PrefabUtility.RevertPropertyOverride(p, InteractionMode.UserAction); }
        }
    }
}
