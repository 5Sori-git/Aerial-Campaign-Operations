# HUD·UI — 표적 마커·작전판·패널·무전·RWR·레이더·관전·데칼

> 설계 문서의 한 분야. 목차·작성 규칙은 `Docs/DESIGN.md`. 절 번호는 예전 DESIGN.md 그대로(코드 주석의 "DESIGN.md 6-xx"가 가리키는 번호).

### 6-6. HUD / 표적 마커 ✅ (로컬 전용, 동기화 없음)

- **점수 HUD** (`ACO_ScoreHUD`): 셋째 줄은 페이즈(`PHASE 2/4  SEAD  3/5`, 할당량은 점수, 시한 표적은 남은 시간, 페이즈 사이엔 `NEXT PHASE 7s`), 점수 옆에 지금 배율. 페이즈 시작/클리어 때 가운데에 `PhaseBannerTime`(5초) 안내. 머리 기준 `HeadOffset`(왼쪽 위 앞 0.9m)에 패널. 회전만 `FollowSharpness`로 부드럽게 따라가고 **위치는 매 프레임 머리에 고정**(위치까지 늦게 따라가면 300m/s 비행 중 패널이 수십 m 뒤처진다). 4Hz로 갱신: `SCORE  x배율` / `TIME`(남은 시간, 1분 미만 빨강; 제한 없으면 `ELAPSED`) `WAVE` / 페이즈 줄. 끝나면 가운데에 `MISSION COMPLETE / RANK S / 보너스` 또는 `MISSION FAILED / RANK F`를 `ResultDisplayTime`(8초) 동안 띄우고 숨긴다(자세한 결과는 작전판). 미션 전(대기)에는 숨김(`ShowWhenIdle`).
- **표적 마커** (`ACO_TargetMarkers`): 미션 생성기 풀(전투기/지상/고가치 표적/함선 부위) 중 활성이고 살아 있는(SaccTarget 체력 > 0) 유닛을 `MaxDistance`(10km) 안에서 표시. 격침된 함선의 남은 부위는 빠진다.
  - **화면 크기**: 머리에서 유닛 방향으로 `DisplayDistance`(30m)에 놓고, 기본 크기 = `2·30·tan(MarkerAngle/2)`(2.5°). **원근감**: `NearDistance`(1km)까지 1배, `MaxDistance`(10km)에서 `FarScale`(0.45)배, 사이는 log(거리)에 비례해 줄인다 — 거리와 무관하게 같은 크기면 원근감이 애매해서. 셰이더가 ZTest Always라 지형/구름/기체 뒤에서도 보인다. 머리 기울기(롤)에 맞춰 세운다.
  - **반투명**: 테두리 알파 0.55(목표 0.75), 마커 글자 0.9, 점수 HUD 글자 `TextAlpha`(0.85). 테두리 색은 `Generated/ACO_Marker*.mat`의 `_Color`에서 바로 바꿀 수 있다.
  - **아군(호위 대상)**: 파란 테두리(`FriendlyMaterial`, `Generated/ACO_MarkerFriendly.mat`) + 위에 이름(`VIP`), 목표처럼 `ObjectiveMaxDistance`까지 보인다. 예전 HUD 프리팹처럼 머티리얼 칸이 비어 있으면 Bake가 채운다(아군/경유점/목적지).
  - **호위 항로**(`ShowEscortRoute`, 공중 호위/지상 호위 페이즈): 호위 대상이 아직 지나지 않은 경유점 = 옅은 파란 마름모(`WaypointMaterial`, 크기 `WaypointScale` 0.7) + "WP n", 목적지 = 큰 하늘색 마름모(`DestinationMaterial`, `ObjectiveScale`) + "DEST 거리". 목적지에서 `WaypointMinGap`(1.5km) 안의 경유점(착륙 접지점 등)은 생략. 마름모 = 사각형 메시를 45° 돌린 것, 마커 풀을 같이 쓴다(유닛 다음).
  - 모양: 항공기 = 사각형, 지상 = 육각형. **지금 페이즈 목표**(관리자 `GetActiveObjectiveTag`와 태그가 같은 유닛 — 전투기 목표면 빨간 사각형) = 빨간 테두리 + 위에 빨간 `TGT`(`ObjectiveScale` 1.3배), 목표는 `ObjectiveMaxDistance`(60km)까지 보인다. 관리자가 연결되지 않았으면 예전처럼 고가치 표적이 목표.
  - **시선 정보**: 머리 정면에서 `LookAngle`(4°) 안의 유닛 중 가장 가운데 하나에 `종류  거리`(예: `FTR ACE  4.2km`, `SAM  850m`). 전투기는 종류 약어(AI `MarkerLabel` — 전투기 FTR, 공격기 ATK) + 숙련도 `ACE/VET/RKY`(신병 = Rookie)(작전판 격추 기록도 같다, 슈퍼에이스는 이름), 지상/표적은 각 유닛의 `MarkerLabel`(SAM, AAG, FAC, BNK, BB, DD, VLS, GUN, CIWS).
  - **체력 바**: 시선 정보 아래에 남은 체력(`SaccTarget.Health / FullHealth`) 막대. 비율 > `HealthMidThreshold`(0.5) 녹색, 그 이하 노랑, `HealthLowThreshold`(0.25) 이하 빨강. 끄려면 `ShowHealthBar`. 머티리얼 `Generated/ACO_Health*.mat`(배경 큐 4010, 채움 4011 — 둘 다 ZTest Always라 큐로 그리는 순서를 정한다). 원격 NPC 체력은 오너의 SaccTarget 동기화를 따라 약간 늦게 줄 수 있다.
  - **락온 표시**: 내가 조종 중이고 고른 `ACO_DFUNC_Missile`(`Weapons`, Bake가 채움)의 표적을 읽는다. 락온을 시도하는 동안(표적이 잡혔고 아직 락온 전) 그 테두리가 `LockBlinkRate`(8회/초) 일정한 속도로 계속 깜빡이고(락온되면 멈추고 빨갛게 켜진다), 시선 정보(이름표 `종류 거리` + 체력 바)가 시선 대신 **락온 대상에 고정**된다. 조준이 시작될 때마다 무작위 방향을 골라 마름모(`LockDiamond`, 사각 테두리 메시를 45° 회전)가 마커 크기 × `DiamondStartDistance`(2.5)에서 출발해 락온 진행도만큼 중심으로 다가온다(남은 거리 = (1−진행도)², 막바지에 천천히 붙는다). 락온되면 마름모가 테두리에 겹쳐지고(`DiamondScale` 0.8 — 0.71이면 꼭짓점이 변 가운데에 닿는다) 마름모와 테두리가 `LockedMaterial`(빨강)로 바뀐다. 진행도가 0으로 떨어지면(조준 이탈) 마름모를 숨기고 다음 조준은 새 방향에서 시작한다. 예전 HUD 프리팹엔 마름모·머티리얼이 없으므로 Bake가 없으면 만든다(`ACO_HUDBuilder.EnsureLockVisuals`).
    - **락온 표적 테두리 = 락온 지점**: 락온 중인 표적의 테두리는 유닛 기준점이 아니라 락온 표적 오브젝트(마름모가 쫓는 자리)에 그린다 — 둘이 달라 표적 상공에서 테두리와 마름모가 어긋나 보였다. **겹침 순서**: 마커는 모두 같은 표시 거리라 겹치면 그리는 순서가 프레임마다 바뀌어 깜빡였다(z-fighting) — 락온 테두리는 표시 거리 × `LockedFrameNear`(0.97), 마름모·십자는 × `LockMarkNear`(0.94)로 앞에 둔다(크기·출발 거리도 같은 비율 — 화면에선 그대로). 투명 큐는 먼 것부터 그리므로 늘 위에.
  - **특수무장은 십자**: 고른 무기의 `CrossLockIndicator`가 켜져 있으면(특수무장 슬롯 안의 모듈 — Bake가 켠다, 4AAM 프리셋도) 락온 진행 표시를 마름모 대신 세워진 십자(+, 가운데가 빈 팔 4개, `LockCrossMesh`, 크기 `CrossScale`)로 그린다. 같은 표시 오브젝트의 메시만 바꿔 쓴다. 메시는 HUD 빌더/Bake(`EnsureLockVisuals`)가 만든다.
  - 마커 풀 64개(빌더 상수). 상태가 바뀔 때만 SetActive/메시/머티리얼을 바꾼다. 갱신은 `PostLateUpdate`(VRChat에서 머리 위치가 확정된 뒤).
- **프리팹**: `ACO → Create Prefabs...`의 HUD → `Prefabs/ACO_HUD`. 메시(`ACO_MarkerSquare/Hexagon`), 머티리얼(녹색 항공기/지상, 빨강 목표), TMP 오버레이 머티리얼(`TextMeshPro/Distance Field Overlay`)은 `Generated/`에 저장(다시 만들면 내용만 덮어써 참조 유지). 씬에 하나 두면 빌드 시(또는 Bake) 생성기/관리자가 자동 연결된다.
- **바뀜 — 얇게·하늘·안개 대비**: 마커 테두리·락온 마름모 두께를 한 변의 8%에서 4%로(`FrameThickness`). 녹색을 순녹색 (0, 1, 0)로, 불투명하게 — 마커 85%(예전 55%), 마커 글자 95%, 비행 HUD 선·글자 100%(예전 90%), 조준 고리 85%, RWR 100/85%, HUD 레이더 격자 80%. HUD는 안개·구름보다 나중에 그려져 가리진 않지만, 안개 속 밝은 회색 배경과 녹색의 밝기가 비슷해 묻혔다 → **HUD 색을 플레이어가 고른다**(HUD 설정 HUD COLOR — 6-17). **HUD 글자에 은은한 그림자**(TMP 밑깔림 — 오른쪽 아래 흐릿한 짙은 그림자 50%, 외곽선 아님 — `ACO_HUDTextOverlay`·`ACO_FlightHudTextOverlay`). 빨강(목표)·파랑(아군)·경유점·체력 막대는 색 그대로(두께만). 적용: 프리팹 판 ACO_HUD 2·ACO_FlightHud 3·ACO_HudSettings 2 다시 만들기 + Setup Check `hud-sky-1`(이미 만든 재질·글자 그림자·씬 RWR·레이더 색) + Bake.
- 글자 크기는 TMP 3D 기준 추정값(글자 크기 10 ≈ 1단위)이라 실제 VR에서 보고 `fontSize`/패널 스케일을 조정한다.

### 6-7. 작전판 / 디브리핑 ✅ (`ACO_MissionLog`, 로컬 전용, 동기화 없음)

- 월드에 놓는 4m x 2.5m 보드(브리핑 룸 등). 미션 중에는 제목 `OPERATION LOG` + 경과 시간/점수/격추 수/웨이브, 끝나면 `DEBRIEFING - MISSION COMPLETE/FAILED  RANK x` + 점수 내역(격추 + 페이즈 + 클리어 + 시간 보너스)과 소요 시간.
- **격추 기록** 한 줄: `시각(미션 경과)  격자  조종사  무기  표적  +점수`. 최근 것이 위, `MaxRows`(16)줄까지 보이고 나머지는 `... n EARLIER`. 최대 `MaxEntries`(200)개 보관. 자기 격추는 노란색, 주요 목표는 빨간 `TGT FAC`.
- **페이즈 줄**: 페이즈 시작(`PHASE 2/4  SEAD  x1.25`)과 종료(`PHASE 2 COMPLETE  +625`)도 기록에 끼운다. 페이즈 목표였던 유닛(태그 있음)은 빨간 `TGT`. 제목 줄에 지금 페이즈, 디브리핑 점수 내역에 페이즈 보너스.
- **조종사 집계**: 이름별 격추 수/점수를 점수 순으로(`MaxPilots` 12명).
- **판정**: 관리자와 같은 "같은 serial 동안 살아 있는 걸 본 뒤 파괴됨" 규칙을 0.5초마다 로컬로 본다. 점수는 `Manager.GetKillScore`(팀 점수와 같은 표). 미션이 끝나는 순간 한 번 더 보고(마지막 목표), 그 뒤 격추는 점수가 없으므로 기록하지 않는다. 관리자의 미션 시작 시각(`GetMissionStartMs`)이 바뀌면 기록을 지운다.
- **조종사/무기**: SAV `SaccTarget.LastHitByPlayer` / `LastHitWeaponType`(맞은 순간 모든 클라이언트에서 갱신되는 마지막 피격자). 무기 1/8 = GUN, 2 = AAM, 3 = AGM, 4 = BOMB. 피격 기록이 없으면 `UNKNOWN`.
- **격자**: 생성기 작전 구역(`AreaMin/AreaMax`, 또는 `UseCustomGrid`의 `GridMin/GridMax`)을 `GridDivisions`(10)칸으로 — 열 A~(서→동, X), 행 1~(북→남, Z). `UseKeypad`면 칸 안을 키패드식 3x3으로 한 번 더 나눠 `E5-7`(7 = 북서, 9 = 북동, 1 = 남서). 기본 구역 40만 m면 한 칸 40km, 키패드 약 13km — 좁은 전장이면 `UseCustomGrid`로 줄인다.
- **한계(표시용)**: 로컬 기록이라 중간에 들어온 사람은 들어온 뒤 격추만 보이고, 아군 NPC 격추/추락처럼 플레이어 피격이 없던 격추는 이전 피격자나 `UNKNOWN`으로 남는다. 팀 점수의 정본은 관리자 값(제목 줄).
- **프리팹**: `ACO → Create Prefabs...`의 작전판 → `Prefabs/ACO_MissionBoard`. 앞면은 루트 +Z(파란 화살표) 쪽 — 화살표가 관객을 보게 놓는다. 배경은 `Generated/ACO_BoardBackground.mat`(Unlit/Color). 빌드 시(또는 Bake) 관리자/생성기가 자동 연결된다(생성기는 관리자가 쓰는 것과 같게).

### 6-16. 작전 테이블 — Tacview식 3D 미니맵 ✅ (`ACO_TacticalMap`, `ACO → Create Tactical Map`, 로컬 전용)

- **배치 대기열**(생성기, 오너 로컬): 전체 배치 요청(미리 배치/미션)은 `QueueDeploy` 하나로 합쳐 지형이 다 세워진 뒤 한 번만 한다 — 지형 대기 루프는 늘 하나, 대기 중 요청은 종류(미션 우선)·씨앗(나중 것)만 바꾼다, 미션 배치가 이미 대기 중이면 시작 요청을 무시한다. 미리 배치는 명시적인 미션 요청 없이는 절대 미션이 되지 않는다(전체 배치 번호 = 관리자의 미션 시작 신호). 예전엔 대기 중 요청마다 루프가 늘어 두 번째 배치가 미션으로 처리될 수 있었다.
- **미리 배치**(`ACO_MissionGenerator.RequestPreviewDeploy(지형 씨앗)`): 전체 배치와 같지만 전체 배치 번호를 올리지 않아 미션 시작으로 세지 않는다(`_previewSeed` = 그 지형 씨앗, 동기화). 미션 시작(`RequestStartMission` → `StartMissionDeploy`)이 오면 같은 지형이면 **다시 배치하지 않고** 번호만 올려 그대로 확정한다. 미리 배치 도중(지형 대기 중)에 시작이 오면 끝나는 대로 확정. 씨앗을 직접 넘기는 이유: 리롤 직후엔 지형 씨앗이 아직 생성기 오너에게 동기화되지 않았을 수 있다.
- 게임 진행이 요청한다: 월드를 연 오너가 첫 지형에(3초 뒤), 리롤마다, 미션이 끝나고 모두 내린 뒤 새 지형마다. 브리핑 동안 적 전투기는 초계를 돈다(미션 뒤 남은 병력과 같은 상태).
- **테이블**: 둥근 테이블(높이 0.9m, 윗면 = 해수면) + 바다 원판 + 축소 지형 + 마커. 축척 = `MapRadius`(1.4m) / 지형 `FieldRadius`, 높이 `VerticalExaggeration`(3배). MapRoot 로컬 축 = 월드 축(지형 중심 기준).
- **지형 메시**: 지형이 새 씨앗으로 다 세워지면 땅 칸마다 육각 기둥(윗면 꼭짓점 6 + 해수면 꼭짓점 6, 뾰족한 꼭짓점 ±Z — 지형 격자와 같다), 윗면 색 = 높이대(`HeightColors`, 경계는 지형 `HeightThresholds`), 옆면 = × `SideShade`. 런타임 `new Mesh()` 하나(3,000칸 ≈ 정점 36,000, 한도 65,000) — 프레임당 `CellsPerFrame`(150)칸씩 나눠 채우고(약 20프레임) 다 되면 한 번에 바꿔 끼운다(그동안 이전 지도가 보인다, 브리핑실을 나가도 끝까지 만든다), 셰이더 `ACO/MapVertexColor`(정점 색, 조명 없음, 양면).
- **마커**(시작할 때 풀마다 하나씩 복제, 배치된 것만 켬): 참가자 기체 파랑(이름표 = 조종사, 보관 장소에 있으면 숨김) / 적 전투기 빨강 / 지상·함선 주황(페이즈 목표면 노란 TGT 고리) / 고가치 표적 노랑 / 호위 대상 초록 / 기지(`HomeBase`) 파랑. 항공기는 기수 방향 그대로 + 꼬리 궤적 8초(`TrailRenderer`), 지상은 요만. 적도 전부 보인다.
- **비용**: 내가 테이블에서 `ActiveRadius`(15m) 안(브리핑실)에 있을 때만 `UpdateInterval`(0.1초)마다 갱신, 지형 메시도 그때 만든다. 네트워크 사용 없음.
- 조작(확대/회전/추적)은 나중에.

