using System;
using System.Collections.Generic;
using UdonSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using ACO;
using Object = UnityEngine.Object;

/// 셋업 검사 창(메뉴 ACO → Setup Check...). 씬을 훑어 ACO 셋업에서 빠진 것을 목록으로 보여 준다 — 오류(빨강: 이대로면 동작 안 함),
/// 경고(노랑: 일부 기능이 꺼짐), 정보(회색: 선택 기능). 자동으로 고칠 수 있는 항목은 [고치기], 직접 손봐야 하는 항목은 [선택]으로 그 오브젝트를 고른다.
/// 고치기는 기존 메뉴(Create ..., Add Upgrade Extension, 프리팹 다시 만들기 등)를 그대로 부르고, 끝나면 Bake와 다시 검사를 한다.
public class ACO_SetupValidator : EditorWindow
{
    private const int OK = 0;
    private const int INFO = 1;
    private const int WARN = 2;
    private const int ERROR = 3;

    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";

    private class Item
    {
        public string Group;
        public int Severity;
        public string Text;
        public Object Target;
        public Action Fix;
    }

    private readonly List<Item> _items = new List<Item>();
    private Vector2 _scroll;
    private bool _showOk;

    [MenuItem("ACO/Setup Check...", false, 1)]
    public static void Open()
    {
        var w = GetWindow<ACO_SetupValidator>("ACO Setup Check");
        w.minSize = new Vector2(520f, 400f);
        w.Run();
    }

    private void OnFocus() { Run(); }

