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
public partial class ACO_SceneBaker : IVRCSDKBuildRequestedCallback
{
    // SAV의 셋업(10) 다음에 실행한다.
    public int callbackOrder => 11;

    public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
    {
        BakeNeighbors();
        return true;
    }

    /// 플레이어 기체와 충돌을 무시할 공중 유닛의 루트.
    public static List<GameObject> AirUnitRoots()
    {
        var airRoots = new List<GameObject>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_EscortUnit e in root.GetComponentsInChildren<ACO_EscortUnit>(true)) { if (e.MoveMode == ACO_EscortUnit.MOVE_AIR) { airRoots.Add(e.gameObject); } }
            foreach (ACO_MobileUnit m in root.GetComponentsInChildren<ACO_MobileUnit>(true)) { if (m.Kind == ACO_MobileUnit.KIND_HELICOPTER) { airRoots.Add(m.gameObject); } }
        }
        return airRoots;
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
            if (v <= 0f || v == f.BaseHealth) { continue; }
            f.BaseHealth = v;
            MarkChanged(f);
            healthFilled++;
        }
        if (healthFilled > 0) { Debug.Log($"[ACO] NPC 전투기 {healthFilled}기의 기준 체력(BaseHealth, 베테랑)을 채웠습니다(일반 전투기 150) — 숙련도 배율(생성기 TierHealthMultiplier)을 곱해 쓴다."); }

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
        // 충돌 방지 컴포넌트(ACO_AirCollisionIgnore)에 콜라이더·플레이어 기체를 채운다.
        var airUnits = new List<ACO_AirCollisionIgnore>();
        foreach (GameObject go in AirUnitRoots())
        {
            ACO_AirCollisionIgnore ign = go.GetComponent<ACO_AirCollisionIgnore>();
            if (ign == null) { continue; }   // 붙이기는 Setup Check가 맡는다
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
            Debug.Log($"[ACO] 공중 충돌 방지: 헬기·폭격기·수송기 등 {airUnits.Count}대" + " — 내 조종 기체와 서로 통과(충돌 데미지 없음).");
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
        ACO_SpecialWeaponCatalog.SyncLoadouts();
        BakePlayerMissiles();
        BakePlayerProgress();
        BakeGameFlow();
        BakePhaseModules();
        BakeFortress();
        BakeTimeSensitive();
        BakeFleet();
        BakeRailgun();
        BakeTierHealth();
        BakeTacticalMaps();
        BakeHiScoreBoard();
        BakeHudSettings();
        BakeRadio();
        BakeInterior();
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

    private static bool SameRefs(Object[] a, Object[] b)
    {
        if (a == null || a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
    }
    private static float[] GrowTo(float[] current, float[] defaults)
    {
        int n = defaults.Length;
        var r = new float[n];
        for (int i = 0; i < n; i++) { r[i] = current != null && i < current.Length ? current[i] : defaults[i]; }
        return r;
    }

    private static void MarkChanged(Object o)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        EditorUtility.SetDirty(o);
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
}
