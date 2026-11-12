using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components.Base;
using VRC.SDK3.Persistence;

namespace ACO
{
    /// 상황별 BGM(로컬 전용, 동기화 없음 — 씬에 하나). 영상 플레이어(VRC Unity Video Player)로 URL을 스트리밍해 2D AudioSource로 낸다 —
    /// 음악 파일을 월드에 넣지 않는다. 곡은 미션 관리자의 동기화 상태로 고른다:
    ///  - 대기(STATE_IDLE): LobbyUrls.
    ///  - 페이즈 진행 중: 그 페이즈 모듈의 BgmUrls → 비었으면 보스 페이즈(IsBossPhase)는 BossUrls, 일반 페이즈는 PhaseUrls → 그것도 비면 무음.
    ///  - 페이즈 사이: 듣던 곡을 계속.  미션 끝(성공·실패): 페이드 아웃(결과 효과음만) → LobbyAfterEnd초 뒤(브리핑실로 돌아온 뒤) 대기실 곡.
    ///    미션이 끝난 뒤 월드에 들어온 사람은 바로 대기실 곡.
    /// 페이즈 곡은 페이즈 시작 시각(동기화)으로 골라 모두 같은 곡으로 시작한다. 곡이 끝나면 풀의 다음 곡, 풀에 곡이 하나면 반복.
    /// URL 불러오기는 LoadInterval초에 한 번(VRChat 제한 5초). 불러오기 제한 오류면 다시, 다른 오류면 다음 곡(풀을 다 돌면 그 상황은 무음).
    /// 곡을 바꿀 땐 페이드 아웃 → 정지 → 불러오기 → 재생 시작에 페이드 인. 이 스크립트는 영상 이벤트를 받도록 플레이어와 같은 오브젝트에 둔다.
    /// 볼륨·켜기는 BGM 패널(ACO_BgmPanel)에서 바꾸고 플레이어 영구 저장(PlayerData)에 남는다 — 끄면 URL을 불러오지도 않는다.
    /// 곡 이름(*Names — URL과 같은 순서, 인스펙터에서 URL 옆에 넣는다)은 패널에 지금 곡으로 보인다(비우면 URL).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_Bgm : UdonSharpBehaviour
    {
        private const int CTX_NONE = 0;
        private const int CTX_LOBBY = 1;
        private const int CTX_PHASE = 2;
        private const int CTX_END = 3;

        // 패널에 보이는 곡 상태(ACO_BgmPanel.SetTrack)
        public const int TRACK_OFF = 0;       // BGM 꺼짐
        public const int TRACK_NONE = 1;      // 이 상황에 곡 없음(무음)
        public const int TRACK_LOADING = 2;   // 불러오는 중
        public const int TRACK_PLAYING = 3;

        [Header("연결")]
        [Tooltip("같은 오브젝트의 VRC Unity Video Player")]
        public BaseVRCVideoPlayer Player;
        [Tooltip("플레이어 소리가 나오는 2D AudioSource(플레이어 Target Audio Sources) — 볼륨·페이드를 여기서 한다")]
        public AudioSource Output;
        [Tooltip("Bake가 넣는다")]
        public ACO_MissionManager Manager;
        [Tooltip("Bake가 넣는다 — 씬의 BGM 패널(값이 바뀌면 글자를 고친다)")]
        public ACO_BgmPanel[] Panels;

        [Header("곡 풀 (URL — 빌드 전에 넣는다. 페이즈 전용 곡은 각 페이즈 모듈의 BgmUrls)")]
        public VRCUrl[] LobbyUrls;
        [Tooltip("전용 곡이 없는 보스 페이즈(요새·랜스 포대·함대·육상함·에이스들)")]
        public VRCUrl[] BossUrls;
        [Tooltip("전용 곡이 없는 일반 페이즈(요격·호위·파괴 등) — 페이즈 시작 시각으로 하나를 골라 모두 같은 곡")]
        public VRCUrl[] PhaseUrls;
        [Tooltip("곡 이름(각 URL 풀과 같은 순서 — 패널에 표시, 비우면 URL). 인스펙터에서 URL 옆에 넣는다")]
        public string[] LobbyNames;
        public string[] BossNames;
        public string[] PhaseNames;

        [Header("소리 (기본값 — 플레이어가 패널에서 바꾼 값은 저장된다)")]
        [Range(0f, 1f)] public float Volume = 0.2f;
        [Tooltip("상황별 BGM 켜기(끄면 무음, 불러오지도 않는다)")]
        public bool Dynamic = true;
        [Tooltip("패널 −/+ 한 번에 바뀌는 볼륨")]
        public float VolumeStep = 0.1f;
        public float FadeOutTime = 2f;
        public float FadeInTime = 1.5f;
        [Tooltip("URL 불러오기 최소 간격(초) — VRChat 영상 불러오기 제한(5초)보다 조금 길게")]
        public float LoadInterval = 5.5f;
        [Tooltip("미션 끝(성공·실패) 뒤 이만큼(초) 무음, 그 뒤 대기실 곡 — 게임 진행의 귀환(ReturnDelay 8초 + 암전)이 끝날 즈음")]
        public float LobbyAfterEnd = 10f;

        private int _ctx = -1;
        private int _ctxStart;
        private VRCUrl[] _pool;
        private string[] _names;      // _pool과 같은 순서의 곡 이름
        private int _shownState = -1;   // 패널에 보낸 곡 상태·곡
        private int _shownIndex = -1;
        private VRCUrl[] _shownPool;
        private int _index;
        private int _errors;
        private bool _switching;   // 페이드 아웃 뒤 정지·다음 곡
        private bool _needLoad;
        private bool _loading;
        private bool _playing;
        private float _nextLoad;
        private float _vol;
        private bool _restored;
        private int _endState = -1;   // 지금 보고 있는 미션 끝 상태(성공·실패) — 바뀌면 끝난 시각을 다시 잰다
        private float _endAt;

        private const string KEY_VOLUME = "aco_bgm_volume";
        private const string KEY_ON = "aco_bgm_on";

        private void Start()
        {
            if (Output != null) { Output.volume = 0f; }
            RefreshPanels();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) { return; }
            bool wasOn = Level() > 0f;
            float v;
            bool b;
            if (PlayerData.TryGetFloat(player, KEY_VOLUME, out v)) { Volume = Mathf.Clamp01(v); }
            if (PlayerData.TryGetBool(player, KEY_ON, out b)) { Dynamic = b; }
            _restored = true;
            LevelChanged(wasOn);
        }

