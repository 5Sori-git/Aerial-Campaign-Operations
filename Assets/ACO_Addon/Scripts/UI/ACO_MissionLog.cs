using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 작전판(미션 로그/디브리핑 보드, 로컬 전용 — 동기화 없음).
    /// 미션 중에는 격추 기록을 "시각  위치  조종사  무기  표적  점수"로 쌓고(페이즈 시작/종료 줄 포함), 미션이 끝나면 결과/랭크/보너스와
    /// 조종사별 격추 수·점수를 함께 보여 준다. 새 미션이 시작되면(관리자의 시작 시각이 바뀌면) 기록을 지운다.
    ///
    /// 격추 판정은 관리자와 같다: 같은 배치 번호(serial) 동안 "살아 있는 걸 본 뒤 파괴됨"일 때 한 번.
    /// 조종사/무기는 SAV SaccTarget의 LastHitByPlayer/LastHitWeaponType(모든 클라이언트에서 맞은 순간 갱신)을 읽는다.
    /// 로컬 기록이라 중간에 들어온 사람은 들어온 뒤의 격추만 보이고, 관리자 점수와 몇 점 어긋날 수 있다(표시용).
    /// 위치는 격자 좌표(열 A~, 행 1~ 북쪽부터)와 키패드 세분(1~9, 7 = 북서, 9 = 북동, 1 = 남서)으로 표기한다.
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
        public ACO_Radio Radio;
        [Tooltip("제목/결과 줄")]
        public TextMeshPro HeaderText;
        [Tooltip("격추 기록")]
        public TextMeshPro LogText;
        [Tooltip("조종사별 집계")]
        public TextMeshPro PilotText;

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
                AddUnit(ACO_MissionGenerator.CAT_FIGHTER, i, f.transform, f.TargetHealth, "FTR");
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
                    ? "<pos=13%><color=#FF5544>PHASE " + _logCleared + " FAILED - "
                        + (Manager.GetCurrentPhaseType() == ACO_MissionManager.PHASE_CONVOY ? "CONVOY LOST"
                            : (Manager.GetCurrentPhaseType() == ACO_MissionManager.PHASE_TST ? "TARGET ESCAPED" : "VIP LOST")) + "</color>"
                    : "<pos=13%><color=#7FBF8A>PHASE " + _logCleared + " COMPLETE</color>");
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
                PushRow(FormatTime(Manager.GetElapsedSeconds()) + "<pos=13%><color=#FFD24D>PHASE " + (phase + 1) + "/" + Manager.GetPhaseCount()
                    + "  " + Manager.GetPhaseName(Manager.GetCurrentPhaseType())
                    + "  x" + Manager.GetScoreMultiplier().ToString("0.0#") + "</color>");
            }
        }

        void Update()
        {
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
            bool valid = Utilities.IsValid(killer);
            string pilot = valid ? Shorten(killer.displayName) : "UNKNOWN";
            bool mine = valid && killer.isLocal;

            string target;
            if (category == ACO_MissionGenerator.CAT_FIGHTER) { target = "FTR " + TierLabel(Generator.GetFighterTier(i)); }
            else { target = _unitLabel[u]; }
            // 페이즈 목표로 배치된 유닛(태그 있음)은 빨간 TGT
            if (Generator.GetUnitTag(category, i) != 0) { target = "<color=#FF5544>TGT " + target + "</color>"; }

            Transform t = _unitTransform[u];
            string grid = t != null ? GridLabel(t.position) : "-";

            string row = FormatTime(Manager.GetElapsedSeconds())
                + "<pos=13%>" + grid
                + "<pos=27%>" + pilot
                + "<pos=60%>" + WeaponLabel(health.LastHitWeaponType)
                + "<pos=71%>" + target
                + "<pos=90%>+" + points;
            if (mine) { row = "<color=#FFD24D>" + row + "</color>"; }

            PushRow(row);
            AddPilotScore(pilot, points);
            if (mine && Progress) { Progress.AddPersonalScore(points); }
            if (Radio && valid) { Radio.Report(mine ? ACO_Radio.EV_KILL : ACO_Radio.EV_ALLY_KILL, killer.displayName); }
        }

        private void PushRow(string row)
        {
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
            _bodyDirty = true;
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
                HeaderText.text = "OPERATION LOG\n<size=60%>AWAITING MISSION</size>";
                return;
            }

            string kills = "FTR " + Manager.GetFighterKills() + "   GND " + Manager.GetGroundKills()
                + "   PHASE " + Manager.GetPhasesCleared() + "/" + Manager.GetPhaseCount();

            if (state == ACO_MissionManager.STATE_RUNNING)
            {
                string phase = "BRIEFING";
                if (Manager.GetPhaseIndex() >= 0)
                {
                    phase = "PHASE " + (Manager.GetPhaseIndex() + 1) + "/" + Manager.GetPhaseCount() + "  "
                        + Manager.GetPhaseName(Manager.GetCurrentPhaseType());
                }
                HeaderText.text = "OPERATION LOG  -  " + phase
                    + "\n<size=60%>ELAPSED " + FormatTime(Manager.GetElapsedSeconds())
                    + "   SCORE " + Manager.GetTeamScore().ToString("N0")
                    + " (x" + Manager.GetScoreMultiplier().ToString("0.0#") + ")"
                    + "   " + kills + "   WAVE " + Manager.GetWavesSent() + "</size>";
                return;
            }

            bool success = state == ACO_MissionManager.STATE_COMPLETE;
            string result = success ? "<color=#FFD24D>MISSION COMPLETE</color>"
                : "<color=#FF5544>MISSION FAILED" + " (" + Manager.GetFailReasonText(false) + ")" + "</color>";
            int bonus = Manager.GetClearBonus() + Manager.GetTimeBonus() + Manager.GetPhaseBonusTotal();
            HeaderText.text = "DEBRIEFING  -  " + result + "   RANK " + Manager.GetRankLetter()
                + "\n<size=60%>SCORE " + Manager.GetTeamScore().ToString("N0")
                + " (KILLS " + (Manager.GetTeamScore() - bonus).ToString("N0")
                + " + PHASE " + Manager.GetPhaseBonusTotal().ToString("N0")
                + " + CLEAR " + Manager.GetClearBonus().ToString("N0")
                + " + TIME " + Manager.GetTimeBonus().ToString("N0") + ")"
                + "   TIME " + FormatTime(Manager.GetElapsedSeconds())
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
                string s = "<color=#7FBF8A>TIME<pos=13%>GRID<pos=27%>PILOT<pos=60%>WPN<pos=71%>TARGET<pos=90%>PTS</color>";
                int shown = Mathf.Min(_rowCount, Mathf.Max(MaxRows, 1));
                int cap = _rows.Length;
                // 최근 것이 위로
                for (int k = 0; k < shown; k++)
                {
                    s += "\n" + _rows[(_rowStart + _rowCount - 1 - k) % cap];
                }
                if (_totalLogged > shown) { s += "\n<color=#7FBF8A>... " + (_totalLogged - shown) + " EARLIER</color>"; }
                if (_rowCount == 0) { s += "\n<color=#7FBF8A>NO KILLS YET</color>"; }
                LogText.text = s;
            }

            if (PilotText != null)
            {
                string s = "<color=#7FBF8A>PILOT<pos=58%>KILLS<pos=78%>SCORE</color>";
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
                    s += "\n" + line;
                }
                PilotText.text = s;
            }
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

        private string WeaponLabel(byte type)
        {
            if (type == 1 || type == 8) { return "GUN"; }
            if (type == 2) { return "AAM"; }
            if (type == 3) { return "AGM"; }
            if (type == 4) { return "BOMB"; }
            return "-";
        }

        private string TierLabel(int tier)
        {
            if (tier == ACO_NPCFighterAI.SKILL_ACE) { return "ACE"; }
            if (tier == ACO_NPCFighterAI.SKILL_VETERAN) { return "VET"; }
            return "NOV";
        }

        private string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) { return "UNKNOWN"; }
            // TMP 태그로 해석되지 않게 꺾쇠를 바꾼다.
            name = name.Replace("<", "(").Replace(">", ")");
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
