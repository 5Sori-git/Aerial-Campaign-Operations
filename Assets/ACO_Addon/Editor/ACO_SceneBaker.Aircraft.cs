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
    private static void BakeDecals()
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
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
        foreach (ACO_NPCGun gun in Object.FindObjectsOfType<ACO_NPCGun>(true))
        {
            ACO_BulletFlyby want = IsFriendlyUnit(gun) ? null : flyby;
            if (gun.Flyby != want) { gun.Flyby = want; MarkChanged(gun); }
        }
    }

    public static bool BulletFlybyMissing()
    {
        return Object.FindObjectOfType<ACO_RadarContacts>(true) != null && Object.FindObjectOfType<ACO_BulletFlyby>(true) == null;
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
        MarkChanged(vd);
        Debug.Log($"[ACO] 데칼: '{entity.name}'에 데칼(프로젝터 2개)을 달았습니다.", go);
    }

    /// 샘플 기체에서 데칼 건이 겨눌 콜라이더가 없는 메시 수.
    public static int DecalSampleMeshesMissing()
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        if (studio == null || studio.SampleRoot == null) { return 0; }
        int n = 0;
        foreach (MeshFilter mf in studio.SampleRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null) { n++; }
        }
        return n;
    }

    public static void AddDecalSampleColliders()
    {
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        if (studio == null || studio.SampleRoot == null) { return; }
        foreach (MeshFilter mf in studio.SampleRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) { continue; }
            MeshCollider mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
        }
    }
}
