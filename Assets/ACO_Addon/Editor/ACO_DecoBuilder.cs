using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// 지형 장식 프리팹: 도시 칸 3종, 마을 2종, 항구, 비행장, 등대, 레이더, 통신탑, 유적.
/// 모델은 Tools/aco_deco_models.py가 만든다: Models/Deco/ACO_Deco.fbx(메시 이름 = "프리팹__재질"), ACO_Deco_Colliders.txt, Textures/Deco.
/// 프리팹은 재질마다 자식 렌더러 하나(드로우콜 = 재질 수)이고, 충돌은 건물마다 상자 콜라이더다(레이어 Environment — 충돌은 Default와 같아 SAV 기체가 부딪히면 추락하고, 땅을 재는 레이에는 맞지 않는다. 활주로·계류장 판만 땅이라 Default).
/// 도시 칸의 비탈 큰길은 메시에 들어 있어 코드 처리가 없다. 칸은 0도와 180도로만 돌려야 도로가 이어진다(ACO_TerrainFeatures.AddCity).
/// 프리팹 경로가 같아 GUID가 유지되므로 씬 참조는 그대로다. 메시·머티리얼 에셋은 Generated/Deco에, 프리팹은 Prefabs에.
public static class ACO_DecoBuilder
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    private const string MeshDir = "Assets/ACO_Addon/Generated/Deco";
    private const string ModelFbx = "Assets/ACO_Addon/Models/Deco/ACO_Deco.fbx";
    private const string ColliderList = "Assets/ACO_Addon/Models/Deco/ACO_Deco_Colliders.txt";
    private const string TextureDir = "Assets/ACO_Addon/Textures/Deco";

    public static readonly string[] CityNames = { "ACO_CityBlock_Downtown", "ACO_CityBlock_Residential", "ACO_CityBlock_Industrial" };
    public static readonly string[] VillageNames = { "ACO_Village_A", "ACO_Village_B" };
    public const string PortName = "ACO_Port";
    private const int StructureLayer = 11;      // Environment
    private const float SlabMaxHeight = 1f;
    private const float SlabMinWidth = 20f;
    public const string AirfieldName = "ACO_Airfield";
    public const string LighthouseName = "ACO_Lighthouse";
    public const string RadarName = "ACO_Radar";
    public const string TowerName = "ACO_CommTower";
    public const string RuinsName = "ACO_Ruins";

    public static string[] AllNames()
    {
        var list = new List<string>(CityNames);
        list.AddRange(VillageNames);
        list.AddRange(new[] { PortName, AirfieldName, LighthouseName, RadarName, TowerName, RuinsName });
        return list.ToArray();
    }

    // 창이 있는 재질: 날씨가 _EmissionColor를 바꾼다(ACO_Weather.WindowMaterials)
    private static readonly string[] WindowNames = { "glass", "office", "plaster", "brick" };
    // 납작한 바닥 재질은 그림자를 드리우지 않는다
    private static readonly string[] FlatNames = { "road", "asphalt", "runway", "field", "grass", "pave" };
    private const int WindowTextureSize = 2048;
    private const int MaskTextureSize = 1024;
    // 날씨가 맑으면 꺼 둔다. 0이면 Standard 인스펙터가 _EMISSION 키워드를 지울 수 있어 아주 작은 값을 둔다
    private static readonly Color WindowsOff = new Color(0.004f, 0.004f, 0.004f, 1f);

    /// Setup Check: 프리팹을 새 모델로 다시 만든다.
    public static void InstallModels()
    {
        BuildAll();
        Debug.Log("[ACO] 지형 장식 프리팹을 새 모델로 바꿨습니다.");
    }

    /// 전부 만든다(창의 "지형 장식" 항목).
    public static GameObject[] BuildAll()
    {
        EnsureFolders();
        ACO_PrefabBuilder.ConfigureModelImporter(ModelFbx);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFbx);
        CheckModelAxes(model);
        Dictionary<string, List<DecoBox>> colliders = ReadColliders();
        var built = new List<GameObject>();
        foreach (string name in AllNames()) { built.Add(BuildPrefab(name, model, colliders)); }
        AssetDatabase.SaveAssets();
        return built.ToArray();
    }

    /// 날씨가 켜는 창문 머티리얼(Bake가 ACO_Weather.WindowMaterials에 넣는다). 프리팹을 아직 안 만들었으면 빈 배열.
    public static Material[] WindowMaterials()
    {
        var list = new List<Material>();
        foreach (string name in WindowNames)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(name));
            if (m != null) { list.Add(m); }
        }
        return list.ToArray();
    }

    public static GameObject Load(string name) { return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab"); }

    // ───────────── 수송기 투하 파티클 메시 (NPC_Transport가 쓴다) ─────────────

    /// 보급 상자 + 낙하산(지름 9m 반구 모양): 원점 = 상자.
    public static Mesh DropCrateMesh()
    {
        return DropMesh("ACO_DropCrate", new Vector3(1.6f, 1.3f, 1.6f), 9f);
    }

    /// 공수부대원 + 낙하산(지름 7m).
    public static Mesh DropTrooperMesh()
    {
        return DropMesh("ACO_DropTrooper", new Vector3(0.6f, 1.8f, 0.5f), 7f);
    }

    public static Material DropMaterial(string name, Color color)
    {
        EnsureFolders();
        string path = $"{MeshDir}/ACO_Drop_{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) { return m; }
        Shader shader = Shader.Find("Particles/Standard Surface");
        if (shader == null) { shader = Shader.Find("Standard"); }
        m = new Material(shader) { name = "ACO_Drop_" + name };
        m.color = color;
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static Mesh DropMesh(string name, Vector3 body, float chute)
    {
        EnsureFolders();
        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        var parts = new[]
        {
            new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, body) },
            // 낙하산: 납작한 구(위가 볼록한 갓처럼 보인다) + 줄 대신 가는 기둥
            new CombineInstance { mesh = sphere, transform = Matrix4x4.TRS(new Vector3(0f, body.y * 0.5f + chute * 0.8f, 0f), Quaternion.identity, new Vector3(chute, chute * 0.35f, chute)) },
            new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(new Vector3(0f, body.y * 0.5f + chute * 0.4f, 0f), Quaternion.identity, new Vector3(0.15f, chute * 0.8f, 0.15f)) },
        };
        var mesh = new Mesh { name = name };
        mesh.CombineMeshes(parts, true, true);
        mesh.RecalculateBounds();
        string path = $"{MeshDir}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) { AssetDatabase.DeleteAsset(path); }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // ───────────── 프리팹 ─────────────

    private static GameObject BuildPrefab(string name, GameObject model, Dictionary<string, List<DecoBox>> colliders)
    {
        string prefix = name + "__";
        var root = new GameObject(name);
        try
        {
            var renderers = new List<Renderer>();
            foreach (Transform part in model.transform)
            {
                if (!part.name.StartsWith(prefix, StringComparison.Ordinal)) { continue; }
                string material = part.name.Substring(prefix.Length);
                var go = new GameObject(material);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = DecoMaterial(material);
                if (Array.IndexOf(FlatNames, material) >= 0) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                renderers.Add(r);
            }
            if (renderers.Count == 0) { throw new InvalidOperationException($"[ACO] {ModelFbx}에 {name} 메시가 없습니다. Tools/aco_deco_models.py를 실행하세요."); }
            if (colliders.TryGetValue(name, out List<DecoBox> boxes))
            {
                for (int i = 0; i < boxes.Count; i++) { AddCollider(root.transform, i, boxes[i]); }
            }
            // 화면 높이 0.4% 아래로 작아지면 그리지 않는다(멀리 있는 마을 수십 개의 비용을 줄인다)
            LODGroup lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.004f, renderers.ToArray()) });
            lod.RecalculateBounds();
            return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private struct DecoBox
    {
        public Vector3 Center;
        public Vector3 Size;
        public Quaternion Rotation;
    }

    private static void AddCollider(Transform root, int index, DecoBox box)
    {
        var go = new GameObject("Col" + index);
        go.transform.SetParent(root, false);
        go.transform.localPosition = box.Center;
        go.transform.localRotation = box.Rotation;
        go.AddComponent<BoxCollider>().size = box.Size;
        go.layer = ColliderLayer(box.Size);
    }

    /// 얇고 넓은 판(활주로, 계류장, 유도로)은 전차가 올라서는 땅이고, 나머지는 구조물이다.
    private static int ColliderLayer(Vector3 size)
    {
        bool slab = size.y < SlabMaxHeight && Mathf.Min(size.x, size.z) >= SlabMinWidth;
        return slab ? 0 : StructureLayer;
    }

    /// 이미 만든 프리팹의 콜라이더 레이어만 바꾼다. 모델과 손으로 고친 내용은 그대로다.
    public static void ApplyColliderLayers()
    {
        foreach (string name in AllNames())
        {
            string path = $"{PrefabDir}/{name}.prefab";
            if (!File.Exists(path)) { continue; }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int changed = 0;
                foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>(true))
                {
                    int layer = ColliderLayer(box.size);
                    if (box.gameObject.layer == layer) { continue; }
                    box.gameObject.layer = layer;
                    changed++;
                }
                if (changed == 0) { continue; }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[ACO] {name}: 콜라이더 {changed}개의 레이어를 바꿨습니다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    /// 줄 형식 두 가지. "프리팹 x y z 크기x 크기y 크기z Y회전", "프리팹 strut 끝점a(xyz) 끝점b(xyz) 단면폭".
    private static Dictionary<string, List<DecoBox>> ReadColliders()
    {
        if (!File.Exists(ColliderList)) { throw new InvalidOperationException($"[ACO] {ColliderList}가 없습니다. Tools/aco_deco_models.py를 실행하세요."); }
        var result = new Dictionary<string, List<DecoBox>>();
        foreach (string line in File.ReadAllLines(ColliderList))
        {
            string[] parts = line.Split(' ');
            bool strut = parts.Length == 9 && parts[1] == "strut";
            if (!strut && parts.Length != 8) { continue; }
            int first = strut ? 2 : 1;
            var v = new float[7];
            for (int i = 0; i < 7; i++) { v[i] = float.Parse(parts[first + i], CultureInfo.InvariantCulture); }
            if (!result.TryGetValue(parts[0], out List<DecoBox> list))
            {
                list = new List<DecoBox>();
                result[parts[0]] = list;
            }
            list.Add(strut ? StrutBox(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]), v[6]) : new DecoBox
            {
                Center = new Vector3(v[0], v[1], v[2]),
                Size = new Vector3(v[3], v[4], v[5]),
                Rotation = Quaternion.Euler(0f, v[6], 0f),
            });
        }
        return result;
    }

    private static DecoBox StrutBox(Vector3 a, Vector3 b, float width)
    {
        Vector3 along = b - a;
        return new DecoBox
        {
            Center = (a + b) * 0.5f,
            Size = new Vector3(width, width, along.magnitude),
            Rotation = Quaternion.LookRotation(along),
        };
    }

    /// 축과 크기가 예상대로인지 본다. 어긋나면 프리팹을 건드리기 전에 멈춘다.
    private static void CheckModelAxes(GameObject model)
    {
        if (model == null) { throw new InvalidOperationException($"[ACO] {ModelFbx}가 없습니다. Tools/aco_deco_models.py를 실행하세요."); }
        Bounds runway = PartBounds(model, AirfieldName + "__runway");
        Bounds hangars = PartBounds(model, AirfieldName + "__metal");
        Bounds quay = PartBounds(model, PortName + "__pave");
        Bounds roads = PartBounds(model, CityNames[0] + "__road");
        if (runway.size.z < 2300f || runway.size.z > 2500f || hangars.center.x < 200f || hangars.center.z < 200f || quay.center.z < 40f || roads.min.y > -30f || roads.max.y < 0f)
        {
            throw new InvalidOperationException($"[ACO] {ModelFbx}의 축이나 크기가 예상과 다릅니다(활주로 {runway.size}, 격납고 {hangars.center}, 부두 {quay.center}, 도로 {roads.min} ~ {roads.max}). 프리팹은 고치지 않았습니다.");
        }
    }

    private static Bounds PartBounds(GameObject model, string partName)
    {
        Transform part = model.transform.Find(partName);
        MeshFilter filter = part != null ? part.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) { throw new InvalidOperationException($"[ACO] {ModelFbx}에 {partName}이 없습니다."); }
        return filter.sharedMesh.bounds;
    }

    // ───────────── 머티리얼 ─────────────

    private static string MaterialPath(string name) { return $"{MeshDir}/ACO_DecoMat_{name}.mat"; }

    private static Material DecoMaterial(string name)
    {
        string path = MaterialPath(name);
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard")) { name = "ACO_DecoMat_" + name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.enableInstancing = true;
        bool window = Array.IndexOf(WindowNames, name) >= 0;
        if (name == "lamp") { m.color = new Color(0.25f, 0.4f, 0.5f); }
        else if (name == "glow")
        {
            m.color = new Color(1f, 0.8f, 0.4f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.4f) * 3f);
        }
        else { m.SetTexture("_MainTex", DecoTexture(name, window ? WindowTextureSize : 1024)); }
        bool shiny = name == "glass" || name == "lamp";
        m.SetFloat("_Glossiness", shiny ? 0.75f : 0.15f);
        m.SetFloat("_Metallic", name == "glass" || name == "metal" || name == "steel" ? 0.5f : 0f);
        if (window)
        {
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", DecoTexture(name + "_e", MaskTextureSize));
            m.SetColor("_EmissionColor", WindowsOff);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Texture2D DecoTexture(string name, int maxSize)
    {
        string path = $"{TextureDir}/ACO_Deco_{name}.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) { throw new InvalidOperationException($"[ACO] {path}가 없습니다. Tools/aco_deco_models.py를 실행하세요."); }
        if (importer.wrapMode != TextureWrapMode.Repeat || importer.maxTextureSize != maxSize || importer.anisoLevel != 4)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = maxSize;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Prefabs")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Prefabs"); }
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
        if (!AssetDatabase.IsValidFolder(MeshDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "Deco"); }
    }
}
