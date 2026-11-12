using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 함내 격실 구역(로컬 전용, 동기화 없음). Boxes는 ACO_Rain의 지붕 상자와 같은 단위 상자다.
    /// 안에서는 비가 오지 않고(ACO_Rain), 해수면 아래여도 물속 연출이 없고(ACO_Underwater),
    /// 벽 너머 표적 마커를 그리지 않고(ACO_TargetMarkers), 안전지대로 친다(ACO_PlayerProgress).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_InteriorZone : UdonSharpBehaviour
    {
        [Tooltip("ACO_InteriorBuilder가 격실마다 만든다. 위치, 회전, 크기가 곧 상자다")]
        public Transform[] Boxes;

        public bool Contains(Vector3 p)
        {
            if (Boxes == null) { return false; }
            for (int i = 0; i < Boxes.Length; i++)
            {
                Transform t = Boxes[i];
                if (!t) { continue; }
                Vector3 l = t.InverseTransformPoint(p);
                if (Mathf.Abs(l.x) <= 0.5f && Mathf.Abs(l.y) <= 0.5f && Mathf.Abs(l.z) <= 0.5f) { return true; }
            }
            return false;
        }
    }
}
