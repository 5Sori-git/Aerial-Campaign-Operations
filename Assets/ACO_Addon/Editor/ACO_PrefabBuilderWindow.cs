using UnityEditor;
using UnityEngine;

/// 메뉴 ACO → Create Prefabs... : ACO 프리팹 생성을 한 곳에서 한다.
/// 이미 있는 프리팹은 기본으로 체크가 꺼져 있다 — 생성은 덮어쓰기라, 직접 손본 프리팹(모델, 수치)이 초기화되기 때문.
public class ACO_PrefabBuilderWindow : EditorWindow
{
    private struct Entry
    {
        public string Label;
        public string Description;
        public string[] PrefabNames; // 이 항목이 만드는 프리팹(존재 확인용, Prefabs 폴더)
        public string[] ExtraPaths;  // Prefabs 폴더 밖의 프리팹(존재 확인용, 전체 경로)
    }

    private static readonly Entry[] Entries =
    {
        new Entry { Label = "미사일", Description = "ACO_Missile_AAM — NPC 전투기/SAM이 쏘는 미사일", PrefabNames = new[] { "ACO_Missile_AAM" } },
        new Entry { Label = "NPC 전투기", Description = "NPC_Fighter — 미사일이 없으면 같이 만든다", PrefabNames = new[] { "NPC_Fighter" } },
        new Entry { Label = "SAM 포대", Description = "NPC_SAM — 미사일이 없으면 같이 만든다", PrefabNames = new[] { "NPC_SAM" } },
        new Entry { Label = "대공포", Description = "NPC_AAGun", PrefabNames = new[] { "NPC_AAGun" } },
        new Entry { Label = "공장 (고가치 표적)", Description = "NPC_Factory", PrefabNames = new[] { "NPC_Factory" } },
        new Entry { Label = "벙커 (고가치 표적)", Description = "NPC_Bunker — 기총이 통하지 않는다", PrefabNames = new[] { "NPC_Bunker" } },
        new Entry { Label = "전함 (부위 파괴)", Description = "NPC_Warship — 고가치 표적. 본체 + VLS 2 + 함포 + CIWS 2, 콜드런치 미사일(ACO_Missile_VLS)도 없으면 같이", PrefabNames = new[] { "NPC_Warship" } },
        new Entry { Label = "구축함 (부위 파괴)", Description = "NPC_Destroyer — 일반 유닛. 본체 + VLS 1 + CIWS 1, 함대/전함 호위로 스폰", PrefabNames = new[] { "NPC_Destroyer" } },
        new Entry { Label = "전차", Description = "NPC_Tank — 지상 항로를 따라 순찰/매복, 아군 지상 표적(콘보이)을 포격", PrefabNames = new[] { "NPC_Tank" } },
        new Entry { Label = "아군 콘보이 차량 (지상 호위 대상)", Description = "NPC_ConvoyTruck — 지상 호위 페이즈 대상. 지면을 따라 항로 주행", PrefabNames = new[] { "NPC_ConvoyTruck" } },
        new Entry { Label = "아군 수송기 (호위 대상)", Description = "NPC_Transport — 호위 페이즈 대상. 항로를 따라 비행, 적이 우선 공격", PrefabNames = new[] { "NPC_Transport" } },
        new Entry { Label = "육각 기둥 지형 조각", Description = "ACO_HexColumn — ACO_HexTerrain이 씨앗으로 깔 기둥(메시/높이대 머티리얼 4종은 Generated에)", PrefabNames = new[] { "ACO_HexColumn" } },
        new Entry { Label = "HUD (점수 + 표적 마커)", Description = "ACO_HUD — 점수 패널, 표적 테두리·락온 표시, 크로스헤어(마커 메시/머티리얼은 Generated 폴더에)", PrefabNames = new[] { "ACO_HUD" } },
        new Entry { Label = "작전판 (미션 로그)", Description = "ACO_MissionBoard — 격추 기록/디브리핑 보드(로컬)", PrefabNames = new[] { "ACO_MissionBoard" } },
        new Entry { Label = "작전 콘솔 (로비)", Description = "ACO_MissionConsole — 1~5 페이즈 선택 + 참가 명단 + JOIN/LEAVE + 지형 리롤", PrefabNames = new[] { "ACO_MissionConsole" } },
        new Entry { Label = "플레이어 특수무장", Description = "폭탄·로켓 탄체(ACO_Bomb, ACO_Rocket) + 무장 정의 16종(SpecialWeapons/ACO_Special_*: 4AAM·UGB·LGB·RKT·LGR·4AGM·LAGM·LASM·SASM·LAAM·QAAM·HVAA·HPAA·FLAK·CCAA·ADAM) — 상점에서 파는 무장. 있는 예제도 다시 만든다(손본 값 초기화 — 없는 것만 더하려면 ACO → Weapons → Add Missing Special Weapon Examples). 복제해 새 무장을 만든다(복제본은 덮어쓰지 않는다)",
            PrefabNames = new[] { "ACO_Bomb", "ACO_Rocket" }, ExtraPaths = ACO_SpecialWeaponCatalog.ExamplePaths() },
        new Entry { Label = "격납고", Description = "ACO_Hangar — 특수무장 선택/해금, 강화 상점(탭), 크레딧·보상, READY/LAUNCH(관전자는 SPECTATE)", PrefabNames = new[] { "ACO_Hangar" } },
        new Entry { Label = "지형 장식 (도시/마을/랜드마크)", Description = "도시 블록 3 + 마을 2 + 항구/비행장/등대/레이더/통신탑/유적 임시 모델 — ACO_TerrainFeatures가 육각 지형 위에 놓는다(메시/머티리얼은 Generated/Deco)", PrefabNames = ACO_DecoBuilder.AllNames() },
        new Entry { Label = "HUD 설정 패널", Description = "ACO_HudSettings — 플레이어별 HUD 조정(점수 패널 위치·크기, 결과·안내 글자 위치·크기, 마커 테두리, 정보 각도, 크로스헤어·RWR 크기·거리), 영구 저장", PrefabNames = new[] { "ACO_HudSettings" } },
        new Entry { Label = "슈퍼에이스 (보너스 페이즈)", Description = "NPC_SuperAce — 체력 6배 에이스 + 다연장(여러 플레이어 동시 발사) + 기만체(가짜 표적). 생성기 풀 아래에 하나 두면 보너스 페이즈에만 나온다", PrefabNames = new[] { "NPC_SuperAce" } },
        new Entry { Label = "적 폭격기 (요격 대상)", Description = "NPC_Bomber — 요격 페이즈. 도시 상공까지 항로 비행, 도착해 폭탄을 떨어뜨리면 미션 실패. 풀 아래에 4~6대", PrefabNames = new[] { "NPC_Bomber" } },
        new Entry { Label = "공격 헬리콥터", Description = "NPC_Helicopter — 전차 항로 이동 + 지면·해수면 위 70m(바다·절벽 위로도), 아군 지상·공중 표적에 기관포. 생성기 지상 풀 아래에", PrefabNames = new[] { "NPC_Helicopter" } },
        new Entry { Label = "곡사포 (자주포)", Description = "NPC_Artillery + 포탄 ACO_ArtilleryShell — 호위 항로에서 2.5~4.5km 떨어져 아군 콘보이·함선을 고각 포격. 생성기 지상 풀 아래에", PrefabNames = new[] { "NPC_Artillery", "ACO_ArtilleryShell" } },
        new Entry { Label = "상륙전 부대 (LCAC·공격헬기·상륙함·호위함)", Description = "NPC_LCAC(지켜야 하는 대상) + NPC_FriendlyGunship(아군 공격헬기) + NPC_AssaultShip(후방 대기 모선) + NPC_GuardShip(대공포 2·함포 1) — 상륙전 페이즈. 생성기 유닛 풀 아래에 LCAC 3~4, 공격헬기 2~3, 상륙함 1, 호위함 2", PrefabNames = new[] { "NPC_LCAC", "NPC_FriendlyGunship", "NPC_AssaultShip", "NPC_GuardShip" } },
        new Entry { Label = "공격기 (지상·해상 호위에만)", Description = "NPC_Attacker — 전투기 바리에이션. 아군 지상(콘보이·LCAC·호위함)만 대지 미사일·기총으로 노린다. 지상 호위·상륙전 페이즈에서만 2기 편대로 나온다. 생성기 전투기 풀 아래에 4~6기", PrefabNames = new[] { "NPC_Attacker" } },
        new Entry { Label = "디버그 패널 (테스트용)", Description = "ACO_DebugPanel — 미션 가중치(페이즈 모듈마다), 에이스·베테랑·보너스 페이즈 확률(모두에게), 내 크레딧(로컬). 공개 월드에선 AllowedPlayers에 이름을 넣거나 끈다", PrefabNames = new[] { "ACO_DebugPanel" } },
        new Entry { Label = "데칼 스튜디오 (격납고)", Description = "ACO_DecalStudio — 이미지 URL로 데칼 2개를 격납고 샘플 기체에 데칼 건으로 붙인다(크기·회전·뒤집기·좌우 대칭, 자리는 저장). 탑승하면 실기체에 같은 자리로. 씬에 두고 SampleRoot·DecalKey를 넣은 뒤 Bake", PrefabNames = new[] { "ACO_DecalStudio" } },
        new Entry { Label = "TAC 네임 패널 (콜사인)", Description = "ACO_TacNamePanel — 내 콜사인 입력(VRChat 키보드) · SET · CLEAR. 플레이어 영구 저장에 남고, 다른 사람 화면의 내 기체 테두리 위에 보인다(중복이면 뒤에 숫자). 격납고 등에 둔다", PrefabNames = new[] { "ACO_TacNamePanel" } },
        new Entry { Label = "비행 HUD (SAV HUD 대신)", Description = "ACO_FlightHud — 기체 앞 고정 HUD: 헤딩 테이프·속도(kt)·스로틀·고도(ft)·피치 래더·속도 벡터·G·상태(GEAR/FLAP/HOOK/AIRBRAKE)·잔탄·HP·장전 막대·조준 표시(기총 피퍼·W·십자). 씬에 하나 두고 Bake하면 SAV HUD를 숨긴다", PrefabNames = new[] { "ACO_FlightHud" } },
        new Entry { Label = "관전 패널 (리플레이식 카메라)", Description = "ACO_SpectatorPanel — 큰 스크린 + POWER / 대상(조종사) < > / 컷 < > (AUTO = 감독). 플레이어 기체를 추적·플라이바이·망원·나란히·조종석·미사일·표적 캠으로 실시간 관전. 로컬 전용, 켜고 끄기는 직접", PrefabNames = new[] { "ACO_SpectatorPanel" } },
    };

