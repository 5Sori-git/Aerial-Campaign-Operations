using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// NPC 기총. 탄은 시각 전용 파티클이고, 데미지는 "조준 체류 시간" 방식으로 타겟 기체의 오너만 계산한다.
    ///   1) 리드 조준점과 기수가 FireStartAngle(A) 이내 → 발포 시작 (시각 효과, 모든 클라이언트 로컬)
    ///   2) EffectiveAngle(B) 이내에 DwellTime(N)초 연속 머물면 → 데미지 시작 (벗어나면 리셋)
    ///   3) 데미지 주기는 가까울수록 짧아진다 (MinDamageInterval ~ MaxDamageInterval)
    ///
    /// SAV 기총처럼 충돌 파티클을 쓰지 않는 이유: SaccEntity.OnParticleCollision은 충돌이 일어난 클라이언트에서
    /// 무조건 데미지를 적용하므로, 한 클라이언트의 화면에서 날아간 탄이 다른 기체에 닿으면 엉뚱한 기체가 맞는다.
    /// 계산을 타겟 오너 한 명에게만 맡기면 그 사람이 자기 화면에서 본 상황으로 판정되고 중복 데미지도 없다.
    ///
    /// Update가 없다. ACO_NPCFighterAI가 매 프레임 GunTick을 호출한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_NPCGun : UdonSharpBehaviour
    {
        [Header("참조")]
        public SaccEntity EntityControl;
        [Tooltip("총구. forward가 발사 방향. 비우면 이 오브젝트")]
        public Transform Muzzle;
        [Tooltip("시각 전용 탄 파티클(충돌 모듈 끔). 발사 중에만 방출한다")]
        public ParticleSystem VisualParticle;
        public AudioSource FireSound;
        [Tooltip("탄이 쏜 기체의 속도를 물려받는다고 보고 리드를 계산할 때 쓴다. 파티클의 Inherit Velocity 설정과 맞춘다")]
        public ACO_NPCFlightController ShooterFlight;
        public bool InheritShooterVelocity = true;

        [Header("사격 조건")]
        public float MaxRange = 800f;
        [Tooltip("A: 리드 조준점과 기수가 이 각도(도) 이내면 발포를 시작한다")]
        public float FireStartAngle = 6f;
        [Tooltip("B: 이 각도(도) 이내에 계속 머물러야 데미지가 들어간다")]
        public float EffectiveAngle = 3f;
        [Tooltip("N: B 이내에 이 시간(초) 연속 머물면 데미지가 시작된다. 플레이어에게 회피할 틈을 주는 구간")]
        public float DwellTime = 0.5f;
        [Tooltip("한 번에 연사하는 시간(초). 0이면 끊지 않고 계속 쏜다")]
        public float BurstDuration = 1.2f;
        [Tooltip("연사 사이 쉬는 시간(초). 쉬는 동안엔 데미지도 없다")]
        public float BurstCooldown = 0.8f;

        [Header("데미지")]
        [Tooltip("데미지 1회분. SAV 전투기 체력은 54")]
        public float DamagePerTick = 4f;
        [Tooltip("코앞에서의 데미지 주기(초). 가까울수록 이 값에 가까워진다")]
        public float MinDamageInterval = 0.1f;
        [Tooltip("최대 사거리에서의 데미지 주기(초)")]
        public float MaxDamageInterval = 0.5f;
        [Tooltip("SAV 무기 타입. 1 = 총탄")]
        public byte WeaponType = 1;
        [Tooltip("지형에 가려지면 데미지가 들어가지 않는다")]
        public LayerMask GroundLayers = 2049;

        [Header("탄도 (리드 계산 / 파티클)")]
        public float MuzzleVelocity = 900f;
        [Tooltip("시각 전용 — 프리팹 빌더가 파티클 원뿔 각도로 쓴다. 데미지와 무관")]
        public float Spread = 0.6f;

        private int _targetIdx = -1;
        private Transform _target;
        private SaccEntity _targetEntity;
        private SaccTarget _targetStatic;
        private Vector3 _targetVel;

        private bool _firing;
        private bool _inBurst;
        private float _burstTimer;
        private float _dwell;
        private float _damageTimer;

        void Start()
        {
            if (!EntityControl) { EntityControl = GetComponentInParent<SaccEntity>(); }
            if (!Muzzle) { Muzzle = transform; }
            if (VisualParticle) { VisualParticle.Stop(); }
        }

        /// AI가 매 프레임 호출한다(모든 클라이언트). targetIndex가 음수면 교전하지 않는다.
        /// targetVelocity는 AI가 이미 추정한 타겟 속도를 그대로 받는다(같은 클라이언트의 같은 관측이라 중복 추정하지 않는다).
        public void GunTick(int targetIndex, Vector3 targetVelocity, float dt)
        {
            if (targetIndex != _targetIdx) { BindTarget(targetIndex); }
            _targetVel = targetVelocity;

            bool valid = targetIndex >= 0 && TargetIsValid();
            float dist = 0f;
            float angle = 180f;
            if (valid)
            {
                dist = Vector3.Distance(MuzzlePosition(), _target.position);
                angle = LeadAngle();
            }
            bool inRange = valid && dist <= MaxRange;

            bool fireNow = UpdateBurst(inRange && angle <= FireStartAngle, dt);
            SetFiring(fireNow);

            // 여기부터는 데미지 판정 — 타겟 기체의 오너만 한다.
            if (!inRange || !IOwnTarget()) { ResetDwell(); return; }

            if (angle > EffectiveAngle) { ResetDwell(); return; }
            _dwell += dt;

            // 연사를 쉬는 동안엔 트레이서가 안 나가므로 데미지도 멈춘다(체류 시간은 유지).
            if (_dwell < DwellTime || !fireNow) { return; }

            // 0 = 코앞, 1 = 최대 사거리. 가까울수록 주기가 짧아 초당 데미지가 커진다.
            float rangeRatio = Mathf.Clamp01(dist / Mathf.Max(MaxRange, 1f));
            float interval = Mathf.Lerp(MinDamageInterval, MaxDamageInterval, rangeRatio);

            _damageTimer += dt;
            if (_damageTimer < interval) { return; }
            _damageTimer = 0f;

            if (BlockedByTerrain()) { return; }
            ApplyDamage(DamagePerTick);
        }

        /// 탄속 기준 리드 조준점. AI는 기총 사거리 안에서 기체를 이 점으로 돌린다.
        /// |D + V·t| = s·t 를 t에 대해 정확히 푼다(D = 타겟-총구, V = 쏜 기체 기준 상대 속도, s = 탄속).
        /// 고정점 반복으로 근사하면 측면 교차에서 수렴이 덜 돼 조준점이 수 m 어긋난다.
        public Vector3 LeadPoint(Vector3 targetPos, Vector3 targetVelocity)
        {
            Vector3 origin = MuzzlePosition();
            Vector3 relVel = targetVelocity - InheritedVelocity();
            Vector3 d = targetPos - origin;
            float speed = Mathf.Max(MuzzleVelocity, 1f);

            float a = Vector3.Dot(relVel, relVel) - speed * speed;
            float b = 2f * Vector3.Dot(d, relVel);
            float c = Vector3.Dot(d, d);

            float t = -1f;
            if (Mathf.Abs(a) < 0.0001f)
            {
                if (b < 0f) { t = -c / b; }
            }
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc >= 0f)
                {
                    float root = Mathf.Sqrt(disc);
                    float t1 = (-b - root) / (2f * a);
                    float t2 = (-b + root) / (2f * a);
                    if (t1 > 0f && t2 > 0f) { t = Mathf.Min(t1, t2); }
                    else if (t1 > 0f) { t = t1; }
                    else if (t2 > 0f) { t = t2; }
                }
            }

            // 해가 없으면(탄이 타겟을 따라잡지 못함) 거리/탄속으로 근사한다.
            if (t < 0f) { t = d.magnitude / speed; }
            return targetPos + relVel * t;
        }

        /// 격추 등으로 즉시 멈출 때.
        public void ForceStop()
        {
            SetFiring(false);
            _inBurst = false;
            _burstTimer = 0f;
            ResetDwell();
        }

        private float LeadAngle()
        {
            Vector3 toLead = LeadPoint(_target.position, _targetVel) - MuzzlePosition();
            return Vector3.Angle(MuzzleForward(), toLead);
        }

        private void ResetDwell()
        {
            _dwell = 0f;
            _damageTimer = 0f;
        }

        private void BindTarget(int idx)
        {
            ResetDwell();
            _targetIdx = idx;
            _target = null;
            _targetEntity = null;
            _targetStatic = null;

            if (idx < 0 || !EntityControl) { return; }
            GameObject[] targets = EntityControl.AAMTargets;
            if (targets == null || idx >= targets.Length) { return; }

            GameObject marker = targets[idx];
            if (!marker) { return; }
            _target = marker.transform;

            // 미사일과 같은 규칙: NPC는 SaccTarget이 체력을 소유하므로 우선한다(설계 2.4).
            _targetStatic = marker.GetComponentInParent<SaccTarget>();
            _targetEntity = marker.GetComponentInParent<SaccEntity>();
        }

        private bool TargetIsValid()
        {
            if (!_target || !_target.gameObject.activeInHierarchy) { return false; }
            if (_targetEntity && _targetEntity.dead) { return false; }
            return true;
        }

        private bool IOwnTarget()
        {
            if (_targetStatic) { return Networking.IsOwner(_targetStatic.gameObject); }
            if (_targetEntity) { return Networking.IsOwner(_targetEntity.gameObject); }
            return false;
        }

        private bool UpdateBurst(bool wantFire, float dt)
        {
            if (BurstDuration <= 0f) { return wantFire; }

            _burstTimer -= dt;
            if (_inBurst)
            {
                if (!wantFire || _burstTimer <= 0f)
                {
                    _inBurst = false;
                    _burstTimer = BurstCooldown;
                }
            }
            else if (wantFire && _burstTimer <= 0f)
            {
                _inBurst = true;
                _burstTimer = BurstDuration;
            }
            return _inBurst;
        }

        private bool BlockedByTerrain()
        {
            RaycastHit blocked;
            return Physics.Linecast(MuzzlePosition(), _target.position, out blocked, GroundLayers, QueryTriggerInteraction.Ignore);
        }

        /// 판정자가 타겟 오너이므로 여기서 호출하면 SaccTarget/SaccEntity가 로컬 적용 + 전파를 처리한다.
        /// damagingObject로 이 오브젝트를 넘기면 SAV가 부모를 타고 올라가 쏜 NPC의 SaccEntity를 찾아 킬을 기록한다.
        private void ApplyDamage(float damage)
        {
            if (_targetStatic)
            {
                _targetStatic.WeaponDamageTarget(damage, gameObject, WeaponType);
            }
            else if (_targetEntity)
            {
                _targetEntity.WeaponDamageVehicle(damage, gameObject, WeaponType);
            }
        }

        private void SetFiring(bool on)
        {
            if (on == _firing) { return; }
            _firing = on;

            if (VisualParticle)
            {
                if (on) { VisualParticle.Play(); }
                else { VisualParticle.Stop(); }
            }
            if (FireSound)
            {
                if (on) { FireSound.Play(); }
                else { FireSound.Stop(); }
            }
        }

        private Vector3 MuzzlePosition()
        {
            return Muzzle != null ? Muzzle.position : transform.position;
        }

        private Vector3 MuzzleForward()
        {
            return Muzzle != null ? Muzzle.forward : transform.forward;
        }

        private Vector3 InheritedVelocity()
        {
            if (!InheritShooterVelocity || !ShooterFlight) { return Vector3.zero; }
            return ShooterFlight.transform.forward * ShooterFlight.Speed;
        }
    }
}
