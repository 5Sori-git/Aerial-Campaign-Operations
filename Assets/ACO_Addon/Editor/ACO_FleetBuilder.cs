using System.Linq;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 함대전 함선: 아군 기함 NPC_Flagship(순양함 — Ticonderoga, 주포 1·VLS 2·CIWS 2)과 적 항모 NPC_Carrier(Kuznetsov, CIWS 4 · VLS 4).
/// 모델은 Low Poly Warship Collection — 원본이 실제의 약 0.43배라 2.3배로 키운다(순양함 172m, 항모 300m). 콜라이더는 빌더가 상자로 둔다.
/// 호위함·적 순양함·구축함은 기존 프리팹을 그대로 쓴다(NPC_GuardShip, NPC_Warship, NPC_Destroyer).
public static partial class ACO_PrefabBuilder
{
    private const string TiconderogaPath = "Assets/Low Poly Warship Collection 10/Ticonderoga/Prefab/Ticonderoga.prefab";
    private const string KuznetsovPath = "Assets/Low Poly Warship Collection 10/Kuznetsov/Prefab/Kuznetsov.prefab";
    private const float FleetModelScale = 2.3f;

    /// 모델 팩 프리팹을 복사해 붙인다(프리팹 연결 없이 — 콜라이더는 뺀다). 없으면 경고하고 상자 선체로 남는다.
    private static bool AddFleetModel(Transform parent, string path)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null)
        {
            Debug.LogWarning($"[ACO] 함선 모델을 찾지 못했습니다: {path}");
            return false;
        }
        GameObject model = Object.Instantiate(source, parent);
        model.name = source.name;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * FleetModelScale;
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(c); }
        return true;
    }

    /// 아군 함선 VLS: 항공기·함선 겸용(적 항공기·적 지상 레이어). 선체가 맞으므로 자체 히트박스·락온 마커는 없다.
    private static GameObject AddFriendlyVls(Transform parent, string name, GameObject missile, Vector3 localPos, int targetLayers)
    {
        GameObject part = CreateVLSObject(name, missile);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        Object.DestroyImmediate(part.GetComponent<BoxCollider>());
        Transform marker = part.transform.Find("AAMTarget");
        if (marker != null) { Object.DestroyImmediate(marker.gameObject); }
        ACO_SAMSite sam = part.GetComponent<ACO_SAMSite>();
        part.GetComponent<SaccEntity>().AAMTargetsLayer = targetLayers;
        sam.TargetLayers = targetLayers;
        sam.MinTargetAltitude = -100000f;
        sam.DetectRange = 5000f;
        sam.LoseTargetRange = 6000f;
        sam.DisableCollidersOnDeath = new Collider[0];
        return part;
    }

    /// 아군 기함: 이게 격침되면 함대전 실패. 호위함처럼 선체 체력 하나 — 무장은 선체가 부서지면 같이 끝난다. 락온 마커는 아군 지상 레이어(26).
    public static GameObject BuildFlagship()
    {
        EnsurePrefabFolder();
        GameObject vlsMissile = LoadOrBuildVlsMissile();
        GameObject root = CreateEscortShip("NPC_Flagship", new Vector3(20f, 16f, 172f), 3000f, "Flagship", "CG", ACO_EscortUnit.ROLE_FLAGSHIP);
        ACO_EscortUnit unit = root.GetComponent<ACO_EscortUnit>();
        unit.Speed = 14f;
        unit.AttackPriority = 2500f;   // 적기·포대가 먼저 노린다 — 3.5km 거리의 기함보다 약 1km 안의 항공기가 먼저
        unit.PriorityForSites = true;
        Transform model = root.transform.Find("Model");
        if (AddFleetModel(model, TiconderogaPath)) { Object.DestroyImmediate(model.Find("Hull").gameObject); }

        int air = 1 << LayerEnemyAir;
        int both = (1 << LayerEnemyAir) | (1 << LayerEnemyGround);
        GameObject gun = CreateShipTurret(root.transform, "MainGun", new Vector3(0f, 12f, 55f), 0f, both, 4500f, 30f);
        GameObject ciwsFwd = CreateShipTurret(root.transform, "AAGun_Fwd", new Vector3(0f, 22f, 12f), 0f, air, 2500f, 3f);
        GameObject ciwsAft = CreateShipTurret(root.transform, "AAGun_Aft", new Vector3(0f, 16f, -50f), 180f, air, 2500f, 3f);
        GameObject vlsFwd = AddFriendlyVls(root.transform, "VLS_Fwd", vlsMissile, new Vector3(0f, 11f, 38f), both);
        GameObject vlsAft = AddFriendlyVls(root.transform, "VLS_Aft", vlsMissile, new Vector3(0f, 11f, -62f), both);
        // 이름이 "AAGun"이어야 아군 CIWS(함대 방어)로 잡힌다 — NPC_GuardShip과 같은 규칙
        ACO_SceneBaker.ApplyInterceptDefaults(ciwsFwd.GetComponent<ACO_AAGunSite>());
        ACO_SceneBaker.ApplyInterceptDefaults(ciwsAft.GetComponent<ACO_AAGunSite>());
        unit.DefenseGuns = new UdonSharp.UdonSharpBehaviour[]
        {
            gun.GetComponent<ACO_AAGunSite>(), ciwsFwd.GetComponent<ACO_AAGunSite>(), ciwsAft.GetComponent<ACO_AAGunSite>(),
            vlsFwd.GetComponent<ACO_SAMSite>(), vlsAft.GetComponent<ACO_SAMSite>(),
        };
        unit.HideOnDeath = new[] { gun, ciwsFwd, ciwsAft, vlsFwd, vlsAft };
        return SaveAndCleanup(root, $"{PrefabDir}/NPC_Flagship.prefab");
    }

    /// 적 항모: 함대전 전용(풀에 있지만 생성기가 일반 고가치 표적 배치에 쓰지 않는다 — ACO_Warship.ReservedForFleet). 격침하면 함대전 성공.
    private static ShipSpec CarrierSpec()
    {
        return new ShipSpec
        {
            PrefabName = "NPC_Carrier", TargetName = "Aircraft Carrier", Label = "CV", HighValue = true, Score = 3000,
            Health = 1600f, Armor = 4f, NoDamageBelow = 3f, PartHullDamage = 0.05f,
            CruiseSpeed = 0f, CruiseRadius = 0f, EscortSpacing = 500f,
            HullSize = new Vector3(70f, 22f, 300f),
            BridgeSize = new Vector3(14f, 34f, 50f), BridgeCenter = new Vector3(26f, 30f, -20f),
            FunnelSize = Vector3.one, FunnelCenter = new Vector3(26f, 40f, -20f), MastHeight = 1f,
            ModelPrefabPath = KuznetsovPath, ReservedForFleet = true,
            Parts = new[]
            {
                // 쿠즈네초프 모델에 그려진 포대(양현 스폰슨의 대형 포탑 짝) 자리 — 6-183. 예전 자리(±30, 19, 110 / -120)는 앞쪽이 좁은 갑판 밖 허공이었다
                new PartSpec(PartKind.CIWS, "CIWS_FL", CarrierCiwsSpots[0]),
                new PartSpec(PartKind.CIWS, "CIWS_FR", CarrierCiwsSpots[1]),
                new PartSpec(PartKind.CIWS, "CIWS_AL", CarrierCiwsSpots[2]),
                new PartSpec(PartKind.CIWS, "CIWS_AR", CarrierCiwsSpots[3]),
                // 모델에 그려진 VLS 해치 묶음(포대 옆 둥근 해치 — Etc_Turret) 자리 — 6-184
                new PartSpec(PartKind.VLS, "VLS_FL", CarrierVlsSpots[0]),
                new PartSpec(PartKind.VLS, "VLS_FR", CarrierVlsSpots[1]),
                new PartSpec(PartKind.VLS, "VLS_AL", CarrierVlsSpots[2]),
                new PartSpec(PartKind.VLS, "VLS_AR", CarrierVlsSpots[3]),
            },
        };
    }

    /// 항모 VLS 자리(FL · FR · AL · AR, 함선 로컬) — 모델 해치판(Etc_Turret, 윗면 14.8) 가운데. 발사기 상자(높이 1.2)가 반쯤 해치판 위로 나온다.
    public static readonly string[] CarrierVlsNames = { "VLS_FL", "VLS_FR", "VLS_AL", "VLS_AR" };
    public static readonly Vector3[] CarrierVlsSpots =
    {
        new Vector3(-22f, 14.2f, 55.6f),
        new Vector3(21.1f, 14.2f, 60.9f),
        new Vector3(-21.8f, 14.2f, -117.4f),
        new Vector3(28.2f, 14.2f, -117.4f),
    };

    /// carrier-vls-1(6-184): 있는 항모 프리팹(LoadPrefabContents 루트)에 VLS 4개를 붙이거나 자리를 맞춘다 — 새로 만들 때와 같은 부위(CreateVLSObject), 함선 Parts에 더한다.
    public static bool EnsureCarrierVls(GameObject root)
    {
        ACO_Warship ship = root.GetComponent<ACO_Warship>();
        Transform body = root.transform.Find("Body");
        if (ship == null || body == null) { return false; }
        bool changed = false;
        GameObject missile = null;
        var parts = new System.Collections.Generic.List<UdonSharp.UdonSharpBehaviour>(ship.Parts ?? new UdonSharp.UdonSharpBehaviour[0]);
        for (int i = 0; i < CarrierVlsNames.Length; i++)
        {
            Transform t = body.Find(CarrierVlsNames[i]);
            if (t == null)
            {
                if (missile == null) { missile = LoadOrBuildVlsMissile(); }
                GameObject part = CreateVLSObject(CarrierVlsNames[i], missile);
                AttachPart(body.gameObject, part, CarrierVlsSpots[i]);
                ACO_SAMSite sam = part.GetComponent<ACO_SAMSite>();
                if (sam != null && !parts.Contains(sam)) { parts.Add(sam); }
                changed = true;
            }
            else if ((t.localPosition - CarrierVlsSpots[i]).sqrMagnitude > 0.0001f)
            {
                t.localPosition = CarrierVlsSpots[i];
                changed = true;
            }
        }
        if (changed)
        {
            ship.Parts = parts.ToArray();
            ACO_PrefabTuning.TuneShip(ship);
            HideCarrierPartModels(root);   // 새로 붙인 VLS도 외형은 선체 모델이 맡는다(6-185)
        }
        return changed;
    }

    /// 항모 CIWS 자리(FL · FR · AL · AR, 함선 로컬) — 모델 포탑 메시(Large_Turret 짝)의 가운데. Setup Check carrier-ciws-1도 쓴다.
    public static readonly string[] CarrierCiwsNames = { "CIWS_FL", "CIWS_FR", "CIWS_AL", "CIWS_AR" };
    public static readonly Vector3[] CarrierCiwsSpots =
    {
        new Vector3(-20.9f, 13.9f, 69.65f),
        new Vector3(20.9f, 15.7f, 72.5f),
        new Vector3(-21.3f, 13.7f, -127.85f),
        new Vector3(28f, 13.7f, -127.85f),
    };

    public static GameObject BuildCarrier()
    {
        EnsurePrefabFolder();
        GameObject prefab = BuildShipPrefab(CarrierSpec(), LoadOrBuildVlsMissile());
        if (prefab != null)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool hid = HideCarrierPartModels(root);
                bool arc = ApplyCarrierCiwsArc(root);
                if (hid || arc) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return prefab;
    }

    /// 항모 CIWS 좌우 한계(6-186): 양현 스폰슨에 붙어 있어 선체 반대편은 못 쏜다 — 바깥쪽 200°.
    public const float CarrierCiwsYawArc = 200f;

    /// carrier-ciws-arc-1(6-186): 항모 CIWS 4개의 쉬는 자세를 바깥쪽(좌현 -90°, 우현 +90°)으로 돌리고 YawArc를 200°로 — 선체 너머 미사일은 요격 · 사격하지 않는다.
    public static bool ApplyCarrierCiwsArc(GameObject root)
    {
        Transform body = root.transform.Find("Body");
        if (body == null) { return false; }
        bool changed = false;
        foreach (string n in CarrierCiwsNames)
        {
            Transform part = body.Find(n);
            ACO_AAGunSite aa = part != null ? part.GetComponent<ACO_AAGunSite>() : null;
            if (aa == null) { continue; }
            if (Mathf.Abs(aa.YawArc - CarrierCiwsYawArc) > 0.01f) { aa.YawArc = CarrierCiwsYawArc; EditorUtility.SetDirty(aa); changed = true; }
            if (aa.TurretYaw == null) { continue; }
            Quaternion rest = Quaternion.Euler(0f, part.localPosition.x < 0f ? -90f : 90f, 0f);
            if (Quaternion.Angle(aa.TurretYaw.localRotation, rest) > 0.1f) { aa.TurretYaw.localRotation = rest; EditorUtility.SetDirty(aa.TurretYaw); changed = true; }
        }
        return changed;
    }

    /// carrier-part-models-1(6-185): 항모 CIWS · VLS의 임시 외형(상자 · 포신 MeshRenderer)을 끈다 — 모델 선체에 포대 · 해치가 그려져 있다.
    /// 히트박스 · 락온 마커 · 포구 · 예광탄 · 파괴 연출(파티클)은 그대로라 쏘고 맞고 터지는 건 같다.
    public static bool HideCarrierPartModels(GameObject root)
    {
        Transform body = root.transform.Find("Body");
        if (body == null) { return false; }
        bool changed = false;
        foreach (string n in CarrierCiwsNames.Concat(CarrierVlsNames))
        {
            Transform part = body.Find(n);
            if (part == null) { continue; }
            foreach (MeshRenderer r in part.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled) { continue; }
                r.enabled = false;
                changed = true;
            }
        }
        return changed;
    }
}
