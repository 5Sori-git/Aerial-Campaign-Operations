using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 무유도 폭탄/로켓(로컬 재현, 동기화 없음). 발사는 ACO_DFUNC_Missile이 한다(장전 슬롯·강화·장전 막대를 그대로 쓴다).
    ///  - 폭탄: 모기체 속도 벡터를 이어받아 중력으로 떨어진다(Thrust 0).
    ///  - 로켓: Acceleration으로 MaxSpeed까지 가속하며 곧게 난다(GravityScale로 살짝 처지게 할 수 있다).
    /// 매 프레임 이동 구간을 선으로 검사해 지형/유닛 콜라이더에 닿으면 터지고, SplashRadius 안의 SaccTarget에 범위 피해를 준다
    /// (중심 Damage → 가장자리 Damage × MinSplashFraction, 거리는 콜라이더 표면까지). 호위 대상(아군, ACO_EscortUnit)은 피해를 받지 않는다.
    /// SAV 폭탄은 범위 피해를 리지드바디가 있는 대상에만 주는데 NPC 지상 유닛엔 리지드바디가 없어서 직격만 들어간다 — 그래서 따로 만든다.
    /// 피해는 쏜 사람만 준다(격추 점수 = 데미지를 보낸 사람).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Ordnance : UdonSharpBehaviour
    {
        [Header("비행")]
        [Tooltip("발사 속력(m/s) — 모기체 속도에 더해진다. 폭탄은 0(투하), 로켓은 사출 속력")]
        public float LaunchSpeed = 0f;
        [Tooltip("추진 가속(m/s²). 0이면 폭탄(중력만)")]
        public float Acceleration = 0f;
        [Tooltip("최고 속력(m/s). 추진은 이 속력까지만")]
        public float MaxSpeed = 600f;
        [Tooltip("중력 배수. 폭탄 1, 로켓 0~0.3")]
        public float GravityScale = 1f;
        [Tooltip("공기 저항(1/s). 속도가 초당 이 비율만큼 준다. 폭탄이 너무 멀리 날아가면 올린다")]
        public float Drag = 0.02f;
        public float MaxLifetime = 30f;
        [Tooltip("발사 후 이 시간(초) 동안은 충돌하지 않는다(내 기체에 닿아 터지지 않게)")]
        public float ArmTime = 0.3f;

        [Header("명중 / 범위 피해 — Damage·SplashRadius·MinSplashFraction은 쏘는 무장(ACO_DFUNC_Missile) 값으로 덮어쓴다")]
        [Tooltip("닿으면 터지는 레이어(지형·물·유닛 히트박스). 기본 = Default, Water, Environment, Walkthrough(17)")]
        public LayerMask HitLayers = 133137;
        [Tooltip("범위 피해를 받을 콜라이더 레이어. 기본 = Walkthrough(17, 유닛 히트박스)")]
        public LayerMask DamageLayers = 1 << 17;
        [Tooltip("중심 피해. 플레이어가 쏘면 무장(ACO_DFUNC_Missile)의 Damage로 덮어쓴다 — 여기 값은 쓰이지 않는다")]
        public float Damage = 120f;
        [Tooltip("범위 피해 반경(m, 지름 아님). 플레이어가 쏘면 무장(ACO_DFUNC_Missile)의 SplashRadius(× 폭발 범위 강화)로 덮어쓴다 — 여기 값은 쓰이지 않는다")]
        public float SplashRadius = 25f;
        [Tooltip("반경 가장자리 피해 비율(0~1). 중심에서 가장자리로 갈수록 선형으로 줄어든다. 플레이어가 쏘면 무장 값으로 덮어쓴다")]
        [Range(0f, 1f)] public float MinSplashFraction = 0.25f;
        [Tooltip("SAV 무기 타입(킬 피드). 4 = 폭탄, 5 = 로켓")]
        public byte WeaponType = 4;
        [Tooltip("테스트용: 쏜 사람 화면에서 터질 때 무엇에 닿았는지(오브젝트·레이어·지면 위 높이)와 범위 피해 결과(반경·맞은 유닛·피해)를 로그로 남긴다")]
        public bool DebugHitLog = false;

        [Header("연출")]
        public GameObject ExplosionObject;
        [Tooltip("폭발 후 풀에 돌려놓기까지의 시간(초). 후연을 쓰면 후연 파티클 Start Lifetime 최댓값 이상으로 둔다")]
        public float ExplosionDuration = 3f;
        [Tooltip("폭발음. 하나를 무작위로 골라 음높이를 조금씩 바꿔 재생한다")]
        public AudioSource[] ExplosionSounds;
        [Tooltip("비행 중에만 켜둘 오브젝트(모델 등)")]
        public GameObject[] DisableOnExplode;
        [Tooltip("후연 파티클(로켓). 발사 시 방출을 켜고 폭발 시 방출만 끈다. Simulation Space는 World, DisableOnExplode에 넣지 않는다")]
        public ParticleSystem[] TrailParticles;

        /// 발사하는 쪽이 넣는다. SaccTarget이 공격자를 찾을 때 이 변수 이름(EntityControl)을 읽으므로 바꾸면 안 된다.
        [System.NonSerialized] public SaccEntity EntityControl;
        [System.NonSerialized] public Vector3 LaunchVelocity;   // 모기체 속도 벡터(월드)
        [System.NonSerialized] public Transform PoolParent;
        [System.NonSerialized] public bool IsShooter;

        private const int MAX_SPLASH_HITS = 32;
        private const int MAX_SPLASH_COLLIDERS = 256;   // 큰 반경이면 함선·지상 그룹 콜라이더가 많다 — 모자라면 일부 유닛이 빠진다
        private Collider[] _hits = new Collider[MAX_SPLASH_COLLIDERS];
        private SaccTarget[] _damaged = new SaccTarget[MAX_SPLASH_HITS];

        /// 비행 계산 간격(초). 프레임 속도와 상관없이 이 간격으로 계산한다 — CCIP 예측(ACO_DFUNC_Missile)이 같은 간격의 궤적을 수식으로 정확히 구한다.
        public const float FLIGHT_STEP = 0.02f;

        private Vector3 _velocity;
        private Vector3 _simPos;   // 계산상 위치(FLIGHT_STEP 경계). 화면 위치는 여기서 남은 시간만큼 앞으로 그린다
        private float _accum;
        private float _life;
        private float _age;
        private bool _flying;
        private bool _exploded;

        /// 발사하는 쪽이 SetActive(true) 직후 부른다.
        public void EnableWeapon()
        {
            _velocity = LaunchVelocity + transform.forward * LaunchSpeed;
            _simPos = transform.position;
            _accum = 0f;
            _life = MaxLifetime;
            _age = 0f;
            _flying = true;
            _exploded = false;
            if (ExplosionObject) { ExplosionObject.SetActive(false); }
            SetFlightVisuals(true);
            SetTrailEmission(true, true);
        }

        void Update()
        {
            if (!_flying || _exploded) { return; }
            Step(Time.deltaTime);
        }

        /// 한 프레임: FLIGHT_STEP 간격으로 나눠 계산(추진 → 중력 → 공기 저항 → 이동)하고, 프레임 동안 움직인 구간을 한 번에 충돌 검사한다.
        /// CCIP 예측(ACO_DFUNC_Missile)이 같은 간격·같은 순서의 궤적을 쓴다.
        private void Step(float dt)
        {
            _age += dt;
            _life -= dt;
            if (_life <= 0f)
            {
                if (DebugHitLog && IsShooter) { Debug.Log("[ACO] " + gameObject.name + ": 수명(" + MaxLifetime + "초)이 다해 공중에서 터졌습니다. 고도 " + transform.position.y.ToString("0") + "m"); }
                Explode(transform.position);
                return;
            }
            Vector3 from = _simPos;
            Vector3 to = from;
            Vector3 fwd = transform.forward;
            _accum += dt;
            int guard = 0;
            while (_accum >= FLIGHT_STEP && guard < 30)
            {
                _accum -= FLIGHT_STEP;
                guard++;
                if (Acceleration > 0f && _velocity.magnitude < MaxSpeed) { _velocity += fwd * (Acceleration * FLIGHT_STEP); }
                _velocity += Physics.gravity * (GravityScale * FLIGHT_STEP);
                if (Drag > 0f) { _velocity *= Mathf.Max(1f - Drag * FLIGHT_STEP, 0f); }
                to += _velocity * FLIGHT_STEP;
            }
            if (guard > 0 && _age >= ArmTime)
            {
                RaycastHit hit;
                if (Physics.Linecast(from, to, out hit, HitLayers, QueryTriggerInteraction.Ignore) && !IsOwnVehicle(hit.collider))
                {
                    if (DebugHitLog && IsShooter) { LogHit(hit); }
                    Explode(hit.point);
                    return;
                }
            }
            _simPos = to;
            // 계산은 50Hz라 그대로 그리면 90fps에서 띄엄띄엄 움직인다 — 남은 시간만큼 속도로 앞으로 그린다.
            transform.position = _simPos + _velocity * _accum;
            // 기수는 속도 방향을 따른다(로켓은 추진 방향이 기수라 거의 그대로).
            if (_velocity.sqrMagnitude > 0.01f) { transform.rotation = Quaternion.LookRotation(_velocity); }
        }

        /// 닿은 콜라이더와 그 지점의 지면 위 높이(아래로 지형 레이어 검사)를 남긴다 — 공중 폭발 원인 찾기.
        private void LogHit(RaycastHit hit)
        {
            Collider c = hit.collider;
            Transform root = c.transform.root;
            float above = -1f;
            RaycastHit ground;
            if (Physics.Raycast(hit.point + Vector3.down * 0.5f, Vector3.down, out ground, 5000f, 2065, QueryTriggerInteraction.Ignore)) { above = ground.distance + 0.5f; }
            Debug.Log("[ACO] " + gameObject.name + " 폭발: '" + c.gameObject.name + "'(최상위 '" + root.name + "') 레이어 " + c.gameObject.layer
                + ", 지점 " + "(" + hit.point.x.ToString("0") + ", " + hit.point.y.ToString("0") + ", " + hit.point.z.ToString("0") + ")" + ", 아래 지면까지 " + (above < 0f ? "없음" : above.ToString("0") + "m"), c);
        }

        private bool IsOwnVehicle(Collider c)
        {
            return EntityControl != null && c != null && c.transform.IsChildOf(EntityControl.transform);
        }

        private void Explode(Vector3 point)
        {
            if (_exploded) { return; }
            _exploded = true;
            _flying = false;
            transform.position = point;

            if (IsShooter) { ApplySplash(point); }

            SetFlightVisuals(false);
            if (ExplosionObject) { ExplosionObject.SetActive(true); }
            PlayExplosionSound();
            SetTrailEmission(false, false);
            SendCustomEventDelayedSeconds(nameof(MoveBackToPool), ExplosionDuration);
        }

        /// 반경 안의 SaccTarget마다 한 번씩: 피해 = Damage × (1 → MinSplashFraction, 콜라이더 표면까지의 거리에 따라 선형).
        /// 한 유닛에 콜라이더가 여럿이면 가장 가까운 것 하나로 센다(같은 SaccTarget은 한 번만).
        private void ApplySplash(Vector3 center)
        {
            if (SplashRadius <= 0f) { return; }
            int n = Physics.OverlapSphereNonAlloc(center, SplashRadius, _hits, DamageLayers);
            if (n >= MAX_SPLASH_COLLIDERS) { Debug.LogWarning("[ACO] " + gameObject.name + ": 범위 안 콜라이더가 " + MAX_SPLASH_COLLIDERS + "개를 넘어 일부 유닛이 빠졌을 수 있습니다(반경 " + SplashRadius.ToString("0") + "m)"); }
            int damagedCount = 0;
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i];
                if (!c) { continue; }
                SaccTarget target = c.GetComponentInParent<SaccTarget>();
                if (!target || target.Health <= 0f) { continue; }
                if (IsOwnVehicle(c)) { continue; }
                // 아군(호위 대상)은 피해를 받지 않는다.
                ACO_EscortUnit escort = c.GetComponentInParent<ACO_EscortUnit>();
                if (escort != null && !escort.Hostile) { continue; }

                // 같은 유닛은 가장 가까운 콜라이더로 한 번만 — 이미 준 대상이면 건너뛴다(가까운 것부터 오지 않으므로 최소 거리를 먼저 구한다).
                bool done = false;
                for (int k = 0; k < damagedCount; k++) { if (_damaged[k] == target) { done = true; break; } }
                if (done) { continue; }

                float best = float.MaxValue;
                for (int j = i; j < n; j++)
                {
                    Collider cj = _hits[j];
                    if (!cj || cj.GetComponentInParent<SaccTarget>() != target) { continue; }
                    float d = Vector3.Distance(center, cj.bounds.ClosestPoint(center));
                    if (d < best) { best = d; }
                }
                float t = Mathf.Clamp01(best / SplashRadius);
                float dmg = Damage * Mathf.Lerp(1f, MinSplashFraction, t);
                target.WeaponDamageTarget(dmg, gameObject, WeaponType);
                if (DebugHitLog) { Debug.Log("[ACO]   범위 피해: '" + target.gameObject.name + "' 거리 " + best.ToString("0") + "m, 피해 " + dmg.ToString("0") + " (체력 " + target.Health.ToString("0") + ")", target); }
                if (damagedCount < MAX_SPLASH_HITS) { _damaged[damagedCount] = target; damagedCount++; }
            }
            if (DebugHitLog) { Debug.Log("[ACO] " + gameObject.name + " 범위 피해: 반경 " + SplashRadius.ToString("0") + "m, 중심 피해 " + Damage.ToString("0") + ", 콜라이더 " + n + "개, 피해 준 유닛 " + damagedCount + "기"); }
        }

        private void SetFlightVisuals(bool on)
        {
            if (DisableOnExplode == null) { return; }
            for (int i = 0; i < DisableOnExplode.Length; i++) { if (DisableOnExplode[i]) { DisableOnExplode[i].SetActive(on); } }
        }

        private void SetTrailEmission(bool on, bool clear)
        {
            if (TrailParticles == null) { return; }
            for (int i = 0; i < TrailParticles.Length; i++)
            {
                ParticleSystem ps = TrailParticles[i];
                if (!ps) { continue; }
                if (clear) { ps.Clear(); }
                ParticleSystem.EmissionModule em = ps.emission;
                em.enabled = on;
                if (on && !ps.isPlaying) { ps.Play(); }
            }
        }

        private void PlayExplosionSound()
        {
            if (ExplosionSounds == null || ExplosionSounds.Length == 0) { return; }
            AudioSource sound = ExplosionSounds[Random.Range(0, ExplosionSounds.Length)];
            if (!sound) { return; }
            sound.pitch = Random.Range(0.94f, 1.2f);
            sound.Play();
        }

        public void MoveBackToPool()
        {
            if (ExplosionObject) { ExplosionObject.SetActive(false); }
            SetFlightVisuals(true);
            SetTrailEmission(false, false);
            _flying = false;
            _exploded = false;
            if (PoolParent) { transform.SetParent(PoolParent); }
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            gameObject.SetActive(false);
        }
    }
}
