using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

namespace ACO
{
    /// 머리를 따라오는 점수 HUD(로컬 전용). 팀 점수, 남은 시간, 목표 진행, 웨이브를 보여 주고,
    /// 미션이 끝나면 결과(클리어/실패, 랭크, 보너스)를 크게 띄운다. 값은 ACO_MissionManager의 동기화 상태를 읽는다.
    ///
    /// 회전만 부드럽게 따라가고 위치는 매 프레임 머리에 고정한다 — 위치까지 늦게 따라가면 고속 비행 중 패널이 수십 m 뒤처진다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_ScoreHUD : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 관리자를 자동으로 넣는다")]
        public ACO_MissionManager Manager;
        [Tooltip("머리를 따라 움직일 패널(텍스트들의 부모)")]
        public Transform Panel;
        [Tooltip("점수/시간/목표/웨이브")]
        public TextMeshPro MainText;
        [Tooltip("미션 결과(클리어/실패, 랭크)")]
        public TextMeshPro ResultText;
        [Tooltip("짧은 안내(좌석 재조정 등). 미션 상태와 상관없이 ShowNotice로 잠깐 띄운다. 프리팹 빌더/Bake가 만든다")]
        public TextMeshPro NoticeText;
        [Tooltip("결과·안내 글자 묶음(점수 패널과 따로 머리를 따라온다). 패널 안에 있던 두 글자는 시작할 때 이리로 옮긴다(결과 글자가 묶음 원점). "
            + "단위는 점수 패널과 같게(스케일 0.03). 비우면 예전처럼 점수 패널에 붙어 있다. 프리팹 빌더/Bake가 만든다")]
        public Transform ResultGroup;

        [Header("위치")]
        [Tooltip("머리 기준 위치(m): 오른쪽(+X), 위(+Y), 앞(+Z)")]
        public Vector3 HeadOffset = new Vector3(-0.28f, 0.16f, 0.9f);
        [Tooltip("결과·안내 묶음의 머리 기준 위치(m): 오른쪽(+X), 위(+Y). 앞(거리)은 HeadOffset.z와 같다(HUD 설정 패널이 바꾼다)")]
        public Vector2 ResultOffset = new Vector2(0f, 0.07f);
        [Tooltip("머리 회전을 따라가는 빠르기. 클수록 딱 붙고, 작을수록 부드럽게 늦게 따라온다")]
        public float FollowSharpness = 10f;
        [Tooltip("텍스트 갱신 주기(초)")]
        public float UpdateInterval = 0.25f;
        [Tooltip("미션 시작 전(대기)에도 패널을 보여 준다")]
        public bool ShowWhenIdle = false;
        [Tooltip("글자 불투명도(1 = 불투명)")]
        [Range(0.1f, 1f)] public float TextAlpha = 0.85f;
        [Tooltip("미션 결과(클리어/실패, 랭크)를 보여 주는 시간(초)")]
        public float ResultDisplayTime = 8f;
        [Tooltip("페이즈 시작/클리어 안내를 보여 주는 시간(초)")]
        public float PhaseBannerTime = 5f;

        private float _previewUntil;   // HUD 설정 패널에서 조정하는 동안 미션 전에도 보여 준다
        private float _noticeUntil;
        private bool _groupReady;

        private Quaternion _smoothRot = Quaternion.identity;
        private bool _hasRot;
        private float _textTimer;
        private int _lastState = -1;
        private float _resultTimer;
        private int _lastPhaseIndex = -2;   // -2 = 아직 기준 없음
        private int _lastCleared;
        private int _lastPhaseType;

