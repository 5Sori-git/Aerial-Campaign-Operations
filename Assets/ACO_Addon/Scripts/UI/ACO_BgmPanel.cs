using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ACO
{
    /// BGM 패널(동기화 없음 — 내 화면만): 볼륨 −/+(VolumeStep, 기본 10%)와 상황별 BGM 켜기/끄기. 값은 ACO_Bgm이 들고 저장한다(PlayerData).
    /// 지금 곡(NowPlayingText): ACO_Bgm이 곡·상태가 바뀔 때 SetTrack으로 알린다 — 곡 이름은 ACO_Bgm·페이즈 모듈의 *Names(비우면 URL).
    /// 버튼(Button.onClick → SendCustomEvent "VolumeDown"/"VolumeUp"/"Toggle")이 부른다. Bake가 Bgm·Sound를 잇고 ACO_Bgm.Panels에 넣는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_BgmPanel : UdonSharpBehaviour
    {
        [Tooltip("Bake가 넣는다")]
        public ACO_Bgm Bgm;
        [Tooltip("Bake가 넣는다 — 버튼 효과음")]
        public ACO_UiSound Sound;
        public TextMeshProUGUI VolumeText;
        public TextMeshProUGUI ToggleText;
        public Image ToggleImage;
        public Color OnColor = new Color(0.08f, 0.3f, 0.14f, 1f);
        public Color OffColor = new Color(0.2f, 0.08f, 0.06f, 1f);

        [Header("지금 곡")]
        public TextMeshProUGUI NowPlayingText;
        [Tooltip("번역 장치(6-166). Bake가 넣는다")]
        public ACO_Localization Loc;
        [Tooltip("재생 중 — {name} 자리에 곡 이름. 영어(번역 장치가 없을 때) — 번역은 표 bgm.*")]
        public string PlayingFormat = "NOW PLAYING\n{name}";
        [Tooltip("불러오는 중 — {name} 자리에 곡 이름")]
        public string LoadingFormat = "LOADING\n{name}";
        [Tooltip("이 상황에 곡이 없다(무음)")]
        public string NoTrackText = "NO MUSIC";
        [Tooltip("BGM이 꺼져 있다")]
        public string OffText = "BGM OFF";

        private void Start()
        {
            if (Bgm != null) { Refresh(Bgm.Volume, Bgm.Dynamic); }
        }

        private float _volume = -1f;
        private bool _on;
        private int _state = -1;
        private string _track = "";

        /// 번역 장치가 언어를 바꾸면 부른다 — 마지막 값으로 다시 쓴다.
        public void LocalizationChanged()
        {
            if (_volume >= 0f) { Refresh(_volume, _on); }
            if (_state >= 0) { SetTrack(_state, _track); }
        }

        /// 번역 표의 문장(장치가 없거나 표에 없으면 english). {0}에 값을 끼운다(6-166)
        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return s == key ? english : s;
        }
        private string T1(string key, string english, string a) { return T(key, english).Replace("{0}", a); }

        public void VolumeDown() { Adjust(-1); }
        public void VolumeUp() { Adjust(1); }

        public void Toggle()
        {
            if (Bgm == null) { return; }
            Bgm.ToggleDynamic();
            Play(ACO_UiSound.KIND_CONFIRM);
        }

        private void Adjust(int direction)
        {
            if (Bgm == null) { return; }
            Play(Bgm.AdjustVolume(direction) ? ACO_UiSound.KIND_CLICK : ACO_UiSound.KIND_DENY);
        }

        /// ACO_Bgm이 값이 바뀔 때 부른다.
        public void Refresh(float volume, bool on)
        {
            _volume = volume;
            _on = on;
            if (VolumeText != null) { VolumeText.text = T1("bgm.volume", "VOLUME  {0}%", Mathf.RoundToInt(volume * 100f).ToString()); }
            if (ToggleText != null) { ToggleText.text = on ? T("bgm.dyn.on", "DYNAMIC BGM  ON") : T("bgm.dyn.off", "DYNAMIC BGM  OFF"); }
            if (ToggleImage != null) { ToggleImage.color = on ? OnColor : OffColor; }
        }

        /// ACO_Bgm이 지금 곡·상태(ACO_Bgm.TRACK_*)가 바뀔 때 부른다.
        public void SetTrack(int state, string trackName)
        {
            _state = state;
            _track = trackName;
            if (NowPlayingText == null) { return; }
            string s;
            if (state == ACO_Bgm.TRACK_PLAYING) { s = T1("bgm.playing", PlayingFormat.Replace("{name}", "{0}"), trackName); }
            else if (state == ACO_Bgm.TRACK_LOADING) { s = T1("bgm.loading", LoadingFormat.Replace("{name}", "{0}"), trackName); }
            else if (state == ACO_Bgm.TRACK_OFF) { s = T("bgm.off", OffText); }
            else { s = T("bgm.none", NoTrackText); }
            NowPlayingText.text = s;
        }

        private void Play(int kind)
        {
            if (Sound != null) { Sound.PlayAt(kind, transform.position); }
        }
    }
}
