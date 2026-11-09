# 미션·페이즈 — 생성기·진행·실패·게임 흐름·페이즈 모듈·난이도

> 설계 문서의 한 분야. 목차·작성 규칙은 `Docs/DESIGN.md`. 절 번호는 예전 DESIGN.md 그대로(코드 주석의 "DESIGN.md 6-xx"가 가리키는 번호).

### 6-2. 미션 생성기 ✅ (`ACO_MissionGenerator`)

- **풀 유닛의 SaccTarget 애니메이터**: `Target.controller`의 normal 상태는 클립 없이 Write Defaults로 되살리므로, 격추된 채 꺼졌다가 다시 켜지면 애니메이터가 다시 바인딩하며 파괴된 모습(모델 끔·파괴 모델 켬·락온 표적 끔)을 기본값으로 잡아 부활해도 락온이 안 됐다. Bake(`BakeTargetAnimators`)와 NPC 프리팹 빌더가 SaccTarget 애니메이터의 **Keep Animator State On Disable**을 켠다. 또 부활 때의 `SaccTarget.RespawnTarget()`은 2프레임 뒤에 부른다(막 켠 SaccTarget은 Start 전이라 예외로 멈춘다).

- **스폰 = 풀 재배치.** SAV가 `AAMTargets`를 빌드 때 굽기 때문에(2.3) 런타임 생성 유닛은 락온이 안 된다. `UnitPoolRoot`(비면 생성기 자신) 아래에 NPC를 넉넉히 미리 배치해 두면 빌드 시(또는 `ACO → Bake NPC References`) `Fighters`/`GroundUnits`로 수집된다. 풀 유닛은 씬에서 **활성**으로 둔다(Start가 한 번 돌아야 재활성화 후 동작).
- **흐름**: 월드에 들어오면 모든 클라이언트가 풀 유닛을 숨긴다(`HidePoolOnStart`, 유닛 Start 이후 2프레임) → 트리거로 `RequestStartMission()` → 전체 배치 → 미션 중 이벤트로 `RequestReinforce(지상 수, 전투기 수)`. 이미 배치를 받은 유닛은 숨기지 않으므로 늦게 들어와도 안전하다. 숨김 전 2프레임 동안은 편집기 배치 위치에 보일 수 있으니 풀은 눈에 안 띄는 곳에 모아 둔다.
- **진입점** (누구나 호출 → 오너가 실행):
  - `RequestStartMission()` — 풀 전체 부활 + 전체 배치. 페이즈 모드(`PhasedMode`, 기본 켬)면 배경 병력만(지상 그룹 `BackgroundGround`, 함대 `BackgroundShipGroups`, 전투기 `BackgroundFighters`) — 고가치 표적은 페이즈가 배치한다(6-4). 끄면 예전처럼 고가치 표적 → 방공 호위 → 지상 그룹 → 함대 → 전투기. `RequestRedeployAll()`/`RequestGenerate()`/생성기 Interact도 같다.
  - `RequestReinforce(지상 수, 전투기 수)` — **파괴된**(SaccTarget `Health <= 0`) 지상 유닛/전투기 중 일부만 같은 규칙으로 다시 배치(고가치 표적은 제외). 살아 있는 유닛은 그대로. `ReinforceAlsoUsesUnused`면 모자랄 때 꺼져 있던 풀 유닛도 쓴다. 인자 없는 `RequestReinforceDefault()`는 `ReinforceGroundCount`/`ReinforceFighterCount` 사용(UI 버튼용). 다른 클라이언트의 요청은 `[NetworkCallable] NetReinforce(int, int)`로 오너에게 간다.
- **권한/동기화**: 오너(기본 인스턴스 마스터)만 배치를 정한다. 유닛별 배열(사용 여부·위치·회전·집결 지점) + **배치 번호(serial)·배치 시각**을 Manual 동기화하고, 각 클라이언트는 serial이 바뀐 유닛만 적용한다 — 부분 재배치가 다른 유닛을 건드리지 않는 이유. 지상 유닛은 위치 동기화가 없으므로 이 배열이 곧 위치다. 늦게 들어온 클라이언트는 **자기가 들어오기 전에 배치된 유닛은 위치만** 맞추고 부활시키지 않는다(유닛별 배치 시각 vs 입장 시각) — 생사는 SaccTarget 체력 동기화가 맞춘다. 늦게 들어온 경우 유닛 Start 이후에 적용하려고 2프레임 늦춘다.
- **동기화 방식 통일**: VRChat은 GameObject 단위로 직렬화하므로, 한 오브젝트의 UdonBehaviour는 모두 같은 방식이어야 한다. SaccTarget/AI(Manual) 옆의 SaccEntity·ACO_StaticTarget 같은 NoVariableSync 컴포넌트가 Continuous로 남으면 오브젝트 전체가 4Hz 비신뢰 전송이 되어, 혼잡할 때 상태가 1~2초 늦게 도착한다(전투기가 튀던 원인). `ACO_SyncModeFixer`가 빌드 직전과 프리팹 생성 시 자동으로 Manual로 맞추고, 메뉴 `ACO/Fix Sync Modes (Scene + Prefabs)`로도 실행할 수 있다. 애드온 컴포넌트가 없는 오브젝트(SAV 기체)는 건드리지 않는다. **에디터 씬만 고쳐서는 안 된다**: UdonSharp의 `UpdateSyncModes`(씬 열기·플레이 모드 전환·빌드 후 정리 때 실행)는 옆에 Manual이 있어도 이미 Manual인 NoVariableSync 컴포넌트를 Continuous로 바꾸는 버그가 있다(else-if 조건). 그래서 실행할 때마다 Manual ↔ Continuous를 오가고, `SyncMethod` 설정자가 같은 오브젝트의 다른 UdonBehaviour까지 같은 값으로 바꾼다. 그래서 빌드되는 씬 사본에 마지막으로(`[PostProcessScene(1000)]`, UdonSharp 다음) 다시 맞춘다. 저장된 씬이 어느 쪽이든 빌드 결과는 Manual이다.
- **네트워크 ID 구성**: 씬 디스크립터는 네트워크 오브젝트마다 컴포넌트 구성을 저장한다. 프리팹에 UdonBehaviour를 더하면(예: 전투기에 `ACO_NPCFighterNet`) 빌드가 "Failed to assign network IDs"(IncompatibleTypes)로 멈춘다. `ACO_NetworkIDFixer`가 빌드 직전에 애드온 오브젝트만 ID는 그대로 두고 구성을 갱신한다(ClientSim·Network ID Utility와 같은 방식). 메뉴: `ACO/Fix Network IDs (after prefab changes)`.
  - **동기화 크기 줄이기**(늦게 들어온 사람에게 한꺼번에 가는 양 — 오너 업로드가 몰린다): 회전은 전부 방위뿐이라 `ushort`(0~65535 = 0~360°, 0.0055° 단위 — 함선의 "정수+0.5도" 방위도 정수 부분이 유지됨), 배치 번호 `byte`(같은지만 비교, 256번째에 한 바퀴), 태그/편대 코드/콘보이 행렬 거리/전차 앞선 거리 `short`(편대 코드 = 기함코드 × 16 + 슬롯, `LEADER_HVT` 1000 — 지상 풀·고가치 표적 각 1000 미만), 경유점 수 `byte`, 전차 항로는 수평 `Vector2`(높이는 전차가 `GroundRayHeight` 5000m에서 쏜 Raycast로 잰다). 유닛당 약 49 → 28바이트, 생성기 전체 약 5KB → 3KB대(40대/16기/전차 15 기준 추정).
  - **적용 나누기**: 바뀐 유닛 적용(스폰/숨김)은 한 프레임에 `ApplyPerFrame`(25)대까지, 나머지는 다음 프레임에 이어서 — 늦게 들어온 클라이언트(100여 대를 한 번에 받음)와 오너의 큰 배치(미션 시작)의 순간 끊김을 나눈다. 적용 전 유닛은 아직 부활 전이라 체력 0으로 보여도 "살아 있는 걸 본 뒤 파괴"가 아니므로 격추로 세지 않는다.
- **지상**: 공역 안 무작위 중심 → `RaycastHeight`에서 아래로 Raycast(`GroundLayers`). 공역 밖·안전지대(콜라이더 바운딩 박스 + `SafeZoneMargin` 5km)·급경사(`MaxGroundSlope`)·너무 낮음(`MinGroundHeight`)이면 다시. 그룹 4~8대: 콘보이(진행 방향 일렬, `ConvoySpacing`, 같은 방향) 또는 부대(반경 `ClusterRadius`, 최소 간격 `MinUnitSpacing`, 무작위 방향). 유닛마다 지면을 다시 잰다. 자리를 못 찾으면 3번 더 시도 후 남은 유닛은 이번 미션에서 끈다.
- **안전지대 여유** `SafeZoneMargin` **5km**(지상 유닛과 적 함선 공통, 예전 기본 500m는 Bake가 5km로): 안전지대(콜라이더 바운딩 박스) 경계에서 이만큼 밖에만 놓는다 — 항모·기지에서 막 이륙한 기체가 대공 무기에 바로 잡히지 않게. 적 함선(전함·구축함 — `SampleSea`)은 순항 원 전체(기함 순항 반경 + 호위 편대 폭)의 중심과 둘레 확인점 모두. 안전지대 근처가 좁은 지형이면 해당 페이즈가 다른 종류로 바뀔 수 있다(그땐 값을 줄인다).
- **고가치 표적**(페이즈 모드를 끈 경우의 전체 배치 — 페이즈 모드에선 타격 페이즈가 같은 규칙으로 플레이어 근처에 놓는다): 전체 배치 때 `HighValueCount`(2)개를 서로 `HighValueMinSeparation`(8km) 이상 떨어뜨려 지면에 놓고, `DefenseChance` 확률로 반경 `DefenseRadius`(450m) 원 위에 방공 호위 `DefenseSizeMin~Max`(3~5)대(지상 풀에서, 바깥을 바라보게)를 두른다. 호위도 `MaxGroundUnits` 한도에 포함된다. 표적은 `ACO_StaticTarget` — 무장 없음, 체력/피격은 SaccTarget, 파괴 시 `HideOnDeath` 숨기고 `ShowOnDeath`(잔해) 켬, 히트박스 끔. `TargetName`/`ScoreValue`/`IsDestroyed()`는 미션 목표 판정용.
  - 프리팹(`ACO → Create Prefabs...`의 공장/벙커): `NPC_Factory`(40x15x30, 체력 300, 장갑 1), `NPC_Bunker`(18x5x18, 체력 400, `ArmorStrength` 4, `NoDamageBelow` 20 — SAV 기총처럼 작은 데미지는 무시, 폭탄/미사일로 부숴야 함). SaccEntity 없이 SaccTarget + 레이어 30 마커만으로 락온/피격된다. ⚠ SAV 미사일 기본값(`AAMDamage` 1 = FullHealth의 100%)은 체력과 무관하게 한 발에 부수므로, 여러 발이 필요하게 하려면 플레이어 무장의 `AAMDamage_AbsoluteMode`를 켠다.
