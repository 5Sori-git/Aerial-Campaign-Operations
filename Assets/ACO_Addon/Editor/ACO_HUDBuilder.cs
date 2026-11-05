using System.Collections.Generic;
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
    private const string FlightFontPath = "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Electronic Highway Sign SDF.asset";
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
        MakeWorldIcon(texts.transform, "HeaderIcon", LoadIcon("operation"), new Vector3(-35f, 19.8f, 0f), 7.4f, IconColor);
        TextMeshPro header = MakeText(texts.transform, "HeaderText", new Vector3(-30f, 23.5f, 0f), 26f, TextGreen,
            TextAlignmentOptions.TopLeft, new Vector2(44f, 8f), new Vector2(0f, 1f), font, textMat, "OPERATION LOG\n<size=60%>AWAITING MISSION</size>");
        MakeWorldIcon(texts.transform, "ScoreIcon", LoadIcon("score"), new Vector3(17.8f, 18.4f, 0f), 5.6f, HangarGold);
        MakeText(texts.transform, "ScoreTitle", new Vector3(21.5f, 20.6f, 0f), 18f, HangarGold,
            TextAlignmentOptions.TopLeft, new Vector2(16.5f, 5f), new Vector2(0f, 1f), font, textMat, "SCORE");
        TextMeshPro logText = MakeText(texts.transform, "LogText", new Vector3(-38f, 14.5f, 0f), 13f, TextGreen,
            TextAlignmentOptions.TopLeft, new Vector2(50f, 38f), new Vector2(0f, 1f), font, textMat, "TIME<pos=13%>GRID<pos=27%>PILOT");
        TextMeshPro pilotText = MakeText(texts.transform, "PilotText", new Vector3(15f, 14.5f, 0f), 13f, TextGreen,
            TextAlignmentOptions.TopLeft, new Vector2(23f, 38f), new Vector2(0f, 1f), font, textMat, "PILOT<pos=58%>KILLS");

        log.HeaderText = header;
        log.LogText = logText;
        log.PilotText = pilotText;
        EnsureLogScrollButtons(log);
        ApplyBoardLayout(log, texts.transform, font);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_MissionBoard.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ACO] 작전판 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 씬(브리핑 룸 등)에 두고 ACO → Bake NPC References로 관리자/생성기를 연결하세요.");
        Selection.activeObject = prefab;
    }

    /// 월드 판(작전판)의 아이콘: size = 부모 단위의 한 변. 글자처럼 앞면이 -Z다.
    private static void MakeWorldIcon(Transform parent, string name, Sprite sprite, Vector3 pos, float size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.color = color;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (sprite != null) { go.transform.localScale = Vector3.one * (size / sprite.bounds.size.x); }
    }

    public const string LogScrollName = "ScrollButtons";

    /// 작전판 스크롤 버튼(판 아래 띠: NEWER · OLDER · LATEST — 콜라이더 Interact). 이미 있으면 그대로. 만들었으면 true(Bake도 부른다 — 예전 작전판).
    private const float BoardScale = 0.7f;   // 4 x 2.5m → 2.8 x 1.75m

    /// 판을 줄이고, 긴 결과는 제목 칸 안에서 줄을 바꿔 글자를 줄이며, 오른쪽 아래에 최고 획득 순위를 놓는다.
    private static void ApplyBoardLayout(ACO_MissionLog log, Transform texts, TMP_FontAsset font)
    {
        log.transform.localScale = Vector3.one * BoardScale;
        TextMeshPro header = log.HeaderText;
        header.enableWordWrapping = true;
        header.overflowMode = TextOverflowModes.Truncate;
        header.fontSizeMax = header.fontSize;
        header.fontSizeMin = 11f;
        header.enableAutoSizing = true;
        header.rectTransform.sizeDelta = new Vector2(46f, 9.4f);
        log.PilotText.rectTransform.sizeDelta = new Vector2(23f, 16f);
        log.MaxPilots = Mathf.Min(log.MaxPilots, 8);
        Material textMat = font.material;
        MakeWorldIcon(texts, "BestIcon", LoadIcon("credits"), new Vector3(17.4f, -4.4f, 0f), 3.6f, HangarGold);
        MakeText(texts, "BestTitle", new Vector3(20f, -2.9f, 0f), 13f, HangarGold,
            TextAlignmentOptions.TopLeft, new Vector2(18f, 3f), new Vector2(0f, 1f), font, textMat, "BEST MISSION PAY");
        log.BestText = MakeText(texts, "BestText", new Vector3(15f, -6.8f, 0f), 13f, TextGreen,
            TextAlignmentOptions.TopLeft, new Vector2(23f, 16f), new Vector2(0f, 1f), font, textMat, "PILOT<pos=62%>BEST CR");
    }

    /// 먼저 만든 작전판을 고친다 — 다시 만들면 직접 고친 내용이 사라진다.
    public static void EnsureBoardLayout()
    {
        string path = $"{PrefabDir}/ACO_MissionBoard.prefab";
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_MissionLog log = root.GetComponent<ACO_MissionLog>();
            if (log == null || log.BestText != null || log.HeaderText == null || log.PilotText == null) { return; }
            Transform texts = log.HeaderText.transform.parent;
            ApplyBoardLayout(log, texts, font);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] 작전판: 크기를 줄이고 긴 결과 줄바꿈과 최고 획득 순위를 넣었습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static bool EnsureLogScrollButtons(ACO_MissionLog log)
    {
        Transform face = log.transform.Find("Face");
        if (face == null) { Debug.LogWarning($"[ACO] 작전판 '{log.name}'에 Face가 없어 스크롤 버튼을 달지 못했습니다 — 작전판 프리팹을 다시 만드세요.", log); return false; }
        if (face.Find(LogScrollName) != null) { return false; }
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Shader bgShader = Shader.Find("Unlit/Color");
        if (font == null || bgShader == null) { return false; }
        Material btnMat = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedDir}/ACO_BoardButton.mat");
        if (btnMat == null)
        {
            btnMat = new Material(bgShader) { name = "ACO_BoardButton" };
            btnMat.SetColor("_Color", ButtonGreen);
            btnMat = SaveAsset(btnMat, $"{GeneratedDir}/ACO_BoardButton.mat");
        }
        // 판(4 x 2.5m) 바로 아래 띠 — Face는 180° 돈 앞면이라 글자가 -Z 쪽(보는 쪽)에서 읽힌다
        Transform group = NewChild(face, LogScrollName, new Vector3(0f, -1.42f, -0.02f));
        Undo.RegisterCreatedObjectUndo(group.gameObject, "ACO log scroll");
        string[] labels = { "NEWER", "OLDER", "LATEST" };
        int[] steps = { -1, 1, 0 };
        for (int i = 0; i < labels.Length; i++)
        {
            Transform b = NewChild(group, "Btn_" + labels[i], new Vector3(-0.7f + i * 0.7f, 0f, 0f));
            BoxCollider c = b.gameObject.AddComponent<BoxCollider>();
            c.size = new Vector3(0.6f, 0.22f, 0.04f);
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "Background";
            quad.transform.SetParent(b, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            quad.transform.localScale = new Vector3(0.6f, 0.22f, 1f);
            MeshRenderer qr = quad.GetComponent<MeshRenderer>();
            qr.sharedMaterial = btnMat;
            qr.shadowCastingMode = ShadowCastingMode.Off;
            qr.receiveShadows = false;
            MakeText(b, "Label", Vector3.zero, 1.1f, TextGreen, TextAlignmentOptions.Center, new Vector2(0.6f, 0.22f), new Vector2(0.5f, 0.5f), font, font.material, labels[i]);
            ACO_LogScrollButton btn = b.gameObject.AddUdonSharpComponent<ACO_LogScrollButton>();
            btn.Log = log;
            btn.Step = steps[i];
            btn.InteractionText = labels[i];
        }
        return true;
    }

    /// 작전 콘솔 글자 이름 — Bake가 이 이름으로 찾아 ACO_GameFlow의 BannerTexts/PhaseTexts/PhaseInfoTexts/TerrainTexts에 넣는다.
    public const string FlowBannerName = "ACO_FlowBanner";
    public const string FlowPhasesName = "ACO_FlowPhases";
    public const string FlowPhaseInfoName = "ACO_FlowPhaseInfo";
    public const string FlowPhaseIconName = "ACO_FlowPhaseIcon";
    public const string FlowTerrainName = "ACO_FlowTerrain";
    private const int ConsoleSlotCards = 8;

    /// 작전 콘솔 프리팹(월드 공간 Canvas 2.4 x 1.5m, 앞면은 루트 +Z 쪽).
    ///  위 = 상태 배너(큰 상태 + 내 다음 할 일 + 거절 안내), 왼쪽 = 작전 카드(공용 설정: 페이즈 −/+ · 예상 시간 · 지형 · NEW TERRAIN · 바꾼 사람),
    ///  오른쪽 = 명단 칸 카드 8장(2열 — 기체 수보다 많은 카드는 게임 진행이 숨긴다, 빈 칸을 누르면 그 칸으로 참가),
    ///  아래 = 내 행동 버튼(JOIN → READY → LAUNCH) + LEAVE. 글자·버튼 상태는 모두 게임 진행이 쓴다.
    /// NEW TERRAIN 버튼 자리를 둘로 나눠 자유 비행 버튼을 놓는다.
    private static bool AddFreeFlightButton(RectTransform canvas, TMP_FontAsset font)
    {
        RectTransform reroll = canvas.Find("Reroll") as RectTransform;
        if (reroll == null || canvas.Find("FreeFlight") != null) { return false; }
        Vector2 pos = reroll.anchoredPosition;
        const float width = 480f;
        float height = reroll.sizeDelta.y;
        float oldWidth = reroll.sizeDelta.x;
        reroll.sizeDelta = new Vector2(width, height);
        reroll.anchoredPosition = pos + new Vector2(-255f, 0f);
        RectTransform icon = reroll.Find("Icon") as RectTransform;
        if (icon != null) { icon.anchoredPosition += new Vector2((oldWidth - width) * 0.5f, 0f); }
        TextMeshProUGUI label = reroll.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) { label.fontSize = Mathf.Min(label.fontSize, 38f); }
        MakeFlowButton(canvas, "FreeFlight", pos + new Vector2(255f, 0f), new Vector2(width, height), font, "FREE FLIGHT: OFF", ACO_GameFlow.ACTION_FREE_FLIGHT, 42f);
        return true;
    }

    /// 먼저 만든 작전 콘솔에 자유 비행 버튼을 더한다 — 다시 만들면 직접 고친 내용이 사라진다.
    public static void EnsureConsoleFreeFlightButton()
    {
        string path = $"{PrefabDir}/ACO_MissionConsole.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (RectTransform canvas in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (canvas.Find("Reroll") == null) { continue; }
                if (AddFreeFlightButton(canvas, font))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("[ACO] 작전 콘솔에 자유 비행 버튼을 넣었습니다.");
                }
                break;
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

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
        banner.margin = new Vector4(190f, 10f, 50f, 10f);
        MakeIcon(canvas, "BannerIcon", LoadIcon("operation"), new Vector2(-1060f, 590f), 150f);

        // 작전 카드(공용 설정)
        const float lx = -640f;
        MakeUiBox(canvas, "OperationBox", new Vector2(lx, -20f), new Vector2(1020f, 900f), new Color(0.04f, 0.08f, 0.06f, 1f));
        MakeUiLabel(canvas, "OperationTitle", new Vector2(lx + 30f, 390f), new Vector2(900f, 60f), 36f, font, "OPERATION - SHARED BY THE SQUAD");
        MakeIcon(canvas, "OperationIcon", LoadIcon("operation"), new Vector2(lx - 370f, 390f), 58f);
        MakeUiLabel(canvas, "PhasesTitle", new Vector2(lx + 20f, 310f), new Vector2(900f, 50f), 34f, font, "PHASES");
        MakeIcon(canvas, "PhasesIcon", LoadIcon("phases"), new Vector2(lx - 105f, 310f), 50f);
        MakeFlowButton(canvas, "PhasesDown", new Vector2(lx - 290f, 190f), new Vector2(170f, 170f), font, "-", ACO_GameFlow.ACTION_PHASES_DOWN, 110f);
        MakeUiText(canvas, FlowPhasesName, new Vector2(lx - 100f, 190f), new Vector2(190f, 190f), 150f, font, "3");
        // 난이도 아이콘: 병아리 → 참새 → 매 → 독수리 → 왕관 쓴 독수리, 초록 → 빨강(게임 진행이 그림·색을 바꾼다)
        MakeIcon(canvas, FlowPhaseIconName, LoadIcon("phase_3"), new Vector2(lx + 95f, 190f), 180f);
        MakeFlowButton(canvas, "PhasesUp", new Vector2(lx + 290f, 190f), new Vector2(170f, 170f), font, "+", ACO_GameFlow.ACTION_PHASES_UP, 110f);
        MakeUiText(canvas, FlowPhaseInfoName, new Vector2(lx, 30f), new Vector2(960f, 120f), 34f, font, "ABOUT 15 MIN");
        MakeUiLabel(canvas, "TerrainTitle", new Vector2(lx + 20f, -80f), new Vector2(900f, 50f), 34f, font, "TERRAIN");
        MakeIcon(canvas, "TerrainIcon", LoadIcon("terrain"), new Vector2(lx - 115f, -80f), 54f);
        MakeUiText(canvas, FlowTerrainName, new Vector2(lx, -180f), new Vector2(960f, 150f), 64f, font, "-");
        Image reroll = MakeFlowButton(canvas, "Reroll", new Vector2(lx, -360f), new Vector2(720f, 130f), font, "NEW TERRAIN", ACO_GameFlow.ACTION_REROLL, 48f);
        AddButtonIcon(reroll, LoadIcon("reroll"), 100f, IconColor);

        // 명단 칸 카드
        const float rx = 560f;
        MakeUiBox(canvas, "RosterBox", new Vector2(rx, -20f), new Vector2(1240f, 900f), new Color(0.04f, 0.08f, 0.06f, 1f));
        MakeUiLabel(canvas, "RosterTitle", new Vector2(rx + 30f, 390f), new Vector2(1120f, 60f), 36f, font, "ROSTER - TAP AN OPEN SLOT TO JOIN");
        MakeIcon(canvas, "RosterIcon", LoadIcon("roster"), new Vector2(rx - 385f, 390f), 62f);
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
            AddButtonIcon(img, LoadIcon("tab_airframe"), 92f, IconColor);
        }

        AddFreeFlightButton(canvas, font);

        // 내 행동 + LEAVE
        Image primary = MakeFlowButton(canvas, "Primary", new Vector2(-300f, -620f), new Vector2(1700f, 180f), font, "JOIN", ACO_GameFlow.ACTION_PRIMARY, 76f);
        MakeIcon(primary.rectTransform, "Icon", LoadIcon("launch"), new Vector2(-560f, 0f), 130f);
        Image leave = MakeFlowButton(canvas, "Leave", new Vector2(900f, -620f), new Vector2(500f, 180f), font, "LEAVE", ACO_GameFlow.ACTION_LEAVE, 50f);
        AddButtonIcon(leave, LoadIcon("leave"), 96f, IconColor);

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
        // 구획 카드(글자·버튼 뒤): 특수무장, 강화, 출격
        MakeUiBox(canvas, "SpecialCard", new Vector2(left, -110f), new Vector2(1360f, 1140f), HangarCardColor);
        MakeUiBox(canvas, "UpgradeCard", new Vector2(right, -110f), new Vector2(1360f, 1140f), HangarCardColor);
        MakeUiBox(canvas, "SortieCard", new Vector2(0f, -805f), new Vector2(2760f, 270f), HangarCardColor);

        MakeUiText(canvas, "Title", new Vector2(50f, 820f), new Vector2(1200f, 130f), 96f, font, "HANGAR");
        MakeIcon(canvas, "TitleIcon", LoadIcon("hangar"), new Vector2(-240f, 820f), 120f);

        // 보유 크레딧: 금색 테두리 판 + 동전 + 큰 숫자, 그 아래 지난 미션 보상
        MakeUiBox(canvas, "CreditsFrame", new Vector2(0f, 600f), new Vector2(1420f, 216f), new Color(1f, 0.82f, 0.3f, 0.75f));
        MakeUiBox(canvas, "CreditsPlate", new Vector2(0f, 600f), new Vector2(1408f, 204f), new Color(0.13f, 0.1f, 0.03f, 1f));
        Image creditsIcon = MakeIcon(canvas, "CreditsIcon", LoadIcon("credits"), new Vector2(-590f, 600f), 160f);
        creditsIcon.color = HangarGold;
        TextMeshProUGUI credits = MakeUiText(canvas, "Credits", new Vector2(80f, 630f), new Vector2(1180f, 130f), 112f, font, "-");
        credits.color = HangarGold;
        credits.fontStyle = FontStyles.Bold;
        TextMeshProUGUI lastReward = MakeUiText(canvas, "LastReward", new Vector2(80f, 532f), new Vector2(1180f, 50f), 36f, font, "");
        lastReward.color = new Color(1f, 0.9f, 0.6f, 0.9f);

        // 왼쪽: 특수무장
        MakeHangarHeader(canvas, "SpecialHeader", "tab_missile", new Vector2(left, 410f), font, "SPECIAL WEAPON");
        TextMeshProUGUI selection = MakeUiText(canvas, "Selection", new Vector2(left, 250f), new Vector2(1300f, 230f), 44f, font, "SPECIAL WEAPON: -");
        // 분류 넘김: < 분류 이름 > (공대공 → 공대지 → 폭탄 → 기타)
        Image prev = MakeUiButton(canvas, "PrevPage", new Vector2(left - 540f, 80f), new Vector2(160f, 90f), font, "<", 60f);
        Image next = MakeUiButton(canvas, "NextPage", new Vector2(left + 540f, 80f), new Vector2(160f, 90f), font, ">", 60f);
        TextMeshProUGUI pageText = MakeUiText(canvas, "PageText", new Vector2(left, 80f), new Vector2(900f, 90f), 46f, font, "AIR-TO-AIR");
        pageText.color = new Color(1f, 0.85f, 0.35f, 1f);
        Image pageIcon = MakeIcon(canvas, "PageIcon", LoadIcon("tab_sp_aam"), new Vector2(left - 400f, 80f), 76f);
        pageIcon.color = pageText.color;
        loadout.PageIcon = pageIcon;
        loadout.PageIcons = new[] { LoadIcon("tab_sp_aam"), LoadIcon("tab_sp_agm"), LoadIcon("tab_sp_bomb"), LoadIcon("cm_pod") };
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
        AddButtonIcon(buy, LoadIcon("credits"), 68f, HangarGold);
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
        MakeHangarHeader(canvas, "UpgradeHeader", "upgrade", new Vector2(right, 410f), font, "UPGRADES");
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
        // 구매 버튼: 줄을 눌러 고르고 이 버튼으로 산다(클릭 실수 방지). 글자·색은 게임 중 상점이 고른 줄에 맞게 쓴다.
        Image shopBuy = MakeUiButton(canvas, "BuyUpgrade", new Vector2(right, -565f), new Vector2(1000f, 84f), font, "SELECT AN ITEM", 40f);
        shopBuy.color = new Color(0.16f, 0.17f, 0.17f, 1f);
        AddButtonIcon(shopBuy, LoadIcon("credits"), 62f, HangarGold);
        ACO_HangarButton shopBuyButton = shopBuy.gameObject.AddUdonSharpComponent<ACO_HangarButton>();
        shopBuyButton.Shop = shop;
        shopBuyButton.Action = ACO_HangarButton.ACTION_SHOP_BUY;
        WireClick(shopBuy.GetComponent<Button>(), shopBuyButton);
        shop.BuyButton = shopBuy.gameObject;
        shop.BuyText = shopBuy.GetComponentInChildren<TextMeshProUGUI>();

        TextMeshProUGUI message = MakeUiText(canvas, "UpgradeMessage", new Vector2(right, -645f), new Vector2(1300f, 60f), 34f, font,
            "SELECT AN UPGRADE, THEN PRESS BUY");

        shop.CreditsText = credits;
        shop.LastRewardText = lastReward;
        shop.TabImages = tabImages;
        shop.RowObjects = rowObjects;
        shop.RowTexts = rowTexts;
        shop.RowImages = rowImages;
        shop.RowIcons = rowIcons;
        shop.MessageText = message;

        // 아래: 출격 — 명단·준비 상태(게임 진행이 글자를 쓴다) + READY(미션 전) + LAUNCH(미션 중 재출격)
        MakeIcon(canvas, "RosterIcon", LoadIcon("roster"), new Vector2(-1310f, -730f), 96f);
        MakeFlowStatus(canvas, new Vector2(left, -805f), new Vector2(1300f, 270f), 34f, font, 120f);
        Image ready = MakeFlowButton(canvas, "Ready", new Vector2(450f, -810f), new Vector2(500f, 170f), font, "READY", ACO_GameFlow.ACTION_TOGGLE_READY);
        AddButtonIcon(ready, LoadIcon("ready"), 84f, IconColor);
        Image launch = MakeFlowButton(canvas, "Launch", new Vector2(1000f, -810f), new Vector2(500f, 170f), font, "LAUNCH", ACO_GameFlow.ACTION_LAUNCH);
        AddButtonIcon(launch, LoadIcon("launch"), 96f, IconColor);
        ApplyHangarStyle(canvas, shop, loadout);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_Hangar.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ACO] 격납고 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 씬에 두면 Bake가 기체의 특수무장 슬롯에 연결합니다.");
        Selection.activeObject = prefab;
    }

    /// 게임 진행 상태 글자. Bake가 이 이름(FlowStatusName)의 글자를 찾아 ACO_GameFlow.StatusTexts에 넣는다.
    public const string FlowStatusName = "ACO_FlowStatus";

    private static readonly Color HangarGold = new Color(1f, 0.82f, 0.3f, 1f);
    private static readonly Color HangarCardColor = new Color(1f, 1f, 1f, 0.05f);

    // 격납고 층: 판 < 항목(카드 안에 파인 칸) < 카드. 상태는 채도로만 나타내고, 눌러서 사는 버튼만 금색으로 밝다
    private static readonly Color HangarPanelBase = new Color(0.025f, 0.035f, 0.04f, 0.97f);
    private static readonly Color HangarCardBase = new Color(0.075f, 0.1f, 0.105f, 1f);
    private static readonly Color HangarStripBase = new Color(0.11f, 0.16f, 0.15f, 1f);
    private static readonly Color HangarRowBase = new Color(0.035f, 0.05f, 0.055f, 1f);
    private static readonly Color HangarRowDim = new Color(0.03f, 0.038f, 0.042f, 1f);
    private static readonly Color HangarRowOwned = new Color(0.05f, 0.15f, 0.1f, 1f);
    private static readonly Color HangarRowOn = new Color(0.07f, 0.26f, 0.15f, 1f);
    private static readonly Color HangarRowPick = new Color(0.24f, 0.2f, 0.06f, 1f);
    private static readonly Color HangarEdge = new Color(0.22f, 0.36f, 0.3f, 1f);

    private static void SetImageColor(RectTransform canvas, string name, Color color)
    {
        Transform t = canvas.Find(name);
        Image img = t != null ? t.GetComponent<Image>() : null;
        if (img != null) { img.color = color; }
    }

    // 항목 왼쪽 끝의 가는 선 — 어두운 칸이 눌리는 곳임을 보여 준다
    private static void AddHangarRow(RectTransform canvas, string name)
    {
        Transform t = canvas.Find(name);
        if (t == null) { return; }
        t.GetComponent<Image>().color = HangarRowBase;
        if (t.Find("Edge") != null) { return; }
        RectTransform rt = (RectTransform)t;
        Image edge = MakeUiBox(rt, "Edge", new Vector2(-rt.sizeDelta.x * 0.5f + 4f, 0f), new Vector2(8f, rt.sizeDelta.y), HangarEdge);
        edge.transform.SetAsFirstSibling();
    }

    private static void ApplyHangarStyle(RectTransform canvas, ACO_Shop shop, ACO_Loadout loadout)
    {
        SetImageColor(canvas, "Background", HangarPanelBase);
        foreach (string card in new[] { "SpecialCard", "UpgradeCard", "SortieCard" }) { SetImageColor(canvas, card, HangarCardBase); }
        foreach (string strip in new[] { "SpecialHeaderStrip", "UpgradeHeaderStrip" }) { SetImageColor(canvas, strip, HangarStripBase); }
        for (int i = 0; i < canvas.childCount; i++)
        {
            string name = canvas.GetChild(i).name;
            if (name.StartsWith("Button_") || name.StartsWith("Upgrade_") || name.StartsWith("Tab_") || name == "PrevPage" || name == "NextPage")
            {
                AddHangarRow(canvas, name);
            }
        }
        shop.NormalColor = HangarRowBase;
        shop.MaxedColor = HangarRowOwned;
        shop.TooExpensiveColor = HangarRowDim;
        shop.SelectedRowColor = HangarRowPick;
        shop.TabColor = HangarRowBase;
        shop.TabSelectedColor = HangarRowOn;
        shop.BuyDisabledColor = HangarRowDim;
        loadout.NormalColor = HangarRowBase;
        loadout.SelectedColor = HangarRowOn;
        loadout.LockedColor = HangarRowDim;
        loadout.PreviewColor = HangarRowPick;
        loadout.BuyDisabledColor = HangarRowDim;
    }

    /// 먼저 만든 격납고 패널의 색과 항목 모양을 바꾼다 — 위치·연결은 그대로.
    public static void EnsureHangarStyle()
    {
        string path = $"{PrefabDir}/ACO_Hangar.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_Shop shop = root.GetComponent<ACO_Shop>();
            ACO_Loadout loadout = root.GetComponent<ACO_Loadout>();
            RectTransform canvas = root.transform.Find("Canvas") as RectTransform;
            if (shop == null || loadout == null || canvas == null) { return; }
            Transform first = canvas.Find("Upgrade_0");
            if (first == null || first.Find("Edge") != null) { return; }
            ApplyHangarStyle(canvas, shop, loadout);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] 격납고 패널의 색과 항목 모양을 바꿨습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 관전 패널: 아이콘과 글자 사이를 띄운다.
    public static void EnsureSpectatorIconGap()
    {
        string path = $"{PrefabDir}/ACO_SpectatorPanel.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            bool changed = false;
            foreach (Image img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.GetComponent<Button>() == null) { continue; }
                RectTransform icon = img.transform.Find("Icon") as RectTransform;
                if (icon != null) { changed |= SetIconGap(img, icon.sizeDelta.x); }
            }
            foreach (RectTransform rt in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name == "TargetIcon" && Mathf.Abs(rt.anchoredPosition.x + 480f) < 1f) { rt.anchoredPosition += Vector2.left * 34f; changed = true; }
                else if (rt.name == "ShotIcon" && Mathf.Abs(rt.anchoredPosition.x - 805f) < 1f) { rt.anchoredPosition += Vector2.left * 26f; changed = true; }
            }
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[ACO] 관전 패널: 아이콘과 글자 사이를 띄웠습니다.");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private const float ButtonIconInset = 26f;
    private const float ButtonIconGap = 18f;

    /// 글자를 아이콘 오른쪽 남은 칸의 가운데에 두고, 넘치면 글자를 줄인다.
    private static bool SetIconGap(Image button, float iconSize)
    {
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null) { return false; }
        var margin = new Vector4(ButtonIconInset + iconSize + ButtonIconGap, 0f, 12f, 0f);
        if (label.margin == margin) { return false; }
        label.margin = margin;
        label.fontSizeMax = label.fontSize;
        label.fontSizeMin = 18f;
        label.enableAutoSizing = true;
        return true;
    }

    /// 격납고 구획 제목 띠: 왼쪽 아이콘 + 왼쪽 정렬 글자.
    private static void MakeHangarHeader(RectTransform canvas, string name, string icon, Vector2 pos, TMP_FontAsset font, string text)
    {
        MakeUiBox(canvas, name + "Strip", pos, new Vector2(1360f, 90f), new Color(0.35f, 1f, 0.45f, 0.14f));
        MakeIcon(canvas, name + "Icon", LoadIcon(icon), pos + new Vector2(-620f, 0f), 72f);
        TextMeshProUGUI t = MakeUiText(canvas, name, pos, new Vector2(1300f, 80f), 52f, font, text);
        t.alignment = TextAlignmentOptions.Left;
        t.margin = new Vector4(100f, 0f, 0f, 0f);
    }

    /// 버튼 왼쪽에 아이콘을 붙이고 글자를 그만큼 오른쪽으로 민다.
    private static void AddButtonIcon(Image button, Sprite icon, float size, Color color)
    {
        float x = -button.rectTransform.sizeDelta.x * 0.5f + size * 0.5f + ButtonIconInset;
        MakeIcon(button.rectTransform, "Icon", icon, new Vector2(x, 0f), size).color = color;
        SetIconGap(button, size);
    }

    private static void MakeFlowStatus(RectTransform canvas, Vector2 pos, Vector2 size, float fontSize, TMP_FontAsset font, float marginLeft = 30f)
    {
        TextMeshProUGUI t = MakeUiText(canvas, FlowStatusName, pos, size, fontSize, font, "SORTIE ROSTER");
        t.alignment = TextAlignmentOptions.TopLeft;
        t.margin = new Vector4(marginLeft, 10f, 30f, 10f);
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
    private static Image MakeFlowButton(RectTransform canvas, string name, Vector2 pos, Vector2 size, TMP_FontAsset font, string label, int action, float fontSize = 56f)
    {
        Image img = MakeUiButton(canvas, name, pos, size, font, label, fontSize);
        ACO_FlowButton fb = img.gameObject.AddUdonSharpComponent<ACO_FlowButton>();
        fb.Action = action;
        fb.Label = img.GetComponentInChildren<TextMeshProUGUI>();
        fb.Background = img;
        WireClick(img.GetComponent<Button>(), fb);
        return img;
    }

    /// 디버그 패널 프리팹(ACO_DebugPanel): 1페이즈 강제(페이즈 모듈마다 P1), 에이스·베테랑·보너스 페이즈 확률(−/+), 내 크레딧(−100K/−10K/+10K/+100K).
    /// 판 1.8m x 2.0m(월드 공간 Canvas), 앞면은 루트 +Z 쪽. 모듈 줄은 ModuleRowCount개(남는 줄은 게임 중 숨긴다). 관리자·생성기·진행도는 Bake가 연결한다.
    public const int DebugModuleRows = 14;
    public static void CreateDebugPanelPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath} — Window → TextMeshPro → Import TMP Essential Resources를 먼저 실행하세요.");
            return;
        }

        GameObject root = new GameObject("ACO_DebugPanel");
        ACO_DebugPanel panel = root.AddUdonSharpComponent<ACO_DebugPanel>();
        RectTransform canvas = MakeUiPanel(root.transform, 1800f, 2400f);
        MakeUiText(canvas, "Title", new Vector2(0f, 1120f), new Vector2(1700f, 100f), 70f, font, "DEBUG PANEL");

        // 1페이즈 강제
        MakeUiLabel(canvas, "WeightHeader", new Vector2(0f, 1040f), new Vector2(1700f, 60f), 40f, font, "FORCE PHASE 1  (P1 = this mission comes first, press again to clear)");
        var rows = new GameObject[DebugModuleRows];
        var texts = new TextMeshProUGUI[DebugModuleRows];
        for (int k = 0; k < DebugModuleRows; k++)
        {
            float y = 970f - k * 72f;
            var row = new GameObject($"Module_{k}", typeof(RectTransform));
            row.layer = 0;
            RectTransform rr = row.GetComponent<RectTransform>();
            rr.SetParent(canvas, false);
            rr.anchoredPosition = new Vector2(0f, y);
            rr.sizeDelta = new Vector2(1700f, 68f);
            TextMeshProUGUI t = MakeUiText(rr, "Text", new Vector2(-180f, 0f), new Vector2(1250f, 64f), 34f, font, $"MODULE {k}");
            t.alignment = TextAlignmentOptions.Left;
            DebugButton(rr, panel, font, "P1", new Vector2(625f, 0f), new Vector2(240f, 60f), ACO_DebugPanel.ACT_FIRST_PHASE, k, 0f);
            rows[k] = row;
            texts[k] = t;
        }

        // 출현 확률
        float y0 = 970f - DebugModuleRows * 72f - 30f;
        MakeUiLabel(canvas, "ChanceHeader", new Vector2(0f, y0), new Vector2(1700f, 60f), 40f, font, "SPAWN CHANCES");
        panel.AceText = DebugValueRow(canvas, panel, font, "Ace", y0 - 70f, ACO_DebugPanel.ACT_ACE, 0.05f);
        panel.VeteranText = DebugValueRow(canvas, panel, font, "Veteran", y0 - 140f, ACO_DebugPanel.ACT_VETERAN, 0.05f);
        panel.BonusText = DebugValueRow(canvas, panel, font, "Bonus", y0 - 210f, ACO_DebugPanel.ACT_BONUS, 0.1f);

        // 날씨·지형(< > = 고르기, 지형은 GENERATE로 만든다)
        float yw = y0 - 290f;
        MakeUiLabel(canvas, "WorldHeader", new Vector2(0f, yw), new Vector2(1700f, 60f), 40f, font, "WORLD  (WEATHER APPLIES NOW, TERRAIN ON GENERATE)");
        panel.WeatherText = DebugValueRow(canvas, panel, font, "Weather", yw - 70f, ACO_DebugPanel.ACT_WEATHER, 1f, "<", ">");
        panel.TerrainText = DebugValueRow(canvas, panel, font, "Terrain", yw - 140f, ACO_DebugPanel.ACT_TERRAIN_PICK, 1f, "<", ">");
        DebugButton(canvas, panel, font, "GENERATE TERRAIN", new Vector2(0f, yw - 225f), new Vector2(640f, 72f), ACO_DebugPanel.ACT_TERRAIN_GENERATE, 0, 0f);

        // 내 크레딧
        float y1 = yw - 320f;
        MakeUiLabel(canvas, "CreditsHeader", new Vector2(0f, y1), new Vector2(1700f, 60f), 40f, font, "MY CREDITS (LOCAL, SAVED)");
        TextMeshProUGUI credits = MakeUiText(canvas, "CreditsText", new Vector2(-180f, y1 - 70f), new Vector2(1250f, 64f), 36f, font, "MY CREDITS");
        credits.alignment = TextAlignmentOptions.Left;
        panel.CreditsText = credits;
        float[] amounts = { -100000f, -10000f, 10000f, 100000f };
        string[] labels = { "-100K", "-10K", "+10K", "+100K" };
        for (int i = 0; i < amounts.Length; i++)
        {
            DebugButton(canvas, panel, font, labels[i], new Vector2(-480f + i * 320f, y1 - 150f), new Vector2(280f, 70f), ACO_DebugPanel.ACT_CREDITS, 0, amounts[i]);
        }

        panel.MessageText = MakeUiText(canvas, "Message", new Vector2(0f, y1 - 240f), new Vector2(1700f, 60f), 34f, font, "CHANGES APPLY TO EVERYONE (PHASE 1 / CHANCES) OR ONLY YOU (CREDITS)");
        panel.ModuleRows = rows;
        panel.ModuleTexts = texts;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_DebugPanel.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 디버그 패널 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 씬에 두고 Bake하면 관리자·생성기·진행도에 연결됩니다. 공개 월드에선 AllowedPlayers에 이름을 넣거나 끄세요.");
        Selection.activeObject = prefab;
    }

    // ───────────── 데칼 스튜디오 ─────────────

    public const string DecalMaterialDir = "Assets/ACO_Addon/Generated/Decals";
    /// 데칼 프로젝터 틀(m) = orthographicSize × 2 — 데칼 최대 크기. 깊이(m).
    public const float DecalFrame = 6f;
    public const float DecalDepth = 0.6f;

    /// 데칼 프로젝터가 비추지 않는 레이어: 물(4)·UI(5)·PlayerLocal(10)·UiMenu(12)·MirrorReflection(18)·InternalUI(19)·FX(ACO_SceneBaker.FxLayer).
    public static int DecalIgnoreLayers()
    {
        return (1 << 4) | (1 << 5) | (1 << 10) | (1 << 12) | (1 << 18) | (1 << 19) | (1 << ACO_SceneBaker.FxLayer);
    }

    /// 데칼 프로젝터 하나(직교 정사각, 머티리얼은 프로젝터마다 따로 — 경로가 같으면 다시 쓴다).
    public static Transform CreateDecalProjector(Transform parent, string name, string materialName, out Material mat)
    {
        EnsureFolder("Assets/ACO_Addon/Generated", "Decals");
        string path = $"{DecalMaterialDir}/{materialName}.mat";
        mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader sh = Shader.Find("ACO/Decal");
            if (sh == null) { Debug.LogError("[ACO] 셰이더 ACO/Decal을 찾지 못했습니다 — 셰이더 컴파일 오류를 확인하세요."); }
            else
            {
                mat = new Material(sh) { name = materialName };
                mat.SetFloat("_FrameSize", DecalFrame);
                AssetDatabase.CreateAsset(mat, path);
            }
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Projector p = go.AddComponent<Projector>();
        p.orthographic = true;
        p.orthographicSize = DecalFrame * 0.5f;
        p.aspectRatio = 1f;
        p.nearClipPlane = 0.01f;
        p.farClipPlane = DecalDepth;
        p.material = mat;
        p.ignoreLayers = DecalIgnoreLayers();
        return go.transform;
    }

    /// 데칼 스튜디오(격납고): 패널(URL 입력 · LOAD, 데칼 1/2, SIZE ±, ROTATE ±, FLIP, MIRROR, CLEAR, HIDE OTHERS, 상태) + 데칼 건(VRC Pickup) + 미리보기 프로젝터 2개.
    /// 씬에 두고 SampleRoot(격납고 샘플 기체 기준점)와 DecalKey를 넣은 뒤 Bake — 프로젝터를 샘플 아래로 옮기고 샘플에 콜라이더를 붙인다.
    public static void CreateDecalStudioPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) { Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath}"); return; }
        Font legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (legacy == null) { legacy = Resources.GetBuiltinResource<Font>("Arial.ttf"); }

        GameObject root = new GameObject("ACO_DecalStudio");
        ACO_DecalStudio studio = root.AddUdonSharpComponent<ACO_DecalStudio>();
        RectTransform canvas = MakeUiPanel(root.transform, 1600f, 980f);
        MakeUiText(canvas, "Title", new Vector2(40f, 430f), new Vector2(1400f, 80f), 56f, font, "DECAL STUDIO");
        MakeIcon(canvas, "TitleIcon", LoadIcon("decal"), new Vector2(-250f, 430f), 84f);
        MakeUiLabel(canvas, "Hint", new Vector2(0f, 370f), new Vector2(1500f, 50f), 28f, font, "IMAGE URL (PNG/JPG, MAX 2048) - PASTE EVERY VISIT  /  GRAB THE DECAL GUN, AIM AT THE SAMPLE AIRCRAFT, PULL TRIGGER");

        // URL 입력 칸(VRCUrlInputField — VRChat 키보드)
        GameObject field = new GameObject("UrlInput", typeof(RectTransform));
        RectTransform fr = field.GetComponent<RectTransform>();
        fr.SetParent(canvas, false);
        fr.anchoredPosition = new Vector2(-170f, 280f);
        fr.sizeDelta = new Vector2(1160f, 100f);
        Image fbg = field.AddComponent<Image>();
        fbg.color = new Color(0.05f, 0.14f, 0.08f, 1f);
        var input = field.AddComponent<VRC.SDK3.Components.VRCUrlInputField>();
        input.lineType = VRC.SDK3.Components.VRCUrlInputField.LineType.SingleLine;
        Text txt = NewLegacyText(fr, "Text", legacy, "", new Color(0.35f, 1f, 0.45f, 1f));
        txt.fontSize = 36;
        Text ph = NewLegacyText(fr, "Placeholder", legacy, "https://i.imgur.com/....png", new Color(0.35f, 0.55f, 0.4f, 1f));
        ph.fontSize = 36;
        ph.fontStyle = FontStyle.Italic;
        input.textComponent = txt;
        input.placeholder = ph;
        input.targetGraphic = fbg;
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(studio);
        if (udon != null) { UnityEditor.Events.UnityEventTools.AddStringPersistentListener(input.onEndEdit, udon.SendCustomEvent, "OnUrlEntered"); }
        studio.UrlInput = input;
        AddButtonIcon(DecalButton(canvas, studio, font, "LOAD", new Vector2(600f, 280f), new Vector2(260f, 100f), ACO_DecalStudio.ACT_LOAD, 1, 44f), LoadIcon("download"), 56f, IconColor);

        // 편집
        AddButtonIcon(DecalButton(canvas, studio, font, "DECAL 1 / 2", new Vector2(-560f, 140f), new Vector2(380f, 100f), ACO_DecalStudio.ACT_SELECT, 1, 40f), LoadIcon("swap"), 56f, IconColor);
        AddButtonIcon(DecalButton(canvas, studio, font, "RESET GUN", new Vector2(590f, 140f), new Vector2(340f, 100f), ACO_DecalStudio.ACT_RESET_GUN, 1, 38f), LoadIcon("gun_reset"), 60f, IconColor);
        studio.SelectText = MakeUiText(canvas, "SelectText", new Vector2(-20f, 140f), new Vector2(680f, 80f), 34f, font, "EDITING: DECAL 1");
        studio.SelectText.alignment = TextAlignmentOptions.Left;
        AddButtonIcon(DecalButton(canvas, studio, font, "SIZE -", new Vector2(-620f, 10f), new Vector2(240f, 100f), ACO_DecalStudio.ACT_SIZE, -1, 38f), LoadIcon("size"), 48f, IconColor);
        AddButtonIcon(DecalButton(canvas, studio, font, "SIZE +", new Vector2(-360f, 10f), new Vector2(240f, 100f), ACO_DecalStudio.ACT_SIZE, 1, 38f), LoadIcon("size"), 48f, IconColor);
        AddButtonIcon(DecalButton(canvas, studio, font, "ROT <", new Vector2(-80f, 10f), new Vector2(240f, 100f), ACO_DecalStudio.ACT_ROTATE, -1, 38f), LoadIcon("rotate"), 48f, IconColor);
        AddButtonIcon(DecalButton(canvas, studio, font, "ROT >", new Vector2(180f, 10f), new Vector2(240f, 100f), ACO_DecalStudio.ACT_ROTATE, 1, 38f), LoadIcon("rotate"), 48f, IconColor);
        AddButtonIcon(DecalButton(canvas, studio, font, "FLIP", new Vector2(460f, 10f), new Vector2(240f, 100f), ACO_DecalStudio.ACT_FLIP, 1, 38f), LoadIcon("flip"), 48f, IconColor);
        studio.SizeText = MakeUiText(canvas, "SizeText", new Vector2(0f, -90f), new Vector2(1500f, 60f), 34f, font, "SIZE -");
        AddButtonIcon(DecalButton(canvas, studio, font, "MIRROR 1 > 2", new Vector2(-450f, -190f), new Vector2(460f, 100f), ACO_DecalStudio.ACT_MIRROR, 1, 40f), LoadIcon("mirror"), 60f, IconColor);
        AddButtonIcon(DecalButton(canvas, studio, font, "CLEAR", new Vector2(80f, -190f), new Vector2(300f, 100f), ACO_DecalStudio.ACT_CLEAR, 1, 40f), LoadIcon("clear"), 60f, IconColor);
        AddButtonIcon(DecalButton(canvas, studio, font, "HIDE OTHERS", new Vector2(520f, -190f), new Vector2(420f, 100f), ACO_DecalStudio.ACT_HIDE_OTHERS, 1, 40f), LoadIcon("hide"), 60f, IconColor);
        studio.HideText = MakeUiText(canvas, "HideText", new Vector2(-180f, -300f), new Vector2(1140f, 60f), 32f, font, "OTHERS' DECALS: SHOWN");
        studio.StatusText = MakeUiText(canvas, "StatusText", new Vector2(-180f, -405f), new Vector2(1140f, 130f), 32f, font, "");

        // 불러온 이미지 창(오른쪽 아래)
        MakeUiLabel(canvas, "LoadedTitle", new Vector2(610f, -262f), new Vector2(300f, 36f), 24f, font, "LOADED IMAGE");
        MakeUiBox(canvas, "LoadedFrame", new Vector2(610f, -378f), new Vector2(208f, 208f), new Color(0.35f, 1f, 0.45f, 0.5f));
        MakeUiBox(canvas, "LoadedBack", new Vector2(610f, -378f), new Vector2(204f, 204f), new Color(0.02f, 0.05f, 0.04f, 1f));
        TextMeshProUGUI loadedEmpty = MakeUiLabel(canvas, "LoadedEmpty", new Vector2(610f, -378f), new Vector2(190f, 60f), 26f, font, "NO IMAGE");
        GameObject loadedGo = new GameObject("LoadedImage", typeof(RectTransform));
        RectTransform loadedRect = loadedGo.GetComponent<RectTransform>();
        loadedRect.SetParent(canvas, false);
        loadedRect.anchoredPosition = new Vector2(610f, -378f);
        loadedRect.sizeDelta = new Vector2(200f, 200f);
        RawImage loadedImage = loadedGo.AddComponent<RawImage>();
        loadedImage.raycastTarget = false;
        loadedImage.enabled = false;
        studio.LoadedImage = loadedImage;
        studio.LoadedEmpty = loadedEmpty.gameObject;

        // 미리보기 프로젝터(Bake가 샘플 기체 아래로 옮긴다)
        var projectors = new Transform[2];
        var mats = new Material[2];
        for (int i = 0; i < 2; i++)
        {
            Material m;
            projectors[i] = CreateDecalProjector(root.transform, "PreviewDecal_" + (i + 1), "ACO_Decal_Preview_" + i, out m);
            mats[i] = m;
        }
        studio.PreviewProjectors = projectors;
        studio.PreviewMaterials = mats;

        // 데칼 건(VRC Pickup — 로컬 도구)
        GameObject gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gun.name = "DecalGun";
        gun.transform.SetParent(root.transform, false);
        gun.transform.localPosition = new Vector3(1.0f, -0.35f, 0.25f);
        gun.transform.localScale = new Vector3(0.05f, 0.09f, 0.24f);
        MeshRenderer gr = gun.GetComponent<MeshRenderer>();
        gr.sharedMaterial = LoadOrSaveMaterial(Shader.Find("Unlit/Color"), new Color(0.12f, 0.35f, 0.18f, 1f), "ACO_DecalGun");
        Rigidbody rb = gun.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var pickup = gun.AddComponent<VRC.SDK3.Components.VRCPickup>();
        pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;
        pickup.orientation = VRC.SDKBase.VRC_Pickup.PickupOrientation.Gun;   // 잡는 자세와 상관없이 총구가 앞을 본다
        pickup.ExactGun = NewChild(gun.transform, "Grip", new Vector3(0f, -0.3f, -0.3f));
        // ExactGun의 +X가 총구 방향, +Z가 총의 위쪽이다(SAV 그래플링 훅과 같은 규칙) — 그대로 두면 총구가 위를 본다
        pickup.ExactGun.localRotation = Quaternion.Euler(-90f, -90f, 0f);
        pickup.InteractionText = "DECAL GUN";
        pickup.UseText = "STAMP DECAL";
        Transform muzzle = NewChild(gun.transform, "Muzzle", new Vector3(0f, 0f, 0.5f));
        ACO_DecalGun g = gun.AddUdonSharpComponent<ACO_DecalGun>();
        g.Studio = studio;
        g.Muzzle = muzzle;
        g.Pickup = pickup;
        studio.Gun = g;
        // 찍는 소리(스프레이) — 로컬, 건 자리에서
        AudioSource stamp = gun.AddComponent<AudioSource>();
        stamp.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ACO_Addon/Audio/Generated/ACO_Decal_Spray.wav");
        stamp.playOnAwake = false;
        stamp.spatialBlend = 1f;
        stamp.dopplerLevel = 0f;
        stamp.minDistance = 0.5f;
        stamp.maxDistance = 8f;
        g.StampSound = stamp;
        GameObject beamGo = new GameObject("Beam");
        beamGo.transform.SetParent(gun.transform, false);
        LineRenderer beam = beamGo.AddComponent<LineRenderer>();
        beam.positionCount = 2;
        beam.useWorldSpace = true;
        beam.startWidth = 0.004f;
        beam.endWidth = 0.004f;
        beam.sharedMaterial = LoadOrSaveMaterial(Shader.Find("Unlit/Color"), new Color(0.35f, 1f, 0.45f, 1f), "ACO_DecalBeam");
        beam.shadowCastingMode = ShadowCastingMode.Off;
        beam.receiveShadows = false;
        beam.enabled = false;
        g.Beam = beam;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_DecalStudio.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 데칼 스튜디오 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 격납고에 두고 SampleRoot(샘플 기체 기준점)·DecalKey를 넣은 뒤 Bake하세요.");
        Selection.activeObject = prefab;
    }

    private static Image DecalButton(RectTransform parent, ACO_DecalStudio studio, TMP_FontAsset font, string label, Vector2 pos, Vector2 size, int action, int step, float fontSize)
    {
        Image img = MakeUiButton(parent, $"Btn_{action}_{step}_{label}", pos, size, font, label, fontSize);
        ACO_DecalButton b = img.gameObject.AddUdonSharpComponent<ACO_DecalButton>();
        b.Studio = studio;
        b.Action = action;
        b.Step = step;
        WireClick(img.GetComponent<Button>(), b);
        return img;
    }

    // ───────────── TAC 네임 패널 ─────────────

    /// TAC 네임(콜사인) 패널: 월드 UI 판 1.4 x 0.6m — 제목, 입력 칸(UI InputField — VRChat 키보드, 입력 끝나면 저장), SET · CLEAR, 지금 콜사인·안내 글자.
    /// 콜사인 패널 아래에 붙는 헐넘버 패널(같은 ACO_TacName이 맡는다).
    private static void AddHullPanel(GameObject root, ACO_TacName tac, TMP_FontAsset font, Font legacy)
    {
        RectTransform canvas = MakeUiPanel(root.transform, 1400f, 340f);
        canvas.gameObject.name = "HullPanel";
        canvas.localPosition += Vector3.down * 0.49f;
        MakeUiText(canvas, "Title", new Vector2(0f, 125f), new Vector2(1300f, 70f), 48f, font, "HULL NUMBER");
        GameObject field = new GameObject("HullInput", typeof(RectTransform));
        RectTransform fr = field.GetComponent<RectTransform>();
        fr.SetParent(canvas, false);
        fr.anchoredPosition = new Vector2(-150f, 20f);
        fr.sizeDelta = new Vector2(900f, 100f);
        Image bg = field.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.14f, 0.08f, 1f);
        InputField input = field.AddComponent<InputField>();
        input.characterLimit = tac.HullMaxLength;
        input.lineType = InputField.LineType.SingleLine;
        Text txt = NewLegacyText(fr, "Text", legacy, "", new Color(0.35f, 1f, 0.45f, 1f));
        Text ph = NewLegacyText(fr, "Placeholder", legacy, "A-Z 0-9  (MAX 6)", new Color(0.35f, 0.55f, 0.4f, 1f));
        ph.fontStyle = FontStyle.Italic;
        input.textComponent = txt;
        input.placeholder = ph;
        input.targetGraphic = bg;
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(tac);
        if (udon != null) { UnityEditor.Events.UnityEventTools.AddStringPersistentListener(input.onEndEdit, udon.SendCustomEvent, "SubmitHull"); }
        Image set = MakeUiButton(canvas, "SetHull", new Vector2(460f, 20f), new Vector2(240f, 100f), font, "SET", 46f);
        WireEvent(set.GetComponent<Button>(), tac, "SubmitHull");
        Image clear = MakeUiButton(canvas, "ClearHull", new Vector2(460f, -100f), new Vector2(240f, 80f), font, "CLEAR", 36f);
        WireEvent(clear.GetComponent<Button>(), tac, "ClearHull");
        tac.HullText = MakeUiText(canvas, "HullCurrent", new Vector2(-150f, -100f), new Vector2(900f, 70f), 34f, font, "YOUR HULL NUMBER: -");
        tac.HullText.alignment = TextAlignmentOptions.Left;
        tac.HullInput = input;
    }

    /// 먼저 만든 콜사인 패널에 헐넘버 패널을 더한다 — 다시 만들면 직접 고친 내용이 사라진다.
    public static void EnsureTacHullPanel()
    {
        string path = $"{PrefabDir}/ACO_TacNamePanel.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Font legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null || legacy == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_TacName tac = root.GetComponent<ACO_TacName>();
            if (tac == null || tac.HullInput != null) { return; }
            AddHullPanel(root, tac, font, legacy);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] 콜사인 패널에 헐넘버 패널을 넣었습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ───────────── 기체 표시(헐넘버·이름·킬마크) ─────────────

    private const string MarkFontPath = "Assets/ACO_Addon/Fonts/ACO_MarkFont.asset";
    private const string MarkFontSource = "Assets/ACO_Addon/Fonts/BlackOpsOne-Regular.ttf";
    // 콜사인·헐넘버 입력 규칙과 같은 글자만 담은 고정 폰트 — 빌드에 폰트 파일이 들어가지 않는다
    // 밑줄은 쓰지 않지만 없으면 TextMeshPro가 경고를 낸다
    private const string MarkFontChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_ ";
    private const string MarkMaterialPath = "Assets/ACO_Addon/Generated/ACO_MarkText.mat";
    private const string MarkIconPath = "Assets/ACO_Addon/Textures/Marks/ACO_KillMarks.png";
    private const string MarkIconMaterialPath = "Assets/ACO_Addon/Generated/ACO_MarkIcons.mat";

    private static TMP_FontAsset EnsureMarkFont()
    {
        TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MarkFontPath);
        if (asset != null)
        {
            if (!asset.HasCharacter('_'))
            {
                asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                asset.TryAddCharacters(MarkFontChars, out string _);
                asset.atlasPopulationMode = AtlasPopulationMode.Static;
                EditorUtility.SetDirty(asset);
            }
            return asset;
        }
        Font source = AssetDatabase.LoadAssetAtPath<Font>(MarkFontSource);
        if (source == null) { Debug.LogWarning($"[ACO] 기체 표시 폰트가 없습니다: {MarkFontSource}"); return null; }
        asset = TMP_FontAsset.CreateFontAsset(source, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 512, 512, AtlasPopulationMode.Dynamic, false);
        if (asset == null) { Debug.LogWarning("[ACO] 기체 표시 폰트를 만들지 못했습니다."); return null; }
        asset.name = "ACO_MarkFont";
        AssetDatabase.CreateAsset(asset, MarkFontPath);
        // 아틀라스와 머티리얼은 폰트 에셋 안에 넣어야 저장된다
        asset.atlasTexture.name = "ACO_MarkFont Atlas";
        AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
        asset.material.name = "ACO_MarkFont Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        if (!asset.TryAddCharacters(MarkFontChars, out string missing)) { Debug.LogWarning($"[ACO] 기체 표시 폰트에 없는 글자: {missing}"); }
        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }

    private static Material EnsureMarkIconMaterial(Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MarkIconMaterialPath);
        if (material != null) { return material; }
        if (AssetImporter.GetAtPath(MarkIconPath) is TextureImporter importer)
        {
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        material = new Material(shader) { name = "ACO_MarkIcons" };
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(MarkIconPath));
        AssetDatabase.CreateAsset(material, MarkIconMaterialPath);
        return material;
    }

    private static TextMeshPro MakeMark(Transform parent, string name, Vector3 pos, float yaw, Vector2 size, float fontSize, TMP_FontAsset font, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        TextMeshPro t = go.AddComponent<TextMeshPro>();
        t.font = font;
        t.fontSharedMaterial = material;
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Truncate;
        t.rectTransform.sizeDelta = size;
        t.text = "";
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return t;
    }

    private static MeshFilter MakeKillMark(Transform parent, string name, Vector3 pos, float yaw, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        MeshFilter filter = go.AddComponent<MeshFilter>();
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return filter;
    }

    private const string KillPreviewPath = "Assets/ACO_Addon/Generated/ACO_KillMarkPreview.asset";
    private const int KillPreviewKills = 199;   // 평소 가장 넓은 모양: 100 하나, 10 아홉, 1 아홉

    /// 킬마크는 실행 중에 메시를 만들어 에디터에 보이지 않는다 — 자리를 잡도록 넓은 경우의 모양을 넣어 둔다.
    /// 실행하면 ACO_VehicleMarks가 실제 값으로 바꾼다. 크기·칸 수를 고쳤으면 Bake가 다시 만든다.
    private static bool ApplyKillPreview(ACO_VehicleMarks marks)
    {
        if (marks == null || marks.KillFilters == null) { return false; }
        int hundreds = KillPreviewKills / 100;
        int tens = KillPreviewKills / 10 % 10;
        int n = hundreds + tens + KillPreviewKills % 10;
        var v = new Vector3[n * 4];
        var uv = new Vector2[n * 4];
        var c = new Color[n * 4];
        var tris = new int[n * 6];
        int cols = Mathf.Max(marks.IconColumns, 1);
        int rows = (n + cols - 1) / cols;
        float size = marks.IconSize;
        const float cell = 1f / 3f;
        for (int i = 0; i < n; i++)
        {
            int kind = i < hundreds ? 2 : (i < hundreds + tens ? 1 : 0);
            int row = i / cols;
            int inRow = row < rows - 1 ? cols : n - row * cols;
            float x = (i % cols - inRow * 0.5f) * size;
            float y = (rows * 0.5f - row - 1f) * size;
            int b = i * 4;
            v[b] = new Vector3(x, y, 0f);
            v[b + 1] = new Vector3(x, y + size, 0f);
            v[b + 2] = new Vector3(x + size, y + size, 0f);
            v[b + 3] = new Vector3(x + size, y, 0f);
            float u = kind * cell;
            uv[b] = new Vector2(u, 0f);
            uv[b + 1] = new Vector2(u, 1f);
            uv[b + 2] = new Vector2(u + cell, 1f);
            uv[b + 3] = new Vector2(u + cell, 0f);
            for (int k = 0; k < 4; k++) { c[b + k] = marks.IconColor; }
            int t = i * 6;
            tris[t] = b;
            tris[t + 1] = b + 1;
            tris[t + 2] = b + 2;
            tris[t + 3] = b;
            tris[t + 4] = b + 2;
            tris[t + 5] = b + 3;
        }
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(KillPreviewPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "ACO_KillMarkPreview" };
            AssetDatabase.CreateAsset(mesh, KillPreviewPath);
        }
        mesh.Clear();
        mesh.vertices = v;
        mesh.uv = uv;
        mesh.colors = c;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        bool changed = false;
        foreach (MeshFilter f in marks.KillFilters)
        {
            if (f == null || f.sharedMesh == mesh) { continue; }
            f.sharedMesh = mesh;
            changed = true;
        }
        return changed;
    }

    /// 기체 하나에 표시를 만든다. 자리는 FA-18 기준 어림값 — 프리팹에서 옮겨 맞춘다.
    private static void AddVehicleMarks(Transform vehicle, SaccFlightAndVehicles.SaccEntity entity)
    {
        TMP_FontAsset font = EnsureMarkFont();
        Shader shader = Shader.Find("ACO/MarkText");
        if (font == null || shader == null) { Debug.LogWarning("[ACO] 기체 표시: 폰트나 셰이더 ACO/MarkText를 찾지 못했습니다."); return; }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MarkMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "ACO_MarkText" };
            material.SetTexture("_MainTex", font.atlasTexture);
            AssetDatabase.CreateAsset(material, MarkMaterialPath);
        }
        Material icons = EnsureMarkIconMaterial(shader);
        var go = new GameObject("ACO_Marks");
        go.transform.SetParent(vehicle, false);
        ACO_VehicleMarks marks = go.AddUdonSharpComponent<ACO_VehicleMarks>();
        var holder = new GameObject("Marks");
        holder.transform.SetParent(go.transform, false);
        var hull = new TextMeshPro[2];
        var names = new TextMeshPro[2];
        var kills = new MeshFilter[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            string tag = i == 0 ? "_L" : "_R";
            float yaw = -side * 90f;   // 글자는 뒤(-Z)에서 읽힌다 — 바깥에서 보면 바로 읽히게
            hull[i] = MakeMark(holder.transform, "Hull" + tag, new Vector3(side * 1.75f, 2.2f, -6.4f), yaw, new Vector2(1.6f, 0.6f), 4f, font, material);
            names[i] = MakeMark(holder.transform, "Name" + tag, new Vector3(side * 0.66f, 1.1f, 4.3f), yaw, new Vector2(1.4f, 0.16f), 1f, font, material);
            kills[i] = MakeKillMark(holder.transform, "Kills" + tag, new Vector3(side * 0.66f, 0.9f, 4.3f), yaw, icons);
        }
        marks.EntityControl = entity;
        marks.PreviewMode = entity == null;
        marks.Root = holder;
        marks.HullTexts = hull;
        marks.NameTexts = names;
        marks.KillFilters = kills;
        ApplyKillPreview(marks);
        ACO_SyncModeFixer.FixHierarchy(go, false);
    }

    /// 글자는 RectTransform이라 자리가 anchoredPosition에 있다(localPosition은 비어 있을 수 있다). 바뀌었으면 true.
    private static bool CopyMarkPose(Transform from, Transform to)
    {
        bool changed = to.localRotation != from.localRotation || to.localScale != from.localScale;
        to.localRotation = from.localRotation;
        to.localScale = from.localScale;
        if (from is RectTransform a && to is RectTransform b)
        {
            changed |= b.anchoredPosition3D != a.anchoredPosition3D || b.sizeDelta != a.sizeDelta;
            b.anchoredPosition3D = a.anchoredPosition3D;
            b.sizeDelta = a.sizeDelta;
            TextMeshPro ta = from.GetComponent<TextMeshPro>();
            TextMeshPro tb = to.GetComponent<TextMeshPro>();
            if (ta != null && tb != null && tb.fontSize != ta.fontSize)
            {
                tb.fontSize = ta.fontSize;
                changed = true;
            }
            return changed;
        }
        changed |= to.localPosition != from.localPosition;
        to.localPosition = from.localPosition;
        return changed;
    }

    private static void CopyMarkPoses(Transform from, Transform to)
    {
        foreach (Transform source in from)
        {
            Transform copy = to.Find(source.name);
            if (copy == null) { continue; }
            Undo.RecordObject(copy, "ACO marks");
            if (!CopyMarkPose(source, copy)) { continue; }
            PrefabUtility.RecordPrefabInstancePropertyModifications(copy);
            EditorUtility.SetDirty(copy);
        }
    }

    /// 기체 프리팹 하나만 고치면 모든 기체가 같은 자리에 표시된다. 프리팹이 아니면 씬의 기체에 넣는다.
    /// SaccEntity는 프리팹 루트가 아닐 수 있다(FA-18은 자식 F/A-18E) — 그 아래에 둔다.
    /// template: 처음 넣을 때 따를 자리(먼저 샘플 기체에서 잡은 자리).
    public static void EnsureVehicleMarks(SaccFlightAndVehicles.SaccEntity entity, Transform template)
    {
        EnsureMarkFont();
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(entity.gameObject);
        if (string.IsNullOrEmpty(path))
        {
            Transform here = entity.transform.Find("ACO_Marks");
            if (here != null) { ApplyKillPreview(here.GetComponent<ACO_VehicleMarks>()); return; }
            AddVehicleMarks(entity.transform, entity);
            Transform made = entity.transform.Find("ACO_Marks/Marks");
            if (template != null && made != null) { CopyMarkPoses(template, made); }
            return;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var e = root.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true);
            if (e == null)
            {
                Debug.LogWarning($"[ACO] 기체 표시: {path}에서 SaccEntity를 찾지 못했습니다.");
                return;
            }
            Transform existing = e.transform.Find("ACO_Marks");
            if (existing != null)
            {
                if (ApplyKillPreview(existing.GetComponent<ACO_VehicleMarks>())) { PrefabUtility.SaveAsPrefabAsset(root, path); }
                return;
            }
            AddVehicleMarks(e.transform, e);
            Transform marks = e.transform.Find("ACO_Marks/Marks");
            if (template != null && marks != null) { CopyMarkPoses(template, marks); }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[ACO] 기체 표시를 넣었습니다: {path} — 글자 자리는 프리팹의 ACO_Marks/Marks 아래에서 옮기세요.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 격납고 샘플 기체에 내 표시를 얹고, 기체 프리팹에서 맞춘 자리를 그대로 복사한다 — 자리는 프리팹 한 곳만 고치면 된다.
    public static void EnsurePreviewMarks(Transform sample, SaccFlightAndVehicles.SaccEntity vehicle)
    {
        if (sample == null || vehicle == null) { return; }
        if (sample.Find("ACO_Marks") == null)
        {
            AddVehicleMarks(sample, null);
            Debug.Log("[ACO] 격납고 샘플 기체에 기체 표시를 넣었습니다.");
        }
        ApplyKillPreview(sample.Find("ACO_Marks").GetComponent<ACO_VehicleMarks>());
        Transform to = sample.Find("ACO_Marks/Marks");
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(vehicle.gameObject);
        GameObject asset = string.IsNullOrEmpty(path) ? vehicle.gameObject : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var e = asset != null ? asset.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true) : null;
        Transform from = e != null ? e.transform.Find("ACO_Marks/Marks") : null;
        if (to != null && from != null) { CopyMarkPoses(from, to); }
    }

    public static void CreateTacNamePanelPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) { Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath}"); return; }
        Font legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (legacy == null) { legacy = Resources.GetBuiltinResource<Font>("Arial.ttf"); }

        GameObject root = new GameObject("ACO_TacNamePanel");
        ACO_TacName tac = root.AddUdonSharpComponent<ACO_TacName>();
        RectTransform canvas = MakeUiPanel(root.transform, 1400f, 600f);
        MakeUiText(canvas, "Title", new Vector2(0f, 235f), new Vector2(1300f, 80f), 56f, font, "TAC NAME (CALLSIGN)");
        MakeUiLabel(canvas, "Hint", new Vector2(0f, 170f), new Vector2(1300f, 50f), 30f, font, "SHOWN ABOVE YOUR AIRCRAFT TO OTHER PILOTS  -  A-Z 0-9 SPACE -  (MAX 12)");

        // 입력 칸: 배경 + InputField + 글자 + 자리표시
        GameObject field = new GameObject("Input", typeof(RectTransform));
        RectTransform fr = field.GetComponent<RectTransform>();
        fr.SetParent(canvas, false);
        fr.anchoredPosition = new Vector2(-150f, 60f);
        fr.sizeDelta = new Vector2(900f, 110f);
        Image fbg = field.AddComponent<Image>();
        fbg.color = new Color(0.05f, 0.14f, 0.08f, 1f);
        InputField input = field.AddComponent<InputField>();
        input.characterLimit = 12;
        input.lineType = InputField.LineType.SingleLine;
        Text txt = NewLegacyText(fr, "Text", legacy, "", new Color(0.35f, 1f, 0.45f, 1f));
        Text ph = NewLegacyText(fr, "Placeholder", legacy, "ENTER CALLSIGN", new Color(0.35f, 0.55f, 0.4f, 1f));
        ph.fontStyle = FontStyle.Italic;
        input.textComponent = txt;
        input.placeholder = ph;
        input.targetGraphic = fbg;
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(tac);
        if (udon != null) { UnityEditor.Events.UnityEventTools.AddStringPersistentListener(input.onEndEdit, udon.SendCustomEvent, "Submit"); }

        Image set = MakeUiButton(canvas, "Set", new Vector2(460f, 60f), new Vector2(240f, 110f), font, "SET", 50f);
        WireEvent(set.GetComponent<Button>(), tac, "Submit");
        Image clear = MakeUiButton(canvas, "Clear", new Vector2(460f, -75f), new Vector2(240f, 90f), font, "CLEAR", 40f);
        WireEvent(clear.GetComponent<Button>(), tac, "ClearName");

        tac.CurrentText = MakeUiText(canvas, "Current", new Vector2(-150f, -75f), new Vector2(900f, 70f), 34f, font, "YOUR CALLSIGN: -");
        tac.CurrentText.alignment = TextAlignmentOptions.Left;
        tac.MessageText = MakeUiText(canvas, "Message", new Vector2(0f, -200f), new Vector2(1300f, 60f), 32f, font, "");
        tac.Input = input;
        AddHullPanel(root, tac, font, legacy);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_TacNamePanel.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] TAC 네임 패널 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 격납고 등에 두고 Bake하면 표적 마커(아군기 콜사인)에 연결됩니다.");
        Selection.activeObject = prefab;
    }

    private static Text NewLegacyText(RectTransform parent, string name, Font font, string text, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(24f, 8f);
        rt.offsetMax = new Vector2(-24f, -8f);
        Text t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = 60;
        t.alignment = TextAnchor.MiddleLeft;
        t.color = color;
        t.text = text;
        t.supportRichText = false;
        return t;
    }

    /// 버튼을 누르면 그 U# 스크립트의 eventName이 불리게 한다(UdonBehaviour.SendCustomEvent).
    private static void WireEvent(Button button, UdonSharp.UdonSharpBehaviour target, string eventName)
    {
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
        if (udon == null) { Debug.LogError($"[ACO] '{target.name}'의 UdonBehaviour를 찾지 못해 버튼을 연결하지 못했습니다.", target); return; }
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(button.onClick, udon.SendCustomEvent, eventName);
    }

    // ───────────── 비행 HUD ─────────────

    private static readonly Color FlightGreen = new Color(0.35f, 1f, 0.45f, 0.9f);
    private const float FlightTextScale = 1.5f;

    // 값 상자는 테이프 숫자 바깥에 둔다 — 안쪽에 두면 흘러가는 숫자와 겹친다
    private const float FlightValueBoxX = 21.6f;
    private const float FlightValueTextX = 19.2f;
    private const float FlightThrottleX = -26f;
    private const float FlightThrottleMarkX = -0.75f;
    private static readonly Vector3 FlightEcmTextPos = new Vector3(0f, 9.2f, 0f);

    /// 막대를 가리키는 > 표시(끝이 원점).
    private static Mesh ThrottleMarkMesh()
    {
        return SaveMesh(LineMesh("ACO_FHud_ThrottleMark", 0.09f,
            new Vector2(-0.8f, 0.45f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(-0.8f, -0.45f)), "ACO_FHud_ThrottleMark");
    }

    /// 이미 있는 비행 HUD 프리팹을 고친다: 값 상자를 테이프 바깥으로 옮기고 ECM SUPPORT 문구와 스로틀 위치 표시를 더한다. 다시 만들면 고친 내용이 사라진다.
    public static void EnsureFlightHudLayout()
    {
        string path = $"{PrefabDir}/ACO_FlightHud.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_FlightHud hud = root.GetComponent<ACO_FlightHud>();
            if (hud == null || hud.Canvas == null) { return; }
            bool layout = hud.EcmText == null;
            bool mark = hud.ThrottleTarget == null && hud.ThrottleAbLine != null;
            if (!layout && !mark) { return; }
            if (layout) { ApplyFlightValueLayout(hud); }
            if (mark) { AddThrottleMark(hud); }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void AddThrottleMark(ACO_FlightHud hud)
    {
        Transform parent = hud.ThrottleAbLine.parent;
        GameObject mark = Object.Instantiate(hud.ThrottleAbLine.gameObject, parent);
        mark.name = "TargetMark";
        mark.transform.localPosition = new Vector3(FlightThrottleMarkX, 0f, 0f);
        mark.GetComponent<MeshFilter>().sharedMesh = ThrottleMarkMesh();
        hud.ThrottleTarget = mark.transform;
        var lines = new List<MeshRenderer>(hud.LineRenderers) { mark.GetComponent<MeshRenderer>() };
        hud.LineRenderers = lines.ToArray();
        Debug.Log("[ACO] 비행 HUD: 스로틀 위치 표시(>)를 넣었습니다.");
    }

    private static void ApplyFlightValueLayout(ACO_FlightHud hud)
    {
        Transform canvas = hud.Canvas;
        MoveX(canvas, "SpeedBox", -FlightValueBoxX);
        MoveX(canvas, "SpeedText", -FlightValueTextX);
        MoveX(canvas, "AltBox", FlightValueBoxX);
        MoveX(canvas, "AltText", FlightValueTextX);
        MoveX(canvas, "RadarAltText", FlightValueTextX);
        MoveX(canvas, "Throttle", FlightThrottleX);
        MoveX(canvas, "ThrottleText", FlightThrottleX);
        TextMeshPro ecm = Object.Instantiate(hud.ThrottleText.gameObject, canvas).GetComponent<TextMeshPro>();
        ecm.gameObject.name = "EcmText";
        ecm.rectTransform.anchoredPosition = FlightEcmTextPos;
        ecm.fontSize = 8f * FlightTextScale;
        ecm.alignment = TextAlignmentOptions.Center;
        ecm.text = "ECM SUPPORT";
        ecm.gameObject.SetActive(false);
        hud.EcmText = ecm;
        var texts = new List<TextMeshPro>(hud.AllTexts) { ecm };
        hud.AllTexts = texts.ToArray();
        Debug.Log("[ACO] 비행 HUD: 속도·고도 값 상자를 테이프 바깥으로 옮기고 ECM SUPPORT 문구를 넣었습니다.");
    }

    private static void MoveX(Transform parent, string name, float x)
    {
        Transform t = parent.Find(name);
        if (t == null) { return; }
        // 글자는 RectTransform이라 자리가 anchoredPosition에 있다(프리팹의 localPosition은 0)
        if (t is RectTransform rect)
        {
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
            return;
        }
        Vector3 p = t.localPosition;
        p.x = x;
        t.localPosition = p;
    }

    /// 비행 HUD 프리팹(ACO_FlightHud + ACO_Units): Root(머리 + 기체 자세로 움직인다) → Canvas(1단위 = 1°). 모두 UI 레이어, 선은 ACO/MarkerOverlay(맨 위), 글자는 TMP 오버레이.
    /// 씬에 하나 두면 Bake가 플레이어 기체(무장·랜딩기어 등)를 연결하고 SAV HUD를 숨긴다.
    public static void CreateFlightHudPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Generated");
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        // 글꼴: Electronic Highway Sign(TMP 예제 — 없으면 기본 글꼴). 머티리얼은 그 글꼴 아틀라스로 오버레이 셰이더(맨 위에 그린다 — 글꼴의 기본 머티리얼을
        // 그대로 쓰면 깊이 검사를 해서 조종석 구조물에 가려 안 보인다)
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FlightFontPath);
        if (font == null)
        {
            Debug.LogWarning($"[ACO] 비행 HUD: 글꼴 {FlightFontPath}이 없어 기본 글꼴을 씁니다(TextMesh Pro 예제를 가져오면 생긴다).");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        }
        Shader lineShader = Shader.Find("ACO/MarkerOverlay");
        Shader textShader = Shader.Find("TextMeshPro/Distance Field Overlay");
        if (font == null || lineShader == null || textShader == null)
        {
            Debug.LogError($"[ACO] 비행 HUD: TMP 폰트({FontPath}) 또는 셰이더(ACO/MarkerOverlay, TextMeshPro/Distance Field Overlay)를 찾지 못했습니다.");
            return;
        }
        // 예제 글꼴은 동적 아틀라스(글자 20개만 들어 있고 빌드 때 비운다) — 출력 가능한 ASCII를 미리 넣고 정적으로 굳힌다(VRChat에서 글자가 빠지지 않게)
        if (font.atlasPopulationMode == AtlasPopulationMode.Dynamic)
        {
            var ascii = new System.Text.StringBuilder();
            for (int ch = 32; ch < 127; ch++) { ascii.Append((char)ch); }
            font.TryAddCharacters(ascii.ToString(), out string missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(font);
            if (font.atlasTextures != null) { foreach (Texture2D t in font.atlasTextures) { if (t != null) { EditorUtility.SetDirty(t); } } }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ACO] 비행 HUD 글꼴 '{font.name}': ASCII 글자를 아틀라스에 넣고 정적으로 바꿨습니다" + (string.IsNullOrEmpty(missing) ? "." : $"(글꼴에 없는 글자: {missing})."), font);
        }
        Material lineMat = LoadOrSaveMaterial(lineShader, FlightGreen, "ACO_FlightHudLine");
        string textMatPath = $"{GeneratedDir}/ACO_FlightHudTextOverlay.mat";
        Material textMat = AssetDatabase.LoadAssetAtPath<Material>(textMatPath);
        if (textMat != null && textMat.mainTexture != font.material.mainTexture)
        {
            // 글꼴이 바뀌었다 — 그 글꼴 머티리얼 값(아틀라스 포함)을 다시 복사한다
            textMat.CopyPropertiesFromMaterial(font.material);
            textMat.shader = textShader;
            EditorUtility.SetDirty(textMat);
        }
        if (textMat == null) { textMat = SaveAsset(new Material(font.material) { shader = textShader, name = "ACO_FlightHudTextOverlay" }, textMatPath); }

        // 메시(1단위 = 1°)
        float th = 0.06f;
        Mesh tickDown = SaveMesh(LineMesh("ACO_FHud_TickDown", th, new Vector2(0f, 0f), new Vector2(0f, -0.9f)), "ACO_FHud_TickDown");
        Mesh tickRight = SaveMesh(LineMesh("ACO_FHud_TickRight", th, new Vector2(0f, 0f), new Vector2(0.8f, 0f)), "ACO_FHud_TickRight");
        Mesh tickLeft = SaveMesh(LineMesh("ACO_FHud_TickLeft", th, new Vector2(0f, 0f), new Vector2(-0.8f, 0f)), "ACO_FHud_TickLeft");
        Mesh rungUp = SaveMesh(LineMesh("ACO_FHud_RungUp", th,
            new Vector2(-6.5f, 0f), new Vector2(-1.8f, 0f), new Vector2(-6.5f, 0f), new Vector2(-6.5f, -0.6f),
            new Vector2(1.8f, 0f), new Vector2(6.5f, 0f), new Vector2(6.5f, 0f), new Vector2(6.5f, -0.6f)), "ACO_FHud_RungUp");
        Mesh rungDown = SaveMesh(LineMesh("ACO_FHud_RungDown", th,
            new Vector2(-6.5f, 0f), new Vector2(-5.4f, 0f), new Vector2(-4.7f, 0f), new Vector2(-3.6f, 0f), new Vector2(-2.9f, 0f), new Vector2(-1.8f, 0f),
            new Vector2(-6.5f, 0f), new Vector2(-6.5f, 0.6f),
            new Vector2(1.8f, 0f), new Vector2(2.9f, 0f), new Vector2(3.6f, 0f), new Vector2(4.7f, 0f), new Vector2(5.4f, 0f), new Vector2(6.5f, 0f),
            new Vector2(6.5f, 0f), new Vector2(6.5f, 0.6f)), "ACO_FHud_RungDown");
        Mesh horizon = SaveMesh(LineMesh("ACO_FHud_Horizon", th, new Vector2(-10f, 0f), new Vector2(-1.5f, 0f), new Vector2(1.5f, 0f), new Vector2(10f, 0f)), "ACO_FHud_Horizon");
        // 속도 벡터: SAV HUD와 같은 꼴(원 + 양 날개 + 위 꼬리)
        Mesh fpm = SaveMesh(Combine("ACO_FHud_Fpm", BuildThinRingMesh(24, 0.7f, 0.16f, "r"),
            LineMesh("w", th * 1.2f, new Vector2(0.7f, 0f), new Vector2(1.7f, 0f), new Vector2(-1.7f, 0f), new Vector2(-0.7f, 0f), new Vector2(0f, 0.7f), new Vector2(0f, 1.3f))), "ACO_FHud_Fpm");
        Mesh gunCross = SaveMesh(BuildCrossMesh(0.7f, 0.2f, 0.05f, "ACO_FHud_GunCross"), "ACO_FHud_GunCross");
        Mesh rocketCross = SaveMesh(BuildCrossMesh(1.6f, 0.45f, 0.06f, "ACO_FHud_RocketCross"), "ACO_FHud_RocketCross");
        Mesh wSight = SaveMesh(LineMesh("ACO_FHud_W", th * 1.2f,
            new Vector2(-2.2f, 0f), new Vector2(-1.2f, 0f), new Vector2(-1.2f, 0f), new Vector2(-0.6f, -0.7f), new Vector2(-0.6f, -0.7f), new Vector2(0f, 0f),
            new Vector2(0f, 0f), new Vector2(0.6f, -0.7f), new Vector2(0.6f, -0.7f), new Vector2(1.2f, 0f), new Vector2(1.2f, 0f), new Vector2(2.2f, 0f)), "ACO_FHud_W");
        Mesh pipper = SaveMesh(Combine("ACO_FHud_Pipper", BuildThinRingMesh(36, 1.3f, 0.1f, "r"), BuildThinRingMesh(12, 0.13f, 1f, "d")), "ACO_FHud_Pipper");
        Mesh valueBox = SaveMesh(BoxMesh("ACO_FHud_ValueBox", 5.6f, 2.3f, th), "ACO_FHud_ValueBox");
        Mesh headingBox = SaveMesh(BoxMesh("ACO_FHud_HeadingBox", 4.4f, 2f, th), "ACO_FHud_HeadingBox");
        Mesh throttleFrame = SaveMesh(OffsetBox("ACO_FHud_ThrottleFrame", new Vector2(-0.35f, 0f), new Vector2(0.35f, 8f), th), "ACO_FHud_ThrottleFrame");
        Mesh throttleFill = SaveMesh(FillRect("ACO_FHud_ThrottleFill", new Vector2(-0.25f, 0f), new Vector2(0.25f, 1f)), "ACO_FHud_ThrottleFill");
        Mesh abLine = SaveMesh(LineMesh("ACO_FHud_AbLine", th, new Vector2(-0.6f, 0f), new Vector2(0.6f, 0f)), "ACO_FHud_AbLine");
        Mesh hpFrame = SaveMesh(OffsetBox("ACO_FHud_HpFrame", new Vector2(0f, 0f), new Vector2(6f, 0.5f), th), "ACO_FHud_HpFrame");
        Mesh hpFill = SaveMesh(FillRect("ACO_FHud_HpFill", new Vector2(0.1f, 0.1f), new Vector2(5.9f, 0.4f)), "ACO_FHud_HpFill");
        // 장전 막대: 세로로 얇은 직사각형(아래 끝 기준 — 채움은 아래 → 위, 스크립트가 y 크기를 바꾼다)
        Mesh slotFrame = SaveMesh(OffsetBox("ACO_FHud_SlotFrame", new Vector2(-0.17f, 0f), new Vector2(0.17f, 2.6f), th * 0.8f), "ACO_FHud_SlotFrame");
        Mesh slotFill = SaveMesh(FillRect("ACO_FHud_SlotFill", new Vector2(-0.1f, 0f), new Vector2(0.1f, 2.48f)), "ACO_FHud_SlotFill");

        GameObject prefabRoot = new GameObject("ACO_FlightHud");
        ACO_FlightHud hud = prefabRoot.AddUdonSharpComponent<ACO_FlightHud>();
        ACO_Units units = prefabRoot.AddUdonSharpComponent<ACO_Units>();
        hud.Units = units;
        var root = new GameObject("Root").transform;
        root.SetParent(prefabRoot.transform, false);
        var canvas = new GameObject("Canvas").transform;
        canvas.SetParent(root, false);
        canvas.localPosition = new Vector3(0f, 0f, 20f);
        canvas.localScale = Vector3.one * (20f * 0.017455065f);
        var lines = new List<MeshRenderer>();
        var texts = new List<TextMeshPro>();
        System.Func<Transform, string, Vector3, Mesh, MeshRenderer> line = (p, n, pos, mesh) =>
        {
            MeshRenderer r = MakeQuad(p, n, pos, Vector3.one, mesh, lineMat);
            lines.Add(r);
            return r;
        };
        // 글자 크기는 아래 숫자 × FlightTextScale(1.5 — 처음 크기는 작아서 읽기 힘들었다). 배치는 키운 크기에 맞췄다
        System.Func<Transform, string, Vector3, float, TextAlignmentOptions, Vector2, string, TextMeshPro> text = (p, n, pos, size, align, pivot, s) =>
        {
            TextMeshPro t = MakeText(p, n, pos, size * FlightTextScale, FlightGreen, align, new Vector2(14f, 2.6f), pivot, font, textMat, s);
            texts.Add(t);
            return t;
        };

        // 헤딩 테이프(위): 5° 눈금 13개 — 눈금이 아래로 처지고 숫자는 위
        Transform heading = NewChild(canvas, "HeadingTape", new Vector3(0f, 12f, 0f));
        var hTicks = new Transform[13];
        var hLabels = new TextMeshPro[13];
        for (int i = 0; i < 13; i++)
        {
            hTicks[i] = line(heading, "Tick_" + i, Vector3.zero, tickDown).transform;
            hLabels[i] = text(hTicks[i], "Label", new Vector3(0f, 1f, 0f), 6f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), "00");
        }
        hud.HeadingTicks = hTicks;
        hud.HeadingLabels = hLabels;
        line(canvas, "HeadingBox", new Vector3(0f, 14.9f, 0f), headingBox);
        hud.HeadingText = text(canvas, "HeadingText", new Vector3(0f, 14.9f, 0f), 10f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), "000");

        // 속도 테이프(왼쪽): 10kt 눈금 13개 — 눈금이 가운데 쪽으로, 숫자는 바깥(왼쪽)
        Transform speed = NewChild(canvas, "SpeedTape", new Vector3(-13f, 0f, 0f));
        var sTicks = new Transform[13];
        var sLabels = new TextMeshPro[13];
        for (int i = 0; i < 13; i++)
        {
            sTicks[i] = line(speed, "Tick_" + i, Vector3.zero, tickRight).transform;
            sLabels[i] = text(sTicks[i], "Label", new Vector3(-0.35f, 0f, 0f), 6f, TextAlignmentOptions.Right, new Vector2(1f, 0.5f), "0");
        }
        hud.SpeedTicks = sTicks;
        hud.SpeedLabels = sLabels;
        line(canvas, "SpeedBox", new Vector3(-FlightValueBoxX, 0f, 0f), valueBox);
        hud.SpeedText = text(canvas, "SpeedText", new Vector3(-FlightValueTextX, 0f, 0f), 10f, TextAlignmentOptions.Right, new Vector2(1f, 0.5f), "0");

        // 스로틀(속도 테이프 옆): 틀 + 채움(추력) + 애프터버너 선, 아래 글자
        Transform thr = NewChild(canvas, "Throttle", new Vector3(FlightThrottleX, -4f, 0f));
        line(thr, "Frame", Vector3.zero, throttleFrame);
        hud.ThrottleFill = line(thr, "Fill", Vector3.zero, throttleFill).transform;
        hud.ThrottleAbLine = line(thr, "AbLine", new Vector3(0f, 6.4f, 0f), abLine).transform;
        hud.ThrottleTarget = line(thr, "TargetMark", new Vector3(FlightThrottleMarkX, 0f, 0f), ThrottleMarkMesh()).transform;
        hud.ThrottleText = text(canvas, "ThrottleText", new Vector3(FlightThrottleX, -5.3f, 0f), 6f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), "THR 0");

        // 고도 테이프(오른쪽): 100ft 눈금 13개 — 눈금이 가운데 쪽으로, 숫자는 바깥(오른쪽)
        Transform alt = NewChild(canvas, "AltTape", new Vector3(13f, 0f, 0f));
        var aTicks = new Transform[13];
        var aLabels = new TextMeshPro[13];
        for (int i = 0; i < 13; i++)
        {
            aTicks[i] = line(alt, "Tick_" + i, Vector3.zero, tickLeft).transform;
            aLabels[i] = text(aTicks[i], "Label", new Vector3(0.35f, 0f, 0f), 6f, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), "0");
        }
        hud.AltTicks = aTicks;
        hud.AltLabels = aLabels;
        line(canvas, "AltBox", new Vector3(FlightValueBoxX, 0f, 0f), valueBox);
        hud.AltText = text(canvas, "AltText", new Vector3(FlightValueTextX, 0f, 0f), 10f, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), "0");
        hud.RadarAltText = text(canvas, "RadarAltText", new Vector3(FlightValueTextX, -2.5f, 0f), 8f, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), "R 0");

        // 피치 래더: −90° ~ +90°(10°마다 — ±90 = 천정·천저, 루프로 넘어갈 때 빈 구간을 메운다), 0° = 수평선. 숫자는 양쪽 끝에
        var rungs = new List<Transform>();
        var angles = new List<float>();
        Transform ladder = NewChild(canvas, "Ladder", Vector3.zero);
        for (int a = -90; a <= 90; a += 10)
        {
            Mesh m = a == 0 ? horizon : (a > 0 ? rungUp : rungDown);
            Transform r = line(ladder, "Rung_" + a, Vector3.zero, m).transform;
            if (a != 0)
            {
                string label = Mathf.Abs(a).ToString();
                text(r, "LabelL", new Vector3(-7.2f, 0f, 0f), 6f, TextAlignmentOptions.Right, new Vector2(1f, 0.5f), label);
                text(r, "LabelR", new Vector3(7.2f, 0f, 0f), 6f, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), label);
            }
            rungs.Add(r);
            angles.Add(a);
        }
        hud.Rungs = rungs.ToArray();
        hud.RungAngles = angles.ToArray();

        // 속도 벡터·조준 표시(가운데 = 기수 방향)·기총 피퍼
        hud.VelocityMarker = line(canvas, "VelocityMarker", Vector3.zero, fpm).transform;
        hud.GunCross = line(canvas, "GunCross", Vector3.zero, gunCross).gameObject;
        hud.WSight = line(canvas, "WSight", Vector3.zero, wSight).gameObject;
        hud.RocketCross = line(canvas, "RocketCross", Vector3.zero, rocketCross).gameObject;
        hud.Pipper = line(canvas, "Pipper", Vector3.zero, pipper).transform;
        hud.PipperRangeText = text(hud.Pipper, "Range", new Vector3(1.7f, -1.9f, 0f), 6f, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), "");
        hud.WSight.SetActive(false);
        hud.RocketCross.SetActive(false);
        hud.Pipper.gameObject.SetActive(false);

        // 가운데 위: 아군 ECM 구역 안에서만 보인다
        hud.EcmText = text(canvas, "EcmText", FlightEcmTextPos, 8f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), "ECM SUPPORT");
        hud.EcmText.gameObject.SetActive(false);

        // 왼쪽 아래: G, 받음각(랜딩기어를 내렸을 때), 상태(GEAR·FLAP·HOOK·AIRBRAKE)
        hud.GText = text(canvas, "GText", new Vector3(-21.5f, -6.8f, 0f), 8f, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), "G 1.0");
        hud.AoaText = text(canvas, "AoaText", new Vector3(-21.5f, -8.5f, 0f), 7f, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), "AOA 0.0");
        hud.StatusText = text(canvas, "StatusText", new Vector3(-21.5f, -9.6f, 0f), 7f, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), "GEAR UP");
        hud.StatusText.rectTransform.sizeDelta = new Vector2(14f, 9f);
        // 오른쪽 아래: 잔탄 — 세 칸(고른 무장 쉐브론 | 이름 | 수 — 오른쪽 맞춤)으로 줄을 맞춘다, HP
        hud.AmmoMarkText = text(canvas, "AmmoMark", new Vector3(13f, -6.2f, 0f), 10f, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), ">");
        hud.AmmoMarkText.rectTransform.sizeDelta = new Vector2(2f, 10f);
        hud.AmmoText = text(canvas, "AmmoText", new Vector3(14.8f, -6.2f, 0f), 10f, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), "GUN");
        hud.AmmoText.rectTransform.sizeDelta = new Vector2(8f, 10f);
        hud.AmmoCountText = text(canvas, "AmmoCount", new Vector3(28f, -6.2f, 0f), 10f, TextAlignmentOptions.TopRight, new Vector2(1f, 1f), "0");
        hud.AmmoCountText.rectTransform.sizeDelta = new Vector2(6f, 10f);
        hud.HpText = text(canvas, "HpText", new Vector3(13f, -14.6f, 0f), 9f, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), "HP 100%");
        Transform hp = NewChild(canvas, "HpBar", new Vector3(13f, -16.4f, 0f));
        line(hp, "Frame", Vector3.zero, hpFrame);
        hud.HpFill = line(hp, "Fill", Vector3.zero, hpFill).transform;

        // 오른쪽 아래, 잔탄 왼쪽: 고른 ACO 무장의 장전 슬롯 막대(최대 8개 — 세로로 얇은 막대를 오른쪽 끝(12.4)부터 왼쪽으로 늘어놓는다, 스크립트가 슬롯 수에 맞춰 놓는다)
        Transform slots = NewChild(canvas, "SlotBars", new Vector3(12.4f, -9.1f, 0f));
        var bars = new Transform[8];
        var fills = new Transform[8];
        for (int i = 0; i < 8; i++)
        {
            Transform b = NewChild(slots, "Slot_" + i, new Vector3(-i * 0.55f, 0f, 0f));
            line(b, "Frame", Vector3.zero, slotFrame);
            fills[i] = line(b, "Fill", new Vector3(0f, 0.06f, 0f), slotFill).transform;
            bars[i] = b;
        }
        hud.SlotGroup = slots.gameObject;
        hud.SlotBars = bars;
        hud.SlotFills = fills;

        hud.Root = root;
        hud.Canvas = canvas;
        hud.LineRenderers = lines.ToArray();
        hud.AllTexts = texts.ToArray();
        foreach (Transform t in prefabRoot.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = 5; }
        root.gameObject.SetActive(false);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, $"{PrefabDir}/ACO_FlightHud.prefab");
        Object.DestroyImmediate(prefabRoot);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 비행 HUD 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 씬에 하나 두고 Bake하면 플레이어 기체가 연결되고 SAV HUD가 숨겨집니다.");
        Selection.activeObject = prefab;
    }

    private static Transform NewChild(Transform parent, string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    /// 선분들(두 점씩)을 두께 thick의 사각형으로. XY 평면.
    private static Mesh LineMesh(string name, float thick, params Vector2[] pts)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i + 1 < pts.Length; i += 2)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            Vector2 d = (b - a).normalized;
            Vector2 n = new Vector2(-d.y, d.x) * (thick * 0.5f);
            Vector2 e = d * (thick * 0.5f);   // 끝을 조금 늘려 꺾인 곳이 이어지게
            int s = v.Count;
            v.Add(a - e - n); v.Add(b + e - n); v.Add(b + e + n); v.Add(a - e + n);
            t.Add(s); t.Add(s + 2); t.Add(s + 1);
            t.Add(s); t.Add(s + 3); t.Add(s + 2);
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// 가운데 기준 사각 테두리.
    private static Mesh BoxMesh(string name, float w, float h, float thick)
    {
        return OffsetBox(name, new Vector2(-w * 0.5f, -h * 0.5f), new Vector2(w * 0.5f, h * 0.5f), thick);
    }

    private static Mesh OffsetBox(string name, Vector2 min, Vector2 max, float thick)
    {
        return LineMesh(name, thick, new Vector2(min.x, min.y), new Vector2(max.x, min.y), new Vector2(max.x, min.y), new Vector2(max.x, max.y),
            new Vector2(max.x, max.y), new Vector2(min.x, max.y), new Vector2(min.x, max.y), new Vector2(min.x, min.y));
    }

    /// 채운 사각형(피벗 = 원점 — 스크립트가 한 축 스케일로 채운다).
    private static Mesh FillRect(string name, Vector2 min, Vector2 max)
    {
        var mesh = new Mesh { name = name };
        mesh.vertices = new[] { new Vector3(min.x, min.y, 0f), new Vector3(max.x, min.y, 0f), new Vector3(max.x, max.y, 0f), new Vector3(min.x, max.y, 0f) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh Combine(string name, params Mesh[] parts)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        foreach (Mesh m in parts)
        {
            int s = v.Count;
            v.AddRange(m.vertices);
            foreach (int i in m.triangles) { t.Add(s + i); }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ───────────── 관전 패널 ─────────────

    public const string SpectatorRtPath = "Assets/ACO_Addon/Generated/ACO_SpectatorRT.renderTexture";
    public const string SpectatorRtFhdPath = "Assets/ACO_Addon/Generated/ACO_SpectatorRT_FHD.renderTexture";
    private const string SpectatorFullscreenMatPath = "Assets/ACO_Addon/Generated/ACO_SpectatorFullscreen.mat";
    private const string FullscreenQuadPath = "Assets/ACO_Addon/Generated/ACO_FullscreenQuad.asset";
    private const string SpectatorScreenMatPath = "Assets/ACO_Addon/Generated/ACO_SpectatorScreen.mat";
    private const string SpectatorOffMatPath = "Assets/ACO_Addon/Generated/ACO_SpectatorOff.mat";
    private const string SkyFogMatPath = "Assets/ACO_Addon/Generated/ACO_SkyFog.mat";
    /// 관전 카메라 전용 레이어: MirrorReflection(18) — VRChat 메인 카메라는 그리지 않는다(관전 카메라 하늘 안개 돔).
    public const int SpectatorOnlyLayer = 18;

    /// 관전 카메라가 그리는 레이어: 모두에서 UI(5, HUD·마커)·PlayerLocal(10, 내 1인칭 아바타 — 대신 18의 거울용 전신을 그린다)·
    /// UiMenu(12)·InternalUI(19)·FX(ACO_SceneBaker.FxLayer — 내 머리를 따라다니는 효과)를 뺀다.
    public static int SpectatorCullingMask()
    {
        return ~((1 << 5) | (1 << 10) | (1 << 12) | (1 << 19) | (1 << ACO_SceneBaker.FxLayer));
    }

    /// 관전 패널: 큰 스크린(3.2 x 1.8m, 렌더 텍스처 HD 1280x720 / FHD 1920x1080) + 아래 조작 줄(POWER, HD/FHD, TARGET < >, SHOT < >, 상태) + 관전 카메라(꺼진 채)
    /// + 카메라 전용 하늘 안개 돔 + 스트림 카메라용 전체 화면(VRChat 카메라가 스크린 4m 안이면 그 화면 전체가 관전 화면).
    /// 씬에 두고 Bake하면 플레이어 기체·해수면·미사일 무장이 연결된다.
    public static void CreateSpectatorPanelPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        EnsureFolder("Assets/ACO_Addon", "Generated");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[ACO] TMP 폰트를 찾지 못했습니다: {FontPath} — Window → TextMeshPro → Import TMP Essential Resources를 먼저 실행하세요.");
            return;
        }

        // 렌더 텍스처·스크린 머티리얼(있으면 그대로 — 해상도를 고친 값 유지)
        RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(SpectatorRtPath);
        if (rt == null)
        {
            rt = new RenderTexture(1280, 720, 24) { name = "ACO_SpectatorRT", antiAliasing = 2 };
            AssetDatabase.CreateAsset(rt, SpectatorRtPath);
        }
        RenderTexture rtFhd = AssetDatabase.LoadAssetAtPath<RenderTexture>(SpectatorRtFhdPath);
        if (rtFhd == null)
        {
            rtFhd = new RenderTexture(1920, 1080, 24) { name = "ACO_SpectatorRT_FHD", antiAliasing = 2 };
            AssetDatabase.CreateAsset(rtFhd, SpectatorRtFhdPath);
        }
        // 스트림 카메라용 전체 화면: 쿼드(-0.5~0.5)를 셰이더가 화면 전체로 펼친다 — 경계를 크게 구워 어느 쪽을 봐도 잘리지 않게
        Mesh fsQuad = AssetDatabase.LoadAssetAtPath<Mesh>(FullscreenQuadPath);
        if (fsQuad == null)
        {
            fsQuad = new Mesh { name = "ACO_FullscreenQuad" };
            fsQuad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            fsQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            fsQuad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            fsQuad.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            AssetDatabase.CreateAsset(fsQuad, FullscreenQuadPath);
        }
        Material fsMat = AssetDatabase.LoadAssetAtPath<Material>(SpectatorFullscreenMatPath);
        if (fsMat == null)
        {
            Shader fsShader = Shader.Find("ACO/SpectatorFullscreen");
            if (fsShader == null) { Debug.LogError("[ACO] 셰이더 ACO/SpectatorFullscreen을 찾지 못했습니다 — 셰이더 컴파일 오류를 확인하세요."); }
            else
            {
                fsMat = new Material(fsShader) { name = "ACO_SpectatorFullscreen", mainTexture = rt };
                AssetDatabase.CreateAsset(fsMat, SpectatorFullscreenMatPath);
            }
        }
        Material onMat = AssetDatabase.LoadAssetAtPath<Material>(SpectatorScreenMatPath);
        if (onMat == null)
        {
            onMat = new Material(Shader.Find("Unlit/Texture")) { name = "ACO_SpectatorScreen", mainTexture = rt };
            AssetDatabase.CreateAsset(onMat, SpectatorScreenMatPath);
        }
        Material offMat = AssetDatabase.LoadAssetAtPath<Material>(SpectatorOffMatPath);
        if (offMat == null)
        {
            offMat = new Material(Shader.Find("Unlit/Color")) { name = "ACO_SpectatorOff", color = new Color(0.02f, 0.03f, 0.03f, 1f) };
            AssetDatabase.CreateAsset(offMat, SpectatorOffMatPath);
        }

        GameObject root = new GameObject("ACO_SpectatorPanel");
        ACO_SpectatorCam spec = root.AddUdonSharpComponent<ACO_SpectatorCam>();

        // 스크린(쿼드는 -Z가 앞면 — 돌려서 루트 +Z 쪽에서 보이게, 패널 캔버스와 같은 방향)
        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "Screen";
        Object.DestroyImmediate(screen.GetComponent<Collider>());
        screen.transform.SetParent(root.transform, false);
        screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        screen.transform.localScale = new Vector3(3.2f, 1.8f, 1f);
        MeshRenderer sr = screen.GetComponent<MeshRenderer>();
        sr.sharedMaterial = offMat;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;

        // 스트림 카메라용 전체 화면(스크린 가운데 — 셰이더가 이 자리에서 카메라 거리를 잰다). FX 레이어 = 관전 카메라는 그리지 않는다
        GameObject fs = new GameObject("StreamCameraFullscreen");
        fs.transform.SetParent(root.transform, false);
        fs.layer = ACO_SceneBaker.FxLayer;
        fs.AddComponent<MeshFilter>().sharedMesh = fsQuad;
        MeshRenderer fsr = fs.AddComponent<MeshRenderer>();
        if (fsMat != null) { fsr.sharedMaterial = fsMat; }
        fsr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fsr.receiveShadows = false;
        fsr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        fsr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        fs.SetActive(false);

        // 관전 카메라(꺼진 채 — 켜면 스크립트가 옮긴다)
        GameObject camGo = new GameObject("SpectatorCamera");
        camGo.transform.SetParent(root.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0f, 2f);
        Camera cam = camGo.AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 40000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.cullingMask = SpectatorCullingMask();
        cam.depth = -5f;
        cam.allowMSAA = true;
        cam.useOcclusionCulling = true;
        cam.enabled = false;

        // 관전 카메라 전용 하늘 안개 돔(메인 카메라는 레이어 18을 그리지 않는다)
        GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "SkyFogDome";
        Object.DestroyImmediate(dome.GetComponent<Collider>());
        dome.transform.SetParent(camGo.transform, false);
        dome.transform.localScale = Vector3.one * 100f;
        dome.layer = SpectatorOnlyLayer;
        MeshRenderer dr = dome.GetComponent<MeshRenderer>();
        Material skyFog = AssetDatabase.LoadAssetAtPath<Material>(SkyFogMatPath);
        if (skyFog != null) { dr.sharedMaterial = skyFog; }
        dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dr.receiveShadows = false;
        dr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        dr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        dome.SetActive(false);

        // 조작 줄(스크린 아래)
        RectTransform canvas = MakeUiPanel(root.transform, 3200f, 420f);
        canvas.localPosition = new Vector3(0f, -1.15f, 0f);
        AddButtonIcon(SpectatorButton(canvas, spec, font, "POWER", new Vector2(-1430f, 50f), new Vector2(260f, 140f), ACO_SpectatorCam.ACT_POWER, 1, 44f), LoadIcon("power"), 56f, IconColor);
        spec.PowerText = MakeUiText(canvas, "PowerText", new Vector2(-1430f, -100f), new Vector2(260f, 70f), 42f, font, "OFF");
        AddButtonIcon(SpectatorButton(canvas, spec, font, "HD / FHD", new Vector2(-1140f, 50f), new Vector2(270f, 140f), ACO_SpectatorCam.ACT_RESOLUTION, 1, 34f), LoadIcon("screen"), 56f, IconColor);
        spec.ResolutionText = MakeUiText(canvas, "ResolutionText", new Vector2(-1140f, -100f), new Vector2(270f, 70f), 36f, font, "HD 720p");

        MakeUiLabel(canvas, "TargetHeader", new Vector2(-300f, 145f), new Vector2(900f, 60f), 38f, font, "TARGET (SLOT)");
        MakeIcon(canvas, "TargetIcon", LoadIcon("target"), new Vector2(-514f, 145f), 56f);
        SpectatorButton(canvas, spec, font, "<", new Vector2(-760f, 30f), new Vector2(130f, 130f), ACO_SpectatorCam.ACT_TARGET, -1, 64f);
        spec.TargetText = MakeUiText(canvas, "TargetText", new Vector2(-300f, 30f), new Vector2(760f, 120f), 46f, font, "AUTO");
        SpectatorButton(canvas, spec, font, ">", new Vector2(160f, 30f), new Vector2(130f, 130f), ACO_SpectatorCam.ACT_TARGET, 1, 64f);

        MakeUiLabel(canvas, "ShotHeader", new Vector2(900f, 145f), new Vector2(900f, 60f), 38f, font, "SHOT");
        MakeIcon(canvas, "ShotIcon", LoadIcon("camera"), new Vector2(779f, 145f), 56f);
        SpectatorButton(canvas, spec, font, "<", new Vector2(480f, 30f), new Vector2(130f, 130f), ACO_SpectatorCam.ACT_SHOT, -1, 64f);
        spec.ShotText = MakeUiText(canvas, "ShotText", new Vector2(900f, 30f), new Vector2(660f, 120f), 46f, font, "AUTO");
        SpectatorButton(canvas, spec, font, ">", new Vector2(1320f, 30f), new Vector2(130f, 130f), ACO_SpectatorCam.ACT_SHOT, 1, 64f);

        spec.StatusText = MakeUiText(canvas, "StatusText", new Vector2(300f, -140f), new Vector2(2400f, 70f), 36f, font, "SPECTATOR CAMERA OFF");

        spec.Cam = cam;
        spec.Screen = sr;
        spec.OnMaterial = onMat;
        spec.OffMaterial = offMat;
        spec.SkyFogDome = dr;
        spec.TextureHD = rt;
        spec.TextureFHD = rtFhd;
        spec.FullscreenOverlay = fsr;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/ACO_SpectatorPanel.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 관전 패널 프리팹 생성 완료: {AssetDatabase.GetAssetPath(prefab)} — 로비 벽에 두고 Bake하면 플레이어 기체·미사일 무장이 연결됩니다. 스크린 해상도는 {SpectatorRtPath}.");
        Selection.activeObject = prefab;
    }

    private static Image SpectatorButton(RectTransform parent, ACO_SpectatorCam spec, TMP_FontAsset font, string label, Vector2 pos, Vector2 size, int action, int step, float fontSize)
    {
        Image img = MakeUiButton(parent, $"Btn_{action}_{step}_{label}", pos, size, font, label, fontSize);
        ACO_SpectatorButton b = img.gameObject.AddUdonSharpComponent<ACO_SpectatorButton>();
        b.Panel = spec;
        b.Action = action;
        b.Step = step;
        WireClick(img.GetComponent<Button>(), b);
        return img;
    }

    /// 값 줄(글자 + −/+ 버튼, 또는 < > 고르기). 글자를 돌려준다.
    private static TextMeshProUGUI DebugValueRow(RectTransform canvas, ACO_DebugPanel panel, TMP_FontAsset font, string name, float y, int action, float step,
        string minus = "-", string plus = "+")
    {
        TextMeshProUGUI t = MakeUiText(canvas, name + "Text", new Vector2(-180f, y), new Vector2(1250f, 64f), 36f, font, name.ToUpperInvariant());
        t.alignment = TextAlignmentOptions.Left;
        DebugButton(canvas, panel, font, minus, new Vector2(560f, y), new Vector2(110f, 60f), action, 0, -step);
        DebugButton(canvas, panel, font, plus, new Vector2(690f, y), new Vector2(110f, 60f), action, 0, step);
        return t;
    }

    private static void DebugButton(RectTransform parent, ACO_DebugPanel panel, TMP_FontAsset font, string label, Vector2 pos, Vector2 size, int action, int index, float amount)
    {
        Image img = MakeUiButton(parent, $"Btn_{action}_{index}_{label}", pos, size, font, label, 40f);
        ACO_DebugButton b = img.gameObject.AddUdonSharpComponent<ACO_DebugButton>();
        b.Panel = panel;
        b.Action = action;
        b.Index = index;
        b.Amount = amount;
        WireClick(img.GetComponent<Button>(), b);
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
    public static Sprite[] BuildUpgradeIcons(ACO_PlayerProgress p)
    {
        var icons = new Sprite[ACO_PlayerProgress.UPGRADE_COUNT];
        string[] airframe = { "hull", "mobility", "engine", "fuel", "stability", "damage" };
        int[] airIds = { ACO_PlayerProgress.UP_HULL, ACO_PlayerProgress.UP_MOBILITY, ACO_PlayerProgress.UP_ENGINE, ACO_PlayerProgress.UP_FUEL, ACO_PlayerProgress.UP_STABILITY, ACO_PlayerProgress.UP_GUN };
        for (int i = 0; i < airIds.Length; i++) { if (airIds[i] < icons.Length) { icons[airIds[i]] = LoadIcon(airframe[i]); } }
        // STAT_* 순서
        string[] stats = { "damage", "ammo", "turn", "lock_speed", "lock_range", "seeker", "blast", "reload" };
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
            new[] { ACO_HudSettings.ITEM_PANEL_X, ACO_HudSettings.ITEM_PANEL_Y, ACO_HudSettings.ITEM_PANEL_DISTANCE, ACO_HudSettings.ITEM_PANEL_SCALE, ACO_HudSettings.ITEM_HUD_FOLLOW },
            new[] { "LEFT / RIGHT", "UP / DOWN", "DISTANCE", "SIZE", "FOLLOW DELAY" }),
        new HudCategory(0, "FLIGHT HUD",
            new[] { ACO_HudSettings.ITEM_FLIGHT_HUD, ACO_HudSettings.ITEM_FLIGHT_HUD_SIZE, ACO_HudSettings.ITEM_FLIGHT_HUD_BRIGHT, ACO_HudSettings.ITEM_DISTANCE_UNIT },
            new[] { "SHOW", "SIZE", "BRIGHTNESS", "DISTANCE UNIT" }),
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
        new HudCategory(1, "RADAR",
            new[] { ACO_HudSettings.ITEM_RADAR, ACO_HudSettings.ITEM_RADAR_SIZE, ACO_HudSettings.ITEM_RADAR_X, ACO_HudSettings.ITEM_RADAR_Y, ACO_HudSettings.ITEM_RADAR_ALPHA, ACO_HudSettings.ITEM_RADAR_RANGE, ACO_HudSettings.ITEM_RADAR_BLIP },
            new[] { "SHOW", "SIZE", "LEFT / RIGHT", "UP / DOWN", "OPACITY", "RANGE", "BLIP SIZE" }),
        DeckCrewCategory,
        GraphicsCategory,
    };

    private static HudCategory DeckCrewCategory => new HudCategory(2, "DECK CREW", new[] { ACO_HudSettings.ITEM_DECK_CREW }, new[] { "SHOW" });
    private static HudCategory GraphicsCategory => new HudCategory(2, "GRAPHICS", new[] { ACO_HudSettings.ITEM_CLOUD_QUALITY }, new[] { "CLOUD QUALITY" });

    private const float HudColWidth = 860f;
    private const float HudRowH = 90f;
    private const float HudHeaderH = 76f;
    private const float HudCardGap = 30f;
    private static readonly Color HudAccent = new Color(1f, 0.85f, 0.35f, 1f);

    /// 분류 카드 하나. 반환값은 카드 높이.
    private static float MakeHudCard(RectTransform canvas, HudCategory cat, float cx, float y0, TextMeshProUGUI[] values, TMP_FontAsset font)
    {
        float h = HudHeaderH + cat.Items.Length * HudRowH + 16f;
        MakeUiBox(canvas, $"Card_{cat.Title}", new Vector2(cx, y0 - h * 0.5f), new Vector2(HudColWidth, h), new Color(0.04f, 0.09f, 0.06f, 1f));
        TextMeshProUGUI header = MakeUiText(canvas, $"Header_{cat.Title}", new Vector2(cx, y0 - HudHeaderH * 0.5f), new Vector2(HudColWidth - 40f, HudHeaderH), 40f, font, cat.Title);
        header.alignment = TextAlignmentOptions.Left;
        header.color = HudAccent;
        for (int r = 0; r < cat.Items.Length; r++)
        {
            int i = cat.Items[r];
            float y = y0 - HudHeaderH - HudRowH * (r + 0.5f);
            TextMeshProUGUI l = MakeUiText(canvas, $"Label_{i}", new Vector2(cx - 215f, y), new Vector2(400f, HudRowH), 36f, font, cat.Labels[r]);
            l.alignment = TextAlignmentOptions.Left;
            MakeHudSettingButton(canvas, $"Minus_{i}", new Vector2(cx + 60f, y), font, "-", i, -1);
            values[i] = MakeUiText(canvas, $"Value_{i}", new Vector2(cx + 200f, y), new Vector2(160f, HudRowH), 36f, font, "-");
            MakeHudSettingButton(canvas, $"Plus_{i}", new Vector2(cx + 340f, y), font, "+", i, 1);
        }
        return h;
    }

    /// 이미 있는 HUD 설정 패널에 빠진 카드를 더한다(RWR 카드 아래로 차례로) — 다시 만들면 직접 고친 내용이 사라진다.
    public static void EnsureHudDeckCrewCard()
    {
        string path = $"{PrefabDir}/ACO_HudSettings.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_HudSettings settings = root.GetComponent<ACO_HudSettings>();
            if (settings == null || settings.ValueTexts == null || settings.ValueTexts.Length >= ACO_HudSettings.ITEM_COUNT) { return; }
            TextMeshProUGUI sample = settings.ValueTexts[ACO_HudSettings.ITEM_RWR];
            RectTransform canvas = sample != null ? sample.rectTransform.parent as RectTransform : null;
            RectTransform card = canvas != null ? canvas.Find("Card_RWR") as RectTransform : null;
            if (card == null) { return; }
            var values = new TextMeshProUGUI[ACO_HudSettings.ITEM_COUNT];
            settings.ValueTexts.CopyTo(values, 0);
            foreach (HudCategory cat in new[] { DeckCrewCategory, GraphicsCategory })
            {
                RectTransform have = canvas.Find($"Card_{cat.Title}") as RectTransform;
                if (have == null)
                {
                    float y0 = card.anchoredPosition.y - card.sizeDelta.y * 0.5f - HudCardGap;
                    MakeHudCard(canvas, cat, card.anchoredPosition.x, y0, values, sample.font);
                    have = canvas.Find($"Card_{cat.Title}") as RectTransform;
                }
                card = have;
            }
            settings.ValueTexts = values;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] HUD 설정: 새 항목을 넣었습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// HUD 설정 패널 프리팹(판 2.8m x 1.75m, 월드 공간 Canvas, 앞면은 루트 +Z 쪽): 3열에 분류 카드(점수 패널, 결과·안내 글자 / 무전 자막, 표적 마커, 레이더 /
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

        const float top = 820f;
        float[] colX = { -900f, 0f, 900f };
        float[] colY = { top, top, top };
        RectTransform canvas = MakeUiPanel(root.transform, 2800f, 1950f);
        MakeUiText(canvas, "Title", new Vector2(0f, 905f), new Vector2(2700f, 100f), 70f, font, "HUD SETTINGS");
        var values = new TextMeshProUGUI[ACO_HudSettings.ITEM_COUNT];
        foreach (HudCategory cat in HudCategories)
        {
            float h = MakeHudCard(canvas, cat, colX[cat.Column], colY[cat.Column], values, font);
            colY[cat.Column] -= h + HudCardGap;
        }
        // 가운데 열 아래: RESET + 안내
        float ry = colY[1] - 70f;
        Image reset = MakeUiButton(canvas, "Reset", new Vector2(colX[1], ry), new Vector2(500f, 110f), font, "RESET ALL", 50f);
        ACO_HudSettingButton rb = reset.gameObject.AddUdonSharpComponent<ACO_HudSettingButton>();
        rb.Settings = settings;
        rb.Direction = 0;
        WireClick(reset.GetComponent<Button>(), rb);
        MakeUiText(canvas, "Hint", new Vector2(colX[1], ry - 120f), new Vector2(HudColWidth, 100f), 30f, font, "SAVED AUTOMATICALLY\nYOUR HUD PREVIEWS WHILE ADJUSTING");
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
        Material tableMat = LoadOrSaveMaterial(unlit, new Color(0.12f, 0.13f, 0.14f), "ACO_MapTable");
        // 예전에 이 머티리얼이 바다 너울 머티리얼의 Variant(부모 = ACO_OceanSwell)가 돼 탁자가 바다로 보였다 — 부모를 끊고 셰이더·색을 되돌린다
        if (tableMat.parent != null || tableMat.shader != unlit)
        {
            tableMat.parent = null;
            tableMat.shader = unlit;
            tableMat.SetColor("_Color", new Color(0.12f, 0.13f, 0.14f));
            EditorUtility.SetDirty(tableMat);
        }
        table.GetComponent<MeshRenderer>().sharedMaterial = tableMat;

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
