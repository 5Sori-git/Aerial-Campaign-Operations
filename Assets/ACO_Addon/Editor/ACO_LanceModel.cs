using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ACO;

/// 랜스 포대 모델: Models/Lance/ACO_Lance.fbx와 Textures/Railgun/ACO_Lance_*.png. Tools/aco_lance_model.py가 만든다.
/// 프리팹 생성(BuildRailgun, BuildMiniTurret)과 Setup Check가 같은 Apply 함수를 쓴다.
/// NPC_Railgun은 씬이 안쪽 오브젝트까지 참조하므로 다시 만들지 않고 제자리에서 고친다. 새로 만들면 오브젝트 ID가 바뀌어 씬 참조가 끊긴다.
public static partial class ACO_PrefabBuilder
{
    private const string LanceFbx = "Assets/ACO_Addon/Models/Lance/ACO_Lance.fbx";
    private const string LanceSteelAlbedo = "ACO_Lance_Steel_Albedo";
    private const string LanceSteelNormal = "ACO_Lance_Steel_Normal";
    private const string LanceArmorAlbedo = "ACO_Lance_Armor_Albedo";

    // 좌우 장갑의 회전축: 포탑 받침 윗면의 바깥 모서리. 닫혔을 때 가운데 틈은 1m다
    private static readonly Vector3 LanceHingeL = new Vector3(-60.3f, 28.5f, -68f);
    private static readonly Vector3 LanceHingeR = new Vector3(60.3f, 28.5f, -68f);
    private static readonly Vector3[] LanceArmorOpen = { new Vector3(0f, 0f, 80f), new Vector3(0f, 0f, -80f) };

    private static readonly string[] LanceCoolerNames = { "Cooler_FL", "Cooler_FR", "Cooler_RL", "Cooler_RR" };
    private static readonly Vector3[] LanceCoolerPlaces =
    {
        new Vector3(-135f, 8f, 135f), new Vector3(135f, 8f, 135f), new Vector3(-135f, 8f, -135f), new Vector3(135f, 8f, -135f),
    };
    private static readonly Vector3[] LanceMiniPlaces =
    {
        new Vector3(58f, 8f, 176f), new Vector3(-58f, 8f, 176f), new Vector3(134f, 16f, 0f), new Vector3(-134f, 16f, 0f),
    };

    // 충돌은 새 윤곽에 맞춘다
    private static readonly Vector3 LanceOldBulkheadCenter = new Vector3(0f, 46f, -12f);
    private static readonly Vector3 LanceBulkheadCenter = new Vector3(0f, 50f, -12f);
    private static readonly Vector3 LanceBulkheadSize = new Vector3(118f, 44f, 10f);
    private static readonly Vector3 LanceClosedCenter = new Vector3(0f, 50.5f, -69f);
    private static readonly Vector3 LanceClosedSize = new Vector3(120f, 45f, 110f);
    private static readonly Vector3 LanceCoreHitSize = new Vector3(44f, 44f, 88f);
    private static readonly Vector3 LanceMiniHitSize = new Vector3(3.6f, 2.4f, 3.6f);

