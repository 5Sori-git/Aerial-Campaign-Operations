using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ACO;
using Object = UnityEngine.Object;

/// 공중 요새·랜스 포대가 함께 쓰는 포대 모델(Flak, CIWS, VLS): Models/Lance/ACO_Turrets.fbx. Tools/aco_lance_model.py가 만든다.
/// 포대 프리팹은 두 보스 안에 중첩되어 있어 다시 만들지 않고 제자리에서 고친다. 메시 크기는 프리팹 로컬 크기다.
public static partial class ACO_PrefabBuilder
{
    private const string TurretFbx = "Assets/ACO_Addon/Models/Lance/ACO_Turrets.fbx";
    private const float FlakYawHeight = 3.2f;
    private const float FlakMuzzle = 10.1f;
    private const float CiwsYawHeight = 2f;
    private const float CiwsMuzzle = 3.45f;
    private static readonly Vector3 FlakHitSize = new Vector3(6f, 4.8f, 7.4f);
    private static readonly Vector3 CiwsHitSize = new Vector3(2.4f, 2.8f, 2.4f);

    // VLS 덮개 경첩: 셀 바깥쪽 모서리, 윗면 높이
    private const float VlsCell = 1.2f;
    private const float VlsHinge = 2.12f;
    private const float VlsDeck = 1.2f;

    private class FortressSite
    {
        public readonly string Name;
        public readonly string Prefab;
        public readonly Vector3 Model;   // 모델 좌표. FortressPoint로 바꾼다
        public readonly Vector3 Up;      // 요새 로컬 좌표의 표면 법선. 포대는 이 방향으로 기운다
        public FortressSite(string name, string prefab, float x, float y, float z, float upX, float upY, float upZ)
        {
            Name = name;
            Prefab = prefab;
            Model = new Vector3(x, y, z);
            Up = new Vector3(upX, upY, upZ).normalized;
        }
    }

    /// 공중 요새 포대 18곳: Flak 4, CIWS 8, VLS 6. 좌우 대칭이고, 선체 경사에 맞춰 기울인다(DESIGN 6-101).
    private static readonly FortressSite[] FortressSites =
    {
        new FortressSite("CIWS_Belly_0", TurretCiws, 245.8f, -38.0f, 1.7f, 0.000f, -0.999f, 0.037f),
        new FortressSite("CIWS_Belly_1", TurretCiws, -68.2f, -56.9f, 1.7f, 0.000f, -1.000f, 0.020f),
        new FortressSite("CIWS_Rear_0", TurretCiws, -596.2f, 61.5f, -27.0f, 0.003f, 1.000f, 0.017f),
        new FortressSite("CIWS_Rear_1", TurretCiws, -596.2f, 61.5f, 30.4f, -0.003f, 1.000f, 0.017f),
        new FortressSite("CIWS_Top_0", TurretCiws, 269.8f, 37.7f, -17.0f, 0.041f, 0.999f, 0.022f),
        new FortressSite("CIWS_Top_1", TurretCiws, 269.8f, 37.7f, 20.4f, -0.041f, 0.999f, 0.022f),
        new FortressSite("CIWS_Top_2", TurretCiws, -264.2f, 32.9f, -169.0f, -0.035f, 0.998f, 0.057f),
        new FortressSite("CIWS_Top_3", TurretCiws, -264.2f, 32.9f, 172.4f, 0.035f, 0.998f, 0.057f),
        new FortressSite("Flak_Belly_2", TurretFlak, -330.2f, -45.8f, -116.0f, -0.181f, -0.944f, 0.276f),
        new FortressSite("Flak_Belly_3", TurretFlak, -330.2f, -45.8f, 119.4f, 0.181f, -0.944f, 0.276f),
        new FortressSite("Flak_Top_2", TurretFlak, 115.8f, 39.7f, -44.0f, 0.067f, 0.998f, 0.019f),
        new FortressSite("Flak_Top_3", TurretFlak, 115.8f, 39.7f, 47.4f, -0.067f, 0.998f, 0.019f),
        new FortressSite("VLS_0", TurretVls, -56.2f, 39.4f, -61.0f, 0.101f, 0.995f, -0.010f),
        new FortressSite("VLS_1", TurretVls, -56.2f, 39.4f, 64.4f, -0.101f, 0.995f, -0.010f),
        new FortressSite("VLS_2", TurretVls, -304.2f, 42.1f, -59.0f, 0.075f, 0.997f, 0.028f),
        new FortressSite("VLS_3", TurretVls, -304.2f, 42.1f, 62.4f, -0.075f, 0.997f, 0.028f),
        new FortressSite("VLS_Belly_0", TurretVls, 47.8f, -47.6f, -25.0f, 0.221f, -0.972f, 0.075f),
        new FortressSite("VLS_Belly_1", TurretVls, 47.8f, -47.6f, 28.4f, -0.221f, -0.972f, 0.075f),
    };

