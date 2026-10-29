using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 지형 장식: 육각 지형(ACO_HexTerrain)이 다 세워지면 칸 높이로 도시/마을/비행장/항구/등대/레이더·통신탑/유적 자리를 골라 프리팹을 놓는다.
    /// 자리는 지형 씨앗(정수 해시)과 끊긴 칸 높이로만 정하므로 모든 클라이언트가 같은 자리에 같은 장식을 놓는다(동기화 없음, 늦게 들어와도 같다).
    ///  - 도시: 낮고 평평한 칸 + 같은 높이 이웃(최대 CityMaxBlocks칸)을 블록으로 덮는다. 바다에 닿으면 그 해안에 항구.
    ///  - 비행장: 같은 높이 칸 4개가 일렬(약 2.8km 평지)인 곳에 활주로.
    ///  - 마을: 평평한 칸 하나. 등대: 바다로 튀어나온 곶. 레이더/통신탑: 이웃보다 높은 봉우리. 유적: 높은 평지.
    /// 이웃 칸이 더 높으면 그 기둥의 경사 치마가 이 칸 위를 덮으므로(MaxOverhang) 그런 칸은 피한다.
    /// 건물은 충돌이 있다. 생성기는 건물 자리(IsBlocked)에 유닛을 놓지 않고, SettlementPreference 확률로 마을/도시/비행장/레이더
    /// 둘레(주둔지, PickSitePoint)에 지상 그룹/페이즈 목표를 놓는다. 자리마다 이름이 붙어 페이즈 배너에 작전 지역으로 나온다.
    /// 만들기는 프레임에 나눈다: 칸 분석(CellsPerFrame) → 자리 고르기(한 프레임에 하나) → 프리팹 놓기(ObjectsPerFrame).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_TerrainFeatures : UdonSharpBehaviour
    {
        public const int SITE_CITY = 0;
        public const int SITE_VILLAGE = 1;
        public const int SITE_AIRFIELD = 2;
        public const int SITE_PORT = 3;
        public const int SITE_LIGHTHOUSE = 4;
        public const int SITE_RADAR = 5;
        public const int SITE_TOWER = 6;
        public const int SITE_RUINS = 7;

        [Header("지형")]
        [Tooltip("이 지형이 다 세워지면 장식을 놓는다(지형의 Features에도 이 오브젝트를 넣는다 — Bake가 서로 연결)")]
        public ACO_HexTerrain Terrain;

        [Header("프리팹 (ACO → Create Prefabs...의 지형 장식 — 직접 만든 모델로 바꿔 끼워도 된다)")]
        [Tooltip("도시 칸 하나를 덮는 블록(원점 = 칸 윗면 중심, 육각 윗면 안쪽 약 340m). 칸마다 무작위 변형")]
        public GameObject[] CityBlockPrefabs;
        [Tooltip("마을(원점 = 칸 윗면 중심, 반경 약 250m 안)")]
        public GameObject[] VillagePrefabs;
        [Tooltip("항구(원점 = 해수면 높이의 해안선, +Z가 바다 쪽)")]
        public GameObject PortPrefab;
        [Tooltip("비행장(원점 = 활주로 가운데, 활주로가 Z축 2.4km, 시설은 +X 쪽)")]
        public GameObject AirfieldPrefab;
        [Tooltip("등대(원점 = 바닥, +Z가 바다 쪽)")]
        public GameObject LighthousePrefab;
        public GameObject RadarPrefab;
        public GameObject CommTowerPrefab;
        public GameObject RuinsPrefab;

        [Header("수")]
        public int CityCount = 2;
        [Tooltip("도시 하나가 덮는 최대 칸 수(가운데 + 같은 높이 이웃)")]
        [Range(1, 7)] public int CityMaxBlocks = 5;
        public int VillageCount = 8;
        public int AirfieldCount = 1;
        public int LighthouseCount = 3;
        [Tooltip("봉우리 위 레이더/통신탑 수")]
        public int HighlandCount = 3;
        public int RuinsCount = 1;

        [Header("자리 조건")]
        [Tooltip("도시/마을/비행장 사이 최소 거리(m)")]
        public float SettlementSpacing = 5000f;
        [Tooltip("같은 종류 랜드마크 사이 최소 거리(m)")]
        public float LandmarkSpacing = 4000f;
        [Tooltip("그 밖의 자리 사이 최소 거리(m)")]
        public float MinSiteGap = 1500f;
        [Tooltip("도시/비행장을 놓을 최대 높이(해수면 위, m)")]
        public float LowlandMaxHeight = 300f;
        public float VillageMaxHeight = 500f;
        [Tooltip("레이더/통신탑 봉우리 최소 높이(해수면 위, m)")]
        public float HighlandMinHeight = 350f;
        public float RuinsMinHeight = 250f;
        [Tooltip("이웃과 높이 차가 이 이하면 같은 높이로 친다(m)")]
        public float FlatTolerance = 20f;
        [Tooltip("이웃 칸이 이보다 더 높으면 그 경사가 칸 위를 덮어 도시/비행장을 놓지 않는다(m)")]
        public float MaxOverhang = 20f;
        [Tooltip("마을/유적 칸의 같은 조건(m) — 건물이 가운데에 모여 있어 더 너그럽다")]
        public float VillageMaxOverhang = 40f;

        [Header("주둔지 (생성기가 적을 놓는 자리)")]
        [Tooltip("자리 가장자리에서 이 거리(m) 범위 안에 지상 유닛/목표를 놓는다")]
        public float GarrisonDistanceMin = 300f;
        public float GarrisonDistanceMax = 1500f;

        [Header("지명 (페이즈 배너의 작전 지역 'AO: 이름')")]
        [Tooltip("이름 앞 음절. 자리마다 앞 + 뒤 음절을 지형 씨앗으로 골라 붙인다(예: KEL + DALE = KELDALE). 기본 TMP 폰트라 영문 대문자·숫자·공백만 쓴다. "
            + "모든 클라이언트가 같은 목록을 써야 같은 이름이 나온다(씬 값이라 그대로 맞는다)")]
        public string[] NamePrefixes = new string[] {
            "AR", "BEL", "COR", "DUN", "EL", "FAL", "GOR", "HAL", "IR", "KEL", "LUN", "MAR",
            "NOR", "OS", "PRA", "RAV", "SEL", "TOR", "UL", "VER", "WIN", "YOR", "ZAN", "KAS",
            "ALD", "AVEN", "BRAN", "CAL", "CRES", "DRAV", "EST", "FEN", "GAL", "GRAN", "HES", "ISEN",
            "JAR", "KOR", "LAN", "LIS", "MOR", "NAV", "OST", "PEL", "QUAR", "ROS", "SAL", "SOL",
            "STRA", "TAL", "THAR", "VAL", "VEL", "VOR", "WES", "ZEL", "ASH", "BRIG", "DER", "MIR" };
        [Tooltip("이름 뒤 음절")]
        public string[] NameSuffixes = new string[] {
            "DALE", "GRAD", "MOUTH", "HOLM", "FORD", "TON", "VIK", "BURG", "STEAD", "HAVEN",
            "IA", "ORA", "EK", "UM", "ANE", "OSA", "RIN", "VAL", "MERE", "ICA",
            "BERG", "LAND", "OV", "NIA", "PORT", "WICK", "GARD", "MOOR", "ENA", "ARA",
            "ETH", "IUM", "OLA", "KIRK", "WATER", "CLIFF", "NESS", "STAD", "HEIM", "ISK",
            "OVA", "ANT", "ERA", "BRIDGE", "FELL", "WOOD", "MARK", "ODAR" };
        [Tooltip("종류별 이름 꼬리말(이름 뒤에 붙는다). 비행장, 등대, 레이더, 통신탑, 항구(가까운 도시 이름 뒤)")]
        public string AirfieldLabel = " AIRBASE";
        public string LighthouseLabel = " LIGHTHOUSE";
        public string RadarLabel = " RADAR STATION";
        public string TowerLabel = " RELAY TOWER";
        public string PortLabel = " HARBOR";
        [Tooltip("폐허 이름 앞말(이름 앞에 붙는다)")]
        public string RuinsLabel = "RUINS OF ";

        [Header("생성")]
        [Tooltip("프레임당 분석할 칸 수")]
        public int CellsPerFrame = 300;
        [Tooltip("프레임당 놓을 장식 수")]
        public int ObjectsPerFrame = 8;

        // 단계
        private const int STAGE_IDLE = 0;
        private const int STAGE_ANALYZE = 1;
        private const int STAGE_SELECT = 2;
        private const int STAGE_SPAWN = 3;
        private const int STAGE_DONE = 4;
        // 자리 고르기 순서(종류 = 고르기 번호)
        private const int PICK_CITY = 0;
        private const int PICK_AIRFIELD = 1;
        private const int PICK_VILLAGE = 2;
        private const int PICK_LIGHTHOUSE = 3;
        private const int PICK_HIGHLAND = 4;
        private const int PICK_RUINS = 5;
        private const int PICK_COUNT = 6;

        private int _stage;
        private bool _running;
        private int _jobSeed;
        private int _index;
        private int _pickKind;
        private int _pickDone;
        private bool _placed;
        private int _placedSeed;

        // 지형 배열(지형 스크립트의 배열 참조) + 칸 분석 결과
        private float[] _cx;
        private float[] _cz;
        private float[] _ch;
        private int[] _nb;
        private int _cellCount;
        private float _sea;
        private float _hexR;
        private Vector3 _origin;
        private int[] _flat;      // 높이가 같은 육지 이웃 수
        private int[] _seaN;      // 바다(또는 범위 밖) 이웃 수
        private float[] _over;    // 가장 높은 이웃 - 이 칸(음수면 이 칸이 제일 높다)
        private bool[] _peak;     // 모든 이웃보다 높다
        private bool[] _taken;

        // 자리
        private const int MAX_SITES = 48;
        private int[] _siteKind = new int[MAX_SITES];
        private float[] _siteX = new float[MAX_SITES];
        private float[] _siteY = new float[MAX_SITES];
        private float[] _siteZ = new float[MAX_SITES];
        private float[] _siteR = new float[MAX_SITES];
        private float[] _siteYaw = new float[MAX_SITES];   // 비행장: 활주로 방향(Y 회전, 활주로 = +Z)
        private string[] _siteName = new string[MAX_SITES];
        private int _siteCount;

        // 막힌 곳(건물 자리, 월드 X/Z + 반지름)
        private const int MAX_BLOCKS = 128;
        private float[] _blockX = new float[MAX_BLOCKS];
        private float[] _blockZ = new float[MAX_BLOCKS];
        private float[] _blockR = new float[MAX_BLOCKS];
        private int _blockCount;

        // 놓을 목록 → 인스턴스(프리팹별로 다시 쓴다)
        private const int MAX_PLACE = 128;
        private GameObject[] _plPrefab = new GameObject[MAX_PLACE];
        private Vector3[] _plPos = new Vector3[MAX_PLACE];
        private float[] _plYaw = new float[MAX_PLACE];
        private int _plCount;
        private const int MAX_INST = 192;
        private GameObject[] _inst = new GameObject[MAX_INST];
        private GameObject[] _instSrc = new GameObject[MAX_INST];
        private bool[] _instUsed = new bool[MAX_INST];
        private int _instCount;

        // PickSitePoint 결과
        private float _pickX;
        private float _pickZ;
        private int[] _pickList = new int[MAX_SITES];


        // ───────────── 지형에서 부른다 ─────────────

        /// 지형을 다시 만들기 시작할 때: 장식을 숨기고 자리를 비운다(생성기가 옛 자리를 쓰지 않게).
        public void OnTerrainChanging()
        {
            _stage = STAGE_IDLE;
            _placed = false;
            _siteCount = 0;
            _blockCount = 0;
            _plCount = 0;
            for (int i = 0; i < _instCount; i++)
            {
                _instUsed[i] = false;
                if (_inst[i] != null && _inst[i].activeSelf) { _inst[i].SetActive(false); }
            }
        }

        /// 지형이 다 세워졌을 때: 이 씨앗으로 자리를 고르고 놓기 시작한다.
        public void OnTerrainBuilt(int seed)
        {
            OnTerrainChanging();
            if (Terrain == null) { return; }
            _jobSeed = seed;
            _cx = Terrain.GetCellXs();
            _cz = Terrain.GetCellZs();
            _ch = Terrain.GetCellHeights();
            _nb = Terrain.GetNeighborTable();
            _cellCount = Terrain.GetCellCount();
            _sea = Terrain.SeaLevel;
            _hexR = Terrain.HexRadius;
            _origin = Terrain.transform.position;
            if (_flat == null || _flat.Length != _cellCount)
            {
                _flat = new int[_cellCount];
                _seaN = new int[_cellCount];
                _over = new float[_cellCount];
                _peak = new bool[_cellCount];
                _taken = new bool[_cellCount];
            }
            _index = 0;
            _stage = STAGE_ANALYZE;
            if (_running) { return; } // 진행 중인 루프가 새 씨앗으로 이어 간다
            _running = true;
            SendCustomEventDelayedFrames(nameof(PlaceStep), 1);
        }

        public bool IsPlaced(int seed) { return _placed && _placedSeed == seed; }

        public void PlaceStep()
        {
            if (_stage == STAGE_ANALYZE) { AnalyzeStep(); }
            else if (_stage == STAGE_SELECT) { SelectStep(); }
            else if (_stage == STAGE_SPAWN) { SpawnStep(); }
            if (_stage == STAGE_IDLE || _stage == STAGE_DONE)
            {
                _running = false;
                return;
            }
            SendCustomEventDelayedFrames(nameof(PlaceStep), 1);
        }

        // ───────────── 1. 칸 분석 ─────────────

        private void AnalyzeStep()
        {
            int end = Mathf.Min(_index + Mathf.Max(CellsPerFrame, 1), _cellCount);
            for (int i = _index; i < end; i++)
            {
                float h = _ch[i];
                int flat = 0;
                int sea = 0;
                float maxNb = -100000f;
                bool peak = h >= _sea;
                for (int dir = 0; dir < 6; dir++)
                {
                    int n = _nb[i * 6 + dir];
                    if (n < 0 || _ch[n] < _sea) { sea++; continue; }
                    float nh = _ch[n];
                    if (Mathf.Abs(nh - h) <= FlatTolerance) { flat++; }
                    if (nh > maxNb) { maxNb = nh; }
                    if (nh >= h) { peak = false; }
                }
                _flat[i] = flat;
                _seaN[i] = sea;
                _over[i] = maxNb - h;
                _peak[i] = peak && sea == 0;
                _taken[i] = false;
            }
            _index = end;
            if (end >= _cellCount)
            {
                _stage = STAGE_SELECT;
                _pickKind = 0;
                _pickDone = 0;
            }
        }

        // ───────────── 2. 자리 고르기 (프레임마다 하나) ─────────────

        private void SelectStep()
        {
            if (_pickKind >= PICK_COUNT)
            {
                _stage = STAGE_SPAWN;
                _index = 0;
                return;
            }
            if (_pickDone >= PickTarget(_pickKind) || !PickOne(_pickKind))
            {
                _pickKind++;
                _pickDone = 0;
                return;
            }
            _pickDone++;
        }

        private int PickTarget(int kind)
        {
            if (kind == PICK_CITY) { return CityCount; }
            if (kind == PICK_AIRFIELD) { return AirfieldCount; }
            if (kind == PICK_VILLAGE) { return VillageCount; }
            if (kind == PICK_LIGHTHOUSE) { return LighthouseCount; }
            if (kind == PICK_HIGHLAND) { return HighlandCount; }
            return RuinsCount;
        }

        /// 이 종류의 가장 좋은 칸을 골라 자리를 만든다. 없으면 false.
        private bool PickOne(int kind)
        {
            if (_siteCount >= MAX_SITES - 1) { return false; }
            int best = -1;
            float bestScore = 0f;
            for (int i = 0; i < _cellCount; i++)
            {
                if (_taken[i]) { continue; }
                float s = Score(kind, i);
                if (s <= bestScore) { continue; }
                if (!FarFromSites(kind, _origin.x + _cx[i], _origin.z + _cz[i])) { continue; }
                best = i;
                bestScore = s;
            }
            if (best < 0) { return false; }
            if (kind == PICK_CITY) { AddCity(best); }
            else if (kind == PICK_AIRFIELD) { AddAirfield(best); }
            else if (kind == PICK_VILLAGE) { AddSimple(best, SITE_VILLAGE, PickVariant(VillagePrefabs, best), _hexR * 0.6f, true); }
            else if (kind == PICK_LIGHTHOUSE) { AddLighthouse(best); }
            else if (kind == PICK_HIGHLAND)
            {
                bool radar = Hash(best, 401, _jobSeed) < 0.5f;
                if (radar && RadarPrefab == null) { radar = false; }
                if (!radar && CommTowerPrefab == null) { radar = true; }
                AddSimple(best, radar ? SITE_RADAR : SITE_TOWER, radar ? RadarPrefab : CommTowerPrefab, 120f, false);
            }
            else { AddSimple(best, SITE_RUINS, RuinsPrefab, _hexR * 0.4f, false); }
            return true;
        }

        /// 칸 점수(0 이하면 못 쓴다). 조건 + 씨앗 해시로 조금 흔든다.
        private float Score(int kind, int i)
        {
            float h = _ch[i];
            if (h < _sea) { return 0f; }
            float up = h - _sea;
            float jitter = Hash(i, kind * 7919 + 13, _jobSeed);
            if (kind == PICK_CITY)
            {
                if (CityBlockPrefabs == null || CityBlockPrefabs.Length == 0) { return 0f; }
                if (up > LowlandMaxHeight || _flat[i] < 3 || _over[i] > MaxOverhang) { return 0f; }
                return 10f + _flat[i] * 4f + (_seaN[i] > 0 ? 8f : 0f) + jitter * 10f;
            }
            if (kind == PICK_AIRFIELD)
            {
                if (AirfieldPrefab == null || up > LowlandMaxHeight || _over[i] > MaxOverhang) { return 0f; }
                if (AirfieldDir(i) < 0) { return 0f; }
                return 10f + jitter * 10f;
            }
            if (kind == PICK_VILLAGE)
            {
                if (VillagePrefabs == null || VillagePrefabs.Length == 0) { return 0f; }
                if (up > VillageMaxHeight || _flat[i] < 1 || _seaN[i] > 3 || _over[i] > VillageMaxOverhang) { return 0f; }
                return 5f + _flat[i] + jitter * 20f;
            }
            if (kind == PICK_LIGHTHOUSE)
            {
                if (LighthousePrefab == null || _seaN[i] < 3 || up > 200f) { return 0f; }
                return _seaN[i] * 5f + jitter * 10f;
            }
            if (kind == PICK_HIGHLAND)
            {
                if ((RadarPrefab == null && CommTowerPrefab == null) || !_peak[i] || up < HighlandMinHeight) { return 0f; }
                return up * 0.02f + jitter * 10f;
            }
            if (RuinsPrefab == null || up < RuinsMinHeight || _flat[i] < 2 || _over[i] > VillageMaxOverhang) { return 0f; }
            return 5f + jitter * 20f;
        }

        /// 같은 높이 칸 4개가 일렬로 이어지는 방향(0~2, 해시로 시작 방향을 돌린다). 없으면 -1.
        private int AirfieldDir(int i)
        {
            int start = Mathf.FloorToInt(Hash(i, 409, _jobSeed) * 2.999f);
            for (int k = 0; k < 3; k++)
            {
                int dir = (start + k) % 3;
                int c = i;
                bool ok = true;
                for (int step = 1; step < 4 && ok; step++)
                {
                    c = _nb[c * 6 + dir];
                    if (c < 0 || _taken[c] || _ch[c] < _sea || Mathf.Abs(_ch[c] - _ch[i]) > 0.5f || _over[c] > MaxOverhang) { ok = false; }
                }
                if (ok) { return dir; }
            }
            return -1;
        }

        private bool FarFromSites(int kind, float x, float z)
        {
            bool settlement = kind == PICK_CITY || kind == PICK_AIRFIELD || kind == PICK_VILLAGE;
            for (int s = 0; s < _siteCount; s++)
            {
                int other = _siteKind[s];
                bool otherSettlement = other == SITE_CITY || other == SITE_AIRFIELD || other == SITE_VILLAGE;
                float spacing = MinSiteGap;
                if (settlement && otherSettlement) { spacing = SettlementSpacing; }
                else if (!settlement && PickOfSite(other) == kind) { spacing = LandmarkSpacing; }
                spacing += _siteR[s];
                float dx = _siteX[s] - x;
                float dz = _siteZ[s] - z;
                if (dx * dx + dz * dz < spacing * spacing) { return false; }
            }
            return true;
        }

        private int PickOfSite(int siteKind)
        {
            if (siteKind == SITE_CITY || siteKind == SITE_PORT) { return PICK_CITY; }
            if (siteKind == SITE_AIRFIELD) { return PICK_AIRFIELD; }
            if (siteKind == SITE_VILLAGE) { return PICK_VILLAGE; }
            if (siteKind == SITE_LIGHTHOUSE) { return PICK_LIGHTHOUSE; }
            if (siteKind == SITE_RADAR || siteKind == SITE_TOWER) { return PICK_HIGHLAND; }
            return PICK_RUINS;
        }

        // ───────────── 자리 만들기 ─────────────

        /// 도시: 가운데 칸 + 같은 높이 이웃을 블록으로 덮고, 블록 칸이 바다에 닿으면 그 해안에 항구.
        private void AddCity(int center)
        {
            int site = AddSite(SITE_CITY, center, _hexR);
            float h = _ch[center];
            int blocks = 0;
            int portCell = -1;
            int portDir = -1;
            int startDir = Mathf.FloorToInt(Hash(center, 419, _jobSeed) * 5.999f);
            // k = -1: 가운데 칸(AddSite가 이미 차지 표시), 0~5: 이웃
            for (int k = -1; k < 6 && blocks < Mathf.Max(CityMaxBlocks, 1); k++)
            {
                int c = k < 0 ? center : _nb[center * 6 + (startDir + k) % 6];
                if (k >= 0 && (c < 0 || _taken[c] || _ch[c] < _sea || Mathf.Abs(_ch[c] - h) > FlatTolerance || _over[c] > MaxOverhang)) { continue; }
                AddBlock(c, PickVariant(CityBlockPrefabs, c), _hexR * 0.95f, Mathf.Floor(Hash(c, 421, _jobSeed) * 5.999f) * 60f);
                blocks++;
                if (portCell < 0 && PortPrefab != null && _seaN[c] > 0)
                {
                    int sd = SeaDir(c);
                    if (sd >= 0) { portCell = c; portDir = sd; }
                }
            }
            if (blocks > 1) { _siteR[site] = _hexR * 1.8f; }
            if (portCell >= 0) { AddPort(portCell, portDir, site); }
        }

        /// 항구: 블록 칸의 바다 쪽 변 가운데에서 경사를 따라 해수면에 닿는 곳(경사 치마 끝을 넘지 않게)에 바다를 보고 놓는다.
        private void AddPort(int cell, int dir, int citySite)
        {
            float ang = dir * 60f * Mathf.Deg2Rad;
            float dx = Mathf.Cos(ang);
            float dz = Mathf.Sin(ang);
            float drop = _ch[cell] - _sea;
            // 기둥 경사: 15°(tan ≈ 0.268)로 800m까지, 그 아래는 벽
            float run = Mathf.Min(drop / 0.268f, 800f);
            float edge = _hexR * 0.866f + run;
            float x = _origin.x + _cx[cell] + dx * edge;
            float z = _origin.z + _cz[cell] + dz * edge;
            if (_siteCount >= MAX_SITES) { return; }
            int s = _siteCount;
            _siteKind[s] = SITE_PORT;
            _siteX[s] = x;
            _siteY[s] = _sea;
            _siteZ[s] = z;
            _siteR[s] = 250f;
            _siteName[s] = _siteName[citySite] + PortLabel;
            _siteCount++;
            AddPlacement(PortPrefab, new Vector3(x, _sea, z), DirYaw(dir));
            AddBlockAt(x, z, 300f);
        }

        /// 비행장: 일렬 4칸 가운데에 활주로(방향 = 줄 방향, 시설 쪽은 해시로 뒤집기).
        private void AddAirfield(int first)
        {
            int dir = AirfieldDir(first);
            if (dir < 0) { return; }
            int site = AddSite(SITE_AIRFIELD, first, 1600f);
            int c = first;
            float sx = _cx[first];
            float sz = _cz[first];
            _taken[first] = true;
            AddBlockAt(_origin.x + _cx[first], _origin.z + _cz[first], _hexR * 0.95f);
            for (int step = 1; step < 4; step++)
            {
                c = _nb[c * 6 + dir];
                _taken[c] = true;
                AddBlockAt(_origin.x + _cx[c], _origin.z + _cz[c], _hexR * 0.95f);
            }
            float mx = (sx + _cx[c]) * 0.5f;
            float mz = (sz + _cz[c]) * 0.5f;
            _siteX[site] = _origin.x + mx;
            _siteZ[site] = _origin.z + mz;
            float yaw = DirYaw(dir) + (Hash(first, 431, _jobSeed) < 0.5f ? 180f : 0f);
            _siteYaw[site] = yaw;
            AddPlacement(AirfieldPrefab, new Vector3(_origin.x + mx, _ch[first], _origin.z + mz), yaw);
        }

        /// 등대: 바다 쪽으로 윗면 가장자리 가까이, 바다를 보고.
        private void AddLighthouse(int cell)
        {
            int dir = SeaDir(cell);
            if (dir < 0) { dir = 0; }
            float ang = dir * 60f * Mathf.Deg2Rad;
            float x = _origin.x + _cx[cell] + Mathf.Cos(ang) * _hexR * 0.55f;
            float z = _origin.z + _cz[cell] + Mathf.Sin(ang) * _hexR * 0.55f;
            int site = AddSite(SITE_LIGHTHOUSE, cell, 100f);
            _siteX[site] = x;
            _siteZ[site] = z;
            _taken[cell] = true;
            AddPlacement(LighthousePrefab, new Vector3(x, _ch[cell], z), DirYaw(dir));
            AddBlockAt(x, z, 80f);
        }

        /// 칸 가운데에 하나(마을/레이더/통신탑/유적). wholeCell이면 칸 전체를 막는다.
        private void AddSimple(int cell, int siteKind, GameObject prefab, float radius, bool wholeCell)
        {
            AddSite(siteKind, cell, radius);
            AddBlock(cell, prefab, wholeCell ? _hexR * 0.7f : radius, Mathf.Floor(Hash(cell, 439, _jobSeed) * 5.999f) * 60f);
        }

        private int AddSite(int kind, int cell, float radius)
        {
            int s = _siteCount;
            _siteKind[s] = kind;
            _siteX[s] = _origin.x + _cx[cell];
            _siteY[s] = _ch[cell];
            _siteZ[s] = _origin.z + _cz[cell];
            _siteR[s] = radius;
            _siteYaw[s] = 0f;
            _siteName[s] = SiteLabel(kind, MakeName(s));
            _siteCount++;
            _taken[cell] = true;
            return s;
        }

        /// 칸 윗면 가운데에 프리팹 하나 + 막기.
        private void AddBlock(int cell, GameObject prefab, float blockRadius, float yaw)
        {
            _taken[cell] = true;
            float x = _origin.x + _cx[cell];
            float z = _origin.z + _cz[cell];
            AddPlacement(prefab, new Vector3(x, _ch[cell], z), yaw);
            AddBlockAt(x, z, blockRadius);
        }

        private void AddPlacement(GameObject prefab, Vector3 pos, float yaw)
        {
            if (prefab == null || _plCount >= MAX_PLACE) { return; }
            _plPrefab[_plCount] = prefab;
            _plPos[_plCount] = pos;
            _plYaw[_plCount] = yaw;
            _plCount++;
        }

        private void AddBlockAt(float x, float z, float r)
        {
            if (_blockCount >= MAX_BLOCKS) { return; }
            _blockX[_blockCount] = x;
            _blockZ[_blockCount] = z;
            _blockR[_blockCount] = r;
            _blockCount++;
        }

        /// 바다(또는 범위 밖) 이웃 방향 하나(해시로 시작 방향을 돌린다). 없으면 -1.
        private int SeaDir(int cell)
        {
            int start = Mathf.FloorToInt(Hash(cell, 443, _jobSeed) * 5.999f);
            for (int k = 0; k < 6; k++)
            {
                int dir = (start + k) % 6;
                int n = _nb[cell * 6 + dir];
                if (n < 0 || _ch[n] < _sea) { return dir; }
            }
            return -1;
        }

        /// 이웃 방향(0~5, +X에서 +Z 쪽으로 60°씩)을 바라보는 Y 회전(+Z가 그 방향).
        private float DirYaw(int dir)
        {
            return 90f - dir * 60f;
        }

        private GameObject PickVariant(GameObject[] list, int cell)
        {
            if (list == null || list.Length == 0) { return null; }
            int k = Mathf.Clamp(Mathf.FloorToInt(Hash(cell, 449, _jobSeed) * list.Length), 0, list.Length - 1);
            return list[k];
        }

        // ───────────── 이름 ─────────────

        private string MakeName(int site)
        {
            // 목록이 비면 번호로(배너에 이름 대신 "SITE 3").
            if (NamePrefixes == null || NameSuffixes == null || NamePrefixes.Length == 0 || NameSuffixes.Length == 0) { return "SITE " + (site + 1); }
            string name = "";
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int a = Mathf.FloorToInt(Hash(site, 457 + attempt, _jobSeed) * NamePrefixes.Length) % NamePrefixes.Length;
                int b = Mathf.FloorToInt(Hash(site, 461 + attempt * 7, _jobSeed) * NameSuffixes.Length) % NameSuffixes.Length;
                name = NamePrefixes[a] + NameSuffixes[b];
                if (!NameUsed(name)) { break; }
            }
            return name;
        }

        private bool NameUsed(string name)
        {
            for (int s = 0; s < _siteCount; s++)
            {
                if (_siteName[s] != null && _siteName[s].Contains(name)) { return true; }
            }
            return false;
        }

        private string SiteLabel(int kind, string name)
        {
            if (kind == SITE_AIRFIELD) { return name + AirfieldLabel; }
            if (kind == SITE_LIGHTHOUSE) { return name + LighthouseLabel; }
            if (kind == SITE_RADAR) { return name + RadarLabel; }
            if (kind == SITE_TOWER) { return name + TowerLabel; }
            if (kind == SITE_RUINS) { return RuinsLabel + name; }
            return name;
        }

        // ───────────── 3. 놓기 ─────────────

        private void SpawnStep()
        {
            int end = Mathf.Min(_index + Mathf.Max(ObjectsPerFrame, 1), _plCount);
            for (int p = _index; p < end; p++)
            {
                GameObject go = TakeInstance(_plPrefab[p]);
                if (go == null) { continue; }
                go.transform.SetPositionAndRotation(_plPos[p], Quaternion.Euler(0f, _plYaw[p], 0f));
                if (!go.activeSelf) { go.SetActive(true); }
            }
            _index = end;
            if (end < _plCount) { return; }
            _placed = true;
            _placedSeed = _jobSeed;
            _stage = STAGE_DONE;
            // 장식이 놓인 칸(도시 블록·마을·비행장·등대·레이더 등)은 나무를 끈다 — 칸 수만큼 한 번 돈다.
            if (Terrain != null)
            {
                for (int i = 0; i < _cellCount; i++) { if (_taken[i]) { Terrain.SetCellTrees(i, false); } }
            }
            Debug.Log("[ACO] 지형 장식 완성: 자리 " + _siteCount + "곳, 장식 " + _plCount + "개");
        }

        /// 이 프리팹의 안 쓰는 인스턴스를 꺼내거나 새로 만든다.
        private GameObject TakeInstance(GameObject prefab)
        {
            for (int i = 0; i < _instCount; i++)
            {
                if (_instUsed[i] || _instSrc[i] != prefab || _inst[i] == null) { continue; }
                _instUsed[i] = true;
                return _inst[i];
            }
            if (_instCount >= MAX_INST) { return null; }
            GameObject go = Object.Instantiate(prefab);
            go.transform.SetParent(transform, true);
            _inst[_instCount] = go;
            _instSrc[_instCount] = prefab;
            _instUsed[_instCount] = true;
            _instCount++;
            return go;
        }

        // ───────────── 생성기/관리자/UI가 부른다 ─────────────

        public bool HasSites() { return _placed && _siteCount > 0; }
        public int GetSiteCount() { return _placed ? _siteCount : 0; }
        public int GetSiteKind(int i) { return _siteKind[i]; }
        public Vector3 GetSitePosition(int i) { return new Vector3(_siteX[i], _siteY[i], _siteZ[i]); }
        public float GetSiteRadius(int i) { return _siteR[i]; }
        /// 비행장 활주로 방향(Y 회전, 도). 활주로 축 = (sin, 0, cos), 가운데 = GetSitePosition, 표면 = 그 높이 + 0.5m, 길이 2.4km.
        public float GetSiteYaw(int i) { return _siteYaw[i]; }
        public string GetSiteName(int i) { return (_placed && i >= 0 && i < _siteCount) ? _siteName[i] : ""; }

        /// (x, z)가 건물 자리 안이면 true — 생성기가 여기에 유닛을 놓지 않는다.
        public bool IsBlocked(float x, float z)
        {
            if (!_placed) { return false; }
            for (int i = 0; i < _blockCount; i++)
            {
                float dx = _blockX[i] - x;
                float dz = _blockZ[i] - z;
                if (dx * dx + dz * dz < _blockR[i] * _blockR[i]) { return true; }
            }
            return false;
        }

        /// 적이 주둔할 만한 자리(도시/마을/비행장/레이더/통신탑) 중 focus에서 minDist~maxDist(maxDist ≤ 0이면 어디든)인 곳을
        /// 무작위로 골라 그 둘레(가장자리에서 GarrisonDistanceMin~Max) 한 점을 GetPickX/Z에 둔다. 지면 확인은 생성기가 한다.
        public bool PickSitePoint(Vector3 focus, float minDist, float maxDist)
        {
            if (!_placed) { return false; }
            int n = 0;
            for (int s = 0; s < _siteCount; s++)
            {
                int k = _siteKind[s];
                if (k == SITE_PORT || k == SITE_LIGHTHOUSE || k == SITE_RUINS) { continue; }
                if (maxDist > 0f)
                {
                    float dx = _siteX[s] - focus.x;
                    float dz = _siteZ[s] - focus.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < minDist || d > maxDist) { continue; }
                }
                _pickList[n] = s;
                n++;
            }
            if (n == 0) { return false; }
            int site = _pickList[Random.Range(0, n)];
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = _siteR[site] + Random.Range(GarrisonDistanceMin, Mathf.Max(GarrisonDistanceMax, GarrisonDistanceMin));
            _pickX = _siteX[site] + Mathf.Cos(a) * r;
            _pickZ = _siteZ[site] + Mathf.Sin(a) * r;
            return true;
        }

        public float GetPickX() { return _pickX; }
        public float GetPickZ() { return _pickZ; }

        /// (x, z)에서 가장자리까지 maxDist 안에 있는 가장 가까운 자리(이름 붙일 작전 지역). 없으면 -1.
        public int FindSiteNear(float x, float z, float maxDist)
        {
            if (!_placed) { return -1; }
            int best = -1;
            float bestD = maxDist;
            for (int s = 0; s < _siteCount; s++)
            {
                float dx = _siteX[s] - x;
                float dz = _siteZ[s] - z;
                float d = Mathf.Sqrt(dx * dx + dz * dz) - _siteR[s];
                if (d <= bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// 정수 해시(0~1) — 지형과 같은 식(모든 클라이언트에서 같다).
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
