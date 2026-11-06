using System;
using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 벽시계(로컬 전용, 동기화 없음). 보는 사람 컴퓨터의 현재 시각으로 바늘을 돌린다.
    /// 바늘 메시의 원점이 회전축이고 12시 방향을 가리킨다. 시계 앞면이 로컬 +Z를 보므로, 앞에서 볼 때 시계 방향이 로컬 Z축의 양수 회전이다.
    /// 1초마다 한 번만 갱신한다. 시계가 꺼져 있으면 멈췄다가 켜질 때 다시 시작한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_WallClock : UdonSharpBehaviour
    {
        [Tooltip("시침, 분침, 초침. 원점이 회전축이어야 한다")]
        public Transform HourHand;
        public Transform MinuteHand;
        public Transform SecondHand;

        private bool _running;

        void OnEnable()
        {
            if (_running) { return; }
            _running = true;
            Tick();
        }

        void OnDisable() { _running = false; }

        public void Tick()
        {
            if (!_running) { return; }
            DateTime now = DateTime.Now;
            float seconds = now.Second;
            float minutes = now.Minute + seconds / 60f;
            float hours = now.Hour % 12 + minutes / 60f;
            if (HourHand) { HourHand.localRotation = Quaternion.Euler(0f, 0f, hours * 30f); }
            if (MinuteHand) { MinuteHand.localRotation = Quaternion.Euler(0f, 0f, minutes * 6f); }
            if (SecondHand) { SecondHand.localRotation = Quaternion.Euler(0f, 0f, seconds * 6f); }
            SendCustomEventDelayedSeconds(nameof(Tick), 1f);
        }
    }
}
