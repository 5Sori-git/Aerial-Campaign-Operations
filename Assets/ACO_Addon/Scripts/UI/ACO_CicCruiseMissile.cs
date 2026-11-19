using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// CIC 순항미사일 한 발(6-276) — ACO_CicStrike 풀의 한 칸. 아군 순양함 VLS에서 커서 둘레로 날아가 약한 범위 피해를 준다.
    /// 비행은 서버 시간 함수(발사 시각 · 발사 자리 · 명중 자리 · 순항 높이)라 위치 동기화가 없다 — ACO_CicStrike가 매 프레임 Arm으로 넣는다.
    ///  1) 수직 상승(BoostTime): VLS에서 곧게 BoostHeight만큼 솟는다.
    ///  2) 꺾어 오름(PopTime): 수평으로 가속하며 순항 높이(그 일제 발사 항로의 가장 높은 지형 + 여유 — Arm 값)로 오른다.
    ///  3) 순항: CruiseSpeed.
    ///  4) 종말: 명중 자리까지 TerminalDistance 남으면 명중 자리 지면 + TerminalHeight로, DiveDistance 남으면 DiveSpeed까지 가속하며 내리꽂는다.
    /// 끝까지 살아 있으면 명중(모든 화면에서 폭발 연출, 미사일 오너 화면만 범위 피해 — 오너 = 쏜 사람, 나가면 마스터).
    /// 적 대공의 미끼(6-276): 체력은 같은 오브젝트의 SaccTarget — 적 대공포 · CIWS · SAM의 표적 목록(Bake)에 들어 있어 쏘인다.
    /// 체력 0(Explode)이면 그 자리에서 터지고 지상 피해는 없다. 아군 대공 · 플레이어 락온 목록에는 넣지 않는다(락온 마커 없음).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_CicCruiseMissile : UdonSharpBehaviour
    {
        [Header("참조 (프리팹 빌더 · Bake)")]
        [Tooltip("체력(대공포 약 1.5초 · SAM 한 발). ExplodeOther에 이 스크립트를 등록해야 격추 통지를 받는다")]
        public SaccTarget TargetHealth;
        public ACO_CicStrike Strike;
        [Tooltip("탄체 · 불꽃(나는 동안만 켠다)")]
        public GameObject Model;
        [Tooltip("연기 궤적(월드 공간) — 탄체 밖에 둔다: 나는 동안만 뿜는다")]
        public ParticleSystem Trail;
        [Tooltip("적 대공이 겨누는 자리(AAMTarget — 나는 동안만 켠다)")]
        public GameObject AimPoint;
        public AudioSource FlySound;
        [Tooltip("발사음(6-312 — VLS 발사 소리, 탄체 Model 아래, 미사일을 따라간다). 발사 순간에만 한 번 — 늦게 들어와 날던 미사일에선 안 난다")]
        public AudioSource LaunchAudio;
        [Tooltip("미끼 세기(m): 적 대공포 · SAM이 이 미사일을 이만큼 더 가깝다고 쳐서 조종사보다 먼저 노린다(6-276)")]
        public float DecoyPriority = 1000f;

        [Header("비행 (서버 시간 함수)")]
        public float BoostTime = 1.2f;
        public float BoostHeight = 40f;
        [Tooltip("수직 상승 뒤 순항 높이 · 속도에 닿는 시간(초)")]
        public float PopTime = 4f;
        [Tooltip("순항 속도(m/s) — 300(작전 지역 끝까지 약 85초). 2026-11-18 잠깐 450이었다가 \"생각보다 세다\"로 300으로 되돌림(6-284)")]
        public float CruiseSpeed = 300f;
        [Tooltip("명중 자리까지 이 거리(m)가 남으면 종말 상승(명중 자리 지면 + TerminalHeight)")]
        public float TerminalDistance = 3000f;
        public float TerminalHeight = 400f;
        [Tooltip("명중 자리까지 이 거리(m)가 남으면 급강하")]
        public float DiveDistance = 1000f;
        public float DiveSpeed = 600f;
        [Tooltip("급강하 곡선(1 = 곧은 선, 클수록 처음 완만 · 끝 가파름)")]
        public float DiveCurve = 1.6f;

        private bool _initialized;
        private bool _armed;
        private int _key = -1;
        private int _launchMs;
        private Vector3 _from;
        private Vector3 _impact;
        private Vector3 _dir;
        private float _cruiseY;
        private float _termY;
        private float _length;
        private float _sPop;
        private float _sTerm;
        private float _sDive;
        private float _tStart;
        private float _tDive;
        private float _diveTime;
        private float _diveAccel;
        private float _flightTime;
        private bool _flying;
        private bool _launchShown;
        private bool _exploded;
        private bool _impacted;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            SetFlying(false);
        }

        // ───────────── ACO_CicStrike가 넣는다 (모든 클라이언트) ─────────────

        /// 이 칸이 맡을 미사일. key가 같으면 그대로 둔다(매 프레임 다시 부른다). 이미 끝난 미사일(늦게 들어옴)은 연출 없이 끝난 상태로 둔다.
        public void Arm(int key, int launchMs, Vector3 from, Vector3 impact, float cruiseY)
        {
            Init();
            if (_armed && key == _key) { return; }
            _armed = true;
            _key = key;
            _launchMs = launchMs;
            _from = from;
            _impact = impact;
            _cruiseY = Mathf.Max(cruiseY, from.y + BoostHeight);
            _termY = impact.y + TerminalHeight;
            Vector3 flat = new Vector3(impact.x - from.x, 0f, impact.z - from.z);
            _length = Mathf.Max(flat.magnitude, 1f);
            _dir = flat / _length;
            float v0 = Mathf.Max(CruiseSpeed, 1f);
            _tStart = BoostTime;
            _sPop = v0 * PopTime * 0.5f;
            _sTerm = Mathf.Clamp(_length - TerminalDistance, _sPop, _length);
            _sDive = Mathf.Clamp(_length - DiveDistance, _sTerm, _length);
            _tDive = _tStart + PopTime + (_sDive - _sPop) / v0;
            float d = Mathf.Max(_length - _sDive, 0f);
            float v1 = Mathf.Max(DiveSpeed, v0);
            _diveTime = 2f * d / (v0 + v1);
            _diveAccel = _diveTime > 0.001f ? (v1 - v0) / _diveTime : 0f;
            _flightTime = _tDive + _diveTime;
            _exploded = false;
            _impacted = false;
            _launchShown = false;
            float t = (Networking.GetServerTimeInMilliseconds() - _launchMs) * 0.001f;
            if (t > _flightTime)
            {
                // 이미 끝난 일제 발사(늦게 들어옴 등): 체력이 0이면 격추, 아니면 명중으로 센다(디버깅 2026-11-18 — 격추를 명중으로 세던 것)
                if (TargetHealth != null && TargetHealth.Health <= 0f) { _exploded = true; }
                else { _impacted = true; }
            }
            else if (t > 1f) { _launchShown = true; }   // 늦게 들어왔다 — 발사 연출은 지난 일
            SetFlying(false);
            transform.position = from;
        }

        /// 이 칸은 지금 맡은 미사일이 없다(작전 끝 등 — 터지지 않고 사라진다).
        public void Disarm()
        {
            Init();
            if (!_armed) { return; }
            _armed = false;
            _key = -1;
            SetFlying(false);
        }

        /// 쏜 사람(오너)이 발사 전에 체력을 채운다. 2프레임 뒤에(SaccTarget Start가 아직일 수 있다).
        public void Revive() { SendCustomEventDelayedFrames(nameof(_ReviveNow), 2); }

        public void _ReviveNow()
        {
            if (!TargetHealth) { return; }
            TargetHealth.LastHitWeaponType = 255;
            TargetHealth.LastHitByPlayer = null;
            TargetHealth.RespawnTarget();
        }

        // ───────────── 상태 ─────────────

        public bool IsArmed() { return _armed; }
        public int GetKey() { return _key; }
        public bool IsFlying() { return _flying; }
        public bool IsImpacted() { return _armed && _impacted && !_exploded; }
        public bool IsShotDown() { return _armed && _exploded; }
        public int GetArrivalMs() { return _launchMs + Mathf.RoundToInt(_flightTime * 1000f); }
        public float GetFlightTime() { return _flightTime; }

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
                if (Strike != null) { Strike.PlayImpact(this, _impact, Networking.IsOwner(gameObject)); }
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
                if (LaunchAudio) { LaunchAudio.Play(); }
                if (Strike != null) { Strike.PlayLaunch(_from); }
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
            if (t < _tStart)
            {
                float u = t / Mathf.Max(_tStart, 0.01f);
                return _from + Vector3.up * (BoostHeight * u * u);
            }
            float tt = t - _tStart;
            float s;
            if (tt < PopTime) { s = CruiseSpeed * tt * tt / (2f * Mathf.Max(PopTime, 0.01f)); }
            else if (t < _tDive) { s = _sPop + CruiseSpeed * (tt - PopTime); }
            else
            {
                float tau = Mathf.Min(t - _tDive, _diveTime);
                s = _sDive + CruiseSpeed * tau + 0.5f * _diveAccel * tau * tau;
            }
            s = Mathf.Clamp(s, 0f, _length);
            float y0 = _from.y + BoostHeight;
            float y;
            if (tt < PopTime) { y = Mathf.Lerp(y0, _cruiseY, Mathf.SmoothStep(0f, 1f, tt / Mathf.Max(PopTime, 0.01f))); }
            else if (s < _sTerm) { y = _cruiseY; }
            else if (s < _sDive)
            {
                float k = (s - _sTerm) / Mathf.Max(_sDive - _sTerm, 1f);
                y = Mathf.Lerp(_cruiseY, _termY, Mathf.SmoothStep(0f, 1f, k));
            }
            else
            {
                float u = Mathf.Clamp01((s - _sDive) / Mathf.Max(_length - _sDive, 1f));
                y = Mathf.Lerp(_termY, _impact.y, Mathf.Pow(u, Mathf.Max(DiveCurve, 1f)));
            }
            Vector3 flat = _from + _dir * s;
            return new Vector3(flat.x, y, flat.z);
        }

        private void SetFlying(bool on)
        {
            _flying = on;
            if (Model && Model.activeSelf != on) { Model.SetActive(on); }
            if (AimPoint && AimPoint.activeSelf != on) { AimPoint.SetActive(on); }
            if (Trail)
            {
                if (on && !Trail.isEmitting) { Trail.Play(true); }
                else if (!on && Trail.isEmitting) { Trail.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            }
            if (FlySound)
            {
                if (on && !FlySound.isPlaying) { FlySound.Play(); }
                else if (!on && FlySound.isPlaying) { FlySound.Stop(); }
            }
        }

        /// SaccTarget이 체력 0일 때 ExplodeOther에 보내는 이벤트(모든 클라이언트) — 적 대공에 격추.
        public void Explode()
        {
            if (!_flying) { return; }
            _exploded = true;
            Vector3 p = transform.position;
            Vector3 f = transform.forward;
            SetFlying(false);
            if (Strike != null) { Strike.PlayShootDown(p, f); }
        }
    }
}
