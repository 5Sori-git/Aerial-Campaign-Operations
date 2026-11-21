using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 잠수함 보스 페이즈(모듈) — 모항 방어(설계 6-202). 보스 ACO_Submarine(LEVIATHAN, 생성기 풀 NPC_Submarine).
    ///  1페이즈(SLCM): 14~20km 바다에서 SLCM 일제 발사 SalvoCount번(한 번에 SalvoBase + 인원 × SalvoPerPlayer발). 날아오는 무리는 늘 하나 — 앞 일제 발사가 모두
    ///     정리되면 SalvoGap초 뒤에 다음. 발사 자리는 앞 자리에서 SalvoMoveMin~Max 안(잠수함이 물속에서 옮겨 간다).
    ///     끝까지 날아온 미사일은 항모 체력 −SlcmDamage. 모두 정리(격추 · 명중)되면 2페이즈.
    ///  2페이즈 A(간헐 부상): 잠항 SubmergedTime → 부상 RiseTime → 수상 SurfaceTime → 잠항 DiveTime을 되풀이. 부상할 때마다 무장 부위를 모두 되살리고,
    ///     잠항할 때마다 어뢰 DiveTorpedoCount발(막을 수 없음). 부상 SonarLead초 전부터 소나 원(반경 SonarRadius, 실제 자리는 원 안).
    ///  2페이즈 B(완전 부상 — 충각 돌격, 6-203): 선체가 FinalRatio 아래가 되면 무장을 되살리고 물 위에 머물며 항모 뱃전으로 돌진한다(RamTime초 뒤 닿는다 — 거리와 상관없이).
    ///     닿으면 항모 격침 = 미션 실패. 어뢰는 쏘지 않고, RamRepairInterval마다 부서진 무장 중 무작위 RamRepairCount문을 되살린다(6-224 — 돌격을 시작할 때만 모두). 순양함을 스치는 길이면 옆으로 비켜 돈다.
    ///     돌격 길이 뭍에 막히면 대비책: FinalSpeed로 와서 FinalStopDistance에서 멈추고 FinalTorpedoInterval마다 어뢰 FinalTorpedoCount발.
    ///  성공: 선체 격침. 실패: 항모 체력 0(CARRIER LOST).
    ///
    /// 자체 동기화(Manual): 단계, 일제 발사 계획(시각 · 자리 · 발 수), 정리된 SLCM, 항모 체력, 부상 주기 · 자리 · 소나 원, 완전 부상 출발 · 도착, 어뢰 기록.
    /// 위치 자체는 보내지 않는다(모두 시간 함수). 판정(명중 · 수리 · 다음 주기 · 어뢰 피해)은 관리자 오너만. 페이즈 도장으로 지난 값을 무시한다.
    /// 대사: 상황별 대사 표(LineEvent · LineSpeaker · LineText · LineClip). ACO_Submarine이 0.5초마다 CheckLines.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseSubmarine : ACO_PhaseBoss
    {
        /// 보스 칭호 · 기록 ID(6-341).
        public override string GetAchievementId() { return "submarine"; }

        public const int MAX_SALVO = 6;
        public const int MAX_TORPEDO = 4;

        [Header("모항 (Bake가 HomeBase를 넣는다)")]
        public Transform HomeBase;
        [Tooltip("Bake가 넣는다 — 참가 인원(일제 발사 발 수)")]
        public ACO_GameFlow Flow;
        [Tooltip("항모 선체 가운데(HomeBase 기준)")]
        public Vector3 CarrierCenter = new Vector3(0f, 0f, 7.6f);
        [Tooltip("선체 반폭 · 반길이(m) — 명중 자리는 이 타원 위(들어오는 쪽 뱃전)")]
        public float CarrierHalfWidth = 17f;
        public float CarrierHalfLength = 145f;
        [Tooltip("명중 높이(m, 해수면 기준)")]
        public float ImpactHeight = 8f;

        [Header("1페이즈 — SLCM")]
        [Range(1, MAX_SALVO)] public int SalvoCount = 4;
        [Tooltip("페이즈 시작부터 첫 발사까지(초) — 멀리 있던 조종사가 돌아올 시간")]
        public float FirstLaunchDelay = 40f;
        [Tooltip("앞 일제 발사가 모두 정리(격추 · 명중)된 뒤 다음 발사까지(초) — 날아오는 무리는 늘 하나")]
        public float SalvoGap = 12f;
        [Tooltip("한 번에 쏘는 발 수 = SalvoBase + 참가 인원 × SalvoPerPlayer(최대 SalvoMax, 잠수함 풀 칸 수)")]
        public int SalvoBase = 2;
        public int SalvoPerPlayer = 1;
        public int SalvoMax = 6;
        [Tooltip("발사 거리(m, 항모 가운데에서)")]
        public float LaunchDistanceMin = 14000f;
        public float LaunchDistanceMax = 20000f;
        [Tooltip("다음 발사 자리는 앞 발사 자리에서 이 거리(m) 안 — 잠수함이 물속에서 옮겨 가는 거리")]
        public float SalvoMoveMin = 2000f;
        public float SalvoMoveMax = 5000f;
        [Tooltip("발사 자리에서 항모까지 바다인지 재는 간격(m) — 항모 이 거리(LaneSkip) 안은 재지 않는다(선체가 지면으로 잡힌다)")]
        public float LaneStep = 250f;
        public float LaneSkip = 1500f;
        [Tooltip("한 일제 발사 안의 발사 간격(초) · 좌우 간격(m)")]
        public float SalvoStagger = 0.5f;
        public float SalvoSpread = 80f;
        [Tooltip("SLCM 하나가 명중하면 깎이는 항모 체력(%)")]
        public int SlcmDamage = 12;
        [Tooltip("명중 판정 유예(초) — 마지막 순간 격추의 피해 이벤트가 늦게 올 수 있다")]
        public float HitGrace = 0.3f;
        [Tooltip("발사 몇 초 전부터 풀 칸에 넣는가(초) · 체력을 채우는가(초)")]
        public float ArmLead = 10f;
        public float ReviveLead = 8f;
        [Tooltip("SLCM이 모두 정리된 뒤 2페이즈까지(초) — 대사")]
        public float PhaseTwoDelay = 10f;
        [Tooltip("2페이즈는 SLCM 소진 대사(적 · AWACS · 항모 · 승무원)가 다 끝나고 이 시간(초) 뒤에 — 대사가 길면 PhaseTwoDelay보다 늦게(최대 PhaseTwoMax)")]
        public float PhaseTwoGap = 1.5f;
        public float PhaseTwoMax = 40f;

        [Header("2페이즈 A — 간헐 부상")]
        public float SubmergedTime = 15f;   // 6-207: 25 → 15(소나 원은 그 마지막 SonarLead초)
        public float RiseTime = 5f;
        public float SurfaceTime = 45f;
        public float DiveTime = 5f;
        [Tooltip("부상할 때 되살리는 무장 수(부서진 것 중 무작위, 0 = 모두) — 충각 돌격의 수리는 모두")]
        public int SurfaceRepairCount = 3;
        [Tooltip("부상 몇 초 전부터 소나 원을 보이는가")]
        public float SonarLead = 10f;
        public float SonarRadius = 1500f;
        [Tooltip("부상 자리(m, 항모 가운데에서)")]
        public float SurfaceDistanceMin = 6000f;
        public float SurfaceDistanceMax = 14000f;
        [Tooltip("다음 부상 자리는 직전 부상 자리에서 SurfaceSpacing~SurfaceMoveMax(m) — 잠수함이 물속에서 옮겨 가는 거리. 첫 부상은 마지막 SLCM 발사 자리 근처")]
        public float SurfaceSpacing = 3000f;
        public float SurfaceMoveMax = 6000f;
        [Tooltip("부상 자리 둘레 이 반경(m)이 바다여야 한다")]
        public float SurfaceSeaRadius = 300f;
        [Tooltip("다 떠오른 뒤 무장을 되살리기까지(초)")]
        public float RepairDelay = 0.5f;
        [Tooltip("잠항할 때 쏘는 어뢰 수")]
        public int DiveTorpedoCount = 1;

        [Header("2페이즈 A — 전투 무인기(6-217)")]
        [Tooltip("부상할 때마다 사출하는 무인기 수(사출구 수 MAX_DRONE_HATCH까지) — 0이면 사출하지 않는다")]
        public int DronesPerSurface = 2;
        [Tooltip("떠 있는 무인기가 이 수 이상이면 그 부상엔 사출하지 않는다(모자라는 만큼만)")]
        public int DroneMaxAlive = 4;
        [Tooltip("다 떠오른 뒤 사출을 정하기까지 최소(초) — 실제로는 부상 · 수리 대사가 끝나고 DroneLineGap초 뒤에 첫 기가 나가게 늦춘다(6-218)")]
        public float DroneDelay = 3f;
        [Tooltip("부상 · 수리 대사(지금 대사의 음성 길이)가 끝나고 첫 기가 나가기까지(초)")]
        public float DroneLineGap = 1.5f;
        [Tooltip("첫 부상만 더 기다리는 시간(초) — 앞 페이즈 대사(SLCM 소진 · 소나)가 남아 있고 첫 사출 대사도 두 줄이라 밀렸다")]
        public float DroneFirstExtra = 6f;
        [Tooltip("사출구 덮개가 열리기 시작해 첫 기가 나가기까지(초) · 사출 간격(초)")]
        public float DroneDoorLead = 1.2f;
        public float DroneSpacing = 1.5f;

        [Header("2페이즈 B — 완전 부상")]
        [Tooltip("선체 체력이 이 비율 아래가 되면 완전 부상")]
        [Range(0.05f, 0.95f)] public float FinalRatio = 0.3f;   // 6-208: 20 → 30%
        [Tooltip("충각 돌격(6-203): 완전 부상부터 항모 뱃전에 닿기까지(초) — 거리와 상관없이 같다(속도를 거리에 맞춘다). 닿으면 항모 격침 = 미션 실패")]
        public float RamTime = 150f;
        [Tooltip("돌격 처음 이만큼(초) 동안 가속하며 항모 쪽으로 돈다")]
        public float RamAccelTime = 15f;
        [Tooltip("닿는 판정 유예(초) — 마지막 순간 격침의 피해 이벤트가 늦게 올 수 있다")]
        public float RamGrace = 0.5f;
        [Tooltip("돌격 중 부서진 무장을 되살리는 간격(초)")]
        public float RamRepairInterval = 30f;
        [Tooltip("돌격 중 한 번에 되살리는 무장 수(무작위, 0 이하 = 모두 — 6-224). 돌격을 시작할 때는 모두 되살린다")]
        public int RamRepairCount = 3;
        [Tooltip("항모에서 이 거리(m) 안이면 접근 경고 대사")]
        public float RamWarnDistance = 2000f;
        [Tooltip("돌격 길이 아군 요격 CIWS(순양함)에서 이 거리(m) 안을 지나면 옆으로 RamDetour(m) 비켜 도는 경유점을 넣는다")]
        public float RamAvoidRadius = 250f;
        public float RamDetour = 400f;
        [Header("2페이즈 B — 돌격 길이 뭍에 막혔을 때(대비책: 예전 방식 — 멈추고 어뢰)")]
        public float FinalSpeed = 15f;
        [Tooltip("항모 가운데에서 이 거리(m)에서 멈춘다 — 순양함 VLS 사거리 밖")]
        public float FinalStopDistance = 5000f;
        [Tooltip("가는 길에 뭍이 있으면 이만큼(m) 앞에서 멈춘다")]
        public float FinalLandMargin = 400f;
        public float FinalFirstTorpedo = 10f;
        public float FinalTorpedoInterval = 40f;
        public int FinalTorpedoCount = 2;

        [Header("어뢰")]
        [Tooltip("어뢰 속도(m/s) — 초공동 어뢰처럼 빠르게(6-212: 150 → 500, 6~14km면 12~28초 뒤 명중). 항적은 프리팹이 이 속도에 맞춰져 있다(ACO_FxBuilder.TorpedoWakeSpeed)")]
        public float TorpedoSpeed = 500f;
        [Tooltip("어뢰 한 발이 명중하면 깎이는 항모 체력(%)")]
        public int TorpedoDamage = 6;

        [Header("끝맺음")]
        [Tooltip("이 페이즈가 시작될 때 미션 제한 시간에 더하는 시간(초) — SLCM 단계만 약 6분 30초(한 무리씩), 간헐 부상 1~3주기, 돌격 2분 30초")]
        public float ExtraTime = 0f;   // 2026-11-20 → 0(사용자 "페이즈당 시간이 이미 있으니 호위 · 시한 있는 임무 빼곤 다 빼자" — 레이저 에이스 · LUNA만 남김)
        [Tooltip("격침 뒤 다 가라앉아(모델이 꺼진 뒤) 이 시간(초) 있다가 성공 — 그전에 풀로 거두면 물 위에서 사라졌다")]
        public float ClearDelay = 2f;
        [Tooltip("다 가라앉았다는 걸 못 봐도 격침 뒤 이 시간(초)이면 성공")]
        public float ClearMaxWait = 35f;
        public float FailDelay = 4f;
        [Tooltip("이 체력(%) 아래면 항모 위기 대사")]
        public int CarrierLowAt = 30;

        public const int SIT_START = 0;
        public const int SIT_SALVO = 1;
        public const int SIT_SPRINT = 2;
        public const int SIT_CARRIER_HIT = 3;
        public const int SIT_CARRIER_LOW = 4;
        public const int SIT_SLCM_OUT = 5;
        public const int SIT_SONAR = 6;
        public const int SIT_SURFACE = 7;
        public const int SIT_REPAIR = 8;
        public const int SIT_DIVE = 9;
        public const int SIT_FINAL = 10;
        public const int SIT_TORPEDO = 11;
        public const int SIT_SUNK = 12;
        public const int SIT_CARRIER_LOST = 13;
        public const int SIT_RAM_NEAR = 14;      // 돌격이 RamWarnDistance 안
        public const int SIT_RAM_HIT = 15;       // 충돌(항모 격침)
        public const int SIT_RAM_REPAIR = 16;    // 돌격 중 무장 수리
        public const int SIT_FIRST_SALVO = 17;   // 첫 일제 발사(6-211 — 표에 없으면 SIT_SALVO)
        public const int SIT_DRONE_FIRST = 18;   // 첫 무인기 사출(6-217 — 표에 없으면 SIT_DRONE)
        public const int SIT_DRONE = 19;         // 그 뒤 무인기 사출
        public const int MAX_DRONE_HATCH = 2;    // 사출구 수(잠수함 프리팹 DroneLaunchPoints와 같다)

        [Header("상황별 대사 표 (같은 순서의 배열 — 한 줄 = 대사 하나)")]
        [Tooltip("이 대사가 나오는 상황(SIT_*)")]
        public int[] LineEvent = new int[0];
        [Tooltip("말하는 쪽(0 = 적 함장, 1 = AWACS, 2 = 항모)")]
        public int[] LineSpeaker = new int[0];
        [Tooltip("대사. {left} = 방위(일제 발사 대사)")]
        public string[] LineText = new string[0];
        [Tooltip("음성(선택 — 비우면 자막만)")]
        public AudioClip[] LineClip = new AudioClip[0];

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _stage;
        [UdonSynced] private int _salvoCount;
        [UdonSynced] private int[] _salvoMs = new int[MAX_SALVO];
        [UdonSynced] private Vector3[] _salvoFrom = new Vector3[MAX_SALVO];
        [UdonSynced] private int[] _salvoSize = new int[MAX_SALVO];
        [UdonSynced] private int _revived;                              // 체력을 채운 일제 발사(비트)
        [UdonSynced] private int[] _slcmDone = new int[MAX_SALVO];      // 정리된 미사일(일제 발사마다 비트)
        [UdonSynced] private int[] _slcmHit = new int[MAX_SALVO];       // 그중 명중
        [UdonSynced] private int _slcmOutMs;                            // 모두 정리된 시각(0 = 아직)
        [UdonSynced] private int _carrierHp = 100;
        [UdonSynced] private int _carrierHits;
        [UdonSynced] private int _cycleMs;
        [UdonSynced] private int _cycleIndex;
        [UdonSynced] private Vector3 _spot;
        [UdonSynced] private float _cycleYaw;
        [UdonSynced] private Vector3 _sonarCenter;
        [UdonSynced] private int _repairedCycle = -1;
        [UdonSynced] private int _repairCount;
        [UdonSynced] private int _torpCycle = -1;
        [UdonSynced] private int _finalMs;
        [UdonSynced] private Vector3 _finalFrom;
        [UdonSynced] private Vector3 _finalTo;
        [UdonSynced] private float _finalYaw0;
        [UdonSynced] private float _finalYaw1;
        [UdonSynced] private int _nextTorpMs;
        [UdonSynced] private bool _ram;              // 충각 돌격 중(아니면 대비책 — 멈추고 어뢰)
        [UdonSynced] private Vector3 _ramVia;        // 경유점(없으면 출발점과 같다)
        [UdonSynced] private bool _rammed;           // 항모에 닿았다
        [UdonSynced] private int _nextRepairMs;
        [UdonSynced] private int[] _torpMs = new int[MAX_TORPEDO];
        [UdonSynced] private Vector3[] _torpFrom = new Vector3[MAX_TORPEDO];
        [UdonSynced] private int[] _torpNum = new int[MAX_TORPEDO];
        [UdonSynced] private int _torpNext;
        [UdonSynced] private int _torpDone;
        [UdonSynced] private int _droneCycle = -1;                        // 무인기 사출을 정한 주기
        [UdonSynced] private int[] _droneMs = new int[MAX_DRONE_HATCH];   // 사출구마다 이번 사출 시각(0 = 없음) — 덮개 연출도 이 시각으로
        [UdonSynced] private int _droneDone;                              // 이번 주기에 사출한 사출구(비트)
        [UdonSynced] private int _droneWaves;                             // 사출한 무리 수(대사)

        // 오너 지역 상태
        private Vector3[] _plan = new Vector3[MAX_SALVO];
        private int _planCount;
        private int _sunkMs;
        private int _downMs;   // 다 가라앉은 걸 본 시각(오너 화면만 — 동기화하지 않는다)
        private int _lostMs;
        private bool _released;

        // 대사(모든 클라이언트 지역 상태)
        private int _lineStamp;
        private int _saidStage;
        private int _saidSalvos;
        private bool _saidSprint;
        private int _saidHits;
        private float _saidHitAt;
        private bool _saidLow;
        private bool _saidOut;
        private int _saidSonar;
        private int _saidSurface;
        private int _saidRepairs;
        private int _saidTorps;
        private bool _saidLost;
        private bool _saidRamNear;
        private int _saidDroneWaves;

        private ACO_Submarine Sub() { return Generator != null ? Generator.Submarine : null; }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null || !Generator.CanDeploySubmarine()) { return false; }
            return PlanSalvos();
        }

        /// 일제 발사 자리(잠수함이 물속에서 옮겨 다니며 쏜다): 첫 자리 = 항모에서 LaunchDistanceMin~Max 무작위 방위, 그다음은 앞 자리에서 SalvoMoveMin~Max 떨어진 곳 중
        /// 항모에서 LaunchDistanceMin~Max. 모두 항모까지 바다(LaneIsSea). 못 찾으면 거리 범위를 넓혀 다시, 그래도 없으면 첫 자리부터 다시(3번).
        private bool PlanSalvos()
        {
            Vector3 c = CarrierPoint();
            int count = Mathf.Clamp(SalvoCount, 1, MAX_SALVO);
            for (int round = 0; round < 3; round++)
            {
                bool ok = true;
                for (int s = 0; s < count && ok; s++)
                {
                    bool found = false;
                    for (int attempt = 0; attempt < 60 && !found; attempt++)
                    {
                        Vector3 p;
                        if (s == 0)
                        {
                            float far = attempt < 40 ? LaunchDistanceMax : Mathf.Lerp(LaunchDistanceMin, LaunchDistanceMax, 0.3f);
                            float dist = Random.Range(Mathf.Min(LaunchDistanceMin, far), far);
                            p = c + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * dist;
                        }
                        else
                        {
                            float move = Random.Range(SalvoMoveMin, SalvoMoveMax);
                            p = _plan[s - 1] + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * move;
                            float dc = new Vector2(p.x - c.x, p.z - c.z).magnitude;
                            float slack = attempt < 40 ? 0f : 0.15f;   // 뒤쪽 시도는 거리 범위를 넓힌다
                            if (dc < LaunchDistanceMin * (1f - slack) || dc > LaunchDistanceMax * (1f + slack)) { continue; }
                        }
                        if (!LaneIsSea(p, c)) { continue; }
                        _plan[s] = new Vector3(p.x, 0f, p.z);
                        found = true;
                    }
                    if (!found) { ok = false; }
                }
                if (ok)
                {
                    _planCount = count;
                    return true;
                }
            }
            _planCount = 0;
            return false;
        }

        private bool LaneIsSea(Vector3 from, Vector3 to)
        {
            Vector3 d = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float len = d.magnitude;
            if (len < 1f) { return false; }
            d /= len;
            float step = Mathf.Max(LaneStep, 50f);
            for (float s = 0f; s <= len - LaneSkip; s += step)
            {
                Vector3 p = from + d * s;
                if (!Generator.IsSeaPoint(p.x, p.z)) { return false; }
            }
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 1, false);
            // 늘 지금 지형으로 다시 세운다 — 대체 시작(PickFallbackType)은 IsFeasible을 거치지 않고, 앞서 후보로만 확인한 계획은 이전 지형(지난 미션)의 것일 수 있다.
            // 안 되면 배치하지 않는다 → Tick이 배치 시한 뒤 RESULT_REPLACE.
            if (!IsFeasible(phaseIndex, focus)) { return; }
            Vector3 deep = new Vector3(_plan[0].x, -100f, _plan[0].z);
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_SUBMARINE, tag, 1, 0, deep, 0f, 0f);
            Manager.SetModuleSite(-1);
            Manager.AddModuleTime(ExtraTime);
            StartPlan(now);
        }

        private void StartPlan(int now)
        {
            _stamp = CurrentStamp();
            _stage = ACO_Submarine.STAGE_SLCM;
            int size = SalvoSize();
            _salvoCount = _planCount;
            for (int s = 0; s < MAX_SALVO; s++)
            {
                bool on = s < _planCount;
                // 첫 발사만 정해 둔다 — 다음은 앞 일제 발사가 모두 정리된 뒤 SalvoGap초 뒤(TickSalvos)
                _salvoMs[s] = on && s == 0 ? now + Mathf.RoundToInt(FirstLaunchDelay * 1000f) : 0;
                _salvoFrom[s] = on ? _plan[s] : Vector3.zero;
                _salvoSize[s] = on ? size : 0;
                _slcmDone[s] = 0;
                _slcmHit[s] = 0;
            }
            _revived = 0;
            _slcmOutMs = 0;
            _carrierHp = 100;
            _carrierHits = 0;
            _cycleMs = 0;
            _cycleIndex = 0;
            _spot = Vector3.zero;
            _sonarCenter = Vector3.zero;
            _repairedCycle = -1;
            _repairCount = 0;
            _torpCycle = -1;
            _finalMs = 0;
            _nextTorpMs = 0;
            _ram = false;
            _ramVia = Vector3.zero;
            _rammed = false;
            _nextRepairMs = 0;
            for (int i = 0; i < MAX_TORPEDO; i++) { _torpMs[i] = 0; _torpNum[i] = 0; _torpFrom[i] = Vector3.zero; }
            _torpNext = 0;
            _torpDone = 0;
            _droneCycle = -1;
            for (int k = 0; k < MAX_DRONE_HATCH; k++) { _droneMs[k] = 0; }
            _droneDone = 0;
            _droneWaves = 0;
            _sunkMs = 0;
            _downMs = 0;
            _lostMs = 0;
            _released = false;
            SyncNow();
            Debug.Log("[ACO] 잠수함: SLCM 일제 발사 " + _salvoCount + "번 × " + size + "발 계획");
        }

        private int SalvoSize()
        {
            int players = Flow != null ? Flow.GetRosterCount() : 0;
            if (players <= 0) { players = 1; }
            ACO_Submarine sub = Sub();
            int cap = Mathf.Min(SalvoMax, sub != null ? sub.SalvoSlots : SalvoMax);
            return Mathf.Clamp(SalvoBase + players * SalvoPerPlayer, 1, Mathf.Max(cap, 1));
        }

        public override int Tick(int now)
        {
            ACO_Submarine sub = Sub();
            if (sub == null) { return RESULT_REPLACE; }
            int tag = Manager.GetModuleTag();
            Manager.CountTaggedObjectives(tag);
            int total = Manager.GetTaggedTotal();
            if (_stamp != CurrentStamp())
            {
                if (total == 0) { return now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f) ? RESULT_REPLACE : RESULT_RUNNING; }
                // 이번 페이즈 값을 못 받은 새 오너(드문 경우): 계획을 지금부터 다시 세운다
                if (_planCount == 0) { PlanSalvos(); }
                if (_planCount == 0) { return RESULT_REPLACE; }
                StartPlan(now);
            }

            // 항모 격침(충돌 · SLCM · 어뢰)이 먼저 났으면 그 뒤에 잠수함이 가라앉아도(실패 대기 중 순양함이 격침 등) 실패다.
            // 잠수함이 먼저 가라앉았으면(_sunkMs) 그 뒤로는 판정을 멈추므로 항모 체력이 바뀌지 않는다.
            if (_carrierHp <= 0 && _sunkMs == 0)
            {
                if (_lostMs == 0) { _lostMs = now; }
                bool fail = now - _lostMs >= Mathf.RoundToInt(FailDelay * 1000f);
                if (fail && !_released)
                {
                    _released = true;
                    Generator.RequestReleaseTag(tag);
                }
                return fail ? RESULT_FAIL : RESULT_RUNNING;
            }
            // 격침: 연쇄 폭발 · 침몰이 보이게 기다린다. 기다린 시간은 돌려준다.
            if (_sunkMs != 0 && !(total > 0 && Manager.GetTaggedDone() >= total)) { _sunkMs = 0; _downMs = 0; }   // 6-338: 목표가 다시 살아 있다(확정 전 오판) — 격파 대기 기록을 푼다
            if (total > 0 && Manager.GetTaggedDone() >= total)
            {
                if (_sunkMs == 0) { _sunkMs = now; }
                // 다 가라앉은 뒤(잠수함 모델이 꺼진 뒤) ClearDelay — 오너 화면 기준(오너가 바뀌면 새 오너가 다시 잰다)
                if (_downMs == 0 && sub.IsSunkDone()) { _downMs = now; }
                bool settled = _downMs != 0 && now - _downMs >= Mathf.RoundToInt(ClearDelay * 1000f);
                if (!settled && now - _sunkMs < Mathf.RoundToInt(ClearMaxWait * 1000f)) { return RESULT_RUNNING; }
                Manager.AddModuleTime((now - _sunkMs) * 0.001f);
                Generator.RequestReleaseTag(tag);
                return RESULT_CLEAR;
            }
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            // 배치 직후엔 체력이 부활 동기화 전이라 0으로 보인다 — 그동안 판정하지 않는다
            if (now - Manager.GetPhaseStartMs() < Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f)) { return RESULT_RUNNING; }

            bool changed = false;
            if (TickSalvos(now, sub)) { changed = true; }
            if (TickTorpedoes(now)) { changed = true; }
            if (_stage == ACO_Submarine.STAGE_SLCM)
            {
                if (TickSlcmStage(now)) { changed = true; }
            }
            else if (_stage == ACO_Submarine.STAGE_CYCLE)
            {
                if (TickCycle(now, sub)) { changed = true; }
            }
            else if (_stage == ACO_Submarine.STAGE_FINAL)
            {
                if (TickFinal(now, sub)) { changed = true; }
            }
            if (_stage == ACO_Submarine.STAGE_SLCM) { Manager.SetModuleProgress(CountResolved(), Mathf.Max(CountPlanned(), 1), false); }
            else { Manager.SetModuleProgress(Mathf.RoundToInt((1f - sub.GetHullRatio()) * 100f), 100, false); }
            if (changed) { SyncNow(); }
            return RESULT_RUNNING;
        }

        /// SLCM: 발사 ReviveLead초 전에 체력을 채우고, 떨어졌는지(플레이어면 점수) · 끝까지 날아왔는지(항모 피해) 정리한다.
        private bool TickSalvos(int now, ACO_Submarine sub)
        {
            bool changed = false;
            for (int s = 0; s < _salvoCount; s++)
            {
                int size = _salvoSize[s];
                int bit = 1 << s;
                if (_salvoMs[s] == 0)
                {
                    // 아직 안 정한 발사: 앞 일제 발사가 모두 정리되면 SalvoGap초 뒤로 정한다(날아오는 무리는 늘 하나)
                    if (s > 0 && _salvoMs[s - 1] != 0 && _slcmDone[s - 1] == (1 << _salvoSize[s - 1]) - 1)
                    {
                        _salvoMs[s] = now + Mathf.RoundToInt(SalvoGap * 1000f);
                        changed = true;
                    }
                    continue;
                }
                if ((_revived & bit) == 0 && now - (_salvoMs[s] - Mathf.RoundToInt(ReviveLead * 1000f)) >= 0)
                {
                    _revived |= bit;
                    changed = true;
                    for (int k = 0; k < size; k++)
                    {
                        ACO_Slcm r = sub.GetSlcm(sub.SlotOf(s, k));
                        if (r != null) { r.Revive(); }
                    }
                }
                for (int k = 0; k < size; k++)
                {
                    int kb = 1 << k;
                    if ((_slcmDone[s] & kb) != 0) { continue; }
                    ACO_Slcm m = sub.GetSlcm(sub.SlotOf(s, k));
                    if (m == null)
                    {
                        _slcmDone[s] |= kb;
                        changed = true;
                        continue;
                    }
                    if (m.GetKey() != SlcmKey(s, k)) { continue; }   // 아직 이 칸에 안 들어왔다(0.25초마다)
                    if (m.IsDown())
                    {
                        _slcmDone[s] |= kb;
                        changed = true;
                        if (m.KilledByPlayer() && Manager != null) { Manager.AddModuleScore(m.ScoreValue); }
                        continue;
                    }
                    if (now - (m.GetArrivalMs() + Mathf.RoundToInt(HitGrace * 1000f)) < 0) { continue; }
                    _slcmDone[s] |= kb;
                    _slcmHit[s] |= kb;
                    _carrierHp = Mathf.Max(_carrierHp - SlcmDamage, 0);
                    _carrierHits++;
                    changed = true;
                    Debug.Log("[ACO] 잠수함: SLCM 명중 — 항모 " + _carrierHp + "%");
                }
            }
            return changed;
        }

        private bool TickSlcmStage(int now)
        {
            if (CountResolved() < CountPlanned()) { return false; }
            if (_slcmOutMs == 0)
            {
                _slcmOutMs = now;
                Debug.Log("[ACO] 잠수함: SLCM 소진 — 항모 " + _carrierHp + "%");
                return true;
            }
            // 첫 부상은 SLCM 소진 대사가 끝난 뒤에(6-210 — 10초 고정이면 소나 · 부상 대사가 4줄 대사 뒤로 밀려 버려졌다). 판정은 관리자 오너 화면의 대사 길이
            float wait = Mathf.Clamp(TableTime(SIT_SLCM_OUT, LineEvent, LineText, LineClip) + PhaseTwoGap, PhaseTwoDelay, Mathf.Max(PhaseTwoMax, PhaseTwoDelay));
            if (now - _slcmOutMs < Mathf.RoundToInt(wait * 1000f)) { return false; }
            _stage = ACO_Submarine.STAGE_CYCLE;
            _cycleIndex = 0;
            // 첫 부상은 마지막 SLCM 발사 자리 근처 — 발사 자리(14~20km)는 부상 범위(6~14km)보다 멀어 그만큼 더 들어올 수 있게 한다
            Vector3 lastLaunch = _salvoFrom[Mathf.Clamp(_salvoCount - 1, 0, MAX_SALVO - 1)];
            Vector3 cc = CarrierPoint();
            float over = Mathf.Max(new Vector2(lastLaunch.x - cc.x, lastLaunch.z - cc.z).magnitude - SurfaceDistanceMax, 0f);
            PickSpot(lastLaunch, over, SurfaceMoveMax + over);
            // 첫 주기는 소나 원이 곧 나오게(잠항 시간을 거의 건너뛴다)
            _cycleMs = now - Mathf.RoundToInt(Mathf.Max(SubmergedTime - SonarLead - 1f, 0f) * 1000f);
            return true;
        }

        private bool TickCycle(int now, ACO_Submarine sub)
        {
            float tc = (now - _cycleMs) * 0.001f;
            float surfStart = SubmergedTime + RiseTime;
            float diveStart = surfStart + SurfaceTime;
            float end = diveStart + DiveTime;
            bool changed = false;
            if (tc >= surfStart + RepairDelay && _repairedCycle != _cycleIndex)
            {
                _repairedCycle = _cycleIndex;
                int n = sub.RepairSome(SurfaceRepairCount);
                if (n > 0) { _repairCount++; }
                changed = true;
            }
            // 완전 부상: 다 떠오른 뒤(반쯤 떠오른 자리에서 바뀌면 물 위로 순간 이동한다) 잠항 시작 1초 뒤까지(피해 이벤트 지연) 선체가 FinalRatio 아래.
            // 잠항 중에 늦게 온 피해로 비율이 넘어갔으면 다음 부상 때 바뀐다.
            if (tc >= surfStart && tc < diveStart + 1f && !sub.IsHullDestroyed() && sub.GetHullRatio() <= FinalRatio)
            {
                StartFinal(now, sub);
                return true;
            }
            // 전투 무인기(6-217): 다 떠오르고 DroneDelay초 뒤 이번 사출을 정한다(떠 있는 수가 상한이면 모자라는 만큼, 풀에 쉬는 만큼만).
            // 덮개가 DroneDoorLead초 동안 열린 뒤 사출구마다 DroneSpacing초 간격으로 1기씩. 잠항이 시작되면 남은 사출은 취소
            if (DronesPerSurface > 0 && tc >= surfStart + DroneDelay && tc < diveStart && _droneCycle != _cycleIndex && tc >= surfStart + DroneDecideAt())
            {
                _droneCycle = _cycleIndex;
                _droneDone = 0;
                int want = Mathf.Min(Mathf.Min(DronesPerSurface, MAX_DRONE_HATCH), Mathf.Max(DroneMaxAlive - Generator.CountActiveDrones(), 0));
                want = Mathf.Min(want, Generator.CountFreeDrones());
                for (int k = 0; k < MAX_DRONE_HATCH; k++) { _droneMs[k] = k < want ? now + Mathf.RoundToInt((DroneDoorLead + k * DroneSpacing) * 1000f) : 0; }
                changed = true;
            }
            if (_droneCycle == _cycleIndex && TickDrones(now, sub, tc >= diveStart)) { changed = true; }
            if (tc >= diveStart && _torpCycle != _cycleIndex)
            {
                _torpCycle = _cycleIndex;
                if (DiveTorpedoCount > 0) { AddTorpedo(now, sub.GetPosition(), DiveTorpedoCount); }
                changed = true;
            }
            if (tc >= end)
            {
                _cycleMs += Mathf.RoundToInt(end * 1000f);
                _cycleIndex++;
                PickSpot(_spot, SurfaceSpacing, SurfaceMoveMax);
                changed = true;
            }
            return changed;
        }

        /// 다 떠오른 뒤 무인기 사출을 정하는 시각(초, 6-218): 첫 기가 부상 · 수리 대사(둘 중 긴 것) + DroneLineGap 뒤에 나가게(첫 부상은 DroneFirstExtra를 더),
        /// 그래도 DroneDelay + DroneDoorLead보다 이르지 않게. 덮개가 DroneDoorLead초 먼저 열리므로 그만큼 앞당겨 정한다.
        private float DroneDecideAt()
        {
            // 첫 부상은 부서진 무장이 없어 수리 대사가 안 나온다 — 부상 대사만
            float surf = TableTime(SIT_SURFACE, LineEvent, LineText, LineClip);
            float lines = _cycleIndex == 0 ? surf : Mathf.Max(surf, TableTime(SIT_REPAIR, LineEvent, LineText, LineClip));
            float launch = Mathf.Max(DroneDelay + DroneDoorLead, lines + DroneLineGap + (_cycleIndex == 0 ? DroneFirstExtra : 0f));
            return launch - DroneDoorLead;
        }

        /// 무인기 사출(6-217): 시각이 된 사출구마다 생성기에 1기를 띄우게 한다(태그 없음 — 공중 요새 해치 증원처럼). cancel이면 남은 사출을 지운다.
        private bool TickDrones(int now, ACO_Submarine sub, bool cancel)
        {
            bool changed = false;
            for (int k = 0; k < MAX_DRONE_HATCH; k++)
            {
                int bit = 1 << k;
                if (_droneMs[k] == 0 || (_droneDone & bit) != 0) { continue; }
                if (cancel) { _droneMs[k] = 0; changed = true; continue; }
                if (now - _droneMs[k] < 0) { continue; }
                if (_droneDone == 0) { _droneWaves++; }
                _droneDone |= bit;
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_SUB_DRONE, 0, 1, ACO_NPCFighterAI.SKILL_ROOKIE, sub.GetDroneLaunchPoint(k), sub.GetDroneLaunchYaw(k), 0f);
                changed = true;
            }
            return changed;
        }

        /// 완전 부상(6-203): 항모 뱃전으로 충각 돌격 — 출발점 → (순양함을 스치면 비켜 도는 경유점) → 뱃전 앞(뱃머리가 닿는 자리). 길이 바다가 아니면 대비책(예전 방식: 멈추고 어뢰).
        private void StartFinal(int now, ACO_Submarine sub)
        {
            _stage = ACO_Submarine.STAGE_FINAL;
            _finalMs = now;
            for (int k = 0; k < MAX_DRONE_HATCH; k++) { if ((_droneDone & (1 << k)) == 0) { _droneMs[k] = 0; } }   // 아직 안 나간 무인기 사출은 취소(충각 단계엔 사출하지 않는다)
            Vector3 p = sub.GetPosition();
            _finalFrom = new Vector3(p.x, 0f, p.z);
            _finalYaw0 = sub.GetYaw();
            _finalYaw1 = _finalYaw0;
            _rammed = false;
            _nextRepairMs = now + Mathf.RoundToInt(RamRepairInterval * 1000f);

            // 돌격 길: 뱃전 명중 자리를 향해, 순양함(모항 근처의 아군 요격 CIWS)을 스치면 옆으로 비켜 도는 경유점
            Vector3 via = _finalFrom;
            Vector3 hit = ImpactToward(_finalFrom, 0f);
            if (FindRamObstacle(sub, _finalFrom, hit))
            {
                Vector3 obstacle = _obstacle;
                Vector3 line = new Vector3(hit.x - _finalFrom.x, 0f, hit.z - _finalFrom.z).normalized;
                Vector3 side = new Vector3(line.z, 0f, -line.x);
                // 길이 지나는 쪽으로 비킨다(순양함에서 길 쪽)
                Vector3 rel = obstacle - _finalFrom;
                float along = rel.x * line.x + rel.z * line.z;
                Vector3 closest = _finalFrom + line * along;
                Vector3 away = new Vector3(closest.x - obstacle.x, 0f, closest.z - obstacle.z);
                if (away.sqrMagnitude > 1f && (away.x * side.x + away.z * side.z) < 0f) { side = -side; }
                via = new Vector3(obstacle.x, 0f, obstacle.z) + side * RamDetour;
                hit = ImpactToward(via, 0f);
            }
            Vector3 last = new Vector3(hit.x - via.x, 0f, hit.z - via.z);
            if (last.sqrMagnitude < 1f) { last = new Vector3(hit.x - _finalFrom.x, 0f, hit.z - _finalFrom.z); }
            Vector3 to = hit - last.normalized * (sub != null ? sub.BowOffset : 131f);   // 뱃머리가 뱃전에 닿는 자리(선체 반길이 — 모델 값)
            to.y = 0f;
            _ram = SegmentSea(_finalFrom, via) && SegmentSea(via, to);
            if (_ram)
            {
                _ramVia = via;
                _finalTo = to;
                Vector3 first = new Vector3((via - _finalFrom).sqrMagnitude > 1f ? via.x - _finalFrom.x : to.x - _finalFrom.x, 0f, (via - _finalFrom).sqrMagnitude > 1f ? via.z - _finalFrom.z : to.z - _finalFrom.z);
                _finalYaw1 = Mathf.Atan2(first.x, first.z) * Mathf.Rad2Deg;
            }
            else
            {
                // 대비책: 항모 쪽으로 와서 FinalStopDistance에서 멈추고 어뢰를 쏜다
                Vector3 c = CarrierPoint();
                Vector3 d = new Vector3(c.x - p.x, 0f, c.z - p.z);
                float dist = d.magnitude;
                _ramVia = _finalFrom;
                _finalTo = _finalFrom;
                if (dist > 1f)
                {
                    d /= dist;
                    _finalYaw1 = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    if (dist > FinalStopDistance) { _finalTo = _finalFrom + d * SeaRun(_finalFrom, d, dist - FinalStopDistance); }
                }
                _nextTorpMs = now + Mathf.RoundToInt(FinalFirstTorpedo * 1000f);
            }
            int n = sub.RepairWeapons();
            if (n > 0) { _repairCount++; }
            Debug.Log("[ACO] 잠수함: 완전 부상 — 선체 " + Mathf.RoundToInt(sub.GetHullRatio() * 100f) + "%, " + (_ram ? "충각 돌격 " + Mathf.RoundToInt(RamLength()) + "m / " + RamTime + "초" + (via != _finalFrom ? "(순양함 비켜 감)" : "") : "길이 막힘 — 멈추고 어뢰(대비책)"));
        }

        private Vector3 _obstacle;   // FindRamObstacle 결과(U#은 out 매개변수를 못 쓴다)

        /// 돌격 길(from → hit)이 모항 근처(3km 안)의 아군 요격 CIWS(순양함)를 RamAvoidRadius 안으로 스치는가 — 처음 걸린 것을 _obstacle에.
        private bool FindRamObstacle(ACO_Submarine sub, Vector3 from, Vector3 hit)
        {
            _obstacle = Vector3.zero;
            if (sub.InterceptSites == null) { return false; }
            Vector3 c = CarrierPoint();
            Vector3 seg = new Vector3(hit.x - from.x, 0f, hit.z - from.z);
            float len = seg.magnitude;
            if (len < 1f) { return false; }
            Vector3 dir = seg / len;
            for (int i = 0; i < sub.InterceptSites.Length; i++)
            {
                ACO_AAGunSite s = sub.InterceptSites[i];
                if (s == null || !s.gameObject.activeInHierarchy) { continue; }
                Vector3 q = s.transform.position;
                if (q.y < -50f) { continue; }   // 풀에 숨은 유닛(원점 아래 대기 자리)은 모항 바로 밑이라 거른다
                if (new Vector2(q.x - c.x, q.z - c.z).magnitude > 3000f) { continue; }
                Vector3 rel = new Vector3(q.x - from.x, 0f, q.z - from.z);
                float along = Mathf.Clamp(rel.x * dir.x + rel.z * dir.z, 0f, len);
                Vector3 closest = from + dir * along;
                if (new Vector2(q.x - closest.x, q.z - closest.z).magnitude < RamAvoidRadius)
                {
                    _obstacle = q;
                    return true;
                }
            }
            return false;
        }

        /// a → b가 바다인가(LaneStep 간격, 항모 LaneSkip 안은 재지 않는다 — 선체가 지면으로 잡힌다).
        private bool SegmentSea(Vector3 a, Vector3 b)
        {
            Vector3 c = CarrierPoint();
            Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float len = d.magnitude;
            if (len < 1f) { return true; }
            d /= len;
            float step = Mathf.Max(LaneStep, 50f);
            for (float s = 0f; s <= len; s += step)
            {
                Vector3 p = a + d * s;
                if (new Vector2(p.x - c.x, p.z - c.z).magnitude < LaneSkip) { continue; }
                if (!Generator.IsSeaPoint(p.x, p.z)) { return false; }
            }
            return true;
        }

        /// from에서 dir로 바다로만 갈 수 있는 거리(최대 want) — 앞에 뭍이 있으면 그 FinalLandMargin 앞에서 멈춘다. 선체 반길이 안(자기 자리)은 재지 않는다.
        private float SeaRun(Vector3 from, Vector3 dir, float want)
        {
            float step = Mathf.Max(LaneStep, 50f);
            for (float s = Mathf.Min(150f, want); s <= want + step * 0.5f; s += step)
            {
                float at = Mathf.Min(s, want);
                Vector3 p = from + dir * at;
                if (!Generator.IsSeaPoint(p.x, p.z)) { return Mathf.Max(at - FinalLandMargin, 0f); }
            }
            return want;
        }

        private bool TickFinal(int now, ACO_Submarine sub)
        {
            if (sub.IsHullDestroyed()) { return false; }
            bool changed = false;
            // 무장 주기 수리(되살린 게 있을 때만 대사)
            if (_nextRepairMs != 0 && now - _nextRepairMs >= 0)
            {
                _nextRepairMs = now + Mathf.RoundToInt(RamRepairInterval * 1000f);
                if (sub.RepairSome(RamRepairCount) > 0) { _repairCount++; }   // 6-224: 시작 때만 모두, 그 뒤는 3문씩
                changed = true;
            }
            if (_ram)
            {
                // 뱃전에 닿았다: 항모 격침 = 미션 실패(관리자는 다음 Tick의 항모 체력 0으로 FailDelay 뒤에 끝낸다)
                if (!_rammed && now - _finalMs >= Mathf.RoundToInt((RamTime + RamGrace) * 1000f))
                {
                    _rammed = true;
                    _carrierHp = 0;
                    _carrierHits++;
                    changed = true;
                    Debug.Log("[ACO] 잠수함: 충각 — 항모 격침");
                }
                return changed;
            }
            if (now - _nextTorpMs < 0) { return changed; }
            _nextTorpMs = now + Mathf.RoundToInt(FinalTorpedoInterval * 1000f);
            if (FinalTorpedoCount > 0) { AddTorpedo(now, sub.GetPosition(), FinalTorpedoCount); }
            return true;
        }

        // ───────────── 충각 돌격 길 (모든 클라이언트 — 동기화 값만) ─────────────

        public bool IsRamming() { return _ram; }
        public bool HasRammed() { return _rammed; }
        public Vector3 GetRamVia() { return _ramVia; }

        public float RamLength()
        {
            return new Vector2(_ramVia.x - _finalFrom.x, _ramVia.z - _finalFrom.z).magnitude + new Vector2(_finalTo.x - _ramVia.x, _finalTo.z - _ramVia.z).magnitude;
        }

        /// 돌격 시작 e초 뒤 간 거리 — 처음 RamAccelTime 동안 고르게 가속, RamTime에 끝(거리와 상관없이 같은 시간).
        public float RamDistanceAt(float e)
        {
            float L = RamLength();
            float T = Mathf.Max(RamTime, 1f);
            float ta = Mathf.Clamp(RamAccelTime, 0.01f, T * 0.5f);
            float v = L / (T - ta * 0.5f);
            float d = e < ta ? v * e * e / (2f * ta) : v * (e - ta * 0.5f);
            return Mathf.Clamp(d, 0f, L);
        }

        /// 돌격 길 위 거리 d의 자리(해수면).
        public Vector3 RamPointAt(float d)
        {
            float l1 = new Vector2(_ramVia.x - _finalFrom.x, _ramVia.z - _finalFrom.z).magnitude;
            if (d <= l1 && l1 > 0.5f) { return Vector3.Lerp(_finalFrom, _ramVia, d / l1); }
            float l2 = new Vector2(_finalTo.x - _ramVia.x, _finalTo.z - _ramVia.z).magnitude;
            return l2 > 0.5f ? Vector3.Lerp(_ramVia, _finalTo, Mathf.Clamp01((d - l1) / l2)) : _finalTo;
        }

        /// 돌격 길 위 거리 d의 진행 방위(도).
        public float RamYawAt(float d)
        {
            float l1 = new Vector2(_ramVia.x - _finalFrom.x, _ramVia.z - _finalFrom.z).magnitude;
            Vector3 a = d < l1 && l1 > 0.5f ? _finalFrom : _ramVia;
            Vector3 b = d < l1 && l1 > 0.5f ? _ramVia : _finalTo;
            Vector3 dir = new Vector3(b.x - a.x, 0f, b.z - a.z);
            if (dir.sqrMagnitude < 1f) { return _finalYaw1; }
            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        /// 돌격이 닿기까지 남은 시간(초).
        public float GetRamRemaining(int now) { return Mathf.Max(RamTime - (now - _finalMs) * 0.001f, 0f); }

        /// 돌격 길에서 남은 거리(m).
        public float GetRamDistanceLeft(int now) { return Mathf.Max(RamLength() - RamDistanceAt((now - _finalMs) * 0.001f), 0f); }

        private void AddTorpedo(int now, Vector3 from, int count)
        {
            int i = _torpNext % MAX_TORPEDO;
            _torpMs[i] = now;
            _torpFrom[i] = new Vector3(from.x, 0f, from.z);
            _torpNum[i] = count;
            _torpDone &= ~(1 << i);
            _torpNext++;
            Debug.Log("[ACO] 잠수함: 어뢰 " + count + "발 — 명중까지 " + Mathf.RoundToInt(GetTorpTravel(i)) + "초");
        }

        /// 어뢰가 닿았다: 항모 피해(막을 수 없다).
        private bool TickTorpedoes(int now)
        {
            bool changed = false;
            for (int i = 0; i < MAX_TORPEDO; i++)
            {
                int bit = 1 << i;
                if (_torpMs[i] == 0 || (_torpDone & bit) != 0) { continue; }
                if (now - (_torpMs[i] + Mathf.RoundToInt(GetTorpTravel(i) * 1000f) + 300) < 0) { continue; }
                _torpDone |= bit;
                _carrierHp = Mathf.Max(_carrierHp - TorpedoDamage * Mathf.Max(_torpNum[i], 1), 0);
                _carrierHits++;
                changed = true;
                Debug.Log("[ACO] 잠수함: 어뢰 명중 — 항모 " + _carrierHp + "%");
            }
            return changed;
        }

        /// 부상 자리(잠수함이 물속에서 옮겨 간다): 앞 자리 prev에서 minMove~maxMove 떨어진 곳 중 항모에서 SurfaceDistanceMin~Max, 둘레 SurfaceSeaRadius가 바다,
        /// 항모까지 바다(어뢰 · 완전 부상 전진이 섬을 지나지 않게). 앞 50번은 이 조건 모두, 그 뒤 30번은 이동 거리 조건을 풀고(아무 방위), 마지막 20번은 항모까지 바다 조건도 푼다.
        /// 소나 원 가운데는 자리에서 반경의 60% 안.
        private void PickSpot(Vector3 prev, float minMove, float maxMove)
        {
            Vector3 c = CarrierPoint();
            Vector3 best = prev;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Vector3 p;
                if (attempt < 50)
                {
                    p = prev + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(minMove, Mathf.Max(maxMove, minMove));
                    float dc = new Vector2(p.x - c.x, p.z - c.z).magnitude;
                    if (dc < SurfaceDistanceMin || dc > SurfaceDistanceMax) { continue; }
                }
                else { p = c + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(SurfaceDistanceMin, SurfaceDistanceMax); }
                if (!SeaAround(p)) { continue; }
                if (attempt < 80 && !LaneIsSea(p, c)) { continue; }
                best = p;
                break;
            }
            _spot = new Vector3(best.x, 0f, best.z);
            _cycleYaw = Random.Range(0f, 360f);
            Vector2 off = Random.insideUnitCircle * (SonarRadius * 0.6f);
            _sonarCenter = new Vector3(_spot.x + off.x, 0f, _spot.z + off.y);
        }

        private bool SeaAround(Vector3 p)
        {
            if (!Generator.IsSeaPoint(p.x, p.z)) { return false; }
            float r = SurfaceSeaRadius;
            return Generator.IsSeaPoint(p.x + r, p.z) && Generator.IsSeaPoint(p.x - r, p.z) && Generator.IsSeaPoint(p.x, p.z + r) && Generator.IsSeaPoint(p.x, p.z - r);
        }

        public override void End()
        {
            _planCount = 0;
            _sunkMs = 0;
            _downMs = 0;
            _lostMs = 0;
            _released = false;
        }

        /// 격침 · 항모 격침 뒤 결과를 기다리는 중 — 이때는 제한 시간으로 실패하지 않는다.
        public override bool HoldsResult() { return _sunkMs != 0 || _lostMs != 0; }

        /// 미사일이 차례로 오는 방어 페이즈 — 시작 증원 웨이브를 보내지 않는다.
        public override bool WaveOnStart() { return false; }

        // ───────────── 계산 (모든 클라이언트 — 같은 동기화 값이면 같은 답) ─────────────

        public int SlcmKey(int s, int k) { return ((_stamp & 0x3FFFFF) << 6) + s * 8 + k; }

        private Vector3 CarrierPoint() { return HomePoint(CarrierCenter); }

        /// 모항 기준 자리(m) — 위치 + 방위만 쓴다(HomeBase = 안전지대 콜라이더라 크기가 100배다 — TransformPoint를 쓰면 늘어난다).
        public Vector3 HomePoint(Vector3 local)
        {
            if (HomeBase == null) { return local; }
            return HomeBase.position + Quaternion.Euler(0f, HomeBase.eulerAngles.y, 0f) * local;
        }

        /// from에서 항모로 곧게 왔을 때 닿는 뱃전(선체 타원 위, 높이 y).
        private Vector3 ImpactToward(Vector3 from, float y)
        {
            Vector3 c = CarrierPoint();
            Quaternion rot = HomeBase != null ? Quaternion.Euler(0f, HomeBase.eulerAngles.y, 0f) : Quaternion.identity;
            Vector3 u = Quaternion.Inverse(rot) * new Vector3(from.x - c.x, 0f, from.z - c.z);
            float len = u.magnitude;
            if (len < 1f) { return new Vector3(c.x, y, c.z); }
            u /= len;
            float a = Mathf.Max(CarrierHalfWidth, 1f);
            float b = Mathf.Max(CarrierHalfLength, 1f);
            float t = 1f / Mathf.Sqrt((u.x / a) * (u.x / a) + (u.z / b) * (u.z / b));
            Vector3 w = c + rot * (u * t);
            return new Vector3(w.x, y, w.z);
        }

        private Vector3 Side(Vector3 from)
        {
            Vector3 c = CarrierPoint();
            Vector3 d = new Vector3(c.x - from.x, 0f, c.z - from.z);
            if (d.sqrMagnitude < 1f) { return Vector3.right; }
            d = d.normalized;
            return new Vector3(d.z, 0f, -d.x);
        }

        /// 이번 페이즈 값을 받았고, 미션이 진행 중이며 지금 페이즈가 이 모듈인가 — 아니면 잠수함은 숨고(SLCM · 소나 · 항모 화재 끔) 대사도 없다.
        /// 미션이 제한 시간 · 다른 이유로 끝난 뒤 다음 미리 배치 전까지 잠수함이 풀에 남아 있어도 움직이거나 말하지 않게.
        public bool IsLive()
        {
            if (Manager == null || _stamp == 0 || _stamp != CurrentStamp() || _salvoCount <= 0) { return false; }
            if (Manager.GetState() != ACO_MissionManager.STATE_RUNNING || !Manager.IsPhaseActive()) { return false; }
            return Manager.GetModule(Manager.GetCurrentPhaseType()) == (ACO_PhaseModule)this;
        }
        public int GetStamp() { return _stamp; }
        public int GetStage() { return _stage; }
        public int GetSalvoCount() { return _salvoCount; }
        public int GetSalvoMs(int s) { return _salvoMs[Mathf.Clamp(s, 0, MAX_SALVO - 1)]; }
        public int GetSalvoSize(int s) { return _salvoSize[Mathf.Clamp(s, 0, MAX_SALVO - 1)]; }
        public int GetSlcmLaunchMs(int s, int k) { return GetSalvoMs(s) + Mathf.RoundToInt(k * SalvoStagger * 1000f); }
        public Vector3 GetSlcmFrom(int s, int k)
        {
            Vector3 f = _salvoFrom[Mathf.Clamp(s, 0, MAX_SALVO - 1)];
            float n = GetSalvoSize(s);
            return f + Side(f) * ((k - (n - 1f) * 0.5f) * SalvoSpread);
        }
        public Vector3 GetSlcmImpact(int s, int k) { return ImpactToward(GetSlcmFrom(s, k), ImpactHeight); }

        public int GetCarrierHp() { return _carrierHp; }
        public int GetCycleMs() { return _cycleMs; }
        /// 사출구 k의 이번 사출 시각(0 = 없음) — 덮개 연출(6-217).
        public int GetDroneMs(int k) { return IsLive() && k >= 0 && k < MAX_DRONE_HATCH ? _droneMs[k] : 0; }
        public Vector3 GetSpot() { return _spot; }
        public float GetCycleYaw() { return _cycleYaw; }
        public Vector3 GetSonarCenter() { return _sonarCenter; }
        public int GetFinalMs() { return _finalMs; }
        public Vector3 GetFinalFrom() { return _finalFrom; }
        public Vector3 GetFinalTo() { return _finalTo; }
        public float GetFinalYaw0() { return _finalYaw0; }
        public float GetFinalYaw1() { return _finalYaw1; }

        /// 소나 원이 보이는가: 간헐 부상 단계에서 부상 SonarLead초 전부터 다 떠오를 때까지.
        public bool IsSonarShown(int now)
        {
            if (!IsLive() || _stage != ACO_Submarine.STAGE_CYCLE) { return false; }
            float tc = (now - _cycleMs) * 0.001f;
            return tc >= SubmergedTime - SonarLead && tc < SubmergedTime + RiseTime;
        }

        public int GetTorpMs(int i) { return IsLive() ? _torpMs[Mathf.Clamp(i, 0, MAX_TORPEDO - 1)] : 0; }
        public int GetTorpNum(int i) { return _torpNum[Mathf.Clamp(i, 0, MAX_TORPEDO - 1)]; }
        private Vector3 TorpFrom(int i, int k)
        {
            Vector3 f = _torpFrom[Mathf.Clamp(i, 0, MAX_TORPEDO - 1)];
            float n = GetTorpNum(i);
            float spread = Sub() != null ? Sub().TorpedoSpread : 40f;
            return f + Side(f) * ((k - (n - 1f) * 0.5f) * spread);
        }
        public Vector3 GetTorpImpact(int i, int k) { return ImpactToward(TorpFrom(i, k), 1f); }
        /// 어뢰가 닿기까지(초) — 첫 발 거리 / TorpedoSpeed(같은 기록의 어뢰는 함께 닿는다).
        public float GetTorpTravel(int i)
        {
            Vector3 a = TorpFrom(i, 0);
            Vector3 b = GetTorpImpact(i, 0);
            return new Vector2(b.x - a.x, b.z - a.z).magnitude / Mathf.Max(TorpedoSpeed, 1f);
        }
        /// 발사 뒤 e초의 어뢰 머리 자리.
        public Vector3 GetTorpPosition(int i, int k, float e)
        {
            Vector3 a = TorpFrom(i, k);
            Vector3 b = GetTorpImpact(i, k);
            float travel = Mathf.Max(GetTorpTravel(i), 0.1f);
            return Vector3.Lerp(a, b, Mathf.Clamp01(e / travel));
        }

        private int CountPlanned()
        {
            int n = 0;
            for (int s = 0; s < _salvoCount; s++) { n += _salvoSize[s]; }
            return n;
        }

        private int CountResolved()
        {
            int n = 0;
            for (int s = 0; s < _salvoCount; s++) { n += Bits(_slcmDone[s]); }
            return n;
        }

        private int Bits(int v)
        {
            int n = 0;
            for (int i = 0; i < 8; i++) { if ((v & (1 << i)) != 0) { n++; } }
            return n;
        }

        /// 아직 정리되지 않은 SLCM 수(발사 전 포함).
        public int CountRemaining() { return Mathf.Max(CountPlanned() - CountResolved(), 0); }

        // ───────────── 대사 (모든 클라이언트) ─────────────

        /// 동기화된 상태가 바뀌었으면 그 상황의 대사를 재생한다 — ACO_Submarine이 0.5초마다 부른다.
        public void CheckLines(ACO_Submarine sub)
        {
            if (Chatter == null || !IsLive()) { return; }
            int now = Networking.GetServerTimeInMilliseconds();
            int launched = 0;
            for (int s = 0; s < _salvoCount; s++) { if (_salvoMs[s] != 0 && now - _salvoMs[s] >= 0) { launched = s + 1; } }
            bool sprint = false;
            if (sub.Slcms != null) { for (int i = 0; i < sub.Slcms.Length && !sprint; i++) { if (sub.Slcms[i] != null && sub.Slcms[i].IsTerminal()) { sprint = true; } } }
            bool sonar = IsSonarShown(now);
            float tc = (now - _cycleMs) * 0.001f;
            bool surfaced = _stage == ACO_Submarine.STAGE_CYCLE && tc >= SubmergedTime + RiseTime && tc < SubmergedTime + RiseTime + SurfaceTime;
            if (_lineStamp != _stamp)
            {
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로 삼는다(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineStamp = _stamp;
                _saidStage = _stage;
                _saidSalvos = launched;
                _saidSprint = sprint;
                _saidHits = _carrierHits;
                _saidHitAt = 0f;
                _saidLow = _carrierHp <= CarrierLowAt;
                _saidOut = _slcmOutMs != 0;
                _saidSonar = sonar ? _cycleIndex : -1;
                _saidSurface = surfaced ? _cycleIndex : -1;
                _saidRepairs = _repairCount;
                _saidTorps = _torpNext;
                _saidLost = _carrierHp <= 0;
                _saidRamNear = _ram && GetRamDistanceLeft(now) <= RamWarnDistance;
                _saidDroneWaves = _droneWaves;
                if (_stage == ACO_Submarine.STAGE_SLCM && launched == 0) { Say(SIT_START, ""); }
                return;
            }
            if (_carrierHp <= 0)
            {
                if (!_saidLost)
                {
                    _saidLost = true;
                    Chatter.ClearQueue();
                    Say(_rammed ? SIT_RAM_HIT : SIT_CARRIER_LOST, "");
                }
                return;
            }
            if (_stage == ACO_Submarine.STAGE_FINAL && _stage == _saidStage)
            {
                // 돌격: 접근 경고 한 번, 무장 주기 수리
                if (_ram && !_saidRamNear && GetRamDistanceLeft(now) <= RamWarnDistance)
                {
                    _saidRamNear = true;
                    Say(SIT_RAM_NEAR, "");
                }
                if (_repairCount > _saidRepairs)
                {
                    _saidRepairs = _repairCount;
                    Say(SIT_RAM_REPAIR, "");
                }
            }
            if (_stage != _saidStage)
            {
                _saidStage = _stage;
                if (_stage == ACO_Submarine.STAGE_FINAL)
                {
                    Chatter.ClearQueue();
                    Say(SIT_FINAL, "");
                    _saidRepairs = _repairCount;   // 완전 부상의 수리는 이 대사에 들어 있다
                    _saidTorps = _torpNext;
                    return;
                }
            }
            if (launched > _saidSalvos)
            {
                _saidSalvos = launched;
                // 첫 발사는 전용 대사(적 "All tubes, fire!" + AWACS 발사 경보 — 6-211). 시작 대사는 소나 접촉 · CAP 지시라 발사를 알리지 않는다
                Say(launched == 1 && HasSituation(SIT_FIRST_SALVO) ? SIT_FIRST_SALVO : SIT_SALVO, Bearing(_salvoFrom[launched - 1]));
            }
            if (sprint && !_saidSprint)
            {
                _saidSprint = true;
                Say(SIT_SPRINT, "");
            }
            if (_carrierHits > _saidHits)
            {
                _saidHits = _carrierHits;
                if (Time.time - _saidHitAt > 8f)
                {
                    _saidHitAt = Time.time;
                    Say(SIT_CARRIER_HIT, "");
                }
            }
            if (!_saidLow && _carrierHp <= CarrierLowAt)
            {
                _saidLow = true;
                Say(SIT_CARRIER_LOW, "");
            }
            if (!_saidOut && _slcmOutMs != 0)
            {
                _saidOut = true;
                Say(SIT_SLCM_OUT, "");
            }
            if (sonar && _saidSonar != _cycleIndex)
            {
                _saidSonar = _cycleIndex;
                Say(SIT_SONAR, "");
            }
            if (surfaced && _saidSurface != _cycleIndex)
            {
                _saidSurface = _cycleIndex;
                Say(_repairCount > _saidRepairs ? SIT_REPAIR : SIT_SURFACE, "");
                _saidRepairs = _repairCount;
            }
            if (_torpNext > _saidTorps)
            {
                _saidTorps = _torpNext;
                Say(_stage == ACO_Submarine.STAGE_FINAL ? SIT_TORPEDO : SIT_DIVE, "");
            }
            if (_droneWaves > _saidDroneWaves)
            {
                _saidDroneWaves = _droneWaves;
                Say(_droneWaves == 1 && HasSituation(SIT_DRONE_FIRST) ? SIT_DRONE_FIRST : SIT_DRONE, "");
            }
        }

        /// 항모에서 본 방위(000~359).
        private string Bearing(Vector3 p)
        {
            Vector3 c = CarrierPoint();
            float deg = Mathf.Atan2(p.x - c.x, p.z - c.z) * Mathf.Rad2Deg;
            if (deg < 0f) { deg += 360f; }
            int b = Mathf.RoundToInt(deg) % 360;
            return b.ToString("000");
        }

        /// 선체 격침(잠수함 Explode — 모든 클라이언트).
        public void SayDown()
        {
            if (Chatter != null) { Chatter.ClearQueue(); }
            Say(SIT_SUNK, "");
        }

        private void Say(int situation, string left) { SayTable(situation, left, LineEvent, LineSpeaker, LineText, LineClip); }

        /// 대사 표에 이 상황의 줄이 있는가(예전 표에는 첫 발사 줄이 없다).
        private bool HasSituation(int situation)
        {
            if (LineEvent == null) { return false; }
            for (int i = 0; i < LineEvent.Length; i++) { if (LineEvent[i] == situation) { return true; } }
            return false;
        }

        // ───────────── 글자 (모든 클라이언트) ─────────────

        public override string GetDescription()
        {
            if (!IsLive()) { return Description; }
            if (_stage == ACO_Submarine.STAGE_SLCM) { return "DEFEND THE CARRIER - SHOOT DOWN THE SLCMS"; }
            if (_stage == ACO_Submarine.STAGE_CYCLE) { return "SINK THE SUBMARINE WHEN IT SURFACES"; }
            return _ram ? "SINK THE SUBMARINE BEFORE IT RAMS THE CARRIER" : "SINK THE SUBMARINE BEFORE IT REACHES THE CARRIER";
        }

        /// BGM 단계(6-203): SLCM 0 → 간헐 부상 1 → 완전 부상(충각 돌격) 2.
        public override int GetBgmStage()
        {
            if (!IsLive()) { return 0; }
            return Mathf.Clamp(_stage, 0, 2);
        }

        /// "SLCM 9  CVN 82%" → "LEVIATHAN 72%  CVN 70%  SURFACE 0:31" → "LEVIATHAN 41%  CVN 64%  TORPEDO 0:22". 항모 체력은 40% 아래면 빨갛게.
        public override string GetProgressText()
        {
            ACO_Submarine sub = Sub();
            if (sub == null || !IsLive()) { return ""; }
            string cvn = "CVN " + _carrierHp + "%";
            if (_carrierHp < 40) { cvn = "<color=#FF5544>" + cvn + "</color>"; }
            if (_stage == ACO_Submarine.STAGE_SLCM) { return "SLCM " + CountRemaining() + "  " + cvn; }
            string hull = BossName() + " " + Mathf.RoundToInt(sub.GetHullRatio() * 100f) + "%  " + cvn;
            int now = Networking.GetServerTimeInMilliseconds();
            if (_stage == ACO_Submarine.STAGE_FINAL)
            {
                if (!_ram) { return hull + "  TORPEDO " + Clock((_nextTorpMs - now) * 0.001f); }
                float left = GetRamRemaining(now);
                string ram = "RAM " + Clock(left) + "  " + Dist(GetRamDistanceLeft(now));
                if (left < 30f) { ram = "<color=#FF5544>" + ram + "</color>"; }
                return hull + "  " + ram;
            }
            float tc = (now - _cycleMs) * 0.001f;
            float surfStart = SubmergedTime + RiseTime;
            float diveStart = surfStart + SurfaceTime;
            if (tc >= surfStart && tc < diveStart) { return hull + "  SURFACE " + Clock(diveStart - tc); }
            float next = tc < surfStart ? surfStart - tc : surfStart + (SubmergedTime + RiseTime + SurfaceTime + DiveTime) - tc;
            return hull + "  DIVE " + Clock(next);
        }

        private string BossName() { return EnemyName.Length > 0 ? EnemyName : "LEVIATHAN"; }

        private string Clock(float seconds)
        {
            int s = Mathf.Max(Mathf.CeilToInt(seconds), 0);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        public override string GetLocation() { return "DEFEND: CVN"; }
    }
}
