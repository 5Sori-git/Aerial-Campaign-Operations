using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 격납고 꾸미기(6-199, 사용자 요청 "EEI 모델 위주로, 무장 거치대에 무장을 올려서"): 씬의 Hanger 아래 HangarDecor 묶음에 EEI 지원 장비를 놓는다.
///  - 무장 거치대(EEI Missile_Rack A형 — 받침 빔 앞뒤 ±0.85m, B형 — ±0.40m, 2단 × V 받침 4칸)에 SSY 미사일 · 폭탄을 얹는다.
///    무장마다 몸통 반지름을 메시에서 재서 V(45° 벽, 바닥 평평 ±2.5cm)에 닿는 높이로, 날개는 45° 굴려(X자) 옆 칸과 안 겹치게,
///    날개가 받침 빔에 걸리지 않는 앞뒤 위치를 찾아 놓는다. 큰 무장은 위 단 안쪽 두 칸만(아래 단 무장과 안 부딪히게).
///  - 견인 트랙터 + 견인봉을 소품 기체 기수 바퀴에 물린다(봉 끝 뼈 ArmEnd_L/R 가운데 = 기수 바퀴 축).
///  - 포드 거치대(PodRack)에 ECM · 데이터링크 · 로켓 · 버디 포드를 한 줄로.
///  - 모든 물건은 바닥을 레이캐스트로 찾아 가장 낮은 점을 바닥에 맞춘다(뜨거나 묻히지 않게). 큰 물건엔 박스 콜라이더(몸이 뚫고 지나가지 않게).
///  - 겹침 검사(6-214, 사용자 "미사일이 겹치거나, 걸리거나… 서로 안겹치게"): 무장 표면을 1cm 간격으로 찍어 2cm 칸에 넣고,
///    이미 놓은 무장의 칸과 하나라도 겹치거나 날개(몸통 반지름 1.3배 밖)가 거치대 칸에 닿으면 앞뒤로 옮겨 다시 본다.
///    칸은 바깥(0 · 3) → 안쪽(1 · 2) 순으로 채우고, 칸마다 그 거치대의 후보 무장을 차례로 넣어 보아 맞는 첫 무장을 놓는다(맞는 게 없으면 빈칸).
///    아래 단은 가는 무장(a9 등)이면 V에 깊이 앉아 X자 날개가 바닥판을 뚫었다 — 그런 무장은 아래 단 후보에서 저절로 빠진다.
/// 다시 실행하면 HangarDecor를 지우고 같은 배치로 다시 만든다.
public static class ACO_HangarDecor
{
    private const string RootName = "HangarDecor";
    private const string HangarName = "Hanger============";
    private const string Eei = "Assets/EEIproducts/AirCraftSupportSet/";
    private const string Ssy = "Assets/studio_symphony/SSY Airplane Missile Set RF版/西側ミサイルセット/prefab/";
    private const float FloorProbeY = 9f;

    // 거치대 V 받침(거치대 원점 기준, 원점은 바퀴 바닥에서 0.30m 위)
    private static readonly float[] SlotX = { -0.51f, -0.195f, 0.195f, 0.51f };
    private const float UpperY = 0.704f, LowerY = 0.313f, VFlat = 0.025f, BeamHalf = 0.08f;

    private struct Weapon { public string Path; public float Scale; }
    private static readonly Weapon Aim9 = new Weapon { Path = Ssy + "対空4種/B-AAM9/a9.prefab", Scale = 1f };
    private static readonly Weapon Aim120 = new Weapon { Path = Ssy + "対空4種/B-AAM120/a120.prefab", Scale = 1f };
    private static readonly Weapon Maverick = new Weapon { Path = Ssy + "対地4種/GM65/GM65.prefab", Scale = 0.1f };
    private static readonly Weapon Harm = new Weapon { Path = Ssy + "対地4種/GM88/GM88.prefab", Scale = 0.1f };
    private static readonly Weapon Harpoon = new Weapon { Path = Ssy + "対地4種/GM84/GM84.prefab", Scale = 0.1f };
    private static readonly Weapon Bomb = new Weapon { Path = Ssy + "特殊4種/B15/B15.prefab", Scale = 0.1f };

