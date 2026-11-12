using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 벽걸이로도 쓴다(WallMode, 브리핑실 평면 지도): 내가 가까이 있을 때만 갱신하고 내 기체 표시가 없다.
    /// 조종석 지도(기체마다 하나, 로컬 전용, 동기화 없음). 기체 안의 화면에 작전 공역 전체를 북쪽이 위인 고정 방위로 그린다 —
    /// 육각 지형(높이대 색, 바다는 바탕색), 공역 테두리, 나(흰 삼각형), 적·목표·아군(접촉 목록 ACO_RadarContacts).
    /// - 내가 이 기체에 타고 있을 때만 갱신한다(다른 기체의 화면은 꺼 둔다 — Screen). 지형 메시는 처음 탔을 때·지형이 바뀌었을 때 프레임마다 나눠 만든다.
    /// - 화면 좌표: Screen의 로컬 XY, −0.5~0.5 정사각형(+X = 동, +Y = 북). 크기는 Screen 스케일로 정한다.
    /// - 머티리얼은 ACO/RadarVertexColor(깊이 검사 — 조종석에 가려진다). 바탕 → 지형 → 점 순서는 렌더 큐로 정한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_CockpitMap : UdonSharpBehaviour
    {
        public const int MAX_BLIPS = 320;
        private const int ECM_RING_SEGMENTS = 40;
        [Tooltip("ECM 재밍 구역 원의 색")]
        public Color EcmColor = new Color(0.3f, 1f, 0.4f, 1f);
        [Tooltip("ECM 재밍 구역 원의 굵기(지도 폭 대비)")]
        public float EcmRingWidth = 0.008f;
        private const int MAX_VERTICES = 65000;

        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 레이더 접촉 목록을 넣는다")]
        public ACO_RadarContacts Contacts;
        [Tooltip("이 화면이 달린 기체(비면 부모에서 찾는다)")]
        public SaccEntity EntityControl;
        [Tooltip("켜고 끄는 화면 묶음(지형·점·테두리). 내가 이 기체에 타고 있을 때만 켠다")]
        public GameObject Screen;
        [Tooltip("지형 메시(ACO/RadarVertexColor)")]
        public MeshFilter TerrainFilter;
        [Tooltip("점 메시(ACO/RadarVertexColor). 스크립트가 메시를 만든다")]
        public MeshFilter BlipFilter;
        [Tooltip("공역 테두리 고리(지름 1 = 공역 지름). 비워도 된다")]
        public Transform Border;

        [Header("벽걸이")]
        [Tooltip("켜면 기체 없이 벽에 건 지도로 동작한다. 나 표시를 그리지 않는다")]
        public bool WallMode;
        [Tooltip("벽걸이일 때, 이 거리(m) 안에 내가 있을 때만 갱신한다")]
        public float WallActiveRadius = 12f;

        [Header("표시")]
        [Tooltip("점을 다시 그리는 간격(초)")]
        public float UpdateInterval = 0.25f;
        [Tooltip("지형이 없을 때의 공역 반경(m)")]
        public float FallbackWorldRadius = 20000f;
        [Tooltip("공역 가장자리 여백(배율) — 테두리 밖 유닛도 조금 보이게")]
        public float Margin = 1.08f;
        [Tooltip("점 크기(화면 한 변 대비)")]
        public float BlipSize = 0.035f;
        [Tooltip("내 기체 표시 크기 배율")]
        public float SelfScale = 1.6f;
        [Tooltip("지형 만들 때 프레임당 칸 수")]
        public int CellsPerFrame = 200;

        [Header("색")]
        public Color[] HeightColors = new Color[] {
            new Color(0.62f, 0.58f, 0.40f), new Color(0.24f, 0.42f, 0.22f), new Color(0.40f, 0.37f, 0.34f), new Color(0.85f, 0.87f, 0.9f) };
        public Color EnemyColor = new Color(1f, 0.82f, 0.2f, 1f);      // 적 노랑(HUD 레이더와 같다)
        public Color ObjectiveColor = new Color(1f, 0.25f, 0.2f, 1f);  // 목표 빨강
        public Color FriendlyColor = new Color(0.35f, 0.65f, 1f, 1f);
        public Color SelfColor = new Color(1f, 1f, 1f, 1f);

        private ACO_HexTerrain _terrain;
        private Vector3 _center;
        private float _k;   // 월드 m → 화면
        private int _mapSeed;
        private bool _started;
        private bool _active;
        private float _timer;

        private Mesh _blipMesh;
        private Vector3[] _v;
        private Color[] _c;
        private int _n;

        // 지형 메시(프레임마다 나눠서)
        private bool _building;
        private int _cellN;
        private float[] _cellXs;
        private float[] _cellZs;
        private float[] _cellHs;
        private float _seaLevel;
        private int _landCap;
        private Vector3[] _meshV;
        private Color[] _meshC;
        private int[] _meshT;
        private float[] _cornerX;
        private float[] _cornerZ;
        private int _buildCell;
        private int _buildPlaced;

        void Start()
        {
            if (_started) { return; }
            _started = true;
            if (!EntityControl && !WallMode) { EntityControl = GetComponentInParent<SaccEntity>(); }
            _v = new Vector3[MAX_BLIPS * 4];
            _c = new Color[MAX_BLIPS * 4];
            int[] tris = new int[MAX_BLIPS * 6];
            for (int b = 0; b < MAX_BLIPS; b++)
            {
                int v = b * 4;
                int t = b * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            _blipMesh = new Mesh();
            _blipMesh.vertices = _v;
            _blipMesh.colors = _c;
            _blipMesh.triangles = tris;
            _blipMesh.RecalculateBounds();
            if (BlipFilter) { BlipFilter.sharedMesh = _blipMesh; }
            SetActive(false);
        }

        void Update()
        {
            if (_building) { BuildStep(); }
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = UpdateInterval;
            Start();

            bool mine = WallMode ? PlayerNearby() : Contacts != null && EntityControl != null && Contacts.LocalVehicle == EntityControl;
            if (mine != _active) { SetActive(mine); }
            if (!mine) { return; }

            ResolveFrame();
            if (_terrain != null)
            {
                int seed = _terrain.GetSeed();
                if (seed != _mapSeed && _terrain.IsBuilt(seed)) { BeginTerrainMesh(seed); }
            }
            Rebuild();
        }

        /// 벽걸이: 내가 근처에 있으면 켠다. 기체에 타지 않아도 접촉 목록이 돌게 깨운다.
        private bool PlayerNearby()
        {
            VRCPlayerApi player = Networking.LocalPlayer;
            if (Contacts == null || !Utilities.IsValid(player)) { return false; }
            if ((player.GetPosition() - transform.position).sqrMagnitude > WallActiveRadius * WallActiveRadius) { return false; }
            Contacts.Wake(UpdateInterval * 4f);
            return true;
        }

        private void SetActive(bool on)
        {
            _active = on;
            if (Screen != null && Screen.activeSelf != on) { Screen.SetActive(on); }
        }

        /// 지도 중심·축척: 육각 지형이 있으면 그 중심·반경, 없으면 생성기 위치·FallbackWorldRadius.
        private void ResolveFrame()
        {
            ACO_MissionGenerator gen = Contacts.Generator;
            if (_terrain == null && gen != null) { _terrain = gen.HexTerrain; }
            float worldRadius = _terrain != null ? _terrain.FieldRadius : FallbackWorldRadius;
            _center = _terrain != null ? _terrain.transform.position : (gen != null ? gen.transform.position : Vector3.zero);
            _k = 0.5f / Mathf.Max(worldRadius * Margin, 1f);
            if (Border != null) { Border.localScale = Vector3.one * (worldRadius * 2f * _k); }
        }

        // ───────────── 점 ─────────────

        private void Rebuild()
        {
            _n = 0;
            float s = BlipSize * 0.5f;
            ACO_RadarContacts c = Contacts;
            if (c.Targets != null)
            {
                for (int i = 0; i < c.Count && _n < MAX_BLIPS - 1; i++)
                {
                    if (!c.Visible[i]) { continue; }
                    Transform t = c.Targets[i];
                    if (t == null) { continue; }
                    Vector3 p = ToMap(t.position);
                    if (Mathf.Abs(p.x) > 0.5f || Mathf.Abs(p.y) > 0.5f) { continue; }
                    int kind = c.Kinds[i];
                    Color col = KindColor(kind);
                    bool air = kind == ACO_RadarContacts.KIND_ENEMY_AIR || kind == ACO_RadarContacts.KIND_FRIENDLY_AIR || kind == ACO_RadarContacts.KIND_OBJECTIVE_AIR;
                    if (air)
                    {
                        Vector3 f = t.forward;
                        Tri(p, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, s, col);
                    }
                    else { Square(p, s * 0.75f, col); }
                }
            }
            EcmRings(c);
            // 나: 맨 위에 그리도록 마지막
            if (!WallMode)
            {
                Transform me = EntityControl.transform;
                Vector3 mf = me.forward;
                Tri(ToMap(me.position), Mathf.Atan2(mf.x, mf.z) * Mathf.Rad2Deg, s * SelfScale, SelfColor);
            }

            for (int v = _n * 4; v < _v.Length; v++) { _v[v] = Vector3.zero; }
            _blipMesh.vertices = _v;
            _blipMesh.colors = _c;
            _blipMesh.RecalculateBounds();
        }

        /// 켜진 ECM의 재밍 구역: 초록 테두리 원. 지도 밖의 호는 그리지 않는다.
        private void EcmRings(ACO_RadarContacts c)
        {
            if (!c.IsEcmOn() || c.EcmPods == null) { return; }
            int now = Networking.GetServerTimeInMilliseconds();
            float radius = c.EcmRange * _k;
            for (int i = 0; i < c.EcmPods.Length; i++)
            {
                ACO_DFUNC_Missile pod = c.EcmPods[i];
                if (pod == null || !pod.IsEcmActive(now)) { continue; }
                Vector3 o = ToMap(pod.transform.position);
                for (int s = 0; s < ECM_RING_SEGMENTS; s++)
                {
                    float a0 = s * Mathf.PI * 2f / ECM_RING_SEGMENTS;
                    float a1 = (s + 1) * Mathf.PI * 2f / ECM_RING_SEGMENTS;
                    Vector3 p0 = o + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * radius;
                    Vector3 p1 = o + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * radius;
                    if (Mathf.Abs(p0.x) > 0.5f || Mathf.Abs(p0.y) > 0.5f || Mathf.Abs(p1.x) > 0.5f || Mathf.Abs(p1.y) > 0.5f) { continue; }
                    if (_n >= MAX_BLIPS - 1) { return; }
                    Vector3 d = p1 - p0;
                    Vector3 r = new Vector3(d.y, -d.x, 0f).normalized * (EcmRingWidth * 0.5f);
                    int v = _n * 4;
                    _v[v] = p0 - r;
                    _v[v + 1] = p0 + r;
                    _v[v + 2] = p1 + r;
                    _v[v + 3] = p1 - r;
                    Fill(v, EcmColor);
                }
            }
        }

        private Vector3 ToMap(Vector3 world)
        {
            Vector3 d = world - _center;
            return new Vector3(Mathf.Clamp(d.x * _k, -0.6f, 0.6f), Mathf.Clamp(d.z * _k, -0.6f, 0.6f), 0f);
        }

        private Color KindColor(int kind)
        {
            if (kind == ACO_RadarContacts.KIND_FRIENDLY_AIR || kind == ACO_RadarContacts.KIND_FRIENDLY_GROUND) { return FriendlyColor; }
            if (kind == ACO_RadarContacts.KIND_OBJECTIVE_AIR || kind == ACO_RadarContacts.KIND_OBJECTIVE_GROUND) { return ObjectiveColor; }
            return EnemyColor;
        }

        private void Tri(Vector3 p, float bearingDeg, float s, Color col)
        {
            if (_n >= MAX_BLIPS) { return; }
            float a = bearingDeg * Mathf.Deg2Rad;
            Vector3 f = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
            Vector3 r = new Vector3(f.y, -f.x, 0f);
            int v = _n * 4;
            _v[v] = p + f * (s * 1.3f);
            _v[v + 1] = p - f * (s * 0.8f) + r * (s * 0.8f);
            _v[v + 2] = p - f * (s * 0.8f) - r * (s * 0.8f);
            _v[v + 3] = _v[v + 2];
            Fill(v, col);
        }

        private void Square(Vector3 p, float s, Color col)
        {
            if (_n >= MAX_BLIPS) { return; }
            int v = _n * 4;
            _v[v] = p + new Vector3(-s, s, 0f);
            _v[v + 1] = p + new Vector3(s, s, 0f);
            _v[v + 2] = p + new Vector3(s, -s, 0f);
            _v[v + 3] = p + new Vector3(-s, -s, 0f);
            Fill(v, col);
        }

        private void Fill(int v, Color col)
        {
            _c[v] = col; _c[v + 1] = col; _c[v + 2] = col; _c[v + 3] = col;
            _n++;
        }

        // ───────────── 지형 메시 (윗면 육각형만, 프레임마다 나눠 만든다) ─────────────

        private void BeginTerrainMesh(int seed)
        {
            _mapSeed = seed;
            if (!TerrainFilter) { return; }
            _cellN = _terrain.GetCellCount();
            _cellXs = _terrain.GetCellXs();
            _cellZs = _terrain.GetCellZs();
            _cellHs = _terrain.GetCellHeights();
            _seaLevel = _terrain.SeaLevel;
            int land = 0;
            for (int i = 0; i < _cellN; i++) { if (_cellHs[i] >= _seaLevel) { land++; } }
            _landCap = Mathf.Min(land, MAX_VERTICES / 6);
            _meshV = new Vector3[_landCap * 6];
            _meshC = new Color[_landCap * 6];
            _meshT = new int[_landCap * 12];
            // 뾰족한 꼭짓점이 ±Z(북·남)를 향하는 육각형(지형 격자와 같다). 이웃 칸과 틈이 없게 조금 키운다.
            float r = _terrain.HexRadius * _k * 1.02f;
            _cornerX = new float[6];
            _cornerZ = new float[6];
            for (int k = 0; k < 6; k++)
            {
                float a = (30f + 60f * k) * Mathf.Deg2Rad;
                _cornerX[k] = Mathf.Cos(a) * r;
                _cornerZ[k] = Mathf.Sin(a) * r;
            }
            _buildCell = 0;
            _buildPlaced = 0;
            _building = true;
        }

        private void BuildStep()
        {
            int end = Mathf.Min(_buildCell + Mathf.Max(CellsPerFrame, 1), _cellN);
            for (; _buildCell < end && _buildPlaced < _landCap; _buildCell++)
            {
                int i = _buildCell;
                if (_cellHs[i] < _seaLevel) { continue; }
                int vi = _buildPlaced * 6;
                int ti = _buildPlaced * 12;
                _buildPlaced++;
                // 칸 좌표는 지형 기준(중심 = 지형 위치)
                float px = _cellXs[i] * _k;
                float py = _cellZs[i] * _k;
                Color col = HeightColor(_cellHs[i]);
                for (int k = 0; k < 6; k++)
                {
                    _meshV[vi + k] = new Vector3(px + _cornerX[k], py + _cornerZ[k], 0f);
                    _meshC[vi + k] = col;
                }
                for (int k = 1; k < 5; k++)
                {
                    _meshT[ti++] = vi;
                    _meshT[ti++] = vi + k;
                    _meshT[ti++] = vi + k + 1;
                }
            }
            if (_buildCell < _cellN && _buildPlaced < _landCap) { return; }

            _building = false;
            Mesh mesh = TerrainFilter.sharedMesh;
            if (!mesh)
            {
                mesh = new Mesh();
                TerrainFilter.sharedMesh = mesh;
            }
            else { mesh.Clear(); }
            mesh.vertices = _meshV;
            mesh.colors = _meshC;
            mesh.triangles = _meshT;
            mesh.RecalculateBounds();
            _meshV = null;
            _meshC = null;
            _meshT = null;
        }

        private Color HeightColor(float h)
        {
            if (HeightColors == null || HeightColors.Length == 0) { return Color.gray; }
            float[] th = _terrain.HeightThresholds;
            int band = 0;
            if (th != null) { for (int i = 0; i < th.Length; i++) { if (h >= th[i]) { band = i + 1; } } }
            return HeightColors[Mathf.Min(band, HeightColors.Length - 1)];
        }
    }
}
