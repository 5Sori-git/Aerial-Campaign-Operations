using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 플레이어 기체용 미사일 다이얼 기능(SAV DFUNC_AAM 대체). SaccEntity의 Dial_Functions_L/R에 넣는다.
    ///
    /// - 표적: TargetLayers에 있는 씬 오브젝트(락온 마커). 빌드 시 ACO_SceneBaker가 Targets에 굽는다 — SaccEntity의
    ///   AAMTargetsLayer와 무관하게 대공(29)/대지(30)를 이 무기만의 설정으로 고를 수 있다.
    /// - 장전(에이스컴뱃 방식): 슬롯 SlotCount개. 장전된 슬롯마다 한 발씩 쏠 수 있고, 쏜 슬롯은 SlotReloadTime 뒤 다시 장전된다.
    ///   장전할 잔탄(예비탄)이 없으면 그 슬롯은 비어 있는다. 잔탄 표시 = 예비탄 + 장전된 탄.
    /// - 성장 요소: 피해/속도/가속/선회/유도/근접 신관/락온 거리·각도·시간/슬롯 수/장전 시간/총탄수는 모두 public 필드다.
    ///   외부 스크립트가 값을 바꾸면 다음 발사부터 적용된다(SlotCount·MaxAmmo를 바꾸면 ResetAmmo 호출).
    ///   비행 성능은 발사 이벤트에 실어 보내므로 다른 클라이언트에서도 같은 궤적으로 보인다. 피해는 쏜 사람만 쓴다.
    /// - 명중 판정은 쏜 사람이 한다(SAV와 같음) — 격추 점수가 SaccTarget.LastHitByPlayer로 매겨지기 때문.
    /// - 탄체: ACO_Missile(유도 미사일) 또는 ACO_Ordnance(무유도 폭탄/로켓, 범위 피해). 폭탄/로켓은 TargetLayers를 비우고
    ///   (락온 안 함) AllowUnguidedFire를 켜고, 로켓은 HoldToFire로 연사한다. 장전 슬롯·강화·장전 막대는 같다.
    /// - 다중 락온(MaxLockTargets ≥ 2, 4AAM 류): 각도 안의 표적을 칸 수만큼 동시에 잡아 각각 따로 락온하고, 트리거 한 번에
    ///   장전된 슬롯을 모두 일제 발사해 락온한 표적들에 차례로 나눠 쏜다. 특수무장 슬롯(ACO_DFUNC_Special)의 모듈로 쓰면
    ///   격납고에서 고른 무장으로 바뀐다(SetEquipped).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_DFUNC_Missile : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("SaccAirVehicle. 지상 활주 중 발사 금지와 발사 속도 상속에 쓴다")]
        public UdonSharpBehaviour SAVControl;
        [Tooltip("HUD 컨트롤러(SAV HUDController). 표적 표시 위치 기준. 비우면 머리 위치")]
        public UdonSharpBehaviour HUDControl;
        [Tooltip("락온 기준 방향. 비우면 기체 정면")]
        public Transform TargetingTransform;
        [Tooltip("데스크톱 발사 키")]
        public KeyCode FireKey = KeyCode.Space;

        [Header("표적")]
        [Tooltip("락온할 수 있는 레이어. 적 항공기 29, 적 지상 30. 빌드 시 이 레이어의 오브젝트가 Targets에 구워진다")]
        public LayerMask TargetLayers = (1 << 29) | (1 << 30);
        [Tooltip("빌드 시 ACO_SceneBaker가 채운다. 수동: ACO → Bake NPC References")]
        public GameObject[] Targets;
        [Tooltip("시야를 가리는 레이어(지형 등). 이 레이어가 표적보다 앞에 있으면 락온이 풀린다")]
        public LayerMask ObstacleLayers = 2049;
        [Tooltip("지상 활주 중에도 락온/발사를 허용한다")]
        public bool AllowFiringWhenGrounded = false;

        [Header("락온 (강화 가능)")]
        [Tooltip("락온 최대 거리(m)")]
        public float LockRange = 6000f;
        [Tooltip("기준 방향에서 이 각도(도) 안에 있어야 락온된다")]
        public float LockAngle = 15f;
        [Tooltip("표적을 이 시간(초) 동안 각도 안에 두면 락온 완료")]
        public float LockTime = 1f;
        [Tooltip("동시에 락온할 수 있는 표적 수(1 = 일반 미사일). 2 이상이면 트리거 한 번에 장전된 슬롯을 모두 일제 발사해 락온한 표적들에 차례로 나눠 쏜다(다중 유도 미사일)")]
        public int MaxLockTargets = 1;
        [Tooltip("단일 락온(MaxLockTargets = 1)일 때: 다른 표적이 지금 표적보다 기준 방향(탐색 원 가운데)에 이 각도(도) 이상 더 가까우면 그쪽으로 옮겨 잡는다. "
            + "지금 표적 각도의 25%와 이 값 중 큰 쪽 — 두 표적 사이에서 깜빡이지 않게. 0이면 한번 잡은 표적을 각도 밖으로 나갈 때까지 유지")]
        public float CenterSwitchMargin = 1.5f;
        [Tooltip("다중 락온: 켜면 락온한 표적마다 한 발씩만 쏜다(표적이 2개면 2발, 나머지 슬롯은 남는다). 끄면 장전된 슬롯을 모두 쏴 표적들에 나눠 준다(표적이 1개면 전부 그 표적에)")]
        public bool OneMissilePerTarget = true;

        [Header("장전 (강화 가능)")]
        [Tooltip("미사일 슬롯 수. 장전된 슬롯 수만큼 연달아 쏠 수 있다. 바꾼 뒤 ResetAmmo를 부른다")]
        public int SlotCount = 2;
        [Tooltip("총 탄수(장전된 탄 포함). 바꾼 뒤 ResetAmmo를 부른다")]
        public int MaxAmmo = 60;
        [Tooltip("발사한 슬롯이 다시 장전되기까지의 시간(초)")]
        public float SlotReloadTime = 4f;
        [Tooltip("슬롯별 발사 지점. 슬롯 i는 LaunchPoints[i % 길이]를 쓴다(좌우 파일런을 번갈아)")]
        public Transform[] LaunchPoints;

        [Header("미사일 성능 (강화 가능 — 발사 시 미사일에 넣는다)")]
        public float Damage = 100f;
        public float MaxSpeed = 420f;
        public float Acceleration = 300f;
        [Tooltip("초당 최대 선회 각도(도)")]
        public float TurnRate = 120f;
        [Tooltip("0 = 직접 추적, 1 = 예측 조준, 2 = 비례항법 (ACO_Missile 참고)")]
        [Range(0, 2)] public int GuidanceMode = 2;
        [Tooltip("비례항법 항법 상수 N")]
        public float NavigationConstant = 3f;
        [Tooltip("근접 신관 반경(m)")]
        public float ProximityRadius = 9f;
        [Tooltip("범위 피해 반경(m, 지름 아님). 쏠 때 탄 프리팹(ACO_Ordnance/ACO_Missile)의 값을 이 값(× 폭발 범위 강화)으로 덮어쓴다 — 반경은 여기서 정한다. "
            + "폭탄/로켓은 늘 쓰고, 유도 미사일은 MissileSplash를 켰을 때만 쓴다. 기본 Sphere로 범위를 보려면 스케일 = 반경 × 2")]
        public float SplashRadius = 25f;
        [Tooltip("범위 가장자리 피해 비율(0~1). 중심 피해는 Damage")]
        [Range(0f, 1f)] public float MinSplashFraction = 0.25f;
        [Tooltip("켜면 유도 미사일도 터진 자리에 범위 피해를 준다(대지 미사일). 대공 미사일은 끈다")]
        public bool MissileSplash = false;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다(내 미사일 빗나감/플레어에 속음 대사)")]
        public ACO_Radio Radio;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 RWR을 넣는다(다른 플레이어를 노린 미사일의 방향/거리 표시)")]
        public ACO_RWR Rwr;
        [Tooltip("락온 진행 표시를 마름모 대신 십자로 그린다(특수무장 — 특수무장 슬롯 안의 모듈이면 Bake가 켠다)")]
        public bool CrossLockIndicator = false;

        [Header("강화 계열")]
        [Tooltip("격납고 강화 중 어느 탭의 효과를 받는가. Missile = 일반 미사일, SpecialAAM = 특수 대공, SpecialAGM = 특수 대지, "
            + "SpecialBomb = 특수 폭탄/로켓, None = 강화 없음. 대공 탭은 피해/탄수/선회/락온 속도·거리·범위, 대지·폭탄 탭은 피해/탄수/폭발 반경")]
        public ACO_UpgradeFamily UpgradeFamily = ACO_UpgradeFamily.Missile;

        [Header("발사 입력")]
        [Tooltip("켜면 트리거를 누르고 있는 동안 FireInterval마다 계속 쏜다(로켓·폭탄 연속 투하). 끄면 한 번 당길 때마다 한 번")]
        public bool HoldToFire = false;
        [Tooltip("누르고 있을 때 발사 간격(초)")]
        public float FireInterval = 0.12f;

        [Header("미사일")]
        [Tooltip("ACO_Missile이 붙은 미사일 프리팹")]
        public GameObject Missile;
        [Tooltip("시작 시 미리 만들어 둘 미사일 수")]
        public int NumPreInstantiated = 4;
        [Tooltip("0 = 레이더, 1 = 열추적, 2 = 기타. 표적 기체의 대응책/접근 경고 변수를 고른다")]
        [Range(0, 2)] public int MissileType = 1;
        [Tooltip("락온 없이도 발사한다(직진). 끄면 락온해야만 쏜다")]
        public bool AllowUnguidedFire = true;
        [Tooltip("모기체 속도를 이어받아 출발한다")]
        public bool InheritVelocity = true;
        [Tooltip("발사된 미사일의 부모. 움직이는 원점 시스템을 쓸 때만 지정")]
        public Transform WorldParent;

        [Header("소리")]
        public AudioSource LaunchSound;
        [Tooltip("선택 중, 표적 없음(오브젝트를 켜고 끈다)")]
        public AudioSource IdleSound;
        [Tooltip("락온 중(오브젝트를 켜고 끈다)")]
        public AudioSource LockingSound;
        [Tooltip("락온 완료(오브젝트를 켜고 끈다)")]
        public AudioSource LockedSound;

        [Header("CCIP (폭탄 낙하 지점)")]
        [Tooltip("폭탄(추진 없는 ACO_Ordnance)을 고르면 지금 투하했을 때 떨어질 지점에 이 표시를 띄운다. 비우면 안 띄운다")]
        public Transform CcipReticle;
        [Tooltip("표시의 화면 크기(도)")]
        public float CcipAngle = 3f;
        [Tooltip("표시를 그릴 가상 거리(m). 화면 크기에는 영향이 없다(표적 마커와 같은 방식)")]
        public float CcipDisplayDistance = 30f;
        [Tooltip("낙하 지점을 다시 계산하는 주기(초)")]
        public float CcipInterval = 0.05f;
        [Tooltip("이 시간(초)까지만 낙하를 예측한다(너무 높으면 지점을 못 찾는다)")]
        public float CcipMaxTime = 40f;

        [Header("HUD")]
        [Tooltip("현재 표적을 가리키는 HUD 요소. 락온되면 뒤집어진다(SAV DFUNC_AAM과 같은 방식)")]
        public Transform TargetIndicator;
        [Tooltip("슬롯 막대 원본. 슬롯 수만큼 복제해 SlotBarSpacing 간격으로 늘어놓는다(강화로 슬롯 수가 바뀌면 다시 만든다). 원본은 꺼 둔다. "
            + "구성: 자식 'Fill' = 장전 진행도만큼 X로 늘어나는 막대(피벗이 왼쪽 끝이어야 왼쪽부터 찬다 — 빈 오브젝트 'Fill'을 왼쪽 끝에 두고 그 아래에 막대를 +X로 반 칸 옮겨 둔다), "
            + "자식 'Ready'(선택) = 장전 완료 시 켜진다. 'Fill'이 없으면 막대 자체가 늘어난다")]
        public Transform SlotBarTemplate;
        [Tooltip("막대 사이 간격(원본의 로컬 좌표 기준). 슬롯 i는 원본 위치 + 간격 × i")]
        public Vector3 SlotBarSpacing = new Vector3(0.02f, 0f, 0f);
        [Tooltip("켜면 'Fill'이 아래에서 위로(Y 스케일), 끄면 왼쪽에서 오른쪽으로(X 스케일) 찬다. 'Fill'의 피벗이 차기 시작하는 쪽 끝에 있어야 한다")]
        public bool SlotBarVertical = true;
        [Tooltip("(고정 배치용) 슬롯별 장전 표시. 장전된 슬롯만 켜진다")]
        public GameObject[] SlotReadyIndicators;
        public Text AmmoText;
        public TextMeshPro AmmoTextTMP;
        public TextMeshProUGUI AmmoTextTMPUGUI;

        [Header("애니메이터 (선택)")]
        public Animator WeaponAnimator;
        [Tooltip("남은 탄 비율(0~1) float")]
        public string AnimFloatName = "AAMs";
        [Tooltip("발사 시 trigger")]
        public string AnimFiredTriggerName = "aamlaunched";

        // SaccEntity가 주입한다(DFUNC 규약).
        [System.NonSerialized] public SaccEntity EntityControl;
        [System.NonSerialized] public bool LeftDial = false;
        [System.NonSerialized] public int DialPosition = -999;

        private const float TARGET_SCAN_INTERVAL = 0.1f;
        // 시야 판정: 장애물이 표적보다 이만큼(m) 이상 앞에 있으면 가려진 것으로 본다(표적 주변 지형 오차 허용).
        private const float LOS_TOLERANCE = 30f;
        // 가려진 상태가 이 시간(초) 넘게 이어지면 표적을 놓는다(SAV와 같은 0.25초).
        private const float OBSCURED_GRACE = 0.25f;
        private const ushort NO_TARGET = 65535;

        private bool _initialized;
        private bool _pilot;
        private bool _selected;
        private bool _triggerLastFrame;
        private VRCPlayerApi _localPlayer;
        private Transform _vehicleTransform;
        private Rigidbody _vehicleRigid;
        private float _distanceFromHead = 1.333f;

        // 장전 상태
        private bool[] _slotLoaded;
        private float[] _slotReadyAt;
        private int _reserve;
        private int _lastSlot = -1;
        private int _ammoDebt;   // 강화 해제로 빼야 했지만 탄이 모자라 못 뺀 수(ApplyUpgrades)

        // 강화 기준값(ApplyUpgrades를 처음 부를 때의 값)
        private bool _upgradeBaseSet;
        private float _baseDamage;
        private int _baseMaxAmmo;
        private float _baseTurnRate;
        private float _baseLockTime;
        private float _baseLockRange;
        private float _baseLockAngle;
        private float _baseSplashRadius;
        private float _baseReloadTime;

        // 표적. 락온 칸 MaxLockTargets개 — 칸마다 표적 번호/락온 시간/완료/가려진 시간. 비어 있으면 -1.
        // 주 표적 = 처음으로 채워진 칸(HUD 표적 표시와 단일 조회가 쓴다). 칸을 옮기지 않으므로 마커 연출이 끊기지 않는다.
        private SaccEntity[] _targetEntities;
        private int[] _lockIdx;
        private float[] _lockTimer;
        private bool[] _lockDone;
        private float[] _lockObscured;
        private float _scanTimer;
        private float _nextHoldFire;

        // 폭탄/로켓(ACO_Ordnance) 탄체의 비행 값(풀의 첫 탄에서 읽는다 — 모두 같은 프리팹). null이면 미사일.
        private ACO_Ordnance _ordTemplate;
        private float _ccipTimer;
        private bool _ccipValid;
        private Vector3 _ccipPoint;
        private bool _ccipShown;
        private Vector3 _targetDir;   // 주 표적 방향(HUD 표시용)

        // 특수무장 슬롯에서 지금 장착된 모듈인가(아니면 입력·HUD·막대를 모두 쉰다)
        private bool _equipped = true;

        private int _numChildrenStart;

        // 슬롯 막대(원본 복제본)
        private Transform[] _barFills;
        private GameObject[] _barReady;
        private GameObject[] _bars;
        private float _barFillScale = 1f;

        // ───────────── SAV DFUNC 규약 ─────────────

        public void SFEXT_L_EntityStart()
        {
            Init();
        }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;

            _localPlayer = Networking.LocalPlayer;
            if (EntityControl)
            {
                _vehicleTransform = EntityControl.transform;
                _vehicleRigid = EntityControl.VehicleRigidbody;
            }
            if (!_vehicleTransform) { _vehicleTransform = transform; }
            if (!TargetingTransform) { TargetingTransform = _vehicleTransform; }
            if (HUDControl) { _distanceFromHead = (float)HUDControl.GetProgramVariable("distance_from_head"); }

            if (Targets == null) { Targets = new GameObject[0]; }
            EnsureLockSlots();
            _targetEntities = new SaccEntity[Targets.Length];
            for (int i = 0; i < Targets.Length; i++)
            {
                if (Targets[i]) { _targetEntities[i] = Targets[i].GetComponentInParent<SaccEntity>(); }
            }

            _numChildrenStart = transform.childCount;
            if (Missile)
            {
                for (int i = 0; i < NumPreInstantiated; i++) { InstantiateMissile(); }
                GameObject sample = transform.childCount > _numChildrenStart ? transform.GetChild(_numChildrenStart).gameObject : InstantiateMissile();
                _ordTemplate = sample.GetComponent<ACO_Ordnance>();
            }
            if (CcipReticle) { CcipReticle.gameObject.SetActive(false); }

            ResetAmmo();
        }

        public void SFEXT_O_PilotEnter()
        {
            _pilot = true;
            UpdateAmmoVisuals();
        }

        public void SFEXT_O_PilotExit()
        {
            _pilot = false;
            Deselect();
        }

        public void SFEXT_G_PilotEnter() { gameObject.SetActive(true); }
        public void SFEXT_G_PilotExit() { gameObject.SetActive(false); }

        public void DFUNC_Selected()
        {
            _selected = true;
            // 선택하는 순간 쥐고 있던 트리거로 바로 발사하지 않는다.
            _triggerLastFrame = true;
            if (TargetIndicator) { TargetIndicator.gameObject.SetActive(true); }
        }

        public void DFUNC_Deselected() { Deselect(); }

        private void Deselect()
        {
            _selected = false;
            ClearLock();
            SetSounds(false, false, false);
            _ccipValid = false;
            ShowCcip(false);
            if (TargetIndicator)
            {
                TargetIndicator.localRotation = Quaternion.identity;
                TargetIndicator.localScale = Vector3.one;
                TargetIndicator.gameObject.SetActive(false);
            }
        }

        /// 특수무장 슬롯이 이 모듈을 장착하거나 내린다. 내리면 락온·소리·HUD 막대를 끈다(잔탄 텍스트는 장착된 모듈이 쓴다).
        public void SetEquipped(bool on)
        {
            Init();
            _equipped = on;
            if (!on) { Deselect(); }
            if (_bars != null)
            {
                for (int i = 0; i < _bars.Length; i++) { if (_bars[i] && _bars[i].activeSelf != on) { _bars[i].SetActive(on); } }
            }
            if (on) { UpdateAmmoVisuals(); }
        }

        // 특수무장 슬롯(ACO_DFUNC_Special)이 SendCustomEvent로 부른다.
        public void _ACO_Equip() { SetEquipped(true); }
        public void _ACO_Unequip() { SetEquipped(false); }

        /// 데스크톱 키로 이 기능을 선택한다(SaccEntity가 부른다).
        public void KeyboardInput()
        {
            if (EntityControl) { EntityControl.ToggleStickSelection(this); }
        }

        public void SFEXT_G_Explode()
        {
            ResetAmmo();
            if (_selected) { Deselect(); }
        }

        public void SFEXT_G_RespawnButton() { ResetAmmo(); }

        /// 보급 지점: 한 번에 총 탄수의 10%(최소 1발)씩 채운다. 빈 슬롯부터 즉시 장전된다.
        public void SFEXT_G_ReSupply()
        {
            Init();
            int total = GetAmmo();
            if (total >= MaxAmmo) { return; }
            if (EntityControl) { EntityControl.SetProgramVariable("ReSupplied", (int)EntityControl.GetProgramVariable("ReSupplied") + 1); }

            int add = Mathf.Min(Mathf.Max(Mathf.CeilToInt(MaxAmmo * 0.1f), 1), MaxAmmo - total);
            for (int i = 0; i < _slotLoaded.Length && add > 0; i++)
            {
                if (_slotLoaded[i]) { continue; }
                _slotLoaded[i] = true;
                _slotReadyAt[i] = 0f;
                add--;
            }
            _reserve += add;
            UpdateAmmoVisuals();
        }

        public void SFEXT_G_ReArm() { SFEXT_G_ReSupply(); }

        // ───────────── 강화 (ACO_VehicleUpgrades) ─────────────

        /// 플레이어 강화를 적용한다. 배수는 처음 부를 때의 값(인스펙터 값)이 기준이고, 모두 1을 넣으면 원래대로 돌아간다.
        /// 탑승할 때 내 강화로, 내릴 때 1로 부른다(기체는 여럿이 같이 쓰므로). 쏜 사람 값만 쓰이므로 조종사 화면에서만 부른다.
        /// 잔탄: 늘어난(줄어든) 총 탄수만큼만 더하고 뺀다 — 다시 가득 채우지 않는다. 뺄 탄이 모자라면 빚으로 남겨 다음에 더할 때 먼저 갚는다
        /// (다 쏘고 내렸다 다시 타서 공짜로 채우는 일이 없게).
        /// lockSpeed는 락온 시간을 그만큼 줄인다(2 = 절반), blast는 폭탄/로켓 범위 반경.
        public void ApplyUpgrades(float damage, float ammo, float turnRate, float lockSpeed, float lockRange, float lockAngle, float blast, float reload)
        {
            Init();
            if (!_upgradeBaseSet)
            {
                _upgradeBaseSet = true;
                _baseDamage = Damage;
                _baseMaxAmmo = MaxAmmo;
                _baseTurnRate = TurnRate;
                _baseLockTime = LockTime;
                _baseLockRange = LockRange;
                _baseLockAngle = LockAngle;
                _baseSplashRadius = SplashRadius;
                _baseReloadTime = SlotReloadTime;
            }
            Damage = _baseDamage * damage;
            TurnRate = _baseTurnRate * turnRate;
            LockTime = _baseLockTime / Mathf.Max(lockSpeed, 0.01f);
            LockRange = _baseLockRange * lockRange;
            LockAngle = _baseLockAngle * lockAngle;
            SplashRadius = _baseSplashRadius * blast;
            SlotReloadTime = _baseReloadTime / Mathf.Max(reload, 0.01f);   // 이미 장전 중인 슬롯은 다음 장전부터

            int newMax = Mathf.Max(Mathf.RoundToInt(_baseMaxAmmo * ammo), 0);
            int delta = newMax - MaxAmmo;
            MaxAmmo = newMax;
            if (delta > 0)
            {
                int pay = Mathf.Min(delta, _ammoDebt);
                _ammoDebt -= pay;
                _reserve += delta - pay;
                // 빈 슬롯이 있으면 바로 장전을 시작한다.
                for (int i = 0; i < _slotLoaded.Length && _reserve > 0; i++)
                {
                    if (_slotLoaded[i]) { continue; }
                    _reserve--;
                    _slotLoaded[i] = true;
                    _slotReadyAt[i] = Time.time + SlotReloadTime;
                }
            }
            else if (delta < 0)
            {
                int remove = -delta;
                int fromReserve = Mathf.Min(remove, _reserve);
                _reserve -= fromReserve;
                remove -= fromReserve;
                for (int i = _slotLoaded.Length - 1; i >= 0 && remove > 0; i--)
                {
                    if (!_slotLoaded[i]) { continue; }
                    _slotLoaded[i] = false;
                    remove--;
                }
                _ammoDebt += remove;
            }
            UpdateAmmoVisuals();
        }

        // ───────────── 장전 ─────────────

        /// 탄을 가득 채운다. SlotCount/MaxAmmo를 바꾼 뒤에도 부른다(강화 적용).
        public void ResetAmmo()
        {
            int slots = Mathf.Max(SlotCount, 1);
            _slotLoaded = new bool[slots];
            _slotReadyAt = new float[slots];
            int loaded = Mathf.Min(slots, Mathf.Max(MaxAmmo, 0));
            for (int i = 0; i < slots; i++) { _slotLoaded[i] = i < loaded; }
            _reserve = Mathf.Max(MaxAmmo, 0) - loaded;
            _ammoDebt = 0;
            _lastSlot = -1;
            BuildSlotBars(slots);
            UpdateAmmoVisuals();
        }

        /// 슬롯 수만큼 막대를 만든다. 이미 있으면 지우고 다시 만든다(슬롯 수 강화).
        private void BuildSlotBars(int slots)
        {
            if (!SlotBarTemplate) { return; }
            if (_bars != null)
            {
                for (int i = 0; i < _bars.Length; i++) { if (_bars[i]) { Object.Destroy(_bars[i]); } }
            }

            Transform templateFill = SlotBarTemplate.Find("Fill");
            Transform templateReady = SlotBarTemplate.Find("Ready");
            // SAV HUDText 셰이더는 한 번 그린 픽셀의 스텐실을 지워(StencilOp Zero) 겹친 자리엔 먼저 그린 것만 남는다.
            // 그래서 밝은 것부터 그린다: Ready → Fill → 나머지(배경). 배경이 먼저면 Fill/Ready가 전혀 안 보인다.
            if (templateFill) { templateFill.SetAsFirstSibling(); }
            if (templateReady) { templateReady.SetAsFirstSibling(); }
            if (!templateFill) { templateFill = SlotBarTemplate; }
            _barFillScale = SlotBarVertical ? templateFill.localScale.y : templateFill.localScale.x;
            SlotBarTemplate.gameObject.SetActive(false);

            _bars = new GameObject[slots];
            _barFills = new Transform[slots];
            _barReady = new GameObject[slots];
            for (int i = 0; i < slots; i++)
            {
                GameObject bar = Object.Instantiate(SlotBarTemplate.gameObject);
                Transform t = bar.transform;
                t.SetParent(SlotBarTemplate.parent, false);
                t.localPosition = SlotBarTemplate.localPosition + SlotBarSpacing * i;
                t.localRotation = SlotBarTemplate.localRotation;
                t.localScale = SlotBarTemplate.localScale;
                bar.name = "SlotBar_" + i;
                bar.SetActive(true);

                Transform fill = t.Find("Fill");
                _barFills[i] = fill ? fill : t;
                Transform ready = t.Find("Ready");
                _barReady[i] = ready ? ready.gameObject : null;
                _bars[i] = bar;
                if (!_equipped) { bar.SetActive(false); }
            }
            Debug.Log("[ACO] " + gameObject.name + ": HUD 슬롯 막대 " + slots + "개를 만들었습니다(" + SlotBarTemplate.parent.name + ")");
        }

        /// 남은 탄(예비탄 + 장전 중이거나 장전된 탄).
        public int GetAmmo()
        {
            if (_slotLoaded == null) { return 0; }
            int n = _reserve;
            for (int i = 0; i < _slotLoaded.Length; i++) { if (_slotLoaded[i]) { n++; } }
            return n;
        }

        /// 지난번에 쏜 슬롯 다음부터 찾는다 — 좌우 파일런을 번갈아 쏜다.
        private int FindReadySlot()
        {
            if (_slotLoaded == null) { return -1; }
            int n = _slotLoaded.Length;
            float now = Time.time;
            for (int k = 1; k <= n; k++)
            {
                int i = (_lastSlot + k + n) % n;
                if (_slotLoaded[i] && now >= _slotReadyAt[i]) { return i; }
            }
            return -1;
        }

        /// 발사한 슬롯을 비우고, 예비탄이 있으면 장전을 시작한다(모든 클라이언트에서 같은 순서로 실행).
        private void ConsumeSlot(int slot)
        {
            if (_slotLoaded == null || slot < 0 || slot >= _slotLoaded.Length) { return; }
            _lastSlot = slot;
            if (_reserve > 0)
            {
                _reserve--;
                _slotLoaded[slot] = true;
                _slotReadyAt[slot] = Time.time + SlotReloadTime;
            }
            else
            {
                _slotLoaded[slot] = false;
            }
        }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            if (!_initialized || !_equipped) { return; }
            UpdateSlotIndicators();
            if (!_pilot || !_selected) { return; }

            bool grounded = !AllowFiringWhenGrounded && SAVControl && (bool)SAVControl.GetProgramVariable("Taxiing");

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = TARGET_SCAN_INTERVAL;
                if (grounded) { ClearLock(); }
                else { ScanTargets(); }
            }
            UpdateLock(Time.deltaTime, grounded);

            float trigger = Input.GetAxisRaw(LeftDial ? "Oculus_CrossPlatform_PrimaryIndexTrigger" : "Oculus_CrossPlatform_SecondaryIndexTrigger");
            bool pressed = trigger > 0.75f || Input.GetKey(FireKey);
            bool fire = pressed && !grounded && (!_triggerLastFrame || (HoldToFire && Time.time >= _nextHoldFire));
            if (fire)
            {
                TryFire();
                _nextHoldFire = Time.time + FireInterval;
            }
            _triggerLastFrame = pressed;

            bool hasAmmo = GetAmmo() > 0;
            bool hasTarget = PrimarySlot() >= 0;
            bool anyLocked = CountLocked() > 0;
            SetSounds(hasAmmo && !hasTarget, hasAmmo && hasTarget && !anyLocked, anyLocked);
            UpdateHud();

            if (CcipReticle && _ordTemplate && _ordTemplate.Acceleration <= 0f)
            {
                _ccipTimer -= Time.deltaTime;
                if (_ccipTimer <= 0f)
                {
                    _ccipTimer = CcipInterval;
                    _ccipValid = !grounded && ComputeCcip();
                }
            }
        }

        /// 락온 칸 배열을 MaxLockTargets에 맞춘다(강화로 칸 수가 바뀌면 다시 만든다).
        private void EnsureLockSlots()
        {
            int n = Mathf.Max(MaxLockTargets, 1);
            if (_lockIdx != null && _lockIdx.Length == n) { return; }
            _lockIdx = new int[n];
            _lockTimer = new float[n];
            _lockDone = new bool[n];
            _lockObscured = new float[n];
            for (int k = 0; k < n; k++) { _lockIdx[k] = -1; }
        }

        /// 칸마다: 잡고 있는 표적이 아직 각도 안이면 유지하고(락온이 흔들리지 않게), 빈 칸은 아직 안 잡은 표적 중
        /// 기준 방향에 가장 가까운 것으로 채운다. 칸이 하나(일반 미사일)면 가운데에 더 가까운 표적이 생길 때 그쪽으로 옮긴다(CenterSwitchMargin).
        private void ScanTargets()
        {
            EnsureLockSlots();
            Vector3 origin = GetEyePosition();
            Vector3 fwd = TargetingTransform.forward;
            float rangeSq = LockRange * LockRange;

            for (int k = 0; k < _lockIdx.Length; k++)
            {
                int i = _lockIdx[k];
                if (i < 0) { continue; }
                if (!IsCandidate(i, origin, rangeSq) || Vector3.Angle(fwd, Targets[i].transform.position - origin) > LockAngle) { ClearSlot(k); }
            }

            // 단일 락온: 탐색 원 가운데에 확실히 더 가까운 표적이 있으면 그쪽으로 옮긴다(락온 시간은 새로 잰다)
            if (_lockIdx.Length == 1 && _lockIdx[0] >= 0 && CenterSwitchMargin > 0f)
            {
                int cur = _lockIdx[0];
                float curAngle = Vector3.Angle(fwd, Targets[cur].transform.position - origin);
                float margin = Mathf.Max(CenterSwitchMargin, curAngle * 0.25f);
                int better = -1;
                float betterAngle = curAngle - margin;
                for (int i = 0; i < Targets.Length; i++)
                {
                    if (i == cur || !IsCandidate(i, origin, rangeSq)) { continue; }
                    float angle = Vector3.Angle(fwd, Targets[i].transform.position - origin);
                    if (angle >= betterAngle) { continue; }
                    betterAngle = angle;
                    better = i;
                }
                if (better >= 0)
                {
                    _lockIdx[0] = better;
                    _lockTimer[0] = 0f;
                    _lockDone[0] = false;
                    _lockObscured[0] = 0f;
                }
            }

            for (int k = 0; k < _lockIdx.Length; k++)
            {
                if (_lockIdx[k] >= 0) { continue; }
                int best = -1;
                float bestAngle = LockAngle;
                for (int i = 0; i < Targets.Length; i++)
                {
                    if (IsHeld(i) || !IsCandidate(i, origin, rangeSq)) { continue; }
                    float angle = Vector3.Angle(fwd, Targets[i].transform.position - origin);
                    if (angle > bestAngle) { continue; }
                    bestAngle = angle;
                    best = i;
                }
                if (best < 0) { break; }
                _lockIdx[k] = best;
                _lockTimer[k] = 0f;
                _lockDone[k] = false;
                _lockObscured[k] = 0f;
            }
        }

        private bool IsHeld(int i)
        {
            for (int k = 0; k < _lockIdx.Length; k++) { if (_lockIdx[k] == i) { return true; } }
            return false;
        }

        private bool IsCandidate(int i, Vector3 origin, float rangeSq)
        {
            GameObject g = Targets[i];
            if (!g || !g.activeInHierarchy) { return false; }
            SaccEntity e = _targetEntities[i];
            if (e && (e.dead || e == EntityControl)) { return false; }
            return (g.transform.position - origin).sqrMagnitude <= rangeSq;
        }

        /// 칸마다 락온 시간을 잰다. 지형에 가리면 잠깐(OBSCURED_GRACE)은 봐주고, 그보다 길면 그 칸을 비운다.
        private void UpdateLock(float dt, bool grounded)
        {
            EnsureLockSlots();
            if (grounded) { ClearLock(); return; }

            Vector3 origin = GetEyePosition();
            Vector3 fwd = TargetingTransform.forward;
            float rangeSq = LockRange * LockRange;
            int primary = PrimarySlot();
            for (int k = 0; k < _lockIdx.Length; k++)
            {
                int i = _lockIdx[k];
                if (i < 0) { continue; }
                if (!IsCandidate(i, origin, rangeSq)) { ClearSlot(k); continue; }

                Vector3 dir = Targets[i].transform.position - origin;
                float dist = dir.magnitude;
                RaycastHit hit;
                bool blocked = Physics.Raycast(origin, dir, out hit, dist, ObstacleLayers, QueryTriggerInteraction.Ignore)
                    && hit.distance < dist - LOS_TOLERANCE;
                _lockObscured[k] = blocked ? _lockObscured[k] + dt : 0f;
                if (_lockObscured[k] > OBSCURED_GRACE) { ClearSlot(k); continue; }
                if (k == primary) { _targetDir = dir; }

                if (Vector3.Angle(fwd, dir) <= LockAngle && !blocked)
                {
                    _lockTimer[k] += dt;
                    if (_lockTimer[k] >= LockTime) { _lockDone[k] = true; }
                }
                else
                {
                    _lockTimer[k] = 0f;
                    _lockDone[k] = false;
                }
            }
        }

        private void ClearSlot(int k)
        {
            _lockIdx[k] = -1;
            _lockTimer[k] = 0f;
            _lockDone[k] = false;
            _lockObscured[k] = 0f;
        }

        private void ClearLock()
        {
            EnsureLockSlots();
            for (int k = 0; k < _lockIdx.Length; k++) { ClearSlot(k); }
        }

        /// 주 표적 칸 = 처음으로 채워진 칸. 없으면 -1.
        private int PrimarySlot()
        {
            if (_lockIdx == null) { return -1; }
            for (int k = 0; k < _lockIdx.Length; k++) { if (_lockIdx[k] >= 0) { return k; } }
            return -1;
        }

        private int CountLocked()
        {
            if (_lockIdx == null) { return 0; }
            int n = 0;
            for (int k = 0; k < _lockIdx.Length; k++) { if (_lockIdx[k] >= 0 && _lockDone[k]) { n++; } }
            return n;
        }

        /// 락온 완료된 n번째 표적 번호.
        private int LockedTargetAt(int n)
        {
            for (int k = 0; k < _lockIdx.Length; k++)
            {
                if (_lockIdx[k] < 0 || !_lockDone[k]) { continue; }
                if (n == 0) { return _lockIdx[k]; }
                n--;
            }
            return -1;
        }

        // ───────────── 발사 ─────────────

        private void TryFire()
        {
            EnsureLockSlots();
            int lockedCount = CountLocked();
            if (lockedCount == 0 && !AllowUnguidedFire) { return; }

            if (MaxLockTargets <= 1)
            {
                int slot = FindReadySlot();
                if (slot < 0) { return; }
                int primary = PrimarySlot();
                bool locked = primary >= 0 && _lockDone[primary];
                SendLaunch(locked ? (ushort)_lockIdx[primary] : NO_TARGET, slot);
            }
            else
            {
                // 일제 발사: 표적마다 한 발(OneMissilePerTarget) 또는 장전된 슬롯 전부를 락온한 표적들에 차례로 나눠 준다.
                // 락온이 없으면(무유도 허용 시) 한 발 또는 전부를 직진으로.
                int limit = OneMissilePerTarget ? Mathf.Max(lockedCount, 1) : _slotLoaded.Length;
                int fired = 0;
                float now = Time.time;
                for (int slot = 0; slot < _slotLoaded.Length && fired < limit; slot++)
                {
                    if (!_slotLoaded[slot] || now < _slotReadyAt[slot]) { continue; }
                    int target = lockedCount > 0 ? LockedTargetAt(fired % lockedCount) : -1;
                    SendLaunch(target >= 0 ? (ushort)target : NO_TARGET, slot);
                    fired++;
                }
                if (fired == 0) { return; }
            }
            if (EntityControl) { EntityControl.SendEventToExtensions("SFEXT_O_AAMLaunch"); }
        }

        /// 비행 성능은 이벤트에 실어 보낸다 — 강화 값이 다른 클라이언트에 없어도 같은 궤적으로 보인다.
        private void SendLaunch(ushort target, int slot)
        {
            if (_ordTemplate)
            {
                // 폭탄/로켓: SAV처럼 슬롯과 비행 성능만 보낸다. 받은 쪽은 자기 화면의 기체 발사 지점과 동기화 속도(CurrentVel)에서
                // 쏜다 — 이벤트가 오는 동안 기체가 앞으로 간 만큼 궤적이 조금 어긋나지만(연출만), 피해는 쏜 사람 화면 기준이라 판정엔 영향이 없다.
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetLaunchOrdnance), (byte)slot, MaxSpeed, Acceleration);
                return;
            }
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetLaunch),
                target, (byte)slot, MaxSpeed, Acceleration, TurnRate, (byte)GuidanceMode, NavigationConstant, ProximityRadius);
        }

        // 로켓 연사는 초당 10발 이상이므로 한도를 넉넉히 둔다(최대 100).
        [NetworkCallable(60)]
        public void NetLaunchOrdnance(byte slot, float maxSpeed, float acceleration)
        {
            Init();
            ConsumeSlot(slot);
            UpdateAmmoVisuals();

            if (LaunchSound && LaunchSound.clip) { LaunchSound.PlayOneShot(LaunchSound.clip); }
            if (WeaponAnimator && AnimFiredTriggerName != string.Empty) { WeaponAnimator.SetTrigger(AnimFiredTriggerName); }
            if (!Missile) { return; }

            Transform launchPoint = GetLaunchPoint(slot);
            GameObject m = TakeFromPool();
            if (WorldParent) { m.transform.SetParent(WorldParent); }
            else { m.transform.SetParent(null); }
            m.transform.SetPositionAndRotation(launchPoint.position, launchPoint.rotation);

            ACO_Ordnance ordnance = m.GetComponent<ACO_Ordnance>();
            if (!ordnance) { m.SetActive(false); return; }
            bool isShooter = NetworkCalling.CallingPlayer != null && NetworkCalling.CallingPlayer.isLocal;
            ordnance.EntityControl = EntityControl;
            ordnance.LaunchVelocity = InheritVelocity ? GetVehicleVelocity() : Vector3.zero;
            ordnance.PoolParent = transform;
            ordnance.IsShooter = isShooter;
            ordnance.MaxSpeed = maxSpeed;
            ordnance.Acceleration = acceleration;
            // 피해는 쏜 사람의 값만 쓰인다.
            if (isShooter)
            {
                ordnance.Damage = Damage;
                ordnance.SplashRadius = SplashRadius;
                ordnance.MinSplashFraction = MinSplashFraction;
            }

            m.SetActive(true);
            ordnance.EnableWeapon();
        }

        /// 지금 투하하면 떨어질 지점. 폭탄과 같은 식(중력 → 공기 저항 → 이동)으로 앞으로 굴려 지형/물에 닿는 곳을 찾는다.
        /// 유닛 히트박스(17)는 빼고 지형만 본다 — 내 기체에 걸리지 않게, 그리고 지면에 떨어지는 지점이 기준이다.
        private bool ComputeCcip()
        {
            int slot = FindReadySlot();
            Transform lp = GetLaunchPoint(slot >= 0 ? slot : 0);
            Vector3 pos = lp.position;
            Vector3 vel = (InheritVelocity ? GetVehicleVelocity() : Vector3.zero) + lp.forward * _ordTemplate.LaunchSpeed;
            int mask = _ordTemplate.HitLayers.value & ~(1 << 17);
            // 폭탄(ACO_Ordnance)과 같은 고정 간격 h의 궤적: v(n+1) = a·(v(n) + G), x(n+1) = x(n) + h·v(n+1)  (G = 중력·h, a = 1 − 공기저항·h).
            // n번째 위치를 수식으로 바로 구해 표본점 사이를 선으로 검사한다 — 실제 폭탄과 같은 궤적이고 반복 수는 적다.
            float h = ACO_Ordnance.FLIGHT_STEP;
            Vector3 gStep = Physics.gravity * (_ordTemplate.GravityScale * h);
            float a = _ordTemplate.Drag > 0f ? Mathf.Max(1f - _ordTemplate.Drag * h, 0f) : 1f;
            Vector3 prev = pos;
            int n = 0;
            int maxSteps = Mathf.CeilToInt(CcipMaxTime / h);
            while (n < maxSteps)
            {
                // 처음엔 잘게(가까운 지면), 나중엔 크게 — 높은 고도에서도 선 검사 수를 줄인다.
                n += n * h < 3f ? 5 : 12;
                Vector3 next = TrajectoryPoint(pos, vel, gStep, a, h, n);
                RaycastHit hit;
                if (Physics.Linecast(prev, next, out hit, mask, QueryTriggerInteraction.Ignore))
                {
                    _ccipPoint = hit.point;
                    return true;
                }
                prev = next;
            }
            return false;
        }

        /// 고정 간격 궤적의 n번째 위치(위 점화식의 닫힌 꼴).
        private Vector3 TrajectoryPoint(Vector3 x0, Vector3 v0, Vector3 gStep, float a, float h, int n)
        {
            if (a >= 0.999999f) { return x0 + h * (n * v0 + gStep * (n * (n + 1) * 0.5f)); }
            Vector3 vInf = gStep * (a / (1f - a));
            float an = Mathf.Pow(a, n);
            return x0 + h * (n * vInf + (v0 - vInf) * (a * (1f - an) / (1f - a)));
        }

        /// 표적 마커와 같은 방식: 머리에서 낙하 지점 방향으로 CcipDisplayDistance에 고정 화면 크기로 그린다(머리가 확정된 뒤).
        public override void PostLateUpdate()
        {
            if (!CcipReticle) { return; }
            bool show = _ccipValid && _equipped && _pilot && _selected && Utilities.IsValid(_localPlayer);
            ShowCcip(show);
            if (!show) { return; }

            VRCPlayerApi.TrackingData head = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 d = _ccipPoint - head.position;
            if (d.sqrMagnitude < 1f) { return; }
            Vector3 dir = d.normalized;
            float size = 2f * CcipDisplayDistance * Mathf.Tan(CcipAngle * 0.5f * Mathf.Deg2Rad);
            CcipReticle.SetPositionAndRotation(head.position + dir * CcipDisplayDistance, Quaternion.LookRotation(dir, head.rotation * Vector3.up));
            CcipReticle.localScale = new Vector3(size, size, size);
        }

        private void ShowCcip(bool on)
        {
            if (!CcipReticle || _ccipShown == on) { return; }
            _ccipShown = on;
            CcipReticle.gameObject.SetActive(on);
        }

        // 일제 발사는 한 프레임에 슬롯 수만큼 보내므로 기본 한도(초당 5회)보다 넉넉히 둔다.
        [NetworkCallable(20)]
        public void NetLaunch(ushort target, byte slot, float maxSpeed, float acceleration, float turnRate,
            byte guidance, float navConstant, float proximity)
        {
            Init();
            ConsumeSlot(slot);
            UpdateAmmoVisuals();

            if (LaunchSound && LaunchSound.clip) { LaunchSound.PlayOneShot(LaunchSound.clip); }
            if (WeaponAnimator && AnimFiredTriggerName != string.Empty) { WeaponAnimator.SetTrigger(AnimFiredTriggerName); }
            if (!Missile) { return; }

            Transform launchPoint = GetLaunchPoint(slot);
            GameObject m = TakeFromPool();
            if (WorldParent) { m.transform.SetParent(WorldParent); }
            else { m.transform.SetParent(null); }
            m.transform.SetPositionAndRotation(launchPoint.position, launchPoint.rotation);

            ACO_Missile missile = m.GetComponent<ACO_Missile>();
            if (missile)
            {
                bool isShooter = NetworkCalling.CallingPlayer != null && NetworkCalling.CallingPlayer.isLocal;
                missile.EntityControl = EntityControl;
                missile.LaunchTarget = target != NO_TARGET && target < Targets.Length ? Targets[target] : null;
                missile.LaunchMissileType = MissileType;
                missile.Radio = Radio;
                missile.Rwr = Rwr;
                missile.LaunchInheritSpeed = InheritVelocity ? GetVehicleSpeed() : 0f;
                missile.PoolParent = transform;
                missile.ShooterJudgesHit = true;
                missile.IsShooter = isShooter;

                missile.MaxSpeed = maxSpeed;
                missile.Acceleration = acceleration;
                missile.TurnRate = turnRate;
                missile.GuidanceMode = guidance;
                missile.NavigationConstant = navConstant;
                missile.ProximityRadius = proximity;
                // 피해는 명중을 판정하는 쏜 사람의 값만 쓰인다(범위 피해도 쏜 사람만 준다).
                if (isShooter) { missile.Damage = Damage; }
                missile.SplashRadius = isShooter && MissileSplash ? SplashRadius : 0f;
                missile.MinSplashFraction = MinSplashFraction;
            }

            m.SetActive(true);
            if (missile) { missile.EnableWeapon(); }
        }

        private Transform GetLaunchPoint(int slot)
        {
            if (LaunchPoints == null || LaunchPoints.Length == 0) { return transform; }
            Transform p = LaunchPoints[slot % LaunchPoints.Length];
            return p ? p : transform;
        }

        private Vector3 GetVehicleVelocity()
        {
            if (SAVControl) { return (Vector3)SAVControl.GetProgramVariable("CurrentVel"); }
            if (_vehicleRigid) { return _vehicleRigid.velocity; }
            return Vector3.zero;
        }

        private float GetVehicleSpeed()
        {
            if (SAVControl) { return ((Vector3)SAVControl.GetProgramVariable("CurrentVel")).magnitude; }
            if (_vehicleRigid) { return _vehicleRigid.velocity.magnitude; }
            return 0f;
        }

        private GameObject InstantiateMissile()
        {
            GameObject m = Object.Instantiate(Missile);
            m.transform.SetParent(transform);
            m.SetActive(false);
            return m;
        }

        private GameObject TakeFromPool()
        {
            if (transform.childCount - _numChildrenStart > 0) { return transform.GetChild(_numChildrenStart).gameObject; }
            return InstantiateMissile();
        }

        // ───────────── 표시 ─────────────

        private Vector3 GetEyePosition()
        {
            if (HUDControl) { return HUDControl.transform.position; }
            if (Utilities.IsValid(_localPlayer)) { return _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position; }
            return TargetingTransform.position;
        }

        private void SetSounds(bool idle, bool locking, bool locked)
        {
            if (IdleSound && IdleSound.gameObject.activeSelf != idle) { IdleSound.gameObject.SetActive(idle); }
            if (LockingSound && LockingSound.gameObject.activeSelf != locking) { LockingSound.gameObject.SetActive(locking); }
            if (LockedSound && LockedSound.gameObject.activeSelf != locked) { LockedSound.gameObject.SetActive(locked); }
        }

        /// SAV DFUNC_AAM과 같은 방식: 표적 방향의 HUD 거리 위치에 두고, 락온되면 뒤집는다.
        private void UpdateHud()
        {
            if (!TargetIndicator) { return; }
            int primary = PrimarySlot();
            if (primary < 0) { TargetIndicator.localScale = Vector3.zero; return; }
            bool locked = _lockDone[primary];

            Vector3 eye = GetEyePosition();
            TargetIndicator.localScale = Vector3.one;
            TargetIndicator.position = eye + _targetDir;
            TargetIndicator.localPosition = TargetIndicator.localPosition.normalized * _distanceFromHead;
            Vector3 look = TargetIndicator.position - eye;
            TargetIndicator.rotation = Quaternion.LookRotation(locked ? -look : look, _vehicleTransform.up);
        }

        private void UpdateSlotIndicators()
        {
            if (_slotLoaded == null || !_equipped) { return; }
            UpdateSlotBars();
            if (SlotReadyIndicators == null) { return; }
            float now = Time.time;
            for (int i = 0; i < SlotReadyIndicators.Length; i++)
            {
                GameObject g = SlotReadyIndicators[i];
                if (!g) { continue; }
                bool ready = i < _slotLoaded.Length && _slotLoaded[i] && now >= _slotReadyAt[i];
                if (g.activeSelf != ready) { g.SetActive(ready); }
            }
        }

        /// 막대 길이 = 장전 진행도(빈 슬롯은 0, 장전 완료는 가득). 장전 완료면 'Ready'를 켠다.
        private void UpdateSlotBars()
        {
            if (_barFills == null) { return; }
            for (int i = 0; i < _barFills.Length; i++)
            {
                Transform fill = _barFills[i];
                if (!fill) { continue; }
                float progress = GetSlotProgress(i);
                Vector3 scale = fill.localScale;
                if (SlotBarVertical) { scale.y = _barFillScale * progress; }
                else { scale.x = _barFillScale * progress; }
                fill.localScale = scale;

                GameObject ready = _barReady[i];
                bool isReady = progress >= 1f;
                if (ready && ready.activeSelf != isReady) { ready.SetActive(isReady); }
            }
        }

        private void UpdateAmmoVisuals()
        {
            // 특수무장 슬롯의 모듈들은 잔탄 텍스트를 함께 쓴다 — 장착된 모듈만 쓴다.
            if (!_equipped) { return; }
            int ammo = GetAmmo();
            string text = ammo.ToString();
            if (AmmoText) { AmmoText.text = text; }
            if (AmmoTextTMP) { AmmoTextTMP.text = text; }
            if (AmmoTextTMPUGUI) { AmmoTextTMPUGUI.text = text; }
            if (WeaponAnimator && AnimFloatName != string.Empty)
            {
                WeaponAnimator.SetFloat(AnimFloatName, MaxAmmo > 0 ? (float)ammo / MaxAmmo : 0f);
            }
        }

        // ───────────── 외부 조회 (HUD/강화 시스템용) ─────────────

        /// 이 클라이언트가 조종 중이고 이 무기를 고른 상태인가(표적 마커가 락온 표시를 그릴지).
        public bool IsHudActive() { return _initialized && _equipped && _pilot && _selected && gameObject.activeInHierarchy; }

        // 주 표적(처음으로 채워진 칸)
        public bool IsLocked() { return IsLockedAt(PrimarySlot()); }
        public float GetLockProgress() { return GetLockProgressAt(PrimarySlot()); }

        // 칸별(다중 락온 표시용). 칸 수 = GetLockSlotCount(), 빈 칸은 null / 0 / false.
        public int GetLockSlotCount() { return _lockIdx != null ? _lockIdx.Length : 0; }
        public GameObject GetLockObject(int k)
        {
            if (_lockIdx == null || k < 0 || k >= _lockIdx.Length) { return null; }
            int i = _lockIdx[k];
            if (i < 0 || Targets == null || i >= Targets.Length) { return null; }
            return Targets[i];
        }
        public bool IsLockedAt(int k) { return _lockIdx != null && k >= 0 && k < _lockIdx.Length && _lockIdx[k] >= 0 && _lockDone[k]; }
        public float GetLockProgressAt(int k)
        {
            if (_lockIdx == null || k < 0 || k >= _lockIdx.Length || _lockIdx[k] < 0) { return 0f; }
            if (_lockDone[k]) { return 1f; }
            return LockTime > 0f ? Mathf.Clamp01(_lockTimer[k] / LockTime) : 0f;
        }
        /// 슬롯 i의 장전 진행도(0~1). 비어 있으면 0.
        public float GetSlotProgress(int i)
        {
            if (_slotLoaded == null || i < 0 || i >= _slotLoaded.Length || !_slotLoaded[i]) { return 0f; }
            if (SlotReloadTime <= 0f) { return 1f; }
            return Mathf.Clamp01(1f - (_slotReadyAt[i] - Time.time) / SlotReloadTime);
        }
    }
}