### 6-17. HUD 설정 패널 / 크로스헤어 ✅ (`ACO_HudSettings`, `ACO_HudSettingButton`, `ACO → Create HUD Settings Panel`, 로컬 전용)

- **크로스헤어**(`ACO_TargetMarkers.Crosshair`): 시선 정면(VR이면 두 눈 사이 정면 = 시선 정보 보기의 기준) `CrosshairDistance`(기본 30m, RWR 링도 같은 거리)에 지름 `CrosshairAngle`(1.2°)의 얇은 원, 항상 맨 위(`ACO/MarkerOverlay`). 미션 중에만(또는 설정 미리보기 중) 보인다. HUD 프리팹에 들어 있고, 없는 HUD는 Bake가 만든다(`EnsureCrosshair`).
- **설정 항목**(−/+, 플레이어별, `PlayerData` `aco_hud_` + 고정 ID(`panel_x`, `panel_y`, `panel_distance`, `panel_scale`, `marker_size`, `info_angle`, `crosshair`, `crosshair_size`)에 저장): 점수 패널 좌우·상하(`HeadOffset.x/y`, 2cm), 거리(`HeadOffset.z`, 5cm), 크기(패널 스케일 배율 0.1), 마커 테두리 크기(`MarkerAngle`, 0.25°), 시선 정보 각도(`LookAngle`, 0.5°), 크로스헤어 켜기/끄기, 크로스헤어 크기(0.2°). 범위는 `Mins/Maxs`. RESET = 시작할 때 HUD에 들어 있던 값(인스펙터 값). 불러오기(OnPlayerRestored) 전에는 저장하지 않는다.
- 바꿀 때마다 `PreviewTime`(6초) 동안 미션 전에도 점수 패널(HUD PREVIEW 예시 글자)과 크로스헤어를 보여 준다(`ACO_ScoreHUD.Preview`, `ACO_TargetMarkers.Preview`).
- 패널 1.8 × 1.75m(월드 공간 Canvas + UI 버튼). Bake가 씬의 HUD를 연결한다. 하나만 두는 걸 권한다(같은 저장 값).
- **결과·안내 묶음**(`ACO_ScoreHUD.ResultGroup`): 결과/페이즈 배너(`ResultText`)와 안내(`NoticeText`)를 점수 패널과 따로 머리에 붙인다 — 위치 = `ResultOffset`(x, y, 기본 (0, 0.07) = 시야 가운데 조금 위), 거리는 점수 패널(`HeadOffset.z`)과 같고 패널 거리 비율로 같이 키운다. 설정 항목 `RESULT LEFT / RIGHT`·`RESULT UP / DOWN`(2cm)·`RESULT SIZE`(0.5~2) — 키 `result_x`, `result_y`, `result_scale`. 예전 HUD는 Bake가 빈 묶음을 만들고(`EnsureResultGroup`, ResultOffset = 지금 결과 글자 자리) 두 글자는 실행 중 `ACO_ScoreHUD`가 묶음으로 옮긴다(프리팹 인스턴스라 에디터에서 못 옮긴다). 미리보기 때 결과 예시 글자와 "NOTICE PREVIEW"가 뜬다.
- **크로스헤어·RWR 거리**(`CROSSHAIR / RWR DISTANCE`, 키 `crosshair_distance`, 0.5~60m, 한 단계 = 현재 거리의 15%): `ACO_TargetMarkers.CrosshairDistance` — 크로스헤어와 RWR 링이 같이 움직인다. 크기는 각도 기준이라 화면 크기는 그대로, VR에서 보이는 깊이만 바뀐다(마커는 그대로 `DisplayDistance`). RWR 링 안쪽 반경은 크로스헤어 바깥(반지름 + 0.4°)보다 작아지지 않고 바깥·락온 호도 그만큼 밀려나, 크로스헤어를 키워도 쉐브론이 겹치지 않는다.
- 패널 줄 순서(보이는 순서만, 항목 번호·저장 ID는 그대로): 점수 패널 4 → 결과·안내 3 → 마커 2 → 크로스헤어·RWR 5. 예전 패널(10줄)은 셋업 검사가 경고하고 [고치기]가 프리팹을 다시 만든다.

- **추가 — HUD COLOR**(`ITEM_HUD_COLOR`, 저장 키 `aco_hud_hud_color`): 프리셋 ◀ ▶ 돌아가며 — GREEN(순녹색, 기본)·YELLOW·CYAN·WHITE·ORANGE·PINK(`HudColors`/`HudColorNames`). 비행 HUD(선·녹색 글자 — 빨강 경고 글자는 그대로), 적 마커(공중·지상 테두리 — 공용 재질을 이 화면 전용 사본으로 바꿔 쓴다, 정보 글자), 조준 고리, RWR(먼 위협·락온 호 — 가까우면 빨강으로 섞이는 건 그대로), HUD 레이더(녹색 선·북쪽 표시). 목표·아군·경유점·체력 막대는 그대로. 알파·밝기·불투명도 설정은 그대로 곱해진다.
- **추가 — TEXT SHADOW**(`ITEM_TEXT_SHADOW`, 저장 키 `aco_hud_text_shadow`, 기본 ON): HUD 글자 그림자(공용 글자 재질 `ACO_HUDTextOverlay`·`ACO_FlightHudTextOverlay`의 밑깔림 알파 — 끄면 0). Bake가 재질을 `TextShadowMaterials`에 넣는다. 재생을 마치면(OnDisable) 원래 값으로 돌려 에디터 재생에서 재질 에셋이 꺼진 채 남지 않게.
- **추가 — 비행 HUD 미리보기**: 값을 바꾸면(PreviewTime 6초) 기체에 타지 않아도 비행 HUD가 시선 앞에 뜬다 — 머리 방향 그대로, 예시 값(속도 450kt·고도 12000ft·방위 = 시선 방향, 피치 사다리·속도 벡터). 크기·밝기·색·그림자를 바꾸며 본다.

### 6-18. 셋업 검사 창 ✅ (`ACO_SetupValidator`, `ACO → Setup Check...`)

- 씬을 훑어 빠진 셋업을 오류(동작 안 함)/경고(일부 기능 꺼짐)/정보(선택 기능)로 보여 준다. [선택]은 그 오브젝트를 고르고, [고치기]는 기존 메뉴(Create ..., 프리팹 다시 만들기·씬에 놓기, Add Upgrade Extension, ExtensionUdonBehaviours 등록, 빈 지점 만들기, MissionSplash 켜기 등)를 부른 뒤 Bake + 다시 검사. [자동 수정 모두]는 하나씩 고치고 다시 검사(고쳐지지 않으면 멈춤). 창에 포커스가 오면 다시 검사.
- 검사 항목: 관리자·생성기(풀)·디렉터·진행도(안전지대)·화면 암전 / 게임 진행(기체, 갑판 위치 수, Briefing·Hangar·Spectate 지점, 기체 원래 위치가 갑판 근처인지, 장소 이동 버튼 Target) / HUD(크로스헤어)·작전 콘솔·격납고(옛 버전 여부)·작전판·작전 테이블(HomeBase)·HUD 설정 패널 / 플레이어 기체(ACO 미사일, 특수무장 슬롯, 강화 확장과 등록, 탄체·TargetLayers·AGM MissileSplash).

### 6-19. 무전 채터 ✅ (`ACO_Chatter` — 예전 무전 자막 `ACO_Radio` + 페이즈 음성 `ACO_MissionVoice`를 합침, `ACO → Create Chatter (Subtitles + Voice)`, 로컬 전용, 네트워크 없음)

- **하나로 합침**: 자막·음성·PTT 소리가 대기열 하나로 차례로 나온다(PTT 누름 → 음성 → PTT 놓음, 자막은 음성이 끝날 때까지 — 겹치지 않는다). 대사 두 종류: **페이즈 대사**(유형 1~10별 `StartTexts`/`SuccessTexts`/`FailTexts` + `*Clips`, 공통 `Default*Text/Clip`, `MissionCompleteText/Clip`, 실패 사유별 `MissionFailedTexts/Clips`(0 시간 초과, 1 도시 폭격, 2 호위 대상, 3 콘보이, 4 시한 표적) — 확률 없이 늘 나오고 `PhaseQueueDelay` 10초까지 기다린다, 화자 `PhaseSpeaker`), **상황 대사**(대사 표 `LineEvent/LineSpeaker/LineText/LineClip` — 미션 시작·증원·격추·빗나감·플레어·미사일 경보·피격·호위 대상, 상황마다 3~5줄 기본, 확률·쿨타임). 화자 4명(SKY EYE, ENEMY, TRANSPORT, WINGMAN). 음성은 칸만 비워 두었다(나중에 같은 칸에 넣으면 그 대사의 음성이 된다 — 자막은 음성 대사와 같게 고쳐 적는다). 이름을 바꿨지만 스크립트 GUID는 그대로라 씬의 컴포넌트·값이 이어진다(다른 스크립트의 `Radio` 필드는 `Chatter`로, 예전 이름을 이어받음). Bake: 대사 표가 예전 무전 기본값 그대로면 새 기본 대사로 바꾼다. **PTT 소리는 채널별**: 화자마다 `SpeakerChannels`(0 관제기 — SKY EYE, 1 아군 — TRANSPORT·WINGMAN, 2 적군 — ENEMY), 채널별 `ControlPttStart/End`(예전 PttStart/End를 이어받음)·`AllyPttStart/End`·`EnemyPttStart/End` — 아군·적군 칸이 비면 관제기 소리. 음성이 있는 대사만 PTT. 슈퍼에이스 대사는 적군 채널(`Say(…, CH_ENEMY)`). 자막 위치(좌우/상하)·크기는 HUD 설정 패널(SUBTITLE 3줄, 거리는 점수 패널과 같다, 키 `chatter_x/y/scale`).

- 상황(`EV_*`)이 생기면 그 상황의 대사 중 하나를 확률(`EventChance`)·쿨타임(`EventCooldown`)에 따라 골라(직전 대사는 피함) 머리를 따라오는 자막 `[화자] 대사`(화자별 색)로 띄우고, 음성 클립이 있으면 함께 튼다. `{name}`은 상황마다 넘긴 이름으로 바뀐다. 겹치면 차례로(대기열 6), `MaxQueueDelay`(6초)보다 밀린 대사는 버린다. 표시 시간 = max(2.5초, 글자 수 × 0.05초) 또는 음성 길이 + 0.3초.
- 대사 = 같은 순서의 배열 `LineEvent`/`LineSpeaker`/`LineText`/`LineClip`(인스펙터). 화자 `SpeakerNames`/`SpeakerColors`(기본 SKY EYE 녹색, ENEMY 빨강, TRANSPORT 파랑). 기본 대사는 영어 — 기본 TMP 폰트(LiberationSans)에 한글이 없어서다. 한국어는 한글 TMP 폰트 에셋을 자막 글자에 넣으면 된다.
- 상황과 출처(모두 각 클라이언트가 스스로 안다):

  | 상황 | 출처 | 기본 확률 / 쿨타임 |
  |---|---|---|
  | 미션 시작·페이즈 시작/성공/실패·증원·미션 완료/실패 | 관리자 상태 폴링(처음 본 상태는 기준만) | 100% (증원 10초) |
  | 내 격추 / 아군 격추(`{name}`) | 작전판 격추 판정(`LastHitByPlayer`) | 50% 6초 / 30% 10초 |
  | 내 미사일 빗나감 / 플레어에 속음 | 쏜 사람 화면의 `ACO_Missile`(락온해 쏜 플레이어 미사일, 범위 피해 미사일은 빗나감 제외, 한 발에 한 번) | 50% 8초 / 70% 8초 |
  | 가까운 적기 플레어 | NPC `NetFlare`(내 머리에서 `FlareCalloutRange` 3km 안) | 35% 12초 |
  | 미사일 경보 | 나를 노린 NPC·SAM 미사일 발사(`_targetEntity.Piloting`) | 60% 6초 |
  | 내가 격추됨 / 아군 격추됨(`{name}`) | 기체 강화 확장 `SFEXT_G_Explode` | 100% / 90% 3초 |
  | 호위 대상 피격 / 격추 | 생성기 호위 유닛 체력 폴링 | 50% 15초 / 100% |

- 잡담(격추 이후 상황)은 미션 중에만. Bake가 관리자·생성기와 알리는 쪽(작전판, `ACO_DFUNC_Missile`, `ACO_MissileLauncher`, NPC 전투기, 강화 확장)에 무전을 넣는다. 페이즈 음성(`ACO_MissionVoice`)과 함께 쓰면 페이즈 대사에 음성이 겹칠 수 있다 — 음성은 한쪽에만 넣는다.
- 보스(네임드 에이스) 대사는 보스를 만들 때 상황을 더한다(오너만 아는 상황은 짧은 네트워크 이벤트로 대사 번호를 알린다).

### 6-20. RWR — 위협 방향/거리 링 + 경보음 ✅ (`ACO_RWR`, Bake가 표적 마커 아래에 만든다, 로컬 전용, 네트워크 없음)
- **미사일**: 크로스헤어 둘레에 오는 방향으로 쉐브론(꼭짓점이 가운데). 방향 = 머리(시선) 기준 수평 방위(위 = 보는 쪽, 아래 = 뒤). 거리 = 반경 — `MaxRange`(10km) 이상 바깥 반경 `OuterAngle`, `CloseRange`(300m) 이하 안쪽 반경 `InnerAngle`(크로스헤어 바로 바깥), 사이는 로그 눈금(가까울수록 빨리 다가온다). 가까울수록 빨갛고(`UseColor`) `CloseScale`배까지 커지며, `BlinkRange`(1.5km) 안이면 경보음 박자에 맞춰 깜빡인다. 가까운 순으로 `MaxShown`(4)발.
- **락온**: 락온 중인 적 방향 바깥(`LockArcAngle`)에 얇은 호. 런처 경보(1초 간격)가 끊기고 `LockHoldTime` 뒤 사라진다. 새 락온이면 그 방향에서 락온음 한 번(SAV 기체 자체 락온음은 따로 난다).
- **경보음**: 가장 가까운 미사일 방향(머리에서 1m)에서 3D로. 간격 = 멀면 `MaxBeepInterval`(1초) → 가까우면 `MinBeepInterval`(0.1초). 클립은 Generated의 사인파 WAV(AudioSource 클립만 바꾸면 된다).
- **데이터**: 미사일은 모든 클라이언트가 로컬로 재현하므로 위치를 그대로 읽는다. `ACO_Missile`이 경고를 켤 때(표적 기체에 이 클라이언트가 탐) `AddMissile`, 끌 때 `RemoveMissile`. `ACO_MissileLauncher.LockWarningEvent`가 피해자 클라이언트에서 `ReportLock`. 터짐·회수·하차는 매 프레임 정리(안전장치).
- **비용**: 위협이 없으면 PostLateUpdate가 바로 돌아간다. 있을 때도 최대 8발 + 락온 6개의 거리·각도 계산과 Transform 몇 개 — 표적 마커(64개)보다 훨씬 가볍다. 쉐브론 머티리얼은 시작할 때 8개만 복제한다.
- **HUD 설정**(RWR 카드): `SHOW`(켜기/끄기 — 링과 경보음 모두), `RING SIZE`(링 반경 배율 0.5~2 — 미사일이 놓이는 안쪽·바깥 반경과 락온 호 반경, `SizeScale`), `MISSILE MARK SIZE`(쉐브론 크기 0.5~2.5, `ChevronScale`), `LOCK ARC SIZE`(락온 호 반경 0.9~2, `LockArcScale` — 바깥 반경 + 0.8° 안으로는 안 들어옴). 저장 키 `aco_hud_rwr`, `aco_hud_rwr_size`, `aco_hud_rwr_missile_size`, `aco_hud_rwr_lock_size`. 항목이 늘면 HUD 설정 패널 프리팹을 다시 만들어야 새 줄이 보인다(예전 프리팹도 오류 없이 동작, 새 항목만 안 보임).
- **색**: 먼 미사일 `FarColor` = HUD 초록 → 가까울수록 `CloseColor` 빨강(`UseColor`), 락온 호 `LockColor` 초록(호마다 머티리얼 복사본에 매번 넣는다). 예전 기본색(노랑)인 RWR은 Bake가 초록으로 바꾼다(직접 바꾼 색은 그대로).
- **HUD 설정 패널 배치**: 가로 2.8 × 1.25m, 3열 분류 카드 — SCORE PANEL(좌우·상하·거리·크기) / RESULT·NOTICE TEXT(좌우·상하·크기) | RADIO SUBTITLES(좌우·상하·크기) / TARGET MARKERS(테두리 크기·정보 각도) + RESET ALL | CROSSHAIR(켜기·크기·거리(+RWR)) / RWR(켜기·링·미사일 표시·락온 호). 카드 제목은 노랑, 줄 이름은 카드 안에서 짧게.
- 연결: Bake(`BakeRwr`)가 씬의 RWR을 모든 `ACO_MissileLauncher`·`ACO_DFUNC_Missile`·`ACO_HudSettings`에 넣는다.
- 나중에: 적외선 미사일 늦게 감지(MAWS 거리), RWR 성능 강화(감지 거리·오차) — 미사일 종류는 이미 알고 있어 `AddMissile`에 넘기기만 하면 된다.

