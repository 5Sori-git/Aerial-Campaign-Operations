using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// F형 동체 합성 텍스처(2026-11-21 — 사용자 "전부 E형과 같게", A안): E 텍스처를 바탕으로 하고, F 메시에만 있는 면(뒷캐노피 · 등판 등)이 쓰는 자리만 F 텍스처로 채운다.
///  - F 전용 판정: F 면(동체 · 캐노피 · 착륙장치)의 UV 점마다 E 메시에서 같은 UV를 쓰는 면을 찾아 3D 위치를 비교 — 없거나 Tolerance보다 멀면 F 전용.
///  - 마스크(MaskSize)를 그리고 넓혀(Dilate) · 흐리게(Feather) 해 경계를 섞은 뒤, 기본 색 · 금속 · AO · 노멀 · 발광 5장을 PNG 원본끼리 합성한다(가져오기 설정은 F 원본과 같게).
///  - 결과: Generated/FTex/ACO_F_*.png. 설치 도구의 F 재질(ACO_FA-18F)이 있으면 이것을 쓴다.
public static class ACO_FTextureComposite
{
    public const string OutDir = "Assets/ACO_Addon/Generated/FTex";
    private const string EPrefab = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
    private const string EMatPath = "Assets/Pit-bool/FA-18/material/E/FA-18E.mat";
    private const string FMatPath = "Assets/Pit-bool/FA-18/material/F/FA-18F.mat";
    public static readonly string[] Props = { "_MainTex", "_MetallicGlossMap", "_OcclusionMap", "_BumpMap", "_EmissionMap" };
    private static readonly string[] Parts = { "Main", "Canopy", "MainGear", "Nosegear" };

    public const float Tolerance = 0.03f;
    [Tooltip("F 원본 재질은 노멀 세기 −2.3(뒤집어 씀) — E와 같은 +1로 쓰려면 F 노멀의 X · Y를 뒤집는다(패널 선이 튀어나와 하얗게 뜨지 않게)")]
    public static bool FlipFNormal = true;
    /// F 기본 색의 밝은 패널 선(주변보다 밝은 가는 선)을 E처럼 어둡게 — 0 = 끔, 1 = 밝은 만큼 그대로 어둡게 뒤집기
    public static float DarkenFLines = 0f;
    /// 다시 긋는 선의 밝기(주변 평균 대비 — E 패널 선 정도)
    public static float LineShade = 0.72f;
    private const int MaskSize = 2048;
    private const int Dilate = 3;
    private const int Feather = 5;

    public static string OutPath(string prop) { return $"{OutDir}/ACO_F{prop}.png"; }

    [MenuItem("ACO/Airframe Variant/Build F Composite Textures")]
    public static string Build()
    {
        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(EPrefab);
        if (go == null) { return "E 프리팹 없음"; }
        var mask = new float[MaskSize * MaskSize];
        int fOnly = 0, total = 0;
        foreach (string part in Parts)
        {
            SkinnedMeshRenderer e = Find(go, part == "Main" || part == "Canopy" ? part + ".E" : part);
            SkinnedMeshRenderer f = Find(go, part + ".F");
            if (e == null || f == null || e.sharedMesh == null || f.sharedMesh == null) { continue; }
            int[] counts = MarkFOnly(e.sharedMesh, f.sharedMesh, mask);
            fOnly += counts[0]; total += counts[1];
        }
        mask = Blur(DilateMask(mask, Dilate), Feather);
        if (!AssetDatabase.IsValidFolder(OutDir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "FTex"); }
        SaveMaskPreview(mask);

        Material em = AssetDatabase.LoadAssetAtPath<Material>(EMatPath), fm = AssetDatabase.LoadAssetAtPath<Material>(FMatPath);
        foreach (string prop in Props)
        {
            Texture et = em.GetTexture(prop), ft = fm.GetTexture(prop);
            if (et == null || ft == null) { continue; }
            Composite(AssetDatabase.GetAssetPath(et), AssetDatabase.GetAssetPath(ft), OutPath(prop), mask, prop == "_BumpMap" && FlipFNormal, prop == "_MainTex" ? DarkenFLines : 0f);
        }
        AssetDatabase.Refresh();
        foreach (string prop in Props)
        {
            Texture ft = fm.GetTexture(prop);
            if (ft == null) { continue; }
            CopyImport(AssetDatabase.GetAssetPath(ft), OutPath(prop));
        }
        string msg = $"[ACO] F 합성 텍스처: F 면 {total}개 중 F 전용 {fOnly}개 — {OutDir}";
        Debug.Log(msg);
        return msg;
    }

