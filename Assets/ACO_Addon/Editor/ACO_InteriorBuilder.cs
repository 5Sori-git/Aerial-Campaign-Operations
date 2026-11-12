using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ACO;
using SaccFlightAndVehicles;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// 함내 격실 프리팹 ACO_Interior: 브리핑실, CIC, 복도, 침실.
/// 빛은 전부 굽는다. 형광등 자리마다 구운 점광원, 벽판은 라이트맵, 나머지(바닥, 천장, 늑골, 보, 띠, 소품)는 라이트 프로브, 격실마다 박스 투영 반사 프로브.
/// 바닥과 천장은 Unity 라이트맵이 격실마다 까맣게 구워져(원인 불명) Blender로 구운 빛 텍스처를 쓴다(Tools/aco_interior_bake.py). Unity 굽기와 상관없다.
/// 재질은 ACO/Interior 셰이더라 태양과 안개를 받지 않고 실시간 조명 비용이 없다. 조명은 프리팹에 들어 있으므로 씬에 놓고 [라이트 굽기]를 한다.
/// 모델은 Tools/aco_interior_models.py가 만든다: Models/Interior/ACO_Interior.fbx(메시 이름 = "모듈__재질"), ACO_Interior_Layout.txt, Textures/Interior.
/// 모듈 640개를 따로 두면 드로우콜이 수천 개이므로, 격실마다 재질별 메시 하나로 합친다. 메시는 Generated/Interior/ACO_Interior_Meshes.asset 한 파일에 모인다.
/// 시계만 합치지 않는다. 바늘이 돌아야 하기 때문이다.
/// 충돌: 격실마다 벽, 바닥, 천장을 합친 메시 콜라이더 하나 + 큰 가구마다 상자 콜라이더.
/// 프리팹 원점이 레이아웃 원점이고 바닥이 Y = 0이다. 씬에 놓는 일과 조명은 여기서 하지 않는다.
/// 머티리얼은 있으면 다시 쓰고 값만 맞춘다.
public static class ACO_InteriorBuilder
{
    public const string PrefabName = "ACO_Interior";
    private const string PrefabPath = "Assets/ACO_Addon/Prefabs/" + PrefabName + ".prefab";
    private const string MeshDir = "Assets/ACO_Addon/Generated/Interior";
    private const string MeshAsset = MeshDir + "/ACO_Interior_Meshes.asset";
    private const string ModelFbx = "Assets/ACO_Addon/Models/Interior/ACO_Interior.fbx";
    private const string LayoutPath = "Assets/ACO_Addon/Models/Interior/ACO_Interior_Layout.txt";
    private const string RoomsPath = "Assets/ACO_Addon/Models/Interior/ACO_Interior_Rooms.txt";
    private const string InteriorShader = "ACO/Interior";
    private const string TextureDir = "Assets/ACO_Addon/Textures/Interior";
    private const string Tool = "Tools/aco_interior_models.py";
    private const string ClockScript = "Assets/ACO_Addon/Scripts/Environment/ACO_WallClock.cs";
    private const string ClockProgram = "Assets/ACO_Addon/Udon/ACO_WallClock.asset";

    // 메시를 건드리지 않는 기능부. 구운 라이트맵을 지키려고 따로 다시 만든다(UpdateSystems)
    private const string SystemsName = "Systems";
    private const float ZoneMargin = 0.25f;     // 격벽 두께와 문턱까지 덮는다
    private const string DoorRoom = "Corridor";
    private const string DoorFrame = "Int_HatchFrame";  // 복도에서는 닫힌 문만 이 모듈을 쓴다
    private const string DoorLeaf = "Int_HatchLeaf";
    private const float DeckDoorYaw = -90f;     // 복도 끝 문. 옆벽 문은 격납고로 간다
    private const float DoorDepth = 0.3f;       // 문짝 바퀴까지 덮어 문짝 콜라이더보다 먼저 눌린다
    private const float ArriveDistance = 1f;
    private const float ArriveLift = 0.05f;
    private const string RadioRoom = "Cic";
    private const int RadioColumns = 3;
    private const int RadioRows = 2;
    private const float RadioWallGap = 0.2f;    // 몸이 벽에서 떨어지는 거리. 이만큼 빼고 나누면 벽 너머에 닿지 않는다
    public const string DoorHangar = "Door_Hangar";
    public const string DoorDeck = "Door_Deck";
    public const string ArriveHangar = "Arrive_Hangar";
    public const string ArriveDeck = "Arrive_Deck";
    public const string ShipHatchHangar = "ACO_ShipHatch_Hangar";
    public const string ShipHatchDeck = "ACO_ShipHatch_Deck";
    public const string ShipHatchArrive = "Arrive";

    private const string FurnitureName = "Col_Furniture";
    private const string SpectatorDesk = "Int_SpectatorDesk";
    private const float SpectatorDeskDepth = 0.49f;    // 앞면의 관전 패널 버튼 뒤에서 끝난다. 더 나오면 버튼을 누르는 레이저를 막는다

    private const string TvScreen = "Int_TvScreen";
    private const string ClockBody = "Int_Clock";
    private static readonly string[] ClockHands = { "Int_ClockHour", "Int_ClockMinute", "Int_ClockSecond" };

    // 충돌 메시에 넣는 모듈: 머리글, 재질(빈 문자열 = 전부)
    private static readonly string[,] WallParts =
    {
        { "Int_Panel_", "wall" },
        { "Int_HatchWall_", "wall" },
        { "Int_Rib_", "" },
    };

    // 메시 범위 그대로 상자 콜라이더를 두는 큰 가구
    private static readonly string[] SolidParts =
    {
        "Int_Chair_", "Int_Desk", "Int_Lectern", "Int_Bookcase", "Int_CicDesk", "Int_Rack", "Int_RadioDesk",
        "Int_SpectatorDesk", "Int_Locker", "Int_TrophyCase", "Int_Bunk", "Int_TvDesk",
    };

    // 라이트맵은 큰 면만 받는다. 벽 띠, 늑골처럼 좁고 조각이 많은 것을 넣으면 UV가 겹쳐 얼룩이 생긴다
    private static readonly string[] LightmapMaterials = { "wall" };
    private const char SurfaceMark = '@';           // 바닥과 천장: 따로 합치고 Blender 빛 텍스처를 쓴다
    private const string BakedLightPath = TextureDir + "/ACO_Int_BakedLight.exr";
    private const float BakedLightScale = 1f;       // 벽 밝기와 맞출 때 재질의 이 값만 바꾸면 된다. 다시 구울 필요가 없다
    private const float LightmapHardAngle = 120f;   // 모서리를 깎은 면이 앞면과 한 조각이 되게 한다
    private const float LightmapPackMargin = 0.012f;
    // 씬 해상도는 4텍셀/m다(ACO_LightingSetup). 가까이서 보는 벽이라 5배인 20텍셀/m로 굽는다
    private const float LightmapScale = 5f;

    private struct RoomLight
    {
        public Color Color;
        public float Intensity;
    }

