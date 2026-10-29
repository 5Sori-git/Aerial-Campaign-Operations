using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;

namespace ACO
{
    /// 미션 진행 관리 — 페이즈 방식. 미션은 1~5개 페이즈로 이루어지고, 각 페이즈의 목표 종류는 무작위(직전 페이즈와 같은 종류는 안 나온다).
    ///
    /// 페이즈 종류:
    ///  - 타격(STRIKE): 고가치 표적(공장/벙커) 파괴. 표적 주위엔 방공 호위 링
    ///  - 방공망 제압(SEAD): 한 지점 둘레의 SAM/대공포 시설 전부 파괴
    ///  - 점수 할당량(QUOTA): 이 페이즈에서 정해진 점수 획득(대상 자유, 사냥터와 편대를 배치)
    ///  - 시한 표적(TST): 표적이 하나씩 나타나고, 각각 TstTimeLimit 안에 못 잡으면 도주. 다 잡으면 클리어. 하나라도 도주하면 그 즉시 미션 실패(FAIL_TARGET_ESCAPED)
    ///  - 제공권 확보(AIR): 적 편대 전멸
    ///  - 보스 편대(BOSS): 이름 붙은 에이스 4기 편대 전멸(BossMinPhase 이후에만)
    ///  - 호위(ESCORT): 아군 수송기(ACO_EscortUnit)가 항로 끝까지 가면 클리어(남은 체력만큼 보너스), 격추되면 그 즉시 미션 실패(FAIL_VIP_LOST).
    ///    항로를 따라 적 요격 편대가 나타나고, 항로 아래 지면이 있으면 방공 시설도 놓인다.
    ///  - 폭격기 요격(INTERCEPT): 적 폭격기(ACO_EscortUnit.Hostile) 편대가 삼각 대형으로 도시 상공으로 날아온다(적 호위 전투기 편대 동행). 전부 격추하면 클리어,
    ///    한 대라도 도시 상공(항로 끝)에 닿아 폭탄을 떨어뜨리면 그 즉시 미션 실패(실패 사유 FAIL_CITY_BOMBED).
    ///  - 지상 호위(CONVOY): 아군 콘보이(지상 ACO_EscortUnit 여러 대)가 지상 항로 끝까지 가면 클리어(살아남은 비율만큼 보너스),
    ///    전멸하면 그 즉시 미션 실패(FAIL_CONVOY_LOST). 항로 옆에 전차 매복조(ACO_Tank)와 방공 시설이 놓인다.
    /// 페이즈 종류는 그 페이즈가 시작되는 순간에 고르고, 고르기 전에 배치가 가능한지(풀 여유, 근처 지면/바다, 항로) 확인한다 —
    /// 안 되는 종류는 빼고 다른 종류를 고른다. 확인을 통과했는데 실제 배치가 실패하면(생성기 오너 쪽 자리 찾기 실패 등)
    /// 페이즈를 건너뛰지 않고 그 종류를 막은 채 다른 종류로 같은 페이즈를 다시 시작한다.
    /// 페이즈 목표는 그 페이즈가 시작될 때 생성기가 배치하고 태그를 단다(GetActiveObjectiveTag와 같은 태그 = 지금 목표).
    /// 목표 위치는 페이즈 시작 순간 플레이어들의 평균 위치 기준 거리 범위 안이다(공역이 넓어도 이동 시간이 일정하게).
    ///
    /// 점수: 격추 점수와 페이즈 보너스에 그 페이즈의 배율(PhaseScoreMultiplier, 뒤 페이즈일수록 높다)을 곱한다.
    ///   격추는 "이번 배치(serial)에서 살아 있는 걸 본 뒤 파괴됨"일 때 한 번만 센다. 클리어하면 클리어 보너스 + 남은 시간 보너스.
    ///   랭크 기준은 페이즈당 기준값 x 고른 페이즈들의 배율 합(페이즈 수가 달라도 공평하게).
    /// 실패: 제한 시간(TimeLimitPerPhase x 페이즈 수) 초과, 또는 실패 조건이 있는 페이즈(호위·지상 호위·시한 표적·폭격기 요격)의 실패 — 미션이 바로 끝난다.
    /// 보너스 페이즈(BONUS): 마지막 페이즈를 클리어하면 BonusPhaseChance 확률로 한 페이즈가 더 붙는다 — 슈퍼에이스(NPC_SuperAce)와 호위 에이스.
    ///   슈퍼에이스를 격추하면 BonusClearBonus(배율 BonusScoreMultiplier). BonusTimeLimit 안에 못 잡으면 슈퍼에이스는 물러나고 보너스만 없다 —
    ///   미션은 이미 클리어라 실패하지 않는다(미션 제한 시간도 보지 않는다). 랭크 기준과 시간 보너스는 본 미션(보너스 전)으로 센다.
    /// 증원 웨이브: 페이즈 시작(2페이즈부터) + 교착(적 전력이 StallStrengthRatio 이하이고 StallTime 동안 진전 없음).
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
        [Tooltip("페이즈 종류 가중치 [타격, 방공망 제압, 점수 할당량, 시한 표적, 제공권, 보스, 호위, 지상 호위, 폭격기 요격]. 0이면 안 나온다. 배열이 짧으면 빠진 종류는 1")]
        public float[] PhaseTypeWeights = new float[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        [Tooltip("보스 편대는 이 페이즈 번호(1부터)부터 나올 수 있다")]
        public int BossMinPhase = 3;
        [Tooltip("시한 표적(긴급 — 짧은 시간 안에 파괴)은 이 페이즈 번호(1부터)부터 나올 수 있다. 2면 첫 페이즈에는 안 나온다")]
        public int TstMinPhase = 2;
        [Tooltip("페이즈 목표 자리에서 이 거리(m) 안에 지형 장식(도시/마을/비행장 등)이 있으면 그 이름을 작전 지역으로 배너에 띄운다")]
        public float SiteNameRadius = 3000f;
        [Tooltip("페이즈 클리어 보너스(배율 곱함)")]
        public int PhaseClearBonus = 500;
        [Tooltip("미션 시작 후 첫 페이즈까지(초) — 배경 병력 배치가 동기화될 시간")]
        public float FirstPhaseDelay = 5f;
        [Tooltip("페이즈 클리어 후 다음 페이즈까지(초)")]
        public float PhaseTransitionDelay = 10f;
        [Tooltip("목표 배치가 이 시간(초) 안에 안 보이면(풀 부족, 자리 없음) 그 페이즈를 다른 종류로 바꾼다(시한 표적의 두 번째 이후 표적은 건너뛴다)")]
        public float DeployTimeout = 20f;
        [Tooltip("배치 후 이 시간(초)이 지난 목표는 살아 있는 걸 못 봤어도 파괴되면 달성으로 친다(오너가 바뀐 직후 등)")]
        public float ObjectiveSettleTime = 10f;
        [Tooltip("목표 지상 시설(타격/방공망/사냥터)을 놓을 거리(m) — 페이즈 시작 순간 플레이어 평균 위치 기준")]
        public float TargetDistanceMin = 12000f;
        public float TargetDistanceMax = 30000f;
        [Tooltip("목표 편대(제공권/보스/할당량)가 나타날 거리(m) — 플레이어 평균 위치 기준")]
        public float FlightDistanceMin = 15000f;
        public float FlightDistanceMax = 25000f;

        [Header("페이즈: 타격")]
        [Tooltip("고가치 표적 수")]
        public int StrikeTargets = 2;

        [Header("페이즈: 방공망 제압")]
        [Tooltip("방공 시설 유닛 수 = Base + PerPhase x (페이즈 번호-1)")]
        public int SeadUnitsBase = 4;
        public int SeadUnitsPerPhase = 1;

        [Header("페이즈: 점수 할당량")]
        [Tooltip("이 페이즈에서 얻어야 하는 점수(배율 곱함 — 격추 점수도 같은 배율이라 필요한 격추 수는 같다)")]
        public int QuotaScore = 1500;
        [Tooltip("사냥터 지상 유닛 수")]
        public int QuotaGround = 8;
        [Tooltip("사냥터로 오는 전투기 수")]
        public int QuotaFighters = 2;

        [Header("페이즈: 시한 표적")]
        [Tooltip("차례로 나타날 표적 수")]
        public int TstCount = 3;
        [Tooltip("표적마다 주어지는 시간(초). 처음 보인 순간부터")]
        public float TstTimeLimit = 90f;
        [Tooltip("표적 사이 간격(초)")]
        public float TstInterval = 8f;
        [Tooltip("잡은 표적 하나당 보너스(배율 곱함)")]
        public int TstBonus = 300;
        [Tooltip("표적이 나타날 거리(m) — 플레이어 평균 위치 기준")]
        public float TstDistanceMin = 5000f;
        public float TstDistanceMax = 12000f;

        [Header("페이즈: 제공권 확보")]
        [Tooltip("적 전투기 수 = Base + PerPhase x (페이즈 번호-1)")]
        public int AirFightersBase = 4;
        public int AirFightersPerPhase = 1;

        [Header("페이즈: 보스 편대")]
        [Tooltip("보스 편대 기수(모두 에이스)")]
        public int BossFighters = 4;
        [Tooltip("보스 편대 이름(무작위)")]
        public string[] BossNames = new string[] { "VIPER", "SPECTRE", "RAVEN", "WARLOCK", "TEMPEST", "NOMAD" };

        [Header("보너스 페이즈 (슈퍼에이스)")]
        [Tooltip("마지막 페이즈를 클리어하면 이 확률로 보너스 페이즈가 붙는다. 생성기 풀에 슈퍼에이스(NPC_SuperAce)가 있어야 한다. 0이면 안 나온다")]
        [Range(0f, 1f)] public float BonusPhaseChance = 0.3f;
        [Tooltip("이 페이즈 수 이상인 미션에서만 보너스가 붙는다")]
        [Range(1, 5)] public int BonusMinPhases = 2;
        [Tooltip("슈퍼에이스와 함께 오는 호위 에이스 수(목표 아님)")]
        public int BonusEscorts = 2;
        [Tooltip("보너스 페이즈 제한 시간(초). 넘으면 슈퍼에이스가 물러나고 보너스 없이 미션 클리어")]
        public float BonusTimeLimit = 240f;
        [Tooltip("슈퍼에이스 격추 보너스(배율 곱하기 전)")]
        public int BonusClearBonus = 15000;
        [Tooltip("보너스 페이즈의 점수 배율(격추 점수·보너스)")]
        public float BonusScoreMultiplier = 2.5f;

        [Header("페이즈: 호위")]
        [Tooltip("호위 항로 길이(m). 수송기 속도(ACO_EscortUnit.Speed)로 나누면 걸리는 시간")]
        public float EscortRouteLength = 32000f;
        [Tooltip("호위 성공 후 이 시간(초) 뒤에 수송기/콘보이를 거둔다 — 그동안 투하 후 계속 비행/활주로 정지/정차 연출이 보인다")]
        public float EscortLingerTime = 30f;
        [Tooltip("항로 아래에 놓을 방공 시설 수(지면이 있는 곳에만)")]
        public int EscortSites = 2;
        [Tooltip("도착 보너스 = 이 값 x 남은 체력 비율 x 배율(페이즈 클리어 보너스에 더한다)")]
        public int EscortBonus = 1000;
        [Tooltip("항로를 따라 나타날 요격 편대 수")]
        public int EscortInterceptWaves = 3;
        [Tooltip("요격 편대 하나의 전투기 수 = Base + PerPhase x (페이즈 번호-1)")]
        public int EscortInterceptBase = 2;
        public int EscortInterceptPerPhase = 1;
        [Tooltip("요격 편대는 수송기보다 항로 진행률로 이만큼 앞에서 기다린다")]
        [Range(0f, 0.5f)] public float EscortInterceptLead = 0.12f;
        [Tooltip("요격 편대가 나타날 거리(m) — 기다리는 지점 기준")]
        public float EscortInterceptDistanceMin = 7000f;
        public float EscortInterceptDistanceMax = 11000f;

        [Header("페이즈: 지상 호위")]
        [Tooltip("콘보이 차량 수(지상 이동 방식 호위 대상)")]
        public int ConvoySize = 3;
        [Tooltip("지상 항로 길이(m). 차량 속도로 나누면 걸리는 시간")]
        public float ConvoyRouteLength = 6000f;
        [Tooltip("항로 옆 전차 매복조 수 = Base + PerPhase x (페이즈 번호-1)")]
        public int ConvoyAmbushBase = 2;
        public int ConvoyAmbushPerPhase = 1;
        [Tooltip("항로 옆 방공 시설 수(플레이어를 노린다)")]
        public int ConvoySites = 1;
        [Tooltip("도착 보너스 = 이 값 x 살아남은 비율 x 배율(페이즈 클리어 보너스에 더한다)")]
        public int ConvoyBonus = 1200;

        [Header("페이즈: 폭격기 요격")]
        [Tooltip("폭격기 수 = 이 값 + InterceptBombersPerPhase x 페이즈 번호(0부터). 풀(적 폭격기 NPC_Bomber)만큼까지")]
        public int InterceptBombersBase = 3;
        public float InterceptBombersPerPhase = 0.5f;
        [Tooltip("폭격기 항로 길이(m). 폭격기 속도(약 110m/s)로 나누면 도시까지 걸리는 시간")]
        public float InterceptRouteLength = 18000f;
        [Tooltip("폭격기와 같이 오는 적 호위 전투기 수 = 이 값 + InterceptEscortsPerPhase x 페이즈 번호")]
        public int InterceptEscortsBase = 2;
        public int InterceptEscortsPerPhase = 1;
        [Tooltip("폭격기 격추 점수(배율 곱하기 전)")]
        public int BomberKillScore = 400;

        [Header("실패 조건")]
        [Tooltip("페이즈당 제한 시간(초). 전체 제한 = 이 값 x 페이즈 수. 0이면 제한 없음")]
        public float TimeLimitPerPhase = 420f;

        [Header("팀 점수")]
        [Tooltip("전투기 격추 점수 [신병, 베테랑, 에이스]")]
        public int[] ScoreFighterByTier = new int[] { 100, 200, 500 };
        [Tooltip("지상 유닛에 ScoreValue 필드가 없을 때의 점수")]
        public int ScoreGroundFallback = 100;
        [Tooltip("고가치 표적에 ScoreValue 필드가 없을 때의 점수")]
        public int ScoreObjectiveFallback = 500;
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
        public const int PHASE_STRIKE = 1;
        public const int PHASE_SEAD = 2;
        public const int PHASE_QUOTA = 3;
        public const int PHASE_TST = 4;
        public const int PHASE_AIR = 5;
        public const int PHASE_BOSS = 6;
        public const int PHASE_ESCORT = 7;
        public const int PHASE_CONVOY = 8;
        public const int PHASE_INTERCEPT = 9;
        public const int PHASE_BONUS = 10;  // 보너스 페이즈(무작위 선택 대상 아님 — PHASE_TYPE_COUNT 밖)
        private const int PHASE_TYPE_COUNT = 9;

        // 실패 사유(GetFailReason)
        public const int FAIL_TIME = 0;
        public const int FAIL_CITY_BOMBED = 1;
        public const int FAIL_VIP_LOST = 2;          // 호위 대상(수송기) 격추
        public const int FAIL_CONVOY_LOST = 3;       // 콘보이 전멸
        public const int FAIL_TARGET_ESCAPED = 4;    // 시한 표적 도주

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
        [UdonSynced] private int _objectivesDone;        // 지금 페이즈 진행(종류별: 파괴 수 / 얻은 점수 / 잡은 시한 표적)
        [UdonSynced] private int _objectiveTotal;
        [UdonSynced] private int _objectiveEvents;       // 목표 파괴 누적(이벤트용)
        [UdonSynced] private int _phaseScoreStart;
        [UdonSynced] private int _tstIndex;
        [UdonSynced] private int _tstHits;
        [UdonSynced] private int _tstSpawnMs;           // 다음 시한 표적을 배치할(또는 배치한) 시각
        [UdonSynced] private int _tstDeadlineMs;        // 0 = 아직 안 보임
        [UdonSynced] private bool _tstDeployed;
        [UdonSynced] private bool _tstEscaped;          // 이번 페이즈에 도주한 시한 표적이 있는지(있으면 미션 실패)
        [UdonSynced] private int _bossName = -1;
        [UdonSynced] private int _escortWaves;           // 이번 호위 페이즈에 보낸 요격 편대 수
        [UdonSynced] private bool _lastPhaseFailed;      // 마지막으로 끝난 페이즈가 실패(호위 대상 격추)였는지
        [UdonSynced] private int _phaseBlocked;          // 이번 페이즈에서 배치에 실패해 막은 종류(비트)
        [UdonSynced] private int _phaseAttempt;          // 이번 페이즈에서 종류를 바꾼 횟수(태그 구분용)
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
        // 오너 로컬: 이번 호위 대상을 살아 있는 상태로 본 적이 있는지(부활 동기화 전 체력 0 오판 방지)
        private bool _escortSeenAlive;
        private bool[] _convoySeenAlive = new bool[0];   // 호위 대상 인덱스별(콘보이)
        // 오너 로컬: 사전 확인이 찾은 배치 계획(그 종류로 시작할 때 그대로 배치한다 — 확인이 통과하면 배치도 성공하게).
        private int _planType;
        private Vector3 _planPoint;      // 지상/바다 "정한 자리"
        private bool _planHasPoint;
        private int _planVariant;        // 타격: 0 = 육상 표적, 1 = 함선
        private int _planSeed;           // 호위/지상 호위: 항로 씨앗
        private int _planSite = -1;      // 정한 자리 근처 지형 장식 자리(작전 지역 이름)
        private bool _tstUsePlan;        // 시한 표적: 첫 표적을 정한 자리에

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

            _phaseCount = _pendingPhaseCount > 0 ? _pendingPhaseCount : Mathf.Clamp(DefaultPhaseCount, 1, MAX_PHASES);
            _pendingPhaseCount = 0;
            // 종류는 페이즈마다 시작 순간에 고른다(그때의 풀/플레이어 위치로 가능 여부를 확인하므로).
            _phaseTypes = new int[MAX_PHASES + 1];
            _bonusIndex = -1;
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
            _bossName = -1;
            _escortWaves = 0;
            _lastPhaseFailed = false;
            _nextPhaseMs = now + Mathf.RoundToInt(FirstPhaseDelay * 1000f);

            ResetKillTracking();
            Publish();

            Debug.Log("[ACO] 미션 시작: " + _phaseCount + "페이즈, 적 전력 " + _initialStrength);
        }

