using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 소형 레일건(랜스 포대 방공 부위): 표적이 LeadTime초 뒤에 있을 자리를 예측해 포신을 고정하고, 탄이 그 시각에 닿도록 늦춰 쏜다.
    /// 탄이 1초 걸리면 고정 2초 뒤에 쏜다. 직진하면 폭발 지역(ZoneTime초)에 들어가고, 고정된 레이저를 보고 방향을 틀면 피한다.
    /// 레이저: 고정 AimTime초 전부터 예측 자리를 따라다니고, 고정되면 탄이 닿을 때까지 남는다. 예측 자리가 MinRange보다 가까우면 탄은 MinRange까지 날아가 터지고, 그 안에서는 직격만 맞는다.
    /// 탐지·조준·체력·부활은 같은 오브젝트의 ACO_AAGunSite가 맡는다(기총 없이 — 그래서 포대 부위 계약과 수리가 그대로다). 이 스크립트는 사격만 한다.
    /// 포대 오너가 쏠 자리를 정해 모두에게 알린다(NetLock). 탄·폭발은 각자 그리고, 피해는 맞는 사람이 자기 화면의 폭발 지역으로 판정한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_MiniRailgun : UdonSharpBehaviour
    {
        private const int MAX_ZONES = 3;

        [Header("참조")]
        public ACO_AAGunSite Site;
        public Transform Muzzle;
        [Tooltip("내가 탄 기체를 알려 준다(Bake가 넣는다)")]
        public ACO_RadarContacts Contacts;
        [Tooltip("Bake가 넣는다 — 첫 사격 무전")]
        public ACO_PhaseRailgun Phase;

        [Header("사격")]
        [Tooltip("포신을 고정한 뒤 탄이 닿을 때까지의 시간(초) — 몇 초 뒤의 자리를 노리는가")]
        public float LeadTime = 3f;
        [Tooltip("탄속(m/s) — 빠를수록 고정한 뒤 늦게 쏜다")]
        public float ShellSpeed = 5000f;
        [Tooltip("사격 간격(초)")]
        public float FireInterval = 7f;
        [Tooltip("최소 폭발 거리(m) — 이보다 가까운 곳에서는 터지지 않고 직격만 맞는다. 빗나간 탄은 이 거리에서 터진다")]
        public float MinRange = 1000f;
        [Tooltip("최소 폭발 거리 안의 직격 판정 반경(m)")]
        public float DirectHitRadius = 15f;
        [Tooltip("직격 피해")]
        public float DirectDamage = 60f;
        [Tooltip("쏘기 전 조준 레이저를 보여 주는 시간(초)")]
        public float AimTime = 1.5f;

        [Header("폭발 지역")]
        public float ZoneRadius = 170f;
        [Tooltip("폭발 지역이 남는 시간(초)")]
        public float ZoneTime = 3.5f;
        [Tooltip("지역 안에서 피해를 입는 간격(초)")]
        public float ZoneTick = 0.5f;
        public float ZoneDamage = 20f;

        [Header("연출 (프리팹 빌더가 넣는다 — 소형 레일건끼리 함께 쓴다)")]
        public ParticleSystem ShellParticle;
        public ParticleSystem BurstParticle;
        public ParticleSystem BurstFlash;
        public int BurstCount = 26;
        [Tooltip("사선 레이저 — 이 문의 것")]
        public LineRenderer Laser;
        [Tooltip("조준 중 레이저 굵기(m)")]
        public float LaserAimWidth = 3f;
        [Tooltip("고정된 레이저 굵기(m)")]
        public float LaserLockWidth = 7f;
        [Tooltip("레이저 길이(m) — 탄착 지점을 지나 이만큼 뻗어 나간다")]
        public float LaserReach = 15000f;
        public AudioSource FireSound;
        public AudioSource BurstSound;

        private Vector3[] _zonePos = new Vector3[MAX_ZONES];
        private float[] _zoneStart = new float[MAX_ZONES];
        private float[] _zoneEnd = new float[MAX_ZONES];
        private bool[] _zoneOn = new bool[MAX_ZONES];
        private bool[] _zoneBurst = new bool[MAX_ZONES];
        private int _nextZone;
        private float _fireTimer;
        private float _nextHurt;
        private bool _aiming;       // 조준 중(모든 클라이언트)
        private float _aimUntil;
        private float _lockAt;      // 포신을 고정할 시각(오너)
        private Vector3 _lockPoint;
        private float _lockUntil;
        private bool _shotPending;  // 포신을 고정하고 쏠 시각을 기다린다(모든 클라이언트)
        private float _shotFireAt;
        private Vector3 _shotPoint;
        private bool _shellFlying;
        private Vector3 _shellFrom;
        private Vector3 _shellDir;
        private float _shellStart;
        private float _shellEnd;
        private float _shellTravelled;
        private bool _shellDirect;  // 최소 폭발 거리 안을 노린 탄: 직격 판정
        private int _shellZone;

        void Start()
        {
            if (!Site) { Site = GetComponent<ACO_AAGunSite>(); }
            if (Site) { Site.AimLeadTime = LeadTime; }
            _fireTimer = Random.Range(0f, FireInterval);   // 네 문이 한꺼번에 쏘지 않게
            if (Laser) { Laser.enabled = false; }
        }

        void Update()
        {
            UpdateShot();
            UpdateZones();
            UpdateLaser();
            if (!Site || !Networking.IsOwner(gameObject) || _shotPending || _shellFlying) { return; }
            if (_lockAt > 0f)
            {
                // 조준이 끝났다: 표적을 잃었으면 고정하지 않는다(레이저는 스스로 꺼진다)
                if (Time.time < _lockAt) { return; }
                _lockAt = 0f;
                Vector3 point;
                if (PredictPoint(out point)) { SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetLock), point); }
                return;
            }
            _fireTimer -= Time.deltaTime;
            if (_fireTimer > 0f) { return; }
            Vector3 check;
            if (!PredictPoint(out check)) { return; }
            _fireTimer = FireInterval * Random.Range(0.85f, 1.15f);
            _lockAt = Time.time + AimTime;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetAim));
        }

        /// 표적이 LeadTime초 뒤에 있을 자리. 표적이 없으면 false.
        private bool PredictPoint(out Vector3 point)
        {
            point = Vector3.zero;
            if (!Site.IsEngaged()) { return false; }
            Transform target = Site.GetTargetTransform();
            if (target == null) { return false; }
            point = target.position + Site.GetTargetVelocity() * LeadTime;
            return true;
        }

        private Vector3 MuzzlePosition() { return Muzzle != null ? Muzzle.position : transform.position; }

        private float FlightTime(Vector3 from, Vector3 to)
        {
            return Vector3.Distance(from, to) / Mathf.Max(ShellSpeed, 1f);
        }

        /// 조준 시작(모두가 받는다) — 레이저가 예측 자리를 따라다닌다.
        [NetworkCallable]
        public void NetAim()
        {
            _aiming = true;
            _aimUntil = Time.time + AimTime + 1f;   // 고정이 안 오면(표적을 잃었으면) 스스로 꺼진다
        }

        private void UpdateLaser()
        {
            if (!Laser) { return; }
            float now = Time.time;
            bool locked = now < _lockUntil;
            Vector3 end = _lockPoint;
            bool on = locked;
            if (!locked && _aiming && now < _aimUntil && Site)
            {
                Vector3 point;
                if (PredictPoint(out point))
                {
                    end = point;
                    on = true;
                }
            }
            if (Laser.enabled != on) { Laser.enabled = on; }
            if (!on) { return; }
            Vector3 from = Muzzle != null ? Muzzle.position : transform.position;
            Laser.SetPosition(0, from);
            Laser.SetPosition(1, from + (end - from).normalized * LaserReach);
            Laser.widthMultiplier = locked ? LaserLockWidth : LaserAimWidth;
        }

        /// 모두가 받는다: 그 자리에 포신을 고정하고, 탄이 LeadTime 뒤에 닿도록 쏠 시각을 정한다. 레이저도 그 자리에 고정된다.
        [NetworkCallable]
        public void NetLock(Vector3 point)
        {
            _aiming = false;
            _lockPoint = point;
            _shotPoint = point;
            _shotPending = true;
            float arrive = Time.time + LeadTime;
            _lockUntil = arrive;
            _shotFireAt = arrive - FlightTime(MuzzlePosition(), point);
            if (Site) { Site.LockAim(point); }
        }

        private void UpdateShot()
        {
            if (_shotPending)
            {
                if (Site && !Site.IsAlive())
                {
                    // 쏘기 전에 부서졌다
                    _shotPending = false;
                    _lockUntil = 0f;
                    Site.UnlockAim();
                }
                else if (Time.time >= _shotFireAt) { FireShell(); }
            }
            if (!_shellFlying) { return; }
            float travelled = (Time.time - _shellStart) * Mathf.Max(ShellSpeed, 1f);
            if (_shellDirect) { CheckDirectHit(_shellTravelled, travelled); }
            _shellTravelled = travelled;
            if (Time.time < _shellEnd) { return; }
            _shellFlying = false;
            if (Site) { Site.UnlockAim(); }
        }

        /// 고정한 자리로 쏜다. 그 자리가 최소 폭발 거리 안이면 탄은 지나쳐 최소 폭발 거리에서 터진다.
        private void FireShell()
        {
            _shotPending = false;
            Vector3 from = MuzzlePosition();
            Vector3 toPoint = _shotPoint - from;
            float distance = toPoint.magnitude;
            Vector3 dir = distance > 0.01f ? toPoint / distance : transform.forward;
            bool close = distance < MinRange;
            float burstDistance = close ? MinRange : distance;
            float speed = Mathf.Max(ShellSpeed, 1f);
            float flight = burstDistance / speed;
            if (ShellParticle)
            {
                var emit = new ParticleSystem.EmitParams();
                emit.position = from;
                emit.velocity = dir * speed;
                emit.startLifetime = flight;
                ShellParticle.Emit(emit, 1);
            }
            if (FireSound)
            {
                FireSound.transform.position = from;
                FireSound.Play();
            }
            float now = Time.time;
            int k = _nextZone;
            _nextZone = (_nextZone + 1) % MAX_ZONES;
            _zonePos[k] = from + dir * burstDistance;
            _zoneStart[k] = now + flight;
            _zoneEnd[k] = _zoneStart[k] + ZoneTime;
            _zoneOn[k] = true;
            _zoneBurst[k] = false;
            _shellFlying = true;
            _shellFrom = from;
            _shellDir = dir;
            _shellStart = now;
            _shellEnd = now + flight;
            _shellTravelled = 0f;
            _shellDirect = close;
            _shellZone = k;
            if (Phase != null) { Phase.SayMiniFire(); }
        }

        /// 이번 프레임에 탄이 지난 구간에 내 기체가 있는가(맞는 사람이 판정한다). 맞으면 내 화면에서는 터지지 않는다.
        private void CheckDirectHit(float fromDistance, float toDistance)
        {
            if (Contacts == null || Contacts.LocalVehicle == null) { return; }
            SaccEntity e = Contacts.LocalVehicle;
            Vector3 offset = e.transform.position - _shellFrom;
            float along = Vector3.Dot(offset, _shellDir);
            if (along < fromDistance || along > toDistance) { return; }
            if ((offset - _shellDir * along).sqrMagnitude > DirectHitRadius * DirectHitRadius) { return; }
            _shellDirect = false;
            _zoneOn[_shellZone] = false;
            e.WeaponDamageVehicle(DirectDamage, gameObject, 0);
            Vector3 point = _shellFrom + _shellDir * along;
            if (BurstFlash)
            {
                var emit = new ParticleSystem.EmitParams();
                emit.position = point;
                BurstFlash.Emit(emit, 1);
            }
            if (BurstSound)
            {
                BurstSound.transform.position = point;
                BurstSound.Play();
            }
        }

        private void UpdateZones()
        {
            float now = Time.time;
            for (int k = 0; k < MAX_ZONES; k++)
            {
                if (!_zoneOn[k] || now < _zoneStart[k]) { continue; }
                if (!_zoneBurst[k])
                {
                    _zoneBurst[k] = true;
                    Burst(_zonePos[k]);
                }
                if (now >= _zoneEnd[k])
                {
                    _zoneOn[k] = false;
                    continue;
                }
                HurtLocalVehicle(_zonePos[k], now);
            }
        }

        private void Burst(Vector3 point)
        {
            var emit = new ParticleSystem.EmitParams();
            emit.position = point;
            emit.applyShapeToPosition = true;
            if (BurstParticle) { BurstParticle.Emit(emit, BurstCount); }
            if (BurstFlash) { BurstFlash.Emit(emit, 1); }
            if (BurstSound)
            {
                BurstSound.transform.position = point;
                BurstSound.Play();
            }
        }

        /// 폭발 지역 안의 내 기체(맞는 사람이 판정한다).
        private void HurtLocalVehicle(Vector3 point, float now)
        {
            if (now < _nextHurt || Contacts == null || Contacts.LocalVehicle == null) { return; }
            SaccEntity e = Contacts.LocalVehicle;
            if ((e.transform.position - point).sqrMagnitude > ZoneRadius * ZoneRadius) { return; }
            _nextHurt = now + ZoneTick;
            e.WeaponDamageVehicle(ZoneDamage, gameObject, 0);
        }
    }
}
