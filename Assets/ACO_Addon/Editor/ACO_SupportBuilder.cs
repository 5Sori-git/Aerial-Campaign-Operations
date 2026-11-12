using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using UdonSharp;
using UdonSharpEditor;
using ACO;

/// 지원 호출(6-142) 빌더 — 구조는 Setup Check [고치기]가 부른다(값은 각 컴포넌트 기본값, 연결은 Bake).
///  - InstallSupportSlots: 플레이어 기체 프리팹에 SUPPORT 다이얼(ACO_DFUNC_Support)을 오른쪽 다이얼 4번째 칸(Dial_Functions_R[3], 키 4)에 넣고,
///    타게팅 포드(AN ASQ-228) 렌즈 자리 TGP_Laser, 프로브 끝(Prove.1 아래) ProbeTip, 조준 레이저·폭발 범위 원(LineRenderer)을 만든다.
///  - EnsureSupportManager: 씬에 지원 관리자(ACO_SupportManager) + 풀(폭격기·벙커버스터·포드 레이저·대폭발 4세트, 급유 드론 4대).
///  - 급유 드론 프리팹(ACO_TankerDrone.prefab): 자리 표시 큐브 몸체(Model — 모델로 바꿔도 된다) + EEI 버디 포드(호스 본). 없을 때만 만든다(손으로 바꾼 모델을 지우지 않게).
///  - 가디언 드론 프리팹(ACO_GuardianDrone.prefab — 요격 대공포 + 자리 표시 몸체 Model), 지원 폭격기 프리팹(ACO_SupportBomber.prefab — NPC_Bomber의 Model 겉모양만).
///    둘 다 없을 때만 만든다. 풀은 이 프리팹의 인스턴스 — 모델을 바꾸려면 프리팹 하나만 고치면 네 대 모두 바뀐다.
public static partial class ACO_PrefabBuilder
{
    public const string SupportManagerName = "ACO_SupportManager";
    public const string TankerDronePrefab = "ACO_TankerDrone";
    public const string GuardianDronePrefab = "ACO_GuardianDrone";
    public const string SupportBomberPrefab = "ACO_SupportBomber";
    private const int SupportDialIndex = 3;
    private const KeyCode SupportKey = KeyCode.Alpha4;
    private const string BuddyPodPrefabPath = "Assets/EEIproducts/AirCraftSupportSet/BuddyPod/Prefab/BuddyPod_Extend.prefab";
    public const string BunkerBusterPrefabPath = "Assets/Pit-bool/FA-18/prefab/weapon/GBU-24.prefab";
    private const string TgpName = "AN ASQ-228";
    private const string ProbeBoneName = "Prove.1";
    private const int StrikePool = 4;
    private const int DronePool = 4;
    private const int GuardianPool = 4;
    private static readonly Color SupportLaserColor = new Color(0.35f, 1f, 0.45f);

    // ───────────── 기체: SUPPORT 다이얼 ─────────────

    /// 씬의 플레이어 기체(강화 확장이 있는 SAV)마다 SUPPORT 다이얼을 넣는다 — 프리팹 인스턴스면 프리팹 에셋에(기체 수정은 프리팹에).
    public static void InstallSupportSlots()
    {
        var prefabs = new List<string>();
        var sceneOnly = new List<SaccEntity>();
        foreach (ACO_VehicleUpgrades up in Object.FindObjectsOfType<ACO_VehicleUpgrades>(true))
        {
            SaccEntity e = up.GetComponentInParent<SaccEntity>(true);
            if (e == null) { continue; }
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(e.gameObject);
            if (string.IsNullOrEmpty(path)) { if (!sceneOnly.Contains(e)) { sceneOnly.Add(e); } }
            else if (!prefabs.Contains(path)) { prefabs.Add(path); }
        }
        foreach (string path in prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (SaccEntity e in root.GetComponentsInChildren<SaccEntity>(true))
                {
                    if (e.GetComponentInChildren<ACO_VehicleUpgrades>(true) == null) { continue; }
                    if (EnsureSupportSlot(e, false)) { changed = true; }
                }
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[ACO] SUPPORT 다이얼을 기체 프리팹에 넣었습니다: {path}");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        bool sceneChanged = false;
        foreach (SaccEntity e in sceneOnly) { if (EnsureSupportSlot(e, true)) { sceneChanged = true; } }
        if (sceneChanged) { EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); }
    }

