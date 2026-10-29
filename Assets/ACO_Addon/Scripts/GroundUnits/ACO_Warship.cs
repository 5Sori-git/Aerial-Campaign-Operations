using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 부위 파괴형 함선(본체). 무장은 자식 부위(Parts — VLS = ACO_SAMSite, 함포/CIWS = ACO_AAGunSite)가 맡고,
    /// 부위마다 자기 SaccTarget/히트박스/락온 마커가 있어 따로 락온·파괴된다. 본체 체력은 이 오브젝트의 SaccTarget.
    ///  - 부위가 파괴되면 본체도 최대 체력의 PartHullDamage만큼 깎인다(본체 오너가 적용). 부위를 먼저 정리할수록 빨리 가라앉는다.
    ///  - 본체가 격침되면 남은 부위는 교전을 멈추고(Silence, 점수 없음) 함선은 그 자리에 멈춘다.
    ///    가라앉는 연출은 본체 SaccTarget의 Animator(격침 애니메이션)와 HideOnDeath/ShowOnDeath로 한다 — 모델과 부위는 Body 아래에 두고 Body를 움직이면 된다.
    ///
    /// 순항: 스폰 지점(MissionSpawnPosition)을 중심으로 반경 CruiseRadius 원을 CruiseSpeed로 돈다.
    /// 위치는 서버 시간으로 계산하므로 모든 클라이언트가 같은 곳에 그리고, 스폰 후 추가 동기화가 없다.
    /// 도는 방향과 시작 각도는 스폰 회전(방위각)으로 정한다 — 생성기가 함선에는 정수+0.5도 방위를 줘서 클라이언트마다 같게 읽힌다.
    ///
    /// 호위(편대): 생성기가 MissionLeader/MissionSlot을 넣으면 기함과 같은 순항 기준(중심/방위 — 생성기가 같은 값을 준다)에
    /// 기함의 반경/속도로 궤도를 계산하고, 그 위에서 편대 슬롯(기함 EscortSpacing 간격)만큼 떨어져 간다.
    /// 궤도는 시간 함수라 결정적이고, 기함이 격침돼도 호위함은 같은 궤도를 계속 돈다.
    ///
    /// HighValue: 켜면(전함) 고가치 표적 풀(타격 페이즈 목표), 끄면(구축함) 지상 유닛 풀 — 생성기가 바다에 무리 지어 놓거나 전함 호위로 붙인다.
    /// SaccTarget 규칙상 본체 콜라이더는 이 오브젝트(루트)에 있어야 한다. 부위 콜라이더는 각 부위 루트에 있다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_Warship : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("본체 체력. ExplodeOther에 이 스크립트를 등록해야 격침 통지를 받는다")]
        public SaccTarget TargetHealth;
        [Tooltip("부위(ACO_SAMSite / ACO_AAGunSite). 각자 SaccTarget을 가진 자식 오브젝트")]
        public UdonSharpBehaviour[] Parts;

        [Header("표적 정보")]
        public string TargetName = "Battleship";
        [Tooltip("격침 시 팀 점수(부위 점수는 각 부위의 ScoreValue)")]
        public int ScoreValue = 1500;
        [Tooltip("표적 마커에 표시할 약어(3~4글자)")]
        public string MarkerLabel = "BB";
        [Tooltip("켜면 고가치 표적(타격 페이즈 목표), 끄면 일반 유닛(지상 풀 — 바다에 무리 지어 스폰, 전함 호위). ACO → Bake NPC References가 이 값으로 풀을 나눈다")]
        public bool HighValue = true;

        [Header("부위 파괴")]
        [Tooltip("부위 하나가 파괴될 때 본체가 잃는 체력(최대 체력 비율)")]
        [Range(0f, 1f)] public float PartHullDamage = 0.12f;
        [Tooltip("부위 상태를 확인하는 주기(초)")]
        public float PartCheckInterval = 0.25f;

        [Header("순항")]
        [Tooltip("순항 속도(m/s). 0이면 제자리. 호위함은 기함 속도를 따른다")]
        public float CruiseSpeed = 12f;
        [Tooltip("순항 원 반경(m). 생성기는 이 원 전체(+ 편대 폭)가 바다인 곳에만 놓는다")]
        public float CruiseRadius = 2500f;
        [Tooltip("파도에 흔들리는 각도(도)")]
        public float BobAngle = 0.6f;
        [Tooltip("흔들림 주기(초)")]
        public float BobPeriod = 7f;
        [Tooltip("이 함선이 기함일 때 호위함 사이 간격(m). 생성기는 순항 원 + 이 값 x 2 범위가 바다인 곳에 함대를 놓는다")]
        public float EscortSpacing = 500f;

        [Header("격침 연출")]
        [Tooltip("격침 시 숨길 오브젝트(멀쩡한 모델 등)")]
        public GameObject[] HideOnDeath;
        [Tooltip("격침 시 켤 오브젝트(화재/연기/폭발 등 연출용)")]
        public GameObject[] ShowOnDeath;
        [Tooltip("격침 시 끌 본체 콜라이더")]
        public Collider[] DisableCollidersOnDeath;

        // 미션 생성기 계약(ACO_MissionGenerator가 SetProgramVariable로 넣고 MissionSpawn을 부른다)
        [System.NonSerialized] public Vector3 MissionSpawnPosition;
        [System.NonSerialized] public Quaternion MissionSpawnRotation = Quaternion.identity;
        [System.NonSerialized] public bool MissionRevive;
        /// 호위할 기함과 편대 슬롯(생성기가 MissionSpawn 직전에 넣는다). null/-1이면 스스로 순항.
        [System.NonSerialized] public ACO_Warship MissionLeader;
        [System.NonSerialized] public int MissionSlot = -1;

        /// EvaluateCruise 결과(흔들림 제외).
        [System.NonSerialized] public Vector3 CruisePos;
        [System.NonSerialized] public Quaternion CruiseRot = Quaternion.identity;

        private const double TWO_PI = 6.283185307179586;

        private bool _initialized;
        private bool _spawned;
        private Vector3 _center;
        private Quaternion _restRotation = Quaternion.identity;
        private double _phase0;
        private float _dir = 1f;
        private float _bobSeed;
        // 궤도 반경/속도(호위함은 기함 값), 편대 슬롯 오프셋(기함 기준 로컬)
        private float _orbitRadius;
        private float _orbitSpeed;
        private Vector3 _slotOffset;

        private SaccTarget[] _partHealth;
        private bool[] _partDead;
        private float _partTimer;

        private bool _dead;

        void Start()
        {
            Init();
        }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }

            int n = Parts != null ? Parts.Length : 0;
            _partHealth = new SaccTarget[n];
            _partDead = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (Parts[i] != null) { _partHealth[i] = Parts[i].GetComponent<SaccTarget>(); }
                _partDead[i] = true; // 살아 있는 걸 보기 전엔 "파괴됨"으로 둔다 — 부활 동기화 전의 체력 0으로 본체를 깎지 않게
            }
            // 씬에 직접 둔 함선은 그 자리를 중심으로 바로 순항한다. 미션 풀의 함선은 생성기가 곧 숨기고(MissionDespawn) 스폰 때 다시 정한다.
            SetCruiseBasis(transform.position, transform.rotation);
            _orbitRadius = CruiseRadius;
            _orbitSpeed = CruiseSpeed;
            _spawned = true;
            SetDeathVisuals(false);
        }

        /// 순항 중심과 방위각(시작 각도, 도는 방향)을 정한다.
        private void SetCruiseBasis(Vector3 center, Quaternion rotation)
        {
            _center = center;
            _restRotation = rotation;
            float yaw = rotation.eulerAngles.y;
            _phase0 = yaw * Mathf.Deg2Rad;
            _dir = (Mathf.FloorToInt(yaw) % 2 == 0) ? 1f : -1f;
            _bobSeed = yaw * 0.1f;
        }

        void Update()
        {
            if (!_spawned) { return; }
            float dt = Time.deltaTime;

            // 격침되면 그 자리에 멈춘다(가라앉는 연출은 애니메이션 몫).
            if (!_dead) { ApplyCruisePose(); }

            _partTimer -= dt;
            if (_partTimer <= 0f)
            {
                _partTimer = PartCheckInterval;
                CheckParts();
            }
        }

        // ───────────── 순항 ─────────────

        /// 서버 시간 t의 순항 위치/방향을 CruisePos/CruiseRot에 넣는다(흔들림 제외). 모든 클라이언트가 같은 값을 얻는다.
        /// 격침 여부와 무관한 시간 함수다(호위함은 같은 기준으로 자기 궤도를 계산한다).
        public void EvaluateCruise(double t)
        {
            if (_orbitSpeed <= 0f || _orbitRadius <= 1f)
            {
                CruisePos = _center;
                CruiseRot = _restRotation;
                return;
            }
            double omega = _orbitSpeed / _orbitRadius;
            float a = (float)((_phase0 + _dir * omega * t) % TWO_PI);
            float c = Mathf.Cos(a);
            float s = Mathf.Sin(a);
            CruisePos = _center + new Vector3(c, 0f, s) * _orbitRadius;
            // 각도가 늘어나는 방향의 접선(반대로 돌면 뒤집는다)
            CruiseRot = Quaternion.LookRotation(new Vector3(-s, 0f, c) * _dir, Vector3.up);
        }

        private void ApplyCruisePose()
        {
            double t = Networking.GetServerTimeInSeconds();
            EvaluateCruise(t);
            // 호위함: 같은 궤도 위 기함 위치에서 편대 슬롯만큼(기함 진행 방향 기준). 바깥쪽 슬롯은 같은 각속도로 조금 더 빨리 돈다.
            Vector3 pos = CruisePos + CruiseRot * _slotOffset;
            transform.SetPositionAndRotation(pos, CruiseRot * BobRotation(t));
        }

        /// 편대 슬롯(기함 로컬, x = 오른쪽, z = 앞): 좌우 뒤 → 바로 뒤 → 앞 → 더 바깥.
        private Vector3 SlotOffset(int slot, float spacing)
        {
            if (slot == 0) { return new Vector3(-spacing, 0f, -spacing * 0.6f); }
            if (slot == 1) { return new Vector3(spacing, 0f, -spacing * 0.6f); }
            if (slot == 2) { return new Vector3(0f, 0f, -spacing * 1.4f); }
            if (slot == 3) { return new Vector3(0f, 0f, spacing * 1.2f); }
            float side = (slot % 2 == 0) ? -1f : 1f;
            int row = slot / 2;
            return new Vector3(side * spacing * row, 0f, -spacing * 0.8f * row);
        }

        private Quaternion BobRotation(double t)
        {
            if (BobAngle <= 0f || BobPeriod <= 0f) { return Quaternion.identity; }
            float phase = (float)(t % (BobPeriod * 1000.0)) / BobPeriod * Mathf.PI * 2f + _bobSeed;
            float roll = Mathf.Sin(phase) * BobAngle;
            float pitch = Mathf.Sin(phase * 0.73f + 1.3f) * BobAngle * 0.4f;
            return Quaternion.Euler(pitch, 0f, roll);
        }

        // ───────────── 부위 ─────────────

        /// 부위가 "살아 있다가" 파괴되면 본체를 깎는다(오너만). 마지막 공격자는 모든 클라이언트에서 본체 기록에 옮겨 둔다 —
        /// 부위 파괴로 본체가 가라앉으면 작전판이 그 부위를 부순 조종사를 격침자로 쓴다.
        private void CheckParts()
        {
            for (int i = 0; i < _partHealth.Length; i++)
            {
                SaccTarget h = _partHealth[i];
                if (h == null) { continue; }
                bool dead = h.Health <= 0f;
                if (!dead) { _partDead[i] = false; continue; }
                if (_partDead[i]) { continue; }
                _partDead[i] = true;
                if (!_dead) { OnPartDestroyed(h); }
            }
        }

        private void OnPartDestroyed(SaccTarget part)
        {
            if (TargetHealth == null) { return; }
            if (Utilities.IsValid(part.LastHitByPlayer)) { TargetHealth.LastHitByPlayer = part.LastHitByPlayer; }
            if (!Networking.IsOwner(gameObject) || TargetHealth.Health <= 0f || PartHullDamage <= 0f) { return; }

            TargetHealth.Health = Mathf.Max(TargetHealth.Health - PartHullDamage * TargetHealth.FullHealth, 0f);
            // SAV의 동기화 경로를 그대로 탄다(체력 0이면 모든 클라이언트에서 Explode).
            TargetHealth.SendNetworkUpdate();
        }

        public bool IsDestroyed()
        {
            return TargetHealth != null && TargetHealth.Health <= 0f;
        }

        // ───────────── 격침 ─────────────

        /// SaccTarget이 격침 시 ExplodeOther에 보내는 이벤트(모든 클라이언트).
        public void Explode()
        {
            if (_dead) { return; }
            _dead = true;
            SetDeathVisuals(true);
            SilenceParts();
        }

        private void SilenceParts()
        {
            if (Parts == null) { return; }
            for (int i = 0; i < Parts.Length; i++)
            {
                if (Parts[i] != null) { Parts[i].SendCustomEvent("Silence"); }
            }
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

        // ───────────── 미션 생성기 계약 ─────────────

        /// 미션 스폰(모든 클라이언트). 순항 기준(또는 호위 위치)을 정하고, MissionRevive면 본체와 부위를 부활시킨다.
        /// 늦게 들어온 클라이언트는 MissionRevive가 false — 생사는 SaccTarget 동기화가 맞추고, 이미 격침됐으면 격침 상태로 둔다.
        public void MissionSpawn()
        {
            Init();
            ACO_Warship leader = MissionLeader;
            int slot = MissionSlot;
            MissionLeader = null;
            MissionSlot = -1;
            SetCruiseBasis(MissionSpawnPosition, MissionSpawnRotation);
            if (leader != null && slot >= 0)
            {
                _orbitRadius = leader.CruiseRadius;
                _orbitSpeed = leader.CruiseSpeed;
                _slotOffset = SlotOffset(slot, leader.EscortSpacing);
            }
            else
            {
                _orbitRadius = CruiseRadius;
                _orbitSpeed = CruiseSpeed;
                _slotOffset = Vector3.zero;
            }
            _spawned = true;
            _dead = false;
            ApplyCruisePose();

            // 부위는 각자 부활/상태 복원한다(위치는 이 오브젝트를 따라간다).
            int n = Parts != null ? Parts.Length : 0;
            for (int i = 0; i < n; i++)
            {
                UdonSharpBehaviour part = Parts[i];
                if (part == null) { continue; }
                part.SetProgramVariable("MissionRevive", MissionRevive);
                part.SendCustomEvent("AttachedSpawn");
                _partDead[i] = true;
            }

            if (MissionRevive)
            {
                ReviveHull();
                return;
            }
            SetDeathVisuals(false);
            if (TargetHealth != null && TargetHealth.Health <= 0f) { Explode(); }
        }

        /// 이번 미션에 쓰지 않는다. 생성기가 곧 오브젝트를 끈다.
        public void MissionDespawn()
        {
            Init();
            _spawned = false;
            if (Parts == null) { return; }
            for (int i = 0; i < Parts.Length; i++)
            {
                if (Parts[i] != null) { Parts[i].SendCustomEvent("MissionDespawn"); }
            }
        }

        /// 부활 진입점(미션 밖에서 쓸 때). 미션 중엔 생성기가 MissionSpawn으로 부활시킨다.
        public void Respawn()
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetRespawn));
        }

        /// 본체와 부위를 모두 부활시킨다(모든 클라이언트).
        public void NetRespawn()
        {
            Init();
            ReviveHull();
            if (Parts == null) { return; }
            for (int i = 0; i < Parts.Length; i++)
            {
                if (Parts[i] == null) { continue; }
                Parts[i].SendCustomEvent("NetRespawn");
                _partDead[i] = true;
            }
        }

        private void ReviveHull()
        {
            // 체력 복구는 한 클라이언트만 해야 한다(RespawnTarget이 내부에서 소유권을 가져가고 동기화한다).
            if (Networking.IsOwner(gameObject) && TargetHealth) { TargetHealth.RespawnTarget(); }
            _dead = false;
            SetDeathVisuals(false);
        }
    }
}