        /// 페이즈 phaseIndex의 종류를 고른다: 가중치 무작위(직전 페이즈와 같은 종류 금지, 보스는 BossMinPhase부터, 시한 표적은 TstMinPhase부터, blocked 비트는 제외).
        /// 뽑힌 종류가 지금 배치할 수 없으면(IsPhaseFeasible) 빼고 다시 뽑는다. 하나도 안 되면 PHASE_NONE.
        private int PickPhaseType(int phaseIndex, int prev, int blocked, Vector3 focus)
        {
            int rejected = blocked;
            for (int round = 0; round < PHASE_TYPE_COUNT; round++)
            {
                float total = 0f;
                for (int t = 1; t <= PHASE_TYPE_COUNT; t++) { total += PickWeight(t, phaseIndex, prev, rejected); }
                if (total <= 0f) { return PHASE_NONE; }
                float r = Random.value * total;
                int pick = PHASE_NONE;
                for (int t = 1; t <= PHASE_TYPE_COUNT; t++)
                {
                    float w = PickWeight(t, phaseIndex, prev, rejected);
                    if (w <= 0f) { continue; }
                    pick = t;
                    if (r < w) { break; }
                    r -= w;
                }
                if (pick == PHASE_NONE) { return PHASE_NONE; }
                if (IsPhaseFeasible(pick, phaseIndex, focus)) { return pick; }
                Debug.Log("[ACO] 페이즈 " + (phaseIndex + 1) + ": " + GetPhaseName(pick) + " 배치 불가 — 다른 종류로");
                rejected |= 1 << pick;
            }
            return PHASE_NONE;
        }

