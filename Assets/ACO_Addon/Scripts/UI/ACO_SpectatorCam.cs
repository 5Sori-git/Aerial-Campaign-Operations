using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 관전 패널(로컬 전용, 동기화 없음): 큰 스크린에 플레이어 기체를 에이스컴뱃 리플레이처럼 찍어 보여 준다(실시간).
    ///  - 켜기/끄기(POWER)는 거리와 상관없이 직접 — 조종사가 VRChat 카메라를 스크린 앞에 두고 자기 비행을 녹화할 수 있게. 끄면 카메라도 꺼서 비용이 없다.
    ///    VRChat 카메라(스트림·사진)를 스크린 가까이 두면 그 카메라 화면 전체가 관전 화면이 된다(FullscreenOverlay — 셰이더 ACO/SpectatorFullscreen, 내 눈엔 안 보인다).
    ///  - 해상도(HD/FHD): 렌더 텍스처 두 장(TextureHD 1280x720 / TextureFHD 1920x1080) 중 하나를 카메라·스크린·전체 화면이 같이 쓴다.
    ///  - 대상(TARGET): AUTO(감독 — 미사일을 쏜 조종사로 옮기고, 조용하면 AutoTargetHold마다 다음 조종사) 또는 슬롯 직접 고르기. 플레이어 기체만(Bake가 슬롯 순서로 Vehicles에 넣는다).
    ///    슬롯은 비어 있어도 고를 수 있다 — 고른 슬롯에 조종사가 타면 그때부터 찍는다(자기 슬롯을 골라 두고 타러 갈 수 있다).
    ///  - 컷(SHOT): AUTO(감독 — 추적·플라이바이·망원·나란히·조종석을 ShotMin~ShotMax마다 바꾸고, 대상이 미사일을 쏘면 미사일 캠 → 명중 직전 표적 캠) 또는 직접 고르기.
    ///    CHASE 뒤에서 늦게 따라감 / FLYBY 앞길 옆에 카메라를 세워 지나가는 기체를 눈으로 쫓음 / TELE 1km 옆 망원(기체 크기 유지, 손떨림) /
    ///    SIDE 옆에서 앞→뒤로 돌며 나란히 / WINGMAN 편대원 시점 — 컷이 바뀐 순간의 기체 속도·방향을 그대로 이어 직진하며 옆의 기체를 찍는다(기체가 기동하면 화면 안에서 흘러간다) /
    ///    COCKPIT 조종사 어깨 뒤 / PILOT 캐노피 앞 비스듬히에서 조종사 얼굴(아바타 머리)을 봄 — 기체에 붙어 함께 움직인다 /
    ///    MISSILE 대상이 쏜 미사일 뒤에서 표적을 봄 / TARGET 미사일의 표적 옆에서 날아드는 미사일·폭발을 봄.
    ///  - 카메라는 지면·해수면 위 MinGroundClearance를 지킨다. UI 레이어(HUD·마커)와 FX 레이어(내 머리를 따라다니는 하늘 안개 돔·물안개 막·비·물빛 벽 — Bake가 옮긴다)는 그리지 않는다.
    ///    하늘 안개 돔은 카메라 자식(SkyFogDome, 레이어 18 MirrorReflection — 메인 카메라는 그리지 않는다)을 따로 쓴다. 비도 같은 방법으로 따로 쓴다(RainRenderer) —
    ///    내 머리가 아니라 관전 카메라 자리로 내릴지 정하므로, 내가 격납고에 있어도 밖을 찍는 카메라에는 비가 온다.
    ///  - 미사일: 플레이어 무장(ACO_DFUNC_Missile)이 발사할 때 NotifyMissile을 부른다(모든 클라이언트 — 발사 이벤트가 모두에게 온다).
    ///  - 소리는 없다(오디오 리스너는 플레이어에게 있다). 씬 안개는 전역이라 내가 구름·물속이면 화면도 짙어진다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_SpectatorCam : UdonSharpBehaviour
    {
        public const int ACT_POWER = 0;
        public const int ACT_TARGET = 1;
        public const int ACT_SHOT = 2;
        public const int ACT_RESOLUTION = 3;

        public const int AUTO = -1;
        public const int SHOT_CHASE = 0;
        public const int SHOT_FLYBY = 1;
        public const int SHOT_TELE = 2;
        public const int SHOT_SIDE = 3;
        public const int SHOT_WINGMAN = 4;
        public const int SHOT_COCKPIT = 5;
        public const int SHOT_PILOT = 6;
        public const int SHOT_MISSILE = 7;
        public const int SHOT_TARGET = 8;
        public const int SHOT_COUNT = 9;

        [Header("연결 (프리팹 빌더·Bake)")]
        public Camera Cam;
        [Tooltip("렌더 텍스처를 보여 주는 스크린")]
        public Renderer Screen;
        [Tooltip("켜져 있을 때 스크린 머티리얼(렌더 텍스처)")]
        public Material OnMaterial;
        [Tooltip("꺼져 있거나 비행 중인 조종사가 없을 때")]
        public Material OffMaterial;
        [Tooltip("관전 카메라 전용 하늘 안개 돔(카메라 자식, 레이어 18). Bake가 하늘 안개 돔 머티리얼을 넣는다")]
        public Renderer SkyFogDome;
        [Tooltip("관전 카메라 전용 체적 구름(카메라 자식, 레이어 18). 켜고 끄기와 머티리얼은 ACO_CloudField가 맡는다")]
        public Renderer CloudVolume;
        [Tooltip("해상도(HD/FHD 버튼): 미리 만든 렌더 텍스처 두 장 — Udon은 실행 중에 렌더 텍스처를 만들 수 없다")]
        public RenderTexture TextureHD;
        public RenderTexture TextureFHD;
        [Tooltip("처음 해상도(켜면 FHD 1920x1080, 끄면 HD 1280x720)")]
        public bool StartFHD = false;
        [Tooltip("스트림 카메라용 전체 화면(셰이더 ACO/SpectatorFullscreen — VRChat 카메라가 스크린 가까이 오면 그 카메라 화면 전체가 관전 화면). 켜져 있고 찍는 중일 때만 켠다")]
        public Renderer FullscreenOverlay;
        [Tooltip("관전할 플레이어 기체(Bake: ACO_VehicleUpgrades가 있는 SaccEntity 전부)")]
        public SaccEntity[] Vehicles;
        [Tooltip("슬롯 이름·명단(Bake가 넣는다). 비면 SLOT 번호로 보인다")]
        public ACO_GameFlow Flow;
        [Tooltip("날씨 비(Bake가 넣는다) — 세기·구름 위·지붕 아래 판정을 쓴다")]
        public ACO_Rain Rain;
        [Tooltip("관전 카메라 전용 빗방울(카메라 자식, 레이어 18 — Bake가 만든다)")]
        public Renderer RainRenderer;
        [Tooltip("Bake: 미션 생성기 해수면·지면 레이어")]
        public float SeaLevel = 0f;
        public LayerMask GroundLayers = 2049;

        [Header("패널 글자")]
        public TextMeshProUGUI PowerText;
        public TextMeshProUGUI TargetText;
        public TextMeshProUGUI ShotText;
        public TextMeshProUGUI StatusText;
        public TextMeshProUGUI ResolutionText;

        [Header("감독")]
        [Tooltip("AUTO 컷 길이(초)")]
        public float ShotMin = 4f;
        public float ShotMax = 7f;
        [Header("AUTO 컷 가중치 (클수록 자주, 0이면 안 나옴)")]
        [Tooltip("비행 중(30m/s 이상) — 추적")] public float WeightChase = 3f;
        [Tooltip("비행 중 — 플라이바이(세워 둔 카메라 옆을 지나감)")] public float WeightFlyby = 3f;
        [Tooltip("비행 중 — 망원(멀리서 좁은 화각)")] public float WeightTele = 2f;
        [Tooltip("비행 중 — 나란히(옆에서 앞 → 뒤로 돌며)")] public float WeightSide = 2f;
        [Tooltip("비행 중 — 편대원(옆에서 같은 속도로 직진)")] public float WeightWingman = 2f;
        [Tooltip("비행 중 — 조종석(어깨 뒤)")] public float WeightCockpit = 1f;
        [Tooltip("비행 중 — 조종사(캐노피 앞에서 얼굴)")] public float WeightPilot = 1.5f;
        [Tooltip("지상·느릴 때(30m/s 아래) — 추적")] public float SlowWeightChase = 3f;
        [Tooltip("지상·느릴 때 — 조종석")] public float SlowWeightCockpit = 1f;
        [Tooltip("지상·느릴 때 — 조종사")] public float SlowWeightPilot = 1.5f;
        [Tooltip("AUTO 대상: 아무 일 없으면 이 시간(초)마다 다음 조종사")]
        public float AutoTargetHold = 25f;
        [Tooltip("AUTO 컷: 대상이 미사일을 쏘면 미사일 캠으로 갈 확률, 미사일·표적 캠이 끝난 뒤 이 시간(초) 안에는 다시 안 간다")]
        [Range(0f, 1f)] public float MissileCutChance = 0.75f;
        public float MissileCutCooldown = 4f;
        [Tooltip("표적 캠: 명중까지 이 시간(초) 남으면 표적 옆으로, 폭발 뒤 이만큼(초) 머문다")]
        public float ImpactLead = 1.1f;
        public float ImpactHold = 3f;
        [Tooltip("AUTO 미사일 컷: 표적에서 멀리서 끝났을 때(요격·플레어·회피) 그 자리에서 터진 곳을 보는 시간(초)")]
        public float MissHold = 1f;

        [Header("컷 값")]
        public float Fov = 55f;
        public float ChaseDistance = 32f;
        public float ChaseHeight = 7f;
        [Tooltip("플라이바이: 이 시간(초) 뒤 기체가 올 자리 옆에 카메라를 세운다")]
        public float FlybyLead = 2.5f;
        public float FlybySideMin = 25f;
        public float FlybySideMax = 70f;
        public float TeleDistanceMin = 700f;
        public float TeleDistanceMax = 1400f;
        [Tooltip("망원: 화면 가로에 담기는 폭(m) — 거리에 따라 화각을 줄여 기체 크기를 유지한다")]
        public float TeleFrame = 45f;
        public float SideDistance = 45f;
        [Tooltip("편대원: 옆 거리(m) 범위 — 컷이 바뀐 순간의 기체 속도로 직진한다. 기체가 이만큼(m) 넘게 멀어지면 컷을 끝낸다")]
        public float WingmanDistanceMin = 30f;
        public float WingmanDistanceMax = 60f;
        public float WingmanBreakDistance = 600f;
        [Tooltip("조종석: 기체 기준 카메라 자리(m) — 기체마다 다르면 고친다")]
        public Vector3 CockpitOffset = new Vector3(0f, 2.4f, -9f);
        [Tooltip("조종사 컷: 조종사 머리 기준 카메라 자리(m, 기체 축 — 앞 Z, 위 Y, 옆 X는 컷마다 좌우 무작위). 캐노피 밖 앞쪽 비스듬히")]
        public Vector3 PilotCamOffset = new Vector3(0.45f, 0.2f, 1.3f);
        public float PilotFov = 48f;
        public float MissileBack = 8f;
        public float TargetSideMin = 60f;
        public float TargetSideMax = 120f;
        [Tooltip("카메라가 지면·해수면 위로 지키는 높이(m)")]
        public float MinGroundClearance = 4f;

        private bool _on;
        private int _targetMode = AUTO;
        private int _shotMode = AUTO;
        private int _subject = -1;
        private float _subjectSince;
        private int _shot = SHOT_CHASE;
        private float _shotTimer;
        private float _shotLen = 5f;
        private int _lastRandomShot = -1;
        // 무작위로 고르는 컷(SHOT_CHASE ~ SHOT_PILOT — 미사일·표적 컷은 미사일을 쏠 때만)
        private const int RANDOM_SHOTS = 7;
        private float[] _shotWeights = new float[7];
        private float _missileCutEnd = -100f;

        // 대상 움직임(원격 기체는 리지드바디 속도를 믿을 수 없어 위치 차로 잰다)
        private Vector3 _subPos;
        private Vector3 _subVel;
        private bool _hasSub;
        private Quaternion _smoothRot = Quaternion.identity;

        // 카메라
        private Vector3 _camPos;
        private Quaternion _camRot = Quaternion.identity;
        private Vector3 _fixedPos;
        private float _side = 1f;
        private float _fov = 55f;
        private Vector3 _wingVel;   // 편대원 컷: 컷이 바뀐 순간의 기체 속도(그대로 직진)

        // 미사일(조종사마다 마지막으로 쏜 것)
        private ACO_Missile[] _lastMissile;
        private float[] _lastMissileTime;
        private ACO_Missile _mis;
        private Transform _misTarget;
        private Vector3 _misTargetPos;
        private Vector3 _misTargetVel;
        private Vector3 _impactPos;
        private bool _misDone;
        private float _misDoneTime;

        private float _textTimer;
        private bool _fhd;
        private Material _screenMat;    // 스크린 머티리얼 복사본(해상도에 따라 텍스처를 바꾼다)
        private Material _overlayMat;

        void Start()
        {
            int n = Vehicles != null ? Vehicles.Length : 0;
            _lastMissile = new ACO_Missile[n];
            _lastMissileTime = new float[n];
            for (int i = 0; i < n; i++) { _lastMissileTime[i] = -100f; }
            if (Screen && OnMaterial)
            {
                Screen.sharedMaterial = OnMaterial;
                _screenMat = Screen.material;
            }
            if (FullscreenOverlay) { _overlayMat = FullscreenOverlay.material; }
            // 체적 구름은 시선을 깊이로 멈춘다. 그림자가 없는 고공에서도 이 카메라의 깊이가 있어야 한다
            if (Cam) { Cam.depthTextureMode = DepthTextureMode.Depth; }
            if (RainRenderer)
            {
                _rainMat = RainRenderer.material;
                RainRenderer.enabled = false;
            }
            SetResolution(StartFHD);
            SetPower(false);
        }

        /// 해상도: 카메라·스크린·전체 화면이 같은 렌더 텍스처를 쓴다. 텍스처가 없으면 그대로.
        private void SetResolution(bool fhd)
        {
            RenderTexture rt = fhd ? TextureFHD : TextureHD;
            if (!rt) { return; }
            _fhd = fhd;
            if (Cam) { Cam.targetTexture = rt; }
            if (_screenMat) { _screenMat.SetTexture("_MainTex", rt); }
            if (_overlayMat) { _overlayMat.SetTexture("_MainTex", rt); }
        }

        private void SetOverlay(bool on)
        {
            if (FullscreenOverlay && FullscreenOverlay.gameObject.activeSelf != on) { FullscreenOverlay.gameObject.SetActive(on); }
        }

        // ───────────── 패널 ─────────────

        /// 패널 버튼(ACO_SpectatorButton)이 부른다. step = ±1(대상·컷 넘기기). 효과음 종류를 돌려준다.
        public int Press(int action, int step)
        {
            if (action == ACT_POWER) { SetPower(!_on); return _on ? ACO_UiSound.KIND_CONFIRM : ACO_UiSound.KIND_CLICK; }
            if (action == ACT_RESOLUTION)
            {
                RenderTexture next = _fhd ? TextureHD : TextureFHD;
                if (!next) { return ACO_UiSound.KIND_DENY; }
                SetResolution(!_fhd);
                UpdateTexts();
                return ACO_UiSound.KIND_CLICK;
            }
            if (action == ACT_TARGET)
            {
                int n = Vehicles != null ? Vehicles.Length : 0;
                // AUTO → 슬롯 0 → 1 … → AUTO 순으로 돈다(비어 있는 슬롯도 고른다 — 조종사가 타면 찍기 시작한다)
                int mode = _targetMode + (step >= 0 ? 1 : -1);
                if (mode >= n) { mode = AUTO; }
                if (mode < AUTO) { mode = n - 1; }
                _targetMode = mode;
                if (mode != AUTO && mode != _subject && IsValid(mode)) { SetSubject(mode); }
                UpdateTexts();
                return ACO_UiSound.KIND_CLICK;
            }
            if (action == ACT_SHOT)
            {
                int mode = _shotMode + (step >= 0 ? 1 : -1);
                if (mode >= SHOT_COUNT) { mode = AUTO; }
                if (mode < AUTO) { mode = SHOT_COUNT - 1; }
                _shotMode = mode;
                if (mode != AUTO) { StartShot(mode); }
                else { NextRandomShot(); }
                UpdateTexts();
                return ACO_UiSound.KIND_CLICK;
            }
            return ACO_UiSound.KIND_DENY;
        }

        private void SetPower(bool on)
        {
            _on = on;
            if (!on)
            {
                if (Cam) { Cam.enabled = false; }
                if (Screen && OffMaterial) { Screen.sharedMaterial = OffMaterial; }
                SetOverlay(false);
                if (RainRenderer) { RainRenderer.enabled = false; }
            }
            else
            {
                _subject = -1;
                _hasSub = false;
            }
            if (SkyFogDome) { SkyFogDome.gameObject.SetActive(on); }
            UpdateTexts();
        }

        public bool IsOn() { return _on; }

        // ───────────── 미사일 ─────────────

        /// 플레이어 무장이 미사일을 쐈다(모든 클라이언트). 꺼져 있으면 아무것도 안 한다.
        public void NotifyMissile(ACO_Missile missile)
        {
            if (!_on || missile == null || Vehicles == null) { return; }
            int v = -1;
            for (int i = 0; i < Vehicles.Length; i++) { if (Vehicles[i] == missile.EntityControl) { v = i; break; } }
            if (v < 0) { return; }
            _lastMissile[v] = missile;
            _lastMissileTime[v] = Time.time;
            bool busy = _shot == SHOT_MISSILE || (_shot == SHOT_TARGET && !_misDone);
            // AUTO 대상: 지금 대상이 미사일 컷 중이 아니면 쏜 조종사로 옮긴다
            if (_targetMode == AUTO && v != _subject && !busy) { SetSubject(v); }
            if (v != _subject) { return; }
            // 미사일은 풀에서 다시 쓰이므로 같은 오브젝트여도 새 비행으로 잡게 비우고 시작한다
            if (_shotMode == SHOT_MISSILE || _shotMode == SHOT_TARGET) { _mis = null; StartShot(_shotMode); return; }
            if (_shotMode != AUTO || busy) { return; }
            if (Time.time - _missileCutEnd < MissileCutCooldown || Random.value > MissileCutChance) { return; }
            _mis = null;
            StartShot(SHOT_MISSILE);
        }

        // ───────────── 매 프레임 ─────────────

        public override void PostLateUpdate()
        {
            if (!_on || !Cam) { return; }
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            UpdateSubject();
            bool live = _subject >= 0;
            if (Cam && Cam.enabled != live) { Cam.enabled = live; }
            if (Screen)
            {
                Material want = live ? (_screenMat ? _screenMat : OnMaterial) : OffMaterial;
                if (want && Screen.sharedMaterial != want) { Screen.sharedMaterial = want; }
            }
            SetOverlay(live);
            _textTimer -= dt;
            if (_textTimer <= 0f) { _textTimer = 0.5f; UpdateTexts(); }
            if (!live)
            {
                if (RainRenderer && RainRenderer.enabled) { RainRenderer.enabled = false; }
                return;
            }

            TrackSubject(dt);
            TrackMissile(dt);
            _shotTimer += dt;
            Direct();
            Shoot(dt);
            UpdateRain(dt);
        }

        // 관전 카메라의 비
        private Material _rainMat;
        private float _rainNow;
        private Vector3 _rainLast;
        private bool _rainHasLast;
        private Vector3 _rainVel;

        /// 비: 날씨 세기로 내리되 구름 위·지붕 아래는 카메라 자리로 판단한다. 빗줄기는 카메라 속도(컷이 바뀐 순간이동은 빼고)로 늘어난다.
        private void UpdateRain(float dt)
        {
            if (!Rain || !RainRenderer || !_rainMat) { return; }
            Vector3 p = Cam.transform.position;
            float target = p.y > Rain.MaxAltitude || Rain.InShelter(p) ? 0f : Rain.Intensity;
            _rainNow = Mathf.MoveTowards(_rainNow, target, dt / Mathf.Max(Rain.FadeTime, 0.01f));
            bool on = _rainNow > 0.001f;
            if (RainRenderer.enabled != on) { RainRenderer.enabled = on; }
            if (!on) { _rainHasLast = false; return; }
            if (_rainHasLast)
            {
                Vector3 v = (p - _rainLast) / dt;
                if (v.sqrMagnitude < 1000000f) { _rainVel = Vector3.Lerp(_rainVel, v, 1f - Mathf.Exp(-dt * 10f)); }
            }
            _rainLast = p;
            _rainHasLast = true;
            _rainMat.SetVector("_CamVel", new Vector4(_rainVel.x, _rainVel.y, _rainVel.z, 0f));
            _rainMat.SetFloat("_Intensity", _rainNow);
        }

        private bool IsValid(int i)
        {
            if (Vehicles == null || i < 0 || i >= Vehicles.Length) { return false; }
            SaccEntity e = Vehicles[i];
            return e != null && e.gameObject.activeInHierarchy && e.Occupied && !e.dead;
        }

        /// 대상 고르기: 직접 고른 슬롯(조종사가 없으면 화면 꺼짐, 타면 찍는다), AUTO면 유지하다가 AutoTargetHold마다 다음 조종사.
        private void UpdateSubject()
        {
            if (_targetMode != AUTO)
            {
                if (IsValid(_targetMode)) { if (_subject != _targetMode) { SetSubject(_targetMode); } }
                else { _subject = -1; }
                return;
            }
            bool busy = _shot == SHOT_MISSILE || (_shot == SHOT_TARGET && !_misDone);
            if (IsValid(_subject) && (busy || Time.time - _subjectSince < AutoTargetHold)) { return; }
            int n = Vehicles != null ? Vehicles.Length : 0;
            for (int k = 1; k <= n; k++)
            {
                int i = ((_subject < 0 ? -1 : _subject) + k + n) % n;
                if (IsValid(i)) { if (i != _subject) { SetSubject(i); } else { _subjectSince = Time.time; } return; }
            }
            _subject = -1;
        }

        private void SetSubject(int i)
        {
            _subject = i;
            _subjectSince = Time.time;
            _hasSub = false;
            if (_shotMode == AUTO) { NextRandomShot(); } else { StartShot(_shotMode); }
        }

        private void TrackSubject(float dt)
        {
            Transform t = Vehicles[_subject].transform;
            Vector3 p = t.position;
            if (!_hasSub || (p - _subPos).sqrMagnitude > 250000f)
            {
                _hasSub = true;
                _subPos = p;
                _subVel = Vector3.zero;
                _smoothRot = t.rotation;
                return;
            }
            Vector3 v = (p - _subPos) / dt;
            _subVel = Vector3.Lerp(_subVel, v, 1f - Mathf.Exp(-dt * 5f));
            _subPos = p;
            _smoothRot = Quaternion.Slerp(_smoothRot, t.rotation, 1f - Mathf.Exp(-dt * 3f));
        }

        /// 지금 컷의 미사일: 날고 있는 동안 표적 위치·속도를 재고, 끝나면 터진 자리를 기억한다.
        private void TrackMissile(float dt)
        {
            if (_mis == null || _misDone) { return; }
            if (!_mis.gameObject.activeInHierarchy || !_mis.IsFlying())
            {
                _misDone = true;
                _misDoneTime = Time.time;
                _impactPos = _mis.transform.position;
                return;
            }
            _impactPos = _mis.transform.position;
            if (_misTarget != null && _misTarget.gameObject.activeInHierarchy)
            {
                Vector3 tp = _misTarget.position;
                _misTargetVel = Vector3.Lerp(_misTargetVel, (tp - _misTargetPos) / dt, 1f - Mathf.Exp(-dt * 5f));
                _misTargetPos = tp;
            }
        }

        // ───────────── 감독 ─────────────

        private void Direct()
        {
            if (_shot == SHOT_MISSILE)
            {
                // 직접 고른 미사일 캠은 미사일이 없으면 추적으로 대신하며 다음 미사일을 기다린다
                if (_mis == null) { if (_shotMode == AUTO) { EndMissileCut(); } return; }
                if (_misDone)
                {
                    if (_shotMode == AUTO)
                    {
                        // 표적 가까이서 터졌으면 표적 캠으로 폭발을, 멀리서 끝났으면(요격·플레어·회피) 그 자리에서 MissHold 동안 터진 곳을 보고 다음 컷
                        bool near = _misTarget != null && (_impactPos - _misTargetPos).sqrMagnitude < 90000f;
                        if (near) { StartShot(SHOT_TARGET); }
                        else if (Time.time - _misDoneTime > MissHold) { EndMissileCut(); }
                    }
                    else if (Time.time - _misDoneTime > ImpactHold) { _mis = null; _misTarget = null; }   // 터진 자리를 잠시 보고 다음 미사일을 기다린다
                    return;
                }
                // 명중 직전이면 표적 옆으로(AUTO 컷만 — 직접 고른 미사일 캠은 끝까지 따라간다)
                if (_shotMode == AUTO && _misTarget != null && _shotTimer > 1.2f && TimeToImpact() < ImpactLead) { StartShot(SHOT_TARGET); }
                return;
            }
            if (_shot == SHOT_TARGET)
            {
                if (_mis == null) { if (_shotMode == AUTO) { EndMissileCut(); } return; }
                if (_misDone && Time.time - _misDoneTime > ImpactHold)
                {
                    if (_shotMode == AUTO) { EndMissileCut(); }
                    else { _mis = null; _misTarget = null; }   // 직접 고른 표적 캠: 다음 미사일을 기다린다
                }
                else if (_shotTimer > 15f && _shotMode == AUTO) { EndMissileCut(); }
                return;
            }
            if (_shotMode != AUTO)
            {
                // 플라이바이는 기체가 지나가면 다시 세우고, 편대원은 기체가 멀어지면 다시 옆에 붙는다
                if (_shot == SHOT_FLYBY && FlybyDone()) { StartShot(SHOT_FLYBY); }
                else if (_shot == SHOT_WINGMAN && WingmanDone()) { StartShot(SHOT_WINGMAN); }
                return;
            }
            if (_shotTimer >= _shotLen || (_shot == SHOT_FLYBY && FlybyDone()) || (_shot == SHOT_WINGMAN && WingmanDone())) { NextRandomShot(); }
        }

        private void EndMissileCut()
        {
            _missileCutEnd = Time.time;
            NextRandomShot();
        }

        /// 무작위 컷: 인스펙터 가중치(Weight* — 비행 중, SlowWeight* — 지상·30m/s 아래, 나머지 컷은 느릴 때 안 나온다)로 고른다.
        /// 바로 앞 컷은 빼고 고른다(같은 컷 연속 없이) — 가중치가 있는 컷이 그것 하나뿐이면 그대로. 가중치가 모두 0이면 추적.
        private void NextRandomShot()
        {
            bool slow = _subVel.magnitude < 30f;
            _shotWeights[SHOT_CHASE] = slow ? SlowWeightChase : WeightChase;
            _shotWeights[SHOT_FLYBY] = slow ? 0f : WeightFlyby;
            _shotWeights[SHOT_TELE] = slow ? 0f : WeightTele;
            _shotWeights[SHOT_SIDE] = slow ? 0f : WeightSide;
            _shotWeights[SHOT_WINGMAN] = slow ? 0f : WeightWingman;
            _shotWeights[SHOT_COCKPIT] = slow ? SlowWeightCockpit : WeightCockpit;
            _shotWeights[SHOT_PILOT] = slow ? SlowWeightPilot : WeightPilot;
            float total = 0f;
            float totalAll = 0f;
            for (int s = 0; s < RANDOM_SHOTS; s++)
            {
                float w = Mathf.Max(_shotWeights[s], 0f);
                totalAll += w;
                if (s != _lastRandomShot) { total += w; }
            }
            bool skipLast = total > 0f;
            if (!skipLast) { total = totalAll; }
            int pick = SHOT_CHASE;
            if (total > 0f)
            {
                float r = Random.value * total;
                for (int s = 0; s < RANDOM_SHOTS; s++)
                {
                    if (skipLast && s == _lastRandomShot) { continue; }
                    float w = Mathf.Max(_shotWeights[s], 0f);
                    if (w <= 0f) { continue; }
                    pick = s;
                    if (r < w) { break; }
                    r -= w;
                }
            }
            _lastRandomShot = pick;
            StartShot(pick);
            _shotLen = Random.Range(ShotMin, Mathf.Max(ShotMax, ShotMin));
        }

        private float TimeToImpact()
        {
            if (_mis == null || _misTarget == null) { return 999f; }
            float speed = Mathf.Max(_mis.GetVelocity().magnitude, 1f);
            return Vector3.Distance(_mis.transform.position, _misTargetPos) / speed;
        }

        /// 편대원 컷: 기체가 기동해 WingmanBreakDistance 넘게 멀어졌다.
        private bool WingmanDone()
        {
            return _shotTimer > 0.5f && (_subPos - _fixedPos).sqrMagnitude > WingmanBreakDistance * WingmanBreakDistance;
        }

        private bool FlybyDone()
        {
            Vector3 d = _subPos - _fixedPos;
            return _shotTimer > FlybyLead + 2.5f || (Vector3.Dot(d, _subVel) > 0f && d.magnitude > 350f);
        }

        // ───────────── 컷 시작 ─────────────

        private void StartShot(int shot)
        {
            if (_subject < 0) { _shot = shot; return; }
            if (!_hasSub) { TrackSubject(0.02f); }
            Transform t = Vehicles[_subject].transform;
            Vector3 fwd = Flat(_subVel.sqrMagnitude > 25f ? _subVel : t.forward);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            _side = Random.value < 0.5f ? -1f : 1f;
            _shotTimer = 0f;
            _fov = Fov;

            if (shot == SHOT_MISSILE || shot == SHOT_TARGET)
            {
                ACO_Missile m = _lastMissile != null && _subject < _lastMissile.Length ? _lastMissile[_subject] : null;
                if (shot == SHOT_TARGET && _shot == SHOT_MISSILE && _mis != null) { m = _mis; }   // 미사일 캠 → 같은 미사일의 표적
                if (m == null || (m != _mis && !m.IsFlying()))
                {
                    // 날고 있는 미사일이 없다 — 추적으로 대신하며 기다린다
                    _shot = shot;
                    _mis = null;
                    _misTarget = null;
                    _misDone = true;
                    SetChase();
                    return;
                }
                if (m != _mis)
                {
                    _mis = m;
                    _misTarget = m.GetTargetTransform();
                    _misDone = false;
                    _misDoneTime = 0f;
                    _impactPos = m.transform.position;
                    if (_misTarget != null) { _misTargetPos = _misTarget.position; }
                    _misTargetVel = Vector3.zero;
                }
            }

            _shot = shot;
            if (shot == SHOT_CHASE) { SetChase(); }
            else if (shot == SHOT_FLYBY)
            {
                Vector3 ahead = _subPos + _subVel * FlybyLead;
                _fixedPos = ahead + right * _side * Random.Range(FlybySideMin, FlybySideMax) + Vector3.up * Random.Range(-5f, 20f);
                _fixedPos = Clearance(_fixedPos);
                _camPos = _fixedPos;
                _camRot = Quaternion.LookRotation(_subPos - _camPos);
                _fov = 50f;
            }
            else if (shot == SHOT_TELE)
            {
                _fixedPos = _subPos + right * _side * Random.Range(TeleDistanceMin, TeleDistanceMax)
                    + Vector3.up * Random.Range(30f, 220f) + fwd * Random.Range(-400f, 400f);
                _fixedPos = Clearance(_fixedPos);
                _camPos = _fixedPos;
                _camRot = Quaternion.LookRotation(_subPos - _camPos);
            }
            else if (shot == SHOT_WINGMAN)
            {
                // 지금 기체 속도·방향을 그대로 이어 직진한다(편대원이 나란히 날며 찍는 것처럼). 느리면 기수 방향으로
                _wingVel = _subVel.sqrMagnitude > 25f ? _subVel : t.forward * 30f;
                _fixedPos = _subPos + right * _side * Random.Range(WingmanDistanceMin, WingmanDistanceMax)
                    + Vector3.up * Random.Range(-8f, 10f) + fwd * Random.Range(-15f, 25f);
                _fixedPos = Clearance(_fixedPos);
                _camPos = _fixedPos;
                _camRot = Quaternion.LookRotation(_subPos - _camPos);
                _fov = 45f;
            }
            else if (shot == SHOT_SIDE || shot == SHOT_COCKPIT) { _smoothRot = t.rotation; }
            else if (shot == SHOT_PILOT) { _smoothRot = t.rotation; _fov = PilotFov; }
            else if (shot == SHOT_MISSILE)
            {
                if (_mis != null) { _camPos = _mis.transform.position - _mis.transform.forward * MissileBack; _camRot = _mis.transform.rotation; }
                _fov = 60f;
            }
            else if (shot == SHOT_TARGET)
            {
                Vector3 tp = _misTarget != null ? _misTargetPos : _impactPos;
                Vector3 from = _mis != null ? _mis.transform.position : _subPos;
                Vector3 d = tp - from;
                d = d.sqrMagnitude > 1f ? d.normalized : fwd;
                Vector3 perp = Vector3.Cross(d, Vector3.up);
                perp = perp.sqrMagnitude > 0.01f ? perp.normalized : right;
                _fixedPos = tp + perp * _side * Random.Range(TargetSideMin, TargetSideMax) - d * Random.Range(20f, 50f) + Vector3.up * Random.Range(10f, 30f);
                _fixedPos = Clearance(_fixedPos);
                _camPos = _fixedPos;
                _camRot = Quaternion.LookRotation(tp - _camPos);
                _fov = 45f;
            }
        }

        private void SetChase()
        {
            _smoothRot = Vehicles[_subject].transform.rotation;
            _camPos = _subPos + _smoothRot * new Vector3(0f, ChaseHeight, -ChaseDistance);
            _camRot = Quaternion.LookRotation(_subPos - _camPos, _smoothRot * Vector3.up);
        }

        // ───────────── 카메라 ─────────────

        private void Shoot(float dt)
        {
            Transform t = Vehicles[_subject].transform;
            Vector3 lookAt = _subPos;
            Vector3 up = Vector3.up;
            float follow = 10f;
            int shot = _shot;
            // 미사일이 없는 미사일·표적 컷(직접 고른 경우 — 다음 미사일을 기다리는 중)은 추적으로 대신한다
            if ((shot == SHOT_MISSILE || shot == SHOT_TARGET) && _mis == null) { shot = SHOT_CHASE; }

            if (shot == SHOT_CHASE)
            {
                _camPos = _subPos + _smoothRot * new Vector3(Mathf.Sin(Time.time * 0.35f) * 4f, ChaseHeight, -ChaseDistance);
                lookAt = _subPos + t.forward * 30f;
                up = _smoothRot * Vector3.up;
                follow = 8f;
            }
            else if (shot == SHOT_FLYBY)
            {
                _camPos = _fixedPos;
                follow = 14f;
            }
            else if (shot == SHOT_TELE)
            {
                _fixedPos += _subVel * (dt * 0.97f);   // 거의 같이 움직여 오래 담는다
                _camPos = _fixedPos;
                float dist = Vector3.Distance(_camPos, _subPos);
                _fov = Mathf.Clamp(2f * Mathf.Atan(TeleFrame * 0.5f / Mathf.Max(dist, 1f)) * Mathf.Rad2Deg, 2f, 30f);
                follow = 6f;
            }
            else if (shot == SHOT_SIDE)
            {
                Vector3 fwd = Flat(_subVel.sqrMagnitude > 25f ? _subVel : t.forward);
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                float a = Mathf.Lerp(55f, 125f, Mathf.Clamp01(_shotTimer / Mathf.Max(_shotLen, 1f))) * Mathf.Deg2Rad;
                _camPos = _subPos + (fwd * Mathf.Cos(a) + right * _side * Mathf.Sin(a)) * SideDistance + Vector3.up * 5f;
                follow = 12f;
            }
            else if (shot == SHOT_WINGMAN)
            {
                // 고정 속도로 직진 + 편대원 기체의 살짝 흔들림(위아래·좌우 몇 m). 카메라는 옆 기체를 눈으로 쫓는다
                _fixedPos += _wingVel * dt;
                if (_wingVel.y < 0f) { _fixedPos = Clearance(_fixedPos); }   // 내려가며 직진하면 땅·바다 위에서 멈춘다
                float bob = Time.time * 0.6f;
                _camPos = _fixedPos + new Vector3(Mathf.Sin(bob * 1.3f) * 1.5f, Mathf.Sin(bob) * 2f, 0f);
                follow = 7f;
            }
            else if (shot == SHOT_PILOT)
            {
                // 조종사 머리(아바타 — 머리 뼈, 없으면 머리 추적)를 캐노피 앞 비스듬히에서. 기체 축에 붙어 같이 움직이고(흔들림 없이), 머리를 부드럽게 쫓는다
                Vector3 head;
                if (PilotHead(out head))
                {
                    Quaternion vr = t.rotation;
                    float sway = Mathf.Sin(Time.time * 0.5f) * 0.05f;
                    _camPos = head + vr * new Vector3(PilotCamOffset.x * _side, PilotCamOffset.y + sway, PilotCamOffset.z);
                    lookAt = head + vr * new Vector3(0f, -0.05f, 0f);
                    up = vr * Vector3.up;
                    follow = 25f;   // 가까워서 회전이 늦으면 얼굴이 화면 밖으로 밀린다
                }
                else
                {
                    // 조종사를 못 찾으면(아바타 없음 등) 조종석 어깨 뒤로
                    _camPos = _subPos + t.rotation * CockpitOffset;
                    lookAt = _camPos + t.forward * 100f;
                    up = t.up;
                }
            }
            else if (shot == SHOT_COCKPIT)
            {
                _smoothRot = Quaternion.Slerp(_smoothRot, t.rotation, 1f - Mathf.Exp(-dt * 12f));
                _camPos = _subPos + t.rotation * CockpitOffset;
                lookAt = _camPos + _smoothRot * Vector3.forward * 100f;
                up = _smoothRot * Vector3.up;
                follow = 30f;
            }
            else if (shot == SHOT_MISSILE)
            {
                if (_mis != null && !_misDone)
                {
                    Transform m = _mis.transform;
                    Vector3 want = m.position - m.forward * MissileBack + Vector3.up * 1.5f;
                    _camPos = Vector3.Lerp(_camPos, want, 1f - Mathf.Exp(-dt * 20f));
                    _camPos += (want - _camPos) * 0.5f;   // 빠른 미사일에 처지지 않게 절반은 바로
                    lookAt = _misTarget != null && _misTarget.gameObject.activeInHierarchy ? _misTargetPos : m.position + m.forward * 200f;
                    follow = 10f;
                }
                else { lookAt = _impactPos; follow = 6f; }   // 직접 고른 미사일 캠: 터진 자리를 잠시 본다
            }
            else if (shot == SHOT_TARGET)
            {
                // 표적과 거의 같이 움직이며(빠른 항공기를 놓치지 않게) 표적 → 터진 뒤엔 터진 자리를 본다
                if (!_misDone) { _fixedPos += _misTargetVel * (dt * 0.85f); }
                _camPos = _fixedPos;
                lookAt = _misDone || _misTarget == null ? _impactPos : _misTargetPos;
                follow = 9f;
            }

            if (shot != SHOT_PILOT) { _camPos = Clearance(_camPos); }   // 조종사 컷은 기체에 붙어 있다(지상에서 위로 밀리지 않게)
            Vector3 dir = lookAt - _camPos;
            if (dir.sqrMagnitude > 0.01f)
            {
                Quaternion want = Quaternion.LookRotation(dir, up);
                _camRot = Quaternion.Slerp(_camRot, want, 1f - Mathf.Exp(-dt * follow));
            }
            Quaternion rot = _camRot;
            if (shot == SHOT_TELE)
            {
                // 망원 손떨림(화각이 좁을수록 작게)
                float k = _fov * 0.02f;
                rot = rot * Quaternion.Euler((Mathf.PerlinNoise(Time.time * 0.7f, 0.3f) - 0.5f) * k, (Mathf.PerlinNoise(0.6f, Time.time * 0.7f) - 0.5f) * k, 0f);
            }
            Transform ct = Cam.transform;
            ct.SetPositionAndRotation(_camPos, rot);
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, _fov, 1f - Mathf.Exp(-dt * 6f));
        }

        /// 대상 기체 조종사의 머리 위치(아바타 머리 뼈 → 없으면 머리 추적). 조종사가 없거나 못 찾으면 false.
        private bool PilotHead(out Vector3 head)
        {
            head = Vector3.zero;
            if (_subject < 0) { return false; }
            VRCPlayerApi p = Networking.GetOwner(Vehicles[_subject].gameObject);
            if (!Utilities.IsValid(p)) { return false; }
            head = p.GetBonePosition(HumanBodyBones.Head);
            if (head.sqrMagnitude < 0.0001f) { head = p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position; }
            // 기체에서 너무 멀면(아직 좌석에 안 붙음 등) 믿지 않는다
            return head.sqrMagnitude > 0.0001f && (head - Vehicles[_subject].transform.position).sqrMagnitude < 400f;
        }

        /// 지면·해수면 위 MinGroundClearance 아래로 내려가지 않게.
        private Vector3 Clearance(Vector3 p)
        {
            float ground = SeaLevel;
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, p.y + 1000f, p.z), Vector3.down, out hit, 3000f, GroundLayers, QueryTriggerInteraction.Ignore))
            {
                ground = Mathf.Max(ground, hit.point.y);
            }
            if (p.y < ground + MinGroundClearance) { p.y = ground + MinGroundClearance; }
            return p;
        }

        private Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }

        // ───────────── 글자 ─────────────

        private string[] ShotNames = { "CHASE", "FLYBY", "TELEPHOTO", "SIDE", "WINGMAN", "COCKPIT", "PILOT", "MISSILE", "TARGET" };

        [Tooltip("번역 장치(6-166). Bake가 넣는다")]
        public ACO_Localization Loc;

        /// 번역 장치가 언어를 바꾸면 부른다.
        public void LocalizationChanged() { UpdateTexts(); }

        /// 번역 표의 문장(장치가 없거나 표에 없으면 english). {0}에 값을 끼운다(6-166)
        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return s == key ? english : s;
        }
        private string T1(string key, string english, string a) { return T(key, english).Replace("{0}", a); }


        private void UpdateTexts()
        {
            if (PowerText) { PowerText.text = _on ? "<color=#66FF77>" + T("set.on", "ON") + "</color>" : "<color=#FF5544>" + T("set.off", "OFF") + "</color>"; }
            if (ResolutionText) { ResolutionText.text = _fhd ? "FHD 1080p" : "HD 720p"; }
            if (TargetText)
            {
                if (_targetMode == AUTO) { TargetText.text = T1("spec.auto", "AUTO  {0}", _subject >= 0 ? SlotLabel(_subject) : "-"); }
                else { TargetText.text = SlotLabel(_targetMode); }
            }
            string shot = _shot >= 0 && _shot < ShotNames.Length ? T("spec.shot." + _shot, ShotNames[_shot]) : "";
            if (ShotText) { ShotText.text = _shotMode == AUTO ? T1("spec.auto", "AUTO  {0}", shot) : shot; }
            if (StatusText)
            {
                if (!_on) { StatusText.text = T("spec.status.off", "SPECTATOR CAMERA OFF"); }
                else if (_subject < 0) { StatusText.text = _targetMode == AUTO ? T("spec.status.nopilots", "NO PILOTS IN THE AIR") : T("spec.status.waitpilot", "WAITING FOR PILOT"); }
                else if ((_shot == SHOT_MISSILE || _shot == SHOT_TARGET) && _mis == null) { StatusText.text = T("spec.status.waitmissile", "WAITING FOR A MISSILE (CHASE)"); }
                else { StatusText.text = T("spec.status.live", "LIVE"); }
            }
        }

        /// 슬롯 표시: 슬롯 이름 + 명단의 참가자. 명단에 없으면 "SLOT 번호"와 지금 조종사.
        private string SlotLabel(int i)
        {
            int slot = Flow != null ? Flow.GetSlotOfAircraft(Vehicles[i]) : -1;
            if (slot >= 0) { return Flow.GetSlotLabel(slot); }
            string label = T1("flow.slot.fallback", "SLOT {0}", (i + 1).ToString());
            if (!IsValid(i)) { return label; }
            VRCPlayerApi p = Networking.GetOwner(Vehicles[i].gameObject);
            return Utilities.IsValid(p) ? label + "  " + p.displayName : label;
        }
    }
}
