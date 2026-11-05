using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 소형 레일건(랜스 포대 방공 부위): 표적이 LeadTime초 뒤에 있을 자리를 예측해 쏘고, 탄이 그 자리에 닿으면 터져 ZoneTime초 동안 폭발 지역이 남는다.
    /// 직진하면 예측대로 폭발 속으로 들어가고, 쏜 뒤 방향을 틀면 피한다. 지역 안에 있는 동안 ZoneTick마다 피해를 입는다.
    /// 레이저: 쏘기 AimTime초 전부터 예측 자리를 따라다니고(조준), 쏘는 순간 그 자리에 고정돼 탄이 닿을 때까지 남는다 — 플레이어가 보고 피한다.
    /// 탐지·조준·체력·부활은 같은 오브젝트의 ACO_AAGunSite가 맡는다(기총 없이 — 그래서 포대 부위 계약과 수리가 그대로다). 이 스크립트는 사격만 한다.
    /// 포대 오너가 쏠 자리를 정해 모두에게 알린다(NetFire). 탄·폭발은 각자 그리고, 피해는 맞는 사람이 자기 화면의 폭발 지역으로 판정한다.
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
        [Tooltip("몇 초 뒤의 자리를 노리는가 — 탄이 날아가는 시간과 같다")]
        public float LeadTime = 3f;
        [Tooltip("사격 간격(초)")]
        public float FireInterval = 7f;
        [Tooltip("이 거리(m)보다 가까우면 쏘지 않는다")]
        public float MinRange = 700f;
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
        private float _fireAt;      // 쏠 시각(오너)
        private Vector3 _lockPoint;
        private float _lockUntil;

        void Start()
        {
            if (!Site) { Site = GetComponent<ACO_AAGunSite>(); }
            _fireTimer = Random.Range(0f, FireInterval);   // 네 문이 한꺼번에 쏘지 않게
            if (Laser) { Laser.enabled = false; }
        }

        void Update()
        {
            UpdateZones();
            UpdateLaser();
            if (!Site || !Networking.IsOwner(gameObject)) { return; }
            if (_fireAt > 0f)
            {
                // 조준이 끝났다: 표적을 잃었으면 쏘지 않는다(레이저는 스스로 꺼진다)
                if (Time.time < _fireAt) { return; }
                _fireAt = 0f;
                Vector3 point;
                if (PredictPoint(out point)) { SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetFire), point); }
                return;
            }
            _fireTimer -= Time.deltaTime;
            if (_fireTimer > 0f) { return; }
            Vector3 check;
            if (!PredictPoint(out check)) { return; }
            _fireTimer = FireInterval * Random.Range(0.85f, 1.15f);
            _fireAt = Time.time + AimTime;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetAim));
        }

        /// 표적이 LeadTime초 뒤에 있을 자리. 쏠 수 없으면 false.
        private bool PredictPoint(out Vector3 point)
        {
            point = Vector3.zero;
            if (!Site.IsEngaged()) { return false; }
            Transform target = Site.GetTargetTransform();
            if (target == null) { return false; }
            point = target.position + Site.GetTargetVelocity() * LeadTime;
            return (point - transform.position).sqrMagnitude >= MinRange * MinRange;
        }

        /// 조준 시작(모두가 받는다) — 레이저가 예측 자리를 따라다닌다.
        [NetworkCallable]
        public void NetAim()
        {
            _aiming = true;
            _aimUntil = Time.time + AimTime + 1f;   // 사격이 안 오면(표적을 잃었으면) 스스로 꺼진다
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

        /// 모두가 받는다: 탄을 그리고 LeadTime 뒤 그 자리에 폭발 지역을 연다. 레이저는 그 자리에 고정된다.
        [NetworkCallable]
        public void NetFire(Vector3 point)
        {
            _aiming = false;
            _lockPoint = point;
            _lockUntil = Time.time + LeadTime;
            Vector3 from = Muzzle != null ? Muzzle.position : transform.position;
            if (ShellParticle)
            {
                var emit = new ParticleSystem.EmitParams();
                emit.position = from;
                emit.velocity = (point - from) / Mathf.Max(LeadTime, 0.1f);
                emit.startLifetime = LeadTime;
                ShellParticle.Emit(emit, 1);
            }
            if (FireSound)
            {
                FireSound.transform.position = from;
                FireSound.Play();
            }
            int k = _nextZone;
            _nextZone = (_nextZone + 1) % MAX_ZONES;
            _zonePos[k] = point;
            _zoneStart[k] = Time.time + LeadTime;
            _zoneEnd[k] = _zoneStart[k] + ZoneTime;
            _zoneOn[k] = true;
            _zoneBurst[k] = false;
            if (Phase != null) { Phase.SayMiniFire(); }
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
