using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 육상전함 페이즈(모듈) — 거대 지상 보스 ACO_LandShip이 지상 항로를 따라 도시로 다가온다. 콘보이처럼 목적지(도시 가장자리)에서 거꾸로 만든 항로를 서버 시간으로 간다.
    ///  1) 장갑: 스커트를 부수면 그 뒤 궤도가, 궤도를 부수면 그 뒤 배기구(가운데 궤도 8개)가 드러난다. 궤도가 부서질 때마다 TrackSlowdown씩 느려진다(최저 MinSpeedScale).
    ///     앞뒤 끝 궤도 4개는 선택 목표다(배기구가 없다 — 부수면 느려지기만 한다).
    ///  2) 코어: 배기구를 모두 부수면 셔터가 열리고 코어가 드러난다. 코어를 부수면 성공, 도시(항로 끝)에 닿으면 미션 실패.
    /// 항로를 못 만들면(선체가 크게 파묻히거나 뜨는 지형) 상륙전처럼 바다에서 해안으로 들어온다(생성기 FindLandShipRoute).
    ///
    /// 자체 동기화(Manual): 단계, 항로 경유점, 기준 거리·시각·속도 배율, 목적지 자리. 위치 자체는 보내지 않는다.
    /// 값은 시작·궤도 파괴·단계 전환 때만 보낸다. 판정은 관리자 오너만. 페이즈 도장으로 지난 값을 무시한다.
    /// 대사: 공중 요새와 같은 상황별 대사 표(LineEvent·LineSpeaker·LineText·LineClip). ACO_LandShip이 0.5초마다 CheckLines.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseLandShip : ACO_PhaseBoss
    {
        public const int MAX_ROUTE = 6;

        [Header("육상전함")]
        [Tooltip("항로 길이(m) — 최고 속도(ACO_LandShip.Speed)로 가면 이 길이 / 속도 초")]
        public float RouteLength = 6000f;
        [Tooltip("이 페이즈가 시작될 때 미션 제한 시간에 더하는 시간(초)")]
        public float ExtraTime = 720f;
        [Tooltip("궤도 하나가 부서질 때마다 줄어드는 속도 비율")]
        [Range(0f, 0.3f)] public float TrackSlowdown = 0.07f;
        [Tooltip("최저 속도 배율")]
        [Range(0.1f, 1f)] public float MinSpeedScale = 0.4f;
        [Tooltip("방공·부포 부위 수리 간격(초). 주포는 따로 ACO_LandShip.GunRepairDelay마다 되살아난다")]
        public float RepairInterval = 40f;
        [Tooltip("한 번에 되살리는 방공·부포 부위 수")]
        public int RepairCount = 3;
        [Tooltip("수리는 살아 있는 방공·부포 부위가 이 수보다 적을 때만, 이 수까지(한 번에 RepairCount곳). 0이면 한도 없이 RepairCount곳씩 — 너무 많이 되살아나면 부순 의미가 없다")]
        public int RepairAliveCap = 6;
        [HideInInspector] public int MaxRepairs = 6;   // 예전 값(페이즈 전체 합계 — 이제 쓰지 않는다, 저장된 값만 남긴다)
        [Tooltip("남은 거리(m)가 이만큼이면 도시 접근 대사 (먼·가까운·마지막)")]
        public float CityWarnFar = 3000f;
        public float CityWarnNear = 1500f;
        public float CityWarnLast = 500f;

        [Header("끝맺음")]
        [Tooltip("코어가 부서진 뒤 성공으로 넘어가기까지(초) — 연쇄 폭발과 대폭발이 보이게")]
        public float ClearDelay = 8f;
        [Tooltip("도시에 닿은 뒤 미션 실패로 넘어가기까지(초)")]
        public float CityFailDelay = 4f;
        [Header("총력 복구 (코어 단계에 한 번 — 자동 사격 통제)")]
        [Tooltip("코어 셔터가 열린 뒤 이만큼(초) 지나 부서진 포대를 전부 되살린다 — 셔터가 열릴 때의 대사(약 9초)가 끝난 뒤. 0 = 끔")]
        public float AllRepairDelay = 10f;
        [Tooltip("되살리기를 이만큼(초)에 걸쳐 차례로(한꺼번에 툭 생기지 않게)")]
        public float AllRepairSpread = 4f;

        public const int SIT_SPAWN = 0;
        public const int SIT_SKIRT_DOWN = 1;
        public const int SIT_TRACK_HALF = 2;   // 궤도 절반 파괴(한 번)
        public const int SIT_VENT_OPEN = 3;
        public const int SIT_VENT_HALF = 4;
        public const int SIT_CORE_OPEN = 5;
        public const int SIT_CORE_HALF = 6;
        public const int SIT_CITY_FAR = 7;
        public const int SIT_CITY_NEAR = 8;
        public const int SIT_CITY_LAST = 9;
        public const int SIT_CORE_DOWN = 10;
        public const int SIT_CITY_HIT = 11;
        public const int SIT_REPAIR = 12;      // 방공·부포 부위가 되살아났다(장갑 단계 — 수리반)
        public const int SIT_ALL_REPAIR = 13;  // 총력 복구 — 선원이 무력화돼 자동 사격 통제로 바꾸고 포대를 전부 되살린다
        public const int SIT_REPAIR_AUTO = 14; // 총력 복구 뒤의 주기 수리(자동 사통 — 시스템 음성)

        [Header("상황별 대사 표 (같은 순서의 배열 — 한 줄 = 대사 하나) — 3판: 적 함장은 1판 대사, AWACS는 2판 대사(6-150)")]
        [Tooltip("이 대사가 나오는 상황(SIT_*)")]
        public int[] LineEvent = new int[] {
            SIT_SPAWN,
            SIT_SKIRT_DOWN,
            SIT_TRACK_HALF,
            SIT_TRACK_HALF,
            SIT_VENT_OPEN,
            SIT_VENT_OPEN,
            SIT_CORE_OPEN,
            SIT_CORE_OPEN,
            SIT_ALL_REPAIR,
            SIT_ALL_REPAIR,
            SIT_CORE_HALF,
            SIT_CORE_HALF,
            SIT_CITY_FAR,
            SIT_CITY_NEAR,
            SIT_CITY_LAST,
            SIT_CITY_LAST,
            SIT_CORE_DOWN,
            SIT_CORE_DOWN,
            SIT_CITY_HIT,
            SIT_CITY_HIT,
            SIT_REPAIR,
            SIT_REPAIR,
            SIT_REPAIR_AUTO };
        [Tooltip("말하는 쪽(0 = 적 함장, 1 = AWACS, 3 = 선원, 4 = 자동 사통 시스템)")]
        public int[] LineSpeaker = new int[] {
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS };
        [Tooltip("대사. {left} = 남은 궤도 수(궤도 절반 파괴 대사)")]
        public string[] LineText = new string[] {
            "All batteries, open fire. Grind a path to the city.",
            "Armor plate down! The track behind it is exposed!",
            "Half our tracks are gone! Keep moving, full power to the rest!",
            "Half its tracks are destroyed! It is losing speed. {left} tracks left!",
            "Exhaust vents exposed! They're tucked inside the track wells, glowing blue. Destroy them and cripple that thing!",
            "Exhaust ducts exposed! Seal them, now!",
            "The core is exposed! Hit it with everything you have!",
            "Reactor overheating! Open the core shutters!",
            "Reroute reactor power to the batteries. All of them. Now!",
            "It's feeding reactor power to its guns! Every battery is back! Stay on the core!",
            "Core integrity at fifty percent! Hold together!",
            "The core is failing! Keep hitting it!",
            "Three kilometers to the city!",
            "It is almost at the city limits! Stop it now!",
            "Nothing can stop us now!",
            "Five hundred meters! Bring it down!",
            "Keep firing! Keep fir-",
            "Core destroyed! The battleship is going up!",
            "We have reached the city. Commence bombardment.",
            "It is inside the city... It is shelling the city!",
            "Damage control, bring the batteries back online!",
            "Some of its guns are back online. Watch yourselves!",
            "Some of its guns are back online. Watch yourselves!" };
        [Tooltip("음성(선택 — 비우면 자막만)")]
        public AudioClip[] LineClip = new AudioClip[23];
        [Tooltip("코어 폭발 첫 대사 음성에서 말이 끊기는 시각(초) — 이 지점이 대폭발(ACO_LandShip.DeathChainTime)에 맞게 늦춰 튼다. 그 뒤는 비명. 0이면 음성 끝")]
        public float CoreDownCutAt = 2.16f;

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _stage;
        [UdonSynced] private int _routeCount;
        [UdonSynced] private Vector3[] _route = new Vector3[MAX_ROUTE];
        [UdonSynced] private int _baseMs;
        [UdonSynced] private float _baseDist;
        [UdonSynced] private float _speedScale = 1f;
        [UdonSynced] private int _destSite = -1;
        [UdonSynced] private int _repairedUnits;   // 지금까지 되살린 방공·부포 부위 수(MaxRepairs와 비교 — 새 오너도 이어서 센다)
        [UdonSynced] private int _allRepairMs;     // 총력 복구를 시작한 서버 시각(0 = 아직 — 페이즈에 한 번)

        // 오너 지역 상태
        private Vector3[] _plan = new Vector3[MAX_ROUTE];
        private int _planCount;
        private int _planSite = -1;
        private int _tracksDown = -1;
        private int _coreDownMs;
        private int _arriveMs;
        private bool _released;
        private int _nextRepairMs;
        private int _coreStageMs;   // 코어 단계가 된 시각(오너 지역 — 새 오너는 처음 본 때부터)
        private int _allRepairTotal;
        private int _allRepairDone;
        private int[] _gunDownMs = new int[0];   // 주포가 고장 난 걸 본 시각(0 = 멀쩡, 오너 지역 상태 — 오너가 바뀌면 처음부터 잰다)

        // 대사(모든 클라이언트 지역 상태)
        private int _lineStamp;
        private int _saidStage;
        private int _saidSkirts;
        private bool _saidTrackHalf;
        private int _saidRepairs;
        private int _saidVentsOpen;
        private bool _saidVentHalf;
        private bool _saidCoreHalf;
        private int _saidCity;
        private bool _saidAllRepair;

        private ACO_LandShip Ship() { return Generator != null ? Generator.LandShip : null; }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null || !Generator.CanDeployLandShip()) { return false; }
            if (!Generator.FindLandShipRoute(RouteLength)) { return false; }
            _planCount = Mathf.Min(Generator.GetLandShipRouteCount(), MAX_ROUTE);
            for (int i = 0; i < _planCount; i++) { _plan[i] = Generator.GetLandShipRoutePoint(i); }
            _planSite = Generator.GetLandShipRouteSite();
            return _planCount >= 2;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            Manager.SetModuleProgress(0, 1, false);
            // 대체 시작(PickFallbackType)은 IsFeasible을 거치지 않는다 — 여기서 다시 확인한다. 안 되면 Tick이 배치 시한 뒤 RESULT_REPLACE.
            if (_planCount < 2 && !IsFeasible(phaseIndex, focus)) { return; }
            Vector3 start = _plan[0];
            Vector3 head = _plan[1] - start;
            int yaw = Mathf.RoundToInt(Mathf.Atan2(head.x, head.z) * Mathf.Rad2Deg);
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_LANDSHIP, tag, 1, yaw, start, 0f, 0f);
            if (_planSite >= 0) { Manager.SetModuleSite(_planSite); }
            Manager.AddModuleTime(ExtraTime);
            StartRoute(now, _plan, _planCount, _planSite);
        }

        private void StartRoute(int now, Vector3[] pts, int count, int site)
        {
            _stamp = CurrentStamp();
            _stage = ACO_LandShip.STAGE_ARMOR;
            _routeCount = count;
            for (int i = 0; i < MAX_ROUTE; i++) { _route[i] = i < count ? pts[i] : Vector3.zero; }
            _baseMs = now;
            _baseDist = 0f;
            _speedScale = 1f;
            _destSite = site;
            _tracksDown = -1;
            _coreDownMs = 0;
            _arriveMs = 0;
            _released = false;
            _repairedUnits = 0;
            _allRepairMs = 0;
            _coreStageMs = 0;
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            SyncNow();
        }

        /// 이번 페이즈 값을 못 받은 새 오너: 지금 자리에서 도시(없으면 앞) 쪽으로 곧은 항로를 다시 만든다(드문 경우).
        private void RecoverRoute(int now, ACO_LandShip ship)
        {
            Vector3 from = ship.transform.position;
            Vector3 to = from + ship.transform.forward * RouteLength;
            int site = Generator.FindCitySite(from);
            if (site >= 0) { to = Generator.GetSitePosition(site); }
            Vector3[] pts = new Vector3[2];
            pts[0] = from;
            pts[1] = to;
            StartRoute(now, pts, 2, site);
        }

        public override int Tick(int now)
        {
            ACO_LandShip ship = Ship();
            if (ship == null) { return RESULT_REPLACE; }
            int tag = Manager.GetModuleTag();
            Manager.CountTaggedObjectives(tag);
            int total = Manager.GetTaggedTotal();
            if (_stamp != CurrentStamp())
            {
                if (total == 0) { return now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f) ? RESULT_REPLACE : RESULT_RUNNING; }
                RecoverRoute(now, ship);
            }

            if (total > 0 && Manager.GetTaggedDone() >= total)
            {
                // 격파: 연쇄 폭발·대폭발이 보이게 기다린다. 기다린 시간은 돌려준다.
                if (_coreDownMs == 0) { _coreDownMs = now; }
                if (now - _coreDownMs < Mathf.RoundToInt(ClearDelay * 1000f)) { return RESULT_RUNNING; }
                Manager.AddModuleTime((now - _coreDownMs) * 0.001f);
                Generator.RequestReleaseTag(tag);
                return RESULT_CLEAR;
            }
            // 도시 도착: 격파 판정 뒤에 본다. 거둔 뒤엔 total이 0이 되므로 배치 대기보다 앞에 둔다
            float length = ship.GetRouteLength();
            if (_arriveMs == 0 && total > 0 && length > 0f && ship.GetRouteDistance(now) >= length - 1f) { _arriveMs = now; }
            if (_arriveMs != 0)
            {
                bool fail = now - _arriveMs >= Mathf.RoundToInt(CityFailDelay * 1000f);
                if (fail && !_released)
                {
                    _released = true;
                    Generator.RequestReleaseTag(tag);
                }
                return fail ? RESULT_FAIL : RESULT_RUNNING;
            }
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            // 배치 직후엔 부위 체력이 부활 동기화 전이라 0으로 보인다 — 그동안 판정하지 않는다
            if (now - Manager.GetPhaseStartMs() < Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f)) { return RESULT_RUNNING; }

            TickGunRepair(now, ship);
            TickRepair(now, ship);
            TickAllRepair(now, ship);

            // 궤도가 부서졌다: 지금 거리에서 기준을 다시 잡아 느려진다(자리가 이어진다)
            int down = ship.CountTracksDestroyed();
            if (down != _tracksDown)
            {
                if (_tracksDown >= 0 || down > 0)
                {
                    _baseDist = ship.GetRouteDistance(now);
                    _baseMs = now;
                    _speedScale = Mathf.Max(MinSpeedScale, 1f - TrackSlowdown * down);
                    SyncNow();
                }
                _tracksDown = down;
            }

            int vents = ship.GetVentTotal();
            int ventsDown = ship.CountVentsDestroyed();
            if (_stage == ACO_LandShip.STAGE_ARMOR)
            {
                Manager.SetModuleProgress(ventsDown, Mathf.Max(vents, 1), false);
                if (vents > 0 && ventsDown >= vents)
                {
                    _stage = ACO_LandShip.STAGE_CORE;
                    _coreStageMs = now;
                    SyncNow();
                    Debug.Log("[ACO] 육상전함: 배기구 " + ventsDown + "/" + vents + " 파괴 — 코어 단계 (페이즈 " + ((now - Manager.GetPhaseStartMs()) / 1000) + "초)");
                }
                return RESULT_RUNNING;
            }
            Manager.SetModuleProgress(Mathf.RoundToInt((1f - ship.GetCoreHealthRatio()) * 100f), 100, false);
            return RESULT_RUNNING;
        }

        /// 방공·부포 부위는 RepairInterval마다 RepairCount곳씩, 페이즈 전체 MaxRepairs곳까지 되살린다(본체가 살아 있을 때만).
        private void TickRepair(int now, ACO_LandShip ship)
        {
            if (RepairInterval <= 0f || now - _nextRepairMs < 0 || ship.IsCoreDestroyed()) { return; }
            _nextRepairMs = now + Mathf.RoundToInt(RepairInterval * 1000f);
            // 살아 있는 방공 부위가 RepairAliveCap보다 적을 때만, 그 수까지(페이즈 전체 합계 한도는 없다 — 6-150)
            int count = RepairAliveCap > 0 ? Mathf.Min(RepairCount, RepairAliveCap - ship.CountAirDefenseAlive()) : RepairCount;
            if (count <= 0) { return; }
            int done = ship.RepairAirDefense(count);
            if (done <= 0) { return; }
            _repairedUnits += done;
            SyncNow();
        }

        /// 총력 복구(6-146): 코어 셔터가 열리고 AllRepairDelay초 뒤 한 번 — 선원이 무력화돼 자동 사격 통제로 바뀌며 부서진 포대(방공·부포·주포)를
        /// AllRepairSpread초에 걸쳐 차례로 전부 되살린다. 주기 수리 한도(MaxRepairs)와 따로 센다. 장갑(스커트·궤도·배기구)은 그대로.
        private void TickAllRepair(int now, ACO_LandShip ship)
        {
            if (AllRepairDelay <= 0f || _stage != ACO_LandShip.STAGE_CORE || ship.IsCoreDestroyed()) { return; }
            if (_allRepairMs == 0)
            {
                if (_coreStageMs == 0) { _coreStageMs = now; }   // 코어 단계 중에 오너가 바뀌었다
                if (now - _coreStageMs < Mathf.RoundToInt(AllRepairDelay * 1000f)) { return; }
                _allRepairMs = now;
                _allRepairDone = 0;
                _allRepairTotal = ship.CountAirDefenseDown();
                int guns = ship.GetRepairGunCount();
                for (int i = 0; i < guns; i++) { if (ship.IsGunDown(i)) { _allRepairTotal++; } }
                SyncNow();
                Debug.Log("[ACO] 육상전함: 총력 복구 — 포대 " + _allRepairTotal + "곳을 " + AllRepairSpread + "초에 걸쳐 되살린다");
            }
            if (_allRepairDone >= _allRepairTotal) { return; }
            float spread = Mathf.Max(AllRepairSpread, 0.1f);
            float f = Mathf.Clamp01((now - _allRepairMs) / (spread * 1000f));
            int want = Mathf.CeilToInt(_allRepairTotal * f) - _allRepairDone;
            if (want <= 0) { return; }
            // 주포 먼저, 그다음 방공·부포(무작위 자리부터)
            int guns2 = ship.GetRepairGunCount();
            for (int i = 0; i < guns2 && want > 0; i++)
            {
                if (!ship.IsGunDown(i)) { continue; }
                ship.RepairGun(i);
                if (i < _gunDownMs.Length) { _gunDownMs[i] = now; }
                want--;
                _allRepairDone++;
            }
            if (want > 0)
            {
                int done = ship.RepairAirDefense(want);
                _allRepairDone += done;
                if (done < want) { _allRepairDone = _allRepairTotal; }   // 더 되살릴 게 없다
            }
        }

        /// 고장 난 주포는 GunRepairDelay초 뒤 되살린다(본체가 살아 있을 때만).
        private void TickGunRepair(int now, ACO_LandShip ship)
        {
            int n = ship.GetRepairGunCount();
            if (_gunDownMs.Length != n) { _gunDownMs = new int[n]; }
            if (ship.IsCoreDestroyed()) { return; }
            int delay = Mathf.RoundToInt(ship.GunRepairDelay * 1000f);
            for (int i = 0; i < n; i++)
            {
                if (!ship.IsGunDown(i)) { _gunDownMs[i] = 0; continue; }
                if (_gunDownMs[i] == 0) { _gunDownMs[i] = now; continue; }
                if (now - _gunDownMs[i] < delay) { continue; }
                ship.RepairGun(i);
                _gunDownMs[i] = now;   // 체력이 돌아오기 전에 다시 보내지 않게 시계를 다시 건다
            }
        }

        public override void End()
        {
            _planCount = 0;
            _planSite = -1;
            _tracksDown = -1;
            _coreDownMs = 0;
            _arriveMs = 0;
            _released = false;
        }

        /// 격파·도시 도착 뒤 결과를 기다리는 중 — 이때는 제한 시간으로 실패하지 않는다.
        public override bool HoldsResult() { return _coreDownMs != 0 || _arriveMs != 0; }

        // ───────────── 대사 (모든 클라이언트) ─────────────

        /// 동기화된 상태·부위 체력이 바뀌었으면 그 상황의 대사를 재생한다 — ACO_LandShip이 0.5초마다 부른다.
        public void CheckLines(ACO_LandShip ship)
        {
            if (Chatter == null || !IsLive()) { return; }
            int skirts = ship.CountSkirtsDestroyed();
            int tracks = ship.CountTracksDestroyed();
            int ventsOpen = 0;
            int vents = ship.GetVentTotal();
            int ventsDown = ship.CountVentsDestroyed();
            float core = ship.GetCoreHealthRatio();
            int cityLevel = CityLevel(ship);
            // 드러난 배기구 수 = 부서진 궤도 중 배기구가 있는 칸(배기구 수를 넘지 않는다)
            if (ship.VentTrack != null && ship.Tracks != null)
            {
                for (int i = 0; i < ship.VentTrack.Length; i++)
                {
                    int t = ship.VentTrack[i];
                    if (t >= 0 && t < ship.Tracks.Length && ship.Tracks[t] != null && ship.Tracks[t].IsDestroyed()) { ventsOpen++; }
                }
            }
            if (_lineStamp != _stamp)
            {
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로 삼는다(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineStamp = _stamp;
                _saidStage = _stage;
                _saidSkirts = skirts;
                _saidTrackHalf = TrackHalf(ship, tracks);
                _saidRepairs = _repairedUnits;
                _saidVentsOpen = ventsOpen;
                _saidVentHalf = vents > 0 && ventsDown * 2 >= vents;
                _saidCoreHalf = _stage == ACO_LandShip.STAGE_CORE && core <= 0.5f;
                _saidCity = cityLevel;
                _saidAllRepair = _allRepairMs != 0;
                if (_stage == ACO_LandShip.STAGE_ARMOR && skirts == 0) { Say(SIT_SPAWN, ""); }
                return;
            }
            if (_stage != _saidStage)
            {
                _saidStage = _stage;
                if (_stage == ACO_LandShip.STAGE_CORE) { Say(SIT_CORE_OPEN, ""); }
                return;
            }
            if (skirts > _saidSkirts)
            {
                if (_saidSkirts == 0) { Say(SIT_SKIRT_DOWN, ""); }
                _saidSkirts = skirts;
            }
            if (ventsOpen > _saidVentsOpen)
            {
                if (_saidVentsOpen == 0) { Say(SIT_VENT_OPEN, ""); }
                _saidVentsOpen = ventsOpen;
            }
            if (!_saidTrackHalf && TrackHalf(ship, tracks))
            {
                _saidTrackHalf = true;
                Say(SIT_TRACK_HALF, (ship.GetTrackTotal() - tracks).ToString());
            }
            if (!_saidAllRepair && _allRepairMs != 0)
            {
                _saidAllRepair = true;
                Say(SIT_ALL_REPAIR, "");
            }
            if (_repairedUnits > _saidRepairs)
            {
                _saidRepairs = _repairedUnits;
                Say(_allRepairMs != 0 ? SIT_REPAIR_AUTO : SIT_REPAIR, "");   // 자동 사통 뒤엔 시스템 음성
            }
            if (!_saidVentHalf && vents > 0 && ventsDown * 2 >= vents && _stage == ACO_LandShip.STAGE_ARMOR)
            {
                _saidVentHalf = true;
                Say(SIT_VENT_HALF, "");
            }
            if (_stage == ACO_LandShip.STAGE_CORE && !_saidCoreHalf && core <= 0.5f)
            {
                _saidCoreHalf = true;
                Say(SIT_CORE_HALF, "");
            }
            if (cityLevel == 4 && _saidCity < 4)
            {
                _saidCity = 4;
                Chatter.ClearQueue();
                Say(SIT_CITY_HIT, "");
                return;
            }
            if (cityLevel > _saidCity && cityLevel < 4)
            {
                _saidCity = cityLevel;
                // 도시 경고는 밀려 있는 대사보다 먼저다
                Chatter.ClearQueue();
                Say(cityLevel == 3 ? SIT_CITY_LAST : (cityLevel == 2 ? SIT_CITY_NEAR : SIT_CITY_FAR), "");
            }
        }

        /// 궤도(앞뒤 끝 궤도 포함)가 절반 이상 부서졌는가.
        private bool TrackHalf(ACO_LandShip ship, int tracks)
        {
            int total = ship.GetTrackTotal();
            return total > 0 && tracks * 2 >= total;
        }

        private int CityLevel(ACO_LandShip ship)
        {
            if (ship.GetRouteLength() <= 0f) { return 0; }
            float d = ship.GetRemainingDistance();
            if (d <= 1f) { return 4; }   // 도착
            return d <= CityWarnLast ? 3 : (d <= CityWarnNear ? 2 : (d <= CityWarnFar ? 1 : 0));
        }

        /// 코어가 파괴됐다(모든 클라이언트).
        /// 코어 폭발(전함 Explode — 모든 클라이언트). 함장 대사("Keep firing! Keep fir-" + 비명)에서 말이 끊기는 곳(CoreDownCutAt)이
        /// 대폭발(ACO_LandShip.DeathChainTime 뒤)에 맞게 늦게 시작한다(끊기는 곳이 더 뒤면 바로). AWACS 대사는 무전 대기열이 그 뒤에 잇는다.
        public void SayDown()
        {
            float delay = 0f;
            ACO_LandShip ship = Ship();
            AudioClip first = FirstClip(SIT_CORE_DOWN);
            float cutAt = CoreDownCutAt > 0f ? CoreDownCutAt : (first != null ? first.length : 0f);
            if (ship != null && cutAt > 0f) { delay = Mathf.Max(ship.DeathChainTime - cutAt, 0f); }
            if (delay <= 0.05f) { _SayDownNow(); return; }
            SendCustomEventDelayedSeconds(nameof(_SayDownNow), delay);
        }

        public void _SayDownNow() { Say(SIT_CORE_DOWN, ""); }

        /// 상황의 첫 줄 음성(없으면 null).
        private AudioClip FirstClip(int situation)
        {
            if (LineEvent == null || LineClip == null) { return null; }
            for (int i = 0; i < LineEvent.Length && i < LineClip.Length; i++) { if (LineEvent[i] == situation) { return LineClip[i]; } }
            return null;
        }

        private void Say(int situation, string left) { SayTable(situation, left, LineEvent, LineSpeaker, LineText, LineClip); }

        // ───────────── 모든 클라이언트 ─────────────

        /// 이번 페이즈 값을 받았는가. 아니면 전함은 스폰 자리에 멈춰 있다.
        public bool IsLive() { return Manager != null && _stamp != 0 && _stamp == CurrentStamp() && _routeCount >= 2; }
        public int GetStamp() { return _stamp; }
        public int GetStage() { return _stage; }
        public int GetRouteCount() { return _routeCount; }
        public Vector3 GetRoutePoint(int i) { return _route[Mathf.Clamp(i, 0, MAX_ROUTE - 1)]; }
        public int GetBaseMs() { return _baseMs; }
        public float GetBaseDistance() { return _baseDist; }
        public float GetSpeedScale() { return _speedScale; }

        public override string GetDescription()
        {
            if (!IsLive()) { return Description; }
            if (_stage == ACO_LandShip.STAGE_ARMOR) { return "BREAK THE ARMOR AND TRACKS, DESTROY THE EXHAUST VENTS"; }
            return "DESTROY THE CORE BEFORE IT REACHES THE CITY";
        }

        /// "VENT 3/8  SPD 79%  CITY 4.2km" → "CORE 72%  CITY 1.2km"(3km 아래면 빨갛게).
        public override string GetProgressText()
        {
            ACO_LandShip ship = Ship();
            if (ship == null || !IsLive()) { return ""; }
            float near = ship.GetRemainingDistance();
            string dist = Dist(near);
            if (near < CityWarnFar) { dist = "<color=#FF5544>" + dist + "</color>"; }
            if (_stage == ACO_LandShip.STAGE_ARMOR)
            {
                return "VENT " + ship.CountVentsDestroyed() + "/" + ship.GetVentTotal() + "  SPD " + Mathf.RoundToInt(_speedScale * 100f) + "%  CITY " + dist;
            }
            return "CORE " + Mathf.RoundToInt(ship.GetCoreHealthRatio() * 100f) + "%  CITY " + dist;
        }

        public override string GetLocation()
        {
            if (Manager == null) { return ""; }
            string name = Manager.GetPhaseSiteName();
            return name.Length > 0 ? "DEFEND: " + name : "";
        }
    }
}
