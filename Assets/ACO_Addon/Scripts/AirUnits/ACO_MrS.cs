using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 태양 에이스 MR. S(NPC_MrS 안의 자식 — 설계 6-167). 몰락한 늙은 귀족이 조상의 영지를 지키러 직접 나섰다(무전 말투: 느리고 격식 있게).
    /// 기체·비행·AI·동기화는 일반 NPC 전투기(ACO_NPCFighterAI) 그대로 쓰고, 이 스크립트가 태양 이탈·반전 레일건·무전을 더한다.
    /// 미션 생성기는 일반 배치에 쓰지 않는다 — MR. S 페이즈(ACO_PhaseMrS)만 내보낸다(OBJ_MRS).
    ///
    /// - 태양 이탈(AI 오너가 정하고 모두에게 알린다 — NetSunRun): 태양 방향으로 곧게(상승각은 비행 한계까지) 급가속.
    ///   패턴 A(미사일 회피): 나를 쫓는 미사일이 SunTriggerDistance 안 → 쫓는 미사일이 모두 없어지거나 SunRunMaxTime까지.
    ///   패턴 B(유인 반격): 꼬리(뒤 ±TailAngle) LureTailDistance 안에 플레이어 → LureRunTime 고정(쫓는 플레이어를 태양 속으로 끌고 간다).
    ///   두 패턴은 기동 간격(ManeuverCooldown — 반전이 끝난 때부터)을 같이 쓰고, B는 지난 기동 뒤 LureInterval(등장 뒤 LureFirstDelay부터).
    /// - 햇빛(모든 화면 — 플레어 장치 BeginSunLure): 태양 이탈 동안 나를 쫓는 미사일의 시선(미사일 → 나)이 태양 방향과 SunGlareAngle 안이면 속아
    ///   태양 쪽으로 솟아오른다(터지지 않는다). 판정은 플레어처럼 맞는 사람 화면. 태양 이탈을 할 수 있는 동안 태양을 등지고 따라올 수 있는 미사일엔
    ///   플레어를 쓰지 않는다(SunHandled — 가까우면 마지막 플레어).
    /// - 반전(오너 — 조종 덮어쓰기): 태양 이탈이 끝나면 꼬리 ReverseTargetDistance 안의 가장 가까운 플레이어 쪽으로 선회력을 순간 크게(TurnBoost) 올려
    ///   순항 속도로 급선회해 돌아선다(일반 비행 그대로 — 동기화는 기체 움직임). A는 꼬리에 플레이어가 없으면 반전하지 않는다. B는 표적이 떨어져 나갔으면 사격 없이 돌아서기만.
    /// - 레일건(단발): 반전을 시작할 때 충전(모든 화면 — 포구 빛, 표적 화면에 RAILGUN WARNING). ChargeTime이 지나면 조준이 흐트러졌어도 바로 한 발
    ///   (RailLauncher — 곧게 나는 초고속 탄, LUNA와 같은 탄). 포구는 기수에서 GimbalAngle까지 앞 자리를 겨누고, 그 밖이면 겨누던 쪽으로 나간다(빗나갈 수 있다).
    /// - 무전: 등장(AWACS는 페이즈 시작 대사), 태양 이탈 A·B, 햇빛에 속음(이번 이탈 첫 발 — 오너 화면이 보고 알린다), 반전, 첫 레일건 뒤 AWACS, 플레어,
    ///   체력 절반(AWACS 놀람 → MR. S), 격추. 기동 대사는 서로 ChatterGap 간격.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_MrS : ACO_AceBase
    {
        public override int GetAceKind() { return KIND_MRS; }
        public override string GetAceName() { return AceName; }

        [Header("이름 / 무전")]
        [Tooltip("HUD 마커·페이즈 배너·무전에 쓰는 이름(영문 대문자)")]
        public string AceName = "MISTER.S";
        public Color SpeakerColor = new Color(1f, 0.74f, 0.32f, 1f);
        [Tooltip("Bake가 넣는다")]
        public ACO_Chatter Chatter;
        [Tooltip("조종석 음성 경고(RAILGUN WARNING) — Bake가 넣는다")]
        public ACO_VoiceWarning Voice;
        [Tooltip("내 기체를 아는 레이더 접촉 목록 — Bake가 넣는다")]
        public ACO_RadarContacts Contacts;
        [Tooltip("음성 대사가 무전 줄에서 이보다 오래(초) 밀리면 버린다")]
        public float VoiceQueueDelay = 8f;
        [Tooltip("기동 대사(태양 이탈·햇빛·반전) 사이 최소 간격(초)")]
        public float ChatterGap = 20f;
        public float FlareLineCooldown = 25f;

        public string LineSpawn = "You trespass on land my family has held for three centuries. I shall see you off it myself.";
        public string LineSunMissile = "Follow me into the light, if you dare.";
        public string LineSunLure = "The sun has always favoured my house.";
        public string LineGlare = "Your missile bows to the sun. As it should.";
        public string LineReverse = "Manners, young one. One does not shoot a gentleman in the back.";
        public string LineFlare = "Too slow, I'm afraid.";
        [Tooltip("체력 절반 아래 — AWACS 대사 뒤에")]
        public string LineHalf = "There are pilots like you in every generation. And I've felled every last one of them.";
        public string LineDown = "Ah... it seems the time has come to pass it on to the next generation.";
        [Tooltip("AWACS: 첫 레일건 사격 뒤(한 번)")]
        public string AwacsRail = "He turned on a dime! Don't chase him into the sun!";
        [Tooltip("AWACS: 체력 절반 아래(한 번 — MR. S 대사 앞)")]
        public string AwacsHalf = "He's hit, but nothing vital! He keeps turning his engines and cockpit away from our fire. He's doing damage control by flying!";
        [Header("음성 (선택 — 위 대사와 짝, 비우면 자막만)")]
        public AudioClip ClipSpawn;
        public AudioClip ClipSunMissile;
        public AudioClip ClipSunLure;
        public AudioClip ClipGlare;
        public AudioClip ClipReverse;
        public AudioClip ClipFlare;
        public AudioClip ClipHalf;
        public AudioClip ClipDown;
        public AudioClip ClipAwacsRail;
        public AudioClip ClipAwacsHalf;

        [Header("참조 (같은 기체 — 프리팹 빌더/Bake가 넣는다)")]
        public ACO_NPCFighterAI AI;
        public SaccTarget TargetHealth;
        [Tooltip("씬의 태양(디렉셔널 라이트) — Bake가 넣는다. 비면 FallbackSunDir")]
        public Light Sun;
        [Tooltip("태양이 없을 때 쓰는 태양 쪽 방향")]
        public Vector3 FallbackSunDir = new Vector3(0.35f, 0.75f, 0.55f);
        [Tooltip("레일건 런처(AI의 일반 미사일 런처와 따로 — 이 스크립트가 쏜다)")]
        public ACO_MissileLauncher RailLauncher;
        [Tooltip("레일건 포구(RailLauncher의 발사 지점) — 모든 화면이 표적의 앞 자리로 돌린다")]
        public Transform Muzzle;
        [Tooltip("충전 빛(포구 — 충전하는 동안 켠다)")]
        public ParticleSystem ChargeFx;
        [Tooltip("충전 소리(선택)")]
        public AudioSource ChargeSound;

        [Header("태양 이탈")]
        [Tooltip("패턴 A: 나를 쫓는 미사일이 이 거리(m) 안이면")]
        public float SunTriggerDistance = 2500f;
        [Tooltip("두 패턴이 같이 쓰는 기동 간격(초) — 반전이 끝난 때부터")]
        public float ManeuverCooldown = 20f;
        [Tooltip("패턴 A 최대 시간(초)")]
        public float SunRunMaxTime = 6f;
        [Tooltip("패턴 B: 꼬리 이 거리(m) 안에 플레이어가 붙으면")]
        public float LureTailDistance = 3000f;
        [Tooltip("패턴 B 간격(초) — 지난 기동 뒤")]
        public float LureInterval = 30f;
        [Tooltip("패턴 B 첫 기동: 등장 뒤(초)")]
        public float LureFirstDelay = 15f;
        [Tooltip("패턴 B 태양 이탈 시간(초)")]
        public float LureRunTime = 5f;
        [Tooltip("꼬리 판정: 뒤쪽 이 각도(도) 안")]
        public float TailAngle = 60f;
        [Tooltip("태양 이탈 속도 배율(비행 MaxBoost까지)")]
        public float SunBoost = 2.8f;
        public float SunAccelBoost = 15f;
        [Tooltip("태양 이탈 선회력 배율 — 태양 쪽으로 빨리 꺾어 곧게 내빼는 시간을 늘린다")]
        public float SunTurnBoost = 3f;
        [Tooltip("햇빛 원뿔(도): 미사일 → 나 시선이 태양 방향과 이 안이면 속는다")]
        public float SunGlareAngle = 20f;
        [Tooltip("태양 이탈을 할 수 있는 동안 이 각도(도) 안에서 따라오는 미사일엔 플레어를 쓰지 않는다")]
        public float SunHandledAngle = 90f;
        [Tooltip("그래도 이 거리(m) 안으로 오면 플레어(마지막 수단)")]
        public float LastFlareDistance = 700f;
        [Tooltip("이 고도(m — 비행 MaxAltitude에서 뺀 값) 안이면 태양 이탈을 일찍 끝낸다")]
        public float CeilingMargin = 300f;

        [Header("반전 · 레일건")]
        [Tooltip("반전 표적: 꼬리 이 거리(m) 안의 플레이어")]
        public float ReverseTargetDistance = 4000f;
        [Tooltip("반전 선회력 배율(선회·피치·롤 속도)")]
        public float ReverseTurnBoost = 4f;
        public float ReverseAccelBoost = 15f;
        [Tooltip("이 시간(초) 안에 다 못 돌면 선회력을 되돌린다")]
        public float ReverseTurnMaxTime = 1.5f;
        [Tooltip("충전 시간(초) — 반전 시작부터")]
        public float ChargeTime = 1f;
        [Tooltip("충전이 끝났는데 쏘지 못할 때(탄이 없음 등) 반전을 끝내기까지 기다리는 시간(초) — 충전이 끝나면 조준과 관계없이 쏜다")]
        public float FireWaitTime = 1f;
        [Tooltip("기수가 표적의 앞 자리 이 각도(도) 안이면 쏜다")]
        public float FireAngle = 10f;
        [Tooltip("포구가 기수에서 돌 수 있는 각도(도)")]
        public float GimbalAngle = 10f;
        [Tooltip("레일건 탄 속도(m/s) — 앞 자리 계산(탄 프리팹과 같게)")]
        public float RailSlugSpeed = 4000f;

        private bool _started;
        private bool _wasAlive;
        private bool _saidHalf;
        private bool _saidRail;
        private float _lastBanter = -100f;
        private float _nextFlareLine;

        // 오너 상태
        private int _mode;              // 0 = 평소, 1 = 태양 이탈, 2 = 반전
        private int _pattern;           // 1 = A(미사일), 2 = B(유인)
        private float _modeStart;
        private float _nextManeuver;
        private float _nextLure;
        private float _check;
        private int _glareBase;
        private bool _glareSaid;
        private int _revTargetOwner = -1;
        private Vector3 _revDir;
        private bool _revTurning;
        private bool _revFired;

        // 모든 화면 — 반전 충전·포구
        private bool _revActive;
        private float _revStart;
        private int _revTarget = -1;
        private int _revAmmo;
        private Vector3 _aimPrev;
        private Vector3 _aimVel;
        private bool _aimHas;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (!AI) { AI = GetComponentInParent<ACO_NPCFighterAI>(); }
            if (AI && !TargetHealth) { TargetHealth = AI.TargetHealth; }
            StopCharge();
        }

        void OnDisable()
        {
            if (!_started) { return; }
            OwnerReset();
            EndCharge();
            if (AI != null && AI.Flares != null) { AI.Flares.EndSunLure(); }
            _wasAlive = false;
        }

        private bool Alive() { return AI != null && AI.IsAlive() && (TargetHealth == null || TargetHealth.Health > 0f); }

        public bool IsAceAlive() { return _wasAlive; }

        void Update()
        {
            if (!_started) { Start(); }
            bool alive = Alive();
            if (alive && !_wasAlive) { HandleSpawn(); }
            if (!alive && _wasAlive) { OnDown(); }
            _wasAlive = alive;
            if (!alive) { return; }

            CheckHalf();
            if (_revActive) { StepCharge(); }
            if (Networking.IsOwner(AI.gameObject)) { OwnerTick(); }
            else if (_mode != 0) { OwnerReset(); }
        }

        private void HandleSpawn()
        {
            _saidHalf = false;
            _saidRail = false;
            _lastBanter = -100f;
            _mode = 0;
            _nextManeuver = Time.time + 5f;
            _nextLure = Time.time + LureFirstDelay;
            EndCharge();
            SayStory(LineSpawn, ClipSpawn);
        }

        private void OnDown()
        {
            if (Chatter != null) { Chatter.DropQueued(AceName); }   // 밀린 기동 대사는 버린다
            SayNow(LineDown, ClipDown);
            OwnerReset();
            EndCharge();
            if (AI != null && AI.Flares != null) { AI.Flares.EndSunLure(); }
        }

        /// 체력 절반 아래(모든 화면 — 한 번): AWACS가 놀라고 MR. S가 답한다.
        private void CheckHalf()
        {
            if (_saidHalf || TargetHealth == null || TargetHealth.FullHealth <= 0f) { return; }
            if (TargetHealth.Health > TargetHealth.FullHealth * 0.5f) { return; }
            _saidHalf = true;
            if (Chatter == null) { return; }
            Chatter.SayControl(AwacsHalf, ClipAwacsHalf, VoiceQueueDelay);
            SayStory(LineHalf, ClipHalf);
        }

        // ───────────── 오너: 기동 ─────────────

        private void OwnerTick()
        {
            if (AI.Flight == null) { return; }
            if (_mode == 1) { SunRunTick(); return; }
            if (_mode == 2) { ReverseTick(); return; }
            _check -= Time.deltaTime;
            if (_check > 0f) { return; }
            _check = 0.2f;
            bool ready = Time.time >= _nextManeuver;
            if (AI.Flares != null)
            {
                AI.Flares.SunHandled = ready;
                AI.Flares.SunHandledDir = SunDir();
                AI.Flares.SunHandledAngle = SunHandledAngle;
                AI.Flares.SunHandledMinDistance = LastFlareDistance;
            }
            if (!ready) { return; }
            if (AI.Flares != null && AI.Flares.HasIncomingWithin(SunTriggerDistance)) { BeginSunRun(1); return; }
            if (Time.time >= _nextLure && TailPlayer(LureTailDistance) >= 0) { BeginSunRun(2); }
        }

        private void BeginSunRun(int pattern)
        {
            _mode = 1;
            _pattern = pattern;
            _modeStart = Time.time;
            _glareBase = AI.Flares != null ? AI.Flares.GetSunGlareCount() : 0;
            _glareSaid = false;
            AI.SteerOverride = true;
            AI.OverrideDirection = SunDir();
            AI.OverrideSpeed = AI.Flight.MaxSpeed * SunBoost;
            AI.Flight.SpeedBoost = SunBoost;
            AI.Flight.AccelBoost = SunAccelBoost;
            AI.Flight.TurnBoost = SunTurnBoost;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSunRun), pattern);
        }

        /// 모든 화면: 태양 이탈 시작 — 햇빛을 켠다(끝은 NetSunEnd, 못 받으면 시간이 지나 꺼진다).
        [NetworkCallable]
        public void NetSunRun(int pattern)
        {
            if (!_wasAlive || AI == null) { return; }
            float time = (pattern == 1 ? SunRunMaxTime : LureRunTime) + 0.3f;
            if (AI.Flares != null) { AI.Flares.BeginSunLure(SunDir(), SunGlareAngle, time); }
            if (pattern == 1) { Banter(LineSunMissile, ClipSunMissile); }
            else { Banter(LineSunLure, ClipSunLure); }
        }

        public void NetSunEnd()
        {
            if (AI != null && AI.Flares != null) { AI.Flares.EndSunLure(); }
        }

        public void NetGlare()
        {
            if (_wasAlive) { Banter(LineGlare, ClipGlare); }
        }

        private void SunRunTick()
        {
            float t = Time.time - _modeStart;
            AI.OverrideDirection = SunDir();
            // 햇빛에 속은 첫 미사일(이 화면이 봤다) — 이번 이탈에 한 번 모두에게 알린다
            if (!_glareSaid && AI.Flares != null && AI.Flares.GetSunGlareCount() > _glareBase)
            {
                _glareSaid = true;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetGlare));
            }
            bool done;
            if (_pattern == 1) { done = t >= SunRunMaxTime || (t >= 1f && (AI.Flares == null || !AI.Flares.HasIncomingWithin(100000f))); }
            else { done = t >= LureRunTime; }
            if (AI.transform.position.y >= AI.Flight.MaxAltitude - CeilingMargin) { done = true; }
            if (!done) { return; }
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSunEnd));
            int target = TailPlayer(ReverseTargetDistance);
            if (target < 0 && _pattern == 1)
            {
                // 꼬리에 아무도 없다 — 반전 없이 평소 전투(간격은 센다)
                FinishManeuver();
                return;
            }
            BeginReverse(target);
        }

        private void BeginReverse(int target)
        {
            _mode = 2;
            _modeStart = Time.time;
            _revTargetOwner = target;
            _revTurning = true;
            _revFired = false;
            Vector3 back = -AI.transform.forward;
            back.y = 0f;
            _revDir = back.sqrMagnitude > 0.0001f ? back.normalized : -AI.transform.forward;
            if (AI.Flares != null) { AI.Flares.SunHandled = false; }   // 반전 중엔 평소 플레어
            AI.SteerOverride = true;
            AI.OverrideSpeed = AI.Flight.CruiseSpeed;
            AI.Flight.SpeedBoost = 1f;
            AI.Flight.AccelBoost = ReverseAccelBoost;
            AI.Flight.TurnBoost = ReverseTurnBoost;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetReverse), target);
        }

        private void ReverseTick()
        {
            float t = Time.time - _modeStart;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 me = AI.transform.position;
            GameObject[] tg = Targets();
            bool has = _revTargetOwner >= 0 && tg != null && _revTargetOwner < tg.Length && IsPlayerTarget(tg, _revTargetOwner);
            Vector3 want = has ? LeadPoint(tg[_revTargetOwner].transform.position, me) - me : _revDir;
            AI.OverrideDirection = want;
            float off = Vector3.Angle(AI.transform.forward, want);
            if (_revTurning && (off <= FireAngle || t >= ReverseTurnMaxTime))
            {
                _revTurning = false;
                AI.Flight.TurnBoost = 1f;
                AI.Flight.AccelBoost = 1f;
                AI.OverrideSpeed = AI.Flight.MaxSpeed;
            }
            if (!has)
            {
                // 표적이 없거나 떨어져 나갔다: 돌아서기만 하고 끝
                if (RailLauncher != null) { RailLauncher.ReleaseAim(); }
                if (!_revTurning || t >= ChargeTime + FireWaitTime) { FinishManeuver(); }
                return;
            }
            if (RailLauncher != null) { RailLauncher.AimAt(_revTargetOwner, dt); }   // 락온 경보(RWR)
            if (!_revFired && t >= ChargeTime && RailLauncher != null && RailLauncher.CanFire())   // 충전이 끝나면 조준과 관계없이 쏜다
            {
                _revFired = true;
                RailLauncher.FireAt(_revTargetOwner);
                FinishManeuver();
                return;
            }
            if (t >= ChargeTime + FireWaitTime) { FinishManeuver(); }
        }

        private void FinishManeuver()
        {
            OwnerReset();
            _nextManeuver = Time.time + ManeuverCooldown;
            _nextLure = Time.time + LureInterval;
        }

        /// 오너 기동 상태를 비운다(조종 덮어쓰기·배율을 되돌린다).
        private void OwnerReset()
        {
            if (_mode == 1 && AI != null && AI.Flares != null && Networking.IsOwner(AI.gameObject)) { SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetSunEnd)); }
            _mode = 0;
            _revTargetOwner = -1;
            if (AI == null) { return; }
            AI.SteerOverride = false;
            if (AI.Flight != null)
            {
                AI.Flight.SpeedBoost = 1f;
                AI.Flight.AccelBoost = 1f;
                AI.Flight.TurnBoost = 1f;
            }
            if (AI.Flares != null) { AI.Flares.SunHandled = false; }
            if (RailLauncher != null) { RailLauncher.ReleaseAim(); }
        }

        // ───────────── 모든 화면: 반전 충전 · 포구 ─────────────

        /// 모든 화면: 반전 시작 — 표적이 있으면 충전(포구 빛·소리, 표적 화면에 경고)하고 포구를 표적의 앞 자리로 돌린다.
        [NetworkCallable]
        public void NetReverse(int target)
        {
            if (!_wasAlive) { return; }
            Banter(LineReverse, ClipReverse);
            if (target < 0) { return; }
            _revActive = true;
            _revStart = Time.time;
            _revTarget = target;
            _revAmmo = RailLauncher != null ? RailLauncher.GetAmmo() : 0;
            _aimHas = false;
            if (ChargeFx != null) { ChargeFx.Play(); }
            if (ChargeSound != null && ChargeSound.clip != null) { ChargeSound.Play(); }
        }

        private void StepCharge()
        {
            float t = Time.time - _revStart;
            bool fired = RailLauncher != null && RailLauncher.GetAmmo() < _revAmmo;
            if (fired)
            {
                if (!_saidRail)
                {
                    _saidRail = true;
                    if (Chatter != null) { Chatter.SayControl(AwacsRail, ClipAwacsRail, VoiceQueueDelay); }
                }
                EndCharge();
                return;
            }
            if (t >= ChargeTime + FireWaitTime + 0.8f) { EndCharge(); return; }   // 쏘지 못했다(또는 이벤트를 못 받았다)
            GameObject[] tg = Targets();
            if (tg == null || _revTarget < 0 || _revTarget >= tg.Length || !IsPlayerTarget(tg, _revTarget) || Muzzle == null) { return; }
            Vector3 tp = tg[_revTarget].transform.position;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            if (_aimHas) { _aimVel = Vector3.Lerp(_aimVel, (tp - _aimPrev) / dt, 0.3f); }
            else { _aimVel = Vector3.zero; _aimHas = true; }
            _aimPrev = tp;
            Vector3 from = Muzzle.position;
            Vector3 lead = tp + _aimVel * (Vector3.Distance(from, tp) / Mathf.Max(RailSlugSpeed, 1f));
            Vector3 fwd = AI.transform.forward;
            Vector3 dir = Vector3.RotateTowards(fwd, (lead - from).normalized, GimbalAngle * Mathf.Deg2Rad, 0f);
            Muzzle.rotation = Quaternion.LookRotation(dir, AI.transform.up);
            // 내가 표적이면 조종석 경고
            SaccEntity mine = Contacts != null ? Contacts.LocalVehicle : null;
            if (Voice != null && mine != null && tg[_revTarget].transform.IsChildOf(mine.transform)) { Voice.Warn(ACO_VoiceWarning.W_RAILGUN); }
        }

        private void EndCharge()
        {
            _revActive = false;
            _revTarget = -1;
            StopCharge();
            if (Muzzle != null) { Muzzle.localRotation = Quaternion.identity; }
        }

        private void StopCharge()
        {
            if (ChargeFx != null) { ChargeFx.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            if (ChargeSound != null) { ChargeSound.Stop(); }
        }

        // ───────────── 도우미 ─────────────

        /// 태양 쪽 방향(나에서 태양으로).
        private Vector3 SunDir()
        {
            Vector3 d = Sun != null ? -Sun.transform.forward : FallbackSunDir;
            return d.sqrMagnitude > 0.000001f ? d.normalized : Vector3.up;
        }

        /// 꼬리(뒤 ±TailAngle) range 안의 가장 가까운 플레이어 번호(없으면 -1).
        private int TailPlayer(float range)
        {
            GameObject[] t = Targets();
            if (t == null) { return -1; }
            Vector3 me = AI.transform.position;
            Vector3 back = -AI.transform.forward;
            float best = range * range;
            int bi = -1;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsPlayerTarget(t, i)) { continue; }
                Vector3 d = t[i].transform.position - me;
                float sq = d.sqrMagnitude;
                if (sq <= best && Vector3.Angle(back, d) <= TailAngle) { best = sq; bi = i; }
            }
            return bi;
        }

        // 오너 화면의 표적 앞 자리(탄이 닿는 시간만큼 지금 속도로 간 자리)
        private Vector3 _leadPrev;
        private Vector3 _leadVel;
        private int _leadFor = -1;

        private Vector3 LeadPoint(Vector3 tp, Vector3 me)
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            if (_leadFor == _revTargetOwner) { _leadVel = Vector3.Lerp(_leadVel, (tp - _leadPrev) / dt, 0.3f); }
            else { _leadFor = _revTargetOwner; _leadVel = Vector3.zero; }
            _leadPrev = tp;
            return tp + _leadVel * (Vector3.Distance(me, tp) / Mathf.Max(RailSlugSpeed, 1f));
        }

        private bool IsPlayerTarget(GameObject[] t, int i)
        {
            return t[i] && t[i].activeInHierarchy && ((1 << t[i].layer) & AI.TargetLayers.value) != 0;
        }

        private GameObject[] Targets() { return AI != null && AI.EntityControl != null ? AI.EntityControl.AAMTargets : null; }

        /// 플레어(모든 화면): 전용 대사(간격).
        public override void OnFlare()
        {
            if (!_wasAlive || Time.time < _nextFlareLine) { return; }
            _nextFlareLine = Time.time + FlareLineCooldown;
            Say(LineFlare, ClipFlare);
        }

        /// 기동 대사: ChatterGap 안이면 건너뛴다.
        private void Banter(string text, AudioClip clip)
        {
            if (Time.time - _lastBanter < ChatterGap) { return; }
            _lastBanter = Time.time;
            Say(text, clip);
        }

        private void Say(string text, AudioClip clip)
        {
            if (Chatter == null || string.IsNullOrEmpty(text)) { return; }
            Chatter.SayAce(AceName, SpeakerColor, text, clip, VoiceQueueDelay, ACO_Chatter.CH_ENEMY);   // 잡담 급 — 밀리면 버린다
        }

        /// 이야기 대사(등장·절반): 잡담 급이 아니라 페이즈 대사처럼 줄에 넣는다.
        private void SayStory(string text, AudioClip clip)
        {
            if (Chatter == null || string.IsNullOrEmpty(text)) { return; }
            Chatter.SayVoice(AceName, SpeakerColor, text, clip, VoiceQueueDelay, ACO_Chatter.CH_ENEMY);
        }

        private void SayNow(string text, AudioClip clip)
        {
            if (Chatter == null || string.IsNullOrEmpty(text)) { return; }
            Chatter.SayAceNow(AceName, SpeakerColor, text, clip, ACO_Chatter.CH_ENEMY);
        }
    }
}
