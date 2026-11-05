using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 발함지휘소의 조작 요원. 캐터펄트 절차를 보고 동작만 바꾼다(동기화 없음 — 각 화면의 요원 상태를 읽는다).
    /// 평소에는 패널을 만지고, 어느 캐터펄트든 고정되면 패널 조작, 발함 신호부터 사출까지는 버튼을 누른다.
    /// 갑판 요원 표시를 끄면(HUD 설정 DECK CREW) 같이 사라지고, 내가 멀면 끈다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_LaunchOperator : UdonSharpBehaviour
    {
        private const string PARAM = "Signal";
        private const float TICK = 0.2f;

        [Tooltip("캐터펄트 요원들 — Bake가 채운다")]
        public ACO_CatapultCrew[] Crews;
        public Animator Operator;
        [Tooltip("내가 이 거리(m) 밖이면 끈다")]
        public float CullRange = 600f;

        private float _tick;
        private int _sig = -1;

        void Update()
        {
            _tick -= Time.deltaTime;
            if (_tick > 0f) { return; }
            _tick = TICK;
            if (Operator == null) { return; }
            VRCPlayerApi me = Networking.LocalPlayer;
            bool show = Utilities.IsValid(me) && (me.GetPosition() - transform.position).sqrMagnitude < CullRange * CullRange;
            int stage = 0;
            if (Crews != null)
            {
                for (int i = 0; i < Crews.Length; i++)
                {
                    if (Crews[i] == null) { continue; }
                    show = show && Crews[i].IsShown();
                    stage = Mathf.Max(stage, Crews[i].GetStage());
                }
            }
            GameObject go = Operator.gameObject;
            if (go.activeSelf != show)
            {
                go.SetActive(show);
                _sig = -1;   // 다시 켜지면 애니메이터 값이 처음으로 돌아간다
            }
            if (!show) { return; }
            int signal = stage == ACO_CatapultCrew.STAGE_LAUNCH ? ACO_CatapultCrew.SIG_PRESS : ACO_CatapultCrew.SIG_OPERATE;
            if (signal == _sig) { return; }
            _sig = signal;
            Operator.SetInteger(PARAM, signal);
        }
    }
}
