using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 무유도 폭탄/로켓(로컬 재현, 동기화 없음). 발사는 ACO_DFUNC_Missile이 한다(장전 슬롯·강화·장전 막대를 그대로 쓴다).
    ///  - 폭탄: 모기체 속도 벡터를 이어받아 중력으로 떨어진다(Thrust 0).
    ///  - 로켓: Acceleration으로 MaxSpeed까지 가속하며 곧게 난다(GravityScale로 살짝 처지게 할 수 있다).
    ///  - 근접 신관(대공 로켓 FLAK): 쏘는 무장이 ProximityRadius를 넣으면 적 항공기 락온 마커(레이어 29) 중심이 이동 구간에서 그 반경 안에 들어올 때
    ///    가장 가까이 지나는 점에서 터진다(범위 피해로 맞힌다). 모든 화면이 각자 재현해 같이 터지고, 피해는 쏜 사람 화면만.
    ///  - 유도(유도 로켓 LGR·유도 폭탄 LGB): 쏘는 무장이 락온한 표적(GuideTarget)을 넣으면 속도 방향을 표적 쪽으로 돌려 날아간다(GuideTurnRate).
    ///    표적 번호는 발사 이벤트에 실려 모든 화면이 같은 표적을 쫓는다. 폭탄·로켓이라 CIWS 요격은 받지 않는다.
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
        // 함선 부위도 범위 피해를 그대로 준다(무장 FullPartSplash). 끄면 직접 닿지 않은 부위는 ACO_Warship.PartSplashScale만큼만 받는다.
        [System.NonSerialized] public bool FullPartSplash;
        [Tooltip("테스트용: 쏜 사람 화면에서 터질 때 무엇에 닿았는지(오브젝트·레이어·지면 위 높이)와 범위 피해 결과(반경·맞은 유닛·피해)를 로그로 남긴다")]
        public bool DebugHitLog = false;

        [Header("연출")]
        public GameObject ExplosionObject;
        [Tooltip("폭발 후 풀에 돌려놓기까지의 시간(초). 후연을 쓰면 후연 파티클 Start Lifetime 최댓값 이상으로 둔다")]
        public float ExplosionDuration = 3f;
        [Header("폭발 범위 표시 (선택)")]
        [Tooltip("터질 때 이 오브젝트의 크기를 범위 피해 반경(SplashRadius, 강화 반영)에 맞춘다 — 반투명 구 메시 등. ExplosionObject 안에 두면 함께 켜지고 꺼진다. 비우면 없음. 범위 피해가 없으면(반경 0) 건드리지 않는다")]
        public Transform SplashVisual;
        [Tooltip("SplashVisual이 스케일 1일 때의 반지름(m). Unity 기본 구(Sphere)는 0.5")]
        public float SplashVisualRadius = 0.5f;
        [Tooltip("0이면 터지는 순간 반경 크기, 0보다 크면 이 시간(초) 동안 작게 시작해 반경까지 커진다")]
        public float SplashVisualGrowTime = 0.25f;
        [Tooltip("폭발음. 하나를 무작위로 골라 음높이를 조금씩 바꿔 재생한다")]
        public AudioSource[] ExplosionSounds;
        [Tooltip("비행 중에만 켜둘 오브젝트(모델 등)")]
        public GameObject[] DisableOnExplode;
        [Tooltip("후연 파티클(로켓). 발사 시 방출을 켜고 폭발 시 방출만 끈다. Simulation Space는 World, DisableOnExplode에 넣지 않는다")]
        public ParticleSystem[] TrailParticles;

        [Header("지면·수면 폭발 (선택 — ExplosionObject에 더해 켠다. 소리는 비우면 ExplosionSounds)")]
        [Tooltip("땅·건물(지형)에 부딪혀 터질 때 ExplosionObject에 더해 켤 오브젝트(흙먼지 등). 이 탄의 자식으로 두면 위(+Y)가 닿은 면의 법선을 향하게 돌린다. 유닛에 맞거나 공중에서 터지면 ExplosionObject")]
        public GameObject GroundExplosionObject;
        [Tooltip("땅에 부딪힐 때 폭발음(비우면 ExplosionSounds)")]
        public AudioSource[] GroundExplosionSounds;
        [Tooltip("물(WaterLayers)에 부딪혀 터질 때 켤 오브젝트(물기둥 등). 위(+Y)가 수면 위를 향하게 돌린다")]
        public GameObject WaterExplosionObject;
        [Tooltip("물에 부딪힐 때 폭발음(비우면 ExplosionSounds)")]
        public AudioSource[] WaterExplosionSounds;
        [Tooltip("수면으로 볼 레이어(바다 콜라이더). 기본 Water(4)")]
        public LayerMask WaterLayers = 1 << 4;

        /// 발사하는 쪽이 넣는다. SaccTarget이 공격자를 찾을 때 이 변수 이름(EntityControl)을 읽으므로 바꾸면 안 된다.
        [System.NonSerialized] public SaccEntity EntityControl;
        [System.NonSerialized] public Vector3 LaunchVelocity;   // 모기체 속도 벡터(월드)
        [System.NonSerialized] public Transform PoolParent;
        [System.NonSerialized] public ACO_DFUNC_Missile PoolOwner;   // 회수 시 돌아갈 플레이어 무장 풀(부모는 그대로)
        [System.NonSerialized] public bool IsShooter;
        /// 적 곡사포(ACO_ArtilleryGun)가 쏜 포탄: 아군(호위 대상)만 범위 피해를 받고 나머지는 받지 않는다.
        [System.NonSerialized] public bool HitFriendlies;
        /// 근접 신관 반경(m, 적 항공기 마커 중심까지). 0이면 없음(닿아야 터진다). 쏘는 무장이 발사마다 넣는다.
        [System.NonSerialized] public float ProximityRadius;
        /// 유도(유도 로켓 LGR·유도 폭탄 LGB — 쏘는 무장이 락온한 표적을 넣는다, null이면 무유도): 계산 간격마다 속도 방향을 표적 쪽으로
        /// GuideTurnRate(도/초)만큼 돌린다(속력은 그대로 — 로켓은 추진, 폭탄은 중력이 계속 더한다). 속도 방향과 표적 방향이 GuideSeekerAngle보다
        /// 벌어지거나 표적이 사라지면 유도를 영영 끊고 그대로 날아간다. 표적 마커 중심이 이동 구간에서 GuideProximity 안이면 거기서 터진다.
        [System.NonSerialized] public Transform GuideTarget;
        [System.NonSerialized] public float GuideTurnRate;
        [System.NonSerialized] public float GuideSeekerAngle = 90f;
        [System.NonSerialized] public float GuideProximity;
        private bool _guided;
        private SaccEntity _guideEntity;

        // 근접 신관이 보는 레이어: 적 항공기 락온 마커(트리거 구). 마커 부모의 SaccTarget이 살아 있는 적만 센다(슈퍼에이스 기만체처럼 떼어 낸 마커는 빠진다).
        private const int PROXIMITY_LAYERS = 1 << 29;
        private Collider[] _proxHits = new Collider[16];

        private const int MAX_SPLASH_HITS = 32;
        private const int MAX_SPLASH_COLLIDERS = 256;   // 큰 반경이면 함선·지상 그룹 콜라이더가 많다 — 모자라면 일부 유닛이 빠진다
        private Collider[] _hits = new Collider[MAX_SPLASH_COLLIDERS];
        private SaccTarget[] _damaged = new SaccTarget[MAX_SPLASH_HITS];
        private SaccTarget _directPart;   // 탄이 직접 닿은 표적(부딪힌 콜라이더·유도 근접 표적) — 범위 피해 배율을 받지 않는다

        /// 비행 계산 간격(초). 프레임 속도와 상관없이 이 간격으로 계산한다 — CCIP 예측(ACO_DFUNC_Missile)이 같은 간격의 궤적을 수식으로 정확히 구한다.
        public const float FLIGHT_STEP = 0.02f;

        private Vector3 _velocity;
        private Vector3 _simPos;   // 계산상 위치(FLIGHT_STEP 경계). 화면 위치는 여기서 남은 시간만큼 앞으로 그린다
        private float _accum;
        private float _life;
        private float _age;
        private bool _flying;
        private bool _exploded;
        private Vector3 _splashVisualScale;
        private float _splashGrowStart;

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
            _directPart = null;
            _guided = GuideTarget != null && GuideTurnRate > 0f;
            _guideEntity = _guided ? GuideTarget.GetComponentInParent<SaccEntity>() : null;
            HideExplosions();
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
                Explode(transform.position, SURFACE_AIR, Vector3.up);
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
                if (_guided) { Steer(to, FLIGHT_STEP); }
                if (Acceleration > 0f && _velocity.magnitude < MaxSpeed) { _velocity += fwd * (Acceleration * FLIGHT_STEP); }
                _velocity += Physics.gravity * (GravityScale * FLIGHT_STEP);
                if (Drag > 0f) { _velocity *= Mathf.Max(1f - Drag * FLIGHT_STEP, 0f); }
                to += _velocity * FLIGHT_STEP;
            }
            if (guard > 0 && _age >= ArmTime)
            {
                // 땅·유닛은 트리거를 빼고, 물(WaterLayers)은 일반 콜라이더와 트리거를 모두 따로 본다(SAV 바다는 트리거 상자다 — 물 레이어엔 바다만 있다).
                // 둘 다 맞으면 가까운 쪽.
                RaycastHit hit;
                RaycastHit water;
                bool hitSolid = Physics.Linecast(from, to, out hit, HitLayers.value & ~WaterLayers.value, QueryTriggerInteraction.Ignore) && !IsOwnVehicle(hit.collider);
                bool hitWater = Physics.Linecast(from, to, out water, WaterLayers.value, QueryTriggerInteraction.Collide);
                if (hitWater && (!hitSolid || water.distance <= hit.distance))
                {
                    if (DebugHitLog && IsShooter) { LogHit(water); }
                    Explode(water.point, SURFACE_WATER, Vector3.up);
                    return;
                }
                if (hitSolid)
                {
                    if (DebugHitLog && IsShooter) { LogHit(hit); }
                    // 유닛 히트박스에 닿았을 때만 직접 맞은 표적 — 선체(Environment) 같은 큰 콜라이더는 본체로 이어져 코어가 "직접" 맞은 걸로 잘못 친다
                    _directPart = hit.collider && hit.collider.gameObject.layer == UNIT_HITBOX_LAYER ? hit.collider.GetComponentInParent<SaccTarget>() : null;
                    Explode(hit.point, SurfaceOf(hit.collider), hit.normal);
                    return;
                }
                Vector3 burst;
                if (_guided && GuideProximity > 0f && NearTarget(from, to, out burst))
                {
                    _directPart = GuideTarget.GetComponentInParent<SaccTarget>();
                    Explode(burst, SURFACE_AIR, Vector3.up);
                    return;
                }
                if (ProximityRadius > 0f && FindEnemyNear(from, to, out burst))
                {
                    if (DebugHitLog && IsShooter) { Debug.Log("[ACO] " + gameObject.name + ": 근접 신관 작동(반경 " + ProximityRadius.ToString("0") + "m)"); }
                    Explode(burst, SURFACE_AIR, Vector3.up);
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

        /// 유도 한 계산 간격: 속도 방향을 표적 쪽으로 돌린다(속력 유지). 표적이 사라졌거나 시커 각도 밖이면 유도를 끊는다.
        private void Steer(Vector3 pos, float h)
        {
            if (!GuideTarget || !GuideTarget.gameObject.activeInHierarchy || (_guideEntity && _guideEntity.dead)) { _guided = false; return; }
            float speed = _velocity.magnitude;
            Vector3 to = GuideTarget.position - pos;
            if (speed < 1f || to.sqrMagnitude < 0.01f) { return; }
            Vector3 dir = _velocity / speed;
            if (Vector3.Angle(dir, to) > GuideSeekerAngle) { _guided = false; return; }
            _velocity = Vector3.RotateTowards(dir, to.normalized, GuideTurnRate * Mathf.Deg2Rad * h, 0f) * speed;
        }

        /// 유도 표적 마커 중심이 이동 구간에서 GuideProximity 안에 드는 가장 가까운 점.
        private bool NearTarget(Vector3 from, Vector3 to, out Vector3 point)
        {
            point = to;
            if (!GuideTarget) { return false; }
            Vector3 p = GuideTarget.position;
            Vector3 step = to - from;
            float lenSq = step.sqrMagnitude;
            float t = lenSq > 0.000001f ? Mathf.Clamp01(Vector3.Dot(p - from, step) / lenSq) : 0f;
            point = from + step * t;
            return (p - point).sqrMagnitude <= GuideProximity * GuideProximity;
        }

        /// 이동 구간(from→to)에서 적 항공기 마커 중심이 ProximityRadius 안에 드는 가장 이른 점. 구간 가운데의 구 하나로 후보를 모은 뒤(구간 전체를 덮는다)
        /// 마커 중심까지 선분 최단 거리로 고른다 — 빠른 탄이 한 프레임에 마커를 지나쳐도 놓치지 않는다.
        private bool FindEnemyNear(Vector3 from, Vector3 to, out Vector3 point)
        {
            point = to;
            Vector3 step = to - from;
            float lenSq = step.sqrMagnitude;
            float r = ProximityRadius;
            int n = Physics.OverlapSphereNonAlloc(from + step * 0.5f, r + Mathf.Sqrt(lenSq) * 0.5f, _proxHits, PROXIMITY_LAYERS, QueryTriggerInteraction.Collide);
            float bestT = 2f;
            for (int i = 0; i < n; i++)
            {
                Collider c = _proxHits[i];
                if (!c || IsOwnVehicle(c)) { continue; }
                SaccTarget target = c.GetComponentInParent<SaccTarget>();
                if (!target || target.Health <= 0f) { continue; }
                ACO_EscortUnit escort = c.GetComponentInParent<ACO_EscortUnit>();
                if (escort != null && !escort.Hostile) { continue; }
                Vector3 p = c.transform.position;
                float t = lenSq > 0.000001f ? Mathf.Clamp01(Vector3.Dot(p - from, step) / lenSq) : 0f;
                Vector3 q = from + step * t;
                if ((p - q).sqrMagnitude > r * r || t >= bestT) { continue; }
                bestT = t;
                point = q;
            }
            return bestT <= 1f;
        }

        private bool IsOwnVehicle(Collider c)
        {
            return EntityControl != null && c != null && c.transform.IsChildOf(EntityControl.transform);
        }

        // 터진 자리: 공중·유닛 = ExplosionObject, 땅 = GroundExplosionObject, 물 = WaterExplosionObject(비었으면 ExplosionObject)
        private const int SURFACE_AIR = 0;
        private const int SURFACE_GROUND = 1;
        private const int SURFACE_WATER = 2;
        private const int UNIT_HITBOX_LAYER = 17;

        /// 닿은 콜라이더의 면: 물(WaterLayers), 유닛 히트박스(17 — 공중 폭발과 같은 연출), 그 밖은 땅.
        private int SurfaceOf(Collider c)
        {
            if (!c) { return SURFACE_AIR; }
            int layer = c.gameObject.layer;
            if (((1 << layer) & WaterLayers.value) != 0) { return SURFACE_WATER; }
            return layer == UNIT_HITBOX_LAYER ? SURFACE_AIR : SURFACE_GROUND;
        }

        private void Explode(Vector3 point, int surface, Vector3 normal)
        {
            if (_exploded) { return; }
            _exploded = true;
            _flying = false;
            transform.position = point;

            if (IsShooter) { ApplySplash(point); }

            SetFlightVisuals(false);
            ShowExplosion(surface, normal);
            ShowSplashVisual();
            PlayExplosionSound(surface);
            SetTrailEmission(false, false);
            SendCustomEventDelayedSeconds(nameof(MoveBackToPool), ExplosionDuration);
        }

        /// 폭발 범위 표시: SplashVisual을 반경에 맞춘다(부모 크기를 나눠 월드 반지름 = SplashRadius). GrowTime이 있으면 프레임마다 키운다.
        private void ShowSplashVisual()
        {
            if (!SplashVisual || SplashRadius <= 0f) { return; }
            Transform parent = SplashVisual.parent;
            float parentScale = parent != null ? Mathf.Max(Mathf.Abs(parent.lossyScale.x), 0.0001f) : 1f;
            _splashVisualScale = Vector3.one * (SplashRadius / Mathf.Max(SplashVisualRadius, 0.0001f) / parentScale);
            if (SplashVisualGrowTime <= 0f)
            {
                SplashVisual.localScale = _splashVisualScale;
                return;
            }
            _splashGrowStart = Time.time;
            SplashVisual.localScale = _splashVisualScale * 0.05f;
            SendCustomEventDelayedFrames(nameof(_GrowSplashVisual), 1);
        }

        public void _GrowSplashVisual()
        {
            if (!SplashVisual) { return; }
            float t = Mathf.Clamp01((Time.time - _splashGrowStart) / Mathf.Max(SplashVisualGrowTime, 0.0001f));
            float k = 1f - (1f - t) * (1f - t);   // 처음엔 빠르게, 끝에서 천천히
            SplashVisual.localScale = _splashVisualScale * Mathf.Lerp(0.05f, 1f, k);
            if (t < 1f) { SendCustomEventDelayedFrames(nameof(_GrowSplashVisual), 1); }
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
                // 아군(호위 대상)은 피해를 받지 않는다. 적 포탄(HitFriendlies)은 반대로 아군만 맞는다.
                ACO_EscortUnit escort = c.GetComponentInParent<ACO_EscortUnit>();
                bool friendly = escort != null && !escort.Hostile;
                if (friendly == !HitFriendlies) { continue; }

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
                if (target != _directPart)
                {
                    ACO_Warship ship = target.GetComponentInParent<ACO_Warship>();
                    if (ship != null)
                    {
                        if (ship.IsSplashImmune(target)) { continue; }   // 코어는 직접 맞혀야만(예외 무기도)
                        if (!FullPartSplash) { dmg *= ship.GetSplashScale(target); }
                    }
                }
                // 쏜 사람 화면의 격추 예측이 피해 이벤트를 버려 공격자가 비는 것을 막는다(ACO_Missile.NoteHit 참고)
                target.LastHitByPlayer = Networking.LocalPlayer;
                target.LastHitWeaponType = WeaponType;
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

        /// 폭발 오브젝트는 어디서 터지든 켜고, 땅·물이면 그 면의 오브젝트를 더 켠다. 땅·물 연출은 위(+Y)를 닿은 면의 법선으로 돌린다(물기둥·흙먼지가 똑바로 서게).
        private void ShowExplosion(int surface, Vector3 normal)
        {
            if (ExplosionObject) { ExplosionObject.SetActive(true); }
            GameObject extra = null;
            if (surface == SURFACE_GROUND) { extra = GroundExplosionObject; }
            else if (surface == SURFACE_WATER) { extra = WaterExplosionObject; }
            if (!extra) { return; }
            extra.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal.sqrMagnitude > 0.0001f ? normal : Vector3.up);
            extra.SetActive(true);
        }

        private void HideExplosions()
        {
            if (ExplosionObject) { ExplosionObject.SetActive(false); }
            if (GroundExplosionObject) { GroundExplosionObject.SetActive(false); }
            if (WaterExplosionObject) { WaterExplosionObject.SetActive(false); }
        }

        /// 폭발음: 면에 맞는 소리 묶음(비었으면 ExplosionSounds)에서 하나를 골라 음높이를 조금씩 바꿔 재생한다.
        private void PlayExplosionSound(int surface)
        {
            AudioSource[] sounds = ExplosionSounds;
            if (surface == SURFACE_GROUND && GroundExplosionSounds != null && GroundExplosionSounds.Length > 0) { sounds = GroundExplosionSounds; }
            else if (surface == SURFACE_WATER && WaterExplosionSounds != null && WaterExplosionSounds.Length > 0) { sounds = WaterExplosionSounds; }
            if (sounds == null || sounds.Length == 0) { return; }
            AudioSource sound = sounds[Random.Range(0, sounds.Length)];
            if (!sound) { return; }
            sound.pitch = Random.Range(0.94f, 1.2f);
            sound.Play();
        }

        public void MoveBackToPool()
        {
            HideExplosions();
            SetFlightVisuals(true);
            SetTrailEmission(false, false);
            _flying = false;
            _exploded = false;
            if (PoolOwner)
            {
                PoolOwner.ReturnToPool(gameObject);
                return;
            }
            if (PoolParent) { transform.SetParent(PoolParent); }
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            gameObject.SetActive(false);
        }
    }
}
