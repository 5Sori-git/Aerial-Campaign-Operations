using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// HUD 설정 패널 버튼(동기화 없음). Direction −1/+1 = 항목(ACO_HudSettings.ITEM_*) 한 단계 줄이기/늘리기(크로스헤어는 켜기/끄기),
    /// 0 = 전체 기본값으로. UI 버튼(Button.onClick → SendCustomEvent "Click")이 부른다. 콜라이더가 있으면 Interact로도 눌린다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_HudSettingButton : UdonSharpBehaviour
    {
        [Tooltip("같은 프리팹의 HUD 설정. 비우면 부모에서 찾는다")]
        public ACO_HudSettings Settings;
        [Tooltip("항목 번호(ACO_HudSettings.ITEM_*)")]
        public int Item;
        [Tooltip("−1 = 줄이기, +1 = 늘리기, 0 = 전체 RESET")]
        public int Direction = 1;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;

        public void Click()
        {
            if (!Settings) { Settings = GetComponentInParent<ACO_HudSettings>(); }
            if (!Settings) { return; }
            int kind = ACO_UiSound.KIND_CONFIRM;
            if (Direction == 0) { Settings.ResetAll(); }
            else { kind = Settings.Adjust(Item, Direction) ? ACO_UiSound.KIND_CLICK : ACO_UiSound.KIND_DENY; }
            if (Sound) { Sound.PlayAt(kind, transform.position); }
        }

        public override void Interact() { Click(); }
    }
}
