using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// FA-18E 조종석 UFC(스크래치패드) · 연료계(RPM/FUEL) 화면 그림을 깔끔하게 다시 그린다(사용자 요청 — 원본은 손으로 그려 키 칸이 비뚤고 선이 울퉁불퉁).
/// 원본 텍스처(Pit-bool cockpit_Base_color · cockpit_Emissive)는 그대로, 복사본(Generated/Cockpit)에 두 영역만 새로 그려 머티리얼 복사본을 기체 프리팹 cockpit.E에 끼운다.
/// 두 영역은 텍스처 안에서 90° 돌아가 있다 — 화면 좌표(dx 오른쪽, dy 아래)는 텍스처 (tx = 오른쪽 끝 - dy, ty = 위 끝 - dx)로 옮긴다.
/// 움직이는 RPM · 연료 막대(SAV 메시)가 겹치는 연료계 선은 원래 자리 그대로 가늘게. 금속 · 매끄러움 맵(원본 발광 텍스처를 같이 씀)은 원본 그대로.
public static class ACO_CockpitTexture
{
    private const string SrcBase = "Assets/Pit-bool/FA-18/material/cockpit/cockpit_Base_color.png";
    private const string SrcEmis = "Assets/Pit-bool/FA-18/material/cockpit/cockpit_Emissive.png";
    private const string SrcMat = "Assets/Pit-bool/FA-18/material/cockpit/cockpit.mat";
    private const string Dir = "Assets/ACO_Addon/Generated/Cockpit";
    private const string OutBase = Dir + "/cockpit_Base_color_ACO.png";
    private const string OutEmis = Dir + "/cockpit_Emissive_ACO.png";
    private const string OutMat = Dir + "/cockpit_ACO.mat";
    private const string FontPath = "Assets/ACO_Addon/Fonts/ACO_Pretendard SDF.asset";
    private const string PrefabPath = "Assets/Pit-bool/FA-18E Main_ACO.prefab";
    private const string RendererName = "cockpit.E";
    private const int SS = 4;              // 슈퍼샘플 배율
    private const int Layer = 31;
    public const int Aniso = 4;            // 비스듬히 볼 때 작은 글자가 덜 뭉개지게

    // 찾을 범위(텍스처 픽셀, 아래가 0)
    private static readonly RectInt UfcSearch = new RectInt(491, 3440, 655, 614);
    private static readonly RectInt FuelSearch = new RectInt(3250, 3050, 350, 410);

    private struct Label { public string Text; public float Cx, Cy, Cap; public bool Left; }

    private class Draw
    {
        public int W, H;              // 화면 픽셀
        public float[] M;             // 슈퍼샘플 마스크
        public List<Label> Texts = new List<Label>();
        public Draw(int w, int h) { W = w; H = h; M = new float[w * SS * h * SS]; }

        public void Fill(float x0, float y0, float x1, float y1)
        {
            int a0 = Mathf.Clamp(Mathf.RoundToInt(x0 * SS), 0, W * SS), a1 = Mathf.Clamp(Mathf.RoundToInt(x1 * SS), 0, W * SS);
            int b0 = Mathf.Clamp(Mathf.RoundToInt(y0 * SS), 0, H * SS), b1 = Mathf.Clamp(Mathf.RoundToInt(y1 * SS), 0, H * SS);
            for (int y = b0; y < b1; y++) { for (int x = a0; x < a1; x++) { M[y * W * SS + x] = 1f; } }
        }

        /// 각진 사각 테두리(선 두께 t, 바깥 기준).
        public void Box(float x0, float y0, float x1, float y1, float t)
        {
            Fill(x0, y0, x1, y0 + t); Fill(x0, y1 - t, x1, y1);
            Fill(x0, y0, x0 + t, y1); Fill(x1 - t, y0, x1, y1);
        }

