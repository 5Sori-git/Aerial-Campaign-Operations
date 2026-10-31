using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 비(로컬 연출, 동기화 없음). 빗방울 메시(셰이더 ACO/Rain)가 셰이더 안에서 카메라 둘레에 끝없이 반복되고,
    /// 이 스크립트는 프레임마다 머리 위치로 따라가며 머리의 속도(= 탄 기체 속도)를 머티리얼에 넘긴다 — 빗줄기가 상대 바람 방향으로 늘어난다.
    ///  - Intensity(0~1)로 켜고 끈다(FadeTime 동안 서서히). 0이면 렌더러·소리를 끄고 아무것도 안 한다.
    ///  - 지붕 아래(ShelterBoxes — 격납고 등)와 MaxAltitude 위(구름 위)에서는 내리지 않는다.
    ///  - 날씨가 쓸 API: SetIntensity.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Rain : UdonSharpBehaviour
    {
        [Tooltip("빗방울 메시 렌더러(셰이더 ACO/Rain)")]
        public Renderer RainRenderer;
        [Range(0f, 1f)] public float Intensity = 0f;
        [Tooltip("켜고 끌 때 걸리는 시간(초)")]
        public float FadeTime = 3f;
        [Tooltip("이 고도(월드 Y) 위에서는 비가 안 온다(구름 위)")]
        public float MaxAltitude = 1500f;
        [Tooltip("지붕 아래 상자 — 이 트랜스폼의 단위 상자(위치·회전·크기 = 상자) 안에서는 비가 안 온다(격납고 등). 콜라이더가 아니라 물리에 영향 없음")]
        public Transform[] ShelterBoxes;

        [Header("소리 (선택)")]
        [Tooltip("빗소리 루프(2D 권장). 세기에 맞춰 볼륨이 바뀐다")]
        public AudioSource RainSound;
        public float SoundVolume = 0.6f;

        private Material _material;
        private float _current;
        private Vector3 _lastHead;
        private bool _hasLast;
        private Vector3 _velocity;

        void Start()
        {
            if (RainRenderer)
            {
                _material = RainRenderer.material;
                RainRenderer.enabled = false;
            }
            if (RainSound) { RainSound.volume = 0f; }
        }

        void Update()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || !_material) { return; }
            if (Intensity <= 0f && _current <= 0f) { _hasLast = false; return; }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            float target = Intensity;
            if (head.y > MaxAltitude || InShelter(head)) { target = 0f; }
            _current = Mathf.MoveTowards(_current, target, dt / Mathf.Max(FadeTime, 0.01f));

            bool on = _current > 0.001f;
            if (RainRenderer.enabled != on) { RainRenderer.enabled = on; }
            if (RainSound)
            {
                RainSound.volume = _current * SoundVolume;
                if (on && !RainSound.isPlaying) { RainSound.Play(); }
                else if (!on && RainSound.isPlaying) { RainSound.Stop(); }
            }
            if (!on) { _hasLast = false; return; }

            // 머리 속도(순간이동·리스폰은 무시)
            if (_hasLast)
            {
                Vector3 v = (head - _lastHead) / dt;
                if (v.sqrMagnitude < 1000000f) { _velocity = Vector3.Lerp(_velocity, v, 1f - Mathf.Exp(-dt * 10f)); }
            }
            _lastHead = head;
            _hasLast = true;

            transform.position = head;
            _material.SetVector("_CamVel", new Vector4(_velocity.x, _velocity.y, _velocity.z, 0f));
            _material.SetFloat("_Intensity", _current);
        }

        private bool InShelter(Vector3 p)
        {
            if (ShelterBoxes == null) { return false; }
            for (int i = 0; i < ShelterBoxes.Length; i++)
            {
                Transform t = ShelterBoxes[i];
                if (!t) { continue; }
                Vector3 l = t.InverseTransformPoint(p);
                if (Mathf.Abs(l.x) <= 0.5f && Mathf.Abs(l.y) <= 0.5f && Mathf.Abs(l.z) <= 0.5f) { return true; }
            }
            return false;
        }

        // ───────────── 날씨가 쓸 API ─────────────

        public void SetIntensity(float intensity)
        {
            Intensity = Mathf.Clamp01(intensity);
        }
    }
}