    /// SUPPORT 다이얼이 없거나 자리(레이저·프로브·조준 선)가 비었으면 채운다. 바꿨으면 true.
    public static bool EnsureSupportSlot(SaccEntity e, bool inScene)
    {
        bool changed = false;
        ACO_DFUNC_Support sup = e.GetComponentInChildren<ACO_DFUNC_Support>(true);
        if (sup == null)
        {
            UdonSharpBehaviour[] r = e.Dial_Functions_R ?? new UdonSharpBehaviour[0];
            if (r.Length > SupportDialIndex && r[SupportDialIndex] != null)
            {
                Debug.LogWarning($"[ACO] '{e.name}': 오른쪽 다이얼 Element {SupportDialIndex}에 '{r[SupportDialIndex].name}'이 있어 SUPPORT 다이얼을 넣지 않았습니다 — 그 기능을 다른 칸으로 옮기고 다시 [고치기].", e);
                return false;
            }
            ACO_DFUNC_Special special = e.GetComponentInChildren<ACO_DFUNC_Special>(true);
            Transform parent = special != null ? special.transform.parent : e.transform;
            var go = new GameObject("ACO_Support");
            go.transform.SetParent(parent, false);
            if (special != null) { go.transform.SetSiblingIndex(special.transform.GetSiblingIndex() + 1); }
            if (inScene)
            {
                Undo.RegisterCreatedObjectUndo(go, "ACO support dial");
                sup = UdonSharpUndo.AddComponent<ACO_DFUNC_Support>(go);
                Undo.RecordObject(e, "ACO support dial");
            }
            else { sup = go.AddUdonSharpComponent<ACO_DFUNC_Support>(); }
            if (r.Length <= SupportDialIndex) { System.Array.Resize(ref r, SupportDialIndex + 1); }
            r[SupportDialIndex] = sup;
            e.Dial_Functions_R = r;
            if (inScene) { PrefabUtility.RecordPrefabInstancePropertyModifications(e); }
            EditorUtility.SetDirty(e);
            ACO_PlayerMissileSetup.BindDialKey(e, sup, SupportKey);
            Debug.Log($"[ACO] '{e.name}': 오른쪽 다이얼 Element {SupportDialIndex}에 SUPPORT 다이얼을 넣었습니다(키보드 {SupportKey}).", sup);
            changed = true;
        }
        if (inScene) { Undo.RecordObject(sup, "ACO support dial"); }

        if (sup.AirVehicle == null) { sup.AirVehicle = e.GetComponentInChildren<SaccAirVehicle>(true); changed = true; }
        if (sup.LaserOrigin == null) { sup.LaserOrigin = MakeTgpPoint(e, inScene); changed = true; }
        if (sup.ProbeTip == null)
        {
            Transform bone = FindDeep(e.transform, ProbeBoneName);
            if (bone != null) { sup.ProbeTip = MakePoint(bone, "ProbeTip", Vector3.zero, inScene); changed = true; }
            else { Debug.LogWarning($"[ACO] '{e.name}': 프로브 본 '{ProbeBoneName}'을 찾지 못했습니다 — SUPPORT 다이얼의 ProbeTip에 프로브 끝을 직접 넣으세요(비면 기수 오른쪽 위로 친다).", sup); }
        }
        if (sup.MfdText == null)
        {
            sup.MfdText = MakeMfdLabel(e, inScene);
            if (sup.MfdText != null) { changed = true; }
        }
        if (sup.AimLaser == null)
        {
            sup.AimLaser = LunaBeam(sup.transform, "AimLaser", SupportLaserColor, SupportLaserColor, 0.3f);
            if (inScene) { Undo.RegisterCreatedObjectUndo(sup.AimLaser.gameObject, "ACO support dial"); }
            changed = true;
        }
        if (sup.AimRing == null)
        {
            LineRenderer ring = LunaBeam(sup.transform, "AimRing", SupportLaserColor, SupportLaserColor, 6f);
            ring.loop = true;
            ring.positionCount = sup.RingSegments;
            ring.alignment = LineAlignment.TransformZ;
            ring.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            sup.AimRing = ring;
            if (inScene) { Undo.RegisterCreatedObjectUndo(ring.gameObject, "ACO support dial"); }
            changed = true;
        }
        if (changed)
        {
            if (inScene) { PrefabUtility.RecordPrefabInstancePropertyModifications(sup); }
            EditorUtility.SetDirty(sup);
        }
        return changed;
    }