### 6-23. 패널 버튼 상태 표시 / 효과음 ✅ (`ACO_UiSound`, Bake가 씬에 하나 만들고 연결, 로컬 전용)
- **회색 = 지금 누를 수 없음**(그래도 누르면 이유를 안내하고 거절음). 게임 진행이 0.5초마다 `ACO_FlowButton.SetState(글자, 가능)`·`ACO_MissionStartButton.SetState(가능, 선택)`로 칠한다(`Buttons`/`PhaseButtons`는 Bake가 모은다, 배경 Image도 채운다).
  - JOIN/LEAVE: 비행 중 LEAVE 불가, 명단이 차면 `ROSTER FULL`. READY: 명단·미션 전·모두 착륙·브리핑 준비 완료일 때(준비 중이면 `PREPARING...`, 준비했으면 `CANCEL READY`). LAUNCH: 미션 중 내 기체가 쉬고 있을 때(`IN FLIGHT`/`RESPAWNING`/`IN USE`), 명단 밖이면 `SPECTATE`.
  - **REROLL: 쿨타임 중엔 `REROLL (23s)`처럼 남은 시간**, 지형을 세우는 중 `GENERATING...`, 출격 카운트다운 중 `TERRAIN LOCKED`.
  - 페이즈 버튼: 고른 수는 밝은 녹색, 미션 중·시작 중·출격 카운트다운 중엔 회색.
  - 격납고: 크레딧이 모자라면 BUY 버튼 회색(이미 있던 강화 줄 회색·잠긴 무장 색과 같은 규칙).
- **효과음**(누른 버튼 자리에서 3D): CLICK = 받아들인 일반 조작(탭·페이지·잠긴 무장 보기·값 조정·리롤·페이즈·장소 이동), CONFIRM = 결정(JOIN/LEAVE·READY·LAUNCH·구매·장착·HUD RESET), DENY = 거절(조건이 안 맞아 안내만 뜸, 크레딧 부족, 최대 단계, 값이 끝에 닿음). 클립은 Generated/`ACO_UiClick`·`ACO_UiConfirm`·`ACO_UiDeny`.wav(사인파 — 클립만 바꿔 끼우면 된다). 결과는 각 동작이 돌려준다(`ACO_GameFlow.ButtonAction/RequestPhases` → bool, `ACO_Loadout.Select/BuyPreview/ChangePage`·`ACO_Shop.BuyRow` → 효과음 종류, `ACO_HudSettings.Adjust` → 바뀌었나).

### 6-30. 레이더 — HUD 레이더 / 조종석 지도 (로컬 전용, 네트워크 없음)

- **접촉 목록**(`ACO_RadarContacts`, 표적 마커 아래 `RADAR`, 씬에 하나): 생성기 풀(전투기·지상·고가치 표적·호위 대상) + 참가자 기체를 처음에 한 번 훑고, 0.5초마다 배치·생존·종류(적/목표/아군 × 공중/지상 — 지상 풀 헬리콥터는 공중)를 다시 본다. 위치는 그리는 쪽이 트랜스폼을 바로 읽는다. NPC 미사일은 런처(`ACO_MissileLauncher.Radar`, Bake 연결)가 쏠 때 `AddMissile` — 날고 있는 동안만(`ACO_Missile.IsFlying`). `LocalVehicle` = 내가 탄 참가자 기체(`SaccEntity.InVehicle`). 아무 기체에도 안 탔고 미리보기도 아니면 쉰다.
- **HUD 레이더**(`ACO_HudRadar`): 기체에 탔을 때 시야 왼쪽 아래(`HeadOffset`, 점수 패널과 같은 거리)에 원형 스코프. 반경 `Range`(표시 거리 — HUD 설정 RANGE로 2~10km, 1km 단위, 최대 `MaxRange` 10km) 안의 **적(노랑)·목표(빨강)**·아군(파랑)·적 미사일(흰색 — 진행 방향으로 긴 막대. 예전 주황은 노랑 적과 헷갈려 바꿨다, Bake가 예전 주황만 바꾼다), 나(흰 쉐브론). 항공기는 진행 방향 **쉐브론**(뒤가 V로 파인 화살촉 — 삼각형은 앞·옆이 헷갈려서 바꿨다, 정점 4개라 점 한 칸 그대로), 지상·함선은 사각형. 점 크기 `BlipSize` 스코프 지름의 4.5%(예전 7% — 10km 스코프에서 점 하나가 1.4km라 편대·지상 유닛이 뭉쳐 보였다), 미사일 막대 길이 `MissileSize` 3.5%(폭은 30%), HUD 설정 BLIP SIZE로 ×0.5~2(`BlipScale`, 북쪽 표시는 그대로). 색을 바꾼 건 목표가 가장 눈에 띄게(빨강) — Bake가 예전 기본값(적 빨강·목표 노랑, 점 7%·미사일 5%)만 바꾸고, 조종석 지도(`ACO_CockpitMap`) 색도 같이 바꾼다. **바라보는 방향이 위** — 점은 북쪽 기준으로 그리고 판(Spin)을 머리 방위만큼 매 프레임 돌린다(테두리 N 표시 = 북쪽, `HeadingFromVehicle`이면 기체 기수). 점 메시는 0.1초마다 정점만 다시 채운다(오브젝트 수 고정). 항상 맨 위(ZTest Always).
  - HUD 설정 패널 `RADAR` 카드(항목 19~23, 저장 ID `radar`/`radar_size`/`radar_x`/`radar_y`/`radar_alpha`): 켜기, 크기, 좌우, 상하, 불투명도(20~100% — 배경·고리·점 머티리얼 알파에 곱한다, `Renderers`). 패널 판이 2.8m × 1.75m로 커졌다(프리팹 다시 만들기).
  - **HUD 따라오는 지연**(SCORE PANEL 카드 FOLLOW DELAY, 항목 24, 저장 ID `hud_follow`): 0~0.5초(0 = 바로 붙음). 점수 패널·결과 글자·무전 자막·HUD 레이더의 `FollowSharpness` = 1 ÷ 지연. 기본 = 점수 패널 값(10 → 0.1초).
- **조종석 지도**(`ACO_CockpitMap`, 기체마다, 메뉴 `ACO → Player Vehicle → Add Cockpit Map to Selected Vehicles`): **북쪽이 위인 고정 방위**로 공역 전체 — 바다 바탕 + 육각 지형 윗면(높이대 색, 처음 탔을 때·지형이 바뀔 때 프레임마다 나눠 만든다) + 공역 테두리 + 적·목표·아군 + 나. 내가 이 기체에 타고 있을 때만 화면(Screen)을 켜고 0.25초마다 갱신한다(다른 기체 화면은 꺼짐 — 테두리만). 깊이 검사(조종석에 가려진다), 겹침 순서는 렌더 큐. 위치는 조종석 의자 앞 대략 자리에 만들어지니 계기판 자리로 옮긴다.
- 셰이더 `ACO/RadarVertexColor`(정점 색 × _Color, `_ZTest`), 메시·머티리얼 `Generated/ACO_Radar*`.

### 6-46. 디버그 패널 (테스트용)

- **프리팹** `ACO_DebugPanel`(프리팹 생성 창 → 디버그 패널): 월드 UI 판 1.8 x 2.0m, 버튼은 `ACO_DebugButton`(Action·Index·Amount를 패널에 넘긴다). Bake가 미션 관리자·생성기·진행도와 버튼 효과음을 연결한다.
  - **미션 가중치**: 미션 관리자 `Modules`마다 한 줄 — 이름(보너스 전용은 [BONUS], 꺼진 모듈은 [OFF]) · W(가중치) · %(같은 무리 — 일반·보너스 — 안 켜진 모듈 가중치 합에 대한 몫). −/+ = ±0.5(0~20). 0이면 그 종류가 안 나온다. 줄은 14개(남는 줄은 숨긴다).
  - **출현 확률**: 에이스(생성기 `AceChance`) ±5%, 베테랑(`VeteranChance`) ±5%, 보너스 페이즈(슈퍼에이스 등 — 관리자 `BonusPhaseChance`) ±10%.
  - 가중치·확률은 페이즈를 고르는 관리자 오너·적기를 놓는 생성기 오너가 써야 하므로 **네트워크 이벤트로 모든 클라이언트에** 넣는다(`NetSetWeight`/`NetSetAce`/`NetSetVeteran`/`NetSetBonus`). 늦게 들어온 사람은 인스펙터 값 — 디버그용이라 동기화 변수로 남기지 않는다.
  - **내 크레딧**: −100K/−10K/+10K/+100K(0 아래로는 안 간다). 로컬 — 진행도 `DebugSetCredits`로 넣어 저장까지 된다.
  - **날씨**: < > = AUTO(씨앗으로) → 맑음 → 구름 → 안개 → 비. 날씨 `ForcedWeather`를 모든 클라이언트에 넣는다(날씨가 1초 안에 알아채고 전환). 늦게 들어온 사람은 AUTO — 화면 연출뿐이라 괜찮다.
  - **지형**: < > = RANDOM / 프리셋 12종(큰 섬 … 분지) 고르기, GENERATE TERRAIN = 만들기. 프리셋을 강제(`ForcedPreset` — 동기화 안 됨)하면 늦게 들어온 사람만 다른 지형이 되므로, **그 프리셋이 자연스럽게 나오는 씨앗**을 찾아(`ACO_HexTerrain.PresetForSeed`, 최대 4000번) 그 씨앗으로 만든다 — 씨앗만 동기화돼 모두 같은 지형. 게임 진행의 리롤과 같은 흐름(`ACO_GameFlow.DebugRequestTerrain` → 오너 `NetDebugTerrain` — 새 지형 + 브리핑 미리 배치, 준비·카운트다운 해제)이고 명단·리롤 대기 시간만 보지 않는다. 미션 중·출격 카운트다운·기체 탑승 중·지형 생성 중이면 막힌다. 가중치 0인 프리셋은 못 만든다.
- **권한**: `AllowedPlayers`(표시 이름) — 비우면 누구나 누를 수 있다(Bake가 경고). 공개 월드에 올릴 땐 이름을 넣거나 패널을 끈다.

### 6-47. HUD 오버레이 = UI 레이어 (사진에 안 찍히게)

- VRChat 문서: UI 레이어(5)는 VRChat 카메라(사진·스트림)에 찍히지 않는다(카메라에서 "UI"를 켜야 찍힌다). 대신 **VRChat 레이저 포인터가 무시**한다(VRChat 메뉴를 열었을 때만 누른다) — 누르는 패널은 옮기면 안 된다.
- Bake(`BakeUiLayers`)가 머리·시야에 붙는 HUD 오버레이를 UI 레이어로 옮긴다: 점수 HUD·표적 마커(ACO_HUD), HUD 레이더, RWR, 무전 자막 패널, 화면 페이드 구, 물속 물빛 구. 그 안에서도 누르는 캔버스(VRCUiShape) 아래·버튼·콜라이더가 붙은 오브젝트는 건너뛴다.
- 옮기지 않는 것: 격납고·작전 콘솔·HUD 설정·디버그 패널(누르는 패널), 작전판·전술 지도(월드에 놓인 판 — 사진에 나와도 자연스럽다), 기체 조종석 HUD(SAV 것 — CCIP·슬롯 막대 포함).

### 6-49. 관전 패널 — 에이스컴뱃 리플레이식 실시간 관전 (`ACO_SpectatorCam`)

- 진짜 리플레이(녹화·재생)는 기체·NPC·미사일 수십 개 기록 + 재생용 유령 복제본이 필요해 1단계는 **실시간 관전 + 리플레이식 카메라 연출**. 기체·NPC는 이미 동기화돼 있어 **새 동기화 없음**(로컬 전용 — 각자 자기 스크린을 자기가 연출).
- **프리팹** `ACO_SpectatorPanel`(프리팹 생성 창 → 관전 패널): 스크린 3.2 x 1.8m(렌더 텍스처 `Generated/ACO_SpectatorRT` 1280x720, MSAA 2) + 아래 조작 줄 + 관전 카메라(꺼진 채) + 카메라 자식 하늘 안개 돔. 스크린 위에 글자(UI)는 띄우지 않는다.
  - **POWER**: 거리와 상관없이 직접 켜고 끈다 — 조종사가 VRChat 카메라를 스크린 앞에 두고(스트림 카메라 등) 날면서 자기 비행을 녹화할 수 있게. 끄면 카메라를 꺼 비용 0, 스크린은 어두운 판. 비행 중인 조종사가 없어도 카메라를 끈다.
  - **HD / FHD**: 렌더 텍스처 두 장(`Generated/ACO_SpectatorRT` 1280x720 / `ACO_SpectatorRT_FHD` 1920x1080, MSAA 2) 중 하나를 카메라·스크린·스트림 카메라 전체 화면이 같이 쓴다(`TextureHD/FHD`, 처음 값 `StartFHD`). Udon은 실행 중에 렌더 텍스처를 만들 수 없어 미리 두 장. FHD는 GPU 부하가 약 2.25배.
  - **스트림 카메라 전체 화면**(`StreamCameraFullscreen`, 셰이더 `ACO/SpectatorFullscreen`, `Generated/ACO_SpectatorFullscreen.mat`): VRChat 손 카메라(스트림·사진 — 전역 `_VRChatCameraMode` 1·2·3)가 스크린 가운데에서 `_Radius`(4m) 안에 오면 **그 카메라 화면 전체를 관전 화면으로** 덮는다 — 녹화 화면이 곧 관전 화면. 내 눈(모드 0)·거울(`_VRChatMirrorMode`)엔 안 보인다. 정점 셰이더가 쿼드를 클립 공간 화면 전체로 펼치고(아니면 한 점으로 접는다), 메시 경계를 100km로 구워 카메라가 어느 쪽을 봐도 잘리지 않는다. 화면 비율이 다르면 검은 띠(늘이지 않는다). FX 레이어라 관전 카메라 자신은 안 찍는다. 패널이 켜져 있고 찍는 중일 때만 켠다. 에디터 테스트는 머티리얼 `Any camera`를 켠다(에디터에선 모드가 늘 0).
  - **TARGET < >**: AUTO → 조종사 1 → 2 … (비행 중인 조종사만, 플레이어 기체만 — Bake가 `ACO_VehicleUpgrades`가 있는 SaccEntity를 `Vehicles`에). AUTO = 감독: 미사일을 쏜 조종사로 옮기고(미사일·표적 컷 중엔 그대로), 조용하면 `AutoTargetHold`(25초)마다 다음 조종사. 직접 고른 조종사가 내리면 화면이 꺼진 판(“PILOT NOT FLYING”).
  - **SHOT < >**: AUTO → CHASE → FLYBY → TELEPHOTO → SIDE → WINGMAN → COCKPIT → PILOT → MISSILE → TARGET. 보통은 대상은 직접, 컷은 AUTO.
