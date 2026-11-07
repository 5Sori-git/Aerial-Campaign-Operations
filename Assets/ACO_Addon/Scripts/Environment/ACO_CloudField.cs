using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 뭉게구름 배치 + 구름층 + 구름 속 안개. 동기화 없음 — 육각 지형 시드(동기화됨)로 모든 클라이언트가 같은 자리에 놓는다.
    /// 구름은 미리 만들어 둔 풀(ACO_SkyBuilder가 만든 자식들, 셰이더 ACO/CloudPuffs)을 켜고 끄며 옮긴다 — 생성 중 끊김이 없다.
    ///  - 지형 시드가 바뀌면(새 지형) 다시 놓는다. 지형이 없으면 FallbackSeed.
    ///  - Coverage(0~1) = 풀 중 켜는 비율. 고도 MinAltitude~MaxAltitude, 지형 반경 안(FieldRadius) 원판에 고르게.
    ///  - 구름 속: 내 머리가 구름 덩어리의 속 타원체(CloudCoreCenters/Extents, 빌더가 구움) 가까이 들어가면 안개를 하얗게 짙게 한다
    ///    (셰이더가 가까운 퍼프를 지우므로 안은 안개 + 물안개 막(CloudVeil)으로 채운다). 나오면 원래 안개(기본 안개)로 돌린다.
    ///  - 구름층(날씨가 SetLayer로 고른다, 판은 머리 아래·위로 따라다닌다 — 셰이더 ACO/CloudLayer):
    ///    LAYER_DECK = 비구름층(DeckBottom~DeckTop): 아래는 짙은 회색 천장(비), 안은 짙은 안개, 위로 뚫고 나오면 흰 구름바다 위 맑은 하늘.
    ///    LAYER_FOG = 낮은 안개층(FogLayerTop 아래 짙고 FogLayerFade 동안 옅어진다): 위에서 보면 흰 안개 바다(높은 봉우리만 솟는다), 위는 맑다.
    ///    날씨가 바뀌면 LayerFadeTime 동안 디더링으로 나타나고 사라진다.
    ///    CloudVolume(셰이더 ACO/CloudVolume)이 있으면 판과 층 속 안개를 쓰지 않는다 — 시선이 층을 지나는 길이로 픽셀마다 계산해 경계를 지날 때 튀지 않는다.
    ///  - 하늘 안개 돔(SkyFog, 셰이더 ACO/SkyFog): 머리로 옮기기만 한다 — 셰이더가 지금 안개로 하늘 픽셀을 덮는다.
    ///  - 날씨가 쓸 API: SetCoverage, SetDarkness(어두운 구름), SetBaseFog(구름 밖 안개), SetLayer. 물속 연출(ACO_Underwater)은 SetUnderwater로 물속 안개를 넣는다 —
    ///    씬 안개는 이 스크립트 하나가 쓴다(기본 → 층 위(맑음) → 층 속 → 구름 속 → 물속 순서로 겹친다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_CloudField : UdonSharpBehaviour
    {
        public const int LAYER_NONE = 0;
        public const int LAYER_DECK = 1;
        public const int LAYER_FOG = 2;

        [Header("배치")]
        [Tooltip("이 지형의 시드로 배치한다(Bake가 넣는다). 비우면 FallbackSeed")]
        public ACO_HexTerrain Terrain;
        public int FallbackSeed = 1234;
        [Range(0f, 1f)] public float Coverage = 0.5f;
        [Tooltip("구름을 놓을 원판 반지름(m, 월드 중심 기준)")]
        public float FieldRadius = 18000f;
        public float MinAltitude = 1300f;
        public float MaxAltitude = 2600f;
        public float MinScale = 0.75f;
        public float MaxScale = 1.4f;

        [Header("구름 풀 (ACO_SkyBuilder가 채운다)")]
        public Transform[] Clouds;
        [Tooltip("구름마다 속 타원체 중심(로컬)")]
        public Vector3[] CloudCoreCenters;
        [Tooltip("구름마다 속 타원체 반지름(로컬)")]
        public Vector3[] CloudCoreExtents;

        [Header("구름 속 안개 (씬 안개가 켜져 있어야 한다 — Exponential Squared 기준 값)")]
        public bool InsideFog = true;
        [Tooltip("뭉게구름 속만 짙게 — 0.006이면 140m에서 반쯤, 250m에서 90%, 360m면 거의 다 가린다(퍼프가 사라지는 40~160m를 덮는다). "
            + "가려지는 거리 = 상수 / 밀도: 반쯤 0.83, 90% 1.52, 99% 2.15. 비구름층·안개층은 따로(DeckFogDensity·FogLayerDensity)")]
        public float InsideFogDensity = 0.006f;
        public Color InsideFogColor = new Color(0.86f, 0.89f, 0.93f, 1f);
        [Tooltip("속 타원체 크기 대비 이 배율 밖에서부터 안개가 짙어지기 시작한다(퍼프가 사라지기 전에 안개가 먼저 차오르게 겉모습보다 조금 밖)")]
        public float FogOuter = 1.6f;
        [Tooltip("이 배율 안이면 안개가 가장 짙다")]
        public float FogInner = 0.9f;
        [Tooltip("안개가 바뀌는 빠르기(초당)")]
        public float FogSpeed = 2.5f;
        public float CheckInterval = 0.1f;

        [Header("비구름층 (LAYER_DECK — 비)")]
        public float DeckBottom = 1300f;
        public float DeckTop = 1800f;
        [Tooltip("층 경계에서 안개가 섞이는 폭(m)")]
        public float DeckEdge = 60f;
        public float DeckFogDensity = 0.001f;
        public Color DeckFogColor = new Color(0.62f, 0.65f, 0.7f, 1f);
        [Tooltip("아랫면(아래에서만 보인다)·윗면(위에서만 보인다) 판 — ACO_SkyBuilder가 만든다")]
        public Renderer DeckBottomRenderer;
        public Renderer DeckTopRenderer;

        [Header("낮은 안개층 (LAYER_FOG — 안개)")]
        [Tooltip("이 고도 아래가 짙은 안개, 윗면 판이 여기 놓인다")]
        public float FogLayerTop = 400f;
        [Tooltip("윗면 위로 이만큼(m) 올라가는 동안 안개가 옅어진다")]
        public float FogLayerFade = 200f;
        public float FogLayerDensity = 0.001f;
        public Color FogLayerColor = new Color(0.8f, 0.82f, 0.85f, 1f);
        public Renderer FogTopRenderer;

        [Header("층 공통")]
        [Tooltip("날씨가 바뀔 때 층이 나타나고 사라지는 시간(초)")]
        public float LayerFadeTime = 5f;
        [Header("하늘 안개 돔·물안개 막 (ACO_SkyBuilder가 만든다)")]
        [Tooltip("머리를 감싸는 구(ACO/SkyFog) — 하늘에만 지금 안개를 덮는다")]
        public Renderer SkyFog;
        [Tooltip("구름 속에서 머리를 감싸는 작은 구(ACO/CloudVeil) — 안개 줄기가 스쳐 간다")]
        public Renderer CloudVeil;
        [Range(0f, 1f)] public float VeilStrength = 1f;
        [Tooltip("비구름층 속 물안개 막 배율(층 속은 시정 2km라 뭉게구름 속보다 옅게)")]
        [Range(0f, 1f)] public float DeckVeilStrength = 0.55f;

        [Header("체적 구름층 (ACO_SkyBuilder가 만든다 — 있으면 판 대신 쓴다)")]
        public Renderer CloudVolume;
        [Tooltip("관전 카메라가 그리는 체적 구름(카메라 자식, 레이어 18). Bake가 넣는다. CloudVolume은 FX 레이어라 관전 카메라에 찍히지 않는다")]
        public Renderer SpectatorVolume;
        [Tooltip("비구름층 밀도(1m당). 층 두께 500m면 0.005가 수직으로 약 92% 가린다")]
        public float DeckVolumeDensity = 0.005f;
        public Color DeckTopColor = new Color(0.95f, 0.96f, 0.98f, 1f);
        public Color DeckUnderColor = new Color(0.55f, 0.58f, 0.63f, 1f);
        [Tooltip("낮은 안개층 밀도(1m당)")]
        public float FogVolumeDensity = 0.004f;
        [Tooltip("층 속에서 전역 안개도 쓰는 정도(0~1). 투명 물체는 체적 층보다 뒤에 그려져 전역 안개로만 흐려진다")]
        [Range(0f, 1f)] public float VolumeGlobalFog = 1f;
        public Color FogTopColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        public Color FogUnderColor = new Color(0.78f, 0.8f, 0.83f, 1f);
        private bool _planeMode;   // HUD 설정 CLOUD QUALITY LOW: 판으로 그린다
        private Material _volumeMat;
        private int _volumeLayer = -1;
        private float _volumeFade = -1f;

        private int _placedSeed = int.MinValue;
        private float _placedCoverage = -1f;
        private float _seedTimer;
        private float _checkTimer;
        private float _inside;
        private float _insideTarget;
        private bool _fogTouched;
        private float _baseDensity;
        private Color _baseColor;
        private float _clearDensity;
        private Color _clearColor;
        private Material _material;

        // 구름층
        private int _layer = LAYER_NONE;
        private float _deckFade;
        private float _fogFade;
        private float _appliedDeckFade = -1f;
        private float _appliedFogFade = -1f;
        private Material _deckBottomMat;
        private Material _deckTopMat;
        private Material _fogTopMat;
        // 이번 프레임의 층 안개 값(머리 고도로) — 바뀌었을 때만 안개를 다시 쓴다
        private float _layerAbove;
        private float _deckIn;
        private float _fogIn;
        private float _lastAbove = -1f;
        private float _lastDeckIn = -1f;
        private float _lastFogIn = -1f;
        private Vector3 _head;
        private bool _hasHead;

        // 물안개 막
        private Material _veilMat;
        private float _veilApplied = -1f;

        void Start()
        {
            _baseDensity = RenderSettings.fogDensity;
            _baseColor = RenderSettings.fogColor;
            _clearDensity = _baseDensity;
            _clearColor = _baseColor;
            // 구름 머티리얼을 한 벌 복사해 모든 구름이 같이 쓴다(날씨로 어둡게 할 때 원본 에셋을 건드리지 않게)
            if (Clouds != null && Clouds.Length > 0 && Clouds[0])
            {
                Renderer r0 = Clouds[0].GetComponent<Renderer>();
                if (r0)
                {
                    _material = r0.material;
                    for (int i = 1; i < Clouds.Length; i++)
                    {
                        if (!Clouds[i]) { continue; }
                        Renderer r = Clouds[i].GetComponent<Renderer>();
                        if (r) { r.sharedMaterial = _material; }
                    }
                }
            }
            if (DeckBottomRenderer) { _deckBottomMat = DeckBottomRenderer.material; DeckBottomRenderer.gameObject.SetActive(false); }
            if (DeckTopRenderer) { _deckTopMat = DeckTopRenderer.material; DeckTopRenderer.gameObject.SetActive(false); }
            if (FogTopRenderer) { _fogTopMat = FogTopRenderer.material; FogTopRenderer.gameObject.SetActive(false); }
            if (CloudVeil) { _veilMat = CloudVeil.material; CloudVeil.gameObject.SetActive(false); }
            if (CloudVolume) { _volumeMat = CloudVolume.material; CloudVolume.gameObject.SetActive(false); }
            if (SpectatorVolume && _volumeMat) { SpectatorVolume.sharedMaterial = _volumeMat; }   // 층 높이, 페이드, 번개를 그대로 나눠 쓴다
            CheckSeed();
        }

        void Update()
        {
            _seedTimer -= Time.deltaTime;
            if (_seedTimer <= 0f)
            {
                _seedTimer = 1f;
                CheckSeed();
            }

            VRCPlayerApi lp = Networking.LocalPlayer;
            _hasHead = Utilities.IsValid(lp);
            if (_hasHead) { _head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position; }

            if (InsideFog)
            {
                _checkTimer -= Time.deltaTime;
                if (_checkTimer <= 0f)
                {
                    _checkTimer = CheckInterval;
                    _insideTarget = InsideAmount();
                }
            }
            else { _insideTarget = 0f; }

            UpdateLayers();

            // 구름 속 정도·층 안개가 그대로이고 새로 넣은 값(물속·기본 안개)도 없으면 쓸 게 없다
            bool layerChanged = _layerAbove != _lastAbove || _deckIn != _lastDeckIn || _fogIn != _lastFogIn;
            if (_inside != _insideTarget || _fogDirty || layerChanged)
            {
                _inside = Mathf.MoveTowards(_inside, _insideTarget, Time.deltaTime * FogSpeed);
                ApplyFog();
            }
            UpdateVeil();
        }

        /// 머리를 따라다니는 것들(하늘 안개 돔, 층 판, 물안개 막)을 머리 위치가 확정된 뒤 옮긴다.
        public override void PostLateUpdate()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { return; }
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            if (SkyFog) { SkyFog.transform.position = head; }
            if (CloudVeil && CloudVeil.gameObject.activeSelf) { CloudVeil.transform.position = head; }
            if (CloudVolume && CloudVolume.gameObject.activeSelf) { CloudVolume.transform.position = head; }
            FollowPlane(DeckBottomRenderer, head, DeckBottom);
            FollowPlane(DeckTopRenderer, head, DeckTop);
            FollowPlane(FogTopRenderer, head, FogLayerTop);
        }

        private void FollowPlane(Renderer r, Vector3 head, float y)
        {
            if (!r || !r.gameObject.activeSelf) { return; }
            r.transform.position = new Vector3(head.x, y, head.z);
        }

        // ───────────── 구름층 ─────────────

        /// 층 나타나기·사라지기와 머리 고도에 따른 층 안개 값.
        private void UpdateLayers()
        {
            float step = Time.deltaTime / Mathf.Max(LayerFadeTime, 0.01f);
            _deckFade = Mathf.MoveTowards(_deckFade, _layer == LAYER_DECK ? 1f : 0f, step);
            _fogFade = Mathf.MoveTowards(_fogFade, _layer == LAYER_FOG ? 1f : 0f, step);
            if (_deckFade != _appliedDeckFade)
            {
                _appliedDeckFade = _deckFade;
                SetPlane(DeckBottomRenderer, _deckBottomMat, _deckFade);
                SetPlane(DeckTopRenderer, _deckTopMat, _deckFade);
                // 층 윗면보다 낮은 뭉게구름을 숨긴다 — 고도가 겹치면 층의 부드러운 경계로 윤곽이 비친다
                if (_material)
                {
                    _material.SetFloat("_HideBelow", DeckTop);
                    _material.SetFloat("_HideAmount", _deckFade);
                }
                ApplyCloudLight();
            }
            if (_fogFade != _appliedFogFade)
            {
                _appliedFogFade = _fogFade;
                SetPlane(FogTopRenderer, _fogTopMat, _fogFade);
                ApplyCloudLight();
            }

            UpdateVolume();

            _layerAbove = 0f;
            _deckIn = 0f;
            _fogIn = 0f;
            if (!_hasHead) { return; }
            float y = _head.y;
            if (_deckFade > 0f)
            {
                // 체적 층은 불투명한 물체가 이미 흐려져 있어 전역 안개를 더 넓게 섞어도 된다(지날 때 튀지 않게)
                float edge = Mathf.Max(DeckEdge, Volumetric() ? 200f : 1f);
                float enter = Smooth01((y - (DeckBottom - edge)) / (edge * 2f));
                float leave = Smooth01(((DeckTop + edge) - y) / (edge * 2f));
                _deckIn = _deckFade * enter * leave;
                // 체적 층은 층 전체를 지나는 동안 맑은 공기로 넘어간다(판이 있을 때는 윗면 둘레에서)
                float above = Volumetric() ? Smooth01((y - DeckBottom) / Mathf.Max(DeckTop - DeckBottom, 1f)) : Smooth01((y - DeckTop) / (edge * 2f));
                _layerAbove = Mathf.Max(_layerAbove, _deckFade * above);
            }
            if (_fogFade > 0f)
            {
                float up = Smooth01((y - FogLayerTop) / Mathf.Max(FogLayerFade, 1f));
                _fogIn = _fogFade * (1f - up);
                _layerAbove = Mathf.Max(_layerAbove, _fogFade * up);
            }
        }

        private bool Volumetric() { return CloudVolume != null && !_planeMode; }

        /// HUD 설정 CLOUD QUALITY(내 화면만): true = 체적, false = 판.
        public void SetVolumetric(bool on)
        {
            _planeMode = !on;
            _appliedDeckFade = -1f;
            _appliedFogFade = -1f;
            _volumeLayer = -1;
            _volumeFade = -1f;
            _fogDirty = true;
        }

        private void UpdateVolume()
        {
            if (!_volumeMat) { return; }
            int layer = _deckFade >= _fogFade ? LAYER_DECK : LAYER_FOG;
            float fade = _planeMode ? 0f : Mathf.Max(_deckFade, _fogFade) * (1f - _underwater);
            bool on = fade > 0f;
            if (CloudVolume.gameObject.activeSelf != on) { CloudVolume.gameObject.SetActive(on); }
            if (SpectatorVolume && SpectatorVolume.enabled != on) { SpectatorVolume.enabled = on; }
            if (!on) { return; }
            if (layer != _volumeLayer)
            {
                _volumeLayer = layer;
                bool deck = layer == LAYER_DECK;
                _volumeMat.SetFloat("_SlabBottom", deck ? DeckBottom : -10f);
                _volumeMat.SetFloat("_SlabTop", deck ? DeckTop : FogLayerTop);
                _volumeMat.SetFloat("_Density", deck ? DeckVolumeDensity : FogVolumeDensity);
                _volumeMat.SetColor("_TopColor", deck ? DeckTopColor : FogTopColor);
                _volumeMat.SetColor("_BottomColor", deck ? DeckUnderColor : FogUnderColor);
            }
            if (fade != _volumeFade)
            {
                _volumeFade = fade;
                _volumeMat.SetFloat("_Fade", fade);
            }
        }

        private void SetPlane(Renderer r, Material m, float fade)
        {
            if (!r) { return; }
            if (Volumetric()) { fade = 0f; }   // 체적 층이 대신 그린다
            bool on = fade > 0f;
            if (r.gameObject.activeSelf != on) { r.gameObject.SetActive(on); }
            if (on && m) { m.SetFloat("_Fade", fade); }
        }

        private float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // 물안개 막: 구름 속(퍼프)과 비구름층 속에서만 — 낮은 안개층(바다·격납고 높이)에서는 쓰지 않는다
        private void UpdateVeil()
        {
            if (!CloudVeil || !_veilMat) { return; }
            float t = _inside * _inside * (3f - 2f * _inside);
            float amount = Mathf.Max(t, _deckIn * DeckVeilStrength) * VeilStrength * (1f - _underwater);
            if (amount == _veilApplied) { return; }
            _veilApplied = amount;
            bool on = amount > 0.001f;
            if (CloudVeil.gameObject.activeSelf != on)
            {
                CloudVeil.gameObject.SetActive(on);
                if (on && _hasHead) { CloudVeil.transform.position = _head; }
            }
            if (on) { _veilMat.SetFloat("_Amount", amount); }
        }

        // 물속 안개(ACO_Underwater가 넣는다): 구름 속 안개 위에 섞는다(0 = 물 밖)
        private float _underwater;
        private float _underwaterDensity;
        private Color _underwaterColor;
        private bool _fogDirty;

        /// 물속 안개 섞기(0~1, 이미 부드럽게 섞은 값). 안개는 이 스크립트 하나가 쓴다.
        public void SetUnderwater(float amount, float density, Color color)
        {
            _underwater = Mathf.Clamp01(amount);
            _underwaterDensity = density;
            _underwaterColor = color;
            _fogDirty = true;
        }

        /// 기본 안개 → 층 위(맑은 하늘) → 비구름층 속 → 낮은 안개층 속 → 구름 속 → 물속을 겹쳐 쓴다. 모두 없으면 기본으로 돌려놓는다.
        private void ApplyFog()
        {
            _fogDirty = false;
            _lastAbove = _layerAbove;
            _lastDeckIn = _deckIn;
            _lastFogIn = _fogIn;
            if (_inside <= 0f && _underwater <= 0f && _layerAbove <= 0f && _deckIn <= 0f && _fogIn <= 0f && _flash <= 0f) { RestoreBaseFog(); return; }
            float density = Mathf.Lerp(_baseDensity, _clearDensity, _layerAbove);
            Color color = Color.Lerp(_baseColor, _clearColor, _layerAbove);
            float layerFog = Volumetric() ? VolumeGlobalFog : 1f;
            if (_deckIn > 0f) { density = Mathf.Lerp(density, DeckFogDensity, _deckIn * layerFog); color = Color.Lerp(color, DeckFogColor, _deckIn * layerFog); }
            if (_fogIn > 0f) { density = Mathf.Lerp(density, FogLayerDensity, _fogIn * layerFog); color = Color.Lerp(color, FogLayerColor, _fogIn * layerFog); }
            float t = _inside * _inside * (3f - 2f * _inside);
            if (t > 0f) { density = Mathf.Lerp(density, InsideFogDensity, t); color = Color.Lerp(color, InsideFogColor, t); }
            // 번개 섬광: 안개(먼 곳·하늘 안개 돔)도 잠깐 밝게 — 물속은 받지 않는다
            if (_flash > 0f) { color = Color.Lerp(color, FlashFogColor, Mathf.Clamp01(_flash) * FlashFogAmount); }
            if (_underwater > 0f)
            {
                density = Mathf.Lerp(density, _underwaterDensity, _underwater);
                color = Color.Lerp(color, _underwaterColor, _underwater);
            }
            RenderSettings.fogDensity = density;
            RenderSettings.fogColor = color;
            _fogTouched = true;
        }

        private void RestoreBaseFog()
        {
            _fogDirty = false;
            if (!_fogTouched) { return; }
            RenderSettings.fogDensity = _baseDensity;
            RenderSettings.fogColor = _baseColor;
            _fogTouched = false;
        }

        private void CheckSeed()
        {
            int seed = Terrain ? Terrain.GetSeed() : 0;
            if (seed == 0) { seed = FallbackSeed; }
            if (seed != _placedSeed || Coverage != _placedCoverage) { Place(seed); }
        }

        /// 시드로 구름을 놓는다(모든 클라이언트가 같은 결과).
        public void Place(int seed)
        {
            _placedSeed = seed;
            _placedCoverage = Coverage;
            if (Clouds == null) { return; }
            Random.InitState(seed * 7919 + 104729);
            int count = Mathf.RoundToInt(Mathf.Clamp01(Coverage) * Clouds.Length);
            for (int i = 0; i < Clouds.Length; i++)
            {
                Transform c = Clouds[i];
                if (!c) { continue; }
                // 켜지 않는 구름도 같은 수의 난수를 써서 Coverage가 달라도 앞쪽 구름 자리는 그대로
                float ang = Random.value * Mathf.PI * 2f;
                float rad = Mathf.Sqrt(Random.value) * FieldRadius;
                float alt = Mathf.Lerp(MinAltitude, MaxAltitude, Random.value);
                float yaw = Random.value * 360f;
                float s = Mathf.Lerp(MinScale, MaxScale, Random.value);
                float sx = s * Mathf.Lerp(0.85f, 1.2f, Random.value);
                float sy = s * Mathf.Lerp(0.8f, 1.1f, Random.value);
                bool on = i < count;
                if (on)
                {
                    c.position = new Vector3(Mathf.Cos(ang) * rad, alt, Mathf.Sin(ang) * rad);
                    c.rotation = Quaternion.Euler(0f, yaw, 0f);
                    c.localScale = new Vector3(sx, sy, s);
                }
                if (c.gameObject.activeSelf != on) { c.gameObject.SetActive(on); }
            }
            Random.InitState(Networking.GetServerTimeInMilliseconds() ^ 0x5bd1e995);
        }

        /// 내 머리가 구름 속에 얼마나 들어갔나(0~1).
        private float InsideAmount()
        {
            if (!_hasHead || Clouds == null || CloudCoreCenters == null || CloudCoreExtents == null) { return 0f; }
            Vector3 head = _head;
            if (_deckFade > 0.5f && head.y < DeckTop) { return 0f; }   // 이 높이의 뭉게구름은 숨겨져 있다(층 안은 층 안개가 맡는다)
            float best = 0f;
            int n = Mathf.Min(Clouds.Length, Mathf.Min(CloudCoreCenters.Length, CloudCoreExtents.Length));
            float span = Mathf.Max(FogOuter - FogInner, 0.01f);
            for (int i = 0; i < n; i++)
            {
                Transform c = Clouds[i];
                if (!c || !c.gameObject.activeSelf) { continue; }
                Vector3 p = c.InverseTransformPoint(head) - CloudCoreCenters[i];
                Vector3 e = CloudCoreExtents[i];
                if (e.x <= 0f || e.y <= 0f || e.z <= 0f) { continue; }
                float len = new Vector3(p.x / e.x, p.y / e.y, p.z / e.z).magnitude;
                if (len >= FogOuter) { continue; }
                float t = Mathf.Clamp01((FogOuter - len) / span);
                if (t > best) { best = t; }
            }
            return best;
        }

        // ───────────── 날씨가 쓸 API ─────────────

        public void SetCoverage(float coverage)
        {
            Coverage = Mathf.Clamp01(coverage);
            CheckSeed();
        }

        /// 0 = 흰 구름, 1 = 폭풍 구름. 비구름층 아랫면(천장)은 비(0.65)보다 어두울 때만(폭풍) 더 어둡게.
        public void SetDarkness(float darkness)
        {
            _darkness = Mathf.Clamp01(darkness);
            ApplyCloudLight();
            if (_deckBottomMat) { _deckBottomMat.SetFloat("_Darken", Mathf.Clamp01((darkness - 0.65f) / 0.3f)); }
            if (_volumeMat) { _volumeMat.SetFloat("_Darken", Mathf.Clamp01((darkness - 0.65f) / 0.3f)); }
        }

        private float _darkness;
        private float _sunScale = 1f;

        /// 날씨가 줄인 태양 세기 배율. 층운이 있으면 뭉게구름이 이 값을 되돌린다.
        public void SetSunScale(float scale)
        {
            _sunScale = scale;
            ApplyCloudLight();
        }

        /// 뭉게구름 밝기: 층운이 있으면 보이는 구름이 층 위(해가 비치는 곳)라 흰 층 윗면과 맞게 날씨 어둡기·태양 감소를 뺀다.
        private void ApplyCloudLight()
        {
            if (!_material) { return; }
            float above = Mathf.Max(_deckFade, _fogFade);
            _material.SetFloat("_Darken", _darkness * (1f - above));
            _material.SetFloat("_SunGain", Mathf.Lerp(1f, 1f / Mathf.Max(_sunScale, 0.05f), above));
        }

        [Header("번개 섬광 (폭풍 — ACO_Lightning이 SetFlash로 넣는다)")]
        [Tooltip("섬광 때 안개 색이 이쪽으로(세기 × FlashFogAmount)")]
        public Color FlashFogColor = new Color(0.8f, 0.85f, 1f, 1f);
        [Range(0f, 1f)] public float FlashFogAmount = 0.45f;
        private float _flash;

        /// 번개 섬광(0 = 없음): 뭉게구름·구름층 판(번개 자리 pos 둘레가 안에서 빛나듯)·안개 색. 조명을 켜지 않는다 — 머티리얼 값만.
        public void SetFlash(float amount, Vector3 pos)
        {
            if (amount == _flash) { return; }
            _flash = amount;
            if (_material) { _material.SetFloat("_Flash", amount); }
            Vector4 p = new Vector4(pos.x, pos.y, pos.z, 0f);
            if (_deckBottomMat) { _deckBottomMat.SetFloat("_Flash", amount); _deckBottomMat.SetVector("_FlashPos", p); }
            if (_deckTopMat) { _deckTopMat.SetFloat("_Flash", amount * 0.6f); _deckTopMat.SetVector("_FlashPos", p); }
            if (_volumeMat) { _volumeMat.SetFloat("_Flash", amount); _volumeMat.SetVector("_FlashPos", p); }
            _fogDirty = true;
        }

        /// 구름 밖 안개(날씨). 구름 속·층이 아니면 바로 적용한다.
        public void SetBaseFog(float density, Color color)
        {
            _baseDensity = density;
            _baseColor = color;
            if (!_fogTouched)
            {
                RenderSettings.fogDensity = density;
                RenderSettings.fogColor = color;
            }
            else { _fogDirty = true; }   // 구름 속·층·물속이면 새 기본 위에 다시 겹친다
        }

        /// 구름층(LAYER_*). instant면 바로(처음 들어왔을 때), 아니면 LayerFadeTime 동안 바뀐다.
        public void SetLayer(int layer, bool instant)
        {
            _layer = layer;
            if (instant)
            {
                _deckFade = layer == LAYER_DECK ? 1f : 0f;
                _fogFade = layer == LAYER_FOG ? 1f : 0f;
            }
        }

        public int GetLayer() { return _layer; }
    }
}
