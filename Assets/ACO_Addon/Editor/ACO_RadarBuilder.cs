using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using SaccFlightAndVehicles;
using ACO;

/// 레이더 두 가지를 만든다(DESIGN.md 6-30).
///  - HUD 레이더: 표적 마커 아래 'RADAR'(ACO_RadarContacts + ACO_HudRadar) → Scope(배경 원판·고리) → Spin(점 메시). Bake가 없으면 만든다(EnsureHudRadar).
///  - 조종석 지도: 메뉴 ACO → Player Vehicle → Add Cockpit Map to Selected Vehicles — 기체 아래 'ACO_CockpitMap'(조종석 앞, 위치는 직접 맞춘다).
/// 메시·머티리얼은 Generated(ACO_Radar*). 셰이더는 ACO/RadarVertexColor(정점 색).
public static class ACO_RadarBuilder
{
    public const string TacticalNorthName = "NorthMark";
    private const float TacticalNorthLift = 0.01f;

    private const string GeneratedDir = "Assets/ACO_Addon/Generated";
    private const string ObjectName = "ACO_CockpitMap";
    public const string PlanMapName = "ACO_PlanMap";

    // ───────────── HUD 레이더 ─────────────

    /// 표적 마커 아래에 HUD 레이더가 없으면 만든다. 만들었으면 true.
    public static bool EnsureHudRadar(ACO_TargetMarkers markers)
    {
        ACO_HudRadar existing = markers.GetComponentInChildren<ACO_HudRadar>(true);
        if (existing != null)
        {
            // 예전에 만든 레이더: 범위(표시 거리 — 최대를 넘거나 비었을 때만 기본 5km로, 6-239)·불투명도용 렌더러 목록
            bool changed = false;
            if (existing.Range <= 0f || existing.Range > existing.MaxRange) { existing.Range = 5000f; changed = true; }
            if ((existing.Renderers == null || existing.Renderers.Length == 0) && existing.Root != null)
            {
                existing.Renderers = existing.Root.GetComponentsInChildren<MeshRenderer>(true);
                changed = true;
            }
            if (changed) { PrefabUtility.RecordPrefabInstancePropertyModifications(existing); EditorUtility.SetDirty(existing); }
            return false;
        }
        Shader shader = Shader.Find("ACO/RadarVertexColor");
        if (shader == null) { Debug.LogWarning("[ACO] ACO/RadarVertexColor 셰이더가 없어 HUD 레이더를 만들지 못했습니다."); return false; }
        EnsureGenerated();

        var go = new GameObject("RADAR");
        go.transform.SetParent(markers.transform, false);
        go.AddUdonSharpComponent<ACO_RadarContacts>();
        ACO_HudRadar radar = go.AddUdonSharpComponent<ACO_HudRadar>();
        radar.Contacts = go.GetComponent<ACO_RadarContacts>();

        var scope = new GameObject("Scope");
        scope.transform.SetParent(go.transform, false);
        // 항상 맨 위(ZTest Always) — 바탕 → 고리 → 점 순서는 렌더 큐
        MakeRenderer(scope.transform, "Background", LoadOrBuildMesh("ACO_RadarDisc", () => BuildDisc(48, 0.5f)),
            LoadOrSaveMaterial(shader, new Color(0f, 0.07f, 0.03f, 0.55f), "ACO_RadarHudBack", 4005, CompareFunction.Always));
        MakeRenderer(scope.transform, "Grid", LoadOrBuildMesh("ACO_RadarHudGrid", BuildHudGrid),
            LoadOrSaveMaterial(shader, new Color(0f, 1f, 0f, 0.8f), "ACO_RadarHudLine", 4006, CompareFunction.Always));
        var spin = new GameObject("Spin");
        spin.transform.SetParent(scope.transform, false);
        MeshRenderer blips = MakeRenderer(spin.transform, "Blips", null,
            LoadOrSaveMaterial(shader, Color.white, "ACO_RadarHudBlips", 4008, CompareFunction.Always));
        scope.SetActive(false);

        radar.Root = scope.transform;
        radar.Spin = spin.transform;
        radar.BlipFilter = blips.GetComponent<MeshFilter>();
        radar.Renderers = scope.GetComponentsInChildren<MeshRenderer>(true);
        ACO_SyncModeFixer.FixHierarchy(go, false);
        EditorUtility.SetDirty(radar);
        return true;
    }

