using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// SHADE 편대 에이스(NPC_Shade 안의 자식 — 4기가 한 편대). 기체·비행·AI·동기화는 일반 NPC 전투기(ACO_NPCFighterAI) 그대로 쓰고,
    /// 이 스크립트가 코브라·무전을 더한다. 평소엔 일반 미사일(AI 런처, 일반 발사 원뿔). 코브라 동안만 전방위 미사일 — AI 런처를 AllAspectLauncher(SHADE 콜드런치 탄
    /// ACO_Missile_SHADE — 튀어나와 표적 쪽으로 돈 뒤 점화)로, 발사 원뿔을 AllAspectCone(180도)으로 바꾼다. 교차 플레어는 ACO_CrossManeuver 편대 집결(살아 있는 편대원 전부가 한가운데로 모였다가 지나며 플레어).
    /// 미션 생성기는 일반 배치에 쓰지 않는다 — SHADE 편대 페이즈(ACO_PhaseShade)만 내보낸다(OBJ_SHADE).
    ///
    /// - 콜사인: Callsign + " " + Number(Bake가 풀 순서로 1~4를 넣는다).
    /// - 코브라(AI 오너가 정하고 모두에게 알린다 — NetCobra): 꼬리(뒤 ±CobraTailAngle) CobraEnemyDistance 안에 적(플레이어)이 있고, 나를 쫓는 미사일도
    ///   꼬리 CobraTriggerDistance 안으로 오면(CobraCooldown 간격, 교차 중 아님). 그럴 수 있는 동안만 뒤에서 오는 미사일에 플레어를 쓰지 않는다
    ///   (플레어 장치 RearHandled) — 뒤에 적이 없거나(멀리서 쏜 미사일) 앞·옆 미사일, 코브라 간격 중엔 플레어.
    ///   진행 방향은 그대로 곧게 날며 CobraSpeed까지 급감속하고(오너 — 조종 덮어쓰기·저속 허용, 동기화는 기체 움직임 그대로), 기체 모델(Model)만 기수를
    ///   CobraPitch도까지 들었다 내린다(모든 화면이 같은 곡선 — 들기 CobraRiseTime, 버티기 CobraHoldTime, 내리기 CobraFallTime). 뒤따르던 플레이어가 앞으로 튀어나간다.
    ///   히트박스(루트 BoxCollider — SAV 직격 판정 때문에 루트에서 뗄 수 없다)는 기운 모델을 감싸도록 높이·길이를 바꾼다(모든 화면 — 맞히는 사람 화면 판정과 맞는다).
    ///   코브라 동안은 플레어와 같은 효과를 받는다(CobraLure — 플레어 장치 BeginLure, 연출 없이 FlareActiveTime마다 다시 켠다): 나를 쫓던 미사일이 끌려간다.
    /// - 무전: 등장 때 1번 "SHADE 편대 교전" → 2·3·4번 차례 응답, 코브라·교차(대표 한 마디, 간격), 편대원 격추(살아 있는 가장 앞 번호가 말한다), 자기 격추.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_ShadeAce : ACO_AceBase
    {
        public override int GetAceKind() { return KIND_SHADE; }
        public override string GetAceName() { return AceName(); }

        [Header("이름 / 무전")]
        public string Callsign = "SHADE";
        [Tooltip("편대 번호 1~4(Bake가 풀 순서로 넣는다)")]
        public int Number = 1;
        public Color SpeakerColor = new Color(0.75f, 0.6f, 1f, 1f);
        [Tooltip("Bake가 넣는다")]
        public ACO_Chatter Chatter;
        [Tooltip("1번 등장")]
        public string LineCheckIn = "Shade flight, engage. Let them chase shadows.";
        [Tooltip("2~4번 등장 응답 — {word} = Two/Three/Four")]
        public string LineReply = "{word}.";
        public string LineCobra = "Overshoot. Your mistake.";
        public string LineCross = "Shades, break and cross!";
        [Tooltip("플레어 대사(평소 플레어 — 일반 적 플레어 무전 대신, FlareLineCooldown에 한 번, 편대 대사 간격 ChatterGap도 — 교차 중엔 교차 대사가 나오므로 없음)")]
        public string LineFlare = "You locked a shadow.";
        public float FlareLineCooldown = 25f;
        [Tooltip("편대원 격추 — {k} = 떨어진 번호")]
        public string LineMemberDown = "Shade {k} is down. Close it up.";
        public string LineDown = "Shade {n}... going dark.";
        [Header("음성 (선택 — 위 대사와 짝, 비우면 자막만). 번호가 들어가는 대사는 칸 0~3 = 1~4번")]
        public AudioClip ClipCheckIn;
        [Tooltip("칸 = 응답하는 번호 − 1(칸 0은 비워 둔다 — 1번은 체크인)")]
        public AudioClip[] ClipReply = new AudioClip[4];
        public AudioClip ClipCobra;
        public AudioClip ClipCross;
        public AudioClip ClipFlare;
        [Tooltip("칸 = 떨어진 번호 − 1")]
        public AudioClip[] ClipMemberDown = new AudioClip[4];
        [Tooltip("칸 = 내 번호 − 1")]
        public AudioClip[] ClipDown = new AudioClip[4];
        [Tooltip("음성 대사가 무전 줄에서 이보다 오래(초) 밀리면 버린다")]
        public float VoiceQueueDelay = 8f;
        [Tooltip("코브라·교차 대사 사이 최소 간격(초) — 편대 전체")]
        public float ChatterGap = 20f;

        [Header("참조 (같은 기체 — 프리팹 빌더/Bake가 넣는다)")]
        public ACO_NPCFighterAI AI;
        public ACO_CrossManeuver Cross;
        [Tooltip("기수를 드는 모델(격추 때 숨기는 Model)")]
        public Transform Model;
        [Tooltip("코브라 때 함께 드는 히트박스(루트 BoxCollider). 비우면 AI 루트에서 찾는다")]
        public BoxCollider Hitbox;
        [Tooltip("편대 전체(Bake가 넣는다 — 번호 순)")]
        public ACO_ShadeAce[] Squad;

        [Header("코브라")]
        [Tooltip("나를 쫓는 미사일이 꼬리 이 거리(m) 안으로 오면")]
        public float CobraTriggerDistance = 1200f;
        [Tooltip("꼬리 이 거리(m) 안에 적(플레이어)이 있어야 코브라 — 없으면 뒤에서 오는 미사일도 플레어로 피한다")]
        public float CobraEnemyDistance = 1500f;
        [Tooltip("꼬리 판정: 뒤쪽 이 각도(도) 안 — 이 안에서 오는 미사일엔 코브라를 할 수 있는 동안 플레어를 쓰지 않는다")]
        public float CobraTailAngle = 60f;
        public float CobraCooldown = 18f;
        [Tooltip("기수를 드는 최대 각(도) — 90 넘으면 뒤로 젖힌다")]
        public float CobraPitch = 110f;
        public float CobraRiseTime = 0.7f;
        public float CobraHoldTime = 0.6f;
        public float CobraFallTime = 1.0f;
        [Tooltip("코브라 중 줄이는 속도(m/s) — 기체 비행의 LowSpeedLimit보다 낮게는 못 간다")]
        public float CobraSpeed = 60f;
        [Tooltip("코브라 중 감속(가속도 배율)")]
        public float CobraDecel = 4f;
        [Tooltip("코브라 동안 플레어와 같은 효과(나를 쫓는 미사일이 끌려간다 — 연출·횟수 없음)")]
        public bool CobraLure = true;
        [Tooltip("코브라에 속은 미사일이 끌려간 점(기체 곁)에서 터지지 않고 이만큼(m) 비켜 지나가 빗나간다 — 급감속한 기체를 지나쳐 날아간다. 0 = 예전처럼 그 점에서 터짐")]
        public float CobraMissDistance = 25f;

        [Header("전방위 미사일 (코브라 동안만 — 평소엔 AI의 일반 미사일 런처)")]
        [Tooltip("코브라 동안 AI가 쓰는 런처(SHADE 콜드런치 탄 — 옆·뒤 표적으로도 돈다). 비우면 전방위 미사일 없음")]
        public ACO_MissileLauncher AllAspectLauncher;
        [Tooltip("코브라 동안 미사일 발사 원뿔(도) — 180이면 뒤쪽 표적에도 쏜다")]
        public float AllAspectCone = 180f;

        private string[] _words = { "One", "Two", "Three", "Four", "Five", "Six" };   // Udon은 static 필드를 못 쓴다

        private bool _started;
        private bool _wasAlive;
        private bool _cobra;
        private float _cobraStart;
        private float _lureAt;
        private float _tailCheck;
        private Vector3 _hitSize;
        private Vector3 _hitCenter;
        private bool _hitStored;
        private float _cobraNext;
        private bool[] _memberAlive = new bool[8];
        private float _lastBanter = -100f;
        private bool _allAspect;
        private ACO_MissileLauncher _normalLauncher;
        private float _normalCone;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (!AI) { AI = GetComponentInParent<ACO_NPCFighterAI>(); }
            if (!Hitbox && AI) { Hitbox = AI.GetComponent<BoxCollider>(); }
            if (Hitbox)
            {
                _hitSize = Hitbox.size;
                _hitCenter = Hitbox.center;
                _hitStored = true;
            }
        }

        void OnDisable()
        {
            if (AI != null && AI.Flares != null) { AI.Flares.RearHandled = false; }
            EndCobra();
            _wasAlive = false;
        }

        public string AceName() { return Callsign + " " + Number; }

        public bool IsAceAlive() { return _wasAlive; }

        private bool Alive() { return AI != null && AI.IsAlive() && (AI.TargetHealth == null || AI.TargetHealth.Health > 0f); }

        void Update()
        {
            if (!_started) { Start(); }
            bool alive = Alive();
            if (alive && !_wasAlive) { HandleSpawn(); }
            if (!alive && _wasAlive)
            {
                if (Chatter != null) { Chatter.DropQueued(AceName()); }   // 밀린 기술 대사는 버린다
                SayNow(LineDown.Replace("{n}", Number.ToString()), ClipAt(ClipDown, Number));   // 격추 대사는 나오던 대사를 끊고 바로
                EndCobra();
            }
            _wasAlive = alive;
            WatchSquad();
            if (!alive) { return; }

            if (_cobra) { StepCobra(); }
            else if (Networking.IsOwner(AI.gameObject)) { OwnerCobraCheck(); }
        }

        private void HandleSpawn()
        {
            EndCobra();
            _cobraNext = Time.time + 5f;
            // 체크인: 1번이 먼저, 2·3·4번이 차례로(무전 줄에 순서대로 들어가게 조금씩 늦춘다)
            if (Number <= 1) { Say(LineCheckIn, ClipCheckIn); }
            else { SendCustomEventDelayedSeconds(nameof(_SayReply), 0.3f * Number); }
        }

        public void _SayReply()
        {
            if (!_wasAlive) { return; }
            string word = Number >= 1 && Number <= _words.Length ? _words[Number - 1] : Number.ToString();
            Say(LineReply.Replace("{word}", word), ClipAt(ClipReply, Number));
        }

        // ───────────── 코브라 ─────────────

        private void OwnerCobraCheck()
        {
            _tailCheck -= Time.deltaTime;   // 꼬리는 0.2초마다만 본다
            if (_tailCheck > 0f) { return; }
            _tailCheck = 0.2f;
            bool ready = Time.time >= _cobraNext && !(Cross != null && Cross.IsCrossing()) && EnemyBehind();
            // 코브라를 할 수 있는 동안(뒤에 적이 있고 간격이 끝남)만 뒤에서 오는 미사일을 코브라가 맡는다 — 아니면 플레어
            if (AI.Flares != null)
            {
                AI.Flares.RearHandled = ready;
                AI.Flares.RearHandledAngle = CobraTailAngle;
            }
            if (!ready) { return; }
            if (AI.Flares == null || !AI.Flares.HasIncomingBehind(CobraTriggerDistance, CobraTailAngle)) { return; }
            _cobraNext = Time.time + CobraCooldown;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetCobra));
        }

        /// 모든 화면: 코브라 시작 — 모델은 모든 화면이 같은 곡선으로 들고, 감속은 오너가 한다.
        public void NetCobra()
        {
            if (!_wasAlive) { return; }
            _cobra = true;
            _cobraStart = Time.time;
            _lureAt = -100f;
            if (Cross != null) { Cross.Allowed = false; }
            BeginAllAspect();
            if (Time.time - SquadLastBanter() >= ChatterGap) { MarkBanter(); Say(LineCobra, ClipCobra); }
        }

        private void StepCobra()
        {
            float t = Time.time - _cobraStart;
            float total = CobraRiseTime + CobraHoldTime + CobraFallTime;
            float pitch;
            if (t < CobraRiseTime) { pitch = Mathf.SmoothStep(0f, CobraPitch, t / Mathf.Max(CobraRiseTime, 0.01f)); }
            else if (t < CobraRiseTime + CobraHoldTime) { pitch = CobraPitch; }
            else { pitch = Mathf.SmoothStep(CobraPitch, 0f, (t - CobraRiseTime - CobraHoldTime) / Mathf.Max(CobraFallTime, 0.01f)); }
            if (Model != null) { Model.localRotation = Quaternion.Euler(-pitch, 0f, 0f); }
            PitchHitbox(pitch);
            // 코브라 동안 플레어 효과(모든 화면 — 활성 시간이 끝나기 전에 다시 켠다)
            if (CobraLure && AI.Flares != null && Time.time >= _lureAt)
            {
                if (CobraMissDistance > 0f) { AI.Flares.BeginLureMiss(CobraMissDistance); }
                else { AI.Flares.BeginLure(); }
                _lureAt = Time.time + Mathf.Max(AI.Flares.FlareActiveTime - 0.2f, 0.3f);
            }

            if (Networking.IsOwner(AI.gameObject))
            {
                Vector3 flat = AI.transform.forward;
                flat.y = 0f;
                if (flat.sqrMagnitude < 0.000001f) { flat = AI.transform.forward; }
                AI.SteerOverride = true;
                AI.OverrideDirection = flat.normalized;
                AI.OverrideSpeed = CobraSpeed;
                if (AI.Flight != null)
                {
                    AI.Flight.AllowLowSpeed = true;
                    AI.Flight.AccelBoost = t < CobraRiseTime + CobraHoldTime ? CobraDecel : 1f;
                }
            }
            if (t >= total) { EndCobra(); }
        }

        private void EndCobra()
        {
            if (Model != null) { Model.localRotation = Quaternion.identity; }
            if (_hitStored && Hitbox != null)
            {
                Hitbox.size = _hitSize;
                Hitbox.center = _hitCenter;
            }
            if (_cobra && AI != null)
            {
                AI.SteerOverride = false;
                if (AI.Flight != null)
                {
                    AI.Flight.AllowLowSpeed = false;
                    AI.Flight.AccelBoost = 1f;
                }
            }
            _cobra = false;
            if (Cross != null) { Cross.Allowed = true; }
            EndAllAspect();
        }

        /// 코브라 동안 AI 미사일을 전방위 런처·넓은 원뿔로 바꾼다(모든 화면 — 쏘는 판단은 오너 AI만 한다).
        private void BeginAllAspect()
        {
            if (_allAspect || AI == null || AllAspectLauncher == null) { return; }
            _allAspect = true;
            _normalLauncher = AI.MissileLauncher;
            _normalCone = AI.MissileFireConeAngle;
            if (_normalLauncher != null) { _normalLauncher.ReleaseAim(); }
            AI.MissileLauncher = AllAspectLauncher;
            AI.MissileFireConeAngle = AllAspectCone;
        }

        private void EndAllAspect()
        {
            if (!_allAspect) { return; }
            _allAspect = false;
            if (AI == null) { return; }
            if (AllAspectLauncher != null) { AllAspectLauncher.ReleaseAim(); }
            AI.MissileLauncher = _normalLauncher;
            AI.MissileFireConeAngle = _normalCone;
        }

        /// 히트박스를 기운 모델에 맞춘다: 기체 축(길이 L·높이 H)을 pitch만큼 돌린 상자를 감싸는 축 정렬 상자(가운데는 그대로 — 모델이 가운데를 축으로 돈다).
        private void PitchHitbox(float pitch)
        {
            if (!_hitStored || Hitbox == null) { return; }
            float r = pitch * Mathf.Deg2Rad;
            float s = Mathf.Abs(Mathf.Sin(r));
            float c = Mathf.Abs(Mathf.Cos(r));
            float L = _hitSize.z;
            float H = _hitSize.y;
            Vector3 center = _hitCenter;
            // 가운데가 원점에서 떨어져 있으면 같이 돈다(위 +Y, 앞 +Z — 기수를 들면 앞이 위로)
            float cy = _hitCenter.y * Mathf.Cos(r) + _hitCenter.z * Mathf.Sin(r);
            float cz = -_hitCenter.y * Mathf.Sin(r) + _hitCenter.z * Mathf.Cos(r);
            Hitbox.center = new Vector3(center.x, cy, cz);
            Hitbox.size = new Vector3(_hitSize.x, s * L + c * H, c * L + s * H);
        }

        /// 꼬리(뒤 ±CobraTailAngle) CobraEnemyDistance 안에 적(플레이어 기체)이 있는가.
        private bool EnemyBehind()
        {
            SaccEntity e = AI.EntityControl;
            GameObject[] t = e != null ? e.AAMTargets : null;
            if (t == null) { return false; }
            Vector3 me = AI.transform.position;
            Vector3 back = -AI.transform.forward;
            float r2 = CobraEnemyDistance * CobraEnemyDistance;
            int mask = AI.TargetLayers.value;
            for (int i = 0; i < t.Length; i++)
            {
                if (!t[i] || !t[i].activeInHierarchy || ((1 << t[i].layer) & mask) == 0) { continue; }
                Vector3 d = t[i].transform.position - me;
                if (d.sqrMagnitude <= r2 && Vector3.Angle(back, d) <= CobraTailAngle) { return true; }
            }
            return false;
        }

        // ───────────── 교차 (ACO_CrossManeuver가 부른다 — 모든 화면) ─────────────

        public void OnCrossStart()
        {
            if (Cross != null && !Cross.IsLeadNow()) { return; }   // 집결의 첫 참가 기체만 말한다
            if (Time.time - SquadLastBanter() >= ChatterGap) { MarkBanter(); Say(LineCross, ClipCross); }
        }

        public void OnCrossPass() { }

        // ───────────── 편대 ─────────────

        /// 편대원이 떨어지면 살아 있는 가장 앞 번호가 알린다(모든 화면이 같은 순간을 본다).
        private void WatchSquad()
        {
            if (Squad == null) { return; }
            int n = Mathf.Min(Squad.Length, _memberAlive.Length);
            for (int i = 0; i < n; i++)
            {
                ACO_ShadeAce m = Squad[i];
                if (m == null) { continue; }
                bool a = m.IsAceAlive();
                if (!a && _memberAlive[i] && m != this && _wasAlive && IsSpeaker())
                {
                    Say(LineMemberDown.Replace("{k}", m.Number.ToString()), ClipAt(ClipMemberDown, m.Number));
                }
                _memberAlive[i] = a;
            }
        }

        /// 살아 있는 편대원 중 가장 앞 번호인가.
        private bool IsSpeaker()
        {
            for (int i = 0; i < Squad.Length; i++)
            {
                ACO_ShadeAce m = Squad[i];
                if (m == null || !m.IsAceAlive()) { continue; }
                return m == this;
            }
            return false;
        }

        // 코브라·교차 대사는 편대 전체로 간격을 둔다(넷이 한꺼번에 떠들지 않게)
        public float GetLastBanter() { return _lastBanter; }

        private float SquadLastBanter()
        {
            float last = _lastBanter;
            if (Squad == null) { return last; }
            for (int i = 0; i < Squad.Length; i++) { if (Squad[i] != null) { last = Mathf.Max(last, Squad[i].GetLastBanter()); } }
            return last;
        }

        private void MarkBanter() { _lastBanter = Time.time; }

        private float _nextFlareLine;

        /// 평소 플레어(플레어 장치 NetFlare — 모든 화면): 전용 대사. 교차 중이면 교차 대사가, 코브라는 플레어가 아니라 없다. 편대 전체 간격도 지킨다.
        public override void OnFlare()
        {
            if (!_wasAlive || (Cross != null && Cross.IsCrossing()) || Time.time < _nextFlareLine) { return; }
            if (Time.time - SquadLastBanter() < ChatterGap) { return; }
            _nextFlareLine = Time.time + FlareLineCooldown;
            MarkBanter();
            Say(LineFlare, ClipFlare);
        }

        private void Say(string text, AudioClip clip)
        {
            if (Chatter == null || string.IsNullOrEmpty(text)) { return; }
            Chatter.SayAce(AceName(), SpeakerColor, text, clip, VoiceQueueDelay, ACO_Chatter.CH_ENEMY);   // 잡담 급 — 밀리면 버린다(페이즈 대사만 우선)
        }

        /// 격추 대사: 나오던 무전을 끊고 바로 낸다.
        private void SayNow(string text, AudioClip clip)
        {
            if (Chatter == null || string.IsNullOrEmpty(text)) { return; }
            Chatter.SayAceNow(AceName(), SpeakerColor, text, clip, ACO_Chatter.CH_ENEMY);
        }

        /// 번호(1~4)별 음성 칸.
        private AudioClip ClipAt(AudioClip[] clips, int number)
        {
            return clips != null && number >= 1 && number <= clips.Length ? clips[number - 1] : null;
        }
    }
}
