using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 랜스 포대 NPC_Railgun(지상 보스): 모델은 코드로 만든다(외부 에셋 없음). 텍스처는 Tools/aco_railgun_tex.py(강철 패널 + 노멀맵, 콘크리트, 경고 줄무늬).
/// 구조(루트 = 함선 규칙 — 코어 SaccTarget·히트박스·Animator는 루트, 포구 방향 +Z, 바닥 y = 0):
///  - Visual_Base(기초·포탑 받침) · Visual_Mount(포신 받침·벽) : 움직이지 않는 모델 — 지우고 다른 모델을 넣어도 된다.
///  - BarrelPivot(포신 회전축) → Visual_Barrel(모델), ChargeStrip(충전 빛), Muzzle(포구)
///  - Armor_L·Armor_R·Armor_Rear(장갑 회전축) → Visual(모델). 좌우 장갑은 벽과 지붕이 한 덩어리라 바깥으로 눕으면 위쪽도 열린다 — 탑어택 무장이 코어로 내리꽂는다.
///  - Core(구체 + 코일, 마커), Hull(충돌 — 자체 SaccTarget이 피해를 흡수), Parts(방공 12곳, 냉각기 4곳).
/// 다른 모델로 바꿀 때: Visual_* 자식만 갈아 끼우고, 크기가 다르면 Hull 상자와 BarrelPivot·Muzzle·Core 자리를 맞춘다. 로직은 이 Transform들만 쓴다.
public static partial class ACO_PrefabBuilder
{
    private const string RailgunTexDir = "Assets/ACO_Addon/Textures/Railgun";
    private const string RailgunGeneratedDir = "Assets/ACO_Addon/Generated";

    private static readonly Vector3 RailgunCorePos = new Vector3(0f, 46f, -68f);
    /// 포대 배치 판 번호 — 프리팹이 이보다 낮으면 Bake가 기초를 넓히고 포대를 벌린다.
    private const int RailgunLayoutVersion = 2;
    private const float RailgunPadRadius = 530f;

    /// 방공 포대 자리의 반경: 안팎 두 줄로 엇갈려 놓는다 — 폭탄 한 발에 여러 곳이 쓸리지 않게.
    private static float RailgunRingRadius(int i) { return i % 2 == 0 ? 363f : 462f; }

