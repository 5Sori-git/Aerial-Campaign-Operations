using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 관전 패널 버튼(동기화 없음). Action(ACO_SpectatorCam.ACT_*: 0 켜기/끄기, 1 대상, 2 컷) · Step(±1 넘기기)을 패널에 넘긴다.
    /// UI 버튼(Button.onClick → SendCustomEvent "Click")이 부른다. 콜라이더가 있으면 Interact로도 눌린다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_SpectatorButton : UdonSharpBehaviour
    {
        [Tooltip("같은 프리팹의 관전 패널. 비우면 부모에서 찾는다")]
        public ACO_SpectatorCam Panel;
        [Tooltip("ACO_SpectatorCam.ACT_*: 0 켜기/끄기, 1 대상 넘기기, 2 컷 넘기기")]
        public int Action;
        [Tooltip("넘기는 방향(+1 다음, -1 이전)")]
        public int Step = 1;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;

        public void Click()
        {
            if (!Panel) { Panel = GetComponentInParent<ACO_SpectatorCam>(); }
            if (!Panel) { return; }
            int kind = Panel.Press(Action, Step);
            if (Sound) { Sound.PlayAt(kind, transform.position); }
        }

        public override void Interact() { Click(); }
    }
}
