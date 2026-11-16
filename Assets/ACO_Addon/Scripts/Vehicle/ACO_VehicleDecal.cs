using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 실기체 데칼(플레이어 기체마다, 수동 동기화 — 오너 = 조종사): 조종석에 앉으면(ACO_VehicleUpgrades가 LocalPilotEnter) 내 데칼 이미지 URL(스튜디오, 이번 접속)과
    /// 저장된 자리 2개(기종 DecalKey — 샘플 기준 상대값 = 이 기체 기준점 기준 상대값)를 동기화 값으로 보낸다. 내리면 끈다.
    /// 모든 클라이언트: 받은 자리에 프로젝터 2개(이 기체 기준점 자식)를 놓고, 스튜디오가 받아 둔 이미지(대기열·캐시)를 머티리얼에 넣어 켠다.
    /// 다른 사람 데칼은 스튜디오의 HIDE OTHERS가 켜져 있으면 숨긴다. 이미지를 못 받으면 꺼 두고 계속 기다린다 —
    /// 스튜디오가 다른 사람 이미지를 시간을 두고 다시 받아(6-267) 받아지면 그때 켠다. 캐시에서 밀려난 이미지도 다시 부탁한다.
    /// 라운델(6-191): 내 라운델 이미지 URL(스튜디오 ROUNDEL)도 같이 보낸다 — 자리는 고정(기체에 붙은 판 Roundels — 동체 양옆, 왼쪽 날개 위, 오른쪽 날개 아래).
    /// 이미지가 없으면 안 보인다. 판들은 처음 켤 때 머티리얼 하나를 복제해 같이 쓴다(기체마다 따로 — 프리팹 머티리얼을 건드리지 않는다).
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
        [Tooltip("라운델 판(고정 자리, 기체 표면에 맞춰 구운 메시 — 생성 코드가 넣는다). 비면 라운델 없음")]
        public Renderer[] Roundels;

        [UdonSynced] private VRCUrl _url = VRCUrl.Empty;    // 데칼 1
        [UdonSynced] private VRCUrl _url1 = VRCUrl.Empty;   // 데칼 2
        [UdonSynced] private Vector3 _p0;
        [UdonSynced] private Vector3 _p1;
        [UdonSynced] private Quaternion _r0 = Quaternion.identity;
        [UdonSynced] private Quaternion _r1 = Quaternion.identity;
        [UdonSynced] private float _s0 = 1f;
        [UdonSynced] private float _s1 = 1f;
        [UdonSynced] private byte _flags;   // 1 켜짐, 2 데칼1, 4 데칼2, 8 뒤집기1, 16 뒤집기2, 32 라운델, 64 조종사 사양 있음(6-194)
        [UdonSynced] private int _pilotId;  // 사양을 보낸 조종사(playerId) — 자유 비행 빈 기체의 표시(헐넘버 · 콜사인 · 킬마크)가 읽는다
        [UdonSynced] private VRCUrl _url2 = VRCUrl.Empty;   // 라운델

        private const int FLAG_ROUNDEL = 32;
        private const int FLAG_PILOT = 64;
        private const int ROUNDEL = 2;   // 이미지 칸 번호(0·1 데칼, 2 라운델)
        private string[] _appliedUrl = new string[] { "", "", "" };
        private bool[] _waiting = new bool[3];   // 보여야 하는데 이미지가 아직(실패해도 계속 — 스튜디오가 다른 사람 이미지를 다시 받는다, 6-267)
        private bool[] _texReady = new bool[3];
        private Material _roundelMat;
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
            VRCUrl url2 = Studio.GetRoundelUrl();
            bool on2 = Roundels != null && Roundels.Length > 0 && url2 != null && !string.IsNullOrEmpty(url2.Get());
            byte f = FLAG_PILOT;   // 데칼이 없어도 — 표시(ACO_VehicleMarks)가 마지막 조종사를 안다
            _pilotId = me.playerId;
            if (on0 || on1 || on2) { f |= 1; }
            if (on2) { f |= FLAG_ROUNDEL; }
            if (on0) { f |= 2; _p0 = Studio.GetPos(DecalKey, 0); _r0 = Studio.GetRot(DecalKey, 0); _s0 = Studio.GetSize(DecalKey, 0); if (Studio.GetFlip(DecalKey, 0)) { f |= 8; } }
            if (on1) { f |= 4; _p1 = Studio.GetPos(DecalKey, 1); _r1 = Studio.GetRot(DecalKey, 1); _s1 = Studio.GetSize(DecalKey, 1); if (Studio.GetFlip(DecalKey, 1)) { f |= 16; } }
            _flags = f;
            _url = on0 ? url0 : VRCUrl.Empty;
            _url1 = on1 ? url1 : VRCUrl.Empty;
            _url2 = on2 ? url2 : VRCUrl.Empty;
            RequestSerialization();
            Apply();
        }

        /// 내가 내렸다: 끈다.
        public void LocalPilotExit()
        {
            if (!Networking.IsOwner(gameObject)) { return; }
            _flags = 0;
            _pilotId = 0;
            _url = VRCUrl.Empty;
            _url1 = VRCUrl.Empty;
            _url2 = VRCUrl.Empty;
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
            bool roundel = active && (_flags & FLAG_ROUNDEL) != 0;
            Prepare(ROUNDEL, _url2, roundel);
            SetRoundel(roundel && _texReady[ROUNDEL]);
        }

        private VRCUrl UrlOf(int i) { return i == 0 ? _url : (i == 1 ? _url1 : _url2); }

        /// 라운델 판을 켜고 끈다. 처음 켤 때 첫 판의 머티리얼을 복제해 모든 판에 같이 준다(기체마다 하나).
        private void SetRoundel(bool show)
        {
            if (Roundels == null || Roundels.Length == 0) { return; }
            if (show && Studio != null)
            {
                Texture2D tex = Studio.GetTexture(_url2);
                if (tex == null) { show = false; }
                else
                {
                    if (_roundelMat == null && Roundels[0] != null)
                    {
                        _roundelMat = Roundels[0].material;
                        for (int i = 1; i < Roundels.Length; i++) { if (Roundels[i]) { Roundels[i].sharedMaterial = _roundelMat; } }
                    }
                    if (_roundelMat != null) { _roundelMat.SetTexture("_MainTex", tex); }
                }
            }
            for (int i = 0; i < Roundels.Length; i++) { if (Roundels[i]) { Roundels[i].enabled = show; } }
        }

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
            if (!wanted || u.Length == 0 || _texReady[i] || Studio == null)
            {
                if (!wanted) { _waiting[i] = false; }
                return;
            }
            if (Studio.GetTexture(url) != null) { _texReady[i] = true; _waiting[i] = false; }
            else
            {
                Studio.Request(url);   // 이미 있거나(받는 중 · 실패 포함) 대기 중이면 그대로
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
            if (!_waiting[0] && !_waiting[1] && !_waiting[ROUNDEL] || Studio == null) { return; }
            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) { return; }
            _pollTimer = 0.5f;
            bool changed = false;
            for (int i = 0; i < 3; i++)
            {
                if (!_waiting[i]) { continue; }
                VRCUrl url = UrlOf(i);
                if (Studio.GetTexture(url) != null)
                {
                    _waiting[i] = false;
                    _texReady[i] = true;
                    changed = true;
                }
                else
                {
                    // 실패로 남아도 기다린다(스튜디오가 시간을 두고 다시 받는다). 캐시에서 밀려났으면 다시 부탁한다
                    Studio.Request(url);
                }
            }
            if (changed) { Apply(); }
        }

        /// 사양을 남겨 둔 조종사(playerId, 없으면 0) — 앉으면 켜지고, 미션에서 내리거나 폭발 · 리스폰 · 회수되면 꺼진다(자유 비행은 내려도 남는다, 6-194).
        public int KeptPilotId() { return (_flags & FLAG_PILOT) != 0 ? _pilotId : 0; }

        /// 다른 사람 데칼 숨기기를 바꿨을 때 스튜디오가 부른다.
        public void Refresh() { Apply(); }
    }
}
