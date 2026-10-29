using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace ACO
{
    /// 무장 모듈(ACO_DFUNC_Missile.UpgradeFamily)이 받을 강화 계열 — 인스펙터에서 드롭다운으로 고른다.
    /// 값은 ACO_PlayerProgress.FAMILY_*와 같다. None = 강화를 받지 않는다.
    public enum ACO_UpgradeFamily
    {
        Missile = 0,        // 일반 미사일 탭(피해/탄수/선회/락온 속도·거리·범위)
        SpecialAAM = 1,     // 특수 대공 탭(위와 같은 6개)
        SpecialAGM = 2,     // 특수 대지 탭(피해/탄수/폭발 반경)
        SpecialBomb = 3,    // 특수 폭탄 탭(피해/탄수/폭발 반경)
        None = 4
    }

    /// 플레이어 진행도(로컬 전용 — 동기화 없음, 씬에 하나). 재화(크레딧)·단계식 강화·특수무장 해금을 VRChat 플레이어 영구 저장(PlayerData)에
    /// 둔다 — 월드를 나갔다 와도 남는다. 각 클라이언트는 자기 값만 읽고 쓴다. 저장 값을 다 불러오기 전(OnPlayerRestored 전)에는
    /// 아무것도 쓰지 않는다(빈 값으로 덮어쓰지 않게). 그 사이에 받은 보상은 불러온 뒤 더한다.
    ///
    /// 보상: 미션이 끝나면(성공/실패) 한 번 — (팀 점수 × TeamShare + 개인 점수 × PersonalShare) × 보정.
    ///  - 개인 점수 = 내가 격추한 표적의 점수. 작전판(ACO_MissionLog)이 SAV LastHitByPlayer로 판정해 AddPersonalScore로 넘긴다.
    ///  - 보정: 격추당할 때마다 DeathPenalty(45%)씩 깎는다. 미션 실패 또는 MinimumRewardDeaths(2)번 이상 격추당하면 최소 보상 MinimumRewardFraction(10%).
    ///    안전지대(SafeZones — 항모 갑판·기지) 밖에서 조종석을 내려도 격추로 센다(추락 직전 탈출로 페널티를 피하지 못하게).
    ///  - 이번 미션에 한 번도 출격(조종석 탑승)하지 않았으면 보상 없음(RequireSortie).
    /// 출격·격추는 기체의 ACO_VehicleUpgrades가 알린다. 늦게 들어와도 끝나기 전에 출격했으면 보상을 받는다(팀 점수는 전체, 개인 점수는 들어온 뒤 것).
    ///
    /// 강화: 항목마다 0 ~ MaxLevel 단계. 효과 배수 = 1 + UpgradeSteps × 단계. 다음 탑승부터 적용된다(ACO_VehicleUpgrades가 탑승할 때 읽는다).
    /// 항목 번호 = 분류(CAT_*) 순서로 이어 붙인 것: 기체 5개(체력/기동/추력/연료 효율/안정성) → 계열(FAMILY_*)별 무장 강화.
    ///  - 대공 계열(일반 미사일, 특수 대공): 피해/탄수/선회/락온 속도/락온 거리/락온 범위 6개.
    ///  - 대지 계열(특수 대지 미사일, 특수 폭탄·로켓): 피해/탄수/폭발 반경 3개(유도 성능은 강화하지 않는다).
    /// 무장이 어느 계열인지는 무장 모듈 인스펙터의 ACO_DFUNC_Missile.UpgradeFamily(드롭다운). GetFamilyUpgradeId(계열, STAT_*)로 항목 번호를 얻는다.
    /// 특수무장 해금: 무장 ID별(aco_unlock_ + ID). 가격이 0인 무장은 처음부터 해금.
    /// 저장은 항목 번호가 아니라 고정 ID로 한다(강화: aco_up_ + _upgradeKeys) — 항목을 더하거나 순서를 바꿔도 기존 기록이 틀어지지 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PlayerProgress : UdonSharpBehaviour
    {
        // 기체
        public const int UP_HULL = 0;          // 체력
        public const int UP_MOBILITY = 1;      // 기동성(피치/요/롤)
        public const int UP_ENGINE = 2;        // 추력
        public const int UP_FUEL = 3;          // 연료 효율(소모량 ÷ 배수 — 오래 난다)
        public const int UP_STABILITY = 4;     // 안정성(양력 — 저속에서도 버틴다)

        // 무장 계열(ACO_UpgradeFamily와 같은 값). 이 밖의 값(None 등)은 강화 없음.
        public const int FAMILY_MISSILE = 0;   // 일반 미사일
        public const int FAMILY_SP_AAM = 1;    // 특수 대공 미사일(4AAM 등)
        public const int FAMILY_SP_AGM = 2;    // 특수 대지 미사일
        public const int FAMILY_SP_BOMB = 3;   // 특수 폭탄/로켓(무유도)

        // 계열 안의 능력치
        public const int STAT_DAMAGE = 0;      // 피해
        public const int STAT_AMMO = 1;        // 총 탄수
        public const int STAT_TURN = 2;        // 선회 속도(대공 계열)
        public const int STAT_LOCK_SPEED = 3;  // 락온 속도(락온 시간 ÷ 배수, 대공 계열)
        public const int STAT_LOCK_RANGE = 4;  // 락온 거리(대공 계열)
        public const int STAT_LOCK_ANGLE = 5;  // 락온 범위 각도(대공 계열)
        public const int STAT_BLAST = 6;       // 폭발 반경(대지 계열)
        public const int STAT_RELOAD = 7;      // 재장전 속도(슬롯 장전 시간 ÷ 배수, 모든 계열)

        // 분류(상점 탭) — 항목 번호는 이 순서로 이어진다
        public const int CAT_AIRFRAME = 0;     // 0~4
        public const int CAT_MISSILE = 1;      // 5~11
        public const int CAT_SP_AAM = 2;       // 12~18
        public const int CAT_SP_AGM = 3;       // 19~22
        public const int CAT_SP_BOMB = 4;      // 23~26
        public const int CATEGORY_COUNT = 5;
        public const int UPGRADE_COUNT = 27;
        public const int MAX_CATEGORY_LENGTH = 7;   // 상점 줄 수(가장 긴 탭)

        // 저장 키(PlayerData). 강화·해금은 항목 번호가 아니라 고정 ID로 저장한다 — 항목을 더하거나 순서·탭을 바꿔도 기존 기록이 틀어지지 않는다.
        private const string KEY_VERSION = "aco_ver";       // 저장 형식 버전(나중에 형식을 바꿀 때 한 번만 옮기는 데 쓴다)
        private const int DATA_VERSION = 1;
        private const string KEY_CREDITS = "aco_credits";
        private const string KEY_UPGRADE = "aco_up_";        // + 강화 ID → 산 단계(int)
        private const string KEY_UNLOCK = "aco_unlock_";     // + 무장 ID(격납고 SpecialIds) → 해금(bool)
        private const string KEY_SPECIAL = "aco_special_id"; // 마지막으로 고른 무장 ID(string)

        // 강화 저장 ID(항목 번호 순 — UpgradeNames/Steps/BaseCosts와 같은 자리). 인스펙터에 두지 않는다(실수로 바뀌지 않게).
        // 규칙: 한 번 쓴 ID는 절대 바꾸거나 다른 항목에 재사용하지 않는다. 새 항목은 아무 자리에나 넣고 새 ID를 준다.
        // 항목을 빼면 ID도 빼면 된다(저장된 값은 쓰이지 않고 남을 뿐). 효과 수치·가격·표시 이름은 언제든 바꿔도 된다.
        private string[] _upgradeKeys = new string[] {
            "hull", "mobility", "engine", "fuel", "stability",
            "msl_damage", "msl_ammo", "msl_turn", "msl_lock_speed", "msl_lock_range", "msl_lock_angle", "msl_reload",
            "aam_damage", "aam_ammo", "aam_turn", "aam_lock_speed", "aam_lock_range", "aam_lock_angle", "aam_reload",
            "agm_damage", "agm_ammo", "agm_blast", "agm_reload",
            "bomb_damage", "bomb_ammo", "bomb_blast", "bomb_reload" };

        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 관리자를 넣는다")]
        public ACO_MissionManager Manager;

        [Header("보상")]
        [Tooltip("팀 점수 중 받는 비율")]
        public float TeamShare = 0.5f;
        [Tooltip("개인 점수(내 격추 점수) 중 받는 비율")]
        public float PersonalShare = 1f;
        [Tooltip("격추당할 때마다 깎는 비율(총보상 기준)")]
        [Range(0f, 1f)] public float DeathPenalty = 0.45f;
        [Tooltip("이만큼 격추당하면 최소 보상만 받는다")]
        public int MinimumRewardDeaths = 2;
        [Tooltip("최소 보상 비율(미션 실패 또는 MinimumRewardDeaths번 이상 격추)")]
        [Range(0f, 1f)] public float MinimumRewardFraction = 0.1f;
        [Tooltip("켜면 이번 미션에 출격(조종석 탑승)한 적이 없으면 보상이 없다")]
        public bool RequireSortie = true;
        [Tooltip("미션이 끝나고 보상을 계산하기까지 기다리는 시간(초) — 작전판이 마지막 격추를 넘길 시간")]
        public float RewardDelay = 2f;
        [Tooltip("처음 들어온 플레이어의 크레딧")]
        public int StartingCredits = 0;
        [Tooltip("테스트용: 플레이 모드에서 0 이상을 넣으면 내 크레딧을 그 값으로 바꾸고(저장 포함) -1로 돌아간다. 평소엔 -1로 둔다")]
        public int DebugSetCredits = -1;

        [Header("안전지대")]
        [Tooltip("이 콜라이더들의 영역(바운딩 박스 — 축에 맞춘 상자) 안에서 내리면 격추로 치지 않는다(항모 갑판, 기지 등). "
            + "밖에서 미션 중에 내리면 격추로 센다. 비우면 어디서 내려도 페널티 없음")]
        public Collider[] SafeZones;

        [Header("강화 (항목 번호 순: 기체 5 / 일반 미사일 7 / 특수 대공 7 / 특수 대지 4 / 특수 폭탄 4)")]
        [Tooltip("상점 탭 이름(CAT_* 순서)")]
        public string[] CategoryNames = new string[] { "AIRFRAME", "MISSILE", "SP. AAM", "SP. AGM", "SP. BOMB" };
        public string[] UpgradeNames = new string[] {
            "HULL ARMOR", "MOBILITY", "ENGINE", "FUEL EFFICIENCY", "STABILITY",
            "DAMAGE", "CAPACITY", "TURN RATE", "LOCK SPEED", "LOCK RANGE", "LOCK CONE", "RELOAD SPEED",
            "DAMAGE", "CAPACITY", "TURN RATE", "LOCK SPEED", "LOCK RANGE", "LOCK CONE", "RELOAD SPEED",
            "DAMAGE", "CAPACITY", "BLAST RADIUS", "RELOAD SPEED",
            "DAMAGE", "CAPACITY", "BLAST RADIUS", "RELOAD SPEED" };
        [Tooltip("단계마다 늘어나는 배수(0.1 = 단계당 +10%). 락온 속도·재장전 속도는 시간이 그만큼 짧아진다(1 + 배수로 나눔)")]
        public float[] UpgradeSteps = new float[] {
            0.10f, 0.05f, 0.04f, 0.08f, 0.06f,
            0.08f, 0.15f, 0.06f, 0.10f, 0.06f, 0.06f, 0.08f,
            0.08f, 0.15f, 0.06f, 0.10f, 0.06f, 0.06f, 0.08f,
            0.08f, 0.15f, 0.06f, 0.08f,
            0.08f, 0.15f, 0.06f, 0.08f };
        [Tooltip("1단계 가격. n단계 가격 = 이 값 × LevelCostScale[n-1]")]
        public int[] UpgradeBaseCosts = new int[] {
            2000, 2000, 2000, 1500, 1500,
            2000, 1500, 1500, 1500, 1500, 1500, 3000,
            2500, 2000, 2000, 2000, 2000, 2000, 3500,
            2500, 2000, 2000, 3500,
            2500, 2000, 2000, 3500 };
        [Tooltip("단계별 가격 배수. 길이 = 최대 단계")]
        public float[] LevelCostScale = new float[] { 1f, 2f, 3.5f, 5.5f, 8f };

        private bool _restored;
        private int _credits;
        private int _pendingCredits;   // 불러오기 전에 받은 보상
        private int[] _levels = new int[UPGRADE_COUNT];
        private string _unlocked = "|";   // 해금한 무장 ID 캐시("|4AAM|UGB|") — 없으면 저장 값을 읽어 채운다
        private string _savedSpecial = "";
        private int _version;          // 값이 바뀔 때마다 +1(UI가 다시 그릴지 판단)

        // 이번 미션
        private bool _inMission;
        private int _missionStart;
        private bool _sortied;
        private bool _piloting;
        private int _deaths;
        private int _personalScore;
        private bool _rewarded;
        private float _rewardAt = -1f;
        private float _pollTimer;

        // 지난 보상(표시용)
        private bool _hasLastReward;
        private int _lastReward;
        private int _lastTeamPart;
        private int _lastPersonalPart;
        private float _lastFraction;
        private int _lastDeaths;
        private bool _lastSortied;
        private int _lastMission;

        // ───────────── 저장 ─────────────

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) { return; }

            if (_upgradeKeys.Length != UPGRADE_COUNT)
            {
                Debug.LogError("[ACO] 진행도: 강화 저장 ID 수(" + _upgradeKeys.Length + ")가 강화 항목 수(" + UPGRADE_COUNT + ")와 다릅니다 — 강화를 저장하지 않습니다.");
            }
            int v;
            _credits = PlayerData.TryGetInt(player, KEY_CREDITS, out v) ? v : StartingCredits;
            for (int i = 0; i < UPGRADE_COUNT; i++)
            {
                string key = UpgradeKey(i);
                _levels[i] = key != "" && PlayerData.TryGetInt(player, key, out v) ? Mathf.Clamp(v, 0, GetMaxLevel()) : 0;
            }
            string s;
            _savedSpecial = PlayerData.TryGetString(player, KEY_SPECIAL, out s) ? s : "";
            if (!PlayerData.TryGetInt(player, KEY_VERSION, out v)) { PlayerData.SetInt(KEY_VERSION, DATA_VERSION); }
            _restored = true;

            if (_pendingCredits != 0)
            {
                _credits += _pendingCredits;
                _pendingCredits = 0;
            }
            PlayerData.SetInt(KEY_CREDITS, _credits);
            _version++;
        }

        private void SaveCredits()
        {
            if (_restored) { PlayerData.SetInt(KEY_CREDITS, _credits); }
        }

        // ───────────── 조회 ─────────────

        /// 저장 값을 다 불러왔는가(그 전에는 구매할 수 없다).
        public bool IsReady() { return _restored; }
        public int GetVersion() { return _version; }
        public int GetCredits() { return _restored ? _credits : _pendingCredits; }

        public int GetMaxLevel() { return LevelCostScale != null ? LevelCostScale.Length : 0; }

        /// 강화 항목의 저장 키("" = ID 없음 — 저장하지 않는다).
        private string UpgradeKey(int id)
        {
            if (_upgradeKeys.Length != UPGRADE_COUNT || id < 0 || id >= _upgradeKeys.Length) { return ""; }
            return KEY_UPGRADE + _upgradeKeys[id];
        }
        public int GetLevel(int id) { return id >= 0 && id < UPGRADE_COUNT ? _levels[id] : 0; }
        public string GetUpgradeName(int id) { return UpgradeNames != null && id >= 0 && id < UpgradeNames.Length ? UpgradeNames[id] : ""; }
        public float GetUpgradeStep(int id) { return UpgradeSteps != null && id >= 0 && id < UpgradeSteps.Length ? UpgradeSteps[id] : 0f; }

        /// 효과 배수 = 1 + 단계당 배수 × 단계. 항목 번호가 -1이면 1.
        public float GetUpgradeFactor(int id) { return id < 0 ? 1f : 1f + GetUpgradeStep(id) * GetLevel(id); }

        /// 계열(FAMILY_*)의 능력치(STAT_*) 강화 항목 번호. 그 계열에 없는 능력치면 -1(대공 계열엔 폭발 반경, 대지 계열엔 선회·락온이 없다).
        /// 탭 안 순서: 대공 계열 = 피해·탄수·선회·락온 속도·거리·범위·재장전, 대지 계열 = 피해·탄수·폭발 반경·재장전.
        public int GetFamilyUpgradeId(int family, int stat)
        {
            if (family == FAMILY_SP_AGM || family == FAMILY_SP_BOMB)
            {
                int start = GetCategoryStart(CAT_MISSILE + family);
                if (stat == STAT_DAMAGE) { return start; }
                if (stat == STAT_AMMO) { return start + 1; }
                if (stat == STAT_BLAST) { return start + 2; }
                if (stat == STAT_RELOAD) { return start + 3; }
                return -1;
            }
            if (family < FAMILY_MISSILE || family > FAMILY_SP_AAM) { return -1; }
            int s = GetCategoryStart(CAT_MISSILE + family);
            if (stat >= STAT_DAMAGE && stat <= STAT_LOCK_ANGLE) { return s + stat; }
            if (stat == STAT_RELOAD) { return s + 6; }
            return -1;
        }

        /// 계열의 능력치 배수(그 계열에 없으면 1).
        public float GetFamilyFactor(int family, int stat) { return GetUpgradeFactor(GetFamilyUpgradeId(family, stat)); }

        /// 분류(CAT_*)의 첫 항목 번호와 항목 수(상점 탭).
        public int GetCategoryStart(int cat)
        {
            int start = 0;
            for (int c = 0; c < cat && c < CATEGORY_COUNT; c++) { start += GetCategoryLength(c); }
            return start;
        }
        public int GetCategoryLength(int cat)
        {
            if (cat == CAT_MISSILE || cat == CAT_SP_AAM) { return 7; }
            if (cat == CAT_AIRFRAME) { return 5; }
            return cat >= 0 && cat < CATEGORY_COUNT ? 4 : 0;
        }
        public int GetUpgradeCategory(int id)
        {
            for (int cat = CATEGORY_COUNT - 1; cat > 0; cat--) { if (id >= GetCategoryStart(cat)) { return cat; } }
            return CAT_AIRFRAME;
        }
        public string GetCategoryName(int cat) { return CategoryNames != null && cat >= 0 && cat < CategoryNames.Length ? CategoryNames[cat] : ""; }

        /// 다음 단계 가격. 이미 최대 단계면 -1.
        public int GetUpgradeCost(int id)
        {
            int lv = GetLevel(id);
            if (lv >= GetMaxLevel() || UpgradeBaseCosts == null || id < 0 || id >= UpgradeBaseCosts.Length) { return -1; }
            return Mathf.RoundToInt(UpgradeBaseCosts[id] * LevelCostScale[lv]);
        }

        /// 무장 ID(격납고 SpecialIds)의 특수무장이 해금됐는가. 가격이 0 이하면 처음부터 해금. 불러오기 전에는 잠김.
        public bool IsSpecialUnlocked(string id, int price)
        {
            if (price <= 0) { return true; }
            if (string.IsNullOrEmpty(id) || !_restored) { return false; }
            if (_unlocked.Contains("|" + id + "|")) { return true; }
            bool b;
            if (PlayerData.TryGetBool(Networking.LocalPlayer, KEY_UNLOCK + id, out b) && b)
            {
                _unlocked += id + "|";
                return true;
            }
            return false;
        }

        /// 마지막으로 고른 특수무장의 ID. 없으면 "".
        public string GetSavedSpecialId() { return _restored ? _savedSpecial : ""; }

        // ───────────── 구매 ─────────────

        public bool BuyUpgrade(int id)
        {
            if (!_restored || id < 0 || id >= UPGRADE_COUNT) { return false; }
            int cost = GetUpgradeCost(id);
            if (cost < 0 || _credits < cost) { return false; }
            _credits -= cost;
            _levels[id]++;
            string key = UpgradeKey(id);
            if (key != "") { PlayerData.SetInt(key, _levels[id]); }
            SaveCredits();
            _version++;
            return true;
        }

        public bool BuySpecial(string id, int price)
        {
            if (!_restored || string.IsNullOrEmpty(id)) { return false; }
            if (IsSpecialUnlocked(id, price)) { return true; }
            if (_credits < price) { return false; }
            _credits -= price;
            _unlocked += id + "|";
            PlayerData.SetBool(KEY_UNLOCK + id, true);
            SaveCredits();
            _version++;
            return true;
        }

        public void SaveSelectedSpecial(string id)
        {
            if (!_restored || id == null || id == _savedSpecial) { return; }
            _savedSpecial = id;
            PlayerData.SetString(KEY_SPECIAL, id);
        }

        // ───────────── 미션 (기체 확장·작전판이 부른다) ─────────────

        /// 조종석에 앉았다/내렸다(ACO_VehicleUpgrades).
        public void ReportPiloting(bool on)
        {
            _piloting = on;
            if (on && _inMission && !_rewarded) { _sortied = true; }
        }

        /// 조종 중인 기체가 폭발했다(격추·추락, ACO_VehicleUpgrades).
        public void ReportShotDown()
        {
            if (!_inMission || _rewarded || Manager == null || Manager.GetState() != ACO_MissionManager.STATE_RUNNING) { return; }
            _deaths++;
            _version++;
        }

        /// 조종석에서 내렸다(폭발로 내려진 경우는 빼고, ACO_VehicleUpgrades). 미션 중 안전지대 밖이면 격추로 센다.
        public void ReportPilotExit(Vector3 vehiclePosition)
        {
            if (IsInSafeZone(vehiclePosition)) { return; }
            int before = _deaths;
            ReportShotDown();
            if (_deaths != before) { Debug.Log("[ACO] 안전지대 밖에서 내림 — 격추로 셉니다(" + _deaths + "회)"); }
        }

        /// 안전지대 안인가. 안전지대를 하나도 정하지 않았으면 어디든 안전.
        public bool IsInSafeZone(Vector3 position)
        {
            if (SafeZones == null || SafeZones.Length == 0) { return true; }
            for (int i = 0; i < SafeZones.Length; i++)
            {
                if (SafeZones[i] && SafeZones[i].bounds.Contains(position)) { return true; }
            }
            return false;
        }

        /// 내가 격추한 표적의 점수(ACO_MissionLog).
        public void AddPersonalScore(int points)
        {
            if (!_inMission || _rewarded) { return; }
            _personalScore += points;
        }

        public int GetMissionDeaths() { return _inMission ? _deaths : 0; }
        public int GetMissionPersonalScore() { return _inMission ? _personalScore : 0; }

        void Update()
        {
            if (DebugSetCredits >= 0 && _restored)
            {
                _credits = DebugSetCredits;
                DebugSetCredits = -1;
                SaveCredits();
                _version++;
                Debug.Log("[ACO] 테스트: 크레딧을 " + _credits + "로 바꿨습니다");
            }

            if (_rewardAt >= 0f && Time.time >= _rewardAt)
            {
                _rewardAt = -1f;
                GrantReward();
            }

            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) { return; }
            _pollTimer = 0.5f;
            if (Manager == null) { return; }

            int state = Manager.GetState();
            if (state == ACO_MissionManager.STATE_IDLE) { return; }
            int start = Manager.GetMissionStartMs();

            if (state == ACO_MissionManager.STATE_RUNNING)
            {
                if (!_inMission || start != _missionStart)
                {
                    _inMission = true;
                    _missionStart = start;
                    _sortied = _piloting;   // 미션 시작 때 이미 타고 있었으면 출격으로 친다
                    _deaths = 0;
                    _personalScore = 0;
                    _rewarded = false;
                    _rewardAt = -1f;
                    _version++;
                }
                return;
            }

            // 끝남(성공/실패): 진행을 본 미션이면 한 번 보상한다.
            if (_inMission && start == _missionStart && !_rewarded && _rewardAt < 0f)
            {
                _rewardAt = Time.time + RewardDelay;
            }
        }

        private void GrantReward()
        {
            if (_rewarded || Manager == null) { return; }
            _rewarded = true;

            bool success = Manager.GetState() == ACO_MissionManager.STATE_COMPLETE;
            int team = Mathf.RoundToInt(Mathf.Max(Manager.GetTeamScore(), 0) * TeamShare);
            int personal = Mathf.RoundToInt(Mathf.Max(_personalScore, 0) * PersonalShare);
            float fraction = RewardFraction(success, _deaths);
            int reward = Mathf.RoundToInt((team + personal) * fraction);
            if (RequireSortie && !_sortied) { reward = 0; }

            _hasLastReward = true;
            _lastMission = _missionStart;
            _lastReward = reward;
            _lastTeamPart = team;
            _lastPersonalPart = personal;
            _lastFraction = fraction;
            _lastDeaths = _deaths;
            _lastSortied = _sortied || !RequireSortie;

            if (reward > 0)
            {
                if (_restored) { _credits += reward; SaveCredits(); }
                else { _pendingCredits += reward; }
            }
            _version++;
            Debug.Log("[ACO] 미션 보상 " + reward + " CR (팀 " + team + " + 개인 " + personal + ") x" + fraction.ToString("0.00")
                + ", 격추당함 " + _deaths + (success ? "" : ", 실패") + (_lastSortied ? "" : ", 출격 안 함"));
        }

        /// 보정 비율: 실패 또는 MinimumRewardDeaths번 이상 격추 = 최소, 아니면 1 − DeathPenalty × 격추 횟수(최소 아래로는 안 내려감).
        public float RewardFraction(bool success, int deaths)
        {
            if (!success || deaths >= Mathf.Max(MinimumRewardDeaths, 1)) { return MinimumRewardFraction; }
            return Mathf.Max(1f - DeathPenalty * deaths, MinimumRewardFraction);
        }

        // ───────────── 지난 보상(표시용) ─────────────

        /// 지금 미션 관리자가 보여 주는 미션의 보상이 나왔는가(새 미션이 시작되면 false).
        public bool HasRewardFor(int missionStartMs) { return _hasLastReward && _lastMission == missionStartMs; }
        public int GetLastReward() { return _lastReward; }

        /// 한 줄 요약: "REWARD +1,234 CR  (TEAM 800 + YOU 900) x55%  SHOT DOWN 1"
        public string GetLastRewardSummary()
        {
            if (!_hasLastReward) { return ""; }
            if (!_lastSortied) { return "REWARD 0 CR  (NO SORTIE)"; }
            string s = "REWARD +" + _lastReward.ToString("N0") + " CR  (TEAM " + _lastTeamPart.ToString("N0")
                + " + YOU " + _lastPersonalPart.ToString("N0") + ")";
            if (_lastFraction < 0.999f) { s += " x" + Mathf.RoundToInt(_lastFraction * 100f) + "%"; }
            if (_lastDeaths > 0) { s += "  SHOT DOWN " + _lastDeaths; }
            return s;
        }
    }
}
