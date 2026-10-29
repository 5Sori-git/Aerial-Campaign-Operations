using UdonSharp;
using UnityEngine;
using VRC.Udon.Common;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 좌석 눈높이 재조정(SAV 확장, 로컬 전용). VR에서 탑승 중 점프 버튼을 한 번 누르면 좌석 보정을 다시 돌린다 — 두 번 누르면 SAV가 내린다
    /// (SaccEntity.DoubleTapToExit, 셋업이 켠다). 한 번 누른 뒤 DoubleTapWindow 동안 두 번째 입력이 없을 때만 재조정한다.
    /// 보정은 SAV SaccVehicleSeat가 탈 때 하는 것과 같다(높이·앞뒤, VR이면 방향): 보정 완료 표시(private CalibratedY/Z/Yaw, AdjustTime)를
    /// SetProgramVariable로 되돌리고 SeatAdjustment()를 부른다. SAV 파일은 바꾸지 않는다 — SAV가 이 변수 이름을 바꾸면 경고만 남기고 아무것도 하지 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_SeatRecenter : UdonSharpBehaviour
    {
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 점수 HUD를 넣는다(안내 문구). 비면 안내 없이 재조정만 한다")]
        public ACO_ScoreHUD Hud;
        [Tooltip("두 번 누름으로 보는 간격(초). SAV DoubleTapToExit와 같은 0.3초")]
        public float DoubleTapWindow = 0.3f;
        [Tooltip("재조정할 때 HUD에 띄우는 안내")]
        public string RecenterNotice = "SEAT RECENTERING\n<size=70%>SIT UPRIGHT AND LOOK STRAIGHT AHEAD</size>";
        [Tooltip("탄 직후 보정이 아직 도는 중일 때 안내")]
        public string BusyNotice = "SEAT ADJUSTING...";
        [Tooltip("안내를 보여 주는 시간(초)")]
        public float NoticeTime = 2.5f;

        /// SAV가 넣는다(ExtensionUdonBehaviours).
        [System.NonSerialized] public SaccEntity EntityControl;

        private float _lastPress = -10f;
        private float _pendingPress = -1f;
        private bool _warned;

        public override void InputJump(bool value, UdonInputEventArgs args)
        {
            // SAV 내리기와 같은 조건(VR, 탑승 중, 누름, 스페이스바 아님).
            if (!args.boolValue || EntityControl == null || !EntityControl.InVehicle || !EntityControl.InVR) { return; }
            if (Input.GetKey(KeyCode.Space)) { return; }
            float now = Time.time;
            if (now - _lastPress < DoubleTapWindow)
            {
                // 두 번째 누름 — SAV가 내린다. 재조정은 취소.
                _pendingPress = -1f;
                _lastPress = -10f;
                return;
            }
            _lastPress = now;
            _pendingPress = now;
            SendCustomEventDelayedSeconds(nameof(_CheckSingleTap), DoubleTapWindow + 0.02f);
        }

        /// 한 번만 눌렸는지 확인한다. 뒤에 다른 누름이 있었으면 그 누름의 확인에 맡긴다.
        public void _CheckSingleTap()
        {
            if (_pendingPress < 0f || Time.time - _pendingPress < DoubleTapWindow) { return; }
            _pendingPress = -1f;
            if (EntityControl == null || !EntityControl.InVehicle) { return; }
            Recenter();
        }

        private void Recenter()
        {
            SaccVehicleSeat[] seats = EntityControl.VehicleSeats;
            int s = EntityControl.MySeat;
            if (seats == null || s < 0 || s >= seats.Length) { return; }
            SaccVehicleSeat seat = seats[s];
            if (seat == null || !seat.AdjustSeatPosition || seat.TargetEyePosition == null) { return; }

            object y = seat.GetProgramVariable("CalibratedY");
            object z = seat.GetProgramVariable("CalibratedZ");
            object yaw = seat.GetProgramVariable("CalibratedYaw");
            if (y == null || z == null || yaw == null)
            {
                if (!_warned) { _warned = true; Debug.LogWarning("[ACO] 좌석 재조정: SAV SaccVehicleSeat의 보정 변수(CalibratedY/Z/Yaw)를 찾지 못했습니다 — SAV가 바뀌었을 수 있습니다.", seat); }
                return;
            }
            // 탄 직후 보정이 아직 돌고 있으면 겹치지 않게 그대로 둔다(보정 반복이 두 줄로 돈다).
            if (!(bool)y || !(bool)z || !(bool)yaw)
            {
                ShowNotice(BusyNotice);
                return;
            }
            seat.SetProgramVariable("CalibratedY", false);
            seat.SetProgramVariable("CalibratedZ", false);
            seat.SetProgramVariable("CalibratedYaw", !seat.AdjustSeatRotation);
            seat.SetProgramVariable("AdjustTime", 0f);
            seat.SeatAdjustment();
            ShowNotice(RecenterNotice);
        }

        private void ShowNotice(string text)
        {
            if (Hud != null && !string.IsNullOrEmpty(text)) { Hud.ShowNotice(text, NoticeTime); }
        }
    }
}
