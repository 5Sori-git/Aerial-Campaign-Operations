using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace ACO
{
    /// CIC 도구(6-275) — 항모 CIC 전술 테이블에서 지도를 찍고 패널 버튼으로 쓴다. 누구나 쓸 수 있고 미션 중에만 작동한다.
    ///  - 찍기(로컬): 테이블 근처(TouchRange)에서 VR은 손, 데스크톱은 시선으로 지도 바닥(MapRoot 높이 평면)을 가리키면 선이 보이고,
    ///    트리거(데스크톱 클릭)로 찍으면 그 자리에 빛기둥 커서가 선다. 공역 밖은 안쪽으로 당긴다.
    ///  - WAYPOINT: 커서 자리를 모든 조종사 HUD에 WaypointTime초 띄운다(쿨타임 WaypointCooldown, 한 번에 하나 — 새로 누르면 옮겨진다).
    ///  - WINGMEN: 커서 자리를 구역 중심으로 CIC 지역 편대를 부른다(ACO_SupportManager.CallWingmenArea — 60초, 쿨타임 20초, 누른 사람이 계산 · 점수).
    ///  - DRONE: 정찰 드론(하나, 공유)을 커서 자리로 보낸다(이동 쿨타임 DroneMoveCooldown). 격추되지 않는다. 그 자리 지형 + DroneAltitude에서
    ///    DroneOrbitRadius로 돈다. 정밀 타격 차량 식별(ACO_PhasePrecision)과 CIC 고글(ACO_CicGoggles) 관전에 쓴다.
    ///  - CRUISE MSL(6-276): 아군 순양함 VLS에서 커서 둘레로 순항미사일 4발 일제 발사(ACO_CicStrike — 쿨타임 40초, 모두가 함께).
    ///  - 고글 관전 대상(UAV · 기체)은 고글 패널(ACO_CicGogglesPanel — 따로 놓인다)이 맡는다.
    /// 동기화(Manual, 누른 사람이 오너가 되어 보낸다): 웨이포인트(자리 · 시각 · 작전)는 여기, 드론(출발 · 목표 · 시각 · 작전)은
    /// 따로 둔 ACO_CicDroneSync(6-278 — 같은 칸이면 동시에 누를 때 한쪽이 지워졌다).
    /// 작전이 끝나거나 바뀌면(작전 시작 시각이 다르면) 모두 없는 것으로 본다 — 따로 지우는 사람이 없어도 모든 화면이 같은 답을 낸다.
    /// 드론 자리는 동기화된 값과 서버 시각으로 모든 화면이 같게 계산한다(위치를 따로 보내지 않는다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_CicConsole : UdonSharpBehaviour
    {
        [Header("연결 (Bake)")]
        public ACO_MissionManager Mission;
        public ACO_SupportManager Support;
        [Tooltip("CIC 전술 테이블(지도 바닥 = MapRoot 높이)")]
        public ACO_TacticalMap Map;
        public ACO_PlayerProgress Progress;
        public ACO_GameFlow Flow;
        [Tooltip("항모(드론 · 편대가 나오는 쪽)")]
        public Transform HomeBase;
        [Tooltip("순항미사일 일제 발사(6-276) — 씬에 하나")]
        public ACO_CicStrike Strike;
        [Tooltip("정찰 드론(씬에 하나 — 격추 불가, 이 스크립트가 매 프레임 옮긴다)")]
        public Transform Drone;
        [Tooltip("드론 경로 동기화 칸(프리팹 빌더 — 콘솔의 자식, 6-278)")]
        public ACO_CicDroneSync DroneSync;
        public ACO_UiSound Sound;
        [Tooltip("번역 장치(6-166). Bake가 넣는다")]
        public ACO_Localization Loc;
        [Tooltip("드론 고도 기준 지면(미션 생성기 GroundLayers)과 해수면 — Bake")]
        public LayerMask GroundLayers = 2049;
        public float SeaLevel = 0f;

        [Header("테이블 표시 (프리팹 빌더 — 시작할 때 MapRoot 아래로 옮긴다)")]
        public Transform Cursor;
        public LineRenderer AimLine;
        public Transform WaypointMark;
        public Transform AreaMark;
        public Transform DroneMark;
        [Tooltip("테이블의 UAV 이름표(드론 표시 위 — 내 머리 쪽을 본다)")]
        public Transform DroneLabel;
        [Tooltip("테이블의 순항미사일 표시(미사일 풀과 같은 수 — 나는 동안만)")]
        public Transform[] CruiseMarks;
        [Tooltip("커서를 찍은 지면에 BeamTime초 서는 큰 빛기둥(로컬 — 고글 UAV 시점에서 보인다)")]
        public Transform WorldBeam;
        public float BeamTime = 3f;

        [Header("패널")]
        public TextMeshProUGUI StatusText;
        public TextMeshProUGUI WaypointText;
        public TextMeshProUGUI WingmenText;
        public TextMeshProUGUI DroneText;
        public TextMeshProUGUI CruiseText;
        public Image WaypointButton;
        public Image WingmenButton;
        public Image DroneButton;
        public Image CruiseButton;
        [Tooltip("키 옆 LED(도구마다 — 준비면 키 색으로 켜고, 아니면 어둡게)")]
        public Image WaypointLed;
        public Image WingmenLed;
        public Image DroneLed;
        public Image CruiseLed;
        [Tooltip("도구 패널 판(프리팹 빌더). 시선 · 손 레이저가 이 판에 먼저 닿으면 지도를 찍지 않는다 — 패널 버튼 누름이 커서를 옮기지 않게(6-278)")]
        public RectTransform ToolPanel;
        [Tooltip("ToolPanel 판정 여유(m, 판 둘레 — 틀 · 뒤판까지)")]
        public float ToolPanelMargin = 0.012f;
        [Tooltip("LCD 머리 오른쪽: 항모에서 커서까지 거리")]
        public TextMeshProUGUI CursorText;
        public Color LedOffColor = new Color(0.22f, 0.2f, 0.16f, 1f);
        [Tooltip("준비가 아닌 키는 원래 색에 이 배율을 곱한다(어둡게)")]
        public float KeyDim = 0.6f;
        public Color LcdReadyColor = new Color(0.49f, 1f, 0.54f, 1f);
        public Color LcdWaitColor = new Color(0.88f, 0.69f, 0.31f, 1f);
        public Color LcdOffColor = new Color(0.3f, 0.45f, 0.32f, 1f);
        [Header("확정 팝업 (순항미사일 · 아군 편대 — 6-276)")]
        public GameObject ConfirmPopup;
        public TextMeshProUGUI ConfirmTitle;
        public TextMeshProUGUI ConfirmBody;
        [Tooltip("팝업을 이 시간(초) 동안 그대로 두면 저절로 닫는다(취소)")]
        public float ConfirmTimeout = 10f;

        [Header("무전 (6-282 — 잡담 급)")]
        [Tooltip("무전 채터(Bake가 넣는다)")]
        public ACO_Chatter Chatter;
        [Tooltip("웨이포인트가 새로 찍히면 모두에게(글로벌 — 각 화면이 동기화된 값이 바뀐 걸 보고 낸다)")]
        [TextArea] public string WaypointLineText = "Datalink. Coordinates received.";
        public AudioClip WaypointLineClip;
        [Tooltip("UAV를 보낸 사람에게만(로컬)")]
        [TextArea] public string UavLineText = "UAV en route to your mark.";
        public AudioClip UavLineClip;
        [Tooltip("이보다 오래 밀린 무전은 버린다(초)")]
        public float LineMaxDelay = 4f;
        [Tooltip("일어난 지 이 시간(ms) 안의 일만 말한다 — 늦게 들어온 사람은 지난 일을 듣지 않는다")]
        public int LineFreshMs = 5000;

        [Header("값")]
        [Tooltip("테이블 가운데에서 이 거리(m) 안에서만 찍힌다")]
        public float TouchRange = 4f;
        public float WaypointTime = 30f;
        public float WaypointCooldown = 10f;
        public float DroneMoveCooldown = 10f;
        public float DroneAltitude = 600f;
        public float DroneOrbitRadius = 300f;
        [Tooltip("한 바퀴 시간(초)")]
        public float DroneOrbitPeriod = 60f;
        [Tooltip("이동 속도(m/s) — 2026-11-18 300 → 450(맵 끝에서 끝까지 약 2분 43초 → 약 1분 49초, 사용자 \"너무 길다\" → 1.5배)")]
        public float DroneSpeed = 450f;
        [Tooltip("도착한 뒤 진행 방향이 선회 방향으로 바뀌는 시간(초)")]
        public float DroneTurnBlend = 3f;
        [Header("UAV 지형 넘기(6-292)")]
        [Tooltip("경로 · 선회 원 아래 지면 위로 이만큼(m) 띄운다")]
        public float DroneClearance = 300f;
        [Tooltip("기본 상승 각도(도) — 출발점 앞 지형이 더 가파르면 그만큼 더 가파르게")]
        public float DroneClimbAngle = 30f;
        [Tooltip("선회 원에 도착한 뒤 순항 고도에서 선회 고도로 내려오는 시간(초)")]
        public float DroneDescentTime = 15f;
        [Tooltip("경로 아래 지면을 재는 간격(m)")]
        public float DroneSampleStep = 300f;
        [Tooltip("경로 계산을 다시 하는 간격(초) — 늦게 들어와 지형이 아직 만들어지는 중이었어도 맞춰진다")]
        public float DroneRouteRefresh = 5f;
        [Tooltip("CIC 편대 선회 고도(지형 위 m)와 나타나는 거리(구역 중심에서 항모 쪽으로 m)")]
        public float WingmenAltitude = 1000f;
        public float WingmenStartDistance = 3000f;

        [UdonSynced] private int _wpMission;
        [UdonSynced] private int _wpMs;
        [UdonSynced] private Vector3 _wpPos;

        private int _wpSaidMs;          // 무전을 이미 낸(또는 지난 일로 넘긴) 웨이포인트 시각
        private bool _hasCursor;
        private Vector3 _cursorLocal;   // MapRoot 기준(y = 0)
        private Vector3 _cursorWorld;   // 월드(높이 = 지면)
        private int _cursorMission;
        private bool _hover;
        private Vector3 _hoverLocal;
        private int _hand;              // 마지막으로 찍은 손(0 오른손, 1 왼손)
        private float _textTimer;
        private float _beamUntil = -1f;
        private bool _droneOn;
        private const int PENDING_NONE = 0;
        private const int PENDING_WINGMEN = 1;
        private const int PENDING_CRUISE = 2;
        private int _pending;
        private float _pendingAt;
        private Color[] _keyColors;
        private Color[] _ledColors;
        private Vector3 _dronePos;
        private Quaternion _droneRot = Quaternion.identity;

        void Start()
        {
            if (Map != null && Map.MapRoot != null)
            {
                Transform root = Map.MapRoot;
                if (Cursor) { Cursor.SetParent(root, false); Cursor.gameObject.SetActive(false); }
                if (WaypointMark) { WaypointMark.SetParent(root, false); WaypointMark.gameObject.SetActive(false); }
                if (AreaMark) { AreaMark.SetParent(root, false); AreaMark.gameObject.SetActive(false); }
                if (DroneMark) { DroneMark.SetParent(root, false); DroneMark.gameObject.SetActive(false); }
                if (CruiseMarks != null)
                {
                    for (int i = 0; i < CruiseMarks.Length; i++) { if (CruiseMarks[i]) { CruiseMarks[i].SetParent(root, false); CruiseMarks[i].gameObject.SetActive(false); } }
                }
            }
            if (AimLine) { AimLine.enabled = false; }
            if (Drone) { Drone.gameObject.SetActive(false); }
            if (WorldBeam) { WorldBeam.gameObject.SetActive(false); }
            if (ConfirmPopup) { ConfirmPopup.SetActive(false); }
            // 키 · LED 원래 색(프리팹 빌더) — 준비가 아니면 어둡게 칠할 때 쓴다
            Image[] keys = { WaypointButton, WingmenButton, DroneButton, CruiseButton };
            Image[] leds = { WaypointLed, WingmenLed, DroneLed, CruiseLed };
            _keyColors = new Color[4];
            _ledColors = new Color[4];
            for (int i = 0; i < 4; i++)
            {
                _keyColors[i] = keys[i] ? keys[i].color : Color.gray;
                _ledColors[i] = leds[i] ? leds[i].color : Color.green;
            }
            UpdateTexts();
        }

        // ───────────── 상태 (모든 화면) ─────────────

        private int Now() { return Networking.GetServerTimeInMilliseconds(); }

        private int CurrentMission()
        {
            if (Mission == null || Mission.GetState() != ACO_MissionManager.STATE_RUNNING) { return 0; }
            int m = Mission.GetMissionStartMs();
            return m == 0 ? 1 : m;
        }

        public bool IsRunning() { return CurrentMission() != 0; }

        public bool IsWaypointActive()
        {
            int m = CurrentMission();
            return m != 0 && _wpMission == m && Now() - _wpMs < Mathf.RoundToInt(WaypointTime * 1000f);
        }

        public Vector3 GetWaypoint() { return _wpPos; }

        public float GetWaypointCooldown()
        {
            int m = CurrentMission();
            if (m == 0 || _wpMission != m) { return 0f; }
            return Mathf.Max(WaypointCooldown - (Now() - _wpMs) * 0.001f, 0f);
        }

        public bool IsDroneActive() { return _droneOn; }
        public Vector3 GetDronePosition() { return _dronePos; }
        public Quaternion GetDroneRotation() { return _droneRot; }

        public float GetDroneCooldown()
        {
            int m = CurrentMission();
            if (m == 0 || DroneSync == null || DroneSync.GetMission() != m) { return 0f; }
            return Mathf.Max(DroneMoveCooldown - (Now() - DroneSync.GetMs()) * 0.001f, 0f);
        }

        // ───────────── 드론 궤도 (모든 화면 — 동기화 값 + 서버 시각) ─────────────

        private bool DroneLive()
        {
            int m = CurrentMission();
            return m != 0 && DroneSync != null && DroneSync.GetMission() == m && DroneSync.GetMs() != 0;
        }

        // UAV 경로 고도(6-292): 경로가 바뀌면(또는 DroneRouteRefresh초마다) 한 번 잰다 — 출발점 · 목표 · 지형이 같아 모든 화면이 같다
        private int _routeMs = int.MinValue;
        private Vector3 _routeFrom;
        private Vector3 _routeTo;
        private float _routeAt = -1000f;
        private float _routeCruise;
        private float _routeOrbit;
        private float _routeClimb;
        private float _routeEnd;   // 경로 끝(선회 원에 들어가는 점) 고도 — 선회하며 여기서 선회 고도로 내려온다

        /// 경로 아래 · 선회 원 둘레 지면을 재서 순항 고도, 선회 고도, 상승 기울기(높이 / 수평 거리)를 정한다.
        private void PlanDroneRoute(int ms, Vector3 from, Vector3 c, Vector3 entry, float r)
        {
            _routeMs = ms;
            _routeFrom = from;
            _routeTo = c;
            _routeAt = Time.time;
            float clear = Mathf.Max(DroneClearance, 0f);
            // 선회 원: 둘레 12곳 + 가운데
            float orbit = c.y;
            for (int i = 0; i < 12; i++)
            {
                float a = i * (Mathf.PI / 6f);
                float g = GroundHeight(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)) + clear;
                if (g > orbit) { orbit = g; }
            }
            // 경로: 출발점 → 들어가는 점, DroneSampleStep마다
            float dx = entry.x - from.x;
            float dz = entry.z - from.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            int n = Mathf.Clamp(Mathf.CeilToInt(len / Mathf.Max(DroneSampleStep, 50f)), 1, 400);
            float[] req = new float[n + 1];
            float cruise = orbit;
            for (int i = 1; i <= n; i++)
            {
                float f = (float)i / n;
                req[i] = GroundHeight(new Vector3(from.x + dx * f, 0f, from.z + dz * f)) + clear;
                if (req[i] > cruise) { cruise = req[i]; }
            }
            // 상승 기울기: 기본 각도, 출발점 앞 지형이 그 선보다 높으면 그 지형을 넘을 만큼
            float k = Mathf.Tan(Mathf.Clamp(DroneClimbAngle, 1f, 85f) * Mathf.Deg2Rad);
            for (int i = 1; i <= n; i++)
            {
                float need = Mathf.Min(req[i], cruise) - from.y;
                float s = len * i / n;
                if (need > 0f && s > 0.01f && need / s > k) { k = need / s; }
            }
            // 짧은 경로도 끝에서 순항 고도에 닿게(오르는 쪽) — 선회 원 위에서 다시 오르면 둘레 지형에 걸릴 수 있다
            if (from.y < cruise && len > 0.01f && (cruise - from.y) / len > k) { k = (cruise - from.y) / len; }
            _routeCruise = cruise;
            _routeOrbit = orbit;
            _routeClimb = k;
            _routeEnd = RouteAltitude(len);
        }

        /// 경로 위 수평 거리 s(m)에서의 고도: 순항 고도까지 오르거나(출발이 더 높으면 내려와) 그 뒤 순항.
        private float RouteAltitude(float s)
        {
            float y0 = _routeFrom.y;
            if (y0 <= _routeCruise) { return Mathf.Min(_routeCruise, y0 + s * _routeClimb); }
            return Mathf.Max(_routeCruise, y0 - s * _routeClimb);
        }

        /// 출발점 → 선회 원의 들어가는 점까지 곧게, 그 뒤 원을 돈다. 자리 · 방향을 _dronePos · _droneRot에.
        /// 고도(6-292): 경로 아래 지면 + DroneClearance를 넘는 순항 고도로 오르고, 원에 도착한 뒤 DroneDescentTime초에 선회 고도로 내려온다.
        private void ComputeDrone(int now)
        {
            Vector3 c = DroneSync.GetTo();
            Vector3 from = DroneSync.GetFrom();
            int ms = DroneSync.GetMs();
            Vector3 away = new Vector3(from.x - c.x, 0f, from.z - c.z);
            if (away.sqrMagnitude < 1f) { away = Vector3.forward; }
            away = away.normalized;
            float r = Mathf.Max(DroneOrbitRadius, 1f);
            Vector3 entry = c + away * r;
            float travel = Vector3.Distance(from, entry) / Mathf.Max(DroneSpeed, 1f);
            float t = Mathf.Max((now - ms) * 0.001f, 0f);
            if (ms != _routeMs || from != _routeFrom || c != _routeTo || Time.time - _routeAt > Mathf.Max(DroneRouteRefresh, 0.5f)) { PlanDroneRoute(ms, from, c, entry, r); }
            Vector3 travelDir = entry - from;
            travelDir.y = 0f;
            if (travelDir.sqrMagnitude < 0.01f) { travelDir = -away; }
            travelDir = travelDir.normalized;
            if (t < travel)
            {
                float f = travel > 0.001f ? t / travel : 1f;
                _dronePos = Vector3.Lerp(from, entry, f);
                float hx = entry.x - from.x;
                float hz = entry.z - from.z;
                _dronePos.y = RouteAltitude(Mathf.Sqrt(hx * hx + hz * hz) * f);
                _droneRot = Quaternion.LookRotation(travelDir, Vector3.up);
                return;
            }
            // 원: 들어간 점의 각도에서 시작해 반시계(위에서 볼 때 +x → +z)로 돈다
            float a0 = Mathf.Atan2(away.z, away.x);
            float w = 2f * Mathf.PI / Mathf.Max(DroneOrbitPeriod, 1f);
            float a = a0 + w * (t - travel);
            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            _dronePos = c + radial * r;
            // 순항 고도에서 선회 고도로 부드럽게 내려온다(선회 고도는 원 둘레 지면 + 여유 이상)
            float down = Mathf.SmoothStep(0f, 1f, (t - travel) / Mathf.Max(DroneDescentTime, 0.01f));
            _dronePos.y = Mathf.Lerp(Mathf.Max(_routeEnd, _routeOrbit), _routeOrbit, down);
            Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            float blend = Mathf.SmoothStep(0f, 1f, (t - travel) / Mathf.Max(DroneTurnBlend, 0.01f));
            Vector3 fwd = Vector3.Slerp(travelDir, tangent, blend);
            if (fwd.sqrMagnitude < 0.0001f) { fwd = tangent; }
            fwd = fwd.normalized;
            // 수평 선회(기울지 않는다 — 고글 VR 시점의 수평선이 기울어 멀미 나지 않게, 2026-11-18)
            _droneRot = Quaternion.LookRotation(fwd, Vector3.up);
        }

        void Update()
        {
            int now = Now();
            bool live = DroneLive();
            _droneOn = live;
            if (live) { ComputeDrone(now); }
            if (Drone)
            {
                if (Drone.gameObject.activeSelf != live) { Drone.gameObject.SetActive(live); }
                if (live) { Drone.SetPositionAndRotation(_dronePos, _droneRot); }
            }

            if (_beamUntil >= 0f && Time.time >= _beamUntil)
            {
                _beamUntil = -1f;
                if (WorldBeam) { WorldBeam.gameObject.SetActive(false); }
            }

            UpdateWaypointLine(now);

            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me) || Map == null || Map.MapRoot == null) { return; }
            bool near = Vector3.Distance(me.GetPosition(), Map.MapRoot.position) <= TouchRange;
            UpdateAim(me, near);
            if (_hasCursor && _cursorMission != CurrentMission()) { ClearCursor(); }   // 작전이 끝나면 커서도 없다
            // 확정 팝업: 시간이 지나거나 작전이 끝나거나 테이블에서 멀어지면 닫는다(취소)
            if (_pending != PENDING_NONE && (!IsRunning() || !near || Time.time - _pendingAt > ConfirmTimeout)) { CloseConfirm(); }
            if (!near) { return; }
            UpdateMarks();
            _textTimer -= Time.deltaTime;
            if (_textTimer <= 0f) { _textTimer = 0.25f; UpdateTexts(); }
        }

        /// 웨이포인트 무전(글로벌, 6-282): 동기화된 웨이포인트가 새로 바뀌면 이 화면이 한 번 말한다. 지난 일(늦게 들어옴)은 넘긴다.
        private void UpdateWaypointLine(int now)
        {
            if (_wpMs == 0 || _wpMs == _wpSaidMs) { return; }
            _wpSaidMs = _wpMs;
            int m = CurrentMission();
            if (m == 0 || _wpMission != m) { return; }
            int age = now - _wpMs;
            if (age < -2000 || age > LineFreshMs) { return; }
            if (Chatter) { Chatter.SayControlLow(WaypointLineText, WaypointLineClip, LineMaxDelay); }
        }

        // ───────────── 찍기 (로컬) ─────────────

        private void UpdateAim(VRCPlayerApi me, bool near)
        {
            _hover = false;
            if (near && IsRunning())
            {
                Vector3 origin;
                Vector3 dir;
                if (me.IsUserInVR())
                {
                    VRCPlayerApi.TrackingData hand = me.GetTrackingData(_hand == 1 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand);
                    origin = hand.position;
                    // VRChat 손 추적 회전의 앞(+Z)은 엄지(위) 쪽이다 — SaccFlight 손 조준(DFUNC_Laser · EXT_HandTurret)과 같이 Y축 60°를 돌려 검지 쪽으로(6-290, 사용자 "포인터가 검지가 아니라 엄지 방향")
                    dir = (hand.rotation * Quaternion.Euler(0f, 60f, 0f)) * Vector3.forward;
                }
                else
                {
                    VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    origin = head.position;
                    dir = head.rotation * Vector3.forward;
                }
                Transform root = Map.MapRoot;
                Vector3 lo = root.InverseTransformPoint(origin);
                Vector3 ld = root.InverseTransformDirection(dir);
                if (ld.y < -0.0001f && lo.y > 0f)
                {
                    float k = -lo.y / ld.y;
                    Vector3 hit = lo + ld * k;
                    hit.y = 0f;
                    float rr = Map.MapRadius;
                    if (hit.x * hit.x + hit.z * hit.z <= rr * rr * 1.21f && !PanelBlocks(origin, dir, root.TransformPoint(hit)))
                    {
                        _hover = true;
                        _hoverLocal = hit;
                        if (AimLine && me.IsUserInVR())
                        {
                            AimLine.enabled = true;
                            AimLine.SetPosition(0, origin);
                            AimLine.SetPosition(1, root.TransformPoint(hit));
                        }
                    }
                }
            }
            if (!_hover && AimLine && AimLine.enabled) { AimLine.enabled = false; }
        }

        /// origin → mapHit(월드) 사이에서 선이 도구 패널 판(팝업 포함 — 같은 판 위)에 먼저 닿으면 true.
        /// 패널 키 · 확정 버튼을 누르는 트리거가 그 뒤 지도에 커서를 찍지 않게 한다(6-278).
        private bool PanelBlocks(Vector3 origin, Vector3 dir, Vector3 mapHit)
        {
            if (ToolPanel == null || !ToolPanel.gameObject.activeInHierarchy) { return false; }
            Vector3 n = ToolPanel.forward;
            float dn = Vector3.Dot(dir, n);
            if (Mathf.Abs(dn) < 0.0001f) { return false; }
            float k = Vector3.Dot(ToolPanel.position - origin, n) / dn;
            if (k <= 0f || k * k >= (mapHit - origin).sqrMagnitude / Mathf.Max(dir.sqrMagnitude, 0.0001f)) { return false; }
            Vector3 p = origin + dir * k;
            Vector3 local = ToolPanel.InverseTransformPoint(p);
            Rect r = ToolPanel.rect;
            Vector3 s = ToolPanel.lossyScale;
            float mx = s.x > 0.000001f ? ToolPanelMargin / s.x : 0f;
            float my = s.y > 0.000001f ? ToolPanelMargin / s.y : 0f;
            return local.x >= r.xMin - mx && local.x <= r.xMax + mx && local.y >= r.yMin - my && local.y <= r.yMax + my;
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (!value) { return; }
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me) || Map == null || Map.MapRoot == null || !IsRunning()) { return; }
            if (Vector3.Distance(me.GetPosition(), Map.MapRoot.position) > TouchRange) { return; }
            int hand = args.handType == HandType.LEFT ? 1 : 0;
            // 물건을 든 손의 트리거(데스크톱은 클릭)는 그 물건 것이다(고글 등)
            if (me.GetPickupInHand(hand == 1 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right) != null) { return; }
            if (!me.IsUserInVR() && me.GetPickupInHand(VRC_Pickup.PickupHand.Right) != null) { return; }
            if (me.IsUserInVR() && hand != _hand) { _hand = hand; UpdateAim(me, true); }   // 다른 손으로 눌렀으면 그 손 기준으로 다시 잰다
            if (!_hover) { return; }
            SetCursor(_hoverLocal);
            if (Sound) { Sound.PlayAt(ACO_UiSound.KIND_CLICK, Map.MapRoot.TransformPoint(_hoverLocal)); }
        }

        private void SetCursor(Vector3 local)
        {
            float rr = Map.MapRadius * 0.98f;
            float d = Mathf.Sqrt(local.x * local.x + local.z * local.z);
            if (d > rr) { local = local * (rr / d); }   // 공역 밖은 안쪽으로 당긴다
            local.y = 0f;
            _cursorLocal = local;
            _cursorWorld = Map.MapToWorld(local);
            _cursorWorld.y = GroundHeight(_cursorWorld);
            _cursorMission = CurrentMission();
            _hasCursor = true;
            if (Cursor)
            {
                Cursor.localPosition = local;
                Cursor.gameObject.SetActive(true);
            }
            if (WorldBeam)
            {
                WorldBeam.position = _cursorWorld;
                WorldBeam.rotation = Quaternion.identity;
                WorldBeam.gameObject.SetActive(true);
                _beamUntil = Time.time + BeamTime;
            }
            UpdateTexts();
        }

        /// CIC 고글(UAV 시점)의 조준선: origin에서 dir로 지면(바다면 해수면)에 닿는 곳에 커서를 찍는다(로컬).
        public void DesignateFromView(Vector3 origin, Vector3 dir)
        {
            if (!IsRunning() || Map == null || dir.sqrMagnitude < 0.0001f) { return; }
            dir = dir.normalized;
            Vector3 p;
            RaycastHit hit;
            bool ground = Physics.Raycast(origin, dir, out hit, 40000f, GroundLayers.value, QueryTriggerInteraction.Ignore);
            if (ground && hit.point.y >= SeaLevel) { p = hit.point; }
            else
            {
                if (dir.y > -0.0001f) { return; }   // 하늘 쪽 — 닿는 곳이 없다
                float t = (SeaLevel - origin.y) / dir.y;
                if (t < 0f) { return; }
                p = origin + dir * t;
            }
            Vector3 local = Map.WorldToMap(p);
            local.y = 0f;
            SetCursor(local);
            if (Sound) { Sound.PlayAt(ACO_UiSound.KIND_CLICK, transform.position); }
        }

        private void ClearCursor()
        {
            _hasCursor = false;
            if (Cursor) { Cursor.gameObject.SetActive(false); }
        }

        /// (x, z)의 지면 높이(바다면 해수면).
        private float GroundHeight(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, 6000f, p.z), Vector3.down, out hit, 12000f, GroundLayers.value, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Max(hit.point.y, SeaLevel);
            }
            return SeaLevel;
        }

        // ───────────── 버튼 ─────────────

        public void ClickWaypoint()
        {
            if (!CanUse() || GetWaypointCooldown() > 0f) { Deny(); return; }
            TakeOwner();
            _wpPos = _cursorWorld;
            _wpMs = Now();
            _wpMission = CurrentMission();
            RequestSerialization();
            Used(ACO_UiSound.KIND_CONFIRM);
        }

        public void ClickWingmen()
        {
            if (!CanUse() || Support == null || Support.GetCicWingmenCooldown() > 0f || Support.GetCicLeft() > 0f) { Deny(); return; }
            OpenConfirm(PENDING_WINGMEN);
        }

        private void DoWingmen()
        {
            if (!CanUse() || Support == null) { Deny(); return; }
            Vector3 center = _cursorWorld + Vector3.up * WingmenAltitude;
            Vector3 home = HomeBase != null ? HomeBase.position : Vector3.zero;
            Vector3 toCenter = new Vector3(center.x - home.x, 0f, center.z - home.z);
            if (toCenter.sqrMagnitude < 1f) { toCenter = HomeBase != null ? HomeBase.forward : Vector3.forward; toCenter.y = 0f; }
            if (toCenter.sqrMagnitude < 0.0001f) { toCenter = Vector3.forward; }
            toCenter = toCenter.normalized;
            float back = Mathf.Min(WingmenStartDistance, Mathf.Max(new Vector2(center.x - home.x, center.z - home.z).magnitude, 0f));
            Vector3 start = center - toCenter * back;
            if (!Support.CallWingmenArea(center, start, toCenter)) { Deny(); return; }
            Used(ACO_UiSound.KIND_CONFIRM);
        }

        public void ClickDrone()
        {
            if (!CanUse() || GetDroneCooldown() > 0f) { Deny(); return; }
            Vector3 from;
            if (_droneOn) { from = _dronePos; }
            else
            {
                Vector3 h = HomeBase != null ? HomeBase.position : Vector3.zero;
                from = new Vector3(h.x, SeaLevel + 40f, h.z);
            }
            if (DroneSync == null) { Deny(); return; }
            DroneSync.Send(CurrentMission(), Now(), from, _cursorWorld + Vector3.up * DroneAltitude);
            Used(ACO_UiSound.KIND_CONFIRM);
            if (Chatter) { Chatter.SayControlLow(UavLineText, UavLineClip, LineMaxDelay); }   // 로컬 — 보낸 사람만(6-282)
        }

        public void ClickCruise()
        {
            if (!CanUse() || Strike == null || Strike.CanLaunch(_cursorWorld) != 0) { Deny(); return; }
            OpenConfirm(PENDING_CRUISE);
        }

        private void DoCruise()
        {
            if (!CanUse() || Strike == null || Strike.CanLaunch(_cursorWorld) != 0) { Deny(); return; }
            if (!Strike.Launch(_cursorWorld)) { Deny(); return; }
            Used(ACO_UiSound.KIND_CONFIRM);
        }

        // ───────────── 확정 팝업 (로컬) ─────────────

        private void OpenConfirm(int what)
        {
            _pending = what;
            _pendingAt = Time.time;
            if (ConfirmPopup) { ConfirmPopup.SetActive(true); }
            if (Sound) { Sound.PlayAt(ACO_UiSound.KIND_CLICK, transform.position); }
            UpdateConfirmText();
        }

        private void CloseConfirm()
        {
            _pending = PENDING_NONE;
            if (ConfirmPopup) { ConfirmPopup.SetActive(false); }
        }

        public void ClickConfirm()
        {
            int what = _pending;
            CloseConfirm();
            if (what == PENDING_WINGMEN) { DoWingmen(); }
            else if (what == PENDING_CRUISE) { DoCruise(); }
        }

        public void ClickCancel()
        {
            CloseConfirm();
            if (Sound) { Sound.PlayAt(ACO_UiSound.KIND_CLICK, transform.position); }
        }

        /// 항모에서 커서까지 수평 거리(km, 소수 한 자리).
        private string CursorKm()
        {
            Vector3 home = HomeBase != null ? HomeBase.position : Vector3.zero;
            float d = new Vector2(_cursorWorld.x - home.x, _cursorWorld.z - home.z).magnitude * 0.001f;
            return d.ToString("F1");
        }

        private void UpdateConfirmText()
        {
            if (_pending == PENDING_NONE) { return; }
            string dist = T1("cic.confirm.dist", "RANGE {0} km", CursorKm());
            if (_pending == PENDING_WINGMEN)
            {
                if (ConfirmTitle) { ConfirmTitle.text = T("cic.btn.wm", "WINGMEN"); }
                if (ConfirmBody) { ConfirmBody.text = T("cic.confirm.wm", "2 FIGHTERS - 3km - 60s") + "\n" + dist; }
            }
            else
            {
                float eta = Strike != null ? Strike.EstimateFlight(_cursorWorld) : 0f;
                if (ConfirmTitle) { ConfirmTitle.text = T("cic.btn.cm", "CRUISE MSL"); }
                if (ConfirmBody) { ConfirmBody.text = T1("cic.confirm.cm", "4 MISSILES - ABOUT {0}s", Secs(eta)) + "\n" + dist; }
            }
        }

        private bool CanUse() { return IsRunning() && _hasCursor && _cursorMission == CurrentMission(); }

        private void TakeOwner()
        {
            if (!Networking.IsOwner(gameObject)) { Networking.SetOwner(Networking.LocalPlayer, gameObject); }
        }

        private void Used(int kind)
        {
            if (Progress) { Progress.NoteCicUse(); }
            if (Sound) { Sound.PlayAt(kind, transform.position); }
            UpdateTexts();
        }

        private void Deny()
        {
            if (Sound) { Sound.PlayAt(ACO_UiSound.KIND_DENY, transform.position); }
        }

        public override void OnDeserialization() { UpdateTexts(); }

        // ───────────── 테이블 표시 (근처에 있을 때만) ─────────────

        private void UpdateMarks()
        {
            UpdateCruiseMarks();
            bool wp = IsWaypointActive();
            if (WaypointMark)
            {
                if (WaypointMark.gameObject.activeSelf != wp) { WaypointMark.gameObject.SetActive(wp); }
                if (wp) { Vector3 l = Map.WorldToMap(_wpPos); l.y = 0f; WaypointMark.localPosition = l; }
            }
            int f = Support != null ? Support.GetCicFlight() : -1;
            bool area = f >= 0;
            if (AreaMark)
            {
                if (AreaMark.gameObject.activeSelf != area) { AreaMark.gameObject.SetActive(area); }
                if (area)
                {
                    Vector3 l = Map.WorldToMap(Support.GetCicCenter());
                    l.y = 0f;
                    AreaMark.localPosition = l;
                    float s = Support.CicWingmenRadius * Map.GetScale() * 2f;
                    AreaMark.localScale = new Vector3(s, AreaMark.localScale.y, s);
                }
            }
            if (DroneMark)
            {
                if (DroneMark.gameObject.activeSelf != _droneOn) { DroneMark.gameObject.SetActive(_droneOn); }
                if (_droneOn)
                {
                    DroneMark.localPosition = Map.WorldToMap(_dronePos);
                    DroneMark.localRotation = Quaternion.Euler(0f, _droneRot.eulerAngles.y, 0f);
                    if (DroneLabel)
                    {
                        VRCPlayerApi me = Networking.LocalPlayer;
                        if (Utilities.IsValid(me))
                        {
                            Vector3 d = DroneLabel.position - me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                            if (d.sqrMagnitude > 0.0001f) { DroneLabel.rotation = Quaternion.LookRotation(d); }
                        }
                    }
                }
            }
        }

        private void UpdateCruiseMarks()
        {
            if (CruiseMarks == null) { return; }
            int n = Strike != null ? Strike.GetMissileCount() : 0;
            for (int i = 0; i < CruiseMarks.Length; i++)
            {
                Transform t = CruiseMarks[i];
                if (!t) { continue; }
                bool on = i < n && Strike.IsMissileFlying(i);
                if (t.gameObject.activeSelf != on) { t.gameObject.SetActive(on); }
                if (on) { t.localPosition = Map.WorldToMap(Strike.GetMissilePosition(i)); }
            }
        }

        // ───────────── 글자 ─────────────

        public void LocalizationChanged() { UpdateTexts(); }

        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return s == key ? english : s;
        }
        private string T1(string key, string english, string a) { return T(key, english).Replace("{0}", a); }

        private void UpdateTexts()
        {
            bool running = IsRunning();
            bool cursor = running && _hasCursor;
            if (StatusText)
            {
                if (!running) { StatusText.text = T("cic.status.noop", "NO OPERATION"); }
                else if (!cursor) { StatusText.text = T("cic.status.tap", "POINT AT THE MAP AND PULL THE TRIGGER (CLICK)"); }
                else { StatusText.text = T("cic.status.cursor", "CURSOR SET - CHOOSE A TOOL"); }
            }
            if (CursorText) { CursorText.text = cursor ? T1("cic.lcd.cursor", "CURSOR {0} km", CursorKm()) : ""; }
            UpdateConfirmText();
            float wpCd = GetWaypointCooldown();
            SetTool(0, WaypointText, WaypointButton, WaypointLed, running, cursor, wpCd,
                IsWaypointActive() ? T1("cic.wp.shown", "SHOWN {0}s", Secs(WaypointTime - (Now() - _wpMs) * 0.001f)) : "");
            float wmCd = Support != null ? Support.GetCicWingmenCooldown() : 0f;
            float left = Support != null ? Support.GetCicLeft() : 0f;
            SetTool(1, WingmenText, WingmenButton, WingmenLed, running && left <= 0f, cursor, wmCd,
                left > 0f ? T1("cic.wm.onstation", "ON STATION {0}s", Secs(left)) : "");
            SetTool(2, DroneText, DroneButton, DroneLed, running, cursor, GetDroneCooldown(), "");
            if (Strike != null)
            {
                bool cmAvail = running;
                string cmBusy = "";
                float flight = Strike.GetFlightLeft();
                if (cursor && Strike.CanLaunch(_cursorWorld) == 3) { cmAvail = false; cmBusy = T("cic.cm.tooclose", "TOO CLOSE TO FLEET"); }
                else if (cursor && Strike.CanLaunch(_cursorWorld) == 4) { cmAvail = false; }
                if (cmBusy == "" && flight >= 0f) { cmBusy = T1("cic.cm.flight", "IN FLIGHT {0}s", Secs(flight)); }
                else if (cmBusy == "" && Strike.HasResult())
                {
                    cmBusy = T("cic.cm.result", "HIT {0} - DOWN {1}").Replace("{0}", Strike.GetResultHits().ToString()).Replace("{1}", Strike.GetResultDowns().ToString());
                }
                SetTool(3, CruiseText, CruiseButton, CruiseLed, cmAvail, cursor, Strike.GetCooldown(), cmBusy);
            }
        }

        /// 도구 한 줄: LCD 글자(준비 · 작전 중 초록, 쿨타임 앰버, 꺼짐 흐린 초록) · 키(준비가 아니면 어둡게) · LED(준비면 켬).
        private void SetTool(int i, TextMeshProUGUI text, Image button, Image led, bool available, bool cursor, float cooldown, string busy)
        {
            bool ready = available && cursor && cooldown <= 0f;
            if (text)
            {
                string s;
                Color c;
                if (!IsRunning()) { s = T("cic.status.noop", "NO OPERATION"); c = LcdOffColor; }
                else if (busy != "") { s = busy; c = LcdReadyColor; }
                else if (cooldown > 0f) { s = T1("cic.cooldown", "COOLDOWN {0}s", Secs(cooldown)); c = LcdWaitColor; }
                else if (!cursor) { s = T("cic.nocursor", "NO CURSOR"); c = LcdOffColor; }
                else { s = T("cic.ready", "READY"); c = LcdReadyColor; }
                text.text = s;
                text.color = c;
            }
            if (_keyColors == null) { return; }
            if (button)
            {
                Color k = _keyColors[i];
                button.color = ready ? k : new Color(k.r * KeyDim, k.g * KeyDim, k.b * KeyDim, k.a);
            }
            if (led) { led.color = ready ? _ledColors[i] : LedOffColor; }
        }

        private string Secs(float s) { return Mathf.CeilToInt(Mathf.Max(s, 0f)).ToString(); }
    }
}
