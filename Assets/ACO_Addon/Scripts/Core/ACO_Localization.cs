using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Data;

namespace ACO
{
    /// 패널 번역(6-166, 동기화 없음 — 각자 자기 언어). 씬에 하나.
    /// - 문장 표: Localization/ACO_Strings.tsv(키 · 한국어 · 영어 · 일본어)를 Setup Check가 Keys/Ko/En/Ja에 넣는다.
    /// - 고정 라벨: 생성기가 라벨 오브젝트 이름을 키로 짓고, Bake가 씬에서 이름이 표의 키인 TMP 글자를 모아 Labels/LabelKeys에 넣는다 — 언어가 바뀌면 한꺼번에 바꾼다.
    /// - 스크립트 문장: Get(키), 숫자 · 이름이 들어가면 Get1/Get2(키, 값) — 문장 안의 {0}, {1}을 바꾼다(이어 붙이지 않는다).
    /// - 듣는 쪽: Bake가 Loc 필드가 있는 스크립트를 Listeners에 넣고, 언어가 바뀌면 LocalizationChanged를 부른다.
    /// - 언어: 설정(ACO_HudSettings LANGUAGE)이 AUTO면 VRChat 클라이언트 언어(ko → 한국어, ja → 일본어, 그 밖 → 영어)를 따르고 바뀌면 따라간다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Localization : UdonSharpBehaviour
    {
        public const int LANG_KO = 0;
        public const int LANG_EN = 1;
        public const int LANG_JA = 2;
        public const int SET_AUTO = 0;
        public const int SET_KO = 1;
        public const int SET_EN = 2;
        public const int SET_JA = 3;

        [Header("문장 표 (Setup Check가 Localization/ACO_Strings.tsv에서 넣는다)")]
        public string[] Keys;
        public string[] Ko;
        public string[] En;
        public string[] Ja;

        [Header("연결 (Bake가 넣는다)")]
        [Tooltip("이름이 표의 키인 TMP 글자")]
        public TextMeshProUGUI[] Labels;
        public string[] LabelKeys;
        [Tooltip("이름이 표의 키인 3D TMP 글자(작전판 · 하이스코어판처럼 Canvas 없는 판)")]
        public TextMeshPro[] Labels3D;
        public string[] Label3DKeys;
        [Tooltip("언어가 바뀌면 LocalizationChanged를 부를 스크립트(Loc 필드가 있는 것)")]
        public UdonSharpBehaviour[] Listeners;

        private DataDictionary _index;
        private int _setting = SET_AUTO;
        private int _lang = -1;
        private bool _ready;

        void Start()
        {
            Init();
        }

        private void Init()
        {
            if (_ready) { return; }
            _ready = true;
            _index = new DataDictionary();
            int n = Keys != null ? Keys.Length : 0;
            for (int i = 0; i < n; i++)
            {
                if (!string.IsNullOrEmpty(Keys[i])) { _index.SetValue(Keys[i], i); }
            }
            Apply(Resolve());
        }

        /// 지금 언어(LANG_*).
        public int GetLanguage()
        {
            Init();
            return _lang;
        }

        /// 설정 값(SET_*) — 설정 패널이 부른다.
        public void SetSetting(int setting)
        {
            Init();
            _setting = Mathf.Clamp(setting, SET_AUTO, SET_JA);
            Apply(Resolve());
        }

        public override void OnLanguageChanged(string language)
        {
            if (_setting == SET_AUTO) { Apply(Resolve()); }
        }

        /// 키의 지금 언어 문장. 그 언어 칸이 비었으면 영어, 표에 없으면 키 그대로.
        public string Get(string key)
        {
            Init();
            DataToken t;
            if (_index == null || !_index.TryGetValue(key, TokenType.Int, out t)) { return key; }
            int i = t.Int;
            string[] src = _lang == LANG_KO ? Ko : (_lang == LANG_JA ? Ja : En);
            string s = src != null && i < src.Length ? src[i] : null;
            if (string.IsNullOrEmpty(s) && En != null && i < En.Length) { s = En[i]; }
            return string.IsNullOrEmpty(s) ? key : s;
        }

        public string Get1(string key, string a)
        {
            return Get(key).Replace("{0}", a);
        }

        public string Get2(string key, string a, string b)
        {
            return Get(key).Replace("{0}", a).Replace("{1}", b);
        }

        public string Get3(string key, string a, string b, string c)
        {
            return Get(key).Replace("{0}", a).Replace("{1}", b).Replace("{2}", c);
        }

        private int Resolve()
        {
            if (_setting == SET_KO) { return LANG_KO; }
            if (_setting == SET_EN) { return LANG_EN; }
            if (_setting == SET_JA) { return LANG_JA; }
            return FromCode(VRCPlayerApi.GetCurrentLanguage());
        }

        /// 언어 코드(ko, ja, en-US …) → LANG_*.
        public int FromCode(string code)
        {
            if (string.IsNullOrEmpty(code)) { return LANG_EN; }
            string c = code.ToLower();
            if (c.StartsWith("ko") || c.Contains("korean") || c.Contains("한국")) { return LANG_KO; }
            if (c.StartsWith("ja") || c.Contains("japanese") || c.Contains("日本")) { return LANG_JA; }
            return LANG_EN;
        }

        private void Apply(int lang)
        {
            if (lang == _lang) { return; }
            _lang = lang;
            if (Labels != null && LabelKeys != null)
            {
                int n = Mathf.Min(Labels.Length, LabelKeys.Length);
                for (int i = 0; i < n; i++) { if (Labels[i]) { Labels[i].text = Get(LabelKeys[i]); } }
            }
            if (Labels3D != null && Label3DKeys != null)
            {
                int n3 = Mathf.Min(Labels3D.Length, Label3DKeys.Length);
                for (int i = 0; i < n3; i++) { if (Labels3D[i]) { Labels3D[i].text = Get(Label3DKeys[i]); } }
            }
            if (Listeners != null)
            {
                for (int i = 0; i < Listeners.Length; i++) { if (Listeners[i]) { Listeners[i].SendCustomEvent("LocalizationChanged"); } }
            }
        }
    }
}
