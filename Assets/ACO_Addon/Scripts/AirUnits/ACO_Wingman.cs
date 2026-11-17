using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 아군 편대 한 기(6-273 — 지원 호출 WINGMEN). 적 전투기와 같은 AI(ACO_NPCFighterAI)를 쓰고 노리는 대상만 적 공중(29) · 지상(30)이다.
    /// 지원 관리자(ACO_SupportManager)가 부를 때 Begin으로 켜고, 끝나면 Finish로 끈다. 시간은 각 화면이 센다(시작 이벤트를 받은 순간부터).
    /// 조종(오너 = 부른 사람):
    ///  - 표적이 없으면 부른 사람 기체 뒤쪽 옆(FormationOffset, 편대 순번마다 좌우)으로 따라간다(AI.SteerOverride).
    ///  - 부른 사람에게서 LeashDistance 넘게 멀어지면 교전 중이어도 돌아온다.
    ///  - 부른 사람이 기체에 없으면(내림 · 격추 · 나감) 마지막 자리를 집결 지점으로 두고 AI가 주변에서 싸운다.
    ///  - 시간이 다 되면(또는 미션이 끝나면) 위로 빠져나가고 LeaveTime초 뒤 모든 화면에서 꺼진다.
    /// 격추: SaccTarget의 ExplodeOther에 이 스크립트를 넣어 Explode를 받는다(관리자가 무전 · 편대 정리).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_Wingman : UdonSharpBehaviour
    {
        [Header("연결 (프리팹 빌더 · Bake)")]
        public ACO_NPCFighterAI AI;
        public ACO_NPCFlightController Flight;
        public ACO_SupportManager Manager;

        [Header("편대")]
        [Tooltip("부른 사람 기체 기준 자리(m, 오른쪽 +x · 위 +y · 앞 +z). 편대 순번 0은 오른쪽, 1은 왼쪽(x 부호를 바꾼다)")]
        public Vector3 FormationOffset = new Vector3(60f, 8f, -70f);
        [Tooltip("이 거리(m)보다 멀어지면 교전을 멈추고 돌아온다")]
        public float LeashDistance = 4000f;
        [Tooltip("자리까지 거리 1m당 더하는 속도(m/s) — 멀면 빨리 따라붙는다")]
        public float CatchUpGain = 0.25f;
        public float MaxCatchUp = 120f;
        [Tooltip("멀어져 돌아오기 시작하면 이 비율(× LeashDistance) 안으로 들어올 때까지 계속 돌아온다 — 경계에서 오락가락하지 않게")]
        public float ReturnFraction = 0.6f;
        [Tooltip("이탈: 위로 빠지는 각(도)")]
        public float LeaveClimbAngle = 25f;

        private bool _active;
        private bool _leaving;
        private bool _dead;
        private int _flight = -1;
        private int _slot;
        private int _support = -1;
        private int _callerId = -1;
        private float _endTime;
        private float _leaveEnd;
        private float _leaveTime = 12f;
        private Vector3 _leaderPos;
        private Vector3 _leaderVel;
        private bool _leaderSeen;
        private bool _returning;   // 너무 멀어져 돌아오는 중(LeashDistance 밖 → ReturnFraction 안까지)

        public bool IsActive() { return _active; }
        public bool IsDead() { return _dead; }
        public int GetFlight() { return _flight; }

        /// 관리자(모든 화면): 편대 시작. fresh = 새로 부름(부활), 아니면 늦게 들어온 화면(위치 · 생사는 동기화가 맞춘다).
        public void Begin(int flight, int slot, int support, int callerId, bool fresh, Vector3 pos, Quaternion rot, float seconds, float leaveTime)
        {
            _flight = flight;
            _slot = slot;
            _support = support;
            _callerId = callerId;
            bool caller = Networking.LocalPlayer != null && Networking.LocalPlayer.playerId == callerId;
            _endTime = Time.time + Mathf.Max(seconds, 0f);
            _leaveTime = Mathf.Max(leaveTime, 1f);
            _leaving = false;
            _dead = false;
            _leaderSeen = false;
            _returning = false;
            gameObject.SetActive(true);
            // 부른 사람이 계산한다 — 체력 · 위치 · 표적 판정이 그 화면에서(점수도 부른 사람에게)
            if (caller && fresh && !Networking.IsOwner(gameObject)) { Networking.SetOwner(Networking.LocalPlayer, gameObject); }
            if (AI)
            {
                AI.EnsureInit();
                AI.SteerOverride = false;
                AI.HoldFire = false;
                AI.MissionSpawn(pos, rot, pos, fresh);
                AI.ApplySkill(ACO_NPCFighterAI.SKILL_VETERAN, 1f, 1f, 1f, 1f, 1f, 1f);
                if (fresh && AI.MissileLauncher) { AI.MissileLauncher.ResetAmmo(); }   // 새 출격은 미사일 가득
            }
            _active = true;
        }

        /// 관리자(모든 화면): 시간이 다 됐거나 미션이 끝났다 — 위로 빠져나가 LeaveTime초 뒤 꺼진다.
        public void StartLeave()
        {
            if (!_active || _leaving) { return; }
            _leaving = true;
            _leaveEnd = Time.time + _leaveTime;
            if (AI)
            {
                AI.HoldFire = true;   // 이탈 중에는 표적을 놓고 쏘지 않는다(작전이 끝난 뒤 격추 · 점수가 생기지 않게)
                if (AI.MissileLauncher) { AI.MissileLauncher.ReleaseAim(); }
            }
        }

        /// 관리자(모든 화면): 끈다.
        public void Finish()
        {
            if (!_active && !gameObject.activeSelf) { return; }
            _active = false;
            _leaving = false;
            if (AI) { AI.SteerOverride = false; AI.HoldFire = false; AI.MissionDespawn(); }
            gameObject.SetActive(false);
        }

        public float GetEndTime() { return _endTime; }
        public bool IsLeaving() { return _leaving; }

        /// SaccTarget 격추 이벤트(ExplodeOther — 모든 화면).
        public void Explode()
        {
            if (!_active || _dead) { return; }
            _dead = true;
            if (Manager) { Manager.OnWingmanDown(_flight); }
        }

        void Update()
        {
            if (!_active) { return; }
            float now = Time.time;
            if (!_leaving && now >= _endTime) { StartLeave(); }
            if (_leaving && now >= _leaveEnd)
            {
                Finish();   // 먼저 끈다 — 관리자가 편대에 켜진 기체가 남았는지 본다
                if (Manager) { Manager.OnWingmanGone(_flight); }
                return;
            }
            // 늦게 온 체력 0(지난 상태)으로 격추로 봤는데 AI가 살아 있다(_SyncRevive가 되돌림) — 격추 표시를 푼다
            if (_dead && AI != null && AI.IsAlive()) { _dead = false; }
            if (_dead || AI == null || Flight == null || !AI.IsLocallyOwned() || !AI.IsAlive()) { return; }

            if (_leaving)
            {
                Vector3 f = transform.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 0.0001f) { f = Vector3.forward; }
                AI.OverrideDirection = (f.normalized + Vector3.up * Mathf.Tan(LeaveClimbAngle * Mathf.Deg2Rad)).normalized;
                AI.OverrideSpeed = Flight.MaxSpeed;
                AI.SteerOverride = true;
                return;
            }

            Transform leader = LeaderTransform();
            if (leader != null)
            {
                float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                Vector3 lp = leader.position;
                if (_leaderSeen)
                {
                    Vector3 raw = (lp - _leaderPos) / dt;
                    if (raw.sqrMagnitude < 2250000f) { _leaderVel = Vector3.Lerp(_leaderVel, raw, 1f - Mathf.Exp(-4f * dt)); }
                }
                else { _leaderVel = leader.forward * 150f; }
                _leaderPos = lp;
                if (!_leaderSeen) { AI.SetMissionHold(lp); }
                _leaderSeen = true;
            }

            float leaderSq = _leaderSeen ? (transform.position - _leaderPos).sqrMagnitude : 0f;
            if (leaderSq > LeashDistance * LeashDistance) { _returning = true; }
            else if (_returning && leaderSq < LeashDistance * LeashDistance * ReturnFraction * ReturnFraction) { _returning = false; }
            if (leader != null && (!AI.IsEngaged() || _returning))
            {
                // 편대 자리: 부른 사람 진행 방향(수평) 기준
                Vector3 fwd = _leaderVel.sqrMagnitude > 100f ? _leaderVel.normalized : leader.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) { fwd = Vector3.forward; }
                fwd = fwd.normalized;
                Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                float side = _slot == 0 ? 1f : -1f;
                Vector3 slotPos = _leaderPos + right * (FormationOffset.x * side) + Vector3.up * FormationOffset.y + fwd * FormationOffset.z;
                Vector3 to = slotPos + _leaderVel * 1.5f - transform.position;
                float dist = to.magnitude;
                AI.OverrideDirection = dist > 1f ? to / dist : fwd;
                AI.OverrideSpeed = Mathf.Clamp(_leaderVel.magnitude + Mathf.Min(dist * CatchUpGain, MaxCatchUp), Flight.CruiseSpeed * 0.7f, Flight.MaxSpeed);
                AI.SteerOverride = true;
            }
            else
            {
                if (leader == null && _leaderSeen) { AI.SetMissionHold(_leaderPos); }   // 부른 사람이 없다 — 마지막 자리 주변에서 싸운다
                AI.SteerOverride = false;
            }
        }

        /// 부른 사람이 지금 탄 기체(그 기체의 SUPPORT 다이얼 — 사람이 타 있을 때만).
        private Transform LeaderTransform()
        {
            if (Manager == null) { return null; }
            ACO_DFUNC_Support sup = Manager.GetSupport(_support);
            if (sup == null || sup.EntityControl == null || !sup.EntityControl.Occupied) { return null; }
            // 그 기체에 지금 탄 사람이 부른 사람일 때만(내린 뒤 다른 사람이 탔으면 따라가지 않는다 — SAV는 조종사가 기체 소유자)
            VRCPlayerApi pilot = Networking.GetOwner(sup.EntityControl.gameObject);
            if (!Utilities.IsValid(pilot) || pilot.playerId != _callerId) { return null; }
            Transform t = sup.EntityControl.transform;
            return t.gameObject.activeInHierarchy ? t : null;
        }
    }
}
