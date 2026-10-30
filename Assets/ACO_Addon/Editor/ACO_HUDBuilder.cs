using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UdonSharpEditor;
using TMPro;
using ACO;

/// 점수 HUD + 표적 마커 풀을 가진 ACO_HUD 프리팹을 만든다(기존 파일은 덮어씀). 메뉴는 ACO → Create Prefabs... 창.
/// 마커 테두리 메시(사각형/육각형)와 항상 위에 그리는 머티리얼은 Assets/ACO_Addon/Generated에 에셋으로 저장한다.
public static class ACO_HUDBuilder
{
    private const string GeneratedDir = "Assets/ACO_Addon/Generated";
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const int MarkerCount = 64;
    // 테두리 두께(마커 한 변 1 기준)
    private const float FrameThickness = 0.08f;
    // 체력 바 크기(마커 한 변 1 기준)
    private const float BarWidth = 1.2f;
    private const float BarHeight = 0.09f;

    // 반투명: 테두리는 배경이 비쳐 보이게 옅게, 글자는 읽히도록 조금 더 진하게.
    private static readonly Color HudGreen = new Color(0.35f, 1f, 0.45f, 0.55f);
    private static readonly Color TargetRed = new Color(1f, 0.25f, 0.2f, 0.75f);
    private static readonly Color TextGreen = new Color(0.35f, 1f, 0.45f, 0.9f);
    private static readonly Color TextRed = new Color(1f, 0.25f, 0.2f, 0.9f);

