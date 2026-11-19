using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ACO;

/// CIC 도구(6-275) 씬 설치 — Setup Check가 부른다. 프리팹이 없으면 만든다. 연결은 Bake.
///  - 도구 패널(ACO_CicConsole): CIC 전술 테이블 동쪽(테이블 축 +X) 강철 테두리 위(받침이 테두리에 얹힌다 — 6-315), 바깥(+X)에 선 사람을 향한다.
///  - 고글 자리(ACO_CicGogglesStation): CIC 남쪽 벽 동쪽 책상의 첫 모니터(x 11.95) 화면을 덮는다, 방 쪽을 향한다(내부 모델 좌표 — ACO_Interior 기준).
///  - 정찰 드론(ACO_CicUav): 씬 루트에 하나(꺼진 채 — 콘솔이 켠다).
/// 자리가 어긋난 도구 패널 · 고글 자리(예전 받침대 자리 등)는 다시 놓는다.
public static class ACO_CicSetup
{
    /// Int_PlotTable 윗면 높이(m) — 전술 테이블 루트가 윗면에 있다(ACO_InteriorBuilder.TableTop)
    private const float TableTop = 0.652f;
    /// 남쪽 동쪽 책상 첫 모니터 화면 가운데 · 앞면(내부 모델 좌표: x, 책상 0.683 + 화면 아래 0.13 + 반높이 0.21, 벽 −4.9 + 책상 0.13 + 화면 0.175)
    private static readonly Vector3 MonitorFace = new Vector3(11.95f, 0.683f + 0.13f + 0.21f, -4.9f + 0.13f + 0.175f);
    private const float PlaceTolerance = 0.05f;

    public static bool Missing()
    {
        if (FindUav() == null) { return true; }
        if (Object.FindObjectOfType<ACO_CicStrike>(true) == null) { return true; }
        ACO_CicConsole c = Object.FindObjectOfType<ACO_CicConsole>(true);
        ACO_CicGogglesPanel g = Object.FindObjectOfType<ACO_CicGogglesPanel>(true);
        if (c == null || g == null) { return true; }
        Pose cp, gp;
        if (ConsolePose(out cp) && (c.transform.position - cp.position).magnitude > PlaceTolerance) { return true; }
        if (GogglesPose(out gp) && (g.transform.position - gp.position).magnitude > PlaceTolerance) { return true; }
        return false;
    }

    private static GameObject FindUav()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name.StartsWith(ACO_HUDBuilder.CicUavName)) { return root; }
        }
        return null;
    }

    /// CIC 전술 테이블: 함내 격실 안에 있는 작전 테이블(없으면 첫 테이블).
    private static ACO_TacticalMap CicTable()
    {
        ACO_InteriorZone zone = Object.FindObjectOfType<ACO_InteriorZone>(true);
        ACO_TacticalMap first = null;
        foreach (ACO_TacticalMap m in Object.FindObjectsOfType<ACO_TacticalMap>(true))
        {
            if (first == null) { first = m; }
            if (zone != null && zone.Boxes != null && zone.Contains(m.transform.position)) { return m; }
        }
        return first;
    }

    private static bool ConsolePose(out Pose pose)
    {
        pose = default(Pose);
        ACO_TacticalMap table = CicTable();
        if (table == null) { return false; }
        Transform t = table.transform;
        Vector3 east = t.rotation * Vector3.right;
        east.y = 0f;
        east = east.normalized;
        pose = new Pose(t.position + east * ACO_HUDBuilder.CicRimDistance - Vector3.up * ACO_HUDBuilder.CicRimDrop, Quaternion.LookRotation(east, Vector3.up));   // +Z = 테이블 바깥(사용자 쪽)
        return true;
    }

    private static bool GogglesPose(out Pose pose)
    {
        pose = default(Pose);
        GameObject interior = GameObject.Find(ACO_InteriorBuilder.PrefabName);
        if (interior == null) { return false; }
        Transform t = interior.transform;
        pose = new Pose(t.TransformPoint(MonitorFace), t.rotation);   // 내부 모델 +Z = 방 쪽(북)
        return true;
    }

    public static void Install()
    {
        string consolePath = $"Assets/ACO_Addon/Prefabs/{ACO_HUDBuilder.CicConsoleName}.prefab";
        string gogglesPath = $"Assets/ACO_Addon/Prefabs/{ACO_HUDBuilder.CicGogglesName}.prefab";
        string uavPath = $"Assets/ACO_Addon/Prefabs/{ACO_HUDBuilder.CicUavName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(consolePath) == null) { ACO_HUDBuilder.CreateCicConsolePrefab(); }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(gogglesPath) == null) { ACO_HUDBuilder.CreateCicGogglesPrefab(); }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(uavPath) == null) { ACO_HUDBuilder.CreateCicUavPrefab(); }
        string strikePath = ACO_PrefabBuilder.PathOf(ACO_PrefabBuilder.CicStrikeName);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(strikePath) == null) { ACO_PrefabBuilder.CreateCicStrikePrefab(); }

        Pose cp, gp;
        ACO_CicConsole console = Object.FindObjectOfType<ACO_CicConsole>(true);
        if (!ConsolePose(out cp)) { Debug.LogWarning("[ACO] CIC 도구: 전술 테이블이 없어 도구 패널을 놓지 못했습니다 — ACO/Interior/Fit Panels To Frames 뒤 다시 [고치기]."); }
        else { Place(console != null ? console.gameObject : null, consolePath, cp, "도구 패널(전술 테이블 동쪽 테두리)"); }

        ACO_CicGogglesPanel panel = Object.FindObjectOfType<ACO_CicGogglesPanel>(true);
        if (!GogglesPose(out gp)) { Debug.LogWarning($"[ACO] CIC 도구: 씬에 {ACO_InteriorBuilder.PrefabName}이 없어 고글 자리를 놓지 못했습니다."); }
        else { Place(panel != null ? panel.gameObject : null, gogglesPath, gp, "고글 자리(남쪽 동쪽 책상 모니터 화면)"); }

        if (FindUav() == null)
        {
            var uav = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(uavPath));
            Undo.RegisterCreatedObjectUndo(uav, "ACO CIC");
            uav.transform.position = Vector3.zero;
            uav.SetActive(false);
            Debug.Log("[ACO] 정찰 드론(ACO_CicUav)을 씬에 놓았습니다(꺼진 채 — CIC 콘솔이 켠다). Bake 하세요.", uav);
        }
        if (Object.FindObjectOfType<ACO_CicStrike>(true) == null && AssetDatabase.LoadAssetAtPath<GameObject>(strikePath) != null)
        {
            var strike = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(strikePath));
            Undo.RegisterCreatedObjectUndo(strike, "ACO CIC");
            strike.transform.position = Vector3.zero;
            Debug.Log("[ACO] CIC 순항미사일(ACO_CicStrike)을 씬 루트에 놓았습니다. Bake 하세요.", strike);
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    private static void Place(GameObject existing, string prefabPath, Pose pose, string what)
    {
        GameObject go = existing;
        if (go == null)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            Undo.RegisterCreatedObjectUndo(go, "ACO CIC");
        }
        else if ((go.transform.position - pose.position).magnitude <= PlaceTolerance) { return; }
        Undo.RecordObject(go.transform, "ACO CIC");
        go.transform.SetPositionAndRotation(pose.position, pose.rotation);
        Debug.Log($"[ACO] CIC {what}를 놓았습니다. Bake 하세요.", go);
    }
}
