using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;

namespace ACO
{
    /// 미션 진행 관리 — 페이즈 방식. 미션은 1~5개 페이즈로 이루어지고, 각 페이즈의 종류는 페이즈 모듈(Modules — ACO_PhaseModule 상속, 이 오브젝트 자식) 중
    /// 가중치 무작위(직전 페이즈와 같은 종류는 안 나온다, 모듈 MinPhase, 보너스 전용 모듈은 빠진다).
    /// 페이즈 종류는 그 페이즈가 시작되는 순간에 고르고, 고르기 전에 모듈 IsFeasible로 배치가 가능한지(풀 여유, 근처 지면/바다, 항로) 확인한다 —
    /// 안 되는 종류는 빼고 다른 종류를 고른다. 확인을 통과했는데 실제 배치가 실패하면(모듈 RESULT_REPLACE) 그 종류를 막은 채 다른 종류로 같은 페이즈를 다시 시작한다.
    /// 배치·판정·글자·무전 대사는 모듈이 하고, 관리자는 선택·점수 배율·웨이브·보너스·제한 시간·미션 끝을 맡는다(모듈 API는 ACO_PhaseModule 설명).
    /// 페이즈 목표는 생성기가 배치하고 태그를 단다(GetActiveObjectiveTag와 같은 태그 = 지금 목표).
    ///
    /// 점수: 격추 점수와 페이즈 보너스에 그 페이즈의 배율(PhaseScoreMultiplier, 뒤 페이즈일수록 높다)을 곱한다.
    ///   격추는 "이번 배치(serial)에서 살아 있는 걸 본 뒤 파괴됨"일 때 한 번만 센다. 클리어하면 클리어 보너스 + 남은 시간 보너스.
    ///   랭크 기준은 페이즈당 기준값 x 고른 페이즈들의 배율 합(페이즈 수가 달라도 공평하게).
    /// 실패: 제한 시간(TimeLimitPerPhase x 페이즈 수 + 호위 이동 시간) 초과, 또는 모듈이 실패를 돌려줌(FAIL_MODULE — 글자는 그 모듈) — 미션이 바로 끝난다.
    /// 보너스 페이즈: 마지막 페이즈를 클리어하면 BonusPhaseChance 확률로 보너스 전용 모듈(BonusOnly) 중 하나가 붙는다(PickBonusType).
    ///   모듈 BonusTimeLimit 안에 못 끝내면 물러남 — 보너스 없이 미션 클리어(미션 제한 시간은 보지 않는다). 랭크 기준과 시간 보너스는 본 미션(보너스 전)으로 센다.
    /// 증원 웨이브: 페이즈 시작(2페이즈부터, 모듈 WaveOnStart) + 교착(적 전력이 StallStrengthRatio 이하이고 StallTime 동안 진전 없음).
    ///
    /// 판단은 오너만 1초마다 한다. 상태는 동기화하고, 바뀔 때마다 모든 클라이언트에서 EventListeners에 이벤트를 보낸다:
    ///   ACO_OnMissionStart, ACO_OnPhaseStart, ACO_OnObjectiveDestroyed, ACO_OnPhaseComplete, ACO_OnWaveIncoming,
    ///   ACO_OnScoreChanged, ACO_OnMissionComplete, ACO_OnMissionFailed
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_MissionManager : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 생성기를 자동으로 넣는다")]
        public ACO_MissionGenerator Generator;
        [Tooltip("미션 이벤트를 받을 스크립트(UI, 무전 음성, 점수 등). 이벤트 이름은 클래스 설명 참고")]
        public UdonSharpBehaviour[] EventListeners;
        [Tooltip("페이즈 모듈(ACO_PhaseModule 상속 — 새 페이즈 종류). 종류 번호 = PHASE_MODULE_BASE + 순서. Bake가 이 오브젝트 아래(자식)의 모듈을 모은다")]
        public ACO_PhaseModule[] Modules;

        [Header("실행")]
        [Tooltip("이 오브젝트를 Interact(클릭)하면 DefaultPhaseCount로 미션을 시작한다(콜라이더 필요). 페이즈 수를 고르려면 ACO_MissionStartButton")]
        public bool AllowInteract = false;
        [Tooltip("판단 주기(초)")]
        public float PollInterval = 1f;

        [Header("페이즈")]
        [Tooltip("페이즈 수를 정하지 않고 시작했을 때(생성기 클릭, 이 오브젝트 클릭)의 페이즈 수")]
        [Range(1, 5)] public int DefaultPhaseCount = 3;
        [Tooltip("페이즈별 점수 배율 [1페이즈, 2, 3, 4, 5]. 격추 점수와 페이즈 보너스에 곱한다")]
        public float[] PhaseScoreMultiplier = new float[] { 1f, 1.25f, 1.5f, 1.75f, 2f };
        [Tooltip("페이즈 목표 자리에서 이 거리(m) 안에 지형 장식(도시/마을/비행장 등)이 있으면 그 이름을 작전 지역으로 배너에 띄운다")]
        public float SiteNameRadius = 3000f;
        [Tooltip("페이즈 클리어 보너스(배율 곱함)")]
        public int PhaseClearBonus = 500;
        [Tooltip("미션 시작 후 첫 페이즈까지(초) — 배경 병력 배치가 동기화될 시간")]
        public float FirstPhaseDelay = 5f;
        [Tooltip("페이즈 클리어 후 다음 페이즈까지(초)")]
        public float PhaseTransitionDelay = 10f;
        [Tooltip("목표 배치가 이 시간(초) 안에 안 보이면(풀 부족, 자리 없음) 모듈이 다른 종류로 바꾸게 한다(시한 표적의 두 번째 이후 표적은 건너뛴다)")]
        public float DeployTimeout = 20f;
        [Tooltip("배치 후 이 시간(초)이 지난 목표는 살아 있는 걸 못 봤어도 파괴되면 달성으로 친다(오너가 바뀐 직후 등)")]
        public float ObjectiveSettleTime = 10f;
        [Tooltip("목표 지상 시설(타격/방공망/사냥터)을 놓을 거리(m) — 페이즈 시작 순간 플레이어 평균 위치 기준")]
        public float TargetDistanceMin = 12000f;
        public float TargetDistanceMax = 30000f;
        [Tooltip("목표 편대(제공권/보스/할당량/보너스)가 나타날 거리(m) — 플레이어 평균 위치 기준")]
        public float FlightDistanceMin = 15000f;
        public float FlightDistanceMax = 25000f;
        [Tooltip("호위 계열 성공 후 이 시간(초) 뒤에 호위 대상을 거둔다 — 그동안 투하 후 계속 비행/활주로 정지/정차 연출이 보인다(모듈 ScheduleModuleRelease)")]
        public float EscortLingerTime = 30f;

        [Header("보너스 페이즈 (보너스 전용 모듈 — BonusOnly)")]
        [Tooltip("마지막 페이즈를 클리어하면 이 확률로 보너스 페이즈가 붙는다. 0이면 안 나온다")]
        [Range(0f, 1f)] public float BonusPhaseChance = 0.3f;
        [Tooltip("이 페이즈 수 이상인 미션에서만 보너스가 붙는다")]
        [Range(1, 5)] public int BonusMinPhases = 2;
        [Tooltip("보너스 페이즈의 점수 배율(격추 점수·보너스)")]
        public float BonusScoreMultiplier = 2.5f;

        [Header("실패 조건")]
        [Tooltip("페이즈당 제한 시간(초). 전체 제한 = 이 값 x 페이즈 수 + 호위 이동 시간(아래). 0이면 제한 없음")]
        public float TimeLimitPerPhase = 420f;
        [Tooltip("모듈 GrantTravelTime(호위·지상 호위·상륙전)이 켜진 페이즈가 시작되면 지켜야 하는 대상이 목적지까지 가는 데 걸리는 시간 + 이 여유(초)만큼 제한 시간을 늘린다. "
            + "음수면 늘리지 않는다")]
        public float EscortTimeMargin = 15f;

        [Header("팀 점수")]
        [Tooltip("전투기 격추 점수 [신병, 베테랑, 에이스]")]
        public int[] ScoreFighterByTier = new int[] { 100, 200, 500 };
        [Tooltip("지상 유닛에 ScoreValue 필드가 없을 때의 점수")]
        public int ScoreGroundFallback = 100;
        [Tooltip("고가치 표적에 ScoreValue 필드가 없을 때의 점수")]
        public int ScoreObjectiveFallback = 500;
        [Tooltip("적 폭격기(호위 대상 풀의 Hostile) 격추 점수(배율 곱하기 전)")]
        public int BomberKillScore = 400;
        [Tooltip("클리어 보너스")]
        public int ClearBonus = 1000;
        [Tooltip("클리어 시 남은 시간 1초당 보너스(제한 시간이 있을 때)")]
        public int TimeBonusPerSecond = 5;
        [Tooltip("랭크 기준(페이즈당). 실제 기준 = 이 값 x 고른 페이즈들의 배율 합. 이상이면 S / A / B, 그 아래 C, 실패 F")]
        public int RankSPerPhase = 2500;
        public int RankAPerPhase = 1600;
        public int RankBPerPhase = 800;

