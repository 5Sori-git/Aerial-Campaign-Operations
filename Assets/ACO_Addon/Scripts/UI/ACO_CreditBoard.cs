using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ACO
{
    /// 크레딧 보드(로컬 전용, 동기화 없음): 시작할 때 Source(텍스트 파일 — Credits/ACO_Credits.txt)를 본문 글자에 넣는다.
    /// 크레딧은 텍스트 파일만 고치면 된다(프리팹을 다시 만들 필요 없음). 일본어 글자는 글꼴의 일본어 폴백이 그린다.
    /// 스크롤(6-228, 로컬): 본문은 ScrollRect 안에서 끌기 · 스크롤바 · ▲▼ 버튼(ACO_CreditScrollButton → ScrollStep, 반 화면씩)으로 움직인다.
    /// 처음 · 다시 들어올 때 맨 위. 글이 짧아 한 화면에 다 들어가면 스크롤바 · 버튼을 숨기고, 맨 위 · 맨 아래에서는 그쪽 버튼을 흐리게 한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_CreditBoard : UdonSharpBehaviour
    {
        [Tooltip("크레딧 텍스트 파일(TMP 서식 태그 사용 가능). 비우면 본문을 그대로 둔다")]
        public TextAsset Source;
        [Tooltip("언어별 텍스트 파일(한국어 · 영어 · 일본어 = ACO_Localization.LANG_*, 6-251 업데이트 노트). 비우면 Source 하나를 쓴다(크레딧)")]
        public TextAsset[] LanguageSources;
        [Tooltip("번역 장치(Bake가 넣는다) — 언어가 바뀌면 LocalizationChanged로 본문을 바꾼다")]
        public ACO_Localization Loc;
        [Tooltip("본문 글자(프리팹 빌더가 넣는다) — 스크롤 내용이기도 하다(높이가 글에 맞춰 는다)")]
        public TextMeshProUGUI Body;

        [Header("스크롤 (프리팹 빌더가 넣는다)")]
        public ScrollRect Scroll;
        [Tooltip("보이는 창(본문이 이 안에서 잘린다)")]
        public RectTransform Viewport;
        [Tooltip("넘칠 때만 보이는 것: 스크롤바 · 버튼")]
        public GameObject[] ScrollControls;
        public Button UpButton;
        public Button DownButton;

        private const float EDGE = 0.002f;   // 맨 위 · 맨 아래로 치는 정규화 거리
        private bool _overflow;
        private float _timer;

        private int _shownLang = -2;

        void Start()
        {
            ApplySource();
        }

        /// 번역 장치가 언어를 바꿨다(로컬) — 언어별 파일이 있으면 본문을 바꾸고 맨 위로.
        public void LocalizationChanged()
        {
            ApplySource();
        }

        private void ApplySource()
        {
            if (Body == null) { return; }
            TextAsset src = Source;
            int lang = -1;
            if (LanguageSources != null && LanguageSources.Length > 0)
            {
                lang = Loc != null ? Loc.GetLanguage() : ACO_Localization.LANG_EN;
                if (lang >= 0 && lang < LanguageSources.Length && LanguageSources[lang] != null) { src = LanguageSources[lang]; }
                else if (ACO_Localization.LANG_EN < LanguageSources.Length && LanguageSources[ACO_Localization.LANG_EN] != null) { src = LanguageSources[ACO_Localization.LANG_EN]; }
            }
            if (lang == _shownLang && _shownLang != -2) { return; }
            _shownLang = lang;
            if (src != null) { Body.text = src.text; }
            // 레이아웃(본문 높이)이 다음 프레임에 맞춰진 뒤 넘침을 본다
            SendCustomEventDelayedFrames(nameof(_Refresh), 2);
        }

        public void _Refresh()
        {
            _overflow = Overflow() > 1f;
            if (ScrollControls != null)
            {
                for (int i = 0; i < ScrollControls.Length; i++) { if (ScrollControls[i]) { ScrollControls[i].SetActive(_overflow); } }
            }
            if (Scroll) { Scroll.velocity = Vector2.zero; Scroll.verticalNormalizedPosition = 1f; }
            UpdateButtons();
        }

        void Update()
        {
            // 끌기 · 스크롤바로 움직여도 버튼 흐림이 맞게 — 넘칠 때만, 0.2초마다
            if (!_overflow) { return; }
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = 0.2f;
            UpdateButtons();
        }

        /// 버튼(ACO_CreditScrollButton)이 부른다. dir +1 = 아래로(뒤쪽 글), −1 = 위로. 반 화면씩. 움직였으면 참.
        public bool ScrollStep(int dir)
        {
            if (!Scroll || !Viewport) { return false; }
            float over = Overflow();
            if (over <= 1f) { return false; }
            float pos = Scroll.verticalNormalizedPosition;
            float step = Viewport.rect.height * 0.5f / over;
            float next = Mathf.Clamp01(pos - dir * step);
            if (Mathf.Abs(next - pos) < EDGE * 0.5f) { return false; }
            Scroll.velocity = Vector2.zero;
            Scroll.verticalNormalizedPosition = next;
            UpdateButtons();
            return true;
        }

        private float Overflow()
        {
            if (!Scroll || !Viewport) { return 0f; }
            RectTransform content = Scroll.content;   // = 본문 글자(TMP rectTransform은 Udon에 없다)
            if (!content) { return 0f; }
            return content.rect.height - Viewport.rect.height;
        }

        private void UpdateButtons()
        {
            if (!Scroll) { return; }
            float pos = Scroll.verticalNormalizedPosition;
            if (UpButton) { UpButton.interactable = pos < 1f - EDGE; }
            if (DownButton) { DownButton.interactable = pos > EDGE; }
        }
    }
}
