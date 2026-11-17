using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace ACO
{
    /// 터널 은신 발사대 페이즈(6-271): 터널 랜드마크 안에 TEL TelCount대가 숨어 있다가 Interval초마다 하나씩 가까운 입구로 나와(초속 10m)
    /// 입구 밖 150m에 멈추고 10초 만에 레일을 세워 쏜다(ACO_TelUnit 터널 모드). 쏜 탄도탄을 요격 시간(TEL FlightTime) 안에 못 잡으면 미션 실패.
    /// 터널 안에 숨은 TEL도 칠 수 있다(터널 안으로 날아 들어가 기관포 · 로켓). 바깥 폭격은 터널 바위에 막힌다(ACO_TerrainFeatures.SplashBlockedByTunnel).
    /// 방공은 생성기가 함께 둔다(터널 안 대공포 1~2, 입구마다 대공포 · SAM).
    ///
    /// 자체 동기화(Manual): 도장(_stamp), 터널 번호(_tunnel), 태그(_tag — 관리자가 시도마다 주는 태그. 고정 MakePhaseTag(1)은 같은 페이즈 번호의 시한 표적 TEL 태그와 겹쳤다). TEL 출격 · 발사 시각은 생성기 배치 시각(동기화)이 정하고, 파괴는 체력 동기화가 정한다.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_PhaseTunnel : ACO_PhaseModule
    {
        public const int MAX_TELS = 4;

        [Header("터널 은신 발사대")]
        public int TelCount = 3;
        [Tooltip("페이즈 시작부터 첫 TEL이 나오기 시작할 때까지(초)")]
        public float FirstDelay = 30f;
        [Tooltip("TEL이 나오는 간격(초)")]
        public float Interval = 90f;
        [Tooltip("요격 시간이 지난 뒤 이만큼(초) 더 기다려 실패로 본다(피해 이벤트 지연)")]
        public float EscapeGrace = 3f;
        [Tooltip("잡은 TEL 하나당 보너스(페이즈 배율)")]
        public int HitBonus = 300;

        [Header("대사 (관제 — 비우면 안 나온다)")]
        [Tooltip("Bake가 넣는다")]
        public ACO_Chatter Chatter;
        public float VoiceQueueDelay = 8f;
        [TextArea] public string LineEmerge = "A TEL is rolling out of the tunnel! Hit it before it sets up!";
        public AudioClip LineEmergeClip;
        [Tooltip("발사 · 지상 격파 · 요격 대사는 시한 표적 페이즈와 같은 문장 · 음성(TEL/*)")]
        [TextArea] public string LineLaunch = "Launch detected! Missile in the air! Intercept it before it leaves the atmosphere!";
        public AudioClip LineLaunchClip;
        [TextArea] public string LineTelDown = "TEL destroyed on the ground. Good kill.";
        public AudioClip LineTelDownClip;
        [TextArea] public string LineMissileDown = "Missile intercepted! Splash one!";
        public AudioClip LineMissileDownClip;

        [UdonSynced] private int _stamp;
        [UdonSynced] private int _tunnel = -1;
        [UdonSynced] private int _tag;

        private int _planTunnel = -1;

        // 모든 화면 — 대사 상태(TEL 순번 = 지상 풀 번호 작은 순)
        private ACO_TelUnit[] _tels = new ACO_TelUnit[MAX_TELS];
        private int _telCount;
        private int _telsStamp;
        private int _telsSerial = -1;
        private int[] _telIdx = new int[MAX_TELS];
        private bool[] _saidEmerge = new bool[MAX_TELS];
        private bool[] _saidLaunch = new bool[MAX_TELS];
        private bool[] _saidDown = new bool[MAX_TELS];
        private int _lineStamp;
        private float _lineTimer;

        private bool IsCurrent() { return Manager != null && _stamp != 0 && _stamp == CurrentStamp(); }
        private int Tag() { return _tag; }

        // ───────────── 오너 ─────────────

        public override bool IsFeasible(int phaseIndex, Vector3 focus)
        {
            _planTunnel = -1;
            if (Generator == null || Generator.Features == null || !Generator.CanDeployTunnel(TelCount)) { return false; }
            ACO_TerrainFeatures f = Generator.Features;
            float best = float.MaxValue;
            for (int k = 0; k < f.GetTunnelCount(); k++)
            {
                Vector3 c = f.GetTunnelCenter(k);
                float d = (c.x - focus.x) * (c.x - focus.x) + (c.z - focus.z) * (c.z - focus.z);
                if (d < best) { best = d; _planTunnel = k; }
            }
            return _planTunnel >= 0;
        }

        public override void Begin(int phaseIndex, int tag, Vector3 focus, int now)
        {
            _stamp = CurrentStamp();
            _tunnel = _planTunnel;
            _tag = tag;
            int t = Tag();
            Manager.SetModuleActiveTag(t);
            Manager.SetModuleProgress(0, Mathf.Max(TelCount, 1), false);
            ACO_TerrainFeatures f = Generator.Features;
            Manager.SetModuleSite(f != null ? f.GetTunnelSite(_tunnel) : -1);
            Vector3 center = f != null ? f.GetTunnelCenter(_tunnel) : focus;
            Generator.RequestDeployObjective(ACO_MissionGenerator.OBJ_TUNNEL, t, TelCount, _tunnel, center, FirstDelay, Interval);
            SyncNow();
        }

        public override int Tick(int now)
        {
            if (!IsCurrent())
            {
                // 이번 페이즈 값을 못 받은 새 오너: 태그는 관리자 목표 태그(동기화)에서
                _stamp = CurrentStamp();
                _tag = Manager.GetActiveTag();
                SyncNow();
            }
            int t = Tag();
            Manager.SetModuleActiveTag(t);
            RefreshTels();
            // 요격 시간이 지난 탄도탄이 하나라도 있으면 실패
            int grace = Mathf.RoundToInt(Mathf.Max(EscapeGrace, 0f) * 1000f);
            for (int k = 0; k < _telCount; k++)
            {
                ACO_TelUnit tel = _tels[k];
                if (tel == null || !TelApplied(k) || tel.IsDestroyed() || !tel.IsAirborne()) { continue; }   // 적용 전 TEL은 지난 미션 상태(발사됨)일 수 있다
                int deadline = tel.GetLaunchMs() + Mathf.RoundToInt(tel.FlightTime * 1000f);
                if (now - deadline >= grace)
                {
                    Debug.Log("[ACO] 터널 은신 발사대: 탄도탄 요격 실패 — 미션 실패");
                    Generator.RequestReleaseTag(t);
                    return RESULT_FAIL;
                }
            }
            Manager.CountTaggedObjectives(t);
            int total = Manager.GetTaggedTotal();
            if (total == 0)
            {
                if (now - Manager.GetPhaseStartMs() >= Mathf.RoundToInt(Manager.GetDeployTimeout() * 1000f)) { return RESULT_REPLACE; }
                return RESULT_RUNNING;
            }
            Manager.SetModuleProgress(Manager.GetTaggedDone(), total, true);
            if (Manager.GetTaggedDone() >= total) { return RESULT_CLEAR; }
            return RESULT_RUNNING;
        }

        public override int GetClearBonus() { return Manager != null ? Manager.GetTaggedDone() * HitBonus : 0; }

        public override void End()
        {
            _planTunnel = -1;
        }

        // ───────────── 모든 화면 ─────────────

        private void RefreshTels()
        {
            if (Generator == null) { _telCount = 0; return; }
            int deploy = Generator.GetDeployCount();
            if (_telsStamp == _stamp && _telsSerial == deploy) { return; }
            _telsStamp = _stamp;
            _telsSerial = deploy;
            _telCount = 0;
            int t = Tag();
            int n = Generator.GetUnitCount(ACO_MissionGenerator.CAT_GROUND);
            for (int i = 0; i < n && _telCount < MAX_TELS; i++)
            {
                if (!Generator.IsUnitUsed(ACO_MissionGenerator.CAT_GROUND, i) || Generator.GetUnitTag(ACO_MissionGenerator.CAT_GROUND, i) != t) { continue; }
                if (!Generator.IsTelUnit(i)) { continue; }
                _tels[_telCount] = Generator.GroundUnits[i].GetComponent<ACO_TelUnit>();
                _telIdx[_telCount] = i;
                _telCount++;
            }
        }

        /// TEL k에 이 화면에서 이번 배치가 적용됐는가(생성기 적용은 OnDeserialization 2프레임 뒤 + 프레임당 한도).
        private bool TelApplied(int k)
        {
            ACO_TelUnit tel = _tels[k];
            return tel != null && tel.GetStartMs() == Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_GROUND, _telIdx[k]);
        }

        void Update()
        {
            _lineTimer -= Time.deltaTime;
            if (_lineTimer > 0f) { return; }
            _lineTimer = 0.25f;
            if (!IsCurrent() || Generator == null) { return; }
            RefreshTels();
            if (_telCount == 0) { return; }
            CheckLines();
        }

        private void CheckLines()
        {
            int now = Networking.GetServerTimeInMilliseconds();
            bool first = _lineStamp != _stamp;
            if (first)
            {
                // 기준은 모든 TEL에 이번 배치가 적용된 뒤(늦게 켜진 화면은 적용이 몇 프레임 늦다), 배치 1.5초 뒤에 잡는다
                // (다시 쓰는 TEL은 체력이 차기 전 몇 프레임 동안 부서진 것으로 보인다)
                for (int k = 0; k < _telCount; k++)
                {
                    if (!TelApplied(k)) { return; }
                    if (now - Generator.GetUnitDeployTimeMs(ACO_MissionGenerator.CAT_GROUND, _telIdx[k]) < 1500) { return; }
                }
            }
            _lineStamp = _stamp;
            for (int k = 0; k < _telCount; k++)
            {
                ACO_TelUnit tel = _tels[k];
                if (tel == null) { continue; }
                bool dead = tel.IsDestroyed();
                bool airborne = tel.IsAirborne();
                bool emerging = tel.IsTunnelMode() && now - tel.GetEmergeMs() >= 0;
                if (first)
                {
                    // 이 페이즈를 처음 본다: 지금 상태를 기준으로(늦게 들어온 클라이언트가 지난 대사를 듣지 않게)
                    _saidEmerge[k] = emerging || dead;
                    _saidLaunch[k] = airborne || dead;
                    _saidDown[k] = dead;
                    continue;
                }
                if (dead && !_saidDown[k])
                {
                    _saidDown[k] = true;
                    _saidEmerge[k] = true;
                    if (_saidLaunch[k]) { Say(LineMissileDown, LineMissileDownClip); }
                    else { Say(LineTelDown, LineTelDownClip); }
                    _saidLaunch[k] = true;
                    continue;
                }
                if (dead) { continue; }
                if (emerging && !_saidEmerge[k])
                {
                    _saidEmerge[k] = true;
                    Say(LineEmerge, LineEmergeClip);
                }
                if (airborne && !_saidLaunch[k])
                {
                    _saidLaunch[k] = true;
                    Say(LineLaunch, LineLaunchClip);
                }
            }
        }

        private void Say(string text, AudioClip clip)
        {
            if (Chatter != null && !string.IsNullOrEmpty(text)) { Chatter.SayControl(text, clip, VoiceQueueDelay); }
        }

        /// "TEL 1/3  NEXT 0:42" → 나와 있으면 "LAUNCH 0:07"(빨강), 날고 있으면 "INTERCEPT 0:45"(빨강).
        public override string GetProgressText()
        {
            if (!IsCurrent() || Generator == null) { return "-"; }
            RefreshTels();
            int now = Networking.GetServerTimeInMilliseconds();
            int dead = 0;
            string urgent = "";
            int nextEmerge = int.MaxValue;
            bool hasNext = false;
            for (int k = 0; k < _telCount; k++)
            {
                ACO_TelUnit tel = _tels[k];
                if (tel == null || !TelApplied(k)) { continue; }   // 아직 적용 전(지난 상태)은 세지 않는다
                if (tel.IsDestroyed()) { dead++; continue; }
                if (tel.IsAirborne())
                {
                    int left = tel.GetLaunchMs() + Mathf.RoundToInt(tel.FlightTime * 1000f) - now;
                    urgent = "  <color=#FF5544>INTERCEPT " + Clock(left) + "</color>";
                    continue;
                }
                int emerge = tel.GetEmergeMs();
                if (now - emerge >= 0)
                {
                    if (urgent.Length == 0) { urgent = "  <color=#FF5544>LAUNCH " + Clock(tel.GetLaunchMs() - now) + "</color>"; }
                }
                else if (emerge - now < nextEmerge - now || !hasNext) { nextEmerge = emerge; hasNext = true; }
            }
            string s = "TEL " + dead + "/" + (_telCount > 0 ? _telCount : TelCount);   // 덜 놓였으면 놓인 수
            if (urgent.Length > 0) { s += urgent; }
            else if (hasNext) { s += "  NEXT " + Clock(nextEmerge - now); }
            return s;
        }

        private string Clock(int ms)
        {
            int sec = Mathf.CeilToInt(Mathf.Max(ms * 0.001f, 0f));
            return (sec / 60) + ":" + (sec % 60).ToString("00");
        }

        public override string GetDescription()
        {
            if (Description.Length > 0) { return Description; }
            return "ENEMY MISSILE LAUNCHERS ARE HIDING IN THE TUNNEL - DESTROY THEM BEFORE THEY LAUNCH, OR INTERCEPT THE MISSILE";
        }
    }
}
