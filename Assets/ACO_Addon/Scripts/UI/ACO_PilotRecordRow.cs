using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 조종사 기록판(6-341) 버튼(로컬): Index ≥ 0 = 목록 줄(누르면 칭호 장착 · 해제), -1 = ▲(위로), -2 = ▼(아래로).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_PilotRecordRow : UdonSharpBehaviour
    {
        public ACO_PilotRecord Record;
        public int Index;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 버튼 효과음을 넣는다")]
        public ACO_UiSound Sound;

        /// UI 버튼 OnClick에서 부른다.
        public void Click()
        {
            if (!Record) { Record = GetComponentInParent<ACO_PilotRecord>(); }
            if (!Record) { return; }
            bool ok = Index >= 0 ? Record.ClickRow(Index) : Record.ScrollStep(Index == -1 ? -1 : 1);
            if (Sound) { Sound.PlayAt(ok ? ACO_UiSound.KIND_CLICK : ACO_UiSound.KIND_DENY, transform.position); }
        }
    }
}
