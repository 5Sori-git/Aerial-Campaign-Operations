using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using ACO;

/// 하늘·날씨·물·표시 레이어 — 전체 순서는 ACO_SceneBaker.BakeNeighbors.
public partial class ACO_SceneBaker
{
    /// FX 레이어: 내 머리를 따라다니는 화면 효과(하늘 안개 돔·물안개 막·비·물속 물빛 벽) — 관전 카메라가 그리지 않는다(멀리서 보면 엉뚱한 구가 보인다).
    /// 사용자 레이어가 모두 이름이 붙어 있어 SAV 경주용 Racing(28)을 같이 쓴다 — 이 효과들엔 콜라이더가 없어 경주 판정과 부딪히지 않고, 메인·VRChat 카메라엔 그대로 보인다.
    public const int FxLayer = 28;

    private static void BakeFxLayer()
    {
        var roots = new List<GameObject>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_CloudField c in root.GetComponentsInChildren<ACO_CloudField>(true))
            {
                if (c.SkyFog != null) { roots.Add(c.SkyFog.gameObject); }
                if (c.CloudVeil != null) { roots.Add(c.CloudVeil.gameObject); }
                if (c.CloudVolume != null) { roots.Add(c.CloudVolume.gameObject); }
            }
            foreach (ACO_Rain c in root.GetComponentsInChildren<ACO_Rain>(true))
            {
                if (c.RainRenderer != null) { roots.Add(c.RainRenderer.gameObject); }
                if (c.CanopyRenderer != null) { roots.Add(c.CanopyRenderer.gameObject); }
            }
            foreach (ACO_Underwater c in root.GetComponentsInChildren<ACO_Underwater>(true)) { if (c.Backdrop != null) { roots.Add(c.Backdrop.gameObject); } }
        }
        int moved = 0;
        foreach (GameObject r in roots)
        {
            foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer == FxLayer || t.GetComponent<Collider>() != null) { continue; }
                t.gameObject.layer = FxLayer;
                MarkChanged(t.gameObject);
                moved++;
            }
        }
        if (moved > 0) { Debug.Log($"[ACO] 화면 효과 {moved}개(하늘 안개 돔·물안개 막·비·물빛 벽)를 FX 레이어({FxLayer})로 옮겼습니다 — 관전 카메라가 그리지 않습니다."); }
    }

    /// 구름·날씨: 육각 지형 연결(지형 시드로 배치·날씨), 게임 진행에 날씨(작전 카드 표시).
    private static void BakeSky()
    {
        ACO_HexTerrain terrain = null;
        var fields = new List<ACO_CloudField>();
        var weathers = new List<ACO_Weather>();
        ACO_GameFlow flow = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            fields.AddRange(root.GetComponentsInChildren<ACO_CloudField>(true));
            weathers.AddRange(root.GetComponentsInChildren<ACO_Weather>(true));
            if (terrain == null) { terrain = root.GetComponentInChildren<ACO_HexTerrain>(true); }
            if (flow == null) { flow = root.GetComponentInChildren<ACO_GameFlow>(true); }
        }
        // 켜진 첫 번째 하늘만 연결한다. 켜고 끄기는 Setup Check가 맡는다
        ACO_Weather mainSky = weathers.Find(w => w.gameObject.activeInHierarchy);
        if (mainSky == null && weathers.Count > 0) { mainSky = weathers[0]; }
        if (weathers.FindAll(w => w.gameObject.activeInHierarchy).Count != 1 && weathers.Count > 0)
        {
            Debug.LogWarning("[ACO] 켜진 하늘(ACO_Sky)이 하나가 아닙니다 — Setup Check에서 확인하세요.", mainSky);
        }
        weathers.Clear();
        if (mainSky != null) { weathers.Add(mainSky); }
        foreach (ACO_CloudField f in fields.ToArray())
        {
            if (!f.gameObject.activeInHierarchy) { fields.Remove(f); }
        }
        foreach (ACO_HudSettings settings in Object.FindObjectsOfType<ACO_HudSettings>(true))
        {
            ACO_CloudField keep = weathers.Count > 0 ? weathers[0].Clouds : null;
            if (settings.Clouds != keep) { settings.Clouds = keep; MarkChanged(settings); }
        }
        foreach (ACO_Weather w in weathers)
        {
            if (terrain != null && w.Terrain != terrain) { w.Terrain = terrain; MarkChanged(w); }
            if (w.Sun == null) { w.Sun = ACO_SkyBuilder.FindSun(); if (w.Sun != null) { MarkChanged(w); } }
            Material[] windows = ACO_DecoBuilder.WindowMaterials();
            if (w.WindowMaterials == null || !System.Linq.Enumerable.SequenceEqual(w.WindowMaterials, windows))
            {
                w.WindowMaterials = windows;
                MarkChanged(w);
            }
            if (w.Lightning != null)
            {
                ACO_Lightning l = w.Lightning;
                ACO_MissionGenerator gen = Object.FindObjectOfType<ACO_MissionGenerator>(true);
                bool lc = false;
                if (l.Clouds != w.Clouds) { l.Clouds = w.Clouds; lc = true; }
                if (l.Sun != w.Sun) { l.Sun = w.Sun; lc = true; }
                if (w.Clouds != null && !Mathf.Approximately(l.FieldRadius, w.Clouds.FieldRadius)) { l.FieldRadius = w.Clouds.FieldRadius; lc = true; }
                if (gen != null && (l.SeaLevel != gen.SeaLevel || l.GroundLayers != gen.GroundLayers)) { l.SeaLevel = gen.SeaLevel; l.GroundLayers = gen.GroundLayers; lc = true; }
                if (lc) { MarkChanged(l); }
            }
            if (w.Rain != null)
            {
                ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
                if (w.Rain.Contacts != contacts) { w.Rain.Contacts = contacts; MarkChanged(w.Rain); }
            }
            // 비는 비구름층 윗면 아래에서만
            if (w.Rain != null && w.Clouds != null && !Mathf.Approximately(w.Rain.MaxAltitude, w.Clouds.DeckTop))
            {
                w.Rain.MaxAltitude = w.Clouds.DeckTop;
                MarkChanged(w.Rain);
            }
        }
        if (flow != null)
        {
            ACO_Weather w0 = weathers.Count > 0 ? weathers[0] : null;
            if (flow.Weather != w0) { flow.Weather = w0; MarkChanged(flow); }
        }
        foreach (ACO_CloudField f in fields)
        {
            if (terrain != null && f.Terrain != terrain) { f.Terrain = terrain; MarkChanged(f); }
            if (f.Clouds == null || f.Clouds.Length == 0)
            {
                Debug.LogWarning($"[ACO] 구름 '{f.name}'에 구름 풀이 없습니다 — ACO → Environment → Create Clouds and Rain으로 다시 만드세요.", f);
            }
            ACO_SkyBuilder.SyncSkyFogBase(f);
        }
    }


    /// 기총 탄이 수면 아래로 들어가면 사라지게: 탄 파티클의 Trigger 모듈에 씬의 Water 트리거(레이어 4 BoxCollider — 깊이 200m)를 넣는다.
    /// 파티클 Collision 모듈은 트리거 콜라이더에 막히지 않고 플레이어 기총은 그 모듈을 명중 판정에 쓰므로, 따로 도는 Trigger 모듈을 쓴다.
    /// 프레임마다 "안에 있는가"를 보므로 상자가 두꺼워야 빠른 탄이 건너뛰지 않는다. 대상: NPC 예광탄, 플레이어 기총 탄(ACO_GunDamageBonus가 붙은 파티클).
    private static void BakeGunWater()
    {
        BoxCollider water = null;
        foreach (BoxCollider c in Object.FindObjectsOfType<BoxCollider>(true))
        {
            if (!c.isTrigger || !c.enabled || c.gameObject.layer != 4) { continue; }
            if (water == null || c.bounds.size.x > water.bounds.size.x) { water = c; }
        }
        ACO_MissionGenerator generator = Object.FindObjectOfType<ACO_MissionGenerator>(true);
        float sea = generator != null ? generator.SeaLevel : 0f;
        if (water == null || Mathf.Abs(water.bounds.max.y - sea) > 5f || water.bounds.size.y < 50f)
        {
            Debug.LogWarning("[ACO] 수면 트리거(레이어 4 BoxCollider, 윗면 = 해수면, 깊이 50m 이상)를 찾지 못해 기총 탄을 수면에서 지우지 못합니다.", water);
            return;
        }
        var systems = new List<ParticleSystem>();
        foreach (ACO_NPCGun gun in Object.FindObjectsOfType<ACO_NPCGun>(true)) { if (gun.VisualParticle != null) { systems.Add(gun.VisualParticle); } }
        foreach (ACO_GunDamageBonus bonus in Object.FindObjectsOfType<ACO_GunDamageBonus>(true))
        {
            ParticleSystem ps = bonus.GetComponent<ParticleSystem>();
            if (ps != null) { systems.Add(ps); }
        }
        int changed = 0;
        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.TriggerModule trigger = ps.trigger;
            if (trigger.enabled && trigger.colliderCount == 1 && trigger.GetCollider(0) == water && trigger.inside == ParticleSystemOverlapAction.Kill) { continue; }
            trigger.enabled = true;
            trigger.SetCollider(0, water);
            trigger.inside = ParticleSystemOverlapAction.Kill;
            trigger.enter = ParticleSystemOverlapAction.Kill;
            trigger.outside = ParticleSystemOverlapAction.Ignore;
            trigger.exit = ParticleSystemOverlapAction.Ignore;
            MarkChanged(ps);
            changed++;
        }
        if (changed > 0) { Debug.Log($"[ACO] 기총 탄 파티클 {changed}개가 수면에서 사라지게 했습니다(Trigger 모듈 → {water.name})."); }
    }

    /// 물속 연출(ACO_Underwater, 메뉴 ACO → Environment → Add Water Effects가 만든다): 해수면 = 미션 생성기 SeaLevel, 안개는 구름 필드와 같이 쓴다.
    private static void BakeUnderwater()
    {
        ACO_Underwater uw = null;
        ACO_MissionGenerator gen = null;
        ACO_CloudField clouds = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (uw == null) { uw = root.GetComponentInChildren<ACO_Underwater>(true); }
            if (gen == null) { gen = root.GetComponentInChildren<ACO_MissionGenerator>(true); }
            if (clouds == null) { clouds = root.GetComponentInChildren<ACO_CloudField>(true); }
        }
        if (uw == null) { return; }
        bool changed = false;
        if (gen != null && uw.SeaLevel != gen.SeaLevel) { uw.SeaLevel = gen.SeaLevel; changed = true; }
        if (uw.Clouds != clouds) { uw.Clouds = clouds; changed = true; }
        // 너울 원판(ACO_OceanSwell) — 셰이더와 같은 너울 높이로 물속을 판정하고 물속에선 뒷면을 그린다. 너울을 지웠으면 비운다(평평한 해수면).
        Renderer swell = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name == ACO_OceanLook.SwellObjectName) { swell = r; break; }
            }
            if (swell != null) { break; }
        }
        if (uw.SwellRenderer != swell) { uw.SwellRenderer = swell; changed = true; }
        if (changed) { MarkChanged(uw); }
    }
}
