using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using ACO;

/// 게임 진행·미션 — 전체 순서는 ACO_SceneBaker.BakeNeighbors.
public partial class ACO_SceneBaker
{
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
        ACO_ScoreHUD flowHud = Object.FindObjectOfType<ACO_ScoreHUD>(true);
        if (flow.Hud != flowHud) { flow.Hud = flowHud; MarkChanged(flow); }
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
        // 출격 카운트다운 판: 상태 글자·배너가 있는 패널(캔버스)마다 하나
        var overlays = new List<GameObject>();
        var overlayTexts = new List<TMPro.TextMeshProUGUI>();
        var panelTexts = new List<TMPro.TextMeshProUGUI>(status);
        panelTexts.AddRange(banners);
        foreach (TMPro.TextMeshProUGUI t in panelTexts)
        {
            Canvas c = t.canvas != null ? t.canvas.rootCanvas : t.GetComponentInParent<Canvas>(true);
            RectTransform canvasRt = c != null ? (RectTransform)c.transform : t.transform.parent as RectTransform;
            if (canvasRt == null) { continue; }
            Transform found = canvasRt.Find(ACO_HUDBuilder.FlowCountdownName);
            GameObject ov = found != null ? found.gameObject : null;
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
    public static void MigrateUpgradeArrays(ACO_PlayerProgress progress)
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
            up.GunBonuses = CollectGunBonuses(entity);
            MarkChanged(up);

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

            generator.Fighters = poolRoot.GetComponentsInChildren<ACO_NPCFighterAI>(true);

            // 함선 부위(ACO_Warship·ACO_EscortUnit 아래의 SAM/대공포)는 함선이 데리고 다니므로 지상 풀에 넣지 않는다.
            var ground = new List<UdonSharp.UdonSharpBehaviour>();
            foreach (ACO_SAMSite sam in poolRoot.GetComponentsInChildren<ACO_SAMSite>(true))
            {
                if (sam.GetComponentInParent<ACO_Warship>(true) == null && sam.GetComponentInParent<ACO_EscortUnit>(true) == null) { ground.Add(sam); }
            }
            foreach (ACO_AAGunSite aa in poolRoot.GetComponentsInChildren<ACO_AAGunSite>(true))
            {
                // 전차의 포탑도 전차가 데리고 다닌다(전차 자신이 지상 풀에 들어간다). 레이저 에이스의 렌즈 포탑도 기체가 데리고 다닌다.
                if (aa.GetComponentInParent<ACO_Warship>(true) == null && aa.GetComponentInParent<ACO_MobileUnit>(true) == null && aa.GetComponentInParent<ACO_EscortUnit>(true) == null
                    && aa.GetComponentInParent<ACO_NPCFighterAI>(true) == null) { ground.Add(aa); }
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
            generator.LandShip = poolRoot.GetComponentInChildren<ACO_LandShip>(true);
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

    /// 강화 배열이 지금 항목 수·최대 단계와 맞는가.
    public static bool UpgradeArraysCurrent(ACO_PlayerProgress progress)
    {
        var temp = new GameObject("ACO_ProgressDefaults") { hideFlags = HideFlags.HideAndDontSave };
        int scale = temp.AddComponent<ACO_PlayerProgress>().LevelCostScale.Length;
        Object.DestroyImmediate(temp);
        int n = ACO_PlayerProgress.UPGRADE_COUNT;
        return progress.UpgradeNames != null && progress.UpgradeNames.Length == n
            && progress.UpgradeSteps != null && progress.UpgradeSteps.Length == n
            && progress.UpgradeBaseCosts != null && progress.UpgradeBaseCosts.Length == n
            && progress.LevelCostScale != null && progress.LevelCostScale.Length == scale;
    }

    private static readonly string[] TerrainDefaultNames = { "BIG ISLAND", "ARCHIPELAGO", "TWIN ISLANDS", "ATOLL", "ISLAND CHAIN", "PLATEAU", "MOUNTAIN RANGE", "CANYON", "COASTLINE", "VOLCANO", "REEF ISLANDS", "BASIN" };
    private static readonly float[] TerrainDefaultWeights = { 1f, 1f, 1f, 0.5f, 1f, 1f, 1f, 1f, 1f, 1f, 0.7f, 1f };

    /// 지형 프리셋이 늘어 이름·비율 배열이 짧은가.
    public static bool TerrainArraysShort()
    {
        foreach (ACO_GameFlow flow in Object.FindObjectsOfType<ACO_GameFlow>(true))
        {
            if (flow.TerrainNames == null || flow.TerrainNames.Length < TerrainDefaultNames.Length) { return true; }
        }
        foreach (ACO_HexTerrain hex in Object.FindObjectsOfType<ACO_HexTerrain>(true))
        {
            if (hex.PresetWeights == null || hex.PresetWeights.Length < TerrainDefaultWeights.Length) { return true; }
        }
        return false;
    }

    /// 짧은 배열만 늘린다. 있던 값은 그대로 둔다.
    public static void GrowTerrainArrays()
    {
        foreach (ACO_GameFlow flow in Object.FindObjectsOfType<ACO_GameFlow>(true))
        {
            if (flow.TerrainNames != null && flow.TerrainNames.Length >= TerrainDefaultNames.Length) { continue; }
            var names = new string[TerrainDefaultNames.Length];
            for (int k = 0; k < names.Length; k++) { names[k] = flow.TerrainNames != null && k < flow.TerrainNames.Length ? flow.TerrainNames[k] : TerrainDefaultNames[k]; }
            flow.TerrainNames = names;
            MarkChanged(flow);
        }
        foreach (ACO_HexTerrain hex in Object.FindObjectsOfType<ACO_HexTerrain>(true))
        {
            if (hex.PresetWeights != null && hex.PresetWeights.Length >= TerrainDefaultWeights.Length) { continue; }
            var w = new float[TerrainDefaultWeights.Length];
            for (int k = 0; k < w.Length; k++) { w[k] = hex.PresetWeights != null && k < hex.PresetWeights.Length ? hex.PresetWeights[k] : TerrainDefaultWeights[k]; }
            hex.PresetWeights = w;
            MarkChanged(hex);
        }
    }
}
