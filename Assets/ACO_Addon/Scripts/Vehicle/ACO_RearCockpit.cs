using SaccFlightAndVehicles;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// F형 뒷좌석 화면(6-346, 로컬 전용 — 동기화 없음). 이 화면의 내가 이 기체의 뒷자리(승객석)에 앉아 있을 때만 켠다(조종사 · 다른 사람에겐 꺼져 있다).
    ///  ① 가운데 큰 화면: 작전 지도(ACO_CockpitMap, 좌석 필터 = 뒷자리) + 아래 범례(번역 라벨).
    ///  ② 왼쪽 MPCD: RWR — 이 기체를 노린 미사일(빨강 쉐브론, 가까울수록 가운데 · 가까우면 깜빡임)과 락온(노랑 막대, 바깥 고리). 위 = 기수.
    ///  ③ 오른쪽 MPCD: 미션 상황 — 페이즈, 목표, 남은 시간, 격추.
    ///  ④ 위 작은 화면: 비행 정보 — 속도(kt) · 고도(ft) · 상승률(fpm) · 방위. 기체 위치 변화로 계산(모든 화면에 동기화돼 오는 값).
    ///  ⑤ 왼쪽 아래 작은 화면: 조종사 · 기체 — 번기 · 형식, 콜사인 · 닉네임, 특수무장 · 지원.
    /// 이 오브젝트는 F형 부품 묶음에 들어 있어 E형이면 통째로 꺼진다(ACO_AirframeVariant).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_RearCockpit : UdonSharpBehaviour
    {
        private const float KT = 1.9438445f;
        private const float FT = 3.28084f;

        [Header("참조 (설치 도구 · Bake가 넣는다)")]
        public SaccEntity Entity;
        public SaccAirVehicle AirVehicle;
        public ACO_AirframeVariant Airframe;
        public ACO_GameFlow Flow;
        public ACO_RWR Rwr;
        public ACO_TacName TacNames;
        [Tooltip("번역 장치(Bake가 넣는다)")]
        public ACO_Localization Loc;

        [Header("화면 (설치 도구가 만든다)")]
        [Tooltip("지도 밖의 화면 묶음(범례 · RWR · 미션 · 비행 · 조종사). 뒷자리에 앉은 내 화면에서만 켠다")]
        public GameObject Screens;
        public TextMeshPro FlightText;
        public TextMeshPro MissionText;
        public TextMeshPro PilotText;
        public TextMeshPro RwrText;
        [Tooltip("RWR 미사일 표시(꼭짓점 +Y 삼각형)")]
        public Transform[] MissileMarks;
        [Tooltip("RWR 락온 표시(가로 막대)")]
        public Transform[] LockMarks;

        [Header("RWR")]
        [Tooltip("바깥 고리 반지름(m) — 먼 미사일 · 락온")]
        public float RwrOuter = 0.04f;
        [Tooltip("안쪽 반지름(m) — 아주 가까운 미사일")]
        public float RwrInner = 0.01f;
        public float MaxRange = 10000f;
        public float CloseRange = 300f;
        [Tooltip("이 거리(m) 안의 미사일은 깜빡인다")]
        public float BlinkRange = 1500f;

        [Tooltip("갱신 간격(초) — 글자는 0.5초마다")]
        public float UpdateInterval = 0.15f;

        private bool _on;
        private bool _started;
        private float _timer;
        private float _textTimer;
        private bool _blink;
        private Vector3 _lastPos;
        private float _lastTime;
        private bool _hasLast;
        private float _speed;
        private float _climb;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (Screens) { Screens.SetActive(false); }
            HideMarks();
        }

        void OnDisable()
        {
            _on = false;
            _hasLast = false;
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = UpdateInterval;
            Start();

            bool on = Airframe != null && Airframe.IsLocalPassenger();
            if (on != _on)
            {
                _on = on;
                _hasLast = false;
                _textTimer = 0f;
                if (Screens && Screens.activeSelf != on) { Screens.SetActive(on); }
                if (!on) { HideMarks(); }
            }
            if (!on) { return; }

            UpdateFlight();
            UpdateRwr();
            _textTimer -= UpdateInterval;
            if (_textTimer <= 0f)
            {
                _textTimer = 0.5f;
                UpdateMission();
                UpdatePilot();
            }
        }

        // ───────────── ④ 비행 정보 ─────────────

        private void UpdateFlight()
        {
            if (!FlightText || !Entity) { return; }
            Transform t = Entity.transform;
            Vector3 p = t.position;
            float now = Time.time;
            if (_hasLast && now - _lastTime > 0.01f)
            {
                Vector3 v = (p - _lastPos) / (now - _lastTime);
                _speed = Mathf.Lerp(_speed, v.magnitude, 0.5f);
                _climb = Mathf.Lerp(_climb, v.y, 0.5f);
            }
            else if (!_hasLast) { _speed = 0f; _climb = 0f; }
            _lastPos = p;
            _lastTime = now;
            _hasLast = true;

            Vector3 fwd = t.forward;
            float hdg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            if (hdg < 0f) { hdg += 360f; }
            float sea = AirVehicle ? AirVehicle.SeaLevel : 0f;
            int kt = Mathf.RoundToInt(_speed * KT);
            int ft = Mathf.RoundToInt((p.y - sea) * FT);
            int fpm = Mathf.RoundToInt(_climb * FT * 60f / 10f) * 10;
            string s = "SPD  " + kt + " kt\nALT  " + ft.ToString("N0") + " ft\nV/S  " + (fpm >= 0 ? "+" : "") + fpm.ToString("N0") + " fpm\nHDG  " + Mathf.RoundToInt(hdg) % 360 + "°";
            if (FlightText.text != s) { FlightText.text = s; }
        }

        // ───────────── ② RWR ─────────────

        private void UpdateRwr()
        {
            _blink = !_blink;
            int missiles = 0;
            int locks = 0;
            float nearest = -1f;
            if (Rwr && Entity)
            {
                Transform t = Entity.transform;
                Quaternion inv = Quaternion.Inverse(t.rotation);
                int n = Rwr.GetMissileCount();
                int slots = MissileMarks != null ? MissileMarks.Length : 0;
                for (int i = 0; i < n && missiles < slots; i++)
                {
                    Transform m = Rwr.GetMissile(i);
                    if (!m || Rwr.GetMissileVictim(i) != Entity || !m.gameObject.activeInHierarchy) { continue; }
                    Vector3 local = inv * (m.position - t.position);
                    float dist = local.magnitude;
                    if (nearest < 0f || dist < nearest) { nearest = dist; }
                    Transform mark = MissileMarks[missiles];
                    missiles++;
                    if (!mark) { continue; }
                    bool show = dist >= BlinkRange || _blink;
                    if (mark.gameObject.activeSelf != show) { mark.gameObject.SetActive(show); }
                    float b = Mathf.Atan2(local.x, local.z);
                    float r = Mathf.Lerp(RwrInner, RwrOuter, RangeT(dist));
                    mark.localPosition = new Vector3(Mathf.Sin(b) * r, Mathf.Cos(b) * r, mark.localPosition.z);
                    mark.localRotation = Quaternion.Euler(0f, 0f, 180f - b * Mathf.Rad2Deg);   // 꼭짓점이 가운데(나)를 향한다
                }
                int lockSlots = LockMarks != null ? LockMarks.Length : 0;
                int ln = Rwr.GetLockSlots();
                for (int i = 0; i < ln && locks < lockSlots; i++)
                {
                    Transform src = Rwr.GetActiveLock(i);
                    if (!src || Rwr.GetLockVictim(i) != Entity) { continue; }
                    Vector3 local = inv * (src.position - t.position);
                    Transform mark = LockMarks[locks];
                    locks++;
                    if (!mark) { continue; }
                    if (!mark.gameObject.activeSelf) { mark.gameObject.SetActive(true); }
                    float b = Mathf.Atan2(local.x, local.z);
                    float r = RwrOuter + 0.004f;
                    mark.localPosition = new Vector3(Mathf.Sin(b) * r, Mathf.Cos(b) * r, mark.localPosition.z);
                    mark.localRotation = Quaternion.Euler(0f, 0f, -b * Mathf.Rad2Deg);   // 고리에 접하는 막대
                }
            }
            for (int i = missiles; MissileMarks != null && i < MissileMarks.Length; i++) { if (MissileMarks[i] && MissileMarks[i].gameObject.activeSelf) { MissileMarks[i].gameObject.SetActive(false); } }
            for (int i = locks; LockMarks != null && i < LockMarks.Length; i++) { if (LockMarks[i] && LockMarks[i].gameObject.activeSelf) { LockMarks[i].gameObject.SetActive(false); } }

            if (!RwrText) { return; }
            string s;
            if (missiles > 0) { s = "<color=#FF4433>" + T1("rear.msl", "MISSILE x{0}", missiles.ToString()) + "</color>  <size=80%>" + Mathf.RoundToInt(nearest).ToString("N0") + "m</size>"; }
            else if (locks > 0) { s = "<color=#FFD133>" + T("rear.lock", "LOCKED") + "</color>"; }
            else { s = "<color=#59FF73>" + T("rear.clear", "CLEAR") + "</color>"; }
            s = "RWR  " + s;
            if (RwrText.text != s) { RwrText.text = s; }
        }

        /// 거리 → 0(아주 가까움) ~ 1(멀다), 로그 눈금(ACO_RWR과 같은 느낌).
        private float RangeT(float dist)
        {
            float close = Mathf.Max(CloseRange, 1f);
            float far = Mathf.Max(MaxRange, close * 2f);
            return Mathf.Clamp01(Mathf.Log(Mathf.Max(dist, close) / close) / Mathf.Log(far / close));
        }

        private void HideMarks()
        {
            if (MissileMarks != null) { for (int i = 0; i < MissileMarks.Length; i++) { if (MissileMarks[i]) { MissileMarks[i].gameObject.SetActive(false); } } }
            if (LockMarks != null) { for (int i = 0; i < LockMarks.Length; i++) { if (LockMarks[i]) { LockMarks[i].gameObject.SetActive(false); } } }
        }

        // ───────────── ③ 미션 상황 ─────────────

        private void UpdateMission()
        {
            if (!MissionText) { return; }
            ACO_MissionManager m = Flow ? Flow.Manager : null;
            string s;
            if (m == null || m.GetState() != ACO_MissionManager.STATE_RUNNING)
            {
                bool free = Flow && Flow.Progress && Flow.Progress.FreeFlight;
                s = free ? T("rear.freeflight", "FREE FLIGHT") : T("rear.nomission", "NO MISSION");
            }
            else
            {
                s = T2("rear.phase", "PHASE {0}/{1}", (m.GetPhaseIndex() + 1).ToString(), m.GetPhaseCount().ToString());
                int all = m.GetTaggedTotal();
                if (all > 0) { s += "\n" + T2("rear.targets", "TARGETS {0}/{1}", m.GetTaggedDone().ToString(), all.ToString()); }
                float rem = m.GetRemainingSeconds();
                string time = rem < 0f ? "--:--" : Mathf.FloorToInt(rem / 60f).ToString("00") + ":" + Mathf.FloorToInt(rem % 60f).ToString("00");
                s += "\n" + T1("rear.time", "TIME {0}", time);
                s += "\n" + T1("rear.kills", "KILLS {0}", (m.GetFighterKills() + m.GetGroundKills()).ToString());
            }
            if (MissionText.text != s) { MissionText.text = s; }
        }

        // ───────────── ⑤ 조종사 · 기체 ─────────────

        private void UpdatePilot()
        {
            if (!PilotText || !Entity) { return; }
            int slot = Flow ? Flow.GetSlotOfAircraft(Entity) : -1;
            int pid = -1;
            if (Entity.SeatedPlayers != null && Entity.PilotSeat >= 0 && Entity.PilotSeat < Entity.SeatedPlayers.Length) { pid = Entity.SeatedPlayers[Entity.PilotSeat]; }
            VRCPlayerApi p = pid >= 0 ? VRCPlayerApi.GetPlayerById(pid) : null;
            string head = (slot >= 0 ? T1("flow.slot.num", "NO. {0}", (slot + 1).ToString()) + "  " : "") + "<color=#9FD8FF>" + T("flow.airframe.f", "F/A-18F") + "</color>";
            string s = head;
            if (Utilities.IsValid(p))
            {
                string call = TacNames ? TacNames.GetLabel(p) : "";
                if (call.Length > 0) { s += "\n<color=#9FD8FF>" + call + "</color>"; }
                s += "\n" + p.displayName;
                if (Flow && slot >= 0) { s += Flow.GetSlotLoadoutText(slot, pid); }
            }
            if (PilotText.text != s) { PilotText.text = s; }
        }

        // ───────────── 번역 ─────────────

        /// 번역 장치가 언어를 바꾸면 부른다 — 다음 갱신에 글자를 다시 쓴다.
        public void LocalizationChanged() { _textTimer = 0f; }

        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return s == key ? english : s;
        }
        private string T1(string key, string english, string a) { return T(key, english).Replace("{0}", a); }
        private string T2(string key, string english, string a, string b) { return T(key, english).Replace("{0}", a).Replace("{1}", b); }
    }
}
