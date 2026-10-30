using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 무전 채터(로컬 전용, 씬에 하나, 동기화 없음) — 예전 무전 자막(ACO_Radio)과 페이즈 음성을 합친 것.
    /// 상황이 생기면 대사(자막 + 선택 음성)를 대기열 하나로 차례로 낸다: PTT 누름 → 음성 → PTT 놓음, 자막은 음성이 끝날 때까지.
    /// PTT 소리는 채널마다 다르다: 화자마다 채널(SpeakerChannels — 0 관제기, 1 아군, 2 적군)이 있고 채널별 PTT 누름/놓음 소리를 쓴다
    /// (아군·적군 칸이 비면 관제기 소리).
    ///
    /// 대사 두 종류:
    ///  - 페이즈 대사(페이즈 유형 1~10별 시작/성공/실패 + 미션 완료 + 실패 사유별 미션 실패): *Texts / *Clips 칸. 확률 없이 늘 나온다.
    ///    배치가 안 돼 같은 페이즈가 다른 유형으로 바뀌면 새 유형의 시작 대사만. 처음 본 상태(늦게 들어옴)는 기준만 잡는다.
    ///  - 상황 대사(미션 시작, 증원, 격추, 미사일, 플레어, 피격, 호위 대상 등 — EV_*): 대사 표(LineEvent/LineSpeaker/LineText/LineClip, 같은 순서)에서
    ///    그 상황의 줄 중 하나를 확률(EventChance)·쿨타임(EventCooldown)에 따라 고른다(직전 줄은 피한다). "{name}"은 넘긴 이름으로 바뀐다.
    /// 상황은 각 클라이언트가 스스로 안다(네트워크 없음):
    ///  - 미션/페이즈/웨이브: 미션 관리자 상태를 PollInterval마다 읽는다. 호위 대상 피격·격추: 생성기의 호위 유닛 체력을 본다.
    ///  - 격추(내 것/아군): 작전판 / 미사일 빗나감·플레어: 쏜 사람 화면의 미사일 / 미사일 경보: 나를 노린 미사일 / 적 플레어: 플레어 장치(가까울 때만)
    ///  - 내·아군 피격 격추: 기체 강화 확장. 유닛 고유 대사(슈퍼에이스 등)는 Say로 바로.
    /// 잡담(EV_KILL 이후)은 미션 중에만. 밀린 대사는 MaxQueueDelay(페이즈 대사는 PhaseQueueDelay)보다 오래되면 버린다.
    /// 화면은 기본 TMP 폰트라 영어 대사가 기본값이다 — 한국어는 한글 TMP 폰트 에셋을 자막 글자에 넣어야 한다.
    /// 자막 위치·크기는 HUD 설정 패널이 바꾼다(HeadOffset, Panel 크기).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Chatter : UdonSharpBehaviour
    {
        // 상황(번호는 예전 ACO_Radio와 같다)
        public const int EV_MISSION_START = 0;
        public const int EV_PHASE_START = 1;       // 페이즈 대사 칸이 맡는다(대사 표에 넣지 않아도 된다)
        public const int EV_PHASE_CLEAR = 2;
        public const int EV_PHASE_FAIL = 3;
        public const int EV_WAVE = 4;
        public const int EV_MISSION_COMPLETE = 5;
        public const int EV_MISSION_FAIL = 6;
        public const int EV_KILL = 7;              // 내가 격추
        public const int EV_ALLY_KILL = 8;         // 아군이 격추({name} = 아군)
        public const int EV_MISSILE_MISS = 9;      // 내 미사일이 빗나감
        public const int EV_MISSILE_DECOYED = 10;  // 내 미사일이 플레어에 속음
        public const int EV_ENEMY_FLARE = 11;      // 가까운 적기가 플레어
        public const int EV_MISSILE_INCOMING = 12; // 나를 노린 미사일 발사
        public const int EV_SELF_DOWN = 13;        // 내가 격추됨
        public const int EV_ALLY_DOWN = 14;        // 아군이 격추됨({name} = 아군)
        public const int EV_ESCORT_HIT = 15;       // 호위 대상이 공격받음
        public const int EV_ESCORT_LOST = 16;      // 호위 대상 격추
        public const int EV_COUNT = 17;

        // 무전 채널(PTT 소리를 고른다)
        public const int CH_CONTROL = 0;   // 관제기(AWACS)
        public const int CH_ALLY = 1;      // 아군(윙맨·수송기)
        public const int CH_ENEMY = 2;     // 적군(적 조종사·슈퍼에이스)

        private const int QUEUE_SIZE = 8;
        private const int PHASE_TYPES = 10;

        [Header("참조 (빌드 시 ACO_SceneBaker가 넣는다)")]
        public ACO_MissionManager Manager;
        public ACO_MissionGenerator Generator;

        [Header("화면 (프리팹 빌더가 만든다 — 위치·크기는 HUD 설정 패널이 바꾼다)")]
        [Tooltip("머리를 따라 움직일 자막 패널")]
        public Transform Panel;
        [Tooltip("자막 글자. 한국어 대사를 쓰려면 한글 TMP 폰트 에셋을 넣는다")]
        public TextMeshPro SubtitleText;
        [Tooltip("머리 기준 위치(m): 오른쪽(+X), 위(+Y), 앞(+Z) — 기본은 시야 아래쪽 가운데")]
        public Vector3 HeadOffset = new Vector3(0f, -0.2f, 0.9f);
        [Tooltip("머리 회전을 따라가는 빠르기")]
        public float FollowSharpness = 8f;

        [Header("소리")]
        [Tooltip("음성·PTT 소리를 낼 AudioSource(2D 권장 — Spatial Blend 0)")]
        public AudioSource Voice;
        [Range(0f, 1f)] public float Volume = 1f;
        [Header("PTT 소리 (채널별 — 음성이 있는 대사만, 아군·적군이 비면 관제기 소리)")]
        [Tooltip("관제기: 음성 앞 무전 송신 소리(PTT 누름). 비우면 없음")]
        [UnityEngine.Serialization.FormerlySerializedAs("PttStart")]
        public AudioClip ControlPttStart;
        [Tooltip("관제기: 음성 뒤 무전 끝 소리(PTT 놓음)")]
        [UnityEngine.Serialization.FormerlySerializedAs("PttEnd")]
        public AudioClip ControlPttEnd;
        [Tooltip("아군(윙맨·수송기) PTT 누름")]
        public AudioClip AllyPttStart;
        [Tooltip("아군 PTT 놓음")]
        public AudioClip AllyPttEnd;
        [Tooltip("적군(적 조종사·슈퍼에이스) PTT 누름 — 감청하는 느낌으로 잡음 섞인 소리 등")]
        public AudioClip EnemyPttStart;
        [Tooltip("적군 PTT 놓음")]
        public AudioClip EnemyPttEnd;
        [Range(0f, 1f)] public float PttVolume = 0.8f;
        [Tooltip("PTT 소리 시작에서 음성 시작까지(초). 음수면 PTT 소리 길이만큼")]
        public float PttLead = -1f;

        [Header("화자 (번호 순)")]
        public string[] SpeakerNames = new string[] { "SKY EYE", "ENEMY", "TRANSPORT", "WINGMAN" };
        public Color[] SpeakerColors = new Color[] { new Color(0.45f, 1f, 0.55f), new Color(1f, 0.4f, 0.35f), new Color(0.5f, 0.8f, 1f), new Color(0.95f, 0.9f, 0.5f) };
        [Tooltip("화자별 무전 채널(PTT 소리): 0 관제기, 1 아군, 2 적군")]
        public int[] SpeakerChannels = new int[] { CH_CONTROL, CH_ENEMY, CH_ALLY, CH_ALLY };
        [Tooltip("페이즈 대사를 말하는 화자 번호")]
        public int PhaseSpeaker = 0;

        // ───────────── 페이즈 대사 ─────────────

        [Header("페이즈 대사 (칸 순서: 1 타격, 2 SEAD, 3 점수 할당, 4 시한 표적, 5 제공권, 6 보스, 7 공중 호위, 8 지상 호위, 9 폭격기 요격, 10 보너스)")]
        [TextArea] public string[] StartTexts = new string[] {
            "Primary target located. All aircraft, destroy the enemy facility.",
            "Enemy air defense network detected. Suppress the SAM and AA sites.",
            "Free hunting zone. Engage any target and meet the score quota.",
            "High-value targets are on the move. Hit each one before it escapes!",
            "Multiple bogeys inbound. Engage and take control of the airspace.",
            "Caution - an enemy ace squadron has been sighted. Stay sharp.",
            "The transport is taking off. Escort it to the drop zone.",
            "The convoy is rolling out. Cover it from the ambush.",
            "Enemy bombers are heading for the city! Intercept them before they arrive!",
            "Unidentified fast mover... it's an ace. Everyone, watch yourselves." };
        [TextArea] public string[] SuccessTexts = new string[] {
            "Target destroyed. Good work, all aircraft.",
            "Enemy air defenses are down. The skies are ours.",
            "Score quota met. Outstanding hunting.",
            "All time-sensitive targets eliminated.",
            "Airspace secured. No more contacts.",
            "The ace squadron has been shot down. Incredible.",
            "The transport has reached the drop zone. Delivery confirmed.",
            "The convoy made it through. Good cover.",
            "All bombers down! The city is safe.",
            "The ace is going down! I can't believe it!" };
        [TextArea] public string[] FailTexts = new string[] {
            "The target survived. Regroup.",
            "Air defenses are still active.",
            "We fell short of the quota.",
            "A target got away!",
            "We lost control of the airspace.",
            "The ace squadron is pulling out.",
            "The transport is down!",
            "The convoy has been destroyed!",
            "The bombers reached the city...",
            "The ace is withdrawing... he got away." };
        public AudioClip[] StartClips = new AudioClip[10];
        public AudioClip[] SuccessClips = new AudioClip[10];
        public AudioClip[] FailClips = new AudioClip[10];
        [Header("공통 (유형별 칸이 비었을 때)")]
        public string DefaultStartText = "New objective. Check your HUD.";
        public string DefaultSuccessText = "Objective complete.";
        public string DefaultFailText = "Objective failed.";
        public AudioClip DefaultStartClip;
        public AudioClip DefaultSuccessClip;
        public AudioClip DefaultFailClip;
        [Header("미션 결과")]
        public string MissionCompleteText = "Mission accomplished. All aircraft, return to base.";
        public AudioClip MissionCompleteClip;
        [Tooltip("실패 사유별(ACO_MissionManager.FAIL_*: 0 시간 초과, 1 도시 폭격, 2 호위 대상 격추, 3 콘보이 전멸, 4 시한 표적 도주)")]
        [TextArea] public string[] MissionFailedTexts = new string[] {
            "Time's up. Mission failed. All units, withdraw.",
            "The city has been bombed... Mission failed. All units, withdraw.",
            "We lost the transport. Mission failed. Return to base.",
            "The convoy is gone. Mission failed. Return to base.",
            "The target escaped. Mission failed. All aircraft, return to base." };
        public AudioClip[] MissionFailedClips = new AudioClip[5];
        [Tooltip("페이즈 대사가 이보다 오래(초) 밀리면 버린다")]
        public float PhaseQueueDelay = 10f;

        // ───────────── 상황 대사 ─────────────

        [Header("상황별 확률 / 쿨타임 (EV_* 순서)")]
        [Tooltip("상황이 생겼을 때 대사가 나올 확률(0~1)")]
        public float[] EventChance = new float[] {
            1f, 1f, 1f, 1f, 1f, 1f, 1f,
            0.5f, 0.3f, 0.5f, 0.7f, 0.35f, 0.6f, 1f, 0.9f, 0.5f, 1f };
        [Tooltip("같은 상황의 대사가 다시 나오기까지 최소 간격(초)")]
        public float[] EventCooldown = new float[] {
            0f, 0f, 0f, 0f, 10f, 0f, 0f,
            6f, 10f, 8f, 8f, 12f, 6f, 0f, 3f, 15f, 0f };
        [Tooltip("적 플레어 대사는 이 거리(m) 안의 적기만")]
        public float FlareCalloutRange = 3000f;

        [Header("상황 대사 표 (같은 순서의 배열 — 한 줄 = 대사 하나)")]
        [Tooltip("이 대사가 나오는 상황(EV_*)")]
        public int[] LineEvent = new int[] {
            EV_MISSION_START, EV_MISSION_START, EV_MISSION_START,
            EV_WAVE, EV_WAVE, EV_WAVE,
            EV_KILL, EV_KILL, EV_KILL, EV_KILL, EV_KILL,
            EV_ALLY_KILL, EV_ALLY_KILL, EV_ALLY_KILL,
            EV_MISSILE_MISS, EV_MISSILE_MISS, EV_MISSILE_MISS,
            EV_MISSILE_DECOYED, EV_MISSILE_DECOYED, EV_MISSILE_DECOYED,
            EV_ENEMY_FLARE, EV_ENEMY_FLARE, EV_ENEMY_FLARE,
            EV_MISSILE_INCOMING, EV_MISSILE_INCOMING, EV_MISSILE_INCOMING, EV_MISSILE_INCOMING,
            EV_SELF_DOWN, EV_SELF_DOWN,
            EV_ALLY_DOWN, EV_ALLY_DOWN, EV_ALLY_DOWN,
            EV_ESCORT_HIT, EV_ESCORT_HIT, EV_ESCORT_HIT, EV_ESCORT_HIT,
            EV_ESCORT_LOST };
        [Tooltip("화자 번호(SpeakerNames)")]
        public int[] LineSpeaker = new int[] {
            0, 0, 0,
            0, 0, 0,
            0, 0, 3, 0, 3,
            0, 3, 0,
            0, 3, 0,
            0, 3, 0,
            1, 1, 3,
            0, 0, 3, 0,
            0, 0,
            0, 3, 0,
            2, 2, 2, 0,
            2 };
        [TextArea] public string[] LineText = new string[] {
            "This is Sky Eye. All aircraft, the operation has begun. Good hunting.",
            "Sky Eye to all units, you are cleared to engage. Make it count.",
            "All aircraft, weapons free. Let's get this done.",
            "Enemy reinforcements inbound!",
            "Radar contact - multiple bogeys approaching.",
            "More hostiles on the scope. They're not giving up.",
            "Splash one! Good kill, {name}.",
            "Target destroyed.",
            "Nice shot! Scratch one.",
            "Enemy down. Keep it up.",
            "Got him! That's one less to worry about.",
            "{name} got one!",
            "Nice shooting, {name}!",
            "Good kill, {name}.",
            "Missile missed. Re-engage.",
            "No hit! Try again.",
            "That one went wide. Get closer.",
            "Flares! Your missile was decoyed.",
            "He dropped flares - missile lost track!",
            "Countermeasures. Wait for the flares to burn out.",
            "Flares, flares!",
            "Deploying countermeasures!",
            "Bandit's popping flares!",
            "Missile alert! Break, break!",
            "Missile inbound, evade!",
            "You've got a missile on your six!",
            "Incoming missile! Pull hard!",
            "You're hit! Return to the hangar and relaunch.",
            "You've been shot down. Head back and get another plane.",
            "{name} is down!",
            "We lost {name}!",
            "{name} has been shot down. Cover the gap!",
            "We're taking fire! Requesting assistance!",
            "Transport under attack! Get them off us!",
            "We've been hit! Hurry!",
            "The transport is under fire. Clear the area!",
            "Mayday, mayday... we're going down!" };
        [Tooltip("대사별 음성(선택, 같은 순서 — 비워도 된다)")]
        public AudioClip[] LineClip;

        [Header("표시 시간")]
        [Tooltip("자막 최소 표시 시간(초). 음성이 있으면 음성 길이 + 0.3초")]
        public float MinDisplayTime = 2.5f;
        [Tooltip("글자당 추가 시간(초)")]
        public float SecondsPerChar = 0.05f;
        [Tooltip("대사 사이 간격(초)")]
        public float Gap = 0.3f;
        [Tooltip("이보다 오래 밀린 상황 대사는 버린다(초)")]
        public float MaxQueueDelay = 6f;
        [Tooltip("미션 상태·호위 체력을 확인하는 주기(초)")]
        public float PollInterval = 0.25f;

        private float[] _lastEventTime;
        private int[] _lastLine;

        // 대기열(원형): 말하는 이·색·글자·음성·넣은 시각·버릴 나이
        private string[] _qWho = new string[QUEUE_SIZE];
        private Color[] _qColor = new Color[QUEUE_SIZE];
        private string[] _qText = new string[QUEUE_SIZE];
        private AudioClip[] _qClip = new AudioClip[QUEUE_SIZE];
        private int[] _qChannel = new int[QUEUE_SIZE];
        private float[] _qTime = new float[QUEUE_SIZE];
        private float[] _qKeep = new float[QUEUE_SIZE];
        private int _qStart;
        private int _qCount;
        private float _showUntil;
        private float _nextAt;
        // 재생 중인 음성: PTT 누름 뒤에 틀 음성, 음성이 끝나면 PTT 놓음
        private AudioClip _pendingClip;
        private float _pendingAt;
        private float _pttEndAt;
        private AudioClip _pttEndClip;   // 지금 음성의 채널 PTT 놓음 소리

        // 미션 상태(마지막으로 본 값)
        private bool _seenAny;
        private int _seenStartMs;
        private int _seenState;
        private int _seenPhase;
        private int _seenPhaseType;
        private int _seenCleared;
        private int _seenWaves;
        private float _pollTimer;
        private float[] _escortHealth;

        private Quaternion _smoothRot = Quaternion.identity;
        private bool _hasRot;

        void Start()
        {
            if (_lastEventTime != null) { return; }
            _lastEventTime = new float[EV_COUNT];
            _lastLine = new int[EV_COUNT];
            for (int i = 0; i < EV_COUNT; i++) { _lastEventTime[i] = -1000f; _lastLine[i] = -1; }
            if (SubtitleText) { SubtitleText.text = ""; }
        }

        // ───────────── 다른 스크립트가 부른다 ─────────────

        /// 상황 ev가 생겼다. name은 대사의 {name} 자리에 들어간다. 확률·쿨타임을 통과하면 대사 하나를 대기열에 넣는다.
        public void Report(int ev, string name)
        {
            Start();
            if (ev < 0 || ev >= EV_COUNT) { return; }
            // 잡담은 미션 중에만
            if (ev >= EV_KILL && (Manager == null || Manager.GetState() != ACO_MissionManager.STATE_RUNNING)) { return; }
            if (Time.time - _lastEventTime[ev] < Cooldown(ev)) { return; }
            if (Random.value > Chance(ev)) { return; }
            int line = PickLine(ev);
            if (line < 0) { return; }
            _lastEventTime[ev] = Time.time;
            _lastLine[ev] = line;
            int speaker = LineSpeaker != null && line < LineSpeaker.Length ? LineSpeaker[line] : 0;
            AudioClip clip = LineClip != null && line < LineClip.Length ? LineClip[line] : null;
            Enqueue(SpeakerName(speaker), SpeakerColor(speaker), LineText[line].Replace("{name}", name != null ? name : ""), clip, MaxQueueDelay, SpeakerChannel(speaker));
        }

        /// 위치가 있는 상황: 내 머리에서 FlareCalloutRange 안일 때만.
        public void ReportNear(int ev, Vector3 position, string name)
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }
            if (Vector3.Distance(me.GetPosition(), position) > FlareCalloutRange) { return; }
            Report(ev, name);
        }

        /// 대사 표 밖의 대사를 바로 넣는다(확률·쿨타임·미션 상태와 상관없이). 슈퍼에이스 등 유닛이 자기 대사를 띄울 때.
        /// channel = 무전 채널(CH_CONTROL / CH_ALLY / CH_ENEMY) — 음성이 붙을 때 PTT 소리를 고른다.
        public void Say(string who, Color color, string text, int channel)
        {
            Start();
            if (string.IsNullOrEmpty(text)) { return; }
            Enqueue(who, color, text, null, MaxQueueDelay, channel);
        }

        /// 음성 + 자막을 바로 넣는다(clip이 없으면 자막만). maxDelay보다 오래 밀리면 버린다.
        public void SayVoice(string who, Color color, string text, AudioClip clip, float maxDelay, int channel)
        {
            Start();
            if (string.IsNullOrEmpty(text) && clip == null) { return; }
            Enqueue(who, color, text, clip, maxDelay, channel);
        }

        // ───────────── 대기열 ─────────────

        private void Enqueue(string who, Color color, string text, AudioClip clip, float keep, int channel)
        {
            if (_qCount >= QUEUE_SIZE)
            {
                // 가득 차면 가장 오래된 대사를 버린다.
                _qStart = (_qStart + 1) % QUEUE_SIZE;
                _qCount--;
            }
            int k = (_qStart + _qCount) % QUEUE_SIZE;
            _qWho[k] = who != null ? who : "";
            _qColor[k] = color;
            _qText[k] = text != null ? text : "";
            _qClip[k] = clip;
            _qChannel[k] = channel;
            _qTime[k] = Time.time;
            _qKeep[k] = keep;
            _qCount++;
        }

        void Update()
        {
            _pollTimer -= Time.deltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = PollInterval;
                PollMission();
                PollEscorts();
            }

            // 음성: PTT 누름 소리가 끝나면 음성, 음성이 끝나면 PTT 놓음 소리
            if (_pendingClip != null && Time.time >= _pendingAt)
            {
                if (Voice) { Voice.PlayOneShot(_pendingClip, Volume); }
                _pttEndAt = Time.time + _pendingClip.length;
                _pendingClip = null;
            }
            if (_pttEndAt > 0f && Time.time >= _pttEndAt)
            {
                _pttEndAt = 0f;
                if (Voice && _pttEndClip) { Voice.PlayOneShot(_pttEndClip, PttVolume * Volume); }
            }

            if (Time.time >= _showUntil && SubtitleText && SubtitleText.text != "") { SubtitleText.text = ""; }
            if (Time.time < _nextAt) { return; }
            // 오래 밀린 대사는 버리고 다음 대사를 띄운다.
            while (_qCount > 0)
            {
                int k = _qStart;
                _qStart = (_qStart + 1) % QUEUE_SIZE;
                _qCount--;
                if (Time.time - _qTime[k] > _qKeep[k]) { continue; }
                Show(_qWho[k], _qColor[k], _qText[k], _qClip[k], _qChannel[k]);
                break;
            }
        }

        private void Show(string who, Color color, string text, AudioClip clip, int channel)
        {
            Start();
            if (SubtitleText)
            {
                SubtitleText.text = text.Length > 0 ? "<color=#" + Hex(color) + ">[" + who + "]</color> " + text : "";
            }
            float duration = Mathf.Max(MinDisplayTime, text.Length * SecondsPerChar);
            float tail = 0f;
            if (clip && Voice)
            {
                // PTT 누름 → (PttLead 뒤) 음성 → PTT 놓음. 자막은 음성이 끝날 때까지
                float lead = 0f;
                AudioClip pttStart = PttStartOf(channel);
                _pttEndClip = PttEndOf(channel);
                if (pttStart)
                {
                    Voice.PlayOneShot(pttStart, PttVolume * Volume);
                    lead = PttLead >= 0f ? PttLead : pttStart.length;
                }
                _pendingClip = clip;
                _pendingAt = Time.time + lead;
                duration = Mathf.Max(duration, lead + clip.length + 0.3f);
                tail = _pttEndClip ? _pttEndClip.length : 0f;
            }
            _showUntil = Time.time + duration;
            _nextAt = _showUntil + tail + Gap;
        }

        private float Chance(int ev) { return EventChance != null && ev < EventChance.Length ? EventChance[ev] : 1f; }
        private float Cooldown(int ev) { return EventCooldown != null && ev < EventCooldown.Length ? EventCooldown[ev] : 0f; }

        private int SpeakerChannel(int s) { return SpeakerChannels != null && s >= 0 && s < SpeakerChannels.Length ? SpeakerChannels[s] : CH_CONTROL; }

        /// 채널의 PTT 누름/놓음 소리(아군·적군 칸이 비면 관제기 소리).
        private AudioClip PttStartOf(int channel)
        {
            if (channel == CH_ALLY && AllyPttStart) { return AllyPttStart; }
            if (channel == CH_ENEMY && EnemyPttStart) { return EnemyPttStart; }
            return ControlPttStart;
        }

        private AudioClip PttEndOf(int channel)
        {
            if (channel == CH_ALLY && AllyPttEnd) { return AllyPttEnd; }
            if (channel == CH_ENEMY && EnemyPttEnd) { return EnemyPttEnd; }
            return ControlPttEnd;
        }

        private string SpeakerName(int s) { return SpeakerNames != null && s >= 0 && s < SpeakerNames.Length ? SpeakerNames[s] : ""; }
        private Color SpeakerColor(int s) { return SpeakerColors != null && s >= 0 && s < SpeakerColors.Length ? SpeakerColors[s] : Color.white; }

        /// 그 상황의 대사 중 하나(직전 대사는 다른 게 있으면 피한다). 없으면 -1.
        private int PickLine(int ev)
        {
            if (LineEvent == null || LineText == null) { return -1; }
            int n = Mathf.Min(LineEvent.Length, LineText.Length);
            int count = 0;
            for (int i = 0; i < n; i++) { if (LineEvent[i] == ev && i != _lastLine[ev]) { count++; } }
            bool allowRepeat = count == 0;
            if (allowRepeat) { for (int i = 0; i < n; i++) { if (LineEvent[i] == ev) { count++; } } }
            if (count == 0) { return -1; }
            int pick = Random.Range(0, count);
            for (int i = 0; i < n; i++)
            {
                if (LineEvent[i] != ev || (!allowRepeat && i == _lastLine[ev])) { continue; }
                if (pick == 0) { return i; }
                pick--;
            }
            return -1;
        }

        private string Hex(Color c)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f).ToString("X2")
                + Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f).ToString("X2")
                + Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f).ToString("X2");
        }

        /// 머리를 따라간다(머리 위치가 확정된 뒤).
        public override void PostLateUpdate()
        {
            if (!Panel) { return; }
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }
            VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            if (!_hasRot) { _smoothRot = head.rotation; _hasRot = true; }
            _smoothRot = Quaternion.Slerp(_smoothRot, head.rotation, 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            Panel.SetPositionAndRotation(head.position + _smoothRot * HeadOffset, _smoothRot);
        }

        /// HUD 설정 패널이 부른다: seconds 동안 예시 자막을 띄운다(위치·크기 확인용).
        public void Preview(float seconds)
        {
            if (!SubtitleText) { return; }
            SubtitleText.text = "<color=#" + Hex(SpeakerColor(PhaseSpeaker)) + ">[" + SpeakerName(PhaseSpeaker) + "]</color> Chatter subtitle preview.";
            _showUntil = Mathf.Max(_showUntil, Time.time + seconds);
        }

        // ───────────── 페이즈 대사 ─────────────

        private void SayPhase(string[] texts, AudioClip[] clips, int type, string fallbackText, AudioClip fallbackClip)
        {
            int k = type - 1;
            string text = texts != null && k >= 0 && k < texts.Length && !string.IsNullOrEmpty(texts[k]) ? texts[k] : fallbackText;
            AudioClip clip = clips != null && k >= 0 && k < clips.Length && clips[k] != null ? clips[k] : fallbackClip;
            SayVoice(SpeakerName(PhaseSpeaker), SpeakerColor(PhaseSpeaker), text, clip, PhaseQueueDelay, SpeakerChannel(PhaseSpeaker));
        }

        private void SayMissionFailed()
        {
            int r = Manager.GetFailReason();
            string text = MissionFailedTexts != null && r >= 0 && r < MissionFailedTexts.Length ? MissionFailedTexts[r] : "";
            if (string.IsNullOrEmpty(text) && MissionFailedTexts != null && MissionFailedTexts.Length > 0) { text = MissionFailedTexts[0]; }
            AudioClip clip = MissionFailedClips != null && r >= 0 && r < MissionFailedClips.Length ? MissionFailedClips[r] : null;
            if (clip == null && MissionFailedClips != null && MissionFailedClips.Length > 0) { clip = MissionFailedClips[0]; }
            SayVoice(SpeakerName(PhaseSpeaker), SpeakerColor(PhaseSpeaker), text, clip, PhaseQueueDelay, SpeakerChannel(PhaseSpeaker));
        }

        // ───────────── 미션 상태 / 호위 대상 ─────────────

        /// 관리자의 동기화 상태가 바뀐 만큼 대사를 낸다. 처음 본 상태(늦게 들어옴)는 기준만 잡는다.
        private void PollMission()
        {
            if (Manager == null) { return; }
            int state = Manager.GetState();
            int startMs = Manager.GetMissionStartMs();
            int phase = Manager.GetPhaseIndex();
            int cleared = Manager.GetPhasesCleared();
            int waves = Manager.GetWavesSent();
            int type = Manager.GetCurrentPhaseType();
            if (!_seenAny)
            {
                _seenAny = true;
                Remember(state, startMs, phase, type, cleared, waves);
                return;
            }

            bool running = state == ACO_MissionManager.STATE_RUNNING;
            bool finished = state == ACO_MissionManager.STATE_COMPLETE || state == ACO_MissionManager.STATE_FAILED;
            if (running && (_seenState != ACO_MissionManager.STATE_RUNNING || startMs != _seenStartMs))
            {
                // 새 미션: "아직 페이즈 없음"을 기준으로(첫 페이즈가 이미 시작됐어도 시작 대사를 낸다)
                Report(EV_MISSION_START, "");
                _seenPhase = -1;
                _seenPhaseType = 0;
                _seenCleared = 0;
                _seenWaves = 0;
            }
            if (running || finished)
            {
                // 페이즈 끝: 시작할 때 본 유형으로 성공/실패
                if (cleared > _seenCleared)
                {
                    if (Manager.GetLastPhaseFailed()) { SayPhase(FailTexts, FailClips, _seenPhaseType, DefaultFailText, DefaultFailClip); }
                    else { SayPhase(SuccessTexts, SuccessClips, _seenPhaseType, DefaultSuccessText, DefaultSuccessClip); }
                }
                // 페이즈 시작, 또는 배치가 안 돼 같은 페이즈가 다른 유형으로 바뀜
                bool changed = running && phase == _seenPhase && phase >= 0 && Manager.IsPhaseActive() && type != _seenPhaseType;
                if (running && phase >= 0 && (phase > _seenPhase || changed))
                {
                    SayPhase(StartTexts, StartClips, type, DefaultStartText, DefaultStartClip);
                    _seenPhaseType = type;
                }
                if (running && waves > _seenWaves) { Report(EV_WAVE, ""); }
            }
            if (finished && _seenState == ACO_MissionManager.STATE_RUNNING)
            {
                if (state == ACO_MissionManager.STATE_COMPLETE) { SayVoice(SpeakerName(PhaseSpeaker), SpeakerColor(PhaseSpeaker), MissionCompleteText, MissionCompleteClip, PhaseQueueDelay, SpeakerChannel(PhaseSpeaker)); }
                else { SayMissionFailed(); }
            }
            Remember(state, startMs, phase, running && phase >= 0 ? _seenPhaseType : type, cleared, waves);
        }

        private void Remember(int state, int startMs, int phase, int type, int cleared, int waves)
        {
            _seenState = state;
            _seenStartMs = startMs;
            _seenPhase = phase;
            _seenPhaseType = type;
            _seenCleared = cleared;
            _seenWaves = waves;
        }

        /// 배치된 호위 대상의 체력이 줄면 피격, 0이 되면 격추.
        private void PollEscorts()
        {
            if (Generator == null || Generator.EscortUnits == null) { return; }
            int n = Generator.EscortUnits.Length;
            if (_escortHealth == null || _escortHealth.Length != n)
            {
                _escortHealth = new float[n];
                for (int i = 0; i < n; i++) { _escortHealth[i] = -1f; }
            }
            for (int i = 0; i < n; i++)
            {
                ACO_EscortUnit unit = Generator.EscortUnits[i];
                if (unit != null && unit.Hostile) { _escortHealth[i] = -1f; continue; }   // 적 폭격기는 호위 대사 대상이 아니다
                SaccTarget health = unit ? unit.GetComponent<SaccTarget>() : null;
                if (!health || !Generator.IsUnitUsed(ACO_MissionGenerator.CAT_ESCORT, i))
                {
                    _escortHealth[i] = -1f;
                    continue;
                }
                float h = health.Health;
                float before = _escortHealth[i];
                _escortHealth[i] = h;
                if (before < 0f) { continue; }   // 처음 본 값은 기준만
                if (h <= 0f && before > 0f) { Report(EV_ESCORT_LOST, ""); }
                else if (h < before - 0.01f) { Report(EV_ESCORT_HIT, ""); }
            }
        }
    }
}
