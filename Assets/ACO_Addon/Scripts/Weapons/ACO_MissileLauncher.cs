using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using SaccFlightAndVehicles;

namespace ACO
{
    /// SAV 미사일(SAV_AAMController)을 비행기 없이 발사하는 공용 런처. NPC 전투기와 SAM이 공유한다.
    /// SAV_AAMController는 런처를 타입 고정 없이 GetProgramVariable로만 읽으므로,
    /// 아래 "덕 타이핑 계약" 변수들을 같은 이름/타입으로 노출하면 DFUNC_AAM을 대체할 수 있다.
    /// (타입은 SaccAAGunController의 선언을 따른다 — 미사일이 AAMTarget을 (int)로 언박싱한다)
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_MissileLauncher : UdonSharpBehaviour
    {
        [Header("덕 타이핑 계약 (SAV_AAMController가 읽는 값)")]
        [Tooltip("필수. 미사일이 타겟 락온 시 EntityControl.CenterOfMass를 역참조하므로 null이면 안 된다")]
        public SaccEntity EntityControl;
        [Tooltip("0=레이더, 1=열추적, 2=기타")]
        public int MissileType = 1;
        public float AAMMaxTargetDistance = 6000f;
        public GameObject PitBullIndicator;

        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다(나를 노린 미사일 경보)")]
        [UnityEngine.Serialization.FormerlySerializedAs("Radio")]
        public ACO_Chatter Chatter;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 RWR을 넣는다(락온 방향·날아오는 미사일 방향/거리 표시)")]
        public ACO_RWR Rwr;

        [Header("발사 설정")]
        public GameObject AAM;
        [Tooltip("발사 위치. 여러 개면 번갈아 사용한다")]
        public Transform[] LaunchPoints;
        [Tooltip("발사 후 미사일을 둘 부모. 비우면 씬 루트")]
        public Transform WorldParent;
        public int NumMissiles = 4;
        public int NumPreInstantiated = 2;
        [Tooltip("연속 발사 최소 간격(초)")]
        public float FireInterval = 1.5f;
        [Tooltip("전탄 소모 후 재장전 시간(초). 0이면 재장전 없음")]
        public float ReloadTime = 20f;
        public AudioSource LaunchSound;

        [Header("락온")]
        [Tooltip("조준을 이만큼 유지해야 발사할 수 있다. 0이면 즉시 발사")]
        public float LockTime = 2f;
        [Tooltip("조준이 끊기면 락온 진행도가 초당 이 속도로 감소한다")]
        public float LockDecayRate = 1.5f;
        [Tooltip("락온 중 타겟 플레이어 조종석에 경고음을 보낸다")]
        public bool SendLockWarning = true;
        public float LockWarningInterval = 1f;
        [Tooltip("모기체 속도를 미사일 초기 속도로 물려줄 비행 컨트롤러. SAM(정지 발사)은 비워둔다")]
        public ACO_NPCFlightController InheritVelocityFrom;

        // SAV_AAMController가 GetProgramVariable로 읽는 값들 — 이름을 바꾸면 안 된다.
        [System.NonSerialized] public UdonSharpBehaviour SAVControl = null;
        [System.NonSerialized] public bool InEditor = true;
        [System.NonSerialized] public bool IsOwner = false;
        [System.NonSerialized] public int AAMTarget = 0;
        [System.NonSerialized] public GameObject[] AAMTargets;

        private int _numChildrenStart;
        private int _ammo;
        private float _nextFireTime;
        private float _reloadDoneTime;
        private int _launchPointIdx;
        private bool _initialized;

        private float _lockProgress;
        private int _aimTargetIdx = -1;
        private float _warningTimer;

        void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) { return; }
            _initialized = true;

            if (!EntityControl) { EntityControl = GetComponentInParent<SaccEntity>(); }
            InEditor = Networking.LocalPlayer == null;
            IsOwner = Networking.IsOwner(gameObject);
            _ammo = NumMissiles;

            // 미사일 풀은 "시작 시점 이후에 추가된 자식"이다. 미사일은 MoveBackToPool에서 스스로 여기로 돌아온다.
            _numChildrenStart = transform.childCount;

