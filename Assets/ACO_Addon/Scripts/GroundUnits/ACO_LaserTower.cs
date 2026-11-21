using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 레이저 탑(지상 보스, 6-331) 표현. 체력 · 부위 · 스폰은 같은 오브젝트의 ACO_Warship(ExternalPose)이 맡고, 이 스크립트는 단계 · 패턴 시간표를 화면에 그린다.
    /// 단계 · 패턴 시간표 · 배치 시드는 ACO_PhaseLaserTower가 동기화한다 — 여기선 읽기만 하므로 모든 클라이언트에서 같은 시각 · 같은 자리에 일어난다.
    ///  - 배치(스폰 때, 모든 클라이언트): ECM 기지 3곳을 탑에서 약 EcmDistance(±EcmJitter) 무작위 방향(서로 120° ± 20°)의 땅에 — 시드 + 지형(모든 화면 같다)으로 계산.
    ///  - 반사판 비행선: 시드로 정한 궤도(반경 · 고도 · 방향)를 서버 시각으로 돈다(위치를 보내지 않는다). 격추되면 기울며 떨어지고 땅 밑으로 숨긴다. 반사경은 천천히 돈다.
    ///  - 단계: 0 ECM(차량만 드러남) → 1 환풍구(환풍구 드러남) → 2 렌즈(과열 창에만 렌즈 히트박스 · 마커). 비행선 · 방공은 처음부터.
    ///  - 패턴 A(6-331 ④-2): 조준 동안 얇은 파란선이 표적을 AimLag초 늦게 따라가고, 발사 동안 굵은 빔이 초당 TrackRateA°로 쫓는다. 늘 BaseWobble만큼 흔들리고, 렌즈가 켜진 ECM 포드 구역 안이면 JamWobble · 추적 절반.
    ///  - 패턴 B: 렌즈 → 비행선 → 가장 가까운 다음 비행선 … 반사경마다 NetBranches갈래(하나는 다음 비행선, 나머지는 탑 쪽으로 몰린 방향으로 15km — 탑을 감싸듯). 순서 · 방향은 패턴 시드 — 모든 화면 같다.
    ///  - 피해: 굵은 빔이 나가는 동안 매 프레임 내 기체만 판정(맞는 사람 화면 — 랜스와 같다), 닿는 순간 바로 · 그 뒤 DamageTick초마다. 헤드 · 렌즈는 빔 방향을 본다(숙임 MinPitch°까지).
    ///  - 3단계 과열 창: 렌즈 둘레 붉은 띠(OverheatGlow)가 켜지고 히트박스 · 락온 마커가 열린다. 1단계엔 환풍구에 JAMMED 마커.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_LaserTower : UdonSharpBehaviour
    {
        public const int STAGE_ECM = 0;
        public const int STAGE_VENTS = 1;
        public const int STAGE_LENS = 2;

        [Header("참조")]
        public ACO_Warship Warship;
        [Tooltip("Bake가 넣는다 — 동기화된 단계 · 시간표 · 시드")]
        public ACO_PhaseLaserTower Phase;
        [Tooltip("렌즈 코어 체력(본체 SaccTarget)")]
        public SaccTarget TargetHealth;
        [Tooltip("내 기체 · 참가자 기체(Bake가 넣는다) — 패턴 표적 · 피해")]
        public ACO_RadarContacts Contacts;
        [Tooltip("지면 레이어(Bake가 생성기 값을 넣는다) — ECM 기지를 땅에 앉힌다")]
        public LayerMask GroundLayers = 2049;
        public float SeaLevel = 0f;

        [Header("부위")]
        [Tooltip("ECM 차량(1단계 목표) — 모두 부숴야 환풍구가 드러난다")]
        public ACO_FortressPart[] EcmTrucks;
        [Tooltip("ECM 기지(차량 + 둘레 방공의 부모) — 스폰 때 자리를 옮긴다")]
        public Transform[] EcmSites;
        [Tooltip("보조탑 끝 환풍구(2단계 목표)")]
        public ACO_FortressPart[] Vents;
        [Tooltip("반사판 비행선(공중 표적)")]
        public ACO_FortressPart[] Airships;
        [Tooltip("비행선의 반사경(천천히 돈다)")]
        public Transform[] Mirrors;
        [Tooltip("탑 둘레 방공(수리 대상)")]
        public UdonSharpBehaviour[] TowerDefense;

        [Header("렌즈")]
        public Transform HeadPivot;
        public Transform LensPivot;
        [Tooltip("렌즈 앞 — 빔이 나가는 자리")]
        public Transform Muzzle;
        public Collider CoreHitbox;
        [Tooltip("렌즈 락온 마커 — 과열 창에만 켠다")]
        public GameObject[] CoreMarkers;

        [Header("빔 (6-331 ④-2)")]
        [Tooltip("패턴 A 빔(조준 = 얇게, 발사 = 굵게)")]
        public LineRenderer BeamA;
        [Tooltip("패턴 B 그물 갈래(1 + 비행선 수 × NetBranches — 10척 × 5갈래면 51개)")]
        public LineRenderer[] NetLines;
        [Tooltip("과열 창에 켜는 렌즈 둘레 붉은 띠")]
        public GameObject OverheatGlow;
        public float AimWidthA = 4f;
        public float FireWidthA = 36f;
        public float AimWidthB = 3f;
        public float FireWidthB = 24f;
        public Color AimColor = new Color(0.35f, 0.65f, 1f, 0.75f);
        public Color FireColor = new Color(0.75f, 0.95f, 1f, 1f);
        [Tooltip("패턴 A 빔 길이(m) — 지형에서 끊긴다")]
        public float BeamLengthA = 15000f;
        [Tooltip("조준선이 표적을 따라가는 늦음(초)")]
        public float AimLag = 0.4f;
        [Tooltip("발사 중 추적 속도(°/s) — 2026-11-20 8 → 4(사용자 \"선회가 너무 강해 멀리서 접근할 수가 없다\")")]
        public float TrackRateA = 6f;   // 8 → 6(사용자 "8도 하니까 여전히 세네")   // → 4 → 8(사용자 "지금은 좀 느리네, 흔들림은 그대로 추적은 8도 정도로")
        [Tooltip("플레이어 ECM 구역 안: 추적 속도(°/s)와 흔들림(±°) — 4 → 2, 2.4 → 3.5")]
        public float JamTrackRate = 3f;
        public float JamWobble = 3.5f;
        [Tooltip("기본 흔들림(±°, ECM 없어도) — 각도라 멀수록 크게 빗나간다(10km 약 ±140m, 2km 약 ±28m)")]
        public float BaseWobble = 0.8f;
        [Tooltip("렌즈 숙임 한계(°) — 렌즈 806m에서 2km 안 지면은 못 쏜다")]
        public float MinPitch = -22f;
        [Tooltip("빔에 맞는 반경(m) — 패턴 A · B")]
        public float BeamRadiusA = 25f;
        public float BeamRadiusB = 20f;
        [Tooltip("초당 피해 — 매 프레임 판정해 처음 닿는 순간 바로, 빔 안에 있는 동안 DamageTick초마다 그 몫을 준다(1프레임만 스쳐도 한 번)")]
        public float BeamDps = 80f;
        public float DamageTick = 0.2f;
        [Tooltip("반사경마다 갈래 수(하나는 다음 비행선)")]
        public int NetBranches = 5;
        [Tooltip("바깥 갈래 길이(m) — 패턴 A 빔과 같은 15km(사용자 \"똑같이 15km로 통일\"), 지형에서 끊긴다")]
        public float NetBranchLength = 15000f;
        [Tooltip("바깥 갈래가 탑 쪽(안쪽)에서 벗어나는 최대 각(°). 안쪽으로 몰린다(u·|u| 분포) — 탑을 감싸듯 그물이 퍼진다")]
        public float NetSpread = 150f;
        [Tooltip("갈래가 겨누는 탑 쪽 높이(m, 탑 바닥 기준) — 이 사이에서 갈래마다 다르다")]
        public float NetAimLow = 0f;
        public float NetAimHigh = 1200f;
        [Tooltip("갈래 위아래 흔들림(±°)")]
        public float NetPitchJitter = 12f;
        [Tooltip("바깥 갈래가 도는 빠르기(°/s)")]
        public float NetSpinRate = 4f;
        [Tooltip("반사경 가운데(비행선 부위 로컬 — 모델 단위)")]
        public Vector3 MirrorCenter = new Vector3(0f, -36.1f, 4f);
        [Tooltip("쉬는 동안 렌즈가 돌아가는 빠르기(°/s)")]
        public float IdleTurnRate = 12f;
        [Tooltip("빔을 맞힌 쪽으로 넘기는 오브젝트(비우면 이 오브젝트)")]
        public GameObject DamageSource;
        [Tooltip("조종석 음성 경고(Bake가 넣는다) — 패턴 A가 내 기체를 겨누거나 쏘는 동안, 그물 갈래가 WarnRadiusB 안일 때 LASER")]
        public ACO_VoiceWarning Voice;
        [Tooltip("그물(패턴 B) 갈래가 내 기체에서 이 거리(m) 안이면 LASER 경고")]
        public float WarnRadiusB = 400f;

        [Header("소리 (6-331 ⑤)")]
        [Tooltip("충전음(렌즈, 3D) — 패턴 A 조준 · 패턴 B 예고가 시작될 때")]
        public AudioSource ChargeSound;
        [Tooltip("발사음(렌즈, 3D) — 굵은 빔이 나가는 동안")]
        public AudioSource FireSound;
        [Tooltip("빔이 내 기체 곁을 스치는 전자음(레이저 기총과 같은 ACO_BulletFlyby.ReportLaser — Bake가 넣는다)")]
        public ACO_BulletFlyby Flyby;
        [Tooltip("아무 데도 안 닿고 끝까지 뻗은 빔이 흐려지기 시작하는 자리(길이 비율) — 거기서 끝까지 투명해진다")]
        [Range(0f, 1f)] public float FadeStart = 0.55f;

        [Header("빔 끝 효과(땅 · 물에 닿은 자리)")]
        [Tooltip("불티(파랑 · 흰 가산, 월드 — 스크립트가 Emit)")]
        public ParticleSystem HitSparks;
        [Tooltip("닿은 자리의 빛(가산 섬광)")]
        public ParticleSystem HitGlow;
        [Tooltip("그을림(누운 검은 얼룩 — 땅에만, 물은 안 남긴다)")]
        public ParticleSystem Scorch;
        [Tooltip("효과를 내는 간격(초)")]
        public float HitFxInterval = 0.1f;
        [Tooltip("그을림은 효과 몇 번에 한 번")]
        public int ScorchEvery = 1;
        public float ScorchSizeMin = 40f;
        public float ScorchSizeMax = 60f;
        [Tooltip("패턴 A(굵은 단일 빔) 효과 크기 배율")]
        public float ImpactScaleA = 1.4f;

        [Header("배치")]
        public float EcmDistance = 2000f;
        public float EcmJitter = 300f;
        public float AirshipOrbitMin = 1500f;
        public float AirshipOrbitMax = 3000f;
        public float AirshipAltMin = 500f;
        public float AirshipAltMax = 900f;
        [Tooltip("비행선 속력(m/s)")]
        public float AirshipSpeed = 15f;
        public float MirrorSpinRate = 6f;
        [Tooltip("격추된 비행선이 떨어지는 빠르기(m/s)와 숨기기까지(초)")]
        public float AirshipFallSpeed = 45f;
        public float AirshipFallTime = 9f;

        [Header("격파")]
        public ACO_FortressBlast Blast;
        [Tooltip("렌즈가 깨진 뒤 대폭발까지(초) — 그동안 코어 단이 위에서부터 터져 내려온다")]
        public float DeathDelay = 14f;
        public float SecondaryInterval = 0.6f;
        [Tooltip("연쇄 폭발이 내려오는 높이(m) — 렌즈에서 이만큼 아래까지")]
        public float DeathDrop = 760f;

        private bool _initialized;
        private bool _spawned;
        private bool _dying;
        private bool _gone;
        private float _liveAt = -100f;
        private const float SPAWN_DOWN_GRACE = 1f;
        private const float DOWN_CONFIRM = 5f;
        private float _pendingExplodeAt = -1f;
        private float _deathLeft;
        private float _secondaryTimer;
        private float _goneAt;
        private int _appliedStage = -1;
        private bool _lensOpen;
        private float _lineTimer;
        private int _placedSeed = -1;
        private SaccTarget[] _defenseHealth;
        // 빔(모든 클라이언트 지역 상태 — 동기화된 시간표 · 시드로 같게 그린다)
        private int _patSeen = -1;
        private int _kindSeen;
        private Vector3 _aimPoint;
        private Vector3 _seenPoint;
        private Vector3 _baseDir = Vector3.forward;
        private Vector3 _headDir = Vector3.forward;
        private bool _fireStarted;
        private float _losTimer;
        private float _jamTimer;
        private bool _jammed;
        private float _nextHitAt;   // 다음 피해를 줄 수 있는 시각(Time.time) — 닿는 순간 바로, 그 뒤 DamageTick마다
        private bool _beamsShown;
        private int[] _netOrder;
        private int _netCount;
        private Vector3[] _segFrom;
        private Vector3[] _segTo;
        private int _segCount;
        private float[] _cutLen;
        private bool[] _cutHit;     // 바깥 갈래가 땅 · 물에 닿았는가(0.15초마다 다시 잰다)
        private bool[] _cutWater;
        private bool[] _segFade;    // 끝을 흐리게(아무 데도 안 닿고 끝까지 뻗은 갈래)
        private bool[] _segHit;     // 끝이 땅 · 물에 닿았다(불티 · 빛, 땅이면 그을림)
        private bool[] _segWater;
        private int[] _lineStyle;   // 갈래 선마다 지금 넣어 둔 색(조준/발사 × 흐림) — 바뀔 때만 넣는다
        private int _styleA = -1;
        private Gradient _gAim;
        private Gradient _gFire;
        private Gradient _gAimFade;
        private Gradient _gFireFade;
        private float _fxTimer;
        private int _fxCount;
        private const int HULL_MASK = 1 << 17;          // 탑 몸체(Hull) · 부위 히트박스 레이어(ACO_PrefabBuilder.LayerHitbox) — 기체도 이 레이어라 이 탑의 콜라이더일 때만 막힌 것으로 본다
        private const float AIRSHIP_CLEARANCE = 200f;   // 비행선 궤도 아래 가장 높은 땅보다 이만큼(m) 위로
        private const int ECM_WIDE_TRIES = 48;          // ECM 기지: 가까운 8번이 다 물이면 탑 둘레 이 횟수만큼 넓게 찾는다
        private const float ECM_WIDE_MAX = 25000f;      // 넓게 찾는 거리(m — 작전 지역 밖 · 안전지대 · 건물 · 물은 생성기가 거른다)
        private bool[] _siteVoid;    // 놓을 땅이 없어 파괴 처리한 ECM 기지(모든 화면이 같은 결과 — 같은 시드 · 같은 지형)
        private int[] _partSite;     // Warship.Parts[k]가 속한 ECM 기지 번호(-1 = 아님)
        private float _voidTimer;
        private int _cutAlive = -1;
        private const int SNAP_MAX = 8;   // ECM 기지 하나의 자식(차량 · 방공) 최대 수
        private Vector3[] _snapHome;      // ECM 기지 자식의 처음 로컬 자리(물에 걸려 당긴 자리가 다음 미션에 남지 않게)  // 그물 갈래 길이를 마지막으로 잰 때 살아 있던 비행선 수
        private const float TERRAIN_WAIT = 30f;          // 늦게 들어온 화면: 지형 · 건물이 다 세워질 때까지 배치를 미루는 최대 시간(초)
        private bool _placedNoTerrain;                   // 지형이 다 세워지기 전에(TERRAIN_WAIT 초과) 배치했다 — 세워지면 한 번 더 배치
        private Transform _hullRoot;                     // 탑 몸체 콜라이더(Hull — 기초판 · 주탑 · 보조탑 · 헤드). 부위 히트박스 · 비행선은 빼고 이것만 빔을 막는다
        private RaycastHit[] _hullHits;
        private bool _castHit;      // CastBeam 결과
        private bool _castWater;
        private float _warnTimer;
        private int _soundState;   // 0 조용, 1 충전, 2 발사
        private float _cutTimer;
        private int _drawnA = -1;   // BeamA 상태: -1 끔, 0 조준, 1 발사
        private int _drawnB = -1;   // 그물 상태: -1 끔, 0 미리보기, 1 발사
        private float[] _airR;
        private float[] _airAlt;
        private float[] _airPhase;
        private float[] _airDir;
        private float[] _fallAt;
        private Vector3[] _fallPos;
        private Quaternion[] _fallRot;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!Warship) { Warship = GetComponent<ACO_Warship>(); }
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            int n = TowerDefense != null ? TowerDefense.Length : 0;
            _defenseHealth = new SaccTarget[n];
            for (int i = 0; i < n; i++) { if (TowerDefense[i] != null) { _defenseHealth[i] = TowerDefense[i].GetComponent<SaccTarget>(); } }
            int a = Airships != null ? Airships.Length : 0;
            _airR = new float[a];
            _airAlt = new float[a];
            _airPhase = new float[a];
            _airDir = new float[a];
            _fallAt = new float[a];
            _fallPos = new Vector3[a];
            _fallRot = new Quaternion[a];
            for (int i = 0; i < a; i++) { _fallAt[i] = -1f; }
            _netOrder = new int[a];
            _hullRoot = transform.Find("Hull");
            int scount = EcmSites != null ? EcmSites.Length : 0;
            _snapHome = new Vector3[scount * SNAP_MAX];
            for (int s = 0; s < scount; s++)
            {
                if (EcmSites[s] == null) { continue; }
                for (int k = 0; k < EcmSites[s].childCount && k < SNAP_MAX; k++) { _snapHome[s * SNAP_MAX + k] = EcmSites[s].GetChild(k).localPosition; }
            }
            _hullHits = new RaycastHit[16];
            int sc = EcmSites != null ? EcmSites.Length : 0;
            _siteVoid = new bool[sc];
            int pc = Warship != null && Warship.Parts != null ? Warship.Parts.Length : 0;
            _partSite = new int[pc];
            for (int k = 0; k < pc; k++)
            {
                _partSite[k] = -1;
                if (Warship.Parts[k] == null) { continue; }
                for (int s = 0; s < sc; s++) { if (EcmSites[s] != null && Warship.Parts[k].transform.IsChildOf(EcmSites[s])) { _partSite[k] = s; break; } }
            }
            int lines = NetLines != null ? NetLines.Length : 0;
            _segFrom = new Vector3[lines];
            _segTo = new Vector3[lines];
            _cutLen = new float[lines];
            _cutHit = new bool[lines];
            _cutWater = new bool[lines];
            _segFade = new bool[lines];
            _segHit = new bool[lines];
            _segWater = new bool[lines];
            _lineStyle = new int[lines];
            for (int i = 0; i < lines; i++) { _lineStyle[i] = -1; }
            _gAim = MakeGradient(AimColor, false);
            _gFire = MakeGradient(FireColor, false);
            _gAimFade = MakeGradient(AimColor, true);
            _gFireFade = MakeGradient(FireColor, true);
            if (HeadPivot != null) { _headDir = HeadPivot.forward; }
            HideBeams();
            SetLens(false);
            ApplyStage(STAGE_ECM);
        }

        // ───────────── 함선 이벤트 ─────────────

        public void ShipSpawned()
        {
            Init();
            _spawned = true;
            _dying = false;
            _gone = false;
            _appliedStage = -1;
            _liveAt = Time.time;
            // 풀에서 다시 쓸 때: 지난 미션의 '놓을 땅 없음' 표시를 지운다(지형을 기다리는 동안 남아 ECM 차량이 꺼져 있던 것)
            if (_siteVoid != null) { for (int i = 0; i < _siteVoid.Length; i++) { _siteVoid[i] = false; } }
            _placedNoTerrain = false;
            if (_pendingExplodeAt >= 0f) { _pendingExplodeAt = Time.time; }
            _placedSeed = -1;
            for (int i = 0; i < _fallAt.Length; i++) { _fallAt[i] = -1f; }
            SetLens(false);
            HideBeams();
            _patSeen = -1;
            if (Warship != null) { transform.SetPositionAndRotation(Warship.GetCruiseCenter(), Quaternion.Euler(0f, Warship.GetCruiseYaw(), 0f)); }
            PlaceLayout();
            ApplyStage(CurrentStage());
        }

        public void ShipDespawned()
        {
            _spawned = false;
            _dying = false;
            _gone = false;
            _pendingExplodeAt = -1f;
            SetLens(false);
            HideBeams();
        }

        /// 렌즈 파괴(SaccTarget.ExplodeOther — 모든 클라이언트): 연쇄 폭발이 위에서부터 내려온 뒤 대폭발.
        public void Explode()
        {
            if (_gone || _dying) { return; }
            if (JustSpawned()) { if (_pendingExplodeAt < 0f) { _pendingExplodeAt = Time.time; } return; }
            ApplyExplode(true);
        }

        private void ApplyExplode(bool say)
        {
            _pendingExplodeAt = -1f;
            if (_gone || _dying) { return; }
            _dying = true;
            _deathLeft = DeathDelay;
            _secondaryTimer = 0f;
            SetLens(false);
            HideBeams();
            if (say && Phase != null && Phase.IsLive()) { Phase.SayDown(); }
        }

        void OnEnable() { _liveAt = Time.time; }

        private bool JustSpawned() { return !_spawned || Time.time - _liveAt < SPAWN_DOWN_GRACE; }

        private bool CoreDown()
        {
            if (TargetHealth != null) { return TargetHealth.Health <= 0f; }
            return Warship == null || Warship.IsDestroyed();
        }

        // ───────────── 배치(시드 — 모든 클라이언트 같다) ─────────────

        /// 0..1 의사 난수(시드 · 번호 — Udon에서 전역 Random 상태를 건드리지 않는다).
        private float Hash(int seed, int k)
        {
            float x = Mathf.Sin(seed * 0.0137f + k * 12.9898f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }

        private void PlaceLayout()
        {
            int seed = Phase != null ? Phase.GetSeed() : 0;
            if (seed == _placedSeed) { return; }
            // 늦게 들어온 화면: 지형 · 건물이 다 세워지기 전이면 레이캐스트가 다 빗나가 ECM 기지가 전부 '놓을 땅 없음'이 됐다 — 세워질 때까지(최대 TERRAIN_WAIT초) 미룬다(매 프레임 다시 본다)
            ACO_MissionGenerator gen = Phase != null ? Phase.Generator : null;
            if (gen != null && !gen.IsTerrainReady() && Time.time - _liveAt < TERRAIN_WAIT) { return; }
            _placedSeed = seed;
            _placedNoTerrain = gen != null && !gen.IsTerrainReady();
            _appliedStage = -1;   // 파괴 처리한 기지가 바뀌었을 수 있다 — 다음 프레임에 단계(드러냄)를 다시 적용
            Vector3 c = transform.position;
            float baseAng = Hash(seed, 1) * Mathf.PI * 2f;
            int sites = EcmSites != null ? EcmSites.Length : 0;
            for (int s = 0; s < sites; s++)
            {
                if (EcmSites[s] == null) { continue; }
                Vector3 best = c + new Vector3(Mathf.Sin(baseAng + s * 2.0944f), 0f, Mathf.Cos(baseAng + s * 2.0944f)) * EcmDistance;
                Vector3 want = best;
                bool found = false;
                // 물 위면 각도 · 거리를 조금씩 바꿔 다시 찾는다(최대 8번 — 모든 화면이 같은 순서)
                for (int t = 0; t < 8; t++)
                {
                    float ang = baseAng + s * 2.0944f + (Hash(seed, 10 + s * 16 + t) - 0.5f) * 0.70f;
                    float dist = EcmDistance + (Hash(seed, 60 + s * 16 + t) - 0.5f) * 2f * EcmJitter;
                    Vector3 p = c + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * dist;
                    RaycastHit hit;
                    if (Physics.Raycast(new Vector3(p.x, c.y + 6000f, p.z), Vector3.down, out hit, 12000f, GroundLayers.value, QueryTriggerInteraction.Ignore) && hit.point.y > SeaLevel + 2f)
                    {
                        best = hit.point;
                        found = true;
                        break;
                    }
                }
                // 8번이 다 물이면(사용자 "거리가 많이 벌어지거나 가까워져도 되니까 공역 전체, 그래도 안 되면 그냥 파괴 처리"): 탑 둘레 넓게 — 원래 자리에 가장 가까운 뭍
                if (!found) { found = FindWideEcmSite(seed, s, c, want, out best); }
                _siteVoid[s] = !found;
                if (!found)
                {
                    EcmSites[s].position = c + Vector3.down * 3000f;   // 땅속에 두고 파괴 처리(SilenceVoidSites — 점수 · 대사 없음, 진행 수에서도 뺀다)
                    continue;
                }
                EcmSites[s].position = best;
                EcmSites[s].rotation = Quaternion.Euler(0f, Hash(seed, 120 + s) * 360f, 0f);
                SnapSiteToGround(s);
            }
            for (int i = 0; i < _airR.Length; i++)
            {
                _airR[i] = Mathf.Lerp(AirshipOrbitMin, AirshipOrbitMax, Hash(seed, 200 + i));
                _airAlt[i] = Mathf.Lerp(AirshipAltMin, AirshipAltMax, Hash(seed, 220 + i));
                _airPhase[i] = i * Mathf.PI * 2f / Mathf.Max(_airR.Length, 1) + (Hash(seed, 240 + i) - 0.5f) * 0.6f;
                _airDir[i] = Hash(seed, 260 + i) < 0.5f ? 1f : -1f;
                // 궤도 아래 산 속으로 들어가지 않게: 궤도 24곳의 땅 높이 중 가장 높은 곳 + AIRSHIP_CLEARANCE 위로(모든 화면 같은 지형 — 같은 값)
                float top = c.y;
                for (int k = 0; k < 24; k++)
                {
                    float b = k * Mathf.PI * 2f / 24f;
                    Vector3 q = c + new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b)) * _airR[i];
                    RaycastHit gh;
                    if (Physics.Raycast(new Vector3(q.x, c.y + 6000f, q.z), Vector3.down, out gh, 12000f, GroundLayers.value, QueryTriggerInteraction.Ignore)) { top = Mathf.Max(top, gh.point.y); }
                }
                _airAlt[i] = Mathf.Max(_airAlt[i], top - c.y + AIRSHIP_CLEARANCE);
            }
        }

        /// 비행선 i의 지금 자리(서버 시각 — 모든 화면 같다). 패턴 B가 같은 값으로 그물을 그린다.
        public Vector3 GetAirshipPosition(int i, int now)
        {
            if (i < 0 || i >= _airR.Length) { return transform.position; }
            float w = AirshipSpeed / Mathf.Max(_airR[i], 1f) * _airDir[i];
            int origin = Phase != null ? Phase.GetStamp() : 0;   // 페이즈 시작(동기화) — 서버 시각 나머지를 쓰면 1시간 경계에서 순간이동했다
            float t = (now - origin) * 0.001f;
            float a = _airPhase[i] + w * t;
            return transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * _airR[i] + Vector3.up * _airAlt[i];
        }

        public bool IsAirshipAlive(int i)
        {
            return Airships != null && i >= 0 && i < Airships.Length && Airships[i] != null && !Airships[i].IsDestroyed();
        }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            if (!_spawned || _gone) { return; }
            float dt = Time.deltaTime;
            if (_pendingExplodeAt >= 0f)
            {
                if (!CoreDown()) { _pendingExplodeAt = -1f; }
                else if (Time.time - _pendingExplodeAt >= DOWN_CONFIRM) { ApplyExplode(false); }
            }
            // 지형 없이 배치했던 화면: 지형이 세워지면 한 번 더 배치(같은 시드 — 다른 화면과 같은 자리로)
            if (_placedNoTerrain && Phase != null && Phase.Generator != null && Phase.Generator.IsTerrainReady()) { _placedNoTerrain = false; _placedSeed = -1; }
            if (Phase != null && Phase.GetSeed() != _placedSeed) { PlaceLayout(); }
            UpdateAirships(dt);
            _voidTimer -= dt;
            if (_voidTimer <= 0f) { _voidTimer = 1f; SilenceVoidSites(); }
            if (_dying)
            {
                UpdateDeath(dt);
                return;
            }
            int stage = CurrentStage();
            if (stage != _appliedStage) { ApplyStage(stage); }
            if (Phase == null || !Phase.IsLive())
            {
                if (_beamsShown) { HideBeams(); }
                if (_lensOpen) { SetLens(false); }   // 과열 창 도중 페이즈가 끝나도 렌즈 히트박스 · 붉은 빛을 닫는다
                SetSound(0);
                return;
            }
            int now = Networking.GetServerTimeInMilliseconds();
            bool open = Phase.IsLensOpen(now);
            if (open != _lensOpen) { SetLens(open); }
            if (_lensOpen && OverheatGlow != null) { OverheatGlow.transform.localScale = Vector3.one * (1f + 0.035f * Mathf.Sin(Time.time * 11f)); }
            UpdateBeams(now, dt);
            _lineTimer -= dt;
            if (_lineTimer <= 0f)
            {
                _lineTimer = 0.5f;
                Phase.CheckLines(this);
            }
        }

        private int CurrentStage() { return Phase != null && Phase.IsLive() ? Phase.GetStage() : STAGE_ECM; }

        private void UpdateAirships(float dt)
        {
            if (Airships == null) { return; }
            int now = Networking.GetServerTimeInMilliseconds();
            for (int i = 0; i < Airships.Length; i++)
            {
                ACO_FortressPart ship = Airships[i];
                if (ship == null) { continue; }
                Transform t = ship.transform;
                if (ship.IsDestroyed())
                {
                    // 격추: 그 자리에서 기울며 떨어지다 숨긴다(늦게 들어온 화면은 곧바로 숨긴다)
                    if (_fallAt[i] < 0f)
                    {
                        _fallAt[i] = Time.time - (Time.time - _liveAt < 6f ? AirshipFallTime : 0f);   // 나타난 지 6초 안(늦게 들어와 체력 동기화가 늦게 온 것)이면 곧바로 숨긴다
                        _fallPos[i] = t.position;
                        _fallRot[i] = t.rotation;
                    }
                    float age = Time.time - _fallAt[i];
                    if (age < AirshipFallTime)
                    {
                        t.SetPositionAndRotation(_fallPos[i] + Vector3.down * (AirshipFallSpeed * age * (0.4f + age * 0.08f)), _fallRot[i] * Quaternion.Euler(age * 3f, 0f, age * 4f));
                    }
                    else if (t.position.y > transform.position.y - 2000f) { t.position = transform.position + Vector3.down * 3000f; }
                    continue;
                }
                _fallAt[i] = -1f;
                Vector3 p = GetAirshipPosition(i, now);
                Vector3 ahead = GetAirshipPosition(i, now + 1000) - p;
                ahead.y = 0f;
                Quaternion r = ahead.sqrMagnitude > 0.01f ? Quaternion.LookRotation(ahead.normalized, Vector3.up) : t.rotation;
                t.SetPositionAndRotation(p, r);
                if (Mirrors != null && i < Mirrors.Length && Mirrors[i] != null) { Mirrors[i].localRotation = Quaternion.Euler(0f, (now % 360000) * 0.001f * MirrorSpinRate * _airDir[i], 0f); }
            }
        }

        // ───────────── 격파 ─────────────

        private void UpdateDeath(float dt)
        {
            _deathLeft -= dt;
            _secondaryTimer -= dt;
            Vector3 top = LensPivot != null ? LensPivot.position : transform.position + Vector3.up * 800f;
            float k = 1f - Mathf.Clamp01(_deathLeft / Mathf.Max(DeathDelay, 0.01f));
            if (_secondaryTimer <= 0f)
            {
                _secondaryTimer = SecondaryInterval;
                Vector3 at = top + Vector3.down * (DeathDrop * k);
                if (Blast) { Blast.PlaySecondary(at + new Vector3(Random.Range(-14f, 14f), Random.Range(-10f, 10f), Random.Range(-14f, 14f))); }
            }
            if (_deathLeft > 0f) { return; }
            _dying = false;
            _gone = true;
            _goneAt = Time.time;
            if (Blast) { Blast.Play(top, false, 1f); }
            transform.position = transform.position + Vector3.down * 3000f;
            if (Phase != null && Phase.IsLive()) { Phase.SayImpact(); }
        }

        public float GetImpactAge() { return _gone ? Time.time - _goneAt : -1f; }

        // ───────────── 단계 표현 ─────────────

        private void ApplyStage(int stage)
        {
            _appliedStage = stage;
            if (Warship != null) { Warship.MarkerAnchor = stage >= STAGE_LENS ? LensPivot : null; }
            SetExposed(EcmTrucks, true);
            if (EcmTrucks != null && _siteVoid != null) { for (int i = 0; i < EcmTrucks.Length && i < _siteVoid.Length; i++) { if (_siteVoid[i] && EcmTrucks[i] != null) { EcmTrucks[i].SetExposed(false); } } }
            SetExposed(Vents, stage >= STAGE_VENTS);
            SetExposed(Airships, true);
            // 1단계: 환풍구에 JAMMED(마커만 — 락온 · 피해 없음)
            if (Vents != null) { for (int i = 0; i < Vents.Length; i++) { if (Vents[i] != null) { Vents[i].SetJammed(stage == STAGE_ECM); } } }
        }

        private void SetExposed(ACO_FortressPart[] parts, bool on)
        {
            if (parts == null) { return; }
            for (int i = 0; i < parts.Length; i++) { if (parts[i] != null) { parts[i].SetExposed(on); } }
        }

        /// 렌즈 과열 창: 열리면 렌즈 히트박스 · 락온 마커를 켠다.
        private void SetLens(bool open)
        {
            _lensOpen = open;
            if (CoreHitbox) { CoreHitbox.enabled = open; }
            if (CoreMarkers != null) { for (int i = 0; i < CoreMarkers.Length; i++) { if (CoreMarkers[i]) { CoreMarkers[i].SetActive(open); } } }
            if (OverheatGlow != null)
            {
                OverheatGlow.SetActive(open);
                OverheatGlow.transform.localScale = Vector3.one;
            }
        }

        // ───────────── 빔(패턴 A · B — 모든 클라이언트) ─────────────

        private void UpdateBeams(int now, float dt)
        {
            int kind = Phase.GetPatternKind();
            int pat = Phase.GetPatternStartMs();
            if (pat != _patSeen || kind != _kindSeen)
            {
                _patSeen = pat;
                _kindSeen = kind;
                StartPattern(kind);
            }
            bool aiming = Phase.IsAiming(now);
            bool firing = Phase.IsFiring(now);
            Vector3 lens = LensPivot != null ? LensPivot.position : transform.position + Vector3.up * 806f;
            _jamTimer -= dt;
            if (_jamTimer <= 0f)
            {
                _jamTimer = 0.25f;
                _jammed = Contacts != null && Contacts.IsJammed(lens);
            }
            int snd = aiming ? 1 : (firing ? 2 : 0);
            if (kind == ACO_PhaseLaserTower.PATTERN_B && CountAirshipsAlive() == 0) { snd = 0; }   // 그물 도중 비행선이 다 격추되면 빔이 없다 — 발사음도 끈다
            SetSound(snd);
            if (kind == ACO_PhaseLaserTower.PATTERN_A && (aiming || firing))
            {
                HideNet();
                UpdatePatternA(now, dt, lens, firing);
            }
            else if (kind == ACO_PhaseLaserTower.PATTERN_B && (aiming || firing))
            {
                ShowBeamA(-1);
                UpdatePatternB(now, dt, lens, firing);
            }
            else
            {
                if (_beamsShown) { HideBeams(); }
                IdleHead(dt);
            }
        }

        private void StartPattern(int kind)
        {
            _fireStarted = false;
            _nextHitAt = 0f;
            _losTimer = 0f;
            _cutTimer = 0f;
            Vector3 lens = LensPivot != null ? LensPivot.position : transform.position + Vector3.up * 806f;
            // 조준점: 지금 렌즈가 보는 방향, 표적 거리에서 시작 — 표적 쪽으로 부드럽게 돈다
            Vector3 tp = Vector3.zero;
            bool has = false;
            if (kind == ACO_PhaseLaserTower.PATTERN_A) { has = TargetPosition(Phase.GetPatternTarget(), out tp); }
            if (!has) { tp = lens + _headDir * 3000f; }
            _aimPoint = lens + _headDir * Mathf.Max(Vector3.Distance(lens, tp), 100f);
            _seenPoint = has ? tp : _aimPoint;
            _baseDir = _headDir;
            if (kind == ACO_PhaseLaserTower.PATTERN_B) { BuildNetOrder(); }
        }

        private void UpdatePatternA(int now, float dt, Vector3 lens, bool firing)
        {
            Vector3 tp;
            bool has = TargetPosition(Phase.GetPatternTarget(), out tp);
            if (has && Voice != null && IsLocalTarget(Phase.GetPatternTarget())) { Voice.Warn(ACO_VoiceWarning.W_LASER); }
            if (!firing)
            {
                // 조준: 보이는 동안만 표적 자리를 갱신(산 · 탑 뒤로 숨으면 마지막 자리로 그대로)
                _losTimer -= dt;
                if (has && _losTimer <= 0f)
                {
                    _losTimer = 0.2f;
                    Vector3 d = tp - lens;
                    if (!Physics.Raycast(lens, d.normalized, d.magnitude, GroundLayers.value, QueryTriggerInteraction.Ignore) && !BlockedByTower(lens, tp)) { _seenPoint = tp; }
                }
                _aimPoint = Vector3.Lerp(_aimPoint, _seenPoint, 1f - Mathf.Exp(-dt / Mathf.Max(AimLag, 0.01f)));
                Vector3 to = _aimPoint - lens;
                if (to.sqrMagnitude > 1f) { _baseDir = ClampPitch(to.normalized); }
            }
            else
            {
                if (!_fireStarted) { _fireStarted = true; _nextHitAt = 0f; }
                if (has)
                {
                    Vector3 want = ClampPitch((tp - lens).normalized);
                    float rate = _jammed ? JamTrackRate : TrackRateA;
                    _baseDir = Vector3.RotateTowards(_baseDir, want, rate * Mathf.Deg2Rad * dt, 0f);
                }
            }
            Vector3 dir = Wobble(_baseDir, now, _jammed ? JamWobble : BaseWobble);   // 기본 흔들림 + ECM이면 더 크게
            AimHead(dir);
            Vector3 from = Muzzle != null ? Muzzle.position : lens;
            float length = CastBeam(from, dir, BeamLengthA);
            bool hitA = _castHit;
            bool waterA = _castWater;
            Vector3 end = from + dir * length;
            ShowBeamA(firing ? 1 : 0);
            if (BeamA != null)
            {
                BeamA.SetPosition(0, from);
                BeamA.SetPosition(1, end);
                // 아무 데도 안 닿고 끝까지 뻗으면 끝을 흐리게
                int style = (firing ? 2 : 0) + (hitA ? 0 : 1);
                if (style != _styleA) { _styleA = style; BeamA.colorGradient = StyleGradient(style); }
            }
            if (firing && CanHit()) { HitIfNear(from, end, BeamRadiusA, hitA); }
            if (firing) { ReportFlyby(from, end); }
            if (firing && hitA && FxTick(dt)) { EmitHitFx(end, waterA, ImpactScaleA); }
        }

        /// 그물 순서: 비행선 전체(생사와 관계없이 — 격추 동기화가 화면마다 조금 늦게 와도 같은 순서) 중 시드로 하나 → 그다음은 아직 안 거친 가장 가까운 비행선(발사 시각 자리 기준 — 모든 화면 같다).
        /// 격추된 비행선은 그릴 때 NextAliveInOrder가 건너뛴다.
        private void BuildNetOrder()
        {
            _netCount = 0;
            int n = Airships != null ? Mathf.Min(Airships.Length, _netOrder.Length) : 0;
            if (n == 0) { return; }
            int seed = Phase.GetPatternSeed();
            int fireMs = Phase.GetFireMs();
            int first = Mathf.Min(Mathf.FloorToInt(Hash(seed, 1) * n), n - 1);
            _netOrder[_netCount++] = first;
            int cur = first;
            while (_netCount < n)
            {
                Vector3 cp = GetAirshipPosition(cur, fireMs);
                int best = -1;
                float bestSq = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (InOrder(i)) { continue; }
                    float sq = (GetAirshipPosition(i, fireMs) - cp).sqrMagnitude;
                    if (best < 0 || sq < bestSq) { best = i; bestSq = sq; }
                }
                if (best < 0) { break; }
                _netOrder[_netCount++] = best;
                cur = best;
            }
        }

        private bool InOrder(int i)
        {
            for (int k = 0; k < _netCount; k++) { if (_netOrder[k] == i) { return true; } }
            return false;
        }

        /// 그물: 렌즈 → 첫 반사경 → (다음 반사경 + 바깥 갈래) … 격추된 비행선은 건너뛴다(그 비행선 갈래만 사라지고 앞 반사경이 다음 반사경으로 잇는다).
        private void UpdatePatternB(int now, float dt, Vector3 lens, bool firing)
        {
            _segCount = 0;
            int max = _segFrom.Length;
            int seed = Phase.GetPatternSeed();
            float spin = (now - Phase.GetFireMs()) * 0.001f * NetSpinRate;
            _cutTimer -= dt;
            bool recut = _cutTimer <= 0f;
            int aliveNow = CountAirshipsAlive();
            if (aliveNow != _cutAlive) { _cutAlive = aliveNow; recut = true; }   // 격추로 갈래 순번이 밀렸다 — 다른 갈래 길이를 쓰지 않게
            if (recut) { _cutTimer = 0.15f; }
            int k = NextAliveInOrder(-1);
            if (k < 0)
            {
                HideNet();
                IdleHead(dt);
                return;
            }
            Vector3 node = MirrorPosition(_netOrder[k]);
            AimHead(ClampPitch((node - lens).normalized));
            AddSeg(Muzzle != null ? Muzzle.position : lens, node, max);
            while (k >= 0)
            {
                int next = NextAliveInOrder(k);
                Vector3 nextPos = next >= 0 ? MirrorPosition(_netOrder[next]) : node;
                int outward = NetBranches - (next >= 0 ? 1 : 0);
                if (next >= 0) { AddSeg(node, nextPos, max); }
                for (int j = 0; j < outward; j++)
                {
                    int idx = _segCount;
                    if (idx >= max) { break; }
                    int h = 100 + _netOrder[k] * 16 + j * 3;
                    // 안쪽(탑) 경향: 탑의 한 높이를 겨눈 방향에서 좌우로 벗어나되 0° 쪽으로 몰린다(u·|u|) — 탑 둘레를 감싸고 지나간다
                    Vector3 aim = transform.position + Vector3.up * Mathf.Lerp(NetAimLow, NetAimHigh, Hash(seed, h + 2)) - node;
                    if (aim.sqrMagnitude < 1f) { aim = Vector3.forward; }
                    float u = Hash(seed, h) * 2f - 1f;
                    float yaw = u * Mathf.Abs(u) * NetSpread + spin * (Hash(seed, h + 1) < 0.5f ? 1f : -1f);
                    float pitch = (Hash(seed, h + 50) - 0.5f) * 2f * NetPitchJitter;
                    Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * aim.normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    if (side.sqrMagnitude > 0.0001f) { dir = Quaternion.AngleAxis(-pitch, side.normalized) * dir; }
                    float len = NetBranchLength;
                    if (recut)
                    {
                        _cutLen[idx] = CastBeam(node, dir, len);
                        _cutHit[idx] = _castHit;
                        _cutWater[idx] = _castWater;
                    }
                    float cut = _cutLen[idx] > 0f ? Mathf.Min(_cutLen[idx], len) : len;
                    AddSeg(node, node + dir * cut, max);
                    _segFade[idx] = !_cutHit[idx];
                    _segHit[idx] = _cutHit[idx];
                    _segWater[idx] = _cutWater[idx];
                }
                if (next < 0) { break; }
                k = next;
                node = nextPos;
            }
            ShowNet(firing);
            _warnTimer -= dt;
            if (Voice != null && _warnTimer <= 0f)
            {
                _warnTimer = 0.2f;
                if (LocalNearNet(WarnRadiusB)) { Voice.Warn(ACO_VoiceWarning.W_LASER); }
            }
            if (firing)
            {
                if (!_fireStarted) { _fireStarted = true; _nextHitAt = 0f; }
                if (CanHit())
                {
                    for (int i = 0; i < _segCount; i++) { if (HitIfNear(_segFrom[i], _segTo[i], BeamRadiusB, _segHit[i])) { break; } }
                }
                if (FxTick(dt))
                {
                    for (int i = 0; i < _segCount; i++) { if (_segHit[i]) { EmitHitFx(_segTo[i], _segWater[i], 1f); } }
                }
                ReportNetFlyby();
            }
        }

        private int NextAliveInOrder(int k)
        {
            for (int m = k + 1; m < _netCount; m++) { if (IsAirshipAlive(_netOrder[m])) { return m; } }
            return -1;
        }

        private Vector3 MirrorPosition(int i)
        {
            return Airships[i].transform.TransformPoint(MirrorCenter);
        }

        private void AddSeg(Vector3 a, Vector3 b, int max)
        {
            if (_segCount >= max) { return; }
            _segFrom[_segCount] = a;
            _segTo[_segCount] = b;
            _segFade[_segCount] = false;
            _segHit[_segCount] = false;
            _segWater[_segCount] = false;
            _segCount++;
        }

        private void ShowNet(bool firing)
        {
            if (NetLines == null) { return; }
            int state = firing ? 1 : 0;
            for (int i = 0; i < NetLines.Length; i++)
            {
                LineRenderer l = NetLines[i];
                if (l == null) { continue; }
                bool on = i < _segCount;
                bool turnedOn = on && !l.enabled;
                if (l.enabled != on) { l.enabled = on; }
                if (!on) { continue; }
                if (_drawnB != state || turnedOn) { l.widthMultiplier = firing ? FireWidthB : AimWidthB; }
                int style = state * 2 + (_segFade[i] ? 1 : 0);
                if (_lineStyle[i] != style)
                {
                    _lineStyle[i] = style;
                    l.colorGradient = StyleGradient(style);
                }
                l.SetPosition(0, _segFrom[i]);
                l.SetPosition(1, _segTo[i]);
            }
            _drawnB = state;
            _beamsShown = true;
        }

        private void HideNet()
        {
            if (_drawnB < 0) { return; }
            _drawnB = -1;
            if (NetLines == null) { return; }
            for (int i = 0; i < NetLines.Length; i++) { if (NetLines[i] != null) { NetLines[i].enabled = false; } }
        }

        /// state: -1 끔, 0 조준(얇게), 1 발사(굵게).
        private void ShowBeamA(int state)
        {
            if (state == _drawnA) { return; }
            _drawnA = state;
            if (BeamA == null) { return; }
            BeamA.enabled = state >= 0;
            if (state < 0) { return; }
            BeamA.widthMultiplier = state == 1 ? FireWidthA : AimWidthA;
            _beamsShown = true;
        }

        /// 선 색: style = (발사면 2) + (끝 흐림이면 1).
        private Gradient StyleGradient(int style)
        {
            if (style == 3) { return _gFireFade; }
            if (style == 2) { return _gFire; }
            if (style == 1) { return _gAimFade; }
            return _gAim;
        }

        private Gradient MakeGradient(Color c, bool fade)
        {
            Gradient g = new Gradient();
            GradientColorKey[] ck = new GradientColorKey[2];
            ck[0] = new GradientColorKey(c, 0f);
            ck[1] = new GradientColorKey(c, 1f);
            GradientAlphaKey[] ak = new GradientAlphaKey[fade ? 3 : 2];
            ak[0] = new GradientAlphaKey(c.a, 0f);
            if (fade)
            {
                ak[1] = new GradientAlphaKey(c.a, Mathf.Clamp(FadeStart, 0.01f, 0.98f));
                ak[2] = new GradientAlphaKey(0f, 1f);
            }
            else { ak[1] = new GradientAlphaKey(c.a, 1f); }
            g.SetKeys(ck, ak);
            return g;
        }

        /// 빔을 from에서 dir로 최대 len까지: 땅(GroundLayers)이나 해수면에 닿으면 거기까지. _castHit · _castWater에 결과.
        private float CastBeam(Vector3 from, Vector3 dir, float len)
        {
            _castHit = false;
            _castWater = false;
            float cut = len;
            RaycastHit hit;
            if (Physics.Raycast(from, dir, out hit, len, GroundLayers.value, QueryTriggerInteraction.Ignore))
            {
                cut = hit.distance;
                _castHit = true;
            }
            // 탑 몸체(Hull — 주탑 · 보조탑 · 헤드 · 기초판)에도 막힌다(비행선 · 부위 히트박스 · 기체는 같은 레이어지만 막지 않는다)
            float hd = HullDistance(from, dir, cut);
            if (hd >= 0f)
            {
                cut = hd;
                _castHit = true;
                _castWater = _hullY > transform.position.y + 5f;   // 탑 벽이면 불티 · 빛만(누운 그을림은 땅 · 기초판 위에만 — 물 표시를 빌린다)
            }
            // 바닷속 지형까지 가지 않고 해수면에서 끊는다
            if (dir.y < -0.0001f && from.y > SeaLevel)
            {
                float t = (SeaLevel - from.y) / dir.y;
                if (t < cut)
                {
                    cut = t;
                    _castHit = true;
                    _castWater = true;
                }
            }
            return cut;
        }

        /// 효과 박자(HitFxInterval초마다 true).
        private bool FxTick(float dt)
        {
            _fxTimer -= dt;
            if (_fxTimer > 0f) { return false; }
            _fxTimer = HitFxInterval;
            _fxCount++;
            return true;
        }

        /// 빔이 닿은 자리: 빛 · 불티, 땅이면 몇 번에 한 번 그을림(모든 화면 — 빔 자리가 같으니 거의 같은 자리에 남는다).
        private void EmitHitFx(Vector3 p, bool water, float k)
        {
            if (HitGlow != null)
            {
                var g = new ParticleSystem.EmitParams();
                g.position = p + Vector3.up * 3f;
                g.startSize = 55f * k;
                HitGlow.Emit(g, 1);
            }
            if (HitSparks != null)
            {
                for (int n = 0; n < 3; n++)
                {
                    var s = new ParticleSystem.EmitParams();
                    s.position = p + Vector3.up * 2f;
                    Vector3 v = Random.insideUnitSphere + Vector3.up * 1.2f;
                    s.velocity = v.normalized * Random.Range(60f, 180f) * k;
                    s.startSize = Random.Range(2.5f, 5f) * k;
                    s.startLifetime = Random.Range(0.6f, 1.4f);
                    HitSparks.Emit(s, 1);
                }
            }
            if (!water && Scorch != null && ScorchEvery > 0 && _fxCount % ScorchEvery == 0)
            {
                var c = new ParticleSystem.EmitParams();
                c.position = p + Vector3.up * 1.5f;
                c.rotation = Random.Range(0f, 360f);
                c.startSize = Random.Range(ScorchSizeMin, ScorchSizeMax) * k;
                Scorch.Emit(c, 1);
            }
        }

        /// 소리 상태: 0 조용, 1 충전(충전음 처음부터), 2 발사(충전음 끄고 발사음 처음부터). 바뀔 때만.
        private void SetSound(int state)
        {
            if (state == _soundState) { return; }
            _soundState = state;
            if (ChargeSound != null) { if (state == 1) { ChargeSound.Play(); } else { ChargeSound.Stop(); } }
            if (FireSound != null) { if (state == 2) { FireSound.Play(); } else { FireSound.Stop(); } }
        }

        /// 굵은 빔(a → b)이 내 기체 곁을 지나면 스침 전자음(간격 · 거리 조건은 ACO_BulletFlyby가 본다).
        private void ReportFlyby(Vector3 a, Vector3 b)
        {
            if (Flyby == null || Contacts == null || Contacts.LocalVehicle == null) { return; }
            Vector3 p = Contacts.LocalVehicle.transform.position;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0.01f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            Vector3 c = a + ab * t;
            float miss = Vector3.Distance(c, p);
            if (miss <= Flyby.MissRadius) { Flyby.ReportLaser(c, miss); }
        }

        /// 그물: 가장 가까운 갈래 하나로 스침 전자음.
        private void ReportNetFlyby()
        {
            if (Flyby == null || Contacts == null || Contacts.LocalVehicle == null) { return; }
            Vector3 p = Contacts.LocalVehicle.transform.position;
            float best = Flyby.MissRadius + 1f;
            Vector3 bestPoint = p;
            for (int i = 0; i < _segCount; i++)
            {
                Vector3 a = _segFrom[i];
                Vector3 ab = _segTo[i] - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 0.01f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
                Vector3 c = a + ab * t;
                float d = Vector3.Distance(c, p);
                if (d < best) { best = d; bestPoint = c; }
            }
            if (best <= Flyby.MissRadius) { Flyby.ReportLaser(bestPoint, best); }
        }

        private void HideBeams()
        {
            SetSound(0);
            _drawnA = 0;
            ShowBeamA(-1);
            _drawnB = 0;
            HideNet();
            _beamsShown = false;
            _segCount = 0;
        }

        /// 피해를 줄 수 있는가(매 프레임 판정 — 지난 피해에서 DamageTick초가 지났다). 스치기만 해도 그 프레임에 맞는다.
        private bool CanHit() { return Time.time >= _nextHitAt; }

        /// 내 기체가 갈래(a → b) radius 안이면 피해(맞는 사람 화면이 판정 — 랜스와 같다)하고 DamageTick초 동안 쉰다. 맞았으면 true.
        /// cut: 빔이 땅 · 탑 · 바다에 막혀 b에서 끊겼다 — 그 너머(b 뒤)에 있는 기체는 가려져서 맞지 않는다(지형 · 탑 뒤 엄폐).
        private bool HitIfNear(Vector3 a, Vector3 b, float radius, bool cut)
        {
            if (Contacts == null || Contacts.LocalVehicle == null) { return false; }
            SaccEntity e = Contacts.LocalVehicle;
            if (e.dead || !e.Piloting) { return false; }   // 승객 화면은 판정하지 않는다(2인승에서 피해 두 배였다)
            if (LocalTaxiing(e)) { return false; }          // 착함 · 활주로 대기(지상 활주) 중이면 피해 없음
            Vector3 p = e.transform.position;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float raw = len2 > 0.01f ? Vector3.Dot(p - a, ab) / len2 : 0f;
            if (cut && raw > 1f) { return false; }
            float t = Mathf.Clamp01(raw);
            if ((a + ab * t - p).sqrMagnitude > radius * radius) { return false; }
            e.WeaponDamageVehicle(BeamDps * DamageTick, DamageSource != null ? DamageSource : gameObject, 0);
            _nextHitAt = Time.time + DamageTick;
            return true;
        }

        /// 헤드(좌우) · 렌즈(위아래)를 dir로. 렌즈 기본 방향은 +Z.
        private void AimHead(Vector3 dir)
        {
            _headDir = dir;
            if (HeadPivot != null)
            {
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude > 0.0001f) { HeadPivot.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up); }
            }
            if (LensPivot != null) { LensPivot.localRotation = Quaternion.Euler(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, 0f, 0f); }
        }

        /// 쉬는 동안: 렌즈를 천천히 수평 위 5°로 되돌린다.
        private void IdleHead(float dt)
        {
            Vector3 flat = new Vector3(_headDir.x, 0f, _headDir.z);
            if (flat.sqrMagnitude < 0.0001f) { flat = transform.forward; }
            flat = flat.normalized;
            Vector3 rest = flat * Mathf.Cos(5f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(5f * Mathf.Deg2Rad);
            if (Vector3.Angle(rest, _headDir) < 0.05f) { return; }
            AimHead(Vector3.RotateTowards(_headDir, rest, IdleTurnRate * Mathf.Deg2Rad * dt, 0f));
        }

        private Vector3 ClampPitch(Vector3 dir)
        {
            float min = Mathf.Sin(MinPitch * Mathf.Deg2Rad);
            if (dir.y >= min) { return dir; }
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 0.0001f) { flat = transform.forward; }
            flat = flat.normalized;
            return flat * Mathf.Cos(MinPitch * Mathf.Deg2Rad) + Vector3.up * min;
        }

        /// 플레이어 ECM 흔들림(대공포와 같은 펄린 — 서버 시각이라 화면마다 같다).
        private Vector3 Wobble(Vector3 dir, int now, float amount)
        {
            if (amount <= 0f) { return dir; }
            float t = (now - (Phase != null ? Phase.GetStamp() : 0)) * 0.001f * 0.8f;   // 페이즈 시작 기준(서버 시각 % 10분이라 10분마다 튀었다)
            float yaw = (Mathf.PerlinNoise(t, 3.1f) - 0.5f) * 2f * amount;
            float pitch = (Mathf.PerlinNoise(7.7f, t) - 0.5f) * 2f * amount;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 0.0001f) { right = Vector3.right; }
            right = right.normalized;
            return Quaternion.AngleAxis(yaw, Vector3.up) * (Quaternion.AngleAxis(pitch, right) * dir);
        }

        /// 패턴 A 표적이 내가 탄 기체인가.
        private bool IsLocalTarget(int i)
        {
            if (Contacts == null || Contacts.LocalVehicle == null || Contacts.Flow == null || Contacts.Flow.Aircraft == null) { return false; }
            if (i < 0 || i >= Contacts.Flow.Aircraft.Length) { return false; }
            return Contacts.Flow.Aircraft[i] == Contacts.LocalVehicle;
        }

        /// 그물 갈래 하나라도 내 기체에서 radius 안인가.
        private bool LocalNearNet(float radius)
        {
            if (Contacts == null || Contacts.LocalVehicle == null || Contacts.LocalVehicle.dead) { return false; }
            Vector3 p = Contacts.LocalVehicle.transform.position;
            float rSq = radius * radius;
            for (int i = 0; i < _segCount; i++)
            {
                Vector3 a = _segFrom[i];
                Vector3 ab = _segTo[i] - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 0.01f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
                if ((a + ab * t - p).sqrMagnitude <= rSq) { return true; }
            }
            return false;
        }

        /// 참가자 기체 i의 자리(타고 있고 살아 있을 때만).
        private bool TargetPosition(int i, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (i < 0 || Contacts == null || Contacts.Flow == null || Contacts.Flow.Aircraft == null || i >= Contacts.Flow.Aircraft.Length) { return false; }
            SaccEntity e = Contacts.Flow.Aircraft[i];
            if (e == null || !e.Occupied || e.dead) { return false; }
            pos = e.transform.position;
            if (InSafeZone(pos)) { return false; }   // 항모 · 기지 안(갑판 대기 등)은 표적도 아니고 패턴도 쉰다
            return true;
        }

        // ───────────── 부위 상태(모듈이 읽는다) ─────────────

        /// ECM 차량 수(놓을 땅이 없어 파괴 처리한 기지는 뺀다 — "ECM VEHICLE 0/2").
        public int GetEcmTotal()
        {
            int n = 0;
            if (EcmTrucks == null) { return 0; }
            for (int i = 0; i < EcmTrucks.Length; i++) { if (EcmTrucks[i] != null && !IsSiteVoid(i)) { n++; } }
            return n;
        }

        public int CountEcmAlive()
        {
            int n = 0;
            if (EcmTrucks == null) { return 0; }
            for (int i = 0; i < EcmTrucks.Length; i++) { if (EcmTrucks[i] != null && !IsSiteVoid(i) && !EcmTrucks[i].IsDestroyed()) { n++; } }
            return n;
        }

        /// t가 파괴 처리한 ECM 기지 안의 부위인가(생성기 · 관리자가 점수에서 뺄 때).
        public bool IsInVoidSite(Transform t)
        {
            if (t == null || _siteVoid == null || EcmSites == null) { return false; }
            for (int s = 0; s < _siteVoid.Length && s < EcmSites.Length; s++)
            {
                if (_siteVoid[s] && EcmSites[s] != null && t.IsChildOf(EcmSites[s])) { return true; }
            }
            return false;
        }

        private bool IsSiteVoid(int s) { return _siteVoid != null && s >= 0 && s < _siteVoid.Length && _siteVoid[s]; }
        public int GetVentTotal() { return Vents != null ? Vents.Length : 0; }
        public int CountVentsAlive() { return CountAlive(Vents); }
        public int GetAirshipTotal() { return Airships != null ? Airships.Length : 0; }
        public int CountAirshipsAlive() { return CountAlive(Airships); }

        private int CountAlive(ACO_FortressPart[] parts)
        {
            if (parts == null) { return 0; }
            int n = 0;
            for (int i = 0; i < parts.Length; i++) { if (parts[i] != null && !parts[i].IsDestroyed()) { n++; } }
            return n;
        }

        public int CountTowerDefenseAlive()
        {
            Init();
            int n = 0;
            for (int i = 0; i < _defenseHealth.Length; i++) { if (_defenseHealth[i] != null && _defenseHealth[i].Health > 0f) { n++; } }
            return n;
        }

        public float GetCoreHealthRatio()
        {
            if (TargetHealth == null || TargetHealth.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(TargetHealth.Health / TargetHealth.FullHealth);
        }

        /// 부서진 탑 둘레 방공을 count곳 되살린다(수리) — 무작위 자리부터, 모든 클라이언트에 알린다.
        public int RepairTowerDefense(int count)
        {
            Init();
            int n = _defenseHealth.Length;
            if (n == 0) { return 0; }
            int start = Random.Range(0, n);
            int done = 0;
            for (int k = 0; k < n && done < count; k++)
            {
                int i = (start + k) % n;
                if (TowerDefense[i] == null || _defenseHealth[i] == null || _defenseHealth[i].Health > 0f) { continue; }
                TowerDefense[i].SendCustomNetworkEvent(NetworkEventTarget.All, "NetRespawn");
                done++;
            }
            return done;
        }

        /// from → to 사이를 이 탑의 몸체(Hull · 부위 히트박스)가 막는가(보조탑 · 주탑 뒤에 숨은 표적).
        private bool BlockedByTower(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1f) { return false; }
            return HullDistance(from, d / len, len) >= 0f;
        }

        private float _hullY;   // HullDistance가 찾은 자리의 높이

        /// from에서 dir로 len 안에 탑 몸체(Hull 아래 콜라이더)가 있으면 가장 가까운 거리, 없으면 -1. 같은 레이어(17)의 기체 · 비행선 · 부위 히트박스는 건너뛴다(가장 가까운 하나만 주는 Raycast로는 그 뒤의 몸체를 놓쳤다).
        private float HullDistance(Vector3 from, Vector3 dir, float len)
        {
            if (_hullRoot == null || _hullHits == null) { return -1f; }
            int n = Physics.RaycastNonAlloc(from, dir, _hullHits, len, HULL_MASK, QueryTriggerInteraction.Ignore);
            float best = -1f;
            for (int i = 0; i < n && i < _hullHits.Length; i++)
            {
                Collider col = _hullHits[i].collider;
                if (col == null || !col.transform.IsChildOf(_hullRoot)) { continue; }
                float d = _hullHits[i].distance;
                if (best < 0f || d < best) { best = d; _hullY = _hullHits[i].point.y; }
            }
            return best;
        }

        /// ECM 기지 s를 넓게 찾는다: 탑 둘레 600m ~ ECM_WIDE_MAX에서 시드 순서로 ECM_WIDE_TRIES곳, 놓을 수 있는 뭍(생성기 검사 — 작전 지역 · 안전지대 · 건물 · 경사 · 물) 중 원래 자리(want)에 가장 가까운 곳.
        private bool FindWideEcmSite(int seed, int s, Vector3 c, Vector3 want, out Vector3 site)
        {
            site = want;
            ACO_MissionGenerator gen = Phase != null ? Phase.Generator : null;
            if (gen == null) { return false; }
            bool found = false;
            float bestSq = 0f;
            for (int t = 0; t < ECM_WIDE_TRIES; t++)
            {
                float ang = Hash(seed, 1000 + s * 200 + t * 2) * Mathf.PI * 2f;
                float dist = Mathf.Lerp(600f, ECM_WIDE_MAX, Mathf.Sqrt(Hash(seed, 1001 + s * 200 + t * 2)));
                float x = c.x + Mathf.Sin(ang) * dist;
                float z = c.z + Mathf.Cos(ang) * dist;
                if (!gen.IsPlaceableGround(x, z)) { continue; }
                float y = gen.GetSampledHeight();
                if (y <= SeaLevel + 2f) { continue; }
                float dx = x - want.x;
                float dz = z - want.z;
                float sq = dx * dx + dz * dz;
                if (!found || sq < bestSq) { found = true; bestSq = sq; site = new Vector3(x, y, z); }
            }
            return found;
        }

        /// ECM 기지의 차량 · 방공을 각자 그 자리 땅 높이에 붙인다(경사면에서 뜨거나 묻히지 않게). 처음 높이(로컬 0)에서 다시 잰다.
        /// 자리가 물이면 차량 쪽으로 반 · 4분의 1로 당겨 다시 찾는다(그래도 물이면 처음 자리 그대로). 처음 자리는 Init에서 기억한다.
        private void SnapSiteToGround(int s)
        {
            Transform site = EcmSites[s];
            for (int k = 0; k < site.childCount; k++)
            {
                Transform ch = site.GetChild(k);
                Vector3 home = (_snapHome != null && k < SNAP_MAX && s * SNAP_MAX + k < _snapHome.Length) ? _snapHome[s * SNAP_MAX + k] : ch.localPosition;
                home.y = 0f;
                bool placed = false;
                for (int tries = 0; tries < 3 && !placed; tries++)
                {
                    float scale = tries == 0 ? 1f : (tries == 1 ? 0.5f : 0.25f);
                    ch.localPosition = home * scale;
                    Vector3 wp = ch.position;
                    RaycastHit h;
                    if (Physics.Raycast(new Vector3(wp.x, wp.y + 600f, wp.z), Vector3.down, out h, 1200f, GroundLayers.value, QueryTriggerInteraction.Ignore) && h.point.y > SeaLevel + 0.5f)
                    {
                        ch.position = new Vector3(wp.x, h.point.y, wp.z);
                        placed = true;
                    }
                }
                if (!placed) { ch.localPosition = home; }
            }
        }

        /// 놓을 땅이 없어 파괴 처리한 ECM 기지의 차량 · 방공을 부순다(Silence — 오너만 실제로 터뜨린다, 점수 없음). 부위가 되살아나도(스폰 순서) 1초마다 다시.
        private void SilenceVoidSites()
        {
            if (_siteVoid == null || _partSite == null || Warship == null || Warship.Parts == null) { return; }
            if (Phase == null || !Phase.IsLive() || Phase.GetSeed() != _placedSeed) { return; }
            for (int k = 0; k < _partSite.Length && k < Warship.Parts.Length; k++)
            {
                int s = _partSite[k];
                if (s < 0 || s >= _siteVoid.Length || !_siteVoid[s] || Warship.Parts[k] == null) { continue; }
                Warship.Parts[k].SendCustomEvent("Silence");
            }
        }

        /// 안전 구역(항모 · 기지 — ACO_PlayerProgress.SafeZones) 안인가. 안전 구역을 하나도 안 정했으면 아무 데도 아니다(그 경우 IsInSafeZone은 어디든 안전이라 쓰지 않는다).
        private bool InSafeZone(Vector3 p)
        {
            if (Contacts == null || Contacts.Flow == null || Contacts.Flow.Progress == null) { return false; }
            ACO_PlayerProgress pr = Contacts.Flow.Progress;
            if (pr.SafeZones == null || pr.SafeZones.Length == 0) { return false; }
            return pr.IsInSafeZone(p);
        }

        private SaccEntity _taxiEntity;
        private SaccAirVehicle _taxiSav;

        /// 내 기체가 땅 · 갑판 위를 굴러가는 중인가(SaccAirVehicle.Taxiing — 기체가 바뀔 때만 찾는다).
        private bool LocalTaxiing(SaccEntity e)
        {
            if (e != _taxiEntity)
            {
                _taxiEntity = e;
                _taxiSav = (SaccAirVehicle)e.GetExtention(GetUdonTypeName<SaccAirVehicle>());
            }
            return _taxiSav != null && _taxiSav.Taxiing;
        }

        /// 패턴 A 표적이 아직 유효한가(타고 있고 살아 있고 렌즈에서 range 안).
        public bool IsTargetValid(int i, float range)
        {
            Vector3 p;
            if (!TargetPosition(i, out p)) { return false; }
            Vector3 from = Muzzle != null ? Muzzle.position : transform.position + Vector3.up * 806f;
            return (p - from).sqrMagnitude <= range * range;
        }

        /// 참가자 기체 i까지의 렌즈(머즐) 거리(m), 표적이 아니면 -1 — 패턴 고르기(가까우면 그물, 멀면 직사).
        public float GetTargetDistance(int i)
        {
            Vector3 p;
            if (!TargetPosition(i, out p)) { return -1f; }
            Vector3 from = Muzzle != null ? Muzzle.position : transform.position + Vector3.up * 806f;
            return Vector3.Distance(p, from);
        }

        /// 렌즈에서 range 안에 날고 있는 조종사가 있는가(없으면 패턴을 쉰다).
        public bool AnyPilotNear(float range)
        {
            if (Contacts == null || Contacts.Flow == null || Contacts.Flow.Aircraft == null) { return false; }
            SaccEntity[] a = Contacts.Flow.Aircraft;
            for (int i = 0; i < a.Length; i++) { if (IsTargetValid(i, range)) { return true; } }
            return false;
        }

        /// 패턴 A 표적 후보: 렌즈에서 range 안, 렌즈가 볼 수 있는(지형에 안 막힌) 참가자 기체 중 하나. 직전 표적(avoid)은 다른 후보가 있으면 피한다. 없으면 -1.
        public int PickTarget(float range, int avoid)
        {
            if (Contacts == null || Contacts.Flow == null || Contacts.Flow.Aircraft == null) { return -1; }
            SaccEntity[] a = Contacts.Flow.Aircraft;
            Vector3 from = Muzzle != null ? Muzzle.position : transform.position + Vector3.up * 806f;
            int count = 0;
            int pickAvoid = -1;
            int[] cands = new int[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null || !a[i].Occupied || a[i].dead) { continue; }
                if (InSafeZone(a[i].transform.position)) { continue; }
                Vector3 d = a[i].transform.position - from;
                if (d.sqrMagnitude > range * range) { continue; }
                if (Physics.Raycast(from, d.normalized, d.magnitude, GroundLayers.value, QueryTriggerInteraction.Ignore)) { continue; }
                if (BlockedByTower(from, a[i].transform.position)) { continue; }
                if (i == avoid) { pickAvoid = i; continue; }
                cands[count++] = i;
            }
            if (count == 0) { return pickAvoid; }
            return cands[Random.Range(0, count)];
        }
    }
}
