using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ACO;

/// BGM 곡 풀 인스펙터: URL 배열과 곡 이름 배열(같은 순서)을 한 줄씩 "곡 이름 | URL"로 보여 주고 함께 늘리고 지운다.
/// 곡 이름은 BGM 패널(ACO_BgmPanel)의 지금 곡에 보인다(비우면 URL).
public static class ACO_BgmPoolGUI
{
    public static void Draw(SerializedObject so, string urlsName, string namesName, string label, ref bool open)
    {
        SerializedProperty urls = so.FindProperty(urlsName);
        SerializedProperty names = so.FindProperty(namesName);
        if (urls == null || names == null) { return; }
        int n = urls.arraySize;
        if (names.arraySize != n) { names.arraySize = n; }

        EditorGUILayout.Space();
        open = EditorGUILayout.Foldout(open, $"{label} — {n}곡", true);
        if (!open) { return; }
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(22f);
                EditorGUILayout.LabelField("곡 이름 (패널에 표시 — 비우면 URL)", EditorStyles.miniBoldLabel, GUILayout.Width(220f));
                EditorGUILayout.LabelField("URL", EditorStyles.miniBoldLabel);
            }
            int remove = -1;
            for (int i = 0; i < n; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(18f));
                    SerializedProperty name = names.GetArrayElementAtIndex(i);
                    name.stringValue = EditorGUILayout.TextField(name.stringValue, GUILayout.Width(220f));
                    SerializedProperty u = urls.GetArrayElementAtIndex(i);
                    SerializedProperty s = u.FindPropertyRelative("url");
                    if (s != null) { s.stringValue = EditorGUILayout.TextField(s.stringValue); }
                    else { EditorGUILayout.PropertyField(u, GUIContent.none); }
                    if (GUILayout.Button("X", GUILayout.Width(24f))) { remove = i; }
                }
            }
            if (remove >= 0)
            {
                urls.DeleteArrayElementAtIndex(remove);
                names.DeleteArrayElementAtIndex(remove);
            }
            if (GUILayout.Button("곡 추가"))
            {
                urls.arraySize = n + 1;
                names.arraySize = n + 1;
                SerializedProperty nu = urls.GetArrayElementAtIndex(n).FindPropertyRelative("url");
                if (nu != null) { nu.stringValue = ""; }
                names.GetArrayElementAtIndex(n).stringValue = "";
            }
        }
    }
}

/// 상황별 BGM 인스펙터: 곡 풀 세 개를 "곡 이름 | URL" 줄로.
[CustomEditor(typeof(ACO_Bgm))]
public class ACO_BgmEditor : Editor
{
    private bool _lobby = true;
    private bool _boss = true;
    private bool _phase = true;

    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) { return; }
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "LobbyUrls", "LobbyNames", "BossUrls", "BossNames", "PhaseUrls", "PhaseNames");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("곡 풀 (페이즈 전용 곡은 각 페이즈 모듈)", EditorStyles.boldLabel);
        ACO_BgmPoolGUI.Draw(serializedObject, "LobbyUrls", "LobbyNames", "대기실", ref _lobby);
        ACO_BgmPoolGUI.Draw(serializedObject, "BossUrls", "BossNames", "보스 기본 (전용 곡 없는 보스 페이즈)", ref _boss);
        ACO_BgmPoolGUI.Draw(serializedObject, "PhaseUrls", "PhaseNames", "일반 페이즈 기본 (전용 곡 없는 일반 페이즈)", ref _phase);
        serializedObject.ApplyModifiedProperties();
    }
}

/// 페이즈 모듈 인스펙터(대사 표가 있는 보스 페이즈는 ACO_PhaseFortressEditor): 전용 BGM 풀을 "곡 이름 | URL" 줄로, 나머지는 그대로.
[CustomEditor(typeof(ACO_PhaseModule), true)]
public class ACO_PhaseModuleEditor : Editor
{
    private bool _showBgm = true;

    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) { return; }
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "BgmUrls", "BgmNames");
        ACO_BgmPoolGUI.Draw(serializedObject, "BgmUrls", "BgmNames", "이 페이즈 전용 BGM (비우면 보스는 보스 기본, 일반은 일반 기본 풀)", ref _showBgm);
        serializedObject.ApplyModifiedProperties();
    }
}
