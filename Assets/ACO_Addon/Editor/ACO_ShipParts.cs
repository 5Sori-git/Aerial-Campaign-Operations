using System.Collections.Generic;
using UdonSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 함선(ACO_Warship) 부위 목록 검사.
/// 인스펙터에서 부위 오브젝트를 Parts 칸에 끌어 넣으면 Unity가 그 오브젝트의 첫 U# 컴포넌트(SaccEntity)를 넣는다.
/// 함선은 격침·스폰·숨김을 SendCustomEvent("Silence"/"AttachedSpawn"/"MissionDespawn"/"NetRespawn")로 부위에 알리는데,
/// SaccEntity는 이 이벤트가 없어 그 부위만 아무것도 듣지 못한다 — 본체가 격침돼도 포탑이 계속 쏘고, 다음 미션에 함께 부활하지도 않는다.
/// 그래서 Parts 칸이 SaccEntity·SaccTarget이면 같은 오브젝트의 부위 스크립트(ACO_SAMSite·ACO_AAGunSite 등)로 바꾼다.
public static class ACO_ShipParts
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";

    /// 잘못 들어간 칸의 올바른 부위 스크립트(없으면 null — 고칠 수 없다).
    private static UdonSharpBehaviour PartScriptOn(GameObject go)
    {
        UdonSharpBehaviour fallback = null;
        foreach (UdonSharpBehaviour c in go.GetComponents<UdonSharpBehaviour>())
        {
            if (c == null || c is SaccEntity || c is SaccTarget) { continue; }
            if (c is ACO_SAMSite || c is ACO_AAGunSite) { return c; }
            if (fallback == null) { fallback = c; }
        }
        return fallback;
    }

    private static bool IsWrong(UdonSharpBehaviour part)
    {
        return part != null && (part is SaccEntity || part is SaccTarget);
    }

    /// 이 함선에서 잘못 들어간 칸 수.
    private static int CountWrong(ACO_Warship ship)
    {
        int n = 0;
        if (ship.Parts == null) { return 0; }
        foreach (UdonSharpBehaviour p in ship.Parts) { if (IsWrong(p)) { n++; } }
        return n;
    }

    /// 잘못 들어간 칸을 고친다. 고친 칸 수.
    private static int FixShip(ACO_Warship ship)
    {
        int fixedCount = 0;
        if (ship.Parts == null) { return 0; }
        UdonSharpBehaviour[] parts = (UdonSharpBehaviour[])ship.Parts.Clone();
        for (int i = 0; i < parts.Length; i++)
        {
            if (!IsWrong(parts[i])) { continue; }
            UdonSharpBehaviour right = PartScriptOn(parts[i].gameObject);
            if (right == null)
            {
                Debug.LogWarning($"[ACO] {ship.name} 부위 {i}({parts[i].gameObject.name})에 부위 스크립트가 없어 고치지 못했습니다 — 손으로 넣으세요.", ship);
                continue;
            }
            Debug.Log($"[ACO] {ship.name} 부위 {i}: {parts[i].GetType().Name} → {right.GetType().Name} ({right.gameObject.name})");
            parts[i] = right;
            fixedCount++;
        }
        if (fixedCount > 0) { ship.Parts = parts; }
        return fixedCount;
    }

    private static IEnumerable<string> ShipPrefabPaths()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null && asset.GetComponentInChildren<ACO_Warship>(true) != null) { yield return path; }
        }
    }

    /// 잘못 들어간 칸이 있는 곳(프리팹 이름·씬 오브젝트 이름). 없으면 빈 목록.
    public static List<string> FindWrong()
    {
        var list = new List<string>();
        foreach (string path in ShipPrefabPaths())
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int n = 0;
            foreach (ACO_Warship s in asset.GetComponentsInChildren<ACO_Warship>(true)) { n += CountWrong(s); }
            if (n > 0) { list.Add($"{asset.name}({n}칸)"); }
        }
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Warship s in root.GetComponentsInChildren<ACO_Warship>(true))
            {
                int n = CountWrong(s);
                if (n > 0) { list.Add($"씬 {s.name}({n}칸)"); }
            }
        }
        return list;
    }

    /// Setup Check [고치기]: 프리팹과 씬의 함선 부위 목록을 고친다(다른 칸·손으로 고친 것은 그대로).
    public static void FixAll()
    {
        foreach (string path in ShipPrefabPaths())
        {
            GameObject probe = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int wrong = 0;
            foreach (ACO_Warship s in probe.GetComponentsInChildren<ACO_Warship>(true)) { wrong += CountWrong(s); }
            if (wrong == 0) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int n = 0;
                foreach (ACO_Warship s in root.GetComponentsInChildren<ACO_Warship>(true)) { n += FixShip(s); }
                if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        bool sceneChanged = false;
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (ACO_Warship s in root.GetComponentsInChildren<ACO_Warship>(true))
            {
                if (CountWrong(s) == 0) { continue; }
                Undo.RecordObject(s, "ACO 함선 부위 고침");
                if (FixShip(s) > 0)
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(s);
                    EditorUtility.SetDirty(s);
                    sceneChanged = true;
                }
            }
        }
        if (sceneChanged) { EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); }
    }
}