        /// 실제로 낼 볼륨(꺼져 있으면 0).
        private float Level() { return Dynamic ? Volume : 0f; }

        private void Update()
        {
            if (Player == null || Output == null) { return; }
            UpdateContext();
            float dt = Time.deltaTime;
            if (_switching)
            {
                _vol = Mathf.MoveTowards(_vol, 0f, dt * Mathf.Max(Volume, 0.05f) / Mathf.Max(FadeOutTime, 0.05f));
                if (_vol <= 0f)
                {
                    _switching = false;
                    if (_playing || _loading) { Player.Stop(); }
                    _playing = false;
                    _loading = false;
                    _needLoad = HasAny(_pool) && Level() > 0f;
                }
            }
            else
            {
                float target = _playing ? Level() : 0f;
                float rate = Mathf.Max(Volume, 0.05f) / Mathf.Max(target > _vol ? FadeInTime : FadeOutTime, 0.05f);
                _vol = Mathf.MoveTowards(_vol, target, dt * rate);
                if (_needLoad && !_loading && Time.time >= _nextLoad && Level() > 0f) { Load(); }
            }
            if (Output.volume != _vol) { Output.volume = _vol; }
            RefreshTrack();
        }

        /// 지금 상황의 곡 풀을 정하고, 바뀌었으면 곡을 바꾼다.
        private void UpdateContext()
        {
            if (Manager == null) { return; }
            int state = Manager.GetState();
            int ctx;
            int start = 0;
            if (state != ACO_MissionManager.STATE_COMPLETE && state != ACO_MissionManager.STATE_FAILED) { _endState = -1; }
            if (state == ACO_MissionManager.STATE_IDLE) { ctx = CTX_LOBBY; }
            else if (state == ACO_MissionManager.STATE_RUNNING)
            {
                // 페이즈 사이 — 듣던 곡 그대로. 아직 아무것도 안 틀었으면(이때 들어온 사람) 첫 페이즈 전이면 대기실, 아니면 지난 페이즈 곡
                if (!Manager.IsPhaseActive() && _ctx >= 0) { return; }
                if (!Manager.IsPhaseActive() && Manager.GetPhaseIndex() < 0) { ctx = CTX_LOBBY; }
                else
                {
                    ctx = CTX_PHASE;
                    start = Manager.GetPhaseStartMs();
                }
            }
            else
            {
                // 미션 끝: 결과 효과음 동안 무음 → 브리핑실로 돌아온 뒤 대기실 곡. 아직 아무것도 안 틀었으면(끝난 뒤 들어온 사람) 바로 대기실 곡
                if (_endState != state)
                {
                    _endState = state;
                    _endAt = _ctx < 0 ? -10000f : Time.time;
                }
                ctx = Time.time - _endAt >= LobbyAfterEnd ? CTX_LOBBY : CTX_END;
            }
            if (ctx == _ctx && start == _ctxStart) { return; }
            _ctx = ctx;
            _ctxStart = start;
            VRCUrl[] pool = null;
            string[] names = null;
            if (ctx == CTX_LOBBY) { pool = LobbyUrls; names = LobbyNames; }
            else if (ctx == CTX_PHASE)
            {
                ACO_PhaseModule m = Manager.GetModule(Manager.GetCurrentPhaseType());
                if (m != null)
                {
                    if (HasAny(m.BgmUrls)) { pool = m.BgmUrls; names = m.BgmNames; }
                    else if (m.IsBossPhase()) { pool = BossUrls; names = BossNames; }
                    else { pool = PhaseUrls; names = PhaseNames; }
                }
            }
            if (!HasAny(pool)) { pool = null; names = null; }
            // 같은 풀이 이어지면(보스 → 보스 기본 등) 듣던 곡 그대로
            if (pool != null && pool == _pool && (_playing || _loading || _needLoad)) { return; }
            _pool = pool;
            _names = names;
            _errors = 0;
            if (pool != null)
            {
                int n = pool.Length;
                int pick = ctx == CTX_PHASE ? (start & 0x7fffffff) % n : Random.Range(0, n);
                _index = NextValid(pick - 1);
            }
            _needLoad = false;
            if (_playing || _loading || _vol > 0f) { _switching = true; }
            else { _needLoad = pool != null && Level() > 0f; }
        }

