using System.Collections.Generic;
using UdonSharp;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.Udon;

/// 한 GameObject에 붙은 UdonBehaviour들의 동기화 방식을 하나로 맞춘다.
/// VRChat은 오브젝트 단위로 직렬화하므로, SaccTarget/AI(Manual) 옆의 SaccEntity·ACO_StaticTarget 등
/// NoVariableSync 컴포넌트가 Continuous로 남아 있으면 오브젝트 전체가 Continuous처럼 동작한다
/// (4Hz 상시 전송 + 비신뢰 전송 → 혼잡 시 상태가 1~2초씩 늦게 도착).
///
/// 에디터 씬만 고쳐서는 안 된다: UdonSharp의 UpdateSyncModes(씬 열기, 플레이 모드 전환, 빌드 후 정리 때 실행)는
/// 옆에 Manual이 있어도 이미 Manual인 NoVariableSync 컴포넌트를 Continuous로 바꾼다(else-if 조건 버그). 그래서 실행할 때마다
/// Manual ↔ Continuous를 오가고, SyncMethod 설정자가 같은 오브젝트의 다른 UdonBehaviour까지 같은 값으로 바꾼다.
/// 그래서 빌드되는 씬 사본에 마지막으로(PostProcessScene, UdonSharp 다음) 다시 맞춘다 — 저장된 씬과 무관하게 빌드 결과는 Manual이다.
/// 메뉴로도 실행할 수 있다. SAV 폴더의 에셋은 건드리지 않는다.
public class ACO_SyncModeFixer : IVRCSDKBuildRequestedCallback
{
    private const string AddonPath = "Assets/ACO_Addon/";
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";

    // 씬 베이크(11) 다음에 실행한다.
    public int callbackOrder => 12;

    public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
    {
        FixScene();
        return true;
    }

