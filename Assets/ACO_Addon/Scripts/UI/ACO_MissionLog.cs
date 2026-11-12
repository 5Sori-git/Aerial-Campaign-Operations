using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using TMPro;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 작전판(미션 로그/디브리핑 보드, 로컬 전용 — 동기화 없음).
    /// 미션 중에는 격추 기록을 "시각  위치  조종사  무기  표적  점수"로 쌓고(페이즈 시작/종료 줄 포함), 미션이 끝나면 결과/랭크/보너스와
    /// 조종사별 격추 수·점수를 함께 보여 준다. 새 미션이 시작되면(관리자의 시작 시각이 바뀌면) 기록을 지운다.
    ///
    /// 격추 판정은 관리자와 같다: 같은 배치 번호(serial) 동안 "살아 있는 걸 본 뒤 파괴됨"일 때 한 번.
    /// 조종사는 SAV SaccTarget의 LastHitByPlayer(모든 클라이언트에서 맞은 순간 갱신)를 읽는다. LastHitWeaponType은 아군 NPC 격추인지만 본다(무장 칸은 없다).
    /// 로컬 기록이라 중간에 들어온 사람은 들어온 뒤의 격추만 보이고, 관리자 점수와 몇 점 어긋날 수 있다(표시용).
    /// 위치는 격자 좌표(열 A~, 행 1~ 북쪽부터)와 키패드 세분(1~9, 7 = 북서, 9 = 북동, 1 = 남서)으로 표기한다.
    /// 최고 획득 순위(BestText): 지금 인스턴스에 있는 사람들의 한 미션 최고 획득 크레딧(PlayerData aco_best_reward)을 높은 순으로.
    /// 다른 사람의 저장값은 같은 인스턴스에 있을 때만 읽을 수 있어 전 세계 순위가 아니다.
    /// 스크롤(ACO_LogScrollButton → Scroll): 기록은 최근 것이 위 — OLDER/NEWER가 반 쪽(MaxRows/2)씩 옮기고 LATEST가 맨 위로. 지난 기록을 보는 동안 새 기록이 와도 보던 줄이 밀리지 않는다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ACO_MissionLog : UdonSharpBehaviour
    {
        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 관리자를 자동으로 넣는다")]
        public ACO_MissionManager Manager;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 생성기를 자동으로 넣는다")]
        public ACO_MissionGenerator Generator;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 진행도를 넣는다. 내 격추 점수를 개인 보상으로 넘기고, 디브리핑에 보상을 띄운다")]
        public ACO_PlayerProgress Progress;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 무전 자막을 넣는다(격추 대사)")]
        [UnityEngine.Serialization.FormerlySerializedAs("Radio")]
        public ACO_Chatter Chatter;
        [Tooltip("제목/결과 줄")]
        public TextMeshPro HeaderText;
        [Tooltip("격추 기록")]
        public TextMeshPro LogText;
        [Tooltip("조종사별 집계")]
        public TextMeshPro PilotText;
        [Tooltip("한 미션 최고 획득 크레딧 순위")]
        public TextMeshPro BestText;
        [Tooltip("이번 미션의 내 격추 수와 획득 크레딧")]
        public TextMeshPro MyResultText;
        [Tooltip("번역 장치(6-166). Bake가 넣는다. 기록 줄의 번역할 말은 [[키|값]] 꼴로 넣어 두고 그릴 때 지금 언어로 바꾼다 — 언어를 바꾸면 지난 줄도 바뀐다")]
        public ACO_Localization Loc;

        [Header("기록")]
        [Tooltip("격추 확인 주기(초)")]
        public float PollInterval = 0.5f;
        [Tooltip("보관할 최대 기록 수(넘으면 오래된 것부터 버린다)")]
        public int MaxEntries = 200;
        [Tooltip("보드에 보일 기록 줄 수(최근 것부터)")]
        public int MaxRows = 16;
        [Tooltip("조종사 집계에 보일 최대 인원")]
        public int MaxPilots = 12;
        [Tooltip("조종사 이름 최대 글자 수")]
        public int NameLength = 14;
        [Tooltip("최고 획득 순위에 보일 인원")]
        public int MaxBestRows = 8;
        [Tooltip("최고 획득 순위를 다시 읽는 주기(초)")]
        public float BestRefreshInterval = 3f;

        [Header("격자 좌표")]
        [Tooltip("작전 구역을 몇 칸으로 나눌지(가로/세로 같음, 최대 26)")]
        public int GridDivisions = 10;
        [Tooltip("칸 안을 키패드식 3x3(1~9)으로 한 번 더 나눠 표기한다")]
        public bool UseKeypad = true;
        [Tooltip("끄면 미션 생성기의 작전 구역(AreaMin/AreaMax)을 쓴다")]
        public bool UseCustomGrid = false;
        [Tooltip("격자 범위(X, Z) — UseCustomGrid일 때만")]
        public Vector2 GridMin = new Vector2(-50000f, -50000f);
        public Vector2 GridMax = new Vector2(50000f, 50000f);

        // 유닛 캐시(생성기 풀 순서: 전투기 → 지상 → 고가치 표적)
        private int _unitCount;
        private int[] _unitCategory;
        private int[] _unitIndex;
        private Transform[] _unitTransform;
        private SaccTarget[] _unitHealth;
        private string[] _unitLabel;
        private int[] _aliveSerial;
        private int[] _loggedSerial;

        // 기록(원형 버퍼)
        private string[] _rows;
        private int _rowStart;
        private int _rowCount;
        private int _scroll;   // 맨 위에서 건너뛴 최근 줄 수(0 = 최신)
        private int _totalLogged;

        // 조종사 집계
        private string[] _pilotName;
        private int[] _pilotKills;
        private int[] _pilotScore;
        private int _pilotCount;

        private bool _hasMission;
        private int _seenStartMs;
        private bool _finalScanned;
        // 기록한 페이즈 진행(시작한 페이즈 번호, 끝난 페이즈 수)
        private int _logPhase = -1;
        private int _logCleared;
        private int _logPhaseType;
        private bool _bodyDirty = true;
        private float _pollTimer;

        void Start()
        {
            BuildUnitList();
            int cap = Mathf.Max(MaxEntries, 1);
            _rows = new string[cap];
            _pilotName = new string[64];
            _pilotKills = new int[64];
            _pilotScore = new int[64];
            RefreshHeader();
            RefreshBody();
        }

        private void BuildUnitList()
        {
            int fighters = (Generator != null && Generator.Fighters != null) ? Generator.Fighters.Length : 0;
            int ground = (Generator != null && Generator.GroundUnits != null) ? Generator.GroundUnits.Length : 0;
            int hvts = (Generator != null && Generator.HighValueTargets != null) ? Generator.HighValueTargets.Length : 0;
            int parts = Generator != null ? Generator.GetUnitCount(ACO_MissionGenerator.CAT_PART) : 0;
            int total = fighters + ground + hvts + parts;

            _unitCategory = new int[total];
            _unitIndex = new int[total];
            _unitTransform = new Transform[total];
            _unitHealth = new SaccTarget[total];
            _unitLabel = new string[total];
            _aliveSerial = new int[total];
            _loggedSerial = new int[total];
            _unitCount = 0;

            for (int i = 0; i < fighters; i++)
            {
                ACO_NPCFighterAI f = Generator.Fighters[i];
                if (f == null) { continue; }
                AddUnit(ACO_MissionGenerator.CAT_FIGHTER, i, f.transform, f.TargetHealth, string.IsNullOrEmpty(f.MarkerLabel) ? "FTR" : f.MarkerLabel);
            }
            for (int i = 0; i < ground; i++)
            {
                UdonSharpBehaviour g = Generator.GroundUnits[i];
                if (g == null) { continue; }
                AddUnit(ACO_MissionGenerator.CAT_GROUND, i, g.transform, g.GetComponent<SaccTarget>(), ReadLabel(g, "GND"));
            }
            for (int i = 0; i < hvts; i++)
            {
                UdonSharpBehaviour h = Generator.HighValueTargets[i];
                if (h == null) { continue; }
                AddUnit(ACO_MissionGenerator.CAT_HVT, i, h.transform, h.GetComponent<SaccTarget>(), ReadLabel(h, "TGT"));
            }
            for (int i = 0; i < parts; i++)
            {
                UdonSharpBehaviour pt = Generator.GetUnitBehaviour(ACO_MissionGenerator.CAT_PART, i);
                if (pt == null) { continue; }
                AddUnit(ACO_MissionGenerator.CAT_PART, i, pt.transform, pt.GetComponent<SaccTarget>(), ReadLabel(pt, "PRT"));
            }
            ResetTracking();
        }

        private void AddUnit(int category, int index, Transform t, SaccTarget health, string label)
        {
            _unitCategory[_unitCount] = category;
            _unitIndex[_unitCount] = index;
            _unitTransform[_unitCount] = t;
            _unitHealth[_unitCount] = health;
            _unitLabel[_unitCount] = label;
            _unitCount++;
        }

        private string ReadLabel(UdonSharpBehaviour unit, string fallback)
        {
            object value = unit.GetProgramVariable("MarkerLabel");
            if (value == null) { return fallback; }
            string s = (string)value;
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        private void ResetTracking()
        {
            for (int u = 0; u < _unitCount; u++)
            {
                _aliveSerial[u] = -1;
                _loggedSerial[u] = -1;
            }
        }

        private void ClearLog()
        {
            _rowStart = 0;
            _rowCount = 0;
            _scroll = 0;
            _totalLogged = 0;
            _pilotCount = 0;
            _finalScanned = false;
            ResetTracking();
            // 진행 중인 미션에 늦게 들어왔으면 지난 페이즈는 기록하지 않는다(시각이 맞지 않으므로).
            bool late = Manager.GetElapsedSeconds() > 10f;
            _logPhase = late ? Manager.GetPhaseIndex() : -1;
            _logPhaseType = late ? Manager.GetCurrentPhaseType() : 0;
            _logCleared = late ? Manager.GetPhasesCleared() : 0;
            _bodyDirty = true;
        }

        /// 페이즈 시작/종료를 기록 줄로 남긴다.
        private void CheckPhases()
        {
            int cleared = Manager.GetPhasesCleared();
            while (_logCleared < cleared)
            {
                _logCleared++;
                bool failed = _logCleared == cleared && Manager.GetLastPhaseFailed();
                string row = FormatTime(Manager.GetElapsedSeconds()) + (failed
                    ? "<pos=13%><color=#FF5544>[[log.phasefailed|" + _logCleared + "]] - "
                        + Manager.GetPhaseFailText(Manager.GetCurrentPhaseType()) + "</color>"
                    : "<pos=13%><color=#7FBF8A>[[log.phasecomplete|" + _logCleared + "]]</color>");
                // 보너스는 마지막 페이즈 것만 알 수 있다.
                if (_logCleared == cleared && Manager.GetLastPhaseBonus() > 0) { row += "<pos=90%>+" + Manager.GetLastPhaseBonus(); }
                PushRow(row);
            }
            int phase = Manager.GetPhaseIndex();
            // 배치가 안 돼 같은 페이즈가 다른 종류로 바뀐 경우도 새 줄로 남긴다.
            int type = Manager.GetCurrentPhaseType();
            bool changed = phase == _logPhase && phase >= 0 && Manager.IsPhaseActive() && type != _logPhaseType;
            if (phase > _logPhase || changed)
            {
                _logPhase = phase;
                _logPhaseType = type;
                PushRow(FormatTime(Manager.GetElapsedSeconds()) + "<pos=13%><color=#FFD24D>[[log.phase|" + (phase + 1) + "/" + Manager.GetPhaseCount() + "]]"
                    + "  " + Manager.GetPhaseName(Manager.GetCurrentPhaseType())
                    + "  x" + Manager.GetScoreMultiplier().ToString("0.0#") + "</color>");
            }
        }

        private const int MAX_PLAYERS = 100;
        private VRCPlayerApi[] _bestPlayers = new VRCPlayerApi[MAX_PLAYERS];
        private int[] _bestValues = new int[MAX_PLAYERS];
        private float _bestTimer;
        private string _bestShown = "";

        /// 높은 순으로 MaxBestRows명. 1~3위는 금·은·동, 나는 노란색. 기록이 없는 사람은 뺀다.
        private void RefreshBest()
        {
            if (BestText == null) { return; }
            int n = Mathf.Min(VRCPlayerApi.GetPlayerCount(), MAX_PLAYERS);
            VRCPlayerApi.GetPlayers(_bestPlayers);
            for (int i = 0; i < n; i++)
            {
                VRCPlayerApi p = _bestPlayers[i];
                int v;
                _bestValues[i] = Utilities.IsValid(p) && PlayerData.TryGetInt(p, ACO_PlayerProgress.KEY_BEST_REWARD, out v) ? v : 0;
            }
            string s = "<color=#7FBF8A>" + T("log.bestcol", "PILOT<pos=62%>BEST CR") + "</color>";
            int rows = 0;
            for (int k = 0; k < Mathf.Max(MaxBestRows, 1); k++)
            {
                int best = -1;
                for (int i = 0; i < n; i++)
                {
                    if (_bestValues[i] > 0 && (best < 0 || _bestValues[i] > _bestValues[best])) { best = i; }
                }
                if (best < 0) { break; }
                VRCPlayerApi p = _bestPlayers[best];
                string line = (k + 1) + ". " + Shorten(p.displayName) + "<pos=62%>" + _bestValues[best].ToString("N0");
                string color = p.isLocal ? "#FFD24D" : (k == 0 ? "#FFE9A8" : (k == 1 ? "#D5DDE0" : (k == 2 ? "#E0A878" : "")));
                s += "\n" + (color != "" ? "<color=" + color + ">" + line + "</color>" : line);
                _bestValues[best] = 0;
                rows++;
            }
            if (rows == 0) { s += "\n<color=#7FBF8A>" + T("log.norecords", "NO RECORDS YET") + "</color>"; }
            if (s == _bestShown) { return; }
            _bestShown = s;
            BestText.text = s;
        }

        private string _myResultShown = "";

        /// 이번 미션의 내 결과. 진행 중에는 지금 끝나면 받을 예상 값(~)을, 보상이 나오면 확정 값을 보여 준다.
        private void RefreshMyResult()
        {
            if (MyResultText == null || Progress == null) { return; }
            string kills = "-";
            string credits = "-";
            if (_hasMission)
            {
                kills = Progress.GetMissionKills().ToString();
                credits = Progress.HasRewardFor(_seenStartMs)
                    ? "<color=#FFD24D>+" + Progress.GetLastReward().ToString("N0") + " CR</color>"
                    : "~" + Progress.GetMissionRewardPreview().ToString("N0") + " CR";
            }
            string s = T("log.myresult", "KILLS<pos=45%>{0}\nCREDITS<pos=45%>{1}").Replace("{0}", kills).Replace("{1}", credits);
            if (s == _myResultShown) { return; }
            _myResultShown = s;
            MyResultText.text = s;
        }

        void Update()
        {
            _bestTimer -= Time.deltaTime;
            if (_bestTimer <= 0f)
            {
                _bestTimer = BestRefreshInterval;
                RefreshBest();
            }
            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) { return; }
            _pollTimer = PollInterval;
            if (Manager == null || Generator == null) { return; }

            int state = Manager.GetState();
            if (state != ACO_MissionManager.STATE_IDLE)
            {
                int start = Manager.GetMissionStartMs();
                if (!_hasMission || start != _seenStartMs)
                {
                    _hasMission = true;
                    _seenStartMs = start;
                    ClearLog();
                }
            }

            if (state == ACO_MissionManager.STATE_RUNNING)
            {
                Scan();
            }
            else if (state != ACO_MissionManager.STATE_IDLE && !_finalScanned)
            {
                // 끝나는 순간 한 번 더 본다(마지막 목표 격추가 상태 변경과 같이 들어오는 경우). 그 뒤 격추는 점수가 없으니 기록하지 않는다.
                Scan();
                _finalScanned = true;
            }
            if (state != ACO_MissionManager.STATE_IDLE && _hasMission) { CheckPhases(); }

            RefreshHeader();
            RefreshMyResult();
            if (_bodyDirty) { RefreshBody(); }
        }

        private void Scan()
        {
            for (int u = 0; u < _unitCount; u++)
            {
                int category = _unitCategory[u];
                int i = _unitIndex[u];
                if (!Generator.IsUnitUsed(category, i)) { continue; }
                int serial = Generator.GetUnitSerial(category, i);
                SaccTarget health = _unitHealth[u];
                if (health == null) { continue; }

                if (health.Health > 0f)
                {
                    _aliveSerial[u] = serial;
                    continue;
                }
                if (_aliveSerial[u] != serial || _loggedSerial[u] == serial) { continue; }

                _loggedSerial[u] = serial;
                AddEntry(u, category, i, health);
            }
        }

        private void AddEntry(int u, int category, int i, SaccTarget health)
        {
            int points = Manager.GetKillScore(category, i);

            VRCPlayerApi killer = health.LastHitByPlayer;
            // 아군 NPC(호위·대공 등)의 무기 — 팀 점수에는 들어가지만(관리자가 센다) 개인 점수·조종사 집계에는 넣지 않는다.
            bool npcKill = health.LastHitWeaponType >= ACO_Missile.NPC_WEAPON_OFFSET;
            bool valid = !npcKill && Utilities.IsValid(killer);
            string pilot = npcKill ? "<color=#66BBFF>[[log.ally]]</color>" : (valid ? Shorten(killer.displayName) : "[[log.unknown]]");
            bool mine = valid && killer.isLocal;

            string target;
            if (category == ACO_MissionGenerator.CAT_FIGHTER)
            {
                ACO_NPCFighterAI f = Generator.Fighters != null && i < Generator.Fighters.Length ? Generator.Fighters[i] : null;
                int tier = Generator.GetFighterTier(i);
                target = f != null ? f.GetDisplayLabel(tier) : "FTR";
            }
            else { target = _unitLabel[u]; }
            // 페이즈 목표로 배치된 유닛(태그 있음)은 빨간 TGT
            if (Generator.GetUnitTag(category, i) != 0) { target = "<color=#FF5544>TGT " + target + "</color>"; }

            Transform t = _unitTransform[u];
            string grid = t != null ? GridLabel(t.position) : "-";

            string row = FormatTime(Manager.GetElapsedSeconds())
                + "<pos=13%>" + grid
                + "<pos=27%>" + pilot
                + "<pos=62%>" + target
                + "<pos=90%>+" + points;
            if (mine) { row = "<color=#FFD24D>" + row + "</color>"; }

            PushRow(row);
            if (!npcKill) { AddPilotScore(pilot, points); }
            if (mine && Progress)
            {
                Progress.AddPersonalScore(points);
                Progress.AddMissionKill();
            }
            if (Chatter && valid) { Chatter.Report(mine ? ACO_Chatter.EV_KILL : ACO_Chatter.EV_ALLY_KILL, killer.displayName); }
        }

        private void PushRow(string row)
        {
            // 지난 기록을 보는 중이면 새 줄만큼 함께 내려 보던 줄을 그대로 둔다
            if (_scroll > 0) { _scroll++; }
            int cap = _rows.Length;
            if (_rowCount < cap)
            {
                _rows[(_rowStart + _rowCount) % cap] = row;
                _rowCount++;
            }
            else
            {
                _rows[_rowStart] = row;
                _rowStart = (_rowStart + 1) % cap;
            }
            _totalLogged++;
            _scroll = Mathf.Min(_scroll, MaxScroll());
            _bodyDirty = true;
        }

        /// 스크롤(버튼): step > 0 = 더 오래된 쪽, < 0 = 최근 쪽(반 쪽씩), 0 = 맨 위(최신). 바뀌었으면 true.
        public bool Scroll(int step)
        {
            int before = _scroll;
            if (step == 0) { _scroll = 0; }
            else
            {
                int page = Mathf.Max(Mathf.Max(MaxRows, 1) / 2, 1);
                _scroll = Mathf.Clamp(_scroll + step * page, 0, MaxScroll());
            }
            if (_scroll == before) { return false; }
            RefreshBody();
            return true;
        }

        private int MaxScroll()
        {
            return Mathf.Max(_rowCount - Mathf.Max(MaxRows, 1), 0);
        }

        private void AddPilotScore(string pilot, int points)
        {
            for (int p = 0; p < _pilotCount; p++)
            {
                if (_pilotName[p] == pilot)
                {
                    _pilotKills[p]++;
                    _pilotScore[p] += points;
                    return;
                }
            }
            if (_pilotCount >= _pilotName.Length) { return; }
            _pilotName[_pilotCount] = pilot;
            _pilotKills[_pilotCount] = 1;
            _pilotScore[_pilotCount] = points;
            _pilotCount++;
        }

        // ───────────── 표시 ─────────────

        private void RefreshHeader()
        {
            if (HeaderText == null) { return; }
            int state = Manager != null ? Manager.GetState() : ACO_MissionManager.STATE_IDLE;

            if (state == ACO_MissionManager.STATE_IDLE || Manager == null)
            {
                HeaderText.text = T("log.title", "OPERATION LOG") + "\n<size=60%>" + T("log.awaiting", "AWAITING MISSION") + "</size>";
                return;
            }

            string kills = T2("log.kills", "FTR {0}   GND {1}", Manager.GetFighterKills().ToString(), Manager.GetGroundKills().ToString())
                + "   " + T1("log.phase", "PHASE {0}", Manager.GetPhasesCleared() + "/" + Manager.GetPhaseCount());

            if (state == ACO_MissionManager.STATE_RUNNING)
            {
                string phase = T("flow.ban.briefing", "BRIEFING");
                if (Manager.GetPhaseIndex() >= 0)
                {
                    phase = T1("log.phase", "PHASE {0}", (Manager.GetPhaseIndex() + 1) + "/" + Manager.GetPhaseCount()) + "  "
                        + Manager.GetPhaseName(Manager.GetCurrentPhaseType());
                }
                HeaderText.text = T("log.title", "OPERATION LOG") + "  -  " + phase
                    + "\n<size=60%>" + T1("log.elapsed", "ELAPSED {0}", FormatTime(Manager.GetElapsedSeconds()))
                    + "   " + T1("log.score", "SCORE {0}", Manager.GetTeamScore().ToString("N0"))
                    + " (x" + Manager.GetScoreMultiplier().ToString("0.0#") + ")"
                    + "   " + kills + "   " + T1("log.wave", "WAVE {0}", Manager.GetWavesSent().ToString()) + "</size>";
                return;
            }

            bool success = state == ACO_MissionManager.STATE_COMPLETE;
            string result = success ? "<color=#FFD24D>" + T("log.complete", "MISSION COMPLETE") + "</color>"
                : "<color=#FF5544>" + T("log.failed", "MISSION FAILED") + " (" + Manager.GetFailReasonText(false) + ")" + "</color>";
            int bonus = Manager.GetClearBonus() + Manager.GetTimeBonus() + Manager.GetPhaseBonusTotal();
            string breakdown = T("log.breakdown", "(KILLS {0} + PHASE {1} + CLEAR {2} + TIME {3})")
                .Replace("{0}", (Manager.GetTeamScore() - bonus).ToString("N0")).Replace("{1}", Manager.GetPhaseBonusTotal().ToString("N0"))
                .Replace("{2}", Manager.GetClearBonus().ToString("N0")).Replace("{3}", Manager.GetTimeBonus().ToString("N0"));
            HeaderText.text = T("log.debrief", "DEBRIEFING") + "  -  " + result + "   " + T1("log.rank", "RANK {0}", Manager.GetRankLetter())
                + "\n<size=60%>" + T1("log.score", "SCORE {0}", Manager.GetTeamScore().ToString("N0")) + " " + breakdown
                + "   " + T1("log.time", "TIME {0}", FormatTime(Manager.GetElapsedSeconds()))
                + "   " + kills + "</size>";
            if (Progress && Progress.HasRewardFor(Manager.GetMissionStartMs()))
            {
                HeaderText.text += "\n<size=60%><color=#FFD24D>" + Progress.GetLastRewardSummary() + "</color></size>";
            }
        }

        private void RefreshBody()
        {
            _bodyDirty = false;

            if (LogText != null)
            {
                string s = "<color=#7FBF8A>" + T("log.col", "TIME<pos=13%>GRID<pos=27%>PILOT<pos=62%>TARGET<pos=90%>PTS") + "</color>";
                int scroll = Mathf.Clamp(_scroll, 0, MaxScroll());
                int shown = Mathf.Min(_rowCount - scroll, Mathf.Max(MaxRows, 1));
                int cap = _rows.Length;
                // 위에 숨은 최근 기록(스크롤 중)
                if (scroll > 0) { s += "\n<color=#FFD24D>" + T1("log.newer", "^ {0} NEWER  (LATEST)", scroll.ToString()) + "</color>"; }
                // 최근 것이 위로(스크롤만큼 건너뛰고)
                for (int k = 0; k < shown; k++)
                {
                    s += "\n" + RenderRow(_rows[(_rowStart + _rowCount - 1 - scroll - k) % cap]);
                }
                int earlier = _totalLogged - scroll - shown;
                if (earlier > 0) { s += "\n<color=#7FBF8A>" + T1("log.earlier", "v {0} EARLIER", earlier.ToString()) + (_totalLogged > _rowCount ? "  " + T1("log.notkept", "({0} NOT KEPT)", (_totalLogged - _rowCount).ToString()) : "") + "</color>"; }
                if (_rowCount == 0) { s += "\n<color=#7FBF8A>" + T("log.nokills", "NO KILLS YET") + "</color>"; }
                LogText.text = s;
            }

            if (PilotText != null)
            {
                string s = "<color=#7FBF8A>" + T("log.pilotcol", "PILOT<pos=58%>KILLS<pos=78%>SCORE") + "</color>";
                // 점수 높은 순으로 MaxPilots명까지(인원이 적으니 매번 남은 사람 중 최고를 고른다)
                bool[] used = new bool[_pilotCount];
                int shown = Mathf.Min(_pilotCount, Mathf.Max(MaxPilots, 1));
                string localName = Networking.LocalPlayer != null ? Shorten(Networking.LocalPlayer.displayName) : "";
                for (int k = 0; k < shown; k++)
                {
                    int best = -1;
                    for (int p = 0; p < _pilotCount; p++)
                    {
                        if (used[p]) { continue; }
                        if (best < 0 || _pilotScore[p] > _pilotScore[best]) { best = p; }
                    }
                    used[best] = true;
                    string line = (k + 1) + ". " + _pilotName[best]
                        + "<pos=58%>" + _pilotKills[best]
                        + "<pos=78%>" + _pilotScore[best].ToString("N0");
                    if (_pilotName[best] == localName) { line = "<color=#FFD24D>" + line + "</color>"; }
                    s += "\n" + RenderRow(line);
                }
                PilotText.text = s;
            }
        }

        // ───────────── 번역(6-166) ─────────────

        /// 번역 장치가 언어를 바꾸면 부른다. 다음 주기에 판 전체를 다시 쓴다(지난 기록 줄 포함).
        public void LocalizationChanged()
        {
            _bodyDirty = true;
            _bestShown = "";
            _myResultShown = "";
            _bestTimer = 0f;
            _pollTimer = 0f;
        }

        /// 번역 표의 문장(장치가 없거나 표에 없으면 english).
        private string T(string key, string english)
        {
            if (Loc == null) { return english; }
            string s = Loc.Get(key);
            return s == key ? english : s;
        }
        private string T1(string key, string english, string a) { return T(key, english).Replace("{0}", a); }
        private string T2(string key, string english, string a, string b) { return T(key, english).Replace("{0}", a).Replace("{1}", b); }

        /// 토큰의 영어(장치가 없을 때).
        private string TokenEnglish(string key)
        {
            if (key == "log.phase") { return "PHASE {0}"; }
            if (key == "log.phasecomplete") { return "PHASE {0} COMPLETE"; }
            if (key == "log.phasefailed") { return "PHASE {0} FAILED"; }
            if (key == "log.ally") { return "ALLY UNIT"; }
            if (key == "log.unknown") { return "UNKNOWN"; }
            return key;
        }

        /// 기록 줄의 [[키]] · [[키|값]]을 지금 언어 문장으로 바꾼다.
        private string RenderRow(string row)
        {
            int a = row.IndexOf("[[");
            int guard = 0;
            while (a >= 0 && guard < 8)
            {
                guard++;
                int b = row.IndexOf("]]", a + 2);
                if (b < 0) { break; }
                string tok = row.Substring(a + 2, b - a - 2);
                int bar = tok.IndexOf('|');
                string key = bar >= 0 ? tok.Substring(0, bar) : tok;
                string arg = bar >= 0 ? tok.Substring(bar + 1) : "";
                string text = T1(key, TokenEnglish(key), arg);
                row = row.Substring(0, a) + text + row.Substring(b + 2);
                a = row.IndexOf("[[", a + text.Length);
            }
            return row;
        }

        // ───────────── 형식 ─────────────

        /// 격자 좌표. 열은 X(서→동) A, B, C…, 행은 Z(북→남) 1, 2, 3…, 키패드는 칸 안의 3x3(7 8 9 / 4 5 6 / 1 2 3).
        private string GridLabel(Vector3 pos)
        {
            Vector2 min = UseCustomGrid ? GridMin : Generator.AreaMin;
            Vector2 max = UseCustomGrid ? GridMax : Generator.AreaMax;
            int n = Mathf.Clamp(GridDivisions, 1, 26);
            float fx = Mathf.InverseLerp(min.x, max.x, pos.x) * n;
            float fzFromNorth = Mathf.InverseLerp(max.y, min.y, pos.z) * n;

            int col = Mathf.Clamp(Mathf.FloorToInt(fx), 0, n - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(fzFromNorth), 0, n - 1);
            string label = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Substring(col, 1) + (row + 1);
            if (!UseKeypad) { return label; }

            int kx = Mathf.Clamp(Mathf.FloorToInt((fx - col) * 3f), 0, 2);
            int kzFromNorth = Mathf.Clamp(Mathf.FloorToInt((fzFromNorth - row) * 3f), 0, 2);
            int keypad = (2 - kzFromNorth) * 3 + kx + 1;
            return label + "-" + keypad;
        }

        private string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) { return "[[log.unknown]]"; }
            // TMP 태그로 해석되지 않게 꺾쇠를 바꾼다. 번역 토큰([[ ]])으로 읽히지 않게 겹대괄호도 끊는다
            name = name.Replace("<", "(").Replace(">", ")").Replace("[[", "[ [").Replace("]]", "] ]");
            return name.Length > NameLength ? name.Substring(0, NameLength) : name;
        }

        private string FormatTime(float seconds)
        {
            int s = Mathf.Max(Mathf.FloorToInt(seconds), 0);
            int m = s / 60;
            s = s % 60;
            return m.ToString("00") + ":" + s.ToString("00");
        }
    }
}
