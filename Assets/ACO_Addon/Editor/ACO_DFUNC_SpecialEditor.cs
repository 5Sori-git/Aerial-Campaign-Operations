using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SaccFlightAndVehicles;
using ACO;

/// 특수무장 슬롯 인스펙터: 모듈마다 한 칸 — 모듈 이름·무장 ID 아래에 기체 외형 / HUD 탄종 / MFD 탄종 / 다이얼 아이콘 오브젝트.
/// (실제로는 Modules와 같은 순서의 배열들이다 — 모듈 수에 맞춰 길이를 맞춘다.) 모듈 추가·제거는 아래 "모듈 목록"에서.
/// 탄종 글자 자동 생성: 기체의 HUD 글자(SP.W)·MFD 글자(MFD_SP.W)를 모듈마다 복제해 글자를 무장 ID로 바꾸고 HUD/MFD 탄종 칸에 넣는다(아래 버튼, 또는 메뉴).
[CustomEditor(typeof(ACO_DFUNC_Special))]
public class ACO_DFUNC_SpecialEditor : Editor
{
    private static readonly string[] DisplayArrays = { "ModuleVisuals", "ModuleHudLabels", "ModuleMfdLabels", "ModuleDialIcons" };
    private static readonly string[] DisplayLabels = { "기체 외형", "HUD 탄종", "MFD 탄종", "다이얼 아이콘" };
    public const string HudTemplateName = "SP.W";
    public const string MfdTemplateName = "MFD_SP.W";
    private bool _showModules;
    private GameObject _hudTemplate;
    private GameObject _mfdTemplate;
    private bool _templateAsEmpty = true;

    private void OnEnable()
    {
        var special = (ACO_DFUNC_Special)target;
        _hudTemplate = FindInVehicle(special, HudTemplateName);
        _mfdTemplate = FindInVehicle(special, MfdTemplateName);
    }

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
        serializedObject.ApplyModifiedProperties();

        // 탄종 글자 자동 생성
        EditorGUILayout.Space(6f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("탄종 글자 자동 생성", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"원본 글자(기체 안 '{HudTemplateName}' / '{MfdTemplateName}')를 모듈마다 복제해 'SP.W'를 무장 ID로 바꾸고 HUD/MFD 탄종 칸에 넣는다. 다시 누르면 글자만 고친다.", EditorStyles.wordWrappedMiniLabel);
            _hudTemplate = (GameObject)EditorGUILayout.ObjectField("HUD 원본", _hudTemplate, typeof(GameObject), true);
            _mfdTemplate = (GameObject)EditorGUILayout.ObjectField("MFD 원본", _mfdTemplate, typeof(GameObject), true);
            _templateAsEmpty = EditorGUILayout.ToggleLeft("원본은 무장이 없을 때만 보이게(무장 없을 때 켤 것에 넣기)", _templateAsEmpty);
            using (new EditorGUI.DisabledScope(_hudTemplate == null && _mfdTemplate == null))
            {
                if (GUILayout.Button("HUD/MFD 탄종 글자 만들기"))
                {
                    Debug.Log("[ACO] " + BuildLabels(special, _hudTemplate, _mfdTemplate, _templateAsEmpty));
                }
            }
        }