    /// 빌드(및 플레이 모드)용 씬 사본 처리의 마지막 단계. UdonSharp의 PostProcessScene(기본 순서 0)보다 뒤에 돈다.
    /// 사본이므로 오버라이드를 기록하거나 저장하지 않는다.
    [PostProcessScene(1000)]
    private static void OnPostProcessScene()
    {
        int count = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            count += FixHierarchy(root, false);
        }
        if (count > 0) { Debug.Log($"[ACO] 빌드 씬: UdonBehaviour {count}개를 Manual 동기화로 맞췄습니다."); }
    }

    [MenuItem("ACO/Fix Sync Modes (Scene + Prefabs)")]
    public static void FixAll()
    {
        int prefabFixes = FixPrefabAssets();
        int sceneFixes = FixScene();
        Debug.Log($"[ACO] 동기화 방식 정리: 프리팹 {prefabFixes}개, 씬 {sceneFixes}개 컴포넌트를 Manual로 맞췄습니다.");
    }

    public static int FixScene()
    {
        int count = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            count += FixHierarchy(root, true);
        }
        if (count > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[ACO] 씬의 UdonBehaviour {count}개를 Manual 동기화로 맞췄습니다. 씬을 저장하세요.");
        }
        return count;
    }

    private static int FixPrefabAssets()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            int fixes = FixHierarchy(contents, false);
            if (fixes > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                count += fixes;
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }
        return count;
    }

    /// 애드온 스크립트가 NoVariableSync(네트워크 이벤트를 쓴다)로 바뀌었는데 컴포넌트가 예전 값 None으로 남아 있으면
    /// 이벤트가 나가지 않는다(예: ACO_VehicleUpgrades — 소모품 외형). Manual로 맞춘다.
    private static int FixNoneButNetworked(UdonBehaviour[] behaviours, bool recordPrefabOverrides)
    {
        int count = 0;
        foreach (UdonBehaviour ub in behaviours)
        {
            if (!(ub.programSource is UdonSharpProgramAsset program)) { continue; }
            if (!AssetDatabase.GetAssetPath(program).StartsWith(AddonPath)) { continue; }
            if (program.behaviourSyncMode != BehaviourSyncMode.NoVariableSync || ub.SyncMethod != Networking.SyncType.None) { continue; }
            ub.SyncMethod = Networking.SyncType.Manual;
            if (recordPrefabOverrides && PrefabUtility.IsPartOfPrefabInstance(ub)) { PrefabUtility.RecordPrefabInstancePropertyModifications(ub); }
            EditorUtility.SetDirty(ub);
            count++;
        }
        return count;
    }

    /// 프리팹 빌더가 저장 직전에 호출한다.
    public static int FixHierarchy(GameObject root, bool recordPrefabOverrides)
    {
        int count = 0;
        var visited = new HashSet<GameObject>();
        foreach (UdonBehaviour ub in root.GetComponentsInChildren<UdonBehaviour>(true))
        {
            if (visited.Add(ub.gameObject)) { count += FixObject(ub.gameObject, recordPrefabOverrides); }
        }
        return count;
    }

    private static int FixObject(GameObject go, bool recordPrefabOverrides)
    {
        UdonBehaviour[] behaviours = go.GetComponents<UdonBehaviour>();
        int count = FixNoneButNetworked(behaviours, recordPrefabOverrides);
        if (behaviours.Length < 2) { return count; }

        bool hasAddon = false;
        bool hasManual = false;
        foreach (UdonBehaviour ub in behaviours)
        {
            if (ub.programSource == null) { continue; }
            if (AssetDatabase.GetAssetPath(ub.programSource).StartsWith(AddonPath)) { hasAddon = true; }
            if (ub.programSource is UdonSharpProgramAsset program)
            {
                if (program.behaviourSyncMode == BehaviourSyncMode.Manual) { hasManual = true; }
            }
            else if (ub.SyncMethod == Networking.SyncType.Manual) { hasManual = true; }
        }
        // 애드온 컴포넌트가 없는 오브젝트(SAV 기체 등)는 그대로 둔다.
        if (!hasAddon || !hasManual) { return count; }

        foreach (UdonBehaviour ub in behaviours)
        {
            if (!(ub.programSource is UdonSharpProgramAsset program)) { continue; }
            BehaviourSyncMode mode = program.behaviourSyncMode;
            bool adaptable = mode == BehaviourSyncMode.NoVariableSync || mode == BehaviourSyncMode.Any || mode == BehaviourSyncMode.Manual;
            if (!adaptable) { continue; }

            // SyncMethod 설정자는 같은 오브젝트의 다른 UdonBehaviour 값도 메모리에서만 바꾼다. 그래서 메모리 값은 Manual인데
            // 씬의 프리팹 오버라이드에는 옛 값(Continuous)이 남아 있고, 씬을 다시 열거나 빌드하면 그 값이 되살아난다.
            bool staleOverride = recordPrefabOverrides && HasStaleSyncOverride(ub);
            if (ub.SyncMethod == Networking.SyncType.Manual && !staleOverride) { continue; }

            ub.SyncMethod = Networking.SyncType.Manual;
            if (recordPrefabOverrides && PrefabUtility.IsPartOfPrefabInstance(ub))
            {
                // 인스턴스 값과 원본 프리팹을 다시 비교해 오버라이드 목록을 갱신한다(옛 값이 지워지거나 Manual로 바뀐다).
                PrefabUtility.RecordPrefabInstancePropertyModifications(ub);
            }
            EditorUtility.SetDirty(ub);
            count++;
        }
        return count;
    }

    /// 씬 인스턴스의 오버라이드 목록에 _syncMethod가 Manual이 아닌 값으로 저장돼 있는가.
    private static bool HasStaleSyncOverride(UdonBehaviour ub)
    {
        if (!PrefabUtility.IsPartOfPrefabInstance(ub)) { return false; }
        Object source = PrefabUtility.GetCorrespondingObjectFromSource(ub);
        PropertyModification[] mods = PrefabUtility.GetPropertyModifications(ub);
        if (source == null || mods == null) { return false; }

        string manual = ((int)Networking.SyncType.Manual).ToString();
        foreach (PropertyModification mod in mods)
        {
            if (mod.target == source && mod.propertyPath == "_syncMethod" && mod.value != manual) { return true; }
        }
        return false;
    }
}