- **전투기**: 4기(`FourShipChance`) 또는 2기 편대, 핑거팁 대형(`FormationSpacing`). 편대마다 **임무 지점**:
  - 엄호(`CoverGroundChance`): 배치돼 있고 살아 있는(이번에 놓은 것 포함) 지상 유닛/고가치 표적 중 하나의 상공 `CoverAltitudeMin~Max`(1.5~3km).
  - CAP: `CapPoints` 중 하나의 위치(고도 포함). 둘 다 없으면 경계에서 `IngressInset` 안쪽 무작위 지점.
  - 스폰: 전체 배치는 `FullDeploySpawnInside`면 임무 지점에서 `InsideSpawnDistanceMin~Max`(3~8km) 떨어진 공역 안, 증원은 공역 경계 바깥 `SpawnOutsideMin~Max`에서 임무 지점을 향해.
  - AI는 순찰 시 임무 지점으로 직행, 가까워지면 `HoldRadius`로 선회 대기(웨이포인트/대기 지점보다 우선), 타겟을 만나면 평소대로 교전하고 끝나면 돌아간다. 편대 유지 비행은 없다(같은 지점·속도라 대략 뭉쳐 다니고, 분리 기동이 간격을 유지).
- **유닛 계약**: 전투기는 `MissionSpawn(pos, rot, ingress, revive)`/`MissionDespawn()`. 지상 유닛(SAM/대공포/구축함, 향후 탱크)과 고가치 표적(공장/벙커/전함)은 덕 타이핑 — 필드 `MissionSpawnPosition`/`MissionSpawnRotation`/`MissionRevive` + 이벤트 `MissionSpawn`/`MissionDespawn`.
- **실행**: 위 진입점, 생성기 오브젝트 Interact = 미션 시작(`AllowInteract`, 콜라이더 필요), `GenerateOnStart`(배치가 없을 때 오너가 미션 시작).
- 기즈모: 공역(노랑), 집결 범위(주황 점선), 안전지대 + 여유(초록), CAP 지점(하늘색 원 + 고도), 플레이 중 사용된 유닛에 점선과 고가치 표적(★ + 방공 링 반경).
- ⚠ 공역 기본값은 ±200km. 지형(콜라이더)이 없는 곳은 Raycast가 실패해 지상 유닛이 덜 놓인다 — 실제 지형 범위에 맞춘다. 원점에서 수십 km 이상 떨어지면 float 정밀도 때문에 렌더링/물리 떨림이 생길 수 있다.

### 6-4. 미션 진행 — 페이즈 ✅ (`ACO_MissionManager`)

- **미션 = 페이즈 1~5개**. 페이즈 수는 시작할 때 고른다: 작전 콘솔(`ACO_MissionConsole`, 버튼마다 `ACO_MissionStartButton.PhaseCount`) → `RequestStartMissionPhases(n)`. 생성기 클릭/`GenerateOnStart`/관리자 Interact로 시작하면 `DefaultPhaseCount`(3).
- **페이즈 종류는 무작위**(`PhaseTypeWeights` 가중치, 0이면 제외), **직전 페이즈와 같은 종류는 나오지 않는다**. 보스는 `BossMinPhase`(3) 페이즈부터, 시한 표적(긴급)은 `TstMinPhase`(2) 페이즈부터(첫 페이즈에는 안 나옴). 종류는 **각 페이즈가 시작되는 순간** 고르고(그때의 풀/플레이어 위치로 판단해야 하므로) 동기화한다(`_phaseTypes`).
- **배치 가능 확인**(`IsPhaseFeasible`): 뽑힌 종류가 지금 안 되면 빼고 다시 뽑는다. 생성기의 동기화된 배치 상태와 정적인 지형만 보므로 관리자 오너가 확인한다.

  | 종류 | 확인 |
  |---|---|
  | 타격 | 쓸 수 있는 육상 고가치 표적 + 근처(`TargetDistance`) 지면, 또는 함선 + 근처 바다(함대째) |
  | 방공망 제압 | 지상 유닛 3대 이상 + 근처 지면 |
  | 점수 할당량 | (지상 3대 + 근처 지면) 또는 전투기 2기 |
  | 시한 표적 | 지상 1대 + `TstDistance` 안 지면 |
  | 제공권/보스 | 전투기 2기 |
  | 호위 | 공중 호위 대상 1기 + 항로 |
  | 지상 호위 | 지상 호위 대상 1대 + 전차 1대 + 지상 항로(행렬 길이 포함) |

  생성기 조회: `CountFreeLandGround/CountFreeHvt(ships)/CountFreeFighterUnits/CountFreeMobileUnits/CountFreeEscorts(moveMode)`, `HasGroundNear/HasShipSiteNear/CanBuildEscortRoute`("쓸 수 있는" = 쓰지 않던 것 + 파괴된 것).
- **확인한 그대로 배치**: 확인이 찾은 결과를 배치 계획(`_plan*`)으로 남기고, 그 종류로 시작할 때 그대로 넘긴다 — 같은 클라이언트(기본: 둘 다 인스턴스 오너)에서는 **확인이 통과하면 배치도 반드시 성공**한다.
  - 자리(타격/방공망/사냥터/첫 시한 표적): 확인이 찾은 좌표(`GetCheckedPoint`)를 **정한 자리**로 넘긴다 — `RequestDeployObjective`의 `minDist < 0`이면 생성기는 그 자리만 같은 Raycast로 다시 확인해 놓는다. 타격은 `tier` 칸에 첫 표적 종류(0 육상 / 1 함선)를 싣고, 두 번째 표적부터는 첫 표적 근처(`HighValueMinSeparation` 0.5~1.5배)에서 찾는다.
  - 항로(호위/지상 호위): 6점 항로는 네트워크 이벤트에 못 실으므로 **무작위 씨앗**을 넘긴다. 확인(`CanBuildEscortRoute(…, seed)`)과 배치(`RequestDeployEscort(…, routeSeed)`)가 같은 씨앗으로 `Random.InitState` 후 같은 함수·같은 인자로 항로를 만들므로 같은 항로가 나온다(항로 앞에 다른 무작위 호출이 끼면 안 된다). 쓴 뒤엔 서버 시각으로 무작위를 다시 흩뜨린다.
  - 계획은 한 번만 쓴다. 마지막 수단(가중치로만 고름)은 계획 없이 예전처럼 무작위로 찾는다.

| 종류 | 목표 | 배치(생성기 `RequestDeployObjective`) | 클리어 |
|---|---|---|---|
| STRIKE 타격 | 고가치 표적 `StrikeTargets`(2)개 | `OBJ_HVT` — 표적 + 방공 호위 링(링은 목표 아님) | 표적 전부 파괴 |
| SEAD 방공망 제압 | 방공 시설 `SeadUnitsBase + PerPhase·(n−1)`대 | `OBJ_SITE` — 한 지점 둘레 링(반경 `DefenseRadius × SiteRadiusScale`) | 시설 전부 파괴 |
| SCORE ATTACK 점수 할당량 | 이 페이즈에서 `QuotaScore × 배율` 획득 | `OBJ_HUNT` 사냥터(지상 `QuotaGround`) + `OBJ_FLIGHT`(`QuotaFighters`) | 페이즈 점수 ≥ 할당량(대상 자유) |
| TIME SENSITIVE 시한 표적 | 표적 `TstCount`(3)개가 하나씩 등장 | `OBJ_GROUND` 1대씩, 플레이어 `TstDistanceMin~Max`(5~12km) | 표적마다 처음 보인 순간부터 `TstTimeLimit`(90초), 못 잡으면 도주(철수). `TstInterval`(8초) 뒤 다음. 다 지나가면 종료 — 잡은 수 × `TstBonus`. **하나라도 도주하면 그 즉시 미션 실패**(`FAIL_TARGET_ESCAPED` → HUD "TIME-SENSITIVE TARGET ESCAPED", 디브리핑 "(TARGET ESCAPED)"). 배치 실패로 건너뛴 표적은 놓친 것으로 치지 않는다 |
| AIR SUPERIORITY 제공권 | 적 편대 `AirFightersBase + PerPhase·(n−1)`기 | `OBJ_FLIGHT` — 플레이어 쪽으로 진입, 숙련도 무작위 | 전부 격추 |
| ACE SQUADRON 보스 | 이름 붙은 에이스 `BossFighters`(4)기(`BossNames` 무작위) | `OBJ_FLIGHT` 숙련도 에이스 고정, 4기 편대 | 전부 격추 |
| CONVOY 지상 호위 | 아군 콘보이 `ConvoySize`(3)대를 지상 항로 끝(`ConvoyRouteLength` 6km)까지 | `RequestDeployEscort`(지상, 여러 대) — 항로 옆 전차 매복조 `ConvoyAmbushBase + PerPhase·(n−1)`곳 + 방공 시설 `ConvoySites`(1) | 살아남은 차량이 모두 도착 = 클리어(+ `ConvoyBonus` × 생존 비율 × 배율), 전멸 = **미션 실패**(`FAIL_CONVOY_LOST` → "CONVOY DESTROYED", 6-10) |
| ESCORT 호위 | 아군 수송기를 항로 끝(`EscortRouteLength` 32km)까지 | `RequestDeployEscort` — 항로 + 항로 아래 방공 시설 `EscortSites`(2, 지면이 있는 곳만), 진행에 맞춰 요격 편대 | 도착 = 클리어(+ `EscortBonus` × 남은 체력 × 배율), 격추 = **미션 실패**(`FAIL_VIP_LOST` → "ESCORT TARGET SHOT DOWN", 6-9) |