        EditorGUILayout.Space(6f);
        _showModules = EditorGUILayout.Foldout(_showModules, "모듈 목록 (추가·제거·순서)", true);
        if (_showModules)
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(modules, true);
            EditorGUILayout.PropertyField(ids, true);
            serializedObject.ApplyModifiedProperties();
        }
    }

    /// 배열 길이를 n에 맞춘다(모자라면 null로 채우고 남으면 자른다). 특수무장 카탈로그도 쓴다.
    public static GameObject[] Fit(GameObject[] list, int n)
    {
        var r = new GameObject[n];
        if (list != null) { for (int i = 0; i < n && i < list.Length; i++) { r[i] = list[i]; } }
        return r;
    }

    // ───────────── 탄종 글자 자동 생성 ─────────────

    [MenuItem("ACO/Player Vehicle/Build Special Weapon HUD-MFD Labels on Selected Vehicles")]
    public static void BuildOnSelected()
    {
        var specials = new List<ACO_DFUNC_Special>();
        foreach (GameObject go in Selection.gameObjects)
        {
            SaccEntity e = go.GetComponentInParent<SaccEntity>(true);
            Component root = e != null ? (Component)e : go.transform;
            foreach (ACO_DFUNC_Special s in root.GetComponentsInChildren<ACO_DFUNC_Special>(true)) { if (!specials.Contains(s)) { specials.Add(s); } }
        }
        if (specials.Count == 0)
        {
            EditorUtility.DisplayDialog("ACO", "특수무장 슬롯(ACO_DFUNC_Special)이 있는 기체를 선택하세요.", "확인");
            return;
        }
        var log = new System.Text.StringBuilder("특수무장 탄종 글자:");
        foreach (ACO_DFUNC_Special s in specials)
        {
            log.Append("\n- ").Append(BuildLabels(s, FindInVehicle(s, HudTemplateName), FindInVehicle(s, MfdTemplateName), true));
        }
        Debug.Log("[ACO] " + log);
    }

    /// 모듈마다 원본을 복제(이미 있으면 그대로)하고 글자를 무장 ID로 바꿔 ModuleHudLabels/ModuleMfdLabels에 넣는다.
    public static string BuildLabels(ACO_DFUNC_Special special, GameObject hudTemplate, GameObject mfdTemplate, bool templateAsEmpty)
    {
        int n = special.Modules != null ? special.Modules.Length : 0;
        Undo.RecordObject(special, "ACO special labels");
        special.ModuleHudLabels = Fit(special.ModuleHudLabels, n);
        special.ModuleMfdLabels = Fit(special.ModuleMfdLabels, n);
        int made = 0;
        for (int i = 0; i < n; i++)
        {
            string id = special.ModuleIds != null && i < special.ModuleIds.Length && !string.IsNullOrEmpty(special.ModuleIds[i])
                ? special.ModuleIds[i] : (special.Modules[i] != null ? special.Modules[i].name : $"MOD{i}");
            if (hudTemplate != null) { special.ModuleHudLabels[i] = MakeLabel(hudTemplate, special.ModuleHudLabels[i], id, ref made); }
            if (mfdTemplate != null) { special.ModuleMfdLabels[i] = MakeLabel(mfdTemplate, special.ModuleMfdLabels[i], id, ref made); }
        }
        if (templateAsEmpty)
        {
            var empty = new List<GameObject>(special.EmptyObjects ?? new GameObject[0]);
            foreach (GameObject t in new[] { hudTemplate, mfdTemplate }) { if (t != null && !empty.Contains(t)) { empty.Add(t); } }
            special.EmptyObjects = empty.ToArray();
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(special);
        EditorUtility.SetDirty(special);
        return $"{special.name}: 모듈 {n}개, 새로 만든 글자 {made}개 (HUD 원본 {(hudTemplate != null ? hudTemplate.name : "없음")}, MFD 원본 {(mfdTemplate != null ? mfdTemplate.name : "없음")})";
    }

    /// 원본 옆(같은 부모, 바로 뒤)에 '원본이름_ID' 복제본을 만들거나 이미 있는 것을 쓰고, 글자의 "SP.W"를 ID로 바꾼다(없으면 글자 전체를 ID로).
    private static GameObject MakeLabel(GameObject template, GameObject existing, string id, ref int made)
    {
        string name = template.name + "_" + id;
        GameObject go = existing != null && existing != template ? existing : null;
        if (go == null && template.transform.parent != null)
        {
            Transform found = template.transform.parent.Find(name);
            if (found != null) { go = found.gameObject; }
        }
        if (go == null)
        {
            go = Object.Instantiate(template, template.transform.parent);
            go.name = name;
            go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            Undo.RegisterCreatedObjectUndo(go, "ACO special labels");
            made++;
        }
        string source = TextOf(template);
        string text = source != null && source.Contains("SP.W") ? source.Replace("SP.W", id) : id;
        SetText(go, text);
        // 슬롯이 장착된 모듈 것만 켠다 — 에디터에서는 꺼 두어 원본과 겹쳐 보이지 않게
        if (go.activeSelf) { Undo.RecordObject(go, "ACO special labels"); go.SetActive(false); }
        return go;
    }

    private static string TextOf(GameObject go)
    {
        TMP_Text tmp = go.GetComponent<TMP_Text>();
        if (tmp != null) { return tmp.text; }
        Text t = go.GetComponent<Text>();
        return t != null ? t.text : null;
    }

    private static void SetText(GameObject go, string text)
    {
        TMP_Text tmp = go.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            Undo.RecordObject(tmp, "ACO special labels");
            tmp.text = text;
            EditorUtility.SetDirty(tmp);
            return;
        }
        Text t = go.GetComponent<Text>();
        if (t != null)
        {
            Undo.RecordObject(t, "ACO special labels");
            t.text = text;
            EditorUtility.SetDirty(t);
        }
    }

    /// 슬롯이 있는 기체(SaccEntity) 안에서 이름이 정확히 같은 오브젝트(비활성 포함).
    public static GameObject FindInVehicle(ACO_DFUNC_Special special, string objectName)
    {
        if (special == null) { return null; }
        SaccEntity e = special.GetComponentInParent<SaccEntity>(true);
        Transform root = e != null ? e.transform : special.transform.root;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == objectName) { return t.gameObject; }
        }
        return null;
    }
}
