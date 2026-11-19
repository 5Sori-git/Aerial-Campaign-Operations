using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using ACO;

/// CIC 도구(6-275) 연결 — 전체 순서는 ACO_SceneBaker.BakeNeighbors.
/// 콘솔(ACO_CicConsole): 미션 관리자 · 지원 관리자 · 가장 가까운 전술 테이블 · 진행도 · 게임 진행 · 항모 · 정찰 드론 · 효과음 · 지면 레이어 · 해수면.
/// 고글(ACO_CicGoggles): 콘솔(드론 자리) · 첫 관전 패널(카메라를 빌려 쓴다). 고글 패널(ACO_CicGogglesPanel): 게임 진행 · 효과음. 관전 패널: 구름 · 함내 격실(카메라 기준 안개).
/// 표적 마커 · 시가지 정밀 타격 → 콘솔(웨이포인트 · 드론 식별).
public partial class ACO_SceneBaker
{
    /// 아군 순양함(이름 Ticonderoga)의 VLS 발사 칸(MissileLauncher 아래 Cell_*) — 이름 순.
    private static Transform[] CruiserVlsCells()
    {
        var list = new List<Transform>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Ticonderoga") { continue; }
                foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                {
                    if (c.name.StartsWith("Cell_") && c.parent != null && c.parent.name == "MissileLauncher") { list.Add(c); }
                }
                break;
            }
            if (list.Count > 0) { break; }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list.ToArray();
    }

    private static bool SameTransforms(Transform[] a, Transform[] b)
    {
        if (a == null || b == null) { return a == b; }
        if (a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
    }

    private static void BakeCic()
    {
        var consoles = new List<ACO_CicConsole>();
        var goggles = new List<ACO_CicGoggles>();
        var panels = new List<ACO_CicGogglesPanel>();
        var specs = new List<ACO_SpectatorCam>();
        var markers = new List<ACO_TargetMarkers>();
        var precisions = new List<ACO_PhasePrecision>();
        var maps = new List<ACO_TacticalMap>();
        Transform uav = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            consoles.AddRange(root.GetComponentsInChildren<ACO_CicConsole>(true));
            goggles.AddRange(root.GetComponentsInChildren<ACO_CicGoggles>(true));
            panels.AddRange(root.GetComponentsInChildren<ACO_CicGogglesPanel>(true));
            specs.AddRange(root.GetComponentsInChildren<ACO_SpectatorCam>(true));
            markers.AddRange(root.GetComponentsInChildren<ACO_TargetMarkers>(true));
            precisions.AddRange(root.GetComponentsInChildren<ACO_PhasePrecision>(true));
            maps.AddRange(root.GetComponentsInChildren<ACO_TacticalMap>(true));
            if (uav == null && root.name.StartsWith(ACO_HUDBuilder.CicUavName)) { uav = root.transform; }
        }

        ACO_CloudField clouds = Object.FindObjectOfType<ACO_CloudField>(true);
        ACO_InteriorZone interior = Object.FindObjectOfType<ACO_InteriorZone>(true);
        foreach (ACO_SpectatorCam s in specs)
        {
            bool changed = false;
            if (s.Clouds != clouds) { s.Clouds = clouds; changed = true; }
            if (s.Interior != interior) { s.Interior = interior; changed = true; }
            if (changed) { MarkChanged(s); }
        }

        ACO_CicConsole console = consoles.Count > 0 ? consoles[0] : null;
        if (consoles.Count > 1) { Debug.LogWarning($"[ACO] CIC 콘솔이 {consoles.Count}개입니다 — 첫 번째만 연결합니다(웨이포인트 · 드론은 하나여야 한다).", consoles[1]); }
        foreach (ACO_TargetMarkers m in markers) { if (m.Cic != console) { m.Cic = console; MarkChanged(m); } }
        // 고글로 보는 동안 마커를 그 카메라 기준으로(6-286)
        ACO_CicGoggles[] allGoggles = Object.FindObjectsOfType<ACO_CicGoggles>(true);
        foreach (ACO_TargetMarkers m in markers)
        {
            bool same = m.Goggles != null && m.Goggles.Length == allGoggles.Length;
            if (same) { for (int i = 0; i < allGoggles.Length; i++) { if (m.Goggles[i] != allGoggles[i]) { same = false; break; } } }
            if (!same) { m.Goggles = allGoggles; MarkChanged(m); }
        }
        foreach (ACO_PhasePrecision p in precisions) { if (p.Cic != console) { p.Cic = console; MarkChanged(p); } }
        if (console == null) { return; }

        ACO_MissionGenerator gen = Object.FindObjectOfType<ACO_MissionGenerator>(true);
        ACO_TacticalMap map = null;
        float best = float.MaxValue;
        foreach (ACO_TacticalMap m in maps)
        {
            float d = (m.transform.position - console.transform.position).sqrMagnitude;
            if (d < best) { best = d; map = m; }
        }
        {
            bool changed = false;
            ACO_MissionManager mission = Object.FindObjectOfType<ACO_MissionManager>(true);
            ACO_SupportManager support = Object.FindObjectOfType<ACO_SupportManager>(true);
            ACO_PlayerProgress progress = Object.FindObjectOfType<ACO_PlayerProgress>(true);
            ACO_GameFlow flow = Object.FindObjectOfType<ACO_GameFlow>(true);
            ACO_UiSound sound = Object.FindObjectOfType<ACO_UiSound>(true);
            Transform home = HomeBase();
            if (console.Mission != mission) { console.Mission = mission; changed = true; }
            if (console.Support != support) { console.Support = support; changed = true; }
            if (console.Map != map) { console.Map = map; changed = true; }
            if (console.Progress != progress) { console.Progress = progress; changed = true; }
            if (console.Flow != flow) { console.Flow = flow; changed = true; }
            if (console.HomeBase != home) { console.HomeBase = home; changed = true; }
            if (console.Drone != uav) { console.Drone = uav; changed = true; }
            if (console.Sound != sound) { console.Sound = sound; changed = true; }
            // 무전(6-282): 채터 · 빈 음성 칸만 만든 파일로(Tools/aco_voice_lines.json의 cic_*)
            ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
            if (console.Chatter != chatter) { console.Chatter = chatter; changed = true; }
            if (console.WaypointLineClip == null) { console.WaypointLineClip = CicVoice("Cic_Waypoint"); changed |= console.WaypointLineClip != null; }
            if (console.UavLineClip == null) { console.UavLineClip = CicVoice("Cic_Uav"); changed |= console.UavLineClip != null; }
            if (gen != null)
            {
                if (console.GroundLayers != gen.GroundLayers) { console.GroundLayers = gen.GroundLayers; changed = true; }
                float sea = gen.HexTerrain != null ? gen.HexTerrain.SeaLevel : 0f;
                if (!Mathf.Approximately(console.SeaLevel, sea)) { console.SeaLevel = sea; changed = true; }
            }
            // 순항미사일 일제 발사(6-276): 콘솔 · 미션 · 항모 · 지면 · 해수면 · 피해 · 물 레이어(지원 관리자 값) · 아군 순양함 VLS 칸
            ACO_CicStrike strike = Object.FindObjectOfType<ACO_CicStrike>(true);
            if (console.Strike != strike) { console.Strike = strike; changed = true; }
            if (changed) { MarkChanged(console); }
            if (strike != null)
            {
                bool sc = false;
                if (strike.Mission != mission) { strike.Mission = mission; sc = true; }
                if (strike.Chatter != chatter) { strike.Chatter = chatter; sc = true; }
                if (strike.LaunchLineClip == null) { strike.LaunchLineClip = CicVoice("Cic_CruiseLaunch"); sc |= strike.LaunchLineClip != null; }
                if (strike.HomeBase != home) { strike.HomeBase = home; sc = true; }
                if (gen != null)
                {
                    if (strike.GroundLayers != gen.GroundLayers) { strike.GroundLayers = gen.GroundLayers; sc = true; }
                    float sea = gen.HexTerrain != null ? gen.HexTerrain.SeaLevel : 0f;
                    if (!Mathf.Approximately(strike.SeaLevel, sea)) { strike.SeaLevel = sea; sc = true; }
                }
                if (support != null)
                {
                    // CIC 편대 무전 음성(6-282): 호출 · 복귀는 새 파일, 격추는 지원 편대 것 그대로
                    if (support.CicLineClips == null || support.CicLineClips.Length < 3)
                    {
                        var grown = new AudioClip[3];
                        if (support.CicLineClips != null) { System.Array.Copy(support.CicLineClips, grown, support.CicLineClips.Length); }
                        support.CicLineClips = grown;
                        MarkChanged(support);
                    }
                    string[] wm = { "Cic/Cic_WingmenCall", "Support/Support_WingmenDown", "Cic/Cic_WingmenEnd" };
                    for (int i = 0; i < wm.Length; i++)
                    {
                        if (support.CicLineClips[i] != null) { continue; }
                        support.CicLineClips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/AWACSVoiceLines/{wm[i]}.mp3");
                        if (support.CicLineClips[i] != null) { MarkChanged(support); }
                    }
                    if (strike.DamageLayers != support.DamageLayers) { strike.DamageLayers = support.DamageLayers; sc = true; }
                    if (strike.WaterLayers != support.WaterLayers) { strike.WaterLayers = support.WaterLayers; sc = true; }
                }
                Transform[] cells = CruiserVlsCells();
                if (!SameTransforms(strike.LaunchPoints, cells)) { strike.LaunchPoints = cells; sc = true; }
                if (strike.Missiles != null)
                {
                    foreach (ACO_CicCruiseMissile m in strike.Missiles)
                    {
                        if (m != null && m.Strike != strike) { m.Strike = strike; MarkChanged(m); }
                    }
                }
                if (sc) { MarkChanged(strike); }
                if (cells.Length == 0) { Debug.LogWarning("[ACO] CIC 순항미사일: 아군 순양함(Ticonderoga)의 VLS 칸(Cell_*)을 찾지 못해 항모 자리에서 쏩니다.", strike); }
            }
            else { Debug.LogWarning("[ACO] CIC 순항미사일(ACO_CicStrike)이 씬에 없습니다 — Setup Check CIC 도구 [고치기]."); }
            if (map == null) { Debug.LogWarning("[ACO] CIC 콘솔: 전술 테이블(ACO_TacticalMap)이 없어 지도를 찍을 수 없습니다.", console); }
            if (uav == null) { Debug.LogWarning("[ACO] CIC 콘솔: 정찰 드론(ACO_CicUav)이 씬에 없습니다 — Setup Check [고치기].", console); }
            if (home == null) { Debug.LogWarning("[ACO] CIC 콘솔: 항모(전술 테이블의 HomeBase)가 없어 드론 · 편대가 공역 가운데에서 나옵니다.", console); }
        }

        ACO_GameFlow gameFlow = Object.FindObjectOfType<ACO_GameFlow>(true);
        ACO_UiSound uiSound = Object.FindObjectOfType<ACO_UiSound>(true);
        foreach (ACO_CicGogglesPanel p in panels)
        {
            bool changed = false;
            if (p.Flow != gameFlow) { p.Flow = gameFlow; changed = true; }
            if (p.Sound != uiSound) { p.Sound = uiSound; changed = true; }
            if (changed) { MarkChanged(p); }
        }
        ACO_SpectatorCam first = specs.Count > 0 ? specs[0] : null;
        foreach (ACO_CicGoggles g in goggles)
        {
            bool changed = false;
            if (g.Console != console) { g.Console = console; changed = true; }
            ACO_CicGogglesPanel own = g.GetComponentInParent<ACO_CicGogglesPanel>(true);
            if (own != null && g.Panel != own) { g.Panel = own; changed = true; }
            if (g.Spectator != first) { g.Spectator = first; changed = true; }
            if (g.Interior != interior) { g.Interior = interior; changed = true; }   // 함 밖 금지(6-287)
            if (changed) { MarkChanged(g); }
        }
        if (first == null && goggles.Count > 0) { Debug.LogWarning("[ACO] CIC 고글: 관전 패널(ACO_SpectatorPanel)이 없어 고글 화면이 나오지 않습니다(카메라를 빌려 쓴다).", goggles[0]); }
    }

    /// CIC 무전 음성(Audio/AWACSVoiceLines/Cic/{name}.mp3, 6-282). 없으면 null(자막만).
    private static AudioClip CicVoice(string name)
    {
        return AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/AWACSVoiceLines/Cic/{name}.mp3");
    }
}
