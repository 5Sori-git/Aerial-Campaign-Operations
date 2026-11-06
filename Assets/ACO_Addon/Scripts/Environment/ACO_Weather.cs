using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 날씨(동기화 없음). 육각 지형 시드(동기화됨)로 날씨를 고른다 — 모든 클라이언트가 같은 날씨를 보고, 지형을 리롤하면(미션 뒤 새 지형 포함) 날씨도 바뀐다.
    /// 날씨(WX_*)마다 비율(Weights)과 값: 구름 양(ACO_CloudField.SetCoverage), 구름 어둡기(SetDarkness), 안개(씬 안개 밀도 × 배율, 색 × 색조 —
    /// 구름 속 안개는 그 위에 덮인다), 비 세기(ACO_Rain.SetIntensity), 태양 세기(Sun.intensity × 배율), 구름층(SetLayer — 비 = 비구름층, 안개 = 낮은 안개층).
    /// 폭풍(WX_STORM) = 더 어두운 비구름층 + 센 비 + 번개(ACO_Lightning.SetStorm — 섬광·낙뢰 줄기·천둥). 바뀔 때 TransitionTime 동안 서서히(처음엔 바로).
    /// 게임 진행이 작전 카드·명단 글자에 날씨 이름을 띄운다(GetWeatherName).
    /// 비와 폭풍에는 건물 창문 불빛을 켠다(WindowGlow). 창문 머티리얼 에셋의 _EmissionColor를 직접 바꾸므로 모든 건물이 함께 바뀐다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Weather : UdonSharpBehaviour
    {
        public const int WX_CLEAR = 0;
        public const int WX_CLOUDY = 1;
        public const int WX_FOG = 2;
        public const int WX_RAIN = 3;
        public const int WX_STORM = 4;
        public const int WEATHER_COUNT = 5;

        [Header("참조 (ACO_SkyBuilder·Bake가 넣는다)")]
        public ACO_HexTerrain Terrain;
        public ACO_CloudField Clouds;
        public ACO_Rain Rain;
        [Tooltip("번개(폭풍) — 날씨별 LightningAmount로 켠다")]
        public ACO_Lightning Lightning;
        [Tooltip("태양(디렉셔널 라이트). 비면 태양 세기를 바꾸지 않는다")]
        public Light Sun;

        [Header("고르기")]
        [Tooltip("-1 = 지형 시드로 무작위, 0~4 = 고정(맑음/구름 많음/안개/비/폭풍) — 테스트용")]
        [Range(-1, 4)] public int ForcedWeather = -1;
        [Tooltip("날씨별 비율(WX_* 순서: 맑음, 구름 많음, 안개, 비, 폭풍)")]
        public float[] Weights = new float[] { 4f, 3f, 1.5f, 1.5f, 0.8f };
        [Tooltip("바뀔 때 서서히 바뀌는 시간(초)")]
        public float TransitionTime = 5f;

        [Header("날씨별 값 (WX_* 순서)")]
        public string[] WeatherNames = new string[] { "CLEAR", "CLOUDY", "FOG", "RAIN", "STORM" };
        [Tooltip("구름 양(0~1, 구름 풀 중 켜는 비율)")]
        public float[] CloudCoverage = new float[] { 0.25f, 0.85f, 0.4f, 1f, 1f };
        [Tooltip("구름 어둡기(0 = 흰 구름, 1 = 어두운 회색)")]
        public float[] CloudDarkness = new float[] { 0f, 0.2f, 0.1f, 0.65f, 0.9f };
        [Tooltip("안개 밀도 = 씬 안개 밀도 × 이 배율(Exponential Squared — 씬 기본 0.000028이면 ×18 ≈ 시야 3km)")]
        public float[] FogDensityScale = new float[] { 1f, 2.2f, 18f, 7f, 9f };
        [Tooltip("안개 색 = 씬 안개 색 × 이 색")]
        public Color[] FogTint = new Color[] { new Color(1f, 1f, 1f, 1f), new Color(0.88f, 0.9f, 0.93f, 1f), new Color(1.05f, 1.05f, 1.05f, 1f), new Color(0.62f, 0.65f, 0.7f, 1f), new Color(0.48f, 0.5f, 0.56f, 1f) };
        [Tooltip("비 세기(0~1)")]
        public float[] RainIntensity = new float[] { 0f, 0f, 0f, 0.85f, 1f };
        [Tooltip("태양 세기 배율")]
        public float[] SunScale = new float[] { 1f, 0.75f, 0.6f, 0.45f, 0.3f };
        [Tooltip("구름층(ACO_CloudField.LAYER_*: 0 = 없음, 1 = 비구름층, 2 = 낮은 안개층)")]
        public int[] CloudLayer = new int[] { 0, 0, 2, 1, 1 };
        [Tooltip("번개(0~1) — 폭풍만")]
        public float[] LightningAmount = new float[] { 0f, 0f, 0f, 0f, 1f };
        [Tooltip("창문 불빛 세기(0 = 꺼짐, 비·폭풍에 켠다)")]
        public float[] WindowGlow = new float[] { 0f, 0f, 0f, 1f, 1.3f };

        [Header("창문 불빛")]
        [Tooltip("건물 창문 머티리얼(Bake가 넣는다). 이미션 마스크의 켜진 창에 불이 들어온다")]
        public Material[] WindowMaterials;
        [Tooltip("불빛 세기 1일 때의 색")]
        public Color WindowGlowColor = Color.white;

        private int _weather = -1;
        private int _seenSeed = int.MinValue;
        private int _seenForced = -2;
        private float _pollTimer;
        private float _t = 1f;
        private float _baseDensity;
        private Color _baseColor;
        private float _baseSun = 1f;
        // 전환 시작·끝 값
        private float _fromDensity, _toDensity, _fromDark, _toDark, _fromSun, _toSun, _fromGlow, _toGlow;
        private Color _fromColor, _toColor;

        void Start()
        {
            _baseDensity = RenderSettings.fogDensity;
            _baseColor = RenderSettings.fogColor;
            if (Sun) { _baseSun = Sun.intensity; }
            _fromDensity = _toDensity = _baseDensity;
            _fromColor = _toColor = _baseColor;
            _fromSun = _toSun = 1f;
            // 첫 확인은 첫 Update에서(구름·비의 Start가 먼저 끝나도록)
        }

        void Update()
        {
            _pollTimer -= Time.deltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = 1f;
                Check();
            }
            if (_t >= 1f) { return; }
            _t = TransitionTime > 0.01f ? Mathf.Min(_t + Time.deltaTime / TransitionTime, 1f) : 1f;
            ApplyBlend(_t * _t * (3f - 2f * _t));
        }

        private void Check()
        {
            int seed = Terrain ? Terrain.GetSeed() : 0;
            if (seed == _seenSeed && ForcedWeather == _seenForced) { return; }
            bool first = _weather < 0;
            _seenSeed = seed;
            _seenForced = ForcedWeather;
            int w = ForcedWeather >= 0 ? Mathf.Min(ForcedWeather, WEATHER_COUNT - 1) : (seed != 0 ? Pick(seed) : WX_CLEAR);
            if (w == _weather) { return; }
            SetWeather(w, first);
        }

        /// 시드로 날씨를 고른다(정수 해시 — 전역 Random을 건드리지 않는다).
        private int Pick(int seed)
        {
            float total = 0f;
            for (int i = 0; i < WEATHER_COUNT; i++) { total += Value(Weights, i, 0f); }
            if (total <= 0f) { return WX_CLEAR; }
            int h = seed ^ 0x2F6B3A1;
            h = (h ^ (h >> 16)) * 0x45d9f3b;
            h = (h ^ (h >> 16)) * 0x45d9f3b;
            h = h ^ (h >> 16);
            float r = (h & 0xFFFFFF) / 16777216f * total;
            for (int i = 0; i < WEATHER_COUNT; i++)
            {
                r -= Value(Weights, i, 0f);
                if (r < 0f) { return i; }
            }
            return WEATHER_COUNT - 1;
        }

        private void SetWeather(int w, bool instant)
        {
            _weather = w;
            // 지금 값에서 새 값으로
            _fromDensity = CurrentDensity();
            _fromColor = CurrentColor();
            _fromDark = _toDark;
            _fromSun = _toSun;
            _fromGlow = _toGlow;
            _toDensity = _baseDensity * Value(FogDensityScale, w, 1f);
            Color tint = FogTint != null && w < FogTint.Length ? FogTint[w] : Color.white;
            _toColor = new Color(_baseColor.r * tint.r, _baseColor.g * tint.g, _baseColor.b * tint.b, _baseColor.a);
            _toDark = Value(CloudDarkness, w, 0f);
            _toSun = Value(SunScale, w, 1f);
            _toGlow = Value(WindowGlow, w, 0f);

            if (Clouds)
            {
                Clouds.SetCoverage(Value(CloudCoverage, w, 0.5f));
                Clouds.SetLayer(CloudLayer != null && w < CloudLayer.Length ? CloudLayer[w] : ACO_CloudField.LAYER_NONE, instant);
            }
            if (Rain) { Rain.SetIntensity(Value(RainIntensity, w, 0f)); }
            if (Lightning) { Lightning.SetStorm(Value(LightningAmount, w, 0f), instant); }
            _t = instant ? 1f : 0f;
            ApplyBlend(instant ? 1f : 0f);
        }

        private float _curDensity;
        private Color _curColor;
        private bool _curSet;

        private float CurrentDensity() { return _curSet ? _curDensity : _baseDensity; }
        private Color CurrentColor() { return _curSet ? _curColor : _baseColor; }

        private void ApplyBlend(float k)
        {
            _curDensity = Mathf.Lerp(_fromDensity, _toDensity, k);
            _curColor = Color.Lerp(_fromColor, _toColor, k);
            _curSet = true;
            if (Clouds)
            {
                Clouds.SetBaseFog(_curDensity, _curColor);
                Clouds.SetDarkness(Mathf.Lerp(_fromDark, _toDark, k));
                Clouds.SetSunScale(Mathf.Lerp(_fromSun, _toSun, k));
            }
            else
            {
                RenderSettings.fogDensity = _curDensity;
                RenderSettings.fogColor = _curColor;
            }
            if (Sun) { Sun.intensity = _baseSun * Mathf.Lerp(_fromSun, _toSun, k); }
            SetWindowGlow(Mathf.Lerp(_fromGlow, _toGlow, k));
        }

        private void SetWindowGlow(float glow)
        {
            if (WindowMaterials == null) { return; }
            Color c = WindowGlowColor * glow;
            for (int i = 0; i < WindowMaterials.Length; i++)
            {
                if (WindowMaterials[i]) { WindowMaterials[i].SetColor("_EmissionColor", c); }
            }
        }

        private float Value(float[] a, int i, float fallback) { return a != null && i >= 0 && i < a.Length ? a[i] : fallback; }

        // ───────────── 조회 ─────────────

        public int GetWeather() { return _weather < 0 ? WX_CLEAR : _weather; }

        public string GetWeatherName()
        {
            int w = GetWeather();
            return WeatherNames != null && w < WeatherNames.Length ? WeatherNames[w] : "";
        }
    }
}
