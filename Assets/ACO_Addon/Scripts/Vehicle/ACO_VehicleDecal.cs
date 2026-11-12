using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 실기체 데칼(플레이어 기체마다, 수동 동기화 — 오너 = 조종사): 조종석에 앉으면(ACO_VehicleUpgrades가 LocalPilotEnter) 내 데칼 이미지 URL(스튜디오, 이번 접속)과
    /// 저장된 자리 2개(기종 DecalKey — 샘플 기준 상대값 = 이 기체 기준점 기준 상대값)를 동기화 값으로 보낸다. 내리면 끈다.
    /// 모든 클라이언트: 받은 자리에 프로젝터 2개(이 기체 기준점 자식)를 놓고, 스튜디오가 받아 둔 이미지(대기열·캐시)를 머티리얼에 넣어 켠다.
    /// 다른 사람 데칼은 스튜디오의 HIDE OTHERS가 켜져 있으면 숨긴다. 이미지를 못 받으면 끈다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_VehicleDecal : UdonSharpBehaviour
    {
        [Tooltip("기종 — 격납고 스튜디오 DecalKey와 같으면 그 샘플에서 정한 자리를 쓴다")]
        public string DecalKey = "default";
        [Tooltip("격납고 데칼 스튜디오(Bake가 넣는다 — 이미지 받기·내 저장 자리)")]
        public ACO_DecalStudio Studio;
        [Tooltip("프로젝터 2개(기체 기준점 자식)와 각 머티리얼(프로젝터마다 따로 — Bake가 만든다)")]
        public Transform[] Projectors;
        public Material[] Materials;

        [UdonSynced] private VRCUrl _url = VRCUrl.Empty;    // 데칼 1
        [UdonSynced] private VRCUrl _url1 = VRCUrl.Empty;   // 데칼 2
        [UdonSynced] private Vector3 _p0;
        [UdonSynced] private Vector3 _p1;
        [UdonSynced] private Quaternion _r0 = Quaternion.identity;
        [UdonSynced] private Quaternion _r1 = Quaternion.identity;
        [UdonSynced] private float _s0 = 1f;
        [UdonSynced] private float _s1 = 1f;
        [UdonSynced] private byte _flags;   // 1 켜짐, 2 데칼1, 4 데칼2, 8 뒤집기1, 16 뒤집기2

        private string[] _appliedUrl = new string[] { "", "" };
        private bool[] _waiting = new bool[2];
        private bool[] _texReady = new bool[2];
        private float _pollTimer;

        void Start() { Apply(); }

        /// 내가 이 기체에 탔다: 내 데칼을 보낸다(이미지 URL이 없으면 끈다).
        public void LocalPilotEnter()
        {
            if (Studio == null) { return; }
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }
            Networking.SetOwner(me, gameObject);
            VRCUrl url0 = Studio.GetUrl(0);
            VRCUrl url1 = Studio.GetUrl(1);
            bool on0 = Studio.HasPose(DecalKey, 0) && url0 != null && !string.IsNullOrEmpty(url0.Get());
            bool on1 = Studio.HasPose(DecalKey, 1) && url1 != null && !string.IsNullOrEmpty(url1.Get());
            byte f = 0;
            if (on0 || on1) { f |= 1; }
            if (on0) { f |= 2; _p0 = Studio.GetPos(DecalKey, 0); _r0 = Studio.GetRot(DecalKey, 0); _s0 = Studio.GetSize(DecalKey, 0); if (Studio.GetFlip(DecalKey, 0)) { f |= 8; } }
            if (on1) { f |= 4; _p1 = Studio.GetPos(DecalKey, 1); _r1 = Studio.GetRot(DecalKey, 1); _s1 = Studio.GetSize(DecalKey, 1); if (Studio.GetFlip(DecalKey, 1)) { f |= 16; } }
            _flags = f;
            _url = on0 ? url0 : VRCUrl.Empty;
            _url1 = on1 ? url1 : VRCUrl.Empty;
            RequestSerialization();
            Apply();
        }

        /// 내가 내렸다: 끈다.
        public void LocalPilotExit()
        {
            if (!Networking.IsOwner(gameObject)) { return; }
            _flags = 0;
            _url = VRCUrl.Empty;
            _url1 = VRCUrl.Empty;
            RequestSerialization();
            Apply();
        }

        public override void OnDeserialization() { Apply(); }

        private void Apply()
        {
            bool active = (_flags & 1) != 0;
            bool mine = Networking.IsOwner(gameObject);
            if (active && !mine && Studio != null && Studio.HideOthers()) { active = false; }
            Prepare(0, _url, active && (_flags & 2) != 0);
            Prepare(1, _url1, active && (_flags & 4) != 0);
            SetOne(0, active && (_flags & 2) != 0, _p0, _r0, _s0, (_flags & 8) != 0);
            SetOne(1, active && (_flags & 4) != 0, _p1, _r1, _s1, (_flags & 16) != 0);
        }

        private VRCUrl UrlOf(int i) { return i == 0 ? _url : _url1; }

        // 보여야 하는데 이미지가 아직이면: 받아 둔 게 있으면 바로, 없으면 스튜디오에 부탁하고 기다린다
        private void Prepare(int i, VRCUrl url, bool wanted)
        {
            string u = url != null ? url.Get() : "";
            if (u != _appliedUrl[i])
            {
                _appliedUrl[i] = u;
                _texReady[i] = false;
                _waiting[i] = false;
            }
            if (!wanted || u.Length == 0 || _texReady[i] || Studio == null) { return; }
            if (Studio.GetTexture(url) != null) { _texReady[i] = true; }
            else if (!_waiting[i] && !Studio.IsFailed(url))
            {
                Studio.Request(url);
                _waiting[i] = true;
            }
        }

        private void SetOne(int i, bool on, Vector3 p, Quaternion r, float s, bool flip)
        {
            if (Projectors == null || i >= Projectors.Length || Projectors[i] == null) { return; }
            Material m = Materials != null && i < Materials.Length ? Materials[i] : null;
            Transform t = Projectors[i];
            t.localPosition = p;
            t.localRotation = r;
            bool show = on && _texReady[i];
            // 안 보이는 프로젝터도 기체를 다시 그린다 — 오브젝트째 끈다
            t.gameObject.SetActive(show);
            if (m == null) { return; }
            m.SetFloat("_Size", s);
            m.SetFloat("_Flip", flip ? 1f : 0f);
            if (show && Studio != null)
            {
                Texture2D tex = Studio.GetTexture(UrlOf(i));
                if (tex != null) { m.SetTexture("_MainTex", tex); }
            }
            m.SetFloat("_Visible", show ? 1f : 0f);
        }

        void Update()
        {
            if (!_waiting[0] && !_waiting[1] || Studio == null) { return; }
            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) { return; }
            _pollTimer = 0.5f;
            bool changed = false;
            for (int i = 0; i < 2; i++)
            {
                if (!_waiting[i]) { continue; }
                VRCUrl url = UrlOf(i);
                if (Studio.GetTexture(url) != null)
                {
                    _waiting[i] = false;
                    _texReady[i] = true;
                    changed = true;
                }
                else if (Studio.IsFailed(url))
                {
                    _waiting[i] = false;
                    _texReady[i] = false;
                    changed = true;
                }
            }
            if (changed) { Apply(); }
        }

        /// 다른 사람 데칼 숨기기를 바꿨을 때 스튜디오가 부른다.
        public void Refresh() { Apply(); }
    }
}
