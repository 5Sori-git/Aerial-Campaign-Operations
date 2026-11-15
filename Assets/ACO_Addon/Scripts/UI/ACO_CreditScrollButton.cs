using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 크레딧 보드 ▲▼ 버튼(동기화 없음 — 로컬). Step: +1 = 아래로(반 화면), −1 = 위로. 6-228.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_CreditScrollButton : UdonSharpBehaviour
    {
        [Tooltip("같은 크레딧 보드. 비우면 부모에서 찾는다")]
        public ACO_CreditBoard Board;
        [Tooltip("+1 = 아래로, −1 = 위로")]
        public int Step = 1;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;

        /// UI 버튼 OnClick에서 부른다.
        public void Click()
        {
            if (!Board) { Board = GetComponentInParent<ACO_CreditBoard>(); }
            if (!Board) { return; }
            bool moved = Board.ScrollStep(Step);
            if (Sound) { Sound.PlayAt(moved ? ACO_UiSound.KIND_CLICK : ACO_UiSound.KIND_DENY, transform.position); }
        }
    }
}
