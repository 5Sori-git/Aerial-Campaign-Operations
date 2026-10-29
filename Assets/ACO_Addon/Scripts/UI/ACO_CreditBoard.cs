using UdonSharp;
using UnityEngine;
using TMPro;

namespace ACO
{
    /// 크레딧 보드(로컬 전용, 동기화 없음): 시작할 때 Source(텍스트 파일 — Credits/ACO_Credits.txt)를 본문 글자에 넣는다.
    /// 크레딧은 텍스트 파일만 고치면 된다(프리팹을 다시 만들 필요 없음). 기본 TMP 폰트(LiberationSans)라 영문으로 적는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_CreditBoard : UdonSharpBehaviour
    {
        [Tooltip("크레딧 텍스트 파일(TMP 서식 태그 사용 가능). 비우면 본문을 그대로 둔다")]
        public TextAsset Source;
        [Tooltip("본문 글자(프리팹 빌더가 넣는다)")]
        public TextMeshProUGUI Body;

        void Start()
        {
            if (Source != null && Body != null) { Body.text = Source.text; }
        }
    }
}
