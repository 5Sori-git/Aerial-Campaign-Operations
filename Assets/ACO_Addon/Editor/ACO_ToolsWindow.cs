using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using ACO;
using Object = UnityEngine.Object;

/// 메뉴 ACO → Tools... : ACO 셋업 도구를 한 창에 모은다(무엇을 하는지 설명과 함께). 개별 메뉴(ACO/...)도 그대로 남아 있다.
///  - 시작하기: 셋업 검사·프리팹 생성 창 열기, Bake
///  - 씬 오브젝트: 씬에 하나씩 두는 관리 오브젝트 만들기(있으면 선택)
///  - 패널 놓기: 프리팹으로 만드는 패널을 씬에 놓기(프리팹이 없으면 만든다)
///  - 플레이어 기체: Hierarchy에서 고른 SAV 기체에 적용
///  - 유지보수: 동기화 모드/네트워크 ID 고치기
public class ACO_ToolsWindow : EditorWindow
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";

    private struct SceneTool
    {
        public string Label;
        public string Description;
        public Type Component;   // 씬에 있는지 확인할 컴포넌트
        public Action Create;
    }

    private struct PanelTool
    {
        public string Label;
        public string Description;
        public Type Component;
        public string Prefab;
        public Action Build;
    }

    private struct VehicleTool
    {
        public string Label;
        public string Description;
        public Action Run;
    }

    private static readonly SceneTool[] SceneTools =
    {
        new SceneTool { Label = "미션 관리자", Component = typeof(ACO_MissionManager), Create = ACO_SceneBaker.CreateMissionManager,
            Description = "페이즈 진행·점수·실패 조건·웨이브·랭크를 판단한다(오너). 씬에 하나." },
        new SceneTool { Label = "미션 생성기", Component = typeof(ACO_MissionGenerator), Create = ACO_SceneBaker.CreateMissionGenerator,
            Description = "NPC 풀(자식으로 둔 전투기·지상·함선·고가치 표적)을 작전 구역에 배치한다. 브리핑 미리 배치·증원·페이즈 목표 배치. 씬에 하나." },
        new SceneTool { Label = "전투 디렉터 (선택)", Component = typeof(ACO_CombatDirector), Create = ACO_SceneBaker.CreateCombatDirector,
            Description = "한 플레이어를 동시에 공격하는 NPC 전투기 수를 제한한다(공격 정원). 없으면 제한 없이 동작." },
        new SceneTool { Label = "육각 지형 (선택)", Component = typeof(ACO_HexTerrain), Create = ACO_SceneBaker.CreateHexTerrain,
            Description = "씨앗으로 섬/고원/산맥/협곡 지형을 육각 기둥으로 만든다. 리롤·작전 테이블 지형에 쓰인다. 항모는 ExcludeZones에." },
        new SceneTool { Label = "지형 장식 (선택)", Component = typeof(ACO_TerrainFeatures), Create = ACO_SceneBaker.CreateTerrainFeatures,
            Description = "육각 지형 위에 도시·마을·비행장·항구·등대 등을 놓는다(육각 지형 필요)." },
        new SceneTool { Label = "페이즈 음성 (선택)", Component = typeof(ACO_MissionVoice), Create = ACO_SceneBaker.CreateMissionVoice,
            Description = "페이즈 시작/성공/실패 때 무전 음성을 튼다(클립은 직접 넣는다)." },
        new SceneTool { Label = "진행도 (재화·강화·해금)", Component = typeof(ACO_PlayerProgress), Create = ACO_SceneBaker.CreatePlayerProgress,
            Description = "크레딧·단계식 강화·특수무장 해금을 플레이어별로 영구 저장, 미션 보상 계산. SafeZones에 항모 갑판 콜라이더를 넣는다. 씬에 하나." },
        new SceneTool { Label = "게임 진행 (명단·준비·출격)", Component = typeof(ACO_GameFlow), Create = ACO_SceneBaker.CreateGameFlow,
            Description = "참가 명단·READY·카운트다운·일제 출격·재출격·관전·귀환·지형 리롤. 만들면 DeckPoint 4개와 Briefing(대기실)/Spectate 지점이 생긴다(격납고를 따로 두려면 HangarPoint를 넣는다) — 위치로 옮긴다. 씬에 하나." },
        new SceneTool { Label = "화면 암전", Component = typeof(ACO_ScreenFader), Create = ACO_SceneBaker.CreateScreenFader,
            Description = "순간이동·출격 때 화면이 어두워졌다 밝아진다. 씬에 하나." },
        new SceneTool { Label = "무전 자막", Component = typeof(ACO_Radio), Create = ACO_SceneBaker.CreateRadio,
            Description = "상황(격추·빗나감·적 플레어·미사일 경보·아군 격추·호위 피격·페이즈 등)에 맞는 대사를 확률·쿨타임으로 골라 머리를 따라오는 자막(+선택 음성)으로 띄운다. 대사는 인스펙터에서. 씬에 하나." },
        new SceneTool { Label = "작전 테이블 (3D 미니맵, 선택)", Component = typeof(ACO_TacticalMap), Create = ACO_SceneBaker.CreateTacticalMap,
            Description = "브리핑실의 Tacview식 미니맵: 축소 지형 + 모든 유닛 실시간. 브리핑실 가운데로 옮기고 HomeBase에 항모." },
    };

    private static readonly PanelTool[] PanelTools =
    {
        new PanelTool { Label = "HUD (점수 + 표적 마커 + 크로스헤어)", Component = typeof(ACO_TargetMarkers), Prefab = "ACO_HUD", Build = ACO_HUDBuilder.CreateHUDPrefab,
            Description = "머리를 따라오는 점수 패널, 표적 테두리·락온 표시, 화면 가운데 크로스헤어. 씬에 하나(위치 무관)." },
        new PanelTool { Label = "작전 콘솔 (로비)", Component = typeof(ACO_MissionStartButton), Prefab = "ACO_MissionConsole", Build = ACO_HUDBuilder.CreateMissionConsolePrefab,
            Description = "페이즈 수 선택 + 참가 명단 + JOIN/LEAVE + 지형 리롤. 브리핑실에." },
        new PanelTool { Label = "작전판 (디브리핑)", Component = typeof(ACO_MissionLog), Prefab = "ACO_MissionBoard", Build = ACO_HUDBuilder.CreateMissionBoardPrefab,
            Description = "격추 기록·조종사 집계·디브리핑·보상 표시. 개인 점수 보상이 여기서 계산된다. 브리핑실에." },
        new PanelTool { Label = "격납고", Component = typeof(ACO_Loadout), Prefab = "ACO_Hangar", Build = ACO_HUDBuilder.CreateHangarPrefab,
            Description = "특수무장 선택/해금, 강화 상점(탭), 크레딧, READY/LAUNCH(관전자는 SPECTATE). 격납고에." },
        new PanelTool { Label = "크레딧 보드", Component = typeof(ACO_CreditBoard), Prefab = "ACO_CreditBoard", Build = ACO_HUDBuilder.CreateCreditBoardPrefab,
            Description = "사용한 에셋·라이선스 표기. 내용은 Credits/ACO_Credits.txt(영문, 실행할 때 읽는다). 로비 벽에." },
        new PanelTool { Label = "HUD 설정 패널 (선택)", Component = typeof(ACO_HudSettings), Prefab = "ACO_HudSettings", Build = ACO_HUDBuilder.CreateHudSettingsPrefab,
            Description = "플레이어별 HUD 조정: 점수 패널 위치·크기, 결과·안내 글자 위치·크기, 마커 테두리 크기, 정보 각도, 크로스헤어·RWR 크기·거리. 로비나 격납고에." },
    };

    private static readonly VehicleTool[] VehicleTools =
    {
        new VehicleTool { Label = "기체 셋업 (한 번에)", Run = ACO_PlayerMissileSetup.SetupSelectedVehicles,
            Description = "DFUNC_AAM → ACO 미사일(+HUD 슬롯 막대), 강화 확장, 좌석 재조정·더블탭 내리기, 특수무장 슬롯(오른쪽 다이얼 Element 2, 키 3), "
                + "무장 카탈로그의 모든 특수무장 모듈(4AAM·UGB·RKT 포함 — SAV 폭탄/로켓은 ACO 모듈로 바꾼다), 키보드 단축키 정리. 여러 번 해도 된다(된 단계는 건너뜀). "
                + "새 무장 정의를 더한 뒤 다시 실행하면 그 모듈만 들어간다." },
        new VehicleTool { Label = "HUD 슬롯 막대 다시 만들기", Run = ACO_PlayerMissileSetup.RebuildBarsOnSelection,
            Description = "ACO 미사일의 잔탄 텍스트 옆 장전 막대를 다시 만든다(위치를 고쳤거나 HUD를 바꿨을 때)." },
    };

    private Vector2 _scroll;
    private bool _foldScene = true;
    private bool _foldPanels = true;
    private bool _foldVehicles = true;
    private bool _foldMaintenance = true;

    [MenuItem("ACO/Tools...", false, -10)]
    public static void Open()
    {
        var w = GetWindow<ACO_ToolsWindow>("ACO Tools");
        w.minSize = new Vector2(520f, 480f);
    }

    private void OnSelectionChange() { Repaint(); }
    private void OnHierarchyChange() { Repaint(); }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        // ── 시작하기
        EditorGUILayout.LabelField("시작하기", EditorStyles.boldLabel);
        Row("셋업 검사", "씬에서 빠진 셋업을 오류/경고로 보여 주고 [고치기]로 채운다. 처음 셋업과 테스트 전에 먼저 연다.", "열기", ACO_SetupValidator.Open);
        Row("프리팹 생성", "NPC·미사일·지형 조각·HUD·패널·플레이어 특수무장(탄체 + 무장 정의) 프리팹을 만든다(이미 있으면 덮어쓸지 고른다).", "열기", ACO_PrefabBuilderWindow.Open);
        Row("Bake (참조 굽기)", "씬의 참조를 연결한다: NPC 이웃·디렉터, 생성기 풀, 플레이어 미사일 표적, 격납고 특수무장 목록(무장 정의), 진행도·게임 진행·작전 테이블·HUD 설정·RWR 연결. 빌드 때도 자동.", "실행", ACO_SceneBaker.BakeNeighbors);

        // ── 씬 오브젝트
        EditorGUILayout.Space(8f);
        _foldScene = EditorGUILayout.Foldout(_foldScene, "씬 오브젝트 (씬에 하나씩)", true, EditorStyles.foldoutHeader);
        if (_foldScene)
        {
            foreach (SceneTool t in SceneTools)
            {
                Component found = FindComponent(t.Component);
                if (found != null) { Row(t.Label, t.Description, "선택", () => Select(found), $"있음: {found.name}", true); }
                else { Row(t.Label, t.Description, "만들기", t.Create, "없음", false); }
            }
            bool natural = ACO_TerrainLook.IsApplied();
            Row("육각 지형 자연 머티리얼 + 나무",
                "셰이더 ACO/HexTerrain: 월드 높이·경사로 모래·풀·숲·흙·바위·눈·절벽(지층)을 칠해 육각 경계를 숨긴다. 나무(ACO/HexTrees)는 숲 자리에만 서고 멀면 사라진다(충돌 없음). "
                + "기둥 프리팹에 넣고 높이대 머티리얼을 끈다. 지면 머티리얼 값을 바꾸면 다시 적용(나무에 복사).",
                natural ? "다시 적용" : "적용", ACO_TerrainLook.Apply, natural ? "적용됨" : "예전 단색 머티리얼 / 나무 없음", natural);
            bool ocean = ACO_OceanLook.IsApplied();
            Row("바다 셰이더",
                "셰이더 ACO/Ocean: 세 배율 물결(고공에서 반복 무늬 안 보임), 하늘 반사·햇빛 반짝임, 흰 파도 거품 + 카메라 둘레 1.2km 너울(정점 이동 원판, 고공에선 잦아듦). "
                + "물 레이어의 큰 바다 평면에 넣는다(콜라이더 그대로). 섬 둘레 얕은 물·해안 거품은 머티리얼의 Use depth texture(그림자를 켠 실시간 방향광 필요).",
                ocean ? "다시 적용" : "적용", ACO_OceanLook.Apply, ocean ? "적용됨" : "기본 머티리얼", ocean);
            bool lit = ACO_LightingSetup.IsSetUp();
            Row("조명 설정 (한 번에)",
                "태양 Mixed(실시간 직사광 — 런타임 지형·기체도 밝게), 하늘·환경광·원거리 안개, 굽기 설정(간접광만), 항모 정적 표시, 격납고 천장 조명·라이트 프로브·반사 프로브, "
                + "포스트 프로세싱(패키지가 있으면: 블룸·ACES). 끝나면 아래 [굽기].",
                lit ? "다시 설정" : "설정", ACO_LightingSetup.SetUp, lit ? "설정됨" : "기본 조명", lit);
            Row("격납고 프로브 맞추기", "ACO_HangarLighting 아래 조명(직접 옮기거나 더 놓은 것)에 맞춰 라이트 프로브·반사 프로브만 다시 깐다. 조명은 그대로.", "맞추기", ACO_LightingSetup.RefitHangarProbes);
            Row("라이트 굽기", "항모 내부 간접광·격납고 조명·반사 프로브·환경광을 굽는다(몇 분). 조명 설정 뒤에.", "굽기", ACO_LightingSetup.Bake);
        }

        // ── 패널
        EditorGUILayout.Space(8f);
        _foldPanels = EditorGUILayout.Foldout(_foldPanels, "패널 놓기 (프리팹 → 씬)", true, EditorStyles.foldoutHeader);
        if (_foldPanels)
        {
            foreach (PanelTool t in PanelTools)
            {
                Component found = FindComponent(t.Component);
                if (found != null) { Row(t.Label, t.Description, "선택", () => Select(found), $"씬에 있음: {found.transform.root.name}", true); }
                else
                {
                    bool prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{t.Prefab}.prefab") != null;
                    PanelTool tool = t;
                    Row(t.Label, t.Description, "씬에 놓기", () => PlacePrefab(tool), prefab ? "씬에 없음" : "프리팹도 없음(만들어서 놓음)", false);
                }
            }
        }

        // ── 플레이어 기체
        EditorGUILayout.Space(8f);
        _foldVehicles = EditorGUILayout.Foldout(_foldVehicles, "플레이어 기체 (Hierarchy에서 고른 SAV 기체에 적용)", true, EditorStyles.foldoutHeader);
        if (_foldVehicles)
        {
            int selected = SelectedEntityCount();
            EditorGUILayout.HelpBox(selected > 0 ? $"선택한 기체 {selected}대에 적용합니다." : "Hierarchy에서 SAV 기체(또는 그 안의 오브젝트)를 선택하세요.",
                selected > 0 ? MessageType.Info : MessageType.Warning);
            using (new EditorGUI.DisabledScope(selected == 0))
            {
                foreach (VehicleTool t in VehicleTools) { Row(t.Label, t.Description, "적용", t.Run); }
            }
        }

        // ── 유지보수
        EditorGUILayout.Space(8f);
        _foldMaintenance = EditorGUILayout.Foldout(_foldMaintenance, "유지보수", true, EditorStyles.foldoutHeader);
        if (_foldMaintenance)
        {
            Row("빌드 오류 고치기 (동기화 모드 + 네트워크 ID)",
                "Udon 동기화 모드가 섞여 되돌아가는 U# 문제(씬·프리팹, 빌드 때도 자동)와 'Failed to assign network IDs' 빌드 실패를 한 번에 고친다.",
                "실행", () => { ACO_SyncModeFixer.FixAll(); ACO_NetworkIDFixer.FixMenu(); });
        }

        EditorGUILayout.EndScrollView();
    }

    // ───────────── 도우미 ─────────────

    private void Row(string label, string description, string button, Action action, string status = null, bool ok = false)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
                if (status != null)
                {
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    style.normal.textColor = ok ? new Color(0.5f, 0.8f, 0.5f) : new Color(1f, 0.7f, 0.35f);
                    EditorGUILayout.LabelField(status, style);
                }
            }
            if (GUILayout.Button(button, GUILayout.Width(80f), GUILayout.Height(34f)))
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                GUIUtility.ExitGUI();
            }
        }
    }

    private static Component FindComponent(Type type)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Component c = root.GetComponentInChildren(type, true);
            if (c != null) { return c; }
        }
        return null;
    }

    private static void Select(Object o)
    {
        Selection.activeObject = o;
        EditorGUIUtility.PingObject(o);
    }

    private static int SelectedEntityCount()
    {
        var set = new HashSet<SaccEntity>();
        foreach (GameObject go in Selection.gameObjects)
        {
            // ACO_PlayerMissileSetup.SelectedEntities와 같은 규칙(부모 쪽, 없으면 자식 쪽)
            SaccEntity e = go.GetComponentInParent<SaccEntity>(true);
            if (e == null) { e = go.GetComponentInChildren<SaccEntity>(true); }
            if (e != null) { set.Add(e); }
        }
        return set.Count;
    }

    private static void PlacePrefab(PanelTool t)
    {
        string path = $"{PrefabDir}/{t.Prefab}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { t.Build(); }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { Debug.LogError($"[ACO] 프리팹을 만들지 못했습니다: {path}"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
        Undo.RegisterCreatedObjectUndo(go, $"ACO place {t.Prefab}");
        Selection.activeGameObject = go;
        ACO_SceneBaker.BakeNeighbors();
        Debug.Log($"[ACO] '{t.Prefab}'을(를) 씬에 놓았습니다 — 알맞은 곳으로 옮기세요(UI 패널은 파란 화살표 +Z가 관객 쪽).", go);
    }
}
