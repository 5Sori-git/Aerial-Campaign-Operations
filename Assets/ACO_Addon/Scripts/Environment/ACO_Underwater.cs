using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 물속 연출(로컬 전용, 동기화 없음): 내 머리가 해수면(SeaLevel) 아래로 들어가면 — 걸어 들어가든, 기체가 가라앉든 —
    ///  - 시야: 짙은 물빛 안개(FogDensity·FogColor) + 머리를 감싸는 옅은 물빛 구(Tint, HUD보다 아래 큐)
    ///    + 물빛 벽(Backdrop — 안개가 거의 다 덮는 거리의 큰 구 안쪽 면을 지금 안개 색으로 칠해 하늘·스카이박스를 가린다)
    ///    + 너울 뒷면(SwellRenderer의 내 화면 머티리얼 복사본만 컬링을 끈다 — 올려다본 수면이 셰이더 _UnderColor로 빛난다).
    ///    안개는 구름 필드(ACO_CloudField.SetUnderwater)가 다른 안개(날씨·구름 속)와 함께 쓴다 — 씬에 구름 필드가 없으면 직접 쓰고 나올 때 되돌린다.
    ///  - 소리: 물속 소리(UnderwaterLoop, 선택)만 켠다 — 다른 소리는 건드리지 않는다.
    /// 수면에서 깜빡이지 않게 들어갈 때는 EnterDepth만큼 더 내려가야 하고, 나올 때는 수면 위로 올라오면 된다. 들고 날 때 FadeSpeed로 섞는다.
    /// 수면 높이 = 너울(SwellRenderer — 바다 셰이더 ACO/Ocean의 가까운 너울 원판)이 있으면 셰이더와 같은 게르스트너 파도 4개를 머리 자리에서 계산한 높이,
    /// 없으면 평평한 SeaLevel. 파도 값(방향·파장·높이·날카로움, 높이 배율, 속도, 해수면)은 시작할 때 머티리얼에서 읽고, 시간은 셰이더(_Time.y)와 같은 레벨 로드 후 시간.
    /// 게르스트너 파도는 수면 점을 옆으로도 옮기므로, 머리 아래로 옮겨져 온 원래 점을 두 번 되짚어 찾아 그 높이를 쓴다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Underwater : UdonSharpBehaviour
    {
        [Tooltip("해수면 높이(m). Bake가 미션 생성기 SeaLevel로 넣는다(너울 머티리얼이 있으면 그 _SeaY를 쓴다)")]
        public float SeaLevel = 0f;
        [Tooltip("가까운 너울 원판(씬의 ACO_OceanSwell — Bake가 넣는다). 있으면 셰이더와 같은 너울 높이로 물속을 판정하고, 물속에선 뒷면을 그린다. 비면 평평한 SeaLevel")]
        public Renderer SwellRenderer;
        [Tooltip("이만큼(m) 더 내려가야 물속으로 본다(수면에서 깜빡이지 않게)")]
        public float EnterDepth = 0.15f;
        [Tooltip("들고 날 때 섞는 속도(1/초)")]
        public float FadeSpeed = 6f;
        [Tooltip("Bake가 씬의 구름 필드를 넣는다(안개를 같이 쓴다). 비면 안개를 직접 쓴다")]
        public ACO_CloudField Clouds;
        [Tooltip("함내 구역. Bake가 넣는다. 격실은 해수면 아래에 놓여도 물속이 아니다")]
        public ACO_InteriorZone Interior;

        [Header("시야")]
        [Tooltip("물속 안개 밀도(Exponential Squared — 0.03이면 약 50m 앞이 거의 안 보인다)")]
        public float FogDensity = 0.03f;
        public Color FogColor = new Color(0.04f, 0.18f, 0.25f, 1f);
        [Tooltip("머리를 감싸는 반투명 구(ACO/MarkerOverlay 머티리얼, 큐는 HUD보다 아래). 물속일 때만 켜고 알파를 섞는다")]
        public Renderer Tint;
        [Tooltip("구의 가장 진한 알파(0~1)")]
        [Range(0f, 1f)] public float TintAlpha = 0.2f;
        [Tooltip("물빛 벽(안쪽 면만 그리는 구, ACO/UnderwaterBackdrop 머티리얼 — Default 레이어). 물속일 때만 켜고, 반경은 지금 안개가 99% 덮는 거리")]
        public Renderer Backdrop;
        [Tooltip("물빛 벽 반경의 한계(m)")]
        public float BackdropMinRadius = 20f;
        public float BackdropMaxRadius = 3000f;

        [Header("소리")]
        [Tooltip("물속 소리(반복, 선택). 물속일 때만 켠다")]
        public AudioSource UnderwaterLoop;
        [Tooltip("물속 소리 최대 볼륨")]
        [Range(0f, 1f)] public float LoopVolume = 0.6f;

        private VRCPlayerApi _local;
        private bool _under;
        private float _amount;
        private float _applied = -1f;
        private Material _tintMat;
        private Color _tintColor;
        private Material _swellMat;
        private bool _culledOff;
        // 구름 필드가 없을 때 직접 쓴 안개를 되돌리기 위한 값
        private bool _fogSaved;
        private float _savedDensity;
        private Color _savedColor;

        // 너울(셰이더 ACO/Ocean의 게르스트너 파도 4개): 방향 단위 벡터, 파수 k, 파속 c, 높이 a, 수평 이동 q
        private const int SWELL_WAVES = 4;
        private bool _hasSwell;
        private float _seaY;
        private float _waveSpeed = 1f;
        private float[] _wDx = new float[SWELL_WAVES];
        private float[] _wDz = new float[SWELL_WAVES];
        private float[] _wK = new float[SWELL_WAVES];
        private float[] _wC = new float[SWELL_WAVES];
        private float[] _wA = new float[SWELL_WAVES];
        private float[] _wQ = new float[SWELL_WAVES];

        void Start()
        {
            _local = Networking.LocalPlayer;
            if (SwellRenderer) { _swellMat = SwellRenderer.material; }   // 내 화면 전용 복사본(컬링만 바꾼다)
            ReadSwell();
            if (Tint)
            {
                _tintMat = Tint.material;
                _tintColor = _tintMat.color;
                Tint.gameObject.SetActive(false);
            }
            if (Backdrop) { Backdrop.gameObject.SetActive(false); }
            if (UnderwaterLoop) { UnderwaterLoop.volume = 0f; }
        }

        void Update()
        {
            if (!Utilities.IsValid(_local)) { return; }
            Vector3 head = _local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            float surface = SurfaceHeight(head.x, head.z);
            bool dry = Interior != null && Interior.Contains(head);
            _under = !dry && (_under ? head.y < surface : head.y < surface - EnterDepth);
            float target = _under ? 1f : 0f;
            if (_amount == target && _applied == _amount) { return; }
            _amount = Mathf.MoveTowards(_amount, target, Time.deltaTime * FadeSpeed);
            Apply();
        }

        /// 물빛 구·벽을 머리에 맞춘다(머리 위치가 확정된 뒤). 벽 반경은 지금 안개(Exponential Squared)가 99% 덮는 거리 = 2.15 / 밀도.
        public override void PostLateUpdate()
        {
            if (_amount <= 0f || !Utilities.IsValid(_local)) { return; }
            Vector3 head = _local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            if (Tint) { Tint.transform.position = head; }
            if (Backdrop)
            {
                float radius = Mathf.Clamp(2.15f / Mathf.Max(RenderSettings.fogDensity, 0.0001f), BackdropMinRadius, BackdropMaxRadius);
                Transform bt = Backdrop.transform;
                bt.position = head;
                bt.localScale = Vector3.one * (radius * 2f);   // 기본 구 지름 1m
            }
        }

        private void Apply()
        {
            _applied = _amount;
            float t = _amount * _amount * (3f - 2f * _amount);
            bool on = _amount > 0f;

            // 안개
            if (Clouds) { Clouds.SetUnderwater(t, FogDensity, FogColor); }
            else if (on)
            {
                if (!_fogSaved) { _fogSaved = true; _savedDensity = RenderSettings.fogDensity; _savedColor = RenderSettings.fogColor; }
                RenderSettings.fogDensity = Mathf.Lerp(_savedDensity, FogDensity, t);
                RenderSettings.fogColor = Color.Lerp(_savedColor, FogColor, t);
            }
            else if (_fogSaved)
            {
                _fogSaved = false;
                RenderSettings.fogDensity = _savedDensity;
                RenderSettings.fogColor = _savedColor;
            }

            // 물빛 구
            if (Tint)
            {
                if (Tint.gameObject.activeSelf != on) { Tint.gameObject.SetActive(on); }
                if (on && _tintMat) { Color c = _tintColor; c.a = TintAlpha * t; _tintMat.color = c; }
            }

            // 물빛 벽 + 너울 뒷면
            if (Backdrop && Backdrop.gameObject.activeSelf != on) { Backdrop.gameObject.SetActive(on); }
            if (_swellMat && _culledOff != on)
            {
                _culledOff = on;
                _swellMat.SetFloat("_Cull", on ? 0f : 2f);   // CullMode.Off / Back
            }

            // 물속 소리
            if (UnderwaterLoop)
            {
                UnderwaterLoop.volume = LoopVolume * t;
                if (on && !UnderwaterLoop.isPlaying) { UnderwaterLoop.Play(); }
                else if (!on && UnderwaterLoop.isPlaying) { UnderwaterLoop.Stop(); }
            }
        }

        /// 지금 물속인가(다른 연출이 볼 수 있게).
        public bool IsUnderwater() { return _under; }

        /// 너울 머티리얼의 파도 값을 읽는다(셰이더 Gerstner와 같은 식). 머티리얼이 없으면 평평한 바다.
        private void ReadSwell()
        {
            _hasSwell = _swellMat != null;
            _seaY = SeaLevel;
            if (!_hasSwell) { return; }
            _seaY = _swellMat.GetFloat("_SeaY");
            _waveSpeed = _swellMat.GetFloat("_WaveSpeed");
            float scale = _swellMat.GetFloat("_SwellScale");
            for (int i = 0; i < SWELL_WAVES; i++)
            {
                Vector4 w = _swellMat.GetVector("_SwellWave" + (i + 1));
                float rad = w.x * Mathf.Deg2Rad;
                _wDx[i] = Mathf.Cos(rad);
                _wDz[i] = Mathf.Sin(rad);
                _wK[i] = 6.2832f / Mathf.Max(w.y, 1f);
                _wC[i] = Mathf.Sqrt(9.8f / _wK[i]);
                _wA[i] = w.z * scale;
                _wQ[i] = Mathf.Clamp01(w.w) / Mathf.Max(_wK[i] * _wA[i] * 4f, 0.0001f);
            }
        }

        /// (x, z)의 수면 높이: 너울이 있으면 셰이더와 같은 파도 높이, 없으면 평평한 해수면.
        /// 파도는 원래 수면 점 p를 p + 수평 이동으로 옮기므로, x - 이동(p)를 두 번 되짚어 머리 아래로 온 점 p를 찾고 그 점의 높이를 쓴다.
        private float SurfaceHeight(float x, float z)
        {
            if (!_hasSwell) { return SeaLevel; }
            float t = Time.timeSinceLevelLoad * _waveSpeed;
            float px = x;
            float pz = z;
            for (int iter = 0; iter < 2; iter++)
            {
                float dx = 0f;
                float dz = 0f;
                for (int i = 0; i < SWELL_WAVES; i++)
                {
                    float f = _wK[i] * (_wDx[i] * px + _wDz[i] * pz - _wC[i] * t);
                    float h = _wQ[i] * _wA[i] * Mathf.Cos(f);
                    dx += h * _wDx[i];
                    dz += h * _wDz[i];
                }
                px = x - dx;
                pz = z - dz;
            }
            float y = 0f;
            for (int i = 0; i < SWELL_WAVES; i++)
            {
                y += _wA[i] * Mathf.Sin(_wK[i] * (_wDx[i] * px + _wDz[i] * pz - _wC[i] * t));
            }
            return _seaY + y;
        }
    }
}
