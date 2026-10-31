using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using ACO;

/// 씬의 NPC 참조를 빌드 시점에 미리 구워둔다 — SAV가 SaccEntity.AAMTargets를 굽는 것과 같은 방식.
/// 런타임에 주변 NPC를 찾는 비용(OverlapSphere 등)이 없고, NPC를 미리 배치하는 풀링 구조와도 맞는다.
public class ACO_SceneBaker : IVRCSDKBuildRequestedCallback
{
    // SAV의 셋업(10) 다음에 실행한다.
    public int callbackOrder => 11;

    public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
    {
        BakeNeighbors();
        return true;
    }

    [MenuItem("ACO/Bake NPC References")]
    /// 물리 충돌을 만드는 콜라이더(트리거·바퀴 제외).
    private static Collider[] SolidColliders(Component root)
    {
        var list = new List<Collider>();
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger || c is WheelCollider) { continue; }
            list.Add(c);
        }
        return list.ToArray();
    }

    public static void BakeNeighbors()
    {
        var fighters = new List<ACO_NPCFighterAI>();
        var directors = new List<ACO_CombatDirector>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            // 풀에서 꺼져 있는 NPC도 포함해야 하므로 비활성까지 찾는다.
            fighters.AddRange(root.GetComponentsInChildren<ACO_NPCFighterAI>(true));
            directors.AddRange(root.GetComponentsInChildren<ACO_CombatDirector>(true));
        }

        ACO_CombatDirector director = directors.Count > 0 ? directors[0] : null;
        if (directors.Count > 1)
        {
            Debug.LogWarning($"[ACO] CombatDirector가 {directors.Count}개 있습니다. '{director.name}' 하나만 사용합니다.", director);
        }

        // 공중 충돌 방지: 플레이어 기체(강화 확장이 있는 기체)와 NPC 전투기의 충돌 콜라이더
        var playerUpgrades = new List<ACO_VehicleUpgrades>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            playerUpgrades.AddRange(root.GetComponentsInChildren<ACO_VehicleUpgrades>(true));
        }
        foreach (ACO_VehicleUpgrades up in playerUpgrades)
        {
            Component body = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            up.BodyColliders = SolidColliders(body != null ? body : up);
            up.NPCFighters = fighters.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(up);
            EditorUtility.SetDirty(up);
        }

        int npcColliders = 0;
        int maxNpcColliders = 0;
        foreach (ACO_NPCFighterAI fighter in fighters)
        {
            Component npcBody = fighter.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            fighter.BodyColliders = SolidColliders(npcBody != null ? npcBody : fighter);
            fighter.PlayerVehicles = playerUpgrades.ToArray();
            npcColliders += fighter.BodyColliders.Length;
            maxNpcColliders = Mathf.Max(maxNpcColliders, fighter.BodyColliders.Length);

            var others = new List<ACO_NPCFighterAI>(fighters.Count);
            foreach (ACO_NPCFighterAI other in fighters)
            {
                if (other != fighter) { others.Add(other); }
            }

            fighter.Neighbors = others.ToArray();
            fighter.Director = director;
            PrefabUtility.RecordPrefabInstancePropertyModifications(fighter);
            EditorUtility.SetDirty(fighter);
        }

        if (fighters.Count > 0 && playerUpgrades.Count > 0)
        {
            int maxPlayer = 0;
            foreach (ACO_VehicleUpgrades up in playerUpgrades) { maxPlayer = Mathf.Max(maxPlayer, up.BodyColliders.Length); }
            Debug.Log($"[ACO] 공중 충돌 방지: 플레이어 기체 {playerUpgrades.Count}대(한 대 최대 콜라이더 {maxPlayer}개) × NPC 전투기 {fighters.Count}대(콜라이더 {npcColliders}개). "
                + $"내 기체만 건다 — NPC가 켜질 때·탑승 때 프레임당 IgnoreCollision 최대 {maxPlayer * maxNpcColliders}회, 그 뒤 비용 없음.");
        }

        if (director != null)
        {
            director.Fighters = fighters.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(director);
            EditorUtility.SetDirty(director);
        }

        string directorInfo = director != null ? $"CombatDirector '{director.name}' 연결" : "CombatDirector 없음 — 공격 정원 제한 없이 동작";
        Debug.Log($"[ACO] NPC 전투기 {fighters.Count}기의 주변 NPC 목록을 베이크했습니다. {directorInfo}.");

        BakeMissionGenerators();
        ACO_SpecialWeaponCatalog.SyncLoadouts();   // 격납고 특수무장 목록 = 무장 정의(SpecialWeapons 폴더)
        BakePlayerMissiles();
        BakePlayerProgress();
        BakeGameFlow();
        BakeTacticalMaps();
        BakeHudSettings();
        BakeRadio();
        BakeRwr();
        BakeSeatRecenter();
        BakeUiSound();
        BakeSuperAces();
        BakeSky();
        BakeTargetAnimators();
    }

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

    /// 구름·날씨: 육각 지형 연결(지형 시드로 배치·날씨), 게임 진행에 날씨(작전 카드 표시).
    private static void BakeSky()
    {
        ACO_HexTerrain terrain = null;
        var fields = new List<ACO_CloudField>();
        var weathers = new List<ACO_Weather>();
        ACO_GameFlow flow = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            fields.AddRange(root.GetComponentsInChildren<ACO_CloudField>(true));
            weathers.AddRange(root.GetComponentsInChildren<ACO_Weather>(true));
            if (terrain == null) { terrain = root.GetComponentInChildren<ACO_HexTerrain>(true); }
            if (flow == null) { flow = root.GetComponentInChildren<ACO_GameFlow>(true); }
        }
        foreach (ACO_Weather w in weathers)
        {
            if (terrain != null && w.Terrain != terrain) { w.Terrain = terrain; MarkChanged(w); }
            if (w.Sun == null) { w.Sun = ACO_SkyBuilder.FindSun(); if (w.Sun != null) { MarkChanged(w); } }
        }
        if (flow != null)
        {
            ACO_Weather w0 = weathers.Count > 0 ? weathers[0] : null;
            if (flow.Weather != w0) { flow.Weather = w0; MarkChanged(flow); }
        }
        foreach (ACO_CloudField f in fields)
        {
            if (terrain != null && f.Terrain != terrain) { f.Terrain = terrain; MarkChanged(f); }
            if (f.Clouds == null || f.Clouds.Length == 0)
            {
                Debug.LogWarning($"[ACO] 구름 '{f.name}'에 구름 풀이 없습니다 — ACO → Environment → Create Clouds and Rain으로 다시 만드세요.", f);
            }
        }
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
            if (!pooled) { Debug.LogWarning($"[ACO] 슈퍼에이스 '{ai.name}'이 미션 생성기 풀(Fighters)에 없습니다 — 생성기의 UnitPoolRoot 아래로 옮기세요(보너스 페이즈가 안 나온다).", ai); }
        }
    }

    /// 패널 버튼 효과음: 씬에 하나 만들고(없으면) 모든 패널 버튼에 연결한다.
    private static void BakeUiSound()
    {
        bool created;
        ACO_UiSound sound = ACO_HUDBuilder.EnsureUiSound(out created);
        if (created) { Debug.Log("[ACO] 패널 버튼 효과음(ACO_UiSound)을 만들었습니다 — 클립은 Generated/ACO_Ui*.wav(바꿔 끼워도 된다).", sound); }
        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_FlowButton c in root.GetComponentsInChildren<ACO_FlowButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_MissionStartButton c in root.GetComponentsInChildren<ACO_MissionStartButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_HangarButton c in root.GetComponentsInChildren<ACO_HangarButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_HudSettingButton c in root.GetComponentsInChildren<ACO_HudSettingButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_Teleporter c in root.GetComponentsInChildren<ACO_Teleporter>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
        }
        if (linked > 0) { Debug.Log($"[ACO] 버튼 효과음을 버튼 {linked}개에 연결했습니다.", sound); }
    }

    /// 좌석 재조정 ↔ 점수 HUD(안내 문구).
    private static void BakeSeatRecenter()
    {
        ACO_ScoreHUD hud = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (hud == null) { hud = root.GetComponentInChildren<ACO_ScoreHUD>(true); }
        }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_SeatRecenter c in root.GetComponentsInChildren<ACO_SeatRecenter>(true)) { if (c.Hud != hud) { c.Hud = hud; MarkChanged(c); } }
        }
    }

    /// RWR ↔ 미사일 런처(NPC·SAM·플레이어 무장)와 HUD 설정 패널. 씬에 하나(표적 마커 아래, Bake가 만든다).
    private static void BakeRwr()
    {
        ACO_RWR rwr = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (rwr == null) { rwr = root.GetComponentInChildren<ACO_RWR>(true); }
        }
        if (rwr == null) { return; }
        // 예전 기본색(노랑)이면 HUD 초록으로(직접 바꾼 색은 그대로)
        var oldYellow = new Color(1f, 0.85f, 0.2f, 0.9f);
        if (ColorClose(rwr.FarColor, oldYellow))
        {
            rwr.FarColor = new Color(0.35f, 1f, 0.45f, 0.9f);
            rwr.LockColor = new Color(0.35f, 1f, 0.45f, 0.75f);
            MarkChanged(rwr);
            Debug.Log("[ACO] RWR 기본색을 노랑에서 HUD 초록으로 바꿨습니다(가까운 미사일은 그대로 빨강).", rwr);
        }
        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissileLauncher c in root.GetComponentsInChildren<ACO_MissileLauncher>(true)) { if (c.Rwr != rwr) { c.Rwr = rwr; MarkChanged(c); linked++; } }
            foreach (ACO_DFUNC_Missile c in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true)) { if (c.Rwr != rwr) { c.Rwr = rwr; MarkChanged(c); linked++; } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true)) { if (c.Rwr != rwr) { c.Rwr = rwr; MarkChanged(c); linked++; } }
        }
        if (linked > 0) { Debug.Log($"[ACO] RWR '{rwr.name}'을 {linked}곳에 연결했습니다.", rwr); }
    }

    private static bool ColorClose(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;
    }

    /// 무전 채터 ↔ 관리자·생성기, 그리고 상황을 알리는 쪽(작전판, 플레이어·NPC 미사일 런처, NPC 전투기, 기체 강화 확장)에 채터를 넣는다.
    private static void BakeRadio()
    {
        ACO_Chatter radio = null;
        ACO_MissionManager manager = null;
        ACO_MissionGenerator generator = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (radio == null) { radio = root.GetComponentInChildren<ACO_Chatter>(true); }
            if (manager == null) { manager = root.GetComponentInChildren<ACO_MissionManager>(true); }
            if (generator == null) { generator = root.GetComponentInChildren<ACO_MissionGenerator>(true); }
        }
        if (radio == null) { return; }
        bool changed = false;
        if (radio.Manager == null && manager != null) { radio.Manager = manager; changed = true; }
        if (radio.Generator == null && generator != null) { radio.Generator = generator; changed = true; }
        if (changed) { MarkChanged(radio); }

        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissionLog c in root.GetComponentsInChildren<ACO_MissionLog>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_DFUNC_Missile c in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_MissileLauncher c in root.GetComponentsInChildren<ACO_MissileLauncher>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_NPCFighterAI c in root.GetComponentsInChildren<ACO_NPCFighterAI>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_SuperAce c in root.GetComponentsInChildren<ACO_SuperAce>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_FlareDispenser c in root.GetComponentsInChildren<ACO_FlareDispenser>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_VehicleUpgrades c in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
        }
        if (radio.Voice == null) { Debug.LogWarning("[ACO] 무전 채터에 음성 AudioSource(Voice)가 없어 음성·PTT가 들리지 않습니다.", radio); }

        // 씬에 저장된 대사 표가 예전 무전 기본값 그대로면 새 기본 대사로 바꾼다(손으로 고친 대사는 건드리지 않는다)
        const string OldFirstLine = "All aircraft, the operation has begun. Good hunting.";
        if (radio.LineText != null && radio.LineText.Length > 0 && radio.LineText[0] == OldFirstLine)
        {
            var temp = new GameObject("ACO_ChatterDefaults") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                ACO_Chatter d = temp.AddComponent<ACO_Chatter>();   // 필드 기본값만 읽는다
                Undo.RecordObject(radio, "ACO chatter defaults");
                radio.LineEvent = d.LineEvent;
                radio.LineSpeaker = d.LineSpeaker;
                radio.LineText = d.LineText;
                radio.LineClip = new AudioClip[0];
                radio.SpeakerNames = d.SpeakerNames;
                radio.SpeakerColors = d.SpeakerColors;
                MarkChanged(radio);
                Debug.Log("[ACO] 무전 채터의 대사 표를 새 기본 대사로 바꿨습니다(예전 기본값이었음).", radio);
            }
            finally { Object.DestroyImmediate(temp); }
        }
        if (linked > 0) { Debug.Log($"[ACO] 무전 채터 '{radio.name}'를 {linked}곳에 연결했습니다.", radio); }
    }

    /// HUD 설정 패널 ↔ 씬의 HUD(점수 패널·표적 마커). 비어 있을 때만 채운다.
    private static void BakeHudSettings()
    {
        ACO_ScoreHUD hud = null;
        ACO_TargetMarkers markers = null;
        var panels = new List<ACO_HudSettings>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (hud == null) { hud = root.GetComponentInChildren<ACO_ScoreHUD>(true); }
            if (markers == null) { markers = root.GetComponentInChildren<ACO_TargetMarkers>(true); }
            panels.AddRange(root.GetComponentsInChildren<ACO_HudSettings>(true));
        }
        foreach (ACO_HudSettings panel in panels)
        {
            bool changed = false;
            if (panel.ScoreHUD == null && hud != null) { panel.ScoreHUD = hud; changed = true; }
            if (panel.Markers == null && markers != null) { panel.Markers = markers; changed = true; }
            if (changed) { MarkChanged(panel); }
        }
        if (panels.Count > 1) { Debug.LogWarning($"[ACO] HUD 설정 패널이 {panels.Count}개 있습니다 — 같은 저장 값을 쓰므로 하나만 두는 걸 권합니다."); }
    }

    /// 작전 테이블 ↔ 생성기(유닛 풀·지형) / 게임 진행(참가자 기체). 비어 있을 때만 채운다.
    private static void BakeTacticalMaps()
    {
        ACO_MissionGenerator generator = null;
        ACO_GameFlow flow = null;
        var maps = new List<ACO_TacticalMap>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (generator == null) { generator = root.GetComponentInChildren<ACO_MissionGenerator>(true); }
            if (flow == null) { flow = root.GetComponentInChildren<ACO_GameFlow>(true); }
            maps.AddRange(root.GetComponentsInChildren<ACO_TacticalMap>(true));
        }
        foreach (ACO_TacticalMap map in maps)
        {
            bool changed = false;
            if (map.Generator == null && generator != null) { map.Generator = generator; changed = true; }
            if (map.Flow == null && flow != null) { map.Flow = flow; changed = true; }
            if (changed) { MarkChanged(map); }
            if (map.HomeBase == null) { Debug.LogWarning($"[ACO] 작전 테이블 '{map.name}': HomeBase(항모)가 비어 있어 기지 표시가 없습니다.", map); }
        }
    }

    /// 게임 진행(씬에 하나)을 관리자·로비 콘솔·격납고 버튼·기체 강화 확장에 연결한다.
    /// 참가자 기체(Aircraft)가 비면 강화 확장이 있는 기체를 Hierarchy 순서로 최대 8대 넣는다. 상태 글자는 이름(ACO_FlowStatus)으로 찾는다.
    private static void BakeGameFlow()
    {
        var flows = new List<ACO_GameFlow>();
        ACO_MissionManager manager = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            flows.AddRange(root.GetComponentsInChildren<ACO_GameFlow>(true));
            if (manager == null) { manager = root.GetComponentInChildren<ACO_MissionManager>(true); }
        }
        if (flows.Count == 0) { return; }
        ACO_GameFlow flow = flows[0];
        if (flows.Count > 1) { Debug.LogWarning($"[ACO] 게임 진행(ACO_GameFlow)이 {flows.Count}개 있습니다. '{flow.name}' 하나만 연결합니다.", flow); }

        if (flow.Manager == null) { flow.Manager = manager; }
        // 지형 이름: 프리셋이 늘었으면 새 이름을 붙인다
        string[] defNames = { "BIG ISLAND", "ARCHIPELAGO", "TWIN ISLANDS", "ATOLL", "ISLAND CHAIN", "PLATEAU", "MOUNTAIN RANGE", "CANYON", "COASTLINE", "VOLCANO", "REEF ISLANDS", "BASIN" };
        if (flow.TerrainNames == null || flow.TerrainNames.Length < defNames.Length)
        {
            var names = new string[defNames.Length];
            for (int k = 0; k < names.Length; k++) { names[k] = flow.TerrainNames != null && k < flow.TerrainNames.Length ? flow.TerrainNames[k] : defNames[k]; }
            flow.TerrainNames = names;
        }
        ACO_ScreenFader fader = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (fader == null) { fader = root.GetComponentInChildren<ACO_ScreenFader>(true); }
        }
        flow.Fader = fader;
        ACO_PlayerProgress progress = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (progress == null) { progress = root.GetComponentInChildren<ACO_PlayerProgress>(true); }
        }
        flow.Progress = progress;
        if (flow.BriefingPoint == null) { Debug.LogWarning("[ACO] 게임 진행: BriefingPoint가 없어 미션이 끝나도 대기실로 옮기지 않습니다" + (flow.HangarPoint == null ? ", 격추·탈출한 참가자도 바다에 남습니다." : "."), flow); }
        if (flow.SpectatePoint == null) { Debug.LogWarning("[ACO] 게임 진행: SpectatePoint가 없어 관전(SPECTATE) 버튼이 동작하지 않습니다.", flow); }
        if (flow.Aircraft == null || flow.Aircraft.Length == 0)
        {
            var aircraft = new List<SaccFlightAndVehicles.SaccEntity>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (ACO_VehicleUpgrades up in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true))
                {
                    SaccFlightAndVehicles.SaccEntity e = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
                    if (e != null && !aircraft.Contains(e) && aircraft.Count < 8) { aircraft.Add(e); }
                }
            }
            flow.Aircraft = aircraft.ToArray();
            Debug.Log($"[ACO] 게임 진행: 참가자 기체 {aircraft.Count}대를 넣었습니다(강화 확장이 있는 기체). 순서 = 칸 번호.", flow);
        }
        int slots = flow.Aircraft.Length;
        int decks = 0;
        if (flow.DeckPoints != null) { foreach (Transform t in flow.DeckPoints) { if (t != null) { decks++; } } }
        if (decks < slots) { Debug.LogWarning($"[ACO] 게임 진행: 기체 {slots}대인데 갑판 출격 위치(DeckPoints)가 {decks}개입니다 — 위치가 없는 칸은 출격할 수 없습니다.", flow); }

        var status = new List<TMPro.TextMeshProUGUI>();
        var banners = new List<TMPro.TextMeshProUGUI>();
        var phaseTexts = new List<TMPro.TextMeshProUGUI>();
        var phaseInfos = new List<TMPro.TextMeshProUGUI>();
        var terrainTexts = new List<TMPro.TextMeshProUGUI>();
        var buttons = new List<ACO_FlowButton>();
        var phaseButtons = new List<ACO_MissionStartButton>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (TMPro.TextMeshProUGUI t in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                string n = t.gameObject.name;
                if (n == ACO_HUDBuilder.FlowStatusName) { status.Add(t); }
                else if (n == ACO_HUDBuilder.FlowBannerName) { banners.Add(t); }
                else if (n == ACO_HUDBuilder.FlowPhasesName) { phaseTexts.Add(t); }
                else if (n == ACO_HUDBuilder.FlowPhaseInfoName) { phaseInfos.Add(t); }
                else if (n == ACO_HUDBuilder.FlowTerrainName) { terrainTexts.Add(t); }
            }
            foreach (ACO_FlowButton b in root.GetComponentsInChildren<ACO_FlowButton>(true))
            {
                if (b.Flow != flow) { b.Flow = flow; MarkChanged(b); }
                if (b.Background == null && b.GetComponent<UnityEngine.UI.Image>() != null) { b.Background = b.GetComponent<UnityEngine.UI.Image>(); MarkChanged(b); }
                buttons.Add(b);
            }
            foreach (ACO_MissionStartButton b in root.GetComponentsInChildren<ACO_MissionStartButton>(true))
            {
                if (b.Flow != flow) { b.Flow = flow; MarkChanged(b); }
                if (b.Background == null && b.GetComponent<UnityEngine.UI.Image>() != null) { b.Background = b.GetComponent<UnityEngine.UI.Image>(); MarkChanged(b); }
                phaseButtons.Add(b);
            }
            foreach (ACO_Teleporter t in root.GetComponentsInChildren<ACO_Teleporter>(true))
            {
                if (t.Fader != fader) { t.Fader = fader; MarkChanged(t); }
            }
        }
        flow.StatusTexts = status.ToArray();
        flow.BannerTexts = banners.ToArray();
        flow.PhaseTexts = phaseTexts.ToArray();
        flow.PhaseInfoTexts = phaseInfos.ToArray();
        flow.TerrainTexts = terrainTexts.ToArray();
        // 출격 카운트다운 판: 상태 글자·배너가 있는 패널(캔버스)마다 하나 — 없으면 만든다
        var overlays = new List<GameObject>();
        var overlayTexts = new List<TMPro.TextMeshProUGUI>();
        var panelTexts = new List<TMPro.TextMeshProUGUI>(status);
        panelTexts.AddRange(banners);
        foreach (TMPro.TextMeshProUGUI t in panelTexts)
        {
            Canvas c = t.canvas != null ? t.canvas.rootCanvas : t.GetComponentInParent<Canvas>(true);
            RectTransform canvasRt = c != null ? (RectTransform)c.transform : t.transform.parent as RectTransform;
            if (canvasRt == null) { continue; }
            GameObject ov = ACO_HUDBuilder.EnsureFlowCountdown(canvasRt);
            if (ov == null || overlays.Contains(ov)) { continue; }
            overlays.Add(ov);
            overlayTexts.Add(ov.GetComponentInChildren<TMPro.TextMeshProUGUI>(true));
        }
        flow.CountdownOverlays = overlays.ToArray();
        flow.CountdownTexts = overlayTexts.ToArray();
        // 격납고·상점: 출격 카운트다운 중 무장 변경·구매를 막는다
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Loadout l in root.GetComponentsInChildren<ACO_Loadout>(true)) { if (l.Flow != flow) { l.Flow = flow; MarkChanged(l); } }
            foreach (ACO_Shop sh in root.GetComponentsInChildren<ACO_Shop>(true)) { if (sh.Flow != flow) { sh.Flow = flow; MarkChanged(sh); } }
        }
        flow.Buttons = buttons.ToArray();
        flow.PhaseButtons = phaseButtons.ToArray();
        MarkChanged(flow);
        Debug.Log($"[ACO] 게임 진행 '{flow.name}'를 연결했습니다(기체 {slots}, 상태 글자 {status.Count}, 카운트다운 판 {overlays.Count}, 진행 버튼 {buttons.Count}, 페이즈 버튼 {phaseButtons.Count}).", flow);
    }

    /// 진행도(재화·강화·해금, 씬에 하나)를 격납고·상점·작전판·기체 강화 확장에 연결한다.
    /// 기체 강화 확장(ACO_VehicleUpgrades): 기체의 ACO_DFUNC_Missile 전부(받을 강화는 무장 인스펙터의 UpgradeFamily).
    /// SaccEntity.ExtensionUdonBehaviours에 없으면 넣는다(그래야 탑승/폭발 이벤트를 받는다).
    // 예전(23항목) 강화 저장 ID 순서. 재장전 속도(27항목)를 넣기 전의 씬 값을 옮길 때만 쓴다.
    private static readonly string[] OldUpgradeKeys23 = {
        "hull", "mobility", "engine", "fuel", "stability",
        "msl_damage", "msl_ammo", "msl_turn", "msl_lock_speed", "msl_lock_range", "msl_lock_angle",
        "aam_damage", "aam_ammo", "aam_turn", "aam_lock_speed", "aam_lock_range", "aam_lock_angle",
        "agm_damage", "agm_ammo", "agm_blast",
        "bomb_damage", "bomb_ammo", "bomb_blast" };

    /// 강화 항목이 늘면 씬의 진행도에 저장된 이름·배수·가격 배열 길이가 맞지 않는다(항목 번호가 탭 순서로 이어져 자리도 밀린다).
    /// 새 기본값 위에 예전 값을 저장 ID가 같은 자리로 옮긴다(직접 바꾼 이름·배수·가격 유지). 예전 배치를 모르면 기본값으로 되돌린다.
    private static void MigrateUpgradeArrays(ACO_PlayerProgress progress)
    {
        int n = ACO_PlayerProgress.UPGRADE_COUNT;
        if (progress.UpgradeNames != null && progress.UpgradeNames.Length == n && progress.UpgradeSteps != null && progress.UpgradeSteps.Length == n
            && progress.UpgradeBaseCosts != null && progress.UpgradeBaseCosts.Length == n) { return; }

        var temp = new GameObject("ACO_ProgressDefaults");
        ACO_PlayerProgress defaults = temp.AddComponent<ACO_PlayerProgress>();
        var keyField = typeof(ACO_PlayerProgress).GetField("_upgradeKeys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        string[] newKeys = keyField != null ? keyField.GetValue(defaults) as string[] : null;
        string[] names = (string[])defaults.UpgradeNames.Clone();
        float[] steps = (float[])defaults.UpgradeSteps.Clone();
        int[] costs = (int[])defaults.UpgradeBaseCosts.Clone();
        Object.DestroyImmediate(temp);

        bool mapped = newKeys != null && newKeys.Length == n;
        int kept = 0;
        for (int i = 0; mapped && i < n; i++)
        {
            int old = System.Array.IndexOf(OldUpgradeKeys23, newKeys[i]);
            if (old < 0) { continue; }
            if (progress.UpgradeNames != null && progress.UpgradeNames.Length == OldUpgradeKeys23.Length) { names[i] = progress.UpgradeNames[old]; kept++; }
            if (progress.UpgradeSteps != null && progress.UpgradeSteps.Length == OldUpgradeKeys23.Length) { steps[i] = progress.UpgradeSteps[old]; }
            if (progress.UpgradeBaseCosts != null && progress.UpgradeBaseCosts.Length == OldUpgradeKeys23.Length) { costs[i] = progress.UpgradeBaseCosts[old]; }
        }
        Undo.RecordObject(progress, "ACO upgrade migrate");
        progress.UpgradeNames = names;
        progress.UpgradeSteps = steps;
        progress.UpgradeBaseCosts = costs;
        MarkChanged(progress);
        Debug.Log(kept > 0
            ? $"[ACO] 진행도 '{progress.name}': 강화 항목이 {n}개로 늘어 이름·배수·가격 배열을 새 배치로 옮겼습니다(기존 값 유지, 새 항목은 기본값)."
            : $"[ACO] 진행도 '{progress.name}': 강화 배열 길이가 맞지 않아 기본값({n}항목)으로 되돌렸습니다.", progress);
    }

    private static void BakePlayerProgress()
    {
        // 예전 이름 "LOCK CONE" → "SEEKER CONE"(강화가 락온 각도가 아니라 미사일 시커 범위를 키운다)
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_PlayerProgress pp in root.GetComponentsInChildren<ACO_PlayerProgress>(true))
            {
                if (pp.UpgradeNames == null) { continue; }
                bool renamed = false;
                for (int k = 0; k < pp.UpgradeNames.Length; k++) { if (pp.UpgradeNames[k] == "LOCK CONE") { pp.UpgradeNames[k] = "SEEKER CONE"; renamed = true; } }
                if (renamed) { MarkChanged(pp); }
            }
        }
        var progresses = new List<ACO_PlayerProgress>();
        var upgrades = new List<ACO_VehicleUpgrades>();
        ACO_MissionManager manager = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            progresses.AddRange(root.GetComponentsInChildren<ACO_PlayerProgress>(true));
            upgrades.AddRange(root.GetComponentsInChildren<ACO_VehicleUpgrades>(true));
            if (manager == null) { manager = root.GetComponentInChildren<ACO_MissionManager>(true); }
        }
        ACO_PlayerProgress progress = progresses.Count > 0 ? progresses[0] : null;
        if (progresses.Count > 1)
        {
            Debug.LogWarning($"[ACO] 진행도(ACO_PlayerProgress)가 {progresses.Count}개 있습니다. '{progress.name}' 하나만 연결합니다 — 나머지는 지우세요(같은 저장 값을 서로 덮어씁니다).", progress);
        }
        if (progress == null)
        {
            if (upgrades.Count > 0) { Debug.LogWarning("[ACO] 기체 강화 확장이 있지만 씬에 진행도가 없습니다 — ACO → Create Player Progress로 만드세요(없으면 강화·보상이 꺼집니다)."); }
            return;
        }
        if (progress.Manager == null && manager != null)
        {
            progress.Manager = manager;
            MarkChanged(progress);
        }
        MigrateUpgradeArrays(progress);
        if (progress.SafeZones == null || progress.SafeZones.Length == 0)
        {
            Debug.LogWarning($"[ACO] 진행도 '{progress.name}': 안전지대(SafeZones)가 비어 있어 어디서 내려도 격추 페널티가 없습니다 — 항모 갑판 등을 덮는 콜라이더를 넣으세요.", progress);
        }

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Loadout loadout in root.GetComponentsInChildren<ACO_Loadout>(true))
            {
                if (loadout.Progress == progress) { continue; }
                loadout.Progress = progress;
                MarkChanged(loadout);
            }
            foreach (ACO_Shop shop in root.GetComponentsInChildren<ACO_Shop>(true))
            {
                if (shop.Progress == progress) { continue; }
                shop.Progress = progress;
                MarkChanged(shop);
            }
            foreach (ACO_MissionLog log in root.GetComponentsInChildren<ACO_MissionLog>(true))
            {
                if (log.Progress == progress) { continue; }
                log.Progress = progress;
                MarkChanged(log);
            }
        }

        foreach (ACO_VehicleUpgrades up in upgrades)
        {
            SaccFlightAndVehicles.SaccEntity entity = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            if (entity == null)
            {
                Debug.LogWarning($"[ACO] 기체 강화 확장 '{up.name}'이 SAV 기체 안에 있지 않습니다.", up);
                continue;
            }
            up.Progress = progress;
            up.AirVehicle = entity.GetComponentInChildren<SaccFlightAndVehicles.SaccAirVehicle>(true);

            // 무장마다 받을 강화는 무장 인스펙터의 UpgradeFamily(드롭다운)가 정한다.
            var weapons = new List<ACO_DFUNC_Missile>(entity.GetComponentsInChildren<ACO_DFUNC_Missile>(true));
            foreach (ACO_DFUNC_Missile m in weapons)
            {
                bool guided = m.Missile == null || m.Missile.GetComponent<ACO_Ordnance>() == null;
                if (m.UpgradeFamily == ACO_UpgradeFamily.SpecialAGM && guided && !m.MissileSplash)
                {
                    Debug.LogWarning($"[ACO] '{entity.name}/{m.name}': 강화 계열이 SpecialAGM인데 MissileSplash가 꺼져 있어 범위 피해(폭발 반경 강화)가 없습니다.", m);
                }
            }
            up.Weapons = weapons.ToArray();
            up.Flares = entity.GetComponentsInChildren<SaccFlightAndVehicles.DFUNC_Flares>(true);   // 대응탄 포드 소모품
            MarkChanged(up);
            ACO_PlayerMissileSetup.EnsureSeatRecenter(entity);

            if (ACO_PlayerMissileSetup.RegisterExtension(entity, up))
            {
                Debug.Log($"[ACO] '{entity.name}': 강화 확장을 ExtensionUdonBehaviours에 넣었습니다.", entity);
            }
            if (up.AirVehicle == null) { Debug.LogWarning($"[ACO] '{entity.name}': SaccAirVehicle을 찾지 못해 기체 강화(체력·기동·추력)가 꺼집니다.", up); }
        }

        // 강화 확장이 없는 플레이어 기체(ACO 미사일이 있는 기체)를 알려 준다.
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (SaccFlightAndVehicles.SaccEntity entity in root.GetComponentsInChildren<SaccFlightAndVehicles.SaccEntity>(true))
            {
                if (entity.GetComponentInChildren<ACO_DFUNC_Missile>(true) == null) { continue; }
                if (entity.GetComponentInChildren<ACO_VehicleUpgrades>(true) != null) { continue; }
                Debug.LogWarning($"[ACO] '{entity.name}'에 강화 확장이 없습니다 — 강화가 적용되지 않고 격추가 보상에 반영되지 않습니다. "
                    + "ACO → Player Missile → Add Upgrade Extension to Selected Vehicles를 실행하세요.", entity);
            }
        }
        Debug.Log($"[ACO] 진행도 '{progress.name}'를 연결했습니다(기체 강화 확장 {upgrades.Count}개).", progress);
    }

    private static void MarkChanged(Object o)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        EditorUtility.SetDirty(o);
    }

    /// 플레이어 미사일(ACO_DFUNC_Missile)의 표적 = TargetLayers에 있는 씬 오브젝트(락온 마커). SAV가 AAMTargets를 굽는 규칙과 같고,
    /// 자기 기체 안의 오브젝트는 뺀다. 풀에서 꺼져 있는 NPC도 포함해야 하므로 비활성까지 찾는다.
    private static void BakePlayerMissiles()
    {
        var weapons = new List<ACO_DFUNC_Missile>();
        var all = new List<Transform>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            weapons.AddRange(root.GetComponentsInChildren<ACO_DFUNC_Missile>(true));
            all.AddRange(root.GetComponentsInChildren<Transform>(true));
        }

        foreach (ACO_DFUNC_Missile weapon in weapons)
        {
            // 특수무장 슬롯 안의 모듈은 락온 진행 표시가 십자다.
            bool special = weapon.GetComponentInParent<ACO_DFUNC_Special>(true) != null;
            if (weapon.CrossLockIndicator != special) { weapon.CrossLockIndicator = special; }
            int mask = weapon.TargetLayers;
            SaccFlightAndVehicles.SaccEntity own = weapon.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            var targets = new List<GameObject>();
            foreach (Transform t in all)
            {
                if (((1 << t.gameObject.layer) & mask) == 0) { continue; }
                if (own != null && t.IsChildOf(own.transform)) { continue; }
                targets.Add(t.gameObject);
            }
            weapon.Targets = targets.ToArray();
            // SAV 키보드 단축키가 바꿔 낀 옛 기능(DFUNC_AAM, 특수무장 자리의 옛 기능)을 가리키면 새 기능으로 옮긴다.
            if (own != null) { ACO_PlayerMissileSetup.FixKeyboardBindings(own); }
            PrefabUtility.RecordPrefabInstancePropertyModifications(weapon);
            EditorUtility.SetDirty(weapon);
            Debug.Log($"[ACO] 플레이어 미사일 '{weapon.name}'({(own != null ? own.name : "기체 없음")}): 표적 {targets.Count}개를 베이크했습니다.", weapon);
        }

        // 특수무장 슬롯 ↔ 격납고(씬의 첫 번째 것)
        ACO_Loadout loadout = null;
        var specials = new List<ACO_DFUNC_Special>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (loadout == null) { loadout = root.GetComponentInChildren<ACO_Loadout>(true); }
            specials.AddRange(root.GetComponentsInChildren<ACO_DFUNC_Special>(true));
        }
        foreach (ACO_DFUNC_Special special in specials)
        {
            if (special.Loadout == loadout) { continue; }
            special.Loadout = loadout;
            PrefabUtility.RecordPrefabInstancePropertyModifications(special);
            EditorUtility.SetDirty(special);
        }
        if (specials.Count > 0)
        {
            Debug.Log(loadout != null
                ? $"[ACO] 특수무장 슬롯 {specials.Count}개를 격납고 '{loadout.name}'에 연결했습니다."
                : $"[ACO] 특수무장 슬롯 {specials.Count}개가 있지만 씬에 격납고(ACO_Hangar)가 없습니다 — 0번 무장만 씁니다.");
        }

        // 표적 마커가 락온 상태(깜빡임·이름표·마름모)를 그릴 수 있게 무기 목록과 락온 표시를 연결한다.
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_TargetMarkers markers in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
            {
                markers.Weapons = weapons.ToArray();
                if (ACO_HUDBuilder.EnsureLockVisuals(markers)) { Debug.Log($"[ACO] 표적 마커 '{markers.name}'에 락온 표시(마름모·머티리얼)를 만들었습니다.", markers); }
                if (ACO_HUDBuilder.EnsureCrosshair(markers)) { Debug.Log($"[ACO] 표적 마커 '{markers.name}'에 크로스헤어를 만들었습니다.", markers); }
                if (ACO_HUDBuilder.EnsureRwr(markers)) { Debug.Log($"[ACO] 표적 마커 '{markers.name}'에 RWR(위협 링·경보음)을 만들었습니다.", markers); }
                PrefabUtility.RecordPrefabInstancePropertyModifications(markers);
                EditorUtility.SetDirty(markers);
            }
        }
    }

    /// 미션 생성기의 유닛 풀 = UnitPoolRoot(비면 생성기 자신) 아래의 NPC. 비활성 유닛도 포함한다.
    private static void BakeMissionGenerators()
    {
        var generators = new List<ACO_MissionGenerator>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            generators.AddRange(root.GetComponentsInChildren<ACO_MissionGenerator>(true));
        }

        foreach (ACO_MissionGenerator generator in generators)
        {
            Transform poolRoot = generator.UnitPoolRoot != null ? generator.UnitPoolRoot : generator.transform;

            generator.Fighters = poolRoot.GetComponentsInChildren<ACO_NPCFighterAI>(true);

            // 함선 부위(ACO_Warship 아래의 SAM/대공포)는 함선이 데리고 다니므로 지상 풀에 넣지 않는다.
            var ground = new List<UdonSharp.UdonSharpBehaviour>();
            foreach (ACO_SAMSite sam in poolRoot.GetComponentsInChildren<ACO_SAMSite>(true))
            {
                if (sam.GetComponentInParent<ACO_Warship>(true) == null) { ground.Add(sam); }
            }
            foreach (ACO_AAGunSite aa in poolRoot.GetComponentsInChildren<ACO_AAGunSite>(true))
            {
                // 전차의 포탑도 전차가 데리고 다닌다(전차 자신이 지상 풀에 들어간다).
                if (aa.GetComponentInParent<ACO_Warship>(true) == null && aa.GetComponentInParent<ACO_Tank>(true) == null && aa.GetComponentInParent<ACO_EscortUnit>(true) == null) { ground.Add(aa); }
            }
            ground.AddRange(poolRoot.GetComponentsInChildren<ACO_Tank>(true));
            // 함선: HighValue(전함)는 고가치 표적 풀, 아니면(구축함) 지상 풀 — 생성기가 둘 다 바다에 놓는다.
            var hvts = new List<UdonSharp.UdonSharpBehaviour>();
            hvts.AddRange(poolRoot.GetComponentsInChildren<ACO_StaticTarget>(true));
            foreach (ACO_Warship ship in poolRoot.GetComponentsInChildren<ACO_Warship>(true))
            {
                if (ship.HighValue) { hvts.Add(ship); }
                else { ground.Add(ship); }
            }
            generator.GroundUnits = ground.ToArray();
            generator.HighValueTargets = hvts.ToArray();
            generator.EscortUnits = poolRoot.GetComponentsInChildren<ACO_EscortUnit>(true);
            // 씬에 육각 지형이 있으면 연결한다(미션마다 새 씨앗으로 다시 만든다). 칸 크기는 기둥 프리팹 메시와 같게 맞춘다.
            foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (ACO_HexTerrain hex in r.GetComponentsInChildren<ACO_HexTerrain>(true))
                {
                    WarnHexMeshMismatch(hex);
                    // 프리셋이 늘었다: 예전(짧은) 비율 배열이면 새 칸을 기본값으로 늘린다(안 늘리면 새 프리셋이 안 나온다)
                    float[] defW = { 1f, 1f, 1f, 0.5f, 1f, 1f, 1f, 1f, 1f, 1f, 0.7f, 1f };
                    if (hex.PresetWeights == null || hex.PresetWeights.Length < defW.Length)
                    {
                        var w = new float[defW.Length];
                        for (int k = 0; k < w.Length; k++) { w[k] = hex.PresetWeights != null && k < hex.PresetWeights.Length ? hex.PresetWeights[k] : defW[k]; }
                        hex.PresetWeights = w;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(hex);
                        EditorUtility.SetDirty(hex);
                        Debug.Log("[ACO] 육각 지형 프리셋 비율에 새 지형(해안·화산섬·암초 군도·분지)을 넣었습니다.", hex);
                    }
                    if (Mathf.Approximately(hex.HexRadius, ACO_PrefabBuilder.HexRadius)) { continue; }
                    hex.HexRadius = ACO_PrefabBuilder.HexRadius;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(hex);
                    EditorUtility.SetDirty(hex);
                    Debug.Log($"[ACO] 육각 지형 '{hex.name}'의 HexRadius를 기둥 프리팹에 맞춰 {hex.HexRadius}m로 바꿨습니다.", hex);
                }
            }
            if (generator.HexTerrain == null)
            {
                foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    ACO_HexTerrain hex = r.GetComponentInChildren<ACO_HexTerrain>(true);
                    if (hex != null) { generator.HexTerrain = hex; break; }
                }
            }
            // 지형 장식: 지형/생성기와 서로 연결하고 비어 있는 프리팹 칸을 빌더 프리팹으로 채운다.
            ACO_TerrainFeatures features = FindFeatures();
            if (features != null)
            {
                LinkFeatures(features, generator.HexTerrain);
                if (generator.Features == null) { generator.Features = features; }
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
            EditorUtility.SetDirty(generator);

            Debug.Log($"[ACO] 미션 생성기 '{generator.name}': 전투기 {generator.Fighters.Length}기, 지상 유닛 {generator.GroundUnits.Length}대, " +
                      $"고가치 표적 {generator.HighValueTargets.Length}개, 호위 대상 {generator.EscortUnits.Length}기를 풀로 수집했습니다.", generator);
        }

        // 미션 관리자: 생성기가 비어 있으면 씬의 (첫 번째) 생성기를 연결한다.
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissionManager manager in root.GetComponentsInChildren<ACO_MissionManager>(true))
            {
                if (manager.Generator != null || generators.Count == 0) { continue; }
                manager.Generator = generators[0];
                PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
                EditorUtility.SetDirty(manager);
                Debug.Log($"[ACO] 미션 관리자 '{manager.name}'에 미션 생성기 '{generators[0].name}'를 연결했습니다.", manager);
            }
        }

        // HUD: 비어 있으면 씬의 (첫 번째) 생성기/관리자를 연결한다.
        ACO_MissionManager firstManager = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_MissionManager found = root.GetComponentInChildren<ACO_MissionManager>(true);
            if (found != null) { firstManager = found; break; }
        }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_TargetMarkers markers in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
            {
                if (ACO_HUDBuilder.FillMarkerMaterials(markers))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(markers);
                    EditorUtility.SetDirty(markers);
                    Debug.Log($"[ACO] 표적 마커 '{markers.name}'에 아군/경유점/목적지 머티리얼을 채웠습니다.", markers);
                }
                if (markers.Generator != null || generators.Count == 0) { continue; }
                markers.Generator = generators[0];
                PrefabUtility.RecordPrefabInstancePropertyModifications(markers);
                EditorUtility.SetDirty(markers);
            }
            foreach (ACO_ScoreHUD hud in root.GetComponentsInChildren<ACO_ScoreHUD>(true))
            {
                if (ACO_HUDBuilder.EnsureNoticeText(hud)) { Debug.Log($"[ACO] 점수 HUD '{hud.name}'에 안내 글자를 만들었습니다.", hud); }
                if (ACO_HUDBuilder.EnsureResultGroup(hud)) { Debug.Log($"[ACO] 점수 HUD '{hud.name}'에 결과·안내 묶음(ResultGroup)을 만들었습니다 — 두 글자는 실행 중 묶음으로 옮겨집니다.", hud); }
                if (hud.Manager != null || firstManager == null) { continue; }
                hud.Manager = firstManager;
                PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
                EditorUtility.SetDirty(hud);
            }
            foreach (ACO_TargetMarkers markers in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
            {
                if (markers.Manager != null || firstManager == null) { continue; }
                markers.Manager = firstManager;
                PrefabUtility.RecordPrefabInstancePropertyModifications(markers);
                EditorUtility.SetDirty(markers);
            }
            foreach (ACO_MissionStartButton button in root.GetComponentsInChildren<ACO_MissionStartButton>(true))
            {
                if (button.Manager != null || firstManager == null) { continue; }
                button.Manager = firstManager;
                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
                EditorUtility.SetDirty(button);
            }
            foreach (ACO_MissionLog log in root.GetComponentsInChildren<ACO_MissionLog>(true))
            {
                bool changed = false;
                if (log.Manager == null && firstManager != null) { log.Manager = firstManager; changed = true; }
                if (log.Generator == null)
                {
                    // 관리자가 쓰는 생성기와 맞춘다(점수 계산이 같은 풀을 봐야 한다).
                    ACO_MissionGenerator gen = log.Manager != null && log.Manager.Generator != null ? log.Manager.Generator
                        : (generators.Count > 0 ? generators[0] : null);
                    if (gen != null) { log.Generator = gen; changed = true; }
                }
                if (!changed) { continue; }
                PrefabUtility.RecordPrefabInstancePropertyModifications(log);
                EditorUtility.SetDirty(log);
            }
        }
    }

    [MenuItem("ACO/Create Mission Manager")]
    public static void CreateMissionManager()
    {
        var go = new GameObject("ACO_MissionManager");
        Undo.RegisterCreatedObjectUndo(go, "Create Mission Manager");
        go.AddUdonSharpComponent<ACO_MissionManager>();
        Selection.activeGameObject = go;
        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    [MenuItem("ACO/Create Mission Generator")]
    public static void CreateMissionGenerator()
    {
        var go = new GameObject("ACO_MissionGenerator");
        Undo.RegisterCreatedObjectUndo(go, "Create Mission Generator");
        go.AddUdonSharpComponent<ACO_MissionGenerator>();
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 미션 생성기를 만들었습니다. 미션에 쓸 NPC를 이 오브젝트 아래로 옮긴 뒤 ACO → Bake NPC References를 실행하세요.", go);
    }

    /// 기둥 프리팹 메시 크기(윗면 반지름 = 메시 X 범위 / 2.866 — 빌더 기둥은 경사 폭 = 2 x 윗면 반지름)가 지형 칸과 다르면 알린다.
    private static void WarnHexMeshMismatch(ACO_HexTerrain hex)
    {
        if (hex.ColumnPrefab == null) { Debug.LogWarning($"[ACO] 육각 지형 '{hex.name}'에 기둥 프리팹(ColumnPrefab)이 없습니다.", hex); return; }
        MeshFilter mf = hex.ColumnPrefab.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { return; }
        float top = mf.sharedMesh.bounds.extents.x / 2.866f;
        if (Mathf.Abs(top - ACO_PrefabBuilder.HexRadius) > 1f)
        {
            Debug.LogWarning($"[ACO] 기둥 프리팹 '{hex.ColumnPrefab.name}'의 메시 윗면이 {top:0}m인데 칸은 {ACO_PrefabBuilder.HexRadius}m입니다 — " +
                             "ACO → Create Prefabs...에서 '육각 기둥 지형 조각'을 다시 만드세요.", hex.ColumnPrefab);
        }
    }

    private static ACO_TerrainFeatures FindFeatures()
    {
        foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_TerrainFeatures f = r.GetComponentInChildren<ACO_TerrainFeatures>(true);
            if (f != null) { return f; }
        }
        return null;
    }

    /// 지형 장식 ↔ 육각 지형 연결 + 빈 프리팹 칸 채우기(빌더 프리팹이 있으면).
    private static void LinkFeatures(ACO_TerrainFeatures features, ACO_HexTerrain hex)
    {
        if (features.Terrain == null && hex != null) { features.Terrain = hex; }
        if (features.Terrain != null && features.Terrain.Features == null)
        {
            features.Terrain.Features = features;
            PrefabUtility.RecordPrefabInstancePropertyModifications(features.Terrain);
            EditorUtility.SetDirty(features.Terrain);
        }
        // 비었거나 지워진 프리팹(Missing)이 섞여 있으면 기본(상자판) 장식으로 다시 채운다.
        if (HasMissing(features.CityBlockPrefabs)) { features.CityBlockPrefabs = LoadAll(ACO_DecoBuilder.CityNames); }
        if (HasMissing(features.VillagePrefabs)) { features.VillagePrefabs = LoadAll(ACO_DecoBuilder.VillageNames); }
        if (features.PortPrefab == null) { features.PortPrefab = ACO_DecoBuilder.Load(ACO_DecoBuilder.PortName); }
        if (features.AirfieldPrefab == null) { features.AirfieldPrefab = ACO_DecoBuilder.Load(ACO_DecoBuilder.AirfieldName); }
        if (features.LighthousePrefab == null) { features.LighthousePrefab = ACO_DecoBuilder.Load(ACO_DecoBuilder.LighthouseName); }
        if (features.RadarPrefab == null) { features.RadarPrefab = ACO_DecoBuilder.Load(ACO_DecoBuilder.RadarName); }
        if (features.CommTowerPrefab == null) { features.CommTowerPrefab = ACO_DecoBuilder.Load(ACO_DecoBuilder.TowerName); }
        if (features.RuinsPrefab == null) { features.RuinsPrefab = ACO_DecoBuilder.Load(ACO_DecoBuilder.RuinsName); }
        PrefabUtility.RecordPrefabInstancePropertyModifications(features);
        EditorUtility.SetDirty(features);
        if (features.Terrain == null) { Debug.LogWarning("[ACO] 지형 장식에 연결할 육각 지형(ACO_HexTerrain)이 없습니다 — 장식이 놓이지 않습니다.", features); }
    }

    private static bool HasMissing(GameObject[] list)
    {
        if (list == null || list.Length == 0) { return true; }
        foreach (GameObject g in list) { if (g == null) { return true; } }
        return false;
    }

    private static GameObject[] LoadAll(string[] names)
    {
        var list = new List<GameObject>();
        foreach (string n in names)
        {
            GameObject g = ACO_DecoBuilder.Load(n);
            if (g != null) { list.Add(g); }
        }
        return list.ToArray();
    }

    [MenuItem("ACO/Create Terrain Features")]
    public static void CreateTerrainFeatures()
    {
        ACO_TerrainFeatures existing = FindFeatures();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[ACO] 씬에 이미 지형 장식이 있어 그것을 선택했습니다.", existing);
            return;
        }
        // 장식 프리팹이 하나라도 없으면 같이 만든다.
        foreach (string n in ACO_DecoBuilder.AllNames())
        {
            if (ACO_DecoBuilder.Load(n) == null) { ACO_DecoBuilder.BuildAll(); AssetDatabase.SaveAssets(); break; }
        }
        var go = new GameObject("ACO_TerrainFeatures");
        Undo.RegisterCreatedObjectUndo(go, "Create Terrain Features");
        ACO_TerrainFeatures features = go.AddUdonSharpComponent<ACO_TerrainFeatures>();
        ACO_HexTerrain hex = null;
        foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            hex = r.GetComponentInChildren<ACO_HexTerrain>(true);
            if (hex != null) { break; }
        }
        LinkFeatures(features, hex);
        Selection.activeGameObject = go;
        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 지형 장식을 만들었습니다. 육각 지형이 세워질 때마다 도시/마을/비행장/랜드마크를 놓고, 생성기가 그 둘레에 적을 배치합니다.", go);
    }

    [MenuItem("ACO/Create Hex Terrain")]
    public static void CreateHexTerrain()
    {
        var go = new GameObject("ACO_HexTerrain");
        Undo.RegisterCreatedObjectUndo(go, "Create Hex Terrain");
        ACO_HexTerrain hex = go.AddUdonSharpComponent<ACO_HexTerrain>();
        GameObject column = AssetDatabase.LoadAssetAtPath<GameObject>(ACO_PrefabBuilder.PathOf("ACO_HexColumn"));
        if (column == null) { column = ACO_PrefabBuilder.BuildHexColumn(); }
        hex.ColumnPrefab = column;
        hex.HexRadius = ACO_PrefabBuilder.HexRadius;
        // 자연 머티리얼이면 기둥 하나의 머티리얼이 높이·경사로 알아서 칠한다 — 높이대별 교체는 끈다(빈 배열).
        hex.HeightMaterials = ACO_TerrainLook.IsApplied() ? new Material[0] : ACO_PrefabBuilder.HexMaterials();
        Selection.activeGameObject = go;
        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 육각 지형을 만들었습니다. 위치 = 지형 중심(높이 0 = 해수면 기준). 생성기 SeaLevel/작전 공역과 맞추고, 기지는 ExcludeZones에 넣으세요.", go);
    }

    [MenuItem("ACO/Create Player Progress")]
    public static void CreatePlayerProgress()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_PlayerProgress existing = root.GetComponentInChildren<ACO_PlayerProgress>(true);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[ACO] 씬에 이미 진행도가 있어 그것을 선택했습니다.", existing);
                return;
            }
        }

        var go = new GameObject("ACO_PlayerProgress");
        Undo.RegisterCreatedObjectUndo(go, "Create Player Progress");
        go.AddUdonSharpComponent<ACO_PlayerProgress>();
        Selection.activeGameObject = go;

        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 진행도(재화·강화·해금, 플레이어 영구 저장)를 만들었습니다. 플레이어 기체에는 Add Upgrade Extension을 실행하세요.", go);
    }

    [MenuItem("ACO/Create Game Flow")]
    public static void CreateGameFlow()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_GameFlow existing = root.GetComponentInChildren<ACO_GameFlow>(true);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[ACO] 씬에 이미 게임 진행이 있어 그것을 선택했습니다.", existing);
                return;
            }
        }

        var go = new GameObject("ACO_GameFlow");
        Undo.RegisterCreatedObjectUndo(go, "Create Game Flow");
        ACO_GameFlow flow = go.AddUdonSharpComponent<ACO_GameFlow>();
        // 갑판 출격 위치 4개(자리만 — 항모 갑판 위로 옮긴다)
        var decks = new Transform[4];
        for (int i = 0; i < decks.Length; i++)
        {
            var d = new GameObject($"DeckPoint_{i + 1}");
            d.transform.SetParent(go.transform, false);
            d.transform.localPosition = new Vector3(i * 20f, 0f, 0f);
            decks[i] = d.transform;
        }
        flow.DeckPoints = decks;
        var briefing = new GameObject("BriefingPoint");
        briefing.transform.SetParent(go.transform, false);
        briefing.transform.localPosition = new Vector3(0f, 0f, -20f);
        flow.BriefingPoint = briefing.transform;
        var spectate = new GameObject("SpectatePoint");
        spectate.transform.SetParent(go.transform, false);
        spectate.transform.localPosition = new Vector3(0f, 0f, 20f);
        flow.SpectatePoint = spectate.transform;
        Selection.activeGameObject = go;

        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 게임 진행을 만들었습니다. DeckPoint_1~4를 항모 갑판의 출격 위치(기체 루트가 놓일 자리)로, BriefingPoint를 대기실(브리핑·격납고 패널이 있는 방 — 격추·탈출해도 여기로 온다)로, "
            + "SpectatePoint를 갑판의 관전 위치로 옮기고, "
            + "참가자 기체 4대의 원래 위치를 안 보이는 보관 장소로 옮기세요. 기체 순서는 Aircraft 배열(비면 Bake가 채움).", go);
    }

    [MenuItem("ACO/Create Screen Fader")]
    public static void CreateScreenFader()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_ScreenFader existing = root.GetComponentInChildren<ACO_ScreenFader>(true);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[ACO] 씬에 이미 화면 암전이 있어 그것을 선택했습니다.", existing);
                return;
            }
        }

        var go = new GameObject("ACO_ScreenFader");
        Undo.RegisterCreatedObjectUndo(go, "Create Screen Fader");
        ACO_ScreenFader fader = go.AddUdonSharpComponent<ACO_ScreenFader>();
        fader.FadeRenderer = ACO_HUDBuilder.CreateScreenFadeSphere(go.transform);
        Selection.activeGameObject = go;

        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 화면 암전을 만들었습니다. 순간이동 버튼·게임 진행(출격, 관전, 브리핑실 귀환)이 이걸 거칩니다.", go);
    }

    [MenuItem("ACO/Create Tactical Map")]
    public static void CreateTacticalMap()
    {
        GameObject go = ACO_HUDBuilder.CreateTacticalMap();
        Undo.RegisterCreatedObjectUndo(go, "Create Tactical Map");
        Selection.activeGameObject = go;
        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 작전 테이블을 만들었습니다. 브리핑실 가운데로 옮기고 HomeBase에 항모를 넣으세요. 지도 크기는 MapRadius(테이블도 같이 바꾸려면 다시 만든다).", go);
    }

    [MenuItem("ACO/Create HUD Settings Panel")]
    public static void CreateHudSettingsPanel()
    {
        const string path = "Assets/ACO_Addon/Prefabs/ACO_HudSettings.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { ACO_HUDBuilder.CreateHudSettingsPrefab(); }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
        Undo.RegisterCreatedObjectUndo(go, "Create HUD Settings Panel");
        Selection.activeGameObject = go;
        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] HUD 설정 패널을 씬에 놓았습니다. 로비나 격납고로 옮기세요(파란 화살표 +Z가 관객 쪽).", go);
    }

    [MenuItem("ACO/Create Chatter (Subtitles + Voice)")]
    public static void CreateChatter()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_Chatter existing = root.GetComponentInChildren<ACO_Chatter>(true);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[ACO] 씬에 이미 무전 채터가 있어 그것을 선택했습니다.", existing);
                return;
            }
        }
        GameObject go = ACO_HUDBuilder.CreateChatter();
        if (go == null) { return; }
        Undo.RegisterCreatedObjectUndo(go, "Create Chatter");
        Selection.activeGameObject = go;
        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 무전 채터를 만들었습니다(위치 무관 — 머리를 따라온다). 대사·음성·확률·쿨타임은 인스펙터에서 고칩니다.", go);
    }

    [MenuItem("ACO/Create Combat Director")]
    public static void CreateCombatDirector()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_CombatDirector existing = root.GetComponentInChildren<ACO_CombatDirector>(true);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[ACO] 씬에 이미 CombatDirector가 있어 그것을 선택했습니다.", existing);
                return;
            }
        }

        var go = new GameObject("ACO_CombatDirector");
        Undo.RegisterCreatedObjectUndo(go, "Create Combat Director");
        go.AddUdonSharpComponent<ACO_CombatDirector>();
        Selection.activeGameObject = go;

        BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(go.scene);
    }
}
