using UnityEditor;
using UnityEngine;

/// FA-18E 캐노피 앞 프레임 가늘게(6-195): 조종석 앞을 가로지르는 띠 = 캐노피 앞 프레임(Canopy.E, 폭 5~7cm, 거울 5개) + 동체 앞유리 아치(Main.E, 약 8cm).
/// 둘이 맞닿은 이음선 쪽으로 기수 방향(z) 거리를 Factor배로 모은다(15cm → 약 10cm). 붙어 있는 캐노피 유리 앞끝 · 앞유리 뒤끝도 같은 변위로 따라가
/// 틈이 없고(유리는 조금 늘어난다), 띠 바깥 Taper 구간에서 변위가 0으로 줄고, 아치 아래쪽(문턱)으로 갈수록 0이 되어 찢어지는 곳이 없다.
/// 원본 FBX(Pit-bool)는 건드리지 않는다 — 복사본 메시(Generated/Canopy)를 기체 프리팹의 스킨 메시에 끼운다(본 · 가중치 그대로 — 캐노피 여닫기 그대로). 되돌리기 있음.
public static class ACO_CanopyThin
{
    private const string Fbx = "Assets/Pit-bool/FA-18/FBX/FA-18E.fbx";
    private const string Dir = "Assets/ACO_Addon/Generated/Canopy";
    private static readonly string[] Targets = { "Canopy.E", "Main.E" };

    public const float Factor = 0.65f;      // 띠 폭 배율 — 이음선 뒤(캐노피 앞 프레임 쪽)
    public const float FactorFront = 0.45f; // 이음선 앞(앞유리 아치 쪽) — 더 가늘게(사용자 요청 "앞아치도", 0.3은 너무 얇아 ×1.5)
    private const float Half = 0.085f;       // 이음선에서 이 거리(m)까지는 그대로 모은다(띠 반폭)
    private const float Taper = 0.10f;       // 그 바깥 이만큼에서 변위가 0으로
    private const float YFull = 2.72f;       // 이 높이 위는 다(아치 다리까지)
    private const float YNone = 2.62f;       // 이 높이 아래는 그대로(문턱)
    private const float XMax = 0.6f;

    // 아치 · 캐노피 프레임 높이 올리기(사용자 요청 — 실기 조종석처럼 앞이 높게 열려 보이게): 문턱 위로 갈수록 비례해 올린다(꼭대기 +Lift).
    // 앞뒤로는 아치 부근(LiftZ0 ~ LiftZ1)만 다 올리고 캐노피 뒤쪽 · 기수 쪽으로 갈수록 0 — 유리가 꺾이지 않고 이어진다. 옆(|x|)도 바깥에서 0.
    public const float Lift = 0.05f;
    private const float LiftSill = 2.78f, LiftTop = 3.29f;
    private const float LiftZBack = 3.2f, LiftZ0 = 4.2f, LiftZ1 = 5.2f, LiftZFront = 6.0f;
    private const float LiftX0 = 0.5f, LiftX1 = 0.7f;

    // 이음선(캐노피 앞 프레임 앞끝 = 앞유리 아치 뒤끝)의 z — 높이별(렌더 · 정점으로 잰 값)
    private static readonly float[] SeamY = { 2.70f, 2.79f, 3.01f, 3.25f, 3.32f };
    private static readonly float[] SeamZ = { 4.760f, 4.770f, 4.790f, 4.834f, 4.845f };

    private static float Seam(float y)
    {
        if (y <= SeamY[0]) { return SeamZ[0]; }
        for (int i = 1; i < SeamY.Length; i++)
        {
            if (y <= SeamY[i]) { return Mathf.Lerp(SeamZ[i - 1], SeamZ[i], (y - SeamY[i - 1]) / (SeamY[i] - SeamY[i - 1])); }
        }
        return SeamZ[SeamZ.Length - 1];
    }

