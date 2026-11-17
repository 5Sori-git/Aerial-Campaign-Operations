using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 도시 차량(6-270 시가지 정밀 타격): 도시 블록 도로 위에 주차하거나 블록 둘레를 도는 민간 · 적 차량.
    /// 생성기 지상 풀(GroundUnits)에 들고, 일반 배치(배경 병력 · 방공 링 · 증원)에는 쓰지 않는다(ACO_MissionGenerator.IsGroundMover).
    /// 생성기가 배치 때 정수 코드(CityCode — 지상 동기화의 _groundLeader 칸)와 배치 시각(SetStartTime)을 넣는다. 코드 비트:
    ///  - 0: 적(1) / 민간(0)       - 1~4: 모델 변형(Variants 번호)     - 5: 주행(1) / 주차(0)
    ///  - 6: 오른쪽으로 돈다(1)    - 7: 앞 변 칸 수 − 1(0~1)          - 8: 옆 변 칸 수 − 1(0~1)
    ///  - 9~10: 처음 방향(0 +Z, 1 +X, 2 −Z, 3 −X)                      - 11~13: 속도 단계(0~7)
    /// 주행: 배치 위치 = 교차로(도로 가운데선), 처음 방향으로 앞 변 → 돌아서 옆 변 → … 사각 고리. 도로 격자는 x 86.6m · z 100m 간격.
    /// 오른쪽 차선(가운데선에서 LaneOffset)으로 달리고 모서리는 반지름 CornerRadius로 돈다. 위치 = (서버 시각 − 배치 시각) × 속도 — 모든 화면이 같은 값을 얻는다.
    /// 식별(Identified)은 페이즈 모듈이 넣는다(동기화된 식별 값 → 각 화면). 식별된 민간 차량은 락온이 막힌다(Gate).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_CityVehicle : UdonSharpBehaviour
    {
        public const float GRID_X = 86.60254f;
        public const float GRID_Z = 100f;
        public const int VARIANT_COUNT_CIVILIAN = 9;

        [Header("참조 (프리팹 빌더가 넣는다)")]
        public SaccTarget TargetHealth;
        [Tooltip("모델 변형(앞 9개 민간, 뒤 2개 군용) — 코드의 변형 번호 하나만 켠다")]
        public GameObject[] Variants;
        [Tooltip("변형별 히트박스 크기(Variants와 같은 순서, 가운데 높이는 크기의 절반)")]
        public Vector3[] VariantSizes;
        public BoxCollider Hitbox;
        [Tooltip("락온 마커(지상 적 층) — 변형 크기에 맞춰 높이를 옮긴다")]
        public Transform LockMarker;
        [Tooltip("식별된 민간 차량은 락온을 막는다(MODE_BLOCK)")]
        public ACO_LockGate Gate;
        [Tooltip("격파 때 켤 연출")]
        public GameObject DeathFx;
        [Tooltip("격파 때 모델에 입힐 탄 재질")]
        public Material BurntMaterial;
        [Tooltip("최대 체력(6-249) — 생성기가 스폰 때마다 넣는다")]
        public float BaseHealth = 0f;

        [Header("점수 · 표시")]
        public int EnemyScore = 600;
        [Tooltip("민간 차량 파괴 감점(팀 + 파괴한 사람, 페이즈 배율 없음)")]
        public int CivilianScore = -1000;
        public string EnemyLabel = "CMD";
        public string CivilianLabel = "CIV";

        [Header("주행")]
        [Tooltip("가운데선에서 주행 차선까지(m) — 도로가 주차(생성기 CityParkOffset 5.25m)와 겹치지 않게. 작은 길 반폭 6.5m")]
        public float LaneOffset = 2.5f;
        public float CornerRadius = 6f;
        public float BaseSpeed = 10f;
        public float SpeedStep = 0.8f;

        // 미션 생성기 계약
        [System.NonSerialized] public Vector3 MissionSpawnPosition;
        [System.NonSerialized] public Quaternion MissionSpawnRotation = Quaternion.identity;
        [System.NonSerialized] public bool MissionRevive;
        [System.NonSerialized] public int CityCode = -1;

        // 생성기 · 미션 관리자 · 기록이 읽는다(GetProgramVariable)
        [System.NonSerialized] public int ScoreValue = 600;
        [System.NonSerialized] public string MarkerLabel = "?";
        [System.NonSerialized] public bool IsEnemy;
        [System.NonSerialized] public bool Identified;

        private bool _initialized;
        private bool _exploded;
        private bool _driving;
        private int _startMs;
        private float _speed;
        private Material _liveMaterial;
        private Renderer[] _activeRenderers;
        // 주행 고리(오른쪽 차선으로 옮긴 꼭짓점 4개와 변 방향)
        private Vector3[] _corner = new Vector3[4];
        private Vector3[] _dir = new Vector3[4];
        private float[] _segLen = new float[4];
        private float _loopLen;
        private float _roadY;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
        }

        // ───────────── 코드 (생성기와 같은 규칙) ─────────────

        public static int MakeCode(bool enemy, int variant, bool drive, bool turnRight, int front, int side, int heading, int speed)
        {
            return (enemy ? 1 : 0) | ((variant & 15) << 1) | (drive ? 32 : 0) | (turnRight ? 64 : 0) | (((front - 1) & 1) << 7) | (((side - 1) & 1) << 8)
                | ((heading & 3) << 9) | ((speed & 7) << 11);
        }

        public static Vector3 HeadingDir(int heading)
        {
            if (heading == 0) { return Vector3.forward; }
            if (heading == 1) { return Vector3.right; }
            if (heading == 2) { return Vector3.back; }
            return Vector3.left;
        }

        /// 방향의 격자 간격(z축 100m, x축 86.6m).
        public static float GridStep(int heading) { return (heading & 1) == 0 ? GRID_Z : GRID_X; }

        /// 오른쪽으로 돌면 오른쪽, 아니면 왼쪽 방향.
        public static int SideHeading(int heading, bool turnRight) { return (heading + (turnRight ? 1 : 3)) & 3; }

        // ───────────── 생성기 계약 ─────────────

        public void SetStartTime(int ms) { _startMs = ms; }

        /// 이 화면에 마지막으로 적용된 배치 시각 — 생성기 배치 시각과 다르면 아직 이번 배치가 적용되지 않았다.
        public int GetStartMs() { return _startMs; }

        public void MissionSpawn()
        {
            Init();
            ApplyCode();
            transform.SetPositionAndRotation(MissionSpawnPosition, MissionSpawnRotation);
            _roadY = MissionSpawnPosition.y;
            if (_driving) { BuildLoop(); ApplyDrive(); }
            if (MissionRevive) { NetRespawn(); }
            else
            {
                // 늦게 들어온 화면: 체력 동기화로 이미 부서진 차량은 탄 모습으로 멈춘다(격파 이벤트가 변형을 켜기 전에 와서 재질을 못 바꿨을 수 있다).
                // 주행 차량이 부서진 자리는 알 수 없어 지금 고리 위치에 선다.
                bool dead = IsDestroyed();
                _exploded = dead;
                SetDeathVisuals(dead);
            }
        }

        public void MissionDespawn()
        {
            _driving = false;
            Identified = false;
            UpdateGate();
        }

        private void ApplyCode()
        {
            int code = CityCode < 0 ? 0 : CityCode;
            IsEnemy = (code & 1) != 0;
            int variant = (code >> 1) & 15;
            int count = Variants != null ? Variants.Length : 0;
            if (count > 0) { variant = Mathf.Clamp(variant, 0, count - 1); }
            for (int i = 0; i < count; i++) { if (Variants[i]) { Variants[i].SetActive(i == variant); } }
            _activeRenderers = count > 0 && Variants[variant] ? Variants[variant].GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            if (_activeRenderers.Length > 0 && _liveMaterial == null) { _liveMaterial = _activeRenderers[0].sharedMaterial; }
            Vector3 size = VariantSizes != null && variant < VariantSizes.Length ? VariantSizes[variant] : new Vector3(2f, 2f, 5f);
            if (Hitbox) { Hitbox.size = size; Hitbox.center = new Vector3(0f, size.y * 0.5f, 0f); }
            if (LockMarker) { LockMarker.localPosition = new Vector3(0f, size.y * 0.5f, 0f); }
            ScoreValue = IsEnemy ? EnemyScore : CivilianScore;
            MarkerLabel = IsEnemy ? EnemyLabel : CivilianLabel;
            _driving = (code & 32) != 0;
            _speed = BaseSpeed + ((code >> 11) & 7) * SpeedStep;
            Identified = false;
            UpdateGate();
        }

        // ───────────── 식별 (페이즈 모듈이 넣는다) ─────────────

        public void SetIdentified(bool on)
        {
            if (Identified == on) { return; }
            Identified = on;
            UpdateGate();
        }

        private void UpdateGate()
        {
            if (Gate == null) { return; }
            Gate.Mode = ACO_LockGate.MODE_BLOCK;
            Gate.Active = Identified && !IsEnemy;
        }

        // ───────────── 주행 ─────────────

        private void BuildLoop()
        {
            int code = CityCode < 0 ? 0 : CityCode;
            bool right = (code & 64) != 0;
            int front = ((code >> 7) & 1) + 1;
            int side = ((code >> 8) & 1) + 1;
            int heading = (code >> 9) & 3;
            int sideHeading = SideHeading(heading, right);
            Vector3 f = HeadingDir(heading);
            Vector3 s = HeadingDir(sideHeading);
            float lf = front * GridStep(heading);
            float ls = side * GridStep(sideHeading);
            Vector3 p0 = new Vector3(MissionSpawnPosition.x, _roadY, MissionSpawnPosition.z);
            // 가운데선 사각형 P0 → P1(앞) → P2 → P3(옆). 오른쪽 차선 = 오른쪽으로 돌면 안쪽, 왼쪽으로 돌면 바깥쪽
            float d = right ? LaneOffset : -LaneOffset;
            _corner[0] = p0 + (f + s) * d;
            _corner[1] = p0 + f * lf + (-f + s) * d;
            _corner[2] = p0 + f * lf + s * ls + (-f - s) * d;
            _corner[3] = p0 + s * ls + (f - s) * d;
            _loopLen = 0f;
            float r = CornerRadius;
            for (int k = 0; k < 4; k++)
            {
                Vector3 seg = _corner[(k + 1) & 3] - _corner[k];
                float len = seg.magnitude;
                _dir[k] = len > 0.01f ? seg / len : f;
                _segLen[k] = Mathf.Max(len - 2f * r, 0f);
                _loopLen += _segLen[k] + r * Mathf.PI * 0.5f;
            }
        }

        void Update()
        {
            if (!_driving || _exploded) { return; }
            ApplyDrive();
        }

        private void ApplyDrive()
        {
            if (_loopLen <= 1f) { return; }
            float dist = (Networking.GetServerTimeInMilliseconds() - _startMs) * 0.001f * _speed;
            dist = dist % _loopLen;
            if (dist < 0f) { dist += _loopLen; }
            float r = CornerRadius;
            float arc = r * Mathf.PI * 0.5f;
            for (int k = 0; k < 4; k++)
            {
                Vector3 a = _corner[k] + _dir[k] * r;
                if (dist <= _segLen[k])
                {
                    Place(a + _dir[k] * dist, _dir[k]);
                    return;
                }
                dist -= _segLen[k];
                Vector3 next = _dir[(k + 1) & 3];
                if (dist <= arc)
                {
                    float phi = dist / r;
                    Vector3 start = _corner[(k + 1) & 3] - _dir[k] * r;
                    Vector3 center = start + next * r;
                    float c = Mathf.Cos(phi);
                    float sn = Mathf.Sin(phi);
                    Place(center - next * (r * c) + _dir[k] * (r * sn), _dir[k] * c + next * sn);
                    return;
                }
                dist -= arc;
            }
            Place(_corner[0] + _dir[0] * r, _dir[0]);
        }

        private void Place(Vector3 pos, Vector3 forward)
        {
            pos.y = _roadY;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) { forward = Vector3.forward; }
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward, Vector3.up));
        }

        // ───────────── 체력 · 격파 ─────────────

        public bool IsDestroyed() { return TargetHealth != null && TargetHealth.Health <= 0f; }

        /// SaccTarget 격파 이벤트(모든 화면): 그 자리에 멈추고 탄 모습으로.
        public void Explode()
        {
            _exploded = true;
            SetDeathVisuals(true);
        }

        private void SetDeathVisuals(bool dead)
        {
            if (_activeRenderers != null)
            {
                for (int i = 0; i < _activeRenderers.Length; i++)
                {
                    if (_activeRenderers[i] == null) { continue; }
                    _activeRenderers[i].sharedMaterial = dead && BurntMaterial != null ? BurntMaterial : _liveMaterial;
                }
            }
            if (DeathFx) { DeathFx.SetActive(dead); }
            if (Hitbox) { Hitbox.enabled = !dead; }
            if (LockMarker) { LockMarker.gameObject.SetActive(!dead); }
        }

        public void NetRespawn()
        {
            if (TargetHealth)
            {
                if (Networking.IsOwner(gameObject)) { SendCustomEventDelayedFrames(nameof(_RespawnTargetHealth), 2); }
                else { SendCustomEventDelayedFrames(nameof(_LocalRefillHealth), 3); }   // 6-247
            }
            _exploded = false;
            SetDeathVisuals(false);
        }

        public void _RespawnTargetHealth()
        {
            if (TargetHealth) { TargetHealth.RespawnTarget(); }
        }

        /// 소유자가 아닌 화면(6-247): 소유자의 스폰 채우기를 놓쳤을 수 있다 — 같은 값으로 직접 채운다(SaccTarget.Start 뒤에만).
        public void _LocalRefillHealth()
        {
            if (!TargetHealth || !TargetHealth.gameObject.activeInHierarchy || Networking.IsOwner(TargetHealth.gameObject)) { return; }
            if (TargetHealth.GetProgramVariable("TargetAnimator") == null) { return; }
            if (TargetHealth.FullHealth <= 0f) { return; }
            TargetHealth.Health = TargetHealth.FullHealth;
            TargetHealth.SendCustomEvent("_onDeserialization");
            _SyncRevive();
        }

        /// 소유자가 아닌 화면(6-249): 체력은 살아 있는데 격파 표시면 되돌린다.
        public void _SyncRevive()
        {
            if (!TargetHealth || TargetHealth.Health <= 0f || !gameObject.activeInHierarchy || Networking.IsOwner(TargetHealth.gameObject)) { return; }
            if (!_exploded) { return; }
            _exploded = false;
            SetDeathVisuals(false);
            if (_driving) { ApplyDrive(); }
        }
    }
}
