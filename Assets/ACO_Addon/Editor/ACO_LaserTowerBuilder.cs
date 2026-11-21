using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 레이저 탑 NPC_LaserTower(지상 보스, 6-331): 모델은 Tools/aco_laser_tower_model.py · aco_laser_tower_units_model.py(Models/LaserTower). 기초판만 코드 메시.
/// 구조(루트 = 함선 규칙 — 코어 SaccTarget · 히트박스 · Animator는 루트, 렌즈 기본 방향 +Z, 바닥 y = 0):
///  - Visual_Pad(기초판) · Visual_Base(주탑) · Visual_Conduits · Visual_Aux1..6(보조탑): 움직이지 않는 모델.
///  - HeadPivot(좌우 회전, (0, 792, 0)) → Visual_Head, LensPivot(위아래 회전, 헤드 위 14m) → Visual_Lens, AAMTarget(코어 락온 마커).
///  - Hull(충돌 — 자체 SaccTarget이 피해를 흡수): 기초판 · 주탑 단 · 보조탑 · 헤드 상자.
///  - Parts: Vent_1..6(보조탑 끝 환풍구 — ACO_FortressPart), Flak_1..6 · VLS_1..2(탑 둘레 방공),
///    EcmSite_0..2(ECM 차량 — ACO_FortressPart + ACO_EnemyJammer, 둘레 대공포 3 · VLS 1 · CIWS 1 — 페이즈가 탑에서 약 2km 자리로 옮긴다),
///    Airship_0..9(반사판 비행선 — ACO_FortressPart, 공중 락온 레이어 — 페이즈가 궤도로 옮긴다, 반사경은 천천히 돈다).
/// 부위는 처음에 드러나지 않는다(ACO_FortressPart.Exposed) — 단계에 따라 페이즈 스크립트(6-331 ④)가 드러낸다.
///  - Beams(6-331 ④-2): BeamA(패턴 A — 조준 얇게 · 발사 굵게) · Net_0..25(패턴 B 그물 갈래) — 파란 빛줄기 LineRenderer(월드 좌표).
///  - LensPivot/OverheatGlow: 렌즈 메시를 조금 키운 붉은 가산 껍질 — 3단계 과열 창에만 켠다.
///  - Beams/BeamSparks · BeamGlow · BeamScorch: 굵은 빔이 땅 · 물에 닿은 자리의 불티 · 빛 · 그을림(땅만, 누운 검은 얼룩 — 텍스처는 코드가 만든다).
public static partial class ACO_PrefabBuilder
{
    public const string LaserTowerName = "NPC_LaserTower";
    private const string LaserTowerDir = "Assets/ACO_Addon/Models/LaserTower";
    private const string LaserTowerFbx = LaserTowerDir + "/ACO_LaserTower.fbx";
    private const string LaserAirshipFbx = LaserTowerDir + "/ACO_ReflectorAirship.fbx";
    private const string LaserEcmFbx = LaserTowerDir + "/ACO_EcmTruck.fbx";
    private const string LaserMatDir = "Assets/ACO_Addon/Generated/LaserTower";

    /// 모델 수치(aco_laser_tower_model.py와 같은 값)
    public static readonly Vector3 LaserHeadPos = new Vector3(0f, 792f, 0f);
    public const float LaserLensRise = 14f;
    public const float LaserAuxRadius = 490f;
    public const float LaserAuxHeight = 440f;
    public const float LaserPadRadius = 560f;
    /// 기초판 옆면 깊이(m) — 사용자 "아주 길게": 생성기 LaserTowerMaxRelief(500)보다 훨씬 깊게, 가장 높은 땅에 얹어도 낮은 쪽이 뜨지 않는다(랜스와 같은 2,000m)
    public const float LaserPadDepth = ACO_PrefabBuilder.RailgunPadDepth;
    private static readonly float[,] LaserProfile = { { 0f, 22f, 13f }, { 264f, 17.5f, 10f }, { 550f, 12.5f, 7.2f }, { 704f, 9.5f, 5.4f }, { 792f, 6.5f, 4f } };

