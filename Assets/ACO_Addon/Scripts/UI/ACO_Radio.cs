using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 무전 자막(로컬 전용, 씬에 하나, 동기화 없음). 상황(EV_*)이 생기면 그 상황의 대사 중 하나를 확률(EventChance)·쿨타임(EventCooldown)에 따라 골라
    /// 머리를 따라오는 자막으로 띄우고, 음성 클립이 있으면 함께 튼다. 대사는 줄 단위 배열(LineEvent/LineSpeaker/LineText/LineClip, 같은 순서)이고
    /// "{name}"은 상황마다 넘긴 이름(격추한 아군, 격추된 아군 등)으로 바뀐다. 같은 상황에서 직전 대사는 되도록 다시 고르지 않는다.
    ///
    /// 상황은 각 클라이언트가 스스로 안다(네트워크 없음):
    ///  - 미션/페이즈/웨이브: 미션 관리자 상태를 PollInterval마다 읽는다(늦게 들어온 클라이언트는 처음 본 상태를 기준으로 삼는다).
    ///  - 격추(내 것/아군): 작전판(ACO_MissionLog) / 내 미사일 빗나감·플레어에 속음: 쏜 사람 화면의 ACO_Missile / 미사일 경보: 나를 노린 NPC 미사일
    ///  - 적 플레어: NPC 전투기(가까울 때만, FlareCalloutRange) / 내·아군 격추: 기체 강화 확장(ACO_VehicleUpgrades)
    ///  - 호위 대상 피격·격추: 생성기의 호위 유닛 체력을 PollInterval마다 본다.
    /// 잡담(EV_KILL 이후)은 미션 중에만 나온다. 대사가 겹치면 차례로, MaxQueueDelay보다 밀린 대사는 버린다.
    /// 화면은 기본 TMP 폰트라 영어 대사가 기본값이다 — 한국어는 한글 TMP 폰트 에셋을 자막 글자에 넣어야 한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Radio : UdonSharpBehaviour
    {
        // 상황
        public const int EV_MISSION_START = 0;
        public const int EV_PHASE_START = 1;
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

        private const int QUEUE_SIZE = 6;

        [Header("참조 (빌드 시 ACO_SceneBaker가 넣는다)")]
        public ACO_MissionManager Manager;
        public ACO_MissionGenerator Generator;

        [Header("화면 (프리팹 빌더가 만든다)")]
        [Tooltip("머리를 따라 움직일 자막 패널")]
        public Transform Panel;
        [Tooltip("자막 글자. 한국어 대사를 쓰려면 한글 TMP 폰트 에셋을 넣는다")]
        public TextMeshPro SubtitleText;
        [Tooltip("머리 기준 위치(m): 오른쪽(+X), 위(+Y), 앞(+Z) — 기본은 시야 아래쪽 가운데")]
        public Vector3 HeadOffset = new Vector3(0f, -0.2f, 0.9f);
        [Tooltip("머리 회전을 따라가는 빠르기")]
        public float FollowSharpness = 8f;
        [Tooltip("음성 클립을 틀 소리(2D)")]
        public AudioSource Voice;

        [Header("화자 (LineSpeaker 번호 순)")]
        public string[] SpeakerNames = new string[] { "SKY EYE", "ENEMY", "TRANSPORT" };
        public Color[] SpeakerColors = new Color[] { new Color(0.45f, 1f, 0.55f), new Color(1f, 0.4f, 0.35f), new Color(0.5f, 0.8f, 1f) };

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

        [Header("대사 (같은 순서의 배열 — 한 줄 = 대사 하나)")]
        [Tooltip("이 대사가 나오는 상황(EV_*)")]
        public int[] LineEvent = new int[] {
            EV_MISSION_START,
            EV_PHASE_START, EV_PHASE_START,
            EV_PHASE_CLEAR, EV_PHASE_CLEAR,
            EV_PHASE_FAIL,
            EV_WAVE, EV_WAVE,
            EV_MISSION_COMPLETE,
            EV_MISSION_FAIL,
            EV_KILL, EV_KILL, EV_KILL,
            EV_ALLY_KILL, EV_ALLY_KILL,
            EV_MISSILE_MISS, EV_MISSILE_MISS,
            EV_MISSILE_DECOYED, EV_MISSILE_DECOYED,
            EV_ENEMY_FLARE, EV_ENEMY_FLARE,
            EV_MISSILE_INCOMING, EV_MISSILE_INCOMING,
            EV_SELF_DOWN,
            EV_ALLY_DOWN, EV_ALLY_DOWN,
            EV_ESCORT_HIT, EV_ESCORT_HIT,
            EV_ESCORT_LOST };
        [Tooltip("화자 번호(SpeakerNames)")]
        public int[] LineSpeaker = new int[] {
            0,
            0, 0,
            0, 0,
            0,
            0, 0,
            0,
            0,
            0, 0, 0,
            0, 0,
            0, 0,
            0, 0,
            1, 1,
            0, 0,
            0,
            0, 0,
            2, 2,
            0 };
        [TextArea] public string[] LineText = new string[] {
            "All aircraft, the operation has begun. Good hunting.",
            "New objective confirmed. Check your HUD.", "All units, proceed to the next objective.",
            "Objective complete. Nice work.", "Target area secured. Stand by for new orders.",
            "We lost the objective... regroup.",
            "Enemy reinforcements inbound!", "Radar contact, multiple bogeys approaching.",
            "Mission accomplished. All aircraft, return to base.",
            "Mission failed. All units, withdraw.",
            "Splash one! Good kill, {name}.", "Target destroyed.", "Enemy down. Keep it up.",
            "{name} got one!", "Nice shooting, {name}.",
            "Missile missed. Re-engage.", "No hit. Try again.",
            "Flares! Your missile was decoyed.", "They dropped flares - missile lost track.",
            "Flares, flares!", "Breaking off, deploying countermeasures!",
            "Missile alert! Break, break!", "Missile inbound, evade!",
            "You're hit! Return to the hangar and relaunch.",
            "{name} is down!", "We lost {name}!",
            "We're taking fire! Help us!", "Transport under attack!",
            "The transport is down... damn it." };
        [Tooltip("대사별 음성(선택, 비워도 된다)")]
        public AudioClip[] LineClip;

        [Header("표시 시간")]
        [Tooltip("자막 최소 표시 시간(초). 음성이 있으면 음성 길이 + 0.3초")]
        public float MinDisplayTime = 2.5f;
        [Tooltip("글자당 추가 시간(초)")]
        public float SecondsPerChar = 0.05f;
        [Tooltip("대사 사이 간격(초)")]
        public float Gap = 0.3f;
        [Tooltip("이보다 오래 밀린 대사는 버린다(초)")]
        public float MaxQueueDelay = 6f;
        [Tooltip("미션 상태·호위 체력을 확인하는 주기(초)")]
        public float PollInterval = 0.5f;

        private float[] _lastEventTime;
        private int[] _lastLine;

        // 대기열(원형)
        private int[] _qLine = new int[QUEUE_SIZE];
        private string[] _qName = new string[QUEUE_SIZE];
        private string[] _qWho = new string[QUEUE_SIZE];     // 직접 띄우는 대사(Say)의 말하는 이 — 대사 번호(_qLine)는 -1
        private Color[] _qColor = new Color[QUEUE_SIZE];
        private float[] _qTime = new float[QUEUE_SIZE];
        private int _qStart;
        private int _qCount;
        private float _showUntil;
        private float _nextAt;

        // 미션 상태(마지막으로 본 값)
        private bool _seenAny;
        private int _seenStartMs;
        private int _seenState;
        private int _seenPhase;
        private int _seenCleared;
        private int _seenWaves;
        private float _pollTimer;
        private float[] _escortHealth;

        private Quaternion _smoothRot = Quaternion.identity;
        private bool _hasRot;

        void Start()
        {
            _lastEventTime = new float[EV_COUNT];
            _lastLine = new int[EV_COUNT];
            for (int i = 0; i < EV_COUNT; i++) { _lastEventTime[i] = -1000f; _lastLine[i] = -1; }
            if (SubtitleText) { SubtitleText.text = ""; }
        }

        // ───────────── 상황 알리기 (다른 스크립트가 부른다) ─────────────

        /// 상황 ev가 생겼다. name은 대사의 {name} 자리에 들어간다. 확률·쿨타임을 통과하면 대사 하나를 대기열에 넣는다.
        public void Report(int ev, string name)
        {
            if (_lastEventTime == null) { Start(); }
            if (ev < 0 || ev >= EV_COUNT) { return; }
            // 잡담은 미션 중에만
            if (ev >= EV_KILL && (Manager == null || Manager.GetState() != ACO_MissionManager.STATE_RUNNING)) { return; }
            if (Time.time - _lastEventTime[ev] < Cooldown(ev)) { return; }
            if (Random.value > Chance(ev)) { return; }
            int line = PickLine(ev);
            if (line < 0) { return; }
            _lastEventTime[ev] = Time.time;
            _lastLine[ev] = line;
            Enqueue(line, name);
        }

        /// 대사 표 밖의 대사를 바로 대기열에 넣는다(확률·쿨타임·미션 상태와 상관없이). 슈퍼에이스 등 유닛이 자기 대사를 띄울 때.
        public void Say(string who, Color color, string text)
        {
            if (_lastEventTime == null) { Start(); }
            if (string.IsNullOrEmpty(text)) { return; }
            Enqueue(-1, text);
            int k = (_qStart + _qCount - 1) % QUEUE_SIZE;
            _qWho[k] = who != null ? who : "";
            _qColor[k] = color;
        }

        /// 위치가 있는 상황: 내 머리에서 FlareCalloutRange 안일 때만.
        public void ReportNear(int ev, Vector3 position, string name)
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }
            if (Vector3.Distance(me.GetPosition(), position) > FlareCalloutRange) { return; }
            Report(ev, name);
        }

        private float Chance(int ev) { return EventChance != null && ev < EventChance.Length ? EventChance[ev] : 1f; }
        private float Cooldown(int ev) { return EventCooldown != null && ev < EventCooldown.Length ? EventCooldown[ev] : 0f; }

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

        private void Enqueue(int line, string name)
        {
            if (_qCount >= QUEUE_SIZE)
            {
                // 가득 차면 가장 오래된 대사를 버린다.
                _qStart = (_qStart + 1) % QUEUE_SIZE;
                _qCount--;
            }
            int k = (_qStart + _qCount) % QUEUE_SIZE;
            _qLine[k] = line;
            _qName[k] = name != null ? name : "";
            _qTime[k] = Time.time;
            _qCount++;
        }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            _pollTimer -= Time.deltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = PollInterval;
                PollMission();
                PollEscorts();
            }

            if (Time.time >= _showUntil && SubtitleText && SubtitleText.text != "") { SubtitleText.text = ""; }
            if (Time.time < _nextAt) { return; }
            // 오래 밀린 대사는 버리고 다음 대사를 띄운다.
            while (_qCount > 0)
            {
                int k = _qStart;
                _qStart = (_qStart + 1) % QUEUE_SIZE;
                _qCount--;
                if (Time.time - _qTime[k] > MaxQueueDelay) { continue; }
                Show(_qLine[k], _qName[k], _qWho[k], _qColor[k]);
                break;
            }
        }

        private void Show(int line, string name, string customWho, Color customColor)
        {
            string who;
            Color color;
            string text;
            if (line < 0)
            {
                // Say로 넣은 대사: name 자리에 대사 전체가 있다.
                who = customWho;
                color = customColor;
                text = name;
            }
            else
            {
                int speaker = LineSpeaker != null && line < LineSpeaker.Length ? LineSpeaker[line] : 0;
                who = SpeakerNames != null && speaker >= 0 && speaker < SpeakerNames.Length ? SpeakerNames[speaker] : "";
                color = SpeakerColors != null && speaker >= 0 && speaker < SpeakerColors.Length ? SpeakerColors[speaker] : Color.white;
                text = LineText[line].Replace("{name}", name);
            }
            if (SubtitleText)
            {
                SubtitleText.text = "<color=#" + Hex(color) + ">[" + who + "]</color> " + text;
            }

            float duration = Mathf.Max(MinDisplayTime, text.Length * SecondsPerChar);
            AudioClip clip = line >= 0 && LineClip != null && line < LineClip.Length ? LineClip[line] : null;
            if (clip && Voice)
            {
                Voice.PlayOneShot(clip);
                duration = Mathf.Max(duration, clip.length + 0.3f);
            }
            _showUntil = Time.time + duration;
            _nextAt = _showUntil + Gap;
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

        // ───────────── 미션 상태 / 호위 대상 ─────────────

        /// 관리자의 동기화 상태가 바뀐 만큼 상황을 알린다. 처음 본 상태(늦게 들어옴)는 기준만 잡는다.
        private void PollMission()
        {
            if (Manager == null) { return; }
            int state = Manager.GetState();
            int startMs = Manager.GetMissionStartMs();
            int phase = Manager.GetPhaseIndex();
            int cleared = Manager.GetPhasesCleared();
            int waves = Manager.GetWavesSent();
            if (!_seenAny)
            {
                _seenAny = true;
                Remember(state, startMs, phase, cleared, waves);
                return;
            }

            bool running = state == ACO_MissionManager.STATE_RUNNING;
            if (running && (_seenState != ACO_MissionManager.STATE_RUNNING || startMs != _seenStartMs))
            {
                Report(EV_MISSION_START, "");
                _seenPhase = -1;
                _seenCleared = 0;
                _seenWaves = 0;
            }
            if (running)
            {
                if (cleared > _seenCleared) { Report(Manager.GetLastPhaseFailed() ? EV_PHASE_FAIL : EV_PHASE_CLEAR, ""); }
                if (phase > _seenPhase && phase >= 0) { Report(EV_PHASE_START, ""); }
                if (waves > _seenWaves) { Report(EV_WAVE, ""); }
            }
            if (state != _seenState)
            {
                if (state == ACO_MissionManager.STATE_COMPLETE) { Report(EV_MISSION_COMPLETE, ""); }
                else if (state == ACO_MissionManager.STATE_FAILED) { Report(EV_MISSION_FAIL, ""); }
            }
            Remember(state, startMs, phase, cleared, waves);
        }

        private void Remember(int state, int startMs, int phase, int cleared, int waves)
        {
            _seenState = state;
            _seenStartMs = startMs;
            _seenPhase = phase;
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
