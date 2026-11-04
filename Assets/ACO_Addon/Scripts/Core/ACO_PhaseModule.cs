using UdonSharp;
using UnityEngine;

namespace ACO
{
    /// 페이즈 모듈(동기화 없음) — 새 페이즈 종류를 미션 관리자 밖에 따로 만드는 기반 클래스. 상속해서 아래 virtual 함수만 채운다.
    /// 관리자(ACO_MissionManager)는 Modules[] 순서대로 종류 번호 PHASE_MODULE_BASE + 순서를 주고, 무작위 선택(Weight·MinPhase)·점수 배율·
    /// 웨이브·보너스·제한 시간·미션 끝은 그대로 맡는다. 모듈은 "배치할 수 있나 → 배치 → 진행 판정 → 글자"만 한다.
    ///
    /// 계약:
    ///  - IsFeasible / Begin / Tick / End는 관리자 오너 화면에서만 불린다. 배치는 생성기 공개 API(Request*)로 한다(생성기가 동기화).
    ///  - 진행 값은 관리자의 동기화 값에 넣는다: Manager.SetModuleProgress(완료, 전체). 페이즈 작전 지역은 Manager.SetModuleSite(자리).
    ///  - Tick 반환: RESULT_RUNNING(계속) / RESULT_CLEAR(성공 — extraBonus는 ClearBonus) / RESULT_FAIL(미션 실패 — 실패 글자는 GetFailText) /
    ///    RESULT_REPLACE(배치가 안 됨 — 관리자가 같은 페이즈를 다른 종류로 바꾼다).
    ///  - 보너스 전용(BonusOnly): RESULT_FAIL = 목표가 물러남(보너스 없이 미션 클리어), RESULT_REPLACE = 건너뜀. 제한 시간(BonusTimeLimit)은 관리자가 본다.
    ///  - 글자(GetDescription / GetProgressText / GetLocation / GetFailText)와 무전 대사는 모든 클라이언트에서 불린다 —
    ///    동기화된 값(관리자 진행 값, 생성기 배치 상태)만 읽어야 한다.
    ///  - 오너가 바뀌면 오너 화면의 지역 상태(배치 확인 전 체력 0 등)는 처음부터 다시 본다 — 생성기 상태만으로 판정이 이어지게 만든다.
    /// Bake가 미션 관리자 아래(자식)의 모듈을 모아 Modules에 넣고 Manager/Generator를 연결한다.
    ///
    /// 동기화: 기반 클래스엔 동기화 방식을 붙이지 않는다 — 모듈마다 정한다. 생성기·관리자 상태만으로 판정하면 None(상륙전),
    /// 자기 상태를 들고 가야 하면 Manual + [UdonSynced](시한 표적 등). 자체 동기화 규칙(Tools/aco_sync_sim.py로 확인):
    ///  - 판정(Tick)은 관리자 오너만 한다. 모듈 오너가 내가 아니면 Tick에서 먼저 가져온다(Networking.SetOwner).
    ///  - 상태 전이는 '동기화 값 + 생성기 상태'만으로 정한다(같은 입력이면 같은 결과) — 오너가 바뀌어 마지막 전송이 유실돼도 새 오너가 같은 전이를 다시 한다.
    ///    목표마다 다른 태그를 쓰면 이미 센 목표를 다시 세지 않는다.
    ///  - 동기화 값에 페이즈 번호를 찍는다 — 보는 쪽은 관리자 페이즈 번호와 다르면(이전 페이즈 값) 무시한다.
    ///  - 시각은 생성기 배치 시각(동기화) 기준으로 계산한다(오너가 처음 본 시각이 아니라). 마감 판정엔 유예를 둔다(피해 이벤트 지연).
    public abstract class ACO_PhaseModule : UdonSharpBehaviour
    {
        public const int RESULT_RUNNING = 0;
        public const int RESULT_CLEAR = 1;
        public const int RESULT_FAIL = 2;
        public const int RESULT_REPLACE = 3;

        [Header("참조 (Bake가 넣는다)")]
        public ACO_MissionManager Manager;
        public ACO_MissionGenerator Generator;
        [Tooltip("거리 단위(HUD 설정 DISTANCE UNIT) — Bake가 넣는다. 진행 글자의 남은 거리에 쓴다")]
        public ACO_Units Units;

        [Header("페이즈 선택")]
        [Tooltip("켜면 무작위 페이즈 후보에 들어간다")]
        public bool Enabled = true;
        [Tooltip("무작위 선택 가중치(기본 페이즈 가중치와 같은 눈금, 1 = 보통)")]
        public float Weight = 1f;
        [Tooltip("이 페이즈 번호(1부터)부터 나온다")]
        public int MinPhase = 1;

        [Header("글자 (영문 — 기본 TMP 폰트)")]
        public string PhaseName = "MODULE";
        [Tooltip("페이즈 배너 안내")]
        public string Description = "";
        [Tooltip("이 페이즈 실패로 미션이 끝났을 때 배너·작전판 글자")]
        public string FailText = "MISSION FAILED";

        [Header("무전 — 이 페이즈의 대사는 여기서만 정한다(비우면 무전의 공통 기본 대사 Default*)")]
        [Tooltip("페이즈 시작 대사")]
        [TextArea] public string StartText = "";
        [Tooltip("페이즈 성공 대사")]
        [TextArea] public string SuccessText = "";
        [Tooltip("실패 대사 — 이 페이즈 때문에 미션이 실패했을 때(보너스 페이즈면 목표가 물러났을 때)")]
        [TextArea] public string FailChatterText = "";
        public AudioClip StartClip;
        public AudioClip SuccessClip;
        public AudioClip FailClip;
        [Tooltip("대사별 화자(무전 SpeakerNames 순서: 0 SKY EYE, 1 ENEMY, 2 TRANSPORT, 3 WINGMAN). -1이면 무전의 PhaseSpeaker. 이름 색·무전 채널(PTT)은 화자를 따른다")]
        public int StartSpeaker = -1;
        public int SuccessSpeaker = -1;
        public int FailSpeaker = -1;