- **흐름**: 미션 시작 = 생성기 전체 배치(페이즈 모드 `PhasedMode`: 고가치 표적 없이 배경 병력 `BackgroundGround`(12)/`BackgroundFighters`(4)만) → `FirstPhaseDelay`(5초) → 페이즈 시작(목표 배치) → 클리어 → `PhaseTransitionDelay`(10초) → 다음 페이즈 … 마지막 페이즈 클리어 = 미션 클리어.
- **목표 위치**: 페이즈 시작 순간 **플레이어들의 평균 위치** 기준. 지상 목표는 `TargetDistanceMin~Max`(12~30km), 편대는 `FlightDistanceMin~Max`(15~25km)에서 나타나 플레이어 상공으로 온다 — 공역이 40만 m여도 페이즈마다 이동 시간이 일정하다.
- **목표 태그**: 생성기가 목표 유닛에 태그(`(페이즈+1)×100 + 하위번호`, 동기화 배열 `_groundTag/_hvtTag/_fighterTag`)를 단다. 관리자는 `GetActiveObjectiveTag()`와 태그가 같은 유닛을 세고, 마커/작전판도 같은 태그로 목표를 표시한다. 마지막 목표 태그(`_protectTag`)의 유닛은 파괴돼도 증원이 재사용하지 않는다(판정 전에 사라지지 않게). 목표 후보는 쓰지 않던 풀 유닛 → 파괴된 유닛 순(살아 있는 유닛은 건드리지 않음).
- **달성 판정**: 태그 유닛이 체력 0이고 (이번 배치에서 살아 있는 걸 봤거나 배치 후 `ObjectiveSettleTime`(10초) 경과) — 부활 동기화 전 체력 0 오판과, 오너가 바뀐 직후 "살아 있는 걸 못 봄"으로 영영 안 끝나는 문제를 둘 다 막는다.
- **대체**(비상용): 확인을 통과했는데도 목표가 `DeployTimeout`(20초) 안에 하나도 안 보이면 — 관리자와 생성기의 오너가 갈라졌는데(마스터가 나가면 오브젝트마다 따로 넘어간다) 배치 요청이 오너 이전과 겹쳐 사라지는 등 — 페이즈를 건너뛰지 않고 **그 종류를 막은 채(`_phaseBlocked`) 다른 종류로 같은 페이즈를 다시 시작한다**(시도마다 태그 하위 번호 20+시도 — 이전 시도의 유닛과 섞이지 않게, 이전 태그는 거둬들임, 페이즈 시작 웨이브는 다시 보내지 않음). HUD 배너와 작전판에 바뀐 종류가 다시 뜬다. 시한 표적은 첫 표적부터 못 놓을 때만 대체하고, 이후 표적은 그 표적만 건너뛴다. 확인을 통과한 종류가 하나도 없으면 가중치로만 고르고, 바꿀 종류가 전혀 없을 때만 보너스 없이 넘어간다(마지막 수단).
- **웨이브**: ① 페이즈 시작(2페이즈부터, `WaveOnPhaseStart`, 시한 표적 제외) ② 교착(적 전력 ≤ `StallStrengthRatio`이고 `StallTime` 동안 진전 없음). 목적지는 지금 목표 중 살아 있는 하나(없으면 페이즈 기준점). `WaveCooldown`/`MaxWaves`, 규모/숙련도 자동 규칙과 수동 목록은 이전과 같다.
- **추적 시작**: 생성기 전체 배치 번호(`GetFullDeployCount`)가 바뀌면 새 미션(동기화된 `_trackedFullDeploy` 기준이라 관리자 오너가 바뀌어도 진행 중인 미션을 새로 시작하지 않는다).
- **이벤트**(모든 클라이언트, 한 번씩): `ACO_OnMissionStart`, `ACO_OnPhaseStart`, `ACO_OnObjectiveDestroyed`(목표 하나/시한 표적 하나), `ACO_OnPhaseComplete`, `ACO_OnWaveIncoming`, `ACO_OnScoreChanged`, `ACO_OnMissionComplete`, `ACO_OnMissionFailed`. 늦게 들어온 클라이언트는 지난 이벤트를 몰아서 받지 않는다.
- **조회**: `GetPhaseCount/GetPhaseIndex/GetPhaseType(k)/GetCurrentPhaseType/IsPhaseActive/GetPhasesCleared/GetNextPhaseSeconds`, `GetPhaseName(type)/GetPhaseDescription(type)`, `GetObjectivesDone/Total`(종류별: 파괴 수, 얻은 점수/할당량, 잡은 시한 표적), `GetTstHits/GetTstCount/GetTstRemainingSeconds`, `GetBossName`, `GetActiveObjectiveTag`, `GetPhaseMultiplier(k)/GetScoreMultiplier`, `GetLastPhaseBonus/GetPhaseBonusTotal`.
- 생성기 API 추가: `RequestDeployObjective(종류, 태그, 수, 숙련도(-1 = 무작위), 기준점, 최소 거리, 최대 거리)`, `RequestReleaseTag(태그)`, 유닛별 `GetUnitTag/GetUnitDeployTimeMs/GetUnitPosition`.
- 페이즈 종류 가중치 `PhaseTypeWeights`는 8칸 [타격, 방공망, 할당량, 시한 표적, 제공권, 보스, 호위, 지상 호위] — 씬에 예전 짧은 배열이 남아 있으면 빠진 종류는 1로 친다.
- **풀 크기 기준**: 지상 = 배경 12 + 방공망 4~8 + 타격 호위 링 3~5×2 + 사냥터 8 + 웨이브(파괴된 유닛 재사용) → 30대 이상 권장. 전투기 = 배경 4 + 제공권 4~8 + 보스 4 → 12기 이상(호위 요격 편대 2~4기 × 3회는 파괴된 전투기 재사용). 호위 대상 = 수송기 1기 + 콘보이 차량 `ConvoySize`(3)대 이상. 전차 = 순찰 소대 `BackgroundTankPlatoons × TankGroupSize`(6) + 매복조 최대 3~4곳 × 3대 → 15대 이상 권장(파괴된 전차 재사용). 고가치 표적 = `StrikeTargets`(2) 이상(파괴된 표적은 다음 타격 페이즈에 재사용). 함선은 지상 30대에 포함하지 않는다(별도, 6-8).

### 6-5. 실패 조건 / 팀 점수 ✅ (`ACO_MissionManager`)

- **실패**: 제한 시간 = `TimeLimitPerPhase`(420초) × 페이즈 수(0이면 제한 없음)를 넘기면 `STATE_FAILED`, `ACO_OnMissionFailed`.
- **팀 공통 점수**(플레이어별 판정 없음): 오너가 1초마다 파괴를 세어 `_teamScore`를 동기화.
  - 기본 점수: 전투기 `ScoreFighterByTier` [신병 100, 베테랑 200, 에이스 500], 지상/고가치 표적은 각자의 `ScoreValue`(SAM 150, 대공포 100, 공장 500, 벙커 800).
  - **페이즈 배율**: 격추 점수와 페이즈 보너스에 `PhaseScoreMultiplier` [1, 1.25, 1.5, 1.75, 2]를 곱한다(지금 페이즈 기준, 첫 페이즈 전은 1페이즈 배율). 뒤 페이즈일수록 같은 격추가 더 비싸다. 할당량도 배율을 곱하므로 필요한 격추 수는 같다.
  - **페이즈 클리어 보너스** `PhaseClearBonus`(500) × 배율. 시한 표적은 잡은 수 × `TstBonus`(300) × 배율(+ 하나도 놓치지 않으면 클리어 보너스).
  - **중복 방지**: 유닛마다 "살아 있는 걸 본 배치 번호"/"점수를 준 배치 번호" — 같은 배치 번호에서 살아 있다가 파괴됐을 때 한 번만.
- **미션 클리어 보너스**: `ClearBonus`(1000) + 남은 시간 1초당 `TimeBonusPerSecond`(5).
- **랭크**: 기준 = `RankSPerPhase`(2500)/`RankAPerPhase`(1600)/`RankBPerPhase`(800) × 고른 페이즈들의 배율 합(3페이즈면 ×3.75 → S 9375). 이상이면 S/A/B, 그 아래 C, 실패 F — 페이즈 수가 달라도 같은 기준으로 공평하게.
- 조회: `GetTeamScore`, `GetFighterKills/GetGroundKills`, `GetClearBonus/GetTimeBonus/GetPhaseBonusTotal`, `GetRankLetter`, `GetRemainingSeconds/GetElapsedSeconds/GetTimeLimitMs`, `GetKillScore(카테고리, i)`(배율 포함, 작전판이 쓴다).

### 6-15. 게임 진행 — 참가 명단 / 출격 준비 / 일제 출격 ✅ (`ACO_GameFlow`, `ACO_FlowButton`, `ACO_Teleporter`, `ACO → Create Game Flow`)

