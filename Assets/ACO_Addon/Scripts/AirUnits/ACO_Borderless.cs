using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 레이저·ECM 단독 에이스(NPC_Borderless 안의 자식 오브젝트). 기체·비행·AI·동기화는 일반 NPC 전투기(ACO_NPCFighterAI) 그대로 쓰고,
    /// 이 스크립트가 레이저·ECM·락온 마커·무전을 더한다. 미션 생성기는 일반 배치에 쓰지 않는다 — 레이저 에이스 페이즈(ACO_PhaseBorderless)만 내보낸다(OBJ_BORDERLESS).
    ///
    /// - 레이저: 렌즈는 포탑(ACO_AAGunSite — 폭격기 방어 기총처럼 기체에 붙인 부위, 콜라이더·락온 마커 없음)이고, 렌즈 축에 레이저 기총(ACO_NPCGun + 레이저 부품 ACO_NPCLaser)이 달려 있다.
    ///   포탑이 표적을 고르고(동기화는 포탑이 한다) 천천히 돌며(YawRate·PitchRate — 옆으로 크게 움직이면 빔이 뒤처진다) 좌우 한계(YawArc)·고각 한계 안의 표적만 노린다(기수 앞 원뿔).
    ///   빔 흔들림은 포탑의 조준 흔들림(AimError)이다. 빔·소리·피해는 기총 규칙(조준이 EffectiveAngle 안에 DwellTime 머물면 피해 — 맞는 사람 화면이 판정)이고 빔은 레이저 부품이 그린다.
    ///   이 스크립트는 기총이 나를 쏘는 동안 LASER 음성만 낸다. 포탑은 에이스가 나올 때 켜고(AttachedSpawn) 격추되면 끈다(MissionDespawn).
    /// - ECM(2단계 — 페이즈가 정한다): 같은 기체의 락온 제한(ACO_LockGate, 정면 원뿔)을 살아 있는 2단계 동안 켠다 — 에이스 기수 앞 원뿔 안에서만 락온된다. 이미 쏜 미사일은 그대로 난다.
    /// - 2단계 전 무적: 페이즈가 아직 2단계(탄도미사일)를 시작하지 않았거나 전환 연출 중이면 체력을 절반 아래로 못 내린다(체력 오너가 막는다) — 한 번에 몰아쳐 2단계를 건너뛰지 않게.
    ///   그동안은 SaccTarget의 미리 보기 격추(PredictExplosion — 쏜 사람 화면이 쌓인 피해로 먼저 터뜨린다)를 모든 화면에서 끈다:
    ///   오너가 체력을 되돌리는 걸 모르는 화면에서 에이스가 1초 터졌다 살아나지 않게. 2단계(전환 뒤)에 다시 켠다.
    /// - 락온 마커(AceLockMarker): 이름이 AAMTarget이 아니라 SAV 애니메이터가 건드리지 않는다 — 살아 있으면 켜고 격추되면 끈다.
    /// - 2단계 이탈·재진입(AI 오너만 — 조종 덮어쓰기, 동기화는 기체 움직임 그대로): 2단계가 시작되면 바로 한 번, 그 뒤 EscapeInterval마다
    ///   또는 플레이어가 꼬리(뒤 ±60도) TailThreatDistance 안에 붙으면 — 플레이어 중심 반대쪽으로 살짝 오르며 급가속(최고 속도 × EscapeBoost)해
    ///   가장 가까운 플레이어와 EscapeDistance 벌어지거나 EscapeMaxTime이 지나면, 평소 속도로 돌아 플레이어 쪽으로 기수를 맞추고(정면 재진입) 평소 전투로 돌아간다.
    ///   가속 상한은 기체 비행의 MaxBoost(프리팹 값 — 동기화 예측도 같은 값을 쓴다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_Borderless : ACO_AceBase
    {
        public override int GetAceKind() { return KIND_BORDERLESS; }
        public override string GetAceName() { return AceName; }

        [Header("이름 / 무전")]
        [Tooltip("HUD 마커·페이즈 배너·무전에 쓰는 이름(영문 대문자)")]
        public string AceName = "BORDERLESS";
        public Color SpeakerColor = new Color(1f, 0.35f, 0.3f, 1f);
        [Tooltip("Bake가 넣는다")]
        public ACO_Chatter Chatter;
        public float VoiceQueueDelay = 8f;

        [Header("참조 (같은 기체 — 프리팹 빌더/Bake가 넣는다)")]
        public ACO_NPCFighterAI AI;
        public SaccTarget TargetHealth;
        [Tooltip("Bake가 넣는다 — 단계(1단계 레이저, 2단계 ECM)")]
        public ACO_PhaseBorderless Phase;
        [Tooltip("Bake가 넣는다 — 내 기체(피해 판정)")]
        public ACO_RadarContacts Contacts;
        [Tooltip("Bake가 넣는다 — 나를 비추는 동안 LASER 음성")]
        public ACO_VoiceWarning Voice;
        [Tooltip("플레이어 미사일이 락온하는 마커(레이어 29). 살아 있으면 켜고 격추되면 끈다")]
        public GameObject LockMarker;

        [Header("레이저")]
        [Tooltip("렌즈 포탑 — 표적 고르기·회전·흔들림을 맡는다(좌우 한계 YawArc·고각 한계로 기수 앞 원뿔만 노린다). 빔·피해는 포탑의 레이저 기총")]
        public ACO_AAGunSite Turret;
        [Tooltip("내가 비춰지면 도는 경보음(선택 — 기본은 비움: 조종석 음성 LASER WARNING으로 알린다)")]
        public AudioSource WarnSound;

        [Header("ECM (2단계)")]
        [Tooltip("같은 기체의 락온 제한(정면 원뿔 — 반각은 거기서 정한다). 살아 있는 2단계 동안 켠다")]
        public ACO_LockGate LockGate;

        [Header("2단계 이탈·재진입 (AI 오너)")]
        [Tooltip("이탈 사이 간격(초) — 2단계 시작 때 바로 한 번")]
        public float EscapeInterval = 45f;
        [Tooltip("이탈 중 최고 속도 배율(기체 비행의 MaxBoost를 넘지 않는다)")]
        public float EscapeBoost = 2.2f;
        [Tooltip("이탈 중 가속도 배율")]
        public float EscapeAccelBoost = 3f;
        [Tooltip("가장 가까운 플레이어와 이만큼(m) 벌어지면 돌아선다")]
        public float EscapeDistance = 4000f;
        public float EscapeMaxTime = 12f;
        [Tooltip("이탈 상승각(도)")]
        public float EscapeClimbAngle = 8f;
        [Tooltip("돌아서는 단계 최대 시간(초) — 기수가 플레이어를 ±20도 안에 잡으면 일찍 끝난다")]
        public float TurnBackMaxTime = 15f;
        [Tooltip("플레이어가 꼬리(뒤 ±60도) 이 거리(m) 안에 붙으면 간격 전이라도 이탈한다")]
        public float TailThreatDistance = 1500f;
        [Tooltip("꼬리 위협으로 이탈할 때도 지난 이탈이 끝나고 이만큼(초)은 기다린다(꼬리에 붙은 플레이어가 이탈을 연달아 일으키지 않게)")]
        public float EscapeTailCooldown = 15f;
        public string LineEscape = "Catch me if you can still find a border to stand on.";

        [Header("대사 (등장·격추 — 2단계 대사는 페이즈 대사 표)")]
        public string LineSpawn = "Every border down there was drawn by a war. I'm here to fire the shot that erases them all.";
        public string LineDown = "You'll just draw the lines again... and fight over them... forever...";
        [Tooltip("플레어 대사(평소 플레어 — 일반 적 플레어 무전 대신, FlareLineCooldown에 한 번)")]
        public string LineFlare = "You're aiming at a line that isn't there.";
        public float FlareLineCooldown = 25f;
        [Header("음성 (선택 — 위 대사와 짝, 비우면 자막만)")]
        public AudioClip ClipSpawn;
        public AudioClip ClipEscape;
        public AudioClip ClipDown;
        public AudioClip ClipFlare;

        private bool _started;
        private bool _wasAlive;
        private float _nextFlareLine;
        private bool _warnOn;
        private bool _markerOn = true;
        private int _esc;            // 0 = 평소, 1 = 급가속 이탈, 2 = 돌아서기(오너)
        private float _escStart;
        private float _escNext;
        private bool _escStage2Seen;
        private Vector3 _escDir;
        private float _escEnd = -100f;
        private float _tailCheck;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (!AI) { AI = GetComponentInParent<ACO_NPCFighterAI>(); }
            if (AI && !TargetHealth) { TargetHealth = AI.TargetHealth; }
        }

        void OnDisable()
        {
            SetWarn(false);
            _wasAlive = false;
            if (Turret) { Turret.MissionDespawn(); }
            if (LockGate) { LockGate.Active = false; }
            EndEscape();
        }

        private bool Alive() { return AI != null && AI.IsAlive() && (TargetHealth == null || TargetHealth.Health > 0f); }

        void Update()
        {
            if (!_started) { Start(); }
            bool alive = Alive();
            if (alive && !_wasAlive) { HandleSpawn(); }
            if (!alive && _wasAlive)
            {
                if (Chatter != null) { Chatter.DropQueued(AceName); }   // 밀린 기술 대사는 버린다
                SayNow(LineDown, ClipDown);   // 격추 대사는 나오던 대사를 끊고 바로
                SetWarn(false);
                if (Turret) { Turret.MissionDespawn(); }
                EndEscape();
            }
            _wasAlive = alive;
            SetMarker(alive);
            if (LockGate) { LockGate.Active = alive && Phase != null && Phase.IsEcmOn(); }
            // 체력 하한이 걸린 동안(1단계·전환) 미리 보기 격추를 끈다
            if (TargetHealth)
            {
                bool predict = !alive || (Phase != null && Phase.IsVulnerable());
                if (TargetHealth.PredictExplosion != predict) { TargetHealth.PredictExplosion = predict; }
            }
            if (!alive) { return; }

            if (Networking.IsOwner(AI.gameObject))
            {
                OwnerHoldHealth();
                OwnerEscape();
            }
            else if (_esc != 0) { EndEscape(); }   // 오너가 바뀌었다 — 새 오너가 처음부터 정한다
            UpdateWarning();
        }

        private void HandleSpawn()
        {
            // 렌즈 포탑을 켠다(모든 화면). 포탑 체력은 쓰지 않으므로(맞지 않는다) 되살리지 않고 교전만 다시 시작한다
            if (Turret)
            {
                Turret.SetProgramVariable("MissionRevive", false);
                Turret.AttachedSpawn();
            }
            EndEscape();
            _escStage2Seen = false;
            Say(LineSpawn, ClipSpawn);
        }

        // ───────────── 2단계 이탈·재진입 (오너) ─────────────

        private void OwnerEscape()
        {
            if (Phase == null || !Phase.IsEcmOn()) { if (_esc != 0) { EndEscape(); } return; }
            if (!_escStage2Seen)
            {
                _escStage2Seen = true;
                // 2단계 시작: 바로 한 번. 2단계 도중에 오너를 넘겨받았으면 바로 이탈하지 않고 조금 뒤부터
                bool justStarted = Networking.GetServerTimeInMilliseconds() - Phase.GetStageMs() < 5000;
                _escNext = justStarted ? Time.time : Time.time + 10f;
            }
            if (_esc == 0)
            {
                if (Time.time >= _escNext) { BeginEscape(); return; }
                // 꼬리 위협: 0.25초마다 보고, 지난 이탈이 끝나고 EscapeTailCooldown이 지났을 때만
                _tailCheck -= Time.deltaTime;
                if (_tailCheck > 0f || Time.time - _escEnd < EscapeTailCooldown) { return; }
                _tailCheck = 0.25f;
                if (TailThreat()) { BeginEscape(); }
                return;
            }
            float t = Time.time - _escStart;
            if (_esc == 1)
            {
                AI.OverrideDirection = _escDir;
                Vector3 np;
                if (NearestPlayer(out np) >= EscapeDistance || t >= EscapeMaxTime)
                {
                    // 돌아서기: 가속을 풀고(천천히 줄어든다) 플레이어 쪽으로
                    _esc = 2;
                    _escStart = Time.time;
                    AI.Flight.SpeedBoost = 1f;
                    AI.Flight.AccelBoost = 1f;
                    AI.OverrideSpeed = AI.Flight.MaxSpeed;
                }
                return;
            }
            Vector3 nearest;
            if (NearestPlayer(out nearest) > 1e8f) { EndEscape(); return; }
            Vector3 to = nearest - AI.transform.position;
            AI.OverrideDirection = to;
            if (Vector3.Angle(AI.transform.forward, to) <= 20f || t >= TurnBackMaxTime) { EndEscape(); }
        }

        private void BeginEscape()
        {
            Vector3 center;
            Vector3 away;
            if (PlayerCenter(out center))
            {
                away = AI.transform.position - center;
                away.y = 0f;
            }
            else { away = AI.transform.forward; away.y = 0f; }
            if (away.sqrMagnitude < 1f) { away = AI.transform.forward; away.y = 0f; }
            if (away.sqrMagnitude < 0.000001f) { away = Vector3.forward; }
            _escDir = (away.normalized + Vector3.up * Mathf.Tan(EscapeClimbAngle * Mathf.Deg2Rad)).normalized;
            _esc = 1;
            _escStart = Time.time;
            AI.SteerOverride = true;
            AI.OverrideDirection = _escDir;
            AI.OverrideSpeed = AI.Flight.MaxSpeed * EscapeBoost;
            AI.Flight.SpeedBoost = EscapeBoost;
            AI.Flight.AccelBoost = EscapeAccelBoost;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetEscapeCall));
        }

        public void NetEscapeCall() { Say(LineEscape, ClipEscape); }

        private void EndEscape()
        {
            if (_esc != 0)
            {
                _escNext = Time.time + EscapeInterval;
                _escEnd = Time.time;
            }
            _esc = 0;
            if (AI == null) { return; }
            AI.SteerOverride = false;
            if (AI.Flight != null)
            {
                AI.Flight.SpeedBoost = 1f;
                AI.Flight.AccelBoost = 1f;
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

        /// 가장 가까운 플레이어까지 거리(없으면 아주 큰 값)와 위치.
        private float NearestPlayer(out Vector3 pos)
        {
            pos = Vector3.zero;
            GameObject[] t = Targets();
            float best = 1e9f;
            if (t == null) { return best; }
            Vector3 me = AI.transform.position;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsPlayerTarget(t, i)) { continue; }
                float d = Vector3.Distance(t[i].transform.position, me);
                if (d < best) { best = d; pos = t[i].transform.position; }
            }
            return best;
        }

        /// 10km 안 플레이어들의 중심.
        private bool PlayerCenter(out Vector3 center)
        {
            center = Vector3.zero;
            GameObject[] t = Targets();
            if (t == null) { return false; }
            Vector3 me = AI.transform.position;
            int n = 0;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsPlayerTarget(t, i)) { continue; }
                Vector3 p = t[i].transform.position;
                if ((p - me).sqrMagnitude > 1e8f) { continue; }
                center += p;
                n++;
            }
            if (n == 0) { return false; }
            center /= n;
            return true;
        }

        private bool IsPlayerTarget(GameObject[] t, int i)
        {
            return t[i] && t[i].activeInHierarchy && ((1 << t[i].layer) & AI.TargetLayers.value) != 0;
        }

        private GameObject[] Targets() { return AI != null && AI.EntityControl != null ? AI.EntityControl.AAMTargets : null; }

        // ───────────── 오너: 체력 ─────────────

        /// 2단계 시작 전(또는 전환 연출 중)엔 체력이 절반 아래로 못 내려간다. 체력 오너 화면에서만 고친다.
        private void OwnerHoldHealth()
        {
            if (TargetHealth == null || Phase == null || !Networking.IsOwner(TargetHealth.gameObject)) { return; }
            if (!Phase.IsLive() || Phase.IsVulnerable()) { return; }
            float floor = TargetHealth.FullHealth * 0.5f;
            if (TargetHealth.Health < floor)
            {
                TargetHealth.Health = floor;
                TargetHealth.SendNetworkUpdate();
            }
        }

        // ───────────── 레이저 경고 (모든 화면) ─────────────

        /// 렌즈 포탑의 레이저 기총이 나를 쏘는 동안 LASER 음성(빔·소리·피해는 기총과 레이저 부품이 한다).
        private void UpdateWarning()
        {
            ACO_NPCGun gun = Turret != null ? Turret.Gun : null;
            Transform target = Turret != null ? Turret.GetTargetTransform() : null;
            SaccEntity mine = Contacts != null ? Contacts.LocalVehicle : null;
            bool me = gun != null && target != null && mine != null && gun.IsFiring() && target.IsChildOf(mine.transform);
            SetWarn(me);
            if (me && Voice != null) { Voice.Warn(ACO_VoiceWarning.W_LASER); }
        }

        private void SetWarn(bool on)
        {
            if (_warnOn == on) { return; }
            _warnOn = on;
            if (!WarnSound) { return; }
            if (on && !WarnSound.isPlaying) { WarnSound.Play(); }
            else if (!on && WarnSound.isPlaying) { WarnSound.Stop(); }
        }

        private void SetMarker(bool on)
        {
            if (_markerOn == on || !LockMarker) { return; }
            _markerOn = on;
            LockMarker.SetActive(on);
        }

        /// 플레어(플레어 장치 NetFlare — 모든 화면): 전용 대사.
        public override void OnFlare()
        {
            if (!_wasAlive || Time.time < _nextFlareLine) { return; }
            _nextFlareLine = Time.time + FlareLineCooldown;
            Say(LineFlare, ClipFlare);
        }

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
