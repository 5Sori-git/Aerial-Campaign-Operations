using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 작전판 스크롤 버튼(동기화 없음 — 각자 자기 화면만). Step: +1 = 더 오래된 기록(반 쪽), −1 = 최근 쪽, 0 = 맨 위(최신).
    /// 콜라이더가 있어 Interact(VR 레이저·데스크톱 클릭)로 눌린다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_LogScrollButton : UdonSharpBehaviour
    {
        [Tooltip("같은 작전판. 비우면 부모에서 찾는다")]
        public ACO_MissionLog Log;
        [Tooltip("+1 = 더 오래된 기록, −1 = 최근 쪽, 0 = 최신으로")]
        public int Step = 1;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;

        /// UI 버튼 OnClick에서 부른다.
        public void Click()
        {
            if (!Log) { Log = GetComponentInParent<ACO_MissionLog>(); }
            if (!Log) { return; }
            bool moved = Log.Scroll(Step);
            if (Sound) { Sound.PlayAt(moved ? ACO_UiSound.KIND_CLICK : ACO_UiSound.KIND_DENY, transform.position); }
        }
    }
}
