using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ACO
{
    /// 격납고 강화 상점(로컬 전용, 동기화 없음). 분류 탭(ACO_PlayerProgress.CAT_*: 기체/일반 미사일/특수 대공/특수 대지/특수 폭탄)을 고르면
    /// 그 분류의 강화 항목이 줄마다 버튼 하나로 보인다. 줄은 분류 중 가장 긴 것만큼 있고 남는 줄은 숨긴다.
    /// 구매는 두 단계다(클릭 실수 방지): 줄을 누르면 고르기만 하고(줄이 강조되고 아래 구매 버튼에 다음 단계와 가격), 구매 버튼(BuyButton)을 눌러야 산다.
    /// 구매 버튼이 없는 예전 격납고 프리팹이면 줄을 누르자마자 산다.
    /// 버튼 글자: "이름  [##---]  +20%  4,000 CR". 보유 크레딧과 지난 미션 보상도 보여 준다. 진행도 값이 바뀔 때만 다시 그린다.
    /// 패널은 격납고 프리팹(ACO_Hangar)에 같이 있다 — 프리팹 빌더가 만든다. 강화는 다음 탑승부터 적용된다.
    /// 마지막 탭(TAB_SUPPLY, 화면 이름 EQUIPMENT — 예전 ITEMS)은 소모품 장비: 줄마다 "이름  효과  EQUIPPED/가격" — 고르고 구매 버튼으로 산다(종류마다 1개, 출격한 미션이 끝나면 사라진다).
    /// SUPPORT 탭(TAB_SUPPORT, 예전 OTHER 자리 — 기총은 AIRFRAME으로 옮겼다)은 지원 호출권(6-142): "이름  효과 ×장수  가격/MAX" — 산 만큼 쌓이고 남는다.
    /// 이번 출격에 가져갈 지원은 격납고 왼쪽 SUPPORT 줄에서 고른다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Shop : UdonSharpBehaviour
    {
        /// 소모품 탭 번호(강화 분류 다음).
        public const int TAB_SUPPLY = ACO_PlayerProgress.CATEGORY_COUNT;
        /// 지원 호출권 탭(강화 분류 CAT_SUPPORT 자리 — 강화 항목 대신 호출권 줄).
        public const int TAB_SUPPORT = ACO_PlayerProgress.CAT_SUPPORT;

        // 탭 설명(CAT_* 순서 + 소모품) — 영어(번역 장치가 없을 때). 번역 키는 _tabInfoKeys
        private string[] _tabInfoKeys = new string[] { "shop.tabinfo.airframe", "shop.tabinfo.missile", "shop.tabinfo.aam", "shop.tabinfo.agm", "shop.tabinfo.bomb", "shop.tabinfo.support", "shop.tabinfo.equipment" };
        private string[] _tabInfo = new string[] {
            "YOUR AIRCRAFT: HULL, HANDLING, ENGINE, FUEL, FLARES AND GUN",
            "THE STANDARD MISSILES EVERY AIRCRAFT CARRIES",
            "APPLIES TO ALL SPECIAL AIR-TO-AIR WEAPONS",
            "APPLIES TO ALL SPECIAL AIR-TO-GROUND WEAPONS",
            "APPLIES TO ALL BOMBS AND ROCKETS",
            "CALLS ARE KEPT UNTIL USED. TAKE ONE ON A SORTIE UNDER SUPPORT (LEFT)",
            "FITTED FOR ONE MISSION. USED UP WHEN THE MISSION ENDS" };

        [Header("상세 칸 (줄을 고르면 아래 칸에 설명·수치 — 6-151)")]
        [Tooltip("상세 칸 글자(프리팹 빌더가 만든다). 비우면 상세 칸 없음(예전 프리팹)")]
        public TextMeshProUGUI DetailText;
        [Tooltip("강화 설명(강화 ID 순서, 30개)")]
        public string[] UpgradeDescriptions = new string[] {
            "REINFORCED AIRFRAME. TAKES MORE HITS BEFORE GOING DOWN.",
            "SHARPER CONTROL RESPONSE FOR TIGHTER TURNS AND FASTER ROLLS.",
            "MORE THRUST FOR FASTER CLIMBS AND ACCELERATION.",
            "LOWER FUEL BURN. STAY IN THE FIGHT LONGER.",
            "EXTRA FLARE CARTRIDGES IN THE DISPENSER.",
            "HEAVIER CANNON ROUNDS.",
            "BIGGER WARHEADS ON YOUR STANDARD MISSILES.",
            "MORE STANDARD MISSILES PER SORTIE.",
            "YOUR STANDARD MISSILES PULL HARDER TO STAY ON A TURNING TARGET.",
            "THE SEEKER LOCKS ON FASTER.",
            "LOCK TARGETS FROM FARTHER OUT.",
            "YOUR STANDARD MISSILES KEEP TRACKING AT WIDER ANGLES.",
            "RAILS RELOAD FASTER BETWEEN SHOTS.",
            "BIGGER WARHEADS ON SPECIAL AIR-TO-AIR WEAPONS.",
            "MORE SPECIAL AIR-TO-AIR WEAPONS PER SORTIE.",
            "SPECIAL AIR-TO-AIR WEAPONS PULL HARDER TO STAY ON A TURNING TARGET.",
            "THE SEEKER LOCKS ON FASTER.",
            "LOCK TARGETS FROM FARTHER OUT.",
            "SPECIAL AIR-TO-AIR WEAPONS KEEP TRACKING AT WIDER ANGLES.",
            "RAILS RELOAD FASTER BETWEEN SHOTS.",
            "BIGGER WARHEADS ON SPECIAL AIR-TO-GROUND WEAPONS.",
            "MORE SPECIAL AIR-TO-GROUND WEAPONS PER SORTIE.",
            "WIDER BLAST FROM EACH HIT.",
            "THE SEEKER LOCKS ON FASTER.",
            "LOCK TARGETS FROM FARTHER OUT.",
            "RAILS RELOAD FASTER BETWEEN SHOTS.",
            "BIGGER WARHEADS ON BOMBS AND ROCKETS.",
            "MORE BOMBS AND ROCKETS PER SORTIE.",
            "WIDER BLAST FROM EACH HIT.",
            "RAILS RELOAD FASTER BETWEEN SHOTS." };
        [Tooltip("장비(소모품) 설명(CONS_* 순서) — 셋째 줄에 정확한 효과 수치가 붙는다")]
        public string[] ConsumableDescriptions = new string[] {
            "BOLT-ON ARMOR FOR ONE MISSION.",
            "AN EXTRA FLARE POD FOR ONE MISSION.",
            "EXTRA SPECIAL WEAPON LOAD FOR ONE MISSION.",
            "AN EXTERNAL TANK FOR ONE MISSION." };
        [Tooltip("호출권 설명(CALL_* 순서)")]
        public string[] CallDescriptions = new string[] {
            "A BOMBER DROPS A GUIDED BOMB ON THE SPOT YOU MARK.",
            "A DRONE FLIES TO YOU AND REFILLS FUEL, AMMO AND FLARES.",
            "A DRONE FLIES ESCORT AND SHOOTS DOWN MISSILES AIMED AT YOU." };
        [Tooltip("호출권 주의사항(CALL_* 순서 — 상세 칸 셋째 줄, 노랑). 정확한 수치는 쓰지 않는다")]
        public string[] CallCautions = new string[] {
            "THE BOMB HITS THE SPOT MARKED WHEN YOU CALL IT AND TAKES TIME TO ARRIVE. MOVING TARGETS MAY BE GONE BY THEN.",
            "FLY STRAIGHT AND LEVEL AT A STEADY SPEED UNTIL THE HOSE CONNECTS. HARD TURNS OR FLYING TOO SLOW BREAK CONTACT.",
            "IT ONLY PROTECTS YOU, NOT YOUR WINGMEN, AND LEAVES AFTER A WHILE." };
        // 설명 · 상세 칸 · 안내 문장은 번역 표(prog.* · shop.*)에서 — 위 배열은 번역 장치가 없을 때의 영어(6-166)
        [Tooltip("번역 장치(6-166). Bake가 넣는다")]
        public ACO_Localization Loc;

        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 진행도를 넣는다")]
        public ACO_PlayerProgress Progress;

        [Header("게임 진행")]
        [Tooltip("빌드 시 ACO_SceneBaker가 넣는다. 모두 READY해 출격 카운트다운이 시작되면 강화 구매를 막는다")]
        public ACO_GameFlow Flow;

        [Header("패널 표시 (프리팹 빌더가 채운다)")]
        [Tooltip("보유 크레딧(크게)")]
        public TextMeshProUGUI CreditsText;
        [Tooltip("지난 미션 보상. 비면 CreditsText 둘째 줄에 쓴다")]
        public TextMeshProUGUI LastRewardText;
        [Tooltip("분류 탭 버튼 배경(CAT_* 순서)")]
        public Image[] TabImages;
        [Tooltip("지금 탭의 설명 한 줄")]
        public TextMeshProUGUI TabInfoText;
        [Tooltip("강화 줄 버튼(남는 줄은 끈다)")]
        public GameObject[] RowObjects;
        [Tooltip("강화 줄 버튼 글자")]
        public TextMeshProUGUI[] RowTexts;
        [Tooltip("강화 줄 버튼 배경")]
        public Image[] RowImages;
        [Tooltip("강화 줄 왼쪽 아이콘(줄마다 하나, 탭에 따라 그림이 바뀐다)")]
        public Image[] RowIcons;
        [Tooltip("강화 항목별 아이콘(강화 ID 순서, ACO_PlayerProgress.UPGRADE_COUNT개). 프리팹 빌더가 Textures/Icons에서 채운다")]
        public Sprite[] UpgradeIcons;
        [Tooltip("소모품 아이콘(CONS_* 순서). 프리팹 빌더가 채운다")]
        public Sprite[] ConsumableIcons;
        [Tooltip("지원 호출권 아이콘(CALL_* 순서). Bake가 채운다")]
        public Sprite[] CallIcons;
        [Tooltip("격납고 전시 기체의 장비 외형(CONS_* 순서: 장갑판, 대응탄 포드, 탄약 증량, 연료탱크). 사 둔 장비와 EQUIPMENT 탭에서 고른 장비를 켠다(로컬 — 사기 전 미리보기)")]
        public GameObject[] ConsumablePreviewVisuals;
        [Tooltip("안내/오류 한 줄")]
        public TextMeshProUGUI MessageText;
        [Tooltip("고른 줄을 사는 버튼(ACO_HangarButton, Action = 상점 구매). 비우면 줄을 누르자마자 산다(예전 방식)")]
        public GameObject BuyButton;
        [Tooltip("구매 버튼 글자(\"BUY LV3  4,000 CR\" 등)")]
        public TextMeshProUGUI BuyText;
        public Color NormalColor = new Color(0.08f, 0.3f, 0.14f, 1f);
        public Color MaxedColor = new Color(0.25f, 0.75f, 0.35f, 1f);
        public Color TooExpensiveColor = new Color(0.12f, 0.14f, 0.13f, 1f);
        [Tooltip("고른 줄 색")]
        public Color SelectedRowColor = new Color(0.45f, 0.4f, 0.12f, 1f);
        public Color TabColor = new Color(0.08f, 0.3f, 0.14f, 1f);
        public Color TabSelectedColor = new Color(0.25f, 0.75f, 0.35f, 1f);
        [Tooltip("구매 버튼 색: 살 수 있을 때 / 못 살 때(고른 게 없음·크레딧 부족·최대 단계)")]
        public Color BuyColor = new Color(0.45f, 0.4f, 0.12f, 1f);
        public Color BuyDisabledColor = new Color(0.16f, 0.17f, 0.17f, 1f);

        private int _tab;
        private int _selectedRow = -1;   // 지금 탭에서 고른 줄(-1 = 없음)
        private Image _buyImage;
        private bool _dirty = true;
        private int _seenVersion = -1;
        private int _seenReward = -1;

        void Update()
        {
            if (!Progress) { return; }
            // 보상은 미션 관리자 쪽 상태로도 바뀌므로 지난 보상 액수도 같이 본다.
            if (!_dirty && Progress.GetVersion() == _seenVersion && Progress.GetLastReward() == _seenReward) { return; }
            _dirty = false;
            _seenVersion = Progress.GetVersion();
            _seenReward = Progress.GetLastReward();
            Refresh();
        }

        /// 번역 장치가 언어를 바꾸면 부른다. 안내 한 줄은 지난 언어라 처음 안내(고르고 구매)로 되돌린다.
        public void LocalizationChanged()
        {
            SetMessage(T("shop.hint", "SELECT AN UPGRADE, THEN PRESS BUY"));
            _dirty = true;
        }

        /// 번역 표의 문장(장치가 없거나 표에 없으면 english). {0} {1} {2}에 값을 끼운다 — 이어 붙이지 않는다(어순)
        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return s == key ? english : s;
        }
        private string T1(string key, string english, string a) { return T(key, english).Replace("{0}", a); }
        private string T2(string key, string english, string a, string b) { return T(key, english).Replace("{0}", a).Replace("{1}", b); }
        private string T3(string key, string english, string a, string b, string c) { return T(key, english).Replace("{0}", a).Replace("{1}", b).Replace("{2}", c); }
        private string Red(string s) { return "<color=#FF5544>" + s + "</color>"; }
        private string Loading() { return Red(T("common.loadingsave", "LOADING SAVE DATA...")); }

        /// 탭 버튼(ACO_HangarButton, Action = 탭 선택)이 부른다.
        public void SelectTab(int cat)
        {
            _tab = Mathf.Clamp(cat, 0, TAB_SUPPLY);
            _selectedRow = -1;
            SetMessage("");
            _dirty = true;
        }

        /// 줄 버튼(ACO_HangarButton, Action = 강화 줄)이 부른다. row = 지금 탭 안의 줄 번호. 구매 버튼이 있으면 고르기만 하고(클릭음),
        /// 없으면(예전 프리팹) 바로 산다. 효과음 종류(ACO_UiSound.KIND_*)를 돌려준다.
        public int ClickRow(int row)
        {
            if (!BuyButton) { return BuyRow(row); }
            if (!Progress || row < 0) { return ACO_UiSound.KIND_DENY; }
            int length = _tab == TAB_SUPPLY ? ACO_PlayerProgress.CONSUMABLE_COUNT : (_tab == TAB_SUPPORT ? ACO_PlayerProgress.CALL_COUNT : Progress.GetCategoryLength(_tab));
            if (row >= length) { return ACO_UiSound.KIND_DENY; }
            _selectedRow = row;
            SetMessage("");
            _dirty = true;
            return ACO_UiSound.KIND_CLICK;
        }

        /// 구매 버튼(ACO_HangarButton, Action = 상점 구매)이 부른다: 고른 줄을 산다. 고른 줄은 그대로 둔다(다음 단계를 이어 사려면 구매 버튼만 다시).
        public int BuySelected()
        {
            if (_selectedRow < 0) { SetMessage(T("shop.msg.select", "SELECT AN ITEM FIRST")); return ACO_UiSound.KIND_DENY; }
            int kind = BuyRow(_selectedRow);
            _dirty = true;
            return kind;
        }

        /// 실제 구매(고른 줄 또는 예전 방식의 줄 클릭). row = 지금 탭 안의 줄 번호.
        /// 효과음 종류(ACO_UiSound.KIND_*)를 돌려준다: 산 경우만 결정음, 아니면 거절음.
        public int BuyRow(int row)
        {
            if (_tab == TAB_SUPPLY) { return BuyConsumable(row); }
            if (_tab == TAB_SUPPORT) { return BuyCall(row); }
            if (!Progress || row < 0 || row >= Progress.GetCategoryLength(_tab)) { return ACO_UiSound.KIND_DENY; }
            int id = Progress.GetCategoryStart(_tab) + row;
            if (!Progress.IsReady()) { SetMessage(Loading()); return ACO_UiSound.KIND_DENY; }
            if (Flow != null && Flow.IsLoadoutLocked()) { SetMessage(Red(T("shop.msg.lock.upgrades", "ALL READY - UPGRADES LOCKED"))); return ACO_UiSound.KIND_DENY; }
            int cost = Progress.GetUpgradeCost(id);
            if (cost < 0) { SetMessage(T("shop.msg.maxlevel", "ALREADY AT MAX LEVEL")); return ACO_UiSound.KIND_DENY; }
            if (Progress.BuyUpgrade(id))
            {
                SetMessage(T2("shop.msg.upgraded", "{0} LV{1} - APPLIED ON NEXT SORTIE", Progress.GetUpgradeName(id), Progress.GetLevel(id).ToString()));
                return ACO_UiSound.KIND_CONFIRM;
            }
            SetMessage(Red(T1("shop.msg.nocredits", "NOT ENOUGH CREDITS ({0} CR)", cost.ToString("N0"))));
            return ACO_UiSound.KIND_DENY;
        }

        private int BuyCall(int c)
        {
            if (!Progress || c < 0 || c >= ACO_PlayerProgress.CALL_COUNT) { return ACO_UiSound.KIND_DENY; }
            if (Flow != null && Flow.IsLoadoutLocked()) { SetMessage(Red(T("shop.msg.lock.support", "ALL READY - SUPPORT LOCKED"))); return ACO_UiSound.KIND_DENY; }
            int r = Progress.BuyCall(c);
            string name = Progress.GetCallName(c);
            if (r == 1) { SetMessage(T2("shop.msg.callbought", "{0} x{1} - PICK IT UNDER SUPPORT (LEFT)", name, Progress.GetCallCount(c).ToString())); return ACO_UiSound.KIND_CONFIRM; }
            if (r == -1) { SetMessage(T2("shop.msg.callfull", "{0} FULL (MAX {1})", name, Progress.GetCallMax().ToString())); return ACO_UiSound.KIND_DENY; }
            if (r == -2) { SetMessage(Loading()); return ACO_UiSound.KIND_DENY; }
            SetMessage(Red(T1("shop.msg.nocredits", "NOT ENOUGH CREDITS ({0} CR)", Progress.GetCallPrice(c).ToString("N0"))));
            return ACO_UiSound.KIND_DENY;
        }

        private int BuyConsumable(int c)
        {
            if (!Progress || c < 0 || c >= ACO_PlayerProgress.CONSUMABLE_COUNT) { return ACO_UiSound.KIND_DENY; }
            if (Flow != null && Flow.IsLoadoutLocked()) { SetMessage(Red(T("shop.msg.lock.equipment", "ALL READY - EQUIPMENT LOCKED"))); return ACO_UiSound.KIND_DENY; }
            int r = Progress.BuyConsumable(c);
            string name = Progress.GetConsumableName(c);
            if (r == 1) { SetMessage(T1("shop.msg.equipbought", "{0} READY - USED UP WHEN YOUR NEXT MISSION ENDS", name)); return ACO_UiSound.KIND_CONFIRM; }
            if (r == -1) { SetMessage(T1("shop.msg.equiploaded", "{0} ALREADY LOADED", name)); return ACO_UiSound.KIND_DENY; }
            if (r == -2) { SetMessage(Loading()); return ACO_UiSound.KIND_DENY; }
            SetMessage(Red(T1("shop.msg.nocredits", "NOT ENOUGH CREDITS ({0} CR)", Progress.GetConsumablePrice(c).ToString("N0"))));
            return ACO_UiSound.KIND_DENY;
        }

        /// 소모품 탭: 줄마다 이름 / 효과 / READY(사 둔 것) 또는 가격.
        private void RefreshSupply()
        {
            int credits = Progress.GetCredits();
            int rows = RowObjects != null ? RowObjects.Length : 0;
            for (int row = 0; row < rows; row++)
            {
                bool used = row < ACO_PlayerProgress.CONSUMABLE_COUNT;
                if (RowObjects[row] && RowObjects[row].activeSelf != used) { RowObjects[row].SetActive(used); }
                if (!used) { continue; }
                bool owned = Progress.HasConsumable(row);
                int price = Progress.GetConsumablePrice(row);
                if (RowTexts != null && row < RowTexts.Length && RowTexts[row])
                {
                    RowTexts[row].text = Progress.GetConsumableName(row) + "<pos=70%>" + (owned ? T("shop.row.equipped", "EQUIPPED") : price.ToString("N0") + " CR");
                }
                if (RowIcons != null && row < RowIcons.Length && RowIcons[row] != null)
                {
                    Sprite icon = ConsumableIcons != null && row < ConsumableIcons.Length ? ConsumableIcons[row] : null;
                    if (RowIcons[row].sprite != icon) { RowIcons[row].sprite = icon; }
                    if (RowIcons[row].enabled != (icon != null)) { RowIcons[row].enabled = icon != null; }
                }
                if (RowImages != null && row < RowImages.Length && RowImages[row])
                {
                    RowImages[row].color = row == _selectedRow ? SelectedRowColor : (owned ? MaxedColor : (credits >= price ? NormalColor : TooExpensiveColor));
                }
            }
        }

        /// SUPPORT 탭: 호출권 줄만(나머지 줄은 숨긴다).
        private void RefreshSupport()
        {
            int credits = Progress.GetCredits();
            int rows = RowObjects != null ? RowObjects.Length : 0;
            for (int row = 0; row < rows; row++)
            {
                bool used = row < ACO_PlayerProgress.CALL_COUNT;
                if (RowObjects[row] && RowObjects[row].activeSelf != used) { RowObjects[row].SetActive(used); }
                if (used) { RefreshCallRow(row, row, credits); }
            }
        }

        /// 호출권 줄: 이름 / 효과 / ×장수 가격(최대면 MAX).
        private void RefreshCallRow(int row, int c, int credits)
        {
            int count = Progress.GetCallCount(c);
            bool full = count >= Progress.GetCallMax();
            int price = Progress.GetCallPrice(c);
            if (RowTexts != null && row < RowTexts.Length && RowTexts[row])
            {
                // 설명·수치는 상세 칸에(줄에는 이름 / ×장수 가격)
                RowTexts[row].text = Progress.GetCallName(c) + "<pos=70%><color=#FFD24D>x" + count + "</color>  " + (full ? T("shop.row.max", "MAX") : price.ToString("N0") + " CR");
            }
            if (RowIcons != null && row < RowIcons.Length && RowIcons[row] != null)
            {
                Sprite icon = CallIcons != null && c < CallIcons.Length ? CallIcons[c] : null;
                if (RowIcons[row].sprite != icon) { RowIcons[row].sprite = icon; }
                if (RowIcons[row].enabled != (icon != null)) { RowIcons[row].enabled = icon != null; }
            }
            if (RowImages != null && row < RowImages.Length && RowImages[row])
            {
                RowImages[row].color = row == _selectedRow ? SelectedRowColor : (full ? MaxedColor : (credits >= price ? NormalColor : TooExpensiveColor));
            }
        }

        /// 구매 버튼 글자·색: 고른 줄의 다음 단계와 가격(살 수 있으면 BuyColor, 아니면 회색 — 그래도 누르면 이유를 알려 준다).
        private void RefreshBuyButton()
        {
            if (!BuyButton) { return; }
            string label = T("shop.buy.select", "SELECT AN ITEM");
            bool can = false;
            int credits = Progress.GetCredits();
            if (_selectedRow >= 0 && _tab == TAB_SUPPORT)
            {
                int c = _selectedRow;
                string name = Progress.GetCallName(c);
                int price = Progress.GetCallPrice(c);
                if (Progress.GetCallCount(c) >= Progress.GetCallMax()) { label = T1("shop.buy.full", "{0} FULL", name); }
                else { label = T2("shop.buy.item", "BUY {0}   {1} CR", name, price.ToString("N0")); can = credits >= price; }
            }
            else if (_selectedRow >= 0 && _tab == TAB_SUPPLY)
            {
                string name = Progress.GetConsumableName(_selectedRow);
                int price = Progress.GetConsumablePrice(_selectedRow);
                if (Progress.HasConsumable(_selectedRow)) { label = T1("shop.buy.loaded", "{0} LOADED", name); }
                else { label = T2("shop.buy.item", "BUY {0}   {1} CR", name, price.ToString("N0")); can = credits >= price; }
            }
            else if (_selectedRow >= 0)
            {
                int id = Progress.GetCategoryStart(_tab) + _selectedRow;
                int cost = Progress.GetUpgradeCost(id);
                string name = Progress.GetUpgradeName(id);
                if (cost < 0) { label = T1("shop.buy.maxlevel", "{0} MAX LEVEL", name); }
                else { label = T3("shop.buy.upgrade", "BUY {0} LV{1}   {2} CR", name, (Progress.GetLevel(id) + 1).ToString(), cost.ToString("N0")); can = credits >= cost; }
            }
            if (BuyText && BuyText.text != label) { BuyText.text = label; }
            if (_buyImage == null) { _buyImage = BuyButton.GetComponent<Image>(); }
            if (_buyImage != null) { _buyImage.color = can ? BuyColor : BuyDisabledColor; }
        }

        private void SetMessage(string s)
        {
            if (MessageText) { MessageText.text = s; }
        }

        private void Refresh()
        {
            if (ConsumablePreviewVisuals != null)
            {
                for (int c = 0; c < ConsumablePreviewVisuals.Length; c++)
                {
                    GameObject g = ConsumablePreviewVisuals[c];
                    if (!g) { continue; }
                    // 산 장비 + EQUIPMENT 탭에서 고른 장비(사기 전 미리보기)
                    bool on = Progress.HasConsumable(c) || (_tab == TAB_SUPPLY && _selectedRow == c);
                    if (g.activeSelf != on) { g.SetActive(on); }
                }
            }
            if (CreditsText)
            {
                string s = Progress.IsReady() ? Progress.GetCredits().ToString("N0") + "<size=50%> CR</size>" : "<size=50%>" + T("common.loading", "LOADING...") + "</size>";
                if (LastRewardText)
                {
                    string last = Progress.GetLastRewardSummary();
                    LastRewardText.text = last != "" ? T1("shop.lastmission", "LAST MISSION: {0}", last) : "";
                }
                CreditsText.text = s;
            }

            if (TabImages != null)
            {
                for (int c = 0; c < TabImages.Length; c++)
                {
                    if (TabImages[c]) { TabImages[c].color = c == _tab ? TabSelectedColor : TabColor; }
                }
            }

            if (TabInfoText) { TabInfoText.text = _tab < _tabInfo.Length ? T(_tabInfoKeys[_tab], _tabInfo[_tab]) : ""; }

            if (_tab == TAB_SUPPLY) { RefreshSupply(); RefreshBuyButton(); RefreshDetail(); return; }
            if (_tab == TAB_SUPPORT) { RefreshSupport(); RefreshBuyButton(); RefreshDetail(); return; }
            int start = Progress.GetCategoryStart(_tab);
            int length = Progress.GetCategoryLength(_tab);
            int max = Progress.GetMaxLevel();
            int credits = Progress.GetCredits();
            int rows = RowObjects != null ? RowObjects.Length : 0;
            for (int row = 0; row < rows; row++)
            {
                bool used = row < length;
                if (RowObjects[row] && RowObjects[row].activeSelf != used) { RowObjects[row].SetActive(used); }
                if (!used) { continue; }

                int id = start + row;
                int lv = Progress.GetLevel(id);
                int cost = Progress.GetUpgradeCost(id);
                if (RowTexts != null && row < RowTexts.Length && RowTexts[row])
                {
                    // 기본 TMP 폰트(LiberationSans)에 있는 글자만 쓴다.
                    // 10단계라 칸이 길다 — 작게, 고정 폭으로(다음 칸을 밀지 않게)
                    string pips = "<size=78%><mspace=0.5em>[";
                    for (int k = 0; k < max; k++) { pips += k < lv ? "#" : "-"; }
                    pips += "]</mspace></size>";
                    RowTexts[row].text = Progress.GetUpgradeName(id) + "<pos=36%>" + pips + "<pos=58%>" + Progress.GetUpgradeEffectText(id)
                        + "<pos=70%>" + (cost < 0 ? T("shop.row.max", "MAX") : cost.ToString("N0") + " CR");
                }
                if (RowIcons != null && row < RowIcons.Length && RowIcons[row] != null)
                {
                    Sprite icon = UpgradeIcons != null && id < UpgradeIcons.Length ? UpgradeIcons[id] : null;
                    if (RowIcons[row].sprite != icon) { RowIcons[row].sprite = icon; }
                    if (RowIcons[row].enabled != (icon != null)) { RowIcons[row].enabled = icon != null; }
                }
                if (RowImages != null && row < RowImages.Length && RowImages[row])
                {
                    RowImages[row].color = row == _selectedRow ? SelectedRowColor : (cost < 0 ? MaxedColor : (credits >= cost ? NormalColor : TooExpensiveColor));
                }
            }
            RefreshBuyButton();
            RefreshDetail();
        }

        /// 상세 칸: 고른 줄의 이름(+단계) / 설명 / 수치. 고른 게 없으면 안내 한 줄. 문장은 틀(Detail*)에 값을 끼운다.
        private void RefreshDetail()
        {
            if (!DetailText) { return; }
            if (_selectedRow < 0) { SetDetail("<color=#8FA89A>" + T("shop.detail.empty", "SELECT AN ITEM TO SEE DETAILS") + "</color>"); return; }
            int credits = Progress.GetCredits();
            string title;
            string desc;
            string stats;
            string statColor = "#7FE39A";
            int need = 0;
            if (_tab == TAB_SUPPORT)
            {
                // 호출권: 수치 대신 주의사항(쓰는 법 — 노랑)
                int c = _selectedRow;
                title = Progress.GetCallName(c);
                string cid = Progress.GetCallSaveId(c);
                desc = T("prog.calldesc." + cid, Pick(CallDescriptions, c));
                stats = T("prog.callnote." + cid, Pick(CallCautions, c));
                statColor = "#FFD24D";
                if (Progress.GetCallCount(c) < Progress.GetCallMax()) { need = Progress.GetCallPrice(c) - credits; }
            }
            else if (_tab == TAB_SUPPLY)
            {
                int c = _selectedRow;
                title = Progress.GetConsumableName(c);
                desc = T("prog.eqdesc." + Progress.GetConsumableSaveId(c), Pick(ConsumableDescriptions, c));
                bool owned = Progress.HasConsumable(c);
                stats = Progress.GetConsumableEffect(c) + (owned ? "  |  " + T("shop.detail.equipped", "EQUIPPED FOR THE NEXT MISSION") : "");
                if (!owned) { need = Progress.GetConsumablePrice(c) - credits; }
            }
            else
            {
                int id = Progress.GetCategoryStart(_tab) + _selectedRow;
                int lv = Progress.GetLevel(id);
                int max = Progress.GetMaxLevel();
                int cost = Progress.GetUpgradeCost(id);
                title = Progress.GetUpgradeName(id) + "   <color=#FFD24D>" + T2("shop.detail.level", "LV {0}/{1}", lv.ToString(), max.ToString()) + "</color>";
                desc = T("prog.updesc." + Progress.GetUpgradeSaveId(id), Pick(UpgradeDescriptions, id));
                string now = Progress.GetUpgradeEffectAt(id, lv);
                stats = cost < 0 ? T1("shop.detail.maxed", "MAX LEVEL   {0}", now) : T2("shop.detail.nownext", "NOW {0}   >   NEXT {1}", now, Progress.GetUpgradeEffectAt(id, lv + 1));
                if (cost >= 0) { need = cost - credits; }
            }
            string s = "<b>" + title + "</b>\n<size=85%>" + desc + "</size>\n<color=" + statColor + ">" + stats + "</color>";
            if (need > 0) { s += "\n<color=#FF5544><size=85%>" + T1("shop.detail.need", "NEED {0} MORE CR", need.ToString("N0")) + "</size></color>"; }
            SetDetail(s);
        }

        private void SetDetail(string s)
        {
            if (DetailText.text != s) { DetailText.text = s; }
        }

        private string Pick(string[] a, int i) { return a != null && i >= 0 && i < a.Length ? a[i] : ""; }
    }
}
