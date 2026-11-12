using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 락온 제한(적 공통 부품). 락온을 막아야 하는 적의 루트(락온 마커의 부모 쪽)에 붙인다 — 플레이어 무장(ACO_DFUNC_Missile)이 표적 마커에서
    /// 부모 쪽으로 찾아 두고, 락온 시간을 쌓을 때마다 CanBeLockedFrom을 묻는다. 무장 쪽은 어떤 적인지 모른다.
    /// 켜고 끄는 건 그 적의 스크립트가 한다(예: 레이저 에이스 — 2단계 동안 켠다). 꺼져 있으면 언제나 락온된다. 이미 쏜 미사일·기총은 막지 않는다.
    ///
    /// 모드
    ///  - MODE_FRONT_CONE(0): 정면 원뿔 안에서만 락온된다(ECM — Facing 앞 기준 HalfAngle 안).
    ///  - MODE_BLOCK(1): 어디서도 락온되지 않는다.
    ///  - MODE_REAR_CONE(2): 뒤쪽 원뿔 안에서만 락온된다(Facing 뒤 기준 HalfAngle 안).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ACO_LockGate : UdonSharpBehaviour
    {
        public const int MODE_FRONT_CONE = 0;
        public const int MODE_BLOCK = 1;
        public const int MODE_REAR_CONE = 2;

        [Tooltip("켜져 있을 때만 막는다. 그 적의 스크립트가 켜고 끈다")]
        public bool Active;
        [Tooltip("0 = 정면 원뿔 안에서만, 1 = 어디서도 안 됨, 2 = 뒤쪽 원뿔 안에서만")]
        public int Mode = MODE_FRONT_CONE;
        [Tooltip("원뿔 반각(도)")]
        public float HalfAngle = 15f;
        [Tooltip("정면 기준(앞 = +Z). 비우면 이 오브젝트")]
        public Transform Facing;

        /// from(락온하는 쪽 위치)에서 이 표적을 락온할 수 있는가.
        public bool CanBeLockedFrom(Vector3 from)
        {
            if (!Active) { return true; }
            if (Mode == MODE_BLOCK) { return false; }
            Transform f = Facing != null ? Facing : transform;
            Vector3 d = from - f.position;
            if (d.sqrMagnitude < 1f) { return true; }
            Vector3 axis = Mode == MODE_REAR_CONE ? -f.forward : f.forward;
            return Vector3.Angle(axis, d) <= HalfAngle;
        }
    }
}
