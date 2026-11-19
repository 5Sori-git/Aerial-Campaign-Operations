using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UdonSharpEditor;
using TMPro;
using ACO;

/// CIC 도구(6-275): 도구 패널(ACO_CicConsole — 전술 테이블 동쪽 테두리 조작판 + 테이블 표시: 빛기둥 커서 · 손 선 · WP · 편대 구역 · 드론)과
/// 고글 자리(ACO_CicGogglesStation — 남쪽 동쪽 책상 모니터 위 패널: UAV · 기체 1~4 · 고글 회수, 책상 위 고글 2개, 화면 덮개 ACO/GogglesView, NO SIGNAL).
/// 정찰 드론 프리팹(ACO_CicUav)은 가디언 드론 겉모양(Model)을 다시 칠한 것이다(표적 · 충돌 없음 — 격추 불가).
/// 씬 설치는 Setup Check(ACO_CicSetup), 연결은 Bake(ACO_SceneBaker.BakeCic).
public static partial class ACO_HUDBuilder
{
    public const string CicConsoleName = "ACO_CicConsole";
    public const string CicUavName = "ACO_CicUav";
    private const string CicDir = "Assets/ACO_Addon/Generated/Cic";
    private const string GogglesRtPath = "Assets/ACO_Addon/Generated/Cic/ACO_GogglesRT.renderTexture";
    /// 고글 화질 FHD(1536² — 패널 화질 버튼으로 고를 때만 그린다, 1024²의 2.25배)
    private const string GogglesRtFhdPath = "Assets/ACO_Addon/Generated/Cic/ACO_GogglesRT_FHD.renderTexture";
    public const string CicGogglesName = "ACO_CicGogglesStation";
    /// 도구 패널: 테이블 가운데에서 이 거리(m, 수평 — 테이블 윗면 반경 1.2m 바로 바깥 테두리)에 놓는다
    public const float CicRimDistance = 1.25f;
    // 도구 패널 D안 색(금속 판 · 틀 · LCD)
    private static readonly Color CicMetal = new Color(0.36f, 0.38f, 0.41f, 1f);
    private static readonly Color CicFrame = new Color(0.13f, 0.15f, 0.16f, 1f);
    private static readonly Color CicLcd = new Color(0.06f, 0.1f, 0.065f, 1f);
    private static readonly Color CicLcdGreen = new Color(0.49f, 1f, 0.54f, 1f);
    private static readonly Color CicLcdDim = new Color(0.36f, 0.6f, 0.4f, 1f);
    private static readonly Color CicLcdAmber = new Color(1f, 0.6f, 0.24f, 1f);
    /// 도구 패널 크기: 캔버스 1000 × 850 → 0.3 × 0.255m(2026-11-18 0.6 → 0.3 — 사용자 "훨씬 작게", 순항미사일 4행째로 850)
    private const float CicToolScale = 0.0003f;
    /// 고글 패널: 모니터 화면(0.672 × 0.392m)을 덮는다 — 캔버스 1000 × 584
    private const float CicViewScale = 0.00067f;

