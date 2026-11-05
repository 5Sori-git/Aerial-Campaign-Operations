using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 곡사포(자주포·야포 — ACO_MobileUnit의 KIND_ARTILLERY 포탑). 아군 호위 대상 중 지상·해상 유닛(콘보이·함선, ACO_EscortUnit — 공중 수송기는 빼고)을
    /// MinRange~MaxRange에서 골라 높은 포물선으로 포격한다. 탄착 예고는 없다(플레이어가 아니라 호위 대상을 노린다).
    ///  - 오너: 가장 가까운 표적을 고르고, 탄 비행 시간만큼 앞을 예측해(표적 진행 방향 × 속도) 고각 탄도를 풀어 FireInterval(+무작위 FireIntervalJitter)마다
    ///    모두에게 발사를 알린다(NetFire(초속도 벡터)). 탄착점은 Spread(m) 안에서 흩어진다.
    ///  - 모든 클라이언트: 포탑을 그 방향으로 돌리고, 풀의 포탄(ACO_Ordnance — 중력, 저항 0)을 같은 초속도로 쏜다. 범위 피해는 오너 화면에서만(IsShooter),
    ///    아군(호위 대상)만 맞는다(HitFriendlies — 적 유닛은 피해 없음).
    /// 체력은 같은 오브젝트의 SaccTarget(전차와 공유). 생성기 계약은 전차가 넘겨준다(AttachedSpawn/MissionDespawn/NetRespawn).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_ArtilleryGun : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("빌드 시 Bake가 씬의 미션 생성기를 넣는다(호위 대상 목록)")]
        public ACO_MissionGenerator Generator;
        public SaccTarget TargetHealth;
        [Tooltip("이 포를 싣고 다니는 전차 스크립트(KIND_ARTILLERY). 있으면 항로 끝에 멈춘 뒤에만 쏜다")]
        public ACO_MobileUnit Carrier;
        [Tooltip("좌우로 도는 포탑(비면 돌리지 않는다)")]
        public Transform TurretYaw;
        [Tooltip("위아래로 드는 포신(로컬 X축 회전, 비면 돌리지 않는다)")]
        public Transform TurretPitch;
        [Tooltip("포구 — 포탄이 나가는 자리")]
        public Transform Muzzle;

        [Header("포탄")]
        [Tooltip("포탄 프리팹(ACO_Ordnance — 중력 1, 저항 0 권장)")]
        public GameObject Shell;
        public int ShellPool = 6;
        public float ShellDamage = 60f;
        public float ShellSplash = 22f;

        [Header("사격")]
        public float MinRange = 600f;
        public float MaxRange = 6000f;
        [Tooltip("초속도(m/s) — 사거리 한계는 v² / (g × ShellGravityScale)(45도)")]
        public float MuzzleSpeed = 420f;
        [Tooltip("포탄 중력 배수. 실제 중력이면 4km 고각 비행이 1분 가까이라 지루하다 — 3이면 약 27초(시간은 1/√배수로 준다)")]
        public float ShellGravityScale = 3f;
        public float FireInterval = 7f;
        public float FireIntervalJitter = 2f;
        [Tooltip("탄착점이 흩어지는 반경(m)")]
        public float Spread = 25f;

        private bool _hasTierBase;
        private float _baseFireInterval;
        private float _baseSpread;

        /// 지상 유닛 숙련도(생성기가 스폰할 때 — 모든 클라이언트). 배율은 프리팹 값 기준이라 여러 번 불러도 누적되지 않는다.
        public void ApplyTier(float intervalMul, float spreadMul)
        {
            if (!_hasTierBase)
            {
                _hasTierBase = true;
                _baseFireInterval = FireInterval;
                _baseSpread = Spread;
            }
            FireInterval = _baseFireInterval * intervalMul;
            Spread = _baseSpread * spreadMul;
        }
        [Tooltip("포탑 회전 속도(도/초)")]
        public float TurnRate = 25f;
        public float RetargetInterval = 1f;

        [Header("연출 / 격파")]
        public AudioSource FireSound;
        public ParticleSystem MuzzleFlash;
        public GameObject[] HideOnDeath;
        public GameObject[] ShowOnDeath;

        // 전차가 넣는다(생성기 계약)
        [System.NonSerialized] public bool MissionRevive;

        private GameObject[] _shells;
        private bool _initialized;
        private bool _active;
        private bool _dead;
        private float _nextFire;
        private float _retargetTimer;
        private ACO_EscortUnit _target;
        private Vector3 _aimDir = Vector3.forward;
        private bool _hasAim;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            if (!Carrier) { Carrier = GetComponent<ACO_MobileUnit>(); }
            if (!Muzzle) { Muzzle = transform; }
            int n = Shell ? Mathf.Max(ShellPool, 1) : 0;
            _shells = new GameObject[n];
            for (int i = 0; i < n; i++)
            {
                // 유닛 계층 밖에 둔다(유닛 애니메이터가 다시 바인딩하지 않게)
                GameObject s = Object.Instantiate(Shell);
                s.transform.SetParent(null);
                s.SetActive(false);
                _shells[i] = s;
            }
        }

        void Update()
        {
            if (!_active || _dead) { return; }
            float dt = Time.deltaTime;
            if (Networking.IsOwner(gameObject)) { OwnerTick(dt); }
            if (_hasAim) { TurnTurret(dt); }
        }

        // ───────────── 오너 ─────────────

        private void OwnerTick(float dt)
        {
            if (Carrier != null && !Carrier.IsStopped()) { return; }   // 사격 위치로 가는 중
            _retargetTimer -= dt;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = RetargetInterval;
                _target = PickTarget();
            }
            if (_target == null) { return; }

            Vector3 vel;
            if (!Solve(_target, Vector3.zero, out vel)) { _target = null; return; }
            _aimDir = vel;
            _hasAim = true;
            if (Time.time < _nextFire) { return; }
            if (!Aligned(vel)) { return; }

            // 탄착점을 흩는다
            Vector2 r = Random.insideUnitCircle * Spread;
            Vector3 fireVel;
            if (!Solve(_target, new Vector3(r.x, 0f, r.y), out fireVel)) { return; }
            _nextFire = Time.time + FireInterval + Random.Range(0f, Mathf.Max(FireIntervalJitter, 0f));
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetFire), fireVel);
        }

        /// 사거리 안의 가장 가까운 아군 지상·해상 호위 대상.
        private ACO_EscortUnit PickTarget()
        {
            if (Generator == null || Generator.EscortUnits == null) { return null; }
            Vector3 me = Muzzle.position;
            float minSq = MinRange * MinRange;
            float maxSq = MaxRange * MaxRange;
            ACO_EscortUnit best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < Generator.EscortUnits.Length; i++)
            {
                ACO_EscortUnit e = Generator.EscortUnits[i];
                if (e == null || e.Hostile || e.MoveMode == ACO_EscortUnit.MOVE_AIR) { continue; }
                if (!e.gameObject.activeInHierarchy || !Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || e.IsDestroyed()) { continue; }
                Vector3 d = e.transform.position - me;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < minSq || sq > maxSq || sq >= bestSq) { continue; }
                best = e;
                bestSq = sq;
            }
            return best;
        }

        /// 고각 탄도: 표적의 예측 위치(+ offset)에 떨어지는 초속도 벡터. 사거리 밖이면 false.
        private bool Solve(ACO_EscortUnit target, Vector3 offset, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            Vector3 p0 = Muzzle.position;
            Vector3 tv = target.transform.forward * target.Speed;
            tv.y = 0f;
            Vector3 aim = target.transform.position + offset;
            float g = Mathf.Max(-Physics.gravity.y * ShellGravityScale, 0.01f);
            float v = Mathf.Max(MuzzleSpeed, 1f);
            float v2 = v * v;
            // 비행 시간만큼 앞을 두 번 고쳐 예측한다
            for (int iter = 0; iter < 3; iter++)
            {
                Vector3 d = aim - p0;
                float x = new Vector2(d.x, d.z).magnitude;
                if (x < 1f) { return false; }
                float disc = v2 * v2 - g * (g * x * x + 2f * d.y * v2);
                if (disc < 0f) { return false; }
                float tanT = (v2 + Mathf.Sqrt(disc)) / (g * x);   // 높은 포물선
                float cosT = 1f / Mathf.Sqrt(1f + tanT * tanT);
                float flight = x / (v * cosT);
                Vector3 horiz = new Vector3(d.x, 0f, d.z) / x;
                velocity = horiz * (v * cosT) + Vector3.up * (v * cosT * tanT);
                if (iter < 2) { aim = target.transform.position + offset + tv * flight; }
            }
            return true;
        }

        private bool Aligned(Vector3 vel)
        {
            if (!TurretYaw && !TurretPitch) { return true; }
            Transform t = TurretPitch ? TurretPitch : TurretYaw;
            return Vector3.Angle(t.forward, vel) < 3f;
        }

        private void TurnTurret(float dt)
        {
            float step = TurnRate * dt;
            if (TurretYaw)
            {
                Vector3 flat = _aimDir;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.0001f)
                {
                    Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
                    TurretYaw.rotation = Quaternion.RotateTowards(TurretYaw.rotation, want, step);
                }
            }
            if (TurretPitch)
            {
                float flatLen = new Vector2(_aimDir.x, _aimDir.z).magnitude;
                float elev = Mathf.Atan2(_aimDir.y, Mathf.Max(flatLen, 0.0001f)) * Mathf.Rad2Deg;
                Quaternion want = Quaternion.Euler(-elev, 0f, 0f);
                TurretPitch.localRotation = Quaternion.RotateTowards(TurretPitch.localRotation, want, step);
            }
        }

        // ───────────── 모든 클라이언트 ─────────────

        [VRC.SDK3.UdonNetworkCalling.NetworkCallable]
        public void NetFire(Vector3 velocity)
        {
            Init();
            if (_dead) { return; }
            _aimDir = velocity;
            _hasAim = true;
            if (FireSound && FireSound.clip) { FireSound.PlayOneShot(FireSound.clip); }
            if (MuzzleFlash) { MuzzleFlash.Play(); }
            GameObject s = TakeShell();
            if (!s) { return; }
            s.transform.SetPositionAndRotation(Muzzle.position, Quaternion.LookRotation(velocity.normalized, Vector3.up));
            ACO_Ordnance o = s.GetComponent<ACO_Ordnance>();
            if (!o) { return; }
            o.EntityControl = null;
            o.PoolParent = null;
            o.PoolOwner = null;
            o.LaunchVelocity = velocity;
            o.LaunchSpeed = 0f;
            o.Acceleration = 0f;
            o.Drag = 0f;
            o.GravityScale = ShellGravityScale;
            o.Damage = ShellDamage;
            o.SplashRadius = ShellSplash;
            o.HitFriendlies = true;
            o.IsShooter = Networking.IsOwner(gameObject);   // 범위 피해는 오너 화면에서만
            s.SetActive(true);
            o.EnableWeapon();
        }

        private GameObject TakeShell()
        {
            if (_shells == null) { return null; }
            for (int i = 0; i < _shells.Length; i++) { if (_shells[i] && !_shells[i].activeSelf) { return _shells[i]; } }
            return null;   // 다 날고 있으면 이번 발은 연출 없이 넘긴다(풀을 늘리면 된다)
        }

        // ───────────── 격파 / 생성기 계약(전차가 넘겨준다) ─────────────

        /// SaccTarget이 격파 시 ExplodeOther로 보낸다(모든 클라이언트).
        public void Explode()
        {
            _dead = true;
            _target = null;
            SetDeathVisuals(true);
        }

        public void AttachedSpawn()
        {
            Init();
            _active = true;
            _hasAim = false;
            _target = null;
            _nextFire = Time.time + Random.Range(2f, FireInterval);
            if (MissionRevive)
            {
                if (Networking.IsOwner(gameObject) && TargetHealth) { SendCustomEventDelayedFrames(nameof(_RespawnTargetHealth), 2); }
                _dead = false;
                SetDeathVisuals(false);
                return;
            }
            _dead = TargetHealth != null && TargetHealth.Health <= 0f;
            SetDeathVisuals(_dead);
        }

        public void MissionDespawn()
        {
            _active = false;
            _target = null;
        }

        public void NetRespawn()
        {
            Init();
            if (Networking.IsOwner(gameObject) && TargetHealth) { SendCustomEventDelayedFrames(nameof(_RespawnTargetHealth), 2); }
            _dead = false;
            SetDeathVisuals(false);
        }

        /// SaccTarget 체력 복구(RespawnTarget) — 막 켠 SaccTarget은 Start 전이라 2프레임 뒤에 부른다.
        public void _RespawnTargetHealth()
        {
            if (TargetHealth) { TargetHealth.RespawnTarget(); }
        }

        private void SetDeathVisuals(bool dead)
        {
            if (HideOnDeath != null) { for (int i = 0; i < HideOnDeath.Length; i++) { if (HideOnDeath[i]) { HideOnDeath[i].SetActive(!dead); } } }
            if (ShowOnDeath != null) { for (int i = 0; i < ShowOnDeath.Length; i++) { if (ShowOnDeath[i]) { ShowOnDeath[i].SetActive(dead); } } }
        }
    }
}
