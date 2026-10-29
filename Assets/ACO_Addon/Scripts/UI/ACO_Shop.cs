using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ACO
{
    /// 격납고 강화 상점(로컬 전용, 동기화 없음). 분류 탭(ACO_PlayerProgress.CAT_*: 기체/일반 미사일/특수 대공/특수 대지/특수 폭탄)을 고르면
    /// 그 분류의 강화 항목이 줄마다 버튼 하나로 보인다 — 누르면 다음 단계를 산다. 줄은 분류 중 가장 긴 것(6)만큼 있고 남는 줄은 숨긴다.
    /// 버튼 글자: "이름  [##---]  +20%  4,000 CR". 보유 크레딧과 지난 미션 보상도 보여 준다. 진행도 값이 바뀔 때만 다시 그린다.
    /// 패널은 격납고 프리팹(ACO_Hangar)에 같이 있다 — 프리팹 빌더가 만든다. 강화는 다음 탑승부터 적용된다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Shop : UdonSharpBehaviour
    {
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 진행도를 넣는다")]
        public ACO_PlayerProgress Progress;

        [Header("패널 표시 (프리팹 빌더가 채운다)")]
        [Tooltip("보유 크레딧과 지난 미션 보상")]
        public TextMeshProUGUI CreditsText;
        [Tooltip("분류 탭 버튼 배경(CAT_* 순서)")]
        public Image[] TabImages;
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
        [Tooltip("안내/오류 한 줄")]
        public TextMeshProUGUI MessageText;
        public Color NormalColor = new Color(0.08f, 0.3f, 0.14f, 1f);
        public Color MaxedColor = new Color(0.25f, 0.75f, 0.35f, 1f);
        public Color TooExpensiveColor = new Color(0.12f, 0.14f, 0.13f, 1f);
        public Color TabColor = new Color(0.08f, 0.3f, 0.14f, 1f);
        public Color TabSelectedColor = new Color(0.25f, 0.75f, 0.35f, 1f);

        private int _tab;
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

        /// 탭 버튼(ACO_HangarButton, Action = 탭 선택)이 부른다.
        public void SelectTab(int cat)
        {
            _tab = Mathf.Clamp(cat, 0, ACO_PlayerProgress.CATEGORY_COUNT - 1);
            SetMessage("");
            _dirty = true;
        }

        /// 줄 버튼(ACO_HangarButton, Action = 강화 구매)이 부른다. row = 지금 탭 안의 줄 번호.
        /// 효과음 종류(ACO_UiSound.KIND_*)를 돌려준다: 산 경우만 결정음, 아니면 거절음.
        public int BuyRow(int row)
        {
            if (!Progress || row < 0 || row >= Progress.GetCategoryLength(_tab)) { return ACO_UiSound.KIND_DENY; }
            int id = Progress.GetCategoryStart(_tab) + row;
            if (!Progress.IsReady()) { SetMessage("<color=#FF5544>LOADING SAVE DATA...</color>"); return ACO_UiSound.KIND_DENY; }
            int cost = Progress.GetUpgradeCost(id);
            if (cost < 0) { SetMessage("ALREADY AT MAX LEVEL"); return ACO_UiSound.KIND_DENY; }
            if (Progress.BuyUpgrade(id))
            {
                SetMessage(Progress.GetCategoryName(_tab) + " " + Progress.GetUpgradeName(id) + " LV" + Progress.GetLevel(id) + " - APPLIED ON NEXT SORTIE");
                return ACO_UiSound.KIND_CONFIRM;
            }
            SetMessage("<color=#FF5544>NOT ENOUGH CREDITS (" + cost.ToString("N0") + " CR)</color>");
            return ACO_UiSound.KIND_DENY;
        }

        private void SetMessage(string s)
        {
            if (MessageText) { MessageText.text = s; }
        }

        private void Refresh()
        {
            if (CreditsText)
            {
                string s = Progress.IsReady() ? "CREDITS " + Progress.GetCredits().ToString("N0") + " CR" : "CREDITS - (LOADING)";
                string last = Progress.GetLastRewardSummary();
                if (last != "") { s += "\n<size=55%>LAST MISSION: " + last + "</size>"; }
                CreditsText.text = s;
            }

            if (TabImages != null)
            {
                for (int c = 0; c < TabImages.Length; c++)
                {
                    if (TabImages[c]) { TabImages[c].color = c == _tab ? TabSelectedColor : TabColor; }
                }
            }

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
                    string pips = "[";
                    for (int k = 0; k < max; k++) { pips += k < lv ? "#" : "-"; }
                    pips += "]";
                    int percent = Mathf.RoundToInt(Progress.GetUpgradeStep(id) * lv * 100f);
                    RowTexts[row].text = Progress.GetUpgradeName(id) + "<pos=36%>" + pips + "<pos=58%>+" + percent + "%"
                        + "<pos=70%>" + (cost < 0 ? "MAX" : cost.ToString("N0") + " CR");
                }
                if (RowIcons != null && row < RowIcons.Length && RowIcons[row] != null)
                {
                    Sprite icon = UpgradeIcons != null && id < UpgradeIcons.Length ? UpgradeIcons[id] : null;
                    if (RowIcons[row].sprite != icon) { RowIcons[row].sprite = icon; }
                    if (RowIcons[row].enabled != (icon != null)) { RowIcons[row].enabled = icon != null; }
                }
                if (RowImages != null && row < RowImages.Length && RowImages[row])
                {
                    RowImages[row].color = cost < 0 ? MaxedColor : (credits >= cost ? NormalColor : TooExpensiveColor);
                }
            }
        }
    }
}
