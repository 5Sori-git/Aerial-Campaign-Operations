using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// F/A-18F(2인승) 플레이어 기체(2026-11-21 사용자 "F형 프리팹을 만들어줘, 3 · 4번기를 F형으로, 데칼 · 커스터마이즈 · 무장도 반영"):
/// ACO E형 프리팹(FA-18E Main_ACO)의 **프리팹 변형** — 무장 · HUD · 데칼 · 마킹 · 튜닝은 E에서 물려받고(E를 고치면 F도 따라간다), F 차이만 덮어쓴다.
///  - 메시: Pit-bool F 원본과 같은 자리의 스킨 메시를 F 메시로(동체 · 캐노피 · 조종석 · 착륙장치 · 좌석). 동체 · 캐노피는 E처럼 가늘게 · 볼트(ACO_CanopyThin.ApplyF).
///  - 재질: Pit-bool FA-18E 몸체 재질 → FA-18F. 조종석은 ACO 조종석 재질(cockpit_ACO) 그대로.
///  - 승객석: 켜고 F 원본의 뒷자리 위치로. 뒷자리 좌석 모델(PasengerSeat)을 F 원본에서 가져온다(본은 이름으로 다시 잇는다).
///  - 뒷자리 화면(CoPilotScreen · 카메라)은 넣지 않는다(카메라 비용).
/// 원본 FBX · Pit-bool 원본 프리팹은 건드리지 않는다.
public static class ACO_FVariantBuilder
{
    public const string BasePath = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
    public const string FSourcePath = "Assets/Pit-bool/FA-18/prefab/FA-18F Main.prefab";
    public const string VariantPath = "Assets/Pit-bool/FA-18F Main_ACO.prefab";
    private const string EBodyMat = "Assets/Pit-bool/FA-18/material/E/FA-18E.mat";
    private const string FBodyMat = "Assets/Pit-bool/FA-18/material/F/FA-18F.mat";

