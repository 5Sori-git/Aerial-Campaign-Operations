using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// TEL(이동식 탄도탄 발사대 — 시한 표적 페이즈 전용, 지상 풀에 있지만 생성기가 일반 배치에 쓰지 않는다). 체력은 같은 오브젝트의 SaccTarget 하나.
    ///  - 발사 준비(배치 후 LaunchDelay초): 지상 표적 — 레일이 서서히 일어난다. 공대지 무장으로 잠긴다.
    ///  - 상승(그 뒤 FlightTime초): 공중 표적 — 탄도탄이 CeilingAltitude까지 등가속으로 곧게 올라간다. 공대공 무장으로 잠긴다.
    ///    높이 = 발사 높이 + (천장 - 발사 높이) × (경과 / FlightTime)² — 서버 시간 함수라 위치 동기화가 없고 늦게 들어와도 같은 곳에 있다.
    /// 표적 마커·레이더는 이 오브젝트(루트)를 따라가므로, 상승할 때 루트가 탄도탄과 함께 올라가고 빈 차체(Chassis)만 매 프레임 발사 자리에 남긴다.
    /// 파괴되면(체력 0 — 모든 클라이언트) 발사 전이든 후든 차체까지 사라지고 폭발만 잠깐(BlastTime) 남는다. 놓치면 미션 실패라 리셋으로 정리된다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_TelUnit : UdonSharpBehaviour
    {
        [Header("표적 정보")]
        [Tooltip("체력은 SaccTarget이 소유한다. ExplodeOther에 이 스크립트를 등록해야 파괴 통지를 받는다")]
        public SaccTarget TargetHealth;
        public int ScoreValue = 600;
        [Tooltip("표적 마커 약어 — 발사 전")]
        public string MarkerLabel = "TEL";
        [Tooltip("표적 마커 약어 — 발사 후")]
        public string MissileLabel = "BM";

        [Header("구성 (프리팹 빌더)")]
        [Tooltip("발사 자리에 남는 차체(발사대·레일 포함)")]
        public Transform Chassis;
        [Tooltip("일어나는 레일(회전축)")]
        public Transform Rail;
        [Tooltip("레일에 실린 탄도탄 — 발사 전")]
        public GameObject RailMissile;
        [Tooltip("날아가는 탄도탄(불꽃·연기 포함) — 발사 후")]
        public GameObject FlyingMissile;
        public GameObject GroundMarker;
        public GameObject AirMarker;
        public Collider ChassisHitbox;
        public Collider MissileHitbox;
        [Tooltip("파괴 시 켤 폭발")]
        public GameObject BlastEffect;
        [Tooltip("폭발을 보여 주는 시간(초)")]
        public float BlastTime = 6f;

        [Header("타이밍 (Bake가 시한 표적 모듈 값으로 넣는다)")]
        [Tooltip("배치부터 발사까지(초)")]
        public float LaunchDelay = 30f;
        [Tooltip("발사부터 요격 불가까지(초)")]
        public float FlightTime = 60f;
        [Tooltip("올라가는 끝 고도(m) — 씬 SAV 최대 고도")]
        public float CeilingAltitude = 19812f;
        [Tooltip("레일이 일어나는 각도(도)")]
        public float RailErectAngle = 88f;

        // 미션 생성기 계약(SetProgramVariable + MissionSpawn)
        [System.NonSerialized] public Vector3 MissionSpawnPosition;
        [System.NonSerialized] public Quaternion MissionSpawnRotation = Quaternion.identity;
        [System.NonSerialized] public bool MissionRevive;

        private bool _initialized;
        private bool _spawned;
        private bool _launched;
        private bool _dead;
        private int _startMs;
        private Vector3 _launchPos;
        private Vector3 _chassisLocal;

        void Start() { Init(); }

        private void Init()
        {
            if (_initialized) { return; }
            _initialized = true;
            if (!TargetHealth) { TargetHealth = GetComponent<SaccTarget>(); }
            if (Chassis) { _chassisLocal = Chassis.localPosition; }
            ApplyState();
        }

        /// 배치 시각(생성기가 MissionSpawn 전에 넣는다 — 모든 클라이언트가 같다).
        public void SetStartTime(int ms) { _startMs = ms; }

        // ───────────── 상태 (모듈·표적 마커·레이더가 읽는다) ─────────────

        public bool IsDestroyed() { return TargetHealth != null && TargetHealth.Health <= 0f; }
        public bool IsAirborne() { return _launched && !_dead; }
        public int GetLaunchMs() { return _startMs + Mathf.RoundToInt(LaunchDelay * 1000f); }

        /// 지금 해수면 고도(m).
        public float GetAltitude() { return transform.position.y; }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            if (!_spawned || _dead) { return; }
            float t = (Networking.GetServerTimeInMilliseconds() - _startMs) * 0.001f;
            if (t < LaunchDelay)
            {
                if (Rail) { Rail.localRotation = Quaternion.Euler(-RailErectAngle * Mathf.Clamp01(t / Mathf.Max(LaunchDelay * 0.8f, 0.01f)), 0f, 0f); }
                return;
            }
            if (!_launched)
            {
                _launched = true;
                ApplyState();
            }
            float k = Mathf.Clamp01((t - LaunchDelay) / Mathf.Max(FlightTime, 0.01f));
            float y = _launchPos.y + Mathf.Max(CeilingAltitude - _launchPos.y, 0f) * (k * k);
            transform.position = new Vector3(_launchPos.x, y, _launchPos.z);
            // 차체는 발사 자리에 남는다(자식이라 루트와 함께 올라가지 않게 되돌린다)
            if (Chassis) { Chassis.position = transform.TransformPoint(_chassisLocal) + Vector3.down * (y - _launchPos.y); }
        }

        private void ApplyState()
        {
            bool alive = !_dead;
            if (Chassis && Chassis.gameObject.activeSelf != alive) { Chassis.gameObject.SetActive(alive); }
            SetObject(RailMissile, alive && !_launched);
            SetObject(FlyingMissile, alive && _launched);
            SetObject(GroundMarker, alive && !_launched);
            SetObject(AirMarker, alive && _launched);
            SetObject(BlastEffect, _dead);
            if (ChassisHitbox) { ChassisHitbox.enabled = alive && !_launched; }
            if (MissileHitbox) { MissileHitbox.enabled = alive && _launched; }
        }

        private void SetObject(GameObject g, bool on)
        {
            if (g && g.activeSelf != on) { g.SetActive(on); }
        }

        /// SaccTarget이 파괴 시 ExplodeOther에 보내는 이벤트(모든 클라이언트).
        public void Explode()
        {
            _dead = true;
            ApplyState();
            SendCustomEventDelayedSeconds(nameof(_HideBlast), BlastTime);
        }

        /// 폭발 연기는 반복 재생이라 BlastTime 뒤에 끈다(파괴된 TEL은 미션이 끝나거나 다시 쓸 때까지 풀에 남는다).
        public void _HideBlast()
        {
            if (_dead) { SetObject(BlastEffect, false); }
        }

        // ───────────── 미션 생성기 계약 ─────────────

        public void MissionSpawn()
        {
            Init();
            transform.SetPositionAndRotation(MissionSpawnPosition, MissionSpawnRotation);
            if (Chassis) { Chassis.localPosition = _chassisLocal; }
            if (Rail) { Rail.localRotation = Quaternion.identity; }
            _launchPos = MissionSpawnPosition;
            _spawned = true;
            _launched = false;
            if (MissionRevive)
            {
                _dead = false;
                if (Networking.IsOwner(gameObject) && TargetHealth) { SendCustomEventDelayedFrames(nameof(_RespawnTargetHealth), 2); }
            }
            else { _dead = IsDestroyed(); }   // 늦게 들어온 클라이언트: 생사는 SaccTarget 동기화를 따른다
            ApplyState();
        }

        public void MissionDespawn()
        {
            Init();
            _spawned = false;
        }

        /// 풀에서 막 켠 유닛은 SaccTarget의 Start가 아직 안 돌아 2프레임 뒤에 부른다(ACO_Warship과 같다).
        public void _RespawnTargetHealth()
        {
            if (TargetHealth) { TargetHealth.RespawnTarget(); }
        }
    }
}
