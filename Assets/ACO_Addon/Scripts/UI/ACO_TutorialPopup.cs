using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace ACO
{
    /// 브리핑 판 튜토리얼(6-160, 브리핑 판으로 옮김 — 6-164. 동기화 없음 — 모든 것이 내 화면에서만).
    /// - 루트 = 브리핑 판(작전 콘솔) 화면 가운데, +Z = 방 쪽. Setup Check가 놓는다.
    /// - 열기: 판 머리줄 오른쪽의 튜토리얼 버튼(Open). 팝업은 판 앞에 판 크기 그대로 고정돼 판 전체를 덮는다(움직이지 않는다).
    /// - 홀로그램 화살표(Marker): 버튼 위에 떠서 돌고 위아래로 흔들리며 링이 맥박친다. 한 번도 열지 않은 사람에게만 보인다
    ///   (PlayerData SeenKey) — 열면 끄고 다시 켜지 않는다. 내가 ActiveRange 안에 있을 때만 움직인다.
    /// - 페이지: 처음 열면 0페이지, 같은 접속 중 다시 열면 마지막으로 보던 페이지. 마지막 페이지에선 다음 버튼을 숨긴다
    ///   (다음 = 닫기가 아니다 — 두 번 눌러 실수로 닫히지 않게).
    /// - 닫힘: X, 팝업에서 CloseDistance 넘게 멀어짐(방을 나감 · 순간이동 · 출격).
    /// - 언어(6-165, 6-166): 한국어 · 영어 · 일본어 — 번역 장치(설정 패널 LANGUAGE, AUTO = VRChat 언어)를 따른다. 판의 튜토리얼 버튼 글자도 함께 바뀐다.
    ///   문장은 생성기가 이 스크립트에 넣는다(LocKo/En/Ja — 튜토리얼 글자 표는 ACO_HUDBuilder.Tutorial.cs).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_TutorialPopup : UdonSharpBehaviour
    {
        [Header("홀로그램 화살표 (생성 코드가 넣는다)")]
        [Tooltip("화살표 루트 — 세로축으로 돌고 위아래로 흔들린다. 처음 열기 전까지만 켜진다")]
        public Transform Marker;
        [Tooltip("화살 머리 위 링 — 크기가 맥박친다")]
        public Transform[] Rings;

        [Header("팝업 (생성 코드가 넣는다)")]
        [Tooltip("팝업 루트(처음엔 꺼 둔다)")]
        public GameObject Popup;
        public GameObject[] Pages;
        public Image[] Dots;
        [Tooltip("다음 버튼 — 마지막 페이지에선 숨긴다")]
        public GameObject NextButton;
        public Color DotOn = new Color(0.37f, 0.88f, 0.56f, 1f);
        public Color DotOff = new Color(0.12f, 0.27f, 0.18f, 1f);

        [Header("언어 (생성 코드가 넣는다 — 6-165)")]
        [Tooltip("언어를 바꿀 글자들(오브젝트 이름 = 글자 키). 아래 세 배열과 같은 순서")]
        public TextMeshProUGUI[] LocTexts;
        public string[] LocKo;
        public string[] LocEn;
        public string[] LocJa;
        [Tooltip("번역 장치(6-166) — 설정 패널의 LANGUAGE를 따른다. Bake가 넣는다")]
        public ACO_Localization Loc;

        [Header("값")]
        [Tooltip("처음 열었는지 저장하는 PlayerData 키(고정 ID — 바꾸지 않는다)")]
        public string SeenKey = "aco_tutorial_seen";
        public float ActiveRange = 12f;
        [Tooltip("팝업에서 이만큼 멀어지면 닫는다(m) — 브리핑실 뒷자리에서도 열려 있게 방 깊이보다 크게")]
        public float CloseDistance = 7f;
        public float SpinSpeed = 45f;
        public float BobHeight = 0.04f;
        public float BobPeriod = 1.6f;
        [Tooltip("링 맥박: 크기 변화 비율, 주기")]
        public float PulseScale = 0.25f;
        public float PulsePeriod = 1.2f;

        private bool _seen;
        private bool _open;
        private bool _openedOnce;
        private int _page;
        private bool _near;
        private float _tick;
        private float _spin;
        private Vector3 _markerBase;
        private Vector3[] _ringBase;
        private bool _baseSet;
        private int _lang = -1;

        public const int LANG_KO = 0;
        public const int LANG_EN = 1;
        public const int LANG_JA = 2;

        void Start()
        {
            ApplyLanguage(Loc != null ? Loc.GetLanguage() : LANG_EN);
            if (Popup) { Popup.SetActive(false); }
            if (Marker)
            {
                _markerBase = Marker.localPosition;
                _baseSet = true;
            }
            int n = Rings != null ? Rings.Length : 0;
            _ringBase = new Vector3[n];
            for (int i = 0; i < n; i++) { if (Rings[i]) { _ringBase[i] = Rings[i].localScale; } }
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) { return; }
            bool v;
            if (PlayerData.TryGetBool(player, SeenKey, out v) && v) { _seen = true; }
            UpdateMarker();
        }

        // ───────────── 언어(6-165 → 6-166: 설정 패널 LANGUAGE) ─────────────

        /// 번역 장치가 언어를 바꾸면 부른다.
        public void LocalizationChanged()
        {
            if (Loc != null) { ApplyLanguage(Loc.GetLanguage()); }
        }

        private void ApplyLanguage(int lang)
        {
            if (lang == _lang) { return; }
            _lang = lang;
            string[] src = lang == LANG_KO ? LocKo : (lang == LANG_JA ? LocJa : LocEn);
            if (LocTexts != null && src != null)
            {
                int n = Mathf.Min(LocTexts.Length, src.Length);
                for (int i = 0; i < n; i++) { if (LocTexts[i]) { LocTexts[i].text = src[i]; } }
            }
        }

        /// 튜토리얼 버튼: 판 앞에 연다(열려 있으면 그대로).
        public void Open()
        {
            if (Popup == null) { return; }
            if (!_openedOnce) { _page = 0; _openedOnce = true; }
            ShowPage();
            Popup.SetActive(true);
            _open = true;
            if (!_seen)
            {
                _seen = true;
                PlayerData.SetBool(SeenKey, true);
            }
            UpdateMarker();
        }

        public void NextPage()
        {
            int count = Pages != null ? Pages.Length : 0;
            if (_page >= count - 1) { return; }
            _page++;
            ShowPage();
        }

        public void PrevPage()
        {
            if (_page <= 0) { return; }
            _page--;
            ShowPage();
        }

        public void ClosePopup()
        {
            _open = false;
            if (Popup) { Popup.SetActive(false); }
            UpdateMarker();
        }

        private void ShowPage()
        {
            int count = Pages != null ? Pages.Length : 0;
            for (int i = 0; i < count; i++) { if (Pages[i]) { Pages[i].SetActive(i == _page); } }
            if (Dots != null)
            {
                for (int i = 0; i < Dots.Length; i++) { if (Dots[i]) { Dots[i].color = i == _page ? DotOn : DotOff; } }
            }
            if (NextButton) { NextButton.SetActive(_page < count - 1); }
        }

        /// 화살표는 아직 안 열어 봤고 팝업이 닫혀 있을 때만 보인다.
        private void UpdateMarker()
        {
            if (Marker) { Marker.gameObject.SetActive(!_seen && !_open); }
        }

        void Update()
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }

            _tick -= Time.deltaTime;
            if (_tick <= 0f)
            {
                _tick = 0.25f;
                Vector3 head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                _near = (head - transform.position).sqrMagnitude <= ActiveRange * ActiveRange;
                if (_open && Popup && (head - Popup.transform.position).sqrMagnitude > CloseDistance * CloseDistance) { ClosePopup(); }
            }
            if (!_near || Marker == null || !_baseSet || !Marker.gameObject.activeSelf) { return; }

            float t = Time.time;
            _spin = (_spin + SpinSpeed * Time.deltaTime) % 360f;
            Marker.localRotation = Quaternion.Euler(0f, _spin, 0f);
            Marker.localPosition = _markerBase + Vector3.up * (Mathf.Sin(t * 2f * Mathf.PI / Mathf.Max(BobPeriod, 0.1f)) * BobHeight);
            if (Rings != null)
            {
                for (int i = 0; i < Rings.Length; i++)
                {
                    if (!Rings[i]) { continue; }
                    float k = 0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / Mathf.Max(PulsePeriod, 0.1f) - i * 1.2f);
                    Rings[i].localScale = _ringBase[i] * (1f + PulseScale * k);
                }
            }
        }
    }
}
