using System.Collections.Generic;
using ACO;
using SaccFlightAndVehicles;
using TMPro;
using UdonSharpEditor;
using UnityEngine;

/// F형 뒷좌석 화면(6-346): 설치 도구(ACO_AirframeInstaller)가 E형 프리팹의 기체(SaccEntity) 아래에 만든다 — F형 부품 묶음에 들어간다.
/// 화면 자리 = F형 조종석(cockpit.F) 뒷좌석 계기판 유리면(기체 좌표, 앞면 −Z = 뒷좌석 쪽 — 메시에서 잰 값). 글자 · 표시는 유리면 2~3mm 앞.
public partial class ACO_HUDBuilder
{
    public const string RearCockpitName = "ACO_RearCockpit";

    // 유리면 가운데 · 크기(m) — cockpit.F
    private static readonly Vector3 RearMainCenter = new Vector3(0f, 2.9095f, 3.335f);
    private static readonly Vector2 RearMainSize = new Vector2(0.19f, 0.279f);
    private static readonly Vector3 RearLeftCenter = new Vector3(-0.2135f, 2.8945f, 3.342f);
    private static readonly Vector3 RearRightCenter = new Vector3(0.2135f, 2.8945f, 3.342f);
    private static readonly Vector2 RearMpcdSize = new Vector2(0.097f, 0.119f);
    private static readonly Vector3 RearTopCenter = new Vector3(0f, 3.169f, 3.330f);
    private static readonly Vector2 RearTopSize = new Vector2(0.16f, 0.148f);
    private static readonly Vector3 RearLowCenter = new Vector3(-0.2f, 2.707f, 3.330f);
    private static readonly Vector2 RearLowSize = new Vector2(0.116f, 0.098f);
    private const float RearFront = 0.003f;   // 유리면 앞으로

    private static readonly Color RearGreen = new Color(0.35f, 1f, 0.45f, 1f);

    /// 뒷좌석 화면 묶음을 entity 아래에 만든다(이미 있으면 지우고 다시). 돌려준 오브젝트를 F형 부품 묶음에 넣는다.
    public static GameObject BuildRearCockpit(Transform entityT, SaccEntity entity, SaccAirVehicle air, ACO_AirframeVariant variant)
    {
        Transform old = entityT.Find(RearCockpitName);
        if (old != null) { Object.DestroyImmediate(old.gameObject); }
        TMP_FontAsset font = TutorialFont();

        var root = new GameObject(RearCockpitName);
        root.transform.SetParent(entityT, false);
        ACO_RearCockpit rc = root.AddUdonSharpComponent<ACO_RearCockpit>();
        rc.Entity = entity;
        rc.AirVehicle = air;
        rc.Airframe = variant;

        // ① 지도: 화면 위쪽 정사각형(좌석 필터 = 뒷자리 — 지도가 스스로 켜고 끈다)
        float mapSize = 0.172f;
        float mapTop = RearMainCenter.y + RearMainSize.y * 0.5f - 0.006f;
        Vector3 mapPos = new Vector3(RearMainCenter.x, mapTop - mapSize * 0.54f, RearMainCenter.z - RearFront);
        ACO_CockpitMap map = ACO_RadarBuilder.BuildPanelMap(root.transform, "Map", mapPos, mapSize);
        if (map != null)
        {
            map.EntityControl = entity;
            map.SeatFilter = ACO_CockpitMap.SEAT_PASSENGER;
        }

        var screens = new GameObject("Screens");
        screens.transform.SetParent(root.transform, false);
        rc.Screens = screens;

        // ① 아래 범례(번역 라벨 — 이름 = 키)
        float legendTop = mapPos.y - mapSize * 0.54f - 0.004f;
        float legendBottom = RearMainCenter.y - RearMainSize.y * 0.5f + 0.004f;
        Vector3 legendPos = new Vector3(RearMainCenter.x, (legendTop + legendBottom) * 0.5f, RearMainCenter.z - RearFront);
        TextMeshPro legend = RearText(screens.transform, "rear.legend", legendPos, new Vector2(RearMainSize.x - 0.012f, legendTop - legendBottom), 0.16f, font, TextAlignmentOptions.Center, "");
        LocLabel3D(legend, "rear.legend");

        // ② RWR: 고리 두 개 + 가운데 내 기체 + 미사일 · 락온 표시
        var rwr = new GameObject("Rwr");
        rwr.transform.SetParent(screens.transform, false);
        rwr.transform.localPosition = new Vector3(RearLeftCenter.x, RearLeftCenter.y - 0.008f, RearLeftCenter.z - RearFront);
        float outer = 0.038f;
        rc.RwrOuter = outer;
        rc.RwrInner = 0.009f;
        Material ringMat = RearMat("ACO_RearRing", new Color(0.35f, 1f, 0.45f, 0.8f));
        RearRenderer(rwr.transform, "RingOuter", RearRingMesh("ACO_RearRingOuter", outer, 0.0012f), ringMat, Vector3.zero);
        RearRenderer(rwr.transform, "RingInner", RearRingMesh("ACO_RearRingInner", outer * 0.5f, 0.0008f), ringMat, Vector3.zero);
        RearRenderer(rwr.transform, "Self", RearTriangleMesh("ACO_RearSelf", 0.008f), RearMat("ACO_RearSelf", Color.white), new Vector3(0f, 0f, -0.0005f));
        Mesh chevron = RearTriangleMesh("ACO_RearChevron", 0.009f);
        Material missileMat = RearMat("ACO_RearMissile", new Color(1f, 0.2f, 0.15f, 1f));
        var missiles = new Transform[ACO_RWR.MAX_MISSILES];
        for (int i = 0; i < missiles.Length; i++)
        {
            missiles[i] = RearRenderer(rwr.transform, "Missile" + i, chevron, missileMat, new Vector3(0f, 0f, -0.001f)).transform;
            missiles[i].gameObject.SetActive(false);
        }
        Mesh bar = RearQuadMesh("ACO_RearLockBar", 0.012f, 0.0025f);
        Material lockMat = RearMat("ACO_RearLock", new Color(1f, 0.82f, 0.2f, 1f));
        var locks = new Transform[ACO_RWR.MAX_LOCKS];
        for (int i = 0; i < locks.Length; i++)
        {
            locks[i] = RearRenderer(rwr.transform, "Lock" + i, bar, lockMat, new Vector3(0f, 0f, -0.001f)).transform;
            locks[i].gameObject.SetActive(false);
        }
        rc.MissileMarks = missiles;
        rc.LockMarks = locks;
        Vector3 rwrTextPos = new Vector3(RearLeftCenter.x, RearLeftCenter.y + RearMpcdSize.y * 0.5f - 0.009f, RearLeftCenter.z - RearFront);
        rc.RwrText = RearText(screens.transform, "RwrText", rwrTextPos, new Vector2(RearMpcdSize.x - 0.006f, 0.014f), 0.1f, font, TextAlignmentOptions.Center, "RWR");

        // ③ 미션 상황 · ④ 비행 정보 · ⑤ 조종사
        rc.MissionText = RearText(screens.transform, "MissionText", new Vector3(RearRightCenter.x, RearRightCenter.y, RearRightCenter.z - RearFront),
            RearMpcdSize - new Vector2(0.008f, 0.01f), 0.14f, font, TextAlignmentOptions.MidlineLeft, "");
        rc.FlightText = RearText(screens.transform, "FlightText", new Vector3(RearTopCenter.x, RearTopCenter.y, RearTopCenter.z - RearFront),
            RearTopSize - new Vector2(0.014f, 0.012f), 0.22f, font, TextAlignmentOptions.MidlineLeft, "");
        rc.PilotText = RearText(screens.transform, "PilotText", new Vector3(RearLowCenter.x, RearLowCenter.y, RearLowCenter.z - RearFront),
            RearLowSize - new Vector2(0.008f, 0.008f), 0.12f, font, TextAlignmentOptions.MidlineLeft, "");

        screens.SetActive(false);
        ACO_SyncModeFixer.FixHierarchy(root, true);
        return root;
    }