- **컷**(카메라는 지면·해수면 위 `MinGroundClearance` 4m를 지킨다, 컷 사이는 바로 잘라 넘긴다):
  - CHASE: 뒤 32m·위 7m를 기체 자세를 늦게 따라가며(자세 3/초 스무딩, 좌우로 살짝 흔들림), 기수 앞 30m를 본다. 기체 롤도 조금 따라간다.
  - FLYBY: 2.5초 뒤 기체가 올 자리 옆 25~70m(위아래 −5~+20m)에 카메라를 **세워 두고** 지나가는 기체를 눈으로 쫓는다 — 지나가 350m 멀어지거나 5초면 끝(직접 고른 경우 다시 세운다).
  - TELEPHOTO: 옆 0.7~1.4km(위 30~220m)에서 기체와 거의 같이 움직이며, 화면 가로에 45m가 담기게 화각을 줄인다(2~30°) + 손떨림(Perlin).
  - SIDE: 옆 45m에서 앞쪽 55° → 뒤쪽 125°로 돌며 나란히(기체에 붙어 같이 꺾인다).
  - WINGMAN(편대원 시점): 컷이 바뀐 순간의 기체 **속도·방향을 그대로 고정해 직진**하며 옆 30~60m의 기체를 찍는다 — 기체가 선회·상승하면 화면 안에서 흘러가고 멀어진다(편대원이 나란히 날다 대장이 기동하는 느낌). 편대원 기체처럼 위아래·좌우로 몇 m 흔들린다. 내려가며 직진하면 땅·바다 위에서 멈춘다. 기체가 600m 넘게 멀어지면 끝(직접 고른 경우 다시 옆에 붙는다).
  - COCKPIT: 기체 기준 `CockpitOffset`(0, 2.4, −9) — 조종사 어깨 뒤에서 앞을 본다.
  - PILOT: 조종사(기체 오너) 아바타 머리 뼈(없으면 머리 추적) 기준 기체 축 `PilotCamOffset`(±0.45, 0.2, 1.3 — 좌우는 컷마다 무작위) 자리에서 얼굴을 본다, 화각 `PilotFov` 48. 기체에 붙어 같이 움직이고 지면 보정 없음. 머리를 못 찾거나 기체에서 20m 넘게 떨어져 있으면 COCKPIT 자리로 대신. 캐노피 프레임에 가리면 오프셋 조정.
  - MISSILE: 대상이 쏜 미사일 뒤 8m에서 표적을 본다(표적이 없으면 미사일 앞).
  - TARGET: 미사일의 표적 옆 60~120m(미사일 방향에 수직)에서 표적과 거의 같이 움직이며 날아드는 미사일 → 폭발 자리를 본다.
- **감독(AUTO 컷)**: 인스펙터 가중치(`Weight*` — 기본 추적 3 · 플라이바이 3 · 망원 2 · 나란히 2 · 편대원 2 · 조종석 1 · 조종사 1.5, 0이면 안 나옴) 비율로 같은 컷 연속 없이(바로 앞 컷을 빼고 고른다 — 가중치 있는 컷이 하나뿐이면 그대로) 4~7초마다. 30m/s 아래(지상)는 `SlowWeight*`(추적 3 · 조종석 1 · 조종사 1.5)만. 대상이 미사일을 쏘면 `MissileCutChance`(75%, 미사일 컷이 끝나고 4초 안엔 안 감)로 MISSILE → 명중까지 1.1초 남으면 TARGET → 터진 뒤 3초 → 다음 컷. 표적에서 300m 넘게 떨어져 끝나면(CIWS 요격·플레어·회피) TARGET 없이 그 자리에서 `MissHold`(1초) 동안 터진 곳을 보고 다음 컷. TARGET은 최대 15초.
  - 직접 고른 MISSILE·TARGET은 미사일이 없을 때 추적으로 대신하며 기다리고(“WAITING FOR A MISSILE”), 쏘면 그 미사일로 — 터진 자리를 3초 본 뒤 다시 기다린다.
- **미사일 알림**: 플레이어 무장(`ACO_DFUNC_Missile`)이 발사할 때(모든 클라이언트 — 발사 이벤트가 모두에게 온다) `Spectator.NotifyMissile` — 패널이 꺼져 있으면 바로 돌아온다. Bake가 무장에 첫 관전 패널을 넣는다. 미사일은 `ACO_Missile.IsFlying`·`GetTargetTransform`으로 본다. 폭탄·로켓(ACO_Ordnance)은 아직 안 본다.
- **대상 움직임**: 원격 기체는 리지드바디 속도를 믿을 수 없어 위치 차로 잰다(0.5km 넘게 튀면 순간이동으로 보고 새로 잰다). 카메라는 `PostLateUpdate`(기체가 다 움직인 뒤).
- **그리지 않는 것**: UI(5 — HUD·마커), PlayerLocal(10 — 내 1인칭 아바타, 대신 18의 거울용 전신), UiMenu(12), InternalUI(19), **FX 레이어**(`ACO_SceneBaker.FxLayer` = 28) — 내 머리를 따라다니는 화면 효과(하늘 안개 돔·물안개 막·비·물속 물빛 벽)를 Bake가 옮긴다. 사용자 레이어가 모두 이름이 붙어 있어 SAV 경주용 Racing(28)을 같이 쓴다(효과엔 콜라이더가 없어 경주 판정과 무관, 메인·VRChat 카메라엔 그대로 보인다). 관전 카메라의 하늘 안개는 카메라 자식 돔(레이어 18 MirrorReflection — VRChat 메인 카메라는 그리지 않는다, Bake가 하늘 안개 돔 머티리얼을 넣는다)이 따로 맡는다.
- 한계: 소리 없음(오디오 리스너는 플레이어에게). 씬 안개는 전역이라 내가 구름·물속이면 관전 화면도 짙어진다(관전은 보통 로비라 괜찮다). 카메라를 켜면 장면을 한 번 더 그려 GPU 부하가 크다 — 패널은 하나만 권장(여러 개면 Bake가 경고, 미사일 알림은 첫 패널만).
- 다음 단계(설계만): 개인용 스크린, 마지막 격추 다시 보기(기체·표적·미사일 3개만 10Hz 기록 + 단순 유령 모델).

### 6-51. 비행 HUD — SAV HUD 대체 (`ACO_FlightHud`)

- 왜: SAV HUD는 멀리 띄운 판을 캐노피 스텐실로 보는 방식이라 각도에 따라 잘리고, 헤딩이 원형이며 쓸데없는 정보(마하·대지속도 등)가 많다.
- **기체 앞 고정(시준)**: 루트를 매 프레임(PostLateUpdate) 내 머리 위치 + 기체 자세에 두고 `Distance`(20m) 앞 판(Canvas)에 그린다. 판의 1단위 = 시야각 1° × 크기(`BaseScale` 0.5 × `Scale` — HUD 설정 FLIGHT HUD SIZE 0.6~1.6, 예전 ×1.0이 너무 커서 기본을 절반으로). 글자는 빌더에서 × `FlightTextScale` 1.5(배치도 키운 글자에 맞췄다). 글꼴 **Electronic Highway Sign**(TMP 예제 `Examples & Extras/.../Electronic Highway Sign SDF` — 없으면 LiberationSans), 머티리얼 `Generated/ACO_FlightHudTextOverlay.mat`(그 글꼴 아틀라스 + `TextMeshPro/Distance Field Overlay` — 글꼴 기본 머티리얼은 깊이 검사를 해서 조종석 구조물에 가려진다). 예제 글꼴은 동적 아틀라스(글자 20개, 빌드 때 비움)라 빌더가 출력 가능한 ASCII를 넣고 정적으로 굳힌다. 속도 벡터·기총 피퍼는 실제 방향을 판에 **접평면 투영**(기체 기준 x/z, y/z)해 바깥 풍경과 정확히 겹친다. **피치 래더**는 같은 투영 자리를 가운데에서의 각도로 바꿔 1° = `LadderScale`(0.5) 판 단위로 놓는다 — HUD 크기와 상관없이 10°마다 5단위(창 ±10.5에 약 ±21°)로 좁고, 기수를 들어도 천천히 움직인다(실제 수평선과는 안 겹친다 — 겹치게 하려면 1 / (BaseScale × Scale)). 선은 `ACO/MarkerOverlay`(맨 위, 스텐실 없음), 글자는 TMP 오버레이, 모두 UI 레이어(사진에 안 찍힌다). 조종 중일 때만(`Contacts.LocalVehicle.Piloting`).
- **배치**(단위 = °): 위 헤딩 테이프(y 12, 5° 눈금 13개, 10°마다 두 자리 숫자, 가운데 상자에 세 자리 방위) / 왼쪽 속도 테이프(x −13, 10kt 눈금, 50kt마다 숫자, 상자에 지금 속도 — **kt**) + 그 옆 **스로틀**(x −19.6, 높이 8 — 채움 = 엔진 출력, 선 = 애프터버너 시작점, 아래 `THR %`/`AB`) / 오른쪽 고도 테이프(x 13, 100ft 눈금, 500ft마다 숫자, 상자에 지금 고도 — **ft**, 해수면 = SAV `SeaLevel`) + 전파고도 `R ft`(2500ft 아래 — 0.1초마다 아래로 레이캐스트, 바다는 트리거라 트리거도 맞힌다) / 피치 래더(−90~+90°, 10°마다 — ±90 = 천정·천저, 루프로 넘어갈 때 빈 구간을 메운다 — **숫자는 래더 양쪽 끝**, 위 = 실선 + 끝이 아래로 꺾임, 아래 = 점선 + 끝이 위로, 0° = 긴 수평선, 가운데 창(±11 × ±10.5) 밖 칸은 숨긴다) / 속도 벡터(원 + 양 날개 + 위 꼬리 — SAV HUD와 같은 꼴, 창 밖이면 가장자리에 붙는다). **래더 기준 방위** = 기수의 수평 성분 — 수직 가까이(|기수 y| 0.94 → 0.995, 약 70° → 84°)에선 기수의 수평 성분이 0에 가까워 조금만 흔들려도 방위가 빙글 돌므로 기체 위쪽을 빼(fwd − up × fwd.y) 배 쪽 방위로 옮긴다 — 수직에서 머물거나 롤해도 래더가 돌지 않고, 루프 꼭대기를 넘으면 약 73°에서 한 번 뒤집힌다.
  - 왼쪽 아래: G, 받음각(랜딩기어를 내렸을 때만), 상태(GEAR UP/DN · FLAP ON/OFF · HOOK ON/OFF — 켜진 것은 밝게, 꺼진 것은 흐리게, 에어브레이크를 펴면(공중 + 브레이크 입력) **AIRBRAKE** 깜빡임).
  - 오른쪽 아래: 잔탄 **한 줄씩** — `GUN`(남은 초 × 초당 발사 수 = 탄 수), `MSL`(ACO 기본 미사일), `SP 무장ID 탄`(특수무장 슬롯 — SAV 모듈이면 `-`), `FLR` — 고른 무장 앞에 `>`. 그 아래 `HP %` + 막대.
  - 오른쪽 아래, 잔탄 글자 왼쪽: 고른 ACO 무장(기본 미사일·특수무장 ACO 모듈)의 **장전 슬롯 막대**(최대 16개 — 바뀜: 8개였어서 MMP 16슬롯 중 8개만 보였다. 슬롯이 `SlotRowMax` 8개를 넘으면 2행으로 나눠 위 줄에 앞 슬롯 절반, 아래 줄(`SlotRowSpacing` 3 아래)에 나머지 — MMP는 2행 8열. 프리팹 판 2(`ACO_FlightHud`). 그 밖은 예전 그대로: 세로 0.34 × 2.6의 얇은 막대를 `SlotSpacing` 0.55 간격으로 잔탄 글자 바로 왼쪽부터 왼쪽으로 늘어놓고, 아래 → 위로 찬다). 잔탄은 세 칸(고른 무장 쉐브론 `>` | 이름 GUN·MSL·특수무장 ID·FLR | 수 — 오른쪽 맞춤)으로 줄을 맞춘다, 글자 10(약 1°), HP 9. 기총 수 = 남은 초 × 파티클 초당 발사 수. SAV HUD를 숨기면 예전 막대도 같이 사라진다.
- **조준 표시**(고른 무장 — 오른쪽 다이얼 먼저, 없으면 왼쪽): 기총 = 작은 십자 + **기총 피퍼**, 유도무기·폭탄(ACO 미사일, ACO 폭탄·유도 로켓, SAV AAM/AGM/폭탄) = **W**, 무유도 로켓(추진하는 탄체 + 락온 표시 없음 — `ACO_DFUNC_Missile.GetSightKind`) = 큰 **십자**. 아무것도 아니면 작은 십자.
- **기총 피퍼**: 0.25초마다 기수에서 12° 안·1.5km 안의 적 항공기(레이더 접촉 목록) 중 기수에 가장 가까운 것을 고르고, 위치 차로 속도를 잰다. 탄이 닿는 시간 t = 거리 / 월드 탄 속력(탄속 + 내 속도 × 물려받기), 그 시간 뒤 표적 자리로 한 번 고친다. **피퍼 = 지금 쏜 탄의 t초 뒤 자리(+ ½ g × 중력 배수 × t²) − 표적 속도 × t** — 적기를 피퍼에 겹치면 맞는다. 표적이 없으면 700m의 탄 자리. 표적 거리를 피퍼 옆에 적는다(거리 단위 설정). 탄속·중력·물려받기·발사 속도는 Bake가 SAV 기총 데미지 파티클(`GunDamageParticle`)에서 읽는다.
- **거리 단위**(`ACO_Units`, HUD 설정 FLIGHT HUD → DISTANCE UNIT: M / KM ↔ NM, 저장 키 `distance_unit`): 비행 HUD 피퍼 거리, 표적 마커 거리, 페이즈 진행 글자(콘보이·호위·요격·상륙전의 남은 거리 — `ACO_PhaseModule.Dist`). 속도·고도는 kt·ft 고정.
- **HUD 설정 FLIGHT HUD 카드**: SHOW(`flight_hud`), SIZE ×0.6~1.6(`flight_hud_size`), BRIGHTNESS 30~100%(`flight_hud_bright`), DISTANCE UNIT. 기존 ACO 크로스헤어(시선 고정)는 CROSSHAIR 카드에서 따로 켜고 끈다.
- **SAV HUD 숨기기**: Bake가 플레이어 기체의 `SAV_HUDController` **자식**(SF-2·FA-18의 `bigstuff` 등)을 끈다(끈 것은 `HiddenSavHudParts`에 적어 둔다). 크기를 줄이는 방식은 안 된다 — SAV HUD는 머리에서 멀리 띄운 큰 판(FA-18 bigstuff 크기 500×500×740)이라 부모를 줄이면 거리와 크기가 같이 줄어 보이는 크기가 그대로다(0으로 줄이면 SAV 속도 벡터 계산이 0 벡터가 돼 매 프레임 경고). 컨트롤러는 켠 채 크기 1로 둔다(무장 스크립트가 위치·`distance_from_head`를 읽는다 — 예전 Bake의 크기 0.001은 되돌린다). 꺼진 자식 안에서 기능이 켜고 끄는 HUD 요소도 안 보인다. `HideSavHud`를 끄고 Bake하면 Bake가 껐던 것만 다시 켠다. SAV 파일은 건드리지 않는다.
- 글자는 0.1초마다만 바꾼다(문자열을 줄인다), 눈금 숫자는 값이 바뀐 칸만. 위치는 매 프레임. 프리팹 생성 창 "비행 HUD"로 만들어 씬에 하나 둔다.

### 6-52. 작전판 스크롤

