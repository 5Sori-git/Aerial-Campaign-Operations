using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 탑승자 전용 충돌(6-259, 동기화 없음 — 화면마다 따로). 발함 지휘소 구덩이를 덮은 바닥처럼, 기체에는 바닥이어야 하고 걸어 다니는 사람은
    /// 지나가야(구덩이로 내려가야) 하는 충돌에 붙인다. 기체 물리는 소유자(조종사) 화면에서만 계산되므로 화면마다 켜고 끄면 된다.
    ///  - 켬: 내가 기체를 조종 중일 때(ACO_VehicleAutomation이 탈 때 · 내릴 때 알린다 — 즉시).
    ///  - 걸어 다닐 때도 켬: 내가 소유한 플레이어 기체가 이 오브젝트에서 NearDistance 안에 있으면 — 내린 빈 기체가 내 화면에서 구덩이로 빠지지 않게.
    ///    걸어 다니는 동안만 CheckInterval마다 본다(내린 순간에는 바로 한 번).
    ///  - 그 밖엔 끔 — 걸어서 지나간다.
    /// Bake가 Aircraft(플레이어 기체)와 기체 자동화의 PilotOnly를 잇는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PilotOnlyColliders : UdonSharpBehaviour
    {
        [Tooltip("켜고 끌 충돌(비우면 같은 오브젝트의 충돌)")]
        public Collider[] Colliders;
        [Tooltip("플레이어 기체(Bake가 채운다) — 내가 소유한 기체가 가까이 있으면 걸어 다녀도 켜 둔다")]
        public SaccEntity[] Aircraft;
        [Tooltip("내 소유 기체가 이 거리(m) 안이면 켜 둔다")]
        public float NearDistance = 15f;
        [Tooltip("걸어 다닐 때 가까운 내 기체를 확인하는 주기(초)")]
        public float CheckInterval = 0.5f;

        private bool _piloting;
        private bool _nearOwned;
        private bool _applied;
        private bool _on;
        private float _timer;

        void Start()
        {
            if (Colliders == null || Colliders.Length == 0) { Colliders = GetComponents<Collider>(); }
            _nearOwned = OwnedAircraftNear();
            Apply();
        }

        /// 내가 기체를 조종하기 시작했다 / 내렸다(ACO_VehicleAutomation — 조종사 화면).
        public void SetLocalPiloting(bool on)
        {
            _piloting = on;
            if (!on)
            {
                _nearOwned = OwnedAircraftNear();
                _timer = CheckInterval;
            }
            Apply();
        }

        void Update()
        {
            if (_piloting) { return; }
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = CheckInterval;
            _nearOwned = OwnedAircraftNear();
            Apply();
        }

        private bool OwnedAircraftNear()
        {
            if (Aircraft == null) { return false; }
            Vector3 p = transform.position;
            float r2 = NearDistance * NearDistance;
            for (int i = 0; i < Aircraft.Length; i++)
            {
                SaccEntity e = Aircraft[i];
                if (!e || !Networking.IsOwner(e.gameObject)) { continue; }
                if ((e.transform.position - p).sqrMagnitude <= r2) { return true; }
            }
            return false;
        }

        private void Apply()
        {
            bool on = _piloting || _nearOwned;
            if (_applied && on == _on) { return; }
            _applied = true;
            _on = on;
            if (Colliders == null) { return; }
            for (int i = 0; i < Colliders.Length; i++) { if (Colliders[i]) { Colliders[i].enabled = on; } }
        }
    }
}
