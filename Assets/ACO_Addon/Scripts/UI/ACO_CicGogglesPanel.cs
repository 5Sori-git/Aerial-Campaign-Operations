using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;

namespace ACO
{
    /// CIC 고글 패널(6-275 — CIC 남쪽 책상 모니터 위, 도구 패널과 따로). 동기화 없음.
    ///  - 관전 대상(로컬): UAV(정찰 드론) 또는 참가자 기체(Flow.Aircraft 번호). 고글(ACO_CicGoggles)이 읽는다.
    ///  - 고글 회수(RESET): 두 고글을 책상 위 처음 자리로 돌려놓는다 — 누가 쓰고 있어도(그 오너 화면에서 놓고 돌아간다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_CicGogglesPanel : UdonSharpBehaviour
    {
        public const int VIEW_UAV = -1;

        [Header("연결 (프리팹 빌더 · Bake)")]
        public ACO_CicGoggles[] Goggles;
        public ACO_GameFlow Flow;
        public ACO_UiSound Sound;
        [Tooltip("번역 장치(6-166). Bake가 넣는다")]
        public ACO_Localization Loc;

        [Header("패널")]
        [Tooltip("UAV, 슬롯 1~4 버튼 글자 · 버튼")]
        public TextMeshProUGUI[] ViewTexts;
        [Tooltip("화질 버튼 글자(HD 1024 / FHD 1536 — 관전 카메라 HD/FHD처럼, 로컬)")]
        public TextMeshProUGUI ResolutionText;
        public Image[] ViewButtons;
        public Color IdleColor = new Color(0.22f, 0.24f, 0.26f, 0.95f);
        public Color SelectedColor = new Color(0.2f, 0.45f, 0.75f, 0.95f);
        [Tooltip("내가 이 거리(m) 안에 있을 때만 글자를 고친다")]
        public float ActiveRadius = 6f;

        private int _view = VIEW_UAV;
        private float _textTimer;
        private bool _fhd;

        void Start() { UpdateTexts(); }

        /// 고글 관전 대상(로컬): VIEW_UAV 또는 참가자 기체 번호(0~).
        public int GetView() { return _view; }

        /// 고글 화질(로컬): FHD(1536²)를 골랐나. 처음엔 HD(1024²).
        public bool IsFhd() { return _fhd; }

        public void ClickResolution()
        {
            _fhd = !_fhd;
            if (Goggles != null)
            {
                for (int i = 0; i < Goggles.Length; i++) { if (Goggles[i]) { Goggles[i].ResolutionChanged(); } }
            }
            if (Sound) { Sound.PlayAt(ACO_UiSound.KIND_CLICK, transform.position); }
            UpdateTexts();
        }

        public void ClickViewUav() { SetView(VIEW_UAV); }
        public void ClickView0() { SetView(0); }
        public void ClickView1() { SetView(1); }
        public void ClickView2() { SetView(2); }
        public void ClickView3() { SetView(3); }

        private void SetView(int v)
        {
            _view = v;
            if (Sound) { Sound.PlayAt(ACO_UiSound.KIND_CLICK, transform.position); }
            UpdateTexts();
        }

        /// 고글 회수: 두 고글의 오너 화면에 돌아가라고 보낸다(쓰고 있으면 놓게 한다).
        public void ClickReset()
        {
            if (Goggles != null)
            {
                for (int i = 0; i < Goggles.Length; i++) { if (Goggles[i]) { Goggles[i].RequestReset(); } }
            }
            if (Sound) { Sound.PlayAt(ACO_UiSound.KIND_CONFIRM, transform.position); }
        }

        void Update()
        {
            _textTimer -= Time.deltaTime;
            if (_textTimer > 0f) { return; }
            _textTimer = 0.5f;
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me) || Vector3.Distance(me.GetPosition(), transform.position) > ActiveRadius) { return; }
            UpdateTexts();
        }

        public void LocalizationChanged() { UpdateTexts(); }

        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return s == key ? english : s;
        }
        private string T1(string key, string english, string a) { return T(key, english).Replace("{0}", a); }

        private void UpdateTexts()
        {
            if (ResolutionText) { ResolutionText.text = _fhd ? "FHD 1536" : "HD 1024"; }
            int n = ViewTexts != null ? ViewTexts.Length : 0;
            for (int i = 0; i < n; i++)
            {
                int v = i - 1;   // 0번 = UAV
                if (ViewTexts[i]) { ViewTexts[i].text = v == VIEW_UAV ? T("cic.view.uav", "UAV") : SlotText(v); }
                if (ViewButtons != null && i < ViewButtons.Length && ViewButtons[i]) { ViewButtons[i].color = v == _view ? SelectedColor : IdleColor; }
            }
        }

        /// 관전 대상 v = 참가자 기체 번호(Flow.Aircraft). 글자는 그 기체의 슬롯 이름 + 지금 조종사.
        private string SlotText(int v)
        {
            string label = T1("flow.slot.fallback", "SLOT {0}", (v + 1).ToString());
            if (Flow == null || Flow.Aircraft == null || v >= Flow.Aircraft.Length || Flow.Aircraft[v] == null) { return label; }
            int slot = Flow.GetSlotOfAircraft(Flow.Aircraft[v]);
            if (slot >= 0) { label = Flow.GetSlotLabel(slot); }
            if (!Flow.Aircraft[v].Occupied) { return label; }
            VRCPlayerApi p = Networking.GetOwner(Flow.Aircraft[v].gameObject);
            return Utilities.IsValid(p) ? label + "\n" + p.displayName : label;
        }
    }
}