- 왜: 기록은 최근 16줄(`MaxRows`)만 보여 누가 누굴 잡았는지 밀리면 확인할 수 없었다(보관은 `MaxEntries` 200줄).
- 판 아래 띠에 버튼 셋(`ACO_LogScrollButton`, 콜라이더 Interact — VR 레이저·데스크톱 클릭): **NEWER**(최근 쪽으로 반 쪽 = `MaxRows`/2줄), **OLDER**(오래된 쪽으로 반 쪽), **LATEST**(맨 위 = 최신). 끝에 닿으면 거절음. 로컬 — 각자 자기 화면만.
- 기록은 최근 것이 위. 스크롤 중이면 맨 위에 `^ n NEWER (LATEST)`, 맨 아래에 `v n EARLIER`(200줄을 넘어 버린 줄이 있으면 `(n NOT KEPT)`). 지난 기록을 보는 동안 새 격추가 와도 보던 줄이 밀리지 않는다(새 줄만큼 함께 내려간다). 새 미션이면 맨 위로.
- 설치: 작전판 프리팹 생성이 월드 UI 버튼을 만든다(`ACO_LogScrollButton.Click`). Bake는 버튼 효과음만 연결한다.

### 6-53. TAC 네임(콜사인)

- 다른 플레이어가 탄 아군기 테두리 위 글자(예전 `WINGMAN n`)를 각자 정한 콜사인으로. 비어 있으면 예전처럼 `WINGMAN 칸 번호`.
- **저장·공유**: 플레이어 영구 저장(PlayerData) 고정 키 `aco_tac_name`(절대 바꾸지 않는다). PlayerData는 VRChat이 모든 플레이어에게 보내 주므로 동기화 코드가 없다 — 다른 화면은 `PlayerData.TryGetString(그 플레이어, 키)`로 읽는다. 다음 접속에도 남는다.
- **입력**: 프리팹 `ACO_TacNamePanel`(프리팹 생성 창 → TAC 네임 패널, 월드 UI 1.4 x 0.6m) — 입력 칸(UI InputField — VRChat 키보드, 입력이 끝나면 바로 저장) · SET · CLEAR, 지금 콜사인과 실제로 보이는 이름. 대문자로, 영문·숫자·공백·`-`만(기본 글꼴), 최대 12자. 저장 값을 불러오기 전엔 거절.
- **중복**: 인스턴스 안 같은 이름 중 플레이어 번호가 작은(먼저 들어온) 사람은 그대로, 다음부터 뒤에 2, 3… — 모든 화면이 같은 규칙이라 같은 결과. `ACO_TacName`이 1초마다 플레이어 목록·이름을 다시 읽어 표시 이름을 정해 두고(`GetLabel`), 표적 마커는 그 값을 쓴다(매 프레임 PlayerData를 읽지 않는다).
- Bake: 표적 마커 `TacNames` ← 씬의 `ACO_TacName`, 패널 효과음. 작전판·관전 패널의 조종사 이름은 아직 VRChat 이름.

### 6-54. 데칼(엠블럼) — URL 이미지 + 격납고 샘플 기체에 배치

- **이미지 = URL만**(VRChat 이미지 로딩 `VRCImageDownloader`): 스튜디오 URL 입력 칸(`VRCUrlInputField` — VRChat 키보드)에 붙여 넣고 LOAD. VRChat 제약: 스크립트가 문자열로 URL을 만들 수 없어 **URL은 저장 불가 — 접속마다 다시 넣는다**(바뀜 6-153: Player Object 저장소에 저장). 허용 호스트(Imgur·GitHub 등) 밖은 보는 사람이 "신뢰하지 않는 URL 허용"을 켜야 보인다. PNG·JPG, 최대 2048. 월드 전체 5초에 1장 — 스튜디오 하나가 **모든 데칼의 받기를 맡는다**(캐시 16칸 — 같은 URL은 한 번, 대기열을 `DownloadGap` 5.5초 간격으로). 실패하면 그 데칼은 끈다(스튜디오에 이유 표시).
- **이미지 2개**: LOAD는 지금 고른 데칼의 이미지를 바꾼다. 이미지를 넣지 않은 데칼은 다른 데칼의 이미지를 쓰고, MIRROR는 이미지도 복사한다. 실기체는 URL 두 개를 동기화한다(`_url`, `_url1`).
- **데칼 셰이더**(`ACO/Decal`, Unity 프로젝터 — 기체 UV와 상관없이 표면에 비춘다): 직교 정사각 틀 6m(데칼 최대 크기), 깊이 0.6m. 크기·좌우 뒤집기·켜기는 **머티리얼 값**(`_Size`·`_Flip`·`_Visible`)이라 Udon은 프로젝터 컴포넌트를 건드리지 않고 위치와 머티리얼만 바꾼다(프로젝터마다 머티리얼이 따로 — `Generated/Decals`). 프로젝터를 마주 보는 면만·비스듬할수록 흐리게(비추는 방향 = `unity_ProjectorClip` 첫 줄(깊이 축)과 노멀로 판정 — 처음엔 투영 행렬 z 부호로 판정해 부호가 어긋나면 모든 면이 버려져 데칼이 통째로 안 보였다), 깊이 끝에서 흐리게 → 날개 반대쪽·갑판엔 안 묻는다. 비추지 않는 레이어: 물·UI·PlayerLocal·UiMenu·MirrorReflection·InternalUI·FX. 비용: 비추는 범위의 기체 메시를 한 번 더 그린다(데칼 하나 = 기체 한 번).
- **배치(격납고 샘플 기체)**: 프리팹 `ACO_DecalStudio`(패널 + 데칼 건 + 미리보기 프로젝터 2개). 씬에서 `SampleRoot`(샘플 기체 기준점 — **실기체 SaccEntity 루트와 같은 자리·같은 모델**이어야 상대 자리가 그대로 옮겨진다)와 `DecalKey`(기종)를 넣고 Bake. 데칼 건(VRC Pickup, 로컬 도구 — 각자 자기 화면)을 들고 샘플을 겨누면 겨누는 선이 맞은 곳까지 늘어나고, **방아쇠를 누르는 순간** 맞은 표면에 고른 데칼이 바로 찍히고 저장된다(건의 위쪽 = 데칼 위쪽, 표면 노멀 방향으로 비춘다 — 겨누는 동안 따라다니는 미리보기는 없다. 플레이어 데이터를 받기 전 접속 직후엔 화면에만 찍힌다). 미리보기 프로젝터는 SampleRoot 기준 상대값을 월드 자리로 바꿔 놓는다(부모가 SampleRoot가 아니어도 샘플에 찍힌다). 놓으면 받침대로 돌아간다. 패널: DECAL 1/2(편집할 데칼), SIZE ±(×1.15, 0.2~6m), ROT ±(15°), FLIP(좌우), **MIRROR 1 > 2**(1을 샘플 기준 X = 0 면 대칭 자리로 — 위치 x 반대, 회전 거울 반사(qx, −qy, −qz, qw), 좌우 뒤집기 반대 → 반대쪽에서도 밖에서 바로 읽힌다), CLEAR(고른 데칼 끄기), HIDE OTHERS.
- **저장**(플레이어 영구 저장, 기종별): `aco_decal_<DecalKey>_<0|1>_on/px/py/pz/rx/ry/rz/rw/size/flip` — 샘플 기준 상대값(= 실기체 기준점 기준). 다른 사람 숨기기 `aco_decal_hide_others`. 키는 바꾸지 않는다.
- **실기체**(`ACO_VehicleDecal`, Bake가 플레이어 기체 SaccEntity 루트 아래 `ACO_Decals` + 프로젝터 2개, 수동 동기화 — 오너 = 조종사): 조종석에 앉으면(`ACO_VehicleUpgrades` → `LocalPilotEnter`) 내 URL(이번 접속)과 내 저장 자리 2개(그 기체 `DecalKey`)를 동기화 값으로 보낸다 — URL이 없거나 자리가 없으면 끈다. 내리면 끈다. 모든 클라이언트가 받은 자리에 프로젝터를 놓고 스튜디오에 이미지를 부탁해 받으면 켠다(0.5초마다 확인). 늦게 들어온 사람도 받는다. 다른 사람 데칼은 HIDE OTHERS면 내 화면에서 숨긴다(바꾸면 바로 — 스튜디오 `VehicleDecals`).
- 이미지: VRChat 이미지 로딩은 **PNG·JPG 직접 링크만**(WebP·HTML 페이지 링크는 실패 — 예: 나무위키 `.webp` 주소는 404). 확인 필요: VRChat에서 URL 입력 칸 키보드.

### 6-61. 관전 패널: 슬롯 선택·비

- **슬롯 선택**: TARGET 버튼은 AUTO → 슬롯 1 → 2 … 순으로 돌고 비어 있는 슬롯도 고른다. 조종사가 없으면 화면은 꺼지고 `WAITING FOR PILOT`, 타면 찍기 시작한다(자기 슬롯을 골라 두고 타러 갈 수 있다 — 세워 둔 기체는 보관 장소에 있어 찍지 않는다). 글자는 슬롯 이름 + 명단의 참가자. Bake가 `Vehicles`를 게임 흐름의 칸 순서로 넣는다.
- **체적 구름**: `CloudVolume`은 FX 레이어(28)라 관전 카메라에 찍히지 않고, 체적 품질에서는 판도 꺼져 층운이 안개로만 보였다. 카메라 자식 `SpectatorCloudVolume`(레이어 18)이 같은 머티리얼 인스턴스로 그린다. 시선을 깊이로 멈추므로 관전 카메라는 `depthTextureMode = Depth`를 켠다.
- **비**: 비는 머리를 따라다니는 FX 레이어(28)라 관전 카메라가 그리지 않았고, 머리가 격납고·구름 위면 꺼졌다. 카메라 자식 `SpectatorRain`(같은 메시·머티리얼, 레이어 18 — Bake가 만든다)을 따로 두고, 구름 위·지붕 아래를 카메라 자리로 판단한다. 빗줄기는 카메라 속도로 늘어난다.

### 6-62. 격납고 가시성

- **보유 크레딧**: 위쪽 금색 테두리 판에 동전 아이콘 + 큰 금색 숫자(`ACO_Shop.CreditsText`), 그 아래 지난 미션 보상(`LastRewardText` — 비면 예전처럼 둘째 줄).
- **구획**: 특수무장·강화·출격을 옅은 카드로 나누고, 제목 띠에 아이콘을 붙였다. 아이콘 hangar(제목), tab_missile(특수무장), upgrade(강화), roster(명단), ready·launch(버튼), credits(구매 버튼), 무장 분류는 `ACO_Loadout.PageIcon`이 분류에 맞게 바꾼다.
- 새 아이콘은 `Tools/aco_icons.py`. 프리팹 생성 창에서 격납고를 다시 만들어야 반영된다(`CreateHangarPrefab`).

### 6-63. 콘솔·관전 패널·작전판 아이콘

- **페이즈 난이도**: 콘솔의 큰 숫자 옆에 새 아이콘(`phase_1~5` — 병아리, 참새, 매, 독수리, 왕관 쓴 독수리). 숫자·아이콘·난이도 글자가 초록 → 빨강으로 함께 바뀐다(`ACO_GameFlow.PhaseLevelColors`, `PhaseLevelNames` EASY ~ ACE — 표시만, 실제 난이도 값을 바꾸지 않는다). 아이콘은 이름(`FlowPhaseIconName`)으로 Bake가 찾는다.
- **콘솔**: 상태 배너, 작전·페이즈·지형·명단 제목, NEW TERRAIN(순환 화살표 + 산), 슬롯 카드, 내 행동 버튼, LEAVE. **관전 패널**: POWER, HD/FHD, TARGET(이름을 SLOT으로), SHOT. **작전판**: 제목(지도), SCORE(우승컵, 월드 판이라 SpriteRenderer — `MakeWorldIcon`).
- 아이콘은 프리팹 빌더가 붙인다 — 프리팹 생성 창에서 작전 콘솔·관전 패널·작전판을 다시 만들어야 반영된다.

### 6-65. 데칼 스튜디오 개선

- **아이콘**: 제목·LOAD·DECAL 1/2·RESET GUN·SIZE·ROT·FLIP·MIRROR·CLEAR·HIDE OTHERS.
- **찍는 소리·이미지 창**: 데칼을 찍으면 건에서 스프레이 소리(`ACO_Decal_Spray.wav`, 합성 — 건의 `StampSound`, 로컬). 패널 오른쪽 아래 작은 창(`LoadedImage`)에 불러온 이미지를 비율대로 보인다(받는 동안엔 이전 이미지, 없으면 NO IMAGE).
- **데칼 건**: `VRCPickup.orientation = Gun`(`ExactGun` = Grip)이라 집으면 총구가 앞을 본다. 놓은 자리에 그대로 떠 있고(자동으로 돌아가지 않는다), 패널 RESET GUN이 시작 자리로 돌려놓는다(`ResetToHome` — 들고 있으면 먼저 놓는다, 로컬).
- **이미지 받기 고침**: (1) 이미 받은 URL을 다시 LOAD하면 받기 응답이 없어 LOADING에 멈추고 미리보기도 안 바뀌었다 → 바로 적용. (2) 실패한 URL은 캐시에 남아 다시 받지 않았다 → 자동 1회 재시도, LOAD로 다시 받기. (3) 응답이 안 오면 대기열 전체가 멈췄다 → `DownloadTimeout`(30초). (4) 칸이 다 차면 가장 앞 칸을 지워 패널이 쓰는 이미지나 대기 중인 요청이 사라졌다 → 실패 → 받아 둠 → 대기 순으로 돌려 쓰고 패널 이미지는 지운 칸에서 뺀다. (5) 패널에서 고른 이미지가 대기열 맨 앞(5초에 1장 제한 안내 표시).
- **잡는 방향**: Gun 방향 픽업의 `ExactGun`은 +X가 총구, +Z가 총의 위쪽이다(SAV 그래플링 훅 프리팹과 같다). 회전 없이 두면 총구가 위를 봐서, `Grip`을 Euler(-90, -90, 0)으로 돌렸다.

### 6-82. 비행 HUD 값 상자 위치

- 속도·고도 값 상자가 흘러가는 테이프 숫자와 겹쳐, 상자를 테이프 바깥(x ±21.6)으로, 스로틀을 x -26으로 옮겼다. 먼저 만든 프리팹은 Bake(`EnsureFlightHudLayout`)가 옮긴다.

### 6-90. 작전판 순위와 패널 정리

- **최고 획득 순위**: `ACO_PlayerProgress`가 미션 끝(`GrantReward`)에 그 미션 보상의 최고 기록을 `PlayerData` `aco_best_reward`에 저장한다. 작전판 `BestText`가 3초마다 인스턴스의 사람들 값을 읽어 높은 순으로 8명을 보여 준다. 다른 사람 값은 같은 인스턴스에서만 읽히므로 전 세계 순위가 아니다. 키는 절대 바꾸지 않는다.
- **작전판**: 프리팹 크기 0.7배(2.8 x 1.75m). 제목 칸은 줄바꿈 + 자동 글자 크기로 칸을 넘지 않는다. 조종사 집계는 8명까지. 먼저 만든 프리팹은 `EnsureBoardLayout`.
- **격납고**: 층을 판 < 항목 < 카드로 정했다 — 항목은 카드 안에 파인 어두운 칸이고 왼쪽 끝 가는 선으로 눌리는 곳을 보여 준다. 상태는 채도(보유 초록, 장착 진한 초록, 고름 금색)로만 나타내고 구매 버튼만 밝다. `ApplyHangarStyle`, 먼저 만든 프리팹은 `EnsureHangarStyle`.
- **관전 패널**: 아이콘 버튼의 글자를 아이콘 오른쪽 남은 칸 가운데에 두고 넘치면 줄인다(`SetIconGap`, 간격 18). 다른 패널의 새 버튼도 같다. 제목 아이콘도 띄웠다.

### 6-114. 데칼에 다른 사람 이미지가 보이던 문제

- **원인**: 기체 데칼은 이미지·크기·뒤집기·켜짐을 프로젝터 **머티리얼 값**으로 넣는다(Udon이 프로젝터 컴포넌트를 건드리지 않으려고). 그래서 머티리얼은 기체마다 따로여야 한다. 그런데 `ACO_Decals`가 기체 프리팹(`Pit-bool/FA-18E Main_ACO`) 안에 들어가 있어, 씬의 FA-18E 4대가 모두 같은 머티리얼 2개(`ACO_Decal_V0_0/1`)를 가리켰다. 한 기체가 이미지를 넣으면 4대 모두 그 이미지로 바뀌고, 누가 내리면 다 같이 꺼졌다(자리는 프로젝터마다 따로라 이미지만 섞인다).
- **고침**: Setup Check가 다른 기체와 데칼 머티리얼을 같이 쓰는 기체를 오류로 잡고, 고치기(`ACO_SceneBaker.SplitDecalMaterials`)가 첫 기체만 그대로 두고 나머지에 아무도 안 쓰는 `ACO_Decal_V<n>_<i>`를 씬 인스턴스 오버라이드로 준다(프로젝터 머티리얼 + `ACO_VehicleDecal.Materials`). 기체를 더 놓아도 같은 검사가 잡는다.

