using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 데칼 스튜디오 패널 버튼(동기화 없음). Action(ACO_DecalStudio.ACT_*) · Step(±1)을 스튜디오에 넘긴다. UI 버튼(onClick → "Click")이 부른다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_DecalButton : UdonSharpBehaviour
    {
        [Tooltip("같은 프리팹의 데칼 스튜디오. 비우면 부모에서 찾는다")]
        public ACO_DecalStudio Studio;
        [Tooltip("ACO_DecalStudio.ACT_*: 0 LOAD, 1 데칼 1/2, 2 크기, 3 회전, 4 뒤집기, 5 대칭, 6 지우기, 7 다른 사람 숨기기, 8 건 리셋")]
        public int Action;
        public int Step = 1;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;

        public void Click()
        {
            if (!Studio) { Studio = GetComponentInParent<ACO_DecalStudio>(); }
            if (!Studio) { return; }
            int kind = Studio.Press(Action, Step);
            if (Sound) { Sound.PlayAt(kind, transform.position); }
        }

        public override void Interact() { Click(); }
    }
}
