using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 공중 요새 NPC_Fortress: 모델은 Assets/ARCANGEL의 archangel 프리팹(중첩 인스턴스 "Model")이다 — 격납고가 있는 대형 비행 함선.
/// 구조(루트 = 함선 규칙 — 코어 SaccTarget·히트박스·Animator는 루트):
///  - Model / Hull(충돌: archangelcollidor 메시 + 후면 해치 막이 — 자체 SaccTarget으로 피해를 흡수해 코어로 번지지 않는다)
///  - Parts(방공 18곳 = 공용 포대 프리팹, 자리는 ACO_TurretModels의 FortressSites. 엔진 4 = 후면 노즐 묶음) / Core(격납고 안의 세운 원자로 — 코어 단계에 후면 해치가 열린다)
///  - 요새는 공중 표적이다 — 락온 마커를 모두 적 항공기 레이어(29)로 옮겨 공대공 무장만 잠긴다.
public static partial class ACO_PrefabBuilder
{
    private const string FortressMatDir = "Assets/ACO_Addon/Generated";

    private const string ArchangelPrefabPath = "Assets/ARCANGEL/model/archangel.prefab";
    private const string ArchangelColliderPath = "Assets/ARCANGEL/model/archangelcollidor.fbx";
    /// 모델 크기 배율. 원본은 길이 1490m, 폭 820m. 부위 자리는 이 값을 따라간다.
    private const float FortressScale = 0.6f;
    private const float ArchangelCenterZ = 1.7f;
    /// 외피 레이어(Environment): 무장·락온·포대 시야·NPC 회피가 지형처럼 막힌다. 코어는 격납고 입구로 정확히 겨눠야만 잠기고 맞는다.
    /// 지형은 Default만 쓰므로, 땅을 재는 쪽(생성기·지상·호위 유닛)은 Bake가 이 레이어를 뺀다 — 요새 밑의 전차가 선체 위로 올라가지 않게.
    public const int LayerFortressHull = 11;

    /// 모델 좌표(archangel.fbx 기준: x 앞, y 위, z 옆) → 요새 로컬 좌표(z 앞). 앞뒤 가운데가 원점, 격납고 높이 가운데가 높이 0.
    private static Vector3 FortressPoint(float x, float y, float z)
    {
        return new Vector3(ArchangelCenterZ - z, y - 12f, x + 107f) * FortressScale;
    }

    private class MeshBag
    {
        public readonly List<Vector3> Verts = new List<Vector3>();
        public readonly List<int> Tris = new List<int>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        /// 0보다 크면 UV를 만든다: 면이 향한 축으로 평면 투영, 이 길이(m)마다 텍스처 한 장.
        public float UvScale = 0f;

