using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.SceneManagement;
using ACO;

public partial class ACO_SceneBaker
{
    /// 번역 장치(6-166) 연결: 이름이 번역 표의 키인 TMP 글자 → Labels/LabelKeys, Loc 필드가 있는 스크립트 → Loc = 장치, Listeners.
    /// 표 내용(Keys 등)은 Setup Check가 넣는다(Bake는 연결만).
    private static void BakeLocalization()
    {
        ACO_Localization loc = ACO_LocalizationSetup.FindManager();
        if (loc == null) { return; }
        var keys = new HashSet<string>(loc.Keys ?? new string[0]);
        var labels = new List<TextMeshProUGUI>();
        var labelKeys = new List<string>();
        var listeners = new List<UdonSharpBehaviour>();
        var labels3D = new List<TextMeshPro>();
        var label3DKeys = new List<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (TextMeshProUGUI t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (keys.Contains(t.name)) { labels.Add(t); labelKeys.Add(t.name); }
            }
            foreach (TextMeshPro t in root.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (keys.Contains(t.name)) { labels3D.Add(t); label3DKeys.Add(t.name); }
            }
            foreach (UdonSharpBehaviour b in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                if (b == loc) { continue; }
                FieldInfo f = b.GetType().GetField("Loc", BindingFlags.Public | BindingFlags.Instance);
                if (f == null || f.FieldType != typeof(ACO_Localization)) { continue; }
                if ((ACO_Localization)f.GetValue(b) != loc) { f.SetValue(b, loc); MarkChanged(b); }
                listeners.Add(b);
            }
        }
        loc.Labels = labels.ToArray();
        loc.LabelKeys = labelKeys.ToArray();
        loc.Labels3D = labels3D.ToArray();
        loc.Label3DKeys = label3DKeys.ToArray();
        loc.Listeners = listeners.ToArray();
        MarkChanged(loc);
        Debug.Log($"[ACO] 번역: 라벨 {labels.Count}개(3D 판 {labels3D.Count}개), 패널 스크립트 {listeners.Count}개를 번역 장치에 연결했습니다.", loc);
    }
}