- **출격 카운트다운 잠금**(모두 READY → `AllReadyDelay` 10초): 리롤·페이즈 변경에 더해 **READY 해제, 새 JOIN(LEAVE는 된다), 무장 변경·해금 구매(`ACO_Loadout.Flow`), 강화 구매(`ACO_Shop.Flow`)**를 막는다(`IsLoadoutLocked` = 카운트다운 또는 시작 중 — 버튼은 거절음 + 안내, JOIN은 LOCKED, READY는 LAUNCHING... 회색). 오너 쪽(`NetJoin`, `NetToggleReady`)도 같은 조건으로 거른다. 패널마다 **카운트다운 판**(`ACO_FlowCountdown` — 패널 전체를 덮는 반투명 판 + 가운데 큰 숫자 "ALL READY - LAUNCH IN / 7 / LOADOUT AND UPGRADES LOCKED", 클릭은 막지 않는다)을 Bake가 상태 글자가 있는 캔버스마다 만들고 `CountdownOverlays/CountdownTexts`에 넣는다 — 매 프레임 초가 바뀔 때만 글자를 고친다. 소리(선택): `AllReadySound`(카운트다운이 시작되는 순간 한 번, 늦게 들어온 사람은 안 남), `CountdownTickSound`(마지막 `TickSeconds` 5초 동안 초마다).
- **장소**: 대기실(브리핑 + 격납고 한 방, VRChat 스폰 — 작전 콘솔·격납고 패널·작전 테이블을 함께 둔다) → 필드(항모). 이동은 자유 — `ACO_Teleporter` 버튼(UI 또는 콜라이더)으로 언제든 오간다. 격납고를 따로 두고 싶으면 `HangarPoint`를 넣으면 된다(예전 방식).
- **참가 명단**(수동 동기화, 칸 = 기체 `Aircraft[k]`, 최대 8, 기본 4): 로비 콘솔의 JOIN/LEAVE. 명단 밖은 관전자. 미션 도중에도 빈 칸이면 JOIN(난입). **미션이 끝나면 명단·준비를 비운다.** 나간 플레이어는 지운다. 요청은 `NetworkCalling.CallingPlayer`로 오너가 처리.
- **시작**: 로비 콘솔 페이즈 버튼은 다음 미션의 페이즈 수만 고른다(`ACO_MissionStartButton.Flow`, 게임 진행이 없으면 예전처럼 바로 시작). 무장·강화 후 READY → **명단 전원이 READY면 `AllReadyDelay`(10초) 출격 카운트다운**(`_launchEndMs`, 동기화) — 그동안 리롤·페이즈 변경을 잠그고(버튼은 안내 메시지), 누가 READY를 풀거나 새로 들어오면(전원 준비가 깨지면) 취소 → 끝나면 오너가 미션을 시작하고 `NetLaunchAll`로 일제 출격.
- **출격**(각자 자기 화면): 내 칸 기체의 오너 → SAV가 오너를 알아채면(`EntityControl.IsOwner`) 갑판의 내 칸 위치(`DeckPoints[k]`)로 순간이동(`ShouldTeleport`, `SetNoGsFor`, `SFEXT_O_VehicleTeleported` — SAV `SaccVehicleTeleporter`와 같은 방식) → 조종석(`SaccVehicleSeat.Interact`). 격추되면 SAV가 원래 위치(= 보관 장소)로 리스폰 → 격납고의 LAUNCH로 재출격(RESPAWNING 중엔 대기).
- **지형 리롤**: 브리핑(미션 전)에 명단에 있는 사람이 로비 콘솔의 REROLL TERRAIN으로 지형을 새로 만든다(`RerollCooldown` 30초 — 지형 + 병력 미리 배치라 무겁다, 리롤하면 READY가 풀린다). 명단 제목에 지형 프리셋(`TerrainNames`, 세우는 중이면 GENERATING). 미션은 **지금 지형 그대로** 시작한다 — 게임 진행이 시작할 때 생성기의 `RegenerateTerrainEachMission`을 끄고, 생성기는 지형이 다 세워질 때까지 기다렸다 배치한다. 미션이 끝나면 참가자 기체가 모두 내린 뒤 새 지형(`NewTerrainAfterMission`, 비행 중에 섬이 바뀌지 않게).
- **보관**: 참가자 기체의 원래 위치는 안 보이는 보관 장소. 내려서 비어 있는 기체가 보관 장소 밖에 `IdleReturnTime`(30초) 있으면 그 기체의 오너가 `MoveToSpawn`.
- **관전자 탑승 차단**: 참가자 기체의 파일럿 좌석(`SaccVehicleSeat.IsPilotSeat`)은 시작할 때 `DisableInteractive` — 눌러서는 못 타고 LAUNCH(좌석 `Interact()`를 코드로 직접 부름)로만 탄다. 비행 중 명단이 비워져도 내리지 않는다.
- **패널**: 로비 콘솔(`ACO_MissionConsole` 2.4 × 1.5m) — 위 = **상태 배너**(`ACO_FlowBanner`: 큰 상태 "BRIEFING n/m READY"·"ALL READY - LAUNCH IN n"·"MISSION IN PROGRESS" + 내 상황에 맞춘 "NEXT: ..." 한 줄 + 자동 시작 카운트다운 + 거절 안내 빨강 6초), 왼쪽 = **작전 카드**(공용 설정 — 페이즈 −/+ 버튼과 큰 숫자 `ACO_FlowPhases`, "ABOUT n MIN"(`MinutesPerPhase` 5분 × 페이즈) + "SET BY 이름"/LOCKED `ACO_FlowPhaseInfo`, 지형 이름 + "NEW TERRAIN BY 이름" `ACO_FlowTerrain`, NEW TERRAIN 버튼 — 바꾼 사람은 `_phasesBy`/`_terrainBy` 동기화, 미션 뒤 자동 새 지형은 이름 없음), 오른쪽 = **명단 칸 카드** 8장(`ACO_FlowButton` Action 6 + `Slot` — 기체 수보다 많은 카드는 숨김. 칸 이름 = `SlotNames` 또는 기체 오브젝트 이름. 빈 칸 "+ OPEN - TAP TO JOIN"을 누르면 그 칸으로 참가(`NetJoinSlot`), 이미 명단이면 미션 전·기체에서 내린 뒤 칸 바꾸기(준비 풀림). 색: 빈 칸/다른 사람/내 칸(노랑, "(YOU)")/READY(초록)/AIRBORNE(파랑)), 아래 = **내 행동 버튼**(Action 7: 명단 밖 JOIN(미션 중 빈 칸이 없으면 SPECTATE) → 미션 전 READY/CANCEL READY → 미션 중 LAUNCH) + LEAVE(Action 8). 페이즈 −/+ = Action 4/5. 격납고(`ACO_Hangar` 2.8 × 1.9m) 아래 = 명단·준비 글자(`ACO_FlowStatus`) + READY + LAUNCH(그대로). 글자·버튼은 Bake가 이름으로 찾아 게임 진행에 연결하고, 출격 카운트다운 판은 상태 글자·배너가 있는 패널마다 만든다. 옛 콘솔(페이즈 1~5 버튼 + 글자 명단)도 계속 동작한다(셋업 검사가 새 프리팹을 권한다).
- **화면 암전**(`ACO_ScreenFader`, `ACO → Create Screen Fader`, 씬에 하나): 머리를 감싸는 검은 구(`ACO/MarkerOverlay`, 렌더 큐 4900 — 마커·HUD보다 위)의 알파를 0.3초 올리고 → 순간이동 → 0.4초 유지 → 0.5초 내린다. `ACO_Teleporter`, 관전, 브리핑실 귀환, 출격(탑승 전 어두워지고 오너 대기 중엔 유지)이 거친다.
- **귀환**: 미션이 끝나면(성공/실패) `ReturnDelay`(8초) 뒤 각 클라이언트가 자기만 브리핑실(`BriefingPoint`)로 — 단 **걸어서 안전지대(항모·기지 — 진행도 `SafeZones`) 안에 있으면 옮기지 않는다**. 참가자 기체에 탄 사람은 안전지대 안이어도 옮긴다. **기체에 탄 채 옮길 때는 모든 순간이동이 같다**(`TeleportLocal` — 귀환·격추 복귀·관전 자리): 암전을 먼저 시작하고(`Fade`) 다 어두워진 뒤(`FadeOutTime` + `ExitMargin` 0.05초) 내리고, 2프레임 뒤 옮긴다 — 그때 암전은 아직 어두운 채(`HoldTime` 0.4초)라 바로 옮기고 어두운 시간을 다시 센 다음 밝아진다. 내리는 모습·순간이동이 보이지 않는다. 내리기는 한 번만(못 내렸어도 되풀이하지 않는다). 암전이 없으면 바로 내리고 옮긴다.
- **관전**: 미션 중 명단 밖 사람의 격납고 버튼은 LAUNCH 대신 **SPECTATE** — 누르면 갑판(`SpectatePoint`)으로. 관전 카메라 등은 나중에.

- **여러 명 플레이 대비**(6-15 보강):
  - **브리핑 준비 완료일 때만 READY**: 생성기가 `_deploying`(배치 요청 대기 중, 동기화)을 알리고, 게임 진행은 지형이 다 세워지고 미리 배치가 그 지형(마지막으로 요청한 씨앗 `_expectedSeed`)에 끝났을 때만 READY(켜기)·리롤을 받는다. 그동안 버튼은 PREPARING / TERRAIN GENERATING. 전원 READY·카운트다운도 준비가 끝나야 시작. 미리 배치가 없고 생성기가 쉬고 있으면 오너가 15초마다 다시 요청(페일세이프 — 미션 뒤 새 지형을 끈 경우, 생성기 오너가 배치 도중 나간 경우 새 오너가 `_deploying`을 푼다). 그래서 미션 시작 때 다시 배치하는 경로(StartMissionDeploy의 RedeployAll)는 정상 흐름에선 쓰이지 않는다.
  - **입장 직후 리롤 금지**: 월드를 연 오너가 시작할 때 리롤 쿨타임을 센다 — 처음 `RerollCooldown`(30초) 동안 리롤 불가(첫 지형·미리 배치와 겹치지 않게).
  - **시작 잠금**: 시작을 요청하면 미션이 RUNNING이 될 때까지(최대 `StartTimeout` 45초) `_startingMs`(동기화)로 READY·리롤·페이즈 변경을 잠근다 — 중복 시작 방지.
  - **준비 카운트다운**: 명단 전원 READY면 10초 출격 카운트다운, 절반 이상이면 `ReadyCountdown`(60초) 뒤 READY 안 한 사람을 명단에서 빼고(관전자) 남은 전원으로 같은 10초 카운트다운. 밑돌면 취소.
  - **비행 중 잠금**: 참가자 기체가 하나라도 떠 있으면(미션 직후) READY·리롤을 받지 않는다(비행 중에 섬이 바뀌지 않게). 비행 중엔 LEAVE 불가.
  - **JOIN 칸 배정**: 빈 칸 중 기체가 비어 있는 칸을 먼저(명단에서 빠진 사람이 아직 그 기체를 타고 있을 수 있다).
  - **격추·탈출 복귀**: 미션 중 내 기체가 터졌거나 안전지대 밖에서 내렸으면 `ShotDownReturnDelay`(5초) 뒤 `HangarPoint`(비면 `BriefingPoint` = 대기실)로.
  - **보관 복귀 = SAV 전체 리스폰**(`SaccEntity.SendRespawn`, 모든 클라이언트): 탄약·연료·체력까지 다시 채운다(`MoveToSpawn`은 위치·체력만).
  - **명단 밖 시작 차단**: 게임 진행이 시작할 때 미션 관리자·생성기의 `AllowInteract`를 끈다.
  - **오너 교대**: 리롤 쿨타임(`_lastRerollMs`)·미션 뒤 지형 예약(`_terrainPending`)을 동기화해 오너가 바뀌어도 이어진다. 오너가 바뀌는 순간의 JOIN/READY는 사라질 수 있다(다시 누르면 된다).

### 6-25. 폭격기 요격 페이즈 ✅ (`PHASE_INTERCEPT`, 프리팹 `NPC_Bomber`, `ACO_EscortUnit.Hostile`)
- **흐름**: 호위 페이즈를 뒤집은 것. 적 폭격기 `InterceptBombersBase`(3) + `InterceptBombersPerPhase`(0.5) × 페이즈 번호 대가 **삼각 대형**으로 도시 상공을 향해 날아온다(선두 뒤로 줄마다 좌우 한 대씩 — 생성기 `BomberFormationBack` 90m 뒤, `BomberFormationSide` 80m 옆; 유닛별 동기화 `_escortLateral`, `ACO_EscortUnit`이 진행 방향 오른쪽으로 그만큼 비켜 난다. 콘보이는 그대로 한 줄)(항로 `InterceptRouteLength` 18km, 폭격기 110m/s → 약 2분 40초). 처음 폭격기가 보이면 선두 둘레에 적 호위 전투기 `InterceptEscortsBase`(2) + `InterceptEscortsPerPhase`(1) × 페이즈 번호 기(목표 아님). **전부 격추하면 클리어**(페이즈 클리어 보너스), **한 대라도 도시 상공(항로 끝)에 닿아 폭탄을 떨어뜨리면 그 즉시 미션 실패**(`GetFailReason` = `FAIL_CITY_BOMBED` → HUD "THE CITY HAS BEEN BOMBED", 디브리핑 "(CITY BOMBED)"). 실패 사유 글자는 `ACO_MissionManager.GetFailReasonText`가 정한다. **실패 조건이 있는 페이즈(호위·지상 호위·시한 표적·요격)는 모두 실패하면 미션 자체가 실패한다** — 보너스 없이 넘어가는 실패는 보너스 페이즈 시간 초과(슈퍼에이스가 물러남, 미션은 이미 클리어)뿐.
- **폭격기** = 호위 대상과 같은 항로 비행 유닛(`ACO_EscortUnit`)에 `Hostile` 켬: 락온 마커는 적 항공기 레이어(29), 기총 막기 없음, 플레이어 범위 피해를 받는다(아군 호위 대상만 범위 피해에서 빠진다). 격추 점수 `BomberKillScore`(400) × 배율, 전투기 격추 수에 센다. HUD 마커 "BMR"(목표 = 빨강), 작전 테이블은 적 항공기 색, 무전의 호위 대사 대상 아님.
- **항로**: 생성기 `RequestDeployEscort`에 `MOVE_AIR | ESCORT_HOSTILE`(네트워크 인자 8개 제한이라 이동 방식에 표시를 더한다) → 적 폭격기만 고르고, 목적지는 **도시만**(늘 도시로 — 없으면 공역 아무 곳), 비행장 착륙·저공 투하 없음(순항 고도에서 투하). 아군 호위는 이제 적 폭격기를 고르지 않는다(`CountFreeEscorts`도 구분). 사전 확인(`CanBuildEscortRoute`)도 같은 표시로 같은 항로.
- **HUD**: 페이즈 줄 "INTERCEPT  BMR 남은/전체  CITY 6.3km"(5km 아래면 빨강), 배너 지역 "DEFEND: 도시 이름", 항로 표시선이 폭격기 항로(도시까지).
- **페이즈 번호**: `PHASE_INTERCEPT` = 9(무작위 선택 대상, 가중치 배열 9번째 — 씬 배열이 짧으면 1), 보너스는 `PHASE_BONUS` = 10으로 옮김.
- **방어**: 폭격기·수송기 모두 **플레어**(`ACO_FlareDispenser` — NPC 전투기 AI 플레어와 같은 규칙: 나를 쫓는 미사일이 1.5km 안이면 미사일마다 한 번 50% 확률로 뿌리고, 켜진 2.5초 동안 나를 쫓는 미사일은 100% 속는다, 출격당 6번, 4초 간격. 오너가 판단해 모두에게 `NetFlare`, 미사일은 표적 마커에서 부모 쪽으로 장치를 찾는다 — `ACO_Missile`이 AI 플레어처럼 다룬다). 수송기 플레어는 적 미사일을 막아 준다. 폭격기는 **방어 기총 2문**(상부 전방위·꼬리 후방, `ACO_EscortUnit.DefenseGuns` = 작은 `ACO_AAGunSite`: 사거리 1.2km, 약한 연사, −60°까지 내려 쏜다) — 자체 콜라이더·락온 마커·체력 판정 없음(맞히면 폭격기가 맞는다), 폭격기가 출격하면 `AttachedSpawn`으로 같이 살아나고 격추·회수되면 멈춘다. Bake는 호위 대상/폭격기에 붙은 기총을 지상 풀에 넣지 않는다.
- **셋업**: 프리팹 생성 창 → **적 폭격기** → `NPC_Bomber`를 생성기 풀 아래에 4~6대(셋업 검사 [고치기]가 4대 놓는다). 수송기 플레어는 **아군 수송기** 프리팹을 다시 만들면 붙는다. 모델은 상자 — 바꿔 끼운다.