    /// 바깥 고리 + 절반 거리 고리 + 십자선.
    private static Mesh BuildHudGrid()
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        AddRing(v, t, 64, 0.5f, 0.012f);
        AddRing(v, t, 48, 0.25f, 0.006f);
        AddBar(v, t, new Vector3(-0.47f, -0.003f, 0f), new Vector3(0.47f, 0.003f, 0f));
        AddBar(v, t, new Vector3(-0.003f, -0.47f, 0f), new Vector3(0.003f, 0.47f, 0f));
        return Finish(v, t);
    }

    // ───────────── 조종석 지도 ─────────────

    [MenuItem("ACO/Player Vehicle/Add Cockpit Map to Selected Vehicles")]
    public static void AddCockpitMapToSelected()
    {
        var entities = new List<SaccEntity>();
        foreach (GameObject go in Selection.gameObjects)
        {
            SaccEntity e = go.GetComponentInParent<SaccEntity>(true);
            if (e == null) { e = go.GetComponentInChildren<SaccEntity>(true); }
            if (e != null && !entities.Contains(e)) { entities.Add(e); }
        }
        if (entities.Count == 0)
        {
            EditorUtility.DisplayDialog("ACO", "기체(SaccEntity)를 선택하세요.", "확인");
            return;
        }
        var log = new System.Text.StringBuilder();
        foreach (SaccEntity e in entities)
        {
            if (e.GetComponentInChildren<ACO_CockpitMap>(true) != null) { log.Append($"\n  {e.name}: 이미 있음(그대로)"); continue; }
            ACO_CockpitMap map = CreateCockpitMap(e);
            log.Append(map != null ? $"\n  {e.name}: 만듦 — 조종석 앞 위치·크기를 직접 맞추세요" : $"\n  {e.name}: 실패(셰이더 없음)");
        }
        Debug.Log("[ACO] 조종석 지도:" + log + "\n  Bake를 다시 하면 레이더 접촉 목록이 연결됩니다.");
    }

    /// 기체 아래 조종석 지도. 위치는 조종석 의자(IsPilotSeat) 앞 0.65m·위 0.5m(대충 — 계기판 자리로 옮긴다). 화면 한 변 0.18m, 조종사 쪽으로 20도 기울임.
    public static ACO_CockpitMap CreateCockpitMap(SaccEntity e)
    {
        Shader shader = Shader.Find("ACO/RadarVertexColor");
        if (shader == null) { return null; }
        EnsureGenerated();

        var go = new GameObject(ObjectName);
        Undo.RegisterCreatedObjectUndo(go, "ACO cockpit map");
        go.transform.SetParent(e.transform, false);
        Transform seat = null;
        foreach (SaccVehicleSeat s in e.GetComponentsInChildren<SaccVehicleSeat>(true)) { if (s.IsPilotSeat) { seat = s.transform; break; } }
        Vector3 basePos = seat != null ? e.transform.InverseTransformPoint(seat.position) : new Vector3(0f, 1f, 0f);
        go.transform.localPosition = basePos + new Vector3(0f, 0.5f, 0.65f);
        go.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        go.transform.localScale = Vector3.one * 0.18f;

        ACO_CockpitMap map = UdonSharpUndo.AddComponent<ACO_CockpitMap>(go);
        map.EntityControl = e;
        BuildMapScreen(go.transform, map, shader);
        ACO_SyncModeFixer.FixHierarchy(go, true);
        EditorUtility.SetDirty(map);
        return map;
    }

    /// 브리핑실 벽걸이 평면 지도 프리팹. 앞면이 루트 +Z이고, 화면 크기는 1 x 1이다.
    public static GameObject CreatePlanMapPrefab()
    {
        Shader shader = Shader.Find("ACO/RadarVertexColor");
        if (shader == null) { return null; }
        EnsureGenerated();
        var root = new GameObject(PlanMapName);
        ACO_CockpitMap map = root.AddUdonSharpComponent<ACO_CockpitMap>();
        map.WallMode = true;
        map.BlipSize = 0.022f;
        var face = new GameObject("Face");
        face.transform.SetParent(root.transform, false);
        face.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        BuildMapScreen(face.transform, map, shader);
        BuildPlanGrid(map, shader);
        ACO_SyncModeFixer.FixHierarchy(root, false);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"Assets/ACO_Addon/Prefabs/{PlanMapName}.prefab");
        ACO_PrefabBuilder.DestroyAfterSetup(root);
        ACO_PrefabVersions.Stamp(PlanMapName);
        return prefab;
    }

    // ───────────── 좌표 격자 (6-301) ─────────────

    public const string GridName = "Grid";
    public const string GridLabelsName = "GridLabels";
    private const string GridFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private static readonly Color GridLabelColor = new Color(0.8f, 0.92f, 1f, 0.9f);

    /// 평면 지도 격자: 선(지형 위 · 공역 고리 아래, 렌더 큐 3003)과 이름표 20개(열 10 · 행 10 — 스크립트가 글자 · 자리를 정한다).
    /// 이름표 머티리얼은 글꼴 머티리얼 사본(렌더 큐 3005 — 바다 3001보다 뒤에 그려야 보인다).
    private static void BuildPlanGrid(ACO_CockpitMap map, Shader shader)
    {
        Transform screen = map.Screen.transform;
        MeshRenderer grid = MakeRenderer(screen, GridName, null,
            LoadOrSaveMaterial(shader, new Color(0.8f, 0.9f, 1f, 0.35f), "ACO_RadarMapGrid", 3003, CompareFunction.LessEqual));
        map.GridFilter = grid.GetComponent<MeshFilter>();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GridFontPath);
        Material labelMat = null;
        if (font != null)
        {
            string path = $"{GeneratedDir}/ACO_RadarMapGridLabel.mat";
            labelMat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (labelMat == null)
            {
                labelMat = new Material(font.material) { name = "ACO_RadarMapGridLabel" };
                labelMat.renderQueue = 3005;
                AssetDatabase.CreateAsset(labelMat, path);
            }
        }
        // TextMeshPro는 꺼진 오브젝트 아래에서 만들면 Awake 전이라 글꼴을 넣을 때 멈춘다 — 화면을 잠깐 켠다
        bool wasActive = screen.gameObject.activeSelf;
        screen.gameObject.SetActive(true);
        map.GridLabels = MakeGridLabels(screen, 20, 0.25f, font, labelMat, false);
        screen.gameObject.SetActive(wasActive);
    }

    /// 작전 테이블 격자 렌더 큐(6-317): 2600 → 2440. 지형 · 바다 · 마커(2000) 뒤, CIC 도구 패널 금속(2449 — Standard 불투명이 허용하는 끝) 앞.
    /// 패널을 격자보다 뒤에 그려야 깊이 검사 없는 격자가 패널을 뚫고 보이지 않는다(Standard 불투명은 2449를 넘기면 머티리얼을 볼 때 2000으로 되돌린다).
    public const int TableGridQueue = 2440;

    /// 프리팹 수치 cic-panel-over-grid-2: 이미 만든 ACO_MapGrid 머티리얼 큐를 TableGridQueue로.
    public static void TuneTableGridQueue()
    {
        Material grid = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedDir}/ACO_MapGrid.mat");
        if (grid == null || grid.renderQueue == TableGridQueue) { return; }
        grid.renderQueue = TableGridQueue;
        EditorUtility.SetDirty(grid);
        AssetDatabase.SaveAssets();
    }

    /// 작전 테이블 격자 깊이 판정(6-318, 사용자 "그냥 격자만 깔고 지형 위에는 깔지 말자"): Always → LessEqual. 바다 위에만 보이고 섬(지형) · 벽 · 사람 · 손에 가려진다
    /// (Always였을 땐 옆방 벽 너머 · 아바타 위로 비쳤다). 선 높이 TableGridLift(0.5mm) — 바다 판(−2mm)보다 위, 가장 낮은 땅 윗면(1mm)보다 아래.
    public const CompareFunction TableGridZTest = CompareFunction.LessEqual;
    public const float TableGridLift = 0.0005f;

    /// 프리팹 수치 table-grid-sea-only-1: 이미 만든 ACO_MapGrid 머티리얼 깊이 판정 → LessEqual, 씬 작전 테이블 격자 선 높이 1mm → 0.5mm.
    public static void TuneTableGridSeaOnly()
    {
        Material grid = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedDir}/ACO_MapGrid.mat");
        if (grid != null && Mathf.RoundToInt(grid.GetFloat("_ZTest")) != (int)TableGridZTest)
        {
            grid.SetFloat("_ZTest", (float)TableGridZTest);
            EditorUtility.SetDirty(grid);
            AssetDatabase.SaveAssets();
        }
        foreach (ACO_TacticalMap map in Object.FindObjectsOfType<ACO_TacticalMap>(true))
        {
            if (map.GridFilter == null || map.GridFilter.transform.parent != map.MapRoot) { continue; }
            Transform t = map.GridFilter.transform;
            if (Mathf.Abs(t.localPosition.y - TableGridLift) < 0.00001f) { continue; }
            Undo.RecordObject(t, "ACO tactical grid");
            t.localPosition = Vector3.up * TableGridLift;
            EditorUtility.SetDirty(t);
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    /// 작전 테이블 격자(6-301): MapRoot 아래 선(해수면 + 0.5mm, 깊이 판정 LessEqual — 바다 위에만, 지형 · 벽에 가려진다(6-318), 렌더 큐 TableGridQueue)과
    /// 이름표 40개(북 · 남 열, 서 · 동 행 — 스크립트가 자리 · 글자 · 방향을 정한다). 이미 있으면 그대로. 만들었으면 true.
    public static bool EnsureTacticalGrid(ACO_TacticalMap map)
    {
        if (map == null || map.MapRoot == null) { return false; }
        if (map.GridFilter != null && map.GridLabels != null && map.GridLabels.Length > 0) { return false; }
        Shader shader = Shader.Find("ACO/RadarVertexColor");
        if (shader == null) { return false; }
        EnsureGenerated();
        Undo.RecordObject(map, "ACO tactical grid");
        Transform old = map.MapRoot.Find(GridName);
        if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); }
        Transform oldLabels = map.MapRoot.Find(GridLabelsName);
        if (oldLabels != null) { Undo.DestroyObjectImmediate(oldLabels.gameObject); }
        MeshRenderer grid = MakeRenderer(map.MapRoot, GridName, null,
            LoadOrSaveMaterial(shader, new Color(0.85f, 0.95f, 1f, 0.35f), "ACO_MapGrid", TableGridQueue, TableGridZTest));
        grid.transform.localPosition = Vector3.up * TableGridLift;
        Undo.RegisterCreatedObjectUndo(grid.gameObject, "ACO tactical grid");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GridFontPath);
        map.GridFilter = grid.GetComponent<MeshFilter>();
        map.GridLabels = MakeGridLabels(map.MapRoot, 40, 0.4f, font, null, true);
        if (map.GridLabels.Length > 0) { Undo.RegisterCreatedObjectUndo(map.GridLabels[0].parent.gameObject, "ACO tactical grid"); }
        EditorUtility.SetDirty(map);
        return true;
    }

    private static Transform[] MakeGridLabels(Transform parent, int count, float fontSize, TMP_FontAsset font, Material mat, bool table)
    {
        var holder = new GameObject(GridLabelsName);
        holder.transform.SetParent(parent, false);
        var labels = new Transform[count];
        float scale = fontSize / 0.25f;
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("L" + i, typeof(RectTransform));
            go.transform.SetParent(holder.transform, false);
            TextMeshPro t = go.AddComponent<TextMeshPro>();
            if (font != null) { t.font = font; }
            if (mat != null) { t.fontSharedMaterial = mat; }
            t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.color = GridLabelColor;
            t.rectTransform.sizeDelta = new Vector2(0.08f, 0.04f) * scale;
            t.text = "";
            labels[i] = go.transform;
        }
        return labels;
    }

    /// 지도 화면(바다, 지형, 공역 고리, 점)을 만들어 지도에 잇는다.
    private static void BuildMapScreen(Transform go, ACO_CockpitMap map, Shader shader)
    {
        // 깊이 검사(조종석에 가려진다) — 테두리 → 바다 → 지형 → 공역 고리 → 점 순서는 렌더 큐
        Mesh quad = LoadOrBuildMesh("ACO_RadarQuad", () => BuildQuad(0.5f));
        MakeRenderer(go, "Bezel", LoadOrBuildMesh("ACO_RadarBezel", () => BuildQuad(0.54f)),
            LoadOrSaveMaterial(shader, new Color(0.02f, 0.02f, 0.025f, 1f), "ACO_RadarMapBezel", 3000, CompareFunction.LessEqual));
        var screen = new GameObject("Screen");
        screen.transform.SetParent(go, false);
        MakeRenderer(screen.transform, "Sea", quad,
            LoadOrSaveMaterial(shader, new Color(0.05f, 0.13f, 0.24f, 1f), "ACO_RadarMapSea", 3001, CompareFunction.LessEqual));
        MeshRenderer terrain = MakeRenderer(screen.transform, "Terrain", null,
            LoadOrSaveMaterial(shader, Color.white, "ACO_RadarMapTerrain", 3002, CompareFunction.LessEqual));
        MeshRenderer border = MakeRenderer(screen.transform, "Border", LoadOrBuildMesh("ACO_RadarMapBorder", () => BuildThinRing(64, 0.5f, 0.008f)),
            LoadOrSaveMaterial(shader, new Color(0.35f, 1f, 0.45f, 0.7f), "ACO_RadarMapBorder", 3003, CompareFunction.LessEqual));
        MeshRenderer blips = MakeRenderer(screen.transform, "Blips", null,
            LoadOrSaveMaterial(shader, Color.white, "ACO_RadarMapBlips", 3004, CompareFunction.LessEqual));
        MakeRenderer(screen.transform, "NorthMark", LoadOrBuildMesh("ACO_RadarNorthMark", BuildNorthMark),
            LoadOrSaveMaterial(shader, new Color(0.35f, 1f, 0.45f, 0.9f), "ACO_RadarMapNorth", 3004, CompareFunction.LessEqual));
        screen.SetActive(false);

        map.Screen = screen;
        map.TerrainFilter = terrain.GetComponent<MeshFilter>();
        map.BlipFilter = blips.GetComponent<MeshFilter>();
        map.Border = border.transform;
    }

    /// 작전 테이블의 북쪽 표시. 평면 지도와 같은 삼각형을 지도 면에 눕힌다. MapRoot의 +Z가 북쪽이다.
    /// 메시는 반지름 1 기준이고 크기로 지도 반지름에 맞춘다.
    public static void EnsureTacticalNorthMark(ACO_TacticalMap map)
    {
        if (map == null || map.MapRoot == null) { return; }
        Transform mark = map.MapRoot.Find(TacticalNorthName);
        if (mark == null)
        {
            Mesh mesh = LoadOrBuildMesh("ACO_MapNorthMark", BuildTacticalNorthMark);
            Material material = LoadOrSaveMaterial(Shader.Find("ACO/RadarVertexColor"), new Color(0.35f, 1f, 0.45f, 0.9f), "ACO_RadarMapNorth", 3004, CompareFunction.LessEqual);
            mark = MakeRenderer(map.MapRoot, TacticalNorthName, mesh, material).transform;
            Undo.RegisterCreatedObjectUndo(mark.gameObject, "ACO north mark");
        }
        Undo.RecordObject(mark, "ACO north mark");
        mark.localPosition = Vector3.up * TacticalNorthLift;
        mark.localRotation = Quaternion.identity;
        mark.localScale = new Vector3(map.MapRadius, 1f, map.MapRadius);
        EditorUtility.SetDirty(mark);
    }

    private static Mesh BuildTacticalNorthMark()
    {
        // 위에서 볼 때 시계 방향이 앞면이다
        var v = new List<Vector3> { new Vector3(0f, 0f, 0.98f), new Vector3(0.05f, 0f, 0.88f), new Vector3(-0.05f, 0f, 0.88f) };
        var t = new List<int> { 0, 1, 2 };
        return Finish(v, t);
    }

    /// 화면 위쪽 가장자리의 북쪽 표시(위를 가리키는 작은 삼각형).
    private static Mesh BuildNorthMark()
    {
        var v = new List<Vector3> { new Vector3(0f, 0.49f, 0f), new Vector3(-0.025f, 0.44f, 0f), new Vector3(0.025f, 0.44f, 0f) };
        var t = new List<int> { 0, 1, 2 };
        return Finish(v, t);
    }

    // ───────────── 메시 / 머티리얼 ─────────────

    private static MeshRenderer MakeRenderer(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        MeshFilter f = go.AddComponent<MeshFilter>();
        f.sharedMesh = mesh;   // null이면 스크립트가 실행 중에 만든다
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return r;
    }

    private static Mesh BuildDisc(int sides, float r)
    {
        var v = new List<Vector3> { Vector3.zero };
        var t = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            t.Add(0); t.Add(1 + i); t.Add(1 + (i + 1) % sides);
        }
        return Finish(v, t);
    }

    private static Mesh BuildQuad(float h)
    {
        var v = new List<Vector3> { new Vector3(-h, -h, 0f), new Vector3(-h, h, 0f), new Vector3(h, h, 0f), new Vector3(h, -h, 0f) };
        var t = new List<int> { 0, 1, 2, 0, 2, 3 };
        return Finish(v, t);
    }

    private static Mesh BuildThinRing(int sides, float r, float width)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        AddRing(v, t, sides, r, width);
        return Finish(v, t);
    }

    private static void AddRing(List<Vector3> v, List<int> t, int sides, float r, float width)
    {
        int b = v.Count;
        float inner = r - width;
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            v.Add(new Vector3(Mathf.Cos(a) * inner, Mathf.Sin(a) * inner, 0f));
            int n = (i + 1) % sides;
            t.Add(b + i * 2); t.Add(b + n * 2); t.Add(b + i * 2 + 1);
            t.Add(b + i * 2 + 1); t.Add(b + n * 2); t.Add(b + n * 2 + 1);
        }
    }

    private static void AddBar(List<Vector3> v, List<int> t, Vector3 min, Vector3 max)
    {
        int b = v.Count;
        v.Add(new Vector3(min.x, min.y, 0f));
        v.Add(new Vector3(min.x, max.y, 0f));
        v.Add(new Vector3(max.x, max.y, 0f));
        v.Add(new Vector3(max.x, min.y, 0f));
        t.Add(b); t.Add(b + 1); t.Add(b + 2);
        t.Add(b); t.Add(b + 2); t.Add(b + 3);
    }

    /// 정점 색은 흰색(셰이더가 정점 색 × _Color — 색은 머티리얼이 정한다).
    private static Mesh Finish(List<Vector3> v, List<int> t)
    {
        var colors = new Color[v.Count];
        for (int i = 0; i < colors.Length; i++) { colors[i] = Color.white; }
        var mesh = new Mesh { vertices = v.ToArray(), colors = colors, triangles = t.ToArray() };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh LoadOrBuildMesh(string name, System.Func<Mesh> build)
    {
        string path = $"{GeneratedDir}/{name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { return existing; }
        Mesh mesh = build();
        mesh.name = name;
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static Material LoadOrSaveMaterial(Shader shader, Color color, string name, int queue, CompareFunction zTest)
    {
        string path = $"{GeneratedDir}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { return existing; }
        var mat = new Material(shader) { name = name };
        mat.SetColor("_Color", color);
        mat.SetFloat("_ZTest", (float)zTest);
        mat.renderQueue = queue;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void EnsureGenerated()
    {
        if (!AssetDatabase.IsValidFolder(GeneratedDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
    }
}