    private const float FortressGunScale = 3f;
    private const float FortressVlsScale = 3.5f;

    /// Setup Check: 공중 요새 포대를 표에 맞춘다. 표에 없는 포대는 빼고, 새 포대는 더한다. 격파 목록도 다시 만든다.
    public static void PlaceFortressTurrets()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("NPC_Fortress")) != null) { EditLancePrefab(PathOf("NPC_Fortress"), ApplyFortressSites); }
    }

    /// 위쪽을 표면 법선에 맞추고 앞쪽은 요새 앞을 본다. 배 쪽은 법선이 아래라 저절로 뒤집힌다.
    private static Quaternion FortressSiteRotation(FortressSite site)
    {
        return Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, site.Up).normalized, site.Up);
    }

    private static float FortressSiteScale(FortressSite site)
    {
        return site.Prefab == TurretVls ? FortressVlsScale : FortressGunScale;
    }

    private static void ApplyFortressSites(GameObject root)
    {
        Transform parts = LancePart(root.transform, "Parts");
        ACO_Warship ship = root.GetComponent<ACO_Warship>();
        ACO_FlyingFortress fortress = root.GetComponent<ACO_FlyingFortress>();
        var names = new HashSet<string>();
        var defense = new List<UdonSharp.UdonSharpBehaviour>();
        foreach (FortressSite site in FortressSites)
        {
            names.Add(site.Name);
            Vector3 point = FortressPoint(site.Model.x, site.Model.y, site.Model.z);
            Transform part = parts.Find(site.Name);
            if (part == null)
            {
                part = NewTurret(site.Prefab, site.Name, parts, point, FortressSiteRotation(site), FortressSiteScale(site)).transform;
                SetAirTarget(part.gameObject);
            }
            part.localPosition = point;
            part.localRotation = FortressSiteRotation(site);
            PrefabUtility.RecordPrefabInstancePropertyModifications(part);
            ACO_AAGunSite gun = part.GetComponent<ACO_AAGunSite>();
            if (gun != null)
            {
                SetFortressPitch(gun);
                defense.Add(gun);
            }
            else { defense.Add(part.GetComponent<ACO_SAMSite>()); }
        }
        for (int i = parts.childCount - 1; i >= 0; i--)
        {
            Transform child = parts.GetChild(i);
            bool turret = child.name.StartsWith("Flak_") || child.name.StartsWith("CIWS_") || child.name.StartsWith("VLS_");
            if (turret && !names.Contains(child.name)) { Object.DestroyImmediate(child.gameObject); }
        }
        fortress.AirDefense = defense.ToArray();
        var all = new List<UdonSharp.UdonSharpBehaviour>(defense);
        all.AddRange(fortress.Engines);
        ship.Parts = all.ToArray();
        PrefabUtility.RecordPrefabInstancePropertyModifications(fortress);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ship);
        EditorUtility.SetDirty(fortress);
        EditorUtility.SetDirty(ship);
    }

    /// 기울여 달았으므로 받침 기준으로 조준한다. 고각 범위도 받침 기준이고, 아래로는 포신이 선체에 닿기 전까지만 내린다.
    private static void SetFortressPitch(ACO_AAGunSite gun)
    {
        gun.MountRelativeAim = true;
        gun.MinPitch = -15f;
        gun.MaxPitch = 85f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(gun);
        EditorUtility.SetDirty(gun);
    }

    // 공중 요새 코어: 격납고 높이(37m)에 맞춘 세운 원자로. 크기는 모델 배율 1 기준이다
    private static readonly Vector3 FortressCoreHitSize = new Vector3(28f, 30f, 28f);
    private static readonly Vector3 FortressLaunchModel = new Vector3(93f, 2f, 103f);   // 측면 문 출구(모델 좌표, z는 대칭축에서 잰 거리)

    /// 모델 좌표 → 요새 루트 좌표. Model의 실제 자리와 크기를 따르므로 손으로 바꾼 배율에서도 맞는다.
    private static Vector3 FortressRootPoint(Transform root, float x, float y, float z)
    {
        Transform model = LancePart(root, "Model");
        return model.localPosition + model.localRotation * (new Vector3(x, y, z) * model.localScale.x);
    }

    /// Setup Check: 코어 모델, 코어 히트박스, 증원 발진 지점을 맞춘다.
    public static void PlaceFortressCore()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PathOf("NPC_Fortress")) == null) { return; }
        PrepareTurretAssets();
        EditLancePrefab(PathOf("NPC_Fortress"), ApplyFortressCore);
    }

    /// 코어는 Core 아래에 있고 히트박스는 SaccTarget 규칙상 루트에 있다. 히트박스를 보이는 코어의 실제 자리에서 다시 잰다.
    private static void ApplyFortressCore(GameObject root)
    {
        Transform t = root.transform;
        float size = LancePart(t, "Model").localScale.x;
        Transform core = LancePart(t, "Core");
        Transform sphere = LancePart(core, "CoreSphere");
        Vector3 place = sphere.localPosition;
        float scale = size / core.lossyScale.x;
        SetHolderModel(sphere, TurretMesh("Fort_Core"), LanceCoreMaterial());
        sphere.localPosition = place;
        sphere.localScale = Vector3.one * scale;
        Transform frame = ModelHolder(core, "Visual_Frame");
        SetHolderModel(frame, TurretMesh("Fort_CoreFrame"), LanceBarrelMaterial());
        frame.localPosition = place;
        frame.localScale = Vector3.one * scale;

        ACO_FlyingFortress fortress = root.GetComponent<ACO_FlyingFortress>();
        var hit = fortress.CoreHitbox as BoxCollider;
        if (hit == null) { throw new InvalidOperationException("[ACO] 공중 요새 코어 히트박스가 BoxCollider가 아닙니다."); }
        SetBox(hit, t.InverseTransformPoint(sphere.position), FortressCoreHitSize * size);

        for (int side = 1; side >= -1; side -= 2)
        {
            Vector3 m = FortressLaunchModel;
            Transform point = LancePart(t, side > 0 ? "Launch_L" : "Launch_R");
            point.localPosition = FortressRootPoint(t, m.x, m.y, ArchangelCenterZ + m.z * side);
            EditorUtility.SetDirty(point);
        }
    }

    /// Setup Check: 포대 프리팹 셋을 새 모델로 바꾼다. 오브젝트 ID를 지키므로 중첩 인스턴스와 씬 참조가 유지된다.
    public static void InstallTurretModels()
    {
        PrepareTurretAssets();
        EditTurret(TurretFlak, ApplyFlakModel);
        EditTurret(TurretCiws, ApplyCiwsModel);
        EditTurret(TurretVls, ApplyVlsModel);
        Debug.Log("[ACO] Flak, CIWS, VLS 포대를 새 모델로 바꿨습니다.");
    }

    private static void EditTurret(string name, Action<GameObject> apply)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(name)) != null) { EditLancePrefab(PathOf(name), apply); }
    }

    private static void PrepareTurretAssets()
    {
        ConfigureModelImporter(TurretFbx);
        Bounds gun = TurretMesh("Flak_Guns").bounds;
        if (gun.max.z < 9.5f || gun.max.z > 10.5f || gun.size.y > 2f)
        {
            throw new InvalidOperationException($"[ACO] ACO_Turrets.fbx의 축이나 크기가 예상과 다릅니다(포신 범위 {gun.min} ~ {gun.max}). 프리팹은 고치지 않았습니다.");
        }
    }

    private static Mesh TurretMesh(string name) { return FbxMesh(TurretFbx, name); }

    /// 예전 프리팹은 팩 모델을 담은 이름이 포대마다 달랐다. 있으면 그 오브젝트를 쓴다 — 격파 때 숨기는 목록에 이미 들어 있다.
    private static Transform ModelHolder(Transform parent, params string[] names)
    {
        foreach (string name in names)
        {
            Transform found = parent.Find(name);
            if (found != null) { return found; }
        }
        var go = new GameObject(names[0]);
        go.layer = LayerHitbox;
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// 자리 잡기용 부모 위치는 원점으로 돌리고, 팩 모델(Turret_0*)을 지운 뒤 메시를 넣는다.
    private static void SetHolderModel(Transform t, Mesh mesh, Material mat)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            Transform child = t.GetChild(i);
            if (child.name.StartsWith("Turret_0")) { Object.DestroyImmediate(child.gameObject); }
        }
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        MeshFilter filter = t.GetComponent<MeshFilter>();
        if (filter == null) { filter = t.gameObject.AddComponent<MeshFilter>(); }
        MeshRenderer renderer = t.GetComponent<MeshRenderer>();
        if (renderer == null) { renderer = t.gameObject.AddComponent<MeshRenderer>(); }
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = mat;
        renderer.enabled = true;
        EditorUtility.SetDirty(t.gameObject);
    }

    /// 받침(Model) + 포탑(TurretYaw) + 포(TurretPitch). 여러 번 불러도 결과가 같다.
    private static void ApplyGunModel(GameObject root, string baseMesh, string yawMesh, string gunMesh, float yawHeight, float muzzle, Vector3 hitSize)
    {
        PrepareTurretAssets();
        Transform t = root.transform;
        Material steel = LanceSteelMaterial();
        Material gun = LanceBarrelMaterial();
        SetHolderModel(LancePart(t, "Model"), TurretMesh(baseMesh), steel);
        Transform yaw = LancePart(t, "TurretYaw");
        yaw.localPosition = Vector3.up * yawHeight;
        SetHolderModel(ModelHolder(yaw, "YawModel", "TurrettYawModel"), TurretMesh(yawMesh), steel);
        Transform pitch = LancePart(yaw, "TurretPitch");
        pitch.localPosition = Vector3.zero;
        SetHolderModel(ModelHolder(pitch, "pitchModel", "TurretPitchModel"), TurretMesh(gunMesh), gun);
        LancePart(pitch, "Gun").localPosition = Vector3.forward * muzzle;
        LancePart(pitch, "Barrel").GetComponent<MeshRenderer>().enabled = false;
        SetBox(root.GetComponent<BoxCollider>(), new Vector3(0f, hitSize.y * 0.5f, 0f), hitSize);
    }

    private static void ApplyFlakModel(GameObject root)
    {
        ApplyGunModel(root, "Flak_Base", "Flak_House", "Flak_Guns", FlakYawHeight, FlakMuzzle, FlakHitSize);
    }

    private static void ApplyCiwsModel(GameObject root)
    {
        ApplyGunModel(root, "Ciws_Base", "Ciws_Mount", "Ciws_Gun", CiwsYawHeight, CiwsMuzzle, CiwsHitSize);
    }

    /// 몸체 + 경고띠 + 덮개 넷. 덮개는 ACO_MissileLauncher가 연다.
    private static void ApplyVlsModel(GameObject root)
    {
        PrepareTurretAssets();
        Transform t = root.transform;
        Material steel = LanceSteelMaterial();
        Material hazard = RailgunMaterial("ACO_RailgunHazard", "ACO_Railgun_Hazard", false, Color.white, 0.2f, 0.3f);
        Transform model = LancePart(t, "Model");
        SetHolderModel(model, TurretMesh("Vls_Body"), steel);
        SetHolderModel(ModelHolder(model, "Hazard"), TurretMesh("Vls_Hazard"), hazard);
        var hatches = new Transform[4];
        for (int k = 0; k < hatches.Length; k++)
        {
            int sx = k % 2 == 0 ? -1 : 1;
            int sz = k < 2 ? 1 : -1;
            Transform hatch = ModelHolder(model, "Hatch_" + k);
            SetHolderModel(hatch, TurretMesh("Vls_Hatch"), steel);
            hatch.localPosition = new Vector3(sx * VlsHinge, VlsDeck, sz * VlsCell);
            hatch.localRotation = sx < 0 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            hatches[k] = hatch;
        }
        ACO_MissileLauncher launcher = LancePart(t, "MissileLauncher").GetComponent<ACO_MissileLauncher>();
        launcher.Hatches = hatches;
        PrefabUtility.RecordPrefabInstancePropertyModifications(launcher);
        EditorUtility.SetDirty(launcher);
    }
}
