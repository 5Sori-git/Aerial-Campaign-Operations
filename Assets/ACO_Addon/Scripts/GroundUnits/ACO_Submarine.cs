using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 잠수함 보스 LEVIATHAN(설계 6-202) — 자세 · 노출 · 연출. 체력 · 부위 · 스폰은 같은 오브젝트의 ACO_Warship(ExternalPose)이 맡고,
    /// 단계 · 시각 · 자리는 페이즈 모듈(ACO_PhaseSubmarine)이 동기화한다. 이 스크립트는 그 값으로 모든 화면에서 같은 자세를 계산한다(위치 동기화 없음).
    ///  - 1페이즈(SLCM): 잠수함은 깊이 숨어 있다. 풀의 SLCM(ACO_Slcm)에 모듈의 일제 발사 계획을 넣는다. 순양함 CIWS(InterceptSites)가 돌입한 미사일을 떨어뜨린다.
    ///  - 2페이즈 A(간헐 부상): 잠항 → 부상 → 수상 → 잠항을 되풀이한다. 갑판이 물 위일 때만 히트박스 · 락온 마커가 켜지고 무장 부위가 살아난다.
    ///  - 2페이즈 B(완전 부상): 물 위에 머물며 항모 쪽으로 다가와 멈춘다.
    ///  - 어뢰: 모듈의 어뢰 기록대로 항적(물보라)만 그린다(막을 수 없다 — 항모 피해는 모듈 오너가 판정).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_Submarine : UdonSharpBehaviour
    {
        public const int STAGE_SLCM = 0;
        public const int STAGE_CYCLE = 1;
        public const int STAGE_FINAL = 2;

        [Header("참조 (프리팹 빌더 · Bake)")]
        public ACO_Warship Warship;
        [Tooltip("Bake가 넣는다 — 동기화된 단계 · 시각 · 자리")]
        public ACO_PhaseSubmarine Phase;
        [Tooltip("선체 체력(본체 SaccTarget)")]
        public SaccTarget TargetHealth;
        [Tooltip("선체 모델(물속 깊으면 끈다)")]
        public GameObject Model;
        [Tooltip("선체 히트박스(루트) — 물 위일 때만 켠다")]
        public Collider[] HullColliders;
        [Tooltip("선체 락온 마커(이름 LockMarker, 지상 레이어 30) — 물 위일 때만 켠다")]
        public GameObject[] HullMarkers;
        [Tooltip("표적 마커를 그릴 자리(함교)")]
        public Transform MarkerPoint;
        [Tooltip("무장 부위(ACO_SAMSite · ACO_AAGunSite) — 물속이면 오브젝트를 끄고, 부상할 때 모듈이 모두 되살린다")]
        public UdonSharpBehaviour[] Weapons;
        [Tooltip("SLCM 풀(일제 발사 칸 SalvoSlots개씩 묶음 — 묶음 수 = 길이 / SalvoSlots)")]
        public ACO_Slcm[] Slcms;
        public int SalvoSlots = 6;
        [Tooltip("Bake가 넣는다 — 씬의 아군 요격 CIWS(순양함 등). 초음속 돌입한 SLCM을 떨어뜨린다")]
        public ACO_AAGunSite[] InterceptSites;

        [Header("연출 (프리팹 빌더)")]
        [Tooltip("소나 원(해면 위 고리 — 반경 1m 메시를 키운다)")]
        public Transform SonarRing;
        [Tooltip("소나 원 가운데 표적 마커 자리(표적 마커가 SONAR로 그린다)")]
        public GameObject SonarMarker;
        [Tooltip("발사 물기둥(월드 파티클 — 발사 자리에 내보낸다)")]
        public ParticleSystem Plume;
        [Tooltip("물보라(월드 파티클 — 어뢰 항적 · 부상 · 잠항)")]
        public ParticleSystem Foam;
        [Tooltip("폭발(SLCM 격추 · 명중 · 어뢰 명중 · 격침)")]
        public ACO_FortressBlast Blast;
        [Tooltip("기존 미사일(LASM) 폭발 묶음 — SLCM 격추 · 명중, 어뢰 명중에 돌려 쓴다(켤 때 재생, 파티클은 월드 공간이라 그 자리에 남는다). 비면 Blast")]
        public GameObject[] FxBlasts;
        [Tooltip("기존 미사일(LASM) 물기둥 묶음 — SLCM 발사 자리 · 어뢰 명중")]
        public GameObject[] FxWaters;
        [Tooltip("기존 미사일 격추 파편(ACO_WreckFx_Missile) 묶음 — SLCM 격추")]
        public ParticleSystem[] FxWrecks;
        [Tooltip("어뢰 항적 — 함선 항적(ACO_WaterFx 항적)과 같은 오브젝트. 어뢰 기록마다 2칸(MAX_TORPEDO × 2), 어뢰 머리를 따라 옮기면 움직인 거리만큼 거품을 남긴다")]
        public ACO_WaterFx[] TorpWakes;
        [Tooltip("무장 부위마다 파괴 연출(DeathEffects — Weapons와 같은 순서). 잠항하며 부위를 끌 때 같이 끈다: 켜 둔 채 부위만 끄면 부상해서 다시 켤 때 부서진 부위마다 폭발 · 연기 · 소리가 처음부터 다시 났다(6-213)")]
        public GameObject[] WeaponDeathFx;
        [Tooltip("발사 소리(발사 자리로 옮겨 튼다)")]
        public AudioSource LaunchSound;
        [Tooltip("무인기 사출구 덮개(6-217 — 바깥 경첩, 로컬 Z 둘레로 연다). 사출구마다 하나, DroneLaunchPoints와 같은 순서")]
        public Transform[] DroneDoors;
        [Tooltip("무인기가 나타나는 자리 · 방향(사출구 위, 바깥으로 비스듬히)")]
        public Transform[] DroneLaunchPoints;
        [Tooltip("덮개가 열리는 각도(도) · 여닫는 시간(초) · 사출 뒤 열어 두는 시간(초)")]
        public float DroneDoorAngle = 115f;
        public float DroneDoorOpenTime = 0.8f;
        public float DroneDoorHold = 2.5f;
        [Tooltip("항모 화재(체력 70 · 40 · 15% 아래에서 차례로 켠다). 자리 = 모듈 HomeBase 기준 CarrierFireOffset(m — 크기는 무시)")]
        public GameObject[] CarrierFire;
        public Vector3[] CarrierFireOffset;
        public float[] CarrierFireAt = new float[] { 70f, 40f, 15f };
        [Tooltip("격침 때 켤 불 · 연기")]
        public GameObject DeathFx;

        [Header("자세")]
        [Tooltip("잠항 깊이(m) — 루트 높이")]
        public float SubDepth = 60f;
        [Tooltip("이보다 깊으면 모델을 끈다(m)")]
        public float HideDepth = 30f;
        [Tooltip("뱃머리 끝까지(m) — 루트 기준. 충각 돌격이 뱃머리를 항모 뱃전에 댄다")]
        public float BowOffset = 131f;
        [Tooltip("보조 동체 스크류(로컬 Z로 돈다) — 물 위에 보일 때만 돈다. 둘째는 반대로")]
        public Transform[] Screws;
        [Tooltip("스크류 회전(도/초): 평소 · 충각 돌격")]
        public float ScrewSpin = 90f;
        public float ScrewSpinRam = 420f;
        [Tooltip("이보다 얕으면 히트박스 · 마커를 켠다(m) — 갑판이 물 위")]
        public float ExposeDepth = 3f;
        [Tooltip("이보다 얕으면 무장 부위를 켠다(m)")]
        public float WeaponDepth = 6f;
        [Tooltip("부상 · 잠항 때 기울기(도)")]
        public float RisePitch = 8f;
        public float DivePitch = 8f;
        [Tooltip("부상한 동안 앞으로 흐르는 속도(m/s)")]
        public float DriftSpeed = 4f;
        public float BobAngle = 0.5f;
        public float BobPeriod = 8f;
        [Tooltip("완전 부상 때 항모 쪽으로 도는 시간(초) — 그동안 속도가 오른다")]
        public float FinalTurnTime = 12f;

        [Header("격침")]
        public float DeathChainTime = 3f;
        public float SecondaryInterval = 0.5f;
        public float SinkTime = 25f;
        public float SinkDepth = 70f;
        public float SinkPitch = 25f;

        [Header("순양함 CIWS")]
        [Tooltip("CIWS 한 문이 돌입한 SLCM 한 발을 떨어뜨리는 데 걸리는 시간(초) — 사거리(InterceptRange) 안에 있어야 한다")]
        public float SlcmInterceptTime = 2.5f;
        [Tooltip("한 발을 떨어뜨린 뒤 다음 발까지 쉬는 시간(초)")]
        public float SlcmInterceptCooldown = 1f;

        [Header("부상 물보라(6-218)")]
        [Tooltip("부상 · 잠항하는 동안 거품을 선체 길이 · 양 뱃전을 따라 뿌린다 — 한 번에 몇 개(FoamInterval마다), 크기(m)")]
        public int FoamPerTick = 10;
        public float FoamSizeMin = 10f;
        public float FoamSizeMax = 17f;
        [Tooltip("거품을 뿌리는 선체 범위(모델 기준): 가운데 동체 앞뒤 반길이 · 반폭, 보조 동체 x · 앞뒤")]
        public float FoamHullHalfLength = 120f;
        public float FoamHullHalfWidth = 12f;
        public float FoamSideX = 32f;
        public float FoamSideZ0 = -125f;
        public float FoamSideZ1 = -20f;
        [Tooltip("부상하다 이 깊이(m)를 지나는 순간(갑판이 수면을 뚫는다) 선체를 따라 큰 물기둥")]
        public float BreachDepth = 3f;
        [Tooltip("물기둥 자리(모델 기준 — 뱃머리 · 함교 앞 · 함교 · 뒤 갑판 · 꼬리)")]
        public Vector3[] BreachPoints = new Vector3[] { new Vector3(0f, 0f, 110f), new Vector3(0f, 0f, 50f), new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, -50f), new Vector3(0f, 0f, -110f) };

        [Header("어뢰 항적")]
        public float TorpedoSpread = 40f;
        public float FoamInterval = 0.08f;

        // ───────────── 지역 상태 ─────────────
        private bool _initialized;
        private bool _spawned;
        private bool _dead;
        private bool _silentDead;
        private float _deadAt;
        private float _spawnedAt;
        // 출현 직후 격침(6-268): 풀에서 다시 쓴 잠수함에 지난 미션의 체력 0이 먼저 들어오면 스폰 직후 격침 이벤트가 온다 — 예전엔 늦게 들어온 격침으로 보고
        // 모델을 숨긴 채 페이즈 끝까지 남았다. 켜지거나 스폰된 지 SPAWN_DOWN_GRACE초 안의 격침은 보류하고, 체력이 살아 있는 걸로 오면 취소.
        // 늦게 들어온 사람(MissionRevive false)이면 바로, 아니면 DOWN_CONFIRM초 넘게 0일 때 연출 없이 가라앉은 상태로.
        private const float SPAWN_DOWN_GRACE = 1f;
        private const float DOWN_CONFIRM = 5f;
        private float _liveAt = -100f;
        private float _pendingExplodeAt = -1f;
        private bool _blasted;
        private bool _ramBlasted;
        private float _secondaryTimer;
        private Vector3 _deadPos;
        private Quaternion _deadRot = Quaternion.identity;
        private float _depth;
        private float _prevDepth = 999f;
        private bool _exposed;   // 물속에서 시작 — 표적 마커 · 레이더가 배치 직후 한순간이라도 선체를 그리지 않게
        private int _weaponsOn = -1;
        private float _slowTimer;
        private float _lineTimer;
        private float _foamTimer;
        private bool _homeSet;
        private int _armStamp;
        private int _blastNext;
        private int _waterNext;
        private int _wreckNext;
        private bool _sonarOn;
        private Vector3 _sonarCenter;
        private float _sonarRadius;

        private SaccTarget[] _weaponHealth;
        private int[] _icClaim;      // CIWS마다 맡은 SLCM 칸(-1 = 없음)
        private float[] _icProgress;
        private float[] _icBusyUntil;
        private int[] _torpSeen = new int[ACO_PhaseSubmarine.MAX_TORPEDO];   // 명중 연출을 튼 어뢰 기록(발사 시각)
        private Quaternion[] _doorBase;
        private float[] _doorOpen;
        private int[] _droneSeen = new int[ACO_PhaseSubmarine.MAX_DRONE_HATCH];   // 사출 소리를 튼 사출 시각

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!Warship) { Warship = GetComponent<ACO_Warship>(); }
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            int n = Weapons != null ? Weapons.Length : 0;
            _weaponHealth = new SaccTarget[n];
            for (int i = 0; i < n; i++) { if (Weapons[i] != null) { _weaponHealth[i] = Weapons[i].GetComponent<SaccTarget>(); } }
            int c = InterceptSites != null ? InterceptSites.Length : 0;
            _icClaim = new int[c];
            _icProgress = new float[c];
            _icBusyUntil = new float[c];
            for (int i = 0; i < c; i++) { _icClaim[i] = -1; }
            SetSonar(false, Vector3.zero, 0f);
            SetCarrierFire(0);
            int d = DroneDoors != null ? DroneDoors.Length : 0;
            _doorBase = new Quaternion[d];
            _doorOpen = new float[d];
            for (int i = 0; i < d; i++) { _doorBase[i] = DroneDoors[i] ? DroneDoors[i].localRotation : Quaternion.identity; }
        }

        // ───────────── 함선 이벤트 ─────────────

        /// ACO_Warship.MissionSpawn이 부른다(모든 클라이언트).
        public void ShipSpawned()
        {
            Init();
            _spawned = true;
            _dead = false;
            _silentDead = false;
            _blasted = false;
            _ramBlasted = false;
            _spawnedAt = Time.time;
            _liveAt = Time.time;
            if (_pendingExplodeAt >= 0f) { _pendingExplodeAt = Time.time; }   // 켜질 때 먼저 온 격침이면 스폰부터 다시 센다
            _weaponsOn = -1;
            _armStamp = 0;
            if (DeathFx) { DeathFx.SetActive(false); }
            if (Model) { Model.SetActive(true); }
            for (int i = 0; i < _icClaim.Length; i++) { _icClaim[i] = -1; _icProgress[i] = 0f; }
            for (int i = 0; i < _torpSeen.Length; i++) { _torpSeen[i] = 0; }
            DisarmAll();
            SetExposed(false);
            ApplyPose();
        }

        public void ShipDespawned()
        {
            _spawned = false;
            _dead = false;
            _pendingExplodeAt = -1f;
            DisarmAll();
            SetSonar(false, Vector3.zero, 0f);
            SetCarrierFire(0);
            HideTorpWakes();
            // 무장 부위를 켜 둔다 — 다음 스폰 때 ACO_Warship이 부위에 보내는 AttachedSpawn(꺼진 오브젝트는 못 받는다)을 받게
            _weaponsOn = -1;
            if (Weapons == null) { return; }
            QuietDeathFx();
            for (int i = 0; i < Weapons.Length; i++) { if (Weapons[i] != null && !Weapons[i].gameObject.activeSelf) { Weapons[i].gameObject.SetActive(true); } }
        }

        /// 부위의 파괴 연출을 끈다(한 번 터지고 끝나는 연출이라 끈 채로 둬도 부서진 모습은 그대로). 부위를 다시 켤 때 연출이 다시 재생되지 않게 —
        /// 부위가 되살아나면(NetRespawn) 부위 스크립트가 어차피 끄고, 다시 부서지면(Explode) 켠다.
        private void QuietDeathFx()
        {
            if (WeaponDeathFx == null) { return; }
            for (int i = 0; i < WeaponDeathFx.Length; i++) { if (WeaponDeathFx[i] && WeaponDeathFx[i].activeSelf) { WeaponDeathFx[i].SetActive(false); } }
        }

        /// 선체 격침(SaccTarget.ExplodeOther — 모든 클라이언트): 연쇄 폭발 뒤 대폭발, 선미부터 가라앉는다.
        public void Explode()
        {
            if (_dead) { return; }
            if (!_spawned || Time.time - _liveAt < SPAWN_DOWN_GRACE)
            {
                if (_pendingExplodeAt < 0f) { _pendingExplodeAt = Time.time; }   // 출현 직후 격침은 보류(6-268)
                return;
            }
            ApplyExplode(false);
        }

        void OnEnable() { _liveAt = Time.time; }

        private bool HullDown()
        {
            if (TargetHealth != null) { return TargetHealth.Health <= 0f; }
            return Warship == null || Warship.IsDestroyed();
        }

        /// silent: 늦게 들어왔거나 출현 때부터 격침돼 있던 것 — 연출 · 대사 없이 가라앉은 상태로.
        private void ApplyExplode(bool silent)
        {
            _pendingExplodeAt = -1f;
            if (_dead) { return; }
            _dead = true;
            _deadAt = Time.time;
            _deadPos = transform.position;
            _deadRot = transform.rotation;
            _secondaryTimer = 0f;
            _silentDead = silent;
            _blasted = _silentDead;
            if (_silentDead && Model) { Model.SetActive(false); }
            if (DeathFx) { DeathFx.SetActive(!_silentDead); }
            SetExposed(false);
            if (Phase != null && Phase.IsLive() && !_silentDead) { Phase.SayDown(); }
        }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            if (!_spawned) { return; }
            float dt = Time.deltaTime;
            if (_pendingExplodeAt >= 0f)
            {
                // 보류한 출현 직후 격침(6-268): 체력이 살아 있으면 지난 미션 값이었다 — 취소. 늦게 들어왔거나 오래 0이면 연출 없이 가라앉은 상태로.
                if (!HullDown()) { _pendingExplodeAt = -1f; }
                else if ((Warship != null && !Warship.MissionRevive) || Time.time - _pendingExplodeAt >= DOWN_CONFIRM) { ApplyExplode(true); }
            }
            if (_dead) { UpdateDeath(dt); }
            else { ApplyPose(); CheckBreach(); }

            _slowTimer -= dt;
            if (_slowTimer <= 0f)
            {
                _slowTimer = 0.25f;
                UpdateSlcms();
                UpdateExposure();
                UpdateSonar();
                UpdateCarrier();
            }
            if (!_dead) { UpdateIntercepts(dt); SpinScrews(dt); }
            if (_sonarOn) { PlaceSonar(); }
            if (!_dead) { UpdateDroneDoors(); }   // 잠수함이 움직이고 기울어도 원은 수면에 수평으로 그 자리에(매 프레임 — 0.25초마다면 그 사이에 따라 기울어 흔들렸다)
            UpdateTorpedoes(dt);

            _lineTimer -= dt;
            if (_lineTimer <= 0f)
            {
                _lineTimer = 0.5f;
                if (Phase != null) { Phase.CheckLines(this); }
            }
        }

        private int Now() { return Networking.GetServerTimeInMilliseconds(); }

        private bool Live() { return Phase != null && Phase.IsLive(); }

        // ───────────── 자세 ─────────────

        private void ApplyPose()
        {
            if (!Live())
            {
                // 값을 아직 못 받았다: 스폰 자리 깊이 숨어 있는다
                Vector3 p0 = Warship != null ? Warship.MissionSpawnPosition : transform.position;
                SetPose(new Vector3(p0.x, -SubDepth * 2f, p0.z), 0f, 0f, 0f);
                return;
            }
            int now = Now();
            int stage = Phase.GetStage();
            if (stage == STAGE_SLCM)
            {
                // 가장 최근에 정한 발사 자리 아래 깊이 숨어 있다
                int last = 0;
                for (int k = 0; k < Phase.GetSalvoCount(); k++) { if (Phase.GetSalvoMs(k) != 0) { last = k; } }
                Vector3 s = Phase.GetSlcmFrom(last, 0);
                SetPose(new Vector3(s.x, -SubDepth * 2f, s.z), 0f, 0f, 0f);
                return;
            }
            if (stage == STAGE_FINAL && Phase.IsRamming())
            {
                // 충각 돌격(6-203): 동기화된 길을 시간 함수로 — 처음 RamAccelTime 동안 돌며 가속
                float er = Mathf.Max((now - Phase.GetFinalMs()) * 0.001f, 0f);
                float dr = Phase.RamDistanceAt(er);
                Vector3 pr = Phase.RamPointAt(dr);
                float ta = Mathf.Max(Phase.RamAccelTime, 0.1f);
                float yawR = Mathf.LerpAngle(Phase.GetFinalYaw0(), Phase.RamYawAt(dr), Mathf.SmoothStep(0f, 1f, er / ta));
                SetPose(new Vector3(pr.x, 0f, pr.z), yawR, 0f, Bob(now));
                // 뱃전에 닿았다(관리자 오너가 정한 충돌 — 동기화): 뱃머리에서 대폭발. 시각만 보고 터뜨리면 유예 안에 격침된 경우(성공)에도 터졌다.
                // 한 번만 — 늦게 들어온 사람은 지난 일이면(닿고 3초 넘게 지남) 건너뛴다
                if (Phase.HasRammed() && !_ramBlasted)
                {
                    _ramBlasted = true;
                    if (er < Phase.RamTime + Phase.RamGrace + 3f && Blast) { Blast.Play(transform.position + transform.forward * BowOffset + Vector3.up * 8f, false, 0.3f); }
                }
                return;
            }
            if (stage == STAGE_FINAL)
            {
                float e = Mathf.Max((now - Phase.GetFinalMs()) * 0.001f, 0f);
                Vector3 a = Phase.GetFinalFrom();
                Vector3 b = Phase.GetFinalTo();
                Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
                float len = flat.magnitude;
                float T = Mathf.Max(FinalTurnTime, 0.1f);
                float v = Phase.FinalSpeed;
                float d = e < T ? v * e * e / (2f * T) : v * (e - T * 0.5f);
                d = Mathf.Min(d, len);
                Vector3 pos = len > 0.5f ? a + flat / len * d : a;
                float yaw = Mathf.LerpAngle(Phase.GetFinalYaw0(), Phase.GetFinalYaw1(), Mathf.SmoothStep(0f, 1f, e / T));
                SetPose(new Vector3(pos.x, 0f, pos.z), yaw, 0f, Bob(now));
                return;
            }
            // 간헐 부상: 잠항 → 부상 → 수상 → 잠항
            float tc = (now - Phase.GetCycleMs()) * 0.001f;
            float sub = Phase.SubmergedTime;
            float rise = Mathf.Max(Phase.RiseTime, 0.1f);
            float surf = Phase.SurfaceTime;
            float dive = Mathf.Max(Phase.DiveTime, 0.1f);
            float y = -SubDepth;
            float pitch = 0f;
            if (tc >= sub && tc < sub + rise)
            {
                float k = (tc - sub) / rise;
                y = Mathf.Lerp(-SubDepth, 0f, Mathf.SmoothStep(0f, 1f, k));
                pitch = -RisePitch * Mathf.Sin(k * Mathf.PI);
            }
            else if (tc >= sub + rise && tc < sub + rise + surf) { y = 0f; }
            else if (tc >= sub + rise + surf && tc < sub + rise + surf + dive)
            {
                float k = (tc - sub - rise - surf) / dive;
                y = Mathf.Lerp(0f, -SubDepth, Mathf.SmoothStep(0f, 1f, k));
                pitch = DivePitch * Mathf.Sin(k * Mathf.PI);
            }
            float cycleYaw = Phase.GetCycleYaw();
            float drift = DriftSpeed * Mathf.Clamp(tc - sub, 0f, rise + surf + dive);
            Vector3 spot = Phase.GetSpot();
            Vector3 fwd = Quaternion.Euler(0f, cycleYaw, 0f) * Vector3.forward;
            Vector3 p = spot + fwd * drift;
            SetPose(new Vector3(p.x, y, p.z), cycleYaw, pitch, y > -1f ? Bob(now) : 0f);
        }

        /// 스크류: 모델이 보일 때만(물속 깊으면 꺼져 있다). 충각 돌격이면 빠르게.
        private void SpinScrews(float dt)
        {
            if (Screws == null || !Model || !Model.activeSelf) { return; }
            float rate = Live() && Phase.GetStage() == STAGE_FINAL && Phase.IsRamming() ? ScrewSpinRam : ScrewSpin;
            for (int i = 0; i < Screws.Length; i++)
            {
                if (!Screws[i]) { continue; }
                float sign = i % 2 == 0 ? 1f : -1f;
                Screws[i].localRotation = Screws[i].localRotation * Quaternion.Euler(0f, 0f, rate * sign * dt);
            }
        }

        private float Bob(int now)
        {
            if (BobAngle <= 0f || BobPeriod <= 0f) { return 0f; }
            return Mathf.Sin(now * 0.001f / BobPeriod * Mathf.PI * 2f) * BobAngle;
        }

        private void SetPose(Vector3 pos, float yaw, float pitch, float roll)
        {
            _depth = -pos.y;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, roll));
            bool show = _depth < HideDepth;
            if (Model && Model.activeSelf != show) { Model.SetActive(show); }
        }

        /// 무인기 사출구(6-217): 덮개는 사출 DroneDoorLead초 전부터 열려 사출 뒤 DroneDoorHold초까지 열려 있다(동기화된 사출 시각으로 모든 화면이 같게).
        /// 사출 순간 그 자리에서 사출 소리 · 물보라. 덮개 각도가 바뀔 때만 쓴다.
        private void UpdateDroneDoors()
        {
            if (DroneDoors == null || _doorOpen == null || Phase == null) { return; }
            int now = Now();
            float lead = Mathf.Max(Phase.DroneDoorLead, 0.01f);
            float move = Mathf.Max(DroneDoorOpenTime, 0.01f);
            for (int k = 0; k < DroneDoors.Length; k++)
            {
                if (!DroneDoors[k]) { continue; }
                int ms = Phase.GetDroneMs(k);
                float open = 0f;
                if (ms != 0)
                {
                    float t = (now - ms) * 0.001f;
                    if (t >= -lead && t < DroneDoorHold) { open = Mathf.Clamp01((t + lead) / move); }
                    else if (t >= DroneDoorHold) { open = 1f - Mathf.Clamp01((t - DroneDoorHold) / move); }
                    if (t >= 0f && t < 1f && k < _droneSeen.Length && _droneSeen[k] != ms)
                    {
                        _droneSeen[k] = ms;
                        Vector3 p = DroneDoors[k].position;
                        if (LaunchSound) { LaunchSound.transform.position = p; LaunchSound.Play(); }
                        if (Foam)
                        {
                            var emit = new ParticleSystem.EmitParams();
                            emit.position = p;
                            emit.applyShapeToPosition = true;
                            Foam.Emit(emit, 10);
                        }
                    }
                }
                if (Mathf.Abs(open - _doorOpen[k]) < 0.001f) { continue; }
                _doorOpen[k] = open;
                DroneDoors[k].localRotation = _doorBase[k] * Quaternion.Euler(0f, 0f, -DroneDoorAngle * Mathf.SmoothStep(0f, 1f, open));
            }
        }

        /// 사출구 k의 무인기 등장 자리 · 방위(모듈 오너가 생성기에 넘긴다).
        public Vector3 GetDroneLaunchPoint(int k)
        {
            if (DroneLaunchPoints == null || k < 0 || k >= DroneLaunchPoints.Length || !DroneLaunchPoints[k]) { return transform.position + Vector3.up * 15f; }
            return DroneLaunchPoints[k].position;
        }

        public float GetDroneLaunchYaw(int k)
        {
            if (DroneLaunchPoints == null || k < 0 || k >= DroneLaunchPoints.Length || !DroneLaunchPoints[k]) { return transform.eulerAngles.y; }
            return DroneLaunchPoints[k].eulerAngles.y;
        }

        /// 지금 자리(모듈 오너가 완전 부상 출발점으로 쓴다).
        public Vector3 GetPosition() { return transform.position; }
        public float GetYaw() { return transform.eulerAngles.y; }

        /// 갑판이 물 위인가(히트박스 · 마커가 켜져 있다).
        public bool IsExposed() { return _exposed; }

        private void UpdateDeath(float dt)
        {
            if (_silentDead) { return; }
            float age = Time.time - _deadAt;
            if (!_blasted)
            {
                _secondaryTimer -= dt;
                if (_secondaryTimer <= 0f && Blast)
                {
                    _secondaryTimer = SecondaryInterval;
                    Blast.PlaySecondary(transform.TransformPoint(new Vector3(Random.Range(-34f, 34f), Random.Range(3f, 14f), Random.Range(-115f, 110f))));
                }
                if (age >= DeathChainTime)
                {
                    _blasted = true;
                    if (Blast) { Blast.Play(transform.position + Vector3.up * 5f, true, 0.35f); }
                }
            }
            // 선미부터(뱃머리가 들린 채) 가라앉는다
            float k = Mathf.Clamp01(age / Mathf.Max(SinkTime, 0.1f));
            float tilt = SinkPitch * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 2.5f));
            Vector3 e = _deadRot.eulerAngles;
            transform.SetPositionAndRotation(_deadPos + Vector3.down * (SinkDepth * k * k), Quaternion.Euler(-tilt, e.y, e.z));
            if (k >= 1f)
            {
                if (Model && Model.activeSelf) { Model.SetActive(false); }
                if (DeathFx && DeathFx.activeSelf) { DeathFx.SetActive(false); }
            }
        }

        /// 격침 뒤 대폭발까지 지났는가(모듈이 성공을 미룬다).
        public bool IsBlasted() { return _blasted; }

        // ───────────── 노출 · 무장 ─────────────

        private void UpdateExposure()
        {
            bool live = Live() && !_dead;
            bool expose = live && Phase.GetStage() >= STAGE_CYCLE && _depth < ExposeDepth;
            SetExposed(expose);
            bool weapons = !_dead && live && Phase.GetStage() >= STAGE_CYCLE && _depth < WeaponDepth;
            if (_dead) { weapons = _depth < WeaponDepth + 30f; }   // 가라앉는 동안은 선체와 함께 보인다(부위는 이미 침묵)
            int code = weapons ? 1 : 0;
            if (code == _weaponsOn) { return; }
            _weaponsOn = code;
            if (Weapons == null) { return; }
            if (!weapons) { QuietDeathFx(); }
            for (int i = 0; i < Weapons.Length; i++)
            {
                if (Weapons[i] != null && Weapons[i].gameObject.activeSelf != weapons) { Weapons[i].gameObject.SetActive(weapons); }
            }
        }

        private void SetExposed(bool on)
        {
            _exposed = on;
            if (HullColliders != null)
            {
                for (int i = 0; i < HullColliders.Length; i++) { if (HullColliders[i] && HullColliders[i].enabled != on) { HullColliders[i].enabled = on; } }
            }
            if (HullMarkers != null)
            {
                for (int i = 0; i < HullMarkers.Length; i++) { if (HullMarkers[i] && HullMarkers[i].activeSelf != on) { HullMarkers[i].SetActive(on); } }
            }
            if (Warship != null) { Warship.MarkerAnchor = MarkerPoint; }
        }

        /// 부서진 무장 부위를 모두 되살린다(모듈 오너가 부상할 때 부른다 — 모든 클라이언트에 알린다). 되살린 수.
        public int RepairWeapons() { return RepairSome(0); }

        /// 부서진 무장 부위 중 최대 max개를 무작위로 되살린다(0 이하 = 모두 — 6-207: 부상할 때는 3문). 모듈 오너가 부르고 모든 클라이언트에 알린다. 되살린 수.
        public int RepairSome(int max)
        {
            Init();
            int n = _weaponHealth.Length;
            int down = 0;
            for (int i = 0; i < n; i++) { if (IsWeaponDown(i)) { down++; } }
            if (down == 0) { return 0; }
            int want = max > 0 ? Mathf.Min(max, down) : down;
            int done = 0;
            // 부서진 것 중 무작위: 남은 부서진 수 중 남은 몫만큼의 확률로 고른다(순서대로 보며 한 번씩 — 고르게 섞인다)
            int left = down;
            for (int i = 0; i < n && done < want; i++)
            {
                if (!IsWeaponDown(i)) { continue; }
                if (Random.Range(0, left) < want - done)
                {
                    Weapons[i].SendCustomNetworkEvent(NetworkEventTarget.All, "NetRespawn");
                    done++;
                }
                left--;
            }
            return done;
        }

        private bool IsWeaponDown(int i)
        {
            return Weapons[i] != null && _weaponHealth[i] != null && _weaponHealth[i].Health <= 0f;
        }

        /// 격침 뒤 다 가라앉아 모델이 꺼졌는가(늦게 들어와 연출 없이 가라앉은 상태면 바로 참) — 모듈이 이 뒤에 성공으로 넘겨 풀로 거둔다.
        public bool IsSunkDone()
        {
            if (!_dead) { return false; }
            return _silentDead || Time.time - _deadAt >= SinkTime;
        }

        public int CountWeaponsDown()
        {
            Init();
            int n = 0;
            for (int i = 0; i < _weaponHealth.Length; i++) { if (_weaponHealth[i] != null && _weaponHealth[i].Health <= 0f) { n++; } }
            return n;
        }

        public bool IsHullDestroyed() { return TargetHealth != null && TargetHealth.Health <= 0f; }

        public float GetHullRatio()
        {
            if (TargetHealth == null || TargetHealth.FullHealth <= 0f) { return 1f; }
            return Mathf.Clamp01(TargetHealth.Health / TargetHealth.FullHealth);
        }

        // ───────────── SLCM ─────────────

        private void DisarmAll()
        {
            if (Slcms == null) { return; }
            for (int i = 0; i < Slcms.Length; i++) { if (Slcms[i] != null) { Slcms[i].Disarm(); } }
        }

        public int GetBankCount() { return SalvoSlots > 0 && Slcms != null ? Slcms.Length / SalvoSlots : 0; }

        /// 일제 발사 s의 k번째 미사일이 쓰는 풀 칸(-1 = 풀이 모자람).
        public int SlotOf(int s, int k)
        {
            int banks = GetBankCount();
            if (banks <= 0 || k < 0 || k >= SalvoSlots) { return -1; }
            return (s % banks) * SalvoSlots + k;
        }

        public ACO_Slcm GetSlcm(int slot) { return Slcms != null && slot >= 0 && slot < Slcms.Length ? Slcms[slot] : null; }

        /// 묶음마다 지금 맡은 일제 발사(발사 ArmLead초 전부터)를 넣는다. 모든 클라이언트가 같은 동기화 값으로 같은 칸을 고른다.
        private void UpdateSlcms()
        {
            if (Slcms == null) { return; }
            if (!Live() || _dead)
            {
                if (_armStamp != 0) { DisarmAll(); _armStamp = 0; }
                return;
            }
            int stamp = Phase.GetStamp();
            if (_armStamp != stamp) { DisarmAll(); _armStamp = stamp; }
            int now = Now();
            int banks = GetBankCount();
            int count = Phase.GetSalvoCount();
            int lead = Mathf.RoundToInt(Phase.ArmLead * 1000f);
            for (int b = 0; b < banks; b++)
            {
                int cur = -1;
                for (int s = b; s < count; s += banks)
                {
                    int ms = Phase.GetSalvoMs(s);
                    if (ms != 0 && now - (ms - lead) >= 0) { cur = s; }   // 0 = 아직 안 정한 발사(앞 무리가 정리되면 정한다)
                }
                int size = cur >= 0 ? Phase.GetSalvoSize(cur) : 0;
                for (int k = 0; k < SalvoSlots; k++)
                {
                    ACO_Slcm m = Slcms[b * SalvoSlots + k];
                    if (m == null) { continue; }
                    if (k < size) { m.Arm(Phase.SlcmKey(cur, k), Phase.GetSlcmLaunchMs(cur, k), Phase.GetSlcmFrom(cur, k), Phase.GetSlcmImpact(cur, k)); }
                    else { m.Disarm(); }
                }
            }
        }

        /// 순양함 CIWS: 사거리 안에 들어온 SLCM 하나를 맡아 SlcmInterceptTime 동안 붙잡으면 떨어뜨린다(체력 오너 화면 — 체력 0이 동기화된다).
        /// 한 문은 한 번에 한 발만, 떨어뜨린 뒤 SlcmInterceptCooldown 쉰다. 예광탄은 CIWS가 공중 표적(SLCM 마커)을 스스로 노려 쏜다.
        private void UpdateIntercepts(float dt)
        {
            if (InterceptSites == null || Slcms == null || !Live()) { return; }
            float now = Time.time;
            for (int i = 0; i < InterceptSites.Length; i++)
            {
                ACO_AAGunSite site = InterceptSites[i];
                if (site == null || !site.gameObject.activeInHierarchy || (site.TargetHealth != null && site.TargetHealth.Health <= 0f)) { _icClaim[i] = -1; continue; }
                Vector3 p = site.transform.position;
                float rangeSq = site.InterceptRange * site.InterceptRange;
                int c = _icClaim[i];
                if (c >= 0)
                {
                    ACO_Slcm m = Slcms[c];
                    if (m == null || !m.IsFlying() || (m.transform.position - p).sqrMagnitude > rangeSq) { _icClaim[i] = -1; continue; }
                    _icProgress[i] += dt;
                    if (_icProgress[i] < SlcmInterceptTime) { continue; }
                    m.InterceptKill();
                    _icClaim[i] = -1;
                    _icBusyUntil[i] = now + SlcmInterceptCooldown;
                    continue;
                }
                if (now < _icBusyUntil[i]) { continue; }
                int best = -1;
                float bestSq = rangeSq;
                for (int k = 0; k < Slcms.Length; k++)
                {
                    ACO_Slcm m = Slcms[k];
                    if (m == null || !m.IsFlying() || IsClaimed(k)) { continue; }
                    float sq = (m.transform.position - p).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; best = k; }
                }
                if (best < 0) { continue; }
                _icClaim[i] = best;
                _icProgress[i] = 0f;
            }
        }

        private bool IsClaimed(int k)
        {
            for (int i = 0; i < _icClaim.Length; i++) { if (_icClaim[i] == k) { return true; } }
            return false;
        }

        // ───────────── 연출 (SLCM이 부른다) ─────────────

        public void PlaySlcmLaunch(Vector3 point)
        {
            if (!PlayWater(point) && Plume)
            {
                var emit = new ParticleSystem.EmitParams();
                emit.position = point;
                emit.applyShapeToPosition = true;
                Plume.Emit(emit, 24);
            }
            if (LaunchSound)
            {
                LaunchSound.transform.position = point;
                LaunchSound.Play();
            }
        }

        public void PlaySlcmShootDown(Vector3 point, Vector3 forward)
        {
            if (!PlayBlast(point) && Blast) { Blast.PlaySecondary(point); }
            PlayWreck(point, forward);
        }

        public void PlaySlcmImpact(Vector3 point)
        {
            if (PlayBlast(point)) { PlayBlast(point + Vector3.up * 12f); return; }
            if (!Blast) { return; }
            Blast.PlaySecondary(point);
            Blast.PlaySecondary(point + Vector3.up * 12f);
        }

        /// 기존 미사일 폭발 하나를 그 자리에서 다시 켠다(묶음을 차례로 돌려 쓴다). 묶음이 없으면 false.
        private bool PlayBlast(Vector3 point)
        {
            if (FxBlasts == null || FxBlasts.Length == 0) { return false; }
            GameObject g = FxBlasts[_blastNext % FxBlasts.Length];
            _blastNext++;
            return Replay(g, point);
        }

        private bool PlayWater(Vector3 point)
        {
            if (FxWaters == null || FxWaters.Length == 0) { return false; }
            GameObject g = FxWaters[_waterNext % FxWaters.Length];
            _waterNext++;
            return Replay(g, new Vector3(point.x, 0f, point.z));
        }

        /// 파편(ACO_WreckFx_Missile)은 로컬 +Z로 쏜다 — 미사일이 날던 방향을 보게 놓는다.
        private void PlayWreck(Vector3 point, Vector3 forward)
        {
            if (FxWrecks == null || FxWrecks.Length == 0) { return; }
            ParticleSystem ps = FxWrecks[_wreckNext % FxWrecks.Length];
            _wreckNext++;
            if (!ps) { return; }
            ps.transform.SetPositionAndRotation(point, forward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(forward) : Quaternion.identity);
            ps.Play(true);
        }

        private bool Replay(GameObject g, Vector3 point)
        {
            if (!g) { return false; }
            g.transform.SetPositionAndRotation(point, Quaternion.identity);
            if (g.activeSelf) { g.SetActive(false); }
            g.SetActive(true);   // 파티클 · 폭발음이 켜질 때 재생된다(기존 미사일과 같다)
            return true;
        }

        // ───────────── 소나 · 항모 화재 ─────────────

        private void UpdateSonar()
        {
            int now = Now();
            if (!Live() || _dead || !Phase.IsSonarShown(now)) { SetSonar(false, Vector3.zero, 0f); return; }
            SetSonar(true, Phase.GetSonarCenter(), Phase.SonarRadius);
        }

        private void SetSonar(bool on, Vector3 center, float radius)
        {
            _sonarOn = on;
            _sonarCenter = center;
            _sonarRadius = radius;
            if (SonarRing && SonarRing.gameObject.activeSelf != on) { SonarRing.gameObject.SetActive(on); }
            if (SonarMarker && SonarMarker.activeSelf != on) { SonarMarker.SetActive(on); }
            if (on) { PlaceSonar(); }
        }

        /// 소나 원(해면 1m, 수평) · 마커(30m)를 월드 자리에 둔다. 둘 다 잠수함 자식이라 잠수함이 움직이면 매 프레임 다시 놓는다.
        private void PlaceSonar()
        {
            if (SonarRing)
            {
                SonarRing.SetPositionAndRotation(new Vector3(_sonarCenter.x, 1f, _sonarCenter.z), Quaternion.identity);
                SonarRing.localScale = new Vector3(_sonarRadius, 1f, _sonarRadius);
            }
            if (SonarMarker) { SonarMarker.transform.SetPositionAndRotation(new Vector3(_sonarCenter.x, 30f, _sonarCenter.z), Quaternion.identity); }
        }

        private void UpdateCarrier()
        {
            if (!Live()) { SetCarrierFire(0); return; }
            if (!_homeSet && Phase.HomeBase != null && CarrierFire != null && CarrierFireOffset != null)
            {
                _homeSet = true;
                for (int i = 0; i < CarrierFire.Length && i < CarrierFireOffset.Length; i++)
                {
                    if (CarrierFire[i]) { CarrierFire[i].transform.position = Phase.HomePoint(CarrierFireOffset[i]); }
                }
            }
            float hp = Phase.GetCarrierHp();
            int level = 0;
            if (CarrierFireAt != null) { for (int i = 0; i < CarrierFireAt.Length; i++) { if (hp <= CarrierFireAt[i]) { level = i + 1; } } }
            SetCarrierFire(level);
        }

        private void SetCarrierFire(int level)
        {
            if (CarrierFire == null) { return; }
            for (int i = 0; i < CarrierFire.Length; i++)
            {
                bool on = i < level;
                if (CarrierFire[i] && CarrierFire[i].activeSelf != on) { CarrierFire[i].SetActive(on); }
            }
        }

        // ───────────── 어뢰 항적 ─────────────

        private void HideTorpWakes()
        {
            if (TorpWakes == null) { return; }
            for (int i = 0; i < TorpWakes.Length; i++) { if (TorpWakes[i] && TorpWakes[i].gameObject.activeSelf) { TorpWakes[i].gameObject.SetActive(false); } }
        }

        /// 어뢰 항적 칸 하나를 어뢰 머리로 옮긴다(처음이면 켠다 — 켤 때 남은 거품을 지운다).
        private void MoveTorpWake(int slot, Vector3 head, Vector3 next)
        {
            if (TorpWakes == null || slot >= TorpWakes.Length || !TorpWakes[slot]) { return; }
            ACO_WaterFx w = TorpWakes[slot];
            Vector3 d = new Vector3(next.x - head.x, 0f, next.z - head.z);
            Quaternion rot = d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : w.transform.rotation;
            w.transform.SetPositionAndRotation(new Vector3(head.x, 0f, head.z), rot);
            if (!w.gameObject.activeSelf) { w.gameObject.SetActive(true); }
        }

        private void UpdateTorpedoes(float dt)
        {
            if (!Live()) { HideTorpWakes(); return; }
            _foamTimer -= dt;
            bool emitFoam = _foamTimer <= 0f;
            if (emitFoam) { _foamTimer = FoamInterval; }
            int now = Now();
            for (int i = 0; i < ACO_PhaseSubmarine.MAX_TORPEDO; i++)
            {
                int ms = Phase.GetTorpMs(i);
                if (ms == 0) { continue; }
                int num = Phase.GetTorpNum(i);
                float travel = Phase.GetTorpTravel(i);
                float e = (now - ms) * 0.001f;
                if (e < 0f) { continue; }
                if (e >= travel)
                {
                    if (_torpSeen[i] == ms) { continue; }
                    _torpSeen[i] = ms;
                    if (e > travel + 2f || !Blast) { continue; }   // 늦게 들어왔다 — 지난 명중
                    Vector3 hit = Phase.GetTorpImpact(i, 0);
                    if (PlayWater(hit)) { PlayBlast(new Vector3(hit.x, 2f, hit.z)); }
                    else { Blast.PlaySecondary(new Vector3(hit.x, 2f, hit.z)); }
                    continue;
                }
                // 달리는 어뢰: 함선 항적과 같은 항적 오브젝트가 머리를 따라간다(멈추면 거품이 더 생기지 않고 남은 것은 사라진다)
                for (int k = 0; k < num && k < 2; k++)
                {
                    MoveTorpWake(i * 2 + k, Phase.GetTorpPosition(i, k, e), Phase.GetTorpPosition(i, k, e + 0.2f));
                }
            }
            // 부상 · 잠항 물보라(6-218): 가운데 한 점이 아니라 선체 길이 · 양 뱃전 · 보조 동체를 따라 크게
            if (emitFoam && Foam && !_dead && Phase.GetStage() == STAGE_CYCLE && _depth > 0.5f && _depth < 25f)
            {
                var emit2 = new ParticleSystem.EmitParams();
                emit2.applyShapeToPosition = true;
                for (int i = 0; i < FoamPerTick; i++)
                {
                    float side = Random.value < 0.5f ? -1f : 1f;
                    Vector3 local = Random.value < 0.7f
                        ? new Vector3(side * FoamHullHalfWidth * Random.Range(0.6f, 1.1f), 0f, Random.Range(-FoamHullHalfLength, FoamHullHalfLength))
                        : new Vector3(side * (FoamSideX + Random.Range(-8f, 8f)), 0f, Random.Range(FoamSideZ0, FoamSideZ1));
                    Vector3 w = transform.TransformPoint(local);
                    emit2.position = new Vector3(w.x, 0.5f, w.z);
                    emit2.startSize = Random.Range(FoamSizeMin, FoamSizeMax);
                    Foam.Emit(emit2, 1);
                }
            }
        }

        /// 부상하다 BreachDepth를 지나는 순간(6-218): 선체를 따라 큰 물기둥(기존 미사일 물기둥 묶음) + 거품 한꺼번에. 깊은 곳에서 순간 이동(배치 · 늦게 들어옴)이면 건너뛴다.
        private void CheckBreach()
        {
            float prev = _prevDepth;
            _prevDepth = _depth;
            if (!Live() || Phase.GetStage() != STAGE_CYCLE) { return; }
            if (!(prev > BreachDepth && _depth <= BreachDepth && prev < BreachDepth + 10f)) { return; }
            if (BreachPoints != null)
            {
                for (int i = 0; i < BreachPoints.Length; i++) { PlayWater(transform.TransformPoint(BreachPoints[i])); }
            }
            if (Foam)
            {
                var emit = new ParticleSystem.EmitParams();
                emit.applyShapeToPosition = true;
                for (int i = 0; i < 40; i++)
                {
                    Vector3 w = transform.TransformPoint(new Vector3((Random.value < 0.5f ? -1f : 1f) * FoamHullHalfWidth * Random.Range(0.5f, 1.2f), 0f, Random.Range(-FoamHullHalfLength, FoamHullHalfLength)));
                    emit.position = new Vector3(w.x, 0.5f, w.z);
                    emit.startSize = Random.Range(FoamSizeMax, FoamSizeMax * 1.5f);
                    Foam.Emit(emit, 1);
                }
            }
        }
    }
}
