using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using SaccFlightAndVehicles;

namespace ACO
{
    /// CIC 1인칭 관전 고글(6-275, 로컬 시점). CIC 거치대의 픽업이다(위치만 VRCObjectSync로 맞춘다 — 다른 사람에겐 쓰고 있는 모습만 보인다).
    ///  - VR: 들고 얼굴 가까이(WearDistance) 가져가면 쓴다. 데스크톱: 집으면 바로 쓴다. 놓으면 바로 벗는다.
    ///  - UAV 시점에서 트리거(데스크톱 클릭): 화면 한가운데(조준선)가 가리키는 지면에 CIC 커서를 찍는다(로컬 — CIC 콘솔 DesignateFromView).
    ///  - 쓰는 동안 관전 카메라(ACO_SpectatorCam — 하늘 안개 돔 · 구름 · 비를 그대로 쓴다)를 빌려 고글 렌더 텍스처에 그리고,
    ///    화면 전체를 덮는 Overlay(셰이더 ACO/GogglesView — 내 눈에만)로 보여 준다.
    ///  - 대상: 고글 패널(ACO_CicGogglesPanel)의 관전 대상(로컬) — UAV(정찰 드론, CIC 콘솔이 자리를 계산) 또는 참가자 기체. 기체는 기체 기준 자리(AircraftViewOffset — 캐노피 위 뒤, 6-290)에서 본다.
    ///  - 회수(고글 패널 RESET): 오너 화면이 들고 있으면 놓고 처음 자리로 돌아간다(NetReset).
    ///  - 방향: 대상 자세(롤 · 피치 포함) × 내 머리가 기준에서 돌아간 각도. 기준 = 쓰는 순간(또는 대상을 바꾼 순간) 내 머리의 좌우 방향.
    ///  - 대상이 없으면(드론 없음, 슬롯 빔 · 격추) NO SIGNAL.
    ///  - 함 밖 금지(6-287): 고글이 함내 구역(Interior) 밖으로 나가면(들고 있으면 OutsideGrace초 뒤, 놓인 것은 바로) 오너 화면이 놓고 거치대로 돌려보낸다.
    ///    함 안에서 놓인 고글은 그 자리에 둔다(자동 복귀 없음 — RESET GOGGLES로 돌린다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]   // 회수 이벤트를 받으려고 네트워크는 켠다(위치는 VRCObjectSync)
    public class ACO_CicGoggles : UdonSharpBehaviour
    {
        [Header("연결 (프리팹 빌더 · Bake)")]
        public ACO_CicConsole Console;
        public ACO_CicGogglesPanel Panel;
        public ACO_SpectatorCam Spectator;
        [Tooltip("고글 렌더 텍스처(정사각형) — 화질 HD")]
        public RenderTexture Texture;
        [Tooltip("고글 렌더 텍스처 — 화질 FHD(고글 패널 화질 버튼, 로컬). Udon은 실행 중에 렌더 텍스처를 만들 수 없어 미리 만든 것")]
        public RenderTexture TextureFHD;
        [Tooltip("화면 전체 덮개(FX 레이어, 셰이더 ACO/GogglesView). 두 고글이 같이 쓴다")]
        public Renderer Overlay;
        [Tooltip("NO SIGNAL 글자(내 머리 앞에 띄운다)")]
        public Transform NoSignal;

        [Header("값")]
        public float WearDistance = 0.22f;
        [Tooltip("세로 화각(도) — 렌더 텍스처가 정사각형이라 가로도 같다")]
        public float Fov = 100f;
        [Tooltip("기체 시점: 기체 원점 기준 카메라 자리(m, 기체 축 — 위 Y, 앞 Z). 캐노피 위 뒤(6-290 — 예전엔 조종사 머리 기준이라 아바타 키 · 자세에 따라 달랐다. "
            + "머리 기준 위 2.2 · 뒤 1.4m였던 자리를 F/A-18E 좌석(원점 기준 위 1.71 · 앞 4.44m) + 머리 0.75m로 옮겨 잼 → 1m 아래로 3.66)")]
        public Vector3 AircraftViewOffset = new Vector3(0f, 3.66f, 3.04f);
        [Tooltip("드론 시점: 드론 몸체 기준 카메라 자리(m) — 기수 아래 카메라 포드(Synty SM_Veh_Drone_Camera_01 앞 아래)")]
        public Vector3 DroneOffset = new Vector3(0f, -1f, 4.4f);
        public float NoSignalDistance = 1.2f;
        [Tooltip("함내 구역(6-287) — 고글이 이 밖으로 나가면 거치대로 돌아간다. Bake가 넣는다. 거치대가 이 안에 없으면 규칙을 끈다")]
        public ACO_InteriorZone Interior;
        [Tooltip("들고 함 밖에 이만큼(초) 있으면 놓고 돌아간다(문간에서 손만 잠깐 나간 것은 봐준다). 놓인 고글은 바로")]
        public float OutsideGrace = 0.5f;
        public float InteriorCheckInterval = 0.25f;