        [Header("웨이브 조건")]
        [Tooltip("2페이즈부터 페이즈가 시작될 때 목표 쪽으로 증원을 보낸다")]
        public bool WaveOnPhaseStart = true;
        [Tooltip("교착: 적 전력이 이 비율 이하로 떨어졌는데")]
        [Range(0f, 1f)] public float StallStrengthRatio = 0.5f;
        [Tooltip("교착: 이 시간(초) 동안 목표 진전이 없으면 증원")]
        public float StallTime = 180f;

        [Header("웨이브 공통")]
        [Tooltip("웨이브 사이 최소 간격(초)")]
        public float WaveCooldown = 90f;
        [Tooltip("미션 하나에서 보낼 최대 웨이브 수")]
        public int MaxWaves = 8;

        [Header("웨이브 규모 (자동) — 웨이브 번호 n은 1부터")]
        [Tooltip("지상 유닛 = Base + PerWave x (n-1)")]
        public int WaveGroundBase = 4;
        public int WaveGroundPerWave = 1;
        public int WaveGroundMax = 8;
        [Tooltip("전투기 = Base + PerWave x (n-1) + PerExtraPlayer x (접속 인원-1)")]
        public int WaveFighterBase = 2;
        public int WaveFighterPerWave = 1;
        public int WaveFighterPerExtraPlayer = 1;
        public int WaveFighterMax = 8;

        [Header("웨이브 숙련도 (자동) — 웨이브가 거듭될수록 강해진다")]
        public float WaveVeteranChanceBase = 0.3f;
        public float WaveVeteranChancePerWave = 0.08f;
        public float WaveAceChanceBase = 0f;
        public float WaveAceChancePerWave = 0.06f;
        [Tooltip("에이스 확률 상한")]
        [Range(0f, 1f)] public float WaveAceChanceMax = 0.5f;

        [Header("웨이브 수동 정의 (비우면 자동, 목록이 끝나면 자동으로 이어감)")]
        [Tooltip("웨이브 n의 지상 유닛 수")]
        public int[] ManualWaveGround;
        [Tooltip("웨이브 n의 전투기 수")]
        public int[] ManualWaveFighters;
        [Tooltip("웨이브 n의 에이스 확률(음수면 자동)")]
        public float[] ManualWaveAceChance;

        public const int STATE_IDLE = 0;
        public const int STATE_RUNNING = 1;
        public const int STATE_COMPLETE = 2;
        public const int STATE_FAILED = 3;

        public const int RANK_S = 0;
        public const int RANK_A = 1;
        public const int RANK_B = 2;
        public const int RANK_C = 3;
        public const int RANK_F = 4;

        /// 웨이브 이유: 페이즈 시작 / 교착
        public const int REASON_PHASE = 1;
        public const int REASON_STALL = 2;

        public const int MAX_PHASES = 5;
        public const int PHASE_NONE = 0;
        public const int PHASE_MODULE_BASE = 20;   // 페이즈 종류 = 20 + Modules 순서(0 = 없음)
        private const int MAX_MODULES = 30;        // 막은 종류 비트(_phaseBlocked)가 int 하나라서

        // 실패 사유(GetFailReason)
        public const int FAIL_TIME = 0;              // 제한 시간
        public const int FAIL_MODULE = 5;            // 모듈 페이즈 실패(글자는 그 모듈의 GetFailText)

        [UdonSynced] private int _state;
        [UdonSynced] private int _missionStartMs;
        [UdonSynced] private int _endMs;
        [UdonSynced] private int _lastProgressMs;   // 마지막 목표 진전 시각(교착 판단 기준)
        [UdonSynced] private int _lastWaveMs;
        [UdonSynced] private int _wavesSent;
        [UdonSynced] private int _initialStrength;
        // 추적을 시작한 생성기 전체 배치 번호. 동기화해서 오너가 바뀌어도 진행 중인 미션을 새 미션으로 착각하지 않게 한다.
        [UdonSynced] private int _trackedFullDeploy;
        [UdonSynced] private int _teamScore;
        [UdonSynced] private int _killFighters;
        [UdonSynced] private int _killGround;
        [UdonSynced] private int _clearBonusAwarded;
        [UdonSynced] private int _timeBonusAwarded;
        [UdonSynced] private int _phaseBonusTotal;
        [UdonSynced] private int _rank = -1;

        // 페이즈
        [UdonSynced] private int _phaseCount;
        [UdonSynced] private int[] _phaseTypes = new int[MAX_PHASES + 1];   // + 보너스 페이즈 한 칸
        [UdonSynced] private int _bonusIndex = -1;       // 보너스 페이즈 번호(-1 = 없음)
        [UdonSynced] private int _bonusType = PHASE_NONE;    // 이번 보너스 페이즈 종류(보너스 전용 모듈)
        [UdonSynced] private bool _bonusRolled;          // 이번 미션에서 보너스를 이미 굴렸다
        [UdonSynced] private int _mainClearMs;           // 본 미션을 클리어한 시각(보너스가 붙었을 때 시간 보너스 기준)
        [UdonSynced] private int _failReason;            // FAIL_*
        [UdonSynced] private int _phaseIndex = -1;       // 지금(또는 마지막) 페이즈, -1 = 아직
        [UdonSynced] private bool _phaseActive;          // 목표 진행 중(false면 페이즈 사이 대기)
        [UdonSynced] private int _phaseStartMs;
        [UdonSynced] private int _nextPhaseMs;
        [UdonSynced] private int _phasesCleared;
        [UdonSynced] private int _lastPhaseBonus;
        [UdonSynced] private Vector3 _phaseFocus;
        [UdonSynced] private int _activeTag;             // 지금 목표 유닛의 태그(0 = 없음)
        [UdonSynced] private int _objectivesDone;        // 지금 페이즈 진행(모듈이 정한다: 파괴 수 / 얻은 점수 / 살아 있는 수 등)
        [UdonSynced] private int _objectiveTotal;
        [UdonSynced] private int _objectiveEvents;       // 목표 파괴 누적(이벤트용)
        [UdonSynced] private int _phaseScoreStart;
        [UdonSynced] private bool _lastPhaseFailed;      // 마지막으로 끝난 페이즈가 실패(보너스 페이즈 물러남)였는지
        [UdonSynced] private int _phaseBlocked;          // 이번 페이즈에서 배치에 실패해 막은 종류(비트)
        [UdonSynced] private int _phaseAttempt;          // 이번 페이즈에서 종류를 바꾼 횟수(태그 구분용)
        [UdonSynced] private int _timeExtensionMs;       // 호위 이동 시간으로 늘어난 제한 시간(이번 미션 합)
        [UdonSynced] private bool _travelGranted;        // 이번 페이즈에 이동 시간을 이미 더했다
        [UdonSynced] private int _lingerTag;             // 도착한 호위 대상을 거둘 태그(0 = 없음)와 그 시각
        [UdonSynced] private int _lingerUntilMs;
        [UdonSynced] private int _phaseSite = -1;        // 이번 페이즈 목표 근처 지형 장식 자리(-1 = 없음, 모든 클라이언트가 같은 번호)

        // 오너 로컬: 다음 시작 요청의 페이즈 수(0 = 기본값)
        private int _pendingPhaseCount;
        // 오너 로컬: 격추를 한 번만 세기 위한 기록(카테고리별). aliveSerial = 살아 있는 걸 본 배치 번호, countedSerial = 점수를 준 배치 번호.
        private int[] _aliveSerialF = new int[0];
        private int[] _countedSerialF = new int[0];
        private int[] _aliveSerialG = new int[0];
        private int[] _countedSerialG = new int[0];
        private int[] _aliveSerialH = new int[0];
        private int[] _countedSerialH = new int[0];
        private int[] _aliveSerialP = new int[0];   // 함선 부위
        private int[] _countedSerialP = new int[0];
        private int[] _aliveSerialE = new int[0];   // 적 폭격기(호위 대상 풀의 Hostile)
        private int[] _countedSerialE = new int[0];
        // CountTagged 결과(U#은 out을 못 쓴다)
        private int _cntTotal;
        private int _cntDone;
        private Vector3 _cntAlivePos;
        private bool _cntHasAlive;

        // 로컬: 이벤트를 이미 보낸 상태(모든 클라이언트가 동기화 값과 비교해 한 번씩만 보낸다)
        private bool _seenAnyMission;
        private int _seenStartMs;
        private int _seenPhaseStarts;
        private int _seenPhasesCleared;
        private int _seenObjectives;
        private int _seenWaves;
        private bool _seenComplete;
        private bool _seenFailed;
        private int _seenScore;
        private int _joinMs;
        private bool _hasJoinMs;
        private float _pollTimer;

