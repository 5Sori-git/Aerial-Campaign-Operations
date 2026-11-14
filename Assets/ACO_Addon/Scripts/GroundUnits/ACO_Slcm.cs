using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 잠수함 발사 순항미사일(SLCM) 한 발 — 잠수함 보스(ACO_Submarine) 풀의 한 칸(설계 6-202). 체력은 같은 오브젝트의 SaccTarget(일반 미사일 2발).
    /// 비행은 서버 시간 함수(발사 시각 · 발사 자리 · 명중 자리)라 위치 동기화가 없다 — 잠수함이 0.25초마다 칸마다 지금 맡은 일제 발사를 Arm으로 넣는다.
    ///  1) 솟구침(PopTime): 물에서 튀어나와 순항 높이 CruiseAltitude(150m — 순양함 51m 위)까지 오르며 가속한다.
    ///  2) 순항: CruiseSpeed(230m/s)로 CruiseAltitude에.
    ///  3) 종말 상승: 명중 자리까지 TerminalDistance(5km) 남으면 같은 속도로 TerminalAltitude(450m)까지 오른다.
    ///  4) 급강하: DiveDistance(1.5km) 남으면 CruiseSpeed → DiveSpeed(750m/s)로 가속하며 뱃전으로 내리꽂는다(처음엔 완만하게, 끝에 가팔라진다 — DiveCurve).
    /// 끝까지 살아 있으면 명중(모든 화면에서 폭발 — 항모 체력은 모듈 오너가 따로 판정한다). 격추되면(SaccTarget 체력 0 — Explode) 그 자리에서 터진다.
    /// 락온 마커(LockMarker — "AAMTarget"이 아닌 이름: SAV Target 애니메이터가 그 이름을 다룬다)와 히트박스는 나는 동안만 켠다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_Slcm : UdonSharpBehaviour
    {
        [Header("참조 (프리팹 빌더)")]
        [Tooltip("체력(일반 미사일 2발 = 200). ExplodeOther에 이 스크립트를 등록해야 격추 통지를 받는다")]
        public SaccTarget TargetHealth;
        [Tooltip("이 미사일을 쓰는 잠수함 — 발사 물기둥 · 폭발 연출을 맡는다")]
        public ACO_Submarine Owner;
        [Tooltip("탄체 · 불꽃(나는 동안만 켠다)")]
        public GameObject Model;
        [Tooltip("연기 궤적(월드 공간) — 탄체(Model) 밖에 둔다: 나는 동안만 뿜고, 터지거나 명중하면 뿜기만 멈춰 남은 연기는 수명대로 사라진다(탄체와 같이 끄면 궤적까지 한꺼번에 사라졌다)")]
        public ParticleSystem Trail;
        [Tooltip("공중 락온 마커(레이어 29, 이름 LockMarker)")]
        public GameObject LockMarker;
        public Collider Hitbox;

        [Header("표적 정보")]
        [Tooltip("플레이어가 격추하면 팀 점수(모듈 오너가 준다 — 순양함 CIWS가 떨어뜨리면 없음)")]
        public int ScoreValue = 300;
        public string MarkerLabel = "SLCM";

        [Header("비행 (서버 시간 함수)")]
        [Tooltip("물 밖으로 솟구쳐 순항 높이에 닿는 시간(초)")]
        public float PopTime = 3f;
        [Tooltip("물속 출발 깊이(m)")]
        public float StartDepth = -4f;
        [Tooltip("순항 높이(m, 해수면 기준 — 순양함 마스트 51m 위) · 속도(m/s)")]
        public float CruiseAltitude = 150f;
        public float CruiseSpeed = 230f;
        [Tooltip("명중 자리까지 이 거리(m)가 남으면 종말 상승을 시작한다")]
        public float TerminalDistance = 5000f;
        [Tooltip("종말 상승 끝 높이(m)")]
        public float TerminalAltitude = 450f;
        [Tooltip("명중 자리까지 이 거리(m)가 남으면 급강하")]
        public float DiveDistance = 1500f;
        [Tooltip("급강하 끝 속도(m/s) — CruiseSpeed에서 고르게 가속한다")]
        public float DiveSpeed = 750f;
        [Tooltip("급강하 곡선(1 = 곧은 선, 클수록 처음 완만 · 끝 가파름)")]
        public float DiveCurve = 1.6f;

        private bool _initialized;
        private bool _armed;
        private int _key = -1;
        private int _launchMs;
        private Vector3 _from;
        private Vector3 _impact;
        private Vector3 _dir;
        private float _length;
        private float _sPop;
        private float _sTerm;
        private float _sDive;
        private float _tTerm;
        private float _tDive;
        private float _diveTime;
        private float _diveAccel;
        private float _flightTime;
        private bool _flying;
        private bool _launchShown;
        private bool _exploded;
        private bool _impacted;
        private bool _intercepted;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            SetFlying(false);
        }

        // ───────────── 잠수함이 넣는다 (모든 클라이언트) ─────────────

        /// 이 칸이 맡을 미사일. key가 같으면 그대로 둔다(0.25초마다 다시 부른다). 이미 끝난 미사일(늦게 들어옴)은 연출 없이 끝난 상태로 둔다.
        public void Arm(int key, int launchMs, Vector3 from, Vector3 impact)
        {
            Init();
            if (_armed && key == _key) { return; }
            _armed = true;
            _key = key;
            _launchMs = launchMs;
            _from = from;
            _impact = impact;
            Vector3 flat = new Vector3(impact.x - from.x, 0f, impact.z - from.z);
            _length = Mathf.Max(flat.magnitude, 1f);
            _dir = flat / _length;
            float v0 = Mathf.Max(CruiseSpeed, 1f);
            _sPop = v0 * PopTime * 0.5f;
            _sTerm = Mathf.Max(_length - TerminalDistance, _sPop);
            _sDive = Mathf.Max(_length - DiveDistance, _sTerm);
            _tTerm = PopTime + (_sTerm - _sPop) / v0;
            _tDive = PopTime + (_sDive - _sPop) / v0;
            // 급강하: v0에서 DiveSpeed까지 고르게 가속 — 거리 D = (v0 + v1) / 2 × 시간
            float d = Mathf.Max(_length - _sDive, 0f);
            float v1 = Mathf.Max(DiveSpeed, v0);
            _diveTime = 2f * d / (v0 + v1);
            _diveAccel = _diveTime > 0.001f ? (v1 - v0) / _diveTime : 0f;
            _flightTime = _tDive + _diveTime;
            _exploded = false;
            _impacted = false;
            _intercepted = false;
            _launchShown = false;
            // 지난 미사일의 격추자 기록을 지운다(로컬) — 모듈 오너가 '플레이어가 떨어뜨렸나'를 이 값으로 본다
            if (TargetHealth)
            {
                TargetHealth.LastHitWeaponType = 255;
                TargetHealth.LastHitByPlayer = null;
            }
            float t = (Networking.GetServerTimeInMilliseconds() - _launchMs) * 0.001f;
            if (t > _flightTime) { _impacted = true; }
            else if (t > 1f) { _launchShown = true; }   // 늦게 들어왔다 — 발사 물기둥은 지난 일
            SetFlying(false);
            transform.position = new Vector3(from.x, StartDepth - 20f, from.z);
        }

        /// 이 칸은 지금 맡은 미사일이 없다.
        public void Disarm()
        {
            Init();
            if (!_armed) { return; }
            _armed = false;
            _key = -1;
            SetFlying(false);
        }

        /// 모듈 오너(체력 오너)가 발사 전에 체력을 채운다. 풀에서 막 켠 직후면 SaccTarget의 Start가 아직이라 2프레임 뒤에.
        public void Revive()
        {
            SendCustomEventDelayedFrames(nameof(_ReviveNow), 2);
        }

        public void _ReviveNow()
        {
            if (TargetHealth) { TargetHealth.RespawnTarget(); }
        }

        /// 순양함 CIWS가 이 미사일을 떨어뜨렸다(체력 오너 화면에서만 부른다 — 체력 0이 동기화되어 모든 화면에서 Explode).
        public void InterceptKill()
        {
            if (!IsFlying() || TargetHealth == null) { return; }
            _intercepted = true;
            if (Networking.IsOwner(TargetHealth.gameObject)) { TargetHealth.Explode(); }
        }

        // ───────────── 상태 (잠수함 · 모듈이 읽는다) ─────────────

        public bool IsArmed() { return _armed; }
        public int GetKey() { return _key; }
        public int GetLaunchMs() { return _launchMs; }
        public float GetFlightTime() { return _flightTime; }
        public int GetArrivalMs() { return _launchMs + Mathf.RoundToInt(_flightTime * 1000f); }
        public bool IsFlying() { return _flying; }
        public bool WasIntercepted() { return _intercepted; }
        /// 떨어졌는가(체력 0 — 발사 1초 뒤부터 본다: 그 전엔 지난 미사일의 체력 0이 남아 있을 수 있다).
        public bool IsDown()
        {
            if (_exploded) { return true; }
            if (!_armed || TargetHealth == null) { return false; }
            float t = (Networking.GetServerTimeInMilliseconds() - _launchMs) * 0.001f;
            return t > 1f && TargetHealth.Health <= 0f;
        }
        /// 플레이어 무장이 마지막으로 맞혔는가(NPC 무장은 무기 번호에 100을 더한다 — ACO_Missile.NPC_WEAPON_OFFSET, 255 = 이번 미사일엔 아직 없음).
        public bool KilledByPlayer()
        {
            if (_intercepted || TargetHealth == null) { return false; }
            byte w = TargetHealth.LastHitWeaponType;
            return w != 255 && w < ACO_Missile.NPC_WEAPON_OFFSET;
        }
        /// 종말 상승 · 급강하 구간인가(명중 자리까지 TerminalDistance 안).
        public bool IsTerminal()
        {
            if (!_flying) { return false; }
            float t = (Networking.GetServerTimeInMilliseconds() - _launchMs) * 0.001f;
            return t >= _tTerm;
        }

        /// 급강하 구간인가.
        public bool IsDiving()
        {
            if (!_flying) { return false; }
            float t = (Networking.GetServerTimeInMilliseconds() - _launchMs) * 0.001f;
            return t >= _tDive;
        }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            if (!_armed || _exploded || _impacted) { return; }
            float t = (Networking.GetServerTimeInMilliseconds() - _launchMs) * 0.001f;
            if (t < 0f)
            {
                if (_flying) { SetFlying(false); }
                return;
            }
            if (t >= _flightTime)
            {
                _impacted = true;
                SetFlying(false);
                transform.position = _impact;
                if (Owner != null) { Owner.PlaySlcmImpact(_impact); }
                return;
            }
            // 늦게 들어왔는데 이미 격추된 미사일: 연출 없이 끝
            if (t > 1f && TargetHealth != null && TargetHealth.Health <= 0f)
            {
                _exploded = true;
                SetFlying(false);
                return;
            }
            if (!_flying) { SetFlying(true); }
            if (!_launchShown)
            {
                _launchShown = true;
                if (Owner != null) { Owner.PlaySlcmLaunch(new Vector3(_from.x, 0f, _from.z)); }
            }
            Vector3 p = PositionAt(t);
            Vector3 q = PositionAt(t + 0.05f);
            transform.position = p;
            Vector3 v = q - p;
            if (v.sqrMagnitude > 0.0001f) { transform.rotation = Quaternion.LookRotation(v, Vector3.up); }
        }

        /// 발사 뒤 t초의 자리(모든 클라이언트가 같은 값).
        public Vector3 PositionAt(float t)
        {
            float s;
            if (t < PopTime) { s = CruiseSpeed * t * t / (2f * Mathf.Max(PopTime, 0.01f)); }
            else if (t < _tDive) { s = _sPop + CruiseSpeed * (t - PopTime); }
            else
            {
                float tau = Mathf.Min(t - _tDive, _diveTime);
                s = _sDive + CruiseSpeed * tau + 0.5f * _diveAccel * tau * tau;
            }
            s = Mathf.Clamp(s, 0f, _length);
            float y;
            if (t < PopTime) { y = Mathf.Lerp(StartDepth, CruiseAltitude, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(PopTime, 0.01f))); }
            else if (s < _sTerm) { y = CruiseAltitude; }
            else if (s < _sDive)
            {
                // 종말 상승(순항 높이 → TerminalAltitude)
                float k = (s - _sTerm) / Mathf.Max(_sDive - _sTerm, 1f);
                y = Mathf.Lerp(CruiseAltitude, TerminalAltitude, Mathf.SmoothStep(0f, 1f, k));
            }
            else
            {
                // 급강하: 처음엔 완만하게 넘어가고 끝으로 갈수록 가파르게(u^DiveCurve) 뱃전 높이까지
                float u = Mathf.Clamp01((s - _sDive) / Mathf.Max(_length - _sDive, 1f));
                y = Mathf.Lerp(TerminalAltitude, _impact.y, Mathf.Pow(u, Mathf.Max(DiveCurve, 1f)));
            }
            Vector3 flat = _from + _dir * s;
            return new Vector3(flat.x, y, flat.z);
        }

        private void SetFlying(bool on)
        {
            _flying = on;
            if (Model && Model.activeSelf != on) { Model.SetActive(on); }
            if (Trail)
            {
                if (on && !Trail.isEmitting) { Trail.Play(true); }
                else if (!on && Trail.isEmitting) { Trail.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            }
            if (LockMarker && LockMarker.activeSelf != on) { LockMarker.SetActive(on); }
            if (Hitbox && Hitbox.enabled != on) { Hitbox.enabled = on; }
        }

        /// SaccTarget이 체력 0일 때 ExplodeOther에 보내는 이벤트(모든 클라이언트).
        public void Explode()
        {
            if (!_flying) { return; }   // 지난 미사일의 체력이 늦게 와서 부르는 경우 등
            _exploded = true;
            Vector3 p = transform.position;
            Vector3 f = transform.forward;   // 파편이 날던 방향으로 흩어진다
            SetFlying(false);
            if (Owner != null) { Owner.PlaySlcmShootDown(p, f); }
        }
    }
}
