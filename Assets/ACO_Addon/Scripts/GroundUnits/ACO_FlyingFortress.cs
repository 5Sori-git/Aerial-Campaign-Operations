using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 공중 요새(거대 보스) 비행·단계 표현. 체력·부위·스폰은 같은 오브젝트의 ACO_Warship(ExternalPose)이 맡고, 이 스크립트는 자리를 옮기고 단계를 화면에 반영한다.
    /// 단계·기준값은 ACO_PhaseFortress가 동기화한다(관리자 오너가 정한다) — 여기선 읽기만 한다. 위치 자체는 동기화하지 않는다.
    ///  - 궤도(0·1단계): 도시(순항 중심) 둘레 원. 각도 = 기준 각도 + 각속도 × 기준 시각부터 흐른 시간. 엔진이 부서지면 기준을 다시 잡아 느려진다(이어 붙으므로 튀지 않는다).
    ///  - 돌진(2단계): 궤도에서 안쪽으로 TurnRadius만큼 선회해 도시를 향한 뒤 곧게 들어간다. 시작부터 끝까지 일정한 비율로 내려가 도시 상공 DiveEndHeight에 닿으면 도시 충돌 = 미션 실패(모듈이 판정).
    ///    이어짐: 돌진은 잠시 뒤로 예약되고(모듈), 시작 자리는 그 시각의 궤도 자리라 모든 클라이언트가 같은 자리에서 전환한다. 방향은 궤도 접선 그대로, 속도는 DiveAccelTime 동안 궤도 속도에서 DiveSpeed까지, 기울기는 경로 위치의 함수다.
    ///  - 격추: 본체(코어) SaccTarget Explode → 그 자리 속도로 떨어지며 동체가 연쇄 폭발하고, 지형·수면에 닿으면 대폭발(ACO_FortressBlast, 피해 없음).
    /// 단계 표현: 1단계부터 엔진을 드러내고(락온·피해), 2단계부터 코어를 드러낸다(덮개 숨김, 히트박스·락온 마커 켬). 드러나기 전에는 맞지도 잠기지도 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_FlyingFortress : UdonSharpBehaviour
    {
        public const int STAGE_AIR_DEFENSE = 0;
        public const int STAGE_ENGINES = 1;
        public const int STAGE_CORE = 2;

        [Header("참조")]
        public ACO_Warship Warship;
        [Tooltip("Bake가 넣는다 — 동기화된 단계·기준값")]
        public ACO_PhaseFortress Phase;
        [Tooltip("코어 체력(본체 SaccTarget)")]
        public SaccTarget TargetHealth;
        [Tooltip("방공 부위(ACO_AAGunSite·ACO_SAMSite) — 1단계 목표. 2·3단계에 일부가 수리돼 다시 돈다")]
        public UdonSharpBehaviour[] AirDefense;
        public ACO_FortressPart[] Engines;

        [Header("코어")]
        public Collider CoreHitbox;
        [Tooltip("코어 락온 마커(적 항공기 레이어 — 공대공 무장만 잠긴다)")]
        public GameObject[] CoreMarkers;
        [Tooltip("코어를 덮은 장갑 — 드러나면 숨긴다")]
        public GameObject[] CoreShutters;
        [Tooltip("격납고 문(모델의 뼈대)")]
        public Transform[] Hatches;
        [Tooltip("Hatches와 같은 순서 — 이 단계부터 열린다(1 = 엔진, 2 = 코어). 비어 있으면 코어 단계")]
        public int[] HatchOpenStage;
        [Tooltip("Hatches와 같은 순서 — 닫힌 회전(로컬)")]
        public Quaternion[] HatchClosed;
        [Tooltip("Hatches와 같은 순서 — 열린 회전(로컬)")]
        public Quaternion[] HatchOpen;
        [Tooltip("문이 다 열리는 시간(초)")]
        public float HatchOpenTime = 4f;
        [Tooltip("문이 닫혀 있는 동안 기총을 막는 콜라이더 — 코어 단계에 끈다")]
        public Collider[] HatchBlockers;
        [Tooltip("증원 적기가 나오는 자리(측면 해치). 앞(Z)이 나가는 방향이다. 비어 있으면 요새 둘레에 나온다")]
        public Transform[] LaunchPoints;

        [Header("궤도")]
        [Tooltip("도시 둘레 궤도 반경(m)")]
        public float OrbitRadius = 7000f;
        [Tooltip("궤도 속도(m/s) — 엔진이 모두 멀쩡할 때")]
        public float OrbitSpeed = 60f;
        [Tooltip("해수면 고도(m) — 비구름층(1300~1800m) 위")]
        public float Altitude = 2400f;
        [Tooltip("고도가 천천히 오르내리는 폭(m)·주기(초)")]
        public float BobHeight = 50f;
        public float BobPeriod = 40f;
        [Tooltip("궤도를 도는 동안 안쪽으로 기우는 각도(도)")]
        public float BankAngle = 4f;
        [Tooltip("엔진 하나가 부서질 때마다 더 기우는 각도(도)")]
        public float EngineLossTilt = 2.5f;

        [Header("돌진")]
        [Tooltip("돌진 속도(m/s)")]
        public float DiveSpeed = 60f;
        [Tooltip("돌진 최소 시간(초) — 코어를 부술 시간을 보장한다")]
        public float DiveMinTime = 110f;
        [Tooltip("돌진이 끝나는 높이(도시 지면 위 m)")]
        public float DiveEndHeight = 150f;
        [Tooltip("선회 반경(m) — 궤도에서 도시를 향하기까지. 궤도 반경의 40%를 넘지 않는다")]
        public float TurnRadius = 2000f;
        [Tooltip("선회할 때 기우는 각도(도)")]
        public float TurnBank = 8f;
        [Tooltip("궤도 속도에서 DiveSpeed까지 올리는 시간(초)")]
        public float DiveAccelTime = 20f;
        [Tooltip("돌진 중 기수를 숙이는 최대 각도(도) — 내려가는 각도보다 작게 두면 수평에 가까운 자세로 가라앉는다")]
        public float GlidePitch = 6f;

        [Header("격추")]
        [Tooltip("떨어지는 가속도(m/s²)")]
        public float CrashGravity = 12f;
        [Tooltip("초당 기수를 숙이는 각도(도)")]
        public float CrashPitchRate = 3f;
        [Tooltip("동체 연쇄 폭발 간격(초)")]
        public float SecondaryInterval = 2.5f;
        [Tooltip("충돌로 치는 층: 지형·건물(Default, Environment) + 수면(Water — 트리거)")]
        public LayerMask ImpactLayers = 2065;
        [Tooltip("중심이 지면에서 이만큼(m) 떨어졌을 때 충돌로 친다 — 동체 두께")]
        public float ImpactClearance = 40f;

        [Header("대폭발 (연출 전용 — 피해 없음)")]
        public ACO_FortressBlast Blast;
        [Tooltip("도시에 충돌할 때 폭발 크기 배율")]
        public float CityBlastScale = 1.5f;

        private bool _initialized;
        private bool _spawned;
        private bool _crashing;
        private int _appliedStage = -1;
        private int _spawnMs;
        private float _spawnAngle;
        private SaccTarget[] _defenseHealth;
        private int _lastEngineLoss;
        private float _tilt;
        private float _tiltTarget;
        private float _engineTimer;
        private Vector3 _velocity;
        private Vector3 _lastPos;
        private bool _hasLast;
        private Vector3 _crashSpin;
        private bool _impacted;
        private float _impactAt;
        private float[] _hatchOpen;   // 문마다 0 = 닫힘, 1 = 열림
        private float _secondaryTimer;

        // 돌진 경로(돌진 기준값으로 한 번 계산 — 모든 클라이언트가 같은 값을 얻는다)
        private bool _pathReady;
        private int _pathMs;
        private Vector3 _turnCenter;
        private float _turnStart;     // 선회 시작 각도(라디안, 선회 중심 기준)
        private float _turnRadius;
        private float _turnLength;
        private Vector3 _lineStart;
        private Vector3 _lineDir;
        private float _pathLength;
        private float _startHeight;
        private float _endHeight;
        private float _startSpeed;
        private float _cruiseSpeed;
        private float _accelTime;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!Warship) { Warship = GetComponent<ACO_Warship>(); }
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            int n = AirDefense != null ? AirDefense.Length : 0;
            _defenseHealth = new SaccTarget[n];
            for (int i = 0; i < n; i++)
            {
                if (AirDefense[i] != null) { _defenseHealth[i] = AirDefense[i].GetComponent<SaccTarget>(); }
            }
            _hatchOpen = new float[Hatches != null ? Hatches.Length : 0];
            ApplyStage(STAGE_AIR_DEFENSE);
        }

        // ───────────── 함선 이벤트 ─────────────

        /// ACO_Warship.MissionSpawn이 부른다(모든 클라이언트): 궤도 시작.
        public void ShipSpawned()
        {
            Init();
            _spawned = true;
            _crashing = false;
            _impacted = false;
            _pathReady = false;
            _hasLast = false;
            _velocity = Vector3.zero;
            _tilt = 0f;
            _tiltTarget = 0f;
            _lastEngineLoss = 0;
            _spawnMs = Networking.GetServerTimeInMilliseconds();
            _spawnAngle = Warship != null ? Warship.GetCruiseYaw() * Mathf.Deg2Rad : 0f;
            _appliedStage = -1;
            ApplyStage(CurrentStage());
            CloseHatches();
            UpdatePose(0f);
        }

        public void ShipDespawned()
        {
            _spawned = false;
            _crashing = false;
            _impacted = false;
        }

        /// 코어 파괴(SaccTarget.ExplodeOther — 모든 클라이언트): 지금 속도로 떨어진다.
        public void Explode()
        {
            if (_impacted) { return; }
            _crashing = true;
            _secondaryTimer = 0f;
            _crashSpin = new Vector3(CrashPitchRate, 0f, _tilt >= 0f ? 1.5f : -1.5f);
            SetAll(CoreMarkers, false);
            if (Phase != null && Phase.IsLive()) { Phase.SayDown(); }
        }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            if (!_spawned || _impacted) { return; }
            float dt = Time.deltaTime;
            if (_crashing)
            {
                UpdateCrash(dt);
                return;
            }
            int stage = CurrentStage();
            if (stage != _appliedStage) { ApplyStage(stage); }
            UpdateHatches(stage, dt);
            if (stage >= STAGE_CORE && GetDiveProgress(Networking.GetServerTimeInMilliseconds()) >= 1f)
            {
                Impact(Warship != null ? Warship.GetCruiseCenter() : transform.position, false, CityBlastScale, true);
                return;
            }
            _engineTimer -= dt;
            if (_engineTimer <= 0f)
            {
                _engineTimer = 0.5f;
                int lost = Engines != null ? Engines.Length - CountEnginesAlive() : 0;
                // 좌우 번갈아 부서진 만큼 기운다(짝수 칸 = 왼쪽, 홀수 칸 = 오른쪽 — 빌더 순서)
                if (lost != _lastEngineLoss) { _lastEngineLoss = lost; _tiltTarget = EngineTiltSum(); }
                if (Phase != null) { Phase.CheckLines(this); }
            }
            _tilt = Mathf.MoveTowards(_tilt, _tiltTarget, dt * 1.5f);
            UpdatePose(dt);
        }

        private int CurrentStage()
        {
            return Phase != null && Phase.IsLive() ? Phase.GetStage() : STAGE_AIR_DEFENSE;
        }

        private float EngineTiltSum()
        {
            float sum = 0f;
            if (Engines == null) { return 0f; }
            for (int i = 0; i < Engines.Length; i++)
            {
                if (Engines[i] != null && Engines[i].IsDestroyed()) { sum += (i % 2 == 0 ? 1f : -1f) * EngineLossTilt; }
            }
            return sum;
        }

        private void UpdatePose(float dt)
        {
            int now = Networking.GetServerTimeInMilliseconds();
            Vector3 pos;
            Vector3 fwd;
            float bank;
            if (CurrentStage() >= STAGE_CORE && now - Phase.GetDiveMs() >= 0) { EvaluateDive(now, out pos, out fwd, out bank); }
            else { EvaluateOrbit(GetOrbitAngle(now), now, out pos, out fwd); bank = BankAngle; }
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, bank + _tilt);
            transform.SetPositionAndRotation(pos, rot);
            if (_hasLast && dt > 0f) { _velocity = Vector3.Lerp(_velocity, (pos - _lastPos) / dt, 1f - Mathf.Exp(-dt * 3f)); }
            _lastPos = pos;
            _hasLast = true;
        }

        private void UpdateCrash(float dt)
        {
            _velocity += Vector3.down * (CrashGravity * dt);
            _secondaryTimer -= dt;
            if (_secondaryTimer <= 0f)
            {
                _secondaryTimer = SecondaryInterval;
                if (Blast) { Blast.PlaySecondary(transform.TransformPoint(new Vector3(Random.Range(-180f, 180f), Random.Range(-20f, 30f), Random.Range(-150f, 150f)))); }
            }
            Vector3 from = transform.position;
            Vector3 step = _velocity * dt;
            float len = step.magnitude;
            RaycastHit hit;
            if (len > 0f && Physics.Raycast(from, step / len, out hit, len + ImpactClearance, ImpactLayers.value, QueryTriggerInteraction.Collide))
            {
                Impact(hit.point, hit.collider.gameObject.layer == 4, 1f, false);
                return;
            }
            // 안전장치: 지면을 못 찾고 도시 높이보다 한참 내려갔다
            Vector3 center = Warship != null ? Warship.GetCruiseCenter() : Vector3.zero;
            if (from.y < center.y - 300f)
            {
                Impact(from, false, 1f, false);
                return;
            }
            transform.SetPositionAndRotation(from + step, transform.rotation * Quaternion.Euler(_crashSpin * dt));
        }

        /// 충돌: 대폭발을 재생하고 동체를 지하로 치운다. 부위가 자식이라 끄면 다음 스폰 때 되살리는 순서가 꼬인다 — 옮기는 것으로 숨긴다.
        /// 옮기는 건 결과가 날 때까지만이다 — ACO_PhaseFortress가 거둔다. 격추는 성공 때, 도시 충돌은 체력이 남아 부위가 살아 있으므로 CityHideDelay 뒤.
        private void Impact(Vector3 point, bool water, float scale, bool city)
        {
            _crashing = false;
            _impacted = true;
            _impactAt = Time.time;
            if (Blast) { Blast.Play(point, water, scale); }
            transform.position = point + Vector3.down * 3000f;
            if (Phase != null && Phase.IsLive()) { Phase.SayImpact(city); }
        }

        /// 충돌한 뒤 지난 시간(초). 아직이면 -1.
        public float GetImpactAge() { return _impacted ? Time.time - _impactAt : -1f; }

        // ───────────── 궤도·돌진 ─────────────

        /// 서버 시각 now의 궤도 각도(라디안). 모듈이 엔진 파괴 때 이 값으로 기준을 다시 잡는다.
        public float GetOrbitAngle(int now)
        {
            float baseAngle = _spawnAngle;
            int baseMs = _spawnMs;
            float scale = 1f;
            if (Phase != null && Phase.IsLive())
            {
                baseAngle = Phase.GetBaseAngle();
                baseMs = Phase.GetBaseMs();
                scale = Phase.GetSpeedScale();
            }
            return baseAngle + OrbitSpeed * scale / Mathf.Max(OrbitRadius, 1f) * ((now - baseMs) * 0.001f);
        }

        private void EvaluateOrbit(float angle, int now, out Vector3 pos, out Vector3 fwd)
        {
            Vector3 center = Warship != null ? Warship.GetCruiseCenter() : Vector3.zero;
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            // 고도 오르내림: 서버 시각의 나머지로 — int 경과 시간이 커도 자릿수를 잃지 않는다
            float period = Mathf.Max(BobPeriod, 1f);
            float bob = Mathf.Sin(((now % Mathf.RoundToInt(period * 1000f)) * 0.001f) / period * Mathf.PI * 2f) * BobHeight;
            pos = new Vector3(center.x + c * OrbitRadius, Altitude + bob, center.z + s * OrbitRadius);
            fwd = new Vector3(-s, 0f, c);
        }

        /// 돌진 경로 계산: 궤도 접선으로 들어가 왼쪽(도시 쪽)으로 돌아, 선회 원에서 도시로 그은 접선을 따라 곧게 간다.
        private void PreparePath()
        {
            if (_pathReady && _pathMs == Phase.GetDiveMs()) { return; }
            _pathReady = true;
            _pathMs = Phase.GetDiveMs();
            Vector3 p0 = Phase.GetDivePos();
            Vector3 end = DiveEnd();
            Vector3 radial = p0 - end;
            radial.y = 0f;
            float radius = Mathf.Max(radial.magnitude, 1f);
            radial = radial / radius;
            _turnRadius = Mathf.Min(TurnRadius, radius * 0.4f);
            _turnCenter = p0 - radial * _turnRadius;
            _turnStart = Mathf.Atan2(radial.z, radial.x);
            float toCity = radius - _turnRadius;
            float beta = Mathf.Asin(_turnRadius / toCity);
            float sweep = Mathf.PI * 0.5f + beta;
            _turnLength = _turnRadius * sweep;
            float angle = _turnStart + sweep;
            _lineStart = _turnCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _turnRadius;
            _lineDir = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            _pathLength = _turnLength + toCity * Mathf.Cos(beta);
            _startHeight = p0.y;
            _endHeight = end.y;
            _startSpeed = OrbitSpeed * Phase.GetSpeedScale();
            _accelTime = Mathf.Max(DiveAccelTime, 0.01f);
            _cruiseSpeed = Mathf.Max(DiveSpeed, 1f);
            // 최소 시간: 너무 빨리 끝나면 순항 속도를 낮춘다(코어를 부술 시간을 보장한다)
            float accelLength = (_startSpeed + _cruiseSpeed) * 0.5f * _accelTime;
            if (_accelTime + (_pathLength - accelLength) / _cruiseSpeed < DiveMinTime)
            {
                _cruiseSpeed = Mathf.Max((_pathLength - _startSpeed * _accelTime * 0.5f) / Mathf.Max(DiveMinTime - _accelTime * 0.5f, 1f), 1f);
            }
        }

        private void EvaluateDive(int now, out Vector3 pos, out Vector3 fwd, out float bank)
        {
            PreparePath();
            float travelled = GetDiveProgress(now) * _pathLength;
            Vector3 heading;
            if (travelled < _turnLength)
            {
                float angle = _turnStart + travelled / _turnRadius;
                pos = _turnCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _turnRadius;
                heading = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            }
            else
            {
                pos = _lineStart + _lineDir * (travelled - _turnLength);
                heading = _lineDir;
            }
            float t = travelled / _pathLength;
            pos.y = Mathf.Lerp(_startHeight, _endHeight, GlideDescent(t));
            // 자세는 경로보다 얕게 숙인다 — 수평에 가깝게 가라앉는다
            float slope = GlideRate(t) * (_startHeight - _endHeight) / _pathLength;
            float pitch = Mathf.Min(Mathf.Atan(slope), GlidePitch * Mathf.Deg2Rad);
            fwd = heading * Mathf.Cos(pitch) + Vector3.down * Mathf.Sin(pitch);
            // 기울기: 궤도 각도 → 선회 각도 → (선회가 끝날 때) 0
            float turn = travelled / Mathf.Max(_turnLength, 1f);
            if (turn < 0.25f) { bank = Mathf.Lerp(BankAngle, TurnBank, Mathf.SmoothStep(0f, 1f, turn / 0.25f)); }
            else if (turn < 0.75f) { bank = TurnBank; }
            else { bank = Mathf.Lerp(TurnBank, 0f, Mathf.SmoothStep(0f, 1f, (turn - 0.75f) / 0.25f)); }
        }

        private const float GLIDE_EASE = 0.15f;   // 처음·끝의 이 비율에서만 강하율이 바뀐다

        /// 고도 진행도(0~1).
        private float GlideDescent(float t)
        {
            float k = 1f / (2f * GLIDE_EASE * (1f - GLIDE_EASE));
            if (t < GLIDE_EASE) { return t * t * k; }
            if (t > 1f - GLIDE_EASE) { return 1f - (1f - t) * (1f - t) * k; }
            return (t - GLIDE_EASE * 0.5f) / (1f - GLIDE_EASE);
        }

        /// GlideDescent의 기울기.
        private float GlideRate(float t)
        {
            return Mathf.Min(Mathf.Min(t, 1f - t), GLIDE_EASE) / (GLIDE_EASE * (1f - GLIDE_EASE));
        }

        private Vector3 DiveEnd()
        {
            Vector3 center = Warship != null ? Warship.GetCruiseCenter() : Vector3.zero;
            return new Vector3(center.x, center.y + DiveEndHeight, center.z);
        }

        /// 돌진 진행도(경로 길이의 비율, 0~1). 1이면 도시에 닿았다. 가속 구간까지 합친 시간 함수다.
        public float GetDiveProgress(int now)
        {
            if (Phase == null) { return 0f; }
            PreparePath();
            float elapsed = (now - Phase.GetDiveMs()) * 0.001f;
            if (elapsed <= 0f) { return 0f; }
            float travelled;
            if (elapsed < _accelTime) { travelled = _startSpeed * elapsed + (_cruiseSpeed - _startSpeed) * elapsed * elapsed / (2f * _accelTime); }
            else { travelled = (_startSpeed + _cruiseSpeed) * 0.5f * _accelTime + _cruiseSpeed * (elapsed - _accelTime); }
            return Mathf.Clamp01(travelled / Mathf.Max(_pathLength, 1f));
        }

        /// 궤도 위 서버 시각 ms의 자리 — 모듈이 돌진 시작 자리를 정한다.
        public Vector3 GetOrbitPosition(int ms)
        {
            Vector3 pos;
            Vector3 fwd;
            EvaluateOrbit(GetOrbitAngle(ms), ms, out pos, out fwd);
            return pos;
        }

        /// 도시까지 남은 수평 거리(m).
        public float GetCityDistance()
        {
            Vector3 d = DiveEnd() - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        // ───────────── 단계 표현 ─────────────

        private void ApplyStage(int stage)
        {
            _appliedStage = stage;
            if (Engines != null)
            {
                for (int i = 0; i < Engines.Length; i++) { if (Engines[i] != null) { Engines[i].SetExposed(stage >= STAGE_ENGINES); } }
            }
            bool core = stage >= STAGE_CORE;
            if (CoreHitbox) { CoreHitbox.enabled = core; }
            SetAll(CoreMarkers, core);
            SetAll(CoreShutters, !core);
            if (HatchBlockers != null)
            {
                for (int i = 0; i < HatchBlockers.Length; i++) { if (HatchBlockers[i]) { HatchBlockers[i].enabled = !core; } }
            }
        }

        private void SetHatch(int i, float open)
        {
            _hatchOpen[i] = open;
            if (i >= HatchClosed.Length || i >= HatchOpen.Length || !Hatches[i]) { return; }
            Hatches[i].localRotation = Quaternion.Slerp(HatchClosed[i], HatchOpen[i], open);
        }

        private void CloseHatches()
        {
            for (int i = 0; i < _hatchOpen.Length; i++) { SetHatch(i, 0f); }
        }

        /// 문마다 자기 단계가 되면 연다(측면 해치 = 엔진 단계, 후면 해치 = 코어 단계).
        private void UpdateHatches(int stage, float dt)
        {
            float step = dt / Mathf.Max(HatchOpenTime, 0.01f);
            for (int i = 0; i < _hatchOpen.Length; i++)
            {
                if (_hatchOpen[i] >= 1f) { continue; }
                int open = HatchOpenStage != null && i < HatchOpenStage.Length ? HatchOpenStage[i] : STAGE_CORE;
                if (stage >= open) { SetHatch(i, Mathf.MoveTowards(_hatchOpen[i], 1f, step)); }
            }
        }

        /// 증원 적기가 나오는 자리 수(0이면 요새 둘레에 나온다).
        public int GetLaunchPointCount() { return LaunchPoints != null ? LaunchPoints.Length : 0; }
        public Vector3 GetLaunchPosition(int i) { return LaunchPoints[i].position; }
        public float GetLaunchYaw(int i) { return LaunchPoints[i].eulerAngles.y; }

        private void SetAll(GameObject[] list, bool on)
        {
            if (list == null) { return; }
            for (int i = 0; i < list.Length; i++) { if (list[i]) { list[i].SetActive(on); } }
        }

        // ───────────── 부위 상태 (모듈이 읽는다) ─────────────

        public int GetAirDefenseTotal() { return AirDefense != null ? AirDefense.Length : 0; }

        public int CountAirDefenseAlive()
        {
            Init();
            int n = 0;
            for (int i = 0; i < _defenseHealth.Length; i++) { if (_defenseHealth[i] != null && _defenseHealth[i].Health > 0f) { n++; } }
            return n;
        }

        public int GetEngineTotal() { return Engines != null ? Engines.Length : 0; }

        public int CountEnginesAlive()
        {
            int n = 0;
            if (Engines == null) { return 0; }
            for (int i = 0; i < Engines.Length; i++) { if (Engines[i] != null && !Engines[i].IsDestroyed()) { n++; } }
            return n;
        }

        public float GetCoreHealthRatio()
        {
            if (TargetHealth == null || TargetHealth.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(TargetHealth.Health / TargetHealth.FullHealth);
        }

        /// 부서진 방공 부위를 count곳 되살린다(수리) — 무작위 자리부터, 모든 클라이언트에 알린다. 되살린 수를 돌려준다.
        public int RepairAirDefense(int count)
        {
            Init();
            int n = _defenseHealth.Length;
            if (n == 0) { return 0; }
            int start = Random.Range(0, n);
            int done = 0;
            for (int k = 0; k < n && done < count; k++)
            {
                int i = (start + k) % n;
                if (AirDefense[i] == null || _defenseHealth[i] == null || _defenseHealth[i].Health > 0f) { continue; }
                AirDefense[i].SendCustomNetworkEvent(NetworkEventTarget.All, "NetRespawn");
                done++;
            }
            return done;
        }
    }
}