    // 격실별 형광등. 값을 바꾸면 다시 구워야 한다. CIC는 화면을 보는 방이라 어둡고 푸르다
    private static readonly Dictionary<string, RoomLight> RoomLights = new Dictionary<string, RoomLight>
    {
        { "Briefing", new RoomLight { Color = new Color(1f, 0.95f, 0.86f), Intensity = 2.2f } },
        { "Cic", new RoomLight { Color = new Color(0.78f, 0.88f, 1f), Intensity = 1.1f } },
        { "Corridor", new RoomLight { Color = new Color(1f, 0.96f, 0.9f), Intensity = 1.5f } },
        { "Berth", new RoomLight { Color = new Color(1f, 0.92f, 0.8f), Intensity = 1.5f } },
    };
    private const float LightRange = 9f;
    private const float LightDrop = 0.13f;          // 등기구 윗면에서 관까지
    private const float LightSoftness = 0.5f;       // 구운 그림자 가장자리
    private const string Fixture = "Int_Fixture";   // 조명이 등기구 안쪽에 있어, 그림자를 드리우면 천장에 큰 얼룩이 생긴다
    private const char NoShadowMark = '!';

    private static readonly float[] ProbeHeights = { 0.35f, 1.25f, 2.1f };
    private const float ProbeStep = 1.6f;
    private const float ProbeMargin = 0.35f;

    // 자체 발광: 알베도에 곱한다. 형광등은 블룸이 걸릴 만큼 밝다
    private static readonly Dictionary<string, float> Emission = new Dictionary<string, float>
    {
        { "tube", 3f }, { "led", 2f }, { "exit", 2.5f }, { "tvscreen", 1f },
    };
    // 다른 재질의 텍스처를 함께 쓰는 재질
    private static readonly Dictionary<string, string> SharedTexture = new Dictionary<string, string>
    {
        { "exit", "sign" },
    };

    // 거칠기, 금속성. 렌더 확인에 쓴 값과 같다
    private static readonly Dictionary<string, Vector2> Surfaces = new Dictionary<string, Vector2>
    {
        { "wall", new Vector2(0.55f, 0f) }, { "strip", new Vector2(0.5f, 0f) }, { "paint", new Vector2(0.5f, 0f) }, { "white", new Vector2(0.45f, 0f) },
        { "deck", new Vector2(0.3f, 0f) }, { "door", new Vector2(0.35f, 0f) }, { "alum", new Vector2(0.35f, 0.6f) }, { "steel", new Vector2(0.4f, 0.6f) },
        { "fabric", new Vector2(0.75f, 0f) }, { "label", new Vector2(0.7f, 0f) }, { "rubber", new Vector2(0.6f, 0f) }, { "red", new Vector2(0.3f, 0f) },
        { "blue", new Vector2(0.4f, 0f) }, { "tube", new Vector2(0.3f, 0f) }, { "wood", new Vector2(0.45f, 0f) }, { "screen", new Vector2(0.2f, 0f) },
        { "sign", new Vector2(0.35f, 0f) }, { "cable", new Vector2(0.55f, 0f) }, { "cork", new Vector2(0.8f, 0f) }, { "led", new Vector2(0.3f, 0f) },
        { "mat", new Vector2(0.7f, 0f) }, { "keys", new Vector2(0.5f, 0f) }, { "ball", new Vector2(0.15f, 0f) }, { "bed", new Vector2(0.8f, 0f) },
        { "blanket", new Vector2(0.8f, 0f) }, { "gold", new Vector2(0.3f, 0.8f) }, { "silver", new Vector2(0.3f, 0.8f) }, { "canvas", new Vector2(0.8f, 0f) },
        { "orange", new Vector2(0.6f, 0f) }, { "webbing", new Vector2(0.8f, 0f) }, { "glass", new Vector2(0.1f, 0f) }, { "curtain", new Vector2(0.85f, 0f) },
        { "exit", new Vector2(0.35f, 0f) },
    };
    private const float GlassAlpha = 0.14f;

    // 텍스처 없이 단색으로 쓰는 재질: 형광등 관과 덮개
    private static readonly Dictionary<string, Color> Untextured = new Dictionary<string, Color>
    {
        { "tube", new Color(0.9f, 0.9f, 0.85f) },
        { "diffuser", new Color(0.9f, 0.9f, 0.85f) },
    };

    private struct Placement
    {
        public string Module;
        public Vector3 Position;
        public float RotY;
        public Matrix4x4 Matrix { get { return Matrix4x4.TRS(Position, Quaternion.Euler(0f, RotY, 0f), Vector3.one); } }
    }

    private struct Piece
    {
        public string Material;
        public Mesh Mesh;
    }

    private struct PanelSpot
    {
        public string Room;
        public string Frame;
        public int Index;       // 같은 틀이 여러 개일 때 레이아웃 순서
        public string Panel;    // 씬의 오브젝트 이름
        public string Prefab;   // 씬에 없으면 만들 프리팹. 비우면 Panel과 같다
    }

    // 틀마다 들어가는 패널. 씬에 있는 패널을 옮기므로 연결은 그대로다
    private static readonly PanelSpot[] PanelSpots =
    {
        new PanelSpot { Room = "Briefing", Frame = "Int_Frame_300", Panel = "ACO_MissionConsole" },
        new PanelSpot { Room = "Briefing", Frame = "Int_Frame_Side", Panel = "ACO_MissionBoard" },
        new PanelSpot { Room = "Briefing", Frame = "Int_Frame_Hangar", Panel = HangarCopy, Prefab = "ACO_Hangar" },
        new PanelSpot { Room = "Briefing", Frame = "Int_Frame_160", Panel = ACO_RadarBuilder.PlanMapName },
        new PanelSpot { Room = "Corridor", Frame = "Int_Frame_120", Panel = ACO_HUDBuilder.HiScoreName },
        new PanelSpot { Room = "Berth", Frame = "Int_Frame_Hud", Panel = "ACO_HudSettings" },
    };
    private const string TacticalMap = "ACO_TacticalMap";
    private const float TableTop = 0.652f;      // Int_PlotTable 유리판 윗면
    private const float TableMapRadius = 1.1f;
    private const float TableMapLift = 0.003f;
    private const float TableActiveRadius = 6f;
    private const string HangarCopy = "ACO_Hangar_Briefing";    // 브리핑실의 격납고 복제 패널. 원본은 격납고에 그대로 있다
    private const string SpectatorPanel = "ACO_SpectatorPanel";
    private const float FrameBorder = 0.03f;    // 틀 테두리
    private const float PanelStandoff = 0.006f; // 틀 앞면에서 띄우는 거리
    private const float SmallPanelScale = 0.6f; // 이보다 줄면 글자를 읽기 어렵다

