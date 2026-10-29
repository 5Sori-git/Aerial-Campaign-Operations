using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 패널 버튼 효과음(로컬 전용, 씬에 하나 — Bake가 만들고 버튼들에 연결한다). 누른 버튼 자리에서 3D로 한 번 낸다.
    ///  CLICK = 받아들인 일반 조작(탭·페이지·값 조정·선택), CONFIRM = 결정(READY·출격·구매·장착·참가), DENY = 거절(조건이 안 맞아 안내 메시지만 뜬 경우).
    /// 클립은 Generated의 사인파 WAV(바꾸려면 클립만 바꾼다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_UiSound : UdonSharpBehaviour
    {
        public const int KIND_CLICK = 0;
        public const int KIND_CONFIRM = 1;
        public const int KIND_DENY = 2;

        [Tooltip("3D AudioSource. 소리를 낼 때 누른 버튼 자리로 옮긴다")]
        public AudioSource Source;
        public AudioClip ClickClip;
        public AudioClip ConfirmClip;
        public AudioClip DenyClip;
        [Range(0f, 1f)] public float Volume = 0.7f;

        public void PlayAt(int kind, Vector3 position)
        {
            if (!Source) { return; }
            AudioClip clip = ClickClip;
            if (kind == KIND_CONFIRM) { clip = ConfirmClip; }
            else if (kind == KIND_DENY) { clip = DenyClip; }
            if (!clip) { return; }
            Source.transform.position = position;
            Source.PlayOneShot(clip, Volume);
        }
    }
}