    // ───────────── 화면 ─────────────

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("다시 검사", EditorStyles.toolbarButton, GUILayout.Width(80f))) { Run(); }
            if (GUILayout.Button("Bake", EditorStyles.toolbarButton, GUILayout.Width(60f))) { ACO_SceneBaker.BakeNeighbors(); Run(); }
            int fixable = 0;
            foreach (Item it in _items) { if (it.Fix != null && it.Severity > OK) { fixable++; } }
            using (new EditorGUI.DisabledScope(fixable == 0))
            {
                if (GUILayout.Button($"자동 수정 {fixable}개 모두", EditorStyles.toolbarButton, GUILayout.Width(130f))) { FixAll(); }
            }
            GUILayout.FlexibleSpace();
            _showOk = GUILayout.Toggle(_showOk, "통과 항목 보기", EditorStyles.toolbarButton);
        }

        int errors = 0, warns = 0;
        foreach (Item it in _items) { if (it.Severity == ERROR) { errors++; } else if (it.Severity == WARN) { warns++; } }
        EditorGUILayout.HelpBox(errors == 0 && warns == 0 ? "필수 셋업이 모두 갖춰졌습니다."
            : $"오류 {errors}개(이대로면 동작 안 함), 경고 {warns}개(일부 기능이 꺼짐).", errors > 0 ? MessageType.Error : (warns > 0 ? MessageType.Warning : MessageType.Info));

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        string group = null;
        foreach (Item it in _items)
        {
            if (it.Severity == OK && !_showOk) { continue; }
            if (it.Group != group)
            {
                group = it.Group;
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(group, EditorStyles.boldLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Icon(it.Severity), GUILayout.Width(20f), GUILayout.Height(18f));
                EditorGUILayout.LabelField(it.Text, EditorStyles.wordWrappedLabel);
                using (new EditorGUI.DisabledScope(it.Target == null))
                {
                    if (GUILayout.Button("선택", GUILayout.Width(44f))) { Selection.activeObject = it.Target; EditorGUIUtility.PingObject(it.Target); }
                }
                using (new EditorGUI.DisabledScope(it.Fix == null || it.Severity == OK))
                {
                    if (GUILayout.Button("고치기", GUILayout.Width(52f))) { ApplyFix(it); GUIUtility.ExitGUI(); }
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static GUIContent Icon(int severity)
    {
        if (severity == ERROR) { return EditorGUIUtility.IconContent("console.erroricon.sml"); }
        if (severity == WARN) { return EditorGUIUtility.IconContent("console.warnicon.sml"); }
        if (severity == INFO) { return EditorGUIUtility.IconContent("console.infoicon.sml"); }
        return EditorGUIUtility.IconContent("TestPassed");
    }

    private void ApplyFix(Item it)
    {
        try { it.Fix(); }
        catch (Exception e) { Debug.LogException(e); }
        ACO_SceneBaker.BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Run();
    }

    private void FixAll()
    {
        // 고치기가 다른 항목을 바꿀 수 있으므로 한 번에 하나씩 하고 다시 검사한다(최대 40번).
        for (int guard = 0; guard < 40; guard++)
        {
            Item next = null;
            foreach (Item it in _items) { if (it.Fix != null && it.Severity > OK) { next = it; break; } }
            if (next == null) { break; }
            Action fix = next.Fix;
            try { fix(); }
            catch (Exception e) { Debug.LogException(e); }
            Run();
            // 같은 항목이 그대로 남으면(고칠 수 없음) 멈춘다.
            bool same = false;
            foreach (Item it in _items) { if (it.Text == next.Text && it.Fix != null && it.Severity > OK) { same = true; break; } }
            if (same) { break; }
        }
        ACO_SceneBaker.BakeNeighbors();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Run();
    }

    // ───────────── 검사 ─────────────

    private void Add(string group, int severity, string text, Object target = null, Action fix = null)
    {
        _items.Add(new Item { Group = group, Severity = severity, Text = text, Target = target, Fix = fix });
    }

    private static List<T> FindAll<T>() where T : Component
    {
        var list = new List<T>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            list.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return list;
    }

    private static T FindOne<T>() where T : Component
    {
        List<T> all = FindAll<T>();
        return all.Count > 0 ? all[0] : null;
    }

    private void Run()
    {
        _items.Clear();
        CheckCore();
        CheckFlow();
        CheckPanels();
        CheckAircraft();
        Repaint();
    }

    private void CheckCore()
    {
        const string g = "핵심";
        Single<ACO_MissionManager>(g, "미션 관리자", ERROR, ACO_SceneBaker.CreateMissionManager);
        ACO_MissionGenerator gen = Single<ACO_MissionGenerator>(g, "미션 생성기", ERROR, ACO_SceneBaker.CreateMissionGenerator);
        if (gen != null)
        {
            int units = (gen.Fighters != null ? gen.Fighters.Length : 0) + (gen.GroundUnits != null ? gen.GroundUnits.Length : 0);
            if (units == 0) { Add(g, WARN, "생성기 풀에 NPC가 없습니다 — 생성기(또는 UnitPoolRoot) 아래에 NPC 프리팹을 두고 Bake하세요.", gen, ACO_SceneBaker.BakeNeighbors); }
            else { Add(g, OK, $"생성기 풀: 전투기 {(gen.Fighters != null ? gen.Fighters.Length : 0)}, 지상 {(gen.GroundUnits != null ? gen.GroundUnits.Length : 0)}", gen); }
            if (gen.HexTerrain == null) { Add(g, INFO, "육각 지형이 없습니다(선택) — 리롤·작전 테이블 지형이 꺼집니다. ACO → Create Hex Terrain.", gen, ACO_SceneBaker.CreateHexTerrain); }
            else if (!ACO_TerrainLook.IsApplied())
            {
                Add(g, INFO, "육각 지형이 예전 높이대 머티리얼(단색)이거나 나무가 없습니다(선택) — [고치기] = 자연 지형 머티리얼(높이·경사·숲·절벽 지층, 육각 경계가 덜 보임) + 나무 적용.", gen.HexTerrain, ACO_TerrainLook.Apply);
            }

            // NPC 전투기 플레어 장치(예전 프리팹은 AI 안에 플레어가 있었다 — 지금은 ACO_FlareDispenser)
            var noFlares = new List<ACO_NPCFighterAI>();
            if (gen.Fighters != null) { foreach (ACO_NPCFighterAI f in gen.Fighters) { if (f != null && f.Flares == null) { noFlares.Add(f); } } }
            if (noFlares.Count > 0)
            {
                Add(g, WARN, $"NPC 전투기 {noFlares.Count}기에 플레어 장치(ACO_FlareDispenser)가 없어 플레어를 못 뿌립니다 — 프리팹을 다시 만들거나 [고치기](장치를 붙이고 'Flare' 파티클을 잇는다).", noFlares[0], () =>
                {
                    foreach (ACO_NPCFighterAI f in noFlares) { AddFlareDispenser(f); }
                });
            }

            // 폭격기 요격 페이즈
            int bombers = 0;
            if (gen.EscortUnits != null) { foreach (ACO_EscortUnit u in gen.EscortUnits) { if (u != null && u.Hostile) { bombers++; } } }
            if (bombers > 0) { Add(g, OK, $"적 폭격기 {bombers}대(요격 페이즈)", gen); }
            else
            {
                ACO_MissionGenerator genB = gen;
                Add(g, INFO, "생성기 풀에 적 폭격기가 없어 요격 페이즈가 나오지 않습니다(선택) — [고치기]는 NPC_Bomber 4대를 풀 아래에 놓는다.", gen, () =>
                {
                    if (!ACO_PrefabBuilder.Exists("NPC_Bomber")) { ACO_PrefabBuilder.BuildBomber(); }
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ACO_PrefabBuilder.PathOf("NPC_Bomber"));
                    if (prefab == null) { return; }
                    Transform parent = genB.UnitPoolRoot != null ? genB.UnitPoolRoot : genB.transform;
                    for (int k = 0; k < 4; k++)
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                        go.transform.localPosition = new Vector3(k * 60f, 0f, 0f);
                        Undo.RegisterCreatedObjectUndo(go, "ACO bomber");
                    }
                    ACO_SceneBaker.BakeNeighbors();
                });
            }

            // 보너스 페이즈(슈퍼에이스)
            ACO_NPCFighterAI ace = null;
            if (gen.Fighters != null) { foreach (ACO_NPCFighterAI f in gen.Fighters) { if (f != null && f.SuperAce != null) { ace = f; break; } } }
            ACO_MissionManager mgr = FindOne<ACO_MissionManager>();
            if (ace != null) { Add(g, OK, $"슈퍼에이스 '{ace.SuperAce.AceName}' — 보너스 페이즈 확률 {(mgr != null ? Mathf.RoundToInt(mgr.BonusPhaseChance * 100f) : 0)}%", ace); }
            else if (mgr != null && mgr.BonusPhaseChance > 0f)
            {
                ACO_MissionGenerator genRef = gen;
                Add(g, INFO, "생성기 풀에 슈퍼에이스가 없어 보너스 페이즈가 나오지 않습니다(선택) — [고치기]는 NPC_SuperAce를 풀 아래에 놓는다.", gen, () =>
                {
                    if (!ACO_PrefabBuilder.Exists("NPC_SuperAce")) { ACO_PrefabBuilder.BuildSuperAce(); }
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ACO_PrefabBuilder.PathOf("NPC_SuperAce"));
                    if (prefab == null) { return; }
                    Transform parent = genRef.UnitPoolRoot != null ? genRef.UnitPoolRoot : genRef.transform;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    Undo.RegisterCreatedObjectUndo(go, "ACO super ace");
                    Selection.activeGameObject = go;
                    ACO_SceneBaker.BakeNeighbors();
                });
            }
        }
        Single<ACO_CombatDirector>(g, "전투 디렉터(공격 정원)", INFO, ACO_SceneBaker.CreateCombatDirector);

        ACO_PlayerProgress progress = Single<ACO_PlayerProgress>(g, "진행도(재화·강화·해금)", ERROR, ACO_SceneBaker.CreatePlayerProgress);
        if (progress != null)
        {
            if (progress.SafeZones == null || progress.SafeZones.Length == 0)
            {
                Add(g, WARN, "진행도 SafeZones가 비어 있습니다 — 어디서 내려도 격추 페널티가 없습니다. 항모 갑판을 덮는 콜라이더를 넣으세요.", progress);
            }
            else { Add(g, OK, $"안전지대 {progress.SafeZones.Length}개", progress); }
        }
        Single<ACO_ScreenFader>(g, "화면 암전", WARN, ACO_SceneBaker.CreateScreenFader);
        Single<ACO_Chatter>(g, "무전 채터", INFO, ACO_SceneBaker.CreateChatter);
    }

    private void CheckFlow()
    {
        const string g = "게임 진행";
        ACO_GameFlow flow = Single<ACO_GameFlow>(g, "게임 진행(명단·준비·출격)", ERROR, ACO_SceneBaker.CreateGameFlow);
        if (flow == null) { return; }

        int slots = flow.Aircraft != null ? flow.Aircraft.Length : 0;
        if (slots == 0) { Add(g, ERROR, "참가자 기체(Aircraft)가 없습니다 — 강화 확장이 있는 기체가 있으면 Bake가 채웁니다.", flow, ACO_SceneBaker.BakeNeighbors); }
        else { Add(g, OK, $"참가자 기체 {slots}대", flow); }

        int decks = 0;
        if (flow.DeckPoints != null) { foreach (Transform t in flow.DeckPoints) { if (t != null) { decks++; } } }
        if (decks < slots) { Add(g, ERROR, $"갑판 출격 위치(DeckPoints) {decks}개 < 기체 {slots}대 — 위치가 없는 칸은 출격할 수 없습니다.", flow, () => AddPoints(flow, slots)); }

        PointCheck(g, flow, flow.BriefingPoint, "BriefingPoint(미션 뒤 귀환할 브리핑실)", "BriefingPoint");
        if (flow.HangarPoint != null) { Add(g, OK, "HangarPoint(격추·탈출 뒤 돌아올 곳 — 따로 둔 격납고)", flow.HangarPoint); }
        else { Add(g, OK, "HangarPoint 없음 — 격추·탈출하면 BriefingPoint(대기실)로 돌아온다", flow); }
        PointCheck(g, flow, flow.SpectatePoint, "SpectatePoint(관전자가 갈 갑판 위치)", "SpectatePoint");

        // 기체 원래 위치 = 보관 장소여야 한다(갑판 위에 있으면 보관이 아니다).
        if (flow.Aircraft != null && flow.DeckPoints != null)
        {
            foreach (SaccEntity e in flow.Aircraft)
            {
                if (e == null) { continue; }
                foreach (Transform d in flow.DeckPoints)
                {
                    if (d == null || Vector3.Distance(e.transform.position, d.position) > 150f) { continue; }
                    Add(g, WARN, $"'{e.name}'의 원래 위치가 갑판 출격 위치 근처입니다 — 기체 원래 위치는 안 보이는 보관 장소(바닥 콜라이더 위)로 옮기세요.", e);
                    break;
                }
            }
        }

        List<ACO_Teleporter> teleporters = FindAll<ACO_Teleporter>();
        if (teleporters.Count == 0) { Add(g, WARN, "장소 이동 버튼(ACO_Teleporter)이 없습니다 — 로비·격납고·항모 사이를 오갈 버튼을 두세요."); }
        foreach (ACO_Teleporter t in teleporters)
        {
            if (t.Target == null) { Add(g, ERROR, $"장소 이동 버튼 '{t.name}'의 Target(도착 지점)이 비어 있습니다.", t); }
        }
        if (teleporters.Count > 0) { Add(g, OK, $"장소 이동 버튼 {teleporters.Count}개"); }
    }

    private void CheckPanels()
    {
        const string g = "패널 / HUD";
        ACO_TargetMarkers markers = FindOne<ACO_TargetMarkers>();
        if (markers == null) { Add(g, ERROR, "HUD(ACO_HUD — 점수 패널·표적 마커)가 씬에 없습니다.", null, () => PlacePrefab("ACO_HUD", ACO_HUDBuilder.CreateHUDPrefab)); }
        else
        {
            Add(g, OK, "HUD", markers);
            if (markers.Crosshair == null) { Add(g, WARN, "HUD에 크로스헤어가 없습니다.", markers, () => ACO_HUDBuilder.EnsureCrosshair(markers)); }
        }

        List<ACO_MissionStartButton> phaseButtons = FindAll<ACO_MissionStartButton>();
        ACO_FlowButton primary = null;
        foreach (ACO_FlowButton b in FindAll<ACO_FlowButton>()) { if (b.Action == ACO_GameFlow.ACTION_PRIMARY) { primary = b; break; } }
        if (phaseButtons.Count == 0 && primary == null) { Add(g, ERROR, "작전 콘솔(ACO_MissionConsole — 상태 배너·작전 카드·명단 칸·JOIN/READY)이 씬에 없습니다.", null, () => PlacePrefab("ACO_MissionConsole", ACO_HUDBuilder.CreateMissionConsolePrefab)); }
        else if (primary == null)
        {
            Add(g, WARN, "작전 콘솔이 옛 버전입니다(페이즈 1~5 버튼 + 글자 명단) — 프리팹을 다시 만들면 상태 배너·작전 카드(페이즈 −/+·지형)·명단 칸 카드·내 행동 버튼으로 바뀝니다(그 뒤 Bake).", phaseButtons[0], () => ACO_HUDBuilder.CreateMissionConsolePrefab());
        }
        else { Add(g, OK, "작전 콘솔", primary); }

        ACO_Loadout hangar = FindOne<ACO_Loadout>();
        if (hangar == null) { Add(g, ERROR, "격납고(ACO_Hangar — 특수무장·강화 상점·READY/LAUNCH)가 씬에 없습니다.", null, () => PlacePrefab("ACO_Hangar", ACO_HUDBuilder.CreateHangarPrefab)); }
        else if (FindOne<ACO_Shop>() == null || !HasFlowButton(ACO_GameFlow.ACTION_TOGGLE_READY) || !HasFlowButton(ACO_GameFlow.ACTION_LAUNCH))
        {
            Add(g, WARN, "격납고가 옛 버전입니다(상점 또는 READY/LAUNCH 없음) — 프리팹을 다시 만들면 씬의 격납고도 바뀝니다.", hangar, () => ACO_HUDBuilder.CreateHangarPrefab());
        }
        else if (FindOne<ACO_Shop>().RowIcons == null || FindOne<ACO_Shop>().RowIcons.Length == 0 || hangar.LockIcons == null || hangar.LockIcons.Length == 0
            || hangar.PagerObjects == null || hangar.PagerObjects.Length == 0 || hangar.EmptyText == null
            || FindOne<ACO_Shop>().RowObjects == null || FindOne<ACO_Shop>().RowObjects.Length < ACO_PlayerProgress.MAX_CATEGORY_LENGTH)
        {
            Add(g, WARN, "격납고가 옛 버전입니다(아이콘·특수무장 페이지 버튼이 없거나 강화 줄이 모자람) — 프리팹을 다시 만들면 아이콘·자물쇠·PREV/NEXT·강화 7줄이 붙습니다.", hangar, () => ACO_HUDBuilder.CreateHangarPrefab());
        }
        else if (FindOne<ACO_Shop>().TabImages == null || FindOne<ACO_Shop>().TabImages.Length <= ACO_Shop.TAB_SUPPLY
            || FindOne<ACO_Shop>().ConsumableIcons == null || FindOne<ACO_Shop>().ConsumableIcons.Length == 0)
        {
            Add(g, WARN, "격납고가 옛 버전입니다(상점에 소모품 SUPPLY 탭이 없음) — 프리팹을 다시 만들면 6번째 탭(장갑판·대응탄 포드·탄약 증량·연료탱크)이 붙습니다.", hangar, () => ACO_HUDBuilder.CreateHangarPrefab());
        }
        else { Add(g, OK, "격납고", hangar); }

        // 특수무장 카탈로그(무장 정의 프리팹 → 격납고 목록 + 기체 모듈)
        int notInstalled = ACO_SpecialWeaponCatalog.CountNotInstalled();
        if (notInstalled > 0)
        {
            Add(g, WARN, $"특수무장 정의 {notInstalled}개가 격납고 목록이나 기체의 특수무장 슬롯에 없습니다 — [고치기]로 설치합니다.", null, ACO_SpecialWeaponCatalog.Install);
        }
        else if (ACO_SpecialWeaponCatalog.LoadDefs().Count > 0) { Add(g, OK, $"특수무장 카탈로그 {ACO_SpecialWeaponCatalog.LoadDefs().Count}종 설치됨", hangar); }
        foreach (ACO_SpecialWeaponDef d in Object.FindObjectsOfType<ACO_SpecialWeaponDef>(true))
        {
            Add(g, ERROR, $"'{d.name}': 특수무장 정의(ACO_SpecialWeaponDef)가 씬에 있습니다 — 일반 스크립트라 VRChat에 올릴 수 없습니다. 프리팹 파일로만 두고 씬에서는 지우세요.", d);
        }

        if (FindOne<ACO_MissionLog>() == null) { Add(g, WARN, "작전판(ACO_MissionBoard — 격추 기록·디브리핑·개인 점수)이 없습니다 — 개인 점수 보상이 0이 됩니다.", null, () => PlacePrefab("ACO_MissionBoard", ACO_HUDBuilder.CreateMissionBoardPrefab)); }

        ACO_TacticalMap map = FindOne<ACO_TacticalMap>();
        if (map == null) { Add(g, INFO, "작전 테이블(3D 미니맵)이 없습니다(선택).", null, ACO_SceneBaker.CreateTacticalMap); }
        else if (map.HomeBase == null) { Add(g, WARN, "작전 테이블의 HomeBase(항모)가 비어 있습니다.", map); }
        else { Add(g, OK, "작전 테이블", map); }

        if (FindOne<ACO_CreditBoard>() == null) { Add(g, INFO, "크레딧 보드가 없습니다(선택) — 사용한 에셋 출처 표기(CC-BY 에셋을 쓰면 필수).", null, () => PlacePrefab("ACO_CreditBoard", ACO_HUDBuilder.CreateCreditBoardPrefab)); }

        ACO_HudSettings hudSettings = FindOne<ACO_HudSettings>();
        if (hudSettings == null) { Add(g, INFO, "HUD 설정 패널이 없습니다(선택).", null, ACO_SceneBaker.CreateHudSettingsPanel); }
        else if (hudSettings.ValueTexts == null || hudSettings.ValueTexts.Length < ACO_HudSettings.ITEM_COUNT)
        {
            Add(g, WARN, $"HUD 설정 패널이 예전 버전입니다(항목 {(hudSettings.ValueTexts != null ? hudSettings.ValueTexts.Length : 0)}/{ACO_HudSettings.ITEM_COUNT} — RWR 요소별 크기·분류 카드 배치 등이 없음). "
                + "[고치기] = 프리팹 다시 만들기(씬의 패널이 프리팹 인스턴스가 아니면 새로 놓으세요).", hudSettings, ACO_HUDBuilder.CreateHudSettingsPrefab);
        }
    }

    private void CheckAircraft()
    {
        const string g = "플레이어 기체";
        var entities = new List<SaccEntity>();
        ACO_GameFlow flow = FindOne<ACO_GameFlow>();
        if (flow != null && flow.Aircraft != null) { foreach (SaccEntity e in flow.Aircraft) { if (e != null && !entities.Contains(e)) { entities.Add(e); } } }
        foreach (SaccEntity e in FindAll<SaccEntity>())
        {
            if (entities.Contains(e)) { continue; }
            if (e.GetComponentInChildren<ACO_DFUNC_Missile>(true) != null || e.GetComponentInChildren<DFUNC_AAM>(true) != null) { entities.Add(e); }
        }
        if (entities.Count == 0) { Add(g, WARN, "플레이어 기체(SAV)가 없습니다."); return; }

        // 프로그램이 빠진 UdonBehaviour(기체 모델에 딸려 왔지만 스크립트가 없는 것 등): 아무 일도 안 하지만,
        // 다른 스크립트가 GetComponent(s)InChildren<U# 타입>으로 훑다 닿으면 예외로 그 스크립트가 멈춘다(로스터 JOIN 거절 등).
        var broken = new List<VRC.Udon.UdonBehaviour>();
        foreach (VRC.Udon.UdonBehaviour ub in FindAll<VRC.Udon.UdonBehaviour>()) { if (ub.programSource == null) { broken.Add(ub); } }
        if (broken.Count > 0)
        {
            Add(g, WARN, $"프로그램이 빠진 UdonBehaviour가 {broken.Count}개 있습니다(예: '{broken[0].name}') — 아무 일도 안 하지만 다른 스크립트를 멈추게 할 수 있습니다. [고치기] = 그 컴포넌트만 제거.", broken[0], () =>
            {
                foreach (VRC.Udon.UdonBehaviour ub in broken)
                {
                    if (ub == null) { continue; }
                    GameObject go = ub.gameObject;
                    Undo.DestroyObjectImmediate(ub);
                    // 지워진 U# 스크립트(예: 예전 페이즈 음성)는 스크립트가 빠진 컴포넌트도 함께 남는다 — 같이 지운다
                    Undo.RegisterCompleteObjectUndo(go, "ACO remove missing scripts");
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                }
            });
        }

        foreach (SaccEntity e in entities)
        {
            string n = e.name;
            bool hasAco = e.GetComponentInChildren<ACO_DFUNC_Missile>(true) != null;
            if (!hasAco)
            {
                bool hasAam = e.GetComponentInChildren<DFUNC_AAM>(true) != null;
                Add(g, hasAam ? WARN : INFO, $"'{n}': ACO 미사일이 없습니다" + (hasAam ? " — DFUNC_AAM을 바꾸세요(강화·락온 HUD·슬롯 장전)." : "."), e,
                    hasAam ? (Action)(() => { ACO_SpecialWeaponCatalog.EnsureExamples(); ACO_PlayerMissileSetup.SetupVehicle(e); ACO_SpecialWeaponCatalog.SyncLoadouts(); ACO_SceneBaker.BakeNeighbors(); }) : null);
            }
            if (e.GetComponentInChildren<ACO_DFUNC_Special>(true) == null)
            {
                Add(g, INFO, $"'{n}': 특수무장 슬롯이 없습니다(선택) — [고치기]는 기체 셋업을 한 번에 한다.", e,
                    () => { ACO_SpecialWeaponCatalog.EnsureExamples(); ACO_PlayerMissileSetup.SetupVehicle(e); ACO_SpecialWeaponCatalog.SyncLoadouts(); ACO_SceneBaker.BakeNeighbors(); });
            }

            int staleKeys = ACO_PlayerMissileSetup.CountStaleKeyboardBindings(e);
            if (staleKeys > 0)
            {
                Add(g, WARN, $"'{n}': 키보드 단축키 {staleKeys}개가 꺼진 옛 기능(DFUNC_AAM·특수무장 자리의 옛 기능)을 가리킵니다 — 데스크톱에서 키로 무기를 못 고릅니다.", e,
                    () => ACO_PlayerMissileSetup.FixKeyboardBindings(e));
            }

            ACO_VehicleUpgrades up = e.GetComponentInChildren<ACO_VehicleUpgrades>(true);
            if (up == null)
            {
                Add(g, ERROR, $"'{n}': 강화 확장이 없습니다 — 강화가 적용되지 않고 출격·격추가 보상에 반영되지 않습니다.", e, () => ACO_PlayerMissileSetup.EnsureUpgradeExtension(e));
            }
            else
            {
                bool registered = false;
                if (e.ExtensionUdonBehaviours != null) { foreach (UdonSharpBehaviour x in e.ExtensionUdonBehaviours) { if (x == up) { registered = true; break; } } }
                if (!registered) { Add(g, ERROR, $"'{n}': 강화 확장이 ExtensionUdonBehaviours에 없습니다(탑승·폭발 이벤트를 못 받음).", e, () => ACO_PlayerMissileSetup.RegisterExtension(e, up)); }
                else { Add(g, OK, $"'{n}': 강화 확장", up); }
            }

            ACO_SeatRecenter rc = e.GetComponentInChildren<ACO_SeatRecenter>(true);
            bool rcRegistered = false;
            if (rc != null && e.ExtensionUdonBehaviours != null) { foreach (UdonSharpBehaviour x in e.ExtensionUdonBehaviours) { if (x == rc) { rcRegistered = true; break; } } }
            if (rc == null || !rcRegistered || !e.DoubleTapToExit)
            {
                Add(g, WARN, $"'{n}': 좌석 재조정(VR 점프 한 번)·더블탭 내리기가 설정되지 않았습니다.", e, () => ACO_PlayerMissileSetup.EnsureSeatRecenter(e));
            }
            else { Add(g, OK, $"'{n}': 좌석 재조정 / 더블탭 내리기", rc); }

            foreach (ACO_DFUNC_Missile m in e.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                bool ordnance = m.Missile != null && m.Missile.GetComponent<ACO_Ordnance>() != null;
                if (m.Missile == null) { Add(g, ERROR, $"'{n}/{m.name}': 탄체 프리팹(Missile)이 비어 있습니다.", m); }
                if (!ordnance && m.TargetLayers.value == 0) { Add(g, WARN, $"'{n}/{m.name}': 유도 미사일인데 TargetLayers가 비어 락온할 표적이 없습니다.", m); }
                if (!ordnance && m.UpgradeFamily == ACO_UpgradeFamily.SpecialAGM && !m.MissileSplash)
                {
                    Add(g, WARN, $"'{n}/{m.name}': 강화 계열이 SpecialAGM인데 MissileSplash가 꺼져 있습니다(범위 피해·폭발 반경 강화 없음).", m,
                        () => { Undo.RecordObject(m, "ACO MissileSplash"); m.MissileSplash = true; PrefabUtility.RecordPrefabInstancePropertyModifications(m); EditorUtility.SetDirty(m); });
                }
            }
        }
    }

    // ───────────── 도우미 ─────────────

    /// 씬에 하나만 있어야 하는 컴포넌트. 없으면 missingSeverity, 여러 개면 경고.
    private T Single<T>(string group, string label, int missingSeverity, Action create) where T : Component
    {
        List<T> all = FindAll<T>();
        if (all.Count == 0)
        {
            Add(group, missingSeverity, $"{label}가(이) 씬에 없습니다.", null, create);
            return null;
        }
        if (all.Count > 1) { Add(group, WARN, $"{label}가(이) {all.Count}개 있습니다 — 첫 번째('{all[0].name}')만 연결됩니다. 나머지는 지우세요.", all[1]); }
        else { Add(group, OK, label, all[0]); }
        return all[0];
    }

    private void PointCheck(string group, ACO_GameFlow flow, Transform point, string label, string childName)
    {
        if (point != null) { Add(group, OK, label, point); return; }
        Add(group, WARN, $"{label}가(이) 비어 있습니다 — [고치기]는 빈 지점을 만들어 넣습니다(위치는 직접 옮기세요).", flow, () =>
        {
            var go = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(go, "ACO point");
            go.transform.SetParent(flow.transform, false);
            Undo.RecordObject(flow, "ACO point");
            if (childName == "BriefingPoint") { flow.BriefingPoint = go.transform; }
            else if (childName == "HangarPoint") { flow.HangarPoint = go.transform; }
            else { flow.SpectatePoint = go.transform; }
            PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
            EditorUtility.SetDirty(flow);
            Selection.activeGameObject = go;
        });
    }

    /// 갑판 출격 위치를 기체 수만큼 채운다(빈 칸에 새 지점, 위치는 직접 옮긴다).
    private static void AddPoints(ACO_GameFlow flow, int count)
    {
        Undo.RecordObject(flow, "ACO deck points");
        var list = new List<Transform>();
        if (flow.DeckPoints != null) { list.AddRange(flow.DeckPoints); }
        while (list.Count < count) { list.Add(null); }
        for (int i = 0; i < count; i++)
        {
            if (list[i] != null) { continue; }
            var d = new GameObject($"DeckPoint_{i + 1}");
            Undo.RegisterCreatedObjectUndo(d, "ACO deck point");
            d.transform.SetParent(flow.transform, false);
            d.transform.localPosition = new Vector3(i * 20f, 0f, 0f);
            list[i] = d.transform;
        }
        flow.DeckPoints = list.ToArray();
        PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
        EditorUtility.SetDirty(flow);
    }

    private static bool HasFlowButton(int action)
    {
        foreach (ACO_FlowButton b in FindAll<ACO_FlowButton>()) { if (b.Action == action) { return true; } }
        return false;
    }

    /// 프리팹이 없으면 만들고 씬에 하나 놓는다.
    /// NPC 전투기에 플레어 장치를 붙인다: 체력·비행을 잇고, 이름에 'flare'가 들어간 자식 파티클을 연출로 쓴다.
    private static void AddFlareDispenser(ACO_NPCFighterAI ai)
    {
        ACO_FlareDispenser d = ai.GetComponent<ACO_FlareDispenser>();
        if (d == null) { d = UdonSharpEditor.UdonSharpUndo.AddComponent<ACO_FlareDispenser>(ai.gameObject); }
        Undo.RecordObject(d, "ACO flares");
        d.TargetHealth = ai.TargetHealth;
        d.Flight = ai.Flight;
        if (d.FlareParticles == null)
        {
            foreach (ParticleSystem ps in ai.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.name.ToLowerInvariant().Contains("flare")) { d.FlareParticles = ps; break; }
            }
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(d);
        EditorUtility.SetDirty(d);
        Undo.RecordObject(ai, "ACO flares");
        ai.Flares = d;
        PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
        EditorUtility.SetDirty(ai);
    }

    private static void PlacePrefab(string name, Action build)
    {
        string path = $"{PrefabDir}/{name}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { build(); }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { Debug.LogError($"[ACO] 프리팹을 만들지 못했습니다: {path}"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
        Undo.RegisterCreatedObjectUndo(go, $"ACO place {name}");
        Selection.activeGameObject = go;
        Debug.Log($"[ACO] '{name}'을(를) 씬에 놓았습니다 — 알맞은 곳으로 옮기세요.", go);
    }
}
