using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;

namespace ACO
{
    /// 육각 기둥 지형(씨앗으로 생성). 반경 FieldRadius 안을 육각 격자로 덮고, 칸마다 씨앗으로 높이를 정해 기둥(ColumnPrefab)을 세운다.
    /// 지형 = 바다 위의 땅: 씨앗마다 프리셋(큰 섬/군도/쌍둥이 섬/환초/열도/고원/산맥/협곡)을 PresetWeights 비율로 골라
    /// 땅 모양(원 + 능선 선분)을 정하고, 해안선은 노이즈로 흔들고, 프리셋마다 다른 높이 모양(산/단 지형/능선/깎인 협곡)을 얹는다.
    /// 범위 가장자리와 제외 구역(항공모함 등) 둘레는 항상 바다로 잦아들어 작전 지역 바깥 바다와 자연스럽게 이어진다.
    /// 해수면 아래 칸은 기둥을 만들지 않는다(바다는 씬의 바다가 보인다).
    /// 기둥은 평평한 육각 윗면 + 바깥으로 경사진 치마(SkirtWidth m, 프리팹 메시) — 이웃 높이 차가 치마가 내려가는 높이 이하면
    /// 높은 기둥의 치마가 낮은 기둥 위로 덮여 차량이 오를 수 있는 비탈이 되고, 그보다 크면 절벽이 된다(콘보이 항로 검사가 피한다).
    ///
    /// 동기화는 씨앗(정수)뿐: 오너가 정해 보내면 모든 클라이언트가 같은 지형을 로컬로 만든다(늦게 들어와도 같은 지형).
    /// 노이즈는 정수 해시 값 노이즈 + 높이를 HeightStep 단위로 끊어서, 클라이언트마다 소수점이 조금 달라도 같은 높이가 나온다.
    /// 기둥은 처음 만들 때 한 번 Instantiate하고 씨앗이 바뀌면 다시 쓴다(네트워크/락온 목록과 무관한 지형이라 런타임 생성이 괜찮다).
    /// 만들기는 프레임마다 ColumnsPerFrame개씩 나눠서 한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_HexTerrain : UdonSharpBehaviour
    {
        [Header("기둥")]
        [Tooltip("육각 기둥 프리팹(ACO → Create Prefabs...의 육각 기둥). 메시 원점 = 윗면 중심")]
        public GameObject ColumnPrefab;
        [Tooltip("기둥 윗면 육각형의 꼭짓점 반지름(m). 프리팹 메시와 같아야 칸이 맞물린다(Bake가 프리팹 값으로 맞춘다)")]
        public float HexRadius = 400f;
        [Tooltip("높이대별 머티리얼(낮은 곳부터). 비우면 프리팹 그대로")]
        public Material[] HeightMaterials;
        [Tooltip("머티리얼 경계 높이(m, 오름차순, HeightMaterials 수 - 1개)")]
        public float[] HeightThresholds = new float[] { 40f, 300f, 650f };

        [Header("범위")]
        [Tooltip("이 오브젝트 위치를 중심으로 이 반경(m) 안을 덮는다. 칸 수 ≈ 반경² x 7.6 / 10⁶ (HexRadius 400m 기준, 20km ≈ 3,000칸 — 바다 칸은 만들지 않는다)")]
        public float FieldRadius = 20000f;
        [Tooltip("이 콜라이더들의 영역(바운딩 박스) 둘레는 바다로 만든다(항공모함/기지 등). ExcludeMargin 안은 바다, 그 바깥 ExcludeFalloff에 걸쳐 섬이 다시 솟을 수 있다")]
        public Collider[] ExcludeZones;
        public float ExcludeMargin = 1500f;
        public float ExcludeFalloff = 4000f;
        [Tooltip("제외 구역(스폰 지점)에서 작전 지역 밖까지 바닷길을 낸다 — 바다와 낮은 땅을 따라 굽이치는 가장 싼 길을 찾아 그 길에 걸친 땅만 판다(항공모함이 섬에 막히지 않고 밖으로 나갈 수 있다)")]
        public bool CarveSeaLane = true;
        [Tooltip("바닷길 폭(m)")]
        public float SeaLaneWidth = 4000f;
        [Tooltip("바닷길 가장자리에서 땅이 다시 솟기까지의 거리(m)")]
        public float SeaLaneFalloff = 3000f;
        [Tooltip("바닷길 경로 탐색 격자 칸 크기(m). 작을수록 해안을 잘 따라가지만 탐색이 느려진다(칸 수 ≈ (2 x 범위 / 이 값)²)")]
        public float SeaLaneGridSize = 1200f;
        [Tooltip("바닷길이 땅을 지나는 비용(클수록 땅을 피해 멀리 돌아간다). 바다 한 칸 = 10, 땅 한 칸 = 10 + 이 값 x (1 + 높이/100m)")]
        public float SeaLaneLandCost = 30f;

        [Header("지형 프리셋")]
        [Tooltip("씨앗마다 이 비율로 지형 모양을 고른다. 순서: 0 큰 섬, 1 군도, 2 쌍둥이 섬, 3 환초, 4 열도, 5 고원, 6 산맥, 7 협곡 (0이면 안 나옴, 배열이 짧으면 빠진 칸은 0)")]
        public float[] PresetWeights = new float[] { 1f, 1f, 1f, 0.5f, 1f, 1f, 1f, 1f };
        [Tooltip("0~7이면 늘 그 프리셋(테스트/고정 맵용), -1이면 씨앗으로 고른다")]
        public int ForcedPreset = -1;

        [Header("섬 (큰 섬/군도/쌍둥이 섬/환초/열도)")]
        [Tooltip("큰 섬 반지름(FieldRadius 비율)")]
        public float BigIslandRadius = 0.6f;
        [Tooltip("큰 섬 중심이 범위 가운데에서 벗어날 수 있는 거리(FieldRadius 비율)")]
        public float BigIslandOffset = 0.4f;
        [Tooltip("큰 섬 둘레 작은 섬 수(최대)")]
        public int SatelliteIslands = 3;
        [Tooltip("군도 섬 수")]
        public int IslandCountMin = 4;
        public int IslandCountMax = 8;
        [Tooltip("군도 섬 중심이 흩어지는 반경(FieldRadius 비율)")]
        [Range(0.1f, 1f)] public float ArchipelagoSpread = 0.85f;
        [Tooltip("군도 섬 중심 사이 최소 거리 = (두 섬 반지름 합) x 이 값. 클수록 섬이 떨어지고, 1보다 작으면 조금씩 붙을 수 있다")]
        [Range(0f, 1.5f)] public float IslandSpacing = 0.8f;
        [Tooltip("군도/작은 섬 반지름(FieldRadius 비율)")]
        public float SmallIslandRadiusMin = 0.1f;
        public float SmallIslandRadiusMax = 0.25f;
        [Tooltip("환초 고리 반지름(FieldRadius 비율). 고리 안쪽은 석호(바다)")]
        public float AtollRadius = 0.55f;
        [Tooltip("섬 모양 값이 이보다 낮으면 바다(클수록 땅이 작아진다)")]
        [Range(0f, 1f)] public float CoastThreshold = 0.3f;
        [Tooltip("해안선 흔들림(노이즈 세기)")]
        public float CoastRoughness = 0.4f;
        [Tooltip("범위 가장자리 이 비율(FieldRadius)에 걸쳐 바다로 잦아든다")]
        [Range(0.05f, 0.5f)] public float EdgeFalloff = 0.12f;

        [Header("고원")]
        [Tooltip("고원 땅 반지름(FieldRadius 비율, 둘레에 혹 2~3개가 붙는다)")]
        public float PlateauRadius = 0.7f;
        [Tooltip("단 수(해안 저지대 위로)")]
        public int PlateauTiers = 4;
        [Tooltip("단 하나의 높이(m). 기둥 경사가 내려가는 높이(약 214m) 이하면 이웃 단 사이가 비탈(차량이 오른다), 두 단이 맞닿은 곳은 절벽")]
        public float PlateauTierHeight = 160f;
        [Tooltip("고원 가장자리(해안 저지대 → 첫 단)가 절벽인 비율(대략). 나머지는 비탈이라 차량이 오를 수 있는 길목이 된다")]
        [Range(0f, 1f)] public float PlateauCliff = 0.6f;

        [Header("산맥")]
        [Tooltip("산맥 땅의 반폭(FieldRadius 비율) — 능선에서 이만큼 떨어지면 바다")]
        public float RangeWidth = 0.34f;
        [Tooltip("능선 최고 높이(해수면 위, m)")]
        public float RangeMaxHeight = 1500f;
        [Tooltip("곁가지 능선이 생길 확률")]
        [Range(0f, 1f)] public float RangeBranchChance = 0.5f;

        [Header("협곡")]
        [Tooltip("협곡 대지 반지름(FieldRadius 비율)")]
        public float CanyonRadius = 0.8f;
        [Tooltip("대지 높이(저지대 위, m) — 협곡 벽 높이")]
        public float CanyonHeight = 520f;
        [Tooltip("협곡 바닥 폭(m). 바닥은 저지대 높이(차량/저공 비행 통로)")]
        public float CanyonWidth = 2400f;
        [Tooltip("벽이 바닥에서 대지까지 올라가는 수평 거리(m). 짧을수록 깎아지른 절벽")]
        public float CanyonWallWidth = 1200f;
        [Tooltip("곁가지 협곡이 생길 확률")]
        [Range(0f, 1f)] public float CanyonBranchChance = 0.5f;

        [Header("높이")]
        [Tooltip("해안 바로 안쪽 땅 높이(해수면 위, m)")]
        public float LowlandHeight = 20f;
        [Tooltip("섬 한가운데 산의 최고 높이(해수면 위, m) — 섬 프리셋. 환초는 이것의 45%, 열도는 115%")]
        public float MaxHeight = 900f;
        [Tooltip("높이를 이 단위(m)로 끊는다 — 계단 모양 + 클라이언트 간 일치")]
        public float HeightStep = 20f;
        [Tooltip("노이즈 한 칸 크기(m). 클수록 완만하고 큰 지형")]
        public float NoiseScale = 6000f;
        [Range(1, 6)] public int Octaves = 4;
        [Range(0f, 1f)] public float Persistence = 0.5f;
        [Tooltip("1보다 크면 낮은 땅이 넓어지고 산이 뾰족해진다")]
        public float HeightExponent = 1.6f;
        [Tooltip("해수면(m, 생성기 SeaLevel과 맞춘다). 이보다 낮은 칸은 기둥을 만들지 않는다")]
        public float SeaLevel = 0f;

        [Header("생성")]
        [Tooltip("월드 시작 시 지형을 만든다(오너가 씨앗을 정한다). 미션마다 새로 만드는 건 생성기의 RegenerateTerrainEachMission")]
        public bool GenerateOnStart = true;
        [Tooltip("시작 씨앗(0이면 무작위)")]
        public int InitialSeed = 0;
        [Tooltip("프레임당 세울 기둥 수(클수록 빨리 끝나고 그 동안 끊김이 크다)")]
        public int ColumnsPerFrame = 200;
        [Tooltip("지형 장식(도시/마을/랜드마크). 지형이 다 세워지면 이 칸 높이로 자리를 골라 놓는다 — 생성기는 장식까지 끝나야 유닛을 배치한다")]
        public ACO_TerrainFeatures Features;

        [UdonSynced] private int _seed;
        [UdonSynced] private int _generation;

        // 로컬: 격자(칸 중심의 로컬 X/Z)와 기둥 풀(칸마다 하나)
        private float[] _cellX;
        private float[] _cellZ;
        // 칸의 축 좌표(q, r)와 이번 씨앗의 높이(바다 칸은 해수면 아래), (q, r) → 칸 번호 표(이웃 찾기)
        private int[] _cellQ;
        private int[] _cellR;
        private float[] _cellH;
        private int[] _cellMap;
        private int _mapN;
        private int[] _cellNb; // 칸 i의 dir 이웃 = _cellNb[i * 6 + dir] (-1 = 범위 밖)
        private int _cellCount;
        private GameObject[] _columns;
        private MeshRenderer[] _renderers;
        private GameObject[] _trees;   // 기둥 자식 "Trees"(나무 묶음 — 셰이더 ACO/HexTrees가 숲 자리에만 세운다). 없으면 null
        private int[] _band;
        private bool _gridBuilt;

        // 프리셋
        public const int PRESET_BIG_ISLAND = 0;
        public const int PRESET_ARCHIPELAGO = 1;
        public const int PRESET_TWIN = 2;
        public const int PRESET_ATOLL = 3;
        public const int PRESET_CHAIN = 4;
        public const int PRESET_PLATEAU = 5;
        public const int PRESET_RANGE = 6;
        public const int PRESET_CANYON = 7;
        public const int PRESET_COUNT = 8;

        // 이번 씨앗의 땅 모양(지형 로컬 X/Z). 씨앗 해시로 정해 모든 클라이언트가 같다.
        // 원(blob): 중심에서 1 - (거리/반지름)², 선분(seg, 산맥 능선): 선분까지 거리로 같은 식, 깎기(cut, 협곡): 땅은 그대로 두고 높이만 깎는다.
        private const int MAX_BLOBS = 16;
        private float[] _blobX = new float[MAX_BLOBS];
        private float[] _blobZ = new float[MAX_BLOBS];
        private float[] _blobR = new float[MAX_BLOBS];
        private int _blobCount;
        private const int MAX_SEGS = 8;
        private float[] _segAX = new float[MAX_SEGS];
        private float[] _segAZ = new float[MAX_SEGS];
        private float[] _segBX = new float[MAX_SEGS];
        private float[] _segBZ = new float[MAX_SEGS];
        private float[] _segW = new float[MAX_SEGS];
        private int _segCount;
        private const int MAX_CUTS = 10;
        private float[] _cutAX = new float[MAX_CUTS];
        private float[] _cutAZ = new float[MAX_CUTS];
        private float[] _cutBX = new float[MAX_CUTS];
        private float[] _cutBZ = new float[MAX_CUTS];
        private int _cutCount;
        // MakePath 결과(꺾은선 점)
        private const int MAX_PATH = 8;
        private float[] _pathX = new float[MAX_PATH];
        private float[] _pathZ = new float[MAX_PATH];
        private int _preset;
        private bool _shapesBuilt;
        private int _shapesSeed;

        // 바닷길(제외 구역마다 하나): 격자 위 최소 비용 경로를 다듬은 꺾은선(지형 로컬). ExcludeMask가 이 선분 둘레를 판다.
        private const int MAX_LANE_SEGS = 64;
        private float[] _laneAX = new float[MAX_LANE_SEGS];
        private float[] _laneAZ = new float[MAX_LANE_SEGS];
        private float[] _laneBX = new float[MAX_LANE_SEGS];
        private float[] _laneBZ = new float[MAX_LANE_SEGS];
        private int _laneSegCount;
        // 탐색 격자(한 변 _laneN칸, 지형 로컬 -_laneHalf ~ +_laneHalf)와 다익스트라 상태(정수 비용 — 클라이언트마다 같은 길)
        private int _laneN;
        private float _laneCell;
        private float _laneHalf;
        private int[] _laneCost;
        private int[] _laneDist;
        private int[] _laneParent;
        private int[] _lanePath;
        private int[] _heapNode;
        private int[] _heapKey;
        private int _heapSize;
        private int _popKey;
        private int _laneSample;
        private int _laneZone;
        private int _laneGoal;
        private float _laneStartX;
        private float _laneStartZ;
        private const int MAX_LANE_PTS = 40;
        private float[] _lanePtX = new float[MAX_LANE_PTS];
        private float[] _lanePtZ = new float[MAX_LANE_PTS];
        private float[] _laneSmX = new float[MAX_LANE_PTS * 2];
        private float[] _laneSmZ = new float[MAX_LANE_PTS * 2];

        // 만들기 단계: 바닷길 비용 재기 → 바닷길 찾기 → 기둥 세우기(모두 프레임에 나눠서)
        private const int STAGE_LANE_SAMPLE = 0;
        private const int STAGE_LANE_SEARCH = 1;
        private const int STAGE_COLUMNS = 2;
        private int _buildStage;

        private int _builtGeneration = -1;
        private int _builtSeed;
        private bool _building;
        private int _buildIndex;
        private int _buildSeed;
        private int _buildGeneration;

        void Start()
        {
            BuildGrid();
            if (GenerateOnStart && Networking.IsOwner(gameObject) && _generation == 0)
            {
                Generate(InitialSeed != 0 ? InitialSeed : Random.Range(1, int.MaxValue));
            }
        }

        // ───────────── 진입점 ─────────────

        /// 씨앗으로 지형을 다시 만든다(누구나 호출 → 오너가 동기화). seed 0이면 무작위.
        public void RequestGenerate(int seed)
        {
            if (Networking.IsOwner(gameObject)) { Generate(seed); }
            else { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(NetGenerate), seed); }
        }

