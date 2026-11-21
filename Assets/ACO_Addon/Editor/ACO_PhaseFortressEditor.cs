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
    // ACO_PhaseSubmarine.SIT_* 순서(6-202 · 6-203)
    private static readonly string[] SubmarineSituations =
    {
        "등장", "일제 발사 ({left} = 방위)", "종말 상승 시작 (한 번)", "항모 피격", "항모 위기", "SLCM 소진", "소나 원",
        "부상", "부상 (무장 수리됨)", "잠항 · 어뢰", "완전 부상 (충각 돌격 시작)", "어뢰 (대비책 — 돌격 길이 막혔을 때)", "격침",
        "항모 침몰 (SLCM · 어뢰)", "돌격 접근 경고", "충돌 (항모 격침)", "돌격 중 무장 수리",
    };
    private static readonly string[] SubmarineSpeakers = { "적 함장", "AWACS", "항모", "-", "-", "항모 승무원" };
    // ACO_PhaseLaserTower.SIT_* 순서(칼리번 6-331)
    private static readonly string[] LaserTowerSituations =
    {
        "등장", "ECM 차량 파괴 ({left} = 남은 수)", "환풍구 단계 시작", "환풍구 파괴 ({left} = 남은 수)", "렌즈 단계 시작", "과열 (첫 번째)",
        "그물 예고", "방공 수리", "적 증원", "렌즈 체력 절반", "렌즈 파괴", "탑 붕괴", "비행선 전멸",
        "재밍 힌트 (1단계가 오래갈 때)", "ECM 차량 하나 남음", "환풍구 하나 남음", "패턴 A 조준", "비행선 격추 ({left} = 남은 수)", "과열 (두 번째부터)",
    };
    private static readonly string[] LaserTowerSpeakers = { "적 (칼리번)", "AWACS", "아군 (쓰지 않음)", "-", "시스템 (쓰지 않음)" };
    // ACO_PhaseBreakingPoint.SIT_* 순서(6-333)
    private static readonly string[] BreakingPointSituations =
    {
        "(쓰지 않음)", "첫 접촉 (적이 공역에 들어옴)", "새 웨이브", "킬 절반", "남은 킬 5 · 3 · 1 ({left})", "아군 격추 (한 기마다)", "(쓰지 않음)", "적 물러남 (목표 달성)",
    };
    private static readonly string[] BreakingPointSpeakers = { "적 공군", "AWACS", "아군 (쓰지 않음)" };
    private bool _showBgm2 = true;
    private bool _showBgm3 = true;
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
        if (t is ACO_PhaseSubmarine) { return SubmarineSituations; }
        if (t is ACO_PhaseLaserTower) { return LaserTowerSituations; }
        if (t is ACO_PhaseBreakingPoint) { return BreakingPointSituations; }
        return Situations;
    }

    private static string[] SpeakersFor(Object t)
    {
        if (t is ACO_PhaseFleet) { return FleetSpeakers; }
        if (t is ACO_PhaseLandShip) { return LandShipSpeakers; }
        if (t is ACO_PhaseSubmarine) { return SubmarineSpeakers; }
        if (t is ACO_PhaseLaserTower) { return LaserTowerSpeakers; }
        if (t is ACO_PhaseBreakingPoint) { return BreakingPointSpeakers; }
        return Speakers;
    }

    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) { return; }
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", Arrays[0], Arrays[1], Arrays[2], Arrays[3], "BgmUrls", "BgmNames", "BgmGains",
            "BgmUrls2", "BgmNames2", "BgmGains2", "BgmUrls3", "BgmNames3", "BgmGains3");
        if (target is ACO_PhaseSubmarine)
        {
            // 단계별 곡(6-203): 비운 단계는 앞 단계 곡을 잇는다
            ACO_BgmPoolGUI.Draw(serializedObject, "BgmUrls", "BgmNames", "BgmGains", "BGM 1단계 — SLCM 방어 (비우면 보스 기본 풀)", ref _showBgm);
            ACO_BgmPoolGUI.Draw(serializedObject, "BgmUrls2", "BgmNames2", "BgmGains2", "BGM 2단계 — 간헐 부상 (비우면 1단계 곡을 잇는다)", ref _showBgm2);
            ACO_BgmPoolGUI.Draw(serializedObject, "BgmUrls3", "BgmNames3", "BgmGains3", "BGM 3단계 — 완전 부상 · 충각 돌격 (비우면 앞 단계 곡을 잇는다)", ref _showBgm3);
        }
        else { ACO_BgmPoolGUI.Draw(serializedObject, "BgmUrls", "BgmNames", "BgmGains", "이 페이즈 전용 BGM (비우면 보스 기본 풀)", ref _showBgm); }

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
                        who.intValue = EditorGUILayout.Popup(who.intValue, SpeakersFor(target), GUILayout.Width(76f));
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
