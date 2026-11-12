using UnityEditor;
using UnityEngine;
using ACO;

/// SHADE 기체 모델(Tools/aco_shade_model.py — 로우폴리 Su-57을 다듬은 사본: 새 UV + 2048 텍스처, 검정 바탕·노란 강조).
/// Setup Check(shade-model-1): 재질(몸체·유리)을 만들고 FBX 재질을 그것으로 잇고, NPC_Shade의 Model 아래 Su-57 인스턴스 자리에 새 모델을 넣는다
/// (원본 메시와 같은 월드 자리·크기 — 히트박스·코브라 발사 자리 그대로). 예전 Su-57은 지우지 않고 꺼 둔다. 다른 손본 값은 그대로.
public static class ACO_ShadeModel
{
    private const string Dir = "Assets/ACO_Addon/Models/Shade/";
    private const string Fbx = Dir + "ACO_Shade.fbx";
    private const string Tex = Dir + "ACO_Shade_Albedo.png";
    private const string ShadePath = "Assets/ACO_Addon/Prefabs/NPC_Shade.prefab";
    private const string NewName = "ACO_Shade";
    private const string OldMarker = "Su57";

    /// 재질(이미 있으면 손본 값 그대로 둔다).
    private static Material EnsureMaterial(string name, Texture2D tex, float metallic, float smoothness)
    {
        string path = Dir + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) { return m; }
        m = new Material(Shader.Find("Standard")) { name = name };
        m.SetTexture("_MainTex", tex);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// FBX 가져오기: 노멀은 파일 그대로(부드럽게·날카롭게 나눈 것), 재질은 만든 재질로.
    private static void SetupImport(Material body, Material glass)
    {
        var ti = AssetImporter.GetAtPath(Tex) as TextureImporter;
        if (ti != null && ti.maxTextureSize != 2048)
        {
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
        }
        var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
        if (mi == null) { return; }
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.importNormals = ModelImporterNormals.Import;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "ACO_Shade_Body"), body);
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "ACO_Shade_Glass"), glass);
        mi.SaveAndReimport();
    }

    /// Setup Check(shade-model-1).
    public static void Apply()
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex);
        if (fbx == null || tex == null)
        {
            Debug.LogError($"[ACO] SHADE 모델이 없습니다: {Fbx} — Tools/aco_shade_model.py를 Blender로 먼저 돌리세요.");
            return;
        }
        Material body = EnsureMaterial("ACO_Shade_Body", tex, 0.3f, 0.35f);
        Material glass = EnsureMaterial("ACO_Shade_Glass", tex, 0.6f, 0.92f);
        SetupImport(body, glass);
        fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ShadePath) == null) { return; }

        GameObject root = PrefabUtility.LoadPrefabContents(ShadePath);
        try
        {
            int placed = 0;
            foreach (ACO_ShadeAce shade in root.GetComponentsInChildren<ACO_ShadeAce>(true))
            {
                Transform model = shade.Model;
                if (model == null) { continue; }
                if (model.Find(NewName) != null) { continue; }   // 이미 넣었다
                Transform old = FindOld(model);
                MeshRenderer oldR = old != null ? old.GetComponentInChildren<MeshRenderer>(true) : null;
                if (oldR == null)
                {
                    Debug.LogWarning($"[ACO] NPC_Shade '{shade.name}': Model 아래 Su-57 모델을 찾지 못해 새 모델을 넣지 않았습니다 — 손으로 넣으세요({Fbx}).", shade);
                    continue;
                }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx, model);
                inst.name = NewName;
                Transform t = inst.transform;
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                MeshRenderer newR = inst.GetComponentInChildren<MeshRenderer>(true);
                // 새 메시가 예전 메시와 같은 월드 자리에 오게(같은 내보내기 축 — 예전 Su-57 프리팹의 위치·회전·크기 덮어쓰기까지 따른다)
                Matrix4x4 rel = t.worldToLocalMatrix * newR.transform.localToWorldMatrix;
                Matrix4x4 world = oldR.transform.localToWorldMatrix * rel.inverse;
                t.SetPositionAndRotation(world.GetColumn(3), world.rotation);
                Vector3 ws = world.lossyScale;
                Vector3 ps = model.lossyScale;
                t.localScale = new Vector3(ws.x / ps.x, ws.y / ps.y, ws.z / ps.z);
                // 파일 단위가 달라 크기가 어긋나면 크기·가운데를 예전 메시에 맞춘다
                Bounds ob = oldR.bounds;
                Bounds nb = newR.bounds;
                float ratio = nb.size.magnitude > 0.001f ? ob.size.magnitude / nb.size.magnitude : 1f;
                if (Mathf.Abs(ratio - 1f) > 0.03f)
                {
                    t.localScale *= ratio;
                    t.position += ob.center - newR.bounds.center;
                }
                old.gameObject.SetActive(false);   // 지우지 않고 꺼 둔다(되돌리기 쉽게)
                placed++;
                Debug.Log($"[ACO] NPC_Shade '{shade.name}': 새 모델({NewName})을 넣고 예전 '{old.name}'을 껐습니다. 크기 비 {ratio:0.000}, 가운데 차 {(ob.center - newR.bounds.center).magnitude:0.00}m.");
            }
            if (placed > 0)
            {
                ACO_AceWreck.Rebuild(root, "NPC_Shade");   // 추락 파티클이 있으면 새 모델로 다시 굽는다
                PrefabUtility.SaveAsPrefabAsset(root, ShadePath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// Model 아래 예전 Su-57 인스턴스(켜져 있는 것).
    private static Transform FindOld(Transform model)
    {
        for (int i = 0; i < model.childCount; i++)
        {
            Transform c = model.GetChild(i);
            if (!c.gameObject.activeSelf) { continue; }
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(c.gameObject);
            if ((!string.IsNullOrEmpty(path) && path.Contains(OldMarker)) || c.name.Contains(OldMarker)) { return c; }
        }
        return null;
    }
}
