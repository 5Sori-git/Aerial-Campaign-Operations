using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using SaccFlightAndVehicles;
using ACO;

/// 선택한 기체의 SAV DFUNC_Catapult를 ACO_DFUNC_Catapult(풀스로틀 유지 사출)로 바꾼다:
/// 같은 오브젝트(물리는 지점·BoxCollider 그대로)에 새 컴포넌트를 붙이고 값을 옮긴 뒤, SaccEntity의 다이얼(Dial_Functions_L/R)·확장 목록에서
/// 옛 것을 새 것으로 바꾸고 옛 컴포넌트를 지운다. SAV의 AutoLaunch + AutoLaunch_FullThrottle이었으면 그 지연을 유지 시간으로(최소 1초).
public static class ACO_CatapultSwap
{
    [MenuItem("ACO/Player Vehicle/Replace Catapult on Selected Vehicles")]
    public static void ReplaceSelected()
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
        int replaced = 0;
        foreach (SaccEntity e in entities)
        {
            foreach (DFUNC_Catapult old in e.GetComponentsInChildren<DFUNC_Catapult>(true))
            {
                Replace(e, old);
                replaced++;
            }
        }
        Debug.Log($"[ACO] 캐터펄트 {replaced}개를 ACO_DFUNC_Catapult(풀스로틀 유지 사출)로 바꿨습니다 — 기체 {entities.Count}대.");
    }

    private static void Replace(SaccEntity e, DFUNC_Catapult old)
    {
        GameObject go = old.gameObject;
        var so = new SerializedObject(old);
        SerializedProperty delayProp = so.FindProperty("AutoLaunchDelay");
        SerializedProperty alignProp = so.FindProperty("AlignToCatapult");
        float delay = delayProp != null ? delayProp.floatValue : 1f;
        bool align = alignProp == null || alignProp.boolValue;

        ACO_DFUNC_Catapult neu = UdonSharpUndo.AddComponent<ACO_DFUNC_Catapult>(go);
        Undo.RecordObject(neu, "ACO catapult");
        neu.SAVControl = old.SAVControl;
        neu.Dial_Funcon = old.Dial_Funcon;
        neu.Dial_Funcon_Array = old.Dial_Funcon_Array;
        neu.CatapultLock = old.CatapultLock;
        neu.MaxAttachAngle = old.MaxAttachAngle;
        neu.CatapultLayer = old.CatapultLayer;
        neu.GearFunc = old.GearFunc;
        neu.AnimTriggerLaunchName = old.AnimTriggerLaunchName;
        neu.AlignToCatapult = align;
        neu.FullThrottleLaunch = true;
        neu.AllowTriggerLaunch = true;
        if (old.AutoLaunch && old.AutoLaunch_FullThrottle) { neu.FullThrottleHoldTime = Mathf.Max(delay, 1f); }
        EditorUtility.SetDirty(neu);

        Undo.RecordObject(e, "ACO catapult");
        e.Dial_Functions_L = Swap(e.Dial_Functions_L, old, neu);
        e.Dial_Functions_R = Swap(e.Dial_Functions_R, old, neu);
        e.ExtensionUdonBehaviours = Swap(e.ExtensionUdonBehaviours, old, neu);
        PrefabUtility.RecordPrefabInstancePropertyModifications(e);
        EditorUtility.SetDirty(e);

        UdonSharpUndo.DestroyImmediate(old);
    }

    private static UdonSharpBehaviour[] Swap(UdonSharpBehaviour[] list, UdonSharpBehaviour from, UdonSharpBehaviour to)
    {
        if (list == null) { return null; }
        var copy = (UdonSharpBehaviour[])list.Clone();
        for (int i = 0; i < copy.Length; i++) { if (copy[i] == from) { copy[i] = to; } }
        return copy;
    }
}