### 6-28. 플레이 테스트 개선 묶음 (2026-11)

- **아군 유닛 격추 점수**: NPC 무기(ACO_NPCGun, NPC 런처의 ACO_Missile — `FromNpc`)가 SaccTarget에 주는 피해의 무기 종류에 `ACO_Missile.NPC_WEAPON_OFFSET`(100)을 더한다. 작전판은 그 값이면 조종사 칸에 파란 `ALLY UNIT`, 개인 점수·조종사 집계·격추 대사 없음(팀 점수는 관리자가 그대로 센다). 플레이어 기체(SaccEntity)에 주는 피해는 SAV 킬피드 때문에 원래 값 그대로.
- **텔레포트 속도 0**: 게임 진행·텔레포터·화면 암전의 TeleportTo 직후 `SetVelocity(0)` + 다음 프레임 한 번 더(방금 내린 항공기 속도로 튕기지 않게).
- **함선 격침 → 부위 파괴**: 원인 — 격침 때 부위에 보내던 `Silence`는 교전만 잠깐 멈췄고, 부위 SaccTarget 체력이 남아 락온 마커가 살아 있었으며 `_disabled`가 체력(> 0)으로 다시 계산되며 사격이 되살아났다. 이제 `Silence`가 부위 SaccTarget을 터뜨린다(오너, `SaccTarget.Explode`). 본체 부활(AttachedSpawn/NetRespawn) 때 부위도 함께 부활한다.
- **대사 분리**(EV_COUNT 21): `EV_ALLY_FLARE`(아군 플레어 — 장치의 `Friendly`, 호위 유닛이 출발할 때 `!Hostile`), `EV_CONVOY_HIT`/`EV_CONVOY_LOST`(호위 대상 중 지상·해상 = 콘보이, 공중 = 수송기 EV_ESCORT_*), `EV_ALLY_SMOKE`. 새 상황 기본 대사는 Bake가 씬의 대사 표 끝에 더한다(확률·쿨타임 배열도 늘림). 슈퍼에이스는 대사별 음성(`ClipSpawn/Salvo/Decoy/Half/Down`) — 있으면 `SayVoice`. **미사일 빗나감 대사**는 터질 때가 아니라 살아 있는 표적을 시커가 놓친 순간(UnlockTime 연속 무력화·시커 범위 밖)에, 플레어에 속으면 기존 DECOYED.
- **연막**(`ACO_FlareDispenser.SmokeMode`, 지상·해상 호위 유닛): 같은 반응 규칙으로 뿌리고, 미사일이 끌려가는 지점이 뿌린 자리에 머문다. 켜진 동안(`FlareActiveTime` — 6~8초 권장) `IsObscured()` — 적 NPC 기총(ACO_NPCGun)은 조준·사격·피해를 멈추고 NPC 미사일 런처는 락온을 잃는다. FlareParticles에 연막 파티클을 넣는다. **적 기총에 조준당할 때도** 뿌린다: 적 NPC 기총(ACO_NPCGun)이 이 유닛을 유효 각도 안에 넣는 순간(조준 체류 시작 — 조준 한 번에 한 번, 피해는 체류 0.5초 뒤부터라 그 전에) `ReportGunThreat` → 오너가 다음 확인(0.2초) 때 `GunSmokeChance`(0.5)로 굴린다. 횟수·간격(FlareSalvos·FlareCooldown)은 미사일 대응과 같이 쓴다. 기총 피해 판정이 원래 표적 오너 화면에서만 돌고 표적 오너 = 연막을 정하는 쪽이라 네트워크가 더 필요 없다(피해 감지 방식보다 가볍고, 맞기 전에 반응한다). 플레어(SmokeMode 꺼짐)는 기총에 반응하지 않는다.
- **기총 피해 강화**(진행도 `UP_GUN` = 기체 탭 6번째, 저장 ID `gun_damage`, 단계당 +8%, 28항목 — Bake가 27항목 배열을 저장 ID로 옮긴다): SAV 기총 피해는 탄 오브젝트에 묶여 플레이어별로 바꿀 수 없어서, 기총 탄 파티클(충돌 메시지를 보내는 것 — 조종사 화면에서만 충돌)에 `ACO_GunDamageBonus`를 붙여 같은 충돌에 '10 ÷ 표적 장갑'(또는 'd:값')의 (배수 − 1)만큼 덤 피해를 준다. Bake가 플레이어 기체의 탄 파티클에 붙이고 강화 확장(`GunBonuses`)에 연결한다.
- **아군기 표시**(`ACO_TargetMarkers.Flow`, Bake 연결): 다른 플레이어가 탄 참가자 기체에 파란 테두리(아군 항공기 모양) + `WINGMAN 칸 번호`(`WingmanLabel`), 목표처럼 멀리서도 보인다(`ShowWingmen`).

### 6-32. 페이즈 모듈 / 상륙전 호위

- **페이즈 모듈**(`ACO_PhaseModule` — 추상 기반 클래스, 동기화 없음): 새 페이즈 종류를 미션 관리자 밖에 만든다. 관리자 자식에 두면 Bake(`BakePhaseModules`)가 `ACO_MissionManager.Modules`에 순서대로 넣는다 — 종류 번호 = `PHASE_MODULE_BASE`(20) + 순서. 기존 9종은 그대로(옮기지 않음 — 이후 6-35에서 모두 모듈로 옮기고 6-38에서 기본 코드를 지웠다).
  - 관리자가 맡는 것: 무작위 선택(모듈 `Enabled`/`Weight`/`MinPhase`, 막은 종류 비트는 11번째부터 — 모듈 최대 20개), 점수 배율, 웨이브, 보너스 페이즈, 제한 시간, 페이즈 교체, 미션 끝.
  - 모듈이 채우는 것(오너): `IsFeasible`(사전 확인 — 자리·씨앗을 들고 있다가) → `Begin`(생성기 공개 API로 배치) → `Tick`(RESULT_RUNNING / CLEAR / FAIL / REPLACE) → `End`. 진행 값은 `Manager.SetModuleProgress`, 작전 지역은 `SetModuleSite`, 도착한 호위 대상 거두기는 `ScheduleModuleRelease`.
  - 모든 클라이언트: `GetName`/`GetDescription`/`GetProgressText`(HUD 페이즈 줄)/`GetLocation`(배너)/`GetFailText`(배너·작전판 — `GetPhaseFailText`, 미션 실패 사유 `FAIL_MODULE`)/`GetClearBonus`, 무전 대사(모듈 인스펙터 Start/Success/Fail 텍스트·음성, 비면 무전 기본 대사). 동기화된 값(관리자 진행 값, 생성기 배치)만 읽는다.
  - 오너가 바뀌면 모듈 지역 상태는 처음부터 다시 본다(콘보이와 같은 '배치 직후 체력 0은 기다림' 규칙).