        public override void PostLateUpdate()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || Panel == null) { return; }

            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            if (!_hasRot) { _smoothRot = head.rotation; _hasRot = true; }
            _smoothRot = Quaternion.Slerp(_smoothRot, head.rotation, 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            Panel.SetPositionAndRotation(head.position + _smoothRot * HeadOffset, _smoothRot);
            if (ResultGroup != null)
            {
                if (!_groupReady) { SetupResultGroup(); }
                ResultGroup.SetPositionAndRotation(head.position + _smoothRot * new Vector3(ResultOffset.x, ResultOffset.y, HeadOffset.z), _smoothRot);
            }

            if (_noticeUntil > 0f && Time.time >= _noticeUntil)
            {
                _noticeUntil = 0f;
                if (NoticeText) { NoticeText.gameObject.SetActive(false); }
            }

            _textTimer -= Time.deltaTime;
            if (_textTimer > 0f) { return; }
            _textTimer = UpdateInterval;
            RefreshText();
        }

        /// 결과·안내 글자를 묶음으로 옮긴다(예전 HUD는 점수 패널 안에 있다 — Bake는 프리팹 인스턴스라 옮기지 못해 실행 중에 옮긴다).
        /// 결과 글자가 묶음 원점, 안내 글자는 결과 글자와의 간격을 그대로 둔다.
        private void SetupResultGroup()
        {
            _groupReady = true;
            // TextMeshPro.transform은 Udon에 노출돼 있지 않다 — gameObject.transform으로 간다.
            Vector3 anchor = Vector3.zero;
            if (ResultText != null)
            {
                Transform rt = ResultText.gameObject.transform;
                if (rt.parent != ResultGroup)
                {
                    anchor = rt.localPosition;
                    rt.SetParent(ResultGroup, false);
                    rt.localPosition = Vector3.zero;
                }
            }
            if (NoticeText != null)
            {
                Transform nt = NoticeText.gameObject.transform;
                if (nt.parent != ResultGroup)
                {
                    Vector3 p = nt.localPosition - anchor;
                    nt.SetParent(ResultGroup, false);
                    nt.localPosition = p;
                }
            }
        }

        /// 짧은 안내를 seconds 동안 띄운다(미션 상태와 상관없이). 다시 부르면 글자와 시간을 바꾼다.
        public void ShowNotice(string text, float seconds)
        {
            if (!NoticeText) { return; }
            NoticeText.text = text;
            NoticeText.color = new Color(1f, 0.85f, 0.3f, TextAlpha);
            if (!NoticeText.gameObject.activeSelf) { NoticeText.gameObject.SetActive(true); }
            _noticeUntil = Time.time + Mathf.Max(seconds, 0.1f);
        }

        /// HUD 설정 패널(ACO_HudSettings)이 부른다: seconds 동안 미션 전에도 패널을 보여 준다(위치·크기 확인용).
        public void Preview(float seconds)
        {
            _previewUntil = Time.time + seconds;
            _textTimer = 0f;
            // 미션 전이면 안내 글자도 예시로 띄운다(결과·안내 묶음 위치 확인용 — 결과 글자 예시는 RefreshText가).
            if (IsIdlePreview()) { ShowNotice("NOTICE PREVIEW", seconds); }
        }

        private bool IsIdlePreview()
        {
            int state = Manager != null ? Manager.GetState() : ACO_MissionManager.STATE_IDLE;
            return state == ACO_MissionManager.STATE_IDLE && !ShowWhenIdle && Time.time < _previewUntil;
        }

        private void RefreshText()
        {
            int state = Manager != null ? Manager.GetState() : ACO_MissionManager.STATE_IDLE;
            if (IsIdlePreview())
            {
                if (MainText)
                {
                    if (!MainText.gameObject.activeSelf) { MainText.gameObject.SetActive(true); }
                    MainText.text = "SCORE 12,345  <size=75%>x1.5</size>\nTIME 12:34   WAVE 2\n<size=80%>HUD PREVIEW</size>";
                    MainText.color = new Color(0.4f, 1f, 0.5f, TextAlpha);
                }
                if (ResultText)
                {
                    if (!ResultText.gameObject.activeSelf) { ResultText.gameObject.SetActive(true); }
                    ResultText.text = "PHASE 1/4  RESULT PREVIEW\n<size=60%>RESULT / BANNER TEXT</size>";
                    ResultText.color = new Color(1f, 0.85f, 0.3f, TextAlpha);
                }
                return;
            }
            // 패널을 통째로 끄지 않고 텍스트만 켜고 끈다(패널에 이 스크립트를 붙여도 멈추지 않게).
            bool visible = Manager != null && (state != ACO_MissionManager.STATE_IDLE || ShowWhenIdle);
            if (MainText && MainText.gameObject.activeSelf != visible) { MainText.gameObject.SetActive(visible); }
            if (!visible)
            {
                if (ResultText && ResultText.gameObject.activeSelf) { ResultText.gameObject.SetActive(false); }
                _lastState = state;
                return;
            }

            if (MainText)
            {
                string time;
                float remaining = Manager.GetRemainingSeconds();
                if (remaining >= 0f) { time = "TIME " + FormatTime(remaining); }
                else { time = "ELAPSED " + FormatTime(Manager.GetElapsedSeconds()); }

                MainText.text = "SCORE " + Manager.GetTeamScore().ToString("N0")
                    + "  <size=75%>x" + Manager.GetScoreMultiplier().ToString("0.0#") + "</size>"
                    + "\n" + time + "   WAVE " + Manager.GetWavesSent()
                    + "\n" + PhaseLine(state);
                // 남은 시간이 1분 아래면 경고색
                MainText.color = (remaining >= 0f && remaining < 60f && state == ACO_MissionManager.STATE_RUNNING)
                    ? new Color(1f, 0.35f, 0.3f, TextAlpha) : new Color(0.4f, 1f, 0.5f, TextAlpha);
            }

            if (ResultText)
            {
                bool done = state == ACO_MissionManager.STATE_COMPLETE || state == ACO_MissionManager.STATE_FAILED;
                if (done && state != _lastState)
                {
                    // 결과창은 끝난 순간부터 ResultDisplayTime 동안만 보여 준다(자세한 내용은 디브리핑 보드에).
                    if (state == ACO_MissionManager.STATE_COMPLETE)
                    {
                        ShowBanner("MISSION COMPLETE\n<size=160%>RANK " + Manager.GetRankLetter() + "</size>"
                            + "\n<size=70%>CLEAR +" + Manager.GetClearBonus().ToString("N0")
                            + "   TIME +" + Manager.GetTimeBonus().ToString("N0") + "</size>",
                            new Color(1f, 0.85f, 0.3f, TextAlpha), ResultDisplayTime);
                    }
                    else
                    {
                        string reason = Manager.GetFailReasonText(true);
                        ShowBanner("MISSION FAILED\n<size=160%>RANK F</size>\n<size=70%>" + reason + "</size>", new Color(1f, 0.3f, 0.25f, TextAlpha), ResultDisplayTime);
                    }
                }
                if (done) { _lastPhaseIndex = -2; }
                else { UpdatePhaseBanners(); }

                if (_resultTimer > 0f) { _resultTimer -= UpdateInterval; }
                bool showResult = _resultTimer > 0f;
                if (ResultText.gameObject.activeSelf != showResult) { ResultText.gameObject.SetActive(showResult); }
            }
            _lastState = state;
        }

        /// 페이즈가 시작되거나 끝나면 가운데에 안내를 띄운다. 처음 본 값(늦게 들어옴 포함)은 띄우지 않고 기준만 잡는다.
        private void UpdatePhaseBanners()
        {
            if (Manager.GetState() != ACO_MissionManager.STATE_RUNNING) { _lastPhaseIndex = -2; return; }
            int phase = Manager.GetPhaseIndex();
            int cleared = Manager.GetPhasesCleared();
            // 처음 보거나 새 미션(값이 줄어듦)이면 기준만 잡는다.
            if (_lastPhaseIndex == -2 || cleared < _lastCleared || phase < _lastPhaseIndex)
            {
                _lastPhaseIndex = phase;
                _lastCleared = cleared;
                _lastPhaseType = Manager.GetCurrentPhaseType();
                return;
            }
            if (cleared > _lastCleared)
            {
                _lastCleared = cleared;
                if (Manager.GetLastPhaseFailed())
                {
                    int endedType = Manager.GetCurrentPhaseType();
                    // 실패한 채 넘어가는 페이즈는 이제 보너스 페이즈(슈퍼에이스가 물러남)뿐이다 — 나머지 실패는 미션 실패 배너.
                    string lost = endedType == ACO_MissionManager.PHASE_BONUS ? "TARGET RETREATED"
                        : (endedType == ACO_MissionManager.PHASE_CONVOY ? "CONVOY LOST" : (endedType == ACO_MissionManager.PHASE_TST ? "TARGET ESCAPED" : "VIP LOST"));
                    int failBonus = Manager.GetLastPhaseBonus();
                    if (failBonus > 0) { lost += "  +" + failBonus.ToString("N0"); }
                    ShowBanner("PHASE " + cleared + " FAILED\n<size=80%>" + lost + "</size>", new Color(1f, 0.3f, 0.25f, TextAlpha), PhaseBannerTime);
                }
                else
                {
                    int bonus = Manager.GetLastPhaseBonus();
                    ShowBanner("PHASE " + cleared + " COMPLETE" + (bonus > 0 ? "\n<size=80%>+" + bonus.ToString("N0") + "</size>" : ""),
                        new Color(0.4f, 1f, 0.5f, TextAlpha), PhaseBannerTime);
                }
            }
            // 배치가 안 돼 같은 페이즈가 다른 종류로 바뀌면 다시 알린다.
            int currentType = Manager.GetCurrentPhaseType();
            bool changed = phase == _lastPhaseIndex && phase >= 0 && Manager.IsPhaseActive() && currentType != _lastPhaseType;
            if (phase > _lastPhaseIndex || changed)
            {
                _lastPhaseIndex = phase;
                _lastPhaseType = currentType;
                int type = currentType;
                string location = Manager.GetPhaseLocation();
                ShowBanner("PHASE " + (phase + 1) + "/" + Manager.GetPhaseCount() + "  " + Manager.GetPhaseName(type)
                    + "\n<size=60%>" + Manager.GetPhaseDescription(type)
                    + "   x" + Manager.GetScoreMultiplier().ToString("0.0#")
                    + (location.Length > 0 ? "\n" + location : "") + "</size>",
                    new Color(1f, 0.85f, 0.3f, TextAlpha), PhaseBannerTime);
            }
        }

        private void ShowBanner(string text, Color color, float seconds)
        {
            ResultText.text = text;
            ResultText.color = color;
            _resultTimer = seconds;
        }

        /// 페이즈 줄: "PHASE 2/4 SEAD  3/5" 등. 페이즈 사이면 다음 페이즈까지 남은 시간.
        private string PhaseLine(int state)
        {
            int count = Manager.GetPhaseCount();
            int phase = Manager.GetPhaseIndex();
            if (state != ACO_MissionManager.STATE_RUNNING)
            {
                return "PHASE " + Manager.GetPhasesCleared() + "/" + count + " CLEAR";
            }
            if (!Manager.IsPhaseActive())
            {
                return "NEXT PHASE " + Mathf.CeilToInt(Manager.GetNextPhaseSeconds()) + "s   (" + Manager.GetPhasesCleared() + "/" + count + ")";
            }

            int type = Manager.GetCurrentPhaseType();
            string head = "PHASE " + (phase + 1) + "/" + count + "  " + Manager.GetPhaseName(type) + "  ";
            if (type == ACO_MissionManager.PHASE_QUOTA)
            {
                return head + Manager.GetObjectivesDone().ToString("N0") + "/" + Manager.GetObjectiveTotal().ToString("N0");
            }
            if (type == ACO_MissionManager.PHASE_TST)
            {
                string s = head + Manager.GetTstHits() + "/" + Manager.GetTstCount();
                float left = Manager.GetTstRemainingSeconds();
                if (left >= 0f) { s += "  <color=#FF5544>" + FormatTime(left) + "</color>"; }
                return s;
            }
            if (type == ACO_MissionManager.PHASE_ESCORT)
            {
                // 호위: 대상 체력과 도착까지 남은 거리. 체력이 낮으면 빨갛게.
                ACO_EscortUnit escort = Manager.GetActiveEscort();
                if (escort == null) { return head + "-"; }
                int hp = Mathf.RoundToInt(escort.GetHealthFraction() * 100f);
                string hpText = hp <= 30 ? "<color=#FF5544>" + escort.MarkerLabel + " " + hp + "%</color>" : escort.MarkerLabel + " " + hp + "%";
                return head + hpText + "  " + (escort.GetRemainingDistance() * 0.001f).ToString("0.0") + "km";
            }
            if (type == ACO_MissionManager.PHASE_INTERCEPT)
            {
                // 요격: 남은 폭격기 / 전체, 도시에 가장 가까운 폭격기의 남은 거리(5km 아래면 빨갛게).
                int total = Manager.GetBombersTotal();
                if (total == 0) { return head + "-"; }
                float near = Manager.GetBomberNearestDistance();
                string dist = near >= 0f ? (near * 0.001f).ToString("0.0") + "km" : "";
                if (near >= 0f && near < 5000f) { dist = "<color=#FF5544>" + dist + "</color>"; }
                return head + "BMR " + Manager.GetBombersAlive() + "/" + total + (dist.Length > 0 ? "  CITY " + dist : "");
            }
            if (type == ACO_MissionManager.PHASE_CONVOY)
            {
                // 지상 호위: 살아 있는 차량 / 전체, 가장 뒤처진 차량의 남은 거리. 절반 이하면 빨갛게.
                int alive = Manager.GetConvoyAlive();
                int total = Manager.GetConvoyTotal();
                float remain = Manager.GetConvoyRemainingDistance();
                if (total == 0) { return head + "-"; }
                string cvy = "CVY " + alive + "/" + total;
                if (alive * 2 <= total) { cvy = "<color=#FF5544>" + cvy + "</color>"; }
                return head + cvy + (remain >= 0f ? "  " + (remain * 0.001f).ToString("0.0") + "km" : "");
            }
            if (Manager.GetObjectiveTotal() == 0) { return head + "-"; }
            return head + Manager.GetObjectivesDone() + "/" + Manager.GetObjectiveTotal();
        }

        private string FormatTime(float seconds)
        {
            int s = Mathf.Max(Mathf.FloorToInt(seconds), 0);
            int m = s / 60;
            s = s % 60;
            return m.ToString("00") + ":" + s.ToString("00");
        }
    }
}
