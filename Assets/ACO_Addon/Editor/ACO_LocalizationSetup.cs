using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ACO;

/// 패널 번역(6-166) 편집기 쪽: 문장 표(Localization/ACO_Strings.tsv) 읽기, 씬의 번역 장치(ACO_Localization) 만들기 · 표 넣기,
/// 패널 글꼴(LiberationSans)에 한글 · 일본어 보조 글꼴 붙이기. 생성기는 English(키)로 라벨 기본 글자를 얻고 라벨 이름을 키로 짓는다.
public static class ACO_LocalizationSetup
{
    public const string TablePath = "Assets/ACO_Addon/Localization/ACO_Strings.tsv";
    public const string PanelFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    public class Table
    {
        public readonly List<string> Keys = new List<string>();
        public readonly List<string> Ko = new List<string>();
        public readonly List<string> En = new List<string>();
        public readonly List<string> Ja = new List<string>();
        public readonly Dictionary<string, int> Index = new Dictionary<string, int>();
    }

    private static Table _cache;
    private static System.DateTime _cacheTime;

    /// 표를 읽는다(줄: 키 \t 한국어 \t 영어 \t 일본어, # 주석, 첫 줄 머리). 칸 안의 \n은 줄바꿈.
    public static Table Load()
    {
        if (!File.Exists(TablePath)) { return new Table(); }
        System.DateTime t = File.GetLastWriteTimeUtc(TablePath);
        if (_cache != null && t == _cacheTime) { return _cache; }
        var table = new Table();
        string[] lines = File.ReadAllText(TablePath, Encoding.UTF8).Replace("\r\n", "\n").Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || line.StartsWith("#")) { continue; }
            string[] c = line.Split('\t');
            string key = c[0].Trim();
            if (key.Length == 0) { continue; }
            if (table.Index.ContainsKey(key)) { Debug.LogWarning($"[ACO] 번역 표에 같은 키가 두 번 있습니다: {key} ({TablePath} {i + 1}줄) — 앞의 것을 씁니다."); continue; }
            table.Index[key] = table.Keys.Count;
            table.Keys.Add(key);
            table.Ko.Add(Cell(c, 1));
            table.En.Add(Cell(c, 2));
            table.Ja.Add(Cell(c, 3));
        }
        _cache = table;
        _cacheTime = t;
        return table;
    }

    private static string Cell(string[] c, int i)
    {
        return i < c.Length ? c[i].Replace("\\n", "\n") : "";
    }

    /// 생성기용: 키의 영어 문장(표에 없으면 경고하고 키).
    public static string English(string key)
    {
        Table t = Load();
        if (t.Index.TryGetValue(key, out int i) && !string.IsNullOrEmpty(t.En[i])) { return t.En[i]; }
        Debug.LogWarning($"[ACO] 번역 표에 키가 없습니다: {key} — {TablePath}에 더하세요.");
        return key;
    }

    public static ACO_Localization FindManager()
    {
        foreach (GameObject r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            ACO_Localization l = r.GetComponentInChildren<ACO_Localization>(true);
            if (l != null) { return l; }
        }
        return null;
    }

    /// 씬의 번역 장치가 표와 같은가.
    public static bool ManagerUpToDate(ACO_Localization m)
    {
        if (m == null) { return false; }
        Table t = Load();
        return Same(m.Keys, t.Keys) && Same(m.Ko, t.Ko) && Same(m.En, t.En) && Same(m.Ja, t.Ja);
    }

    private static bool Same(string[] a, List<string> b)
    {
        if (a == null) { return b.Count == 0; }
        if (a.Length != b.Count) { return false; }
        for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
        return true;
    }

    /// 번역 장치가 없으면 씬 맨 위에 만들고, 표를 넣는다.
    public static void ApplyTable()
    {
        ACO_Localization m = FindManager();
        if (m == null)
        {
            var go = new GameObject("ACO_Localization");
            m = go.AddUdonSharpComponent<ACO_Localization>();
            Undo.RegisterCreatedObjectUndo(go, "ACO localization");
            Debug.Log("[ACO] 번역 장치(ACO_Localization)를 씬에 만들었습니다 — Bake가 라벨과 패널 스크립트를 연결합니다.", go);
        }
        Table t = Load();
        m.Keys = t.Keys.ToArray();
        m.Ko = t.Ko.ToArray();
        m.En = t.En.ToArray();
        m.Ja = t.Ja.ToArray();
        EditorUtility.SetDirty(m);
        PrefabUtility.RecordPrefabInstancePropertyModifications(m);
        EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
        Debug.Log($"[ACO] 번역 표 {t.Keys.Count}줄을 넣었습니다({TablePath}). 라벨 연결은 Bake가 합니다.", m);
    }

    /// 패널 글꼴(LiberationSans)의 보조 글꼴에 Pretendard(한글, 그 뒤에 일본어 보조가 붙어 있다)가 있는가.
    public static bool PanelFallbackReady()
    {
        TMP_FontAsset lib = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PanelFontPath);
        TMP_FontAsset kr = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ACO_HUDBuilder.TutorialFontPath);
        return lib != null && kr != null && lib.fallbackFontAssetTable != null && lib.fallbackFontAssetTable.Contains(kr);
    }

    /// LiberationSans에 Pretendard 보조 글꼴을 붙인다(영어 모양은 그대로, 한글 · 가나 · 한자만 Pretendard로).
    public static void EnsurePanelFallback()
    {
        if (!ACO_HUDBuilder.PanelFontsReady()) { ACO_HUDBuilder.EnsureTutorialFonts(); }
        TMP_FontAsset lib = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PanelFontPath);
        TMP_FontAsset kr = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ACO_HUDBuilder.TutorialFontPath);
        if (lib == null || kr == null) { Debug.LogError("[ACO] 패널 글꼴 또는 Pretendard 글꼴을 찾지 못했습니다."); return; }
        if (lib.fallbackFontAssetTable == null) { lib.fallbackFontAssetTable = new List<TMP_FontAsset>(); }
        if (!lib.fallbackFontAssetTable.Contains(kr)) { lib.fallbackFontAssetTable.Add(kr); }
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        Debug.Log("[ACO] 패널 글꼴(LiberationSans)에 Pretendard(한글 + 일본어 보조)를 보조 글꼴로 붙였습니다.", lib);
    }
}