- **상륙전 — 초수평선 상륙**(`ACO_PhaseAmphibious`, 메뉴 `ACO → Missions → Add Amphibious Assault Phase`, 기본 `MinPhase` 2, 보너스 1500 × LCAC 생존 비율):
  - **상륙 지점**(`ACO_MissionGenerator.BuildAmphibiousRoute`) — 항구가 아니어도 된다. 순위: **1) 항구**(`MaxPlacementAttempts`번) → **2) 도시 근처 해안**(도시 둘레 `CityCoastRadius` 3km 안, `LandingCoastAttempts` 12번) → **3) 아무 해변**(지도 아무 곳에서 `AnyCoastRadius` 4km 안, 12번). 2·3순위 해안선 찾기: 무작위 방향으로 100m씩 걸어가며 땅 ↔ 바다가 바뀌는 곳(그 앞 `LandingStandoff`까지 바다여야 — 작은 만·호수 제외, 방향 4번). **절벽 아님**(`IsGentleBeach` — 항구는 보지 않는다): 해안선에서 바다 쪽 100m → 육지 쪽 `LcacBeachInland` + 60m를 20m마다, 지면이 해수면 위 `BeachMaxHeight`(25m) 이하이고 이웃 점 사이 경사 `BeachMaxGrade`(30°) 이하. 그 뒤는 항구와 같다(바다 쪽 방향 → 항로, 해안 방어는 상륙 지점 뒤). 배너 이름: 항구·도시는 그 이름, 해변은 3km 안의 가장 가까운 자리(없으면 이름 없음). 공격기는 상륙 지점(`GetLastLandingPoint`) 쪽에서 들어온다.
  - 부대(생성기 유닛 풀의 아군 호위 대상, 이동 방식 × 역할로 고른다 — 프리팹 생성 창 "상륙전 부대"):
    - 강습상륙함 `NPC_AssaultShip`(해상, `ROLE_MOTHERSHIP`, 무장 없음) `AssaultShips`(1) — 상륙 지점에서 `RouteLength`(10km) 떨어진 후방 바다(LCAC 항로 출발점)에 와서 대기.
    - 호위함 `NPC_GuardShip`(해상, `ROLE_GUARD`, 함포 1 — 적 항공기·적 지상 / 대공포 2 — 적 항공기) `GuardShips`(2) — 상륙함 좌우 `AmphibiousGuardLateral`(450m)에서 대기하며 지원.
    - **LCAC** `NPC_LCAC`(새 이동 방식 `MOVE_HOVER` — 바다 위는 `HoverSeaLevel`, 해변에 올라서면 지면(지상·공기부양 호위 유닛은 지형만 따른다 — 구조물은 Environment라 `GroundLayers`에 맞지 않는다), `ROLE_VIP` = 지켜야 하는 대상, 30m/s, 체력 450) `LandingCraft`(3) — 상륙함 자리에서 횡대(`LcacSpacing` 70m)로 바다를 건너 해변(상륙 지점 안쪽 `LcacBeachInland` 60m)에 올라가 멈춘다.
    - 아군 공격헬기 `NPC_FriendlyGunship`(공중, `ROLE_GUARD`, 55m/s — LCAC보다 먼저 닿는다, 턱 밑 기관포 — 적 지상·적 항공기, 플레어, 락온 마커 아군 항공기 25 — 적 방공·전투기가 노린다) `AttackHelicopters`(2) — LCAC 항로를 해수면/지면 위 `HeliAltitude`(90m)로 날아가 해안 안쪽 `HeliHoverInland`(700m) 상공에 멈춰 싸운다.
    - 모두 행렬 앞선 거리 0, 대형은 옆 거리로만(앞선 거리가 섞이면 정지 거리가 어긋난다). 기존 호위·지상 호위·요격은 `ROLE_VIP`만 고르고, 공기부양은 상륙전만 쓴다. 무전 피격/격추 대사는 `ROLE_VIP`만.
  - 항로: 지형 장식의 **항구**(`SITE_PORT` — 도시가 바다에 닿을 때)를 골라, 16방향 중 바다가 `LandingApproachCheck`(3km) 넘게 이어지는 방향을 '바다 쪽'으로 잡고 항구 앞 `LandingStandoff`(800m)까지 바다 항로를 거꾸로 만든 뒤(바다 쪽 반대 ±35°, 마지막 구간 곧게, 경유점·곡선 모두 바다) 해변 한 점을 더한다. 경유점 = 바다 `EscortRoutePoints`(최대 5) + 해변 1.
  - 해안 방어(목표 아님): 항구 뒤 방공 시설 링, 해안 전차(내륙 1.5km → 해안선 250m), 곡사포(상륙 지점 뒤 1.2~2.5km, 좌우로는 `ShoreSpread`의 60% 안 — LCAC가 앞바다부터 사거리에 들어온다. 처음엔 2~3.5km였는데 LCAC가 사거리에 잘 안 들어와 당겼다(Bake가 예전 기본값만 바꾼다). 자리가 안 나면 시도마다 조금씩 더 해안 쪽으로(최대 40%) — 멀리 밀려나지 않는다. 상륙함 대기 자리 10km는 사거리 밖), 적 헬리콥터(내륙 4km → 앞바다 상공), 항로 가운데 옆 2.5~4.5km 바다에 적 함대. 수는 모듈 인스펙터(Base + PerPhase × 페이즈 번호, 각 최대 15).
  - 성공: 살아 있는 LCAC가 모두 해변에 정지. 실패: LCAC 전부 격파 → 미션 실패("LANDING CRAFT LOST"). 상륙함·호위함·공격헬기는 잃어도 된다. HUD `LCAC 살아 있는/전체  남은 거리`, 배너 "LANDING: 항구·도시·가까운 자리 이름". 성공 뒤 `EscortLingerTime` 지나 부대를 거둔다.
  - 생성기 API: `CanBuildAmphibiousRoute(길이, seed)` / `RequestDeployAmphibious(tag, 길이, units, defense, seed)` — 수는 4비트씩(units = LCAC | 상륙함 << 4 | 호위함 << 8 | 공격헬기 << 12, defense = 방공 | 전차 << 4 | 곡사포 << 8 | 적 헬리콥터 << 12 | 함대 << 16).

### 6-33. 호위 이동 시간 = 보너스 제한 시간

- 호위(공중 수송기)·지상 호위(콘보이)·모듈 중 `GrantTravelTime`을 켠 것(상륙전 — 추가 메뉴가 켠다)은, 지켜야 하는 대상(`ROLE_VIP`)이 출발한 게 관리자 오너 화면에 보이면 가장 늦게 도착하는 대상의 남은 시간(`ACO_EscortUnit.GetArrivalSeconds` — 같은 시간 함수, 정차·착륙 감속 포함) + `EscortTimeMargin`(15초)을 미션 제한 시간에 한 번 더한다.
- 제한 시간 = `TimeLimitPerPhase` × 페이즈 수 + `_timeExtensionMs`(동기화, 미션마다 0부터). 배치가 아직 이 클라이언트에 적용되지 않았으면(유닛 출발 시각 ≠ 생성기 배치 시각) 다음 틱에 다시 본다. 페이즈가 바뀌면(교체 포함) 다시 한 번.
- HUD 안내 "TIME EXTENDED +m:ss"(늘어난 값이 보이면, 처음 본 값은 기준만). `EscortTimeMargin`을 음수로 두면 끈다. 요격(폭격기)은 대상이 아니다.

### 6-34. 동기화 시뮬레이션 (`Tools/aco_sync_sim.py`) — 모듈 자체 동기화 규칙

- VRChat 동작을 흉내 낸 몬테카를로: 오브젝트별 Manual 동기화(최신 값만, 오브젝트끼리 순서 무보장), 0~0.2초 전송 대기(그 전에 오너가 나가면 유실), 지연 로그정규 120ms + 3% 1~4초 끊김, 오너 이탈 시 0.5~3초 뒤 새 마스터(그때까지 받은 값으로), Owner 대상 이벤트는 받는 쪽 오너가 없으면 유실, 서버 시각 ±40ms, SaccTarget 피해 = 쏜 사람 → 표적 오너 이벤트(살아 보이면 3초 뒤 다시 쏨). 실행: `python Tools/aco_sync_sim.py [반복]`.
- 결과(시한 표적 3개, 시나리오마다 3000회):
  - 지금 코드의 **거짓 도주**(마감 직전에 맞혔는데 피해가 오너에게 늦게 와서 도주 판정 → 미션 실패) 0.7~1%/미션, 배치 직후 마스터가 나가면 3.5%. 개선(마감 = 생성기 배치 시각 + 제한, 도주 판정 유예 `TstEscapeGrace`)으로 유예 0초 0.9%, 1초 0.2%, 2초 0.13%, **3초 0%**(기본 3초). 유예 때문에 마감 뒤 인정되는 격파는 미션당 0.1개 정도(플레이어에게 유리한 쪽).
  - **표적 중복**: 배치 직후 마스터가 나가면(생성기 상태는 나갔는데 관리자 '배치함'은 유실) 새 오너가 같은 태그로 다시 요청 → 지금 코드는 4% 확률로 같은 표적이 둘(하나만 부숴서는 안 끝나 도주로 실패하기도 한다) → 생성기가 같은 태그·같은 분류를 다시 놓지 않게 막아 0%(`HasTaggedUnits` — 목표·호위·상륙전 배치 모두).
  - 멈춤(페이즈가 끝나지 않음) 0%, 늦게 들어온 사람의 HUD 어긋남 평균 0.6초 / p95 2.3초(관리자 값과 생성기·체력 값이 따로 도착 — 보이기만).
  - 관리자와 모듈이 다른 오브젝트면 새 페이즈 직후 이전 페이즈 값이 보인다(43%, 평균 0.13초·최대 4초) → 모듈 동기화 값에 페이즈 번호를 찍어 다르면 무시하면 0%.
  - 호위 이동 시간 보너스: 더한 값과 '더했음'이 같은 관리자 오브젝트라 함께 오간다 — 오너 교체로 두 번 더해지는 일 0%, 새 오너가 다시 더할 때 지난 만큼 덜 더해진다(평균 0.85초, 최대 4초).
- 모듈 자체 동기화 규칙(`ACO_PhaseModule` 설명에 적었다): 판정은 관리자 오너만(모듈 오너가 아니면 가져온다), 상태 전이는 동기화 값 + 생성기 상태의 결정적 함수, 목표마다 다른 태그, 동기화 값에 페이즈 번호, 시각은 생성기 배치 시각 기준, 마감 판정엔 유예. 기반 클래스에는 동기화 방식을 붙이지 않고 모듈마다 붙인다.
- 남은 드문 경우: 페이즈 시작 0.2초 안에 마스터가 나가 새 오너가 **다른 종류**를 고르면 이전 종류 목표(같은 태그)가 함께 남는다 — 둘 다 부수면 끝난다. 확률이 매우 낮아(페이즈마다 0.1% 미만 추정) 두었다.

### 6-35. 기본 페이즈 9종 모듈화 (`ACO → Missions → Convert Built-in Phases to Modules`)

> 이 절의 변환 메뉴·`ReplacesBuiltin`·기본 코드 유지는 중간 단계다 — 6-38에서 기본 코드와 `ReplacesBuiltin`을 지웠고 메뉴는 `Create Default Phase Modules`로 바뀌었다.

- 메뉴가 관리자 아래에 모듈 9개를 만들고 관리자 인스펙터 값·`PhaseTypeWeights`·`BossMinPhase`/`TstMinPhase`와 무전 대사(종류별 `StartTexts`/`SuccessTexts`/음성, 실패는 `MissionFailedTexts`의 사유 칸)를 옮긴다. 모듈마다 `ReplacesBuiltin` = 기본 종류 번호 — 켜져 있으면 그 기본 종류는 무작위 선택에서 빠지고 모듈이 나온다. **기본 코드는 그대로** — 모듈을 끄면(`Enabled`) 원래대로. 확인이 끝나면 기본 코드를 지운다(다음 단계).
  | 모듈 | 대신하는 종류 | 동기화 | 판정 근거 |
  |---|---|---|---|
  | `ACO_PhaseDestroy`(Kind 0 타격 / 1 방공망 / 2 제공권 / 3 보스) | STRIKE·SEAD·AIR·BOSS | 없음 | 태그 목표 세기(`Manager.CountTaggedObjectives`) |
  | `ACO_PhaseQuota` | QUOTA | 없음 | 팀 점수 − 페이즈 시작 점수(둘 다 동기화), 목표 표시 안 함 |
  | `ACO_PhaseEscort` | ESCORT | Manual(보낸 요격 편대 수 + 도장) | 호위 대상 생사·도착(생성기) |
  | `ACO_PhaseConvoy` | CONVOY | 없음 | 콘보이 생사·도착(생성기) |
  | `ACO_PhaseIntercept` | INTERCEPT | Manual(호위 편대 보냄 + 도장) | 폭격기 생사·도착(생성기) |
  | `ACO_PhaseTimeSensitive` | TST | Manual(순번·잡은 수·배치함·다음 배치 시각 + 도장) | 표적마다 다른 태그, 마감 = 생성기 배치 시각 + 제한, 유예 3초 |
