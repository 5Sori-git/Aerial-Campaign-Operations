using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 물 연출(동기화 없음 — 화면마다 자기 화면의 위치로 그린다).
    ///  - MODE_SPRAY(저공 물보라 — 시스키밍 미사일, 저공비행 기체): 부모(미사일·기체)가 수면 위 MaxHeight 안을 MinSpeed 이상으로 지나면
    ///    바로 아래 수면에 물보라(Particles — 월드 공간, 위로 튀는 원뿔)를 뿌린다. CheckInterval마다 아래로 짧게 Raycast(물 + 지형) — 물이 먼저 맞을 때만.
    ///    세기 = 높이(수면에 가까울수록) × 속력(FullSpeed까지). 방출기는 매 프레임 수면 위 같은 자리(부모 바로 아래)로 옮기고 위를 향하게 세운다.
    ///  - MODE_WAKE(함선 항적): 파티클은 거리 비례 방출(Rate over Distance, 월드 공간)이라 움직일 때만 생긴다. 이 스크립트는 순간이동(풀 배치·부활·대기 자리 이동)할 때
    ///    옛 자리에서 새 자리까지 그어지는 줄을 지운다(한 프레임에 JumpDistance보다 멀리 움직이면 Clear).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_WaterFx : UdonSharpBehaviour
    {
        public const int MODE_SPRAY = 0;
        public const int MODE_WAKE = 1;

        [Tooltip("0 = 저공 물보라(미사일·기체 자식), 1 = 함선 항적(함선 자식)")]
        public int Mode = MODE_SPRAY;
        [Tooltip("파티클(월드 공간). 물보라는 수면 자리로 옮겨 뿌리고, 항적은 거리 비례 방출이라 그대로 둔다")]
        public ParticleSystem[] Particles;

        [Header("물보라")]
        [Tooltip("수면 위 이 높이(m) 안에서만 뿌린다. 수면에 가까울수록 세다")]
        public float MaxHeight = 25f;
        [Tooltip("이 속력(m/s)보다 느리면 뿌리지 않는다")]
        public float MinSpeed = 40f;
        [Tooltip("이 속력(m/s)에서 가장 세다")]
        public float FullSpeed = 250f;
        [Tooltip("가장 셀 때 초당 파티클 수")]
        public float MaxRate = 150f;
        [Tooltip("수면 확인 간격(초)")]
        public float CheckInterval = 0.15f;
        [Tooltip("수면 레이어(바다 콜라이더, 기본 Water 4)")]
        public LayerMask WaterLayers = 1 << 4;
        [Tooltip("지형 레이어 — 물보다 먼저 맞으면(육지 위) 뿌리지 않는다")]
        public LayerMask GroundLayers = 2049;

        [Header("항적")]
        [Tooltip("한 프레임에 이보다 멀리(m) 움직이면 순간이동으로 보고 남은 항적을 지운다")]
        public float JumpDistance = 60f;

        private Vector3 _lastPos;
        private bool _hasLast;
        private float _timer;
        private bool _overWater;
        private float _surfaceY;
        private float _speed;
        private float _rate = -1f;
        private Quaternion _upRot;

        void OnEnable()
        {
            _hasLast = false;
            _timer = 0f;
            _speed = 0f;
            _overWater = false;
            _upRot = Quaternion.Euler(-90f, 0f, 0f);   // 원뿔 축(+Z)이 위로
            if (Mode == MODE_WAKE) { ClearAll(); }
            else { SetRate(0f); }
        }

        void Update()
        {
            Vector3 p = transform.position;
            float dt = Time.deltaTime;

            if (Mode == MODE_WAKE)
            {
                if (_hasLast && (p - _lastPos).sqrMagnitude > JumpDistance * JumpDistance) { ClearAll(); }
                _lastPos = p;
                _hasLast = true;
                return;
            }

            if (_hasLast && dt > 0f) { _speed = Mathf.Lerp(_speed, (p - _lastPos).magnitude / dt, 0.2f); }
            _lastPos = p;
            _hasLast = true;

            _timer -= dt;
            if (_timer <= 0f)
            {
                _timer = CheckInterval;
                Probe(p);
            }

            float rate = 0f;
            if (_overWater && _speed >= MinSpeed)
            {
                float near = Mathf.Clamp01(1f - (p.y - _surfaceY) / Mathf.Max(MaxHeight, 0.1f));
                float fast = Mathf.Clamp01((_speed - MinSpeed) / Mathf.Max(FullSpeed - MinSpeed, 1f));
                rate = MaxRate * near * (0.3f + 0.7f * fast);
                if (rate > 0f && Particles != null)
                {
                    Vector3 at = new Vector3(p.x, _surfaceY, p.z);
                    for (int i = 0; i < Particles.Length; i++)
                    {
                        if (Particles[i]) { Particles[i].transform.SetPositionAndRotation(at, _upRot); }
                    }
                }
            }
            SetRate(rate);
        }

        /// 아래로 짧게: 물이 지형보다 먼저 맞으면 수면 높이를 잡는다. 아무것도 없거나 지형이 먼저면 물 위가 아니다.
        /// 물은 일반 콜라이더와 트리거를 모두 본다(SAV 바다는 기체가 뜨도록 트리거 상자다 — 물 레이어엔 바다만 있다), 지형은 트리거를 뺀다.
        private void Probe(Vector3 p)
        {
            Vector3 origin = p + Vector3.up * 2f;
            float reach = MaxHeight + 4f;
            RaycastHit water;
            RaycastHit ground;
            bool hitWater = Physics.Raycast(origin, Vector3.down, out water, reach, WaterLayers.value, QueryTriggerInteraction.Collide);
            bool hitGround = Physics.Raycast(origin, Vector3.down, out ground, reach, GroundLayers.value & ~WaterLayers.value, QueryTriggerInteraction.Ignore);
            _overWater = hitWater && (!hitGround || water.distance <= ground.distance);
            if (_overWater) { _surfaceY = water.point.y; }
        }

        private void SetRate(float rate)
        {
            // 0과 0이 아닌 것 사이를 오갈 때, 아니면 눈에 띄게 바뀔 때만 쓴다
            if (_rate >= 0f && Mathf.Abs(rate - _rate) < 2f && (rate > 0f) == (_rate > 0f)) { return; }
            _rate = rate;
            if (Particles == null) { return; }
            for (int i = 0; i < Particles.Length; i++)
            {
                ParticleSystem ps = Particles[i];
                if (!ps) { continue; }
                ParticleSystem.EmissionModule em = ps.emission;
                em.rateOverTimeMultiplier = rate;
                if (rate > 0f && !ps.isPlaying) { ps.Play(); }
            }
        }

        private void ClearAll()
        {
            if (Particles == null) { return; }
            for (int i = 0; i < Particles.Length; i++) { if (Particles[i]) { Particles[i].Clear(); } }
        }
    }
}
