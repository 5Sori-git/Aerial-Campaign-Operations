using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 에이스 추락(6-145, 동기화 없음 — 격추 이벤트는 이미 모든 화면에 온다): 격추되면 기체 메시를 파티클 입자로 격추 직전 속도 그대로 앞으로 쏜다.
    /// 그 뒤는 파티클이 한다 — 중력·공기 저항으로 포물선, 빙글빙글 돌기, 불·연기 꼬리(태어날 때 하위 방출), 땅에 닿거나 수명이 다하면 폭발(죽을 때 하위 방출).
    /// Udon은 격추 때 Fall() 한 번(+ 살아 있는 동안 속도 재기 — 위치 차 한 줄). 모델은 AI의 HideOnDeath가 지금처럼 숨긴다.
    /// 조각(Pieces): 메시 모드 파티클 하나씩(Setup Check ace-wreck-1이 모델 메시를 합쳐 굽는다). 조각 기준 자세 = 기체 자세(렌더 정렬 Local),
    /// 조각마다 더하는 속도(PieceKick, 기체 기준)·회전(PieceSpin) — 레이저 에이스는 동체와 포드가 따로 떨어진다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_AirWreck : UdonSharpBehaviour
    {
        [Tooltip("속도를 재는 기체(AI 루트)")]
        public Transform Body;
        [Tooltip("조각마다 메시 파티클(메시 모드, 월드 시뮬레이션, 렌더 정렬 Local — 기체 자세 그대로 나온다)")]
        public ParticleSystem[] Pieces;
        [Tooltip("조각마다 더하는 속도(기체 기준, m/s) — 떨어져 나가는 조각에")]
        public Vector3[] PieceKick;
        [Tooltip("조각마다 회전 속도(도/초 — x 피치, y 요, z 롤)")]
        public Vector3[] PieceSpin;
        [Tooltip("추락 시간(초) — 땅에 닿지 않으면 이 뒤 공중에서 터진다")]
        public float FallLife = 8f;
        [Tooltip("잰 속도가 이보다 느리면 기수 방향으로 이 속도(m/s)")]
        public float MinSpeed = 120f;

        private Vector3 _lastPos;
        private Vector3 _vel;
        private bool _has;
        private bool _fallen;

        private void Update()
        {
            if (_fallen || Body == null) { return; }
            float dt = Time.deltaTime;
            Vector3 p = Body.position;
            if (_has && dt > 0.0001f)
            {
                Vector3 raw = (p - _lastPos) / dt;
                if (raw.sqrMagnitude < 1000000f) { _vel = Vector3.Lerp(_vel, raw, 1f - Mathf.Exp(-6f * dt)); }   // 순간이동(부활·풀 이동)은 버린다
            }
            _lastPos = p;
            _has = true;
        }

        /// 격추(AI.Explode — 모든 화면): 조각마다 입자 하나를 쏜다.
        public void Fall()
        {
            if (_fallen || Body == null || Pieces == null) { return; }
            _fallen = true;
            Vector3 v = _vel;
            if (v.magnitude < MinSpeed) { v = Body.forward * MinSpeed; }
            Quaternion rot = Body.rotation;
            for (int i = 0; i < Pieces.Length; i++)
            {
                ParticleSystem ps = Pieces[i];
                if (ps == null) { continue; }
                ps.Clear(true);
                ps.Play(true);
                ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
                ep.position = ps.transform.position;
                ep.applyShapeToPosition = false;
                Vector3 kick = PieceKick != null && i < PieceKick.Length ? PieceKick[i] : Vector3.zero;
                ep.velocity = v + rot * kick;
                ep.angularVelocity3D = PieceSpin != null && i < PieceSpin.Length ? PieceSpin[i] : Vector3.zero;
                ep.startLifetime = FallLife;
                ps.Emit(ep, 1);
            }
        }

        /// 부활: 남은 추락 입자를 지우고 다시 속도를 잰다.
        public void Clear()
        {
            _fallen = false;
            _has = false;
            _vel = Vector3.zero;
            if (Pieces == null) { return; }
            for (int i = 0; i < Pieces.Length; i++)
            {
                if (Pieces[i] == null) { continue; }
                Pieces[i].Clear(true);
                Pieces[i].Stop(true);
            }
        }
    }
}
