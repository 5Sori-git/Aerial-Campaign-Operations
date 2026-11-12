using System;
using UnityEditor;
using UnityEngine;
using ACO;
using Object = UnityEngine.Object;

/// TEL 모델: Models/TEL/ACO_TEL.fbx. Tools/aco_tel_model.py가 만든다. 아래 치수는 그 파일의 PIVOT, RAM_BASE, RAM_TOP, CRADLE_Y, MISSILE_TAIL과 같아야 한다.
public static partial class ACO_PrefabBuilder
{
    private const string TelFbx = "Assets/ACO_Addon/Models/TEL/ACO_TEL.fbx";
    private const string TelAlbedo = "Assets/ACO_Addon/Models/TEL/ACO_TEL_Albedo.png";
    private const float TelErectAngle = 90f;
    private const float TelMissileHeight = 15f;
    private const float TelRamMiddleLength = 2.4f;
    private static readonly Vector3 TelPivot = new Vector3(0f, 1.9f, -8.6f);
    private static readonly Vector3 TelRamBase = new Vector3(0f, 2.55f, -3.6f);
    private static readonly Vector3 TelRamTop = new Vector3(0f, 1.2f, 2.6f);
    private static readonly Vector3 TelMissileOnRail = new Vector3(0f, 2.2f, 0.4f);
    private static readonly Vector3 TelFlyingTail = new Vector3(0f, 2.3f, -10.8f);   // 다 일어선 이렉터의 탄도탄 꼬리(TelPivot + 90도 돌린 TelMissileOnRail)

    /// Setup Check: 예전 모델을 끄고 새 모델을 넣는다. 판정, 표적 마커, 불꽃을 탄도탄이 서는 자리로 옮긴다.
    public static void InstallTelModel()
    {
        string path = PathOf("NPC_TEL");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        EditLancePrefab(path, ApplyTelModel);
        Debug.Log("[ACO] TEL을 새 모델로 바꿨습니다.");
    }

    private static Material TelMaterial()
    {
        var importer = AssetImporter.GetAtPath(TelAlbedo) as TextureImporter;
        if (importer != null && importer.maxTextureSize != 2048)
        {
            importer.maxTextureSize = 2048;   // 멀리서 보는 유닛이다
            importer.SaveAndReimport();
        }
        string path = $"{RailgunGeneratedDir}/ACO_TEL.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_TEL" };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TelAlbedo));
        m.SetColor("_Color", Color.white);
        m.SetFloat("_Metallic", 0.1f);
        m.SetFloat("_Glossiness", 0.32f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 예전 모델은 지우지 않고 끈다. 여러 번 불러도 결과가 같다.
    private static void ApplyTelModel(GameObject root)
    {
        ConfigureModelImporter(TelFbx);
        Mesh missile = FbxMesh(TelFbx, "TEL_Missile");
        Bounds b = missile.bounds;
        if (b.max.z < TelMissileHeight - 0.5f || b.max.z > TelMissileHeight + 0.5f || b.min.z < -0.5f)
        {
            throw new InvalidOperationException($"[ACO] ACO_TEL.fbx의 축이나 크기가 예상과 다릅니다(범위 {b.min} ~ {b.max}).");
        }
        ACO_TelUnit tel = root.GetComponent<ACO_TelUnit>();
        Material mat = TelMaterial();
        Transform chassis = tel.Chassis;
        Transform rail = tel.Rail;
        Transform onRail = tel.RailMissile.transform;
        Transform flying = tel.FlyingMissile.transform;

        HideOldModel(chassis, rail);
        HideOldModel(rail, onRail);
        HideOldModel(onRail, null);
        HideOldModel(flying, null);

        rail.localPosition = TelPivot;
        rail.localRotation = Quaternion.identity;
        onRail.localPosition = Vector3.zero;
        onRail.localRotation = Quaternion.identity;
        TelPart(chassis, "TEL_Chassis", mat, Vector3.zero, Quaternion.identity);
        TelPart(rail, "TEL_Erector", mat, Vector3.zero, Quaternion.identity);
        TelPart(onRail, "TEL_Missile", mat, TelMissileOnRail, Quaternion.identity);
        Quaternion upright = Quaternion.Euler(-90f, 0f, 0f);   // 모델의 +Z(기수)가 위를 본다
        TelPart(flying, "TEL_Missile", mat, TelFlyingTail, upright);

        Transform cylinder = TelPart(chassis, "TEL_RamCylinder", mat, TelRamBase, Quaternion.identity);
        Transform middle = TelPart(chassis, "TEL_RamMiddle", mat, TelRamBase, Quaternion.identity);
        Transform rod = TelPart(rail, "TEL_RamRod", mat, TelRamTop, Quaternion.identity);
        // 누운 자세로 맞춰 둔다. 게임에서는 ACO_TelUnit이 매 프레임 맞춘다
        Vector3 top = TelPivot + TelRamTop;
        Vector3 along = top - TelRamBase;
        Quaternion aim = Quaternion.LookRotation(along, Vector3.up);
        cylinder.localRotation = aim;
        middle.localRotation = aim;
        middle.localPosition = (TelRamBase + top) * 0.5f - along.normalized * (TelRamMiddleLength * 0.5f);
        rod.localRotation = Quaternion.LookRotation(-along, Vector3.up);

        tel.RailErectAngle = TelErectAngle;
        tel.RamCylinder = cylinder;
        tel.RamMiddle = middle;
        tel.RamRod = rod;
        tel.RamMiddleLength = TelRamMiddleLength;
        EditorUtility.SetDirty(tel);

        // 탄도탄이 서는 자리(차체 뒤)로 판정, 마커, 불꽃, 공중 폭발을 옮긴다
        Vector3 center = TelFlyingTail + Vector3.up * (TelMissileHeight * 0.5f);
        if (tel.MissileHitbox is BoxCollider box)
        {
            box.center = center;
            box.size = new Vector3(3.6f, TelMissileHeight + 1f, 3.6f);
            EditorUtility.SetDirty(box);
        }
        if (tel.AirMarker != null) { tel.AirMarker.transform.localPosition = center; }
        if (tel.AirBlastEffect != null) { tel.AirBlastEffect.transform.localPosition = center; }
        Transform trail = flying.Find("Trail");
        Transform engine = flying.Find("Effects");
        if (trail != null && engine != null)
        {
            Vector3 toEngine = engine.localPosition - trail.localPosition;   // 불꽃과 궤적 사이 간격은 손으로 맞춘 값을 지킨다
            trail.localPosition = TelFlyingTail;
            engine.localPosition = TelFlyingTail + toEngine;
        }
    }

    /// 렌더러가 있는 자식을 끈다. keep과 새 모델(TEL_), 파티클이 든 자식은 그대로 둔다.
    private static void HideOldModel(Transform parent, Transform keep)
    {
        foreach (Transform child in parent)
        {
            if (child == keep || child.name.StartsWith("TEL_")) { continue; }
            if (child.GetComponentInChildren<Renderer>(true) != null && child.GetComponentInChildren<ParticleSystem>(true) == null)
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static Transform TelPart(Transform parent, string name, Material mat, Vector3 position, Quaternion rotation)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        SetHolderModel(t, FbxMesh(TelFbx, name), mat);
        t.localPosition = position;
        t.localRotation = rotation;
        return t;
    }
}
