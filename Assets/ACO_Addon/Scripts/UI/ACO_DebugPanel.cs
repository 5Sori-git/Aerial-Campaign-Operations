using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace ACO
{
    /// 디버그 패널(테스트용 — 공개 월드에 올릴 땐 AllowedPlayers에 이름을 넣거나 패널을 끈다).
    ///  - 1페이즈 강제: 페이즈 모듈(미션 관리자 Modules)마다 P1 버튼 — 다음 미션부터 첫 페이즈가 그 모듈이다(가중치 무시, 다시 누르면 해제). 가중치는 에디터 표(ACO → Missions → Phase Weights)에서 정한다.
    ///  - 출현 확률: 전투기 에이스(생성기 AceChance)·베테랑(VeteranChance), 보너스 페이즈(슈퍼에이스 등 — 관리자 BonusPhaseChance).
    ///    페이즈를 고르고 적기를 놓는 건 관리자·생성기 오너라서 값은 네트워크 이벤트로 모든 클라이언트에 넣는다(늦게 들어온 사람은 인스펙터 값 — 디버그용).
    ///  - 날씨: AUTO(씨앗으로) / 맑음 / 구름 / 안개 / 비 / 폭풍 — 날씨 ForcedWeather를 모든 클라이언트에(늦게 들어온 사람은 AUTO — 화면 연출뿐).
    ///  - 지형: 프리셋(RANDOM / 큰 섬 … 분지)을 고르고 GENERATE — 그 프리셋이 나오는 씨앗을 찾아(씨앗만 동기화되므로 늦게 들어와도 같은 지형)
    ///    게임 진행(ACO_GameFlow.DebugRequestTerrain)의 리롤과 같은 흐름으로 새 지형 + 브리핑 미리 배치. 미션 중·카운트다운·기체 탑승 중이면 막힌다.
    ///  - 소유 자금: 내 크레딧을 ± (로컬, 진행도 DebugSetCredits로 저장까지).
    /// 표시는 0.5초마다 다시 쓴다. 버튼은 ACO_DebugButton(프리팹 빌더가 만든다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_DebugPanel : UdonSharpBehaviour
    {
        public const int ACT_FIRST_PHASE = 0;
        public const int ACT_ACE = 1;
        public const int ACT_VETERAN = 2;
        public const int ACT_BONUS = 3;
        public const int ACT_CREDITS = 4;
        public const int ACT_WEATHER = 5;
        public const int ACT_TERRAIN_PICK = 6;
        public const int ACT_TERRAIN_GENERATE = 7;

        [Header("연결 (Bake가 채운다)")]
        public ACO_MissionManager Manager;
        public ACO_MissionGenerator Generator;
        public ACO_PlayerProgress Progress;
        public ACO_Weather Weather;
        public ACO_HexTerrain Terrain;
        public ACO_GameFlow Flow;

        [Header("지형 이름 (ACO_HexTerrain.PRESET_* 순서 — 패널 글꼴이 영문이라 따로 둔다)")]
        public string[] TerrainNames = new string[] { "BIG ISLAND", "ARCHIPELAGO", "TWIN ISLANDS", "ATOLL", "ISLAND CHAIN", "PLATEAU",
            "MOUNTAIN RANGE", "CANYON", "COAST", "VOLCANO", "REEF", "BASIN" };

        [Header("권한")]
        [Tooltip("누를 수 있는 플레이어 표시 이름. 비우면 누구나 — 공개 월드에 올릴 땐 이름을 넣거나 패널을 끈다")]
        public string[] AllowedPlayers;

        [Header("표시 (프리팹 빌더가 채운다)")]
        [Tooltip("모듈 줄(남는 줄은 숨긴다)")]
        public GameObject[] ModuleRows;
        public TextMeshProUGUI[] ModuleTexts;
        public TextMeshProUGUI AceText;
        public TextMeshProUGUI VeteranText;
        public TextMeshProUGUI BonusText;
        public TextMeshProUGUI CreditsText;
        public TextMeshProUGUI WeatherText;
        public TextMeshProUGUI TerrainText;
        public TextMeshProUGUI MessageText;

        private float _timer;
        private int _terrainPick = -1;   // 고른 지형 프리셋(-1 = RANDOM) — GENERATE를 누르기 전까진 이 화면에만

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = 0.5f;
            Refresh();
        }

        /// 버튼(ACO_DebugButton)이 부른다. 효과음 종류(ACO_UiSound.KIND_*)를 돌려준다.
        public int Press(int action, int index, float amount)
        {
            if (!Allowed()) { SetMessage("<color=#FF5544>NOT ALLOWED</color>"); return ACO_UiSound.KIND_DENY; }
            if (action == ACT_FIRST_PHASE)
            {
                ACO_PhaseModule m = ModuleAt(index);
                if (m == null) { return ACO_UiSound.KIND_DENY; }
                int next = Manager.DebugFirstModule == index ? -1 : index;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSetFirstModule), next);
                SetMessage(next >= 0 ? "PHASE 1 FORCED: " + m.PhaseName : "PHASE 1: RANDOM");
            }
            else if (action == ACT_ACE || action == ACT_VETERAN)
            {
                if (!Generator) { return ACO_UiSound.KIND_DENY; }
                // 표(페이즈별)를 쓰는 동안 지금 값은 AceChance·VeteranChance가 아니지만, 누르면 이 값에서 시작해 고정한다
                float now = action == ACT_ACE ? Generator.AceChance : Generator.VeteranChance;
                float v = Mathf.Clamp01(Mathf.Round((now + amount) * 100f) / 100f);
                SendCustomNetworkEvent(NetworkEventTarget.All, action == ACT_ACE ? nameof(NetSetAce) : nameof(NetSetVeteran), v);
                SetMessage((action == ACT_ACE ? "ACE" : "VETERAN") + " CHANCE " + Mathf.RoundToInt(v * 100f) + "%");
            }
            else if (action == ACT_BONUS)
            {
                if (!Manager) { return ACO_UiSound.KIND_DENY; }
                float v = Mathf.Clamp01(Mathf.Round((Manager.GetBonusChance(5) + amount) * 100f) / 100f);
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSetBonus), v);
                SetMessage("BONUS PHASE CHANCE " + Mathf.RoundToInt(v * 100f) + "%");
            }
            else if (action == ACT_WEATHER)
            {
                if (!Weather) { return ACO_UiSound.KIND_DENY; }
                // AUTO(-1) → 맑음 → 구름 → 안개 → 비 → AUTO 순환
                int n = ACO_Weather.WEATHER_COUNT + 1;
                int next = ((Weather.ForcedWeather + 1 + (amount >= 0f ? 1 : -1)) % n + n) % n - 1;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSetWeather), next);
                SetMessage("WEATHER -> " + WeatherLabel(next));
            }
            else if (action == ACT_TERRAIN_PICK)
            {
                int n = ACO_HexTerrain.PRESET_COUNT + 1;
                _terrainPick = ((_terrainPick + 1 + (amount >= 0f ? 1 : -1)) % n + n) % n - 1;
                SetMessage("TERRAIN SELECTED: " + TerrainLabel(_terrainPick) + " - PRESS GENERATE");
            }
            else if (action == ACT_TERRAIN_GENERATE)
            {
                if (!Terrain) { return ACO_UiSound.KIND_DENY; }
                int seed = FindSeed(_terrainPick);
                if (seed == 0 && _terrainPick >= 0)
                {
                    SetMessage("<color=#FF5544>" + TerrainLabel(_terrainPick) + " NEVER COMES UP (PRESET WEIGHT 0?)</color>");
                    return ACO_UiSound.KIND_DENY;
                }
                string why = Flow ? Flow.DebugRequestTerrain(seed) : "";
                if (!Flow) { Terrain.RequestGenerate(seed); }
                if (why.Length > 0) { SetMessage("<color=#FF5544>" + why + "</color>"); return ACO_UiSound.KIND_DENY; }
                SetMessage("GENERATING " + TerrainLabel(_terrainPick) + "...");
            }
            else if (action == ACT_CREDITS)
            {
                if (!Progress || !Progress.IsReady()) { SetMessage("<color=#FF5544>LOADING SAVE DATA...</color>"); return ACO_UiSound.KIND_DENY; }
                int target = Mathf.Max(Progress.GetCredits() + Mathf.RoundToInt(amount), 0);
                Progress.DebugSetCredits = target;   // 진행도가 다음 프레임에 넣고 저장한다
                SetMessage("CREDITS -> " + target.ToString("N0"));
            }
            _timer = 0.05f;   // 곧 다시 그린다(네트워크 이벤트가 돌아온 뒤)
            return ACO_UiSound.KIND_CLICK;
        }

        [NetworkCallable]
        public void NetSetFirstModule(int index)
        {
            if (Manager) { Manager.DebugFirstModule = index; }
            _timer = 0f;
        }

        /// 패널에서 고정한 값은 %, 아니면 페이즈별 표를 쓴다.
        private string ChanceText(bool fixedValue, float chance)
        {
            return fixedValue ? Mathf.RoundToInt(chance * 100f) + "%" : "BY PHASE";
        }

        [NetworkCallable]
        public void NetSetAce(float chance)
        {
            if (Generator)
            {
                Generator.AceChance = chance;
                Generator.DebugFixedChance = true;
            }
            _timer = 0f;
        }

        [NetworkCallable]
        public void NetSetVeteran(float chance)
        {
            if (Generator)
            {
                Generator.VeteranChance = chance;
                Generator.DebugFixedChance = true;
            }
            _timer = 0f;
        }

        [NetworkCallable]
        public void NetSetBonus(float chance)
        {
            if (Manager) { Manager.DebugBonusChance = chance; }
            _timer = 0f;
        }

        [NetworkCallable]
        public void NetSetWeather(int weather)
        {
            if (Weather) { Weather.ForcedWeather = Mathf.Clamp(weather, -1, ACO_Weather.WEATHER_COUNT - 1); }   // 날씨가 1초 안에 알아채고 바꾼다
            _timer = 0f;
        }

        /// 그 프리셋이 나오는 씨앗(지형과 같은 고르기 — 씨앗만 동기화). pick < 0이면 0(무작위). 못 찾으면 0.
        private int FindSeed(int pick)
        {
            if (pick < 0) { return 0; }
            for (int i = 0; i < 4000; i++)
            {
                int seed = Random.Range(1, int.MaxValue);
                if (Terrain.PresetForSeed(seed) == pick) { return seed; }
            }
            return 0;
        }

        private string WeatherLabel(int w)
        {
            if (w < 0) { return "AUTO"; }
            return Weather && Weather.WeatherNames != null && w < Weather.WeatherNames.Length ? Weather.WeatherNames[w] : "WX " + w;
        }

        private string TerrainLabel(int preset)
        {
            if (preset < 0) { return "RANDOM"; }
            return TerrainNames != null && preset < TerrainNames.Length ? TerrainNames[preset] : "PRESET " + preset;
        }

        private ACO_PhaseModule ModuleAt(int index)
        {
            if (!Manager || Manager.Modules == null || index < 0 || index >= Manager.Modules.Length) { return null; }
            return Manager.Modules[index];
        }

        private bool Allowed()
        {
            if (AllowedPlayers == null || AllowedPlayers.Length == 0) { return true; }
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) { return false; }
            string me = local.displayName;
            for (int i = 0; i < AllowedPlayers.Length; i++) { if (AllowedPlayers[i] == me) { return true; } }
            return false;
        }

        private void SetMessage(string s)
        {
            if (MessageText) { MessageText.text = s; }
        }

        /// 모듈 줄: "이름 [BONUS] [OFF]   PHASE 1"(지금 첫 페이즈로 강제한 모듈).
        private void Refresh()
        {
            int n = Manager != null && Manager.Modules != null ? Manager.Modules.Length : 0;
            int rows = ModuleRows != null ? ModuleRows.Length : 0;
            for (int r = 0; r < rows; r++)
            {
                bool used = r < n && Manager.Modules[r] != null;
                if (ModuleRows[r] && ModuleRows[r].activeSelf != used) { ModuleRows[r].SetActive(used); }
                if (!used || ModuleTexts == null || r >= ModuleTexts.Length || !ModuleTexts[r]) { continue; }
                ACO_PhaseModule m = Manager.Modules[r];
                string name = m.PhaseName + (m.BonusOnly ? " <color=#FFD24D>[BONUS]</color>" : "") + (m.Enabled ? "" : " <color=#FF5544>[OFF]</color>");
                ModuleTexts[r].text = name + (Manager.DebugFirstModule == r ? "<pos=62%><color=#FFD24D>PHASE 1</color>" : "");
            }
            if (AceText) { AceText.text = "ACE CHANCE<pos=62%>" + ChanceText(Generator != null && Generator.DebugFixedChance, Generator ? Generator.AceChance : 0f); }
            if (VeteranText) { VeteranText.text = "VETERAN CHANCE<pos=62%>" + ChanceText(Generator != null && Generator.DebugFixedChance, Generator ? Generator.VeteranChance : 0f); }
            if (BonusText) { BonusText.text = "BONUS PHASE CHANCE<pos=62%>" + ChanceText(Manager != null && Manager.DebugBonusChance >= 0f, Manager ? Manager.DebugBonusChance : 0f); }
            if (WeatherText)
            {
                string now = Weather ? Weather.GetWeatherName() : "-";
                WeatherText.text = "WEATHER<pos=40%>" + (Weather ? WeatherLabel(Weather.ForcedWeather) : "-") + "<pos=62%><size=80%>NOW " + now + "</size>";
            }
            if (TerrainText)
            {
                int built = Terrain ? Terrain.GetBuiltPreset() : -1;
                string now = Terrain ? (built < 0 ? "BUILDING..." : TerrainLabel(built)) : "-";
                TerrainText.text = "TERRAIN<pos=40%>" + TerrainLabel(_terrainPick) + "<pos=62%><size=80%>NOW " + now + "</size>";
            }
            if (CreditsText) { CreditsText.text = "MY CREDITS<pos=62%>" + (Progress && Progress.IsReady() ? Progress.GetCredits().ToString("N0") + " CR" : "(LOADING)"); }
        }
    }
}