        private void Load()
        {
            _needLoad = false;
            if (!HasAny(_pool) || _index < 0 || _index >= _pool.Length) { return; }
            _loading = true;
            _nextLoad = Time.time + LoadInterval;
            Player.Loop = CountValid(_pool) == 1;
            Player.PlayURL(_pool[_index]);
        }

        public override void OnVideoStart()
        {
            _loading = false;
            _playing = true;
            _errors = 0;
        }

        public override void OnVideoEnd()
        {
            _playing = false;
            if (_switching || !HasAny(_pool)) { return; }
            _index = NextValid(_index);
            _needLoad = Level() > 0f;
        }

        public override void OnVideoError(VideoError videoError)
        {
            _loading = false;
            _playing = false;
            if (_switching || !HasAny(_pool)) { return; }
            if (videoError == VideoError.RateLimited)
            {
                _nextLoad = Time.time + LoadInterval;
                _needLoad = true;
                return;
            }
            _errors++;
            if (_errors >= CountValid(_pool)) { return; }   // 풀을 다 돌았다 — 이 상황은 무음
            _index = NextValid(_index);
            _needLoad = true;
        }

        // ───────────── 패널 (ACO_BgmPanel) ─────────────

        /// 볼륨을 VolumeStep만큼 줄이기(−1)/늘리기(+1). 끝이라 못 바꾸면 false.
        public bool AdjustVolume(int direction)
        {
            float v = Mathf.Clamp01(Mathf.Round((Volume + direction * VolumeStep) * 100f) / 100f);
            if (Mathf.Approximately(v, Volume)) { return false; }
            bool wasOn = Level() > 0f;
            Volume = v;
            if (_restored) { PlayerData.SetFloat(KEY_VOLUME, Volume); }
            LevelChanged(wasOn);
            return true;
        }

