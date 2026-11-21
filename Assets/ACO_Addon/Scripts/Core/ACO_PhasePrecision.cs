using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 시가지 정밀 타격 페이즈(6-270): 도시 도로에 적 지휘 · 통신 차량 EnemyCount대가 민간 차량 CivilianCount대 사이에 섞여 있다(일부는 블록 둘레를 돈다).
    /// 처음엔 모두 미확인(회색 ?) — 누군가 차량에서 직선 IdentifyDistance(300m — 6-290) 안으로 지나가면(건물 가림은 따지지 않는다)
    /// 그 차량이 식별된다(적 = 빨간 TGT, 민간 = 초록 CIV — 식별된 민간 차량은 락온이 막힌다). 식별은 모두에게 공유되고 계속 유지된다.
    /// 적 차량을 모두 부수면 성공, 민간 차량이 CivilianFailCount대 부서지면 미션 실패. 민간 차량 하나 = 팀 · 파괴한 사람 감점(ACO_CityVehicle.CivilianScore).
    ///
    /// 자체 동기화(Manual): 도장(_stamp), 식별 비트(_ident — 차량 순번 = 이번 페이즈 태그가 달린 지상 풀 번호를 작은 순으로 센 것, 모든 화면이 같은 생성기 상태로 같은 순번),
    /// 도시 자리(_site), 태그(_tag — 관리자가 시도마다 주는 태그 = 적, 민간 = _tag + CIVILIAN_TAG_OFFSET. 고정 MakePhaseTag(1 · 2)는 시한 표적 TEL 태그와 겹쳤다). 식별 판정은 관리자 오너가 Tick에서 한다(플레이어 위치는 모든 화면이 보지만 판정을 한 곳에서 해야 값이 하나다).
    /// 적 · 민간 파괴 수는 체력 동기화로 모든 화면이 같게 센다(따로 보내지 않는다).
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhasePrecision : ACO_PhaseModule
    {
        public const int MAX_VEHICLES = 31;
        public const int CIVILIAN_TAG_OFFSET = 20;   // 관리자 시도 태그 = 페이즈 × 100 + 20 + 시도 → 민간 = 40 + 시도(다른 태그와 겹치지 않는다)

        [Header("시가지 정밀 타격")]
        public int EnemyCount = 3;
        public int CivilianCount = 14;
        [Tooltip("민간 차량이 이만큼 부서지면 미션 실패")]
        public int CivilianFailCount = 3;
        [Tooltip("식별 — 차량까지 직선 거리(m). 건물에 가려도 식별된다")]
        public float IdentifyDistance = 300f;   // 200 → 300(6-290, 사용자 "도시 상공을 날아도 마킹이 안 돼")
        [Tooltip("식별 판정 간격(초, 관리자 오너)")]
        public float IdentifyInterval = 0.25f;
        [Tooltip("CIC 정찰 드론(6-275): 드론 아래 수평 이 반경(m) 안에 DroneIdentifyTime초 머문 차량도 식별된다. Bake가 콘솔을 넣는다")]
        public ACO_CicConsole Cic;
        public float DroneIdentifyRadius = 700f;   // 300 → 700(6-290)
        public float DroneIdentifyTime = 3f;

        [Header("대사 (관제 — 비우면 안 나온다)")]
        [Tooltip("Bake가 넣는다")]
        public ACO_Chatter Chatter;
        public float VoiceQueueDelay = 8f;
        [TextArea] public string LineFirstHostile = "Hostile vehicle confirmed. Weapons free on that one only.";
        public AudioClip LineFirstHostileClip;
        [TextArea] public string LineCivilianHit = "Civilian vehicle hit! Watch your fire!";
        public AudioClip LineCivilianHitClip;
        [TextArea] public string LineCivilianLast = "That's two civilians down. One more and we abort the operation!";
        public AudioClip LineCivilianLastClip;
        [TextArea] public string LineEnemyDown = "Hostile vehicle destroyed.";
        public AudioClip LineEnemyDownClip;

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _ident;
        [UdonSynced] private int _site = -1;
        [UdonSynced] private int _tag;

        private int _planSite = -1;
        private float _identNext;
        private float[] _droneDwell = new float[32];   // 차량 순번마다 드론 아래 머문 시간(오너)

        // 모든 화면 — 이번 페이즈 차량 목록과 대사 상태
        private int[] _slots = new int[MAX_VEHICLES];
        private int _slotCount;
        private int _slotsStamp;
        private int _slotsSerial = -1;
        private int _lineStamp;
        private bool _saidHostile;
        private int _seenCivDead;
        private int _seenEnemyDead;
        private float _lineTimer;
        // 모든 화면 — 지상 풀 번호별 살아 있는 걸 본 배치 번호(관리자 집계와 같은 규칙: 본 적 있거나 배치 후 정착 시간이 지난 것만 파괴로 센다)
        private int[] _aliveSerial = new int[0];
        // 4Hz로 센 값(진행 표시가 매 프레임 읽는다)
        private int _civDead;
        private int _enemyDead;
        private int _enemyTotal;
        private int _countStamp;
        private float _clearHoldUntil = -1f;   // 관리자 오너 — 적을 다 부순 뒤 성공을 미루는 끝(로컬 시각)
        private const float CLEAR_HOLD = 1.5f;

        private bool IsCurrent() { return Manager != null && _stamp != 0 && _stamp == CurrentStamp(); }
        private int EnemyTag() { return _tag; }
        private int CivilianTag() { return _tag != 0 ? _tag + CIVILIAN_TAG_OFFSET : 0; }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            _planSite = -1;
            if (Generator == null) { return false; }
            int site = Generator.FindVehicleCitySite(focus);
            if (site < 0) { return false; }
            if (Generator.CountFreeCityVehicles() < EnemyCount + CivilianFailCount + 2) { return false; }
            _planSite = site;
            return true;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            _stamp = CurrentStamp();
            _ident = 0;
            for (int i = 0; i < _droneDwell.Length; i++) { _droneDwell[i] = 0f; }
            _site = _planSite;
            _tag = tag;
            _clearHoldUntil = -1f;
            int enemyTag = EnemyTag();
            Manager.SetModuleActiveTag(enemyTag);
            Manager.SetModuleProgress(0, Mathf.Max(EnemyCount, 1), false);
            Manager.SetModuleSite(_site);
            Vector3 city = _site >= 0 ? Generator.GetSitePosition(_site) : focus;
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_CITY, enemyTag, EnemyCount, CivilianTag(), city, CivilianCount, 0f);
            SyncNow();
        }

        public override int Tick(int now)
        {
            if (!IsCurrent())
            {
                // 이번 페이즈 값을 못 받은 새 오너: 식별은 처음부터(다시 지나가면 된다). 도시 자리는 관리자 작전 지역(_phaseSite)에 남아 있다.
                _stamp = CurrentStamp();
                _ident = 0;
                _site = -1;
                _tag = Manager.GetActiveTag();
                _clearHoldUntil = -1f;
                SyncNow();
            }
            int enemyTag = EnemyTag();
            Manager.SetModuleActiveTag(enemyTag);
            // 민간 피해를 먼저 센다 — 관리자 태그 집계는 마지막에 센 태그(적) 값으로 남겨 진행 표시에 쓴다
            RefreshCounts(now);
            int civDead = _civDead;
            if (civDead >= Mathf.Max(CivilianFailCount, 1))
            {
                Debug.Log("[ACO] 시가지 정밀 타격: 민간 차량 " + civDead + "대 파괴 — 미션 실패");
                return RESULT_FAIL;
            }
            Manager.CountTaggedObjectives(enemyTag);
            int total = Manager.GetTaggedTotal();
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            Manager.SetModuleProgress(Manager.GetTaggedDone(), total, true);
            if (Manager.GetTaggedDone() >= total)
            {
                // 같은 폭발로 마지막 적과 세 번째 민간이 부서져도 체력 동기화는 다른 프레임에 올 수 있다(차마다 소유자가 다르면) —
                // CLEAR_HOLD초 동안 민간 수를 더 본다. 그 사이 한도에 닿으면 위에서 실패(사용자 "같이폭발하면 실패처리로해줘")
                if (_clearHoldUntil < 0f) { _clearHoldUntil = Time.time + CLEAR_HOLD; }
                if (Time.time < _clearHoldUntil) { return RESULT_RUNNING; }
                return RESULT_CLEAR;
            }
            _clearHoldUntil = -1f;
            if (Time.time >= _identNext)
            {
                _identNext = Time.time + IdentifyInterval;
                OwnerIdentify();
            }
            return RESULT_RUNNING;
        }

        /// 플레이어가 차량 가까이 낮게 지나갔는가 — 새로 식별된 차량 비트를 켜고 보낸다.
        private void OwnerIdentify()
        {
            RefreshSlots();
            if (_slotCount == 0) { return; }
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            float r2 = IdentifyDistance * IdentifyDistance;
            int ident = _ident;
            bool drone = Cic != null && Cic.IsDroneActive();
            Vector3 dp = drone ? Cic.GetDronePosition() : Vector3.zero;
            float dr2 = DroneIdentifyRadius * DroneIdentifyRadius;
            for (int s = 0; s < _slotCount; s++)
            {
                if ((ident & (1 << s)) != 0) { continue; }
                ACO_CityVehicle unit = Generator.GetCityVehicle(_slots[s]);
                if (unit == null) { continue; }
                // 이 화면에 아직 이번 배치가 적용되지 않은 차량(생성기 오너 ≠ 관리자 오너)은 예전 자리에 있다 — 적용된 뒤에 본다
                if (unit.GetStartMs() != Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_GROUND, _slots[s])) { continue; }
                Vector3 car = unit.transform.position;
                // 드론: 수평 반경 안에 이어서 머문 시간을 센다(벗어나면 처음부터)
                if (s < _droneDwell.Length)
                {
                    float hx = car.x - dp.x;
                    float hz = car.z - dp.z;
                    if (drone && hx * hx + hz * hz <= dr2) { _droneDwell[s] += IdentifyInterval; }
                    else { _droneDwell[s] = 0f; }
                    if (_droneDwell[s] >= DroneIdentifyTime) { ident |= 1 << s; continue; }
                }
                for (int p = 0; p < players.Length; p++)
                {
                    if (!Utilities.IsValid(players[p])) { continue; }
                    Vector3 at = players[p].GetPosition();
                    float dx = at.x - car.x;
                    float dz = at.z - car.z;
                    float dy = at.y - car.y;
                    if (dx * dx + dy * dy + dz * dz <= r2) { ident |= 1 << s; break; }
                }
            }
            if (ident != _ident)
            {
                _ident = ident;
                SyncNow();
                ApplyIdentified();
            }
        }

        public override bool WaveOnStart() { return true; }

        /// 성공 보류(CLEAR_HOLD) 중에는 미션 제한 시간으로 실패시키지 않는다 — 남은 1~2초에 마지막 적을 잡아도 성공. 보류는 1.5초 뒤 반드시 끝난다.
        public override bool HoldsResult() { return _clearHoldUntil >= 0f; }

        /// 증원은 도시 둘레에 흩어 놓는다(살아 있는 적 차량 근처에 나오면 위치가 드러난다).
        public override bool WaveAroundSite() { return true; }

        /// 관리자 오너 — 성공 · 실패 · 교체 모두 여기를 지난다. 남은 민간 차량을 거둔다(관리자는 적 태그만 거둔다):
        /// 남겨 두면 다음 페이즈에 맞혀도 감점이다.
        public override void End()
        {
            _planSite = -1;
            _clearHoldUntil = -1f;
            // 이번 페이즈 값을 아직 못 받은 새 오너(첫 Tick 전에 끝남)는 관리자 목표 태그(= 적 태그)로 민간 태그를 찾는다
            int civ = IsCurrent() ? CivilianTag() : (Manager != null && Manager.GetActiveTag() != 0 ? Manager.GetActiveTag() + CIVILIAN_TAG_OFFSET : 0);
            if (Generator != null && civ != 0) { Generator.RequestReleaseTag(civ); }
        }

        // ───────────── 모든 화면 ─────────────

        /// 이번 페이즈 차량(적 · 민간 태그) 지상 풀 번호를 작은 순으로. 생성기 배치가 바뀌면 다시 센다.
        private void RefreshSlots()
        {
            if (Generator == null) { _slotCount = 0; return; }
            int deploy = Generator.GetDeployCount();
            if (_slotsStamp == _stamp && _slotsSerial == deploy) { return; }
            _slotsStamp = _stamp;
            _slotsSerial = deploy;
            _slotCount = 0;
            int enemyTag = EnemyTag();
            int civTag = CivilianTag();
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_GROUND);
            for (int i = 0; i < n && _slotCount < MAX_VEHICLES; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_GROUND, i)) { continue; }
                int tag = Generator.GetUnitTag(ACO_MissionGenerator.CAT_GROUND, i);
                if (tag != enemyTag && tag != civTag) { continue; }
                if (!Generator.IsCityVehicle(i)) { continue; }
                _slots[_slotCount] = i;
                _slotCount++;
            }
        }

        private void ApplyIdentified()
        {
            for (int s = 0; s < _slotCount; s++)
            {
                ACO_CityVehicle car = Generator.GetCityVehicle(_slots[s]);
                if (car == null) { continue; }
                // 차가 아직 이번 배치로 바뀌기 전이면(지난 역할) 식별 표시를 미룬다
                bool applied = car.GetStartMs() == Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_GROUND, _slots[s]);
                car.SetIdentified(applied && (_ident & (1 << s)) != 0);
            }
        }

        /// 적 · 민간 파괴 수와 놓인 적 수를 센다(_civDead · _enemyDead · _enemyTotal). 파괴 = 체력 0이고, 이번 배치에서 살아 있는 걸 봤거나
        /// 배치 후 정착 시간(관리자 ObjectiveSettleTime)이 지남 — 지난 페이즈에 부서진 차를 다시 쓸 때 체력이 차기 전 몇 프레임을 파괴로 세지 않는다.
        private void RefreshCounts(int now)
        {
            _civDead = 0;
            _enemyDead = 0;
            _enemyTotal = 0;
            if (Generator == null || Manager == null) { return; }
            _countStamp = _stamp;
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_GROUND);
            if (_aliveSerial.Length != n)
            {
                _aliveSerial = new int[n];
                for (int i = 0; i < n; i++) { _aliveSerial[i] = -1; }
            }
            int enemyTag = EnemyTag();
            int civTag = CivilianTag();
            int settleMs = Mathf.RoundToInt(Manager.GetObjectiveSettleTime() * 1000f);
            for (int i = 0; i < n; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_GROUND, i)) { continue; }
                int tag = Generator.GetUnitTag(ACO_MissionGenerator.CAT_GROUND, i);
                bool enemy = tag == enemyTag;
                if (!enemy && tag != civTag) { continue; }
                if (enemy) { _enemyTotal++; }
                int serial = Generator.GetUnitSerial(ACO_MissionGenerator.CAT_GROUND, i);
                // 이 화면에 이번 배치가 아직 적용되지 않은 차(동기화가 늦게 온 재사용 차 — 체력이 지난 값)는 파괴로 세지 않는다
                ACO_CityVehicle car = Generator.GetCityVehicle(i);
                if (car == null || car.GetStartMs() != Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_GROUND, i)) { continue; }
                if (!Generator.IsUnitDestroyed(ACO_MissionGenerator.CAT_GROUND, i))
                {
                    _aliveSerial[i] = serial;
                    continue;
                }
                if (_aliveSerial[i] != serial && now - Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_GROUND, i) < settleMs) { continue; }
                if (!Manager.IsUnitGone(ACO_MissionGenerator.CAT_GROUND, i)) { continue; }   // 6-338: 체력 0이 이어져야(늦게 온 옛 0으로 민간 피해 · 실패가 나지 않게)
                if (enemy) { _enemyDead++; } else { _civDead++; }
            }
        }

        /// 지금 진행 중인 이 페이즈인가(페이즈가 끝난 뒤 다음 페이즈 도장이 오기 전 — 전환 대기 · 미션 종료 — 에는 대사 · 표시를 멈춘다).
        private bool IsLive() { return IsCurrent() && Manager.IsPhaseActive(); }

        public override void OnDeserialization()
        {
            if (!IsCurrent()) { return; }
            RefreshSlots();
            ApplyIdentified();
        }

        void Update()
        {
            _lineTimer -= Time.deltaTime;
            if (_lineTimer > 0f) { return; }
            _lineTimer = 0.25f;
            if (!IsLive() || Generator == null) { return; }
            RefreshSlots();
            if (_slotCount == 0) { return; }
            ApplyIdentified();   // 늦게 켜진 차량(배치 적용이 식별보다 늦은 화면)도 맞춘다
            RefreshCounts(Networking.GetServerTimeInMilliseconds());
            CheckLines();
        }

        private void CheckLines()
        {
            int civDead = _civDead;
            int enemyDead = _enemyDead;
            bool hostileSeen = false;
            for (int s = 0; s < _slotCount; s++)
            {
                if ((_ident & (1 << s)) == 0) { continue; }
                if (Generator.GetUnitTag(ACO_MissionGenerator.CAT_GROUND, _slots[s]) == EnemyTag()) { hostileSeen = true; break; }
            }
            if (_lineStamp != _stamp)
            {
                // 기준은 모든 차에 이번 배치가 적용되고 1.5초 지난 뒤에 잡는다(늦게 켜진 화면은 적용이 프레임마다 나뉘어 이미 부서진 차가 뒤늦게 세어진다)
                int nowMs = Networking.GetServerTimeInMilliseconds();
                for (int s = 0; s < _slotCount; s++)
                {
                    ACO_CityVehicle c = Generator.GetCityVehicle(_slots[s]);
                    int deployMs = Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_GROUND, _slots[s]);
                    if (c == null || c.GetStartMs() != deployMs || nowMs - deployMs < 1500) { return; }
                }
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                _lineStamp = _stamp;
                _saidHostile = hostileSeen;
                _seenCivDead = civDead;
                _seenEnemyDead = enemyDead;
                return;
            }
            if (civDead > _seenCivDead)
            {
                _seenCivDead = civDead;
                if (civDead == CivilianFailCount - 1) { Say(LineCivilianLast, LineCivilianLastClip); }
                else if (civDead < CivilianFailCount) { Say(LineCivilianHit, LineCivilianHitClip); }
            }
            if (enemyDead > _seenEnemyDead)
            {
                _seenEnemyDead = enemyDead;
                if (enemyDead < _enemyTotal) { Say(LineEnemyDown, LineEnemyDownClip); }   // 마지막은 페이즈 성공 대사(놓인 수 기준 — 덜 놓였어도 겹치지 않게)
            }
            if (!_saidHostile && hostileSeen)
            {
                _saidHostile = true;
                Say(LineFirstHostile, LineFirstHostileClip);
            }
        }

        private void Say(string text, AudioClip clip)
        {
            if (Chatter != null && !string.IsNullOrEmpty(text)) { Chatter.SayControl(text, clip, VoiceQueueDelay); }
        }

        /// "TGT 1/3  CIV 1/3  ID 200m" — 민간 피해는 빨갛게.
        public override string GetProgressText()
        {
            if (!IsCurrent() || Generator == null) { return "-"; }
            // 매 프레임 불린다 — Update(4Hz)가 센 값을 쓴다(놓인 적이 아직 안 보이면 요청 수)
            bool counted = _countStamp == _stamp;   // 새 페이즈를 아직 안 셌으면 0부터
            int civDead = counted ? _civDead : 0;
            int enemyDead = counted ? _enemyDead : 0;
            int total = counted && _enemyTotal > 0 ? _enemyTotal : EnemyCount;
            string civ = civDead > 0 ? "<color=#FF5544>CIV " + civDead + "/" + CivilianFailCount + "</color>" : "CIV 0/" + CivilianFailCount;
            return "TGT " + enemyDead + "/" + total + "  " + civ + "  ID " + Dist(IdentifyDistance);
        }

        public override string GetDescription()
        {
            if (Description.Length > 0) { return Description; }
            return "DESTROY " + EnemyCount + " ENEMY COMMAND VEHICLES HIDDEN AMONG CIVILIAN TRAFFIC - FLY LOW TO IDENTIFY. " + CivilianFailCount + " CIVILIAN LOSSES = MISSION FAILED";
        }
    }
}
