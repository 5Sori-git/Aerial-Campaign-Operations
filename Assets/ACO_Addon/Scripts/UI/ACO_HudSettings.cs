using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace ACO
{
    /// HUD 설정 패널(로컬 전용, 동기화 없음). 내 HUD만 바꾼다 — 점수 패널 위치(좌우/상하/거리)·크기, 결과·안내 글자 묶음 위치(좌우/상하)·크기(거리는 점수 패널과 같다),
    /// 표적 마커 테두리 크기, 시선 정보 각도, 크로스헤어 켜기/크기, RWR 켜기/크기, 크로스헤어·RWR 거리. 값은 플레이어 영구 저장(PlayerData)에 남아 다음 접속 때도 이어진다(불러오기 전에는 저장하지 않는다).
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
        public const int ITEM_COUNT = 14;

        private const string KEY_PREFIX = "aco_hud_";

        // 저장 ID(ITEM_* 순서). 키 = "aco_hud_" + ID — 항목을 더하거나 순서를 바꿔도 기존 설정이 틀어지지 않는다.
        // 규칙: 한 번 쓴 ID는 절대 바꾸거나 재사용하지 않는다. 인스펙터에 두지 않는다(실수로 바뀌지 않게).
        private string[] _keys = new string[] {
            "panel_x", "panel_y", "panel_distance", "panel_scale", "marker_size", "info_angle", "crosshair", "crosshair_size", "rwr", "rwr_size",
            "result_x", "result_y", "result_scale", "crosshair_distance" };

        [Header("참조 (빌드 시 ACO_SceneBaker가 넣는다)")]
        public ACO_ScoreHUD ScoreHUD;
        public ACO_TargetMarkers Markers;
        public ACO_RWR Rwr;

        [Header("항목 (ITEM_* 순서: 패널 좌우, 상하, 거리, 크기, 마커 크기, 정보 각도, 크로스헤어, 크로스헤어 크기, RWR, RWR 크기, 결과 좌우, 결과 상하, 결과 크기, 크로스헤어·RWR 거리)")]
        public string[] ItemNames = new string[] {
            "PANEL LEFT / RIGHT", "PANEL UP / DOWN", "PANEL DISTANCE", "PANEL SIZE",
            "MARKER FRAME SIZE", "INFO VIEW ANGLE", "CROSSHAIR", "CROSSHAIR SIZE", "RWR", "RWR SIZE",
            "RESULT LEFT / RIGHT", "RESULT UP / DOWN", "RESULT SIZE", "CROSSHAIR / RWR DISTANCE" };
        [Tooltip("−/+ 한 번에 바뀌는 양(위치 m, 크기 배율, 각도 도). 크로스헤어·RWR은 켜기/끄기. 패널 거리·크로스헤어 거리는 현재 거리의 DistanceStepRatio와 이 값 중 큰 쪽")]
        public float[] Steps = new float[] { 0.02f, 0.02f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 0.2f, 1f, 0.1f, 0.02f, 0.02f, 0.1f, 0.5f };
        public float[] Mins = new float[] { -0.6f, -0.4f, 0.4f, 0.5f, 1f, 1f, 0f, 0.4f, 0f, 0.5f, -0.6f, -0.4f, 0.5f, 0.5f };
        public float[] Maxs = new float[] { 0.6f, 0.4f, 30f, 2f, 6f, 10f, 1f, 4f, 1f, 2f, 0.6f, 0.4f, 2f, 60f };
        // 예전 프리팹(항목이 적은 배열)을 위한 코드 기본값 — 인스펙터 배열이 짧으면 이 값을 쓴다.
        private float[] _fbSteps = new float[] { 0.02f, 0.02f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 0.2f, 1f, 0.1f, 0.02f, 0.02f, 0.1f, 0.5f };
        private float[] _fbMins = new float[] { -0.6f, -0.4f, 0.4f, 0.5f, 1f, 1f, 0f, 0.4f, 0f, 0.5f, -0.6f, -0.4f, 0.5f, 0.5f };
        private float[] _fbMaxs = new float[] { 0.6f, 0.4f, 30f, 2f, 6f, 10f, 1f, 4f, 1f, 2f, 0.6f, 0.4f, 2f, 60f };
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
            if (item == ITEM_CROSSHAIR || item == ITEM_RWR) { _values[item] = _values[item] > 0.5f ? 0f : 1f; }
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
            }
            RefreshTexts();
        }

        private void Preview()
        {
            if (ScoreHUD) { ScoreHUD.Preview(PreviewTime); }
            if (Markers) { Markers.Preview(PreviewTime); }
            if (Rwr) { Rwr.Preview(PreviewTime); }
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

        private void RefreshTexts()
        {
            if (ValueTexts == null || _values == null) { return; }
            for (int i = 0; i < ValueTexts.Length && i < ITEM_COUNT; i++)
            {
                if (ValueTexts[i]) { ValueTexts[i].text = Format(i); }
            }
        }

        /// 기본 TMP 폰트(LiberationSans)에 있는 글자만 쓴다(도 기호 대신 DEG).
        private string Format(int i)
        {
            float v = _values[i];
            if (i == ITEM_PANEL_X || i == ITEM_PANEL_Y || i == ITEM_RESULT_X || i == ITEM_RESULT_Y)
            {
                int cm = Mathf.RoundToInt(v * 100f);
                return (cm > 0 ? "+" : "") + cm + " cm";
            }
            if (i == ITEM_PANEL_DISTANCE) { return v.ToString("0.00") + " m"; }
            if (i == ITEM_CROSSHAIR_DISTANCE) { return v.ToString(v < 10f ? "0.00" : "0.0") + " m"; }
            if (i == ITEM_PANEL_SCALE || i == ITEM_RWR_SIZE || i == ITEM_RESULT_SCALE) { return "x" + v.ToString("0.0"); }
            if (i == ITEM_CROSSHAIR || i == ITEM_RWR) { return v > 0.5f ? "ON" : "OFF"; }
            return v.ToString("0.0#") + " DEG";
        }
    }
}
