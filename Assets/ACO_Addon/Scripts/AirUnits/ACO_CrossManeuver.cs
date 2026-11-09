using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace ACO
{
    /// 교차 기동(에이스 공통 부품 — NPC 전투기 안의 자식). 짝(Partner — Bake가 잇는다)과 서로를 향해 날아 엇갈리고, 엇갈리는 순간 무언가를 뿌린다.
    /// 분신 에이스 형제(HEADS·TAILS — 기만체+플레어), SHADE 편대(1·2번, 3·4번 — 플레어)가 쓴다.
    ///
    /// - 시작: 정하는 쪽(Leader)의 AI 오너가 Interval마다(등장 FirstDelay 뒤부터, 서로 MinDistance~MaxDistance일 때) 또는 어느 쪽에 미사일이
    ///   IncomingReactDistance 안으로 다가오면(IncomingCooldown 간격) 모두에게 알린다(NetStart). 둘 다 살아 있고 Allowed여야 한다.
    /// - 진행(각 기체 오너가 조종 — AI 조종 덮어쓰기, 동기화는 기체 움직임 그대로): 서로를 향해 최고 속도로, 정하는 쪽은 오른쪽·짝은 왼쪽으로 Offset씩 비켜
    ///   PassDistance 안으로 엇갈리는 순간 오너가 모두에게 알린다(NetPass — FlareOnPass면 플레어, Listener에 OnCrossPass). AfterTime 더 곧게 난 뒤 평소 전투로.
    ///   MaxTime 안에 못 만나면 그만둔다. 짝이 죽거나 Allowed가 꺼지면 그만둔다.
    /// - Listener(선택): 시작 때 OnCrossStart(모든 화면 — 정하는 쪽 먼저, 짝 다음), 엇갈릴 때 OnCrossPass(모든 화면, 그 기체만).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_CrossManeuver : UdonSharpBehaviour
    {
        [Header("참조 (프리팹 빌더/Bake가 넣는다)")]
        public ACO_NPCFighterAI AI;
        [Tooltip("엇갈릴 짝(Bake가 넣는다)")]
        public ACO_CrossManeuver Partner;
        [Tooltip("교차를 정하는 쪽(Bake가 넣는다 — 짝 중 하나만)")]
        public bool Leader;
        [Tooltip("시작·엇갈림 알림을 받는 스크립트(선택) — OnCrossStart, OnCrossPass")]
        public UdonSharpBehaviour Listener;

        [Header("교차")]
        public float Interval = 45f;
        [Tooltip("등장 뒤 첫 교차까지(초)")]
        public float FirstDelay = 20f;
        [Tooltip("서로 이 거리(m) 사이일 때만 교차한다")]
        public float MinDistance = 1000f;
        public float MaxDistance = 6000f;
        [Tooltip("엇갈릴 때 좌우로 비키는 거리(m) — 둘이 반대쪽으로")]
        public float Offset = 60f;
        [Tooltip("이 거리(m) 안으로 엇갈리면 뿌린다")]
        public float PassDistance = 300f;
        [Tooltip("엇갈린 뒤 곧게 나는 시간(초)")]
        public float AfterTime = 3f;
        [Tooltip("이 시간(초) 안에 못 만나면 그만둔다")]
        public float MaxTime = 15f;
        [Tooltip("어느 쪽을 노린 미사일이 이 거리(m) 안이면 간격 전이라도 교차한다")]
        public float IncomingReactDistance = 2500f;
        public float IncomingCooldown = 10f;
        [Tooltip("엇갈리는 순간 플레어를 뿌린다")]
        public bool FlareOnPass = true;

        // 주인 스크립트가 끄면 교차하지 않는다(예: 형제 분노, 코브라 중)
        [System.NonSerialized] public bool Allowed = true;

        private bool _wasAlive;
        private int _state;          // 0 = 평소, 1 = 서로를 향해, 2 = 엇갈린 뒤 곧게
        private float _start;
        private float _afterUntil;
        private float _next;
        private float _lastIncoming = -100f;
        private bool _passSent;
        private float _side = 1f;

        void OnDisable()
        {
            End();
            _wasAlive = false;
        }

        public bool IsAliveNow() { return _wasAlive; }
        public bool IsCrossing() { return _state != 0; }

        private bool Alive() { return AI != null && AI.IsAlive() && (AI.TargetHealth == null || AI.TargetHealth.Health > 0f); }

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
                if (Leader && owner) { TryStart(); }
                return;
            }
            float t = Time.time - _start;
            if (Partner == null || Partner.AI == null || !Partner.IsAliveNow() || !Allowed || !Partner.Allowed || t > MaxTime) { End(); return; }
            if (_state == 1)
            {
                if (!owner) { return; }
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
            _start = Time.time;
            _passSent = false;
            _side = side;
            if (Listener != null) { Listener.SendCustomEvent("OnCrossStart"); }
        }

        /// 모든 화면: 이 기체가 엇갈렸다 — 뿌리고 잠깐 곧게 난다.
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
        }
    }
}
