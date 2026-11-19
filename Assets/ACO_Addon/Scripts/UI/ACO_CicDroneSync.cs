using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// CIC 정찰 UAV 동기화(6-278) — CIC 콘솔(ACO_CicConsole)과 따로 둔 Manual 동기화 칸.
    /// 웨이포인트와 같은 칸에 있으면, 두 사람이 거의 동시에 각각 웨이포인트 · UAV를 누를 때 나중 오너가 아직 못 받은 옛 값을 함께 보내
    /// 앞사람 것이 지워졌다(디버깅 2026-11-18). 그래서 UAV 값(출발 · 목표 · 시각 · 작전)만 여기서 보낸다.
    /// 콘솔 프리팹의 자식(항상 켜져 있는 오브젝트 — UAV 모델은 껐다 켰다 하므로 거기 두지 않는다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_CicDroneSync : UdonSharpBehaviour
    {
        [UdonSynced] private int _mission;
        [UdonSynced] private int _ms;
        [UdonSynced] private Vector3 _from;
        [UdonSynced] private Vector3 _to;

        public int GetMission() { return _mission; }
        public int GetMs() { return _ms; }
        public Vector3 GetFrom() { return _from; }
        public Vector3 GetTo() { return _to; }

        /// 누른 사람(로컬)이 오너가 되어 새 경로를 보낸다.
        public void Send(int mission, int ms, Vector3 from, Vector3 to)
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }
            if (!Networking.IsOwner(gameObject)) { Networking.SetOwner(me, gameObject); }
            _mission = mission;
            _ms = ms;
            _from = from;
            _to = to;
            RequestSerialization();
        }
    }
}
