using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// 지형 장식 프리팹(임시 모델): 도시 블록 3종, 마을 2종, 항구, 비행장, 등대, 레이더, 통신탑, 유적.
/// 상자/원기둥/구 기본 메시를 머티리얼별로 합쳐 프리팹 하나 = 메시 하나(드로우콜 = 머티리얼 수)로 만들고,
/// 충돌은 부품마다 상자/구 콜라이더(레이어 Default — 지형 기둥과 같아 SAV 기체가 부딪히면 추락한다).
/// 메시 에셋은 Generated/Deco에, 프리팹은 Prefabs에. LODGroup으로 화면에서 아주 작아지면 그리지 않는다.
/// 치수는 육각 지형 칸(윗면 꼭짓점 반지름 400m)에 맞췄다 — 원점 규칙은 ACO_TerrainFeatures의 프리팹 필드 설명 참고.
public static class ACO_DecoBuilder
{
    private const string PrefabDir = "Assets/ACO_Addon/Prefabs";
    private const string MeshDir = "Assets/ACO_Addon/Generated/Deco";

    public static readonly string[] CityNames = { "ACO_CityBlock_Downtown", "ACO_CityBlock_Residential", "ACO_CityBlock_Industrial" };
    public static readonly string[] VillageNames = { "ACO_Village_A", "ACO_Village_B" };
    public const string PortName = "ACO_Port";
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

    // 머티리얼 번호
    public const int Concrete = 0, Roof = 1, Glass = 2, Asphalt = 3, White = 4, Red = 5, Stone = 6, Lamp = 7, Metal = 8, Field = 9;
    private static readonly string[] MatNames = { "Concrete", "Roof", "Glass", "Asphalt", "White", "Red", "Stone", "Lamp", "Metal", "Field" };
    private static readonly Color[] MatColors =
    {
        new Color(0.62f, 0.62f, 0.6f), new Color(0.55f, 0.22f, 0.15f), new Color(0.2f, 0.28f, 0.38f), new Color(0.17f, 0.17f, 0.18f),
        new Color(0.9f, 0.9f, 0.88f), new Color(0.75f, 0.12f, 0.1f), new Color(0.72f, 0.66f, 0.55f), new Color(1f, 0.85f, 0.4f),
        new Color(0.45f, 0.47f, 0.5f), new Color(0.6f, 0.58f, 0.3f),
    };

