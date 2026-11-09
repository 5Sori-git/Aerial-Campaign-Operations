using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 육상전함(거대 지상 보스) 이동·단계 표현. 체력·부위·스폰은 같은 오브젝트의 ACO_Warship(ExternalPose)이 맡고, 이 스크립트는 자리를 옮기고 부위 노출을 화면에 반영한다.
    /// 항로·기준값은 ACO_PhaseLandShip이 동기화한다(관리자 오너가 정한다) — 여기선 읽기만 한다. 위치 자체는 동기화하지 않는다.
    ///  - 이동: 항로(경유점, Catmull-Rom — 생성기가 확인한 곡선과 같은 식)를 따라 일정한 속도로 간다. 간 거리 = 기준 거리 + 속도 × 배율 × 기준 시각부터 흐른 시간.
    ///    궤도가 부서지면 모듈이 기준을 다시 잡아 느려진다(이어 붙으므로 튀지 않는다). 끝(도시 앞)에 닿으면 멈춘다 — 모듈이 미션 실패로 판정한다.
    ///  - 자세: 선체 축을 따라 앞뒤 다섯 곳(±HalfLength)의 지면을 재서 직선을 맞춘다. 기울기 = 그 직선(최대 MaxPitch), 높이 = 가운데 높이. 천천히 따라간다.
    ///    물 위는 해수면 - SeaDraft로 잰다(수륙양용 — 물에 잠겨 간다).
    ///  - 부위: 스커트는 처음부터 노출. 스커트가 부서지면 그 뒤 궤도가, 궤도가 부서지면 그 뒤 배기구(가운데 궤도만)가 드러난다. 모두 체력(동기화)으로 모든 클라이언트가 같게 정한다.
    ///    배기구가 모두 부서지면 모듈이 코어 단계로 넘기고, 셔터가 열려 코어가 드러난다.
    ///  - 격파: 코어 SaccTarget Explode → 그 자리에 멈추고 연쇄 폭발 뒤 대폭발(ACO_FortressBlast, 피해 없음).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_LandShip : UdonSharpBehaviour
    {
        public const int STAGE_ARMOR = 0;
        public const int STAGE_CORE = 1;
        private const int MAX_TABLE = 640;

        [Header("참조")]
        public ACO_Warship Warship;
        [Tooltip("Bake가 넣는다 — 동기화된 항로·단계·기준값")]
        public ACO_PhaseLandShip Phase;
        [Tooltip("코어 체력(본체 SaccTarget)")]
        public SaccTarget TargetHealth;
        [Tooltip("스커트(함수부터, 오른쪽 6 → 왼쪽 6). Tracks와 같은 칸이 그 스커트가 덮는 궤도다")]
        public ACO_FortressPart[] Skirts;
        public ACO_FortressPart[] Tracks;
        [Tooltip("배기구 — 가운데 궤도 뒤 선체 벽")]
        public ACO_FortressPart[] Vents;
        [Tooltip("Vents와 같은 순서 — 그 배기구를 가리는 궤도 칸(Tracks 번호)")]
        public int[] VentTrack;
        [Tooltip("부서지면 사라지지 않고 고장 상태로 남았다가 GunRepairDelay초 뒤 되살아나는 포대(주포). 되살리기는 모듈(관리자 오너)이 한다")]
        public UdonSharpBehaviour[] RepairGuns;
        [Tooltip("고장 난 주포가 되살아나기까지(초)")]
        public float GunRepairDelay = 20f;
        [Tooltip("방공·부포 부위(부포, VLS, CIWS, Flak) — 모듈이 RepairInterval마다 몇 곳씩 되살린다(페이즈 전체 MaxRepairs까지)")]
        public UdonSharpBehaviour[] AirDefense;

        [Header("코어")]
        public Collider CoreHitbox;
        [Tooltip("코어 락온 마커(지상 레이어 — 공대지 무장이 잠긴다)")]
        public GameObject[] CoreMarkers;
        [Tooltip("코어 셔터(경첩이 원점). 로컬 Z축으로 젖혀 연다")]
        public Transform[] Shutters;
        [Tooltip("셔터가 다 열린 회전(닫힌 회전에 곱한다, 도)")]
        public Vector3 ShutterOpenEuler = new Vector3(0f, 0f, -95f);
        [Tooltip("셔터가 다 열리는 시간(초)")]
        public float ShutterOpenTime = 5f;

        [Header("이동")]
        [Tooltip("최고 속도(m/s) — 궤도가 모두 멀쩡할 때")]
        public float Speed = 10f;
        [Tooltip("선체 절반 길이(m) — 지면을 재는 앞뒤 거리. 생성기 항로 확인도 이 값을 쓴다")]
        public float HalfLength = 250f;
        [Tooltip("선체 절반 폭(m) — 생성기 항로 확인이 옆 지면을 재는 거리")]
        public float HalfWidth = 40f;
        [Tooltip("최대 기울기(도) — 육각 지형 비탈(15도)과 같게 두면 비탈을 그대로 타고 오른다")]
        public float MaxPitch = 15f;
        [Tooltip("지면에 묻히는 깊이(m) — 바닥이 원래 약 5m 떠 있다")]
        public float SinkDepth = 2f;
        [Tooltip("물 위에서 해수면 아래로 잠기는 깊이(m)")]
        public float SeaDraft = 14f;
        [Tooltip("Bake가 생성기 값을 넣는다 — 지형만(선체 레이어 제외)")]
        public LayerMask GroundLayers = 1;
        [Tooltip("Bake가 생성기 값을 넣는다")]
        public float SeaLevel = 0f;
        [Tooltip("지면을 다시 재는 간격(초)")]
        public float GroundInterval = 0.2f;
        [Tooltip("로컬 플레이어가 이 거리(m)보다 멀면 LodInterval마다만 잰다")]
        public float LodDistance = 12000f;
        public float LodInterval = 1f;
        [Tooltip("높이·기울기를 따라가는 빠르기(1/초)")]
        public float PoseFollow = 1.2f;

        [Header("이동 연출")]
        [Tooltip("앞뒤 끝 궤도의 흙먼지(빌더가 만든다 — 움직인 거리만큼 방출). 뭍에서만, 그 궤도가 살아 있을 때만 켠다. 물 위 항적은 ACO_WaterFx(MODE_WAKE)가 맡는다")]
        public ParticleSystem[] Dust;
        [Tooltip("Dust[i]가 붙은 궤도 번호(Tracks)")]
        public int[] DustTrack;

        [Header("격파")]
        public ACO_FortressBlast Blast;
        [Tooltip("코어가 터진 뒤 대폭발까지(초) — 그동안 선체가 연쇄로 터진다")]
        public float DeathChainTime = 4f;
        public float SecondaryInterval = 0.6f;

        private bool _initialized;
        private bool _spawned;
        private bool _dead;
        private bool _silentDead;        // 늦게 들어와 이미 격파된 것으로 본 상태(연출 없음) — 체력이 살아 있는 걸로 오면 되돌린다
        private bool _blasted;
        private float _deadAt;
        private float _secondaryTimer;
        private int _appliedStage = -1;
        private float _partTimer;
        private float _groundTimer;
        private float _y;
        private float _pitch;
        private float _targetY;
        private float[] _axisH = new float[5];
        private bool _onLand = true;     // 선체 가운데 아래가 뭍인가(SampleGround가 잰다)
        private int[] _dustState;        // 먼지마다 -1 아직, 0 끔, 1 켬
        private float _targetPitch;
        private bool _poseReady;
        private Vector3 _spawnPos;
        private Quaternion _spawnRot = Quaternion.identity;

        private SaccTarget[] _gunHealth;
        private SaccTarget[] _defenseHealth;
        private int[] _skirtState;
        private int[] _trackState;
        private int[] _ventState;
        private float _forceUntil;
        private Quaternion[] _shutterClosed;
        private float _shutterOpen;

        // 항로 거리 표(경유점이 바뀌면 다시 만든다)
        private Vector3[] _tablePos = new Vector3[MAX_TABLE];
        private float[] _tableDist = new float[MAX_TABLE];
        private int _tableCount;
        private float _routeLength;
        private int _tableStamp;
        private int _tableRouteCount = -1;
        private Vector3 _tableFirst;
        private Vector3 _tableLast;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!Warship) { Warship = GetComponent<ACO_Warship>(); }
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            int defense = AirDefense != null ? AirDefense.Length : 0;
            _defenseHealth = new SaccTarget[defense];
            for (int i = 0; i < defense; i++) { if (AirDefense[i] != null) { _defenseHealth[i] = AirDefense[i].GetComponent<SaccTarget>(); } }
            int guns = RepairGuns != null ? RepairGuns.Length : 0;
            _gunHealth = new SaccTarget[guns];
            for (int i = 0; i < guns; i++) { if (RepairGuns[i] != null) { _gunHealth[i] = RepairGuns[i].GetComponent<SaccTarget>(); } }
            _skirtState = new int[Skirts != null ? Skirts.Length : 0];
            _trackState = new int[Tracks != null ? Tracks.Length : 0];
            _ventState = new int[Vents != null ? Vents.Length : 0];
            int n = Shutters != null ? Shutters.Length : 0;
            _shutterClosed = new Quaternion[n];
            for (int i = 0; i < n; i++) { _shutterClosed[i] = Shutters[i] ? Shutters[i].localRotation : Quaternion.identity; }
            ApplyStage(STAGE_ARMOR);
        }

        // ───────────── 함선 이벤트 ─────────────

        /// ACO_Warship.MissionSpawn이 부른다(모든 클라이언트).
        public void ShipSpawned()
        {
            Init();
            _spawned = true;
            _dead = false;
            _silentDead = false;
            _blasted = false;
            _poseReady = false;
            _spawnPos = Warship != null ? Warship.MissionSpawnPosition : transform.position;
            _spawnRot = Warship != null ? Warship.MissionSpawnRotation : transform.rotation;
            _appliedStage = -1;
            _shutterOpen = 0f;
            SetShutters(0f);
            // 부활한 부위 체력은 오너가 2프레임 뒤에 되살리고 다른 클라이언트엔 동기화로 늦게 온다 — 그동안 노출 상태를 계속 다시 넣는다
            _forceUntil = Time.time + 6f;
            _partTimer = 0f;
            _tableRouteCount = -1;
            _dustState = null;
            ApplyStage(CurrentStage());
            UpdatePose(0f, true);
        }

        public void ShipDespawned()
        {
            _spawned = false;
            _dead = false;
        }

        /// 코어 파괴(SaccTarget.ExplodeOther — 모든 클라이언트): 그 자리에 멈추고 연쇄 폭발 뒤 대폭발.
        public void Explode()
        {
            if (_dead && !_silentDead) { return; }
            _silentDead = false;
            _blasted = false;
            // 확인용: 격파 순간의 단계와 부위 상태(코어 단계 전에 터지면 원인을 찾는다)
            Debug.Log("[ACO] 육상전함 격파: 단계 " + (_appliedStage == STAGE_CORE ? "코어" : "장갑") + ", 스커트 " + CountSkirtsDestroyed() + "/" + GetSkirtTotal()
                + ", 궤도 " + CountTracksDestroyed() + "/" + GetTrackTotal() + ", 배기구 " + CountVentsDestroyed() + "/" + GetVentTotal()
                + ", 마지막 무기 " + (TargetHealth != null ? TargetHealth.LastHitWeaponType.ToString() : "?"));
            _dead = true;
            _deadAt = Time.time;
            _secondaryTimer = 0f;
            SetAll(CoreMarkers, false);
            if (CoreHitbox) { CoreHitbox.enabled = false; }
            if (Phase != null && Phase.IsLive()) { Phase.SayDown(); }
            StopDust();
        }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            if (!_spawned) { return; }
            float dt = Time.deltaTime;
            // 늦게 들어왔는데 이미 격파됐다: 연출 없이 멈춘다.
            // 스폰 직후 몇 초(_forceUntil)는 보지 않는다 — 풀에서 다시 쓰는 전함은 지난 격파의 체력 0이 남아 있고,
            // 오너는 2프레임 뒤에·다른 클라이언트는 동기화로 늦게 되살린다. 그 사이에 멈추면 이번 미션 내내 멈춘 채 남는다
            // (단계·셔터·부위 노출이 안 바뀌고 진짜 격파 연출도 안 나온다).
            if (!_dead && TargetHealth != null && TargetHealth.Health <= 0f && _poseReady && Time.time >= _forceUntil)
            {
                _dead = true;
                _silentDead = true;
                _blasted = true;
                StopDust();
                Debug.Log("[ACO] 육상전함: 이미 격파된 상태로 받았습니다(연출 없이 멈춤)");
            }
            else if (_silentDead && TargetHealth != null && TargetHealth.Health > 0f)
            {
                // 체력이 살아 있는 걸로 왔다: 멈춘 걸 되돌린다
                _dead = false;
                _silentDead = false;
                _blasted = false;
                _appliedStage = -1;
                _forceUntil = Time.time + 2f;
                _partTimer = 0f;
                _dustState = null;
            }
            if (_dead)
            {
                UpdateDeath(dt);
                return;
            }
            int stage = CurrentStage();
            if (stage != _appliedStage) { ApplyStage(stage); }
            UpdateShutters(stage, dt);
            _partTimer -= dt;
            if (_partTimer <= 0f)
            {
                _partTimer = 0.5f;
                KeepCoreHitbox(stage);
                UpdateParts();
                if (Phase != null) { Phase.CheckLines(this); }
            }
            UpdatePose(dt, false);
        }

        private void UpdateDeath(float dt)
        {
            if (_blasted) { return; }
            float age = Time.time - _deadAt;
            _secondaryTimer -= dt;
            if (_secondaryTimer <= 0f && Blast)
            {
                _secondaryTimer = SecondaryInterval;
                Blast.PlaySecondary(transform.TransformPoint(new Vector3(Random.Range(-40f, 40f), Random.Range(30f, 70f), Random.Range(-220f, 220f))));
            }
            if (age < DeathChainTime) { return; }
            _blasted = true;
            if (Blast) { Blast.Play(transform.TransformPoint(new Vector3(0f, 40f, -40f)), transform.position.y < SeaLevel, 1f); }
        }

        /// 격파 뒤 대폭발까지 지났는가(모듈이 성공을 미룬다).
        public bool IsBlasted() { return _blasted; }

        private int CurrentStage()
        {
            return Phase != null && Phase.IsLive() ? Phase.GetStage() : STAGE_ARMOR;
        }

        // ───────────── 항로 ─────────────

        /// 항로 거리 표를 경유점이 바뀌었을 때만 다시 만든다(모든 클라이언트가 같은 값을 얻는다).
        private bool EnsureTable()
        {
            if (Phase == null || !Phase.IsLive()) { return false; }
            int count = Phase.GetRouteCount();
            if (count < 2) { return false; }
            Vector3 first = Phase.GetRoutePoint(0);
            Vector3 last = Phase.GetRoutePoint(count - 1);
            if (_tableRouteCount == count && _tableStamp == Phase.GetStamp() && first == _tableFirst && last == _tableLast) { return true; }
            _tableRouteCount = count;
            _tableStamp = Phase.GetStamp();
            _tableFirst = first;
            _tableLast = last;
            BuildTable(count);
            return true;
        }

        private void BuildTable(int count)
        {
            float total = 0f;
            int n = 0;
            for (int k = 0; k < count - 1 && n < MAX_TABLE; k++)
            {
                Vector3 a = Phase.GetRoutePoint(k);
                Vector3 b = Phase.GetRoutePoint(k + 1);
                float chord = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                int steps = Mathf.Clamp(Mathf.CeilToInt(chord / 20f), 1, (MAX_TABLE - 1) / Mathf.Max(count - 1, 1));
                for (int j = (k == 0 ? 0 : 1); j <= steps && n < MAX_TABLE; j++)
                {
                    Vector3 p = CurvePoint(count, k, j / (float)steps);
                    p.y = 0f;
                    if (n > 0) { total += Vector3.Distance(p, _tablePos[n - 1]); }
                    _tablePos[n] = p;
                    _tableDist[n] = total;
                    n++;
                }
            }
            _tableCount = n;
            _routeLength = total;
        }

        /// 생성기(RouteCurvePoint)와 같은 Catmull-Rom(끝점 복제).
        private Vector3 CurvePoint(int count, int k, float u)
        {
            Vector3 p1 = Phase.GetRoutePoint(k);
            Vector3 p2 = Phase.GetRoutePoint(k + 1);
            Vector3 p0 = k > 0 ? Phase.GetRoutePoint(k - 1) : p1 + (p1 - p2);
            Vector3 p3 = k + 2 < count ? Phase.GetRoutePoint(k + 2) : p2 + (p2 - p1);
            float u2 = u * u;
            float u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        /// 항로 위 거리 d의 수평 자리.
        private Vector3 PointAt(float d)
        {
            if (_tableCount == 0) { return _spawnPos; }
            if (d <= 0f) { return _tablePos[0]; }
            if (d >= _routeLength) { return _tablePos[_tableCount - 1]; }
            int lo = 0;
            int hi = _tableCount - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_tableDist[mid] <= d) { lo = mid; } else { hi = mid; }
            }
            float span = Mathf.Max(_tableDist[hi] - _tableDist[lo], 0.001f);
            return Vector3.Lerp(_tablePos[lo], _tablePos[hi], (d - _tableDist[lo]) / span);
        }

        /// 항로 길이(m). 항로를 아직 못 받았으면 0.
        public float GetRouteLength()
        {
            return EnsureTable() ? _routeLength : 0f;
        }

        /// 서버 시각 now까지 간 거리(m) — 끝에서 멈춘다.
        public float GetRouteDistance(int now)
        {
            if (!EnsureTable()) { return 0f; }
            float d = Phase.GetBaseDistance() + Speed * Phase.GetSpeedScale() * ((now - Phase.GetBaseMs()) * 0.001f);
            return Mathf.Clamp(d, 0f, _routeLength);
        }

        /// 도시(항로 끝)까지 남은 거리(m).
        public float GetRemainingDistance()
        {
            if (!EnsureTable()) { return 0f; }
            return Mathf.Max(_routeLength - GetRouteDistance(Networking.GetServerTimeInMilliseconds()), 0f);
        }

        // ───────────── 자세 ─────────────

        private void UpdatePose(float dt, bool snap)
        {
            Vector3 pos;
            Vector3 dir;
            if (EnsureTable())
            {
                float d = GetRouteDistance(Networking.GetServerTimeInMilliseconds());
                pos = PointAt(d);
                dir = PointAt(d + 15f) - PointAt(d - 15f);
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) { dir = transform.forward; dir.y = 0f; }
                dir = dir.normalized;
            }
            else
            {
                pos = _spawnPos;
                dir = _spawnRot * Vector3.forward;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            }

            _groundTimer -= dt;
            if (snap || _groundTimer <= 0f)
            {
                _groundTimer = Far() ? LodInterval : GroundInterval;
                SampleGround(pos, dir);
            }
            if (snap || !_poseReady)
            {
                _y = _targetY;
                _pitch = _targetPitch;
                _poseReady = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-dt * PoseFollow);
                _y = Mathf.Lerp(_y, _targetY, k);
                _pitch = Mathf.Lerp(_pitch, _targetPitch, k);
            }
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(-_pitch, 0f, 0f);
            transform.SetPositionAndRotation(new Vector3(pos.x, _y, pos.z), rot);
        }

        private bool Far()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || !Utilities.IsValid(local)) { return false; }
            return (local.GetPosition() - transform.position).sqrMagnitude > LodDistance * LodDistance;
        }

        /// 선체 축을 따라 다섯 곳의 지면 높이로 직선을 맞춘다 → 기울기. 높이는 그 직선을 가장 높은 접지점까지 올린다 —
        /// 가운데 축 다섯 곳과 양옆(±HalfWidth, 앞·가운데·뒤 여섯 곳) 중 직선보다 가장 높이 솟은 땅에 얹힌다.
        /// 굴곡이 있으면 파묻히지 않고 조금 뜬다(항로 확인이 콘보이처럼 가벼워서 선체 길이 안의 굴곡을 거르지 않는다).
        private void SampleGround(Vector3 pos, Vector3 dir)
        {
            float sumOH = 0f;
            float sumOO = 0f;
            float sumH = 0f;
            for (int i = 0; i < 5; i++)
            {
                float o = (i - 2) * HalfLength * 0.5f;
                float h = GroundAt(pos.x + dir.x * o, pos.z + dir.z * o);
                _axisH[i] = h;
                sumOH += o * h;
                sumOO += o * o;
                sumH += h;
            }
            float slope = sumOO > 0f ? sumOH / sumOO : 0f;
            float limit = Mathf.Tan(Mathf.Clamp(MaxPitch, 0f, 45f) * Mathf.Deg2Rad);
            slope = Mathf.Clamp(slope, -limit, limit);
            float mean = sumH / 5f;
            // 직선(가운데 높이 mean, 기울기 slope)보다 솟은 만큼 올린다. 다섯 곳 평균을 지나므로 lift는 0 이상이다
            float lift = 0f;
            for (int i = 0; i < 5; i++)
            {
                float o = (i - 2) * HalfLength * 0.5f;
                lift = Mathf.Max(lift, _axisH[i] - (mean + slope * o));
            }
            Vector3 side = new Vector3(dir.z, 0f, -dir.x) * HalfWidth;
            for (int i = -1; i <= 1; i++)
            {
                float o = i * HalfLength * 0.5f;
                float cx = pos.x + dir.x * o;
                float cz = pos.z + dir.z * o;
                float line = mean + slope * o;
                lift = Mathf.Max(lift, GroundAt(cx + side.x, cz + side.z) - line);
                lift = Mathf.Max(lift, GroundAt(cx - side.x, cz - side.z) - line);
            }
            _targetPitch = Mathf.Atan(slope) * Mathf.Rad2Deg;
            _targetY = mean + lift - SinkDepth;
            _onLand = _axisH[2] >= SeaLevel;   // 물 위면 GroundAt이 해수면 - 흘수를 준다
        }

        /// 지면 높이. 물 위(해수면보다 낮은 땅·아무것도 없음)는 해수면 - SeaDraft.
        private float GroundAt(float x, float z)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(x, 5000f, z), Vector3.down, out hit, 10000f, GroundLayers.value, QueryTriggerInteraction.Ignore))
            {
                if (hit.point.y >= SeaLevel) { return hit.point.y; }
            }
            return SeaLevel - SeaDraft + SinkDepth;
        }

        // ───────────── 부위·단계 ─────────────

        /// 스커트가 부서지면 그 궤도가, 궤도가 부서지면 그 배기구가 드러난다(체력 동기화로 모든 클라이언트가 같다).
        /// 0.5초마다 부위마다 '노출 여부 + 살아 있는지'를 보고, 마지막으로 적용한 상태와 다르면 다시 적용한다(락온 마커·히트박스·파괴 연출).
        /// 체력은 동기화가 늦게 올 수 있어(늦게 들어온 사람, 다른 클라이언트의 부활) 변화가 생길 때마다 다시 맞춰야 마커가 꺼진 채 남지 않는다.
        private void UpdateParts()
        {
            bool force = Time.time < _forceUntil;
            int ns = Skirts != null ? Skirts.Length : 0;
            for (int i = 0; i < ns; i++) { ApplyPart(Skirts[i], true, _skirtState, i, force); }
            int nt = Tracks != null ? Tracks.Length : 0;
            for (int i = 0; i < nt; i++)
            {
                bool open = i >= ns || Skirts[i] == null || Skirts[i].IsDestroyed();
                ApplyPart(Tracks[i], open, _trackState, i, force);
            }
            int nv = Vents != null ? Vents.Length : 0;
            for (int i = 0; i < nv; i++)
            {
                int t = VentTrack != null && i < VentTrack.Length ? VentTrack[i] : -1;
                bool open = t < 0 || t >= nt || Tracks[t] == null || Tracks[t].IsDestroyed();
                ApplyPart(Vents[i], open, _ventState, i, force);
            }
            UpdateDust();
        }

        /// 흙먼지: 뭍 위 + 그 궤도가 살아 있으면 켠다(방출은 움직인 거리만큼이라 멈추면 저절로 안 나온다). 바뀔 때만 쓴다.
        private void UpdateDust()
        {
            int n = Dust != null ? Dust.Length : 0;
            if (n == 0) { return; }
            if (_dustState == null || _dustState.Length != n)
            {
                _dustState = new int[n];
                for (int i = 0; i < n; i++) { _dustState[i] = -1; }
            }
            int nt = Tracks != null ? Tracks.Length : 0;
            for (int i = 0; i < n; i++)
            {
                if (Dust[i] == null) { continue; }
                int t = DustTrack != null && i < DustTrack.Length ? DustTrack[i] : -1;
                bool on = _onLand && !_dead && !(t >= 0 && t < nt && Tracks[t] != null && Tracks[t].IsDestroyed());
                int code = on ? 1 : 0;
                if (_dustState[i] == code) { continue; }
                _dustState[i] = code;
                ParticleSystem.EmissionModule em = Dust[i].emission;
                em.enabled = on;
            }
        }

        private void StopDust()
        {
            if (Dust == null) { return; }
            for (int i = 0; i < Dust.Length; i++)
            {
                if (Dust[i] == null) { continue; }
                ParticleSystem.EmissionModule em = Dust[i].emission;
                em.enabled = false;
            }
            _dustState = null;
        }

        /// 상태 코드: 0 = 아직 안 적용, 1 = 숨김·살아 있음, 2 = 노출·살아 있음, 3 = 숨김·파괴, 4 = 노출·파괴.
        private void ApplyPart(ACO_FortressPart part, bool exposed, int[] state, int i, bool force)
        {
            if (part == null || i >= state.Length) { return; }
            int code = (part.IsDestroyed() ? 3 : 1) + (exposed ? 1 : 0);
            if (!force && state[i] == code && part.Exposed == exposed) { return; }
            state[i] = code;
            part.SetExposed(exposed);
        }

        private void ApplyStage(int stage)
        {
            _appliedStage = stage;
            bool core = stage >= STAGE_CORE && !_dead;
            if (CoreHitbox) { CoreHitbox.enabled = core; }
            SetAll(CoreMarkers, core);
            // 코어 단계: TGT 마커를 코어 자리(코어 락온 마커)에
            if (Warship != null) { Warship.MarkerAnchor = core && CoreMarkers != null && CoreMarkers.Length > 0 && CoreMarkers[0] != null ? CoreMarkers[0].transform : null; }
        }

        /// 코어 히트박스는 코어 단계에만 켠다. ACO_Warship이 스폰 때 ShipSpawned 다음에 부활 처리(SetDeathVisuals — DisableCollidersOnDeath에 코어 히트박스가 있다)로
        /// 다시 켜 버려서, 장갑 단계에도 닫힌 셔터 너머 코어가 맞았다. 0.5초마다 단계대로 되돌린다.
        private void KeepCoreHitbox(int stage)
        {
            if (!CoreHitbox) { return; }
            bool core = stage >= STAGE_CORE;
            if (CoreHitbox.enabled != core) { CoreHitbox.enabled = core; }
        }

        private void UpdateShutters(int stage, float dt)
        {
            float want = stage >= STAGE_CORE ? 1f : 0f;
            if (Mathf.Approximately(_shutterOpen, want)) { return; }
            _shutterOpen = Mathf.MoveTowards(_shutterOpen, want, dt / Mathf.Max(ShutterOpenTime, 0.01f));
            SetShutters(_shutterOpen);
        }

        private void SetShutters(float open)
        {
            if (Shutters == null) { return; }
            float t = Mathf.SmoothStep(0f, 1f, open);
            Quaternion delta = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(ShutterOpenEuler), t);
            for (int i = 0; i < Shutters.Length; i++)
            {
                if (Shutters[i] && i < _shutterClosed.Length) { Shutters[i].localRotation = _shutterClosed[i] * delta; }
            }
        }

        private void SetAll(GameObject[] list, bool on)
        {
            if (list == null) { return; }
            for (int i = 0; i < list.Length; i++) { if (list[i]) { list[i].SetActive(on); } }
        }

        // ───────────── 부위 상태 (모듈이 읽는다) ─────────────

        public int GetTrackTotal() { return Tracks != null ? Tracks.Length : 0; }
        public int GetVentTotal() { return Vents != null ? Vents.Length : 0; }
        public int GetSkirtTotal() { return Skirts != null ? Skirts.Length : 0; }

        public int CountTracksDestroyed() { return CountDestroyed(Tracks); }
        public int CountVentsDestroyed() { return CountDestroyed(Vents); }
        public int CountSkirtsDestroyed() { return CountDestroyed(Skirts); }

        private int CountDestroyed(ACO_FortressPart[] list)
        {
            if (list == null) { return 0; }
            int n = 0;
            for (int i = 0; i < list.Length; i++) { if (list[i] != null && list[i].IsDestroyed()) { n++; } }
            return n;
        }

        public int GetRepairGunCount() { Init(); return _gunHealth.Length; }

        /// 고장 난(체력 0) 주포인가.
        public bool IsGunDown(int i)
        {
            Init();
            return i >= 0 && i < _gunHealth.Length && _gunHealth[i] != null && _gunHealth[i].Health <= 0f;
        }

        /// 고장 난 주포를 되살린다(모든 클라이언트에 알린다 — 포대 오너가 체력을 되돌린다). 모듈이 부른다.
        public void RepairGun(int i)
        {
            Init();
            if (i < 0 || i >= _gunHealth.Length || RepairGuns[i] == null) { return; }
            RepairGuns[i].SendCustomNetworkEvent(NetworkEventTarget.All, "NetRespawn");
        }

        /// 부서진 방공 부위를 count곳 되살린다 — 무작위 자리부터, 모든 클라이언트에 알린다. 되살린 수를 돌려준다(요새와 같다).
        public int RepairAirDefense(int count)
        {
            Init();
            int n = _defenseHealth.Length;
            if (n == 0 || count <= 0) { return 0; }
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

        public bool IsCoreDestroyed() { return TargetHealth != null && TargetHealth.Health <= 0f; }

        public float GetCoreHealthRatio()
        {
            if (TargetHealth == null || TargetHealth.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(TargetHealth.Health / TargetHealth.FullHealth);
        }
    }
}
