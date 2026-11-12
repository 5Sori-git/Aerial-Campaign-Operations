using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// SUPPORT 다이얼(6-142) — 플레이어 기체 오른쪽 다이얼 4번째 칸(Dial_Functions_R[3], 데스크톱 키 4). 격납고에서 고른 지원 호출(ACO_PlayerProgress.GetCallPick)을 부른다.
    ///  - 벙커버스터(CALL_STRIKE): 칸을 고르면 타게팅 포드(LaserOrigin — AN/ASQ-228 렌즈 자리)에서 바라보는 방향(VR = 머리, 데스크톱 = 시선)으로 레이저가 나가고
    ///    닿은 곳(최대 MaxRange)에 폭발 범위 원이 보인다(내 화면만). 트리거를 당기면 그 지점으로 확정 — 호출권 1장, 쿨타임, 지원 관리자(ACO_SupportManager)가 모두에게 알린다.
    ///  - 급유 드론(CALL_TANKER): 트리거를 당기면 드론을 부른다(호출권은 보급이 끝날 때 쓴다). 연결·이탈 판정은 관리자가 한다.
    /// 조종사 화면에서만 동작한다(다른 화면은 관리자 이벤트로 연출만). HUD(ACO_FlightHud)가 GetHudName/GetHudCount/IsSelected로 잔탄 줄에 그린다.
    /// MFD: 오른쪽 다이얼 표시(StickDisplayR)의 왼쪽 칸 글자(MfdText) — 첫 줄 "BB x3"(고른 지원·장수), 쿨타임이나 안내가 있으면 둘째 줄("85s" / "NO SPOT").
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_DFUNC_Support : UdonSharpBehaviour
    {
        [Header("연결 (Bake / Setup Check)")]
        public ACO_PlayerProgress Progress;
        public ACO_SupportManager Manager;
        [Tooltip("이 다이얼이 관리자 Supports 배열의 몇 번째인가(Bake가 넣는다 — 네트워크 이벤트가 이 번호로 기체를 가리킨다)")]
        public int SupportIndex = -1;
        public SaccAirVehicle AirVehicle;

        [Header("자리 (Setup Check가 기체 프리팹에 만든다 — 직접 옮겨도 된다)")]
        [Tooltip("레이저가 나가는 점(타게팅 포드 렌즈). 비면 기체 중심 아래")]
        public Transform LaserOrigin;
        [Tooltip("급유 프로브 끝(드로그가 꽂히는 점 — F-18 Prove.1 아래)")]
        public Transform ProbeTip;
        [Tooltip("기체 애니메이터의 프로브 bool 이름(급유할 때 켠다)")]
        public string ProbeBool = "PROBE";

        [Header("조준 (내 화면만)")]
        [Tooltip("조준 레이저(월드 좌표 선 2점)")]
        public LineRenderer AimLaser;
        [Tooltip("폭발 범위 원(월드 좌표 닫힌 선)")]
        public LineRenderer AimRing;
        public int RingSegments = 48;
        [Tooltip("레이저가 닿는 거리(m)")]
        public float MaxRange = 10000f;
        [Tooltip("닿는 면(지면·건물·유닛 히트박스). 물은 WaterLayers로 따로(트리거라서)")]
        public LayerMask AimLayers = 1 | (1 << 11) | (1 << 17);
        public LayerMask WaterLayers = 1 << 4;
        public KeyCode FireKey = KeyCode.Space;
        [Tooltip("안내 문구(NO SPOT 등)를 HUD에 보이는 시간(초)")]
        public float NoticeTime = 2f;
        [Tooltip("HUD 상태 줄 경고(\"TANKER\" + 줄바꿈 + \"BANK UNDER 45\" 등 — 칸이 좁아 두 줄)를 보이는 시간(초)")]
        public float AlertTime = 3f;

        [Header("MFD (Setup Check가 오른쪽 다이얼 표시의 왼쪽 칸에 만든다)")]
        [Tooltip("다이얼 칸 글자 — 고른 지원·장수, 쿨타임은 둘째 줄")]
        public TextMeshProUGUI MfdText;
        public float MfdInterval = 0.25f;

        // SaccEntity가 주입한다(DFUNC 규약).
        [System.NonSerialized] public SaccEntity EntityControl;
        [System.NonSerialized] public bool LeftDial = false;
        [System.NonSerialized] public int DialPosition = -999;

        private bool _pilot;
        private bool _selected;
        private int _pick = ACO_PlayerProgress.CALL_NONE;
        private bool _triggerLast;
        private bool _aimValid;
        private Vector3 _aimPoint;
        private string _notice = "";
        private float _noticeUntil;
        private string _alert = "";
        private float _alertUntil;
        private float _mfdTimer;

        public void SFEXT_L_EntityStart()
        {
            ShowAim(false);
        }

        public void SFEXT_O_PilotEnter()
        {
            _pilot = true;
            _pick = Progress ? Progress.GetCallPick() : ACO_PlayerProgress.CALL_NONE;
            _triggerLast = true;   // 탈 때 누르고 있던 트리거로 부르지 않게
            _mfdTimer = 0f;
            UpdateMfd();
        }

        public void SFEXT_O_PilotExit()
        {
            _pilot = false;
            _selected = false;
            ShowAim(false);
            if (Manager) { Manager.LocalPilotGone(SupportIndex); }
        }

        public void SFEXT_G_Explode()
        {
            if (_pilot && Manager) { Manager.LocalPilotGone(SupportIndex); }
        }

        public void DFUNC_Selected()
        {
            _selected = true;
            _triggerLast = true;
        }

        public void DFUNC_Deselected()
        {
            _selected = false;
            ShowAim(false);
        }

        /// 데스크톱 키로 이 칸을 고른다(SaccEntity가 부른다).
        public void KeyboardInput()
        {
            if (EntityControl) { EntityControl.ToggleStickSelection(this); }
        }

        void Update()
        {
            if (!_pilot) { return; }
            _mfdTimer -= Time.deltaTime;
            if (_mfdTimer <= 0f) { _mfdTimer = MfdInterval; UpdateMfd(); }
            if (!_selected) { return; }
            float trigger = Input.GetAxisRaw(LeftDial ? "Oculus_CrossPlatform_PrimaryIndexTrigger" : "Oculus_CrossPlatform_SecondaryIndexTrigger");
            bool pressed = trigger > 0.75f || Input.GetKey(FireKey);
            bool clicked = pressed && !_triggerLast;
            _triggerLast = pressed;

            if (_pick == ACO_PlayerProgress.CALL_STRIKE)
            {
                UpdateAim();
                if (clicked) { TryStrike(); }
            }
            else
            {
                ShowAim(false);
                if (clicked)
                {
                    if (_pick == ACO_PlayerProgress.CALL_TANKER) { TryTanker(); }
                    else if (_pick == ACO_PlayerProgress.CALL_GUARDIAN) { TryGuardian(); }
                    else { Warn("NONE", "SUPPORT\nNONE - PICK IN LOADOUT"); }
                }
            }
        }

        // ───────────── 벙커버스터 ─────────────

        private void UpdateAim()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { ShowAim(false); return; }
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 dir = head.rotation * Vector3.forward;
            // 내 기체(꼬리·날개)에 걸리지 않게 조금 앞에서 쏜다
            Vector3 from = head.position + dir * 25f;
            RaycastHit hit;
            float best = MaxRange;
            _aimValid = false;
            if (Physics.Raycast(from, dir, out hit, MaxRange, AimLayers, QueryTriggerInteraction.Ignore) && !IsOwnVehicle(hit.collider))
            {
                best = hit.distance;
                _aimPoint = hit.point;
                _aimValid = true;
            }
            if (Physics.Raycast(from, dir, out hit, best, WaterLayers, QueryTriggerInteraction.Collide))
            {
                _aimPoint = hit.point;
                _aimValid = true;
            }
            if (!_aimValid) { ShowAim(false); return; }

            Vector3 origin = LaserOrigin ? LaserOrigin.position : (EntityControl ? EntityControl.transform.position - EntityControl.transform.up * 1.5f : from);
            if (AimLaser)
            {
                // 빔 자신의 좌표로(고속에서 기체가 그 뒤에 움직여도 포드에 붙어 다닌다)
                if (AimLaser.useWorldSpace) { AimLaser.useWorldSpace = false; }
                AimLaser.SetPosition(0, AimLaser.transform.InverseTransformPoint(origin));
                AimLaser.SetPosition(1, AimLaser.transform.InverseTransformPoint(_aimPoint));
                if (!AimLaser.enabled) { AimLaser.enabled = true; }
            }
            if (AimRing)
            {
                // 점 수·닫힘(loop)은 빌더가 정해 둔다(RingSegments) — 여기선 자리만
                int n = Mathf.Max(RingSegments, 8);
                float r = Manager ? Manager.StrikeRadius : 500f;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    AimRing.SetPosition(i, _aimPoint + new Vector3(Mathf.Cos(a) * r, 2f, Mathf.Sin(a) * r));
                }
                if (!AimRing.enabled) { AimRing.enabled = true; }
            }
        }

        private bool IsOwnVehicle(Collider c)
        {
            return c != null && EntityControl != null && c.transform.IsChildOf(EntityControl.transform);
        }

        private void TryStrike()
        {
            int c = ACO_PlayerProgress.CALL_STRIKE;
            if (!CanCall(c)) { return; }
            if (!_aimValid) { Warn("NO SPOT", "STRIKE\nNO SPOT - LOOK AT GROUND"); return; }
            Vector3 fwd = EntityControl ? EntityControl.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) { fwd = Vector3.forward; }
            fwd = fwd.normalized;
            if (!Manager.CallStrike(SupportIndex, _aimPoint, fwd)) { Warn("BUSY", "STRIKE\nNO BOMBER AVAILABLE"); return; }
            Progress.UseCall(c);
            Progress.StartCallCooldown(c);
        }

        // ───────────── 급유 드론 ─────────────

        private void TryTanker()
        {
            int c = ACO_PlayerProgress.CALL_TANKER;
            if (Manager && Manager.IsTankerActive(SupportIndex)) { Warn("ACTIVE", "TANKER\nALREADY ON THE WAY"); return; }
            if (!CanCall(c)) { return; }
            if (!Manager.CallTanker(SupportIndex)) { Warn("BUSY", "TANKER\nNO DRONE AVAILABLE"); }
        }

        // ───────────── 가디언 드론 ─────────────

        private void TryGuardian()
        {
            int c = ACO_PlayerProgress.CALL_GUARDIAN;
            if (Manager && Manager.GetGuardianLeft(SupportIndex) > 0f) { Warn("ACTIVE", "GUARDIAN\nALREADY ON STATION"); return; }
            if (!CanCall(c)) { return; }
            if (!Manager.CallGuardian(SupportIndex)) { Warn("BUSY", "GUARDIAN\nNO DRONE AVAILABLE"); return; }
            Progress.UseCall(c);   // 쿨타임은 드론이 돌아간 뒤부터(관리자)
        }

        private bool CanCall(int c)
        {
            if (!Manager || !Progress || SupportIndex < 0) { Warn("N/A", "SUPPORT\nUNAVAILABLE"); return false; }
            if (Progress.GetCallCount(c) <= 0) { Warn("EMPTY", "SUPPORT\nNO CALLS - BUY IN ITEMS"); return false; }
            float cd = Progress.GetCallCooldown(c);
            if (cd > 0f) { Warn("WAIT", "SUPPORT\nCOOLDOWN " + Mathf.CeilToInt(cd) + "s"); return false; }
            return true;
        }

        private void ShowAim(bool on)
        {
            if (AimLaser && AimLaser.enabled != on) { AimLaser.enabled = on; }
            if (AimRing && AimRing.enabled != on) { AimRing.enabled = on; }
        }

        /// 관리자가 띄우는 안내(급유 대기 중 STEADY 등).
        public void ShowNotice(string s) { Notice(s); }

        /// 관리자가 띄우는 안내 + HUD 경고(급유 대기 중 "TANKER: BANK UNDER 45" 등).
        public void ShowAlert(string shortCode, string alert) { Warn(shortCode, alert); }

        /// 조건이 맞지 않을 때: 잔탄 줄·MFD에 짧은 글자(shortCode), HUD 상태 줄에 긴 경고(alert — 깜빡임, AlertTime초).
        private void Warn(string shortCode, string alert)
        {
            Notice(shortCode);
            _alert = alert;
            _alertUntil = Time.time + AlertTime;
        }

        /// HUD 상태 줄에 띄울 경고(없으면 ""). 조종 중일 때만.
        public string GetAlert() { return _pilot && _alert != "" && Time.time < _alertUntil ? _alert : ""; }

        private void Notice(string s)
        {
            _notice = s;
            _noticeUntil = Time.time + NoticeTime;
        }

        private void UpdateMfd()
        {
            if (!MfdText) { return; }
            string s = GetMfdText();
            if (MfdText.text != s) { MfdText.text = s; }
        }

        /// MFD 글자: "BB x3" + (쿨타임이나 안내가 있으면) 줄바꿈 + "85s" / "NO SPOT". 고른 것이 없으면 "SUP - N/A".
        public string GetMfdText()
        {
            int p = GetPick();
            if (p < 0 || !Progress) { return "SUP - N/A"; }
            string s = GetHudName() + " x" + Progress.GetCallCount(p);
            string second = "";
            if (_notice != "" && Time.time < _noticeUntil) { second = _notice; }
            else if (GuardianOnText(p) != "") { second = GuardianOnText(p); }
            else
            {
                float cd = Progress.GetCallCooldown(p);
                if (cd > 0f) { second = Mathf.CeilToInt(cd) + "s"; }
            }
            return second == "" ? s : s + "\n" + second;
        }

        /// 가디언이 떠 있으면 "ON 64s", 아니면 "".
        private string GuardianOnText(int pick)
        {
            if (pick != ACO_PlayerProgress.CALL_GUARDIAN || !Manager) { return ""; }
            float left = Manager.GetGuardianLeft(SupportIndex);
            return left > 0f ? "ON " + Mathf.CeilToInt(left) + "s" : "";
        }

        // ───────────── 관리자·HUD가 읽는다 ─────────────

        public bool IsPilot() { return _pilot; }
        public bool IsSelected() { return _pilot && _selected; }
        public int GetPick() { return _pilot ? _pick : (Progress ? Progress.GetCallPick() : ACO_PlayerProgress.CALL_NONE); }

        /// HUD 잔탄 줄 이름: BB(벙커버스터) / TNK(급유 드론) / SUP(고른 것 없음).
        public string GetHudName()
        {
            int p = GetPick();
            if (p == ACO_PlayerProgress.CALL_STRIKE) { return "BB"; }
            if (p == ACO_PlayerProgress.CALL_TANKER) { return "TNK"; }
            if (p == ACO_PlayerProgress.CALL_GUARDIAN) { return "GD"; }
            return "SUP";
        }

        /// HUD 잔탄 줄 수: 안내 문구(잠깐) → 가디언이 떠 있으면 남은 시간("ON 64s") → 쿨타임 남은 초("85s") → 장수. 고른 것이 없으면 "---".
        public string GetHudCount()
        {
            if (_notice != "" && Time.time < _noticeUntil) { return _notice; }
            int p = GetPick();
            if (p < 0 || !Progress) { return "---"; }
            string on = GuardianOnText(p);
            if (on != "") { return on; }
            float cd = Progress.GetCallCooldown(p);
            if (cd > 0f) { return Mathf.CeilToInt(cd) + "s"; }
            return Progress.GetCallCount(p).ToString();
        }
    }
}
