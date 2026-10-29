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

        /// 체력은 SaccTarget이 동기화하므로 모든 클라이언트에서 같은 답이 나온다.
        public bool IsDestroyed()
        {
            return TargetHealth != null && TargetHealth.Health <= 0f;
        }

        /// SaccTarget이 격추 시 ExplodeOther의 스크립트들에 보내는 이벤트(모든 클라이언트에서 실행).
        public void Explode()
        {
            SetDeathVisuals(true);
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
            if (Networking.IsOwner(gameObject) && TargetHealth) { TargetHealth.RespawnTarget(); }
            SetDeathVisuals(false);
        }
    }
}
