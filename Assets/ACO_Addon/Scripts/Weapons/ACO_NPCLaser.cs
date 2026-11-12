using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// NPC 기총(ACO_NPCGun)의 레이저 연출 부품 — 기총의 Laser에 넣으면 예광탄 대신 빔을 그린다(터렛·기총 규칙은 그대로: 포탑이 돌고, 기총이 쏠 때 켜고 멈출 때 끈다).
    /// 빔은 포구 정면으로 Length(m) — 표적보다 길게 뻗고, 포탑이 돌면 휘두른다. BlockLayers가 있으면 그 층에서 끊기고 닿은 곳에 Sparks.
    /// 쏘는 동안만 이 컴포넌트를 켠다(LateUpdate로 그 프레임의 포구 자리에 그린다 — 기총 수십 개가 매 프레임 비용을 내지 않게). 피해·판정은 기총이 한다.
    /// 쓰는 곳: 레이저 에이스 렌즈 포탑(빨강, 피해 있음), LUNA UAV 요격(파랑, 연출만).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_NPCLaser : UdonSharpBehaviour
    {
        [Tooltip("빔이 나가는 곳·방향(forward). 비우면 이 오브젝트")]
        public Transform Muzzle;
        [Tooltip("LineRenderer(점 2개) — 빔 자신의 좌표로 그린다(기체 아래에 두면 그린 뒤에 기체가 움직여도 포구에 붙어 다닌다)")]
        public LineRenderer Beam;
        [Tooltip("빔 길이(m)")]
        public float Length = 1500f;
        public float Width = 1f;
        [Tooltip("빔을 자르는 층(지형·건물). 0이면 자르지 않는다")]
        public LayerMask BlockLayers = 0;
        [Tooltip("빔이 닿은 곳의 불티(선택 — 월드 파티클, Emit)")]
        public ParticleSystem Sparks;

        private bool _on;

        void Start()
        {
            if (!_on)
            {
                if (Beam) { Beam.enabled = false; }
                enabled = false;
            }
        }

        /// 기총이 쏘기 시작·멈출 때 부른다(모든 화면).
        public void SetFiring(bool on)
        {
            _on = on;
            if (Beam) { Beam.enabled = on; Beam.widthMultiplier = Width; }
            enabled = on;
            if (on) { Draw(); }
        }

        void LateUpdate()
        {
            if (_on) { Draw(); }
        }

        private void Draw()
        {
            if (!Beam) { return; }
            Transform m = Muzzle != null ? Muzzle : transform;
            Vector3 from = m.position;
            Vector3 dir = m.forward;
            float length = Length;
            if (BlockLayers.value != 0)
            {
                RaycastHit hit;
                if (Physics.Raycast(from, dir, out hit, Length, BlockLayers.value, QueryTriggerInteraction.Ignore))
                {
                    length = hit.distance;
                    if (Sparks)
                    {
                        var emit = new ParticleSystem.EmitParams();
                        emit.position = hit.point;
                        emit.applyShapeToPosition = true;
                        Sparks.Emit(emit, 1);
                    }
                }
            }
            // 빔 자신의 좌표로: 이 프레임에 그린 뒤 기체가 움직여도(가디언 드론은 PostLateUpdate에 자리 — 실행 순서) 고속에서 포구와 어긋나지 않는다
            if (Beam.useWorldSpace) { Beam.useWorldSpace = false; }
            Transform bt = Beam.transform;
            Beam.SetPosition(0, bt.InverseTransformPoint(from));
            Beam.SetPosition(1, bt.InverseTransformPoint(from + dir * length));
        }
    }
}
