# 지난 설계 — 다른 절로 합쳐지거나 바뀐 내용(기록용)

> 설계 문서의 한 분야. 목차·작성 규칙은 `Docs/DESIGN.md`. 절 번호는 예전 DESIGN.md 그대로(코드 주석의 "DESIGN.md 6-xx"가 가리키는 번호).

### 6-13. 페이즈 음성 → 무전 채터(6-19)로 합쳐짐 (`ACO_MissionVoice` 삭제 — 씬에 남은 컴포넌트는 셋업 검사 "프로그램이 빠진 UdonBehaviour" [고치기]로 지운다)

- 로컬 전용(동기화 없음): 관리자의 동기화 상태(페이즈 번호/클리어 수/유형/실패 여부/미션 상태)를 `PollInterval`(0.25초)마다 읽어 바뀐 순간에 튼다. 처음 본 상태(늦게 들어온 클라이언트)는 기준만 잡는다.
- 클립: 유형별 `StartClips`/`SuccessClips`/`FailClips`(칸 순서 = 1 타격, 2 SEAD, 3 점수 할당, 4 시한 표적, 5 제공권, 6 보스, 7 공중 호위, 8 지상 호위), 비면 공통 `DefaultStart/Success/Fail`, 미션 결과 `MissionComplete/MissionFailed`(선택).
- 언제: 페이즈 시작(배치 실패로 유형이 바뀌면 새 유형의 시작만), 페이즈 끝 — 실패(`GetLastPhaseFailed`, 호위 대상 격추/콘보이 전멸/시한 표적 도주)면 실패, 아니면 성공(시작 때 본 유형 기준). 마지막 페이즈 성공 뒤 `MissionComplete`, 제한 시간 실패면 `MissionFailed`.
- 무전 효과(선택): `PttStart`(PTT 누름 소리)를 먼저 틀고 `PttLead`(음수 = 그 소리 길이) 뒤에 음성, 음성이 끝나면 `PttEnd`(PTT 놓음 소리). 크기 `PttVolume`(음성 Volume에 곱함).
- 겹치면 줄을 세워 앞 음성이 끝나고 `Gap`(0.3초) 뒤 차례로, `MaxQueueDelay`(10초)보다 밀린 음성은 버린다. AudioSource는 2D(무전처럼 어디서나 같은 크기) — 메뉴가 만든다, Bake가 관리자/AudioSource를 연결.

### 6-36. 보너스 페이즈 모듈

> `ReplacesBuiltin`·변환 메뉴 언급은 중간 단계(6-38에서 지움).

- 모듈의 `BonusOnly`를 켜면 보너스 전용: 일반 무작위 선택에서 빠지고, 본 미션 클리어 뒤 보너스(관리자 `BonusPhaseChance`, `BonusMinPhases`)로만 나온다.
- 보너스 종류 고르기(관리자 `PickBonusType`, 오너): 후보 = 기본 슈퍼에이스(대신하는 모듈이 없고 풀에 쉬는 슈퍼에이스가 있으면 가중치 1) + 보너스 모듈(`Enabled`, `MinPhase` ≤ 본 미션 페이즈 수, 가중치 `Weight`). 가중치로 뽑고 모듈은 `IsFeasible`로 확인해 안 되면 빼고 다시. 고른 종류는 동기화 값 `_bonusType`에 남고 다음 페이즈 시작 때 그 종류로 시작한다.
- 보너스 공통 규칙은 관리자가 본다: 모듈 `BonusTimeLimit`(초)이 지나면 물러남(태그 철수, 보너스 없이 미션 클리어 — 배너 "PHASE n FAILED / 모듈 FailText", 무전 페이즈 실패 대사), 모듈이 `RESULT_FAIL`을 돌려줘도 물러남, `RESULT_REPLACE`면 건너뜀. 성공 보너스 = (`PhaseClearBonus` + `GetClearBonus()`) × `BonusScoreMultiplier`. 보너스 페이즈엔 시작 증원 웨이브·교착 웨이브가 없다. HUD 남은 시간은 모듈 `BonusTimeLimit`.
- 슈퍼에이스 보너스 = `ACO_PhaseTwins`(동기화 없음, `ReplacesBuiltin` = PHASE_BONUS) — 변환 메뉴가 관리자 `BonusEscorts`/`BonusTimeLimit`/`BonusClearBonus`와 무전 보너스 대사(시작·성공·페이즈 실패 칸)를 옮겨 만든다. 이름 `BONUS - {ACE}`(슈퍼에이스 이름으로 바뀐다).
- 새 보너스 종류는 `ACO_PhaseModule`을 상속해 `BonusOnly`를 켜고 관리자 아래에 두면 된다(Bake가 모듈 목록에 넣는다). 다음 후보: 에이스 편대, 스텔스·전자전 에이스, 공중 요새·포대 요새 등 슈퍼웨폰.
