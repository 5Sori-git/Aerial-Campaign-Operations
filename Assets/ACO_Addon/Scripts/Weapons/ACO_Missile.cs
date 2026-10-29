using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// SAV_AAMController를 참고해 만든 경량 자체 미사일.
    /// SAV 미사일과 다른 핵심: 위치를 동기화하지 않고 각 클라이언트가 로컬에서 재현한다.
    /// 명중 판정: NPC가 쏜 미사일은 "피격 대상의 오너"(설계 3.5-4 victim-authoritative),
    /// 플레이어가 쏜 미사일은 "쏜 사람"(SAV와 같음 — 격추 점수가 SaccTarget.LastHitByPlayer=데미지를 보낸 사람으로 매겨진다).
    /// 발사하는 쪽(ACO_MissileLauncher / ACO_DFUNC_Missile)이 EnableWeapon 전에 Launch* 값을 넣는다.
    /// 유도는 최단거리 직접 추적. 대응책(플레어/채프)과 미사일 접근 경고는 SAV 미사일과
    /// 같은 변수를 같은 규칙으로 읽고 써서, SAV 기체의 대응탄/경고 UI가 그대로 동작한다.
    /// 이동은 Update에서 처리한다 — SAV 차량/NPC도 Update에서 움직이기 때문.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Missile : UdonSharpBehaviour
    {
        [Header("비행")]
        public float LaunchSpeed = 80f;
        public float MaxSpeed = 420f;
        public float Acceleration = 300f;
        [Tooltip("초당 최대 선회 각도(도)")]
        public float TurnRate = 120f;
        [Tooltip("발사 직후 유도 없이 직진하는 시간(초)")]
        public float FlyStraightTime = 0.35f;
        public float MaxLifetime = 20f;

        [Header("유도 방식")]
        [Tooltip("0 = 직접 추적: 타겟의 현재 위치를 곧장 향한다(기본, 단순하고 예측 가능)\n1 = 예측 조준: 타겟 속도로 요격점을 계산해 그곳을 향한다\n2 = 비례항법: 시선(미사일→타겟) 회전율 x N 만큼 선회해 충돌 코스를 유지한다. 가로지르는 타겟에 강하다")]
        [Range(0, 2)] public int GuidanceMode = 0;
        [Tooltip("비례항법 항법 상수 N. 3~5가 일반적. 클수록 빨리 충돌 코스를 잡지만 타겟 위치 흔들림에 민감하다")]
        public float NavigationConstant = 3f;
        [Tooltip("비례항법: 기수가 시선에서 이 각도(도) 넘게 벗어나 있으면 먼저 직접 추적으로 기수를 돌린다(발사 직후, 콜드 런치 후). 비례항법은 대략 타겟을 향하고 있을 때만 제대로 동작한다")]
        public float PNPursuitAngle = 45f;
        [Tooltip("예측 조준: 이 시간(초)보다 먼 미래는 예측하지 않는다. 너무 멀리 예측하면 조준점이 엉뚱한 곳으로 튄다")]
        public float MaxLeadTime = 4f;

        [Header("콜드 런치 (수직 발사)")]
        [Tooltip("발사 후 이 시간(초) 동안 엔진을 켜지 않고 사출 속도로 날아간다. 0이면 끔(일반 발사). 수직 발사(VLS)는 런처의 LaunchPoint 앞쪽(파란 축)이 위를 향하게 둔다")]
        public float ColdLaunchTime = 0f;
        [Tooltip("콜드 런치 사출 속도(m/s). 모기체 속도가 더 빠르면 그 속도를 쓴다")]
        public float ColdLaunchSpeed = 30f;
        [Tooltip("콜드 런치 중 타겟 쪽으로 기수를 돌리는 속도(도/초). 0이면 사출 방향 그대로 날아간다")]
        public float ColdLaunchTurnRate = 0f;
        [Tooltip("콜드 런치 중 중력 배수(1 = Physics.gravity 그대로, 0 = 중력 없음). 사출된 미사일이 솟구쳤다 느려지며 처지는 궤적을 만든다")]
        public float ColdLaunchGravity = 1f;

        [Header("시커 (SAV_AAMController와 같은 의미/기본값)")]
        [Tooltip("기수와 타겟 방향이 이 각도(도)보다 벌어지면 그 프레임은 추적하지 못한다. 타겟을 지나치면 자연히 여기에 걸린다")]
        public float MaxTrackingAngle = 90f;
        [Tooltip("추적 불가 상태가 이 시간(초) 연속되면 타겟을 영구히 놓친다")]
        public float UnlockTime = 0.1f;

        // 플레어/채프: 표적이 켜면 100% 속는다 — NPC는 ACO_NPCFighterAI의 플레어, 플레이어는 SAV 대응탄(MissileType에 맞는 NumActiveChaff/Flares/OtherCM > 0).
        [Header("대응책")]
        [Tooltip("켜면(에이스컴뱃식) 표적 기체가 어떤 대응탄(채프/플레어/기타)이든 켜면 속는다. 끄면 SAV처럼 미사일 종류(MissileType)에 맞는 대응탄만 통한다 — "
            + "레이더 미사일은 채프, 열추적은 플레어")]
        public bool AnyCountermeasure = true;
        [Tooltip("노칭 판정 각도(도). 0이면 비활성(SAV 기본값)")]
        public float NotchAngle = 0f;
        public float NotchHorizon = 5f;

        [Header("미사일 접근 경고 (SAV 호환)")]
        [Tooltip("추적 중 타겟 기체의 MissilesIncoming* 카운터를 올려 조종석 경고를 울린다")]
        public bool SendMissileIncoming = true;
        [Tooltip("타겟 기체 Animator의 정수 파라미터 이름")]
        public string MissileIncomingName = "missilesincoming";

        [Header("명중 / 피해")]
        [Tooltip("근접 신관 반경(m)")]
        public float ProximityRadius = 9f;
        public float Damage = 50f;
        [Tooltip("SAV 무기 타입. 2 = 미사일 — SAV 기체는 이 값일 때 피격 시 무작위 회전력(MissilePushForce)을 스스로 넣는다")]
        public byte WeaponType = 2;
        [Tooltip("범위 피해를 받는 레이어(지상 유닛의 SaccTarget 콜라이더). 범위 반경은 플레이어 런처(ACO_DFUNC_Missile, MissileSplash)가 쏠 때 넣는다")]
        public LayerMask SplashLayers = 1 << 17;

        [Header("피격 충격 (플레이어 기체)")]
        [Tooltip("명중 시 기체를 미사일 진행 방향으로 미는 속도 변화(m/s). SAV 미사일은 실제 물리 충돌로 기체를 치지만 이 미사일은 근접 신관이라 직접 넣는다")]
        public float ImpactPush = 3f;
        [Tooltip("명중 시 기수를 튕기는 회전 속도(도/초). 폭발 지점 반대쪽으로 꺾인다")]
        public float ImpactSpin = 45f;
        [Tooltip("지형에 부딪히면 폭발한다")]
        public LayerMask GroundLayers = 2049;

        [Header("연출")]
        public GameObject ExplosionObject;
        [Tooltip("폭발음. 하나를 무작위로 골라 음높이를 조금씩 바꿔 재생한다(SAV 미사일과 같은 방식). ExplosionObject 안에 두면 오브젝트와 함께 켜진다")]
        public AudioSource[] ExplosionSounds;
        [Tooltip("폭발 후 미사일을 풀에 돌려놓기까지의 시간(초). 후연을 쓰면 후연 파티클 Start Lifetime 최댓값 이상으로 둔다 — 돌려놓는 순간 남은 연기가 사라진다")]
        public float ExplosionDuration = 2f;
        [Tooltip("비행 중에만 켜둘 오브젝트(모델/트레일 등)")]
        public GameObject[] DisableOnExplode;
        [Tooltip("후연 파티클. 발사 시 방출을 켜고 폭발 시 방출만 끈다 — 이미 뿌린 연기는 수명이 다할 때까지 남는다. "
            + "꺼진 오브젝트의 파티클은 바로 사라지므로 DisableOnExplode에 넣지 말고, Simulation Space는 World로 둔다(Local이면 연기가 미사일을 따라다닌다). "
            + "ExplosionDuration을 파티클 수명 이상으로 둔다")]
        public ParticleSystem[] TrailParticles;

        /// 런처가 발사 직전에 주입한다. SaccEntity의 공격자 추적이 이 변수 이름을 읽으므로 바꾸면 안 된다.
        [System.NonSerialized] public SaccEntity EntityControl;
        // 발사하는 쪽이 EnableWeapon 전에 넣는다.
        [System.NonSerialized] public GameObject LaunchTarget;      // 락온 마커. null이면 무유도(직진)
        [System.NonSerialized] public int LaunchMissileType = 1;    // 0 레이더, 1 열추적, 2 기타 (대응책/경고 변수 선택)
        [System.NonSerialized] public float LaunchInheritSpeed;     // 모기체 속력. 발사 속력이 이보다 느리면 이 속력에서 출발한다
        [System.NonSerialized] public Transform PoolParent;         // 회수 시 돌아갈 부모(런처)
        [System.NonSerialized] public bool ShooterJudgesHit;        // true면 쏜 사람이, false면 피격 대상의 오너가 명중을 판정한다
        [System.NonSerialized] public bool IsShooter;               // 이 클라이언트가 쏜 사람인가
        // 범위 피해(대지 미사일). 0이면 없음. 쏜 사람만 넣는다 — 쏜 사람 화면에서 판정한다(폭탄/로켓과 같다).
        [System.NonSerialized] public float SplashRadius;
        [System.NonSerialized] public float MinSplashFraction = 0.25f;
        // 무전 자막(런처가 넣는다): 내 미사일이 빗나감/플레어에 속음, 나를 노린 미사일 경보.
        [System.NonSerialized] public ACO_Radio Radio;
        // RWR(런처가 넣는다): 나를 노린 미사일이면 경고를 켤 때 등록해 방향·거리를 그린다.
        [System.NonSerialized] public ACO_RWR Rwr;

        // SAV_AAMController와 같은 이름/순서 — MissileType(0 레이더, 1 열추적, 2 기타)으로 인덱싱한다.
        private string[] MissileTypes = { "MissilesIncomingRadar", "MissilesIncomingHeat", "MissilesIncomingOther" };
        private string[] CMTypes = { "NumActiveChaff", "NumActiveFlares", "NumActiveOtherCM" };

        private Transform _target;
        private SaccEntity _targetEntity;
        private SaccTarget _targetStatic;
        private SaccAirVehicle _targetSAV;
        private Animator _targetAnimator;
        private int _missileType = 1;

        private float _speed;
        private float _lifeTimer;
        private float _straightTimer;
        private float _coldTimer;
        // 콜드 런치 중 속도 벡터. 기수(transform)가 돌아가도 이동은 이 벡터(사출 속도 + 중력)를 따른다.
        private Vector3 _coldVelocity;
        // 콜드 런치 동안 + 엔진 점화 후 타겟이 처음 시커 각도 안에 들어올 때까지는 각도로 락온을 잃지 않는다(360도).
        // 수직으로 솟은 기수에서 낮은 타겟은 90도 가까이 벗어나 있어, 점화 직후 기수를 돌리는 동안 놓치기 때문이다.
        private bool _seekerUnlimited;
        private float _acquireTimer;
        private const float COLD_ACQUIRE_WINDOW = 2f;
        private float _unlockTimer;
        private bool _flying;
        private bool _exploded;
        private bool _hadTarget;        // 락온한 채 발사됐다(무유도 발사는 빗나감을 알리지 않는다)
        private bool _outcomeReported;  // 빗나감/속음을 이미 알렸다(한 발에 한 번)
        private const int MAX_SPLASH_HITS = 32;
        private const int MAX_SPLASH_COLLIDERS = 256;   // 큰 반경이면 함선·지상 그룹 콜라이더가 많다 — 모자라면 일부 유닛이 빠진다
        private Collider[] _splashHits = new Collider[MAX_SPLASH_COLLIDERS];
        private SaccTarget[] _splashDamaged = new SaccTarget[MAX_SPLASH_HITS];
        private bool _targetLost;
        private bool _missileIncoming;

        private Vector3 _lastTargetPos;
        private Vector3 _targetMoveDir;
        private bool _hasLastTargetPos;

        private const int GUIDE_LEAD = 1;
        private const int GUIDE_PN = 2;
        private const float TARGET_VEL_SMOOTH_TIME = 0.25f;

        // 예측 조준용 타겟 속도. SAV 기체는 동기화된 속도, NPC는 비행 컨트롤러 값, 둘 다 아니면 위치 미분.
        private ACO_NPCFlightController _targetNPC;
        // NPC 전투기 표적: 플레어(대응책)를 쓰고, 날아오는 미사일을 알아야 하므로 등록한다.
        private ACO_NPCFighterAI _targetAI;
        // 다른 유닛(수송기·폭격기)의 플레어 장치 — NPC 전투기 AI 플레어와 같은 규칙.
        private ACO_FlareDispenser _targetFlares;
        private ACO_FlareDispenser _decoyFlares;
        // 플레어에 속았다: 표적을 놓치고, 그 플레어가 켜져 있는 동안 플레어 쪽으로 끌려간다.
        // NPC 표적은 그 AI가 플레어 지점을 알려 주고, 플레이어(SAV) 표적은 속은 순간의 기체 위치·속도로 떨어지는 점을 쓴다.
        private bool _decoyed;
        private SaccAirVehicle _decoySAV;
        private Vector3 _decoyPos;
        private Vector3 _decoyVel;
        private float _decoyStart;
        // SAV 플레어 지점: 기체 속도 일부를 이어받아 떨어진다(SAV 플레어는 리지드바디라 정확히 같진 않다 — 연출만).
        private const float SAV_FLARE_INHERIT = 0.8f;
        private const float SAV_FLARE_GRAVITY = 0.5f;
        private Vector3 _targetVel;
        private float _lastVelSampleTime;
        // 비례항법용 시선 방향과 시선 회전율(rad/s, 회전축 x 각속도)
        private Vector3 _prevLos;
        private float _prevLosTime;
        private bool _hasPrevLos;
        private Vector3 _losRate;
        private float _notchHorizonDot;
        private float _notchLimitDot;

        /// 런처가 SetActive(true) 직후 SendCustomEvent로 호출한다 (SAV 미사일과 같은 진입점 이름).
        public void EnableWeapon()
        {
            ResolveTarget();

            _missileType = Mathf.Clamp(LaunchMissileType, 0, 2);
            _notchHorizonDot = 1f - Mathf.Cos(NotchHorizon * Mathf.Deg2Rad);
            _notchLimitDot = 1f - Mathf.Cos(NotchAngle * Mathf.Deg2Rad);

            // 모기체가 빠르면 그 속도에서 출발해야 뒤로 처지지 않는다. SAM은 InheritVelocityFrom이 없어 LaunchSpeed로 발사된다.
            bool cold = ColdLaunchTime > 0f;
            _speed = cold ? ColdLaunchSpeed : LaunchSpeed;
            _speed = Mathf.Max(_speed, LaunchInheritSpeed);
            _lifeTimer = MaxLifetime;
            _straightTimer = FlyStraightTime;
            _coldTimer = cold ? ColdLaunchTime : 0f;
            // 런처가 EnableWeapon 전에 발사 지점의 자세를 넣어 두므로 지금의 앞쪽이 사출 방향이다.
            _coldVelocity = transform.forward * _speed;
            _seekerUnlimited = cold;
            _acquireTimer = 0f;
            _unlockTimer = 0f;
            _flying = true;
            _exploded = false;
            _targetLost = false;
            _decoyed = false;
            _decoyFlares = null;
            _decoySAV = null;
            _missileIncoming = false;
            _hasLastTargetPos = false;
            _targetMoveDir = Vector3.zero;
            _targetVel = Vector3.zero;
            _hasPrevLos = false;
            _losRate = Vector3.zero;

            if (ExplosionObject) { ExplosionObject.SetActive(false); }
            for (int i = 0; i < DisableOnExplode.Length; i++)
            {
                if (DisableOnExplode[i]) { DisableOnExplode[i].SetActive(true); }
            }
            // 지난 발사의 연기가 남아 있으면 발사 지점으로 순간이동해 보이므로 지우고 시작한다.
            SetTrailEmission(true, true);

            // SAV 미사일과 같이 발사 시점에 경고를 시작한다. NPC 표적은 날아오는 미사일로 등록한다(플레어 판단용).
            StartIncomingWarning();
            if (_targetFlares) { _targetFlares.RegisterIncoming(this); }   // NPC 전투기·수송기·폭격기의 플레어 장치
            _hadTarget = _target != null;
            _outcomeReported = false;
            // 나를 노린 미사일(NPC·SAM 미사일 — 표적 기체를 이 클라이언트가 조종 중)
            if (Radio && !ShooterJudgesHit && _targetEntity && _targetEntity.Piloting) { Radio.Report(ACO_Radio.EV_MISSILE_INCOMING, ""); }
        }

        /// 내 미사일의 결과를 무전으로 한 번 알린다(쏜 사람 화면에서만, 락온해 쏜 플레이어 미사일만).
        private void ReportOutcome(int ev)
        {
            if (_outcomeReported) { return; }
            _outcomeReported = true;
            if (Radio && ShooterJudgesHit && IsShooter && _hadTarget) { Radio.Report(ev, ""); }
        }

        private void ResolveTarget()
        {
            _target = null;
            _targetEntity = null;
            _targetStatic = null;
            _targetSAV = null;
            _targetAnimator = null;
            _targetNPC = null;
            _targetAI = null;
            _targetFlares = null;

            GameObject marker = LaunchTarget;
            if (!marker) { return; }
            _target = marker.transform;

            // NPC는 루트에 SaccEntity와 SaccTarget을 둘 다 가진다. 체력은 SaccTarget이 소유하므로(설계 0.2-1)
            // 둘 다 찾아두고 데미지는 SaccTarget을 우선한다. 플레이어 기체는 SaccTarget이 없어 SaccEntity로 간다.
            _targetStatic = marker.GetComponentInParent<SaccTarget>();
            _targetEntity = marker.GetComponentInParent<SaccEntity>();
            _targetNPC = marker.GetComponentInParent<ACO_NPCFlightController>();
            _targetAI = marker.GetComponentInParent<ACO_NPCFighterAI>();
            _targetFlares = marker.GetComponentInParent<ACO_FlareDispenser>();

            // 대응책/경고 변수는 SaccAirVehicle(=플레이어 기체)에만 있다. NPC는 null이 되어 두 기능이 자연스럽게 꺼진다.
            if (_targetEntity)
            {
                _targetSAV = (SaccAirVehicle)_targetEntity.GetExtention(GetUdonTypeName<SaccAirVehicle>());
            }
        }

        /// 플레이어 미사일은 쏜 사람이, NPC 미사일은 피격 대상의 오너가 판정한다(플레이어 기체라면 그 플레이어 자신).
        /// 비행 중 오너십이 바뀔 수 있으므로 발사 시점이 아니라 명중 순간에 확인한다.
        private bool IJudgeThisHit()
        {
            if (ShooterJudgesHit) { return IsShooter; }
            if (_targetStatic) { return Networking.IsOwner(_targetStatic.gameObject); }
            if (_targetEntity) { return Networking.IsOwner(_targetEntity.gameObject); }
            return false;
        }

        void Update()
        {
            if (!_flying || _exploded) { return; }

            float dt = Time.deltaTime;

            _lifeTimer -= dt;
            if (_lifeTimer <= 0f)
            {
                Explode(false);
                return;
            }

            if (_straightTimer > 0f) { _straightTimer -= dt; }

            if (_coldTimer > 0f)
            {
                // 콜드 런치: 엔진 없이 사출 속도 + 중력으로 날아간다(탄도). 선회는 기수만 돈다(옵션).
                _coldTimer -= dt;
                if (ColdLaunchTurnRate > 0f) { Track(dt, ColdLaunchTurnRate); }
                _coldVelocity += Physics.gravity * (ColdLaunchGravity * dt);

                // 점화: 지금까지의 속력을 이어받아 기수 방향으로 날기 시작한다.
                if (_coldTimer <= 0f) { _speed = _coldVelocity.magnitude; }
            }
            else
            {
                // 점화 후 타겟을 시커 각도 안에 못 넣은 채 이 시간이 지나면 일반 시커로 돌아간다(무한 추적 방지).
                if (_seekerUnlimited)
                {
                    _acquireTimer += dt;
                    if (_acquireTimer >= COLD_ACQUIRE_WINDOW) { _seekerUnlimited = false; }
                }
                if (_straightTimer <= 0f) { Track(dt, TurnRate); }
                _speed = Mathf.Min(_speed + Acceleration * dt, MaxSpeed);
            }
            if (_exploded) { return; } // 플레어에 닿아 터졌다

            Vector3 from = transform.position;
            // 콜드 런치 중엔 기수와 무관하게 탄도 속도 벡터로 이동한다. 점화 후엔 기수 방향으로 난다.
            Vector3 step = _coldTimer > 0f ? _coldVelocity * dt : transform.forward * (_speed * dt);
            Vector3 to = from + step;

            if (CheckHit(from, to)) { return; }

            transform.position = to;
        }

        /// SAV와 같은 규칙: 시커가 무력화된 프레임에는 직진하고, 그 상태가 UnlockTime 연속되면 타겟을 영구히 놓친다.
        private void Track(float dt, float turnRate)
        {
            // 에이스컴뱃처럼 플레어는 100% 통한다: 표적이 플레어를 켜면(켜진 동안 새로 쏜 미사일 포함) 표적을 놓치고 끌려간다.
            //  - NPC 전투기·수송기·폭격기: 플레어 장치(ACO_FlareDispenser.NetFlare)
            //  - 플레이어 기체: SAV 대응탄 — 이 미사일 종류(MissileType)에 맞는 NumActiveChaff/Flares/OtherCM이 0보다 크다.
            //    SAV는 대응탄 투하를 모두에게 보내고 각자 FlareActiveTime 동안 개수를 올려 두므로 어느 화면에서나 같은 시각에 켜진다.
            if (!_decoyed && !_targetLost)
            {
                if (_targetFlares != null && _targetFlares.IsFlareActive())
                {
                    ReportOutcome(ACO_Radio.EV_MISSILE_DECOYED);
                    _decoyed = true;
                    _decoyFlares = _targetFlares;
                    LoseTarget();
                }
                else if (_targetSAV && SavCountermeasuresActive(_targetSAV))
                {
                    ReportOutcome(ACO_Radio.EV_MISSILE_DECOYED);
                    _decoyed = true;
                    _decoySAV = _targetSAV;
                    _decoyPos = _target.position;
                    _decoyVel = _targetSAV.CurrentVel * SAV_FLARE_INHERIT;
                    _decoyStart = Time.time;
                    LoseTarget();
                }
            }
            if (_decoyed) { TrackDecoy(dt, turnRate); return; }
            if (_targetLost) { return; }

            if (!TargetIsValid() || _unlockTimer >= UnlockTime)
            {
                LoseTarget();
                return;
            }

            Vector3 targetPos = _target.position;
            if (_hasLastTargetPos) { _targetMoveDir = targetPos - _lastTargetPos; }
            _lastTargetPos = targetPos;
            _hasLastTargetPos = true;
            // 이동량과 같은 구간으로 재야 하므로 시커 판정보다 먼저 갱신한다.
            UpdateTargetVelocity(targetPos);

            Vector3 toTarget = targetPos - transform.position;
            if (toTarget.sqrMagnitude < 0.000001f) { return; }
            Vector3 dirToTarget = toTarget.normalized;

            if (IsSeekerDefeated(dirToTarget))
            {
                _unlockTimer += dt;
                return;
            }
            _unlockTimer = 0f;

            if (GuidanceMode == GUIDE_PN)
            {
                if (SteerProportional(dirToTarget, turnRate, dt)) { return; }
                // 시선에서 크게 벗어나 있으면 아래 직접 추적으로 먼저 기수를 돌린다.
            }

            Vector3 desired = dirToTarget;
            if (GuidanceMode == GUIDE_LEAD)
            {
                // 예측 조준: 지금 속력으로 타겟과 만나는 요격점을 향한다.
                float t = InterceptTime(toTarget, _targetVel, Mathf.Max(_speed, 1f));
                Vector3 lead = targetPos + _targetVel * t - transform.position;
                if (lead.sqrMagnitude > 0.000001f) { desired = lead.normalized; }
            }

            Quaternion want = Quaternion.LookRotation(desired, transform.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnRate * dt);
        }

        /// 속은 미사일: 플레어가 켜져 있는 동안 떨어지는 플레어를 직접 추적하고, 닿으면 피해 없이 터진다. 꺼지면 그대로 직진.
        private void TrackDecoy(float dt, float turnRate)
        {
            Vector3 flare;
            if (_decoyFlares != null)
            {
                if (!_decoyFlares.IsFlareActive()) { return; }
                flare = _decoyFlares.GetFlarePosition();
            }
            else if (_decoySAV != null)
            {
                if (!SavCountermeasuresActive(_decoySAV)) { return; }
                float t = Time.time - _decoyStart;
                flare = _decoyPos + _decoyVel * t + Physics.gravity * (0.5f * SAV_FLARE_GRAVITY * t * t);
            }
            else { return; }
            Vector3 to = flare - transform.position;
            if (to.sqrMagnitude <= ProximityRadius * ProximityRadius)
            {
                Explode(false);
                return;
            }
            Quaternion want = Quaternion.LookRotation(to.normalized, transform.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnRate * dt);
        }

        private bool SavCountermeasuresActive(SaccAirVehicle sav)
        {
            if (!AnyCountermeasure) { return (int)sav.GetProgramVariable(CMTypes[_missileType]) > 0; }
            for (int i = 0; i < CMTypes.Length; i++)
            {
                if ((int)sav.GetProgramVariable(CMTypes[i]) > 0) { return true; }
            }
            return false;
        }

        /// 비례항법: 기수 회전율 = N x 시선 회전율. 시선이 돌지 않으면(충돌 코스) 직진하고, 돌면 그보다 N배 빨리 앞질러 돈다.
        /// 기수가 시선에서 PNPursuitAngle 넘게 벗어나 있으면 false를 돌려 직접 추적에 맡긴다.
        private bool SteerProportional(Vector3 dirToTarget, float turnRate, float dt)
        {
            float now = Time.time;
            float elapsed = now - _prevLosTime;
            if (_hasPrevLos && elapsed > 0f && elapsed < 0.2f)
            {
                // |a x b| = sin(사잇각), 방향 = 회전축 → 작은 각에서 각속도 벡터가 된다. 타겟 위치 흔들림을 줄이려 평활한다.
                Vector3 rate = Vector3.Cross(_prevLos, dirToTarget) / elapsed;
                _losRate = Vector3.Lerp(_losRate, rate, 0.5f);
            }
            else
            {
                _losRate = Vector3.zero;
            }
            _prevLos = dirToTarget;
            _prevLosTime = now;
            _hasPrevLos = true;

            if (Vector3.Angle(transform.forward, dirToTarget) > PNPursuitAngle) { return false; }

            Vector3 cmd = _losRate * NavigationConstant;
            float cmdRate = cmd.magnitude * Mathf.Rad2Deg;
            if (cmdRate > 0.0001f)
            {
                float step = Mathf.Min(cmdRate, turnRate) * dt;
                transform.rotation = Quaternion.AngleAxis(step, cmd / cmd.magnitude) * transform.rotation;
            }
            return true;
        }

        /// |D + V·t| = s·t 를 t에 대해 푼다(D = 타겟-미사일, V = 타겟 속도, s = 미사일 속력). 해가 없으면 0(직접 추적).
        private float InterceptTime(Vector3 d, Vector3 v, float s)
        {
            float a = Vector3.Dot(v, v) - s * s;
            float b = 2f * Vector3.Dot(d, v);
            float c = Vector3.Dot(d, d);

            float t = 0f;
            if (Mathf.Abs(a) < 0.0001f)
            {
                if (b < 0f) { t = -c / b; }
            }
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc < 0f) { return 0f; }
                float root = Mathf.Sqrt(disc);
                float t1 = (-b - root) / (2f * a);
                float t2 = (-b + root) / (2f * a);
                if (t1 > 0f && t2 > 0f) { t = Mathf.Min(t1, t2); }
                else if (t1 > 0f) { t = t1; }
                else if (t2 > 0f) { t = t2; }
            }
            return Mathf.Clamp(t, 0f, MaxLeadTime);
        }

        /// 타겟 속도. 원격 기체 위치는 네트워크 보간으로 흔들리므로 가능하면 직접 받는다(NPC AI와 같은 규칙).
        private void UpdateTargetVelocity(Vector3 targetPos)
        {
            float now = Time.time;
            float elapsed = now - _lastVelSampleTime;
            _lastVelSampleTime = now;

            if (_targetSAV != null) { _targetVel = _targetSAV.CurrentVel; return; }
            if (_targetNPC != null) { _targetVel = _targetNPC.transform.forward * _targetNPC.Speed; return; }

            // 위치 미분(_targetMoveDir는 Track이 매 프레임 갱신하는 이동량).
            if (elapsed > 0f && elapsed < 0.2f)
            {
                Vector3 raw = _targetMoveDir / elapsed;
                _targetVel = Vector3.Lerp(_targetVel, raw, 1f - Mathf.Exp(-elapsed / TARGET_VEL_SMOOTH_TIME));
            }
        }

        private bool IsSeekerDefeated(Vector3 dirToTarget)
        {
            float angle = Vector3.Angle(dirToTarget, transform.forward);
            if (_seekerUnlimited)
            {
                // 점화 후 타겟이 처음 시커 각도 안에 들어오면 그때부터 일반 시커로 돌아간다.
                if (_coldTimer <= 0f && angle <= MaxTrackingAngle) { _seekerUnlimited = false; }
            }
            else if (angle > MaxTrackingAngle)
            {
                return true;
            }

            // NPC 전투기의 플레어는 Track에서 처리한다(켜져 있으면 100% 속는다). 노칭/SAV 대응탄 변수는 NPC에 없다.
            if (_targetAI != null || _targetFlares != null) { return false; }

            // 대응책과 노칭은 SAV 기체에만 적용된다(변수가 거기에만 있다).
            if (!_targetSAV) { return false; }

            // 대응탄(플레어/채프)은 Track에서 처리한다 — 켜져 있으면 100% 속는다(에이스컴뱃식).

            // 노칭: 타겟이 시선과 직각으로 움직이며 지평선 근처에 있을 때 (SAV L417과 동일, NotchAngle 0이면 비활성).
            if (_targetMoveDir.sqrMagnitude > 0.000001f
                && Vector3.Dot(Vector3.up, dirToTarget) < _notchHorizonDot
                && Mathf.Abs(Vector3.Dot(_targetMoveDir.normalized, dirToTarget)) < _notchLimitDot)
            {
                return true;
            }

            return false;
        }

        private void LoseTarget()
        {
            if (_targetLost) { return; }
            _targetLost = true;
            StopIncomingWarning();
            if (_targetFlares) { _targetFlares.UnregisterIncoming(this); }
        }

        /// 아직 표적을 쫓고 있는가(NPC가 플레어를 뿌릴지 판단할 때 읽는다).
        public bool IsTracking() { return _flying && !_exploded && !_targetLost; }

        private bool TargetIsValid()
        {
            if (!_target || !_target.gameObject.activeInHierarchy) { return false; }
            if (_targetEntity && _targetEntity.dead) { return false; }
            return true;
        }

        /// 고속 미사일이 타겟을 지나쳐 버리는(터널링) 문제를 막기 위해 이번 프레임 이동 구간과의 최단 거리로 판정한다.
        /// 대응책에 무력화된(타겟을 놓친) 미사일은 근접 신관이 동작하지 않는다.
        private bool CheckHit(Vector3 from, Vector3 to)
        {
            if (!_targetLost && TargetIsValid())
            {
                Vector3 targetPos = _target.position;
                Vector3 step = to - from;
                float stepLenSq = step.sqrMagnitude;

                float closest;
                if (stepLenSq < 0.000001f)
                {
                    closest = Vector3.Distance(from, targetPos);
                }
                else
                {
                    float t = Mathf.Clamp01(Vector3.Dot(targetPos - from, step) / stepLenSq);
                    closest = Vector3.Distance(from + step * t, targetPos);
                }

                if (closest <= ProximityRadius)
                {
                    Explode(true);
                    return true;
                }
            }

            RaycastHit hit;
            if (Physics.Linecast(from, to, out hit, GroundLayers, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;
                Explode(false);
                return true;
            }

            return false;
        }

        private void Explode(bool hitTarget)
        {
            if (_exploded) { return; }
            _exploded = true;
            _flying = false;

            StopIncomingWarning();
            if (_targetFlares) { _targetFlares.UnregisterIncoming(this); }
            // 맞혔으면 결과는 격추 대사(작전판)가 맡는다. 범위 피해 미사일은 빗나가도 피해를 줄 수 있어 알리지 않는다.
            if (hitTarget || SplashRadius > 0f) { _outcomeReported = true; }
            else { ReportOutcome(ACO_Radio.EV_MISSILE_MISS); }

            // 데미지는 피격 대상의 오너에서만 적용한다.
            // SaccTarget/SaccEntity가 내부적으로 로컬 적용 + 다른 클라이언트 전파를 처리한다.
            if (hitTarget && IJudgeThisHit())
            {
                if (_targetStatic)
                {
                    _targetStatic.WeaponDamageTarget(Damage, gameObject, WeaponType);
                }
                else if (_targetEntity)
                {
                    _targetEntity.WeaponDamageVehicle(Damage, gameObject, WeaponType);
                    ApplyImpact();
                }
            }

            // 범위 피해: 명중이든 지형 충돌이든 터진 자리에서. 정통으로 맞은 표적은 이미 받았으므로 뺀다.
            if (SplashRadius > 0f && ShooterJudgesHit && IsShooter)
            {
                ApplySplash(transform.position, hitTarget ? _targetStatic : null);
            }

            for (int i = 0; i < DisableOnExplode.Length; i++)
            {
                if (DisableOnExplode[i]) { DisableOnExplode[i].SetActive(false); }
            }

            if (ExplosionObject) { ExplosionObject.SetActive(true); }
            PlayExplosionSound();
            SetTrailEmission(false, false);

            SendCustomEventDelayedSeconds(nameof(MoveBackToPool), ExplosionDuration);
        }

        /// ACO_Ordnance와 같은 규칙: 반경 안의 SaccTarget마다 가장 가까운 콜라이더 거리로 한 번, 중심 Damage → 가장자리 MinSplashFraction.
        /// 쏜 기체와 아군(호위 대상)은 빼고, skip(정통으로 맞은 표적)도 뺀다.
        private void ApplySplash(Vector3 center, SaccTarget skip)
        {
            int n = Physics.OverlapSphereNonAlloc(center, SplashRadius, _splashHits, SplashLayers);
            int damagedCount = 0;
            for (int i = 0; i < n; i++)
            {
                Collider c = _splashHits[i];
                if (!c) { continue; }
                SaccTarget target = c.GetComponentInParent<SaccTarget>();
                if (!target || target == skip || target.Health <= 0f) { continue; }
                if (EntityControl && c.transform.IsChildOf(EntityControl.transform)) { continue; }
                ACO_EscortUnit escort = c.GetComponentInParent<ACO_EscortUnit>();
                if (escort != null && !escort.Hostile) { continue; }

                bool done = false;
                for (int k = 0; k < damagedCount; k++) { if (_splashDamaged[k] == target) { done = true; break; } }
                if (done) { continue; }

                float best = float.MaxValue;
                for (int j = i; j < n; j++)
                {
                    Collider cj = _splashHits[j];
                    if (!cj || cj.GetComponentInParent<SaccTarget>() != target) { continue; }
                    float d = Vector3.Distance(center, cj.bounds.ClosestPoint(center));
                    if (d < best) { best = d; }
                }
                float t = Mathf.Clamp01(best / SplashRadius);
                target.WeaponDamageTarget(Damage * Mathf.Lerp(1f, MinSplashFraction, t), gameObject, WeaponType);
                if (damagedCount < MAX_SPLASH_HITS) { _splashDamaged[damagedCount] = target; damagedCount++; }
            }
        }

        /// 피격 기체를 밀고 기수를 튕긴다. 명중 판정을 하는 클라이언트(=그 기체의 오너, 플레이어 기체면 조종사)에서만 불리므로
        /// 기체 물리가 실제로 돌고 있는 곳에 힘이 들어간다. 키네마틱(NPC 등)이면 건너뛴다.
        private void ApplyImpact()
        {
            Rigidbody rb = _targetEntity.VehicleRigidbody;
            if (!rb || rb.isKinematic) { return; }

            Vector3 dir = transform.forward;
            if (ImpactPush > 0f) { rb.AddForce(dir * ImpactPush, ForceMode.VelocityChange); }

            if (ImpactSpin > 0f)
            {
                // 무게중심에서 비켜 맞은 만큼 그 반대쪽으로 돈다(미사일 진행 방향 × 무게중심까지의 거리). 정통으로 맞으면 무작위 축.
                Vector3 axis = Vector3.Cross(dir, rb.worldCenterOfMass - transform.position);
                if (axis.sqrMagnitude < 0.01f) { axis = Random.onUnitSphere; }
                rb.AddTorque(axis.normalized * (ImpactSpin * Mathf.Deg2Rad), ForceMode.VelocityChange);
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

        /// 후연 방출을 켜거나 끈다. 끌 때는 이미 뿌린 파티클을 그대로 둔다(clear는 발사 시에만).
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

        /// 모든 클라이언트가 같은 미사일을 재현하므로 각자 자기 화면의 타겟 기체 카운터를 올린다.
        /// 카운터는 동기화되지 않는 로컬 값이라(SAV도 동일) 조종 중인 플레이어 자신의 클라이언트 값만 의미가 있다.
        private void StartIncomingWarning()
        {
            if (!SendMissileIncoming || !_targetSAV || _missileIncoming) { return; }

            _targetAnimator = (Animator)_targetSAV.GetProgramVariable("VehicleAnimator");
            if (!IsTargetOccupied()) { return; }

            string key = MissileTypes[_missileType];
            _targetSAV.SetProgramVariable(key, (int)_targetSAV.GetProgramVariable(key) + 1);
            _missileIncoming = true;
            UpdateIncomingAnimator();
            if (Rwr) { Rwr.AddMissile(transform, _targetEntity); }
        }

        /// 타겟을 놓치거나 폭발하면 경고를 내린다. 비어 있는 기체는 SAV가 하차 시 카운터를 0으로 리셋하므로 감소시키지 않는다.
        private void StopIncomingWarning()
        {
            if (!_missileIncoming) { return; }
            _missileIncoming = false;
            if (Rwr) { Rwr.RemoveMissile(transform); }
            if (!_targetSAV) { return; }

            if (IsTargetOccupied())
            {
                string key = MissileTypes[_missileType];
                int count = (int)_targetSAV.GetProgramVariable(key) - 1;
                if (count < 0) { count = 0; }
                _targetSAV.SetProgramVariable(key, count);
            }
            UpdateIncomingAnimator();
        }

        private bool IsTargetOccupied()
        {
            return (bool)_targetSAV.GetProgramVariable("Piloting") || (bool)_targetSAV.GetProgramVariable("Passenger");
        }

        private void UpdateIncomingAnimator()
        {
            if (!_targetAnimator) { return; }
            _targetAnimator.SetInteger(MissileIncomingName, (int)_targetSAV.GetProgramVariable(MissileTypes[_missileType]));
        }

        public void MoveBackToPool()
        {
            // 폭발 경로를 거치지 않고 회수되는 경우에도 경고가 남지 않게 한다.
            StopIncomingWarning();
            if (_targetFlares) { _targetFlares.UnregisterIncoming(this); }

            if (ExplosionObject) { ExplosionObject.SetActive(false); }
            for (int i = 0; i < DisableOnExplode.Length; i++)
            {
                if (DisableOnExplode[i]) { DisableOnExplode[i].SetActive(true); }
            }
            SetTrailEmission(false, false);

            _flying = false;
            _exploded = false;
            _target = null;
            _targetEntity = null;
            _targetStatic = null;
            _targetSAV = null;
            _targetAnimator = null;
            _targetAI = null;
            _targetFlares = null;
            _decoyed = false;
            _decoyFlares = null;
            _decoySAV = null;

            if (PoolParent) { transform.SetParent(PoolParent); }
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            gameObject.SetActive(false);
        }
    }
}
