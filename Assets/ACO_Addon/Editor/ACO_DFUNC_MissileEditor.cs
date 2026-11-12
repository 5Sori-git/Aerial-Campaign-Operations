using UdonSharpEditor;
using UnityEditor;
using ACO;

/// 무장 모듈 인스펙터. ECM 모듈은 슬롯·재장전·HUD·입력을 재사용하려고 같은 스크립트를 쓴다 —
/// 미사일 항목을 보여 주면 헷갈리므로 ECM에 쓰는 값만 보여 주고, 나머지는 접어 둔다. 일반 무장은 그대로다.
[CustomEditor(typeof(ACO_DFUNC_Missile))]
public class ACO_DFUNC_MissileEditor : Editor
{
    private static readonly string[] EcmFields = { "EcmMode", "EcmDuration", "SlotReloadTime", "EcmHub" };
    private bool _showUnused;

    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) { return; }
        var m = (ACO_DFUNC_Missile)target;
        if (!m.EcmMode)
        {
            DrawDefaultInspector();
            return;
        }
        serializedObject.Update();
        EditorGUILayout.HelpBox("ECM 모듈(재밍 포드)입니다. 미사일을 쏘지 않습니다.\n반경은 ACO_RadarContacts의 EcmRange에서 정합니다.", MessageType.Info);
        foreach (string f in EcmFields) { EditorGUILayout.PropertyField(serializedObject.FindProperty(f)); }
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.Space(4f);
        _showUnused = EditorGUILayout.Foldout(_showUnused, "미사일용 설정 (ECM에서는 대부분 쓰지 않음)", true);
        if (_showUnused) { DrawDefaultInspector(); }
    }
}