    /// ECM 기지 · 비행선 처음 자리(페이즈가 옮긴다)
    public const float LaserEcmDistance = 2000f;
    public const float LaserAirshipOrbit = 2200f;
    public const float LaserAirshipAltitude = 700f;
    /// 반사판 비행선 크기 · 체력(사용자 "체력 3천에 크기는 30% 정도로 — 반사경만 든 것치곤 너무 커"): 모델(길이 130m)을 부위째 줄인다 — 길이 약 39m, 반사경 지름 약 6.6m
    public const float LaserAirshipScale = 0.3f;
    /// 바뀜(사용자 "비행선 척당 체력은 절반 정도로 줄이고 개수를 늘리자"): 6척 × 3,000 → 10척 × 1,500
    public const float LaserAirshipHealth = 1000f;   // 2026-11-20 1500 → 1000(사용자 "기구 체력 1000 정도로")
    public const float LaserLensHealth = 2000f;   // 렌즈(코어) — 2026-11-20 3000 → 2000(과열 창에만 맞는다)
    public const int LaserAirshipCount = 10;
    /// 환풍구 체력(사용자 "환풍구는 체력 3000으로")
    public const float LaserVentHealth = 2200f;   // 2026-11-20 3000 → 2200(보스 총체력 맞춤 — 사용자)
    /// ECM 기지 방공 크기(차량 체급에 맞춘다)
    public const float LaserSiteTurretScale = 1.2f;
    /// ECM 기지 배치(사용자 "UGB에 한 방에 다 안 쓸리게" → 풀 강화 기준): 대공포 3문 · VLS · CIWS 모두 차량에서 600m(대공포는 120° 간격, VLS · CIWS는 그 사이).
    /// 가장 나쁜 자리에 떨어져도 기본 UGB(반경 200m)는 1곳, 풀 강화 UGB(500m)는 3곳까지만 맞는다.
    public const float LaserSiteFlakRadius = 600f;
    public static readonly Vector3 LaserSiteVlsPos = new Vector3(595.5f, 0f, 73.1f);
    public static readonly Vector3 LaserSiteCiwsPos = new Vector3(-234.4f, 0f, -552.3f);
    /// 그물 갈래 수(렌즈 → 첫 비행선 1 + 비행선 10척 × 반사경 5갈래)
    public const int LaserNetBranches = 5;
    public const int LaserNetLines = 1 + LaserAirshipCount * LaserNetBranches;
    private static readonly Color LaserBeamCore = new Color(0.75f, 0.95f, 1f, 1f);
    private static readonly Color LaserBeamEdge = new Color(0.35f, 0.65f, 1f, 0.8f);

    public static Vector3 LaserAuxPos(int i)
    {
        float a = Mathf.PI / 6f + i * Mathf.PI * 2f / 6f;
        return new Vector3(LaserAuxRadius * Mathf.Sin(a), 0f, LaserAuxRadius * Mathf.Cos(a));
    }

    private static Vector2 LaserHalf(float y)
    {
        for (int i = 0; i < LaserProfile.GetLength(0) - 1; i++)
        {
            float y0 = LaserProfile[i, 0], y1 = LaserProfile[i + 1, 0];
            if (y <= y1)
            {
                float t = Mathf.Clamp01((y - y0) / (y1 - y0));
                return new Vector2(Mathf.Lerp(LaserProfile[i, 1], LaserProfile[i + 1, 1], t), Mathf.Lerp(LaserProfile[i, 2], LaserProfile[i + 1, 2], t));
            }
        }
        return new Vector2(LaserProfile[LaserProfile.GetLength(0) - 1, 1], LaserProfile[LaserProfile.GetLength(0) - 1, 2]);
    }

    // ───────────── 재질 ─────────────

