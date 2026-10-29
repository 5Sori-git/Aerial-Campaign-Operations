using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 목표 방향을 받아 뱅크-투-턴(선회 시 롤이 먼저 들어가는) 비행 모델로 기체를 움직인다.
    /// Update를 직접 돌리지 않고 ACO_NPCFighterAI가 FlightTick을 호출한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_NPCFlightController : UdonSharpBehaviour
    {
        [Header("속도 (m/s)")]
        public float CruiseSpeed = 140f;
        public float MaxSpeed = 260f;
        public float MinSpeed = 70f;
        public float Acceleration = 40f;

        [Header("롤 (선회 시 기울기)")]
        [Tooltip("수평 방향 오차 1도당 목표 뱅크각(도). 값이 크면 조금만 틀어져도 깊게 눕는다")]
        public float BankPerYawError = 2.5f;
        [Tooltip("최대 뱅크각(도)")]
        public float MaxBankAngle = 75f;
        [Tooltip("목표 뱅크각을 따라가는 반응 속도")]
        public float RollGain = 3f;
        [Tooltip("초당 최대 롤 속도(도)")]
        public float MaxRollRate = 150f;

        [Header("선회")]
        [Tooltip("선회 성능(G). 뱅크각과 속도로 선회율이 결정된다 — 빠를수록 선회 반경이 커진다")]
        public float TurnG = 7f;
        [Tooltip("초당 최대 선회율(도)")]
        public float MaxTurnRate = 60f;

        [Header("피치")]
        public float PitchGain = 2f;
        [Tooltip("초당 최대 피치 속도(도)")]
        public float MaxPitchRate = 45f;
        [Tooltip("기수각 한계(도). 수직 기동으로 자세가 뒤집히는 것을 막는다")]
        public float MaxPitchAttitude = 65f;

        [Header("지형 회피")]
        [Tooltip("지형 위로 유지할 최소 여유 고도(m). 절대 Y가 아니라 아래 지형까지의 거리다")]
        public float MinAltitude = 80f;
        [Tooltip("아래에 지형이 없을 때(바다/허공) 기준으로 삼을 절대 고도")]
        public float GroundHeight = 0f;
        [Tooltip("지형/장애물로 판정할 레이어. 기본값 2049 = Default + Environment")]
        public LayerMask GroundLayers = 2049;
        [Tooltip("지형 탐지 주기(초). 매 프레임 캐스트하면 유닛 수만큼 비용이 늘어나므로 간격을 둔다")]
        public float ProbeInterval = 0.15f;
        [Tooltip("몇 초 앞의 경로를 미리 볼지. 실제 거리는 속도 × 이 값")]
        public float LookAheadTime = 3f;
        [Tooltip("전방 SphereCast 반경(m). 산/구조물을 여유 있게 잡으려면 크게")]
        public float LookAheadRadius = 15f;
        [Tooltip("전방 장애물이 가까울수록 이 각도까지 더 급하게 상승한다")]
        public float MaxAvoidClimbAngle = 55f;
        [Tooltip("여유 고도 오차를 이 시간(초)에 걸쳐 메우는 속도로 상승/하강을 제한한다. 작을수록 바닥에 급하게 붙는다")]
        public float AltitudeSettleTime = 2f;
        [Tooltip("하강 속도를 이 시간(초)만큼 미리 반영해 일찍 기수를 든다. 피치 반응이 늦어 바닥을 파고드는 것을 막는다")]
        public float SinkLeadTime = 0.8f;

        [System.NonSerialized] public float Speed;
        [System.NonSerialized] public Vector3 DesiredDirection = Vector3.forward;
        [System.NonSerialized] public float DesiredSpeed;
        // 진단용(AI의 DebugFlight 로그): 지난 프레임의 원하는 상승각과 지면 회피가 허용한 최소 상승각(도)
        [System.NonSerialized] public float LastWantAngle;
        [System.NonSerialized] public float LastFloorAngle;

        private float _probeTimer;
        private float _groundY;
        private bool _hasGround;
        private float _obstacleUrgency; // 0 = 장애물 없음, 1 = 바로 앞 (캐스트 원값)
        private float _obstacleUrgencySmooth; // 실제로 쓰는 값. 탐지 주기마다 계단식으로 바뀌지 않게 한다

        void Start()
        {
            if (Speed <= 0f) { Speed = CruiseSpeed; }
            if (DesiredSpeed <= 0f) { DesiredSpeed = CruiseSpeed; }
        }

        public void FlightTick(float dt)
        {
            if (dt <= 0f) { return; }

            ProbeTerrain(dt);

            Vector3 dir = DesiredDirection;
            if (dir.sqrMagnitude < 0.000001f) { dir = transform.forward; }
            dir = dir.normalized;
            dir = ApplyGroundAvoidance(dir, dt);

            // 오차를 월드 기준 두 축으로 분리한다: 방위(수평) → 뱅크 → 정상선회 / 상승각(수직) → 지평선 기준 피치.
            // 뱅크한 기체 좌표에서 피치 오차를 재면, 옆의 타겟이 "위"로 잡혀 선회할 때마다 당긴 힘이 고도 상승으로 새고
            // 아래쪽 타겟도 선회 중엔 위로 보여 내려가지 못한다(도그파이트에서 상승만 하던 원인).
            float headingError = HeadingError(dir);
            float elevationError = Elevation(dir) - Elevation(transform.forward);

            ApplyRoll(headingError, GetBankAngle(), dt);
            ApplyCoordinatedTurn(dt);
            ApplyPitch(elevationError, dt);
            ApplyThrust(dt);
        }

        /// 지평선 위에서 본 방위 오차(도). +: 목표가 오른쪽. 목표가 거의 수직이면 0(뱅크하지 않고 피치로만 처리).
        private float HeadingError(Vector3 dir)
        {
            Vector3 fwdFlat = new Vector3(transform.forward.x, 0f, transform.forward.z);
            Vector3 dirFlat = new Vector3(dir.x, 0f, dir.z);
            if (fwdFlat.sqrMagnitude < 0.000001f || dirFlat.sqrMagnitude < 0.000001f) { return 0f; }
            fwdFlat = fwdFlat.normalized;
            dirFlat = dirFlat.normalized;
            return Mathf.Atan2(Vector3.Dot(Vector3.Cross(fwdFlat, dirFlat), Vector3.up), Vector3.Dot(fwdFlat, dirFlat)) * Mathf.Rad2Deg;
        }

        /// 지평선 기준 상승각(도). +: 위.
        private float Elevation(Vector3 v)
        {
            return Mathf.Asin(Mathf.Clamp(v.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// 수평 비행으로 복귀할 때도 이 값이 0으로 수렴하므로 롤이 저절로 펴진다.
        public float GetBankAngle()
        {
            // 수평일 때 right.y=0, up.y=1 → 0도. 오른쪽으로 눕으면 +.
            return Mathf.Atan2(-transform.right.y, transform.up.y) * Mathf.Rad2Deg;
        }

        /// 아래쪽 지형 높이와 전방 장애물을 주기적으로 측정한다.
        /// 매 프레임 캐스트하지 않는 이유: 유닛 수만큼 비용이 선형으로 늘어나기 때문(설계 0.6절).
        private void ProbeTerrain(float dt)
        {
            _probeTimer -= dt;
            if (_probeTimer > 0f) { return; }
            _probeTimer = ProbeInterval;

            Vector3 pos = transform.position;

            // 아래 지형 높이 — 절대 Y 대신 이 값을 기준으로 여유 고도를 계산한다.
            RaycastHit down;
            float downRange = MinAltitude * 4f + 50f;
            if (Physics.Raycast(pos, Vector3.down, out down, downRange, GroundLayers, QueryTriggerInteraction.Ignore))
            {
                _groundY = down.point.y;
                _hasGround = true;
            }
            else
            {
                _hasGround = false;
            }

            // 전방 경로의 산/구조물 — 가까울수록 긴급도가 1에 가까워진다.
            // 하강 중엔 수평 방향으로 본다. 기수 방향으로 쏘면 아래의 평지가 '장애물'로 잡혀,
            // 상승 → 기수가 들려 안 잡힘 → 다시 하강을 반복한다(지면 근처에서 통통 튀던 원인 중 하나).
            // 바닥과의 거리는 아래 캐스트 + 연속 바닥 제약이 따로 맡는다.
            Vector3 castDir = transform.forward;
            if (castDir.y < 0f)
            {
                castDir.y = 0f;
                castDir = castDir.sqrMagnitude > 0.000001f ? castDir.normalized : Vector3.forward;
            }
            float lookAhead = Mathf.Max(Speed, 1f) * LookAheadTime;
            RaycastHit ahead;
            if (Physics.SphereCast(pos, LookAheadRadius, castDir, out ahead, lookAhead, GroundLayers, QueryTriggerInteraction.Ignore))
            {
                _obstacleUrgency = Mathf.Clamp01(1f - (ahead.distance / lookAhead));
            }
            else
            {
                _obstacleUrgency = 0f;
            }
        }

        /// 원하는 방향의 상승각에 "바닥"을 건다. 방위는 그대로 두고 상승각만 max(원하는 각, 바닥 각)으로 올린다.
        /// 예전처럼 최소 고도 아래에서 25~55° 상승으로 '덮어쓰는' 스위치식이면, 낮은 타겟을 쫓을 때
        /// 80m 아래 → 급상승 → 80m 위 → 다시 하강을 반복해 통통 튄다. 여기서는 바닥 각이 고도에 따라 연속으로 변해
        /// 최소 고도에 부드럽게 붙어 수평 비행한다(시뮬레이션: 저공 추격 32초 동안 상승/하강 전환 8회 → 0회).
        private Vector3 ApplyGroundAvoidance(Vector3 dir, float dt)
        {
            // 장애물 긴급도: 올라갈 땐 즉시, 내려갈 땐 천천히 — 캐스트가 맞았다 안 맞았다 해도 기수가 떨리지 않는다.
            if (_obstacleUrgency > _obstacleUrgencySmooth) { _obstacleUrgencySmooth = _obstacleUrgency; }
            else { _obstacleUrgencySmooth = Mathf.MoveTowards(_obstacleUrgencySmooth, _obstacleUrgency, dt); }

            float clearance = GetClearance();
            float sinkRate = transform.forward.y * Speed; // 수직 속도(+: 상승)

            // 여유 고도 오차를 AltitudeSettleTime에 걸쳐 메우는 수직 속도 → 그 속도를 내는 상승각이 바닥이다.
            // 높이 있으면 바닥이 크게 음수라 자유롭게 내려가고, 최소 고도에 가까울수록 0으로, 아래면 양수(상승)가 된다.
            float predicted = clearance + sinkRate * SinkLeadTime;
            float requiredVz = (MinAltitude - predicted) / Mathf.Max(AltitudeSettleTime, 0.1f);
            float floorAngle = Mathf.Asin(Mathf.Clamp(requiredVz / Mathf.Max(Speed, 1f), -1f, 1f)) * Mathf.Rad2Deg;
            // 전방 장애물: 긴급도만큼 바닥 각을 최대 상승각 쪽으로 끌어올린다. 긴급도 0이면 영향 없음.
            // (예전엔 max(바닥 각, 최대 상승각 x 긴급도)였는데, 장애물이 없으면 이 항이 0도라 바닥 각이 0도로 올라가
            //  고도와 무관하게 수평 아래로 내려가지 못했다 — 전투 중 하강을 안 하던 원인.)
            floorAngle = Mathf.Lerp(floorAngle, Mathf.Max(floorAngle, MaxAvoidClimbAngle), _obstacleUrgencySmooth);
            floorAngle = Mathf.Clamp(floorAngle, -MaxPitchAttitude, MaxAvoidClimbAngle);

            float wantAngle = Elevation(dir);
            LastWantAngle = wantAngle;
            LastFloorAngle = floorAngle;
            if (wantAngle >= floorAngle) { return dir; }

            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 0.000001f)
            {
                flat = new Vector3(transform.forward.x, 0f, transform.forward.z);
                if (flat.sqrMagnitude < 0.000001f) { flat = Vector3.forward; }
            }
            flat = flat.normalized;
            return (flat + Vector3.up * Mathf.Tan(floorAngle * Mathf.Deg2Rad)).normalized;
        }

        /// 지형 위 현재 여유 고도. AI가 저고도에서 공격 진입을 자제할 때 쓴다.
        public float GetClearance()
        {
            float groundRef = _hasGround ? _groundY : GroundHeight;
            return transform.position.y - groundRef;
        }

        private void ApplyRoll(float headingError, float currentBank, float dt)
        {
            float desiredBank = Mathf.Clamp(headingError * BankPerYawError, -MaxBankAngle, MaxBankAngle);
            float rollRate = Mathf.Clamp((desiredBank - currentBank) * RollGain, -MaxRollRate, MaxRollRate);
            // Unity의 +Z 회전은 왼쪽 롤이라 부호를 뒤집는다.
            transform.Rotate(0f, 0f, -rollRate * dt, Space.Self);
        }

        /// 정상선회: 눕힌 각도와 속도가 수평 선회율을 만든다. 롤이 먼저 들어가고 선회가 따라오므로 기동이 자연스럽다.
        private void ApplyCoordinatedTurn(float dt)
        {
            if (Speed <= 1f) { return; }

            float bankRad = GetBankAngle() * Mathf.Deg2Rad;
            float turnRate = (TurnG * 9.81f * Mathf.Tan(bankRad) / Speed) * Mathf.Rad2Deg;
            turnRate = Mathf.Clamp(turnRate, -MaxTurnRate, MaxTurnRate);
            // 지평선 기준으로 기수를 돌려야 뱅크각이 유지된다.
            transform.Rotate(Vector3.up, turnRate * dt, Space.World);
        }

        private void ApplyPitch(float elevationError, float dt)
        {
            float pitchRate = Mathf.Clamp(elevationError * PitchGain, -MaxPitchRate, MaxPitchRate);

            float pitchAttitude = Elevation(transform.forward);
            if (pitchAttitude > MaxPitchAttitude && pitchRate > 0f) { pitchRate = 0f; }
            else if (pitchAttitude < -MaxPitchAttitude && pitchRate < 0f) { pitchRate = 0f; }

            // 기체 좌표가 아니라 "지평선 기준 오른쪽" 축으로 돌려야 뱅크 상태와 무관하게 순수하게 오르내리기만 한다.
            // 이 축을 +로 돌리면 기수가 내려가므로 부호를 뒤집는다.
            Vector3 horizontalRight = Vector3.Cross(Vector3.up, transform.forward);
            if (horizontalRight.sqrMagnitude < 0.000001f) { return; }
            transform.Rotate(horizontalRight.normalized, -pitchRate * dt, Space.World);
        }

        private void ApplyThrust(float dt)
        {
            float target = DesiredSpeed <= 0f ? CruiseSpeed : DesiredSpeed;
            target = Mathf.Clamp(target, MinSpeed, MaxSpeed);
            Speed = Mathf.MoveTowards(Speed, target, Acceleration * dt);
            transform.position += transform.forward * Speed * dt;
        }
    }
}
