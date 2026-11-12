using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 지원 호출 관리자(씬에 하나, 6-142) — 벙커버스터 폭격과 급유 드론의 연출·판정. 호출한 사람(SUPPORT 다이얼 ACO_DFUNC_Support)이 네트워크 이벤트로 알리면
    /// 모든 화면이 받은 순간부터 같은 시간표로 각자 재현한다(늦게 들어온 사람은 진행 중인 지원을 못 봐도 된다). Supports[번호]가 기체를 가리킨다(Bake가 채운다).
    ///
    /// 벙커버스터(NetStrike): 지점 상공(지점 높이 + BomberAltitude)에 호출한 사람의 진행 방향으로 아군 폭격기가 나타나 지나가며 한 발을 떨어뜨리고 상승·이탈한다.
    ///  - 시간표: 출현 → BomberLeadDistance를 날아 투하점(= 지점에서 BomberSpeed × BombFallTime 뒤) → 투하 → BombFallTime 뒤 탄착(폭탄은 수평 속도 그대로, 높이는 등가속으로 지점에 닿는다).
    ///  - 탄착부터 끝까지 포드 레이저(호출한 기체의 LaserOrigin → 지점)를 모두에게 보인다. 탄착: 대폭발(ACO_FortressBlast) + 호출한 사람 화면만 범위 피해
    ///    (StrikeRadius, 중심 StrikeDamage → 가장자리 StrikeMinFraction, 아군 호위 대상 제외 — ACO_Ordnance와 같은 규칙, 격추 점수는 호출한 사람).
    /// 급유 드론(NetTankerStart): 호출한 기체 뒤 위(DroneStartOffset — 진행 방향 기준)에서 나타나 편대처럼 따라붙는다(LUNA UAV와 같은 방식 — 기체 속도만큼 같이 가고
    ///    자리 쪽으로 지수 보간, 자세는 진행 방향 + 뱅크 BankFollow만큼만). 자리(프로브 끝 + 호스 길이만큼 앞·위)에 오면 대기 → 호출한 사람이 SteadyTime 동안
    ///    안정(이탈 조건에 안 걸림)이면 NetTankerExtend → 기체 애니메이터 bool PROBE를 켜고 호스를 풀고
    ///    드로그를 프로브 끝에 꽂는다 → ContactTime(3초) 지나면 호출한 사람이 NetTankerDone → 그 기체를 가득(오너 화면이 연료·체력을 최대로, 모든 화면이 무장 보급 SFEXT_G_ReArm을
    ///    RearmRepeats번 — SAV 무장은 한 번에 10%씩 채운다. SAV 보급 이벤트를 연달아 부르면 오너가 매번 네트워크 이벤트를 보내므로 연료·체력은 직접) → 호스를 감고 이탈.
    ///  - 이탈 조건(호출한 사람 화면만, 호스를 풀기 시작한 뒤부터 완료 전까지): 하중 MinG 아래·MaxG 위, 뱅크 MaxBank 위(뒤집힘 포함), 애프터버너, 내리거나 격추 → NetTankerAbort.
    ///    접근·대기 중에는 기동해도 끊지 않는다 — 대기 중 불안정하면 HUD/MFD에 STEADY, WaitTimeout 동안 안정되지 않으면 돌아간다.
    ///    이탈하면 호출권은 쓰지 않고 쿨타임은 돈다. 완료하면 호출권 1장을 쓰고 쿨타임.
    /// 가디언 드론(NetGuardianStart, 6-143): 요격 드론 한 대가 호출한 기체 옆 살짝 앞(GuardianOffset — 왼쪽/오른쪽은 부를 때 랜덤, 이벤트에 실린다)에 편대로 붙어
    ///    GuardianDuration초 동안 나를 노린 미사일을 요격한다. 드론은 대공포(ACO_AAGunSite — CIWS와 같은 요격, 판정은 미사일, 무전 없음 SilentIntercept).
    ///    동기화는 이벤트만: 시작(기체·호출한 사람·쪽·남은 시간 — 늦게 들어온 사람에겐 호출한 사람이 다시 보낸다)·일찍 끝남(내림·격추). 시간이 다 된 끝은 각 화면이 센다.
    ///    호출권은 부를 때(다이얼), 쿨타임은 드론이 돌아갈 때(호출한 사람 화면).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_SupportManager : UdonSharpBehaviour
    {
        [Header("연결 (Bake)")]
        public ACO_DFUNC_Support[] Supports;
        public ACO_PlayerProgress Progress;
        public ACO_Chatter Chatter;

        [Header("벙커버스터 — 풀(같은 길이, 빌더가 만든다)")]
        public Transform[] Bombers;
        public Transform[] Bombs;
        public LineRenderer[] Lasers;
        public ACO_FortressBlast[] Blasts;
        [Tooltip("폭격기 엔진 소리(선택, 풀과 같은 순서)")]
        public AudioSource[] BomberSounds;

        [Header("벙커버스터 — 값")]
        public float BomberAltitude = 2500f;
        public float BomberSpeed = 220f;
        [Tooltip("출현부터 투하점까지 거리(m)")]
        public float BomberLeadDistance = 1500f;
        public float BombFallTime = 12f;
        [Tooltip("투하 뒤 폭격기가 보이는 시간(초) — 상승하며 이탈")]
        public float BomberExitTime = 14f;
        public float BomberClimb = 6f;
        public float StrikeDamage = 1500f;
        public float StrikeRadius = 500f;
        [Range(0f, 1f)] public float StrikeMinFraction = 0.3f;
        public LayerMask DamageLayers = 1 << 17;
        public LayerMask WaterLayers = 1 << 4;
        public float BlastScale = 1.2f;
        [Tooltip("SAV 무기 타입(킬 피드). 4 = 폭탄")]
        public byte WeaponType = 4;

        [Header("급유 드론 — 풀(빌더가 만든다)")]
        public ACO_TankerDrone[] Drones;

        [Header("급유 드론 — 값")]
        public float ExtendTime = 3f;
        public float ConnectTime = 1.5f;
        public float ContactTime = 3f;
        public float RetractTime = 2f;
        [Tooltip("감기 전에 드로그가 프로브에서 빠져 프로브 앞 대기 자리(PreContactGap)로 물러나는 시간(초) — 그 뒤 RetractTime 동안 감는다")]
        public float DisconnectTime = 0.8f;
        [Tooltip("드로그 대기 자리: 프로브 끝에서 기수 방향으로 이만큼(m) 앞(프로브 축 위). 드로그는 출구에서 여기까지 풀렸다가 ConnectTime 동안 프로브로 꽂히고, 뺄 때는 여기로 물러난 뒤 감긴다")]
        public float PreContactGap = 3f;
        public float LeaveTime = 8f;
        [Tooltip("완료 이벤트를 기다리는 최대 시간(초) — 못 받으면 호스를 감고 돌아간다")]
        public float ResultTimeout = 8f;
        [Tooltip("드론 출현 자리(호출한 기체 기준 — 진행 방향 프레임, 뱅크 무시, m)")]
        public Vector3 DroneStartOffset = new Vector3(0f, 300f, -2000f);
        [Tooltip("자리: 프로브 끝에서 호스 길이 × 이 비율만큼 앞, 위")]
        public float HoseReach = 0.95f;
        public float HoseRise = 0.15f;
        public float MinG = 0.3f;
        public float MaxG = 2.5f;
        public float MaxBank = 45f;
        [Tooltip("기수 피치 한계(도, 위·아래). 0이면 보지 않는다")]
        public float MaxPitch = 20f;
        [Tooltip("급유 속도 범위(kt — HUD 속도와 같은 대기 속도). 0이면 그쪽은 보지 않는다")]
        public float MinSpeedKt = 180f;
        public float MaxSpeedKt = 1000f;
        [Tooltip("드론은 MinSpeedKt보다 느리게 날 수 없다 — 기체가 그보다 느리면(착륙·저속) 자리를 지나쳐 버리고 급유 실패(쿨타임 TankerFailCooldown)로 돌아간다. "
            + "자리에 온 뒤 느려졌으면 이 시간(초) 동안 다시 빨라지기를 기다린다(HUD에 SPEED UP)")]
        public float SlowGraceTime = 2f;
        [Tooltip("자리에 온 뒤 이만큼(초) 안정하면 호스를 푼다")]
        public float SteadyTime = 1f;
        [Tooltip("자리에서 안정되기를 기다리는 최대 시간(초) — 넘으면 돌아간다(호출권 그대로, 쿨타임)")]
        public float WaitTimeout = 20f;
        [Tooltip("급유 실패(이탈·합류 못 함·안정 못 함·기체가 사라짐) 뒤 쿨타임(초) — 성공하면 진행도의 급유 쿨타임 그대로")]
        public float TankerFailCooldown = 15f;

        [Header("급유 드론 — 움직임 (LUNA UAV처럼 편대 추종)")]
        [Tooltip("접근할 때 편대 속도에 더하는 최대 상대 속력(m/s)")]
        public float ApproachSpeed = 220f;
        [Tooltip("남은 거리 × 이것 = 접근 상대 속력(가까워질수록 감속)")]
        public float ApproachGain = 0.8f;
        [Tooltip("상대 속도가 바뀌는 빠르기(1/s) — 낮을수록 묵직하게 가감속")]
        public float ApproachAccel = 1.5f;
        [Tooltip("자리에 왔다고 보는 거리(m)")]
        public float ArriveDistance = 5f;
        [Tooltip("이 시간(초) 안에 자리에 못 오면 돌아간다")]
        public float ApproachTimeout = 40f;
        [Tooltip("대기·호스 풀기 중 자리 추종 세기(1/s) — 높을수록 딱 붙는다")]
        public float FollowSharpness = 2.5f;
        [Tooltip("연결·결합·감기 중 자리 추종 세기(1/s)")]
        public float ContactSharpness = 8f;
        [Tooltip("자세가 따라가는 빠르기(1/s)")]
        public float TurnSharpness = 3f;
        [Tooltip("플레이어 뱅크를 이 비율만큼만 따라 기운다")]
        [Range(0f, 1f)] public float BankFollow = 0.5f;
        [Tooltip("이탈할 때 앞쪽 위로 가속(m/s²)")]
        public float LeaveAccel = 25f;
        [Tooltip("완료할 때 무장 보급 이벤트(SFEXT_G_ReArm)를 몇 번 연달아 — SAV·ACO 무장은 한 번에 10%씩 채운다")]
        public int RearmRepeats = 12;

        [Header("가디언 드론 — 풀(빌더가 만든다)")]
        public ACO_AAGunSite[] Guardians;

        [Header("가디언 드론 — 값")]
        public float GuardianDuration = 90f;
        [Tooltip("자리(진행 방향 + 뱅크 프레임, m): x = 옆(왼쪽/오른쪽은 부를 때 랜덤), y = 위, z = 앞")]
        public Vector3 GuardianOffset = new Vector3(18f, 2f, 25f);
        [Tooltip("출현 자리(같은 프레임 — x는 붙을 쪽 바깥)")]
        public Vector3 GuardianStartOffset = new Vector3(80f, 120f, -600f);
        [Tooltip("자리 추종 세기(1/s)")]
        public float GuardianSharpness = 4f;
        [Tooltip("이 거리(m) 안이면 접근을 마치고 자리에 붙는다")]
        public float GuardianJoinDistance = 25f;
        [Tooltip("돌아갈 때 보이는 시간(초) — 위·바깥으로 가속하며 빠진다")]
        public float GuardianLeaveTime = 6f;

        [Header("가디언 드론 — 저속 선회 (기체가 MinSpeedKt보다 느릴 때 — 착륙·이륙 직후)")]
        [Tooltip("선회 반경(m). 드론은 최저 속도(MinSpeedKt)로 돈다")]
        public float GuardianOrbitRadius = 600f;
        [Tooltip("선회 높이(기체 위, m)")]
        public float GuardianOrbitHeight = 300f;
        [Tooltip("선회 중 방어 반경(m) — 머리 위에서도 내 기체를 노리는 미사일을 요격하게 넓힌다. 옆 자리로 돌아오면 원래 값")]
        public float GuardianOrbitDefense = 900f;
        [Tooltip("다시 옆 자리로 붙는 속도 = MinSpeedKt × 이것(오락가락하지 않게 조금 높게)")]
        public float GuardianRejoinFactor = 1.1f;

        [Header("무전 (호출한 사람만, LINE_* 순서)")]
        [TextArea] public string[] LineTexts = new string[] {
            "Copy your mark. Bomber inbound.",
            "Bomb away.",
            "Impact. Good effect on target.",
            "Tanker drone en route. Hold steady.",
            "Contact.",
            "Transfer complete. Clear to break.",
            "Breakaway, breakaway. Tanker is returning.",
            "Guardian drone on station. Missile defense active.",
            "Guardian drone returning to base." };
        public AudioClip[] LineClips = new AudioClip[9];
        public float LineMaxDelay = 4f;

        public const int LINE_STRIKE_CALL = 0;
        public const int LINE_STRIKE_RELEASE = 1;
        public const int LINE_STRIKE_IMPACT = 2;
        public const int LINE_TANKER_CALL = 3;
        public const int LINE_TANKER_CONTACT = 4;
        public const int LINE_TANKER_DONE = 5;
        public const int LINE_TANKER_BREAK = 6;
        public const int LINE_GUARDIAN_CALL = 7;
        public const int LINE_GUARDIAN_END = 8;

        private const int G_IDLE = 0;
        private const int G_ACTIVE = 1;
        private const int G_LEAVE = 2;

        private const int T_IDLE = 0;
        private const int T_APPROACH = 1;
        private const int T_WAIT = 2;      // 자리에서 안정되기를 기다린다
        private const int T_EXTEND = 3;
        private const int T_CONNECT = 4;
        private const int T_CONTACT = 5;
        private const int T_RETRACT = 6;
        private const int T_LEAVE = 7;

        /// 범위 피해 공격자: SaccTarget이 피해를 준 오브젝트의 UdonBehaviour에서 이 이름(EntityControl)을 읽는다 — 피해 직전에 호출한 기체를 넣는다.
        [System.NonSerialized] public SaccEntity EntityControl;

        // 폭격 슬롯
        private bool[] _sActive;
        private float[] _sStart;
        private Vector3[] _sPoint;
        private Vector3[] _sDir;
        private int[] _sSupport;
        private bool[] _sLocal;
        private bool[] _sReleased;
        private bool[] _sImpacted;
        private bool[] _sSaidImpact;

        // 드론 슬롯
        private int[] _dSupport;
        private int[] _dState;
        private float[] _dT;
        private bool[] _dLocal;
        private float[] _dExtend;
        private Vector3[] _dHeadFrom;   // 빠지기(꽂혀 있었으면) / 감기(풀다 말았으면)를 시작할 때의 드로그 자리(기체 로컬)
        private Vector3[] _dHeadNow;    // 지난 프레임 드로그 자리(기체 로컬 — 빠질 때 여기서 시작)
        private float[] _dUnplug;       // 이번 감기에서 빠지는 시간(초, 0 = 꽂혀 있지 않았다)
        private float[] _dExtendFrom;   // 감기 시작 때 풀린 정도(풀다 말고 감으면 거기서부터)
        private bool[] _dResult;        // 호출한 사람: 결과(완료/이탈)를 보냈다
        private bool[] _dFresh;         // 첫 프레임에 출현 자리를 잡는다
        private Vector3[] _dPos;        // 드론 월드 위치(편대 추종 — 매 프레임 기체 속도만큼 같이 가고 목표로 보간)
        private Quaternion[] _dRot;
        private Vector3[] _dRelVel;     // 편대 기준 상대 속도(접근·이탈)
        private Vector3[] _dVehPos;     // 기체 위치(속도 추정)
        private Vector3[] _dVehVel;
        private float[] _dSteady;       // 호출한 사람: 대기 중 안정된 시간
        private float[] _dSlow;         // 호출한 사람: 대기 중 최저 속도 아래였던 시간
        private bool[] _dExtendSent;

        // 가디언 슬롯
        private int[] _gSupport;
        private int[] _gState;
        private float[] _gT;
        private float[] _gEnd;
        private float[] _gSide;
        private bool[] _gLocal;
        private bool[] _gFresh;
        private Vector3[] _gPos;
        private Quaternion[] _gRot;
        private Vector3[] _gRelVel;
        private Vector3[] _gVehPos;
        private Vector3[] _gVehVel;
        private bool[] _gOrbit;         // 저속 선회 중
        private Vector3[] _gOrbitVel;   // 선회 중 드론 속도(월드)
        private float[] _gBaseDefense;  // 가디언 원래 방어 반경

        private Collider[] _hits = new Collider[256];
        private SaccTarget[] _damaged = new SaccTarget[64];
        private bool _init;

        void Start() { Init(); }

        private void Init()
        {
            if (_init) { return; }
            _init = true;
            int s = Bombers != null ? Bombers.Length : 0;
            _sActive = new bool[s];
            _sStart = new float[s];
            _sPoint = new Vector3[s];
            _sDir = new Vector3[s];
            _sSupport = new int[s];
            _sLocal = new bool[s];
            _sReleased = new bool[s];
            _sImpacted = new bool[s];
            _sSaidImpact = new bool[s];
            for (int i = 0; i < s; i++) { HideStrike(i); }
            int d = Drones != null ? Drones.Length : 0;
            _dSupport = new int[d];
            _dState = new int[d];
            _dT = new float[d];
            _dLocal = new bool[d];
            _dExtend = new float[d];
            _dHeadFrom = new Vector3[d];
            _dHeadNow = new Vector3[d];
            _dUnplug = new float[d];
            _dExtendFrom = new float[d];
            _dResult = new bool[d];
            _dFresh = new bool[d];
            _dPos = new Vector3[d];
            _dRot = new Quaternion[d];
            _dRelVel = new Vector3[d];
            _dVehPos = new Vector3[d];
            _dVehVel = new Vector3[d];
            _dSteady = new float[d];
            _dSlow = new float[d];
            _dExtendSent = new bool[d];
            int g = Guardians != null ? Guardians.Length : 0;
            _gSupport = new int[g];
            _gState = new int[g];
            _gT = new float[g];
            _gEnd = new float[g];
            _gSide = new float[g];
            _gLocal = new bool[g];
            _gFresh = new bool[g];
            _gPos = new Vector3[g];
            _gRot = new Quaternion[g];
            _gRelVel = new Vector3[g];
            _gVehPos = new Vector3[g];
            _gVehVel = new Vector3[g];
            _gOrbit = new bool[g];
            _gOrbitVel = new Vector3[g];
            _gBaseDefense = new float[g];
            for (int i = 0; i < g; i++)
            {
                _gSupport[i] = -1;
                if (Guardians[i]) { _gBaseDefense[i] = Guardians[i].DefenseRadius; }
                if (Guardians[i]) { Guardians[i].gameObject.SetActive(false); }
            }
            for (int i = 0; i < d; i++)
            {
                _dSupport[i] = -1;
                // 꺼진 오브젝트의 Udon은 불러도 돌지 않을 수 있다 — 켜고 끄기는 여기서 직접
                if (Drones[i]) { Drones[i].gameObject.SetActive(false); }
            }
        }

        // ───────────── 호출 (호출한 사람 화면) ─────────────

        /// 벙커버스터를 부른다(SUPPORT 다이얼). 빈 풀이 없으면 false.
        public bool CallStrike(int support, Vector3 point, Vector3 dir)
        {
            Init();
            if (support < 0 || Bombers == null || Bombers.Length == 0) { return false; }
            Say(LINE_STRIKE_CALL);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStrike), support, point, dir, Networking.LocalPlayer.playerId);
            return true;
        }

        /// 급유 드론을 부른다. 빈 드론이 없으면 false.
        public bool CallTanker(int support)
        {
            Init();
            if (support < 0 || FreeDrone() < 0) { return false; }
            Say(LINE_TANKER_CALL);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetTankerStart), support, Networking.LocalPlayer.playerId);
            return true;
        }

        public bool IsTankerActive(int support)
        {
            Init();
            return DroneOf(support) >= 0;
        }

        /// 가디언 드론을 부른다(쪽은 여기서 랜덤 — 모든 화면이 같은 쪽). 빈 드론이 없으면 false.
        public bool CallGuardian(int support)
        {
            Init();
            if (support < 0 || (GuardianOf(support) < 0 && FreeGuardian() < 0)) { return false; }
            int side = Random.value < 0.5f ? -1 : 1;
            Say(LINE_GUARDIAN_CALL);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetGuardianStart), support, Networking.LocalPlayer.playerId, side, GuardianDuration);
            return true;
        }

        /// 가디언이 떠 있으면 남은 시간(초), 아니면 0.
        public float GetGuardianLeft(int support)
        {
            Init();
            int i = GuardianOf(support);
            return i >= 0 && _gState[i] == G_ACTIVE ? Mathf.Max(_gEnd[i] - Time.time, 0.01f) : 0f;
        }

        /// 조종사가 내렸거나 격추됐다(SUPPORT 다이얼): 그 기체의 급유를 끊고 가디언을 돌려보낸다.
        public void LocalPilotGone(int support)
        {
            Init();
            int gi = GuardianOf(support);
            if (gi >= 0 && _gLocal[gi] && _gState[gi] == G_ACTIVE) { SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetGuardianEnd), support); }
            int i = DroneOf(support);
            if (i < 0 || !_dLocal[i] || _dResult[i] || _dState[i] >= T_RETRACT) { return; }
            Abort(i, "");
        }

        // ───────────── 네트워크 ─────────────

        [NetworkCallable]
        public void NetStrike(int support, Vector3 point, Vector3 dir, int callerId)
        {
            Init();
            int n = Bombers != null ? Bombers.Length : 0;
            if (n == 0) { return; }
            // 빈 슬롯, 없으면 가장 오래된 것
            int slot = -1;
            float oldest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (!_sActive[i]) { slot = i; break; }
                if (_sStart[i] < oldest) { oldest = _sStart[i]; slot = i; }
            }
            if (slot < 0) { return; }
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) { dir = Vector3.forward; }
            _sActive[slot] = true;
            _sStart[slot] = Time.time;
            _sPoint[slot] = point;
            _sDir[slot] = dir.normalized;
            _sSupport[slot] = support;
            _sLocal[slot] = Networking.LocalPlayer != null && Networking.LocalPlayer.playerId == callerId;
            _sReleased[slot] = false;
            _sImpacted[slot] = false;
            _sSaidImpact[slot] = false;
            if (Bombers[slot]) { Bombers[slot].gameObject.SetActive(true); }
            if (BomberSounds != null && slot < BomberSounds.Length && BomberSounds[slot]) { BomberSounds[slot].Play(); }
        }

        [NetworkCallable]
        public void NetTankerStart(int support, int callerId)
        {
            Init();
            if (DroneOf(support) >= 0) { return; }
            int i = FreeDrone();
            if (i < 0 || !Drones[i]) { return; }
            _dSupport[i] = support;
            _dLocal[i] = Networking.LocalPlayer != null && Networking.LocalPlayer.playerId == callerId;
            _dResult[i] = false;
            _dExtend[i] = 0f;
            _dFresh[i] = true;
            _dSteady[i] = 0f;
            _dSlow[i] = 0f;
            _dExtendSent[i] = false;
            SetState(i, T_APPROACH);
            Drones[i].gameObject.SetActive(true);
            Drones[i].SetShown(true);
            Drones[i].PoseHose(Drones[i].GetHoseBase(), Drones[i].transform.forward);
        }

        /// 호출한 사람이 자리에서 안정됐다: 프로브를 펴고 호스를 푼다(아직 접근 중인 화면도 바로).
        [NetworkCallable]
        public void NetTankerExtend(int support)
        {
            Init();
            int i = DroneOf(support);
            if (i < 0 || _dState[i] > T_WAIT) { return; }
            SetState(i, T_EXTEND);
            SetProbe(SupportAt(support), true);
        }

        [NetworkCallable]
        public void NetTankerDone(int support)
        {
            Init();
            int i = DroneOf(support);
            ACO_DFUNC_Support sup = SupportAt(support);
            if (sup && sup.EntityControl)
            {
                SaccAirVehicle air = sup.AirVehicle;
                // 연료·체력은 기체 오너만(동기화 값) — 가득 채운 뒤 SAV 급유 이벤트 한 번으로 저연료 상태를 풀게 한다(오너가 한 번만 알린다)
                if (air != null && Networking.IsOwner(air.gameObject))
                {
                    air.Fuel = air.FullFuel;
                    air.Health = air.FullHealth;
                }
                sup.EntityControl.SendEventToExtensions("SFEXT_G_ReFuel");
                // 무장은 각 화면이 센다 — 모든 화면이 같은 횟수만큼
                for (int k = 0; k < RearmRepeats; k++) { sup.EntityControl.SendEventToExtensions("SFEXT_G_ReArm"); }
            }
            if (i >= 0 && _dState[i] < T_RETRACT) { BeginRetract(i); }
        }

        [NetworkCallable]
        public void NetTankerAbort(int support)
        {
            Init();
            int i = DroneOf(support);
            if (i >= 0 && _dState[i] < T_RETRACT) { BeginRetract(i); }
        }

        [NetworkCallable]
        public void NetGuardianStart(int support, int callerId, int side, float seconds)
        {
            Init();
            int i = GuardianOf(support);
            if (i < 0) { i = FreeGuardian(); }
            if (i < 0 || !Guardians[i]) { return; }
            if (_gState[i] == G_IDLE)
            {
                _gFresh[i] = true;
                SetGuardianOrbit(i, false, Vector3.zero, Vector3.zero);
                Guardians[i].gameObject.SetActive(true);
            }
            _gSupport[i] = support;
            _gLocal[i] = Networking.LocalPlayer != null && Networking.LocalPlayer.playerId == callerId;
            _gSide[i] = side < 0 ? -1f : 1f;
            _gEnd[i] = Time.time + Mathf.Max(seconds, 0f);
            if (_gState[i] != G_ACTIVE) { _gState[i] = G_ACTIVE; _gT[i] = Time.time; }
        }

        [NetworkCallable]
        public void NetGuardianEnd(int support)
        {
            Init();
            int i = GuardianOf(support);
            if (i >= 0 && _gState[i] == G_ACTIVE) { BeginGuardianLeave(i); }
        }

        /// 늦게 들어온 사람: 내가 부른 가디언이 떠 있으면 남은 시간으로 다시 알린다(이미 있는 화면은 남은 시간만 맞춘다).
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!_init || player == null || player.isLocal || Networking.LocalPlayer == null) { return; }
            for (int i = 0; i < _gState.Length; i++)
            {
                if (_gState[i] != G_ACTIVE || !_gLocal[i]) { continue; }
                SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetGuardianStart), _gSupport[i], Networking.LocalPlayer.playerId, (int)_gSide[i], Mathf.Max(_gEnd[i] - Time.time, 0f));
            }
        }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            if (!_init) { return; }
            for (int i = 0; i < _sActive.Length; i++) { if (_sActive[i]) { UpdateStrike(i); } }
        }

        /// 기체를 따라붙는 급유 드론·가디언 드론은 그 프레임 기체 자세가 다 정해진 뒤에 놓는다(Update에선 기체가 그 뒤에 움직여
        /// 고속에서 드로그가 프로브와 한 프레임씩 어긋나 뚝뚝 끊겨 보였다).
        public override void PostLateUpdate()
        {
            if (!_init) { return; }
            for (int i = 0; i < _dState.Length; i++) { if (_dState[i] != T_IDLE) { UpdateDrone(i); } }
            for (int i = 0; i < _gState.Length; i++) { if (_gState[i] != G_IDLE) { UpdateGuardian(i); } }
        }

        // ───────────── 가디언 드론 ─────────────

        private void UpdateGuardian(int i)
        {
            ACO_AAGunSite drone = Guardians[i];
            ACO_DFUNC_Support sup = SupportAt(_gSupport[i]);
            Transform vt = sup != null && sup.EntityControl != null ? sup.EntityControl.transform : null;
            if (drone == null || vt == null || !vt.gameObject.activeInHierarchy) { FinishGuardian(i); return; }
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            // 기체 속도(위치 차, 순간이동은 버린다)
            Vector3 vp = vt.position;
            if (_gFresh[i]) { _gVehPos[i] = vp; _gVehVel[i] = vt.forward * 150f; }
            Vector3 raw = (vp - _gVehPos[i]) / dt;
            if (!_gFresh[i] && raw.sqrMagnitude < 2250000f) { _gVehVel[i] = Vector3.Lerp(_gVehVel[i], raw, 1f - Mathf.Exp(-8f * dt)); }
            _gVehPos[i] = vp;
            Vector3 vel = _gVehVel[i];

            // 진행 방향 + 뱅크 프레임 — 뱅크해도 조종석 옆 같은 자리에 보이게 뱅크를 다 따라간다
            Vector3 dirFwd = vel.sqrMagnitude > 100f ? vel.normalized : vt.forward;
            if (Mathf.Abs(dirFwd.y) > 0.98f) { dirFwd = vt.forward; }
            Quaternion path = Quaternion.LookRotation(dirFwd, Vector3.up);
            float roll = -Mathf.Atan2(vt.right.y, vt.up.y) * Mathf.Rad2Deg;
            Quaternion formation = path * Quaternion.Euler(0f, 0f, -roll);
            float side = _gSide[i];

            if (_gFresh[i])
            {
                _gFresh[i] = false;
                _gPos[i] = vp + path * new Vector3(GuardianStartOffset.x * side, GuardianStartOffset.y, GuardianStartOffset.z);
                _gRot[i] = path;
                _gRelVel[i] = Vector3.zero;
            }

            // 저속 선회: 드론은 최저 속도(MinSpeedKt)보다 느리게 날 수 없다 — 기체가 그보다 느리면 머리 위를 돌며 엄호, 다시 빨라지면 옆 자리로
            // (모든 화면이 같은 방법으로 잰 속도로 정한다)
            float minMs = MinSpeedKt > 0f ? MinSpeedKt / 1.9438445f : 0f;
            float speed = vel.magnitude;
            if (_gState[i] == G_ACTIVE && minMs > 0f)
            {
                if (!_gOrbit[i] && speed < minMs) { SetGuardianOrbit(i, true, vel + _gRelVel[i], vel); }
                else if (_gOrbit[i] && speed > minMs * GuardianRejoinFactor) { SetGuardianOrbit(i, false, _gOrbitVel[i], vel); }
            }

            Vector3 pos = _gPos[i] + vel * dt;   // 편대: 기체 속도만큼 같이 간다
            Quaternion want = formation;
            float t = Time.time - _gT[i];
            if (_gOrbit[i])
            {
                float r = Mathf.Max(GuardianOrbitRadius, 50f);
                if (_gState[i] == G_ACTIVE)
                {
                    // 선회 자리: 기체 위 GuardianOrbitHeight, 반경 r. 각도는 서버 시각이라 모든 화면이 같은 자리, 도는 방향은 붙는 쪽(side)
                    float omega = Mathf.Max(minMs, 1f) / r;
                    double period = 2.0 * Mathf.PI / omega;
                    float ang = (float)(Networking.GetServerTimeInSeconds() % period) * omega * side;
                    Vector3 point = vp + Vector3.up * GuardianOrbitHeight + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    Vector3 tangentVel = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * (omega * side * r);
                    Vector3 desired = tangentVel + vel + (point - _gPos[i]) * 0.5f;
                    float cap = minMs + ApproachSpeed;
                    if (desired.sqrMagnitude > cap * cap) { desired = desired.normalized * cap; }
                    if (desired.sqrMagnitude < minMs * minMs) { desired = (desired.sqrMagnitude > 0.01f ? desired.normalized : _gRot[i] * Vector3.forward) * minMs; }
                    _gOrbitVel[i] = Vector3.Lerp(_gOrbitVel[i], desired, 1f - Mathf.Exp(-ApproachAccel * dt));
                    if (Time.time >= _gEnd[i]) { BeginGuardianLeave(i); }
                }
                else
                {
                    // 선회 중에 돌아감: 지금 속도 그대로 위로 가속
                    _gOrbitVel[i] += Vector3.up * (LeaveAccel * dt);
                    if (t >= GuardianLeaveTime) { FinishGuardian(i); return; }
                }
                pos = _gPos[i] + _gOrbitVel[i] * dt;
                Vector3 v = _gOrbitVel[i];
                if (v.sqrMagnitude > 1f)
                {
                    // 도는 쪽으로 기울인다(원심 가속에 맞춰 — 최대 70도). 돌아갈 때는 수평으로
                    float bankDeg = 0f;
                    if (_gState[i] == G_ACTIVE)
                    {
                        float turn = Vector3.Cross(v.normalized, vp - pos).y;   // < 0 = 중심이 왼쪽
                        bankDeg = Mathf.Min(Mathf.Atan2(v.sqrMagnitude, r * 9.81f) * Mathf.Rad2Deg, 70f);
                        if (turn > 0f) { bankDeg = -bankDeg; }
                    }
                    want = Quaternion.LookRotation(v, Vector3.up) * Quaternion.Euler(0f, 0f, bankDeg);
                }
            }
            else if (_gState[i] == G_ACTIVE)
            {
                Vector3 station = vp + formation * new Vector3(GuardianOffset.x * side, GuardianOffset.y, GuardianOffset.z);
                Vector3 to = station - pos;
                float dist = to.magnitude;
                if (dist > GuardianJoinDistance)
                {
                    // 접근: 상대 속도로 다가오며 감속(급유 드론과 같은 값)
                    Vector3 desired = to * (Mathf.Min(ApproachSpeed, dist * ApproachGain) / dist);
                    _gRelVel[i] = Vector3.Lerp(_gRelVel[i], desired, 1f - Mathf.Exp(-ApproachAccel * dt));
                    pos += _gRelVel[i] * dt;
                    Vector3 v = vel + _gRelVel[i];
                    if (v.sqrMagnitude > 1f) { want = Quaternion.LookRotation(v, Vector3.up); }
                }
                else
                {
                    _gRelVel[i] = Vector3.zero;
                    pos = Vector3.Lerp(pos, station, 1f - Mathf.Exp(-GuardianSharpness * dt));
                }
                if (Time.time >= _gEnd[i]) { BeginGuardianLeave(i); }
            }
            else
            {
                // 돌아감: 위·바깥으로 가속
                _gRelVel[i] += (formation * new Vector3(0.5f * side, 1f, 0.6f)).normalized * (LeaveAccel * dt);
                pos += _gRelVel[i] * dt;
                want = Quaternion.LookRotation(vel + _gRelVel[i], Vector3.up);
                if (t >= GuardianLeaveTime) { FinishGuardian(i); return; }
            }
            _gPos[i] = pos;
            _gRot[i] = Quaternion.Slerp(_gRot[i], want, 1f - Mathf.Exp(-TurnSharpness * dt));
            drone.transform.SetPositionAndRotation(pos, _gRot[i]);
        }

        /// 저속 선회로 들어가거나(on) 옆 자리로 돌아온다. 지금 드론 속도(월드)를 이어받아 튀지 않게. 선회 중엔 방어 반경을 넓힌다.
        private void SetGuardianOrbit(int i, bool on, Vector3 droneVel, Vector3 vehVel)
        {
            _gOrbit[i] = on;
            if (on) { _gOrbitVel[i] = droneVel; }
            else { _gRelVel[i] = droneVel - vehVel; }
            ACO_AAGunSite g = Guardians[i];
            if (g != null) { g.DefenseRadius = on ? Mathf.Max(GuardianOrbitDefense, _gBaseDefense[i]) : _gBaseDefense[i]; }
        }

        /// 돌아가기 시작: 호출한 사람 화면은 쿨타임 + 무전.
        private void BeginGuardianLeave(int i)
        {
            _gState[i] = G_LEAVE;
            _gT[i] = Time.time;
            _gRelVel[i] = Vector3.zero;
            if (_gLocal[i])
            {
                if (Progress) { Progress.StartCallCooldown(ACO_PlayerProgress.CALL_GUARDIAN); }
                Say(LINE_GUARDIAN_END);
            }
        }

        private void FinishGuardian(int i)
        {
            // 호출한 사람: 돌아가기 전에 사라졌으면(기체가 사라짐 등) 쿨타임만
            if (_gLocal[i] && _gState[i] == G_ACTIVE && Progress) { Progress.StartCallCooldown(ACO_PlayerProgress.CALL_GUARDIAN); }
            _gState[i] = G_IDLE;
            _gSupport[i] = -1;
            if (Guardians[i]) { Guardians[i].gameObject.SetActive(false); }
        }

        private int GuardianOf(int support)
        {
            if (support < 0 || _gSupport == null) { return -1; }
            for (int i = 0; i < _gSupport.Length; i++) { if (_gSupport[i] == support && _gState[i] != G_IDLE) { return i; } }
            return -1;
        }

        private int FreeGuardian()
        {
            if (_gState == null) { return -1; }
            for (int i = 0; i < _gState.Length; i++) { if (_gState[i] == G_IDLE && Guardians[i]) { return i; } }
            return -1;
        }

        // ───────────── 벙커버스터 ─────────────

        private void UpdateStrike(int i)
        {
            float t = Time.time - _sStart[i];
            float speed = Mathf.Max(BomberSpeed, 1f);
            float fall = Mathf.Max(BombFallTime, 0.5f);
            float lead = Mathf.Max(BomberLeadDistance, 0f) / speed;
            Vector3 dir = _sDir[i];
            Vector3 point = _sPoint[i];
            Vector3 release = point - dir * (speed * fall) + Vector3.up * BomberAltitude;
            Vector3 spawn = release - dir * BomberLeadDistance;

            // 폭격기
            Transform bomber = Bombers[i];
            bool bomberOn = t < lead + BomberExitTime;
            if (bomber)
            {
                if (bomberOn)
                {
                    Vector3 p;
                    Vector3 v;
                    if (t <= lead) { p = spawn + dir * (speed * t); v = dir * speed; }
                    else
                    {
                        float u = t - lead;
                        p = release + dir * (speed * u) + Vector3.up * (0.5f * BomberClimb * u * u);
                        v = dir * speed + Vector3.up * (BomberClimb * u);
                    }
                    bomber.SetPositionAndRotation(p, Quaternion.LookRotation(v));
                }
                else if (bomber.gameObject.activeSelf)
                {
                    bomber.gameObject.SetActive(false);
                    if (BomberSounds != null && i < BomberSounds.Length && BomberSounds[i]) { BomberSounds[i].Stop(); }
                }
            }

            // 폭탄
            Transform bomb = i < Bombs.Length ? Bombs[i] : null;
            if (t >= lead && !_sImpacted[i])
            {
                if (!_sReleased[i])
                {
                    _sReleased[i] = true;
                    if (bomb) { bomb.gameObject.SetActive(true); }
                    if (_sLocal[i]) { Say(LINE_STRIKE_RELEASE); }
                }
                float u = Mathf.Min(t - lead, fall);
                float g = 2f * BomberAltitude / (fall * fall);
                Vector3 p = release + dir * (speed * u) + Vector3.down * (0.5f * g * u * u);
                Vector3 v = dir * speed + Vector3.down * (g * u);
                if (bomb) { bomb.SetPositionAndRotation(p, Quaternion.LookRotation(v)); }
                if (t - lead >= fall) { Impact(i); }
            }

            // 포드 레이저: 탄착까지
            LineRenderer laser = Lasers != null && i < Lasers.Length ? Lasers[i] : null;
            if (laser)
            {
                ACO_DFUNC_Support sup = SupportAt(_sSupport[i]);
                bool on = !_sImpacted[i] && sup != null && sup.EntityControl != null && sup.EntityControl.gameObject.activeInHierarchy;
                if (on)
                {
                    Vector3 from = sup.LaserOrigin ? sup.LaserOrigin.position : sup.EntityControl.transform.position - sup.EntityControl.transform.up * 1.5f;
                    laser.SetPosition(0, from);
                    laser.SetPosition(1, point);
                }
                if (laser.enabled != on) { laser.enabled = on; }
            }

            if (_sImpacted[i] && !_sSaidImpact[i] && t >= lead + fall + 2f)
            {
                _sSaidImpact[i] = true;
                if (_sLocal[i]) { Say(LINE_STRIKE_IMPACT); }
            }
            if (!bomberOn && _sImpacted[i] && _sSaidImpact[i]) { _sActive[i] = false; HideStrike(i); }
        }

        private void Impact(int i)
        {
            _sImpacted[i] = true;
            Transform bomb = i < Bombs.Length ? Bombs[i] : null;
            if (bomb) { bomb.gameObject.SetActive(false); }
            Vector3 point = _sPoint[i];
            bool water = Physics.Raycast(point + Vector3.up * 20f, Vector3.down, 40f, WaterLayers, QueryTriggerInteraction.Collide);
            ACO_FortressBlast blast = Blasts != null && i < Blasts.Length ? Blasts[i] : null;
            if (blast) { blast.Play(point, water, BlastScale); }
            if (_sLocal[i]) { ApplySplash(point, SupportAt(_sSupport[i])); }
        }

        /// 범위 피해(호출한 사람 화면만) — ACO_Ordnance.ApplySplash와 같은 규칙: 유닛마다 가장 가까운 콜라이더 한 번, 아군 호위 대상 제외, 함선 부위 배율.
        private void ApplySplash(Vector3 center, ACO_DFUNC_Support sup)
        {
            if (StrikeRadius <= 0f) { return; }
            EntityControl = sup != null ? sup.EntityControl : null;
            int n = Physics.OverlapSphereNonAlloc(center, StrikeRadius, _hits, DamageLayers);
            int damagedCount = 0;
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i];
                if (!c) { continue; }
                SaccTarget target = c.GetComponentInParent<SaccTarget>();
                if (!target || target.Health <= 0f) { continue; }
                if (EntityControl != null && c.transform.IsChildOf(EntityControl.transform)) { continue; }
                ACO_EscortUnit escort = c.GetComponentInParent<ACO_EscortUnit>();
                if (escort != null && !escort.Hostile) { continue; }
                bool done = false;
                for (int k = 0; k < damagedCount; k++) { if (_damaged[k] == target) { done = true; break; } }
                if (done) { continue; }
                float best = float.MaxValue;
                for (int j = i; j < n; j++)
                {
                    Collider cj = _hits[j];
                    if (!cj || cj.GetComponentInParent<SaccTarget>() != target) { continue; }
                    float d = Vector3.Distance(center, cj.bounds.ClosestPoint(center));
                    if (d < best) { best = d; }
                }
                float dmg = StrikeDamage * Mathf.Lerp(1f, StrikeMinFraction, Mathf.Clamp01(best / StrikeRadius));
                ACO_Warship ship = target.GetComponentInParent<ACO_Warship>();
                if (ship != null)
                {
                    if (ship.IsSplashImmune(target)) { continue; }
                    dmg *= ship.GetSplashScale(target);
                }
                target.LastHitByPlayer = Networking.LocalPlayer;
                target.LastHitWeaponType = WeaponType;
                target.WeaponDamageTarget(dmg, gameObject, WeaponType);
                if (damagedCount < _damaged.Length) { _damaged[damagedCount] = target; damagedCount++; }
            }
        }

        private void HideStrike(int i)
        {
            if (Bombers != null && i < Bombers.Length && Bombers[i]) { Bombers[i].gameObject.SetActive(false); }
            if (Bombs != null && i < Bombs.Length && Bombs[i]) { Bombs[i].gameObject.SetActive(false); }
            if (Lasers != null && i < Lasers.Length && Lasers[i]) { Lasers[i].enabled = false; }
            if (BomberSounds != null && i < BomberSounds.Length && BomberSounds[i]) { BomberSounds[i].Stop(); }
        }

        // ───────────── 급유 드론 ─────────────

        private void SetState(int i, int state)
        {
            _dState[i] = state;
            _dT[i] = Time.time;
        }

        private void UpdateDrone(int i)
        {
            ACO_TankerDrone drone = Drones[i];
            ACO_DFUNC_Support sup = SupportAt(_dSupport[i]);
            Transform vt = sup != null && sup.EntityControl != null ? sup.EntityControl.transform : null;
            if (drone == null || vt == null || !vt.gameObject.activeInHierarchy) { Finish(i); return; }
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float t = Time.time - _dT[i];
            int state = _dState[i];

            // 기체 속도(위치 차 — 모든 화면에서 같은 방법, 순간이동은 버린다)
            Vector3 vp = vt.position;
            if (_dFresh[i]) { _dVehPos[i] = vp; _dVehVel[i] = vt.forward * 150f; }
            Vector3 raw = (vp - _dVehPos[i]) / dt;
            if (!_dFresh[i] && raw.sqrMagnitude < 2250000f) { _dVehVel[i] = Vector3.Lerp(_dVehVel[i], raw, 1f - Mathf.Exp(-8f * dt)); }
            _dVehPos[i] = vp;
            Vector3 vel = _dVehVel[i];

            // 진행 방향 프레임(뱅크 없음) — 자리·출현 오프셋은 이 프레임으로
            Vector3 dirFwd = vel.sqrMagnitude > 100f ? vel.normalized : vt.forward;
            if (Mathf.Abs(dirFwd.y) > 0.98f) { dirFwd = vt.forward; }
            Quaternion path = Quaternion.LookRotation(dirFwd, Vector3.up);
            float roll = -Mathf.Atan2(vt.right.y, vt.up.y) * Mathf.Rad2Deg;
            Quaternion formation = path * Quaternion.Euler(0f, 0f, -roll * BankFollow);

            if (_dFresh[i])
            {
                _dFresh[i] = false;
                _dPos[i] = vp + path * DroneStartOffset;
                _dRot[i] = path;
                _dRelVel[i] = Vector3.zero;
            }

            // 자리: 포드 출구가 프로브 끝 + (위 HoseRise, 앞 HoseReach) × 호스 길이(진행 방향 프레임)
            float len = drone.GetHoseLength();
            Vector3 probe = sup.ProbeTip ? sup.ProbeTip.position : vt.TransformPoint(new Vector3(0.6f, 0.6f, 6f));
            Vector3 baseOffset = _dRot[i] * Vector3.Scale(drone.GetHoseBaseLocal(), drone.transform.lossyScale);
            Vector3 station = probe + path * new Vector3(0f, len * HoseRise, len * HoseReach) - baseOffset;

            // 드론은 최저 속도(MinSpeedKt)보다 느리게 날 수 없다 — 기체가 그보다 느리면(착륙·저속) 진행 방향으로 최저 속도를 지켜 자리를 지나쳐 버린다
            float minMs = MinSpeedKt > 0f ? MinSpeedKt / 1.9438445f : 0f;
            Vector3 fly = vel.sqrMagnitude < minMs * minMs ? dirFwd * minMs : vel;
            bool slow = TooSlow(i, sup, vel, minMs);

            Vector3 pos = _dPos[i] + fly * dt;   // 편대: 기체 속도만큼 같이 간다(최저 속도 아래면 최저 속도로)
            Quaternion want = formation;
            if (state == T_APPROACH)
            {
                Vector3 to = station - pos;
                float dist = to.magnitude;
                Vector3 desired = dist > 0.01f ? to * (Mathf.Min(ApproachSpeed, dist * ApproachGain) / dist) : Vector3.zero;
                _dRelVel[i] = Vector3.Lerp(_dRelVel[i], desired, 1f - Mathf.Exp(-ApproachAccel * dt));
                pos += _dRelVel[i] * dt;
                Vector3 v = fly + _dRelVel[i];
                if (v.sqrMagnitude > 1f) { want = Quaternion.Slerp(Quaternion.LookRotation(v, Vector3.up), formation, Mathf.Clamp01(1f - dist / 200f)); }
                bool passed = Vector3.Dot(to, dirFwd) < 0f;   // 자리를 앞으로 지나쳤다
                if (!slow && dist < ArriveDistance) { _dRelVel[i] = Vector3.zero; SetState(i, T_WAIT); }
                else if (slow && (dist < ArriveDistance || passed))
                {
                    // 정렬하러 왔는데 기체가 너무 느리다: 오버슈트 — 호출한 사람이 실패로 돌려보낸다(이탈은 지금 속도 그대로 앞·위로)
                    if (_dLocal[i] && !_dResult[i]) { Abort(i, SlowReason()); }
                    else if (!_dLocal[i] && t > ApproachTimeout) { BeginRetract(i); }
                }
                else if (t > ApproachTimeout) { if (_dLocal[i] && !_dResult[i]) { Abort(i, "CANNOT JOIN"); } else if (!_dLocal[i]) { BeginRetract(i); } }
            }
            else if (state == T_LEAVE)
            {
                _dRelVel[i] += (path * new Vector3(0f, 0.25f, 1f)).normalized * (LeaveAccel * dt);
                pos += _dRelVel[i] * dt;
                want = Quaternion.LookRotation(fly + _dRelVel[i], Vector3.up);
                if (t >= LeaveTime) { Finish(i); return; }
            }
            else
            {
                float sharp = state >= T_CONNECT ? ContactSharpness : FollowSharpness;
                pos = Vector3.Lerp(pos, station, 1f - Mathf.Exp(-sharp * dt));
            }
            _dPos[i] = pos;
            _dRot[i] = Quaternion.Slerp(_dRot[i], want, 1f - Mathf.Exp(-TurnSharpness * dt));
            drone.transform.SetPositionAndRotation(pos, _dRot[i]);

            if (state == T_WAIT)
            {
                // 호출한 사람: 안정되면 호스를 풀자고 알린다. 불안정하면 HUD/MFD에 STEADY
                if (_dLocal[i] && !_dResult[i] && !_dExtendSent[i])
                {
                    // 자리에 온 뒤 최저 속도 아래로 떨어졌다: SlowGraceTime 안에 다시 빨라지지 않으면 드론이 지나쳐 버린다(실패)
                    _dSlow[i] = slow ? _dSlow[i] + dt : 0f;
                    if (_dSlow[i] >= SlowGraceTime) { Abort(i, SlowReason()); return; }
                    string why = BreakReason(sup, vt);
                    if (why != "") { _dSteady[i] = 0f; sup.ShowAlert("STEADY", "TANKER\n" + why); }
                    else { _dSteady[i] += dt; }
                    if (_dSteady[i] >= SteadyTime)
                    {
                        _dExtendSent[i] = true;
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetTankerExtend), _dSupport[i]);
                    }
                    else if (t > WaitTimeout) { Abort(i, "NOT STABLE"); }
                }
                else if (!_dLocal[i] && t > WaitTimeout + ResultTimeout) { BeginRetract(i); }
            }
            else if (state == T_EXTEND)
            {
                _dExtend[i] = Mathf.Clamp01(t / Mathf.Max(ExtendTime, 0.1f));
                if (t >= ExtendTime) { SetState(i, T_CONNECT); }
            }
            else if (state == T_CONNECT)
            {
                if (t >= ConnectTime)
                {
                    SetState(i, T_CONTACT);
                    if (_dLocal[i]) { Say(LINE_TANKER_CONTACT); }
                }
            }
            else if (state == T_CONTACT)
            {
                if (_dLocal[i] && !_dResult[i] && t >= ContactTime)
                {
                    _dResult[i] = true;
                    if (Progress) { Progress.UseCall(ACO_PlayerProgress.CALL_TANKER); Progress.StartCallCooldown(ACO_PlayerProgress.CALL_TANKER); }
                    Say(LINE_TANKER_DONE);
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetTankerDone), _dSupport[i]);
                }
                else if (!_dLocal[i] && t >= ContactTime + ResultTimeout) { BeginRetract(i); }
            }
            else if (state == T_RETRACT)
            {
                // 꽂혀 있었으면 먼저 DisconnectTime 동안 빠져 늘어진 자리로(호스는 다 풀린 채), 그 뒤 감는다
                float reel = t - _dUnplug[i];
                _dExtend[i] = _dExtendFrom[i] * (1f - Mathf.Clamp01(reel / Mathf.Max(RetractTime, 0.1f)));
                if (reel >= RetractTime) { SetProbe(sup, false); _dRelVel[i] = Vector3.zero; SetState(i, T_LEAVE); }
            }

            // 호스: 출구 → 프로브 앞 대기 자리까지 호스 선을 따라 곧게 풀리고(EXTEND), 대기 자리에서 프로브로 꽂힌다(CONNECT).
            // 뺄 때는 거꾸로 — 프로브에서 대기 자리로 물러난 뒤(DisconnectTime) 같은 선으로 감긴다. 아래로 늘어졌다가 올라오는 일이 없다.
            Vector3 exit = drone.GetHoseBase();
            Vector3 nose = vt.forward;
            Vector3 pre = PreContactPoint(probe, exit, nose);
            Vector3 head = exit;
            float now = Time.time;
            if (_dState[i] == T_EXTEND) { head = Vector3.Lerp(exit, pre, Smooth(_dExtend[i])); }
            else if (_dState[i] == T_CONNECT) { head = Vector3.Lerp(pre, probe, Smooth((now - _dT[i]) / Mathf.Max(ConnectTime, 0.1f))); }
            else if (_dState[i] == T_CONTACT) { head = probe; }
            else if (_dState[i] == T_RETRACT)
            {
                float since = now - _dT[i];
                if (_dUnplug[i] > 0f && since < _dUnplug[i])
                {
                    // 빠지는 중: 꽂혀 있던 자리에서 대기 자리로
                    head = Vector3.Lerp(vt.TransformPoint(_dHeadFrom[i]), pre, Smooth(since / _dUnplug[i]));
                }
                else
                {
                    // 감기: 대기 자리(풀다 만 경우엔 그때 드로그 자리)에서 출구로 같은 선을 따라
                    Vector3 from = _dUnplug[i] > 0f ? pre : vt.TransformPoint(_dHeadFrom[i]);
                    head = Vector3.Lerp(from, exit, Smooth((since - _dUnplug[i]) / Mathf.Max(RetractTime, 0.1f)));
                }
            }
            // 호스는 드로그 쪽에서 기수 방향과 나란히(바스켓이 뒤를 보고 프로브가 축으로 들어간다). 감겨 있을 때(접근·대기·이탈)는 드론 진행 방향
            bool deployed = _dState[i] >= T_EXTEND && _dState[i] <= T_RETRACT;
            drone.PoseHose(head, deployed ? nose : drone.transform.forward);
            // 기체 기준으로 기억한다 — 호출한 사람 화면에선 완료 신호가 이 프레임 드론을 놓은 뒤(위 상태 처리 중)에 BeginRetract를 불러,
            // 월드 좌표로 두면 지난 프레임 자리를 이번 프레임 드론 기준으로 바꿔 한 프레임 비행 거리(고속이면 수 m)만큼 드로그가 튀었다
            _dHeadNow[i] = vt.InverseTransformPoint(head);

            // 이탈 판정(호출한 사람만, 호스를 풀기 시작한 뒤부터 완료 전까지)
            if (_dLocal[i] && !_dResult[i] && _dState[i] >= T_EXTEND && _dState[i] <= T_CONTACT)
            {
                string why = BreakReason(sup, vt);
                if (why != "") { Abort(i, why); }
            }
        }

        /// 호출한 사람: 급유를 끊는다(why = HUD에 띄울 이유, ""면 경고 없이 — 내렸을 때 등).
        private void Abort(int i, string why)
        {
            ACO_DFUNC_Support sup = SupportAt(_dSupport[i]);
            if (sup != null && why != "") { sup.ShowAlert("BRK", "BREAKAWAY\n" + why); }
            _dResult[i] = true;
            if (Progress) { Progress.StartCallCooldownSeconds(ACO_PlayerProgress.CALL_TANKER, TankerFailCooldown); }   // 실패는 짧게
            Say(LINE_TANKER_BREAK);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetTankerAbort), _dSupport[i]);
        }

        /// 기체가 드론 최저 속도(MinSpeedKt)보다 느린가. 호출한 사람은 대기 속도(HUD와 같은 값), 다른 화면은 위치 차로 잰 속도.
        private bool TooSlow(int i, ACO_DFUNC_Support sup, Vector3 vel, float minMs)
        {
            if (minMs <= 0f) { return false; }
            if (_dLocal[i] && sup.AirVehicle != null) { return sup.AirVehicle.AirSpeed < minMs; }
            return vel.sqrMagnitude < minMs * minMs;
        }

        private string SlowReason()
        {
            return "TOO SLOW - MIN " + Mathf.RoundToInt(MinSpeedKt) + "KT";
        }

        /// 급유 조건이 안 맞는 이유(HUD에 그대로 — 맞으면 ""). 뒤집힘 → 뱅크 → 피치 → 하중 → 애프터버너 → 속도 순으로 하나만.
        private string BreakReason(ACO_DFUNC_Support sup, Transform vt)
        {
            if (!sup.IsPilot()) { return "PILOT OUT"; }
            if (vt.up.y < 0f) { return "ROLL UPRIGHT"; }
            float bank = Mathf.Abs(Mathf.Asin(Mathf.Clamp(vt.right.y, -1f, 1f)) * Mathf.Rad2Deg);
            if (bank > MaxBank) { return "BANK UNDER " + Mathf.RoundToInt(MaxBank); }
            if (MaxPitch > 0f)
            {
                float pitch = Mathf.Abs(Mathf.Asin(Mathf.Clamp(vt.forward.y, -1f, 1f)) * Mathf.Rad2Deg);
                if (pitch > MaxPitch) { return "PITCH WITHIN " + Mathf.RoundToInt(MaxPitch); }
            }
            SaccAirVehicle air = sup.AirVehicle;
            if (air != null)
            {
                float g = air.VertGs;
                if (g < MinG || g > MaxG) { return "KEEP G " + MinG.ToString("0.0") + "-" + MaxG.ToString("0.0"); }
                if (air.AfterburnerOn) { return "AFTERBURNER OFF"; }
                float kt = air.AirSpeed * 1.9438445f;
                if (MinSpeedKt > 0f && kt < MinSpeedKt) { return "SPEED UP - MIN " + Mathf.RoundToInt(MinSpeedKt) + "KT"; }
                if (MaxSpeedKt > 0f && kt > MaxSpeedKt) { return "SLOW DOWN - MAX " + Mathf.RoundToInt(MaxSpeedKt) + "KT"; }
            }
            return "";
        }

        /// 드로그 대기 자리: 프로브 끝에서 기수 방향으로 PreContactGap(출구까지 앞뒤 거리의 절반을 넘지 않게).
        private Vector3 PreContactPoint(Vector3 probe, Vector3 exit, Vector3 nose)
        {
            float reach = Vector3.Dot(exit - probe, nose);
            return probe + nose * Mathf.Clamp(PreContactGap, 0f, Mathf.Max(reach * 0.5f, 0f));
        }

        private void BeginRetract(int i)
        {
            // 꽂으러 가거나 꽂혀 있었으면 그 자리에서 빠져 늘어진 자리로 간 뒤 감는다(바로 감으면 드로그가 늘어진 자리로 튀었다). 호스를 풀기 전이면 바로 이탈
            bool plugged = _dState[i] == T_CONNECT || _dState[i] == T_CONTACT;
            _dUnplug[i] = plugged ? Mathf.Max(DisconnectTime, 0.05f) : 0f;
            // 빠지기(꽂혀 있었으면)나 감기(풀다 말았으면)를 시작할 드로그 자리
            _dHeadFrom[i] = _dHeadNow[i];
            if (_dState[i] <= T_WAIT) { _dExtend[i] = 0f; }
            _dExtendFrom[i] = plugged ? 1f : Mathf.Clamp01(_dExtend[i]);
            SetState(i, T_RETRACT);
            if (_dExtend[i] <= 0f) { _dT[i] = Time.time - RetractTime; }
        }

        private void Finish(int i)
        {
            ACO_DFUNC_Support sup = SupportAt(_dSupport[i]);
            if (sup != null && _dState[i] >= T_EXTEND && _dState[i] <= T_RETRACT) { SetProbe(sup, false); }
            // 호출한 사람: 결과 없이 끝났으면(기체가 사라짐 등) 쿨타임만
            if (_dLocal[i] && !_dResult[i] && Progress) { Progress.StartCallCooldownSeconds(ACO_PlayerProgress.CALL_TANKER, TankerFailCooldown); }
            _dState[i] = T_IDLE;
            _dSupport[i] = -1;
            if (Drones[i]) { Drones[i].SetShown(false); }
        }

        private void SetProbe(ACO_DFUNC_Support sup, bool on)
        {
            if (sup == null || sup.AirVehicle == null) { return; }
            Animator a = sup.AirVehicle.VehicleAnimator;
            if (a != null && !string.IsNullOrEmpty(sup.ProbeBool)) { a.SetBool(sup.ProbeBool, on); }
        }

        private int DroneOf(int support)
        {
            if (support < 0 || _dSupport == null) { return -1; }
            for (int i = 0; i < _dSupport.Length; i++) { if (_dSupport[i] == support && _dState[i] != T_IDLE) { return i; } }
            return -1;
        }

        private int FreeDrone()
        {
            if (_dState == null) { return -1; }
            for (int i = 0; i < _dState.Length; i++) { if (_dState[i] == T_IDLE && Drones[i]) { return i; } }
            return -1;
        }

        private ACO_DFUNC_Support SupportAt(int support)
        {
            return Supports != null && support >= 0 && support < Supports.Length ? Supports[support] : null;
        }

        private float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private void Say(int line)
        {
            if (!Chatter) { return; }
            string text = LineTexts != null && line < LineTexts.Length ? LineTexts[line] : "";
            AudioClip clip = LineClips != null && line < LineClips.Length ? LineClips[line] : null;
            if (text == "" && clip == null) { return; }
            Chatter.SayControl(text, clip, LineMaxDelay);
        }
    }
}
