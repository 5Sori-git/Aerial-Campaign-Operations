using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 플레이어별 URL 영구 저장소(6-153). PlayerData는 VRCUrl을 저장하지 못하고 스크립트는 문자열로 URL을 만들 수 없어, Player Object
    /// (VRCPlayerObject + VRCEnablePersistence — 플레이어마다 VRChat이 이 템플릿을 복제하고 동기화 변수를 저장한다)에 VRCUrl 칸을 둔다.
    ///  - 칸은 처음부터 넉넉히 SLOT_COUNT(16)개로 고정한다 — 나중에 동기화 필드를 더하면 저장 데이터가 깨진다는 제보가 있어 늘리지 않는다.
    ///    칸 번호는 저장 ID다(바꾸거나 다른 용도로 다시 쓰지 않는다): 0~3 데칼 이미지(지금 0·1), 4~15 비워 둠(개인 BGM, 선원실 TV 등 나중 용도).
    ///  - 쓰는 건 그 플레이어(오너)뿐이고, 복원(OnPlayerRestored) 뒤에만 쓴다(그 전에 쓰면 저장 값이 덮어쓴다).
    ///  - 복원되면 Studio(데칼 스튜디오 — Bake가 템플릿에 넣는다, 복제본도 같은 참조)에 자신을 알린다 → 스튜디오가 저장된 이미지를 다시 받는다.
    ///  - 빈 칸은 VRCUrl.Empty(null이면 ClientSim에서 오류가 난다는 제보).
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_UrlStore : UdonSharpBehaviour
    {
        public const int SLOT_COUNT = 16;
        public const int SLOT_DECAL = 0;   // 데칼 이미지 0~3(DECAL_SLOTS)
        public const int DECAL_SLOTS = 4;

        [Tooltip("복원되면 알릴 데칼 스튜디오(Bake가 넣는다)")]
        public ACO_DecalStudio Studio;

        [UdonSynced] private VRCUrl[] _urls = new VRCUrl[] {
            VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty,
            VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty, VRCUrl.Empty };

        private bool _restored;

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || !Networking.IsOwner(gameObject)) { return; }
            _restored = true;
            if (_urls == null || _urls.Length != SLOT_COUNT) { Fix(); }
            if (Studio != null) { Studio.OnUrlStoreRestored(this); }
        }

        /// 내 저장소이고 복원이 끝났는가(쓸 수 있는가).
        public bool IsReady() { return _restored && Networking.IsOwner(gameObject); }

        /// 저장된 URL(없으면 VRCUrl.Empty).
        public VRCUrl Get(int slot)
        {
            if (_urls == null || slot < 0 || slot >= _urls.Length || _urls[slot] == null) { return VRCUrl.Empty; }
            return _urls[slot];
        }

        public bool Has(int slot)
        {
            VRCUrl u = Get(slot);
            return u != null && !string.IsNullOrEmpty(u.Get());
        }

        /// 저장(오너, 복원 뒤에만). 바뀌었으면 동기화 — VRChat이 영구 저장한다.
        public void Set(int slot, VRCUrl url)
        {
            if (!IsReady() || slot < 0 || slot >= SLOT_COUNT) { return; }
            if (_urls == null || _urls.Length != SLOT_COUNT) { Fix(); }
            if (url == null) { url = VRCUrl.Empty; }
            if (_urls[slot] != null && _urls[slot].Get() == url.Get()) { return; }
            _urls[slot] = url;
            RequestSerialization();
        }

        /// 칸 수가 다르면(예전 데이터 등) 16칸으로 맞춘다(있는 값은 옮긴다).
        private void Fix()
        {
            var n = new VRCUrl[SLOT_COUNT];
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                n[i] = _urls != null && i < _urls.Length && _urls[i] != null ? _urls[i] : VRCUrl.Empty;
            }
            _urls = n;
        }
    }
}