    private static Texture2D LoadRailgunTexture(string file, bool normal)
    {
        string path = $"{RailgunTexDir}/{file}.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null && System.IO.File.Exists(path))
        {
            AssetDatabase.ImportAsset(path);
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
        }
        if (importer != null && normal && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material RailgunMaterial(string name, string albedo, bool normal, Color color, float metallic, float smoothness)
    {
        string path = $"{RailgunGeneratedDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) { return m; }
        m = new Material(Shader.Find("Standard")) { name = name };
        m.SetColor("_Color", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        if (albedo != null) { m.SetTexture("_MainTex", LoadRailgunTexture(albedo, false)); }
        if (normal)
        {
            m.SetTexture("_BumpMap", LoadRailgunTexture("ACO_Railgun_Steel_N", true));
            m.EnableKeyword("_NORMALMAP");
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static Material RailgunUnlitMaterial(string name, Color color)
    {
        string path = $"{RailgunGeneratedDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) { return m; }
        m = new Material(Shader.Find("Unlit/Color")) { name = name };
        m.SetColor("_Color", color);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static Mesh BuildRailgunBaseMesh(bool concrete)
    {
        var bag = new MeshBag { UvScale = 40f };
        if (concrete)
        {
            // 옆면이 깊다 — 기초는 가장 높은 땅에 얹히므로 낮은 쪽 틈을 가린다(FindRailgunSite)
            bag.Prism(new Vector3(0f, 0f, 0f), RailgunPadRadius - 20f, RailgunPadRadius, -90f, 8f, 48);
            bag.Prism(new Vector3(0f, 0f, 0f), 140f, 150f, 8f, 16f, 24);
            return bag.ToMesh("ACO_Railgun_Pad");
        }
        // 포탑 받침 + 포신 받침(양쪽 뺨) + 코어 칸 낮은 틀 + 뒤쪽 축전기 뱅크
        bag.Prism(new Vector3(0f, 0f, 0f), 112f, 120f, 16f, 28f, 24);
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * 40f;
            bag.Hexa(new[]
            {
                new Vector3(x - 8f, 28f, 90f), new Vector3(x + 8f, 28f, 90f), new Vector3(x + 8f, 62f, 56f), new Vector3(x - 8f, 62f, 56f),
                new Vector3(x - 8f, 28f, -8f), new Vector3(x + 8f, 28f, -8f), new Vector3(x + 8f, 62f, 18f), new Vector3(x - 8f, 62f, 18f),
            });
            bag.Box(new Vector3(x + s * 14f, 44f, 38f), new Vector3(12f, 24f, 34f));   // 받침 보강
            bag.Cylinder(new Vector3(x, 50f, 38f), 15f, 15f, 1f, 16);                  // 회전축 덮개 표시
            // 코어 칸 옆 낮은 틀
            bag.Box(new Vector3(s * 52f, 32f, -68f), new Vector3(8f, 8f, 104f));
        }
        bag.Box(new Vector3(0f, 32f, -122f), new Vector3(112f, 8f, 8f));
        bag.Box(new Vector3(0f, 46f, -12f), new Vector3(112f, 36f, 10f));              // 앞 격벽(고정)
        bag.Box(new Vector3(0f, 30f, -68f), new Vector3(96f, 4f, 100f));               // 코어 칸 바닥
        // 코어를 둘러싼 축전기 묶음(칸 안쪽 — 장갑이 열리면 보인다): 받침 위에 놓고 위를 굵은 모선으로 잇는다
        for (int s = -1; s <= 1; s += 2)
        {
            bag.Box(new Vector3(s * 36f, 33f, -68f), new Vector3(22f, 2f, 96f));
            bag.Box(new Vector3(s * 36f, 49f, -68f), new Vector3(6f, 3f, 88f));
            for (int i = 0; i < 4; i++)
            {
                float z = -104f + i * 24f;
                bag.Cylinder(new Vector3(s * 36f, 42f, z), 8f, 8f, 20f, 16);
                bag.Box(new Vector3(s * 36f, 46.5f, z), new Vector3(16f, 3f, 5f));   // 고정 띠 + 모선 연결
                bag.Box(new Vector3(s * 36f, 42f, z - 12f), new Vector3(18f, 16f, 4f));   // 사이 칸막이
            }
            bag.Box(new Vector3(s * 36f, 42f, -20f), new Vector3(18f, 16f, 4f));
        }
        return bag.ToMesh("ACO_Railgun_Mount");
    }

    private static Mesh BuildRailgunBarrelMesh()
    {
        var bag = new MeshBag { UvScale = 30f };
        const float length = 320f;
        // 레일 두 줄(위아래) + 포미 뭉치 + 일정 간격 띠
        bag.Box(new Vector3(0f, 12f, length * 0.5f - 30f), new Vector3(22f, 12f, length));
        bag.Box(new Vector3(0f, -12f, length * 0.5f - 30f), new Vector3(22f, 12f, length));
        bag.Box(new Vector3(-13f, 0f, length * 0.5f - 30f), new Vector3(4f, 14f, length));
        bag.Box(new Vector3(13f, 0f, length * 0.5f - 30f), new Vector3(4f, 14f, length));
        bag.Box(new Vector3(0f, 0f, -18f), new Vector3(44f, 44f, 52f));                // 포미
        bag.Cylinder(new Vector3(0f, 0f, 8f), 25f, 25f, 12f, 8);                       // 회전축 고리(축은 양쪽 뺨에)
        bag.Box(new Vector3(0f, 0f, 8f), new Vector3(80f, 20f, 20f));
        for (int i = 0; i < 9; i++)
        {
            float z = 40f + i * 30f;
            float w = i == 8 ? 40f : 32f - i * 0.6f;
            bag.Box(new Vector3(0f, 0f, z), new Vector3(w, w + 2f, i == 8 ? 12f : 7f));
        }
        // 위쪽 냉각 지느러미
        for (int i = 0; i < 12; i++)
        {
            float z = 24f + i * 18f;
            bag.Box(new Vector3(0f, 22f, z), new Vector3(12f, 8f, 3f));
        }
        return bag.ToMesh("ACO_Railgun_Barrel");
    }

    /// 충전 빛: 레일 사이에 보이는 띠 — ACO_Railgun이 발광을 올린다.
    private static Mesh BuildRailgunStripMesh()
    {
        var bag = new MeshBag();
        bag.Box(new Vector3(0f, 0f, 130f), new Vector3(14f, 6f, 320f));
        return bag.ToMesh("ACO_Railgun_ChargeStrip");
    }

    /// 코어 칸 좌우 장갑 — 회전축이 원점(바닥 바깥 모서리), side = -1 왼쪽, 1 오른쪽. 벽 + 지붕 절반 + 덧댄 판.
    private static Mesh BuildRailgunClamMesh(int side)
    {
        var bag = new MeshBag { UvScale = 30f };
        const float width = 52f, height = 48f, depth = 104f;
        float s = side;
        bag.Box(new Vector3(0f, height * 0.5f, 0f), new Vector3(5f, height, depth));
        bag.Box(new Vector3(-s * width * 0.5f, height + 2f, 0f), new Vector3(width, 5f, depth));
        bag.Hexa(new[]   // 지붕 모서리 경사
        {
            new Vector3(-3f, height - 8f, depth * 0.5f), new Vector3(3f, height - 8f, depth * 0.5f), new Vector3(3f, height + 4f, depth * 0.5f), new Vector3(-3f, height + 4f, depth * 0.5f),
            new Vector3(-3f, height - 8f, -depth * 0.5f), new Vector3(3f, height - 8f, -depth * 0.5f), new Vector3(3f, height + 4f, -depth * 0.5f), new Vector3(-3f, height + 4f, -depth * 0.5f),
        });
        for (int i = 0; i < 5; i++)
        {
            float z = -depth * 0.5f + 12f + i * 20f;
            bag.Box(new Vector3(s * 3.5f, height * 0.5f, z), new Vector3(2f, height - 8f, 14f));   // 바깥 덧판
            bag.Box(new Vector3(-s * width * 0.5f, height + 5.5f, z), new Vector3(width - 8f, 2f, 14f));
        }
        return bag.ToMesh(side < 0 ? "ACO_Railgun_ClamL" : "ACO_Railgun_ClamR");
    }

    private static Mesh BuildRailgunRearMesh()
    {
        var bag = new MeshBag { UvScale = 30f };
        bag.Box(new Vector3(0f, 24f, 0f), new Vector3(100f, 48f, 5f));
        for (int i = 0; i < 4; i++) { bag.Box(new Vector3(-36f + i * 24f, 24f, -3.5f), new Vector3(16f, 40f, 2f)); }
        return bag.ToMesh("ACO_Railgun_ArmorRear");
    }

    /// 코어를 감싼 코일: 매끄러운 원환 셋 + 이음봉 넷. 법선을 직접 넣어 면이 각지지도, 이음매가 보이지도 않는다.
    private static Mesh BuildRailgunCoilMesh()
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        const float uvScale = 20f;

        // (i, j) 격자: a(i) = 고리 중심, ax·ay = 단면 축
        void Grid(int rows, int cols, System.Func<int, int, Vector3> pos, System.Func<int, int, Vector3> normal)
        {
            int start = verts.Count;
            for (int i = 0; i <= rows; i++)
            {
                for (int j = 0; j <= cols; j++)
                {
                    verts.Add(pos(i, j));
                    normals.Add(normal(i, j));
                    uvs.Add(new Vector2(i * 6f, j * 2f) / uvScale);
                }
            }
            int stride = cols + 1;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    int a = start + i * stride + j;
                    int b = a + stride;
                    int c = b + 1;
                    int d = a + 1;
                    tris.AddRange(new[] { a, b, c, a, c, d });
                }
            }
        }

        for (int k = -1; k <= 1; k++)
        {
            const int major = 72;
            const int minor = 14;
            float z = k * 16f;
            float ring = k == 0 ? 24f : 21f;      // 지붕(높이 76m) 아래에 들어가게
            float tube = k == 0 ? 3.8f : 3f;
            Vector3 Dir(int i) { float u = i * Mathf.PI * 2f / major; return new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f); }
            Vector3 Out(int i, int j)
            {
                float v = j * Mathf.PI * 2f / minor;
                return Dir(i) * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v);
            }
            Grid(major, minor, (i, j) => Dir(i) * ring + Vector3.forward * z + Out(i, j) * tube, Out);
        }

        // 세 고리를 잇는 봉(z축)
        for (int k = 0; k < 4; k++)
        {
            const int sides = 12;
            float angle = (k + 0.5f) * Mathf.PI * 0.5f;
            Vector3 center = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 22.5f;
            Vector3 Side(int j) { float v = j * Mathf.PI * 2f / sides; return new Vector3(Mathf.Cos(v), Mathf.Sin(v), 0f); }
            Grid(1, sides, (i, j) => center + Side(j) * 1.3f + Vector3.forward * (i == 0 ? -20f : 20f), (i, j) => Side(j));
        }

        var mesh = new Mesh { name = "ACO_Railgun_Coil" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildRailgunCoolerMesh()
    {
        var bag = new MeshBag { UvScale = 20f };
        bag.Box(new Vector3(0f, 3f, 0f), new Vector3(48f, 6f, 48f));
        bag.Box(new Vector3(0f, 14f, 0f), new Vector3(38f, 16f, 38f));
        // 방열판: 가운데 기둥과 네 귀 지지대에 끼운다
        bag.Box(new Vector3(0f, 36f, 0f), new Vector3(20f, 32f, 20f));
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2) { bag.Box(new Vector3(sx * 12f, 36f, sz * 12f), new Vector3(2.5f, 32f, 2.5f)); }
        }
        for (int i = 0; i < 7; i++) { bag.Box(new Vector3(0f, 25f + i * 4f, 0f), new Vector3(36f - i * 1.5f, 1.6f, 36f - i * 1.5f)); }
        bag.Prism(new Vector3(0f, 0f, 0f), 10f, 12f, 50f, 60f, 16);                     // 위 배기구(기둥에 얹는다)
        for (int s = -1; s <= 1; s += 2)
        {
            bag.Box(new Vector3(s * 21f, 10f, 0f), new Vector3(3f, 10f, 30f));           // 옆 배관
        }
        return bag.ToMesh("ACO_Railgun_Cooler");
    }