        [NetworkCallable]
        public void NetGenerate(int seed)
        {
            Generate(seed);
        }

        public void Generate(int seed)
        {
            if (!Networking.IsOwner(gameObject)) { return; }
            _seed = seed != 0 ? seed : Random.Range(1, int.MaxValue);
            _generation++;
            RequestSerialization();
            BeginBuild();
        }

        public override void OnDeserialization()
        {
            if (_generation != _builtGeneration && !(_building && _buildGeneration == _generation)) { BeginBuild(); }
        }

        /// 이 씨앗의 지형이 이 클라이언트에서 다 세워졌는지(생성기가 유닛 배치 전에 기다린다).
        public bool IsBuilt(int seed) { return !_building && _builtSeed == seed && _builtGeneration == _generation && (Features == null || Features.IsPlaced(seed)); }

        /// 지금 동기화된 씨앗(생성기가 이 지형 그대로 배치할 때 다 세워지기를 기다리는 기준).
        public int GetSeed() { return _seed; }

        /// 다 세워진 지형의 프리셋(PRESET_*). 세우는 중이면 -1(브리핑 표시용).
        public int GetBuiltPreset() { return _building ? -1 : _preset; }

        public string PresetName(int preset)
        {
            if (preset == PRESET_ARCHIPELAGO) { return "군도"; }
            if (preset == PRESET_TWIN) { return "쌍둥이 섬"; }
            if (preset == PRESET_ATOLL) { return "환초"; }
            if (preset == PRESET_CHAIN) { return "열도"; }
            if (preset == PRESET_PLATEAU) { return "고원"; }
            if (preset == PRESET_RANGE) { return "산맥"; }
            if (preset == PRESET_CANYON) { return "협곡"; }
            return "큰 섬";
        }

