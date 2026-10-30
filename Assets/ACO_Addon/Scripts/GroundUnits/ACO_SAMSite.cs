using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 고정형 SAM 포대. 사거리 내 타겟을 자동 탐지 → 락온 타이머 누적 → 미사일 발사.
    /// 탐지는 빌드 시 베이크된 SaccEntity.AAMTargets를 순회한다(OverlapSphere 미사용 — 설계 3.3절).
    /// 미사일/명중판정은 ACO_MissileLauncher + ACO_Missile을 그대로 재사용한다.
    /// 원거리 잠재우기: 조종 중인 플레이어 기체가 (탐지 거리 + SleepMargin) 밖이면 탐지/조준을 멈추고 SleepCheckInterval마다 깨울지만 본다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_SAMSite : UdonSharpBehaviour
    {
        [Header("참조")]
        public SaccEntity EntityControl;
        [Tooltip("체력/데미지/리스폰은 SaccTarget이 소유한다(0.2-1절). ExplodeOther에 이 스크립트를 등록해야 격추 통지를 받는다")]
        public SaccTarget TargetHealth;
        public ACO_MissileLauncher MissileLauncher;
        [Tooltip("파괴 시 팀 점수(ACO_MissionManager)")]
        public int ScoreValue = 150;
        [Tooltip("표적 마커에 표시할 약어(3~4글자)")]
        public string MarkerLabel = "SAM";

        [Header("탐지")]
        [Tooltip("적으로 간주할 레이어. 기본값은 아군 항공기(25)")]
        public LayerMask TargetLayers = 1 << 25;
        public float DetectRange = 4000f;
        [Tooltip("이 거리를 넘으면 타겟을 놓는다")]
        public float LoseTargetRange = 5000f;
        [Tooltip("레이더 기준 방향에서 이 각도 안에 있어야 탐지한다(도). 180이면 전방위")]
        public float RadarConeAngle = 180f;
        [Tooltip("이 고도 미만의 타겟은 무시한다(지면 클러터)")]
        public float MinTargetAltitude = 20f;
        public float ScanInterval = 0.5f;
        [Tooltip("타겟과의 시야가 막혀 있으면 탐지하지 않는다")]
        public bool RequireLineOfSight = true;
        public LayerMask LineOfSightBlockers = 2049;

        [Header("발사")]
        [Tooltip("발사 후 다음 교전까지 대기(초). 락온 시간 자체는 런처(ACO_MissileLauncher)가 관리한다")]
        public float PostFireDelay = 2f;

        [Header("포탑 (선택)")]
        public Transform TurretYaw;
        public Transform TurretPitch;
        public float TurretTurnRate = 45f;
        [Tooltip("포탑 최대 상향각(도)")]
        public float TurretMaxPitch = 85f;
        [Tooltip("추적할 타겟이 없어진 뒤 이 시간(초)이 지나면 포탑을 처음 자세로 되돌린다. 잠깐 시야가 가려질 때마다 돌아가지 않게 둔다")]
        public float ReturnToRestDelay = 2f;

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

        [UdonSynced] private short _syncTargetIdx = -1;

        private int _targetIdx = -1;
        private Transform _target;
        private SaccEntity _targetEntity;

        private float _scanTimer;
        private float _fireCooldown;
        private bool _isOwner;
        // 오너 여부 재확인(늦게 들어온 클라이언트는 네트워크가 자리 잡기 전 IsOwner가 틀릴 수 있고, 꺼져 있는 동안의 소유권 변경은 이벤트가 오지 않는다)
        private float _ownerCheckTimer;
        private bool _disabled;

        // 기본 자세(시작 시 로컬 회전)와 타겟이 없어진 뒤 흐른 시간
        private Quaternion _restYaw = Quaternion.identity;
        private Quaternion _restPitch = Quaternion.identity;
        private float _noTargetTime;

        // 원거리 잠재우기
        private bool _sleeping;
        private float _sleepTimer;
        private SaccEntity[] _aamEntities;

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
            if (!MissileLauncher) { MissileLauncher = GetComponentInChildren<ACO_MissileLauncher>(); }
            _isOwner = Networking.IsOwner(gameObject);
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

            // 원거리 잠재우기: 잠든 동안엔 주기적으로 깨울지만 확인한다.
            if (SleepMargin > 0f)
            {
                _sleepTimer -= dt;
                if (_sleepTimer <= 0f)
                {
                    _sleepTimer = SleepCheckInterval;
                    UpdateSleep();
                }
                if (_sleeping) { return; }
            }
            else { _sleeping = false; }

            if (_isOwner) { OwnerTick(dt); }

            // 포탑은 모든 클라이언트가 동기화된 타겟 위치로 로컬 계산한다 — 추가 트래픽이 없다.
            AimTurret(dt);
        }

        private void OwnerTick(float dt)
        {
            if (_fireCooldown > 0f) { _fireCooldown -= dt; }

            _scanTimer -= dt;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                ScanForTarget();
            }

            if (!MissileLauncher) { return; }

            if (TargetIsEngageable())
            {
                // 락온 누적과 경고 전송은 런처가 담당한다. 포대는 "이 타겟을 조준 중"이라고만 알린다.
                MissileLauncher.AimAt(_targetIdx, dt);
                if (MissileLauncher.IsLocked() && _fireCooldown <= 0f) { FireMissile(); }
            }
            else
            {
                MissileLauncher.ReleaseAim();
            }
        }

        private void FireMissile()
        {
            if (!MissileLauncher || !MissileLauncher.CanFire()) { return; }

            MissileLauncher.FireAt(_targetIdx);
            _fireCooldown = PostFireDelay;
        }

        private void ScanForTarget()
        {
            if (!EntityControl) { return; }
            GameObject[] targets = EntityControl.AAMTargets;
            if (targets == null) { return; }

            // 기존 타겟이 아직 쓸만하면 유지한다(락온 진행도를 버리지 않기 위해).
            if (_target && TargetInRange(_target, LoseTargetRange) && _target.gameObject.activeInHierarchy)
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

                Transform t = g.transform;
                if (t.position.y < MinTargetAltitude) { continue; }

                float d = Vector3.Distance(transform.position, t.position);
                if (d >= bestDist) { continue; }
                if (!InRadarCone(t.position)) { continue; }
                if (!HasLineOfSight(t.position)) { continue; }

                bestDist = d;
                bestIdx = i;
            }

            if (bestIdx != _targetIdx) { SetTarget(bestIdx); }
        }

        private bool TargetIsEngageable()
        {
            if (!_target || !_target.gameObject.activeInHierarchy) { return false; }
            if (_targetEntity && _targetEntity.dead) { return false; }
            if (!TargetInRange(_target, LoseTargetRange)) { return false; }
            if (_target.position.y < MinTargetAltitude) { return false; }
            if (!InRadarCone(_target.position)) { return false; }
            if (!HasLineOfSight(_target.position)) { return false; }
            return true;
        }

        private bool TargetInRange(Transform t, float range)
        {
            return Vector3.Distance(transform.position, t.position) <= range;
        }

        private bool InRadarCone(Vector3 worldPos)
        {
            if (RadarConeAngle >= 180f) { return true; }
            return Vector3.Angle(transform.forward, worldPos - transform.position) <= RadarConeAngle;
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

            if (idx < 0 || !EntityControl) { return; }
            GameObject[] targets = EntityControl.AAMTargets;
            if (targets == null || idx >= targets.Length) { return; }

            GameObject g = targets[idx];
            if (!g) { return; }
            _target = g.transform;
            _targetEntity = g.GetComponentInParent<SaccEntity>();
        }

        private void AimTurret(float dt)
        {
            if (!TurretYaw && !TurretPitch) { return; }

            // 추적할 타겟이 없으면(사라짐/격추/사거리 밖/최소 고도 아래) 잠시 뒤 처음 자세로 돌아간다.
            // 시야(LOS)는 매 프레임 Linecast가 들어서 여기서는 보지 않는다 — 가려진 동안엔 마지막 방향을 계속 따라간다.
            bool tracking = _target != null && _target.gameObject.activeInHierarchy
                && !(_targetEntity != null && _targetEntity.dead)
                && _target.position.y >= MinTargetAltitude
                && TargetInRange(_target, LoseTargetRange);
            if (!tracking)
            {
                _noTargetTime += dt;
                if (_noTargetTime >= ReturnToRestDelay) { ReturnToRest(dt); }
                return;
            }
            _noTargetTime = 0f;

            Vector3 origin = TurretPitch != null ? TurretPitch.position : transform.position;
            Vector3 aimDir = _target.position - origin;
            if (aimDir.sqrMagnitude < 0.000001f) { return; }

            if (TurretYaw)
            {
                Vector3 flat = new Vector3(aimDir.x, 0f, aimDir.z);
                if (flat.sqrMagnitude > 0.000001f)
                {
                    Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
                    TurretYaw.rotation = Quaternion.RotateTowards(TurretYaw.rotation, want, TurretTurnRate * dt);
                }
            }

            if (TurretPitch)
            {
                float pitch = Mathf.Asin(Mathf.Clamp(aimDir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(pitch, 0f, TurretMaxPitch);
                Quaternion want = Quaternion.Euler(-pitch, TurretPitch.localEulerAngles.y, 0f);
                TurretPitch.localRotation = Quaternion.RotateTowards(TurretPitch.localRotation, want, TurretTurnRate * dt);
            }
        }


        /// 포탑을 시작 자세로 되돌린다. 조준과 같은 회전 속도 한계를 지킨다.
        private void ReturnToRest(float dt)
        {
            if (TurretYaw) { TurretYaw.localRotation = Quaternion.RotateTowards(TurretYaw.localRotation, _restYaw, TurretTurnRate * dt); }
            if (TurretPitch) { TurretPitch.localRotation = Quaternion.RotateTowards(TurretPitch.localRotation, _restPitch, TurretTurnRate * dt); }
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

        /// 잠들 때: 오너는 런처 조준(락온 경고)을 확실히 내린다.
        private void OnSleep()
        {
            if (_isOwner && MissileLauncher) { MissileLauncher.ReleaseAim(); }
        }

        public bool IsSleeping() { return _sleeping; }

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
        /// 모델 숨김과 폭발 연출은 SaccTarget의 Animator가 처리하므로 여기서는 포대만 멈춘다.
        public void Explode()
        {
            _disabled = true;
            if (MissileLauncher) { MissileLauncher.ReleaseAim(); }
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
            // 지워 둔 타겟을 동기화 값으로 다시 잇는다. 안 그러면 이 클라이언트에서만 포대가 멈춰 있다.
            _disabled = TargetHealth != null && TargetHealth.Health <= 0f;
            BindTarget(_syncTargetIdx);
            WakeUp();
        }

        /// 이번 미션에 쓰지 않는다. 생성기가 곧 오브젝트를 끈다.
        public void MissionDespawn()
        {
            _disabled = true;
            if (MissileLauncher) { MissileLauncher.ReleaseAim(); }
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
            _disabled = true;
            if (MissileLauncher) { MissileLauncher.ReleaseAim(); }
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
            _fireCooldown = 0f;
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
