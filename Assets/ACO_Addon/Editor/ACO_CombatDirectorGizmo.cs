using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;
using ACO;

/// CombatDirector의 흐름을 씬 뷰에 그린다(에디터 전용, 게임 빌드에 포함되지 않음).
///
/// 플레이 모드(ClientSim): 실제 로직은 Udon이 돌리므로 C# 쪽 필드가 아니라 각 NPC의 UdonBehaviour 변수를 읽는다.
///   - NPC → 타겟 선: 빨강 실선 = 추격, 주황 실선 = 이탈, 하늘색 점선 = 대기, 순찰은 선 없음
///   - 타겟 주위: 하늘색 원 = 대기 선회 궤도(고도 포함), 빨간 구 = 자기 방어 거리
///   - 라벨: 타겟 "공격 2/2 · 대기 5", NPC "추격 12s" / "대기 #2" / "대기 #3 · 쿨다운 6s"
/// 편집 모드: 디렉터를 선택하면 연결된 NPC로 선을 그려 베이크가 됐는지 보여준다.
public static class ACO_CombatDirectorGizmo
{
    // ACO_NPCFighterAI의 private 필드 이름과 모드 값. AI에서 이름/값을 바꾸면 여기도 바꾼다.
    private const string F_MODE = "_mode";
    private const string F_ALIVE = "_alive";
    private const string F_TARGET = "_target";
    private const string F_ENGAGE = "_engageTimer";
    private const string F_YIELD = "_yieldTimer";
    private const string F_WAIT_START = "_waitStartTime";

    private const byte MODE_PATROL = 0;
    private const byte MODE_PURSUIT = 1;
    private const byte MODE_EXTEND = 2;
    private const byte MODE_WAIT = 3;

    private static readonly Color PursuitColor = new Color(1f, 0.25f, 0.2f);
    private static readonly Color ExtendColor = new Color(1f, 0.6f, 0.1f);
    private static readonly Color WaitColor = new Color(0.3f, 0.8f, 1f);
    private static readonly Color PatrolColor = new Color(0.7f, 0.7f, 0.7f);
    private static readonly Color DeadColor = new Color(0.4f, 0.4f, 0.4f);
    private static readonly Color DefendFill = new Color(1f, 0.2f, 0.2f, 0.06f);
    private static readonly Color DefendWire = new Color(1f, 0.2f, 0.2f, 0.5f);

    private class FighterState
    {
        public ACO_NPCFighterAI Ai;
        public byte Mode;
        public bool Alive;
        public Transform Target;
        public float Engage;
        public float Yield;
        public float WaitStart;
        public int QueueOrder; // 대기 순번(1부터). 대기가 아니면 0
    }

    private static GUIStyle _labelStyle;

    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
    private static void Draw(ACO_CombatDirector director, GizmoType gizmoType)
    {
        if (!director.ShowGizmos) { return; }

        if (Application.isPlaying) { DrawPlayMode(director); }
        else if ((gizmoType & GizmoType.Selected) != 0) { DrawEditMode(director); }
    }

    // ───────────── 편집 모드: 연결 확인 ─────────────

    private static void DrawEditMode(ACO_CombatDirector director)
    {
        Vector3 origin = director.transform.position;
        int count = director.Fighters != null ? director.Fighters.Length : 0;

        Handles.color = PatrolColor;
        if (director.Fighters != null)
        {
            foreach (ACO_NPCFighterAI ai in director.Fighters)
            {
                if (ai != null) { Handles.DrawDottedLine(origin, ai.transform.position, 6f); }
            }
        }

        if (director.ShowLabels)
        {
            string text = count > 0
                ? $"<b>CombatDirector</b>\n연결된 NPC {count}기 · 정원 {director.MaxAttackersPerTarget}"
                : "<b>CombatDirector</b>\n<color=#ff6060>연결된 NPC 없음 — ACO → Bake NPC References</color>";
            Handles.Label(origin + Vector3.up * 5f, text, LabelStyle());
        }
    }

    // ───────────── 플레이 모드: 실제 흐름 ─────────────

    private static void DrawPlayMode(ACO_CombatDirector director)
    {
        if (director.Fighters == null) { return; }

        var states = new List<FighterState>();
        var byTarget = new Dictionary<Transform, List<FighterState>>();

        foreach (ACO_NPCFighterAI ai in director.Fighters)
        {
            FighterState s = ReadState(ai);
            if (s == null) { continue; }
            states.Add(s);

            if (s.Alive && s.Target != null && s.Mode != MODE_PATROL)
            {
                if (!byTarget.TryGetValue(s.Target, out List<FighterState> list))
                {
                    list = new List<FighterState>();
                    byTarget.Add(s.Target, list);
                }
                list.Add(s);
            }
        }

        foreach (KeyValuePair<Transform, List<FighterState>> pair in byTarget)
        {
            AssignQueueOrder(pair.Value);
            DrawTarget(director, pair.Key, pair.Value);
        }

        foreach (FighterState s in states) { DrawFighter(director, s); }
    }

    /// 대기 순번은 디렉터와 같은 규칙(먼저 기다린 순서)으로 매긴다.
    private static void AssignQueueOrder(List<FighterState> group)
    {
        var waiting = new List<FighterState>();
        foreach (FighterState s in group)
        {
            if (s.Mode == MODE_WAIT) { waiting.Add(s); }
        }
        waiting.Sort((a, b) => a.WaitStart.CompareTo(b.WaitStart));
        for (int i = 0; i < waiting.Count; i++) { waiting[i].QueueOrder = i + 1; }
    }

