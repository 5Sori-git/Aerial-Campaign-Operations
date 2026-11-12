using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace ACO
{
    /// HUD 설정 패널(로컬 전용, 동기화 없음). 내 HUD만 바꾼다 — 점수 패널 위치(좌우/상하/거리)·크기, 결과·안내 글자 묶음 위치(좌우/상하)·크기(거리는 점수 패널과 같다),
    /// HUD 글자 그림자 켜기/끄기, 값을 바꾸는 동안 비행 HUD 미리보기(기체 밖에서도 시선 앞에), HUD 색(프리셋 — 순녹색·노랑·하늘색·흰색·주황·분홍, 안개·하늘에서 잘 보이는 색을 고른다), 무전 자막 위치(좌우/상하)·크기(거리는 점수 패널과 같다), 표적 마커 테두리 크기, 시선 정보 각도, 크로스헤어 켜기/크기, RWR 켜기/링 반경/미사일 표시 크기/락온 호 크기, 크로스헤어·RWR 거리, HUD 레이더 켜기/크기/위치/불투명도/표시 거리(2~10km)/점 크기, 비행 HUD 켜기/크기/밝기, 거리 단위(m·km / NM), HUD 따라오는 지연. 값은 플레이어 영구 저장(PlayerData)에 남아 다음 접속 때도 이어진다(불러오기 전에는 저장하지 않는다).
    /// 버튼(ACO_HudSettingButton)으로 항목마다 −/+, 전체 RESET. 바꿀 때마다 HUD를 PreviewTime 동안 미션 전에도 보여 준다.
    /// 기본값 = 시작할 때 HUD 컴포넌트에 들어 있던 값(인스펙터 값). 패널은 프리팹 빌더가 만들고, Bake가 씬의 HUD를 연결한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_HudSettings : UdonSharpBehaviour
    {
        public const int ITEM_PANEL_X = 0;
        public const int ITEM_PANEL_Y = 1;
        public const int ITEM_PANEL_DISTANCE = 2;
        public const int ITEM_PANEL_SCALE = 3;
        public const int ITEM_MARKER_SIZE = 4;
        public const int ITEM_INFO_ANGLE = 5;
        public const int ITEM_CROSSHAIR = 6;
        public const int ITEM_CROSSHAIR_SIZE = 7;
        public const int ITEM_RWR = 8;
        public const int ITEM_RWR_SIZE = 9;
        public const int ITEM_RESULT_X = 10;
        public const int ITEM_RESULT_Y = 11;
        public const int ITEM_RESULT_SCALE = 12;
        public const int ITEM_CROSSHAIR_DISTANCE = 13;
        public const int ITEM_CHATTER_X = 14;
        public const int ITEM_CHATTER_Y = 15;
        public const int ITEM_CHATTER_SCALE = 16;
        public const int ITEM_RWR_MISSILE_SIZE = 17;
        public const int ITEM_RWR_LOCK_SIZE = 18;
        public const int ITEM_RADAR = 19;
        public const int ITEM_RADAR_SIZE = 20;
        public const int ITEM_RADAR_X = 21;
        public const int ITEM_RADAR_Y = 22;
        public const int ITEM_RADAR_ALPHA = 23;
        public const int ITEM_HUD_FOLLOW = 24;
        public const int ITEM_RADAR_RANGE = 25;
        public const int ITEM_RADAR_BLIP = 26;
        public const int ITEM_FLIGHT_HUD = 27;
        public const int ITEM_FLIGHT_HUD_SIZE = 28;
        public const int ITEM_FLIGHT_HUD_BRIGHT = 29;
        public const int ITEM_DISTANCE_UNIT = 30;
        public const int ITEM_DECK_CREW = 31;
        public const int ITEM_CLOUD_QUALITY = 32;
        public const int ITEM_HUD_COLOR = 33;   // HUD 색 프리셋 번호(HudColors)
        public const int ITEM_TEXT_SHADOW = 34; // HUD 글자 그림자 켜기/끄기
        public const int ITEM_LANGUAGE = 35;    // 언어(6-166): 0 AUTO(VRChat 언어), 1 한국어, 2 영어, 3 일본어 — ACO_Localization.SET_*
        public const int ITEM_COUNT = 36;

        private const string KEY_PREFIX = "aco_hud_";

        // 저장 ID(ITEM_* 순서). 키 = "aco_hud_" + ID — 항목을 더하거나 순서를 바꿔도 기존 설정이 틀어지지 않는다.
        // 규칙: 한 번 쓴 ID는 절대 바꾸거나 재사용하지 않는다. 인스펙터에 두지 않는다(실수로 바뀌지 않게).
        private string[] _keys = new string[] {
            "panel_x", "panel_y", "panel_distance", "panel_scale", "marker_size", "info_angle", "crosshair", "crosshair_size", "rwr", "rwr_size",
            "result_x", "result_y", "result_scale", "crosshair_distance", "chatter_x", "chatter_y", "chatter_scale", "rwr_missile_size", "rwr_lock_size",
            "radar", "radar_size", "radar_x", "radar_y", "radar_alpha", "hud_follow", "radar_range", "radar_blip", "flight_hud", "flight_hud_size", "flight_hud_bright", "distance_unit", "deck_crew", "cloud_quality", "hud_color", "text_shadow", "language" };

        [Header("참조 (빌드 시 ACO_SceneBaker가 넣는다)")]
        public ACO_ScoreHUD ScoreHUD;
        public ACO_TargetMarkers Markers;
        public ACO_RWR Rwr;
        public ACO_HudRadar HudRadar;
        public ACO_Chatter Chatter;
        public ACO_FlightHud FlightHud;
        public ACO_Units Units;
        [Tooltip("갑판 요원 — 내 화면에서 켜고 끈다")]
        public ACO_CatapultCrew[] Crews;
        [Tooltip("구름 — 구름 품질(체적/판)")]
        public ACO_CloudField Clouds;
        [Tooltip("번역 장치(6-166) — LANGUAGE 항목을 넘기고, 값 글자(ON/OFF 등)를 언어에 맞게 쓴다")]
        public ACO_Localization Loc;

        [Header("항목 (ITEM_* 순서: 패널 좌우, 상하, 거리, 크기, 마커 크기, 정보 각도, 크로스헤어, 크로스헤어 크기, RWR, RWR 링 반경, 결과 좌우, 결과 상하, 결과 크기, 크로스헤어·RWR 거리, 자막 좌우, 자막 상하, 자막 크기, RWR 미사일 표시 크기, RWR 락온 호 크기, 레이더, 레이더 크기, 레이더 좌우, 레이더 상하, 레이더 불투명도, HUD 따라오는 지연, 레이더 표시 거리, 레이더 점 크기, 비행 HUD, 비행 HUD 크기, 비행 HUD 밝기, 거리 단위)")]
        public string[] ItemNames = new string[] {
            "PANEL LEFT / RIGHT", "PANEL UP / DOWN", "PANEL DISTANCE", "PANEL SIZE",
            "MARKER FRAME SIZE", "INFO VIEW ANGLE", "CROSSHAIR", "CROSSHAIR SIZE", "RWR", "RWR RING SIZE",
            "RESULT LEFT / RIGHT", "RESULT UP / DOWN", "RESULT SIZE", "CROSSHAIR / RWR DISTANCE",
            "SUBTITLE LEFT / RIGHT", "SUBTITLE UP / DOWN", "SUBTITLE SIZE", "RWR MISSILE MARK SIZE", "RWR LOCK ARC SIZE",
            "RADAR", "RADAR SIZE", "RADAR LEFT / RIGHT", "RADAR UP / DOWN", "RADAR OPACITY", "HUD FOLLOW DELAY", "RADAR RANGE", "RADAR BLIP SIZE", "FLIGHT HUD", "FLIGHT HUD SIZE", "FLIGHT HUD BRIGHTNESS", "DISTANCE UNIT", "DECK CREW", "CLOUD QUALITY", "HUD COLOR", "TEXT SHADOW", "LANGUAGE" };
        [Tooltip("−/+ 한 번에 바뀌는 양(위치 m, 크기 배율, 각도 도). 크로스헤어·RWR·레이더는 켜기/끄기, 따라오는 지연은 초. 패널 거리·크로스헤어 거리는 현재 거리의 DistanceStepRatio와 이 값 중 큰 쪽")]
        public float[] Steps = new float[] { 0.02f, 0.02f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 0.2f, 1f, 0.1f, 0.02f, 0.02f, 0.1f, 0.5f, 0.02f, 0.02f, 0.1f, 0.1f, 0.1f, 1f, 0.1f, 0.02f, 0.02f, 0.1f, 0.02f, 1000f, 0.1f, 1f, 0.1f, 0.1f, 1f, 1f, 1f, 1f, 1f, 1f };
        public float[] Mins = new float[] { -0.6f, -0.4f, 0.4f, 0.5f, 1f, 1f, 0f, 0.4f, 0f, 0.5f, -0.6f, -0.4f, 0.5f, 0.5f, -0.6f, -0.5f, 0.5f, 0.5f, 0.9f, 0f, 0.5f, -0.6f, -0.4f, 0.2f, 0f, 2000f, 0.5f, 0f, 0.6f, 0.3f, 0f, 0f, 0f, 0f, 0f, 0f };
        public float[] Maxs = new float[] { 0.6f, 0.4f, 30f, 2f, 6f, 10f, 1f, 4f, 1f, 2f, 0.6f, 0.4f, 2f, 60f, 0.6f, 0.4f, 2f, 2.5f, 2f, 1f, 2.5f, 0.6f, 0.4f, 1f, 0.5f, 10000f, 2f, 1f, 1.6f, 1f, 1f, 1f, 1f, 5f, 1f, 3f };
        // 예전 프리팹(항목이 적은 배열)을 위한 코드 기본값 — 인스펙터 배열이 짧으면 이 값을 쓴다.
        private float[] _fbSteps = new float[] { 0.02f, 0.02f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 0.2f, 1f, 0.1f, 0.02f, 0.02f, 0.1f, 0.5f, 0.02f, 0.02f, 0.1f, 0.1f, 0.1f, 1f, 0.1f, 0.02f, 0.02f, 0.1f, 0.02f, 1000f, 0.1f, 1f, 0.1f, 0.1f, 1f, 1f, 1f, 1f, 1f, 1f };
        private float[] _fbMins = new float[] { -0.6f, -0.4f, 0.4f, 0.5f, 1f, 1f, 0f, 0.4f, 0f, 0.5f, -0.6f, -0.4f, 0.5f, 0.5f, -0.6f, -0.5f, 0.5f, 0.5f, 0.9f, 0f, 0.5f, -0.6f, -0.4f, 0.2f, 0f, 2000f, 0.5f, 0f, 0.6f, 0.3f, 0f, 0f, 0f, 0f, 0f, 0f };
        private float[] _fbMaxs = new float[] { 0.6f, 0.4f, 30f, 2f, 6f, 10f, 1f, 4f, 1f, 2f, 0.6f, 0.4f, 2f, 60f, 0.6f, 0.4f, 2f, 2.5f, 2f, 1f, 2.5f, 0.6f, 0.4f, 1f, 0.5f, 10000f, 2f, 1f, 1.6f, 1f, 1f, 1f, 1f, 5f, 1f, 3f };

        [Header("HUD 색 (ITEM_HUD_COLOR — 프리셋 번호, 0 = 순녹색)")]
        [Tooltip("비행 HUD·적 마커(테두리·정보 글자)·조준 고리·RWR·HUD 레이더 색. 목표 빨강·아군 파랑·체력 막대는 그대로")]
        public Color[] HudColors = new Color[] {
            new Color(0f, 1f, 0f), new Color(1f, 0.92f, 0f), new Color(0f, 0.95f, 1f), new Color(1f, 1f, 1f), new Color(1f, 0.55f, 0f), new Color(1f, 0.4f, 0.8f) };
        public string[] HudColorNames = new string[] { "GREEN", "YELLOW", "CYAN", "WHITE", "ORANGE", "PINK" };
        [Tooltip("Bake가 넣는다 — HUD 글자 재질(그림자 = TMP 밑깔림). 끄면 그림자 알파를 0으로")]
        public Material[] TextShadowMaterials;
        private float[] _shadowAlpha;
        [Tooltip("패널 거리·크로스헤어 거리 한 단계 = 현재 거리 × 이 비율(멀수록 크게 움직인다)")]
        public float DistanceStepRatio = 0.15f;
        [Tooltip("값을 바꾼 뒤 HUD를 미션 전에도 보여 주는 시간(초)")]
        public float PreviewTime = 6f;

        [Header("표시 (프리팹 빌더가 채운다)")]
        [Tooltip("항목별 값 글자(ITEM_* 순서)")]
        public TextMeshProUGUI[] ValueTexts;

        private float[] _values;
        private float[] _defaults;
        private Vector3 _basePanelScale = Vector3.one;
        private Vector3 _baseResultScale = Vector3.one;
        private Vector3 _baseChatterScale = Vector3.one;
        private float _baseRadarSize = 0.2f;
        private bool _started;
        private bool _restored;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            _defaults = new float[ITEM_COUNT];
            if (ScoreHUD)
            {
                _defaults[ITEM_PANEL_X] = ScoreHUD.HeadOffset.x;
                _defaults[ITEM_PANEL_Y] = ScoreHUD.HeadOffset.y;
                _defaults[ITEM_PANEL_DISTANCE] = ScoreHUD.HeadOffset.z;
                if (ScoreHUD.Panel) { _basePanelScale = ScoreHUD.Panel.localScale; }
                _defaults[ITEM_RESULT_X] = ScoreHUD.ResultOffset.x;
                _defaults[ITEM_RESULT_Y] = ScoreHUD.ResultOffset.y;
                if (ScoreHUD.ResultGroup) { _baseResultScale = ScoreHUD.ResultGroup.localScale; }
            }
            _defaults[ITEM_PANEL_SCALE] = 1f;
            _defaults[ITEM_RESULT_SCALE] = 1f;
            _defaults[ITEM_CHATTER_X] = Chatter != null ? Chatter.HeadOffset.x : 0f;
            _defaults[ITEM_CHATTER_Y] = Chatter != null ? Chatter.HeadOffset.y : -0.2f;
            _defaults[ITEM_CHATTER_SCALE] = 1f;
            if (Chatter != null && Chatter.Panel != null) { _baseChatterScale = Chatter.Panel.localScale; }
            _defaults[ITEM_CROSSHAIR_DISTANCE] = Markers != null ? Markers.CrosshairDistance : 30f;
            if (Markers)
            {
                _defaults[ITEM_MARKER_SIZE] = Markers.MarkerAngle;
                _defaults[ITEM_INFO_ANGLE] = Markers.LookAngle;
                _defaults[ITEM_CROSSHAIR] = Markers.ShowCrosshair ? 1f : 0f;
                _defaults[ITEM_CROSSHAIR_SIZE] = Markers.CrosshairAngle;
            }
            _defaults[ITEM_RWR] = Rwr == null || Rwr.ShowRwr ? 1f : 0f;
            _defaults[ITEM_RWR_SIZE] = Rwr != null ? Rwr.SizeScale : 1f;
            _defaults[ITEM_RWR_MISSILE_SIZE] = Rwr != null ? Rwr.ChevronScale : 1f;
            _defaults[ITEM_RWR_LOCK_SIZE] = Rwr != null ? Rwr.LockArcScale : 1f;
            _defaults[ITEM_RADAR] = HudRadar == null || HudRadar.ShowRadar ? 1f : 0f;
            _defaults[ITEM_RADAR_SIZE] = 1f;
            _defaults[ITEM_RADAR_X] = HudRadar != null ? HudRadar.HeadOffset.x : -0.32f;
            _defaults[ITEM_RADAR_Y] = HudRadar != null ? HudRadar.HeadOffset.y : -0.2f;
            _defaults[ITEM_RADAR_ALPHA] = HudRadar != null ? HudRadar.Opacity : 1f;
            _defaults[ITEM_RADAR_RANGE] = HudRadar != null ? Mathf.Min(HudRadar.Range, HudRadar.MaxRange) : 10000f;
            _defaults[ITEM_RADAR_BLIP] = HudRadar != null ? HudRadar.BlipScale : 1f;
            _defaults[ITEM_FLIGHT_HUD] = FlightHud == null || FlightHud.ShowHud ? 1f : 0f;
            _defaults[ITEM_FLIGHT_HUD_SIZE] = FlightHud != null ? FlightHud.Scale : 1f;
            _defaults[ITEM_FLIGHT_HUD_BRIGHT] = FlightHud != null ? FlightHud.Brightness : 1f;
            _defaults[ITEM_DISTANCE_UNIT] = Units != null && Units.NauticalMiles ? 1f : 0f;
            _defaults[ITEM_DECK_CREW] = 1f;
            _defaults[ITEM_CLOUD_QUALITY] = 1f;
            _defaults[ITEM_HUD_COLOR] = 0f;
            _defaults[ITEM_TEXT_SHADOW] = 1f;
            _defaults[ITEM_LANGUAGE] = 0f;   // AUTO
            // 글자 그림자 진하기(재질 값 — 끄면 0, 켜면 이 값)
            int ns = TextShadowMaterials != null ? TextShadowMaterials.Length : 0;
            _shadowAlpha = new float[ns];
            for (int i = 0; i < ns; i++)
            {
                Material m = TextShadowMaterials[i];
                float a = m != null ? m.GetColor("_UnderlayColor").a : 0.5f;
                _shadowAlpha[i] = a > 0.01f ? a : 0.5f;
            }
            // 따라오는 지연(초) = 1 / 빠르기. 기준은 점수 패널
            _defaults[ITEM_HUD_FOLLOW] = ScoreHUD != null && ScoreHUD.FollowSharpness > 0.01f ? Mathf.Round(100f / ScoreHUD.FollowSharpness) / 100f : 0.1f;
            if (HudRadar != null) { _baseRadarSize = HudRadar.Size; }
            _values = new float[ITEM_COUNT];
            for (int i = 0; i < ITEM_COUNT; i++) { _values[i] = _defaults[i]; }
            RefreshTexts();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) { return; }
            Start();
            float v;
            for (int i = 0; i < ITEM_COUNT; i++)
            {
                if (i < _keys.Length && PlayerData.TryGetFloat(player, KEY_PREFIX + _keys[i], out v)) { _values[i] = Clamp(i, v); }
            }
            _restored = true;
            Apply();
        }

        // ───────────── 버튼 (ACO_HudSettingButton) ─────────────

        /// direction −1/+1: 한 단계 줄이기/늘리기. 크로스헤어 항목은 켜기/끄기.
        /// 값이 바뀌었으면 true(끝값이라 그대로면 false — 버튼이 거절음).
        public bool Adjust(int item, int direction)
        {
            Start();
            if (item < 0 || item >= ITEM_COUNT) { return false; }
            float before = _values[item];
            if (item == ITEM_CROSSHAIR || item == ITEM_RWR || item == ITEM_RADAR || item == ITEM_FLIGHT_HUD || item == ITEM_DISTANCE_UNIT || item == ITEM_DECK_CREW || item == ITEM_CLOUD_QUALITY || item == ITEM_TEXT_SHADOW) { _values[item] = _values[item] > 0.5f ? 0f : 1f; }
            else if (item == ITEM_HUD_COLOR || item == ITEM_LANGUAGE)
            {
                // 프리셋 · 언어를 돌아가며(끝에서 처음으로)
                int n = item == ITEM_LANGUAGE ? 4 : (HudColors != null && HudColors.Length > 0 ? HudColors.Length : 1);
                int k = Mathf.RoundToInt(_values[item]) + (direction >= 0 ? 1 : -1);
                _values[item] = ((k % n) + n) % n;
            }
            else
            {
                float step = StepOf(item);
                bool distance = item == ITEM_PANEL_DISTANCE || item == ITEM_CROSSHAIR_DISTANCE;
                if (distance) { step = Mathf.Max(step, _values[item] * DistanceStepRatio); }
                float v = _values[item] + direction * step;
                if (distance) { v = Mathf.Round(v * 100f) / 100f; }
                _values[item] = Clamp(item, v);
            }
            Apply();
            Save(item);
            Preview();
            return !Mathf.Approximately(before, _values[item]);
        }

        public void ResetAll()
        {
            Start();
            for (int i = 0; i < ITEM_COUNT; i++)
            {
                _values[i] = _defaults[i];
                Save(i);
            }
            Apply();
            Preview();
        }

        // ───────────── 적용 ─────────────

        private void Apply()
        {
            if (ScoreHUD)
            {
                // 거리를 바꿔도 화면에서 보이는 크기·위치는 그대로 — 좌우/상하/크기를 기본 거리 대비 비율만큼 함께 키운다(기체 HUD처럼 멀리 둬도 작아지지 않게).
                float d = _values[ITEM_PANEL_DISTANCE];
                float k = _defaults[ITEM_PANEL_DISTANCE] > 0.01f ? d / _defaults[ITEM_PANEL_DISTANCE] : 1f;
                ScoreHUD.HeadOffset = new Vector3(_values[ITEM_PANEL_X] * k, _values[ITEM_PANEL_Y] * k, d);
                if (ScoreHUD.Panel) { ScoreHUD.Panel.localScale = _basePanelScale * (_values[ITEM_PANEL_SCALE] * k); }
                // 결과·안내 묶음: 거리는 점수 패널과 같고 좌우/상하/크기만 따로(같은 거리 비율로 키운다).
                ScoreHUD.ResultOffset = new Vector2(_values[ITEM_RESULT_X] * k, _values[ITEM_RESULT_Y] * k);
                if (ScoreHUD.ResultGroup) { ScoreHUD.ResultGroup.localScale = _baseResultScale * (_values[ITEM_RESULT_SCALE] * k); }
                // 무전 자막: 거리는 점수 패널과 같고 좌우/상하/크기만 따로
                if (Chatter)
                {
                    Chatter.HeadOffset = new Vector3(_values[ITEM_CHATTER_X] * k, _values[ITEM_CHATTER_Y] * k, d);
                    if (Chatter.Panel) { Chatter.Panel.localScale = _baseChatterScale * (_values[ITEM_CHATTER_SCALE] * k); }
                }
                // HUD 레이더: 거리는 점수 패널과 같고 좌우/상하/크기만 따로
                if (HudRadar)
                {
                    HudRadar.HeadOffset = new Vector3(_values[ITEM_RADAR_X] * k, _values[ITEM_RADAR_Y] * k, d);
                    HudRadar.Size = _baseRadarSize * (_values[ITEM_RADAR_SIZE] * k);
                }
            }
            if (Markers)
            {
                Markers.MarkerAngle = _values[ITEM_MARKER_SIZE];
                Markers.LookAngle = _values[ITEM_INFO_ANGLE];
                Markers.ShowCrosshair = _values[ITEM_CROSSHAIR] > 0.5f;
                Markers.CrosshairAngle = _values[ITEM_CROSSHAIR_SIZE];
                Markers.CrosshairDistance = _values[ITEM_CROSSHAIR_DISTANCE];   // RWR 링도 이 거리를 쓴다
            }
            if (Rwr)
            {
                Rwr.ShowRwr = _values[ITEM_RWR] > 0.5f;
                Rwr.SizeScale = _values[ITEM_RWR_SIZE];
                Rwr.ChevronScale = _values[ITEM_RWR_MISSILE_SIZE];
                Rwr.LockArcScale = _values[ITEM_RWR_LOCK_SIZE];
            }
            if (HudRadar)
            {
                HudRadar.ShowRadar = _values[ITEM_RADAR] > 0.5f;
                HudRadar.Opacity = _values[ITEM_RADAR_ALPHA];
                HudRadar.Range = Mathf.Min(_values[ITEM_RADAR_RANGE], HudRadar.MaxRange);
                HudRadar.BlipScale = _values[ITEM_RADAR_BLIP];
            }
            if (FlightHud)
            {
                FlightHud.ShowHud = _values[ITEM_FLIGHT_HUD] > 0.5f;
                FlightHud.Scale = _values[ITEM_FLIGHT_HUD_SIZE];
                FlightHud.Brightness = _values[ITEM_FLIGHT_HUD_BRIGHT];
            }
            if (Units) { Units.NauticalMiles = _values[ITEM_DISTANCE_UNIT] > 0.5f; }
            // HUD 색: 비행 HUD·적 마커·조준 고리·RWR·HUD 레이더
            Color hc = HudColor();
            if (FlightHud) { FlightHud.SetHudColor(hc); }
            if (Markers) { Markers.SetHudColor(hc); }
            if (Rwr) { Rwr.SetHudColor(hc); }
            if (HudRadar) { HudRadar.SetHudColor(hc); }
            SetTextShadow(_values[ITEM_TEXT_SHADOW] > 0.5f);
            if (Loc) { Loc.SetSetting(Mathf.RoundToInt(_values[ITEM_LANGUAGE])); }
            if (Clouds) { Clouds.SetVolumetric(_values[ITEM_CLOUD_QUALITY] > 0.5f); }
            if (Crews != null)
            {
                bool crew = _values[ITEM_DECK_CREW] > 0.5f;
                for (int i = 0; i < Crews.Length; i++) { if (Crews[i]) { Crews[i].SetShown(crew); } }
            }
            // 머리를 따라오는 HUD(점수 패널·결과 글자·무전 자막·레이더)가 늦게 따라오는 정도. 0이면 바로 붙는다.
            float delay = _values[ITEM_HUD_FOLLOW];
            float sharp = delay > 0.005f ? 1f / delay : 1000f;
            if (ScoreHUD) { ScoreHUD.FollowSharpness = sharp; }
            if (Chatter) { Chatter.FollowSharpness = sharp; }
            if (HudRadar) { HudRadar.FollowSharpness = sharp; }
            RefreshTexts();
        }

        /// HUD 글자 그림자(공용 재질의 밑깔림 알파). 재생을 마치면(OnDisable) 원래 값으로 — 에디터 재생에서 재질 에셋이 꺼진 채 남지 않게.
        private void SetTextShadow(bool on)
        {
            if (TextShadowMaterials == null || _shadowAlpha == null) { return; }
            for (int i = 0; i < TextShadowMaterials.Length && i < _shadowAlpha.Length; i++)
            {
                Material m = TextShadowMaterials[i];
                if (m == null) { continue; }
                Color c = m.GetColor("_UnderlayColor");
                c.a = on ? _shadowAlpha[i] : 0f;
                m.SetColor("_UnderlayColor", c);
            }
        }

        private void OnDisable()
        {
            SetTextShadow(true);
        }

        private Color HudColor()
        {
            int k = Mathf.RoundToInt(_values[ITEM_HUD_COLOR]);
            if (HudColors == null || HudColors.Length == 0) { return new Color(0f, 1f, 0f); }
            return HudColors[Mathf.Clamp(k, 0, HudColors.Length - 1)];
        }

        private void Preview()
        {
            if (ScoreHUD) { ScoreHUD.Preview(PreviewTime); }
            if (Markers) { Markers.Preview(PreviewTime); }
            if (Rwr) { Rwr.Preview(PreviewTime); }
            if (Chatter) { Chatter.Preview(PreviewTime); }
            if (HudRadar) { HudRadar.Preview(PreviewTime); }
            if (FlightHud) { FlightHud.Preview(PreviewTime); }   // 기체 HUD도 시선 앞에 예시 값으로
        }

        private void Save(int item)
        {
            if (_restored && item < _keys.Length) { PlayerData.SetFloat(KEY_PREFIX + _keys[item], _values[item]); }
        }

        private float StepOf(int i)
        {
            if (Steps != null && i < Steps.Length) { return Steps[i]; }
            return i < _fbSteps.Length ? _fbSteps[i] : 0.1f;
        }

        private float Clamp(int i, float v)
        {
            float lo = Mins != null && i < Mins.Length ? Mins[i] : (i < _fbMins.Length ? _fbMins[i] : -1000f);
            float hi = Maxs != null && i < Maxs.Length ? Maxs[i] : (i < _fbMaxs.Length ? _fbMaxs[i] : 1000f);
            return Mathf.Clamp(v, lo, hi);
        }

        /// 번역 장치가 언어를 바꾸면 부른다(값 글자를 다시 쓴다).
        public void LocalizationChanged()
        {
            RefreshTexts();
        }

        /// 번역 문장(장치가 없으면 영어).
        private string T(string key, string english)
        {
            return Loc != null ? Loc.Get(key) : english;
        }

        private void RefreshTexts()
        {
            if (ValueTexts == null || _values == null) { return; }
            for (int i = 0; i < ValueTexts.Length && i < ITEM_COUNT; i++)
            {
                if (ValueTexts[i]) { ValueTexts[i].text = Format(i); }
            }
        }

        /// 값 글자. 단위(cm · m · km · %)와 숫자는 그대로, 말(ON/OFF · 색 · 초 · 도)은 번역 표(set.*)에서.
        private string Format(int i)
        {
            float v = _values[i];
            if (i == ITEM_PANEL_X || i == ITEM_PANEL_Y || i == ITEM_RESULT_X || i == ITEM_RESULT_Y || i == ITEM_CHATTER_X || i == ITEM_CHATTER_Y || i == ITEM_RADAR_X || i == ITEM_RADAR_Y)
            {
                int cm = Mathf.RoundToInt(v * 100f);
                return (cm > 0 ? "+" : "") + cm + " cm";
            }
            if (i == ITEM_PANEL_DISTANCE) { return v.ToString("0.00") + " m"; }
            if (i == ITEM_CROSSHAIR_DISTANCE) { return v.ToString(v < 10f ? "0.00" : "0.0") + " m"; }
            if (i == ITEM_PANEL_SCALE || i == ITEM_RWR_SIZE || i == ITEM_RESULT_SCALE || i == ITEM_CHATTER_SCALE
                || i == ITEM_RWR_MISSILE_SIZE || i == ITEM_RWR_LOCK_SIZE || i == ITEM_RADAR_SIZE || i == ITEM_RADAR_BLIP) { return "x" + v.ToString("0.0"); }
            if (i == ITEM_RADAR_RANGE) { return (v * 0.001f).ToString("0") + " km"; }
            if (i == ITEM_CROSSHAIR || i == ITEM_RWR || i == ITEM_RADAR || i == ITEM_FLIGHT_HUD || i == ITEM_DECK_CREW || i == ITEM_TEXT_SHADOW) { return v > 0.5f ? T("set.on", "ON") : T("set.off", "OFF"); }
            if (i == ITEM_DISTANCE_UNIT) { return v > 0.5f ? "NM" : "M / KM"; }
            if (i == ITEM_CLOUD_QUALITY) { return v > 0.5f ? T("set.high", "HIGH") : T("set.low", "LOW"); }
            if (i == ITEM_HUD_COLOR)
            {
                int k = Mathf.RoundToInt(v);
                string en = HudColorNames != null && k >= 0 && k < HudColorNames.Length ? HudColorNames[k] : "COLOR " + (k + 1);
                return T("set.color." + k, en);
            }
            if (i == ITEM_LANGUAGE)
            {
                // 언어 이름은 그 언어로 적는다(지금 글자를 못 읽어도 찾을 수 있게). AUTO만 번역
                int k = Mathf.RoundToInt(v);
                if (k == 1) { return "한국어"; }
                if (k == 2) { return "ENGLISH"; }
                if (k == 3) { return "日本語"; }
                return T("set.lang.auto", "AUTO");
            }
            if (i == ITEM_FLIGHT_HUD_SIZE) { return "x" + v.ToString("0.0"); }
            if (i == ITEM_FLIGHT_HUD_BRIGHT) { return Mathf.RoundToInt(v * 100f) + "%"; }
            if (i == ITEM_RADAR_ALPHA) { return Mathf.RoundToInt(v * 100f) + "%"; }
            if (i == ITEM_HUD_FOLLOW) { return v < 0.005f ? T("set.off", "OFF") : (Loc != null ? Loc.Get1("set.unit.sec", v.ToString("0.00")) : v.ToString("0.00") + " S"); }
            return Loc != null ? Loc.Get1("set.unit.deg", v.ToString("0.0#")) : v.ToString("0.0#") + " DEG";
        }
    }
}