    /// MFD 다이얼 칸 글자: 오른쪽 다이얼 표시(StickDisplayR)의 기총 글자(MFD_Gun)를 복제해 왼쪽 칸(오른쪽 칸 MFD_DFUNCMissile의 좌우 반대)에 둔다.
    /// 다이얼 칸 순서가 위(0) → 오른쪽(1) → 아래(2) → 왼쪽(3)이라 Element 3 = 왼쪽.
    private static TMPro.TextMeshProUGUI MakeMfdLabel(SaccEntity e, bool inScene)
    {
        Transform display = FindDeep(e.transform, "StickDisplayR");
        if (display == null)
        {
            Debug.LogWarning($"[ACO] '{e.name}': 오른쪽 다이얼 표시(StickDisplayR)를 찾지 못해 MFD 지원 글자를 만들지 않았습니다 — SUPPORT 다이얼의 MfdText에 직접 넣으세요.", e);
            return null;
        }
        Transform existing = display.Find("MFD_Support");
        if (existing != null) { return existing.GetComponent<TMPro.TextMeshProUGUI>(); }
        Transform template = display.Find("MFD_Gun");
        if (template == null || template.GetComponent<TMPro.TextMeshProUGUI>() == null)
        {
            Debug.LogWarning($"[ACO] '{e.name}': MFD 글자 원본(StickDisplayR/MFD_Gun)이 없어 MFD 지원 글자를 만들지 않았습니다.", display);
            return null;
        }
        GameObject go = Object.Instantiate(template.gameObject, display);
        go.name = "MFD_Support";
        go.SetActive(true);
        if (inScene) { Undo.RegisterCreatedObjectUndo(go, "ACO support MFD"); }
        var rt = (RectTransform)go.transform;
        Transform right = display.Find("MFD_DFUNCMissile");
        float x = right != null ? ((RectTransform)right).anchoredPosition.x : 0.123f;
        rt.anchoredPosition = new Vector2(-Mathf.Abs(x), 0f);
        TMPro.TextMeshProUGUI text = go.GetComponent<TMPro.TextMeshProUGUI>();
        text.text = "SUP - N/A";
        EditorUtility.SetDirty(text);
        return text;
    }

