using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ACO;

/// 공중 요새·랜스 포대가 함께 쓰는 포대 프리팹(NPC_Turret_Flak / CIWS / VLS). 두 보스의 포대는 이 프리팹의 중첩 인스턴스다 —
/// 체력·사거리·요격 수치·모델은 이 프리팹 하나만 고치면 모두 바뀐다. 자리·크기·피치 범위·락온 레이어만 보스 쪽 값이다.
/// 없을 때만 만든다. 다시 만들면 중첩 인스턴스와의 연결이 끊어지므로 프리팹 창에 넣지 않았다 — 직접 고쳐서 쓴다.
public static partial class ACO_PrefabBuilder
{
    private const string TurretFlak = "NPC_Turret_Flak";
    private const string TurretCiws = "NPC_Turret_CIWS";
    private const string TurretVls = "NPC_Turret_VLS";

    private static GameObject SaveTurret(GameObject root)
    {
        Material mat = FortressMaterial("ACO_FortressPart", new Color(0.12f, 0.13f, 0.15f), 0.6f, 0.4f, Color.black);
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true)) { r.sharedMaterial = mat; }
        // 기준값을 프리팹에 넣어 둔다 — 그래야 Bake가 씬에 덮어쓰지 않고 프리팹 수정이 그대로 전해진다
        ACO_AAGunSite gun = root.GetComponent<ACO_AAGunSite>();
        if (gun != null)
        {
            ACO_SceneBaker.ApplyInterceptDefaults(gun);
            if (gun.TargetHealth != null) { gun.BaseHealth = gun.TargetHealth.Health; }
            ACO_SceneBaker.ApplyShellRadius(gun);
        }
        ACO_SAMSite sam = root.GetComponent<ACO_SAMSite>();
        if (sam != null && sam.TargetHealth != null) { sam.BaseHealth = sam.TargetHealth.Health; }
        return SaveAndCleanup(root, PathOf(root.name));
    }

    private static GameObject TurretAsset(string prefabName)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(prefabName));
        if (asset != null) { return asset; }
        EnsurePrefabFolder();
        if (prefabName == TurretFlak) { return SaveTurret(CreateAAGunObject(prefabName, FortressFlakSpec())); }
        if (prefabName == TurretCiws) { return SaveTurret(CreateAAGunObject(prefabName, FortressCiwsSpec())); }
        return SaveTurret(CreateVLSObject(prefabName, LoadOrBuildVlsMissile()));
    }

    /// 포대 인스턴스 하나를 parent 아래에 놓는다. 인스턴스 값은 기록해야 저장할 때 살아남는다.
    private static GameObject NewTurret(string prefabName, string name, Transform parent, Vector3 pos, Quaternion rot, float scale)
    {
        GameObject part = (GameObject)PrefabUtility.InstantiatePrefab(TurretAsset(prefabName), parent);
        part.name = name;
        part.transform.localPosition = pos;
        part.transform.localRotation = rot;
        part.transform.localScale = Vector3.one * scale;
        PrefabUtility.RecordPrefabInstancePropertyModifications(part);
        PrefabUtility.RecordPrefabInstancePropertyModifications(part.transform);
        return part;
    }

    /// 먼저 만든 공용 포대에 탄 도착 판정을 넣는다(프리팹 쪽에 넣어야 프리팹 수정이 씬 값에 가리지 않는다).
    public static void EnsureTurretShellRadius()
    {
        foreach (string prefabName in new[] { TurretFlak, TurretCiws })
        {
            string path = PathOf(prefabName);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ACO_AAGunSite gun = root.GetComponent<ACO_AAGunSite>();
                if (gun == null || !ACO_SceneBaker.ApplyShellRadius(gun)) { continue; }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    private static void ReplacePart(List<UdonSharp.UdonSharpBehaviour> list, UdonSharp.UdonSharpBehaviour from, UdonSharp.UdonSharpBehaviour to)
    {
        int i = list.IndexOf(from);
        if (i >= 0) { list[i] = to; }
    }

    /// 먼저 만든 랜스 포대의 포대들을 프리팹 인스턴스로 바꾼다 — 자리·크기는 그대로, 프리팹을 다시 만들지 않아 바꿔 끼운 모델이 남는다.
    public static void EnsureRailgunTurrets()
    {
        string path = PathOf("NPC_Railgun");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_Railgun railgun = root.GetComponent<ACO_Railgun>();
            ACO_Warship ship = root.GetComponent<ACO_Warship>();
            if (railgun == null || ship == null || railgun.AirDefense == null || ship.Parts == null) { return; }
            var defense = new List<UdonSharp.UdonSharpBehaviour>(railgun.AirDefense);
            var parts = new List<UdonSharp.UdonSharpBehaviour>(ship.Parts);
            int swapped = 0;
            foreach (UdonSharp.UdonSharpBehaviour old in railgun.AirDefense)
            {
                if (old == null || PrefabUtility.IsPartOfPrefabInstance(old.gameObject)) { continue; }
                if (old.GetComponent<ACO_MiniRailgun>() != null) { continue; }   // 소형 레일건은 연출을 공유해 프리팹으로 빼지 않는다
                ACO_AAGunSite gun = old.GetComponent<ACO_AAGunSite>();
                bool vls = old.GetComponent<ACO_SAMSite>() != null;
                if (!vls && gun == null) { continue; }
                string prefabName = vls ? TurretVls : (gun.MarkerLabel == "CIWS" ? TurretCiws : TurretFlak);
                Transform t = old.transform;
                GameObject part = NewTurret(prefabName, old.name, t.parent, t.localPosition, t.localRotation, t.localScale.x);
                part.transform.SetSiblingIndex(t.GetSiblingIndex());
                UdonSharp.UdonSharpBehaviour fresh = vls ? (UdonSharp.UdonSharpBehaviour)part.GetComponent<ACO_SAMSite>() : part.GetComponent<ACO_AAGunSite>();
                ReplacePart(defense, old, fresh);
                ReplacePart(parts, old, fresh);
                Object.DestroyImmediate(old.gameObject);
                swapped++;
            }
            if (swapped == 0) { return; }
            railgun.AirDefense = defense.ToArray();
            ship.Parts = parts.ToArray();
            ACO_SyncModeFixer.FixHierarchy(root, false);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[ACO] 랜스 포대: 포대 {swapped}곳을 공용 프리팹(NPC_Turret_*)으로 바꿨습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