### 6-115. TGT 마커: 앞에 그리기, 보스 코어 자리

- **겹침 깜빡임**: 마커는 모두 머리에서 30m(`DisplayDistance`)에 같은 화면 크기로 놓여, 보스 TGT 테두리와 부위 마커가 겹치면 그리는 순서가 프레임마다 바뀌어 깜빡였다(z-fighting). 지금 페이즈 목표(빨간 TGT)는 `ObjectiveFrameNear`(0.985)만큼 가까이 놓고 크기도 같은 비율로 줄인다(화면에선 그대로). 락온 테두리(`LockedFrameNear` 0.97)·락온 마름모(0.94)는 그보다도 앞.
- **보스 코어 단계의 TGT 자리**: `ACO_Warship.MarkerAnchor`(동기화 없음 — 각 클라이언트가 단계로 정한다)가 있으면 마커를 유닛 기준점 대신 그 자리에 그린다. 공중 요새·육상전함은 코어 단계에 코어 락온 마커(`CoreMarkers[0]`), 랜스 포대는 코어 단계 내내 `Core`(덮개가 닫혀 있어도 열릴 자리를 보여 준다). 단계가 바뀌거나 다시 스폰되면 비운다. 락온 중이면 예전처럼 락온 지점이 먼저다.

### 6-131. 조종석 음성 경고 — `ACO_VoiceWarning` (로컬 전용, 씬에 하나)
- **경고 9종**(급한 정도 — 작을수록 급하다, 급한 음성이 덜 급한 음성을 끊는다):
  - PULL UP(0, 2초마다): 지금 하강 속도로 지면까지 3초 안, 또는 진행 방향 앞 지형까지 3초 안.
  - RAILGUN WARNING·LASER WARNING(1, 4·5초마다): 다른 스크립트가 상황이 이어지는 동안 `Warn(종류)` — 랜스 포대 사선 경고 중 내 기체가 사선 반경의 1.5배(240m) 안(`ACO_Railgun.UpdateWarn`), 소형 레일건이 나를 조준·고정(`ACO_MiniRailgun.UpdateLaser`), 레이저 에이스가 나를 비춤(`ACO_Borderless.UpdateLaser` — 기존 삑 경보와 함께).
  - STALL(1, 3초마다): 속도 55m/s 아래 또는 받음각이 기체 한계(SAV `MaxAngleOfAttackPitch`)의 90% 넘음.
  - DAMAGE CRITICAL(1, 한 번): 체력이 30% 아래로 떨어지는 순간(35% 위로 오르면 다시 센다).
  - ALTITUDE(2, 4초마다): 지면 위 150m 아래로 초속 2m 넘게 내려가는 중.
  - FLARES EMPTY(2): 플레어가 0이 되는 순간 한 번 + 0인데 쏘려 할 때(SAV 플레어와 같은 입력 — 키 X 또는 플레어를 고른 쪽 트리거, 누르는 순간마다) + 0인 플레어로 바꿨을 때.
  - AMMUNITION ZERO(2, 한 번): 탄이 다 떨어진 무장으로 바꿨을 때 — 기총(`GunAmmoInSeconds`)·ACO 미사일(`GetAmmo`)·특수무장 슬롯의 지금 모듈(슬롯 안 모듈 바꿈도 센다)·SAV AAM/AGM/폭탄. 탈 때 고른 무장은 세지 않는다.
  - BINGO FUEL(3, 한 번): 연료가 20% 아래로 떨어지는 순간(25% 위로 오르면 다시 센다).
- **재생**: 한 번에 한 음성. 이어지는 경고는 간격(`Repeat`)마다 다시, 끊겼다(0.6초) 다시 시작하면 바로. 한 번만 말하는 경고가 밀리면 3초 안에 자리가 나면 말한다.
- **끄기**: PULL UP·ALTITUDE·STALL은 기어 내림·지상(`Taxiing`)·안전지대(항모 갑판 등 — 안전지대가 정해져 있을 때만) 안에서, 고도 둘은 60m/s 아래에서도. 지면 판정 층 Default·Environment·Water.
- **음성**: `Tools/aco_make_voice.py` — Windows 기본 음성(Microsoft Zira, 영어 여성, 다운로드 없음)으로 "Pull up. Pull up." "Railgun warning." "Laser warning." "Altitude. Altitude." "Damage critical." "Stall. Stall." "Flares empty." "Bingo fuel." "Ammunition zero."를 읽고 무전 필터를 약하게(찌그러짐 1.0, 잡음 0.004) 입혀 앞뒤 무음을 자른다 → `Audio/Generated/ACO_Voice_*.wav`. 클립은 `Clips` 배열(순서 = `W_*`). 녹음한 음성으로 바꾸려면 같은 이름으로 덮어쓰거나 칸에 넣는다. **바뀜(2026-11-10)**: ElevenLabs Betty1(목소리 `N06kq0AB9NaDyfgAAiF9`, eleven_v3, 태그 없이 안정도 1.0)로 같은 파일 이름(wav, 24kHz)을 덮어쓴다 — `Tools/aco_elevenlabs.py`, 목록의 `warn_*` 줄(앞뒤 무음·끝 잡음을 자르고 반복 사이 0.25초로 다듬는다). 예전 Zira 음성은 aco_make_voice.py를 다시 돌리면 돌아온다.
- **설치**: Setup Check "조종석 음성 경고" [고치기](2D AudioSource + 스크립트 + 클립 9개) → Bake가 비행 HUD(기체·기어·플레어·기총·미사일·특수무장 목록)·레이더 연락·진행도와, 랜스 포대·소형 레일건·레이저 에이스의 `Voice`를 잇는다.

### 6-133. 무전 대기열 우선순위 — 페이즈 대사 먼저 (`ACO_Chatter`)
- **두 급**: 잡담 = 대사 표의 EV_KILL 이후 상황(격추·아군 격추·미사일 빗나감/속음·적 플레어·미사일 경보·피격·호위/콘보이 피격·연막·CIWS 등, `Report`). 우선 = 나머지 전부(페이즈 시작·성공·실패, 미션 완료·실패, 미션 시작·증원 웨이브, 보스 대사 표 `SayVoice`/`SayControl`, 에이스·유닛 고유 대사 `Say`).
- **차례**: 대기열에 우선 대사가 있으면 그것부터 낸다(같은 급끼리는 넣은 차례). 지금 나오는 대사는 끊지 않는다 — 잡담 한 줄이 나오는 중이면 끝난 뒤 바로 페이즈 대사.
- **가득 차면**(8줄): 가장 오래된 잡담부터 버린다. 잡담이 없을 때 새 잡담은 넣지 않고, 새 우선 대사는 가장 오래된 대사를 밀어낸다.
- 밀린 대사를 버리는 시간(잡담 `MaxQueueDelay` 6초, 페이즈 `PhaseQueueDelay` 10초)과 보스 페이즈의 `ClearQueue`는 그대로.
- **우선 대사는 버리지 않는다**: 대기열에서 밀린 대사를 버리는 시간(각 대사의 maxDelay — 보스 모듈 VoiceQueueDelay 8초 등)을 우선 대사는 최소 `HighPriorityKeep`(60초)로 늘린다. 레이저 에이스 2단계 전환처럼 긴 대사 다섯 줄(약 27초)이 한꺼번에 들어와도 차례로 다 나온다. 잡담(격추·미사일 경고 등)은 그대로 MaxQueueDelay·자기 시간이 지나면 버린다.
- **에이스 고유 대사는 잡담 급**(`ACO_Chatter.SayAce` — 레이저 에이스·형제·SHADE의 등장·기술·격추·편대원 대사): 우선 대사가 아니다 — 페이즈 대사 뒤에 나오고, `AceLineKeep`(3초)보다 밀리면 버린다(기술을 쓴 지 한참 뒤에 그 대사가 나오던 것). 에이스가 격추되면 그 에이스의 밀린 대사를 버린다(`DropQueued`) — 격추 뒤에 기술 대사가 나오지 않게. 격추 대사는 나오던 대사(페이즈 대사 포함)를 끊고 바로 나온다(`SayAceNow` — 음성·PTT를 멈추고 띄운다, 끊긴 대사는 다시 나오지 않는다). 다른 격추 대사가 나오는 중이면 끊지 않고 바로 뒤에(8초 안). 우선 대사는 페이즈 모듈 대사(시작·성공·실패·상황별 대사 표)와 미션 진행뿐.

### 6-144. 발사 무전 — 무장별 코드 (`ACO_Chatter.CallLaunch`, `ACO_DFUNC_Missile.LaunchCall`)
> 바뀜(6-158): 자막 화자를 AWACS로, Spear·Drive 음성을 담담하게.
- **쏜 사람 화면만**(네트워크 없음): 발사 입력 한 번에 한 마디 — 자막 `[내 콜사인] 코드`(콜사인은 TAC 네임 `ACO_TacName.GetLabel`, 없으면 VRChat 이름 — Bake가 무전에 TAC 네임을 잇는다), 음성은 관제기(AWACS) 목소리로 코드만(관제 채널 PTT).
- **언제**: 새로 누른 입력에서 실제로 쐈을 때만(누르고 있는 로켓·폭탄 연사, MMP 연사 다음 발은 아님). ECM은 켤 때. 같은 코드는 `LaunchCooldown`(3초) 안에 다시 내지 않는다.
- **다른 무전과 겹칠 때**: 아무것도 안 나오면 바로. 대기열에 기다리는 대사가 있으면 버린다. 다른 대사가 나오는 중(자막·음성·PTT·대사 사이 간격)이어도 버린다 — 버린 발사 무전은 나중에 나오지 않는다.
- **코드**(무장 모듈 `LaunchCall` — 드롭다운, 글자·음성은 무전의 `LaunchTexts`/`LaunchClips` 같은 번호): Fox Two(일반 미사일·QAAM·SASM·HPAA·CCAA), Fox Three(4AAM·LAAM·HVAA·ADAM), Drive! Drive!(MMP), Rifle(4AGM), Magnum(LAGM), Greyhound(SCM), Bruiser(LASM), Pickle(UGB·LGB), Rockets away(RKT·LGR·FLAK), Spear!(RGP), Music on(ECM). 예제 무장은 만들 때 넣고(`LaunchCallFor`), 이미 있는 무장은 Setup Check `launch-call-1`(정의 프리팹·기체 슬롯 모듈·일반 미사일, 이미 코드가 있으면 그대로). 복제해 만든 무장은 모듈에서 고른다(None = 무전 없음).
- **음성**: ElevenLabs AWACS(`Launch/Launch_*.mp3`, 목록 `launch_*`) — 무전 칸은 Setup Check `launch-voice-1`이 빈 칸만 채운다.

### 6-149. 데칼 건 모델 — 스프레이건 + 도료 안개
- 모델: `Tools/aco_decal_gun_model.py`(Blender) → `Models/DecalGun/ACO_DecalGun.fbx` + 텍스처 512(`ACO_DecalGun_Albedo.png`), 약 1천 삼각형. 예전엔 초록 상자(5×9×24cm)였다.
  - 에어 도장용 스프레이건(길이 약 25cm): 팔각 몸통(금속 은색), 앞 노즐·공기캡(초록 띠)·뿔 둘·노즐 끝, 뒤 조절 손잡이 둘(널링 — 뒤에서 볼 때 몸통 팔각 윤곽 안), 몸통 앞 이음 고리, 위 걸이 고리, 공기 밸브 핀, 권총 손잡이(뒤로 15도 — 윗부분은 몸통과 같은 금속으로 윗면을 수평하게 몸통 안에 묻고, 그 아래 검정 고무 덮개. 기울어진 단면이 몸통 밑으로 삐져나오지 않게) + 끝 금속 마감 + 아래 공기 호스 연결구(황동 육각 너트·꼭지),
    두 손가락 방아쇠(크롬), 위 도료 컵(흰 플라스틱, 양옆 초록 라벨 "ACO DECAL" — 글자는 텍스처에 칠함)·뚜껑(옆 홈)·숨구멍 꼭지.
  - 메시는 FBX 자식 변환을 구워 `Generated/ACO_DecalGun_Mesh.asset`(노즐 +Z), 머티리얼 `ACO_DecalGun_Model`(Standard — 격납고 빛을 받는다). 충돌 상자 = 메시 크기.
  - Grip(ExactGun)은 손잡이 가운데, Muzzle은 노즐 끝(조준선·찍기 레이가 여기서). 놓인 높이는 예전 상자 밑면에 손잡이 끝을 맞췄다.
- 도료 안개(`ACO_DecalGun.Mist`): 방아쇠를 누를 때마다(샘플에 안 맞아도) 노즐 앞 6도 원뿔로 10개, 3~4m/s로 나가 금방 느려지고 0.3~0.45초 동안 3cm → 약 10cm로 커지며 옅어진다(옅은 흰 회색, 고른 데칼 색과 상관없음).
  기본 파티클 텍스처 대신 직접 만든 부드러운 점(`ACO_SprayMist.png`, ACO/FxParticle 알파). 월드 공간·로컬. 분사 소리도 방아쇠마다(예전엔 찍혔을 때만). 데칼은 예전처럼 샘플에 맞았을 때만 찍힌다.
- 설치: 프리팹 판 `ACO_DecalStudio` 2(생성기로 다시 만든다) → Bake.

### 6-151. 격납고 새 배치 — 머리줄 하나 · 상세 칸 · JOIN · 문구 새로
- **배치**(`CreateHangarPrefab`, 프리팹 판 `ACO_Hangar` 6): 제목 HANGAR는 왼쪽 위, 보유 크레딧은 오른쪽 위 판 하나(지난 미션 보상 줄은 뺐다 — `LastRewardText`가 없으면 쓰지 않는다). 생긴 높이(약 240)만큼 두 카드를 위로 늘렸다(위 700 ~ 아래 -680).
  - 왼쪽 특수무장: 제목 줄(SPECIAL WEAPON: 이름 + 잠김·가격·안내만) → 분류 넘김 → 목록 5줄 → **무장 상세 칸**(`ACO_Loadout.DetailText` — 부제 굵게 / 설명 / 기본 수치) → BUY → SUPPORT 줄.
  - 오른쪽 강화: 탭 → 탭 안내 → 줄 7개(크기 그대로) → **상세 칸**(`ACO_Shop.DetailText`) → 구매 버튼 → 메시지.
  - 아래 출격 줄: 명단 글자 + **JOIN/LEAVE**(`ACTION_TOGGLE_JOIN` — 콘솔에 가지 않아도 명단에 들어간다) + READY + LAUNCH.
- **상세 칸**(줄을 고르면, 안 고르면 `SELECT AN ITEM TO SEE DETAILS`): 강화 = 이름 + `LV n/10` / 설명 / `NOW +30% > NEXT +40%`(정확한 수치, 최대면 `MAX LEVEL`), 장비(소모품 — 탭 이름 ITEMS → **EQUIPMENT**) = 이름 / 설명 / 정확한 효과(+ 장착함), 호출권 = 이름 / 설명 / **주의사항**(노랑 — 벙커버스터는 부를 때 찍은 자리에 늦게 떨어진다, 급유는 곧고 수평하게 일정한 속도로, 가디언은 나만 지키고 잠시 뒤 떠난다 — 수치·장수 없음). 크레딧이 모자라면 `NEED n MORE CR`(빨강). 장비·호출권 줄에는 이름과 가격(×장수)만.
  - 무장 상세 칸은 부제 + 설명만(정확한 수치는 쓰지 않는다 — 추상적으로).
  - 문장은 틀(`Detail*`, `CallStatTemplates` — `{NOW}` `{CR}` 같은 자리)로 끼운다(언어 전환 대비). 글자 칸은 자동 크기(34 → 22).
