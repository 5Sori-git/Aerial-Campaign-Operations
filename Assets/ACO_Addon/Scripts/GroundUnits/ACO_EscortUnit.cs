using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 호위 대상(아군 유닛). 미션 생성기가 정해 준 항로(경유점 2~6개)를 서버 시간 기준으로 따라간다 —
    /// 위치 = f(항로, 출발 시각, 경과 시간)라 스폰 후 동기화가 없고 늦게 들어와도 같은 곳에 있다. 체력은 같은 오브젝트의 SaccTarget.
    ///
    /// 이동 방식(MoveMode)으로 종류를 늘린다: 공중(항로 고도 그대로, 선회에 맞춰 뱅크) / 해상(해수면, 약한 흔들림) / 지상(지면을 따라감).
    /// 도착 방식(생성기가 정해 동기화): 계속 진행 / 투하(목적지 상공에서 DropEffects를 켜고 상승하며 계속 비행) /
    /// 착륙(최종 접근 시작점부터 등감속 → 활주로에 내려 멈춤) / 정차(지상, 끝에서 감속해 멈춤). 감속도 시간 함수라 모든 클라이언트가 같다.
    /// 생성기는 이동 방식에 맞는 항로를 만든다(해상 = 항로 전체가 바다, 지상 = 지면). 지금 페이즈(ESCORT)는 공중을 쓴다.
    ///
    /// 적 폭격기(Hostile, 요격 페이즈): 같은 항로 비행을 적으로 쓴다 — 목적지(도시) 상공에 도착하면 폭탄을 투하(DropEffects)하고, 그 순간 미션 실패.
    ///   락온 마커는 적 항공기 레이어(29)에 두고 기총 막기는 끈다. 플레이어 범위 피해도 받고, 격추하면 점수가 난다(BomberKillScore).
    /// 적이 노리게 하는 방법: 락온 마커("AAMTarget")를 적이 노리는 레이어(아군 항공기 25)에 두면 빌드 시 적의 AAMTargets에 들어가고,
    /// 적 전투기는 AttackPriority(m)만큼 이 표적을 가깝게 보고 먼저 노린다. 플레이어 기총 오사는 SaccTarget.DisableBulletHitEvent로 막는다
    /// (NPC 기총/미사일은 파티클이 아니라 직접 판정이라 영향 없다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_EscortUnit : UdonSharpBehaviour
    {
        public const int MOVE_AIR = 0;
        public const int MOVE_SEA = 1;
        public const int MOVE_GROUND = 2;
        public const int ARRIVE_CONTINUE = 0;
        public const int ARRIVE_DROP = 1;
        public const int ARRIVE_LAND = 2;
        public const int ARRIVE_STOP = 3;

        [Header("참조")]
        [Tooltip("체력. ExplodeOther에 이 스크립트를 등록해야 격추 통지를 받는다")]
        public SaccTarget TargetHealth;

        [Header("표적 정보")]
        [Tooltip("켜면 적 폭격기(요격 페이즈 — 플레이어가 막아야 하는 적). 끄면 아군 호위 대상(호위·지상 호위 페이즈)")]
        public bool Hostile = false;
        public string TargetName = "Transport";
        [Tooltip("마커에 표시할 약어(3~4글자)")]
        public string MarkerLabel = "VIP";

        [Header("이동")]
        [Tooltip("0 = 공중, 1 = 해상, 2 = 지상. 생성기가 이 값에 맞는 항로를 만든다")]
        public int MoveMode = MOVE_AIR;
        [Tooltip("이동 속도(m/s)")]
        public float Speed = 120f;
        [Tooltip("공중: 선회 각속도(도/초)당 뱅크 각도")]
        public float BankPerTurnRate = 2.5f;
        [Tooltip("공중: 최대 뱅크(도)")]
        public float MaxBank = 25f;
        [Tooltip("지상: 지면 판정 레이어")]
        public LayerMask GroundLayers = 2049;
        [Tooltip("해상/지상: 흔들림 각도(도)")]
        public float BobAngle = 0.5f;

        [Header("도착")]
        [Tooltip("투하: 목적지 상공에서 재생할 파티클(보급 상자/공수부대 낙하산, 월드 공간). 비우면 투하 연출 없음")]
        public ParticleSystem[] DropEffects;
        [Tooltip("투하 연출을 이 시간(초) 뒤 기체에서 떼어 월드에 남긴다 — 기체가 거둬져도 낙하산이 끝까지 내려간다(방출이 끝난 뒤로)")]
        public float DropDetachDelay = 4f;
        [Tooltip("투하 후 계속 비행할 때 상승률(m/s) — 앞의 산에 부딪히지 않게")]
        public float DepartClimbRate = 15f;
        [Tooltip("착륙: 기체 원점이 바퀴 아래 면보다 이만큼(m) 위(활주로 위 높이)")]
        public float GearHeight = 3f;
        [Tooltip("정차(지상): 항로 끝에서 이 거리(m)에 걸쳐 감속해 멈춘다")]
        public float StopDistance = 150f;

        [Header("방어 (선택)")]
        [Tooltip("플레어 장치(같은 오브젝트). 출격할 때 횟수를 채운다")]
        public ACO_FlareDispenser Flares;
        [Tooltip("방어 기총(자식 ACO_AAGunSite — 폭격기 상부·꼬리 기총 등). 출격하면 같이 살아나고 격추되면 멈춘다")]
        public UdonSharpBehaviour[] DefenseGuns;

        [Header("적 우선순위")]
        [Tooltip("적 전투기가 이 표적을 이만큼(m) 더 가깝게 본다 — 클수록 플레이어보다 먼저 노린다")]
        public float AttackPriority = 3000f;

        [Header("격추 연출")]
        [Tooltip("격추 시 숨길 오브젝트")]
        public GameObject[] HideOnDeath;
        [Tooltip("격추 시 켤 오브젝트(화재/연기 등 연출용)")]
        public GameObject[] ShowOnDeath;
        [Tooltip("격추 시 끌 콜라이더")]
        public Collider[] DisableCollidersOnDeath;
        [Tooltip("공중: 격추 후 이 시간(초) 동안 떨어진 뒤 HideAfterCrash를 숨긴다. 0이면 그 자리에 멈춘다")]
        public float CrashFallTime = 8f;
        public GameObject[] HideAfterCrash;

        public const int MAX_ROUTE_POINTS = 6;
        private const int SAMPLES_PER_SEGMENT = 8;

        private bool _initialized;
        private bool _active;
        private bool _dead;
        private Vector3[] _route = new Vector3[MAX_ROUTE_POINTS];
        private int _routeCount;
        private int _startMs;
        // 같은 항로를 가는 행렬(콘보이)에서 이 유닛이 앞서 있는 거리(m). 선두가 가장 크다.
        private float _distanceOffset;
        // 편대에서 항로 옆으로 벌어진 거리(m, 진행 방향 오른쪽 +). 적 폭격기 삼각 대형.
        private float _lateral;
        // 도착 방식, 이 유닛이 멈출 거리(항로 끝 - 행렬에서 선두 뒤로 처진 거리), 감속 시작 거리/감속도
        private int _arrival;
        private float _endDistance;
        private float _brakeStart;
        private float _brakeDecel;
        private bool _dropped;
        private float _dropTime;
        private bool _dropDetached;
        private Vector3[] _dropLocalPos;
        private Quaternion[] _dropLocalRot;
        // 구간별 길이(Catmull-Rom 근사)와 누적 길이
        private float[] _segLen = new float[MAX_ROUTE_POINTS];
        private float[] _segStart = new float[MAX_ROUTE_POINTS];
        private float _totalLength;

        private Vector3 _crashVel;
        private float _crashTime;
        private float _groundY;
        private float _groundTimer;

        void Start()
        {
            Init();
        }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            SetDeathVisuals(false);
            // 투하 연출의 원래 자리(떼어 낸 뒤 다시 붙일 때)
            int n = DropEffects != null ? DropEffects.Length : 0;
            _dropLocalPos = new Vector3[n];
            _dropLocalRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                if (DropEffects[i] == null) { continue; }
                _dropLocalPos[i] = DropEffects[i].transform.localPosition;
                _dropLocalRot[i] = DropEffects[i].transform.localRotation;
            }
        }

        void Update()
        {
            if (!_active) { return; }
            if (_dead) { UpdateCrash(Time.deltaTime); return; }
            float s = GetDistanceTravelled();
            ApplyPose(s);
            if (_arrival == ARRIVE_DROP && !_dropped && s >= _totalLength) { PlayDrop(); }
            if (_dropped && !_dropDetached && _dropTime > 0f && Time.time - _dropTime >= DropDetachDelay) { DetachDrops(); }
        }

        /// 보급/공수부대 투하 연출(로컬). 파티클은 월드 공간이라 기체가 지나가도 그 자리에서 떨어진다.
        private void PlayDrop()
        {
            _dropped = true;
            _dropTime = Time.time;
            _dropDetached = false;
            if (DropEffects == null) { return; }
            for (int i = 0; i < DropEffects.Length; i++)
            {
                if (DropEffects[i] == null) { continue; }
                DropEffects[i].gameObject.SetActive(true);
                DropEffects[i].Play();
            }
        }

        // ───────────── 항로 ─────────────

        /// 미션 생성기가 부른다(모든 클라이언트). route[offset..offset+count) = 경유점, startMs = 출발 서버 시각,
        /// distanceOffset = 행렬 안에서 앞선 거리(m, 혼자면 0), stopBack = 선두보다 뒤에 멈출 거리(m, 선두/혼자면 0),
        /// arrival = 도착 방식(ARRIVE_*, 착륙이면 마지막 세 경유점 = 최종 접근 시작점/접지점/정지점), lateral = 항로 옆으로 벌어진 거리(m, 오른쪽 +).
        /// revive면 부활시킨다(늦게 들어온 클라이언트는 false — 생사는 SaccTarget 동기화가 맞춘다).
        public void MissionStart(Vector3[] route, int offset, int count, int startMs, bool revive, float distanceOffset, float stopBack, int arrival, float lateral)
        {
            Init();
            _routeCount = Mathf.Clamp(count, 2, MAX_ROUTE_POINTS);
            for (int k = 0; k < _routeCount; k++) { _route[k] = route[offset + k]; }
            _startMs = startMs;
            _distanceOffset = Mathf.Max(distanceOffset, 0f);
            _lateral = lateral;
            BuildLengths();
            _arrival = arrival;
            if (_arrival == ARRIVE_LAND && _routeCount < 4) { _arrival = ARRIVE_CONTINUE; }
            _endDistance = Mathf.Max(_totalLength - Mathf.Max(stopBack, 0f), 0f);
            // 감속 거리: 착륙 = 최종 접근 시작점부터 끝까지(접근하며 느려져 접지 후 멈춘다), 정차 = StopDistance
            float brake = 0f;
            if (_arrival == ARRIVE_LAND) { brake = _totalLength - _segStart[_routeCount - 3]; }
            else if (_arrival == ARRIVE_STOP) { brake = StopDistance; }
            brake = Mathf.Clamp(brake, 1f, Mathf.Max(_endDistance, 1f));
            _brakeStart = _endDistance - brake;
            _brakeDecel = Speed * Speed / (2f * brake);
            // 늦게 들어왔는데 이미 투하가 한참 지났으면 연출을 다시 틀지 않는다.
            ResetDrops();
            _dropped = GetDistanceTravelled() > _totalLength + Speed * 3f;
            _active = true;
            _dead = false;
            _groundTimer = 0f;
            _groundY = _route[0].y;
            ApplyPose(GetDistanceTravelled());

            if (Flares) { Flares.ResetSalvos(); }
            SpawnGuns(revive);
            if (revive)
            {
                if (Networking.IsOwner(gameObject) && TargetHealth) { TargetHealth.RespawnTarget(); }
                SetDeathVisuals(false);
                SetCrashHidden(false);
                return;
            }
            SetDeathVisuals(false);
            SetCrashHidden(false);
            if (TargetHealth != null && TargetHealth.Health <= 0f) { Explode(); }
        }

        /// 이번 미션에 쓰지 않는다. 생성기가 곧 오브젝트를 끈다.
        public void MissionDespawn()
        {
            Init();
            _active = false;
            SilenceGuns();
        }

        /// 방어 기총: 부모(이 유닛)에 붙은 부위로 살린다(위치는 부모가 옮긴다). revive면 체력도 채운다.
        private void SpawnGuns(bool revive)
        {
            if (DefenseGuns == null) { return; }
            for (int i = 0; i < DefenseGuns.Length; i++)
            {
                UdonSharpBehaviour g = DefenseGuns[i];
                if (g == null) { continue; }
                g.SetProgramVariable("MissionRevive", revive);
                g.SendCustomEvent("AttachedSpawn");
            }
        }

        private void SilenceGuns()
        {
            if (DefenseGuns == null) { return; }
            for (int i = 0; i < DefenseGuns.Length; i++)
            {
                if (DefenseGuns[i] != null) { DefenseGuns[i].SendCustomEvent("MissionDespawn"); }
            }
        }

        private void BuildLengths()
        {
            _totalLength = 0f;
            for (int k = 0; k < _routeCount - 1; k++)
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

        /// 구간 k(경유점 k → k+1)의 Catmull-Rom 점. 끝점은 복제해 곡선이 경유점을 지나게 한다.
        private Vector3 SplinePoint(int k, float u)
        {
            Vector3 p1 = _route[k];
            Vector3 p2 = _route[k + 1];
            Vector3 p0 = k > 0 ? _route[k - 1] : p1 + (p1 - p2);
            Vector3 p3 = k + 2 < _routeCount ? _route[k + 2] : p2 + (p2 - p1);
            float u2 = u * u;
            float u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        /// 항로 시작부터 거리 s(m) 지점.
        public Vector3 PointAtDistance(float s)
        {
            if (_routeCount < 2) { return transform.position; }
            s = Mathf.Clamp(s, 0f, _totalLength);
            int k = 0;
            while (k < _routeCount - 2 && s > _segStart[k] + _segLen[k]) { k++; }
            return SplinePoint(k, Mathf.Clamp01((s - _segStart[k]) / _segLen[k]));
        }

        /// 진행률 p(0~1) 지점(웨이브 목적지 등).
        public Vector3 PointAtProgress(float p) { return PointAtDistance(Mathf.Clamp01(p) * _totalLength); }

        /// 항로 위 거리. 착륙/정차는 감속 시작 거리부터 등감속해 _endDistance에서 멈춘다(행렬은 모두 같은 시각에 감속 — 간격 유지).
        public float GetDistanceTravelled()
        {
            float elapsed = (Networking.GetServerTimeInMilliseconds() - _startMs) * 0.001f;
            float raw = Mathf.Max(elapsed, 0f) * Speed + _distanceOffset;
            if ((_arrival != ARRIVE_LAND && _arrival != ARRIVE_STOP) || raw <= _brakeStart) { return raw; }
            float tau = (raw - _brakeStart) / Mathf.Max(Speed, 0.01f); // 감속 시작 뒤 지난 시간(초)
            float stopTime = Speed / Mathf.Max(_brakeDecel, 0.0001f);
            if (tau >= stopTime) { return _endDistance; }
            return Mathf.Min(_brakeStart + Speed * tau - 0.5f * _brakeDecel * tau * tau, _endDistance);
        }

        /// 경유점(HUD 항로 표시용): 수, 위치, 항로 시작부터 그 경유점까지 거리.
        public int GetWaypointCount() { return _active ? _routeCount : 0; }
        public Vector3 GetWaypoint(int k) { return _route[Mathf.Clamp(k, 0, MAX_ROUTE_POINTS - 1)]; }
        public float GetWaypointDistance(int k)
        {
            if (k <= 0) { return 0f; }
            if (k >= _routeCount - 1) { return _totalLength; }
            return _segStart[k];
        }
        public float GetProgress() { return _totalLength > 0f ? Mathf.Clamp01(GetDistanceTravelled() / _totalLength) : 0f; }
        public float GetRemainingDistance() { return Mathf.Max(_totalLength - GetDistanceTravelled(), 0f); }
        /// 도착: 투하/계속 진행은 항로 끝, 착륙/정차는 멈춘 순간. 시간 함수라 모든 클라이언트가 같은 순간에 도착한다.
        public bool IsArrived() { return _active && _routeCount >= 2 && GetDistanceTravelled() >= _endDistance - 0.5f; }
        public bool IsDestroyed() { return TargetHealth != null && TargetHealth.Health <= 0f; }
        public float GetHealthFraction()
        {
            if (TargetHealth == null || TargetHealth.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(TargetHealth.Health / TargetHealth.FullHealth);
        }

        private void ApplyPose(float s)
        {
            if (_routeCount < 2) { return; }
            Vector3 pos = PointAtDistance(s);
            // 진행 방향: 조금 앞 지점. 끝에 다다르면 마지막 방향을 유지한다.
            float ahead = Mathf.Max(Speed * 0.5f, 5f);
            Vector3 fwd = s + ahead <= _totalLength ? PointAtDistance(s + ahead) - pos : pos - PointAtDistance(s - ahead);
            if (s >= _totalLength)
            {
                // 도착 후(계속 진행/투하)에는 마지막 방향으로 수평으로 계속 간다 — 공중은 DepartClimbRate로 오르며(관리자가 잠시 뒤 거둬들인다).
                Vector3 lastDir = PointAtDistance(_totalLength) - PointAtDistance(_totalLength - ahead);
                lastDir.y = 0f;
                lastDir = lastDir.sqrMagnitude > 0.0001f ? lastDir.normalized : transform.forward;
                float past = s - _totalLength;
                pos = PointAtDistance(_totalLength) + lastDir * past;
                if (MoveMode == MOVE_AIR) { pos.y += DepartClimbRate * past / Mathf.Max(Speed, 0.01f); }
                fwd = lastDir * Speed + Vector3.up * (MoveMode == MOVE_AIR ? DepartClimbRate : 0f);
            }
            // 착륙: 활주로(마지막 경유점 높이) 아래로 내려가지 않게 — 접지 뒤에는 바퀴 높이로 굴러간다.
            if (_arrival == ARRIVE_LAND) { pos.y = Mathf.Max(pos.y, _route[_routeCount - 1].y + GearHeight); }
            if (fwd.sqrMagnitude < 0.0001f) { fwd = transform.forward; }
            // 편대 자리: 진행 방향 오른쪽으로 _lateral만큼(수평). 항로 곡선을 따라 같이 돈다.
            if (_lateral != 0f)
            {
                Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z);
                if (flatFwd.sqrMagnitude > 0.0001f) { pos += Vector3.Cross(Vector3.up, flatFwd.normalized) * _lateral; }
            }
            // 활주로 위에서는 기수를 수평으로(접지 후 굴러갈 때 기수가 땅을 파지 않게)
            if (_arrival == ARRIVE_LAND && pos.y <= _route[_routeCount - 1].y + GearHeight + 0.5f) { fwd.y = 0f; }

            Quaternion rot;
            if (MoveMode == MOVE_AIR)
            {
                // 뱅크: 1초 뒤 방향과의 방위 차(도/초)에 비례.
                Vector3 later = PointAtDistance(Mathf.Min(s + Speed + ahead, _totalLength)) - PointAtDistance(Mathf.Min(s + Speed, _totalLength));
                float yawRate = later.sqrMagnitude > 0.0001f ? FlatSignedAngle(fwd, later) : 0f;
                float bank = Mathf.Clamp(-yawRate * BankPerTurnRate, -MaxBank, MaxBank);
                rot = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
            }
            else
            {
                Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
                if (flat.sqrMagnitude < 0.0001f) { flat = transform.forward; }
                float t = Time.time;
                rot = Quaternion.LookRotation(flat.normalized, Vector3.up)
                    * Quaternion.Euler(Mathf.Sin(t * 0.7f) * BobAngle * 0.4f, 0f, Mathf.Sin(t * 0.9f) * BobAngle);
                if (MoveMode == MOVE_GROUND) { pos.y = SampleGroundY(pos); }
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        /// 수평면에서 a → b 방위 차(도, 오른쪽 +).
        private float FlatSignedAngle(Vector3 a, Vector3 b)
        {
            float ang = Mathf.Atan2(a.x * b.z - a.z * b.x, a.x * b.x + a.z * b.z) * Mathf.Rad2Deg;
            return -ang;
        }

        /// 지상: 0.2초마다 지면 높이를 재고 그 사이는 부드럽게 따라간다(매 프레임 Raycast를 피한다).
        private float SampleGroundY(Vector3 pos)
        {
            _groundTimer -= Time.deltaTime;
            if (_groundTimer <= 0f)
            {
                _groundTimer = 0.2f;
                RaycastHit hit;
                if (Physics.Raycast(new Vector3(pos.x, pos.y + 500f, pos.z), Vector3.down, out hit, 2000f, GroundLayers, QueryTriggerInteraction.Ignore))
                {
                    _groundY = hit.point.y;
                }
            }
            return Mathf.Lerp(transform.position.y, _groundY, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }

        /// 방출이 끝난 투하 연출을 월드로 떼어 낸다(기체가 거둬져 꺼져도 낙하산이 남는다).
        private void DetachDrops()
        {
            _dropDetached = true;
            if (DropEffects == null) { return; }
            for (int i = 0; i < DropEffects.Length; i++)
            {
                if (DropEffects[i] != null) { DropEffects[i].transform.SetParent(null, true); }
            }
        }

        /// 다시 출발할 때: 떼어 낸 투하 연출을 기체에 돌려 붙이고 비운다.
        private void ResetDrops()
        {
            _dropTime = 0f;
            _dropDetached = false;
            if (DropEffects == null) { return; }
            for (int i = 0; i < DropEffects.Length; i++)
            {
                ParticleSystem ps = DropEffects[i];
                if (ps == null) { continue; }
                ps.Stop();
                ps.Clear();
                if (ps.transform.parent != transform)
                {
                    ps.transform.SetParent(transform, false);
                    if (i < _dropLocalPos.Length) { ps.transform.localPosition = _dropLocalPos[i]; ps.transform.localRotation = _dropLocalRot[i]; }
                }
            }
        }

        // ───────────── 격추 ─────────────

        /// SaccTarget이 격추 시 ExplodeOther에 보내는 이벤트(모든 클라이언트).
        public void Explode()
        {
            if (_dead) { return; }
            _dead = true;
            _crashTime = 0f;
            _crashVel = MoveMode == MOVE_AIR ? transform.forward * Speed : Vector3.zero;
            SetDeathVisuals(true);
            SilenceGuns();
        }

        /// 공중: 관성 + 중력으로 떨어진 뒤 숨긴다(연출 전용, 클라이언트마다 로컬).
        private void UpdateCrash(float dt)
        {
            if (MoveMode != MOVE_AIR || CrashFallTime <= 0f) { return; }
            if (_crashTime >= CrashFallTime) { return; }
            _crashTime += dt;
            _crashVel += new Vector3(0f, -9.81f, 0f) * dt;
            _crashVel *= 1f - Mathf.Min(0.15f * dt, 1f);
            transform.position += _crashVel * dt;
            if (_crashVel.sqrMagnitude > 1f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(_crashVel.normalized) * Quaternion.Euler(0f, 0f, 40f), 1f - Mathf.Exp(-1.5f * dt));
            }
            if (_crashTime >= CrashFallTime) { SetCrashHidden(true); }
        }

        private void SetCrashHidden(bool hidden)
        {
            if (HideAfterCrash == null) { return; }
            for (int i = 0; i < HideAfterCrash.Length; i++)
            {
                if (HideAfterCrash[i]) { HideAfterCrash[i].SetActive(!hidden); }
            }
        }

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
    }
}
