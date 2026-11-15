using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 고가치 표적(공장, 벙커 등). 무장이 없는 건물로, 체력/피격/격추는 같은 오브젝트의 SaccTarget이 맡고
    /// 이 스크립트는 격추 연출, 부활, 미션 생성기 계약(MissionSpawn/MissionDespawn)만 처리한다.
    /// 미션 목표 판정(추후)이 읽을 수 있게 이름/점수와 파괴 여부를 제공한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_StaticTarget : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("체력/데미지/리스폰은 SaccTarget이 소유한다. ExplodeOther에 이 스크립트를 등록해야 격추 통지를 받는다")]
        public SaccTarget TargetHealth;
        [Tooltip("최대 체력(6-249). Bake가 SaccTarget 체력을 넣는다 — 생성기가 스폰 때마다 모든 화면에 넣어, 늦게 켜진 화면이 깎인 체력을 최대 체력으로 잡지 않게 한다. 0 = 건드리지 않음")]
        public float BaseHealth = 0f;

        [Header("표적 정보")]
        [Tooltip("미션 브리핑/목표 표시용 이름")]
        public string TargetName = "Factory";
        [Tooltip("파괴 시 팀 점수(ACO_MissionManager)")]
        public int ScoreValue = 500;
        [Tooltip("표적 마커에 표시할 약어(3~4글자)")]
        public string MarkerLabel = "FAC";

        [Header("격추 연출")]
        [Tooltip("파괴 시 숨길 오브젝트(멀쩡한 모델). SAV의 TargetExplode 애니메이션은 '루트'의 MeshRenderer/MeshCollider만 끈다")]
        public GameObject[] HideOnDeath;
        [Tooltip("파괴 시 켤 오브젝트(잔해 모델 등). 비워도 된다")]
        public GameObject[] ShowOnDeath;
        [Tooltip("파괴 시 끌 콜라이더")]
        public Collider[] DisableCollidersOnDeath;

        // 미션 생성기 계약(ACO_MissionGenerator가 SetProgramVariable로 넣고 MissionSpawn을 부른다)
        [System.NonSerialized] public Vector3 MissionSpawnPosition;
        [System.NonSerialized] public Quaternion MissionSpawnRotation = Quaternion.identity;
        [System.NonSerialized] public bool MissionRevive;

        private bool _initialized;

        void Start()
        {
            Init();
        }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponentInParent<SaccTarget>(); }
            SetDeathVisuals(false);
        }

        // 체력 0으로 격파 이벤트를 받았다(부활에서 풀림) — 표시 복구(_SyncRevive) 판단용. 본체 격침(Silence)은 따로다
        private bool _exploded;

        /// 체력은 SaccTarget이 동기화하므로 모든 클라이언트에서 같은 답이 나온다.
        public bool IsDestroyed()
        {
            return TargetHealth != null && TargetHealth.Health <= 0f;
        }

        /// SaccTarget이 격추 시 ExplodeOther의 스크립트들에 보내는 이벤트(모든 클라이언트에서 실행).
        public void Explode()
        {
            SetDeathVisuals(true);
            _exploded = true;   // 체력으로 격파됨(6-249 표시 복구 판단)
        }

        private void SetDeathVisuals(bool dead)
        {
            if (HideOnDeath != null)
            {
                for (int i = 0; i < HideOnDeath.Length; i++)
                {
                    if (HideOnDeath[i]) { HideOnDeath[i].SetActive(!dead); }
                }
            }
            if (ShowOnDeath != null)
            {
                for (int i = 0; i < ShowOnDeath.Length; i++)
                {
                    if (ShowOnDeath[i]) { ShowOnDeath[i].SetActive(dead); }
                }
            }
            if (DisableCollidersOnDeath != null)
            {
                for (int i = 0; i < DisableCollidersOnDeath.Length; i++)
                {
                    if (DisableCollidersOnDeath[i]) { DisableCollidersOnDeath[i].enabled = !dead; }
                }
            }
        }

        /// 미션 스폰(모든 클라이언트). 위치를 옮기고, MissionRevive면 부활시킨다.
        /// 늦게 들어온 클라이언트는 MissionRevive가 false라 위치만 맞춘다 — 생사는 SaccTarget 동기화가 맞춘다.
        public void MissionSpawn()
        {
            Init();
            transform.SetPositionAndRotation(MissionSpawnPosition, MissionSpawnRotation);
            if (MissionRevive) { NetRespawn(); }
        }

        /// 이번 미션에 쓰지 않는다. 생성기가 곧 오브젝트를 끈다.
        public void MissionDespawn()
        {
        }

        /// 부활 진입점. SaccTarget의 RespawnDelay는 0으로 두고(자동 부활 안 함) 미션 생성기나 매니저가 이걸 호출한다.
        public void Respawn()
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetRespawn));
        }

        public void NetRespawn()
        {
            // 체력 복구는 한 클라이언트만 해야 한다(RespawnTarget이 내부에서 소유권을 가져가고 동기화한다).
            if (TargetHealth)
            {
                if (Networking.IsOwner(gameObject)) { SendCustomEventDelayedFrames(nameof(_RespawnTargetHealth), 2); }
                else { SendCustomEventDelayedFrames(nameof(_LocalRefillHealth), 3); }   // 소유자가 아닌 화면은 같은 값으로 직접 채운다(6-247)
            }
            SetDeathVisuals(false);
            _exploded = false;
        }

        /// SaccTarget 체력 복구(RespawnTarget). 풀에서 막 켠 유닛은 SaccTarget의 Start가 아직 안 돌아 localPlayer·FullHealth가 비어 있다 —
        /// 바로 부르면 예외로 SaccTarget이 멈춘다(그 뒤로 피해·격추가 안 된다). 그래서 2프레임 뒤에 부른다.
        public void _RespawnTargetHealth()
        {
            if (TargetHealth) { TargetHealth.RespawnTarget(); }
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
        }

        /// 소유자가 아닌 화면(6-249): SaccTarget은 살아 있는데(체력 > 0) 이 스크립트만 격파 상태면 표시를 되돌린다. 미리 보기 격추를 껐으므로(6-248) 늘 어긋남이다
        /// (예: 꺼져 있던 동안의 지난 미션 격파가 켜진 뒤 늦게 적용). 스폰 채우기와 1.5 · 4초 다시 보내기(ACO_MissionGenerator) 때 부른다.
        public void _SyncRevive()
        {
            if (!TargetHealth || TargetHealth.Health <= 0f || !gameObject.activeInHierarchy || Networking.IsOwner(TargetHealth.gameObject)) { return; }
            if (!_exploded) { return; }
            _exploded = false;
            SetDeathVisuals(false);
            Debug.Log("[ACO_SYNC] 표시 복구(격파 상태였음): " + gameObject.name + " 체력 " + TargetHealth.Health.ToString("F0") + "/" + TargetHealth.FullHealth.ToString("F0"));
        }
    }
}