        /// 오른쪽을 가리키는 삼각형(가운데 cx, cy, 크기 s).
        public void TriRight(float cx, float cy, float s)
        {
            for (int y = 0; y < H * SS; y++)
            {
                for (int x = 0; x < W * SS; x++)
                {
                    float px = (x + 0.5f) / SS - cx, py = (y + 0.5f) / SS - cy;
                    if (px >= -s * 0.5f && px <= s * 0.5f && Mathf.Abs(py) <= (s * 0.5f - px) * 0.5f) { M[y * W * SS + x] = 1f; }
                }
            }
        }

        public void Text(string t, float cx, float cy, float cap, bool left = false) { Texts.Add(new Label { Text = t, Cx = cx, Cy = cy, Cap = cap, Left = left }); }
    }

    [MenuItem("ACO/FA-18E Cockpit Displays/Redraw")]
    public static void Apply()
    {
        if (!File.Exists(SrcBase) || !File.Exists(SrcEmis)) { Debug.LogWarning("[ACO] 조종석 화면: 원본 텍스처를 찾지 못했습니다."); return; }
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) { Debug.LogWarning($"[ACO] 조종석 화면: 글꼴 {FontPath}을 찾지 못했습니다."); return; }
        var tb = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var te = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tb.LoadImage(File.ReadAllBytes(SrcBase));
        te.LoadImage(File.ReadAllBytes(SrcEmis));
        try
        {
            string ufc = Ufc(tb, te, font, FaceTris(UfcSearch));
            string fuel = Fuel(tb, te, font);
            if (!AssetDatabase.IsValidFolder(Dir)) { AssetDatabase.CreateFolder("Assets/ACO_Addon/Generated", "Cockpit"); }
            File.WriteAllBytes(OutBase, tb.EncodeToPNG());
            File.WriteAllBytes(OutEmis, te.EncodeToPNG());
            Debug.Log($"[ACO] 조종석 화면을 다시 그렸습니다 — UFC {ufc}, 연료계 {fuel}.");
        }
        finally { Object.DestroyImmediate(tb); Object.DestroyImmediate(te); }
        AssetDatabase.ImportAsset(OutBase, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(OutEmis, ImportAssetOptions.ForceUpdate);
        CopyImport(SrcBase, OutBase);
        CopyImport(SrcEmis, OutEmis);
        Material mat = MakeMaterial();
        SetPrefab(mat);
    }

    [MenuItem("ACO/FA-18E Cockpit Displays/Restore Original")]
    public static void Revert() { SetPrefab(AssetDatabase.LoadAssetAtPath<Material>(SrcMat)); }

    // ---------- UFC ----------