        private float PickWeight(int type, int phaseIndex, int prev, int rejected)
        {
            if ((rejected & (1 << type)) != 0) { return 0f; }
            return TypeWeight(type, phaseIndex, prev);
        }

        /// 지금 이 종류를 배치할 수 있는지(풀 여유, focus 근처 지면/바다, 항로). 생성기의 동기화 상태와 지형만 본다.
        /// 통과하면 찾은 자리/항로 씨앗을 배치 계획(_plan*)으로 남긴다 — BeginPhase가 그대로 배치하므로 확인이 통과하면 배치도 성공한다.
        private bool IsPhaseFeasible(int type, int phaseIndex, Vector3 focus)
        {
            if (Generator == null) { return false; }
            _planType = PHASE_NONE;
            _planHasPoint = false;
            _planSite = -1;
            _planVariant = 0;
            _planSeed = 0;
            bool ok = false;
            if (type == PHASE_STRIKE)
            {
                if (Generator.CountFreeHvt(false) > 0 && Generator.HasGroundNear(focus, TargetDistanceMin, TargetDistanceMax)) { ok = true; _planVariant = 0; }
                else if (Generator.CountFreeHvt(true) > 0 && Generator.HasShipSiteNear(focus, TargetDistanceMin, TargetDistanceMax)) { ok = true; _planVariant = 1; }
                if (ok) { SetPlanPoint(); }
            }
            else if (type == PHASE_SEAD)
            {
                ok = Generator.CountFreeLandGround() >= Mathf.Min(SeadUnitsBase, 3) && Generator.HasGroundNear(focus, TargetDistanceMin, TargetDistanceMax);
                if (ok) { SetPlanPoint(); }
            }
            else if (type == PHASE_QUOTA)
            {
                // 사냥터든 편대든 점수를 얻을 거리가 있으면 된다. 사냥터 자리를 찾았으면 그 자리에.
                bool ground = Generator.CountFreeLandGround() >= 3 && Generator.HasGroundNear(focus, TargetDistanceMin * 0.5f, TargetDistanceMax * 0.6f);
                if (ground) { SetPlanPoint(); }
                ok = ground || Generator.CountFreeFighterUnits() >= 2;
            }
            else if (type == PHASE_TST)
            {
                ok = Generator.CountFreeLandGround() >= 1 && Generator.HasGroundNear(focus, TstDistanceMin, TstDistanceMax);
                if (ok) { SetPlanPoint(); }
            }
            else if (type == PHASE_AIR || type == PHASE_BOSS)
            {
                ok = Generator.CountFreeFighterUnits() >= 2;
            }
            else if (type == PHASE_ESCORT)
            {
                int seed = NewSeed();
                ok = Generator.CountFreeEscorts(ACO_EscortUnit.MOVE_AIR) >= 1
                    && Generator.CanBuildEscortRoute(ACO_EscortUnit.MOVE_AIR, focus, EscortRouteLength, 1, seed);
                if (ok) { _planSeed = seed; _planSite = Generator.GetLastRouteSite(); }
            }
            else if (type == PHASE_INTERCEPT)
            {
                int mode = ACO_EscortUnit.MOVE_AIR | ACO_MissionGenerator.ESCORT_HOSTILE;
                int units = Mathf.Min(Generator.CountFreeEscorts(mode), InterceptBomberCount(phaseIndex));
                int seed = NewSeed();
                ok = units >= 1 && Generator.CanBuildEscortRoute(mode, focus, InterceptRouteLength, units, seed);
                if (ok) { _planSeed = seed; _planSite = Generator.GetLastRouteSite(); }
            }
            else if (type == PHASE_CONVOY)
            {
                int units = Mathf.Min(Generator.CountFreeEscorts(ACO_EscortUnit.MOVE_GROUND), Mathf.Max(ConvoySize, 1));
                int seed = NewSeed();
                // 콘보이를 위협할 전차가 있어야 지상 호위가 성립한다.
                ok = units >= 1 && Generator.CountFreeTankUnits() >= 1
                    && Generator.CanBuildEscortRoute(ACO_EscortUnit.MOVE_GROUND, focus, ConvoyRouteLength, units, seed);
                if (ok) { _planSeed = seed; _planSite = Generator.GetLastRouteSite(); }
            }
            if (ok) { _planType = type; }
            return ok;
        }

