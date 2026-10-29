using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 전차(적 지상 유닛, 지상 풀). 미션 생성기가 준 지상 항로를 서버 시간 기준으로 따라간다 —
    /// 순찰(닫힌 고리를 계속 돈다) 또는 매복(출발점 → 매복 지점까지 가서 멈춘다). 위치 = f(항로, 배치 시각, 경과 시간)라 동기화가 없다.
    /// 항로는 수평 좌표만 동기화되고, 높이는 0.2초마다 GroundRayHeight에서 아래로 쏜 Raycast로 재서 따라간다. 로컬 플레이어가 LodDistance보다 멀면 자세를 LodInterval마다만 갱신한다
    /// (가까이 있는 플레이어 — 쏘거나 맞는 쪽 — 의 화면에서는 매 프레임 정확하다).
    ///
    /// 포탑/사격은 같은 오브젝트의 ACO_AAGunSite(Turret)가 맡는다: 아군 지상 표적(레이어 26 — 호위 콘보이, 플레이어 지상 차량)을 노리는
    /// 느린 포탑 + 묵직한 단발(ACO_NPCGun 체류 판정, 피격자 판정). 체력은 같은 오브젝트의 SaccTarget(포탑과 공유).
    /// 미션 생성기 계약은 지상 유닛과 같고(MissionSpawnPosition/Rotation/Revive, MissionSpawn/MissionDespawn), 항로는 SetRoute로 받는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_Tank : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("체력(포탑과 공유). ExplodeOther에 이 스크립트와 포탑을 등록한다")]
        public SaccTarget TargetHealth;
        [Tooltip("포탑/사격(같은 오브젝트의 ACO_AAGunSite)")]
        public ACO_AAGunSite Turret;

        [Header("표적 정보")]
        [Tooltip("파괴 시 팀 점수(ACO_MissionManager)")]
        public int ScoreValue = 200;
        [Tooltip("표적 마커에 표시할 약어(3~4글자)")]
        public string MarkerLabel = "TNK";

        [Header("이동")]
        [Tooltip("이동 속도(m/s)")]
        public float Speed = 10f;
        [Tooltip("지면 판정 레이어")]
        public LayerMask GroundLayers = 2049;
        [Tooltip("지면 Raycast 시작 높이(m, 월드). 항로는 수평 좌표만 받으므로 지형 최고점보다 높은 곳에서 아래로 쏜다")]
        public float GroundRayHeight = 5000f;
        [Tooltip("로컬 플레이어가 이 거리(m)보다 멀면 자세를 LodInterval마다만 갱신한다")]
        public float LodDistance = 12000f;
        public float LodInterval = 1f;

        // 미션 생성기 계약(ACO_MissionGenerator가 SetProgramVariable로 넣고 MissionSpawn을 부른다)
        [System.NonSerialized] public Vector3 MissionSpawnPosition;
        [System.NonSerialized] public Quaternion MissionSpawnRotation = Quaternion.identity;
        [System.NonSerialized] public bool MissionRevive;

        public const int MAX_ROUTE_POINTS = 6;
        private const int SAMPLES_PER_SEGMENT = 8;

        private bool _initialized;
        private bool _active;
        private bool _dead;
        private Vector3[] _route = new Vector3[MAX_ROUTE_POINTS];
        private int _routeCount;
        private bool _loop;
        private float _distanceOffset;
        private float[] _segLen = new float[MAX_ROUTE_POINTS];
        private float[] _segStart = new float[MAX_ROUTE_POINTS];
        private int _segCount;
        private float _totalLength;
        private int _startMs;

        private float _groundY;
        private float _groundTimer;
        private float _lodTimer;
        private bool _far;
        private float _farCheckTimer;

        void Start()
        {
            Init();
        }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            if (!Turret) { Turret = GetComponent<ACO_AAGunSite>(); }
        }

        void Update()
        {
            if (!_active || _dead || _routeCount < 2) { return; }
            float dt = Time.deltaTime;

            _farCheckTimer -= dt;
            if (_farCheckTimer <= 0f)
            {
                _farCheckTimer = 1f;
                VRCPlayerApi local = Networking.LocalPlayer;
                _far = local != null && (local.GetPosition() - transform.position).sqrMagnitude > LodDistance * LodDistance;
            }
            if (_far)
            {
                _lodTimer -= dt;
                if (_lodTimer > 0f) { return; }
                _lodTimer = LodInterval;
                ApplyPose(true);
                return;
            }
            ApplyPose(false);
        }

        // ───────────── 항로 ─────────────

        /// 생성기가 MissionSpawn 직전에 부른다(모든 클라이언트). loop면 닫힌 고리를 계속 돌고, 아니면 끝점에서 멈춘다.
        /// distanceOffset = 같은 항로를 가는 소대에서 앞선 거리(m).
        public void SetRoute(Vector3[] route, int offset, int count, bool loop, float distanceOffset)
        {
            _routeCount = Mathf.Clamp(count, 2, MAX_ROUTE_POINTS);
            for (int k = 0; k < _routeCount; k++) { _route[k] = route[offset + k]; }
            _loop = loop;
            _distanceOffset = Mathf.Max(distanceOffset, 0f);
            BuildLengths();
        }

        private void BuildLengths()
        {
            _segCount = _loop ? _routeCount : _routeCount - 1;
            _totalLength = 0f;
            for (int k = 0; k < _segCount; k++)
            {
                float len = 0f;
                Vector3 prev = SplinePoint(k, 0f);
                for (int j = 1; j <= SAMPLES_PER_SEGMENT; j++)
                {
                    Vector3 p = SplinePoint(k, j / (float)SAMPLES_PER_SEGMENT);
                    len += Vector3.Distance(prev, p);
                    prev = p;
                }
                _segStart[k] = _totalLength;
                _segLen[k] = Mathf.Max(len, 0.01f);
                _totalLength += _segLen[k];
            }
        }

        /// 구간 k의 Catmull-Rom 점. 고리면 인덱스가 돌고, 아니면 끝점을 복제한다.
        private Vector3 SplinePoint(int k, float u)
        {
            Vector3 p1 = RoutePoint(k);
            Vector3 p2 = RoutePoint(k + 1);
            Vector3 p0 = (_loop || k > 0) ? RoutePoint(k - 1) : p1 + (p1 - p2);
            Vector3 p3 = (_loop || k + 2 < _routeCount) ? RoutePoint(k + 2) : p2 + (p2 - p1);
            float u2 = u * u;
            float u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        private Vector3 RoutePoint(int k)
        {
            if (_loop) { k = ((k % _routeCount) + _routeCount) % _routeCount; }
            else { k = Mathf.Clamp(k, 0, _routeCount - 1); }
            return _route[k];
        }

        private Vector3 PointAtDistance(float s)
        {
            int k = 0;
            while (k < _segCount - 1 && s > _segStart[k] + _segLen[k]) { k++; }
            return SplinePoint(k, Mathf.Clamp01((s - _segStart[k]) / _segLen[k]));
        }

        /// 지금 항로 위 거리: 고리는 길이로 나눈 나머지, 아니면 끝에서 멈춘다.
        private float CurrentDistance()
        {
            float elapsed = Mathf.Max((Networking.GetServerTimeInMilliseconds() - _startMs) * 0.001f, 0f);
            float s = elapsed * Speed + _distanceOffset;
            if (_loop) { return _totalLength > 0f ? s % _totalLength : 0f; }
            return Mathf.Min(s, _totalLength);
        }

        private void ApplyPose(bool snapGround)
        {
            float s = CurrentDistance();
            Vector3 pos = PointAtDistance(s);
            float ahead = 4f;
            Vector3 fwd;
            if (_loop) { fwd = PointAtDistance((s + ahead) % _totalLength) - pos; }
            else if (s + ahead <= _totalLength) { fwd = PointAtDistance(s + ahead) - pos; }
            else { fwd = pos - PointAtDistance(Mathf.Max(s - ahead, 0f)); }
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) { fwd = transform.forward; fwd.y = 0f; }
            if (fwd.sqrMagnitude < 0.0001f) { fwd = Vector3.forward; }

            pos.y = SampleGroundY(pos, snapGround);
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        }

        /// 0.2초마다 지면 높이를 재고 그 사이는 부드럽게 따라간다(매 프레임 Raycast를 피한다). snap이면 바로 그 높이로.
        private float SampleGroundY(Vector3 pos, bool snap)
        {
            _groundTimer -= Time.deltaTime;
            if (_groundTimer <= 0f || snap)
            {
                _groundTimer = 0.2f;
                RaycastHit hit;
                if (Physics.Raycast(new Vector3(pos.x, GroundRayHeight, pos.z), Vector3.down, out hit, GroundRayHeight * 2f, GroundLayers, QueryTriggerInteraction.Ignore))
                {
                    _groundY = hit.point.y;
                }
            }
            if (snap) { return _groundY; }
            return Mathf.Lerp(transform.position.y, _groundY, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }

        // ───────────── 격추 ─────────────

        /// SaccTarget이 격추 시 ExplodeOther에 보내는 이벤트(모든 클라이언트). 멈춘다(모델/포탑은 포탑 스크립트의 HideOnDeath가 처리).
        public void Explode()
        {
            _dead = true;
        }

        // ───────────── 미션 생성기 계약 ─────────────

        /// 미션 스폰(모든 클라이언트). 항로(SetRoute)는 생성기가 먼저 넣는다. 배치 시각 = 출발 시각.
        public void MissionSpawn()
        {
            Init();
            _active = true;
            _dead = false;
            _far = false;
            _farCheckTimer = 0f;
            _groundTimer = 0f;
            if (_routeCount < 2)
            {
                // 항로 없이 배치되면 그 자리에 선다.
                _route[0] = MissionSpawnPosition;
                _route[1] = MissionSpawnPosition + MissionSpawnRotation * Vector3.forward;
                _routeCount = 2;
                _loop = false;
                BuildLengths();
            }
            ApplyPose(true);

            if (Turret != null)
            {
                Turret.MissionRevive = MissionRevive;
                Turret.AttachedSpawn();
            }
            _dead = TargetHealth != null && TargetHealth.Health <= 0f && !MissionRevive;
        }

        /// 생성기가 MissionSpawn 직전에 부른다(배치 시각 = 출발 시각).
        public void SetStartTime(int startMs)
        {
            _startMs = startMs;
        }

        /// 이번 미션에 쓰지 않는다. 생성기가 곧 오브젝트를 끈다.
        public void MissionDespawn()
        {
            Init();
            _active = false;
            _routeCount = 0;
            if (Turret != null) { Turret.MissionDespawn(); }
        }

        public bool IsDestroyed()
        {
            return TargetHealth != null && TargetHealth.Health <= 0f;
        }

        /// 부활 진입점(미션 밖에서 쓸 때).
        public void Respawn()
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetRespawn));
        }

        public void NetRespawn()
        {
            Init();
            _dead = false;
            if (Turret != null) { Turret.NetRespawn(); }
        }
    }
}
