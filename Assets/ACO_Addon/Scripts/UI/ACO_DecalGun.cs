using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 데칼 건(격납고 — VRC Pickup, 로컬): 들고 있는 동안 총구(Muzzle) 앞으로 레이캐스트해 겨누는 선(Beam)을 맞은 곳까지 늘이고,
    /// 방아쇠(Use)를 누르는 순간 샘플 기체(스튜디오 SampleRoot 아래 콜라이더)의 맞은 자리에 고른 데칼을 바로 찍고 저장한다(건의 위쪽 = 데칼 위쪽).
    /// 겨누는 동안 데칼이 따라다니지 않는다. Gun 방향으로 잡혀 집으면 총구가 앞을 본다. 놓은 자리에 그대로 있고, 패널의 RESET GUN으로 받침대로 돌아간다.
    /// 모양은 스프레이건(6-149). 방아쇠를 누를 때마다 노즐에서 도료 안개(Mist — 간단한 파티클)와 분사 소리가 나고, 샘플에 맞았을 때만 데칼이 찍힌다.
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
        [Tooltip("방아쇠마다 노즐에서 뿜는 도료 안개(선택 — 월드 공간 파티클, Emit으로 MistCount개)")]
        public ParticleSystem Mist;
        public int MistCount = 10;

        [Header("홀로그램 화살표 (들고 있지 않을 때 건 위 — 튜토리얼 화살표와 같은 모양, 생성 코드가 넣는다)")]
        [Tooltip("화살표 루트(건의 자식이 아니다 — 건이 어떻게 놓여도 위에 곧게 선다)")]
        public Transform Marker;
        public Transform[] MarkerRings;
        [Tooltip("건 가운데 위 이 높이(m)에 화살 끝")]
        public float MarkerHeight = 0.22f;
        public float MarkerSpin = 90f;
        public float MarkerBob = 0.025f;
        public float MarkerBobPeriod = 1.6f;
        public float MarkerPulse = 0.25f;
        public float MarkerPulsePeriod = 1.2f;
        [Tooltip("이 거리(m) 안에 있을 때만 화살표를 움직인다(멀면 멈춘 채 보인다)")]
        public float MarkerNear = 8f;

        private bool _held;
        private float _markerSpinNow;
        private float _nearCheck;
        private bool _near;
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

        /// 누르는 순간: 안개·분사 소리(늘), 샘플에 맞았으면 그 자리에 찍는다.
        public override void OnPickupUseDown()
        {
            if (Mist) { Mist.Emit(MistCount); }
            if (StampSound && StampSound.clip) { StampSound.PlayOneShot(StampSound.clip); }
            if (Studio == null || Studio.SampleRoot == null) { return; }
            RaycastHit h;
            if (!AimHit(out h)) { return; }
            Studio.PlaceAt(h.point, h.normal, Muzzle.up);
        }

        void Update()
        {
            UpdateMarker();
            if (!_held || Beam == null || Studio == null || Studio.SampleRoot == null) { return; }
            RaycastHit h;
            bool ok = AimHit(out h);
            Vector3 o = Muzzle.position;
            Beam.SetPosition(0, o);
            Beam.SetPosition(1, ok ? h.point : o + Muzzle.forward * Range);
        }

        /// 홀로그램 화살표: 내가 들고 있지 않을 때 건 위에(건이 어디 어떻게 놓여도 월드 위쪽으로 곧게) 돌고 흔들리며 링이 맥박친다. 가까울 때만 움직인다.
        private void UpdateMarker()
        {
            if (Marker == null) { return; }
            bool show = !_held;
            if (Marker.gameObject.activeSelf != show) { Marker.gameObject.SetActive(show); }
            if (!show) { return; }
            _nearCheck -= Time.deltaTime;
            if (_nearCheck <= 0f)
            {
                _nearCheck = 0.5f;
                VRCPlayerApi lp = Networking.LocalPlayer;
                _near = Utilities.IsValid(lp) && (lp.GetPosition() - transform.position).sqrMagnitude <= MarkerNear * MarkerNear;
            }
            float t = Time.time;
            float bob = _near ? Mathf.Sin(t * 2f * Mathf.PI / Mathf.Max(MarkerBobPeriod, 0.1f)) * MarkerBob : 0f;
            Marker.position = transform.position + Vector3.up * (MarkerHeight + bob);
            if (!_near) { return; }
            _markerSpinNow = (_markerSpinNow + MarkerSpin * Time.deltaTime) % 360f;
            Marker.rotation = Quaternion.Euler(0f, _markerSpinNow, 0f);
            if (MarkerRings == null) { return; }
            for (int i = 0; i < MarkerRings.Length; i++)
            {
                if (MarkerRings[i] == null) { continue; }
                float k = 0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / Mathf.Max(MarkerPulsePeriod, 0.1f) - i * 1.2f);
                float sc = 1f + MarkerPulse * k;
                Vector3 baseScale = i == 0 ? Vector3.one : new Vector3(0.75f, 1f, 0.75f);
                MarkerRings[i].localScale = new Vector3(baseScale.x * sc, 1f, baseScale.z * sc);
            }
        }

        /// 총구 앞 레이캐스트가 샘플 기체에 맞았는가.
        private bool AimHit(out RaycastHit h)
        {
            return Physics.Raycast(Muzzle.position, Muzzle.forward, out h, Range, AimLayers, QueryTriggerInteraction.Ignore)
                && Utilities.IsValid(h.collider) && h.collider.transform.IsChildOf(Studio.SampleRoot);
        }
    }
}