    /// 씬의 패널을 ACO_Interior의 틀에 맞춰 옮기고 비율을 유지해 줄인다. 패널 앞면이 프리팹 +Z라는 전제다. 되돌리기(Ctrl+Z)가 된다.
    [MenuItem("ACO/Interior/Fit Panels To Frames")]
    public static void FitPanels()
    {
        Transform interior = FindSceneObject(PrefabName);
        if (interior == null)
        {
            Debug.LogWarning($"[ACO] 씬에 {PrefabName}이 없습니다. 프리팹을 먼저 놓으세요.");
            return;
        }
        Dictionary<string, List<Piece>> modules = ReadModules(AssetDatabase.LoadAssetAtPath<GameObject>(ModelFbx));
        List<KeyValuePair<string, List<Placement>>> rooms = ReadLayout(modules);
        Undo.SetCurrentGroupName("ACO fit panels");
        var log = new System.Text.StringBuilder("[ACO] 패널 맞추기:");
        foreach (PanelSpot spot in PanelSpots)
        {
            List<Placement> frames = FramesOf(rooms, spot.Room, spot.Frame);
            if (spot.Index >= frames.Count)
            {
                log.Append($"\n- {spot.Panel}: 틀 {spot.Frame}이 레이아웃에 없어 건너뜀");
                continue;
            }
            Transform panel = FindOrCreatePanel(spot, interior);
            if (panel == null)
            {
                log.Append($"\n- {spot.Panel}: 프리팹이 없어 건너뜀");
                continue;
            }
            log.Append("\n- ").Append(FitPanel(panel, interior, frames[spot.Index], BoundsOf(modules[spot.Frame])));
        }
        log.Append("\n- ").Append(FitSpectator(interior, FramesOf(rooms, "Cic", "Int_Frame_260"), FramesOf(rooms, "Cic", "Int_Frame_SpecCtrl"), modules));
        log.Append("\n- ").Append(FitTacticalMap(interior, FramesOf(rooms, "Cic", "Int_PlotTable")));
        EditorSceneManagerMarkDirty(interior);
        log.Append("\n→ 씬 뷰에서 확인하세요. 맞으면 씬을 저장하고, 안 맞으면 Ctrl+Z로 되돌립니다. 패널을 새로 만들었으면 Bake로 연결하세요. 전술 테이블의 HomeBase에는 항모를 넣으세요.");
        Debug.Log(log.ToString());
    }

    /// CIC 전술 테이블 지도. 씬에 없으면 받침 없이 만들어 유리판 위에 놓는다.
    private static string FitTacticalMap(Transform interior, List<Placement> tables)
    {
        if (tables.Count == 0) { return $"{TacticalMap}: 테이블이 레이아웃에 없어 건너뜀"; }
        Transform map = FindSceneObject(TacticalMap);
        if (map == null)
        {
            GameObject made = ACO_HUDBuilder.CreateTacticalMap(false, TableMapRadius);
            Undo.RegisterCreatedObjectUndo(made, "ACO fit panels");
            map = made.transform;
        }
        Undo.RecordObject(map, "ACO fit panels");
        map.rotation = interior.rotation;
        map.localScale = Vector3.one;
        map.position = interior.TransformPoint(tables[0].Position + Vector3.up * TableTop);
        ACO_TacticalMap component = map.GetComponent<ACO_TacticalMap>();
        Undo.RecordObject(component, "ACO fit panels");
        component.MapRadius = TableMapRadius;
        component.ActiveRadius = TableActiveRadius;
        ACO_RadarBuilder.EnsureTacticalNorthMark(component);
        // 받침이 있던 예전 지도: 받침을 끄고 지도를 루트 높이로 내린다. 윗면은 모델 테이블이다
        Transform table = map.Find("Table");
        if (table != null)
        {
            Undo.RecordObject(table.gameObject, "ACO fit panels");
            table.gameObject.SetActive(false);
        }
        Transform mapRoot = component.MapRoot;
        if (mapRoot != null)
        {
            Undo.RecordObject(mapRoot, "ACO fit panels");
            mapRoot.localPosition = Vector3.up * TableMapLift;
            mapRoot.localRotation = Quaternion.identity;
            Transform sea = mapRoot.Find("Sea");
            if (sea != null)
            {
                Undo.RecordObject(sea, "ACO fit panels");
                sea.localScale = new Vector3(TableMapRadius * 2f, sea.localScale.y, TableMapRadius * 2f);
            }
        }
        EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(map);
        return $"{TacticalMap}: CIC 테이블 위, 반경 {TableMapRadius}m";
    }

    private static List<Placement> FramesOf(List<KeyValuePair<string, List<Placement>>> rooms, string room, string frame)
    {
        List<Placement> placements = rooms.Find(r => r.Key == room).Value;
        return placements == null ? new List<Placement>() : placements.FindAll(q => q.Module == frame);
    }

    private static Transform FindOrCreatePanel(PanelSpot spot, Transform interior)
    {
        Transform panel = FindSceneObject(spot.Panel);
        if (panel != null) { return panel; }
        string prefab = string.IsNullOrEmpty(spot.Prefab) ? spot.Panel : spot.Prefab;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ACO_Addon/Prefabs/{prefab}.prefab");
        if (asset == null) { return null; }
        var made = (GameObject)PrefabUtility.InstantiatePrefab(asset, interior.gameObject.scene);
        made.name = spot.Panel;
        Undo.RegisterCreatedObjectUndo(made, "ACO fit panels");
        return made.transform;
    }

    /// 관전 패널은 둘로 나눈다. 화면은 틀 안쪽에 딱 맞추고, 버튼 캔버스는 조작대 앞 틀로 옮긴다.
    /// 패널 자체의 크기는 1로 둔다. 스트림 카메라용 돔과 전체 화면이 이 자리를 기준으로 거리를 잰다.
    private static string FitSpectator(Transform interior, List<Placement> screens, List<Placement> controls, Dictionary<string, List<Piece>> modules)
    {
        Transform panel = FindSceneObject(SpectatorPanel);
        if (panel == null) { return $"{SpectatorPanel}: 씬에 없어 건너뜀"; }
        Transform screen = panel.Find("Screen");
        Canvas canvas = panel.GetComponentInChildren<Canvas>(true);
        if (screen == null || canvas == null || screens.Count == 0 || controls.Count == 0) { return $"{SpectatorPanel}: 화면, 캔버스나 틀을 찾지 못해 건너뜀"; }

        Bounds screenBounds = BoundsOf(modules["Int_Frame_260"]);
        Quaternion rotation = interior.rotation * Quaternion.Euler(0f, screens[0].RotY, 0f);
        Undo.RecordObject(panel, "ACO fit panels");
        Undo.RecordObject(screen, "ACO fit panels");
        panel.rotation = rotation;
        panel.localScale = Vector3.one;
        panel.position = interior.TransformPoint(screens[0].Position) + rotation * new Vector3(screenBounds.center.x, screenBounds.center.y, screenBounds.max.z + PanelStandoff);
        float width = screenBounds.size.x - FrameBorder * 2f;
        float height = screenBounds.size.y - FrameBorder * 2f;
        screen.localPosition = Vector3.zero;
        screen.localScale = new Vector3(width, height, 1f);

        Bounds controlBounds = BoundsOf(modules["Int_Frame_SpecCtrl"]);
        Quaternion controlRotation = interior.rotation * Quaternion.Euler(0f, controls[0].RotY, 0f);
        var rect = (RectTransform)canvas.transform;
        Undo.RecordObject(rect, "ACO fit panels");
        float scale = Mathf.Min((controlBounds.size.x - FrameBorder * 2f) / rect.rect.width, (controlBounds.size.y - FrameBorder * 2f) / rect.rect.height);
        rect.rotation = controlRotation * Quaternion.Euler(0f, 180f, 0f);
        rect.localScale = Vector3.one * scale;
        rect.position = interior.TransformPoint(controls[0].Position) + controlRotation * new Vector3(controlBounds.center.x, controlBounds.center.y, controlBounds.max.z + PanelStandoff);
        EditorUtility.SetDirty(screen);
        EditorUtility.SetDirty(rect);
        EditorUtility.SetDirty(panel);
        return $"{SpectatorPanel}: 화면 {width:0.00} x {height:0.00}m, 버튼 {rect.rect.width * scale:0.00} x {rect.rect.height * scale:0.00}m(조작대 앞)";
    }