    private class Info { public float R; public Vector3 Center; public Vector3 Nose; public List<Vector2> Fins = new List<Vector2>(); public float Len; }
    private static readonly Dictionary<string, Info> _info = new Dictionary<string, Info>();

    private static Transform _root;
    private static int _count, _skipped;

    // 겹침 검사(6-214)
    private const float Vox = 0.02f, SampleStep = 0.01f, FinRadius = 1.3f;
    private static readonly HashSet<long> _taken = new HashSet<long>();       // 놓은 무장 표면 칸
    private static readonly HashSet<long> _rackCells = new HashSet<long>();   // 지금 거치대 표면 칸
    private static readonly Dictionary<string, List<Vector3>> _samples = new Dictionary<string, List<Vector3>>();   // 무장 루트 로컬 표면 점
    private static readonly List<string> _skipNames = new List<string>();

    [MenuItem("ACO/Hangar/Decorate")]
    public static void Build()
    {
        _info.Clear();
        _count = 0;
        _skipped = 0;
        _taken.Clear();
        _rackCells.Clear();
        _samples.Clear();
        _skipNames.Clear();
        _roll.Clear();
        _xCount = 0;
        GameObject hangar = GameObject.Find(HangarName);
        if (hangar == null)
        {
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true)) { if (t.name == HangarName) { hangar = t.gameObject; break; } }
        }
        if (hangar == null) { Debug.LogWarning($"[ACO] 격납고 꾸미기: '{HangarName}'를 찾지 못했습니다."); return; }
        Transform old = hangar.transform.Find(RootName);
        if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); }
        var rootGo = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(rootGo, "ACO hangar decor");
        rootGo.transform.SetParent(hangar.transform, false);
        rootGo.transform.position = Vector3.zero;
        _root = rootGo.transform;

        // 패널(미리보기 기체) 쪽으로 모은다(사용자 요청 — 안쪽은 잘 안 간다). 해치 앞 통로 · 엘리베이터 입구 · 미리보기 기체 둘레는 비운다.
        // ── 좌현 엘리베이터 입구 안쪽 띠: 무장 대기 줄 — 거치대 5대(견인봉이 미리보기 기체 쪽을 본다)
        Rack("A", "G", new Vector3(-13f, 0f, -70f), 90f, new[] { Aim120, Aim9 }, new[] { Aim120, Aim9 });
        Rack("A", "W", new Vector3(-13f, 0f, -74.5f), 90f, new[] { Harm, Aim120 }, new[] { Aim120, Aim9 });
        Rack("B", "Y", new Vector3(-13f, 0f, -79f), 90f, new[] { Maverick, Aim9 }, new[] { Maverick, Aim9 });
        Rack("A", "Y", new Vector3(-13f, 0f, -83.5f), 90f, new[] { Harpoon, Aim9 }, new[] { Harpoon, Aim9 });
        Rack("B", "D", new Vector3(-13f, 0f, -88f), 90f, new[] { Bomb }, new[] { Bomb });
        Place(Eei + "TowSet/Prefab/G/TowTractor_TypeB.prefab", new Vector3(-8.3f, 0f, -70.5f), 0f, 1f, true);

        // ── 뒤쪽 벽 앞 좌현 구석: 포드 거치대 한 줄
        Pod(Eei + "ECMPod/Prefab/ECMPod.prefab", new Vector3(-14f, 0f, -97f));
        Pod(Eei + "DLPod/Prefab/DLPod.prefab", new Vector3(-12.4f, 0f, -97f));
        Pod(Eei + "RocketPod/Prefab/RocketPod_L.prefab", new Vector3(-10.8f, 0f, -97f));
        Pod(Eei + "BuddyPod/Prefab/BuddyPod_Shorten.prefab", new Vector3(-9.2f, 0f, -97f));

        // ── 우현 앞쪽: 장전 거치대 2대, 뒤쪽 벽 앞 우현 구석에 트랙터
        Rack("A", "G", new Vector3(12f, 0f, -71f), 180f, new[] { Aim120, Aim9 }, new[] { Aim120, Aim9 });
        Rack("A", "W", new Vector3(12f, 0f, -76.5f), 180f, new[] { Aim9, Aim120 }, new[] { Aim120, Aim9 });
        Place(Eei + "TowSet/Prefab/Y/TowTractor_TypeC.prefab", new Vector3(11f, 0f, -97.3f), 270f, 1f, true);

        // ── 패널 쪽에서 가장 가까운 소품 기체 기수 바퀴에 견인 트랙터를 물린다 (EEI 굄목은 셰이더가 없어 분홍으로 보여 뺐다)
        TowToNose("FA-18F (Prop)");

        EditorSceneManager.MarkSceneDirty(rootGo.scene);
        Debug.Log($"[ACO] 격납고 꾸미기: {_count}개를 놓았습니다(거치대 무장 중 날개 X자 {_xCount}개, 나머지 +자). 거치대별 무장(위 단 / 아래 단 — 겹치지 않게 맞는 것만): {string.Join(" · ", _skipNames)}. 씬을 저장하세요.", rootGo);
    }

    // ───────────── 배치 ─────────────

    private static GameObject Place(string path, Vector3 pos, float yaw, float scale, bool collider)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) { Debug.LogWarning($"[ACO] 격납고 꾸미기: {path}가 없습니다."); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.transform.SetParent(_root, true);
        go.transform.localScale = src.transform.localScale * scale;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * src.transform.rotation;
        float floor = Floor(pos);
        go.transform.position = new Vector3(pos.x, floor, pos.z);
        Bounds b;
        if (WorldBounds(go, out b)) { go.transform.position += Vector3.up * (floor - b.min.y); }
        if (collider) { AddBox(go); }
        _count++;
        return go;
    }

    private static float Floor(Vector3 p)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, FloorProbeY, p.z), Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            if (h.collider.transform.IsChildOf(_root)) { continue; }
            return h.point.y;
        }
        return 6.28f;
    }

    /// 렌더러(스킨 메시는 지금 자세로 구워서)의 월드 바운드.
    private static bool WorldBounds(GameObject go, out Bounds b)
    {
        b = new Bounds();
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Vector3 w in WorldVerts(r))
            {
                if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else { b.Encapsulate(w); }
            }
        }
        return any;
    }

    private static IEnumerable<Vector3> WorldVerts(Renderer r)
    {
        var smr = r as SkinnedMeshRenderer;
        if (smr != null)
        {
            var m = new Mesh();
            smr.BakeMesh(m, true);
            Matrix4x4 l2w = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            Vector3[] v = m.vertices;
            Object.DestroyImmediate(m);
            for (int i = 0; i < v.Length; i++) { yield return l2w.MultiplyPoint3x4(v[i]); }
            yield break;
        }
        MeshFilter mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { yield break; }
        Bounds lb = mf.sharedMesh.bounds;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            yield return r.transform.TransformPoint(c);
        }
    }

    private static void AddBox(GameObject go)
    {
        Bounds wb;
        if (!WorldBounds(go, out wb)) { return; }
        var box = Undo.AddComponent<BoxCollider>(go);
        // 루트 축에 맞춘 상자(대략) — 회전은 요(y)뿐이라 월드 바운드를 루트 로컬로 옮긴다
        Vector3 size = Quaternion.Inverse(go.transform.rotation) * wb.size;
        Vector3 s = go.transform.lossyScale;
        box.center = go.transform.InverseTransformPoint(wb.center);
        Vector3 local = go.transform.InverseTransformVector(wb.size);
        box.size = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
    }

    // ───────────── 무장 거치대 ─────────────

    private static readonly int[] SlotOrder = { 0, 3, 1, 2 };   // 바깥 칸 먼저(서로 1m 떨어져 날개가 안 닿는다), 그다음 안쪽

    private static void Rack(string type, string color, Vector3 pos, float yaw, Weapon[] upper, Weapon[] lower)
    {
        GameObject rack = Place(Eei + $"MissileRack/Prefab/{color}/Missile_Rack_Type{type}.prefab", pos, yaw, 1f, true);
        if (rack == null) { return; }
        _rackCells.Clear();
        foreach (Vector3 p in SurfacePoints(rack)) { _rackCells.Add(Cell(p)); }
        // 받침 좌표는 요 0°에서 잰 월드 축 기준(거치대 루트 자체는 프리팹 회전이 있다)
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        float beam = type == "A" ? 0.85f : 0.40f;
        // 거치대 원점 = 그 자리 바닥 + 0.30(바퀴 바닥이 원점 아래 0.30)
        var tiers = new string[2];
        for (int tier = 0; tier < 2; tier++)
        {
            Weapon[] cands = tier == 0 ? upper : lower;
            var placed = new List<string>();
            if (cands != null)
            {
                foreach (int s in SlotOrder)
                {
                    foreach (Weapon w in cands)
                    {
                        if (Load(rack.transform.position, yawRot, w, SlotX[s], tier == 0 ? UpperY : LowerY, beam)) { placed.Add(System.IO.Path.GetFileNameWithoutExtension(w.Path)); break; }
                    }
                }
            }
            tiers[tier] = placed.Count > 0 ? string.Join(",", placed) : "-";
        }
        _skipNames.Add($"{type}{color}[{tiers[0]} / {tiers[1]}]");
    }

    /// 무장 하나를 이 칸에 놓아 본다 — 겹치지 않는 자리가 있으면 놓고 참.
    private static bool Load(Vector3 rackPos, Quaternion yawRot, Weapon w, float x, float vBottom, float beam)
    {
        Info info = Measure(w);
        if (info == null) { return false; }
        float hc = Mathf.Max(info.R, info.R * 1.41421f - VFlat);
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(w.Path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.transform.SetParent(_root, true);
        go.transform.localScale = src.transform.localScale * w.Scale;
        // 기수 = 거치대 앞(+z, 견인봉 쪽), 날개 X자
        // 모델의 긴 축(기수 쪽)을 거치대 앞(+z)으로, 그 다음 축 둘레로 날개가 X자가 되는 각도만큼(6-214 — SSY 모델은 날개가 이미 X자라 45°를 더 굴리면 +자가 됐다)
        List<Vector3> local = LocalSamples(w, src);
        Quaternion toFront = Quaternion.FromToRotation(info.Nose, Vector3.forward);
        float xRoll = XRoll(w, info, local, toFront, go.transform.localScale);
        Vector3 dir = yawRot * Vector3.forward;
        var cells = new List<long>();
        // 날개 X자를 먼저, 안 들어가면 +자(X자면 아래로 비스듬한 날개가 위 단 V 받침 사이 세로 막대에 걸리는 무장이 있다 — +자는 아래 날개가 막대 사이로 곧게 내려간다)
        for (int ri = 0; ri < 2; ri++)
        {
            Quaternion rot = yawRot * Quaternion.Euler(0f, 0f, xRoll + ri * 45f) * toFront;
            go.transform.rotation = rot;
            // 날개가 받침 빔(±beam, 두께 ±BeamHalf)에 걸리지 않고, 놓은 무장 · 거치대와 겹치지 않는 앞뒤 위치 — 가운데에서 가까운 것(2cm 간격)
            for (int k = 0; k <= 120; k++)
            {
                float d = ((k + 1) / 2) * 0.02f * (k % 2 == 1 ? 1f : -1f);
                if (!FinsClear(info, d, beam)) { continue; }
                Vector3 axis = rackPos + yawRot * new Vector3(x, vBottom + hc, d);
                go.transform.position = axis - rot * Vector3.Scale(info.Center, go.transform.localScale);
                if (!Clear(go.transform.localToWorldMatrix, local, axis, dir, info.R * FinRadius, cells)) { continue; }
                foreach (long c in cells) { _taken.Add(c); }
                _count++;
                if (ri == 0) { _xCount++; }
                return true;
            }
        }
        Object.DestroyImmediate(go);
        return false;
    }

    private static readonly Dictionary<string, float> _roll = new Dictionary<string, float>();
    private static int _xCount;   // 날개 X자로 놓인 수

    /// 날개가 X자(옆 · 위아래로 똑바로 뻗지 않게)가 되는 축 둘레 굴림 각도 — 날개 점(축에서 몸통 반지름 1.35배 밖)의 가장 먼 옆 거리가 가장 작은 각도(3° 간격).
    /// +자면 날개 하나가 옆으로 다 뻗고, X자면 그 0.7배라 옆 칸과 덜 겹친다. 무장마다 한 번.
    private static float XRoll(Weapon w, Info info, List<Vector3> local, Quaternion toFront, Vector3 scale)
    {
        float best;
        if (_roll.TryGetValue(w.Path, out best)) { return best; }
        var fins = new List<Vector3>();
        float fr = info.R * 1.35f;
        for (int i = 0; i < local.Count; i += 5)
        {
            Vector3 v = Vector3.Scale(local[i] - info.Center, scale);
            Vector3 radial = v - Vector3.Dot(v, info.Nose) * info.Nose;
            if (radial.magnitude > fr) { fins.Add(toFront * v); }
        }
        best = 45f;
        float bestReach = float.MaxValue;
        for (int a = 0; a < 90; a += 3)
        {
            Quaternion q = Quaternion.Euler(0f, 0f, a);
            float reach = 0f;
            foreach (Vector3 f in fins) { reach = Mathf.Max(reach, Mathf.Abs((q * f).x)); }
            if (reach < bestReach - 1e-4f) { bestReach = reach; best = a; }
        }
        _roll[w.Path] = best;
        return best;
    }

    /// 이 자세의 무장 표면 칸(cells에 채운다)이 놓은 무장과 겹치지 않고, 날개(축에서 finR 밖)가 거치대에 닿지 않는가. 몸통은 V 받침에 얹히므로 거치대 검사에서 뺀다.
    private static bool Clear(Matrix4x4 l2w, List<Vector3> local, Vector3 axis, Vector3 dir, float finR, List<long> cells)
    {
        cells.Clear();
        float fin2 = finR * finR;
        foreach (Vector3 lp in local)
        {
            Vector3 p = l2w.MultiplyPoint3x4(lp);
            long c = Cell(p);
            if (_taken.Contains(c)) { return false; }
            Vector3 v = p - axis;
            Vector3 radial = v - Vector3.Dot(v, dir) * dir;
            if (radial.sqrMagnitude > fin2 && _rackCells.Contains(c)) { return false; }
            cells.Add(c);
        }
        return true;
    }

    private static long Cell(Vector3 p)
    {
        long x = Mathf.FloorToInt(p.x / Vox) + 100000, y = Mathf.FloorToInt(p.y / Vox) + 100000, z = Mathf.FloorToInt(p.z / Vox) + 100000;
        return (x << 40) | (y << 20) | z;
    }

    /// 무장 프리팹 표면 점(루트 로컬, 루트 스케일 전) — 삼각형을 SampleStep 간격으로 찍는다(월드 크기 기준). 무장마다 한 번.
    private static List<Vector3> LocalSamples(Weapon w, GameObject src)
    {
        List<Vector3> pts;
        if (_samples.TryGetValue(w.Path, out pts)) { return pts; }
        pts = new List<Vector3>();
        Matrix4x4 rootInv = src.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) { continue; }
            Matrix4x4 m = rootInv * mf.transform.localToWorldMatrix;
            // 간격은 놓일 크기(루트 스케일 × 무장 배율)로 잰다 — 로컬 점 사이 간격 = 월드 간격 / 그 배율
            float k = Mathf.Max(src.transform.localScale.x * w.Scale, 1e-4f);
            SampleMesh(mf.sharedMesh, m, SampleStep / k, pts);
        }
        _samples[w.Path] = pts;
        return pts;
    }

    /// 놓인 물건의 월드 표면 점(스킨 메시는 지금 자세로 굽는다).
    private static List<Vector3> SurfacePoints(GameObject go)
    {
        var pts = new List<Vector3>();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            var smr = r as SkinnedMeshRenderer;
            if (smr != null)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                SampleMesh(baked, Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one), SampleStep, pts);
                Object.DestroyImmediate(baked);
                continue;
            }
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) { SampleMesh(mf.sharedMesh, r.transform.localToWorldMatrix, SampleStep, pts); }
        }
        return pts;
    }

    /// 삼각형마다 두 변을 step 간격으로 나눈 격자 점(무게중심 좌표)을 m으로 옮겨 넣는다. step은 m을 거친 뒤 공간의 길이가 아니라 메시 공간 길이.
    private static void SampleMesh(Mesh mesh, Matrix4x4 m, float step, List<Vector3> pts)
    {
        Vector3[] v = mesh.vertices;
        int[] tri = mesh.triangles;
        for (int i = 0; i + 2 < tri.Length; i += 3)
        {
            Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
            // 간격은 옮긴 뒤(루트 로컬) 길이로 — LocalSamples는 그 공간의 step을 넘긴다
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max((b - a).magnitude, (c - a).magnitude, (c - b).magnitude) / step), 1, 400);
            for (int s = 0; s <= n; s++)
            {
                for (int t = 0; t <= n - s; t++)
                {
                    float u = (float)s / n, w2 = (float)t / n;
                    pts.Add(a + (b - a) * u + (c - a) * w2);
                }
            }
        }
    }

    private static bool FinsClear(Info info, float d, float beam)
    {
        // 무장 로컬 z(기수 +) 기준 날개 구간을 거치대 z로: 가운데가 d
        foreach (Vector2 f in info.Fins)
        {
            float a = d + f.x, b = d + f.y;
            foreach (float bz in new[] { beam, -beam })
            {
                float lo = bz - BeamHalf - 0.03f, hi = bz + BeamHalf + 0.03f;
                if (b > lo && a < hi) { return false; }
            }
        }
        // 몸통이 두 빔에 다 얹혀야 한다
        return d - info.Len * 0.5f < -beam + BeamHalf && d + info.Len * 0.5f > beam - BeamHalf;
    }

    /// 무장 메시를 잰다(스케일 반영, 프리팹 루트 로컬): 긴 축(모델마다 다르다 — 프리팹 회전으로 눕혀 둔 것도 있다), 몸통 반지름(가운데 40% 구간 반경의 25번째 백분위),
    /// 단면 가운데, 기수 방향(끝 10% 구간 최대 반경이 작은 쪽), 날개 구간(몸통 반지름 1.35배 넘는 정점 — 가운데 기준, 기수 +).
    private static Info Measure(Weapon w)
    {
        Info info;
        if (_info.TryGetValue(w.Path, out info)) { return info; }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(w.Path);
        if (src == null) { _info[w.Path] = null; return null; }
        Vector3 k = src.transform.localScale * w.Scale;
        var pts = new List<Vector3>();
        Matrix4x4 rootInv = src.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) { continue; }
            Matrix4x4 m = rootInv * mf.transform.localToWorldMatrix;
            foreach (Vector3 v in mf.sharedMesh.vertices) { pts.Add(Vector3.Scale(m.MultiplyPoint3x4(v), k)); }
        }
        if (pts.Count == 0) { _info[w.Path] = null; return null; }
        Vector3 min = pts[0], max = pts[0];
        foreach (Vector3 p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        Vector3 size = max - min;
        int a = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
        int b1 = (a + 1) % 3, b2 = (a + 2) % 3;
        info = new Info();
        Vector3 c = (min + max) * 0.5f;
        info.Center = new Vector3(c.x / k.x, c.y / k.y, c.z / k.z);   // 스케일 전 로컬(배치 때 localScale을 곱한다)
        info.Len = size[a];
        var mid = new List<float>();
        foreach (Vector3 p in pts) { if (Mathf.Abs(p[a] - c[a]) < info.Len * 0.2f) { mid.Add(new Vector2(p[b1] - c[b1], p[b2] - c[b2]).magnitude); } }
        mid.Sort();
        info.R = mid.Count > 0 ? mid[Mathf.Clamp(mid.Count / 4, 0, mid.Count - 1)] : 0.1f;
        float rLo = 0f, rHi = 0f;
        foreach (Vector3 p in pts)
        {
            float rr = new Vector2(p[b1] - c[b1], p[b2] - c[b2]).magnitude;
            if (p[a] < min[a] + info.Len * 0.1f) { rLo = Mathf.Max(rLo, rr); }
            if (p[a] > max[a] - info.Len * 0.1f) { rHi = Mathf.Max(rHi, rr); }
        }
        float sign = rLo < rHi ? -1f : 1f;   // 기수 = 반경이 작은 끝
        Vector3 nose = Vector3.zero;
        nose[a] = sign;
        info.Nose = nose;
        var fz = new List<float>();
        foreach (Vector3 p in pts)
        {
            if (new Vector2(p[b1] - c[b1], p[b2] - c[b2]).magnitude > info.R * 1.35f) { fz.Add((p[a] - c[a]) * sign); }
        }
        fz.Sort();
        for (int i = 0; i < fz.Count;)
        {
            float lo = fz[i], hi = lo;
            int j = i + 1;
            while (j < fz.Count && fz[j] - hi < 0.12f) { hi = fz[j]; j++; }
            info.Fins.Add(new Vector2(lo, hi));
            i = j;
        }
        _info[w.Path] = info;
        return info;
    }

    // ───────────── 포드 · 견인 · 굄목 ─────────────

    private static void Pod(string podPath, Vector3 pos)
    {
        GameObject stand = Place(Eei + "PodRack/FBX/PodRack.fbx", pos, 0f, 1f, false);
        if (stand == null) { return; }
        Bounds sb;
        if (!WorldBounds(stand, out sb)) { return; }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(podPath);
        if (src == null) { return; }
        var pod = (GameObject)PrefabUtility.InstantiatePrefab(src);
        pod.transform.SetParent(_root, true);
        pod.transform.rotation = src.transform.rotation;
        pod.transform.position = new Vector3(sb.center.x, sb.max.y, sb.center.z);
        Bounds pb;
        if (WorldBounds(pod, out pb)) { pod.transform.position += new Vector3(sb.center.x - pb.center.x, sb.max.y - pb.min.y, sb.center.z - pb.center.z); }
        AddBox(pod);
        _count++;
    }

    private static Transform Prop(string name)
    {
        foreach (Transform t in Object.FindObjectsOfType<Transform>(true)) { if (t.name == name && t.parent != null && t.parent.name == HangarName) { return t; } }
        return null;
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) { if (t.name == name) { return t; } }
        return null;
    }

    /// 견인 트랙터를 기체 앞에 기체와 같은 방향으로 두고, 견인봉 끝(ArmEnd_L/R 가운데)이 기수 바퀴 축에 오게 옮긴다.
    private static void TowToNose(string propName)
    {
        Transform prop = Prop(propName);
        Transform wheel = prop != null ? Find(prop, "Nosewheel") : null;
        if (wheel == null) { Debug.LogWarning($"[ACO] 격납고 꾸미기: '{propName}'의 기수 바퀴를 찾지 못했습니다."); return; }
        Vector3 fwd = prop.forward;
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        GameObject tr = Place(Eei + "TowSet/Prefab/G/TowTractor_TypeA_Bar.prefab", wheel.position + fwd * 8f, yaw, 1f, false);
        if (tr == null) { return; }
        Transform l = Find(tr.transform, "ArmEnd_L"), r = Find(tr.transform, "ArmEnd_R");
        if (l != null && r != null)
        {
            Vector3 tip = (l.position + r.position) * 0.5f;
            Vector3 d = wheel.position - tip;
            tr.transform.position += new Vector3(d.x, 0f, d.z);
        }
        // 콜라이더 없음 — 견인봉까지 든 상자는 봉 위로 보이지 않는 벽이 된다
    }

    /// 기수 바퀴 앞뒤 굄목.
    private static void Chocks(string propName)
    {
        Transform prop = Prop(propName);
        Transform wheel = prop != null ? Find(prop, "Nosewheel") : null;
        if (wheel == null) { return; }
        Vector3 fwd = prop.forward;
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        Place(Eei.Replace("AirCraftSupportSet/", "") + "Other/DeckCrewSet/NWC/Prefab/NWC_STK_L.prefab", wheel.position + fwd * 0.34f, yaw, 1f, false);
        Place(Eei.Replace("AirCraftSupportSet/", "") + "Other/DeckCrewSet/NWC/Prefab/NWC_STK_L.prefab", wheel.position - fwd * 0.34f, yaw + 180f, 1f, false);
    }
}
