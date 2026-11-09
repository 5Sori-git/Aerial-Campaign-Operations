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
            foreach (ACO_TacName c in root.GetComponentsInChildren<ACO_TacName>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
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
            foreach (ACO_SuperAce c in root.GetComponentsInChildren<ACO_SuperAce>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
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
        var maps = new List<ACO_TacticalMap>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (generator == null) { generator = root.GetComponentInChildren<ACO_MissionGenerator>(true); }
            if (flow == null) { flow = root.GetComponentInChildren<ACO_GameFlow>(true); }
            maps.AddRange(root.GetComponentsInChildren<ACO_TacticalMap>(true));
        }
        foreach (ACO_TacticalMap map in maps)
        {
            bool changed = false;
            if (map.Generator == null && generator != null) { map.Generator = generator; changed = true; }
            if (map.Flow == null && flow != null) { map.Flow = flow; changed = true; }
            if (changed) { MarkChanged(map); }
            if (map.HomeBase == null) { Debug.LogWarning($"[ACO] 작전 테이블 '{map.name}': HomeBase(항모)가 비어 있어 기지 표시가 없습니다.", map); }
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
                Debug.LogWarning($"[ACO] 디버그 패널 '{p.name}'은 누구나 누를 수 있습니다(AllowedPlayers 비어 있음) — 공개 월드에 올릴 땐 이름을 넣거나 패널을 끄세요.", p);
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