    private static void DrawTarget(ACO_CombatDirector director, Transform target, List<FighterState> group)
    {
        Vector3 pos = target.position;
        int attackers = 0;
        int waiting = 0;
        foreach (FighterState s in group)
        {
            if (s.Mode == MODE_PURSUIT || s.Mode == MODE_EXTEND) { attackers++; }
            else if (s.Mode == MODE_WAIT) { waiting++; }
        }

        // 자기 방어 거리: 이 안으로 들어온 대기 NPC는 정원과 무관하게 교전한다.
        Gizmos.color = DefendFill;
        Gizmos.DrawSphere(pos, director.DefendDistance);
        Gizmos.color = DefendWire;
        Gizmos.DrawWireSphere(pos, director.DefendDistance);

        // 대기 선회 궤도(실제 반경은 NPC마다 LoseTargetRange의 80%로 줄 수 있다 — 여기선 디렉터 값을 그린다).
        if (waiting > 0)
        {
            Handles.color = WaitColor;
            Vector3 orbitCenter = new Vector3(pos.x, pos.y + director.WaitAltitudeAboveTarget, pos.z);
            Handles.DrawWireDisc(orbitCenter, Vector3.up, director.WaitOrbitRadius);
            Handles.DrawDottedLine(pos, orbitCenter, 4f);
        }

        if (director.ShowLabels)
        {
            int max = Mathf.Max(director.MaxAttackersPerTarget, 1);
            string countColor = attackers > max ? "#ff5050" : "#ffffff";
            string text = $"<b>{target.root.name}</b>\n<color={countColor}>공격 {attackers}/{max}</color> · <color=#60d0ff>대기 {waiting}</color>";
            Handles.Label(pos + Vector3.up * (director.DefendDistance * 0.15f + 30f), text, LabelStyle());
        }
    }

    private static void DrawFighter(ACO_CombatDirector director, FighterState s)
    {
        Vector3 pos = s.Ai.transform.position;
        string label;

        if (!s.Alive)
        {
            label = $"<color=#808080>{s.Ai.name}\n격추</color>";
            Handles.color = DeadColor;
        }
        else if (s.Mode == MODE_PURSUIT || s.Mode == MODE_EXTEND)
        {
            bool pursuit = s.Mode == MODE_PURSUIT;
            Handles.color = pursuit ? PursuitColor : ExtendColor;
            if (s.Target != null) { Handles.DrawAAPolyLine(3f, pos, s.Target.position); }

            float remain = director.MaxEngageTime > 0f ? Mathf.Max(director.MaxEngageTime - s.Engage, 0f) : -1f;
            string swap = remain >= 0f ? $" (교대까지 {remain:0}s)" : "";
            string colorHex = pursuit ? "#ff6050" : "#ffa030";
            label = $"<color={colorHex}>{s.Ai.name}\n{(pursuit ? "추격" : "이탈")} {s.Engage:0}s{swap}</color>";
        }
        else if (s.Mode == MODE_WAIT)
        {
            Handles.color = WaitColor;
            if (s.Target != null) { Handles.DrawDottedLine(pos, s.Target.position, 4f); }

            string cooldown = s.Yield > 0f ? $" · 쿨다운 {s.Yield:0}s" : "";
            label = $"<color=#60d0ff>{s.Ai.name}\n대기 #{s.QueueOrder}{cooldown}</color>";
        }
        else
        {
            Handles.color = PatrolColor;
            label = $"<color=#b0b0b0>{s.Ai.name}\n순찰</color>";
        }

        // 기체 위치 표시(멀리서도 보이도록 화면 크기 기준).
        float size = HandleUtility.GetHandleSize(pos) * 0.08f;
        Vector3 facing = Camera.current != null ? -Camera.current.transform.forward : Vector3.up;
        Handles.DrawSolidDisc(pos, facing, size);

        if (director.ShowLabels)
        {
            Handles.Label(pos + Vector3.up * 15f, label, LabelStyle());
        }
    }

    // ───────────── Udon 변수 읽기 ─────────────

    private static FighterState ReadState(ACO_NPCFighterAI ai)
    {
        if (ai == null) { return null; }
        UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(ai);
        if (ub == null) { return null; }

        // GetProgramVariable은 변수가 없으면 매 프레임 에러 로그를 남기므로 조용히 실패하는 Try 버전을 쓴다.
        var s = new FighterState { Ai = ai };
        ub.TryGetProgramVariable(F_MODE, out object mode);
        ub.TryGetProgramVariable(F_ALIVE, out object alive);
        ub.TryGetProgramVariable(F_TARGET, out object target);
        ub.TryGetProgramVariable(F_ENGAGE, out object engage);
        ub.TryGetProgramVariable(F_YIELD, out object yield);
        ub.TryGetProgramVariable(F_WAIT_START, out object waitStart);

        s.Mode = mode is byte m ? m : MODE_PATROL;
        s.Alive = !(alive is bool a) || a;
        s.Target = target as Transform;
        s.Engage = engage is float e ? e : 0f;
        s.Yield = yield is float y ? y : 0f;
        s.WaitStart = waitStart is float w ? w : 0f;
        return s;
    }

    private static GUIStyle LabelStyle()
    {
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                alignment = TextAnchor.LowerCenter,
                fontSize = 11,
            };
            _labelStyle.normal.textColor = Color.white;
        }
        return _labelStyle;
    }
}
