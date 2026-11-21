using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 적 재머(6-332, 공용) — 어느 적 유닛에든 붙인다. 살아 있는 동안 반경 Radius 안의 내 기체는 미사일 락온이 LockSpeedFactor배로 느리게 쌓인다.
    /// 스스로는 아무것도 하지 않는다(Update 없음): 허브(ACO_RadarContacts — Bake가 씬의 재머를 모아 넣는다)가 0.5초마다 내 기체 자리로 잰다.
    /// 오브젝트가 꺼져 있거나(풀에서 안 쓰임) 체력이 0이면 효과가 없다. 동기화 없음(유닛 생존은 이미 동기화돼 온다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_EnemyJammer : UdonSharpBehaviour
    {
        [Tooltip("재밍 구역 반경(m)")]
        public float Radius = 4000f;
        [Tooltip("구역 안에서 플레이어 미사일 락온이 쌓이는 속도 배율(겹치면 가장 작은 값 하나)")]
        [Range(0.05f, 1f)] public float LockSpeedFactor = 0.5f;
        [Tooltip("이 유닛의 체력 — Bake가 같은 오브젝트나 부모의 SaccTarget을 넣는다. 비면 살아 있는 것으로 본다")]
        public SaccTarget Health;
        [Tooltip("구역 가운데(비우면 이 오브젝트)")]
        public Transform Anchor;

        /// 지금 재밍 중인가: 켜져 있고 체력이 남았다.
        public bool IsJamming()
        {
            if (!gameObject.activeInHierarchy) { return false; }
            if (Health != null && Health.Health <= 0f) { return false; }
            return true;
        }

        public Vector3 GetCenter()
        {
            return Anchor != null ? Anchor.position : transform.position;
        }
    }
}
