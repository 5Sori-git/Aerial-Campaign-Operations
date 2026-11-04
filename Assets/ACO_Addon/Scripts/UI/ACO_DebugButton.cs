using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 디버그 패널 버튼(동기화 없음). Action(ACO_DebugPanel.ACT_*) · Index(모듈 번호) · Amount(± 값)를 패널에 넘긴다.
    /// UI 버튼(Button.onClick → SendCustomEvent "Click")이 부른다. 콜라이더가 있으면 Interact로도 눌린다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_DebugButton : UdonSharpBehaviour
    {
        [Tooltip("같은 프리팹의 디버그 패널. 비우면 부모에서 찾는다")]
        public ACO_DebugPanel Panel;
        [Tooltip("ACO_DebugPanel.ACT_*: 0 미션 가중치, 1 에이스 확률, 2 베테랑 확률, 3 보너스 페이즈 확률, 4 내 크레딧")]
        public int Action;
        [Tooltip("미션 가중치의 모듈 번호(관리자 Modules 순서)")]
        public int Index;
        [Tooltip("더할 값(빼려면 음수): 가중치 ±0.5, 확률 ±0.05, 크레딧 ±10000 등")]
        public float Amount = 1f;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;

        public void Click()
        {
            if (!Panel) { Panel = GetComponentInParent<ACO_DebugPanel>(); }
            if (!Panel) { return; }
            int kind = Panel.Press(Action, Index, Amount);
            if (Sound) { Sound.PlayAt(kind, transform.position); }
        }

        public override void Interact() { Click(); }
    }
}