    private static TextMeshPro RearText(Transform parent, string name, Vector3 pos, Vector2 size, float maxSize, TMP_FontAsset font, TextAlignmentOptions align, string text)
    {
        TextMeshPro t = MakeText(parent, name, pos, maxSize, RearGreen, align, size, new Vector2(0.5f, 0.5f), font, font.material, text);
        t.enableAutoSizing = true;
        t.fontSizeMax = maxSize;
        t.fontSizeMin = maxSize * 0.35f;
        t.overflowMode = TextOverflowModes.Truncate;
        return t;
    }

    private static MeshRenderer RearRenderer(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    private static Material RearMat(string name, Color c)
    {
        string path = $"{GeneratedDir}/{name}.mat";
        Material m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = SaveAsset(new Material(Shader.Find("Unlit/Color")) { name = name }, path); }
        m.color = c;
        UnityEditor.EditorUtility.SetDirty(m);
        return m;
    }

    /// 앞면 −Z(뒷좌석 쪽) 고리.
    private static Mesh RearRingMesh(string name, float radius, float width)
    {
        const int seg = 64;
        var v = new List<Vector3>();
        var tri = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector3 d = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
            v.Add(d * (radius - width * 0.5f));
            v.Add(d * (radius + width * 0.5f));
        }
        for (int i = 0; i < seg; i++)
        {
            int k = i * 2;
            tri.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 });
        }
        return RearSaveMesh(name, v, tri);
    }

    /// 꼭짓점 +Y 삼각형(앞면 −Z).
    private static Mesh RearTriangleMesh(string name, float size)
    {
        var v = new List<Vector3> { new Vector3(0f, size * 0.6f, 0f), new Vector3(size * 0.45f, -size * 0.4f, 0f), new Vector3(-size * 0.45f, -size * 0.4f, 0f) };
        return RearSaveMesh(name, v, new List<int> { 0, 1, 2 });
    }

    private static Mesh RearQuadMesh(string name, float w, float h)
    {
        var v = new List<Vector3> { new Vector3(-w * 0.5f, -h * 0.5f, 0f), new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(w * 0.5f, -h * 0.5f, 0f) };
        return RearSaveMesh(name, v, new List<int> { 0, 1, 2, 0, 2, 3 });
    }

    private static Mesh RearSaveMesh(string name, List<Vector3> v, List<int> tri)
    {
        var m = new Mesh { name = name };
        m.SetVertices(v);
        m.SetTriangles(tri, 0);
        // 양면(앞뒤 어느 쪽에서도 보이게 — 감기 방향을 신경 쓰지 않는다)
        var both = new List<int>(tri);
        for (int i = 0; i < tri.Count; i += 3) { both.Add(tri[i]); both.Add(tri[i + 2]); both.Add(tri[i + 1]); }
        m.SetTriangles(both, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return SaveMesh(m, name);
    }
}
