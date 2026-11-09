using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 비(로컬 연출, 동기화 없음). 빗방울 메시(셰이더 ACO/Rain)가 셰이더 안에서 카메라 둘레에 끝없이 반복되고,
    /// 이 스크립트는 프레임마다 머리 위치로 따라가며 머리의 속도(= 탄 기체 속도)를 머티리얼에 넘긴다 — 빗줄기가 상대 바람 방향으로 늘어난다.
    ///  - Intensity(0~1)로 켜고 끈다(FadeTime 동안 서서히). 0이면 렌더러·소리를 끄고 아무것도 안 한다.
    ///  - 지붕 아래와 MaxAltitude 위(구름 위)에서는 내리지 않는다. 지붕 = 머리 위로 쏜 선이 ShelterLayers(Default·Environment)에 닿음(0.2초마다),
    ///    또는 ShelterBoxes(손으로 놓은 상자) 안. 기체 콜라이더는 레이어 17·31이라 조종석 안에서는 걸리지 않는다.
    ///  - 캐노피 빗방울(CanopyRenderer, 셰이더 ACO/CanopyRain): 기체를 조종·탑승 중일 때(Contacts.LocalVehicle) 머리를 감싸는 구를 기체 자세로 돌려
    ///    방울이 캐노피에 붙어 흐르게 한다 — 느리면 맺혀 흘러내리고 빠르면 위로 길게 넘어간다. 비가 안 오는 곳(지붕 아래·구름층 위)에 가면 CanopyDryTime 동안 마른다.
    ///  - 날씨 비 세기를 VRChat 셰이더 전역으로 넘긴다(내가 지붕 아래·구름 위여도 날씨 값 — 바다·땅은 비를 맞고 있다):
    ///    _UdonACORain(바다 빗방울 파문 — 비 세기를 FadeTime으로 섞은 값), _UdonACOWet(젖은 땅·나무 — WetTime 동안 젖고 DryTime 동안 마른다).
    ///  - 날씨가 쓸 API: SetIntensity.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Rain : UdonSharpBehaviour
    {
        [Tooltip("빗방울 메시 렌더러(셰이더 ACO/Rain)")]
        public Renderer RainRenderer;
        [Range(0f, 1f)] public float Intensity = 0f;
        [Tooltip("켜고 끌 때 걸리는 시간(초)")]
        public float FadeTime = 3f;
        [Tooltip("이 고도(월드 Y) 위에서는 비가 안 온다(구름 위)")]
        public float MaxAltitude = 1500f;
        [Tooltip("지붕 아래 상자 — 이 트랜스폼의 단위 상자(위치·회전·크기 = 상자) 안에서는 비가 안 온다(격납고 등). 콜라이더가 아니라 물리에 영향 없음")]
        public Transform[] ShelterBoxes;
        [Tooltip("함내 구역. Bake가 넣는다. 격실 안은 지붕 아래로 친다")]
        public ACO_InteriorZone Interior;
        [Tooltip("머리 위로 선을 쏴 이 레이어(기본 Default·Environment)에 닿으면 지붕 아래 — 격납고·다리·공중 요새 밑. 0이면 안 쏜다(상자만)")]
        public LayerMask ShelterLayers = 2049;
        [Tooltip("지붕을 찾는 높이(m)")]
        public float ShelterRayLength = 200f;
        [Tooltip("지붕 확인 간격(초) — 사이에는 앞 결과를 쓴다(그 사이 2m 넘게 움직이면 다시 쏜다)")]
        public float ShelterCheckInterval = 0.2f;
        private Vector3 _roofPos;
        private float _roofAt = -100f;
        private bool _roof;

        [Header("소리 (선택)")]
        [Tooltip("빗소리 루프(2D 권장). 세기에 맞춰 볼륨이 바뀐다")]
        public AudioSource RainSound;
        public float SoundVolume = 0.6f;

        [Header("캐노피 빗방울")]
        [Tooltip("캐노피 빗방울을 그린다(기본 끔 — 지금 모양은 쓰지 않는다)")]
        public bool CanopyDrops = false;
        [Tooltip("머리를 감싸는 구(ACO/CanopyRain, 반경 0.9m — ACO_SkyBuilder가 만든다). 기체에 탔을 때만 켠다")]
        public Renderer CanopyRenderer;
        [Tooltip("내가 탄 기체를 알려 준다(Bake가 씬의 레이더 접촉 목록을 넣는다)")]
        public ACO_RadarContacts Contacts;
        [Tooltip("비가 안 오는 곳에 가면 캐노피가 마르는 시간(초)")]
        public float CanopyDryTime = 6f;

        [Header("젖은 땅·바다 (셰이더 전역)")]
        [Tooltip("비가 오기 시작해 땅이 다 젖는 시간(초)")]
        public float WetTime = 25f;
        [Tooltip("비가 그친 뒤 다 마르는 시간(초)")]
        public float DryTime = 60f;

        private Material _material;
        private Material _canopyMat;
        private float _current;
        private float _canopy;
        private Vector3 _lastHead;
        private bool _hasLast;
        private Vector3 _velocity;
        // 셰이더 전역(이름이 _Udon으로 시작해야 VRChat이 허락한다)
        private int _idRain;
        private int _idWet;
        private float _weatherRain;
        private float _wet;
        private float _sentRain = -1f;
        private float _sentWet = -1f;

        void Start()
        {
            if (RainRenderer)
            {
                _material = RainRenderer.material;
                RainRenderer.enabled = false;
            }
            if (CanopyRenderer)
            {
                _canopyMat = CanopyRenderer.material;
                CanopyRenderer.gameObject.SetActive(false);
            }
            if (RainSound) { RainSound.volume = 0f; }
            _idRain = VRCShader.PropertyToID("_UdonACORain");
            _idWet = VRCShader.PropertyToID("_UdonACOWet");
            SendGlobals();
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            UpdateGlobals(dt);

            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || !_material) { return; }
            if (Intensity <= 0f && _current <= 0f && _canopy <= 0f) { _hasLast = false; return; }

            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            float target = Intensity;
            if (head.y > MaxAltitude || InShelter(head)) { target = 0f; }
            _current = Mathf.MoveTowards(_current, target, dt / Mathf.Max(FadeTime, 0.01f));

            bool on = _current > 0.001f;
            if (RainRenderer.enabled != on) { RainRenderer.enabled = on; }
            if (RainSound)
            {
                RainSound.volume = _current * SoundVolume;
                if (on && !RainSound.isPlaying) { RainSound.Play(); }
                else if (!on && RainSound.isPlaying) { RainSound.Stop(); }
            }

            // 머리 속도(순간이동·리스폰은 무시)
            if (_hasLast)
            {
                Vector3 v = (head - _lastHead) / dt;
                if (v.sqrMagnitude < 1000000f) { _velocity = Vector3.Lerp(_velocity, v, 1f - Mathf.Exp(-dt * 10f)); }
            }
            _lastHead = head;
            _hasLast = true;

            UpdateCanopy(lp, head, target, dt);
            if (!on) { return; }
            transform.position = head;
            _material.SetVector("_CamVel", new Vector4(_velocity.x, _velocity.y, _velocity.z, 0f));
            _material.SetFloat("_Intensity", _current);
        }

        /// 캐노피 빗방울: 기체에 탔을 때만, 비 맞는 곳이면 빗방울이 늘고(FadeTime) 아니면 마른다(CanopyDryTime). 구는 머리 위치 + 기체 자세.
        private void UpdateCanopy(VRCPlayerApi lp, Vector3 head, float rainHere, float dt)
        {
            if (!CanopyRenderer || !_canopyMat) { return; }
            Transform vehicle = Contacts != null && Contacts.LocalVehicle != null ? Contacts.LocalVehicle.transform : null;
            float want = vehicle != null && CanopyDrops ? rainHere : 0f;
            float rate = want > _canopy ? 1f / Mathf.Max(FadeTime, 0.01f) : 1f / Mathf.Max(CanopyDryTime, 0.01f);
            _canopy = Mathf.MoveTowards(_canopy, want, dt * rate);
            bool on = _canopy > 0.001f && vehicle != null;
            if (CanopyRenderer.gameObject.activeSelf != on) { CanopyRenderer.gameObject.SetActive(on); }
            _canopyVehicle = on ? vehicle : null;
            if (!on) { return; }
            _canopyMat.SetFloat("_Intensity", _canopy);
            _canopyMat.SetFloat("_Speed", Vector3.Dot(_velocity, vehicle.forward));
        }

        private Transform _canopyVehicle;

        /// 캐노피 구는 머리 위치가 확정된 뒤 옮긴다(빠른 기체에서 머리가 구 밖으로 나가지 않게).
        public override void PostLateUpdate()
        {
            if (_canopyVehicle == null) { return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { return; }
            CanopyRenderer.transform.SetPositionAndRotation(lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, _canopyVehicle.rotation);
        }

        /// 바다 파문(날씨 비 세기를 FadeTime으로 섞은 값)·젖은 땅(WetTime으로 젖고 DryTime으로 마른다) — 바뀐 만큼만 보낸다.
        private void UpdateGlobals(float dt)
        {
            _weatherRain = Mathf.MoveTowards(_weatherRain, Intensity, dt / Mathf.Max(FadeTime, 0.01f));
            float wetTarget = Mathf.Clamp01(Intensity * 1.2f);
            float rate = wetTarget > _wet ? 1f / Mathf.Max(WetTime, 0.01f) : 1f / Mathf.Max(DryTime, 0.01f);
            _wet = Mathf.MoveTowards(_wet, wetTarget, dt * rate);
            if (Mathf.Abs(_weatherRain - _sentRain) > 0.002f || Mathf.Abs(_wet - _sentWet) > 0.002f) { SendGlobals(); }
        }

        private void SendGlobals()
        {
            _sentRain = _weatherRain;
            _sentWet = _wet;
            VRCShader.SetGlobalFloat(_idRain, _weatherRain);
            VRCShader.SetGlobalFloat(_idWet, _wet);
        }

        public bool InShelter(Vector3 p)
        {
            if (Interior != null && Interior.Contains(p)) { return true; }
            if (RoofAbove(p)) { return true; }
            if (ShelterBoxes == null) { return false; }
            for (int i = 0; i < ShelterBoxes.Length; i++)
            {
                Transform t = ShelterBoxes[i];
                if (!t) { continue; }
                Vector3 l = t.InverseTransformPoint(p);
                if (Mathf.Abs(l.x) <= 0.5f && Mathf.Abs(l.y) <= 0.5f && Mathf.Abs(l.z) <= 0.5f) { return true; }
            }
            return false;
        }

        /// 머리 위에 지붕(ShelterLayers)이 있는가. 같은 자리 근처면 ShelterCheckInterval 동안 앞 결과를 쓴다(관전 카메라와 번갈아 물어도 한 프레임 두 번이 고작).
        private bool RoofAbove(Vector3 p)
        {
            if (ShelterLayers.value == 0) { return false; }
            if (Time.time - _roofAt < ShelterCheckInterval && (p - _roofPos).sqrMagnitude < 4f) { return _roof; }
            _roofAt = Time.time;
            _roofPos = p;
            _roof = Physics.Raycast(p + Vector3.up * 0.3f, Vector3.up, ShelterRayLength, ShelterLayers.value, QueryTriggerInteraction.Ignore);
            return _roof;
        }

        // ───────────── 날씨가 쓸 API ─────────────

        public void SetIntensity(float intensity)
        {
            Intensity = Mathf.Clamp01(intensity);
        }
    }
}
