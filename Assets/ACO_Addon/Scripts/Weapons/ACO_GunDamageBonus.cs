using UdonSharp;
using UnityEngine;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 기총 피해 강화(동기화 없음). SAV 기총 탄(충돌 메시지를 보내는 파티클 — FA-18이면 GunParticle_pilot, 조종사 화면에서만 충돌한다)에 붙는다.
    /// SAV는 탄이 SaccTarget에 맞으면 '10 ÷ 표적 장갑'(탄 오브젝트에 'd:값' 자식이 있으면 그 값)만큼 피해를 준다 — 그 값은 기체에 묶여 있어
    /// 플레이어별 강화를 넣을 수 없다. 그래서 같은 충돌을 이 스크립트도 받아, 같은 규칙의 기본 피해 × (Factor − 1)을 덤으로 준다(장갑 규칙 그대로).
    /// Factor는 기체 강화 확장(ACO_VehicleUpgrades)이 탑승할 때 내 강화(진행도 UP_GUN)로 넣고 내릴 때 1로 돌린다. 플레이어 기체(SaccEntity)는 건드리지 않는다(PvE).
    /// Bake가 플레이어 기체의 기총 탄 파티클에 붙이고 강화 확장에 연결한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_GunDamageBonus : UdonSharpBehaviour
    {
        [Tooltip("SAV 기총의 무기 종류(작전판 GUN 표시)")]
        public byte WeaponType = 1;

        /// 강화 배수. 1이면 아무것도 하지 않는다.
        [System.NonSerialized] public float Factor = 1f;

        private float _childDamage = -1f;   // 탄 오브젝트의 'd:값' 자식(없으면 -1 — 표적 장갑으로 계산)
        private bool _started;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            for (int i = 0; i < transform.childCount; i++)
            {
                string n = transform.GetChild(i).name;
                if (!n.StartsWith("d:")) { continue; }
                float v;
                if (float.TryParse(n.Substring(2), out v)) { _childDamage = v; }
            }
        }

        void OnParticleCollision(GameObject other)
        {
            if (Factor <= 1.0001f || !other) { return; }
            Start();
            SaccTarget t = other.GetComponent<SaccTarget>();
            if (!t) { t = other.GetComponentInParent<SaccTarget>(); }
            if (!t || t.DisableBulletHitEvent || t.Health <= 0f) { return; }
            float baseDamage = _childDamage >= 0f ? _childDamage : 10f / Mathf.Max(t.ArmorStrength, 0.0001f);
            if (baseDamage > 0f && baseDamage < t.NoDamageBelow) { return; }   // SAV가 무시하는 약한 탄은 덤도 없다
            float bonus = baseDamage * (Factor - 1f);
            if (bonus <= 0f) { return; }
            t.WeaponDamageTarget(bonus, gameObject, WeaponType);
        }
    }
}
