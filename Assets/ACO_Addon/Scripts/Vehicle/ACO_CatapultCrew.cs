using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 캐터펄트 하나의 갑판 요원 6명(유도 1, 점검 3, 홀드백 1, 사출장교 1). 절차:
    ///  대기 → 유도(접근 구역의 기체에 수신호) → 점검(고정 뒤 점검요원이 숙인 채 기체를 돌고 홀드백 요원이 들고 있던 펜던트를 앞바퀴에 걸며,
    ///  모두 제자리로 물러나 무릎을 꿇고 엄지를 든다. 펜던트는 사출하는 순간 사라지고, 절차가 끝나면 다시 손에 들려 있다)
    ///  → 준비(유도요원이 앉고 사출장교가 스로틀 업 신호, 애프터버너를 기다린다) → 지적확인(요원을 한 명씩 가리키며 확인 — 조종사가 아니라 대피 확인이다)
    ///  → 발함 신호(무릎을 꿇고 전방을 확인한 뒤 팔을 뻗는다) → 사출. 신호 중 애프터버너를 끄면 준비로 돌아간다.
    /// 새 동기화 없음: 기체 위치·캐터펄트 고정·애프터버너·사출은 이미 모두에게 전해지므로 각자 그 값으로 요원을 움직인다.
    /// 사출 판단은 조종사 쪽(ACO_DFUNC_Catapult가 LaunchApproved를 읽는다)만 쓰고, 다른 화면은 타이머가 조금 어긋나도 연출뿐이다.
    /// 내가 요원을 끄면(HUD 설정 DECK CREW) 내 화면에서 사라지고, 내가 조종할 때는 기존 풀스로틀 유지 사출로 돌아간다.
    /// 애니메이터의 정수 파라미터 Signal = SIG_* — 컨트롤러의 클립만 바꿔 끼우면 동작을 교체할 수 있다.
    /// 프리팹 NPC_CatapultCrew: Bake가 캐터펄트마다 한 벌 놓고 이어 준다. 자리·경유점은 프리팹에서 옮기면 된다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_CatapultCrew : UdonSharpBehaviour
    {
        public const int SIG_IDLE = 0;
        public const int SIG_WALK = 1;
        public const int SIG_KNEEL = 2;
        public const int SIG_COME = 3;
        public const int SIG_TURN_LEFT = 4;   // 조종사 기준 왼쪽
        public const int SIG_TURN_RIGHT = 5;  // 조종사 기준 오른쪽
        public const int SIG_SLOW = 6;
        public const int SIG_STOP = 7;
        public const int SIG_THUMB = 8;
        public const int SIG_THROTTLE = 9;
        public const int SIG_POINT = 10;
        public const int SIG_LAUNCH = 11;
        public const int SIG_SIT = 12;
        public const int SIG_OPERATE = 13;   // 발함지휘소: 패널 조작
        public const int SIG_PRESS = 14;     // 발함지휘소: 사출 버튼

        // GetStage: 발함지휘소 요원이 읽는다
        public const int STAGE_IDLE = 0;
        public const int STAGE_WORK = 1;
        public const int STAGE_LAUNCH = 2;

        private const int PHASE_IDLE = 0;
        private const int PHASE_CHECK = 1;
        private const int PHASE_READY = 2;
        private const int PHASE_POINT = 3;
        private const int PHASE_SIGNAL = 4;
        private const int PHASE_LAUNCHED = 5;

        private const int CK_HOME = 0;
        private const int CK_WALK = 1;
        private const int CK_KNEEL = 2;
        private const int CK_THUMB = 3;
        private const int CK_RETURN = 4;
        private const int CK_HOLD = 5;   // 제자리에서 엄지를 든 채 사출을 기다린다

        private const int M_DIRECTOR = 0;
        private const int M_SHOOTER = 1;
        private const int M_CHECK = 2;     // 여기부터 점검요원 CHECKERS명(왼쪽, 오른쪽, 추가, 홀드백)
        private const int CHECKERS = 4;
        private const int PENDANT_CHECKER = 3;
        private const float TICK = 0.2f;
        private const string PARAM = "Signal";

        [Header("참조 (Bake가 채운다)")]
        [Tooltip("이 요원들이 맡은 캐터펄트의 Trigger — 기체가 물리는 오브젝트")]
        public Transform Trigger;
        [Tooltip("씬의 모든 기체 캐터펄트 기능")]
        public ACO_DFUNC_Catapult[] Catapults;

        [Header("요원 (프리팹 빌더가 채운다)")]
        public Animator Director;
        public Animator CheckerLeft;
        public Animator CheckerRight;
        [Tooltip("추가 점검요원 — 홀드백 요원의 반대편")]
        public Animator CheckerExtra;
        [Tooltip("홀드백 펜던트를 거는 요원")]
        public Animator PendantCrew;
        public Animator Shooter;
        [Tooltip("왼쪽 점검요원이 들르는 곳(앞쪽 방향 = 쪼그려 볼 방향)")]
        public Transform[] StopsLeft;
        [Tooltip("오른쪽 점검요원이 들르는 곳")]
        public Transform[] StopsRight;
        [Tooltip("추가 점검요원이 들르는 곳")]
        public Transform[] StopsExtra;
        [Tooltip("홀드백 요원이 들르는 곳 — 마지막 곳에서 펜던트를 건다")]
        public Transform[] StopsPendant;

        [Header("홀드백 펜던트 (프리팹 빌더가 채운다)")]
        public Transform Pendant;
        [Tooltip("들고 있을 때의 자리(요원 손 아래)")]
        public Transform PendantGrip;
        [Tooltip("앞바퀴에 걸었을 때의 자리")]
        public Transform PendantPoint;

        [Header("유도")]
        [Tooltip("캐터펄트 뒤 이 거리(m) 안의 기체를 유도한다")]
        public float ApproachRange = 90f;
        [Tooltip("캐터펄트 축에서 좌우 이 거리(m) 안의 기체를 유도한다")]
        public float ApproachWidth = 30f;
        [Tooltip("가야 할 방향에서 이 각도(도) 넘게 벗어나면 선회 신호")]
        public float AlignAngle = 5f;
        [Tooltip("축에서 벗어난 거리의 몇 배 앞 축 위 지점을 향하게 할지 — 클수록 완만하게 축에 들어온다")]
        public float LineupRatio = 1.5f;
        [Tooltip("이 속도(m/s)를 넘으면 감속 신호")]
        public float SlowSpeed = 7f;
        [Tooltip("감속 신호를 주는 거리(m)")]
        public float SlowRange = 50f;
        [Tooltip("고정 뒤 유도요원이 정지 신호를 유지하는 시간(초)")]
        public float LockStopTime = 2f;

        [Header("JBD (제트 블라스트 디플렉터 — Bake가 채운다)")]
        [Tooltip("이 캐터펄트의 JBD 판")]
        public Transform Jbd;
        [Tooltip("JBD 경첩(월드) — 사출 방향 쪽 끝. 뒤쪽 끝이 올라온다")]
        public Vector3 JbdHinge;
        [Tooltip("올렸을 때 각도(도)")]
        public float JbdAngle = 55f;
        [Tooltip("움직이는 빠르기(도/초)")]
        public float JbdSpeed = 25f;

        [Header("절차")]
        public float CheckWalkSpeed = 3.2f;
        [Tooltip("경유점마다 쪼그려 보는 시간(초)")]
        public float KneelTime = 1.2f;
        [Tooltip("제자리로 물러나 엄지를 든 뒤 점검 완료로 치기까지(초). 엄지는 사출이 끝날 때까지 든다")]
        public float ThumbTime = 1f;
        [Tooltip("지적확인: 요원 한 명을 가리키는 시간(초). 전체 시간 = 이 값 × 요원 수")]
        public float PointEachTime = 0.6f;
        [Tooltip("지적확인 뒤 발함 신호부터 사출까지(초) — 동기화 여유로 1초 이상")]
        public float SignalTime = 2f;
        [Tooltip("몸을 돌리는 빠르기(도/초)")]
        public float TurnRate = 240f;
        [Tooltip("고정된 기체를 찾는 반경(m). 다른 화면의 위치 지연까지 품는다")]
        public float LockRadius = 8f;
        [Tooltip("내가 이 거리(m) 밖이면 요원을 끈다")]
        public float CullRange = 600f;

        // 지적확인 순서: 앞의 유도요원 → 왼쪽 → 뒤 → 오른쪽으로 한 바퀴 돌아본다
        private int[] _pointOrder = new int[] { M_DIRECTOR, M_CHECK, M_CHECK + PENDANT_CHECKER, M_CHECK + 2, M_CHECK + 1 };
        private Animator[] _members;
        private Vector3[] _homePos;
        private Quaternion[] _homeRot;
        private int[] _sig;
        private int[] _ckState;
        private int[] _ckStop;
        private float[] _ckTimer;
        private bool[] _ckDone;

        private bool _started;
        private bool _shown = true;
        private bool _active = true;
        private float _cull;
        private float _tick;
        private int _phase;
        private float _phaseTime;
        private bool _launchOk;
        private ACO_DFUNC_Catapult _occ;
        private ACO_DFUNC_Catapult _guideCat;
        private Vector3 _guidePos;
        private float _guideTime;
        private Vector3 _directorLook;
        private Vector3 _shooterLook;
        private bool _directorLooking;
        private Vector3 _jbdPos;
        private Quaternion _jbdRot;
        private float _jbdNow;
        private float _jbdTick;
        private ACO_DFUNC_Catapult _jbdCat;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            _members = new Animator[] { Director, Shooter, CheckerLeft, CheckerRight, CheckerExtra, PendantCrew };
            int n = _members.Length;
            _homePos = new Vector3[n];
            _homeRot = new Quaternion[n];
            _sig = new int[n];
            for (int i = 0; i < n; i++)
            {
                _sig[i] = -1;
                if (_members[i] == null) { continue; }
                _homePos[i] = _members[i].transform.position;
                _homeRot[i] = _members[i].transform.rotation;
            }
            _ckState = new int[CHECKERS];
            _ckStop = new int[CHECKERS];
            _ckTimer = new float[CHECKERS];
            _ckDone = new bool[CHECKERS];
            _shooterLook = Shooter != null ? Shooter.transform.position + Shooter.transform.forward : Vector3.zero;
            HoldPendant();
            if (Jbd != null)
            {
                _jbdPos = Jbd.position;
                _jbdRot = Jbd.rotation;
            }
        }

        // ───────────── 조종사·설정 쪽 ─────────────

        /// HUD 설정: 내 화면의 요원 표시.
        public void SetShown(bool on)
        {
            _shown = on;
            _cull = 0f;
        }

        public bool IsShown() { return _shown; }

        /// 발함지휘소 요원이 읽는다.
        public int GetStage()
        {
            if (_occ == null) { return STAGE_IDLE; }
            return _phase == PHASE_SIGNAL || _phase == PHASE_LAUNCHED ? STAGE_LAUNCH : STAGE_WORK;
        }

        /// 절차가 끝나 이 기체를 사출해도 되는가.
        public bool LaunchApproved(ACO_DFUNC_Catapult cat) { return _launchOk && _occ == cat; }

        // ───────────── 갱신 ─────────────

        void Update()
        {
            float dt = Time.deltaTime;
            UpdateJbd(dt);
            _cull -= dt;
            if (_cull <= 0f)
            {
                _cull = 1f;
                UpdateActive();
            }
            if (!_active) { return; }
            MoveCheckers(dt);
            if (_directorLooking && Director != null) { Face(Director.transform, _directorLook, dt); }
            if (Shooter != null) { Face(Shooter.transform, _shooterLook, dt); }
            _tick -= dt;
            if (_tick > 0f) { return; }
            _tick = TICK;
            Think();
        }

        // JBD는 시설이라 요원 표시를 꺼도, 멀어도 움직인다(콜라이더도 함께 — 모두의 화면에서 같아야 한다)
        private void UpdateJbd(float dt)
        {
            if (Jbd == null) { return; }
            _jbdTick -= dt;
            if (_jbdTick <= 0f)
            {
                _jbdTick = 0.25f;
                if (_jbdCat != null && !_jbdCat.OnCatapult) { _jbdCat = null; }
                if (_jbdCat == null) { _jbdCat = FindLocked(); }
            }
            float target = _jbdCat != null ? JbdAngle : 0f;
            if (_jbdNow == target) { return; }
            _jbdNow = Mathf.MoveTowards(_jbdNow, target, JbdSpeed * dt);
            Quaternion q = Quaternion.AngleAxis(_jbdNow, transform.right);
            Jbd.SetPositionAndRotation(JbdHinge + q * (_jbdPos - JbdHinge), q * _jbdRot);
        }

        private ACO_DFUNC_Catapult FindLocked()
        {
            if (Catapults == null) { return null; }
            float r2 = LockRadius * LockRadius;
            for (int i = 0; i < Catapults.Length; i++)
            {
                ACO_DFUNC_Catapult cat = Catapults[i];
                if (cat == null || !cat.OnCatapult || cat.LaunchStarted) { continue; }
                if ((cat.transform.position - transform.position).sqrMagnitude <= r2) { return cat; }
            }
            return null;
        }

        private void UpdateActive()
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            bool near = Utilities.IsValid(me) && (me.GetPosition() - transform.position).sqrMagnitude < CullRange * CullRange;
            bool want = _shown && near;
            if (want == _active) { return; }
            _active = want;
            ResetAll();
            for (int i = 0; i < _members.Length; i++)
            {
                if (_members[i] != null) { _members[i].gameObject.SetActive(want); }
            }
            if (want) { for (int i = 0; i < _members.Length; i++) { Signal(i, SIG_IDLE); } }
        }

        // 숨겼다 다시 보일 때 애니메이터 값이 처음으로 돌아가므로 기억한 신호도 비운다
        private void ResetAll()
        {
            _occ = null;
            _guideCat = null;
            _phase = PHASE_IDLE;
            _launchOk = false;
            _directorLooking = false;
            for (int i = 0; i < _members.Length; i++)
            {
                _sig[i] = -1;
                if (_members[i] == null) { continue; }
                _members[i].transform.SetPositionAndRotation(_homePos[i], _homeRot[i]);
            }
            for (int i = 0; i < CHECKERS; i++)
            {
                _ckState[i] = CK_HOME;
                _ckDone[i] = false;
            }
            if (Shooter != null) { _shooterLook = Shooter.transform.position + Shooter.transform.forward; }
            HoldPendant();
        }

        private void Think()
        {
            if (_occ != null && !_occ.OnCatapult) { EndProcedure(); }
            if (_occ == null) { FindOccupant(); }
            UpdateDirector();
            if (_occ != null) { UpdateProcedure(); }
        }

        private void FindOccupant()
        {
            if (Catapults == null) { return; }
            float r2 = LockRadius * LockRadius;
            for (int i = 0; i < Catapults.Length; i++)
            {
                ACO_DFUNC_Catapult cat = Catapults[i];
                if (cat == null || !cat.OnCatapult || cat.LaunchStarted) { continue; }
                if ((cat.transform.position - transform.position).sqrMagnitude > r2) { continue; }
                BeginProcedure(cat);
                return;
            }
        }

        // ───────────── 절차 ─────────────

        private void BeginProcedure(ACO_DFUNC_Catapult cat)
        {
            _occ = cat;
            _launchOk = false;
            EnterPhase(PHASE_CHECK);
            for (int i = 0; i < CHECKERS; i++)
            {
                _ckDone[i] = false;
                _ckStop[i] = 0;
                if (StopCount(i) > 0 && _members[M_CHECK + i] != null)
                {
                    _ckState[i] = CK_WALK;
                    Signal(M_CHECK + i, SIG_WALK);
                }
                else
                {
                    _ckState[i] = CK_HOME;
                    _ckDone[i] = true;
                }
            }
        }

        private void EndProcedure()
        {
            _occ = null;
            _launchOk = false;
            EnterPhase(PHASE_IDLE);
            Signal(M_SHOOTER, SIG_IDLE);
            if (Shooter != null) { _shooterLook = _homePos[M_SHOOTER] + _homeRot[M_SHOOTER] * Vector3.forward; }
            HoldPendant();
            for (int i = 0; i < CHECKERS; i++)
            {
                if (_ckState[i] == CK_THUMB || _ckState[i] == CK_HOLD) { _ckState[i] = CK_HOLD; }
                else if (_ckState[i] != CK_HOME) { BeginReturn(i); }
            }
        }

        private void EnterPhase(int phase)
        {
            _phase = phase;
            _phaseTime = Time.time;
        }

        private void UpdateProcedure()
        {
            if (_occ.LaunchStarted)
            {
                _launchOk = false;
                EnterPhase(PHASE_LAUNCHED);
                if (Pendant != null) { Pendant.gameObject.SetActive(false); }
                return;
            }
            if (_phase == PHASE_LAUNCHED) { return; }
            float t = Time.time - _phaseTime;
            bool burner = AfterburnerOn();
            if (_phase == PHASE_CHECK)
            {
                if (AllChecked()) { EnterPhase(PHASE_READY); }
            }
            else if (_phase == PHASE_READY)
            {
                if (burner) { EnterPhase(PHASE_POINT); }
            }
            else if (!burner)
            {
                _launchOk = false;
                EnterPhase(PHASE_READY);
            }
            else if (_phase == PHASE_POINT)
            {
                if (t >= PointEachTime * _pointOrder.Length) { EnterPhase(PHASE_SIGNAL); }
            }
            else if (_phase == PHASE_SIGNAL)
            {
                if (t >= SignalTime) { _launchOk = true; }
            }
            ShowShooter();
        }

        private bool AllChecked()
        {
            for (int i = 0; i < CHECKERS; i++) { if (!_ckDone[i]) { return false; } }
            return true;
        }

        private void HoldPendant() { PlacePendant(PendantGrip); }

        private void PlacePendant(Transform parent)
        {
            if (Pendant == null || parent == null) { return; }
            Pendant.SetParent(parent, false);
            Pendant.localPosition = Vector3.zero;
            Pendant.localRotation = Quaternion.identity;
            Pendant.gameObject.SetActive(true);
        }

        private void ShowShooter()
        {
            if (_phase == PHASE_READY) { Signal(M_SHOOTER, SIG_THROTTLE); }
            else if (_phase == PHASE_POINT) { Signal(M_SHOOTER, SIG_POINT); }
            else if (_phase == PHASE_SIGNAL) { Signal(M_SHOOTER, SIG_LAUNCH); }
            if (Shooter == null) { return; }
            if (_phase == PHASE_SIGNAL)
            {
                // 발함 신호는 사출 방향으로 한다
                _shooterLook = Shooter.transform.position + transform.forward * 10f;
            }
            else if (_phase == PHASE_POINT)
            {
                // 지금 확인하는 요원을 본다(몸을 돌리면 뻗은 팔도 따라간다)
                int k = Mathf.FloorToInt((Time.time - _phaseTime) / Mathf.Max(PointEachTime, 0.01f));
                Animator who = _members[_pointOrder[Mathf.Clamp(k, 0, _pointOrder.Length - 1)]];
                if (who != null) { _shooterLook = who.transform.position; }
            }
            else
            {
                // 지적확인·스로틀 업은 조종석을 본다 — 물리는 지점은 앞바퀴라 조금 뒤
                _shooterLook = _occ.transform.position - _occ.transform.forward * 2f;
            }
        }

        // 애프터버너가 없는 기체는 출력으로 본다
        private bool AfterburnerOn()
        {
            UdonSharpBehaviour sav = _occ.SAVControl;
            if (sav == null) { return false; }
            return (bool)sav.GetProgramVariable("AfterburnerOn") || (float)sav.GetProgramVariable("EngineOutput") >= 0.95f;
        }

        // ───────────── 유도 ─────────────

        private void UpdateDirector()
        {
            ACO_DFUNC_Catapult best = FindApproaching();
            // 점검이 끝나면 사출까지 앉아 있는다
            if (_occ != null && _phase != PHASE_CHECK)
            {
                Signal(M_DIRECTOR, SIG_SIT);
                _directorLook = _occ.transform.position;
                _directorLooking = true;
                _guideCat = null;
                return;
            }
            if (best == null)
            {
                bool hold = _occ != null && Time.time - _phaseTime < LockStopTime && _phase == PHASE_CHECK;
                Signal(M_DIRECTOR, hold ? SIG_STOP : SIG_IDLE);
                _guideCat = null;
                if (_occ != null)
                {
                    _directorLook = _occ.transform.position;
                    _directorLooking = true;
                }
                else if (Director != null)
                {
                    _directorLook = _homePos[M_DIRECTOR] + _homeRot[M_DIRECTOR] * Vector3.forward;
                    _directorLooking = true;
                }
                return;
            }
            _directorLook = best.transform.position;
            _directorLooking = true;
            float speed = GuideSpeed(best);
            // 앞 기체가 캐터펄트를 쓰는 동안 다음 기체는 세운다
            Signal(M_DIRECTOR, _occ != null ? SIG_STOP : GuideSignal(best, speed));
        }

        private ACO_DFUNC_Catapult FindApproaching()
        {
            if (Catapults == null) { return null; }
            ACO_DFUNC_Catapult best = null;
            float bestDist = ApproachRange;
            for (int i = 0; i < Catapults.Length; i++)
            {
                ACO_DFUNC_Catapult cat = Catapults[i];
                if (cat == null || cat == _occ || cat.OnCatapult) { continue; }
                SaccEntity e = cat.EntityControl;
                if (e == null || !e.Occupied || e._dead || cat.SAVControl == null) { continue; }
                Vector3 p = transform.InverseTransformPoint(cat.transform.position);
                float dist = -p.z;
                if (dist < -12f || dist > bestDist || Mathf.Abs(p.x) > ApproachWidth) { continue; }
                if (!(bool)cat.SAVControl.GetProgramVariable("Taxiing")) { continue; }
                best = cat;
                bestDist = dist;
            }
            return best;
        }

        // 다른 화면에서도 같게 나오도록 위치 변화로 잰다
        private float GuideSpeed(ACO_DFUNC_Catapult cat)
        {
            Vector3 pos = cat.transform.position;
            float now = Time.time;
            float speed = 0f;
            if (cat == _guideCat && now > _guideTime) { speed = (pos - _guidePos).magnitude / (now - _guideTime); }
            _guideCat = cat;
            _guidePos = pos;
            _guideTime = now;
            return speed;
        }

        /// 트리거를 바로 겨누게 하면 옆에서 비스듬히 들어와 걸리지 않는다(방향이 15도 안이어야 걸린다).
        /// 그래서 캐터펄트 축 위, 앞쪽 지점을 향하게 해 먼저 축에 올려놓고, 축을 따라 트리거로 들어오게 한다.
        private int GuideSignal(ACO_DFUNC_Catapult cat, float speed)
        {
            Vector3 p = transform.InverseTransformPoint(cat.transform.position);
            Vector3 f = transform.InverseTransformDirection(cat.EntityControl.transform.forward);
            float dist = Mathf.Max(-p.z, 0f);
            if (speed > SlowSpeed && dist < SlowRange) { return SIG_SLOW; }
            float side = Mathf.Abs(p.x);
            float lead = Mathf.Clamp(side * LineupRatio, 6f, 40f);
            // 축에 닿을 지점: 트리거보다 충분히 뒤 — 가까이서 옆으로 벗어났으면 뒤로 돌아가게 된다
            float aimZ = Mathf.Min(p.z + lead, -side * 1.2f);
            float want = Mathf.Atan2(-p.x, aimZ - p.z) * Mathf.Rad2Deg;
            float have = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float error = Mathf.DeltaAngle(have, want);
            int current = _sig[M_DIRECTOR];
            bool turning = current == SIG_TURN_LEFT || current == SIG_TURN_RIGHT;
            float tolerance = turning ? AlignAngle * 0.5f : AlignAngle;
            if (error > tolerance) { return SIG_TURN_RIGHT; }
            if (error < -tolerance) { return SIG_TURN_LEFT; }
            return SIG_COME;
        }

        // ───────────── 점검요원 ─────────────

        private Transform[] StopsOf(int i)
        {
            if (i == 0) { return StopsLeft; }
            if (i == 1) { return StopsRight; }
            return i == 2 ? StopsExtra : StopsPendant;
        }

        private int StopCount(int i)
        {
            Transform[] stops = StopsOf(i);
            return stops != null ? stops.Length : 0;
        }

        private Transform StopAt(int i, int stop) { return StopsOf(i)[stop]; }

        private void BeginReturn(int i)
        {
            _ckState[i] = CK_RETURN;
            Signal(M_CHECK + i, SIG_WALK);
        }

        private void MoveCheckers(float dt)
        {
            for (int i = 0; i < CHECKERS; i++)
            {
                int state = _ckState[i];
                if (state == CK_HOME) { continue; }
                Animator a = _members[M_CHECK + i];
                if (a == null) { continue; }
                Transform body = a.transform;
                if (state == CK_WALK)
                {
                    Transform stop = StopAt(i, _ckStop[i]);
                    if (stop == null || Walk(body, stop.position, dt))
                    {
                        _ckState[i] = CK_KNEEL;
                        _ckTimer[i] = KneelTime;
                        Signal(M_CHECK + i, SIG_KNEEL);
                    }
                }
                else if (state == CK_KNEEL)
                {
                    Transform stop = StopAt(i, _ckStop[i]);
                    if (stop != null) { Face(body, body.position + stop.forward, dt); }
                    _ckTimer[i] -= dt;
                    if (_ckTimer[i] > 0f) { continue; }
                    _ckStop[i]++;
                    if (i == PENDANT_CHECKER && _ckStop[i] >= StopCount(i) && _occ != null) { PlacePendant(PendantPoint); }
                    if (_ckStop[i] < StopCount(i))
                    {
                        _ckState[i] = CK_WALK;
                        Signal(M_CHECK + i, SIG_WALK);
                    }
                    else { BeginReturn(i); }
                }
                else if (state == CK_THUMB)
                {
                    FaceHome(i, dt);
                    _ckTimer[i] -= dt;
                    if (_ckTimer[i] > 0f) { continue; }
                    _ckDone[i] = true;
                    _ckState[i] = CK_HOLD;
                }
                else if (state == CK_HOLD)
                {
                    FaceHome(i, dt);
                    if (_occ != null) { continue; }
                    Signal(M_CHECK + i, SIG_IDLE);
                    if (Quaternion.Angle(body.rotation, _homeRot[M_CHECK + i]) < 2f) { _ckState[i] = CK_HOME; }
                }
                else if (Walk(body, _homePos[M_CHECK + i], dt))
                {
                    if (_occ != null)
                    {
                        _ckState[i] = CK_THUMB;
                        _ckTimer[i] = ThumbTime;
                        Signal(M_CHECK + i, SIG_THUMB);
                    }
                    else { _ckState[i] = CK_HOLD; }
                }
            }
        }

        private void FaceHome(int i, float dt)
        {
            Transform body = _members[M_CHECK + i].transform;
            Face(body, body.position + _homeRot[M_CHECK + i] * Vector3.forward, dt);
        }

        /// 다 왔으면 true.
        private bool Walk(Transform body, Vector3 target, float dt)
        {
            Vector3 next = Vector3.MoveTowards(body.position, target, CheckWalkSpeed * dt);
            Face(body, target, dt);
            body.position = next;
            return (next - target).sqrMagnitude < 0.01f;
        }

        // 고개를 젖히지 않게 수평으로만 돈다
        private void Face(Transform body, Vector3 target, float dt)
        {
            Vector3 d = target - body.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) { return; }
            body.rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(d), TurnRate * dt);
        }

        private void Signal(int member, int signal)
        {
            if (_sig[member] == signal) { return; }
            _sig[member] = signal;
            Animator a = _members[member];
            if (a != null && a.gameObject.activeInHierarchy) { a.SetInteger(PARAM, signal); }
        }
    }
}
