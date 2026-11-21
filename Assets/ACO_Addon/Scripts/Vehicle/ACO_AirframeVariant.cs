using SaccFlightAndVehicles;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace ACO
{
    /// 기체 형식 스위치(6-343): F/A-18E(1인승) ↔ F/A-18F(2인승). 같은 기체 안에 E · F 부품 묶음을 둘 다 두고 하나만 켠다(뼈대 같음 — 캐노피 · 착륙장치 동작 그대로).
    ///  - 동기화: 형식 하나(Manual). 바꾼 사람이 오너가 되어 보낸다 — 모든 화면 · 늦게 들어온 사람이 같은 형식.
    ///  - 바꾸기(ACO_GameFlow — 로스터 그 슬롯을 채운 사람만, 조종석이 비었을 때): 뒷자리에 승객이 있으면 먼저 강제로 내려 브리핑실로 보내고, 좌석이 빈 뒤 바꾼다(최대 WaitSeconds).
    ///  - 승객 강제 하차(ForceExitPassengers — 모든 화면에 보내면 그 좌석에 앉은 사람만 자기 화면에서 내리고 브리핑실로): 형식 전환 · 보관 장소로 되돌리기 · 기체 폭발 때.
    ///  - SAV 기체와 동기화가 섞이지 않게 기체의 자식 오브젝트에 따로 둔다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_AirframeVariant : UdonSharpBehaviour
    {
        public const byte TYPE_E = 0;
        public const byte TYPE_F = 1;

        [Header("참조 (설치 도구 · Bake가 넣는다)")]
        public SaccEntity Entity;
        public ACO_GameFlow Flow;
        [Tooltip("E형(1인승)일 때 켜는 부품")]
        public GameObject[] EObjects;
        [Tooltip("F형(2인승)일 때 켜는 부품(동체 · 캐노피 · 조종석 · 착륙장치 · 좌석 · 뒷자리 좌석 모델)")]
        public GameObject[] FObjects;
        [Tooltip("승객석 오브젝트(켜고 끄지 않는다 — 기체 애니메이터의 Respawn 상태가 매 프레임 켜 둔다)")]
        public GameObject PassengerSeat;
        [Tooltip("승객석 상호작용 콜라이더 — F형일 때만 켠다(E형은 뒷자리에 탈 수 없다)")]
        public Collider PassengerCollider;
        [Tooltip("승객이 내리기를 기다리는 최대 시간(초) — 넘으면 바꾸지 않는다")]
        public float WaitSeconds = 3f;

        [UdonSynced] private byte _type = TYPE_E;
        private byte _applied = 255;
        private byte _pending = 255;
        private float _waitUntil;

        void Start() { Apply(); }

        public byte GetAirframeType() { return _type; }
        public bool IsTwoSeat() { return _type == TYPE_F; }

        public override void OnDeserialization() { Apply(); }

        /// 형식 전환 요청(게임 진행이 권한을 확인한 뒤 부른다). 조종석이 차 있으면 false.
        public bool RequestToggle()
        {
            if (Entity == null || Entity.Occupied || Entity.dead) { return false; }
            byte want = _type == TYPE_F ? TYPE_E : TYPE_F;
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            if (HasPassenger())
            {
                // 승객을 먼저 내리게 하고, 좌석이 비면 바꾼다
                _pending = want;
                _waitUntil = Time.time + WaitSeconds;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ForceExitPassengers));
                SendCustomEventDelayedSeconds(nameof(_TryApplyPending), 0.3f);
                return true;
            }
            SetType(want);
            return true;
        }

        public void _TryApplyPending()
        {
            if (_pending == 255) { return; }
            // 기다리는 동안 조종석이 찼거나 기체가 파괴됐으면 취소(조종사 밑에서 형식이 바뀌지 않게)
            if (Entity == null || Entity.Occupied || Entity.dead) { _pending = 255; Failed(); return; }
            if (!HasPassenger()) { byte t = _pending; _pending = 255; SetType(t); return; }
            if (Time.time >= _waitUntil) { _pending = 255; Debug.LogWarning("[ACO] 기체 형식: 승객이 내리지 않아 바꾸지 않았습니다 — " + (Entity ? Entity.gameObject.name : name)); Failed(); return; }
            SendCustomEventDelayedSeconds(nameof(_TryApplyPending), 0.3f);
        }

        private void Failed()
        {
            if (Flow) { Flow.AirframeSwitchFailed(); }
        }

        private void SetType(byte t)
        {
            if (!Networking.IsOwner(gameObject)) { Networking.SetOwner(Networking.LocalPlayer, gameObject); }
            _type = t;
            RequestSerialization();
            Apply();
        }

        /// 형식대로 부품 묶음 · 승객석을 켜고 끈다(바뀌었을 때만).
        private void Apply()
        {
            if (_applied == _type) { return; }
            // 내가 승객석에 앉아 있는데 E로 바뀌었다(드묾 — 보통은 먼저 내린다) → 먼저 내린다
            if (_type != TYPE_F && IsLocalPassenger()) { ForceExitPassengers(); }
            _applied = _type;
            bool f = _type == TYPE_F;
            SetAll(EObjects, !f);
            SetAll(FObjects, f);
            // 승객석 오브젝트는 애니메이터가 켜 두므로 상호작용 콜라이더만 켜고 끈다
            if (PassengerCollider && PassengerCollider.enabled != f) { PassengerCollider.enabled = f; }
        }

        private void SetAll(GameObject[] list, bool on)
        {
            if (list == null) { return; }
            for (int i = 0; i < list.Length; i++) { if (list[i] && list[i].activeSelf != on) { list[i].SetActive(on); } }
        }

        /// 뒷자리(승객석)의 좌석 번호(SaccEntity.VehicleSeats) — 없으면 -1.
        public int RearSeatIndex()
        {
            if (Entity == null || Entity.VehicleSeats == null) { return -1; }
            SaccVehicleSeat[] seats = Entity.VehicleSeats;
            for (int i = 0; i < seats.Length; i++)
            {
                if (i == Entity.PilotSeat || !seats[i]) { continue; }
                if (PassengerSeat == null || seats[i].gameObject == PassengerSeat) { return i; }
            }
            return -1;
        }

        /// 뒷자리에 앉은 플레이어 ID — 비었으면 -1.
        public int RearPlayerId()
        {
            int i = RearSeatIndex();
            if (i < 0 || Entity.SeatedPlayers == null || i >= Entity.SeatedPlayers.Length) { return -1; }
            return Entity.SeatedPlayers[i];
        }

        /// 출격한 F형(조종석에 사람)이고 뒷자리가 비었는가(6-344 — 브리핑 패널에서 탑승).
        public bool CanBoardRear()
        {
            return IsTwoSeat() && Entity != null && Entity.Occupied && !Entity.dead && RearSeatIndex() >= 0 && RearPlayerId() < 0;
        }

        /// 이 화면의 나를 뒷자리에 앉힌다(조건은 부르는 쪽이 확인). 멀리서도 좌석으로 옮겨진다.
        public void BoardRear()
        {
            int i = RearSeatIndex();
            if (i >= 0) { Entity.VehicleSeats[i].Interact(); }
        }

        /// 조종석 말고 다른 좌석에 누가 앉아 있는가.
        public bool HasPassenger()
        {
            if (Entity == null || Entity.SeatedPlayers == null) { return false; }
            int[] seated = Entity.SeatedPlayers;
            for (int i = 0; i < seated.Length; i++) { if (i != Entity.PilotSeat && seated[i] >= 0) { return true; } }
            return false;
        }

        /// 이 화면의 내가 이 기체의 승객석(조종석이 아닌 좌석)에 앉아 있는가.
        public bool IsLocalPassenger()
        {
            return Entity != null && Entity.MySeat > -1 && Entity.MySeat != Entity.PilotSeat;
        }

        /// 모든 화면에 보낸다: 이 기체의 승객석에 앉은 사람만 자기 화면에서 내리고 브리핑실로(암전).
        public void ForceExitPassengers()
        {
            if (!IsLocalPassenger()) { return; }
            if (Flow) { Flow.ForceToBriefing(); }
            else { Entity.ExitStation(); }   // 게임 진행이 연결되지 않았으면(Bake 전) 좌석에서만 내린다
        }
    }
}