    /// 도구 패널(테이블 동쪽 테두리의 비스듬한 조작판) + 테이블 표시. 루트 = 테이블 윗면 테두리 자리, +Z = 사용자 쪽(테이블 바깥).
    public static void CreateCicConsolePrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        EnsureFolder("Assets/ACO_Addon", "Generated");
        EnsureFolder("Assets/ACO_Addon/Generated", "Cic");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) { Debug.LogError($"[ACO] CIC 콘솔: TMP 폰트({FontPath})를 찾지 못했습니다."); return; }

        Material steel = CicSteel();
        // 커서 받침: 반투명 빛 원판(ACO/LightDisc — 가운데 진하고 바깥으로 옅게 + 가장자리 옅은 고리). 셰이더가 없으면 단색
        Shader discShader = Shader.Find("ACO/LightDisc");
        Material cursorMat = discShader != null ? CicMaterial("ACO_Cic_CursorFoot", discShader, new Color(1f, 0.82f, 0.25f, 0.75f)) : CicMaterial("ACO_Cic_Cursor", Shader.Find("Unlit/Color"), new Color(1f, 0.85f, 0.2f));
        if (discShader != null) { cursorMat.SetFloat("_Fill", 0f); }   // 고리만 — 가운데 마커가 그대로 보이게
        Material wpMat = CicMaterial("ACO_Cic_Waypoint", Shader.Find("Unlit/Color"), new Color(0.3f, 0.95f, 1f));
        Material areaMat = CicMaterial("ACO_Cic_Area", Shader.Find("Unlit/Color"), new Color(0.35f, 0.6f, 1f));
        Material droneMat = CicMaterial("ACO_Cic_DroneMark", Shader.Find("Unlit/Color"), new Color(0.85f, 0.95f, 1f));
        Material lineMat = CicMaterial("ACO_Cic_AimLine", Shader.Find("Sprites/Default"), new Color(1f, 0.85f, 0.2f, 0.8f));
        // 빛기둥: 반투명 · 위로 갈수록 흐려짐 · 부드러운 가장자리(ACO/LightBeam). 큰 것(400m)은 띠 간격 · 속도를 높이 비율로 맞춘다
        Material beamMat = CicBeamMaterial("ACO_Cic_WorldBeam", 0.5f, 0.5f, 1.3f, 0.0375f, 12f, 0.10f);
        Material cursorBeamMat = CicBeamMaterial("ACO_Cic_CursorBeam", 0.6f, 0.5f, 1.1f, 0.083f, 5f, 0.6f);
        // 위에서 내려다볼수록 옅게(큰 기둥은 UAV가 위에서 본다 — 찍은 자리를 덮지 않게, 테이블은 서서 내려다보니 약하게)
        beamMat.SetFloat("_TopFade", 1f);
        cursorBeamMat.SetFloat("_TopFade", 0.4f);
        // 둘레의 옅고 넓은 빛무리(심 없음, 가장자리 더 부드럽게, 더 빨리 흐려짐)
        Material beamHaloMat = CicBeamHalo(CicBeamMaterial("ACO_Cic_WorldBeamHalo", 0.22f, 0.5f, 2.2f, 0.1f, 12f, 0.10f));
        Material cursorHaloMat = CicBeamHalo(CicBeamMaterial("ACO_Cic_CursorBeamHalo", 0.28f, 0.5f, 1.8f, 0.15f, 5f, 0.6f));
        beamHaloMat.SetFloat("_TopFade", 2f);
        cursorHaloMat.SetFloat("_TopFade", 0.8f);
        Mesh ring = CicRingMesh();

        GameObject root = new GameObject(CicConsoleName);
        ACO_CicConsole console = root.AddUdonSharpComponent<ACO_CicConsole>();
        // 드론 경로 동기화 칸(6-278): 웨이포인트와 따로 보낸다 — 항상 켜진 자식
        GameObject droneSyncGo = new GameObject("DroneSync");
        droneSyncGo.transform.SetParent(root.transform, false);
        console.DroneSync = droneSyncGo.AddUdonSharpComponent<ACO_CicDroneSync>();

        // 테두리에 물린 받침(테이블 윗면 위)과 비스듬한 판(윗변이 테이블 안쪽으로 35° 기운다 — 바깥에 선 사람을 향한다)
        CicBox(root.transform, "Clamp", new Vector3(0f, 0.012f, 0.0f), new Vector3(0.33f, 0.024f, 0.06f), steel);
        Transform pivot = new GameObject("PanelPivot").transform;
        pivot.SetParent(root.transform, false);
        pivot.localPosition = new Vector3(0f, 0.024f, 0f);
        pivot.localRotation = Quaternion.Euler(-35f, 0f, 0f);
        CicBox(pivot, "Back", new Vector3(0f, 0.13f, -0.008f), new Vector3(0.315f, 0.265f, 0.014f), steel);

        RectTransform tools = MakeUiPanel(pivot, 1000f, 850f);
        tools.name = "ToolCanvas";
        tools.localScale = Vector3.one * CicToolScale;
        tools.localPosition = new Vector3(0f, 0.13f, 0.001f);
        // 지나다닐 때 걸리지 않게(6-291): UI 콜라이더를 미리 트리거로 넣는다 — VRCUiShape는 있는 콜라이더를 크기만 맞춰 쓰고(트리거 유지), UI 포인터는 트리거에도 닿는다
        BoxCollider toolCol = tools.gameObject.AddComponent<BoxCollider>();
        toolCol.isTrigger = true;
        toolCol.size = new Vector3(1000f, 850f, 1f);
        console.ToolPanel = tools;   // 이 판에 먼저 닿는 트리거는 지도를 찍지 않는다(6-278)
        // D안(2026-11-18 사용자 선택 — "하드웨어 버튼"): 금속 판 + 어두운 틀, 위 LCD 머리(제목 · 커서 거리 · 안내),
        // 도구마다 백라이트 키(LED 점 · 아이콘 · 이름)와 오른쪽 LCD 표시창(상태 · 설명). 순항미사일 · 편대는 확정 팝업.
        Transform bgT = tools.Find("Background");
        if (bgT != null) { bgT.GetComponent<Image>().color = CicMetal; }
        MakeUiBox(tools, "FrameTop", new Vector2(0f, 415f), new Vector2(1000f, 20f), CicFrame);
        MakeUiBox(tools, "FrameBottom", new Vector2(0f, -415f), new Vector2(1000f, 20f), CicFrame);
        MakeUiBox(tools, "FrameLeft", new Vector2(-490f, 0f), new Vector2(20f, 850f), CicFrame);
        MakeUiBox(tools, "FrameRight", new Vector2(490f, 0f), new Vector2(20f, 850f), CicFrame);
        MakeUiBox(tools, "LcdHeader", new Vector2(0f, 330f), new Vector2(940f, 130f), CicLcd);
        TextMeshProUGUI title = LocLabel(MakeUiText(tools, "Title", new Vector2(-225f, 357f), new Vector2(430f, 56f), 40f, font, ""), "cic.title");
        title.alignment = TextAlignmentOptions.Left;
        title.color = CicLcdGreen;
        console.CursorText = MakeUiText(tools, "CursorText", new Vector2(225f, 357f), new Vector2(430f, 56f), 34f, font, "");
        console.CursorText.alignment = TextAlignmentOptions.Right;
        console.CursorText.color = CicLcdGreen;
        console.StatusText = MakeUiText(tools, "StatusText", new Vector2(0f, 300f), new Vector2(900f, 46f), 28f, font, "NO OPERATION");
        console.StatusText.alignment = TextAlignmentOptions.Left;
        console.StatusText.color = CicLcdDim;
        // 4행: 왼쪽 백라이트 키(LED · 아이콘 · 이름), 오른쪽 LCD(상태 · 설명)
        // 행 순서: 웨이포인트 · 정찰 UAV · 아군 편대 · 순항미사일(2026-11-18 사용자 — 편대와 UAV 자리를 바꿈: 되돌릴 수 없는 두 도구가 아래에 모인다)
        string[] keys = { "cic.btn.wp", "cic.btn.drone", "cic.btn.wm", "cic.btn.cm" };
        string[] labels = { "WAYPOINT", "RECON UAV", "WINGMEN", "CRUISE MSL" };
        string[] events = { "ClickWaypoint", "ClickDrone", "ClickWingmen", "ClickCruise" };
        string[] icons = { "cic_waypoint", "cic_drone", "cic_wingmen", "cic_cruise" };
        string[] helps = { "cic.help.wp", "cic.help.drone", "cic.help.wm", "cic.help.cm" };
        Color[] keyColors = { new Color(0.17f, 0.23f, 0.17f, 1f), new Color(0.2f, 0.21f, 0.23f, 1f), new Color(0.15f, 0.19f, 0.24f, 1f), new Color(0.24f, 0.17f, 0.12f, 1f) };
        Color[] ledColors = { new Color(0.36f, 1f, 0.43f, 1f), new Color(0.85f, 0.88f, 0.9f, 1f), new Color(0.35f, 0.69f, 1f, 1f), new Color(1f, 0.6f, 0.24f, 1f) };
        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        var buttons = new Image[4];
        var states = new TextMeshProUGUI[4];
        var leds = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            float y = 175f - i * 165f;
            buttons[i] = LocButton(CicEventButton(tools, console, font, labels[i], new Vector2(-240f, y), new Vector2(440f, 140f), events[i], 40f), keys[i]);
            buttons[i].color = keyColors[i];
            Outline edge = buttons[i].gameObject.AddComponent<Outline>();
            edge.effectColor = CicFrame;
            edge.effectDistance = new Vector2(6f, -6f);
            leds[i] = MakeIcon(buttons[i].rectTransform, "Led", knob, new Vector2(-186f, 0f), 28f);
            leds[i].color = ledColors[i];
            MakeIcon(buttons[i].rectTransform, "Icon", LoadIcon(icons[i]), new Vector2(-118f, 0f), 84f).color = new Color(0.9f, 0.93f, 0.95f, 1f);
            TextMeshProUGUI label = buttons[i].GetComponentInChildren<TextMeshProUGUI>(true);
            label.alignment = TextAlignmentOptions.Left;
            label.margin = new Vector4(175f, 0f, 10f, 0f);
            label.color = new Color(0.93f, 0.95f, 0.96f, 1f);
            MakeUiBox(tools, "Lcd" + i, new Vector2(237f, y), new Vector2(456f, 140f), CicLcd);
            states[i] = MakeUiText(tools, labels[i] + "Text", new Vector2(237f, y + 24f), new Vector2(416f, 60f), 38f, font, "");
            states[i].alignment = TextAlignmentOptions.Left;
            states[i].color = CicLcdGreen;
            TextMeshProUGUI help = LocLabel(MakeUiText(tools, "Help" + i, new Vector2(237f, y - 32f), new Vector2(416f, 44f), 24f, font, ""), helps[i]);
            help.alignment = TextAlignmentOptions.Left;
            help.color = CicLcdDim;
        }
        console.WaypointButton = buttons[0];
        console.DroneButton = buttons[1];
        console.WingmenButton = buttons[2];
        console.CruiseButton = buttons[3];
        console.WaypointText = states[0];
        console.DroneText = states[1];
        console.WingmenText = states[2];
        console.CruiseText = states[3];
        console.WaypointLed = leds[0];
        console.DroneLed = leds[1];
        console.WingmenLed = leds[2];
        console.CruiseLed = leds[3];

        // 확정 팝업(순항미사일 · 아군 편대, 로컬): 패널을 어둡게 덮어(뒤 키는 눌리지 않는다) 가운데 금속 판 + LCD(도구 · 내용 · 거리) + 확정(빨강) · 취소
        var popGo = new GameObject("ConfirmPopup", typeof(RectTransform));
        RectTransform pop = popGo.GetComponent<RectTransform>();
        pop.SetParent(tools, false);
        pop.anchorMin = Vector2.zero;
        pop.anchorMax = Vector2.one;
        pop.offsetMin = Vector2.zero;
        pop.offsetMax = Vector2.zero;
        Image dim = MakeUiBox(pop, "Dim", Vector2.zero, new Vector2(1000f, 850f), new Color(0f, 0f, 0f, 0.62f));
        dim.raycastTarget = true;
        MakeUiBox(pop, "Frame", new Vector2(0f, -10f), new Vector2(830f, 470f), CicFrame);
        MakeUiBox(pop, "Box", new Vector2(0f, -10f), new Vector2(800f, 440f), CicMetal);
        MakeUiBox(pop, "Lcd", new Vector2(0f, 75f), new Vector2(740f, 210f), CicLcd);
        console.ConfirmTitle = MakeUiText(pop, "ConfirmTitle", new Vector2(0f, 135f), new Vector2(700f, 64f), 44f, font, "");
        console.ConfirmTitle.alignment = TextAlignmentOptions.Left;
        console.ConfirmTitle.color = CicLcdAmber;
        console.ConfirmBody = MakeUiText(pop, "ConfirmBody", new Vector2(0f, 45f), new Vector2(700f, 110f), 32f, font, "");
        console.ConfirmBody.alignment = TextAlignmentOptions.Left;
        console.ConfirmBody.color = CicLcdAmber;
        Image ok = LocButton(CicEventButton(pop, console, font, "CONFIRM", new Vector2(-185f, -145f), new Vector2(350f, 120f), "ClickConfirm", 40f), "cic.confirm.ok");
        ok.color = new Color(0.48f, 0.16f, 0.1f, 1f);
        ok.GetComponentInChildren<TextMeshProUGUI>(true).color = new Color(1f, 0.9f, 0.84f, 1f);
        Outline okEdge = ok.gameObject.AddComponent<Outline>();
        okEdge.effectColor = CicFrame;
        okEdge.effectDistance = new Vector2(6f, -6f);
        Image no = LocButton(CicEventButton(pop, console, font, "CANCEL", new Vector2(185f, -145f), new Vector2(350f, 120f), "ClickCancel", 40f), "cic.confirm.cancel");
        no.color = new Color(0.2f, 0.21f, 0.23f, 1f);
        no.GetComponentInChildren<TextMeshProUGUI>(true).color = new Color(0.85f, 0.87f, 0.89f, 1f);
        Outline noEdge = no.gameObject.AddComponent<Outline>();
        noEdge.effectColor = CicFrame;
        noEdge.effectDistance = new Vector2(6f, -6f);
        console.ConfirmPopup = popGo;
        popGo.SetActive(false);

        // 테이블 표시(시작할 때 콘솔이 지도 MapRoot 아래로 옮긴다 — 꺼 둔다)
        Transform cursor = new GameObject("CicCursor").transform;
        cursor.SetParent(root.transform, false);
        CicPrimitive(PrimitiveType.Cylinder, cursor, "Beam", new Vector3(0f, 0.15f, 0f), new Vector3(0.018f, 0.15f, 0.018f), cursorBeamMat);
        CicPrimitive(PrimitiveType.Cylinder, cursor, "Halo", new Vector3(0f, 0.15f, 0f), new Vector3(0.055f, 0.15f, 0.055f), cursorHaloMat);
        CicPrimitive(PrimitiveType.Cylinder, cursor, "Foot", new Vector3(0f, 0.002f, 0f), new Vector3(0.07f, 0.001f, 0.07f), cursorMat);
        console.Cursor = cursor;

        GameObject line = new GameObject("CicAimLine");
        line.transform.SetParent(root.transform, false);
        LineRenderer lr = line.AddComponent<LineRenderer>();
        lr.sharedMaterial = lineMat;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.startWidth = 0.004f;
        lr.endWidth = 0.004f;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        console.AimLine = lr;

        Transform wp = new GameObject("CicWaypointMark").transform;
        wp.SetParent(root.transform, false);
        // 마름모는 산 꼭대기(약 0.17m)보다 위에
        CicPrimitive(PrimitiveType.Cylinder, wp, "Pole", new Vector3(0f, 0.11f, 0f), new Vector3(0.004f, 0.11f, 0.004f), wpMat);
        Transform diamond = CicPrimitive(PrimitiveType.Cube, wp, "Diamond", new Vector3(0f, 0.24f, 0f), new Vector3(0.03f, 0.03f, 0.03f), wpMat);
        diamond.localRotation = Quaternion.Euler(45f, 0f, 45f);
        console.WaypointMark = wp;

        GameObject area = new GameObject("CicAreaMark");
        area.transform.SetParent(root.transform, false);
        Transform ringT = new GameObject("Ring").transform;
        ringT.SetParent(area.transform, false);
        ringT.localPosition = new Vector3(0f, 0.2f, 0f);   // 지형(높이 과장 1.5배 — 산 꼭대기 약 0.17m)보다 위에 띄운다 — 해수면에 두면 산 속에 묻힌다
        ringT.gameObject.AddComponent<MeshFilter>().sharedMesh = ring;
        MeshRenderer ringR = ringT.gameObject.AddComponent<MeshRenderer>();
        ringR.sharedMaterial = areaMat;
        ringR.shadowCastingMode = ShadowCastingMode.Off;
        console.AreaMark = area.transform;

        // 테이블 순항미사일 표시(6-276): 미사일 풀과 같은 수, 주황 작은 점
        Material cruiseMat = CicMaterial("ACO_Cic_CruiseMark", Shader.Find("Unlit/Color"), new Color(0.22f, 0.52f, 1f));   // 아군 LACM — 파랑(2026-11-18, 주황에서 바꿈)
        var cruiseMarks = new Transform[8];
        for (int i = 0; i < cruiseMarks.Length; i++)
        {
            cruiseMarks[i] = CicPrimitive(PrimitiveType.Sphere, root.transform, "CicCruiseMark_" + i, Vector3.zero, Vector3.one * 0.012f, cruiseMat);
            cruiseMarks[i].gameObject.SetActive(false);
        }
        console.CruiseMarks = cruiseMarks;
        Transform droneMark = new GameObject("CicDroneMark").transform;
        droneMark.SetParent(root.transform, false);
        CicPrimitive(PrimitiveType.Cube, droneMark, "Body", Vector3.zero, new Vector3(0.012f, 0.006f, 0.03f), droneMat);
        CicPrimitive(PrimitiveType.Cube, droneMark, "Wing", Vector3.zero, new Vector3(0.04f, 0.003f, 0.008f), droneMat);
        console.DroneMark = droneMark;
        // UAV 이름표(테이블 플레이어 이름표와 같은 크기 · 모양)
        var tag = new GameObject("cic.btn.drone", typeof(RectTransform));
        tag.transform.SetParent(droneMark, false);
        tag.transform.localPosition = new Vector3(0f, 0.035f, 0f);
        TextMeshPro tagText = tag.AddComponent<TextMeshPro>();
        tagText.font = font;
        tagText.fontSize = 0.25f;
        tagText.alignment = TextAlignmentOptions.Center;
        tagText.enableWordWrapping = false;
        tagText.color = new Color(0.85f, 0.95f, 1f);
        tagText.rectTransform.sizeDelta = new Vector2(0.4f, 0.05f);
        LocLabel3D(tagText, "cic.btn.drone");
        tagText.fontSize = 0.25f;
        tagText.enableAutoSizing = false;
        console.DroneLabel = tag.transform;
        // 찍은 지면의 큰 빛기둥(로컬, 3초 — 고글 UAV 시점에서 보인다). 높이 400m · 폭 12m
        Transform beam = new GameObject("CicWorldBeam").transform;
        beam.SetParent(root.transform, false);
        CicPrimitive(PrimitiveType.Cylinder, beam, "Beam", new Vector3(0f, 200f, 0f), new Vector3(12f, 200f, 12f), beamMat);
        CicPrimitive(PrimitiveType.Cylinder, beam, "Halo", new Vector3(0f, 200f, 0f), new Vector3(40f, 200f, 40f), beamHaloMat);
        console.WorldBeam = beam;
        beam.gameObject.SetActive(false);
        cursor.gameObject.SetActive(false);
        wp.gameObject.SetActive(false);
        area.SetActive(false);
        droneMark.gameObject.SetActive(false);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{CicConsoleName}.prefab");
        ACO_PrefabVersions.Stamp(CicConsoleName);
        ACO_PrefabBuilder.DestroyAfterSetup(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] CIC 도구 패널 프리팹을 만들었습니다: {AssetDatabase.GetAssetPath(prefab)} — Setup Check가 전술 테이블 동쪽 테두리에 놓고 Bake가 연결합니다.", prefab);
    }

    /// 고글 자리(CIC 남쪽 벽 동쪽 책상 — 첫 모니터 화면을 덮는 패널 + 책상 위 고글 2개). 루트 = 모니터 화면 가운데(앞면), +Z = 방 쪽.
    /// 패널: 관전 대상(UAV · 기체 1~4)과 고글 회수(RESET). 고글 화면 덮개(ACO/GogglesView)와 NO SIGNAL 글자도 여기 둔다.
    public static void CreateCicGogglesPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Prefabs");
        EnsureFolder("Assets/ACO_Addon", "Generated");
        EnsureFolder("Assets/ACO_Addon/Generated", "Cic");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Shader viewShader = Shader.Find("ACO/GogglesView");
        Shader textShader = Shader.Find("TextMeshPro/Distance Field Overlay");
        if (font == null || viewShader == null || textShader == null)
        {
            Debug.LogError($"[ACO] CIC 고글: TMP 폰트({FontPath}) 또는 셰이더(ACO/GogglesView, TextMeshPro/Distance Field Overlay)를 찾지 못했습니다.");
            return;
        }
        RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(GogglesRtPath);
        if (rt == null)
        {
            rt = new RenderTexture(1024, 1024, 24) { name = "ACO_GogglesRT", antiAliasing = 2 };
            AssetDatabase.CreateAsset(rt, GogglesRtPath);
        }
        RenderTexture rtFhd = AssetDatabase.LoadAssetAtPath<RenderTexture>(GogglesRtFhdPath);
        if (rtFhd == null)
        {
            rtFhd = new RenderTexture(1536, 1536, 24) { name = "ACO_GogglesRT_FHD", antiAliasing = 2 };
            AssetDatabase.CreateAsset(rtFhd, GogglesRtFhdPath);
        }
        Material viewMat = CicMaterial("ACO_GogglesView", viewShader, Color.white);
        viewMat.SetTexture("_MainTex", rt);
        Mesh fsQuad = EnsureFullscreenQuad();
        Material steel = CicSteel();
        Material rubber = CicMaterial("ACO_Cic_Rubber", Shader.Find("Standard"), new Color(0.06f, 0.06f, 0.07f));
        rubber.SetFloat("_Glossiness", 0.2f);
        Material lens = CicMaterial("ACO_Cic_Lens", Shader.Find("Standard"), new Color(0.05f, 0.25f, 0.35f));
        lens.SetFloat("_Metallic", 0.2f);
        lens.SetFloat("_Glossiness", 0.9f);
        Material noSignalMat = new Material(font.material) { name = "ACO_Cic_NoSignalText" };
        noSignalMat.shader = textShader;
        noSignalMat.renderQueue = 4090;   // 고글 덮개(Overlay+80) 위
        noSignalMat = SaveAsset(noSignalMat, $"{CicDir}/ACO_Cic_NoSignalText.mat");

        GameObject root = new GameObject(CicGogglesName);
        ACO_CicGogglesPanel panel = root.AddUdonSharpComponent<ACO_CicGogglesPanel>();

        // 모니터 화면을 덮는 패널(화면 앞 1mm — 모니터가 곧 고글 패널)
        RectTransform view = MakeUiPanel(root.transform, 1000f, 584f);
        view.name = "ViewCanvas";
        view.localScale = Vector3.one * CicViewScale;
        view.localPosition = new Vector3(0f, 0f, 0.001f);
        LocLabel(MakeUiLabel(view, "Title", new Vector2(0f, 245f), new Vector2(940f, 64f), 44f, font, ""), "cic.view.title");
        string[] viewEvents = { "ClickViewUav", "ClickView0", "ClickView1", "ClickView2", "ClickView3" };
        Vector2[] viewPos = { new Vector2(0f, 150f), new Vector2(-240f, 35f), new Vector2(240f, 35f), new Vector2(-240f, -85f), new Vector2(240f, -85f) };
        Vector2[] viewSize = { new Vector2(940f, 100f), new Vector2(460f, 110f), new Vector2(460f, 110f), new Vector2(460f, 110f), new Vector2(460f, 110f) };
        panel.ViewTexts = new TextMeshProUGUI[viewEvents.Length];
        panel.ViewButtons = new Image[viewEvents.Length];
        for (int i = 0; i < viewEvents.Length; i++)
        {
            Image b = MakeUiButton(view, "Btn_" + viewEvents[i], viewPos[i], viewSize[i], font, i == 0 ? "UAV" : $"SLOT {i}", 36f);
            WireEvent(b.GetComponent<Button>(), panel, viewEvents[i]);
            panel.ViewButtons[i] = b;
            panel.ViewTexts[i] = b.GetComponentInChildren<TextMeshProUGUI>(true);
        }
        Image reset = LocButton(MakeUiButton(view, "Btn_ClickReset", new Vector2(-160f, -212f), new Vector2(620f, 96f), font, "RESET GOGGLES", 38f), "cic.reset");
        // 화질(관전 카메라 HD/FHD처럼, 로컬): HD 1024 ↔ FHD 1536
        Image res = MakeUiButton(view, "Btn_ClickResolution", new Vector2(320f, -212f), new Vector2(300f, 96f), font, "HD 1024", 36f);
        WireEvent(res.GetComponent<Button>(), panel, "ClickResolution");
        panel.ResolutionText = res.GetComponentInChildren<TextMeshProUGUI>(true);
        reset.color = new Color(0.45f, 0.16f, 0.12f, 1f);
        WireEvent(reset.GetComponent<Button>(), panel, "ClickReset");

        // 고글 화면 덮개(FX 레이어 — 관전 카메라는 그리지 않는다)와 NO SIGNAL
        GameObject overlay = new GameObject("GogglesOverlay");
        overlay.transform.SetParent(root.transform, false);
        overlay.layer = ACO_SceneBaker.FxLayer;
        overlay.AddComponent<MeshFilter>().sharedMesh = fsQuad;
        MeshRenderer or = overlay.AddComponent<MeshRenderer>();
        or.sharedMaterial = viewMat;
        or.shadowCastingMode = ShadowCastingMode.Off;
        or.receiveShadows = false;
        or.lightProbeUsage = LightProbeUsage.Off;
        or.reflectionProbeUsage = ReflectionProbeUsage.Off;
        overlay.SetActive(false);
        TextMeshPro noSignal = MakeText(root.transform, "NoSignal", Vector3.zero, 1.4f, new Color(1f, 0.85f, 0.3f, 1f),
            TextAlignmentOptions.Center, new Vector2(3f, 0.6f), new Vector2(0.5f, 0.5f), font, noSignalMat, "NO SIGNAL");
        LocLabel3D(noSignal, "cic.nosignal");
        noSignal.gameObject.layer = ACO_SceneBaker.FxLayer;
        noSignal.gameObject.SetActive(false);

        // 고글 2개: 책상 위 두 근무석 사이 빈자리(첫 키보드 오른쪽 끝 x 0.265 ~ 둘째 트랙볼 x 0.554, 폭 29cm) 가운데 x 0.41.
        // 바로 세워 접안부가 앉는 사람 쪽(+Z — 모델은 접안부가 −Z라 180° 돌린다), 앞뒤로 — 안쪽 것은 공책(Int_ClutterB, 높이 1.6cm) 위, 바깥 것은 책상 앞쪽.
        // 모델 크기 0.173 × 0.085 × 0.187m(밑면 −0.0378). 조금씩 다른 각도로 놓아 진열품처럼 보이지 않게
        panel.Goggles = new ACO_CicGoggles[2];
        Vector3[] gogglePos = { new Vector3(0.41f, CicGogglesDeskDrop + 0.016f + 0.0378f, 0.18f), new Vector3(0.41f, CicGogglesDeskDrop + 0.0378f, 0.41f) };
        float[] goggleYaw = { 174f, 187f };
        if (AssetDatabase.LoadAssetAtPath<GameObject>(FpvGogglesFbx) != null) { ACO_PrefabBuilder.ConfigureModelImporter(FpvGogglesFbx); }
        for (int i = 0; i < 2; i++)
        {
            ACO_CicGoggles g = CicGoggles(root.transform, $"Goggles_{i}", gogglePos[i], rubber, lens);
            g.transform.localRotation = Quaternion.Euler(0f, goggleYaw[i], 0f);
            g.Panel = panel;
            g.Texture = rt;
            g.TextureFHD = rtFhd;
            g.Overlay = or;
            g.NoSignal = noSignal.transform;
            panel.Goggles[i] = g;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{CicGogglesName}.prefab");
        ACO_PrefabVersions.Stamp(CicGogglesName);
        ACO_PrefabBuilder.DestroyAfterSetup(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] CIC 고글 자리 프리팹을 만들었습니다: {AssetDatabase.GetAssetPath(prefab)} — Setup Check가 CIC 남쪽 동쪽 책상 첫 모니터 위에 놓고 Bake가 연결합니다.", prefab);
    }

    /// 모니터 화면 가운데(책상 위 0.34m)에서 책상 윗면까지(m, 음수)
    public const float CicGogglesDeskDrop = -0.34f;

    private static Material CicSteel()
    {
        Material steel = CicMaterial("ACO_Cic_Steel", Shader.Find("Standard"), new Color(0.32f, 0.34f, 0.36f));
        steel.SetFloat("_Metallic", 0.6f);
        steel.SetFloat("_Glossiness", 0.45f);
        return steel;
    }

    /// 정찰 UAV 모델: Synty PolygonMilitary SM_Veh_Drone_01(날개폭 19.4m, 기수 +Z). 비행 중이라 바퀴 · 바퀴 다리는 뺀다.
    private const string SyntyUavPrefab = "Assets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Drone_01.prefab";
    /// 회색 단색 도장(같은 UV 배치의 Jet_01 변형 중 A) — Synty 머티리얼 대신 우리 Standard 머티리얼에 붙인다(Synty 머티리얼은 VR에서 이상하게 보였다)
    private const string SyntyUavGrayTexture = "Assets/Synty/PolygonMilitary/Textures/Vehicles/Jet_01_A.png";

    /// 정찰 UAV(격추 불가): Synty 무인기 겉모양(없으면 가디언 드론 겉모양을 다시 칠함). 표적 · 충돌 · 스크립트 없음 — ACO_CicConsole이 옮긴다.
    public static void CreateCicUavPrefab()
    {
        EnsureFolder("Assets/ACO_Addon", "Generated");
        EnsureFolder("Assets/ACO_Addon/Generated", "Cic");
        GameObject root = new GameObject(CicUavName);
        GameObject synty = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyUavPrefab);
        if (synty != null)
        {
            GameObject model = Object.Instantiate(synty, root.transform, false);
            model.name = "Model";
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t != model.transform && t.name.Contains("Wheel")) { Object.DestroyImmediate(t.gameObject); }
            }
            foreach (Component c in model.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) { continue; }
                Object.DestroyImmediate(c);
            }
            Material gray = CicMaterial("ACO_CicUav_Gray", Shader.Find("Standard"), Color.white);
            gray.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SyntyUavGrayTexture));
            gray.SetFloat("_Metallic", 0.15f);
            gray.SetFloat("_Glossiness", 0.35f);
            gray.enableInstancing = true;
            EditorUtility.SetDirty(gray);
            foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) { mats[i] = gray; }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            root.SetActive(false);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{CicUavName}.prefab");
            ACO_PrefabVersions.Stamp(CicUavName);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ACO] 정찰 UAV 프리팹을 만들었습니다(Synty SM_Veh_Drone_01): {AssetDatabase.GetAssetPath(saved)}", saved);
            return;
        }
        Debug.LogWarning($"[ACO] 정찰 UAV: {SyntyUavPrefab}가 없어 가디언 드론 겉모양으로 만듭니다.");
        Material body = CicMaterial("ACO_CicUav_Body", Shader.Find("Standard"), new Color(0.26f, 0.31f, 0.38f));
        body.SetFloat("_Metallic", 0.35f);
        body.SetFloat("_Glossiness", 0.5f);
        GameObject guardian = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{ACO_PrefabBuilder.GuardianDronePrefab}.prefab");
        Transform srcModel = guardian != null ? guardian.transform.Find("Model") : null;
        if (srcModel != null)
        {
            GameObject model = Object.Instantiate(srcModel.gameObject, root.transform, false);
            model.name = "Model";
            model.SetActive(true);
            foreach (Component c in model.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) { continue; }
                Object.DestroyImmediate(c);
            }
            foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) { mats[i] = body; }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
        }
        else
        {
            Debug.LogWarning("[ACO] 정찰 드론: 가디언 드론 프리팹의 Model을 찾지 못해 상자로 만듭니다.");
            Material m = body;
            CicBox(root.transform, "Body", Vector3.zero, new Vector3(1.2f, 0.5f, 4f), m);
            CicBox(root.transform, "Wing", Vector3.zero, new Vector3(6f, 0.12f, 0.9f), m);
        }
        root.SetActive(false);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{CicUavName}.prefab");
        ACO_PrefabVersions.Stamp(CicUavName);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 정찰 드론 프리팹을 만들었습니다: {AssetDatabase.GetAssetPath(prefab)}", prefab);
    }

    // ───────────── 부품 ─────────────

    private static Material CicMaterial(string name, Shader shader, Color color)
    {
        string path = $"{CicDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        if (m.HasProperty("_Color")) { m.SetColor("_Color", color); }
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 노란 빛기둥 재질(ACO/LightBeam — 기본 원기둥용). strength = 바닥 가운데 진하기, glow = 더하기 빛, fadePower = 위로 흐려지는 빠르기,
    /// baseSoft = 바닥을 비우는 높이 비율(그 아래는 거의 투명 — 찍은 자리의 유닛 · 마커가 보이게: 큰 기둥 15m, 테이블 2.5cm), bandFreq/bandSpeed = 위로 흐르는 띠(높이 비율 단위).
    private static Material CicBeamMaterial(string name, float strength, float glow, float fadePower, float baseSoft, float bandFreq, float bandSpeed)
    {
        Shader shader = Shader.Find("ACO/LightBeam");
        if (shader == null)
        {
            Debug.LogWarning("[ACO] CIC 빛기둥: ACO/LightBeam 셰이더가 없어 Sprites/Default로 칠합니다.");
            return CicMaterial(name, Shader.Find("Sprites/Default"), new Color(1f, 0.85f, 0.2f, 0.55f));
        }
        Material m = CicMaterial(name, shader, new Color(1f, 0.82f, 0.25f, strength));
        m.SetColor("_CoreColor", new Color(1f, 0.92f, 0.65f, 1f));   // 심은 덜 하얗게 — 기둥 속 마커가 바래지 않게
        m.SetFloat("_Glow", glow);
        m.SetFloat("_FadePower", fadePower);
        m.SetFloat("_BaseSoft", baseSoft);
        m.SetFloat("_EdgePower", 1.5f);
        m.SetFloat("_CorePower", 10f);
        m.SetFloat("_BandFreq", bandFreq);
        m.SetFloat("_BandSpeed", bandSpeed);
        m.SetFloat("_BandAmount", 0.2f);
        m.SetFloat("_Pulse", 0.1f);
        return m;
    }

    /// 빛무리용: 심 없음(심 색 = 바탕 색), 가장자리 더 부드럽게, 띠 · 맥동은 같이.
    private static Material CicBeamHalo(Material m)
    {
        if (m.shader == null || m.shader.name != "ACO/LightBeam") { return m; }
        m.SetColor("_CoreColor", new Color(1f, 0.82f, 0.25f, 1f));
        m.SetFloat("_EdgePower", 2.5f);
        return m;
    }

    private static Mesh EnsureFullscreenQuad()
    {
        Mesh q = AssetDatabase.LoadAssetAtPath<Mesh>(FullscreenQuadPath);
        if (q != null) { return q; }
        q = new Mesh { name = "ACO_FullscreenQuad" };
        q.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        q.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        q.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        q.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        AssetDatabase.CreateAsset(q, FullscreenQuadPath);
        return q;
    }

    /// XZ 평면 고리(바깥 반경 0.5, 안 0.46) — 크기(지름)로 늘인다. 양면(위 · 아래 어느 쪽에서 봐도 보인다 — 예전 한쪽 면은 위에서 컬링됐다).
    private static Mesh CicRingMesh()
    {
        string path = $"{CicDir}/ACO_CicRing2.asset";
        Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m != null) { return m; }
        const int n = 64;
        var v = new Vector3[n * 2];
        var t = new int[n * 12];
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v[i * 2] = d * 0.5f;
            v[i * 2 + 1] = d * 0.46f;
            int j = (i + 1) % n;
            int k = i * 12;
            t[k] = i * 2; t[k + 1] = j * 2; t[k + 2] = i * 2 + 1;
            t[k + 3] = i * 2 + 1; t[k + 4] = j * 2; t[k + 5] = j * 2 + 1;
            t[k + 6] = i * 2; t[k + 7] = i * 2 + 1; t[k + 8] = j * 2;
            t[k + 9] = i * 2 + 1; t[k + 10] = j * 2 + 1; t[k + 11] = j * 2;
        }
        m = new Mesh { name = "ACO_CicRing2", vertices = v, triangles = t };
        m.RecalculateNormals();
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static Transform CicBox(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        return CicPrimitive(PrimitiveType.Cube, parent, name, pos, size, mat);
    }

    private static Transform CicPrimitive(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    private static Image CicEventButton(RectTransform parent, ACO_CicConsole console, TMP_FontAsset font, string label, Vector2 pos, Vector2 size, string eventName, float fontSize)
    {
        Image img = MakeUiButton(parent, "Btn_" + eventName, pos, size, font, label, fontSize);
        WireEvent(img.GetComponent<Button>(), console, eventName);
        return img;
    }

    /// 고글 픽업: 테 + 렌즈 두 알 + 끈. VRCPickup(데스크톱은 사용 키로 켜고 끄므로 자동 들기) + VRCObjectSync(위치만).
    /// 고글 모델 = 군용 휴대 관측 장비(열상 · 레이저 거리측정 쌍안경 계열 상자형, 머리끈 없음 — Tools/aco_fpv_goggles_model.py): 서브메시 순서 = Body(국방색 고무) · Front(유리) · Foam(검은 고무) · Accent(표시등) · Metal.
    /// 원점 = 하우징 가운데, 대물 후드 쪽 +Z(+0.088), 접안 눈컵 쪽 −Z(−0.099), 폭 ±0.087(배터리 캡), 밑면 −0.0378 · 윗면(전원 스위치) +0.047.
    private const string FpvGogglesFbx = "Assets/ACO_Addon/Models/CicGoggles/ACO_FpvGoggles.fbx";
    private const string FpvGogglesAlbedo = "Assets/ACO_Addon/Textures/CicGoggles/ACO_FpvGoggles_Albedo.png";

    private static Material[] FpvGogglesMaterials()
    {
        string[] names = { "Body", "Front", "Foam", "Accent", "Metal" };
        Color[] colors = { new Color(0.19f, 0.21f, 0.14f), new Color(0.03f, 0.04f, 0.06f), new Color(0.035f, 0.035f, 0.035f), new Color(0.12f, 0.62f, 0.30f), new Color(0.10f, 0.10f, 0.11f) };
        float[] gloss = { 0.18f, 0.95f, 0.08f, 0.6f, 0.45f };
        float[] metal = { 0f, 0.3f, 0f, 0f, 0.6f };
        // 색 · 마모 · 표기는 굽힌 텍스처(Tools/aco_fpv_goggles_model.py — 한 장, 모든 재질이 같이 쓴다). 재질은 광택 · 금속감만 다르다.
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(FpvGogglesAlbedo);
        if (albedo == null) { Debug.LogWarning($"[ACO] CIC 고글: {FpvGogglesAlbedo}가 없어 단색으로 칠합니다(Tools/aco_fpv_goggles_model.py)."); }
        Material[] mats = new Material[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            mats[i] = CicMaterial("ACO_FpvGoggles_" + names[i], Shader.Find("Standard"), albedo != null ? Color.white : colors[i]);
            mats[i].SetTexture("_MainTex", albedo);
            mats[i].SetFloat("_Glossiness", gloss[i]);
            mats[i].SetFloat("_Metallic", metal[i]);
        }
        return mats;
    }

    private static ACO_CicGoggles CicGoggles(Transform parent, string name, Vector3 pos, Material rubber, Material lens)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        BoxCollider col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(0.18f, 0.07f, 0.09f);
        // 트리거(6-293): 들고 기체에 타면 고글 콜라이더가 기체를 밀었다. 키네마틱 · 중력 없음이라 놓아도 떨어지지 않고, 픽업은 트리거로도 잡힌다
        col.isTrigger = true;
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FpvGogglesFbx);
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var pickup = go.AddComponent<VRC.SDK3.Components.VRCPickup>();
        pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;
        pickup.InteractionText = "CIC GOGGLES";
        pickup.UseText = "MARK";
        go.AddComponent<VRC.SDK3.Components.VRCObjectSync>();
        MeshFilter fbxMesh = fbx != null ? fbx.GetComponentInChildren<MeshFilter>(true) : null;
        if (fbxMesh != null && fbxMesh.sharedMesh != null)
        {
            // 관측 장비 모델(하우징 · 광학부 · 눈컵 · 레일 · 버튼). 잡는 콜라이더 = 전체 덩어리
            col.size = new Vector3(0.173f, 0.085f, 0.187f);
            col.center = new Vector3(0f, 0.0044f, -0.0055f);
            GameObject model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = fbxMesh.sharedMesh;
            MeshRenderer mr = model.AddComponent<MeshRenderer>();
            mr.sharedMaterials = FpvGogglesMaterials();
        }
        else
        {
            Debug.LogWarning($"[ACO] CIC 고글: {FpvGogglesFbx}가 없어 상자 고글로 만듭니다(Tools/aco_fpv_goggles_model.py).");
            CicBox(go.transform, "Frame", new Vector3(0f, 0f, 0f), new Vector3(0.17f, 0.06f, 0.05f), rubber);
            Transform l = CicPrimitive(PrimitiveType.Cylinder, go.transform, "LensL", new Vector3(-0.042f, 0f, 0.027f), new Vector3(0.05f, 0.006f, 0.05f), lens);
            l.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Transform r = CicPrimitive(PrimitiveType.Cylinder, go.transform, "LensR", new Vector3(0.042f, 0f, 0.027f), new Vector3(0.05f, 0.006f, 0.05f), lens);
            r.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CicBox(go.transform, "Strap", new Vector3(0f, 0f, -0.06f), new Vector3(0.16f, 0.02f, 0.08f), rubber);
        }
        return go.AddUdonSharpComponent<ACO_CicGoggles>();
    }
}
