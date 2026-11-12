using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 공중 요새 페이즈(모듈) — 거대 보스 ACO_FlyingFortress가 도시 둘레를 돌다 도시로 돌진한다. 세 단계로 진행한다.
    /// 나오는 때는 PhaseWeights로 정한다(기본은 5번째 페이즈에서만). 오래 걸리므로 나올 때 미션 제한 시간에 ExtraTime을 더한다.
    ///  1) 방공망: 대공포·CIWS·SAM을 모두 부순다. 엔진·코어는 아직 잠기지도 맞지도 않는다.
    ///  2) 엔진: 엔진을 모두 부순다. 부서질수록 느려진다. 방공망 일부가 주기적으로 수리돼 다시 돈다(RepairInterval마다 RepairCount곳).
    ///  3) 코어: 코어가 드러나고 요새가 선회해 도시로 곧게 돌진한다. 도시에 닿기 전에 코어를 부수면 성공, 닿으면 미션 실패. 수리는 계속된다.
    /// 끝맺음: 결과를 바로 내지 않는다 — 격추는 땅·바다에 충돌한 뒤 CrashClearDelay, 도시 충돌은 CityFailDelay 뒤에 성공·실패로 넘어가 폭발이 보이게 한다. 기다리는 동안은 제한 시간으로 실패하지 않는다.
    /// 방공망이 다시 돌아도 단계는 돌아가지 않는다 — 수리된 포대는 다시 부수지 않아도 된다.
    ///
    /// 자체 동기화(Manual): 단계와 궤도·돌진 기준값. 위치 자체는 보내지 않고, 모든 클라이언트가 이 값과 서버 시각으로 같은 자리를 계산한다.
    /// 값은 단계가 바뀌거나 엔진이 부서질 때·수리할 때만 보낸다. 판정은 관리자 오너만.
    ///
    /// 대사: 상황별 대사 표(LineEvent·LineSpeaker·LineText·LineClip — 같은 순서의 배열, 한 줄 = 대사 하나). 같은 상황의 줄은 위에서 아래로 차례로 나온다 — 적 줄 뒤에 AWACS 줄을 두면 이어서 설명한다.
    /// AWACS 음성은 이 미션에서만 쓰므로 공용 채터가 아니라 이 표에 넣는다. 페이즈 시작·성공·실패 관제 대사는 공통 칸(StartText·SuccessText·FailChatterText).
    /// 동기화된 값과 부위 체력만 보고 모든 클라이언트가 각자 재생한다(ACO_FlyingFortress가 0.5초마다 CheckLines) — 슈퍼에이스와 같다. 늦게 들어온 클라이언트는 지난 대사를 다시 듣지 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseFortress : ACO_PhaseBoss
    {
        [Header("공중 요새")]
        [Tooltip("단계가 바뀔 때 측면 해치에서 출격하는 적 전투기 수")]
        public int ReinforceFighters = 4;
        [Tooltip("엔진 단계가 시작된 뒤 첫 기가 나오기까지 최소 시간(초) — 측면 해치가 열리는 시간. 대사가 더 길면 대사 뒤에 나온다")]
        public float LaunchDelay = 4f;
        [Tooltip("출격 간격(초) — 좌우 해치를 번갈아 한 기씩")]
        public float LaunchSpacing = 2f;
        [Tooltip("대사가 끝난 뒤 다음 일까지 두는 여유(초)")]
        public float StageLineGap = 1f;
        [Tooltip("방공망 수리 간격(초) — 엔진·코어 단계")]
        public float RepairInterval = 40f;
        [Tooltip("한 번에 수리되는 방공 부위 수")]
        public int RepairCount = 3;
        [Tooltip("수리는 살아 있는 방공 부위가 이 수보다 적을 때만, 이 수까지(한 번에 RepairCount곳). 0이면 한도 없이 RepairCount곳씩 — 너무 많이 되살아나면 부순 의미가 없다")]
        public int RepairAliveCap = 6;
        [HideInInspector] public int MaxRepairs = 6;   // 예전 값(페이즈 전체 합계 — 이제 쓰지 않는다, 저장된 값만 남긴다)
        [Tooltip("이 페이즈가 시작될 때 미션 제한 시간에 더하는 시간(초) — 세 단계를 끝낼 시간")]
        public float ExtraTime = 600f;
        [Tooltip("엔진이 모두 부서졌을 때의 궤도 속도 배율 — 코어 단계로 넘어가기 직전 값")]
        [Range(0.1f, 1f)] public float MinSpeedScale = 0.45f;

        [Tooltip("돌진 중 도시까지 이 거리(m)면 도시 접근 대사 (먼·가까운·마지막)")]
        public float CityWarnFar = 5000f;
        public float CityWarnNear = 3000f;
        public float CityWarnLast = 1000f;
        [Tooltip("충돌 대사가 폭발 뒤 나오기까지(초)")]
        public float ImpactLineDelay = 3f;

        [Header("끝맺음")]
        [Tooltip("돌진 시작을 늦추는 시간(초) — 모든 클라이언트가 같은 자리에서 궤도를 벗어나게")]
        public float DiveLead = 1.5f;
        [Tooltip("격추된 요새가 땅·바다에 충돌한 뒤 성공으로 넘어가기까지(초)")]
        public float CrashClearDelay = 4f;
        [Tooltip("충돌을 못 봐도 이 시간(초)이 지나면 성공으로 넘어간다")]
        public float CrashHoldMax = 30f;
        [Tooltip("도시에 충돌한 뒤 미션 실패로 넘어가기까지(초)")]
        public float CityFailDelay = 6f;
        [Tooltip("도시에 충돌한 뒤 요새를 풀로 거두기까지(초) — 격추와 달리 체력이 남아 있어, 거두기 전엔 부위가 지하에서 살아 있다")]
        public float CityHideDelay = 1f;

        // 대사 표의 상황 번호 — ACO_PhaseFortressEditor의 이름표와 같은 순서
        public const int SIT_SPAWN = 0;
        public const int SIT_AA_HALF = 1;
        public const int SIT_ENGINES = 2;
        public const int SIT_ENGINE_DOWN = 3;
        public const int SIT_REPAIR = 4;
        public const int SIT_REINFORCE = 5;
        public const int SIT_DIVE = 6;
        public const int SIT_CORE_HALF = 7;
        public const int SIT_CITY_FAR = 8;
        public const int SIT_CITY_NEAR = 9;
        public const int SIT_CITY_LAST = 10;
        public const int SIT_CORE_DOWN = 11;
        public const int SIT_IMPACT_DOWN = 12;
        public const int SIT_IMPACT_CITY = 13;

        [Header("상황별 대사 표 (같은 순서의 배열 — 한 줄 = 대사 하나)")]
        [Tooltip("이 대사가 나오는 상황(SIT_*)")]
        public int[] LineEvent = new int[] {
            SIT_SPAWN,
            SIT_AA_HALF, SIT_AA_HALF,
            SIT_ENGINES, SIT_ENGINES,
            SIT_ENGINE_DOWN, SIT_ENGINE_DOWN,
            SIT_REPAIR, SIT_REPAIR,
            SIT_REINFORCE, SIT_REINFORCE,
            SIT_DIVE, SIT_DIVE,
            SIT_CORE_HALF, SIT_CORE_HALF,
            SIT_CITY_FAR,
            SIT_CITY_NEAR,
            SIT_CITY_LAST, SIT_CITY_LAST,
            SIT_CORE_DOWN, SIT_CORE_DOWN,
            SIT_IMPACT_DOWN,
            SIT_IMPACT_CITY };
        [Tooltip("말하는 쪽(0 = 적, 1 = AWACS)")]
        public int[] LineSpeaker = new int[] {
            SPEAKER_ENEMY,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS };
        [Tooltip("대사. {left} = 남은 엔진 수")]
        public string[] LineText = new string[] {
            "This city is finished. Nothing you fly can stop this ship.",
            "Half the batteries are gone! Hold them off!",
            "Their air defenses are thinning out. Keep it up!",
            "Enemy aircraft at the engines. All stations, repair the batteries!",
            "Their air defenses are down! Go for the engines, slow that thing down!",
            "We have lost an engine! Hold course!",
            "Engine down! {left} left!",
            "Batteries restored. Resume fire.",
            "Some of their guns are back online. Watch yourselves!",
            "All squadrons, scramble! Protect the ship!",
            "Enemy fighters launching around the fortress!",
            "If we fall, we fall on them. Set course for the city!",
            "All engines down, but it is turning for the city! The core is exposed, hit it now!",
            "Core integrity at fifty percent! Divert everything to shields!",
            "The core is failing! Keep hitting it!",
            "Five kilometers to the city!",
            "Three kilometers! Bring it down, now!",
            "Nothing can stop us now!",
            "One kilometer! Hit it with everything you have!",
            "Impossible... The core... We are going down!",
            "Core destroyed! It is going down!",
            "Impact confirmed. The fortress is gone.",
            "The fortress has hit the city... We were too late." };
        [Tooltip("음성(선택 — 비우면 자막만)")]
        public AudioClip[] LineClip = new AudioClip[23];

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _stage;
        [UdonSynced] private float _baseAngle;
        [UdonSynced] private int _baseMs;
        [UdonSynced] private float _speedScale = 1f;
        [UdonSynced] private int _diveMs;
        [UdonSynced] private Vector3 _divePos;
        [UdonSynced] private int _repairs;
        [UdonSynced] private int _repairedUnits;   // 지금까지 되살린 부위 수(MaxRepairs와 비교 — 새 오너도 이어서 센다)
        [UdonSynced] private int _reinforceMs;     // 증원 대사·출격을 시작하는 서버 시각(0 = 없음)

        // 대사(모든 클라이언트 지역 상태)
        private int _lineStamp;
        private int _saidStage;
        private int _saidEngines;
        private int _saidRepairs;
        private int _saidReinforceMs;
        private bool _saidHalf;
        private bool _saidAaHalf;
        private int _saidCity;     // 지나온 도시 접근 경고 수(0~3)
        private int _impactLine;

        private int _planSite = -1;
        private int _planYaw;
        private int _nextRepairMs;
        private int _enginesAlive = -1;
        private int _coreDownMs;   // 코어가 부서진 걸 본 시각(0 = 아직, 오너 지역 상태)
        private int _cityHitMs;    // 도시에 닿은 걸 본 시각(0 = 아직, 오너 지역 상태)
        private bool _cityReleased;
        private int _launchLeft;    // 아직 안 나온 증원기(오너 지역 상태 — 오너가 바뀌면 남은 기체는 나오지 않는다)
        private int _nextLaunchMs;
        private int _launchSide;

        private ACO_FlyingFortress Fortress() { return Generator != null ? Generator.Fortress : null; }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null || !Generator.CanDeployFortress()) { return false; }
            int site = Generator.FindCitySite(focus);
            if (site < 0) { return false; }
            _planSite = site;
            _planYaw = Random.Range(0, 360);
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 1, false);
            // 대체 시작(PickFallbackType)은 IsFeasible을 거치지 않는다: 자리 없이 GetSitePosition(-1)을 부르면 지형 스크립트가 멈춘다.
            // 배치하지 않고 돌아가면 Tick이 배치 시한 뒤 RESULT_REPLACE로 바꾼다.
            if (_planSite < 0 && !IsFeasible(phaseIndex, focus)) { return; }
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FORTRESS, tag, 1, _planYaw, Generator.GetSitePosition(_planSite), 0f, 0f);
            Manager.SetModuleSite(_planSite);
            Manager.AddModuleTime(ExtraTime);
            StartAt(now, (_planYaw + 0.5f) * Mathf.Deg2Rad);
        }

        private void StartAt(int now, float angle)
        {
            _stamp = CurrentStamp();
            _stage = ACO_FlyingFortress.STAGE_AIR_DEFENSE;
            _baseAngle = angle;
            _baseMs = now;
            _speedScale = 1f;
            _repairs = 0;
            _repairedUnits = 0;
            _enginesAlive = -1;
            _coreDownMs = 0;
            _cityHitMs = 0;
            _cityReleased = false;
            _launchLeft = 0;
            _reinforceMs = 0;
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            SyncNow();
        }

        public override int Tick(int now)
        {
            ACO_FlyingFortress fort = Fortress();
            if (fort == null) { return RESULT_REPLACE; }
            // 이번 페이즈 값을 못 받은 새 오너: 지금 자리에서 첫 단계로 이어 간다(단계는 부위 상태로 다시 올라간다)
            if (_stamp != CurrentStamp()) { StartAt(now, fort.GetOrbitAngle(now)); }

            int tag = Manager.GetModuleTag();
            Manager.CountTaggedObjectives(tag);
            int total = Manager.GetTaggedTotal();
            if (total > 0 && Manager.GetTaggedDone() >= total)
            {
                // 격추: 충돌 폭발이 보이게 기다린다. 기다린 시간은 시간 보너스에서 빼지 않게 돌려준다.
                if (_coreDownMs == 0) { _coreDownMs = now; }
                float age = fort.GetImpactAge();
                bool done = age >= 0f ? age >= CrashClearDelay : now - _coreDownMs >= Mathf.RoundToInt(CrashHoldMax * 1000f);
                if (!done) { return RESULT_RUNNING; }
                Manager.AddModuleTime((now - _coreDownMs) * 0.001f);
                // 지하로 옮겨 숨긴 동체를 풀로 거둔다 — 안 거두면 다음 전체 배치까지 켜진 채 남는다(점수는 이미 집계됐다)
                Generator.RequestReleaseTag(tag);
                return RESULT_CLEAR;
            }
            // 도시 충돌: 격추 판정 뒤에 본다(돌진 진행은 시간 함수라 격추된 뒤에도 1까지 찬다).
            // 거둔 뒤엔 total이 0이 되므로 배치 대기보다 앞에 둔다 — 새 오너도 같은 자리에서 다시 찾는다.
            if (_cityHitMs == 0 && _stage == ACO_FlyingFortress.STAGE_CORE && fort.GetDiveProgress(now) >= 1f) { _cityHitMs = now; }
            if (_cityHitMs != 0)
            {
                bool fail = now - _cityHitMs >= Mathf.RoundToInt(CityFailDelay * 1000f);
                if (!_cityReleased && (fail || now - _cityHitMs >= Mathf.RoundToInt(CityHideDelay * 1000f)))
                {
                    // 체력이 남은 채 지하로 치워져 있다: 마커·레이더·무장이 살아 있으므로 바로 거둔다
                    _cityReleased = true;
                    Generator.RequestReleaseTag(tag);
                }
                return fail ? RESULT_FAIL : RESULT_RUNNING;
            }
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            // 배치 직후엔 부위 체력이 부활 동기화 전이라 0으로 보인다 — 그동안 단계를 판정하지 않는다
            if (now - Manager.GetPhaseStartMs() < Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f)) { return RESULT_RUNNING; }

            TickLaunch(now, fort);

            if (_stage == ACO_FlyingFortress.STAGE_AIR_DEFENSE)
            {
                int alive = fort.CountAirDefenseAlive();
                int all = fort.GetAirDefenseTotal();
                Manager.SetModuleProgress(all - alive, all, false);
                if (alive == 0) { NextStage(now, fort, ACO_FlyingFortress.STAGE_ENGINES); }
                return RESULT_RUNNING;
            }

            TickRepair(now, fort);

            if (_stage == ACO_FlyingFortress.STAGE_ENGINES)
            {
                int alive = fort.CountEnginesAlive();
                int all = fort.GetEngineTotal();
                Manager.SetModuleProgress(all - alive, all, false);
                if (alive == 0)
                {
                    // 조금 뒤로 예약한다: 그 시각의 궤도 자리에서 시작하므로 어느 클라이언트도 자리를 건너뛰지 않는다
                    _diveMs = now + Mathf.RoundToInt(DiveLead * 1000f);
                    _divePos = fort.GetOrbitPosition(_diveMs);
                    NextStage(now, fort, ACO_FlyingFortress.STAGE_CORE);
                }
                else if (alive != _enginesAlive)
                {
                    // 엔진이 부서졌다: 지금 각도에서 기준을 다시 잡아 느려진다(자리가 이어진다)
                    _baseAngle = fort.GetOrbitAngle(now);
                    _baseMs = now;
                    _enginesAlive = alive;
                    _speedScale = Mathf.Lerp(MinSpeedScale, 1f, all > 0 ? (float)alive / all : 0f);
                    SyncNow();
                }
                return RESULT_RUNNING;
            }

            Manager.SetModuleProgress(Mathf.RoundToInt((1f - fort.GetCoreHealthRatio()) * 100f), 100, false);
            return RESULT_RUNNING;
        }

        private void NextStage(int now, ACO_FlyingFortress fort, int stage)
        {
            _stage = stage;
            _enginesAlive = -1;
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            _reinforceMs = 0;
            if (ReinforceFighters > 0)
            {
                // 이 단계의 대사가 끝난 뒤 증원 대사가 나오고 적기가 뜬다 — 한꺼번에 넣으면 밀려서 잘린다
                bool engines = stage == ACO_FlyingFortress.STAGE_ENGINES;
                int lead = LineMs(engines ? SIT_ENGINES : SIT_DIVE);
                if (engines) { lead = Mathf.Max(lead, Mathf.RoundToInt(LaunchDelay * 1000f)); }
                _reinforceMs = now + lead;
                if (fort.GetLaunchPointCount() > 0)
                {
                    _launchLeft += ReinforceFighters;
                    _nextLaunchMs = _reinforceMs;
                }
                else
                {
                    Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_FLIGHT, 0, ReinforceFighters, -1, fort.transform.position, 800f, 1800f);
                }
            }
            SyncNow();
        }

        /// 이 상황의 대사가 끝나고 StageLineGap이 지날 때까지(ms).
        private int LineMs(int situation)
        {
            return Mathf.RoundToInt((TableTime(situation, LineEvent, LineText, LineClip) + StageLineGap) * 1000f);
        }

        /// 측면 해치에서 좌우 번갈아 한 기씩 내보낸다. 자리는 그 순간의 해치 자리다(요새가 움직인다).
        private void TickLaunch(int now, ACO_FlyingFortress fort)
        {
            if (_launchLeft <= 0 || now - _nextLaunchMs < 0) { return; }
            _launchLeft--;
            _nextLaunchMs = now + Mathf.RoundToInt(LaunchSpacing * 1000f);
            int side = _launchSide % fort.GetLaunchPointCount();
            _launchSide++;
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_HATCH_FLIGHT, 0, 1, -1, fort.GetLaunchPosition(side), fort.GetLaunchYaw(side), 0f);
        }

        private void TickRepair(int now, ACO_FlyingFortress fort)
        {
            if (RepairInterval <= 0f || now - _nextRepairMs < 0) { return; }
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            // 살아 있는 방공 부위가 RepairAliveCap보다 적을 때만, 그 수까지(페이즈 전체 합계 한도는 없다 — 6-150)
            int count = RepairAliveCap > 0 ? Mathf.Min(RepairCount, RepairAliveCap - fort.CountAirDefenseAlive()) : RepairCount;
            if (count <= 0) { return; }
            int done = fort.RepairAirDefense(count);
            if (done > 0)
            {
                _repairs++;
                _repairedUnits += done;
                SyncNow();
            }
        }

        public override void End()
        {
            _planSite = -1;
            _enginesAlive = -1;
            _coreDownMs = 0;
            _cityHitMs = 0;
            _cityReleased = false;
            _launchLeft = 0;
        }

        /// 격추·도시 충돌 뒤 결과를 기다리는 중 — 이때는 제한 시간으로 실패하지 않는다.
        public override bool HoldsResult() { return _coreDownMs != 0 || _cityHitMs != 0; }

        // ───────────── 대사 (모든 클라이언트) ─────────────

        /// 동기화된 상태가 바뀌었으면 그 상황의 대사를 재생한다 — ACO_FlyingFortress가 0.5초마다 부른다.
        public void CheckLines(ACO_FlyingFortress fort)
        {
            if (Chatter == null || !IsLive()) { return; }
            int engines = fort.CountEnginesAlive();
            float core = fort.GetCoreHealthRatio();
            int cityLevel = CityLevel(fort);
            bool aaHalf = fort.CountAirDefenseAlive() * 2 <= fort.GetAirDefenseTotal();
            if (_lineStamp != _stamp)
            {
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로 삼는다(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineStamp = _stamp;
                _saidStage = _stage;
                _saidEngines = engines;
                _saidRepairs = _repairs;
                _saidReinforceMs = _reinforceMs;
                _saidAaHalf = aaHalf;
                _saidHalf = _stage == ACO_FlyingFortress.STAGE_CORE && core <= 0.5f;
                _saidCity = cityLevel;
                if (_stage == ACO_FlyingFortress.STAGE_AIR_DEFENSE) { Say(SIT_SPAWN, ""); }
                return;
            }
            if (_stage != _saidStage)
            {
                _saidStage = _stage;
                _saidEngines = engines;
                if (_stage == ACO_FlyingFortress.STAGE_ENGINES) { Say(SIT_ENGINES, ""); }
                else if (_stage == ACO_FlyingFortress.STAGE_CORE) { Say(SIT_DIVE, ""); }
                return;
            }
            if (_reinforceMs != 0 && _reinforceMs != _saidReinforceMs && Networking.GetServerTimeInMilliseconds() - _reinforceMs >= 0)
            {
                _saidReinforceMs = _reinforceMs;
                Say(SIT_REINFORCE, "");
            }
            if (_stage == ACO_FlyingFortress.STAGE_AIR_DEFENSE && aaHalf && !_saidAaHalf)
            {
                _saidAaHalf = true;
                Say(SIT_AA_HALF, "");
            }
            if (_stage == ACO_FlyingFortress.STAGE_ENGINES && engines < _saidEngines)
            {
                _saidEngines = engines;
                // 마지막 엔진은 곧 돌진 대사가 나온다
                if (engines > 0) { Say(SIT_ENGINE_DOWN, engines.ToString()); }
            }
            if (_repairs != _saidRepairs)
            {
                _saidRepairs = _repairs;
                Say(SIT_REPAIR, "");
            }
            if (_stage != ACO_FlyingFortress.STAGE_CORE) { return; }
            if (!_saidHalf && core <= 0.5f)
            {
                _saidHalf = true;
                Say(SIT_CORE_HALF, "");
            }
            if (cityLevel > _saidCity)
            {
                _saidCity = cityLevel;
                // 도시 경고는 밀려 있는 대사보다 먼저다
                Chatter.ClearQueue();
                Say(cityLevel == 3 ? SIT_CITY_LAST : (cityLevel == 2 ? SIT_CITY_NEAR : SIT_CITY_FAR), "");
            }
        }

        /// 지나온 도시 접근 경고 수(돌진 단계가 아니면 0).
        private int CityLevel(ACO_FlyingFortress fort)
        {
            if (_stage != ACO_FlyingFortress.STAGE_CORE) { return 0; }
            float d = fort.GetCityDistance();
            return d <= CityWarnLast ? 3 : (d <= CityWarnNear ? 2 : (d <= CityWarnFar ? 1 : 0));
        }

        /// 코어가 파괴됐다(모든 클라이언트).
        public void SayDown() { Say(SIT_CORE_DOWN, ""); }

        /// 땅·바다·도시에 충돌했다(모든 클라이언트). 폭발이 보인 뒤에 말한다.
        public void SayImpact(bool city)
        {
            _impactLine = city ? SIT_IMPACT_CITY : SIT_IMPACT_DOWN;
            SendCustomEventDelayedSeconds(nameof(_SayImpactLine), ImpactLineDelay);
        }

        public void _SayImpactLine()
        {
            if (Chatter == null) { return; }
            Chatter.ClearQueue();
            Say(_impactLine, "");
        }

        private void Say(int situation, string left) { SayTable(situation, left, LineEvent, LineSpeaker, LineText, LineClip); }

        // ───────────── 모든 클라이언트 ─────────────

        /// 이번 페이즈 값을 받았는가. 아니면 요새는 스폰 기준으로 첫 단계처럼 돈다.
        public bool IsLive() { return Manager != null && _stamp != 0 && _stamp == CurrentStamp(); }
        public int GetStage() { return _stage; }
        public float GetBaseAngle() { return _baseAngle; }
        public int GetBaseMs() { return _baseMs; }
        public float GetSpeedScale() { return _speedScale; }
        public int GetDiveMs() { return _diveMs; }
        public Vector3 GetDivePos() { return _divePos; }

        public override string GetDescription()
        {
            if (!IsLive()) { return Description; }
            if (_stage == ACO_FlyingFortress.STAGE_AIR_DEFENSE) { return "DESTROY THE FORTRESS AIR DEFENSES"; }
            if (_stage == ACO_FlyingFortress.STAGE_ENGINES) { return "DESTROY THE ENGINES"; }
            return "DESTROY THE CORE BEFORE IT REACHES THE CITY";
        }

        /// "AA 5/18" → "ENG 3/4" → "CORE 72%  CITY 4.2km"(5km 아래면 빨갛게).
        public override string GetProgressText()
        {
            ACO_FlyingFortress fort = Fortress();
            if (fort == null || !IsLive()) { return ""; }
            if (_stage == ACO_FlyingFortress.STAGE_AIR_DEFENSE) { return "AA " + fort.CountAirDefenseAlive() + "/" + fort.GetAirDefenseTotal(); }
            if (_stage == ACO_FlyingFortress.STAGE_ENGINES) { return "ENG " + fort.CountEnginesAlive() + "/" + fort.GetEngineTotal(); }
            float near = fort.GetCityDistance();
            string dist = Dist(near);
            if (near < 5000f) { dist = "<color=#FF5544>" + dist + "</color>"; }
            return "CORE " + Mathf.RoundToInt(fort.GetCoreHealthRatio() * 100f) + "%  CITY " + dist;
        }

        public override string GetLocation()
        {
            if (Manager == null) { return ""; }
            string name = Manager.GetPhaseSiteName();
            return name.Length > 0 ? "DEFEND: " + name : "";
        }
    }
}
