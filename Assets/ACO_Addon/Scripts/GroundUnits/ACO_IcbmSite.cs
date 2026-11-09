using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 레이저 에이스 페이즈의 사일로·탄도미사일 연출(동기화 없음). ACO_PhaseLaserAce의 동기화 값(단계·시각·사일로 자리·착탄 방향)과 서버 시각만으로
    /// 모든 화면에서 같게 그린다 — 사일로 문이 열리고, 미사일이 연기 기둥을 남기며 솟아 대기권 밖으로 사라지고, 착탄 직전 지평선 너머로 떨어진다.
    /// 에이스가 격추되면 날던 미사일이 하늘에서 자폭한다(섬광·불덩이, 하늘이 밝아진다). 착탄하면 지평선 너머 아군 기지에서 큰 섬광과 늦게 오는 폭음.
    /// 페이즈가 아니면 모두 숨긴다. 피해·판정은 없다(판정은 모듈).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_IcbmSite : UdonSharpBehaviour
    {
        [Header("참조 (Bake가 넣는다)")]
        public ACO_PhaseLaserAce Phase;
        public ACO_CloudField Clouds;

        [Header("사일로")]
        [Tooltip("사일로 전체(받침·문). 페이즈 동안만 켠다")]
        public Transform SiloRoot;
        [Tooltip("문 경첩(로컬 Z축으로 젖혀 연다) — 왼쪽은 +, 오른쪽은 - 방향")]
        public Transform DoorL;
        public Transform DoorR;
        public float DoorOpenAngle = 110f;
        [Tooltip("사일로 안 미사일 바닥 깊이(m) — 받침 윗면 아래")]
        public float SiloDepth = 21f;

        [Header("미사일")]
        [Tooltip("미사일(원점 = 바닥, 앞 = 위로 솟는 방향 +Y)")]
        public Transform Missile;
        [Tooltip("날고 있을 때만 켠다(엔진 불꽃)")]
        public GameObject Flame;
        [Tooltip("미사일 연기 궤적(월드, 미사일 밖에 두고 매 프레임 미사일 꼬리로 옮긴다 — 미사일을 숨겨도 남은 연기가 그대로)")]
        public ParticleSystem Trail;
        [Tooltip("발사 때 사일로에서 뿜는 연기")]
        public ParticleSystem LaunchSmoke;
        public AudioSource LaunchSound;
        [Tooltip("오르는 가속도(m/s²)")]
        public float ClimbAccel = 32f;
        [Tooltip("착탄 방향으로 기우는 정도(높이 대비 수평 이동 비율)")]
        public float Pitchover = 0.35f;
        [Tooltip("이 높이(m)를 넘으면 보이지 않게 한다(대기권 밖)")]
        public float VisibleAltitude = 30000f;
        [Tooltip("착탄 몇 초 전부터 재진입 궤적을 그리는가")]
        public float ReentryTime = 20f;
        public float ReentryAltitude = 30000f;

        [Header("자폭 (에이스 격추)")]
        public ParticleSystem SkyBurst;
        public AudioSource SkyBurstSound;
        [Tooltip("미사일이 보이지 않는 높이일 때 자폭을 그리는 높이(m)")]
        public float BurstAltitude = 18000f;

        [Header("착탄 (지평선 너머)")]
        public ParticleSystem HorizonFlash;
        public ParticleSystem HorizonGlow;
        public AudioSource HorizonSound;
        [Tooltip("착탄 섬광을 그리는 거리(m) — 이보다 먼 착탄 자리는 이 거리로 당겨 그린다(시야 끝 안)")]
        public float HorizonDrawDistance = 45000f;
        public float HorizonHeight = 1200f;
        public float HorizonSoundDelay = 4f;
        public float SkyFlash = 1f;
        public float SkyFlashTime = 2f;

        private bool _shown;
        private bool _burstDone;
        private bool _impactDone;
        private bool _launchFxDone;
        private float _skyLeft;
        private Vector3 _flashPoint;
        private Quaternion _doorLClosed;
        private Quaternion _doorRClosed;
        private bool _init;
        private Vector3 _lastMissilePos;
        private bool _trailOn;

        void Start() { Init(); }

        private void Init()
        {
            if (_init) { return; }
            _init = true;
            if (DoorL) { _doorLClosed = DoorL.localRotation; }
            if (DoorR) { _doorRClosed = DoorR.localRotation; }
            Show(false);
        }

        void Update()
        {
            if (!_init) { Init(); }
            UpdateSkyFlash(Time.deltaTime);
            if (Phase == null || !Phase.IsLive())
            {
                if (_shown) { Show(false); }
                return;
            }
            if (!_shown)
            {
                Show(true);
                _burstDone = false;
                _impactDone = false;
                _launchFxDone = false;
            }
            Vector3 silo = Phase.GetSiloPosition();
            Vector3 dir = Phase.GetImpactDirection();
            if (SiloRoot) { SiloRoot.SetPositionAndRotation(silo, Quaternion.LookRotation(dir, Vector3.up)); }

            int now = Networking.GetServerTimeInMilliseconds();
            bool icbm = Phase.IsLaunchStarted();
            float doors = icbm ? Mathf.Clamp01((now - Phase.GetStageMs()) / Mathf.Max(Phase.TransitionTime * 800f, 1f)) : 0f;
            SetDoors(doors);

            Vector3 basePos = silo - Vector3.up * SiloDepth;
            if (!icbm || now - Phase.GetLaunchMs() < 0)
            {
                // 사일로 안에서 기다린다
                PlaceMissile(basePos, Vector3.up, true, false);
                return;
            }
            bool down = Phase.IsAceDown();
            int impactMs = Phase.GetImpactMs();
            if (!_launchFxDone)
            {
                _launchFxDone = true;
                // 늦게 들어왔거나 오래 지난 발사는 연기·소리를 다시 내지 않는다
                if (now - Phase.GetLaunchMs() < 3000)
                {
                    if (LaunchSmoke) { LaunchSmoke.transform.position = silo; LaunchSmoke.Play(); }
                    if (LaunchSound) { LaunchSound.transform.position = silo; LaunchSound.Play(); }
                }
            }
            if (down)
            {
                if (!_burstDone && now - impactMs < 0)
                {
                    _burstDone = true;
                    Vector3 p = _lastMissilePos;
                    if (p.y - silo.y > VisibleAltitude || p == Vector3.zero) { p = silo + dir * (BurstAltitude * Pitchover) + Vector3.up * BurstAltitude; }
                    if (SkyBurst) { SkyBurst.transform.position = p; SkyBurst.Play(); }
                    if (SkyBurstSound) { SendCustomEventDelayedSeconds(nameof(_PlayBurstSound), 2.5f); }
                    _flashPoint = p;
                    _skyLeft = SkyFlashTime;
                }
                PlaceMissile(basePos, Vector3.up, false, false);
                return;
            }
            if (now - impactMs >= 0)
            {
                PlaceMissile(basePos, Vector3.up, false, false);
                if (!_impactDone)
                {
                    _impactDone = true;
                    if (now - impactMs < 5000) { HorizonImpact(silo, dir); }
                }
                return;
            }
            float left = (impactMs - now) * 0.001f;
            if (left < ReentryTime)
            {
                // 재진입: 착탄 자리(시야 끝 안으로 당겨 그린다) 위에서 곧게 떨어진다
                Vector3 ground = silo + dir * Mathf.Min(Phase.ImpactDistance, HorizonDrawDistance);
                float h = ReentryAltitude * (left / Mathf.Max(ReentryTime, 0.1f));
                PlaceMissile(ground + Vector3.up * h + dir * (h * 0.3f), (-Vector3.up - dir * 0.3f).normalized, true, true);
                return;
            }
            float t = (now - Phase.GetLaunchMs()) * 0.001f;
            float alt = 0.5f * ClimbAccel * t * t;
            Vector3 pos = basePos + Vector3.up * alt + dir * (alt * Pitchover * Mathf.Clamp01(t / 25f));
            float t2 = t + 0.2f;
            float alt2 = 0.5f * ClimbAccel * t2 * t2;
            Vector3 next = basePos + Vector3.up * alt2 + dir * (alt2 * Pitchover * Mathf.Clamp01(t2 / 25f));
            Vector3 v = next - pos;
            bool visible = alt <= VisibleAltitude;
            PlaceMissile(pos, v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.up, visible, visible);
        }

        private void PlaceMissile(Vector3 pos, Vector3 up, bool visible, bool flying)
        {
            if (!Missile) { return; }
            if (Missile.gameObject.activeSelf != visible) { Missile.gameObject.SetActive(visible); }
            if (visible)
            {
                Vector3 side = Mathf.Abs(Vector3.Dot(up, Vector3.forward)) > 0.95f ? Vector3.right : Vector3.forward;
                Missile.SetPositionAndRotation(pos, Quaternion.LookRotation(side, up));
                _lastMissilePos = pos;
                if (Trail) { Trail.transform.position = pos; }
            }
            if (Flame && Flame.activeSelf != flying) { Flame.SetActive(flying); }
            SetTrail(flying);
        }

        private void SetTrail(bool on)
        {
            if (!Trail || _trailOn == on) { return; }
            _trailOn = on;
            ParticleSystem.EmissionModule em = Trail.emission;
            em.enabled = on;
            if (on && !Trail.isPlaying) { Trail.Play(); }
        }

        private void SetDoors(float open)
        {
            float k = Mathf.SmoothStep(0f, 1f, open);
            if (DoorL) { DoorL.localRotation = _doorLClosed * Quaternion.Euler(0f, 0f, DoorOpenAngle * k); }
            if (DoorR) { DoorR.localRotation = _doorRClosed * Quaternion.Euler(0f, 0f, -DoorOpenAngle * k); }
        }

        private void HorizonImpact(Vector3 silo, Vector3 dir)
        {
            Vector3 p = silo + dir * Mathf.Min(Phase.ImpactDistance, HorizonDrawDistance) + Vector3.up * HorizonHeight;
            var emit = new ParticleSystem.EmitParams();
            emit.position = p;
            emit.applyShapeToPosition = false;
            if (HorizonFlash) { HorizonFlash.Emit(emit, 1); }
            if (HorizonGlow) { HorizonGlow.Emit(emit, 1); }
            _flashPoint = p;
            _skyLeft = SkyFlashTime;
            if (HorizonSound) { SendCustomEventDelayedSeconds(nameof(_PlayHorizonSound), HorizonSoundDelay); }
        }

        public void _PlayHorizonSound() { if (HorizonSound) { HorizonSound.Play(); } }
        public void _PlayBurstSound() { if (SkyBurstSound) { SkyBurstSound.Play(); } }

        private void UpdateSkyFlash(float dt)
        {
            if (_skyLeft <= 0f) { return; }
            _skyLeft -= dt;
            if (Clouds) { Clouds.SetFlash(_skyLeft > 0f ? SkyFlash * _skyLeft / Mathf.Max(SkyFlashTime, 0.01f) : 0f, _flashPoint); }
        }

        private void Show(bool on)
        {
            _shown = on;
            if (SiloRoot && SiloRoot.gameObject.activeSelf != on) { SiloRoot.gameObject.SetActive(on); }
            if (!on)
            {
                if (Missile && Missile.gameObject.activeSelf) { Missile.gameObject.SetActive(false); }
                if (Flame && Flame.activeSelf) { Flame.SetActive(false); }
                SetTrail(false);
                _lastMissilePos = Vector3.zero;
            }
        }
    }
}
