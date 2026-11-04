using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 번개(폭풍 날씨, 로컬 연출 — 동기화 없음). 날씨가 SetStorm으로 켜고 끈다.
    ///  - 일정: 서버 시간을 SlotMs(1.5초) 칸으로 나눠 칸마다 정수 해시로 '칠지(StrikeChance)·칸 안 몇 ms에·지도 어디에(FieldRadius 원판)'를 정한다 —
    ///    모든 클라이언트가 같은 때 같은 자리에 번개를 본다(네트워크 없음). 지도 전체에 1.5초마다 떨어지므로 내 둘레 몇 km 안에도 자주 친다.
    ///  - 섬광: 조명을 새로 켜지 않는다(포워드 렌더링에선 조명 하나가 모든 물체를 한 번 더 그린다). 이미 있는 값만 0.5초 동안 2~3번 깜빡이게 —
    ///    태양 세기(× 1 + SunFlashBoost), 구름층 판·뭉게구름(ACO_CloudField.SetFlash — 번개 자리 둘레가 안에서 빛나듯), 안개 색. 세기는 거리에 따라(FullFlashRange → FlashRange).
    ///  - 낙뢰 줄기(Bolt, 셰이더 ACO/Lightning — 엇갈린 리본 두 장의 지그재그 메시, 높이 1 = 땅~구름): 번개 자리 땅(레이캐스트)에서 비구름층 아랫면까지 늘려 0.5초 켠다.
    ///  - 천둥: 거리 / 343m/s 뒤에(ThunderRange 안만) 가까우면(NearThunderDistance) 날카로운 천둥, 멀면 낮은 우르릉. 세기는 거리로, 좌우는 번개 방향으로(2D 소리, 소스 여러 개를 돌려 쓴다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_Lightning : UdonSharpBehaviour
    {
        [Header("연결 (ACO_SkyBuilder·Bake)")]
        public ACO_CloudField Clouds;
        [Tooltip("태양(날씨와 같은 디렉셔널 라이트) — 섬광 때 잠깐 밝힌다")]
        public Light Sun;
        [Tooltip("낙뢰 줄기(ACO/Lightning, 메시 높이 0~1 = 땅~구름)")]
        public Renderer Bolt;
        [Tooltip("천둥 소리 소스(2D) — 겹쳐 들리게 여러 개를 돌려 쓴다")]
        public AudioSource[] ThunderSources;
        public AudioClip[] NearThunder;
        public AudioClip[] FarThunder;
        public LayerMask GroundLayers = 2049;
        public float SeaLevel = 0f;

        [Header("번개")]
        [Tooltip("일정 칸(ms) — 칸마다 한 번까지 친다")]
        public int SlotMs = 1500;
        [Range(0f, 1f)] public float StrikeChance = 0.8f;
        [Tooltip("번개가 떨어지는 원판 반지름(m, 월드 중심)")]
        public float FieldRadius = 18000f;
        [Tooltip("섬광: 이 거리(m) 안이면 가장 밝게, FlashRange에서 0")]
        public float FullFlashRange = 3000f;
        public float FlashRange = 15000f;
        [Tooltip("섬광 때 태양 세기 배율 = 1 + 이 값 × 섬광 세기")]
        public float SunFlashBoost = 2.5f;
        [Tooltip("비구름층 윗면(DeckTop) 위로 이만큼(m) 올라가며 태양 섬광·낙뢰 줄기가 사라진다(번개는 구름 아래)")]
        public float AboveDeckFade = 300f;
        [Tooltip("비구름층 위에서 구름 안 섬광을 이만큼 줄인다(0~1 — 0이면 그대로)")]
        [Range(0f, 1f)] public float AboveDeckDim = 0.4f;
        [Tooltip("낙뢰 줄기를 보여 주는 거리(m)")]
        public float BoltRange = 20000f;
        [Tooltip("천둥이 들리는 거리(m)·가까운 천둥 거리(m)·볼륨")]
        public float ThunderRange = 9000f;
        public float NearThunderDistance = 2000f;
        [Range(0f, 1f)] public float ThunderVolume = 1f;
        [Tooltip("폭풍이 켜지고 꺼지는 시간(초) — 이 절반 넘게 켜져야 번개가 친다")]
        public float FadeTime = 5f;

        private const float FLASH_TIME = 0.5f;
        private const float SOUND_SPEED = 343f;
        private const int MAX_PENDING = 6;

        private float _storm;
        private float _stormTarget;
        private int _nextSlot;
        private bool _slotStarted;

        // 섬광
        private bool _flashing;
        private float _flashStart;
        private float _flashAmp;
        private float _flashAbove;
        private Vector3 _flashPos;
        private float _sunBase;
        private float _pulse2;
        private float _pulse3;
        private Material _boltMat;

        // 천둥 대기열(번개 → 소리가 닿는 시각)
        private float[] _pendTime = new float[MAX_PENDING];
        private bool[] _pendNear = new bool[MAX_PENDING];
        private float[] _pendVol = new float[MAX_PENDING];
        private float[] _pendPan = new float[MAX_PENDING];
        private bool[] _pendUsed = new bool[MAX_PENDING];
        private int _pendCount;
        private int _nextSource;

        void Start()
        {
            if (Bolt)
            {
                _boltMat = Bolt.material;
                Bolt.gameObject.SetActive(false);
            }
        }

        /// 날씨가 부른다: 1 = 폭풍(번개), 0 = 없음. instant면 바로.
        public void SetStorm(float amount, bool instant)
        {
            _stormTarget = Mathf.Clamp01(amount);
            if (instant) { _storm = _stormTarget; }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _storm = Mathf.MoveTowards(_storm, _stormTarget, dt / Mathf.Max(FadeTime, 0.01f));
            if (_flashing) { UpdateFlash(); }
            if (_pendCount > 0) { UpdateThunder(); }
            if (_storm <= 0f) { _slotStarted = false; return; }

            int now = Networking.GetServerTimeInMilliseconds();
            int slotMs = Mathf.Max(SlotMs, 100);
            int slot = now >= 0 ? now / slotMs : (now - slotMs + 1) / slotMs;
            if (!_slotStarted || slot - _nextSlot > 4 || _nextSlot - slot > 1) { _nextSlot = slot; _slotStarted = true; }
            // 지난 칸부터 이번 칸까지 — 칠 시각이 지난 것만
            while (_nextSlot <= slot)
            {
                int s = _nextSlot;
                bool strike = Hash01(s, 1) < StrikeChance;
                int at = s * slotMs + Mathf.FloorToInt(Hash01(s, 2) * slotMs);
                if (strike && at > now) { break; }   // 이번 칸 번개는 아직
                _nextSlot++;
                if (!strike || _storm < 0.5f) { continue; }
                float ang = Hash01(s, 3) * Mathf.PI * 2f;
                float rad = Mathf.Sqrt(Hash01(s, 4)) * FieldRadius;
                Strike(new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad), s);
            }
        }

        /// 정수 해시 0~1(칸 번호 x, 용도 k) — 모든 클라이언트에서 같다.
        private float Hash01(int x, int k)
        {
            int h = x * 73856093 ^ (k + 1) * 19349663;
            h = (h ^ (h >> 16)) * 0x45d9f3b;
            h = (h ^ (h >> 16)) * 0x45d9f3b;
            h = h ^ (h >> 16);
            return (h & 0xFFFFFF) / 16777216f;
        }

        private void Strike(Vector3 pos, int slot)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { return; }
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 d = pos - head.position;
            d.y = 0f;
            float dist = d.magnitude;

            // 섬광: 거리로 세기, 깜빡임 모양은 번개마다 조금 다르게
            float amp = 1f - Mathf.Clamp01((dist - FullFlashRange) / Mathf.Max(FlashRange - FullFlashRange, 1f));
            // 비구름층 위(DeckTop + AboveDeckFade까지 서서히): 번개는 구름 아래라 태양 섬광·낙뢰 줄기는 없고, 발밑 구름이 안에서 빛나는 것만 보인다
            float above = Clouds != null ? Mathf.Clamp01((head.position.y - Clouds.DeckTop) / Mathf.Max(AboveDeckFade, 1f)) : 0f;
            bool bolt = Bolt != null && dist < BoltRange && above < 0.5f;
            if (amp > 0.01f || bolt)
            {
                if (!_flashing && Sun) { _sunBase = Sun.intensity; }
                _flashing = true;
                _flashStart = Time.time;
                _flashAmp = amp;
                _flashAbove = above;
                _flashPos = pos;
                _pulse2 = 0.4f + 0.5f * Hash01(slot, 5);
                _pulse3 = Hash01(slot, 6) < 0.6f ? 0.5f + 0.4f * Hash01(slot, 7) : 0f;
            }
            if (bolt) { PlaceBolt(pos, slot); }

            // 천둥: 소리가 닿는 시각에
            if (dist < ThunderRange && ThunderSources != null && ThunderSources.Length > 0)
            {
                float vol = ThunderVolume * Mathf.Pow(1f - dist / ThunderRange, 0.7f);
                Vector3 right = head.rotation * Vector3.right;
                float pan = dist > 1f ? Mathf.Clamp(Vector3.Dot(d / dist, right) * 0.7f, -0.7f, 0.7f) : 0f;
                Queue(Time.time + dist / SOUND_SPEED, dist < NearThunderDistance, vol, pan);
            }
        }

        /// 낙뢰 줄기: 번개 자리 땅(없으면 해수면)에서 비구름층 아랫면까지, 방향·폭은 칸마다 다르게.
        private void PlaceBolt(Vector3 pos, int slot)
        {
            float ground = SeaLevel;
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(pos.x, 5000f, pos.z), Vector3.down, out hit, 10000f, GroundLayers, QueryTriggerInteraction.Ignore))
            {
                ground = Mathf.Max(ground, hit.point.y);
            }
            float top = Clouds != null ? Clouds.DeckBottom : ground + 1300f;
            float height = Mathf.Max(top - ground, 100f);
            float width = 0.8f + 0.5f * Hash01(slot, 8);
            Transform t = Bolt.transform;
            t.SetPositionAndRotation(new Vector3(pos.x, ground, pos.z), Quaternion.Euler(0f, Hash01(slot, 9) * 360f, 0f));
            t.localScale = new Vector3(width, height, width);
            if (!Bolt.gameObject.activeSelf) { Bolt.gameObject.SetActive(true); }
        }

        /// 섬광 모양: 0초·0.11초·0.24초에 짧은 펄스 셋(둘째·셋째 세기는 번개마다), 0.5초에 끝.
        private float FlashCurve(float t)
        {
            float a = Mathf.Exp(-(t * t) / (0.035f * 0.035f));
            float b = _pulse2 * Mathf.Exp(-((t - 0.11f) * (t - 0.11f)) / (0.03f * 0.03f));
            float c = _pulse3 * Mathf.Exp(-((t - 0.24f) * (t - 0.24f)) / (0.05f * 0.05f));
            return Mathf.Max(a, Mathf.Max(b, c));
        }

        private void UpdateFlash()
        {
            float t = Time.time - _flashStart;
            if (t >= FLASH_TIME)
            {
                _flashing = false;
                if (Sun) { Sun.intensity = _sunBase; }
                if (Clouds) { Clouds.SetFlash(0f, _flashPos); }
                if (Bolt && Bolt.gameObject.activeSelf) { Bolt.gameObject.SetActive(false); }
                return;
            }
            float f = FlashCurve(t);
            float k = f * _flashAmp;
            if (Sun) { Sun.intensity = _sunBase * (1f + SunFlashBoost * k * (1f - _flashAbove)); }
            if (Clouds) { Clouds.SetFlash(k * (1f - AboveDeckDim * _flashAbove), _flashPos); }
            if (_boltMat) { _boltMat.SetFloat("_Intensity", f * 2f); }
        }

        private void Queue(float time, bool near, float vol, float pan)
        {
            for (int i = 0; i < MAX_PENDING; i++)
            {
                if (_pendUsed[i]) { continue; }
                _pendUsed[i] = true;
                _pendTime[i] = time;
                _pendNear[i] = near;
                _pendVol[i] = vol;
                _pendPan[i] = pan;
                _pendCount++;
                return;
            }
        }

        private void UpdateThunder()
        {
            for (int i = 0; i < MAX_PENDING; i++)
            {
                if (!_pendUsed[i] || Time.time < _pendTime[i]) { continue; }
                _pendUsed[i] = false;
                _pendCount--;
                AudioClip[] clips = _pendNear[i] ? NearThunder : FarThunder;
                if (clips == null || clips.Length == 0) { continue; }
                AudioClip clip = clips[Random.Range(0, clips.Length)];
                AudioSource src = ThunderSources[_nextSource % ThunderSources.Length];
                _nextSource++;
                if (!src || !clip) { continue; }
                src.panStereo = _pendPan[i];
                src.PlayOneShot(clip, _pendVol[i]);
            }
        }
    }
}