    private bool[] _selected;
    private Vector2 _scroll;

    [MenuItem("ACO/Create Prefabs...", false, 0)]
    public static void Open()
    {
        var window = GetWindow<ACO_PrefabBuilderWindow>(true, "ACO 프리팹 생성", true);
        window.minSize = new Vector2(460f, 360f);
        window.ResetSelection();
    }

    private void OnEnable()
    {
        if (_selected == null || _selected.Length != Entries.Length) { ResetSelection(); }
    }

    /// 없는 프리팹만 체크한다.
    private void ResetSelection()
    {
        _selected = new bool[Entries.Length];
        for (int i = 0; i < Entries.Length; i++) { _selected[i] = !AllExist(Entries[i]); }
    }

    private static bool AllExist(Entry e)
    {
        foreach (string name in e.PrefabNames)
        {
            if (!ACO_PrefabBuilder.Exists(name)) { return false; }
        }
        if (e.ExtraPaths != null)
        {
            foreach (string path in e.ExtraPaths) { if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return false; } }
        }
        return true;
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("체크한 프리팹을 Assets/ACO_Addon/Prefabs에 만듭니다.\n" +
                                "이미 있는 프리팹을 체크하면 덮어씁니다 — 직접 바꾼 모델과 수치가 초기화되고, 씬에 놓인 인스턴스도 새 프리팹을 따릅니다.",
                                MessageType.Info);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry e = Entries[i];
            bool exists = AllExist(e);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                _selected[i] = EditorGUILayout.ToggleLeft(e.Label, _selected[i], EditorStyles.boldLabel, GUILayout.Width(190f));
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(e.Description, EditorStyles.miniLabel);
                    var status = new GUIStyle(EditorStyles.miniLabel);
                    status.normal.textColor = exists ? (_selected[i] ? new Color(1f, 0.55f, 0.2f) : new Color(0.5f, 0.8f, 0.5f)) : new Color(0.6f, 0.7f, 1f);
                    EditorGUILayout.LabelField(exists ? (_selected[i] ? "이미 있음 → 덮어씀" : "이미 있음") : "없음 → 새로 만듦", status);
                }
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("없는 것만 선택")) { ResetSelection(); }
            if (GUILayout.Button("모두 선택")) { for (int i = 0; i < _selected.Length; i++) { _selected[i] = true; } }
            if (GUILayout.Button("모두 해제")) { for (int i = 0; i < _selected.Length; i++) { _selected[i] = false; } }
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!AnySelected()))
        {
            if (GUILayout.Button("선택한 프리팹 생성", GUILayout.Height(32f))) { Build(); }
        }
    }

    private bool AnySelected()
    {
        foreach (bool s in _selected) { if (s) { return true; } }
        return false;
    }

    private void Build()
    {
        // 덮어쓰는 항목이 있으면 한 번 더 확인한다.
        string overwrite = "";
        for (int i = 0; i < Entries.Length; i++)
        {
            if (_selected[i] && AllExist(Entries[i])) { overwrite += "\n  · " + Entries[i].Label; }
        }
        if (overwrite.Length > 0 &&
            !EditorUtility.DisplayDialog("프리팹 덮어쓰기", "다음 프리팹을 덮어씁니다. 직접 바꾼 내용이 초기화됩니다." + overwrite, "덮어쓰기", "취소"))
        {
            return;
        }

        var built = new System.Text.StringBuilder();
        // 미사일을 먼저 만들어야 전투기/SAM이 새 미사일을 참조한다.
        if (_selected[0]) { Log(built, ACO_PrefabBuilder.BuildMissile()); }
        if (_selected[1]) { Log(built, ACO_PrefabBuilder.BuildFighter()); }
        if (_selected[2]) { Log(built, ACO_PrefabBuilder.BuildSAM()); }
        if (_selected[3]) { Log(built, ACO_PrefabBuilder.BuildAAGun()); }
        if (_selected[4]) { Log(built, ACO_PrefabBuilder.BuildFactory()); }
        if (_selected[5]) { Log(built, ACO_PrefabBuilder.BuildBunker()); }
        if (_selected[6]) { Log(built, ACO_PrefabBuilder.BuildWarship()); }
        if (_selected[7]) { Log(built, ACO_PrefabBuilder.BuildDestroyer()); }
        if (_selected[8]) { Log(built, ACO_PrefabBuilder.BuildTank()); }
        if (_selected[9]) { Log(built, ACO_PrefabBuilder.BuildConvoyTruck()); }
        if (_selected[10]) { Log(built, ACO_PrefabBuilder.BuildTransport()); }
        if (_selected[11]) { Log(built, ACO_PrefabBuilder.BuildHexColumn()); }
        if (_selected[12]) { ACO_HUDBuilder.CreateHUDPrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_HUD")); }
        if (_selected[13]) { ACO_HUDBuilder.CreateMissionBoardPrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_MissionBoard")); }
        if (_selected[14]) { ACO_HUDBuilder.CreateMissionConsolePrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_MissionConsole")); }
        if (_selected[15])
        {
            Log(built, ACO_PrefabBuilder.BuildBomb());
            Log(built, ACO_PrefabBuilder.BuildRocket());
            ACO_SpecialWeaponCatalog.CreateExamples(true);
            built.Append("\n  ").Append(ACO_SpecialWeaponCatalog.Folder).Append("/ACO_Special_* (").Append(ACO_SpecialWeaponCatalog.ExamplePaths().Length).Append(")");
        }
        if (_selected[16]) { ACO_HUDBuilder.CreateHangarPrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_Hangar")); }
        if (_selected[17]) { foreach (GameObject deco in ACO_DecoBuilder.BuildAll()) { Log(built, deco); } }
        if (_selected[18]) { ACO_HUDBuilder.CreateHudSettingsPrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_HudSettings")); }
        if (_selected[19]) { Log(built, ACO_PrefabBuilder.BuildSuperAce()); }
        if (_selected[20]) { Log(built, ACO_PrefabBuilder.BuildBomber()); }
        if (_selected[21]) { Log(built, ACO_PrefabBuilder.BuildHelicopter()); }
        if (_selected[22]) { Log(built, ACO_PrefabBuilder.BuildArtilleryShell()); Log(built, ACO_PrefabBuilder.BuildArtillery()); }
        if (_selected[23]) { Log(built, ACO_PrefabBuilder.BuildLcac()); Log(built, ACO_PrefabBuilder.BuildFriendlyGunship()); Log(built, ACO_PrefabBuilder.BuildAssaultShip()); Log(built, ACO_PrefabBuilder.BuildGuardShip()); }
        if (_selected[24]) { Log(built, ACO_PrefabBuilder.BuildAttacker()); }
        if (_selected[25]) { ACO_HUDBuilder.CreateDebugPanelPrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_DebugPanel")); }
        if (_selected[26]) { ACO_HUDBuilder.CreateDecalStudioPrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_DecalStudio")); }
        if (_selected[27]) { ACO_HUDBuilder.CreateTacNamePanelPrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_TacNamePanel")); }
        if (_selected[28]) { ACO_HUDBuilder.CreateFlightHudPrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_FlightHud")); }
        if (_selected[29]) { ACO_HUDBuilder.CreateSpectatorPanelPrefab(); built.Append("\n  ").Append(ACO_PrefabBuilder.PathOf("ACO_SpectatorPanel")); }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ACO] 프리팹 생성 완료:" + built);
        ResetSelection();
        Repaint();
    }

    private static void Log(System.Text.StringBuilder sb, GameObject prefab)
    {
        if (prefab != null) { sb.Append("\n  ").Append(AssetDatabase.GetAssetPath(prefab)); }
    }
}
