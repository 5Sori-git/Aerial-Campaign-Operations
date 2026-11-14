using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using ACO;

/// 기체 표시 글자 판의 곡면 맞춤(6-192): 헐넘버(기수 Hull_L · Hull_R)와 플랩 헐넘버(Flap.L · Flap.R 아래 ACO_FlapHull)는 평평한 TMP 판이라
/// 둥근 기수 · 기운 플랩 위에서 뜨거나 묻혔다(최대 6 · 9cm — VR에선 시차로 보인다). 판 아래 기체 표면을 재서(쉬는 자세 스킨 메시에 CPU 광선)
/// 판 좌표의 3차 곡면 z = f(x, y)로 맞추고, 글자 셰이더(ACO/MarkText _BendA · _BendB · _BendC)가 정점을 그 곡면으로 휜다 — 글자가 바뀌어도 그대로 붙는다.
/// 판마다 머티리얼 하나(Generated/Marks/ACO_MarkText_Fit_*) — 같은 모델 · 같은 자리면 기체 프리팹과 격납고 샘플이 같이 쓴다.
/// 판을 옮겼으면 다시 맞춘다(Setup Check 경고 · 메뉴 ACO/Fit Marks To Surface).
public static class ACO_MarkSurfaceFit
{
    private const string BasePath = "Assets/ACO_Addon/Generated/ACO_MarkText.mat";
    private const string Dir = "Assets/ACO_Addon/Generated/Marks";
    private const string Prefix = "ACO_MarkText_Fit_";
    private const float Lift = 0.004f;   // 표면 위 4mm(셰이더가 카메라 쪽으로 조금 더 당긴다)

    /// 맞출 판: 기수 헐넘버 · 플랩 헐넘버. key = 머티리얼 이름 꼬리.
    private static List<KeyValuePair<string, TextMeshPro>> Targets(Transform vehicle)
    {
        var list = new List<KeyValuePair<string, TextMeshPro>>();
        foreach (TextMeshPro t in vehicle.GetComponentsInChildren<TextMeshPro>(true))
        {
            if (t.name == "Hull_L" || t.name == "Hull_R") { list.Add(new KeyValuePair<string, TextMeshPro>(t.name, t)); }
            else if (t.name == ACO_HUDBuilder.FlapMarkName && t.transform.parent != null)
            {
                list.Add(new KeyValuePair<string, TextMeshPro>("FlapHull_" + (t.transform.parent.name.EndsWith(".L") ? "L" : "R"), t));
            }
        }
        return list;
    }

    /// 아직 곡면에 맞추지 않은 판이 있는가(Setup Check).
    public static bool Unfitted()
    {
        foreach (Transform v in Vehicles())
        {
            foreach (var kv in Targets(v))
            {
                Material m = kv.Value.fontSharedMaterial;
                if (m == null || !m.name.StartsWith(Prefix)) { return true; }
            }
        }
        return false;
    }

    private static List<Transform> Vehicles()
    {
        var list = new List<Transform>();
        foreach (ACO_VehicleUpgrades up in Object.FindObjectsOfType<ACO_VehicleUpgrades>(true))
        {
            var e = up.GetComponentInParent<SaccFlightAndVehicles.SaccEntity>(true);
            if (e != null && !list.Contains(e.transform)) { list.Add(e.transform); }
        }
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        if (studio != null && studio.SampleRoot != null) { list.Add(studio.SampleRoot); }
        return list;
    }

