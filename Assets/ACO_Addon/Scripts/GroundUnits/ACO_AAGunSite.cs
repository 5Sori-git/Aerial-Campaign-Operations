using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 고정형 대공포. 사거리 내 항공기를 탐지해 포탑을 리드 조준점으로 돌리고, ACO_NPCGun으로 사격한다.
    /// 탐지/포탑/격추/부활 구조는 ACO_SAMSite와 같고, 사격과 데미지 판정은 NPC 기총(조준 체류 방식, 타겟 오너가 판정)을 그대로 쓴다.
    ///
    /// 회피 설계: 포탑은 TrackingLag만큼 늦은 타겟 속도로 리드를 잡고, 기총의 명중 판정은 "현재" 속도로 잡은 리드와 비교한다.
    /// 직진/완만한 선회는 둘이 일치해 맞고, 급기동하면 둘이 벌어져 빗나간다 — 기동으로 피할 수 있는 대공포.
    ///
    /// 동기화는 타겟 인덱스뿐이다. 포탑 각도와 발포 여부는 각 클라이언트가 자기 화면의 타겟 위치로 계산하므로,
    /// 피격자(타겟 오너)는 자기 화면에서 포신이 자신을 향할 때만 맞는다.
    /// 원거리 잠재우기: 조종 중인 플레이어 기체가 (탐지 거리 + SleepMargin) 밖이면 탐지/조준/기총을 멈추고 SleepCheckInterval마다 깨울지만 본다.
    ///
    /// 미사일 요격(CIWS, InterceptProtectLayers가 0이 아닐 때): 판정은 미사일(ACO_Missile)이 한다 — 미사일이 0.1초마다 이 CIWS가 자기를 덮는지(CanIntercept)
    /// 확인하고 매 프레임 InterceptTick을 부른다. CIWS는 한 번에 한 발만 상대하고, (쉬다가 처음이면 반응 시간 +) 격추 시간이 차면 그 미사일이 요격된다.
    /// 미사일 궤적·함선 위치·생사가 모든 화면에서 같아 동기화 없이 같은 결과가 나오고, 피해 판정자(플레이어 미사일 = 쏜 사람, NPC 미사일 = 맞는 쪽) 화면이 결과다.
    /// 요격 중에는 비행기보다 미사일을 먼저 겨누고(연출 — 예광탄만, 피해 없음), 잠들어 있어도 깬다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_AAGunSite : UdonSharpBehaviour
    {
        [Header("참조")]
        public SaccEntity EntityControl;
        [Tooltip("체력/데미지/리스폰은 SaccTarget이 소유한다. ExplodeOther에 이 스크립트를 등록해야 격추 통지를 받는다")]
        public SaccTarget TargetHealth;
        [Tooltip("포신 끝의 기총. 사거리/발포각/체류 시간/데미지는 기총 쪽에서 설정한다")]
        public ACO_NPCGun Gun;
        [Tooltip("파괴 시 팀 점수(ACO_MissionManager)")]
        public int ScoreValue = 100;
        [Tooltip("표적 마커에 표시할 약어(3~4글자)")]
        public string MarkerLabel = "AAG";

        [Header("탐지")]
        [Tooltip("적으로 간주할 레이어. 기본값은 아군 항공기(25)")]
        public LayerMask TargetLayers = 1 << 25;
        [Tooltip("이 거리 안의 타겟을 추적하기 시작한다(m). 기총 사거리(Gun.MaxRange)보다 길게 두면 미리 포신을 돌려 둔다")]
        public float DetectRange = 2500f;
        [Tooltip("이 거리를 넘으면 타겟을 놓는다")]
        public float LoseTargetRange = 3000f;
        [Tooltip("이 고도 미만의 타겟은 무시한다(지면 클러터, 착륙 중인 기체)")]
        public float MinTargetAltitude = 20f;
        public float ScanInterval = 0.5f;
        [Tooltip("타겟과의 시야가 막혀 있으면 추적/사격하지 않는다")]
        public bool RequireLineOfSight = true;
        public LayerMask LineOfSightBlockers = 2049;

        [Header("포탑")]
        public Transform TurretYaw;
        public Transform TurretPitch;
        [Tooltip("초당 최대 방위 회전(도). 가까이서 빠르게 가로지르는 기체는 포탑이 못 따라간다")]
        public float YawRate = 70f;
        [Tooltip("초당 최대 고각 회전(도)")]
        public float PitchRate = 50f;
        public float MinPitch = -5f;
        public float MaxPitch = 85f;
        [Tooltip("교전할 타겟이 없어진 뒤 이 시간(초)이 지나면 포탑을 처음 자세로 되돌린다. 잠깐 시야가 가려질 때마다 돌아가지 않게 둔다")]
        public float ReturnToRestDelay = 2f;

        [Header("조준 (회피 가능성)")]
        [Tooltip("포탑은 이 시간(초)만큼 늦게 따라가는 타겟 속도로 리드를 잡는다. 클수록 급기동에 약하다(피하기 쉽다). 0이면 완벽 추적")]
        public float TrackingLag = 0.6f;
        [Tooltip("포신이 조준점 주위로 흔들리는 최대 각도(도). 기총의 EffectiveAngle보다 작아야 직진하는 타겟을 맞힌다")]
        public float AimError = 0.8f;
        [Tooltip("흔들림 속도(Hz 정도)")]
        public float AimErrorFrequency = 0.7f;

        [Header("미사일 요격 (CIWS)")]
        [Tooltip("이 레이어의 락온 마커를 노린 유도 미사일(ACO_Missile)을 쏘아 떨어뜨린다. 0 = 요격 안 함. "
            + "적 CIWS = 적 항공기·지상(29, 30 — 플레이어 미사일을 막는다), 아군 CIWS = 아군 항공기·지상(25, 26 — 적 미사일을 막는다). 폭탄·로켓은 막지 않는다")]
        public LayerMask InterceptProtectLayers = 0;
        [Tooltip("요격 사거리(m): 미사일이 이 CIWS에서 이 거리 안에 있어야 쏜다")]
        public float InterceptRange = 1200f;
        [Tooltip("방어 반경(m): 미사일의 표적이 이 CIWS에서 이 거리 안이어야 막는다(함대 방어 — 옆 함선·근처 아군기). 자기 유닛을 노린 미사일은 늘 막는다")]
        public float DefenseRadius = 600f;
        [Tooltip("쉬다가(1초 넘게 교전 없음) 처음 교전할 때 더 걸리는 시간(초)")]
        public float InterceptReactionTime = 0.5f;
        [Tooltip("미사일 한 발을 떨어뜨리는 데 걸리는 시간(초). 한 번에 한 발만 상대한다 — 일제 발사는 일부가 뚫는다")]
        public float InterceptKillTime = 0.5f;
        [Tooltip("자기 유닛이 아닌 표적(옆 함선·아군기)을 노린 미사일은 걸리는 시간에 이 배수를 곱한다")]
        public float OtherDefenseFactor = 2f;
        // Bake가 함선 CIWS·아군 함선 대공포에 요격 기본값을 한 번 넣었다(그 뒤로 직접 바꾼 값은 건드리지 않는다)
        [HideInInspector] public bool InterceptDefaultsApplied;

        [Header("원거리 잠재우기")]
        [Tooltip("조종 중인 플레이어 기체가 (탐지/추적 거리 + 이 거리)(m) 안에 하나도 없으면 잠든다 — 탐지/조준 계산을 건너뛴다. 0 이하면 잠재우지 않는다")]
        public float SleepMargin = 3000f;
        [Tooltip("잠든 동안 깨울지 확인하는 주기(초). 늦어도 이 시간 안에 깨어난다")]
        public float SleepCheckInterval = 1f;

        [Header("격추 연출")]
        [Tooltip("격추 시 숨길 오브젝트. SAV의 TargetExplode 애니메이션은 '루트'의 MeshRenderer/MeshCollider만 끄므로 자식에 있는 모델은 여기 넣어야 한다")]
        public GameObject[] HideOnDeath;
        [Tooltip("격추 시 켤 오브젝트(화재/연기/잔해 등 연출용). 비워도 된다")]
        public GameObject[] ShowOnDeath;
        [Tooltip("격추 시 끌 콜라이더. 루트가 BoxCollider면 애니메이션 대상(MeshCollider)이 아니라 꺼지지 않으므로 여기 넣는다")]
        public Collider[] DisableCollidersOnDeath;

        private const float TARGET_VEL_SMOOTH_TIME = 0.25f;

        [UdonSynced] private short _syncTargetIdx = -1;

        private int _targetIdx = -1;
        private Transform _target;
        private SaccEntity _targetEntity;
        private SaccAirVehicle _targetSAV;
        private ACO_NPCFlightController _targetNPC;

        // 현재 타겟 속도(명중 판정용)와 늦게 따라가는 속도(포탑 조준용)
        private Vector3 _targetVel;
        private Vector3 _aimVel;
        private Vector3 _lastTargetPos;
        private float _lastTargetSampleTime;
        private bool _hasLastTargetPos;

        private float _scanTimer;
        private bool _hasLineOfSight;
        private bool _isOwner;
        // 오너 여부 재확인(늦게 들어온 클라이언트는 네트워크가 자리 잡기 전 IsOwner가 틀릴 수 있고, 꺼져 있는 동안의 소유권 변경은 이벤트가 오지 않는다)
        private float _ownerCheckTimer;
        private bool _disabled;
        private float _noiseSeed;

        // 기본 자세(시작 시 로컬 회전)와 타겟이 없어진 뒤 흐른 시간
        private Quaternion _restYaw = Quaternion.identity;
        private Quaternion _restPitch = Quaternion.identity;
        private float _noTargetTime;

        // 원거리 잠재우기
        private bool _sleeping;
        private float _sleepTimer;
        private SaccEntity[] _aamEntities;

        // 미사일 요격: 지키는 유닛(함선 본체·호위 유닛 — 이 아래를 노린 미사일은 '자기 유닛'), 지금 상대하는 미사일과 진행
        private Transform _defendedRoot;
        private ACO_Missile _icMissile;
        private float _icLastTick = -100f;
        private float _icProgress;
        private float _icNeed;
        // 상대하던 미사일이 이 시간(초) 넘게 틱하지 않으면(터졌거나 사거리 밖) 다른 미사일을 받는다
        private const float IC_HOLD = 0.15f;
        // 이 시간(초) 넘게 교전이 없었으면 다음 교전에 반응 시간이 붙는다
        private const float IC_IDLE = 1f;

        // 미션 생성기 계약(ACO_MissionGenerator가 SetProgramVariable로 넣고 MissionSpawn을 부른다)
        [System.NonSerialized] public Vector3 MissionSpawnPosition;
        [System.NonSerialized] public Quaternion MissionSpawnRotation = Quaternion.identity;
        [System.NonSerialized] public bool MissionRevive;
        private bool _initialized;

        void Start()
        {
            Init();
        }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;

            if (TurretYaw) { _restYaw = TurretYaw.localRotation; }
            if (TurretPitch) { _restPitch = TurretPitch.localRotation; }

            if (!EntityControl) { EntityControl = GetComponentInParent<SaccEntity>(); }
            if (!TargetHealth) { TargetHealth = GetComponentInParent<SaccTarget>(); }
            if (!Gun) { Gun = GetComponentInChildren<ACO_NPCGun>(); }
            _isOwner = Networking.IsOwner(gameObject);

            ACO_Warship ship = GetComponentInParent<ACO_Warship>();
            ACO_EscortUnit escortUnit = GetComponentInParent<ACO_EscortUnit>();
            if (ship) { _defendedRoot = ship.transform; }
            else if (escortUnit) { _defendedRoot = escortUnit.transform; }
            else if (EntityControl) { _defendedRoot = EntityControl.transform; }
            else { _defendedRoot = transform; }

            // 포대마다 흔들림 위상이 달라야 여러 문이 같은 박자로 움직이지 않는다. 위치 해시라 모든 클라이언트가 같다.
            Vector3 p = transform.position;
            int h = Mathf.RoundToInt(p.x) * 73856093 ^ Mathf.RoundToInt(p.z) * 83492791;
            _noiseSeed = (h & 0xffff) * 0.01f;
            WakeUp();
        }

        void Update()
        {
            if (_disabled) { return; }
            float dt = Time.deltaTime;
            _ownerCheckTimer -= dt;
            if (_ownerCheckTimer <= 0f)
            {
                _ownerCheckTimer = 1f;
                _isOwner = Networking.IsOwner(gameObject);
            }

            bool intercepting = IsIntercepting();

            // 원거리 잠재우기: 잠든 동안엔 주기적으로 깨울지만 확인한다. 미사일을 요격하는 동안은 깨어 있고, 끝나면 포탑만 쉬는 자세로 돌린다.
            if (SleepMargin > 0f)
            {
                _sleepTimer -= dt;
                if (_sleepTimer <= 0f)
                {
                    _sleepTimer = SleepCheckInterval;
                    UpdateSleep();
                }
                if (_sleeping && !intercepting)
                {
                    if (!TurretAtRest()) { ReturnToRest(dt); }
                    if (Gun) { Gun.ForceStop(); }
                    return;
                }
            }
            else { _sleeping = false; }

            // 미사일 요격이 비행기보다 먼저다(조준·예광탄 연출만 — 요격 판정은 미사일이 InterceptTick으로 한다)
            if (intercepting)
            {
                _noTargetTime = 0f;
                Vector3 mpos = _icMissile.transform.position;
                Vector3 aimPoint = Gun != null ? Gun.LeadPoint(mpos, _icMissile.GetVelocity()) : mpos;
                Vector3 origin = TurretPitch != null ? TurretPitch.position : transform.position;
                RotateTurret(aimPoint - origin, dt);
                if (Gun) { Gun.InterceptFire(aimPoint); }
                return;
            }

            // 시야 확인은 비용이 있어 주기적으로만 한다. 타겟 선택은 오너만, 시야 갱신은 모든 클라이언트가 한다.
            _scanTimer -= dt;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                if (_isOwner) { ScanForTarget(); }
                _hasLineOfSight = _target != null && HasLineOfSight(_target.position);
            }

            bool engageable = TargetIsEngageable();
            if (engageable) { UpdateTargetVelocity(dt); }

            AimTurret(engageable, dt);

            // 발포(시각)는 모든 클라이언트가 로컬로, 데미지는 기총 안에서 타겟 오너만 계산한다.
            // 기총에는 "현재" 속도를 넘긴다 — 포탑은 늦은 속도로 조준하므로, 급기동하면 판정 리드와 포신이 어긋난다.
            if (Gun) { Gun.GunTick(engageable ? _targetIdx : -1, _targetVel, dt); }
        }

        // ───────────── 탐지 ─────────────

        private void ScanForTarget()
        {
            if (!EntityControl) { return; }
            GameObject[] targets = EntityControl.AAMTargets;
            if (targets == null) { return; }

            // 기존 타겟이 아직 유효하면 유지한다(포탑을 이미 돌려 둔 상태를 버리지 않기 위해).
            if (_target != null && _target.gameObject.activeInHierarchy && !TargetDead()
                && Vector3.Distance(transform.position, _target.position) <= LoseTargetRange
                && _target.position.y >= MinTargetAltitude && HasLineOfSight(_target.position))
            {
                return;
            }

            int mask = TargetLayers;
            int bestIdx = -1;
            float bestDist = DetectRange;

            for (int i = 0; i < targets.Length; i++)
            {
                GameObject g = targets[i];
                if (!g || !g.activeInHierarchy) { continue; }
                if (((1 << g.layer) & mask) == 0) { continue; }

                Vector3 tp = g.transform.position;
                if (tp.y < MinTargetAltitude) { continue; }

                float d = Vector3.Distance(transform.position, tp);
                if (d >= bestDist) { continue; }
                if (!HasLineOfSight(tp)) { continue; }

                bestDist = d;
                bestIdx = i;
            }

            if (bestIdx != _targetIdx) { SetTarget(bestIdx); }
        }

        private bool TargetIsEngageable()
        {
            if (_target == null || !_target.gameObject.activeInHierarchy) { return false; }
            if (TargetDead()) { return false; }
            if (_target.position.y < MinTargetAltitude) { return false; }
            if (RequireLineOfSight && !_hasLineOfSight) { return false; }
            return Vector3.Distance(transform.position, _target.position) <= LoseTargetRange;
        }

        private bool TargetDead()
        {
            return _targetEntity != null && _targetEntity.dead;
        }

        private bool HasLineOfSight(Vector3 worldPos)
        {
            if (!RequireLineOfSight) { return true; }
            Vector3 origin = TurretPitch != null ? TurretPitch.position : transform.position;
            return !Physics.Linecast(origin, worldPos, LineOfSightBlockers, QueryTriggerInteraction.Ignore);
        }

        private void SetTarget(int idx)
        {
            BindTarget(idx);
            if (_isOwner)
            {
                _syncTargetIdx = (short)idx;
                RequestSerialization();
            }
        }

        private void BindTarget(int idx)
        {
            _targetIdx = idx;
            _target = null;
            _targetEntity = null;
            _targetSAV = null;
            _targetNPC = null;
            _hasLastTargetPos = false;
            _targetVel = Vector3.zero;
            _aimVel = Vector3.zero;
            _hasLineOfSight = false;
            _scanTimer = 0f; // 새 타겟의 시야를 바로 확인한다

            if (idx < 0 || !EntityControl) { return; }
            GameObject[] targets = EntityControl.AAMTargets;
            if (targets == null || idx >= targets.Length) { return; }

            GameObject g = targets[idx];
            if (!g) { return; }
            _target = g.transform;
            _targetEntity = g.GetComponentInParent<SaccEntity>();
            if (_targetEntity != null) { _targetSAV = _targetEntity.GetComponentInChildren<SaccAirVehicle>(true); }
            _targetNPC = g.GetComponentInParent<ACO_NPCFlightController>();
        }

        // ───────────── 조준 ─────────────

        /// 타겟 속도. 원격 기체 위치는 네트워크 보간으로 흔들리므로 가능하면 직접 받는다(ACO_NPCFighterAI와 같은 규칙).
        private void UpdateTargetVelocity(float dt)
        {
            float now = Time.time;
            float elapsed = now - _lastTargetSampleTime;
            Vector3 pos = _target.position;

            Vector3 raw;
            bool hasRaw = true;
            if (_targetSAV != null) { raw = _targetSAV.CurrentVel; }
            else if (_targetNPC != null) { raw = _targetNPC.transform.forward * _targetNPC.Speed; }
            else if (_hasLastTargetPos && elapsed > 0f && elapsed < 0.2f) { raw = (pos - _lastTargetPos) / elapsed; }
            else { raw = Vector3.zero; hasRaw = false; }

            if (!hasRaw) { _targetVel = Vector3.zero; }
            else if (!_hasLastTargetPos || elapsed <= 0f || elapsed >= 0.2f) { _targetVel = raw; _aimVel = raw; }
            else { _targetVel = Vector3.Lerp(_targetVel, raw, 1f - Mathf.Exp(-elapsed / TARGET_VEL_SMOOTH_TIME)); }

            // 포탑용 속도는 TrackingLag만큼 늦게 따라간다 — 이 차이가 급기동 회피의 근거다.
            if (TrackingLag > 0f) { _aimVel = Vector3.Lerp(_aimVel, _targetVel, 1f - Mathf.Exp(-dt / TrackingLag)); }
            else { _aimVel = _targetVel; }

            _lastTargetPos = pos;
            _lastTargetSampleTime = now;
            _hasLastTargetPos = true;
        }

        private void AimTurret(bool engageable, float dt)
        {
            if (!TurretYaw && !TurretPitch) { return; }

            if (!engageable)
            {
                _noTargetTime += dt;
                if (_noTargetTime >= ReturnToRestDelay) { ReturnToRest(dt); }
                return;
            }
            _noTargetTime = 0f;

            Vector3 origin = TurretPitch != null ? TurretPitch.position : transform.position;
            Vector3 aimPoint = Gun != null ? Gun.LeadPoint(_target.position, _aimVel) : _target.position;
            Vector3 aimDir = aimPoint - origin;
            if (aimDir.sqrMagnitude < 0.000001f) { return; }

            // 조준 흔들림: 조준 방향 기준으로 좌우/상하를 부드러운 노이즈만큼 비튼다.
            if (AimError > 0f)
            {
                float t = Time.time * AimErrorFrequency;
                float nx = (Mathf.PerlinNoise(t, _noiseSeed) - 0.5f) * 2f * AimError;
                float ny = (Mathf.PerlinNoise(_noiseSeed, t) - 0.5f) * 2f * AimError;
                aimDir = Quaternion.LookRotation(aimDir.normalized, Vector3.up) * Quaternion.Euler(nx, ny, 0f) * Vector3.forward;
            }
            RotateTurret(aimDir, dt);
        }

        /// 포탑을 조준 방향으로 돌린다(방위·고각 회전 속도 한계).
        private void RotateTurret(Vector3 aimDir, float dt)
        {
            if (aimDir.sqrMagnitude < 0.000001f) { return; }
            if (TurretYaw)
            {
                Vector3 flat = new Vector3(aimDir.x, 0f, aimDir.z);
                if (flat.sqrMagnitude > 0.000001f)
                {
                    Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
                    TurretYaw.rotation = Quaternion.RotateTowards(TurretYaw.rotation, want, YawRate * dt);
                }
            }

            if (TurretPitch)
            {
                float pitch = Mathf.Asin(Mathf.Clamp(aimDir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
                Quaternion want = Quaternion.Euler(-pitch, 0f, 0f);
                TurretPitch.localRotation = Quaternion.RotateTowards(TurretPitch.localRotation, want, PitchRate * dt);
            }
        }

        /// 포탑을 시작 자세로 되돌린다. 조준과 같은 회전 속도 한계를 지킨다.
        private void ReturnToRest(float dt)
        {
            if (TurretYaw) { TurretYaw.localRotation = Quaternion.RotateTowards(TurretYaw.localRotation, _restYaw, YawRate * dt); }
            if (TurretPitch) { TurretPitch.localRotation = Quaternion.RotateTowards(TurretPitch.localRotation, _restPitch, PitchRate * dt); }
        }

        private void SnapToRest()
        {
            if (TurretYaw) { TurretYaw.localRotation = _restYaw; }
            if (TurretPitch) { TurretPitch.localRotation = _restPitch; }
            _noTargetTime = 0f;
        }

        // ───────────── 원거리 잠재우기 ─────────────

        /// 깨어 있어야 하는지 확인한다. 조종 중인 플레이어 기체가 가까우면 깨우고,
        /// 아무도 없으면 타겟을 놓고 포탑이 기본 자세로 돌아간 뒤에 잠든다(멈춘 포탑이 엉뚱한 방향을 보고 있지 않게).
        private void UpdateSleep()
        {
            float range = Mathf.Max(DetectRange, LoseTargetRange) + SleepMargin;
            if (AnyPilotedAircraftWithin(range))
            {
                _sleeping = false;
                return;
            }
            if (_sleeping) { return; }
            // 오너는 타겟을 놓은 뒤에(놓는 동기화가 나간 뒤에) 잔다. 다른 클라이언트는 타겟이 멀어 포탑이 쉬는 자세면 된다.
            if (_isOwner && _targetIdx >= 0) { return; }
            if (!TurretAtRest()) { return; }
            _sleeping = true;
            OnSleep();
        }

        /// 판단 기준은 이 유닛이 쏘는 대상과 같은 목록(빌드 시 베이크된 AAMTargets) 중 "조종사가 탄" 기체다 — 세워 둔 빈 기체는 깨우지 않는다.
        private bool AnyPilotedAircraftWithin(float range)
        {
            if (!EntityControl) { return true; }
            GameObject[] targets = EntityControl.AAMTargets;
            if (targets == null) { return true; }
            if (_aamEntities == null || _aamEntities.Length != targets.Length)
            {
                _aamEntities = new SaccEntity[targets.Length];
                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i]) { _aamEntities[i] = targets[i].GetComponentInParent<SaccEntity>(); }
                }
            }

            int mask = TargetLayers;
            float rangeSq = range * range;
            Vector3 p = transform.position;
            for (int i = 0; i < targets.Length; i++)
            {
                GameObject g = targets[i];
                if (!g || !g.activeInHierarchy) { continue; }
                if (((1 << g.layer) & mask) == 0) { continue; }
                SaccEntity e = _aamEntities[i];
                if (e != null && !e.Occupied) { continue; }
                if ((g.transform.position - p).sqrMagnitude <= rangeSq) { return true; }
            }
            return false;
        }

        private bool TurretAtRest()
        {
            if (TurretYaw && Quaternion.Angle(TurretYaw.localRotation, _restYaw) > 0.5f) { return false; }
            if (TurretPitch && Quaternion.Angle(TurretPitch.localRotation, _restPitch) > 0.5f) { return false; }
            return true;
        }

        /// 스폰/부활 직후에는 깨어 있게 하고, 확인 시점은 유닛마다 흩뜨린다(한 프레임에 몰리지 않게).
        private void WakeUp()
        {
            _sleeping = false;
            _sleepTimer = Random.Range(0f, Mathf.Max(SleepCheckInterval, 0.1f));
        }

        /// 잠들 때: 발사 연출을 확실히 멈춘다.
        private void OnSleep()
        {
            if (Gun) { Gun.ForceStop(); }
        }

        public bool IsSleeping() { return _sleeping; }

        // ───────────── 미사일 요격 (판정은 ACO_Missile이 부른다) ─────────────

        private bool IsIntercepting()
        {
            return _icMissile != null && Time.time - _icLastTick < IC_HOLD;
        }

        /// 이 CIWS가 이 미사일을 막을 수 있는가: 살아 있음, 지키는 레이어의 표적, 미사일이 요격 사거리 안, 표적이 자기 유닛이거나 방어 반경 안.
        public bool CanIntercept(Vector3 missilePos, Transform target)
        {
            if (InterceptProtectLayers.value == 0 || _disabled || !gameObject.activeInHierarchy || !target) { return false; }
            Init();
            if (TargetHealth && TargetHealth.Health <= 0f) { return false; }
            if (((1 << target.gameObject.layer) & InterceptProtectLayers.value) == 0) { return false; }
            Vector3 p = transform.position;
            if ((missilePos - p).sqrMagnitude > InterceptRange * InterceptRange) { return false; }
            if (_defendedRoot && target.IsChildOf(_defendedRoot)) { return true; }
            return (target.position - p).sqrMagnitude <= DefenseRadius * DefenseRadius;
        }

        /// 미사일이 매 프레임 부른다. -1 = 못 맡음(덮지 못하거나 다른 미사일을 쏘는 중 — 미사일은 다른 CIWS를 찾는다), 0 = 쏘는 중, 1 = 요격했다.
        public int InterceptTick(ACO_Missile missile, Transform target, float dt)
        {
            if (!missile || !CanIntercept(missile.transform.position, target))
            {
                if (_icMissile == missile) { _icMissile = null; }
                return -1;
            }
            float now = Time.time;
            if (_icMissile != missile)
            {
                if (IsIntercepting()) { return -1; }
                bool idle = now - _icLastTick > IC_IDLE;
                bool own = _defendedRoot && target.IsChildOf(_defendedRoot);
                _icMissile = missile;
                _icProgress = 0f;
                _icNeed = ((idle ? InterceptReactionTime : 0f) + InterceptKillTime) * (own ? 1f : Mathf.Max(OtherDefenseFactor, 0.01f));
                if (_sleeping) { _sleeping = false; _sleepTimer = SleepCheckInterval; }
            }
            _icLastTick = now;
            _icProgress += dt;
            if (_icProgress < _icNeed) { return 0; }
            _icMissile = null;
            return 1;
        }

        // ───────────── 네트워크 / 격추 / 부활 ─────────────

        public override void OnDeserialization()
        {
            // 상태를 받았다 = 다른 누군가가 오너다
            _isOwner = Networking.IsOwner(gameObject);
            if (_syncTargetIdx != _targetIdx) { BindTarget(_syncTargetIdx); }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            _isOwner = Networking.IsOwner(gameObject);
        }

        /// SaccTarget이 격추 시 ExplodeOther의 스크립트들에 보내는 이벤트(모든 클라이언트에서 실행).
        public void Explode()
        {
            _disabled = true;
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
            if (ShowOnDeath != null)
            {
                for (int i = 0; i < ShowOnDeath.Length; i++)
                {
                    if (ShowOnDeath[i]) { ShowOnDeath[i].SetActive(dead); }
                }
            }
            if (DisableCollidersOnDeath != null)
            {
                for (int i = 0; i < DisableCollidersOnDeath.Length; i++)
                {
                    if (DisableCollidersOnDeath[i]) { DisableCollidersOnDeath[i].enabled = !dead; }
                }
            }
        }

        /// 부활 진입점. SaccTarget의 RespawnDelay는 0으로 두고(자동 부활 안 함) 스폰/웨이브 매니저가 이걸 호출한다.
        public void Respawn()
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetRespawn));
        }

        /// 미션 스폰(모든 클라이언트). 위치를 옮기고, MissionRevive면 부활시킨다.
        /// 늦게 들어온 클라이언트는 MissionRevive가 false라 위치만 맞춘다 — 생사는 SaccTarget 동기화가 맞춘다.
        public void MissionSpawn()
        {
            Init();
            transform.SetPositionAndRotation(MissionSpawnPosition, MissionSpawnRotation);
            if (MissionRevive) { NetRespawn(); return; }

            // 부활 없이 받는 경우(늦게 들어온 클라이언트): 풀 숨김(MissionDespawn)으로 꺼진 상태를 실제 생사에 맞춰 되돌리고,
            // 지워 둔 타겟을 동기화 값으로 다시 잇는다. 대공포 데미지는 피격자 화면에서 계산하므로, 멈춰 있으면 이 플레이어는 맞지 않는다.
            _disabled = TargetHealth != null && TargetHealth.Health <= 0f;
            BindTarget(_syncTargetIdx);
            WakeUp();
        }

        /// 이번 미션에 쓰지 않는다. 생성기가 곧 오브젝트를 끈다.
        public void MissionDespawn()
        {
            _disabled = true;
            if (Gun) { Gun.ForceStop(); }
            BindTarget(-1);
        }

        /// 다른 유닛(함선 등)에 붙은 부위로 쓸 때의 스폰(모든 클라이언트). 위치는 부모가 옮기므로 건드리지 않는다.
        /// MissionRevive는 부모가 넣는다.
        public void AttachedSpawn()
        {
            Init();
            if (MissionRevive) { NetRespawn(); return; }
            _disabled = TargetHealth != null && TargetHealth.Health <= 0f;
            BindTarget(_syncTargetIdx);
            WakeUp();
        }

        /// 부모(함선)가 격침됐다: 모델은 그대로 두고(같이 가라앉는다) 교전만 멈추고 히트박스를 끈다.
        public void Silence()
        {
            // 본체가 격침되면 부위도 파괴한다 — 예전엔 교전만 잠깐 멈췄는데, 부위 체력이 남아 있어 락온 마커가 살아 있고
            // _disabled가 체력(> 0)으로 다시 계산되며 사격이 되살아났다. 부활은 본체가 AttachedSpawn/NetRespawn으로 함께 한다.
            if (TargetHealth && TargetHealth.Health > 0f && Networking.IsOwner(TargetHealth.gameObject)) { TargetHealth.Explode(); }
            _disabled = true;
            if (Gun) { Gun.ForceStop(); }
            BindTarget(-1);
            if (DisableCollidersOnDeath != null)
            {
                for (int i = 0; i < DisableCollidersOnDeath.Length; i++)
                {
                    if (DisableCollidersOnDeath[i]) { DisableCollidersOnDeath[i].enabled = false; }
                }
            }
        }

        public void NetRespawn()
        {
            _isOwner = Networking.IsOwner(gameObject);

            // 체력 복구는 한 클라이언트만 해야 한다(RespawnTarget이 내부에서 소유권을 가져가고 동기화한다).
            if (_isOwner && TargetHealth) { SendCustomEventDelayedFrames(nameof(_RespawnTargetHealth), 2); }

            SetDeathVisuals(false);
            SnapToRest();
            WakeUp();
            _disabled = false;
            SetTarget(-1);
        }

        /// SaccTarget 체력 복구(RespawnTarget). 풀에서 막 켠 유닛은 SaccTarget의 Start가 아직 안 돌아 localPlayer·FullHealth가 비어 있다 —
        /// 바로 부르면 예외로 SaccTarget이 멈춘다(그 뒤로 피해·격추가 안 된다). 그래서 2프레임 뒤에 부른다.
        public void _RespawnTargetHealth()
        {
            if (TargetHealth) { TargetHealth.RespawnTarget(); }
        }
    }
}
