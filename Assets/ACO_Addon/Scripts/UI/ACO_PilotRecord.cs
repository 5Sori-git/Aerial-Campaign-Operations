using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace ACO
{
    /// 조종사 기록판(6-341, 로컬 전용 — 동기화 코드 없음): 선원실 사물함(크레딧 보드 옆)에 기댄 판. 내 기록 · 장착 중 칭호 · 칭호 목록(스크롤).
    ///  - 칭호 목록(Docs/TITLE_LIST.md)은 아래 정의에서 만든다. 칭호 ID = 저장값(aco_title)이라 바꾸지 않는다. 기준 수치 · 이름은 바꿔도 된다.
    ///  - 얻었는지는 그 분류의 누적 값이 기준 이상인지로 본다(ACO_PlayerProgress.GetStat · GetBossKills · GetBossFlags).
    ///  - 목록은 끌기 · 스크롤바 · ▲▼로 움직인다. 얻은 칭호 줄을 누르면 장착(다시 누르면 해제) — 다른 사람 화면의 로스터 · 머리 위 이름표에 보인다.
    ///  - 한 번도 클리어하지 않은 보스의 칭호는 ???(스포일러 방지). 기준이 올라 조건이 안 맞게 된 칭호는 장착이 풀린다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PilotRecord : UdonSharpBehaviour
    {
        [Header("참조 (Bake가 넣는다)")]
        public ACO_PlayerProgress Progress;
        public ACO_Localization Loc;

        [Header("표시 (프리팹 빌더가 채운다)")]
        public TextMeshProUGUI StatsText;
        public TextMeshProUGUI TitleText;
        public ScrollRect Scroll;
        public RectTransform Viewport;
        public RectTransform Content;
        [Tooltip("목록 줄(쓰지 않는 줄은 끈다)")]
        public GameObject[] Rows;
        public Image[] RowBacks;
        public TextMeshProUGUI[] RowNames;
        public TextMeshProUGUI[] RowProgress;
        public TextMeshProUGUI[] RowConditions;
        public Image[] RowIcons;
        [Tooltip("기록 값 글자(STAT_* 순서 7개 — 레벨 · 성공 · 5페이즈 · 격추 · 비행 · CIC 최고 · CIC 누적)")]
        public TextMeshProUGUI[] StatValues;
        [Tooltip("장착 중 칭호 아이콘")]
        public Image TitleIcon;

        [Header("아이콘 (프리팹 빌더가 채운다 — Textures/Icons/aco_icon_rec_*)")]
        [Tooltip("분류 아이콘: STAT_* 7개 + 보스(왕관)")]
        public Sprite[] GroupIcons;
        [Tooltip("보스 칭호 종류 아이콘: 사냥꾼 · 정복자 · 처형인 · 공략대")]
        public Sprite[] BossKindIcons;
        [Tooltip("전문 분야 칭호 아이콘(ROLE_* 순서: 전투기 · 공격기 · 폭격기 · 테스트 · 인터셉터 · 멀티롤 · 무기고)")]
        public Sprite[] RoleIcons;
        [Tooltip("전문 분야 분류 제목 아이콘")]
        public Sprite RoleHeaderIcon;
        [Tooltip("전문 분야 칭호 색(TierColors 번호 — 3 = 금)")]
        public int RoleTier = 3;

        [Header("참조 (Bake가 넣는다)")]
        [Tooltip("격납고 무장 목록(특수무장 ID · 가격) — 무기고 칭호(모두 해금)")]
        public ACO_Loadout Loadout;
        [Tooltip("등급 색: 0 = 못 얻음, 1~6단계(동 · 은 · 금 · 하늘 · 보라 · 빨강)")]
        public Color[] TierColors = new Color[] { new Color(0.37f, 0.48f, 0.4f, 1f), new Color(0.8f, 0.54f, 0.29f, 1f), new Color(0.78f, 0.82f, 0.86f, 1f),
            new Color(1f, 0.82f, 0.3f, 1f), new Color(0.5f, 0.91f, 1f, 1f), new Color(0.75f, 0.55f, 1f, 1f), new Color(1f, 0.42f, 0.35f, 1f) };
        [Tooltip("보스 칭호 종류별 등급(TierColors 번호): 사냥꾼 동 · 정복자 은 · 단독 정복자 금 · 공략대 하늘")]
        public int[] BossKindTiers = new int[] { 1, 2, 3, 4 };
        public float RefreshInterval = 1f;

        // 분류(STAT_*) — ACO_PlayerProgress.GetStat와 같은 번호
        private const int GROUP_ROLE = 7;   // 전문 분야(강화 분류 모두 최대 · 특수무장 모두 해금)
        private const int GROUP_BOSS = 8;   // 8 + 보스 번호
        private const int ROLE_FIGHTER = 0;
        private const int ROLE_ATTACK = 1;
        private const int ROLE_BOMBER = 2;
        private const int ROLE_TEST = 3;
        private const int ROLE_INTERCEPTOR = 4;
        private const int ROLE_MULTIROLE = 5;
        private const int ROLE_ARSENAL = 6;
        private string[] _roleKeys = new string[] { "fighter", "attack", "bomber", "test", "interceptor", "multirole", "arsenal" };
        private string[] _roleIconNames = new string[] { "tab_sp_aam", "tab_sp_agm", "tab_sp_bomb", "tab_airframe", "tab_missile", "rec_role_multirole", "rec_role_arsenal" };
        private const int KIND_KILL = 0;
        private const int KIND_S = 1;
        private const int KIND_SOLO = 2;
        private const int KIND_PARTY = 3;

        // 일반 칭호 정의: 분류, 기준(비행 시간은 시간 단위). 순서 = 목록 순서.
        private int[] _defGroup = new int[] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 5, 5, 5, 6, 6, 6 };
        private int[] _defThreshold = new int[] { 10, 50, 100, 200, 300, 1, 10, 50, 100, 300, 1000, 1, 10, 50, 100, 100, 500, 1000, 5000, 10000, 1, 5, 10, 50, 100, 5, 15, 30, 50, 300, 1000 };
        private string[] _groupKeys = new string[] { "level", "clears", "clears5", "kills", "flight", "cicbest", "cickills" };
        private string[] _kindKeys = new string[] { "kill", "s", "solo", "party" };

        private string[] _ids = new string[0];
        private int[] _group = new int[0];
        private int[] _thr = new int[0];      // 일반: 기준 / 보스: 종류(KIND_*)
        private int[] _tier = new int[0];     // 등급(1~) — 아이콘 색
        private int[] _rowEntry = new int[0]; // 줄 → 칭호 번호(-1 = 분류 제목)
        private bool _built;
        private float _timer;
        private int _shownVersion = -1;
        private string _shownLang = "";
        private Color _backPlain = new Color(0.06f, 0.12f, 0.08f, 1f);
        private Color _backHeader = new Color(0.02f, 0.05f, 0.04f, 1f);
        private Color _backEquipped = new Color(0.25f, 0.2f, 0.05f, 1f);

        void Start() { Build(); }

        private void Build()
        {
            if (_built) { return; }
            _built = true;
            int bosses = Progress != null ? Progress.GetBossCount() : 0;
            int n = _defGroup.Length + _roleKeys.Length + bosses * 4;
            _ids = new string[n];
            _group = new int[n];
            _thr = new int[n];
            _tier = new int[n];
            int k = 0;
            for (int i = 0; i < _defGroup.Length; i++)
            {
                int g = _defGroup[i];
                if (g == 1 && i > 0 && _defGroup[i - 1] == 0)
                {
                    // 레벨 다음에 전문 분야
                    for (int r = 0; r < _roleKeys.Length; r++)
                    {
                        _group[k] = GROUP_ROLE;
                        _thr[k] = r;
                        _ids[k] = "role_" + _roleKeys[r];
                        _tier[k] = RoleTier;
                        k++;
                    }
                }
                _group[k] = g;
                _thr[k] = _defThreshold[i];
                _ids[k] = _groupKeys[g] + "_" + _defThreshold[i] + (g == ACO_PlayerProgress.STAT_FLIGHT ? "h" : "");
                _tier[k] = k > 0 && _group[k - 1] == g ? _tier[k - 1] + 1 : 1;
                k++;
            }
            for (int b = 0; b < bosses; b++)
            {
                for (int kind = 0; kind < 4; kind++)
                {
                    _group[k] = GROUP_BOSS + b;
                    _thr[k] = kind;
                    _ids[k] = "boss_" + Progress.GetBossId(b) + "_" + _kindKeys[kind];
                    _tier[k] = BossKindTiers != null && kind < BossKindTiers.Length ? BossKindTiers[kind] : 1;
                    k++;
                }
            }
            _rowEntry = new int[Rows != null ? Rows.Length : 0];
            if (Rows != null && Rows.Length < n + 18) { Debug.LogWarning("[ACO] 조종사 기록판: 줄(" + Rows.Length + ")이 목록보다 적습니다 — 프리팹을 다시 만드세요."); }
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = RefreshInterval;
            Build();
            if (Progress == null) { return; }
            string lang = Loc != null ? Loc.Get("record.none") : "";
            int ver = Progress.GetVersion();
            if (ver == _shownVersion && lang == _shownLang) { return; }
            _shownVersion = ver;
            _shownLang = lang;
            ValidateEquipped();
            Refresh();
        }

        // ───────────── 판정 ─────────────

        private bool IsEarned(int i)
        {
            if (Progress == null || i < 0 || i >= _ids.Length) { return false; }
            int g = _group[i];
            if (g == GROUP_ROLE) { RoleProgress(_thr[i]); return _roleNeed > 0 && _roleHave >= _roleNeed; }
            if (g >= GROUP_BOSS)
            {
                int b = g - GROUP_BOSS;
                int kind = _thr[i];
                if (kind == KIND_KILL) { return Progress.GetBossKills(b) >= 1; }
                int bit = kind == KIND_S ? ACO_PlayerProgress.BOSS_FLAG_S : (kind == KIND_SOLO ? ACO_PlayerProgress.BOSS_FLAG_SOLO : ACO_PlayerProgress.BOSS_FLAG_PARTY);
                return (Progress.GetBossFlags(b) & bit) != 0;
            }
            return StatValue(g) >= Threshold(i);
        }

        private bool IsUnseenBoss(int i) { return i >= 0 && i < _group.Length && _group[i] >= GROUP_BOSS && Progress.GetBossKills(_group[i] - GROUP_BOSS) <= 0; }

        /// 전문 분야 진행(결과는 _roleHave / _roleNeed — U# 사용자 함수에 out을 쓰지 않는다): 강화 단계 합 / 최대(멀티롤은 대공 + 대지), 무기고는 해금한 특수무장 수 / 전체.
        private int _roleHave;
        private int _roleNeed;
        private void RoleProgress(int role)
        {
            int have = 0, need = 0;
            _roleHave = 0; _roleNeed = 0;
            if (Progress == null) { return; }
            if (role == ROLE_ARSENAL)
            {
                if (Loadout == null || Loadout.SpecialIds == null) { return; }
                for (int w = 0; w < Loadout.SpecialIds.Length; w++)
                {
                    string id = Loadout.SpecialIds[w];
                    if (string.IsNullOrEmpty(id)) { continue; }
                    need++;
                    int price = Loadout.SpecialPrices != null && w < Loadout.SpecialPrices.Length ? Loadout.SpecialPrices[w] : 0;
                    if (Progress.IsSpecialUnlocked(id, price)) { have++; }
                }
                _roleHave = have; _roleNeed = need;
                return;
            }
            int cat = role == ROLE_FIGHTER ? ACO_PlayerProgress.CAT_SP_AAM : (role == ROLE_ATTACK ? ACO_PlayerProgress.CAT_SP_AGM : (role == ROLE_BOMBER ? ACO_PlayerProgress.CAT_SP_BOMB
                : (role == ROLE_TEST ? ACO_PlayerProgress.CAT_AIRFRAME : (role == ROLE_INTERCEPTOR ? ACO_PlayerProgress.CAT_MISSILE : ACO_PlayerProgress.CAT_SP_AAM))));
            have = Progress.GetCategoryLevelSum(cat);
            need = Progress.GetCategoryLevelMax(cat);
            if (role == ROLE_MULTIROLE)
            {
                have += Progress.GetCategoryLevelSum(ACO_PlayerProgress.CAT_SP_AGM);
                need += Progress.GetCategoryLevelMax(ACO_PlayerProgress.CAT_SP_AGM);
            }
            _roleHave = have; _roleNeed = need;
        }

        /// 분류의 지금 값(비행 시간은 초).
        private int StatValue(int g) { return Progress != null ? Progress.GetStat(g) : 0; }

        /// 기준(비행 시간은 초로).
        private int Threshold(int i) { return _group[i] == ACO_PlayerProgress.STAT_FLIGHT ? _thr[i] * 3600 : _thr[i]; }

        private int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) { return -1; }
            for (int i = 0; i < _ids.Length; i++) { if (_ids[i] == id) { return i; } }
            return -1;
        }

        /// 장착한 칭호가 없어졌거나 조건이 안 맞으면(기준이 올랐다 등) 푼다.
        private void ValidateEquipped()
        {
            if (Progress == null || !Progress.IsRestored()) { return; }
            string id = Progress.GetTitleId();
            if (id.Length == 0) { return; }
            if (!IsEarned(IndexOf(id))) { Progress.SetTitle(""); Debug.Log("[ACO] 칭호 장착을 풀었습니다(조건이 맞지 않음): " + id); }
        }

        // ───────────── 버튼 (ACO_PilotRecordRow) ─────────────

        /// 목록 줄을 눌렀다: 얻은 칭호면 장착(이미 장착 중이면 해제). 바뀌었으면 true(아니면 거절음).
        public bool ClickRow(int row)
        {
            Build();
            if (Progress == null || !Progress.IsRestored() || row < 0 || row >= _rowEntry.Length) { return false; }
            int i = _rowEntry[row];
            if (i < 0 || IsUnseenBoss(i) || !IsEarned(i)) { return false; }
            Progress.SetTitle(Progress.GetTitleId() == _ids[i] ? "" : _ids[i]);
            _shownVersion = -1;
            _timer = 0f;
            return true;
        }

        /// ▲(-1) ▼(+1): 반 화면씩. 움직였으면 true.
        public bool ScrollStep(int dir)
        {
            if (!Scroll || !Viewport || !Content) { return false; }
            float over = Content.rect.height - Viewport.rect.height;
            if (over <= 1f) { return false; }
            float pos = Scroll.verticalNormalizedPosition;
            float next = Mathf.Clamp01(pos - dir * (Viewport.rect.height * 0.5f / over));
            if (Mathf.Approximately(next, pos)) { return false; }
            Scroll.verticalNormalizedPosition = next;
            return true;
        }

        // ───────────── 글자 ─────────────

        private void Refresh()
        {
            if (!Progress.IsRestored())
            {
                if (StatsText) { StatsText.text = T("record.loading", "LOADING..."); }
                if (TitleText) { TitleText.text = T("record.loading", "LOADING..."); }
                return;
            }
            if (StatValues != null)
            {
                for (int st = 0; st < StatValues.Length; st++)
                {
                    if (!StatValues[st]) { continue; }
                    StatValues[st].text = st == ACO_PlayerProgress.STAT_FLIGHT ? FlightText(StatValue(st)) : (st == ACO_PlayerProgress.STAT_LEVEL ? Mathf.Max(Progress.GetPlayerLevel(Networking.LocalPlayer), 0).ToString() : StatValue(st).ToString("N0"));
                }
            }
            int lv = Progress.GetPlayerLevel(Networking.LocalPlayer);
            if (StatsText)
            {
                StatsText.text = T("record.stats1", "LV {0}    CLEARS {1}    5-PHASE {2}    KILLS {3}")
                    .Replace("{0}", lv < 0 ? "-" : lv.ToString())
                    .Replace("{1}", StatValue(ACO_PlayerProgress.STAT_CLEARS).ToString("N0"))
                    .Replace("{2}", StatValue(ACO_PlayerProgress.STAT_CLEARS5).ToString("N0"))
                    .Replace("{3}", StatValue(ACO_PlayerProgress.STAT_KILLS).ToString("N0"))
                    + "\n" + T("record.stats2", "FLIGHT {0}    CIC BEST {1}    CIC TOTAL {2}")
                    .Replace("{0}", FlightText(StatValue(ACO_PlayerProgress.STAT_FLIGHT)))
                    .Replace("{1}", StatValue(ACO_PlayerProgress.STAT_CIC_BEST).ToString("N0"))
                    .Replace("{2}", StatValue(ACO_PlayerProgress.STAT_CIC_KILLS).ToString("N0"));
            }
            int equipped = IndexOf(Progress.GetTitleId());
            if (TitleText)
            {
                string name = equipped >= 0 ? "<color=#FFD15C>" + TitleName(equipped) + "</color>" : "<color=#7FBF8A>" + T("record.none", "NONE") + "</color>";
                TitleText.text = T("record.equipped", "EQUIPPED: {0}").Replace("{0}", name);
            }
            if (TitleIcon)
            {
                TitleIcon.enabled = equipped >= 0;
                if (equipped >= 0) { TitleIcon.sprite = IconOf(equipped); TitleIcon.color = TierColor(_tier[equipped]); }
            }
            if (Rows == null) { return; }

            int row = 0;
            int lastGroup = -1;
            for (int i = 0; i < _ids.Length && row < Rows.Length; i++)
            {
                int g = _group[i];
                bool unseen = IsUnseenBoss(i);
                if (g != lastGroup)
                {
                    lastGroup = g;
                    string head = g >= GROUP_BOSS ? (unseen ? "???" : T("title.boss." + Progress.GetBossId(g - GROUP_BOSS), "BOSS"))
                        : (g == GROUP_ROLE ? T("title.cat.role", "SPECIALTY") : T("title.cat." + _groupKeys[g], _groupKeys[g].ToUpper()));
                    SetRow(row++, -1, "<color=#FFD15C>" + head + "</color>", "", "", _backHeader, GroupIcon(g), unseen ? TierColor(0) : new Color(1f, 0.82f, 0.36f, 1f));
                    if (row >= Rows.Length) { break; }
                }
                bool earned = IsEarned(i);
                string color = i == equipped ? "#FFD24D" : (earned ? "#7FFF8A" : "#5E7A66");
                string name = unseen ? "???" : TitleName(i);
                string cond = unseen ? "???" : CondText(i);
                SetRow(row++, i, "<color=" + color + ">" + (i == equipped ? "* " : "") + name + "</color>",
                    "<color=" + color + ">" + ProgressText(i, earned) + "</color>", cond, i == equipped ? _backEquipped : _backPlain,
                    IconOf(i), earned && !unseen ? TierColor(_tier[i]) : TierColor(0));
            }
            for (int r = row; r < Rows.Length; r++)
            {
                if (r < _rowEntry.Length) { _rowEntry[r] = -1; }
                if (Rows[r] && Rows[r].activeSelf) { Rows[r].SetActive(false); }
            }
        }

        private void SetRow(int r, int entry, string name, string prog, string cond, Color back, Sprite icon, Color iconColor)
        {
            if (RowIcons != null && r < RowIcons.Length && RowIcons[r])
            {
                RowIcons[r].enabled = icon != null;
                RowIcons[r].sprite = icon;
                RowIcons[r].color = iconColor;
            }
            if (r < _rowEntry.Length) { _rowEntry[r] = entry; }
            if (Rows[r] && !Rows[r].activeSelf) { Rows[r].SetActive(true); }
            if (RowNames != null && r < RowNames.Length && RowNames[r]) { RowNames[r].text = name; }
            if (RowProgress != null && r < RowProgress.Length && RowProgress[r]) { RowProgress[r].text = prog; }
            if (RowConditions != null && r < RowConditions.Length && RowConditions[r]) { RowConditions[r].text = cond; }
            if (RowBacks != null && r < RowBacks.Length && RowBacks[r]) { RowBacks[r].color = back; }
        }

        private string[] _groupIconNames = new string[] { "rec_level", "rec_clears", "rec_clears5", "rec_kills", "rec_flight", "rec_cicbest", "rec_cickills" };
        private string[] _kindIconNames = new string[] { "rec_boss_kill", "rec_boss_s", "rec_boss_solo", "rec_boss_party" };

        /// 칭호 ID → 글자 안 아이콘 태그(등급 색, TMP 스프라이트 aco_rec_icons — 머리 위 이름표 · 로스터). 모르는 ID면 "".
        public string GetIconTag(string id)
        {
            Build();
            int i = IndexOf(id);
            if (i < 0) { return ""; }
            int g = _group[i];
            string icon = g >= GROUP_BOSS ? _kindIconNames[Mathf.Clamp(_thr[i], 0, _kindIconNames.Length - 1)]
                : (g == GROUP_ROLE ? _roleIconNames[Mathf.Clamp(_thr[i], 0, _roleIconNames.Length - 1)] : _groupIconNames[g]);
            return "<color=" + Hex(TierColor(_tier[i])) + "><sprite name=\"" + icon + "\" tint=1></color> ";
        }

        /// 색 → "#RRGGBB"(ColorUtility는 Udon에 없다).
        private string Hex(Color c)
        {
            return "#" + Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255).ToString("X2") + Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255).ToString("X2") + Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255).ToString("X2");
        }

        /// 칭호 아이콘: 일반 = 분류 아이콘, 보스 = 종류 아이콘.
        private Sprite IconOf(int i)
        {
            int g = _group[i];
            if (g >= GROUP_BOSS) { int kind = _thr[i]; return BossKindIcons != null && kind < BossKindIcons.Length ? BossKindIcons[kind] : null; }
            if (g == GROUP_ROLE) { int r = _thr[i]; return RoleIcons != null && r < RoleIcons.Length ? RoleIcons[r] : null; }
            return GroupIcon(g);
        }

        /// 분류 아이콘(보스는 모두 왕관).
        private Sprite GroupIcon(int g)
        {
            if (g == GROUP_ROLE) { return RoleHeaderIcon; }
            if (GroupIcons == null || GroupIcons.Length == 0) { return null; }
            int k = g >= GROUP_BOSS ? 7 : g;   // GroupIcons[7] = 보스(왕관)
            return k < GroupIcons.Length ? GroupIcons[k] : null;
        }

        private Color TierColor(int tier)
        {
            if (TierColors == null || TierColors.Length == 0) { return Color.white; }
            return TierColors[Mathf.Clamp(tier, 0, TierColors.Length - 1)];
        }

        private string TitleName(int i)
        {
            string key = "title." + _ids[i];
            return Loc != null ? Loc.Get(key) : key;
        }

        private string CondText(int i)
        {
            int g = _group[i];
            if (g >= GROUP_BOSS) { return T("title.cond.boss." + _kindKeys[_thr[i]], _kindKeys[_thr[i]].ToUpper()); }
            if (g == GROUP_ROLE) { return T("title.cond.role." + _roleKeys[_thr[i]], _roleKeys[_thr[i]].ToUpper()); }
            return T("title.cond." + _groupKeys[g], "{0}").Replace("{0}", _thr[i].ToString("N0"));
        }

        private string ProgressText(int i, bool earned)
        {
            int g = _group[i];
            if (earned) { return "OK"; }
            if (g >= GROUP_BOSS) { return "-"; }
            if (g == GROUP_ROLE) { RoleProgress(_thr[i]); return _roleHave + " / " + _roleNeed; }
            if (g == ACO_PlayerProgress.STAT_FLIGHT) { return FlightText(StatValue(g)) + " / " + _thr[i] + "h"; }
            return StatValue(g).ToString("N0") + " / " + _thr[i].ToString("N0");
        }

        /// 초 → "12h 30m".
        private string FlightText(int sec)
        {
            int m = Mathf.Max(sec, 0) / 60;
            return (m / 60) + "h " + (m % 60) + "m";
        }

        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return s == key ? english : s;
        }
    }
}