- **문구 새로**(브리핑식 — 무장·호출권은 수치 없이, 강화·장비는 정확한 수치): 특수무장 20종 설명(부제 + 한두 문장 — `ACO_SpecialWeaponCatalog.Descriptions`, Setup Check `hangar-texts-1`이 정의 프리팹을 고친다), 강화 30·소모품 4·호출권 3 설명(`ACO_Shop.*Descriptions`), 탭 안내 7줄.
- 설치: 프리팹 판 `ACO_Hangar` 6 다시 만들기 → Setup Check `hangar-texts-1` → Bake.

### 6-153. URL 영구 저장소 — Player Object (데칼 이미지 URL 저장)
- PlayerData는 VRCUrl을 못 담고 스크립트는 문자열로 URL을 만들 수 없다. 대신 **Player Object**(`VRCPlayerObject` — 플레이어마다 VRChat이 템플릿을 복제) + `VRCEnablePersistence`(그 오브젝트의 동기화 변수를 영구 저장)에 `[UdonSynced] VRCUrl[]`을 둔다(`ACO_UrlStore`, 수동 동기화, 오너 = 그 플레이어).
- **칸은 처음부터 16칸 고정**(나중에 동기화 필드를 더하면 저장 데이터가 깨진다는 제보). 칸 번호 = 저장 ID(바꾸거나 다른 용도로 다시 쓰지 않는다): 0~3 데칼 이미지(지금 0·1), 4~15 비워 둠(개인 BGM·선원실 TV 등 나중 용도). 빈 칸은 `VRCUrl.Empty`(null이면 ClientSim 오류 제보).
- 흐름: LOAD → 스튜디오가 이미지를 받고 저장소 칸에 넣어 동기화(복원 뒤에만 — 그 전엔 화면에만). 다음 접속: 저장소 `OnPlayerRestored` → 스튜디오 `OnUrlStoreRestored`가 저장된 URL을 다시 받는다("SAVED IMAGES RESTORED") → 조종석에 앉으면 예전처럼 기체가 URL·자리를 보낸다. 이번 접속에 이미 넣은 칸은 덮지 않는다. CLEAR는 데칼만 끄고 URL은 남긴다. 받기 실패면 예전처럼 이유 표시(저장 값은 새로 LOAD하면 바뀐다).
- 설치: Setup Check "씬 부품" URL 저장소 [고치기](`CreateUrlStore` — 씬에 템플릿 하나) → Bake(저장소 → 스튜디오). 에디터(ClientSim)에선 저장이 흉내라 실제 확인은 업로드 뒤 다시 접속해서.
- 용량: 플레이어당 Player Object 100KB 한도 중 URL 16칸은 아주 작다. PlayerData가 이미 모두에게 보내 주는 값(콜사인·헐넘버·킬마크·최고 보상 등)은 옮기지 않는다 — Player Object만의 쓸모는 VRCUrl 저장.

### 6-158. 발사 무전 자막 — 화자 AWACS · Spear/Drive 음성 담담하게
- 자막: `[AWACS] 내 콜사인, 코드`(예: `[AWACS] VIPER, Fox two.`) — 말하는 쪽이 관제기라서. 틀은 `ACO_Chatter.LaunchFormat`("{name}, {call}" — 언어 전환 때 틀만 바꾼다), 화자 이름·색은 SpeakerNames/Colors 0번(AWACS). 코드 글자(LaunchTexts)는 그대로 — Spear!·Drive! Drive!의 느낌표 유지.
- 음성: Spear·Drive만 감정이 많이 들어가 있어 다른 코드처럼 `[crisp, calm]`으로 다시 만들었다("Spear." · "Drive, drive.").

### 6-160. 튜토리얼 — 책장 위 표식 + 눈앞 팝업 (`ACO_TutorialPopup`, 프리팹 `ACO_Tutorial`)
> 바뀜(6-164): 책장 표식 · 눈앞 팝업을 없애고 브리핑 판으로 옮겼다 — 판 머리줄 버튼 + 홀로그램 화살표 + 판을 덮는 팝업, 마지막 페이지엔 다음 버튼이 없다(다음 = 닫기 아님).
- **왜**: 콘솔 배너가 "다음 할 일"은 알려 주지만, 처음 온 사람은 이 월드가 무엇인지, 무장 패널(오른쪽 벽 — 스폰 자리에서 시야 밖)이 어디인지, ACO 조작(SAV 기본 안내와 다른 키)을 모른다.
- **표식**: 브리핑실 책장 위 30cm에 "튜토리얼" 글자 + 아래 쉐브론 둘(월드 Canvas, 누를 수 없음). 내가 10m(`ActiveRange`) 안에 있을 때만 움직인다 — 글자판은 좌우로만 돌아 나를 향하고 쉐브론은 위아래로 흔들린다. 아직 열지 않았으면 밝게 깜빡이고(PlayerData 새 고정 키 `aco_tutorial_seen` — 처음 연 순간 저장), 연 뒤로는 은은한 밝기.
- **열기**: 루트의 트리거 상자(책장 앞면 + 표식, 몸에 부딪히지 않음)를 Interact("Tutorial"). 팝업(0.96 x 0.6m)은 머리에서 수평 앞으로 1.1m, 눈높이에 나타나 나를 향하고 그 자리에 고정된다. 앞에 벽·가구가 있으면 그 15cm 앞(최소 0.5m) — 선을 쏘기 전에 열린 팝업을 꺼서 자기 UI 콜라이더에 걸리지 않게. 열린 채 다시 누르면 눈앞으로 옮긴다(페이지 유지).
- **페이지 5장**: 1 개요(표지 사진 + 카드 4장 — 바뀜(6-163): 표지 전체 배경 + 아래 카드 한 줄: 매번 새로운 전장 · 다양한 적과 작전 · 캠페인과 난이도(연속 임무 수 — 길수록 플레이 시간이 늘고 강한 적이 나올 확률이 오른다) · 크레딧과 성장 — 개수는 쓰지 않는다, 업데이트로 늘어나므로. 보너스 작전은 말하지 않는다), 2 출격 방법(JOIN → 무장 → READY → 함께 출격 + 콘솔 맨 위에 할 일이 나온다 + 격추되면 LAUNCH로 재출격), 3 비행 중(데스크톱 | VR — 내리기: 데스크톱 Enter 두 번, VR 점프 한 번 = 좌석 높이 맞추기(6-21) · 두 번 = 내리기 + 미사일 슬롯 장전 · 지원 호출(격납고에서 고른 지원 — 위치가 필요한 지원만 바라본 곳 지정, 이름을 들지 않는다) · 캐터펄트(유도요원 신호로 캐터펄트에 올라 고정 → 사출장교 신호에 애프터버너 → 점검 뒤 사출, 갑판 요원을 끄면 풀스로틀 2초) · 착함 불필요 — SAV 기본 안내와 다른 ACO 키 기준: 3 특수무장, 4 지원 호출), 4 크레딧과 강화(격추 페널티는 "보상이 줄어든다"만), 5 함내 지도(가운데 위에서 본 도면 `tut_plan.png`, 위아래에 방 사진 6장 — 도면의 그 자리(지금 위치 = 흰 점·흰 테두리)에서 사진까지 선으로 잇는다. 위 줄 = 복도·격납고 문·갑판 문(갑판은 "관전"만 — 갑판에 간다고 탈 수 있지 않다), 아래 줄 = 브리핑실·CIC·선원실 — 선이 엇갈리지 않게 같은 순서. 오른쪽에 "격납고와 비행갑판은 복도 끝 문을 눌러(Interact) 이동" 안내 — 문은 지나가는 것이 아니라 눌러야 순간이동한다. 자리는 생성 코드의 월드 좌표).
  - 처음 열면 1페이지, 같은 접속 중 다시 열면 마지막 페이지. 마지막 페이지의 `>` = 닫기. 닫힘: X · 마지막 `>` · 팝업에서 3m 넘게 멀어짐(걷기·출격·귀환).
- **글자**: 생성 코드(`ACO_HUDBuilder.Tutorial.cs`)의 표 `TutorialTexts` 한 곳에 키별로(`tut.p1.title` 등, 글자 오브젝트 이름 = 키). 언어 전환 때 이 표만 언어별로 늘린다.
- **폰트**(범용 패널 폰트 — 앞으로 한글을 쓰는 패널도 같이 쓴다): Pretendard(SIL OFL 1.1, `Fonts/Pretendard-OFL.txt` — GitHub orioncactus/pretendard v1.3.9의 `Pretendard-Medium.ttf`·`PretendardJP-Medium.ttf`). **동적 TMP 폰트** `Fonts/ACO_Pretendard SDF.asset`(1024 아틀라스, 여러 장 허용) + 일본어 보조 `ACO_PretendardJP SDF.asset`(Fallback) — 어떤 글자든 쓸 때 그린다(언어 전환 대비). 빌드에 TTF 두 개(약 8MB, 압축 전)가 들어가고, 에디터에서 채운 글자는 빌드 때 지운다(`m_ClearDynamicDataOnBuild`). Setup Check [고치기] = `EnsureTutorialFonts`. 폰트가 없으면 기본 폰트로 만들고 경고한다(한글이 네모). 기존 패널(LiberationSans)은 그대로 — 전환은 따로.
- **사진**: `Textures/Tutorial/` — 방 사진 6장 `tut_{briefing,cic,corridor,berth,hangar,deck}.png`(512), `tut_plan.png`(함내 도면: 천장 보·배관 아래(y −247.9)에서 아래로 찍은 직교 촬영, 가로 22m × 세로 11m, 2048 x 1024)와 `tut_cover.png`(표지). Setup Check [고치기]가 찍는다(범위·경로는 생성 코드 값 — 함내 배치를 옮기면 고친다). 표지는 있으면 그대로 둔다(전투 장면 스크린샷으로 바꿔 넣고 프리팹을 다시 만든다).
- **설치**: Setup Check — 사진 찍기, 튜토리얼 놓기(내부 가구 콜라이더 `Int_Bookcase` 자리, 루트 = 책장 바닥 가운데 · +Z = 앞면). 프리팹 판 번호 `ACO_Tutorial` 1. Bake 연결 없음(동기화 없음, 혼자 돈다).
- **같이 바꾼 것**: 콘솔 안내 `NEXT: PICK YOUR LOADOUT ON THE HANGAR PANEL (RIGHT WALL), THEN PRESS READY`(진짜 격납고로 찾아가지 않게). 데스크톱 캐노피 키(Z) 끊기 — 캐노피는 기체 자동화(6-27)가 여닫는다(Setup Check `canopy-key-off-1`, 기체 프리팹 SAV_KeyboardControls).

> 바뀜(6-160): 설정 패널(`ACO_HudSettings`) 제목을 HUD SETTINGS → SETTINGS(판 번호 4) — HUD 말고도 갑판 요원 등 설정이 있어 튜토리얼과 같이 "설정"으로 부른다.

### 6-162. 격납고 패널 → 로드아웃 패널 (화면 이름만)
- 왜: "격납고"가 무장·강화 패널(브리핑실 오른쪽 벽 사본 포함)과 데칼·콜사인이 있는 진짜 격납고 방을 함께 가리켜, 처음 온 사람이 "격납고에서 무장을 고르라"를 듣고 방으로 찾아갔다.
- 바꾼 것(보이는 글자만): 패널 제목 HANGAR → **LOADOUT**(`ACO_Hangar` 판 번호 7 — 브리핑실 사본과 격납고 방 패널이 함께), 콘솔 안내 `...ON THE LOADOUT PANEL (RIGHT WALL)...`, 지원 칸 HUD `NONE - PICK IN LOADOUT`, 튜토리얼 "로드아웃 패널"(2·3·4페이지). 방 이름 "격납고"(5페이지 사진, 복도 문 TO HANGAR)는 그대로.
- 그대로 둔 것: 코드·프리팹·오브젝트 이름(`ACO_Hangar`, `ACO_HangarButton` 등), 저장 키, 설계 문서의 기존 "격납고 패널" 표현(같은 것을 가리킨다).

### 6-163. 튜토리얼 표지 — 영화풍 편대 사진, 1페이지 전체 배경
- **표지 사진** `Textures/Tutorial/tut_cover.png`: F/A-18 4기가 해안 육각 지형(도시 3블록 · 육상전함) 위로 상승하는 장면. 흐린 낮, 망원(화각 22°), 기체 위에서 내려다보는 각도, 살짝 왼쪽 롤, 기어 업, SAV 애프터버너 · 날개 끝 트레일. 배경만 흐리게(피사계 심도) + 청록 그림자 · 주황 밝은 곳 + 비네팅 · 그레인, 기체는 원래 밝기. 튜토리얼에는 타이틀 없는 판을 쓴다(이름 줄이 페이지에 따로 있다).
- **만든 방법**(다시 뽑을 때): Unity에서 임시 오브젝트로 장면을 세워(씬에 남기지 않음) 기체 없는 배경 + 기체·이펙트만(검은/흰 배경 두 번 찍어 알파 계산)으로 나눠 찍고, HTML(`Promo/source/poster3.html`)로 합성한다. 원본 층과 최종 이미지는 프로젝트 루트 `Promo/`(git 밖): `ACO_Thumbnail_1200x900.png`(VRChat 업로드 썸네일 — 템플릿의 인게임·확대 범위 안에 타이틀), `ACO_Cover_1920x1080.png`(타이틀 포함), `ACO_Cover_NoTitle_1920x1080.png`(= tut_cover).
- **1페이지 배치**(`ACO_Tutorial` 판 번호 2): 사진을 팝업 전체(1600 x 1000)에 깔고 가운데 기준으로 자른다 — UV는 사진 원본 비율로 계산(텍스처가 2의 거듭제곱으로 늘어나도 맞게, 표지가 없어 항모 사진을 찍은 경우도 같다). 위 330 · 아래 520을 어두운 띠로 덮고, 위 = 페이지 제목 · 이름(54) · 한 줄 소개(31), 아래 = 반투명 카드(불투명도 0.78, 위쪽 초록 선) 4장 한 줄(356 x 250, 제목 34 · 본문 26). 2 x 2로 두면 기체 대부분을 가려서 한 줄로 바꿨다. 카드는 기체 꼬리 · 애프터버너 일부를 덮는다(받아들임).
- 표지 텍스처는 그대로 최대 1024.

### 6-164. 튜토리얼을 브리핑 판으로 — 머리줄 버튼 · 홀로그램 화살표 · 판을 덮는 팝업
- 왜: 책장은 왼쪽 옆벽 앞 구석이라 스폰에서 정면(앞벽)을 보면 화면 끝에 걸리고, 떠 있는 글자 표식은 보기 좋지 않았다. 팝업이 일부만 가리는 것도 어색했다.
- **자리**: 프리팹 루트 = 작전 콘솔(`ACO_MissionConsole`) 캔버스 가운데, +Z = 방 쪽. Setup Check가 놓고, 예전 책장 자리 인스턴스는 "판 자리로 옮기기"로 옮긴다. 책장 클릭 · 열기 콜라이더는 없앴다(책장은 소품).
- **버튼**: 판 머리줄 오른쪽 빈칸(콘솔 캔버스 좌표 (840, 600), 520 x 130)에 "?  튜토리얼" — 판 앞 1.2cm에 따로 놓은 캔버스(VRC UI Shape) → `Open`. PC는 화면 가운데 커서, VR은 레이저. 콘솔 판 배치를 바꾸면 생성 코드의 TutButtonPos · TutButtonSize를 함께 고친다.
- **홀로그램 화살표**(`Marker`): 버튼 바로 위, 판 앞 0.18m에서 아래를 가리키는 3D 화살표(길이 0.42m — 천장 3.2m에 닿지 않게) + 화살 머리 위 링 2개. 가산(Legacy Particles/Additive) 녹색 두 겹(`Generated/ACO_TutHoloFill · ACO_TutHoloGlow`, 메시 `ACO_TutArrow · ACO_TutRing`). 세로축으로 돌고(45°/s) 위아래로 흔들리며 링 크기가 맥박친다. **한 번도 열지 않은 사람에게만** 보인다(PlayerData `aco_tutorial_seen`) — 열면 끄고 다시 켜지 않는다. 팝업이 열려 있는 동안도 끈다.
- **팝업**: 판 앞 4cm에 판과 같은 크기(1600 x 1000 캔버스 x 2.94/1600 = 2.94 x 1.84m, 비율이 같아 꼭 맞게 덮는다). 움직이지 않는다. 판은 뒤에서 계속 갱신되고 닫으면 최신 상태가 보인다. 팝업은 연 사람 화면에만.
- **페이지**: 마지막 페이지에서 다음 버튼을 숨긴다(다음 = 닫기를 없앰 — 두 번 눌러 실수로 닫히지 않게). 닫기는 X 또는 팝업에서 7m 넘게 멀어질 때(브리핑실 뒷자리에서도 열려 있게 방 깊이보다 크게).
- 표지 텍스처 최대 크기 2048(판 크기 전체에 깔려서).

