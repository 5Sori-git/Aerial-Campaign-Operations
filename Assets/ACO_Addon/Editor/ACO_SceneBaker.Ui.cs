using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using ACO;
using SaccFlightAndVehicles;

/// UI·관전·무전 — 전체 순서는 ACO_SceneBaker.BakeNeighbors.
public partial class ACO_SceneBaker
{
    /// 관전 패널(ACO_SpectatorCam): 플레이어 기체(ACO_VehicleUpgrades가 있는 SaccEntity), 해수면·지면 레이어(미션 생성기), 카메라 전용 하늘 안개 돔 머티리얼,
    /// 카메라가 그리는 레이어(UI·FX 빼기), 플레이어 미사일 무장 → 관전 패널(미사일·표적 캠).
    private static void BakeSpectators()
    {
        var specs = new List<ACO_SpectatorCam>();
        var vehicles = new List<SaccFlightAndVehicles.SaccEntity>();
        ACO_MissionGenerator gen = null;
        ACO_CloudField clouds = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            specs.AddRange(root.GetComponentsInChildren<ACO_SpectatorCam>(true));
            foreach (ACO_VehicleUpgrades up in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true))
            {
                SaccFlightAndVehicles.SaccEntity e = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
                if (e != null && !vehicles.Contains(e)) { vehicles.Add(e); }
            }
            if (gen == null) { gen = root.GetComponentInChildren<ACO_MissionGenerator>(true); }
            if (clouds == null) { clouds = root.GetComponentInChildren<ACO_CloudField>(true); }
        }
        // 관전 패널은 슬롯 순서로 넘긴다: 게임 흐름의 칸 순서를 앞에 둔다
        ACO_GameFlow flow = Object.FindObjectOfType<ACO_GameFlow>(true);
        if (flow != null && flow.Aircraft != null)
        {
            var ordered = new List<SaccFlightAndVehicles.SaccEntity>();
            foreach (SaccFlightAndVehicles.SaccEntity e in flow.Aircraft) { if (e != null && vehicles.Contains(e) && !ordered.Contains(e)) { ordered.Add(e); } }
            foreach (SaccFlightAndVehicles.SaccEntity e in vehicles) { if (!ordered.Contains(e)) { ordered.Add(e); } }
            vehicles = ordered;
        }
        ACO_Rain rain = Object.FindObjectOfType<ACO_Rain>();
        ACO_SpectatorCam first = specs.Count > 0 ? specs[0] : null;
        if (clouds != null && first != null && clouds.SpectatorVolume != first.CloudVolume)
        {
            clouds.SpectatorVolume = first.CloudVolume;
            MarkChanged(clouds);
        }
        if (specs.Count > 1) { Debug.LogWarning($"[ACO] 관전 패널이 {specs.Count}개입니다 — 미사일 캠은 첫 패널 '{first.name}'에만 연결됩니다(카메라를 여러 개 켜면 그만큼 무겁다).", first); }
        foreach (ACO_SpectatorCam s in specs)
        {
            bool changed = false;
            var arr = vehicles.ToArray();
            bool same = s.Vehicles != null && s.Vehicles.Length == arr.Length;
            for (int i = 0; same && i < arr.Length; i++) { same = s.Vehicles[i] == arr[i]; }
            if (!same) { s.Vehicles = arr; changed = true; }
            if (s.Flow != flow) { s.Flow = flow; changed = true; }
            if (s.Rain != rain) { s.Rain = rain; changed = true; }
            // 6-238: 씬 오버라이드로 비어 버린 관전 비(카메라 자식 SpectatorRain)를 다시 잇는다 — 비면 관전 화면에 비가 안 온다
            if (s.RainRenderer == null && s.Cam != null)
            {
                Transform sr = s.Cam.transform.Find("SpectatorRain");
                Renderer rr = sr != null ? sr.GetComponent<Renderer>() : null;
                if (rr != null) { s.RainRenderer = rr; changed = true; Debug.Log($"[ACO] 관전 패널 '{s.name}': 비어 있던 관전 비(SpectatorRain)를 다시 연결했습니다.", s); }
                else { Debug.LogWarning($"[ACO] 관전 패널 '{s.name}': 관전 비(SpectatorRain)가 없습니다 — 관전 화면에 비가 안 옵니다. 프리팹 생성 창에서 관전 패널을 다시 만드세요.", s); }
            }
            if (gen != null && (s.SeaLevel != gen.SeaLevel || s.GroundLayers != gen.GroundLayers)) { s.SeaLevel = gen.SeaLevel; s.GroundLayers = gen.GroundLayers; changed = true; }
            if (s.SkyFogDome != null && clouds != null && clouds.SkyFog != null && s.SkyFogDome.sharedMaterial != clouds.SkyFog.sharedMaterial)
            {
                s.SkyFogDome.sharedMaterial = clouds.SkyFog.sharedMaterial;
                MarkChanged(s.SkyFogDome);
            }
            if (s.SkyFogDome != null && s.SkyFogDome.gameObject.layer != ACO_HUDBuilder.SpectatorOnlyLayer)
            {
                s.SkyFogDome.gameObject.layer = ACO_HUDBuilder.SpectatorOnlyLayer;
                MarkChanged(s.SkyFogDome.gameObject);
            }
            if (s.FullscreenOverlay != null && s.FullscreenOverlay.gameObject.layer != FxLayer)
            {
                s.FullscreenOverlay.gameObject.layer = FxLayer;   // 관전 카메라가 자기 화면을 다시 찍지 않게
                MarkChanged(s.FullscreenOverlay.gameObject);
            }
            if (s.TextureHD == null || s.TextureFHD == null) { Debug.LogWarning($"[ACO] 관전 패널 '{s.name}': HD/FHD 렌더 텍스처가 비어 있습니다 — 프리팹 생성 창에서 관전 패널을 다시 만드세요(해상도 버튼·스트림 카메라 전체 화면).", s); }
            if (s.Cam != null && s.Cam.cullingMask != ACO_HUDBuilder.SpectatorCullingMask())
            {
                s.Cam.cullingMask = ACO_HUDBuilder.SpectatorCullingMask();
                MarkChanged(s.Cam);
            }
            if (changed) { MarkChanged(s); }
            if (vehicles.Count == 0) { Debug.LogWarning($"[ACO] 관전 패널 '{s.name}': 플레이어 기체(ACO_VehicleUpgrades가 있는 SaccEntity)가 없습니다.", s); }
        }
        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Missile c in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true)) { if (c.Spectator != first) { c.Spectator = first; MarkChanged(c); linked++; } }
        }
        if (first != null && linked > 0) { Debug.Log($"[ACO] 관전 패널: 플레이어 기체 {vehicles.Count}대, 미사일 무장 {linked}개를 연결했습니다.", first); }
    }

    // ───────────── 6-220: 선원실 영상 플레이어 소리 토글 · BGM 패널 크기 ─────────────
    // 브리핑실 모니터 화면(메시 Briefing/screen에서 잰 값): 가운데 · 크기(m). 패널은 화면보다 4mm 앞(뷰어 쪽 -z)
    private static readonly Vector3 BgmMonitorCenter = new Vector3(0.95f, -249.03f, 3.074f);
    private const float BgmMonitorW = 0.57f;
    // 선원실 셋톱박스 앞면 가운데(박스 x 19.06~19.54, y −249.815~−249.745, 앞면 z 0.265) — 토글은 3mm 앞
    private static readonly Vector3 SetTopFront = new Vector3(19.30f, -249.78f, 0.261f);

    /// YamaPlayer 소리(첫 오디오 소스 — 컨트롤러의 _audioSources). 씬에 없으면 null.
    public static AudioSource FindYamaAudio()
    {
        foreach (MonoBehaviour b in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (b == null || b.GetType().Name != "Controller" || b.GetType().Namespace == null || !b.GetType().Namespace.Contains("YamaStream")) { continue; }
            var so = new SerializedObject(b);
            SerializedProperty arr = so.FindProperty("_audioSources");
            if (arr == null) { continue; }
            for (int i = 0; i < arr.arraySize; i++) { var a = arr.GetArrayElementAtIndex(i).objectReferenceValue as AudioSource; if (a != null) { return a; } }
        }
        return null;
    }

    private static AudioSource[] FindYamaAudios()
    {
        var list = new List<AudioSource>();
        foreach (MonoBehaviour b in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (b == null || b.GetType().Name != "Controller" || b.GetType().Namespace == null || !b.GetType().Namespace.Contains("YamaStream")) { continue; }
            var so = new SerializedObject(b);
            SerializedProperty arr = so.FindProperty("_audioSources");
            if (arr == null) { continue; }
            for (int i = 0; i < arr.arraySize; i++) { var a = arr.GetArrayElementAtIndex(i).objectReferenceValue as AudioSource; if (a != null && !list.Contains(a)) { list.Add(a); } }
        }
        return list.ToArray();
    }

    /// 토글 ↔ YamaPlayer 소리(Bake).
    private static void BakeVideoAudioToggle()
    {
        AudioSource[] audios = FindYamaAudios();
        foreach (ACO_VideoAudioToggle t in Object.FindObjectsOfType<ACO_VideoAudioToggle>(true))
        {
            bool same = t.Targets != null && t.Targets.Length == audios.Length;
            for (int i = 0; same && i < audios.Length; i++) { same = t.Targets[i] == audios[i]; }
            if (same) { continue; }
            t.Targets = audios;
            MarkChanged(t);
            Debug.Log($"[ACO] 영상 소리 토글: YamaPlayer 소리 {audios.Length}개를 연결했습니다.", t);
        }
    }

    /// Setup Check [고치기]: 셋톱박스 앞면에 토글을 놓고, YamaPlayer 소리를 NEAR 3D로(VRC 공간 음향은 오디오 소스 거리 곡선을 쓰게 — 2D/3D 전환이 그대로 먹게).
    // 크레딧 보드 자리(6-228): 선원실 서쪽 벽 사물함 5개 중 가운데(레이아웃 y −1.17) 앞면(x 14.90)에 뒤로 기대 세운다. 함내 레이아웃 (x, y, 높이) → 함내 프리팹 로컬 (x, 높이, y).
    private const float CreditLockerFrontX = 14.90f;
    private const float CreditLockerY = -1.17f;
    private const float CreditTilt = 8f;

    /// Setup Check [고치기]: 크레딧 보드(없으면 프리팹을 만든다)를 선원실 가운데 사물함 앞 바닥에 놓고 뒤로 8도 기대 세운다. 함내(ACO_Interior)가 없으면 아무것도 하지 않는다.
    public static void CreateCreditBoard()
    {
        string path = "Assets/ACO_Addon/Prefabs/ACO_CreditBoard.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { ACO_HUDBuilder.CreateCreditBoardPrefab(); }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Transform interior = null;
        foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in r.GetComponentsInChildren<Transform>(true)) { if (t.name == ACO_InteriorBuilder.PrefabName) { interior = t; break; } }
            if (interior != null) { break; }
        }
        if (prefab == null || interior == null) { Debug.LogWarning("[ACO] 크레딧 보드: 프리팹이나 함내(ACO_Interior)가 없습니다."); return; }
        Transform parent = interior.parent;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "ACO credit board");
        float h = ACO_HUDBuilder.CreditH * ACO_HUDBuilder.CreditUnit;
        // 앞(캔버스 앞면 = 루트 +Z)이 방 쪽(+x), 윗변을 사물함 쪽으로 기울인다
        Quaternion rot = interior.rotation * Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(CreditTilt, 0f, 0f);
        float baseX = CreditLockerFrontX + Mathf.Sin(CreditTilt * Mathf.Deg2Rad) * h + 0.02f;
        Vector3 bottom = interior.TransformPoint(new Vector3(baseX, 0.02f, CreditLockerY));
        go.transform.SetPositionAndRotation(bottom + rot * Vector3.up * (h * 0.5f), rot);
        BakeUiSound();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 크레딧 보드를 선원실 가운데 사물함 앞에 기대 세웠습니다. 씬을 저장하세요.", go);
    }

    // 업데이트 노트 보드 자리(6-251): 크레딧 보드 바로 옆 사물함(레이아웃 y −0.51, 간격 0.66) 앞면에 같은 각도로 기대 세운다.
    private const float UpdateLockerY = -0.51f;

    /// Setup Check [고치기]: 업데이트 노트 보드(없으면 프리팹을 만든다)를 선원실 크레딧 보드 옆 사물함 앞에 놓는다. 함내(ACO_Interior)가 없으면 아무것도 하지 않는다.
    public static void CreateUpdateBoard()
    {
        string path = $"Assets/ACO_Addon/Prefabs/{ACO_HUDBuilder.UpdateBoardPrefabName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { ACO_HUDBuilder.CreateUpdateBoardPrefab(); }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Transform interior = null;
        foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in r.GetComponentsInChildren<Transform>(true)) { if (t.name == ACO_InteriorBuilder.PrefabName) { interior = t; break; } }
            if (interior != null) { break; }
        }
        if (prefab == null || interior == null) { Debug.LogWarning("[ACO] 업데이트 노트 보드: 프리팹이나 함내(ACO_Interior)가 없습니다."); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, interior.parent);
        Undo.RegisterCreatedObjectUndo(go, "ACO update board");
        float h = ACO_HUDBuilder.CreditH * ACO_HUDBuilder.CreditUnit;
        Quaternion rot = interior.rotation * Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(CreditTilt, 0f, 0f);
        float baseX = CreditLockerFrontX + Mathf.Sin(CreditTilt * Mathf.Deg2Rad) * h + 0.02f;
        Vector3 bottom = interior.TransformPoint(new Vector3(baseX, 0.02f, UpdateLockerY));
        go.transform.SetPositionAndRotation(bottom + rot * Vector3.up * (h * 0.5f), rot);
        // 크레딧 보드가 있으면 그 자리 · 각도 · 크기를 따라 사물함 한 칸(0.66m) 옆에 — 씬에서 크레딧 보드를 옮겨 둔 경우(사물함에 세워 붙임)
        foreach (ACO_CreditBoard cb in Object.FindObjectsOfType<ACO_CreditBoard>(true))
        {
            if (cb.gameObject == go || (cb.LanguageSources != null && cb.LanguageSources.Length > 0)) { continue; }
            go.transform.SetPositionAndRotation(cb.transform.position + interior.rotation * new Vector3(0f, 0f, -0.66f), cb.transform.rotation);
            go.transform.localScale = cb.transform.localScale;
            break;
        }
        BakeUiSound();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 업데이트 노트 보드를 선원실 크레딧 보드 옆 사물함 앞에 기대 세웠습니다. Bake 후 씬을 저장하세요.", go);
    }

    public static void CreateVideoAudioToggle()
    {
        string path = $"Assets/ACO_Addon/Prefabs/{ACO_HUDBuilder.VideoAudioTogglePrefab}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { ACO_HUDBuilder.CreateVideoAudioTogglePrefab(); }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        AudioSource yama = FindYamaAudio();
        if (prefab == null || yama == null) { return; }
        Transform parent = yama.transform.parent != null ? yama.transform.parent.parent : null;   // YamaPlayer와 같은 부모(BOARD)
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "ACO video audio toggle");
        go.transform.SetPositionAndRotation(SetTopFront, Quaternion.Euler(0f, 180f, 0f));
        foreach (AudioSource a in FindYamaAudios())
        {
            Undo.RecordObject(a, "ACO video audio");
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 5f;
            a.maxDistance = 10f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(a);
            foreach (Component c in a.GetComponents<Component>())
            {
                if (c == null || c.GetType().Name != "VRCSpatialAudioSource") { continue; }
                var so = new SerializedObject(c);
                SerializedProperty curve = so.FindProperty("UseAudioSourceVolumeCurve");
                SerializedProperty spat = so.FindProperty("EnableSpatialization");
                if (curve != null) { curve.boolValue = true; }
                if (spat != null) { spat.boolValue = false; }
                so.ApplyModifiedProperties();
            }
        }
        BakeVideoAudioToggle();
        BakeUiSound();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[ACO] 영상 소리 토글을 선원실 셋톱박스 앞에 놓고 YamaPlayer 소리를 NEAR(3D, 5~10m)로 맞췄습니다. 씬을 저장하세요.", go);
    }

    /// BGM 패널 캔버스가 모니터 화면 크기 · 자리인가(1cm · 1mm 안).
    /// 무전 자막 글자 상자 폭이 줄바꿈 폭(ACO_HUDBuilder.SubtitleWrapWidth)인가 — 6-236.
    public static bool SubtitleWidthOk(ACO_Chatter chatter)
    {
        if (chatter == null || chatter.SubtitleText == null) { return true; }
        return Mathf.Abs(chatter.SubtitleText.rectTransform.sizeDelta.x - ACO_HUDBuilder.SubtitleWrapWidth) < 0.01f;
    }

    /// Setup Check [고치기]: 씬 무전 자막의 글자 상자 폭을 줄바꿈 폭으로(높이 · 위치는 그대로, 아래 정렬이라 줄이 늘면 위로 쌓인다).
    public static void FitSubtitleWidth(ACO_Chatter chatter)
    {
        if (chatter == null || chatter.SubtitleText == null) { return; }
        RectTransform rt = chatter.SubtitleText.rectTransform;
        Undo.RecordObject(rt, "ACO subtitle width");
        rt.sizeDelta = new Vector2(ACO_HUDBuilder.SubtitleWrapWidth, rt.sizeDelta.y);
        Undo.RecordObject(chatter.SubtitleText, "ACO subtitle width");
        chatter.SubtitleText.enableWordWrapping = true;
        EditorUtility.SetDirty(rt);
        EditorUtility.SetDirty(chatter.SubtitleText);
        EditorSceneManager.MarkSceneDirty(chatter.gameObject.scene);
        Debug.Log("[ACO] 무전 자막 폭을 " + ACO_HUDBuilder.SubtitleWrapWidth + "단위로 맞췄습니다(긴 대사가 줄바꿈된다). 씬을 저장하세요.", chatter);
    }

    public static bool BgmPanelFitsMonitor(ACO_BgmPanel panel)
    {
        RectTransform canvas = panel.GetComponentInChildren<Canvas>(true) != null ? panel.GetComponentInChildren<Canvas>(true).GetComponent<RectTransform>() : null;
        if (canvas == null) { return true; }
        float w = canvas.sizeDelta.x * canvas.lossyScale.x;
        return Mathf.Abs(w - BgmMonitorW) < 0.01f && (panel.transform.position - BgmMonitorCenter).sqrMagnitude < 0.000001f;
    }

    /// Setup Check [고치기]: 패널 루트 배율을 캔버스 폭이 모니터 화면 폭이 되게, 자리를 화면 가운데(4mm 앞)로.
    public static void FitBgmPanelToMonitor(ACO_BgmPanel panel)
    {
        RectTransform canvas = panel.GetComponentInChildren<Canvas>(true).GetComponent<RectTransform>();
        Undo.RecordObject(panel.transform, "ACO bgm panel fit");
        float k = BgmMonitorW / Mathf.Max(canvas.sizeDelta.x * canvas.localScale.x, 1e-6f);
        panel.transform.localScale = Vector3.one * k;
        panel.transform.position = BgmMonitorCenter;
        PrefabUtility.RecordPrefabInstancePropertyModifications(panel.transform);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        Debug.Log($"[ACO] BGM 패널을 모니터 화면에 맞췄습니다(배율 {k:0.000}, 폭 {BgmMonitorW}m). 씬을 저장하세요.", panel);
    }

    /// 패널 버튼 효과음: 씬에 하나 만들고(없으면) 모든 패널 버튼에 연결한다.
    private static void BakeUiSound()
    {
        ACO_UiSound sound = Object.FindObjectOfType<ACO_UiSound>(true);
        if (sound == null) { return; }   // 만드는 일은 Setup Check가 맡는다
        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_FlowButton c in root.GetComponentsInChildren<ACO_FlowButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_MissionStartButton c in root.GetComponentsInChildren<ACO_MissionStartButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_HangarButton c in root.GetComponentsInChildren<ACO_HangarButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_HudSettingButton c in root.GetComponentsInChildren<ACO_HudSettingButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_DebugButton c in root.GetComponentsInChildren<ACO_DebugButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_SpectatorButton c in root.GetComponentsInChildren<ACO_SpectatorButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_LogScrollButton c in root.GetComponentsInChildren<ACO_LogScrollButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_CreditScrollButton c in root.GetComponentsInChildren<ACO_CreditScrollButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_TacName c in root.GetComponentsInChildren<ACO_TacName>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_BgmPanel c in root.GetComponentsInChildren<ACO_BgmPanel>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_VideoAudioToggle c in root.GetComponentsInChildren<ACO_VideoAudioToggle>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_DecalButton c in root.GetComponentsInChildren<ACO_DecalButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_Teleporter c in root.GetComponentsInChildren<ACO_Teleporter>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
        }
        if (linked > 0) { Debug.Log($"[ACO] 버튼 효과음을 버튼 {linked}개에 연결했습니다.", sound); }
    }

    /// 함내 격실: 구역 ↔ 비·물속·표적 마커·진행도, 복도 문 ↔ 격납고·갑판 문, CIC 무전 구역 ↔ SAV 무전 기지.
    private static void BakeInterior()
    {
        ACO_InteriorZone zone = Object.FindObjectOfType<ACO_InteriorZone>(true);
        if (zone == null) { return; }
        ACO_Rain rain = Object.FindObjectOfType<ACO_Rain>(true);
        if (rain != null && rain.Interior != zone) { rain.Interior = zone; MarkChanged(rain); }
        ACO_Underwater underwater = Object.FindObjectOfType<ACO_Underwater>(true);
        if (underwater != null && underwater.Interior != zone) { underwater.Interior = zone; MarkChanged(underwater); }
        ACO_TargetMarkers markers = Object.FindObjectOfType<ACO_TargetMarkers>(true);
        if (markers != null && markers.Interior != zone) { markers.Interior = zone; MarkChanged(markers); }
        ACO_PlayerProgress progress = Object.FindObjectOfType<ACO_PlayerProgress>(true);
        if (progress != null && progress.Interior != zone) { progress.Interior = zone; MarkChanged(progress); }

        LinkHatch(zone.transform, ACO_InteriorBuilder.DoorHangar, ACO_InteriorBuilder.ArriveHangar, ACO_InteriorBuilder.ShipHatchHangar);
        LinkHatch(zone.transform, ACO_InteriorBuilder.DoorDeck, ACO_InteriorBuilder.ArriveDeck, ACO_InteriorBuilder.ShipHatchDeck);

        SaccRadioBase radio = Object.FindObjectOfType<SaccRadioBase>(true);
        if (radio == null) { return; }
        // 무전기 등 다른 구역은 그대로 두고 격실 구역만 바꾼다
        var zones = new List<SaccRadioZone>();
        if (radio.RadioZones != null)
        {
            foreach (SaccRadioZone z in radio.RadioZones)
            {
                if (z != null && !z.transform.IsChildOf(zone.transform) && !zones.Contains(z)) { zones.Add(z); }
            }
        }
        zones.AddRange(zone.GetComponentsInChildren<SaccRadioZone>(true));
        if (!SameRefs(radio.RadioZones, zones.ToArray()))
        {
            radio.RadioZones = zones.ToArray();
            MarkChanged(radio);
        }
    }

    /// 복도 문과 바깥 문을 서로의 도착 지점에 잇는다. 바깥 문이 씬에 없으면 Setup Check가 알려 준다.
    private static void LinkHatch(Transform systems, string door, string arrive, string hatchName)
    {
        Transform doors = systems.Find("Doors");
        ACO_Teleporter inner = doors != null ? doors.Find(door)?.GetComponent<ACO_Teleporter>() : null;
        Transform innerArrive = doors != null ? doors.Find(arrive) : null;
        if (inner == null || innerArrive == null) { return; }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Teleporter outer in root.GetComponentsInChildren<ACO_Teleporter>(true))
            {
                if (!outer.name.StartsWith(hatchName, System.StringComparison.Ordinal)) { continue; }
                Transform outerArrive = outer.transform.Find(ACO_InteriorBuilder.ShipHatchArrive);
                if (inner.Target != outerArrive) { inner.Target = outerArrive; MarkChanged(inner); }
                if (outer.Target != innerArrive) { outer.Target = innerArrive; MarkChanged(outer); }
            }
        }
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
            foreach (ACO_Twins c in root.GetComponentsInChildren<ACO_Twins>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_ShadeAce c in root.GetComponentsInChildren<ACO_ShadeAce>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_FlareDispenser c in root.GetComponentsInChildren<ACO_FlareDispenser>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_VehicleUpgrades c in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
        }
        if (radio.Voice == null) { Debug.LogWarning("[ACO] 무전 채터에 음성 AudioSource(Voice)가 없어 음성·PTT가 들리지 않습니다.", radio); }

        if (linked > 0) { Debug.Log($"[ACO] 무전 채터 '{radio.name}'를 {linked}곳에 연결했습니다.", radio); }
    }

    /// 새로 생긴 상황(EV_*)의 기본 대사가 씬의 대사 표에 하나도 없으면 끝에 더하고, 확률·쿨타임 배열도 늘린다(손으로 고친 대사·음성은 그대로).
    public static void AppendMissingChatterLines(ACO_Chatter radio)
    {
        var temp = new GameObject("ACO_ChatterDefaults") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            ACO_Chatter d = temp.AddComponent<ACO_Chatter>();
            var ev = new List<int>(radio.LineEvent ?? new int[0]);
            var sp = new List<int>(radio.LineSpeaker ?? new int[0]);
            var tx = new List<string>(radio.LineText ?? new string[0]);
            var cl = new List<AudioClip>(radio.LineClip ?? new AudioClip[0]);
            var present = new HashSet<int>(ev);
            int added = 0;
            for (int i = 0; i < d.LineEvent.Length; i++)
            {
                if (present.Contains(d.LineEvent[i])) { continue; }
                while (sp.Count < ev.Count) { sp.Add(0); }
                while (tx.Count < ev.Count) { tx.Add(""); }
                while (cl.Count < ev.Count) { cl.Add(null); }
                ev.Add(d.LineEvent[i]);
                sp.Add(i < d.LineSpeaker.Length ? d.LineSpeaker[i] : 0);
                tx.Add(d.LineText[i]);
                cl.Add(null);
                added++;
            }
            bool grow = (radio.EventChance == null || radio.EventChance.Length < ACO_Chatter.EV_COUNT)
                || (radio.EventCooldown == null || radio.EventCooldown.Length < ACO_Chatter.EV_COUNT);
            if (added == 0 && !grow) { return; }
            Undo.RecordObject(radio, "ACO chatter lines");
            if (added > 0)
            {
                radio.LineEvent = ev.ToArray();
                radio.LineSpeaker = sp.ToArray();
                radio.LineText = tx.ToArray();
                radio.LineClip = cl.ToArray();
            }
            if (grow)
            {
                radio.EventChance = GrowTo(radio.EventChance, d.EventChance);
                radio.EventCooldown = GrowTo(radio.EventCooldown, d.EventCooldown);
            }
            MarkChanged(radio);
            Debug.Log($"[ACO] 무전 채터: 새 상황 대사 {added}줄을 표 끝에 더했습니다(아군 플레어·연막, 콘보이 피격·파괴).", radio);
        }
        finally { Object.DestroyImmediate(temp); }
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

    /// 하이스코어판의 글자를 작전판에 잇는다. 순위를 계산하는 쪽은 작전판(ACO_MissionLog)이다.
    private static void BakeHiScoreBoard()
    {
        ACO_MissionLog log = null;
        TMPro.TextMeshPro text = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (log == null) { log = root.GetComponentInChildren<ACO_MissionLog>(true); }
            foreach (TMPro.TextMeshPro t in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))
            {
                if (t.name == ACO_HUDBuilder.HiScoreTextName && t.transform.root.name == ACO_HUDBuilder.HiScoreName) { text = t; }
            }
        }
        if (log == null || text == null || log.BestText == text) { return; }
        log.BestText = text;
        MarkChanged(log);
        Debug.Log($"[ACO] 하이스코어판을 작전판 '{log.name}'에 연결했습니다.");
    }

    private static void BakeTacticalMaps()
    {
        ACO_MissionGenerator generator = null;
        ACO_GameFlow flow = null;
        ACO_SupportManager support = null;
        var maps = new List<ACO_TacticalMap>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (generator == null) { generator = root.GetComponentInChildren<ACO_MissionGenerator>(true); }
            if (flow == null) { flow = root.GetComponentInChildren<ACO_GameFlow>(true); }
            if (support == null) { support = root.GetComponentInChildren<ACO_SupportManager>(true); }
            maps.AddRange(root.GetComponentsInChildren<ACO_TacticalMap>(true));
        }
        foreach (ACO_TacticalMap map in maps)
        {
            bool changed = false;
            if (map.Generator == null && generator != null) { map.Generator = generator; changed = true; }
            if (map.Flow == null && flow != null) { map.Flow = flow; changed = true; }
            if (map.Support != support) { map.Support = support; changed = true; }   // 아군 편대 표시(6-285)
            ACO_MissionLog gridLog = map.GridFilter != null ? Object.FindObjectOfType<ACO_MissionLog>(true) : null;
            if (map.GridLog != gridLog) { map.GridLog = gridLog; changed = true; }   // 좌표 격자(6-301)
            if (changed) { MarkChanged(map); }
            if (map.HomeBase == null) { Debug.LogWarning($"[ACO] 작전 테이블 '{map.name}': HomeBase(항모)가 비어 있어 기지 표시가 없습니다.", map); }
        }
    }

    /// 아군 기지(항모) — 작전 테이블의 HomeBase 하나로 정한다(HUD 마커 · 조종석/벽걸이 지도가 같이 쓴다). 없으면 null.
    private static Transform HomeBase()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_TacticalMap map in root.GetComponentsInChildren<ACO_TacticalMap>(true)) { if (map.HomeBase != null) { return map.HomeBase; } }
        }
        return null;
    }

    /// 지형 제외 구역(스폰 지점 — 둘레 바다 + 작전 지역 밖까지 바닷길)이 비었거나 끊긴 칸이 있으면 기지(HomeBase)의 콜라이더로 채운다.
    /// (6-198: 예전엔 미션 생성기의 1m 상자였는데 생성기 콜라이더를 지우면서 끊겨 섬이 항모 자리까지 솟고 바닷길이 사라졌다)
    private static void LinkTerrainExclude(Transform home)
    {
        Collider zone = home != null ? home.GetComponent<Collider>() : null;
        if (zone == null) { return; }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_HexTerrain t in root.GetComponentsInChildren<ACO_HexTerrain>(true))
            {
                bool broken = t.ExcludeZones == null || t.ExcludeZones.Length == 0;
                if (!broken) { foreach (Collider c in t.ExcludeZones) { if (c == null) { broken = true; } } }
                if (!broken) { continue; }
                var list = new List<Collider>();
                if (t.ExcludeZones != null) { foreach (Collider c in t.ExcludeZones) { if (c != null && c != zone) { list.Add(c); } } }
                list.Insert(0, zone);
                t.ExcludeZones = list.ToArray();
                MarkChanged(t);
                Debug.Log($"[ACO] 지형 '{t.name}': 제외 구역(스폰 지점)에 기지 '{zone.name}'를 넣었습니다 — 둘레 바다 · 바닷길이 다시 생깁니다.", t);
            }
        }
    }

    /// 머리·시야에 붙는 HUD 오버레이를 UI 레이어(5)로 — VRChat 카메라(사진·스트림)에 찍히지 않는다(카메라에서 "UI"를 켜야 찍힌다),
    /// 상호작용 레이저도 막지 않는다. 대상: 점수 HUD·표적 마커(ACO_HUD), HUD 레이더, RWR, 무전 자막 패널, 화면 페이드 구, 물속 물빛 구.
    /// UI 레이어는 VRChat 레이저 포인터가 무시하므로(메뉴를 열었을 때만 누른다) 누르는 패널(격납고·콘솔·HUD 설정·디버그 패널·작전판)은 옮기지 않고,
    /// 대상 안에서도 버튼·VRCUiShape·콜라이더가 붙은 오브젝트는 건너뛴다.
    private const int UiLayer = 5;

    private static void BakeUiLayers()
    {
        var roots = new List<GameObject>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_ScoreHUD c in root.GetComponentsInChildren<ACO_ScoreHUD>(true)) { roots.Add(c.gameObject); }
            foreach (ACO_TargetMarkers c in root.GetComponentsInChildren<ACO_TargetMarkers>(true)) { roots.Add(c.gameObject); }
            foreach (ACO_HudRadar c in root.GetComponentsInChildren<ACO_HudRadar>(true)) { roots.Add(c.gameObject); }
            foreach (ACO_RWR c in root.GetComponentsInChildren<ACO_RWR>(true)) { roots.Add(c.gameObject); }
            foreach (ACO_Chatter c in root.GetComponentsInChildren<ACO_Chatter>(true)) { if (c.Panel != null) { roots.Add(c.Panel.gameObject); } }
            foreach (ACO_ScreenFader c in root.GetComponentsInChildren<ACO_ScreenFader>(true)) { if (c.FadeRenderer != null) { roots.Add(c.FadeRenderer.gameObject); } }
            foreach (ACO_Underwater c in root.GetComponentsInChildren<ACO_Underwater>(true)) { if (c.Tint != null) { roots.Add(c.Tint.gameObject); } }
            foreach (ACO_FlightHud c in root.GetComponentsInChildren<ACO_FlightHud>(true)) { if (c.Root != null) { roots.Add(c.Root.gameObject); } }
        }
        int moved = 0;
        var seen = new HashSet<GameObject>();
        foreach (GameObject r in roots)
        {
            foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;
                if (!seen.Add(go) || go.layer == UiLayer) { continue; }
                if (IsInteractive(go)) { continue; }
                go.layer = UiLayer;
                MarkChanged(go);
                moved++;
            }
        }
        if (moved > 0) { Debug.Log($"[ACO] HUD 오버레이 오브젝트 {moved}개를 UI 레이어(5)로 옮겼습니다 — VRChat 카메라(사진)에 찍히지 않습니다."); }
    }

    /// 누르거나 부딪히는 오브젝트(UI 레이어로 옮기면 레이저·Interact가 안 된다). 누르는 캔버스(VRCUiShape) 아래는 통째로.
    private static bool IsInteractive(GameObject go)
    {
        return go.GetComponentInParent<VRC.SDK3.Components.VRCUiShape>(true) != null
            || go.GetComponent<UnityEngine.UI.Selectable>() != null
            || go.GetComponent<Collider>() != null;
    }

    /// 디버그 패널(ACO_DebugPanel) ↔ 미션 관리자·생성기·진행도.
    private static void BakeDebugPanels()
    {
        ACO_MissionManager manager = null;
        ACO_MissionGenerator gen = null;
        ACO_PlayerProgress progress = null;
        ACO_Weather weather = null;
        ACO_HexTerrain terrain = null;
        ACO_GameFlow flow = null;
        var panels = new List<ACO_DebugPanel>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (manager == null) { manager = root.GetComponentInChildren<ACO_MissionManager>(true); }
            if (gen == null) { gen = root.GetComponentInChildren<ACO_MissionGenerator>(true); }
            if (progress == null) { progress = root.GetComponentInChildren<ACO_PlayerProgress>(true); }
            if (weather == null) { weather = root.GetComponentInChildren<ACO_Weather>(true); }
            if (terrain == null) { terrain = root.GetComponentInChildren<ACO_HexTerrain>(true); }
            if (flow == null) { flow = root.GetComponentInChildren<ACO_GameFlow>(true); }
            panels.AddRange(root.GetComponentsInChildren<ACO_DebugPanel>(true));
        }
        foreach (ACO_DebugPanel p in panels)
        {
            if (p.Manager == manager && p.Generator == gen && p.Progress == progress && p.Weather == weather && p.Terrain == terrain && p.Flow == flow) { continue; }
            p.Manager = manager;
            p.Generator = gen;
            p.Progress = progress;
            p.Weather = weather;
            p.Terrain = terrain;
            p.Flow = flow;
            MarkChanged(p);
        }
        foreach (ACO_DebugPanel p in panels)
        {
            if (p.AllowedPlayers == null || p.AllowedPlayers.Length == 0)
            {
                Debug.LogWarning($"[ACO] 디버그 패널 '{p.name}'의 AllowedPlayers가 비어 있어 아무에게도 보이지 않습니다 — 쓸 사람의 VRChat 표시 이름을 넣으세요(6-223).", p);
            }
        }
    }

    /// 코드에 새 상황 대사나 이벤트가 있어 씬 표가 모자란가.
    public static bool ChatterTableShort(ACO_Chatter radio)
    {
        if (radio.EventChance == null || radio.EventChance.Length < ACO_Chatter.EV_COUNT) { return true; }
        if (radio.EventCooldown == null || radio.EventCooldown.Length < ACO_Chatter.EV_COUNT) { return true; }
        var temp = new GameObject("ACO_ChatterDefaults") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var present = new HashSet<int>(radio.LineEvent ?? new int[0]);
            foreach (int ev in temp.AddComponent<ACO_Chatter>().LineEvent)
            {
                if (!present.Contains(ev)) { return true; }
            }
            return false;
        }
        finally { Object.DestroyImmediate(temp); }
    }
}