    /// mark-fit-1: 플레이어 기체 프리팹(또는 씬 기체)과 격납고 샘플의 글자 판을 곡면에 맞춘다.
    [MenuItem("ACO/Fit Marks To Surface")]
    public static void FitAll()
    {
        var done = new List<string>();
        ACO_DecalStudio studio = Object.FindObjectOfType<ACO_DecalStudio>(true);
        foreach (Transform v in Vehicles())
        {
            bool sample = studio != null && v == studio.SampleRoot;
            string path = sample ? null : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(v.gameObject);
            if (string.IsNullOrEmpty(path))
            {
                Fit(v);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(v.gameObject.scene);
                continue;
            }
            if (done.Contains(path)) { continue; }
            done.Add(path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var e = root.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true);
                if (e != null && Fit(e.transform) > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
    }

    /// 한 기체의 판들을 맞춘다. 맞춘 판 수.
    private static int Fit(Transform vehicle)
    {
        var targets = Targets(vehicle);
        if (targets.Count == 0) { return 0; }
        List<Vector3> tris = ACO_RoundelBuilder.SurfaceTriangles(vehicle, vehicle);
        if (tris.Count == 0) { return 0; }
        Material baseMat = AssetDatabase.LoadAssetAtPath<Material>(BasePath);
        if (baseMat == null) { Debug.LogWarning($"[ACO] 표시 곡면 맞춤: {BasePath}가 없습니다(기체 표시를 먼저 만드세요)."); return 0; }
        int fitted = 0;
        foreach (var kv in targets)
        {
            TextMeshPro tmp = kv.Value;
            Transform t = tmp.transform;
            // 맞출 영역 = 가장 긴 헐넘버(6자)의 글자 자리(+5%) — 판 위아래 빈 여백은 빼야 둥근 곳에서 덜 어긋난다. 짧은 글자는 이 안에 놓인다.
            string keep = tmp.text;
            tmp.text = "VFA-41";
            tmp.ForceMeshUpdate();
            Bounds tb = tmp.textBounds;
            tmp.text = keep;
            tmp.ForceMeshUpdate();
            Rect r = tb.size.x > 0.01f
                ? new Rect(tb.min.x - tb.size.x * 0.05f, tb.min.y - tb.size.y * 0.05f, tb.size.x * 1.1f, tb.size.y * 1.1f)
                : tmp.rectTransform.rect;
            Vector3 outW = -t.forward;   // 글자 앞면(-Z) = 바깥
            Vector3 outV = vehicle.InverseTransformDirection(outW);
            Vector3 centerV = vehicle.InverseTransformPoint(t.TransformPoint(r.center));
            var near = new List<int>();
            float reach = Mathf.Max(r.width, r.height) + 0.6f;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 m = (tris[i] + tris[i + 1] + tris[i + 2]) / 3f;
                if ((m - centerV).sqrMagnitude < reach * reach) { near.Add(i); }
            }
            var X = new List<float>(); var Y = new List<float>(); var Z = new List<float>();
            for (int j = 0; j <= 6; j++)
            {
                for (int i = 0; i <= 10; i++)
                {
                    var lp = new Vector3(Mathf.Lerp(r.xMin, r.xMax, i / 10f), Mathf.Lerp(r.yMin, r.yMax, j / 6f), 0f);
                    Vector3 pV = vehicle.InverseTransformPoint(t.TransformPoint(lp));
                    Vector3 hit, n;
                    if (!ACO_RoundelBuilder.Ray(tris, near, pV + outV * 0.15f, -outV, 0.3f, out hit, out n)) { continue; }
                    Vector3 hl = t.InverseTransformPoint(vehicle.TransformPoint(hit));
                    X.Add(lp.x); Y.Add(lp.y); Z.Add(hl.z);
                }
            }
            if (X.Count < 12) { Debug.LogWarning($"[ACO] 표시 곡면 맞춤: '{vehicle.name}'의 {kv.Key} 아래 표면을 충분히 찾지 못했습니다({X.Count}점) — 평판 그대로."); continue; }
            double[] c = FitCubic(X, Y, Z);
            float err = 0f;
            for (int k = 0; k < X.Count; k++)
            {
                err = Mathf.Max(err, Mathf.Abs(Z[k] - (float)Eval(c, X[k], Y[k])));
            }
            Material mat = EnsureMaterial(kv.Key, baseMat);
            // 바깥 = -Z — 표면에서 Lift만큼 바깥으로
            mat.SetVector("_BendA", new Vector4((float)c[0] - Lift, (float)c[1], (float)c[2], 0f));
            mat.SetVector("_BendB", new Vector4((float)c[3], (float)c[4], (float)c[5], 0f));
            mat.SetVector("_BendC", new Vector4((float)c[6], (float)c[7], (float)c[8], (float)c[9]));
            EditorUtility.SetDirty(mat);
            if (tmp.fontSharedMaterial != mat)
            {
                Undo.RecordObject(tmp, "ACO mark fit");
                tmp.fontSharedMaterial = mat;
                EditorUtility.SetDirty(tmp);
                PrefabUtility.RecordPrefabInstancePropertyModifications(tmp);
            }
            fitted++;
            Debug.Log($"[ACO] 표시 곡면 맞춤: '{vehicle.name}' {kv.Key} — 표면 {X.Count}점, 맞춘 뒤 최대 어긋남 {err * 100f:0.0}cm.");
        }
        return fitted;
    }

    private static Material EnsureMaterial(string key, Material baseMat)
    {
        if (!AssetDatabase.IsValidFolder(Dir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "Marks"); }
        string path = $"{Dir}/{Prefix}{key}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(baseMat) { name = Prefix + key };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
        // 글자 기본 머티리얼(아틀라스 · 색 · 페이드)을 따라간다 — 곡면 값만 판마다
        Vector4 a = m.GetVector("_BendA"), b = m.GetVector("_BendB"), c3 = m.GetVector("_BendC");
        m.CopyPropertiesFromMaterial(baseMat);
        m.SetVector("_BendA", a);
        m.SetVector("_BendB", b);
        m.SetVector("_BendC", c3);
        return m;
    }

    private static double[] Terms(double x, double y) { return new[] { 1, x, y, x * x, y * y, x * y, x * x * x, y * y * y, x * x * y, x * y * y }; }

    private static double Eval(double[] c, double x, double y)
    {
        double[] f = Terms(x, y);
        double v = 0;
        for (int i = 0; i < f.Length; i++) { v += c[i] * f[i]; }
        return v;
    }

    /// z = c0 + c1·x + c2·y + c3·x² + c4·y² + c5·xy + c6·x³ + c7·y³ + c8·x²y + c9·xy² 최소제곱.
    private static double[] FitCubic(List<float> X, List<float> Y, List<float> Z)
    {
        const int n = 10;
        var A = new double[n, n];
        var B = new double[n];
        for (int k = 0; k < X.Count; k++)
        {
            double x = X[k], y = Y[k];
            double[] f = Terms(x, y);
            for (int i = 0; i < n; i++)
            {
                B[i] += f[i] * Z[k];
                for (int j = 0; j < n; j++) { A[i, j] += f[i] * f[j]; }
            }
        }
        for (int i = 0; i < n; i++)
        {
            int piv = i;
            for (int r = i + 1; r < n; r++) { if (System.Math.Abs(A[r, i]) > System.Math.Abs(A[piv, i])) { piv = r; } }
            for (int c = 0; c < n; c++) { double t = A[i, c]; A[i, c] = A[piv, c]; A[piv, c] = t; }
            double tb = B[i]; B[i] = B[piv]; B[piv] = tb;
            if (System.Math.Abs(A[i, i]) < 1e-12) { continue; }
            for (int r = 0; r < n; r++)
            {
                if (r == i) { continue; }
                double fct = A[r, i] / A[i, i];
                for (int c = 0; c < n; c++) { A[r, c] -= fct * A[i, c]; }
                B[r] -= fct * B[i];
            }
        }
        var sol = new double[n];
        for (int i = 0; i < n; i++) { sol[i] = System.Math.Abs(A[i, i]) < 1e-12 ? 0 : B[i] / A[i, i]; }
        return sol;
    }
}
