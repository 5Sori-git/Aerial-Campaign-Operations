using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 뭉게구름 배치 + 구름 속 안개. 동기화 없음 — 육각 지형 시드(동기화됨)로 모든 클라이언트가 같은 자리에 놓는다.
    /// 구름은 미리 만들어 둔 풀(ACO_SkyBuilder가 만든 자식들, 셰이더 ACO/CloudPuffs)을 켜고 끄며 옮긴다 — 생성 중 끊김이 없다.
    ///  - 지형 시드가 바뀌면(새 지형) 다시 놓는다. 지형이 없으면 FallbackSeed.
    ///  - Coverage(0~1) = 풀 중 켜는 비율. 고도 MinAltitude~MaxAltitude, 지형 반경 안(FieldRadius) 원판에 고르게.
    ///  - 구름 속: 내 머리가 구름 덩어리의 속 타원체(CloudCoreCenters/Extents, 빌더가 구움) 가까이 들어가면 안개를 하얗게 짙게 한다
    ///    (셰이더가 가까운 퍼프를 지우므로 안은 안개로 채운다). 나오면 원래 안개(기본 안개)로 돌린다.
    ///  - 날씨가 쓸 API: SetCoverage, SetDarkness(어두운 구름), SetBaseFog(구름 밖 안개).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_CloudField : UdonSharpBehaviour
    {
        [Header("배치")]
        [Tooltip("이 지형의 시드로 배치한다(Bake가 넣는다). 비우면 FallbackSeed")]
        public ACO_HexTerrain Terrain;
        public int FallbackSeed = 1234;
        [Range(0f, 1f)] public float Coverage = 0.5f;
        [Tooltip("구름을 놓을 원판 반지름(m, 월드 중심 기준)")]
        public float FieldRadius = 18000f;
        public float MinAltitude = 1300f;
        public float MaxAltitude = 2600f;
        public float MinScale = 0.75f;
        public float MaxScale = 1.4f;

        [Header("구름 풀 (ACO_SkyBuilder가 채운다)")]
        public Transform[] Clouds;
        [Tooltip("구름마다 속 타원체 중심(로컬)")]
        public Vector3[] CloudCoreCenters;
        [Tooltip("구름마다 속 타원체 반지름(로컬)")]
        public Vector3[] CloudCoreExtents;

        [Header("구름 속 안개 (씬 안개가 켜져 있어야 한다 — Exponential Squared 기준 값)")]
        public bool InsideFog = true;
        public float InsideFogDensity = 0.0035f;
        public Color InsideFogColor = new Color(0.86f, 0.89f, 0.93f, 1f);
        [Tooltip("속 타원체 크기 대비 이 배율 밖에서부터 안개가 짙어지기 시작한다")]
        public float FogOuter = 1.25f;
        [Tooltip("이 배율 안이면 안개가 가장 짙다")]
        public float FogInner = 0.7f;
        [Tooltip("안개가 바뀌는 빠르기(초당)")]
        public float FogSpeed = 2.5f;
        public float CheckInterval = 0.1f;

        private int _placedSeed = int.MinValue;
        private float _placedCoverage = -1f;
        private float _seedTimer;
        private float _checkTimer;
        private float _inside;
        private float _insideTarget;
        private bool _fogTouched;
        private float _baseDensity;
        private Color _baseColor;
        private Material _material;

        void Start()
        {
            _baseDensity = RenderSettings.fogDensity;
            _baseColor = RenderSettings.fogColor;
            // 구름 머티리얼을 한 벌 복사해 모든 구름이 같이 쓴다(날씨로 어둡게 할 때 원본 에셋을 건드리지 않게)
            if (Clouds != null && Clouds.Length > 0 && Clouds[0])
            {
                Renderer r0 = Clouds[0].GetComponent<Renderer>();
                if (r0)
                {
                    _material = r0.material;
                    for (int i = 1; i < Clouds.Length; i++)
                    {
                        if (!Clouds[i]) { continue; }
                        Renderer r = Clouds[i].GetComponent<Renderer>();
                        if (r) { r.sharedMaterial = _material; }
                    }
                }
            }
            CheckSeed();
        }

        void Update()
        {
            _seedTimer -= Time.deltaTime;
            if (_seedTimer <= 0f)
            {
                _seedTimer = 1f;
                CheckSeed();
            }

            if (!InsideFog) { return; }
            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = CheckInterval;
                _insideTarget = InsideAmount();
            }
            if (_inside == _insideTarget && !_fogTouched) { return; }
            _inside = Mathf.MoveTowards(_inside, _insideTarget, Time.deltaTime * FogSpeed);
            if (_inside > 0f)
            {
                float t = _inside * _inside * (3f - 2f * _inside);
                RenderSettings.fogDensity = Mathf.Lerp(_baseDensity, InsideFogDensity, t);
                RenderSettings.fogColor = Color.Lerp(_baseColor, InsideFogColor, t);
                _fogTouched = true;
            }
            else if (_fogTouched)
            {
                RenderSettings.fogDensity = _baseDensity;
                RenderSettings.fogColor = _baseColor;
                _fogTouched = false;
            }
        }

        private void CheckSeed()
        {
            int seed = Terrain ? Terrain.GetSeed() : 0;
            if (seed == 0) { seed = FallbackSeed; }
            if (seed != _placedSeed || Coverage != _placedCoverage) { Place(seed); }
        }

        /// 시드로 구름을 놓는다(모든 클라이언트가 같은 결과).
        public void Place(int seed)
        {
            _placedSeed = seed;
            _placedCoverage = Coverage;
            if (Clouds == null) { return; }
            Random.InitState(seed * 7919 + 104729);
            int count = Mathf.RoundToInt(Mathf.Clamp01(Coverage) * Clouds.Length);
            for (int i = 0; i < Clouds.Length; i++)
            {
                Transform c = Clouds[i];
                if (!c) { continue; }
                // 켜지 않는 구름도 같은 수의 난수를 써서 Coverage가 달라도 앞쪽 구름 자리는 그대로
                float ang = Random.value * Mathf.PI * 2f;
                float rad = Mathf.Sqrt(Random.value) * FieldRadius;
                float alt = Mathf.Lerp(MinAltitude, MaxAltitude, Random.value);
                float yaw = Random.value * 360f;
                float s = Mathf.Lerp(MinScale, MaxScale, Random.value);
                float sx = s * Mathf.Lerp(0.85f, 1.2f, Random.value);
                float sy = s * Mathf.Lerp(0.8f, 1.1f, Random.value);
                bool on = i < count;
                if (on)
                {
                    c.position = new Vector3(Mathf.Cos(ang) * rad, alt, Mathf.Sin(ang) * rad);
                    c.rotation = Quaternion.Euler(0f, yaw, 0f);
                    c.localScale = new Vector3(sx, sy, s);
                }
                if (c.gameObject.activeSelf != on) { c.gameObject.SetActive(on); }
            }
            Random.InitState(Networking.GetServerTimeInMilliseconds() ^ 0x5bd1e995);
        }

        /// 내 머리가 구름 속에 얼마나 들어갔나(0~1).
        private float InsideAmount()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || Clouds == null || CloudCoreCenters == null || CloudCoreExtents == null) { return 0f; }
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            float best = 0f;
            int n = Mathf.Min(Clouds.Length, Mathf.Min(CloudCoreCenters.Length, CloudCoreExtents.Length));
            float span = Mathf.Max(FogOuter - FogInner, 0.01f);
            for (int i = 0; i < n; i++)
            {
                Transform c = Clouds[i];
                if (!c || !c.gameObject.activeSelf) { continue; }
                Vector3 p = c.InverseTransformPoint(head) - CloudCoreCenters[i];
                Vector3 e = CloudCoreExtents[i];
                if (e.x <= 0f || e.y <= 0f || e.z <= 0f) { continue; }
                float len = new Vector3(p.x / e.x, p.y / e.y, p.z / e.z).magnitude;
                if (len >= FogOuter) { continue; }
                float t = Mathf.Clamp01((FogOuter - len) / span);
                if (t > best) { best = t; }
            }
            return best;
        }

        // ───────────── 날씨가 쓸 API ─────────────

        public void SetCoverage(float coverage)
        {
            Coverage = Mathf.Clamp01(coverage);
            CheckSeed();
        }

        /// 0 = 흰 구름, 1 = 폭풍 구름.
        public void SetDarkness(float darkness)
        {
            if (_material) { _material.SetFloat("_Darken", Mathf.Clamp01(darkness)); }
        }

        /// 구름 밖 안개(날씨). 구름 속이 아니면 바로 적용한다.
        public void SetBaseFog(float density, Color color)
        {
            _baseDensity = density;
            _baseColor = color;
            if (!_fogTouched)
            {
                RenderSettings.fogDensity = density;
                RenderSettings.fogColor = color;
            }
        }
    }
}