    /// ACO → Create Prefabs... 창에서 부른다.
    public static void CreateHUDPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Generated");
        EnsureFolder("Assets/ACO_Addon", "Prefabs");

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath} — Window → TextMeshPro → Import TMP Essential Resources를 먼저 실행하세요.");
            return;
        }
        Shader markerShader = Shader.Find("ACO/MarkerOverlay");
        Shader textShader = Shader.Find("TextMeshPro/Distance Field Overlay");
        if (markerShader == null || textShader == null)
        {
            Debug.LogError("[ACO] 셰이더를 찾지 못했습니다(ACO/MarkerOverlay 또는 TextMeshPro/Distance Field Overlay).");
            return;
        }

        // 사각형: 꼭짓점이 대각선 방향(45°)이어야 변이 축과 나란하다. 반지름은 한 변 1이 되게.
        Mesh airMesh = SaveMesh(BuildRingMesh(4, 0.5f * Mathf.Sqrt(2f), 45f, "ACO_MarkerSquare"), "ACO_MarkerSquare");
        // 육각형: 위/아래가 평평하게(꼭짓점 0°). 반지름 0.55로 사각형과 비슷한 크기.
        Mesh groundMesh = SaveMesh(BuildRingMesh(6, 0.55f, 0f, "ACO_MarkerHexagon"), "ACO_MarkerHexagon");

        Material airMat = SaveMaterial(markerShader, HudGreen, "ACO_MarkerAir");
        Material groundMat = SaveMaterial(markerShader, HudGreen, "ACO_MarkerGround");
        Material objectiveMat = SaveMaterial(markerShader, TargetRed, "ACO_MarkerObjective");
        Material friendlyMat = SaveMaterial(markerShader, FriendlyBlue, "ACO_MarkerFriendly");
        Material waypointMat = SaveMaterial(markerShader, WaypointBlue, "ACO_MarkerWaypoint");
        Material destinationMat = SaveMaterial(markerShader, DestinationCyan, "ACO_MarkerDestination");
        // 체력 바: 배경을 먼저, 채움을 나중에 그리도록 큐를 나눈다(둘 다 ZTest Always라 깊이로는 순서가 안 정해진다).
        Material barBackMat = SaveMaterial(markerShader, new Color(0f, 0f, 0f, 0.45f), "ACO_HealthBack", 4010);
        Material barHighMat = SaveMaterial(markerShader, new Color(0.35f, 1f, 0.45f, 0.8f), "ACO_HealthHigh", 4011);
        Material barMidMat = SaveMaterial(markerShader, new Color(1f, 0.85f, 0.25f, 0.8f), "ACO_HealthMid", 4011);
        Material barLowMat = SaveMaterial(markerShader, new Color(1f, 0.25f, 0.2f, 0.85f), "ACO_HealthLow", 4011);
        Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        Material textMat = new Material(font.material) { shader = textShader, name = "ACO_HUDTextOverlay" };
        textMat = SaveAsset(textMat, $"{GeneratedDir}/ACO_HUDTextOverlay.mat");

        GameObject root = new GameObject("ACO_HUD");
        ACO_ScoreHUD hud = root.AddUdonSharpComponent<ACO_ScoreHUD>();
        ACO_TargetMarkers markers = root.AddUdonSharpComponent<ACO_TargetMarkers>();

        // ── 점수 패널: 한 단위 = 0.03m. 글자 크기 10 ≈ 1단위라 한 줄이 약 3cm.
        GameObject panel = new GameObject("ScorePanel");
        panel.transform.SetParent(root.transform, false);
        panel.transform.localScale = Vector3.one * 0.03f;

        TextMeshPro mainText = MakeText(panel.transform, "MainText", Vector3.zero, 9f, TextGreen,
            TextAlignmentOptions.TopLeft, new Vector2(40f, 14f), new Vector2(0f, 1f), font, textMat, "SCORE 0\nTIME 00:00\nTGT 0/0   WAVE 0");
        // ── 결과·안내 묶음: 점수 패널과 따로 머리를 따라온다(ACO_ScoreHUD.ResultOffset = 시야 가운데 조금 위, 거리는 점수 패널과 같다). 단위는 점수 패널과 같다.
        GameObject resultGroup = new GameObject("ResultGroup");
        resultGroup.transform.SetParent(root.transform, false);
        resultGroup.transform.localScale = Vector3.one * 0.03f;
        TextMeshPro resultText = MakeText(resultGroup.transform, "ResultText", Vector3.zero, 14f, new Color(1f, 0.85f, 0.3f, 0.9f),
            TextAlignmentOptions.Center, new Vector2(60f, 20f), new Vector2(0.5f, 0.5f), font, textMat, "MISSION COMPLETE\nRANK S");
        resultText.gameObject.SetActive(false);

        hud.Panel = panel.transform;
        hud.MainText = mainText;
        hud.ResultText = resultText;
        hud.ResultGroup = resultGroup.transform;
        hud.ResultOffset = new Vector2(0f, 0.07f);
        hud.NoticeText = MakeNoticeText(resultGroup.transform, NoticeOffset, font, textMat);

        // ── 마커 풀: 마커 한 변 = 1(실행 중 스크립트가 각도 크기에 맞춰 스케일한다)
        GameObject pool = new GameObject("Markers");
        pool.transform.SetParent(root.transform, false);

        var markerRoots = new Transform[MarkerCount];
        var frames = new MeshFilter[MarkerCount];
        var frameRenderers = new MeshRenderer[MarkerCount];
        var tgtLabels = new TextMeshPro[MarkerCount];
        var infoLabels = new TextMeshPro[MarkerCount];
        var healthBars = new Transform[MarkerCount];
        var healthFills = new Transform[MarkerCount];
        var healthFillRenderers = new MeshRenderer[MarkerCount];

        for (int i = 0; i < MarkerCount; i++)
        {
            GameObject m = new GameObject($"Marker_{i:00}");
            m.transform.SetParent(pool.transform, false);

            GameObject frame = new GameObject("Frame");
            frame.transform.SetParent(m.transform, false);
            MeshFilter mf = frame.AddComponent<MeshFilter>();
            mf.sharedMesh = airMesh;
            MeshRenderer mr = frame.AddComponent<MeshRenderer>();
            mr.sharedMaterial = airMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            TextMeshPro tgt = MakeText(m.transform, "TGT", new Vector3(0f, 0.85f, 0f), 4f, TextRed,
                TextAlignmentOptions.Center, new Vector2(4f, 1f), new Vector2(0.5f, 0.5f), font, textMat, "TGT");
            tgt.gameObject.SetActive(false);
            TextMeshPro info = MakeText(m.transform, "Info", new Vector3(0f, -0.85f, 0f), 3.2f, TextGreen,
                TextAlignmentOptions.Center, new Vector2(6f, 1f), new Vector2(0.5f, 0.5f), font, textMat, "VET  4.2km");
            info.gameObject.SetActive(false);

            // 체력 바(정보 글자 바로 아래, 폭 1.2): 채움은 왼쪽 끝 기준점의 X 스케일로 줄인다.
            GameObject bar = new GameObject("HealthBar");
            bar.transform.SetParent(m.transform, false);
            bar.transform.localPosition = new Vector3(0f, -1.22f, 0f);
            MakeQuad(bar.transform, "Back", Vector3.zero, new Vector3(BarWidth + 0.04f, BarHeight + 0.04f, 1f), quadMesh, barBackMat);
            GameObject fillPivot = new GameObject("Fill");
            fillPivot.transform.SetParent(bar.transform, false);
            fillPivot.transform.localPosition = new Vector3(-BarWidth * 0.5f, 0f, 0f);
            MeshRenderer fillRenderer = MakeQuad(fillPivot.transform, "Bar", new Vector3(BarWidth * 0.5f, 0f, 0f),
                new Vector3(BarWidth, BarHeight, 1f), quadMesh, barHighMat);
            bar.SetActive(false);

            m.SetActive(false);
            markerRoots[i] = m.transform;
            frames[i] = mf;
            frameRenderers[i] = mr;
            tgtLabels[i] = tgt;
            infoLabels[i] = info;
            healthBars[i] = bar.transform;
            healthFills[i] = fillPivot.transform;
            healthFillRenderers[i] = fillRenderer;
        }

        markers.AirMesh = airMesh;
        markers.GroundMesh = groundMesh;
        markers.AirMaterial = airMat;
        markers.GroundMaterial = groundMat;
        markers.ObjectiveMaterial = objectiveMat;
        markers.FriendlyMaterial = friendlyMat;
        markers.WaypointMaterial = waypointMat;
        markers.DestinationMaterial = destinationMat;
        markers.Markers = markerRoots;
        markers.Frames = frames;
        markers.FrameRenderers = frameRenderers;
        markers.TgtLabels = tgtLabels;
        markers.InfoLabels = infoLabels;
        markers.HealthHighMaterial = barHighMat;
        markers.HealthMidMaterial = barMidMat;
        markers.HealthLowMaterial = barLowMat;
        markers.HealthBars = healthBars;
        markers.HealthFills = healthFills;
        markers.HealthFillRenderers = healthFillRenderers;

        // 락온 표시: 진행 마름모 하나(사각 테두리를 45° 돌려 쓴다) + 조준 중/락온 머티리얼.
        markers.LockingMaterial = SaveMaterial(markerShader, LockingGreen, "ACO_MarkerLocking");
        markers.LockedMaterial = SaveMaterial(markerShader, LockedRed, "ACO_MarkerLocked");
        MakeLockDiamonds(root.transform, markers);
        EnsureLockVisuals(markers);   // 특수무장용 십자 메시
        EnsureCrosshair(markers);
        EnsureRwr(markers);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_HUD.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ACO] HUD 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 씬에 하나 두고 ACO → Bake NPC References로 생성기/관리자를 연결하세요.");
        Selection.activeObject = prefab;
    }

    /// 작전판(미션 로그/디브리핑 보드) 프리팹. 4m x 2.5m 패널, 앞면은 루트의 +Z(파란 화살표) 쪽 — 화살표가 관객을 보게 놓는다.
    /// 월드에 놓는 판이라 글자는 일반 TMP 머티리얼(벽 뒤에선 가려짐)을 쓴다.
    public static void CreateMissionBoardPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Generated");
        EnsureFolder("Assets/ACO_Addon", "Prefabs");

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath} — Window → TextMeshPro → Import TMP Essential Resources를 먼저 실행하세요.");
            return;
        }
        Shader bgShader = Shader.Find("Unlit/Color");
        if (bgShader == null)
        {
            Debug.LogError("[ACO] 셰이더를 찾지 못했습니다(Unlit/Color).");
            return;
        }
        var bgMat = new Material(bgShader) { name = "ACO_BoardBackground" };
        bgMat.SetColor("_Color", new Color(0.02f, 0.05f, 0.04f, 1f));
        bgMat = SaveAsset(bgMat, $"{GeneratedDir}/ACO_BoardBackground.mat");

        GameObject root = new GameObject("ACO_MissionBoard");
        ACO_MissionLog log = root.AddUdonSharpComponent<ACO_MissionLog>();

        // TMP 글자는 -Z 쪽에서 읽히므로 앞면을 180° 돌려 루트 +Z에서 읽히게 한다.
        GameObject face = new GameObject("Face");
        face.transform.SetParent(root.transform, false);
        face.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Background";
        Object.DestroyImmediate(bg.GetComponent<Collider>());
        bg.transform.SetParent(face.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        bg.transform.localScale = new Vector3(4f, 2.5f, 1f);
        MeshRenderer bgr = bg.GetComponent<MeshRenderer>();
        bgr.sharedMaterial = bgMat;
        bgr.shadowCastingMode = ShadowCastingMode.Off;
        bgr.receiveShadows = false;

        // 글자: 한 단위 = 0.05m, 판은 80 x 50 단위.
        GameObject texts = new GameObject("Texts");
        texts.transform.SetParent(face.transform, false);
        texts.transform.localScale = Vector3.one * 0.05f;

        Material textMat = font.material;
        TextMeshPro header = MakeText(texts.transform, "HeaderText", new Vector3(-38f, 23.5f, 0f), 26f, TextGreen,
            TextAlignmentOptions.TopLeft, new Vector2(76f, 8f), new Vector2(0f, 1f), font, textMat, "OPERATION LOG\n<size=60%>AWAITING MISSION</size>");
        TextMeshPro logText = MakeText(texts.transform, "LogText", new Vector3(-38f, 14.5f, 0f), 13f, TextGreen,
            TextAlignmentOptions.TopLeft, new Vector2(50f, 38f), new Vector2(0f, 1f), font, textMat, "TIME<pos=13%>GRID<pos=27%>PILOT");
        TextMeshPro pilotText = MakeText(texts.transform, "PilotText", new Vector3(15f, 14.5f, 0f), 13f, TextGreen,
            TextAlignmentOptions.TopLeft, new Vector2(23f, 38f), new Vector2(0f, 1f), font, textMat, "PILOT<pos=58%>KILLS");

        log.HeaderText = header;
        log.LogText = logText;
        log.PilotText = pilotText;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_MissionBoard.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ACO] 작전판 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 씬(브리핑 룸 등)에 두고 ACO → Bake NPC References로 관리자/생성기를 연결하세요.");
        Selection.activeObject = prefab;
    }

    /// 작전 콘솔 글자 이름 — Bake가 이 이름으로 찾아 ACO_GameFlow의 BannerTexts/PhaseTexts/PhaseInfoTexts/TerrainTexts에 넣는다.
    public const string FlowBannerName = "ACO_FlowBanner";
    public const string FlowPhasesName = "ACO_FlowPhases";
    public const string FlowPhaseInfoName = "ACO_FlowPhaseInfo";
    public const string FlowTerrainName = "ACO_FlowTerrain";
    private const int ConsoleSlotCards = 8;

    /// 작전 콘솔 프리팹(월드 공간 Canvas 2.4 x 1.5m, 앞면은 루트 +Z 쪽).
    ///  위 = 상태 배너(큰 상태 + 내 다음 할 일 + 거절 안내), 왼쪽 = 작전 카드(공용 설정: 페이즈 −/+ · 예상 시간 · 지형 · NEW TERRAIN · 바꾼 사람),
    ///  오른쪽 = 명단 칸 카드 8장(2열 — 기체 수보다 많은 카드는 게임 진행이 숨긴다, 빈 칸을 누르면 그 칸으로 참가),
    ///  아래 = 내 행동 버튼(JOIN → READY → LAUNCH) + LEAVE. 글자·버튼 상태는 모두 게임 진행이 쓴다.
    public static void CreateMissionConsolePrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath} — Window → TextMeshPro → Import TMP Essential Resources를 먼저 실행하세요.");
            return;
        }

        GameObject root = new GameObject("ACO_MissionConsole");
        RectTransform canvas = MakeUiPanel(root.transform, 2400f, 1500f);

        // 상태 배너
        MakeUiBox(canvas, "BannerBox", new Vector2(0f, 590f), new Vector2(2300f, 240f), new Color(0.03f, 0.17f, 0.1f, 1f));
        TextMeshProUGUI banner = MakeUiText(canvas, FlowBannerName, new Vector2(0f, 590f), new Vector2(2300f, 240f), 70f, font, "BRIEFING");
        banner.alignment = TextAlignmentOptions.Left;
        banner.margin = new Vector4(50f, 10f, 50f, 10f);

        // 작전 카드(공용 설정)
        const float lx = -640f;
        MakeUiBox(canvas, "OperationBox", new Vector2(lx, -20f), new Vector2(1020f, 900f), new Color(0.04f, 0.08f, 0.06f, 1f));
        MakeUiLabel(canvas, "OperationTitle", new Vector2(lx, 390f), new Vector2(960f, 60f), 36f, font, "OPERATION - SHARED BY THE SQUAD");
        MakeUiLabel(canvas, "PhasesTitle", new Vector2(lx, 310f), new Vector2(960f, 50f), 34f, font, "PHASES");
        MakeFlowButton(canvas, "PhasesDown", new Vector2(lx - 290f, 190f), new Vector2(170f, 170f), font, "-", ACO_GameFlow.ACTION_PHASES_DOWN, 110f);
        MakeUiText(canvas, FlowPhasesName, new Vector2(lx, 190f), new Vector2(320f, 190f), 150f, font, "3");
        MakeFlowButton(canvas, "PhasesUp", new Vector2(lx + 290f, 190f), new Vector2(170f, 170f), font, "+", ACO_GameFlow.ACTION_PHASES_UP, 110f);
        MakeUiText(canvas, FlowPhaseInfoName, new Vector2(lx, 30f), new Vector2(960f, 120f), 34f, font, "ABOUT 15 MIN");
        MakeUiLabel(canvas, "TerrainTitle", new Vector2(lx, -80f), new Vector2(960f, 50f), 34f, font, "TERRAIN");
        MakeUiText(canvas, FlowTerrainName, new Vector2(lx, -180f), new Vector2(960f, 150f), 64f, font, "-");
        MakeFlowButton(canvas, "Reroll", new Vector2(lx, -360f), new Vector2(720f, 130f), font, "NEW TERRAIN", ACO_GameFlow.ACTION_REROLL, 48f);

        // 명단 칸 카드
        const float rx = 560f;
        MakeUiBox(canvas, "RosterBox", new Vector2(rx, -20f), new Vector2(1240f, 900f), new Color(0.04f, 0.08f, 0.06f, 1f));
        MakeUiLabel(canvas, "RosterTitle", new Vector2(rx, 390f), new Vector2(1180f, 60f), 36f, font, "ROSTER - TAP AN OPEN SLOT TO JOIN");
        for (int k = 0; k < ConsoleSlotCards; k++)
        {
            int col = k % 2, row = k / 2;
            var pos = new Vector2(rx + (col == 0 ? -305f : 305f), 265f - row * 185f);
            Image img = MakeUiButton(canvas, $"Slot_{k}", pos, new Vector2(590f, 170f), font, $"{k + 1}. OPEN", 44f);
            ACO_FlowButton fb = img.gameObject.AddUdonSharpComponent<ACO_FlowButton>();
            fb.Action = ACO_GameFlow.ACTION_JOIN_SLOT;
            fb.Slot = k;
            fb.Label = img.GetComponentInChildren<TextMeshProUGUI>();
            fb.Label.lineSpacing = -20f;
            fb.Background = img;
            WireClick(img.GetComponent<Button>(), fb);
        }

        // 내 행동 + LEAVE
        MakeFlowButton(canvas, "Primary", new Vector2(-300f, -620f), new Vector2(1700f, 180f), font, "JOIN", ACO_GameFlow.ACTION_PRIMARY, 76f);
        MakeFlowButton(canvas, "Leave", new Vector2(900f, -620f), new Vector2(500f, 180f), font, "LEAVE", ACO_GameFlow.ACTION_LEAVE, 50f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_MissionConsole.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ACO] 작전 콘솔 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 씬에 두고 ACO → Bake NPC References로 관리자를 연결하세요.");
        Selection.activeObject = prefab;
    }

    /// 격납고 프리팹: 왼쪽 = 특수무장 선택/해금(ACO_Loadout), 오른쪽 = 강화 상점(ACO_Shop), 위 = 보유 크레딧과 지난 미션 보상.
    /// 무장 목록(ACO_Loadout 기본값)만큼 무장 버튼, 강화 항목(ACO_PlayerProgress.UPGRADE_COUNT)만큼 강화 버튼을 만든다.
    /// 판 2.8m x 1.5m(월드 공간 Canvas), 앞면은 루트 +Z 쪽. 무장을 더하면 ACO_Loadout의 목록 기본값을 늘리고 이 프리팹을 다시 만든다.
    /// 진행도(ACO_PlayerProgress)는 씬에 따로 하나 둔다(ACO → Create Player Progress) — Bake가 연결한다.
    public static void CreateHangarPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath} — Window → TextMeshPro → Import TMP Essential Resources를 먼저 실행하세요.");
            return;
        }

        GameObject root = new GameObject("ACO_Hangar");
        ACO_Loadout loadout = root.AddUdonSharpComponent<ACO_Loadout>();
        string[] names = loadout.SpecialNames != null && loadout.SpecialNames.Length > 0 ? loadout.SpecialNames : loadout.SpecialIds;
        int count = names != null ? names.Length : 0;
        const int pageSize = 5;   // 목록 줄 수(한 분류 화면)

        ACO_Shop shop = root.AddUdonSharpComponent<ACO_Shop>();

        const float left = -700f;
        const float right = 700f;
        RectTransform canvas = MakeUiPanel(root.transform, 2800f, 1900f);
        MakeUiText(canvas, "Title", new Vector2(0f, 660f), new Vector2(2600f, 120f), 90f, font, "HANGAR");
        TextMeshProUGUI credits = MakeUiText(canvas, "Credits", new Vector2(0f, 535f), new Vector2(2600f, 140f), 56f, font, "CREDITS -");
        MakeIcon(canvas, "CreditsIcon", LoadIcon("credits"), new Vector2(-560f, 560f), 90f);

        // 왼쪽: 특수무장
        MakeUiText(canvas, "SpecialHeader", new Vector2(left, 410f), new Vector2(1300f, 80f), 50f, font, "SPECIAL WEAPON");
        TextMeshProUGUI selection = MakeUiText(canvas, "Selection", new Vector2(left, 250f), new Vector2(1300f, 230f), 44f, font, "SPECIAL WEAPON: -");
        // 분류 넘김: < 분류 이름 > (공대공 → 공대지 → 폭탄 → 기타)
        Image prev = MakeUiButton(canvas, "PrevPage", new Vector2(left - 540f, 80f), new Vector2(160f, 90f), font, "<", 60f);
        Image next = MakeUiButton(canvas, "NextPage", new Vector2(left + 540f, 80f), new Vector2(160f, 90f), font, ">", 60f);
        TextMeshProUGUI pageText = MakeUiText(canvas, "PageText", new Vector2(left, 80f), new Vector2(900f, 90f), 46f, font, "AIR-TO-AIR");
        pageText.color = new Color(1f, 0.85f, 0.35f, 1f);
        // 목록: 줄마다 왼쪽 아이콘 + 오른쪽 이름·상태(EQUIPPED / OWNED / 가격) + 잠긴 무장 자물쇠. 글자·아이콘은 게임 중 ACO_Loadout이 분류에 맞게 쓴다.
        var images = new Image[pageSize];
        var labels = new TextMeshProUGUI[pageSize];
        var icons = new Image[pageSize];
        var locks = new GameObject[pageSize];
        Sprite lockSprite = LoadIcon("lock");
        for (int k = 0; k < pageSize; k++)
        {
            float y = -10f - k * 100f;
            string name = k < count ? names[k] : "-";
            Image img = MakeUiButton(canvas, $"Button_{k}", new Vector2(left, y), new Vector2(1240f, 92f), font, name, 42f);
            TextMeshProUGUI label = img.GetComponentInChildren<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Left;
            label.margin = new Vector4(130f, 0f, 70f, 0f);
            string id = loadout.SpecialIds != null && k < loadout.SpecialIds.Length ? loadout.SpecialIds[k] : "";
            Image icon = MakeIcon(img.rectTransform, "Icon", k < count ? SpecialIcon(id) : null, new Vector2(-560f, 0f), 80f);
            Image lockIcon = MakeIcon(img.rectTransform, "Lock", lockSprite, new Vector2(585f, 0f), 46f);
            lockIcon.color = new Color(1f, 0.85f, 0.3f, 0.95f);
            labels[k] = label;
            icons[k] = icon;
            locks[k] = lockIcon.gameObject;
            if (k >= count) { img.gameObject.SetActive(false); }
            ACO_HangarButton hb = img.gameObject.AddUdonSharpComponent<ACO_HangarButton>();
            hb.Loadout = loadout;
            hb.Action = ACO_HangarButton.ACTION_SELECT_SPECIAL;
            hb.Index = k;
            WireClick(img.GetComponent<Button>(), hb);
            images[k] = img;
        }
        TextMeshProUGUI empty = MakeUiText(canvas, "EmptyText", new Vector2(left, -160f), new Vector2(1200f, 100f), 40f, font, "NO WEAPONS IN THIS CATEGORY YET");
        empty.color = new Color(0.56f, 0.66f, 0.6f, 1f);
        empty.gameObject.SetActive(false);
        loadout.EmptyText = empty.gameObject;
        foreach (Image pager in new[] { prev, next })
        {
            ACO_HangarButton pb = pager.gameObject.AddUdonSharpComponent<ACO_HangarButton>();
            pb.Loadout = loadout;
            pb.Action = ACO_HangarButton.ACTION_SPECIAL_PAGE;
            pb.Index = pager == prev ? -1 : 1;
            WireClick(pager.GetComponent<Button>(), pb);
        }
        Image buy = MakeUiButton(canvas, "BuySpecial", new Vector2(left, -540f), new Vector2(800f, 100f), font, "BUY", 48f);
        buy.color = new Color(0.45f, 0.4f, 0.12f, 1f);
        ACO_HangarButton buyButton = buy.gameObject.AddUdonSharpComponent<ACO_HangarButton>();
        buyButton.Loadout = loadout;
        buyButton.Action = ACO_HangarButton.ACTION_BUY_SPECIAL;
        WireClick(buy.GetComponent<Button>(), buyButton);
        MakeUiText(canvas, "SpecialHint", new Vector2(left, -615f), new Vector2(1300f, 40f), 28f, font, "APPLIED WHEN YOU ENTER THE COCKPIT");

        loadout.SelectionText = selection;
        loadout.ButtonImages = images;
        loadout.ButtonLabels = labels;
        loadout.ButtonIcons = icons;
        loadout.LockIcons = locks;
        loadout.PageText = pageText;
        loadout.PagerObjects = new[] { prev.gameObject, next.gameObject };
        var specialIcons = new Sprite[loadout.SpecialIds != null ? loadout.SpecialIds.Length : 0];
        for (int i = 0; i < specialIcons.Length; i++) { specialIcons[i] = SpecialIcon(loadout.SpecialIds[i]); }
        loadout.SpecialIcons = specialIcons;
        loadout.BuyButton = buy.gameObject;
        loadout.BuyText = buy.GetComponentInChildren<TextMeshProUGUI>();

        // 오른쪽: 강화 상점 — 분류 탭(CAT_*) + 강화 줄(분류 중 가장 긴 것만큼, 게임 중 상점이 탭에 맞게 글자를 쓰고 남는 줄을 숨긴다)
        MakeUiText(canvas, "UpgradeHeader", new Vector2(right, 410f), new Vector2(1300f, 80f), 50f, font, "UPGRADES");
        // 기본 이름(프리팹 글자 미리보기용)
        var defaultsGo = new GameObject("ACO_ProgressDefaults");
        ACO_PlayerProgress defaults = defaultsGo.AddComponent<ACO_PlayerProgress>();
        string[] upgradeNames = defaults.UpgradeNames;
        string[] categoryNames = defaults.CategoryNames;
        int rowCount = 0;
        for (int c = 0; c < ACO_PlayerProgress.CATEGORY_COUNT; c++) { rowCount = Mathf.Max(rowCount, defaults.GetCategoryLength(c)); }
        rowCount = Mathf.Max(rowCount, ACO_PlayerProgress.CONSUMABLE_COUNT);   // SUPPLY 탭
        shop.UpgradeIcons = BuildUpgradeIcons(defaults);
        shop.ConsumableIcons = new[] { LoadIcon("hull"), LoadIcon("cm_pod"), LoadIcon("ammo"), LoadIcon("fuel") };
        Object.DestroyImmediate(defaultsGo);

        // 탭: 강화 분류 + 마지막 SUPPLY(소모품). 패널 폭(1300)에 맞춰 나눈다.
        int tabCount = ACO_PlayerProgress.CATEGORY_COUNT + 1;
        float tabStep = 1300f / tabCount;
        var tabImages = new Image[tabCount];
        for (int c = 0; c < tabCount; c++)
        {
            string label = c == ACO_Shop.TAB_SUPPLY ? "SUPPLY" : (categoryNames != null && c < categoryNames.Length ? categoryNames[c] : $"TAB {c}");
            float x = right + (c - (tabCount - 1) * 0.5f) * tabStep;
            Image img = MakeUiButton(canvas, $"Tab_{c}", new Vector2(x, 320f), new Vector2(tabStep - 10f, 100f), font, label, 26f);
            Sprite tabIcon = c < TabIconNames.Length ? LoadIcon(TabIconNames[c]) : null;
            if (tabIcon != null)
            {
                img.GetComponentInChildren<TextMeshProUGUI>().margin = new Vector4(56f, 0f, 4f, 0f);
                MakeIcon(img.rectTransform, "Icon", tabIcon, new Vector2(-(tabStep - 10f) * 0.5f + 34f, 0f), 54f);
            }
            ACO_HangarButton hb = img.gameObject.AddUdonSharpComponent<ACO_HangarButton>();
            hb.Shop = shop;
            hb.Action = ACO_HangarButton.ACTION_SHOP_TAB;
            hb.Index = c;
            WireClick(img.GetComponent<Button>(), hb);
            tabImages[c] = img;
        }

        var rowObjects = new GameObject[rowCount];
        var rowTexts = new TextMeshProUGUI[rowCount];
        var rowImages = new Image[rowCount];
        var rowIcons = new Image[rowCount];
        for (int row = 0; row < rowCount; row++)
        {
            string label = upgradeNames != null && row < upgradeNames.Length ? upgradeNames[row] : $"UPGRADE {row}";
            Image img = MakeUiButton(canvas, $"Upgrade_{row}", new Vector2(right, 190f - row * 110f), new Vector2(1300f, 96f), font, label, 40f);
            TextMeshProUGUI t = img.GetComponentInChildren<TextMeshProUGUI>();
            t.alignment = TextAlignmentOptions.Left;
            t.margin = new Vector4(110f, 0f, 30f, 0f);
            rowIcons[row] = MakeIcon(img.rectTransform, "Icon", null, new Vector2(-590f, 0f), 76f);
            ACO_HangarButton hb = img.gameObject.AddUdonSharpComponent<ACO_HangarButton>();
            hb.Shop = shop;
            hb.Action = ACO_HangarButton.ACTION_BUY_UPGRADE;
            hb.Index = row;
            WireClick(img.GetComponent<Button>(), hb);
            rowObjects[row] = img.gameObject;
            rowTexts[row] = t;
            rowImages[row] = img;
        }
        TextMeshProUGUI message = MakeUiText(canvas, "UpgradeMessage", new Vector2(right, -600f), new Vector2(1300f, 80f), 38f, font,
            "UPGRADES APPLY ON YOUR NEXT SORTIE");

        shop.CreditsText = credits;
        shop.TabImages = tabImages;
        shop.RowObjects = rowObjects;
        shop.RowTexts = rowTexts;
        shop.RowImages = rowImages;
        shop.RowIcons = rowIcons;
        shop.MessageText = message;

        // 아래: 출격 — 명단·준비 상태(게임 진행이 글자를 쓴다) + READY(미션 전) + LAUNCH(미션 중 재출격)
        MakeFlowStatus(canvas, new Vector2(left, -790f), new Vector2(1300f, 300f), 34f, font);
        MakeFlowButton(canvas, "Ready", new Vector2(450f, -790f), new Vector2(500f, 170f), font, "READY", ACO_GameFlow.ACTION_TOGGLE_READY);
        MakeFlowButton(canvas, "Launch", new Vector2(1000f, -790f), new Vector2(500f, 170f), font, "LAUNCH", ACO_GameFlow.ACTION_LAUNCH);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_Hangar.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ACO] 격납고 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 씬에 두면 Bake가 기체의 특수무장 슬롯에 연결합니다.");
        Selection.activeObject = prefab;
    }

    /// 게임 진행 상태 글자. Bake가 이 이름(FlowStatusName)의 글자를 찾아 ACO_GameFlow.StatusTexts에 넣는다.
    public const string FlowStatusName = "ACO_FlowStatus";

    private static void MakeFlowStatus(RectTransform canvas, Vector2 pos, Vector2 size, float fontSize, TMP_FontAsset font)
    {
        TextMeshProUGUI t = MakeUiText(canvas, FlowStatusName, pos, size, fontSize, font, "SORTIE ROSTER");
        t.alignment = TextAlignmentOptions.TopLeft;
        t.margin = new Vector4(30f, 10f, 30f, 10f);
    }

    /// 출격 카운트다운 판. Bake가 상태 글자가 있는 패널마다 만들고 ACO_GameFlow.CountdownOverlays/CountdownTexts에 넣는다.
    public const string FlowCountdownName = "ACO_FlowCountdown";

    /// 패널 전체를 덮는 반투명 판 + 가운데 큰 글자(처음엔 꺼 둔다, 클릭은 막지 않는다 — 아래 버튼이 거절 안내를 띄운다).
    /// 이미 있으면 그것을 돌려준다. 항상 맨 위(마지막 자식)에 둔다.
    public static GameObject EnsureFlowCountdown(RectTransform canvas)
    {
        Transform old = canvas.Find(FlowCountdownName);
        if (old != null) { old.SetAsLastSibling(); return old.gameObject; }
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) { return null; }

        var go = new GameObject(FlowCountdownName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "ACO countdown overlay");
        go.layer = canvas.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(canvas, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = go.AddComponent<Image>();
        img.color = new Color(0f, 0.04f, 0.02f, 0.72f);
        img.raycastTarget = false;

        TextMeshProUGUI t = MakeUiText(rt, "Text", Vector2.zero, Vector2.zero, 400f, font, "10");
        var trt = (RectTransform)t.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(40f, 40f);
        trt.offsetMax = new Vector2(-40f, -40f);
        t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = true;
        t.fontSizeMin = 40f;
        t.fontSizeMax = 600f;
        t.color = new Color(0.45f, 1f, 0.55f, 1f);
        rt.SetAsLastSibling();
        go.SetActive(false);
        return go;
    }

    /// 게임 진행 버튼(JOIN/READY/LAUNCH 등). 게임 진행은 씬 오브젝트라 Bake가 연결한다.
    private static void MakeFlowButton(RectTransform canvas, string name, Vector2 pos, Vector2 size, TMP_FontAsset font, string label, int action, float fontSize = 56f)
    {
        Image img = MakeUiButton(canvas, name, pos, size, font, label, fontSize);
        ACO_FlowButton fb = img.gameObject.AddUdonSharpComponent<ACO_FlowButton>();
        fb.Action = action;
        fb.Label = img.GetComponentInChildren<TextMeshProUGUI>();
        fb.Background = img;
        WireClick(img.GetComponent<Button>(), fb);
    }

    private static readonly Color PanelBackground = new Color(0.02f, 0.05f, 0.04f, 0.92f);
    private static readonly Color ButtonGreen = new Color(0.08f, 0.3f, 0.14f, 1f);

    /// VRChat 월드 UI 패널: 월드 공간 Canvas(Default 레이어, 1단위 = 1mm) + VRCUiShape(레이저로 누를 수 있게) + 배경.
    /// Canvas는 -Z 쪽에서 보이므로 180° 돌려 루트 +Z 쪽이 앞면이 되게 한다(예전 콘솔과 같은 방향).
    private static RectTransform MakeUiPanel(Transform root, float width, float height)
    {
        GameObject go = new GameObject("Canvas", typeof(RectTransform));
        go.layer = 0;
        go.transform.SetParent(root, false);
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();
        go.AddComponent<VRC.SDK3.Components.VRCUiShape>();
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);
        rt.localScale = Vector3.one * 0.001f;

        GameObject bg = new GameObject("Background", typeof(RectTransform));
        bg.layer = 0;
        RectTransform bgr = bg.GetComponent<RectTransform>();
        bgr.SetParent(rt, false);
        bgr.anchorMin = Vector2.zero;
        bgr.anchorMax = Vector2.one;
        bgr.offsetMin = Vector2.zero;
        bgr.offsetMax = Vector2.zero;
        Image img = bg.AddComponent<Image>();
        img.color = PanelBackground;
        img.raycastTarget = false;
        return rt;
    }

    /// 카드 배경(누를 수 없는 단색 판).
    private static Image MakeUiBox(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 0;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// 카드 안 작은 제목(흐린 색).
    private static TextMeshProUGUI MakeUiLabel(RectTransform parent, string name, Vector2 pos, Vector2 size, float fontSize, TMP_FontAsset font, string text)
    {
        TextMeshProUGUI t = MakeUiText(parent, name, pos, size, fontSize, font, text);
        t.color = new Color(0.56f, 0.66f, 0.6f, 1f);
        return t;
    }

    private static TextMeshProUGUI MakeUiText(RectTransform parent, string name, Vector2 pos, Vector2 size, float fontSize, TMP_FontAsset font, string text)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 0;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.font = font;
        t.fontSize = fontSize;
        t.color = TextGreen;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.richText = true;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    /// UI 버튼(배경 Image + Button + 글자). 반환: 배경 Image(선택 표시 색을 바꿀 때 쓴다).
    private static Image MakeUiButton(RectTransform parent, string name, Vector2 pos, Vector2 size, TMP_FontAsset font, string label, float fontSize)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 0;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.color = ButtonGreen;
        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        var nav = b.navigation;
        nav.mode = Navigation.Mode.None;
        b.navigation = nav;

        TextMeshProUGUI t = MakeUiText(rt, "Label", Vector2.zero, size, fontSize, font, label);
        t.rectTransform.anchorMin = Vector2.zero;
        t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = Vector2.zero;
        t.rectTransform.offsetMax = Vector2.zero;
        return img;
    }

    /// 버튼을 누르면 그 U# 스크립트의 Click()이 불리게 한다(UdonBehaviour.SendCustomEvent("Click") — VRChat UI 이벤트 허용 목록에 있다).
    private static void WireClick(Button button, UdonSharp.UdonSharpBehaviour target)
    {
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
        if (udon == null) { Debug.LogError($"[ACO] '{target.name}'의 UdonBehaviour를 찾지 못해 버튼을 연결하지 못했습니다.", target); return; }
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(button.onClick, udon.SendCustomEvent, "Click");
    }

    /// 정n각형 테두리(링) 메시. XY 평면, 중심 원점. 두께는 변에서 안쪽으로 FrameThickness.
    private static Mesh BuildRingMesh(int sides, float outerRadius, float startAngleDeg, string name)
    {
        // 변까지의 거리(아포템) 기준으로 두께를 빼야 모든 변 두께가 같다.
        float apothem = outerRadius * Mathf.Cos(Mathf.PI / sides);
        float innerRadius = outerRadius * (apothem - FrameThickness) / apothem;

        var verts = new Vector3[sides * 2];
        var tris = new int[sides * 6];
        for (int i = 0; i < sides; i++)
        {
            float a = (startAngleDeg + i * 360f / sides) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            verts[i * 2] = dir * outerRadius;
            verts[i * 2 + 1] = dir * innerRadius;
        }
        for (int i = 0; i < sides; i++)
        {
            int o0 = i * 2;
            int i0 = i * 2 + 1;
            int o1 = ((i + 1) % sides) * 2;
            int i1 = ((i + 1) % sides) * 2 + 1;
            int t = i * 6;
            tris[t] = o0; tris[t + 1] = o1; tris[t + 2] = i0;
            tris[t + 3] = i0; tris[t + 4] = o1; tris[t + 5] = i1;
        }

        var mesh = new Mesh { name = name, vertices = verts, triangles = tris };
        mesh.RecalculateBounds();
        // 마커는 항상 화면 안쪽에 그려지지만 컬링으로 사라지지 않게 경계를 넉넉히 둔다.
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        return mesh;
    }

    // 안내 글자 위치(결과 글자 기준, 점수 패널 단위 0.03m): 결과 글자 아래.
    private static readonly Vector3 NoticeOffset = new Vector3(0f, -7f, 0f);
    // 예전 HUD(결과·안내가 점수 패널 안)에서 결과 글자가 있던 자리.
    private static readonly Vector3 LegacyResultPos = new Vector3(9.3f, -3f, 0f);

    /// 짧은 안내 글자: 결과 글자 아래(시야 가운데보다 조금 아래). 처음엔 꺼 둔다.
    private static TextMeshPro MakeNoticeText(Transform parent, Vector3 localPos, TMP_FontAsset font, Material mat)
    {
        TextMeshPro t = MakeText(parent, "NoticeText", localPos, 9f, new Color(1f, 0.85f, 0.3f, 0.9f),
            TextAlignmentOptions.Center, new Vector2(60f, 10f), new Vector2(0.5f, 0.5f), font, mat, "SEAT RECENTERING");
        t.gameObject.SetActive(false);
        return t;
    }

    /// Bake용: 점수 HUD에 안내 글자가 없으면 만든다(예전 HUD 프리팹에는 없었다). 만들었으면 true.
    public static bool EnsureNoticeText(ACO_ScoreHUD hud)
    {
        if (hud.NoticeText != null || hud.Panel == null) { return false; }
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedDir}/ACO_HUDTextOverlay.mat");
        if (font == null || mat == null) { return false; }
        // 묶음이 있으면 묶음에, 없으면 예전처럼 패널 안 결과 글자 아래에(실행 중 묶음으로 옮겨질 때 간격이 그대로 이어진다).
        hud.NoticeText = hud.ResultGroup != null
            ? MakeNoticeText(hud.ResultGroup, NoticeOffset, font, mat)
            : MakeNoticeText(hud.Panel, (hud.ResultText != null ? hud.ResultText.transform.localPosition : LegacyResultPos) + NoticeOffset, font, mat);
        EditorUtility.SetDirty(hud);
        return true;
    }

    /// Bake용: 점수 HUD에 결과·안내 묶음이 없으면 만든다(예전 HUD는 두 글자가 점수 패널 안에 있었다). 만들었으면 true.
    /// 씬의 HUD는 프리팹 인스턴스라 글자를 옮기지 못한다 — 빈 묶음만 만들고 ResultOffset을 지금 결과 글자 자리로 맞춘다(옮기기는 실행 중 ACO_ScoreHUD가).
    public static bool EnsureResultGroup(ACO_ScoreHUD hud)
    {
        if (hud.ResultGroup != null || hud.Panel == null || hud.ResultText == null) { return false; }
        Transform parent = hud.Panel.parent != null ? hud.Panel.parent : hud.transform;
        GameObject go = new GameObject("ResultGroup");
        Undo.RegisterCreatedObjectUndo(go, "ACO result group");
        go.transform.SetParent(parent, false);
        go.transform.localScale = hud.Panel.localScale;
        Undo.RecordObject(hud, "ACO result group");
        Vector3 p = hud.ResultText.transform.parent == hud.Panel ? hud.ResultText.transform.localPosition : LegacyResultPos;
        hud.ResultOffset = new Vector2(hud.HeadOffset.x + p.x * hud.Panel.localScale.x, hud.HeadOffset.y + p.y * hud.Panel.localScale.y);
        hud.ResultGroup = go.transform;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
        EditorUtility.SetDirty(hud);
        return true;
    }

    private static TextMeshPro MakeText(Transform parent, string name, Vector3 localPos, float fontSize, Color color,
        TextAlignmentOptions alignment, Vector2 size, Vector2 pivot, TMP_FontAsset font, Material mat, string text)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.font = font;
        tmp.fontSharedMaterial = mat;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = false;
        tmp.richText = true;
        tmp.rectTransform.pivot = pivot;
        tmp.rectTransform.sizeDelta = size;
        tmp.rectTransform.localPosition = localPos;
        tmp.text = text;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return tmp;
    }

    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        return SaveAsset(mesh, $"{GeneratedDir}/{name}.asset");
    }

    private static readonly Color FriendlyBlue = new Color(0.4f, 0.75f, 1f, 0.7f);
    private static readonly Color LockingGreen = new Color(0.45f, 1f, 0.5f, 0.85f);

    /// 폭탄 CCIP 표시(낙하 지점): 원 테두리 + 가운데 점. 한 변 1 크기(실행 중 ACO_DFUNC_Missile이 화면 크기에 맞춰 스케일한다).
    /// 그리기는 표적 마커와 같은 ACO/MarkerOverlay(무엇에도 가려지지 않는다). 처음엔 꺼 둔다.
    public static Transform CreateCcipReticle(Transform parent)
    {
        EnsureFolder("Assets/ACO_Addon", "Generated");
        Shader markerShader = Shader.Find("ACO/MarkerOverlay");
        if (markerShader == null)
        {
            Debug.LogError("[ACO] 셰이더를 찾지 못했습니다(ACO/MarkerOverlay) — CCIP 표시를 만들지 못했습니다.");
            return null;
        }
        Mesh ring = AssetDatabase.LoadAssetAtPath<Mesh>($"{GeneratedDir}/ACO_CcipRing.asset");
        if (ring == null) { ring = SaveMesh(BuildRingMesh(32, 0.5f, 0f, "ACO_CcipRing"), "ACO_CcipRing"); }
        Material mat = LoadOrSaveMaterial(markerShader, LockingGreen, "ACO_MarkerCcip");

        GameObject root = new GameObject("CCIP");
        Undo.RegisterCreatedObjectUndo(root, "ACO CCIP");
        root.transform.SetParent(parent, false);
        MeshFilter mf = root.AddComponent<MeshFilter>();
        mf.sharedMesh = ring;
        MeshRenderer r = root.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        MakeQuad(root.transform, "Dot", Vector3.zero, new Vector3(0.08f, 0.08f, 1f), Resources.GetBuiltinResource<Mesh>("Quad.fbx"), mat);
        root.SetActive(false);
        return root.transform;
    }
    private static readonly Color LockedRed = new Color(1f, 0.15f, 0.12f, 0.95f);

    /// Bake용: 락온 표시(마름모, 머티리얼)가 없으면 채운다(예전 HUD 프리팹에는 없었다). 바꿨으면 true.
    /// 크로스헤어(화면 가운데 작은 원)가 없으면 만든다: 지름 1의 얇은 원 테두리, 항상 맨 위(ACO/MarkerOverlay). 스크립트가 각도 크기에 맞춰 스케일한다.
    /// 마커 풀과 따로(HUD 루트 아래) 둔다. 만들었으면 true.
    public static bool EnsureCrosshair(ACO_TargetMarkers markers)
    {
        if (markers.Crosshair != null) { return false; }
        Shader markerShader = Shader.Find("ACO/MarkerOverlay");
        if (markerShader == null) { return false; }
        EnsureFolder("Assets/ACO_Addon", "Generated");
        Mesh ring = AssetDatabase.LoadAssetAtPath<Mesh>($"{GeneratedDir}/ACO_CrosshairRing.asset");
        if (ring == null) { ring = SaveMesh(BuildThinRingMesh(32, 0.5f, 0.12f, "ACO_CrosshairRing"), "ACO_CrosshairRing"); }

        var go = new GameObject("Crosshair");
        go.transform.SetParent(markers.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = ring;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = LoadOrSaveMaterial(markerShader, new Color(0.35f, 1f, 0.45f, 0.7f), "ACO_Crosshair");
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.SetActive(false);
        markers.Crosshair = go.transform;
        return true;
    }

    private static readonly Color RwrFar = new Color(0.35f, 1f, 0.45f, 0.9f);
    private static readonly Color RwrLock = new Color(0.35f, 1f, 0.45f, 0.75f);

    /// RWR(크로스헤어 둘레 위협 링)이 없으면 만든다: 표적 마커 아래 'RWR'(ACO_RWR) → Ring(쉐브론 8개, 락온 호 6개, 항상 맨 위) + 경보음 2개(3D).
    /// 경보음은 사인파 WAV를 Generated에 만들어 쓴다(바꾸려면 AudioSource의 클립만 바꾼다). 만들었으면 true.
    public static bool EnsureRwr(ACO_TargetMarkers markers)
    {
        if (markers.GetComponentInChildren<ACO_RWR>(true) != null) { return false; }
        Shader markerShader = Shader.Find("ACO/MarkerOverlay");
        if (markerShader == null) { return false; }
        EnsureFolder("Assets/ACO_Addon", "Generated");
        Mesh chevron = AssetDatabase.LoadAssetAtPath<Mesh>($"{GeneratedDir}/ACO_RwrChevron.asset");
        if (chevron == null) { chevron = SaveMesh(BuildChevronMesh("ACO_RwrChevron"), "ACO_RwrChevron"); }
        Mesh arc = AssetDatabase.LoadAssetAtPath<Mesh>($"{GeneratedDir}/ACO_RwrArc.asset");
        if (arc == null) { arc = SaveMesh(BuildArcMesh(12, 12f, 0.05f, "ACO_RwrArc"), "ACO_RwrArc"); }
        Material chevronMat = LoadOrSaveMaterial(markerShader, RwrFar, "ACO_RwrChevron");
        Material lockMat = LoadOrSaveMaterial(markerShader, RwrLock, "ACO_RwrLock");

        var go = new GameObject("RWR");
        go.transform.SetParent(markers.transform, false);
        ACO_RWR rwr = go.AddUdonSharpComponent<ACO_RWR>();
        rwr.Markers = markers;

        var ring = new GameObject("Ring");
        ring.transform.SetParent(go.transform, false);
        rwr.Ring = ring.transform;
        var chevrons = new Transform[ACO_RWR.MAX_MISSILES];
        var chevronRenderers = new MeshRenderer[ACO_RWR.MAX_MISSILES];
        for (int i = 0; i < chevrons.Length; i++)
        {
            MeshRenderer r = MakeOverlayMesh(ring.transform, $"Missile_{i}", chevron, chevronMat);
            chevrons[i] = r.transform;
            chevronRenderers[i] = r;
            r.gameObject.SetActive(false);
        }
        var arcs = new Transform[ACO_RWR.MAX_LOCKS];
        for (int i = 0; i < arcs.Length; i++)
        {
            MeshRenderer r = MakeOverlayMesh(ring.transform, $"Lock_{i}", arc, lockMat);
            arcs[i] = r.transform;
            r.gameObject.SetActive(false);
        }
        rwr.Chevrons = chevrons;
        rwr.ChevronRenderers = chevronRenderers;
        rwr.LockArcs = arcs;
        ring.SetActive(false);

        // 경보음은 Ring 밖에 둔다(Ring이 꺼져도 소리는 난다).
        rwr.WarnSource = MakeRwrAudio(go.transform, "WarnSound", EnsureToneClip("ACO_RwrMissileBeep", 1350f, 0f, 0.07f), 0.55f);
        rwr.LockSource = MakeRwrAudio(go.transform, "LockSound", EnsureToneClip("ACO_RwrLockTone", 880f, 660f, 0.16f), 0.4f);
        EditorUtility.SetDirty(rwr);
        return true;
    }

    private static MeshRenderer MakeOverlayMesh(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    private static AudioSource MakeRwrAudio(Transform parent, string name, AudioClip clip, float volume)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        AudioSource a = go.AddComponent<AudioSource>();
        a.clip = clip;
        a.playOnAwake = false;
        a.loop = false;
        a.volume = volume;
        a.spatialBlend = 1f;
        a.dopplerLevel = 0f;
        a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = 2f;
        a.maxDistance = 10f;
        return a;
    }

    /// 패널 버튼 효과음(씬에 하나)이 없으면 만든다: 'ACO_UiSound'(ACO_UiSound + 3D AudioSource) + 클릭·결정·거절 사인파 WAV. 만들었으면 true.
    public static ACO_UiSound EnsureUiSound(out bool created)
    {
        created = false;
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_UiSound found = root.GetComponentInChildren<ACO_UiSound>(true);
            if (found != null) { return found; }
        }
        EnsureFolder("Assets/ACO_Addon", "Generated");
        var go = new GameObject("ACO_UiSound");
        Undo.RegisterCreatedObjectUndo(go, "ACO UI sound");
        ACO_UiSound s = go.AddUdonSharpComponent<ACO_UiSound>();
        AudioSource a = go.AddComponent<AudioSource>();
        a.playOnAwake = false;
        a.loop = false;
        a.spatialBlend = 1f;
        a.dopplerLevel = 0f;
        a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = 2f;
        a.maxDistance = 15f;
        s.Source = a;
        s.ClickClip = EnsureToneClip("ACO_UiClick", 1800f, 0f, 0.035f);
        s.ConfirmClip = EnsureToneClip("ACO_UiConfirm", 880f, 1320f, 0.14f);
        s.DenyClip = EnsureToneClip("ACO_UiDeny", 300f, 210f, 0.2f);
        EditorUtility.SetDirty(s);
        created = true;
        return s;
    }

    /// 짧은 사인파 경보음 WAV(16bit 모노). freq2 > 0이면 뒤 절반을 그 음으로(두 음). 이미 있으면 그대로 쓴다.
    public static AudioClip EnsureToneClip(string name, float freq, float freq2, float seconds)
    {
        string path = $"{GeneratedDir}/{name}.wav";
        AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (existing != null) { return existing; }
        const int rate = 44100;
        int n = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
        using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + n * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(n * 2);
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                float f = freq2 > 0f && i >= n / 2 ? freq2 : freq;
                phase += 2.0 * System.Math.PI * f / rate;
                // 앞뒤 5ms 페이드(딸깍 소리 방지)
                float env = Mathf.Min(1f, Mathf.Min(i / (rate * 0.005f), (n - i) / (rate * 0.005f)));
                w.Write((short)(System.Math.Sin(phase) * 0.7 * env * short.MaxValue));
            }
        }
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    /// 화살촉(XY 평면, 크기 1): 꼭짓점 +Y(0, 0.5), 아래 가운데가 파인 모양 — 꼭짓점이 향하는 쪽이 진행 방향.
    private static Mesh BuildChevronMesh(string name)
    {
        var v = new Vector3[]
        {
            new Vector3(0f, 0.5f, 0f),
            new Vector3(0.45f, -0.45f, 0f),
            new Vector3(0f, -0.12f, 0f),
            new Vector3(-0.45f, -0.45f, 0f),
        };
        var t = new int[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new Mesh { name = name, vertices = v, triangles = t };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// 호(XY 평면, 반지름 1, +Y 쪽 가운데로 ±halfDeg, 두께 thickness).
    private static Mesh BuildArcMesh(int segments, float halfDeg, float thickness, string name)
    {
        var v = new Vector3[(segments + 1) * 2];
        var t = new int[segments * 6];
        float inner = 1f - thickness;
        for (int i = 0; i <= segments; i++)
        {
            float a = (90f - halfDeg + 2f * halfDeg * i / segments) * Mathf.Deg2Rad;
            v[i * 2] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v[i * 2 + 1] = new Vector3(Mathf.Cos(a) * inner, Mathf.Sin(a) * inner, 0f);
            if (i == segments) { continue; }
            int k = i * 6;
            t[k] = i * 2; t[k + 1] = i * 2 + 2; t[k + 2] = i * 2 + 1;
            t[k + 3] = i * 2 + 1; t[k + 4] = i * 2 + 2; t[k + 5] = i * 2 + 3;
        }
        var mesh = new Mesh { name = name, vertices = v, triangles = t };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// 십자(+) 메시(XY 평면, 중심 원점): 가운데 gap을 비운 팔 4개(길이 = arm − gap, 두께 2 × halfThickness). 팔이 겹치지 않아
    /// 반투명 오버레이에서도 가운데만 진해지지 않는다.
    private static Mesh BuildCrossMesh(float arm, float gap, float halfThickness, string name)
    {
        var v = new Vector3[16];
        var t = new int[24];
        float h = halfThickness;
        // 오른쪽, 왼쪽, 위, 아래 팔
        Rect(v, t, 0, new Vector2(gap, -h), new Vector2(arm, h));
        Rect(v, t, 1, new Vector2(-arm, -h), new Vector2(-gap, h));
        Rect(v, t, 2, new Vector2(-h, gap), new Vector2(h, arm));
        Rect(v, t, 3, new Vector2(-h, -arm), new Vector2(h, -gap));
        var mesh = new Mesh { name = name, vertices = v, triangles = t };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Rect(Vector3[] v, int[] t, int i, Vector2 min, Vector2 max)
    {
        int b = i * 4;
        v[b] = new Vector3(min.x, min.y, 0f);
        v[b + 1] = new Vector3(max.x, min.y, 0f);
        v[b + 2] = new Vector3(max.x, max.y, 0f);
        v[b + 3] = new Vector3(min.x, max.y, 0f);
        int k = i * 6;
        t[k] = b; t[k + 1] = b + 2; t[k + 2] = b + 1;
        t[k + 3] = b; t[k + 4] = b + 3; t[k + 5] = b + 2;
    }

    /// 원 테두리 메시(XY 평면, 중심 원점, 바깥 반지름 r, 두께 = r × thicknessRatio).
    private static Mesh BuildThinRingMesh(int sides, float r, float thicknessRatio, string name)
    {
        float inner = r * (1f - thicknessRatio);
        var v = new Vector3[sides * 2];
        var t = new int[sides * 6];
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            v[i * 2] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            v[i * 2 + 1] = new Vector3(Mathf.Cos(a) * inner, Mathf.Sin(a) * inner, 0f);
            int n = (i + 1) % sides;
            t[i * 6] = i * 2; t[i * 6 + 1] = n * 2; t[i * 6 + 2] = i * 2 + 1;
            t[i * 6 + 3] = i * 2 + 1; t[i * 6 + 4] = n * 2; t[i * 6 + 5] = n * 2 + 1;
        }
        var mesh = new Mesh { name = name, vertices = v, triangles = t };
        mesh.RecalculateBounds();
        return mesh;
    }

    private const string IconDir = "Assets/ACO_Addon/Textures/Icons";
    private static readonly Color IconColor = new Color(0.8f, 1f, 0.85f, 0.95f);

    /// Textures/Icons/aco_icon_{name}.png를 UI Sprite로 가져오게 설정하고 불러온다(흰색 선 + 알파 — Image 색으로 물든다). 없으면 null.
    /// 월드 공간 패널을 멀리서 보므로 밉맵을 켠다. 아이콘은 Tools/aco_icons.py로 다시 만들 수 있다.
    public static Sprite LoadIcon(string name)
    {
        string path = $"{IconDir}/aco_icon_{name}.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null && System.IO.File.Exists(path))
        {
            AssetDatabase.ImportAsset(path);   // 방금 만든 파일이면 아직 가져오지 않았다
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
        }
        if (importer == null) { Debug.LogWarning($"[ACO] 아이콘을 찾지 못했습니다: {path}"); return null; }
        if (importer.textureType != TextureImporterType.Sprite || !importer.mipmapEnabled || !importer.alphaIsTransparency)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// 아이콘 Image(누르기는 뒤 버튼이 받는다 — raycastTarget 끔).
    private static Image MakeIcon(RectTransform parent, string name, Sprite sprite, Vector2 pos, float size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 0;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(size, size);
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = IconColor;
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.enabled = sprite != null;
        return img;
    }

    /// 강화 ID → 아이콘 이름: 기체 5개는 항목별, 미사일/특수무장 계열은 능력치(STAT_*)별.
    private static Sprite[] BuildUpgradeIcons(ACO_PlayerProgress p)
    {
        var icons = new Sprite[ACO_PlayerProgress.UPGRADE_COUNT];
        string[] airframe = { "hull", "mobility", "engine", "fuel", "stability" };
        int[] airIds = { ACO_PlayerProgress.UP_HULL, ACO_PlayerProgress.UP_MOBILITY, ACO_PlayerProgress.UP_ENGINE, ACO_PlayerProgress.UP_FUEL, ACO_PlayerProgress.UP_STABILITY };
        for (int i = 0; i < airIds.Length; i++) { if (airIds[i] < icons.Length) { icons[airIds[i]] = LoadIcon(airframe[i]); } }
        string[] stats = { "damage", "ammo", "turn", "lock_speed", "lock_range", "lock_angle", "blast", "reload" };
        for (int f = ACO_PlayerProgress.FAMILY_MISSILE; f <= ACO_PlayerProgress.FAMILY_SP_BOMB; f++)
        {
            for (int s = 0; s < stats.Length; s++)
            {
                int id = p.GetFamilyUpgradeId(f, s);
                if (id >= 0 && id < icons.Length) { icons[id] = LoadIcon(stats[s]); }
            }
        }
        return icons;
    }

    private static readonly string[] TabIconNames = { "tab_airframe", "tab_missile", "tab_sp_aam", "tab_sp_agm", "tab_sp_bomb", "tab_supply" };

    /// 특수무장 ID → 아이콘: Textures/Icons/aco_icon_weapon_<id 소문자>.png, 없으면 일반 미사일 그림.
    public static Sprite SpecialIcon(string id)
    {
        string key = (id ?? "").ToLowerInvariant();
        if (key.Length > 0 && System.IO.File.Exists($"{IconDir}/aco_icon_weapon_{key}.png")) { return LoadIcon("weapon_" + key); }
        return LoadIcon("tab_missile");
    }

    // HUD 설정 패널: 분류 카드(제목 + 줄 — 항목 번호는 저장 ID 순서라 그대로 두고 보이는 묶음만 정한다). 열마다 카드를 위에서 아래로 쌓는다.
    private struct HudCategory
    {
        public int Column;
        public string Title;
        public int[] Items;
        public string[] Labels;
        public HudCategory(int column, string title, int[] items, string[] labels) { Column = column; Title = title; Items = items; Labels = labels; }
    }

    private static readonly HudCategory[] HudCategories =
    {
        new HudCategory(0, "SCORE PANEL",
            new[] { ACO_HudSettings.ITEM_PANEL_X, ACO_HudSettings.ITEM_PANEL_Y, ACO_HudSettings.ITEM_PANEL_DISTANCE, ACO_HudSettings.ITEM_PANEL_SCALE },
            new[] { "LEFT / RIGHT", "UP / DOWN", "DISTANCE", "SIZE" }),
        new HudCategory(0, "RESULT / NOTICE TEXT",
            new[] { ACO_HudSettings.ITEM_RESULT_X, ACO_HudSettings.ITEM_RESULT_Y, ACO_HudSettings.ITEM_RESULT_SCALE },
            new[] { "LEFT / RIGHT", "UP / DOWN", "SIZE" }),
        new HudCategory(1, "RADIO SUBTITLES",
            new[] { ACO_HudSettings.ITEM_CHATTER_X, ACO_HudSettings.ITEM_CHATTER_Y, ACO_HudSettings.ITEM_CHATTER_SCALE },
            new[] { "LEFT / RIGHT", "UP / DOWN", "SIZE" }),
        new HudCategory(1, "TARGET MARKERS",
            new[] { ACO_HudSettings.ITEM_MARKER_SIZE, ACO_HudSettings.ITEM_INFO_ANGLE },
            new[] { "FRAME SIZE", "INFO VIEW ANGLE" }),
        new HudCategory(2, "CROSSHAIR",
            new[] { ACO_HudSettings.ITEM_CROSSHAIR, ACO_HudSettings.ITEM_CROSSHAIR_SIZE, ACO_HudSettings.ITEM_CROSSHAIR_DISTANCE },
            new[] { "SHOW", "SIZE", "DISTANCE (+RWR)" }),
        new HudCategory(2, "RWR",
            new[] { ACO_HudSettings.ITEM_RWR, ACO_HudSettings.ITEM_RWR_SIZE, ACO_HudSettings.ITEM_RWR_MISSILE_SIZE, ACO_HudSettings.ITEM_RWR_LOCK_SIZE },
            new[] { "SHOW", "RING SIZE", "MISSILE MARK SIZE", "LOCK ARC SIZE" }),
    };

    /// HUD 설정 패널 프리팹(판 2.8m x 1.25m, 월드 공간 Canvas, 앞면은 루트 +Z 쪽): 3열에 분류 카드(점수 패널, 결과·안내 글자 / 무전 자막, 표적 마커 /
    /// 크로스헤어, RWR) — 카드마다 제목 + 줄(이름 / − / 값 / +), 가운데 열 아래에 RESET. HUD(점수 패널·표적 마커·RWR·자막)는 씬 오브젝트라 Bake가 연결한다.
    public const string CreditsPath = "Assets/ACO_Addon/Credits/ACO_Credits.txt";

    /// 크레딧 보드 프리팹: 판 2.4m x 1.9m(월드 공간 Canvas), 제목 + 본문(텍스트 파일 Credits/ACO_Credits.txt — 실행할 때 ACO_CreditBoard가 읽는다).
    /// 본문은 글자 수에 맞춰 자동으로 줄어든다(20~34). 기본 TMP 폰트라 영문으로 적는다. 로비 벽 등에 둔다.
    public static void CreateCreditBoardPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath} — Window → TextMeshPro → Import TMP Essential Resources를 먼저 실행하세요.");
            return;
        }
        TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(CreditsPath);

        GameObject root = new GameObject("ACO_CreditBoard");
        ACO_CreditBoard board = root.AddUdonSharpComponent<ACO_CreditBoard>();
        RectTransform canvas = MakeUiPanel(root.transform, 2400f, 1900f);
        MakeUiText(canvas, "Title", new Vector2(0f, 860f), new Vector2(2300f, 120f), 84f, font, "CREDITS");
        TextMeshProUGUI body = MakeUiText(canvas, "Body", new Vector2(0f, -70f), new Vector2(2240f, 1660f), 34f, font, source != null ? source.text : "(Credits/ACO_Credits.txt)");
        body.alignment = TextAlignmentOptions.TopLeft;
        body.enableWordWrapping = true;
        body.enableAutoSizing = true;
        body.fontSizeMin = 20f;
        body.fontSizeMax = 34f;
        body.color = new Color(0.85f, 0.95f, 0.88f, 1f);
        board.Source = source;
        board.Body = body;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_CreditBoard.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 크레딧 보드 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 내용은 {CreditsPath}를 고치면 된다(실행할 때 읽는다).");
    }

    public static void CreateHudSettingsPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath} — Window → TextMeshPro → Import TMP Essential Resources를 먼저 실행하세요.");
            return;
        }

        GameObject root = new GameObject("ACO_HudSettings");
        ACO_HudSettings settings = root.AddUdonSharpComponent<ACO_HudSettings>();

        const float colWidth = 860f, rowH = 90f, headerH = 76f, cardGap = 30f, top = 470f;
        float[] colX = { -900f, 0f, 900f };
        float[] colY = { top, top, top };
        RectTransform canvas = MakeUiPanel(root.transform, 2800f, 1250f);
        MakeUiText(canvas, "Title", new Vector2(0f, 555f), new Vector2(2700f, 100f), 70f, font, "HUD SETTINGS");
        var values = new TextMeshProUGUI[ACO_HudSettings.ITEM_COUNT];
        var accent = new Color(1f, 0.85f, 0.35f, 1f);
        foreach (HudCategory cat in HudCategories)
        {
            float cx = colX[cat.Column];
            float y0 = colY[cat.Column];
            float h = headerH + cat.Items.Length * rowH + 16f;
            MakeUiBox(canvas, $"Card_{cat.Title}", new Vector2(cx, y0 - h * 0.5f), new Vector2(colWidth, h), new Color(0.04f, 0.09f, 0.06f, 1f));
            TextMeshProUGUI header = MakeUiText(canvas, $"Header_{cat.Title}", new Vector2(cx, y0 - headerH * 0.5f), new Vector2(colWidth - 40f, headerH), 40f, font, cat.Title);
            header.alignment = TextAlignmentOptions.Left;
            header.color = accent;
            for (int r = 0; r < cat.Items.Length; r++)
            {
                int i = cat.Items[r];
                float y = y0 - headerH - rowH * (r + 0.5f);
                TextMeshProUGUI l = MakeUiText(canvas, $"Label_{i}", new Vector2(cx - 215f, y), new Vector2(400f, rowH), 36f, font, cat.Labels[r]);
                l.alignment = TextAlignmentOptions.Left;
                MakeHudSettingButton(canvas, $"Minus_{i}", new Vector2(cx + 60f, y), font, "-", i, -1);
                values[i] = MakeUiText(canvas, $"Value_{i}", new Vector2(cx + 200f, y), new Vector2(160f, rowH), 36f, font, "-");
                MakeHudSettingButton(canvas, $"Plus_{i}", new Vector2(cx + 340f, y), font, "+", i, 1);
            }
            colY[cat.Column] = y0 - h - cardGap;
        }
        // 가운데 열 아래: RESET + 안내
        float ry = colY[1] - 70f;
        Image reset = MakeUiButton(canvas, "Reset", new Vector2(colX[1], ry), new Vector2(500f, 110f), font, "RESET ALL", 50f);
        ACO_HudSettingButton rb = reset.gameObject.AddUdonSharpComponent<ACO_HudSettingButton>();
        rb.Settings = settings;
        rb.Direction = 0;
        WireClick(reset.GetComponent<Button>(), rb);
        MakeUiText(canvas, "Hint", new Vector2(colX[1], ry - 120f), new Vector2(colWidth, 100f), 30f, font, "SAVED AUTOMATICALLY\nYOUR HUD PREVIEWS WHILE ADJUSTING");
        settings.ValueTexts = values;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_HudSettings.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ACO] HUD 설정 패널 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 씬(로비·격납고)에 두면 Bake가 HUD를 연결합니다.");
    }

    private static void MakeHudSettingButton(RectTransform canvas, string name, Vector2 pos, TMP_FontAsset font, string label, int item, int direction)
    {
        Image img = MakeUiButton(canvas, name, pos, new Vector2(120f, 76f), font, label, 56f);
        ACO_HudSettingButton b = img.gameObject.AddUdonSharpComponent<ACO_HudSettingButton>();
        b.Item = item;
        b.Direction = direction;
        WireClick(img.GetComponent<Button>(), b);
    }

    public static bool EnsureLockVisuals(ACO_TargetMarkers markers)
    {
        bool changed = false;
        Shader markerShader = Shader.Find("ACO/MarkerOverlay");
        if (markerShader == null) { return false; }
        if (markers.LockingMaterial == null) { markers.LockingMaterial = LoadOrSaveMaterial(markerShader, LockingGreen, "ACO_MarkerLocking"); changed = true; }
        if (markers.LockedMaterial == null) { markers.LockedMaterial = LoadOrSaveMaterial(markerShader, LockedRed, "ACO_MarkerLocked"); changed = true; }
        if (markers.LockCrossMesh == null)
        {
            EnsureFolder("Assets/ACO_Addon", "Generated");
            Mesh cross = AssetDatabase.LoadAssetAtPath<Mesh>($"{GeneratedDir}/ACO_LockCross.asset");
            if (cross == null) { cross = SaveMesh(BuildCrossMesh(0.5f, 0.1f, 0.035f, "ACO_LockCross"), "ACO_LockCross"); }
            markers.LockCrossMesh = cross;
            changed = true;
        }
        bool complete = markers.LockDiamonds != null && markers.LockDiamonds.Length >= ACO_TargetMarkers.MAX_LOCKS
            && markers.LockDiamondRenderers != null && markers.LockDiamondRenderers.Length >= ACO_TargetMarkers.MAX_LOCKS;
        if (complete)
        {
            foreach (Transform d in markers.LockDiamonds) { if (d == null) { complete = false; break; } }
        }
        if (!complete && markers.AirMesh != null)
        {
            MakeLockDiamonds(markers.transform, markers);
            changed = true;
        }
        return changed;
    }

    /// 락온 칸마다 하나씩 마름모 풀(ACO_TargetMarkers.MAX_LOCKS개)을 만든다. 이미 있는 이름(LockDiamond, LockDiamond_k)은 재사용한다
    /// (예전 단일 락온 버전이 만든 'LockDiamond'는 0번 칸이 된다).
    private static void MakeLockDiamonds(Transform parent, ACO_TargetMarkers markers)
    {
        int n = ACO_TargetMarkers.MAX_LOCKS;
        var diamonds = new Transform[n];
        var renderers = new MeshRenderer[n];
        for (int k = 0; k < n; k++)
        {
            Transform existing = parent.Find("LockDiamond_" + k);
            if (existing == null && k == 0) { existing = parent.Find("LockDiamond"); }
            GameObject d;
            if (existing != null)
            {
                d = existing.gameObject;
                d.name = "LockDiamond_" + k;
            }
            else
            {
                d = new GameObject("LockDiamond_" + k);
                d.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(d, "ACO lock diamond");
            }
            MeshFilter mf = d.GetComponent<MeshFilter>();
            if (mf == null) { mf = d.AddComponent<MeshFilter>(); }
            mf.sharedMesh = markers.AirMesh;
            MeshRenderer r = d.GetComponent<MeshRenderer>();
            if (r == null) { r = d.AddComponent<MeshRenderer>(); }
            r.sharedMaterial = markers.LockingMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            d.SetActive(false);
            diamonds[k] = d.transform;
            renderers[k] = r;
        }
        markers.LockDiamonds = diamonds;
        markers.LockDiamondRenderers = renderers;
    }
    private static readonly Color WaypointBlue = new Color(0.55f, 0.8f, 1f, 0.45f);
    private static readonly Color DestinationCyan = new Color(0.4f, 0.9f, 1f, 0.85f);

    /// Bake용: 표적 마커의 빈 아군/경유점/목적지 머티리얼을 채운다(예전 HUD 프리팹에는 이 칸이 없었다). 바꿨으면 true.
    public static bool FillMarkerMaterials(ACO_TargetMarkers markers)
    {
        if (markers.FriendlyMaterial != null && markers.WaypointMaterial != null && markers.DestinationMaterial != null) { return false; }
        Shader markerShader = Shader.Find("ACO/MarkerOverlay");
        if (markerShader == null) { return false; }
        if (markers.FriendlyMaterial == null) { markers.FriendlyMaterial = LoadOrSaveMaterial(markerShader, FriendlyBlue, "ACO_MarkerFriendly"); }
        if (markers.WaypointMaterial == null) { markers.WaypointMaterial = LoadOrSaveMaterial(markerShader, WaypointBlue, "ACO_MarkerWaypoint"); }
        if (markers.DestinationMaterial == null) { markers.DestinationMaterial = LoadOrSaveMaterial(markerShader, DestinationCyan, "ACO_MarkerDestination"); }
        return true;
    }

    /// 작전 테이블(ACO_TacticalMap)을 씬에 만든다: 둥근 테이블(윗면 = 해수면, 높이 0.9m) + 바다 원판 + 축소 지형 메시 자리 + 마커 원본(꺼 둔 자식).
    /// 마커 = 빈 루트(위치·방향) + Body(모양) + 선택: TGT 고리, Label(이름표), 루트의 꼬리 궤적. 생성기·게임 진행은 Bake가 연결한다.
    public static GameObject CreateTacticalMap()
    {
        EnsureFolder("Assets/ACO_Addon", "Generated");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Shader unlit = Shader.Find("Unlit/Color");

        var root = new GameObject("ACO_TacticalMap");
        ACO_TacticalMap map = root.AddUdonSharpComponent<ACO_TacticalMap>();
        float r = map.MapRadius;
        const float tableHeight = 0.9f;

        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        table.name = "Table";
        table.transform.SetParent(root.transform, false);
        table.transform.localPosition = new Vector3(0f, tableHeight * 0.5f, 0f);
        table.transform.localScale = new Vector3(r * 2f + 0.2f, tableHeight * 0.5f, r * 2f + 0.2f);
        table.GetComponent<MeshRenderer>().sharedMaterial = LoadOrSaveMaterial(unlit, new Color(0.12f, 0.13f, 0.14f), "ACO_MapTable");

        var mapRoot = new GameObject("MapRoot");
        mapRoot.transform.SetParent(root.transform, false);
        mapRoot.transform.localPosition = new Vector3(0f, tableHeight + 0.003f, 0f);

        GameObject sea = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        sea.name = "Sea";
        Object.DestroyImmediate(sea.GetComponent<Collider>());
        sea.transform.SetParent(mapRoot.transform, false);
        sea.transform.localPosition = new Vector3(0f, -0.002f, 0f);
        sea.transform.localScale = new Vector3(r * 2f, 0.001f, r * 2f);
        sea.GetComponent<MeshRenderer>().sharedMaterial = LoadOrSaveMaterial(unlit, new Color(0.06f, 0.16f, 0.30f), "ACO_MapSea");

        var terrain = new GameObject("Terrain");
        terrain.transform.SetParent(mapRoot.transform, false);
        MeshFilter terrainFilter = terrain.AddComponent<MeshFilter>();
        MeshRenderer terrainRenderer = terrain.AddComponent<MeshRenderer>();
        terrainRenderer.sharedMaterial = LoadOrSaveMaterial(Shader.Find("ACO/MapVertexColor"), Color.white, "ACO_MapTerrain");
        terrainRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector3 airShape = new Vector3(0.012f, 0.004f, 0.03f);
        map.MapRoot = mapRoot.transform;
        map.TerrainFilter = terrainFilter;
        map.PlayerMarker = MakeMapMarker(mapRoot.transform, "PlayerMarker", airShape, new Color(0.35f, 0.7f, 1f), true, false, font);
        map.EnemyAirMarker = MakeMapMarker(mapRoot.transform, "EnemyAirMarker", airShape, new Color(1f, 0.25f, 0.2f), true, false, null);
        map.GroundMarker = MakeMapMarker(mapRoot.transform, "GroundMarker", new Vector3(0.01f, 0.008f, 0.01f), new Color(1f, 0.55f, 0.15f), false, true, null);
        map.ObjectiveMarker = MakeMapMarker(mapRoot.transform, "ObjectiveMarker", new Vector3(0.018f, 0.014f, 0.018f), new Color(1f, 0.85f, 0.2f), false, false, null);
        map.FriendlyMarker = MakeMapMarker(mapRoot.transform, "FriendlyMarker", airShape, new Color(0.3f, 1f, 0.45f), true, false, null);
        map.BaseMarker = MakeMapMarker(mapRoot.transform, "BaseMarker", new Vector3(0.02f, 0.006f, 0.06f), new Color(0.35f, 0.7f, 1f), false, false, null);
        return root;
    }

    private static GameObject MakeMapMarker(Transform parent, string name, Vector3 shape, Color color, bool trail, bool tgtRing, TMP_FontAsset labelFont)
    {
        Shader unlit = Shader.Find("Unlit/Color");
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = shape;
        MeshRenderer br = body.GetComponent<MeshRenderer>();
        br.sharedMaterial = LoadOrSaveMaterial(unlit, color, "ACO_Map" + name);
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (trail)
        {
            TrailRenderer tr = go.AddComponent<TrailRenderer>();
            tr.time = 8f;
            tr.startWidth = 0.004f;
            tr.endWidth = 0f;
            tr.minVertexDistance = 0.002f;
            tr.autodestruct = false;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.sharedMaterial = LoadOrSaveMaterial(Shader.Find("Sprites/Default"), Color.white, "ACO_MapTrail");
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
        }

        if (tgtRing)
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "TGT";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(go.transform, false);
            ring.transform.localScale = new Vector3(0.03f, 0.0005f, 0.03f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = LoadOrSaveMaterial(unlit, new Color(1f, 0.85f, 0.2f), "ACO_MapTargetRing");
            ring.SetActive(false);
        }

        if (labelFont != null)
        {
            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            TextMeshPro t = label.AddComponent<TextMeshPro>();
            t.font = labelFont;
            t.fontSize = 0.25f;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.color = color;
            t.rectTransform.sizeDelta = new Vector2(0.4f, 0.05f);
            t.text = "";
        }

        go.SetActive(false);
        return go;
    }

    /// 무전 채터(ACO_Chatter)를 씬에 만든다: 머리를 따라오는 자막 한 줄(가운데 정렬, 줄바꿈 허용) + 2D 음성 AudioSource.
    /// 글자는 HUD와 같은 '항상 맨 위' 머티리얼(Generated/ACO_HUDTextOverlay — HUD 프리팹을 만들 때 생긴다)을 쓴다.
    public static GameObject CreateChatter()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath} — Window → TextMeshPro → Import TMP Essential Resources를 먼저 실행하세요.");
            return null;
        }
        Material textMat = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedDir}/ACO_HUDTextOverlay.mat");
        if (textMat == null) { textMat = font.material; }

        var root = new GameObject("ACO_Chatter");
        ACO_Chatter radio = root.AddUdonSharpComponent<ACO_Chatter>();

        // 한 단위 = 0.03m(점수 패널과 같다). 폭 50단위 ≈ 1.5m, 넘치면 줄바꿈.
        var panel = new GameObject("SubtitlePanel");
        panel.transform.SetParent(root.transform, false);
        panel.transform.localScale = Vector3.one * 0.03f;
        TextMeshPro text = MakeText(panel.transform, "Subtitle", Vector3.zero, 6f, Color.white,
            TextAlignmentOptions.Bottom, new Vector2(50f, 8f), new Vector2(0.5f, 0f), font, textMat, "[SKY EYE] Chatter check.");
        text.enableWordWrapping = true;

        var voiceGo = new GameObject("Voice");
        voiceGo.transform.SetParent(root.transform, false);
        AudioSource voice = voiceGo.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;

        radio.Panel = panel.transform;
        radio.SubtitleText = text;
        radio.Voice = voice;
        return root;
    }

    /// 화면 암전 구: 머리를 감싸는 검은 구(ACO/MarkerOverlay — 항상 맨 위, 양면, 알파). 다른 오버레이(마커·HUD)보다 나중에 그린다.
    public static Renderer CreateScreenFadeSphere(Transform parent)
    {
        EnsureFolder("Assets/ACO_Addon", "Generated");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedDir}/ACO_ScreenFade.mat");
        if (mat == null) { mat = SaveMaterial(Shader.Find("ACO/MarkerOverlay"), new Color(0f, 0f, 0f, 0f), "ACO_ScreenFade", 4900); }

        var go = new GameObject("FadeSphere");
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * 0.3f;
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;
        return r;
    }

    private static Material LoadOrSaveMaterial(Shader shader, Color color, string name)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedDir}/{name}.mat");
        return existing != null ? existing : SaveMaterial(shader, color, name);
    }

    private static Material SaveMaterial(Shader shader, Color color, string name, int renderQueue = -1)
    {
        var mat = new Material(shader) { name = name };
        mat.SetColor("_Color", color);
        if (renderQueue >= 0) { mat.renderQueue = renderQueue; }
        return SaveAsset(mat, $"{GeneratedDir}/{name}.mat");
    }

    private static MeshRenderer MakeQuad(Transform parent, string name, Vector3 localPos, Vector3 localScale, Mesh mesh, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    /// 같은 경로에 이미 있으면 내용을 덮어써서 기존 참조(GUID)를 유지한다.
    private static T SaveAsset<T>(T obj, string path) where T : Object
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(obj, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(obj, path);
        return obj;
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) { AssetDatabase.CreateFolder(parent, name); }
    }
}