    private static string FitPanel(Transform panel, Transform interior, Placement frame, Bounds bounds)
    {
        Undo.RecordObject(panel, "ACO fit panels");
        Quaternion rotation = interior.rotation * Quaternion.Euler(0f, frame.RotY, 0f);
        panel.rotation = rotation;
        if (!PanelExtent(panel, out Vector2 min, out Vector2 max)) { return $"{panel.name}: 크기를 재지 못해 건너뜀"; }
        Vector2 size = max - min;
        float width = bounds.size.x - FrameBorder * 2f;
        float height = bounds.size.y - FrameBorder * 2f;
        float scale = Mathf.Min(width / size.x, height / size.y);
        Vector3 parent = panel.parent != null ? panel.parent.lossyScale : Vector3.one;
        panel.localScale = Vector3.one * scale / parent.x;
        Vector2 middle = (min + max) * 0.5f * scale;
        Vector3 front = interior.TransformPoint(frame.Position) + rotation * new Vector3(bounds.center.x, bounds.center.y, bounds.max.z + PanelStandoff);
        panel.position = front - rotation * new Vector3(middle.x, middle.y, 0f);
        EditorUtility.SetDirty(panel);
        float fill = size.x * scale * size.y * scale / (width * height);
        string warn = scale < SmallPanelScale ? " (많이 줄었다. 글자를 확인할 것)" : "";
        return $"{panel.name}: 원래 {size.x:0.00} x {size.y:0.00}m → {size.x * scale:0.00} x {size.y * scale:0.00}m, 배율 {scale:0.00}, 틀 {width:0.00} x {height:0.00}m의 {fill * 100f:0}%{warn}";
    }

    /// 패널의 면 범위(패널 로컬, 크기 1 기준). 캔버스와 배경, 화면 메시만 본다. 글자와 카메라용 돔은 뺀다.
    private static bool PanelExtent(Transform panel, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);
        Vector3 scale = panel.localScale;
        panel.localScale = Vector3.one;
        var corners = new Vector3[4];
        var points = new List<Vector3>();
        foreach (Canvas canvas in panel.GetComponentsInChildren<Canvas>(true))
        {
            ((RectTransform)canvas.transform).GetWorldCorners(corners);
            points.AddRange(corners);
        }
        foreach (MeshFilter filter in panel.GetComponentsInChildren<MeshFilter>(true))
        {
            if ((filter.name != "Background" && filter.name != "Screen" && filter.name != "Bezel") || filter.sharedMesh == null) { continue; }
            Bounds b = filter.sharedMesh.bounds;
            foreach (float x in new[] { b.min.x, b.max.x })
            {
                foreach (float y in new[] { b.min.y, b.max.y })
                {
                    points.Add(filter.transform.TransformPoint(new Vector3(x, y, b.center.z)));
                }
            }
        }
        foreach (Vector3 q in points)
        {
            Vector3 local = panel.InverseTransformPoint(q);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        panel.localScale = scale;
        return points.Count > 0;
    }

