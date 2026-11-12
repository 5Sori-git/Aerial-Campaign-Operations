using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 플레어 장치(동기화 없음 — 네트워크 이벤트만). NPC 전투기·수송기·폭격기가 같이 쓰는 대응책 모듈:
    /// 나를 쫓는 미사일(ACO_Missile이 RegisterIncoming)이 FlareReactDistance × ReactDistanceMultiplier 안으로 오면 미사일마다 한 번 FlareChance로 굴려 뿌리고
    /// (오너 판단 → 모두에게 NetFlare), 켜져 있는 FlareActiveTime 동안 나를 쫓는 미사일은 100% 속아 떨어지는 플레어 쪽으로 끌려간다(에이스컴뱃 규칙, 판정은 ACO_Missile).
    /// 미사일이 표적 마커에서 GetComponentInParent로 찾으므로 표적 유닛의 루트(마커의 부모 쪽)에 둔다.
    /// 등급이 있는 유닛(NPC 전투기)은 AI가 FlareChance·ReactDistanceMultiplier를 등급에 맞게 넣는다. 출격할 때 유닛이 ResetSalvos로 횟수를 채운다.
    /// 연막(SmokeMode, 지상·해상 호위 유닛): 같은 규칙으로 뿌리되 미사일이 끌려가는 지점이 뿌린 자리에 머물고(연막 구름), 켜져 있는 동안(FlareActiveTime —
    /// 연막은 6~8초 권장) 적 NPC 기총·미사일 런처가 이 유닛을 조준하지 못한다(IsObscured — 사격·락온을 멈춘다). FlareParticles에 연막 파티클을 넣는다.
    /// 연막은 적 NPC 기총이 조준하기 시작할 때도(ReportGunThreat — 기총이 표적 오너 화면에서 부른다) GunSmokeChance로 굴린다.
    /// Friendly: 아군 유닛(호위 대상)이면 켠다(EscortUnit이 출발할 때 넣는다) — 무전 대사가 적 플레어가 아니라 아군 플레어/연막이 된다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_FlareDispenser : UdonSharpBehaviour
    {
        [Header("유닛")]
        [Tooltip("체력(죽으면 뿌리지 않는다). 비우면 같은 오브젝트에서 찾는다")]
        public SaccTarget TargetHealth;
        [Tooltip("속도를 읽을 NPC 비행(전투기). 플레어가 기체 속도 일부를 이어받는다")]
        public ACO_NPCFlightController Flight;
        [Tooltip("속도를 읽을 항로 비행 유닛(수송기·폭격기). Flight가 있으면 Flight를 쓴다")]
        public ACO_EscortUnit Unit;

        [Header("규칙")]
        [Tooltip("출격당 뿌릴 수 있는 횟수")]
        public int FlareSalvos = 6;
        [Tooltip("플레어가 켜져 있는 시간(초). 이 동안 나를 쫓는 미사일(새로 쏜 것 포함)은 표적을 놓치고 플레어 쪽으로 끌려간다")]
        public float FlareActiveTime = 2.5f;
        [Tooltip("나를 쫓는 미사일이 이 거리(m) × ReactDistanceMultiplier 안이면 뿌릴지 정한다(미사일당 한 번)")]
        public float FlareReactDistance = 1500f;
        [Tooltip("미사일 하나당 뿌릴 확률. NPC 전투기는 AI가 등급별 값으로 덮어쓴다")]
        [Range(0f, 1f)] public float FlareChance = 0.5f;
        [Tooltip("반응 거리 배율. NPC 전투기는 AI가 등급별로 넣는다(신병 0.7, 에이스 1.3)")]
        public float ReactDistanceMultiplier = 1f;
        [Tooltip("두 번 뿌리는 사이 최소 간격(초)")]
        public float FlareCooldown = 3f;
        [Tooltip("플레어가 떨어지는 중력 배수(미사일이 끌려가는 지점 = 뿌린 자리에서 기체 속도 일부를 이어받아 떨어지는 점). 연출 파티클과 맞춘다")]
        public float FlareGravity = 0.35f;

        [Tooltip("연막: 미사일이 끌려가는 지점이 뿌린 자리에 머물고, 켜져 있는 동안 적 NPC가 조준하지 못한다(지상·해상 호위 유닛)")]
        public bool SmokeMode = false;
        [Tooltip("연막: 적 NPC 기총이 나를 조준하기 시작할 때(조준 한 번에 한 번) 연막을 뿌릴 확률. 횟수·간격은 미사일과 같이 쓴다")]
        [Range(0f, 1f)] public float GunSmokeChance = 0.5f;
        [Tooltip("아군 유닛(호위 대상). 호위 유닛이 출발할 때 넣는다 — 무전 대사 구분용")]
        public bool Friendly = false;
        /// 보스 에이스(AI가 Init에서 넣는다 — 모든 화면). 있으면 일반 적 플레어 무전 대신 에이스의 OnFlare(전용 대사 또는 없음).
        [System.NonSerialized] public ACO_AceBase Ace;

        [Header("연출")]
        public ParticleSystem FlareParticles;
        [Tooltip("끄면 뿌릴 때 FlareParticleCount개를 한 번에 방출한다. 켜면 파티클 시스템의 Emission을 FlareEmitDuration 동안 켰다 끈다 — "
            + "양·간격은 파티클 시스템의 Rate/Bursts 설정(간격을 두고 뿌리려면 Bursts에 간격을 넣는다)")]
        public bool FlareUseEmitter = false;
        [Tooltip("한 번에 방출할 수(FlareUseEmitter 끔)")]
        public int FlareParticleCount = 8;
        [Tooltip("이미터를 켜 두는 시간(초, FlareUseEmitter 켬)")]
        public float FlareEmitDuration = 1f;
        public AudioSource FlareSound;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다(적 플레어 대사)")]
        [UnityEngine.Serialization.FormerlySerializedAs("Radio")]
        public ACO_Chatter Chatter;

        private const int MAX_INCOMING = 8;
        /// 주인 스크립트(SHADE 코브라)가 뒤에서 오는 미사일을 맡는다 — 켜져 있으면 꼬리(뒤 ±RearHandledAngle)에서 오는 미사일엔 플레어로 반응하지 않는다(오너가 매번 넣는다).
        [System.NonSerialized] public bool RearHandled;
        [System.NonSerialized] public float RearHandledAngle = 60f;
        // 태양 이탈(MR. S — 6-167): 오너 에이스가 태양 이탈을 할 수 있는 동안 넣는다. 태양을 등지고 따라올 수 있는 미사일
        // (미사일 → 나 시선이 태양 방향과 SunHandledAngle 안)은 SunHandledMinDistance보다 멀면 플레어를 굴리지 않고 둔다(가까우면 마지막 플레어).
        [System.NonSerialized] public bool SunHandled;
        [System.NonSerialized] public Vector3 SunHandledDir = Vector3.up;
        [System.NonSerialized] public float SunHandledAngle = 90f;
        [System.NonSerialized] public float SunHandledMinDistance = 700f;

        private ACO_Missile[] _incoming = new ACO_Missile[MAX_INCOMING];
        private bool[] _decided = new bool[MAX_INCOMING];   // 그 미사일에 대응할지 이미 정했다(미사일당 한 번만 굴린다)
        private int _salvosLeft;
        private float _nextFlare;
        private float _flareStart = -100f;
        private float _lureMiss;   // 0 = 속은 미사일이 플레어 점에 닿아 터진다. > 0 = 그만큼 비켜 지나간다(SHADE 코브라)
        private Vector3 _flarePos;
        private Vector3 _flareVel;
        private float _checkTimer;
        private float _emitStopAt;
        private bool _started;
        private bool _gunThreat;   // 적 기총이 조준하기 시작했다(오너 — 다음 확인 때 굴린다)

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            if (!Flight) { Flight = GetComponent<ACO_NPCFlightController>(); }
            if (!Unit) { Unit = GetComponent<ACO_EscortUnit>(); }
            // 이미터 방식은 평소엔 방출을 꺼 둔다(뿌릴 때만 켠다).
            if (FlareParticles && FlareUseEmitter)
            {
                ParticleSystem.EmissionModule em = FlareParticles.emission;
                em.enabled = false;
            }
            // 횟수는 모든 클라이언트가 같이 센다(NetFlare가 모두에게서 줄인다) — 소유권이 넘어가도 이어진다.
            _salvosLeft = FlareSalvos;
        }

        /// 출격(부활)할 때 유닛이 부른다.
        public void ResetSalvos()
        {
            Start();
            _salvosLeft = FlareSalvos;
            _nextFlare = 0f;
            _flareStart = -100f;
            _sunUntil = -100f;
            for (int i = 0; i < MAX_INCOMING; i++) { _decided[i] = false; }
        }

        // ───────────── 햇빛(MR. S 태양 이탈 — 모든 화면) ─────────────
        private float _sunUntil = -100f;
        private Vector3 _sunDir = Vector3.up;
        private float _sunCos = 1f;
        private int _sunGlared;

        /// 모든 화면: duration초 동안 햇빛을 켠다 — 나를 쫓는 미사일의 시선(미사일 → 나)이 태양 방향(sunDir — 나에서 태양 쪽)과 halfAngle도 안이면 속는다.
        public void BeginSunLure(Vector3 sunDir, float halfAngle, float duration)
        {
            if (TargetHealth != null && TargetHealth.Health <= 0f) { return; }
            _sunDir = sunDir.sqrMagnitude > 0.000001f ? sunDir.normalized : Vector3.up;
            _sunCos = Mathf.Cos(Mathf.Clamp(halfAngle, 0f, 90f) * Mathf.Deg2Rad);
            _sunUntil = Time.time + Mathf.Max(duration, 0f);
        }

        public void EndSunLure() { _sunUntil = -100f; }

        public bool IsSunLureActive() { return Time.time < _sunUntil; }

        /// 미사일이 매 프레임 읽는다: 햇빛이 켜져 있고 이 미사일에서 본 내가 태양 원뿔 안인가.
        public bool IsSunGlare(Vector3 missilePos)
        {
            if (Time.time >= _sunUntil) { return false; }
            Vector3 los = transform.position - missilePos;
            float m = los.magnitude;
            if (m < 1f) { return false; }
            return Vector3.Dot(los / m, _sunDir) >= _sunCos;
        }

        /// 햇빛에 속은 미사일이 날아갈 방향(태양 쪽).
        public Vector3 GetSunDir() { return _sunDir; }

        /// 미사일이 햇빛에 속으면 알린다(이 화면에서 센 수 — 오너 에이스가 대사를 정한다).
        public void ReportSunGlare() { _sunGlared++; }
        public int GetSunGlareCount() { return _sunGlared; }

        /// 미사일이 이 유닛을 표적으로 발사될 때 부른다(모든 클라이언트). 판단은 오너만 하지만 등록은 어디서나 한다(오너가 바뀌어도 이어지게).
        public void RegisterIncoming(ACO_Missile missile)
        {
            if (missile == null) { return; }
            int free = -1;
            for (int i = 0; i < MAX_INCOMING; i++)
            {
                if (_incoming[i] == missile) { return; }
                if (free < 0 && _incoming[i] == null) { free = i; }
            }
            if (free < 0) { return; }
            _incoming[free] = missile;
            _decided[free] = false;
        }

        /// 미사일이 표적을 놓치거나 터지거나 회수될 때 부른다.
        public void UnregisterIncoming(ACO_Missile missile)
        {
            for (int i = 0; i < MAX_INCOMING; i++)
            {
                if (_incoming[i] == missile) { _incoming[i] = null; _decided[i] = false; }
            }
        }

        /// 나를 쫓는 미사일(등록된 것) 중 distance(m) 안에 있는 게 있는지 — 슈퍼에이스 기만체가 본다.
        public bool HasIncomingWithin(float distance)
        {
            float sq = distance * distance;
            for (int i = 0; i < MAX_INCOMING; i++)
            {
                ACO_Missile m = _incoming[i];
                if (m == null || !m.gameObject.activeInHierarchy) { continue; }
                if ((m.transform.position - transform.position).sqrMagnitude <= sq) { return true; }
            }
            return false;
        }

        /// 나를 쫓는 미사일 중 꼬리(뒤 ±halfAngle) distance(m) 안에 있는 게 있는지 — SHADE 코브라가 본다.
        public bool HasIncomingBehind(float distance, float halfAngle)
        {
            float sq = distance * distance;
            Vector3 me = transform.position;
            Vector3 back = -transform.forward;
            for (int i = 0; i < MAX_INCOMING; i++)
            {
                ACO_Missile m = _incoming[i];
                if (m == null || !m.gameObject.activeInHierarchy || !m.IsTracking()) { continue; }
                Vector3 d = m.transform.position - me;
                if (d.sqrMagnitude <= sq && Vector3.Angle(back, d) <= halfAngle) { return true; }
            }
            return false;
        }

        /// 미사일이 매 프레임 읽는다: 켜져 있으면 나를 쫓는 미사일은 표적을 놓치고 플레어로 끌려간다.
        public bool IsFlareActive()
        {
            float t = Time.time - _flareStart;
            return t >= 0f && t < FlareActiveTime;
        }

        /// 속은 미사일이 쫓는 지점: 뿌린 자리에서 기체 속도 일부로 날며 FlareGravity로 떨어지는 점.
        public Vector3 GetFlarePosition()
        {
            if (SmokeMode) { return _flarePos; }   // 연막 구름은 제자리
            float t = Mathf.Max(Time.time - _flareStart, 0f);
            return _flarePos + _flareVel * t + Physics.gravity * (0.5f * FlareGravity * t * t);
        }

        /// 연막이 켜져 있다 — 적 NPC 기총·미사일 런처가 이 유닛을 조준하지 않는다.
        public bool IsObscured() { return SmokeMode && IsFlareActive(); }

        /// 적 NPC 기총이 나를 조준하기 시작했다(ACO_NPCGun — 표적 오너 화면에서만, 조준 한 번에 한 번). 연막 유닛만, 뿌릴 수 있을 때만 받아 둔다.
        public void ReportGunThreat()
        {
            if (!SmokeMode || _salvosLeft <= 0 || Time.time < _nextFlare || IsFlareActive()) { return; }
            _gunThreat = true;
        }

        /// 오너: 나를 쫓는 미사일이 반응 거리 안으로 들어오면 미사일마다 한 번 확률을 굴려 뿌린다.
        void Update()
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f) { return; }
            _checkTimer = 0.2f;
            if (!_started) { Start(); }
            if (!Networking.IsOwner(gameObject)) { _gunThreat = false; return; }
            if (_salvosLeft <= 0 || Time.time < _nextFlare) { _gunThreat = false; return; }
            if (TargetHealth != null && TargetHealth.Health <= 0f) { _gunThreat = false; return; }

            // 연막: 적 기총이 조준하기 시작했으면 한 번 굴린다
            if (_gunThreat)
            {
                _gunThreat = false;
                if (Random.value <= GunSmokeChance)
                {
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetFlare));
                    _nextFlare = Time.time + FlareCooldown;
                    return;
                }
            }

            float react = FlareReactDistance * ReactDistanceMultiplier;
            float reactSq = react * react;
            Vector3 me = transform.position;
            for (int i = 0; i < MAX_INCOMING; i++)
            {
                ACO_Missile m = _incoming[i];
                if (m == null) { continue; }
                if (!m.IsTracking()) { _incoming[i] = null; _decided[i] = false; continue; }
                if (_decided[i]) { continue; }
                if ((m.transform.position - me).sqrMagnitude > reactSq) { continue; }
                // 뒤에서 오는 미사일을 주인 스크립트가 맡으면(코브라 준비됨) 굴리지 않고 둔다 — 못 맡게 되면 다음 확인 때 굴린다
                if (RearHandled && Vector3.Angle(-transform.forward, m.transform.position - me) <= RearHandledAngle) { continue; }
                // 태양 이탈이 맡는 미사일(태양을 등지고 따라오는, 아직 먼 것)도 같다
                if (SunHandled && (m.transform.position - me).sqrMagnitude > SunHandledMinDistance * SunHandledMinDistance
                    && Vector3.Angle(me - m.transform.position, SunHandledDir) <= SunHandledAngle) { continue; }
                _decided[i] = true;
                if (Random.value > FlareChance) { continue; }
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetFlare));
                _nextFlare = Time.time + FlareCooldown;
                return;
            }
        }

        /// 모든 클라이언트: 플레어를 뿌린다(연출 + 활성 시간 시작). 미사일은 각 클라이언트에서 따로 날지만
        /// 명중 판정은 정해진 한 화면이라, 그 화면에서 플레어가 켜져 있으면 된다.
        public void NetFlare()
        {
            if (TargetHealth != null && TargetHealth.Health <= 0f) { return; }
            if (_salvosLeft > 0) { _salvosLeft--; }
            StartLure();
            StartVisual();
            if (FlareSound && FlareSound.clip) { FlareSound.PlayOneShot(FlareSound.clip); }
            if (Ace != null) { Ace.OnFlare(); }
            else if (Chatter)
            {
                int ev = Friendly ? (SmokeMode ? ACO_Chatter.EV_ALLY_SMOKE : ACO_Chatter.EV_ALLY_FLARE) : ACO_Chatter.EV_ENEMY_FLARE;
                Chatter.ReportNear(ev, transform.position, "");
            }
        }

        /// 모든 클라이언트: 플레어와 같은 효과만 켠다(FlareActiveTime 동안 나를 쫓는 미사일이 끌려간다) — 연출·소리·무전·횟수 없음(SHADE 코브라).
        public void BeginLure()
        {
            if (TargetHealth != null && TargetHealth.Health <= 0f) { return; }
            StartLure();
        }

        /// BeginLure와 같고, 속은 미사일이 끌려간 점에서 터지지 않고 missDistance(m)만큼 비켜 지나가 빗나간다(SHADE 코브라 — 급감속한 기체를 지나쳐 날아간다).
        public void BeginLureMiss(float missDistance)
        {
            if (TargetHealth != null && TargetHealth.Health <= 0f) { return; }
            StartLure();
            _lureMiss = Mathf.Max(missDistance, 0f);
        }

        /// 지금 켜진 플레어 효과에 속은 미사일이 비켜 갈 거리(m). 0이면 플레어 점에서 터진다.
        public float GetLureMiss() { return _lureMiss; }

        // 플레어는 기체 속도 일부를 이어받고 뒤로 사출된다(연출 파티클: 속도 상속 0.8, 사출 15~35m/s). 미사일은 이 점을 쫓는다.
        private void StartLure()
        {
            _flareStart = Time.time;
            _lureMiss = 0f;
            _flarePos = FlareParticles ? FlareParticles.transform.position : transform.position;
            float speed = Flight != null ? Flight.Speed : (Unit != null ? Unit.Speed : 0f);
            _flareVel = transform.forward * Mathf.Max(speed * 0.8f - 25f, 0f);
        }

        private void StartVisual()
        {
            if (!FlareParticles) { return; }
            if (FlareUseEmitter)
            {
                ParticleSystem.EmissionModule em = FlareParticles.emission;
                em.enabled = true;
                if (!FlareParticles.isPlaying) { FlareParticles.Play(); }
                bool scheduled = _emitStopAt > Time.time;
                _emitStopAt = Time.time + Mathf.Max(FlareEmitDuration, 0.05f);
                if (!scheduled) { SendCustomEventDelayedSeconds(nameof(_EmitStop), Mathf.Max(FlareEmitDuration, 0.05f)); }
            }
            else
            {
                FlareParticles.Emit(Mathf.Max(FlareParticleCount, 1));
            }
        }

        /// 이미터 방식: 다시 뿌려 끝 시각이 늦춰졌으면 남은 만큼 더 기다린다.
        public void _EmitStop()
        {
            if (!FlareParticles) { return; }
            float left = _emitStopAt - Time.time;
            if (left > 0.02f)
            {
                SendCustomEventDelayedSeconds(nameof(_EmitStop), left);
                return;
            }
            ParticleSystem.EmissionModule em = FlareParticles.emission;
            em.enabled = false;
        }
    }
}
