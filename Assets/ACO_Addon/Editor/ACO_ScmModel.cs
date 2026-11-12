using System;
using UnityEditor;
using UnityEngine;

/// SCM 탄체 모델: Models/SCM/ACO_SCM_Flight.fbx(날개를 편 자세). Tools/aco_scm_missile.py가 만든다.
/// 파일런 외형(ACO_SCM.fbx)은 Pit-bool 기체 프리팹에 직접 놓는다. 재질은 둘이 같이 쓴다.
public static partial class ACO_PrefabBuilder
{
    private const string ScmFlightFbx = "Assets/ACO_Addon/Models/SCM/ACO_SCM_Flight.fbx";
    private const string ScmAlbedo = "Assets/ACO_Addon/Models/SCM/ACO_SCM_Albedo.png";
    private const string ScmFlightName = "ACO_SCM_Flight";

    /// Setup Check: 탄체 프리팹의 모델을 비행용으로 바꾼다.
    public static void InstallScmModel()
    {
        string path = PathOf("ACO_Missile_SCM");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        EditLancePrefab(path, ApplyScmModel);
        Debug.Log("[ACO] SCM 탄체를 날개를 편 새 모델로 바꿨습니다.");
    }

    private static Material ScmMaterial()
    {
        var importer = AssetImporter.GetAtPath(ScmAlbedo) as TextureImporter;
        if (importer != null && importer.maxTextureSize != 4096)
        {
            importer.maxTextureSize = 4096;   // 표기가 작아서 줄이면 뭉개진다
            importer.SaveAndReimport();
        }
        string path = $"{RailgunGeneratedDir}/ACO_SCM.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_SCM" };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ScmAlbedo));
        m.SetColor("_Color", Color.white);
        m.SetFloat("_Metallic", 0.15f);
        m.SetFloat("_Glossiness", 0.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 예전 모델은 지우지 않고 끈다. 여러 번 불러도 결과가 같다.
    private static void ApplyScmModel(GameObject root)
    {
        ConfigureModelImporter(ScmFlightFbx);
        Mesh mesh = FbxMesh(ScmFlightFbx, ScmFlightName);
        Bounds b = mesh.bounds;
        if (b.max.z < 2.0f || b.max.z > 2.5f || b.size.x < 3f)
        {
            throw new InvalidOperationException($"[ACO] ACO_SCM_Flight.fbx의 축이나 크기가 예상과 다릅니다(범위 {b.min} ~ {b.max}). 프리팹은 고치지 않았습니다.");
        }
        Transform holder = LancePart(root.transform, "Model/Mesh");
        foreach (Transform child in holder)
        {
            if (child.name != ScmFlightName && child.GetComponentInChildren<Renderer>(true) != null && child.GetComponentInChildren<ParticleSystem>(true) == null)
            {
                child.gameObject.SetActive(false);
            }
        }
        Transform model = holder.Find(ScmFlightName);
        if (model == null)
        {
            var go = new GameObject(ScmFlightName);
            go.layer = root.layer;
            go.transform.SetParent(holder, false);
            model = go.transform;
        }
        SetHolderModel(model, mesh, ScmMaterial());
    }
}
