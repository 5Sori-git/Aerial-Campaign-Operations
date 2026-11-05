using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 공중 요새의 무장 없는 부위(엔진). ACO_Warship 부위 계약(AttachedSpawn·Silence·NetRespawn·MissionDespawn, MarkerLabel·ScoreValue, 같은 오브젝트의 SaccTarget)을 따른다.
    /// 드러나기 전(Exposed = false)에는 락온 마커와 히트박스를 꺼 둔다 — 락온도 피해도 없고, 표적 마커에도 안 나온다(생성기 IsUnitSuppressed).
    /// 드러내는 때는 ACO_FlyingFortress가 동기화된 단계로 모든 클라이언트에서 정한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_FortressPart : UdonSharpBehaviour
    {
        [Tooltip("체력은 SaccTarget이 소유한다. ExplodeOther에 이 스크립트를 등록해야 격추 통지를 받는다")]
        public SaccTarget TargetHealth;
        [Tooltip("파괴 시 팀 점수")]
        public int ScoreValue = 600;
        [Tooltip("표적 마커에 표시할 약어(3~4글자)")]
        public string MarkerLabel = "ENG";
        [Tooltip("락온 마커(드러났을 때만 켠다). 이름을 AAMTarget으로 하면 안 된다 — SAV 애니메이터가 꺼 버린다")]
        public GameObject[] LockMarkers;
        public Collider[] Hitboxes;
        [Tooltip("파괴 시 숨길 오브젝트(엔진 불꽃 등)")]
        public GameObject[] HideOnDeath;
        [Tooltip("파괴 시 켤 오브젝트(연기 등)")]
        public GameObject[] ShowOnDeath;

        // 함선이 SetProgramVariable로 넣는다
        [System.NonSerialized] public bool MissionRevive;
        [System.NonSerialized] public bool Exposed;

        private bool _initialized;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            ApplyState();
        }

        /// 체력은 SaccTarget이 동기화하므로 모든 클라이언트에서 같은 답이 나온다.
        public bool IsDestroyed() { return TargetHealth != null && TargetHealth.Health <= 0f; }

        public void SetExposed(bool on)
        {
            Init();
            Exposed = on;
            ApplyState();
        }

        private void ApplyState()
        {
            bool dead = IsDestroyed();
            bool open = Exposed && !dead;
            SetAll(LockMarkers, open);
            SetAll(HideOnDeath, !dead);
            SetAll(ShowOnDeath, dead);
            if (Hitboxes == null) { return; }
            for (int i = 0; i < Hitboxes.Length; i++) { if (Hitboxes[i]) { Hitboxes[i].enabled = open; } }
        }

        private void SetAll(GameObject[] list, bool on)
        {
            if (list == null) { return; }
            for (int i = 0; i < list.Length; i++) { if (list[i]) { list[i].SetActive(on); } }
        }

        /// SaccTarget이 격추 시 ExplodeOther에 보내는 이벤트(모든 클라이언트).
        public void Explode() { ApplyState(); }

        // ───────────── 함선 부위 계약 ─────────────

        public void AttachedSpawn()
        {
            Init();
            Exposed = false;
            if (MissionRevive) { NetRespawn(); return; }
            ApplyState();
        }

        /// 본체가 파괴됐다: 부위도 파괴한다(락온·피해 끝, 점수 없음).
        public void Silence()
        {
            if (TargetHealth && TargetHealth.Health > 0f && Networking.IsOwner(TargetHealth.gameObject)) { TargetHealth.Explode(); }
            Exposed = false;
            ApplyState();
        }

        public void NetRespawn()
        {
            Init();
            if (Networking.IsOwner(gameObject) && TargetHealth) { SendCustomEventDelayedFrames(nameof(_RespawnTargetHealth), 2); }
            SetAll(HideOnDeath, true);
            SetAll(ShowOnDeath, false);
        }

        /// 체력 복구 — 풀에서 막 켠 유닛은 SaccTarget의 Start가 아직 안 돌아 2프레임 뒤에 부른다(ACO_Warship과 같다).
        public void _RespawnTargetHealth()
        {
            if (TargetHealth) { TargetHealth.RespawnTarget(); }
            ApplyState();
        }

        public void MissionDespawn()
        {
            Init();
            Exposed = false;
            ApplyState();
        }
    }
}