    /// face: UFC 면 삼각형(텍스처 픽셀 UV, 기체 좌표 x · y) — UV가 사다리꼴이라 픽셀마다 실제 자리를 구해 3D에서 반듯하게 그린다. 없으면 텍스처 기준.
    private static string Ufc(Texture2D tb, Texture2D te, TMP_FontAsset font, List<Vector2[]> face)
    {
        // 녹색 판(기본색) 테두리 상자
        RectInt r = Bounds(tb, UfcSearch, c => c.g > c.r + 0.06f && c.g > c.b + 0.03f && c.g > 0.2f);
        if (r.width <= 0) { return "판을 못 찾음"; }
        int W = r.height, H = r.width;                 // 화면: 가로 = 텍스처 세로, 세로 = 텍스처 가로
        Rect phys = new Rect();
        // 판 상자 안에 든 삼각형만(찾는 범위의 다른 부품 면 제외)
        if (face != null) { face = face.FindAll(f => InRect(f[0], r) && InRect(f[1], r) && InRect(f[2], r)); }
        bool mapped = face != null && face.Count > 0;
        if (mapped)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (Vector2[] f in face) { for (int k = 3; k < 6; k++) { x0 = Mathf.Min(x0, f[k].x); x1 = Mathf.Max(x1, f[k].x); y0 = Mathf.Min(y0, f[k].y); y1 = Mathf.Max(y1, f[k].y); } }
            phys = Rect.MinMaxRect(x0, y0, x1, y1);
            H = Mathf.RoundToInt(W * phys.height / phys.width);   // 화면 비율 = 실제 면 비율
        }
        Color panel = Average(tb, te, r, (b, e) => e.g < 0.1f && b.g > b.r);
        Color line = Average(tb, te, r, (b, e) => e.g > 0.5f);
        Color glow = Average(te, te, r, (b, e) => e.g > 0.5f);
        var d = new Draw(W, H);
        float sx = W / 305f, sy = H / 247f;
        float t = 2f * Mathf.Min(sx, sy);
        string[,] keys = { { "1", "2", "3" }, { "4", "5", "6" }, { "7", "8", "9" }, { "CLR", "0", "ENT" } };
        string[] opt1 = { "CPL\nT/R", "BALT", "RALT", "FPAH", "CNI" };
        string[] opt2 = { "CH12\n122.500", "ROLL", "GTRK", "HDG", "CH 6" };
        float kw = 50f * sx, kh = 52f * sy;
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                float x0 = (16f + col * 56f) * sx, y0 = (16f + row * 58f) * sy;
                d.Box(x0, y0, x0 + kw, y0 + kh, t);
                string k = keys[row, col];
                d.Text(k, x0 + kw * 0.5f, y0 + kh * 0.5f, k.Length == 1 ? kh * 0.46f : kh * 0.22f);
            }
        }
        float ow = 46f * sx, oh = 39f * sy;
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                float x0 = (186f + col * 58f) * sx, y0 = (16f + row * 46.75f) * sy;
                d.Box(x0, y0, x0 + ow, y0 + oh, t);
                string k = col == 0 ? opt1[row] : opt2[row];
                d.Text(k, x0 + ow * 0.5f, y0 + oh * 0.5f, k.Contains("\n") ? oh * 0.2f : oh * 0.25f);
            }
        }
        float[] m = Render(d, font, true);
        for (int tx = r.xMin; tx < r.xMax; tx++)
        {
            for (int ty = r.yMin; ty < r.yMax; ty++)
            {
                float a;
                if (mapped)
                {
                    // 이 픽셀의 실제 자리(면 삼각형 안 UV → 기체 좌표) → 화면 좌표
                    Vector2 p;
                    if (!FacePos(face, new Vector2(tx + 0.5f, ty + 0.5f), out p)) { a = 0f; }
                    else { a = Sample(m, W, H, (p.x - phys.xMin) / phys.width * W, (phys.yMax - p.y) / phys.height * H); }
                }
                else { a = m[(r.xMax - 1 - tx) * W + (r.yMax - 1 - ty)]; }
                tb.SetPixel(tx, ty, Color.Lerp(panel, line, a));
                Color e0 = te.GetPixel(tx, ty);
                Color e = glow * a;
                e.a = e0.a;
                te.SetPixel(tx, ty, e);
            }
        }
        return $"판 {r.x},{r.y} {r.width}x{r.height}" + (mapped ? $", 실제 면 {phys.width * 100f:0.0}x{phys.height * 100f:0.0}cm에 맞춤" : "");
    }

    // ---------- 연료계 ----------

    private static string Fuel(Texture2D tb, Texture2D te, TMP_FontAsset font)
    {
        // 검은 화면(기본색) 테두리 상자 — 어둡거나 녹색인 픽셀
        System.Func<Color, bool> screen = c => (c.r + c.g + c.b) / 3f < 0.1f || (c.g > c.r + 0.15f && c.g > c.b + 0.15f);
        RectInt r = Bounds(tb, FuelSearch, screen);
        if (r.width <= 0) { return "화면을 못 찾음"; }
        int W = r.height, H = r.width;
        Color line = Average(tb, te, r, (b, e) => e.g > 0.5f);
        Color glow = Average(te, te, r, (b, e) => e.g > 0.5f);
        var d = new Draw(W, H);
        float sx = W / 321f, sy = H / 246f, s = Mathf.Min(sx, sy);
        float lt = 3f * s, bt = 2f * s;
        // 원래 선 자리 그대로(움직이는 막대 메시와 맞물림)
        d.Fill(6f * sx, 83f * sy - lt * 0.5f, 315f * sx, 83f * sy + lt * 0.5f);                 // 가로 구분선
        d.Fill(196f * sx - lt * 0.5f, 83f * sy, 196f * sx + lt * 0.5f, 240f * sy);              // 세로 구분선(RPM | FUEL)
        d.Box(63f * sx, 8f * sy, 220f * sx, 53f * sy, bt);                                         // BINGO 상자
        d.Fill(68f * sx, 24f * sy, 215f * sx, 40f * sy);                                           // 안쪽 막대
        d.TriRight(55f * sx, 38f * sy, 9f * s);
        d.Text("B", 40f * sx, 38f * sy, 15f * s);
        d.Text("BINGO", 228f * sx, 26f * sy, 10f * s, true);
        d.Text("4.5", 300f * sx, 26f * sy, 11f * s);
        d.Text("INT", 228f * sx, 45f * sy, 10f * s, true);
        d.Text("7.2", 300f * sx, 45f * sy, 11f * s);
        d.Text("RPM", 103f * sx, 106f * sy, 16f * s);
        d.Text("L", 63f * sx, 128f * sy, 14f * s);
        d.Text("R", 140f * sx, 128f * sy, 14f * s);
        d.Text("FUEL", 258f * sx, 104f * sy, 16f * s);
        float[] m = Render(d, font, false);
        for (int dy = 0; dy < H; dy++)
        {
            for (int dx = 0; dx < W; dx++)
            {
                int tx = r.xMax - 1 - dy, ty = r.yMax - 1 - dx;
                Color b0 = tb.GetPixel(tx, ty);
                if (!screen(b0)) { continue; }                  // 둥근 모서리 바깥(회색)은 그대로
                float a = m[dy * W + dx];
                Color nb = Color.Lerp(Color.black, line, a);
                nb.a = b0.a;
                tb.SetPixel(tx, ty, nb);
                Color e0 = te.GetPixel(tx, ty);
                Color e = glow * a;
                e.a = e0.a;
                te.SetPixel(tx, ty, e);
            }
        }
        return $"화면 {r.x},{r.y} {r.width}x{r.height}";
    }

    // ---------- 면 매핑 ----------

    /// 기체 프리팹 cockpit.E에서 UV가 area 안에 든 삼각형: [uv0, uv1, uv2(텍스처 픽셀), p0, p1, p2(기체 좌표 x · y)].
    private static List<Vector2[]> FaceTris(RectInt area)
    {
        var list = new List<Vector2[]>();
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var entity = root.GetComponentInChildren<SaccFlightAndVehicles.SaccEntity>(true);
            SkinnedMeshRenderer smr = null;
            foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { if (s.name == RendererName) { smr = s; } }
            if (entity == null || smr == null || smr.sharedMesh == null) { return list; }
            Matrix4x4 toFrame = entity.transform.worldToLocalMatrix * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            Mesh mesh = smr.sharedMesh;
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int tw = 4096;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] tri = mesh.GetTriangles(sub);
                for (int t = 0; t < tri.Length; t += 3)
                {
                    var f = new Vector2[6];
                    bool inside = true;
                    for (int e = 0; e < 3 && inside; e++)
                    {
                        Vector2 u = uv[tri[t + e]] * tw;
                        inside = u.x >= area.xMin - 3 && u.x <= area.xMax + 3 && u.y >= area.yMin - 3 && u.y <= area.yMax + 3;
                        Vector3 p = toFrame.MultiplyPoint3x4(v[tri[t + e]]);
                        f[e] = u;
                        f[3 + e] = new Vector2(p.x, p.y);
                    }
                    if (inside) { list.Add(f); }
                }
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return list;
    }

    private static bool InRect(Vector2 u, RectInt r) { return u.x >= r.xMin - 3 && u.x <= r.xMax + 3 && u.y >= r.yMin - 3 && u.y <= r.yMax + 3; }

    private static bool FacePos(List<Vector2[]> face, Vector2 u, out Vector2 p)
    {
        foreach (Vector2[] f in face)
        {
            Vector2 a = f[0], b = f[1], c = f[2];
            float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(den) < 1e-6f) { continue; }
            float w0 = ((b.y - c.y) * (u.x - c.x) + (c.x - b.x) * (u.y - c.y)) / den;
            float w1 = ((c.y - a.y) * (u.x - c.x) + (a.x - c.x) * (u.y - c.y)) / den;
            float w2 = 1f - w0 - w1;
            if (w0 < -0.002f || w1 < -0.002f || w2 < -0.002f) { continue; }
            p = f[3] * w0 + f[4] * w1 + f[5] * w2;
            return true;
        }
        p = Vector2.zero;
        return false;
    }

    private static float Sample(float[] m, int W, int H, float x, float y)
    {
        x -= 0.5f; y -= 0.5f;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        float s = 0f;
        for (int j = 0; j < 2; j++)
        {
            for (int i = 0; i < 2; i++)
            {
                int xi = Mathf.Clamp(x0 + i, 0, W - 1), yi = Mathf.Clamp(y0 + j, 0, H - 1);
                s += m[yi * W + xi] * (i == 0 ? 1f - fx : fx) * (j == 0 ? 1f - fy : fy);
            }
        }
        return s;
    }

    // ---------- 공통 ----------

    private static RectInt Bounds(Texture2D t, RectInt area, System.Func<Color, bool> hit)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        Color[] px = t.GetPixels(area.x, area.y, area.width, area.height);
        for (int y = 0; y < area.height; y++)
        {
            for (int x = 0; x < area.width; x++)
            {
                if (!hit(px[y * area.width + x])) { continue; }
                x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
            }
        }
        if (x0 > x1) { return new RectInt(0, 0, 0, 0); }
        return new RectInt(area.x + x0, area.y + y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    private static Color Average(Texture2D a, Texture2D e, RectInt r, System.Func<Color, Color, bool> pick)
    {
        Color[] pa = a.GetPixels(r.x, r.y, r.width, r.height), pe = e.GetPixels(r.x, r.y, r.width, r.height);
        Color sum = Color.clear;
        int n = 0;
        for (int i = 0; i < pa.Length; i++) { if (pick(pa[i], pe[i])) { sum += pa[i]; n++; } }
        if (n == 0) { return Color.black; }
        Color c = sum / n;
        c.a = 1f;
        return c;
    }

    /// 도형 마스크 + TMP 글자(카메라로 렌더) → 화면 해상도 마스크(0 ~ 1).
    private static float[] Render(Draw d, TMP_FontAsset font, bool fitWidth)
    {
        int W = d.W * SS, H = d.H * SS;
        var root = new GameObject("ACO_CockpitTextureTemp") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = new Vector3(0f, -20000f, 0f);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        Camera cam = null;
        var text = new float[W * H];
        try
        {
            // 글자 크기 기준: 크기 10에서 대문자 높이(글꼴의 capLine — textBounds는 줄 높이라 더 크다)
            float capAt10 = Mathf.Max(0.001f, 10f * font.faceInfo.capLine / font.faceInfo.pointSize * 0.1f);
            foreach (Label l in d.Texts)
            {
                var tmp = NewText(root, font, l.Text, l.Cap * 10f / capAt10);
                tmp.alignment = l.Left ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
                tmp.ForceMeshUpdate();
                Vector3 c = new Vector3(l.Cx, -l.Cy, 0f);
                Bounds b = tmp.bounds;   // 그려진 글자 사각형(메시) — 위아래 가운데가 글자 가운데
                // 가운데 맞춤: 글자 경계 가운데를 (Cx, Cy)에, 왼쪽 맞춤: 왼쪽 끝을 Cx에
                Vector3 off = l.Left ? new Vector3(-b.min.x, -b.center.y, 0f) : -b.center;
                tmp.transform.localPosition = c + off;
            }
            var camGo = new GameObject("cam") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(root.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = d.H * 0.5f;
            cam.aspect = (float)d.W / d.H;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.targetTexture = rt;
            camGo.transform.localPosition = new Vector3(d.W * 0.5f, -d.H * 0.5f, -10f);
            camGo.transform.localRotation = Quaternion.identity;
            cam.Render();
            RenderTexture.active = rt;
            var read = new Texture2D(W, H, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            Color[] px = read.GetPixels();
            Object.DestroyImmediate(read);
            // 렌더는 아래가 0 — 화면 좌표(위가 0)로 뒤집는다
            for (int y = 0; y < H; y++) { for (int x = 0; x < W; x++) { text[(H - 1 - y) * W + x] = px[y * W + x].grayscale; } }
        }
        finally
        {
            if (cam != null) { cam.targetTexture = null; }
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(root);
        }
        var outM = new float[d.W * d.H];
        float inv = 1f / (SS * SS);
        for (int y = 0; y < d.H; y++)
        {
            for (int x = 0; x < d.W; x++)
            {
                float sum = 0f;
                for (int j = 0; j < SS; j++)
                {
                    for (int i = 0; i < SS; i++)
                    {
                        int k = (y * SS + j) * W + x * SS + i;
                        sum += Mathf.Max(d.M[k], text[k]);
                    }
                }
                outM[y * d.W + x] = Mathf.Clamp01(sum * inv);
            }
        }
        return outM;
    }

    private static TextMeshPro NewText(GameObject root, TMP_FontAsset font, string s, float size)
    {
        var go = new GameObject("t") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
        go.transform.SetParent(root.transform, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = font;
        tmp.text = s;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.enableWordWrapping = false;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.lineSpacing = -10f;
        tmp.rectTransform.sizeDelta = new Vector2(1000f, 1000f);
        return tmp;
    }

    private static void CopyImport(string src, string dst)
    {
        var a = (TextureImporter)AssetImporter.GetAtPath(src);
        var b = (TextureImporter)AssetImporter.GetAtPath(dst);
        if (a == null || b == null) { return; }
        var s = new TextureImporterSettings();
        a.ReadTextureSettings(s);
        b.SetTextureSettings(s);
        b.SetPlatformTextureSettings(a.GetDefaultPlatformTextureSettings());
        b.anisoLevel = Aniso;
        b.SaveAndReimport();
    }

    private static Material MakeMaterial()
    {
        var src = AssetDatabase.LoadAssetAtPath<Material>(SrcMat);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(OutMat);
        if (mat == null)
        {
            mat = new Material(src);
            AssetDatabase.CreateAsset(mat, OutMat);
        }
        else { mat.CopyPropertiesFromMaterial(src); }
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(OutBase));
        mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(OutEmis));
        // _MetallicGlossMap은 원본 발광 텍스처 그대로(금속 · 매끄러움이 바뀌지 않게)
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static void SetPrefab(Material use)
    {
        var src = AssetDatabase.LoadAssetAtPath<Material>(SrcMat);
        var copy = AssetDatabase.LoadAssetAtPath<Material>(OutMat);
        if (use == null) { return; }
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            int changed = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name != RendererName) { continue; }
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if ((mats[i] == src || (copy != null && mats[i] == copy)) && mats[i] != use) { mats[i] = use; changed++; }
                }
                r.sharedMaterials = mats;
            }
            if (changed > 0) { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            Debug.Log($"[ACO] 조종석 화면: {PrefabPath}의 {RendererName}에 {use.name} 머티리얼을 끼웠습니다({changed}칸).");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
