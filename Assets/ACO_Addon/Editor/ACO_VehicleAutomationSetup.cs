using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 선택한 기체에 ACO_VehicleAutomation(자동 캐노피 + 기어·후크 연동)을 붙인다: 기체 아래 'ACO_VehicleAutomation' 오브젝트에 컴포넌트를 두고
/// SaccAirVehicle·DFUNC_Canopy·DFUNC_Gear·DFUNC_Hook을 찾아 넣고, SaccEntity.ExtensionUdonBehaviours에 더한다(이미 있으면 참조만 다시 채운다).
/// 고르면 캐노피·후크를 다이얼(Dial_Functions_L/R)에서 빼서 확장 목록으로 옮긴다 — 다이얼 칸이 비지만 뒤 칸이 당겨지므로 MFD 다이얼 그림도 맞춰야 한다.
public static class ACO_VehicleAutomationSetup
{
    private const string ObjectName = "ACO_VehicleAutomation";

    [MenuItem("ACO/Player Vehicle/Add Auto Canopy + Gear-Hook Link to Selected Vehicles")]
    public static void AddToSelected()
    {
        var entities = new List<SaccEntity>();
        foreach (GameObject go in Selection.gameObjects)
        {
            SaccEntity e = go.GetComponentInParent<SaccEntity>(true);
            if (e == null) { e = go.GetComponentInChildren<SaccEntity>(true); }
            if (e != null && !entities.Contains(e)) { entities.Add(e); }
        }
        if (entities.Count == 0)
        {
            EditorUtility.DisplayDialog("ACO", "기체(SaccEntity)를 선택하세요.", "확인");
            return;
        }
        bool moveFromDial = EditorUtility.DisplayDialog("ACO 기체 자동화",
            "캐노피와 후크를 다이얼에서 빼서 확장 목록으로 옮길까요?\n\n" +
            "옮기면 다이얼 칸이 비고(캐노피는 자동, 후크는 기어와 함께 움직인다), 뒤 칸 기능이 한 칸씩 당겨진다 — 기체 MFD의 다이얼 그림도 맞춰야 한다.\n" +
            "그대로 두면 다이얼로도 조작할 수 있다.",
            "다이얼에서 빼기", "그대로 두기");

        var log = new System.Text.StringBuilder();
        foreach (SaccEntity e in entities) { log.Append(Setup(e, moveFromDial)); }
        Debug.Log("[ACO] 기체 자동화(자동 캐노피 + 기어·후크 연동):" + log);
    }

    private static string Setup(SaccEntity e, bool moveFromDial)
    {
        SaccAirVehicle sav = e.GetComponentInChildren<SaccAirVehicle>(true);
        DFUNC_Canopy canopy = e.GetComponentInChildren<DFUNC_Canopy>(true);
        DFUNC_Gear gear = e.GetComponentInChildren<DFUNC_Gear>(true);
        DFUNC_Hook hook = e.GetComponentInChildren<DFUNC_Hook>(true);

        ACO_VehicleAutomation auto = e.GetComponentInChildren<ACO_VehicleAutomation>(true);
        if (auto == null)
        {
            var go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "ACO vehicle automation");
            go.transform.SetParent(e.transform, false);
            auto = UdonSharpUndo.AddComponent<ACO_VehicleAutomation>(go);
        }
        Undo.RecordObject(auto, "ACO vehicle automation");
        auto.SAVControl = sav;
        auto.Canopy = canopy;
        auto.Gear = gear;
        auto.Hook = hook;
        int restore = BakeRespawnRestore(e, auto);
        EditorUtility.SetDirty(auto);