- **동기화를 줄이는 방법**: 보스 이름은 태그로 정한다(태그 % 이름 수 — 모두 같은 값, 동기화 없음). 시한 표적 마감은 생성기 배치 시각에서 계산한다. 할당량 진행은 이미 동기화되는 팀 점수에서. 꼭 들고 가야 하는 값(보낸 편대 수, 표적 순번)만 모듈이 Manual로 동기화하고 **도장**(관리자 페이즈 시작 서버 시각)을 같이 넣는다.
- **도장이 안 맞을 때(새 오너가 이번 페이즈 값을 못 받음)**: 시한 표적은 생성기 상태에서 순번·잡은 수를 다시 찾고(순번 0부터 배치된 표적을 따라가며 부서진 것은 잡은 것으로), 호위·요격은 '이미 보냈다'고 친다(지금 진행률까지의 요격 편대·호위 편대 — 두 번 보내지 않는다, 하나 빠질 수는 있다).
- 관리자 모듈 API(오너): `CountTaggedObjectives` / `GetTagged*`, `MakePhaseTag(sub)`, `SetModuleActiveTag`, `SetModuleProgress(완료, 전체, 파괴 이벤트로 셀지)`, `SetModuleSite`, `MarkModuleProgress`, `ScheduleModuleRelease`, `GetPlayerCenter`, `GetPhaseScoreStart`, `GetModuleTag`(표시를 숨긴 모듈도 쓰는 진짜 태그). 모듈 virtual: `WaveOnStart`(시한 표적 false), `MarksObjectiveTag`(할당량 false), `ShowsEscortRoute`(호위 계열 true — HUD 항로 표시), `GetClearBonus`.
- 보너스 페이즈(슈퍼에이스)는 관리자에 남는다(무작위 선택 밖, 미션 클리어 뒤에 붙고 제한 시간이 따로다).
- **검증 도구**: `Tools/aco_compile_check.py` — Unity가 마지막 컴파일에 쓴 응답 파일(Library/Bee/.../Assembly-CSharp*.rsp)과 Unity 컴파일러(DotNetSdkRoslyn)로 ACO 스크립트만 컴파일해 C# 오류를 잡는다(U# 고유 제약은 Unity에서). `Tools/aco_sync_sim.py` — 시한 표적 모듈 설계('module': 상태가 관리자와 다른 오브젝트, 도장이 안 맞으면 복구)까지 넣었다.
  - 결과(3000회): 모듈 설계는 기본·마스터 이탈·늦은 입장에서 거짓 도주·표적 중복·멈춤·잡은 수 틀림 모두 0%. 배치 직후 마스터 이탈에서는 16%가 복구 경로를 탔고 잡은 수 틀림 0%, 거짓 도주 0.07%(3000회 중 2회 — 4초 넘는 끊김이 겹친 경우). 지금 기본 코드는 같은 조건에서 거짓 도주 3.5%·표적 중복 4%.

### 6-37. 페이즈 대사 = 모듈 (무전과의 역할)

- 페이즈 대사(시작·성공·실패)는 **모듈 인스펙터에서만** 정한다 — 기본 9종을 바꾼 모듈도, 앞으로 만들 모듈도 같다. 변환 메뉴는 무전의 종류별 대사를 한 번 모듈로 옮길 뿐이고, 그 뒤 무전의 종류별 칸(`StartTexts` 등)은 기본 코드 전용(모듈을 끈 종류)이다 — 기본 코드를 지울 때 함께 정리한다.
- 모듈 칸이 비면 무전의 공통 기본 대사(`DefaultStartText`/`DefaultSuccessText`/`DefaultFailText`, 미션 실패는 `MissionFailedTexts[0]`).
- 화자: 모듈 `StartSpeaker`/`SuccessSpeaker`/`FailSpeaker`(무전 `SpeakerNames` 순서, -1 = 무전 `PhaseSpeaker`). 이름 색·무전 채널(PTT)은 화자를 따른다.
- 무전이 계속 맡는 것: 언제 말할지(관리자 동기화 상태를 보고 페이즈 시작·끝·미션 끝을 알아챈다), 자막·음성 대기열·PTT, 미션 시작·완료 대사, 상황 대사 표(격추·경보·플레어·호위 피격·증원 — `Line*`).

### 6-38. 기본 페이즈 코드 정리

- 관리자에서 기본 9종 + 기본 슈퍼에이스 보너스의 판정·배치·이름·설명·실패 글자·UI 전용 조회(시한 표적·호위·폭격기·콘보이), 종류 상수(PHASE_STRIKE~PHASE_BONUS), 실패 사유 1~4, 전용 동기화 변수(_tst*, _bossName, _escortWaves), 사전 확인 계획(_plan*)을 지웠다(2200줄 → 1400줄). 페이즈 종류는 이제 모듈뿐(20 + 순서, 최대 30개). 모듈이 없으면 그 페이즈는 건너뛴다. 모듈의 `ReplacesBuiltin`도 지웠다.
- 점수 HUD 페이즈 줄은 모듈 진행 글자(비면 완료/전체), 무전 페이즈 대사는 모듈 대사(비면 공통 기본)뿐이다. 셋업 검사기가 켜진 페이즈 모듈이 없으면 경고한다.
- **씬 값 옮기기(끝남)**: 관리자의 페이즈별 설정과 무전의 종류별 대사·음성을 잠시 숨겨 남겨 모듈 10개로 옮겼고(씬 저장 확인 — 시한 표적 간격 3초·보너스 400, 보스 최소 페이즈 2, 보너스 180초·3000, 요격 편대 위치 0.3, 직접 넣은 대사·음성), 그 뒤 숨긴 값을 지웠다. 메뉴는 `ACO → Missions → Create Default Phase Modules`(기본값·기본 대사로 없는 모듈만 만든다 — 새 씬·지운 모듈 되살리기, 셋업 검사기 [고치기])로 바꿨다. 무전 `MissionFailedTexts`는 0번(시간 초과)만 쓰인다.
- 되돌리기: 모듈화 전 상태는 커밋 `2c24c86`(feature/aco-session-updates).

### 6-57. 시한 표적 — TEL과 탄도탄

- **흐름**(`ACO_PhaseTimeSensitive`): TEL `Count`(3)대가 8초 간격으로 하나씩 나온다. 한 대에 `TimeLimit` 90초 — 앞 `LaunchDelay` 30초는 발사 준비(지상 표적, 공대지), 나머지 60초는 탄도탄 상승(공중 표적, 공대공). 체력은 하나(`NPC_TEL` 200 — 미사일 한 발)라 발사 전이든 후든 부수면 그 표적은 끝. 90초가 지나면 요격 불가 = 미션 실패(MISSILE LAUNCHED).
- **상승**(`ACO_TelUnit`): 등가속. 높이 = 발사 높이 + (천장 − 발사 높이) × (경과 / 60초)². 천장 = 씬 SAV 최대 고도(`AtmosphereThinningEnd` + `SeaLevel` = 19,812m — Bake가 넣는다). 해수면 기준 평균 330m/s, 끝 속도 660m/s, 가속도 11m/s². 10초 550m, 30초 5km, 50초 13.8km. 서버 시간 함수라 위치 동기화가 없다.
- **표적 전환**: 마커 둘(지상 30 / 공중 29)과 히트박스 둘을 시간에 따라 바꾼다. 표적 마커·레이더는 루트를 따라가므로 상승할 때 루트가 탄도탄과 올라가고, 빈 차체(`Chassis`)를 매 프레임 발사 자리로 되돌린다. 표적 마커 모양·약어(TEL → BM)와 레이더 종류도 발사하면 공중으로 바뀐다(`IsTelAirborne`). 공대지 미사일이 날아가던 중 발사되면 마커가 꺼져 표적을 놓친다.
- **사라짐**: 파괴되면(발사 전/후) 차체까지 사라지고 폭발만 `BlastTime`(6초) 남는다. 다음 표적으로 넘어간다는 건 앞 표적이 파괴됐다는 뜻이라 페이즈 변경 때 따로 거두지 않는다. 놓치면 미션 실패라 리셋으로 정리된다.
- **전용 풀**: `GroundUnits`에 있지만 `IsGroundMover`로 일반 지상 배치에서 빠진다(`IsTelUnit`). 배치는 `OBJ_TEL`. 시작 시각은 생성기가 `SetStartTime`으로 넣는다. Bake가 프리팹이 없으면 만들고 풀에 3대를 넣는다(`Count`를 늘리면 `TelPoolSize`도). 모델은 임시 큐브(`ACO_TelBuilder.cs`).
- **표시·대사**: 진행 글자 `2/3  LAUNCH 0:12` → `2/3  INTERCEPT 0:42  ALT 24000ft`. 관제 대사(`LineSpotted`·`LineLaunch`·`LineLimit`(`LimitWarnTime` 15초 전)·`LineTelDown`·`LineMissileDown`, 음성 칸 있음)를 모든 클라이언트가 TEL 상태를 보고 각자 재생한다. 이전 기본 대사는 Bake가 새 문구로 바꾼다.

### 6-58. 함대전 지원

- **흐름**(`ACO_PhaseFleet`, 보너스 5000, 시작할 때 제한 시간 +5분): 아군 기함(순양함)·호위함 2척과 적 항모전단(항모 1, 순양함 2, 구축함 3)이 10km 거리에서 서로 다가가 3.5km에서 멈추고 포격전을 벌인다. 적 항모(태그 목표) 격침 = 성공, 아군 기함 격침 = 미션 실패(FLAGSHIP LOST). 항모가 살아 있는 동안 `LaunchInterval`(50초)마다 전투기 2·공격기 2가 나온다. 끝나고 `ReleaseDelay`(20초) 뒤 함대에 쓴 함선을 모두 거둔다(`RequestReleaseFleet` — 격침 연출이 보이게). 곧은 바다가 없는 지형이면 나오지 않는다.
- **이동**: 위치 동기화 없음. 적 함선은 `ACO_Warship.MissionAdvance`(스폰 방위로 곧게 가서 멈춘다 — 시간 함수), 아군은 `ACO_EscortUnit` 해상 항로(2점, `ARRIVE_STOP`, 함대 속도로 `SetSpeedOverride`). 양쪽 다 `FleetSpeed` 14m/s, (10km − 3.5km) / 2 = 3.25km를 약 3분 50초에 간다(`_fleetContactMs`). 호위함 기본 속도(6)는 함대전에서만 바뀐다.
- **함종 정리**: `BB`로 쓰던 것은 슬라바급이라 순양함으로 바꿨다(`CG`, Cruiser — Bake가 이름을 고친다, 프리팹 이름 NPC_Warship·저장 키는 그대로). 전함은 없다. 구축함(소브레멘니급) `DD`. 순양함·구축함·호위함은 기존 풀을 함께 쓰므로 일반 타격 페이즈에도 나온다. 항모(`ReservedForFleet`, 생성기 `Carrier`)만 함대전 전용. 기함은 `ROLE_FLAGSHIP`이라 다른 호위 페이즈 후보에 섞이지 않는다.
- **무장**: 주포는 전차와 같은 `ACO_AAGunSite` + `ACO_NPCGun`(파티클 탄, 사거리 4~4.5km), VLS는 대공·대함 겸용 — 적 VLS·함포의 표적에 아군 지상 레이어(26)를 더하고 최저 고도를 푼다(Bake가 씬 값을 고친다 — 모델을 직접 바꾼 프리팹을 다시 만들지 않는다). CIWS는 그대로 대공·요격. 상륙전 LCAC도 근처 적 함선이 있으면 노릴 수 있다.
- **기함 우선**: 수송기처럼 표적 쪽 값이다 — 기함 `AttackPriority` 2.5km. 적 전투기뿐 아니라 포대·VLS도 따르도록 `PriorityForSites`를 켰다(표적이 처음 보일 때 부모의 ACO_EscortUnit을 찾아 캐시). 거리에서 이 값을 빼서 고르므로 3.5km 거리의 기함은 약 1km 안의 항공기보다 먼저다 — 플레이어가 바짝 붙으면 플레이어를 쏜다. 수송기(3km)도 켰다 — 호위 페이즈에서 SAM·대공포가 수송기를 먼저 노린다. 나머지 유닛은 꺼져 있다. 기존 프리팹은 Bake가 한 번만 켠다(`PriorityDefaultsApplied` — 그 뒤 체크를 꺼도 유지된다).
- **함재기**: 전용 유닛 없이 일반 전투기·공격기 풀을 쓴다. 항모 발진이면 `ACO_NPCFighterAI.CarrierVisuals`가 켜지고 숙련도 칸(Rookie·Veteran·Ace)이 꺼진다(비우면 숙련도 외형 그대로, 동기화 `_fighterCarrier`). 출격 때마다 전투기 → 공격기 순으로 `LaunchSpacing`(4초)마다 한 기씩, 뱃머리 앞(`CarrierLaunchForward` 165m, 높이 `CarrierLaunchHeight` 40m)에 뱃머리 방향으로 나타나 기함 상공(고도 `SpawnAltitudeMin~Max`)으로 올라간다. 항모가 아군 함대를 보고 있어 곧장 아군 쪽으로 날아간다. 쉬는 기체가 없으면 그 기체는 건너뛴다.
- **잠재우기**: 포대는 플레이어가 멀면 잠들지만, 함선 선체 마커에는 SaccEntity가 없어 '조종 중'으로 치므로 양쪽 함대는 서로 가까우면 깨어 있다. 그래서 플레이어가 멀어도 포격전은 계속되고, 돕지 않으면 기함이 진다(아군만으로 이길 수도 있는지는 균형 확인). 같은 이유로 일반 페이즈에서도 적 함선 포대가 근처의 아군 함선·LCAC가 있으면 깨어 쏜다.
- **모델**: 기함 Ticonderoga, 항모 Kuznetsov(Low Poly Warship Collection, 2.3배 — 172m, 300m), `ACO_FleetBuilder.cs`. 포대 자리는 어림값이다.
- **설치**: Bake가 프리팹이 없으면 만들고 풀에 넣으며, 모듈 `Phase_Fleet`(4페이즈 위주, PhaseWeights {0, 0, 0, 2, 0.5})도 만든다. 호위함 2·순양함 2·구축함 3은 풀에 있는 만큼 나온다.