    private static Transform FindSceneObject(string name)
    {
        foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name == name && t.gameObject.scene.IsValid() && (t.parent == null || t.parent.name != name)) { return t; }
        }
        return null;
    }

    private static void EditorSceneManagerMarkDirty(Transform t)
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
    }

    /// 창의 "함내 격실" 항목.
    public static GameObject Build()
    {
        EnsureFolders();
        EnsureClockProgram();
        ACO_PrefabBuilder.ConfigureModelImporter(ModelFbx);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelFbx);
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFbx);
        Dictionary<string, List<Piece>> modules = ReadModules(model);
        CheckModelAxes(modules);
        List<KeyValuePair<string, List<Placement>>> rooms = ReadLayout(modules);
        Dictionary<string, List<Bounds>> volumes = ReadRooms();

        if (File.Exists(MeshAsset)) { AssetDatabase.DeleteAsset(MeshAsset); }
        bool created = false;
        var root = new GameObject(PrefabName);
        try
        {
            foreach (KeyValuePair<string, List<Placement>> room in rooms)
            {
                var roomObject = new GameObject(room.Key);
                roomObject.transform.SetParent(root.transform, false);
                BuildRenderers(roomObject.transform, room.Key, room.Value, modules, ref created);
                BuildWallCollider(roomObject.transform, room.Key, room.Value, modules, ref created);
                BuildBoxes(roomObject.transform, room.Value, modules);
                BuildClocks(roomObject.transform, room.Value, modules);
                BuildTvScreen(roomObject.transform, room.Value, modules);
                BuildSunBlock(roomObject.transform);
                BuildLights(roomObject.transform, room.Key, room.Value);
                if (!volumes.TryGetValue(room.Key, out List<Bounds> boxes)) { throw new InvalidOperationException($"[ACO] {RoomsPath}에 {room.Key}가 없습니다. {Tool}을 다시 실행하세요."); }
                BuildProbes(roomObject.transform, boxes, room.Value, modules);
            }
            BuildSystems(root.transform, rooms, volumes, modules);
            BuildShipHatches(rooms, modules);
            AssetDatabase.SaveAssets();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            ACO_PrefabVersions.Stamp(PrefabName);
            Debug.Log("[ACO] 함내 격실 프리팹을 만들었습니다: " + PrefabPath);
            return prefab;
        }
        finally { ACO_PrefabBuilder.DestroyAfterSetup(root); }
    }

    /// 기능부(구역, 문, 무전)와 가구 콜라이더만 다시 만든다. 렌더러와 메시를 그대로 두므로 빛을 다시 굽지 않아도 된다.
    public static void UpdateSystems()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Build();
            return;
        }
        Dictionary<string, List<Piece>> modules = ReadModules(AssetDatabase.LoadAssetAtPath<GameObject>(ModelFbx));
        List<KeyValuePair<string, List<Placement>>> rooms = ReadLayout(modules);
        Dictionary<string, List<Bounds>> volumes = ReadRooms();
        BuildShipHatches(rooms, modules);
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform old = contents.transform.Find(SystemsName);
            if (old != null) { UnityEngine.Object.DestroyImmediate(old.gameObject); }
            BuildSystems(contents.transform, rooms, volumes, modules);
            foreach (KeyValuePair<string, List<Placement>> room in rooms)
            {
                Transform roomObject = contents.transform.Find(room.Key);
                if (roomObject == null) { continue; }
                Transform furniture = roomObject.Find(FurnitureName);
                if (furniture != null) { UnityEngine.Object.DestroyImmediate(furniture.gameObject); }
                BuildBoxes(roomObject, room.Value, modules);
            }
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            ACO_PrefabVersions.Stamp(PrefabName);
            Debug.Log("[ACO] 함내 격실의 구역, 문, 무전을 다시 만들었습니다. Bake로 잇습니다.");
        }
        // AddUdonSharpComponent의 뒷정리가 끝난 뒤에 내린다
        finally { EditorApplication.delayCall += () => PrefabUtility.UnloadPrefabContents(contents); }
    }

    // ───────────── 기능부 ─────────────

    private static void BuildSystems(Transform root, List<KeyValuePair<string, List<Placement>>> rooms, Dictionary<string, List<Bounds>> volumes, Dictionary<string, List<Piece>> modules)
    {
        var systems = new GameObject(SystemsName).transform;
        systems.SetParent(root, false);
        BuildZones(systems, volumes);
        BuildDoors(systems, rooms, modules);
        BuildRadio(systems, volumes);
    }

    /// 격실마다 단위 상자. 비, 물속 연출, 표적 마커, 안전지대가 이 상자로 격실 안을 판단한다.
    private static void BuildZones(Transform systems, Dictionary<string, List<Bounds>> volumes)
    {
        var parent = new GameObject("Zones").transform;
        parent.SetParent(systems, false);
        var boxes = new List<Transform>();
        foreach (KeyValuePair<string, List<Bounds>> room in volumes)
        {
            for (int i = 0; i < room.Value.Count; i++)
            {
                var box = new GameObject($"Zone_{room.Key}_{i}").transform;
                box.SetParent(parent, false);
                box.localPosition = room.Value[i].center;
                box.localScale = room.Value[i].size + Vector3.one * (ZoneMargin * 2f);
                boxes.Add(box);
            }
        }
        ACO_InteriorZone zone = systems.gameObject.AddUdonSharpComponent<ACO_InteriorZone>();
        zone.Boxes = boxes.ToArray();
    }

    /// 닫힌 문 둘. 상대편 목적지는 씬에 있으므로 Bake가 Target을 잇는다.
    private static void BuildDoors(Transform systems, List<KeyValuePair<string, List<Placement>>> rooms, Dictionary<string, List<Piece>> modules)
    {
        var parent = new GameObject("Doors").transform;
        parent.SetParent(systems, false);
        Bounds frame = BoundsOf(modules[DoorFrame]);
        foreach (Placement place in rooms.Find(r => r.Key == DoorRoom).Value)
        {
            if (place.Module != DoorFrame) { continue; }
            bool deck = Mathf.Abs(Mathf.DeltaAngle(place.RotY, DeckDoorYaw)) < 1f;
            Quaternion rotation = Quaternion.Euler(0f, place.RotY, 0f);
            GameObject door = MakeDoor(deck ? DoorDeck : DoorHangar, frame, deck ? "TO FLIGHT DECK" : "TO HANGAR");
            door.transform.SetParent(parent, false);
            door.transform.localPosition = place.Position;
            door.transform.localRotation = rotation;
            var arrive = new GameObject(deck ? ArriveDeck : ArriveHangar).transform;
            arrive.SetParent(parent, false);
            arrive.localPosition = place.Position + rotation * Vector3.forward * ArriveDistance + Vector3.up * ArriveLift;
            arrive.localRotation = rotation;
        }
    }

    private static GameObject MakeDoor(string name, Bounds frame, string text)
    {
        var go = new GameObject(name);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(frame.center.x, frame.center.y, DoorDepth * 0.5f);
        box.size = new Vector3(frame.size.x, frame.size.y, DoorDepth);
        ACO_Teleporter teleporter = go.AddUdonSharpComponent<ACO_Teleporter>();
        UdonSharpEditorUtility.GetBackingUdonBehaviour(teleporter).InteractionText = text;
        return go;
    }

    /// CIC 전체를 덮되 다른 격실로 넘어가지 않게 작은 무전 구역을 격자로 깐다. SAV 구역은 구라서 하나로는 네모난 방을 맞출 수 없다.
    /// 몸이 닿을 수 있는 범위를 칸으로 나누고 칸의 대각선 절반을 반지름으로 쓰면 칸 모서리까지 덮는다.
    private static void BuildRadio(Transform systems, Dictionary<string, List<Bounds>> volumes)
    {
        var parent = new GameObject("Radio").transform;
        parent.SetParent(systems, false);
        Bounds room = volumes[RadioRoom][0];
        float cellX = (room.size.x - RadioWallGap * 2f) / RadioColumns;
        float cellZ = (room.size.z - RadioWallGap * 2f) / RadioRows;
        float radius = new Vector2(cellX, cellZ).magnitude * 0.5f;
        for (int z = 0; z < RadioRows; z++)
        {
            for (int x = 0; x < RadioColumns; x++)
            {
                var go = new GameObject($"RadioZone_{x}_{z}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(
                    room.min.x + RadioWallGap + cellX * (x + 0.5f), room.min.y, room.min.z + RadioWallGap + cellZ * (z + 0.5f));
                go.AddUdonSharpComponent<SaccRadioZone>().ZoneRadius = radius;
            }
        }
    }

    /// 격납고와 갑판에 놓는 돌아오는 문. 복도의 닫힌 문과 같은 모델이지만 바깥이라 Standard 재질로 태양을 받는다.
    private static void BuildShipHatches(List<KeyValuePair<string, List<Placement>>> rooms, Dictionary<string, List<Piece>> modules)
    {
        List<Placement> corridor = rooms.Find(r => r.Key == DoorRoom).Value;
        Placement frame = corridor.Find(p => p.Module == DoorFrame);
        Placement leaf = corridor.Find(p => p.Module == DoorLeaf);
        Vector3 leafOffset = Quaternion.Inverse(Quaternion.Euler(0f, frame.RotY, 0f)) * (leaf.Position - frame.Position);
        foreach (string name in new[] { ShipHatchHangar, ShipHatchDeck })
        {
            GameObject root = MakeDoor(name, BoundsOf(modules[DoorFrame]), "TO SHIP INTERIOR");
            try
            {
                foreach (Piece piece in modules[DoorFrame]) { AddShipPart(root.transform, Vector3.zero, piece); }
                foreach (Piece piece in modules[DoorLeaf]) { AddShipPart(root.transform, leafOffset, piece); }
                var arrive = new GameObject(ShipHatchArrive).transform;
                arrive.SetParent(root.transform, false);
                arrive.localPosition = Vector3.forward * ArriveDistance + Vector3.up * ArriveLift;
                PrefabUtility.SaveAsPrefabAsset(root, $"Assets/ACO_Addon/Prefabs/{name}.prefab");
            }
            finally { ACO_PrefabBuilder.DestroyAfterSetup(root); }
        }
    }

    private static void AddShipPart(Transform parent, Vector3 position, Piece piece)
    {
        var go = new GameObject(piece.Material);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = ShipMaterial(piece.Material);
    }

    // ───────────── 읽기 ─────────────

    /// 모듈 이름 → 재질별 메시.
    private static Dictionary<string, List<Piece>> ReadModules(GameObject model)
    {
        if (model == null) { throw new InvalidOperationException($"[ACO] {ModelFbx}가 없습니다. {Tool}을 실행하세요."); }
        var modules = new Dictionary<string, List<Piece>>();
        foreach (Transform part in model.transform)
        {
            int split = part.name.LastIndexOf("__", StringComparison.Ordinal);
            MeshFilter filter = part.GetComponent<MeshFilter>();
            if (split <= 0 || filter == null || filter.sharedMesh == null) { continue; }
            string name = part.name.Substring(0, split);
            if (!modules.TryGetValue(name, out List<Piece> pieces))
            {
                pieces = new List<Piece>();
                modules[name] = pieces;
            }
            pieces.Add(new Piece { Material = part.name.Substring(split + 2), Mesh = filter.sharedMesh });
        }
        return modules;
    }

    /// 줄 형식: "격실 모듈 x y z Y회전". 격실은 파일에 나온 순서를 지킨다.
    private static List<KeyValuePair<string, List<Placement>>> ReadLayout(Dictionary<string, List<Piece>> modules)
    {
        if (!File.Exists(LayoutPath)) { throw new InvalidOperationException($"[ACO] {LayoutPath}가 없습니다. {Tool}을 실행하세요."); }
        var rooms = new List<KeyValuePair<string, List<Placement>>>();
        foreach (string line in File.ReadAllLines(LayoutPath))
        {
            string[] parts = line.Split(' ');
            if (parts.Length != 6) { continue; }
            if (!modules.ContainsKey(parts[1])) { throw new InvalidOperationException($"[ACO] {ModelFbx}에 모듈 {parts[1]}이 없습니다. {Tool}을 다시 실행하세요."); }
            if (rooms.Count == 0 || rooms[rooms.Count - 1].Key != parts[0])
            {
                rooms.Add(new KeyValuePair<string, List<Placement>>(parts[0], new List<Placement>()));
            }
            rooms[rooms.Count - 1].Value.Add(new Placement
            {
                Module = parts[1],
                Position = new Vector3(Number(parts[2]), Number(parts[3]), Number(parts[4])),
                RotY = Number(parts[5]),
            });
        }
        if (rooms.Count == 0) { throw new InvalidOperationException($"[ACO] {LayoutPath}가 비어 있습니다."); }
        return rooms;
    }

    /// 줄 형식: "격실 최소 x y z 최대 x y z". 한 격실에 여러 줄일 수 있다.
    private static Dictionary<string, List<Bounds>> ReadRooms()
    {
        if (!File.Exists(RoomsPath)) { throw new InvalidOperationException($"[ACO] {RoomsPath}가 없습니다. {Tool}을 실행하세요."); }
        var result = new Dictionary<string, List<Bounds>>();
        foreach (string line in File.ReadAllLines(RoomsPath))
        {
            string[] parts = line.Split(' ');
            if (parts.Length != 7) { continue; }
            var box = new Bounds();
            box.SetMinMax(new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])), new Vector3(Number(parts[4]), Number(parts[5]), Number(parts[6])));
            if (!result.TryGetValue(parts[0], out List<Bounds> list))
            {
                list = new List<Bounds>();
                result[parts[0]] = list;
            }
            list.Add(box);
        }
        return result;
    }

    private static float Number(string text) { return float.Parse(text, CultureInfo.InvariantCulture); }

    /// 벽판 1.4m가 x 0~1.4, 높이 1.4, 앞면 +z이면 축과 크기가 맞다. 어긋나면 프리팹을 건드리기 전에 멈춘다.
    private static void CheckModelAxes(Dictionary<string, List<Piece>> modules)
    {
        if (!modules.TryGetValue("Int_Panel_140", out List<Piece> panel)) { throw new InvalidOperationException($"[ACO] {ModelFbx}에 Int_Panel_140이 없습니다."); }
        Bounds b = BoundsOf(panel);
        bool ok = Mathf.Abs(b.size.x - 1.4f) < 0.01f && Mathf.Abs(b.size.y - 1.4f) < 0.01f && b.size.z < 0.05f && b.min.x > -0.01f && b.min.y > -0.01f && b.min.z > -0.01f;
        if (!ok) { throw new InvalidOperationException($"[ACO] {ModelFbx}의 축이나 크기가 예상과 다릅니다(벽판 범위 {b})."); }
    }

    private static Bounds BoundsOf(List<Piece> pieces)
    {
        Bounds b = pieces[0].Mesh.bounds;
        foreach (Piece piece in pieces) { b.Encapsulate(piece.Mesh.bounds); }
        return b;
    }

    private static bool IsClockPart(string module) { return module == ClockBody || Array.IndexOf(ClockHands, module) >= 0; }

    // ───────────── 메시 ─────────────

    private static Mesh Combine(string name, List<CombineInstance> parts, ref bool created, bool lightmapUv = false)
    {
        // 형식은 합치기 전에 정한다. 데이터가 든 메시의 형식을 바꾸면 인덱스가 사라진다
        int vertices = 0;
        foreach (CombineInstance part in parts) { vertices += part.mesh.vertexCount; }
        var mesh = new Mesh { name = name, indexFormat = vertices < 65000 ? IndexFormat.UInt16 : IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        mesh.RecalculateBounds();
        if (lightmapUv)
        {
            UnwrapParam.SetDefaults(out UnwrapParam unwrap);
            unwrap.hardAngle = LightmapHardAngle;
            unwrap.packMargin = LightmapPackMargin;
            Unwrapping.GenerateSecondaryUVSet(mesh, unwrap);
        }
        if (created) { AssetDatabase.AddObjectToAsset(mesh, MeshAsset); }
        else
        {
            AssetDatabase.CreateAsset(mesh, MeshAsset);
            created = true;
        }
        return mesh;
    }

    private static void BuildRenderers(Transform room, string roomName, List<Placement> placements, Dictionary<string, List<Piece>> modules, ref bool created)
    {
        // 재질과 라이트맵 여부가 같은 것끼리 합친다. 이름 끝의 "#"이 라이트맵을 받는 묶음이다
        var groups = new SortedDictionary<string, List<CombineInstance>>(StringComparer.Ordinal);
        foreach (Placement place in placements)
        {
            if (IsClockPart(place.Module) || place.Module == TvScreen) { continue; }
            foreach (Piece piece in modules[place.Module])
            {
                string key = IsLightmapped(piece.Material) ? piece.Material + "#" : piece.Material;
                if (place.Module == Fixture) { key = NoShadowMark + key; }
                if (IsSurface(place.Module, piece.Material)) { key = SurfaceMark + key; }
                if (!groups.TryGetValue(key, out List<CombineInstance> list))
                {
                    list = new List<CombineInstance>();
                    groups[key] = list;
                }
                list.Add(new CombineInstance { mesh = piece.Mesh, transform = place.Matrix });
            }
        }
        foreach (KeyValuePair<string, List<CombineInstance>> entry in groups)
        {
            bool lightmapped = entry.Key.EndsWith("#", StringComparison.Ordinal);
            bool noShadow = entry.Key[0] == NoShadowMark;
            bool surface = entry.Key[0] == SurfaceMark;
            string material = entry.Key.TrimEnd('#').TrimStart(NoShadowMark, SurfaceMark);
            string objectName = noShadow ? "Fixture_" + material : surface ? "Surface_" + material : lightmapped ? material + "_Lightmap" : material;
            var go = new GameObject(objectName);
            go.transform.SetParent(room, false);
            go.AddComponent<MeshFilter>().sharedMesh = Combine($"{roomName}_{go.name}", entry.Value, ref created, lightmapped);
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = surface ? SurfaceMaterial(material) : InteriorMaterial(material);
            if (noShadow) { r.shadowCastingMode = ShadowCastingMode.Off; }
            if (material == "glass")
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
                continue;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            r.receiveGI = lightmapped ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
            if (lightmapped)
            {
                var settings = new SerializedObject(r);
                settings.FindProperty("m_ScaleInLightmap").floatValue = LightmapScale;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static bool IsLightmapped(string material) { return Array.IndexOf(LightmapMaterials, material) >= 0; }

    private static bool IsSurface(string module, string material)
    {
        return material == "deck" || module.EndsWith("_Ceil", StringComparison.Ordinal);
    }

    // ───────────── 조명과 프로브 ─────────────

    /// 형광등마다 구운 점광원 하나. 실행 중 비용이 없다.
    private static void BuildLights(Transform room, string roomName, List<Placement> placements)
    {
        RoomLight setting = RoomLights[roomName];
        var parent = new GameObject("Lights").transform;
        parent.SetParent(room, false);
        int index = 0;
        foreach (Placement place in placements)
        {
            if (place.Module != "Int_Fixture") { continue; }
            var go = new GameObject($"Fixture{index++}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = place.Position + Vector3.down * LightDrop;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.color = setting.Color;
            l.intensity = setting.Intensity;
            l.range = LightRange;
            l.shadows = LightShadows.Soft;
            l.shadowRadius = LightSoftness;
        }
    }

    private static void BuildProbes(Transform room, List<Bounds> boxes, List<Placement> placements, Dictionary<string, List<Piece>> modules)
    {
        Bounds all = boxes[0];
        foreach (Bounds box in boxes) { all.Encapsulate(box); }

        var reflection = new GameObject("Reflection");
        reflection.transform.SetParent(room, false);
        reflection.transform.localPosition = new Vector3(boxes[0].center.x, 1.4f, boxes[0].center.z);
        ReflectionProbe probe = reflection.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Baked;
        probe.boxProjection = true;
        probe.hdr = true;
        probe.resolution = 256;
        probe.size = all.size;
        probe.center = all.center - reflection.transform.localPosition;
        probe.blendDistance = 0.3f;
        probe.nearClipPlane = 0.05f;
        probe.farClipPlane = 30f;

        var points = new List<Vector3>();
        foreach (Bounds box in boxes)
        {
            foreach (float y in ProbeHeights)
            {
                if (y > box.max.y - 0.2f) { continue; }
                for (float x = box.min.x + ProbeMargin; x <= box.max.x - ProbeMargin + 0.01f; x += ProbeStep)
                {
                    for (float z = box.min.z + ProbeMargin; z <= box.max.z - ProbeMargin + 0.01f; z += ProbeStep)
                    {
                        var q = new Vector3(x, y, z);
                        if (!InsideFurniture(q, placements, modules)) { points.Add(q); }
                    }
                }
            }
        }
        var group = new GameObject("LightProbes");
        group.transform.SetParent(room, false);
        group.AddComponent<LightProbeGroup>().probePositions = points.ToArray();
    }

    /// 가구 속 프로브는 까맣게 구워져 둘레를 어둡게 만든다.
    private static bool InsideFurniture(Vector3 q, List<Placement> placements, Dictionary<string, List<Piece>> modules)
    {
        foreach (Placement place in placements)
        {
            if (!IsSolid(place.Module) && place.Module != "Int_PlotTable") { continue; }
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, place.RotY, 0f)) * (q - place.Position);
            if (BoundsOf(modules[place.Module]).Contains(local)) { return true; }
        }
        return false;
    }

    private static bool IsWallPart(string module, string material)
    {
        if (module.EndsWith("_Shell", StringComparison.Ordinal) || module.EndsWith("_Ceil", StringComparison.Ordinal)) { return true; }
        for (int i = 0; i < WallParts.GetLength(0); i++)
        {
            if (!module.StartsWith(WallParts[i, 0], StringComparison.Ordinal)) { continue; }
            if (WallParts[i, 1].Length == 0 || WallParts[i, 1] == material) { return true; }
        }
        return false;
    }

    private static void BuildWallCollider(Transform room, string roomName, List<Placement> placements, Dictionary<string, List<Piece>> modules, ref bool created)
    {
        var parts = new List<CombineInstance>();
        foreach (Placement place in placements)
        {
            foreach (Piece piece in modules[place.Module])
            {
                if (IsWallPart(place.Module, piece.Material)) { parts.Add(new CombineInstance { mesh = piece.Mesh, transform = place.Matrix }); }
            }
        }
        var go = new GameObject("Col_Walls");
        go.transform.SetParent(room, false);
        go.AddComponent<MeshCollider>().sharedMesh = Combine($"{roomName}_Collider", parts, ref created);
    }

    // ───────────── 상자 콜라이더 ─────────────

    private static bool IsSolid(string module)
    {
        foreach (string head in SolidParts)
        {
            if (head.EndsWith("_", StringComparison.Ordinal) ? module.StartsWith(head, StringComparison.Ordinal) : module == head) { return true; }
        }
        return false;
    }

    private static void BuildBoxes(Transform room, List<Placement> placements, Dictionary<string, List<Piece>> modules)
    {
        var parent = new GameObject(FurnitureName).transform;
        parent.SetParent(room, false);
        int index = 0;
        foreach (Placement place in placements)
        {
            List<Piece> pieces = modules[place.Module];
            if (IsSolid(place.Module))
            {
                Bounds b = BoundsOf(pieces);
                if (place.Module == SpectatorDesk) { b.SetMinMax(b.min, new Vector3(b.max.x, b.max.y, SpectatorDeskDepth)); }
                AddBox(parent, ref index, place, b.center, b.size, 0f);
            }
            else if (place.Module == "Int_PlotTable")
            {
                // 둥근 테이블: 상자 둘을 45도 어긋나게 겹쳐 팔각형으로 만든다
                Bounds b = BoundsOf(pieces);
                AddBox(parent, ref index, place, b.center, b.size, 0f);
                AddBox(parent, ref index, place, b.center, b.size, 45f);
            }
            else if (place.Module.StartsWith("Int_HatchFrame", StringComparison.Ordinal))
            {
                // 문턱: 격실 바닥 사이 틈을 잇는다
                Bounds b = BoundsOf(pieces);
                float top = 0.02f;
                AddBox(parent, ref index, place, new Vector3(0f, top - 0.05f, b.center.z), new Vector3(1.2f, 0.1f, b.size.z), 0f);
            }
            else if (place.Module.StartsWith("Int_HatchLeaf", StringComparison.Ordinal) && IsClosed(place.RotY))
            {
                Bounds b = BoundsOf(pieces);
                AddBox(parent, ref index, place, b.center, b.size, 0f);
            }
        }
    }

    /// 닫힌 문짝은 직각으로 놓이고, 열어 고정한 문짝은 168도쯤 돌아가 있다.
    private static bool IsClosed(float rotY) { return Mathf.Abs(Mathf.DeltaAngle(rotY, Mathf.Round(rotY / 90f) * 90f)) < 1f; }

    private static void AddBox(Transform parent, ref int index, Placement place, Vector3 center, Vector3 size, float extraYaw)
    {
        var go = new GameObject($"Col{index++}_{place.Module}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = place.Position;
        go.transform.localRotation = Quaternion.Euler(0f, place.RotY + extraYaw, 0f);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    // ───────────── 시계 ─────────────

    /// U# 스크립트는 프로그램 에셋이 있어야 컴포넌트로 붙는다. 처음 한 번 만들고 컴파일한다.
    private static void EnsureClockProgram()
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ClockProgram) != null) { return; }
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ClockScript);
        if (script == null) { throw new InvalidOperationException($"[ACO] {ClockScript}가 없습니다."); }
        var program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, ClockProgram);
        AssetDatabase.SaveAssets();
        UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
    }

    /// 모델에서 바깥쪽 면을 지웠으므로 벽이 햇빛을 막지 못한다. 격실을 덮는 그림자 전용 상자를 둔다.
    private static void BuildSunBlock(Transform room)
    {
        var bounds = new Bounds();
        bool first = true;
        foreach (MeshFilter filter in room.GetComponentsInChildren<MeshFilter>())
        {
            Bounds b = filter.sharedMesh.bounds;
            b.center += room.InverseTransformPoint(filter.transform.position);
            if (first) { bounds = b; }
            else { bounds.Encapsulate(b); }
            first = false;
        }
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "SunBlock";
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(room, false);
        go.transform.localPosition = bounds.center;
        go.transform.localScale = bounds.size + Vector3.one * 0.1f;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        r.sharedMaterial = InteriorMaterial("paint");
    }

    private static void BuildClocks(Transform room, List<Placement> placements, Dictionary<string, List<Piece>> modules)
    {
        int index = 0;
        foreach (Placement place in placements)
        {
            if (place.Module != ClockBody) { continue; }
            var go = new GameObject($"Clock{index++}");
            go.transform.SetParent(room, false);
            go.transform.localPosition = place.Position;
            go.transform.localRotation = Quaternion.Euler(0f, place.RotY, 0f);
            foreach (Piece piece in modules[ClockBody]) { AddPart(go.transform, "Body_" + piece.Material, piece, true); }
            Transform[] hands = new Transform[ClockHands.Length];
            for (int i = 0; i < hands.Length; i++)
            {
                var hand = new GameObject(ClockHands[i].Substring("Int_Clock".Length)).transform;
                hand.SetParent(go.transform, false);
                foreach (Piece piece in modules[ClockHands[i]]) { AddPart(hand, piece.Material, piece, false); }
                hands[i] = hand;
            }
            ACO_WallClock clock = go.AddUdonSharpComponent<ACO_WallClock>();
            clock.HourHand = hands[0];
            clock.MinuteHand = hands[1];
            clock.SecondHand = hands[2];
        }
    }

    /// TV 화면은 합치지 않는다. 영상 플레이어가 이 렌더러의 _MainTex에 영상을 넣는다. 꺼져 있으면 검고, 영상은 조명과 상관없이 밝다.
    private static void BuildTvScreen(Transform room, List<Placement> placements, Dictionary<string, List<Piece>> modules)
    {
        foreach (Placement place in placements)
        {
            if (place.Module != TvScreen) { continue; }
            var go = new GameObject("TvScreen");
            go.transform.SetParent(room, false);
            go.transform.localPosition = place.Position;
            go.transform.localRotation = Quaternion.Euler(0f, place.RotY, 0f);
            Piece piece = modules[TvScreen][0];
            go.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = InteriorMaterial(piece.Material);
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    private static void AddPart(Transform parent, string name, Piece piece, bool isStatic)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = InteriorMaterial(piece.Material);
        if (isStatic) { GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic); }
    }

    // ───────────── 머티리얼 ─────────────

    private static Material InteriorMaterial(string name)
    {
        string path = $"{MeshDir}/ACO_IntMat_{name}.mat";
        Shader shader = Shader.Find(name == "glass" ? "Standard" : InteriorShader);
        if (shader == null) { throw new InvalidOperationException($"[ACO] 셰이더 {InteriorShader}를 찾지 못했습니다. 컴파일이 끝난 뒤 다시 실행하세요."); }
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader) { name = "ACO_IntMat_" + name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        bool photo = name.StartsWith("photo", StringComparison.Ordinal);
        string textureName = SharedTexture.TryGetValue(name, out string shared) ? shared : name;
        string texture = photo ? $"{TextureDir}/Photos/ACO_Photo_{name.Substring(5)}.png" : $"{TextureDir}/ACO_Int_{textureName}.png";
        if (Untextured.TryGetValue(name, out Color plain)) { m.color = plain; }
        else { m.SetTexture("_MainTex", InteriorTexture(texture, false)); }
        string normal = $"{TextureDir}/ACO_Int_{name}_n.png";
        if (!photo && File.Exists(normal))
        {
            m.SetTexture("_BumpMap", InteriorTexture(normal, true));
            m.EnableKeyword("_NORMALMAP");
        }
        Vector2 surface = Surfaces.TryGetValue(name, out Vector2 known) ? known : new Vector2(0.6f, 0f);
        m.SetFloat("_Glossiness", 1f - surface.x);
        m.SetFloat("_Metallic", surface.y);
        if (name == "glass") { MakeTransparent(m); }
        else { m.SetColor("_EmissionColor", Color.white * (Emission.TryGetValue(name, out float glow) ? glow : 0f)); }
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 함내 재질과 같은 텍스처의 Standard 재질. ACO/Interior는 태양을 받지 않아 바깥에서는 쓸 수 없다.
    private static Material ShipMaterial(string name)
    {
        string path = $"{MeshDir}/ACO_ShipMat_{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_ShipMat_" + name };
            AssetDatabase.CreateAsset(m, path);
        }
        Material interior = InteriorMaterial(name);
        m.CopyPropertiesFromMaterial(interior);
        if (interior.IsKeywordEnabled("_NORMALMAP")) { m.EnableKeyword("_NORMALMAP"); }
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 바닥과 천장 재질: 일반 재질을 복사해 Blender로 구운 빛 텍스처를 켠다.
    private static Material SurfaceMaterial(string name)
    {
        string path = $"{MeshDir}/ACO_IntMat_{name}_Surface.mat";
        Material source = InteriorMaterial(name);
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(source) { name = "ACO_IntMat_" + name + "_Surface" };
            m.SetFloat("_BakedLightScale", BakedLightScale);
            AssetDatabase.CreateAsset(m, path);
        }
        m.CopyPropertiesFromMaterial(source);
        m.SetTexture("_BakedLight", BakedLightTexture());
        m.EnableKeyword("_BAKEDLIGHT");
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Texture2D BakedLightTexture()
    {
        var importer = AssetImporter.GetAtPath(BakedLightPath) as TextureImporter;
        if (importer == null) { throw new InvalidOperationException($"[ACO] {BakedLightPath}가 없습니다. Tools/aco_interior_bake.py를 실행하세요."); }
        if (importer.wrapMode != TextureWrapMode.Clamp || importer.sRGBTexture)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(BakedLightPath);
    }

    /// Standard 셰이더의 Transparent 모드. 인스펙터가 하는 설정을 그대로 넣는다.
    private static void MakeTransparent(Material m)
    {
        Color c = m.color;
        c.a = GlassAlpha;
        m.color = c;
        m.SetFloat("_Mode", 3f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.One);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    private static Texture2D InteriorTexture(string path, bool normal)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) { throw new InvalidOperationException($"[ACO] {path}가 없습니다. {Tool}을 실행하세요."); }
        TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != type || importer.wrapMode != TextureWrapMode.Repeat || importer.anisoLevel != 8)
        {
            importer.textureType = type;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Prefabs")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Prefabs"); }
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
        if (!AssetDatabase.IsValidFolder(MeshDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "Interior"); }
    }
}
