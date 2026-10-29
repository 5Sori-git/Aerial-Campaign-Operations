using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.SDKBase.Network;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.Udon;

/// 씬 디스크립터의 네트워크 ID 목록은 오브젝트마다 "네트워크 컴포넌트 구성"을 함께 저장한다. 애드온 프리팹에 UdonBehaviour를
/// 더하거나 빼면(예: 전투기에 ACO_NPCFighterNet 추가) 저장된 구성과 달라져 빌드가 "Failed to assign network IDs"로 멈춘다.
/// ID 번호는 그대로 두고 구성만 현재 것으로 바꾼다 — ClientSim이 플레이 모드에서 같은 오류를 고치는 방식과 같다.
/// 애드온 컴포넌트가 없는 오브젝트(SAV 등)는 건드리지 않는다.
public class ACO_NetworkIDFixer : IVRCSDKBuildRequestedCallback
{
    private const string AddonPath = "Assets/ACO_Addon/";

    // 동기화 방식 정리(12) 다음, SDK의 네트워크 ID 할당(씬 전처리) 전에 실행된다.
    public int callbackOrder => 13;

    public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
    {
        if (FixNetworkIDs() > 0) { EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); }
        return true;
    }

    [MenuItem("ACO/Fix Network IDs (after prefab changes)")]
    public static void FixMenu()
    {
        int count = FixNetworkIDs();
        Debug.Log($"[ACO] 네트워크 ID 구성 정리: {count}개 오브젝트를 고쳤습니다." + (count > 0 ? " 씬을 저장하세요." : ""));
    }

    public static int FixNetworkIDs()
    {
        VRC_SceneDescriptor descriptor = Object.FindObjectOfType<VRC_SceneDescriptor>();
        if (descriptor == null) { return 0; }

        NetworkIDAssignment.ConfigureNetworkIDs(descriptor, out List<NetworkIDAssignment.SetErrorLocation> errors,
            NetworkIDAssignment.SetError.InvalidObject);

        var fixedPairs = new List<NetworkIDPair>();
        foreach (NetworkIDAssignment.SetErrorLocation error in errors)
        {
            if (error.error != NetworkIDAssignment.SetError.IncompatibleTypes) { continue; }
            GameObject go = error.location.gameObject;
            if (go == null) { continue; }
            if (!HasAddonBehaviour(go))
            {
                Debug.LogWarning($"[ACO] '{go.name}'의 네트워크 ID 구성이 바뀌었지만 애드온 오브젝트가 아니라 건드리지 않습니다. VRChat SDK의 Network ID Utility로 확인하세요.", go);
                continue;
            }
            fixedPairs.Add(new NetworkIDPair { gameObject = go, ID = error.location.ID, SerializedTypeNames = NetworkIDAssignment.GetSerializedTypes(go) });
        }
        if (fixedPairs.Count == 0) { return 0; }

        descriptor.NetworkIDCollection = descriptor.NetworkIDCollection
            .Where(pair => fixedPairs.FindIndex(f => f.ID == pair.ID && f.gameObject == pair.gameObject) == -1)
            .ToList();
        descriptor.NetworkIDCollection.AddRange(fixedPairs);

        EditorUtility.SetDirty(descriptor);
        if (PrefabUtility.IsPartOfPrefabInstance(descriptor)) { PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor); }
        EditorSceneManager.MarkSceneDirty(descriptor.gameObject.scene);

        foreach (NetworkIDPair pair in fixedPairs)
        {
            Debug.Log($"[ACO] 네트워크 ID {pair.ID} '{pair.gameObject.name}': 컴포넌트 구성을 현재 것으로 갱신했습니다.", pair.gameObject);
        }
        return fixedPairs.Count;
    }

    private static bool HasAddonBehaviour(GameObject go)
    {
        foreach (UdonBehaviour ub in go.GetComponents<UdonBehaviour>())
        {
            if (ub.programSource != null && AssetDatabase.GetAssetPath(ub.programSource).StartsWith(AddonPath)) { return true; }
        }
        return false;
    }
}