    /// Setup Check: 소형 레일건 프리팹과 NPC_Railgun을 제자리에서 새 모델로 바꾼다.
    public static void InstallLanceModel()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("NPC_Railgun")) == null) { return; }
        PrepareLanceAssets();
        ConvertRailgunMinis();
        EditLancePrefab(PathOf(TurretMini), ApplyMiniModel);
        EditLancePrefab(PathOf("NPC_Railgun"), ApplyLanceModel);
        Debug.Log("[ACO] 랜스 포대와 소형 레일건을 새 모델로 바꿨습니다.");
    }

    private static void EditLancePrefab(string path, Action<GameObject> apply)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            apply(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// FBX 불러오기 설정을 맞추고, 축과 크기가 예상대로인지 본다. 어긋나면 프리팹을 건드리기 전에 멈춘다.
    private static void PrepareLanceAssets()
    {
        ConfigureModelImporter(LanceFbx);
        Bounds barrel = LanceMesh("Lance_Barrel").bounds;
        if (barrel.max.z < 280f || barrel.max.z > 300f || barrel.size.y > 60f)
        {
            throw new InvalidOperationException($"[ACO] ACO_Lance.fbx의 축이나 크기가 예상과 다릅니다(포신 범위 {barrel.min} ~ {barrel.max}). 프리팹은 고치지 않았습니다.");
        }
    }

    /// U# 컴포넌트를 붙인 임시 오브젝트를 지운다. U#이 다음 틱에 컴포넌트의 hideFlags를 고치므로, 바로 지우면 예외가 난다.
    public static void DestroyAfterSetup(GameObject root)
    {
        root.SetActive(false);
        root.hideFlags = HideFlags.HideAndDontSave;
        EditorApplication.delayCall += () => { if (root != null) { UnityEngine.Object.DestroyImmediate(root); } };
    }

    public static void ConfigureModelImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) { throw new InvalidOperationException($"[ACO] {path}가 없습니다. Tools/aco_lance_model.py를 실행하세요."); }
        if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation || importer.meshCompression != ModelImporterMeshCompression.Off
            || !Mathf.Approximately(importer.globalScale, 1f) || !importer.useFileScale || importer.importNormals != ModelImporterNormals.Import)
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.SaveAndReimport();
        }
    }

    private static Mesh LanceMesh(string name) { return FbxMesh(LanceFbx, name); }

    private static Mesh FbxMesh(string path, string name)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Transform node = asset != null ? asset.transform.Find(name) : null;
        // 메시가 하나뿐인 FBX는 자식이 없고 모델 루트에 메시가 붙는다
        if (node == null && asset != null && asset.transform.childCount == 0) { node = asset.transform; }
        MeshFilter filter = node != null ? node.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) { throw new InvalidOperationException($"[ACO] {path}에 {name}이 없습니다."); }
        return filter.sharedMesh;
    }

    /// 텍스처 표면 재질. 값은 언제나 이 함수가 정한다 — 렌더 시안과 같은 값.
    private static Material LanceMaterial(string name, string albedo, string normal, float metallic, float smoothness)
    {
        Material m = RailgunMaterial(name, albedo, false, Color.white, metallic, smoothness);
        m.SetColor("_Color", Color.white);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        m.SetTexture("_MainTex", LoadRailgunTexture(albedo, false));
        if (normal != null)
        {
            m.SetTexture("_BumpMap", LoadRailgunTexture(normal, true));
            m.EnableKeyword("_NORMALMAP");
        }
        else
        {
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material LanceSteelMaterial() { return LanceMaterial("ACO_RailgunSteel", LanceSteelAlbedo, LanceSteelNormal, 0.3f, 0.4f); }

    private static Material LanceBarrelMaterial() { return LanceMaterial("ACO_RailgunBarrel", LanceSteelAlbedo, LanceSteelNormal, 0.5f, 0.55f); }

    private static Material LanceCoreMaterial()
    {
        Color core = new Color(1f, 0.35f, 0.08f);
        Material m = FortressMaterial("ACO_RailgunCore", core, 0f, 0.4f, core * 2.5f);
        m.SetColor("_Color", core);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", core * 2.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Transform LancePart(Transform root, string path)
    {
        Transform t = root.Find(path);
        if (t == null) { throw new InvalidOperationException($"[ACO] {root.name}에 {path}가 없습니다. 구조를 바꿨다면 경로를 맞춰야 합니다."); }
        return t;
    }

    private static void SetLanceModel(Transform t, string mesh, Material mat)
    {
        t.GetComponent<MeshFilter>().sharedMesh = LanceMesh(mesh);
        t.GetComponent<MeshRenderer>().sharedMaterial = mat;
        EditorUtility.SetDirty(t.gameObject);
    }

    private static void SetBox(BoxCollider box, Vector3 center, Vector3 size)
    {
        box.center = center;
        box.size = size;
        EditorUtility.SetDirty(box);
    }

    /// 여러 번 불러도 결과가 같다.
    private static void ApplyLanceModel(GameObject root)
    {
        PrepareLanceAssets();
        Transform t = root.transform;
        ACO_Railgun railgun = root.GetComponent<ACO_Railgun>();
        Material steel = LanceSteelMaterial();
        Material barrel = LanceBarrelMaterial();
        Material armor = LanceMaterial("ACO_RailgunArmor", LanceArmorAlbedo, null, 0.1f, 0.35f);
        Material concrete = RailgunMaterial("ACO_RailgunConcrete", "ACO_Railgun_Concrete", false, Color.white, 0f, 0.15f);
        Material hazard = RailgunMaterial("ACO_RailgunHazard", "ACO_Railgun_Hazard", false, Color.white, 0.2f, 0.3f);

        SetLanceModel(LancePart(t, "Visual_Mount"), "Lance_Mount", steel);
        Transform detail = t.Find("Visual_Base/Visual_BaseDetail");
        if (detail == null) { AddFortressMesh(LancePart(t, "Visual_Base"), "Visual_BaseDetail", LanceMesh("Lance_BaseDetail"), concrete, Vector3.zero); }
        else { SetLanceModel(detail, "Lance_BaseDetail", concrete); }
        SetLanceModel(LancePart(t, "BarrelPivot/Visual_Barrel"), "Lance_Barrel", barrel);
        SetLanceModel(LancePart(t, "BarrelPivot/ChargeStrip"), "Lance_ChargeStrip", FortressMaterial("ACO_RailgunStrip", Color.black, 0f, 0.5f, new Color(0.4f, 1.2f, 3f)));

        // 원자로: 노심과 구조물을 나눈다. 노심은 CoreSphere 자리를 그대로 쓴다
        Transform core = LancePart(t, "Core/CoreSphere");
        core.localScale = Vector3.one;
        SetLanceModel(core, "Lance_ReactorCore", LanceCoreMaterial());
        Transform frame = t.Find("Core/Visual_Coil");
        if (frame == null) { frame = LancePart(t, "Core/Visual_Frame"); }
        frame.name = "Visual_Frame";
        SetLanceModel(frame, "Lance_ReactorFrame", barrel);

        // 장갑: 뒤 장갑은 고정이다
        Transform armorL = LancePart(t, "Armor_L");
        Transform armorR = LancePart(t, "Armor_R");
        armorL.localPosition = LanceHingeL;
        armorR.localPosition = LanceHingeR;
        SetLanceModel(LancePart(armorL, "Visual"), "Lance_ClamL", armor);
        SetLanceModel(LancePart(armorR, "Visual"), "Lance_ClamR", armor);
        SetLanceModel(LancePart(t, "Armor_Rear/Visual"), "Lance_Rear", armor);
        railgun.ArmorPanels = new[] { armorL, armorR };
        railgun.ArmorOpenEuler = LanceArmorOpen;

        for (int i = 0; i < LanceCoolerNames.Length; i++)
        {
            Transform cooler = LancePart(t, "Parts/" + LanceCoolerNames[i]);
            cooler.localPosition = LanceCoolerPlaces[i];
            Transform visual = LancePart(cooler, "Visual");
            SetLanceModel(visual, "Lance_Cooler", steel);
            SetLanceModel(LancePart(visual, "Hazard"), "Lance_CoolerHazard", hazard);
        }
        for (int i = 0; i < LanceMiniPlaces.Length; i++)
        {
            Transform mini = LancePart(t, "Parts/MiniRailgun_" + i);
            mini.localPosition = LanceMiniPlaces[i];
            PrefabUtility.RecordPrefabInstancePropertyModifications(mini);
        }

        foreach (BoxCollider box in LancePart(t, "Hull").GetComponents<BoxCollider>())
        {
            if (box.center == LanceOldBulkheadCenter) { SetBox(box, LanceBulkheadCenter, LanceBulkheadSize); }
        }
        SetBox(LancePart(t, "Hull/ArmorClosed").GetComponent<BoxCollider>(), LanceClosedCenter, LanceClosedSize);
        var coreHit = railgun.CoreHitbox as BoxCollider;
        if (coreHit != null) { SetBox(coreHit, RailgunCorePos, LanceCoreHitSize); }

        PrefabUtility.RecordPrefabInstancePropertyModifications(railgun);
        EditorUtility.SetDirty(railgun);
    }

    /// 소형 레일건: 받침 + 방위 회전 포크(TurretYaw) + 고각 회전 포신(TurretPitch). 포구는 그대로다.
    private static void ApplyMiniModel(GameObject part)
    {
        PrepareLanceAssets();
        Transform t = part.transform;
        Material steel = LanceBarrelMaterial();
        Transform model = LancePart(t, "Model");
        model.localPosition = Vector3.zero;
        model.localScale = Vector3.one;
        SetLanceModel(model, "Lance_MiniBase", steel);
        Transform yaw = LancePart(t, "TurretYaw");
        Transform yoke = yaw.Find("Yoke");
        if (yoke == null) { AddFortressMesh(yaw, "Yoke", LanceMesh("Lance_MiniYoke"), steel, Vector3.zero); }
        else { SetLanceModel(yoke, "Lance_MiniYoke", steel); }
        Transform barrel = LancePart(yaw, "TurretPitch/Barrel");
        barrel.localPosition = Vector3.zero;
        barrel.localScale = Vector3.one;
        SetLanceModel(barrel, "Lance_MiniGun", steel);
        SetBox(part.GetComponent<BoxCollider>(), new Vector3(0f, LanceMiniHitSize.y * 0.5f, 0f), LanceMiniHitSize);
    }
}