    private static SkinnedMeshRenderer Find(GameObject go, string name)
    {
        foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { if (r.name == name) { return r; } }
        return null;
    }

    /// F 면마다 UV 점 4개(가운데 + 꼭짓점 쪽 3)를 E 메시와 비교해 둘 이상 어긋나면 F 전용 — 마스크에 그 면의 UV 삼각형을 칠한다. [F 전용 수, 전체 수]
    private static int[] MarkFOnly(Mesh e, Mesh f, float[] mask)
    {
        Vector2[] ue = e.uv; Vector3[] pe = e.vertices; int[] te = e.GetTriangles(0);
        Vector2[] uf = f.uv; Vector3[] pf = f.vertices; int[] tf = f.GetTriangles(0);
        const int G = 128;
        var grid = new List<int>[G * G];
        for (int i = 0; i < te.Length; i += 3)
        {
            Vector2 a = ue[te[i]], b = ue[te[i + 1]], c = ue[te[i + 2]];
            int x0 = Cell(Mathf.Min(a.x, Mathf.Min(b.x, c.x)), G), x1 = Cell(Mathf.Max(a.x, Mathf.Max(b.x, c.x)), G);
            int y0 = Cell(Mathf.Min(a.y, Mathf.Min(b.y, c.y)), G), y1 = Cell(Mathf.Max(a.y, Mathf.Max(b.y, c.y)), G);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                int k = y * G + x;
                if (grid[k] == null) { grid[k] = new List<int>(); }
                grid[k].Add(i);
            }
        }
        int fOnly = 0;
        for (int i = 0; i < tf.Length; i += 3)
        {
            Vector2 a = uf[tf[i]], b = uf[tf[i + 1]], c = uf[tf[i + 2]];
            Vector3 pa = pf[tf[i]], pb = pf[tf[i + 1]], pc = pf[tf[i + 2]];
            int bad = 0;
            for (int s = 0; s < 4; s++)
            {
                Vector3 w = s == 0 ? new Vector3(1f, 1f, 1f) / 3f : s == 1 ? new Vector3(0.7f, 0.15f, 0.15f) : s == 2 ? new Vector3(0.15f, 0.7f, 0.15f) : new Vector3(0.15f, 0.15f, 0.7f);
                Vector2 u = a * w.x + b * w.y + c * w.z;
                Vector3 p = pa * w.x + pb * w.y + pc * w.z;
                if (!SameInE(u, p, ue, pe, te, grid, G)) { bad++; }
            }
            if (bad >= 2) { fOnly++; FillTri(mask, a, b, c); }
        }
        return new[] { fOnly, tf.Length / 3 };
    }

    private static int Cell(float v, int g) { return Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(v, 1f) * g), 0, g - 1); }

    private static bool SameInE(Vector2 u, Vector3 p, Vector2[] ue, Vector3[] pe, int[] te, List<int>[] grid, int g)
    {
        List<int> list = grid[Cell(u.y, g) * g + Cell(u.x, g)];
        if (list == null) { return false; }
        foreach (int i in list)
        {
            Vector2 a = ue[te[i]], b = ue[te[i + 1]], c = ue[te[i + 2]];
            if (!Bary(u, a, b, c, out Vector3 w)) { continue; }
            Vector3 q = pe[te[i]] * w.x + pe[te[i + 1]] * w.y + pe[te[i + 2]] * w.z;
            if ((q - p).sqrMagnitude <= Tolerance * Tolerance) { return true; }
        }
        return false;
    }

    private static bool Bary(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 w)
    {
        Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
        float d = v0.x * v1.y - v1.x * v0.y;
        w = Vector3.zero;
        if (Mathf.Abs(d) < 1e-12f) { return false; }
        float v = (v2.x * v1.y - v1.x * v2.y) / d;
        float t = (v0.x * v2.y - v2.x * v0.y) / d;
        float s = 1f - v - t;
        const float eps = -1e-4f;
        if (s < eps || v < eps || t < eps) { return false; }
        w = new Vector3(s, v, t);
        return true;
    }

    private static void FillTri(float[] mask, Vector2 a, Vector2 b, Vector2 c)
    {
        int n = MaskSize;
        Vector2 A = a * n, B = b * n, C = c * n;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)))), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)))), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
        {
            if (Bary(new Vector2(x + 0.5f, y + 0.5f), A, B, C, out _)) { mask[y * n + x] = 1f; }
        }
    }

    private static float[] DilateMask(float[] m, int r)
    {
        int n = MaskSize;
        var o = new float[m.Length];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float v = 0f;
            for (int dy = -r; dy <= r && v < 1f; dy++) for (int dx = -r; dx <= r; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || yy < 0 || xx >= n || yy >= n) { continue; }
                if (m[yy * n + xx] > v) { v = m[yy * n + xx]; if (v >= 1f) { break; } }
            }
            o[y * n + x] = v;
        }
        return o;
    }

    private static float[] Blur(float[] m, int r)
    {
        int n = MaskSize;
        var t = new float[m.Length];
        var o = new float[m.Length];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float s = 0f; int c = 0;
            for (int dx = -r; dx <= r; dx++) { int xx = x + dx; if (xx < 0 || xx >= n) { continue; } s += m[y * n + xx]; c++; }
            t[y * n + x] = s / c;
        }
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float s = 0f; int c = 0;
            for (int dy = -r; dy <= r; dy++) { int yy = y + dy; if (yy < 0 || yy >= n) { continue; } s += t[yy * n + x]; c++; }
            o[y * n + x] = s / c;
        }
        return o;
    }

    private static float Sample(float[] m, float u, float v)
    {
        int n = MaskSize;
        float x = u * n - 0.5f, y = v * n - 0.5f;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, n - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, n - 1);
        int x1 = Mathf.Min(x0 + 1, n - 1), y1 = Mathf.Min(y0 + 1, n - 1);
        float fx = Mathf.Clamp01(x - x0), fy = Mathf.Clamp01(y - y0);
        float a = Mathf.Lerp(m[y0 * n + x0], m[y0 * n + x1], fx), b = Mathf.Lerp(m[y1 * n + x0], m[y1 * n + x1], fx);
        return Mathf.Lerp(a, b, fy);
    }

    /// PNG 원본끼리(색 변환 없이) 마스크로 섞어 PNG로 저장한다. 크기가 다르면 F 크기에 맞춰 E를 늘린다.
    private static void Composite(string ePath, string fPath, string outPath, float[] mask, bool flipNormal, float darkenLines)
    {
        var e = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        var f = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        e.LoadImage(File.ReadAllBytes(ePath));
        f.LoadImage(File.ReadAllBytes(fPath));
        int w = f.width, h = f.height;
        Color32[] fp = f.GetPixels32();
        if (flipNormal) { for (int i = 0; i < fp.Length; i++) { fp[i].r = (byte)(255 - fp[i].r); fp[i].g = (byte)(255 - fp[i].g); } }
        if (darkenLines > 0f) { fp = DarkenLines(fp, f.width, f.height, darkenLines); }
        Color32[] ep = e.width == w && e.height == h ? e.GetPixels32() : null;
        var o = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float m = Sample(mask, u, v);
                Color32 ec = ep != null ? ep[y * w + x] : (Color32)e.GetPixelBilinear(u, v);
                o[y * w + x] = m <= 0f ? ec : (m >= 1f ? fp[y * w + x] : Color32.Lerp(ec, fp[y * w + x], m));
            }
        }
        var outT = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        outT.SetPixels32(o);
        outT.Apply();
        File.WriteAllBytes(outPath, outT.EncodeToPNG());
        Object.DestroyImmediate(e); Object.DestroyImmediate(f); Object.DestroyImmediate(outT);
    }

    /// F 기본 색의 밝은 패널 선 · 리벳을 지우고 가운데 1px만 어두운 선으로 다시 긋는다(E처럼 가늘게).
    ///  - 주변 평균(상자 흐림, 반지름 4px)보다 밝은 픽셀(선 · 번짐)은 주변 평균 밝기로 지운다.
    ///  - 그중 가로나 세로로 가장 밝은 줄(선의 한가운데)만 주변 평균 × LineShade로 어둡게 칠한다. k = 세기(0~1).
    private static Color32[] DarkenLines(Color32[] px, int w, int h, float k)
    {
        const int r = 4;
        const float thr = 0.015f;
        var lum = new float[w * h];
        for (int i = 0; i < px.Length; i++) { lum[i] = (px[i].r + px[i].g + px[i].b) / 765f; }
        var t = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            float s = 0f; int c = 0;
            for (int x = -r; x <= r; x++) { if (x >= 0 && x < w) { s += lum[y * w + x]; c++; } }
            for (int x = 0; x < w; x++)
            {
                t[y * w + x] = s / c;
                int xo = x - r, xi = x + r + 1;
                if (xo >= 0) { s -= lum[y * w + xo]; c--; }
                if (xi < w) { s += lum[y * w + xi]; c++; }
            }
        }
        var mean = new float[w * h];
        for (int x = 0; x < w; x++)
        {
            float s = 0f; int c = 0;
            for (int y = -r; y <= r; y++) { if (y >= 0 && y < h) { s += t[y * w + x]; c++; } }
            for (int y = 0; y < h; y++)
            {
                mean[y * w + x] = s / c;
                int yo = y - r, yi = y + r + 1;
                if (yo >= 0) { s -= t[yo * w + x]; c--; }
                if (yi < h) { s += t[yi * w + x]; c++; }
            }
        }
        var o = new Color32[px.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float d = lum[i] - mean[i];
                Color32 p = px[i];
                if (d <= thr) { o[i] = p; continue; }
                // 지우기: 주변 평균 밝기로
                float f = Mathf.Clamp01(mean[i] / Mathf.Max(lum[i], 0.001f));
                // 선 한가운데(가로 또는 세로로 이웃보다 밝다)만 어둡게 다시 긋는다
                float dl = x > 0 ? lum[i - 1] - mean[i - 1] : -1f, dr = x < w - 1 ? lum[i + 1] - mean[i + 1] : -1f;
                float du = y > 0 ? lum[i - w] - mean[i - w] : -1f, dd = y < h - 1 ? lum[i + w] - mean[i + w] : -1f;
                bool ridge = (d >= dl && d > dr) || (d >= du && d > dd);
                if (ridge && d > thr * 2f) { f *= Mathf.Lerp(1f, LineShade, k); }
                o[i] = new Color32((byte)(p.r * f), (byte)(p.g * f), (byte)(p.b * f), p.a);
            }
        }
        return o;
    }

    private static void CopyImport(string srcPath, string dstPath)
    {
        var s = AssetImporter.GetAtPath(srcPath) as TextureImporter;
        var d = AssetImporter.GetAtPath(dstPath) as TextureImporter;
        if (s == null || d == null) { return; }
        d.textureType = s.textureType;
        d.sRGBTexture = s.sRGBTexture;
        d.maxTextureSize = s.maxTextureSize;
        d.textureCompression = s.textureCompression;
        d.mipmapEnabled = s.mipmapEnabled;
        d.wrapMode = s.wrapMode;
        d.filterMode = s.filterMode;
        d.anisoLevel = s.anisoLevel;
        d.alphaSource = s.alphaSource;
        d.alphaIsTransparency = s.alphaIsTransparency;
        d.crunchedCompression = s.crunchedCompression;
        d.compressionQuality = s.compressionQuality;
        d.SaveAndReimport();
    }

    private static void SaveMaskPreview(float[] mask)
    {
        const int n = 512;
        var t = new Texture2D(n, n, TextureFormat.RGB24, false);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { byte b = (byte)(Sample(mask, (x + 0.5f) / n, (y + 0.5f) / n) * 255f); px[y * n + x] = new Color32(b, b, b, 255); }
        t.SetPixels32(px); t.Apply();
        string p = Path.Combine(Path.GetTempPath(), "ACO_FTex_mask.png");
        File.WriteAllBytes(p, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }
}
