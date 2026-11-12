using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 격납고 버튼(동기화 없음). Action에 따라:
    ///  0 = 특수무장 선택(ACO_Loadout.Select(Index)), 1 = 보고 있는 특수무장 구매(ACO_Loadout.BuyPreview),
    ///  2 = 강화 줄(ACO_Shop.ClickRow(Index) — 지금 탭의 줄 번호를 고른다, 구매 버튼이 없는 예전 프리팹이면 바로 산다),
    ///  3 = 상점 탭 선택(ACO_Shop.SelectTab(Index) — ACO_PlayerProgress.CAT_*),
    ///  4 = 특수무장 페이지 넘김(ACO_Loadout.ChangePage(Index) — −1 이전, +1 다음), 5 = 상점 구매(ACO_Shop.BuySelected — 고른 줄을 산다),
    ///  6 = 지원 고르기(ACO_Loadout.ChangeSupport(Index) — −1 이전, +1 다음: 없음 → 폭격 → 급유).
    ///  특수무장 선택의 Index는 페이지 안의 칸 번호.
    /// 격납고 프리팹(ACO_Hangar)의 UI 버튼(Button.onClick → SendCustomEvent "Click")이 부른다. 콜라이더가 있으면 Interact로도 눌린다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_HangarButton : UdonSharpBehaviour
    {
        public const int ACTION_SELECT_SPECIAL = 0;
        public const int ACTION_BUY_SPECIAL = 1;
        public const int ACTION_BUY_UPGRADE = 2;
        public const int ACTION_SHOP_TAB = 3;
        public const int ACTION_SPECIAL_PAGE = 4;
        public const int ACTION_SHOP_BUY = 5;
        public const int ACTION_SUPPORT = 6;

        [Tooltip("같은 프리팹의 격납고. 비우면 부모에서 찾는다")]
        public ACO_Loadout Loadout;
        [Tooltip("같은 프리팹의 강화 상점. 비우면 부모에서 찾는다")]
        public ACO_Shop Shop;
        [Tooltip("0 = 특수무장 선택, 1 = 특수무장 구매, 2 = 강화 줄 고르기(줄 번호), 3 = 상점 탭 선택, 4 = 특수무장 페이지 넘김, 5 = 상점 구매(고른 줄), 6 = 지원 고르기(−1/+1)")]
        public int Action = ACTION_SELECT_SPECIAL;
        [Tooltip("특수무장 칸 번호(페이지 안), 상점 줄 번호, 탭 번호(ACO_PlayerProgress.CAT_*), 또는 페이지 방향(−1/+1)")]
        public int Index;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다(구매 = 결정음, 크레딧 부족 = 거절음, 그 밖 = 클릭)")]
        public ACO_UiSound Sound;

        public void Click()
        {
            int kind = ACO_UiSound.KIND_CLICK;
            if (Action == ACTION_BUY_UPGRADE || Action == ACTION_SHOP_TAB || Action == ACTION_SHOP_BUY)
            {
                if (!Shop) { Shop = GetComponentInParent<ACO_Shop>(); }
                if (!Shop) { return; }
                if (Action == ACTION_SHOP_TAB) { Shop.SelectTab(Index); }
                else if (Action == ACTION_SHOP_BUY) { kind = Shop.BuySelected(); }
                else { kind = Shop.ClickRow(Index); }
            }
            else
            {
                if (!Loadout) { Loadout = GetComponentInParent<ACO_Loadout>(); }
                if (!Loadout) { return; }
                if (Action == ACTION_BUY_SPECIAL) { kind = Loadout.BuyPreview(); }
                else if (Action == ACTION_SPECIAL_PAGE) { kind = Loadout.ChangePage(Index); }
                else if (Action == ACTION_SUPPORT) { kind = Loadout.ChangeSupport(Index); }
                else { kind = Loadout.Select(Index); }
            }
            if (Sound) { Sound.PlayAt(kind, transform.position); }
        }

        public override void Interact() { Click(); }
    }
}
