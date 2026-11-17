using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 도시 차량 NPC_CityVehicle(6-270 시가지 정밀 타격): 모델은 Models/CityVehicle/ACO_CityVehicles.fbx(Tools/aco_city_vehicle_model.py).
/// 루트(SaccTarget · 히트박스 · 락온 막기 ACO_LockGate · ACO_CityVehicle) / Models(변형 11개 — 하나만 켠다) / AAMTarget(지상 적 층) / DeathFx(지상 파괴 효과).
/// 생성기 지상 풀에 CityVehiclePoolSize대 넣는다(Setup Check · Bake).
public static partial class ACO_PrefabBuilder
{
    public const string CityVehiclePrefab = "NPC_CityVehicle";
    private const string CityVehicleFbx = "Assets/ACO_Addon/Models/CityVehicle/ACO_CityVehicles.fbx";
    private const string CityVehicleAlbedo = "Assets/ACO_Addon/Models/CityVehicle/ACO_CityVehicles_Albedo.png";
    /// Variants 순서 = 모델 스크립트 VARIANTS 순서(앞 9개 민간, 뒤 2개 군용 — ACO_CityVehicle.VARIANT_COUNT_CIVILIAN).
    public static readonly string[] CityVehicleMeshes =
    {
        "CIV_Sedan_Red", "CIV_Sedan_White", "CIV_Sedan_Blue", "CIV_Sedan_Silver",
        "CIV_Van_White", "CIV_Van_Yellow", "CIV_Van_Green",
        "CIV_Bus_Blue", "CIV_Bus_Orange",
        "MIL_Command", "MIL_Comms",
    };

    private static Material CityVehicleMaterial()
    {
        var importer = AssetImporter.GetAtPath(CityVehicleAlbedo) as TextureImporter;
        if (importer != null && (importer.filterMode != FilterMode.Point || importer.mipmapEnabled))
        {
            importer.filterMode = FilterMode.Point;   // 색 칸 텍스처 — 칸 사이가 번지지 않게
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        string path = $"{RailgunGeneratedDir}/ACO_CityVehicles.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_CityVehicles" };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(CityVehicleAlbedo));
        m.SetColor("_Color", Color.white);
        m.SetFloat("_Metallic", 0.15f);
        m.SetFloat("_Glossiness", 0.45f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material CityVehicleBurntMaterial()
    {
        string path = $"{RailgunGeneratedDir}/ACO_CityVehicles_Burnt.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_CityVehicles_Burnt" };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_Color", new Color(0.07f, 0.065f, 0.06f));
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", 0.08f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    public static GameObject BuildCityVehicle()
    {
        EnsurePrefabFolder();
        ConfigureModelImporter(CityVehicleFbx);
        Material mat = CityVehicleMaterial();
        Material burnt = CityVehicleBurntMaterial();

        var root = new GameObject(CityVehiclePrefab);
        root.layer = LayerHitbox;
        BoxCollider hitbox = root.AddComponent<BoxCollider>();
        hitbox.size = new Vector3(2f, 1.5f, 4.7f);
        hitbox.center = new Vector3(0f, 0.75f, 0f);

        var models = new GameObject("Models").transform;
        models.SetParent(root.transform, false);
        var variants = new GameObject[CityVehicleMeshes.Length];
        var sizes = new Vector3[CityVehicleMeshes.Length];
        for (int i = 0; i < CityVehicleMeshes.Length; i++)
        {
            Mesh mesh = FbxMesh(CityVehicleFbx, CityVehicleMeshes[i]);
            var go = new GameObject(CityVehicleMeshes[i]);
            go.transform.SetParent(models, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            go.SetActive(i == 0);
            variants[i] = go;
            // 히트박스: 안테나 · 마스트는 빼고 몸통 크기(군용은 높이를 3.6m로 자른다)
            Bounds b = mesh != null ? mesh.bounds : new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(2f, 1.5f, 4.7f));
            float h = i >= ACO_CityVehicle.VARIANT_COUNT_CIVILIAN ? Mathf.Min(b.max.y, 3.6f) : b.max.y;
            sizes[i] = new Vector3(b.size.x, h, b.size.z);
        }

        GameObject marker = NewLockMarker(root.transform, "AAMTarget", LayerEnemyGround, 3f);
        marker.transform.localPosition = new Vector3(0f, 0.8f, 0f);

        GameObject fxPrefab = ACO_FxBuilder.GroundDeathFx();
        GameObject fx;
        if (fxPrefab != null)
        {
            fx = (GameObject)PrefabUtility.InstantiatePrefab(fxPrefab, root.transform);
            fx.name = "DeathFx";
            fx.transform.localScale = Vector3.one * 0.6f;
        }
        else
        {
            fx = new GameObject("DeathFx");
            fx.transform.SetParent(root.transform, false);
        }
        fx.SetActive(false);

        AddTargetAnimator(root);
        SaccTarget health = root.AddUdonSharpComponent<SaccTarget>();
        ACO_LockGate gate = root.AddUdonSharpComponent<ACO_LockGate>();
        ACO_CityVehicle car = root.AddUdonSharpComponent<ACO_CityVehicle>();
        health.Health = 120f;   // 기관포 한 번 · 로켓 · 미사일 한 발
        health.ArmorStrength = 1f;
        health.RespawnDelay = 0f;
        health.PredictExplosion = false;   // 파괴는 소유자 기준(6-248) — SaccTarget 기본값(켜짐)이면 틀릴 때 쏜 사람 화면에서만 투명 · 무적
        health.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { car };
        gate.Mode = ACO_LockGate.MODE_BLOCK;
        gate.Active = false;

        car.TargetHealth = health;
        car.Variants = variants;
        car.VariantSizes = sizes;
        car.Hitbox = hitbox;
        car.LockMarker = marker.transform;
        car.Gate = gate;
        car.DeathFx = fx;
        car.BurntMaterial = burnt;
        car.BaseHealth = health.Health;
        return SaveAndCleanup(root, $"{PrefabDir}/{CityVehiclePrefab}.prefab");
    }
}
