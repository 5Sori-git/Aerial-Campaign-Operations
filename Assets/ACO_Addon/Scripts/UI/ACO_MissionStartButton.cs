using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ACO
{
    /// 미션 페이즈 수 버튼(동기화 없음). 게임 진행(ACO_GameFlow)이 있으면 다음 미션의 페이즈 수만 고른다 — 미션은 명단 전원이 READY하면 시작된다.
    /// 게임 진행이 없으면 예전처럼 바로 PhaseCount 페이즈로 미션을 시작한다(진행 중이면 새 미션으로 다시 시작).
    /// 작전 콘솔 프리팹(ACO_MissionConsole)의 UI 버튼(Button.onClick → SendCustomEvent "Click")이 부른다.
    /// 콜라이더가 있는 오브젝트에 붙이면 Interact로도 눌린다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_MissionStartButton : UdonSharpBehaviour
    {
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 관리자를 자동으로 넣는다")]
        public ACO_MissionManager Manager;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 게임 진행을 자동으로 넣는다")]
        public ACO_GameFlow Flow;
        [Tooltip("이 버튼으로 고를 페이즈 수")]
        [Range(1, 5)] public int PhaseCount = 3;
        [Tooltip("버튼 배경(비면 Bake가 이 오브젝트의 Image를 넣는다). 게임 진행이 고른 수는 밝게, 바꿀 수 없을 때는 회색으로 칠한다")]
        public Image Background;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;
        public Color NormalColor = new Color(0.08f, 0.3f, 0.14f, 1f);
        public Color SelectedColor = new Color(0.25f, 0.75f, 0.35f, 1f);
        public Color DisabledColor = new Color(0.16f, 0.17f, 0.17f, 1f);
        public Color DisabledSelectedColor = new Color(0.3f, 0.36f, 0.32f, 1f);

        public void Click()
        {
            bool ok = true;
            if (Flow != null) { ok = Flow.RequestPhases(PhaseCount); }
            else if (Manager != null) { Manager.RequestStartMissionPhases(PhaseCount); }
            if (Sound) { Sound.PlayAt(ok ? ACO_UiSound.KIND_CLICK : ACO_UiSound.KIND_DENY, transform.position); }
        }

        /// 게임 진행이 부른다: 고른 페이즈 수는 밝게, 바꿀 수 없으면(미션 중·출격 카운트다운) 회색.
        public void SetState(bool interactable, bool selected)
        {
            if (!Background) { return; }
            if (interactable) { Background.color = selected ? SelectedColor : NormalColor; }
            else { Background.color = selected ? DisabledSelectedColor : DisabledColor; }
        }

        public override void Interact() { Click(); }
    }
}
