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
    private const string TurretMini = "NPC_Turret_RailgunMini";

    private static GameObject SaveTurret(GameObject root, System.Action<GameObject> applyModel)
    {
        applyModel(root);
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
        if (prefabName == TurretMini) { return BuildMiniTurret(); }
        if (prefabName == TurretFlak) { return SaveTurret(CreateAAGunObject(prefabName, FortressFlakSpec()), ApplyFlakModel); }
        if (prefabName == TurretCiws) { return SaveTurret(CreateAAGunObject(prefabName, FortressCiwsSpec()), ApplyCiwsModel); }
        return SaveTurret(CreateVLSObject(prefabName, LoadOrBuildVlsMissile()), ApplyVlsModel);
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

}
