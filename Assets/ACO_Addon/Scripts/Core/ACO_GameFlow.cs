using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
using SaccFlightAndVehicles;

namespace ACO
{
    /// 게임 진행(씬에 하나, 수동 동기화). 참가 명단 → 출격 준비 → 미션 시작과 동시에 일제 출격 → 격추되면 격납고에서 다시 출격.
    ///
    /// - 참가 명단: 칸 = 기체(Aircraft[k]). 로비 콘솔의 JOIN으로 빈 칸에 들어가고(비어 있는 기체의 칸 먼저) LEAVE로 나온다(비행 중엔 못 나간다).
    ///   명단 밖은 관전자(기체에 못 탄다). 미션 도중에도 빈 칸이 있으면 들어올 수 있다(난입). 미션이 끝나면 명단·준비를 비운다. 나간 플레이어는 지운다.
    /// - 브리핑 준비: 지형이 다 세워지고 그 위에 미리 배치가 끝나야(생성기 IsPreviewReady + 기다리던 씨앗) READY를 받는다. 그동안 READY·리롤 버튼은
    ///   PREPARING. 미리 배치가 없으면(미션 뒤 새 지형을 끈 경우, 생성기 오너가 배치 도중 나간 경우) 오너가 다시 요청한다.
    ///   월드를 연 뒤 RerollCooldown(30초) 동안은 리롤을 받지 않는다(첫 지형·배치와 겹치지 않게).
    /// - 준비: 미션 전, 무장·강화를 마치고 READY. 명단 전원이 READY면 AllReadyDelay(10초) 출격 카운트다운 — 그동안 리롤·페이즈 변경, READY 해제,
    ///   새 참가(JOIN), 무장 변경·해금 구매(격납고), 강화 구매(상점)를 잠그고, 패널에 큰 카운트다운을 덮어 띄우며, 시작 순간 AllReadySound를 튼다.
    ///   누가 READY를 풀거나 새로 들어오면(전원 준비가 깨지면) 취소된다. 절반 이상이면 ReadyCountdown(60초) 뒤 READY 안 한 사람을 명단에서 빼고(관전자)
    ///   남은 전원으로 같은 출격 카운트다운에 들어간다. 참가자 기체가 하나라도 떠 있으면(미션 직후) READY·리롤을 받지 않는다.
    /// - 시작: 오너가 미션을 요청하고 모든 클라이언트에 일제 출격을 알린다. 미션 관리자가 RUNNING이 될 때까지(최대 StartTimeout) "시작 중"으로
    ///   잠가 READY·리롤·페이즈 변경을 받지 않는다(중복 시작 방지).
    /// - 출격(LaunchLocal, 내 화면에서만): 내 칸 기체의 오너가 되어 항모 갑판의 내 칸 위치(DeckPoints[k])로 옮기고(SAV 순간이동 규약:
    ///   ShouldTeleport) 바로 조종석에 앉힌다(SaccVehicleSeat.Interact — 직접 누른 것과 같다).
    /// - 격추·탈출: 미션 중 내 기체가 터졌거나 안전지대(진행도 SafeZones) 밖에서 내렸으면 ShotDownReturnDelay 뒤 HangarPoint(비면 BriefingPoint —
    ///   대기실 하나로 쓸 때)로 옮긴다(바다에 남지 않게). 격추된 기체는 SAV가 원래 스폰 위치(= 안 보이는 보관 장소)로 되돌리고, LAUNCH로 다시 탄다.
    /// - 보관: 내려서 비어 있는 기체가 보관 장소 밖에 IdleReturnTime(30초) 있으면 그 기체의 오너가 SAV 전체 리스폰(SendRespawn)으로
    ///   되돌린다 — 탄약·연료·체력도 다시 채워진다.
    /// - 참가자 기체의 파일럿 좌석은 시작할 때 DisableInteractive — 눌러서는 못 타고 LAUNCH로만 탄다(관전자 탑승 차단).
    ///   생성기·미션 관리자를 눌러 미션을 시작하는 것도 끈다(명단을 거치지 않는 시작 방지).
    /// - 지형: 브리핑(미션 전)에 명단에 있는 사람이 REROLL로 지형을 새로 만든다(RerollCooldown 30초, 리롤하면 READY·카운트다운이 풀린다).
    ///   새 지형마다 생성기가 병력을 미리 배치하고(작전 테이블에 보인다), 미션은 그 지형·배치 그대로 시작한다 — 시작할 때 생성기의
    ///   RegenerateTerrainEachMission을 끈다. 미션이 끝나면 참가자 기체가 모두 내린 뒤 새 지형 + 미리 배치(NewTerrainAfterMission).
    ///   월드를 연 오너가 첫 지형에도 미리 배치한다. 쿨타임·예약은 동기화해 오너가 바뀌어도 이어진다.
    /// - 관전: 미션 중 명단 밖 사람의 격납고 버튼은 LAUNCH 대신 SPECTATE — 누르면 갑판(SpectatePoint)으로 간다.
    /// - 귀환: 미션이 끝나면(성공/실패) ReturnDelay 뒤 모든 플레이어를 브리핑실(BriefingPoint)로 옮긴다(참가자 기체에 타고 있으면 내린 뒤).
    /// 이동(대기실·필드)은 자유 — ACO_Teleporter 버튼으로 오간다. 순간이동·출격은 화면 암전(ACO_ScreenFader)을 거친다.
    /// 브리핑실과 격납고는 한 방(대기실)이어도 된다: 콘솔(명단·리롤·페이즈)·격납고 패널(무장·상점·READY/LAUNCH)·작전 테이블을 한곳에 두고 HangarPoint를 비운다.
    /// - 작전 콘솔(ACO_MissionConsole): 위 = 상태 배너(큰 상태 + "내 다음 할 일" + 거절 안내 6초), 왼쪽 = 작전 카드(공용 설정 — 페이즈 −/+,
    ///   예상 시간, 지형 이름, NEW TERRAIN, 마지막으로 바꾼 사람), 오른쪽 = 명단 칸 카드(기체 이름·조종사·준비 — 빈 칸을 누르면 그 칸으로 참가,
    ///   미션 전엔 칸 바꾸기), 아래 = 내 행동 버튼 하나(JOIN → READY/CANCEL READY → LAUNCH/SPECTATE) + LEAVE.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ACO_GameFlow : UdonSharpBehaviour
    {
        public const int ACTION_TOGGLE_JOIN = 0;
        public const int ACTION_TOGGLE_READY = 1;
        public const int ACTION_LAUNCH = 2;
        public const int ACTION_REROLL = 3;
        public const int ACTION_PHASES_DOWN = 4;
        public const int ACTION_PHASES_UP = 5;
        /// 명단 칸 카드(ACO_FlowButton.Slot): 빈 칸이면 그 칸으로 참가(이미 명단이면 칸 바꾸기).
        public const int ACTION_JOIN_SLOT = 6;
        /// 내 행동 버튼: 명단 밖 = JOIN(미션 중 빈 칸이 없으면 SPECTATE), 미션 전 = READY/CANCEL READY, 미션 중 = LAUNCH.
        public const int ACTION_PRIMARY = 7;
        public const int ACTION_LEAVE = 8;
        private const int MAX_SLOTS = 8;
        private const float MESSAGE_TIME = 6f;

        [Header("참조")]
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 미션 관리자를 넣는다")]
        public ACO_MissionManager Manager;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 진행도를 넣는다(안전지대 판정)")]
        public ACO_PlayerProgress Progress;
        [Tooltip("참가자 기체(칸 번호 = 기체 번호, 최대 8). 기체의 원래 위치(스폰 위치)는 안 보이는 보관 장소에 둔다. 비우면 Bake가 강화 확장이 있는 기체로 채운다")]
        public SaccEntity[] Aircraft;
        [Tooltip("칸별 항모 갑판 출격 위치. 기체 루트가 이 위치·방향에 놓인다 — 갑판에 세워 둔 기체의 루트 위치를 그대로 옮겨 두면 된다")]
        public Transform[] DeckPoints;
        [Tooltip("미션이 끝나면 모두가 돌아올 브리핑실(대기실) 위치")]
        public Transform BriefingPoint;
        [Tooltip("격추·탈출한 참가자가 돌아올 위치. 비우면 BriefingPoint(브리핑실과 격납고를 한 방으로 쓸 때)")]
        public Transform HangarPoint;
        [Tooltip("관전자가 SPECTATE로 갈 갑판 위치(출격 위치와 겹치지 않게)")]
        public Transform SpectatePoint;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 화면 암전을 넣는다. 비면 바로 옮긴다")]
        public ACO_ScreenFader Fader;
        [Tooltip("빌드 시 ACO_SceneBaker가 씬의 날씨를 넣는다(작전 카드·명단 글자에 날씨 이름). 비면 표시 안 함")]
        public ACO_Weather Weather;

        [Header("진행")]
        [Tooltip("로비 콘솔에서 고르기 전의 페이즈 수")]
        [Range(1, 5)] public int DefaultPhases = 3;
        [Tooltip("페이즈당 대략 걸리는 시간(분) — 작전 카드에 'ABOUT N MIN'으로 보인다")]
        public float MinutesPerPhase = 5f;
        [Tooltip("명단 칸(기체) 표시 이름(칸 순서). 비면 기체 오브젝트 이름")]
        public string[] SlotNames;
        [Tooltip("명단의 절반 이상이 READY면 이 시간(초) 뒤 READY 안 한 사람을 명단에서 빼고 출격 카운트다운에 들어간다")]
        public float ReadyCountdown = 60f;
        [Tooltip("명단 전원이 READY면 이 시간(초) 뒤 출격한다. 그동안 리롤·페이즈 변경을 잠근다(READY를 풀거나 새로 들어오면 취소)")]
        public float AllReadyDelay = 10f;
        [Tooltip("시작을 요청하고 미션이 RUNNING이 되기를 기다리는 최대 시간(초). 그동안 READY·리롤을 잠근다")]
        public float StartTimeout = 45f;
        [Tooltip("미션 중 격추·탈출한 뒤 격납고로 옮기기까지의 시간(초)")]
        public float ShotDownReturnDelay = 5f;
        [Tooltip("내린 기체가 보관 장소 밖에 이 시간(초) 비어 있으면 보관 장소로 되돌린다")]
        public float IdleReturnTime = 30f;
        [Tooltip("보관 장소(원래 위치)에서 이 거리(m) 안이면 보관 중으로 본다")]
        public float HomeTolerance = 50f;
        [Tooltip("미션이 끝나고 브리핑실로 옮기기까지의 시간(초) — 결과 안내를 볼 시간")]
        public float ReturnDelay = 8f;

        [Header("지형 (생성기의 육각 지형)")]
        [Tooltip("지형 리롤 사이 최소 간격(초) — 리롤마다 지형을 세우고 병력을 다시 미리 배치하므로 넉넉히")]
        public float RerollCooldown = 30f;
        [Tooltip("미션이 끝나고 참가자 기체가 모두 내리면 다음 브리핑을 위해 지형을 새로 만든다")]
        public bool NewTerrainAfterMission = true;
        [Tooltip("지형 프리셋 표시 이름(ACO_HexTerrain.PRESET_* 순서)")]
        public string[] TerrainNames = new string[] {
            "BIG ISLAND", "ARCHIPELAGO", "TWIN ISLANDS", "ATOLL", "ISLAND CHAIN", "PLATEAU", "MOUNTAIN RANGE", "CANYON",
            "COASTLINE", "VOLCANO", "REEF ISLANDS", "BASIN" };

        [Header("표시 (프리팹 빌더가 채운다)")]
        [Tooltip("명단·준비·상태(격납고, 옛 콘솔)")]
        public TextMeshProUGUI[] StatusTexts;
        [Tooltip("작전 콘솔 상태 배너: 큰 상태 + 내 다음 할 일 + 거절 안내")]
        public TextMeshProUGUI[] BannerTexts;
        [Tooltip("작전 카드: 페이즈 수(큰 숫자)")]
        public TextMeshProUGUI[] PhaseTexts;
        [Tooltip("작전 카드: 예상 시간·바꾼 사람")]
        public TextMeshProUGUI[] PhaseInfoTexts;
        [Tooltip("작전 카드: 지형 이름·바꾼 사람")]
        public TextMeshProUGUI[] TerrainTexts;

        [Header("명단 칸 카드 색")]
        public Color SlotOpenColor = new Color(0.05f, 0.1f, 0.08f, 1f);
        public Color SlotTakenColor = new Color(0.08f, 0.2f, 0.12f, 1f);
        public Color SlotMineColor = new Color(0.32f, 0.26f, 0.05f, 1f);
        public Color SlotReadyColor = new Color(0.1f, 0.42f, 0.18f, 1f);
        public Color SlotAirborneColor = new Color(0.07f, 0.24f, 0.4f, 1f);
        [Tooltip("JOIN/READY/LAUNCH/REROLL 버튼(빌드 시 Bake가 씬에서 모은다). 상태에 맞게 글자를 바꾸고, 지금 누를 수 없으면 회색으로 칠한다")]
        public ACO_FlowButton[] Buttons;
        [Tooltip("페이즈 수 버튼(빌드 시 Bake가 모은다). 고른 수는 밝게, 바꿀 수 없을 때(미션 중·출격 카운트다운)는 회색")]
        public ACO_MissionStartButton[] PhaseButtons;
        [Tooltip("출격 카운트다운 때 패널 위에 덮어 띄우는 판(빌드 시 Bake가 콘솔·격납고 패널마다 만들고 모은다)")]
        public GameObject[] CountdownOverlays;
        [Tooltip("카운트다운 판의 큰 글자(CountdownOverlays와 같은 순서)")]
        public TextMeshProUGUI[] CountdownTexts;

        [Header("소리 (선택)")]
        [Tooltip("모두 READY해 출격 카운트다운이 시작되는 순간 한 번(모든 클라이언트 각자). 2D 권장")]
        public AudioSource AllReadySound;
        [Tooltip("출격 카운트다운 마지막 TickSeconds초 동안 초마다(선택)")]
        public AudioSource CountdownTickSound;
        public int TickSeconds = 5;

        [UdonSynced] private int[] _roster = new int[0];   // 칸별 playerId, -1 = 빈 칸
        [UdonSynced] private int _readyMask;
        [UdonSynced] private int _phases = 3;
        [UdonSynced] private int _startingMs;      // 시작을 요청한 서버 시각(0 = 시작 중 아님)
        [UdonSynced] private int _countdownEndMs;  // 준비 카운트다운이 끝나는 서버 시각(0 = 없음)
        [UdonSynced] private int _launchEndMs;     // 전원 준비 → 출격 카운트다운이 끝나는 서버 시각(0 = 없음). 그동안 리롤·페이즈 잠금
        [UdonSynced] private int _lastRerollMs;    // 마지막 리롤 서버 시각(0 = 없음)
        [UdonSynced] private bool _terrainPending; // 미션이 끝났고, 모두 내리면 지형을 새로 만든다
        [UdonSynced] private int _expectedSeed;    // 마지막으로 요청한 새 지형 씨앗(0 = 지금 지형) — 미리 배치가 이 지형에 놓여야 준비 완료
        [UdonSynced] private int _phasesBy = -1;   // 페이즈를 마지막으로 바꾼 playerId(-1 = 기본값)
        [UdonSynced] private int _terrainBy = -1;  // 지형을 마지막으로 바꾼 playerId(-1 = 자동)

        private bool _wasRunning;
        private float _pollTimer;
        private ACO_HexTerrain _terrain;
        private float _returnAt = -1f;          // 미션이 끝나 브리핑실로 옮길 시각(로컬)
        private float _shotDownReturnAt = -1f;  // 격추·탈출 뒤 격납고로 옮길 시각(로컬)
        private int _pilotedSlot = -1;          // 지난 확인 때 내가 조종하던 참가자 기체(로컬)
        private float _previewRequestAt = -1000f; // 오너가 마지막으로 미리 배치를 요청한 시각(로컬, 다시 요청 간격)
        private string _message = "";
        private float _messageTime;
        private int _seenLaunchEndMs;      // 출격 카운트다운 시작 감지(로컬) — 늦게 들어온 사람은 소리 없이
        private bool _launchSeenInit;
        private int _lastCountdownSec = -1;

        // 기체별(로컬)
        private Vector3[] _homePos;
        private float[] _idleTime;

        // 출격 진행
        private int _launchSlot = -1;
        private int _launchTries;

        void Start()
        {
            if (Aircraft == null) { Aircraft = new SaccEntity[0]; }
            int n = SlotCount();
            _homePos = new Vector3[n];
            _idleTime = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!Aircraft[i]) { continue; }
                _homePos[i] = Aircraft[i].transform.position;
                // 파일럿 좌석은 눌러서 탈 수 없게 한다 — LAUNCH(좌석의 Interact를 직접 부른다)로만 탄다. 관전자 탑승 차단.
                // 좌석은 SAV가 모은 목록(VehicleSeats)을 쓴다 — GetComponentsInChildren<U# 타입>은 자식에 프로그램이 빠진 UdonBehaviour가 있으면
                // 예외로 이 스크립트 전체가 멈춘다(기체 모델에 딸려 온 빈 UdonBehaviour 등). Init은 여러 번 불러도 한 번만 초기화한다.
                Aircraft[i].Init();
                SaccVehicleSeat[] seats = Aircraft[i].VehicleSeats;
                if (seats == null) { continue; }
                for (int k = 0; k < seats.Length; k++)
                {
                    if (seats[k] && seats[k].IsPilotSeat) { seats[k].DisableInteractive = true; }
                }
            }
            if (Manager)
            {
                // 명단을 거치지 않는 시작(관리자·생성기를 눌러 시작)을 끈다.
                Manager.AllowInteract = false;
                Manager.DisableInteractive = true;
                if (Manager.Generator)
                {
                    Manager.Generator.AllowInteract = false;
                    Manager.Generator.DisableInteractive = true;
                    // 지형은 브리핑 리롤과 미션 뒤 갱신으로만 바꾼다 — 미션 시작 때 생성기가 다시 만들면 리롤한 지형이 사라진다.
                    if (Manager.Generator.HexTerrain)
                    {
                        _terrain = Manager.Generator.HexTerrain;
                        Manager.Generator.RegenerateTerrainEachMission = false;
                    }
                }
            }
            EnsureRoster();
            if (Networking.IsOwner(gameObject)) { _phases = Mathf.Clamp(DefaultPhases, 1, 5); }
            _wasRunning = IsRunning();
            // 월드를 연 사람(오너)이 첫 지형에 병력을 미리 배치한다(작전 테이블 브리핑용). 지형이 먼저 세워지도록 조금 뒤에.
            // 그동안과 그 뒤 RerollCooldown 동안은 리롤을 받지 않는다(리롤 쿨타임을 지금부터 센다).
            if (Networking.IsOwner(gameObject) && !_wasRunning)
            {
                _lastRerollMs = Now();
                if (_lastRerollMs == 0) { _lastRerollMs = 1; }
                RequestSerialization();
                _previewRequestAt = Time.time;
                SendCustomEventDelayedSeconds(nameof(_InitialPreview), 3f);
            }
            Refresh();
        }

        private int SlotCount() { return Mathf.Min(Aircraft != null ? Aircraft.Length : 0, MAX_SLOTS); }

        private void EnsureRoster()
        {
            int n = SlotCount();
            if (_roster != null && _roster.Length == n) { return; }
            int[] old = _roster;
            _roster = new int[n];
            for (int i = 0; i < n; i++) { _roster[i] = old != null && i < old.Length ? old[i] : -1; }
        }

        private int Now() { return Networking.GetServerTimeInMilliseconds(); }

        private bool IsRunning() { return Manager != null && Manager.GetState() == ACO_MissionManager.STATE_RUNNING; }

        /// 시작을 요청했고 아직 RUNNING이 되지 않았다(StartTimeout 안).
        private bool IsStarting() { return _startingMs != 0 && Now() - _startingMs < Mathf.RoundToInt(StartTimeout * 1000f); }

        /// 미션 전 조작(READY·리롤·페이즈)을 받지 않는 상태.
        private bool IsBusy() { return IsRunning() || IsStarting(); }

        /// 전원 준비 출격 카운트다운 중(리롤·페이즈 변경 잠금).
        private bool IsLaunchCountdown() { return _launchEndMs != 0; }

        /// 격납고(무장 변경·해금 구매)와 상점(강화 구매)이 본다: 출격 카운트다운·시작 중엔 잠근다.
        public bool IsLoadoutLocked() { return IsLaunchCountdown() || IsStarting(); }

        /// 브리핑 준비 완료: 지형이 다 세워지고 그 위에 미리 배치가 끝났다(그리고 마지막으로 요청한 새 지형이다 — 리롤 직후 동기화가 늦게 와도
        /// 옛 배치를 준비 완료로 보지 않게). 생성기가 없으면 늘 준비 완료.
        private bool IsBriefingReady()
        {
            if (!Manager || !Manager.Generator) { return true; }
            ACO_MissionGenerator gen = Manager.Generator;
            if (!gen.IsPreviewReady()) { return false; }
            if (_expectedSeed != 0 && gen.GetPreviewSeed() != _expectedSeed) { return false; }
            return !_terrain || _terrain.GetBuiltPreset() >= 0;
        }

        private bool AnyAircraftOccupied()
        {
            for (int i = 0; i < SlotCount(); i++) { if (Aircraft[i] && Aircraft[i].Occupied) { return true; } }
            return false;
        }

        // ───────────── 조회 ─────────────

        public int GetSlotOf(int playerId)
        {
            if (_roster == null) { return -1; }
            for (int i = 0; i < _roster.Length; i++) { if (_roster[i] == playerId) { return i; } }
            return -1;
        }

        public int LocalSlot()
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            return Utilities.IsValid(me) ? GetSlotOf(me.playerId) : -1;
        }

        public bool IsLocalParticipant() { return LocalSlot() >= 0; }

        public int GetSelectedPhases() { return _phases; }

        // ───────────── 버튼 (ACO_FlowButton, ACO_MissionStartButton) ─────────────

        /// 버튼이 부른다. 받아들였으면 true(버튼이 결정음), 조건이 안 맞아 안내만 띄웠으면 false(거절음).
        public bool ButtonAction(int action) { return ButtonActionSlot(action, -1); }

        /// slot = 명단 칸 카드의 칸 번호(ACTION_JOIN_SLOT), 다른 동작은 -1.
        public bool ButtonActionSlot(int action, int slot)
        {
            // 내 행동·LEAVE는 해당 동작으로 바꿔 아래에서 처리한다(U#은 재귀 호출이 안전하지 않다)
            if (action == ACTION_PRIMARY)
            {
                if (LocalSlot() < 0) { action = IsRunning() && !HasOpenSlot() ? ACTION_LAUNCH : ACTION_TOGGLE_JOIN; }   // 빈 칸 없는 미션 중 = 관전
                else { action = IsBusy() ? ACTION_LAUNCH : ACTION_TOGGLE_READY; }
            }
            else if (action == ACTION_LEAVE)
            {
                if (LocalSlot() < 0) { return Deny("YOU ARE NOT ON THE ROSTER"); }
                action = ACTION_TOGGLE_JOIN;
            }
            if (action == ACTION_PHASES_DOWN || action == ACTION_PHASES_UP)
            {
                int want = _phases + (action == ACTION_PHASES_UP ? 1 : -1);
                if (want < 1) { return Deny("MINIMUM 1 PHASE"); }
                if (want > 5) { return Deny("MAXIMUM 5 PHASES"); }
                return RequestPhases(want);
            }
            if (action == ACTION_JOIN_SLOT)
            {
                if (slot < 0 || slot >= SlotCount()) { return false; }
                EnsureRoster();
                int mine = LocalSlot();
                if (mine == slot) { return Deny("THIS IS YOUR SLOT - PRESS LEAVE TO EXIT THE ROSTER"); }
                if (_roster[slot] >= 0) { return Deny("SLOT TAKEN - PICK AN OPEN SLOT"); }
                if (IsLaunchCountdown()) { return Deny("ALL READY - LAUNCH COUNTDOWN, ROSTER LOCKED"); }
                if (Aircraft[slot] && Aircraft[slot].Occupied) { return Deny("THAT AIRCRAFT IS STILL IN USE"); }
                if (mine >= 0)
                {
                    if (IsBusy()) { return Deny("MISSION IN PROGRESS - SLOTS LOCKED"); }
                    if (Aircraft[mine] && Aircraft[mine].Occupied) { return Deny("LAND AND EXIT BEFORE CHANGING SLOTS"); }
                }
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetJoinSlot), slot);
                return true;
            }
            if (action == ACTION_TOGGLE_JOIN)
            {
                int k = LocalSlot();
                if (k >= 0 && k < SlotCount() && Aircraft[k] && Aircraft[k].Piloting) { return Deny("LAND AND EXIT BEFORE LEAVING THE ROSTER"); }
                if (k < 0 && IsLaunchCountdown()) { return Deny("ALL READY - LAUNCH COUNTDOWN, ROSTER LOCKED"); }
                if (k < 0 && !HasOpenSlot()) { return Deny("ROSTER FULL - YOU CAN SPECTATE"); }
                SendCustomNetworkEvent(NetworkEventTarget.Owner, k >= 0 ? nameof(NetLeave) : nameof(NetJoin));
                return true;
            }
            if (action == ACTION_TOGGLE_READY)
            {
                if (!IsLocalParticipant()) { return Deny("JOIN THE ROSTER FIRST"); }
                if (IsStarting()) { return Deny("MISSION STARTING..."); }
                if (IsRunning()) { return Deny("MISSION IN PROGRESS - USE LAUNCH"); }
                if (AnyAircraftOccupied()) { return Deny("WAIT UNTIL ALL AIRCRAFT HAVE LANDED"); }
                bool readyNow = (_readyMask & (1 << LocalSlot())) != 0;
                if (readyNow && IsLaunchCountdown()) { return Deny("ALL READY - LAUNCH COUNTDOWN, READY LOCKED"); }
                if (!readyNow && !IsBriefingReady()) { return Deny("PREPARING TERRAIN AND ENEMY FORCES - PLEASE WAIT"); }
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetToggleReady));
                return true;
            }
            if (action == ACTION_REROLL)
            {
                if (!IsLocalParticipant()) { return Deny("JOIN THE ROSTER FIRST"); }
                if (IsBusy()) { return Deny("MISSION IN PROGRESS"); }
                if (IsLaunchCountdown()) { return Deny("ALL READY - TERRAIN LOCKED (CANCEL READY TO CHANGE)"); }
                if (AnyAircraftOccupied()) { return Deny("WAIT UNTIL ALL AIRCRAFT HAVE LANDED"); }
                if (!_terrain) { return false; }
                if (!IsBriefingReady()) { return Deny("TERRAIN GENERATING - PLEASE WAIT"); }
                int left = RerollCooldownLeft();
                if (left > 0) { return Deny("NEW TERRAIN AVAILABLE IN " + left + "s"); }
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetReroll));
                return true;
            }
            if (action == ACTION_LAUNCH)
            {
                // 미션 중 명단 밖이면 관전(갑판으로)
                if (!IsLocalParticipant())
                {
                    if (IsRunning() && SpectatePoint) { TeleportLocal(SpectatePoint); return true; }
                    return Deny("JOIN THE ROSTER FIRST");
                }
                if (IsStarting()) { return Deny("MISSION STARTING..."); }
                if (!IsRunning()) { return Deny("MISSION NOT STARTED - PRESS READY"); }
                return LaunchLocal();
            }
            return false;
        }

        /// 페이즈 버튼이 부른다. 받아들였으면 true.
        public bool RequestPhases(int phases)
        {
            if (IsBusy()) { return Deny("MISSION IN PROGRESS"); }
            if (IsLaunchCountdown()) { return Deny("ALL READY - PHASES LOCKED (CANCEL READY TO CHANGE)"); }
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetSetPhases), Mathf.Clamp(phases, 1, 5));
            return true;
        }

        private bool Deny(string message)
        {
            SetMessage(message);
            return false;
        }

        private bool HasOpenSlot()
        {
            if (_roster == null) { return SlotCount() > 0; }
            for (int i = 0; i < _roster.Length; i++) { if (_roster[i] < 0) { return true; } }
            return false;
        }

        private int RerollCooldownLeft()
        {
            if (_lastRerollMs == 0) { return 0; }
            int left = Mathf.RoundToInt(RerollCooldown * 1000f) - (Now() - _lastRerollMs);
            return left > 0 ? Mathf.CeilToInt(left / 1000f) : 0;
        }

        // ───────────── 오너 ─────────────

        /// 빈 칸 중 기체가 비어 있는 칸을 먼저 준다(명단에서 빠진 사람이 아직 그 기체를 타고 있을 수 있다).
        [NetworkCallable]
        public void NetJoin()
        {
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p)) { return; }
            EnsureRoster();
            if (GetSlotOf(p.playerId) >= 0 || _launchEndMs != 0) { return; }   // 출격 카운트다운 중엔 새로 못 들어온다
            int slot = -1;
            for (int i = 0; i < _roster.Length; i++)
            {
                if (_roster[i] >= 0) { continue; }
                if (Aircraft[i] && Aircraft[i].Occupied) { if (slot < 0) { slot = i; } continue; }
                slot = i;
                break;
            }
            if (slot < 0) { return; }
            _roster[slot] = p.playerId;
            Publish();
            EvaluateReady();
        }

        /// 그 칸으로 참가한다. 이미 명단이면 칸을 바꾼다(미션 전, 기체에서 내린 뒤 — 준비는 풀린다).
        [NetworkCallable]
        public void NetJoinSlot(int slot)
        {
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p)) { return; }
            EnsureRoster();
            if (slot < 0 || slot >= _roster.Length || _roster[slot] >= 0 || _launchEndMs != 0) { return; }
            if (Aircraft[slot] && Aircraft[slot].Occupied) { return; }
            int old = GetSlotOf(p.playerId);
            if (old >= 0)
            {
                if (IsBusy() || (Aircraft[old] && Aircraft[old].Occupied)) { return; }
                _roster[old] = -1;
                _readyMask &= ~(1 << old);
            }
            _roster[slot] = p.playerId;
            Publish();
            EvaluateReady();
        }

        [NetworkCallable]
        public void NetLeave()
        {
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (Utilities.IsValid(p)) { RemovePlayer(p.playerId); }
        }

        [NetworkCallable]
        public void NetToggleReady()
        {
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p) || IsBusy() || AnyAircraftOccupied()) { return; }
            int k = GetSlotOf(p.playerId);
            if (k < 0) { return; }
            bool turningOn = (_readyMask & (1 << k)) == 0;
            if (turningOn && !IsBriefingReady()) { return; }   // 준비는 브리핑 준비가 끝난 뒤에만
            if (!turningOn && _launchEndMs != 0) { return; }    // 출격 카운트다운 중엔 해제하지 못한다
            _readyMask ^= 1 << k;
            Publish();
            EvaluateReady();
        }

        /// 명단에 있는 사람만, 미션 전에만, 참가자 기체가 모두 내린 뒤, RerollCooldown 간격으로. 리롤하면 준비·카운트다운이 풀린다.
        [NetworkCallable]
        public void NetReroll()
        {
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p) || !_terrain || IsBusy() || IsLaunchCountdown() || AnyAircraftOccupied() || GetSlotOf(p.playerId) < 0) { return; }
            if (RerollCooldownLeft() > 0 || !IsBriefingReady()) { return; }
            _lastRerollMs = Now();
            _terrainPending = false;
            _readyMask = 0;
            _countdownEndMs = 0;
            _terrainBy = p.playerId;
            Publish();
            NewTerrainAndPreview();
        }

        [NetworkCallable]
        public void NetSetPhases(int phases)
        {
            if (IsBusy() || IsLaunchCountdown()) { return; }
            _phases = Mathf.Clamp(phases, 1, 5);
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            _phasesBy = Utilities.IsValid(p) ? p.playerId : -1;
            Publish();
        }

        private void RemovePlayer(int playerId)
        {
            int k = GetSlotOf(playerId);
            if (k < 0) { return; }
            _roster[k] = -1;
            _readyMask &= ~(1 << k);
            Publish();
            EvaluateReady();   // 준비 안 한 사람이 나가면 나머지 전원이 준비 상태일 수 있다
        }

        /// 명단 전원이 READY면 출격 카운트다운(AllReadyDelay), 절반 이상이면 준비 카운트다운을 켠다(밑돌면 끈다).
        /// 전원 준비가 깨지면(READY 해제·새 참가자) 출격 카운트다운을 취소한다.
        private void EvaluateReady()
        {
            if (!Networking.IsOwner(gameObject) || IsBusy() || Manager == null) { return; }
            int members = 0;
            int ready = 0;
            for (int i = 0; i < _roster.Length; i++)
            {
                if (_roster[i] < 0) { continue; }
                members++;
                if ((_readyMask & (1 << i)) != 0) { ready++; }
            }
            // 전원 준비여도 브리핑 준비(지형·미리 배치)가 끝나야 카운트다운을 시작한다 — 끝나면 OwnerTick이 다시 부른다.
            if (members > 0 && ready == members)
            {
                if (_launchEndMs == 0 && IsBriefingReady())
                {
                    _launchEndMs = Now() + Mathf.RoundToInt(AllReadyDelay * 1000f);
                    if (_launchEndMs == 0) { _launchEndMs = 1; }
                    _countdownEndMs = 0;
                    Publish();
                }
                return;
            }
            if (_launchEndMs != 0)
            {
                _launchEndMs = 0;
                Publish();
            }

            bool countdown = members > 0 && ready > 0 && ready * 2 >= members;
            if (countdown && _countdownEndMs == 0)
            {
                _countdownEndMs = Now() + Mathf.RoundToInt(ReadyCountdown * 1000f);
                Publish();
            }
            else if (!countdown && _countdownEndMs != 0)
            {
                _countdownEndMs = 0;
                Publish();
            }
        }

        /// 준비 카운트다운이 끝났다: READY 안 한 사람을 명단에서 빼고(관전자) 남은 전원으로 출격 카운트다운에 들어간다.
        private void FinishCountdown()
        {
            _countdownEndMs = 0;
            for (int i = 0; i < _roster.Length; i++)
            {
                if (_roster[i] >= 0 && (_readyMask & (1 << i)) == 0) { _roster[i] = -1; }
            }
            Publish();
            EvaluateReady();
        }

        /// 미션을 요청하고 일제 출격을 알린다. RUNNING이 될 때까지 "시작 중"으로 잠근다.
        private void StartMission()
        {
            _readyMask = 0;
            _countdownEndMs = 0;
            _launchEndMs = 0;
            _terrainPending = false;
            _startingMs = Now();
            if (_startingMs == 0) { _startingMs = 1; }
            Publish();
            Manager.RequestStartMissionPhases(_phases);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetLaunchAll));
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Networking.IsOwner(gameObject) && player != null) { RemovePlayer(player.playerId); }
        }

        private void Publish()
        {
            RequestSerialization();
            Refresh();
        }

        public override void OnDeserialization()
        {
            Refresh();
        }

        // ───────────── 출격 (내 화면) ─────────────

        [NetworkCallable]
        public void NetLaunchAll()
        {
            if (IsLocalParticipant()) { LaunchLocal(); }
        }

        /// 내 칸 기체를 갑판으로 옮겨 탄다: 오너 가져오기 → (SAV가 오너를 알아챈 뒤) 순간이동 → 조종석.
        private bool LaunchLocal()
        {
            int k = LocalSlot();
            if (k < 0 || k >= SlotCount()) { return false; }
            SaccEntity e = Aircraft[k];
            if (!e) { return false; }
            if (e.Piloting) { return Deny("ALREADY IN FLIGHT"); }
            if (e.dead) { return Deny("AIRCRAFT RESPAWNING - TRY AGAIN SHORTLY"); }
            if (e.Occupied) { return Deny("AIRCRAFT IN USE"); }
            if (DeckPoints == null || k >= DeckPoints.Length || !DeckPoints[k]) { return Deny("NO DECK POINT FOR SLOT " + (k + 1)); }

            _launchSlot = k;
            _launchTries = 0;
            _shotDownReturnAt = -1f;
            SetMessage("");
            // 화면이 어두워진 뒤에 기체를 옮기고 태운다(암전 유지 시간 안에 끝난다).
            if (Fader)
            {
                Fader.Fade();
                SendCustomEventDelayedSeconds(nameof(_LaunchBegin), Fader.FadeOutTime);
            }
            else { _LaunchBegin(); }
            return true;
        }

        public void _LaunchBegin()
        {
            if (_launchSlot < 0) { return; }
            SaccEntity e = Aircraft[_launchSlot];
            if (!e) { _launchSlot = -1; return; }
            if (!Networking.IsOwner(e.gameObject)) { Networking.SetOwner(Networking.LocalPlayer, e.gameObject); }
            SendCustomEventDelayedFrames(nameof(_LaunchMove), 2);
        }

        public void _LaunchMove()
        {
            if (_launchSlot < 0) { return; }
            SaccEntity e = Aircraft[_launchSlot];
            if (!e || e.Occupied || e.dead) { _launchSlot = -1; return; }
            // SAV 동기화는 OnOwnershipTransferred로 갱신되는 EntityControl.IsOwner를 본다 — 그게 켜질 때까지 기다린다.
            if (!Networking.IsOwner(e.gameObject) || !e.IsOwner)
            {
                _launchTries++;
                if (Fader) { Fader.Fade(); }   // 기다리는 동안 화면을 계속 어둡게 둔다(어두운 시간을 다시 센다)
                if (_launchTries < 60) { SendCustomEventDelayedFrames(nameof(_LaunchMove), 2); }
                else { SetMessage("LAUNCH FAILED - TRY AGAIN"); _launchSlot = -1; }
                return;
            }

            Transform p = DeckPoints[_launchSlot];
            e.ShouldTeleport = true;
            e.SetNoGsFor(1f);
            e.transform.SetPositionAndRotation(p.position, p.rotation);
            Rigidbody rb = e.VehicleRigidbody;
            if (rb)
            {
                rb.position = p.position;
                rb.rotation = p.rotation;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            e.SendEventToExtensions("SFEXT_O_VehicleTeleported");
            SendCustomEventDelayedFrames(nameof(_LaunchSeat), 2);
        }

        public void _LaunchSeat()
        {
            if (_launchSlot < 0) { return; }
            SaccEntity e = Aircraft[_launchSlot];
            _launchSlot = -1;
            if (!e || e.Occupied || e.VehicleSeats == null || e.PilotSeat < 0 || e.PilotSeat >= e.VehicleSeats.Length) { return; }
            SaccVehicleSeat seat = e.VehicleSeats[e.PilotSeat];
            if (seat) { seat.Interact(); }
        }

        // ───────────── 주기 처리 ─────────────

        void Update()
        {
            UpdateCountdown();
            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) { return; }
            _pollTimer = 0.5f;

            bool running = IsRunning();
            if (Networking.IsOwner(gameObject)) { OwnerTick(running); }

            // 미션이 끝나면 잠시 뒤 모두 브리핑실로(내 화면에서 나만 옮긴다 — 모든 클라이언트가 각자 한다).
            if (_wasRunning && !running) { _returnAt = Time.time + ReturnDelay; }
            if (running) { _returnAt = -1f; }
            if (_returnAt >= 0f && Time.time >= _returnAt)
            {
                _returnAt = -1f;
                _shotDownReturnAt = -1f;
                ReturnToBriefing();
            }
            _wasRunning = running;

            CheckShotDown(running);
            ReturnIdleAircraft(0.5f);
            if (_message != "" && Time.time - _messageTime > MESSAGE_TIME) { _message = ""; }
            Refresh();
        }

        private void OwnerTick(bool running)
        {
            // 시작 잠금 풀기: RUNNING이 됐거나 제한 시간이 지났다.
            if (_startingMs != 0 && (running || !IsStarting()))
            {
                if (!running) { Debug.LogWarning("[ACO] 미션 시작을 기다리다 제한 시간이 지났습니다 — 다시 READY를 받습니다"); }
                _startingMs = 0;
                Publish();
            }
            // 미션이 끝나면 명단·준비를 비우고, 모두 내린 뒤 새 지형을 만들도록 예약한다.
            if (_wasRunning && !running)
            {
                EnsureRoster();
                for (int i = 0; i < _roster.Length; i++) { _roster[i] = -1; }
                _readyMask = 0;
                _countdownEndMs = 0;
                _launchEndMs = 0;
                _terrainPending = NewTerrainAfterMission && _terrain != null;
                Publish();
            }
            if (_terrainPending && !IsBusy() && !AnyAircraftOccupied())
            {
                _terrainPending = false;
                _lastRerollMs = Now();
                _terrainBy = -1;
                Publish();
                NewTerrainAndPreview();
            }
            // 출격 카운트다운이 끝났다(그동안 리롤이 잠겨 있어 브리핑 준비는 그대로다).
            if (_launchEndMs != 0 && !IsBusy() && Now() - _launchEndMs >= 0)
            {
                if (IsBriefingReady()) { StartMission(); }
                else { _launchEndMs = 0; Publish(); }
            }
            // 준비 카운트다운(끝났어도 브리핑 준비가 안 됐으면 될 때까지 기다린다)
            if (_countdownEndMs != 0 && !IsBusy() && Now() - _countdownEndMs >= 0 && IsBriefingReady()) { FinishCountdown(); }
            // 브리핑 준비가 막 끝났을 때 전원 준비면 시작
            if (!IsBusy()) { EvaluateReady(); }
            // 페일세이프: 미리 배치가 없고 생성기가 쉬고 있으면 다시 요청한다(미션 뒤 새 지형을 끈 경우, 생성기 오너가 배치 도중 나간 경우).
            if (!IsBusy() && !_terrainPending && !AnyAircraftOccupied() && Manager && Manager.Generator
                && !Manager.Generator.IsDeploying() && !IsBriefingReady() && Time.time - _previewRequestAt > 15f)
            {
                _previewRequestAt = Time.time;
                if (_terrain && _terrain.GetSeed() == 0) { NewTerrainAndPreview(); }   // 지형이 한 번도 안 만들어졌다
                else
                {
                    if (_expectedSeed != 0 && _terrain && _terrain.GetSeed() != _expectedSeed) { _expectedSeed = 0; Publish(); }
                    Manager.Generator.RequestPreviewDeploy(0);
                }
            }
            // 나간 플레이어가 남아 있으면 지운다(OnPlayerLeft를 놓친 경우 — 오너가 바뀌는 순간 등).
            for (int i = 0; i < _roster.Length; i++)
            {
                if (_roster[i] >= 0 && !Utilities.IsValid(VRCPlayerApi.GetPlayerById(_roster[i]))) { RemovePlayer(_roster[i]); }
            }
        }

        /// 미션 중 내가 조종하던 참가자 기체에서 벗어났다: 기체가 터졌거나 안전지대 밖이면(바다·공중) 잠시 뒤 HangarPoint(비면 BriefingPoint)로.
        private void CheckShotDown(bool running)
        {
            int piloted = -1;
            for (int i = 0; i < SlotCount(); i++) { if (Aircraft[i] && Aircraft[i].Piloting) { piloted = i; break; } }
            Transform back = HangarPoint != null ? HangarPoint : BriefingPoint;
            if (_pilotedSlot >= 0 && piloted < 0 && running && back != null)
            {
                SaccEntity e = Aircraft[_pilotedSlot];
                bool crashed = e != null && e.dead;
                VRCPlayerApi me = Networking.LocalPlayer;
                bool unsafeExit = Progress != null && Progress.SafeZones != null && Progress.SafeZones.Length > 0
                    && Utilities.IsValid(me) && !Progress.IsInSafeZone(me.GetPosition());
                if (crashed || unsafeExit) { _shotDownReturnAt = Time.time + ShotDownReturnDelay; }
            }
            _pilotedSlot = piloted;

            if (_shotDownReturnAt >= 0f && Time.time >= _shotDownReturnAt)
            {
                _shotDownReturnAt = -1f;
                if (piloted < 0 && back != null) { TeleportLocal(back); }
            }
        }

        /// 참가자 기체에 타고 있으면 내리고(순간이동은 좌석 밖에서만 된다) 브리핑실로 옮긴다.
        private void ReturnToBriefing()
        {
            if (!BriefingPoint) { return; }
            for (int i = 0; i < SlotCount(); i++)
            {
                if (Aircraft[i] && Aircraft[i].Piloting) { Aircraft[i].ExitStation(); }
            }
            // 내리는 처리가 끝난 뒤 옮긴다(암전이 있으면 어두워지는 동안 끝난다).
            SendCustomEventDelayedFrames(nameof(_ReturnTeleport), 2);
        }

        public void _ReturnTeleport()
        {
            if (BriefingPoint) { TeleportLocal(BriefingPoint); }
        }

        private void TeleportLocal(Transform target)
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!target || !Utilities.IsValid(me)) { return; }
            if (Fader) { Fader.FadeTeleport(target.position, target.rotation); }
            else { me.TeleportTo(target.position, target.rotation); }
        }

        /// 새 지형을 만들고 그 위에 병력을 미리 배치한다(브리핑 작전 테이블용). 씨앗을 직접 넘겨 생성기가 새 지형을 기다리게 한다.
        private void NewTerrainAndPreview()
        {
            int seed = Random.Range(1, int.MaxValue);
            _expectedSeed = _terrain != null ? seed : 0;
            _previewRequestAt = Time.time;
            RequestSerialization();
            if (_terrain) { _terrain.RequestGenerate(seed); }
            if (Manager && Manager.Generator) { Manager.Generator.RequestPreviewDeploy(_expectedSeed); }
        }

        /// 첫 미리 배치. 지형이 아직 한 번도 만들어지지 않았으면(육각 지형의 GenerateOnStart가 꺼져 있으면) 새 지형부터 만든다 —
        /// 미션 시작 때 지형을 새로 만들지 않으므로 여기서 안 만들면 지형이 없다.
        public void _InitialPreview()
        {
            if (!Networking.IsOwner(gameObject) || IsBusy() || !Manager || !Manager.Generator) { return; }
            if (_terrain && _terrain.GetSeed() == 0) { NewTerrainAndPreview(); }
            else { Manager.Generator.RequestPreviewDeploy(0); }
        }

        /// 내가 오너인 빈 기체가 보관 장소 밖에 오래 있으면 SAV 전체 리스폰으로 되돌린다(모든 클라이언트에서 탄약·연료·체력 재충전,
        /// 오너가 원래 위치로 옮긴다).
        private void ReturnIdleAircraft(float dt)
        {
            for (int i = 0; i < SlotCount(); i++)
            {
                SaccEntity e = Aircraft[i];
                if (!e || e.Occupied || e.dead || !Networking.IsOwner(e.gameObject)
                    || Vector3.Distance(e.transform.position, _homePos[i]) < HomeTolerance)
                {
                    _idleTime[i] = 0f;
                    continue;
                }
                _idleTime[i] += dt;
                if (_idleTime[i] < IdleReturnTime) { continue; }
                _idleTime[i] = 0f;
                e.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SaccEntity.SendRespawn));
            }
        }

        // ───────────── 표시 ─────────────

        /// 매 프레임: 출격 카운트다운 시작 소리, 큰 카운트다운 판(초가 바뀔 때만 글자를 고친다), 마지막 몇 초 틱 소리.
        private void UpdateCountdown()
        {
            int end = _launchEndMs;
            if (!_launchSeenInit)
            {
                // 처음 본 상태(늦게 들어옴 포함)는 기준만 — 이미 진행 중인 카운트다운에 소리를 내지 않는다
                _launchSeenInit = true;
                _seenLaunchEndMs = end;
            }
            if (end != 0 && _seenLaunchEndMs == 0 && AllReadySound) { AllReadySound.Play(); }
            _seenLaunchEndMs = end;

            bool show = end != 0 && !IsBusy();
            int sec = show ? Mathf.Max(Mathf.CeilToInt((end - Now()) / 1000f), 0) : -1;
            if (sec == _lastCountdownSec) { return; }
            if (show && CountdownTickSound && sec > 0 && sec <= TickSeconds && _lastCountdownSec > sec) { CountdownTickSound.Play(); }
            _lastCountdownSec = sec;
            if (CountdownOverlays != null)
            {
                for (int i = 0; i < CountdownOverlays.Length; i++)
                {
                    if (CountdownOverlays[i] && CountdownOverlays[i].activeSelf != show) { CountdownOverlays[i].SetActive(show); }
                }
            }
            if (!show || CountdownTexts == null) { return; }
            string text = "<size=40%>ALL READY - LAUNCH IN</size>\n" + sec + "\n<size=30%>LOADOUT AND UPGRADES LOCKED</size>";
            for (int i = 0; i < CountdownTexts.Length; i++) { if (CountdownTexts[i]) { CountdownTexts[i].text = text; } }
        }

        private void SetMessage(string s)
        {
            _message = s;
            _messageTime = Time.time;
            Refresh();
        }

        private void Refresh()
        {
            bool running = IsRunning();
            bool starting = IsStarting();
            int mySlot = LocalSlot();

            string s = "SORTIE ROSTER   <size=70%>MISSION: " + _phases + " PHASE" + (_phases > 1 ? "S" : "") + TerrainLabel() + "</size>";
            for (int i = 0; i < SlotCount(); i++)
            {
                int id = _roster != null && i < _roster.Length ? _roster[i] : -1;
                VRCPlayerApi p = id >= 0 ? VRCPlayerApi.GetPlayerById(id) : null;
                string line = (i + 1) + ". ";
                if (!Utilities.IsValid(p)) { line += "<color=#557766>- OPEN -</color>"; }
                else
                {
                    line += (i == mySlot ? "<color=#FFD24D>" + p.displayName + "</color>" : p.displayName);
                    if (Aircraft[i] && Aircraft[i].Occupied) { line += "<pos=60%><color=#7FD0FF>AIRBORNE</color>"; }
                    else if (!running && (_readyMask & (1 << i)) != 0) { line += "<pos=60%><color=#7FFF8A>READY</color>"; }
                    else if (!running) { line += "<pos=60%><color=#888888>STANDBY</color>"; }
                }
                s += "\n<size=80%>" + line + "</size>";
            }
            string footer;
            if (starting) { footer = "<color=#FFD24D>MISSION STARTING...</color>"; }
            else if (running) { footer = "MISSION IN PROGRESS - JOIN AN OPEN SLOT AND PRESS LAUNCH"; }
            else if (_launchEndMs != 0)
            {
                int sec = Mathf.Max(Mathf.CeilToInt((_launchEndMs - Now()) / 1000f), 0);
                footer = "<color=#7FFF8A>ALL READY - LAUNCH IN " + sec + "s</color> - ROSTER, READY, LOADOUT AND UPGRADES LOCKED";
            }
            else if (_countdownEndMs != 0)
            {
                int sec = Mathf.Max(Mathf.CeilToInt((_countdownEndMs - Now()) / 1000f), 0);
                footer = "<color=#FFD24D>AUTO START IN " + sec + "s</color> - PLAYERS NOT READY WILL BECOME SPECTATORS";
            }
            else if (!IsBriefingReady()) { footer = "<color=#FFD24D>PREPARING TERRAIN AND ENEMY FORCES...</color>"; }
            else { footer = "JOIN, PICK YOUR LOADOUT (NEW TERRAIN IF YOU LIKE), THEN PRESS READY - LAUNCH 10s AFTER ALL ARE READY"; }
            s += "\n<size=60%>" + footer + "</size>";
            if (_message != "") { s += "\n<size=60%><color=#FF5544>" + _message + "</color></size>"; }
            SetTexts(StatusTexts, s);

            RefreshConsole(running, starting, mySlot);
            RefreshButtons(running, starting, mySlot);
        }

        /// 버튼 글자와 누를 수 있는지(회색). 회색이어도 누를 수는 있다 — 누르면 이유를 안내하고 거절음이 난다.
        private void RefreshButtons(bool running, bool starting, int mySlot)
        {
            bool participant = mySlot >= 0;
            bool occupied = AnyAircraftOccupied();
            bool briefingReady = IsBriefingReady();
            bool locked = IsLaunchCountdown();
            bool myReady = participant && (_readyMask & (1 << mySlot)) != 0;
            SaccEntity mine = participant && mySlot < SlotCount() ? Aircraft[mySlot] : null;

            // JOIN/LEAVE
            string joinLabel = participant ? "LEAVE" : "JOIN";
            bool joinOn = participant ? !(mine != null && mine.Piloting) : HasOpenSlot() && !locked;
            if (!participant && locked) { joinLabel = "LOCKED"; }
            else if (!participant && !joinOn) { joinLabel = "ROSTER FULL"; }

            // READY
            string readyLabel = myReady ? "CANCEL READY" : (!running && !starting && !briefingReady ? "PREPARING..." : "READY");
            bool readyOn = participant && !running && !starting && !occupied && (myReady ? !locked : briefingReady);
            if (myReady && locked) { readyLabel = "LAUNCHING..."; }

            // LAUNCH / SPECTATE
            string launchLabel = "LAUNCH";
            bool launchOn = false;
            if (!participant && running) { launchLabel = "SPECTATE"; launchOn = SpectatePoint != null; }
            else if (participant && running && !starting && mine != null)
            {
                if (mine.Piloting) { launchLabel = "IN FLIGHT"; }
                else if (mine.dead) { launchLabel = "RESPAWNING"; }
                else if (mine.Occupied) { launchLabel = "IN USE"; }
                else { launchOn = true; }
            }

            // REROLL(쿨타임 중엔 남은 시간)
            string rerollLabel = "NEW TERRAIN";
            bool rerollOn = false;
            if (running || starting) { }
            else if (locked) { rerollLabel = "TERRAIN LOCKED"; }
            else if (_terrain != null && !briefingReady) { rerollLabel = "GENERATING..."; }
            else
            {
                int left = RerollCooldownLeft();
                if (left > 0) { rerollLabel = "NEW TERRAIN (" + left + "s)"; }
                else { rerollOn = participant && !occupied && _terrain != null; }
            }

            // 내 행동 버튼(JOIN → READY → LAUNCH)
            string primaryLabel;
            bool primaryOn;
            if (!participant)
            {
                if (running && !HasOpenSlot()) { primaryLabel = "SPECTATE"; primaryOn = SpectatePoint != null; }
                else { primaryLabel = joinLabel; primaryOn = joinOn; }
            }
            else if (running || starting) { primaryLabel = starting ? "STARTING..." : launchLabel; primaryOn = !starting && launchOn; }
            else { primaryLabel = readyLabel; primaryOn = readyOn; }
            bool leaveOn = participant && !(mine != null && mine.Piloting);
            bool phaseOn = !running && !starting && !locked;

            if (Buttons != null)
            {
                for (int i = 0; i < Buttons.Length; i++)
                {
                    ACO_FlowButton b = Buttons[i];
                    if (!b) { continue; }
                    if (b.Action == ACTION_TOGGLE_JOIN) { b.SetState(joinLabel, joinOn); }
                    else if (b.Action == ACTION_TOGGLE_READY) { b.SetState(readyLabel, readyOn); }
                    else if (b.Action == ACTION_LAUNCH) { b.SetState(launchLabel, launchOn); }
                    else if (b.Action == ACTION_REROLL) { b.SetState(rerollLabel, rerollOn); }
                    else if (b.Action == ACTION_PRIMARY) { b.SetState(primaryLabel, primaryOn); }
                    else if (b.Action == ACTION_LEAVE) { b.SetState("LEAVE", leaveOn); }
                    else if (b.Action == ACTION_PHASES_DOWN) { b.SetState("-", phaseOn && _phases > 1); }
                    else if (b.Action == ACTION_PHASES_UP) { b.SetState("+", phaseOn && _phases < 5); }
                    else if (b.Action == ACTION_JOIN_SLOT) { RefreshSlotCard(b, running, mySlot, locked); }
                }
            }
            if (PhaseButtons != null)
            {
                for (int i = 0; i < PhaseButtons.Length; i++)
                {
                    if (PhaseButtons[i]) { PhaseButtons[i].SetState(phaseOn, PhaseButtons[i].PhaseCount == _phases); }
                }
            }
        }

        private string TerrainName()
        {
            if (!_terrain) { return "-"; }
            int preset = _terrain.GetBuiltPreset();
            if (preset < 0) { return "GENERATING..."; }
            return TerrainNames != null && preset < TerrainNames.Length ? TerrainNames[preset] : "#" + preset;
        }

        private string PlayerName(int playerId)
        {
            if (playerId < 0) { return ""; }
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(playerId);
            return Utilities.IsValid(p) ? p.displayName : "";
        }

        private string SlotName(int k)
        {
            if (SlotNames != null && k < SlotNames.Length && SlotNames[k] != null && SlotNames[k] != "") { return SlotNames[k]; }
            return k < SlotCount() && Aircraft[k] ? Aircraft[k].gameObject.name : "SLOT " + (k + 1);
        }

        /// 작전 콘솔: 상태 배너, 작전 카드(페이즈·지형).
        private void RefreshConsole(bool running, bool starting, int mySlot)
        {
            bool hasBanner = BannerTexts != null && BannerTexts.Length > 0;
            bool hasCards = (PhaseTexts != null && PhaseTexts.Length > 0) || (TerrainTexts != null && TerrainTexts.Length > 0);
            if (!hasBanner && !hasCards) { return; }

            int members = 0;
            int ready = 0;
            for (int i = 0; i < SlotCount(); i++)
            {
                if (_roster == null || i >= _roster.Length || _roster[i] < 0) { continue; }
                members++;
                if ((_readyMask & (1 << i)) != 0) { ready++; }
            }
            bool participant = mySlot >= 0;
            SaccEntity mine = participant && mySlot < SlotCount() ? Aircraft[mySlot] : null;
            bool myReady = participant && (_readyMask & (1 << mySlot)) != 0;

            if (hasBanner)
            {
                string head;
                string next;
                if (starting) { head = "MISSION STARTING"; next = "STAND BY - LAUNCHING ALL PILOTS"; }
                else if (running)
                {
                    head = "MISSION IN PROGRESS";
                    if (participant)
                    {
                        if (mine != null && mine.Piloting) { next = "GOOD HUNTING - YOU ARE IN FLIGHT"; }
                        else if (mine != null && mine.dead) { next = "YOUR AIRCRAFT IS RESPAWNING..."; }
                        else { next = "NEXT: PRESS LAUNCH TO SORTIE"; }
                    }
                    else { next = HasOpenSlot() ? "NEXT: TAP AN OPEN SLOT TO JOIN, THEN LAUNCH" : "ROSTER FULL - PRESS SPECTATE TO WATCH FROM THE DECK"; }
                }
                else if (_launchEndMs != 0)
                {
                    int sec = Mathf.Max(Mathf.CeilToInt((_launchEndMs - Now()) / 1000f), 0);
                    head = "<color=#7FFF8A>ALL READY - LAUNCH IN " + sec + "</color>";
                    next = "ROSTER, READY, LOADOUT AND UPGRADES LOCKED";
                }
                else
                {
                    head = members > 0 ? "BRIEFING   <color=#7FFF8A>" + ready + " / " + members + " READY</color>" : "BRIEFING   <color=#8FA89A>NO PILOTS YET</color>";
                    if (!IsBriefingReady()) { next = "<color=#FFD24D>PREPARING TERRAIN AND ENEMY FORCES...</color>"; }
                    else if (!participant) { next = HasOpenSlot() ? "NEXT: TAP AN OPEN SLOT (OR JOIN) TO SIGN UP" : "ROSTER FULL - WAIT FOR AN OPEN SLOT"; }
                    else if (AnyAircraftOccupied()) { next = "WAIT UNTIL ALL AIRCRAFT HAVE LANDED"; }
                    else if (!myReady) { next = "NEXT: PICK YOUR LOADOUT IN THE HANGAR, THEN PRESS READY"; }
                    else
                    {
                        int left = members - ready;
                        next = "READY - WAITING FOR " + left + " MORE PILOT" + (left > 1 ? "S" : "");
                    }
                    if (_countdownEndMs != 0)
                    {
                        int sec = Mathf.Max(Mathf.CeilToInt((_countdownEndMs - Now()) / 1000f), 0);
                        next += "\n<color=#FFD24D>AUTO START IN " + sec + "s - PILOTS NOT READY BECOME SPECTATORS</color>";
                    }
                }
                string b = head + "\n<size=55%>" + next + "</size>";
                if (_message != "") { b += "\n<size=50%><color=#FF5544>" + _message + "</color></size>"; }
                SetTexts(BannerTexts, b);
            }

            if (hasCards)
            {
                bool locked = running || starting || IsLaunchCountdown();
                SetTexts(PhaseTexts, _phases.ToString());
                string by = PlayerName(_phasesBy);
                string info = "ABOUT " + Mathf.RoundToInt(_phases * MinutesPerPhase) + " MIN - LATER PHASES SCORE HIGHER";
                info += "\n<size=80%>" + (locked ? "<color=#FFD24D>LOCKED</color>" : (by != "" ? "SET BY " + by : "TAP - / + TO CHANGE")) + "</size>";
                SetTexts(PhaseInfoTexts, info);
                string tby = PlayerName(_terrainBy);
                string wx = Weather ? "WEATHER: " + Weather.GetWeatherName() + "   " : "";
                string terrain = TerrainName() + "\n<size=45%>" + wx + (tby != "" ? "NEW TERRAIN BY " + tby : "SEE THE OPS TABLE") + "</size>";
                SetTexts(TerrainTexts, terrain);
            }
        }

        /// 명단 칸 카드: 기체 이름 + 조종사 + 상태(빈 칸은 "OPEN - TAP TO JOIN"). 칸 수보다 많은 카드는 숨긴다.
        private void RefreshSlotCard(ACO_FlowButton b, bool running, int mySlot, bool locked)
        {
            int k = b.Slot;
            bool exists = k >= 0 && k < SlotCount();
            if (b.gameObject.activeSelf != exists) { b.gameObject.SetActive(exists); }
            if (!exists) { return; }
            int id = _roster != null && k < _roster.Length ? _roster[k] : -1;
            VRCPlayerApi p = id >= 0 ? VRCPlayerApi.GetPlayerById(id) : null;
            string head = "<size=65%>" + (k + 1) + ". " + SlotName(k) + "</size>";
            if (!Utilities.IsValid(p))
            {
                bool inUse = Aircraft[k] && Aircraft[k].Occupied;
                string open = locked ? "<color=#8FA89A>OPEN - LOCKED</color>" : (inUse ? "<color=#8FA89A>OPEN - AIRCRAFT IN USE</color>" : "<color=#9FE0B0>+ OPEN - TAP TO JOIN</color>");
                b.SetStyle(head + "\n" + open, SlotOpenColor);
                return;
            }
            string name = k == mySlot ? "<color=#FFD24D>" + p.displayName + " (YOU)</color>" : p.displayName;
            string state;
            Color bg = k == mySlot ? SlotMineColor : SlotTakenColor;
            if (Aircraft[k] && Aircraft[k].Occupied) { state = "<color=#7FD0FF>AIRBORNE</color>"; bg = SlotAirborneColor; }
            else if (running) { state = "<color=#B0C4B8>ON DECK</color>"; }
            else if ((_readyMask & (1 << k)) != 0) { state = "<color=#7FFF8A>READY</color>"; bg = k == mySlot ? SlotMineColor : SlotReadyColor; }
            else { state = "<color=#B0C4B8>ARMING</color>"; }
            b.SetStyle(head + "\n" + name + "\n<size=65%>" + state + "</size>", bg);
        }

        private string TerrainLabel()
        {
            if (!_terrain) { return ""; }
            int preset = _terrain.GetBuiltPreset();
            if (preset < 0) { return "   TERRAIN: GENERATING..."; }
            return "   TERRAIN: " + (TerrainNames != null && preset < TerrainNames.Length ? TerrainNames[preset] : "#" + preset)
                + (Weather ? "   WEATHER: " + Weather.GetWeatherName() : "");
        }

        private void SetTexts(TextMeshProUGUI[] texts, string s)
        {
            if (texts == null) { return; }
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] && texts[i].text != s) { texts[i].text = s; }
            }
        }
    }
}
