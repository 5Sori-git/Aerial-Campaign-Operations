using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 소형 레일건(랜스 포대 방공 부위·육상전함 주포): 깔아 두는 사격 — 표적이 LeadTime초 뒤에 있을 자리를 예측해 포신을 고정하자마자 쏜다.
    /// 탄은 표적보다 먼저 그 자리에 닿아 앞에서 터지고, 폭발 지역(ZoneTime초)이 남는다. 직진하면 그 안으로 들어가고, 앞에서 터진 폭발을 보고 방향을 틀면 피한다.
    /// 레이저: 고정 AimTime초 전부터 예측 자리를 따라다니고, 고정되면 탄이 닿을 때까지 남는다. 레이저는 포신 축(Muzzle 앞)으로 그린다 — 포신이 그 자리를 따라간다. 예측 자리가 MinRange보다 가까우면 탄은 MinRange까지 날아가 터지고, 그 안에서는 직격만 맞는다.
    /// 탐지·조준·체력·부활은 같은 오브젝트의 ACO_AAGunSite가 맡는다(기총 없이 — 그래서 포대 부위 계약과 수리가 그대로다). 이 스크립트는 사격만 한다.
    /// 포대 오너가 쏠 자리를 정해 모두에게 알린다(NetLock). 탄·폭발은 각자 그리고, 피해는 맞는 사람이 자기 화면의 폭발 지역으로 판정한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_MiniRailgun : UdonSharpBehaviour
    {
        private const int MAX_ZONES = 6;

        [Header("참조")]
        public ACO_AAGunSite Site;
        public Transform Muzzle;
        [Tooltip("내가 탄 기체를 알려 준다(Bake가 넣는다)")]
        public ACO_RadarContacts Contacts;
        [Tooltip("Bake가 넣는다 — 첫 사격 무전")]
        public ACO_PhaseRailgun Phase;

        [Header("사격")]
        [Tooltip("몇 초 뒤 표적 자리를 노리는가(초). 고정하자마자 쏘므로 폭발은 표적 앞에 먼저 생기고, ZoneTime이 이보다 길어야 표적이 닿을 때까지 남는다")]
        public float LeadTime = 3.5f;
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
        [Tooltip("한 번 고정에 쏘는 발 수(1 또는 2). 둘째 발은 표적이 VolleyGap초 더 간 자리를 노린다(육상전함 주포)")]
        public int VolleyShots = 1;
        [Tooltip("연발 간격(초)")]
        public float VolleyGap = 0.8f;

        [Header("폭발 지역")]
        public float ZoneRadius = 170f;
        [Tooltip("폭발 지역이 남는 시간(초)")]
        public float ZoneTime = 3.5f;
        [Tooltip("지역 안에서 피해를 입는 간격(초)")]
        public float ZoneTick = 0.5f;
        public float ZoneDamage = 20f;
        [Tooltip("폭발을 가운데 둘레에 몇 곳 더 터뜨리는가(큰 폭발 지역이 넓게 보이게). 0 = 가운데만")]
        public int BurstSpread = 0;
        [Tooltip("폭발 지역이 남아 있는 동안 이 간격(초)으로 불꽃을 다시 뿜는다(오래 남는 폭발). 0 = 안 함")]
        public float ZoneReburst = 0f;

        [Header("연출 (프리팹 빌더가 넣는다 — 소형 레일건끼리 함께 쓴다)")]
        public ParticleSystem ShellParticle;
        public ParticleSystem BurstParticle;
        public ParticleSystem BurstFlash;
        public int BurstCount = 26;
        [Tooltip("공중 폭발 불티(소형 레일건끼리 함께 쓴다)")]
        public ParticleSystem BurstSparks;
        public int BurstSparkCount = 40;
        [Tooltip("공중 폭발 고리(소형 레일건끼리 함께 쓴다). 궤적의 고리는 Shell의 서브 이미터 Rings가 남긴다")]
        public ParticleSystem BurstRing;
        [Tooltip("공중 폭발 고리 지름 = 폭발 지역 반경 × 이 값")]
        public float BurstRingScale = 2.6f;
        [Tooltip("사선 레이저 — 이 문의 것")]
        public LineRenderer Laser;
        [Tooltip("조준 중 레이저 굵기(m)")]
        public float LaserAimWidth = 3f;
        [Tooltip("고정된 레이저 굵기(m)")]
        public float LaserLockWidth = 7f;
        [Tooltip("레이저 길이(m) — 탄착 지점을 지나 이만큼 뻗어 나간다")]
        public float LaserReach = 15000f;
        [Tooltip("폭발 지역(피해 구)을 보여 주는 반투명 껍질 — 지역 칸마다 하나(MAX_ZONES개, 지름 1 구, ACO/ZoneShell). 시작할 때 월드로 떼어 낸다(배가 움직여도 지역은 그 자리)")]
        public Transform[] ZoneShells;
        [Tooltip("껍질이 피해 반경까지 커지는 시간(초)")]
        public float ShellGrowTime = 0.25f;
        [Tooltip("피해가 끝나기 전 껍질이 옅어지는 시간(초) — 피해가 끝나는 순간 다 사라진다")]
        public float ShellFadeTime = 0.6f;
        public AudioSource FireSound;
        public AudioSource BurstSound;

        private Vector3[] _zonePos = new Vector3[MAX_ZONES];
        private float[] _zoneStart = new float[MAX_ZONES];
        private float[] _zoneEnd = new float[MAX_ZONES];
        private bool[] _zoneOn = new bool[MAX_ZONES];
        private Material[] _shellMats;
        private bool[] _shellOn = new bool[MAX_ZONES];
        private bool[] _zoneBurst = new bool[MAX_ZONES];
        private float[] _zoneNextBurst = new float[MAX_ZONES];
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
        private bool _volleyPending;   // 둘째 발이 남았다(모든 클라이언트)
        private Vector3 _volleyPoint;

        void Start()
        {
            if (!Site) { Site = GetComponent<ACO_AAGunSite>(); }
            if (Site) { Site.AimLeadTime = LeadTime; }
            _fireTimer = Random.Range(0f, FireInterval);   // 네 문이 한꺼번에 쏘지 않게
            if (Laser) { Laser.enabled = false; }
            InitShells();
        }

        /// 껍질: 월드로 떼어 내고(배가 움직여도 지역은 그 자리) 칸마다 머티리얼을 따로 쓴다(옅어지는 값이 칸마다 다르다).
        private void InitShells()
        {
            int n = ZoneShells != null ? ZoneShells.Length : 0;
            _shellMats = new Material[n];
            for (int k = 0; k < n; k++)
            {
                Transform s = ZoneShells[k];
                if (s == null) { continue; }
                s.SetParent(null, false);
                MeshRenderer r = s.GetComponent<MeshRenderer>();
                if (r != null) { _shellMats[k] = r.material; }
                s.gameObject.SetActive(false);
            }
        }

        void OnDisable() { HideShells(); }

        private void HideShells()
        {
            if (ZoneShells == null) { return; }
            for (int k = 0; k < ZoneShells.Length && k < MAX_ZONES; k++)
            {
                if (ZoneShells[k] != null) { ZoneShells[k].gameObject.SetActive(false); }
                _shellOn[k] = false;
            }
        }

        /// 지역 k의 껍질: 터진 뒤 ShellGrowTime 동안 피해 반경까지 커지고, 끝나기 ShellFadeTime 전부터 옅어져 끝나는 순간 사라진다.
        private void UpdateShell(int k, float now)
        {
            if (ZoneShells == null || k >= ZoneShells.Length || ZoneShells[k] == null) { return; }
            Transform s = ZoneShells[k];
            if (!_shellOn[k])
            {
                _shellOn[k] = true;
                s.SetPositionAndRotation(_zonePos[k], Quaternion.identity);
                s.gameObject.SetActive(true);
            }
            float age = now - _zoneStart[k];
            float grow = Mathf.Clamp01(age / Mathf.Max(ShellGrowTime, 0.01f));
            grow = 1f - (1f - grow) * (1f - grow);
            s.localScale = Vector3.one * (ZoneRadius * 2f * Mathf.Max(grow, 0.02f));
            Material m = _shellMats != null && k < _shellMats.Length ? _shellMats[k] : null;
            if (m != null)
            {
                float left = _zoneEnd[k] - now;
                float fade = Mathf.Clamp01(left / Mathf.Max(ShellFadeTime, 0.01f));
                m.SetFloat("_Fade", Mathf.Min(fade, 0.35f + 0.65f * grow));
            }
        }

        private void EndShell(int k)
        {
            if (!_shellOn[k]) { return; }
            _shellOn[k] = false;
            if (ZoneShells != null && k < ZoneShells.Length && ZoneShells[k] != null) { ZoneShells[k].gameObject.SetActive(false); }
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
                if (!PredictPoint(out point)) { return; }
                if (VolleyShots >= 2)
                {
                    Vector3 second = Site.GetTargetTransform().position + Site.GetTargetVelocity() * (LeadTime + VolleyGap);
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetLockVolley), point, second);
                }
                else { SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetLock), point); }
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
            // 레이저는 포신을 따라 나간다(포구 자식 Muzzle의 앞 = 포신 축). 표적 자리로 곧게 그리면 포탑이 도는 동안·고각 한계에서
            // 포신과 어긋나 보였다. 포신이 그 자리를 따라가므로(조준 중엔 예측 자리, 고정 뒤엔 LockAim) 다 돌면 그 자리를 지난다
            Vector3 from = Muzzle != null ? Muzzle.position : transform.position;
            Vector3 dir = Muzzle != null ? Muzzle.forward : (end - from).normalized;
            Laser.SetPosition(0, from);
            Laser.SetPosition(1, from + dir * LaserReach);
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
            // 깔아 두기: 고정하자마자 쏜다(다음 프레임). 레이저는 탄이 닿을 때까지 남는다
            _shotFireAt = Time.time;
            _lockUntil = Time.time + FlightTime(MuzzlePosition(), point);
            if (Site) { Site.LockAim(point); }
        }

        /// 모두가 받는다: 두 발 연사 — 첫 발은 NetLock과 같고, 둘째 발은 첫 발 VolleyGap초 뒤에 second(표적이 VolleyGap초 더 간 자리)를 노려 쏜다.
        [NetworkCallable]
        public void NetLockVolley(Vector3 first, Vector3 second)
        {
            NetLock(first);
            _volleyPending = true;
            _volleyPoint = second;
        }

        private void UpdateShot()
        {
            if (_shotPending)
            {
                if (Site && !Site.IsAlive())
                {
                    // 쏘기 전에 부서졌다
                    _shotPending = false;
                    _volleyPending = false;
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
            if (Site && !_shotPending) { Site.UnlockAim(); }
        }

        /// 고정한 자리로 쏜다. 그 자리가 최소 폭발 거리 안이면 탄은 지나쳐 최소 폭발 거리에서 터진다.
        private void FireShell()
        {
            _shotPending = false;
            Vector3 from = MuzzlePosition();
            Vector3 toPoint = _shotPoint - from;
            float distance = toPoint.magnitude;
            // 탄도 레이저처럼 포신 축(Muzzle 앞)으로 나간다 — 포신이 고정 자리에 다 돌았으면 그 자리, 고각 한계 밖 표적이면 한계 각도로 빗나간다.
            // 터지는 거리는 고정 자리까지의 거리(최소 폭발 거리 안이면 MinRange). 포신 자세는 클라이언트마다 같은 규칙으로 돌아 거의 같다
            Vector3 dir = Muzzle != null ? Muzzle.forward : (distance > 0.01f ? toPoint / distance : transform.forward);
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
            if (_volleyPending)
            {
                // 둘째 발: 포신을 둘째 자리(표적이 VolleyGap초 더 간 자리)로 고정하고 VolleyGap초 뒤에 쏜다
                _volleyPending = false;
                _shotPending = true;
                _shotPoint = _volleyPoint;
                _lockPoint = _volleyPoint;
                _shotFireAt = now + VolleyGap;
                _lockUntil = Mathf.Max(_lockUntil, _shotFireAt + FlightTime(from, _volleyPoint));
                if (Site) { Site.LockAim(_volleyPoint); }
            }
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
                    _zoneNextBurst[k] = now + ZoneReburst;
                    _fireLife = ZoneTime;
                    Burst(_zonePos[k]);
                }
                else if (ZoneReburst > 0f && now >= _zoneNextBurst[k] && now < _zoneEnd[k] - 0.5f)
                {
                    _zoneNextBurst[k] = now + ZoneReburst;
                    _fireLife = _zoneEnd[k] - now;
                    Reburst(_zonePos[k]);
                }
                if (now >= _zoneEnd[k])
                {
                    _zoneOn[k] = false;
                    EndShell(k);
                    continue;
                }
                UpdateShell(k, now);
                HurtLocalVehicle(_zonePos[k], now);
            }
        }

        // 폭발 이펙트는 피해 구(반경 ZoneRadius) 안에 들어가게 크기를 정해 내보낸다 — 예전엔 프리팹 크기(불덩이 120~260m, 섬광 700m,
        // 고리 반경 × 2.6, 불티가 1km 가까이 날아감)라 판정보다 훨씬 넓게 보였다. 불덩이는 커지며(Grow 최대 1.15배) 반경의 약 90%까지 닿는다.
        private const float FIRE_GROW = 1.15f;
        private float _fireLife = 3f;   // 이번에 내보내는 불덩이의 최대 수명 = 폭발 지역에 남은 시간

        private void Burst(Vector3 point)
        {
            float r = ZoneRadius;
            // 가운데 불덩이: 반경 35% 안에 지름 0.9R — 끝이 반경 약 87%까지
            if (BurstParticle)
            {
                int n = Mathf.Max(BurstCount / 3, 1);
                for (int i = 0; i < 3; i++) { EmitFire(point + Random.insideUnitSphere * (r * 0.35f), r * Random.Range(0.75f, 0.9f), n); }
            }
            if (BurstFlash)
            {
                var flash = new ParticleSystem.EmitParams();
                flash.position = point;
                flash.applyShapeToPosition = false;
                flash.startSize = r * 1.8f;   // 지름 = 구 지름의 90%
                BurstFlash.Emit(flash, 1);
            }
            if (BurstSparks)
            {
                // 불티는 최고 480m/s — 반경 안에서 사라지게 수명을 줄인다
                var sp = new ParticleSystem.EmitParams();
                sp.position = point;
                sp.applyShapeToPosition = true;
                sp.startLifetime = Mathf.Clamp(r * 0.9f / 480f, 0.15f, 2f);
                BurstSparks.Emit(sp, BurstSparkCount);
            }
            for (int i = 0; i < BurstSpread; i++)
            {
                // 가운데 둘레(반경 55%)에 작은 불덩이 — 지름 0.6R이라 끝이 반경 약 90%
                float a = i * Mathf.PI * 2f / BurstSpread;
                Vector3 around = point + new Vector3(Mathf.Cos(a), Random.Range(-0.3f, 0.3f), Mathf.Sin(a)) * (r * 0.55f);
                if (BurstParticle) { EmitFire(around, r * 0.6f, Mathf.Max(BurstCount / 4, 1)); }
            }
            if (BurstRing)
            {
                var ring = new ParticleSystem.EmitParams();
                ring.position = point;
                ring.velocity = _shellDir * 40f;   // 고리 방향은 속도가 정한다 — 0이면 안 된다
                ring.startSize = r * Mathf.Min(BurstRingScale, 2f);   // 고리 지름이 구 지름을 넘지 않게
                BurstRing.Emit(ring, 1);
            }
            if (BurstSound)
            {
                BurstSound.transform.position = point;
                BurstSound.Play();
            }
        }

        /// 불덩이 count개를 at에 지름 size로(커지며 FIRE_GROW배). 모양 오프셋은 쓰지 않는다 — 크기를 반경에 맞추려고.
        private void EmitFire(Vector3 at, float size, int count)
        {
            var emit = new ParticleSystem.EmitParams();
            emit.position = at;
            emit.applyShapeToPosition = false;
            emit.startSize = size / FIRE_GROW;
            // 불덩이는 폭발 지역이 끝나기 전에 사라진다(프리팹 수명 1.8~3.4초가 지역보다 길면 피해가 끝난 뒤에도 불이 남아 보였다)
            emit.startLifetime = Mathf.Clamp(_fireLife, 0.3f, 3.4f) * Random.Range(0.7f, 1f);
            BurstParticle.Emit(emit, count);
        }

        /// 남아 있는 폭발 지역에서 불꽃을 다시 뿜는다(섬광·고리·소리 없이) — 반경 45% 안에 지름 0.8R(끝이 반경 약 85%).
        private void Reburst(Vector3 point)
        {
            if (!BurstParticle) { return; }
            EmitFire(point + Random.insideUnitSphere * (ZoneRadius * 0.45f), ZoneRadius * 0.8f, Mathf.Max(BurstCount / 2, 1));
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
