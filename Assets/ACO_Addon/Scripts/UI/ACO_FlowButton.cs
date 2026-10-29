using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ACO
{
    /// 게임 진행 버튼(동기화 없음). Action: 0 = JOIN/LEAVE(로비), 1 = READY(격납고, 미션 전), 2 = LAUNCH(격납고, 미션 중 재출격),
    /// 3 = 지형 리롤(로비, 미션 전, 명단에 있는 사람만).
    /// UI 버튼(Button.onClick → SendCustomEvent "Click")이 부른다. 콜라이더가 있으면 Interact로도 눌린다.
    /// 게임 진행이 SetState로 글자와 색을 바꾼다(지금 누를 수 없으면 회색 — 그래도 누르면 이유를 안내하고 거절음). 누르면 효과음(ACO_UiSound).
    /// Bake가 게임 진행의 버튼 목록(Buttons)에 넣고 Background·Sound를 채운다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_FlowButton : UdonSharpBehaviour
    {
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 게임 진행을 넣는다")]
        public ACO_GameFlow Flow;
        [Tooltip("0 = JOIN/LEAVE, 1 = READY, 2 = LAUNCH, 3 = 지형 리롤")]
        [Range(0, 3)] public int Action;
        [Tooltip("버튼 글자(프리팹 빌더가 넣는다)")]
        public TextMeshProUGUI Label;
        [Tooltip("버튼 배경(비면 Bake가 이 오브젝트의 Image를 넣는다)")]
        public Image Background;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;
        public Color NormalColor = new Color(0.08f, 0.3f, 0.14f, 1f);
        public Color DisabledColor = new Color(0.16f, 0.17f, 0.17f, 1f);
        public Color DisabledLabelColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        private Color _labelColor;
        private bool _labelColorSet;

        public void Click()
        {
            if (!Flow) { return; }
            bool ok = Flow.ButtonAction(Action);
            if (Sound)
            {
                int kind = !ok ? ACO_UiSound.KIND_DENY
                    : (Action == ACO_GameFlow.ACTION_REROLL ? ACO_UiSound.KIND_CLICK : ACO_UiSound.KIND_CONFIRM);
                Sound.PlayAt(kind, transform.position);
            }
        }

        /// 게임 진행이 부른다: 글자, 누를 수 있으면 원래 색·아니면 회색.
        public void SetState(string label, bool interactable)
        {
            if (Label)
            {
                if (!_labelColorSet) { _labelColor = Label.color; _labelColorSet = true; }
                if (Label.text != label) { Label.text = label; }
                Label.color = interactable ? _labelColor : DisabledLabelColor;
            }
            if (Background) { Background.color = interactable ? NormalColor : DisabledColor; }
        }

        public override void Interact() { Click(); }
    }
}
