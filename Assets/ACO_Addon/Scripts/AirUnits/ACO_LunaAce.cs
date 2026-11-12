using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// UAV 에이스 LUNA(NPC_Luna 안의 자식 오브젝트) — 설계 6-137. 기체·비행·AI·동기화는 일반 NPC 전투기(ACO_NPCFighterAI) 그대로 쓰고,
    /// 이 스크립트가 도시 선회·유도 레이저·레일건 조준 경고·요격 UAV·무전을 더한다. 미션 생성기는 일반 배치에 쓰지 않는다 — LUNA 페이즈(ACO_PhaseLuna)만 내보낸다(OBJ_LUNA).
    ///
    /// - 선회(AI 오너 — 조종 덮어쓰기, 동기화는 기체 움직임 그대로): 도시 위 OrbitAltitude, 반지름 OrbitRadius를 돈다. 플레이어가 EngageDistance 안에 들어오면
    ///   덮어쓰기를 풀어 평소 전투(추격 — 레일건은 AI의 미사일 런처)를 하고, 도시에서 LeashDistance보다 멀어지면 다시 궤도로 돌아온다. 덮어쓰는 동안에도 AI는 기수 앞 표적에 쏜다.
    /// - 유도 레이저(모든 화면): 페이즈가 유도 중이면 기체 아래(Designator)에서 도시 땅까지 빛줄기.
    /// - 레일건: AI의 미사일 런처(곧게 나는 초고속 탄 ACO_Missile_LunaRail, 좁은 발사 원뿔·긴 락온 = 충전). 기수가 플레이어를 WarnAngle 안에 겨누면
    ///   모든 화면에 조준 레이저(가는 빛줄기)가 보이고, 그 플레이어 화면엔 RAILGUN WARNING(조종석 음성). 포구(발사 지점)는 기수에서 GimbalAngle 안으로
    ///   그 플레이어의 앞 자리(탄이 닿는 시간만큼 지금 속도로 간 자리)를 겨눈다 — 탄은 곧게 나므로, 피하려면 2초 충전(락온) 동안 기수 앞에서 벗어나야 한다.
    ///   각 화면이 자기 화면의 표적으로 겨누고, 맞는지는 맞는 사람 화면(자기 기체의 실제 움직임)이 판정한다.
    /// - UAV(기체의 자식 Uavs — 처음엔 꺼 두고 기체에 달린 포드 모양 DockedPods를 보인다): 페이즈가 전개 차수를 올리면 켜서 기체에서 UavSlots 자리로 펼쳐 따라다닌다
    ///   (모든 화면이 기체 위치로 같게 계산 — 따로 동기화 없음). 요격은 UAV의 ACO_AAGunSite(함선 CIWS와 똑같다 — 포탑이 미사일 쪽으로 돌고 기총이 쏜다, 판정은 미사일)가 하고,
    ///   연출만 예광탄 대신 파란 레이저(기총의 레이저 부품 ACO_NPCLaser — 레이저 에이스와 같다. 포구 정면으로 길게, 포탑이 돌면 휘두른다).
    ///   전개 직후(3초 안)면 되살리고(체력 복구 — UAV 오너가 한다), 늦게 들어온 화면은 생사를 체력 동기화로 맞춘다. 격추된 UAV는 그 자리에 멈춘다(폭발 연출).
    ///   LUNA가 격추되면 UAV도 모두 터지고 몇 초 뒤 숨긴다.
    /// - 저격(AI 오너 — 교전 중): SnipeInterval마다, 또는 플레이어가 꼬리(뒤 ±60도) TailThreatDistance 안에 붙으면(지난 저격 뒤 SnipeTailCooldown) —
    ///   ① 플레이어 반대쪽으로 급가속 이탈(도시에서 LeashDistance 가까이 가면 도시 쪽으로 꺾는다) → SnipeDistance 벌어지거나 SnipeEscapeMaxTime 뒤
    ///   ② 표적 쪽으로 급선회(선회 ×SnipeTurnBoost + 순항 속도까지 급감속)로 돌아서기 → 기수가 ±10도 안이면 ③ 기수를 표적의 앞 자리에 고정하고 곧게 날며 레일건 충전·발사(AI의 미사일 런처가 쏜다). 쏘거나 SnipeHoldTime 뒤 평소 전투.
    /// - 플레어: 일반 적 플레어 무전(남성 목소리) 대신 LUNA 전용 대사(OnFlare — FlareLineCooldown에 한 번).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_LunaAce : ACO_AceBase
    {
        public override int GetAceKind() { return KIND_LUNA; }
        public override string GetAceName() { return AceName; }
        /// UAV — 자기 체력·"UAV" 이름으로 따로 표시(전개했을 때만 켜져 있다).
        public override Transform[] GetSubUnits() { return Uavs; }
        public override SaccTarget GetSubUnitHealth(int i) { return UavSites != null && i < UavSites.Length && UavSites[i] != null ? UavSites[i].TargetHealth : null; }
        public override string GetSubUnitLabel() { return "UAV"; }

        [Header("이름 / 무전")]
        [Tooltip("HUD 마커·페이즈 배너·무전에 쓰는 이름(영문 대문자)")]
        public string AceName = "LUNA";
        public Color SpeakerColor = new Color(0.72f, 0.82f, 1f, 1f);
        [Tooltip("Bake가 넣는다")]
        public ACO_Chatter Chatter;
        public float VoiceQueueDelay = 8f;

        [Header("참조 (같은 기체 — 프리팹 빌더/Bake가 넣는다)")]
        public ACO_NPCFighterAI AI;
        public SaccTarget TargetHealth;
        [Tooltip("Bake가 넣는다 — 도시·유도·UAV 전개 차수")]
        public ACO_PhaseLuna Phase;
        [Tooltip("Bake가 넣는다 — 내 기체(레일건 경고)")]
        public ACO_RadarContacts Contacts;
        [Tooltip("Bake가 넣는다 — 레일건이 나를 겨누는 동안 RAILGUN 음성")]
        public ACO_VoiceWarning Voice;

        [Header("선회 (AI 오너)")]
        [Tooltip("도시 위 선회 고도(m, 도시 땅 높이에서)")]
        public float OrbitAltitude = 1500f;
        public float OrbitRadius = 2000f;
        [Tooltip("플레이어가 이 거리(m) 안이면 궤도를 벗어나 상대한다(레일건 사거리와 같게 — AI 탐지 거리 DetectRange는 이보다 넓게)")]
        public float EngageDistance = 5000f;
        [Tooltip("도시에서 이 거리(m, 수평)보다 멀어지면 궤도로 돌아온다")]
        public float LeashDistance = 8000f;

        [Header("저격 (AI 오너 — 교전 중 거리를 벌렸다 돌아서 레일건)")]
        [Tooltip("저격 사이 간격(초)")]
        public float SnipeInterval = 20f;
        [Tooltip("교전을 시작하고 첫 저격까지(초)")]
        public float SnipeFirstDelay = 6f;
        [Tooltip("이탈 중 최고 속도 배율(기체 비행의 MaxBoost를 넘지 않는다)")]
        public float SnipeBoost = 1.8f;
        public float SnipeAccelBoost = 3f;
        [Tooltip("가장 가까운 플레이어와 이만큼(m) 벌어지면 돌아선다")]
        public float SnipeDistance = 4000f;
        public float SnipeEscapeMaxTime = 10f;
        [Tooltip("이탈 상승각(도)")]
        public float SnipeClimbAngle = 6f;
        [Tooltip("돌아서기 급선회 배율 — 선회 G·최대 선회율·롤·피치 속도 ×(평소 7G·초당 60도 → ×3이면 21G·180도, 180도 약 2초). 돌면서 순항 속도까지 급감속(가속도 ×SnipeAccelBoost)")]
        public float SnipeTurnBoost = 3f;
        [Tooltip("돌아서기 최대 시간(초) — 기수가 표적을 ±10도 안에 잡으면 일찍 끝난다")]
        public float SnipeTurnMaxTime = 12f;
        [Tooltip("조준(충전·발사) 최대 시간(초) — 쏘면 바로 끝난다")]
        public float SnipeHoldTime = 6f;
        [Tooltip("조준 중 표적이 이 거리(m) 안으로 오면 그만두고 평소 전투")]
        public float SnipeAbortDistance = 1200f;
        [Tooltip("플레이어가 꼬리(뒤 ±60도) 이 거리(m) 안에 붙으면 간격 전이라도 저격 이탈")]
        public float TailThreatDistance = 1500f;
        [Tooltip("꼬리 위협으로 이탈할 때도 지난 저격이 끝나고 이만큼(초)은 기다린다")]
        public float SnipeTailCooldown = 8f;
        public string LineSnipe = "Stay right there. I'll make it quick... and beautiful.";
        public AudioClip ClipSnipe;

        [Header("플레어 대사 (일반 적 플레어 무전 대신)")]
        public string LineFlare = "Not yet. You don't get to touch me.";
        public AudioClip ClipFlare;
        [Tooltip("플레어 대사 사이 최소 간격(초)")]
        public float FlareLineCooldown = 25f;

        [Header("유도 레이저")]
        [Tooltip("빛줄기가 나가는 곳(기체 아래)")]
        public Transform Designator;
        public LineRenderer DesignatorBeam;
        public float DesignatorWidth = 3f;
        [Tooltip("도시 땅 높이를 재는 층")]
        public LayerMask GroundLayers = 2049;

        [Header("레일건 (AI 미사일 런처 — 빌더가 넣는다)")]
        [Tooltip("포구(조준 레이저가 나가는 곳)")]
        public Transform Muzzle;
        public LineRenderer AimLaser;
        public float AimLaserWidth = 0.5f;
        [Tooltip("조준 레이저 길이(m) — 표적·착탄 자리에서 끊지 않고 포구 방향으로 길게 뻗는다")]
        public float AimLaserLength = 6000f;
        [Tooltip("이 거리(m) 안의 플레이어를 기수가 WarnAngle 안에 겨누면 조준 레이저·RAILGUN 경고")]
        public float RailRange = 5000f;
        [Tooltip("AI 발사 원뿔(MissileFireConeAngle — 숙련도 배율을 받는다)보다 좁으면 원뿔을 쓴다(쏘기 전에 늘 경고)")]
        public float WarnAngle = 15f;
        [Tooltip("포구가 기수에서 돌 수 있는 각도(도) — 표적의 앞 자리를 겨눈다. 발사 원뿔(8도)보다 좁다: 원뿔 안이지만 이 각도 밖이면 겨누지 못한 채로도 쏜다(위협 사격 — 빗나간다)")]
        public float GimbalAngle = 5f;
        [Tooltip("레일건 탄 속도(m/s) — 앞 자리 계산(ACO_Missile_LunaRail과 같게)")]
        public float RailSlugSpeed = 4000f;
        [Tooltip("연사(런처 BurstCount)의 뒤 발일수록 표적이 이만큼(초) 더 간 자리를 겨눈다 — 진행 방향 앞으로 흩뿌린다")]
        public float BurstLeadStep = 0.6f;
        [Tooltip("연사 뒤 발에 섞는 흔들림(도)")]
        public float BurstJitter = 1.5f;
        [Tooltip("예전 조준 레이저를 그린다(6-155에서 끔 — 경고는 조종석 음성 RAILGUN WARNING)")]
        public bool ShowAimLaser = false;

        [Header("UAV")]
        [Tooltip("UAV 루트(기체의 자식 — 요격 CIWS ACO_AAGunSite·체력·락온 마커). 처음엔 꺼 둔다")]
        public Transform[] Uavs;
        public ACO_AAGunSite[] UavSites;
        [Tooltip("전개 전 기체에 달린 UAV 모양(전개하면 숨긴다)")]
        public GameObject[] DockedPods;
        [Tooltip("UAV 자리(기체 방위 기준, m) — 기체에서 약 190m, 서로 210m 넘게(요격 방어 반경 210m — 본체는 모두 덮고 다른 UAV는 못 덮는다)")]
        public Vector3[] UavSlots = new Vector3[] { new Vector3(190f, 0f, -20f), new Vector3(-190f, 0f, -20f), new Vector3(0f, 110f, -155f), new Vector3(0f, -110f, -155f) };
        [Tooltip("자리를 따라가는 빠르기")]
        public float UavFollow = 2.5f;
        [Tooltip("전개 때 기체에서 자리까지 펼쳐지는 시간(초)")]
        public float UavDeployTime = 1.5f;
        [Tooltip("LUNA가 격추된 뒤 UAV를 숨기기까지(초)")]
        public float UavHideDelay = 6f;

        [Header("대사 (등장·격추 — 유도·UAV 대사는 페이즈 대사 표)")]
        public string LineSpawn = "The city is so bright tonight. Let me make it brighter.";
        public string LineDown = "So this... is what falling feels like...";
        [Header("음성 (선택 — 위 대사와 짝, 비우면 자막만)")]
        public AudioClip ClipSpawn;
        public AudioClip ClipDown;

        private bool _started;
        private bool _wasAlive;
        private int _seenStamp;
        private int _seenWave;
        private bool _deployed;
        private float _deployStart;
        private float _hideAt = -1f;
        private Vector3[] _uavPos;
        private bool[] _uavSeenAlive;   // 이번 전개에서 이 화면이 살아 있는 걸 본 적이 있는가(늦게 들어와 이미 격추된 UAV는 숨긴다)
        private int _aimIdx = -1;
        private int _jitterFor = -1;      // 흔들림을 정한 연사 번호
        private Vector3 _jitter;          // 이번 발 흔들림(라디안, 기수 좌표 x·y)
        private Vector3 _aimPrev;
        private Vector3 _aimVel;
        private Vector3 _lastPos;
        private bool _hasLastPos;
        private bool _designOn;
        private bool _aimOn;
        private float _groundCheck;
        private Vector3 _cityGround;
        private bool _flying;   // 오너가 궤도·저격 덮어쓰기 중
        private int _snp;            // 0 = 평소, 1 = 급가속 이탈, 2 = 돌아서기, 3 = 조준(오너)
        private float _snpStart;
        private float _snpNext = -1f;
        private float _snpEnd = -100f;
        private Vector3 _snpDir;
        private int _snpTarget = -1;
        private int _snpAmmo;
        private Vector3 _snpPrev;
        private Vector3 _snpVel;
        private float _tailCheck;
        private float _nextFlareLine;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (!AI) { AI = GetComponentInParent<ACO_NPCFighterAI>(); }
            if (AI && !TargetHealth) { TargetHealth = AI.TargetHealth; }
            int n = Uavs != null ? Uavs.Length : 0;
            _uavPos = new Vector3[n];
            _uavSeenAlive = new bool[n];
            SetDesignator(false);
            SetAim(false);
            Dock();
        }

        void OnDisable()
        {
            if (!_started) { return; }
            Dock();
            _wasAlive = false;
            _seenStamp = 0;
            SetDesignator(false);
            SetAim(false);
            EndSnipe();
            ReleaseFlight();
        }

        private bool Alive() { return AI != null && AI.IsAlive() && (TargetHealth == null || TargetHealth.Health > 0f); }

        /// 살아 있는가(모든 화면 — 페이즈가 읽는다).
        public bool IsAceAlive() { return _wasAlive; }

        void Update()
        {
            if (!_started) { Start(); }
            bool alive = Alive();
            if (alive && !_wasAlive) { HandleSpawn(); }
            if (!alive && _wasAlive) { OnDown(); }
            _wasAlive = alive;

            if (_hideAt > 0f && Time.time >= _hideAt) { _hideAt = -1f; Dock(); }
            if (!alive)
            {
                SetDesignator(false);
                SetAim(false);
                if (_flying || _snp != 0) { EndSnipe(); ReleaseFlight(); }
                return;
            }
            bool live = Phase != null && Phase.IsLive();
            if (live && Phase.GetStartMs() != _seenStamp)
            {
                // 새 페이즈 — 차수를 처음부터 센다(늦게 들어온 화면은 지금 차수를 바로 따른다)
                _seenStamp = Phase.GetStartMs();
                _seenWave = 0;
                Dock();
            }
            if (live)
            {
                int wave = Phase.GetUavWave();
                if (wave > _seenWave)
                {
                    _seenWave = wave;
                    Deploy(Networking.GetServerTimeInMilliseconds() - Phase.GetUavMs() < 3000);
                }
            }
            if (Networking.IsOwner(AI.gameObject)) { OwnerFly(live); }
            else if (_flying || _snp != 0) { EndSnipe(); ReleaseFlight(); }
            UpdateDesignator(live);
            UpdateAim();
        }

        void LateUpdate()
        {
            if (!_started || AI == null) { return; }
            Vector3 p = AI.transform.position;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 vel = _hasLastPos ? (p - _lastPos) / dt : Vector3.zero;
            _lastPos = p;
            _hasLastPos = true;
            if (_deployed) { UpdateUavs(p, vel, dt); }
        }

        private void HandleSpawn()
        {
            _hideAt = -1f;
            _seenStamp = 0;
            _seenWave = 0;
            _hasLastPos = false;
            _snpNext = -1f;
            _snp = 0;
            Dock();
            Say(LineSpawn, ClipSpawn);
        }

        private void OnDown()
        {
            if (Chatter != null) { Chatter.DropQueued(AceName); }   // 밀린 대사는 버린다
            SayNow(LineDown, ClipDown);   // 격추 대사는 나오던 대사를 끊고 바로
            // UAV도 모두 터뜨린다(체력 오너가) — 몇 초 뒤 숨긴다
            if (_deployed && UavSites != null)
            {
                for (int i = 0; i < UavSites.Length; i++) { if (UavSites[i] != null && UavSites[i].IsAlive()) { UavSites[i].Silence(); } }
                _hideAt = Time.time + UavHideDelay;
            }
        }

        // ───────────── 선회 (AI 오너) ─────────────

        private void OwnerFly(bool live)
        {
            if (!live || !Phase.IsDesignating() || AI.Flight == null) { EndSnipe(); if (_flying) { ReleaseFlight(); } return; }
            Vector3 me = AI.transform.position;
            Vector3 city = Phase.GetCity();
            Vector3 flat = me - city;
            flat.y = 0f;
            float fromCity = flat.magnitude;
            if (_snp != 0) { SnipeTick(city, fromCity); return; }   // 저격은 끝까지(이탈 중 교전 거리 밖으로 나가도)
            Vector3 np;
            bool engage = NearestPlayer(out np) <= EngageDistance && fromCity <= LeashDistance;
            if (engage)
            {
                if (_flying) { ReleaseFlight(); }
                if (_snpNext < 0f) { _snpNext = Time.time + SnipeFirstDelay; }
                if (Time.time >= _snpNext) { BeginSnipe(city, fromCity); return; }
                // 꼬리 위협: 0.25초마다, 지난 저격이 끝나고 SnipeTailCooldown이 지났을 때만
                _tailCheck -= Time.deltaTime;
                if (_tailCheck <= 0f && Time.time - _snpEnd >= SnipeTailCooldown)
                {
                    _tailCheck = 0.25f;
                    if (TailThreat()) { BeginSnipe(city, fromCity); }
                }
                return;
            }
            // 궤도: 접선 + 반지름 보정 + 고도 보정(반시계)
            Vector3 radial = fromCity > 1f ? flat / fromCity : Vector3.forward;
            Vector3 tangent = Vector3.Cross(Vector3.up, radial);
            Vector3 dir = tangent + radial * Mathf.Clamp((OrbitRadius - fromCity) / Mathf.Max(OrbitRadius, 1f) * 2f, -1.5f, 1.5f);
            dir.y = Mathf.Clamp((city.y + OrbitAltitude - me.y) * 0.002f, -0.35f, 0.35f);
            _flying = true;
            AI.SteerOverride = true;
            AI.OverrideDirection = dir.normalized;
            AI.OverrideSpeed = AI.Flight.CruiseSpeed;
        }

        private void ReleaseFlight()
        {
            _flying = false;
            if (AI != null) { AI.SteerOverride = false; }
        }

        // ───────────── 저격 (AI 오너) ─────────────

        private void BeginSnipe(Vector3 city, float fromCity)
        {
            Vector3 center;
            Vector3 away;
            if (PlayerCenter(out center)) { away = AI.transform.position - center; }
            else { away = AI.transform.forward; }
            away.y = 0f;
            if (away.sqrMagnitude < 1f) { away = AI.transform.forward; away.y = 0f; }
            if (away.sqrMagnitude < 0.000001f) { away = Vector3.forward; }
            _snpDir = away.normalized;
            _snp = 1;
            _snpStart = Time.time;
            _snpTarget = -1;
            _flying = true;
            AI.SteerOverride = true;
            AI.OverrideDirection = EscapeDirection(city, fromCity);
            AI.OverrideSpeed = AI.Flight.MaxSpeed * SnipeBoost;
            AI.Flight.SpeedBoost = SnipeBoost;
            AI.Flight.AccelBoost = SnipeAccelBoost;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSnipeCall));
        }

        public void NetSnipeCall() { if (_wasAlive) { Say(LineSnipe, ClipSnipe); } }

        /// 이탈 방향: 플레이어 반대쪽 + 상승. 도시에서 LeashDistance의 70%를 넘으면 도시 쪽으로 꺾는다(궤도에서 너무 멀어지지 않게).
        private Vector3 EscapeDirection(Vector3 city, float fromCity)
        {
            Vector3 dir = _snpDir;
            float soft = LeashDistance * 0.7f;
            if (fromCity > soft)
            {
                Vector3 toCity = city - AI.transform.position;
                toCity.y = 0f;
                if (toCity.sqrMagnitude > 1f)
                {
                    float k = Mathf.Clamp01((fromCity - soft) / Mathf.Max(LeashDistance - soft, 1f));
                    dir = Vector3.Lerp(dir, toCity.normalized, k);
                    if (dir.sqrMagnitude < 0.0001f) { dir = toCity.normalized; }
                    dir = dir.normalized;
                }
            }
            return (dir + Vector3.up * Mathf.Tan(SnipeClimbAngle * Mathf.Deg2Rad)).normalized;
        }

        private void SnipeTick(Vector3 city, float fromCity)
        {
            float t = Time.time - _snpStart;
            if (_snp == 1)
            {
                AI.OverrideDirection = EscapeDirection(city, fromCity);
                Vector3 np;
                if (NearestPlayer(out np) >= SnipeDistance || t >= SnipeEscapeMaxTime || fromCity >= LeashDistance)
                {
                    // 돌아서기: 급선회(선회 ×SnipeTurnBoost) + 순항 속도까지 급감속 — 짧게 돌아 표적 쪽으로
                    _snp = 2;
                    _snpStart = Time.time;
                    AI.Flight.SpeedBoost = 1f;
                    AI.Flight.AccelBoost = SnipeAccelBoost;
                    AI.Flight.TurnBoost = SnipeTurnBoost;
                    AI.OverrideSpeed = AI.Flight.CruiseSpeed;
                }
                return;
            }
            GameObject[] tg = Targets();
            int idx = SnipeTarget(tg);
            if (idx < 0) { EndSnipe(); return; }
            Vector3 me = AI.transform.position;
            Vector3 tp = tg[idx].transform.position;
            if (_snp == 2)
            {
                Vector3 to = tp - me;
                AI.OverrideDirection = to;
                if (Vector3.Angle(AI.transform.forward, to) <= 10f || t >= SnipeTurnMaxTime)
                {
                    // 조준: 기수를 표적의 앞 자리에 고정 — AI의 미사일 런처(레일건)가 원뿔 안에서 2초 락온(충전) 뒤 쏜다
                    _snp = 3;
                    _snpStart = Time.time;
                    AI.Flight.TurnBoost = 1f;
                    AI.Flight.AccelBoost = 1f;
                    AI.OverrideSpeed = AI.Flight.MaxSpeed;
                    _snpAmmo = AI.MissileLauncher != null ? AI.MissileLauncher.GetAmmo() : 0;
                    _snpPrev = tp;
                    _snpVel = Vector3.zero;
                }
                return;
            }
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            _snpVel = Vector3.Lerp(_snpVel, (tp - _snpPrev) / dt, 0.3f);
            _snpPrev = tp;
            float dist = Vector3.Distance(me, tp);
            AI.OverrideDirection = tp + _snpVel * (dist / Mathf.Max(RailSlugSpeed, 1f)) - me;
            bool fired = AI.MissileLauncher != null && AI.MissileLauncher.GetAmmo() < _snpAmmo;
            if (fired || t >= SnipeHoldTime || dist <= SnipeAbortDistance) { EndSnipe(); }
        }

        /// 저격 표적: AI가 노리는 플레이어(레일건은 AI 표적에 쏜다), 없으면 가장 가까운 플레이어. 정하면 저격이 끝날 때까지 그대로.
        private int SnipeTarget(GameObject[] t)
        {
            if (t == null) { return -1; }
            if (_snpTarget >= 0 && _snpTarget < t.Length && IsPlayerTarget(t, _snpTarget)) { return _snpTarget; }
            int ai = AI.GetNetTargetIdx();
            if (ai >= 0 && ai < t.Length && IsPlayerTarget(t, ai)) { _snpTarget = ai; return ai; }
            Vector3 me = AI.transform.position;
            float best = 1e18f;
            int bi = -1;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsPlayerTarget(t, i)) { continue; }
                float d = (t[i].transform.position - me).sqrMagnitude;
                if (d < best) { best = d; bi = i; }
            }
            _snpTarget = bi;
            return bi;
        }

        private void EndSnipe()
        {
            if (_snp != 0)
            {
                _snpNext = Time.time + SnipeInterval;
                _snpEnd = Time.time;
            }
            _snp = 0;
            _snpTarget = -1;
            if (AI == null) { return; }
            if (_flying) { ReleaseFlight(); }
            if (AI.Flight != null)
            {
                AI.Flight.SpeedBoost = 1f;
                AI.Flight.AccelBoost = 1f;
                AI.Flight.TurnBoost = 1f;
            }
        }

        /// 플레이어가 꼬리(뒤 ±60도) TailThreatDistance 안에 있는가.
        private bool TailThreat()
        {
            GameObject[] t = Targets();
            if (t == null) { return false; }
            Vector3 me = AI.transform.position;
            Vector3 back = -AI.transform.forward;
            float r2 = TailThreatDistance * TailThreatDistance;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsPlayerTarget(t, i)) { continue; }
                Vector3 d = t[i].transform.position - me;
                if (d.sqrMagnitude <= r2 && Vector3.Angle(back, d) <= 60f) { return true; }
            }
            return false;
        }

        /// 플레이어들의 가운데(없으면 false).
        private bool PlayerCenter(out Vector3 center)
        {
            center = Vector3.zero;
            GameObject[] t = Targets();
            if (t == null) { return false; }
            int n = 0;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsPlayerTarget(t, i)) { continue; }
                center += t[i].transform.position;
                n++;
            }
            if (n == 0) { return false; }
            center /= n;
            return true;
        }

        /// 플레어를 뿌렸다(모든 화면) — 일반 적 플레어 무전 대신 전용 대사(간격을 둔다).
        public override void OnFlare()
        {
            if (!_wasAlive || Time.time < _nextFlareLine) { return; }
            _nextFlareLine = Time.time + FlareLineCooldown;
            Say(LineFlare, ClipFlare);
        }

        // ───────────── 유도 레이저 (모든 화면) ─────────────

        private void UpdateDesignator(bool live)
        {
            if (!live || !Phase.IsDesignating() || Designator == null || DesignatorBeam == null)
            {
                SetDesignator(false);
                return;
            }
            _groundCheck -= Time.deltaTime;
            if (_groundCheck <= 0f || !_designOn)
            {
                // 도시 땅 높이는 가끔만 잰다(도시는 움직이지 않는다)
                _groundCheck = 2f;
                Vector3 c = Phase.GetCity();
                RaycastHit hit;
                _cityGround = Physics.Raycast(new Vector3(c.x, c.y + 4000f, c.z), Vector3.down, out hit, 8000f, GroundLayers.value, QueryTriggerInteraction.Ignore) ? hit.point : c;
            }
            SetDesignator(true);
            // 빔 자신의 좌표로(고속에서 기체가 그 뒤에 움직여도 붙어 다닌다)
            if (DesignatorBeam.useWorldSpace) { DesignatorBeam.useWorldSpace = false; }
            DesignatorBeam.SetPosition(0, DesignatorBeam.transform.InverseTransformPoint(Designator.position));
            DesignatorBeam.SetPosition(1, DesignatorBeam.transform.InverseTransformPoint(_cityGround));
        }

        private void SetDesignator(bool on)
        {
            if (_designOn == on) { return; }
            _designOn = on;
            if (DesignatorBeam) { DesignatorBeam.enabled = on; DesignatorBeam.widthMultiplier = DesignatorWidth; }
        }

        // ───────────── 레일건 조준 경고 (모든 화면) ─────────────

        private void UpdateAim()
        {
            GameObject[] t = Targets();
            if (t == null || Muzzle == null) { SetAim(false); return; }
            Vector3 from = Muzzle.position;
            Vector3 fwd = AI.transform.forward;
            float r2 = RailRange * RailRange;
            float warnAngle = Mathf.Max(WarnAngle, AI.MissileFireConeAngle);
            SaccEntity mine = Contacts != null ? Contacts.LocalVehicle : null;
            int aimed = -1;
            bool me = false;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsPlayerTarget(t, i)) { continue; }
                Vector3 d = t[i].transform.position - from;
                if (d.sqrMagnitude > r2 || Vector3.Angle(fwd, d) > warnAngle) { continue; }
                bool mineHit = mine != null && t[i].transform.IsChildOf(mine.transform);
                if (aimed < 0 || mineHit) { aimed = i; me = mineHit; }
                if (me) { break; }
            }
            int burst = AI.MissileLauncher != null ? AI.MissileLauncher.GetPendingBurstIndex() : 0;
            if (aimed < 0)
            {
                _aimIdx = -1;
                // 연사 도중 표적을 놓치면 남은 발은 겨누던 방향 그대로(흩뿌리기 유지)
                if (burst == 0) { Muzzle.localRotation = Quaternion.identity; }
                SetAim(false);
                return;
            }
            // 표적 속도(이 화면에서 본 움직임) → 탄이 닿는 시간만큼 앞 자리를 포구가 겨눈다(기수에서 GimbalAngle 안)
            Vector3 tp = t[aimed].transform.position;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            if (aimed != _aimIdx) { _aimIdx = aimed; _aimVel = Vector3.zero; }
            else { _aimVel = Vector3.Lerp(_aimVel, (tp - _aimPrev) / dt, 0.3f); }
            _aimPrev = tp;
            float flight = Vector3.Distance(from, tp) / Mathf.Max(RailSlugSpeed, 1f);
            // 연사 뒤 발: 표적이 BurstLeadStep초씩 더 간 자리 + 작은 흔들림(발마다 한 번 정한다)
            Vector3 lead = tp + _aimVel * (flight + burst * BurstLeadStep);
            Vector3 aimDir = (lead - from).normalized;
            if (burst > 0)
            {
                if (_jitterFor != burst)
                {
                    _jitterFor = burst;
                    Vector2 r = Random.insideUnitCircle * (BurstJitter * Mathf.Deg2Rad);
                    _jitter = new Vector3(r.x, r.y, 0f);
                }
                Vector3 side = Vector3.Cross(Vector3.up, aimDir);
                side = side.sqrMagnitude > 0.000001f ? side.normalized : AI.transform.right;
                Vector3 up = Vector3.Cross(aimDir, side);
                aimDir = (aimDir + side * _jitter.x + up * _jitter.y).normalized;
            }
            else { _jitterFor = -1; }
            Vector3 dir = Vector3.RotateTowards(fwd, aimDir, GimbalAngle * Mathf.Deg2Rad, 0f);
            Muzzle.rotation = Quaternion.LookRotation(dir, AI.transform.up);
            SetAim(true);
            if (AimLaser && ShowAimLaser)
            {
                // 빔 자신의 좌표로(고속에서 기체가 그 뒤에 움직여도 포구에 붙어 다닌다). 착탄 자리에서 끊지 않고 이어진다
                if (AimLaser.useWorldSpace) { AimLaser.useWorldSpace = false; }
                AimLaser.SetPosition(0, AimLaser.transform.InverseTransformPoint(from));
                AimLaser.SetPosition(1, AimLaser.transform.InverseTransformPoint(from + dir * AimLaserLength));
            }
            if (me && Voice != null) { Voice.Warn(ACO_VoiceWarning.W_RAILGUN); }
        }

        private void SetAim(bool on)
        {
            if (_aimOn == on) { return; }
            _aimOn = on;
            if (AimLaser) { AimLaser.enabled = on && ShowAimLaser; AimLaser.widthMultiplier = AimLaserWidth; }
        }

        // ───────────── UAV (모든 화면) ─────────────

        /// UAV를 켜서 기체에서 자리로 펼친다. revive = 체력을 되살린다(전개 직후 — 늦게 들어온 화면은 생사를 동기화로 맞춘다).
        private void Deploy(bool revive)
        {
            if (Uavs == null) { return; }
            _hideAt = -1f;
            _deployed = true;
            _deployStart = Time.time;
            Vector3 p = AI != null ? AI.transform.position : transform.position;
            for (int i = 0; i < Uavs.Length; i++)
            {
                if (Uavs[i] == null) { continue; }
                _uavPos[i] = p;
                _uavSeenAlive[i] = false;
                Uavs[i].gameObject.SetActive(true);
                Uavs[i].position = p;
                ACO_AAGunSite site = UavSites != null && i < UavSites.Length ? UavSites[i] : null;
                if (site != null)
                {
                    site.SetProgramVariable("MissionRevive", revive);
                    site.AttachedSpawn();
                }
            }
            SetPods(false);
        }

        /// UAV를 끄고 기체에 단 모양을 보인다.
        private void Dock()
        {
            _deployed = false;
            if (Uavs != null)
            {
                for (int i = 0; i < Uavs.Length; i++)
                {
                    if (Uavs[i] == null) { continue; }
                    ACO_AAGunSite site = UavSites != null && i < UavSites.Length ? UavSites[i] : null;
                    if (site != null && Uavs[i].gameObject.activeSelf) { site.MissionDespawn(); }
                    Uavs[i].gameObject.SetActive(false);
                }
            }
            SetPods(true);
        }

        private void SetPods(bool on)
        {
            if (DockedPods == null) { return; }
            for (int i = 0; i < DockedPods.Length; i++) { if (DockedPods[i] != null && DockedPods[i].activeSelf != on) { DockedPods[i].SetActive(on); } }
        }

        /// 자리를 따라간다(살아 있는 UAV만 — 격추된 UAV는 그 자리에 멈춘다).
        private void UpdateUavs(Vector3 p, Vector3 vel, float dt)
        {
            Quaternion yaw = Quaternion.Euler(0f, AI.transform.eulerAngles.y, 0f);
            float spread = Mathf.Clamp01((Time.time - _deployStart) / Mathf.Max(UavDeployTime, 0.01f));
            float k = 1f - Mathf.Exp(-UavFollow * dt);
            for (int i = 0; i < Uavs.Length; i++)
            {
                Transform u = Uavs[i];
                if (u == null || !u.gameObject.activeSelf) { continue; }
                ACO_AAGunSite site = UavSites != null && i < UavSites.Length ? UavSites[i] : null;
                bool uavAlive = site == null || site.IsAlive();
                if (uavAlive) { _uavSeenAlive[i] = true; }
                else if (!_uavSeenAlive[i] && Time.time - _deployStart > 2f)
                {
                    // 늦게 들어와 이미 격추돼 있던 UAV: 격추 연출(모델 숨김)을 못 받았다 — 통째로 숨긴다
                    u.gameObject.SetActive(false);
                    continue;
                }
                if (uavAlive && _wasAlive)
                {
                    Vector3 slot = UavSlots != null && i < UavSlots.Length ? UavSlots[i] : Vector3.zero;
                    Vector3 target = p + yaw * (slot * Mathf.SmoothStep(0f, 1f, spread));
                    Vector3 cur = _uavPos[i] + vel * dt;
                    cur = Vector3.Lerp(cur, target, spread < 1f ? Mathf.Max(k, 0.25f) : k);
                    _uavPos[i] = cur;
                    u.SetPositionAndRotation(cur, yaw);
                }
                else { u.position = _uavPos[i]; }
            }
        }

        /// 살아 있는 UAV 수(전개 전이면 0).
        public int UavAliveCount()
        {
            if (!_deployed || UavSites == null) { return 0; }
            int n = 0;
            for (int i = 0; i < UavSites.Length; i++)
            {
                if (UavSites[i] != null && Uavs[i] != null && Uavs[i].gameObject.activeSelf && UavSites[i].IsAlive()) { n++; }
            }
            return n;
        }

        /// 위협이 보이는가 — 나를 노린 미사일이 missileDistance 안이거나 플레이어가 playerDistance 안(페이즈가 UAV 전개를 정한다).
        public bool ThreatNear(float missileDistance, float playerDistance)
        {
            if (AI == null) { return false; }
            if (AI.Flares != null && AI.Flares.HasIncomingWithin(missileDistance)) { return true; }
            Vector3 np;
            return NearestPlayer(out np) <= playerDistance;
        }

        // ───────────── 도우미 ─────────────

        /// 가장 가까운 플레이어까지 거리(없으면 아주 큰 값)와 위치.
        private float NearestPlayer(out Vector3 pos)
        {
            pos = Vector3.zero;
            GameObject[] t = Targets();
            float best = 1e9f;
            if (t == null || AI == null) { return best; }
            Vector3 me = AI.transform.position;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsPlayerTarget(t, i)) { continue; }
                float d = Vector3.Distance(t[i].transform.position, me);
                if (d < best) { best = d; pos = t[i].transform.position; }
            }
            return best;
        }

        private bool IsPlayerTarget(GameObject[] t, int i)
        {
            return t[i] && t[i].activeInHierarchy && ((1 << t[i].layer) & AI.TargetLayers.value) != 0;
        }

        private GameObject[] Targets() { return AI != null && AI.EntityControl != null ? AI.EntityControl.AAMTargets : null; }

        private void Say(string text, AudioClip clip)
        {
            if (Chatter == null || string.IsNullOrEmpty(text)) { return; }
            Chatter.SayAce(AceName, SpeakerColor, text, clip, VoiceQueueDelay, ACO_Chatter.CH_ENEMY);   // 잡담 급 — 밀리면 버린다(페이즈 대사만 우선)
        }

        /// 격추 대사: 나오던 무전을 끊고 바로 낸다.
        private void SayNow(string text, AudioClip clip)
        {
            if (Chatter == null || string.IsNullOrEmpty(text)) { return; }
            Chatter.SayAceNow(AceName, SpeakerColor, text, clip, ACO_Chatter.CH_ENEMY);
        }
    }
}