    private static Texture2D LaserTexture(string file, bool normal)
    {
        string path = $"{LaserTowerDir}/{file}.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && normal && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// 재질 하나(있으면 그대로 — 값을 고치면 이름을 바꾸거나 지우고 다시 만든다).
    private static Material LaserMaterial(string name, string albedo, string normal, Color color, float metallic, float smoothness, Color emission, string emissionTex = null)
    {
        if (!AssetDatabase.IsValidFolder(LaserMatDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "LaserTower"); }
        string path = $"{LaserMatDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) { return m; }
        m = new Material(Shader.Find("Standard")) { name = name };
        m.SetColor("_Color", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        if (albedo != null) { m.SetTexture("_MainTex", LaserTexture(albedo, false)); }
        if (normal != null)
        {
            m.SetTexture("_BumpMap", LaserTexture(normal, true));
            m.EnableKeyword("_NORMALMAP");
        }
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            if (emissionTex != null) { m.SetTexture("_EmissionMap", LaserTexture(emissionTex, false)); }
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// FBX 재질 이름(블렌더 재질) → 유니티 재질.
    private static Dictionary<string, Material> LaserMaterials()
    {
        Material metal = LaserMaterial("ACO_LaserTower_Metal", "ACO_LaserTower_Metal", "ACO_LaserTower_Metal_Normal", Color.white, 0.55f, 0.35f, Color.black);
        Material window = LaserMaterial("ACO_LaserTower_Window", null, null, new Color(0.05f, 0.06f, 0.08f), 0.2f, 0.9f, Color.black);
        Material glow = LaserMaterial("ACO_LaserTower_Glow", null, null, new Color(1f, 0.3f, 0.25f), 0f, 0.5f, new Color(2.4f, 0.35f, 0.25f));
        return new Dictionary<string, Material>
        {
            { "ACO_LaserTower_Concrete", LaserMaterial("ACO_LaserTower_Concrete", "ACO_LaserTower_Concrete", "ACO_LaserTower_Concrete_Normal", Color.white, 0f, 0.12f, Color.black) },
            { "ACO_LaserTower_Metal", metal },
            { "ACO_LaserTower_Window", window },
            { "ACO_LaserTower_Glow", glow },
            { "ACO_LaserTower_Lens", LaserMaterial("ACO_LaserTower_Lens", null, null, new Color(0.3f, 0.95f, 1f), 0.1f, 0.95f, new Color(0.35f, 1.6f, 2.0f)) },
            { "ACO_LaserTower_Radiator", LaserMaterial("ACO_LaserTower_Radiator", "ACO_LaserTower_Radiator", null, Color.white, 0.3f, 0.4f, new Color(1.6f, 1.6f, 1.6f), "ACO_LaserTower_Radiator") },
            { "ACO_LaserTowerUnits_Hull", LaserMaterial("ACO_LaserTower_AirshipHull", "ACO_ReflectorAirship_Hull", null, Color.white, 0.1f, 0.3f, Color.black) },
            { "ACO_LaserTowerUnits_Metal", metal },
            { "ACO_LaserTowerUnits_Window", window },
            { "ACO_LaserTowerUnits_Mirror", LaserMaterial("ACO_LaserTower_Mirror", null, null, new Color(0.82f, 0.88f, 0.92f), 1f, 0.95f, Color.black) },
            { "ACO_LaserTowerUnits_Glow", glow },
            { "ACO_LaserTowerUnits_Paint", LaserMaterial("ACO_LaserTower_EcmPaint", "ACO_EcmTruck_Paint", null, Color.white, 0.1f, 0.2f, Color.black) },
            { "ACO_LaserTowerUnits_Rubber", LaserMaterial("ACO_LaserTower_Rubber", null, null, new Color(0.05f, 0.05f, 0.05f), 0f, 0.1f, Color.black) },
        };
    }

    /// FBX 노드의 메시를 새 오브젝트에 — 서브메시 재질은 FBX 재질 이름으로 찾아 넣는다.
    private static GameObject LaserModel(Transform parent, string objName, string fbx, string node, Vector3 localPos, Dictionary<string, Material> mats)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        Transform src = asset != null ? asset.transform.Find(node) : null;
        if (src == null && asset != null && asset.transform.childCount == 0) { src = asset.transform; }
        if (src == null) { throw new System.InvalidOperationException($"[ACO] {fbx}에 {node}이 없습니다."); }
        GameObject go = AddFortressMesh(parent, objName, src.GetComponent<MeshFilter>().sharedMesh, null, localPos);
        Material[] from = src.GetComponent<MeshRenderer>().sharedMaterials;
        var to = new Material[from.Length];
        for (int i = 0; i < from.Length; i++)
        {
            string key = from[i] != null ? from[i].name : "";
            to[i] = mats.TryGetValue(key, out Material m) ? m : mats["ACO_LaserTower_Metal"];
        }
        go.GetComponent<MeshRenderer>().sharedMaterials = to;
        return go;
    }

    /// 과열 껍질: 렌즈 메시를 가운데 기준 4% 키우고 붉은 정점색을 넣은 가산(ACO/FxParticle) 메시.
    private static GameObject LaserOverheatShell(Transform lens)
    {
        Transform src = lens.Find("Visual_Lens");
        Mesh from = src.GetComponent<MeshFilter>().sharedMesh;
        Vector3 c = from.bounds.center;
        Vector3[] v = from.vertices;
        for (int i = 0; i < v.Length; i++) { v[i] = c + (v[i] - c) * 1.04f; }
        var tris = new List<int>();
        for (int k = 0; k < from.subMeshCount; k++) { tris.AddRange(from.GetTriangles(k)); }
        var colors = new Color[v.Length];
        for (int i = 0; i < colors.Length; i++) { colors[i] = new Color(1f, 0.22f, 0.08f, 0.55f); }
        var mesh = new Mesh { name = "ACO_LaserTower_OverheatShell" };
        mesh.vertices = v;
        mesh.colors = colors;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        Material mat = AssetDatabase.LoadAssetAtPath<Material>($"{LaserMatDir}/ACO_LaserTower_Overheat.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("ACO/FxParticle")) { name = "ACO_LaserTower_Overheat" };
            AssetDatabase.CreateAsset(mat, $"{LaserMatDir}/ACO_LaserTower_Overheat.mat");
        }
        mat.SetFloat("_Boost", 2.5f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_FogToBlack", 1f);
        EditorUtility.SetDirty(mat);
        GameObject go = AddFortressMesh(lens, "OverheatGlow", SaveFortressMesh(mesh), mat, Vector3.zero);
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.SetActive(false);
        return go;
    }

    // ───────────── 기초판 메시(옆면 셰이드 스무스) ─────────────

    /// 세로 기둥 꼴(위 반지름 rTop · 아래 rBottom, 높이 yBottom~yTop) 여러 개로 된 기초 메시. 옆면은 정점을 이웃 면과 나눠 쓰고 바깥쪽 법선을 직접 넣어 부드럽게(셰이드 스무스),
    /// 윗면은 따로 평평하게. 이음매는 UV 때문에 정점을 겹쳐 두되 법선은 같아 줄이 보이지 않는다. 아랫면은 없다(땅속).
    /// prisms: 행마다 { rTop, rBottom, yBottom, yTop, segments }.
    public static Mesh SmoothPadMesh(string name, float uvScale, float[,] prisms)
    {
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        for (int p = 0; p < prisms.GetLength(0); p++)
        {
            float rTop = prisms[p, 0], rBottom = prisms[p, 1], yBottom = prisms[p, 2], yTop = prisms[p, 3];
            int seg = Mathf.RoundToInt(prisms[p, 4]);
            float rAvg = (rTop + rBottom) * 0.5f;
            // 옆면: 위 · 아래 고리(이음매에서 한 번 겹침)
            int side = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 normal = (d * (yTop - yBottom) + Vector3.up * (rBottom - rTop)).normalized;
                float u = a * rAvg / uvScale;
                v.Add(d * rTop + Vector3.up * yTop); n.Add(normal); uv.Add(new Vector2(u, yTop / uvScale));
                v.Add(d * rBottom + Vector3.up * yBottom); n.Add(normal); uv.Add(new Vector2(u, yBottom / uvScale));
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = side + i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                Vector3 outward = (n[a0] + n[a1]).normalized;
                AddPadTri(v, t, a0, a1, b1, outward);
                AddPadTri(v, t, a0, b1, b0, outward);
            }
            // 윗면: 가운데 + 고리(평평)
            int top = v.Count;
            v.Add(Vector3.up * yTop); n.Add(Vector3.up); uv.Add(Vector2.zero);
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 q = new Vector3(Mathf.Cos(a) * rTop, yTop, Mathf.Sin(a) * rTop);
                v.Add(q); n.Add(Vector3.up); uv.Add(new Vector2(q.x, q.z) / uvScale);
            }
            for (int i = 0; i < seg; i++) { AddPadTri(v, t, top, top + 1 + i, top + 1 + (i + 1) % seg, Vector3.up); }
        }
        var mesh = new Mesh { name = name };
        if (v.Count > 65000) { mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// 삼각형 하나 — 앞면(Unity: Cross(b - a, c - a))이 outward 쪽을 보게 감는다.
    private static void AddPadTri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) >= 0f) { t.Add(a); t.Add(b); t.Add(c); }
        else { t.Add(a); t.Add(c); t.Add(b); }
    }

