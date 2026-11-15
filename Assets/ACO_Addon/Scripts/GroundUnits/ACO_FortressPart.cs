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
        [Tooltip("최대 체력(6-249). Bake가 SaccTarget 체력을 넣는다 — 생성기가 스폰 때마다 모든 화면에 넣어, 늦게 켜진 화면이 깎인 체력을 최대 체력으로 잡지 않게 한다. 0 = 건드리지 않음")]
        public float BaseHealth = 0f;
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
        [Tooltip("범위 피해 저항(6-182): 이 부위가 받는 범위 피해에 곱한다(함선 PartSplashScale 위에 더). 1 = 그대로. 탄이 직접 닿으면 그대로 받는다. 랜스 냉각탑 0.45 — 지원 폭격 두 번쯤에 깨진다")]
        [Range(0f, 1f)] public float SplashScale = 1f;

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
            if (TargetHealth)
            {
                if (Networking.IsOwner(gameObject)) { SendCustomEventDelayedFrames(nameof(_RespawnTargetHealth), 2); }
                else { SendCustomEventDelayedFrames(nameof(_LocalRefillHealth), 3); }   // 소유자가 아닌 화면은 같은 값으로 직접 채운다(6-247)
            }
            SetAll(HideOnDeath, true);
            SetAll(ShowOnDeath, false);
        }

        /// 체력 복구 — 풀에서 막 켠 유닛은 SaccTarget의 Start가 아직 안 돌아 2프레임 뒤에 부른다(ACO_Warship과 같다).
        public void _RespawnTargetHealth()
        {
            if (TargetHealth) { TargetHealth.RespawnTarget(); }
            ApplyState();
        }

        /// 소유자가 아닌 화면(6-247): 유닛을 켜기 전에 지나간 소유자의 스폰 채우기(RespawnTarget)를 놓쳤을 수 있다 — 그러면 지난 미션 체력 · 격파 상태가 남아
        /// 쏜 사람 화면의 격추 미리 보기가 틀리거나(투명 · 무적) 피해가 안 들어간다. 소유자와 같은 값(최대 체력)으로 직접 채운다.
        /// 숙련도 최대 체력(2프레임 뒤) 다음인 3프레임 뒤에, SaccTarget.Start가 끝난 것만(Start 전에 부르면 SaccTarget이 멈춘다).
        public void _LocalRefillHealth()
        {
            if (!TargetHealth || !TargetHealth.gameObject.activeInHierarchy || Networking.IsOwner(TargetHealth.gameObject)) { return; }
            if (TargetHealth.GetProgramVariable("TargetAnimator") == null) { return; }
            if (TargetHealth.FullHealth <= 0f) { return; }
            float before = TargetHealth.Health;
            TargetHealth.Health = TargetHealth.FullHealth;
            TargetHealth.SendCustomEvent("_onDeserialization");   // 격파 상태 · 체력 바를 새 값으로(이 화면에서만)
            if (before < TargetHealth.FullHealth * 0.99f) { Debug.Log("[ACO_SYNC] 스폰 직접 채우기: " + gameObject.name + " " + before.ToString("F0") + " → " + TargetHealth.FullHealth.ToString("F0") + "(소유자 채우기를 놓침)"); }
            _SyncRevive();
            ApplyState();
        }

        public void MissionDespawn()
        {
            Init();
            Exposed = false;
            ApplyState();
        }

        /// 소유자가 아닌 화면(6-249): SaccTarget은 살아 있는데(체력 > 0) 이 스크립트만 격파 상태면 표시를 되돌린다. 미리 보기 격추를 껐으므로(6-248) 늘 어긋남이다
        /// (예: 꺼져 있던 동안의 지난 미션 격파가 켜진 뒤 늦게 적용). 스폰 채우기와 1.5 · 4초 다시 보내기(ACO_MissionGenerator) 때 부른다.
        public void _SyncRevive()
        {
            if (!TargetHealth || TargetHealth.Health <= 0f || !gameObject.activeInHierarchy || Networking.IsOwner(TargetHealth.gameObject)) { return; }
            ApplyState();   // 부위 상태는 체력에서 바로 나온다
        }
    }
}