        private VRC_Pickup _pickup;
        private VRCObjectSync _sync;
        private Material _overlayMat;
        private bool _held;
        private Vector3 _camPos;
        private Quaternion _camRot = Quaternion.identity;
        private bool _camLive;
        private int _camView = ACO_CicGogglesPanel.VIEW_UAV;
        private bool _wearing;
        private Vector3 _home;
        private bool _homeInside;
        private float _nextCheck;
        private float _outsideAt = -1f;
        private float _refYaw;
        private int _refView = -99;
        private int _poseFrame = -1;
        private Quaternion _poseHead = Quaternion.identity;   // 카메라를 돌릴 때 쓴 내 머리 각도(덮개 셰이더가 실제 눈 방향을 이 기준으로 되돌린다 — 6-290)   // 카메라 자세를 계산한 프레임(PostLateUpdate · 마커가 같은 값을 쓴다 — 6-286)

        void Start()
        {
            _pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            _sync = (VRCObjectSync)GetComponent(typeof(VRCObjectSync));
            _home = transform.position;
            _homeInside = Interior != null && Interior.Contains(_home);
            if (Interior != null && !_homeInside) { Debug.LogWarning("[ACO] CIC 고글: 거치대 자리가 함내 구역 밖이라 함 밖 금지 규칙을 끕니다(6-287).", this); }
            if (Overlay) { _overlayMat = Overlay.material; Overlay.gameObject.SetActive(false); }
            if (NoSignal) { NoSignal.gameObject.SetActive(false); }
        }

        public override void OnPickup() { _held = true; }

        public override void OnDrop() { _held = false; }

        /// 트리거(데스크톱 클릭): UAV 시점이면 조준선이 가리키는 곳에 커서를 찍는다.
        public override void OnPickupUseDown()
        {
            if (!_wearing || !_camLive || _camView != ACO_CicGogglesPanel.VIEW_UAV || Console == null) { return; }
            Console.DesignateFromView(_camPos, _camRot * Vector3.forward);
        }

        void Update()
        {
            // 함 밖 금지(6-287): 오너 화면(들고 있으면 든 사람)이 고글 자리를 함내 구역과 비교한다
            if (!_homeInside || !Networking.IsOwner(gameObject)) { _outsideAt = -1f; return; }
            if (Time.time < _nextCheck) { return; }
            _nextCheck = Time.time + InteriorCheckInterval;
            if (Interior.Contains(transform.position)) { _outsideAt = -1f; return; }
            if (_pickup != null && _pickup.IsHeld)
            {
                if (_outsideAt < 0f) { _outsideAt = Time.time; return; }
                if (Time.time - _outsideAt < OutsideGrace) { return; }
            }
            _outsideAt = -1f;
            NetReset();
        }

        public override void PostLateUpdate()
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }
            bool held = _held && _pickup != null && _pickup.IsHeld && _pickup.currentPlayer != null && _pickup.currentPlayer.isLocal;
            VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            bool want = held && (!me.IsUserInVR() || (transform.position - head.position).sqrMagnitude < WearDistance * WearDistance);
            if (want != _wearing) { SetWearing(want, head.rotation); }
            if (!_wearing) { return; }