    /// 전부 만든다(창의 "지형 장식" 항목).
    public static GameObject[] BuildAll()
    {
        EnsureFolders();
        Material[] mats = Materials();
        var built = new List<GameObject>();
        for (int v = 0; v < CityNames.Length; v++) { built.Add(BuildCityBlock(CityNames[v], v, mats)); }
        for (int v = 0; v < VillageNames.Length; v++) { built.Add(BuildVillage(VillageNames[v], v, mats)); }
        built.Add(BuildPort(mats));
        built.Add(BuildAirfield(mats));
        built.Add(BuildLighthouse(mats));
        built.Add(BuildRadar(mats));
        built.Add(BuildCommTower(mats));
        built.Add(BuildRuins(mats));
        return built.ToArray();
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

    // ───────────── 프리팹들 ─────────────

    /// 도시 블록(칸 하나): 아스팔트 육각 바닥 + 격자로 늘어선 건물. 0 = 도심(가운데 고층), 1 = 주거(중층), 2 = 공업(넓고 낮은 창고 + 굴뚝).
    private static GameObject BuildCityBlock(string name, int variant, Material[] mats)
    {
        var d = new Deco(name, mats);
        var rnd = new System.Random(1000 + variant * 77);
        d.HexPlate(Asphalt, 392f, 0.3f);
        const float cell = 62f;
        for (int gx = -6; gx <= 6; gx++)
        {
            for (int gz = -6; gz <= 6; gz++)
            {
                if (variant != 2 && (gx == 0 || gz == 0)) { continue; } // 큰길
                float x = gx * cell + R(rnd, -6f, 6f);
                float z = gz * cell + R(rnd, -6f, 6f);
                if (!InsideHex(x, z, 330f)) { continue; }
                if (rnd.NextDouble() < 0.15) { continue; } // 공터/공원
                float dist = Mathf.Sqrt(x * x + z * z) / 330f;
                if (variant == 0)
                {
                    float h = Mathf.Lerp(170f, 25f, dist) * R(rnd, 0.45f, 1.15f);
                    float w = R(rnd, 26f, 44f);
                    float l = R(rnd, 26f, 44f);
                    d.Box(h > 70f ? Glass : Concrete, new Vector3(x, h * 0.5f, z), new Vector3(w, h, l));
                    if (h > 90f) { d.Box(Metal, new Vector3(x, h + 4f, z), new Vector3(w * 0.4f, 8f, l * 0.4f)); }
                }
                else if (variant == 1)
                {
                    float h = R(rnd, 14f, 48f);
                    float w = R(rnd, 30f, 46f);
                    float l = R(rnd, 22f, 40f);
                    d.Box(rnd.NextDouble() < 0.5 ? Concrete : White, new Vector3(x, h * 0.5f, z), new Vector3(w, h, l));
                    d.Box(Roof, new Vector3(x, h + 1f, z), new Vector3(w + 2f, 2f, l + 2f), 0f, false);
                }
                else
                {
                    float h = R(rnd, 10f, 22f);
                    float w = R(rnd, 42f, 56f);
                    float l = R(rnd, 28f, 54f);
                    d.Box(Metal, new Vector3(x, h * 0.5f, z), new Vector3(w, h, l));
                    if (rnd.NextDouble() < 0.12) { d.Cylinder(Concrete, new Vector3(x + w * 0.3f, h, z), 3.5f, 55f); }
                    else if (rnd.NextDouble() < 0.15) { d.Cylinder(White, new Vector3(x, h, z + l * 0.25f), 9f, 14f); }
                }
            }
        }
        return d.Save(PrefabDir);
    }

    /// 마을: 지붕 얹은 집 10~14채 + 교회(첨탑) + 둘레 밭. 반경 약 250m.
    private static GameObject BuildVillage(string name, int variant, Material[] mats)
    {
        var d = new Deco(name, mats);
        var rnd = new System.Random(2000 + variant * 91);
        var placed = new List<Vector2>();
        // 교회
        float cy = R(rnd, 0f, 360f);
        d.Box(Stone, new Vector3(0f, 6f, 0f), new Vector3(16f, 12f, 30f), cy);
        d.Box(Roof, new Vector3(0f, 13.5f, 0f), new Vector3(17f, 3f, 31f), cy, false);
        Vector3 towerPos = Quaternion.Euler(0f, cy, 0f) * new Vector3(0f, 0f, 18f);
        d.Box(Stone, towerPos + new Vector3(0f, 14f, 0f), new Vector3(8f, 28f, 8f), cy);
        d.Box(Roof, towerPos + new Vector3(0f, 32f, 0f), new Vector3(5f, 8f, 5f), cy);
        placed.Add(Vector2.zero);
        int houses = 10 + variant * 3 + rnd.Next(0, 3);
        for (int k = 0, tries = 0; k < houses && tries < 400; tries++)
        {
            float a = R(rnd, 0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt((float)rnd.NextDouble()) * 170f + 30f;
            var p = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            bool ok = true;
            foreach (Vector2 q in placed) { if ((q - p).sqrMagnitude < 30f * 30f) { ok = false; break; } }
            if (!ok) { continue; }
            placed.Add(p);
            float yaw = R(rnd, 0f, 360f);
            float w = R(rnd, 12f, 16f);
            float l = R(rnd, 9f, 12f);
            d.Box(rnd.NextDouble() < 0.6 ? White : Stone, new Vector3(p.x, 3.5f, p.y), new Vector3(w, 7f, l), yaw);
            d.Box(Roof, new Vector3(p.x, 8.2f, p.y), new Vector3(w + 1.5f, 2.4f, l + 1.5f), yaw, false);
            k++;
        }
        // 밭(충돌 없음): 마을 바깥 고리
        for (int f = 0; f < 6; f++)
        {
            float a = (f + R(rnd, -0.2f, 0.2f)) / 6f * Mathf.PI * 2f;
            float r = R(rnd, 220f, 270f);
            d.Box(rnd.NextDouble() < 0.5 ? Field : Roof, new Vector3(Mathf.Cos(a) * r, 0.2f, Mathf.Sin(a) * r),
                  new Vector3(R(rnd, 60f, 90f), 0.4f, R(rnd, 40f, 70f)), a * Mathf.Rad2Deg, false);
        }
        return d.Save(PrefabDir);
    }

    /// 항구: 원점 = 해안선(해수면), +Z가 바다. 안벽 + 부두 2 + 크레인 2 + 창고 2 + 컨테이너.
    private static GameObject BuildPort(Material[] mats)
    {
        var d = new Deco(PortName, mats);
        var rnd = new System.Random(3001);
        d.Box(Concrete, new Vector3(0f, -4f, 40f), new Vector3(320f, 14f, 80f));
        d.Box(Concrete, new Vector3(70f, -4f, 200f), new Vector3(30f, 14f, 240f));
        d.Box(Concrete, new Vector3(-90f, -4f, 150f), new Vector3(25f, 14f, 140f));
        for (int c = 0; c < 2; c++)
        {
            float x = -40f + c * 70f;
            d.Box(Red, new Vector3(x - 8f, 23f, 72f), new Vector3(3f, 40f, 3f));
            d.Box(Red, new Vector3(x + 8f, 23f, 72f), new Vector3(3f, 40f, 3f));
            d.Box(Red, new Vector3(x, 44f, 80f), new Vector3(20f, 3f, 60f));
        }
        d.Box(Metal, new Vector3(-90f, 10f, 20f), new Vector3(60f, 14f, 30f));
        d.Box(Metal, new Vector3(90f, 10f, 20f), new Vector3(60f, 14f, 30f));
        for (int k = 0; k < 14; k++)
        {
            int m = k % 3 == 0 ? Red : (k % 3 == 1 ? White : Metal);
            float x = -20f + (k % 7) * 13f;
            float y = 4.3f + (k / 7) * 2.6f;
            d.Box(m, new Vector3(x, y, 45f + R(rnd, -1f, 1f)), new Vector3(12f, 2.6f, 2.5f), 90f, k < 7);
        }
        return d.Save(PrefabDir);
    }

    /// 비행장: 원점 = 활주로 가운데, 활주로 Z축 2.4km. 유도로/계류장/격납고/관제탑/연료 탱크는 +X 쪽
    /// (칸 4개 일렬의 평지 안 — 칸 경계에서 평지 폭이 ±200m로 좁아지므로 시설은 칸 가운데(Z = +346) 근처에 모았다).
    private static GameObject BuildAirfield(Material[] mats)
    {
        var d = new Deco(AirfieldName, mats);
        d.Box(Asphalt, new Vector3(0f, 0.25f, 0f), new Vector3(60f, 0.5f, 2400f));
        for (int k = 0; k < 28; k++) { d.Box(White, new Vector3(0f, 0.55f, -1080f + k * 80f), new Vector3(1.5f, 0.1f, 30f), 0f, false); }
        for (int end = -1; end <= 1; end += 2)
        {
            for (int k = 0; k < 8; k++) { d.Box(White, new Vector3(-24f + k * 6.8f, 0.55f, end * 1170f), new Vector3(3f, 0.1f, 40f), 0f, false); }
        }
        d.Box(Asphalt, new Vector3(150f, 0.2f, 0f), new Vector3(25f, 0.4f, 1800f));
        for (int k = -1; k <= 1; k++) { d.Box(Asphalt, new Vector3(75f, 0.2f, k * 700f), new Vector3(150f, 0.4f, 25f)); }
        d.Box(Concrete, new Vector3(250f, 0.2f, 346f), new Vector3(120f, 0.4f, 200f));
        for (int k = -1; k <= 1; k++) { d.Box(Metal, new Vector3(285f, 9f, 346f + k * 62f), new Vector3(40f, 18f, 48f)); }
        d.Box(Concrete, new Vector3(200f, 15f, 200f), new Vector3(10f, 30f, 10f));
        d.Box(Glass, new Vector3(200f, 33f, 200f), new Vector3(16f, 6f, 16f));
        d.Box(Metal, new Vector3(200f, 36.5f, 200f), new Vector3(17f, 1f, 17f));
        d.Cylinder(White, new Vector3(230f, 0f, 480f), 8f, 10f);
        d.Cylinder(White, new Vector3(230f, 0f, 505f), 8f, 10f);
        return d.Save(PrefabDir);
    }

    /// 등대: 원점 = 바닥, +Z가 바다. 흰 탑(빨간 띠) + 등실 + 관리소.
    private static GameObject BuildLighthouse(Material[] mats)
    {
        var d = new Deco(LighthouseName, mats);
        d.Cylinder(Stone, Vector3.zero, 12f, 4f);
        d.Cylinder(White, new Vector3(0f, 4f, 0f), 5f, 40f);
        d.Cylinder(Red, new Vector3(0f, 14f, 0f), 5.2f, 5f, false);
        d.Cylinder(Red, new Vector3(0f, 30f, 0f), 5.2f, 5f, false);
        d.Cylinder(Metal, new Vector3(0f, 44f, 0f), 7f, 1f);
        d.Cylinder(Lamp, new Vector3(0f, 45f, 0f), 3.5f, 5f);
        d.Cylinder(Red, new Vector3(0f, 50f, 0f), 4f, 2f);
        d.Box(White, new Vector3(-14f, 3f, -12f), new Vector3(10f, 6f, 8f));
        d.Box(Roof, new Vector3(-14f, 7f, -12f), new Vector3(11f, 2f, 9f), 0f, false);
        return d.Save(PrefabDir);
    }

    /// 레이더 기지: 건물 + 받침 + 레이더 돔 + 안테나 마스트.
    private static GameObject BuildRadar(Material[] mats)
    {
        var d = new Deco(RadarName, mats);
        d.Box(Concrete, new Vector3(0f, 6f, 0f), new Vector3(36f, 12f, 24f));
        d.Cylinder(Concrete, new Vector3(0f, 12f, 0f), 10f, 10f);
        d.Sphere(White, new Vector3(0f, 32f, 0f), 16f);
        d.Box(Metal, new Vector3(22f, 15f, 10f), new Vector3(1.5f, 30f, 1.5f));
        d.Box(Concrete, new Vector3(-30f, 4f, -6f), new Vector3(16f, 8f, 12f));
        return d.Save(PrefabDir);
    }

    /// 통신탑: 빨강/흰색 마디 마스트 150m + 접시 안테나 + 꼭대기 등 + 기계실.
    private static GameObject BuildCommTower(Material[] mats)
    {
        var d = new Deco(TowerName, mats);
        d.Box(Concrete, new Vector3(8f, 2.5f, 6f), new Vector3(10f, 5f, 8f));
        for (int k = 0; k < 6; k++) { d.Box(k % 2 == 0 ? Red : White, new Vector3(0f, 12.5f + k * 25f, 0f), new Vector3(3f, 25f, 3f), 0f, false); }
        d.ColliderBox(new Vector3(0f, 75f, 0f), new Vector3(3f, 150f, 3f), 0f);
        d.Cylinder(White, new Vector3(0f, 100f, 2f), 2.5f, 0.6f, false, 90f);
        d.Cylinder(White, new Vector3(2f, 112f, 0f), 2.5f, 0.6f, false, 90f, 90f);
        d.Cylinder(White, new Vector3(0f, 124f, -2f), 2f, 0.6f, false, 90f, 180f);
        d.Sphere(Lamp, new Vector3(0f, 151f, 0f), 1.5f, false);
        return d.Save(PrefabDir);
    }

    /// 유적: 돌 단 위에 돌기둥 고리(일부 부러짐, 몇 쌍은 상인방) + 가운데 오벨리스크 + 쓰러진 기둥.
    private static GameObject BuildRuins(Material[] mats)
    {
        var d = new Deco(RuinsName, mats);
        var rnd = new System.Random(4001);
        d.Cylinder(Stone, Vector3.zero, 70f, 3f);
        const int pillars = 14;
        var full = new bool[pillars];
        var pos = new Vector3[pillars];
        for (int k = 0; k < pillars; k++)
        {
            float a = k / (float)pillars * Mathf.PI * 2f;
            pos[k] = new Vector3(Mathf.Cos(a) * 50f, 3f, Mathf.Sin(a) * 50f);
            full[k] = rnd.NextDouble() > 0.3;
            d.Cylinder(Stone, pos[k], 3.5f, full[k] ? 35f : R(rnd, 8f, 20f));
        }
        for (int k = 0; k < pillars; k += 2)
        {
            int n = (k + 1) % pillars;
            if (!full[k] || !full[n]) { continue; }
            Vector3 mid = (pos[k] + pos[n]) * 0.5f + new Vector3(0f, 36.5f, 0f);
            Vector3 dir = pos[n] - pos[k];
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            d.Box(Stone, mid, new Vector3(4f, 3f, dir.magnitude + 5f), yaw);
        }
        d.Box(Stone, new Vector3(0f, 38f, 0f), new Vector3(8f, 70f, 8f));
        d.Box(Stone, new Vector3(0f, 75f, 0f), new Vector3(5f, 4f, 5f), 45f);
        for (int k = 0; k < 3; k++)
        {
            float a = R(rnd, 0f, Mathf.PI * 2f);
            d.Cylinder(Stone, new Vector3(Mathf.Cos(a) * 78f, 3.5f, Mathf.Sin(a) * 78f), 3.5f, 30f, true, 90f, R(rnd, 0f, 360f), true);
        }
        return d.Save(PrefabDir);
    }

    // ───────────── 도우미 ─────────────

    private static float R(System.Random rnd, float lo, float hi) { return lo + (float)rnd.NextDouble() * (hi - lo); }

    /// 꼭짓점이 ±Z인 육각형(지형 기둥과 같은 방향) 안인지.
    public static bool InsideHex(float x, float z, float radius)
    {
        float ax = Mathf.Abs(x);
        return ax <= radius * 0.866f && Mathf.Abs(z) <= radius - ax * 0.57735f;
    }

    public static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Prefabs")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Prefabs"); }
        if (!AssetDatabase.IsValidFolder("Assets/ACO_Addon/Generated")) { AssetDatabase.CreateFolder("Assets/ACO_Addon", "Generated"); }
        if (!AssetDatabase.IsValidFolder(MeshDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "Deco"); }
    }

    /// 장식 기본 머티리얼(MatNames 순서 — Concrete … Field). 다른 빌더도 쓸 수 있게 공개.
    public static Material[] Materials()
    {
        var mats = new Material[MatNames.Length];
        for (int i = 0; i < MatNames.Length; i++)
        {
            string path = $"{MeshDir}/ACO_Deco_{MatNames[i]}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard")) { name = "ACO_Deco_" + MatNames[i] };
                m.color = MatColors[i];
                m.enableInstancing = true;
                if (i == Glass) { m.SetFloat("_Glossiness", 0.8f); }
                if (i == Lamp)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", MatColors[i] * 2f);
                }
                AssetDatabase.CreateAsset(m, path);
            }
            mats[i] = m;
        }
        return mats;
    }

    /// 부품을 머티리얼별로 모아 메시 하나(서브메시 = 머티리얼)로 합치고, 충돌 부품은 자식 콜라이더로 만든다.
    private class Deco
    {
        private readonly string _name;
        private readonly Material[] _mats;
        private readonly List<CombineInstance>[] _parts;
        private readonly GameObject _root;
        private readonly Mesh _cube;
        private readonly Mesh _cylinder;
        private readonly Mesh _sphere;
        private int _colliders;

        public Deco(string name, Material[] mats)
        {
            _name = name;
            _mats = mats;
            _parts = new List<CombineInstance>[mats.Length];
            for (int i = 0; i < mats.Length; i++) { _parts[i] = new List<CombineInstance>(); }
            _root = new GameObject(name);
            _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            _cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            _sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        }

        /// 상자: center(가운데), size, Y 회전. collide면 같은 크기 상자 콜라이더.
        public void Box(int mat, Vector3 center, Vector3 size, float yaw = 0f, bool collide = true)
        {
            Add(mat, _cube, Matrix4x4.TRS(center, Quaternion.Euler(0f, yaw, 0f), size));
            if (collide) { ColliderBox(center, size, yaw); }
        }

        /// 원기둥: 바닥 가운데 basePos에서 위로 height. tilt(X축 기울기)/yaw로 눕힐 수 있다. lying이면 basePos가 가운데.
        public void Cylinder(int mat, Vector3 basePos, float radius, float height, bool collide = true, float tilt = 0f, float yaw = 0f, bool lying = false)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f);
            Vector3 center = lying || tilt != 0f ? basePos : basePos + Vector3.up * (height * 0.5f);
            // 기본 원기둥 메시: 높이 2, 반지름 0.5
            Add(mat, _cylinder, Matrix4x4.TRS(center, rot, new Vector3(radius * 2f, height * 0.5f, radius * 2f)));
            if (!collide) { return; }
            GameObject go = NewCollider(center, rot);
            BoxCollider c = go.AddComponent<BoxCollider>();
            c.size = new Vector3(radius * 1.8f, height, radius * 1.8f);
        }

        public void Sphere(int mat, Vector3 center, float radius, bool collide = true)
        {
            Add(mat, _sphere, Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * radius * 2f));
            if (!collide) { return; }
            GameObject go = NewCollider(center, Quaternion.identity);
            go.AddComponent<SphereCollider>().radius = radius;
        }

        /// 보이는 메시 없이 상자 콜라이더만(가는 마스트를 한 덩어리로 등).
        public void ColliderBox(Vector3 center, Vector3 size, float yaw)
        {
            GameObject go = NewCollider(center, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<BoxCollider>().size = size;
        }

        /// 육각 바닥판(꼭짓점 ±Z, 지형 기둥 윗면과 같은 방향) — 충돌 없음(기둥 윗면이 받친다).
        public void HexPlate(int mat, float radius, float y)
        {
            var mesh = new Mesh();
            var v = new Vector3[7];
            var t = new int[18];
            v[0] = new Vector3(0f, y, 0f);
            for (int k = 0; k < 6; k++)
            {
                float a = (90f + 60f * k) * Mathf.Deg2Rad;
                v[k + 1] = new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
            }
            for (int k = 0; k < 6; k++)
            {
                t[k * 3] = 0;
                t[k * 3 + 1] = (k + 1) % 6 + 1;
                t[k * 3 + 2] = k + 1;
            }
            mesh.vertices = v;
            mesh.triangles = t;
            mesh.RecalculateNormals();
            if (mesh.normals[0].y < 0f)
            {
                for (int k = 0; k < 6; k++) { int tmp = t[k * 3 + 1]; t[k * 3 + 1] = t[k * 3 + 2]; t[k * 3 + 2] = tmp; }
                mesh.triangles = t;
                mesh.RecalculateNormals();
            }
            Add(mat, mesh, Matrix4x4.identity);
        }

        private void Add(int mat, Mesh mesh, Matrix4x4 m)
        {
            _parts[mat].Add(new CombineInstance { mesh = mesh, transform = m });
        }

        private GameObject NewCollider(Vector3 center, Quaternion rot)
        {
            var go = new GameObject("Col" + _colliders++);
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rot;
            return go;
        }

        /// 메시 에셋(지우고 새로) + 프리팹 저장.
        public GameObject Save(string prefabDir)
        {
            var subMeshes = new List<CombineInstance>();
            var usedMats = new List<Material>();
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i].Count == 0) { continue; }
                var sub = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                sub.CombineMeshes(_parts[i].ToArray(), true, true);
                subMeshes.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
                usedMats.Add(_mats[i]);
            }
            var mesh = new Mesh { name = _name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(subMeshes.ToArray(), false, false);
            mesh.RecalculateBounds();
            string meshPath = $"{MeshDir}/{_name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) { AssetDatabase.DeleteAsset(meshPath); }
            AssetDatabase.CreateAsset(mesh, meshPath);

            _root.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = _root.AddComponent<MeshRenderer>();
            r.sharedMaterials = usedMats.ToArray();
            // 화면 높이 0.4% 아래로 작아지면 그리지 않는다(멀리 있는 마을 수십 개의 비용을 줄인다)
            LODGroup lod = _root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.004f, new Renderer[] { r }) });
            lod.RecalculateBounds();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, $"{prefabDir}/{_name}.prefab");
            Object.DestroyImmediate(_root);
            return prefab;
        }
    }
}