    /// 기체 좌표의 z 변위.
    private static float Shift(Vector3 p)
    {
        if (Mathf.Abs(p.x) > XMax || p.y <= YNone) { return 0f; }
        float d = p.z - Seam(p.y);
        float a = Mathf.Abs(d);
        if (a > Half + Taper) { return 0f; }
        float k = d > 0f ? FactorFront : Factor;
        float s = a <= Half ? (k - 1f) * d : (k - 1f) * Mathf.Sign(d) * Half * (1f - (a - Half) / Taper);
        float f = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(YNone, YFull, p.y));
        return s * f;
    }

    /// 기체 좌표의 y 변위(올리기).
    // 프레임 안쪽 깊이 줄이기(사용자 요청 "유리와 연결부가 매끄럽게"): 띠(아치 · 캐노피 앞 프레임)의 바깥면(유리와 맞닿는 면)은 그대로 두고,
    // 안쪽으로 튀어나온 깊이를 DepthKeep배로 — 캐노피 단면 중심(x = 0, y = DepthCenterY)에서 바깥으로 재는 거리로 안쪽 면을 바깥면 쪽으로 올린다.
    // 유리는 건드리지 않는다. 거울(캐노피 프레임 안쪽에 붙은 작은 덩어리)은 납작해지지 않게 붙은 자리의 변위로 통째로 옮긴다.
    public const float DepthKeep = 0.4f;
    private const float DepthCenterY = 2.70f;
    private const float BinAngle = 2f * Mathf.Deg2Rad, BinZ = 0.02f;

    /// 띠 가중치(이음선 거리 · 높이) — 0 ~ 1.
    private static float BandWeight(Vector3 p)
    {
        if (Mathf.Abs(p.x) > XMax || p.y <= YNone) { return 0f; }
        float a = Mathf.Abs(p.z - Seam(p.y));
        if (a > Half + Taper) { return 0f; }
        float w = a <= Half ? 1f : 1f - (a - Half) / Taper;
        return w * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(YNone, YFull, p.y));
    }

    private static long Bin(Vector3 p)
    {
        float th = Mathf.Atan2(p.x, p.y - DepthCenterY);
        long a = Mathf.FloorToInt(th / BinAngle), z = Mathf.FloorToInt(p.z / BinZ);
        return (a + 100000L) * 1000000L + (z + 100000L);
    }

    private static float Radius(Vector3 p) { return new Vector2(p.x, p.y - DepthCenterY).magnitude; }

    /// 덩어리(같은 자리 정점을 묶고 삼각형으로 이음, submesh 0) — 정점별 대표 번호. used = 삼각형에 쓰인 정점, size = 덩어리 정점 수.
    private static int[] Groups(Mesh orig, Vector3[] p, out System.Collections.Generic.HashSet<int> used, out System.Collections.Generic.Dictionary<int, int> size)
    {
        int[] tri = orig.GetTriangles(0);
        int n = p.Length;
        var par = new int[n];
        for (int i = 0; i < n; i++) { par[i] = i; }
        System.Func<int, int> find = null;
        find = x => { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; };
        var weld = new System.Collections.Generic.Dictionary<Vector3Int, int>();
        for (int i = 0; i < n; i++)
        {
            var k = new Vector3Int(Mathf.RoundToInt(p[i].x * 2000f), Mathf.RoundToInt(p[i].y * 2000f), Mathf.RoundToInt(p[i].z * 2000f));
            int j;
            if (weld.TryGetValue(k, out j)) { par[find(i)] = find(j); } else { weld[k] = i; }
        }
        for (int t = 0; t < tri.Length; t += 3) { int a = find(tri[t]); par[find(tri[t + 1])] = a; par[find(tri[t + 2])] = a; }
        used = new System.Collections.Generic.HashSet<int>(tri);
        size = new System.Collections.Generic.Dictionary<int, int>();
        foreach (int i in used) { int r = find(i); size[r] = size.ContainsKey(r) ? size[r] + 1 : 1; }
        var grp = new int[n];
        for (int i = 0; i < n; i++) { grp[i] = find(i); }
        return grp;
    }

    /// 프레임 정점(submesh 0)의 안쪽 깊이 변위(기체 좌표). 거울 같은 작은 덩어리는 붙은 자리 변위로 통째로.
    private static Vector3[] Flatten(Mesh orig, Vector3[] p)
    {
        var d = new Vector3[p.Length];
        if (DepthKeep >= 1f) { return d; }
        System.Collections.Generic.HashSet<int> used;
        System.Collections.Generic.Dictionary<int, int> size;
        int[] grp = Groups(orig, p, out used, out size);
        // 바깥면: 각도 · z 칸마다 프레임(작은 덩어리 제외) 가장 바깥 거리
        var outer = new System.Collections.Generic.Dictionary<long, float>();
        foreach (int i in used)
        {
            if (size[grp[i]] <= 300 || BandWeight(p[i]) <= 0f) { continue; }
            long b = Bin(p[i]);
            float r = Radius(p[i]);
            float o;
            if (!outer.TryGetValue(b, out o) || r > o) { outer[b] = r; }
        }
        System.Func<Vector3, Vector3> delta = q =>
        {
            float w = BandWeight(q);
            if (w <= 0f) { return Vector3.zero; }
            float th = Mathf.Atan2(q.x, q.y - DepthCenterY);
            long a0 = Mathf.FloorToInt(th / BinAngle), z0 = Mathf.FloorToInt(q.z / BinZ);
            float ro = -1f;
            for (long da = -1; da <= 1; da++)
            {
                for (long dz = -1; dz <= 1; dz++)
                {
                    float o;
                    if (outer.TryGetValue((a0 + da + 100000L) * 1000000L + (z0 + dz + 100000L), out o) && o > ro) { ro = o; }
                }
            }
            float r = Radius(q);
            if (ro <= r) { return Vector3.zero; }
            float keep = 1f - (1f - DepthKeep) * w;
            float rn = ro - (ro - r) * keep;
            Vector2 dir = new Vector2(q.x, q.y - DepthCenterY).normalized;
            return new Vector3(dir.x, dir.y, 0f) * (rn - r);
        };
        var rigid = new System.Collections.Generic.Dictionary<int, Vector3>();
        foreach (int i in used)
        {
            int root = grp[i];
            if (size[root] > 300) { d[i] = delta(p[i]); continue; }
            // 작은 덩어리(거울): 프레임에 붙은 자리(가장 바깥 정점)의 변위로 통째로
            if (!rigid.ContainsKey(root))
            {
                int best = i;
                foreach (int j in used) { if (grp[j] == root && Radius(p[j]) > Radius(p[best])) { best = j; } }
                rigid[root] = delta(p[best]);
            }
            d[i] = rigid[root];
        }
        return d;
    }

    // 옆모습 단차 메우기(사용자 요청 — 캐노피 유리 앞끝이 앞유리 뒤끝보다 약 4cm 높아 아치에서 윤곽선이 계단처럼 꺾였다, 원본 모델부터):
    // 캐노피 쪽은 그대로, 이음선(0) → 아치 앞끝(StepRamp)에서 StepFix까지 올리고 앞유리 앞끝(StepZFront)으로 갈수록 0 — 문턱 위로 갈수록 비례.
    public const float StepFix = 0.035f;
    private const float StepRamp = 0.08f, StepZFront = 5.9f;

    private static float StepRise(Vector3 p)
    {
        if (StepFix <= 0f || p.y <= LiftSill) { return 0f; }
        float d = p.z - Seam(p.y);
        if (d <= 0f) { return 0f; }
        float zEnd = Seam(p.y) + StepRamp;
        float w = d <= StepRamp ? Mathf.SmoothStep(0f, 1f, d / StepRamp) : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StepZFront, zEnd, p.z));
        float g = Mathf.Clamp01((p.y - LiftSill) / (LiftTop - LiftSill));
        float fx = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LiftX1, LiftX0, Mathf.Abs(p.x)));
        return StepFix * w * g * fx;
    }

    // 이음선 홈 메우기(사용자 요청 — 옆에서 보면 아치 꼭대기가 V자로 파여 유리 선이 끊겨 보인다, 원본 모델부터):
    // 캐노피 앞 프레임과 아치가 맞닿는 줄(두 메시에서 같은 자리 정점)이 양쪽 테두리(유리와 맞닿는 곳)보다 꼭대기에서 약 1.6cm 낮다.
    // 이음선에서 NotchDepth만큼 바깥(단면 중심에서 바깥쪽)으로 밀고, 뒤 테두리(NotchBack) · 앞 테두리(NotchFront)까지 직선으로 0 — V가 한 직선이 된다.
    // 꼭대기에서만 깊어 옆으로 NotchAngle에서 0. 띠 안쪽 면 · 밑에 깔린 유리 끝도 같은 거리를 따라가 두께는 그대로.
    public const float NotchDepth = 0.016f;
    private const float NotchBack = 0.021f, NotchFront = 0.08f * FactorFront, NotchAngle = 35f; // 앞 테두리 = 아치 폭(원래 약 8cm × FactorFront)
    private static float[] _seamTh, _seamZ;

    /// 두 메시(쉬는 자세 · z 모으기 · 올리기 뒤)에서 같은 자리 정점 = 이음선 → 각도별 z. 아치 부근만(캐노피 레일 같은 다른 맞닿는 곳 제외), 각도 칸마다 가장 바깥 정점.
    private static void FindSeam(Mesh canopy, Matrix4x4 canopyToFrame, Mesh main, Matrix4x4 mainToFrame)
    {
        var keys = new System.Collections.Generic.HashSet<Vector3Int>();
        foreach (Vector3 v in canopy.vertices) { keys.Add(SeamKey(Moved(canopyToFrame.MultiplyPoint3x4(v)))); }
        var bins = new System.Collections.Generic.SortedDictionary<int, Vector2>();
        foreach (Vector3 v in main.vertices)
        {
            Vector3 q = Moved(mainToFrame.MultiplyPoint3x4(v));
            if (q.y < LiftSill || Mathf.Abs(q.z - Seam(q.y)) > 0.04f || !keys.Contains(SeamKey(q))) { continue; }
            int b = Mathf.RoundToInt(Mathf.Atan2(q.x, q.y - DepthCenterY) * Mathf.Rad2Deg);
            float r = Radius(q);
            Vector2 o;
            if (!bins.TryGetValue(b, out o) || r > o.y) { bins[b] = new Vector2(q.z, r); }
        }
        _seamTh = new float[bins.Count];
        _seamZ = new float[bins.Count];
        int i = 0;
        foreach (var kv in bins) { _seamTh[i] = kv.Key; _seamZ[i] = kv.Value.x; i++; }
    }

    /// 각도(도)의 이음선 z(모으기 · 올리기 뒤). 찾은 범위 밖이면 false.
    private static bool SeamAt(float th, out float zc)
    {
        zc = 0f;
        if (_seamTh == null || _seamTh.Length == 0 || th < _seamTh[0] - 2f || th > _seamTh[_seamTh.Length - 1] + 2f) { return false; }
        int k = 0;
        while (k < _seamTh.Length - 1 && _seamTh[k + 1] < th) { k++; }
        zc = k < _seamTh.Length - 1 ? Mathf.Lerp(_seamZ[k], _seamZ[k + 1], Mathf.InverseLerp(_seamTh[k], _seamTh[k + 1], th)) : _seamZ[k];
        return true;
    }

    private static Vector3Int SeamKey(Vector3 q) { return new Vector3Int(Mathf.RoundToInt(q.x * 2000f), Mathf.RoundToInt(q.y * 2000f), Mathf.RoundToInt(q.z * 2000f)); }

    private static Vector3 Moved(Vector3 p) { return p + new Vector3(0f, Rise(p) + StepRise(p), Shift(p)); }

    /// 홈 메우기 변위(기체 좌표) — q는 모으기 · 올리기 뒤 자리.
    private static Vector3 Notch(Vector3 q)
    {
        if (NotchDepth <= 0f || Mathf.Abs(q.x) > XMax) { return Vector3.zero; }
        float th = Mathf.Atan2(q.x, q.y - DepthCenterY) * Mathf.Rad2Deg;
        float zc;
        if (Mathf.Abs(th) >= NotchAngle || !SeamAt(th, out zc)) { return Vector3.zero; }
        float d = q.z - zc;
        float w = d >= 0f ? 1f - d / NotchFront : 1f + d / NotchBack;
        if (w <= 0f) { return Vector3.zero; }
        float a = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(th) / NotchAngle);
        Vector2 dir = new Vector2(q.x, q.y - DepthCenterY).normalized;
        return new Vector3(dir.x, dir.y, 0f) * (NotchDepth * w * a);
    }

    private static float Rise(Vector3 p)
    {
        if (Lift <= 0f || p.y <= LiftSill) { return 0f; }
        float g = Mathf.Clamp01((p.y - LiftSill) / (LiftTop - LiftSill));
        float fz = p.z < LiftZ0 ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LiftZBack, LiftZ0, p.z))
                                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LiftZFront, LiftZ1, p.z));
        float fx = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LiftX1, LiftX0, Mathf.Abs(p.x)));
        return Lift * g * fz * fx;
    }

    private static Mesh Original(string name)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(Fbx)) { if (o is Mesh m && m.name == name) { return m; } }
        return null;
    }

    private static string PlayerPrefabPath()
    {
        foreach (ACO.ACO_VehicleUpgrades u in Object.FindObjectsOfType<ACO.ACO_VehicleUpgrades>(true))
        {
            string p = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(u.gameObject);
            if (!string.IsNullOrEmpty(p)) { return p; }
        }
        return "Assets/Pit-bool/FA-18E Main_ACO.prefab";
    }

    /// canopy-thin-1: 가늘게 만든 복사본을 끼운다.
    [MenuItem("ACO/FA-18E Canopy Frame/Thin")]
    public static void Apply() { Run(true); }

    /// 원본 메시로 되돌린다.
    [MenuItem("ACO/FA-18E Canopy Frame/Restore Original")]
    public static void Revert() { Run(false); }

    private static void Run(bool thin)
    {
        string path = PlayerPrefabPath();
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var entity = root.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true);
            if (entity == null) { Debug.LogWarning("[ACO] 캐노피 프레임: SaccEntity를 찾지 못했습니다."); return; }
            Transform frame = entity.transform;
            bool changed = false;
            if (thin) { PrepareSeam(root, frame); }
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (System.Array.IndexOf(Targets, smr.name) < 0) { continue; }
                Mesh orig = Original(smr.name);
                if (orig == null) { Debug.LogWarning($"[ACO] 캐노피 프레임: {Fbx}에서 {smr.name} 메시를 찾지 못했습니다."); continue; }
                Mesh use = thin ? Build(orig, smr, frame) : orig;
                if (FitMaterials(smr, use)) { changed = true; }
                if (smr.sharedMesh == use) { continue; }
                smr.sharedMesh = use;
                changed = true;
            }
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log(thin ? $"[ACO] 캐노피 앞 프레임을 가늘게 했습니다(폭 ×{Factor}): {path}" : $"[ACO] 캐노피 프레임을 원본 메시로 되돌렸습니다: {path}");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        SwapPreview(thin);
    }

    /// canopy-thin-preview-1: 씬의 미리보기 모델(격납고 PreviewRoot 아래 — 프리팹이 아니라 씬에 직접 놓인 FA-18)에도 같은 메시를 끼운다(사용자 요청).
    /// 같은 FBX 리그라 본 · 바인드포즈가 같다 — 메시만 바꾸면 된다. 되돌리기는 원본으로.
    public static void ApplyPreview() { SwapPreview(true); }

    private const string PreviewRootName = "PreviewRoot";

    private static void SwapPreview(bool thin)
    {
        int n = 0;
        foreach (SkinnedMeshRenderer smr in Object.FindObjectsOfType<SkinnedMeshRenderer>(true))
        {
            if (System.Array.IndexOf(Targets, smr.name) < 0 || !UnderPreview(smr.transform)) { continue; }
            Mesh use = thin ? AssetDatabase.LoadAssetAtPath<Mesh>($"{Dir}/{smr.name}_ACOThin.asset") : Original(smr.name);
            if (use == null) { continue; }
            Undo.RecordObject(smr, "ACO canopy preview");
            bool mats = FitMaterials(smr, use);
            if (smr.sharedMesh == use && !mats) { continue; }
            smr.sharedMesh = use;
            EditorUtility.SetDirty(smr);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(smr.gameObject.scene);
            n++;
        }
        if (n > 0) { Debug.Log($"[ACO] 캐노피 프레임: 미리보기 모델 메시 {n}개를 " + (thin ? "가늘게 만든 복사본으로" : "원본으로") + " 바꿨습니다 — 씬을 저장하세요."); }
    }

    /// 메시의 submesh 수에 재질 칸을 맞춘다: 볼트 submesh(2)가 있으면 셋째 칸 = 볼트 재질, 없으면(원본) 두 칸. 바꿨으면 true.
    private static bool FitMaterials(SkinnedMeshRenderer smr, Mesh mesh)
    {
        Material[] mats = smr.sharedMaterials;
        int want = Mathf.Max(mesh.subMeshCount, 1);
        if (want <= 2 && mats.Length <= 2) { return false; }
        if (want > 2)
        {
            Material bolt = BoltMaterial();
            if (mats.Length == want && mats[2] == bolt) { return false; }
            var grown = new Material[want];
            for (int i = 0; i < want; i++) { grown[i] = i < mats.Length && i < 2 ? mats[i] : bolt; }
            smr.sharedMaterials = grown;
            return true;
        }
        var cut = new Material[want];
        for (int i = 0; i < want; i++) { cut[i] = mats[i]; }
        smr.sharedMaterials = cut;
        return true;
    }

    private static Material BoltMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(BoltMaterialPath);
        if (m == null)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "Canopy"); }
            m = new Material(Shader.Find("Standard")) { name = "ACO_CanopyBolt" };
            AssetDatabase.CreateAsset(m, BoltMaterialPath);
        }
        m.SetColor("_Color", BoltColor);
        m.SetFloat("_Metallic", 0.35f);
        m.SetFloat("_Glossiness", 0.45f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static bool UnderPreview(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent) { if (p.name == PreviewRootName) { return true; } }
        return false;
    }

    private static Matrix4x4 ToFrame(SkinnedMeshRenderer smr, Transform frame)
    {
        return frame.worldToLocalMatrix * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
    }

    private static void PrepareSeam(GameObject root, Transform frame)
    {
        _seamTh = null;
        _seamZ = null;
        _frameTris = null;
        SkinnedMeshRenderer c = null, m = null;
        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name == Targets[0]) { c = smr; } else if (smr.name == Targets[1]) { m = smr; }
        }
        Mesh co = c != null ? Original(c.name) : null, mo = m != null ? Original(m.name) : null;
        if (co == null || mo == null) { Debug.LogWarning("[ACO] 캐노피 프레임: 이음선을 찾을 메시가 없어 홈 메우기를 건너뜁니다."); return; }
        FindSeam(co, ToFrame(c, frame), mo, ToFrame(m, frame));
        Vector3[] pc;
        MeasureFrame(co, Final(co, ToFrame(c, frame), out pc));
        Debug.Log($"[ACO] 캐노피 프레임: 이음선 {_seamTh.Length}칸(각도 {(_seamTh.Length > 0 ? _seamTh[0] : 0f)}° ~ {(_seamTh.Length > 0 ? _seamTh[_seamTh.Length - 1] : 0f)}°).");
    }

    // 아치 안쪽 깊이 맞추기(사용자 요청 — 조종석에서 보면 아치 안쪽이 캐노피 프레임보다 깊어 턱의 옆면이 어둡게 보였다, 25° ~ 45°에서 2 ~ 2.5cm):
    // 각도마다 이음선 바로 뒤 캐노피 앞 프레임(거울 제외) 면을 그 각도 평면으로 잘라 가장 안쪽 거리를 재고(정점이 각도마다 드문드문이라 면으로),
    // 그보다 깊은 아치(Main.E 프레임, 이음선 앞 띠) 정점만 그 거리까지 바깥으로 올린다 — 아치 안쪽 면 = 프레임 안쪽 면. 바깥면 · 유리 그대로.
    // 아치가 이미 얕은 곳은 그대로(더 깊게는 안 함). 문턱 쪽은 띠 가중치로 0.
    public const bool ArchMatch = true;
    private const float FrameWin = 0.025f, ArchWin = 0.06f, ArchDepthMax = 0.10f;
    private static System.Collections.Generic.List<Vector3> _frameTris;

    private static float Theta(Vector3 q) { return Mathf.Atan2(q.x, q.y - DepthCenterY) * Mathf.Rad2Deg; }

    /// 모든 변위를 넣은 기체 좌표(pf = 원래 기체 좌표).
    private static Vector3[] Final(Mesh orig, Matrix4x4 toFrame, out Vector3[] pf)
    {
        Vector3[] v = orig.vertices;
        pf = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) { pf[i] = toFrame.MultiplyPoint3x4(v[i]); }
        Vector3[] flat = Flatten(orig, pf);
        var q = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = pf[i];
            Vector3 m = new Vector3(0f, Rise(p) + StepRise(p), Shift(p));
            q[i] = p + m + flat[i] + Notch(p + m);
        }
        return q;
    }

    /// 이음선 뒤 FrameWin 안에 걸친 캐노피 앞 프레임 삼각형(거울 같은 작은 덩어리 제외)을 모아 둔다.
    private static void MeasureFrame(Mesh canopy, Vector3[] q)
    {
        _frameTris = new System.Collections.Generic.List<Vector3>();
        System.Collections.Generic.HashSet<int> used;
        System.Collections.Generic.Dictionary<int, int> size;
        int[] grp = Groups(canopy, q, out used, out size);
        int[] tri = canopy.GetTriangles(0);
        for (int t = 0; t < tri.Length; t += 3)
        {
            if (size[grp[tri[t]]] <= 300) { continue; }
            bool near = false;
            for (int e = 0; e < 3; e++)
            {
                Vector3 p = q[tri[t + e]];
                float zc;
                if (p.y > LiftSill && Mathf.Abs(p.x) <= XMax && SeamAt(Theta(p), out zc) && p.z <= zc + 0.005f && p.z >= zc - FrameWin) { near = true; }
            }
            if (!near) { continue; }
            _frameTris.Add(q[tri[t]]);
            _frameTris.Add(q[tri[t + 1]]);
            _frameTris.Add(q[tri[t + 2]]);
        }
    }

    /// 각도(도) 평면으로 자른 캐노피 앞 프레임의 가장 안쪽 거리(이음선 뒤 FrameWin 안). 없으면 false.
    private static bool FrameInner(float th, out float inner)
    {
        inner = float.MaxValue;
        float zc;
        if (_frameTris == null || !SeamAt(th, out zc)) { return false; }
        float a = th * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
        for (int t = 0; t < _frameTris.Count; t += 3)
        {
            for (int e = 0; e < 3; e++)
            {
                Vector3 p0 = _frameTris[t + e], p1 = _frameTris[t + (e + 1) % 3];
                float d0 = p0.x * c - (p0.y - DepthCenterY) * s, d1 = p1.x * c - (p1.y - DepthCenterY) * s;
                if ((d0 < 0f) == (d1 < 0f)) { continue; }
                Vector3 x = Vector3.Lerp(p0, p1, d0 / (d0 - d1));
                float r = x.x * s + (x.y - DepthCenterY) * c;
                if (r <= 0f || x.z > zc + 0.005f || x.z < zc - FrameWin) { continue; }
                if (r < inner) { inner = r; }
            }
        }
        return inner < float.MaxValue;
    }

    /// 프레임 안쪽보다 깊은 아치 정점을 그 거리까지 바깥으로(q를 고친다). 옮긴 정점 수.
    private static int MatchArch(Mesh main, Vector3[] pf, Vector3[] q)
    {
        if (!ArchMatch || _frameTris == null || _frameTris.Count == 0) { return 0; }
        var used = new System.Collections.Generic.HashSet<int>(main.GetTriangles(0));
        int moved = 0;
        foreach (int i in used)
        {
            Vector3 p = q[i];
            if (p.y <= LiftSill || Mathf.Abs(p.x) > XMax) { continue; }
            float w = BandWeight(pf[i]);
            if (w <= 0f) { continue; }
            float th = Theta(p), zc, fi;
            if (!SeamAt(th, out zc) || p.z < zc - 0.005f || p.z > zc + ArchWin || !FrameInner(th, out fi)) { continue; }
            float r = Radius(p);
            if (r >= fi || fi - r > ArchDepthMax) { continue; }
            float rn = r + (fi - r) * w;
            Vector2 dir = new Vector2(p.x, p.y - DepthCenterY).normalized;
            q[i] += new Vector3(dir.x, dir.y, 0f) * (rn - r);
            moved++;
        }
        return moved;
    }

    // 아치 안쪽 볼트(사용자 요청 — 실기 조종석처럼 앞유리 아치 안쪽에 볼트가 줄지어 박힌 디테일): 아치(Main.E) 복사본에 볼트 정점 · 면을 더한다.
    // 각도마다 아치를 잘라 안쪽 면 가운데 선을 찾고, 그 선을 따라 꼭대기 양쪽 반 간격부터 BoltSpacing 간격, 문턱 위 BoltMinY까지.
    // 간격은 6cm 근처에서 모든 볼트가 평평한 자리에 앉는 값을 골라 고르게(PlaceSide) — 와셔 밑 면이 고르지 않은 곳에 걸리지 않게.
    // 볼트 = 얇은 와셔(8각) + 육각 머리(윗모서리 깎음), 면에 수직으로 서고 밑동은 면 속으로 1mm 묻는다(떠 보이지 않게). 안 보이는 밑면은 없다.
    // 색 · 본은 바로 옆 아치 정점의 UV · 가중치 그대로(같은 색, 동체 본) — 새 오브젝트 · 머티리얼 없음. 면마다 노멀 따로(모서리가 또렷하게).
    public const bool Bolts = true;
    public const float BoltSpacing = 0.06f;
    private const float BoltMinY = 2.86f, BoltStep = 0.5f, BoltMaxTh = 80f, BoltJump = 0.02f;
    private const float WasherEmbed = 0.002f, SeatSpread = 0.001f, SeatAngle = 35f;   // SeatAngle: 단면 방향과 그 자리 면 방향 차 허용(축은 늘 면에 수직)   // 와셔를 면 속으로 묻는 깊이 · 와셔 밑 면 높이 차 허용(넘으면 떠 보이니 자리를 옮기거나 건너뜀)
    private const float WasherR = 0.008f, WasherH = 0.001f, HexR = 0.006f, HexH = 0.0038f, HexTopR = 0.0047f, HexTopH = 0.0046f;

    private struct BoltBuf
    {
        public System.Collections.Generic.List<Vector3> V, N, W;   // N = 음영 노멀(아치 면 방향), W = 면의 실제 방향(감김 확인용)
        public System.Collections.Generic.List<int> T;
        public System.Collections.Generic.List<int> Src;   // 새 정점마다 UV · 가중치를 빌려올 아치 정점
    }

    /// 아치를 각도(도) 평면으로 잘라 안쪽 면 가운데 점(P)과 안쪽을 보는 면 방향(N). 기체 좌표.
    private static bool ArchInner(float th, Vector3[] q, System.Collections.Generic.List<int> tris, out Vector3 P, out Vector3 N)
    {
        P = Vector3.zero;
        N = Vector3.zero;
        float a = th * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
        Vector3 radial = new Vector3(s, c, 0f);
        var segA = new System.Collections.Generic.List<Vector3>();
        var segB = new System.Collections.Generic.List<Vector3>();
        var segN = new System.Collections.Generic.List<Vector3>();
        float rmin = float.MaxValue;
        for (int t = 0; t < tris.Count; t += 3)
        {
            Vector3 p0 = q[tris[t]], p1 = q[tris[t + 1]], p2 = q[tris[t + 2]];
            Vector3 hitA = Vector3.zero, hitB = Vector3.zero;
            int hits = 0;
            for (int e = 0; e < 3; e++)
            {
                Vector3 u = e == 0 ? p0 : e == 1 ? p1 : p2, w = e == 0 ? p1 : e == 1 ? p2 : p0;
                float d0 = u.x * c - (u.y - DepthCenterY) * s, d1 = w.x * c - (w.y - DepthCenterY) * s;
                if ((d0 < 0f) == (d1 < 0f)) { continue; }
                Vector3 x = Vector3.Lerp(u, w, d0 / (d0 - d1));
                if (Vector3.Dot(new Vector3(x.x, x.y - DepthCenterY, 0f), radial) <= 0f) { continue; }
                if (hits == 0) { hitA = x; } else { hitB = x; }
                hits++;
            }
            if (hits != 2) { continue; }
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            if (n.sqrMagnitude < 1e-12f) { continue; }
            n.Normalize();
            if (Vector3.Dot(n, radial) > 0f) { n = -n; }
            segA.Add(hitA);
            segB.Add(hitB);
            segN.Add(n);
            rmin = Mathf.Min(rmin, Mathf.Min(Radius(hitA), Radius(hitB)));
        }
        if (segA.Count == 0) { return false; }
        // 안쪽 면: 가장 안쪽 거리 3mm 안에 있고 안쪽을 보는 조각들
        float zA = float.MaxValue, zB = float.MinValue;
        for (int i = 0; i < segA.Count; i++)
        {
            if (Radius(segA[i]) > rmin + 0.003f || Radius(segB[i]) > rmin + 0.003f || Vector3.Dot(segN[i], -radial) < 0.7f) { continue; }
            zA = Mathf.Min(zA, Mathf.Min(segA[i].z, segB[i].z));
            zB = Mathf.Max(zB, Mathf.Max(segA[i].z, segB[i].z));
        }
        if (zA > zB) { return false; }
        float zc = (zA + zB) * 0.5f;
        for (int i = 0; i < segA.Count; i++)
        {
            if (Radius(segA[i]) > rmin + 0.003f || Radius(segB[i]) > rmin + 0.003f || Vector3.Dot(segN[i], -radial) < 0.7f) { continue; }
            float lo = Mathf.Min(segA[i].z, segB[i].z), hi = Mathf.Max(segA[i].z, segB[i].z);
            if (zc < lo || zc > hi || hi - lo < 1e-6f) { continue; }
            P = Vector3.Lerp(segA[i], segB[i], (zc - segA[i].z) / (segB[i].z - segA[i].z));
            N = segN[i];
            return true;
        }
        return false;
    }

    /// 꼭대기에서 한쪽(dir = ±1)으로 안쪽 면 가운데 선.
    private static System.Collections.Generic.List<Vector3[]> ArchLine(float dir, Vector3[] q, System.Collections.Generic.List<int> tris)
    {
        var line = new System.Collections.Generic.List<Vector3[]>();
        int miss = 0;
        for (float th = 0f; th <= BoltMaxTh; th += BoltStep)
        {
            Vector3 P, N;
            // 정점 줄과 딱 겹치는 각도에선 가운데 점을 못 찾을 때가 있다 — 몇 칸은 건너뛰고, 3° 넘게 연속이면 끝
            if (!ArchInner(dir * th, q, tris, out P, out N)) { if (++miss * BoltStep > 3f) { break; } continue; }
            int steps = miss + 1;
            miss = 0;
            if (P.y < BoltMinY) { break; }
            if (line.Count > 0 && (P - line[line.Count - 1][0]).magnitude > BoltJump * steps) { break; }
            line.Add(new[] { P, N });
        }
        return line;
    }

    // 볼트 음영(사용자 지적 — 옆면이 거의 검어 반투명 HUD 글자 뒤로 진한 육각 테두리가 비쳤다): 면마다 그 자리 아치 면 방향(shade)에 실제 면 방향을 BoltShadeMix만큼 섞어 비춘다.
    // 색은 아치 텍스처가 아니라 볼트 전용 재질(submesh 2, 짙은 회색 — 사용자 요청 "검은색말고 약간 짙은회색") — 검은 쪽 아치에서도 보인다.
    public const float BoltShadeMix = 0.7f;
    public const string BoltMaterialPath = "Assets/ACO_Addon/Generated/Canopy/ACO_CanopyBolt.mat";
    private static readonly Color BoltColor = new Color(0.32f, 0.33f, 0.35f);
    private static void BoltQuad(BoltBuf b, Vector3 a0, Vector3 a1, Vector3 a2, Vector3 a3, Vector3 outward, Vector3 shade, int src)
    {
        Vector3 n = Vector3.Cross(a1 - a0, a3 - a0).normalized;
        if (Vector3.Dot(n, outward) < 0f) { n = -n; }
        int i = b.V.Count;
        b.V.Add(a0); b.V.Add(a1); b.V.Add(a2); b.V.Add(a3);
        Vector3 sn = (shade + n * BoltShadeMix).normalized;   // 아치 면 방향 + 실제 면 방향 BoltShadeMix — 옆면이 조금 어둡게(입체감)
        for (int k = 0; k < 4; k++) { b.N.Add(sn); b.W.Add(n); b.Src.Add(src); }
        b.T.Add(i); b.T.Add(i + 1); b.T.Add(i + 2);
        b.T.Add(i); b.T.Add(i + 2); b.T.Add(i + 3);
    }

    /// 축(P, N) 둘레 sides각 띠: (r0, h0) → (r1, h1).
    private static void BoltBand(BoltBuf b, Vector3 P, Vector3 N, Vector3 X, Vector3 Y, int sides, float r0, float h0, float r1, float h1, int src)
    {
        for (int k = 0; k < sides; k++)
        {
            float u = k * Mathf.PI * 2f / sides, v = (k + 1) * Mathf.PI * 2f / sides;
            Vector3 du = X * Mathf.Cos(u) + Y * Mathf.Sin(u), dv = X * Mathf.Cos(v) + Y * Mathf.Sin(v);
            Vector3 mid = (du + dv).normalized;
            BoltQuad(b, P + N * h0 + du * r0, P + N * h0 + dv * r0, P + N * h1 + dv * r1, P + N * h1 + du * r1, mid, N, src);
        }
    }

    private static void BoltCap(BoltBuf b, Vector3 P, Vector3 N, Vector3 X, Vector3 Y, int sides, float r, float h, int src)
    {
        int c = b.V.Count;
        b.V.Add(P + N * h); b.N.Add(N); b.W.Add(N); b.Src.Add(src);
        for (int k = 0; k < sides; k++)
        {
            float u = k * Mathf.PI * 2f / sides;
            b.V.Add(P + N * h + (X * Mathf.Cos(u) + Y * Mathf.Sin(u)) * r); b.N.Add(N); b.W.Add(N); b.Src.Add(src);
        }
        for (int k = 0; k < sides; k++) { b.T.Add(c); b.T.Add(c + 1 + k); b.T.Add(c + 1 + (k + 1) % sides); }
    }

    private static bool RayTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-12f) { return false; }
        float inv = 1f / det;
        Vector3 s = o - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) { return false; }
        Vector3 qv = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, qv) * inv;
        if (v < 0f || u + v > 1f) { return false; }
        t = Vector3.Dot(e2, qv) * inv;
        return t > 0f;
    }

    /// 아치 면 위 한 점에서 방향 -dir로 쏜 광선이 처음 닿는 거리와 삼각형(없으면 -1).
    private static int CastArch(Vector3 start, Vector3 dir, Vector3 near, Vector3[] q, System.Collections.Generic.List<int> tris, out float dist)
    {
        dist = float.MaxValue;
        int hit = -1;
        for (int t = 0; t < tris.Count; t += 3)
        {
            Vector3 a = q[tris[t]];
            if ((a - near).sqrMagnitude > 0.01f) { continue; }
            float d;
            if (RayTri(start, -dir, a, q[tris[t + 1]], q[tris[t + 2]], out d) && d < dist) { dist = d; hit = t; }
        }
        return hit;
    }

    /// 볼트 자리: 가운데 아래 면을 찾아 축을 그 면에 수직으로 잡고(단면 방향 N과 SeatAngle 안), 그 축으로 와셔 테두리 8곳 아래 높이를 재서
    /// 높이 차 SeatSpread 안이면(평평) 가장 높은 곳에 앉힌다. 꺾이는 구간처럼 면이 단면 방향에서 기울어 있어도 평평하면 앉고, 접힌 선에 걸치면 안 된다.
    private static bool Seat(Vector3 P, Vector3 N, Vector3 along, Vector3[] q, System.Collections.Generic.List<int> tris, out Vector3 seated, out Vector3 normal)
    {
        seated = P;
        normal = N;
        float d0;
        int t0 = CastArch(P + N * 0.02f, N, P, q, tris, out d0);
        if (t0 < 0) { return false; }
        Vector3 H = P + N * (0.02f - d0);
        Vector3 fn = Vector3.Cross(q[tris[t0 + 1]] - q[tris[t0]], q[tris[t0 + 2]] - q[tris[t0]]).normalized;
        if (Vector3.Dot(fn, N) < 0f) { fn = -fn; }
        if (Vector3.Angle(fn, N) > SeatAngle) { return false; }
        Vector3 X = Vector3.ProjectOnPlane(along, fn).normalized;
        if (X.sqrMagnitude < 0.5f) { X = Vector3.Cross(fn, Vector3.forward).normalized; }
        Vector3 Y = Vector3.Cross(fn, X);
        float hi = 0f, lo = 0f;
        for (int k = 0; k < 8; k++)
        {
            float u = k * Mathf.PI / 4f;
            Vector3 at = H + (X * Mathf.Cos(u) + Y * Mathf.Sin(u)) * WasherR;
            float d;
            if (CastArch(at + fn * 0.02f, fn, H, q, tris, out d) < 0) { return false; }
            float h = 0.02f - d;
            hi = Mathf.Max(hi, h);
            lo = Mathf.Min(lo, h);
        }
        if (hi - lo > SeatSpread) { return false; }
        seated = H + fn * hi;
        normal = fn;
        return true;
    }

    private static void Bolt(BoltBuf b, Vector3 P, Vector3 N, Vector3 along, int src)
    {
        Vector3 X = Vector3.ProjectOnPlane(along, N).normalized;
        if (X.sqrMagnitude < 0.5f) { X = Vector3.Cross(N, Vector3.forward).normalized; }
        Vector3 Y = Vector3.Cross(N, X);
        BoltBand(b, P, N, X, Y, 8, WasherR, -WasherEmbed, WasherR, WasherH, src);
        BoltCap(b, P, N, X, Y, 8, WasherR, WasherH, src);
        BoltBand(b, P, N, X, Y, 6, HexR, WasherH, HexR, HexH, src);
        BoltBand(b, P, N, X, Y, 6, HexR, HexH, HexTopR, HexTopH, src);
        BoltCap(b, P, N, X, Y, 6, HexTopR, HexTopH, src);
    }

    /// 선 위 거리 s(꼭대기에서)의 점 · 면 방향 · 진행 방향.
    private static void LineAt(System.Collections.Generic.List<Vector3[]> line, float[] cum, float s, out Vector3 P, out Vector3 N, out Vector3 dir)
    {
        int i = 0;
        while (i < line.Count - 2 && cum[i + 1] < s) { i++; }
        float seg = cum[i + 1] - cum[i];
        float f = seg > 1e-6f ? Mathf.Clamp01((s - cum[i]) / seg) : 0f;
        P = Vector3.Lerp(line[i][0], line[i + 1][0], f);
        N = Vector3.Slerp(line[i][1], line[i + 1][1], f).normalized;
        dir = (line[i + 1][0] - line[i][0]).normalized;
    }

    /// 한쪽 볼트 자리: 꼭대기에서 반 간격부터 고른 간격으로 — 6cm 근처(±1cm, 1mm 단위)에서 모든 볼트가 평평한 자리(Seat)에 앉는 간격을 고른다
    /// (사용자 지적 "저기만 간격이 많이 떠서 이상하다" — 아치가 꺾이는 곳은 면이 접혀 6.5cm쯤 앉을 자리가 없어, 6cm 고정이면 한 칸이 빠졌다).
    /// 각 볼트는 제자리 ±5mm 안에서만 움직인다. 맞는 간격이 없으면 6cm로 두고 앉을 수 없는 자리만 건너뛴다. 결과: [자리, 축, 진행 방향].
    private static System.Collections.Generic.List<Vector3[]> PlaceSide(System.Collections.Generic.List<Vector3[]> line, Vector3[] q, System.Collections.Generic.List<int> tris, out float spacing, ref int skipped)
    {
        var cum = new float[line.Count];
        for (int i = 1; i < line.Count; i++) { cum[i] = cum[i - 1] + (line[i][0] - line[i - 1][0]).magnitude; }
        float L = cum[line.Count - 1];
        for (int k = 0; k <= 20; k++)
        {
            float d = BoltSpacing + ((k + 1) / 2) * 0.001f * (k % 2 == 1 ? 1f : -1f);
            var seats = new System.Collections.Generic.List<Vector3[]>();
            bool all = true;
            for (float s = d * 0.5f; s <= L && all; s += d)
            {
                Vector3[] st;
                all = SeatNear(line, cum, s, 0.005f, q, tris, out st);
                if (all) { seats.Add(st); }
            }
            if (all) { spacing = d; return seats; }
        }
        spacing = BoltSpacing;
        var some = new System.Collections.Generic.List<Vector3[]>();
        for (float s = BoltSpacing * 0.5f; s <= L; s += BoltSpacing)
        {
            Vector3[] st;
            if (SeatNear(line, cum, s, 0.005f, q, tris, out st)) { some.Add(st); } else { skipped++; }
        }
        return some;
    }

    /// 선 위 s 근처(±tol, 2.5mm 단위)에서 와셔가 앉는 첫 자리.
    private static bool SeatNear(System.Collections.Generic.List<Vector3[]> line, float[] cum, float s, float tol, Vector3[] q, System.Collections.Generic.List<int> tris, out Vector3[] st)
    {
        st = null;
        for (int t = 0; t * 0.0025f <= tol * 2f + 1e-5f; t++)
        {
            float off = ((t + 1) / 2) * 0.0025f * (t % 2 == 1 ? 1f : -1f);
            if (Mathf.Abs(off) > tol + 1e-5f) { break; }
            Vector3 P, N, dir, seated, axis;
            LineAt(line, cum, s + off, out P, out N, out dir);
            // 안쪽 면이 좁거나 치우친 곳(꺾이는 구간)은 옆으로도 3 · 6mm 옮겨 본다 — 와셔 한쪽이 면 밖으로 떨어지지 않게
            Vector3 side = Vector3.Cross(N, dir).normalized;
            for (int l = 0; l < 5; l++)
            {
                float lat = ((l + 1) / 2) * 0.003f * (l % 2 == 1 ? 1f : -1f);
                if (Seat(P + side * lat, N, dir, q, tris, out seated, out axis)) { st = new[] { seated, axis, dir }; return true; }
            }
        }
        return false;
    }

    /// 아치 안쪽 볼트를 m(이미 변위를 넣은 Main.E 복사본)에 더한다. q = 기체 좌표 최종 자리. 박은 볼트 수.
    private static int AddBolts(Mesh m, Vector3[] q, Matrix4x4 toMesh)
    {
        if (!Bolts || _seamTh == null) { return 0; }
        int[] tri0 = m.GetTriangles(0);
        var tris = new System.Collections.Generic.List<int>();
        var archV = new System.Collections.Generic.List<int>();
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int t = 0; t < tri0.Length; t += 3)
        {
            bool all = true;
            for (int e = 0; e < 3 && all; e++)
            {
                Vector3 p = q[tri0[t + e]];
                float zc;
                all = p.y > LiftSill - 0.05f && Mathf.Abs(p.x) <= XMax && SeamAt(Theta(p), out zc) && p.z >= zc - 0.01f && p.z <= zc + ArchWin + 0.01f;
            }
            if (!all) { continue; }
            for (int e = 0; e < 3; e++) { tris.Add(tri0[t + e]); if (seen.Add(tri0[t + e])) { archV.Add(tri0[t + e]); } }
        }
        if (tris.Count == 0) { return 0; }
        var b = new BoltBuf
        {
            V = new System.Collections.Generic.List<Vector3>(), N = new System.Collections.Generic.List<Vector3>(), W = new System.Collections.Generic.List<Vector3>(),
            T = new System.Collections.Generic.List<int>(), Src = new System.Collections.Generic.List<int>()
        };
        int count = 0, skipped = 0;
        // 한쪽(+x)만 자리를 찾고 반대쪽은 x를 뒤집어 그대로 박는다(사용자 요청 — 양쪽 대칭). 아치는 좌우 대칭이다.
        var line = ArchLine(1f, q, tris);
        if (line.Count >= 2)
        {
            float spacing;
            var seats = PlaceSide(line, q, tris, out spacing, ref skipped);
            foreach (Vector3[] st in seats)
            {
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    Vector3 P = st[0], N = st[1], D = st[2];
                    if (mirror == 1) { P.x = -P.x; N.x = -N.x; D.x = -D.x; }
                    int src = archV[0];
                    float best = float.MaxValue;
                    foreach (int j in archV) { float d = (q[j] - P).sqrMagnitude; if (d < best) { best = d; src = j; } }
                    Bolt(b, P, N, D, src);
                    count++;
                }
            }
            Debug.Log($"[ACO] 캐노피 프레임: 아치 볼트 간격 {spacing * 100f:0.0}cm(한쪽 {seats.Count}개, 반대쪽은 거울).");
        }
        if (skipped > 0) { Debug.Log($"[ACO] 캐노피 프레임: 와셔가 면에 붙지 않는 볼트 자리 {skipped}곳은 건너뛰었습니다."); }
        if (count == 0) { return 0; }
        int baseN = m.vertexCount;
        if (baseN + b.V.Count > 65000) { m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
        var verts = new System.Collections.Generic.List<Vector3>(m.vertices);
        var norms = new System.Collections.Generic.List<Vector3>(m.normals);
        var tans = new System.Collections.Generic.List<Vector4>(m.tangents);
        var uvs = new System.Collections.Generic.List<Vector2>(m.uv);
        var bws = new System.Collections.Generic.List<BoneWeight>(m.boneWeights);
        var wind = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < b.V.Count; i++)
        {
            verts.Add(toMesh.MultiplyPoint3x4(b.V[i]));
            Vector3 n = toMesh.MultiplyVector(b.N[i]).normalized;
            norms.Add(n);
            wind.Add(toMesh.MultiplyVector(b.W[i]).normalized);
            Vector3 tg = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            tans.Add(new Vector4(tg.x, tg.y, tg.z, 1f));
            uvs.Add(uvs[b.Src[i]]);
            bws.Add(bws[b.Src[i]]);
        }
        var sub2 = new System.Collections.Generic.List<int>();
        for (int t = 0; t < b.T.Count; t += 3)
        {
            int i0 = b.T[t], i1 = b.T[t + 1], i2 = b.T[t + 2];
            // 메시 좌표에서 앞면이 노멀 쪽을 보게(감김 방향 확인)
            Vector3 cr = Vector3.Cross(verts[baseN + i1] - verts[baseN + i0], verts[baseN + i2] - verts[baseN + i0]);
            if (Vector3.Dot(cr, wind[i0]) < 0f) { int x = i1; i1 = i2; i2 = x; }
            sub2.Add(baseN + i0); sub2.Add(baseN + i1); sub2.Add(baseN + i2);
        }
        int[] tri1 = m.GetTriangles(1);
        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetTangents(tans);
        m.SetUVs(0, uvs);
        m.boneWeights = bws.ToArray();
        m.subMeshCount = 3;
        m.SetTriangles(tri0, 0);
        m.SetTriangles(tri1, 1);
        m.SetTriangles(sub2, 2);   // 볼트 — 전용 재질(BoltMaterial)
        return count;
    }

    // 유리 끝 넣기(사용자 지적 — 앞유리가 맞지 않아 떨어져 보인다): 띠(아치 · 캐노피 앞 프레임)를 얇게 · 얕게 하면서 프레임 안쪽 면은 바깥으로 올렸는데,
    // 원래 프레임 속에 묻혀 있던 유리 끝(submesh 1)은 그대로라 안쪽 면 아래로 드러나 아치를 따라 옅은 띠로 보였다.
    // 단면 중심(x = 0, y = DepthCenterY, 같은 z)에서 유리 정점 쪽으로 광선을 쏴 처음 닿는 프레임 면(= 안쪽 면, 거울 · 볼트 같은 작은 덩어리 제외)보다
    // 유리가 안쪽이면 그 면 바로 바깥(GlassTuck)으로 옮긴다 — 다시 프레임 속에 묻힌다. 바깥에서 보이는 유리(프레임 바깥면 근처)는 안쪽 면보다 바깥이라 그대로.
    public const bool GlassFix = true;
    private const float GlassTuck = 0.0015f;

    private static int TuckGlass(Mesh orig, Vector3[] pf, Vector3[] q)
    {
        if (!GlassFix || orig.subMeshCount < 2) { return 0; }
        System.Collections.Generic.HashSet<int> used;
        System.Collections.Generic.Dictionary<int, int> size;
        int[] grp = Groups(orig, q, out used, out size);
        int[] t0 = orig.GetTriangles(0);
        var tris = new System.Collections.Generic.List<int>();
        for (int t = 0; t < t0.Length; t += 3)
        {
            if (size[grp[t0[t]]] <= 300) { continue; }
            bool band = false;
            for (int e = 0; e < 3; e++) { if (BandWeight(pf[t0[t + e]]) > 0f || BandWeight(q[t0[t + e]]) > 0f) { band = true; } }
            if (band) { tris.Add(t0[t]); tris.Add(t0[t + 1]); tris.Add(t0[t + 2]); }
        }
        if (tris.Count == 0) { return 0; }
        var glass = new System.Collections.Generic.HashSet<int>(orig.GetTriangles(1));
        int moved = 0;
        foreach (int g in glass)
        {
            if (BandWeight(pf[g]) <= 0f) { continue; }
            Vector3 p = q[g];
            Vector3 a = new Vector3(0f, DepthCenterY, p.z);
            Vector3 dir = new Vector3(p.x, p.y - DepthCenterY, 0f);
            float r = dir.magnitude;
            if (r < 0.1f) { continue; }
            dir /= r;
            float hit = float.MaxValue;
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 v0 = q[tris[t]];
                if (Mathf.Abs(v0.z - p.z) > 0.25f) { continue; }
                float d;
                if (RayTri(a, dir, v0, q[tris[t + 1]], q[tris[t + 2]], out d) && d < hit) { hit = d; }
            }
            if (hit == float.MaxValue || r >= hit + GlassTuck) { continue; }
            q[g] = a + dir * (hit + GlassTuck);
            moved++;
        }
        return moved;
    }

    // 캐노피 프레임 면에 직접 붙은 작은 부품(손잡이 2 · 옆 거울 2) 붙이기(사용자 지적 "캐노피 프레임 손잡이가 공중에 떠 있다"):
    // 프레임 안쪽 면은 깊이 줄이기(Flatten)로 바깥쪽으로 많이 옮겨지는데, 부품은 자기 가장 바깥 정점(프레임 속에 박힌 끝 — 바깥면 근처라 변위가 작다)의
    // 변위로 옮겨져 안쪽 면과 벌어졌다. 부품 둘레(GlueReach) 프레임 안쪽 면(단면 중심에서 가장 가까운 점 + GlueBand)의 변위 평균으로 통째로 옮긴다.
    private const float GlueBand = 0.005f, GlueReach = 0.02f, GlueTouch = 0.003f;

    private static Vector3 ClosestOnTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) { bary = new Vector3(1f, 0f, 0f); return a; }
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) { bary = new Vector3(0f, 1f, 0f); return b; }
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); bary = new Vector3(1f - v, v, 0f); return a + ab * v; }
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) { bary = new Vector3(0f, 0f, 1f); return c; }
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); bary = new Vector3(1f - w, 0f, w); return a + ac * w; }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); bary = new Vector3(0f, 1f - w, w); return b + (c - b) * w; }
        float denom = 1f / (va + vb + vc);
        float vv = vb * denom, ww = vc * denom;
        bary = new Vector3(1f - vv - ww, vv, ww);
        return a + ab * vv + ac * ww;
    }

    /// 작은 덩어리(submesh 0, 정점 300 이하)를 붙은 프레임 면의 변위로 통째로 옮긴다(q를 고친다). 옮긴 덩어리 수.
    private static int GlueParts(Mesh orig, Vector3[] pf, Vector3[] q)
    {
        System.Collections.Generic.HashSet<int> used;
        System.Collections.Generic.Dictionary<int, int> size;
        int[] grp = Groups(orig, pf, out used, out size);
        int[] t0 = orig.GetTriangles(0);
        var frame = new System.Collections.Generic.List<int>();
        for (int t = 0; t < t0.Length; t += 3)
        {
            if (size[grp[t0[t]]] > 300) { frame.Add(t0[t]); frame.Add(t0[t + 1]); frame.Add(t0[t + 2]); }
        }
        if (frame.Count == 0) { return 0; }
        var parts = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
        foreach (int i in used)
        {
            if (size[grp[i]] > 300) { continue; }
            if (!parts.ContainsKey(grp[i])) { parts[grp[i]] = new System.Collections.Generic.List<int>(); }
            parts[grp[i]].Add(i);
        }
        int glued = 0;
        var candR = new System.Collections.Generic.List<float>();
        var candD = new System.Collections.Generic.List<Vector3>();
        foreach (var part in parts.Values)
        {
            // 부품 정점마다 GlueReach 안의 프레임 면 점(가장 가까운 점) — 그중 안쪽 면(단면 중심에서 가장 가까운 것, + GlueBand)의 변위 평균.
            // 부품 끝은 프레임 속에 박혀 있어 가장 가까운 면이 바깥면일 수 있다 — 안쪽 면으로 고른다(부품은 조종석 쪽에 붙어 있다).
            candR.Clear();
            candD.Clear();
            float touch = float.MaxValue;
            foreach (int i in part)
            {
                Vector3 p = pf[i];
                for (int t = 0; t < frame.Count; t += 3)
                {
                    int a = frame[t], b = frame[t + 1], c = frame[t + 2];
                    Vector3 bary;
                    Vector3 cpt = ClosestOnTri(p, pf[a], pf[b], pf[c], out bary);
                    float d2 = (cpt - p).sqrMagnitude;
                    if (d2 < touch) { touch = d2; }
                    if (d2 > GlueReach * GlueReach) { continue; }
                    candR.Add(Radius(cpt));
                    candD.Add((q[a] - pf[a]) * bary.x + (q[b] - pf[b]) * bary.y + (q[c] - pf[c]) * bary.z);
                }
            }
            // 프레임 면에 직접 닿은 부품만(손잡이 · 옆 거울 — 1~2mm). 가운데 거울은 면에서 약 7mm 떨어져 매달린 모양이라 붙이면 오히려 벌어진다 — 예전 방식(Flatten) 그대로.
            if (candR.Count == 0 || touch > GlueTouch * GlueTouch) { continue; }
            float minR = float.MaxValue;
            foreach (float r in candR) { if (r < minR) { minR = r; } }
            Vector3 sum = Vector3.zero;
            int cnt = 0;
            for (int k = 0; k < candR.Count; k++)
            {
                if (candR[k] > minR + GlueBand) { continue; }
                sum += candD[k];
                cnt++;
            }
            Vector3 move = sum / cnt;
            foreach (int i in part) { q[i] = pf[i] + move; }
            glued++;
        }
        return glued;
    }

    /// 원본 복사본에 변위를 넣어 저장한다(있으면 덮어쓴다). 노멀 · 탄젠트 · UV · 가중치는 그대로.
    private static Mesh Build(Mesh orig, SkinnedMeshRenderer smr, Transform frame)
    {
        // 쉬는 자세: 이 부위의 메시 정점 = SMR 로컬(스케일 1) — 기체 좌표로 바꿔 변위를 구하고 다시 메시 좌표로
        Matrix4x4 toFrame = ToFrame(smr, frame);
        Matrix4x4 toMesh = toFrame.inverse;
        Mesh m = Object.Instantiate(orig);
        m.name = orig.name + "_ACOThin";
        Vector3[] v = m.vertices;
        Vector3[] pf;
        Vector3[] q = Final(orig, toFrame, out pf);
        int matched = orig.name == Targets[1] ? MatchArch(orig, pf, q) : 0;
        int glued = orig.name == Targets[0] ? GlueParts(orig, pf, q) : 0;
        int glass = TuckGlass(orig, pf, q);
        int moved = 0;
        float maxShift = 0f;
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 d = q[i] - pf[i];
            if (d == Vector3.zero) { continue; }
            v[i] += toMesh.MultiplyVector(d);
            moved++;
            maxShift = Mathf.Max(maxShift, d.magnitude);
        }
        m.vertices = v;
        int bolts = orig.name == Targets[1] ? AddBolts(m, q, toMesh) : 0;
        m.RecalculateBounds();
        if (!AssetDatabase.IsValidFolder(Dir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "Canopy"); }
        string path = $"{Dir}/{m.name}.asset";
        Mesh old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old == null) { AssetDatabase.CreateAsset(m, path); old = m; }
        else
        {
            // CopySerialized는 정점 수가 바뀐 메시(볼트 추가)를 제대로 옮기지 못한다 — 같은 에셋(참조 유지)에 데이터를 직접 옮겨 담는다
            CopyMesh(m, old);
            Object.DestroyImmediate(m);
            EditorUtility.SetDirty(old);
        }
        Debug.Log($"[ACO] 캐노피 프레임: {orig.name} 정점 {moved}개를 옮겼습니다(최대 {maxShift * 100f:0.0}cm" + (matched > 0 ? $", 아치 안쪽 맞추기 {matched}개" : "") + (glued > 0 ? $", 부품 붙이기 {glued}개" : "") + (glass > 0 ? $", 유리 끝 넣기 {glass}개" : "") + (bolts > 0 ? $", 볼트 {bolts}개" : "") + ").");
        return old;
    }

    /// 메시 데이터(위치 · 노멀 · 탄젠트 · UV · 본 가중치 · 바인드포즈 · 서브메시)를 dst에 옮긴다. FA-18E 메시가 쓰는 채널만.
    private static void CopyMesh(Mesh src, Mesh dst)
    {
        string name = dst.name;
        dst.Clear();
        dst.indexFormat = src.indexFormat;
        dst.vertices = src.vertices;
        dst.normals = src.normals;
        dst.tangents = src.tangents;
        dst.uv = src.uv;
        dst.boneWeights = src.boneWeights;
        dst.bindposes = src.bindposes;
        dst.subMeshCount = src.subMeshCount;
        for (int i = 0; i < src.subMeshCount; i++) { dst.SetTriangles(src.GetTriangles(i), i); }
        dst.bounds = src.bounds;
        dst.name = name;
    }
}
