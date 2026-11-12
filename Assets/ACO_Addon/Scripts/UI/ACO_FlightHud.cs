using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 비행 HUD(로컬 전용, 동기화 없음) — SAV HUD(멀리 띄운 판을 스텐실로 보는 방식, 원형 헤딩)를 대신한다(Bake가 SAV HUD를 숨긴다).
    ///  - 기체 앞 고정(시준): 루트를 매 프레임 내 머리 위치 + 기체 자세에 두고, Distance(m) 앞 판(Canvas)에 그린다. 판의 1단위 = 시야각 1°(가운데 기준, Scale 배).
    ///    피치 래더·속도 벡터·기총 피퍼는 실제 방향을 판에 투영(접평면 — 기체 기준 x/z, y/z)하므로 바깥 풍경과 정확히 겹친다. 맨 위에 겹쳐 그린다(ZTest Always), UI 레이어(사진에 안 찍힌다).
    ///  - 표시: 위 일자형 헤딩 테이프, 왼쪽 속도(kt) 테이프 + 그 옆 스로틀(추력 막대, 애프터버너 구간), 오른쪽 고도(ft) 테이프 + 낮으면 전파고도(R),
    ///    피치 래더(10° — 양쪽 끝에 숫자, 위는 실선·아래는 점선), 속도 벡터, G, 받음각(랜딩기어를 내렸을 때만), 상태(GEAR·FLAP·HOOK, 에어브레이크를 펴면 AIRBRAKE),
    ///    잔탄(GUN·MSL·SP·FLR 한 줄씩, 고른 무장에 >), HP 막대, 고른 ACO 무장의 장전 슬롯 막대(잔탄 왼쪽 — 가로로 얇은 막대를 쌓는다), 조준 표시(고른 무장 — 기총 작은 십자 + 피퍼, 유도무기·폭탄 W, 무유도 로켓 십자).
    ///  - 기총 피퍼: 기수 앞 적기(PipperTargetAngle 안, GunRange 안 — 가장 기수 가까운 것) 거리에서 탄이 닿는 시간 t를 구하고,
    ///    피퍼 = 지금 쏜 탄의 t초 뒤 자리(탄속 + 내 속도 물려받기 + 중력) − 적기 속도 × t. 적기를 피퍼에 겹치면(지금 자리 + 속도 × t = 탄 자리) 맞는다. 적기가 없으면 PipperDefaultRange 거리의 탄 자리.
    ///  - 기체별 값(Bake가 Vehicles와 같은 순서로 채운다): SAV 비행·랜딩기어·플랩·후크·브레이크·기총·플레어, ACO 기본 미사일·특수무장 슬롯, 기총 탄속·중력·물려받기·발사 속도.
    ///  - 글자는 TextInterval마다만 바꾼다(문자열 만들기를 줄인다). 위치는 매 프레임.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_FlightHud : UdonSharpBehaviour
    {
        [Header("연결 (Bake)")]
        public ACO_RadarContacts Contacts;
        public ACO_Units Units;
        public SaccEntity[] Vehicles;
        public SaccAirVehicle[] Air;
        public DFUNC_Gear[] Gears;
        public DFUNC_Flaps[] FlapFuncs;
        public DFUNC_Hook[] Hooks;
        public DFUNC_Brake[] Brakes;
        public DFUNC_Gun[] Guns;
        public DFUNC_Flares[] FlareFuncs;
        [Tooltip("기본 미사일(특수무장 슬롯 밖의 ACO 미사일 무장)")]
        public ACO_DFUNC_Missile[] Missiles;
        public ACO_DFUNC_Special[] Specials;
        [Tooltip("SUPPORT 다이얼(지원 호출 — BB/TNK 줄, 6-142)")]
        public ACO_DFUNC_Support[] Supports;
        [Tooltip("기총 탄속(m/s)·중력 배수·기체 속도 물려받기(0~1)·초당 발사 수(잔탄 = 남은 초 × 이것) — Bake가 기총 파티클에서 읽는다")]
        public float[] GunSpeed;
        public float[] GunGravity;
        public float[] GunInherit;
        public float[] GunRate;
        [Tooltip("전파고도 레이캐스트 레이어(지면 + 물 — 바다는 트리거라 트리거도 맞힌다)")]
        public LayerMask GroundLayers = 2049 | 16;
        [Tooltip("Bake가 플레이어 기체의 SAV HUD를 숨긴다(SAV_HUDController의 자식 — bigstuff 등 — 을 끈다). 끄고 Bake하면 Bake가 껐던 것만 다시 켠다")]
        public bool HideSavHud = true;
        [Tooltip("Bake가 끈 SAV HUD 자식(다시 보이게 할 때 쓴다 — 손대지 않는다)")]
        public GameObject[] HiddenSavHudParts;

        [Header("표시 (HUD 설정 패널이 바꾼다)")]
        public bool ShowHud = true;
        [Tooltip("크기 배율(HUD 설정 FLIGHT HUD SIZE — 0.6~1.6). 실제 크기 = BaseScale × 이것")]
        public float Scale = 1f;
        [Tooltip("기본 크기(설정 ×1.0일 때의 크기 — 예전 1.0이 너무 커서 0.5)")]
        public float BaseScale = 0.5f;
        [Tooltip("밝기(0~1) — 선·글자 알파에 곱한다")]
        [Range(0.1f, 1f)] public float Brightness = 1f;
        [Tooltip("판까지 거리(m) — 멀수록 시준이 정확하다(머리가 움직여도 덜 어긋난다)")]
        public float Distance = 20f;

        [Header("구성 (프리팹 빌더)")]
        public Transform Root;
        public Transform Canvas;
        public MeshRenderer[] LineRenderers;
        public TextMeshPro[] AllTexts;
        public Transform[] HeadingTicks;
        public TextMeshPro[] HeadingLabels;
        public TextMeshPro HeadingText;
        public Transform[] SpeedTicks;
        public TextMeshPro[] SpeedLabels;
        public TextMeshPro SpeedText;
        public Transform[] AltTicks;
        public TextMeshPro[] AltLabels;
        public TextMeshPro AltText;
        public TextMeshPro RadarAltText;
        public Transform ThrottleFill;
        public Transform ThrottleAbLine;
        [Tooltip("스로틀 위치(목표 출력) 표시 — 막대는 실제 엔진 출력이라 이 표시를 뒤따라간다")]
        public Transform ThrottleTarget;
        public TextMeshPro ThrottleText;
        [Tooltip("피치 래더 칸(RungAngles 순서, 각도 도 — 0 = 수평선)")]
        public Transform[] Rungs;
        public float[] RungAngles;
        public Transform VelocityMarker;
        public GameObject GunCross;
        public GameObject WSight;
        public GameObject RocketCross;
        public Transform Pipper;
        public TextMeshPro PipperRangeText;
        public TextMeshPro GText;
        public TextMeshPro AoaText;
        public TextMeshPro StatusText;
        [Tooltip("내 기체가 아군 ECM 구역 안일 때 보이는 문구")]
        public TextMeshPro EcmText;
        [Tooltip("잔탄 세 칸: 고른 무장 쉐브론 | 무장 이름(AmmoText) | 수(오른쪽 맞춤) — 한 줄씩")]
        public TextMeshPro AmmoMarkText;
        public TextMeshPro AmmoText;
        public TextMeshPro AmmoCountText;
        public TextMeshPro HpText;
        public Transform HpFill;
        public GameObject SlotGroup;
        public Transform[] SlotBars;
        public Transform[] SlotFills;

        [Header("값")]
        public float HeadingScale = 0.35f;     // 단위/도
        public float SpeedScale = 0.08f;       // 단위/kt
        public float AltScale = 0.008f;        // 단위/ft
        public float ThrottleHeight = 8f;
        [Tooltip("장전 막대 간격(단위)")]
        public float SlotSpacing = 0.55f;
        [Tooltip("한 줄에 놓는 장전 막대 수 — 슬롯이 이보다 많으면(MMP 16) 2행으로 나눈다(위 줄 먼저)")]
        public int SlotRowMax = 8;
        [Tooltip("장전 막대 2행일 때 줄 간격(단위 — 막대 높이 2.6보다 조금 크게, 둘째 줄은 아래)")]
        public float SlotRowSpacing = 3f;        [Tooltip("피치 래더 간격(판 단위 / 각도 1°) — 0.5 = 10°마다 5단위(창 ±10.5에 약 ±21°). HUD 크기와 상관없다. 1 / (BaseScale × Scale)이면 실제 각도(바깥 수평선과 겹친다)")]
        [Range(0.2f, 3f)] public float LadderScale = 0.5f;
        [Tooltip("래더 칸을 보이는 창(단위, 가운데 기준 ±)")]
        public Vector2 LadderWindow = new Vector2(11f, 10.5f);
        [Tooltip("전파고도를 보이는 높이(ft) 아래")]
        public float RadarAltBelow = 2500f;
        public float GunRange = 1500f;
        public float PipperTargetAngle = 12f;
        public float PipperDefaultRange = 700f;
        public float TextInterval = 0.1f;

        public const int SIGHT_NONE = 0;
        public const int SIGHT_GUN = 1;
        public const int SIGHT_W = 2;
        public const int SIGHT_CROSS = 3;

        private const float KT = 1.9438445f;
        private const float FT = 3.28084f;
        private const float TAN1 = 0.017455065f;

        private int _vehicle = -1;
        private bool _shown;
        private float _previewUntil;
        private float _textTimer;
        private float _radarTimer;
        private float _radarAltFt = 99999f;
        private Material _lineMat;
        private Color _lineBase;
        private Color[] _textBase;
        private float _appliedBright = -1f;
        private Color[] _textOrig;   // 처음 글자 색(녹색이었는지 — HUD 색을 바꿀 대상)
        private Color _tint;
        private bool _tintSet;
        private int[] _headingVal;
        private int[] _speedVal;
        private int[] _altVal;
        private float _scaleApplied = -1f;
        private float _distApplied = -1f;
        // 기총 피퍼 표적
        private Transform _gunTarget;
        private Vector3 _gunTargetPos;
        private Vector3 _gunTargetVel;
        private bool _gunTargetHas;
        private float _gunTargetDist;
        private float _gunScanTimer;
        private int _sight = SIGHT_NONE;
        private ACO_DFUNC_Missile _selectedAco;
        private bool _started;
        private Vector3 _ladderFlat = Vector3.forward;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            // 선은 머티리얼 한 벌을 같이 쓴다(밝기를 한 번에 바꾸려고)
            if (LineRenderers != null && LineRenderers.Length > 0 && LineRenderers[0])
            {
                _lineMat = LineRenderers[0].material;
                _lineBase = _lineMat.color;
                for (int i = 1; i < LineRenderers.Length; i++) { if (LineRenderers[i]) { LineRenderers[i].sharedMaterial = _lineMat; } }
            }
            int nt = AllTexts != null ? AllTexts.Length : 0;
            _textBase = new Color[nt];
            _textOrig = new Color[nt];
            for (int i = 0; i < nt; i++) { if (AllTexts[i]) { _textBase[i] = AllTexts[i].color; _textOrig[i] = _textBase[i]; } }
            ApplyTint();
            _headingVal = NewVals(HeadingTicks);
            _speedVal = NewVals(SpeedTicks);
            _altVal = NewVals(AltTicks);
            SetShown(false);
        }

        private int[] NewVals(Transform[] a)
        {
            int n = a != null ? a.Length : 0;
            int[] v = new int[n];
            for (int i = 0; i < n; i++) { v[i] = int.MinValue; }
            return v;
        }

        // ───────────── 매 프레임 ─────────────

        public override void PostLateUpdate()
        {
            Start();
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || Root == null) { return; }
            int v = FindPilotedVehicle();
            if (ShowHud && v < 0 && Time.time < _previewUntil) { PreviewFrame(lp); return; }
            if (!ShowHud || v < 0) { if (_shown) { SetShown(false); } _vehicle = -1; return; }
            if (v != _vehicle) { _vehicle = v; _gunTarget = null; _textTimer = 0f; }
            if (!_shown) { SetShown(true); }
            ApplyLook();

            SaccEntity e = Vehicles[v];
            Transform vt = e.transform;
            Quaternion rot = vt.rotation;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Root.SetPositionAndRotation(head, rot);
            Quaternion inv = Quaternion.Inverse(rot);
            float scale = Mathf.Max(Scale * BaseScale, 0.05f);
            SaccAirVehicle air = Air != null && v < Air.Length ? Air[v] : null;

            Vector3 fwd = vt.forward;
            float heading = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            if (heading < 0f) { heading += 360f; }
            float kt = air != null ? air.AirSpeed * KT : 0f;
            float seaLevel = air != null ? air.SeaLevel : 0f;
            float ft = (vt.position.y - seaLevel) * FT;
            Vector3 vel = air != null ? air.CurrentVel : Vector3.zero;

            UpdateTape(HeadingTicks, HeadingLabels, _headingVal, heading, 5f, 10, HeadingScale, true, 360);
            UpdateTape(SpeedTicks, SpeedLabels, _speedVal, kt, 10f, 50, SpeedScale, false, 0);
            UpdateTape(AltTicks, AltLabels, _altVal, ft, 100f, 500, AltScale, false, 0);
            UpdateLadder(inv, fwd, vt.up, scale);
            UpdateVelocityMarker(inv, vel, scale);
            UpdateThrottle(air);
            UpdateSelection(v);
            UpdatePipper(v, inv, head, vt, vel, scale);
            UpdateSlotBars();
            UpdateRadarAlt(vt.position);

            _textTimer -= Time.deltaTime;
            if (_textTimer <= 0f)
            {
                _textTimer = TextInterval;
                UpdateTexts(v, air, heading, kt, ft);
            }
        }

        /// HUD 설정 패널이 부른다: seconds 동안 기체 밖에서도 시선 앞에 보여 준다(크기·밝기·색을 바꾸며 볼 수 있게).
        public void Preview(float seconds)
        {
            Start();
            _previewUntil = Time.time + seconds;
        }

        /// 미리보기 한 프레임: 머리 방향 그대로, 예시 값(속도 450kt·고도 12000ft·방위는 시선 방향) — 테이프·피치 사다리·속도 벡터만.
        private void PreviewFrame(VRCPlayerApi lp)
        {
            if (!_shown) { SetShown(true); }
            _vehicle = -1;
            ApplyLook();
            VRCPlayerApi.TrackingData hd = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Quaternion rot = hd.rotation;
            Root.SetPositionAndRotation(hd.position, rot);
            Quaternion inv = Quaternion.Inverse(rot);
            float scale = Mathf.Max(Scale * BaseScale, 0.05f);
            Vector3 fwd = rot * Vector3.forward;
            float heading = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            if (heading < 0f) { heading += 360f; }
            UpdateTape(HeadingTicks, HeadingLabels, _headingVal, heading, 5f, 10, HeadingScale, true, 360);
            UpdateTape(SpeedTicks, SpeedLabels, _speedVal, 450f, 10f, 50, SpeedScale, false, 0);
            UpdateTape(AltTicks, AltLabels, _altVal, 12000f, 100f, 500, AltScale, false, 0);
            UpdateLadder(inv, fwd, rot * Vector3.up, scale);
            UpdateVelocityMarker(inv, fwd * 230f, scale);
        }

        /// 내가 조종 중인 플레이어 기체(Vehicles 번호), 없으면 -1.
        private int FindPilotedVehicle()
        {
            if (Contacts == null || Contacts.LocalVehicle == null || Vehicles == null) { return -1; }
            SaccEntity e = Contacts.LocalVehicle;
            if (!e.Piloting) { return -1; }
            for (int i = 0; i < Vehicles.Length; i++) { if (Vehicles[i] == e) { return i; } }
            return -1;
        }

        private void SetShown(bool on)
        {
            _shown = on;
            if (Root != null && Root.gameObject.activeSelf != on) { Root.gameObject.SetActive(on); }
        }

        /// 크기·거리·밝기가 바뀌었을 때만 판 배율·머티리얼 알파를 다시 넣는다.
        /// HUD 설정 패널이 부른다: 선·녹색 글자를 이 색으로(알파·밝기는 그대로). 빨강 경고 같은 다른 색 글자는 그대로.
        public void SetHudColor(Color c)
        {
            _tint = c;
            _tintSet = true;
            ApplyTint();
        }

        private void ApplyTint()
        {
            if (!_tintSet || _textBase == null) { return; }
            if (_lineMat) { _lineBase = new Color(_tint.r, _tint.g, _tint.b, _lineBase.a); }
            for (int i = 0; i < _textBase.Length; i++)
            {
                if (IsHudGreen(_textOrig[i])) { _textBase[i] = new Color(_tint.r, _tint.g, _tint.b, _textOrig[i].a); }
            }
            _appliedBright = -1f;   // 다음 ApplyLook에서 다시 넣는다
        }

        /// HUD 녹색 계열인가(색을 바꿀 대상 — 빨강 경고·파랑 아군 같은 다른 색은 그대로).
        private bool IsHudGreen(Color c) { return c.g > 0.7f && c.r < 0.5f && c.b < 0.6f; }

        private void ApplyLook()
        {
            float scale = Mathf.Max(Scale * BaseScale, 0.05f);
            float dist = Mathf.Max(Distance, 1f);
            if (scale != _scaleApplied || dist != _distApplied)
            {
                _scaleApplied = scale;
                _distApplied = dist;
                if (Canvas)
                {
                    Canvas.localPosition = new Vector3(0f, 0f, dist);
                    Canvas.localScale = Vector3.one * (dist * TAN1 * scale);
                }
            }
            float b = Mathf.Clamp(Brightness, 0.1f, 1f);
            if (Mathf.Abs(b - _appliedBright) < 0.001f) { return; }
            _appliedBright = b;
            if (_lineMat) { Color c = _lineBase; c.a *= b; _lineMat.color = c; }
            if (AllTexts != null)
            {
                for (int i = 0; i < AllTexts.Length; i++)
                {
                    if (!AllTexts[i]) { continue; }
                    Color c = _textBase[i];
                    c.a *= b;
                    AllTexts[i].color = c;
                }
            }
        }

        /// 판 좌표(단위 — 1 = 1°): 기체 기준 방향을 접평면에 투영. 뒤쪽이면 false.
        private bool Project(Quaternion inv, Vector3 worldDir, float scale, out Vector2 p)
        {
            Vector3 l = inv * worldDir;
            if (l.z <= 0.01f) { p = Vector2.zero; return false; }
            p = new Vector2(l.x / l.z, l.y / l.z) / (TAN1 * scale);
            return true;
        }

        // ───────────── 테이프 ─────────────

        /// 눈금 테이프: 지금 값 가운데, step마다 눈금, labelEvery마다 숫자(눈금 자식 글자). horizontal = 헤딩(가로), 아니면 세로. wrap > 0이면 값을 감는다(헤딩 0~360).
        private void UpdateTape(Transform[] ticks, TextMeshPro[] labels, int[] shown, float value, float step, int labelEvery, float unitsPer, bool horizontal, int wrap)
        {
            if (ticks == null) { return; }
            int n = ticks.Length;
            float baseTick = Mathf.Floor(value / step) * step;
            int half = n / 2;
            for (int i = 0; i < n; i++)
            {
                Transform t = ticks[i];
                if (!t) { continue; }
                float tv = baseTick + (i - half) * step;
                float off = (tv - value) * unitsPer;
                Vector3 lp = t.localPosition;
                if (horizontal) { lp.x = off; } else { lp.y = off; }
                t.localPosition = lp;
                int iv = Mathf.RoundToInt(tv);
                if (wrap > 0) { iv = ((iv % wrap) + wrap) % wrap; }
                if (labels == null || i >= labels.Length || !labels[i] || shown[i] == iv) { continue; }
                shown[i] = iv;
                bool lab = iv % labelEvery == 0 && (wrap > 0 || iv >= 0);
                if (labels[i].gameObject.activeSelf != lab) { labels[i].gameObject.SetActive(lab); }
                if (lab) { labels[i].text = wrap > 0 ? (iv / 10).ToString("00") : iv.ToString(); }
                // 긴 눈금 = 숫자 자리
                Vector3 s = t.localScale;
                if (horizontal) { s.y = lab ? 1f : 0.6f; } else { s.x = lab ? 1f : 0.6f; }
                t.localScale = s;
            }
        }

        // ───────────── 피치 래더·속도 벡터 ─────────────

        /// 래더 칸: 기준 방위 쪽의 고도각 a 지점과 그 옆(수평) 지점을 투영 — 가운데·기울기를 정한다. 창 밖이면 숨긴다.
        /// 기준 방위 = 기수의 수평 성분. 수직 가까이(|기수 y| 0.94 → 0.995, 약 70° → 84°)에선 기수의 수평 성분이 0에 가까워 조금만 흔들려도
        /// 방위가 빙글 돌므로, 기체 위쪽을 빼서(fwd − up × fwd.y) 배 쪽 방위로 옮겨 간다 — 수직에서 머물거나 롤해도 래더가 기체에 붙어 돌지 않는다.
        private void UpdateLadder(Quaternion inv, Vector3 fwd, Vector3 vup, float scale)
        {
            if (Rungs == null || RungAngles == null) { return; }
            float w = Mathf.InverseLerp(0.94f, 0.995f, Mathf.Abs(fwd.y));
            Vector3 refDir = fwd - vup * (fwd.y * w);
            Vector3 flat = new Vector3(refDir.x, 0f, refDir.z);
            if (flat.sqrMagnitude < 0.000001f) { flat = _ladderFlat; }
            flat = flat.normalized;   // Udon: 지역 Vector3에 Normalize()를 불러도 값이 바뀌지 않는다(복사본에 적용) — 꼭 대입한다
            _ladderFlat = flat;
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            for (int i = 0; i < Rungs.Length && i < RungAngles.Length; i++)
            {
                Transform r = Rungs[i];
                if (!r) { continue; }
                float a = RungAngles[i] * Mathf.Deg2Rad;
                Vector3 d = flat * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
                Vector2 c, c2;
                bool ok1 = Project(inv, d, scale, out c);
                bool ok = Project(inv, d + right * 0.05f, scale, out c2) && ok1;
                // 래더 간격: 투영 자리를 가운데에서의 각도(도 — 접평면은 멀수록 늘어나므로 각도로 고르게)로 바꿔 1° = LadderScale 단위로 놓는다.
                // HUD 크기와 상관없이 판 위 간격이 같다(판 단위로 정한다). 실제 각도보다 좁으면 기수를 들어도 천천히 움직인다
                // (실제 수평선과는 안 겹친다 — 속도 벡터·피퍼는 그대로 실제 방향).
                Vector2 pos = c;
                if (ok)
                {
                    float m = c.magnitude;
                    if (m > 0.0001f)
                    {
                        float deg = Mathf.Atan(m * TAN1 * scale) * Mathf.Rad2Deg;
                        pos = c * (deg * LadderScale / m);
                    }
                }
                ok = ok && Mathf.Abs(pos.x) < LadderWindow.x && Mathf.Abs(pos.y) < LadderWindow.y;
                if (r.gameObject.activeSelf != ok) { r.gameObject.SetActive(ok); }
                if (!ok) { continue; }
                Vector2 dir = c2 - c;
                r.localPosition = new Vector3(pos.x, pos.y, 0f);
                r.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            }
        }

        private void UpdateVelocityMarker(Quaternion inv, Vector3 vel, float scale)
        {
            if (!VelocityMarker) { return; }
            Vector2 p = Vector2.zero;
            bool ok = vel.sqrMagnitude > 25f && Project(inv, vel, scale, out p);
            if (!ok) { p = Vector2.zero; }
            // 창 밖으로 나가면 가장자리에 붙인다
            p.x = Mathf.Clamp(p.x, -LadderWindow.x, LadderWindow.x);
            p.y = Mathf.Clamp(p.y, -LadderWindow.y, LadderWindow.y);
            VelocityMarker.localPosition = new Vector3(p.x, p.y, 0f);
        }

        // ───────────── 스로틀·전파고도 ─────────────

        private void UpdateThrottle(SaccAirVehicle air)
        {
            if (air == null) { return; }
            if (ThrottleFill)
            {
                Vector3 s = ThrottleFill.localScale;
                s.y = Mathf.Max(Mathf.Clamp01(air.EngineOutput) * ThrottleHeight, 0.001f);
                ThrottleFill.localScale = s;
            }
            if (ThrottleAbLine)
            {
                Vector3 p = ThrottleAbLine.localPosition;
                p.y = Mathf.Clamp01(air.ThrottleAfterburnerPoint) * ThrottleHeight;
                ThrottleAbLine.localPosition = p;
            }
            if (ThrottleTarget)
            {
                // 스로틀 입력은 조종사 쪽에만 있다
                SetActive(ThrottleTarget.gameObject, air.Piloting);
                Vector3 p = ThrottleTarget.localPosition;
                p.y = Mathf.Clamp01(air.ThrottleInput) * ThrottleHeight;
                ThrottleTarget.localPosition = p;
            }
        }

        private void UpdateRadarAlt(Vector3 pos)
        {
            _radarTimer -= Time.deltaTime;
            if (_radarTimer > 0f) { return; }
            _radarTimer = 0.1f;
            RaycastHit hit;
            float limit = RadarAltBelow / FT + 50f;
            if (Physics.Raycast(pos, Vector3.down, out hit, limit, GroundLayers, QueryTriggerInteraction.Collide)) { _radarAltFt = hit.distance * FT; }
            else { _radarAltFt = 99999f; }
        }

        // ───────────── 무장 ─────────────

        /// 고른 무장(오른쪽 → 왼쪽 다이얼): 기총 = 작은 십자 + 피퍼, ACO 무장 = W 또는 십자(무유도 로켓), SAV 폭탄·미사일 = W.
        private void UpdateSelection(int v)
        {
            _sight = SIGHT_NONE;
            _selectedAco = null;
            SaccEntity e = Vehicles[v];
            UdonSharpBehaviour sel = Selected(e.Dial_Functions_R, e.RStickSelection);
            int kind = SightOf(v, sel);
            if (kind == SIGHT_NONE)
            {
                sel = Selected(e.Dial_Functions_L, e.LStickSelection);
                kind = SightOf(v, sel);
            }
            _sight = kind;
            SetActive(GunCross, kind == SIGHT_GUN || kind == SIGHT_NONE);
            SetActive(WSight, kind == SIGHT_W);
            SetActive(RocketCross, kind == SIGHT_CROSS);
            if (Pipper) { SetActive(Pipper.gameObject, kind == SIGHT_GUN); }
        }

        private UdonSharpBehaviour Selected(UdonSharpBehaviour[] dial, int index)
        {
            return dial != null && index >= 0 && index < dial.Length ? dial[index] : null;
        }

        private int SightOf(int v, UdonSharpBehaviour sel)
        {
            if (sel == null) { return SIGHT_NONE; }
            if (Guns != null && v < Guns.Length && Guns[v] != null && sel == (UdonSharpBehaviour)Guns[v]) { return SIGHT_GUN; }
            ACO_DFUNC_Missile aco = null;
            if (Missiles != null && v < Missiles.Length && Missiles[v] != null && sel == (UdonSharpBehaviour)Missiles[v]) { aco = Missiles[v]; }
            if (aco == null && Specials != null && v < Specials.Length && Specials[v] != null && sel == (UdonSharpBehaviour)Specials[v])
            {
                UdonSharpBehaviour m = Specials[v].GetModule();
                if (m == null) { return SIGHT_NONE; }
                if (m.GetUdonTypeName() != GetUdonTypeName<ACO_DFUNC_Missile>()) { return SIGHT_W; }   // SAV 폭탄·미사일 모듈
                aco = (ACO_DFUNC_Missile)m;
            }
            if (aco == null)
            {
                string type = sel.GetUdonTypeName();
                if (type == GetUdonTypeName<DFUNC_AAM>() || type == GetUdonTypeName<DFUNC_AGM>() || type == GetUdonTypeName<DFUNC_Bomb>()) { return SIGHT_W; }
                return SIGHT_NONE;
            }
            _selectedAco = aco;
            return aco.GetSightKind() == 1 ? SIGHT_CROSS : SIGHT_W;
        }

        private void SetActive(GameObject g, bool on)
        {
            if (g != null && g.activeSelf != on) { g.SetActive(on); }
        }

        // ───────────── 기총 피퍼 ─────────────

        private void UpdatePipper(int v, Quaternion inv, Vector3 head, Transform vt, Vector3 vel, float scale)
        {
            if (_sight != SIGHT_GUN || !Pipper) { return; }
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            _gunScanTimer -= dt;
            if (_gunScanTimer <= 0f) { _gunScanTimer = 0.25f; PickGunTarget(vt); }
            // 표적 속도(위치 차, 순간이동은 버린다)
            float range = PipperDefaultRange;
            Vector3 tvel = Vector3.zero;
            if (_gunTarget != null && _gunTarget.gameObject.activeInHierarchy)
            {
                Vector3 tp = _gunTarget.position;
                if (_gunTargetHas)
                {
                    Vector3 nv = (tp - _gunTargetPos) / dt;
                    if (nv.sqrMagnitude < 1000000f) { _gunTargetVel = Vector3.Lerp(_gunTargetVel, nv, 1f - Mathf.Exp(-dt * 6f)); }
                }
                _gunTargetPos = tp;
                _gunTargetHas = true;
                range = Vector3.Distance(vt.position, tp);
                tvel = _gunTargetVel;
            }
            else { _gunTarget = null; }
            _gunTargetDist = _gunTarget != null ? range : -1f;

            float speed = GunSpeed != null && v < GunSpeed.Length && GunSpeed[v] > 1f ? GunSpeed[v] : 900f;
            float grav = GunGravity != null && v < GunGravity.Length ? GunGravity[v] : 0f;
            float inherit = GunInherit != null && v < GunInherit.Length ? GunInherit[v] : 1f;
            Vector3 muzzleVel = vt.forward * speed + vel * inherit;
            // 탄이 닿는 시간: 월드에서의 탄 속력으로 표적까지 → 그 시간 뒤 표적 자리까지로 한 번 고친다
            float bulletSpeed = Mathf.Max(muzzleVel.magnitude, 1f);
            float t = range / bulletSpeed;
            if (_gunTarget != null) { t = Vector3.Distance(vt.position, _gunTargetPos + tvel * t) / bulletSpeed; }
            Vector3 bullet = vt.position + muzzleVel * t + Physics.gravity * (0.5f * grav * t * t);
            Vector3 aim = bullet - tvel * t;
            Vector2 p;
            if (!Project(inv, aim - head, scale, out p)) { p = Vector2.zero; }
            p.x = Mathf.Clamp(p.x, -LadderWindow.x, LadderWindow.x);
            p.y = Mathf.Clamp(p.y, -LadderWindow.y, LadderWindow.y);
            Pipper.localPosition = new Vector3(p.x, p.y, 0f);
        }

        /// 기총 표적: 기수에서 PipperTargetAngle 안, GunRange 안의 적 항공기 중 기수에 가장 가까운 것.
        private void PickGunTarget(Transform vt)
        {
            Transform best = null;
            if (Contacts != null && Contacts.Targets != null)
            {
                float bestAng = PipperTargetAngle;
                float rangeSq = GunRange * GunRange;
                for (int i = 0; i < Contacts.Count; i++)
                {
                    if (!Contacts.Visible[i]) { continue; }
                    int kind = Contacts.Kinds[i];
                    if (kind != ACO_RadarContacts.KIND_ENEMY_AIR && kind != ACO_RadarContacts.KIND_OBJECTIVE_AIR) { continue; }
                    Transform t = Contacts.Targets[i];
                    if (t == null) { continue; }
                    Vector3 d = t.position - vt.position;
                    if (d.sqrMagnitude > rangeSq) { continue; }
                    float ang = Vector3.Angle(vt.forward, d);
                    if (ang < bestAng) { bestAng = ang; best = t; }
                }
            }
            if (best != _gunTarget) { _gunTarget = best; _gunTargetHas = false; _gunTargetVel = Vector3.zero; }
        }

        // ───────────── 장전 막대 ─────────────

        private void UpdateSlotBars()
        {
            ACO_DFUNC_Missile w = _selectedAco;
            bool on = w != null && SlotBars != null;
            if (SlotGroup) { SetActive(SlotGroup, on); }
            if (!on) { return; }
            int n = Mathf.Min(w.GetSlotCount(), SlotBars.Length);
            // 한 줄에 SlotRowMax개까지 — 넘으면 2행(위 줄 = 앞 슬롯, 아래 줄 = 나머지, 열 수는 반씩)
            int rowMax = Mathf.Max(SlotRowMax, 1);
            int cols = n > rowMax ? (n + 1) / 2 : n;
            for (int i = 0; i < SlotBars.Length; i++)
            {
                bool show = i < n;
                if (SlotBars[i]) { SetActive(SlotBars[i].gameObject, show); }
                if (!show) { continue; }
                // 세로 막대를 왼쪽부터 늘어놓고 마지막 막대가 잔탄 글자 바로 옆(묶음 기준점)에 오게
                int row = i / Mathf.Max(cols, 1);
                int col = i - row * cols;
                int rowCount = row == 0 ? cols : n - cols;
                SlotBars[i].localPosition = new Vector3(-(rowCount - 1 - col) * SlotSpacing, -row * SlotRowSpacing, 0f);
                if (SlotFills != null && i < SlotFills.Length && SlotFills[i])
                {
                    // 아래 → 위로 찬다
                    Vector3 s = SlotFills[i].localScale;
                    s.y = Mathf.Max(w.GetSlotProgress(i), 0.001f);
                    SlotFills[i].localScale = s;
                }
            }
        }

        // ───────────── 글자 ─────────────

        private void UpdateTexts(int v, SaccAirVehicle air, float heading, float kt, float ft)
        {
            if (HeadingText) { HeadingText.text = (Mathf.RoundToInt(heading) % 360).ToString("000"); }
            if (EcmText) { SetActive(EcmText.gameObject, Contacts != null && Contacts.LocalJammed); }
            if (SpeedText) { SpeedText.text = Mathf.RoundToInt(kt).ToString(); }
            if (AltText) { AltText.text = Mathf.RoundToInt(ft).ToString(); }
            if (RadarAltText)
            {
                bool low = _radarAltFt < RadarAltBelow;
                SetActive(RadarAltText.gameObject, low);
                if (low) { RadarAltText.text = "R " + Mathf.RoundToInt(_radarAltFt); }
            }
            DFUNC_Gear gear = Gears != null && v < Gears.Length ? Gears[v] : null;
            DFUNC_Flaps flaps = FlapFuncs != null && v < FlapFuncs.Length ? FlapFuncs[v] : null;
            DFUNC_Hook hook = Hooks != null && v < Hooks.Length ? Hooks[v] : null;
            DFUNC_Brake brake = Brakes != null && v < Brakes.Length ? Brakes[v] : null;
            bool gearDown = gear != null && !gear.GearUp;
            if (air != null)
            {
                if (GText) { GText.text = "G " + air.VertGs.ToString("0.0"); }
                if (AoaText)
                {
                    SetActive(AoaText.gameObject, gearDown);
                    if (gearDown) { AoaText.text = "AOA " + air.AngleOfAttack.ToString("0.0"); }
                }
                if (ThrottleText) { ThrottleText.text = air.AfterburnerOn ? "AB" : "THR " + Mathf.RoundToInt(Mathf.Clamp01(air.ThrottleInput) * 100f); }
            }
            if (StatusText)
            {
                string s = "";
                if (gear != null) { s += Lit(gearDown, gearDown ? "GEAR DN" : "GEAR UP") + "\n"; }
                if (flaps != null) { s += Lit(flaps.Flaps, flaps.Flaps ? "FLAP ON" : "FLAP OFF") + "\n"; }
                if (hook != null) { s += Lit(hook.HookDown, hook.HookDown ? "HOOK ON" : "HOOK OFF") + "\n"; }
                bool airborne = air != null && !air.Taxiing;
                if (brake != null && airborne && brake.BrakeInput > 0.1f)
                {
                    // 에어브레이크: 반 초마다 깜빡인다
                    s += (Time.time % 1f < 0.6f ? "<color=#FFD050>AIRBRAKE</color>" : "") + "\n";
                }
                // 지원 호출 경고(조건이 안 맞을 때 — "TANKER: BANK UNDER 45", "STRIKE: NO LASER SPOT" 등): 깜빡인다
                ACO_DFUNC_Support supAlert = Supports != null && v < Supports.Length ? Supports[v] : null;
                string alert = supAlert != null ? supAlert.GetAlert() : "";
                if (alert != "") { s += (Time.time % 1f < 0.7f ? "<color=#FFD050>" + alert + "</color>" : "") + "\n"; }
                StatusText.text = s;
            }
            if (AmmoText) { BuildAmmo(v); }
            if (air != null)
            {
                float hp = air.FullHealth > 0f ? Mathf.Clamp01(air.Health / air.FullHealth) : 0f;
                if (HpText) { HpText.text = "HP " + Mathf.RoundToInt(hp * 100f) + "%"; }
                if (HpFill) { Vector3 s = HpFill.localScale; s.x = Mathf.Max(hp, 0.001f); HpFill.localScale = s; }
            }
            if (PipperRangeText)
            {
                bool has = _sight == SIGHT_GUN && _gunTargetDist >= 0f;
                SetActive(PipperRangeText.gameObject, has);
                if (has) { PipperRangeText.text = Units != null ? Units.Distance(_gunTargetDist) : Mathf.RoundToInt(_gunTargetDist) + "m"; }
            }
        }

        /// 켜진 상태는 밝게, 꺼진 상태는 흐리게.
        private string Lit(bool on, string text)
        {
            return on ? text : "<alpha=#60>" + text + "<alpha=#FF>";
        }

        /// 잔탄 한 줄씩, 세 칸(쉐브론 | 이름 | 수)에 같은 줄 수로 나눠 쓴다: GUN(남은 초 × 초당 발사 수), MSL(기본 미사일), 특수무장 ID, 지원(BB/TNK — 장수·쿨타임·안내), FLR. 고른 무장 줄에 ">".
        private void BuildAmmo(int v)
        {
            string mark = "";
            string names = "";
            string counts = "";
            DFUNC_Gun gun = Guns != null && v < Guns.Length ? Guns[v] : null;
            if (gun != null)
            {
                float rate = GunRate != null && v < GunRate.Length ? GunRate[v] : 0f;
                mark += Mark(_sight == SIGHT_GUN);
                names += "GUN\n";
                counts += (rate > 0f ? Mathf.RoundToInt(gun.GunAmmoInSeconds * rate).ToString() : gun.GunAmmoInSeconds.ToString("0.0") + "s") + "\n";
            }
            ACO_DFUNC_Missile msl = Missiles != null && v < Missiles.Length ? Missiles[v] : null;
            if (msl != null)
            {
                mark += Mark(_selectedAco == msl);
                names += "MSL\n";
                counts += msl.GetAmmo() + "\n";
            }
            ACO_DFUNC_Special sp = Specials != null && v < Specials.Length ? Specials[v] : null;
            if (sp != null)
            {
                UdonSharpBehaviour m = sp.GetModule();
                if (m == null)
                {
                    mark += "\n";
                    names += "<alpha=#60>---<alpha=#FF>\n";
                    counts += "\n";
                }
                else
                {
                    mark += Mark(_selectedAco != null && _selectedAco != msl);
                    names += sp.GetModuleId() + "\n";
                    counts += (m.GetUdonTypeName() == GetUdonTypeName<ACO_DFUNC_Missile>() ? ((ACO_DFUNC_Missile)m).GetHudCount() : "-") + "\n";
                }
            }
            ACO_DFUNC_Support sup = Supports != null && v < Supports.Length ? Supports[v] : null;
            if (sup != null)
            {
                mark += Mark(sup.IsSelected());
                names += sup.GetHudName() + "\n";
                counts += sup.GetHudCount() + "\n";
            }
            DFUNC_Flares flr = FlareFuncs != null && v < FlareFuncs.Length ? FlareFuncs[v] : null;
            if (flr != null)
            {
                mark += "\n";
                names += "FLR\n";
                counts += flr.NumFlares + "\n";
            }
            AmmoText.text = names;
            if (AmmoMarkText) { AmmoMarkText.text = mark; }
            if (AmmoCountText) { AmmoCountText.text = counts; }
        }

        private string Mark(bool selected) { return selected ? ">\n" : "\n"; }
    }
}
