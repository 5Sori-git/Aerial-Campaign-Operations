using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using ACO;

/// 대사 표 페이즈(공중 요새, 랜스 포대, 함대전) 인스펙터: 대사 표를 한 줄씩 보여 준다(실제로는 LineEvent·LineSpeaker·LineText·LineClip 같은 순서의 배열).
/// 같은 상황의 줄은 위에서 아래로 차례로 나온다 — 적 줄 다음에 AWACS 줄을 두면 적 대사에 AWACS가 이어 말한다.
[CustomEditor(typeof(ACO_PhaseBoss), true)]
public class ACO_PhaseFortressEditor : Editor
{
    private bool _showBgm = true;
    private static readonly string[] Arrays = { "LineEvent", "LineSpeaker", "LineText", "LineClip" };
    // ACO_PhaseRailgun.SIT_* 순서
    private static readonly string[] RailgunSituations =
    {
        "등장", "방공망 절반 파괴", "냉각기 단계 시작", "냉각기 파괴 ({left} = 남은 수)", "방공망 수리", "적 증원",
        "코어 단계 시작", "충전 시작", "발사 임박", "코어 장갑 열림", "아군 기지 피격", "기지 마지막 1칸",
        "코어 체력 절반", "코어 파괴", "대폭발", "아군 기지 파괴", "소형 레일건 첫 사격",
    };
    // ACO_PhaseFortress.SIT_* 순서
    private static readonly string[] Situations =
    {
        "등장", "방공망 절반 파괴", "엔진 단계 시작", "엔진 파괴 ({left} = 남은 수)", "방공망 수리", "적 증원",
        "돌진 시작", "코어 체력 절반", "도시 접근 (먼 거리)", "도시 접근 (가까운 거리)", "도시 접근 (마지막)",
        "코어 파괴", "추락 충돌", "도시 충돌",
    };
    // ACO_PhaseFleet.SIT_* 순서
    private static readonly string[] FleetSituations =
    {
        "양 함대 교전 시작", "적 함재기 출격", "기함 체력 절반", "기함 위험", "적 항모 체력 절반",
    };
    // ACO_PhaseLandShip.SIT_* 순서
    private static readonly string[] LandShipSituations =
    {
        "등장", "첫 스커트 파괴", "궤도 절반 파괴 ({left} = 남은 수)", "첫 배기구 노출", "배기구 절반 파괴", "코어 셔터 열림",
        "코어 체력 절반", "도시 접근 (먼 거리)", "도시 접근 (가까운 거리)", "도시 접근 (마지막)", "코어 파괴", "도시 도착", "방공 수리",
        "총력 복구 (자동 사통)", "방공 수리 (자동 사통 뒤)",
    };
    // ACO_PhaseBorderless.SIT_* 순서
    private static readonly string[] BorderlessSituations =
    {
        "등장", "탄도미사일 발사 (2단계 시작)", "ECM 재밍 (정면 락온만)", "착탄 2분 전", "착탄 1분 전", "착탄 30초 전",
        "에이스 격추 (미사일 공중 폭발)", "착탄 (기지 파괴)",
    };
    // ACO_PhaseLuna.SIT_* 순서
    private static readonly string[] LunaSituations =
    {
        "등장", "UAV 전개 (1차)", "UAV 전멸", "UAV 재전개 (2차)", "폭격 2분 전", "폭격 1분 전", "폭격 30초 전",
        "LUNA 격추 (폭격 빗나감)", "도시 착탄",
    };
    private static readonly string[] Speakers = { "적", "AWACS" };
    private static readonly string[] FleetSpeakers = { "적", "AWACS", "함대장" };
    private static readonly string[] LandShipSpeakers = { "적 함장", "AWACS", "-", "선원", "시스템" };
    private bool _showLines = true;

    /// 모듈 종류별 상황 이름(각 스크립트의 SIT_* 순서). 새 대사 표 모듈을 만들면 여기에 더한다 — 빠지면 공중 요새 이름이 나온다.
    private static string[] SituationsFor(Object t)
    {
        if (t is ACO_PhaseFleet) { return FleetSituations; }
        if (t is ACO_PhaseRailgun) { return RailgunSituations; }
        if (t is ACO_PhaseLandShip) { return LandShipSituations; }
        if (t is ACO_PhaseBorderless) { return BorderlessSituations; }
        if (t is ACO_PhaseLuna) { return LunaSituations; }
        return Situations;
    }

    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) { return; }
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", Arrays[0], Arrays[1], Arrays[2], Arrays[3], "BgmUrls", "BgmNames");
        ACO_BgmPoolGUI.Draw(serializedObject, "BgmUrls", "BgmNames", "이 페이즈 전용 BGM (비우면 보스 기본 풀)", ref _showBgm);

        SerializedProperty events = serializedObject.FindProperty(Arrays[0]);
        SerializedProperty speakers = serializedObject.FindProperty(Arrays[1]);
        SerializedProperty texts = serializedObject.FindProperty(Arrays[2]);
        SerializedProperty clips = serializedObject.FindProperty(Arrays[3]);
        int n = Mathf.Max(events.arraySize, Mathf.Max(speakers.arraySize, Mathf.Max(texts.arraySize, clips.arraySize)));
        events.arraySize = n;
        speakers.arraySize = n;
        texts.arraySize = n;
        clips.arraySize = n;

        EditorGUILayout.Space();
        _showLines = EditorGUILayout.Foldout(_showLines, "상황별 대사 표 (" + n + "줄 — 같은 상황은 위에서 아래로 차례로)", true);
        if (_showLines)
        {
            int remove = -1;
            int moveUp = -1;
            for (int i = 0; i < n; i++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        SerializedProperty sit = events.GetArrayElementAtIndex(i);
                        SerializedProperty who = speakers.GetArrayElementAtIndex(i);
                        sit.intValue = EditorGUILayout.Popup(sit.intValue, SituationsFor(target));
                        who.intValue = EditorGUILayout.Popup(who.intValue, target is ACO_PhaseFleet ? FleetSpeakers : (target is ACO_PhaseLandShip ? LandShipSpeakers : Speakers), GUILayout.Width(76f));
                        using (new EditorGUI.DisabledScope(i == 0))
                        {
                            if (GUILayout.Button("▲", GUILayout.Width(24f))) { moveUp = i; }
                        }
                        if (GUILayout.Button("X", GUILayout.Width(24f))) { remove = i; }
                    }
                    SerializedProperty text = texts.GetArrayElementAtIndex(i);
                    text.stringValue = EditorGUILayout.TextArea(text.stringValue, EditorStyles.textArea, GUILayout.MinHeight(34f));
                    EditorGUILayout.PropertyField(clips.GetArrayElementAtIndex(i), new GUIContent("음성"));
                }
            }
            if (moveUp > 0)
            {
                foreach (string a in Arrays) { serializedObject.FindProperty(a).MoveArrayElement(moveUp, moveUp - 1); }
            }
            if (remove >= 0)
            {
                foreach (string a in Arrays)
                {
                    SerializedProperty p = serializedObject.FindProperty(a);
                    if (a == "LineClip") { p.GetArrayElementAtIndex(remove).objectReferenceValue = null; }
                    p.DeleteArrayElementAtIndex(remove);
                }
            }
            if (GUILayout.Button("줄 추가"))
            {
                foreach (string a in Arrays) { serializedObject.FindProperty(a).arraySize = n + 1; }
            }
        }
        serializedObject.ApplyModifiedProperties();
    }
}