### 6-64. 난이도 곡선

- **페이즈 수**: 5로 유지한다. 페이즈 가중치는 페이즈 번호 기준이라 함대전·요새는 페이즈를 많이 고른 판에서만 나온다(난이도 선택의 보상).
- **임무 순서**: 기본 모듈의 `PhaseWeights`를 앞은 타격·제압·제공권, 중간은 호위·콘보이·상륙전, 4는 함대전, 5는 요새 위주로 둔다. 비어 있는 모듈에만 Bake가 넣는다(`ApplyDefaultWeights` — ACO → Missions → Phase Weights에서 직접 바꾼 값은 유지).
- **숙련도**: `ACO_MissionGenerator.PhaseVeteranChance`·`PhaseAceChance`(1~5: 베테랑 15→50%, 에이스 0→25%)를 전투기 편대와 지상 유닛이 함께 쓴다. 웨이브 증원은 웨이브 값과 페이즈 값 중 높은 쪽.
- **지상 유닛**: 유닛마다 숙련도를 굴려(`RollUnitTier` — 배치의 시드 난수를 건드리지 않는 해시) `_groundTier`·`_hvtTier`로 동기화하고, 스폰할 때 `ApplyUnitTier`가 넣는다. 함선은 부위마다, 전차·곡사포는 포마다. 기준은 프리팹 값(베테랑 1.0)이고 신병은 약해지고 에이스는 강해진다: 포 조준·체류·사격 간격, SAM·VLS 락온·재장전, 곡사포 간격·산포, 최대 체력(신병 ×0.9, 에이스 ×1.3 — 플레이어 피해 강화와 맞물린다, `Ground*Multiplier`). 체력 기준은 Bake가 채운 `BaseHealth`이고, 값은 스폰 2프레임 뒤 `SaccTarget.FullHealth`에 넣는다(전투기와 같은 방식 — 부활이 새 체력으로 채운다). 건물·TEL·공중 요새 코어·엔진은 체력 대상이 아니다(요새 포대는 대상). 아군 함선·호위 유닛은 아무것도 대상이 아니다.
- **보너스**: 페이즈를 많이 고를수록 보너스 페이즈가 잘 붙는다(1~5페이즈: 5·10·25·50·100%, `BonusChanceByPhases`). 디버그 패널의 ACE·VETERAN·BONUS 버튼은 표 대신 고정 값을 쓰게 만든다(`DebugFixedChance`, `DebugBonusChance` — 표시는 그전까지 BY PHASE).

### 6-75. 함대전 대사 표·항로 검사

- **대사**: `ACO_PhaseFleet`이 `ACO_PhaseBoss`를 물려받아 요새·랜스 포대와 같은 상황별 표를 쓴다(상황 5가지, 적 줄 뒤에 아군 줄). 화자는 셋이다: 적(`EnemyName` "CARRIER GROUP"), AWACS(관제 이름·색), 함대장(`AllyName` "FLAGSHIP"·`AllyColor`, 아군 무전 채널). 기함 입장의 교전 시작·기함 피해 줄은 함대장, 함재기 출격·항모 피해 줄은 AWACS가 말한다. 예전 개별 필드(`LineContact` 등)는 없앴다.
- **섬 통과 원인**: `FindFleetLine`이 가운데·아군 시작·적 시작 세 곳의 바다만 재고, 다가가는 길은 재지 않았다.
- **처리**: 양쪽 함대가 지나는 길을 대열 폭 전체로 `FleetLaneStep`(200m) 간격으로 잰다(`LaneIsSea`, 앞뒤·좌우 여유 `FleetLaneMargin` 200m). 폭은 호위함·순양함·구축함 수로 계산하고, 적 쪽 길은 구축함이 먼저 나가는 `FleetSpacing`까지 포함한다. 멈춘 뒤 두 함대 사이는 지나지 않으므로 섬이 있어도 된다.
- 검사가 엄격해진 만큼 자리 하나에서 방향을 4개(45도 간격) 시험한다. 그래도 못 찾으면 함대전은 그 페이즈에 나오지 않는다.

### 6-88. 자유 비행 모드

- **권한**: 작전 콘솔 `FREE FLIGHT` 버튼(`ACTION_FREE_FLIGHT`). 인스턴스 오너만 바꾼다(`Networking.InstanceOwner`, 호출자는 게임 진행 오너가 `NetworkCalling.CallingPlayer`로 다시 확인). 오너가 없거나 나간 인스턴스는 마스터가 맡는다. 버튼 아랫줄에 바꿀 수 있는 사람의 이름을 적는다. 미션·시작·출격 카운트다운 중에는 바꿀 수 없다. 같은 버튼을 다시 누르면 꺼진다.
- **켜면**: READY·준비 카운트다운을 비우고 `ACO_MissionGenerator.ClearAll`로 병력을 모두 거둔다(미리 배치 포함). 켜져 있는 동안 미리 배치·READY·미션 시작이 없다. 지형 리롤은 된다.
- **탑승**: LAUNCH가 바로 갑판으로 보낸다(명단 필요). 조종석 `DisableInteractive`를 풀어 명단 밖이어도 빈 기체를 눌러 탈 수 있다.
- **내릴 때**: 기체 회수(`ReturnIdleAircraft`)와 순간이동이 없다. 격추되면 평소처럼 격납고로 돌아가고 기체는 SAV가 보관 장소로 되돌린다.
- **사양 유지**: `ACO_VehicleUpgrades`가 내릴 때 강화 되돌리기·데칼 끄기·소모품 외형 끄기를 건너뛴다. 다시 타면 그 조종사 값으로 맞춘다. 격추되거나 기체가 회수되면(`SFEXT_G_ReAppear`, `SFEXT_G_RespawnButton`) 지운다.
- **끄면**: 미션이 끝날 때처럼 모든 클라이언트가 `ReturnToBriefing`으로 내려서 브리핑실로 돌아간다(걸어서 안전지대에 있으면 그대로). 기체는 `FreeFlightRecallTime`(20초) 동안 기다리지 않고 바로 회수한다. 모두 내리면 `_terrainPending`으로 새 지형 + 미리 배치(`NewTerrainAfterMission`과 상관없이). 명단은 유지한다.
- **쿨타임**: 켜고 끄는 데 `FreeFlightCooldown`(10초). 동기화된 시각이라 게임 진행 오너가 바뀌어도 이어지고, 버튼 글자에 남은 초를 표시한다.

### 6-89. 과반 READY 자동 시작 표시

- `ReadyCountdown` 60초 → 30초(`ReadyTuneVersion`으로 Bake가 한 번만 바꾼다).
- 콘솔 배너 맨 위가 `AUTO START IN n`으로 바뀌고, READY 안 한 조종사에게는 빨간 안내를 띄운다. 상태 패널도 크게 표시한다.
- 명단에 있는 사람은 머리를 따라오는 안내(`ACO_ScoreHUD.ShowNotice`)로 어디서든 남은 시간을 본다. 시작할 때와 마지막 `TickSeconds`에 `CountdownTickSound`.
- 전 패널을 덮는 판(`CountdownOverlays`)은 쓰지 않는다 — READY를 누르거나 패널을 볼 수 있어야 한다.

### 6-93. 함대전 함선 교전 조건

- **적 함선**: 최저 고도(20m) 때문에 해수면의 함선 마커가 걸러졌다. `ACO_PrefabTuning.TuneShip`이 프리팹의 VLS·ASM·함포에 `MinTargetAltitude` -100000, 엔티티 `AAMTargetsLayer`에 26을 넣는다. CIWS는 이름으로 구분해 건드리지 않는다.
- **사거리**: 두 함대는 3.5km에서 멈춘다. 탐지·추적 거리가 4.5km 미만인 무장만 5km·6km로 올린다. 직접 정한 ASM 4.5km는 유지한다.
- **교전 시점**: 시작 거리 10km, 양쪽 14m/s라 5km까지 약 3분 걸린다. 그 전에는 쏘지 않는다.
- **하늘**: 전부 꺼지면 `ACO_Weather`가 돌지 않아 맑음에 멈춘다. Setup Check의 하늘 항목으로 확인한다.

### 6-122. 안전지대 안의 아군은 공격받지 않는다

- 안전지대 = `ACO_PlayerProgress.SafeZones`(보급 트리거 — 씬의 `SafeArea`, 레이어 27 = SAV 보급 레이어). 기체 확장 `ACO_VehicleUpgrades`가 모든 클라이언트에서 0.5초마다 기체 위치가 그 콜라이더 안인지 본다(`Collider.ClosestPoint` — 회전한 상자도 맞다).
- 안이면 그 기체의 락온 마커(`LockTarget` — SaccEntity 아래 "AAMTarget", 레이어 25, Bake가 찾아 넣는다)를 끈다. 적 대공포·SAM·전투기·기총은 꺼진 마커를 표적으로 고르지 않고(`activeInHierarchy`), 날아오던 적 미사일도 표적을 잃는다(`ACO_Missile.TargetIsValid`). 나오면 다시 켠다. 위치는 SAV가 동기화하므로 표적을 고르는 오너 화면에서도 같은 답이 나온다.
- 이미 터져 남아 있는 소형 레일건 폭발 지역 같은 범위 피해는 막지 않는다(안전지대 근처로 쏘지 않으므로).