    private static ACO_FortressPart AddRailgunCooler(Transform parts, string name, Vector3 pos, Mesh mesh, Material mat, Material hazard)
    {
        var root = new GameObject(name);
        root.layer = LayerHitbox;
        root.transform.SetParent(parts, false);
        root.transform.localPosition = pos;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(50f, 62f, 50f);
        box.center = new Vector3(0f, 31f, 0f);
        box.enabled = false;
        GameObject visual = AddFortressMesh(root.transform, "Visual", mesh, mat, Vector3.zero);
        var strip = new MeshBag { UvScale = 12f };
        strip.Box(new Vector3(0f, 7.5f, 0f), new Vector3(40f, 3f, 40f));
        AddFortressMesh(visual.transform, "Hazard", SaveFortressMesh(strip.ToMesh("ACO_Railgun_CoolerHazard")), hazard, Vector3.zero);
        GameObject smoke = CreateFortressSmoke(root.transform, "Smoke", new Vector3(0f, 30f, 0f), 18f, 60f);
        smoke.SetActive(false);
        GameObject marker = NewLockMarker(root.transform, PartMarkerName, LayerEnemyGround, 26f);
        marker.transform.localPosition = new Vector3(0f, 30f, 0f);
        marker.SetActive(false);
        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        AddTargetAnimator(root);
        SaccTarget health = root.AddUdonSharpComponent<SaccTarget>();
        ACO_FortressPart part = root.AddUdonSharpComponent<ACO_FortressPart>();
        health.Health = 700f;
        health.ArmorStrength = 1f;
        health.RespawnDelay = 0f;
        health.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { part };
        part.TargetHealth = health;
        part.ScoreValue = 800;
        part.MarkerLabel = "CLR";
        part.LockMarkers = new[] { marker };
        part.Hitboxes = new Collider[] { box };
        part.HideOnDeath = new[] { visual };
        part.ShowOnDeath = new[] { smoke };
        return part;
    }