            if (AAM)
            {
                int count = Mathf.Min(NumMissiles, NumPreInstantiated);
                for (int i = 0; i < count; i++) { InstantiateMissile(); }
            }
        }

        private GameObject InstantiateMissile()
        {
            GameObject m = Object.Instantiate(AAM);
            m.transform.SetParent(transform);
            m.SetActive(false);
            return m;
        }

        /// 탄약/연사 간격을 확인한다. 재장전 완료 판정도 여기서 처리하므로 Update가 필요 없다.
        public bool CanFire()
        {
            if (!AAM) { return false; }
            if (_ammo <= 0)
            {
                if (ReloadTime <= 0f || Time.time < _reloadDoneTime) { return false; }
                _ammo = NumMissiles;
            }
            return Time.time >= _nextFireTime;
        }

        public int GetAmmo()
        {
            return _ammo;
        }

        /// 오너의 AI가 조준 중일 때 매 틱 호출한다. 락온 진행도를 누적하고 타겟에게 경고를 보낸다.
        /// "누구를 조준할지"는 플랫폼(AI/SAM)이, "락온됐는지"는 무기(이 스크립트)가 판단한다.
        public void AimAt(int targetIndex, float dt)
        {
            if (targetIndex < 0) { ReleaseAim(); return; }

            // 타겟이 바뀌면 락온을 처음부터 다시 쌓는다.
            if (targetIndex != _aimTargetIdx)
            {
                _aimTargetIdx = targetIndex;
                _lockProgress = 0f;
                _warningTimer = 0f;
            }

            _lockProgress += dt;

            if (!SendLockWarning) { return; }
            _warningTimer -= dt;
            if (_warningTimer <= 0f)
            {
                _warningTimer = LockWarningInterval;
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All,
                    nameof(LockWarningEvent), (ushort)targetIndex);
            }
        }

        /// 락온 경보만 보낸다(락온 진행 없이) — 슈퍼에이스 다연장이 쏘기 전에 고른 플레이어들에게.
        public void WarnTarget(int targetIndex)
        {
            if (targetIndex < 0) { return; }
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(LockWarningEvent), (ushort)targetIndex);
        }

        public void ReleaseAim()
        {
            _aimTargetIdx = -1;
            _lockProgress = Mathf.Max(0f, _lockProgress - LockDecayRate * Time.deltaTime);
        }

        public bool IsLocked()
        {
            return _lockProgress >= LockTime;
        }

        public float GetLockProgress()
        {
            return LockTime > 0f ? Mathf.Clamp01(_lockProgress / LockTime) : 1f;
        }

        /// SAV는 자기 DFUNC_AAM에서만 경고음을 보내므로 우리 런처가 직접 보내야 한다(설계 0.4절).
        /// 각 클라이언트가 "그 기체에 내가 타고 있는가"를 확인하고 로컬 경고음을 재생한다.
        [NetworkCallable]
        public void LockWarningEvent(ushort targetIndex)
        {
            if (!EntityControl) { return; }
            GameObject[] targets = EntityControl.AAMTargets;
            if (targets == null || targetIndex >= targets.Length) { return; }

            GameObject g = targets[targetIndex];
            if (!g) { return; }

            SaccEntity victim = g.GetComponentInParent<SaccEntity>();
            if (victim && victim.InVehicle)
            {
                victim.SendEventToExtensions("SFEXT_L_AAMTargeted");
                if (Rwr) { Rwr.ReportLock(transform, victim); }
            }
        }

        /// 오너가 호출한다. 모든 클라이언트가 같은 발사를 로컬에서 재현하도록 1회 브로드캐스트한다.
        public void FireAt(int targetIndex)
        {
            if (!CanFire()) { return; }
            if (targetIndex < 0) { return; }
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(LaunchEvent), (ushort)targetIndex);
        }

        [NetworkCallable]
        public void LaunchEvent(ushort targetIndex)
        {
            if (!_initialized) { Initialize(); }
            if (!AAM) { return; }

            // 각 클라이언트가 스스로 판정하므로 오너에서만 true가 된다.
            IsOwner = Networking.IsOwner(gameObject);
            if (!EntityControl) { return; }

            AAMTargets = EntityControl.AAMTargets;
            if (AAMTargets == null || targetIndex >= AAMTargets.Length) { return; }
            AAMTarget = targetIndex;

            if (_ammo > 0) { _ammo--; }
            _nextFireTime = Time.time + FireInterval;
            if (_ammo <= 0) { _reloadDoneTime = Time.time + ReloadTime; }
            _lockProgress = 0f;

            Transform launchPoint = GetLaunchPoint();
            GameObject missile = TakeFromPool();
            if (!missile) { return; }

            if (WorldParent) { missile.transform.SetParent(WorldParent); }
            else { missile.transform.SetParent(null); }

            missile.transform.SetPositionAndRotation(launchPoint.position, launchPoint.rotation);

            Rigidbody rb = missile.GetComponent<Rigidbody>();
            if (rb)
            {
                rb.position = launchPoint.position;
                rb.rotation = launchPoint.rotation;
                // 모든 클라이언트가 같은 비행 상태를 재생하므로 여기서 계산해도 값이 일치한다.
                rb.velocity = InheritVelocityFrom != null
                    ? InheritVelocityFrom.transform.forward * InheritVelocityFrom.Speed
                    : Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // 자체 미사일(ACO_Missile)은 발사 정보를 직접 주입한다.
            // SAV 미사일은 GetProgramVariable로 이 스크립트를 읽으므로 주입이 필요 없다.
            ACO_Missile acoMissile = missile.GetComponent<ACO_Missile>();
            if (acoMissile) { acoMissile.Chatter = Chatter; acoMissile.Rwr = Rwr; }
            if (acoMissile != null)
            {
                acoMissile.EntityControl = EntityControl;
                acoMissile.LaunchTarget = AAMTargets[targetIndex];
                acoMissile.LaunchMissileType = MissileType;
                acoMissile.LaunchInheritSpeed = InheritVelocityFrom != null ? InheritVelocityFrom.Speed : 0f;
                acoMissile.PoolParent = transform;
                acoMissile.ShooterJudgesHit = false; // NPC 미사일은 피격자가 판정한다
                acoMissile.IsShooter = false;
            }

            missile.SetActive(true);

            if (LaunchSound && LaunchSound.clip) { LaunchSound.PlayOneShot(LaunchSound.clip); }

            UdonSharpBehaviour missileBehaviour = missile.GetComponent<UdonSharpBehaviour>();
            if (missileBehaviour) { missileBehaviour.SendCustomEvent("EnableWeapon"); }
        }

        private Transform GetLaunchPoint()
        {
            if (LaunchPoints == null || LaunchPoints.Length == 0) { return transform; }
            if (_launchPointIdx >= LaunchPoints.Length) { _launchPointIdx = 0; }
            Transform p = LaunchPoints[_launchPointIdx];
            _launchPointIdx++;
            return p != null ? p : transform;
        }

        private GameObject TakeFromPool()
        {
            if (transform.childCount - _numChildrenStart > 0)
            {
                return transform.GetChild(_numChildrenStart).gameObject;
            }
            return InstantiateMissile();
        }
    }
}
