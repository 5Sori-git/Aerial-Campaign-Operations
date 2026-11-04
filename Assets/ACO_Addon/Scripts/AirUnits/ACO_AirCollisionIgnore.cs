using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 공중 충돌 방지(전투기 밖의 공중 유닛 — 헬기·폭격기·수송기·아군 공격헬기, 동기화 없음). Bake가 유닛 루트에 붙이고 채운다.
    /// 전투기(ACO_NPCFighterAI)와 같은 규칙: 레이어·충돌 매트릭스는 그대로 두고 Physics.IgnoreCollision으로 내 조종 기체 콜라이더와 이 유닛 콜라이더 쌍만 끈다 —
    /// 충돌 데미지는 기체 오너(조종사) 화면에서만 계산하므로 각 클라이언트는 자기가 조종 중인 기체만 건다. 기총·미사일·범위 피해·지형 충돌은 그대로.
    ///  - 거는 때: 켜질 때(OnEnable — 풀에서 배치), 탑승할 때(기체 쪽 ACO_VehicleUpgrades가 OtherAirUnits를 한 대씩), 그리고 RefreshInterval마다
    ///    내 기체가 NearDistance 안에 있을 때 다시 — 이 유닛들은 격파·부활 때 콜라이더를 껐다 켜서(애니메이터·체력) 무시 상태가 풀릴 수 있다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_AirCollisionIgnore : UdonSharpBehaviour
    {
        [Tooltip("이 유닛의 충돌 콜라이더(트리거·바퀴 제외) — Bake가 채운다")]
        public Collider[] BodyColliders;
        [Tooltip("플레이어 기체 강화 확장 — Bake가 채운다(내가 조종 중인 기체를 찾는다)")]
        public ACO_VehicleUpgrades[] PlayerVehicles;
        [Tooltip("다시 거는 주기(초)와 거리(m) — 내 기체가 이 거리 안일 때만")]
        public float RefreshInterval = 1f;
        public float NearDistance = 2500f;

        private float _timer;

        void OnEnable()
        {
            IgnorePlayerCollisions();
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = RefreshInterval;
            ACO_VehicleUpgrades v = LocalVehicle();
            if (v == null) { return; }
            if ((v.transform.position - transform.position).sqrMagnitude > NearDistance * NearDistance) { return; }
            IgnoreWith(v.BodyColliders);
        }

        private ACO_VehicleUpgrades LocalVehicle()
        {
            if (PlayerVehicles == null) { return null; }
            for (int i = 0; i < PlayerVehicles.Length; i++)
            {
                ACO_VehicleUpgrades v = PlayerVehicles[i];
                if (v && v.IsLocalPilot()) { return v; }
            }
            return null;
        }

        /// 내가 조종 중인 기체와 지금 바로 건다(조종 중이 아니면 할 일 없음).
        public void IgnorePlayerCollisions()
        {
            ACO_VehicleUpgrades v = LocalVehicle();
            if (v != null) { IgnoreWith(v.BodyColliders); }
        }

        /// 이 유닛의 충돌 콜라이더와 others 사이의 충돌을 끈다(이 클라이언트에서만).
        public void IgnoreWith(Collider[] others)
        {
            if (BodyColliders == null || others == null) { return; }
            for (int i = 0; i < BodyColliders.Length; i++)
            {
                Collider a = BodyColliders[i];
                if (!a) { continue; }
                for (int j = 0; j < others.Length; j++)
                {
                    if (others[j]) { Physics.IgnoreCollision(a, others[j], true); }
                }
            }
        }
    }
}