        // ───────────── 만들기 ─────────────

        private void BeginBuild()
        {
            BuildGrid();
            _buildSeed = _seed;
            _buildGeneration = _generation;
            _buildIndex = 0;
            StartLane();
            if (Features != null) { Features.OnTerrainChanging(); }
            if (_building) { return; } // 진행 중인 루프가 새 씨앗으로 처음부터 이어 간다
            _building = true;
            SendCustomEventDelayedFrames(nameof(BuildStep), 1);
        }

        public void BuildStep()
        {
            // 만드는 도중 씨앗이 또 바뀌었으면 처음부터
            if (_buildGeneration != _generation) { _buildSeed = _seed; _buildGeneration = _generation; _buildIndex = 0; StartLane(); }
            if (_buildStage == STAGE_LANE_SAMPLE)
            {
                LaneSampleStep();
                SendCustomEventDelayedFrames(nameof(BuildStep), 1);
                return;
            }
            if (_buildStage == STAGE_LANE_SEARCH)
            {
                LaneSearchStep();
                SendCustomEventDelayedFrames(nameof(BuildStep), 1);
                return;
            }
            int end = Mathf.Min(_buildIndex + Mathf.Max(ColumnsPerFrame, 1), _cellCount);
            Vector3 origin = transform.position;
            for (int i = _buildIndex; i < end; i++) { PlaceColumn(i, origin); }
            _buildIndex = end;
            if (_buildIndex < _cellCount)
            {
                SendCustomEventDelayedFrames(nameof(BuildStep), 1);
                return;
            }
            _building = false;
            _builtSeed = _buildSeed;
            _builtGeneration = _buildGeneration;
            BuildShapes(_builtSeed);
            Debug.Log("[ACO] 육각 지형 완성: 씨앗 " + _builtSeed + ", " + PresetName(_preset) + ", 칸 " + _cellCount);
            if (Features != null) { Features.OnTerrainBuilt(_builtSeed); }
        }

        private void PlaceColumn(int i, Vector3 origin)
        {
            float x = origin.x + _cellX[i];
            float z = origin.z + _cellZ[i];
            float h = HeightAt(_cellX[i], _cellZ[i], _buildSeed);
            _cellH[i] = h;
            GameObject col = _columns[i];
            // 바다 칸: 기둥을 만들지 않는다(이전 씨앗에서 만든 기둥은 끈다).
            if (h < SeaLevel)
            {
                if (col != null && col.activeSelf) { col.SetActive(false); }
                return;
            }
            if (col == null)
            {
                if (ColumnPrefab == null) { return; }
                col = Object.Instantiate(ColumnPrefab);
                col.transform.SetParent(transform, true);
                _columns[i] = col;
                _renderers[i] = col.GetComponent<MeshRenderer>();
                Transform trees = col.transform.Find("Trees");
                _trees[i] = trees != null ? trees.gameObject : null;
                _band[i] = -1;
            }
            col.transform.SetPositionAndRotation(new Vector3(x, h, z), Quaternion.identity);
            if (!col.activeSelf) { col.SetActive(true); }
            // 나무는 새 지형마다 다시 켠다 — 장식(도시·비행장 등)이 놓인 칸은 지형 장식이 끝난 뒤 SetCellTrees로 끈다.
            if (_trees[i] != null && !_trees[i].activeSelf) { _trees[i].SetActive(true); }

            int band = HeightBand(h);
            if (band != _band[i] && HeightMaterials != null && band < HeightMaterials.Length && _renderers[i] != null && HeightMaterials[band] != null)
            {
                _renderers[i].sharedMaterial = HeightMaterials[band];
                _band[i] = band;
            }
        }

        private int HeightBand(float h)
        {
            int band = 0;
            if (HeightThresholds == null) { return 0; }
            for (int k = 0; k < HeightThresholds.Length; k++) { if (h >= HeightThresholds[k]) { band = k + 1; } }
            return band;
        }

