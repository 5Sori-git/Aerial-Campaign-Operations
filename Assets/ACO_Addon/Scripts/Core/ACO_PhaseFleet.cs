using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 함대전 지원 페이즈(모듈) — 아군 기함(순양함)·호위함과 적 항모전단(항모·순양함·구축함)이 서로 다가가 FleetStopGap(3.5km)에서 멈추고 포격전을 벌인다.
    ///  - 성공: 적 항모(태그 목표) 격침. 실패: 아군 기함 격침(미션 실패).
    ///  - 항모가 살아 있는 동안 LaunchInterval마다 함재기(전투기·공격기)가 나온다.
    ///  - 양쪽 함포·VLS는 부위 그대로 서로를 노린다(적은 아군 기함을 우선 — 기함의 AttackPriority, PriorityForSites).
    /// 이동·배치는 생성기(RequestDeployFleet — 시간 함수라 위치 동기화 없음). 함선 포대는 서로의 선체 마커(SaccEntity 없음)가 가까우면 잠들지 않으므로
    /// 플레이어가 멀어도 포격전은 계속된다 — 돕지 않으면 기함이 진다.
    /// 판정은 생성기·체력 상태만 보므로 자체 동기화가 없다. 대사는 모든 클라이언트가 같은 상태를 보고 각자 재생한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PhaseFleet : ACO_PhaseBoss
    {
        [Header("함대 구성 (풀에 있는 만큼)")]
        [Range(0, 4)] public int Guards = 2;
        [Range(0, 4)] public int Cruisers = 2;
        [Range(0, 6)] public int Destroyers = 3;

        [Header("함재기 (적 항모가 살아 있는 동안)")]
        [Tooltip("출격 간격(초)")]
        public float LaunchInterval = 50f;
        public int LaunchFighters = 2;
        [Tooltip("공격기 — 아군 함선을 노린다")]
        public int LaunchAttackers = 2;
        [Tooltip("한 번 출격할 때 기체 사이 간격(초) — 캐터펄트처럼 한 기씩 뜬다")]
        public float LaunchSpacing = 4f;
        [Tooltip("이 페이즈가 시작될 때 미션 제한 시간에 더하는 시간(초)")]
        public float ExtraTime = 300f;
        [Tooltip("페이즈가 끝난 뒤 함대를 거두기까지(초) — 격침 연출이 보이게")]
        public float ReleaseDelay = 20f;

        // 대사 표의 상황 번호 — ACO_PhaseFortressEditor의 이름표와 같은 순서
        public const int SIT_CONTACT = 0;
        public const int SIT_LAUNCH = 1;
        public const int SIT_FLAGSHIP_HALF = 2;
        public const int SIT_FLAGSHIP_LOW = 3;
        public const int SIT_CARRIER_HALF = 4;

        [Header("상황별 대사 표 (같은 순서의 배열 — 한 줄 = 대사 하나)")]
        [Tooltip("이 대사가 나오는 상황(SIT_*)")]
        public int[] LineEvent = new int[] {
            SIT_CONTACT, SIT_CONTACT,
            SIT_LAUNCH, SIT_LAUNCH,
            SIT_FLAGSHIP_HALF, SIT_FLAGSHIP_HALF,
            SIT_FLAGSHIP_LOW, SIT_FLAGSHIP_LOW,
            SIT_CARRIER_HALF, SIT_CARRIER_HALF };
        [Tooltip("말하는 쪽(0 = 적, 1 = AWACS, 2 = 함대장)")]
        public int[] LineSpeaker = new int[] {
            SPEAKER_ENEMY, SPEAKER_ALLY,
            SPEAKER_ENEMY, SPEAKER_AWACS,
            SPEAKER_ENEMY, SPEAKER_ALLY,
            SPEAKER_ENEMY, SPEAKER_ALLY,
            SPEAKER_ENEMY, SPEAKER_AWACS };
        // Bake가 먼저 만든 표의 함대장 줄을 한 번 나눴다(그 뒤로 직접 바꾼 화자는 건드리지 않는다)
        [HideInInspector] public bool AllySpeakerApplied;
        [Tooltip("대사")]
        public string[] LineText = new string[] {
            "Enemy fleet in range. All batteries, commence firing!",
            "Both fleets are in gun range! All ships, open fire!",
            "Launch all aircraft! Find that flagship and sink it!",
            "Enemy carrier is launching aircraft!",
            "Their flagship is burning! Concentrate your fire!",
            "Flagship is taking heavy fire! Get those ships off us!",
            "One more salvo and she goes down!",
            "Flagship is critical! We cannot take much more!",
            "The carrier is hit! Damage control, now!",
            "The carrier is burning! Finish it!" };
        [Tooltip("음성(선택 — 비우면 자막만)")]
        public AudioClip[] LineClip = new AudioClip[10];

        // 오너 지역 상태
        private int _planSeed;
        private int _nextLaunchMs;
        private int _nextCatapultMs;
        private int _pendingFighters;
        private int _pendingAttackers;
        private bool _seenFlagship;
        private int _endedTag;

        // 대사(모든 클라이언트 지역 상태)
        private int _lineTag;
        private bool _saidContact;
        private bool _saidLaunch;
        private bool _saidFlagshipHalf;
        private bool _saidFlagshipLow;
        private bool _saidCarrierHalf;
        private float _lineTimer;

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null || !Generator.CanDeployFleet()) { return false; }
            int seed = Random.Range(1, int.MaxValue);
            if (!Generator.CanBuildFleet(focus, seed, Guards, Cruisers, Destroyers)) { return false; }
            _planSeed = seed;
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 1, false);
            Generator.RequestDeployFleet(tag, _planSeed, Guards, Cruisers, Destroyers, focus);
            Manager.SetModuleSite(-1);
            Manager.AddModuleTime(ExtraTime);
            _planSeed = 0;
            _seenFlagship = false;
            _nextLaunchMs = now + Mathf.RoundToInt(LaunchInterval * 1000f);
            _nextCatapultMs = now;
            _pendingFighters = 0;
            _pendingAttackers = 0;
        }

        public override int Tick(int now)
        {
            int tag = Manager.GetModuleTag();
            Manager.CountTaggedObjectives(tag);
            int total = Manager.GetTaggedTotal();
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            if (Manager.GetTaggedDone() >= total) { return RESULT_CLEAR; }

            // 기함: 살아 있는 걸 본 뒤 파괴되면 실패(배치 직후 체력 0은 부활 동기화 전). 오너가 바뀌면 다시 본다.
            ACO_EscortUnit flagship = Generator.GetFleetFlagship(tag);
            if (flagship != null)
            {
                if (!flagship.IsDestroyed()) { _seenFlagship = true; }
                else if (_seenFlagship) { return RESULT_FAIL; }
            }

            if (now - _nextLaunchMs >= 0)
            {
                _nextLaunchMs = now + Mathf.RoundToInt(LaunchInterval * 1000f);
                _pendingFighters = LaunchFighters;
                _pendingAttackers = LaunchAttackers;
            }
            if (flagship != null && _pendingFighters + _pendingAttackers > 0 && now - _nextCatapultMs >= 0)
            {
                _nextCatapultMs = now + Mathf.RoundToInt(LaunchSpacing * 1000f);
                bool fighter = _pendingFighters > 0;
                if (fighter) { _pendingFighters--; }
                else { _pendingAttackers--; }
                Generator.RequestDeployObjective(fighter ? ACO_MissionGenerator.OBJ_CARRIER_FLIGHT : ACO_MissionGenerator.OBJ_CARRIER_ATTACK, 0, 1, -1, flagship.transform.position, 0f, 0f);
            }
            return RESULT_RUNNING;
        }

        public override void End()
        {
            _planSeed = 0;
            _endedTag = Manager.GetModuleTag();
            SendCustomEventDelayedSeconds(nameof(_ReleaseFleet), ReleaseDelay);
        }

        public void _ReleaseFleet()
        {
            if (Generator == null) { return; }
            // 기다리는 사이 다음 함대전이 시작됐으면 그 함대를 거두면 안 된다
            int tag = Manager.GetModuleTag();
            if (tag != _endedTag && Generator.GetFleetFlagship(tag) != null) { return; }
            Generator.RequestReleaseFleet();
        }

        // ───────────── 대사 (모든 클라이언트) ─────────────

        void Update()
        {
            _lineTimer -= Time.deltaTime;
            if (_lineTimer > 0f) { return; }
            _lineTimer = 0.5f;
            if (Manager == null || Generator == null || !Manager.IsPhaseActive()) { return; }
            int tag = Manager.GetActiveObjectiveTag();
            if (tag == 0) { return; }
            ACO_EscortUnit flagship = Generator.GetFleetFlagship(tag);
            if (flagship == null) { return; }   // 이 페이즈가 아니다(태그 달린 기함이 없다)
            float own = HealthRatio(flagship.TargetHealth);
            float carrier = HealthRatio(CarrierHealth());
            int now = Networking.GetServerTimeInMilliseconds();
            bool contact = now - Generator.GetFleetContactMs() >= 0;
            // 첫 출격 시각은 오너의 Tick과 같은 계산(페이즈 시작 + 간격)이라 동기화가 필요 없다
            bool launched = now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(LaunchInterval * 1000f);
            if (tag != _lineTag)
            {
                // 처음 본다: 지금 상태를 기준으로(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineTag = tag;
                _saidContact = contact;
                _saidLaunch = launched;
                _saidFlagshipHalf = own <= 0.5f;
                _saidFlagshipLow = own <= 0.25f;
                _saidCarrierHalf = carrier <= 0.5f;
                return;
            }
            if (!_saidContact && contact)
            {
                _saidContact = true;
                Say(SIT_CONTACT);
            }
            if (!_saidLaunch && launched)
            {
                _saidLaunch = true;
                if (carrier > 0f) { Say(SIT_LAUNCH); }
            }
            if (!_saidFlagshipHalf && own <= 0.5f && own > 0f)
            {
                _saidFlagshipHalf = true;
                Say(SIT_FLAGSHIP_HALF);
            }
            if (!_saidFlagshipLow && own <= 0.25f && own > 0f)
            {
                _saidFlagshipLow = true;
                Say(SIT_FLAGSHIP_LOW);
            }
            if (!_saidCarrierHalf && carrier <= 0.5f && carrier > 0f)
            {
                _saidCarrierHalf = true;
                Say(SIT_CARRIER_HALF);
            }
        }

        private void Say(int situation) { SayTable(situation, "", LineEvent, LineSpeaker, LineText, LineClip); }

        private SaccTarget CarrierHealth() { return Generator != null && Generator.Carrier != null ? Generator.Carrier.TargetHealth : null; }

        private float HealthRatio(SaccTarget t)
        {
            if (t == null || t.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(t.Health / t.FullHealth);
        }

        // ───────────── 글자 ─────────────

        /// "CONTACT 1:20  CV 100%  CG 100%" → 교전 중 "CV 78%  CG 64%"(기함 30% 아래면 빨갛게).
        public override string GetProgressText()
        {
            if (Manager == null || Generator == null) { return ""; }
            int tag = Manager.GetActiveObjectiveTag();
            ACO_EscortUnit flagship = Generator.GetFleetFlagship(tag);
            if (flagship == null) { return "-"; }
            float own = HealthRatio(flagship.TargetHealth);
            string ownText = "CG " + Mathf.RoundToInt(own * 100f) + "%";
            if (own < 0.3f) { ownText = "<color=#FF5544>" + ownText + "</color>"; }
            string s = "CV " + Mathf.RoundToInt(HealthRatio(CarrierHealth()) * 100f) + "%  " + ownText;
            int left = Generator.GetFleetContactMs() - Networking.GetServerTimeInMilliseconds();
            if (left > 0)
            {
                int sec = Mathf.CeilToInt(left * 0.001f);
                s = "CONTACT " + (sec / 60) + ":" + (sec % 60).ToString("00") + "  " + s;
            }
            return s;
        }

        public override string GetDescription()
        {
            if (Description.Length > 0) { return Description; }
            return "SINK THE ENEMY CARRIER - PROTECT THE FLAGSHIP";
        }
    }
}