    /// 타게팅 포드 렌즈 자리: 켜진 AN ASQ-228의 메시 앞쪽 아래. 포드가 없으면 기체 중심 아래 1.5m.
    private static Transform MakeTgpPoint(SaccEntity e, bool inScene)
    {
        Transform pod = null;
        foreach (Transform t in e.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != TgpName) { continue; }
            if (pod == null || (!pod.gameObject.activeInHierarchy && t.gameObject.activeInHierarchy)) { pod = t; }
        }
        if (pod == null)
        {
            Debug.LogWarning($"[ACO] '{e.name}': 타게팅 포드 '{TgpName}'를 찾지 못해 레이저 자리를 기체 아래에 만들었습니다 — TGP_Laser를 옮기세요.", e);
            return MakePoint(e.transform, "TGP_Laser", new Vector3(0f, -1.5f, 0f), inScene);
        }
        Renderer[] rs = pod.GetComponentsInChildren<Renderer>(true);
        Vector3 world = pod.position;
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) { b.Encapsulate(rs[i].bounds); }
            Vector3 fwd = e.transform.forward;
            world = b.center + fwd * Vector3.Dot(b.extents, new Vector3(Mathf.Abs(fwd.x), Mathf.Abs(fwd.y), Mathf.Abs(fwd.z))) * 0.8f - e.transform.up * b.extents.y * 0.5f;
        }
        return MakePoint(pod, "TGP_Laser", pod.InverseTransformPoint(world), inScene);
    }

    private static Transform MakePoint(Transform parent, string name, Vector3 local, bool inScene)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            if (inScene) { Undo.RegisterCreatedObjectUndo(go, "ACO support point"); }
            t = go.transform;
        }
        return t;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) { if (t.name == name) { return t; } }
        return null;
    }

    // ───────────── 씬: 지원 관리자 ─────────────

    /// 씬에 지원 관리자와 풀을 만든다(있으면 풀만 채운다). 드론 프리팹이 없으면 먼저 만든다.
    public static void EnsureSupportManager()
    {
        ACO_SupportManager m = Object.FindObjectOfType<ACO_SupportManager>(true);
        GameObject root;
        if (m == null)
        {
            root = new GameObject(SupportManagerName);
            Undo.RegisterCreatedObjectUndo(root, "ACO support manager");
            m = UdonSharpUndo.AddComponent<ACO_SupportManager>(root);
        }
        root = m.gameObject;
        Undo.RecordObject(m, "ACO support manager");

        if (m.Bombers == null || m.Bombers.Length == 0 || HasNull(m.Bombers) || HasNull(m.Bombs) || HasNull(m.Lasers) || HasNull(m.Blasts))
        {
            Transform old = root.transform.Find("Strikes");
            if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); }
            var strikes = new GameObject("Strikes").transform;
            strikes.SetParent(root.transform, false);
            if (!Exists(SupportBomberPrefab)) { BuildSupportBomberPrefab(); }
            GameObject bomberPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(SupportBomberPrefab));
            GameObject bombAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BunkerBusterPrefabPath);
            if (bombAsset == null) { Debug.LogWarning($"[ACO] 지원 관리자: 벙커버스터 모델({BunkerBusterPrefabPath})을 찾지 못해 상자로 둡니다."); }
            m.Bombers = new Transform[StrikePool];
            m.Bombs = new Transform[StrikePool];
            m.Lasers = new LineRenderer[StrikePool];
            m.Blasts = new ACO_FortressBlast[StrikePool];
            for (int i = 0; i < StrikePool; i++)
            {
                var slot = new GameObject($"Strike_{i}").transform;
                slot.SetParent(strikes, false);
                m.Bombers[i] = PoolInstance(bomberPrefab, slot, "Bomber", false);
                m.Bombs[i] = VisualCopy(bombAsset, slot, "BunkerBuster", new Vector3(0.6f, 0.6f, 5f));
                FaceBombForward(m.Bombs[i], bombAsset);
                m.Lasers[i] = LunaBeam(slot, "Laser", SupportLaserColor, SupportLaserColor, 1.5f);
                m.Blasts[i] = CreateFortressBlast(slot);
                m.Bombers[i].gameObject.SetActive(false);
                m.Bombs[i].gameObject.SetActive(false);
            }
            Undo.RegisterCreatedObjectUndo(strikes.gameObject, "ACO support manager");
        }

        if (m.Drones == null || m.Drones.Length == 0 || HasNull(m.Drones))
        {
            Transform old = root.transform.Find("Drones");
            if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); }
            if (!Exists(TankerDronePrefab)) { BuildTankerDronePrefab(); }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(TankerDronePrefab));
            var drones = new GameObject("Drones").transform;
            drones.SetParent(root.transform, false);
            m.Drones = new ACO_TankerDrone[DronePool];
            for (int i = 0; i < DronePool; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, drones);
                go.name = $"Drone_{i}";
                m.Drones[i] = go.GetComponent<ACO_TankerDrone>();
                go.SetActive(false);
            }
            Undo.RegisterCreatedObjectUndo(drones.gameObject, "ACO support manager");
        }
        if (m.Guardians == null || m.Guardians.Length == 0 || HasNull(m.Guardians))
        {
            Transform old = root.transform.Find("Guardians");
            if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); }
            if (!Exists(GuardianDronePrefab)) { BuildGuardianDronePrefab(); }
            GameObject guardianPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(GuardianDronePrefab));
            var guardians = new GameObject("Guardians").transform;
            guardians.SetParent(root.transform, false);
            m.Guardians = new ACO_AAGunSite[GuardianPool];
            for (int i = 0; i < GuardianPool; i++)
            {
                Transform g = PoolInstance(guardianPrefab, guardians, $"Guardian_{i}", false);
                m.Guardians[i] = g.GetComponent<ACO_AAGunSite>();
            }
            Undo.RegisterCreatedObjectUndo(guardians.gameObject, "ACO support manager");
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(m);
        EditorUtility.SetDirty(m);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[ACO] 지원 관리자(벙커버스터·급유 드론·가디언 드론 풀)를 만들었습니다. Bake 하세요.", m);
    }

    private static bool HasNull(Object[] list)
    {
        if (list == null || list.Length == 0) { return true; }
        foreach (Object o in list) { if (o == null) { return true; } }
        return false;
    }

    /// 보이기만 하는 사본: 원본(프리팹 에셋의 일부)의 메시만 남기고 콜라이더·스크립트·소리를 뗀다. 원본이 없으면 크기 size의 상자.
    /// 돌려주는 것은 빈 부모(관리자가 위치·방향을 매 프레임 정한다 — 앞 = 진행 방향). 모델은 그 아래 Model이라 축이 다르면 Model만 돌리면 된다.
    private static Transform VisualCopy(GameObject source, Transform parent, string name, Vector3 size)
    {
        var holder = new GameObject(name).transform;
        holder.SetParent(parent, false);
        parent = holder;
        GameObject go;
        if (source != null)
        {
            go = Object.Instantiate(source, parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            // 파티클(연기·불꽃)은 렌더러가 딸려 있어 컴포넌트만 뗄 수 없다 — 오브젝트째 뺀다
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps != null && ps.gameObject != go) { Object.DestroyImmediate(ps.gameObject); }
            }
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer || c is LODGroup) { continue; }
                if (c is UdonSharpBehaviour) { Object.DestroyImmediate(c); }
            }
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer || c is LODGroup) { continue; }
                Object.DestroyImmediate(c);
            }
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
        }
        go.name = "Model";
        return holder;
    }

    /// 폭격 호출 폭탄의 모델 회전: 원본 메시는 기수가 -Y라 프리팹 루트 회전(-90도 X)이 있어야 기수가 +Z(관리자가 속도 방향으로 돌리는 축)를 본다.
    /// VisualCopy가 회전을 0으로 두므로 그 회전을 다시 넣는다. 바꿨으면 true.
    private static bool FaceBombForward(Transform holder, GameObject bombAsset)
    {
        if (holder == null || bombAsset == null) { return false; }
        Transform model = holder.Find("Model");
        if (model == null || Quaternion.Angle(model.localRotation, bombAsset.transform.localRotation) < 0.01f) { return false; }
        Undo.RecordObject(model, "ACO strike bomb rotation");
        model.localRotation = bombAsset.transform.localRotation;
        PrefabUtility.RecordPrefabInstancePropertyModifications(model);
        return true;
    }

    /// Setup Check strike-bomb-forward-1: 씬의 폭격 호출 폭탄(지원 관리자 Bombs)이 옆으로 누운 채 날던 것 — 모델 회전을 원본 프리팹대로(기수 = 진행 방향).
    public static void ApplyStrikeBombForward()
    {
        ACO_SupportManager m = Object.FindObjectOfType<ACO_SupportManager>(true);
        GameObject bombAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BunkerBusterPrefabPath);
        if (m == null || m.Bombs == null || bombAsset == null) { return; }
        int n = 0;
        foreach (Transform b in m.Bombs) { if (FaceBombForward(b, bombAsset)) { n++; } }
        if (n > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[ACO] 폭격 호출 폭탄 {n}개: 기수가 진행 방향을 보게 모델 회전을 고쳤습니다. 씬을 저장하세요.");
        }
    }

    // ───────────── 가디언 드론 ─────────────

    /// 가디언 드론 한 대(6-143): LUNA 요격 UAV와 같은 대공포(ACO_AAGunSite) 바탕 — 비행기는 노리지 않고(TargetLayers 0) 아군 항공기(25)를 노린 미사일을 요격한다.
    /// 방어 반경 60m(옆에 붙은 호출한 기체만), 사거리 1200m, 반응 0.4초 + 격추 0.8초, 쏜 뒤 2초 쉼. 무전 없음(SilentIntercept).
    /// 무적·락온 불가: 판정 상자·락온 마커를 뺀다. 레이저는 초록(아군 지원 색). 몸체는 Model 아래 자리 표시 상자(사용자가 바꾼다).
    private static ACO_AAGunSite CreateGuardianDrone(Transform parent, int index, Material bodyMat, Material glowMat)
    {
        var spec = new GunSpec
        {
            BodySize = new Vector3(2.4f, 0.6f, 2.4f), BarrelLength = 0.2f, BarrelThickness = 0.1f,
            Health = 100f, Armor = 1f, Score = 0, Label = "GD",
            DetectRange = 0f, LoseTargetRange = 0f, YawRate = 160f, PitchRate = 160f, TrackingLag = 0f, AimError = 0f,
            MaxRange = 1200f, MuzzleVelocity = 2200f, FireStartAngle = 6f, EffectiveAngle = 2f, DwellTime = 1f,
            BurstDuration = 1f, BurstCooldown = 1f, DamagePerTick = 0f, MinDamageInterval = 0.2f, MaxDamageInterval = 0.5f,
            Spread = 0f, TracerRate = 0f,
        };
        GameObject go = CreateAAGunObject($"Guardian_{index}", spec);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        ACO_AAGunSite aa = go.GetComponent<ACO_AAGunSite>();
        Transform barrel = aa.TurretPitch.Find("Barrel");
        if (barrel != null) { Object.DestroyImmediate(barrel.gameObject); }
        aa.TurretYaw.localPosition = new Vector3(0f, -0.25f, 0.4f);
        aa.TurretPitch.localRotation = Quaternion.identity;
        aa.MountRelativeAim = true;
        aa.YawArc = 360f;
        aa.MinPitch = -85f;
        aa.MaxPitch = 85f;
        aa.MinTargetAltitude = -100000f;
        aa.ReturnToRestDelay = 0.8f;
        ACO_NPCGun gun = aa.Gun;
        if (gun != null)
        {
            gun.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            AttachLaserModule(gun, new Color(0.9f, 1f, 0.9f), SupportLaserColor, 0.6f, 1500f, 0, false);
            gun.DamagePerTick = 0f;
            AudioSource hum = CreateRailgunSound(gun.transform, "LaserSound", "ACO_Railgun_Charge_Loop", 30f, 1200f, true);
            hum.pitch = 2.4f;
            hum.volume = 0.45f;
            gun.FireSound = hum;
        }
        // 무적·락온 불가: 판정 상자·락온 마커를 뺀다. 표적이 아니므로 SaccEntity·SaccTarget·표적 Animator도 뺀다(요격은 ACO_AAGunSite만으로 된다)
        StripGuardianEntity(go);
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(c); }
        Transform marker = go.transform.Find("AAMTarget");
        if (marker != null) { Object.DestroyImmediate(marker.gameObject); }
        Transform oldModel = go.transform.Find("Model");
        if (oldModel != null) { Object.DestroyImmediate(oldModel.gameObject); }
        var model = new GameObject("Model");
        model.layer = go.layer;
        model.transform.SetParent(go.transform, false);
        LunaBox(model.transform, "Body", Vector3.zero, new Vector3(1.2f, 0.35f, 2.2f), bodyMat);
        LunaBox(model.transform, "Wing", new Vector3(0f, 0.05f, -0.2f), new Vector3(3.6f, 0.12f, 0.8f), bodyMat);
        LunaBox(model.transform, "Lens", new Vector3(0f, -0.25f, 0.4f), new Vector3(0.4f, 0.25f, 0.4f), glowMat);
        aa.HideOnDeath = new GameObject[] { model };
        aa.DisableCollidersOnDeath = new Collider[0];

        aa.TargetLayers = 0;
        aa.RequireLineOfSight = false;
        aa.SleepMargin = 0f;
        aa.MarkerLabel = "GD";
        aa.ScoreValue = 0;
        aa.InterceptProtectLayers = 1 << 25;   // 아군 항공기를 노린 미사일
        aa.InterceptRange = 1200f;
        aa.DefenseRadius = 60f;
        aa.InterceptReactionTime = 0.4f;
        aa.InterceptKillTime = 0.8f;
        aa.InterceptCooldown = 2f;
        aa.OtherDefenseFactor = 1f;
        aa.SilentIntercept = true;
        aa.InterceptTuneVersion = ACO_SceneBaker.CiwsTuneVersion;
        aa.InterceptDefaultsApplied = true;
        go.SetActive(false);   // 부를 때 관리자가 켠다
        return aa;
    }

    // ───────────── 가디언 드론·지원 폭격기 프리팹 ─────────────

    /// 가디언 드론 프리팹(없을 때만 — 몸체 모델을 바꿨다면 지우지 않는다). 요격 대공포 + Model 아래 자리 표시 상자 셋.
    public static void BuildGuardianDronePrefab()
    {
        EnsurePrefabFolder();
        Material body = LunaMaterial("ACO_GuardianDrone_Placeholder", new Color(0.5f, 0.56f, 0.6f), 0f);
        Material glow = LunaMaterial("ACO_GuardianDrone_Lens", SupportLaserColor, 2.5f);
        ACO_AAGunSite aa = CreateGuardianDrone(null, 0, body, glow);
        GameObject root = aa.gameObject;
        root.name = GuardianDronePrefab;
        root.SetActive(true);   // 프리팹은 켜 둔다 — 풀의 인스턴스를 끈다(부를 때 관리자가 켠다)
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PathOf(GuardianDronePrefab));
        DestroyAfterSetup(root);
        ACO_EngineSounds.EnsureOnPrefab(prefab);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 가디언 드론 프리팹을 만들었습니다: {PathOf(GuardianDronePrefab)} — Model 아래 상자를 모델로 바꾸세요(레이저 포탑 TurretYaw 자리도 함께).", prefab);
    }

    /// 가디언 드론에서 표적용 부품을 뺀다: SaccEntity·SaccTarget(U# + 뒤의 UdonBehaviour)·표적 Animator, 포대·기총의 참조. 뺐으면 true.
    /// 가디언은 무적·락온 불가라 체력·락온 목록이 필요 없다 — 요격(ACO_AAGunSite.CanIntercept/InterceptTick)과 레이저 기총(InterceptFire)은 이것 없이 돈다.
    private static bool StripGuardianEntity(GameObject root)
    {
        bool changed = false;
        foreach (ACO_AAGunSite aa in root.GetComponentsInChildren<ACO_AAGunSite>(true))
        {
            if (aa.EntityControl != null || aa.TargetHealth != null) { aa.EntityControl = null; aa.TargetHealth = null; changed = true; }
        }
        foreach (ACO_NPCGun g in root.GetComponentsInChildren<ACO_NPCGun>(true))
        {
            if (g.EntityControl != null) { g.EntityControl = null; changed = true; }
        }
        foreach (SaccEntity e in root.GetComponentsInChildren<SaccEntity>(true)) { RemoveUdonSharp(e); changed = true; }
        foreach (SaccTarget t in root.GetComponentsInChildren<SaccTarget>(true)) { RemoveUdonSharp(t); changed = true; }
        Animator an = root.GetComponent<Animator>();
        if (an != null) { Object.DestroyImmediate(an); changed = true; }
        return changed;
    }

    /// U# 컴포넌트와 그 뒤의 UdonBehaviour를 함께 지운다.
    private static void RemoveUdonSharp(UdonSharpBehaviour c)
    {
        VRC.Udon.UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
        Object.DestroyImmediate(c);
        if (ub != null) { Object.DestroyImmediate(ub); }
    }

    /// 가디언 드론 프리팹에 표적용 부품(SaccEntity·SaccTarget)이 남아 있는가(Setup Check).
    public static bool GuardianPrefabHasEntity()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(GuardianDronePrefab));
        return asset != null && (asset.GetComponentInChildren<SaccEntity>(true) != null || asset.GetComponentInChildren<SaccTarget>(true) != null);
    }

    /// Setup Check: 이미 있는 가디언 드론 프리팹에서 표적용 부품을 뺀다(몸체 모델 등 손으로 고친 것은 그대로).
    public static void StripGuardianPrefab()
    {
        string path = PathOf(GuardianDronePrefab);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (StripGuardianEntity(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] 가디언 드론 프리팹에서 SaccEntity·SaccTarget·표적 Animator를 뺐습니다: {path} — Bake 하세요.");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 지원 폭격기 프리팹(없을 때만): NPC_Bomber의 Model 겉모양만 복사(스크립트·충돌·파티클 없음). 없으면 상자.
    public static void BuildSupportBomberPrefab()
    {
        EnsurePrefabFolder();
        GameObject bomberAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("NPC_Bomber"));
        Transform bomberModel = bomberAsset != null ? bomberAsset.transform.Find("Model") : null;
        if (bomberModel == null) { Debug.LogWarning("[ACO] 지원 폭격기: NPC_Bomber 프리팹의 Model을 찾지 못해 상자로 만듭니다."); }
        Transform root = VisualCopy(bomberModel != null ? bomberModel.gameObject : null, null, SupportBomberPrefab, new Vector3(30f, 4f, 20f));
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PathOf(SupportBomberPrefab));
        DestroyAfterSetup(root.gameObject);
        ACO_EngineSounds.EnsureOnPrefab(prefab);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 지원 폭격기 프리팹을 만들었습니다: {PathOf(SupportBomberPrefab)} — Model을 모델로 바꿔도 된다(진행 방향 +Z).", prefab);
    }

    /// 풀 한 칸: 프리팹 인스턴스(꺼 둔다). 프리팹이 없으면 빈 오브젝트.
    private static Transform PoolInstance(GameObject prefab, Transform parent, string name, bool active)
    {
        GameObject go = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent) : new GameObject();
        if (prefab == null) { go.transform.SetParent(parent, false); }
        go.name = name;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.SetActive(active);
        return go.transform;
    }

    /// 이 오브젝트가 그 프리팹의 인스턴스 루트인가.
    private static bool IsPoolInstance(Object o, string prefabName)
    {
        Component c = o as Component;
        if (c == null) { return false; }
        GameObject go = c.gameObject;
        if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) { return false; }
        return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == PathOf(prefabName);
    }

    /// 지원 관리자의 폭격기·가디언 드론 풀이 프리팹 인스턴스가 아닌 칸 수(Setup Check).
    public static int SupportPoolsNotPrefab(ACO_SupportManager m)
    {
        int n = 0;
        if (m == null) { return 0; }
        if (m.Bombers != null) { foreach (Transform t in m.Bombers) { if (t != null && !IsPoolInstance(t, SupportBomberPrefab)) { n++; } } }
        if (m.Guardians != null) { foreach (ACO_AAGunSite a in m.Guardians) { if (a != null && !IsPoolInstance(a, GuardianDronePrefab)) { n++; } } }
        return n;
    }

    /// 지원 관리자의 폭격기·가디언 드론 풀을 프리팹 인스턴스로 바꾼다(프리팹이 없으면 먼저 만든다). 같은 자리·이름·순서, 꺼 둔 채로.
    public static void ConvertSupportPoolsToPrefabs()
    {
        ACO_SupportManager m = Object.FindObjectOfType<ACO_SupportManager>(true);
        if (m == null) { return; }
        if (!Exists(SupportBomberPrefab)) { BuildSupportBomberPrefab(); }
        if (!Exists(GuardianDronePrefab)) { BuildGuardianDronePrefab(); }
        GameObject bomberPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(SupportBomberPrefab));
        GameObject guardianPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(GuardianDronePrefab));
        Undo.RecordObject(m, "ACO support prefabs");
        int n = 0;
        if (m.Bombers != null && bomberPrefab != null)
        {
            for (int i = 0; i < m.Bombers.Length; i++)
            {
                Transform old = m.Bombers[i];
                if (old == null || IsPoolInstance(old, SupportBomberPrefab)) { continue; }
                Transform t = ReplaceWithInstance(old.gameObject, bomberPrefab);
                m.Bombers[i] = t;
                n++;
            }
        }
        if (m.Guardians != null && guardianPrefab != null)
        {
            for (int i = 0; i < m.Guardians.Length; i++)
            {
                ACO_AAGunSite old = m.Guardians[i];
                if (old == null || IsPoolInstance(old, GuardianDronePrefab)) { continue; }
                Transform t = ReplaceWithInstance(old.gameObject, guardianPrefab);
                m.Guardians[i] = t.GetComponent<ACO_AAGunSite>();
                n++;
            }
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(m);
        EditorUtility.SetDirty(m);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ACO] 지원 관리자: 폭격기·가디언 드론 {n}칸을 프리팹 인스턴스로 바꿨습니다. Bake 하고 씬을 저장하세요.", m);
    }

    /// 씬 오브젝트를 같은 부모·자리·이름·순서·켜짐 상태의 프리팹 인스턴스로 바꾼다(옛 오브젝트는 Undo로 지운다 — 생성기가 만든 것).
    private static Transform ReplaceWithInstance(GameObject old, GameObject prefab)
    {
        Transform parent = old.transform.parent;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = old.name;
        go.transform.localPosition = old.transform.localPosition;
        go.transform.localRotation = old.transform.localRotation;
        go.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
        go.SetActive(old.activeSelf);
        Undo.RegisterCreatedObjectUndo(go, "ACO support prefabs");
        Undo.DestroyObjectImmediate(old);
        return go.transform;
    }

    // ───────────── 급유 드론 프리팹 ─────────────

    /// 급유 드론 프리팹(없을 때만 — 몸체 모델을 바꿨다면 지우지 않는다). 몸체 = Model 아래 큐브 셋(자리 표시), 아래에 버디 포드.
    public static void BuildTankerDronePrefab()
    {
        var root = new GameObject(TankerDronePrefab);
        ACO_TankerDrone drone = root.AddUdonSharpComponent<ACO_TankerDrone>();
        Material mat = LunaMaterial("ACO_TankerDrone_Placeholder", new Color(0.55f, 0.58f, 0.6f), 0f);
        var model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        PlaceholderCube(model, "Body", new Vector3(0f, 0f, 0f), new Vector3(2.4f, 1f, 9f), mat);
        PlaceholderCube(model, "Wing", new Vector3(0f, 0.15f, 0.5f), new Vector3(12f, 0.25f, 2.4f), mat);
        PlaceholderCube(model, "Tail", new Vector3(0f, 0.9f, -3.8f), new Vector3(0.25f, 1.6f, 1.4f), mat);

        GameObject podAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BuddyPodPrefabPath);
        var bones = new List<Transform>();
        if (podAsset != null)
        {
            var pod = (GameObject)PrefabUtility.InstantiatePrefab(podAsset, root.transform);
            pod.name = "BuddyPod";
            pod.transform.localPosition = new Vector3(0f, -1.1f, 0f);
            Transform b = FindDeep(pod.transform, "Drogue_Base");
            if (b != null) { bones.Add(b); }
            for (int i = 1; i <= 25; i++)
            {
                Transform t = FindDeep(pod.transform, $"Drogue_{i:000}");
                if (t != null) { bones.Add(t); }
            }
            Transform head = FindDeep(pod.transform, "Drogue_Head");
            if (head != null) { bones.Add(head); }
            foreach (SkinnedMeshRenderer smr in pod.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { smr.updateWhenOffscreen = true; }
        }
        else { Debug.LogWarning($"[ACO] 급유 드론: 버디 포드({BuddyPodPrefabPath})를 찾지 못했습니다 — 호스 없이 만듭니다."); }
        drone.Bones = bones.ToArray();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PathOf(TankerDronePrefab));
        DestroyAfterSetup(root);
        ACO_EngineSounds.EnsureOnPrefab(prefab);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 급유 드론 프리팹을 만들었습니다(호스 본 {bones.Count}개): {PathOf(TankerDronePrefab)} — Model 아래 큐브를 모델로 바꾸세요.", prefab);
    }

    private static void PlaceholderCube(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
