using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 거리 단위(로컬 전용, 동기화 없음): HUD 설정 패널의 DISTANCE UNIT이 바꾸고, 거리를 글자로 쓰는 곳(비행 HUD·표적 마커·페이즈 진행 글자)이 같이 쓴다.
    /// NauticalMiles면 해리(1NM = 1852m, 10NM 아래는 소수 한 자리), 아니면 m(1km 아래)·km(소수 한 자리). 속도·고도는 비행 HUD가 kt·ft로 고정.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Units : UdonSharpBehaviour
    {
        [Tooltip("켜면 해리(NM), 끄면 m/km")]
        public bool NauticalMiles = false;

        public string Distance(float meters)
        {
            if (NauticalMiles)
            {
                float nm = meters / 1852f;
                return nm < 10f ? nm.ToString("0.0") + "NM" : Mathf.RoundToInt(nm) + "NM";
            }
            if (meters < 1000f) { return Mathf.RoundToInt(meters) + "m"; }
            return (meters * 0.001f).ToString("0.0") + "km";
        }
    }
}
