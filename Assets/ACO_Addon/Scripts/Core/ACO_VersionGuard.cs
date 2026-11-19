using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace ACO
{
    /// 이전 버전 인스턴스와 섞였는지 알린다(6-296). 막지는 않는다 — 알림만 작전판 위에 계속 띄운다.
    /// - BuildStamp: 빌드(업로드 · Build & Test)할 때 에디터가 넣는 빌드 번호(2020-01-01 UTC부터 지난 분). 0이면 검사를 끈다.
    /// - 인스턴스 번호(_instanceStamp, 동기화): 인스턴스를 연 사람만 자기 BuildStamp를 한 번 기록한다 —
    ///   "들어온 순간(내 OnPlayerJoined) 마스터"인 사람(6-327: 예전엔 Start에서 "오너이고 혼자"였는데, 여럿이 한꺼번에 들어오면
    ///   연 사람이 로딩하는 사이 인원이 늘어 아무도 기록하지 않았다). 이미 사람이 있는 인스턴스에 들어온 사람은 들어온 순간 마스터가 아니므로,
    ///   옛 인스턴스에 새 버전으로 들어가거나 나중에 마스터를 넘겨받아도 기록하지 않는다(섞인 인스턴스가 정상처럼 보이지 않게).
    ///   보내기에 실패하면(OnPostSerialization) 1초 뒤 다시 보낸다.
    /// - 비교(로컬): 같으면 정상(알림 없음). 다르거나, 입장 뒤 CheckTimeout초(60) 동안 번호가 없으면(이 기능이 없는 옛 버전 인스턴스) 다른 버전.
    ///   그 전까지는 노란 "확인 중" — 연 사람이 늦게 로딩돼도 빨간 알림이 먼저 뜨지 않게.
    ///   늦게 받은 번호가 같으면 정상으로 돌아간다. 비교는 정수 같음만 본다(크기 · 날짜 형식과 무관).
    /// - 표시: 작전판(브리핑 판) 위 가장자리 바로 위 알림 띠 — 확인 중(노랑) · 다른 버전(빨강). 정상이 될 때까지 계속 보인다. 클릭은 받지 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_VersionGuard : UdonSharpBehaviour
    {
        public const int STATE_CHECK = 0;
        public const int STATE_OK = 1;
        public const int STATE_MISMATCH = 2;

        [Header("빌드 번호 (빌드할 때 에디터가 자동으로 넣는다 — 손대지 않는다)")]
        [Tooltip("2020-01-01 UTC부터 지난 분. 0이면 버전 검사를 하지 않는다")]
        public int BuildStamp;

        [Header("연결 (생성 코드 · Bake)")]
        [Tooltip("작전판 위 알림 띠(처음엔 꺼 둔다)")]
        public GameObject Banner;
        public Image BannerBack;
        [Tooltip("알림 글자 — 상태에 따라 스크립트가 바꾼다")]
        public TextMeshProUGUI BannerText;
        [Tooltip("번역 장치 — Bake가 넣는다")]
        public ACO_Localization Loc;

        [Header("값")]
        [Tooltip("입장 뒤 이만큼(초) 인스턴스 번호가 없으면 이전 버전 인스턴스로 본다(그 전엔 '확인 중'). 연 사람이 늦게 로딩될 수 있어 넉넉히(6-327)")]
        public float CheckTimeout = 60f;
        public float CheckInterval = 0.5f;
        public Color CheckColor = new Color(0.55f, 0.42f, 0.08f, 0.95f);
        public Color MismatchColor = new Color(0.55f, 0.08f, 0.06f, 0.95f);

        [UdonSynced] private int _instanceStamp;
        private int _state = -1;
        private float _joinAt;
        private float _nextCheck;
        private bool _stampOwner;   // 내가 인스턴스 번호를 기록한 사람(보내기 실패면 다시 보낸다)

        void Start()
        {
            _joinAt = Time.time;
            if (BuildStamp == 0) { SetState(STATE_OK); return; }
            SetState(STATE_CHECK);
            Evaluate();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) { return; }
            _joinAt = Time.time;   // 기다리는 시간은 네트워크에 들어온 때부터
            if (BuildStamp == 0) { SetState(STATE_OK); return; }
            // 인스턴스를 연 사람 = 들어온 순간 마스터(빈 인스턴스에 처음 들어온 사람만 그렇다)
            if (Networking.IsMaster && _instanceStamp == 0)
            {
                if (!Networking.IsOwner(gameObject)) { Networking.SetOwner(player, gameObject); }
                _stampOwner = true;
                _instanceStamp = BuildStamp;
                RequestSerialization();
                SetState(STATE_OK);
                return;
            }
            Evaluate();
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (!result.success && _stampOwner) { SendCustomEventDelayedSeconds(nameof(ResendStamp), 1f); }
        }

        public void ResendStamp()
        {
            if (!_stampOwner || !Networking.IsOwner(gameObject)) { return; }
            RequestSerialization();
        }

        void Update()
        {
            if (_state == STATE_OK) { return; }
            if (Time.time < _nextCheck) { return; }
            _nextCheck = Time.time + CheckInterval;
            Evaluate();
        }

        public override void OnDeserialization() { Evaluate(); }

        private void Evaluate()
        {
            if (BuildStamp == 0) { SetState(STATE_OK); return; }
            if (_instanceStamp != 0) { SetState(_instanceStamp == BuildStamp ? STATE_OK : STATE_MISMATCH); return; }
            if (Time.time - _joinAt >= CheckTimeout) { SetState(STATE_MISMATCH); }
        }

        private void SetState(int state)
        {
            if (state == _state) { return; }
            _state = state;
            Refresh();
        }

        private void Refresh()
        {
            bool show = _state == STATE_CHECK || _state == STATE_MISMATCH;
            if (Banner) { Banner.SetActive(show); }
            if (!show) { return; }
            bool check = _state == STATE_CHECK;
            if (BannerBack) { BannerBack.color = check ? CheckColor : MismatchColor; }
            if (BannerText)
            {
                BannerText.text = check ? T("ver.checking", "Checking the world version...")
                    : T("ver.mismatch", "This instance was opened with a different (older) version of the world.\nPlease create a new instance.");
            }
        }

        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return string.IsNullOrEmpty(s) || s == key ? english : s;   // 표에 없으면 Get은 키를 돌려준다
        }

        public void LocalizationChanged() { Refresh(); }

        public int GetState() { return _state; }
    }
}
