using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 함대전 함선: 아군 기함 NPC_Flagship(순양함 — Ticonderoga, 주포 1·VLS 2·CIWS 2)과 적 항모 NPC_Carrier(Kuznetsov, CIWS 4).
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
                new PartSpec(PartKind.CIWS, "CIWS_FL", new Vector3(-30f, 19f, 110f)),
                new PartSpec(PartKind.CIWS, "CIWS_FR", new Vector3(30f, 19f, 110f)),
                new PartSpec(PartKind.CIWS, "CIWS_AL", new Vector3(-30f, 19f, -120f)),
                new PartSpec(PartKind.CIWS, "CIWS_AR", new Vector3(30f, 19f, -120f)),
            },
        };
    }

    public static GameObject BuildCarrier()
    {
        EnsurePrefabFolder();
        return BuildShipPrefab(CarrierSpec(), LoadOrBuildVlsMissile());
    }
}
