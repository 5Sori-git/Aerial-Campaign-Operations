using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 산탄 탄두(SCM — 미사일 프리팹의 자식, 로컬, 동기화 없음): ACO_Missile이 표적 BurstDistance 앞에서 Burst를 부르면 자탄을 원뿔로 뿌린다.
    ///  - 자탄은 오브젝트가 아니라 파티클(Bomblets — 곧게 날아간다, 탄속 BombletSpeed는 파티클 시작 속도와 같게)이다.
    ///  - 떨어지는 자리: 원뿔 안에 고르게 놓은 Count개 방향(황금각 나선 — 모든 화면이 같다)으로 지면까지 레이캐스트. 비스듬히 쏘면 지면에 긴 타원으로 깔린다.
    ///  - 닿는 시각(거리 / 탄속)마다 그 자리에서 폭발(Blasts 파티클, 소리는 SoundEvery발마다)하고 ACO_Missile.SubmunitionHit으로 범위 피해 — 쏜 사람 화면에서만 들어간다.
    ///  - 미사일은 터진 자리에 멈춰 있으므로(풀로 돌아가는 건 GetDuration 뒤) 자식 파티클이 끝까지 재생된다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Dispenser : UdonSharpBehaviour
    {
        public ACO_Missile Missile;
        [Tooltip("자탄(월드 공간, 콘 축 +Z — 빌더가 만든다). 시작 속도가 BombletSpeed와 같아야 폭발 시각이 맞는다")]
        public ParticleSystem Bomblets;
        [Tooltip("자탄 폭발(월드 공간 — 터질 때마다 자리로 옮겨 방출한다)")]
        public ParticleSystem Blasts;
        public AudioSource[] BlastSounds;
        [Tooltip("지면 레이캐스트 레이어(지면 + 물 — 바다는 트리거라 트리거도 맞힌다)")]
        public LayerMask GroundLayers = 2049 | 16;
        [Tooltip("자탄 수(최대 32) — 자탄마다 범위 피해 한 번(무장 Damage·SplashRadius, 강화 반영)")]
        public int Count = 16;
        [Tooltip("표적까지 이 거리(m)에서 터져 뿌린다")]
        public float BurstDistance = 420f;
        [Tooltip("원뿔 반각(도) — 클수록 넓게 뿌린다")]
        public float ConeAngle = 12f;
        public float BombletSpeed = 260f;
        [Tooltip("자탄이 날아가는 최대 거리(m) — 이보다 멀면 지면에 안 닿은 것으로 친다")]
        public float MaxReach = 1500f;
        [Tooltip("자탄 폭발 하나에 방출할 파티클 수")]
        public int BlastParticles = 6;
        [Tooltip("폭발음은 이 발 수마다 한 번")]
        public int SoundEvery = 3;
        [Tooltip("마지막 폭발 뒤 파티클이 사라질 때까지 기다리는 시간(초)")]
        public float LingerTime = 3f;

        private const int MAX_BOMBLETS = 32;
        private const float GOLDEN_ANGLE = 2.3999631f;

        private Vector3[] _pos = new Vector3[MAX_BOMBLETS];
        private float[] _time = new float[MAX_BOMBLETS];
        private bool[] _done = new bool[MAX_BOMBLETS];
        private int _count;
        private int _left;
        private int _sound;
        private float _duration;

        /// 터지는 순간: 원뿔을 target 쪽으로 돌려 자탄을 뿌리고, 떨어질 자리·시각을 정한다.
        public void Burst(Vector3 origin, Vector3 direction)
        {
            Quaternion aim = Quaternion.LookRotation(direction);
            int n = Mathf.Clamp(Count, 1, MAX_BOMBLETS);
            if (Bomblets)
            {
                Bomblets.transform.SetPositionAndRotation(origin, aim);
                Bomblets.Emit(n);
            }

            float now = Time.time;
            float speed = Mathf.Max(BombletSpeed, 1f);
            float spread = Mathf.Tan(ConeAngle * Mathf.Deg2Rad);
            float last = 0f;
            _count = 0;
            _sound = 0;
            for (int i = 0; i < n; i++)
            {
                float r = Mathf.Sqrt((i + 0.5f) / n) * spread;
                float a = i * GOLDEN_ANGLE;
                Vector3 local = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 1f);
                RaycastHit hit;
                if (!Physics.Raycast(origin, aim * local.normalized, out hit, MaxReach, GroundLayers, QueryTriggerInteraction.Collide)) { continue; }
                float t = hit.distance / speed;
                _pos[_count] = hit.point;
                _time[_count] = now + t;
                _done[_count] = false;
                _count++;
                if (t > last) { last = t; }
            }
            _left = _count;
            _duration = last + Mathf.Max(LingerTime, 0f);
            if (_left > 0) { SendCustomEventDelayedFrames(nameof(_Tick), 1); }
        }

        /// 터진 뒤 미사일이 풀로 돌아가기까지 기다릴 시간(초) — 이 시간이 지나기 전에 비활성화되면 자탄이 사라진다.
        public float GetDuration() { return _duration; }

        public void _Tick()
        {
            float now = Time.time;
            for (int i = 0; i < _count; i++)
            {
                if (_done[i] || now < _time[i]) { continue; }
                _done[i] = true;
                _left--;
                Land(_pos[i], i);
            }
            if (_left > 0) { SendCustomEventDelayedFrames(nameof(_Tick), 1); }
        }

        private void Land(Vector3 p, int index)
        {
            if (Blasts)
            {
                // 반구 축(+Z)이 위를 보게(탄체는 기울어진 채 터진다)
                Blasts.transform.SetPositionAndRotation(p, Quaternion.Euler(-90f, 0f, 0f));
                Blasts.Emit(BlastParticles);
            }
            if (BlastSounds != null && BlastSounds.Length > 0 && index % Mathf.Max(SoundEvery, 1) == 0)
            {
                AudioSource s = BlastSounds[_sound % BlastSounds.Length];
                _sound++;
                if (s)
                {
                    s.transform.position = p;
                    s.pitch = Random.Range(0.9f, 1.15f);
                    s.Play();
                }
            }
            if (Missile) { Missile.SubmunitionHit(p); }
        }
    }
}
