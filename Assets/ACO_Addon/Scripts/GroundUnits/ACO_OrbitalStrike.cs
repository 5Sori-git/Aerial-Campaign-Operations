using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// LUNA 페이즈의 궤도 폭격 연출(동기화 없음, 피해 없음). ACO_PhaseLuna의 동기화 값(폭격 시각·자리)과 서버 시각만으로 모든 화면에서 같게 그린다.
    /// 착탄 StrikeFallTime초 전부터 하늘 높은 곳에서 밝은 빛줄기가 곧게 내려오고, 착탄하면 대폭발(ACO_FortressBlast — 소리는 음속만큼 늦게)·지평선 섬광·하늘이 밝아진다.
    /// 유도 중 LUNA가 격추되면 폭격 자리가 도시에서 빗나간 자리로 바뀐다(내려오던 빛줄기도 그쪽으로). 늦게 들어와 이미 떨어진 폭격은 다시 그리지 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_OrbitalStrike : UdonSharpBehaviour
    {
        [Header("참조 (Bake가 넣는다)")]
        public ACO_PhaseLuna Phase;
        public ACO_CloudField Clouds;

        [Header("빛줄기")]
        [Tooltip("내려오는 빛줄기(월드 좌표 LineRenderer, 점 2개)")]
        public LineRenderer Streak;
        [Tooltip("빛줄기가 시작하는 높이(m) — 땅에서")]
        public float StreakAltitude = 40000f;
        [Tooltip("빛줄기 꼬리 길이(m)")]
        public float StreakLength = 9000f;
        public float StreakWidth = 60f;
        [Tooltip("내려오는 동안 도는 소리(2D, 선택)")]
        public AudioSource FallSound;

        [Header("착탄")]
        [Tooltip("대폭발(공중 요새와 같은 연출)")]
        public ACO_FortressBlast Blast;
        [Tooltip("도시에 떨어질 때 대폭발 크기 배율")]
        public float CityBlastScale = 1.8f;
        [Tooltip("빗나갔을 때 대폭발 크기 배율")]
        public float MissBlastScale = 1.4f;
        public ParticleSystem HorizonFlash;
        public ParticleSystem HorizonGlow;
        [Tooltip("하늘(구름)이 밝아지는 세기와 시간")]
        public float SkyFlash = 1.2f;
        public float SkyFlashTime = 3f;
        [Tooltip("착탄 자리 땅 높이를 재는 층")]
        public LayerMask GroundLayers = 2049;

        private int _doneStamp;
        private float _skyLeft;
        private Vector3 _flashPoint;
        private bool _streakOn;

        void Start() { SetStreak(false); }

        void Update()
        {
            UpdateSkyFlash(Time.deltaTime);
            if (Phase == null || !Phase.IsLive())
            {
                SetStreak(false);
                return;
            }
            int stamp = Phase.GetStartMs();
            if (_doneStamp == stamp) { SetStreak(false); return; }
            int now = Networking.GetServerTimeInMilliseconds();
            int strikeMs = Phase.GetStrikeMs();
            int left = strikeMs - now;
            if (left > Mathf.RoundToInt(Phase.StrikeFallTime * 1000f))
            {
                SetStreak(false);
                return;
            }
            Vector3 point = Ground(Phase.GetStrikePoint());
            if (left > 0)
            {
                // 내려오는 중: 머리가 StreakAltitude에서 땅까지 곧게
                float k = Mathf.Clamp01(left / Mathf.Max(Phase.StrikeFallTime * 1000f, 1f));
                float head = StreakAltitude * k * k;
                SetStreak(true);
                if (Streak)
                {
                    Streak.SetPosition(0, point + Vector3.up * (head + StreakLength));
                    Streak.SetPosition(1, point + Vector3.up * head);
                }
                return;
            }
            // 착탄(한 번). 늦게 들어와 오래 지난 착탄은 그리지 않는다
            _doneStamp = stamp;
            SetStreak(false);
            if (now - strikeMs > 5000) { return; }
            bool city = Phase.GetDownMs() == 0;
            if (Blast) { Blast.Play(point, false, city ? CityBlastScale : MissBlastScale); }
            var emit = new ParticleSystem.EmitParams();
            emit.position = point + Vector3.up * 800f;
            emit.applyShapeToPosition = false;
            if (HorizonFlash) { HorizonFlash.Emit(emit, 1); }
            if (HorizonGlow) { HorizonGlow.Emit(emit, 1); }
            _flashPoint = point;
            _skyLeft = SkyFlashTime;
        }

        /// 그 자리의 땅 높이(못 재면 그대로).
        private Vector3 Ground(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, 6000f, p.z), Vector3.down, out hit, 12000f, GroundLayers.value, QueryTriggerInteraction.Ignore)) { return hit.point; }
            return p;
        }

        private void SetStreak(bool on)
        {
            if (_streakOn == on) { return; }
            _streakOn = on;
            if (Streak) { Streak.enabled = on; Streak.widthMultiplier = StreakWidth; }
            if (FallSound)
            {
                if (on && !FallSound.isPlaying) { FallSound.Play(); }
                else if (!on && FallSound.isPlaying) { FallSound.Stop(); }
            }
        }

        private void UpdateSkyFlash(float dt)
        {
            if (_skyLeft <= 0f) { return; }
            _skyLeft -= dt;
            if (Clouds) { Clouds.SetFlash(_skyLeft > 0f ? SkyFlash * _skyLeft / Mathf.Max(SkyFlashTime, 0.01f) : 0f, _flashPoint); }
        }
    }
}