        void Start()
        {
            MarkJoin();
            DisableInteractive = !AllowInteract;
        }

        private void MarkJoin()
        {
            if (_hasJoinMs) { return; }
            _hasJoinMs = true;
            _joinMs = Networking.GetServerTimeInMilliseconds();
        }

        public override void Interact()
        {
            if (AllowInteract) { RequestStartMission(); }
        }

        // ───────────── 진입점 ─────────────

        /// 기본 페이즈 수(DefaultPhaseCount)로 미션 시작.
        public void RequestStartMission()
        {
            RequestStartMissionPhases(DefaultPhaseCount);
        }

        /// 페이즈 수(1~5)를 정해 미션 시작: 생성기에 배경 병력 배치를 시키고, 배치가 끝나면 페이즈를 진행한다.
        public void RequestStartMissionPhases(int phases)
        {
            phases = Mathf.Clamp(phases, 1, MAX_PHASES);
            if (Networking.IsOwner(gameObject)) { StartMission(phases); }
            else { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetStartMission), phases); }
        }

        [NetworkCallable]
        public void NetStartMission(int phases)
        {
            StartMission(phases);
        }

        public void StartMission(int phases)
        {
            if (!Networking.IsOwner(gameObject) || Generator == null) { return; }
            _pendingPhaseCount = Mathf.Clamp(phases, 1, MAX_PHASES);
            // 추적 시작은 Update가 생성기의 전체 배치 번호가 바뀐 걸 보고 한다(생성기 오너가 다른 클라이언트면 동기화를 기다린다).
            Generator.RequestStartMission();
            _pollTimer = 0f;
        }

        // ───────────── 판단 (오너) ─────────────

        void Update()
        {
            if (!Networking.IsOwner(gameObject) || Generator == null) { return; }

            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) { return; }
            _pollTimer = PollInterval;

            // 새 전체 배치가 생기면 새 미션으로 추적을 시작한다 — 관리자 요청뿐 아니라 생성기 자동 시작(GenerateOnStart)이나
            // 생성기 클릭으로 시작해도 페이즈/점수/제한 시간이 돈다.
            int fullDeploy = Generator.GetFullDeployCount();
            if (fullDeploy != _trackedFullDeploy)
            {
                _trackedFullDeploy = fullDeploy;
                BeginTracking();
                return;
            }

            UpdateLinger();
            if (_state == STATE_RUNNING) { Tick(); }
        }

        /// 도착한 호위 대상을 EscortLingerTime 뒤에 거둔다(미션이 끝난 뒤에도).
        private void UpdateLinger()
        {
            if (_lingerTag == 0) { return; }
            int now = Networking.GetServerTimeInMilliseconds();
            if (now - _lingerUntilMs < 0) { return; }
            Generator.RequestReleaseTag(_lingerTag);
            _lingerTag = 0;
            RequestSerialization();
        }

        /// 도착한 호위 대상(지금 태그)을 잠시 두었다가 거두도록 예약한다. 앞서 예약한 것이 있으면 바로 거둔다.
        private void ScheduleEscortRelease(int now)
        {
            if (_lingerTag != 0 && _lingerTag != _activeTag) { Generator.RequestReleaseTag(_lingerTag); }
            _lingerTag = _activeTag;
            _lingerUntilMs = now + Mathf.RoundToInt(Mathf.Max(EscortLingerTime, 0f) * 1000f);
        }

        private void BeginTracking()
        {
            int now = Networking.GetServerTimeInMilliseconds();
            _state = STATE_RUNNING;
            _missionStartMs = now;
            _lingerTag = 0; // 새 전체 배치가 호위 대상을 이미 모두 거뒀다
            _endMs = 0;
            _lastProgressMs = now;
            _lastWaveMs = now - Mathf.RoundToInt(WaveCooldown * 1000f); // 첫 웨이브는 쿨다운 없이 가능
            _wavesSent = 0;
            _initialStrength = Mathf.Max(Generator.CountDeployedEnemies(), 1);
            _teamScore = 0;
            _killFighters = 0;
            _killGround = 0;
            _clearBonusAwarded = 0;
            _timeBonusAwarded = 0;
            _phaseBonusTotal = 0;
            _rank = -1;
            _timeExtensionMs = 0;
            _travelGranted = false;

            _phaseCount = _pendingPhaseCount > 0 ? _pendingPhaseCount : Mathf.Clamp(DefaultPhaseCount, 1, MAX_PHASES);
            _pendingPhaseCount = 0;
            // 종류는 페이즈마다 시작 순간에 고른다(그때의 풀/플레이어 위치로 가능 여부를 확인하므로).
            _phaseTypes = new int[MAX_PHASES + 1];
            _bonusIndex = -1;
            _bonusType = PHASE_NONE;
            _bonusRolled = false;
            _mainClearMs = 0;
            _failReason = FAIL_TIME;
            _phaseBlocked = 0;
            _phaseAttempt = 0;
            _phaseIndex = -1;
            _phaseActive = false;
            _phasesCleared = 0;
            _lastPhaseBonus = 0;
            _activeTag = 0;
            _objectivesDone = 0;
            _objectiveTotal = 0;
            _objectiveEvents = 0;
            _lastPhaseFailed = false;
            _nextPhaseMs = now + Mathf.RoundToInt(FirstPhaseDelay * 1000f);

            ResetKillTracking();
            Publish();

            Debug.Log("[ACO] 미션 시작: " + _phaseCount + "페이즈, 적 전력 " + _initialStrength);
        }

        /// 페이즈 phaseIndex의 종류를 고른다: 모듈 가중치 무작위(직전 페이즈와 같은 종류 금지, 모듈 MinPhase부터, 보너스 전용 제외, blocked 비트는 제외).
        /// 뽑힌 종류가 지금 배치할 수 없으면(IsPhaseFeasible) 빼고 다시 뽑는다. 하나도 안 되면 PHASE_NONE.
        private int PickPhaseType(int phaseIndex, int prev, int blocked, Vector3 focus)
        {
            int rejected = blocked;
            int slots = SlotCount();
            for (int round = 0; round < slots; round++)
            {
                float total = 0f;
                for (int s = 0; s < slots; s++) { total += PickWeight(TypeAtSlot(s), phaseIndex, prev, rejected); }
                if (total <= 0f) { return PHASE_NONE; }
                float r = Random.value * total;
                int pick = PHASE_NONE;
                for (int s = 0; s < slots; s++)
                {
                    int t = TypeAtSlot(s);
                    float w = PickWeight(t, phaseIndex, prev, rejected);
                    if (w <= 0f) { continue; }
                    pick = t;
                    if (r < w) { break; }
                    r -= w;
                }
                if (pick == PHASE_NONE) { return PHASE_NONE; }
                if (IsPhaseFeasible(pick, phaseIndex, focus)) { return pick; }
                Debug.Log("[ACO] 페이즈 " + (phaseIndex + 1) + ": " + GetPhaseName(pick) + " 배치 불가 — 다른 종류로");
                rejected |= TypeBit(pick);
            }
            return PHASE_NONE;
        }

        // ───────────── 페이즈 모듈 ─────────────

        public bool IsModuleType(int type)
        {
            int k = type - PHASE_MODULE_BASE;
            return k >= 0 && Modules != null && k < Modules.Length && k < MAX_MODULES && Modules[k] != null;
        }

        /// 종류 번호의 모듈(모듈 페이즈가 아니면 null).
        public ACO_PhaseModule GetModule(int type) { return IsModuleType(type) ? Modules[type - PHASE_MODULE_BASE] : null; }

        /// 무작위 선택 후보 칸 = 모듈 순서.
        private int SlotCount() { return Modules != null ? Mathf.Min(Modules.Length, MAX_MODULES) : 0; }
        private int TypeAtSlot(int s) { return PHASE_MODULE_BASE + s; }
        /// 막은 종류 비트 = 모듈 순서 자리.
        private int TypeBit(int type) { return type >= PHASE_MODULE_BASE ? 1 << (type - PHASE_MODULE_BASE) : 0; }

        /// 모듈이 진행 값(완료/전체)을 넣는다 — 동기화되어 HUD·작전판이 읽는다. 값이 바뀌면 진행으로 친다(교착 웨이브 기준).
        /// countsDestroyed면 완료가 는 만큼 목표 파괴 이벤트(ACO_OnObjectiveDestroyed)를 센다 — 진행 값이 '파괴한 수'인 모듈만(상륙전 = 살아 있는 수라 false).
        public void SetModuleProgress(int done, int total, bool countsDestroyed)
        {
            if (done == _objectivesDone && total == _objectiveTotal) { return; }
            if (countsDestroyed && done > _objectivesDone) { _objectiveEvents += done - _objectivesDone; }
            _objectivesDone = done;
            _objectiveTotal = total;
            _lastProgressMs = Networking.GetServerTimeInMilliseconds();
            _cntChanged = true;
        }

        /// 모듈: 태그 tag인 배치 목표(전투기·지상·고가치 표적)를 센다 — 결과는 GetTagged*. 파괴 = 체력 0이고 살아 있는 걸 봤거나 배치 후 ObjectiveSettleTime이 지남.
        public void CountTaggedObjectives(int tag) { CountTagged(tag, Networking.GetServerTimeInMilliseconds()); }
        public int GetTaggedTotal() { return _cntTotal; }
        public int GetTaggedDone() { return _cntDone; }
        public bool HasTaggedAlive() { return _cntHasAlive; }
        public Vector3 GetTaggedAlivePosition() { return _cntAlivePos; }

        /// 모듈: 지금 페이즈의 하위 태그(시한 표적처럼 목표마다 다른 태그) — 기본 페이즈와 같은 규칙((페이즈 번호 + 1) × 100 + sub).
        public int MakePhaseTag(int sub) { return PhaseTag(_phaseIndex, sub); }

        /// 모듈: 지금 목표 태그를 바꾼다(표적 마커·항로 표시가 따라간다). 0 = 지금은 목표 없음.
        public void SetModuleActiveTag(int tag)
        {
            if (_activeTag == tag) { return; }
            _activeTag = tag;
            _cntChanged = true;
        }

        /// 모듈: 진행이 있었다(교착 웨이브 기준 시각을 지금으로) — 요격 편대를 보냈을 때 등.
        public void MarkModuleProgress()
        {
            _lastProgressMs = Networking.GetServerTimeInMilliseconds();
            _cntChanged = true;
        }

        public Vector3 GetPlayerCenter() { return PlayerCenter(); }
        /// 이번 페이즈를 시작할 때의 팀 점수(점수 할당량).
        public int GetPhaseScoreStart() { return _phaseScoreStart; }

        /// 모듈이 작전 지역(지형 장식 자리, -1 = 없음)을 넣는다 — 배너의 "AO: 이름".
        public void SetModuleSite(int site) { _phaseSite = site; }

        /// 모듈: 지금 태그의 호위 대상을 EscortLingerTime 뒤에 거둔다(도착한 채로 잠시 둔다). 성공을 돌려주기 전에 부른다.
        public void ScheduleModuleRelease() { ScheduleEscortRelease(Networking.GetServerTimeInMilliseconds()); }

        /// 배치 확인 시간(초) — 이 안에 목표가 안 보이면 모듈도 RESULT_REPLACE를 돌려준다.
        public float GetDeployTimeout() { return DeployTimeout; }
        public float GetObjectiveSettleTime() { return ObjectiveSettleTime; }
        public int GetPhaseStartMs() { return _phaseStartMs; }

        /// 지금 페이즈 작전 지역(지형 장식) 이름. 없으면 "".
        public string GetPhaseSiteName() { return _phaseSite >= 0 && Generator != null ? Generator.GetSiteName(_phaseSite) : ""; }

        /// 보너스 페이즈 종류를 고른다(오너): 보너스 전용 모듈(Enabled, MinPhase ≤ 본 미션 페이즈 수, 가중치 Weight) 중 가중치로 —
        /// 고른 뒤 IsFeasible로 확인하고, 안 되면 빼고 다시. 하나도 안 되면 PHASE_NONE(보너스 없음).
        private int PickBonusType(int mainPhases)
        {
            int n = Modules != null ? Mathf.Min(Modules.Length, MAX_MODULES) : 0;
            int rejected = 0;   // 비트 k = 모듈 k 제외
            Vector3 focus = PlayerCenter();
            for (int round = 0; round <= n; round++)
            {
                float total = 0f;
                for (int k = 0; k < n; k++) { total += BonusWeight(k, mainPhases, rejected); }
                if (total <= 0f) { return PHASE_NONE; }
                float r = Random.value * total;
                int pick = -1;
                for (int k = 0; k < n; k++)
                {
                    float w = BonusWeight(k, mainPhases, rejected);
                    if (w <= 0f) { continue; }
                    pick = k;
                    if (r < w) { break; }
                    r -= w;
                }
                if (pick < 0) { return PHASE_NONE; }
                if (Modules[pick].IsFeasible(mainPhases, focus)) { return PHASE_MODULE_BASE + pick; }
                Debug.Log("[ACO] 보너스 " + Modules[pick].GetName() + " 배치 불가 — 다른 보너스로");
                rejected |= 1 << pick;
            }
            return PHASE_NONE;
        }

        private float BonusWeight(int k, int mainPhases, int rejected)
        {
            ACO_PhaseModule m = Modules[k];
            if (m == null || !m.Enabled || !m.BonusOnly || (rejected & (1 << k)) != 0 || mainPhases < m.MinPhase) { return 0f; }
            return Mathf.Max(m.Weight, 0f);
        }

        /// 지금 페이즈가 모듈이면 끝을 알린다(성공·실패·교체·미션 끝).
        private void EndModulePhase()
        {
            ACO_PhaseModule m = GetModule(GetCurrentPhaseType());
            if (m != null) { m.End(); }
        }

        private bool UpdateModule(int now)
        {
            ACO_PhaseModule m = GetModule(GetCurrentPhaseType());
            bool bonus = _phaseIndex == _bonusIndex;
            int r = m.Tick(now);
            if (bonus && r == ACO_PhaseModule.RESULT_RUNNING && now - _phaseStartMs >= Mathf.RoundToInt(Mathf.Max(m.BonusTimeLimit, 1f) * 1000f))
            {
                r = ACO_PhaseModule.RESULT_FAIL;   // 제한 시간 — 물러남
            }
            if (bonus && r == ACO_PhaseModule.RESULT_FAIL)
            {
                Debug.Log("[ACO] 보너스 페이즈 " + m.GetName() + ": 시간 초과 — 물러남");
                if (_activeTag != 0) { Generator.RequestReleaseTag(_activeTag); }
                m.End();
                EndPhase(now, false, true, 0);
                return true;
            }
            if (bonus && r == ACO_PhaseModule.RESULT_REPLACE)
            {
                Debug.LogWarning("[ACO] 보너스 페이즈 " + m.GetName() + ": 배치되지 않음 — 건너뜀");
                m.End();
                EndPhase(now, true, false, 0);
                return true;
            }
            if (r == ACO_PhaseModule.RESULT_CLEAR)
            {
                int extra = Mathf.RoundToInt(m.GetClearBonus() * GetPhaseMultiplier(_phaseIndex));
                Debug.Log("[ACO] " + m.GetName() + " 성공, 보너스 " + extra);
                m.End();
                EndPhase(now, false, false, extra);
                return true;
            }
            if (r == ACO_PhaseModule.RESULT_FAIL)
            {
                Debug.Log("[ACO] " + m.GetName() + " 실패: " + m.GetFailText() + " — 미션 실패");
                _failReason = FAIL_MODULE;
                FinishMission(false, now);
                return true;
            }
            if (r == ACO_PhaseModule.RESULT_REPLACE) { return ReplacePhase(now, "모듈 배치 실패"); }
            return false;
        }

        private float PickWeight(int type, int phaseIndex, int prev, int rejected)
        {
            if ((rejected & TypeBit(type)) != 0) { return 0f; }
            return TypeWeight(type, phaseIndex, prev);
        }

        /// 지금 이 종류를 배치할 수 있는지(모듈 IsFeasible — 풀 여유, focus 근처 지면/바다, 항로). 찾은 자리·씨앗은 모듈이 들고 있다가 Begin에서 쓴다.
        private bool IsPhaseFeasible(int type, int phaseIndex, Vector3 focus)
        {
            if (Generator == null || !IsModuleType(type)) { return false; }
            return GetModule(type).IsFeasible(phaseIndex, focus);
        }

        private float TypeWeight(int type, int phaseIndex, int prev)
        {
            if (type == prev || !IsModuleType(type)) { return 0f; }
            ACO_PhaseModule m = GetModule(type);
            if (!m.Enabled || m.BonusOnly || phaseIndex + 1 < m.MinPhase) { return 0f; }
            return Mathf.Max(m.Weight, 0f);
        }

        private void Tick()
        {
            int now = Networking.GetServerTimeInMilliseconds();
            bool changed = UpdateKills();

            // 실패: 제한 시간 초과
            int limitMs = GetTimeLimitMs();
            if (_bonusIndex < 0 && limitMs > 0 && now - _missionStartMs >= limitMs)
            {
                FinishMission(false, now);
                Debug.Log("[ACO] 미션 실패(제한 시간): 팀 점수 " + _teamScore);
                return;
            }

            if (!_phaseActive)
            {
                if (_phaseIndex + 1 >= _phaseCount)
                {
                    // 마지막 페이즈 클리어는 ClearPhase가 바로 끝내므로 보통 오지 않는다(오너가 바뀌는 사이 등 안전장치).
                    FinishMission(true, now);
                    return;
                }
                if (now - _nextPhaseMs >= 0) { StartNextPhase(now); return; }
            }
            else
            {
                int type = GetCurrentPhaseType();
                if (!_travelGranted && GrantsTravelTime(type)) { GrantTravelTime(now); }
                bool ended;
                if (IsModuleType(type)) { ended = UpdateModule(now); }
                else
                {
                    // 모듈이 없어졌다(씬에서 지움 등) — 건너뛴다
                    Debug.LogWarning("[ACO] 페이즈 " + (_phaseIndex + 1) + ": 종류 " + type + "의 모듈이 없음 — 건너뜀");
                    ClearPhase(now, true);
                    ended = true;
                }
                if (ended) { return; }
                if (_cntChanged) { changed = true; _cntChanged = false; }
            }

            // 교착 웨이브
            bool cooldownOver = now - _lastWaveMs >= Mathf.RoundToInt(WaveCooldown * 1000f);
            bool wavesLeft = _wavesSent < MaxWaves;
            float strength = Generator.CountAliveEnemies() / (float)_initialStrength;
            bool stalled = now - _lastProgressMs >= Mathf.RoundToInt(StallTime * 1000f);
            if (_phaseActive && _phaseIndex != _bonusIndex && strength <= StallStrengthRatio && stalled && wavesLeft && cooldownOver)
            {
                SendWave(REASON_STALL, now);
                // 교착 웨이브도 "진행"으로 쳐서 StallTime마다 한 번씩만 나가게 한다.
                _lastProgressMs = now;
                return;
            }

            if (changed) { Publish(); }
        }

        private bool _cntChanged;

        // ───────────── 페이즈 (오너) ─────────────

        private void StartNextPhase(int now)
        {
            _phaseIndex++;
            _phaseActive = true;
            _phaseBlocked = 0;
            _phaseAttempt = 0;
            _phaseFocus = PlayerCenter();
            if (_phaseIndex == _bonusIndex)
            {
                BeginPhase(_bonusType, now, true);
                return;
            }
            int prev = _phaseIndex > 0 ? _phaseTypes[_phaseIndex - 1] : PHASE_NONE;
            int type = PickPhaseType(_phaseIndex, prev, 0, _phaseFocus);
            if (type == PHASE_NONE)
            {
                // 지금은 어떤 종류도 확인을 통과하지 못했다 — 가중치만으로 고르고, 배치가 실패하면 다른 종류로 바꾼다.
                Debug.LogWarning("[ACO] 페이즈 " + (_phaseIndex + 1) + ": 배치 가능한 종류가 없음(풀/지형) — 가중치로만 고름");
                type = PickFallbackType(_phaseIndex, prev, 0);
                if (type == PHASE_NONE)
                {
                    // 켜진 모듈이 없거나 가중치가 전부 0 — 이 페이즈는 건너뛴다
                    Debug.LogWarning("[ACO] 페이즈 " + (_phaseIndex + 1) + ": 고를 수 있는 페이즈 모듈이 없음(Modules·Weight 확인) — 건너뜀");
                    _phaseTypes[_phaseIndex] = PHASE_NONE;
                    ClearPhase(now, true);
                    return;
                }
            }
            BeginPhase(type, now, true);
        }

        /// 확인 없이 가중치로만 고른다(마지막 수단). 막힌 종류는 뺀다.
        private int PickFallbackType(int phaseIndex, int prev, int blocked)
        {
            int slots = SlotCount();
            float total = 0f;
            for (int s = 0; s < slots; s++) { total += PickWeight(TypeAtSlot(s), phaseIndex, prev, blocked); }
            if (total <= 0f) { return PHASE_NONE; }
            float r = Random.value * total;
            int pick = PHASE_NONE;
            for (int s = 0; s < slots; s++)
            {
                int t = TypeAtSlot(s);
                float w = PickWeight(t, phaseIndex, prev, blocked);
                if (w <= 0f) { continue; }
                pick = t;
                if (r < w) { break; }
                r -= w;
            }
            return pick;
        }

        /// 배치가 실패한 지금 종류를 막고 같은 페이즈를 다른 종류로 다시 시작한다. 바꿀 종류가 없으면 보너스 없이 넘어간다(마지막 수단).
        /// 반환: 항상 true(이번 틱의 페이즈 처리는 끝).
        private bool ReplacePhase(int now, string reason)
        {
            int failedType = GetCurrentPhaseType();
            EndModulePhase();
            _phaseBlocked |= TypeBit(failedType);
            _phaseAttempt++;
            if (_activeTag != 0) { Generator.RequestReleaseTag(_activeTag); }
            int prev = _phaseIndex > 0 ? _phaseTypes[_phaseIndex - 1] : PHASE_NONE;
            int type = PickPhaseType(_phaseIndex, prev, _phaseBlocked, _phaseFocus);
            if (type == PHASE_NONE) { type = PickFallbackType(_phaseIndex, prev, _phaseBlocked); }
            if (type == PHASE_NONE)
            {
                Debug.LogWarning("[ACO] 페이즈 " + (_phaseIndex + 1) + ": " + reason + " — 바꿀 종류가 없어 넘어감");
                ClearPhase(now, true);
                return true;
            }
            Debug.LogWarning("[ACO] 페이즈 " + (_phaseIndex + 1) + ": " + GetPhaseName(failedType) + " " + reason + " — " + GetPhaseName(type) + "(으)로 바꿈");
            BeginPhase(type, now, false);
            return true;
        }

        /// 페이즈를 type으로 시작한다(목표 배치). firstAttempt가 아니면(종류를 바꾼 재시작) 페이즈 시작 웨이브를 다시 보내지 않는다.
        private void BeginPhase(int type, int now, bool firstAttempt)
        {
            _phaseTypes[_phaseIndex] = type;
            _phaseStartMs = now;
            _lastProgressMs = now;
            _objectivesDone = 0;
            _objectiveTotal = 0;
            _phaseScoreStart = _teamScore;
            _travelGranted = false;

            // 종류를 바꿔 다시 시작해도 이전 시도의 유닛과 섞이지 않게 시도마다 다른 태그(하위 20+)를 쓴다(시한 표적은 MakePhaseTag로 1~).
            int tag = PhaseTag(_phaseIndex, 20 + _phaseAttempt);
            _activeTag = tag;
            _phaseSite = -1;
            // 배치·진행 값·작전 지역은 모듈이 정한다(SetModuleProgress / SetModuleSite)
            ACO_PhaseModule startModule = GetModule(type);
            startModule.Begin(_phaseIndex, tag, _phaseFocus, now);
            Debug.Log("[ACO] 페이즈 " + (_phaseIndex + 1) + "/" + _phaseCount + " 시작: " + GetPhaseName(type) + " (배율 x" + GetPhaseMultiplier(_phaseIndex) + ")");

            // 2페이즈부터는 목표 쪽으로 증원도 보낸다.
            bool cooldownOver = now - _lastWaveMs >= Mathf.RoundToInt(WaveCooldown * 1000f);
            if (firstAttempt && WaveOnPhaseStart && _phaseIndex > 0 && _wavesSent < MaxWaves && cooldownOver && startModule.WaveOnStart() && _phaseIndex != _bonusIndex)
            {
                SendWave(REASON_PHASE, now);
                return;
            }
            Publish();
        }

        /// 미션 실패 사유(FAIL_TIME = 제한 시간, FAIL_MODULE = 페이즈 모듈 실패 — 글자는 그 모듈).
        public int GetFailReason() { return _failReason; }

        /// 실패 사유 한 줄(영문 — 기본 TMP 폰트). long = 점수 HUD 배너용, 아니면 작전판용 짧은 말.
        public string GetFailReasonText(bool longText)
        {
            if (_failReason == FAIL_MODULE)
            {
                ACO_PhaseModule m = GetModule(GetCurrentPhaseType());
                return m != null ? m.GetFailText() : "MISSION FAILED";
            }
            return "TIME UP";
        }

        /// 페이즈가 실패로 끝났을 때(배너·작전판) 짧은 글자 — 그 모듈의 FailText.
        public string GetPhaseFailText(int type)
        {
            return IsModuleType(type) ? GetModule(type).GetFailText() : "FAILED";
        }

        private void SetProgress(int done, int total, int now)
        {
            if (done > _objectivesDone)
            {
                _objectiveEvents += done - _objectivesDone;
                _lastProgressMs = now;
            }
            if (done != _objectivesDone || total != _objectiveTotal) { _cntChanged = true; }
            _objectivesDone = done;
            _objectiveTotal = total;
        }

        /// 페이즈 종료(성공 또는 건너뜀).
        private void ClearPhase(int now, bool skipped)
        {
            EndPhase(now, skipped, false, 0);
        }

        /// 페이즈 종료: 보너스(배율 곱함 + extraBonus)를 더하고, 마지막 페이즈면 미션 클리어, 아니면 다음 페이즈를 예약한다.
        /// failed는 보너스 페이즈 물러남뿐이다 — 보너스 없이 미션 클리어. 실패 조건이 있는 페이즈의 실패는 FinishMission(false).
        private void EndPhase(int now, bool skipped, bool failed, int extraBonus)
        {
            float mult = GetPhaseMultiplier(_phaseIndex);
            int bonus = 0;
            _lastPhaseFailed = failed;
            if (!failed && !skipped)
            {
                bonus = Mathf.RoundToInt(PhaseClearBonus * mult) + extraBonus;
            }
            _teamScore += bonus;
            _phaseBonusTotal += bonus;
            _lastPhaseBonus = bonus;
            _phasesCleared++;
            _phaseActive = false;
            _activeTag = 0;
            _lastProgressMs = now;
            Debug.Log("[ACO] 페이즈 " + (_phaseIndex + 1) + " 종료: 보너스 " + bonus + (failed ? " (실패)" : (skipped ? " (건너뜀)" : "")));

            if (_phasesCleared >= _phaseCount)
            {
                // 본 미션 클리어 → 한 번만 보너스를 굴린다(풀에 쉬는 슈퍼에이스가 있어야).
                if (!_bonusRolled)
                {
                    _bonusRolled = true;
                    int bonusType = PHASE_NONE;
                    if (_phaseCount >= BonusMinPhases && _phaseCount <= MAX_PHASES && Random.value < BonusPhaseChance && Generator != null)
                    {
                        bonusType = PickBonusType(_phaseCount);
                    }
                    if (bonusType != PHASE_NONE)
                    {
                        _bonusType = bonusType;
                        _bonusIndex = _phaseCount;
                        _phaseCount++;
                        _mainClearMs = now;
                        if (_mainClearMs == 0) { _mainClearMs = 1; }
                        _nextPhaseMs = now + Mathf.RoundToInt(PhaseTransitionDelay * 1000f);
                        Debug.Log("[ACO] 보너스 페이즈가 붙었습니다: " + GetPhaseName(bonusType));
                        Publish();
                        return;
                    }
                }
                FinishMission(true, now);
                Debug.Log("[ACO] 미션 클리어: 팀 점수 " + _teamScore + ", 랭크 " + GetRankLetter());
                return;
            }
            _nextPhaseMs = now + Mathf.RoundToInt(PhaseTransitionDelay * 1000f);
            Publish();
        }

        /// 태그가 tag인 배치 유닛 수(_cntTotal)와 그중 파괴된 수(_cntDone), 살아 있는 하나의 위치(_cntAlivePos).
        /// 파괴 = 체력 0이고, 이번 배치에서 살아 있는 걸 봤거나 배치 후 ObjectiveSettleTime이 지남(부활 동기화 전 체력 0 오판 방지).
        private void CountTagged(int tag, int now)
        {
            _cntTotal = 0;
            _cntDone = 0;
            _cntHasAlive = false;
            if (tag == 0) { return; }
            CountTaggedIn(ACO_MissionGenerator.CAT_FIGHTER, _aliveSerialF, tag, now);
            CountTaggedIn(ACO_MissionGenerator.CAT_GROUND, _aliveSerialG, tag, now);
            CountTaggedIn(ACO_MissionGenerator.CAT_HVT, _aliveSerialH, tag, now);
        }

        private void CountTaggedIn(int category, int[] aliveSerial, int tag, int now)
        {
            int settleMs = Mathf.RoundToInt(ObjectiveSettleTime * 1000f);
            int n = Generator.GetUnitCount(category);
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(category, i) || Generator.GetUnitTag(category, i) != tag) { continue; }
                _cntTotal++;
                if (Generator.IsUnitDestroyed(category, i))
                {
                    bool seen = i < aliveSerial.Length && aliveSerial[i] == Generator.GetUnitSerial(category, i);
                    if (seen || now - Generator.GetUnitDeployTimeMs(category, i) >= settleMs) { _cntDone++; }
                }
                else if (!_cntHasAlive)
                {
                    _cntHasAlive = true;
                    _cntAlivePos = Generator.GetUnitPosition(category, i);
                }
            }
        }

        private int PhaseTag(int phaseIndex, int sub)
        {
            return (phaseIndex + 1) * 100 + sub;
        }

        /// 플레이어들의 평균 위치(목표 배치 기준점). 아무도 없으면 공역 중앙.
        private Vector3 PlayerCenter()
        {
            int count = VRCPlayerApi.GetPlayerCount();
            Vector2 c = (Generator.AreaMin + Generator.AreaMax) * 0.5f;
            if (count <= 0) { return new Vector3(c.x, 0f, c.y); }
            VRCPlayerApi[] players = new VRCPlayerApi[count];
            VRCPlayerApi.GetPlayers(players);
            Vector3 sum = Vector3.zero;
            int valid = 0;
            for (int i = 0; i < players.Length; i++)
            {
                if (!Utilities.IsValid(players[i])) { continue; }
                sum += players[i].GetPosition();
                valid++;
            }
            if (valid == 0) { return new Vector3(c.x, 0f, c.y); }
            return sum / valid;
        }

        // ───────────── 팀 점수 (오너) ─────────────

        /// 격추 기록을 새로 만든다. 이미 파괴된 유닛은 살아 있는 걸 못 봤으니 다시 세지 않는다.
        private void ResetKillTracking()
        {
            int f = Generator.GetUnitCount(ACO_MissionGenerator.CAT_FIGHTER);
            int g = Generator.GetUnitCount(ACO_MissionGenerator.CAT_GROUND);
            int h = Generator.GetUnitCount(ACO_MissionGenerator.CAT_HVT);
            int p = Generator.GetUnitCount(ACO_MissionGenerator.CAT_PART);
            _aliveSerialP = FilledArray(p);
            _countedSerialP = FilledArray(p);
            int e = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            _aliveSerialE = FilledArray(e);
            _countedSerialE = FilledArray(e);
            _aliveSerialF = FilledArray(f);
            _countedSerialF = FilledArray(f);
            _aliveSerialG = FilledArray(g);
            _countedSerialG = FilledArray(g);
            _aliveSerialH = FilledArray(h);
            _countedSerialH = FilledArray(h);
        }

        private int[] FilledArray(int count)
        {
            int[] a = new int[count];
            for (int i = 0; i < count; i++) { a[i] = -1; }
            return a;
        }

        /// 파괴된 유닛을 찾아 점수를 준다. 같은 배치 번호 동안 "살아 있는 걸 본 뒤 파괴됨"일 때 한 번만.
        /// 반환: 점수가 바뀌었으면 true.
        private bool UpdateKills()
        {
            // 풀 크기가 바뀌었거나 오너가 막 넘어왔으면 기록을 새로 만든다.
            if (_aliveSerialF.Length != Generator.GetUnitCount(ACO_MissionGenerator.CAT_FIGHTER)
                || _aliveSerialG.Length != Generator.GetUnitCount(ACO_MissionGenerator.CAT_GROUND)
                || _aliveSerialH.Length != Generator.GetUnitCount(ACO_MissionGenerator.CAT_HVT)
                || _aliveSerialP.Length != Generator.GetUnitCount(ACO_MissionGenerator.CAT_PART)
                || _aliveSerialE.Length != Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT))
            {
                ResetKillTracking();
            }

            int before = _teamScore;
            ScanCategory(ACO_MissionGenerator.CAT_FIGHTER, _aliveSerialF, _countedSerialF);
            ScanCategory(ACO_MissionGenerator.CAT_GROUND, _aliveSerialG, _countedSerialG);
            ScanCategory(ACO_MissionGenerator.CAT_HVT, _aliveSerialH, _countedSerialH);
            ScanCategory(ACO_MissionGenerator.CAT_PART, _aliveSerialP, _countedSerialP);
            ScanCategory(ACO_MissionGenerator.CAT_ESCORT, _aliveSerialE, _countedSerialE);
            return _teamScore != before;
        }

        private void ScanCategory(int category, int[] aliveSerial, int[] countedSerial)
        {
            for (int i = 0; i < aliveSerial.Length; i++)
            {
                if (!Generator.IsUnitUsed(category, i)) { continue; }
                // 호위 대상 풀은 적 폭격기만 격추로 센다(아군 수송기·콘보이가 격추되는 건 점수가 아니다).
                if (category == ACO_MissionGenerator.CAT_ESCORT)
                {
                    ACO_EscortUnit eu = Generator.GetEscort(i);
                    if (eu == null || !eu.Hostile) { continue; }
                }
                int serial = Generator.GetUnitSerial(category, i);

                if (!Generator.IsUnitDestroyed(category, i))
                {
                    aliveSerial[i] = serial;
                    continue;
                }
                if (aliveSerial[i] != serial || countedSerial[i] == serial) { continue; }

                countedSerial[i] = serial;
                _teamScore += GetKillScore(category, i);
                if (category == ACO_MissionGenerator.CAT_FIGHTER || category == ACO_MissionGenerator.CAT_ESCORT) { _killFighters++; }
                else if (category != ACO_MissionGenerator.CAT_HVT) { _killGround++; } // 지상 유닛, 함선 부위
            }
        }

        /// 유닛 하나를 격추했을 때의 점수(지금 페이즈 배율 포함). 미션 로그도 이 값을 써서 팀 점수와 맞춘다.
        public int GetKillScore(int category, int i)
        {
            if (Generator == null) { return 0; }
            int score;
            if (category == ACO_MissionGenerator.CAT_FIGHTER)
            {
                int tier = Mathf.Clamp(Generator.GetFighterTier(i), 0, 2);
                score = ScoreFighterByTier != null && tier < ScoreFighterByTier.Length ? ScoreFighterByTier[tier] : 100;
            }
            else if (category == ACO_MissionGenerator.CAT_HVT) { score = Generator.GetUnitScoreValue(category, i, ScoreObjectiveFallback); }
            else if (category == ACO_MissionGenerator.CAT_ESCORT) { score = BomberKillScore; }
            else { score = Generator.GetUnitScoreValue(category, i, ScoreGroundFallback); } // 지상 유닛, 함선 부위
            return Mathf.RoundToInt(score * GetScoreMultiplier());
        }

        /// 클리어/실패 확정. 클리어면 보너스를 더하고 랭크를 매긴다.
        private void FinishMission(bool success, int now)
        {
            if (_phaseActive) { EndModulePhase(); }
            UpdateKills();
            _endMs = now;
            _phaseActive = false;
            _activeTag = 0;

            if (success)
            {
                _clearBonusAwarded = ClearBonus;
                _timeBonusAwarded = 0;
                int limitMs = GetTimeLimitMs();
                int clearAt = _mainClearMs != 0 ? _mainClearMs : now;   // 보너스가 붙었으면 본 미션을 끝낸 시각
                if (limitMs > 0)
                {
                    float remaining = (limitMs - (clearAt - _missionStartMs)) * 0.001f;
                    _timeBonusAwarded = Mathf.Max(Mathf.FloorToInt(remaining), 0) * TimeBonusPerSecond;
                }
                _teamScore += _clearBonusAwarded + _timeBonusAwarded;
                _state = STATE_COMPLETE;
                float scale = 0f;
                for (int k = 0; k < _phaseCount; k++) { if (k != _bonusIndex) { scale += GetPhaseMultiplier(k); } }
                if (_teamScore >= RankSPerPhase * scale) { _rank = RANK_S; }
                else if (_teamScore >= RankAPerPhase * scale) { _rank = RANK_A; }
                else if (_teamScore >= RankBPerPhase * scale) { _rank = RANK_B; }
                else { _rank = RANK_C; }
            }
            else
            {
                _state = STATE_FAILED;
                _rank = RANK_F;
            }
            Publish();
        }

        private void SendWave(int reason, int now)
        {
            int n = _wavesSent + 1;
            int ground = WaveGround(n);
            int fighters = WaveFighters(n);
            float ace = WaveAce(n);
            float veteran = Mathf.Clamp01(WaveVeteranChanceBase + WaveVeteranChancePerWave * (n - 1));

            Vector3 focus = CurrentObjectivePosition(now);
            Generator.RequestReinforceAt(focus, ground, fighters, veteran, ace);

            _wavesSent = n;
            _lastWaveMs = now;
            Publish();
            Debug.Log("[ACO] 웨이브 " + n + " (" + (reason == REASON_PHASE ? "페이즈 시작" : "교착") + "): 지상 " + ground
                + ", 전투기 " + fighters + ", 베테랑 " + veteran.ToString("P0") + ", 에이스 " + ace.ToString("P0"));
        }

        /// 지금 목표 중 살아 있는 하나의 위치. 없으면(아직 배치 전, 할당량 등) 페이즈 기준점.
        private Vector3 CurrentObjectivePosition(int now)
        {
            CountTagged(_activeTag, now);
            if (_cntHasAlive) { return _cntAlivePos; }
            return _phaseFocus;
        }

        private int WaveGround(int n)
        {
            if (ManualWaveGround != null && n - 1 < ManualWaveGround.Length) { return ManualWaveGround[n - 1]; }
            return Mathf.Min(WaveGroundBase + WaveGroundPerWave * (n - 1), WaveGroundMax);
        }

        private int WaveFighters(int n)
        {
            if (ManualWaveFighters != null && n - 1 < ManualWaveFighters.Length) { return ManualWaveFighters[n - 1]; }
            int players = Mathf.Max(VRCPlayerApi.GetPlayerCount(), 1);
            int count = WaveFighterBase + WaveFighterPerWave * (n - 1) + WaveFighterPerExtraPlayer * (players - 1);
            return Mathf.Min(count, WaveFighterMax);
        }

        private float WaveAce(int n)
        {
            if (ManualWaveAceChance != null && n - 1 < ManualWaveAceChance.Length && ManualWaveAceChance[n - 1] >= 0f)
            {
                return ManualWaveAceChance[n - 1];
            }
            return Mathf.Min(WaveAceChanceBase + WaveAceChancePerWave * (n - 1), WaveAceChanceMax);
        }

        // ───────────── 동기화 / 이벤트 (모든 클라이언트) ─────────────

        private void Publish()
        {
            RequestSerialization();
            FireEvents();
        }

        public override void OnDeserialization()
        {
            MarkJoin();
            FireEvents();
        }

        /// 동기화 값이 지난번에 본 값보다 앞서 있으면 그만큼 이벤트를 보낸다(클라이언트마다 한 번씩).
        /// 이 클라이언트가 들어오기 전에 시작된 미션은 지난 이벤트를 몰아서 보내지 않고 상태만 맞춘다(무전 음성이 겹쳐 나오지 않게).
        private void FireEvents()
        {
            if (_state == STATE_IDLE) { return; }

            if (!_seenAnyMission || _missionStartMs != _seenStartMs)
            {
                bool startedBeforeJoin = !_seenAnyMission && _missionStartMs - _joinMs < 0;
                _seenAnyMission = true;
                _seenStartMs = _missionStartMs;
                _seenComplete = false;
                _seenFailed = false;

                if (startedBeforeJoin)
                {
                    _seenPhaseStarts = _phaseIndex + 1;
                    _seenPhasesCleared = _phasesCleared;
                    _seenObjectives = _objectiveEvents;
                    _seenWaves = _wavesSent;
                    _seenScore = _teamScore;
                    _seenComplete = _state == STATE_COMPLETE;
                    _seenFailed = _state == STATE_FAILED;
                    return;
                }

                _seenPhaseStarts = 0;
                _seenPhasesCleared = 0;
                _seenObjectives = 0;
                _seenWaves = 0;
                _seenScore = 0;
                Broadcast("ACO_OnMissionStart");
            }

            while (_seenPhaseStarts < _phaseIndex + 1)
            {
                _seenPhaseStarts++;
                Broadcast("ACO_OnPhaseStart");
            }
            while (_seenObjectives < _objectiveEvents)
            {
                _seenObjectives++;
                Broadcast("ACO_OnObjectiveDestroyed");
            }
            while (_seenPhasesCleared < _phasesCleared)
            {
                _seenPhasesCleared++;
                Broadcast("ACO_OnPhaseComplete");
            }
            while (_seenWaves < _wavesSent)
            {
                _seenWaves++;
                Broadcast("ACO_OnWaveIncoming");
            }

            if (_teamScore != _seenScore)
            {
                _seenScore = _teamScore;
                Broadcast("ACO_OnScoreChanged");
            }

            if (_state == STATE_COMPLETE && !_seenComplete)
            {
                _seenComplete = true;
                Broadcast("ACO_OnMissionComplete");
            }
            if (_state == STATE_FAILED && !_seenFailed)
            {
                _seenFailed = true;
                Broadcast("ACO_OnMissionFailed");
            }
        }

        private void Broadcast(string eventName)
        {
            if (EventListeners == null) { return; }
            for (int i = 0; i < EventListeners.Length; i++)
            {
                UdonSharpBehaviour listener = EventListeners[i];
                if (listener == null) { continue; }
                listener.SendCustomEvent(eventName);
            }
        }

        // ───────────── 조회 (UI용) ─────────────

        public int GetState() { return _state; }
        public int GetWavesSent() { return _wavesSent; }
        /// 미션 시작 서버 시각(ms). 값이 바뀌면 새 미션이다(로그 초기화 기준).
        public int GetMissionStartMs() { return _missionStartMs; }

        /// 지금 페이즈 진행: 파괴형 = 파괴 수/전체, 할당량 = 얻은 점수/목표 점수, 시한 표적 = 잡은 수/전체.
        public int GetObjectivesDone() { return _objectivesDone; }
        public int GetObjectiveTotal() { return _objectiveTotal; }

        public int GetPhaseCount() { return _phaseCount; }
        /// 지금(또는 마지막) 페이즈 번호(0부터). -1 = 아직 첫 페이즈 전.
        public int GetPhaseIndex() { return _phaseIndex; }
        public int GetPhaseType(int k) { return (k >= 0 && k < _phaseTypes.Length) ? _phaseTypes[k] : PHASE_NONE; }
        public int GetCurrentPhaseType() { return GetPhaseType(_phaseIndex); }
        /// 목표 진행 중이면 true, 페이즈 사이(또는 시작 전)면 false.
        public bool IsPhaseActive() { return _phaseActive; }
        public int GetPhasesCleared() { return _phasesCleared; }
        public int GetLastPhaseBonus() { return _lastPhaseBonus; }
        public int GetPhaseBonusTotal() { return _phaseBonusTotal; }
        /// 지금 목표 유닛의 태그. 생성기 GetUnitTag와 같으면 목표(0 = 지금은 목표 유닛 없음 — 할당량, 시한 표적 사이 등).
        public int GetActiveObjectiveTag()
        {
            ACO_PhaseModule m = GetModule(GetCurrentPhaseType());
            if (m != null && !m.MarksObjectiveTag()) { return 0; }
            return _activeTag;
        }

        /// 모듈이 판정에 쓰는 진짜 목표 태그(점수 할당량처럼 표시를 숨기는 모듈도 이 값으로 센다).
        public int GetModuleTag() { return _activeTag; }
        /// 다음 페이즈까지 남은 시간(초). 페이즈 진행 중이면 0.
        public float GetNextPhaseSeconds()
        {
            if (_phaseActive || _state != STATE_RUNNING) { return 0f; }
            return Mathf.Max((_nextPhaseMs - Networking.GetServerTimeInMilliseconds()) * 0.001f, 0f);
        }

        /// 마지막으로 끝난 페이즈가 실패였는지(보너스 페이즈 물러남 — 보너스 없음).
        public bool GetLastPhaseFailed() { return _lastPhaseFailed; }

        /// HUD 항로 표시용: 지금 페이즈 모듈이 항로를 그리면(ShowsEscortRoute) 그 태그의 살아 있는 호위 대상(Role VIP) 하나. 없으면 null.
        public ACO_EscortUnit GetRouteEscort()
        {
            ACO_PhaseModule routeModule = GetModule(GetCurrentPhaseType());
            if (!_phaseActive || Generator == null || routeModule == null || !routeModule.ShowsEscortRoute()) { return null; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != _activeTag) { continue; }
                ACO_EscortUnit unit = Generator.GetEscort(i);
                if (unit != null && !unit.IsDestroyed() && unit.Role == ACO_EscortUnit.ROLE_VIP) { return unit; }
            }
            return null;
        }

        /// 페이즈 k(0부터)의 점수 배율.
        public float GetPhaseMultiplier(int k)
        {
            if (k >= 0 && k == _bonusIndex) { return BonusScoreMultiplier; }
            if (PhaseScoreMultiplier == null || PhaseScoreMultiplier.Length == 0) { return 1f; }
            k = Mathf.Clamp(k, 0, PhaseScoreMultiplier.Length - 1);
            return PhaseScoreMultiplier[k];
        }

        /// 지금 격추에 적용되는 배율(첫 페이즈 전은 1페이즈 배율).
        public float GetScoreMultiplier() { return GetPhaseMultiplier(Mathf.Max(_phaseIndex, 0)); }

        public string GetPhaseName(int type)
        {
            return IsModuleType(type) ? GetModule(type).GetName() : "-";
        }

        /// 페이즈 안내 문구(영문, HUD 배너용).
        /// 지금 페이즈의 작전 지역 — 모듈 GetLocation("DEST: 이름" 등), 비면 "AO: 이름"(SetModuleSite). 지형 장식과 관계없으면 "".
        public string GetPhaseLocation()
        {
            if (!_phaseActive || Generator == null) { return ""; }
            ACO_PhaseModule module = GetModule(GetCurrentPhaseType());
            if (module != null)
            {
                string own = module.GetLocation();
                if (own.Length > 0) { return own; }
            }
            if (_phaseSite < 0) { return ""; }
            string name = Generator.GetSiteName(_phaseSite);
            return name.Length > 0 ? "AO: " + name : "";
        }

        public string GetPhaseDescription(int type)
        {
            return IsModuleType(type) ? GetModule(type).GetDescription() : "";
        }

        /// 전체 제한 시간(ms). 0 = 없음.
        public int GetTimeLimitMs()
        {
            if (TimeLimitPerPhase <= 0f) { return 0; }
            int mainPhases = _bonusIndex >= 0 ? _phaseCount - 1 : _phaseCount;
            return Mathf.RoundToInt(TimeLimitPerPhase * 1000f) * Mathf.Max(mainPhases, 1) + _timeExtensionMs;
        }

        /// 이번 미션에 호위 이동 시간으로 늘어난 제한 시간(ms). HUD가 늘어난 걸 보고 알린다.
        public int GetTimeExtensionMs() { return _timeExtensionMs; }

        // ───────────── 호위 이동 시간 (오너) ─────────────

        private bool GrantsTravelTime(int type)
        {
            if (EscortTimeMargin < 0f || TimeLimitPerPhase <= 0f) { return false; }
            ACO_PhaseModule m = GetModule(type);
            return m != null && m.GrantTravelTime;
        }

        /// 지금 태그의 지켜야 하는 호위 대상(Role VIP)이 모두 출발한 게 보이면(이 클라이언트에 배치가 적용됨) 가장 늦게 도착하는 대상의
        /// 남은 시간 + EscortTimeMargin을 제한 시간에 한 번 더한다. 아직 안 보이면 다음 틱에 다시 본다.
        private void GrantTravelTime(int now)
        {
            if (_activeTag == 0 || Generator == null) { return; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            float worst = -1f;
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != _activeTag) { continue; }
                ACO_EscortUnit unit = Generator.GetEscort(i);
                if (unit == null || unit.Role != ACO_EscortUnit.ROLE_VIP) { continue; }
                // 배치가 이 클라이언트에 아직 적용되지 않았으면(예전 항로) 기다린다
                if (unit.GetStartMs() != Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_ESCORT, i)) { return; }
                worst = Mathf.Max(worst, unit.GetArrivalSeconds());
            }
            if (worst < 0f) { return; }
            int add = Mathf.RoundToInt((worst + Mathf.Max(EscortTimeMargin, 0f)) * 1000f);
            _timeExtensionMs += add;
            _travelGranted = true;
            _cntChanged = true;
            Debug.Log("[ACO] 호위 이동 시간: 제한 시간 +" + Mathf.RoundToInt(add * 0.001f) + "초(도착까지 " + Mathf.RoundToInt(worst) + "초 + 여유)");
        }

        /// 미션 경과 시간(초). 끝났으면 끝난 시점까지.
        public float GetElapsedSeconds()
        {
            if (_state == STATE_IDLE) { return 0f; }
            int end = (_state == STATE_RUNNING) ? Networking.GetServerTimeInMilliseconds() : _endMs;
            return (end - _missionStartMs) * 0.001f;
        }

        /// 남은 시간(초). 제한 시간이 없으면 -1.
        public float GetRemainingSeconds()
        {
            if (_bonusIndex >= 0 && _phaseIndex == _bonusIndex && _phaseActive && _state == STATE_RUNNING)
            {
                ACO_PhaseModule bm = GetModule(GetCurrentPhaseType());
                float limit = bm != null ? bm.BonusTimeLimit : 0f;
                return Mathf.Max(limit - (Networking.GetServerTimeInMilliseconds() - _phaseStartMs) * 0.001f, 0f);
            }
            int limitMs = GetTimeLimitMs();
            if (limitMs <= 0) { return -1f; }
            return Mathf.Max(limitMs * 0.001f - GetElapsedSeconds(), 0f);
        }

        public int GetTeamScore() { return _teamScore; }
        public int GetFighterKills() { return _killFighters; }
        public int GetGroundKills() { return _killGround; }
        public int GetClearBonus() { return _clearBonusAwarded; }
        public int GetTimeBonus() { return _timeBonusAwarded; }

        public string GetRankLetter()
        {
            if (_rank == RANK_S) { return "S"; }
            if (_rank == RANK_A) { return "A"; }
            if (_rank == RANK_B) { return "B"; }
            if (_rank == RANK_C) { return "C"; }
            if (_rank == RANK_F) { return "F"; }
            return "-";
        }
    }
}
