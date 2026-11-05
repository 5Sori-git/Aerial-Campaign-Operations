using UnityEditor;
using UnityEngine;
using ACO;

/// 페이즈 모듈의 페이즈별 가중치(ACO_PhaseModule.PhaseWeights)를 한 표에서 고치고, 페이즈마다 나올 확률(%)을 본다. 메뉴 ACO → Missions → Phase Weights.
/// 값은 씬의 모듈에 저장된다(씬을 저장해야 남는다). 칸을 비운 모듈(자동)은 모든 페이즈에 Weight를 쓴다 — 칸을 고치면 5칸이 만들어진다.
public class ACO_PhaseWeightWindow : EditorWindow
{
    private const float NameWidth = 190f;
    private const float CellWidth = 56f;
    private Vector2 _scroll;

    [MenuItem("ACO/Missions/Phase Weights")]
    public static void Open() { GetWindow<ACO_PhaseWeightWindow>("페이즈 가중치"); }

    private void OnGUI()
    {
        ACO_MissionManager manager = Object.FindObjectOfType<ACO_MissionManager>(true);
        if (manager == null)
        {
            EditorGUILayout.HelpBox("씬에 미션 관리자(ACO_MissionManager)가 없습니다.", MessageType.Warning);
            return;
        }
        ACO_PhaseModule[] modules = manager.GetComponentsInChildren<ACO_PhaseModule>(true);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.HelpBox("가중치가 클수록 자주 나오고, 0이면 그 페이즈엔 안 나옵니다. 아래 %는 그 페이즈 후보끼리의 비율입니다. "
            + "실제로는 직전과 같은 종류가 빠지고, 배치할 수 없는 종류(지형·풀)도 빠져서 조금 달라집니다. 고친 뒤 씬을 저장하세요.", MessageType.Info);

        Section(modules, false, "일반 페이즈 — 가중치 (열 = 페이즈 번호)");
        Percent(modules, false, "일반 페이즈 — 나올 확률");
        EditorGUILayout.Space(12f);
        Section(modules, true, "보너스 페이즈 — 가중치 (열 = 본 미션 페이즈 수)");
        Percent(modules, true, "보너스 페이즈 — 후보끼리의 확률 (페이즈 수별 확률 " + BonusChances(manager) + "로 굴린 뒤)");

        EditorGUILayout.EndScrollView();
    }

    private static string BonusChances(ACO_MissionManager manager)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (int p = 1; p <= ACO_MissionManager.MAX_PHASES; p++) { parts.Add(Mathf.RoundToInt(manager.GetBonusChance(p) * 100f) + "%"); }
        return string.Join(" / ", parts);
    }

    private static void Header(string title)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(NameWidth));
        for (int p = 1; p <= ACO_MissionManager.MAX_PHASES; p++) { GUILayout.Label(p.ToString(), GUILayout.Width(CellWidth)); }
        EditorGUILayout.EndHorizontal();
    }

    /// 이 페이즈에 실제로 쓰이는 가중치(꺼졌거나 MinPhase 전이면 0).
    private static float Effective(ACO_PhaseModule m, int phase)
    {
        return m.Enabled && phase >= m.MinPhase ? m.GetPhaseWeight(phase) : 0f;
    }

    private static void Section(ACO_PhaseModule[] modules, bool bonus, string title)
    {
        Header(title);
        foreach (ACO_PhaseModule m in modules)
        {
            if (m.BonusOnly != bonus) { continue; }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(m.gameObject.name + (m.Enabled ? "" : " (꺼짐)"), GUILayout.Width(NameWidth));
            for (int p = 1; p <= ACO_MissionManager.MAX_PHASES; p++)
            {
                using (new EditorGUI.DisabledScope(p < m.MinPhase))
                {
                    float value = m.GetPhaseWeight(p);
                    float edited = EditorGUILayout.DelayedFloatField(value, GUILayout.Width(CellWidth));
                    if (!Mathf.Approximately(edited, value)) { SetWeight(m, p, Mathf.Max(edited, 0f)); }
                }
            }
            bool perPhase = m.PhaseWeights != null && m.PhaseWeights.Length > 0;
            using (new EditorGUI.DisabledScope(!perPhase))
            {
                if (GUILayout.Button("칸 비우기", GUILayout.Width(70f))) { ClearWeights(m); }
            }
            if (GUILayout.Button("선택", GUILayout.Width(44f))) { Selection.activeGameObject = m.gameObject; }
            EditorGUILayout.EndHorizontal();
        }
    }

    private static void Percent(ACO_PhaseModule[] modules, bool bonus, string title)
    {
        Header(title);
        foreach (ACO_PhaseModule m in modules)
        {
            if (m.BonusOnly != bonus) { continue; }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(m.gameObject.name, GUILayout.Width(NameWidth));
            for (int p = 1; p <= ACO_MissionManager.MAX_PHASES; p++)
            {
                float total = 0f;
                foreach (ACO_PhaseModule o in modules) { if (o.BonusOnly == bonus) { total += Effective(o, p); } }
                float w = Effective(m, p);
                GUILayout.Label(w > 0f && total > 0f ? Mathf.RoundToInt(w / total * 100f) + "%" : "-", GUILayout.Width(CellWidth));
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private static void SetWeight(ACO_PhaseModule m, int phase, float value)
    {
        Undo.RecordObject(m, "ACO phase weight");
        int n = ACO_MissionManager.MAX_PHASES;
        var weights = new float[n];
        for (int p = 1; p <= n; p++) { weights[p - 1] = m.GetPhaseWeight(p); }
        weights[phase - 1] = value;
        m.PhaseWeights = weights;
        MarkChanged(m);
    }

    private static void ClearWeights(ACO_PhaseModule m)
    {
        Undo.RecordObject(m, "ACO phase weight");
        m.PhaseWeights = new float[0];
        MarkChanged(m);
    }

    private static void MarkChanged(ACO_PhaseModule m)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(m);
        EditorUtility.SetDirty(m);
    }
}
