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
        [Tooltip("최대 체력(6-249). Bake가 SaccTarget 체력을 넣는다 — 생성기가 스폰 때마다 모든 화면에 넣어, 늦게 켜진 화면이 깎인 체력을 최대 체력으로 잡지 않게 한다. 0 = 건드리지 않음")]
        public float BaseHealth = 0f;
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
        [Tooltip("유압 실린더: 아랫단(차체 축), 가운데 단, 윗단(이렉터 축). 프리팹 빌더가 넣는다")]
        public Transform RamCylinder;
        public Transform RamMiddle;
        public Transform RamRod;
        public float RamMiddleLength = 2.4f;
        [Tooltip("파괴 시 켤 폭발")]
        public GameObject BlastEffect;
        [Tooltip("비행 중 파괴 시 켤 폭발. 위로 날아가는 파편이 있다. BlastEffect는 지상 파괴만 쓴다")]
        public GameObject AirBlastEffect;
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

        [Header("터널 은신 (6-271)")]
        [Tooltip("Bake가 넣는다 — 지형의 터널 모양(TEL이 터널 중심선을 따라 달린다)")]
        public ACO_TerrainFeatures Features;
        [Tooltip("터널에서 나오는 속도(m/s)")]
        public float TunnelSpeed = 10f;
        [Tooltip("입구 밖에 멈춘 뒤 발사까지(초 — 레일을 세운다)")]
        public float TunnelErectTime = 10f;
        [Tooltip("입구 밖 멈추는 거리(m)")]
        public float TunnelExitDistance = 150f;

        // 미션 생성기 계약(SetProgramVariable + MissionSpawn)
        [System.NonSerialized] public Vector3 MissionSpawnPosition;
        /// 터널 은신 코드(생성기 _groundLeader 칸): -1 = 보통 시한 표적 TEL, 아니면 MakeTunnelCode — 비트 0 = +z 쪽 입구로 나간다, 1~2 = 터널 번호,
        /// 3~ = 배치 뒤 입구 밖에 멈출 때까지(초 — 출격 지연 + 주행 시간, 생성기가 계산). 시작 시각(SetStartTime)은 배치 시각 그대로다(미래 시각이면 늦게 들어온 화면이 부활로 오해한다).
        /// 발사 시각 = 배치 시각 + 멈춤 초 + TunnelErectTime — 지형이 없는 화면도 같은 시각을 얻는다. 나오기 시작하는 시각은 멈춤 − 주행 시간(지형으로 잰다).
        [System.NonSerialized] public int TunnelCode = -1;
        [System.NonSerialized] public Quaternion MissionSpawnRotation = Quaternion.identity;
        [System.NonSerialized] public bool MissionRevive;

        private bool _initialized;
        private bool _spawned;
        private bool _launched;
        private bool _dead;
        private int _startMs;
        private Vector3 _launchPos;
        private Vector3 _chassisLocal;
        // 터널 모드
        private bool _tunnelMode;
        private int _tunnel;
        private float _hideZ;
        private float _exitZ;
        private int _driveMs;
        private int _emergeMs;
        private int _stopMs;
        private bool _tunnelPending;   // 터널 코드는 받았는데 이 화면 지형에 아직 그 터널이 없다(늦게 들어온 화면) — Update가 다시 본다
        private float _spawnTime;
        private const float TUNNEL_AXIS_TOLERANCE = 6f;
        private const float TUNNEL_WAIT = 60f;   // 이 화면에서 켠 뒤 이만큼 기다려도 터널이 없으면 숨은 자리에서 쏜다

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

        public static int MakeTunnelCode(int tunnel, bool exitPlusZ, int stopSec)
        {
            return (exitPlusZ ? 1 : 0) | ((tunnel & 3) << 1) | (Mathf.Clamp(stopSec, 0, 4000) << 3);
        }
        public static int TunnelOf(int code) { return (code >> 1) & 3; }
        public static bool TunnelExitPlusZ(int code) { return (code & 1) != 0; }
        public static int TunnelStopSec(int code) { return code >> 3; }

        /// 이 화면에 마지막으로 적용된 배치 시각(생성기 배치 시각과 다르면 아직 이번 배치가 적용되지 않았다).
        public int GetStartMs() { return _startMs; }

        // ───────────── 상태 (모듈·표적 마커·레이더가 읽는다) ─────────────

        public bool IsDestroyed() { return TargetHealth != null && TargetHealth.Health <= 0f; }
        public bool IsAirborne() { return _launched && !_dead; }
        public int GetLaunchMs() { return LaunchBaseMs() + Mathf.RoundToInt(ErectTime() * 1000f); }

        /// 레일이 일어나기 시작하는 시각(터널 은신: 입구 밖에 멈춘 순간 — 지형과 무관하게 모든 화면이 같다).
        private int LaunchBaseMs() { return TunnelCode >= 0 ? _stopMs : _startMs; }
        private float ErectTime() { return TunnelCode >= 0 ? TunnelErectTime : LaunchDelay; }

        /// 터널 은신: 나오기 시작하는 시각(-1 = 터널 은신 아님). 이 화면에 터널이 아직 없으면 멈추는 시각.
        public int GetEmergeMs() { return TunnelCode < 0 ? -1 : (_tunnelMode ? _emergeMs : _stopMs); }
        /// 터널 은신 TEL인가(이 화면 지형에 터널이 아직 없어 기다리는 중도 포함).
        public bool IsTunnelMode() { return TunnelCode >= 0; }

        /// 지금 해수면 고도(m).
        public float GetAltitude() { return transform.position.y; }

        // ───────────── 매 프레임 ─────────────

        void Update()
        {
            if (!_spawned || _dead) { return; }
            int now = Networking.GetServerTimeInMilliseconds();
            if (TunnelCode >= 0 && !_tunnelMode)
            {
                // 이 화면 지형에 터널이 아직 없다(늦게 들어온 화면 — 생성기는 지형을 기다리지 않고 유닛을 켠다).
                // 놓이면 바로 터널 모드 — 위치 · 시각이 서버 시각 함수라 그 순간 제자리에 맞는다. 그때까지는 숨은 자리에서 기다린다.
                if (TrySetupTunnel()) { _tunnelPending = false; }
                else if (_tunnelPending)
                {
                    if (Time.time - _spawnTime < TUNNEL_WAIT) { return; }
                    // 끝내 안 놓인다(지형이 어긋난 화면 — 드묾): 숨은 자리에서 같은 시각에 쏜다. 그 뒤에 놓여도 위에서 터널 모드로 바꾼다
                    _tunnelPending = false;
                    Debug.LogWarning("[ACO] TEL " + gameObject.name + ": 이 화면에 터널이 없어 숨은 자리에서 발사");
                }
            }
            if (_tunnelMode && now - _stopMs < 0)
            {
                // 터널 안에 숨어 있다가 중심선을 따라 나온다
                float f = now - _emergeMs <= 0 ? 0f : Mathf.Clamp01((now - _emergeMs) / (float)Mathf.Max(_driveMs, 1));
                PlaceInTunnel(Mathf.Lerp(_hideZ, _exitZ, f));
                return;
            }
            float delay = ErectTime();
            float t = (now - LaunchBaseMs()) * 0.001f;
            if (t < delay)
            {
                if (_tunnelMode && transform.position != _launchPos) { PlaceInTunnel(_exitZ); }
                if (Rail) { Rail.localRotation = Quaternion.Euler(-RailErectAngle * Mathf.Clamp01(t / Mathf.Max(delay * 0.8f, 0.01f)), 0f, 0f); }
                AimRam();
                return;
            }
            if (!_launched)
            {
                _launched = true;
                // 발사 뒤에 들어온 클라이언트는 일어나는 과정을 못 봤다 — 다 선 자세로 맞춘다
                if (_tunnelMode) { PlaceInTunnel(_exitZ); }   // 발사 뒤에 들어온 화면: 숨은 자리 방향이 아니라 출구 방향으로(곡선 터널)
                if (Rail) { Rail.localRotation = Quaternion.Euler(-RailErectAngle, 0f, 0f); }
                AimRam();
                ApplyState();
            }
            float k = Mathf.Clamp01((t - delay) / Mathf.Max(FlightTime, 0.01f));
            float y = _launchPos.y + Mathf.Max(CeilingAltitude - _launchPos.y, 0f) * (k * k);
            transform.position = new Vector3(_launchPos.x, y, _launchPos.z);
            // 차체는 발사 자리에 남는다(자식이라 루트와 함께 올라가지 않게 되돌린다)
            if (Chassis) { Chassis.position = transform.TransformPoint(_chassisLocal) + Vector3.down * (y - _launchPos.y); }
        }

        /// 터널 모드: 축 위치 z의 중심선 위에 출구를 보고 선다.
        private void PlaceInTunnel(float z)
        {
            if (Features == null || Features.GetTunnelCount() <= _tunnel) { return; }   // 리롤 중(터널 기록이 비었다) — 원점으로 튀지 않게
            Vector3 p = Features.TunnelPoint(_tunnel, z, 0f, 0f);
            Vector3 fwd = Features.TunnelForward(_tunnel, z) * (_exitZ >= _hideZ ? 1f : -1f);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up));
        }

        /// 유압 실린더 세 단을 두 축 사이에 놓는다. 겹쳐 있던 단이 이렉터가 일어나면서 뽑혀 나온다.
        private void AimRam()
        {
            if (!RamCylinder || !RamMiddle || !RamRod) { return; }
            Vector3 bottom = RamCylinder.position;
            Vector3 top = RamRod.position;
            Vector3 along = top - bottom;
            if (along.sqrMagnitude < 0.01f) { return; }
            Vector3 up = transform.up;
            Quaternion aim = Quaternion.LookRotation(along, up);
            RamCylinder.rotation = aim;
            RamMiddle.rotation = aim;
            RamMiddle.position = (bottom + top) * 0.5f - along.normalized * (RamMiddleLength * 0.5f);
            RamRod.rotation = Quaternion.LookRotation(-along, up);
        }

        private void ApplyState()
        {
            bool alive = !_dead;
            if (Chassis && Chassis.gameObject.activeSelf != alive) { Chassis.gameObject.SetActive(alive); }
            SetObject(RailMissile, alive && !_launched);
            SetObject(FlyingMissile, alive && _launched);
            SetObject(GroundMarker, alive && !_launched);
            SetObject(AirMarker, alive && _launched);
            SetObject(BlastEffect, _dead && !_launched);
            SetObject(AirBlastEffect, _dead && _launched);
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
            if (!_dead) { return; }
            SetObject(BlastEffect, false);
            SetObject(AirBlastEffect, false);
        }

        // ───────────── 미션 생성기 계약 ─────────────

        public void MissionSpawn()
        {
            Init();
            transform.SetPositionAndRotation(MissionSpawnPosition, MissionSpawnRotation);
            if (Chassis) { Chassis.localPosition = _chassisLocal; }
            if (Rail) { Rail.localRotation = Quaternion.identity; }
            AimRam();
            _launchPos = MissionSpawnPosition;
            _tunnelMode = false;
            _tunnelPending = false;
            if (TunnelCode >= 0)
            {
                _stopMs = _startMs + TunnelStopSec(TunnelCode) * 1000;
                _emergeMs = _stopMs;
                _spawnTime = Time.time;
                // 늦게 들어온 화면은 생성기가 지형을 기다리지 않고 유닛을 켠다 — 터널이 아직 없으면 Update가 놓일 때까지 다시 본다
                if (!TrySetupTunnel()) { _tunnelPending = true; }
            }
            _spawned = true;
            _launched = false;
            if (MissionRevive)
            {
                _dead = false;
                if (TargetHealth)
                {
                    if (Networking.IsOwner(gameObject)) { SendCustomEventDelayedFrames(nameof(_RespawnTargetHealth), 2); }
                    else { SendCustomEventDelayedFrames(nameof(_LocalRefillHealth), 3); }   // 소유자가 아닌 화면은 같은 값으로 직접 채운다(6-247)
                }
            }
            else { _dead = IsDestroyed(); }   // 늦게 들어온 클라이언트: 생사는 SaccTarget 동기화를 따른다
            ApplyState();
        }

        /// 이 화면 지형에 터널이 있고 숨은 자리가 그 중심선 위면 터널 모드 값을 정한다.
        private bool TrySetupTunnel()
        {
            int tunnel = TunnelOf(TunnelCode);
            if (Features == null || !Features.IsOnTunnelAxis(tunnel, MissionSpawnPosition, TUNNEL_AXIS_TOLERANCE)) { return false; }
            _tunnel = tunnel;
            _tunnelMode = true;
            float half = Features.GetTunnelHalfLength(_tunnel);
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, Features.GetTunnelYaw(_tunnel), 0f)) * (MissionSpawnPosition - Features.GetTunnelCenter(_tunnel));
            _hideZ = local.z;
            _exitZ = (TunnelExitPlusZ(TunnelCode) ? 1f : -1f) * (half + TunnelExitDistance);
            _driveMs = Mathf.RoundToInt(Mathf.Abs(_exitZ - _hideZ) / Mathf.Max(TunnelSpeed, 0.1f) * 1000f);
            _emergeMs = _stopMs - _driveMs;
            _launchPos = Features.TunnelPoint(_tunnel, _exitZ, 0f, 0f);
            return true;
        }

        public void MissionDespawn()
        {
            Init();
            _spawned = false;
            _launched = false;   // 다시 쓸 때 적용 전 몇 프레임 동안 지난 발사 상태(마감 지난 탄도탄)로 보이지 않게
        }

        /// 풀에서 막 켠 유닛은 SaccTarget의 Start가 아직 안 돌아 2프레임 뒤에 부른다(ACO_Warship과 같다).
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
            if (!_dead) { return; }
            _dead = false;
            ApplyState();
            Debug.Log("[ACO_SYNC] 표시 복구(격파 상태였음): " + gameObject.name + " 체력 " + TargetHealth.Health.ToString("F0") + "/" + TargetHealth.FullHealth.ToString("F0"));
        }
    }
}
