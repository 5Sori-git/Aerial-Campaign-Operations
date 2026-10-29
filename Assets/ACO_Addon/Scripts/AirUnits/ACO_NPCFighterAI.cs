using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// NPC 전투기 두뇌. 타겟이 없으면 기동 패턴, 타겟이 있으면 추적으로 비행한다.
    /// 씬에 ACO_CombatDirector가 있으면 한 타겟을 동시에 공격하는 수가 제한되고, 자리가 없으면 타겟 주위를 돌며 차례를 기다린다.
    /// 오너만 AI와 비행을 계산한다. 동기화(전송 판단, 원격 예측/보정, 소유권)는 같은 오브젝트의 ACO_NPCFighterNet이 맡는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_NPCFighterAI : UdonSharpBehaviour
    {
        [Header("참조")]
        public SaccEntity EntityControl;
        [Tooltip("체력/데미지/리스폰은 SaccTarget이 소유한다(0.2-1절). ExplodeOther에 이 스크립트를 등록해야 격추 통지를 받는다")]
        public SaccTarget TargetHealth;
        public ACO_NPCFlightController Flight;
        [Tooltip("비우면 미사일을 쏘지 않는다")]
        public ACO_MissileLauncher MissileLauncher;
        [Tooltip("비우면 기총을 쏘지 않는다")]
        public ACO_NPCGun Gun;
        [Tooltip("동기화 담당. 비우면 같은 오브젝트에서 찾는다")]
        public ACO_NPCFighterNet Net;
        [Tooltip("슈퍼에이스면 그 스크립트(자식). 있으면 미션 생성기가 일반 배치에 쓰지 않고 보너스 페이즈만 내보낸다. 프리팹 빌더/Bake가 넣는다")]
        public ACO_SuperAce SuperAce;

        [Header("숙련도 외형 (미션 생성기가 단계를 정한다)")]
        [Tooltip("신병일 때 켤 오브젝트(신병 모델 통째로, 도색 등). 다른 단계에선 꺼진다 — 등급마다 다른 모델을 쓰려면 단계마다 모델을 하나씩 넣는다")]
        public GameObject[] RookieVisuals;
        [Tooltip("베테랑일 때 켤 오브젝트(베테랑 모델 통째로, 도색, 표식 등). 다른 단계에선 꺼진다")]
        public GameObject[] VeteranVisuals;
        [Tooltip("에이스일 때 켤 오브젝트(에이스 모델 통째로, 전용 도색, 트레일 등). 다른 단계에선 꺼진다")]
        public GameObject[] AceVisuals;

        [Header("격추 연출")]
        [Tooltip("격추 시 숨길 오브젝트. SAV의 TargetExplode 애니메이션은 '루트'의 MeshRenderer/MeshCollider만 끄므로 자식에 있는 모델은 여기 넣어야 한다")]
        public GameObject[] HideOnDeath;
        [Tooltip("격추 시 끌 콜라이더. 루트가 BoxCollider면 애니메이션 대상(MeshCollider)이 아니라 꺼지지 않으므로 여기 넣는다")]
        public Collider[] DisableCollidersOnDeath;

        [Header("플레어 (대응책) — 규칙·연출은 플레어 장치(ACO_FlareDispenser)가 맡는다")]
        [Tooltip("플레어 장치(같은 오브젝트). 출격할 때 횟수를 채우고 등급에 맞춰 확률·반응 거리(신병 ×0.7, 에이스 ×1.3)를 넣는다. 비우면 플레어 없음")]
        public ACO_FlareDispenser Flares;
        [Tooltip("숙련도별: 미사일 하나당 플레어를 뿌릴 확률")]
        [Range(0f, 1f)] public float RookieFlareChance = 0.1f;
        [Range(0f, 1f)] public float VeteranFlareChance = 0.5f;
        [Range(0f, 1f)] public float AceFlareChance = 0.9f;

        [Header("부활")]
        [Tooltip("부활 위치. 비우면 시작 위치로 돌아간다")]
        public Transform RespawnPoint;

        [Header("타겟 탐지")]
        [Tooltip("적으로 간주할 레이어. 기본값은 아군 항공기(25)")]
        public LayerMask TargetLayers = 1 << 25;
        public float DetectRange = 2500f;
        [Tooltip("이 거리를 넘어가면 타겟을 놓는다")]
        public float LoseTargetRange = 3500f;
        public float RetargetInterval = 0.5f;

        [Header("추적")]
        [Tooltip("타겟 속도를 이용한 예측 조준 비율. 0이면 현재 위치만 추적")]
        public float LeadFactor = 1f;
        [Tooltip("예측 조준 최대 시간(초). 너무 먼 미래를 예측하면 조준점이 엉뚱한 곳으로 튄다")]
        public float MaxLeadTime = 3f;
        [Tooltip("이보다 가까워지면 공격을 끊고 이탈한다")]
        public float BreakOffDistance = 350f;
        [Tooltip("이탈 후 이 거리까지 벌어지면 다시 공격에 진입한다. 지상 타겟 위에서 선회만 하는 것을 막는 핵심 값")]
        public float ReEngageDistance = 1400f;
        [Tooltip("거리가 안 벌어져도 이 시간이 지나면 이탈을 끝낸다(안전장치)")]
        public float MaxExtendDuration = 15f;
        [Tooltip("이탈 중 최대 상승/하강 각도(도)")]
        public float ExtendClimbAngle = 20f;
        [Tooltip("이탈 시 목표로 할 고도 = 타겟 고도 + 이 값. 항상 상승하면 이탈할 때마다 고도가 쌓이기만 한다")]
        public float ExtendAltitudeAboveTarget = 300f;

        [Header("미사일 발사 조건")]
        public float MissileMaxRange = 2000f;
        [Tooltip("이보다 가까우면 쏘지 않는다 (최소 사거리)")]
        public float MissileMinRange = 300f;
        [Tooltip("기수와 타겟 사이 각도가 이 안에 들어와야 발사한다(도)")]
        public float MissileFireConeAngle = 15f;

        [Header("순찰 (타겟 없을 때)")]
        [Tooltip("순서대로 순회한다. 비우면 대기 지점 선회, 그것도 없으면 자유 기동 패턴")]
        public Transform[] Waypoints;
        [Tooltip("웨이포인트에 이 거리까지 접근하면 다음으로 넘어간다")]
        public float WaypointReachDistance = 250f;
        [Tooltip("웨이포인트가 없을 때 이 지점을 중심으로 선회한다. 고도도 이 지점을 따른다")]
        public Transform HoldPoint;
        public float HoldRadius = 600f;

        [Header("자유 기동 패턴 (웨이포인트/대기지점이 없을 때)")]
        public float PatternMinDuration = 3f;
        public float PatternMaxDuration = 6f;
        [Tooltip("선회 패턴의 초당 방위 변화량(도)")]
        public float PatternTurnRate = 12f;
        public float PatternClimbAngle = 15f;
        public float PatternWeaveAmplitude = 30f;
        public float PatternWeaveFrequency = 0.25f;

        [Header("편대 간격 (겹침 방지)")]
        [Tooltip("빌드 시 ACO_SceneBaker가 자동으로 채운다. 수동: ACO → Bake NPC References")]
        public ACO_NPCFighterAI[] Neighbors;
        [Tooltip("이 거리 안의 다른 NPC 전투기에게서 멀어지도록 기동한다(m)")]
        public float SeparationRadius = 250f;
        [Tooltip("분리 기동의 세기. 클수록 추격보다 간격 유지를 우선한다")]
        public float SeparationWeight = 1.5f;
        public float NeighborCheckInterval = 0.2f;
        [Tooltip("우회 접근 거리(m). 같은 타겟을 노려도 NPC마다 다른 각도로 돌아 들어간다")]
        public float ApproachOffsetDistance = 600f;
        [Tooltip("이 거리 안으로 들어오면 우회를 멈추고 타겟으로 합류한다(m)")]
        public float ApproachConvergeDistance = 1500f;
        [Tooltip("우회에서 합류로 넘어가는 구간 폭(m)")]
        public float ApproachFadeRange = 800f;
        [Tooltip("우회 접근 각도(도). 음수면 배치 위치로 자동 결정 — 모든 클라이언트가 같은 값을 갖는다")]
        public float ApproachSlotAngle = -1f;

        [Header("공격 정원 (ACO_CombatDirector)")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 CombatDirector를 자동으로 넣는다. 비어 있으면 정원 없이 모두 공격한다. 대기 선회 반경/고도와 자기 방어 거리는 디렉터에서 일괄 설정한다")]
        public ACO_CombatDirector Director;

        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다(가까운 적기 플레어 대사)")]
        public ACO_Radio Radio;

        [Header("디버그")]
        [Tooltip("켜면 1초마다 고도/타겟/원하는 상승각/지면 회피 바닥각을 [ACO_FLT]로 로그에 남긴다(오너만). 비행 진단용")]
        public bool DebugFlight = false;

        private const byte MODE_PATROL = 0;
        private const byte MODE_PURSUIT = 1;
        private const byte MODE_EXTEND = 2;
        // 공격 정원이 차서 타겟 주위를 돌며 차례를 기다린다. 미사일/기총을 쓰지 않는다.
        private const byte MODE_WAIT = 3;

        private const byte PAT_STRAIGHT = 0;
        private const byte PAT_TURN_LEFT = 1;
        private const byte PAT_TURN_RIGHT = 2;
        private const byte PAT_CLIMB = 3;
        private const byte PAT_DIVE = 4;
        private const byte PAT_WEAVE = 5;
        private const int PAT_COUNT = 6;

        // 분리 벡터/타겟 속도를 따라가는 시간(초). 계단식으로 바뀌면 기수가 툭툭 꺾인다.
        private const float SEPARATION_SMOOTH_TIME = 0.3f;
        private const float TARGET_VEL_SMOOTH_TIME = 0.25f;

        private bool _initialized;
        private bool _isOwner;
        private bool _alive = true;
        private byte _mode;
        private byte _pattern;
        private Quaternion _patternStartRot = Quaternion.identity;
        private int _patternStartTimeMs;

        private Transform _target;
        private int _targetIdx = -1;
        private Vector3 _lastTargetPos;
        private Vector3 _targetVel;
        private bool _hasLastTargetPos;
        private float _lastTargetSampleTime;
        // 타겟 속도의 출처. SAV 기체는 동기화된 속도를, NPC는 비행 컨트롤러 값을 쓴다(둘 다 없으면 위치 미분).
        private SaccAirVehicle _targetSAV;
        // 표적 기체의 엔티티. 사람이 타 있으면 그 오너가 조종사다(SAV는 탑승할 때 기체 소유권을 가져간다).
        private SaccEntity _targetEntity;
        private ACO_NPCFlightController _targetNPC;

        private float _retargetTimer;
        // 표적 우선순위(AAMTargets 인덱스별, m — 호위 대상 등). 표적이 처음 활성으로 보일 때 한 번 확인한다
        // (풀에서 꺼져 있는 동안엔 부모 컴포넌트를 찾을 수 없으므로).
        private float[] _targetPriority;
        private bool[] _priorityResolved;
        private float _patternTimer;
        private float _extendTimer;
        private float _engageTimer;   // 공격(추격+이탈)을 계속한 시간. 교대 판단용
        private float _yieldTimer;    // 자리를 넘긴 뒤 다시 신청하기까지 남은 시간
        private float _waitStartTime; // 대기 순서(먼저 기다린 NPC부터 투입)
        private int _waypointIdx;

        private float _debugFlightTimer;

        private Vector3 _startPos;
        private Quaternion _startRot;

        // 미션 생성기가 준 집결 지점. 순찰 시 웨이포인트/대기 지점보다 우선한다.
        private bool _missionHold;
        private Vector3 _missionHoldPos;

        // 숙련도: 인스펙터 값(=베테랑 기준)을 시작 시 저장해 두고, 단계 배율을 곱해 적용한다(누적되지 않게).
        public const int SKILL_ROOKIE = 0;
        public const int SKILL_VETERAN = 1;
        public const int SKILL_ACE = 2;
        private int _skillTier = SKILL_VETERAN;

        private bool _hasSkillBase;
        private float _baseTurnG;
        private float _baseMaxSpeed;
        private float _baseCruiseSpeed;
        private float _baseLockTime;
        private float _baseGunDwell;
        private float _baseMissileCone;

        private Vector3 _separation;
        private Vector3 _separationTarget;
        private float _neighborTimer;
        private float _slotAngle;

        void Start()
        {
            Init();
        }

        /// 늦게 들어온 클라이언트는 Start보다 NetState가 먼저 올 수 있어 양쪽에서 부른다.
        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;

            if (!Flight) { Flight = GetComponent<ACO_NPCFlightController>(); }
            if (!EntityControl) { EntityControl = GetComponentInParent<SaccEntity>(); }
            if (!TargetHealth) { TargetHealth = GetComponentInParent<SaccTarget>(); }
            if (!Net) { Net = GetComponent<ACO_NPCFighterNet>(); }
            if (!Flares) { Flares = GetComponent<ACO_FlareDispenser>(); }

            _startPos = transform.position;
            _startRot = transform.rotation;
            _slotAngle = ApproachSlotAngle >= 0f ? ApproachSlotAngle : AutoSlotAngle(_startPos);
            _isOwner = Networking.IsOwner(gameObject);
            _mode = MODE_PATROL;
            _pattern = PAT_STRAIGHT;
            _patternStartRot = transform.rotation;
            _patternStartTimeMs = Networking.GetServerTimeInMilliseconds();

            if (Flight) { Flight.Speed = Flight.CruiseSpeed; }
            if (Net) { Net.ResetMotion(); }
            if (_isOwner) { EnterPatrol(); }
        }

        /// 동기화 스크립트가 상태를 받았을 때 부른다(늦게 들어온 클라이언트는 Start보다 수신이 먼저 올 수 있다).
        public void EnsureInit() { Init(); }

        void Update()
        {
            if (Net) { Net.OwnerCheckTick(Time.deltaTime); }
            if (!_alive || !Flight) { return; }
            float dt = Time.deltaTime;

            if (_isOwner)
            {
                OwnerTick(dt);
                Steer(dt);
                Flight.FlightTick(dt);
                if (DebugFlight) { LogFlight(dt); }
                if (Net) { Net.OwnerTick(dt); }
            }
            else
            {
                if (Net) { Net.RemoteTick(dt); }
                // 원격은 Steer를 돌리지 않으므로 기총 리드용 타겟 속도를 여기서 갱신한다.
                if (_mode == MODE_PURSUIT && _target != null) { UpdateTargetVelocity(_target.position); }
            }

            // 발사 여부는 모든 클라이언트가 로컬에서 판단하고(트래픽 0), 명중 계산은 기총 안에서 타겟 오너만 한다.
            // 원격에서는 예측 자세로 판단하므로 피격자는 자기 화면에 보이는 NPC 기수 방향대로 맞는다.
            if (Gun)
            {
                int gunTarget = (_mode == MODE_PURSUIT && _target != null) ? _targetIdx : -1;
                Gun.GunTick(gunTarget, _targetVel, dt);
            }
        }

        /// SaccTarget이 격추 시 ExplodeOther의 스크립트들에 보내는 이벤트(모든 클라이언트에서 실행).
        /// 모델 숨김과 폭발 파티클은 SaccTarget의 Animator가 처리하므로 여기서는 AI만 멈춘다.
        public void Explode()
        {
            _alive = false;
            ClearTarget();
            if (Gun) { Gun.ForceStop(); }
            SetDeathVisuals(true);
        }

        /// SaccTarget 애니메이터가 처리하지 못하는 부분(자식 모델, 루트가 아닌/MeshCollider가 아닌 콜라이더)을 직접 켜고 끈다.
        private void SetDeathVisuals(bool dead)
        {
            if (HideOnDeath != null)
            {
                for (int i = 0; i < HideOnDeath.Length; i++)
                {
                    if (HideOnDeath[i]) { HideOnDeath[i].SetActive(!dead); }
                }
            }
            if (DisableCollidersOnDeath != null)
            {
                for (int i = 0; i < DisableCollidersOnDeath.Length; i++)
                {
                    if (DisableCollidersOnDeath[i]) { DisableCollidersOnDeath[i].enabled = !dead; }
                }
            }
            // 단계별 모델을 HideOnDeath에 직접 넣었어도 부활 때 다른 단계 모델이 같이 켜지지 않게.
            if (!dead && _hasSkillBase) { ApplyTierVisuals(); }
        }

        /// 부활 진입점. SaccTarget의 RespawnDelay는 0으로 두고(자동 부활 안 함) 스폰/웨이브 매니저가 이걸 호출한다.
        public void Respawn()
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetRespawn));
        }

        public void NetRespawn()
        {
            if (RespawnPoint) { RespawnAt(RespawnPoint.position, RespawnPoint.rotation); }
            else { RespawnAt(_startPos, _startRot); }
        }

        // ───────────── 미션 생성기 연동 (ACO_MissionGenerator가 모든 클라이언트에서 호출) ─────────────

        /// 미션 스폰. ingress는 진입 후 선회 대기할 집결 지점이다(웨이포인트/대기 지점보다 우선).
        /// revive가 false면(늦게 들어온 클라이언트) 집결 지점만 받는다 — 위치는 이 NPC의 동기화가, 생사는 SaccTarget 동기화가 맞춘다.
        public void MissionSpawn(Vector3 position, Quaternion rotation, Vector3 ingress, bool revive)
        {
            Init();
            _missionHold = true;
            _missionHoldPos = ingress;
            if (revive) { RespawnAt(position, rotation); return; }

            // 부활 없이 받는 경우(늦게 들어온 클라이언트): 월드 시작 시 풀 숨김(MissionDespawn)으로 꺼진 상태를
            // 실제 생사(SaccTarget 체력)에 맞춰 되돌린다. 안 그러면 이 클라이언트에서만 NPC가 멈춰 있다.
            // 위치는 이 NPC의 동기화가, 이미 격추된 경우의 연출은 SaccTarget의 격추 이벤트가 맞춘다.
            _alive = TargetHealth == null || TargetHealth.Health > 0f;
        }

        /// 숙련도 적용(모든 클라이언트). 비행/락온은 오너에서, 기총 체류 시간은 피격자 쪽에서 쓰이므로 모두가 같은 값을 가져야 한다.
        /// 배율은 인스펙터 값 기준이라 여러 번 불러도 누적되지 않는다.
        public void ApplySkill(int tier, float turnMul, float speedMul, float lockTimeMul, float gunDwellMul, float missileConeMul)
        {
            Init();
            if (!_hasSkillBase)
            {
                _hasSkillBase = true;
                if (Flight)
                {
                    _baseTurnG = Flight.TurnG;
                    _baseMaxSpeed = Flight.MaxSpeed;
                    _baseCruiseSpeed = Flight.CruiseSpeed;
                }
                if (MissileLauncher) { _baseLockTime = MissileLauncher.LockTime; }
                if (Gun) { _baseGunDwell = Gun.DwellTime; }
                _baseMissileCone = MissileFireConeAngle;
            }

            _skillTier = tier;
            if (Flight)
            {
                Flight.TurnG = _baseTurnG * turnMul;
                Flight.MaxSpeed = _baseMaxSpeed * speedMul;
                Flight.CruiseSpeed = _baseCruiseSpeed * speedMul;
            }
            if (MissileLauncher) { MissileLauncher.LockTime = _baseLockTime * lockTimeMul; }
            if (Gun) { Gun.DwellTime = _baseGunDwell * gunDwellMul; }
            MissileFireConeAngle = _baseMissileCone * missileConeMul;

            ApplyTierVisuals();
            // 플레어: 확률은 숙련도별 값, 반응 거리는 신병은 늦게(×0.7) 에이스는 일찍(×1.3).
            if (Flares)
            {
                Flares.FlareChance = FlareChanceForTier();
                Flares.ReactDistanceMultiplier = tier == SKILL_ROOKIE ? 0.7f : (tier == SKILL_ACE ? 1.3f : 1f);
            }
        }

        public int GetSkillTier() { return _skillTier; }

        /// 숙련도 단계의 외형만 켠다(단계별 모델 교체). 부활할 때 HideOnDeath가 모델을 다시 켜므로 그 뒤에도 다시 부른다.
        private void ApplyTierVisuals()
        {
            SetVisuals(RookieVisuals, _skillTier == SKILL_ROOKIE);
            SetVisuals(VeteranVisuals, _skillTier == SKILL_VETERAN);
            SetVisuals(AceVisuals, _skillTier == SKILL_ACE);
        }

        private void SetVisuals(GameObject[] objects, bool on)
        {
            if (objects == null) { return; }
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i]) { objects[i].SetActive(on); }
            }
        }

        /// 이번 미션에 쓰지 않는다(월드 시작 시 풀 숨김 포함). 생성기가 곧 오브젝트를 끈다.
        public void MissionDespawn()
        {
            _alive = false;
            ClearTarget();
            if (Gun) { Gun.ForceStop(); }
            if (MissileLauncher) { MissileLauncher.ReleaseAim(); }
        }

        private void RespawnAt(Vector3 position, Quaternion rotation)
        {
            _isOwner = Net != null ? Net.RefreshOwnerSilent() : Networking.IsOwner(gameObject);

            // 체력 복구는 한 클라이언트만 해야 한다(RespawnTarget이 내부에서 소유권을 가져가고 동기화한다).
            if (_isOwner && TargetHealth) { TargetHealth.RespawnTarget(); }

            transform.position = position;
            transform.rotation = rotation;

            if (Flight) { Flight.Speed = Flight.CruiseSpeed; }
            if (Net) { Net.ResetMotion(); }
            _extendTimer = 0f;
            _engageTimer = 0f;
            _yieldTimer = 0f;
            if (Flares) { Flares.ResetSalvos(); }
            SetDeathVisuals(false);
            _alive = true;
            if (_isOwner)
            {
                EnterPatrol();
                if (Net) { Net.SendStateAfterRespawn(); }
            }
        }

        private void OwnerTick(float dt)
        {
            if (_mode == MODE_PURSUIT || _mode == MODE_EXTEND) { _engageTimer += dt; }
            if (_yieldTimer > 0f) { _yieldTimer -= dt; }

            _retargetTimer -= dt;
            bool retargetTick = _retargetTimer <= 0f;
            if (retargetTick)
            {
                _retargetTimer = RetargetInterval;
                AcquireTarget();
            }

            if (_mode == MODE_PURSUIT)
            {
                if (!_target)
                {
                    EnterPatrol();
                }
                else if (ShouldYield())
                {
                    // 오래 붙어 있었고 기다리는 NPC가 있으면 자리를 넘긴다 — 같은 NPC만 계속 덤비지 않게.
                    _yieldTimer = Director.YieldCooldown;
                    EnterWait();
                }
                else if (Director && !TargetInDefendRange() && Director.ShouldStepDown(this, _targetIdx))
                {
                    // 자기 방어로 정원을 넘겨 교전했는데 위협이 지나갔다 — 가장 늦게 합류한 쪽이 대기로 돌아간다.
                    EnterWait();
                }
                else if (Vector3.Distance(transform.position, _target.position) < BreakOffDistance
                         && Vector3.Dot(transform.forward, _target.position - transform.position) > 0f)
                {
                    // 공격을 끊고 이탈. 시간이 아니라 거리로 복귀를 판단해야
                    // 지상 타겟 위에서 선회만 반복하는 상황을 피할 수 있다. 이탈 중에도 공격 자리는 유지한다.
                    _extendTimer = MaxExtendDuration;
                    if (MissileLauncher) { MissileLauncher.ReleaseAim(); }
                    SetState(MODE_EXTEND, _pattern);
                }
                else
                {
                    TryFireMissile(dt);
                }
            }
            else if (_mode == MODE_EXTEND)
            {
                _extendTimer -= dt;

                if (!_target) { EnterPatrol(); }
                else if (Vector3.Distance(transform.position, _target.position) >= ReEngageDistance
                         || _extendTimer <= 0f)
                {
                    SetState(MODE_PURSUIT, _pattern);
                }
            }
            else if (_mode == MODE_WAIT)
            {
                if (!_target) { EnterPatrol(); }
                // 자리 확인은 재탐색 주기에만 한다. 자기 방어는 즉시.
                else if (TargetInDefendRange() || (retargetTick && CanAttackNow())) { Engage(); }
            }
            else
            {
                if (_target) { TryEngage(); }
                else { PatrolTick(dt); }
            }
        }

        // ───────────── 공격 정원 ─────────────

        /// 공격 자리를 받을 수 있는지. 정원 관리자가 없으면 항상 공격한다.
        private bool CanAttackNow()
        {
            if (!Director || _targetIdx < 0) { return true; }
            if (TargetInDefendRange()) { return true; }
            if (_yieldTimer > 0f) { return false; }
            return Director.CanAttack(this, _targetIdx);
        }

        private bool TargetInDefendRange()
        {
            if (!Director || _target == null) { return false; }
            return Vector3.Distance(transform.position, _target.position) <= Director.DefendDistance;
        }

        private void TryEngage()
        {
            if (CanAttackNow()) { Engage(); }
            else { EnterWait(); }
        }

        private void Engage()
        {
            _engageTimer = 0f;
            SetState(MODE_PURSUIT, _pattern);
        }

        private void EnterWait()
        {
            if (MissileLauncher) { MissileLauncher.ReleaseAim(); }
            _waitStartTime = Time.time;
            SetState(MODE_WAIT, _pattern);
        }

        public float GetWaitStartTime() { return _waitStartTime; }
        public float GetEngageTime() { return _engageTimer; }
        public bool IsLocallyOwned() { return _isOwner; }

        private bool ShouldYield()
        {
            if (!Director || Director.MaxEngageTime <= 0f) { return false; }
            if (_engageTimer < Director.MaxEngageTime) { return false; }
            if (TargetInDefendRange()) { return false; }
            return Director.HasWaiting(_targetIdx, this);
        }

        /// CombatDirector가 정원을 셀 때 쓴다. 원격 NPC도 동기화된 모드/타겟으로 같은 답을 낸다.
        public bool IsAttacking(int targetIdx)
        {
            return _alive && _targetIdx == targetIdx
                && (_mode == MODE_PURSUIT || _mode == MODE_EXTEND)
                && gameObject.activeInHierarchy;
        }

        /// 이 타겟을 기다리며 바로 투입될 수 있는지. 교대 직후 쿨다운 중인 NPC는 세지 않는다 —
        /// 세면 공격 중인 NPC가 자리를 넘겨도 아무도 받지 못해 공격이 비는 구간이 생긴다.
        /// (쿨다운은 동기화하지 않으므로 다른 오너의 NPC는 항상 투입 가능으로 본다.)
        public bool IsWaitingOn(int targetIdx)
        {
            return _alive && _targetIdx == targetIdx && _mode == MODE_WAIT && _yieldTimer <= 0f
                && gameObject.activeInHierarchy;
        }

        /// 오너만 호출한다. 락온 누적과 경고 전송은 런처가 담당하므로 여기서는 "조준 중인지"만 알린다.
        private void TryFireMissile(float dt)
        {
            if (!MissileLauncher || _targetIdx < 0 || !_target) { return; }

            Vector3 toTarget = _target.position - transform.position;
            float dist = toTarget.magnitude;
            bool inEnvelope = dist <= MissileMaxRange
                && dist >= MissileMinRange
                && Vector3.Angle(transform.forward, toTarget) <= MissileFireConeAngle;

            if (!inEnvelope)
            {
                MissileLauncher.ReleaseAim();
                return;
            }

            MissileLauncher.AimAt(_targetIdx, dt);
            if (MissileLauncher.IsLocked() && MissileLauncher.CanFire())
            {
                MissileLauncher.FireAt(_targetIdx);
            }
        }

        private void Steer(float dt)
        {
            if (_mode == MODE_PURSUIT && _target)
            {
                Vector3 targetPos = _target.position;
                UpdateTargetVelocity(targetPos);

                Vector3 aimPoint;
                // 기총 사거리 안에서는 탄속 기준 리드로 조준한다. 미사일용 요격점(내 기체 속력 기준)을 향하면
                // 훨씬 빠른 총탄이 타겟 앞을 지나간다.
                if (Gun && Vector3.Distance(transform.position, targetPos) <= Gun.MaxRange)
                {
                    aimPoint = Gun.LeadPoint(targetPos, _targetVel);
                }
                else
                {
                    aimPoint = targetPos + _targetVel * (InterceptTime(targetPos) * LeadFactor);
                    aimPoint += ApproachOffset(targetPos);
                }

                Flight.DesiredDirection = aimPoint - transform.position;
                Flight.DesiredSpeed = Flight.MaxSpeed;
            }
            else if (_mode == MODE_EXTEND && _target)
            {
                // 타겟 반대 방향 + 상승. 지상 타겟이면 이 조합이 자연스럽게 "치고 빠지는" 궤적이 된다.
                Vector3 away = transform.position - _target.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.000001f) { away = transform.forward; away.y = 0f; }
                if (away.sqrMagnitude < 0.000001f) { away = Vector3.forward; }
                away = away.normalized;

                // 타겟보다 약간 높은 고도로 수렴한다 — 높이 우위는 잡되 이탈마다 상승이 누적되지 않게.
                float altError = (_target.position.y + ExtendAltitudeAboveTarget) - transform.position.y;
                float climbAngle = Mathf.Clamp(altError * 0.05f, -ExtendClimbAngle, ExtendClimbAngle);
                Flight.DesiredDirection = (away + Vector3.up * Mathf.Tan(climbAngle * Mathf.Deg2Rad)).normalized;
                Flight.DesiredSpeed = Flight.MaxSpeed;
            }
            else if (_mode == MODE_WAIT && _target && Director)
            {
                // 타겟을 중심으로 돌며 기다린다. 반경이 LoseTargetRange를 넘으면 기다리다 타겟을 놓친다.
                Vector3 center = _target.position;
                float radius = Mathf.Min(Director.WaitOrbitRadius, LoseTargetRange * 0.8f);
                Flight.DesiredDirection = OrbitDirection(center, radius, center.y + Director.WaitAltitudeAboveTarget);
                // 빠른 타겟을 놓치지 않도록 궤도에서 많이 벗어나면 최대 속도로 따라간다.
                Vector3 flat = transform.position - center;
                flat.y = 0f;
                Flight.DesiredSpeed = flat.magnitude > radius * 1.3f ? Flight.MaxSpeed : Flight.CruiseSpeed;
            }
            else
            {
                Flight.DesiredDirection = PatrolDirection();
                Flight.DesiredSpeed = Flight.CruiseSpeed;
            }

            UpdateSeparation(dt);
            if (_separation.sqrMagnitude > 0.000001f)
            {
                Flight.DesiredDirection = Flight.DesiredDirection.normalized + _separation * SeparationWeight;
            }
        }

        /// 다른 NPC가 가까울수록 강하게 밀어내는 벡터(최대 길이 ≈ 이웃 수). 위치만으로 계산하므로 원격 클라이언트도 같은 결과를 낸다.
        private void UpdateSeparation(float dt)
        {
            _neighborTimer -= dt;
            if (_neighborTimer <= 0f)
            {
                _neighborTimer = NeighborCheckInterval;
                _separationTarget = ComputeSeparation();
            }
            // 검사 주기마다 계단식으로 바뀌면 기수가 툭툭 꺾여 보이고 원격 예측도 틀어진다.
            _separation = Vector3.Lerp(_separation, _separationTarget, 1f - Mathf.Exp(-dt / SEPARATION_SMOOTH_TIME));
        }

        private Vector3 ComputeSeparation()
        {
            Vector3 sum = Vector3.zero;
            if (Neighbors == null) { return sum; }

            Vector3 myPos = transform.position;
            float radiusSq = SeparationRadius * SeparationRadius;

            for (int i = 0; i < Neighbors.Length; i++)
            {
                ACO_NPCFighterAI other = Neighbors[i];
                if (!other || !other.IsActiveFighter()) { continue; }

                Vector3 away = myPos - other.transform.position;
                float distSq = away.sqrMagnitude;
                if (distSq >= radiusSq || distSq < 0.0001f) { continue; }

                float dist = Mathf.Sqrt(distSq);
                sum += (away / dist) * (1f - dist / SeparationRadius);
            }
            return sum;
        }

        /// 같은 타겟을 노리는 NPC들이 서로 다른 각도로 돌아 들어가게 한다. 사거리 안에 들어오면 0으로 줄어 타겟에 합류한다.
        private Vector3 ApproachOffset(Vector3 targetPos)
        {
            if (ApproachOffsetDistance <= 0f) { return Vector3.zero; }

            Vector3 los = targetPos - transform.position;
            float dist = los.magnitude;
            float weight = Mathf.Clamp01((dist - ApproachConvergeDistance) / Mathf.Max(ApproachFadeRange, 1f));
            if (weight <= 0f) { return Vector3.zero; }

            Vector3 losDir = los / dist;
            Vector3 right = Vector3.Cross(Vector3.up, losDir);
            if (right.sqrMagnitude < 0.000001f) { return Vector3.zero; }
            right = right.normalized;
            Vector3 up = Vector3.Cross(losDir, right);

            // 위아래 성분은 절반만 — 아래쪽 우회가 지형과 겹치지 않게.
            float rad = _slotAngle * Mathf.Deg2Rad;
            Vector3 offsetDir = right * Mathf.Cos(rad) + up * (Mathf.Sin(rad) * 0.5f);
            return offsetDir * (ApproachOffsetDistance * weight);
        }

        /// 배치 위치로 각도를 정한다. 삼각함수 대신 정수 해시를 쓰는 이유: 모든 클라이언트에서 비트 단위로 같은 값이 나와야 한다.
        private float AutoSlotAngle(Vector3 pos)
        {
            int h = Mathf.RoundToInt(pos.x) * 73856093
                  ^ Mathf.RoundToInt(pos.y) * 19349663
                  ^ Mathf.RoundToInt(pos.z) * 83492791;
            return (h & 0x7fffffff) % 360;
        }

        public bool IsActiveFighter()
        {
            return _alive && gameObject.activeInHierarchy;
        }

        /// 순찰 방향: 웨이포인트 → 대기 지점 선회 → 자유 기동 패턴 순으로 우선한다.
        private Vector3 PatrolDirection()
        {
            if (_missionHold)
            {
                // 집결 지점까지는 곧장 날아가고(고도도 같이 맞춘다), 가까워지면 그 주위를 선회하며 대기한다.
                Vector3 to = _missionHoldPos - transform.position;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.magnitude > HoldRadius * 3f) { return to; }
                return OrbitDirection(_missionHoldPos, HoldRadius, _missionHoldPos.y);
            }

            if (Waypoints != null && Waypoints.Length > 0)
            {
                Transform wp = CurrentWaypoint();
                if (wp) { return wp.position - transform.position; }
            }

            if (HoldPoint) { return OrbitDirection(HoldPoint.position, HoldRadius, HoldPoint.position.y); }

            return PatternDirection();
        }

        private Transform CurrentWaypoint()
        {
            if (Waypoints == null || Waypoints.Length == 0) { return null; }
            if (_waypointIdx < 0 || _waypointIdx >= Waypoints.Length) { _waypointIdx = 0; }
            return Waypoints[_waypointIdx];
        }

        /// center 주위를 선회한다. 접선 방향에 반경 오차를 섞어 원 궤도로 수렴시킨다. 대기 지점과 공격 대기 선회가 함께 쓴다.
        private Vector3 OrbitDirection(Vector3 center, float radius, float altitude)
        {
            Vector3 toSelf = transform.position - center;
            toSelf.y = 0f;

            float r = toSelf.magnitude;
            Vector3 radial = r > 0.01f ? toSelf / r : transform.forward;
            radial.y = 0f;
            if (radial.sqrMagnitude < 0.000001f) { radial = Vector3.forward; }
            radial = radial.normalized;

            Vector3 tangent = Vector3.Cross(Vector3.up, radial);
            float radiusError = Mathf.Clamp((radius - r) / Mathf.Max(radius, 1f), -1f, 1f);

            Vector3 dir = tangent + radial * radiusError;
            dir.y = Mathf.Clamp((altitude - transform.position.y) * 0.01f, -0.4f, 0.4f);
            return dir;
        }

        /// 오너만 호출한다. 웨이포인트 도달/패턴 교체를 판단한다.
        private void PatrolTick(float dt)
        {
            if (_missionHold) { return; }

            if (Waypoints != null && Waypoints.Length > 0)
            {
                Transform wp = CurrentWaypoint();
                if (wp && Vector3.Distance(transform.position, wp.position) <= WaypointReachDistance)
                {
                    _waypointIdx = (_waypointIdx + 1) % Waypoints.Length;
                    MarkNetDirty();
                }
                return;
            }

            if (HoldPoint) { return; }

            _patternTimer -= dt;
            if (_patternTimer <= 0f) { PickNewPattern(); }
        }

        private void EnterPatrol()
        {
            ClearTarget();
            if (_missionHold) { SetState(MODE_PATROL, _pattern); }
            else if (Waypoints != null && Waypoints.Length > 0) { SetState(MODE_PATROL, _pattern); }
            else if (HoldPoint) { SetState(MODE_PATROL, _pattern); }
            else { PickNewPattern(); }
        }

        /// 타겟 속도. 원격 플레이어 기체의 위치는 네트워크 보간 때문에 프레임마다 흔들려서, 위치를 미분하면
        /// 속도가 튀고 리드 조준점(속도 × 최대 3초)이 크게 흔들린다 → NPC 기수가 떨린다. 그래서 가능한 한 직접 받는다.
        private void UpdateTargetVelocity(Vector3 targetPos)
        {
            float now = Time.time;
            float elapsed = now - _lastTargetSampleTime;

            Vector3 raw;
            bool hasRaw = true;
            if (_targetSAV != null) { raw = _targetSAV.CurrentVel; }   // SAV_SyncScript가 동기화·평활해 둔 속도
            else if (_targetNPC != null) { raw = _targetNPC.transform.forward * _targetNPC.Speed; }
            else if (_hasLastTargetPos && elapsed > 0f && elapsed < 0.2f) { raw = (targetPos - _lastTargetPos) / elapsed; }
            else
            {
                // 이탈 등으로 추격이 끊겼다 재개되면 몇 초 전 위치로 속도를 계산해 수만 m/s가 나오므로 버린다.
                raw = Vector3.zero;
                hasRaw = false;
            }

            if (!hasRaw) { _targetVel = Vector3.zero; }
            else if (!_hasLastTargetPos || elapsed <= 0f || elapsed >= 0.2f) { _targetVel = raw; }
            else { _targetVel = Vector3.Lerp(_targetVel, raw, 1f - Mathf.Exp(-elapsed / TARGET_VEL_SMOOTH_TIME)); }

            _lastTargetPos = targetPos;
            _lastTargetSampleTime = now;
            _hasLastTargetPos = true;
        }

        /// 내 속력으로 타겟과 만나는 가장 이른 시각. |D + V·t| = s·t 를 t에 대해 푼다 (D = 타겟-나, V = 타겟 속도).
        /// "거리 / 내 속도"로 근사하면 정면 접근 시 접근 속도를 무시해 조준점이 내 뒤로 넘어가고, 반대로 선회하게 된다.
        private float InterceptTime(Vector3 targetPos)
        {
            Vector3 d = targetPos - transform.position;
            float speed = Mathf.Max(Flight.Speed, 1f);

            float a = Vector3.Dot(_targetVel, _targetVel) - speed * speed;
            float b = 2f * Vector3.Dot(d, _targetVel);
            float c = Vector3.Dot(d, d);

            float t = 0f;
            if (Mathf.Abs(a) < 0.0001f)
            {
                if (b < 0f) { t = -c / b; }
            }
            else
            {
                float disc = b * b - 4f * a * c;
                // 해가 없으면 따라잡을 수 없는 상황 — 순수 추적(예측 0)으로 둔다.
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

        /// 패턴 시작 자세와 경과 시간만으로 결정되므로 오너와 원격이 같은 경로를 그린다.
        private Vector3 PatternDirection()
        {
            // int 뺄셈이라 서버 시간이 오버플로로 넘어가도 짧은 구간의 경과 시간은 정상이다.
            float t = (Networking.GetServerTimeInMilliseconds() - _patternStartTimeMs) * 0.001f;
            if (t < 0f) { t = 0f; }

            Vector3 baseForward = _patternStartRot * Vector3.forward;
            Vector3 flat = new Vector3(baseForward.x, 0f, baseForward.z);
            if (flat.sqrMagnitude < 0.000001f) { flat = Vector3.forward; }
            flat = flat.normalized;

            if (_pattern == PAT_TURN_LEFT)
            {
                return Quaternion.AngleAxis(-PatternTurnRate * t, Vector3.up) * flat;
            }
            if (_pattern == PAT_TURN_RIGHT)
            {
                return Quaternion.AngleAxis(PatternTurnRate * t, Vector3.up) * flat;
            }
            if (_pattern == PAT_CLIMB)
            {
                return (flat + Vector3.up * Mathf.Tan(PatternClimbAngle * Mathf.Deg2Rad)).normalized;
            }
            if (_pattern == PAT_DIVE)
            {
                return (flat - Vector3.up * Mathf.Tan(PatternClimbAngle * Mathf.Deg2Rad)).normalized;
            }
            if (_pattern == PAT_WEAVE)
            {
                float angle = Mathf.Sin(t * PatternWeaveFrequency * Mathf.PI * 2f) * PatternWeaveAmplitude;
                return Quaternion.AngleAxis(angle, Vector3.up) * flat;
            }
            return flat;
        }

        private void AcquireTarget()
        {
            if (!EntityControl) { return; }
            GameObject[] targets = EntityControl.AAMTargets;
            if (targets == null) { return; }

            Vector3 myPos = transform.position;
            if (_targetPriority == null || _targetPriority.Length != targets.Length)
            {
                _targetPriority = new float[targets.Length];
                _priorityResolved = new bool[targets.Length];
            }

            if (_target)
            {
                bool stillValid = _target.gameObject.activeInHierarchy
                    && Vector3.Distance(myPos, _target.position) <= LoseTargetRange;
                // 대기 중이면 정원이 빈 다른 타겟이 있는지 다시 본다 — 플레이어가 여럿일 때 대기열이 한쪽에 고이지 않게.
                // 우선순위가 더 높은 표적(호위 대상)이 탐지 거리에 들어오면 교전 중이어도 다시 고른다.
                if (stillValid && _mode != MODE_WAIT && !HigherPriorityInRange(targets, myPos)) { return; }
                if (!stillValid) { ClearTarget(); }
            }

            int mask = TargetLayers;
            int bestIdx = -1;
            float bestScore = float.MaxValue;

            for (int i = 0; i < targets.Length; i++)
            {
                GameObject g = targets[i];
                if (!g || !g.activeInHierarchy) { continue; }
                if (((1 << g.layer) & mask) == 0) { continue; }

                float d = Vector3.Distance(myPos, g.transform.position);
                // 지금 기다리는 타겟은 탐지 거리 밖(놓침 거리 안)이어도 후보로 남긴다.
                if (d > DetectRange && i != _targetIdx) { continue; }

                // 정원이 찬 타겟은 그만큼 멀리 있는 것처럼 본다 → 비어 있는 타겟으로 흩어진다.
                float score = d - PriorityOf(i, g);
                if (Director && Director.IsTargetFull(i, this)) { score += Director.FullTargetPenalty; }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0 || bestIdx == _targetIdx) { return; }
            BindTarget(bestIdx);
            // 새 타겟은 자리부터 확인한다(이탈 중이었어도 — 자리 없이 재진입하면 정원을 넘는다).
            TryEngage();
        }

        /// 표적 i의 우선순위(m): 마커 부모의 ACO_EscortUnit.AttackPriority, 아니면 0. g는 활성이어야 한다.
        private float PriorityOf(int i, GameObject g)
        {
            if (!_priorityResolved[i])
            {
                _priorityResolved[i] = true;
                ACO_EscortUnit escort = g.GetComponentInParent<ACO_EscortUnit>();
                _targetPriority[i] = escort != null ? Mathf.Max(escort.AttackPriority, 0f) : 0f;
            }
            return _targetPriority[i];
        }

        /// 지금 표적보다 우선순위가 높은 표적이 탐지 거리 안에 살아 있는지.
        private bool HigherPriorityInRange(GameObject[] targets, Vector3 myPos)
        {
            float current = (_targetIdx >= 0 && _targetIdx < _targetPriority.Length) ? _targetPriority[_targetIdx] : 0f;
            int mask = TargetLayers;
            float rangeSq = DetectRange * DetectRange;
            for (int i = 0; i < targets.Length; i++)
            {
                if (i == _targetIdx) { continue; }
                GameObject g = targets[i];
                if (!g || !g.activeInHierarchy || ((1 << g.layer) & mask) == 0) { continue; }
                if ((g.transform.position - myPos).sqrMagnitude > rangeSq) { continue; }
                if (PriorityOf(i, g) > current) { return true; }
            }
            return false;
        }

        private void BindTarget(int idx)
        {
            _targetIdx = idx;
            _target = null;
            _targetSAV = null;
            _targetEntity = null;
            _targetNPC = null;
            _hasLastTargetPos = false;
            _targetVel = Vector3.zero;
            MarkNetDirty();

            if (idx < 0 || !EntityControl) { return; }
            GameObject[] targets = EntityControl.AAMTargets;
            if (targets == null || idx >= targets.Length) { return; }

            GameObject g = targets[idx];
            if (!g) { return; }
            _target = g.transform;

            SaccEntity entity = g.GetComponentInParent<SaccEntity>();
            _targetEntity = entity;
            // SAV 확장 목록에서 찾는다(GetComponentInChildren<U# 타입>은 프로그램이 빠진 UdonBehaviour가 있으면 예외로 멈춘다)
            if (entity != null) { _targetSAV = (SaccAirVehicle)entity.GetExtention(GetUdonTypeName<SaccAirVehicle>()); }
            _targetNPC = g.GetComponentInParent<ACO_NPCFlightController>();
        }

        private void ClearTarget()
        {
            _target = null;
            _targetSAV = null;
            _targetEntity = null;
            _targetNPC = null;
            _targetIdx = -1;
            _hasLastTargetPos = false;
            _targetVel = Vector3.zero;
            MarkNetDirty();
        }

        private void PickNewPattern()
        {
            byte p = (byte)Random.Range(0, PAT_COUNT);
            // 저고도에서 하강 패턴을 뽑으면 지면 회피와 싸우게 되므로 상승으로 바꾼다.
            if (p == PAT_DIVE && Flight && transform.position.y - Flight.GroundHeight < Flight.MinAltitude + 200f)
            {
                p = PAT_CLIMB;
            }

            _patternTimer = Random.Range(PatternMinDuration, PatternMaxDuration);
            SetState(MODE_PATROL, p);
        }

        /// 모드/패턴 전환. 패턴 기준점은 여기서만 정한다(전송 시점과 무관 — 패턴은 오너만 계산한다).
        private void SetState(byte mode, byte pattern)
        {
            _mode = mode;
            _pattern = pattern;
            _patternStartRot = transform.rotation;
            _patternStartTimeMs = Networking.GetServerTimeInMilliseconds();
            MarkNetDirty();
        }

        // ───────────── 동기화 연동 (ACO_NPCFighterNet) ─────────────

        private void MarkNetDirty()
        {
            if (Net) { Net.MarkStateDirty(); }
        }

        // ───────────── 플레어 (ACO_FlareDispenser) ─────────────

        private float FlareChanceForTier()
        {
            if (_skillTier == SKILL_ROOKIE) { return RookieFlareChance; }
            if (_skillTier == SKILL_ACE) { return AceFlareChance; }
            return VeteranFlareChance;
        }

        public byte GetNetMode() { return _mode; }
        public int GetNetWaypoint() { return _waypointIdx; }
        public int GetNetTargetIdx() { return _targetIdx; }
        public bool IsAlive() { return _alive; }

        /// 지금 표적과 싸우는 중인지(추격 또는 이탈 후 재진입). 대기 선회는 교전이 아니다.
        public bool IsEngaged()
        {
            return _alive && _target != null && (_mode == MODE_PURSUIT || _mode == MODE_EXTEND);
        }

        /// 표적 기체의 조종사. 빈 기체나 NPC(호위 대상 등)면 null.
        public VRCPlayerApi GetTargetPilot()
        {
            if (_targetEntity == null || !_targetEntity.Occupied) { return null; }
            return Networking.GetOwner(_targetEntity.gameObject);
        }

        /// 원격: 오너가 보낸 AI 상태를 적용한다.
        public void ApplyNetState(byte mode, int waypoint, int targetIdx)
        {
            _mode = mode;
            _waypointIdx = waypoint;
            if (targetIdx != _targetIdx) { BindTarget(targetIdx); }
        }

        /// 소유권이 바뀌었다. 운동 상태는 동기화 스크립트가 넘겨받고, 여기서는 AI만 이어받는다.
        public void OnNetOwnershipChanged(bool owner)
        {
            _isOwner = owner;
            // 패턴은 동기화하지 않으므로 순찰이면 새로 고른다.
            if (owner && _mode == MODE_PATROL) { EnterPatrol(); }
        }

        /// 비행 진단: 하강을 안 하는 등 기동이 이상할 때 원인(타겟 선택 / 지면 회피 / 조준점)을 가려낸다.
        private void LogFlight(float dt)
        {
            _debugFlightTimer -= dt;
            if (_debugFlightTimer > 0f) { return; }
            _debugFlightTimer = 1f;

            string targetInfo = "none";
            if (_target != null)
            {
                targetInfo = _target.root.name
                    + " tY=" + _target.position.y.ToString("F0")
                    + " dist=" + Vector3.Distance(transform.position, _target.position).ToString("F0")
                    + " tVelY=" + _targetVel.y.ToString("F0");
            }
            float pitch = Mathf.Asin(Mathf.Clamp(transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            Debug.Log("[ACO_FLT] " + gameObject.name
                + " t=" + Time.time.ToString("F1")
                + " mode=" + _mode
                + " y=" + transform.position.y.ToString("F0")
                + " clear=" + Flight.GetClearance().ToString("F0")
                + " want=" + Flight.LastWantAngle.ToString("F1")
                + " floor=" + Flight.LastFloorAngle.ToString("F1")
                + " pitch=" + pitch.ToString("F1")
                + " bank=" + Flight.GetBankAngle().ToString("F0")
                + " spd=" + Flight.Speed.ToString("F0")
                + " sep=" + _separation.magnitude.ToString("F2")
                + " target=" + targetInfo);
        }
    }
}