        [Header("제한 시간")]
        [Tooltip("켜면 이 페이즈 목표 태그의 지켜야 하는 호위 대상(Role VIP)이 도착하는 데 걸리는 시간만큼 미션 제한 시간을 늘린다(호위 페이즈와 같다)")]
        public bool GrantTravelTime = false;

        [Header("보너스")]
        [Tooltip("성공 보너스(페이즈 클리어 보너스에 더한다, 페이즈 배율이 곱해진다 — 보너스 페이즈면 관리자 BonusScoreMultiplier)")]
        public int ClearBonus = 0;
        [Tooltip("켜면 보너스 페이즈 전용 — 일반 페이즈 무작위 선택에 나오지 않고, 본 미션 클리어 뒤 보너스(관리자 BonusPhaseChance)로만 나온다. "
            + "가중치(Weight)는 보너스 후보끼리 비교, MinPhase는 본 미션 페이즈 수 기준")]
        public bool BonusOnly = false;
        [Tooltip("보너스 페이즈 제한 시간(초) — 지나면 목표가 물러나고 보너스 없이 미션 클리어(관리자가 판정한다)")]
        public float BonusTimeLimit = 240f;

        // ───────────── 오너 (관리자가 부른다) ─────────────

        /// 지금 배치할 수 있는가(풀 여유, 지형, 항로). 찾은 자리·씨앗은 모듈이 들고 있다가 Begin에서 그대로 쓴다.
        public virtual bool IsFeasible(int phaseIndex, Vector3 focus) { return false; }

        /// 페이즈 시작: 목표를 배치한다. tag = 이번 시도의 목표 태그(생성기 배치에 넘긴다).
        public virtual void Begin(int phaseIndex, int tag, Vector3 focus, int now) { }

        /// 진행 판정(관리자 Tick마다). RESULT_* 를 돌려준다.
        public virtual int Tick(int now) { return RESULT_RUNNING; }

        /// 페이즈가 끝났다(성공·실패·교체). 모듈 지역 상태를 비운다.
        public virtual void End() { }

        /// 2페이즈부터 페이즈 시작 증원 웨이브를 보내는가(시한 표적처럼 목표가 차례로 나오는 페이즈는 false).
        public virtual bool WaveOnStart() { return true; }

        // ───────────── 자체 동기화 도우미 (Manual 모듈만) ─────────────

        /// 이번 페이즈 시도의 도장 — 동기화 값에 같이 넣고, 읽을 때 다르면(이전 페이즈 값) 무시한다. 페이즈 시작 서버 시각(관리자가 동기화).
        protected int CurrentStamp() { return Manager != null ? Manager.GetPhaseStartMs() : 0; }

        /// 오너가 아니면 가져오고 동기화를 보낸다(관리자 오너의 Tick에서만 부른다). 동기화 방식이 Manual인 모듈에서만 쓴다.
        protected void SyncNow()
        {
            if (!VRC.SDKBase.Networking.IsOwner(gameObject)) { VRC.SDKBase.Networking.SetOwner(VRC.SDKBase.Networking.LocalPlayer, gameObject); }
            RequestSerialization();
        }

        // ───────────── 모든 클라이언트 ─────────────

        /// 표적 마커가 지금 목표 태그를 목표로 표시하는가(점수 할당량처럼 아무거나 잡으면 되는 페이즈는 false).
        public virtual bool MarksObjectiveTag() { return true; }
        /// HUD에 지금 목표 태그의 호위 대상(Role VIP) 항로를 그리는가.
        public virtual bool ShowsEscortRoute() { return false; }

        public virtual string GetName() { return PhaseName; }
        public virtual string GetDescription() { return Description; }
        /// HUD 페이즈 줄 뒤에 붙는 진행 글자(예: "LST 2/2  3.4km"). 비우면 관리자 진행 값(완료/전체).
        public virtual string GetProgressText() { return ""; }
        /// 진행 글자용 거리(거리 단위 설정을 따른다 — m/km 또는 NM).
        protected string Dist(float meters) { return Units != null ? Units.Distance(meters) : (meters * 0.001f).ToString("0.0") + "km"; }
        /// "AO: 이름" / "DEST: 이름" 등. 비우면 관리자가 작전 지역 이름으로 만든다.
        public virtual string GetLocation() { return ""; }
        public virtual string GetFailText() { return FailText; }
        /// 성공 보너스(관리자가 페이즈 배율을 곱한다). 기본 = ClearBonus.
        public virtual int GetClearBonus() { return ClearBonus; }

        /// 무전 대사: which 0 = 시작, 1 = 성공, 2 = 실패. 비면 "".
        public string GetChatterText(int which)
        {
            if (which == 0) { return StartText; }
            if (which == 1) { return SuccessText; }
            return FailChatterText;
        }

        public AudioClip GetChatterClip(int which)
        {
            if (which == 0) { return StartClip; }
            if (which == 1) { return SuccessClip; }
            return FailClip;
        }

        /// 무전 대사의 화자(-1 = 무전 PhaseSpeaker).
        public int GetChatterSpeaker(int which)
        {
            if (which == 0) { return StartSpeaker; }
            if (which == 1) { return SuccessSpeaker; }
            return FailSpeaker;
        }
    }
}