        /// 제외 구역(바운딩 박스) 둘레의 섬 억제: ExcludeMargin 안 0(바다), 그 바깥 ExcludeFalloff에 걸쳐 1로.
        /// 그리고 찾아 둔 바닷길 꺾은선 둘레(SeaLaneWidth)를 판다 — 스폰 지점에서 작전 지역 밖으로 나가는 길.
        private float ExcludeMask(float worldX, float worldZ)
        {
            if (ExcludeZones == null) { return 1f; }
            Vector3 origin = transform.position;
            float mask = 1f;
            for (int i = 0; i < ExcludeZones.Length; i++)
            {
                Collider zone = ExcludeZones[i];
                if (zone == null) { continue; }
                Bounds b = zone.bounds;
                float dx = Mathf.Max(Mathf.Max(b.min.x - worldX, worldX - b.max.x), 0f);
                float dz = Mathf.Max(Mathf.Max(b.min.z - worldZ, worldZ - b.max.z), 0f);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                mask = Mathf.Min(mask, SmoothStep(ExcludeMargin, ExcludeMargin + Mathf.Max(ExcludeFalloff, 1f), d));
            }
            if (_laneSegCount > 0)
            {
                float lx = worldX - origin.x;
                float lz = worldZ - origin.z;
                float laneDist = 1e9f;
                for (int k = 0; k < _laneSegCount; k++)
                {
                    float d2 = SegDistance(lx, lz, _laneAX[k], _laneAZ[k], _laneBX[k], _laneBZ[k]);
                    if (d2 < laneDist) { laneDist = d2; }
                }
                float half = SeaLaneWidth * 0.5f;
                mask = Mathf.Min(mask, SmoothStep(half, half + Mathf.Max(SeaLaneFalloff, 1f), laneDist));
            }
            return mask;
        }

        // ───────────── 바닷길 찾기 ─────────────
        // 범위를 덮는 격자(SeaLaneGridSize)의 칸마다 바닷길 없이 잰 높이로 비용을 매기고(바다 0, 땅은 높을수록 비쌈),
        // 구역 중심에서 범위 밖 칸까지 다익스트라(8방향, 정수 비용)로 가장 싼 길을 찾는다 → 몇 칸 걸러 점을 뽑고
        // 모서리를 한 번 깎아(Chaikin) 매끄러운 꺾은선으로 만든다. 섬 사이 바다, 그다음 낮은 땅(해협/협곡 바닥)을 따라 굽이친다.

        private void StartLane()
        {
            _laneSegCount = 0;
            _buildStage = STAGE_COLUMNS;
            if (!CarveSeaLane || SeaLaneWidth <= 0f || ExcludeZones == null || ExcludeZones.Length == 0) { return; }
            _laneCell = Mathf.Max(SeaLaneGridSize, 200f);
            _laneHalf = FieldRadius + _laneCell * 2f;
            int n = Mathf.Clamp(Mathf.CeilToInt(_laneHalf * 2f / _laneCell), 4, 160);
            int total = n * n;
            if (_laneCost == null || _laneCost.Length != total)
            {
                _laneCost = new int[total];
                _laneDist = new int[total];
                _laneParent = new int[total];
                _lanePath = new int[total];
                _heapNode = new int[total * 8 + 16];
                _heapKey = new int[total * 8 + 16];
            }
            _laneN = n;
            _laneSample = 0;
            _laneZone = -1;
            _buildStage = STAGE_LANE_SAMPLE;
        }

        private float LaneCoord(int i) { return -_laneHalf + (i + 0.5f) * _laneCell; }

        private void LaneSampleStep()
        {
            int total = _laneN * _laneN;
            int end = Mathf.Min(_laneSample + Mathf.Max(ColumnsPerFrame, 1), total);
            float r2 = FieldRadius * FieldRadius;
            for (int i = _laneSample; i < end; i++)
            {
                float x = LaneCoord(i % _laneN);
                float z = LaneCoord(i / _laneN);
                int cost = 0;
                if (x * x + z * z < r2)
                {
                    float h = HeightAt(x, z, _buildSeed); // 아직 바닷길 선분이 없어 원래 지형 높이
                    if (h >= SeaLevel) { cost = Mathf.RoundToInt(SeaLaneLandCost * (1f + (h - SeaLevel) / 100f)); }
                }
                _laneCost[i] = cost;
            }
            _laneSample = end;
            if (end >= total) { NextLaneZone(); }
        }

        /// 다음 제외 구역의 길 찾기를 시작한다. 더 없으면 기둥 세우기로.
        private void NextLaneZone()
        {
            _laneZone++;
            while (_laneZone < ExcludeZones.Length && ExcludeZones[_laneZone] == null) { _laneZone++; }
            if (_laneZone >= ExcludeZones.Length || _laneSegCount >= MAX_LANE_SEGS - 1)
            {
                _buildStage = STAGE_COLUMNS;
                return;
            }
            Vector3 o = transform.position;
            Bounds b = ExcludeZones[_laneZone].bounds;
            _laneStartX = b.center.x - o.x;
            _laneStartZ = b.center.z - o.z;
            int n = _laneN;
            int sx = Mathf.Clamp(Mathf.FloorToInt((_laneStartX + _laneHalf) / _laneCell), 0, n - 1);
            int sz = Mathf.Clamp(Mathf.FloorToInt((_laneStartZ + _laneHalf) / _laneCell), 0, n - 1);
            int total = n * n;
            for (int i = 0; i < total; i++) { _laneDist[i] = int.MaxValue; _laneParent[i] = -1; }
            _heapSize = 0;
            _laneGoal = -1;
            int start = sz * n + sx;
            _laneDist[start] = 0;
            HeapPush(start, 0);
            _buildStage = STAGE_LANE_SEARCH;
        }

