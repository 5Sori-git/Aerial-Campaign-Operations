using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 화면 암전(로컬 전용, 씬에 하나). 머리를 감싸는 검은 구(항상 맨 위에 그리는 머티리얼)의 알파를 올렸다 내린다.
    /// FadeTeleport: 어두워짐(FadeOutTime) → 순간이동 → 잠깐 유지(HoldTime) → 밝아짐(FadeInTime). Fade: 순간이동 없이 같은 연출(출격 탑승용).
    /// 어두워지는 도중에 또 부르면 목적지만 바꾼다. ACO → Create Screen Fader가 만들고, Bake가 순간이동 버튼·게임 진행에 연결한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_ScreenFader : UdonSharpBehaviour
    {
        [Tooltip("머리를 감싸는 구(ACO/MarkerOverlay 머티리얼, 검정)")]
        public Renderer FadeRenderer;
        [Tooltip("어두워지는 시간(초)")]
        public float FadeOutTime = 0.3f;
        [Tooltip("완전히 어두운 채로 있는 시간(초) — 순간이동·탑승이 이 사이에 끝난다")]
        public float HoldTime = 0.4f;
        [Tooltip("밝아지는 시간(초)")]
        public float FadeInTime = 0.5f;

        private const int PHASE_IDLE = 0;
        private const int PHASE_OUT = 1;
        private const int PHASE_HOLD = 2;
        private const int PHASE_IN = 3;

        private Material _mat;
        private int _phase = PHASE_IDLE;
        private float _t;
        private bool _teleport;
        private Vector3 _pos;
        private Quaternion _rot;

        void Start()
        {
            if (!FadeRenderer) { return; }
            _mat = FadeRenderer.material;
            SetAlpha(0f);
            FadeRenderer.enabled = false;
        }

        /// 어두워진 뒤 그 자리로 옮긴다.
        public void FadeTeleport(Vector3 position, Quaternion rotation)
        {
            _teleport = true;
            _pos = position;
            _rot = rotation;
            Begin();
        }

        /// 순간이동 없이 어두워졌다 밝아진다.
        public void Fade()
        {
            Begin();
        }

        private void Begin()
        {
            if (!FadeRenderer)
            {
                DoTeleport();
                return;
            }
            // 어두워지는 중이면 그대로 이어 간다(목적지만 바뀜). 이미 어두우면 바로 옮기고 어두운 시간을 다시 센다.
            // 밝아지는 중이면 지금 밝기에서 다시 어두워진다.
            if (_phase == PHASE_OUT) { return; }
            if (_phase == PHASE_HOLD)
            {
                DoTeleport();
                _t = 0f;
                return;
            }
            float alpha = _phase == PHASE_IN ? 1f - _t / Mathf.Max(FadeInTime, 0.01f) : 0f;
            _phase = PHASE_OUT;
            _t = alpha * Mathf.Max(FadeOutTime, 0.01f);
            FadeRenderer.enabled = true;
        }

        void Update()
        {
            if (_phase == PHASE_IDLE) { return; }
            _t += Time.deltaTime;
            if (_phase == PHASE_OUT)
            {
                SetAlpha(Mathf.Clamp01(_t / Mathf.Max(FadeOutTime, 0.01f)));
                if (_t < FadeOutTime) { return; }
                SetAlpha(1f);
                DoTeleport();
                _phase = PHASE_HOLD;
                _t = 0f;
            }
            else if (_phase == PHASE_HOLD)
            {
                if (_t < HoldTime) { return; }
                _phase = PHASE_IN;
                _t = 0f;
            }
            else
            {
                SetAlpha(1f - Mathf.Clamp01(_t / Mathf.Max(FadeInTime, 0.01f)));
                if (_t < FadeInTime) { return; }
                SetAlpha(0f);
                _phase = PHASE_IDLE;
                FadeRenderer.enabled = false;
            }
        }

        /// 머리를 따라간다(머리 위치가 확정된 뒤).
        public override void PostLateUpdate()
        {
            if (_phase == PHASE_IDLE || !FadeRenderer) { return; }
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }
            FadeRenderer.transform.position = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        }

        private void DoTeleport()
        {
            if (!_teleport) { return; }
            _teleport = false;
            VRCPlayerApi me = Networking.LocalPlayer;
            if (Utilities.IsValid(me)) { me.TeleportTo(_pos, _rot); }
        }

        private void SetAlpha(float a)
        {
            if (_mat) { _mat.SetColor("_Color", new Color(0f, 0f, 0f, a)); }
        }
    }
}
