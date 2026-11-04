using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace ACO
{
    /// TAC 네임(콜사인, 로컬 패널 — 동기화 코드 없음): 내 콜사인을 플레이어 영구 저장(PlayerData, 고정 키 KEY)에 둔다. PlayerData는 VRChat이 모든 플레이어에게 보내 주므로
    /// 다른 사람 화면도 내 콜사인을 읽는다(표적 마커가 탑승한 아군기 테두리 위에 — 비어 있으면 예전처럼 WINGMAN n).
    ///  - 입력: 패널의 입력 칸(UI InputField — VRChat 키보드)에 쓰고 SET(또는 입력 끝) → 대문자로, 영문·숫자·공백·'-'만, 최대 MaxLength자. CLEAR = 지우기.
    ///  - 중복: 인스턴스 안에서 같은 이름이면 플레이어 번호(먼저 들어온 사람)가 작은 쪽이 그대로, 다음부터 뒤에 2, 3…(모든 화면에서 같은 결과).
    ///  - RefreshInterval마다 지금 플레이어 목록과 이름을 다시 읽어 표시 이름을 정한다(GetLabel이 그 결과를 돌려준다 — 매 프레임 읽지 않는다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_TacName : UdonSharpBehaviour
    {
        /// 저장 키 — 절대 바꾸지 않는다.
        public const string KEY = "aco_tac_name";
        private const int MAX_PLAYERS = 100;

        [Header("입력 패널 (프리팹 빌더)")]
        public InputField Input;
        public TextMeshProUGUI CurrentText;
        public TextMeshProUGUI MessageText;
        [Tooltip("최대 글자 수")]
        public int MaxLength = 12;
        [Tooltip("표시 이름을 다시 정하는 주기(초)")]
        public float RefreshInterval = 1f;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;

        private VRCPlayerApi[] _players = new VRCPlayerApi[MAX_PLAYERS];
        private int[] _ids = new int[MAX_PLAYERS];
        private string[] _bases = new string[MAX_PLAYERS];
        private string[] _labels = new string[MAX_PLAYERS];
        private int _count;
        private float _timer;
        private bool _restored;

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) { return; }
            _restored = true;
            if (Input) { Input.text = ReadName(player); }
            Refresh();
            UpdateTexts("");
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = RefreshInterval;
            Refresh();
        }

        // ───────────── 패널 ─────────────

        /// SET 버튼·입력 끝: 다듬어 저장한다.
        public void Submit()
        {
            if (!_restored) { UpdateTexts("<color=#FF5544>LOADING SAVE DATA...</color>"); Play(ACO_UiSound.KIND_DENY); return; }
            string name = Sanitize(Input ? Input.text : "");
            PlayerData.SetString(KEY, name);
            if (Input) { Input.text = name; }
            Refresh();
            UpdateTexts(name.Length > 0 ? "CALLSIGN SET" : "CALLSIGN CLEARED");
            Play(ACO_UiSound.KIND_CONFIRM);
        }

        /// CLEAR 버튼.
        public void ClearName()
        {
            if (!_restored) { Play(ACO_UiSound.KIND_DENY); return; }
            PlayerData.SetString(KEY, "");
            if (Input) { Input.text = ""; }
            Refresh();
            UpdateTexts("CALLSIGN CLEARED");
            Play(ACO_UiSound.KIND_CLICK);
        }

        private void Play(int kind)
        {
            if (Sound) { Sound.PlayAt(kind, transform.position); }
        }

        private void UpdateTexts(string message)
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (CurrentText && Utilities.IsValid(me))
            {
                string mine = ReadName(me);
                string shown = GetLabel(me);
                CurrentText.text = mine.Length == 0 ? "YOUR CALLSIGN: <color=#7FBF8A>(NONE - WINGMAN n)</color>"
                    : "YOUR CALLSIGN: " + mine + (shown != mine && shown.Length > 0 ? "  <color=#FFD24D>(SHOWN AS " + shown + " - SAME NAME IN USE)</color>" : "");
            }
            if (MessageText) { MessageText.text = message; }
        }

        // ───────────── 표시 이름 ─────────────

        /// 그 플레이어의 표시 이름(중복이면 뒤에 숫자). 콜사인이 없으면 "".
        public string GetLabel(VRCPlayerApi p)
        {
            if (!Utilities.IsValid(p)) { return ""; }
            int id = p.playerId;
            for (int i = 0; i < _count; i++) { if (_ids[i] == id) { return _labels[i]; } }
            return "";
        }

        /// 플레이어 목록과 이름을 다시 읽어 표시 이름을 정한다: 같은 이름 중 플레이어 번호가 더 작은 사람 수 + 1을 뒤에 붙인다(첫 사람은 그대로).
        private void Refresh()
        {
            int n = Mathf.Min(VRCPlayerApi.GetPlayerCount(), MAX_PLAYERS);
            VRCPlayerApi.GetPlayers(_players);
            _count = 0;
            for (int i = 0; i < n; i++)
            {
                VRCPlayerApi p = _players[i];
                if (!Utilities.IsValid(p)) { continue; }
                _ids[_count] = p.playerId;
                _bases[_count] = ReadName(p);
                _count++;
            }
            for (int i = 0; i < _count; i++)
            {
                string b = _bases[i];
                if (b.Length == 0) { _labels[i] = ""; continue; }
                int before = 0;
                for (int j = 0; j < _count; j++) { if (j != i && _bases[j] == b && _ids[j] < _ids[i]) { before++; } }
                _labels[i] = before == 0 ? b : b + (before + 1);
            }
        }

        private string ReadName(VRCPlayerApi p)
        {
            string s;
            return PlayerData.TryGetString(p, KEY, out s) ? Sanitize(s) : "";
        }

        /// 대문자, 영문·숫자·공백·'-'만(기본 글꼴에 있는 글자), 앞뒤 공백 없이, 최대 MaxLength자.
        private string Sanitize(string raw)
        {
            if (raw == null) { return ""; }
            string up = raw.ToUpper();
            string s = "";
            for (int i = 0; i < up.Length && s.Length < Mathf.Max(MaxLength, 1); i++)
            {
                char c = up[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || (c == ' ' && s.Length > 0);
                if (ok) { s += c.ToString(); }
            }
            return s.Trim();
        }
    }
}