        /// 사각형 하나(정점을 따로 둬 면이 각진다). outFrom에서 면 쪽으로 나가는 방향이 앞면이 되게 감는다.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outFrom)
        {
            // 대각선 외적 — a == b인 삼각형 꼴에서도 0이 아니다. Unity 삼각형 (a, b, c)의 앞면 노멀 = Cross(b - a, c - a)
            Vector3 n = Vector3.Cross(c - a, d - b);
            bool forward = Vector3.Dot(n, (a + b + c + d) * 0.25f - outFrom) >= 0f;
            int s = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c); Verts.Add(d);
            if (UvScale > 0f)
            {
                Vector3 m = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                foreach (Vector3 v in new[] { a, b, c, d })
                {
                    if (m.y >= m.x && m.y >= m.z) { Uvs.Add(new Vector2(v.x, v.z) / UvScale); }
                    else if (m.x >= m.z) { Uvs.Add(new Vector2(v.z, v.y) / UvScale); }
                    else { Uvs.Add(new Vector2(v.x, v.y) / UvScale); }
                }
            }
            if (forward) { Tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 }); }
            else { Tris.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 }); }
        }

        /// 여덟 꼭짓점 육면체: 0123 = 앞면(z+), 4567 = 뒷면, 같은 순서.
        public void Hexa(Vector3[] p)
        {
            Vector3 c = Vector3.zero;
            foreach (Vector3 v in p) { c += v; }
            c /= 8f;
            Quad(p[0], p[1], p[2], p[3], c);
            Quad(p[4], p[5], p[6], p[7], c);
            Quad(p[0], p[1], p[5], p[4], c);
            Quad(p[1], p[2], p[6], p[5], c);
            Quad(p[2], p[3], p[7], p[6], c);
            Quad(p[3], p[0], p[4], p[7], c);
        }

        public void Box(Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Hexa(new[]
            {
                center + new Vector3(-h.x, -h.y, h.z), center + new Vector3(h.x, -h.y, h.z), center + new Vector3(h.x, h.y, h.z), center + new Vector3(-h.x, h.y, h.z),
                center + new Vector3(-h.x, -h.y, -h.z), center + new Vector3(h.x, -h.y, -h.z), center + new Vector3(h.x, h.y, -h.z), center + new Vector3(-h.x, h.y, -h.z),
            });
        }

        /// z축 원통(앞 반지름 r1, 뒤 반지름 r2). 앞뒤 막음.
        public void Cylinder(Vector3 center, float r1, float r2, float length, int segments)
        {
            Vector3 f = center + Vector3.forward * (length * 0.5f);
            Vector3 b = center - Vector3.forward * (length * 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                Vector3 d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                Quad(f + d0 * r1, f + d1 * r1, b + d1 * r2, b + d0 * r2, center);
                Quad(f, f, f + d1 * r1, f + d0 * r1, center - Vector3.forward * length);
                Quad(b, b, b + d1 * r2, b + d0 * r2, center + Vector3.forward * length);
            }
        }

        /// y축 각기둥(위 반지름 rTop, 아래 반지름 rBottom, 높이 yBottom~yTop). 윗면 막음.
        public void Prism(Vector3 center, float rTop, float rBottom, float yBottom, float yTop, int segments)
        {
            Vector3 t = center + Vector3.up * yTop;
            Vector3 b = center + Vector3.up * yBottom;
            Vector3 inside = center + Vector3.up * ((yTop + yBottom) * 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Quad(t + d0 * rTop, t + d1 * rTop, b + d1 * rBottom, b + d0 * rBottom, inside);
                Quad(t, t, t + d1 * rTop, t + d0 * rTop, b);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (Verts.Count > 65000) { mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
            mesh.SetVertices(Verts);
            if (UvScale > 0f) { mesh.SetUVs(0, Uvs); }
            mesh.SetTriangles(Tris, 0);
            mesh.RecalculateNormals();
            if (UvScale > 0f) { mesh.RecalculateTangents(); }
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    private static Mesh SaveFortressMesh(Mesh mesh)
    {
        string path = $"{FortressMatDir}/{mesh.name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static Material FortressMaterial(string name, Color color, float metallic, float smoothness, Color emission)
    {
        string path = $"{FortressMatDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) { return m; }
        m = new Material(Shader.Find("Standard")) { name = name };
        m.SetColor("_Color", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static GameObject AddFortressMesh(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos)
    {
        var go = new GameObject(name);
        go.layer = LayerHitbox;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        return go;
    }

    /// 처음에 꺼 두는 부위(엔진·냉각기)의 락온 마커 이름. "AAMTarget"이면 안 된다: SAV Target 애니메이터가 그 이름의 활성 상태를 다루고
    /// (TargetExplode 클립, Write Defaults 켜짐) 시작할 때 값(꺼짐)을 매 프레임 되돌려 켜도 바로 꺼진다 — 락온이 안 된다.
    private const string PartMarkerName = "LockMarker";

    private static GameObject NewLockMarker(Transform parent, string name, int layer, float radius)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        SphereCollider c = go.AddComponent<SphereCollider>();
        c.isTrigger = true;
        c.radius = radius;
        return go;
    }

    private static GunSpec FortressFlakSpec()
    {
        GunSpec s = MainGunSpec();
        s.Health = 300f;
        s.Armor = 1f;
        s.Score = 300;
        s.Label = "FLK";
        s.YawRate = 40f;
        s.PitchRate = 30f;
        return s;
    }

    private static GunSpec FortressCiwsSpec()
    {
        GunSpec s = CiwsSpec();
        s.Health = 150f;
        s.Score = 200;
        return s;
    }

    /// 포대 한 곳. surface = 모델 표면 자리, rotation = 표면에 맞춘 자세.
    private static void AddFortressGun(Transform parts, string prefab, string name, Vector3 surface, Quaternion rotation, float scale, List<UdonSharp.UdonSharpBehaviour> list)
    {
        GameObject part = NewTurret(prefab, name, parts, surface, rotation, scale);
        ACO_AAGunSite aa = part.GetComponent<ACO_AAGunSite>();
        SetFortressPitch(aa);
        SetAirTarget(part);
        list.Add(aa);
    }

    private static void AddFortressVls(Transform parts, string name, Vector3 surface, Quaternion rotation, float scale, List<UdonSharp.UdonSharpBehaviour> list)
    {
        GameObject part = NewTurret(TurretVls, name, parts, surface, rotation, scale);
        SetAirTarget(part);
        list.Add(part.GetComponent<ACO_SAMSite>());
    }

    /// 포대의 락온 마커(지상 레이어)를 적 항공기 레이어로 옮긴다 — 공대공 무장만 잠긴다.
    private static void SetAirTarget(GameObject part)
    {
        GameObject marker = part.transform.Find("AAMTarget").gameObject;
        marker.layer = LayerEnemyAir;
        PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
    }

    /// 좌우 대칭 한 쌍.
    // 후면 노즐 한쪽 묶음(모델 좌표 y, z — +z 쪽 값, 반대쪽은 ArchangelCenterZ 기준 대칭). 안쪽 4개·바깥쪽 5개가 엔진 하나씩이다.
    private static readonly float[,] NozzlesInner = { { -4.5f, 116f }, { -9.5f, 103.5f }, { -23f, 117.5f }, { 1.5f, 128.5f } };
    private static readonly float[,] NozzlesOuter = { { -17.5f, 130f }, { -12f, 142.5f }, { -25.5f, 156.5f }, { -31f, 144f }, { -36.5f, 131.5f } };
    private const float NozzleX = -551f;

    /// 엔진 하나: 모델의 노즐 묶음에 히트박스·불꽃·연기·락온 마커를 얹는다(모델은 Archangel의 것). side = +1(+z 쪽), -1(반대쪽).
    private static ACO_FortressPart AddFortressEngine(Transform parts, string name, float[,] nozzles, float side, Material flameMat)
    {
        int n = nozzles.GetLength(0);
        var points = new Vector3[n];
        Vector3 center = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            float z = ArchangelCenterZ + (nozzles[i, 1] - ArchangelCenterZ) * side;
            points[i] = FortressPoint(NozzleX, nozzles[i, 0], z);
            center += points[i];
        }
        center /= n;

        var root = new GameObject(name);
        root.layer = LayerHitbox;
        root.transform.SetParent(parts, false);
        root.transform.localPosition = center;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(30f, 30f, 44f);
        box.center = new Vector3(0f, 0f, 8f);
        box.enabled = false;

        var flames = new GameObject("Flames");
        flames.transform.SetParent(root.transform, false);
        for (int i = 0; i < n; i++)
        {
            GameObject flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flame.name = "Flame_" + i;
            flame.layer = LayerHitbox;
            Object.DestroyImmediate(flame.GetComponent<Collider>());
            flame.transform.SetParent(flames.transform, false);
            flame.transform.localPosition = points[i] - center + Vector3.back * (10f * FortressScale);
            flame.transform.localScale = new Vector3(11f, 11f, 30f) * FortressScale;
            flame.GetComponent<MeshRenderer>().sharedMaterial = flameMat;
        }

        GameObject smoke = CreateFortressSmoke(root.transform, "Smoke", new Vector3(0f, 0f, -10f), 22f, 60f);
        smoke.SetActive(false);

        GameObject marker = NewLockMarker(root.transform, PartMarkerName, LayerEnemyAir, 12f);
        marker.SetActive(false);

        GameObject effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);

        AddTargetAnimator(root);
        SaccTarget health = root.AddUdonSharpComponent<SaccTarget>();
        ACO_FortressPart part = root.AddUdonSharpComponent<ACO_FortressPart>();
        health.Health = 1200f;   // 공대공 직격 200 기준 6발
        health.ArmorStrength = 1f;
        health.RespawnDelay = 0f;
        health.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { part };
        part.TargetHealth = health;
        part.ScoreValue = 800;
        part.MarkerLabel = "ENG";
        part.LockMarkers = new[] { marker };
        part.Hitboxes = new Collider[] { box };
        part.HideOnDeath = new[] { flames };
        part.ShowOnDeath = new[] { smoke };
        return part;
    }

    /// 검은 연기·불 — 켜져 있는 동안 나온다(부서진 엔진·격추).
    private static GameObject CreateFortressSmoke(Transform parent, string name, Vector3 localPos, float size, float rate)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 16f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.4f);
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = rate * 0.2f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = size;
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0f), new GradientColorKey(new Color(0.12f, 0.11f, 0.1f), 0.2f), new GradientColorKey(new Color(0.08f, 0.08f, 0.08f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);
        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        Material mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        if (mat) { renderer.sharedMaterial = mat; }
        return go;
    }

    /// 폭발용 파티클 하나: 한 번에 Emit으로 내보낸다(월드 좌표, Hierarchy 크기). 색은 시간에 따라 c0 → c1, 투명도 a0 → 0.
    private static ParticleSystem CreateBlastSystem(Transform parent, string name, Vector2 life, Vector2 speed, Vector2 size, float gravity, int max, Color c0, Color c1, float a0)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = gravity;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 0.6f), new GradientColorKey(c1, 1f) },
            new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a0 * 0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);
        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        Material mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        if (mat) { renderer.sharedMaterial = mat; }
        renderer.maxParticleSize = 8f;
        return ps;
    }

    private static void SetBlastShape(ParticleSystem ps, ParticleSystemShapeType type, float radius, float angle = 0f)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = type;
        shape.radius = radius;
        shape.angle = angle;
        if (type == ParticleSystemShapeType.Cone || type == ParticleSystemShapeType.Circle) { ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); }
    }

    private static AudioSource CreateBlastSound(Transform parent, string name, string clipPath, float minDistance, float maxDistance)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        AudioSource a = go.AddComponent<AudioSource>();
        a.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
        a.playOnAwake = false;
        a.spatialBlend = 1f;
        a.dopplerLevel = 0f;
        a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = minDistance;
        a.maxDistance = maxDistance;
        a.priority = 16;
        return a;
    }

    /// 대폭발 오브젝트. 처음 터질 때 요새에서 떨어져 나간다(ACO_FortressBlast).
    private static ACO_FortressBlast CreateFortressBlast(Transform parent)
    {
        var root = new GameObject("FortressBlast");
        root.transform.SetParent(parent, false);
        ACO_FortressBlast blast = root.AddUdonSharpComponent<ACO_FortressBlast>();
        Color white = new Color(1f, 0.97f, 0.9f);
        Color orange = new Color(1f, 0.5f, 0.12f);
        Color dark = new Color(0.1f, 0.09f, 0.09f);
        Color spray = new Color(0.86f, 0.92f, 0.96f);

        blast.Fire = CreateBlastSystem(root.transform, "Fire", new Vector2(2.5f, 6f), new Vector2(30f, 160f), new Vector2(150f, 420f), -0.01f, 80, orange, dark, 1f);
        SetBlastShape(blast.Fire, ParticleSystemShapeType.Sphere, 120f);
        blast.Debris = CreateBlastSystem(root.transform, "Debris", new Vector2(5f, 9f), new Vector2(150f, 420f), new Vector2(6f, 20f), 1f, 150, dark, dark, 1f);
        SetBlastShape(blast.Debris, ParticleSystemShapeType.Cone, 80f, 60f);
        blast.Smoke = CreateBlastSystem(root.transform, "Smoke", new Vector2(18f, 30f), new Vector2(30f, 70f), new Vector2(300f, 700f), -0.002f, 90, dark, new Color(0.2f, 0.19f, 0.18f), 0.85f);
        SetBlastShape(blast.Smoke, ParticleSystemShapeType.Cone, 200f, 12f);
        ParticleSystem.MainModule smokeMain = blast.Smoke.main;
        smokeMain.duration = 10f;
        ParticleSystem.EmissionModule smokeEmission = blast.Smoke.emission;
        smokeEmission.rateOverTime = 6f;
        blast.WaterColumn = CreateBlastSystem(root.transform, "WaterColumn", new Vector2(5f, 9f), new Vector2(180f, 420f), new Vector2(60f, 180f), 0.9f, 150, spray, spray, 0.95f);
        SetBlastShape(blast.WaterColumn, ParticleSystemShapeType.Cone, 120f, 12f);
        blast.Mist = CreateBlastSystem(root.transform, "Mist", new Vector2(6f, 12f), new Vector2(30f, 120f), new Vector2(200f, 450f), 0f, 60, spray, spray, 0.6f);
        SetBlastShape(blast.Mist, ParticleSystemShapeType.Circle, 150f);
        blast.Flash = CreateBlastSystem(root.transform, "Flash", new Vector2(0.35f, 0.35f), Vector2.zero, new Vector2(1800f, 1800f), 0f, 2, white, white, 1f);
        SetBlastShape(blast.Flash, ParticleSystemShapeType.Sphere, 0.1f);
        blast.Shock = CreateBlastSystem(root.transform, "Shock", new Vector2(3f, 3f), Vector2.zero, new Vector2(3000f, 3000f), 0f, 2, white, new Color(0.6f, 0.6f, 0.62f), 0.5f);
        SetBlastShape(blast.Shock, ParticleSystemShapeType.Sphere, 0.1f);
        ParticleSystem.SizeOverLifetimeModule shockSize = blast.Shock.sizeOverLifetime;
        shockSize.enabled = true;
        shockSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.02f, 1f, 1f));
        ParticleSystemRenderer shockRenderer = blast.Shock.GetComponent<ParticleSystemRenderer>();
        shockRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        shockRenderer.maxParticleSize = 100f;
        blast.Secondary = CreateBlastSystem(root.transform, "Secondary", new Vector2(1f, 2.5f), new Vector2(10f, 40f), new Vector2(50f, 120f), 0f, 60, orange, dark, 1f);
        SetBlastShape(blast.Secondary, ParticleSystemShapeType.Sphere, 20f);

        var lightGo = new GameObject("FlashLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 200f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 6000f;
        light.color = new Color(1f, 0.85f, 0.65f);
        light.shadows = LightShadows.None;
        light.enabled = false;
        blast.FlashLight = light;

        blast.Boom = CreateBlastSound(root.transform, "Boom", "Assets/ACO_Addon/Audio/Generated/ACO_Explosion_Big.wav", 3000f, 40000f);
        blast.SecondarySound = CreateBlastSound(root.transform, "SecondarySound", "Assets/ACO_Addon/Audio/Generated/ACO_Explosion_Small.wav", 300f, 8000f);
        return blast;
    }

    private static Mesh LoadArchangelCollider()
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ArchangelColliderPath))
        {
            if (o is Mesh mesh) { return mesh; }
        }
        return null;
    }

    /// 루트 축 기준 크기를 축이 90도 단위로 돌아간 자식의 로컬 크기로 바꾼다.
    private static Vector3 LocalBoxSize(Transform local, Vector3 rootSize)
    {
        Vector3 x = local.InverseTransformVector(new Vector3(rootSize.x, 0f, 0f));
        Vector3 y = local.InverseTransformVector(new Vector3(0f, rootSize.y, 0f));
        Vector3 z = local.InverseTransformVector(new Vector3(0f, 0f, rootSize.z));
        return new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
    }

    /// 측면 해치 출격 자리(ExportHatch 문 가운데, 모델 x 93 · y 2 · z ±103): 앞쪽 바깥으로 45도 나간다. 프리팹에서 옮겨도 된다.
    /// 해치는 엔진 단계부터 열려 있다 — 첫 증원이 엔진 단계에 나온다.
    private static void AddFortressLaunchPoints(Transform root, ACO_FlyingFortress fortress)
    {
        fortress.HatchOpenStage = new[] { ACO_FlyingFortress.STAGE_CORE, ACO_FlyingFortress.STAGE_ENGINES, ACO_FlyingFortress.STAGE_ENGINES };
        var points = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;   // 모델 +z가 요새의 왼쪽이다
            var go = new GameObject(i == 0 ? "Launch_L" : "Launch_R");
            go.transform.SetParent(root, false);
            go.transform.localPosition = FortressPoint(93f, 2f, ArchangelCenterZ + 103f * side);
            go.transform.localRotation = Quaternion.Euler(0f, -45f * side, 0f);
            points[i] = go.transform;
        }
        fortress.LaunchPoints = points;
    }

    public static GameObject BuildFortress()
    {
        EnsurePrefabFolder();
        string path = $"{PrefabDir}/NPC_Fortress.prefab";
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ArchangelPrefabPath);
        Mesh colliderMesh = LoadArchangelCollider();
        if (modelAsset == null || colliderMesh == null)
        {
            Debug.LogError($"[ACO] 공중 요새: 모델을 찾지 못했습니다({ArchangelPrefabPath}, {ArchangelColliderPath}). 있는 프리팹을 그대로 둡니다.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        Material flameMat = FortressMaterial("ACO_FortressFlame", Color.black, 0f, 0f, new Color(0.5f, 1.4f, 3f));
        Material coreMat = FortressMaterial("ACO_FortressCore", new Color(0.1f, 0f, 0f), 0f, 0.8f, new Color(4f, 0.35f, 0.15f));

        var root = new GameObject("NPC_Fortress");
        root.layer = LayerHitbox;
        // 격납고 한가운데: 후면 해치 통로의 축 위라 해치가 열리면 뒤에서 보인다
        Vector3 corePos = FortressPoint(-250f, 12f, ArchangelCenterZ);

        // 코어 히트박스(드러나기 전엔 꺼 둔다). SaccTarget 규칙상 루트에.
        BoxCollider coreCol = root.AddComponent<BoxCollider>();
        coreCol.size = new Vector3(140f, 34f, 140f) * FortressScale;
        coreCol.center = corePos;
        coreCol.enabled = false;

        // 모델: 원본은 기수가 +X — 요새는 +Z가 앞이다
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
        model.name = "Model";
        model.transform.localPosition = FortressPoint(0f, 0f, 0f);
        model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        model.transform.localScale = Vector3.one * FortressScale;
        PrefabUtility.RecordPrefabInstancePropertyModifications(model);
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);

        // 선체 충돌: 자체 SaccTarget이 무장 피해를 흡수한다(콜라이더가 루트 아래면 GetComponentInParent로 코어가 맞는다).
        // 충돌 메시는 격납고가 뚫려 있고 fuselage와 같은 좌표계다. 해치는 없으므로 닫혀 있는 동안 상자로 막는다.
        Transform fuselage = model.transform.Find("fuselage");
        var hull = new GameObject("Hull");
        hull.layer = LayerFortressHull;
        hull.transform.SetParent(root.transform, false);
        hull.transform.SetPositionAndRotation(fuselage.position, fuselage.rotation);
        hull.transform.localScale = Vector3.one * FortressScale;
        MeshCollider hullMesh = hull.AddComponent<MeshCollider>();
        hullMesh.sharedMesh = colliderMesh;
        BoxCollider hatchPlug = hull.AddComponent<BoxCollider>();
        hatchPlug.center = hull.transform.InverseTransformPoint(root.transform.TransformPoint(FortressPoint(-560f, 12f, ArchangelCenterZ)));
        hatchPlug.size = LocalBoxSize(hull.transform, new Vector3(90f, 44f, 30f) * FortressScale);
        var hullCols = new List<Collider> { hullMesh, hatchPlug };
        AddTargetAnimator(hull);
        SaccTarget armor = hull.AddUdonSharpComponent<SaccTarget>();
        armor.Health = 1000000f;
        armor.RespawnDelay = 0f;
        armor.DisableBulletHitEvent = true;   // 선체에 맞은 기총탄마다 피해 이벤트를 보내지 않게

        // 방공 18곳: 윗면·배 표면 높이는 모델에서 잰 값이다
        var body = new GameObject("Parts").transform;
        body.SetParent(root.transform, false);
        var defense = new List<UdonSharp.UdonSharpBehaviour>();
        foreach (FortressSite site in FortressSites)
        {
            Vector3 point = FortressPoint(site.Model.x, site.Model.y, site.Model.z);
            if (site.Prefab == TurretVls) { AddFortressVls(body, site.Name, point, FortressSiteRotation(site), FortressVlsScale, defense); }
            else { AddFortressGun(body, site.Prefab, site.Name, point, FortressSiteRotation(site), FortressGunScale, defense); }
        }

        // 엔진: 왼쪽·오른쪽 번갈아(ACO_FlyingFortress가 칸 번호로 기우는 쪽을 정한다). 모델 +z가 요새의 왼쪽이다
        var engines = new[]
        {
            AddFortressEngine(body, "Engine_L_Outer", NozzlesOuter, 1f, flameMat),
            AddFortressEngine(body, "Engine_R_Outer", NozzlesOuter, -1f, flameMat),
            AddFortressEngine(body, "Engine_L_Inner", NozzlesInner, 1f, flameMat),
            AddFortressEngine(body, "Engine_R_Inner", NozzlesInner, -1f, flameMat),
        };
        var partList = new List<UdonSharp.UdonSharpBehaviour>(defense);
        partList.AddRange(engines);

        // 코어: 격납고 안의 빛나는 구체
        var coreRoot = new GameObject("Core").transform;
        coreRoot.SetParent(root.transform, false);
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "CoreSphere";
        sphere.layer = LayerHitbox;
        Object.DestroyImmediate(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(coreRoot, false);
        sphere.transform.localPosition = corePos;
        sphere.transform.localScale = Vector3.one * (30f * FortressScale);
        sphere.GetComponent<MeshRenderer>().sharedMaterial = coreMat;
        GameObject coreMarker = NewLockMarker(coreRoot, "AAMTarget", LayerEnemyAir, 22f);
        coreMarker.transform.localPosition = corePos;
        coreMarker.SetActive(false);

        // SAV 격침 애니메이션이 "Effects"를 켠다. 그 밖의 연출은 DeathEffects.
        var effects = new GameObject("Effects");
        effects.transform.SetParent(root.transform, false);
        effects.SetActive(false);
        var deathFx = new GameObject("DeathEffects");
        deathFx.transform.SetParent(root.transform, false);
        CreateFortressSmoke(deathFx.transform, "Fire_Core", corePos, 40f, 120f);
        CreateFortressSmoke(deathFx.transform, "Fire_L", FortressPoint(-280f, 30f, ArchangelCenterZ + 170f), 40f, 120f);
        CreateFortressSmoke(deathFx.transform, "Fire_R", FortressPoint(-280f, 30f, ArchangelCenterZ - 170f), 40f, 120f);
        deathFx.SetActive(false);

        AddTargetAnimator(root);
        SaccTarget core = root.AddUdonSharpComponent<SaccTarget>();
        ACO_Warship ship = root.AddUdonSharpComponent<ACO_Warship>();
        ACO_FlyingFortress fortress = root.AddUdonSharpComponent<ACO_FlyingFortress>();

        core.Health = 4000f;   // 공대공 직격 200 기준 20발 — 4명이 돌진 시간(110초) 안에 끝낼 수 있게
        core.ArmorStrength = 1f;
        core.RespawnDelay = 0f;
        core.ExplodeOther = new UdonSharp.UdonSharpBehaviour[] { ship, fortress };

        ship.TargetHealth = core;
        ship.Parts = partList.ToArray();
        ship.TargetName = "Flying Fortress";
        ship.MarkerLabel = "FORT";
        ship.ScoreValue = 5000;
        ship.HighValue = true;
        ship.PartHullDamage = 0f;
        ship.CruiseSpeed = 0f;
        ship.BobAngle = 0f;
        ship.ExternalPose = true;
        ship.PoseOwner = fortress;
        ship.ShowOnDeath = new[] { deathFx };
        ship.HideOnDeath = new GameObject[0];
        hullCols.Add(coreCol);
        ship.DisableCollidersOnDeath = hullCols.ToArray();

        fortress.Warship = ship;
        fortress.TargetHealth = core;
        fortress.AirDefense = defense.ToArray();
        fortress.Engines = engines;
        fortress.CoreHitbox = coreCol;
        fortress.CoreMarkers = new[] { coreMarker };
        fortress.CoreShutters = new GameObject[0];
        fortress.Blast = CreateFortressBlast(root.transform);

        // 격납고 문: 닫힘 = 모델 기본 자세, 열림 = ARCHANGEL_hub 프리팹의 열린 자세
        Transform armature = model.transform.Find("Armature");
        fortress.Hatches = new[] { armature.Find("backHatch"), armature.Find("ExportHatch_L"), armature.Find("ExportHatch_R") };
        fortress.HatchClosed = new[] { fortress.Hatches[0].localRotation, fortress.Hatches[1].localRotation, fortress.Hatches[2].localRotation };
        fortress.HatchOpen = new[]
        {
            new Quaternion(0.4304593f, -0.43045932f, 0.56098557f, 0.56098557f),
            new Quaternion(0.01574087f, -0.062461726f, 0.078369014f, 0.9948412f),
            new Quaternion(0.014747916f, 0.043481644f, -0.07821297f, 0.9958788f),
        };
        fortress.HatchBlockers = new Collider[] { hatchPlug };
        hullCols.Add(hatchPlug);   // 추락 충돌이 자기 해치 막이에 걸리지 않게 격침 때 같이 끈다
        ship.DisableCollidersOnDeath = hullCols.ToArray();
        AddFortressLaunchPoints(root.transform, fortress);
        ApplyFortressCore(root);

        return SaveAndCleanup(root, path);
    }
}
