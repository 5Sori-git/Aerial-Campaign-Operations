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
    /// 레이저 기총(ACO_NPCGun + ACO_NPCLaser)은 총알 소리 대신 전자음(LaserClips — Tools/aco_make_laser_zap.py)을 ReportLaser로 낸다(빛이라 늦지 않는다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_BulletFlyby : UdonSharpBehaviour
    {
        private const int MAX_PENDING = 8;

        [Header("참조 (Bake가 채운다)")]
        public ACO_RadarContacts Contacts;
        [Tooltip("동시에 겹쳐 나는 소리 — 2D 오디오 소스")]
        public AudioSource[] Sources;
        public AudioClip[] Clips;
        [Tooltip("레이저가 스칠 때의 전자음(레이저 기총이 ReportLaser로 부른다)")]
        public AudioClip[] LaserClips;

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

        [Header("레일건 탄 (6-156)")]
        [Tooltip("레일건 탄이 스칠 때(Audio/Generated/ACO_Rail_Flyby — 파일이 이미 작게 만들어져 있다). 레일건·루나·Lance가 쏠 때 ReportRail")]
        public AudioClip RailClip;
        [Tooltip("탄 궤적이 내 기체에서 (맞는 거리 + 이 거리) 안으로 지나가면 소리")]
        public float RailRadius = 80f;
        [Tooltip("레일건 스침 소리 사이 최소 간격(초)")]
        public float RailMinInterval = 0.15f;
        [Range(0f, 1f)] public float RailVolume = 1f;

        private Vector3[] _from = new Vector3[MAX_PENDING];
        private float[] _at = new float[MAX_PENDING];
        private float[] _near = new float[MAX_PENDING];
        private int[] _kind = new int[MAX_PENDING];   // 0 총탄, 1 레이저, 2 레일건
        private float _nextRail;
        private int _count;
        private float _next;
        private int _source;

        /// 이 기체가 지금 내가 조종하는 기체인가.
        public bool IsLocalTarget(SaccEntity entity)
        {
            return entity != null && Contacts != null && Contacts.LocalVehicle == entity;
        }

        /// muzzle: 쏜 자리, delay: 탄이 날아오는 시간(초), miss: 탄이 내 기체에서 벗어나는 거리(m).
        public void ReportFire(Vector3 muzzle, float delay, float miss) { Queue(muzzle, delay, miss, 0); }

        /// 레이저가 내 기체 곁을 스친다(빛이라 바로) — 전자음. miss: 빔 축이 내 기체에서 벗어나는 거리(m).
        public void ReportLaser(Vector3 muzzle, float miss) { Queue(muzzle, 0f, miss, 1); }

        /// 레일건 탄 한 발(모든 화면이 각자 — 쏘는 쪽이 부른다). from·dir: 궤적, length: 날아가는 거리(그 끝에서 터지거나 사라진다),
        /// speed: 탄속(0 이하 = 빛처럼 바로), hitRadius: 이 안이면 맞는다(소리 대신 맞는 소리).
        /// 쏘는 순간 내 기체의 속도로 탄이 닿을 때의 자리를 미리 셈해, 궤적이 그 자리 곁(hitRadius ~ hitRadius + RailRadius)을 지나면
        /// 가장 가까워지는 순간에 한 번 소리를 낸다. 궤적 끝(앞에서 터짐)이나 뒤쪽이면 내지 않는다.
        public void ReportRail(Vector3 from, Vector3 dir, float length, float speed, float hitRadius)
        {
            if (RailClip == null || Contacts == null || Contacts.LocalVehicle == null || Sources == null || Sources.Length == 0) { return; }
            float now = Time.time;
            if (now < _nextRail || _count >= MAX_PENDING) { return; }
            SaccEntity e = Contacts.LocalVehicle;
            Vector3 p = e.transform.position;
            Vector3 v = e.VehicleRigidbody != null ? e.VehicleRigidbody.velocity : Vector3.zero;
            float along = Vector3.Dot(p - from, dir);
            float t = 0f;
            for (int i = 0; i < 2; i++)
            {
                t = speed > 0f ? Mathf.Max(along, 0f) / speed : 0f;
                along = Vector3.Dot(p + v * t - from, dir);
            }
            if (along <= 0f || along >= length) { return; }
            Vector3 point = from + dir * along;
            float miss = Vector3.Distance(p + v * t, point);
            if (miss <= hitRadius || miss > hitRadius + RailRadius) { return; }
            _nextRail = now + RailMinInterval;
            _from[_count] = point;
            _at[_count] = now + (speed > 0f ? along / speed : 0f);
            _near[_count] = 1f - (miss - hitRadius) / Mathf.Max(RailRadius, 1f);
            _kind[_count] = 2;
            _count++;
        }

        private void Queue(Vector3 muzzle, float delay, float miss, int kind)
        {
            float now = Time.time;
            if (now < _next || miss > MissRadius || _count >= MAX_PENDING) { return; }
            AudioClip[] set = kind == 1 ? LaserClips : Clips;
            if (set == null || set.Length == 0 || Sources == null || Sources.Length == 0) { return; }
            _next = now + Random.Range(MinInterval, MaxInterval);
            _from[_count] = muzzle;
            _at[_count] = now + delay;
            _near[_count] = 1f - miss / Mathf.Max(MissRadius, 1f);
            _kind[_count] = kind;
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
                Play(_from[i], _near[i], _kind[i]);
                _count--;
                _from[i] = _from[_count];
                _at[i] = _at[_count];
                _near[i] = _near[_count];
                _kind[i] = _kind[_count];
            }
        }

        private void Play(Vector3 muzzle, float near, int kind)
        {
            AudioClip clip;
            if (kind == 2) { clip = RailClip; }
            else
            {
                AudioClip[] set = kind == 1 ? LaserClips : Clips;
                if (set == null || set.Length == 0) { return; }
                clip = set[Random.Range(0, set.Length)];
            }
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) { return; }
            VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 to = (muzzle - head.position).normalized;
            AudioSource s = Sources[_source];
            _source = (_source + 1) % Sources.Length;
            if (s == null || clip == null) { return; }
            s.panStereo = Vector3.Dot(head.rotation * Vector3.right, to) * PanAmount;
            s.pitch = Random.Range(MinPitch, MaxPitch);
            float vol = kind == 2 ? RailVolume : Volume;
            s.PlayOneShot(clip, vol * Mathf.Lerp(0.55f, 1f, near));
        }
    }
}