    /// 원형 기초에 내접하는 정사각형 변 — 모서리가 기초 밖으로 나가지 않는다. 둘을 45도 엇갈려 둘레를 덮는다.
    private const float RailgunPadBox = 730f;

    private static void AddRailgunPadDiagonal(Transform hull)
    {
        var go = new GameObject("PadDiagonal");
        go.layer = LayerHitbox;
        go.transform.SetParent(hull, false);
        go.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, -20f, 0f);
        box.size = new Vector3(RailgunPadBox, 56f, RailgunPadBox);
    }

    /// 먼저 만든 프리팹을 새 배치로 옮긴다: 메시는 같은 자산을 고쳐 쓰고(연결 유지), 방공 포대만 벌린다. 다시 만들면 고친 내용이 사라진다.
    public static void EnsureRailgunLayout()
    {
        string path = PathOf("NPC_Railgun");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_Railgun railgun = root.GetComponent<ACO_Railgun>();
            if (railgun == null || railgun.LayoutVersion >= RailgunLayoutVersion) { return; }
            int from = railgun.LayoutVersion;
            railgun.LayoutVersion = RailgunLayoutVersion;
            // 판 2: 소형 레일건 발사음 전용 소리(같은 프리팹을 고친다)
            AudioClip miniClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ACO_Addon/Audio/Generated/ACO_Railgun_MiniFire.wav");
            if (railgun.MiniGuns != null && miniClip != null)
            {
                foreach (ACO_MiniRailgun mini in railgun.MiniGuns)
                {
                    if (mini == null || mini.FireSound == null) { continue; }
                    mini.FireSound.clip = miniClip;
                    mini.FireSound.pitch = 1f;
                }
            }
            if (from >= 1)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return;
            }
            SaveFortressMesh(BuildRailgunBaseMesh(true));
            SaveFortressMesh(BuildRailgunBaseMesh(false));
            SaveFortressMesh(BuildRailgunCoilMesh());
            SaveFortressMesh(BuildRailgunCoolerMesh());
            int moved = 0;
            foreach (UdonSharp.UdonSharpBehaviour part in railgun.AirDefense)
            {
                if (part == null || part.GetComponent<ACO_MiniRailgun>() != null) { continue; }
                Transform t = part.transform;
                Vector3 p = t.localPosition;
                float r = Mathf.Max(new Vector2(p.x, p.z).magnitude, 1f);
                float scale = RailgunRingRadius(moved) / r;
                t.localPosition = new Vector3(p.x * scale, p.y, p.z * scale);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                moved++;
            }
            Transform hull = root.transform.Find("Hull");
            foreach (BoxCollider box in hull.GetComponents<BoxCollider>())
            {
                if (box.size.x < 300f) { continue; }
                box.center = new Vector3(0f, -20f, 0f);
                box.size = new Vector3(RailgunPadBox, 56f, RailgunPadBox);
            }
            AddRailgunPadDiagonal(hull);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ACO] 랜스 포대: 기초를 넓히고 방공 포대 {moved}곳을 벌렸습니다. 코일·냉각기·축전기 모델을 새로 만들었습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static ACO_AAGunSite AddRailgunGun(Transform parts, string name, string prefab, Vector3 pos, float yaw, List<UdonSharp.UdonSharpBehaviour> list)
    {
        ACO_AAGunSite aa = NewTurret(prefab, name, parts, pos, Quaternion.Euler(0f, yaw, 0f), 2.5f).GetComponent<ACO_AAGunSite>();
        list.Add(aa);
        return aa;
    }

    private static LineRenderer CreateRailgunBeam(Transform parent, string name, Color color, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = width;
        line.endWidth = width;
        line.sharedMaterial = RailgunUnlitMaterial(name == "WarnBeam" ? "ACO_RailgunWarn" : "ACO_RailgunBeam", color);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private static AudioSource CreateRailgunSound(Transform parent, string name, string clip, float minDistance, float maxDistance, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        AudioSource a = go.AddComponent<AudioSource>();
        a.clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/Generated/{clip}.wav");
        a.playOnAwake = false;
        a.loop = loop;
        a.spatialBlend = 1f;
        a.dopplerLevel = 0f;
        a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = minDistance;
        a.maxDistance = maxDistance;
        a.priority = 16;
        return a;
    }

    public static GameObject BuildRailgun()
    {
        EnsurePrefabFolder();
        GameObject vlsMissile = LoadOrBuildVlsMissile();
        Material steel = RailgunMaterial("ACO_RailgunSteel", "ACO_Railgun_Steel", true, Color.white, 0.75f, 0.42f);
        Material barrelMat = RailgunMaterial("ACO_RailgunBarrel", "ACO_Railgun_Steel", true, new Color(0.55f, 0.57f, 0.62f), 0.85f, 0.55f);
        Material armorMat = RailgunMaterial("ACO_RailgunArmor", "ACO_Railgun_Steel", true, new Color(0.78f, 0.74f, 0.62f), 0.7f, 0.38f);
        Material concrete = RailgunMaterial("ACO_RailgunConcrete", "ACO_Railgun_Concrete", false, Color.white, 0f, 0.15f);
        Material hazard = RailgunMaterial("ACO_RailgunHazard", "ACO_Railgun_Hazard", false, Color.white, 0.2f, 0.3f);
        Material coreMat = FortressMaterial("ACO_RailgunCore", new Color(0.05f, 0.1f, 0.14f), 0f, 0.8f, new Color(0.5f, 2.4f, 5f));
        Material stripMat = FortressMaterial("ACO_RailgunStrip", Color.black, 0f, 0.5f, new Color(0.4f, 1.2f, 3f));
        Mesh padMesh = SaveFortressMesh(BuildRailgunBaseMesh(true));
        Mesh mountMesh = SaveFortressMesh(BuildRailgunBaseMesh(false));
        Mesh barrelMesh = SaveFortressMesh(BuildRailgunBarrelMesh());
        Mesh stripMesh = SaveFortressMesh(BuildRailgunStripMesh());
        Mesh clamL = SaveFortressMesh(BuildRailgunClamMesh(-1));
        Mesh clamR = SaveFortressMesh(BuildRailgunClamMesh(1));
        Mesh rearMesh = SaveFortressMesh(BuildRailgunRearMesh());
        Mesh coilMesh = SaveFortressMesh(BuildRailgunCoilMesh());
        Mesh coolerMesh = SaveFortressMesh(BuildRailgunCoolerMesh());

        var root = new GameObject("NPC_Railgun");
        root.layer = LayerHitbox;

        // 코어 히트박스(장갑이 열렸을 때만). SaccTarget 규칙상 루트에.
        BoxCollider coreCol = root.AddComponent<BoxCollider>();
        coreCol.size = new Vector3(48f, 48f, 48f);
        coreCol.center = RailgunCorePos;
        coreCol.enabled = false;

        // 고정 모델 — Visual_* 아래만 바꾸면 다른 모델이 된다
        AddFortressMesh(root.transform, "Visual_Base", padMesh, concrete, Vector3.zero);
        GameObject mount = AddFortressMesh(root.transform, "Visual_Mount", mountMesh, steel, Vector3.zero);
        var hazardRing = new MeshBag { UvScale = 10f };
        hazardRing.Prism(Vector3.zero, 141f, 143f, 16f, 17f, 16);
        AddFortressMesh(mount.transform, "HazardRing", SaveFortressMesh(hazardRing.ToMesh("ACO_Railgun_HazardRing")), hazard, Vector3.zero);

        // 포신
        var pivot = new GameObject("BarrelPivot").transform;
        pivot.SetParent(root.transform, false);
        pivot.localPosition = new Vector3(0f, 50f, 38f);
        AddFortressMesh(pivot, "Visual_Barrel", barrelMesh, barrelMat, Vector3.zero);
        GameObject strip = AddFortressMesh(pivot, "ChargeStrip", stripMesh, stripMat, Vector3.zero);
        Transform muzzle = NewChild(pivot.gameObject, "Muzzle", new Vector3(0f, 0f, 295f));

        // 코어 + 장갑(좌우는 벽과 지붕이 한 덩어리 — 바깥으로 누우면 위도 열린다)
        var coreRoot = new GameObject("Core").transform;
        coreRoot.SetParent(root.transform, false);
        coreRoot.localPosition = RailgunCorePos;
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "CoreSphere";
        sphere.layer = LayerHitbox;
        Object.DestroyImmediate(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(coreRoot, false);
        sphere.transform.localScale = Vector3.one * 38f;
        sphere.GetComponent<MeshRenderer>().sharedMaterial = coreMat;
        AddFortressMesh(coreRoot, "Visual_Coil", coilMesh, barrelMat, Vector3.zero);
        GameObject coreMarker = NewLockMarker(coreRoot, "AAMTarget", LayerEnemyGround, 26f);
        coreMarker.SetActive(false);

        var armorL = new GameObject("Armor_L").transform;
        armorL.SetParent(root.transform, false);
        armorL.localPosition = new Vector3(-54f, 28f, -68f);
        AddFortressMesh(armorL, "Visual", clamL, armorMat, Vector3.zero).transform.localPosition = Vector3.zero;
        var armorR = new GameObject("Armor_R").transform;
        armorR.SetParent(root.transform, false);
        armorR.localPosition = new Vector3(54f, 28f, -68f);
        AddFortressMesh(armorR, "Visual", clamR, armorMat, Vector3.zero);
        var armorRear = new GameObject("Armor_Rear").transform;
        armorRear.SetParent(root.transform, false);
        armorRear.localPosition = new Vector3(0f, 28f, -122f);
        AddFortressMesh(armorRear, "Visual", rearMesh, armorMat, Vector3.zero);

        // 충돌: 자체 SaccTarget이 피해를 흡수한다. 코어 칸 위는 비워 두고, 닫힌 동안만 ArmorClosed가 덮는다.
        var hull = new GameObject("Hull");
        hull.layer = LayerHitbox;
        hull.transform.SetParent(root.transform, false);
        var hullCols = new List<Collider>();
        foreach (Vector3[] b in new[]
        {
            new[] { new Vector3(0f, -20f, 0f), new Vector3(RailgunPadBox, 56f, RailgunPadBox) },
            new[] { new Vector3(0f, 22f, 0f), new Vector3(230f, 14f, 230f) },
            new[] { new Vector3(-40f, 45f, 40f), new Vector3(18f, 34f, 100f) },
            new[] { new Vector3(40f, 45f, 40f), new Vector3(18f, 34f, 100f) },
            new[] { new Vector3(0f, 46f, -12f), new Vector3(112f, 36f, 10f) },
        })
        {
            BoxCollider c = hull.AddComponent<BoxCollider>();
            c.center = b[0];
            c.size = b[1];
            hullCols.Add(c);
        }
        AddRailgunPadDiagonal(hull.transform);
        var closed = new GameObject("ArmorClosed");
        closed.layer = LayerHitbox;
        closed.transform.SetParent(hull.transform, false);
        BoxCollider closedCol = closed.AddComponent<BoxCollider>();
        closedCol.center = new Vector3(0f, 52f, -68f);
        closedCol.size = new Vector3(112f, 48f, 112f);
        hullCols.Add(closedCol);
        AddTargetAnimator(hull);
        SaccTarget armor = hull.AddUdonSharpComponent<SaccTarget>();
        armor.Health = 1000000f;
        armor.RespawnDelay = 0f;
        armor.DisableBulletHitEvent = true;

        // 부위: 방공 12곳(기초 둘레, 앞쪽은 포신 자리라 비운다) + 냉각기 4곳
        var body = new GameObject("Parts").transform;
        body.SetParent(root.transform, false);
        var defense = new List<UdonSharp.UdonSharpBehaviour>();
        float[] ringAngles = { 40f, 66f, 92f, 118f, 144f, 170f, 190f, 216f, 242f, 268f, 294f, 320f };
        for (int i = 0; i < ringAngles.Length; i++)
        {
            float a = ringAngles[i] * Mathf.Deg2Rad;
            var p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * RailgunRingRadius(i) + Vector3.up * 8f;
            if (i % 3 == 0) { AddRailgunGun(body, "Flak_" + i, TurretFlak, p, ringAngles[i], defense); }
            else if (i % 3 == 1) { AddRailgunGun(body, "CIWS_" + i, TurretCiws, p, ringAngles[i], defense); }
            else { defense.Add(NewTurret(TurretVls, "VLS_" + i, body, p, Quaternion.identity, 3f).GetComponent<ACO_SAMSite>()); }
        }
        var coolers = new[]
        {
            AddRailgunCooler(body, "Cooler_FL", new Vector3(-120f, 16f, 76f), coolerMesh, steel, hazard),
            AddRailgunCooler(body, "Cooler_FR", new Vector3(120f, 16f, 76f), coolerMesh, steel, hazard),
            AddRailgunCooler(body, "Cooler_RL", new Vector3(-112f, 16f, -96f), coolerMesh, steel, hazard),
            AddRailgunCooler(body, "Cooler_RR", new Vector3(112f, 16f, -96f), coolerMesh, steel, hazard),
        };
        var partList = new List<UdonSharp.UdonSharpBehaviour>(defense);
        partList.AddRange(coolers);

        // 격파 연출·소리·빔
        var effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        var deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        CreateFortressSmoke(deathFx.transform, "Fire_Core", RailgunCorePos, 40f, 120f);
        deathFx.SetActive(false);
        LineRenderer warn = CreateRailgunBeam(root.transform, "WarnBeam", new Color(1f, 0.05f, 0.03f), 5f);
        LineRenderer beam = CreateRailgunBeam(root.transform, "FireBeam", new Color(0.75f, 0.95f, 1f), 1f);
        ParticleSystem flash = CreateBlastSystem(muzzle, "MuzzleFlash", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 500f), 0f, 2, new Color(0.85f, 0.95f, 1f), new Color(0.3f, 0.6f, 1f), 1f);
        SetBlastShape(flash, ParticleSystemShapeType.Sphere, 0.1f);

        AddTargetAnimator(root);
        SaccTarget core = root.AddUdonSharpComponent<SaccTarget>();
        ACO_Warship ship = root.AddUdonSharpComponent<ACO_Warship>();
        ACO_Railgun railgun = root.AddUdonSharpComponent<ACO_Railgun>();

        core.Health = 3000f;
        core.ArmorStrength = 1f;
        core.RespawnDelay = 0f;
        core.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { ship, railgun };

        ship.TargetHealth = core;
        ship.Parts = partList.ToArray();
        ship.TargetName = "Lance Battery";
        ship.MarkerLabel = "LNC";
        ship.ScoreValue = 5000;
        ship.HighValue = true;
        ship.PartHullDamage = 0f;
        ship.CruiseSpeed = 0f;
        ship.CruiseRadius = 0f;
        ship.BobAngle = 0f;
        ship.ExternalPose = true;
        ship.PoseOwner = railgun;
        ship.ShowOnDeath = new[] { deathFx };
        ship.HideOnDeath = new GameObject[0];
        hullCols.Add(coreCol);
        ship.DisableCollidersOnDeath = hullCols.ToArray();

        railgun.LayoutVersion = RailgunLayoutVersion;
        railgun.Warship = ship;
        railgun.TargetHealth = core;
        railgun.AirDefense = defense.ToArray();
        railgun.Coolers = coolers;
        railgun.BarrelPivot = pivot;
        railgun.Muzzle = muzzle;
        railgun.ChargeGlow = strip.GetComponent<MeshRenderer>();
        railgun.ArmorPanels = new[] { armorL, armorR, armorRear };
        railgun.ArmorOpenEuler = new[] { new Vector3(0f, 0f, 80f), new Vector3(0f, 0f, -80f), new Vector3(-85f, 0f, 0f) };
        railgun.ArmorColliders = new Collider[] { closedCol };
        railgun.CoreHitbox = coreCol;
        railgun.CoreMarkers = new[] { coreMarker };
        railgun.Core = coreRoot;
        railgun.WarnBeam = warn;
        railgun.FireBeam = beam;
        railgun.MuzzleFlash = flash;
        railgun.DamageSource = ((ACO_AAGunSite)defense[0]).gameObject;
        railgun.ChargeSound = CreateRailgunSound(root.transform, "ChargeSound", "ACO_Railgun_Charge_Loop", 600f, 18000f, true);
        railgun.FireSound = CreateRailgunSound(root.transform, "FireSound", "ACO_Railgun_Fire", 4000f, 40000f, false);
        railgun.Blast = CreateFortressBlast(root.transform);
        AddRailgunHorizon(root.transform, railgun);
        AddRailgunMinis(root.transform, railgun, ship);

        return SaveAndCleanup(root, $"{PrefabDir}/NPC_Railgun.prefab");
    }

    private static ParticleSystem CreateHorizonSystem(Transform parent, string name, float life, float sizeX, float sizeY, Color c0, Color c1, Material mat)
    {
        ParticleSystem ps = CreateBlastSystem(parent, name, new Vector2(life, life), Vector2.zero, new Vector2(sizeX, sizeX), 0f, 4, c0, c1, 1f);
        ParticleSystem.MainModule main = ps.main;
        main.startSize3D = true;
        main.startSizeX = sizeX;
        main.startSizeY = sizeY;
        main.startSizeZ = sizeX;
        main.startRotation = 0f;
        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.maxParticleSize = 1000f;   // 판이 화면보다 커져도 줄이지 않는다
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// 지평선 섬광: 땅 위로 넓게 퍼지는 흰 빛과 오래 남는 주황 빛, 멀리서 오는 폭음.
    private static void AddRailgunHorizon(Transform root, ACO_Railgun railgun)
    {
        string path = $"{RailgunGeneratedDir}/ACO_HorizonFlash.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("ACO/HorizonFlash")) { name = "ACO_HorizonFlash" };
            AssetDatabase.CreateAsset(mat, path);
        }
        var group = new GameObject("Horizon").transform;
        group.SetParent(root, false);
        railgun.HorizonFlash = CreateHorizonSystem(group, "HorizonFlash", 0.9f, 14000f, 5000f, new Color(1f, 0.97f, 0.9f), new Color(1f, 0.7f, 0.4f), mat);
        railgun.HorizonGlow = CreateHorizonSystem(group, "HorizonGlow", 5f, 9000f, 2600f, new Color(1f, 0.55f, 0.2f), new Color(0.5f, 0.12f, 0.04f), mat);
        AudioSource boom = CreateRailgunSound(group, "HorizonSound", "ACO_Distant_Boom", 1f, 100f, false);
        boom.spatialBlend = 0f;   // 멀리서 울리는 소리 — 어디서 들어도 같다
        boom.volume = 0.85f;
        railgun.HorizonSound = boom;
    }

    /// 소형 레일건 4문(기초 앞쪽 둘 + 좌우 둘): 대공포 틀(탐지·조준·체력)에서 기총을 빼고 ACO_MiniRailgun을 얹는다. 방공 부위라 1단계 목표이고 수리도 된다.
    /// 탄·폭발 파티클은 넷이 함께 쓴다 — 루트 아래 둬 크기 배율을 받지 않는다.
    private static void AddRailgunMinis(Transform root, ACO_Railgun railgun, ACO_Warship ship)
    {
        Material steel = RailgunMaterial("ACO_RailgunBarrel", "ACO_Railgun_Steel", true, new Color(0.55f, 0.57f, 0.62f), 0.85f, 0.55f);
        var fx = new GameObject("MiniFx").transform;
        fx.SetParent(root, false);
        Color white = new Color(0.8f, 0.95f, 1f);
        ParticleSystem shell = CreateBlastSystem(fx, "Shell", new Vector2(3f, 3f), Vector2.zero, new Vector2(12f, 12f), 0f, 16, white, white, 1f);
        SetBlastShape(shell, ParticleSystemShapeType.Sphere, 0.1f);
        ParticleSystemRenderer shellRenderer = shell.GetComponent<ParticleSystemRenderer>();
        shellRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        shellRenderer.velocityScale = 0.08f;
        shellRenderer.lengthScale = 1f;
        ParticleSystem burst = CreateBlastSystem(fx, "Burst", new Vector2(3f, 3.8f), new Vector2(4f, 22f), new Vector2(70f, 140f), 0f, 130, new Color(1f, 0.55f, 0.18f), new Color(0.12f, 0.1f, 0.1f), 0.95f);
        SetBlastShape(burst, ParticleSystemShapeType.Sphere, 125f);
        ParticleSystem flash = CreateBlastSystem(fx, "BurstFlash", new Vector2(0.4f, 0.4f), Vector2.zero, new Vector2(420f, 420f), 0f, 6, new Color(1f, 0.95f, 0.8f), new Color(1f, 0.6f, 0.2f), 1f);
        SetBlastShape(flash, ParticleSystemShapeType.Sphere, 0.1f);

        var spec = new GunSpec
        {
            BodySize = new Vector3(3f, 1.4f, 3f), BarrelLength = 10f, BarrelThickness = 0.55f,
            Health = 260f, Armor = 1f, Score = 350, Label = "RGN",
            DetectRange = 6500f, LoseTargetRange = 7500f, YawRate = 50f, PitchRate = 40f, TrackingLag = 0.2f, AimError = 0.2f,
        };
        Transform parts = root.Find("Parts");
        Vector3[] places = { new Vector3(58f, 8f, 160f), new Vector3(-58f, 8f, 160f), new Vector3(134f, 16f, 0f), new Vector3(-134f, 16f, 0f) };
        var minis = new List<ACO_MiniRailgun>();
        var defense = new List<UdonSharp.UdonSharpBehaviour>(railgun.AirDefense);
        var all = new List<UdonSharp.UdonSharpBehaviour>(ship.Parts);
        for (int i = 0; i < places.Length; i++)
        {
            GameObject part = CreateAAGunObject("MiniRailgun_" + i, spec);
            part.transform.SetParent(parts, false);
            part.transform.localPosition = places[i];
            part.transform.localScale = Vector3.one * 3f;
            foreach (MeshRenderer r in part.GetComponentsInChildren<MeshRenderer>(true)) { r.sharedMaterial = steel; }
            ACO_AAGunSite aa = part.GetComponent<ACO_AAGunSite>();
            // 기총을 뺀다 — 사격은 ACO_MiniRailgun이 한다
            Object.DestroyImmediate(aa.Gun.gameObject);
            aa.Gun = null;
            ACO_MiniRailgun mini = part.AddUdonSharpComponent<ACO_MiniRailgun>();
            aa.AimLeadTime = mini.LeadTime;
            mini.Site = aa;
            mini.Muzzle = NewChild(aa.TurretPitch.gameObject, "Muzzle", new Vector3(0f, 0f, spec.BarrelLength));
            mini.ShellParticle = shell;
            mini.BurstParticle = burst;
            mini.BurstFlash = flash;
            mini.FireSound = CreateRailgunSound(fx, "MiniFire_" + i, "ACO_Railgun_MiniFire", 300f, 7000f, false);
            mini.BurstSound = CreateRailgunSound(fx, "MiniBurst_" + i, "ACO_Explosion_Small", 400f, 6000f, false);
            mini.Laser = CreateMiniLaser(fx, i);
            minis.Add(mini);
            defense.Add(aa);
            all.Add(aa);
        }
        railgun.MiniGuns = minis.ToArray();
        railgun.AirDefense = defense.ToArray();
        ship.Parts = all.ToArray();
    }

    private static LineRenderer CreateMiniLaser(Transform parent, int index)
    {
        LineRenderer line = CreateRailgunBeam(parent, "WarnBeam", new Color(1f, 0.05f, 0.03f), 1f);
        line.gameObject.name = "MiniLaser_" + index;
        return line;
    }

    /// 이미 있는 프리팹에 소형 레일건만 더한다 — 다시 만들면 바꿔 끼운 모델이 사라진다.
    public static void EnsureRailgunMinis()
    {
        string path = PathOf("NPC_Railgun");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_Railgun railgun = root.GetComponent<ACO_Railgun>();
            if (railgun == null) { return; }
            if (railgun.MiniGuns != null && railgun.MiniGuns.Length > 0)
            {
                // 먼저 만든 소형 레일건: 레이저만 더한다
                bool added = false;
                for (int i = 0; i < railgun.MiniGuns.Length; i++)
                {
                    ACO_MiniRailgun mini = railgun.MiniGuns[i];
                    if (mini == null || mini.Laser != null) { continue; }
                    mini.Laser = CreateMiniLaser(root.transform.Find("MiniFx"), i);
                    added = true;
                }
                if (!added) { return; }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[ACO] 소형 레일건에 사선 레이저를 넣었습니다.");
                return;
            }
            AddRailgunMinis(root.transform, railgun, root.GetComponent<ACO_Warship>());
            ACO_SyncModeFixer.FixHierarchy(root, false);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] 랜스 포대 프리팹에 소형 레일건 4문을 넣었습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// 이미 있는 프리팹에 지평선 섬광만 더한다 — 다시 만들면 바꿔 끼운 모델이 사라진다.
    public static void EnsureRailgunHorizon()
    {
        string path = PathOf("NPC_Railgun");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ACO_Railgun railgun = root.GetComponent<ACO_Railgun>();
            if (railgun == null || railgun.HorizonFlash != null) { return; }
            AddRailgunHorizon(root.transform, railgun);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[ACO] 랜스 포대 프리팹에 지평선 섬광을 넣었습니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
