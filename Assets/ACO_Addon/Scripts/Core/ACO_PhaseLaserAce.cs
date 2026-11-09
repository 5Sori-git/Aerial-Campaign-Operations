using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 레이저 에이스 페이즈(공중 보스, 단독) — 레이저를 쓰는 에이스(ACO_LaserAce) 한 기.
    ///  1) 레이저: 에이스가 기수 앞 원뿔 안의 적을 계속 비춘다. 락온은 평소처럼.
    ///  2) 체력 절반: 작전 지역 안 사일로에서 탄도미사일이 오른다(TransitionTime 뒤 발사, 그동안 에이스는 무적). 에이스는 ECM을 켜 정면에서만 락온된다.
    ///     미사일은 에이스의 관제에 묶여 있어 에이스를 격추하면 대기권에서 자폭한다(성공). ImpactTime초 안에 못 잡으면 작전 지역 밖 아군 기지에 떨어져 미션 실패.
    /// 사일로·미사일·섬광 연출은 ACO_IcbmSite(이 모듈의 자식)가 이 모듈의 동기화 값과 서버 시각으로 모든 화면에서 같게 그린다.
    ///
    /// 자체 동기화(Manual): 단계, 단계 시작 시각, 사일로 자리, 착탄 방향. 판정은 관리자 오너만. 페이즈 도장으로 지난 값을 무시한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseLaserAce : ACO_PhaseBoss
    {
        public const int STAGE_LASER = 0;
        public const int STAGE_ICBM = 1;

        [Header("배치")]
        [Tooltip("사일로: 작전 지역 중심에서의 거리(m)")]
        public float SiloDistanceMin = 2500f;
        public float SiloDistanceMax = 7000f;
        [Tooltip("사일로 받침 반지름(m) — 이 둘레의 땅 높이 차가 SiloMaxRelief 이하인 평지를 고른다(받침은 가장 높은 곳에 — 낮은 쪽은 받침 옆면이 1m 묻힌다)")]
        public float SiloPadRadius = 20f;
        public float SiloMaxRelief = 2.5f;
        [Tooltip("이 페이즈가 시작될 때 미션 제한 시간에 더하는 시간(초) — 탄도미사일 시간보다 넉넉히")]
        public float ExtraTime = 600f;

        [Header("탄도미사일")]
        [Tooltip("2단계가 시작되고 미사일이 발사되기까지(초) — 사일로가 열리고 대사가 나온다. 그동안 에이스는 무적")]
        public float TransitionTime = 3f;
        [Tooltip("발사부터 착탄까지(초)")]
        public float ImpactTime = 300f;
        [Tooltip("착탄 자리: 사일로에서 이 거리(m) — 작전 지역 밖 아군 기지(지평선 너머)")]
        public float ImpactDistance = 60000f;

        [Header("끝맺음")]
        [Tooltip("에이스 격추 뒤 성공으로 넘어가기까지(초) — 미사일 자폭이 보이게")]
        public float ClearDelay = 6f;
        [Tooltip("착탄 뒤 미션 실패로 넘어가기까지(초)")]
        public float FailDelay = 7f;

        [Header("연출")]
        [Tooltip("사일로·미사일·섬광(이 모듈의 자식 — 빌더가 만든다)")]
        public ACO_IcbmSite Site;

        public const int SIT_SPAWN = 0;
        public const int SIT_LAUNCH = 1;
        public const int SIT_ECM = 2;
        public const int SIT_T120 = 3;
        public const int SIT_T60 = 4;
        public const int SIT_T30 = 5;
        public const int SIT_DOWN = 6;
        public const int SIT_IMPACT = 7;

        [Header("상황별 대사 표 (같은 순서의 배열 — 한 줄 = 대사 하나)")]
        // 에이스(BORDERLESS) 대사 주제: 탄도미사일 한 발로 전쟁을 끝내고 국경을 지우겠다. 등장·격추 대사는 에이스 스크립트(ACO_LaserAce)에 있다.
        public int[] LineEvent = new int[] {
            SIT_SPAWN,
            SIT_LAUNCH, SIT_LAUNCH, SIT_LAUNCH,
            SIT_ECM, SIT_ECM,
            SIT_T120, SIT_T120,
            SIT_T60, SIT_T60,
            SIT_T30, SIT_T30,
            SIT_DOWN,
            SIT_IMPACT, SIT_IMPACT };
        public int[] LineSpeaker = new int[] {
            SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_AWACS, SPEAKER_AWACS,
            SPEAKER_AWACS, SPEAKER_ENEMY,
            SPEAKER_AWACS, SPEAKER_ENEMY,
            SPEAKER_AWACS, SPEAKER_ENEMY,
            SPEAKER_AWACS, SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS, SPEAKER_ENEMY };
        public string[] LineText = new string[] {
            "Single bogey, closing fast. It is carrying a directed-energy weapon. Do not sit in front of its nose!",
            "Launch authority confirmed. One warhead ends this war... and every border with it.",
            "Ground launch detected! A ballistic missile is climbing out of a silo. Its target is our base!",
            "The warhead is slaved to that ace's fire control. Shoot the ace down and the missile dies with it!",
            "Heavy jamming! You can only get a lock from its front. Go head-on, and watch that laser!",
            "Come at me head-on, if you still think your flag is worth dying for.",
            "Two minutes to impact!",
            "Two minutes. After that, there will be no nations left to fight over.",
            "One minute! Bring that ace down!",
            "Why keep fighting? In one minute, this war ends whether you win or not.",
            "Thirty seconds! It is now or never!",
            "Thirty seconds. Take one last look at the world they taught you to divide.",
            "The ace is down! The missile lost guidance... it detonated in the upper atmosphere!",
            "Impact... The base is gone.",
            "It's done. No more borders... no more war." };
        public AudioClip[] LineClip = new AudioClip[15];

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _stage;
        [UdonSynced] private int _stageMs;     // 2단계가 시작된 시각
        [UdonSynced] private Vector3 _silo;
        [UdonSynced] private int _impactYaw;   // 사일로에서 착탄 자리 쪽(도)

        // 오너 지역 상태
        private Vector3 _planSilo;
        private bool _planOk;
        private int _downMs;
        private int _lostMs;

        // 대사(모든 클라이언트)
        private int _lineStamp;
        private int _saidStage;
        private int _saidLeft;
        private bool _saidDown;
        private bool _saidImpact;
        private float _lineTimer;

        private ACO_LaserAce Ace() { return Generator != null ? Generator.GetLaserAce() : null; }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null || !Generator.CanDeployLaserAce()) { return false; }
            _planOk = Generator.FindPadSite(focus, SiloDistanceMin, SiloDistanceMax, SiloPadRadius, SiloMaxRelief);
            if (_planOk) { _planSilo = Generator.GetPadSite(); }
            return _planOk;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 100, false);
            if (!_planOk && !IsFeasible(phaseIndex, focus)) { return; }
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_LASER_ACE, tag, 0, -1, focus, Manager.FlightDistanceMin, Manager.FlightDistanceMax);
            Manager.SetModuleSite(-1);
            Manager.AddModuleTime(ExtraTime);
            StartAt(_planSilo);
        }

        private void StartAt(Vector3 silo)
        {
            _stamp = CurrentStamp();
            _stage = STAGE_LASER;
            _stageMs = 0;
            _silo = silo;
            _impactYaw = Random.Range(0, 360);
            _downMs = 0;
            _lostMs = 0;
            SyncNow();
        }

        public override int Tick(int now)
        {
            ACO_LaserAce ace = Ace();
            if (ace == null) { return RESULT_REPLACE; }
            int tag = Manager.GetModuleTag();
            Manager.CountTaggedObjectives(tag);
            int total = Manager.GetTaggedTotal();
            if (_stamp != CurrentStamp())
            {
                // 이번 페이즈 값을 못 받은 새 오너: 사일로를 다시 정한다(드문 경우)
                if (total == 0) { return now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f) ? RESULT_REPLACE : RESULT_RUNNING; }
                Vector3 focus = ace.transform.position;
                StartAt(Generator.FindPadSite(focus, SiloDistanceMin, SiloDistanceMax, SiloPadRadius, SiloMaxRelief) ? Generator.GetPadSite() : new Vector3(focus.x, 0f, focus.z));
            }
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            if (_lostMs != 0)
            {
                if (now - _lostMs < Mathf.RoundToInt(FailDelay * 1000f)) { return RESULT_RUNNING; }
                Manager.ScheduleModuleRelease();
                return RESULT_FAIL;
            }
            if (Manager.GetTaggedDone() >= total)
            {
                if (_downMs == 0) { _downMs = now; }
                if (now - _downMs < Mathf.RoundToInt(ClearDelay * 1000f)) { return RESULT_RUNNING; }
                Manager.AddModuleTime((now - _downMs) * 0.001f);
                Generator.RequestReleaseTag(tag);
                return RESULT_CLEAR;
            }
            if (now - Manager.GetPhaseStartMs() < Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f)) { return RESULT_RUNNING; }

            float ratio = AceHealthRatio(ace);
            Manager.SetModuleProgress(Mathf.RoundToInt((1f - ratio) * 100f), 100, false);
            if (_stage == STAGE_LASER)
            {
                if (ratio <= 0.5f + 0.001f)
                {
                    _stage = STAGE_ICBM;
                    _stageMs = now;
                    SyncNow();
                    Debug.Log("[ACO] 레이저 에이스: 2단계 — 탄도미사일 발사");
                }
                return RESULT_RUNNING;
            }
            if (now - GetImpactMs() >= 0) { _lostMs = now; }
            return RESULT_RUNNING;
        }

        private float AceHealthRatio(ACO_LaserAce ace)
        {
            if (ace == null || ace.TargetHealth == null || ace.TargetHealth.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(ace.TargetHealth.Health / ace.TargetHealth.FullHealth);
        }

        public override void End()
        {
            _planOk = false;
            _downMs = 0;
            _lostMs = 0;
        }

        public override bool HoldsResult() { return _downMs != 0 || _lostMs != 0; }

        // ───────────── 모든 클라이언트 ─────────────

        /// 이번 페이즈 값을 받았는가.
        public bool IsLive() { return Manager != null && _stamp != 0 && _stamp == CurrentStamp(); }
        public int GetStage() { return IsLive() ? _stage : STAGE_LASER; }
        /// 2단계(탄도미사일)가 시작됐는가.
        public bool IsLaunchStarted() { return IsLive() && _stage == STAGE_ICBM; }
        /// ECM: 2단계 동안 켜져 있다.
        public bool IsEcmOn() { return IsLaunchStarted(); }
        /// 에이스 체력을 절반 아래로 내릴 수 있는가 — 2단계가 시작되고 전환 연출(TransitionTime)이 끝난 뒤.
        public bool IsVulnerable() { return IsLaunchStarted() && Networking.GetServerTimeInMilliseconds() - GetLaunchMs() >= 0; }
        public int GetStageMs() { return _stageMs; }
        public int GetLaunchMs() { return _stageMs + Mathf.RoundToInt(TransitionTime * 1000f); }
        public int GetImpactMs() { return GetLaunchMs() + Mathf.RoundToInt(ImpactTime * 1000f); }
        public Vector3 GetSiloPosition() { return _silo; }
        public Vector3 GetImpactDirection()
        {
            float a = _impactYaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }
        public Vector3 GetImpactPoint() { return _silo + GetImpactDirection() * ImpactDistance; }
        /// 에이스가 격추됐는가(모든 클라이언트 — 체력 동기화).
        public bool IsAceDown()
        {
            ACO_LaserAce ace = Ace();
            return ace != null && ace.TargetHealth != null && ace.TargetHealth.Health <= 0f;
        }
        /// 착탄까지 남은 초(2단계가 아니면 -1).
        public float GetSecondsLeft()
        {
            if (!IsLaunchStarted()) { return -1f; }
            return Mathf.Max((GetImpactMs() - Networking.GetServerTimeInMilliseconds()) * 0.001f, 0f);
        }

        void Update()
        {
            _lineTimer -= Time.deltaTime;
            if (_lineTimer > 0f) { return; }
            _lineTimer = 0.5f;
            if (IsLive()) { CheckLines(); }
        }

        /// 대사: 동기화된 단계·시각·에이스 체력이 바뀌었으면 그 상황의 대사를 한 번 말한다.
        private void CheckLines()
        {
            if (Chatter == null) { return; }
            float left = GetSecondsLeft();
            bool down = IsAceDown();
            if (_lineStamp != _stamp)
            {
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineStamp = _stamp;
                _saidStage = _stage;
                _saidLeft = left < 0f ? 999 : Mathf.CeilToInt(left);
                _saidDown = down;
                _saidImpact = left == 0f;
                if (_stage == STAGE_LASER && !down) { Say(SIT_SPAWN); }
                return;
            }
            if (down && !_saidDown)
            {
                _saidDown = true;
                if (_stage == STAGE_ICBM && left > 0f) { Say(SIT_DOWN); }
                return;
            }
            if (down) { return; }
            if (_stage != _saidStage)
            {
                _saidStage = _stage;
                if (_stage == STAGE_ICBM) { Say(SIT_LAUNCH); Say(SIT_ECM); }
                return;
            }
            if (left < 0f) { return; }
            if (left == 0f && !_saidImpact)
            {
                _saidImpact = true;
                Chatter.ClearQueue();
                Say(SIT_IMPACT);
                return;
            }
            int s = Mathf.CeilToInt(left);
            if (_saidLeft > 120 && s <= 120) { Say(SIT_T120); }
            else if (_saidLeft > 60 && s <= 60) { Say(SIT_T60); }
            else if (_saidLeft > 30 && s <= 30) { Say(SIT_T30); }
            _saidLeft = s;
        }

        private void Say(int situation) { SayTable(situation, "", LineEvent, LineSpeaker, LineText, LineClip); }

        private string AceName()
        {
            ACO_LaserAce ace = Ace();
            return ace != null ? ace.AceName : "ACE";
        }

        public override string GetName()
        {
            return PhaseName.Length > 0 && PhaseName != "MODULE" ? PhaseName.Replace("{ACE}", AceName()) : "ACE - " + AceName();
        }

        public override string GetDescription()
        {
            if (!IsLive() || _stage == STAGE_LASER) { return (Description.Length > 0 ? Description : "SHOOT DOWN {ACE}").Replace("{ACE}", AceName()); }
            return "SHOOT DOWN " + AceName() + " BEFORE THE MISSILE HITS THE BASE";
        }

        /// "ACE 72%" → "ICBM 4:12  ACE 38%"(1분 아래면 빨갛게).
        public override string GetProgressText()
        {
            if (!IsLive()) { return ""; }
            ACO_LaserAce ace = Ace();
            string hp = "ACE " + Mathf.RoundToInt(AceHealthRatio(ace) * 100f) + "%";
            float left = GetSecondsLeft();
            if (left < 0f) { return hp; }
            int s = Mathf.CeilToInt(left);
            string t = "ICBM " + (s / 60) + ":" + (s % 60).ToString("00");
            if (s <= 60) { t = "<color=#FF5544>" + t + "</color>"; }
            return t + "  " + hp;
        }
    }
}