            ComputePose();
            bool live = _camLive;
            int view = _camView;
            Vector3 pos = _camPos;
            if (Spectator) { Spectator.ExternalPose(live, pos, _camRot, Fov); }
            if (_overlayMat)
            {
                Quaternion inv = Quaternion.Inverse(_poseHead);
                _overlayMat.SetVector("_HeadInvQ", new Vector4(inv.x, inv.y, inv.z, inv.w));
            }
            if (_overlayMat) { _overlayMat.SetFloat("_Reticle", live && view == ACO_CicGogglesPanel.VIEW_UAV ? 1f : 0f); }
            if (_overlayMat) { _overlayMat.SetFloat("_Live", live ? 1f : 0f); }
            if (NoSignal)
            {
                if (NoSignal.gameObject.activeSelf == live) { NoSignal.gameObject.SetActive(!live); }
                if (!live)
                {
                    Vector3 f = head.rotation * Vector3.forward;
                    NoSignal.SetPositionAndRotation(head.position + f * NoSignalDistance, head.rotation);
                }
            }
        }

        /// 표적 마커(ACO_TargetMarkers, 6-286)가 부른다: 이 고글을 쓰고 대상이 보이면 true — 마커를 이 카메라 기준으로 그린다.
        public bool GetView()
        {
            if (!_wearing) { return false; }
            ComputePose();
            return _camLive;
        }
        public Vector3 GetViewPosition() { return _camPos; }
        public Quaternion GetViewRotation() { return _camRot; }

        /// 고글 카메라 자리 · 방향(이번 프레임 한 번만 — PostLateUpdate와 마커가 같은 값을 쓴다).
        private void ComputePose()
        {
            if (_poseFrame == Time.frameCount) { return; }
            _poseFrame = Time.frameCount;
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { _camLive = false; return; }
            VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            int view = Panel != null ? Panel.GetView() : ACO_CicGogglesPanel.VIEW_UAV;
            if (view != _refView) { _refView = view; _refYaw = head.rotation.eulerAngles.y; }   // 대상을 바꾸면 지금 보는 방향이 새 정면

            bool live = false;
            Vector3 pos = Vector3.zero;
            Quaternion baseRot = Quaternion.identity;
            if (view == ACO_CicGogglesPanel.VIEW_UAV)
            {
                if (Console != null && Console.IsDroneActive())
                {
                    live = true;
                    baseRot = Console.GetDroneRotation();
                    pos = Console.GetDronePosition() + baseRot * DroneOffset;
                }
            }
            else
            {
                SaccEntity e = Vehicle(view);
                if (e != null && e.gameObject.activeInHierarchy && e.Occupied && !e.dead)
                {
                    live = true;
                    baseRot = e.transform.rotation;
                    pos = e.transform.position + baseRot * AircraftViewOffset;   // 기체 기준(아바타 · 조종사 자세와 무관, 6-290)
                }
            }
            // 내 머리가 기준(쓰는 순간의 좌우 방향)에서 돌아간 만큼 대상 기준으로 돌린다
            _poseHead = head.rotation;
            Quaternion rel = Quaternion.Euler(0f, -_refYaw, 0f) * head.rotation;
            _camPos = pos;
            _camRot = baseRot * rel;
            _camLive = live;
            _camView = view;
        }

        private void SetWearing(bool on, Quaternion headRot)
        {
            _wearing = on;
            if (on)
            {
                _refYaw = headRot.eulerAngles.y;
                _refView = Panel != null ? Panel.GetView() : ACO_CicGogglesPanel.VIEW_UAV;
                RenderTexture rt = ActiveTexture();
                if (Spectator) { Spectator.ExternalBegin(rt); }
                if (_overlayMat)
                {
                    _overlayMat.SetTexture("_MainTex", rt);
                    _overlayMat.SetFloat("_TanHalf", Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad));
                }
                if (Overlay) { Overlay.gameObject.SetActive(true); }
                return;
            }
            if (Spectator) { Spectator.ExternalEnd(); }
            if (Overlay) { Overlay.gameObject.SetActive(false); }
            if (NoSignal) { NoSignal.gameObject.SetActive(false); }
        }

        /// 지금 화질의 렌더 텍스처(FHD를 골랐고 있으면 FHD).
        private RenderTexture ActiveTexture()
        {
            return Panel != null && Panel.IsFhd() && TextureFHD != null ? TextureFHD : Texture;
        }

        /// 고글 패널 화질 버튼(로컬): 쓰고 있으면 바로 바꾼다.
        public void ResolutionChanged()
        {
            if (!_wearing) { return; }
            RenderTexture rt = ActiveTexture();
            if (Spectator) { Spectator.ExternalBegin(rt); }
            if (_overlayMat) { _overlayMat.SetTexture("_MainTex", rt); }
        }

        /// 고글 패널 RESET(누른 사람 화면): 오너 화면에 돌아가라고 보낸다.
        public void RequestReset()
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetReset));
        }

        /// 오너 화면: 들고 있으면 놓고 처음 자리로.
        [NetworkCallable]
        public void NetReset()
        {
            if (!Networking.IsOwner(gameObject)) { return; }
            if (_pickup != null && _pickup.IsHeld) { _pickup.Drop(); }
            _held = false;
            _outsideAt = -1f;
            if (_sync) { _sync.Respawn(); }
        }

        private SaccEntity Vehicle(int v)
        {
            ACO_GameFlow flow = Panel != null ? Panel.Flow : (Console != null ? Console.Flow : null);
            if (flow == null || flow.Aircraft == null) { return null; }
            SaccEntity[] a = flow.Aircraft;
            return v >= 0 && v < a.Length ? a[v] : null;
        }
    }
}
