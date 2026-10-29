using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace ACO
{
    /// NPC 전투기 동기화. ACO_NPCFighterAI와 같은 오브젝트에 둔다(소유권이 같아야 한다).
    /// 오너만 AI와 비행을 계산한다. 원격 클라이언트는 AI를 재생하지 않고, 받은 상태(위치/기수 방향/속력/각속도/가속도)로
    /// "같은 각속도로 계속 돈다"고 예측해 그린다(데드 레커닝). 롤은 보내지 않고 예측 선회율에서 역산한다. 오너는 원격의 예측을 그대로 따라 해 보다가
    /// 실제와 일정 이상 벌어질 때만 전송한다 — 직선 비행은 거의 안 보내고, 기동이 바뀌는 순간에만 보낸다.
    /// AI 쪽 상태(모드/웨이포인트/표적)는 보낼 때 AI에게 묻고, 받으면 AI에게 돌려준다. 매 프레임 호출은 AI의 Update가 한다
    /// (비행 계산 → 운동 측정 → 전송 순서가 프레임마다 같아야 하므로 자체 Update를 쓰지 않는다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_NPCFighterNet : UdonSharpBehaviour
    {
        [Header("참조 (비우면 같은 오브젝트에서 찾는다)")]
        public ACO_NPCFighterAI AI;
        public ACO_NPCFlightController Flight;

        [Header("오차 기반 전송")]
        [Tooltip("원격 화면의 예측 위치가 실제와 이만큼(m) 벌어지면 상태를 보낸다")]
        public float PositionErrorThreshold = 4f;
        [Tooltip("가장 가까운 다른 플레이어와의 거리 1km당 허용 오차에 더하는 값(m). 멀리 있는 NPC는 덜 자주 보낸다")]
        public float ThresholdPerKm = 10f;
        [Tooltip("원격 화면의 예측 기수 방향이 실제와 이 각도(도) 이상 벌어지면 보낸다. 롤은 기준에 넣지 않는다 — 원격이 선회율로 직접 계산한다")]
        public float DirectionErrorThreshold = 5f;
        [Tooltip("전송 최소 간격(초)")]
        public float MinSendInterval = 0.3f;
        [Tooltip("오차가 없어도 이 주기로 보낸다(패킷 유실 대비)")]
        public float HeartbeatInterval = 4f;
        [Tooltip("네트워크가 밀리면(Networking.IsClogged) 위 기준들을 최대 이 배수까지 자동으로 늘린다")]
        public float MaxCongestionScale = 4f;

        [Header("교전 상대에게 소유권 넘기기")]
        [Tooltip("교전 중인 플레이어에게 이 NPC의 소유권을 넘긴다. 그 플레이어 화면에서는 동기화 없이 직접 계산되므로 리드 사격과 미사일 예측이 정확해진다")]
        public bool HandoffToEngagedPlayer = true;
        [Tooltip("같은 플레이어와 이 시간(초) 이상 교전해야 넘긴다")]
        public float HandoffHoldTime = 2f;
        [Tooltip("소유권이 바뀐 뒤 이 시간(초) 동안은 다시 넘기지 않는다(오너가 오가며 튀지 않게)")]
        public float HandoffCooldown = 5f;

        [Header("원격 표시")]
        [Tooltip("새 상태를 받았을 때 화면 위치/방향의 차이를 흡수하는 시간 척도(초, 대략 2배면 거의 다 흡수). 작을수록 정확하고 클수록 부드럽다")]
        public float SmoothTime = 0.5f;
        [Tooltip("이보다 크게 벗어나면 보정 대신 즉시 이동한다")]
        public float HardSnapDistance = 200f;
        [Tooltip("켜면 전송/수신/보정을 VRChat 로그에 [ACO_NET]으로 남긴다. 테스트용 — 배포 전에 끈다")]
        public bool DebugNetwork = false;

        // 수신 지연 따라잡기: 최대 시간과 한 스텝 길이. 큰 dt 한 번에 돌리면 원호가 직선처럼 펴진다.
        // 네트워크가 나쁠 때 지연이 1~4초까지 관측됐다 — 그보다 짧으면 넘는 만큼 화면이 뒤처진다.
        private const float MAX_CATCH_UP_TIME = 4f;
        // 지연 1초당 순간이동 기준에 더할 거리 = 속력 × 이 배수. 지연이 길면 같은 기동에도 예측 오차가 커진다.
        private const float SNAP_LAG_FACTOR = 3f;
        // 오차 100m당 보정 시간 배수(최대 MAX_SMOOTH_SCALE배). 큰 오차를 짧게 보정하면 옆으로 미끄러지듯 튀어 보인다.
        private const float MAX_SMOOTH_SCALE = 4f;
        private const float CATCH_UP_STEP = 1f / 30f;
        // 이보다 먼 플레이어는 이 거리로 본다(허용 오차가 끝없이 커지지 않게).
        private const float MAX_VIEWER_DISTANCE = 5000f;
        private const float VIEWER_CHECK_INTERVAL = 1f;
        // 오너 여부 재확인 주기(초). 늦게 들어온 클라이언트는 네트워크가 자리 잡기 전에 IsOwner가 틀린 값을 줄 수 있고,
        // 꺼져 있는 동안 소유권이 바뀌면 OnOwnershipTransferred도 오지 않는다 — 캐시만 믿으면 그 기체는 영영 동기화되지 않는다.
        private const float OWNER_CHECK_INTERVAL = 1f;
        // 순서가 뒤바뀐 패킷으로 볼 최대 과거(ms). 실제로 뒤바뀌는 건 수 초 이내다.
        private const int STALE_WINDOW_MS = 30000;
        private const float HANDOFF_CHECK_INTERVAL = 0.5f;

        // 상태는 동기화 변수가 아니라 네트워크 이벤트(NetState)로 보낸다 — SAV도 위치를 이벤트로 보낸다(SAV_SyncScript.SendSyncData).
        // 동기화 변수로 보냈을 때는 Manual인데도 VRChat이 0.25초마다 다시 직렬화했고 새 상태가 평균 1.5초 늦게 도착했으며
        // (전송 구간은 0.1초), 풀에서 꺼낼 때 수십 초 전 상태가 다시 넘어와 기체가 튀었다. 아래는 보내고 받는 값의 임시 보관이다.
        private byte _syncMode;
        private byte _syncWaypoint;
        private short _syncTargetIdx = -1;
        private Vector3 _syncPos;
        private Vector3 _syncFwd = Vector3.forward;
        private float _syncSpeed;
        private float _syncAccel;
        // 기수 방향의 월드 각속도(rad/s, 롤 제외). 선회 중엔 거의 일정하므로 이것 하나로 원호를 그대로 예측할 수 있다.
        private Vector3 _syncAngVel;
        // 서버 시간(ms)은 모든 클라이언트가 공유하므로 패킷이 늦게 와도 경과 시간을 맞출 수 있다.
        private int _syncTimeMs;

        private bool _initialized;
        private bool _isOwner;

        // 오너: 실제 운동 측정
        private Vector3 _angVel;
        private float _accel;
        private Vector3 _prevFwd = Vector3.forward;
        private float _prevSpeed;
        private bool _hasPrevMotion;

        // 오너: 원격이 지금 그리고 있을 예측(마지막 전송 상태를 같은 모델로 전진)
        private Vector3 _ghostPos;
        private Vector3 _ghostFwd = Vector3.forward;
        private float _ghostSpeed;
        private Vector3 _ghostAngVel;
        private float _ghostAccel;
        private bool _hasGhost;
        private bool _stateDirty;
        private float _sinceSend;
        private float _congestion = 1f;
        private float _viewerTimer;
        private float _ownerCheckTimer;
        private float _viewerDistance = MAX_VIEWER_DISTANCE;
        private VRCPlayerApi[] _players = new VRCPlayerApi[100];

        // 원격: 예측 상태 + 화면 보정 오프셋(표시 위치 = 예측 + 오프셋, 표시 방향 = 오프셋 회전 · 예측 방향)
        private Vector3 _predPos;
        private Vector3 _predFwd = Vector3.forward;
        private float _predSpeed;
        private Vector3 _predAngVel;
        private float _predAccel;
        private bool _hasPrediction;
        private Vector3 _offsetPos;
        private Quaternion _offsetRot = Quaternion.identity;
        // 오프셋은 임계 감쇠 스프링으로 없앤다. 지수 감쇠는 받는 순간 옆으로 미는 속도(오차/시간)가 갑자기 생겨 기동 중에
        // '통통' 튀어 보였다. 스프링은 보정 속도 0에서 출발해 부드럽게 붙는다.
        private Vector3 _offsetVel;
        private Vector3 _offsetRotVec;    // 회전 오프셋(축 × 라디안)
        private Vector3 _offsetRotVel;
        private float _smoothTime = 0.5f; // 이번 보정에 쓰는 시간(오차가 클수록 길다)
        private float _remoteBank;
        private int _lastAppliedTimeMs;
        private bool _hasApplied;

        // 디버그: 다른 코드가 transform을 옮겼는지, 프레임 사이에 튀었는지 확인용
        private Vector3 _lastWrittenPos;
        private bool _hasWrittenPos;
        private float _lastRecvTime = -1f;

        // 소유권 넘기기: 같은 표적과 교전을 시작한 시각, 마지막으로 소유권이 바뀐 시각
        private float _handoffTimer;
        private int _handoffTargetIdx = -1;
        private float _handoffSince;
        private float _lastOwnerChangeTime = -1000f;

        void Start()
        {
            Init();
        }

        /// AI의 Init에서도 부른다(어느 쪽 Start가 먼저 돌지 모른다).
        public void Init()
        {
            if (_initialized) { return; }
            _initialized = true;

            if (!AI) { AI = GetComponent<ACO_NPCFighterAI>(); }
            if (!Flight) { Flight = GetComponent<ACO_NPCFlightController>(); }
            _isOwner = Networking.IsOwner(gameObject);
            ResetMotionCore();
        }

        public bool IsOwner() { return _isOwner; }

        /// AI의 모드/웨이포인트/표적이 바뀌었다 — 다음 틱에 바로 보낸다.
        public void MarkStateDirty() { _stateDirty = true; }

        // ───────────── 오너 ─────────────

        /// AI가 비행 계산을 마친 뒤 매 프레임 부른다(오너).
        public void OwnerTick(float dt)
        {
            TrackMotion(dt);
            NetworkTick(dt);
            HandoffTick(dt);
        }

        /// 교전 중인 플레이어에게 소유권을 넘긴다. 표적은 놓침 거리를 벗어나거나 격추·우선 표적 등장 때만 바뀌므로 자주 넘어가지 않는다.
        /// 피격 판정은 원래 피격자/사수 쪽이고 CombatDirector도 오너가 여럿이어도 동작하므로, 넘겨도 AI·무기 판정은 그대로다.
        private void HandoffTick(float dt)
        {
            if (!HandoffToEngagedPlayer || AI == null) { return; }
            _handoffTimer -= dt;
            if (_handoffTimer > 0f) { return; }
            _handoffTimer = HANDOFF_CHECK_INTERVAL;

            int idx = AI.IsEngaged() ? AI.GetNetTargetIdx() : -1;
            if (idx != _handoffTargetIdx)
            {
                _handoffTargetIdx = idx;
                _handoffSince = Time.time;
                return;
            }
            if (idx < 0) { return; }
            if (Time.time - _handoffSince < HandoffHoldTime) { return; }
            if (Time.time - _lastOwnerChangeTime < HandoffCooldown) { return; }

            VRCPlayerApi pilot = AI.GetTargetPilot();
            if (!Utilities.IsValid(pilot) || pilot.isLocal) { return; }

            if (DebugNetwork) { LogNet("handoff to=" + pilot.displayName + " target=" + idx); }
            _lastOwnerChangeTime = Time.time;
            Networking.SetOwner(pilot, gameObject);
        }

        /// 비행 컨트롤러가 이번 프레임에 만든 기수 방향/속력 변화로 각속도와 가속도를 잰다.
        /// 롤(기수 축 회전)은 위치에 영향이 없는데 가장 빠르게 변해서, 섞으면 예측이 틀어지고 전송만 늘어난다
        /// (첫 테스트에서 전송의 97%가 롤 때문이었다). 그래서 기수 방향의 회전만 재고, 롤은 원격이 선회율로 계산한다.
        private void TrackMotion(float dt)
        {
            if (dt <= 0f || !Flight) { return; }
            Vector3 fwd = transform.forward;
            float speed = Flight.Speed;

            if (_hasPrevMotion)
            {
                // |f0 × f1| = sin(회전각), 방향 = 회전축
                _angVel = Vector3.Lerp(_angVel, Vector3.Cross(_prevFwd, fwd) / dt, 0.5f);
                _accel = Mathf.Lerp(_accel, (speed - _prevSpeed) / dt, 0.2f);
            }

            _prevFwd = fwd;
            _prevSpeed = speed;
            _hasPrevMotion = true;
        }

        /// 원격 예측을 똑같이 따라 해 보고, 실제와 벌어졌을 때만 보낸다.
        private void NetworkTick(float dt)
        {
            _sinceSend += dt;
            if (_hasGhost) { StepGhost(dt); }

            _viewerTimer -= dt;
            if (_viewerTimer <= 0f)
            {
                _viewerTimer = VIEWER_CHECK_INTERVAL;
                UpdateViewerDistance();
            }

            // 네트워크가 소화하는 것보다 많이 보내면 큐가 쌓여 지연이 수 초로 커지고, 그게 곧 순간이동이 된다.
            // 밀리는 동안엔 기준을 늘려 전송을 줄이고, 풀리면 천천히 되돌린다.
            if (Networking.IsClogged) { _congestion = Mathf.Min(_congestion + dt * 2f, Mathf.Max(MaxCongestionScale, 1f)); }
            else { _congestion = Mathf.Max(_congestion - dt * 0.5f, 1f); }

            if (_sinceSend < MinSendInterval * _congestion) { return; }

            float posThreshold = (PositionErrorThreshold + ThresholdPerKm * _viewerDistance * 0.001f) * _congestion;
            float dirThreshold = DirectionErrorThreshold * _congestion;
            float posError = _hasGhost ? Vector3.Distance(transform.position, _ghostPos) : 0f;
            float dirError = _hasGhost ? Vector3.Angle(transform.forward, _ghostFwd) : 0f;

            bool send = _stateDirty || !_hasGhost || _sinceSend >= HeartbeatInterval * _congestion
                || posError > posThreshold || dirError > dirThreshold;
            if (!send) { return; }

            if (DebugNetwork)
            {
                LogNet("send why=" + SendReason(posError, posThreshold, dirError, dirThreshold)
                    + " since=" + _sinceSend.ToString("F2")
                    + " posErr=" + posError.ToString("F1") + "/" + posThreshold.ToString("F1")
                    + " dirErr=" + dirError.ToString("F1") + "/" + dirThreshold.ToString("F1")
                    + " cong=" + _congestion.ToString("F2")
                    + " viewer=" + _viewerDistance.ToString("F0")
                    + " dt=" + Time.deltaTime.ToString("F3")
                    + " spd=" + (Flight != null ? Flight.Speed : 0f).ToString("F0")
                    + " w=" + (_angVel.magnitude * Mathf.Rad2Deg).ToString("F0")
                    + " mode=" + (AI != null ? AI.GetNetMode() : (byte)0));
            }
            SendState();
        }

        private string SendReason(float posError, float posThreshold, float dirError, float dirThreshold)
        {
            if (!_hasGhost) { return "first"; }
            if (_stateDirty) { return "state"; }
            if (posError > posThreshold) { return "pos"; }
            if (dirError > dirThreshold) { return "dir"; }
            return "heartbeat";
        }

        private void LogNet(string msg)
        {
            Debug.Log("[ACO_NET] " + gameObject.name
                + " srv=" + Networking.GetServerTimeInMilliseconds()
                + " t=" + Time.time.ToString("F2")
                + " owner=" + _isOwner
                + " " + msg);
        }

        /// 가장 가까운 다른 플레이어까지의 거리. 혼자 있으면 최대값이 되어 하트비트만 나간다.
        private void UpdateViewerDistance()
        {
            int count = VRCPlayerApi.GetPlayerCount();
            if (_players == null || _players.Length < count) { _players = new VRCPlayerApi[count]; }
            _players = VRCPlayerApi.GetPlayers(_players);

            Vector3 myPos = transform.position;
            float best = MAX_VIEWER_DISTANCE;
            for (int i = 0; i < count && i < _players.Length; i++)
            {
                VRCPlayerApi p = _players[i];
                if (!Utilities.IsValid(p) || p.isLocal) { continue; }
                float d = Vector3.Distance(myPos, p.GetPosition());
                if (d < best) { best = d; }
            }
            _viewerDistance = best;
        }

        public void SendState()
        {
            if (!_isOwner || !Flight) { return; }

            _syncMode = AI != null ? AI.GetNetMode() : (byte)0;
            _syncWaypoint = AI != null ? (byte)AI.GetNetWaypoint() : (byte)0;
            _syncTargetIdx = AI != null ? (short)AI.GetNetTargetIdx() : (short)-1;
            _syncPos = transform.position;
            _syncFwd = transform.forward;
            _syncSpeed = Flight.Speed;
            _syncAccel = _accel;
            _syncAngVel = _angVel;
            _syncTimeMs = Networking.GetServerTimeInMilliseconds();

            _ghostPos = _syncPos;
            _ghostFwd = _syncFwd;
            _ghostSpeed = _syncSpeed;
            _ghostAngVel = _syncAngVel;
            _ghostAccel = _syncAccel;
            _hasGhost = true;

            _stateDirty = false;
            _sinceSend = 0f;

            // 이벤트 파라미터는 8개까지라 모드/웨이포인트/표적은 int 하나에 묶는다. 표적은 -1(없음)이 있어 +1 해서 넣는다
            // (음수를 short로 되돌리는 변환은 Udon에서 범위 검사에 걸린다).
            int packed = _syncMode | (_syncWaypoint << 8) | ((_syncTargetIdx + 1) << 16);
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetState),
                packed, _syncPos, _syncFwd, _syncSpeed, _syncAccel, _syncAngVel, _syncTimeMs);
        }

        /// 부활 직후. 부활 이벤트와 상태 패킷의 도착 순서가 뒤바뀔 수 있으므로 0.5초 뒤에 한 번 더 보낸다.
        public void SendStateAfterRespawn()
        {
            SendState();
            _sinceSend = HeartbeatInterval - 0.5f;
        }

        /// 늦게 들어온 플레이어는 지난 이벤트를 받지 못하므로 바로 한 번 보낸다(놓쳐도 하트비트가 4초 안에 맞춘다).
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!_isOwner || player == null || player.isLocal) { return; }
            if (AI && !AI.IsAlive()) { return; }
            SendState();
        }

        // ───────────── 원격 ─────────────

        /// 오너가 보낸 상태. 기동 중엔 MinSendInterval 간격(초당 3회 남짓)까지 보내므로 기본 한도보다 여유를 둔다.
        [NetworkCallable(10)]
        public void NetState(int packed, Vector3 pos, Vector3 fwd, float speed, float accel, Vector3 angVel, int timeMs)
        {
            // 늦게 들어온 클라이언트는 Start보다 이게 먼저 올 수 있다.
            Init();
            if (AI) { AI.EnsureInit(); }
            if (_isOwner) { SyncOwnership(); }
            if (_isOwner) { return; }

            _syncMode = (byte)(packed & 0xFF);
            _syncWaypoint = (byte)((packed >> 8) & 0xFF);
            _syncTargetIdx = (short)(((packed >> 16) & 0xFFFF) - 1);
            _syncPos = pos;
            _syncFwd = fwd;
            _syncSpeed = speed;
            _syncAccel = accel;
            _syncAngVel = angVel;
            _syncTimeMs = timeMs;
            ApplyReceivedState();
        }

        private void ApplyReceivedState()
        {
            // 시각 0 = 빈 상태(안전장치). 적용하면 원점에 놓이고 이후 진짜 상태가 전부 "옛 상태"로 버려질 수 있다.
            if (_syncTimeMs == 0)
            {
                if (DebugNetwork) { LogNet("recv empty — ignored"); }
                return;
            }
            // 순서가 뒤바뀐 옛 상태는 버린다. int 뺄셈이라 서버 시간이 오버플로로 넘어가도 비교가 맞다. 기준보다 STALE_WINDOW_MS 넘게 과거면 옛 상태가 아니라
            // 기준이 잘못된 것이므로 받는다(안전장치).
            int age = _syncTimeMs - _lastAppliedTimeMs;
            if (_hasApplied && age <= 0 && age > -STALE_WINDOW_MS)
            {
                if (DebugNetwork)
                {
                    LogNet("recv stale age=" + ((_lastAppliedTimeMs - _syncTimeMs) * 0.001f).ToString("F2"));
                }
                return;
            }
            _hasApplied = true;
            _lastAppliedTimeMs = _syncTimeMs;

            if (AI) { AI.ApplyNetState(_syncMode, _syncWaypoint, _syncTargetIdx); }
            bool alive = AI == null || AI.IsAlive();

            Vector3 shownPos = transform.position;
            Vector3 shownFwd = transform.forward;

            _predPos = _syncPos;
            _predFwd = _syncFwd.sqrMagnitude > 0.000001f ? _syncFwd.normalized : shownFwd;
            _predSpeed = _syncSpeed;
            _predAngVel = _syncAngVel;
            _predAccel = _syncAccel;
            _hasPrediction = true;

            // 패킷이 보내진 뒤 흐른 시간만큼 같은 원호를 따라 전진시킨다.
            float rawLag = (Networking.GetServerTimeInMilliseconds() - _syncTimeMs) * 0.001f;
            float elapsed = Mathf.Clamp(rawLag, 0f, MAX_CATCH_UP_TIME);
            int steps = Mathf.CeilToInt(elapsed / CATCH_UP_STEP);
            if (steps > 0)
            {
                float h = elapsed / steps;
                for (int i = 0; i < steps; i++) { StepPrediction(h); }
            }

            // 화면은 그대로 두고 차이를 오프셋으로 들고 있다가 서서히 없앤다 — 순간이동이 보이지 않는다.
            float error = (shownPos - _predPos).magnitude;
            float snapDistance = HardSnapDistance + _predSpeed * elapsed * SNAP_LAG_FACTOR;
            bool snap = !alive || error > snapDistance;
            if (snap)
            {
                _smoothTime = SmoothTime;
                ClearOffset();
                _remoteBank = TargetBank();
                ApplyRemotePose();
                _lastWrittenPos = transform.position;
            }
            else
            {
                // 위치만 잇고 보정 속도는 0에서 다시 시작한다. 받기 직전의 화면 속도까지 이으면 틀린 움직임이 다음 보정으로
                // 넘어가 오차가 쌓인다(예전 스프링 시도가 실측에서 더 나빴던 이유).
                _offsetPos = shownPos - _predPos;
                _offsetVel = Vector3.zero;
                _offsetRotVec = RotationVector(_predFwd, shownFwd);
                _offsetRotVel = Vector3.zero;
                _offsetRot = RotationFromVector(_offsetRotVec);
                _smoothTime = SmoothTime * Mathf.Clamp(error / 100f, 1f, MAX_SMOOTH_SCALE);
            }

            if (DebugNetwork)
            {
                float gap = _lastRecvTime >= 0f ? Time.time - _lastRecvTime : -1f;
                LogNet("recv gap=" + gap.ToString("F2")
                    + " lag=" + rawLag.ToString("F3")
                    + " err=" + error.ToString("F1")
                    + " dirErr=" + Vector3.Angle(shownFwd, _predFwd).ToString("F1")
                    + " snap=" + snap
                    + " spd=" + _syncSpeed.ToString("F0")
                    + " w=" + (_syncAngVel.magnitude * Mathf.Rad2Deg).ToString("F0")
                    + " mode=" + _syncMode
                    + " alive=" + alive);
            }
            _lastRecvTime = Time.time;
        }

        /// AI가 매 프레임 부른다(원격). 예측을 전진시키고 화면 자세를 쓴다.
        public void RemoteTick(float dt)
        {
            if (!_hasPrediction || !Flight) { return; }

            // 지난 프레임에 내가 쓴 위치와 다르면 다른 코드가 transform을 옮긴 것이다.
            if (DebugNetwork && _hasWrittenPos)
            {
                float moved = Vector3.Distance(transform.position, _lastWrittenPos);
                if (moved > 1f) { LogNet("external move=" + moved.ToString("F1")); }
            }
            Vector3 before = transform.position;

            StepPrediction(dt);
            StepOffsetSpring(dt);

            // 롤은 동기화하지 않는다. 예측 선회율에서 정상선회 뱅크각을 역산하고, 실제 기체와 같은 롤 속도 한계로 따라간다.
            _remoteBank = Mathf.MoveTowards(_remoteBank, TargetBank(), Flight.MaxRollRate * dt);
            ApplyRemotePose();

            if (DebugNetwork)
            {
                // 정상 이동량(속력 × dt)을 뺀 나머지가 화면에서 보이는 '튐'이다.
                float jump = Vector3.Distance(before, transform.position) - _predSpeed * dt;
                if (_hasWrittenPos && jump > 3f)
                {
                    LogNet("frame jump=" + jump.ToString("F1") + " dt=" + dt.ToString("F3")
                        + " offset=" + _offsetPos.magnitude.ToString("F1"));
                }
                _lastWrittenPos = transform.position;
                _hasWrittenPos = true;
            }
            // 기총 리드/미사일 초기 속도가 이 값을 쓴다.
            Flight.Speed = _predSpeed;
        }

        /// 임계 감쇠 스프링의 정확해: x(t) = (x0 + (v0 + w·x0)t)e^(-wt). 프레임이 길어도 발산하지 않는다.
        private void StepOffsetSpring(float dt)
        {
            float w = 2f / Mathf.Max(_smoothTime, 0.05f);
            float e = Mathf.Exp(-w * dt);

            Vector3 c = _offsetVel + _offsetPos * w;
            _offsetPos = (_offsetPos + c * dt) * e;
            _offsetVel = (_offsetVel - c * (w * dt)) * e;

            Vector3 cr = _offsetRotVel + _offsetRotVec * w;
            _offsetRotVec = (_offsetRotVec + cr * dt) * e;
            _offsetRotVel = (_offsetRotVel - cr * (w * dt)) * e;
            _offsetRot = RotationFromVector(_offsetRotVec);
        }

        private void ClearOffset()
        {
            _offsetPos = Vector3.zero;
            _offsetVel = Vector3.zero;
            _offsetRotVec = Vector3.zero;
            _offsetRotVel = Vector3.zero;
            _offsetRot = Quaternion.identity;
        }

        /// from을 to로 돌리는 회전(축 × 라디안).
        private Vector3 RotationVector(Vector3 from, Vector3 to)
        {
            Vector3 axis = Vector3.Cross(from, to);
            float sin = axis.magnitude;
            if (sin < 0.000001f) { return Vector3.zero; }
            return axis / sin * (Vector3.Angle(from, to) * Mathf.Deg2Rad);
        }

        private Quaternion RotationFromVector(Vector3 rotVec)
        {
            float angle = rotVec.magnitude;
            if (angle < 0.000001f) { return Quaternion.identity; }
            return Quaternion.AngleAxis(angle * Mathf.Rad2Deg, rotVec / angle);
        }

        private void ApplyRemotePose()
        {
            Vector3 fwd = _offsetRot * _predFwd;
            transform.position = _predPos + _offsetPos;
            // 비행 컨트롤러와 같은 규칙: 로컬 Z축으로 -각도만큼 돌리면 오른쪽으로 눕는다(+뱅크).
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, -_remoteBank);
        }

        /// 정상선회식 turnRate = TurnG·g·tan(bank) / speed 를 뒤집어 예측 선회율에서 뱅크각을 구한다.
        private float TargetBank()
        {
            if (!Flight || _predSpeed <= 1f) { return 0f; }
            // 월드 위쪽 성분이 수평 선회율이다(+: 오른쪽 선회).
            float turnRate = Vector3.Dot(_predAngVel, Vector3.up);
            float bank = Mathf.Atan(turnRate * _predSpeed / (Mathf.Max(Flight.TurnG, 0.1f) * 9.81f)) * Mathf.Rad2Deg;
            return Mathf.Clamp(bank, -Flight.MaxBankAngle, Flight.MaxBankAngle);
        }

        // ───────────── 예측 모델 (오너의 ghost와 원격의 예측이 반드시 같은 식을 써야 한다) ─────────────

        private void StepPrediction(float dt)
        {
            _predSpeed = AccelerateBy(_predSpeed, _predAccel, dt);
            _predFwd = RotateDir(_predFwd, _predAngVel, dt);
            _predPos += _predFwd * (_predSpeed * dt);
        }

        private void StepGhost(float dt)
        {
            _ghostSpeed = AccelerateBy(_ghostSpeed, _ghostAccel, dt);
            _ghostFwd = RotateDir(_ghostFwd, _ghostAngVel, dt);
            _ghostPos += _ghostFwd * (_ghostSpeed * dt);
        }

        private Vector3 RotateDir(Vector3 dir, Vector3 angVel, float dt)
        {
            float rate = angVel.magnitude;
            if (rate < 0.000001f) { return dir; }
            return (Quaternion.AngleAxis(rate * dt * Mathf.Rad2Deg, angVel / rate) * dir).normalized;
        }

        private float AccelerateBy(float speed, float accel, float dt)
        {
            speed += accel * dt;
            if (Flight) { speed = Mathf.Clamp(speed, Flight.MinSpeed, Flight.MaxSpeed); }
            return speed;
        }

        /// 현재 transform에서 측정/예측을 새로 시작한다(시작, 부활).
        public void ResetMotion()
        {
            Init();
            ResetMotionCore();
        }

        private void ResetMotionCore()
        {
            _predPos = transform.position;
            _predFwd = transform.forward;
            _predSpeed = Flight != null ? Flight.Speed : 0f;
            _predAngVel = Vector3.zero;
            _predAccel = 0f;
            _hasPrediction = true;
            ClearOffset();
            _remoteBank = Flight != null ? Flight.GetBankAngle() : 0f;

            _angVel = Vector3.zero;
            _accel = 0f;
            _hasPrevMotion = false;
            _hasGhost = false;
            _hasWrittenPos = false;
        }

        // ───────────── 소유권 ─────────────

        /// AI가 매 프레임 부른다. 소유권을 주기적으로 다시 확인한다.
        public void OwnerCheckTick(float dt)
        {
            _ownerCheckTimer -= dt;
            if (_ownerCheckTimer > 0f) { return; }
            _ownerCheckTimer = OWNER_CHECK_INTERVAL;
            SyncOwnership();
        }

        /// 부활처럼 운동 상태를 어차피 새로 시작하는 곳에서 쓴다 — 넘겨받기 처리 없이 캐시만 맞춘다.
        public bool RefreshOwnerSilent()
        {
            Init();
            _isOwner = Networking.IsOwner(gameObject);
            return _isOwner;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (DebugNetwork) { LogNet("ownership event newOwner=" + (player != null ? player.displayName : "null")); }
            SyncOwnership();
        }

        /// 실제 소유권과 캐시(_isOwner)가 다르면 맞추고 AI/예측을 넘겨받는다. 소유권 이벤트, 상태 수신, 주기 확인에서 부른다.
        public void SyncOwnership()
        {
            Init();
            bool wasOwner = _isOwner;
            bool owner = Networking.IsOwner(gameObject);
            if (owner == wasOwner) { return; }
            _isOwner = owner;
            _lastOwnerChangeTime = Time.time;
            _handoffTargetIdx = -1;
            if (DebugNetwork) { LogNet("ownership changed wasOwner=" + wasOwner + " owner=" + owner); }

            if (_isOwner)
            {
                // 화면에 보이던 자세 그대로 AI를 이어받는다.
                if (Flight) { Flight.Speed = _hasPrediction ? _predSpeed : Flight.CruiseSpeed; }
                _angVel = _predAngVel;
                _accel = _predAccel;
                _hasPrevMotion = false;
                _hasGhost = false;
                _congestion = 1f;
                ClearOffset();

                if (AI) { AI.OnNetOwnershipChanged(true); }
                if (AI == null || AI.IsAlive()) { SendState(); }
            }
            else
            {
                _predPos = transform.position;
                _predFwd = transform.forward;
                _predSpeed = Flight != null ? Flight.Speed : 0f;
                _predAngVel = _angVel;
                _predAccel = _accel;
                _hasPrediction = true;
                ClearOffset();
                _remoteBank = Flight != null ? Flight.GetBankAngle() : 0f;

                if (AI) { AI.OnNetOwnershipChanged(false); }
            }
        }
    }
}
