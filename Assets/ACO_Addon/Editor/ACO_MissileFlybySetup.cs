using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ACO;

/// 적 미사일이 스쳐 지나가는 소리(ACO_Missile.FlybySounds) 설치. 소리는 Tools/aco_make_flyby.py가 만든 Audio/Generated/ACO_Missile_Flyby_1·2.
///  - 메뉴 ACO → Weapons → Add Missile Flyby Sounds: Prefabs 폴더의 미사일 프리팹(ACO_Missile)마다 자식 "FlybySound_1·2"(3D, 도플러 0)를 붙인다.
///    이미 있으면 건너뛴다. 플레이어 무장도 같은 미사일 프리팹을 쓰지만 소리는 나를 락온해 쏜 적 미사일이 빗나갔을 때만 난다.
///  - 프리팹 빌더가 새로 만드는 미사일에도 붙인다(AddTo).
public static class ACO_MissileFlybySetup
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    private static readonly string[] ClipPaths =
    {
        "Assets/ACO_Addon/Audio/Generated/ACO_Missile_Flyby_1.wav",
        "Assets/ACO_Addon/Audio/Generated/ACO_Missile_Flyby_2.wav",
    };

    [MenuItem("ACO/Weapons/Add Missile Flyby Sounds")]
    public static void AddToPrefabs()
    {
        int done = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            ACO_Missile m = asset != null ? asset.GetComponent<ACO_Missile>() : null;
            if (m == null || (m.FlybySounds != null && m.FlybySounds.Length > 0)) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (AddTo(root.GetComponent<ACO_Missile>()) > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); done++; }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ACO] 미사일 프리팹 {done}개에 스쳐 지나가는 소리를 붙였습니다(나를 락온해 쏜 적 미사일이 빗나가 나를 놓친 순간 150m 안이면 난다).");
    }

    /// 미사일에 스쳐 지나가는 소리 칸을 만든다(소리 파일이 있는 만큼). 붙인 수.
    public static int AddTo(ACO_Missile missile)
    {
        if (missile == null) { return 0; }
        var sources = new List<AudioSource>();
        for (int i = 0; i < ClipPaths.Length; i++)
        {
            AssetDatabase.ImportAsset(ClipPaths[i]);   // 방금 만든 파일이면 아직 가져오지 않았다
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPaths[i]);
            if (clip == null) { continue; }
            var go = new GameObject("FlybySound_" + (i + 1));
            go.transform.SetParent(missile.transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 1f;
            s.dopplerLevel = 0f;          // 도플러는 소리에 들어 있다(두 번 걸리지 않게)
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 25f;
            s.maxDistance = 600f;
            s.volume = 1f;
            s.priority = 64;
            sources.Add(s);
        }
        if (sources.Count == 0)
        {
            Debug.LogWarning("[ACO] 스쳐 지나가는 소리 파일이 없습니다 — python Tools/aco_make_flyby.py로 만드세요.");
            return 0;
        }
        missile.FlybySounds = sources.ToArray();
        EditorUtility.SetDirty(missile);
        return sources.Count;
    }
}
