using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 레이더 접촉 목록(로컬 전용, 동기화 없음, 씬에 하나 — HUD 레이더와 같은 오브젝트). HUD 레이더(ACO_HudRadar)와 조종석 지도(ACO_CockpitMap)가 같이 읽는다.
    /// - 유닛: 생성기 풀(전투기·지상·고가치 표적·호위 대상)과 게임 진행의 참가자 기체를 처음에 한 번 훑어 둔다. RefreshInterval마다 '배치됐고 살아 있는가'와
    ///   종류(적/아군 × 공중/지상, 페이즈 목표)를 다시 정한다. 위치는 그리는 쪽이 트랜스폼을 바로 읽는다(이미 동기화돼 오는 값 — 네트워크 추가 없음).
    /// - 미사일: NPC 런처(ACO_MissileLauncher)가 쏠 때 AddMissile로 등록하고, 날고 있는 동안만 보인다(터지거나 회수되면 빠진다).
    /// - LocalVehicle: 내가 탄 참가자 기체(없으면 null). 아무 기체에도 타지 않았고 미리보기도 아니면 쉰다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_RadarContacts : UdonSharpBehaviour
    {
        public const int KIND_ENEMY_AIR = 0;
        public const int KIND_ENEMY_GROUND = 1;
        public const int KIND_FRIENDLY_AIR = 2;
        public const int KIND_FRIENDLY_GROUND = 3;
        public const int KIND_OBJECTIVE_AIR = 4;
        public const int KIND_OBJECTIVE_GROUND = 5;
        public const int MAX_MISSILES = 32;

        private const int SRC_PLAYER = 0;
        private const int SRC_FIGHTER = 1;
        private const int SRC_GROUND = 2;
        private const int SRC_HVT = 3;
        private const int SRC_ESCORT = 4;

        [Header("참조 (빌드 시 ACO_SceneBaker가 넣는다)")]
        public ACO_MissionGenerator Generator;
        public ACO_GameFlow Flow;

        [Tooltip("배치·격파·종류를 다시 보는 간격(초). 위치는 그리는 쪽이 매번 읽는다")]
        public float RefreshInterval = 0.5f;

        // 그리는 쪽이 읽는다(Count까지)
        [System.NonSerialized] public int Count;
        [System.NonSerialized] public Transform[] Targets;
        [System.NonSerialized] public int[] Kinds;
        [System.NonSerialized] public bool[] Visible;
        [System.NonSerialized] public int MissileCount;
        [System.NonSerialized] public Transform[] Missiles = new Transform[MAX_MISSILES];
        [System.NonSerialized] public SaccEntity LocalVehicle;

        private int[] _source;
        private int[] _index;
        private bool[] _air;
        private SaccTarget[] _health;
        private ACO_Missile[] _missileScripts = new ACO_Missile[MAX_MISSILES];
        private bool _built;
        private float _timer;
        private float _wakeUntil;

        void Start() { Build(); }

        /// 그리는 쪽이 미리보기 등으로 기체 밖에서도 seconds 동안 목록을 갱신하게 한다.
        public void Wake(float seconds) { _wakeUntil = Mathf.Max(_wakeUntil, Time.time + seconds); }

        /// NPC 런처가 미사일을 쏜 직후 부른다.
        public void AddMissile(ACO_Missile missile)
        {
            if (missile == null) { return; }
            for (int i = 0; i < MissileCount; i++) { if (_missileScripts[i] == missile) { return; } }
            if (MissileCount >= MAX_MISSILES) { PruneMissiles(); }
            if (MissileCount >= MAX_MISSILES) { return; }
            _missileScripts[MissileCount] = missile;
            Missiles[MissileCount] = missile.transform;
            MissileCount++;
        }

        // ───────────── 목록 ─────────────

        private void Build()
        {
            if (_built) { return; }
            _built = true;
            SaccEntity[] aircraft = Flow != null ? Flow.Aircraft : null;
            int players = aircraft != null ? aircraft.Length : 0;
            int fighters = Generator != null && Generator.Fighters != null ? Generator.Fighters.Length : 0;
            int ground = Generator != null && Generator.GroundUnits != null ? Generator.GroundUnits.Length : 0;
            int hvts = Generator != null && Generator.HighValueTargets != null ? Generator.HighValueTargets.Length : 0;
            int escorts = Generator != null && Generator.EscortUnits != null ? Generator.EscortUnits.Length : 0;
            int total = players + fighters + ground + hvts + escorts;
            Targets = new Transform[total];
            Kinds = new int[total];
            Visible = new bool[total];
            _source = new int[total];
            _index = new int[total];
            _air = new bool[total];
            _health = new SaccTarget[total];
            Count = 0;
            for (int i = 0; i < players; i++) { if (aircraft[i]) { Add(SRC_PLAYER, i, aircraft[i].transform, true, null); } }
            for (int i = 0; i < fighters; i++)
            {
                ACO_NPCFighterAI f = Generator.Fighters[i];
                if (f) { Add(SRC_FIGHTER, i, f.transform, true, f.TargetHealth); }
            }
            for (int i = 0; i < ground; i++)
            {
                UdonSharpBehaviour g = Generator.GroundUnits[i];
                if (!g) { continue; }
                // 지상 풀의 헬리콥터는 공중
                ACO_MobileUnit tank = g.GetComponent<ACO_MobileUnit>();
                bool heli = tank != null && tank.Kind == ACO_MobileUnit.KIND_HELICOPTER;
                Add(SRC_GROUND, i, g.transform, heli, g.GetComponent<SaccTarget>());
            }
            for (int i = 0; i < hvts; i++)
            {
                UdonSharpBehaviour h = Generator.HighValueTargets[i];
                if (h) { Add(SRC_HVT, i, h.transform, false, h.GetComponent<SaccTarget>()); }
            }
            for (int i = 0; i < escorts; i++)
            {
                ACO_EscortUnit e = Generator.EscortUnits[i];
                if (e) { Add(SRC_ESCORT, i, e.transform, e.MoveMode == ACO_EscortUnit.MOVE_AIR, e.TargetHealth); }
            }
        }

        private void Add(int source, int index, Transform t, bool air, SaccTarget health)
        {
            int k = Count;
            _source[k] = source;
            _index[k] = index;
            _air[k] = air;
            _health[k] = health;
            Targets[k] = t;
            Kinds[k] = air ? KIND_ENEMY_AIR : KIND_ENEMY_GROUND;
            Visible[k] = false;
            Count++;
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) { return; }
            _timer = RefreshInterval;
            Build();
            LocalVehicle = FindLocalVehicle();
            if (LocalVehicle == null && Time.time > _wakeUntil) { return; }
            for (int k = 0; k < Count; k++) { RefreshContact(k); }
            PruneMissiles();
        }

        private SaccEntity FindLocalVehicle()
        {
            if (Flow == null || Flow.Aircraft == null) { return null; }
            SaccEntity[] a = Flow.Aircraft;
            for (int i = 0; i < a.Length; i++) { if (a[i] != null && a[i].InVehicle) { return a[i]; } }
            return null;
        }

        private void RefreshContact(int k)
        {
            Transform t = Targets[k];
            int src = _source[k];
            if (t == null || !t.gameObject.activeInHierarchy) { Visible[k] = false; return; }
            if (src == SRC_PLAYER)
            {
                // 다른 플레이어가 탄 참가자 기체만(내 기체는 그리는 쪽이 따로 그린다)
                SaccEntity e = Flow.Aircraft[_index[k]];
                Visible[k] = e != null && e.Occupied && e != LocalVehicle;
                Kinds[k] = KIND_FRIENDLY_AIR;
                return;
            }
            int cat = Category(src);
            if (Generator == null || !Generator.IsUnitUsed(cat, _index[k])) { Visible[k] = false; return; }
            SaccTarget h = _health[k];
            if (h != null && h.Health <= 0f) { Visible[k] = false; return; }
            Visible[k] = true;
            bool friendly = src == SRC_ESCORT && !Generator.EscortUnits[_index[k]].Hostile;
            bool objective = !friendly && Generator.GetUnitTag(cat, _index[k]) != 0;
            if (friendly) { Kinds[k] = _air[k] ? KIND_FRIENDLY_AIR : KIND_FRIENDLY_GROUND; }
            else if (objective) { Kinds[k] = _air[k] ? KIND_OBJECTIVE_AIR : KIND_OBJECTIVE_GROUND; }
            else { Kinds[k] = _air[k] ? KIND_ENEMY_AIR : KIND_ENEMY_GROUND; }
        }

        private int Category(int src)
        {
            if (src == SRC_FIGHTER) { return ACO_MissionGenerator.CAT_FIGHTER; }
            if (src == SRC_GROUND) { return ACO_MissionGenerator.CAT_GROUND; }
            if (src == SRC_HVT) { return ACO_MissionGenerator.CAT_HVT; }
            return ACO_MissionGenerator.CAT_ESCORT;
        }

        /// 날고 있는 미사일만 남긴다(터졌거나 회수됐으면 뺀다).
        public void PruneMissiles()
        {
            for (int i = MissileCount - 1; i >= 0; i--)
            {
                ACO_Missile m = _missileScripts[i];
                if (m != null && m.gameObject.activeInHierarchy && m.IsFlying()) { continue; }
                int last = MissileCount - 1;
                _missileScripts[i] = _missileScripts[last];
                Missiles[i] = Missiles[last];
                _missileScripts[last] = null;
                Missiles[last] = null;
                MissileCount = last;
            }
        }

        /// 그리는 쪽이 미사일 하나를 그려도 되는지(목록 갱신 사이에 터진 것을 거른다).
        public bool IsMissileFlying(int i)
        {
            if (i < 0 || i >= MissileCount) { return false; }
            ACO_Missile m = _missileScripts[i];
            return m != null && m.gameObject.activeInHierarchy && m.IsFlying();
        }
    }
}
