using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 랜스 포대 페이즈(지상 보스) — 초대형 포 한 문이 지평선 너머 아군 기지를 포격한다. 공대지 무장이 주인공이다.
    /// 포격 주기: 충전(ChargeTime) → 발사(아군 기지 내구도 1칸) → 재장전(ReloadTime)을 되풀이한다. 처음엔 대기(STANDBY) — 우리 쪽이 공격을 시작하면
    /// (방공 부위·코어가 처음 피해를 입으면) 첫 충전을 건다. ArmMaxWait초가 지나도 공격이 없으면 그때 건다(0 = 기다리기만).
    ///  1) 방공망: 대공포·CIWS·VLS·소형 레일건을 모두 부순다. 코어 장갑은 닫혀 있다. 소형 레일건은 몇 초 뒤 자리를 예측해 쏜다 — 직진하면 폭발 속으로 들어간다(ACO_MiniRailgun).
    ///  2) 냉각기: 냉각기를 모두 부순다. 하나 부술 때마다 이번·다음 충전이 CoolerDelay만큼 느려진다. 방공망 일부가 RepairInterval마다 수리된다.
    ///  3) 코어: 충전 마지막 CoreOpenBefore초 + 발사 뒤 재장전 동안만 장갑이 열린다(위쪽도 열려 탑어택 무장이 내리꽂을 수 있다). 코어를 부수면 성공.
    /// 아군 기지가 BaseHits번 맞으면 미션 실패. 발사 WarnTime초 전부터 붉은 레이저가 사선을 보여 주고, 사선 안의 기체는 큰 피해를 입는다(ACO_Railgun).
    /// 끝맺음은 요새와 같다 — 격추는 폭발 뒤 CrashClearDelay, 패배는 FailDelay 뒤에 결과가 나온다(그동안 제한 시간으로 실패하지 않는다).
    ///
    /// 자체 동기화(Manual): 단계와 주기 시각·충전 시간·기지 피격. 모든 클라이언트가 이 값과 서버 시각으로 같은 장갑·포신·발사를 계산한다. 판정은 관리자 오너만.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseRailgun : ACO_PhaseBoss
    {
        [Header("배치")]
        [Tooltip("플레이어 평균 위치에서의 거리(m)")]
        public float SiteDistanceMin = 7000f;
        public float SiteDistanceMax = 20000f;
        [Tooltip("이 페이즈가 시작될 때 미션 제한 시간에 더하는 시간(초)")]
        public float ExtraTime = 480f;

        [Header("포격 주기")]
        [Tooltip("충전 시간(초)")]
        public float ChargeTime = 90f;
        [Tooltip("발사 뒤 다음 충전까지(초) — 코어가 계속 열려 있다")]
        public float ReloadTime = 10f;
        [Tooltip("냉각기 하나를 부술 때마다 충전이 늘어나는 시간(초)")]
        public float CoolerDelay = 15f;
        [Tooltip("발사 몇 초 전부터 코어 장갑이 열리는가")]
        public float CoreOpenBefore = 30f;
        [Tooltip("공격이 없어도 페이즈 시작 뒤 이 시간(초)이 지나면 첫 충전을 건다. 0 = 공격할 때까지 기다린다")]
        public float ArmMaxWait = 180f;
        [Tooltip("발사 몇 초 전부터 붉은 레이저로 사선을 보여 주는가")]
        public float WarnTime = 5f;
        [Tooltip("아군 기지가 이만큼 맞으면 미션 실패")]
        public int BaseHits = 3;

        [Header("압박")]
        [Tooltip("단계가 바뀔 때 포대 둘레에 나오는 적 전투기 수")]
        public int ReinforceFighters = 4;
        [Tooltip("방공망 수리 간격(초) — 냉각기·코어 단계")]
        public float RepairInterval = 40f;
        public int RepairCount = 3;
        [Tooltip("이 페이즈 동안 수리로 되살리는 방공 부위의 최대 수(합계). 0이면 끝없이")]
        public int MaxRepairs = 6;

        [Header("끝맺음")]
        [Tooltip("코어가 터진 뒤 성공으로 넘어가기까지(초)")]
        public float CrashClearDelay = 4f;
        [Tooltip("폭발을 못 봐도 이 시간(초)이 지나면 성공으로 넘어간다")]
        public float CrashHoldMax = 20f;
        [Tooltip("마지막 포격 뒤 미션 실패로 넘어가기까지(초)")]
        public float FailDelay = 6f;
        [Tooltip("충돌 대사가 폭발 뒤 나오기까지(초)")]
        public float ImpactLineDelay = 3f;
        [Tooltip("이 시간(초) 전에 발사 임박 대사")]
        public float FireWarnLine = 30f;

        // 대사 표의 상황 번호 — ACO_PhaseFortressEditor의 이름표와 같은 순서
        public const int SIT_SPAWN = 0;
        public const int SIT_AA_HALF = 1;
        public const int SIT_COOLERS = 2;
        public const int SIT_COOLER_DOWN = 3;
        public const int SIT_REPAIR = 4;
        public const int SIT_REINFORCE = 5;
        public const int SIT_CORE_STAGE = 6;
        public const int SIT_CHARGE_START = 7;
        public const int SIT_FIRE_SOON = 8;
        public const int SIT_CORE_OPEN = 9;
        public const int SIT_BASE_HIT = 10;
        public const int SIT_BASE_LAST = 11;
        public const int SIT_CORE_HALF = 12;
        public const int SIT_CORE_DOWN = 13;
        public const int SIT_IMPACT_DOWN = 14;
        public const int SIT_BASE_LOST = 15;
        public const int SIT_MINI_FIRE = 16;

        [Header("상황별 대사 표 (같은 순서의 배열 — 한 줄 = 대사 하나)")]
        [Tooltip("이 대사가 나오는 상황(SIT_*)")]
        public int[] LineEvent = new int[] {
            SIT_SPAWN, SIT_SPAWN,
            SIT_AA_HALF, SIT_AA_HALF,
            SIT_COOLERS, SIT_COOLERS,
            SIT_COOLER_DOWN, SIT_COOLER_DOWN,
            SIT_REPAIR, SIT_REPAIR,
            SIT_REINFORCE, SIT_REINFORCE,
            SIT_CORE_STAGE, SIT_CORE_STAGE,
            SIT_CHARGE_START,
            SIT_FIRE_SOON,
            SIT_CORE_OPEN,
            SIT_BASE_HIT,
            SIT_BASE_LAST,
            SIT_CORE_HALF, SIT_CORE_HALF,
            SIT_CORE_DOWN, SIT_CORE_DOWN,
            SIT_IMPACT_DOWN,
            SIT_BASE_LOST,
            SIT_MINI_FIRE };
        [Tooltip("말하는 쪽(0 = 적, 1 = AWACS)")]
        public int[] LineSpeaker = new int[] {
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS };
        [Tooltip("대사. {left} = 남은 냉각기 수")]
        public string[] LineText = new string[] {
            "The Lance is online. Your base has minutes to live.",
            "That is a strategic railgun, and it is aimed at our base! Take out its air defenses first!",
            "Half the batteries are down! Keep them off the gun!",
            "Their air defenses are thinning out. Keep pushing!",
            "They are through the batteries. Protect the coolant towers!",
            "Air defenses down! Hit the coolant towers, that will slow its charge!",
            "Coolant pressure dropping! Compensate!",
            "Tower down! {left} left! Its charge is slowing!",
            "Batteries restored. Resume fire.",
            "Some of their guns are back online. Watch yourselves!",
            "All squadrons, cover the Lance!",
            "Enemy fighters scrambling over the gun!",
            "We do not need coolant for one more shot.",
            "All towers down! The core vents when it charges. Wait for the armor to open and hit it!",
            "It is charging again.",
            "Thirty seconds to firing! Stay out of the line of fire!",
            "Armor is open! Core exposed, hit it now!",
            "Impact on the base! We cannot take many more of those!",
            "One more hit and the base is gone!",
            "Core containment failing! Fire anyway!",
            "The core is cracking! Keep hitting it!",
            "No... The core is going critical!",
            "Core destroyed! Get clear, it is going up!",
            "The Lance is destroyed. The base is safe. Outstanding work.",
            "The base is gone... We lost it.",
            "Those smaller guns are leading you! Do not fly straight, keep turning!" };
        [Tooltip("음성(선택 — 비우면 자막만)")]
        public AudioClip[] LineClip = new AudioClip[26];

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _stage;
        [UdonSynced] private int _cycleMs;      // 이번 충전을 시작한 시각
        [UdonSynced] private float _charge;     // 이번 충전 시간(초)
        [UdonSynced] private int _hits;         // 아군 기지가 맞은 수
        [UdonSynced] private int _hitCycleMs;   // 마지막으로 친 주기(같은 주기를 두 번 세지 않게)
        [UdonSynced] private int _repairs;
        [UdonSynced] private int _repairedUnits;   // 지금까지 되살린 부위 수(MaxRepairs와 비교 — 새 오너도 이어서 센다)
        [UdonSynced] private bool _armed;          // 첫 충전을 걸었다(그 전엔 대기 — 충전·발사·경고·장갑 열림 없음)

        // 오너 지역 상태
        private Vector3 _planPos;
        private int _planYaw;
        private int _nextRepairMs;
        private int _coolersAlive = -1;
        private int _coreDownMs;
        private int _lostMs;
        private float _armBaseline = -1f;   // 대기 중 공격 감지 기준(방공 부위 + 코어 체력 합)

        // 대사(모든 클라이언트 지역 상태)
        private int _lineStamp;
        private int _saidStage;
        private int _saidCoolers;
        private int _saidRepairs;
        private int _saidHits;
        private int _saidCycle;
        private int _saidSoon;
        private int _saidOpen;
        private bool _saidAaHalf;
        private bool _saidHalf;
        private int _saidMini;

        private ACO_Railgun Gun() { return Generator != null ? Generator.Railgun : null; }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null || !Generator.CanDeployRailgun()) { return false; }
            if (!Generator.FindRailgunSite(focus, SiteDistanceMin, SiteDistanceMax)) { return false; }
            _planPos = Generator.GetRailgunSite();
            // 포구가 플레이어 쪽을 지나 지평선을 겨눈다 — 사선을 피하는 일이 생긴다
            Vector3 d = focus - _planPos;
            _planYaw = Mathf.RoundToInt(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 1, false);
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_RAILGUN, tag, 1, _planYaw, _planPos, 0f, 0f);
            Manager.SetModuleSite(-1);
            Manager.AddModuleTime(ExtraTime);
            StartAt(now);
        }

        private void StartAt(int now)
        {
            _stamp = CurrentStamp();
            _stage = ACO_Railgun.STAGE_AIR_DEFENSE;
            _cycleMs = now;
            _charge = ChargeTime;
            _hits = 0;
            _hitCycleMs = 0;
            _repairs = 0;
            _repairedUnits = 0;
            _coolersAlive = -1;
            _coreDownMs = 0;
            _lostMs = 0;
            _armed = false;
            _armBaseline = -1f;
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            SyncNow();
        }

        /// 대기 중: 방공 부위·코어 체력 합이 처음 잰 값보다 줄면(공격 시작) 또는 ArmMaxWait가 지나면 첫 충전을 건다.
        /// 기준은 배치 확인 시간이 지난 뒤 처음 잰다(풀에서 막 꺼낸 부위의 체력 0이 부활로 채워진 뒤).
        private void TickArm(int now, ACO_Railgun gun)
        {
            float sum = gun.SumDefenseAndCoreHealth();
            bool attacked = false;
            if (_armBaseline < 0f) { _armBaseline = sum; }
            else if (sum < _armBaseline - 0.5f) { attacked = true; }
            else if (sum > _armBaseline) { _armBaseline = sum; }   // 늦게 채워진 체력(숙련도 등)은 기준을 올린다
            bool timeout = ArmMaxWait > 0f && now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(ArmMaxWait * 1000f);
            if (!attacked && !timeout) { return; }
            _armed = true;
            _cycleMs = now;
            _charge = ChargeTime;
            SyncNow();
            Debug.Log("[ACO] 랜스 포대: " + (attacked ? "공격 감지" : "대기 시간 초과") + " — 첫 충전 시작");
        }

        public override int Tick(int now)
        {
            ACO_Railgun gun = Gun();
            if (gun == null) { return RESULT_REPLACE; }
            if (_stamp != CurrentStamp()) { StartAt(now); }

            int tag = Manager.GetModuleTag();
            Manager.CountTaggedObjectives(tag);
            int total = Manager.GetTaggedTotal();
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            if (Manager.GetTaggedDone() >= total)
            {
                if (_coreDownMs == 0) { _coreDownMs = now; }
                float age = gun.GetImpactAge();
                bool done = age >= 0f ? age >= CrashClearDelay : now - _coreDownMs >= Mathf.RoundToInt(CrashHoldMax * 1000f);
                if (!done) { return RESULT_RUNNING; }
                Manager.AddModuleTime((now - _coreDownMs) * 0.001f);
                // 지하로 옮겨 숨긴 동체를 풀로 거둔다 — 안 거두면 다음 전체 배치까지 켜진 채 남는다(점수는 이미 집계됐다)
                Generator.RequestReleaseTag(tag);
                return RESULT_CLEAR;
            }
            if (_lostMs != 0)
            {
                if (now - _lostMs < Mathf.RoundToInt(FailDelay * 1000f)) { return RESULT_RUNNING; }
                Manager.ScheduleModuleRelease();
                return RESULT_FAIL;
            }
            if (now - Manager.GetPhaseStartMs() < Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f)) { return RESULT_RUNNING; }

            if (!_armed) { TickArm(now, gun); }
            else { TickCycle(now, gun); }
            if (_lostMs != 0) { return RESULT_RUNNING; }

            if (_stage == ACO_Railgun.STAGE_AIR_DEFENSE)
            {
                int alive = gun.CountAirDefenseAlive();
                int all = gun.GetAirDefenseTotal();
                Manager.SetModuleProgress(all - alive, all, false);
                if (alive == 0) { NextStage(now, gun, ACO_Railgun.STAGE_COOLERS); }
                return RESULT_RUNNING;
            }
            TickRepair(now, gun);
            if (_stage == ACO_Railgun.STAGE_COOLERS)
            {
                int alive = gun.CountCoolersAlive();
                int all = gun.GetCoolerTotal();
                Manager.SetModuleProgress(all - alive, all, false);
                if (_coolersAlive >= 0 && alive < _coolersAlive)
                {
                    // 부술 때마다 충전이 늦어진다 — 이미 발사했으면 다음 충전부터
                    if (_armed && now - GetFireMs() < 0) { _charge += CoolerDelay * (_coolersAlive - alive); SyncNow(); }
                }
                _coolersAlive = alive;
                if (alive == 0) { NextStage(now, gun, ACO_Railgun.STAGE_CORE); }
                return RESULT_RUNNING;
            }
            Manager.SetModuleProgress(Mathf.RoundToInt((1f - gun.GetCoreHealthRatio()) * 100f), 100, false);
            return RESULT_RUNNING;
        }

        /// 발사 시각이 지나면 기지 피격을 한 번 세고, 재장전이 끝나면 다음 충전을 건다.
        private void TickCycle(int now, ACO_Railgun gun)
        {
            int fire = GetFireMs();
            if (now - fire < 0) { return; }
            if (_hitCycleMs != _cycleMs)
            {
                _hitCycleMs = _cycleMs;
                _hits++;
                SyncNow();
                if (_hits >= BaseHits) { _lostMs = now; return; }
            }
            if (now - fire < Mathf.RoundToInt(ReloadTime * 1000f)) { return; }
            int destroyed = gun.GetCoolerTotal() - gun.CountCoolersAlive();
            _cycleMs = now;
            _charge = ChargeTime + CoolerDelay * Mathf.Max(destroyed, 0);
            SyncNow();
        }

        private void NextStage(int now, ACO_Railgun gun, int stage)
        {
            _stage = stage;
            _coolersAlive = -1;
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            SyncNow();
            if (ReinforceFighters > 0)
            {
                Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, 0, ReinforceFighters, -1, gun.transform.position + Vector3.up * 1500f, 1500f, 3000f);
            }
        }

        private void TickRepair(int now, ACO_Railgun gun)
        {
            if (RepairInterval <= 0f || now - _nextRepairMs < 0) { return; }
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            int count = MaxRepairs > 0 ? Mathf.Min(RepairCount, MaxRepairs - _repairedUnits) : RepairCount;
            if (count <= 0) { return; }
            int done = gun.RepairAirDefense(count);
            if (done > 0)
            {
                _repairs++;
                _repairedUnits += done;
                SyncNow();
            }
        }

        public override void End()
        {
            _coolersAlive = -1;
            _coreDownMs = 0;
            _lostMs = 0;
        }

        public override bool HoldsResult() { return _coreDownMs != 0 || _lostMs != 0; }

        // ───────────── 대사 (모든 클라이언트) ─────────────

        /// 동기화된 상태가 바뀌었으면 그 상황의 대사를 재생한다 — ACO_Railgun이 0.5초마다 부른다.
        public void CheckLines(ACO_Railgun gun)
        {
            if (Chatter == null || !IsLive()) { return; }
            int now = Networking.GetServerTimeInMilliseconds();
            int coolers = gun.CountCoolersAlive();
            float core = gun.GetCoreHealthRatio();
            bool aaHalf = gun.CountAirDefenseAlive() * 2 <= gun.GetAirDefenseTotal();
            bool soon = _armed && GetFireMs() - now <= Mathf.RoundToInt(FireWarnLine * 1000f);
            bool open = IsCoreOpen(now);
            if (_lineStamp != _stamp)
            {
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로 삼는다(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineStamp = _stamp;
                _saidStage = _stage;
                _saidCoolers = coolers;
                _saidRepairs = _repairs;
                _saidHits = _hits;
                _saidCycle = _cycleMs;
                _saidSoon = soon ? _cycleMs : 0;
                _saidOpen = open ? _cycleMs : 0;
                _saidAaHalf = aaHalf;
                _saidHalf = core <= 0.5f;
                if (_stage == ACO_Railgun.STAGE_AIR_DEFENSE) { Say(SIT_SPAWN, ""); }
                return;
            }
            if (_hits != _saidHits)
            {
                _saidHits = _hits;
                Chatter.ClearQueue();
                if (_hits >= BaseHits) { Say(SIT_BASE_LOST, ""); }
                else if (_hits == BaseHits - 1) { Say(SIT_BASE_HIT, ""); Say(SIT_BASE_LAST, ""); }
                else { Say(SIT_BASE_HIT, ""); }
            }
            if (_stage != _saidStage)
            {
                _saidStage = _stage;
                _saidCoolers = coolers;
                if (_stage == ACO_Railgun.STAGE_COOLERS) { Say(SIT_COOLERS, ""); }
                else if (_stage == ACO_Railgun.STAGE_CORE) { Say(SIT_CORE_STAGE, ""); }
                if (ReinforceFighters > 0) { Say(SIT_REINFORCE, ""); }
            }
            if (_cycleMs != _saidCycle)
            {
                _saidCycle = _cycleMs;
                Say(SIT_CHARGE_START, "");
            }
            if (soon && _saidSoon != _cycleMs)
            {
                _saidSoon = _cycleMs;
                Say(SIT_FIRE_SOON, "");
            }
            if (open && _saidOpen != _cycleMs)
            {
                _saidOpen = _cycleMs;
                Chatter.ClearQueue();
                Say(SIT_CORE_OPEN, "");
            }
            if (_stage == ACO_Railgun.STAGE_AIR_DEFENSE && aaHalf && !_saidAaHalf)
            {
                _saidAaHalf = true;
                Say(SIT_AA_HALF, "");
            }
            if (_stage == ACO_Railgun.STAGE_COOLERS && coolers < _saidCoolers)
            {
                _saidCoolers = coolers;
                if (coolers > 0) { Say(SIT_COOLER_DOWN, coolers.ToString()); }
            }
            if (_repairs != _saidRepairs)
            {
                _saidRepairs = _repairs;
                Say(SIT_REPAIR, "");
            }
            if (_stage == ACO_Railgun.STAGE_CORE && !_saidHalf && core <= 0.5f)
            {
                _saidHalf = true;
                Say(SIT_CORE_HALF, "");
            }
        }

        public void SayDown() { Say(SIT_CORE_DOWN, ""); }

        /// 소형 레일건의 첫 사격 — 페이즈마다 한 번.
        public void SayMiniFire()
        {
            if (!IsLive() || _saidMini == _stamp) { return; }
            _saidMini = _stamp;
            Say(SIT_MINI_FIRE, "");
        }

        public void SayImpact() { SendCustomEventDelayedSeconds(nameof(_SayImpactLine), ImpactLineDelay); }

        public void _SayImpactLine()
        {
            if (Chatter == null) { return; }
            Chatter.ClearQueue();
            Say(SIT_IMPACT_DOWN, "");
        }

        private void Say(int situation, string left) { SayTable(situation, left, LineEvent, LineSpeaker, LineText, LineClip); }

        // ───────────── 모든 클라이언트 ─────────────

        public bool IsLive() { return Manager != null && _stamp != 0 && _stamp == CurrentStamp(); }
        public int GetStage() { return _stage; }
        public int GetFireMs() { return _cycleMs + Mathf.RoundToInt(_charge * 1000f); }
        /// 첫 충전을 걸었는가(그 전엔 대기 — 충전·발사·경고·장갑 열림 없음).
        public bool IsArmed() { return IsLive() && _armed; }
        public int GetCycleMs() { return _cycleMs; }

        /// 포의 충전 정도(0~1). 발사 뒤 재장전 동안 내려온다 — 포신 각도·빛·소리.
        public float GetChargeRatio(int now)
        {
            if (!_armed) { return 0f; }
            float t = (now - _cycleMs) * 0.001f;
            if (t < _charge) { return Mathf.Clamp01(t / Mathf.Max(_charge, 0.1f)); }
            return Mathf.Clamp01(1f - (t - _charge) / Mathf.Max(ReloadTime, 0.1f));
        }

        /// 발사 임박: 사선 레이저를 켠다.
        public bool IsWarning(int now)
        {
            if (!_armed) { return false; }
            int left = GetFireMs() - now;
            return left > 0 && left <= Mathf.RoundToInt(WarnTime * 1000f);
        }

        /// 코어 장갑이 열려 있는가(코어 단계, 충전 끝자락 + 재장전).
        public bool IsCoreOpen(int now)
        {
            if (_stage < ACO_Railgun.STAGE_CORE || !_armed) { return false; }
            return GetFireMs() - now <= Mathf.RoundToInt(CoreOpenBefore * 1000f);
        }

        public override string GetDescription()
        {
            if (!IsLive()) { return Description; }
            if (_stage == ACO_Railgun.STAGE_AIR_DEFENSE) { return "DESTROY THE AIR DEFENSES"; }
            if (_stage == ACO_Railgun.STAGE_COOLERS) { return "DESTROY THE COOLANT TOWERS"; }
            return "DESTROY THE CORE WHEN THE ARMOR OPENS";
        }

        /// "AA 5/16  BASE 3/3  FIRE 1:12" → "CLR 2/4 ..." → "CORE 72% ...". 발사 20초 전이면 빨갛게, 코어가 열리면 OPEN.
        public override string GetProgressText()
        {
            ACO_Railgun gun = Gun();
            if (gun == null || !IsLive()) { return ""; }
            int now = Networking.GetServerTimeInMilliseconds();
            string s;
            if (_stage == ACO_Railgun.STAGE_AIR_DEFENSE) { s = "AA " + gun.CountAirDefenseAlive() + "/" + gun.GetAirDefenseTotal(); }
            else if (_stage == ACO_Railgun.STAGE_COOLERS) { s = "CLR " + gun.CountCoolersAlive() + "/" + gun.GetCoolerTotal(); }
            else { s = "CORE " + Mathf.RoundToInt(gun.GetCoreHealthRatio() * 100f) + "%"; }
            s += "  BASE " + Mathf.Max(BaseHits - _hits, 0) + "/" + BaseHits;
            if (!_armed) { return s + "  STANDBY"; }
            int left = GetFireMs() - now;
            if (left <= 0) { return s + "  RELOAD"; }
            int sec = Mathf.CeilToInt(left * 0.001f);
            string fire = "FIRE " + (sec / 60) + ":" + (sec % 60).ToString("00");
            if (sec <= 20) { fire = "<color=#FF5544>" + fire + "</color>"; }
            s += "  " + fire;
            if (IsCoreOpen(now)) { s += "  <color=#FFD24D>OPEN</color>"; }
            return s;
        }
    }
}