    [MenuItem("ACO/FA-18F/Build Variant")]
    public static void BuildVariant()
    {
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
        GameObject fSrc = AssetDatabase.LoadAssetAtPath<GameObject>(FSourcePath);
        if (basePrefab == null || fSrc == null) { Debug.LogError("[ACO] F형: E형 ACO 프리팹이나 Pit-bool F 원본이 없습니다."); return; }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        GameObject f = Object.Instantiate(fSrc);
        f.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            inst.name = "FA-18F Main_ACO";
            // 모델 루트 = SaccEntity(ACO E형은 F/A-18E, Pit-bool F 원본은 FA-18) — 그 아래 경로가 같다
            var entity = inst.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true);
            var fEntity = f.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true);
            if (entity == null || fEntity == null) { Debug.LogError("[ACO] F형: SaccEntity(모델 루트)를 찾지 못했습니다."); return; }
            Transform eModel = entity.transform;
            Transform fModel = fEntity.transform;
            // 모델 루트 이름(F/A-18E)은 바꾸지 않는다 — 씬 기체를 F로 바꿀 때 계층 이름으로 맞춰 참조 · 덮어쓰기가 그대로 남는다

            // 1) 메시: 같은 경로의 렌더러끼리
            int meshes = 0;
            foreach (Renderer rf in fModel.GetComponentsInChildren<Renderer>(true))
            {
                string path = PathOf(rf.transform, fModel);
                Transform te = eModel.Find(path);
                if (te == null) { continue; }
                if (rf is SkinnedMeshRenderer sf && te.TryGetComponent(out SkinnedMeshRenderer se))
                {
                    if (se.sharedMesh != sf.sharedMesh) { se.sharedMesh = sf.sharedMesh; meshes++; }
                }
                else if (rf.TryGetComponent(out MeshFilter mf) && te.TryGetComponent(out MeshFilter me))
                {
                    if (me.sharedMesh != mf.sharedMesh) { me.sharedMesh = mf.sharedMesh; meshes++; }
                }
            }
            // 2) 몸체 재질 E → F
            Material eMat = AssetDatabase.LoadAssetAtPath<Material>(EBodyMat), fMat = AssetDatabase.LoadAssetAtPath<Material>(FBodyMat);
            int mats = 0;
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                Material[] m = r.sharedMaterials;
                bool ch = false;
                for (int i = 0; i < m.Length; i++) { if (m[i] == eMat) { m[i] = fMat; ch = true; } }
                if (ch) { r.sharedMaterials = m; mats++; }
            }
            // 3) 캐노피 앞 프레임 · 아치 · 볼트(E와 같이)
            ACO_CanopyThin.ApplyF(inst);
            // 4) 승객석: 켜고 F 뒷자리 위치로
            Transform ps = Find(eModel, "PassengerSeat"), psF = Find(fModel, "PassengerSeat");
            if (ps != null && psF != null)
            {
                if (ps.parent == eModel && psF.parent == fModel) { ps.localPosition = psF.localPosition; ps.localRotation = psF.localRotation; }   // 둘 다 모델 루트(SaccEntity) 바로 아래
                else { Debug.LogWarning("[ACO] F형: 승객석 부모가 달라 위치를 옮기지 않았습니다 — 확인하세요."); }
                ps.gameObject.SetActive(true);
            }
            // 5) 뒷자리 좌석 모델
            Transform rear = Find(fModel, "PasengerSeat");
            Transform armF = rear != null ? rear.parent : null;
            Transform armE = armF != null ? eModel.Find(PathOf(armF, fModel)) : null;
            if (rear != null && armE != null && armE.Find(rear.name) == null)
            {
                GameObject copy = Object.Instantiate(rear.gameObject, armE, false);
                copy.name = rear.name;
                copy.hideFlags = HideFlags.None;
                Rebind(copy, eModel);
            }
            PrefabUtility.SaveAsPrefabAsset(inst, VariantPath);
            Debug.Log($"[ACO] F형 프리팹 변형을 만들었습니다: {VariantPath} — 메시 {meshes}개, 재질 {mats}곳, 승객석 켬, 뒷자리 좌석.");
        }
        finally
        {
            Object.DestroyImmediate(f);
            Object.DestroyImmediate(inst);
        }
        AssetDatabase.SaveAssets();
    }

    /// 씬의 3 · 4번기(게임 진행 Aircraft[2] · [3])를 F형 프리팹으로 바꾼다 — 프리팹 원본만 바꾸고(계층으로 맞춤) 씬의 덮어쓰기 · 참조는 그대로.
    [MenuItem("ACO/FA-18F/Use For Slots 3-4")]
    public static void UseForSlots34() { UseForSlots(new[] { 2, 3 }); }

    public static void UseForSlots(int[] slots)
    {
        GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        if (variant == null) { BuildVariant(); variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath); }
        ACO.ACO_GameFlow flow = Object.FindObjectOfType<ACO.ACO_GameFlow>(true);
        if (variant == null || flow == null || flow.Aircraft == null) { Debug.LogError("[ACO] F형: 프리팹이나 게임 진행(Aircraft)이 없습니다."); return; }
        var settings = new PrefabReplacingSettings { logInfo = true, objectMatchMode = ObjectMatchMode.ByHierarchy, prefabOverridesOptions = PrefabOverridesOptions.KeepAllPossibleOverrides };
        int n = 0;
        foreach (int s in slots)
        {
            if (s < 0 || s >= flow.Aircraft.Length || flow.Aircraft[s] == null) { continue; }
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(flow.Aircraft[s].gameObject);
            if (root == null) { Debug.LogWarning($"[ACO] F형: {s + 1}번기가 프리팹 인스턴스가 아닙니다 — 건너뜁니다."); continue; }
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == VariantPath) { continue; }
            PrefabUtility.ReplacePrefabAssetOfPrefabInstance(root, variant, settings, InteractionMode.UserAction);
            root.name = root.name.Replace("FA-18E", "FA-18F");
            n++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
        Debug.Log($"[ACO] F형: {n}대를 F형 프리팹으로 바꿨습니다. Bake 하고 씬을 저장하세요.");
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) { if (t.name == name) { return t; } }
        return null;
    }

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (Transform p = t; p != null && p != root; p = p.parent) { parts.Insert(0, p.name); }
        return string.Join("/", parts);
    }

    /// 복사한 스킨 메시의 본 · 루트 본을 대상 기체의 같은 이름 본으로 다시 잇는다.
    private static void Rebind(GameObject copy, Transform root)
    {
        var byName = new Dictionary<string, Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) { if (!byName.ContainsKey(t.name)) { byName[t.name] = t; } }
        foreach (SkinnedMeshRenderer smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Transform[] bones = smr.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && byName.TryGetValue(bones[i].name, out Transform b)) { bones[i] = b; }
            }
            smr.bones = bones;
            if (smr.rootBone != null && byName.TryGetValue(smr.rootBone.name, out Transform rb)) { smr.rootBone = rb; }
        }
    }
}
