using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 급유 드론 한 대(연출 전용, 동기화 없음 — 지원 관리자 ACO_SupportManager가 위치와 호스를 정한다, 6-142).
    /// 몸체는 Model 자식(자리 표시 큐브 — 모델로 통째로 바꿔도 된다), 아래에 버디 포드(EEI BuddyPod — 호스 본 Drogue_Base → Drogue_001~025 → Drogue_Head).
    /// 호스: Bones(포드 쪽 → 드로그 순서)를 매 프레임 포드 출구 → 드로그 곡선(2차 베지어 — 드로그 쪽에선 진행 방향과 나란히)에 늘어놓는다.
    /// 드로그가 출구에 있으면 모든 본이 출구에 겹쳐 포드 안에 감긴 모양이 된다. 본마다 원래(펼친 포드) 자세를 기억해 두고, 원래 다음 본 쪽 축이 곡선 접선을 따라가게만 돌린다
    /// (본의 원래 비틀림·기울기는 그대로 — 드로그 바스켓이 틀어지지 않게).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_TankerDrone : UdonSharpBehaviour
    {
        [Tooltip("호스 본(포드 출구 Drogue_Base → … → 드로그 Drogue_Head 순서)")]
        public Transform[] Bones;
        [Tooltip("다 풀린 호스 길이(m). 0이면 시작할 때 펼친 포드의 본 간격을 더해 잰다")]
        public float HoseLength = 0f;
        [Tooltip("잴 수 없을 때 쓰는 길이(m)")]
        public float FallbackLength = 15f;
        [Tooltip("호스 굽음: 조절점을 드로그에서 진행 방향으로 출구까지 수평 거리 × 이것만큼 둔다. 0 = 곧은 줄, 1 = 출구에서 곧장 아래로 떨어졌다가 수평으로")]
        [Range(0f, 1f)] public float FlowBend = 0.5f;
        [Tooltip("엔진 소리(드론이 보일 때만 재생, 선택)")]
        public AudioSource EngineSound;

        private bool _measured;
        private float _length;
        private Quaternion[] _restRel;   // 본 원래 자세(포드 기준)
        private Vector3[] _restAxis;     // 본 원래 사슬 축(다음 본 쪽, 본 로컬 — 마지막 본은 앞 본에서 오는 쪽)

        void Start() { Measure(); }

        private void Measure()
        {
            if (_measured) { return; }
            _measured = true;
            _length = HoseLength;
            if (_length <= 0f && Bones != null)
            {
                for (int i = 1; i < Bones.Length; i++)
                {
                    if (Bones[i] && Bones[i - 1]) { _length += Vector3.Distance(Bones[i].position, Bones[i - 1].position); }
                }
            }
            if (_length < 1f) { _length = FallbackLength; }
            // 원래 자세: 처음 한 번(아직 움직이기 전 — 펼친 포드 모양)
            int n = Bones != null ? Bones.Length : 0;
            _restRel = new Quaternion[n];
            _restAxis = new Vector3[n];
            Quaternion podRot = PodRotation();
            Quaternion podInv = Quaternion.Inverse(podRot);
            for (int i = 0; i < n; i++)
            {
                Transform b = Bones[i];
                _restRel[i] = Quaternion.identity;
                _restAxis[i] = Vector3.up;
                if (!b) { continue; }
                _restRel[i] = podInv * b.rotation;
                Vector3 dir = Vector3.zero;
                if (i + 1 < n && Bones[i + 1]) { dir = Bones[i + 1].position - b.position; }
                else if (i > 0 && Bones[i - 1]) { dir = b.position - Bones[i - 1].position; }
                if (dir.sqrMagnitude > 0.000001f) { _restAxis[i] = Quaternion.Inverse(b.rotation) * dir.normalized; }
            }
        }

        private Quaternion PodRotation()
        {
            Transform b0 = Bones != null && Bones.Length > 0 ? Bones[0] : null;
            return b0 != null && b0.parent != null ? b0.parent.rotation : transform.rotation;
        }

        /// 다 풀린 호스 길이(m).
        public float GetHoseLength() { Measure(); return _length; }

        /// 포드 출구(호스가 나오는 점, 월드).
        public Vector3 GetHoseBase() { return Bones != null && Bones.Length > 0 && Bones[0] ? Bones[0].position : transform.position; }

        /// 드론 기준 출구 위치(로컬).
        public Vector3 GetHoseBaseLocal() { return transform.InverseTransformPoint(GetHoseBase()); }

        public void SetShown(bool on)
        {
            if (gameObject.activeSelf != on) { gameObject.SetActive(on); }
            if (EngineSound)
            {
                if (on && !EngineSound.isPlaying) { EngineSound.Play(); }
                else if (!on && EngineSound.isPlaying) { EngineSound.Stop(); }
            }
        }

        /// 호스 모양: 출구에서 head(드로그, 월드)까지 2차 베지어. 조절점은 head 높이에서 flow(진행 방향) 쪽으로 출구까지 수평 거리 × FlowBend —
        /// 출구에서 뒤·아래로 나와 드로그 쪽에선 진행 방향과 나란해진다(바스켓이 뒤를 보고 프로브가 축으로 들어간다. 예전 처짐 곡선은 끝이 아래에서
        /// 올라와 바스켓이 위로 들렸다). head가 출구에 있으면(감김) 모든 본이 출구에 겹치고 뒤(-flow)를 본다 — 풀리기 시작·다 감길 때 자세가 튀지 않게.
        public void PoseHose(Vector3 head, Vector3 flow)
        {
            if (Bones == null || Bones.Length < 2) { return; }
            Measure();
            Transform b0 = Bones[0];
            if (!b0) { return; }
            Vector3 p0 = b0.position;
            Vector3 f = flow.sqrMagnitude > 0.0001f ? flow.normalized : transform.forward;
            float reach = Mathf.Max(Vector3.Dot(p0 - head, f), 0f);
            Vector3 ctrl = head + f * (reach * FlowBend);
            Quaternion podRot = PodRotation();
            int n = Bones.Length;
            for (int i = 0; i < n; i++)
            {
                Transform b = Bones[i];
                if (!b) { continue; }
                float s = (float)i / (n - 1);
                float u = 1f - s;
                Vector3 p = u * u * p0 + 2f * u * s * ctrl + s * s * head;
                Vector3 tangent = 2f * u * (ctrl - p0) + 2f * s * (head - ctrl);
                if (tangent.sqrMagnitude < 0.000001f) { tangent = -f; }
                if (i > 0) { b.position = p; }
                if (i < _restRel.Length)
                {
                    // 원래 자세에서 원래 사슬 축만 접선으로 돌린다(최소 회전 — 본의 원래 비틀림 유지)
                    Quaternion rest = podRot * _restRel[i];
                    b.rotation = Quaternion.FromToRotation(rest * _restAxis[i], tangent.normalized) * rest;
                }
            }
        }
    }
}
