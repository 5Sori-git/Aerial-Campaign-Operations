using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace ACO
{
    /// 교차 기동(에이스 공통 부품 — NPC 전투기 안의 자식). 두 가지 꼴:
    /// - 짝 교차(Group 없음 — 분신 에이스 형제 HEADS·TAILS): 짝(Partner — Bake가 잇는다)과 서로를 향해 날아 엇갈리고, 엇갈리는 순간 무언가를 뿌린다.
    /// - 편대 집결(Group 2기 이상 — SHADE 편대): 살아 있는 편대원 전부가 그 한가운데로 같이 도착하게 모였다가, 지나는 순간 각자 뿌리고 사방으로 흩어진다.
    ///
    /// - 시작: 정하는 쪽의 AI 오너가 Interval마다(등장 FirstDelay 뒤부터) 또는 참가 기체에 미사일이 IncomingReactDistance 안으로 다가오면
    ///   (IncomingCooldown 간격) 모두에게 알린다. 짝 교차는 Leader가, 편대 집결은 살아 있는 편대원 중 Group 앞 번호가 정한다(죽으면 다음이 이어받는다).
    ///   짝 교차는 서로 MinDistance~MaxDistance일 때, 편대 집결은 한가운데에서 가장 먼 기체가 그 절반 거리 사이일 때만. 참가는 살아 있고 Allowed인 기체.
    /// - 짝 교차 진행(각 기체 오너가 조종 — AI 조종 덮어쓰기, 동기화는 기체 움직임 그대로): 서로를 향해 최고 속도로, 정하는 쪽은 오른쪽·짝은 왼쪽으로 Offset씩 비켜
    ///   PassDistance 안으로 엇갈리는 순간 오너가 모두에게 알린다(NetPass — FlareOnPass면 플레어, Listener에 OnCrossPass). AfterTime 더 곧게 난 뒤 평소 전투로.
    /// - 편대 집결 진행: 정하는 쪽이 한가운데·도착 시간(가장 먼 기체가 최고 속도로 걸리는 시간)·참가 기체를 보낸다. 기체마다 높이를 GroupStack씩 달리하고
    ///   다가오는 방향 오른쪽으로 Offset 비켜 지나간다(부딪치지 않게). 가까운 기체는 같이 도착하게 속도를 줄인다(최고 속도 × GroupMinSpeed 아래로는 안 줄인다).
    ///   지점 GroupPassDistance 안에 들거나 지나치면 NetPass. 한 기체가 격추·Allowed 꺼짐이면 그 기체만 빠지고, 나머지가 다 빠지면 그만둔다.
    /// - MaxTime 안에 못 지나가면 그만둔다.
    /// - Listener(선택): 시작 때 OnCrossStart(모든 화면 — 참가 기체마다, IsLeadNow는 정하는 쪽·편대 집결의 첫 참가 기체만), 엇갈릴 때 OnCrossPass(모든 화면, 그 기체만).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_CrossManeuver : UdonSharpBehaviour
    {
        [Header("참조 (프리팹 빌더/Bake가 넣는다)")]
        public ACO_NPCFighterAI AI;
        [Tooltip("엇갈릴 짝(Bake가 넣는다 — 짝 교차)")]
        public ACO_CrossManeuver Partner;
        [Tooltip("교차를 정하는 쪽(Bake가 넣는다 — 짝 중 하나만)")]
        public bool Leader;
        [Tooltip("편대 집결(Bake가 넣는다 — 자기 포함, 편대원 모두 같은 순서). 2기 이상이면 짝 교차 대신 편대 집결을 한다")]
        public ACO_CrossManeuver[] Group;
        [Tooltip("시작·엇갈림 알림을 받는 스크립트(선택) — OnCrossStart, OnCrossPass")]
        public UdonSharpBehaviour Listener;

        [Header("교차")]
        public float Interval = 90f;
        [Tooltip("등장 뒤 첫 교차까지(초)")]
        public float FirstDelay = 30f;
        [Tooltip("짝 교차: 서로 이 거리(m) 사이일 때만. 편대 집결: 한가운데에서 가장 먼 기체가 이 절반 사이일 때만")]
        public float MinDistance = 1000f;
        public float MaxDistance = 6000f;
        [Tooltip("엇갈릴 때 비키는 거리(m) — 짝 교차는 둘이 반대쪽으로, 편대 집결은 각자 다가오는 방향 오른쪽으로")]
        public float Offset = 60f;
        [Tooltip("짝 교차: 이 거리(m) 안으로 엇갈리면 뿌린다")]
        public float PassDistance = 300f;
        [Tooltip("엇갈린 뒤 곧게 나는 시간(초)")]
        public float AfterTime = 3f;
        [Tooltip("이 시간(초) 안에 못 지나가면 그만둔다")]
        public float MaxTime = 15f;
        [Tooltip("참가 기체를 노린 미사일이 이 거리(m) 안이면 간격 전이라도 교차한다")]
        public float IncomingReactDistance = 2500f;
        public float IncomingCooldown = 40f;
        [Tooltip("엇갈리는 순간 플레어를 뿌린다")]
        public bool FlareOnPass = true;

        [Header("편대 집결")]
        [Tooltip("기체마다 집결 높이를 이만큼(m)씩 달리한다(부딪치지 않게)")]
        public float GroupStack = 30f;
        [Tooltip("집결 지점 이 거리(m) 안에 들거나 지나치면 뿌린다")]
        public float GroupPassDistance = 150f;
        [Tooltip("같이 도착하려고 속도를 줄일 때의 하한(최고 속도 × 이 값)")]
        public float GroupMinSpeed = 0.55f;

        // 주인 스크립트가 끄면 교차하지 않는다(예: 형제 분노, 코브라 중)
        [System.NonSerialized] public bool Allowed = true;

        private const int MAX_GROUP = 16;

        private bool _wasAlive;
        private int _state;          // 0 = 평소, 1 = 서로를(집결 지점을) 향해, 2 = 엇갈린 뒤 곧게
        private float _start;
        private float _afterUntil;
        private float _next;
        private float _lastIncoming = -100f;
        private bool _passSent;
        private float _side = 1f;
        private bool _group;         // 이번 기동이 편대 집결
        private bool _leadNow;       // 이번 기동에서 대사를 맡는 쪽
        private Vector3 _aim;        // 편대 집결: 이 기체가 지나갈 자리
        private Vector3 _approach;   // 편대 집결: 다가오는 수평 방향(지나쳤는지 판단)
        private float _arriveAt;     // 편대 집결: 같이 도착할 시각

        void OnDisable()
        {
            End();
            _wasAlive = false;
        }

        public bool IsAliveNow() { return _wasAlive; }
        public bool IsCrossing() { return _state != 0; }
        /// 이번 교차에서 대사를 맡는 쪽인가(짝 교차는 정하는 쪽, 편대 집결은 첫 참가 기체).
        public bool IsLeadNow() { return _state != 0 && _leadNow; }

        private bool Alive() { return AI != null && AI.IsAlive() && (AI.TargetHealth == null || AI.TargetHealth.Health > 0f); }
        private bool HasGroup() { return Group != null && Group.Length >= 2; }

        void Update()
        {
            bool alive = Alive();
            if (alive && !_wasAlive)
            {
                End();
                _next = Time.time + FirstDelay;
                Allowed = true;
            }
            if (!alive && _wasAlive) { End(); }
            _wasAlive = alive;
            if (!alive) { return; }

            bool owner = Networking.IsOwner(AI.gameObject);
            if (_state == 0)
            {
                if (owner)
                {
                    if (HasGroup()) { if (IsGroupDecider()) { TryStartGroup(); } }
                    else if (Leader) { TryStart(); }
                }
                return;
            }
            float t = Time.time - _start;
            if (_group)
            {
                if (!Allowed || t > MaxTime || (_state == 1 && !OthersInGroup())) { End(); return; }
            }
            else if (Partner == null || Partner.AI == null || !Partner.IsAliveNow() || !Allowed || !Partner.Allowed || t > MaxTime) { End(); return; }
            if (_state == 1)
            {
                if (!owner) { return; }
                if (_group) { SteerGroup(); return; }
                Vector3 me = AI.transform.position;
                Vector3 p = Partner.AI.transform.position;
                Vector3 d = p - me;
                Vector3 side = Vector3.Cross(Vector3.up, d);
                side = side.sqrMagnitude > 0.000001f ? side.normalized : AI.transform.right;
                AI.SteerOverride = true;
                AI.OverrideDirection = p + side * (Offset * _side) - me;
                AI.OverrideSpeed = AI.Flight != null ? AI.Flight.MaxSpeed : 0f;
                if (!_passSent && d.sqrMagnitude <= PassDistance * PassDistance)
                {
                    _passSent = true;
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetPass));
                }
                return;
            }
            if (owner)
            {
                AI.SteerOverride = true;
                AI.OverrideDirection = AI.transform.forward;
            }
            if (Time.time >= _afterUntil) { End(); }
        }

        /// 정하는 쪽 오너: 간격마다, 또는 어느 쪽에 미사일이 다가오면 교차를 알린다.
        private void TryStart()
        {
            if (!Allowed || Partner == null || Partner.AI == null || !Partner.IsAliveNow() || !Partner.Allowed) { return; }
            bool incoming = (AI.Flares != null && AI.Flares.HasIncomingWithin(IncomingReactDistance))
                || (Partner.AI.Flares != null && Partner.AI.Flares.HasIncomingWithin(IncomingReactDistance));
            bool due = Time.time >= _next;
            if (!due && !(incoming && Time.time - _lastIncoming >= IncomingCooldown)) { return; }
            float dist = Vector3.Distance(AI.transform.position, Partner.AI.transform.position);
            if (dist < MinDistance || dist > MaxDistance) { return; }
            if (incoming) { _lastIncoming = Time.time; }
            _next = Time.time + Interval;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStart));
        }

        /// 모든 화면(정하는 쪽에서): 둘이 교차를 시작한다 — 정하는 쪽은 오른쪽, 짝은 왼쪽으로 비켜 엇갈린다.
        public void NetStart()
        {
            Begin(1f);
            if (Partner != null) { Partner.Begin(-1f); }
        }

        public void Begin(float side)
        {
            if (!_wasAlive) { return; }
            _state = 1;
            _group = false;
            _leadNow = side > 0f;
            _start = Time.time;
            _passSent = false;
            _side = side;
            if (Listener != null) { Listener.SendCustomEvent("OnCrossStart"); }
        }

        // ───────────── 편대 집결 ─────────────

        /// 살아 있는 편대원 중 Group 앞 번호가 정한다(모든 화면이 같은 규칙).
        private bool IsGroupDecider()
        {
            for (int i = 0; i < Group.Length; i++)
            {
                ACO_CrossManeuver m = Group[i];
                if (m != null && m.IsAliveNow()) { return m.gameObject == gameObject; }
            }
            return false;
        }

        /// 이번 집결에서 아직 날고 있는 다른 편대원이 있는가.
        private bool OthersInGroup()
        {
            for (int i = 0; i < Group.Length; i++)
            {
                ACO_CrossManeuver m = Group[i];
                if (m != null && m.gameObject != gameObject && m.IsAliveNow() && m.IsCrossing()) { return true; }
            }
            return false;
        }

        /// 정하는 쪽 오너: 간격마다, 또는 참가 기체에 미사일이 다가오면 편대 집결을 알린다(참가 2기 이상, 너무 뭉쳤거나 흩어져 있으면 안 한다).
        private void TryStartGroup()
        {
            int n = Mathf.Min(Group.Length, MAX_GROUP);
            int mask = 0;
            int count = 0;
            Vector3 sum = Vector3.zero;
            bool incoming = false;
            for (int i = 0; i < n; i++)
            {
                ACO_CrossManeuver m = Group[i];
                if (m == null || m.AI == null || !m.IsAliveNow() || !m.Allowed || m.IsCrossing()) { continue; }
                mask |= 1 << i;
                count++;
                sum += m.AI.transform.position;
                if (m.AI.Flares != null && m.AI.Flares.HasIncomingWithin(IncomingReactDistance)) { incoming = true; }
            }
            if (count < 2) { return; }
            bool due = Time.time >= _next;
            if (!due && !(incoming && Time.time - _lastIncoming >= IncomingCooldown)) { return; }
            Vector3 center = sum / count;
            float far = 0f;
            float arrive = 0f;
            for (int i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) == 0) { continue; }
                ACO_NPCFighterAI ai = Group[i].AI;
                float d = Vector3.Distance(ai.transform.position, center);
                float speed = ai.Flight != null ? Mathf.Max(ai.Flight.MaxSpeed, 1f) : 200f;
                far = Mathf.Max(far, d);
                arrive = Mathf.Max(arrive, d / speed);
            }
            if (far < MinDistance * 0.5f || far > MaxDistance * 0.5f || arrive > MaxTime * 0.8f) { return; }
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetGroupStart), center, arrive, mask, incoming);
        }

        /// 모든 화면(정하는 쪽에서): 참가 기체가 한가운데로 모인다 — 높이를 GroupStack씩 달리해 같은 시각에 도착하게.
        [NetworkCallable]
        public void NetGroupStart(Vector3 center, float arrive, int mask, bool incoming)
        {
            if (!HasGroup()) { return; }
            float now = Time.time;
            int n = Mathf.Min(Group.Length, MAX_GROUP);
            int count = 0;
            for (int i = 0; i < n; i++) { if ((mask & (1 << i)) != 0) { count++; } }
            int slot = 0;
            bool first = true;
            for (int i = 0; i < n; i++)
            {
                ACO_CrossManeuver m = Group[i];
                if (m == null) { continue; }
                m.MarkGroupStart(now, incoming);
                if ((mask & (1 << i)) == 0) { continue; }
                float h = (slot - (count - 1) * 0.5f) * GroupStack;
                if (m.BeginGroup(center + Vector3.up * h, now + arrive, first)) { first = false; }
                slot++;
            }
        }

        /// 편대 집결이 시작됐다 — 다음 정기 집결·미사일 반응 간격은 편대가 같이 센다(정하는 쪽이 바뀌어도 이어진다).
        public void MarkGroupStart(float now, bool incoming)
        {
            _next = now + Interval;
            if (incoming) { _lastIncoming = now; }
        }

        /// 이 기체가 집결에 들어간다. 이미 교차 중이거나 죽었으면 false.
        public bool BeginGroup(Vector3 point, float arriveAt, bool lead)
        {
            if (!_wasAlive || _state != 0 || AI == null) { return false; }
            _state = 1;
            _group = true;
            _leadNow = lead;
            _start = Time.time;
            _passSent = false;
            _arriveAt = arriveAt;
            Vector3 flat = point - AI.transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1f) { flat = AI.transform.forward; flat.y = 0f; }
            if (flat.sqrMagnitude < 0.000001f) { flat = Vector3.forward; }
            _approach = flat.normalized;
            _aim = point + Vector3.Cross(Vector3.up, _approach) * Offset;
            if (Listener != null) { Listener.SendCustomEvent("OnCrossStart"); }
            return true;
        }

        /// 오너: 집결 지점으로 — 같이 도착하게 속도를 맞추고, 들거나 지나치면 모두에게 알린다.
        private void SteerGroup()
        {
            Vector3 me = AI.transform.position;
            Vector3 to = _aim - me;
            float dist = to.magnitude;
            float maxSpeed = AI.Flight != null ? AI.Flight.MaxSpeed : 0f;
            float speed = maxSpeed;
            float left = _arriveAt - Time.time;
            if (maxSpeed > 0f && left > 0.5f) { speed = Mathf.Clamp(dist / left, maxSpeed * GroupMinSpeed, maxSpeed); }
            AI.SteerOverride = true;
            AI.OverrideDirection = to;
            AI.OverrideSpeed = speed;
            bool passed = Vector3.Dot(to, _approach) < 0f;
            if (!_passSent && (dist <= GroupPassDistance || passed))
            {
                _passSent = true;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetPass));
            }
        }

        /// 모든 화면: 이 기체가 엇갈렸다(지나갔다) — 뿌리고 잠깐 곧게 난다.
        public void NetPass()
        {
            if (FlareOnPass && AI != null && AI.Flares != null) { AI.Flares.NetFlare(); }
            if (Listener != null) { Listener.SendCustomEvent("OnCrossPass"); }
            if (_state != 0)
            {
                _state = 2;
                _afterUntil = Time.time + AfterTime;
            }
        }

        private void End()
        {
            if (_state != 0 && AI != null) { AI.SteerOverride = false; }
            _state = 0;
            _group = false;
            _leadNow = false;
        }
    }
}
