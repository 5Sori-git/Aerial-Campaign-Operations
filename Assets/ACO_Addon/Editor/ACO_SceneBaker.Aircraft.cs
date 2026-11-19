using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using ACO;

/// 플레이어 기체 — 전체 순서는 ACO_SceneBaker.BakeNeighbors.
public partial class ACO_SceneBaker
{
    /// 데칼: 스튜디오(격납고) 미리보기 프로젝터를 샘플 기체 기준점 아래로, 샘플 메시에 콜라이더(데칼 건이 겨눈다 — 없는 것만 MeshCollider),
    /// 플레이어 기체마다 ACO_VehicleDecal(기준점 = SaccEntity 루트 자식 "ACO_Decals" + 프로젝터 2개, 머티리얼은 기체마다 따로) ↔ 스튜디오·기체 강화 확장.
    /// 플레이어 기체 확장(ACO_VehicleUpgrades)의 안전지대 보호용 락온 마커: SaccEntity 아래 이름이 "AAMTarget"이고 아군 항공기 레이어(25)인 오브젝트.
    private static void BakeSafeLockTargets()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_VehicleUpgrades up in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true))
            {
                SaccFlightAndVehicles.SaccEntity e = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
                if (e == null) { continue; }
                GameObject marker = null;
                foreach (Transform t in e.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "AAMTarget" && t.gameObject.layer == 25) { marker = t.gameObject; break; }
                }
                if (marker == null)
                {
                    Debug.LogWarning($"[ACO] 안전지대 보호: '{e.name}'에서 락온 마커(AAMTarget, 레이어 25)를 찾지 못했습니다 — 안전지대에서도 적이 노립니다.", e);
                    continue;
                }
                if (up.LockTarget != marker) { up.LockTarget = marker; MarkChanged(up); }
            }
        }
    }

    private static void BakeDecals()
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        // URL 저장소(Player Object 템플릿) → 스튜디오(복원되면 저장된 이미지를 다시 받게)
        foreach (ACO_UrlStore store in Object.FindObjectsOfType<ACO_UrlStore>(true))
        {
            if (store.Studio == studio) { continue; }
            store.Studio = studio;
            MarkChanged(store);
        }
        var decals = new List<ACO_VehicleDecal>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_VehicleUpgrades up in root.GetComponentsInChildren<ACO_VehicleUpgrades>(true))
            {
                SaccFlightAndVehicles.SaccEntity e = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
                if (e == null) { continue; }
                Transform holder = e.transform.Find("ACO_Decals");
                ACO_VehicleDecal vd = holder != null ? holder.GetComponent<ACO_VehicleDecal>() : null;
                if (vd == null) { continue; }   // 설치는 ACO_PlayerMissileSetup.SetupVehicle
                if (vd.Studio != studio) { vd.Studio = studio; MarkChanged(vd); }
                if (up.Decal != vd) { up.Decal = vd; MarkChanged(up); }
                decals.Add(vd);
            }
        }
        if (studio == null) { return; }
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
            foreach (ACO_VehicleDecal vd in decals)
            {
                if (vd.DecalKey != studio.DecalKey && vd.DecalKey == "default") { vd.DecalKey = studio.DecalKey; MarkChanged(vd); }
            }
        }
    }

    /// 비행 HUD(ACO_FlightHud): 플레이어 기체(ACO_VehicleUpgrades가 있는 SaccEntity)마다 SAV 비행·랜딩기어·플랩·후크·브레이크·기총·플레어,
    /// ACO 기본 미사일(특수무장 슬롯 밖)·특수무장 슬롯, 기총 탄도(파티클 탄속·중력·물려받기·발사 속도)를 같은 순서로 채운다. 레이더 접촉 목록·거리 단위를 잇고,
    /// 거리 단위를 표적 마커·페이즈 모듈·HUD 설정에, HideSavHud면 SAV HUD(SAV_HUDController)를 크기 0으로(끄면 다시 1로) 둔다.
    private static void BakeFlightHud()
    {
        ACO_FlightHud hud = Object.FindObjectOfType<ACO_FlightHud>(true);
        ACO_Units units = hud != null && hud.Units != null ? hud.Units : Object.FindObjectOfType<ACO_Units>(true);
        ACO_TacName tac = Object.FindObjectOfType<ACO_TacName>(true);
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_TargetMarkers c in root.GetComponentsInChildren<ACO_TargetMarkers>(true))
            {
                if (c.Units != units || c.TacNames != tac) { c.Units = units; c.TacNames = tac; MarkChanged(c); }
            }
            foreach (ACO_Chatter c in root.GetComponentsInChildren<ACO_Chatter>(true)) { if (c.TacNames != tac) { c.TacNames = tac; MarkChanged(c); } }   // 발사 무전 자막의 내 콜사인
            foreach (ACO_PhaseModule c in root.GetComponentsInChildren<ACO_PhaseModule>(true)) { if (c.Units != units) { c.Units = units; MarkChanged(c); } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true))
            {
                if (c.Units != units || c.FlightHud != hud) { c.Units = units; c.FlightHud = hud; MarkChanged(c); }
                // HUD 글자 그림자 켜기/끄기 대상(점수 패널·마커 글자, 비행 HUD 글자 재질)
                var shadow = new System.Collections.Generic.List<Material>();
                foreach (string matName in new[] { "ACO_HUDTextOverlay", "ACO_FlightHudTextOverlay" })
                {
                    Material m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/ACO_Addon/Generated/{matName}.mat");
                    if (m != null) { shadow.Add(m); }
                }
                if (c.TextShadowMaterials == null || c.TextShadowMaterials.Length != shadow.Count || !System.Linq.Enumerable.SequenceEqual(c.TextShadowMaterials, shadow))
                {
                    c.TextShadowMaterials = shadow.ToArray();
                    MarkChanged(c);
                }
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
        var supports = new ACO_DFUNC_Support[n];
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
            supports[i] = e.GetComponentInChildren<ACO_DFUNC_Support>(true);
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
        hud.Guns = guns; hud.FlareFuncs = flares; hud.Missiles = missiles; hud.Specials = specials; hud.Supports = supports;
        hud.GunSpeed = gunSpeed; hud.GunGravity = gunGravity; hud.GunInherit = gunInherit; hud.GunRate = gunRate;
        if (!hud.HideSavHud) { hiddenParts.Clear(); }
        hud.HiddenSavHudParts = hiddenParts.ToArray();
        LinkIls(hud);
        MarkChanged(hud);
        // 바뀐 게 없어도 매번 상태를 알린다(이미 숨겨 둔 SAV HUD는 "숨긴 상태")
        string savState = hud.HideSavHud ? "숨김" : "보임";
        Debug.Log($"[ACO] 비행 HUD: 플레이어 기체 {n}대 연결, SAV HUD(SAV_HUDController) {savHuds}개 {savState} 상태"
            + (hidden > 0 ? $" — 이번에 {hidden}개를 " + (hud.HideSavHud ? "숨겼습니다" : "다시 보이게 했습니다") : " — 바꾼 것 없음") + ".", hud);
    }

    /// ILS(6-201): 착함 와이어(이름이 ArrestingCable로 시작)를 착함 방향으로 줄 세워 가운데 것(4개면 선미에서 셋째)을 목표점으로,
    /// 가장 먼 두 와이어를 이은 수평 방향(함수 쪽 — HomeBase 앞과 같은 쪽)을 착함 방향으로 넣는다. 와이어가 없으면 비운다(ILS 꺼짐).
    private static void LinkIls(ACO_FlightHud hud)
    {
        var cables = new List<Transform>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(false)) { if (t.name.StartsWith("ArrestingCable")) { cables.Add(t); } }
        }
        Transform home = HomeBase();
        Transform aim = null;
        Vector3 dir = hud.IlsDir;
        if (cables.Count >= 2)
        {
            Vector3 a = cables[0].position, b = cables[1].position;
            float best = -1f;
            foreach (Transform x in cables)
            {
                foreach (Transform y in cables)
                {
                    Vector3 d = y.position - x.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > best) { best = d.sqrMagnitude; a = x.position; b = y.position; }
                }
            }
            dir = b - a;
            dir.y = 0f;
            dir.Normalize();
            if (home != null && Vector3.Dot(dir, home.forward) < 0f) { dir = -dir; }
            Vector3 along = dir;
            cables.Sort((p, q) => Vector3.Dot(p.position, along).CompareTo(Vector3.Dot(q.position, along)));
            aim = cables[cables.Count / 2];
        }
        else if (cables.Count == 1 && home != null)
        {
            aim = cables[0];
            dir = home.forward;
            dir.y = 0f;
            dir.Normalize();
        }
        if (hud.IlsAim != aim || (hud.IlsDir - dir).sqrMagnitude > 1e-6f) { hud.IlsAim = aim; hud.IlsDir = dir; }
        if (aim == null) { Debug.LogWarning("[ACO] 비행 HUD ILS: 착함 와이어(이름이 ArrestingCable로 시작하는 오브젝트)를 찾지 못해 ILS가 나오지 않습니다.", hud); return; }
        float heading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        if (heading < 0f) { heading += 360f; }
        Debug.Log($"[ACO] 비행 HUD ILS: 와이어 {cables.Count}개 — 목표점 '{aim.name}', 착함 방위 {heading:0}°.", hud);
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
        int linked = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissileLauncher c in root.GetComponentsInChildren<ACO_MissileLauncher>(true)) { if (c.Rwr != rwr) { c.Rwr = rwr; MarkChanged(c); linked++; } }
            foreach (ACO_DFUNC_Missile c in root.GetComponentsInChildren<ACO_DFUNC_Missile>(true)) { if (c.Rwr != rwr) { c.Rwr = rwr; MarkChanged(c); linked++; } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true)) { if (c.Rwr != rwr) { c.Rwr = rwr; MarkChanged(c); linked++; } }
        }
        if (linked > 0) { Debug.Log($"[ACO] RWR '{rwr.name}'을 {linked}곳에 연결했습니다.", rwr); }
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
        BakePilotOnly();
    }

    /// 탑승자 전용 충돌(6-259) ↔ 기체 자동화, 플레이어 기체 목록.
    private static void BakePilotOnly()
    {
        ACO_PilotOnlyColliders po = Object.FindObjectOfType<ACO_PilotOnlyColliders>(true);
        var autos = Object.FindObjectsOfType<ACO_VehicleAutomation>(true);
        foreach (ACO_VehicleAutomation a in autos)
        {
            if (a.PilotOnly != po) { a.PilotOnly = po; MarkChanged(a); }
        }
        if (po == null) { return; }
        var list = new List<SaccFlightAndVehicles.SaccEntity>();
        foreach (ACO_VehicleAutomation a in autos)
        {
            SaccFlightAndVehicles.SaccEntity e = a.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>();
            if (e != null && !list.Contains(e)) { list.Add(e); }
        }
        bool same = po.Aircraft != null && po.Aircraft.Length == list.Count;
        for (int i = 0; same && i < list.Count; i++) { if (po.Aircraft[i] != list[i]) { same = false; } }
        if (!same) { po.Aircraft = list.ToArray(); MarkChanged(po); }
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

    /// 캐터펄트 위치(갑판의 Trigger). 기체에 달린 것은 뺀다.
    public static List<Transform> CatapultTriggers()
    {
        var triggers = new List<Transform>();
        foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t.name == "Trigger" && t.gameObject.layer == 24 && t.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true) == null) { triggers.Add(t); }
        }
        return triggers;
    }

    /// Setup Check [고치기]: 애니메이션·프리팹을 맞추고, 빠진 캐터펄트에 갑판 요원을 놓고, JBD와 발함지휘소 요원을 잇는다.
    public static void InstallCatapultCrew()
    {
        List<Transform> triggers = CatapultTriggers();
        if (triggers.Count == 0) { return; }
        ACO_PrefabBuilder.EnsureCrewAnimations();
        GameObject prefab = ACO_PrefabBuilder.EnsureCatapultCrewPrefab();
        ACO_PrefabBuilder.EnsureCatapultCrewExtras();
        ACO_PrefabBuilder.EnsureCatapultCrewModels();
        if (prefab == null) { return; }

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
        RemoveLaunchBubbleLid();   // 6-259
        FillLaunchBubblePit();
        MakeLaunchBubbleFloorPilotOnly();
        InstallLaunchOperator(crews);
    }

    private static void InstallLaunchOperator(List<ACO_CatapultCrew> crews)
    {
        if (Object.FindObjectOfType<ACO_LaunchOperator>(true) != null) { return; }
        Transform station = FindSceneTransform("発艦指揮所", Vector3.zero);
        GameObject prefab = ACO_PrefabBuilder.EnsureLaunchOperatorPrefab();
        if (station == null || prefab == null || crews.Count == 0) { Debug.LogWarning("[ACO] 발함지휘소 또는 요원 프리팹을 찾지 못해 지휘소 요원을 놓지 않았습니다."); return; }
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
        Debug.Log($"[ACO] 발함지휘소 요원을 놓았습니다: 위치 {pos}, 지휘소 크기 {b.size} — 어긋나면 씬에서 옮기세요.", go);
    }

    /// 갑판 요원 ↔ 캐터펄트·HUD 설정·지휘소 요원 연결. 설치는 Setup Check가 맡는다.
    private static void BakeCatapultCrew()
    {
        var crews = new List<ACO_CatapultCrew>(Object.FindObjectsOfType<ACO_CatapultCrew>(true));
        if (crews.Count == 0) { return; }
        ACO_LaunchOperator op = Object.FindObjectOfType<ACO_LaunchOperator>(true);
        if (op != null && (op.Crews == null || !new HashSet<ACO_CatapultCrew>(op.Crews).SetEquals(crews)))
        {
            op.Crews = crews.ToArray();
            MarkChanged(op);
        }
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
        FixJbdCollision(crew);
    }

    // ───────────── JBD 충돌 없애기 · 자리 바닥 (6-253) ─────────────
    // 항모 갑판 충돌(모델 메시)은 JBD 자리가 약 1m 파여 있고, 그 위를 JBD 판의 충돌이 덮고 있었다. JBD 충돌을 지우면 바퀴 · 발이 빠지므로
    // 갑판 높이에 맞춘 납작한 상자 충돌을 JBD 옆(같은 부모 — 항모와 함께 움직인다)에 둔다. JBD는 모양만 움직인다(올라가 있어도 지나간다).
    private const string JbdFloorSuffix = " Floor";
    private const float JbdFloorThickness = 0.3f;
    private const float JbdFloorMargin = 0.1f;   // 구덩이 가장자리 갑판에 걸치게(이음매 없게)
    private const float JbdScanStep = 0.1f;
    private const int JbdDeckMask = 2049;        // 기체 접지와 같은 마스크

    /// JBD 자리 바닥(JBD와 같은 부모의 "<JBD 이름> Floor" + BoxCollider). 없으면 null.
    public static BoxCollider FindJbdFloor(Transform jbd)
    {
        if (jbd == null || jbd.parent == null) { return null; }
        Transform f = jbd.parent.Find(jbd.name + JbdFloorSuffix);
        return f != null ? f.GetComponent<BoxCollider>() : null;
    }

    /// 바닥이 없으면 구덩이를 재서 만들고(JBD 충돌을 잠깐 끄고 잰다), JBD와 그 자식의 충돌을 지운다. 여러 번 불러도 같다.
    private static void FixJbdCollision(ACO_CatapultCrew crew)
    {
        Transform jbd = crew.Jbd;
        if (jbd == null || jbd.parent == null) { return; }
        Collider[] cols = jbd.GetComponentsInChildren<Collider>(true);
        if (FindJbdFloor(jbd) == null)
        {
            var wasEnabled = new bool[cols.Length];
            for (int i = 0; i < cols.Length; i++) { wasEnabled[i] = cols[i].enabled; cols[i].enabled = false; }
            Physics.SyncTransforms();
            try { CreateJbdFloor(jbd); }
            finally
            {
                for (int i = 0; i < cols.Length; i++) { cols[i].enabled = wasEnabled[i]; }
                Physics.SyncTransforms();
            }
        }
        if (FindJbdFloor(jbd) == null) { return; }   // 바닥을 못 만들었으면 충돌을 남겨 둔다(빠지는 것보다 낫다)
        foreach (Collider c in cols)
        {
            if (c == null) { continue; }
            Debug.Log($"[ACO] {jbd.name}: 충돌({c.GetType().Name})을 지웠습니다(6-253).");
            Undo.DestroyObjectImmediate(c);
        }
        if (cols.Length > 0) { MarkChanged(jbd.gameObject); }
    }

    // ───────────── 발함 지휘소 뚜껑 충돌 없애기 (6-259) ─────────────
    // 두 캐터펄트 사이 지휘소(항모 모델 '発艦指揮所')는 갑판 위로 돔 · 테두리가 약 0.95m 솟아 기체 바퀴가 걸리면 떠서(SAV Taxiing 아님) 갑판 택싱 제한이 풀렸다.
    // 충돌 메시를 갑판 높이에서 잘라 위(뚜껑 · 턱)만 없앤 메시로 바꾼다 — 갑판 아래 구덩이 벽은 그대로. 원본 FBX는 건드리지 않는다.
    public const string LaunchBubbleName = "発艦指揮所";
    private const string LaunchBubbleMeshPath = "Assets/ACO_Addon/Generated/ACO_LaunchBubbleCollider.asset";

    /// 항모의 발함 지휘소 충돌(없으면 null).
    public static MeshCollider FindLaunchBubble()
    {
        foreach (MeshCollider c in Object.FindObjectsOfType<MeshCollider>(true)) { if (c.name == LaunchBubbleName) { return c; } }
        return null;
    }

    /// 지휘소 충돌이 아직 뚜껑까지 있는 원본 메시인가.
    public static bool LaunchBubbleLidRemains()
    {
        MeshCollider c = FindLaunchBubble();
        return c != null && c.sharedMesh != null && AssetDatabase.GetAssetPath(c.sharedMesh) != LaunchBubbleMeshPath;
    }

    /// 지휘소 충돌 메시를 둘레 갑판 높이에서 잘라 아래만 남긴 메시로 바꾼다(여러 번 불러도 같다).
    public static void RemoveLaunchBubbleLid()
    {
        MeshCollider c = FindLaunchBubble();
        if (c == null || c.sharedMesh == null || !LaunchBubbleLidRemains()) { return; }
        // 둘레 갑판 높이: 충돌 바깥 앞 · 뒤 · 좌 · 우 2m에서 잰 높이 중 가장 높은 값(지휘소 충돌은 잠깐 끄고 잰다)
        Bounds b = c.bounds;
        bool was = c.enabled;
        c.enabled = false;
        Physics.SyncTransforms();
        float deckY = float.MinValue;
        foreach (Vector3 d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        {
            Vector3 p = b.center + Vector3.Scale(d, b.extents + Vector3.one * 2f);
            if (Physics.Raycast(new Vector3(p.x, b.max.y + 10f, p.z), Vector3.down, out RaycastHit h, 30f, 2049, QueryTriggerInteraction.Ignore)) { deckY = Mathf.Max(deckY, h.point.y); }
        }
        c.enabled = was;
        Physics.SyncTransforms();
        if (deckY == float.MinValue) { Debug.LogWarning("[ACO] 발함 지휘소: 둘레 갑판을 찾지 못해 뚜껑 충돌을 그대로 둡니다."); return; }

        Mesh clipped = ClipMeshBelow(c.sharedMesh, c.transform, deckY);
        clipped.name = "ACO_LaunchBubbleCollider";
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(LaunchBubbleMeshPath);
        if (existing != null) { EditorUtility.CopySerialized(clipped, existing); clipped = existing; }
        else { AssetDatabase.CreateAsset(clipped, LaunchBubbleMeshPath); }
        AssetDatabase.SaveAssets();
        Undo.RecordObject(c, "ACO launch bubble lid");
        c.sharedMesh = clipped;
        MarkChanged(c);
        Debug.Log($"[ACO] 발함 지휘소: 갑판 높이 {deckY:0.00}에서 뚜껑 · 턱 충돌을 잘라냈습니다(삼각형 {clipped.triangles.Length / 3}개 남음, 6-259).");
    }

    /// 지휘소 구덩이 바닥(지휘소와 같은 부모의 "発艦指揮所 Floor" + BoxCollider). 없으면 null.
    public static BoxCollider FindLaunchBubbleFloor()
    {
        MeshCollider c = FindLaunchBubble();
        if (c == null || c.transform.parent == null) { return null; }
        Transform f = c.transform.parent.Find(LaunchBubbleName + JbdFloorSuffix);
        return f != null ? f.GetComponent<BoxCollider>() : null;
    }

    /// 지휘소 구덩이(약 2 × 4.5m · 깊이 3m — 뚜껑 충돌을 지워 열렸다)를 갑판 높이의 기본(Default) 레이어 바닥으로 덮는다(6-259).
    /// 워크스루 레이어는 SAV 땅 감지 레이(Default · Environment)에 안 잡혀 그 위에선 공중으로 판정되므로 쓰지 않는다. 여러 번 불러도 같다.
    public static void FillLaunchBubblePit()
    {
        MeshCollider c = FindLaunchBubble();
        if (c == null || c.transform.parent == null || FindLaunchBubbleFloor() != null) { return; }
        Transform parent = c.transform.parent;
        Vector3 right = Vector3.ProjectOnPlane(parent.right, Vector3.up).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(parent.forward, Vector3.up).normalized;
        Bounds b = c.bounds;
        Vector3 c0 = new Vector3(b.center.x, 0f, b.center.z);
        float reachA = Mathf.Abs(right.x) * b.extents.x + Mathf.Abs(right.z) * b.extents.z + 1.5f;
        float reachB = Mathf.Abs(fwd.x) * b.extents.x + Mathf.Abs(fwd.z) * b.extents.z + 1.5f;
        float top = b.max.y + 10f;

        bool was = c.enabled;
        c.enabled = false;   // 남은 구덩이 벽은 빼고 잰다
        Physics.SyncTransforms();
        try
        {
            float deckY = float.MinValue;
            GameObject deckObj = null;
            foreach (Vector3 d in new[] { fwd * (reachB + 1.5f), -fwd * (reachB + 1.5f), right * (reachA + 1.5f), -right * (reachA + 1.5f) })
            {
                Vector3 p = c0 + d;
                if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out RaycastHit h, 40f, JbdDeckMask, QueryTriggerInteraction.Ignore) && h.point.y > deckY)
                {
                    deckY = h.point.y;
                    deckObj = h.collider.gameObject;
                }
            }
            if (deckObj == null) { Debug.LogWarning("[ACO] 발함 지휘소: 둘레 갑판을 찾지 못해 구덩이 바닥을 만들지 않았습니다."); return; }

            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            int n = 0;
            for (float a = -reachA; a <= reachA; a += JbdScanStep)
            {
                for (float bb = -reachB; bb <= reachB; bb += JbdScanStep)
                {
                    Vector3 p = c0 + right * a + fwd * bb;
                    bool hit = Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out RaycastHit h, 40f, JbdDeckMask, QueryTriggerInteraction.Ignore);
                    if (hit && h.point.y > deckY - 0.3f) { continue; }   // 갑판(또는 그 위) — 구덩이 아님
                    n++;
                    minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
                    minB = Mathf.Min(minB, bb); maxB = Mathf.Max(maxB, bb);
                }
            }
            if (n == 0) { Debug.Log("[ACO] 발함 지휘소: 갑판에 파인 자리가 없어 바닥을 만들지 않았습니다."); return; }

            float sizeA = maxA - minA + JbdScanStep + JbdFloorMargin * 2f;
            float sizeB = maxB - minB + JbdScanStep + JbdFloorMargin * 2f;
            Vector3 center = c0 + right * ((minA + maxA) * 0.5f) + fwd * ((minB + maxB) * 0.5f);
            center.y = deckY - JbdFloorThickness * 0.5f;

            var go = new GameObject(LaunchBubbleName + JbdFloorSuffix);
            Undo.RegisterCreatedObjectUndo(go, "ACO launch bubble floor");
            go.layer = deckObj.layer;   // 갑판과 같은 레이어(Default) — SAV 땅 감지에 잡힌다
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(fwd, Vector3.up));
            go.transform.localScale = Vector3.one;
            Vector3 s = go.transform.lossyScale;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(sizeA / Mathf.Abs(s.x), JbdFloorThickness / Mathf.Abs(s.y), sizeB / Mathf.Abs(s.z));
            MarkChanged(go);
            Debug.Log($"[ACO] 발함 지휘소: 구덩이 바닥을 만들었습니다 — {sizeA:0.00} × {sizeB:0.00}m, 윗면 {deckY:0.00}, 레이어 {LayerMask.LayerToName(go.layer)}(6-259).");
        }
        finally
        {
            c.enabled = was;
            Physics.SyncTransforms();
        }
    }

    /// 지휘소 구덩이 바닥이 탑승자 전용(ACO_PilotOnlyColliders)인가 — 걸어 다니는 사람은 구덩이로 내려간다.
    public static bool LaunchBubbleFloorPilotOnly()
    {
        BoxCollider f = FindLaunchBubbleFloor();
        return f == null || f.GetComponent<ACO_PilotOnlyColliders>() != null;
    }

    /// 지휘소 구덩이 바닥에 탑승자 전용 스크립트를 붙인다(6-259). 기체 자동화 · 기체 목록 연결은 Bake(BakePilotOnly).
    public static void MakeLaunchBubbleFloorPilotOnly()
    {
        BoxCollider f = FindLaunchBubbleFloor();
        if (f == null || f.GetComponent<ACO_PilotOnlyColliders>() != null) { return; }
        ACO_PilotOnlyColliders po = f.gameObject.AddUdonSharpComponent<ACO_PilotOnlyColliders>();
        po.Colliders = new Collider[] { f };
        MarkChanged(po);
        Debug.Log("[ACO] 발함 지휘소: 구덩이 바닥을 탑승자 전용으로 했습니다 — 기체를 탄 사람 화면에만 바닥, 걸어 다니면 구덩이로 내려간다(6-259).");
    }

    /// 메시를 월드 높이 cutY 아래만 남긴다 — 걸치는 삼각형은 그 높이에서 자른다(Sutherland–Hodgman, 한 평면).
    private static Mesh ClipMeshBelow(Mesh src, Transform t, float cutY)
    {
        Vector3[] v = src.vertices;
        int[] tri = src.triangles;
        var outV = new List<Vector3>();
        var outT = new List<int>();
        var poly = new List<Vector3>(6);
        var next = new List<Vector3>(6);
        for (int i = 0; i < tri.Length; i += 3)
        {
            poly.Clear();
            poly.Add(t.TransformPoint(v[tri[i]])); poly.Add(t.TransformPoint(v[tri[i + 1]])); poly.Add(t.TransformPoint(v[tri[i + 2]]));
            next.Clear();
            for (int k = 0; k < poly.Count; k++)
            {
                Vector3 a = poly[k], bb = poly[(k + 1) % poly.Count];
                bool ain = a.y <= cutY, bin = bb.y <= cutY;
                if (ain) { next.Add(a); }
                if (ain != bin)
                {
                    float f = (cutY - a.y) / (bb.y - a.y);
                    next.Add(Vector3.Lerp(a, bb, f));
                }
            }
            if (next.Count < 3) { continue; }
            int start = outV.Count;
            for (int k = 0; k < next.Count; k++) { outV.Add(t.InverseTransformPoint(next[k])); }
            for (int k = 1; k + 1 < next.Count; k++) { outT.Add(start); outT.Add(start + k); outT.Add(start + k + 1); }
        }
        var m = new Mesh();
        if (outV.Count > 65000) { m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
        m.SetVertices(outV);
        m.SetTriangles(outT, 0);
        m.RecalculateBounds();
        m.RecalculateNormals();
        return m;
    }

    /// JBD 둘레를 항모 축(부모의 앞 · 오른쪽)으로 훑어, 둘레 갑판보다 0.3m 넘게 낮은 칸(구덩이)을 감싸는 상자를 갑판 높이에 만든다.
    private static void CreateJbdFloor(Transform jbd)
    {
        Renderer r = jbd.GetComponent<Renderer>();
        if (r == null) { return; }
        Transform parent = jbd.parent;
        Vector3 right = Vector3.ProjectOnPlane(parent.right, Vector3.up).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(parent.forward, Vector3.up).normalized;
        Bounds b = r.bounds;
        Vector3 c0 = new Vector3(b.center.x, 0f, b.center.z);
        float top = b.max.y + 5f;
        // 훑는 범위: 판 크기(항모 축으로) + 1.5m — 더 넓으면 캐터펄트 홈 같은 다른 파인 자리까지 잡는다
        float reachA = Mathf.Abs(right.x) * b.extents.x + Mathf.Abs(right.z) * b.extents.z + 1.5f;
        float reachB = Mathf.Abs(fwd.x) * b.extents.x + Mathf.Abs(fwd.z) * b.extents.z + 1.5f;

        // 둘레 갑판 높이: 판 바깥 앞 · 뒤 · 좌 · 우에서 잰 높이 중 가장 높은 값
        float deckY = float.MinValue;
        GameObject deckObj = null;
        foreach (Vector3 d in new[] { fwd * reachB, -fwd * reachB, right * reachA, -right * reachA })
        {
            Vector3 p = c0 + d + d.normalized * 1.5f;
            if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out RaycastHit h, 20f, JbdDeckMask, QueryTriggerInteraction.Ignore) && h.point.y > deckY)
            {
                deckY = h.point.y;
                deckObj = h.collider.gameObject;
            }
        }
        if (deckObj == null) { Debug.LogWarning($"[ACO] {jbd.name}: 둘레 갑판을 찾지 못해 바닥을 만들지 않았습니다."); return; }

        float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
        int n = 0;
        for (float a = -reachA; a <= reachA; a += JbdScanStep)
        {
            for (float bb = -reachB; bb <= reachB; bb += JbdScanStep)
            {
                Vector3 p = c0 + right * a + fwd * bb;
                if (!Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out RaycastHit h, 20f, JbdDeckMask, QueryTriggerInteraction.Ignore)) { continue; }
                if (h.point.y > deckY - 0.3f) { continue; }
                n++;
                minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
                minB = Mathf.Min(minB, bb); maxB = Mathf.Max(maxB, bb);
            }
        }
        if (n == 0) { Debug.LogWarning($"[ACO] {jbd.name}: 갑판에 파인 자리가 없어 바닥을 만들지 않았습니다."); return; }

        float sizeA = maxA - minA + JbdScanStep + JbdFloorMargin * 2f;
        float sizeB = maxB - minB + JbdScanStep + JbdFloorMargin * 2f;
        Vector3 center = c0 + right * ((minA + maxA) * 0.5f) + fwd * ((minB + maxB) * 0.5f);
        center.y = deckY - JbdFloorThickness * 0.5f;

        var go = new GameObject(jbd.name + JbdFloorSuffix);
        Undo.RegisterCreatedObjectUndo(go, "ACO JBD floor");
        go.layer = deckObj.layer;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(fwd, Vector3.up));
        go.transform.localScale = Vector3.one;
        Vector3 s = go.transform.lossyScale;
        var box = go.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(sizeA / Mathf.Abs(s.x), JbdFloorThickness / Mathf.Abs(s.y), sizeB / Mathf.Abs(s.z));
        MarkChanged(go);
        Debug.Log($"[ACO] {jbd.name}: 자리 바닥을 만들었습니다 — {sizeA:0.00} × {sizeB:0.00}m, 윗면 {deckY:0.00}(둘레 갑판), 파인 칸 {n}개(6-253).");
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
        }
        if (contacts.EcmPods == null || !new HashSet<ACO_DFUNC_Missile>(contacts.EcmPods).SetEquals(pods))
        {
            contacts.EcmPods = pods.ToArray();
            MarkChanged(contacts);
        }
        // 구역 안에서 들리는 소리는 특수무장 설치가 만든다
        Transform soundObj = contacts.transform.Find("EcmSound");
        AudioSource sound = soundObj != null ? soundObj.GetComponent<AudioSource>() : null;
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
        if (c.GetComponentInParent<ACO_SupportManager>(true) != null) { return true; }   // 가디언 드론(아군 지원)
        ACO_EscortUnit escort = c.GetComponentInParent<ACO_EscortUnit>(true);
        return escort != null && !escort.Hostile;
    }

    /// 음성 경고 ↔ 비행 HUD(내 기체·기어)·레이더 연락(내 기체)·진행도(안전지대), 그리고 경고를 부르는 쪽(랜스 포대·소형 레일건·레이저 에이스) → 음성 경고.
    public static void BakeVoiceWarning()
    {
        ACO_VoiceWarning voice = null;
        ACO_FlightHud hud = null;
        ACO_RadarContacts contacts = null;
        ACO_PlayerProgress progress = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (voice == null) { voice = root.GetComponentInChildren<ACO_VoiceWarning>(true); }
            if (hud == null) { hud = root.GetComponentInChildren<ACO_FlightHud>(true); }
            if (contacts == null) { contacts = root.GetComponentInChildren<ACO_RadarContacts>(true); }
            if (progress == null) { progress = root.GetComponentInChildren<ACO_PlayerProgress>(true); }
        }
        if (voice != null && (voice.FlightHud != hud || voice.Contacts != contacts || voice.Progress != progress))
        {
            voice.FlightHud = hud;
            voice.Contacts = contacts;
            voice.Progress = progress;
            MarkChanged(voice);
        }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Railgun c in root.GetComponentsInChildren<ACO_Railgun>(true)) { if (c.Voice != voice) { c.Voice = voice; MarkChanged(c); } }
            foreach (ACO_MiniRailgun c in root.GetComponentsInChildren<ACO_MiniRailgun>(true)) { if (c.Voice != voice) { c.Voice = voice; MarkChanged(c); } }
            foreach (ACO_Borderless c in root.GetComponentsInChildren<ACO_Borderless>(true)) { if (c.Voice != voice) { c.Voice = voice; MarkChanged(c); } }
            foreach (ACO_LunaAce c in root.GetComponentsInChildren<ACO_LunaAce>(true)) { if (c.Voice != voice) { c.Voice = voice; MarkChanged(c); } }
            foreach (ACO_MrS c in root.GetComponentsInChildren<ACO_MrS>(true)) { if (c.Voice != voice) { c.Voice = voice; MarkChanged(c); } }
        }
    }

    /// BGM ↔ 미션 관리자(상황 판정).
    public static void BakeBgm()
    {
        ACO_Bgm bgm = null;
        ACO_MissionManager manager = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (bgm == null) { bgm = root.GetComponentInChildren<ACO_Bgm>(true); }
            if (manager == null) { manager = root.GetComponentInChildren<ACO_MissionManager>(true); }
        }
        if (bgm != null && bgm.Manager != manager)
        {
            bgm.Manager = manager;
            MarkChanged(bgm);
        }
        var panels = new List<ACO_BgmPanel>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_BgmPanel p in root.GetComponentsInChildren<ACO_BgmPanel>(true))
            {
                panels.Add(p);
                if (p.Bgm != bgm) { p.Bgm = bgm; MarkChanged(p); }
            }
        }
        if (bgm != null && !SameList(bgm.Panels, panels))
        {
            bgm.Panels = panels.ToArray();
            MarkChanged(bgm);
        }
    }

    private static bool SameList<T>(T[] a, List<T> b) where T : Object
    {
        if (a == null) { return b.Count == 0; }
        if (a.Length != b.Count) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
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
        int launchers = 0;
        int maps = 0;
        Transform home = HomeBase();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_MissileLauncher c in root.GetComponentsInChildren<ACO_MissileLauncher>(true)) { if (c.Radar != contacts) { c.Radar = contacts; MarkChanged(c); launchers++; } }
            foreach (ACO_HudSettings c in root.GetComponentsInChildren<ACO_HudSettings>(true)) { if (hud != null && c.HudRadar != hud) { c.HudRadar = hud; MarkChanged(c); } }
            foreach (ACO_CockpitMap m in root.GetComponentsInChildren<ACO_CockpitMap>(true))
            {
                bool changed = false;
                if (m.Contacts != contacts) { m.Contacts = contacts; changed = true; }
                if (m.HomeBase != home) { m.HomeBase = home; changed = true; }   // 항모 표시(작전 테이블의 HomeBase)
                ACO_MissionLog gridLog = m.GridFilter != null ? Object.FindObjectOfType<ACO_MissionLog>(true) : null;
                if (m.GridLog != gridLog) { m.GridLog = gridLog; changed = true; }   // 좌표 격자(6-301 — 벽걸이 지도)
                if (!m.WallMode && m.EntityControl == null) { m.EntityControl = m.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true); changed = true; }
                if (changed) { MarkChanged(m); }
                maps++;
            }
        }
        if (flow == null || flow.Aircraft == null || flow.Aircraft.Length == 0) { Debug.LogWarning("[ACO] 레이더: 게임 진행의 참가자 기체(Aircraft)가 없어 HUD 레이더·조종석 지도가 내 기체를 알 수 없습니다.", contacts); }
        Debug.Log($"[ACO] 레이더 연결: 미사일 런처 {launchers}곳 새로 연결, 조종석 지도 {maps}개" + (maps == 0 ? " (ACO → Player Vehicle → Add Cockpit Map to Selected Vehicles로 기체에 단다)" : ""), contacts);
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
                PrefabUtility.RecordPrefabInstancePropertyModifications(markers);
                EditorUtility.SetDirty(markers);
            }
        }
    }

    /// 샘플 기체의 표시 자리를 기체 프리팹에 맞춘다. 샘플에 표시를 넣는 일은 기체 설치가 맡는다.
    private static void BakeVehicleMarks()
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        if (studio == null || studio.SampleRoot == null || studio.SampleRoot.Find("ACO_Marks") == null) { return; }
        foreach (ACO_VehicleUpgrades up in Object.FindObjectsOfType<ACO_VehicleUpgrades>(true))
        {
            var entity = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            if (entity == null) { continue; }
            ACO_HUDBuilder.EnsurePreviewMarks(studio.SampleRoot, entity);
            return;
        }
    }

    /// 총탄이 스치는 소리 ↔ 나를 쏠 수 있는 기총 연결. 만드는 일은 Setup Check가 맡는다.
    private static void BakeBulletFlyby()
    {
        ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
        ACO_BulletFlyby flyby = Object.FindObjectOfType<ACO_BulletFlyby>(true);
        if (contacts == null || flyby == null) { return; }
        if (flyby.Contacts != contacts) { flyby.Contacts = contacts; MarkChanged(flyby); }
        if (flyby.LaserClips == null || flyby.LaserClips.Length == 0)
        {
            AudioClip[] zaps = LaserZapClips();
            if (zaps.Length > 0) { flyby.LaserClips = zaps; MarkChanged(flyby); }
        }
        foreach (ACO_NPCGun gun in Object.FindObjectsOfType<ACO_NPCGun>(true))
        {
            ACO_BulletFlyby want = IsFriendlyUnit(gun) ? null : flyby;
            if (gun.Flyby != want) { gun.Flyby = want; MarkChanged(gun); }
        }
        // 레일건 탄 스침 소리(6-156): 소형 레일건·육상전함 주포, Lance, LUNA 레일건 런처(RailFlyby)
        foreach (ACO_MiniRailgun gun in Object.FindObjectsOfType<ACO_MiniRailgun>(true))
        {
            if (gun.Flyby != flyby) { gun.Flyby = flyby; MarkChanged(gun); }
        }
        foreach (ACO_Railgun gun in Object.FindObjectsOfType<ACO_Railgun>(true))
        {
            if (gun.Flyby != flyby) { gun.Flyby = flyby; MarkChanged(gun); }
        }
        foreach (ACO_MissileLauncher launcher in Object.FindObjectsOfType<ACO_MissileLauncher>(true))
        {
            ACO_BulletFlyby want = launcher.RailFlyby ? flyby : null;
            if (launcher.Flyby != want) { launcher.Flyby = want; MarkChanged(launcher); }
        }
    }

    public static bool BulletFlybyMissing()
    {
        return Object.FindObjectOfType<ACO_RadarContacts>(true) != null && Object.FindObjectOfType<ACO_BulletFlyby>(true) == null;
    }

    /// 레일건 탄이 스칠 때(ElevenLabs 효과음, 1/4 볼륨으로 만든 파일 — 6-156).
    public static AudioClip RailFlybyClip()
    {
        return AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ACO_Addon/Audio/Generated/ACO_Rail_Flyby.wav");
    }

    /// 레이저가 스칠 때의 전자음(Tools/aco_make_laser_zap.py — ACO_Laser_Zap_1~3).
    private static AudioClip[] LaserZapClips()
    {
        var zaps = new List<AudioClip>();
        for (int i = 1; i <= 3; i++)
        {
            AudioClip c = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/Generated/ACO_Laser_Zap_{i}.wav");
            if (c != null) { zaps.Add(c); }
        }
        return zaps.ToArray();
    }

    /// 레이더 아래에 소리 한 벌을 만든다(2D 소스 3개 + 클립).
    public static void InstallBulletFlyby()
    {
        ACO_RadarContacts contacts = Object.FindObjectOfType<ACO_RadarContacts>(true);
        if (contacts == null) { return; }
        var go = new GameObject("BulletFlyby");
        go.transform.SetParent(contacts.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "ACO bullet flyby");
        ACO_BulletFlyby flyby = go.AddUdonSharpComponent<ACO_BulletFlyby>();
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
        var clips = new List<AudioClip>();
        for (int i = 1; i <= 4; i++)
        {
            AudioClip c = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/Generated/ACO_Bullet_Whiz_{i}.wav");
            if (c != null) { clips.Add(c); }
        }
        flyby.Clips = clips.ToArray();
        flyby.LaserClips = LaserZapClips();
        flyby.RailClip = RailFlybyClip();
        ACO_SyncModeFixer.FixHierarchy(go, false);
        MarkChanged(flyby);
        Debug.Log("[ACO] 총탄이 스치는 소리를 만들었습니다: BulletFlyby");
    }

    /// 기총 탄 파티클(플레어·미사일·폭탄 파티클 제외).
    private static List<ParticleSystem> GunParticles(SaccFlightAndVehicles.SaccEntity entity)
    {
        var flareSystems = new List<ParticleSystem>();
        foreach (var f in entity.GetComponentsInChildren<SaccFlightAndVehicles.DFUNC_Flares>(true))
        {
            if (f != null && f.FlareParticles != null) { flareSystems.AddRange(f.FlareParticles); }
        }
        var list = new List<ParticleSystem>();
        foreach (ParticleSystem ps in entity.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (flareSystems.Contains(ps)) { continue; }
            var col = ps.collision;
            if (!col.enabled || !col.sendCollisionMessages) { continue; }
            if (ps.GetComponentInParent<ACO_Missile>(true) != null || ps.GetComponentInParent<ACO_Ordnance>(true) != null) { continue; }
            list.Add(ps);
        }
        return list;
    }

    private static ACO_GunDamageBonus[] CollectGunBonuses(SaccFlightAndVehicles.SaccEntity entity)
    {
        var list = new List<ACO_GunDamageBonus>();
        foreach (ParticleSystem ps in GunParticles(entity))
        {
            ACO_GunDamageBonus b = ps.GetComponent<ACO_GunDamageBonus>();
            if (b != null) { list.Add(b); }
        }
        return list.ToArray();
    }

    /// 기체에 빠진 ACO 부품(표시, 데칼, 기총 강화)이 있는가.
    public static bool VehiclePartsMissing(SaccFlightAndVehicles.SaccEntity entity)
    {
        if (entity.GetComponentInChildren<ACO_VehicleUpgrades>(true) == null) { return false; }
        if (entity.transform.Find("ACO_Marks") == null || entity.transform.Find("ACO_Decals") == null) { return true; }
        foreach (ParticleSystem ps in GunParticles(entity))
        {
            if (ps.GetComponent<ACO_GunDamageBonus>() == null) { return true; }
        }
        return false;
    }

    /// 기체 표시, 데칼, 기총 강화를 넣는다. SetupVehicle이 부른다. 바꿨으면 true.
    public static bool InstallVehicleParts(SaccFlightAndVehicles.SaccEntity entity)
    {
        if (!VehiclePartsMissing(entity)) { return false; }
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        Transform template = studio != null && studio.SampleRoot != null ? studio.SampleRoot.Find("ACO_Marks/Marks") : null;
        ACO_HUDBuilder.EnsureVehicleMarks(entity, template);
        if (studio != null) { ACO_HUDBuilder.EnsurePreviewMarks(studio.SampleRoot, entity); }
        if (entity.transform.Find("ACO_Decals") == null) { InstallDecal(entity, studio); }
        foreach (ParticleSystem ps in GunParticles(entity))
        {
            if (ps.GetComponent<ACO_GunDamageBonus>() != null) { continue; }
            UdonSharpEditor.UdonSharpUndo.AddComponent<ACO_GunDamageBonus>(ps.gameObject);
            Debug.Log($"[ACO] 기총 피해 강화: '{entity.name}/{ps.name}'에 ACO_GunDamageBonus를 붙였습니다.", ps);
        }
        return true;
    }

    /// 기체에 데칼(기준점 ACO_Decals + 프로젝터 2개)을 단다. 머티리얼 번호는 씬의 데칼 수로 정한다.
    private static void InstallDecal(SaccFlightAndVehicles.SaccEntity entity, ACO_DecalStudio studio)
    {
        int index = Object.FindObjectsOfType<ACO_VehicleDecal>(true).Length;
        var go = new GameObject("ACO_Decals");
        Undo.RegisterCreatedObjectUndo(go, "ACO decals");
        go.transform.SetParent(entity.transform, false);
        ACO_VehicleDecal vd = go.AddUdonSharpComponent<ACO_VehicleDecal>();
        var pr = new Transform[2];
        var mats = new Material[2];
        for (int i = 0; i < 2; i++)
        {
            Material m;
            // 기체 기준점 자식(ACO_Decals는 원점) — 저장된 자리가 곧 기준점 기준 상대값
            pr[i] = ACO_HUDBuilder.CreateDecalProjector(go.transform, "Decal_" + (i + 1), "ACO_Decal_V" + index + "_" + i, out m);
            mats[i] = m;
        }
        vd.Projectors = pr;
        vd.Materials = mats;
        if (studio != null) { vd.DecalKey = studio.DecalKey; }
        ACO_RoundelBuilder.Ensure(vd);   // 라운델 판(6-191)
        ACO_HUDBuilder.LinkMarksDecal(entity.transform);   // 자유 비행 빈 기체 표시(6-194)
        MarkChanged(vd);
        Debug.Log($"[ACO] 데칼: '{entity.name}'에 데칼(프로젝터 2개)을 달았습니다.", go);
    }

    /// 다른 기체와 데칼 머티리얼을 같이 쓰는 기체 데칼. 머티리얼에 이미지·크기·켜짐을 넣으므로, 같이 쓰면 한 기체의 데칼이 다른 기체에도 보인다
    /// (기체 프리팹 안에 데칼을 넣으면 그 프리팹의 씬 인스턴스가 모두 같은 머티리얼을 가리킨다). 첫 기체는 그대로, 뒤 기체들을 돌려준다.
    public static List<ACO_VehicleDecal> DecalsSharingMaterials()
    {
        var seen = new HashSet<Material>();
        var shared = new List<ACO_VehicleDecal>();
        foreach (ACO_VehicleDecal vd in SceneDecals())
        {
            bool dup = false;
            for (int i = 0; i < 2; i++)
            {
                Material m = DecalMaterialOf(vd, i);
                if (m == null) { continue; }
                if (!seen.Add(m)) { dup = true; }
            }
            if (dup) { shared.Add(vd); }
        }
        return shared;
    }

    /// Setup Check: 머티리얼을 같이 쓰는 기체 데칼에 기체마다 따로 머티리얼(Generated/Decals/ACO_Decal_V<n>_<i>)을 준다 — 씬 인스턴스 오버라이드로 넣는다.
    public static void SplitDecalMaterials()
    {
        var used = new HashSet<Material>();
        foreach (ACO_VehicleDecal vd in SceneDecals())
        {
            for (int i = 0; i < 2; i++)
            {
                Material m = DecalMaterialOf(vd, i);
                if (m != null && !used.Contains(m)) { used.Add(m); continue; }
                // 같이 쓰는(또는 없는) 칸: 아무도 안 쓰는 번호의 머티리얼을 찾거나 만든다
                Material fresh = null;
                for (int n = 0; n < 256 && fresh == null; n++)
                {
                    Material cand = ACO_HUDBuilder.LoadOrCreateDecalMaterial("ACO_Decal_V" + n + "_" + i);
                    if (cand != null && !used.Contains(cand)) { fresh = cand; }
                }
                if (fresh == null) { continue; }
                used.Add(fresh);
                Projector p = vd.Projectors != null && i < vd.Projectors.Length && vd.Projectors[i] != null ? vd.Projectors[i].GetComponent<Projector>() : null;
                if (p != null)
                {
                    Undo.RecordObject(p, "ACO decal material");
                    p.material = fresh;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(p);
                }
                Undo.RecordObject(vd, "ACO decal material");
                if (vd.Materials == null || vd.Materials.Length < 2)
                {
                    var arr = new Material[2];
                    if (vd.Materials != null) { for (int k = 0; k < vd.Materials.Length && k < 2; k++) { arr[k] = vd.Materials[k]; } }
                    vd.Materials = arr;
                }
                vd.Materials[i] = fresh;
                MarkChanged(vd);
                Debug.Log($"[ACO] 데칼: '{vd.transform.parent?.name}'의 데칼 {i + 1}에 따로 머티리얼 {fresh.name}을 줬습니다.", vd);
            }
        }
    }

    private static List<ACO_VehicleDecal> SceneDecals()
    {
        var list = new List<ACO_VehicleDecal>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            list.AddRange(root.GetComponentsInChildren<ACO_VehicleDecal>(true));
        }
        return list;
    }

    /// 데칼 칸 i가 실제로 쓰는 머티리얼: 스크립트가 값을 넣는 Materials[i]. 프로젝터가 다른 걸 가리키면 그것도 같이 본다(둘이 다르면 같이 쓰는 것으로 친다).
    private static Material DecalMaterialOf(ACO_VehicleDecal vd, int i)
    {
        Material m = vd.Materials != null && i < vd.Materials.Length ? vd.Materials[i] : null;
        Projector p = vd.Projectors != null && i < vd.Projectors.Length && vd.Projectors[i] != null ? vd.Projectors[i].GetComponent<Projector>() : null;
        if (p != null && p.material != m) { return null; }   // 어긋남 — 아래 고치기에서 새로 준다
        return m;
    }

    /// 샘플 기체에서 데칼 건이 겨눌 콜라이더가 없는 메시 수.
    public static int DecalSampleMeshesMissing()
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        if (studio == null || studio.SampleRoot == null) { return 0; }
        int n = 0;
        foreach (MeshFilter mf in studio.SampleRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (DecalSampleNeedsCollider(mf, studio.SampleRoot)) { n++; }
        }
        return n;
    }

    /// 데칼 건이 맞힐 샘플 기체 메시인데 콜라이더가 없는가. 무장 겉모양(WeaponVis — 장착에 따라 켜지는 포드·미사일, 기본은 꺼짐)은 뺀다:
    /// 데칼은 기체에 찍는 것이고, 무장 설치(MMP·RGP 등)가 포드를 다시 만들면 콜라이더가 빠져 경고가 계속 다시 떴다.
    private static bool DecalSampleNeedsCollider(MeshFilter mf, Transform root)
    {
        if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) { return false; }
        for (Transform t = mf.transform; t != null && t != root; t = t.parent)
        {
            if (t.name == "WeaponVis") { return false; }
        }
        return true;
    }

    public static void AddDecalSampleColliders()
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        if (studio == null || studio.SampleRoot == null) { return; }
        foreach (MeshFilter mf in studio.SampleRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!DecalSampleNeedsCollider(mf, studio.SampleRoot)) { continue; }
            MeshCollider mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
        }
    }
}