        Undo.RecordObject(e, "ACO vehicle automation");
        var ext = new List<UdonSharpBehaviour>(e.ExtensionUdonBehaviours ?? new UdonSharpBehaviour[0]);
        if (!ext.Contains(auto)) { ext.Add(auto); }
        int moved = 0;
        if (moveFromDial)
        {
            foreach (UdonSharpBehaviour f in new UdonSharpBehaviour[] { canopy, hook })
            {
                if (f == null) { continue; }
                bool inDial = false;
                e.Dial_Functions_L = Remove(e.Dial_Functions_L, f, ref inDial);
                e.Dial_Functions_R = Remove(e.Dial_Functions_R, f, ref inDial);
                if (inDial) { moved++; }
                if (!ext.Contains(f)) { ext.Add(f); }
            }
        }
        e.ExtensionUdonBehaviours = ext.ToArray();
        PrefabUtility.RecordPrefabInstancePropertyModifications(e);
        EditorUtility.SetDirty(e);

        return $"\n- {e.name}: 캐노피 {(canopy != null ? "O" : "없음")}, 기어 {(gear != null ? "O" : "없음")}, 후크 {(hook != null ? "O" : "없음")}"
            + (moveFromDial ? $", 다이얼에서 뺀 기능 {moved}개" : "") + $", 리스폰 복구 오브젝트 {restore}개";
    }

    /// 기체 애니메이터 컨트롤러에서 explode 트리거로 들어가는 상태의 클립이 끄는 오브젝트(m_IsActive)를 찾아 지금(에디터) 켜짐 상태와 함께 굽는다.
    private static int BakeRespawnRestore(SaccEntity e, ACO_VehicleAutomation auto)
    {
        Animator anim = e.GetComponent<Animator>();
        auto.VehicleAnimator = anim;
        var objs = new List<GameObject>();
        var active = new List<bool>();
        AnimatorController ac = anim != null ? anim.runtimeAnimatorController as AnimatorController : null;
        if (ac == null)
        {
            if (anim != null && anim.runtimeAnimatorController is AnimatorOverrideController ov) { ac = ov.runtimeAnimatorController as AnimatorController; }
        }
        if (ac != null)
        {
            var states = new List<AnimatorState>();
            foreach (AnimatorControllerLayer layer in ac.layers) { CollectExplodeStates(layer.stateMachine, states); }
            foreach (AnimatorState st in states)
            {
                var clip = st.motion as AnimationClip;
                if (clip == null) { continue; }
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.propertyName != "m_IsActive" || string.IsNullOrEmpty(b.path)) { continue; }
                    Transform t = anim.transform.Find(b.path);
                    if (t == null || objs.Contains(t.gameObject)) { continue; }
                    objs.Add(t.gameObject);
                    active.Add(t.gameObject.activeSelf);
                }
            }
        }
        auto.RespawnRestoreObjects = objs.ToArray();
        auto.RespawnRestoreActive = active.ToArray();
        return objs.Count;
    }

    /// explode 트리거 조건인 전이의 도착 상태(하위 상태 기계 포함).
    private static void CollectExplodeStates(AnimatorStateMachine sm, List<AnimatorState> into)
    {
        foreach (AnimatorStateTransition t in sm.anyStateTransitions) { AddIfExplode(t, into); }
        foreach (ChildAnimatorState cs in sm.states)
        {
            foreach (AnimatorStateTransition t in cs.state.transitions) { AddIfExplode(t, into); }
        }
        foreach (ChildAnimatorStateMachine child in sm.stateMachines) { CollectExplodeStates(child.stateMachine, into); }
    }

    private static void AddIfExplode(AnimatorStateTransition t, List<AnimatorState> into)
    {
        if (t.destinationState == null || into.Contains(t.destinationState)) { return; }
        foreach (AnimatorCondition c in t.conditions)
        {
            if (string.Equals(c.parameter, "explode", System.StringComparison.OrdinalIgnoreCase)) { into.Add(t.destinationState); return; }
        }
    }

    private static UdonSharpBehaviour[] Remove(UdonSharpBehaviour[] list, UdonSharpBehaviour f, ref bool found)
    {
        if (list == null) { return null; }
        var l = new List<UdonSharpBehaviour>(list);
        if (l.Remove(f)) { found = true; }
        return l.ToArray();
    }
}