        /// 상황별 BGM 켜기/끄기.
        public void ToggleDynamic()
        {
            bool wasOn = Level() > 0f;
            Dynamic = !Dynamic;
            if (_restored) { PlayerData.SetBool(KEY_ON, Dynamic); }
            LevelChanged(wasOn);
        }

        /// 꺼지면 페이드 아웃 뒤 정지(불러오지도 않는다), 켜지면 지금 상황의 곡을 다시 불러온다. 볼륨만 바뀌면 페이드로 따라간다.
        private void LevelChanged(bool wasOn)
        {
            bool on = Level() > 0f;
            if (!on && (_playing || _loading)) { _switching = true; }
            else if (on && !wasOn && !_playing && !_loading && HasAny(_pool)) { _needLoad = true; }
            RefreshPanels();
        }

        private void RefreshPanels()
        {
            if (Panels == null) { return; }
            for (int i = 0; i < Panels.Length; i++) { if (Panels[i] != null) { Panels[i].Refresh(Volume, Dynamic); } }
        }

        /// 지금 곡 상태(TRACK_*).
        private int TrackState()
        {
            if (Level() <= 0f) { return TRACK_OFF; }
            if (_pool == null || _index < 0 || _index >= _pool.Length) { return TRACK_NONE; }
            if (_playing && !_switching) { return TRACK_PLAYING; }
            if (_loading || _needLoad || _switching) { return TRACK_LOADING; }
            return TRACK_NONE;
        }

        /// 곡 이름(비었으면 URL).
        private string TrackName(int i)
        {
            if (_pool == null || i < 0 || i >= _pool.Length) { return ""; }
            if (_names != null && i < _names.Length && _names[i] != null && _names[i].Length > 0) { return _names[i]; }
            return _pool[i] != null ? _pool[i].Get() : "";
        }

        /// 곡이나 상태가 바뀌었을 때만 패널에 알린다(매 프레임 부르지만 비교만).
        private void RefreshTrack()
        {
            int state = TrackState();
            if (state == _shownState && _index == _shownIndex && _pool == _shownPool) { return; }
            _shownState = state;
            _shownIndex = _index;
            _shownPool = _pool;
            if (Panels == null) { return; }
            string name = state >= TRACK_LOADING ? TrackName(_index) : "";
            for (int i = 0; i < Panels.Length; i++) { if (Panels[i] != null) { Panels[i].SetTrack(state, name); } }
        }

        private int NextValid(int from)
        {
            if (_pool == null) { return -1; }
            int n = _pool.Length;
            for (int k = 1; k <= n; k++)
            {
                int i = ((from + k) % n + n) % n;
                if (IsValid(_pool[i])) { return i; }
            }
            return -1;
        }

        private bool HasAny(VRCUrl[] pool) { return CountValid(pool) > 0; }

        private int CountValid(VRCUrl[] pool)
        {
            if (pool == null) { return 0; }
            int c = 0;
            for (int i = 0; i < pool.Length; i++) { if (IsValid(pool[i])) { c++; } }
            return c;
        }

        private bool IsValid(VRCUrl u)
        {
            if (u == null) { return false; }
            string s = u.Get();
            return s != null && s.Length > 0;
        }
    }
}
