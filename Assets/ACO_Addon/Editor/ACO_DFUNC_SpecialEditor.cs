using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ACO;

/// 특수무장 슬롯 인스펙터: 모듈마다 한 칸 — 모듈 이름·무장 ID 아래에 기체 외형 / HUD 탄종 / MFD 탄종 / 다이얼 아이콘 오브젝트.
/// (실제로는 Modules와 같은 순서의 배열들이다 — 모듈 수에 맞춰 길이를 맞춘다.) 모듈 추가·제거는 아래 "모듈 목록"에서.
[CustomEditor(typeof(ACO_DFUNC_Special))]
public class ACO_DFUNC_SpecialEditor : Editor
{
    private static readonly string[] DisplayArrays = { "ModuleVisuals", "ModuleHudLabels", "ModuleMfdLabels", "ModuleDialIcons" };
    private static readonly string[] DisplayLabels = { "기체 외형", "HUD 탄종", "MFD 탄종", "다이얼 아이콘" };
    private bool _showModules;

    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) { return; }
        serializedObject.Update();
        var special = (ACO_DFUNC_Special)target;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("Loadout"));
        EditorGUILayout.Space(4f);

        SerializedProperty modules = serializedObject.FindProperty("Modules");
        SerializedProperty ids = serializedObject.FindProperty("ModuleIds");
        int n = modules.arraySize;
        // 표시 배열·ID 길이를 모듈 수에 맞춘다(모자라면 빈 칸, 남으면 자른다)
        if (ids.arraySize != n) { ids.arraySize = n; }
        foreach (string a in DisplayArrays)
        {
            SerializedProperty p = serializedObject.FindProperty(a);
            if (p.arraySize != n) { p.arraySize = n; }
        }

        EditorGUILayout.LabelField("모듈별 표시 — 장착된 모듈 것만 켜고 나머지는 끈다(여러 개면 빈 부모로 묶어 부모를 넣는다)", EditorStyles.wordWrappedMiniLabel);
        for (int i = 0; i < n; i++)
        {
            Object m = modules.GetArrayElementAtIndex(i).objectReferenceValue;
            string id = ids.GetArrayElementAtIndex(i).stringValue;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"{i}. {(string.IsNullOrEmpty(id) ? "(ID 없음)" : id)}   {(m != null ? m.name : "(모듈 없음)")}", EditorStyles.boldLabel);
                for (int k = 0; k < DisplayArrays.Length; k++)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(DisplayArrays[k]).GetArrayElementAtIndex(i), new GUIContent(DisplayLabels[k]));
                }
            }
        }
        if (n == 0) { EditorGUILayout.HelpBox("모듈이 없습니다 — 기체 셋업(특수무장 카탈로그)으로 넣거나 아래 모듈 목록에 추가하세요.", MessageType.Info); }

        EditorGUILayout.Space(4f);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("EmptyObjects"), new GUIContent("무장 없을 때 켤 것"), true);

        EditorGUILayout.Space(6f);
        _showModules = EditorGUILayout.Foldout(_showModules, "모듈 목록 (추가·제거·순서)", true);
        if (_showModules)
        {
            EditorGUILayout.PropertyField(modules, true);
            EditorGUILayout.PropertyField(ids, true);
        }
        serializedObject.ApplyModifiedProperties();
    }

    /// 배열 길이를 n에 맞춘다(모자라면 null로 채우고 남으면 자른다). 특수무장 카탈로그도 쓴다.
    public static GameObject[] Fit(GameObject[] list, int n)
    {
        var r = new GameObject[n];
        if (list != null) { for (int i = 0; i < n && i < list.Length; i++) { r[i] = list[i]; } }
        return r;
    }
}
