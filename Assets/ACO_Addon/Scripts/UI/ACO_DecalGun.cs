using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 데칼 건(격납고 — VRC Pickup, 로컬): 들고 있는 동안 총구(Muzzle) 앞으로 레이캐스트해 겨누는 선(Beam)을 맞은 곳까지 늘이고,
    /// 방아쇠(Use)를 누르는 순간 샘플 기체(스튜디오 SampleRoot 아래 콜라이더)의 맞은 자리에 고른 데칼을 바로 찍고 저장한다(건의 위쪽 = 데칼 위쪽).
    /// 겨누는 동안 데칼이 따라다니지 않는다. Gun 방향으로 잡혀 집으면 총구가 앞을 본다. 놓은 자리에 그대로 있고, 패널의 RESET GUN으로 받침대로 돌아간다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_DecalGun : UdonSharpBehaviour
    {
        public ACO_DecalStudio Studio;
        [Tooltip("이 오브젝트의 VRC Pickup — RESET GUN이 손에서 놓게 한다")]
        public VRC.SDK3.Components.VRCPickup Pickup;
        [Tooltip("겨누는 방향(앞 = forward). 비우면 이 오브젝트")]
        public Transform Muzzle;
        public float Range = 12f;
        [Tooltip("겨누는 레이어 — 플레이어(9 Player, 10 PlayerLocal)·18 MirrorReflection은 뺀다(플레이어 콜라이더는 Udon에서 null로 보여 예외가 난다)")]
        public LayerMask AimLayers = ~((1 << 9) | (1 << 10) | (1 << 18));
        [Tooltip("겨누는 선(선택) — 샘플을 맞히면 맞은 곳까지 늘인다")]
        public LineRenderer Beam;
        public AudioSource StampSound;

        private bool _held;
        private Vector3 _homePos;
        private Quaternion _homeRot;

        void Start()
        {
            _homePos = transform.position;
            _homeRot = transform.rotation;
            if (!Muzzle) { Muzzle = transform; }
            if (Beam) { Beam.enabled = false; }
        }

        public override void OnPickup() { _held = true; if (Beam) { Beam.enabled = true; } }

        public override void OnDrop()
        {
            _held = false;
            if (Beam) { Beam.enabled = false; }
        }

        /// 받침대로 돌려놓는다(패널 RESET GUN) — 들고 있으면 먼저 놓는다.
        public void ResetToHome()
        {
            if (_held && Pickup) { Pickup.Drop(); }
            transform.SetPositionAndRotation(_homePos, _homeRot);
        }

        /// 누르는 순간 그 자리에 찍는다.
        public override void OnPickupUseDown()
        {
            if (Studio == null || Studio.SampleRoot == null) { return; }
            RaycastHit h;
            if (!AimHit(out h)) { return; }
            if (Studio.PlaceAt(h.point, h.normal, Muzzle.up) && StampSound && StampSound.clip) { StampSound.PlayOneShot(StampSound.clip); }
        }

        void Update()
        {
            if (!_held || Beam == null || Studio == null || Studio.SampleRoot == null) { return; }
            RaycastHit h;
            bool ok = AimHit(out h);
            Vector3 o = Muzzle.position;
            Beam.SetPosition(0, o);
            Beam.SetPosition(1, ok ? h.point : o + Muzzle.forward * Range);
        }

        /// 총구 앞 레이캐스트가 샘플 기체에 맞았는가.
        private bool AimHit(out RaycastHit h)
        {
            return Physics.Raycast(Muzzle.position, Muzzle.forward, out h, Range, AimLayers, QueryTriggerInteraction.Ignore)
                && Utilities.IsValid(h.collider) && h.collider.transform.IsChildOf(Studio.SampleRoot);
        }
    }
}
