using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 브레이킹 포인트(6-333) — 대규모 공중전. 적이 교전 공역(ZONE)에 공군 전력을 쏟아붓는다: 목표 킬 수만큼 떨어뜨리면 적 공군이 무너져 물러난다.
    ///  - 페이즈 시작: 플레이어 평균 위치에서 ZoneDistanceMin~Max 떨어진 곳에 공역 중심(고도 ZoneAltitude). HUD 마커 ZONE · 레이더 · 조종석 지도 원(ZoneRadius).
    ///  - 적: 공역 중심에서 SpawnDistanceMin~Max 둘레에 2 · 4기 편대로 나타나 공역으로 온다. 동시 생존 AliveCap(16 — 배경 전투기 포함), WaveInterval초마다 WaveMax기까지 보충.
    ///    남은 킬 수보다 많이 띄우지 않는다.
    ///  - 아군: 페이즈 시작 때 2기(지원 관리자 전용 편대 — 공역 안에서 싸운다, 다시 오지 않는다). 아군이 잡은 적은 킬 수 · 팀 점수에 들어가고 개인 점수는 없다.
    ///  - 킬 수 = 관리자의 적 전투기 격추 수(동기화) − 페이즈 시작 값. 목표 = KillBase + 인원 × KillPerPlayer(최대 KillMax).
    ///  - 목표에 닿으면 남은 적(이 페이즈 태그)은 사격을 멈추고 바깥 위로 빠지며 락온 · 마커가 꺼진다(모든 화면). RetreatTime초 뒤 디스폰하고 클리어.
    ///  - 이 페이즈만의 실패 조건은 없다(미션 제한 시간만 — 물러나는 동안은 시간에서 뺀다).
    /// 동기화(Manual): 페이즈 시작 시각(_stamp), 공역 중심, 시작 킬 수, 물러남 시작 시각. 판단(보충 · 아군 호출 · 클리어)은 관리자 오너.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseBreakingPoint : ACO_PhaseBoss
    {
        [Header("교전 공역")]
        [Tooltip("공역 중심: 플레이어 평균 위치에서 이 거리(m) 사이")]
        public float ZoneDistanceMin = 6000f;
        public float ZoneDistanceMax = 12000f;
        [Tooltip("공역 중심 고도(m)")]
        public float ZoneAltitude = 3000f;
        [Tooltip("공역 반경(m) — HUD 레이더 · 조종석 지도의 원, 아군이 싸우는 구역")]
        public float ZoneRadius = 6000f;

        [Header("적 편대")]
        [Tooltip("동시에 살아 있는 적 전투기 상한(배경 전투기 포함)")]
        public int AliveCap = 16;
        [Tooltip("처음 보내는 수")]
        public int FirstWave = 8;
        [Tooltip("보충 간격(초)")]
        public float WaveInterval = 15f;
        [Tooltip("한 번에 보충하는 최대 수")]
        public int WaveMax = 4;
        [Tooltip("공역 중심에서 나타나는 거리(m)")]
        public float SpawnDistanceMin = 3000f;    // 2026-11-20 9 ~ 13km → 3 ~ 6km(사용자 "스폰 위치는 zone 훨씬 가까이에서")
        public float SpawnDistanceMax = 6000f;

        [Header("킬 목표")]
        public int KillBase = 20;
        public int KillPerPlayer = 5;
        public int KillMax = 40;

        [Header("아군 · 물러남")]
        [Tooltip("지원 관리자(Bake가 넣는다) — 아군 2기")]
        public ACO_SupportManager Support;
        [Tooltip("물러나는 시간(초) — 그 뒤 디스폰하고 클리어")]
        public float RetreatTime = 12f;

        [Header("대사")]
        [Tooltip("새 웨이브 알림 사이 최소 간격(초)")]
        public float WaveLineGap = 45f;
        [Tooltip("남은 킬 수 알림을 하는 수(사용자 \"5, 3, 1\") — 한꺼번에 여러 기가 떨어져 건너뛰면 지나친 가장 작은 수 하나만")]
        public int[] CountdownAt = new int[] { 5, 3, 1 };

        public const int SIT_CONTACT = 1;      // 첫 적 편대가 공역에 들어옴
        public const int SIT_WAVE = 2;         // 새 웨이브(간격 WaveLineGap)
        public const int SIT_HALF = 3;         // 절반
        public const int SIT_COUNT = 4;        // 남은 킬 {left}
        public const int SIT_ALLY_DOWN = 5;    // 아군 한 기 격추
        public const int SIT_RETREAT = 7;      // 적이 무너져 물러남

        public int[] LineEvent = new int[] {
            SIT_CONTACT,
            SIT_CONTACT,
            SIT_WAVE,
            SIT_HALF,
            SIT_HALF,
            SIT_COUNT,
            SIT_ALLY_DOWN,
            SIT_RETREAT,
            SIT_RETREAT };
        public int[] LineSpeaker = new int[] {
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_AWACS,
            SPEAKER_ENEMY,
            SPEAKER_AWACS };
        public string[] LineText = new string[] {
            "All squadrons, converge on the zone. Sweep them from the sky.",
            "Multiple bandits closing on the zone. It's going to be a furball. Stay with your wingman.",
            "More bandits inbound. They keep throwing fighters at us.",
            "Losses are irrelevant. Send everything we have!",
            "They're taking losses, but they keep coming. Keep the pressure on.",
            "{left} more kills and their air wing breaks!",
            "We lost a wingman! Keep fighting!",
            "Too many losses... All units, disengage!",
            "They're breaking off! The enemy air wing is in full retreat!" };
        public AudioClip[] LineClip = new AudioClip[9];

        [UdonSynced] private int _stamp;
        [UdonSynced] private Vector3 _zone;
        [UdonSynced] private int _killStart;
        [UdonSynced] private int _retreatMs;

        // 오너 지역 상태(오너가 바뀌면 다음 주기부터 — 0이면 그때부터 잰다)
        private Vector3 _planZone;
        private int _nextWaveMs;

        // 모든 화면
        private int _lineStamp;
        private int _saidKillsHalf;
        private int _saidLeft;
        private bool _saidContact;
        private bool _saidRetreat;
        private int _saidAllies;
        private float _lastWaveLineAt = -1000f;
        private int _seenTagged;
        private float _lineTimer;
        private float _retreatTimer;
        private float _redirTimer;
        private int _redirStamp;
        private int[] _redirSerial;   // 공역으로 보낸 전투기의 배치 번호(새로 배치되면 다시 보낸다)

        public override bool IsBossPhase() { return false; }   // 대사 표만 보스 모듈과 같이 쓴다 — 일반 페이즈

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            if (Generator == null || Manager == null) { return false; }
            if (Generator.CountFreeFighterUnits() < FirstWave) { return false; }
            _planZone = PickZone(focus);
            return true;
        }

        /// 공역 중심: focus에서 ZoneDistanceMin~Max 무작위 방향(NPC 공역 안), 고도 ZoneAltitude.
        private Vector3 PickZone(Vector3 focus)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float d = Random.Range(ZoneDistanceMin, ZoneDistanceMax);
            Vector3 p = new Vector3(focus.x + Mathf.Sin(a) * d, ZoneAltitude, focus.z + Mathf.Cos(a) * d);
            p = Generator.ClampToField(p);
            p.y = ZoneAltitude;
            return p;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            int players = Mathf.Clamp(VRCPlayerApi.GetPlayerCount(), 1, 4);
            int target = Mathf.Min(KillBase + players * KillPerPlayer, KillMax);
            Manager.SetModuleProgress(0, target, false);
            Manager.SetModuleSite(-1);
            _stamp = CurrentStamp();
            // 공역은 여기서 focus로 다시 정한다(대체 선택 경로는 IsFeasible 없이 Begin을 부른다 — 옛 자리 · 원점이 되지 않게)
            _zone = PickZone(focus);
            _killStart = Manager.GetFighterKills();
            _retreatMs = 0;
            _nextWaveMs = now + Mathf.RoundToInt(WaveInterval * 1000f);
            SyncNow();
            SpawnWave(ACO_MissionGenerator.OBJ_FLIGHT, tag, Mathf.Min(FirstWave, target));
            // 아군 2기: 공역 가장자리(플레이어 쪽)에서 공역을 향해 — 페이즈 동안(다시 오지 않는다)
            if (Support != null)
            {
                Vector3 side = new Vector3(focus.x - _zone.x, 0f, focus.z - _zone.z);
                if (side.sqrMagnitude < 1f) { side = Vector3.back; }
                side = side.normalized;
                Vector3 pos = _zone + side * (ZoneRadius * 0.45f) + Vector3.up * 200f;
                Support.CallPhaseWingmen(_zone, pos, -side, 3600f, ZoneRadius);
            }
        }

        public override int Tick(int now)
        {
            int tag = Manager.GetModuleTag();
            if (_stamp != CurrentStamp())
            {
                // 동기화를 받지 못한 새 오너(드묾): 지금 값으로 다시 시작한다
                _stamp = CurrentStamp();
                if (_zone == Vector3.zero) { _zone = _planZone; }
                _killStart = Manager.GetFighterKills();
                _retreatMs = 0;
                SyncNow();
            }
            int total = Manager.GetObjectiveTotal();
            if (_retreatMs != 0)
            {
                if (now - _retreatMs < Mathf.RoundToInt(RetreatTime * 1000f)) { return RESULT_RUNNING; }
                Manager.AddModuleTime(RetreatTime);   // 물러나는 동안은 미션 시간에서 뺀다
                Generator.RequestReleaseTag(tag);
                return RESULT_CLEAR;
            }
            int kills = Mathf.Max(Manager.GetFighterKills() - _killStart, 0);
            Manager.SetModuleProgress(Mathf.Min(kills, total), total, true);
            if (total > 0 && kills >= total)
            {
                _retreatMs = now;
                SyncNow();
                if (Support != null) { Support.EndPhaseWingmen(); }
                return RESULT_RUNNING;
            }
            // 보충: 동시 생존 상한 · 남은 킬 수 안에서
            if (_nextWaveMs == 0) { _nextWaveMs = now + Mathf.RoundToInt(WaveInterval * 1000f); }
            if (now - _nextWaveMs >= 0)
            {
                _nextWaveMs = now + Mathf.RoundToInt(WaveInterval * 1000f);
                int alive = Generator.CountAliveFighters();
                int n = Mathf.Min(AliveCap - alive, WaveMax);
                n = Mathf.Min(n, total - kills - alive);
                if (n > 0) { SpawnWave(ACO_MissionGenerator.OBJ_FLIGHT_MORE, tag, n); }   // 같은 태그로 더 — 중복 요청 검사를 건너뛰는 종류
            }
            return RESULT_RUNNING;
        }

        /// 웨이브를 띄운다: 공역 중심 둘레 SpawnDistanceMin~Max에서 나타나 공역으로 온다(플레이어 쪽에 조금 가깝게 나와도 괜찮다 — 사용자).
        private void SpawnWave(int kind, int tag, int count)
        {
            Generator.RequestDeployObjective(kind, tag, count, -1, _zone, SpawnDistanceMin, SpawnDistanceMax);
        }

        public override void End()
        {
            if (Support != null) { Support.EndPhaseWingmen(); }
        }

        /// 물러나는 동안 결과를 쥐고 있다(미션 제한 시간이 그 사이에 실패로 끝내지 않게).
        public override bool HoldsResult() { return _retreatMs != 0; }

        /// 지금 진행 중인 페이즈가 이것일 때만(끝난 뒤에는 시작 시각이 그대로라 — 태그 0 배경 전투기까지 물러나게 하고 다음 미션 집결 지점을 바꾸던 것).
        public bool IsLive() { return Manager != null && _stamp != 0 && _stamp == CurrentStamp() && Manager.GetActiveModule() == this; }
        public bool IsRetreating() { return IsLive() && _retreatMs != 0; }

        public override bool HasZone() { return IsLive() && _retreatMs == 0; }
        public override Vector3 GetZoneCenter() { return _zone; }
        public override float GetZoneRadius() { return ZoneRadius; }

        // ───────────── 모든 화면: 물러남 · 대사 ─────────────

        void Update()
        {
            if (!IsLive()) { return; }
            _redirTimer -= Time.deltaTime;
            if (_redirTimer <= 0f) { _redirTimer = 1f; RedirectToZone(); }
            if (_retreatMs != 0)
            {
                _retreatTimer -= Time.deltaTime;
                if (_retreatTimer <= 0f) { _retreatTimer = 0.25f; ApplyRetreat(); }
            }
            _lineTimer -= Time.deltaTime;
            if (_lineTimer <= 0f) { _lineTimer = 0.5f; CheckLines(); }
        }

        /// 살아 있는 적 전투기를 모두 공역으로(배경 전투기 포함 — 동시 생존 상한에 들어가니 공역 싸움에 오게). 집결 지점만 바꾼다(교전 중이면 끝난 뒤 온다).
        /// 모든 화면이 부른다(비행은 오너가 — 소유권이 넘어가도 같은 값). 배치 번호로 한 기에 한 번, 새로 배치되면 다시.
        private void RedirectToZone()
        {
            if (Generator == null || Generator.Fighters == null) { return; }
            int n = Generator.Fighters.Length;
            if (_redirSerial == null || _redirSerial.Length != n || _redirStamp != _stamp)
            {
                _redirSerial = new int[n];
                for (int i = 0; i < n; i++) { _redirSerial[i] = -1; }
                _redirStamp = _stamp;
            }
            for (int i = 0; i < n; i++)
            {
                ACO_NPCFighterAI f = Generator.Fighters[i];
                if (f == null || f.Retreating) { continue; }
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_FIGHTER, i) || Generator.IsUnitDestroyed(ACO_MissionGenerator.CAT_FIGHTER, i)) { continue; }
                int serial = Generator.GetUnitSerial(ACO_MissionGenerator.CAT_FIGHTER, i);
                if (serial == _redirSerial[i]) { continue; }
                _redirSerial[i] = serial;
                f.SetMissionHold(_zone);
            }
        }

        /// 이 페이즈의 남은 적을 물러나게 한다(사격 멈춤 · 바깥 위로 — 락온 · 마커는 그대로) — 아직 안 한 기체만.
        private void ApplyRetreat()
        {
            if (Generator == null || Generator.Fighters == null) { return; }
            int tag = Manager.GetModuleTag();
            if (tag == 0) { return; }   // 태그 없는 배경 전투기는 물러나지 않는다
            for (int i = 0; i < Generator.Fighters.Length; i++)
            {
                ACO_NPCFighterAI f = Generator.Fighters[i];
                if (f == null || f.Retreating) { continue; }
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_FIGHTER, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_FIGHTER, i) != tag) { continue; }
                if (Generator.IsUnitDestroyed(ACO_MissionGenerator.CAT_FIGHTER, i)) { continue; }
                Vector3 p = f.transform.position;
                Vector3 out1 = new Vector3(p.x - _zone.x, 0f, p.z - _zone.z);
                if (out1.sqrMagnitude < 1f) { out1 = f.transform.forward; out1.y = 0f; }
                if (out1.sqrMagnitude < 0.0001f) { out1 = Vector3.forward; }
                out1 = out1.normalized;
                f.SetRetreat(true, p + out1 * 15000f + Vector3.up * 3000f);
            }
        }

        private int CountTaggedAlive()
        {
            if (Generator == null || Generator.Fighters == null) { return 0; }
            int tag = Manager.GetModuleTag();
            int n = 0;
            for (int i = 0; i < Generator.Fighters.Length; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_FIGHTER, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_FIGHTER, i) != tag) { continue; }
                if (!Generator.IsUnitDestroyed(ACO_MissionGenerator.CAT_FIGHTER, i)) { n++; }
            }
            return n;
        }

        private bool AnyTaggedNear(float range)
        {
            if (Generator == null || Generator.Fighters == null) { return false; }
            int tag = Manager.GetModuleTag();
            float r2 = range * range;
            for (int i = 0; i < Generator.Fighters.Length; i++)
            {
                ACO_NPCFighterAI f = Generator.Fighters[i];
                if (f == null || !Generator.IsUnitUsed(ACO_MissionGenerator.CAT_FIGHTER, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_FIGHTER, i) != tag) { continue; }
                if (Generator.IsUnitDestroyed(ACO_MissionGenerator.CAT_FIGHTER, i)) { continue; }
                Vector3 d = f.transform.position - _zone;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) { return true; }
            }
            return false;
        }

        private void CheckLines()
        {
            if (Chatter == null) { return; }
            int total = Manager.GetObjectiveTotal();
            int done = Manager.GetObjectivesDone();
            int left = Mathf.Max(total - done, 0);
            int allies = Support != null ? Support.CountPhaseWingmenDown() : 0;   // 격추된 페이즈 아군 수
            int tagged = CountTaggedAlive();
            if (_lineStamp != _stamp)
            {
                // 이 페이즈를 처음 본다: 지금 상태를 기준으로(늦게 들어온 화면이 지난 대사를 듣지 않게)
                bool fresh = Networking.GetServerTimeInMilliseconds() - _stamp < 20000;
                _lineStamp = _stamp;
                _saidKillsHalf = total > 0 && done * 2 >= total ? 1 : 0;
                _saidLeft = left;
                _saidContact = !fresh;
                _saidRetreat = _retreatMs != 0;
                _saidAllies = allies;
                _seenTagged = tagged;
                _lastWaveLineAt = Time.time;
                return;
            }
            if (_retreatMs != 0)
            {
                if (!_saidRetreat) { _saidRetreat = true; Chatter.ClearQueue(); Say(SIT_RETREAT, ""); }
                return;
            }
            // 아군 격추: 격추된 수가 늘 때마다(4인 협동이라 "혼자 남았다"는 맞지 않는다 — 한 기씩 같은 대사, 사용자)
            if (allies < _saidAllies) { _saidAllies = allies; }
            else if (allies > _saidAllies)
            {
                _saidAllies = allies;
                Say(SIT_ALLY_DOWN, "");
            }
            if (!_saidContact && AnyTaggedNear(ZoneRadius))
            {
                _saidContact = true;
                _lastWaveLineAt = Time.time;
                Say(SIT_CONTACT, "");
            }
            // 새 웨이브: 살아 있는 이 페이즈 적이 늘었다(간격 WaveLineGap)
            if (tagged > _seenTagged && _saidContact && Time.time - _lastWaveLineAt >= WaveLineGap)
            {
                _lastWaveLineAt = Time.time;
                Say(SIT_WAVE, "");
            }
            _seenTagged = tagged;
            if (_saidKillsHalf == 0 && total > 0 && done * 2 >= total)
            {
                _saidKillsHalf = 1;
                Say(SIT_HALF, "");
            }
            if (left < _saidLeft)
            {
                // 이번에 지나친 알림 수(5 · 3 · 1) 중 가장 작은 것 하나 — 지금 남은 수로 말한다
                bool hit = false;
                for (int i = 0; CountdownAt != null && i < CountdownAt.Length; i++)
                {
                    int c = CountdownAt[i];
                    if (c > 0 && left <= c && _saidLeft > c) { hit = true; }
                }
                _saidLeft = left;
                if (hit && left > 0) { Say(SIT_COUNT, left.ToString()); }
            }
        }

        private void Say(int situation, string left) { SayTable(situation, left, LineEvent, LineSpeaker, LineText, LineClip); }

        public override bool MarksObjectiveTag() { return false; }   // 적기에 TGT를 달지 않는다(사용자 "굳이 안 넣어도 될 것 같아") — 태그는 물러남 · 정리에만

        public override string GetDescription()
        {
            if (!IsLive()) { return Description; }
            if (_retreatMs != 0) { return "ENEMY AIR WING IS RETREATING"; }
            return "SHOOT DOWN ENEMY FIGHTERS IN THE ZONE";
        }

        public override string GetProgressText()
        {
            if (Manager == null || !IsLive()) { return ""; }
            string s = "KILLS " + Manager.GetObjectivesDone() + "/" + Manager.GetObjectiveTotal();
            if (_retreatMs == 0 && Generator != null) { s += "  AIR " + Generator.CountAliveFighters(); }
            return s;
        }
    }
}