    // ───────────── 소리(6-331 ⑤) ─────────────

    /// 렌즈 자리 3D 소리(Audio/SFX/<clip>.mp3) — 탑이 거대해서 멀리서도 들리게(15km까지 줄어든다).
    private static AudioSource LaserSound(Transform parent, string name, string clip, float volume)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        AudioSource a = go.AddComponent<AudioSource>();
        a.clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/ACO_Addon/Audio/SFX/{clip}.mp3");
        a.playOnAwake = false;
        a.loop = false;
        a.volume = volume;
        a.spatialBlend = 1f;
        a.dopplerLevel = 0f;
        a.rolloffMode = AudioRolloffMode.Logarithmic;
        a.minDistance = 900f;
        a.maxDistance = 15000f;
        a.priority = 16;
        return a;
    }

    // ───────────── 빔 끝 효과(6-331 ④-2) ─────────────

    /// 그을림 텍스처(128², 가운데가 진하고 가장자리가 들쭉날쭉한 검은 얼룩 — 펄린 노이즈). 있으면 그대로.
    private static Texture2D LaserScorchTexture()
    {
        string path = LaserTowerDir + "/ACO_LaserScorch.png";
        if (!System.IO.File.Exists(path))
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = 0.72f + 0.2f * Mathf.PerlinNoise(Mathf.Cos(ang) * 2.2f + 5f, Mathf.Sin(ang) * 2.2f + 9f);
                    float a = Mathf.Clamp01((edge - d) / 0.38f);
                    a = a * a * (3f - 2f * a);
                    a *= 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.09f + 13f, y * 0.09f + 3f);
                    Color c = Color.Lerp(new Color(0.02f, 0.018f, 0.016f), new Color(0.16f, 0.11f, 0.07f), Mathf.Clamp01(d * 1.1f));
                    c.a = a * 0.92f;
                    t.SetPixel(x, y, c);
                }
            }
            t.Apply();
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// 효과 재질(ACO/FxParticle) — 가산이면 불빛, 아니면 알파(그을림).
    private static Material LaserFxMaterial(string name, Texture2D tex, bool additive, float boost)
    {
        string path = $"{LaserMatDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("ACO/FxParticle")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_MainTex", tex);
        m.SetFloat("_Boost", boost);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_FogToBlack", additive ? 1f : 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// 스크립트가 Emit만 하는 월드 파티클(자체 방출 없음).
    private static ParticleSystem LaserEmitOnly(Transform parent, string name, float life, int max, Color color, Material mat, ParticleSystemRenderMode mode)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = life;
        main.startSpeed = 0f;
        main.startSize = 10f;
        main.startColor = color;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.maxParticleSize = 0.6f;
        return ps;
    }

    private static void LaserFade(ParticleSystem ps, Color from, Color to, float holdUntil)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, holdUntil), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    /// 불티: 레이저 에이스 불티(늘어나는 점)를 파랑 · 흰색으로.
    private static ParticleSystem LaserBeamSparks(Transform parent)
    {
        ParticleSystem ps = ACO_FxBuilder.BuildLaserSparks(parent);
        ps.gameObject.name = "BeamSparks";
        ParticleSystem.MainModule main = ps.main;
        main.maxParticles = 900;
        main.gravityModifier = 1.5f;
        main.startColor = new Color(0.55f, 0.85f, 1f, 1f);   // 불티 텍스처가 따뜻한 색이라 파랑을 곱한다(레이저 에이스 불티는 주황)
        LaserFade(ps, Color.white, new Color(0.45f, 0.8f, 1f), 0.4f);
        return ps;
    }

    /// 닿은 자리의 빛(짧게 번쩍 — 연속으로 내면 타오르는 점처럼 보인다).
    private static ParticleSystem LaserBeamGlow(Transform parent)
    {
        ParticleSystem ps = LaserEmitOnly(parent, "BeamGlow", 0.45f, 400, new Color(0.7f, 0.9f, 1f, 1f), ACO_FxBuilder.FlashMaterial(), ParticleSystemRenderMode.Billboard);
        LaserFade(ps, Color.white, new Color(0.5f, 0.8f, 1f), 0.2f);
        return ps;
    }

    /// 그을림: 땅에 누운 검은 얼룩(10초 — 마지막 30%에 사라진다). 빔이 지나간 자리를 따라 0.1초마다 남아 띠가 된다.
    private static ParticleSystem LaserBeamScorch(Transform parent)
    {
        Material mat = LaserFxMaterial("ACO_LaserTower_Scorch", LaserScorchTexture(), false, 1f);
        ParticleSystem ps = LaserEmitOnly(parent, "BeamScorch", 10f, 2500, Color.white, mat, ParticleSystemRenderMode.HorizontalBillboard);
        ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 3f;
        LaserFade(ps, Color.white, Color.white, 0.7f);
        return ps;
    }

    // ───────────── 부위 ─────────────

    private static ACO_FortressPart LaserPart(Transform parent, string name, Vector3 pos, Vector3 boxCenter, Vector3 boxSize, float markerRadius, int markerLayer,
                                              float health, int score, string label, out GameObject visualRoot)
    {
        var root = new GameObject(name);
        root.layer = LayerHitbox;
        root.transform.SetParent(parent, false);
        root.transform.localPosition = pos;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = boxCenter;
        box.size = boxSize;
        box.enabled = false;
        visualRoot = new GameObject("Visual");
        visualRoot.layer = LayerHitbox;
        visualRoot.transform.SetParent(root.transform, false);
        GameObject smoke = CreateFortressSmoke(root.transform, "Smoke", boxCenter, Mathf.Max(boxSize.x, boxSize.z) * 0.6f, 40f);
        smoke.SetActive(false);
        GameObject marker = NewLockMarker(root.transform, PartMarkerName, markerLayer, markerRadius);
        marker.transform.localPosition = boxCenter;
        marker.SetActive(false);
        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        AddTargetAnimator(root);
        SaccTarget hp = root.AddUdonSharpComponent<SaccTarget>();
        ACO_FortressPart part = root.AddUdonSharpComponent<ACO_FortressPart>();
        hp.Health = health;
        hp.ArmorStrength = 1f;
        hp.RespawnDelay = 0f;
        hp.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { part };
        part.TargetHealth = hp;
        part.BaseHealth = health;
        part.ScoreValue = score;
        part.MarkerLabel = label;
        part.LockMarkers = new[] { marker };
        part.Hitboxes = new Collider[] { box };
        part.HideOnDeath = new[] { visualRoot };
        part.ShowOnDeath = new[] { smoke };
        return part;
    }

    private static void AddLaserHullBox(GameObject hull, Vector3 center, Vector3 size, List<Collider> cols)
    {
        BoxCollider c = hull.AddComponent<BoxCollider>();
        c.center = center;
        c.size = size;
        cols.Add(c);
    }

    // ───────────── 프리팹 ─────────────

    public static GameObject BuildLaserTower()
    {
        EnsurePrefabFolder();
        Dictionary<string, Material> mats = LaserMaterials();
        Material concrete = mats["ACO_LaserTower_Concrete"];

        var root = new GameObject(LaserTowerName);
        root.layer = LayerHitbox;

        // 코어(렌즈) 히트박스 — 3단계 과열 창에만 켠다(페이즈). SaccTarget 규칙상 루트에
        Vector3 lensCenter = LaserHeadPos + Vector3.up * LaserLensRise;
        BoxCollider coreCol = root.AddComponent<BoxCollider>();
        coreCol.center = lensCenter;
        coreCol.size = new Vector3(18f, 18f, 26f);
        coreCol.enabled = false;

        // 고정 모델
        // 기초판: 옆면 셰이드 스무스(사용자 "토대 옆면 셰이드 스무스")
        Mesh padMesh = SmoothPadMesh("ACO_LaserTower_Pad", 64f, new float[,] { { LaserPadRadius - 20f, LaserPadRadius, -LaserPadDepth, 0.2f, 48f } });
        AddFortressMesh(root.transform, "Visual_Pad", SaveFortressMesh(padMesh), concrete, Vector3.zero);
        LaserModel(root.transform, "Visual_Base", LaserTowerFbx, "LaserTower_Base", Vector3.zero, mats);
        LaserModel(root.transform, "Visual_Conduits", LaserTowerFbx, "LaserTower_Conduits", Vector3.zero, mats);
        for (int i = 0; i < 6; i++) { LaserModel(root.transform, "Visual_Aux" + (i + 1), LaserTowerFbx, "LaserTower_Aux" + (i + 1), LaserAuxPos(i), mats); }

        // 헤드(좌우) → 렌즈(위아래)
        var head = new GameObject("HeadPivot").transform;
        head.SetParent(root.transform, false);
        head.localPosition = LaserHeadPos;
        LaserModel(head, "Visual_Head", LaserTowerFbx, "LaserTower_Head", Vector3.zero, mats);
        var lens = new GameObject("LensPivot").transform;
        lens.SetParent(head, false);
        lens.localPosition = Vector3.up * LaserLensRise;
        LaserModel(lens, "Visual_Lens", LaserTowerFbx, "LaserTower_Lens", Vector3.zero, mats);
        Transform muzzle = NewChild(lens.gameObject, "Muzzle", new Vector3(0f, 0f, 12.5f));
        GameObject coreMarker = NewLockMarker(lens, "AAMTarget", LayerEnemyGround, 16f);
        coreMarker.SetActive(false);

        // 충돌: 자체 SaccTarget이 피해를 흡수한다(기초판 · 주탑 단 · 보조탑 · 헤드)
        var hull = new GameObject("Hull");
        hull.layer = LayerHitbox;
        hull.transform.SetParent(root.transform, false);
        var hullCols = new List<Collider>();
        float padBox = LaserPadRadius * 1.38f;   // 원에 내접하는 정사각형 둘(45° 엇갈림)
        AddLaserHullBox(hull, new Vector3(0f, -LaserPadDepth * 0.5f, 0f), new Vector3(padBox, LaserPadDepth, padBox), hullCols);   // 옆면까지 막는다
        var diag = new GameObject("PadDiagonal");
        diag.layer = LayerHitbox;
        diag.transform.SetParent(hull.transform, false);
        diag.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        AddLaserHullBox(diag, new Vector3(0f, -LaserPadDepth * 0.5f, 0f), new Vector3(padBox, LaserPadDepth, padBox), hullCols);
        for (float y = 0f; y < LaserHeadPos.y; y += 72f)
        {
            float y1 = Mathf.Min(y + 72f, LaserHeadPos.y);
            Vector2 h = LaserHalf(y);
            AddLaserHullBox(hull, new Vector3(0f, (y + y1) * 0.5f, 0f), new Vector3(h.x * 2f + 6f, y1 - y, h.y * 2f + 6f), hullCols);
        }
        AddLaserHullBox(hull, LaserHeadPos + Vector3.up * 3f, new Vector3(20f, 8f, 20f), hullCols);
        for (int i = 0; i < 6; i++)
        {
            Vector3 p = LaserAuxPos(i);
            var auxBox = new GameObject("Aux" + (i + 1));
            auxBox.layer = LayerHitbox;
            auxBox.transform.SetParent(hull.transform, false);
            auxBox.transform.localPosition = p;
            auxBox.transform.localRotation = Quaternion.Euler(0f, (Mathf.PI / 6f + i * Mathf.PI / 3f) * Mathf.Rad2Deg, 0f);
            for (int k = 0; k < 3; k++)
            {
                float y0 = k * LaserAuxHeight / 3f, y1 = (k + 1) * LaserAuxHeight / 3f;
                float half = 13f + (4.2f - 13f) * y0 / LaserAuxHeight;
                AddLaserHullBox(auxBox, new Vector3(0f, (y0 + y1) * 0.5f, 0f), new Vector3(half * 2f, y1 - y0, half * 2f), hullCols);
            }
        }
        AddTargetAnimator(hull);
        SaccTarget armor = hull.AddUdonSharpComponent<SaccTarget>();
        armor.Health = 1000000f;
        armor.RespawnDelay = 0f;
        armor.DisableBulletHitEvent = true;

        // 부위
        var parts = new GameObject("Parts").transform;
        parts.SetParent(root.transform, false);
        var partList = new List<UdonSharp.UdonSharpBehaviour>();
        var vents = new List<ACO_FortressPart>();
        var trucks = new List<ACO_FortressPart>();
        var sites = new List<Transform>();
        var airships = new List<ACO_FortressPart>();
        var mirrors = new List<Transform>();
        // 보조탑 끝 환풍구(정밀 표적)
        for (int i = 0; i < 6; i++)
        {
            Vector3 p = LaserAuxPos(i) + Vector3.up * LaserAuxHeight;
            ACO_FortressPart vent = LaserPart(parts, "Vent_" + (i + 1), p, new Vector3(0f, 3f, 0f), new Vector3(9f, 7f, 9f), 7f, LayerEnemyGround, LaserVentHealth, 800, "VNT", out GameObject vis);
            LaserModel(vis.transform, "Radiator", LaserTowerFbx, "LaserTower_Radiator" + (i + 1), Vector3.zero, mats);
            vent.SplashScale = LanceCoolerSplashScale;
            vent.JammedLabel = "JAMMED";   // 1단계(ECM 차량이 살아 있는 동안) 마커 글자 — 락온 · 피해는 없다
            partList.Add(vent);
            vents.Add(vent);
        }
        // 탑 둘레 방공: 보조탑 밑동 대공포 6 + 기단 둘레 VLS 2
        var defense = new List<UdonSharp.UdonSharpBehaviour>();
        for (int i = 0; i < 6; i++)
        {
            Vector3 p = LaserAuxPos(i) * ((LaserAuxRadius - 34f) / LaserAuxRadius) + Vector3.up * 0.2f;
            AddRailgunGun(parts, "Flak_" + (i + 1), TurretFlak, p, Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg, defense);
        }
        defense.Add(NewTurret(TurretVls, "VLS_1", parts, new Vector3(110f, 0.2f, -80f), Quaternion.identity, 3f).GetComponent<ACO_SAMSite>());
        defense.Add(NewTurret(TurretVls, "VLS_2", parts, new Vector3(-110f, 0.2f, 80f), Quaternion.identity, 3f).GetComponent<ACO_SAMSite>());
        partList.AddRange(defense);
        // ECM 기지 3곳(차량 + 둘레 방공) — 처음 자리는 탑에서 2km, 페이즈가 무작위 자리로 옮긴다
        for (int s = 0; s < 3; s++)
        {
            float a = s * Mathf.PI * 2f / 3f;
            var site = new GameObject("EcmSite_" + s).transform;
            site.SetParent(parts, false);
            site.localPosition = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * LaserEcmDistance;
            ACO_FortressPart truck = LaserPart(site, "EcmTruck", Vector3.zero, new Vector3(0f, 2.6f, 0f), new Vector3(4f, 5.2f, 15f), 9f, LayerEnemyGround, 900f, 900, "ECM", out GameObject tvis);
            LaserModel(tvis.transform, "Truck", LaserEcmFbx, "EcmTruck_Body", Vector3.zero, mats);
            ACO_EnemyJammer jam = truck.gameObject.AddUdonSharpComponent<ACO_EnemyJammer>();
            jam.Radius = 4000f;   // 사용자 "재머 크기를 4km 정도로" (처음 1.5km)
            jam.LockSpeedFactor = 0.5f;
            jam.Health = truck.TargetHealth;
            partList.Add(truck);
            trucks.Add(truck);
            sites.Add(site);
            // 기지 방공은 차량 체급(보통 지상 유닛)에 맞춰 작게 — 탑 둘레 포대(2.5 · 3배)와 달리 1.2배
            for (int k = 0; k < 3; k++)
            {
                float b = k * Mathf.PI * 2f / 3f + 0.4f;
                Vector3 p = new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b)) * LaserSiteFlakRadius;   // 넓게(UGB 범위 200m — 한 발에 다 쓸리지 않게)
                ACO_AAGunSite gun = NewTurret(TurretFlak, "Flak_" + k, site, p, Quaternion.Euler(0f, b * Mathf.Rad2Deg, 0f), LaserSiteTurretScale).GetComponent<ACO_AAGunSite>();
                partList.Add(gun);
            }
            partList.Add(NewTurret(TurretVls, "VLS", site, LaserSiteVlsPos, Quaternion.identity, LaserSiteTurretScale).GetComponent<ACO_SAMSite>());
            partList.Add(NewTurret(TurretCiws, "CIWS", site, LaserSiteCiwsPos, Quaternion.identity, LaserSiteTurretScale).GetComponent<ACO_AAGunSite>());
        }
        // 반사판 비행선(공중 락온) — 처음 자리는 궤도 위, 페이즈가 옮긴다
        for (int i = 0; i < LaserAirshipCount; i++)
        {
            float a = i * Mathf.PI * 2f / LaserAirshipCount;
            Vector3 p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * LaserAirshipOrbit + Vector3.up * LaserAirshipAltitude;
            ACO_FortressPart ship = LaserPart(parts, "Airship_" + i, p, new Vector3(0f, -6f, 0f), new Vector3(32f, 44f, 130f), 45f, LayerEnemyAir, LaserAirshipHealth, 700, "RFL", out GameObject svis);
            ship.transform.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f, 0f);
            ship.transform.localScale = Vector3.one * LaserAirshipScale;   // 모델 · 히트박스 · 락온 마커 · 연기가 함께 줄어든다
            ship.HideOnDeath = new GameObject[0];   // 격추되면 기울며 떨어지는 모습을 보인다 — 숨기기(땅속으로)는 ACO_LaserTower가 낙하 뒤에
            LaserModel(svis.transform, "Hull", LaserAirshipFbx, "Airship_Hull", Vector3.zero, mats);
            mirrors.Add(LaserModel(svis.transform, "Mirror", LaserAirshipFbx, "Airship_Mirror", new Vector3(0f, -22.6f, 4f), mats).transform);
            partList.Add(ship);
            airships.Add(ship);
        }

        // 빔(6-331 ④-2): 패턴 A 하나 + 그물 갈래 — 월드 좌표, 스크립트가 폭 · 색을 바꾼다
        var beams = new GameObject("Beams").transform;
        beams.SetParent(root.transform, false);
        LineRenderer beamA = LunaBeam(beams, "BeamA", LaserBeamCore, LaserBeamEdge, 4f);
        var netLines = new LineRenderer[LaserNetLines];
        for (int i = 0; i < LaserNetLines; i++) { netLines[i] = LunaBeam(beams, "Net_" + i, LaserBeamCore, LaserBeamEdge, 3f); }
        GameObject overheat = LaserOverheatShell(lens);
        ParticleSystem sparks = LaserBeamSparks(beams);
        ParticleSystem glow = LaserBeamGlow(beams);
        ParticleSystem scorch = LaserBeamScorch(beams);

        // 격파 연출
        var deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        CreateFortressSmoke(deathFx.transform, "Fire_Lens", lensCenter, 30f, 100f);
        deathFx.SetActive(false);
        var effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        ACO_FortressBlast blast = CreateFortressBlast(root.transform);

        AddTargetAnimator(root);
        SaccTarget core = root.AddUdonSharpComponent<SaccTarget>();
        ACO_Warship ship2 = root.AddUdonSharpComponent<ACO_Warship>();
        ACO_LaserTower tower = root.AddUdonSharpComponent<ACO_LaserTower>();
        core.Health = LaserLensHealth;
        core.ArmorStrength = 1f;
        core.RespawnDelay = 0f;
        core.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { ship2, tower };
        ship2.TargetHealth = core;
        ship2.Parts = partList.ToArray();
        ship2.TargetName = "Caliburn";   // 병기 이름(사용자 "병기 이름은 칼리번으로 하자")
        ship2.MarkerLabel = "CLB";
        ship2.ScoreValue = 6000;
        ship2.HighValue = true;
        ship2.PartHullDamage = 0f;
        ship2.CoreSplashImmune = true;
        ship2.CruiseSpeed = 0f;
        ship2.CruiseRadius = 0f;
        ship2.BobAngle = 0f;
        ship2.ExternalPose = true;
        ship2.PoseOwner = tower;
        ship2.ShowOnDeath = new[] { deathFx };
        ship2.HideOnDeath = new GameObject[0];
        hullCols.Add(coreCol);
        ship2.DisableCollidersOnDeath = hullCols.ToArray();

        tower.Warship = ship2;
        tower.TargetHealth = core;
        tower.EcmTrucks = trucks.ToArray();
        tower.EcmSites = sites.ToArray();
        tower.Vents = vents.ToArray();
        tower.Airships = airships.ToArray();
        tower.Mirrors = mirrors.ToArray();
        tower.TowerDefense = defense.ToArray();
        tower.HeadPivot = head;
        tower.LensPivot = lens;
        tower.Muzzle = muzzle;
        tower.CoreHitbox = coreCol;
        tower.CoreMarkers = new[] { coreMarker };
        tower.Blast = blast;
        tower.BeamA = beamA;
        tower.NetLines = netLines;
        tower.NetBranches = LaserNetBranches;
        tower.OverheatGlow = overheat;
        tower.HitSparks = sparks;
        tower.HitGlow = glow;
        tower.Scorch = scorch;
        tower.ChargeSound = LaserSound(lens, "ChargeSound", "Caliburn_Charge", 1f);   // 사용자 고른 충전음(샘플 충전 B)
        tower.FireSound = LaserSound(lens, "FireSound", "Caliburn_Fire", 1f);         // 발사음(샘플 충전 C — 사용자 "발사음은 충전음에서 C가 낫겠다")

        return SaveAndCleanup(root, $"{PrefabDir}/{LaserTowerName}.prefab");
    }
}