        private void LaneSearchStep()
        {
            int budget = Mathf.Max(ColumnsPerFrame, 1) * 2;
            int n = _laneN;
            float r2 = FieldRadius * FieldRadius;
            for (int k = 0; k < budget; k++)
            {
                if (_heapSize == 0) { NextLaneZone(); return; } // 길 없음(있을 수 없지만) → 이 구역은 바닷길 없이
                int node = HeapPop();
                int key = _popKey;
                if (key > _laneDist[node]) { continue; }
                int ix = node % n;
                int iz = node / n;
                float x = LaneCoord(ix);
                float z = LaneCoord(iz);
                if (x * x + z * z >= r2)
                {
                    _laneGoal = node;
                    AddLanePath();
                    NextLaneZone();
                    return;
                }
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nz = iz + dz;
                    if (nz < 0 || nz >= n) { continue; }
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = ix + dx;
                        if ((dx == 0 && dz == 0) || nx < 0 || nx >= n) { continue; }
                        int nb = nz * n + nx;
                        int step = (dx != 0 && dz != 0) ? 14 : 10;
                        int nd = key + step * (10 + _laneCost[nb]) / 10;
                        if (nd < _laneDist[nb])
                        {
                            _laneDist[nb] = nd;
                            _laneParent[nb] = node;
                            HeapPush(nb, nd);
                        }
                    }
                }
            }
        }

        /// 찾은 길(목표 칸 → 시작 칸)을 구역 중심에서 범위 밖까지의 매끄러운 꺾은선으로 바꿔 바닷길 선분에 더한다.
        private void AddLanePath()
        {
            int count = 0;
            int node = _laneGoal;
            while (node >= 0 && count < _lanePath.Length)
            {
                _lanePath[count] = node;
                count++;
                node = _laneParent[node];
            }
            if (count == 0) { return; }
            // 점 뽑기: 구역 중심 → 몇 칸 걸러 → 목표 칸(범위 밖으로 조금 더 밀어 가장자리까지 확실히 열리게)
            int stride = Mathf.Max(1, Mathf.CeilToInt(count / 12f));
            int m = 0;
            _lanePtX[m] = _laneStartX;
            _lanePtZ[m] = _laneStartZ;
            m++;
            for (int i = count - 1 - stride; i > 0 && m < MAX_LANE_PTS - 1; i -= stride)
            {
                _lanePtX[m] = LaneCoord(_lanePath[i] % _laneN);
                _lanePtZ[m] = LaneCoord(_lanePath[i] / _laneN);
                m++;
            }
            float gx = LaneCoord(_laneGoal % _laneN);
            float gz = LaneCoord(_laneGoal / _laneN);
            float gl = Mathf.Sqrt(gx * gx + gz * gz);
            float push = gl > 1f ? (FieldRadius * 1.15f) / gl : 1f;
            _lanePtX[m] = gx * Mathf.Max(push, 1f);
            _lanePtZ[m] = gz * Mathf.Max(push, 1f);
            m++;
            // 모서리 깎기(Chaikin 한 번, 양 끝 고정)
            int k = 0;
            _laneSmX[k] = _lanePtX[0];
            _laneSmZ[k] = _lanePtZ[0];
            k++;
            for (int i = 0; i < m - 1; i++)
            {
                if (i > 0)
                {
                    _laneSmX[k] = _lanePtX[i] * 0.75f + _lanePtX[i + 1] * 0.25f;
                    _laneSmZ[k] = _lanePtZ[i] * 0.75f + _lanePtZ[i + 1] * 0.25f;
                    k++;
                }
                if (i < m - 2)
                {
                    _laneSmX[k] = _lanePtX[i] * 0.25f + _lanePtX[i + 1] * 0.75f;
                    _laneSmZ[k] = _lanePtZ[i] * 0.25f + _lanePtZ[i + 1] * 0.75f;
                    k++;
                }
            }
            _laneSmX[k] = _lanePtX[m - 1];
            _laneSmZ[k] = _lanePtZ[m - 1];
            k++;
            for (int i = 0; i < k - 1 && _laneSegCount < MAX_LANE_SEGS; i++)
            {
                _laneAX[_laneSegCount] = _laneSmX[i];
                _laneAZ[_laneSegCount] = _laneSmZ[i];
                _laneBX[_laneSegCount] = _laneSmX[i + 1];
                _laneBZ[_laneSegCount] = _laneSmZ[i + 1];
                _laneSegCount++;
            }
        }

        // 최소 힙(키 = 누적 비용). 같은 칸이 여러 번 들어갈 수 있고, 꺼낼 때 더 나은 값이 이미 있으면 버린다.
        private void HeapPush(int node, int key)
        {
            if (_heapSize >= _heapNode.Length) { return; }
            int i = _heapSize;
            _heapSize++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_heapKey[parent] <= key) { break; }
                _heapNode[i] = _heapNode[parent];
                _heapKey[i] = _heapKey[parent];
                i = parent;
            }
            _heapNode[i] = node;
            _heapKey[i] = key;
        }

        /// 가장 싼 칸을 꺼낸다(비용은 _popKey).
        private int HeapPop()
        {
            int top = _heapNode[0];
            _popKey = _heapKey[0];
            _heapSize--;
            if (_heapSize == 0) { return top; }
            int node = _heapNode[_heapSize];
            int key = _heapKey[_heapSize];
            int i = 0;
            while (true)
            {
                int c = i * 2 + 1;
                if (c >= _heapSize) { break; }
                if (c + 1 < _heapSize && _heapKey[c + 1] < _heapKey[c]) { c++; }
                if (_heapKey[c] >= key) { break; }
                _heapNode[i] = _heapNode[c];
                _heapKey[i] = _heapKey[c];
                i = c;
            }
            _heapNode[i] = node;
            _heapKey[i] = key;
            return top;
        }

        private float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ───────────── 땅 모양(프리셋) ─────────────

        /// 씨앗으로 프리셋과 땅 모양을 정한다(정수 해시 — 모든 클라이언트가 같다).
        private void BuildShapes(int seed)
        {
            if (_shapesBuilt && _shapesSeed == seed) { return; }
            _shapesBuilt = true;
            _shapesSeed = seed;
            _blobCount = 0;
            _segCount = 0;
            _cutCount = 0;
            _preset = PickPreset(seed);
            float R = Mathf.Max(FieldRadius, 1f);
            if (_preset == PRESET_ARCHIPELAGO) { BuildArchipelago(seed, R); }
            else if (_preset == PRESET_TWIN) { BuildTwin(seed, R); }
            else if (_preset == PRESET_ATOLL) { BuildAtoll(seed, R); }
            else if (_preset == PRESET_CHAIN) { BuildChain(seed, R); }
            else if (_preset == PRESET_PLATEAU) { BuildLandmass(seed, R, PlateauRadius, 241); }
            else if (_preset == PRESET_RANGE) { BuildRange(seed, R); }
            else if (_preset == PRESET_CANYON) { BuildCanyon(seed, R); }
            else { BuildBigIsland(seed, R); }
        }

        private int PickPreset(int seed)
        {
            if (ForcedPreset >= 0 && ForcedPreset < PRESET_COUNT) { return ForcedPreset; }
            float total = 0f;
            for (int i = 0; i < PRESET_COUNT; i++) { total += PresetWeight(i); }
            if (total <= 0f) { return PRESET_BIG_ISLAND; }
            float pick = Hash(11, 17, seed) * total;
            int last = PRESET_BIG_ISLAND;
            for (int i = 0; i < PRESET_COUNT; i++)
            {
                float w = PresetWeight(i);
                if (w <= 0f) { continue; }
                last = i;
                pick -= w;
                if (pick < 0f) { return i; }
            }
            return last;
        }

        private float PresetWeight(int i)
        {
            if (PresetWeights == null || i >= PresetWeights.Length) { return 0f; }
            return Mathf.Max(PresetWeights[i], 0f);
        }

        /// 큰 섬(가운데에서 BigIslandOffset까지 벗어남) + 둘레 작은 섬. 작은 섬이 범위 밖으로 나가면 반대편에 둔다.
        private void BuildBigIsland(int seed, float R)
        {
            float a = Hash(43, 47, seed) * Mathf.PI * 2f;
            float d = Mathf.Sqrt(Hash(53, 59, seed)) * BigIslandOffset * R;
            float cx = Mathf.Cos(a) * d;
            float cz = Mathf.Sin(a) * d;
            float r0 = BigIslandRadius * R * Mathf.Lerp(0.85f, 1.15f, Hash(61, 67, seed));
            AddBlob(cx, cz, r0);
            int sats = Mathf.Clamp(SatelliteIslands, 0, MAX_BLOBS - 1);
            for (int k = 0; k < sats; k++)
            {
                if (Hash(k, 71, seed) < 0.3f) { continue; } // 가끔 빠진다
                float sa = Hash(k, 73, seed) * Mathf.PI * 2f;
                float sd = r0 * Mathf.Lerp(0.9f, 1.3f, Hash(k, 79, seed));
                float sx = Mathf.Cos(sa) * sd;
                float sz = Mathf.Sin(sa) * sd;
                float px = cx + sx;
                float pz = cz + sz;
                if (px * px + pz * pz > R * R * 0.72f) { px = cx - sx; pz = cz - sz; }
                AddBlob(px, pz, Mathf.Lerp(SmallIslandRadiusMin, SmallIslandRadiusMax, Hash(k, 83, seed)) * R * 0.6f);
            }
        }

        /// 군도: 섬 IslandCountMin~Max개를 ArchipelagoSpread 안에 흩되, 서로 IslandSpacing보다 가까우면 다른 자리를 몇 번 더 찾는다.
        private void BuildArchipelago(int seed, float R)
        {
            int lo = Mathf.Clamp(IslandCountMin, 1, MAX_BLOBS);
            int hi = Mathf.Clamp(IslandCountMax, lo, MAX_BLOBS);
            int count = lo + Mathf.FloorToInt(Hash(23, 29, seed) * (hi - lo + 0.999f));
            float spread = Mathf.Clamp(ArchipelagoSpread, 0.1f, 1f) * R;
            for (int k = 0; k < count; k++)
            {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    int key = k * 8 + attempt;
                    float a = Hash(key, 31, seed) * Mathf.PI * 2f;
                    float d = Mathf.Sqrt(Hash(key, 37, seed)) * spread;
                    float r = Mathf.Lerp(SmallIslandRadiusMin, SmallIslandRadiusMax, Hash(key, 41, seed)) * R;
                    float x = Mathf.Cos(a) * d;
                    float z = Mathf.Sin(a) * d;
                    if (BlobFits(x, z, r)) { AddBlob(x, z, r); break; }
                }
            }
        }

        /// 쌍둥이 섬: 무작위 축 양쪽에 비슷한 큰 섬 둘, 그 사이 해협(약 2~6km), 해협 양 끝에 가끔 작은 섬.
        private void BuildTwin(int seed, float R)
        {
            float a = Hash(89, 97, seed) * Mathf.PI * 2f;
            float ux = Mathf.Cos(a);
            float uz = Mathf.Sin(a);
            for (int side = -1; side <= 1; side += 2)
            {
                float sd = Mathf.Lerp(0.42f, 0.5f, Hash(side + 2, 107, seed)) * R * side;
                AddBlob(ux * sd, uz * sd, Mathf.Lerp(0.34f, 0.4f, Hash(side + 2, 109, seed)) * R);
            }
            for (int k = 0; k < 2; k++)
            {
                if (Hash(k, 113, seed) < 0.4f) { continue; }
                float d = Mathf.Lerp(0.45f, 0.65f, Hash(k, 127, seed)) * R * (k == 0 ? 1f : -1f);
                AddBlob(-uz * d, ux * d, Mathf.Lerp(SmallIslandRadiusMin, SmallIslandRadiusMax, Hash(k, 131, seed)) * R * 0.6f);
            }
        }

        /// 환초: 고리(AtollRadius) 위에 섬 12개를 이어 붙이고 몇 개를 빼서 석호로 드나드는 물길을 낸다. 가끔 석호 한가운데 작은 섬.
        private void BuildAtoll(int seed, float R)
        {
            float a0 = Hash(137, 139, seed) * Mathf.PI * 2f;
            float d0 = Mathf.Sqrt(Hash(149, 151, seed)) * 0.15f * R;
            float cx = Mathf.Cos(a0) * d0;
            float cz = Mathf.Sin(a0) * d0;
            float ring = AtollRadius * R * Mathf.Lerp(0.9f, 1.1f, Hash(157, 163, seed));
            int count = Mathf.Min(12, MAX_BLOBS - 1);
            for (int k = 0; k < count; k++)
            {
                if (Hash(k, 167, seed) < 0.2f) { continue; }
                float a = a0 + (k + (Hash(k, 173, seed) - 0.5f) * 0.5f) / count * Mathf.PI * 2f;
                float d = ring * Mathf.Lerp(0.92f, 1.08f, Hash(k, 179, seed));
                AddBlob(cx + Mathf.Cos(a) * d, cz + Mathf.Sin(a) * d, Mathf.Lerp(0.16f, 0.22f, Hash(k, 181, seed)) * R);
            }
            if (Hash(191, 193, seed) < 0.5f) { AddBlob(cx, cz, 0.07f * R); }
        }

        /// 열도: 범위를 가로지르는 휜 곡선(2차 베지어)을 따라 섬 6~10개를 늘어놓는다(가운데 섬이 크다).
        private void BuildChain(int seed, float R)
        {
            float a = Hash(197, 199, seed) * Mathf.PI * 2f;
            float ux = Mathf.Cos(a);
            float uz = Mathf.Sin(a);
            float half = 0.8f * R;
            float bend = (Hash(211, 223, seed) - 0.5f) * 0.9f * R;
            float shift = (Hash(227, 229, seed) - 0.5f) * 0.4f * R;
            // P0, P2 = 양 끝, P1 = 조절점(곡선 가운데가 shift + bend만큼 옆으로)
            float p0x = -ux * half - uz * shift;
            float p0z = -uz * half + ux * shift;
            float p2x = ux * half - uz * shift;
            float p2z = uz * half + ux * shift;
            float p1x = -uz * (shift + bend * 2f);
            float p1z = ux * (shift + bend * 2f);
            int count = Mathf.Min(7 + Mathf.FloorToInt(Hash(233, 239, seed) * 4.999f), MAX_BLOBS);
            for (int k = 0; k < count; k++)
            {
                float t = Mathf.Clamp01((k + 0.5f + (Hash(k, 241, seed) - 0.5f) * 0.4f) / count);
                float u = 1f - t;
                float x = u * u * p0x + 2f * u * t * p1x + t * t * p2x;
                float z = u * u * p0z + 2f * u * t * p1z + t * t * p2z;
                float j = (Hash(k, 251, seed) - 0.5f) * 0.1f * R;
                x += -uz * j;
                z += ux * j;
                float r = Mathf.Lerp(0.09f, 0.17f, Hash(k, 257, seed)) * R * (0.75f + 0.5f * Mathf.Sin(Mathf.PI * t));
                AddBlob(x, z, r);
            }
        }

        /// 넓은 땅덩이(고원/협곡): 가운데 근처 큰 원(radius) + 둘레 혹 2~3개.
        private void BuildLandmass(int seed, float R, float radius, int key)
        {
            float a = Hash(key, 263, seed) * Mathf.PI * 2f;
            float d = Mathf.Sqrt(Hash(key, 269, seed)) * 0.2f * R;
            float cx = Mathf.Cos(a) * d;
            float cz = Mathf.Sin(a) * d;
            float r0 = radius * R * Mathf.Lerp(0.9f, 1.1f, Hash(key, 271, seed));
            AddBlob(cx, cz, r0);
            for (int k = 0; k < 3; k++)
            {
                if (Hash(key + k, 277, seed) < 0.35f) { continue; }
                float la = Hash(key + k, 281, seed) * Mathf.PI * 2f;
                float ld = r0 * Mathf.Lerp(0.55f, 0.8f, Hash(key + k, 283, seed));
                AddBlob(cx + Mathf.Cos(la) * ld, cz + Mathf.Sin(la) * ld, r0 * Mathf.Lerp(0.35f, 0.5f, Hash(key + k, 293, seed)));
            }
        }

        /// 산맥: 범위를 가로지르는 구불구불한 능선(꺾은선 4마디) + 가끔 곁가지 능선 + 작은 섬 0~2개.
        private void BuildRange(int seed, float R)
        {
            float a = Hash(307, 311, seed) * Mathf.PI * 2f;
            float ux = Mathf.Cos(a);
            float uz = Mathf.Sin(a);
            float off = (Hash(313, 317, seed) - 0.5f) * 0.4f * R;
            float w = RangeWidth * R;
            int n = MakePath(seed, 331, -uz * off, ux * off, ux, uz, 0.85f * R, 0.18f * R, 4, false);
            for (int i = 0; i < n; i++) { AddSeg(_pathX[i], _pathZ[i], _pathX[i + 1], _pathZ[i + 1], w); }
            if (Hash(337, 347, seed) < RangeBranchChance)
            {
                float ba = a + (Hash(349, 353, seed) < 0.5f ? 1f : -1f) * Mathf.Lerp(0.6f, 1.2f, Hash(359, 367, seed));
                n = MakePath(seed, 373, _pathX[2], _pathZ[2], Mathf.Cos(ba), Mathf.Sin(ba), 0.55f * R, 0.12f * R, 2, true);
                for (int i = 0; i < n; i++) { AddSeg(_pathX[i], _pathZ[i], _pathX[i + 1], _pathZ[i + 1], w * 0.75f); }
            }
            for (int k = 0; k < 2; k++)
            {
                if (Hash(k, 379, seed) < 0.5f) { continue; }
                float ia = Hash(k, 383, seed) * Mathf.PI * 2f;
                float id = Mathf.Lerp(0.5f, 0.8f, Hash(k, 389, seed)) * R;
                AddBlob(Mathf.Cos(ia) * id, Mathf.Sin(ia) * id, Mathf.Lerp(SmallIslandRadiusMin, SmallIslandRadiusMax, Hash(k, 397, seed)) * R * 0.6f);
            }
        }

        /// 협곡: 넓은 대지를 범위 끝에서 끝까지(바다로 열리게) 구불구불한 협곡 하나가 가르고, 가끔 곁가지 협곡.
        private void BuildCanyon(int seed, float R)
        {
            BuildLandmass(seed, R, CanyonRadius, 401);
            float a = Hash(409, 419, seed) * Mathf.PI * 2f;
            float ux = Mathf.Cos(a);
            float uz = Mathf.Sin(a);
            float off = (Hash(421, 431, seed) - 0.5f) * 0.3f * R;
            int n = MakePath(seed, 433, -uz * off, ux * off, ux, uz, 1.15f * R, 0.22f * R, 6, false);
            for (int i = 0; i < n; i++) { AddCut(_pathX[i], _pathZ[i], _pathX[i + 1], _pathZ[i + 1]); }
            if (Hash(439, 443, seed) < CanyonBranchChance)
            {
                int from = Hash(449, 457, seed) < 0.5f ? 2 : 4;
                float ba = a + (Hash(461, 463, seed) < 0.5f ? 1f : -1f) * Mathf.Lerp(0.7f, 1.3f, Hash(467, 479, seed));
                n = MakePath(seed, 487, _pathX[from], _pathZ[from], Mathf.Cos(ba), Mathf.Sin(ba), 0.9f * R, 0.15f * R, 3, true);
                for (int i = 0; i < n; i++) { AddCut(_pathX[i], _pathZ[i], _pathX[i + 1], _pathZ[i + 1]); }
            }
        }

        /// 꺾은선을 _pathX/_pathZ에 만든다(segs + 1개 점, 마디 수를 돌려준다). 방향 (ux, uz)로
        /// fromStart면 (sx, sz)에서 length만큼, 아니면 (sx, sz)를 가운데로 양쪽 length만큼. 점마다 옆으로 ±wobble 흔든다(시작점 고정 시 첫 점은 그대로).
        private int MakePath(int seed, int key, float sx, float sz, float ux, float uz, float length, float wobble, int segs, bool fromStart)
        {
            int n = Mathf.Clamp(segs, 1, MAX_PATH - 1);
            for (int i = 0; i <= n; i++)
            {
                float f = (float)i / n;
                float along = fromStart ? length * f : Mathf.Lerp(-length, length, f);
                float side = (fromStart && i == 0) ? 0f : (Hash(key, i + 1, seed) - 0.5f) * 2f * wobble;
                _pathX[i] = sx + ux * along - uz * side;
                _pathZ[i] = sz + uz * along + ux * side;
            }
            return n;
        }

        private void AddBlob(float x, float z, float r)
        {
            if (_blobCount >= MAX_BLOBS || r < 1f) { return; }
            _blobX[_blobCount] = x;
            _blobZ[_blobCount] = z;
            _blobR[_blobCount] = r;
            _blobCount++;
        }

        private void AddSeg(float ax, float az, float bx, float bz, float w)
        {
            if (_segCount >= MAX_SEGS || w < 1f) { return; }
            _segAX[_segCount] = ax;
            _segAZ[_segCount] = az;
            _segBX[_segCount] = bx;
            _segBZ[_segCount] = bz;
            _segW[_segCount] = w;
            _segCount++;
        }

        private void AddCut(float ax, float az, float bx, float bz)
        {
            if (_cutCount >= MAX_CUTS) { return; }
            _cutAX[_cutCount] = ax;
            _cutAZ[_cutCount] = az;
            _cutBX[_cutCount] = bx;
            _cutBZ[_cutCount] = bz;
            _cutCount++;
        }

        private bool BlobFits(float x, float z, float r)
        {
            for (int i = 0; i < _blobCount; i++)
            {
                float dx = x - _blobX[i];
                float dz = z - _blobZ[i];
                float min = (r + _blobR[i]) * IslandSpacing;
                if (dx * dx + dz * dz < min * min) { return false; }
            }
            return true;
        }

        /// 점 (px, pz)에서 선분 a-b까지 거리.
        private float SegDistance(float px, float pz, float ax, float az, float bx, float bz)
        {
            float vx = bx - ax;
            float vz = bz - az;
            float wx = px - ax;
            float wz = pz - az;
            float len2 = vx * vx + vz * vz;
            float t = len2 > 0f ? Mathf.Clamp01((wx * vx + wz * vz) / len2) : 0f;
            float dx = wx - vx * t;
            float dz = wz - vz * t;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// 땅 모양(0~1): 원/능선 중 가장 높은 값(1 - (거리/반지름)²), 범위 가장자리 EdgeFalloff에 걸쳐 0으로.
        private float ShapeMask(float x, float z)
        {
            float best = 0f;
            for (int i = 0; i < _blobCount; i++)
            {
                float dx = x - _blobX[i];
                float dz = z - _blobZ[i];
                float f = 1f - (dx * dx + dz * dz) / (_blobR[i] * _blobR[i]);
                if (f > best) { best = f; }
            }
            for (int i = 0; i < _segCount; i++)
            {
                float d = SegDistance(x, z, _segAX[i], _segAZ[i], _segBX[i], _segBZ[i]) / _segW[i];
                float f = 1f - d * d;
                if (f > best) { best = f; }
            }
            float edge = Mathf.Sqrt(x * x + z * z) / Mathf.Max(FieldRadius, 1f);
            return Mathf.Clamp01(best) * (1f - SmoothStep(1f - EdgeFalloff, 1f, edge));
        }

        /// 협곡 꺾은선까지 가장 가까운 거리(없으면 아주 큼).
        private float CutDistance(float x, float z)
        {
            float best = 1e9f;
            for (int i = 0; i < _cutCount; i++)
            {
                float d = SegDistance(x, z, _cutAX[i], _cutAZ[i], _cutBX[i], _cutBZ[i]);
                if (d < best) { best = d; }
            }
            return best;
        }

        /// 육각 격자(꼭짓점이 위인 육각형, 축 좌표 q/r): 중심 X = R·√3·(q + r/2), Z = R·1.5·r. 반경 안의 칸만.
        private void BuildGrid()
        {
            if (_gridBuilt) { return; }
            _gridBuilt = true;
            float w = HexRadius * 1.7320508f;
            float hStep = HexRadius * 1.5f;
            int n = Mathf.CeilToInt(FieldRadius / hStep) + 1;
            float r2 = FieldRadius * FieldRadius;
            int count = 0;
            for (int r = -n; r <= n; r++)
            {
                for (int q = -n * 2; q <= n * 2; q++)
                {
                    float x = w * (q + r * 0.5f);
                    float z = hStep * r;
                    if (x * x + z * z <= r2) { count++; }
                }
            }
            _cellX = new float[count];
            _cellZ = new float[count];
            _cellQ = new int[count];
            _cellR = new int[count];
            _cellH = new float[count];
            _mapN = n;
            int span = n * 4 + 1;
            _cellMap = new int[(n * 2 + 1) * span];
            for (int m = 0; m < _cellMap.Length; m++) { _cellMap[m] = -1; }
            int k = 0;
            for (int r = -n; r <= n; r++)
            {
                for (int q = -n * 2; q <= n * 2; q++)
                {
                    float x = w * (q + r * 0.5f);
                    float z = hStep * r;
                    if (x * x + z * z > r2) { continue; }
                    _cellX[k] = x;
                    _cellZ[k] = z;
                    _cellQ[k] = q;
                    _cellR[k] = r;
                    _cellMap[(r + n) * span + (q + n * 2)] = k;
                    k++;
                }
            }
            _cellCount = count;
            _cellNb = new int[count * 6];
            for (int c = 0; c < count; c++)
            {
                for (int dir = 0; dir < 6; dir++) { _cellNb[c * 6 + dir] = GetNeighbor(c, dir); }
            }
            _columns = new GameObject[count];
            _renderers = new MeshRenderer[count];
            _trees = new GameObject[count];
            _band = new int[count];
        }

        /// 칸 i의 나무 묶음을 켜고 끈다(지형 장식이 건물 자리에서 끈다). 기둥이나 나무 묶음이 없으면 아무것도 안 한다.
        public void SetCellTrees(int i, bool on)
        {
            if (_trees == null || i < 0 || i >= _trees.Length) { return; }
            GameObject t = _trees[i];
            if (t != null && t.activeSelf != on) { t.SetActive(on); }
        }

        // ───────────── 칸 조회 (지형 장식용, 지형이 다 세워진 뒤) ─────────────
        // 이웃 방향 0~5: 축 좌표 (+1,0) (0,+1) (-1,+1) (-1,0) (0,-1) (+1,-1) = 각도 0/60/120/180/240/300°(+X에서 +Z 쪽으로).

        public int GetCellCount() { return _cellCount; }
        /// 배열 그대로(읽기 전용으로 쓸 것) — 다른 스크립트가 칸마다 메서드를 부르면 Udon에서 느리다.
        public float[] GetCellXs() { return _cellX; }
        public float[] GetCellZs() { return _cellZ; }
        public float[] GetCellHeights() { return _cellH; }
        /// 이웃 표: 칸 i의 dir(0~5) 이웃 = 표[i * 6 + dir], 범위 밖 -1.
        public int[] GetNeighborTable() { return _cellNb; }

        /// 칸 i의 dir(0~5) 방향 이웃 칸 번호. 범위 밖이면 -1.
        public int GetNeighbor(int i, int dir)
        {
            int q = _cellQ[i];
            int r = _cellR[i];
            if (dir == 0) { q += 1; }
            else if (dir == 1) { r += 1; }
            else if (dir == 2) { q -= 1; r += 1; }
            else if (dir == 3) { q -= 1; }
            else if (dir == 4) { r -= 1; }
            else { q += 1; r -= 1; }
            int n = _mapN;
            if (r < -n || r > n || q < -n * 2 || q > n * 2) { return -1; }
            return _cellMap[(r + n) * (n * 4 + 1) + (q + n * 2)];
        }

        // ───────────── 노이즈 ─────────────

        /// 로컬 (x, z)의 높이. 해수면보다 낮으면 바다(기둥 없음).
        /// 땅 모양(ShapeMask x 제외 구역/바닷길) + 해안 노이즈 → 해안 기준값보다 낮으면 바다, 높으면 해안에서 안쪽 정도(t, 0~1)로
        /// 프리셋별 높이를 정한다. HeightStep 단위로 끊는다.
        public float HeightAt(float x, float z, int seed)
        {
            BuildShapes(seed);
            float step = Mathf.Max(HeightStep, 0.01f);
            Vector3 o = transform.position;
            float mask = ShapeMask(x, z) * ExcludeMask(o.x + x, o.z + z);
            float coast = mask + (Fractal(x, z, seed) - 0.5f) * CoastRoughness;
            if (coast < CoastThreshold) { return SeaLevel - step; }

            float t = Mathf.Clamp01((coast - CoastThreshold) / Mathf.Max(1f - CoastThreshold, 0.01f));
            float low = SeaLevel + LowlandHeight;
            float h;
            if (_preset == PRESET_PLATEAU)
            {
                // 단 지형: 안쪽일수록, 노이즈가 높을수록 높은 단. 단 위는 평평(메사).
                // 첫 단은 대개 한 단 더 높여 해안에서 절벽(단애)으로 솟게 하고, 노이즈가 낮은 곳만 비탈 길목으로 남긴다.
                float v = Mathf.Sqrt(t) * Mathf.Lerp(0.5f, 1.05f, Fractal(x, z, seed ^ 0x2545f491));
                int tiers = Mathf.Max(PlateauTiers, 1);
                int level = Mathf.Clamp(Mathf.FloorToInt(v * (tiers + 1)), 0, tiers);
                if (level >= 1 && Fractal(x, z, seed ^ 0x68e31da4) > 0.65f - 0.3f * PlateauCliff) { level++; }
                h = low + level * PlateauTierHeight;
            }
            else if (_preset == PRESET_RANGE)
            {
                // 능선: 능선 가까이(t → 1) 높고 둘레는 구릉, 능선 노이즈(1 - |2n - 1|, 두 배 촘촘)로 봉우리와 안부(고개)가 갈린다
                float ridge = 1f - Mathf.Abs(Fractal(x * 2f, z * 2f, seed ^ 0x2545f491) * 2f - 1f);
                h = low + RangeMaxHeight * Mathf.Pow(t, 1.8f) * Mathf.Lerp(0.25f, 1f, ridge);
            }
            else if (_preset == PRESET_CANYON)
            {
                // 대지(해안에서 곧 솟는다)를 협곡 꺾은선 둘레만 바닥(저지대)까지 깎는다
                float top = low + CanyonHeight * SmoothStep(0f, 0.3f, t) * Mathf.Lerp(0.85f, 1.15f, Fractal(x, z, seed ^ 0x2545f491));
                float half = CanyonWidth * 0.5f;
                h = Mathf.Lerp(low, top, SmoothStep(half, half + Mathf.Max(CanyonWallWidth, 1f), CutDistance(x, z)));
            }
            else
            {
                // 섬: 해안에서 안쪽으로 갈수록 산 노이즈(지수)만큼 솟는다
                float scale = _preset == PRESET_ATOLL ? 0.45f : (_preset == PRESET_CHAIN ? 1.15f : 1f);
                float mountain = Mathf.Pow(Fractal(x, z, seed ^ 0x5bd1e995), HeightExponent);
                h = low + MaxHeight * scale * t * mountain;
            }
            // 해안 칸도 반드시 물 위에 있게(끊은 높이가 해수면이 되지 않게)
            return Mathf.Max(Mathf.Round(h / step) * step, SeaLevel + step);
        }

        /// 옥타브 합 값 노이즈(0~1).
        private float Fractal(float x, float z, int seed)
        {
            float sum = 0f;
            float amp = 1f;
            float norm = 0f;
            float freq = 1f / Mathf.Max(NoiseScale, 1f);
            for (int o = 0; o < Octaves; o++)
            {
                sum += amp * ValueNoise(x * freq, z * freq, seed + o * 1013904223);
                norm += amp;
                amp *= Persistence;
                freq *= 2f;
            }
            return norm > 0f ? Mathf.Clamp01(sum / norm) : 0f;
        }

        private float ValueNoise(float fx, float fz, int seed)
        {
            int x0 = Mathf.FloorToInt(fx);
            int z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0;
            float tz = fz - z0;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float a = Hash(x0, z0, seed);
            float b = Hash(x0 + 1, z0, seed);
            float c = Hash(x0, z0 + 1, seed);
            float d = Hash(x0 + 1, z0 + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        /// 정수 해시(0~1). 정수 연산이라 모든 클라이언트에서 같다.
        private float Hash(int x, int z, int seed)
        {
            int h = seed;
            h ^= x * 374761393;
            h ^= z * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / 2147483647f;
        }
    }
}