        private void SetPlanPoint()
        {
            _planPoint = Generator.GetCheckedPoint();
            _planHasPoint = true;
            _planSite = Generator.FindSiteNear(_planPoint, SiteNameRadius);
        }

        private int NewSeed()
        {
            int seed = Random.Range(1, int.MaxValue);
            return seed;
        }

        /// 이 종류의 배치 계획이 있으면 true(사전 확인을 거쳤다).
        private bool HasPlan(int type) { return _planType == type && type != PHASE_NONE; }

        private float TypeWeight(int type, int phaseIndex, int prev)
        {
            if (type == prev) { return 0f; }
            if (type == PHASE_BOSS && phaseIndex + 1 < BossMinPhase) { return 0f; }
            if (type == PHASE_TST && phaseIndex + 1 < TstMinPhase) { return 0f; }
            if (PhaseTypeWeights == null || type - 1 >= PhaseTypeWeights.Length) { return 1f; }
            return Mathf.Max(PhaseTypeWeights[type - 1], 0f);
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
                bool ended;
                if (type == PHASE_BONUS) { ended = UpdateBonus(now); }
                else if (type == PHASE_QUOTA) { ended = UpdateQuota(now); }
                else if (type == PHASE_TST) { ended = UpdateTst(now); }
                else if (type == PHASE_ESCORT) { ended = UpdateEscort(now); }
                else if (type == PHASE_CONVOY) { ended = UpdateConvoy(now); }
                else if (type == PHASE_INTERCEPT) { ended = UpdateIntercept(now); }
                else { ended = UpdateDestroy(now); }
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
                BeginPhase(PHASE_BONUS, now, true);
                return;
            }
            int prev = _phaseIndex > 0 ? _phaseTypes[_phaseIndex - 1] : PHASE_NONE;
            int type = PickPhaseType(_phaseIndex, prev, 0, _phaseFocus);
            if (type == PHASE_NONE)
            {
                // 지금은 어떤 종류도 확인을 통과하지 못했다 — 가중치만으로 고르고, 배치가 실패하면 다른 종류로 바꾼다.
                Debug.LogWarning("[ACO] 페이즈 " + (_phaseIndex + 1) + ": 배치 가능한 종류가 없음(풀/지형) — 가중치로만 고름");
                type = PickFallbackType(_phaseIndex, prev, 0);
                if (type == PHASE_NONE) { type = PHASE_AIR; } // 가중치가 전부 0인 설정 — 제공권으로
            }
            BeginPhase(type, now, true);
        }

        /// 확인 없이 가중치로만 고른다(마지막 수단). 막힌 종류는 뺀다.
        private int PickFallbackType(int phaseIndex, int prev, int blocked)
        {
            float total = 0f;
            for (int t = 1; t <= PHASE_TYPE_COUNT; t++) { total += PickWeight(t, phaseIndex, prev, blocked); }
            if (total <= 0f) { return PHASE_NONE; }
            float r = Random.value * total;
            int pick = PHASE_NONE;
            for (int t = 1; t <= PHASE_TYPE_COUNT; t++)
            {
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
            _phaseBlocked |= 1 << failedType;
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
            _tstIndex = 0;
            _tstHits = 0;
            _tstDeadlineMs = 0;
            _tstDeployed = false;
            _tstEscaped = false;
            _tstSpawnMs = now;
            _escortWaves = 0;
            _escortSeenAlive = false;
            _convoySeenAlive = new bool[Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT)];

            // 종류를 바꿔 다시 시작해도 이전 시도의 유닛과 섞이지 않게 시도마다 다른 태그(하위 20+)를 쓴다. 시한 표적은 1~.
            int tag = PhaseTag(_phaseIndex, 20 + _phaseAttempt);
            _activeTag = tag;

            // 사전 확인의 계획이 있으면 그 자리/항로 그대로(정한 자리 = minDist −1, 항로 = 같은 씨앗). 없으면(마지막 수단 선택) 예전처럼 무작위로 찾는다.
            bool plan = HasPlan(type);
            bool point = plan && _planHasPoint;
            // 작전 지역: 정한 자리 근처 장식, 호위는 항로 목적지(도시/비행장)
            _phaseSite = plan ? _planSite : -1;
            if (type == PHASE_STRIKE)
            {
                if (point) { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_HVT, tag, StrikeTargets, _planVariant, _planPoint, -1f, -1f); }
                else { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_HVT, tag, StrikeTargets, -1, _phaseFocus, TargetDistanceMin, TargetDistanceMax); }
            }
            else if (type == PHASE_SEAD)
            {
                int count = SeadUnitsBase + SeadUnitsPerPhase * _phaseIndex;
                if (point) { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_SITE, tag, count, -1, _planPoint, -1f, -1f); }
                else { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_SITE, tag, count, -1, _phaseFocus, TargetDistanceMin, TargetDistanceMax); }
            }
            else if (type == PHASE_QUOTA)
            {
                _objectiveTotal = Mathf.RoundToInt(QuotaScore * GetPhaseMultiplier(_phaseIndex));
                if (point) { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_HUNT, tag, QuotaGround, -1, _planPoint, -1f, -1f); }
                else { Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_HUNT, tag, QuotaGround, -1, _phaseFocus, TargetDistanceMin * 0.5f, TargetDistanceMax * 0.6f); }
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, tag, QuotaFighters, -1, _phaseFocus, FlightDistanceMin, FlightDistanceMax);
            }
            else if (type == PHASE_TST)
            {
                _objectiveTotal = Mathf.Max(TstCount, 1);
                _activeTag = 0;
                _tstUsePlan = point;
            }
            else if (type == PHASE_ESCORT)
            {
                _objectiveTotal = 100; // 진행 = 호위 대상 체력(%)
                Generator.RequestDeployEscort(tag, _phaseFocus, EscortRouteLength, EscortSites, ACO_EscortUnit.MOVE_AIR, 1, 0, plan ? _planSeed : 0);
            }
            else if (type == PHASE_INTERCEPT)
            {
                _objectiveTotal = InterceptBomberCount(_phaseIndex); // 진행 = 격추한 폭격기 수
                Generator.RequestDeployEscort(tag, _phaseFocus, InterceptRouteLength, 0, ACO_EscortUnit.MOVE_AIR | ACO_MissionGenerator.ESCORT_HOSTILE,
                    _objectiveTotal, 0, plan ? _planSeed : 0);
            }
            else if (type == PHASE_CONVOY)
            {
                _objectiveTotal = Mathf.Max(ConvoySize, 1); // 진행 = 살아 있는 차량 수
                _objectivesDone = _objectiveTotal;
                int ambush = ConvoyAmbushBase + ConvoyAmbushPerPhase * _phaseIndex;
                Generator.RequestDeployEscort(tag, _phaseFocus, ConvoyRouteLength, ConvoySites, ACO_EscortUnit.MOVE_GROUND, ConvoySize, ambush, plan ? _planSeed : 0);
            }
            else if (type == PHASE_BONUS)
            {
                _objectiveTotal = 1;
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_SUPER_ACE, tag, BonusEscorts, -1, _phaseFocus, FlightDistanceMin, FlightDistanceMax);
            }
            else if (type == PHASE_AIR)
            {
                int count = AirFightersBase + AirFightersPerPhase * _phaseIndex;
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, tag, count, -1, _phaseFocus, FlightDistanceMin, FlightDistanceMax);
            }
            else
            {
                _bossName = (BossNames != null && BossNames.Length > 0) ? Random.Range(0, BossNames.Length) : -1;
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, tag, BossFighters, ACO_NPCFighterAI.SKILL_ACE, _phaseFocus, FlightDistanceMin, FlightDistanceMax);
            }

            _planType = PHASE_NONE; // 계획은 한 번만 쓴다
            Debug.Log("[ACO] 페이즈 " + (_phaseIndex + 1) + "/" + _phaseCount + " 시작: " + GetPhaseName(type) + " (배율 x" + GetPhaseMultiplier(_phaseIndex) + ")"
                + (plan ? "" : " — 사전 확인 없이(무작위 배치)"));

            // 2페이즈부터는 목표 쪽으로 증원도 보낸다.
            bool cooldownOver = now - _lastWaveMs >= Mathf.RoundToInt(WaveCooldown * 1000f);
            if (firstAttempt && WaveOnPhaseStart && _phaseIndex > 0 && _wavesSent < MaxWaves && cooldownOver && type != PHASE_TST && type != PHASE_BONUS)
            {
                SendWave(REASON_PHASE, now);
                return;
            }
            Publish();
        }

        /// 파괴형 페이즈(타격/방공망/제공권/보스): 태그가 붙은 유닛이 모두 파괴되면 클리어. 반환: 페이즈가 끝났으면 true.
        private bool UpdateDestroy(int now)
        {
            CountTagged(_activeTag, now);
            if (_cntTotal == 0)
            {
                // 아직 배치 동기화 전이거나, 풀/자리가 없어 아무것도 못 놓았다.
                if (now - _phaseStartMs >= Mathf.RoundToInt(DeployTimeout * 1000f))
                {
                    return ReplacePhase(now, "목표가 배치되지 않음(풀 부족/배치 실패)");
                }
                return false;
            }
            SetProgress(_cntDone, _cntTotal, now);
            if (_cntDone >= _cntTotal)
            {
                ClearPhase(now, false);
                return true;
            }
            return false;
        }

        /// 보너스 페이즈: 슈퍼에이스(태그) 격추 = 클리어(+BonusClearBonus), BonusTimeLimit 초과 = 물러남(보너스 없음), 배치 안 됨 = 건너뜀.
        /// 어느 쪽이든 EndPhase가 미션을 클리어로 끝낸다.
        private bool UpdateBonus(int now)
        {
            CountTagged(_activeTag, now);
            if (_cntTotal == 0)
            {
                if (now - _phaseStartMs >= Mathf.RoundToInt(DeployTimeout * 1000f))
                {
                    Debug.LogWarning("[ACO] 보너스 페이즈: 슈퍼에이스가 배치되지 않음 — 건너뜀");
                    EndPhase(now, true, false, 0);
                    return true;
                }
                return false;
            }
            SetProgress(_cntDone, _cntTotal, now);
            if (_cntDone >= _cntTotal)
            {
                EndPhase(now, false, false, Mathf.RoundToInt(BonusClearBonus * BonusScoreMultiplier));
                return true;
            }
            if (now - _phaseStartMs >= Mathf.RoundToInt(BonusTimeLimit * 1000f))
            {
                Debug.Log("[ACO] 보너스 페이즈: 시간 초과 — 슈퍼에이스가 물러남");
                if (_activeTag != 0) { Generator.RequestReleaseTag(_activeTag); }
                EndPhase(now, false, true, 0);
                return true;
            }
            return false;
        }

        /// 점수 할당량: 페이즈 시작 후 얻은 점수가 목표에 닿으면 클리어.
        private bool UpdateQuota(int now)
        {
            int earned = _teamScore - _phaseScoreStart;
            if (earned != _objectivesDone)
            {
                _objectivesDone = earned;
                _lastProgressMs = now;
                _cntChanged = true;
            }
            if (earned >= _objectiveTotal)
            {
                ClearPhase(now, false);
                return true;
            }
            return false;
        }

        /// 시한 표적: 하나씩 배치 → 처음 보인 순간부터 TstTimeLimit → 파괴(명중) 또는 시간 초과(철수) → 간격 뒤 다음.
        private bool UpdateTst(int now)
        {
            int count = Mathf.Max(TstCount, 1);
            if (!_tstDeployed)
            {
                if (now - _tstSpawnMs < 0) { return false; }
                _tstDeployed = true;
                _tstDeadlineMs = 0;
                _tstSpawnMs = now;
                _activeTag = PhaseTag(_phaseIndex, 1 + _tstIndex);
                if (_tstIndex == 0 && _tstUsePlan)
                {
                    // 첫 표적은 사전 확인이 찾은 자리에(확인이 통과하면 배치도 된다).
                    Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_GROUND, _activeTag, 1, -1, _planPoint, -1f, -1f);
                    _tstUsePlan = false;
                }
                else
                {
                    Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_GROUND, _activeTag, 1, -1, PlayerCenter(), TstDistanceMin, TstDistanceMax);
                }
                _cntChanged = true;
                return false;
            }

            CountTagged(_activeTag, now);
            if (_cntTotal == 0)
            {
                if (_tstDeadlineMs == 0 && now - _tstSpawnMs >= Mathf.RoundToInt(DeployTimeout * 1000f))
                {
                    // 첫 표적부터 못 놓으면 이 페이즈는 성립하지 않는다 — 다른 종류로. 이후 표적은 그 표적만 건너뛴다.
                    if (_tstIndex == 0 && _tstHits == 0) { return ReplacePhase(now, "시한 표적이 배치되지 않음"); }
                    Debug.LogWarning("[ACO] 시한 표적 " + (_tstIndex + 1) + " 배치 실패 — 건너뜀");
                    return NextTst(now, count);
                }
                return false;
            }
            if (_tstDeadlineMs == 0)
            {
                _tstDeadlineMs = now + Mathf.RoundToInt(TstTimeLimit * 1000f);
                _cntChanged = true;
            }
            if (_cntDone >= _cntTotal)
            {
                _tstHits++;
                _objectivesDone = _tstHits;
                _objectiveEvents++;
                _lastProgressMs = now;
                Debug.Log("[ACO] 시한 표적 " + (_tstIndex + 1) + "/" + count + " 파괴");
                return NextTst(now, count);
            }
            if (now - _tstDeadlineMs >= 0)
            {
                Debug.Log("[ACO] 시한 표적 " + (_tstIndex + 1) + "/" + count + " 도주");
                Generator.RequestReleaseTag(_activeTag);
                _tstEscaped = true;
                Debug.Log("[ACO] 시한 표적 도주 — 미션 실패");
                _failReason = FAIL_TARGET_ESCAPED;
                FinishMission(false, now);
                return true;
            }
            return false;
        }

        private bool NextTst(int now, int count)
        {
            _tstIndex++;
            _tstDeployed = false;
            _tstDeadlineMs = 0;
            _activeTag = 0;
            if (_tstIndex >= count)
            {
                // 다 지나갔다 = 모두 잡았다(도주하면 그 자리에서 미션 실패).
                EndPhase(now, false, false, 0);
                return true;
            }
            _tstSpawnMs = now + Mathf.RoundToInt(TstInterval * 1000f);
            Publish();
            return true;
        }

        /// 호위: 도착하면 클리어(남은 체력만큼 보너스), 격추되면 미션 실패. 항로 진행에 맞춰 앞쪽에 요격 편대를 보낸다.
        private bool UpdateEscort(int now)
        {
            int idx = FindEscortIndex(_activeTag);
            if (idx < 0)
            {
                if (now - _phaseStartMs >= Mathf.RoundToInt(DeployTimeout * 1000f))
                {
                    return ReplacePhase(now, "호위 대상이 배치되지 않음(EscortUnits 풀/항로)");
                }
                return false;
            }
            ACO_EscortUnit unit = Generator.GetEscort(idx);

            if (unit.IsDestroyed())
            {
                int settleMs = Mathf.RoundToInt(ObjectiveSettleTime * 1000f);
                if (!_escortSeenAlive && now - Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_ESCORT, idx) < settleMs) { return false; }
                Debug.Log("[ACO] 호위 실패: " + unit.TargetName + " 격추 — 미션 실패");
                _failReason = FAIL_VIP_LOST;
                FinishMission(false, now);
                return true;
            }
            _escortSeenAlive = true;

            int hp = Mathf.RoundToInt(unit.GetHealthFraction() * 100f);
            if (hp != _objectivesDone) { _objectivesDone = hp; _cntChanged = true; }

            if (unit.IsArrived())
            {
                int extra = Mathf.RoundToInt(EscortBonus * unit.GetHealthFraction() * GetPhaseMultiplier(_phaseIndex));
                Debug.Log("[ACO] 호위 성공: 체력 " + hp + "%, 도착 보너스 " + extra);
                ScheduleEscortRelease(now); // 투하 후 계속 비행/착륙 정지 연출이 끝나면 거둔다
                EndPhase(now, false, false, extra);
                return true;
            }

            // 요격 편대: 진행률 5% → 80% 사이에 고르게, 수송기보다 앞쪽 항로 위에서 기다린다.
            int waves = Mathf.Max(EscortInterceptWaves, 0);
            if (_escortWaves < waves)
            {
                float threshold = 0.05f + 0.75f * _escortWaves / Mathf.Max(waves - 1, 1);
                float progress = unit.GetProgress();
                if (progress >= threshold)
                {
                    Vector3 ahead = unit.PointAtProgress(progress + EscortInterceptLead);
                    int count = EscortInterceptBase + EscortInterceptPerPhase * _phaseIndex;
                    Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, 0, count, -1, ahead, EscortInterceptDistanceMin, EscortInterceptDistanceMax);
                    _escortWaves++;
                    _lastProgressMs = now;
                    _cntChanged = true;
                    Debug.Log("[ACO] 호위 요격 편대 " + _escortWaves + "/" + waves + ": " + count + "기");
                }
            }
            return false;
        }

        /// 지상 호위: 태그가 붙은 차량이 모두 도착(또는 파괴)하면 끝. 하나라도 도착하면 클리어(살아남은 비율만큼 보너스), 전멸이면 보너스 없이.
        private bool UpdateConvoy(int now)
        {
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            if (_convoySeenAlive.Length != n) { _convoySeenAlive = new bool[n]; }
            int settleMs = Mathf.RoundToInt(ObjectiveSettleTime * 1000f);
            int total = 0;
            int alive = 0;
            int arrived = 0;
            bool pending = false;
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != _activeTag) { continue; }
                ACO_EscortUnit unit = Generator.GetEscort(i);
                if (unit == null) { continue; }
                total++;
                if (unit.IsDestroyed())
                {
                    // 부활 동기화 전의 체력 0은 파괴로 보지 않는다.
                    if (!_convoySeenAlive[i] && now - Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_ESCORT, i) < settleMs) { pending = true; alive++; }
                    continue;
                }
                _convoySeenAlive[i] = true;
                alive++;
                if (unit.IsArrived()) { arrived++; }
            }
            if (total == 0)
            {
                if (now - _phaseStartMs >= Mathf.RoundToInt(DeployTimeout * 1000f))
                {
                    return ReplacePhase(now, "콘보이가 배치되지 않음(지상 호위 대상 풀/지상 항로)");
                }
                return false;
            }
            if (alive != _objectivesDone || total != _objectiveTotal)
            {
                if (alive < _objectivesDone) { _lastProgressMs = now; }
                _objectivesDone = alive;
                _objectiveTotal = total;
                _cntChanged = true;
            }
            if (pending) { return false; }

            if (alive == 0)
            {
                Debug.Log("[ACO] 지상 호위 실패: 콘보이 전멸 — 미션 실패");
                _failReason = FAIL_CONVOY_LOST;
                FinishMission(false, now);
                return true;
            }
            if (arrived >= alive)
            {
                float survived = alive / (float)total;
                int extra = Mathf.RoundToInt(ConvoyBonus * survived * GetPhaseMultiplier(_phaseIndex));
                Debug.Log("[ACO] 지상 호위 성공: " + alive + "/" + total + "대 도착, 보너스 " + extra);
                ScheduleEscortRelease(now); // 정차한 채로 잠시 뒀다가 거둔다
                EndPhase(now, false, false, extra);
                return true;
            }
            return false;
        }

        private int InterceptBomberCount(int phaseIndex)
        {
            return Mathf.Max(InterceptBombersBase + Mathf.FloorToInt(InterceptBombersPerPhase * Mathf.Max(phaseIndex, 0)), 1);
        }

        /// 폭격기 요격: 지금 태그의 폭격기를 센다. 한 대라도 도시 상공(항로 끝)에 닿으면 미션 실패, 모두 격추하면 클리어.
        /// 처음 폭격기가 보이면 그 앞쪽에 적 호위 전투기 편대를 한 번 보낸다(태그 없음 — 목표 아님).
        private bool UpdateIntercept(int now)
        {
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            if (_convoySeenAlive.Length != n) { _convoySeenAlive = new bool[n]; }
            int settleMs = Mathf.RoundToInt(ObjectiveSettleTime * 1000f);
            int total = 0;
            int down = 0;
            bool pending = false;
            ACO_EscortUnit lead = null;
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != _activeTag) { continue; }
                ACO_EscortUnit unit = Generator.GetEscort(i);
                if (unit == null) { continue; }
                total++;
                if (unit.IsDestroyed())
                {
                    // 부활 동기화 전의 체력 0은 격추로 보지 않는다.
                    if (!_convoySeenAlive[i] && now - Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_ESCORT, i) < settleMs) { pending = true; }
                    else { down++; }
                    continue;
                }
                _convoySeenAlive[i] = true;
                if (lead == null) { lead = unit; }
                if (unit.IsArrived())
                {
                    Debug.Log("[ACO] 요격 실패: 폭격기가 도시 상공에 도달 — 미션 실패");
                    _failReason = FAIL_CITY_BOMBED;
                    ScheduleEscortRelease(now); // 투하 후 계속 비행하다 거둔다
                    FinishMission(false, now);
                    return true;
                }
            }
            if (total == 0)
            {
                if (now - _phaseStartMs >= Mathf.RoundToInt(DeployTimeout * 1000f))
                {
                    return ReplacePhase(now, "폭격기가 배치되지 않음(적 폭격기 풀/항로)");
                }
                return false;
            }
            if (down != _objectivesDone || total != _objectiveTotal)
            {
                if (down > _objectivesDone) { _lastProgressMs = now; }
                _objectivesDone = down;
                _objectiveTotal = total;
                _cntChanged = true;
            }
            // 호위 전투기: 선두 폭격기 둘레에서 한 번
            if (_escortWaves == 0 && lead != null)
            {
                int escorts = InterceptEscortsBase + InterceptEscortsPerPhase * _phaseIndex;
                if (escorts > 0)
                {
                    Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, 0, escorts, -1, lead.transform.position, 300f, 900f);
                    Debug.Log("[ACO] 폭격기 호위 편대: " + escorts + "기");
                }
                _escortWaves = 1;
                _cntChanged = true;
            }
            if (pending) { return false; }
            if (down >= total)
            {
                Debug.Log("[ACO] 요격 성공: 폭격기 " + total + "대 전부 격추");
                EndPhase(now, false, false, 0);
                return true;
            }
            return false;
        }

        /// 요격 페이즈 UI: 남은 폭격기 수 / 전체, 도시까지 가장 가까운 폭격기의 남은 거리(m, 없으면 -1).
        public int GetBombersAlive() { return CountBombers(true); }
        public int GetBombersTotal() { return CountBombers(false); }
        public float GetBomberNearestDistance()
        {
            if (Generator == null || GetCurrentPhaseType() != PHASE_INTERCEPT || !_phaseActive) { return -1f; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            float best = -1f;
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != _activeTag) { continue; }
                ACO_EscortUnit unit = Generator.GetEscort(i);
                if (unit == null || unit.IsDestroyed()) { continue; }
                float d = unit.GetRemainingDistance();
                if (best < 0f || d < best) { best = d; }
            }
            return best;
        }

        private int CountBombers(bool aliveOnly)
        {
            if (Generator == null || GetCurrentPhaseType() != PHASE_INTERCEPT || !_phaseActive) { return 0; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != _activeTag) { continue; }
                ACO_EscortUnit unit = Generator.GetEscort(i);
                if (unit == null) { continue; }
                if (!aliveOnly || !unit.IsDestroyed()) { c++; }
            }
            return c;
        }

        /// 미션 실패 사유(FAIL_TIME = 제한 시간, FAIL_CITY_BOMBED = 폭격기가 도시에 도달, FAIL_VIP_LOST = 호위 대상 격추,
        /// FAIL_CONVOY_LOST = 콘보이 전멸, FAIL_TARGET_ESCAPED = 시한 표적 도주).
        public int GetFailReason() { return _failReason; }

        /// 실패 사유 한 줄(영문 — 기본 TMP 폰트). long = 점수 HUD 배너용, 아니면 작전판용 짧은 말.
        public string GetFailReasonText(bool longText)
        {
            if (_failReason == FAIL_CITY_BOMBED) { return longText ? "THE CITY HAS BEEN BOMBED" : "CITY BOMBED"; }
            if (_failReason == FAIL_VIP_LOST) { return longText ? "ESCORT TARGET SHOT DOWN" : "VIP LOST"; }
            if (_failReason == FAIL_CONVOY_LOST) { return longText ? "CONVOY DESTROYED" : "CONVOY LOST"; }
            if (_failReason == FAIL_TARGET_ESCAPED) { return longText ? "TIME-SENSITIVE TARGET ESCAPED" : "TARGET ESCAPED"; }
            return "TIME UP";
        }

        /// 지금 지상 호위 차량 중 살아 있는 수 / 전체(UI용 — 모든 클라이언트에서 같은 답).
        public int GetConvoyAlive() { return CountConvoy(true); }
        public int GetConvoyTotal() { return CountConvoy(false); }

        private int CountConvoy(bool aliveOnly)
        {
            if (Generator == null || GetCurrentPhaseType() != PHASE_CONVOY || !_phaseActive) { return 0; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != _activeTag) { continue; }
                ACO_EscortUnit unit = Generator.GetEscort(i);
                if (unit == null) { continue; }
                if (aliveOnly && unit.IsDestroyed()) { continue; }
                c++;
            }
            return c;
        }

        /// 지상 호위: 살아 있는 차량 중 가장 뒤처진 차량의 남은 거리(m) — 전원이 도착해야 끝나므로. 없으면 -1.
        public float GetConvoyRemainingDistance()
        {
            if (Generator == null || GetCurrentPhaseType() != PHASE_CONVOY || !_phaseActive) { return -1f; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            float worst = -1f;
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != _activeTag) { continue; }
                ACO_EscortUnit unit = Generator.GetEscort(i);
                if (unit == null || unit.IsDestroyed()) { continue; }
                worst = Mathf.Max(worst, unit.GetRemainingDistance());
            }
            return worst;
        }

        /// 태그가 tag인(배치된) 호위 대상 인덱스, 없으면 -1.
        private int FindEscortIndex(int tag)
        {
            if (Generator == null || tag == 0) { return -1; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            for (int i = 0; i < n; i++)
            {
                if (Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) && Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) == tag) { return i; }
            }
            return -1;
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
        /// failed는 이제 보너스 페이즈 시간 초과(슈퍼에이스가 물러남)뿐이다 — 보너스 없이 미션 클리어. 실패 조건이 있는 페이즈의 실패는 FinishMission(false).
        private void EndPhase(int now, bool skipped, bool failed, int extraBonus)
        {
            float mult = GetPhaseMultiplier(_phaseIndex);
            int bonus = 0;
            _lastPhaseFailed = failed;
            if (GetCurrentPhaseType() == PHASE_TST)
            {
                // 시한 표적은 실패해도 잡은 수만큼, 하나도 놓치지 않으면 클리어 보너스까지(배치 실패로 건너뛴 표적은 놓친 것으로 치지 않는다).
                bonus = Mathf.RoundToInt(_tstHits * TstBonus * mult);
                if (!failed && !skipped) { bonus += Mathf.RoundToInt(PhaseClearBonus * mult); }
            }
            else if (!failed && !skipped)
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
                    if (_phaseCount >= BonusMinPhases && _phaseCount <= MAX_PHASES && Random.value < BonusPhaseChance
                        && Generator != null && Generator.CanDeploySuperAce())
                    {
                        _bonusIndex = _phaseCount;
                        _phaseCount++;
                        _mainClearMs = now;
                        if (_mainClearMs == 0) { _mainClearMs = 1; }
                        _nextPhaseMs = now + Mathf.RoundToInt(PhaseTransitionDelay * 1000f);
                        Debug.Log("[ACO] 보너스 페이즈가 붙었습니다: 슈퍼에이스 " + Generator.GetSuperAceName());
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
        public int GetActiveObjectiveTag() { return GetCurrentPhaseType() == PHASE_QUOTA ? 0 : _activeTag; }
        /// 다음 페이즈까지 남은 시간(초). 페이즈 진행 중이면 0.
        public float GetNextPhaseSeconds()
        {
            if (_phaseActive || _state != STATE_RUNNING) { return 0f; }
            return Mathf.Max((_nextPhaseMs - Networking.GetServerTimeInMilliseconds()) * 0.001f, 0f);
        }

        public int GetTstHits() { return _tstHits; }
        public int GetTstCount() { return Mathf.Max(TstCount, 1); }
        /// 지금 시한 표적의 남은 시간(초). 아직 안 보였으면 -1.
        public float GetTstRemainingSeconds()
        {
            if (_tstDeadlineMs == 0 || !_phaseActive) { return -1f; }
            return Mathf.Max((_tstDeadlineMs - Networking.GetServerTimeInMilliseconds()) * 0.001f, 0f);
        }

        /// 마지막으로 끝난 페이즈가 실패였는지(호위 대상 격추 — 보너스 없음, 시한 표적 도주 — 잡은 수만큼만).
        public bool GetLastPhaseFailed() { return _lastPhaseFailed; }

        /// HUD 항로 표시용: 지금 공중/지상 호위 페이즈의 살아 있는 호위 대상 하나(콘보이는 모두 같은 항로). 없으면 null.
        public ACO_EscortUnit GetRouteEscort()
        {
            int type = GetCurrentPhaseType();
            if (!_phaseActive || Generator == null || (type != PHASE_ESCORT && type != PHASE_CONVOY && type != PHASE_INTERCEPT)) { return null; }
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_ESCORT);
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_ESCORT, i) != _activeTag) { continue; }
                ACO_EscortUnit unit = Generator.GetEscort(i);
                if (unit != null && !unit.IsDestroyed()) { return unit; }
            }
            return null;
        }

        /// 지금 호위 대상(없으면 null). 모든 클라이언트에서 같은 답(동기화된 태그).
        public ACO_EscortUnit GetActiveEscort()
        {
            if (GetCurrentPhaseType() != PHASE_ESCORT || !_phaseActive) { return null; }
            int idx = FindEscortIndex(_activeTag);
            return idx >= 0 ? Generator.GetEscort(idx) : null;
        }

        public string GetBossName()
        {
            if (BossNames == null || _bossName < 0 || _bossName >= BossNames.Length) { return "ACE"; }
            return BossNames[_bossName];
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
            if (type == PHASE_STRIKE) { return "STRIKE"; }
            if (type == PHASE_SEAD) { return "SEAD"; }
            if (type == PHASE_QUOTA) { return "SCORE ATTACK"; }
            if (type == PHASE_TST) { return "TIME SENSITIVE"; }
            if (type == PHASE_AIR) { return "AIR SUPERIORITY"; }
            if (type == PHASE_BOSS) { return "ACE SQUADRON"; }
            if (type == PHASE_ESCORT) { return "ESCORT"; }
            if (type == PHASE_CONVOY) { return "CONVOY"; }
            if (type == PHASE_INTERCEPT) { return "INTERCEPT"; }
            if (type == PHASE_BONUS) { return "BONUS - " + (Generator != null ? Generator.GetSuperAceName() : "ACE"); }
            return "-";
        }

        /// 페이즈 안내 문구(영문, HUD 배너용).
        /// 지금 페이즈의 작전 지역("AO: 이름") — 호위는 목적지("DEST: 이름"). 지형 장식과 관계없으면 "".
        public string GetPhaseLocation()
        {
            if (!_phaseActive || _phaseSite < 0 || Generator == null) { return ""; }
            string name = Generator.GetSiteName(_phaseSite);
            if (name.Length == 0) { return ""; }
            int type = GetCurrentPhaseType();
            if (type == PHASE_INTERCEPT) { return "DEFEND: " + name; }
            return (type == PHASE_ESCORT || type == PHASE_CONVOY ? "DEST: " : "AO: ") + name;
        }

        public string GetPhaseDescription(int type)
        {
            if (type == PHASE_STRIKE) { return "DESTROY THE HIGH VALUE TARGETS"; }
            if (type == PHASE_SEAD) { return "SUPPRESS ENEMY AIR DEFENSES"; }
            if (type == PHASE_QUOTA) { return "EARN " + _objectiveTotal.ToString("N0") + " POINTS"; }
            if (type == PHASE_TST) { return "DESTROY " + GetTstCount() + " TARGETS BEFORE THEY ESCAPE"; }
            if (type == PHASE_AIR) { return "SHOOT DOWN THE ENEMY FLIGHT"; }
            if (type == PHASE_BOSS) { return "SHOOT DOWN " + GetBossName() + " SQUADRON"; }
            if (type == PHASE_ESCORT) { return "ESCORT THE TRANSPORT TO THE EXTRACTION POINT"; }
            if (type == PHASE_CONVOY) { return "PROTECT THE CONVOY FROM ENEMY ARMOR"; }
            if (type == PHASE_INTERCEPT) { return "SHOOT DOWN ALL BOMBERS BEFORE THEY REACH THE CITY"; }
            if (type == PHASE_BONUS) { return "SHOOT DOWN " + (Generator != null ? Generator.GetSuperAceName() : "THE ACE") + " BEFORE IT ESCAPES (" + Mathf.RoundToInt(BonusTimeLimit) + "s)"; }
            return "";
        }

        /// 전체 제한 시간(ms). 0 = 없음.
        public int GetTimeLimitMs()
        {
            if (TimeLimitPerPhase <= 0f) { return 0; }
            int mainPhases = _bonusIndex >= 0 ? _phaseCount - 1 : _phaseCount;
            return Mathf.RoundToInt(TimeLimitPerPhase * 1000f) * Mathf.Max(mainPhases, 1);
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
                return Mathf.Max(BonusTimeLimit - (Networking.GetServerTimeInMilliseconds() - _phaseStartMs) * 0.001f, 0f);
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