### 6-165. 튜토리얼 한국어 · 영어 · 일본어
- **글자 표**: `ACO_HUDBuilder.Tutorial.cs`의 TutorialTexts — 키 하나에 한국어 · 영어 · 일본어 세 칸. 글자 오브젝트 이름 = 키. 생성기가 프리팹 안의 키 이름 글자(판 버튼 포함)를 모아 `ACO_TutorialPopup.LocTexts`와 언어별 문장 배열(LocKo · LocEn · LocJa)에 넣고, 실행 중엔 문장만 바꿔 끼운다(문자열 이어 붙이기 없음). 게임 용어(JOIN · READY · LAUNCH · LOADOUT 패널 · SETTINGS)는 패널 글자 그대로.
- **언어 정하기**: 처음엔 VRChat 클라이언트 언어(`VRCPlayerApi.GetCurrentLanguage` — ko → 한국어, ja → 일본어, 그 밖 → 영어), 게임 중 바꾸면 따라간다(`OnLanguageChanged`). 팝업 아래 띠 X 옆 버튼(한 · EN · 日 — 각 언어로 적어 바꾸지 않는다)으로 고르면 PlayerData `aco_tutorial_lang`(고정 ID, 0 한국어 · 1 영어 · 2 일본어)에 남고 그 뒤로는 VRChat 언어를 따르지 않는다. 열린 채 바꾸면 보던 페이지에서 글자만 바뀐다. 동기화 없음(각자 화면).
- **넘침**: 튜토리얼 글자는 모두 TMP 자동 크기(지정 크기 ~ 75%) — 영어가 길어도 칸 안에 맞춘다.
- **글꼴**: Pretendard(한글 · 영문) + Pretendard JP 보조(가나 · 한자) 동적 폰트.
- 같이 바꾼 것: 5페이지 격납고 이름 "데칼, 콜사인" → "커스터마이징"(Customization / カスタマイズ).
- 언어를 늘리려면: 표에 칸 추가 → 스크립트 언어 번호 · 배열 · 버튼 추가.

### 6-166. 패널 번역 — 번역 장치 · 문장 표 · 설정 LANGUAGE (1단계)
- **번역 장치** `ACO_Localization`(씬에 하나, 동기화 없음 — 각자 자기 언어): 문장 표(Keys · Ko · En · Ja)는 Setup Check가 `Localization/ACO_Strings.tsv`에서 넣는다. Bake는 연결만 — 이름이 표의 키인 TMP 글자(Labels/LabelKeys)와, `Loc` 필드가 있는 스크립트(Listeners, Loc = 장치)를 모은다. 언어가 바뀌면 라벨을 한꺼번에 바꾸고 듣는 스크립트의 `LocalizationChanged`를 부른다.
- **문장 표** `Localization/ACO_Strings.tsv`: 줄마다 `키 \t 한국어 \t 영어 \t 일본어`(UTF-8, 첫 줄 머리, `#` 주석, 칸 안 줄바꿈은 `\n`). 스프레드시트로 고쳐도 된다. 키 규칙 = `패널.분류.이름`(설정 `set.*`). 한 번 쓴 키는 이름을 바꾸지 않는다(라벨 오브젝트 이름 = 키). 그 언어 칸이 비면 영어, 표에 없으면 키 그대로.
- **스크립트 문장**: `Loc.Get(키)`, 값이 들어가면 `Get1/Get2/Get3(키, 값…)` — 문장 안 `{0} {1} {2}`를 바꾼다(이어 붙이지 않는다 — 언어마다 어순이 다르다). 장치가 없으면 영어 그대로(`T(키, 영어)`).
- **생성기**: 라벨 이름 = 키, 기본 글자 = `ACO_LocalizationSetup.English(키)`(표의 영어), 칸을 넘으면 70%까지 자동 축소(`LocFit`).
- **언어 설정**: 설정 패널 LANGUAGE 항목(`ITEM_LANGUAGE` 35, 저장 ID `language` — 값 0 AUTO · 1 한국어 · 2 영어 · 3 일본어). AUTO = VRChat 클라이언트 언어(ko → 한국어, ja → 일본어, 그 밖 → 영어)를 따르고 게임 중 바뀌면 따라간다(`OnLanguageChanged`). 언어 이름은 그 언어로 적는다(한국어 · ENGLISH · 日本語 — 지금 글자를 못 읽어도 찾게), 카드 제목도 `LANGUAGE · 언어 · 言語`.
- **튜토리얼**(6-165): 팝업의 한 · EN · 日 버튼을 없애고 장치를 따른다(튜토리얼 문장은 그대로 튜토리얼 생성기 표). 저장 키 `aco_tutorial_lang`은 올린 적이 없어 버린다(다시 쓰지 않는다).
- **글꼴**: 패널 글꼴 LiberationSans SDF의 보조 글꼴에 Pretendard(한글, 그 뒤 Pretendard JP 보조)를 붙인다(Setup Check) — 영어 모양은 그대로, 한글 · 가나 · 한자만 Pretendard.
- **동기화 규칙**: 네트워크로는 문장이 아니라 상태 번호를 보내고 받는 쪽이 자기 언어로 만든다(1단계에서 점검).
- **로드아웃 패널**(ACO_Hangar 판 8): 키 `load.*`(무장 목록 · 지원 줄), `shop.*`(상점 탭 설명 · 상세 칸 · 구매 버튼 · 안내), `prog.*`(진행도 이름 — 강화 `prog.up.<저장 ID>` · 설명 `prog.updesc.<저장 ID>`, 장비 `prog.eq/eqfx/eqdesc.<저장 ID>`, 지원 호출권 `prog.call/callfx/calldesc/callnote.<저장 ID>`, 분류 `prog.cat.*`, 보상 요약 `prog.reward.*`), 무장 설명 `wpn.<무장 ID>`(첫 줄 = 부제), 공통 `common.*`. 진행도(ACO_PlayerProgress)의 이름 함수가 지금 언어로 돌려주므로 상점 · 로드아웃 · 미션 기록이 같이 바뀐다. 표에 없는 키는 스크립트 · 인스펙터의 영어 그대로(새 무장은 `wpn.` 줄을 더하기 전까지 영어 설명).
  안내 한 줄은 언어가 바뀌면 상점은 처음 안내(`shop.hint`)로 되돌리고, 로드아웃은 키로 들고 있어 새 언어로 다시 쓴다. 상세 칸 문장 틀(DetailLevel 등 인스펙터 칸)은 없애고 표로 옮겼다. 한국어 · 일본어 문장의 "SUPPORT"는 화면 이름에 맞춰 지원/支援으로 쓴다. 강화를 산 뒤 안내는 분류 이름 없이 `강화 이름 LV n - 다음 출격부터 적용`.
- **브리핑 작전 콘솔 · 출격 명단**(ACO_GameFlow, ACO_MissionConsole 판 2): 키 `flow.*` — 거절 안내 `flow.msg.*`, 버튼 `flow.btn.*`(참가 · 준비 · 출격 · 관전 · 나가기 · 새 지형 · 자유 비행), 명단 `flow.roster.*` · 상태 `flow.state.*`, 상태 배너 `flow.ban.*`, 작전 카드 `flow.card.*` · 카드 제목 `flow.title.*`, 명단 칸 `flow.slot.*`, 출격 카운트다운 판 `flow.cd.*`, 자동 시작 HUD 안내. 지형 이름 `flow.terrain.<프리셋 번호>`, 난이도 `flow.level.<페이즈 수 - 1>`, 날씨 `flow.wx.<WX_*>`(표에 없으면 인스펙터 이름 — 지형 프리셋을 더하면 표에도 한 줄).
  동기화 점검: 게임 진행이 보내는 값은 숫자 · 참거짓뿐(명단 playerId, 준비 비트, 시각, 씨앗)이라 고칠 것이 없었다. 문장은 각자 자기 언어로 만든다. 거절 안내는 키 + 값으로 들고 있다가 그릴 때 만든다(언어를 바꾸면 0.5초 안에 다시 쓴다). 플레이어 이름 · 기체 이름은 번역하지 않는다. 디버그 패널이 받는 거절 문장은 영어 그대로.
  상태 배너는 오른쪽 튜토리얼 버튼 밑으로 글자가 들어가지 않게 오른쪽을 비우고(넘치면 70%까지 줄임), 영어 "다음: 로드아웃 고르기" 문장을 줄였다.
- **2단계 패널**: 키 `log.*`(작전판 머리글 · 기록 표 · 조종사 표 · 내 미션 · 하이스코어), `bgm.*`, `tac.*`(콜사인 · 헐넘버), `decal.*`(버튼 · 칸 · 안내), `spec.*`(전원 · 대상 · 촬영 이름 9개 · 상태), `credits.title`. 판 번호: 작전판 6, 하이스코어판 2(새 판 항목), BGM 3, TAC 네임 2, 데칼 3→4, 관전 3, 크레딧 2.
  - 3D 판(작전판 · 하이스코어판 — Canvas 없는 TextMeshPro): 번역 장치에 `Labels3D/Label3DKeys`를 더해 Bake가 이름이 키인 3D 글자도 모은다.
  - 작전판 기록 줄은 번역할 말을 `[[키|값]]`으로 넣어 두고 그릴 때 바꾼다 — 언어를 바꾸면 이미 쌓인 줄(페이즈 완료/실패, 아군 유닛, 알 수 없음)도 바뀐다. 플레이어 이름의 `[[ ]]`는 끊어 둔다. 무장 약어(GUN · AAM · AGM · BOMB), 유닛 표지(FTR · TGT · 기종), 격자 좌표는 그대로.
  - 머리글 칸 높이 9.4 → 8.6(기록 표 첫 줄을 덮지 않게 — 길면 칸 안에서 줄어든다).
  - 데칼 · TAC 안내 한 줄은 키 + 값으로 들고 있다가 다시 쓴다(데칼). TAC은 언어를 바꾸면 안내를 지운다. BGM 패널은 마지막 음량 · 곡 상태로 다시 쓴다.
  - 번역 안 함: 곡 이름, 콜사인 · 헐넘버 값, 크레딧 본문(출처 표기), 입력 칸 안내 글자(옛 UI Text — ENTER CALLSIGN 등), 데칼 건 잡기 안내(VRC Pickup 글자), 해상도 이름(HD / FHD).
  - 페이즈 이름 · 실패 이유(작전판 머리글 · 기록 줄)는 페이즈 모듈 18개가 내는 글자로 비행 중 배너와 같이 쓰므로 3단계에서 한다.
- **범위**: 1단계 = 장치 · 설정 LANGUAGE · 튜토리얼 · 설정 패널 · 로드아웃 패널 · 브리핑 작전 콘솔(모두 완료). 2단계 = 작전판 · 하이스코어 · BGM · TAC 네임/헐넘버 · 데칼 스튜디오 · 관전 패널 · 크레딧 제목(완료). 2단계 = 작전판 · 하이스코어 · 크레딧 · BGM · TAC 이름 · 데칼 · 관전. 3단계 = 비행 중 화면(점수 HUD · 지원 다이얼 · 무전 자막 · 경고 — 계기 글자는 그때 정한다). 번역 안 함: 디버그 패널, 무장 · 유닛 이름, 콜사인, 작전 지역 이름, 게임 제목.

### 6-172. 로드아웃 패널 특수무장 순서 통일 · 데칼 건 홀로그램 화살표
- **특수무장 순서**: 격납고(`ACO_Hangar`)와 브리핑실(`ACO_Hangar_Briefing`) 로드아웃 패널의 특수무장 목록 순서가 달랐다 — 카탈로그 동기화(`ACO_SpecialWeaponCatalog.AddToLoadout`)가 패널마다 없는 ID를 끝에 따로 붙였기 때문.
  - 이제 동기화가 모든 패널 목록(ID · 이름 · 설명 · 가격 · 아이콘 · 분류)을 정의 순서(`SortOrder`)로 정렬한다(정의에 없는 ID는 끝에 원래 순서로).
  - 정의 순서는 격납고 패널 기준: 4AAM 10, UGB 20, RKT 30, 4AGM 40, LAGM 50, LAAM 60, SASM 70, QAAM 80, HVAA 90, HPAA 100, LGB 110, LGR 120, LASM 130, FLAK 140, CCAA 150, ADAM 160, SCM 170, ECM 180, MMP 190, RGP 200(정의 프리팹 · 예시 코드 둘 다).
  - 해금 · 선택은 ID로 저장하므로 기록은 그대로. 기체 다이얼의 기존 모듈 순서는 바뀌지 않는다(새 모듈만 이 순서로 붙는다).
  - 무장 미리보기 모형(`PreviewVisuals`)은 격납고 패널에만 있다(브리핑실엔 모형이 없음 — 그대로).
- **데칼 건 화살표**: 내가 들고 있지 않을 때 건 위 0.22m에 튜토리얼과 같은 녹색 홀로그램 화살표(길이 0.16m, 가산 두 겹 + 링 두 개)가 아래를 가리키며 돌고 흔들리고 링이 맥박친다(`ACO_DecalGun.Marker`).
  - 화살표는 건의 자식이 아니라 스튜디오 루트 아래 — 건이 어디 어떻게 놓여도 월드 위쪽으로 곧게 선다. 8m 안에 있을 때만 움직인다.
  - 집으면 사라지고 놓으면 다시. 로컬(동기화 없음). `ACO_PrefabVersions` 데칼 스튜디오 5.

### 6-173. 브리핑 명단 카드에 각자의 특수무장 · 서포트
- 화면: 작전 콘솔 명단 칸을 4개(기체 수)로 줄이고 크게(590×395, 2열 × 2줄, 글자 최대 66). 카드 = 기체 · 조종사 · 상태 + 두 줄 `특수무장  LAAM` / `지원  급유 드론 x2`(없으면 없음, 아직 못 받았으면 ...). 번역 키 `flow.slot.special` · `flow.slot.support`(템플릿).
- 동기화(`ACO_GameFlow`): 고른 특수무장 · 서포트는 각자 저장 데이터에만 있다 — 명단에 든 사람은 0.5초마다 자기 값(칸, 특수무장 목록 번호, 서포트 번호, 남은 수)을 보고 바뀌었으면 `NetLoadout`으로 모두에게 알린다.
  - 받는 화면은 칸별로 둔다(동기화 변수 아님, 보낸 사람 playerId로 확인 — 칸이 바뀌면 옛 칸은 지운다). 새 사람이 들어오면 명단의 각자가 0.5~2.5초 뒤 다시 보낸다(`OnPlayerJoined`).
  - 무장 이름은 로드아웃 목록 번호 → `ACO_GameFlow.Loadout`(Bake — 격납고 패널, 두 패널 순서는 같다 6-172)의 표시 이름. 서포트 이름은 진행도 `GetCallName`(번역).
- `ACO_PrefabVersions` 작전 콘솔 3.

### 6-174. 지원 호출(서포트) 전용 아이콘
- 예전엔 일반 아이콘을 빌려 썼다(벙커버스터 = 폭탄 탭, 급유 드론 = 연료, 가디언 드론 = 체력). 전용 아이콘 셋을 `Tools/aco_icons.py`(같은 선 스타일)로 만들었다: `sup_strike`(떨어지는 폭탄 + 땅에 묻힌 벙커 + 충격선), `sup_tanker`(급유 드론 + 점선 급유 호스 + 작은 사다리꼴 드로그), `sup_guardian`(방패 안의 드론 + 방패 모서리에 막힌 미사일과 불꽃).
- 드론은 인게임 모델을 위에서 본 모양: 급유 드론 = 뾰족한 기수 + 넓은 삼각 날개(`TANKER_HALF`, 테두리), 가디언 드론 = 긴 동체 + 가운데 후퇴익 + 꼬리 작은 핀(`GUARD_HALF`, 채움). 급유 드론의 드로그는 작은 사다리꼴. `ACO_HUDBuilder.SupportCallIcons()`(CALL_* 순서)가 쓰고, Bake가 상점 `CallIcons` · 로드아웃 `SupportIcons`에 넣는다.
