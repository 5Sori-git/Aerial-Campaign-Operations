using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using SaccFlightAndVehicles;
using ACO;
using Object = UnityEngine.Object;

/// 프리팹 수치 변경. 씬이 아니라 프리팹 에셋을 고치고, 씬 인스턴스에 남은 같은 값의 오버라이드를 지운다.
/// Setup Check의 "프리팹 수치" 항목이 부른다. 적용하거나 무시한 항목은 ID를 AppliedPath에 적어 다시 묻지 않는다.
/// 수치를 또 바꾸려면 Entries에 새 ID로 항목을 더한다.
public static class ACO_PrefabTuning
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    private const string AppliedPath = "Assets/ACO_Addon/Editor/ACO_AppliedTuning.txt";
    private const int FriendlyShips = 1 << 26;

    public class Entry
    {
        public string Id;
        public string Text;
        public Action Apply;
    }

    public static readonly Entry[] Entries =
    {
        new Entry { Id = "ciws-1", Text = "NPC 프리팹에 CIWS 요격 기본값과 탄 판정 반경을 넣습니다", Apply = TuneCiws },
        new Entry { Id = "escort-priority-1", Text = "NPC 프리팹에 호위 유닛 공격 우선순위를 넣습니다", Apply = TuneEscortPriority },
        new Entry { Id = "fleet-1", Text = "적 함선 프리팹의 VLS·ASM·함포가 해수면의 아군 함선을 노리게 합니다(순양함 VLS 사거리 5km)", Apply = TuneFleet },
        new Entry { Id = "railgun-mini-1", Text = "랜스 포대 소형 레일건 4문을 NPC_Turret_RailgunMini 프리팹으로 바꿉니다", Apply = ACO_PrefabBuilder.ConvertRailgunMinis },
        new Entry { Id = "railgun-model-1", Text = "랜스 포대와 소형 레일건을 새 모델로 바꿉니다. 옮긴 부위의 충돌도 맞춥니다. 씬 참조는 유지됩니다", Apply = ACO_PrefabBuilder.InstallLanceModel },
        new Entry { Id = "turret-model-1", Text = "Flak, CIWS, VLS 포대를 새 모델로 바꿉니다. 공중 요새의 포대도 함께 바뀝니다. 팩 모델 자식은 프리팹에서 지웁니다", Apply = ACO_PrefabBuilder.InstallTurretModels },
        new Entry { Id = "fortress-turrets-3", Text = "공중 요새 포대를 Flak 4, CIWS 8, VLS 6으로 바꾸고 선체 경사에 맞춰 좌우 대칭으로 놓습니다. 배 쪽 VLS 2문과 후면 해치 CIWS 2문이 늘고 Flak 4문이 빠집니다", Apply = ACO_PrefabBuilder.PlaceFortressTurrets },
        new Entry { Id = "fortress-core-1", Text = "공중 요새 코어를 세운 원자로로 바꾸고, 코어 히트박스와 증원 발진 지점을 실제 자리에 맞춥니다", Apply = ACO_PrefabBuilder.PlaceFortressCore },
        new Entry { Id = "scm-model-1", Text = "SCM 탄체를 날개를 편 새 모델로 바꿉니다. 예전 모델은 프리팹에 꺼 둡니다", Apply = ACO_PrefabBuilder.InstallScmModel },
        new Entry { Id = "deco-model-1", Text = "도시, 마을, 항구, 비행장, 등대, 레이더, 통신탑, 유적을 새 모델로 다시 만듭니다. 프리팹을 손으로 고쳤다면 그 내용은 사라집니다", Apply = ACO_DecoBuilder.InstallModels },
    };

    public static bool IsApplied(string id)
    {
        if (!File.Exists(AppliedPath)) { return false; }
        return Array.IndexOf(File.ReadAllLines(AppliedPath), id) >= 0;
    }

    public static void MarkApplied(string id)
    {
        if (IsApplied(id)) { return; }
        File.AppendAllText(AppliedPath, id + "\n");
        AssetDatabase.ImportAsset(AppliedPath);
    }

    public static void Apply(Entry entry)
    {
        entry.Apply();
        AssetDatabase.SaveAssets();
        MarkApplied(entry.Id);
        Debug.Log($"[ACO] 프리팹 수치 '{entry.Id}'를 적용했습니다.");
    }

    // ───────────── CIWS ─────────────

    private static void TuneCiws()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_AAGunSite aa in root.GetComponentsInChildren<ACO_AAGunSite>(true))
            {
                bool dirty = false;
                if (!aa.InterceptDefaultsApplied) { ACO_SceneBaker.ApplyInterceptDefaults(aa); dirty = true; }
                if (aa.InterceptTuneVersion < ACO_SceneBaker.CiwsTuneVersion && (aa.InterceptProtectLayers.value & (1 << 29)) != 0)
                {
                    ACO_SceneBaker.ApplyEnemyCiwsTune(aa);
                    dirty = true;
                }
                if (ACO_SceneBaker.ApplyShellRadius(aa)) { Mark(aa.Gun); dirty = true; }
                if (dirty) { Mark(aa); any = true; }
            }
            return any;
        });
        RevertInScene<ACO_AAGunSite>("InterceptProtectLayers", "InterceptRange", "InterceptReactionTime", "InterceptKillTime",
            "InterceptCooldown", "DefenseRadius", "OtherDefenseFactor", "InterceptTuneVersion", "InterceptDefaultsApplied");
        RevertInScene<ACO_NPCGun>("ShellHitRadius");
    }

    // ───────────── 호위 유닛 ─────────────

    private static void TuneEscortPriority()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_EscortUnit e in root.GetComponentsInChildren<ACO_EscortUnit>(true))
            {
                if (e.PriorityDefaultsApplied) { continue; }
                e.PriorityDefaultsApplied = true;
                bool flagship = e.Role == ACO_EscortUnit.ROLE_FLAGSHIP;
                if (flagship) { e.AttackPriority = 2500f; }
                if (flagship || (e.MoveMode == ACO_EscortUnit.MOVE_AIR && !e.Hostile && e.Role == ACO_EscortUnit.ROLE_VIP)) { e.PriorityForSites = true; }
                Mark(e);
                any = true;
            }
            return any;
        });
        RevertInScene<ACO_EscortUnit>("AttackPriority", "PriorityForSites", "PriorityDefaultsApplied");
    }

    // ───────────── 함대전 ─────────────

    private static void TuneFleet()
    {
        EditPrefabs(root =>
        {
            bool any = false;
            foreach (ACO_Warship ship in root.GetComponentsInChildren<ACO_Warship>(true))
            {
                if (TuneShip(ship)) { any = true; }
            }
            return any;
        });
        RevertInScene<ACO_SAMSite>("TargetLayers", "MinTargetAltitude", "DetectRange", "LoseTargetRange");
        foreach (ACO_AAGunSite gun in FindInScene<ACO_AAGunSite>())
        {
            if (IsMainGun(gun)) { Revert(gun, "TargetLayers", "MinTargetAltitude"); }
        }
    }

    /// 함선 VLS·ASM·함포가 해수면의 아군 함선(26)을 노리게 한다. 두 함대는 3.5km에서 멈추므로 그보다 짧은 사거리만 올린다.
    public static bool TuneShip(ACO_Warship ship)
    {
        if (ship.ExternalPose || ship.Parts == null) { return false; }   // 공중 요새는 함대전에 나오지 않는다
        bool any = false;
        foreach (UdonSharp.UdonSharpBehaviour part in ship.Parts)
        {
            ACO_SAMSite sam = part as ACO_SAMSite;
            if (sam != null)
            {
                bool dirty = false;
                if ((sam.TargetLayers.value & FriendlyShips) == 0) { sam.TargetLayers = sam.TargetLayers.value | FriendlyShips; dirty = true; }
                if (sam.MinTargetAltitude > -100000f) { sam.MinTargetAltitude = -100000f; dirty = true; }
                if (sam.DetectRange < 4500f) { sam.DetectRange = 5000f; dirty = true; }
                if (sam.LoseTargetRange < 4500f) { sam.LoseTargetRange = 6000f; dirty = true; }
                if (AddShipTargets(sam.EntityControl)) { dirty = true; }
                if (dirty) { Mark(sam); any = true; }
            }
            ACO_AAGunSite gun = part as ACO_AAGunSite;
            if (gun != null && IsMainGun(gun))   // CIWS는 대공·요격 그대로
            {
                bool dirty = false;
                if ((gun.TargetLayers.value & FriendlyShips) == 0) { gun.TargetLayers = gun.TargetLayers.value | FriendlyShips; dirty = true; }
                if (gun.MinTargetAltitude > -100000f) { gun.MinTargetAltitude = -100000f; dirty = true; }
                if (AddShipTargets(gun.EntityControl)) { dirty = true; }
                if (dirty) { Mark(gun); any = true; }
            }
        }
        return any;
    }

    private static bool IsMainGun(ACO_AAGunSite gun) { return gun.gameObject.name.Contains("MainGun"); }

    private static bool AddShipTargets(SaccEntity entity)
    {
        if (entity == null || (entity.AAMTargetsLayer.value & FriendlyShips) != 0) { return false; }
        entity.AAMTargetsLayer |= FriendlyShips;
        Mark(entity);
        return true;
    }

    // ───────────── 공통 ─────────────

    /// NPC 프리팹을 하나씩 열어 고친다. 포대 프리팹을 먼저 고쳐야 포대를 품은 함선·요새가 새 값을 받는다.
    private static void EditPrefabs(Func<GameObject, bool> edit)
    {
        var paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("NPC_ t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path).StartsWith("NPC_")) { paths.Add(path); }
        }
        paths.Sort((a, b) =>
        {
            int ta = a.Contains("NPC_Turret") ? 0 : 1;
            int tb = b.Contains("NPC_Turret") ? 0 : 1;
            return ta != tb ? ta - tb : string.CompareOrdinal(a, b);
        });
        foreach (string path in paths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!edit(root)) { continue; }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] 프리팹 수치: {Path.GetFileName(path)}를 고쳤습니다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    private static void Mark(Object o)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        EditorUtility.SetDirty(o);
    }

    private static List<T> FindInScene<T>() where T : Component
    {
        var list = new List<T>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            list.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return list;
    }

    private static void RevertInScene<T>(params string[] fields) where T : Component
    {
        foreach (T c in FindInScene<T>()) { Revert(c, fields); }
    }

    /// 씬 인스턴스의 오버라이드를 지운다 — 프리팹 값이 보이게. 프리팹이 아닌 씬 오브젝트는 그대로다.
    private static void Revert(Component c, params string[] fields)
    {
        if (!PrefabUtility.IsPartOfPrefabInstance(c)) { return; }
        var so = new SerializedObject(c);
        foreach (string field in fields)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null && p.prefabOverride) { PrefabUtility.RevertPropertyOverride(p, InteractionMode.UserAction); }
        }
    }
}
