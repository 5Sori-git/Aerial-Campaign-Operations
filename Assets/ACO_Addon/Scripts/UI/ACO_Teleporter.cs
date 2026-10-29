using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 장소 이동 버튼(동기화 없음). 누른 사람을 Target 위치·방향으로 옮긴다(로비 ↔ 격납고 ↔ 항모 등, 언제든 자유롭게).
    /// 화면 암전(ACO_ScreenFader)이 있으면 어두워졌다 밝아지며 옮긴다.
    /// UI 버튼(Button.onClick → SendCustomEvent "Click") 또는 콜라이더(Interact)로 누른다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Teleporter : UdonSharpBehaviour
    {
        [Tooltip("도착 지점(파란 화살표 +Z가 바라볼 방향)")]
        public Transform Target;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 화면 암전을 넣는다. 비면 바로 옮긴다")]
        public ACO_ScreenFader Fader;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;

        public void Click()
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Target || !Utilities.IsValid(me)) { return; }
            if (Sound) { Sound.PlayAt(ACO_UiSound.KIND_CLICK, transform.position); }
            if (Fader) { Fader.FadeTeleport(Target.position, Target.rotation); }
            else { me.TeleportTo(Target.position, Target.rotation); }
        }

        public override void Interact() { Click(); }
    }
}
