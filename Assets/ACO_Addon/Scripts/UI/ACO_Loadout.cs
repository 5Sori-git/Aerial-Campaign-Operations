using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ACO
{
    /// 격납고(로컬 전용, 동기화 없음). 내 특수무장을 고른다 — 고른 값은 이 클라이언트에만 있고, 내가 조종석에 앉을 때
    /// 그 기체의 특수무장 슬롯(ACO_DFUNC_Special)이 읽어서 장착하고 다른 플레이어에게 알린다(플레이어별 무장).
    /// 무장 목록(SpecialIds/Names/Descriptions/Prices/Icons/Categories)은 같은 순서의 배열이다. 화면은 목록형 — 줄마다 왼쪽 아이콘, 오른쪽 무장 이름과
    /// 상태(EQUIPPED / OWNED / 가격). 분류(CAT_*: 공대공 → 공대지 → 폭탄 → 기타)마다 한 화면이고 좌우 버튼(ACO_HangarButton 페이지 넘김)으로 돌아가며 넘긴다
    /// (한 분류가 줄 수보다 많으면 그 분류의 다음 쪽 → 다음 분류). 버튼(ACO_HangarButton)은 화면 안의 줄 번호로 고른다.
    /// 목록은 Bake가 무장 정의 프리팹(ACO_SpecialWeaponDef, Assets/ACO_Addon/SpecialWeapons)에 맞춘다.
    /// 패널은 UI(월드 공간 Canvas + UI 버튼) — 프리팹 빌더가 만든다.
    ///
    /// 해금: 가격(SpecialPrices)이 0보다 큰 무장은 진행도(ACO_PlayerProgress)에서 사야 쓸 수 있다. 잠긴 무장을 누르면 설명과 가격만 보여 주고
    /// (장착은 그대로), BUY 버튼으로 사면 바로 장착한다. 고른 무장은 영구 저장돼 다음 접속 때도 이어진다. 진행도가 없으면 모두 해금.
    /// 해금한 무장이 하나도 없으면 특수무장 없이 출격한다(슬롯이 비고, GetSpecialId = "").
    /// 미리보기: PreviewVisuals[목록 번호] 중 지금 보고 있는 무장(잠긴 무장 포함)의 오브젝트만 켠다 — 격납고 전시 기체의 무장 모델(로컬).
    /// 같은 오브젝트를 여러 무장 칸에 넣어도 된다(보고 있는 무장의 오브젝트와 같으면 켜 둔다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Loadout : UdonSharpBehaviour
    {
        public const int CAT_AAM = 0;
        public const int CAT_AGM = 1;
        public const int CAT_BOMB = 2;
        public const int CAT_OTHER = 3;
        public const int CATEGORY_COUNT = 4;

        [Header("특수무장 목록 (같은 순서)")]
        [Tooltip("무장 ID. 기체의 특수무장 슬롯(ACO_DFUNC_Special.ModuleIds)과 같은 값을 쓴다. 기체에 없는 무장을 고르면 그 기체의 0번 모듈을 쓴다")]
        public string[] SpecialIds = new string[] { "4AAM", "UGB", "RKT" };
        [Tooltip("표시 이름")]
        public string[] SpecialNames = new string[] { "4AAM", "UGB", "RKT" };
        [Tooltip("설명(격납고 패널에 표시)")]
        [TextArea] public string[] SpecialDescriptions = new string[] {
            "MULTI-TARGET AAM\nLOCKS UP TO 4 AIR TARGETS - ONE MISSILE EACH",
            "UNGUIDED BOMB\nFREE-FALL BOMBS FOR GROUND TARGETS",
            "ROCKET LAUNCHER\nUNGUIDED ROCKETS FOR GROUND TARGETS" };
        [Tooltip("해금 가격(크레딧). 0 = 처음부터 해금")]
        public int[] SpecialPrices = new int[] { 6000, 8000, 8000 };
        [Tooltip("버튼 아이콘. 비운 칸은 아이콘 없이 이름만")]
        public Sprite[] SpecialIcons;
        [Tooltip("분류(CAT_*: 0 공대공, 1 공대지, 2 폭탄, 3 기타). Bake가 무장 정의(분류 또는 모듈의 강화 계열)로 채운다")]
        public int[] SpecialCategories = new int[] { 0, 2, 2 };
        [Tooltip("분류 이름(CAT_* 순서)")]
        public string[] CategoryNames = new string[] { "AIR-TO-AIR", "AIR-TO-GROUND", "BOMBS / ROCKETS", "OTHER" };
        [Tooltip("처음 보여 줄 무장(목록 번호). 해금돼 있으면 장착한다")]
        public int DefaultSpecial = 0;

        [Header("게임 진행")]
        [Tooltip("빌드 시 ACO_SceneBaker가 넣는다. 모두 READY해 출격 카운트다운이 시작되면 무장 변경·해금 구매를 막는다")]
        public ACO_GameFlow Flow;

        [Header("진행도")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 진행도를 넣는다. 비면 모든 무장이 해금된 것으로 본다")]
        public ACO_PlayerProgress Progress;

        [Header("패널 표시 (프리팹 빌더가 채운다)")]
        [Tooltip("지금 보고 있는 무장의 이름과 설명(잠겼으면 가격)")]
        public TextMeshProUGUI SelectionText;
        [Tooltip("페이지 칸별 버튼 배경(장착한 무장만 SelectedColor, 잠긴 무장은 LockedColor). 칸 수 = 한 페이지의 무장 수, 남는 칸은 숨긴다")]
        public Image[] ButtonImages;
        [Tooltip("칸별 이름 글자(페이지에 따라 바뀐다)")]
        public TextMeshProUGUI[] ButtonLabels;
        [Tooltip("칸별 아이콘(SpecialIcons에서)")]
        public Image[] ButtonIcons;
        [Tooltip("칸 구석의 자물쇠 표시. 잠긴 무장만 켠다")]
        public GameObject[] LockIcons;
        [Tooltip("분류 표시(< AIR-TO-AIR > 1/4)")]
        public TextMeshProUGUI PageText;
        [Tooltip("분류 넘김 좌우 버튼(늘 보인다 — 분류 4개를 돌아가며 넘긴다)")]
        public GameObject[] PagerObjects;
        [Tooltip("이 분류에 무장이 없을 때 켜는 글자")]
        public GameObject EmptyText;
        [Tooltip("잠긴 무장을 보고 있을 때만 켜지는 구매 버튼")]
        public GameObject BuyButton;
        [Tooltip("구매 버튼 글자")]
        public TextMeshProUGUI BuyText;
        public Color NormalColor = new Color(0.08f, 0.3f, 0.14f, 1f);
        public Color SelectedColor = new Color(0.25f, 0.75f, 0.35f, 1f);
        public Color LockedColor = new Color(0.12f, 0.14f, 0.13f, 1f);
        public Color PreviewColor = new Color(0.45f, 0.4f, 0.12f, 1f);
        [Tooltip("구매 버튼 색: 살 수 있을 때 / 크레딧이 모자랄 때(회색)")]
        public Color BuyColor = new Color(0.45f, 0.4f, 0.12f, 1f);
        public Color BuyDisabledColor = new Color(0.16f, 0.17f, 0.17f, 1f);

        [Header("미리보기 (선택)")]
        [Tooltip("목록 번호별 미리보기 오브젝트(격납고 전시 기체에 달린 무장 모델 등). 보고 있는 무장 것만 켜고 나머지는 끈다. 비운 칸은 건너뛴다")]
        public GameObject[] PreviewVisuals;

        private int _special = -1;   // 장착한 무장(-1 = 없음)
        private int _preview = -1;   // 보고 있는 무장(잠긴 무장일 수 있다)
        private int _category;      // 보고 있는 분류
        private int _page;          // 그 분류 안의 쪽
        private int[] _rows;        // 줄 → 목록 번호(-1 = 빈 줄)
        private Image _buyImage;
        private bool _started;
        private bool _restoredSeen;
        private int _seenVersion = -1;
        private string _message = "";

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (IsUnlocked(DefaultSpecial)) { Equip(DefaultSpecial); }
            else
            {
                _preview = Mathf.Clamp(DefaultSpecial, 0, Mathf.Max((SpecialIds != null ? SpecialIds.Length : 0) - 1, 0));
                ShowWeapon(_preview);
                Refresh();
            }
        }

        void Update()
        {
            if (!Progress) { return; }
            // 저장 값을 불러오면 지난번에 고른 무장으로 돌아간다.
            if (!_restoredSeen && Progress.IsReady())
            {
                _restoredSeen = true;
                int saved = IndexOfId(Progress.GetSavedSpecialId());
                if (saved >= 0 && IsUnlocked(saved)) { Equip(saved); }
            }
            if (Progress.GetVersion() != _seenVersion)
            {
                _seenVersion = Progress.GetVersion();
                Refresh();
            }
        }

        /// 격납고 버튼이 부른다(slot = 지금 화면 안의 줄 번호). 해금된 무장은 장착하고, 잠긴 무장은 보기만 한다.
        /// 버튼 효과음 종류(ACO_UiSound.KIND_*)를 돌려준다: 장착 = 결정, 잠긴 무장 보기 = 클릭.
        public int Select(int slot)
        {
            int n = SpecialIds != null ? SpecialIds.Length : 0;
            int index = _rows != null && slot >= 0 && slot < _rows.Length ? _rows[slot] : -1;
            if (n == 0 || index < 0 || index >= n) { return ACO_UiSound.KIND_DENY; }
            _message = "";
            if (IsUnlocked(index))
            {
                if (IsLocked()) { return ACO_UiSound.KIND_DENY; }
                Equip(index);
                return ACO_UiSound.KIND_CONFIRM;
            }
            _preview = index;
            Refresh();
            return ACO_UiSound.KIND_CLICK;
        }

        /// 좌우 버튼(dir = −1 이전, +1 다음): 분류 안의 다음 쪽, 끝이면 다음 분류(공대공 → 공대지 → 폭탄 → 기타 → 공대공). 효과음 종류를 돌려준다.
        public int ChangePage(int dir)
        {
            if (dir > 0)
            {
                if (_page + 1 < PagesIn(_category)) { _page++; }
                else { _category = (_category + 1) % CATEGORY_COUNT; _page = 0; }
            }
            else
            {
                if (_page > 0) { _page--; }
                else { _category = (_category + CATEGORY_COUNT - 1) % CATEGORY_COUNT; _page = PagesIn(_category) - 1; }
            }
            Refresh();
            return ACO_UiSound.KIND_CLICK;
        }

        private int PageSize()
        {
            int n = ButtonImages != null ? ButtonImages.Length : 0;
            return n > 0 ? n : Mathf.Max(SpecialIds != null ? SpecialIds.Length : 0, 1);
        }

        public int CategoryOf(int index)
        {
            int c = SpecialCategories != null && index >= 0 && index < SpecialCategories.Length ? SpecialCategories[index] : CAT_OTHER;
            return c >= 0 && c < CATEGORY_COUNT ? c : CAT_OTHER;
        }

        private int CountIn(int category)
        {
            int n = SpecialIds != null ? SpecialIds.Length : 0;
            int count = 0;
            for (int i = 0; i < n; i++) { if (CategoryOf(i) == category) { count++; } }
            return count;
        }

        private int PagesIn(int category)
        {
            int size = PageSize();
            return Mathf.Max((CountIn(category) + size - 1) / size, 1);
        }

        /// 그 무장이 보이는 분류·쪽으로 옮긴다.
        private void ShowWeapon(int index)
        {
            _category = CategoryOf(index);
            int order = 0;
            for (int i = 0; i < index; i++) { if (CategoryOf(i) == _category) { order++; } }
            _page = order / PageSize();
        }

        /// 구매 버튼: 보고 있는 잠긴 무장을 사고 장착한다. 효과음 종류를 돌려준다(산 경우만 결정음).
        /// 출격 카운트다운 중이면 안내를 띄우고 true(무장 변경·구매 거절).
        private bool IsLocked()
        {
            if (Flow == null || !Flow.IsLoadoutLocked()) { return false; }
            _message = "ALL READY - LOADOUT LOCKED";
            Refresh();
            return true;
        }

        public int BuyPreview()
        {
            if (_preview < 0 || IsUnlocked(_preview) || !Progress) { return ACO_UiSound.KIND_DENY; }
            if (IsLocked()) { return ACO_UiSound.KIND_DENY; }
            if (!Progress.IsReady()) { _message = "LOADING SAVE DATA..."; Refresh(); return ACO_UiSound.KIND_DENY; }
            if (Progress.BuySpecial(IdOf(_preview), GetPrice(_preview))) { _message = ""; Equip(_preview); return ACO_UiSound.KIND_CONFIRM; }
            _message = "NOT ENOUGH CREDITS";
            Refresh();
            return ACO_UiSound.KIND_DENY;
        }

        /// 기체의 특수무장 슬롯이 조종석에 앉을 때 읽는다. 장착한 무장이 없으면 "".
        public string GetSpecialId()
        {
            Start();
            if (SpecialIds == null || _special < 0 || _special >= SpecialIds.Length) { return ""; }
            return SpecialIds[_special];
        }

        /// 무장 ID로 해금 여부(목록에 없는 ID는 false). 기체에 고른 무장이 없을 때 대신 쓸 모듈을 고를 때 쓴다.
        public bool IsIdUnlocked(string id)
        {
            if (SpecialIds == null) { return false; }
            for (int i = 0; i < SpecialIds.Length; i++)
            {
                if (SpecialIds[i] == id) { return IsUnlocked(i); }
            }
            return false;
        }

        public bool IsUnlocked(int index)
        {
            return !Progress || Progress.IsSpecialUnlocked(IdOf(index), GetPrice(index));
        }

        /// 목록 번호의 무장 ID(해금·선택은 ID로 저장한다 — 목록 순서를 바꿔도 기록이 틀어지지 않게).
        private string IdOf(int index)
        {
            return SpecialIds != null && index >= 0 && index < SpecialIds.Length ? SpecialIds[index] : "";
        }

        private int IndexOfId(string id)
        {
            if (string.IsNullOrEmpty(id) || SpecialIds == null) { return -1; }
            for (int i = 0; i < SpecialIds.Length; i++) { if (SpecialIds[i] == id) { return i; } }
            return -1;
        }

        public int GetPrice(int index)
        {
            return SpecialPrices != null && index >= 0 && index < SpecialPrices.Length ? SpecialPrices[index] : 0;
        }

        private void Equip(int index)
        {
            int n = SpecialIds != null ? SpecialIds.Length : 0;
            if (n == 0) { return; }
            _special = Mathf.Clamp(index, 0, n - 1);
            _preview = _special;
            ShowWeapon(_special);
            if (Progress) { Progress.SaveSelectedSpecial(IdOf(_special)); }
            Refresh();
        }

        private void Refresh()
        {
            int n = SpecialIds != null ? SpecialIds.Length : 0;
            bool locked = _preview >= 0 && !IsUnlocked(_preview);
            if (SelectionText && _preview >= 0 && _preview < n)
            {
                string label = SpecialNames != null && _preview < SpecialNames.Length ? SpecialNames[_preview] : SpecialIds[_preview];
                string desc = SpecialDescriptions != null && _preview < SpecialDescriptions.Length ? SpecialDescriptions[_preview] : "";
                string head = locked ? "<color=#BBBBBB>LOCKED: " + label + "</color>" : "SPECIAL WEAPON: " + label;
                string s = head + "\n<size=60%>" + desc + "</size>";
                if (locked) { s += "\n<size=60%><color=#FFD24D>PRICE " + GetPrice(_preview).ToString("N0") + " CR</color></size>"; }
                if (_special < 0) { s += "\n<size=55%><color=#FF5544>NO SPECIAL WEAPON EQUIPPED</color></size>"; }
                if (_message != "") { s += "\n<size=55%><color=#FF5544>" + _message + "</color></size>"; }
                SelectionText.text = s;
            }
            if (BuyButton) { BuyButton.SetActive(locked); }
            if (PreviewVisuals != null)
            {
                GameObject shown = _preview >= 0 && _preview < PreviewVisuals.Length ? PreviewVisuals[_preview] : null;
                for (int i = 0; i < PreviewVisuals.Length; i++)
                {
                    GameObject g = PreviewVisuals[i];
                    if (!g) { continue; }
                    bool on = g == shown;
                    if (g.activeSelf != on) { g.SetActive(on); }
                }
            }
            if (BuyText && locked) { BuyText.text = "BUY " + GetPrice(_preview).ToString("N0") + " CR"; }
            // 크레딧이 모자라면 구매 버튼을 회색으로(그래도 누르면 안내 + 거절음).
            if (BuyButton && locked)
            {
                if (_buyImage == null) { _buyImage = BuyButton.GetComponent<Image>(); }
                bool afford = Progress == null || (Progress.IsReady() && Progress.GetCredits() >= GetPrice(_preview));
                if (_buyImage != null) { _buyImage.color = afford ? BuyColor : BuyDisabledColor; }
            }

            int pages = PagesIn(_category);
            if (_page >= pages) { _page = pages - 1; }
            if (PageText)
            {
                string cat = CategoryNames != null && _category < CategoryNames.Length ? CategoryNames[_category] : "";
                PageText.text = cat + "<size=70%>   " + (_category + 1) + " / " + CATEGORY_COUNT + (pages > 1 ? "   (" + (_page + 1) + "/" + pages + ")" : "") + "</size>";
            }
            if (ButtonImages == null) { return; }
            // 이 분류·쪽의 줄 → 목록 번호
            int size = ButtonImages.Length;
            if (_rows == null || _rows.Length != size) { _rows = new int[size]; }
            for (int k = 0; k < size; k++) { _rows[k] = -1; }
            int skip = _page * size;
            int filled = 0;
            for (int i = 0; i < n && filled < size; i++)
            {
                if (CategoryOf(i) != _category) { continue; }
                if (skip > 0) { skip--; continue; }
                _rows[filled] = i;
                filled++;
            }
            if (EmptyText && EmptyText.activeSelf != (filled == 0)) { EmptyText.SetActive(filled == 0); }
            for (int k = 0; k < size; k++)
            {
                if (!ButtonImages[k]) { continue; }
                int i = _rows[k];
                bool used = i >= 0;
                if (ButtonImages[k].gameObject.activeSelf != used) { ButtonImages[k].gameObject.SetActive(used); }
                if (!used) { continue; }
                Color c = NormalColor;
                if (i == _special) { c = SelectedColor; }
                else if (!IsUnlocked(i)) { c = i == _preview ? PreviewColor : LockedColor; }
                else if (i == _preview) { c = PreviewColor; }
                ButtonImages[k].color = c;
                if (ButtonLabels != null && k < ButtonLabels.Length && ButtonLabels[k] != null)
                {
                    string name = SpecialNames != null && i < SpecialNames.Length ? SpecialNames[i] : SpecialIds[i];
                    string state = i == _special ? "<color=#BFFFC8>EQUIPPED</color>"
                        : (IsUnlocked(i) ? "<color=#8FA89A>OWNED</color>" : "<color=#FFD24D>" + GetPrice(i).ToString("N0") + " CR</color>");
                    string label = name + "<pos=62%><size=75%>" + state + "</size>";
                    if (ButtonLabels[k].text != label) { ButtonLabels[k].text = label; }
                }
                if (ButtonIcons != null && k < ButtonIcons.Length && ButtonIcons[k] != null)
                {
                    Sprite icon = SpecialIcons != null && i < SpecialIcons.Length ? SpecialIcons[i] : null;
                    if (ButtonIcons[k].sprite != icon) { ButtonIcons[k].sprite = icon; }
                    if (ButtonIcons[k].enabled != (icon != null)) { ButtonIcons[k].enabled = icon != null; }
                }
                if (LockIcons != null && k < LockIcons.Length && LockIcons[k] != null)
                {
                    bool show = !IsUnlocked(i);
                    if (LockIcons[k].activeSelf != show) { LockIcons[k].SetActive(show); }
                }
            }
        }
    }
}
