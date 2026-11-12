using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 공중 요새 대폭발(연출 전용 — 피해 없음). 낙하 중 동체 연쇄 폭발(PlaySecondary)과 충돌 폭발(Play)을 재생한다.
    /// 각 클라이언트가 자기 화면의 충돌 자리에서 재생하므로 동기화가 없다.
    /// 프리팹에선 요새의 자식이다. 처음 터질 때 부모에서 떼어 내 요새가 풀로 돌아가도 연기가 남는다(파티클은 월드 좌표).
    /// 소리는 음속만큼 늦게 도착한다 — 멀리서 보면 섬광이 먼저, 폭음이 나중이다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_FortressBlast : UdonSharpBehaviour
    {
        [Header("땅")]
        public ParticleSystem Fire;
        public ParticleSystem Debris;
        public ParticleSystem Smoke;
        [Header("바다")]
        public ParticleSystem WaterColumn;
        public ParticleSystem Mist;
        [Header("공통")]
        public ParticleSystem Flash;
        public ParticleSystem Shock;
        public ParticleSystem Secondary;
        [Tooltip("번쩍이는 조명(그림자 없음)")]
        public Light FlashLight;
        public float FlashIntensity = 8f;
        [Tooltip("조명이 꺼지는 시간(초)")]
        public float FlashTime = 1.2f;

        [Header("소리")]
        public AudioSource Boom;
        public AudioSource SecondarySound;
        [Tooltip("음속(m/s). 0이면 늦추지 않는다")]
        public float SoundSpeed = 340f;

        [Tooltip("낙하 중 연쇄 폭발 한 번에 내보내는 수")]
        public int SecondaryCount = 10;

        private float _flashLeft;

        void Update()
        {
            if (_flashLeft <= 0f) { return; }
            _flashLeft -= Time.deltaTime;
            if (FlashLight == null) { return; }
            FlashLight.intensity = FlashIntensity * Mathf.Clamp01(_flashLeft / Mathf.Max(FlashTime, 0.01f));
            if (_flashLeft <= 0f) { FlashLight.enabled = false; }
        }

        /// 충돌 폭발. water면 물기둥, 아니면 화염구·파편·연기 기둥. scale = 크기 배율.
        /// 터지는 양과 순서는 각 파티클의 Burst가 정한다(ACO_FxBuilder.StyleBlast). Fire의 자식(불티·연기 줄기·먼지 고리)도 함께 돈다.
        public void Play(Vector3 point, bool water, float scale)
        {
            transform.SetParent(null);
            transform.SetPositionAndRotation(point, Quaternion.identity);
            transform.localScale = Vector3.one * scale;
            if (Flash) { Flash.Play(); }
            if (Shock) { Shock.Play(); }
            if (water)
            {
                if (WaterColumn) { WaterColumn.Play(); }
                if (Mist) { Mist.Play(); }
            }
            else
            {
                if (Fire) { Fire.Play(); }
                if (Debris) { Debris.Play(); }
                if (Smoke) { Smoke.Play(); }
            }
            if (FlashLight)
            {
                FlashLight.enabled = true;
                FlashLight.intensity = FlashIntensity;
                _flashLeft = FlashTime;
            }
            if (Boom)
            {
                float delay = SoundDelay(point);
                if (delay > 0.02f) { SendCustomEventDelayedSeconds(nameof(_PlayBoom), delay); }
                else { Boom.Play(); }
            }
        }

        public void _PlayBoom()
        {
            if (Boom) { Boom.Play(); }
        }

        /// 낙하 중 동체 연쇄 폭발.
        public void PlaySecondary(Vector3 point)
        {
            if (Secondary)
            {
                var emit = new ParticleSystem.EmitParams();
                emit.position = point;
                emit.applyShapeToPosition = true;
                Secondary.Emit(emit, SecondaryCount);
            }
            if (SecondarySound)
            {
                SecondarySound.transform.position = point;
                SecondarySound.Play();
            }
        }

        private float SoundDelay(Vector3 point)
        {
            if (SoundSpeed <= 0f) { return 0f; }
            VRCPlayerApi player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player)) { return 0f; }
            return Vector3.Distance(player.GetPosition(), point) / SoundSpeed;
        }
    }
}
