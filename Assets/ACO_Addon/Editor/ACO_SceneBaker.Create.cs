using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using ACO;

/// ACO 메뉴의 Create 항목 — 전체 순서는 ACO_SceneBaker.BakeNeighbors.
public partial class ACO_SceneBaker
{
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
