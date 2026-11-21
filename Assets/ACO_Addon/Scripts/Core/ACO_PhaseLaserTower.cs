using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 레이저 탑 페이즈(지상 보스, 6-331 — 병기 이름 칼리번 Caliburn). 이 페이즈만의 실패 조건은 없고 제한 시간도 더하지 않는다(미션 제한 시간은 그대로).
    ///  0) ECM: ECM 기지 3곳의 차량을 모두 부순다 — 그동안 환풍구는 드러나지 않는다(락온 · 피해 없음).
    ///  1) 환풍구: 보조탑 6기의 환풍구를 모두 부순다 — 렌즈는 무적. 패턴 A · B 교대.
    ///  2) 렌즈: 패턴을 쏜 직후 LensOpenTime초 동안만 렌즈가 열린다(과열). 렌즈를 부수면 성공.
    /// 패턴 시간표(조준 → 발사 → 과열)는 이 모듈이 동기화하고, ACO_LaserTower가 같은 값으로 빔을 그리고 맞는 사람 화면이 피해를 판정한다(6-331 ④-2).
    /// 자체 동기화(Manual): 단계, 배치 시드, 패턴(시작 시각 · 종류 · 시드 · 표적 · 조준 시간 · 다음 패턴 시각), 수리 횟수. 판정 · 표적 선택은 관리자 오너만.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseLaserTower : ACO_PhaseBoss
    {
        /// 보스 칭호 · 기록 ID(6-341).
        public override string GetAchievementId() { return "caliburn"; }

        public const int PATTERN_NONE = 0;
        public const int PATTERN_A = 1;
        public const int PATTERN_B = 2;

        [Header("배치")]
        [Tooltip("플레이어 평균 위치에서의 거리(m)")]
        public float SiteDistanceMin = 7000f;
        public float SiteDistanceMax = 20000f;

        [Header("패턴 A — 단일 추적 레이저(단계별 0 · 1 · 2)")]
        public float[] AimTimeA = new float[] { 3.5f, 3f, 2.2f };
        [Tooltip("2단계(환풍구)에서 부순 환풍구 하나마다 조준이 늘어나는 시간(초)")]
        public float AimPerVent = 0.5f;
        public float FireTimeA = 3f;
        public float[] PeriodA = new float[] { 14f, 11f, 8f };   // 2026-11-20 20 · 16 · 10 → (사용자 "패턴 좀 더 자주")
        [Tooltip("표적을 고르는 렌즈 거리(m)")]
        public float RangeA = 12000f;
        [Tooltip("이 거리(m) 안에 날고 있는 조종사가 없으면 패턴을 쉰다(모두 사망 · 착함)")]
        public float PauseRange = 25000f;

        [Header("패턴 B — 반사판 그물(1단계부터)")]
        public float PreviewTimeB = 2.5f;
        public float FireTimeB = 4f;
        public float[] PeriodB = new float[] { 22f, 22f, 14f };   // 30 · 30 · 18 →
        [Tooltip("부순 환풍구 하나마다 패턴 B 간격이 늘어나는 시간(초)")]
        public float PeriodPerVentB = 3f;   // 5 → 3(환풍구 6개면 +18초)
        [Tooltip("살아 있는 비행선이 이보다 적으면 패턴 B를 쓰지 않는다")]
        public int MinAirshipsB = 2;
        [Tooltip("고른 표적이 렌즈에서 이 거리(m) 안이면 그물(B), 밖이면 직사(A) — 사용자 \"가까이 있으면 그물, 멀리 있으면 직사\"")]
        public float NetRangeB = 6000f;

        [Header("렌즈 과열(3단계)")]
        [Tooltip("패턴을 쏜 직후 렌즈가 열려 있는 시간(초)")]
        public float LensOpenTime = 5f;
        [Tooltip("첫 패턴까지(초) — 페이즈 시작 · 배치 확인 뒤")]
        public float FirstPatternDelay = 8f;

        [Header("압박")]
        public int ReinforceFighters = 6;
        [Tooltip("탑 둘레 방공 수리 간격(초) — 2 · 3단계")]
        public float RepairInterval = 40f;
        public int RepairCount = 3;
        public int RepairAliveCap = 6;

        [Header("끝맺음")]
        public float CrashClearDelay = 4f;
        public float CrashHoldMax = 24f;
        public float ImpactLineDelay = 3f;

        // 대사 표의 상황 번호
        public const int SIT_SPAWN = 0;
        public const int SIT_ECM_DOWN = 1;
        public const int SIT_VENTS = 2;
        public const int SIT_VENT_DOWN = 3;
        public const int SIT_LENS_STAGE = 4;
        public const int SIT_OVERHEAT = 5;
        public const int SIT_NET = 6;
        public const int SIT_REPAIR = 7;
        public const int SIT_REINFORCE = 8;
        public const int SIT_LENS_HALF = 9;
        public const int SIT_LENS_DOWN = 10;
        public const int SIT_IMPACT_DOWN = 11;
        public const int SIT_AIRSHIPS_GONE = 12;
        public const int SIT_JAM_HINT = 13;        // 1단계가 JamHintDelay초 넘게 가면 한 번
        public const int SIT_ECM_LAST = 14;        // ECM 차량 하나 남음
        public const int SIT_VENT_LAST = 15;       // 환풍구 하나 남음
        public const int SIT_AIM = 16;             // 패턴 A 조준(처음 AimLineCount번) — AWACS 경고만(자동 사통 줄은 쓰지 않는다)
        public const int SIT_AIRSHIP_DOWN = 17;    // 반사판 비행선 격추(남은 수)
        public const int SIT_OVERHEAT_AGAIN = 18;  // 두 번째부터의 과열 창

        [Header("대사 밀림 조절")]
        [Tooltip("단계 · 시작 · 끝 대사가 밀려도 기다리는 시간(초)")]
        public float KeepLong = 10f;
        [Tooltip("경고(조준 · 그물 · 과열)가 이 시간(초) 안에 못 나오면 버린다 — 늦은 경고는 쓸모없다")]
        public float KeepWarning = 3f;
        [Tooltip("남은 수 알림을 기다리는 시간(초)")]
        public float KeepCount = 4f;
        [Tooltip("분위기 대사(증원 · 수리 · 렌즈 절반 · 힌트)를 기다리는 시간(초)")]
        public float KeepFlavor = 6f;
        [Tooltip("남은 수 알림 사이 최소 간격(초) — 그 사이 줄어든 수는 다음 알림이 말한다")]
        public float CountLineGap = 5f;
        [Tooltip("1단계가 이 시간(초) 넘게 가면 재머 힌트를 한 번")]
        public float JamHintDelay = 60f;
        [Tooltip("패턴 A 조준 경고를 몇 번까지(그 뒤로는 말하지 않는다 — 10~20초마다 온다)")]
        public int AimLineCount = 2;

        [Header("상황별 대사 표 (같은 순서의 배열 — 한 줄 = 대사 하나)")]
        public int[] LineEvent = new int[] {
            SIT_SPAWN,
            SIT_SPAWN,
            SIT_JAM_HINT,
            SIT_ECM_DOWN,
            SIT_ECM_LAST,
            SIT_VENTS,
            SIT_VENTS,
            SIT_VENT_DOWN,
            SIT_VENT_LAST,
            SIT_AIM,
            SIT_NET,
            SIT_AIRSHIP_DOWN,
            SIT_AIRSHIPS_GONE,
            SIT_AIRSHIPS_GONE,
            SIT_REPAIR,
            SIT_REINFORCE,
            SIT_REINFORCE,
            SIT_LENS_STAGE,
            SIT_LENS_STAGE,
            SIT_OVERHEAT,
            SIT_OVERHEAT_AGAIN,
            SIT_LENS_HALF,
            SIT_LENS_HALF,
            SIT_LENS_DOWN,
            SIT_LENS_DOWN,
            SIT_IMPACT_DOWN };
        public int[] LineSpeaker = new int[] {
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS };
        [Tooltip("대사. {left} = 남은 수")]
        public string[] LineText = new string[] {
            "Let there be light. Caliburn opens its eye, and this sky will be cleansed.",
            "Optical weapon tower detected! Its jammers are shielding the power grid. Find the three ECM vehicles and take them out!",
            "Your missiles won't lock the vents while the jammers are up. ECM vehicles first!",
            "ECM vehicle down! {left} left!",
            "One jammer left. Take it out!",
            "You tore away our veil? Then face the light unshielded.",
            "Jamming is down! The exhaust vents on the power towers are exposed. Hit them!",
            "Vent destroyed! {left} left!",
            "Last vent! Knock it out!",
            "Laser lock on one of you! Break hard, don't fly straight!",
            "Reflector net forming! Watch the blue lines and find a gap!",
            "Reflector down! {left} left!",
            "The mirrors are broken... but the light needs no mirrors!",
            "All reflectors are down. No more nets!",
            "Their guns around the tower are back online. Watch the low approach!",
            "Faithful, take wing! Guard Caliburn!",
            "Enemy fighters inbound over the tower!",
            "Pour everything into the lens. The light is judgment.",
            "All vents down! The lens overheats after every shot. Hit it the moment it fires!",
            "The lens is glowing red! Hit it now!",
            "Lens is open again. Strike now!",
            "The vessel cracks, but the light endures! Give it everything!",
            "The lens is cracking! Keep it up!",
            "No... the light... it is leaving us!",
            "Lens destroyed! Get clear, the tower is coming down!",
            "The tower is down. Outstanding work, all aircraft." };
        public AudioClip[] LineClip = new AudioClip[26];

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _stage;
        [UdonSynced] private int _seed;
        [UdonSynced] private int _patMs;        // 이번 패턴 시작(서버 ms)
        [UdonSynced] private int _patKind;
        [UdonSynced] private int _patSeed;
        [UdonSynced] private int _patTarget;    // 패턴 A 표적(참가자 기체 번호, -1 = 없음)
        [UdonSynced] private float _patAim;     // 이번 조준(미리보기) 시간(초)
        [UdonSynced] private int _nextPatMs;
        [UdonSynced] private int _repairs;

        // 오너 지역 상태
        private Vector3 _planPos;
        private int _nextRepairMs;
        private int _coreDownMs;
        private int _lastKind;
        private int _lastTarget = -1;

        // 대사(모든 클라이언트 지역 상태)
        private int _lineStamp;
        private int _saidStage;
        private int _saidEcm;
        private int _saidVents;
        private int _saidRepairs;
        private int _saidOpen;
        private int _saidNet;
        private bool _saidHalf;
        private bool _saidAirshipsGone;
        private int _saidAirships;
        private int _saidAim;
        private int _aimLines;
        private int _overheatLines;
        private bool _saidJamHint;
        private float _stageSeenAt;
        private float _lastCountAt = -100f;

        private ACO_LaserTower Tower() { return Generator != null ? Generator.LaserTower : null; }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null || !Generator.CanDeployLaserTower()) { return false; }
            if (!Generator.FindLaserTowerSite(focus, SiteDistanceMin, SiteDistanceMax)) { return false; }
            _planPos = Generator.GetLaserTowerSite();
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 1, false);
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_LASERTOWER, tag, 1, 0, _planPos, 0f, 0f);
            Manager.SetModuleSite(-1);
            StartAt(now);
        }

        private void StartAt(int now)
        {
            _stamp = CurrentStamp();
            _stage = ACO_LaserTower.STAGE_ECM;
            _seed = Random.Range(1, 1000000);
            _patMs = 0;
            _patKind = PATTERN_NONE;
            _patTarget = -1;
            _patAim = 0f;
            _nextPatMs = now + Mathf.RoundToInt((Manager.GetObjectiveSettleTime() + FirstPatternDelay) * 1000f);
            _repairs = 0;
            _coreDownMs = 0;
            _lastKind = PATTERN_NONE;
            _lastTarget = -1;
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            SyncNow();
        }

        public override int Tick(int now)
        {
            ACO_LaserTower tower = Tower();
            if (tower == null) { return RESULT_REPLACE; }
            if (_stamp != CurrentStamp()) { StartAt(now); }

            int tag = Manager.GetModuleTag();
            Manager.CountTaggedObjectives(tag);
            int total = Manager.GetTaggedTotal();
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            if (_coreDownMs != 0 && Manager.GetTaggedDone() < total) { _coreDownMs = 0; }   // 6-338: 목표가 다시 살아 있다(확정 전 오판) — 격파 대기 기록을 푼다
            if (Manager.GetTaggedDone() >= total)
            {
                if (_coreDownMs == 0) { _coreDownMs = now; }
                float age = tower.GetImpactAge();
                bool done = age >= 0f ? age >= CrashClearDelay : now - _coreDownMs >= Mathf.RoundToInt(CrashHoldMax * 1000f);
                if (!done) { return RESULT_RUNNING; }
                Manager.AddModuleTime((now - _coreDownMs) * 0.001f);
                Generator.RequestReleaseTag(tag);
                return RESULT_CLEAR;
            }
            if (now - Manager.GetPhaseStartMs() < Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f)) { return RESULT_RUNNING; }

            TickPattern(now, tower);
            if (_stage == ACO_LaserTower.STAGE_ECM)
            {
                int alive = tower.CountEcmAlive();
                int all = tower.GetEcmTotal();
                Manager.SetModuleProgress(all - alive, all, false);
                if (alive == 0) { NextStage(now, tower, ACO_LaserTower.STAGE_VENTS); }
                return RESULT_RUNNING;
            }
            TickRepair(now, tower);
            if (_stage == ACO_LaserTower.STAGE_VENTS)
            {
                int alive = tower.CountVentsAlive();
                int all = tower.GetVentTotal();
                Manager.SetModuleProgress(all - alive, all, false);
                if (alive == 0) { NextStage(now, tower, ACO_LaserTower.STAGE_LENS); }
                return RESULT_RUNNING;
            }
            Manager.SetModuleProgress(Mathf.RoundToInt((1f - tower.GetCoreHealthRatio()) * 100f), 100, false);
            return RESULT_RUNNING;
        }

        /// 다음 패턴 시각이 되면 새 패턴을 고른다: 렌즈가 볼 수 있는 조종사 하나를 고르고, 렌즈에서 NetRangeB 안이면 그물(B — 2단계부터 · 비행선 MinAirshipsB 이상), 아니면 직사(A).
        /// 볼 수 있는 조종사가 없어도 NetRangeB 안에 조종사가 있으면 그물(시야가 필요 없다), 그것도 아니면 2초 뒤 다시 본다.
        private void TickPattern(int now, ACO_LaserTower tower)
        {
            // 조준 중 표적이 격추 · 착함 · 범위 밖이면 이 패턴을 취소한다(과열 창도 없다)
            if (_patKind == PATTERN_A && IsAiming(now) && !tower.IsTargetValid(_patTarget, RangeA * 1.25f))
            {
                _patKind = PATTERN_NONE;
                _patTarget = -1;
                _nextPatMs = now + 2000;
                SyncNow();
                return;
            }
            if (now - _nextPatMs < 0) { return; }
            // 날고 있는 조종사가 근처에 없으면 쉰다
            if (!tower.AnyPilotNear(PauseRange)) { _nextPatMs = now + 2000; SyncNow(); return; }
            bool canB = _stage >= ACO_LaserTower.STAGE_VENTS && tower.CountAirshipsAlive() >= MinAirshipsB;
            int s = Mathf.Clamp(_stage, 0, 2);
            int ventsDown = tower.GetVentTotal() - tower.CountVentsAlive();
            int target = tower.PickTarget(RangeA, _lastTarget);
            int kind;
            if (target >= 0)
            {
                // 표적 거리로: 가까우면 그물, 멀면 직사(그물을 못 쓰면 직사)
                float d = tower.GetTargetDistance(target);
                kind = canB && d >= 0f && d <= NetRangeB ? PATTERN_B : PATTERN_A;
                _lastTarget = target;
            }
            else if (canB && tower.AnyPilotNear(NetRangeB)) { kind = PATTERN_B; }   // 탑 · 지형 뒤에 숨은 가까운 조종사 — 그물은 시야가 필요 없다
            else { _nextPatMs = now + 2000; SyncNow(); return; }
            if (kind == PATTERN_A)
            {
                _patTarget = target;
                _patAim = AimTimeA[s] + (_stage == ACO_LaserTower.STAGE_VENTS ? AimPerVent * ventsDown : 0f);
            }
            if (kind == PATTERN_B)
            {
                _patTarget = -1;
                _patAim = PreviewTimeB;
            }
            _patKind = kind;
            _lastKind = kind;
            _patMs = now;
            _patSeed = Random.Range(1, 1000000);
            float period = kind == PATTERN_A ? PeriodA[s] : PeriodB[s] + (_stage == ACO_LaserTower.STAGE_VENTS ? PeriodPerVentB * ventsDown : 0f);
            float busy = _patAim + (kind == PATTERN_A ? FireTimeA : FireTimeB) + (_stage == ACO_LaserTower.STAGE_LENS ? LensOpenTime : 0f) + 1f;
            _nextPatMs = now + Mathf.RoundToInt(Mathf.Max(period, busy) * 1000f);
            SyncNow();
        }

        private void NextStage(int now, ACO_LaserTower tower, int stage)
        {
            _stage = stage;
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            SyncNow();
            if (ReinforceFighters > 0)
            {
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, 0, ReinforceFighters, -1, tower.transform.position + Vector3.up * 1500f, 1500f, 3000f);
            }
        }

        private void TickRepair(int now, ACO_LaserTower tower)
        {
            if (RepairInterval <= 0f) { return; }
            // 오너가 바뀌면 새 오너는 StartAt을 거치지 않아 0 — 그때부터 간격을 잰다(바로 수리하거나, 서버 시각이 음수면 영영 안 하던 것)
            if (_nextRepairMs == 0) { _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f); return; }
            if (now - _nextRepairMs < 0) { return; }
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            int count = RepairAliveCap > 0 ? Mathf.Min(RepairCount, RepairAliveCap - tower.CountTowerDefenseAlive()) : RepairCount;
            if (count <= 0) { return; }
            if (tower.RepairTowerDefense(count) > 0)
            {
                _repairs++;
                SyncNow();
            }
        }

        public override void End()
        {
            _coreDownMs = 0;
        }

        public override bool HoldsResult() { return _coreDownMs != 0; }

        // ───────────── 대사 (모든 클라이언트) ─────────────

        /// 동기화된 상태가 바뀌었으면 그 상황의 대사를 낸다 — ACO_LaserTower가 0.5초마다 부른다.
        /// 밀림 조절: 경고(조준 · 그물 · 과열)는 KeepWarning초 안에 못 나오면 버린다(늦은 경고는 쓸모없다). 남은 수 알림은 잡담 급 · CountLineGap초 간격
        /// (그 사이 또 줄어든 수는 다음 알림이 말한다 — 마지막 하나 알림은 간격 없이). 단계 전환 · 과열 · 렌즈 파괴 · 붕괴는 밀린 대사를 비우고 바로. 분위기 대사(증원 · 수리 · 렌즈 절반)는 잡담 급.
        public void CheckLines(ACO_LaserTower tower)
        {
            if (Chatter == null || !IsLive()) { return; }
            int now = Networking.GetServerTimeInMilliseconds();
            int ecm = tower.CountEcmAlive();
            int vents = tower.CountVentsAlive();
            int airships = tower.CountAirshipsAlive();
            float core = tower.GetCoreHealthRatio();
            bool open = IsLensOpen(now);
            bool net = _patKind == PATTERN_B && IsAiming(now);
            bool aim = _patKind == PATTERN_A && IsAiming(now);
            if (_lineStamp != _stamp)
            {
                _lineStamp = _stamp;
                _saidStage = _stage;
                _saidEcm = ecm;
                _saidVents = vents;
                _saidAirships = airships;
                _saidRepairs = _repairs;
                _saidOpen = open ? _patMs : 0;
                _saidNet = net ? _patMs : 0;
                _saidAim = aim ? _patMs : 0;
                _saidHalf = core <= 0.5f;
                _saidAirshipsGone = airships == 0;
                _saidJamHint = false;
                _aimLines = 0;
                _overheatLines = 0;
                _lastCountAt = -100f;
                _stageSeenAt = Time.time;
                if (_stage == ACO_LaserTower.STAGE_ECM) { SayP(SIT_SPAWN, "", true, KeepLong); }
                return;
            }
            if (_stage != _saidStage)
            {
                _saidStage = _stage;
                _saidVents = vents;
                _stageSeenAt = Time.time;
                Chatter.ClearQueue();
                if (_stage == ACO_LaserTower.STAGE_VENTS) { SayP(SIT_VENTS, "", true, KeepLong); }
                else if (_stage == ACO_LaserTower.STAGE_LENS) { SayP(SIT_LENS_STAGE, "", true, KeepLong); }
                if (ReinforceFighters > 0) { SayP(SIT_REINFORCE, "", false, KeepFlavor); }
            }
            // 풀에서 다시 쓰는 탑: 페이즈 시작 직후 몇 프레임은 지난 미션의 부위 체력(0)이 남아 기준이 낮게 잡힐 수 있다(그러면 격파 대사가 안 나왔다).
            // 수가 늘어나면(부위가 되살아나면) 기준을 따라 올린다 — 탑 둘레 방공 말고는 되살아나는 부위가 없다.
            if (ecm > _saidEcm) { _saidEcm = ecm; }
            if (vents > _saidVents) { _saidVents = vents; }
            if (airships > _saidAirships) { _saidAirships = airships; }
            if (airships > 0) { _saidAirshipsGone = false; }
            if (core > 0.5f) { _saidHalf = false; }
            // 1단계가 오래가면 한 번: 재머가 살아 있는 동안 환풍구는 락온이 안 된다
            if (_stage == ACO_LaserTower.STAGE_ECM && !_saidJamHint && ecm > 0 && Time.time - _stageSeenAt >= JamHintDelay)
            {
                _saidJamHint = true;
                SayP(SIT_JAM_HINT, "", false, KeepFlavor);
            }
            if (_stage == ACO_LaserTower.STAGE_ECM && ecm < _saidEcm)
            {
                _saidEcm = ecm;
                if (ecm == 1) { SayCount(SIT_ECM_LAST, 1, true); }
                else if (ecm > 1) { SayCount(SIT_ECM_DOWN, ecm, false); }
            }
            if (_stage == ACO_LaserTower.STAGE_VENTS && vents < _saidVents)
            {
                _saidVents = vents;
                if (vents == 1) { SayCount(SIT_VENT_LAST, 1, true); }
                else if (vents > 1) { SayCount(SIT_VENT_DOWN, vents, false); }
            }
            if (airships < _saidAirships)
            {
                _saidAirships = airships;
                if (airships > 0) { SayCount(SIT_AIRSHIP_DOWN, airships, false); }
            }
            if (aim && _saidAim != _patMs)
            {
                _saidAim = _patMs;
                if (_aimLines < AimLineCount)
                {
                    _aimLines++;
                    SayP(SIT_AIM, "", true, KeepWarning);
                }
            }
            if (net && _saidNet != _patMs)
            {
                _saidNet = _patMs;
                SayP(SIT_NET, "", true, KeepWarning);
            }
            if (open && _saidOpen != _patMs)
            {
                _saidOpen = _patMs;
                Chatter.ClearQueue();
                SayP(_overheatLines == 0 ? SIT_OVERHEAT : SIT_OVERHEAT_AGAIN, "", true, KeepWarning);
                _overheatLines++;
            }
            if (_repairs != _saidRepairs)
            {
                _saidRepairs = _repairs;
                SayP(SIT_REPAIR, "", false, KeepFlavor);
            }
            if (_stage == ACO_LaserTower.STAGE_LENS && !_saidHalf && core <= 0.5f)
            {
                _saidHalf = true;
                SayP(SIT_LENS_HALF, "", false, KeepFlavor);
            }
            if (!_saidAirshipsGone && airships == 0)
            {
                _saidAirshipsGone = true;
                SayP(SIT_AIRSHIPS_GONE, "", true, KeepLong);
            }
        }

        /// 남은 수 알림: 지난 알림에서 CountLineGap초가 안 지났으면 건너뛴다(마지막 하나 알림 force는 언제나).
        private void SayCount(int situation, int left, bool force)
        {
            if (!force && Time.time - _lastCountAt < CountLineGap) { return; }
            _lastCountAt = Time.time;
            SayP(situation, left.ToString(), false, KeepCount);
        }

        public void SayDown()
        {
            if (Chatter == null) { return; }
            Chatter.ClearQueue();
            SayP(SIT_LENS_DOWN, "", true, KeepLong);
        }

        public void SayImpact() { SendCustomEventDelayedSeconds(nameof(_SayImpactLine), ImpactLineDelay); }

        public void _SayImpactLine()
        {
            if (Chatter == null) { return; }
            Chatter.ClearQueue();
            SayP(SIT_IMPACT_DOWN, "", true, KeepLong);
        }

        /// 표에서 이 상황의 줄을 위에서 아래로 낸다 — high면 페이즈 대사 급(잡담보다 먼저), 아니면 잡담 급. keep초보다 밀리면 무전이 버린다.
        private void SayP(int situation, string left, bool high, float keep)
        {
            if (Chatter == null || LineEvent == null || LineText == null) { return; }
            int n = Mathf.Min(LineEvent.Length, LineText.Length);
            for (int i = 0; i < n; i++)
            {
                if (LineEvent[i] != situation) { continue; }
                AudioClip clip = LineClip != null && i < LineClip.Length ? LineClip[i] : null;
                string text = LineText[i];
                if (string.IsNullOrEmpty(text) && clip == null) { continue; }
                if (!string.IsNullOrEmpty(text) && text.Contains("{left}")) { text = Chatter.Tr(text).Replace("{left}", left); }
                int speaker = LineSpeaker != null && i < LineSpeaker.Length ? LineSpeaker[i] : SPEAKER_AWACS;
                if (speaker == SPEAKER_AWACS)
                {
                    if (high) { Chatter.SayControl(text, clip, keep); }
                    else { Chatter.SayControlLow(text, clip, keep); }
                    continue;
                }
                string who = EnemyName;
                Color color = EnemyColor;
                int channel = ACO_Chatter.CH_ENEMY;
                if (speaker == SPEAKER_ALLY) { who = AllyName; color = AllyColor; channel = ACO_Chatter.CH_ALLY; }
                else if (speaker == SPEAKER_SYSTEM) { who = SystemName; color = SystemColor; }
                if (high) { Chatter.SayVoice(who, color, text, clip, keep, channel); }
                else { Chatter.SayLow(who, color, text, clip, keep, channel); }
            }
        }

        // ───────────── 모든 클라이언트 ─────────────

        public bool IsLive() { return Manager != null && _stamp != 0 && _stamp == CurrentStamp(); }
        /// 페이즈 시작 서버 시각(동기화) — 비행선 궤도의 기준 시각.
        public int GetStamp() { return _stamp; }
        public int GetStage() { return _stage; }
        public int GetSeed() { return _seed; }
        public int GetPatternKind() { return _patKind; }
        public int GetPatternSeed() { return _patSeed; }
        public int GetPatternTarget() { return _patTarget; }
        public int GetPatternStartMs() { return _patMs; }
        /// 조준(A) · 미리보기(B)가 끝나고 굵은 빔이 나가는 시각.
        public int GetFireMs() { return _patMs + Mathf.RoundToInt(_patAim * 1000f); }
        public int GetFireEndMs() { return GetFireMs() + Mathf.RoundToInt((_patKind == PATTERN_A ? FireTimeA : FireTimeB) * 1000f); }

        public bool IsAiming(int now)
        {
            if (_patKind == PATTERN_NONE || !IsLive()) { return false; }
            return now - _patMs >= 0 && now - GetFireMs() < 0;
        }

        public bool IsFiring(int now)
        {
            if (_patKind == PATTERN_NONE || !IsLive()) { return false; }
            return now - GetFireMs() >= 0 && now - GetFireEndMs() < 0;
        }

        /// 렌즈 과열 창(3단계): 굵은 빔이 끝난 뒤 LensOpenTime초.
        public bool IsLensOpen(int now)
        {
            if (_stage < ACO_LaserTower.STAGE_LENS || _patKind == PATTERN_NONE || !IsLive()) { return false; }
            int end = GetFireEndMs();
            return now - end >= 0 && now - end < Mathf.RoundToInt(LensOpenTime * 1000f);
        }

        public override string GetDescription()
        {
            if (!IsLive()) { return Description; }
            if (_stage == ACO_LaserTower.STAGE_ECM) { return "DESTROY THE ECM VEHICLES"; }
            if (_stage == ACO_LaserTower.STAGE_VENTS) { return "DESTROY THE EXHAUST VENTS"; }
            return "HIT THE LENS WHEN IT OVERHEATS";
        }

        /// 단계마다 필수 목표 하나만: "ECM VEHICLE 1/3" → "VENT 2/6"(부순 수) → "LENS 72%". 과열 중이면 OPEN. 비행선은 필수 목표가 아니라 뺐다.
        public override string GetProgressText()
        {
            ACO_LaserTower tower = Tower();
            if (tower == null || !IsLive()) { return ""; }
            int now = Networking.GetServerTimeInMilliseconds();
            string s;
            if (_stage == ACO_LaserTower.STAGE_ECM) { s = "ECM VEHICLE " + (tower.GetEcmTotal() - tower.CountEcmAlive()) + "/" + tower.GetEcmTotal(); }   // 부순 수(사용자 "ECM vehicle 0/3 이런 식으로")
            else if (_stage == ACO_LaserTower.STAGE_VENTS) { s = "VENT " + (tower.GetVentTotal() - tower.CountVentsAlive()) + "/" + tower.GetVentTotal(); }
            else { s = "LENS " + Mathf.RoundToInt(tower.GetCoreHealthRatio() * 100f) + "%"; }
            if (IsLensOpen(now)) { s += "  <color=#FFD24D>OPEN</color>"; }
            return s;
        }
    }
}
