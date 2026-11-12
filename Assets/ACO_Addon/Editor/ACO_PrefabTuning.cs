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
        new Entry { Id = "deco-model-1", Text = "도시, 마을, 항구, 비행장, 등대, 레이더, 통신탑, 유적을 새 모델로 다시 만듭니다. 프리팹을 손으로 고쳤다면 그 내용은 사라집니다", Apply = ACO_DecoBuilder.InstallModels },
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
                if (aa.InterceptTuneVersion < ACO_SceneBaker.CiwsTuneVersion && (aa.InterceptProtectLayers.value & (1 << 29)) != 0)
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
