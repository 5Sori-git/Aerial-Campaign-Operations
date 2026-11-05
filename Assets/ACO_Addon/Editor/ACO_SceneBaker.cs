using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using ACO;

/// 씬의 NPC 참조를 빌드 시점에 미리 구워둔다 — SAV가 SaccEntity.AAMTargets를 굽는 것과 같은 방식.
/// 런타임에 주변 NPC를 찾는 비용(OverlapSphere 등)이 없고, NPC를 미리 배치하는 풀링 구조와도 맞는다.
public class ACO_SceneBaker : IVRCSDKBuildRequestedCallback
{
    // SAV의 셋업(10) 다음에 실행한다.
    public int callbackOrder => 11;

    public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
    {
        BakeNeighbors();
        return true;
    }

    /// 물리 충돌을 만드는 콜라이더(트리거·바퀴 제외).
    private static Collider[] SolidColliders(Component root)
    {
        var list = new List<Collider>();
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger || c is WheelCollider) { continue; }
            list.Add(c);
        }
        return list.ToArray();
    }

    [MenuItem("ACO/Bake NPC References")]
    public static void BakeNeighbors()
    {
        var fighters = new List<ACO_NPCFighterAI>();
        var directors = new List<ACO_CombatDirector>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            // 풀에서 꺼져 있는 NPC도 포함해야 하므로 비활성까지 찾는다.
            fighters.AddRange(root.GetComponentsInChildren<ACO_NPCFighterAI>(true));
            directors.AddRange(root.GetComponentsInChildren<ACO_CombatDirector>(true));
        }

        // 숙련도 체력 배율의 기준(베테랑 최대 체력): 비었으면 SaccTarget 체력(프리팹 값). 일반 전투기의 예전 기본 100(빈 칸을 채운 값 포함)은 150으로.
        int healthFilled = 0;
        foreach (ACO_NPCFighterAI f in fighters)
        {
            // 표시 약어: 공격기인데 기본값(FTR)이면 ATK
            if (f.GroundAttacker && (string.IsNullOrEmpty(f.MarkerLabel) || f.MarkerLabel == "FTR")) { f.MarkerLabel = "ATK"; MarkChanged(f); }
            if (f.TargetHealth == null) { continue; }
            float v = f.BaseHealth > 0f ? f.BaseHealth : f.TargetHealth.Health;
            if (f.SuperAce == null && !f.GroundAttacker && v == 100f) { v = 150f; }
            if (v <= 0f || v == f.BaseHealth) { continue; }
            f.BaseHealth = v;
            MarkChanged(f);
            healthFilled++;
        }
        if (healthFilled > 0) { Debug.Log($"[ACO] NPC 전투기 {healthFilled}기의 기준 체력(BaseHealth, 베테랑)을 채웠습니다(일반 전투기 150) — 숙련도 배율(생성기 TierHealthMultiplier)을 곱해 쓴다."); }
        // 체력 배율 예전 기본값(0.8/1/1.5, 1/1.5/2)으로 저장된 생성기는 새 기본값(0.66/1/1.33 — 신병 99·베테랑 150·에이스 199.5)으로. 직접 바꾼 값은 그대로.
        // 안전지대 여유 예전 기본값(500m)도 새 기본값(5km — 지상 유닛·적 함선 모두)으로.
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissionGenerator g in root.GetComponentsInChildren<ACO_MissionGenerator>(true))
            {
                if (g.SafeZoneMargin == 500f)
                {
                    g.SafeZoneMargin = 5000f;
                    MarkChanged(g);
                    Debug.Log("[ACO] 미션 생성기 안전지대 여유를 500m → 5km로 바꿨습니다(지상 유닛·적 함선 모두).", g);
                }
                // 공중 호위 순항 고도: 잠깐 2100m로 올렸던 것을 1800m로 되돌린다(구름층 판은 셰이더가 기체 둘레를 비워 해결 — 고도는 그대로)
                if (g.EscortAltitude == 2100f)
                {
                    g.EscortAltitude = 1800f;
                    MarkChanged(g);
                    Debug.Log("[ACO] 공중 호위 순항 고도를 1800m로 되돌렸습니다.", g);
                }
                // 상륙전 해안 곡사포: 예전 기본값(상륙 지점 뒤 2~3.5km)은 LCAC가 사거리에 잘 안 들어왔다 → 1.2~2.5km
                if (g.ShoreArtilleryMin == 2000f && g.ShoreArtilleryMax == 3500f)
                {
                    g.ShoreArtilleryMin = 1200f;
                    g.ShoreArtilleryMax = 2500f;
                    MarkChanged(g);
                    Debug.Log("[ACO] 상륙전 해안 곡사포 거리를 2~3.5km → 1.2~2.5km로 바꿨습니다.", g);
                }
                float[] t = g.TierHealthMultiplier;
                if (t == null || t.Length != 3) { continue; }
                bool old1 = t[0] == 0.8f && t[1] == 1f && t[2] == 1.5f;
                bool old2 = t[0] == 1f && t[1] == 1.5f && t[2] == 2f;
                if (!old1 && !old2) { continue; }
                g.TierHealthMultiplier = new float[] { 0.66f, 1f, 1.33f };
                MarkChanged(g);
                Debug.Log("[ACO] 미션 생성기 숙련도 체력 배율을 0.66 / 1 / 1.33으로 바꿨습니다(베테랑 150 기준 — 신병 99·에이스 199.5).", g);
            }
        }

        ACO_CombatDirector director = directors.Count > 0 ? directors[0] : null;
        if (directors.Count > 1)
        {
            Debug.LogWarning($"[ACO] CombatDirector가 {directors.Count}개 있습니다. '{director.name}' 하나만 사용합니다.", director);
        }

        // 공중 충돌 방지: 플레이어 기체(강화 확장이 있는 기체)와 NPC 전투기의 충돌 콜라이더
        var playerUpgrades = new List<ACO_VehicleUpgrades>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            playerUpgrades.AddRange(root.GetComponentsInChildren<ACO_VehicleUpgrades>(true));
        }
        foreach (ACO_VehicleUpgrades up in playerUpgrades)
        {
            Component body = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            up.BodyColliders = SolidColliders(body != null ? body : up);
            up.NPCFighters = fighters.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(up);
            EditorUtility.SetDirty(up);
        }

        int npcColliders = 0;
        int maxNpcColliders = 0;
        foreach (ACO_NPCFighterAI fighter in fighters)
        {
            Component npcBody = fighter.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            fighter.BodyColliders = SolidColliders(npcBody != null ? npcBody : fighter);
            fighter.PlayerVehicles = playerUpgrades.ToArray();
            npcColliders += fighter.BodyColliders.Length;
            maxNpcColliders = Mathf.Max(maxNpcColliders, fighter.BodyColliders.Length);

            var others = new List<ACO_NPCFighterAI>(fighters.Count);
            foreach (ACO_NPCFighterAI other in fighters)
            {
                if (other != fighter) { others.Add(other); }
            }

            fighter.Neighbors = others.ToArray();
            fighter.Director = director;
            PrefabUtility.RecordPrefabInstancePropertyModifications(fighter);
            EditorUtility.SetDirty(fighter);
        }

        // 다른 공중 유닛(폭격기·수송기·아군 공격헬기 = 항로 비행 ACO_EscortUnit MOVE_AIR, 적 헬기 = ACO_MobileUnit KIND_HELICOPTER)도 같은 규칙으로 —
        // 충돌 방지 컴포넌트(ACO_AirCollisionIgnore)를 유닛 루트에 붙이고 콜라이더·플레이어 기체를 채운다.
        var airUnits = new List<ACO_AirCollisionIgnore>();
        var airRoots = new List<GameObject>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_EscortUnit e in root.GetComponentsInChildren<ACO_EscortUnit>(true)) { if (e.MoveMode == ACO_EscortUnit.MOVE_AIR) { airRoots.Add(e.gameObject); } }
            foreach (ACO_MobileUnit m in root.GetComponentsInChildren<ACO_MobileUnit>(true)) { if (m.Kind == ACO_MobileUnit.KIND_HELICOPTER) { airRoots.Add(m.gameObject); } }
        }
        int added = 0;
        foreach (GameObject go in airRoots)
        {
            ACO_AirCollisionIgnore ign = go.GetComponent<ACO_AirCollisionIgnore>();
            if (ign == null) { ign = go.AddUdonSharpComponent<ACO_AirCollisionIgnore>(); added++; }
            ign.BodyColliders = SolidColliders(go.transform);
            ign.PlayerVehicles = playerUpgrades.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(ign);
            EditorUtility.SetDirty(ign);
            airUnits.Add(ign);
        }
        foreach (ACO_VehicleUpgrades up in playerUpgrades)
        {
            up.OtherAirUnits = airUnits.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(up);
            EditorUtility.SetDirty(up);
        }
        if (airUnits.Count > 0 && playerUpgrades.Count > 0)
        {
            Debug.Log($"[ACO] 공중 충돌 방지: 헬기·폭격기·수송기 등 {airUnits.Count}대" + (added > 0 ? $"(새로 붙임 {added})" : "") + " — 내 조종 기체와 서로 통과(충돌 데미지 없음).");
        }

        if (fighters.Count > 0 && playerUpgrades.Count > 0)
        {
            int maxPlayer = 0;
            foreach (ACO_VehicleUpgrades up in playerUpgrades) { maxPlayer = Mathf.Max(maxPlayer, up.BodyColliders.Length); }
            Debug.Log($"[ACO] 공중 충돌 방지: 플레이어 기체 {playerUpgrades.Count}대(한 대 최대 콜라이더 {maxPlayer}개) × NPC 전투기 {fighters.Count}대(콜라이더 {npcColliders}개). "
                + $"내 기체만 건다 — NPC가 켜질 때·탑승 때 프레임당 IgnoreCollision 최대 {maxPlayer * maxNpcColliders}회, 그 뒤 비용 없음.");
        }

        if (director != null)
        {
            director.Fighters = fighters.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(director);
            EditorUtility.SetDirty(director);
        }

        string directorInfo = director != null ? $"CombatDirector '{director.name}' 연결" : "CombatDirector 없음 — 공격 정원 제한 없이 동작";
        Debug.Log($"[ACO] NPC 전투기 {fighters.Count}기의 주변 NPC 목록을 베이크했습니다. {directorInfo}.");

        BakeMissionGenerators();
        // 새 예제 무장 정의를 만들고(코드에 더해진 예제만), 기체 특수무장 슬롯에 없는 모듈을 넣고, 격납고 목록을 정의에 맞춘다
        ACO_SpecialWeaponCatalog.InstallForBake();
        BakePlayerMissiles();
        BakePlayerProgress();
        ACO_HUDBuilder.EnsureConsoleFreeFlightButton();
        ACO_HUDBuilder.EnsureBoardLayout();
        ACO_HUDBuilder.EnsureHangarStyle();
        ACO_HUDBuilder.EnsureSpectatorIconGap();
        BakeGameFlow();
        BakePhaseModules();
        BakeFortress();
        BakeTimeSensitive();
        BakeFleet();
        BakeRailgun();
        BakeTierHealth();
        BakeTacticalMaps();
        BakeHudSettings();
        BakeRadio();
        BakeRwr();
        BakeCiws();
        BakeUnderwater();
        BakeGunWater();
        BakeDebugPanels();
        BakeUiLayers();
        BakeRadar();
        BakeEcm();
        BakeDeckTaxi();
        BakeVehicleMarks();
        BakeBulletFlyby();
        BakeCatapultCrew();
        BakeSeatRecenter();
        BakeUiSound();
        BakeSuperAces();
        BakeSky();
        BakeTargetAnimators();
        BakeFxLayer();
        BakeSpectators();
        BakeFlightHud();
        BakeDecals();
    }

    /// 데칼: 스튜디오(격납고) 미리보기 프로젝터를 샘플 기체 기준점 아래로, 샘플 메시에 콜라이더(데칼 건이 겨눈다 — 없는 것만 MeshCollider),
    /// 플레이어 기체마다 ACO_VehicleDecal(기준점 = SaccEntity 루트 자식 "ACO_Decals" + 프로젝터 2개, 머티리얼은 기체마다 따로) ↔ 스튜디오·기체 강화 확장.
    private static void BakeDecals()
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        var decals = new List<ACO_VehicleDecal>();
        int made = 0;
        int n = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_VehicleUpgrades up in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true))
            {
                SaccFlightAndVehicles.SaccEntity e = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
                if (e == null) { continue; }
                Transform holder = e.transform.Find("ACO_Decals");
                ACO_VehicleDecal vd = holder != null ? holder.GetComponent<ACO_VehicleDecal>() : null;
                if (vd == null)
                {
                    var go = new GameObject("ACO_Decals");
                    Undo.RegisterCreatedObjectUndo(go, "ACO decals");
                    go.transform.SetParent(e.transform, false);
                    vd = go.AddUdonSharpComponent<ACO_VehicleDecal>();
                    var pr = new Transform[2];
                    var mats = new Material[2];
                    for (int i = 0; i < 2; i++)
                    {
                        Material m;
                        // 기체 기준점 자식(ACO_Decals는 원점) — 저장된 자리가 곧 기준점 기준 상대값
                        pr[i] = ACO_HUDBuilder.CreateDecalProjector(go.transform, "Decal_" + (i + 1), "ACO_Decal_V" + n + "_" + i, out m);
                        mats[i] = m;
                    }
                    vd.Projectors = pr;
                    vd.Materials = mats;
                    if (studio != null) { vd.DecalKey = studio.DecalKey; }
                    made++;
                }
                if (vd.Studio != studio) { vd.Studio = studio; MarkChanged(vd); }
                if (up.Decal != vd) { up.Decal = vd; MarkChanged(up); }
                decals.Add(vd);
                n++;
            }
        }
        if (studio == null)
        {
            if (made > 0) { Debug.LogWarning("[ACO] 데칼: 플레이어 기체에 데칼을 달았지만 씬에 데칼 스튜디오(ACO_DecalStudio — 프리팹 생성 창)가 없어 이미지를 받을 수 없습니다."); }
            return;
        }
        studio.VehicleDecals = decals.ToArray();
        MarkChanged(studio);
        if (studio.SampleRoot == null)
        {
            Debug.LogWarning("[ACO] 데칼 스튜디오: SampleRoot(격납고 샘플 기체 기준점 — 실기체 SaccEntity 루트와 같은 자리·같은 모델)를 넣으세요.", studio);
        }
        else
        {
            // 미리보기 프로젝터를 샘플 기준점 아래로(상대 자리 = 실기체와 같은 기준)
            if (studio.PreviewProjectors != null)
            {
                foreach (Transform p in studio.PreviewProjectors)
                {
                    if (p != null && p.parent != studio.SampleRoot) { Undo.SetTransformParent(p, studio.SampleRoot, "ACO decal preview"); p.localPosition = Vector3.zero; p.localRotation = Quaternion.identity; MarkChanged(p); }
                }
            }
            // 데칼 건이 겨눌 콜라이더(없는 메시만)
            int cols = 0;
            foreach (MeshFilter mf in studio.SampleRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) { continue; }
                MeshCollider mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
                mc.sharedMesh = mf.sharedMesh;
                cols++;
            }
            if (cols > 0) { Debug.Log($"[ACO] 데칼 스튜디오: 샘플 기체 메시 {cols}개에 콜라이더를 붙였습니다(데칼 건이 겨눈다).", studio); }
            foreach (ACO_VehicleDecal vd in decals)
            {
                if (vd.DecalKey != studio.DecalKey && vd.DecalKey == "default") { vd.DecalKey = studio.DecalKey; MarkChanged(vd); }
            }
        }
        if (made > 0) { Debug.Log($"[ACO] 데칼: 플레이어 기체 {made}대에 데칼(프로젝터 2개)을 달았습니다 — 기종 DecalKey가 스튜디오와 같아야 그 자리를 쓴다.", studio); }
    }

    /// 비행 HUD(ACO_FlightHud): 플레이어 기체(ACO_VehicleUpgrades가 있는 SaccEntity)마다 SAV 비행·랜딩기어·플랩·후크·브레이크·기총·플레어,
    /// ACO 기본 미사일(특수무장 슬롯 밖)·특수무장 슬롯, 기총 탄도(파티클 탄속·중력·물려받기·발사 속도)를 같은 순서로 채운다. 레이더 접촉 목록·거리 단위를 잇고,
    /// 거리 단위를 표적 마커·페이즈 모듈·HUD 설정에, HideSavHud면 SAV HUD(SAV_HUDController)를 크기 0으로(끄면 다시 1로) 둔다.
    private static void BakeFlightHud()
    {
        ACO_HUDBuilder.EnsureFlightHudLayout();
        ACO_FlightHud hud = Object.FindObjectOfType<ACO_FlightHud>(true);
        ACO_Units units = hud != null && hud.Units != null ? hud.Units : Object.FindObjectOfType<ACO_Units>(true);
        ACO_TacName tac = Object.FindObjectOfType<ACO_TacName>(true);
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_TargetMarkers c in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
            {
                if (c.Units != units || c.TacNames != tac) { c.Units = units; c.TacNames = tac; MarkChanged(c); }
            }
            foreach (ACO_PhaseModule c in root.GetComponentsInChildren<ACO_PhaseModule>(true)) { if (c.Units != units) { c.Units = units; MarkChanged(c); } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true))
            {
                if (c.Units != units || c.FlightHud != hud) { c.Units = units; c.FlightHud = hud; MarkChanged(c); }
            }
        }
        if (hud == null) { return; }
        bool changed = false;
        if (hud.Units != units) { hud.Units = units; changed = true; }
        ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
        if (hud.Contacts != contacts) { hud.Contacts = contacts; changed = true; }
        if (contacts == null) { Debug.LogWarning("[ACO] 비행 HUD: 레이더 접촉 목록(ACO_RadarContacts — HUD 레이더와 같은 오브젝트)이 없어 내 기체를 알 수 없습니다.", hud); }

        var vehicles = new List<SaccFlightAndVehicles.SaccEntity>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_VehicleUpgrades up in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true))
            {
                SaccFlightAndVehicles.SaccEntity e = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
                if (e != null && !vehicles.Contains(e)) { vehicles.Add(e); }
            }
        }
        int n = vehicles.Count;
        var air = new SaccFlightAndVehicles.SaccAirVehicle[n];
        var gears = new SaccFlightAndVehicles.DFUNC_Gear[n];
        var flaps = new SaccFlightAndVehicles.DFUNC_Flaps[n];
        var hooks = new SaccFlightAndVehicles.DFUNC_Hook[n];
        var brakes = new SaccFlightAndVehicles.DFUNC_Brake[n];
        var guns = new SaccFlightAndVehicles.DFUNC_Gun[n];
        var flares = new SaccFlightAndVehicles.DFUNC_Flares[n];
        var missiles = new ACO_DFUNC_Missile[n];
        var specials = new ACO_DFUNC_Special[n];
        var gunSpeed = new float[n];
        var gunGravity = new float[n];
        var gunInherit = new float[n];
        var gunRate = new float[n];
        int hidden = 0;
        int savHuds = 0;
        // Bake가 끈 SAV HUD 자식(다시 보이게 할 때 이것만 켠다 — 원래 꺼져 있던 건 건드리지 않는다)
        var hiddenParts = new List<GameObject>();
        if (hud.HiddenSavHudParts != null) { foreach (GameObject g in hud.HiddenSavHudParts) { if (g != null) { hiddenParts.Add(g); } } }
        for (int i = 0; i < n; i++)
        {
            SaccFlightAndVehicles.SaccEntity e = vehicles[i];
            air[i] = e.GetComponentInChildren<SaccFlightAndVehicles.SaccAirVehicle>(true);
            gears[i] = e.GetComponentInChildren<SaccFlightAndVehicles.DFUNC_Gear>(true);
            flaps[i] = e.GetComponentInChildren<SaccFlightAndVehicles.DFUNC_Flaps>(true);
            hooks[i] = e.GetComponentInChildren<SaccFlightAndVehicles.DFUNC_Hook>(true);
            brakes[i] = e.GetComponentInChildren<SaccFlightAndVehicles.DFUNC_Brake>(true);
            guns[i] = e.GetComponentInChildren<SaccFlightAndVehicles.DFUNC_Gun>(true);
            flares[i] = e.GetComponentInChildren<SaccFlightAndVehicles.DFUNC_Flares>(true);
            specials[i] = e.GetComponentInChildren<ACO_DFUNC_Special>(true);
            foreach (ACO_DFUNC_Missile m in e.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (m.GetComponentInParent<ACO_DFUNC_Special>(true) == null) { missiles[i] = m; break; }
            }
            // 기총 탄도: 데미지 파티클의 시작 속도·중력·속도 물려받기·초당 방출
            gunSpeed[i] = 900f;
            gunInherit[i] = 1f;
            ParticleSystem ps = guns[i] != null && guns[i].GunDamageParticle != null ? guns[i].GunDamageParticle.GetComponent<ParticleSystem>() : null;
            if (ps != null)
            {
                ParticleSystem.MainModule main = ps.main;
                gunSpeed[i] = main.startSpeed.mode == ParticleSystemCurveMode.TwoConstants ? main.startSpeed.constantMax : main.startSpeed.constant;
                gunGravity[i] = main.gravityModifier.constant;
                ParticleSystem.InheritVelocityModule iv = ps.inheritVelocity;
                gunInherit[i] = iv.enabled ? iv.curveMultiplier : 0f;
                gunRate[i] = ps.emission.rateOverTime.constant;
            }
            // SAV HUD 숨기기: SAV_HUDController의 자식(SF-2·FA-18의 bigstuff 등)을 끈다. 크기를 줄이면 안 된다 — SAV HUD는 머리에서 멀리 띄운 큰 판이라
            // 부모를 줄이면 거리와 크기가 같이 줄어 보이는 크기(시야각)가 그대로다(0으로 줄이면 SAV 속도 벡터 계산이 0 벡터가 돼 매 프레임 경고).
            // 컨트롤러 자체는 켜 둔다(무장 스크립트가 위치·distance_from_head를 읽는다). 꺼진 자식 안에서 기능이 켜고 끄는 HUD 요소도 안 보인다.
            foreach (SaccFlightAndVehicles.SAV_HUDController c in e.GetComponentsInChildren<SaccFlightAndVehicles.SAV_HUDController>(true))
            {
                savHuds++;
                if (c.transform.localScale != Vector3.one)
                {
                    // 예전 방식(크기 0·0.001)에서 되돌린다
                    Undo.RecordObject(c.transform, "ACO restore SAV HUD scale");
                    c.transform.localScale = Vector3.one;
                    MarkChanged(c.transform);
                }
                foreach (Transform child in c.transform)
                {
                    if (hud.HideSavHud)
                    {
                        if (!child.gameObject.activeSelf) { continue; }
                        Undo.RecordObject(child.gameObject, "ACO hide SAV HUD");
                        child.gameObject.SetActive(false);
                        MarkChanged(child.gameObject);
                        hiddenParts.Add(child.gameObject);
                        hidden++;
                    }
                    else if (hiddenParts.Contains(child.gameObject) && !child.gameObject.activeSelf)
                    {
                        Undo.RecordObject(child.gameObject, "ACO show SAV HUD");
                        child.gameObject.SetActive(true);
                        MarkChanged(child.gameObject);
                        hidden++;
                    }
                }
            }
        }
        if (!SameRefs(hud.Vehicles, vehicles.ToArray())) { hud.Vehicles = vehicles.ToArray(); changed = true; }
        hud.Air = air; hud.Gears = gears; hud.FlapFuncs = flaps; hud.Hooks = hooks; hud.Brakes = brakes;
        hud.Guns = guns; hud.FlareFuncs = flares; hud.Missiles = missiles; hud.Specials = specials;
        hud.GunSpeed = gunSpeed; hud.GunGravity = gunGravity; hud.GunInherit = gunInherit; hud.GunRate = gunRate;
        if (!hud.HideSavHud) { hiddenParts.Clear(); }
        hud.HiddenSavHudParts = hiddenParts.ToArray();
        MarkChanged(hud);
        // 바뀐 게 없어도 매번 상태를 알린다(이미 숨겨 둔 SAV HUD는 "숨긴 상태")
        string savState = hud.HideSavHud ? "숨김" : "보임";
        Debug.Log($"[ACO] 비행 HUD: 플레이어 기체 {n}대 연결, SAV HUD(SAV_HUDController) {savHuds}개 {savState} 상태"
            + (hidden > 0 ? $" — 이번에 {hidden}개를 " + (hud.HideSavHud ? "숨겼습니다" : "다시 보이게 했습니다") : " — 바꾼 것 없음") + ".", hud);
    }

    private static bool SameRefs(Object[] a, Object[] b)
    {
        if (a == null || a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
    }

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

    /// 관전 카메라 전용 빗방울: 비와 같은 메시·머티리얼을 카메라 자식으로, 메인 카메라가 그리지 않는 레이어(18)에 둔다. 비는 FX 레이어라 관전 카메라가 그리지 않는다.
    private static Renderer CreateSpectatorRain(Transform cam, Renderer source)
    {
        var go = new GameObject("SpectatorRain");
        Undo.RegisterCreatedObjectUndo(go, "ACO spectator rain");
        go.layer = ACO_HUDBuilder.SpectatorOnlyLayer;
        go.transform.SetParent(cam, false);
        go.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = source.sharedMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.enabled = false;
        Debug.Log("[ACO] 관전 카메라 전용 비(SpectatorRain)를 만들었습니다.", go);
        return r;
    }

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
            if (s.RainRenderer == null && s.Cam != null && rain != null && rain.RainRenderer != null)
            {
                s.RainRenderer = CreateSpectatorRain(s.Cam.transform, rain.RainRenderer);
                changed = true;
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

    /// SaccTarget 애니메이터(Target.controller)는 'normal' 상태가 클립 없이 Write Defaults로 모델·락온 표적·콜라이더를 되살린다.
    /// 그런데 격추된 채(TargetExplode — 모델 끔, 파괴 모델 켬, 락온 표적 끔) 풀에서 꺼졌다가 다음 미션에 다시 켜지면 애니메이터가
    /// 다시 바인딩하면서 그 '파괴된 모습'을 기본값으로 잡는다 → 부활해도 파괴 모델이 보이고 락온이 안 되는 유닛이 된다.
    /// Keep Animator State On Disable을 켜면 다시 켤 때 바인딩·상태를 그대로 두므로 처음(살아 있을 때) 잡은 기본값이 유지된다.
    private static void BakeTargetAnimators()
    {
        int fixedCount = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (SaccFlightAndVehicles.SaccTarget t in root.GetComponentsInChildren<SaccFlightAndVehicles.SaccTarget>(true))
            {
                Animator a = t.GetComponent<Animator>();
                if (a == null || a.keepAnimatorStateOnDisable) { continue; }
                a.keepAnimatorStateOnDisable = true;
                MarkChanged(a);
                fixedCount++;
            }
        }
        if (fixedCount > 0) { Debug.Log($"[ACO] SaccTarget 애니메이터 {fixedCount}개에 Keep Animator State On Disable을 켰습니다(풀 재사용 때 파괴된 모습이 기본값으로 잡히지 않게)."); }
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
        // ACO_Sky가 둘이면 날씨가 따로 돌아 디버그 패널·게임 진행이 한쪽만 바꾸고, 다른 쪽 구름층이 그대로 남는다.
        // 첫 번째만 쓰고 나머지는 끈다(지우지 않는다 — 켜면 돌아온다). 첫 번째가 꺼져 있으면 켠다 — 다 꺼지면 날씨가 돌지 않아 맑음에 멈춘다
        if (weathers.Count > 0 && !weathers[0].gameObject.activeSelf)
        {
            GameObject first = weathers[0].gameObject;
            Undo.RecordObject(first, "ACO sky");
            first.SetActive(true);
            MarkChanged(first);
            Debug.LogWarning($"[ACO] 하늘(ACO_Sky) '{first.transform.parent?.name}/{first.name}'이 꺼져 있어 켰습니다 — 꺼져 있으면 날씨가 돌지 않아 맑음에서 바뀌지 않습니다.");
        }
        for (int i = weathers.Count - 1; i >= 1; i--)
        {
            GameObject extra = weathers[i].gameObject;
            if (extra.activeSelf)
            {
                Undo.RecordObject(extra, "ACO duplicate sky");
                extra.SetActive(false);
                MarkChanged(extra);
                Debug.LogWarning($"[ACO] 하늘(ACO_Sky)이 {weathers.Count}개라 '{extra.transform.parent?.name}/{extra.name}'을 껐습니다 — 날씨가 따로 돌아 구름층이 남던 원인입니다. 필요 없으면 지워도 됩니다.");
            }
            weathers.RemoveAt(i);
        }
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
            // 구름층(비·폭풍 = 비구름층, 안개 = 낮은 안개층) — 예전 씬엔 칸이 없거나 4칸(폭풍 전)
            if (w.CloudLayer == null || w.CloudLayer.Length < 4)
            {
                w.CloudLayer = new[] { ACO_CloudField.LAYER_NONE, ACO_CloudField.LAYER_NONE, ACO_CloudField.LAYER_FOG, ACO_CloudField.LAYER_DECK, ACO_CloudField.LAYER_DECK };
                MarkChanged(w);
            }
            // 폭풍(WX_STORM): 날씨별 배열이 4칸이면 폭풍 값을 붙인다(기존 4칸 값은 그대로)
            if (MigrateStorm(w)) { MarkChanged(w); Debug.Log("[ACO] 날씨: 폭풍(STORM)을 더했습니다 — 비율 0.8, 더 어두운 비구름층 + 센 비 + 번개.", w); }
            // 캐노피 빗방울·번개(폭풍) — 예전 씬엔 없다
            if (ACO_SkyBuilder.EnsureStormParts(w)) { Debug.Log("[ACO] 날씨: 캐노피 빗방울·번개(낙뢰 줄기·천둥)를 만들었습니다.", w); }
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
            // 예전 구름 속 안개 기본값 → 더 일찍·짙게(퍼프가 사라지기 전에 안개가 먼저 차오르게)
            bool fogChanged = false;
            // 뭉게구름 속만 짙게(0.006 — 퍼프가 사라지는 거리를 덮는다), 비구름층·안개층 속은 시정 약 2km(0.001) — 예전 기본값만 바꾼다
            if (Mathf.Approximately(f.InsideFogDensity, 0.0035f) || Mathf.Approximately(f.InsideFogDensity, 0.001f)) { f.InsideFogDensity = 0.006f; fogChanged = true; }
            if (Mathf.Approximately(f.DeckFogDensity, 0.006f)) { f.DeckFogDensity = 0.001f; fogChanged = true; }
            if (Mathf.Approximately(f.FogLayerDensity, 0.004f)) { f.FogLayerDensity = 0.001f; fogChanged = true; }
            if (Mathf.Approximately(f.FogOuter, 1.25f)) { f.FogOuter = 1.6f; fogChanged = true; }
            if (Mathf.Approximately(f.FogInner, 0.7f)) { f.FogInner = 0.9f; fogChanged = true; }
            if (fogChanged) { MarkChanged(f); }
            // 하늘 안개 돔·구름층 판·물안개 막(예전 씬엔 없다)
            if (ACO_SkyBuilder.EnsureSkyParts(f)) { Debug.Log("[ACO] 하늘 안개 돔·비구름층·안개층·물안개 막을 만들었습니다(ACO_Sky 아래).", f); }
            ACO_SkyBuilder.SyncSkyFogBase(f);
            // 물안개 막: 뭉게구름 속은 진하게(0.55), 비구름층 속은 스크립트가 DeckVeilStrength로 옅게 — 잠깐 쓰던 0.3이면 되돌린다
            Material veil = f.CloudVeil != null ? f.CloudVeil.sharedMaterial : null;
            if (veil != null && veil.HasProperty("_Opacity") && Mathf.Approximately(veil.GetFloat("_Opacity"), 0.3f))
            {
                veil.SetFloat("_Opacity", 0.55f);
                EditorUtility.SetDirty(veil);
            }
        }
    }

    /// 슈퍼에이스 ↔ 같은 기체의 AI(AI.SuperAce가 있어야 생성기가 일반 배치에서 뺀다). 생성기 풀에 없으면 알린다.
    private static void BakeSuperAces()
    {
        var generators = new List<ACO_MissionGenerator>();
        var aces = new List<ACO_SuperAce>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            generators.AddRange(root.GetComponentsInChildren<ACO_MissionGenerator>(true));
            aces.AddRange(root.GetComponentsInChildren<ACO_SuperAce>(true));
        }
        foreach (ACO_SuperAce ace in aces)
        {
            ACO_NPCFighterAI ai = ace.GetComponentInParent<ACO_NPCFighterAI>(true);
            if (ai == null) { Debug.LogWarning($"[ACO] 슈퍼에이스 '{ace.name}'이 NPC 전투기(ACO_NPCFighterAI) 안에 있지 않습니다.", ace); continue; }
            if (ai.SuperAce != ace) { ai.SuperAce = ace; MarkChanged(ai); }
            if (ace.AI != ai) { ace.AI = ai; MarkChanged(ace); }
            bool pooled = false;
            foreach (ACO_MissionGenerator g in generators) { if (g.Fighters != null && System.Array.IndexOf(g.Fighters, ai) >= 0) { pooled = true; } }
            if (!pooled) { Debug.LogWarning($"[ACO] 슈퍼에이스 '{ai.name}'이 미션 생성기 풀(Fighters)에 없습니다 — 생성기의 UnitPoolRoot 아래로 옮기세요(보너스 페이즈가 안 나온다).", ai); }
        }
    }

    /// 패널 버튼 효과음: 씬에 하나 만들고(없으면) 모든 패널 버튼에 연결한다.
    private static void BakeUiSound()
    {
        bool created;
        ACO_UiSound sound = ACO_HUDBuilder.EnsureUiSound(out created);
        if (created) { Debug.Log("[ACO] 패널 버튼 효과음(ACO_UiSound)을 만들었습니다 — 클립은 Generated/ACO_Ui*.wav(바꿔 끼워도 된다).", sound); }
        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_FlowButton c in root.GetComponentsInChildren<ACO_FlowButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_MissionStartButton c in root.GetComponentsInChildren<ACO_MissionStartButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_HangarButton c in root.GetComponentsInChildren<ACO_HangarButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_HudSettingButton c in root.GetComponentsInChildren<ACO_HudSettingButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_DebugButton c in root.GetComponentsInChildren<ACO_DebugButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_SpectatorButton c in root.GetComponentsInChildren<ACO_SpectatorButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            // 작전판 스크롤 버튼(예전 작전판엔 없다 — 만든다)
            foreach (ACO_MissionLog c in root.GetComponentsInChildren<ACO_MissionLog>(true))
            {
                if (ACO_HUDBuilder.EnsureLogScrollButtons(c)) { Debug.Log($"[ACO] 작전판 '{c.name}'에 스크롤 버튼(NEWER · OLDER · LATEST)을 달았습니다.", c); }
            }
            foreach (ACO_LogScrollButton c in root.GetComponentsInChildren<ACO_LogScrollButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_TacName c in root.GetComponentsInChildren<ACO_TacName>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_DecalButton c in root.GetComponentsInChildren<ACO_DecalButton>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
            foreach (ACO_Teleporter c in root.GetComponentsInChildren<ACO_Teleporter>(true)) { if (c.Sound != sound) { c.Sound = sound; MarkChanged(c); linked++; } }
        }
        if (linked > 0) { Debug.Log($"[ACO] 버튼 효과음을 버튼 {linked}개에 연결했습니다.", sound); }
    }

    /// 좌석 재조정 ↔ 점수 HUD(안내 문구).
    private static void BakeSeatRecenter()
    {
        ACO_ScoreHUD hud = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (hud == null) { hud = root.GetComponentInChildren<ACO_ScoreHUD>(true); }
        }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_SeatRecenter c in root.GetComponentsInChildren<ACO_SeatRecenter>(true)) { if (c.Hud != hud) { c.Hud = hud; MarkChanged(c); } }
        }
    }

    /// RWR ↔ 미사일 런처(NPC·SAM·플레이어 무장)와 HUD 설정 패널. 씬에 하나(표적 마커 아래, Bake가 만든다).
    private static void BakeRwr()
    {
        ACO_RWR rwr = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (rwr == null) { rwr = root.GetComponentInChildren<ACO_RWR>(true); }
        }
        if (rwr == null) { return; }
        // 예전 기본색(노랑)이면 HUD 초록으로(직접 바꾼 색은 그대로)
        var oldYellow = new Color(1f, 0.85f, 0.2f, 0.9f);
        if (ColorClose(rwr.FarColor, oldYellow))
        {
            rwr.FarColor = new Color(0.35f, 1f, 0.45f, 0.9f);
            rwr.LockColor = new Color(0.35f, 1f, 0.45f, 0.75f);
            MarkChanged(rwr);
            Debug.Log("[ACO] RWR 기본색을 노랑에서 HUD 초록으로 바꿨습니다(가까운 미사일은 그대로 빨강).", rwr);
        }
        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissileLauncher c in root.GetComponentsInChildren<ACO_MissileLauncher>(true)) { if (c.Rwr != rwr) { c.Rwr = rwr; MarkChanged(c); linked++; } }
            foreach (ACO_DFUNC_Missile c in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true)) { if (c.Rwr != rwr) { c.Rwr = rwr; MarkChanged(c); linked++; } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true)) { if (c.Rwr != rwr) { c.Rwr = rwr; MarkChanged(c); linked++; } }
        }
        if (linked > 0) { Debug.Log($"[ACO] RWR '{rwr.name}'을 {linked}곳에 연결했습니다.", rwr); }
    }

    /// 레이더 접촉 목록(HUD 레이더와 같은 오브젝트, 씬에 하나) ↔ 생성기·게임 진행, HUD 레이더 ↔ HUD 설정 패널,
    /// NPC 미사일 런처(날아가는 미사일 등록), 조종석 지도(기체마다).
    /// 플레이어 ECM: 기체의 ECM 모듈을 모으고, 재밍 판정을 적 런처·적 대공포에 잇는다. 아군 함선·헬기는 잇지 않는다 — 내 재밍에 아군이 묶이지 않게.
    /// 총탄이 스치는 소리: 레이더 아래에 한 벌 만들고, 나를 쏠 수 있는 기총과 잇는다.
    private static void BakeBulletFlyby()
    {
        ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
        if (contacts == null) { return; }
        ACO_BulletFlyby flyby = Object.FindObjectOfType<ACO_BulletFlyby>(true);
        if (flyby == null)
        {
            var go = new GameObject("BulletFlyby");
            go.transform.SetParent(contacts.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "ACO bullet flyby");
            flyby = go.AddUdonSharpComponent<ACO_BulletFlyby>();
            var sources = new AudioSource[3];
            for (int i = 0; i < sources.Length; i++)
            {
                var child = new GameObject("Source_" + i);
                child.transform.SetParent(go.transform, false);
                AudioSource s = child.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.dopplerLevel = 0f;
                sources[i] = s;
            }
            flyby.Sources = sources;
            ACO_SyncModeFixer.FixHierarchy(go, false);
            Debug.Log("[ACO] 총탄이 스치는 소리를 만들었습니다: BulletFlyby");
        }
        if (flyby.Contacts != contacts) { flyby.Contacts = contacts; MarkChanged(flyby); }
        if (flyby.Clips == null || flyby.Clips.Length == 0)
        {
            var clips = new List<AudioClip>();
            for (int i = 1; i <= 4; i++)
            {
                AudioClip c = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/Generated/ACO_Bullet_Whiz_{i}.wav");
                if (c != null) { clips.Add(c); }
            }
            flyby.Clips = clips.ToArray();
            MarkChanged(flyby);
        }
        foreach (ACO_NPCGun gun in Object.FindObjectsOfType<ACO_NPCGun>(true))
        {
            ACO_BulletFlyby want = IsFriendlyUnit(gun) ? null : flyby;
            if (gun.Flyby != want) { gun.Flyby = want; MarkChanged(gun); }
        }
    }

    /// 플레이어 기체의 표시(헐넘버·이름·킬마크)와 콜사인 패널의 헐넘버 칸.
    private static void BakeVehicleMarks()
    {
        ACO_HUDBuilder.EnsureTacHullPanel();
        // 먼저 샘플 기체에서 잡은 자리가 있으면 기체에 처음 넣을 때 그대로 쓴다
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        Transform template = studio != null && studio.SampleRoot != null ? studio.SampleRoot.Find("ACO_Marks/Marks") : null;
        SaccFlightAndVehicles.SaccEntity first = null;
        foreach (ACO_VehicleUpgrades up in Object.FindObjectsOfType<ACO_VehicleUpgrades>(true))
        {
            var entity = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            if (entity == null) { continue; }
            ACO_HUDBuilder.EnsureVehicleMarks(entity, template);
            if (first == null) { first = entity; }
        }
        if (studio != null) { ACO_HUDBuilder.EnsurePreviewMarks(studio.SampleRoot, first); }
    }

    /// 기체 자동화가 갑판 영역을 읽을 수 있게 진행도를 잇는다.
    private static void BakeDeckTaxi()
    {
        ACO_PlayerProgress progress = Object.FindObjectOfType<ACO_PlayerProgress>(true);
        if (progress == null) { return; }
        foreach (ACO_VehicleAutomation a in Object.FindObjectsOfType<ACO_VehicleAutomation>(true))
        {
            if (a.Progress != progress) { a.Progress = progress; MarkChanged(a); }
        }
    }

    /// 항모 프리팹 안의 이름으로 찾는다(인스턴스 값이 Transform에 있어 비활성도 찾는다).
    private static Transform FindSceneTransform(string name, Vector3 near)
    {
        Transform best = null;
        float bestDist = float.MaxValue;
        foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name != name && !t.name.StartsWith(name + " ")) { continue; }
            float d = (t.position - near).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = t; }
        }
        return best;
    }

    /// 가까운 JBD를 잇고 경첩(사출 방향 쪽 끝)을 잰다. 움직여야 하니 정적 배칭에서 뺀다.
    private static void BakeJbd(ACO_CatapultCrew crew)
    {
        if (crew.Trigger == null) { return; }
        Transform jbd = crew.Jbd != null ? crew.Jbd : FindSceneTransform("JBD", crew.transform.position);
        Renderer r = jbd != null ? jbd.GetComponent<Renderer>() : null;
        if (r == null) { Debug.LogWarning($"[ACO] {crew.name}: JBD를 찾지 못했습니다."); return; }
        Bounds b = r.bounds;
        Vector3 f = crew.transform.forward;
        Vector3 hinge = b.center + f * (Mathf.Abs(f.x) * b.extents.x + Mathf.Abs(f.z) * b.extents.z);
        if (crew.Jbd != jbd || crew.JbdHinge != hinge)
        {
            crew.Jbd = jbd;
            crew.JbdHinge = hinge;
            MarkChanged(crew);
            Debug.Log($"[ACO] {crew.name}: {jbd.name}을 이었습니다(거리 {Vector3.Distance(jbd.position, crew.transform.position):0.0}m, 판 크기 {b.size}).");
        }
        StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(jbd.gameObject);
        if ((flags & StaticEditorFlags.BatchingStatic) != 0)
        {
            GameObjectUtility.SetStaticEditorFlags(jbd.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
            MarkChanged(jbd.gameObject);
        }
    }

    /// 발함지휘소 요원: 처음 한 번 지휘소 바닥에 사출 방향을 보고 놓는다. 놓인 뒤의 자리는 건드리지 않는다.
    private static void BakeLaunchOperator(List<ACO_CatapultCrew> crews)
    {
        ACO_LaunchOperator op = Object.FindObjectOfType<ACO_LaunchOperator>(true);
        if (op == null)
        {
            Transform station = FindSceneTransform("発艦指揮所", Vector3.zero);
            GameObject prefab = ACO_PrefabBuilder.EnsureLaunchOperatorPrefab();
            if (station == null || prefab == null || crews.Count == 0) { Debug.LogWarning("[ACO] 발함지휘소 또는 요원 프리팹을 찾지 못했습니다."); return; }
            Renderer r = station.GetComponent<Renderer>();
            Bounds b = r != null ? r.bounds : new Bounds(station.position, Vector3.one);
            Vector3 pos = b.center;
            // 안쪽에서 아래로 쏴 바닥을 찾는다(없으면 바닥면)
            pos.y = Physics.Raycast(b.center, Vector3.down, out RaycastHit hit, b.extents.y + 1f, 2049) ? hit.point.y : b.min.y;
            GameObject holder = GameObject.Find("ACO_DeckCrew");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder != null ? holder.transform : null);
            Undo.RegisterCreatedObjectUndo(go, "ACO launch operator");
            go.name = "LaunchOperator";
            go.transform.SetPositionAndRotation(pos, crews[0].transform.rotation);
            MarkChanged(go.transform);
            op = go.GetComponent<ACO_LaunchOperator>();
            Debug.Log($"[ACO] 발함지휘소 요원을 놓았습니다: 위치 {pos}, 지휘소 크기 {b.size} — 어긋나면 씬에서 옮기세요.");
        }
        if (op.Crews == null || !new HashSet<ACO_CatapultCrew>(op.Crews).SetEquals(crews))
        {
            op.Crews = crews.ToArray();
            MarkChanged(op);
        }
    }

    /// 캐터펄트마다 갑판 요원을 한 벌 놓고 기체·HUD 설정과 잇는다. 놓인 뒤의 자리는 건드리지 않는다.
    private static void BakeCatapultCrew()
    {
        var triggers = new List<Transform>();
        foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name == "Trigger" && t.gameObject.layer == 24 && t.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true) == null) { triggers.Add(t); }
        }
        if (triggers.Count == 0) { return; }
        ACO_PrefabBuilder.EnsureCrewAnimations();
        GameObject prefab = ACO_PrefabBuilder.EnsureCatapultCrewPrefab();
        ACO_PrefabBuilder.EnsureCatapultCrewExtras();
        ACO_PrefabBuilder.EnsureCatapultCrewModels();
        if (prefab == null) { return; }
        ACO_HUDBuilder.EnsureHudDeckCrewCard();

        var crews = new List<ACO_CatapultCrew>(Object.FindObjectsOfType<ACO_CatapultCrew>(true));
        foreach (Transform trigger in triggers)
        {
            if (crews.Exists(c => c.Trigger == trigger)) { continue; }
            GameObject holder = GameObject.Find("ACO_DeckCrew");
            if (holder == null)
            {
                holder = new GameObject("ACO_DeckCrew");
                Undo.RegisterCreatedObjectUndo(holder, "ACO deck crew");
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            Undo.RegisterCreatedObjectUndo(go, "ACO deck crew");
            go.name = "CatapultCrew_" + (crews.Count + 1);
            Vector3 forward = trigger.forward;
            forward.y = 0f;
            Vector3 pos = trigger.position;
            // 2049 = 갑판도 기체 접지와 같은 마스크로 찾는다
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, 2049)) { pos = hit.point; }
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward));
            ACO_CatapultCrew crew = go.GetComponent<ACO_CatapultCrew>();
            crew.Trigger = trigger;
            MarkChanged(go.transform);
            MarkChanged(crew);
            crews.Add(crew);
            Debug.Log($"[ACO] 갑판 요원을 놓았습니다: {go.name}");
        }

        foreach (ACO_CatapultCrew crew in crews) { BakeJbd(crew); }
        BakeLaunchOperator(crews);

        var catapults = new List<ACO_DFUNC_Catapult>(Object.FindObjectsOfType<ACO_DFUNC_Catapult>(true));
        foreach (ACO_CatapultCrew crew in crews)
        {
            if (crew.Catapults == null || !new HashSet<ACO_DFUNC_Catapult>(crew.Catapults).SetEquals(catapults))
            {
                crew.Catapults = catapults.ToArray();
                MarkChanged(crew);
            }
        }
        foreach (ACO_DFUNC_Catapult cat in catapults)
        {
            if (cat.Crews == null || !new HashSet<ACO_CatapultCrew>(cat.Crews).SetEquals(crews))
            {
                cat.Crews = crews.ToArray();
                MarkChanged(cat);
            }
        }
        foreach (ACO_HudSettings settings in Object.FindObjectsOfType<ACO_HudSettings>(true))
        {
            if (settings.Crews == null || !new HashSet<ACO_CatapultCrew>(settings.Crews).SetEquals(crews))
            {
                settings.Crews = crews.ToArray();
                MarkChanged(settings);
            }
        }
    }

    private static void BakeEcm()
    {
        ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
        if (contacts == null) { return; }
        var pods = new List<ACO_DFUNC_Missile>();
        foreach (ACO_DFUNC_Missile m in Object.FindObjectsOfType<ACO_DFUNC_Missile>(true))
        {
            if (!m.EcmMode) { continue; }
            pods.Add(m);
            if (m.EcmHub != contacts) { m.EcmHub = contacts; MarkChanged(m); }
            // 먼저 설치된 모듈은 주 무장의 미사일이 들어가 있다
            if (m.Missile != null) { m.Missile = null; MarkChanged(m); }
        }
        if (contacts.EcmPods == null || !new HashSet<ACO_DFUNC_Missile>(contacts.EcmPods).SetEquals(pods))
        {
            contacts.EcmPods = pods.ToArray();
            MarkChanged(contacts);
        }
        // 구역 안에서 들리는 소리: 2D 루프 하나(내 기체가 구역 안일 때만 켠다)
        Transform soundObj = contacts.transform.Find("EcmSound");
        if (soundObj == null)
        {
            soundObj = new GameObject("EcmSound").transform;
            soundObj.SetParent(contacts.transform, false);
            Undo.RegisterCreatedObjectUndo(soundObj.gameObject, "ACO ECM sound");
        }
        AudioSource sound = soundObj.GetComponent<AudioSource>();
        if (sound == null) { sound = soundObj.gameObject.AddComponent<AudioSource>(); }
        if (sound.clip == null)
        {
            sound.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ACO_Addon/Audio/Generated/ACO_Ecm_Loop.wav");
            sound.loop = true;
            sound.playOnAwake = false;
            sound.spatialBlend = 0f;
            sound.volume = 0.45f;
            MarkChanged(sound);
        }
        if (contacts.EcmSound != sound) { contacts.EcmSound = sound; MarkChanged(contacts); }
        foreach (ACO_MissileLauncher l in Object.FindObjectsOfType<ACO_MissileLauncher>(true))
        {
            ACO_RadarContacts want = IsFriendlyUnit(l) ? null : contacts;
            if (l.EcmHub != want) { l.EcmHub = want; MarkChanged(l); }
        }
        foreach (ACO_AAGunSite aa in Object.FindObjectsOfType<ACO_AAGunSite>(true))
        {
            ACO_RadarContacts want = IsFriendlyUnit(aa) ? null : contacts;
            if (aa.EcmHub != want) { aa.EcmHub = want; MarkChanged(aa); }
        }
        Debug.Log($"[ACO] 전자전: ECM 모듈 {pods.Count}개를 이었습니다.");
    }

    private static bool IsFriendlyUnit(Component c)
    {
        ACO_EscortUnit escort = c.GetComponentInParent<ACO_EscortUnit>(true);
        return escort != null && !escort.Hostile;
    }

    private static void BakeRadar()
    {
        ACO_RadarContacts contacts = null;
        ACO_HudRadar hud = null;
        ACO_MissionGenerator generator = null;
        ACO_GameFlow flow = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (contacts == null) { contacts = root.GetComponentInChildren<ACO_RadarContacts>(true); }
            if (hud == null) { hud = root.GetComponentInChildren<ACO_HudRadar>(true); }
            if (generator == null) { generator = root.GetComponentInChildren<ACO_MissionGenerator>(true); }
            if (flow == null) { flow = root.GetComponentInChildren<ACO_GameFlow>(true); }
        }
        if (contacts == null) { return; }
        if (contacts.Generator != generator || contacts.Flow != flow) { contacts.Generator = generator; contacts.Flow = flow; MarkChanged(contacts); }
        if (hud != null && hud.Contacts != contacts) { hud.Contacts = contacts; MarkChanged(hud); }
        if (hud != null)
        {
            // 예전 기본값 → 작은 점(분별이 잘 되게), 색 바꿈(적 노랑·목표 빨강)
            bool changed = false;
            if (Mathf.Approximately(hud.BlipSize, 0.07f)) { hud.BlipSize = 0.045f; changed = true; }
            if (Mathf.Approximately(hud.MissileSize, 0.05f)) { hud.MissileSize = 0.035f; changed = true; }
            if (SwapOldRadarColors(ref hud.EnemyColor, ref hud.ObjectiveColor)) { changed = true; }
            // 미사일: 예전 주황은 노랑(적)과 헷갈려 흰색으로
            if (SameColor(hud.MissileColor, new Color(1f, 0.55f, 0.1f, 1f))) { hud.MissileColor = Color.white; changed = true; }
            if (changed) { MarkChanged(hud); Debug.Log("[ACO] HUD 레이더: 점을 작게 하고 색을 바꿨습니다(적 노랑, 목표 빨강, 미사일 흰색).", hud); }
        }
        int launchers = 0;
        int maps = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissileLauncher c in root.GetComponentsInChildren<ACO_MissileLauncher>(true)) { if (c.Radar != contacts) { c.Radar = contacts; MarkChanged(c); launchers++; } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true)) { if (hud != null && c.HudRadar != hud) { c.HudRadar = hud; MarkChanged(c); } }
            foreach (ACO_CockpitMap m in root.GetComponentsInChildren<ACO_CockpitMap>(true))
            {
                bool changed = false;
                if (m.Contacts != contacts) { m.Contacts = contacts; changed = true; }
                if (m.EntityControl == null) { m.EntityControl = m.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true); changed = true; }
                if (SwapOldRadarColors(ref m.EnemyColor, ref m.ObjectiveColor)) { changed = true; }
                if (changed) { MarkChanged(m); }
                maps++;
            }
        }
        if (flow == null || flow.Aircraft == null || flow.Aircraft.Length == 0) { Debug.LogWarning("[ACO] 레이더: 게임 진행의 참가자 기체(Aircraft)가 없어 HUD 레이더·조종석 지도가 내 기체를 알 수 없습니다.", contacts); }
        Debug.Log($"[ACO] 레이더 연결: 미사일 런처 {launchers}곳 새로 연결, 조종석 지도 {maps}개" + (maps == 0 ? " (ACO → Player Vehicle → Add Cockpit Map to Selected Vehicles로 기체에 단다)" : ""), contacts);
    }

    /// 날씨 배열(WX_* 순서)이 4칸(폭풍 전)이면 폭풍 값을 붙인다. 바꿨으면 true.
    private static bool MigrateStorm(ACO_Weather w)
    {
        bool changed = false;
        changed |= AppendStorm(ref w.Weights, 0.8f);
        changed |= AppendStorm(ref w.CloudCoverage, 1f);
        changed |= AppendStorm(ref w.CloudDarkness, 0.9f);
        changed |= AppendStorm(ref w.FogDensityScale, 9f);
        changed |= AppendStorm(ref w.RainIntensity, 1f);
        changed |= AppendStorm(ref w.SunScale, 0.3f);
        if (w.LightningAmount == null || w.LightningAmount.Length < ACO_Weather.WEATHER_COUNT) { w.LightningAmount = new[] { 0f, 0f, 0f, 0f, 1f }; changed = true; }
        if (w.CloudLayer != null && w.CloudLayer.Length == 4)
        {
            var c = new int[5];
            System.Array.Copy(w.CloudLayer, c, 4);
            c[4] = ACO_CloudField.LAYER_DECK;
            w.CloudLayer = c;
            changed = true;
        }
        if (w.WeatherNames != null && w.WeatherNames.Length == 4)
        {
            var n = new string[5];
            System.Array.Copy(w.WeatherNames, n, 4);
            n[4] = "STORM";
            w.WeatherNames = n;
            changed = true;
        }
        if (w.FogTint != null && w.FogTint.Length == 4)
        {
            var t = new Color[5];
            System.Array.Copy(w.FogTint, t, 4);
            t[4] = new Color(0.48f, 0.5f, 0.56f, 1f);
            w.FogTint = t;
            changed = true;
        }
        return changed;
    }

    private static bool AppendStorm(ref float[] a, float storm)
    {
        if (a == null || a.Length != 4) { return false; }
        var n = new float[5];
        System.Array.Copy(a, n, 4);
        n[4] = storm;
        a = n;
        return true;
    }

    /// 레이더·조종석 지도 색: 예전 기본값(적 빨강, 목표 노랑)이면 바꾼다(적 노랑, 목표 빨강 — 목표가 더 눈에 띄게). 바꿨으면 true.
    private static bool SwapOldRadarColors(ref Color enemy, ref Color objective)
    {
        Color oldRed = new Color(1f, 0.25f, 0.2f, 1f);
        Color oldYellow = new Color(1f, 0.82f, 0.2f, 1f);
        if (!SameColor(enemy, oldRed) || !SameColor(objective, oldYellow)) { return false; }
        enemy = oldYellow;
        objective = oldRed;
        return true;
    }

    private static bool SameColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;
    }

    /// 플레이어 기체의 기총 탄(충돌 메시지를 보내는 파티클 — 플레어 파티클은 빼고)에 ACO_GunDamageBonus를 붙인다(이미 있으면 그대로).
    private static ACO_GunDamageBonus[] EnsureGunBonuses(SaccFlightAndVehicles.SaccEntity entity, SaccFlightAndVehicles.DFUNC_Flares[] flares)
    {
        var flareSystems = new List<ParticleSystem>();
        if (flares != null) { foreach (var f in flares) { if (f != null && f.FlareParticles != null) { flareSystems.AddRange(f.FlareParticles); } } }
        var list = new List<ACO_GunDamageBonus>();
        foreach (ParticleSystem ps in entity.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (flareSystems.Contains(ps)) { continue; }
            var col = ps.collision;
            if (!col.enabled || !col.sendCollisionMessages) { continue; }
            if (ps.GetComponentInParent<ACO_Missile>(true) != null || ps.GetComponentInParent<ACO_Ordnance>(true) != null) { continue; }
            ACO_GunDamageBonus b = ps.GetComponent<ACO_GunDamageBonus>();
            if (b == null)
            {
                b = UdonSharpEditor.UdonSharpUndo.AddComponent<ACO_GunDamageBonus>(ps.gameObject);
                Debug.Log($"[ACO] 기총 피해 강화: '{entity.name}/{ps.name}'에 ACO_GunDamageBonus를 붙였습니다.", ps);
            }
            list.Add(b);
        }
        return list.ToArray();
    }

    private static bool ColorClose(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;
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
            foreach (ACO_FlareDispenser c in root.GetComponentsInChildren<ACO_FlareDispenser>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_VehicleUpgrades c in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true)) { if (c.Chatter != radio) { c.Chatter = radio; MarkChanged(c); linked++; } }
        }
        if (radio.Voice == null) { Debug.LogWarning("[ACO] 무전 채터에 음성 AudioSource(Voice)가 없어 음성·PTT가 들리지 않습니다.", radio); }

        // 씬에 저장된 대사 표가 예전 무전 기본값 그대로면 새 기본 대사로 바꾼다(손으로 고친 대사는 건드리지 않는다)
        const string OldFirstLine = "All aircraft, the operation has begun. Good hunting.";
        if (radio.LineText != null && radio.LineText.Length > 0 && radio.LineText[0] == OldFirstLine)
        {
            var temp = new GameObject("ACO_ChatterDefaults") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                ACO_Chatter d = temp.AddComponent<ACO_Chatter>();   // 필드 기본값만 읽는다
                Undo.RecordObject(radio, "ACO chatter defaults");
                radio.LineEvent = d.LineEvent;
                radio.LineSpeaker = d.LineSpeaker;
                radio.LineText = d.LineText;
                radio.LineClip = new AudioClip[0];
                radio.SpeakerNames = d.SpeakerNames;
                radio.SpeakerColors = d.SpeakerColors;
                MarkChanged(radio);
                Debug.Log("[ACO] 무전 채터의 대사 표를 새 기본 대사로 바꿨습니다(예전 기본값이었음).", radio);
            }
            finally { Object.DestroyImmediate(temp); }
        }
        AppendMissingChatterLines(radio);
        if (linked > 0) { Debug.Log($"[ACO] 무전 채터 '{radio.name}'를 {linked}곳에 연결했습니다.", radio); }
    }

    /// 새로 생긴 상황(EV_*)의 기본 대사가 씬의 대사 표에 하나도 없으면 끝에 더하고, 확률·쿨타임 배열도 늘린다(손으로 고친 대사·음성은 그대로).
    private static void AppendMissingChatterLines(ACO_Chatter radio)
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

    private static float[] GrowTo(float[] current, float[] defaults)
    {
        int n = defaults.Length;
        var r = new float[n];
        for (int i = 0; i < n; i++) { r[i] = current != null && i < current.Length ? current[i] : defaults[i]; }
        return r;
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

    /// 작전 테이블 ↔ 생성기(유닛 풀·지형) / 게임 진행(참가자 기체). 비어 있을 때만 채운다.
    /// 페이즈 모듈(ACO_PhaseModule 상속) ↔ 미션 관리자: 관리자 아래(자식)의 모듈을 순서대로 Modules에 넣고(종류 번호 = 20 + 순서) 관리자·생성기를 연결한다.
    /// 모듈 변환 메뉴가 부른다(Bake 전체를 돌리지 않고 모듈 목록만).
    public static void BakePhaseModulesNow() { BakePhaseModules(); }

    private static void BakePhaseModules()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissionManager manager in root.GetComponentsInChildren<ACO_MissionManager>(true))
            {
                ACO_PhaseModuleSetup.EnsureFortress(manager);
                ACO_PhaseModuleSetup.EnsureFleet(manager);
                ACO_PhaseModuleSetup.EnsureRailgun(manager);
                ACO_PhaseModuleSetup.ApplyDefaultWeights(manager);
                ACO_PhaseModule[] modules = manager.GetComponentsInChildren<ACO_PhaseModule>(true);
                manager.Modules = modules;
                MarkChanged(manager);
                foreach (ACO_PhaseModule m in modules)
                {
                    if (m.Manager == manager && m.Generator == manager.Generator) { continue; }
                    m.Manager = manager;
                    m.Generator = manager.Generator;
                    MarkChanged(m);
                }
                if (modules.Length > 0) { Debug.Log($"[ACO] 미션 관리자 '{manager.name}'에 페이즈 모듈 {modules.Length}개를 연결했습니다.", manager); }
                // 상륙전: 해상 상륙함(VIP)이 풀에 없으면 늘 배치 불가로 걸러진다
                if (manager.GetComponentInChildren<ACO_PhaseAmphibious>(true) != null && manager.Generator != null)
                {
                    int lcac = 0, heli = 0, ships = 0, guards = 0;
                    if (manager.Generator.EscortUnits != null)
                    {
                        foreach (ACO_EscortUnit e in manager.Generator.EscortUnits)
                        {
                            if (e == null || e.Hostile) { continue; }
                            if (e.MoveMode == ACO_EscortUnit.MOVE_HOVER)
                            {
                                if (e.Role == ACO_EscortUnit.ROLE_VIP) { lcac++; }
                                // 공기부양정은 바다 위에서 해수면을 미끄러진다 — 생성기 해수면과 맞춘다
                                if (!Mathf.Approximately(e.HoverSeaLevel, manager.Generator.SeaLevel)) { e.HoverSeaLevel = manager.Generator.SeaLevel; MarkChanged(e); }
                            }
                            else if (e.MoveMode == ACO_EscortUnit.MOVE_AIR && e.Role == ACO_EscortUnit.ROLE_GUARD) { heli++; }
                            else if (e.MoveMode == ACO_EscortUnit.MOVE_SEA && e.Role == ACO_EscortUnit.ROLE_MOTHERSHIP) { ships++; }
                            else if (e.MoveMode == ACO_EscortUnit.MOVE_SEA && e.Role == ACO_EscortUnit.ROLE_GUARD) { guards++; }
                        }
                    }
                    if (lcac == 0) { Debug.LogWarning("[ACO] 상륙전 페이즈: 생성기 유닛 풀에 LCAC(공기부양, Role VIP)가 없어 이 페이즈는 나오지 않습니다 — 프리팹 생성 창의 상륙전 부대를 풀 아래에 두세요.", manager); }
                    else { Debug.Log($"[ACO] 상륙전 페이즈: LCAC {lcac}, 공격헬기 {heli}, 강습상륙함 {ships}, 호위함 {guards}. 지형에 항구(도시가 바다에 닿을 때 — TerrainFeatures.PortPrefab)가 있어야 나옵니다.", manager); }
                }
            }
        }
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

    /// 게임 진행(씬에 하나)을 관리자·로비 콘솔·격납고 버튼·기체 강화 확장에 연결한다.
    /// 참가자 기체(Aircraft)가 비면 강화 확장이 있는 기체를 Hierarchy 순서로 최대 8대 넣는다. 상태 글자는 이름(ACO_FlowStatus)으로 찾는다.
    private static void BakeGameFlow()
    {
        var flows = new List<ACO_GameFlow>();
        ACO_MissionManager manager = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            flows.AddRange(root.GetComponentsInChildren<ACO_GameFlow>(true));
            if (manager == null) { manager = root.GetComponentInChildren<ACO_MissionManager>(true); }
        }
        if (flows.Count == 0) { return; }
        ACO_GameFlow flow = flows[0];
        if (flows.Count > 1) { Debug.LogWarning($"[ACO] 게임 진행(ACO_GameFlow)이 {flows.Count}개 있습니다. '{flow.name}' 하나만 연결합니다.", flow); }

        if (flow.Manager == null) { flow.Manager = manager; }
        if (flow.ReadyTuneVersion < 1)
        {
            flow.ReadyTuneVersion = 1;
            flow.ReadyCountdown = 30f;
            MarkChanged(flow);
        }
        ACO_ScoreHUD flowHud = Object.FindObjectOfType<ACO_ScoreHUD>(true);
        if (flow.Hud != flowHud) { flow.Hud = flowHud; MarkChanged(flow); }
        // 지형 이름: 프리셋이 늘었으면 새 이름을 붙인다
        string[] defNames = { "BIG ISLAND", "ARCHIPELAGO", "TWIN ISLANDS", "ATOLL", "ISLAND CHAIN", "PLATEAU", "MOUNTAIN RANGE", "CANYON", "COASTLINE", "VOLCANO", "REEF ISLANDS", "BASIN" };
        if (flow.TerrainNames == null || flow.TerrainNames.Length < defNames.Length)
        {
            var names = new string[defNames.Length];
            for (int k = 0; k < names.Length; k++) { names[k] = flow.TerrainNames != null && k < flow.TerrainNames.Length ? flow.TerrainNames[k] : defNames[k]; }
            flow.TerrainNames = names;
        }
        ACO_ScreenFader fader = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (fader == null) { fader = root.GetComponentInChildren<ACO_ScreenFader>(true); }
        }
        flow.Fader = fader;
        ACO_PlayerProgress progress = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (progress == null) { progress = root.GetComponentInChildren<ACO_PlayerProgress>(true); }
        }
        flow.Progress = progress;
        if (flow.BriefingPoint == null) { Debug.LogWarning("[ACO] 게임 진행: BriefingPoint가 없어 미션이 끝나도 대기실로 옮기지 않습니다" + (flow.HangarPoint == null ? ", 격추·탈출한 참가자도 바다에 남습니다." : "."), flow); }
        if (flow.SpectatePoint == null) { Debug.LogWarning("[ACO] 게임 진행: SpectatePoint가 없어 관전(SPECTATE) 버튼이 동작하지 않습니다.", flow); }
        if (flow.Aircraft == null || flow.Aircraft.Length == 0)
        {
            var aircraft = new List<SaccFlightAndVehicles.SaccEntity>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (ACO_VehicleUpgrades up in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true))
                {
                    SaccFlightAndVehicles.SaccEntity e = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
                    if (e != null && !aircraft.Contains(e) && aircraft.Count < 8) { aircraft.Add(e); }
                }
            }
            flow.Aircraft = aircraft.ToArray();
            Debug.Log($"[ACO] 게임 진행: 참가자 기체 {aircraft.Count}대를 넣었습니다(강화 확장이 있는 기체). 순서 = 칸 번호.", flow);
        }
        int slots = flow.Aircraft.Length;
        int decks = 0;
        if (flow.DeckPoints != null) { foreach (Transform t in flow.DeckPoints) { if (t != null) { decks++; } } }
        if (decks < slots) { Debug.LogWarning($"[ACO] 게임 진행: 기체 {slots}대인데 갑판 출격 위치(DeckPoints)가 {decks}개입니다 — 위치가 없는 칸은 출격할 수 없습니다.", flow); }

        var status = new List<TMPro.TextMeshProUGUI>();
        var banners = new List<TMPro.TextMeshProUGUI>();
        var phaseTexts = new List<TMPro.TextMeshProUGUI>();
        var phaseInfos = new List<TMPro.TextMeshProUGUI>();
        var terrainTexts = new List<TMPro.TextMeshProUGUI>();
        var buttons = new List<ACO_FlowButton>();
        var phaseButtons = new List<ACO_MissionStartButton>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (TMPro.TextMeshProUGUI t in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                string n = t.gameObject.name;
                if (n == ACO_HUDBuilder.FlowStatusName) { status.Add(t); }
                else if (n == ACO_HUDBuilder.FlowBannerName) { banners.Add(t); }
                else if (n == ACO_HUDBuilder.FlowPhasesName) { phaseTexts.Add(t); }
                else if (n == ACO_HUDBuilder.FlowPhaseInfoName) { phaseInfos.Add(t); }
                else if (n == ACO_HUDBuilder.FlowTerrainName) { terrainTexts.Add(t); }
            }
            foreach (ACO_FlowButton b in root.GetComponentsInChildren<ACO_FlowButton>(true))
            {
                if (b.Flow != flow) { b.Flow = flow; MarkChanged(b); }
                if (b.Background == null && b.GetComponent<UnityEngine.UI.Image>() != null) { b.Background = b.GetComponent<UnityEngine.UI.Image>(); MarkChanged(b); }
                buttons.Add(b);
            }
            foreach (ACO_MissionStartButton b in root.GetComponentsInChildren<ACO_MissionStartButton>(true))
            {
                if (b.Flow != flow) { b.Flow = flow; MarkChanged(b); }
                if (b.Background == null && b.GetComponent<UnityEngine.UI.Image>() != null) { b.Background = b.GetComponent<UnityEngine.UI.Image>(); MarkChanged(b); }
                phaseButtons.Add(b);
            }
            foreach (ACO_Teleporter t in root.GetComponentsInChildren<ACO_Teleporter>(true))
            {
                if (t.Fader != fader) { t.Fader = fader; MarkChanged(t); }
            }
        }
        flow.StatusTexts = status.ToArray();
        flow.BannerTexts = banners.ToArray();
        flow.PhaseTexts = phaseTexts.ToArray();
        var phaseIcons = new List<UnityEngine.UI.Image>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (UnityEngine.UI.Image img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img.name == ACO_HUDBuilder.FlowPhaseIconName) { phaseIcons.Add(img); }
            }
        }
        flow.PhaseIcons = phaseIcons.ToArray();
        flow.PhaseLevelSprites = new Sprite[5];
        for (int i = 0; i < flow.PhaseLevelSprites.Length; i++) { flow.PhaseLevelSprites[i] = ACO_HUDBuilder.LoadIcon("phase_" + (i + 1)); }
        flow.PhaseInfoTexts = phaseInfos.ToArray();
        flow.TerrainTexts = terrainTexts.ToArray();
        // 출격 카운트다운 판: 상태 글자·배너가 있는 패널(캔버스)마다 하나 — 없으면 만든다
        var overlays = new List<GameObject>();
        var overlayTexts = new List<TMPro.TextMeshProUGUI>();
        var panelTexts = new List<TMPro.TextMeshProUGUI>(status);
        panelTexts.AddRange(banners);
        foreach (TMPro.TextMeshProUGUI t in panelTexts)
        {
            Canvas c = t.canvas != null ? t.canvas.rootCanvas : t.GetComponentInParent<Canvas>(true);
            RectTransform canvasRt = c != null ? (RectTransform)c.transform : t.transform.parent as RectTransform;
            if (canvasRt == null) { continue; }
            GameObject ov = ACO_HUDBuilder.EnsureFlowCountdown(canvasRt);
            if (ov == null || overlays.Contains(ov)) { continue; }
            overlays.Add(ov);
            overlayTexts.Add(ov.GetComponentInChildren<TMPro.TextMeshProUGUI>(true));
        }
        flow.CountdownOverlays = overlays.ToArray();
        flow.CountdownTexts = overlayTexts.ToArray();
        // 격납고·상점: 출격 카운트다운 중 무장 변경·구매를 막는다
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Loadout l in root.GetComponentsInChildren<ACO_Loadout>(true)) { if (l.Flow != flow) { l.Flow = flow; MarkChanged(l); } }
            foreach (ACO_TargetMarkers tm in root.GetComponentsInChildren<ACO_TargetMarkers>(true)) { if (tm.Flow != flow) { tm.Flow = flow; MarkChanged(tm); } }
            foreach (ACO_Shop sh in root.GetComponentsInChildren<ACO_Shop>(true)) { if (sh.Flow != flow) { sh.Flow = flow; MarkChanged(sh); } }
        }
        flow.Buttons = buttons.ToArray();
        flow.PhaseButtons = phaseButtons.ToArray();
        MarkChanged(flow);
        Debug.Log($"[ACO] 게임 진행 '{flow.name}'를 연결했습니다(기체 {slots}, 상태 글자 {status.Count}, 카운트다운 판 {overlays.Count}, 진행 버튼 {buttons.Count}, 페이즈 버튼 {phaseButtons.Count}).", flow);
    }

    /// 씬 진행도의 강화 배열(이름·배수·1단계 가격·단계 가격 배수)이 지금 항목 수·최대 단계(10)와 다르면 새 기본값으로 되돌린다(DESIGN.md 6-31).
    private static void MigrateUpgradeArrays(ACO_PlayerProgress progress)
    {
        var temp = new GameObject("ACO_ProgressDefaults");
        ACO_PlayerProgress defaults = temp.AddComponent<ACO_PlayerProgress>();
        string[] names = (string[])defaults.UpgradeNames.Clone();
        float[] steps = (float[])defaults.UpgradeSteps.Clone();
        int[] costs = (int[])defaults.UpgradeBaseCosts.Clone();
        float[] scale = (float[])defaults.LevelCostScale.Clone();
        Object.DestroyImmediate(temp);

        int n = ACO_PlayerProgress.UPGRADE_COUNT;
        bool current = progress.UpgradeNames != null && progress.UpgradeNames.Length == n
            && progress.UpgradeSteps != null && progress.UpgradeSteps.Length == n
            && progress.UpgradeBaseCosts != null && progress.UpgradeBaseCosts.Length == n
            && progress.LevelCostScale != null && progress.LevelCostScale.Length == scale.Length;
        if (current) { return; }
        Undo.RecordObject(progress, "ACO upgrade defaults");
        progress.UpgradeNames = names;
        progress.UpgradeSteps = steps;
        progress.UpgradeBaseCosts = costs;
        progress.LevelCostScale = scale;
        MarkChanged(progress);
        Debug.Log($"[ACO] 진행도 '{progress.name}': 강화 배열을 새 기본값({n}항목, 최대 {scale.Length}단계)으로 바꿨습니다.", progress);
    }

    /// 진행도(재화·강화·해금, 씬에 하나)를 격납고·상점·작전판·기체 강화 확장에 연결한다.
    /// 기체 강화 확장(ACO_VehicleUpgrades): 기체의 ACO_DFUNC_Missile 전부(받을 강화는 무장 인스펙터의 UpgradeFamily).
    /// SaccEntity.ExtensionUdonBehaviours에 없으면 넣는다(그래야 탑승/폭발 이벤트를 받는다).
    private static void BakePlayerProgress()
    {
        var progresses = new List<ACO_PlayerProgress>();
        var upgrades = new List<ACO_VehicleUpgrades>();
        ACO_MissionManager manager = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            progresses.AddRange(root.GetComponentsInChildren<ACO_PlayerProgress>(true));
            upgrades.AddRange(root.GetComponentsInChildren<ACO_VehicleUpgrades>(true));
            if (manager == null) { manager = root.GetComponentInChildren<ACO_MissionManager>(true); }
        }
        ACO_PlayerProgress progress = progresses.Count > 0 ? progresses[0] : null;
        if (progresses.Count > 1)
        {
            Debug.LogWarning($"[ACO] 진행도(ACO_PlayerProgress)가 {progresses.Count}개 있습니다. '{progress.name}' 하나만 연결합니다 — 나머지는 지우세요(같은 저장 값을 서로 덮어씁니다).", progress);
        }
        if (progress == null)
        {
            if (upgrades.Count > 0) { Debug.LogWarning("[ACO] 기체 강화 확장이 있지만 씬에 진행도가 없습니다 — ACO → Create Player Progress로 만드세요(없으면 강화·보상이 꺼집니다)."); }
            return;
        }
        if (progress.Manager == null && manager != null)
        {
            progress.Manager = manager;
            MarkChanged(progress);
        }
        MigrateUpgradeArrays(progress);
        if (progress.SafeZones == null || progress.SafeZones.Length == 0)
        {
            Debug.LogWarning($"[ACO] 진행도 '{progress.name}': 안전지대(SafeZones)가 비어 있어 어디서 내려도 격추 페널티가 없습니다 — 항모 갑판 등을 덮는 콜라이더를 넣으세요.", progress);
        }

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Loadout loadout in root.GetComponentsInChildren<ACO_Loadout>(true))
            {
                if (loadout.Progress == progress) { continue; }
                loadout.Progress = progress;
                MarkChanged(loadout);
            }
            foreach (ACO_Shop shop in root.GetComponentsInChildren<ACO_Shop>(true))
            {
                // 강화 항목 수가 바뀌었으면(예: 특수 대지에 락온 속도·거리 추가) 항목별 아이콘을 다시 채운다
                if (shop.UpgradeIcons == null || shop.UpgradeIcons.Length != ACO_PlayerProgress.UPGRADE_COUNT)
                {
                    shop.UpgradeIcons = ACO_HUDBuilder.BuildUpgradeIcons(progress);
                    MarkChanged(shop);
                    Debug.Log($"[ACO] 상점 '{shop.name}': 강화 아이콘을 {ACO_PlayerProgress.UPGRADE_COUNT}항목에 맞게 다시 채웠습니다.", shop);
                }
                if (shop.Progress == progress) { continue; }
                shop.Progress = progress;
                MarkChanged(shop);
            }
            foreach (ACO_MissionLog log in root.GetComponentsInChildren<ACO_MissionLog>(true))
            {
                if (log.Progress == progress) { continue; }
                log.Progress = progress;
                MarkChanged(log);
            }
        }

        foreach (ACO_VehicleUpgrades up in upgrades)
        {
            SaccFlightAndVehicles.SaccEntity entity = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            if (entity == null)
            {
                Debug.LogWarning($"[ACO] 기체 강화 확장 '{up.name}'이 SAV 기체 안에 있지 않습니다.", up);
                continue;
            }
            up.Progress = progress;
            up.AirVehicle = entity.GetComponentInChildren<SaccFlightAndVehicles.SaccAirVehicle>(true);

            // 무장마다 받을 강화는 무장 인스펙터의 UpgradeFamily(드롭다운)가 정한다.
            var weapons = new List<ACO_DFUNC_Missile>(entity.GetComponentsInChildren<ACO_DFUNC_Missile>(true));
            foreach (ACO_DFUNC_Missile m in weapons)
            {
                bool guided = m.Missile == null || m.Missile.GetComponent<ACO_Ordnance>() == null;
                if (m.UpgradeFamily == ACO_UpgradeFamily.SpecialAGM && guided && !m.MissileSplash)
                {
                    Debug.LogWarning($"[ACO] '{entity.name}/{m.name}': 강화 계열이 SpecialAGM인데 MissileSplash가 꺼져 있어 범위 피해(폭발 반경 강화)가 없습니다.", m);
                }
            }
            up.Weapons = weapons.ToArray();
            up.Flares = entity.GetComponentsInChildren<SaccFlightAndVehicles.DFUNC_Flares>(true);   // 대응탄 포드 소모품
            up.GunBonuses = EnsureGunBonuses(entity, up.Flares);
            MarkChanged(up);
            ACO_PlayerMissileSetup.EnsureSeatRecenter(entity);

            if (ACO_PlayerMissileSetup.RegisterExtension(entity, up))
            {
                Debug.Log($"[ACO] '{entity.name}': 강화 확장을 ExtensionUdonBehaviours에 넣었습니다.", entity);
            }
            if (up.AirVehicle == null) { Debug.LogWarning($"[ACO] '{entity.name}': SaccAirVehicle을 찾지 못해 기체 강화(체력·기동·추력)가 꺼집니다.", up); }
        }

        // 강화 확장이 없는 플레이어 기체(ACO 미사일이 있는 기체)를 알려 준다.
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (SaccFlightAndVehicles.SaccEntity entity in root.GetComponentsInChildren<SaccFlightAndVehicles.SaccEntity>(true))
            {
                if (entity.GetComponentInChildren<ACO_DFUNC_Missile>(true) == null) { continue; }
                if (entity.GetComponentInChildren<ACO_VehicleUpgrades>(true) != null) { continue; }
                Debug.LogWarning($"[ACO] '{entity.name}'에 강화 확장이 없습니다 — 강화가 적용되지 않고 격추가 보상에 반영되지 않습니다. "
                    + "ACO → Player Missile → Add Upgrade Extension to Selected Vehicles를 실행하세요.", entity);
            }
        }
        Debug.Log($"[ACO] 진행도 '{progress.name}'를 연결했습니다(기체 강화 확장 {upgrades.Count}개).", progress);
    }

    /// CIWS 미사일 요격: (1) 요격 기본값을 아직 안 넣은 대공포에 한 번 넣고(ApplyInterceptDefaults — 그 뒤 직접 바꾼 값은 그대로),
    /// (2) 요격 CIWS 목록(InterceptProtectLayers가 있는 것)을 모든 무장(ACO_DFUNC_Missile)·NPC 런처(ACO_MissileLauncher)에 굽는다 — 미사일이 이 목록에서 자기를 덮는 CIWS를 찾는다.
    private static void BakeCiws()
    {
        var sites = new List<ACO_AAGunSite>();
        int defaults = 0;
        int tuned = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_AAGunSite aa in root.GetComponentsInChildren<ACO_AAGunSite>(true))
            {
                if (!aa.InterceptDefaultsApplied)
                {
                    if (ApplyInterceptDefaults(aa)) { defaults++; }
                    MarkChanged(aa);
                }
                // 공용 포대 프리팹은 판이 같아 건드리지 않는다 — 프리팹 수정이 씬 값에 가리지 않는다
                if (ApplyShellRadius(aa)) { MarkChanged(aa.Gun); }
                if (aa.InterceptTuneVersion < CiwsTuneVersion && (aa.InterceptProtectLayers.value & (1 << 29)) != 0)
                {
                    ApplyEnemyCiwsTune(aa);
                    MarkChanged(aa);
                    tuned++;
                }
                if (aa.InterceptProtectLayers.value != 0) { sites.Add(aa); }
            }
        }
        if (tuned > 0) { Debug.Log($"[ACO] 적 CIWS {tuned}문에 새 요격 수치를 넣었습니다."); }
        ACO_AAGunSite[] list = sites.ToArray();
        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_DFUNC_Missile c in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true))
            {
                if (SameSites(c.InterceptSites, list)) { continue; }
                c.InterceptSites = list; MarkChanged(c); linked++;
            }
            foreach (ACO_MissileLauncher c in root.GetComponentsInChildren<ACO_MissileLauncher>(true))
            {
                if (SameSites(c.InterceptSites, list)) { continue; }
                c.InterceptSites = list; MarkChanged(c); linked++;
            }
        }
        if (defaults > 0) { Debug.Log($"[ACO] 미사일 요격: 대공포 {defaults}문에 요격 기본값을 넣었습니다(함선 CIWS = 적 요격, 아군 호위함 대공포 = 아군 요격)."); }
        if (linked > 0) { Debug.Log($"[ACO] 미사일 요격: 요격 CIWS {list.Length}문을 무장·런처 {linked}곳에 연결했습니다."); }
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
        // 예전 기본값(물빛 구 알파 0.45)은 물빛 벽이 하늘을 가리게 되며 옅게
        if (Mathf.Approximately(uw.TintAlpha, 0.45f)) { uw.TintAlpha = 0.2f; changed = true; }
        if (changed) { MarkChanged(uw); }
        // 예전에 만든 물속 연출엔 물빛 벽이 없다 — 더한다
        if (ACO_WaterFxBuilder.EnsureUnderwaterParts(uw)) { Debug.Log("[ACO] 물속 연출에 물빛 벽(Backdrop)을 더했습니다.", uw); }
    }

    private static bool SameSites(ACO_AAGunSite[] a, ACO_AAGunSite[] b)
    {
        if (a == null || a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
    }

    /// 요격 기본값(프리팹 빌더·Bake 공용). 함선 CIWS(MarkerLabel "CIWS") = 적 요격: 적 항공기·지상(29, 30)을 노린 미사일, 사거리 1.2km, 방어 반경 600m,
    /// 다른 함선 ×2. 아군 호위 유닛(ACO_EscortUnit, Hostile 아님)의 대공포(이름에 "AAGun") = 아군 요격: 아군 항공기·지상(25, 26), 사거리·반경 1.5km, 배수 1.
    /// 그 밖(지상 대공포·함포 등)은 요격하지 않는다. 기본값을 넣었으면 true. 어느 쪽이든 InterceptDefaultsApplied를 켠다.
    /// 적 CIWS 요격 수치의 판 번호. 수치를 바꾸면 올린다 — 씬·프리팹의 먼저 만든 CIWS에 Bake가 한 번 다시 넣는다.
    private const int CiwsTuneVersion = 1;

    /// 적 CIWS 요격 수치: AAM 최고 속도(600m/s)면 사거리를 1.5초에 지나 혼자서는 겨우 맞출 수 있고, 다음 발까지 쉰다 — 일제 사격이 뚫는다.
    private static void ApplyEnemyCiwsTune(ACO_AAGunSite aa)
    {
        aa.InterceptRange = 900f;
        aa.InterceptReactionTime = 0.5f;
        aa.InterceptKillTime = 1f;
        aa.InterceptCooldown = 1.5f;
        aa.InterceptTuneVersion = CiwsTuneVersion;
    }

    /// 대공포 탄 도착 판정 반경: 아직 없으면 넣는다. CIWS는 조밀한 탄, 그 밖의 포는 큰 탄이라 넓다. 넣었으면 true.
    public static bool ApplyShellRadius(ACO_AAGunSite aa)
    {
        if (aa.Gun == null || aa.Gun.ShellHitRadius > 0f) { return false; }
        aa.Gun.ShellHitRadius = aa.MarkerLabel == "CIWS" ? 20f : 45f;
        return true;
    }

    public static bool ApplyInterceptDefaults(ACO_AAGunSite aa)
    {
        aa.InterceptDefaultsApplied = true;
        if (aa.InterceptProtectLayers.value != 0) { return false; }   // 이미 직접 정했다 — 덮어쓰지 않는다
        ACO_EscortUnit escort = aa.GetComponentInParent<ACO_EscortUnit>(true);
        if (aa.MarkerLabel == "CIWS")
        {
            aa.InterceptProtectLayers = (1 << 29) | (1 << 30);
            aa.DefenseRadius = 600f;
            aa.OtherDefenseFactor = 2f;
            ApplyEnemyCiwsTune(aa);
            return true;
        }
        else if (escort != null && !escort.Hostile && aa.name.Contains("AAGun"))
        {
            aa.InterceptProtectLayers = (1 << 25) | (1 << 26);
            aa.InterceptRange = 1500f;
            aa.DefenseRadius = 1500f;
            aa.OtherDefenseFactor = 1f;
        }
        else { return false; }
        aa.InterceptReactionTime = 0.5f;
        aa.InterceptKillTime = 0.5f;
        return true;
    }

    private static void MarkChanged(Object o)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        EditorUtility.SetDirty(o);
    }

    /// 플레이어 미사일(ACO_DFUNC_Missile)의 표적 = TargetLayers에 있는 씬 오브젝트(락온 마커). SAV가 AAMTargets를 굽는 규칙과 같고,
    /// 자기 기체 안의 오브젝트는 뺀다. 풀에서 꺼져 있는 NPC도 포함해야 하므로 비활성까지 찾는다.
    private static void BakePlayerMissiles()
    {
        var weapons = new List<ACO_DFUNC_Missile>();
        var all = new List<Transform>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            weapons.AddRange(root.GetComponentsInChildren<ACO_DFUNC_Missile>(true));
            all.AddRange(root.GetComponentsInChildren<Transform>(true));
        }

        foreach (ACO_DFUNC_Missile weapon in weapons)
        {
            // 특수무장 슬롯 안의 모듈은 락온 진행 표시가 십자다.
            bool special = weapon.GetComponentInParent<ACO_DFUNC_Special>(true) != null;
            if (weapon.CrossLockIndicator != special) { weapon.CrossLockIndicator = special; }
            int mask = weapon.TargetLayers;
            SaccFlightAndVehicles.SaccEntity own = weapon.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            var targets = new List<GameObject>();
            foreach (Transform t in all)
            {
                if (((1 << t.gameObject.layer) & mask) == 0) { continue; }
                if (own != null && t.IsChildOf(own.transform)) { continue; }
                targets.Add(t.gameObject);
            }
            weapon.Targets = targets.ToArray();
            // SAV 키보드 단축키가 바꿔 낀 옛 기능(DFUNC_AAM, 특수무장 자리의 옛 기능)을 가리키면 새 기능으로 옮긴다.
            if (own != null) { ACO_PlayerMissileSetup.FixKeyboardBindings(own); }
            PrefabUtility.RecordPrefabInstancePropertyModifications(weapon);
            EditorUtility.SetDirty(weapon);
            Debug.Log($"[ACO] 플레이어 미사일 '{weapon.name}'({(own != null ? own.name : "기체 없음")}): 표적 {targets.Count}개를 베이크했습니다.", weapon);
        }

        // 특수무장 슬롯 ↔ 격납고(씬의 첫 번째 것)
        ACO_Loadout loadout = null;
        var specials = new List<ACO_DFUNC_Special>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (loadout == null) { loadout = root.GetComponentInChildren<ACO_Loadout>(true); }
            specials.AddRange(root.GetComponentsInChildren<ACO_DFUNC_Special>(true));
        }
        foreach (ACO_DFUNC_Special special in specials)
        {
            if (special.Loadout == loadout) { continue; }
            special.Loadout = loadout;
            PrefabUtility.RecordPrefabInstancePropertyModifications(special);
            EditorUtility.SetDirty(special);
        }
        if (specials.Count > 0)
        {
            Debug.Log(loadout != null
                ? $"[ACO] 특수무장 슬롯 {specials.Count}개를 격납고 '{loadout.name}'에 연결했습니다."
                : $"[ACO] 특수무장 슬롯 {specials.Count}개가 있지만 씬에 격납고(ACO_Hangar)가 없습니다 — 0번 무장만 씁니다.");
        }

        // 표적 마커가 락온 상태(깜빡임·이름표·마름모)를 그릴 수 있게 무기 목록과 락온 표시를 연결한다.
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_TargetMarkers markers in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
            {
                markers.Weapons = weapons.ToArray();
                if (ACO_HUDBuilder.EnsureLockVisuals(markers)) { Debug.Log($"[ACO] 표적 마커 '{markers.name}'에 락온 표시(마름모·머티리얼)를 만들었습니다.", markers); }
                if (ACO_HUDBuilder.EnsureCrosshair(markers)) { Debug.Log($"[ACO] 표적 마커 '{markers.name}'에 크로스헤어를 만들었습니다.", markers); }
                if (ACO_HUDBuilder.EnsureRwr(markers)) { Debug.Log($"[ACO] 표적 마커 '{markers.name}'에 RWR(위협 링·경보음)을 만들었습니다.", markers); }
                if (ACO_RadarBuilder.EnsureHudRadar(markers)) { Debug.Log($"[ACO] 표적 마커 '{markers.name}'에 HUD 레이더(RADAR)를 만들었습니다.", markers); }
                PrefabUtility.RecordPrefabInstancePropertyModifications(markers);
                EditorUtility.SetDirty(markers);
            }
        }
    }

    /// 공중 요새: 풀에 없으면 프리팹(없으면 만든다)을 풀 아래에 하나 둔다 — 부위가 많아 손으로 넣기 번거롭다.
    private static void EnsureFortress(Transform poolRoot)
    {
        if (ACO_PrefabBuilder.IsFortressOutdated())
        {
            ACO_PrefabBuilder.BuildFortress();
            Debug.Log("[ACO] 공중 요새 프리팹을 Archangel 모델로 다시 만들었습니다.");
        }
        ACO_PrefabBuilder.EnsureTurretShellRadius();
        ACO_PrefabBuilder.EnsureFortressBlast();
        ACO_PrefabBuilder.EnsurePartMarkers("NPC_Fortress");
        ACO_PrefabBuilder.EnsureFortressLaunchPoints();
        ACO_PrefabBuilder.EnsureFortressHullLayer();
        if (poolRoot.GetComponentInChildren<ACO_FlyingFortress>(true) != null) { return; }
        GameObject prefab = ACO_PrefabBuilder.Exists("NPC_Fortress")
            ? AssetDatabase.LoadAssetAtPath<GameObject>(ACO_PrefabBuilder.PathOf("NPC_Fortress"))
            : ACO_PrefabBuilder.BuildFortress();
        if (prefab == null) { return; }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, poolRoot);
        Undo.RegisterCreatedObjectUndo(go, "ACO fortress");
        go.SetActive(false);
        MarkChanged(go);
        Debug.Log("[ACO] 공중 요새 NPC_Fortress를 유닛 풀에 넣었습니다.", go);
    }

    /// 랜스 포대: 프리팹이 없으면 만들고 풀에 넣는다.
    private static void EnsureRailgun(Transform poolRoot)
    {
        ACO_PrefabBuilder.EnsureRailgunHorizon();
        ACO_PrefabBuilder.EnsurePartMarkers("NPC_Railgun");
        ACO_PrefabBuilder.EnsureRailgunMinis();
        ACO_PrefabBuilder.EnsureRailgunTurrets();
        ACO_PrefabBuilder.EnsureRailgunLayout();
        if (poolRoot.GetComponentInChildren<ACO_Railgun>(true) != null) { return; }
        AddFleetUnit(poolRoot, "NPC_Railgun", ACO_PrefabBuilder.BuildRailgun);
    }

    private static void BakeRailgun()
    {
        ACO_Railgun gun = Object.FindObjectOfType<ACO_Railgun>(true);
        ACO_PhaseRailgun phase = Object.FindObjectOfType<ACO_PhaseRailgun>(true);
        if (gun == null) { return; }
        if (phase == null) { Debug.LogWarning("[ACO] 랜스 포대: 페이즈 모듈(ACO_PhaseRailgun)이 없습니다.", gun); return; }
        ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
        ACO_CloudField clouds = Object.FindObjectOfType<ACO_CloudField>();
        if (gun.Phase != phase || gun.Contacts != contacts || gun.Clouds != clouds) { gun.Phase = phase; gun.Contacts = contacts; gun.Clouds = clouds; MarkChanged(gun); }
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        if (phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }
        // 씬에 저장된 옛 기초 값(반경 200m)을 넓어진 기초에 맞춘다
        ACO_MissionGenerator generator = Object.FindObjectOfType<ACO_MissionGenerator>(true);
        if (generator != null && generator.RailgunPadRadius == 200f)
        {
            generator.RailgunPadRadius = 530f;
            generator.RailgunMaxRelief = 80f;
            MarkChanged(generator);
        }
        if (gun.MiniGuns != null)
        {
            foreach (ACO_MiniRailgun mini in gun.MiniGuns)
            {
                if (mini == null || (mini.Phase == phase && mini.Contacts == contacts)) { continue; }
                mini.Phase = phase;
                mini.Contacts = contacts;
                MarkChanged(mini);
            }
        }
        // 먼저 만든 모듈의 대사 표에 소형 레일건 줄을 더한다
        if (System.Array.IndexOf(phase.LineEvent, ACO_PhaseRailgun.SIT_MINI_FIRE) < 0)
        {
            ArrayUtility.Add(ref phase.LineEvent, ACO_PhaseRailgun.SIT_MINI_FIRE);
            ArrayUtility.Add(ref phase.LineSpeaker, ACO_PhaseBoss.SPEAKER_AWACS);
            ArrayUtility.Add(ref phase.LineText, "Those smaller guns are leading you! Do not fly straight, keep turning!");
            ArrayUtility.Add(ref phase.LineClip, null);
            MarkChanged(phase);
        }
    }

    /// 지상 유닛 숙련도 체력의 기준(BaseHealth, 베테랑)을 SaccTarget 체력으로 맞춘다 — 동기화되는 Health는 게임 중 믿을 수 없다(늦게 들어온 클라이언트에겐 깎인 값).
    private static void BakeTierHealth()
    {
        int changed = 0;
        foreach (ACO_AAGunSite u in Object.FindObjectsOfType<ACO_AAGunSite>(true))
        {
            if (u.TargetHealth != null && u.BaseHealth != u.TargetHealth.Health) { u.BaseHealth = u.TargetHealth.Health; MarkChanged(u); changed++; }
        }
        foreach (ACO_SAMSite u in Object.FindObjectsOfType<ACO_SAMSite>(true))
        {
            if (u.TargetHealth != null && u.BaseHealth != u.TargetHealth.Health) { u.BaseHealth = u.TargetHealth.Health; MarkChanged(u); changed++; }
        }
        foreach (ACO_MobileUnit u in Object.FindObjectsOfType<ACO_MobileUnit>(true))
        {
            if (u.TargetHealth != null && u.BaseHealth != u.TargetHealth.Health) { u.BaseHealth = u.TargetHealth.Health; MarkChanged(u); changed++; }
        }
        foreach (ACO_Warship u in Object.FindObjectsOfType<ACO_Warship>(true))
        {
            if (u.TargetHealth != null && u.BaseHealth != u.TargetHealth.Health) { u.BaseHealth = u.TargetHealth.Health; MarkChanged(u); changed++; }
        }
        if (changed > 0) { Debug.Log($"[ACO] 지상 유닛 {changed}곳의 기준 체력(BaseHealth, 베테랑)을 채웠습니다."); }
    }

    /// 함대전: 기함·항모가 풀에 없으면 프리팹(없으면 만든다)을 넣는다. 호위함·순양함·구축함은 풀의 기존 유닛을 함께 쓴다.
    private static void EnsureFleet(Transform poolRoot)
    {
        bool hasFlagship = false;
        foreach (ACO_EscortUnit e in poolRoot.GetComponentsInChildren<ACO_EscortUnit>(true)) { if (e.Role == ACO_EscortUnit.ROLE_FLAGSHIP) { hasFlagship = true; } }
        if (!hasFlagship) { AddFleetUnit(poolRoot, "NPC_Flagship", ACO_PrefabBuilder.BuildFlagship); }
        bool hasCarrier = false;
        foreach (ACO_Warship s in poolRoot.GetComponentsInChildren<ACO_Warship>(true)) { if (s.ReservedForFleet) { hasCarrier = true; } }
        if (!hasCarrier) { AddFleetUnit(poolRoot, "NPC_Carrier", ACO_PrefabBuilder.BuildCarrier); }
    }

    private static void AddFleetUnit(Transform poolRoot, string prefabName, System.Func<GameObject> build)
    {
        GameObject prefab = ACO_PrefabBuilder.Exists(prefabName) ? AssetDatabase.LoadAssetAtPath<GameObject>(ACO_PrefabBuilder.PathOf(prefabName)) : build();
        if (prefab == null) { return; }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, poolRoot);
        Undo.RegisterCreatedObjectUndo(go, "ACO fleet");
        go.SetActive(false);
        MarkChanged(go);
        Debug.Log($"[ACO] {prefabName}을 유닛 풀에 넣었습니다.", go);
    }

    private const int FleetTuneVersion = 1;

    /// 함대전: 이름 BB → CG(순양함), 함선 VLS·함포가 아군 함선(레이어 26)도 노리게(바다 높이라 최저 고도 해제), 무전.
    /// 프리팹을 다시 만들지 않고 씬 값을 고친다 — 순양함·구축함 모델을 직접 바꿔 뒀다.
    private static void BakeFleet()
    {
        ACO_PhaseFleet phase = Object.FindObjectOfType<ACO_PhaseFleet>(true);
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        if (phase != null && phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }
        // 먼저 만든 모듈: 공통 부모의 기본 이름(요새)을 함대전 이름으로
        if (phase != null && phase.EnemyName == "FORTRESS COMMAND") { phase.EnemyName = "CARRIER GROUP"; MarkChanged(phase); }
        // 먼저 만든 표: 기함 입장에서 하는 줄을 함대장으로(글이 그대로인 줄만, 한 번만)
        if (phase != null && !phase.AllySpeakerApplied)
        {
            phase.AllySpeakerApplied = true;
            string[] flagshipLines =
            {
                "Both fleets are in gun range! All ships, open fire!",
                "Flagship is taking heavy fire! Get those ships off us!",
                "Flagship is critical! We cannot take much more!",
            };
            for (int i = 0; i < phase.LineText.Length && i < phase.LineSpeaker.Length; i++)
            {
                if (System.Array.IndexOf(flagshipLines, phase.LineText[i]) >= 0) { phase.LineSpeaker[i] = ACO_PhaseBoss.SPEAKER_ALLY; }
            }
            MarkChanged(phase);
        }

        // 먼저 만들어 둔 프리팹: 포대도 수송기·기함의 AttackPriority를 따르게(한 번만 — 그 뒤 직접 바꾼 값은 그대로)
        foreach (ACO_EscortUnit e in Object.FindObjectsOfType<ACO_EscortUnit>(true))
        {
            if (e.PriorityDefaultsApplied) { continue; }
            e.PriorityDefaultsApplied = true;
            bool flagship = e.Role == ACO_EscortUnit.ROLE_FLAGSHIP;
            if (flagship) { e.AttackPriority = 2500f; }
            if (flagship || (e.MoveMode == ACO_EscortUnit.MOVE_AIR && !e.Hostile && e.Role == ACO_EscortUnit.ROLE_VIP)) { e.PriorityForSites = true; }
            MarkChanged(e);
        }
        const int friendlyShips = 1 << 26;
        int changed = 0;
        foreach (ACO_Warship ship in Object.FindObjectsOfType<ACO_Warship>(true))
        {
            if (ship.ExternalPose) { continue; }   // 공중 요새는 함대전에 나오지 않는다
            if (ship.MarkerLabel == "BB")
            {
                ship.MarkerLabel = "CG";
                ship.TargetName = "Cruiser";
                MarkChanged(ship);
                changed++;
            }
            if (ship.Parts == null) { continue; }
            // 사거리는 한 번만 — 두 함대는 3.5km에서 멈춘다. 그 뒤 직접 바꾼 값은 그대로 둔다
            bool tune = ship.FleetTuneVersion < FleetTuneVersion;
            if (tune) { ship.FleetTuneVersion = FleetTuneVersion; MarkChanged(ship); }
            foreach (UdonSharp.UdonSharpBehaviour part in ship.Parts)
            {
                ACO_SAMSite sam = part as ACO_SAMSite;
                ACO_AAGunSite gun = part as ACO_AAGunSite;
                if (sam != null)
                {
                    bool dirty = false;
                    if ((sam.TargetLayers.value & friendlyShips) == 0) { sam.TargetLayers = sam.TargetLayers.value | friendlyShips; dirty = true; }
                    if (sam.MinTargetAltitude > -100000f) { sam.MinTargetAltitude = -100000f; dirty = true; }
                    if (tune && sam.DetectRange < 5000f) { sam.DetectRange = 5000f; dirty = true; }
                    if (tune && sam.LoseTargetRange < 6000f) { sam.LoseTargetRange = 6000f; dirty = true; }
                    if (sam.EntityControl != null && (sam.EntityControl.AAMTargetsLayer.value & friendlyShips) == 0)
                    {
                        sam.EntityControl.AAMTargetsLayer |= friendlyShips;
                        MarkChanged(sam.EntityControl);
                        dirty = true;
                    }
                    if (dirty) { MarkChanged(sam); changed++; }
                }
                // 함포만(CIWS는 대공·요격 그대로)
                if (gun != null && part.gameObject.name.Contains("MainGun"))
                {
                    bool dirty = false;
                    if ((gun.TargetLayers.value & friendlyShips) == 0) { gun.TargetLayers = gun.TargetLayers.value | friendlyShips; dirty = true; }
                    if (gun.MinTargetAltitude > -100000f) { gun.MinTargetAltitude = -100000f; dirty = true; }
                    if (gun.EntityControl != null && (gun.EntityControl.AAMTargetsLayer.value & friendlyShips) == 0)
                    {
                        gun.EntityControl.AAMTargetsLayer |= friendlyShips;
                        MarkChanged(gun.EntityControl);
                        dirty = true;
                    }
                    if (dirty) { MarkChanged(gun); changed++; }
                }
            }
        }
        if (changed > 0) { Debug.Log($"[ACO] 함대전: 함선 이름·표적 설정 {changed}곳을 고쳤습니다(BB → CG, VLS·함포가 아군 함선도 노린다)."); }
    }

    /// TEL: 시한 표적 전용 풀 — 풀에 TelPoolSize대가 되게 프리팹(없으면 만든다)을 넣는다.
    private const int TelPoolSize = 3;
    private static void EnsureTels(Transform poolRoot)
    {
        int have = poolRoot.GetComponentsInChildren<ACO_TelUnit>(true).Length;
        if (have >= TelPoolSize) { return; }
        GameObject prefab = ACO_PrefabBuilder.Exists("NPC_TEL")
            ? AssetDatabase.LoadAssetAtPath<GameObject>(ACO_PrefabBuilder.PathOf("NPC_TEL"))
            : ACO_PrefabBuilder.BuildTel();
        if (prefab == null) { return; }
        for (int i = have; i < TelPoolSize; i++)
        {
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, poolRoot);
            Undo.RegisterCreatedObjectUndo(go, "ACO TEL");
            go.SetActive(false);
            MarkChanged(go);
        }
        Debug.Log($"[ACO] TEL NPC_TEL을 유닛 풀에 {TelPoolSize - have}대 넣었습니다.");
    }

    /// 시한 표적 ↔ TEL: 발사·상승 시간(모듈 값), 끝 고도(씬 SAV 최대 고도 — 대기가 0이 되는 높이), 무전.
    private static void BakeTimeSensitive()
    {
        ACO_PhaseTimeSensitive phase = Object.FindObjectOfType<ACO_PhaseTimeSensitive>(true);
        if (phase == null) { return; }
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        if (phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }
        const string oldStart = "High-value targets are on the move. Hit each one before it escapes!";
        if (phase.StartText == oldStart)
        {
            phase.StartText = "Ballistic missile launchers are setting up! Stop every launch!";
            phase.SuccessText = "All launchers are down. No missiles got through.";
            phase.FailText = "MISSILE LAUNCHED";
            phase.FailChatterText = "The missile is out of reach. We failed. All aircraft, return to base.";
            MarkChanged(phase);
        }
        SaccFlightAndVehicles.SaccAirVehicle sav = Object.FindObjectOfType<SaccFlightAndVehicles.SaccAirVehicle>(true);
        float ceiling = sav != null ? sav.AtmosphereThinningEnd + sav.SeaLevel : 19812f;
        foreach (ACO_TelUnit tel in Object.FindObjectsOfType<ACO_TelUnit>(true))
        {
            float flight = Mathf.Max(phase.TimeLimit - phase.LaunchDelay, 1f);
            if (Mathf.Approximately(tel.LaunchDelay, phase.LaunchDelay) && Mathf.Approximately(tel.FlightTime, flight) && Mathf.Approximately(tel.CeilingAltitude, ceiling)) { continue; }
            tel.LaunchDelay = phase.LaunchDelay;
            tel.FlightTime = flight;
            tel.CeilingAltitude = ceiling;
            MarkChanged(tel);
        }
    }

    /// 공중 요새 ↔ 보스 페이즈(동기화된 단계·기준값).
    private static void BakeFortress()
    {
        ACO_FlyingFortress fortress = Object.FindObjectOfType<ACO_FlyingFortress>(true);
        ACO_PhaseFortress phase = Object.FindObjectOfType<ACO_PhaseFortress>(true);
        if (fortress == null) { return; }
        if (phase == null) { Debug.LogWarning("[ACO] 공중 요새: 페이즈 모듈(ACO_PhaseFortress)이 없습니다.", fortress); return; }
        if (fortress.Phase != phase) { fortress.Phase = phase; MarkChanged(fortress); }
        ACO_Chatter chatter = Object.FindObjectOfType<ACO_Chatter>(true);
        if (phase.Chatter != chatter) { phase.Chatter = chatter; MarkChanged(phase); }

        // 땅을 재는 쪽은 요새 외피 레이어를 뺀다 — 위에서 쏘는 레이가 선체에 맞으면 유닛이 그 위에 놓인다
        int hullBit = 1 << ACO_PrefabBuilder.LayerFortressHull;
        foreach (ACO_MissionGenerator g in Object.FindObjectsOfType<ACO_MissionGenerator>(true))
        {
            if ((g.GroundLayers.value & hullBit) != 0) { g.GroundLayers = g.GroundLayers.value & ~hullBit; MarkChanged(g); }
        }
        foreach (ACO_MobileUnit u in Object.FindObjectsOfType<ACO_MobileUnit>(true))
        {
            if ((u.GroundLayers.value & hullBit) != 0) { u.GroundLayers = u.GroundLayers.value & ~hullBit; MarkChanged(u); }
        }
        foreach (ACO_EscortUnit u in Object.FindObjectsOfType<ACO_EscortUnit>(true))
        {
            if ((u.GroundLayers.value & hullBit) != 0) { u.GroundLayers = u.GroundLayers.value & ~hullBit; MarkChanged(u); }
        }
    }

    /// 미션 생성기의 유닛 풀 = UnitPoolRoot(비면 생성기 자신) 아래의 NPC. 비활성 유닛도 포함한다.
    private static void BakeMissionGenerators()
    {
        var generators = new List<ACO_MissionGenerator>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            generators.AddRange(root.GetComponentsInChildren<ACO_MissionGenerator>(true));
        }

        foreach (ACO_MissionGenerator generator in generators)
        {
            Transform poolRoot = generator.UnitPoolRoot != null ? generator.UnitPoolRoot : generator.transform;
            EnsureFortress(poolRoot);
            EnsureTels(poolRoot);
            EnsureFleet(poolRoot);
            EnsureRailgun(poolRoot);

            generator.Fighters = poolRoot.GetComponentsInChildren<ACO_NPCFighterAI>(true);

            // 함선 부위(ACO_Warship·ACO_EscortUnit 아래의 SAM/대공포)는 함선이 데리고 다니므로 지상 풀에 넣지 않는다.
            var ground = new List<UdonSharp.UdonSharpBehaviour>();
            foreach (ACO_SAMSite sam in poolRoot.GetComponentsInChildren<ACO_SAMSite>(true))
            {
                if (sam.GetComponentInParent<ACO_Warship>(true) == null && sam.GetComponentInParent<ACO_EscortUnit>(true) == null) { ground.Add(sam); }
            }
            foreach (ACO_AAGunSite aa in poolRoot.GetComponentsInChildren<ACO_AAGunSite>(true))
            {
                // 전차의 포탑도 전차가 데리고 다닌다(전차 자신이 지상 풀에 들어간다).
                if (aa.GetComponentInParent<ACO_Warship>(true) == null && aa.GetComponentInParent<ACO_MobileUnit>(true) == null && aa.GetComponentInParent<ACO_EscortUnit>(true) == null) { ground.Add(aa); }
            }
            ground.AddRange(poolRoot.GetComponentsInChildren<ACO_MobileUnit>(true));
            ground.AddRange(poolRoot.GetComponentsInChildren<ACO_TelUnit>(true));   // 전용 풀 — 생성기가 일반 배치에서 뺀다
            // 함선: HighValue(전함)는 고가치 표적 풀, 아니면(구축함) 지상 풀 — 생성기가 둘 다 바다에 놓는다.
            var hvts = new List<UdonSharp.UdonSharpBehaviour>();
            hvts.AddRange(poolRoot.GetComponentsInChildren<ACO_StaticTarget>(true));
            foreach (ACO_Warship ship in poolRoot.GetComponentsInChildren<ACO_Warship>(true))
            {
                if (ship.HighValue) { hvts.Add(ship); }
                else { ground.Add(ship); }
            }
            generator.GroundUnits = ground.ToArray();
            generator.HighValueTargets = hvts.ToArray();
            generator.Fortress = poolRoot.GetComponentInChildren<ACO_FlyingFortress>(true);
            generator.Manager = Object.FindObjectOfType<ACO_MissionManager>(true);
            generator.Railgun = poolRoot.GetComponentInChildren<ACO_Railgun>(true);
            generator.Carrier = null;
            foreach (ACO_Warship ship in poolRoot.GetComponentsInChildren<ACO_Warship>(true)) { if (ship.ReservedForFleet) { generator.Carrier = ship; } }
            generator.EscortUnits = poolRoot.GetComponentsInChildren<ACO_EscortUnit>(true);
            // 곡사포는 생성기의 호위 대상 목록에서 표적을 고른다.
            foreach (ACO_ArtilleryGun art in poolRoot.GetComponentsInChildren<ACO_ArtilleryGun>(true))
            {
                if (art.Generator == generator) { continue; }
                art.Generator = generator;
                PrefabUtility.RecordPrefabInstancePropertyModifications(art);
                EditorUtility.SetDirty(art);
            }
            // 씬에 육각 지형이 있으면 연결한다(미션마다 새 씨앗으로 다시 만든다). 칸 크기는 기둥 프리팹 메시와 같게 맞춘다.
            foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (ACO_HexTerrain hex in r.GetComponentsInChildren<ACO_HexTerrain>(true))
                {
                    WarnHexMeshMismatch(hex);
                    // 프리셋이 늘었다: 예전(짧은) 비율 배열이면 새 칸을 기본값으로 늘린다(안 늘리면 새 프리셋이 안 나온다)
                    float[] defW = { 1f, 1f, 1f, 0.5f, 1f, 1f, 1f, 1f, 1f, 1f, 0.7f, 1f };
                    if (hex.PresetWeights == null || hex.PresetWeights.Length < defW.Length)
                    {
                        var w = new float[defW.Length];
                        for (int k = 0; k < w.Length; k++) { w[k] = hex.PresetWeights != null && k < hex.PresetWeights.Length ? hex.PresetWeights[k] : defW[k]; }
                        hex.PresetWeights = w;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(hex);
                        EditorUtility.SetDirty(hex);
                        Debug.Log("[ACO] 육각 지형 프리셋 비율에 새 지형(해안·화산섬·암초 군도·분지)을 넣었습니다.", hex);
                    }
                    if (Mathf.Approximately(hex.HexRadius, ACO_PrefabBuilder.HexRadius)) { continue; }
                    hex.HexRadius = ACO_PrefabBuilder.HexRadius;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(hex);
                    EditorUtility.SetDirty(hex);
                    Debug.Log($"[ACO] 육각 지형 '{hex.name}'의 HexRadius를 기둥 프리팹에 맞춰 {hex.HexRadius}m로 바꿨습니다.", hex);
                }
            }
            if (generator.HexTerrain == null)
            {
                foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    ACO_HexTerrain hex = r.GetComponentInChildren<ACO_HexTerrain>(true);
                    if (hex != null) { generator.HexTerrain = hex; break; }
                }
            }
            // 지형 장식: 지형/생성기와 서로 연결하고 비어 있는 프리팹 칸을 빌더 프리팹으로 채운다.
            ACO_TerrainFeatures features = FindFeatures();
            if (features != null)
            {
                LinkFeatures(features, generator.HexTerrain);
                if (generator.Features == null) { generator.Features = features; }
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
            EditorUtility.SetDirty(generator);

            Debug.Log($"[ACO] 미션 생성기 '{generator.name}': 전투기 {generator.Fighters.Length}기, 지상 유닛 {generator.GroundUnits.Length}대, " +
                      $"고가치 표적 {generator.HighValueTargets.Length}개, 호위 대상 {generator.EscortUnits.Length}기를 풀로 수집했습니다.", generator);
        }

        // 미션 관리자: 생성기가 비어 있으면 씬의 (첫 번째) 생성기를 연결한다.
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissionManager manager in root.GetComponentsInChildren<ACO_MissionManager>(true))
            {
                if (manager.Generator != null || generators.Count == 0) { continue; }
                manager.Generator = generators[0];
                PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
                EditorUtility.SetDirty(manager);
                Debug.Log($"[ACO] 미션 관리자 '{manager.name}'에 미션 생성기 '{generators[0].name}'를 연결했습니다.", manager);
            }
        }

        // HUD: 비어 있으면 씬의 (첫 번째) 생성기/관리자를 연결한다.
        ACO_MissionManager firstManager = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_MissionManager found = root.GetComponentInChildren<ACO_MissionManager>(true);
            if (found != null) { firstManager = found; break; }
        }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_TargetMarkers markers in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
            {
                if (ACO_HUDBuilder.FillMarkerMaterials(markers))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(markers);
                    EditorUtility.SetDirty(markers);
                    Debug.Log($"[ACO] 표적 마커 '{markers.name}'에 아군/경유점/목적지 머티리얼을 채웠습니다.", markers);
                }
                if (markers.Generator != null || generators.Count == 0) { continue; }
                markers.Generator = generators[0];
                PrefabUtility.RecordPrefabInstancePropertyModifications(markers);
                EditorUtility.SetDirty(markers);
            }
            foreach (ACO_ScoreHUD hud in root.GetComponentsInChildren<ACO_ScoreHUD>(true))
            {
                if (ACO_HUDBuilder.EnsureNoticeText(hud)) { Debug.Log($"[ACO] 점수 HUD '{hud.name}'에 안내 글자를 만들었습니다.", hud); }
                if (ACO_HUDBuilder.EnsureResultGroup(hud)) { Debug.Log($"[ACO] 점수 HUD '{hud.name}'에 결과·안내 묶음(ResultGroup)을 만들었습니다 — 두 글자는 실행 중 묶음으로 옮겨집니다.", hud); }
                if (hud.Manager != null || firstManager == null) { continue; }
                hud.Manager = firstManager;
                PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
                EditorUtility.SetDirty(hud);
            }
            foreach (ACO_TargetMarkers markers in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
            {
                if (markers.Manager != null || firstManager == null) { continue; }
                markers.Manager = firstManager;
                PrefabUtility.RecordPrefabInstancePropertyModifications(markers);
                EditorUtility.SetDirty(markers);
            }
            foreach (ACO_MissionStartButton button in root.GetComponentsInChildren<ACO_MissionStartButton>(true))
            {
                if (button.Manager != null || firstManager == null) { continue; }
                button.Manager = firstManager;
                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
                EditorUtility.SetDirty(button);
            }
            foreach (ACO_MissionLog log in root.GetComponentsInChildren<ACO_MissionLog>(true))
            {
                bool changed = false;
                if (log.Manager == null && firstManager != null) { log.Manager = firstManager; changed = true; }
                if (log.Generator == null)
                {
                    // 관리자가 쓰는 생성기와 맞춘다(점수 계산이 같은 풀을 봐야 한다).
                    ACO_MissionGenerator gen = log.Manager != null && log.Manager.Generator != null ? log.Manager.Generator
                        : (generators.Count > 0 ? generators[0] : null);
                    if (gen != null) { log.Generator = gen; changed = true; }
                }
                if (!changed) { continue; }
                PrefabUtility.RecordPrefabInstancePropertyModifications(log);
                EditorUtility.SetDirty(log);
            }
        }
    }

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

    private static bool HasMissing(GameObject[] list)
    {
        if (list == null || list.Length == 0) { return true; }
        foreach (GameObject g in list) { if (g == null) { return true; } }
        return false;
    }

    private static GameObject[] LoadAll(string[] names)
    {
        var list = new List<GameObject>();
        foreach (string n in names)
        {
            GameObject g = ACO_DecoBuilder.Load(n);
            if (g != null) { list.Add(g); }
        }
        return list.ToArray();
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
