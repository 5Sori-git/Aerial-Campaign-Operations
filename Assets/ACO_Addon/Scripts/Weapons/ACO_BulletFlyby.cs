using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 내 기체를 노린 총탄이 스쳐 지나가는 소리(로컬, 동기화 없음). 기총·대공포(ACO_NPCGun)가 나를 쏘는 동안 ReportFire를 부른다.
    /// 소리는 탄이 날아오는 시간만큼 늦게 나고, 총이 몇 문이든 전체 간격(MinInterval~MaxInterval)을 지켜 시끄러워지지 않는다.
    /// 조종석에서 듣는 소리라 3D로 두지 않고, 쏘는 쪽으로 좌우만 치우친다 — 기체가 빨라 3D 소리는 재생 중에 뒤로 처진다.
    /// Bake가 ACO_RadarContacts 아래에 만들고 기총과 잇는다. 소리는 Clips를 바꾸면 된다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_BulletFlyby : UdonSharpBehaviour
    {
        private const int MAX_PENDING = 8;

        [Header("참조 (Bake가 채운다)")]
        public ACO_RadarContacts Contacts;
        [Tooltip("동시에 겹쳐 나는 소리 — 2D 오디오 소스")]
        public AudioSource[] Sources;
        public AudioClip[] Clips;

        [Header("조건")]
        [Tooltip("탄이 내 기체에서 이 거리(m) 안으로 지날 만큼 조준이 맞아야 소리가 난다")]
        public float MissRadius = 60f;
        [Tooltip("소리 사이 최소 간격(초)")]
        public float MinInterval = 0.12f;
        [Tooltip("소리 사이 최대 간격(초)")]
        public float MaxInterval = 0.32f;

        [Header("소리")]
        [Range(0f, 1f)] public float Volume = 0.8f;
        [Tooltip("쏘는 쪽으로 치우치는 정도(0 = 가운데, 1 = 완전히 한쪽)")]
        [Range(0f, 1f)] public float PanAmount = 0.7f;
        public float MinPitch = 0.92f;
        public float MaxPitch = 1.1f;

        private Vector3[] _from = new Vector3[MAX_PENDING];
        private float[] _at = new float[MAX_PENDING];
        private float[] _near = new float[MAX_PENDING];
        private int _count;
        private float _next;
        private int _source;

        /// 이 기체가 지금 내가 조종하는 기체인가.
        public bool IsLocalTarget(SaccEntity entity)
        {
            return entity != null && Contacts != null && Contacts.LocalVehicle == entity;
        }

        /// muzzle: 쏜 자리, delay: 탄이 날아오는 시간(초), miss: 탄이 내 기체에서 벗어나는 거리(m).
        public void ReportFire(Vector3 muzzle, float delay, float miss)
        {
            float now = Time.time;
            if (now < _next || miss > MissRadius || _count >= MAX_PENDING) { return; }
            if (Clips == null || Clips.Length == 0 || Sources == null || Sources.Length == 0) { return; }
            _next = now + Random.Range(MinInterval, MaxInterval);
            _from[_count] = muzzle;
            _at[_count] = now + delay;
            _near[_count] = 1f - miss / Mathf.Max(MissRadius, 1f);
            _count++;
        }

        void Update()
        {
            if (_count == 0) { return; }
            // 탄이 오는 사이 격추됐거나 내렸으면 소리를 버린다
            if (Contacts == null || Contacts.LocalVehicle == null)
            {
                _count = 0;
                return;
            }
            float now = Time.time;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (now < _at[i]) { continue; }
                Play(_from[i], _near[i]);
                _count--;
                _from[i] = _from[_count];
                _at[i] = _at[_count];
                _near[i] = _near[_count];
            }
        }

        private void Play(Vector3 muzzle, float near)
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }
            VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 to = (muzzle - head.position).normalized;
            AudioSource s = Sources[_source];
            _source = (_source + 1) % Sources.Length;
            AudioClip clip = Clips[Random.Range(0, Clips.Length)];
            if (s == null || clip == null) { return; }
            s.panStereo = Vector3.Dot(head.rotation * Vector3.right, to) * PanAmount;
            s.pitch = Random.Range(MinPitch, MaxPitch);
            s.PlayOneShot(clip, Volume * Mathf.Lerp(0.55f, 1f, near));
        }
    }
}
