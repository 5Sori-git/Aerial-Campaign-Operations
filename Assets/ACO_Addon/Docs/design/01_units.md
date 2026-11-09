# 적 유닛 — 공통 시스템·NPC 전투기·대공포·함선·호위·지상·포대·부품

> 설계 문서의 한 분야. 목차·작성 규칙은 `Docs/DESIGN.md`. 절 번호는 예전 DESIGN.md 그대로(코드 주석의 "DESIGN.md 6-xx"가 가리키는 번호).

## 4. 공통 시스템

### 4.1 IFF ✅

- 소속 = 락온 마커의 레이어(2.2). 런타임에 바뀌지 않으며 동기화하지 않는다.
- "누구를 적으로 보는가" = 각 유닛의 `TargetLayers`(기본 25). NPC 간 교전은 아군 NPC를 25/26에, 적 NPC를 29/30에 두고 서로를 `TargetLayers`에 넣으면 성립한다.
- ⚠ 플레이어 기체의 `SaccEntity.AAMTargetsLayer`를 **29+30으로 바꿔야 한다**(인스펙터 값이라 SAV 파일 수정 아님). SAV 프리팹은 25로 출고되어, 그대로면 플레이어 미사일이 아군을 락온한다.
- 포획/배신처럼 전투 중 소속이 바뀌는 연출은 범위 밖.

### 4.2 탐지 ✅

`Physics.OverlapSphere`를 쓰지 않고(호출마다 배열 할당 — Udon에서 비쌈) 빌드 때 베이크된 `SaccEntity.AAMTargets`를 순회한다. 적 NPC의 배열엔 아군 마커(플레이어 기체 몇 대)만 있어 후보가 한 자릿수다. 전투기는 거리, SAM은 거리 + 레이더 콘 + 최소 고도 + LOS로 거른다.

### 4.3 무기 ✅

**락온은 플랫폼이 아니라 무기가 소유한다** — 락온 시간은 미사일/레이더 종류에 따라 다르고(`MissileType`도 런처에 있음), 한 플랫폼에 다른 런처를 달면 각각 다른 특성을 가져야 하기 때문.

- 플랫폼(AI/SAM): 누구를 조준할지 + 교전 가능 여부를 판단해 매 틱 `AimAt(idx, dt)` 또는 `ReleaseAim()`. `IsLocked() && CanFire()`이면 `FireAt(idx)`.
- 런처(`ACO_MissileLauncher`): 락온 누적/감쇠(`LockTime`, `LockDecayRate`), **피락온 경고** 전송, 탄약·연사·재장전. 발사는 `NetworkEventTarget.All` 1회 브로드캐스트 → 모든 클라이언트가 같은 미사일을 로컬 재현. 탄약/연사/재장전은 `Time.time` 타임스탬프 방식이라 Update가 없다.
- **피락온 경고**: SAV는 자기 `DFUNC_AAM`에서만 경고를 보내므로 런처가 직접 `LockWarningEvent`를 전역 전송하고, 각 클라이언트가 `victim.InVehicle`이면 `SFEXT_L_AAMTargeted`로 조종석 경고음을 울린다.

**미사일(`ACO_Missile`)**
- **유도** (`GuidanceMode`, 미사일 프리팹마다 선택, `TurnRate` 한도는 공통):
  - `0` 직접 추적(기본): 현재 타겟 위치를 곧장 향함. 단순·예측 가능하지만 가로지르는 타겟은 뒤를 쫓다 끝에서 스친다(시뮬레이션: 측면 통과 타겟에 최근접 약 75m로 빗나감).
  - `1` 예측 조준: `|D + V·t| = s·t` 요격점(최대 `MaxLeadTime` 4초)을 향함. 타겟 속도는 SAV 기체면 `CurrentVel`, NPC면 비행 컨트롤러 값, 아니면 위치 미분(0.25초 평활).
  - `2` 비례항법: 기수 회전율 = `NavigationConstant`(N=3) x 시선 회전율(시선 방향의 프레임 간 외적/경과시간, 평활). 기수가 시선에서 `PNPursuitAngle`(45°) 넘게 벗어나 있으면 직접 추적으로 먼저 돌려 세운다(발사 직후·콜드 런치 후).
  - 시커/대응탄/노칭/근접 신관은 세 방식 모두 같다. 예측 조준·비례항법은 기수가 타겟보다 앞을 보므로 `MaxTrackingAngle`이 너무 좁으면 리드각 때문에 놓칠 수 있다.
  - ⚠ 세 방식의 비교 시뮬레이션은 아직 못 돌렸다(실행 환경 제약) — 실제 테스트로 N과 회전 한도를 맞춘다.
- **탑 어택(상공 진입 후 내리꽂기, AGM용)** (`ACO_Missile.TopAttack`): 먼저 표적 바로 위 `TopAttackAltitude`(500m) 지점을 향해 `TopAttackClimbTurnRate`(45°/s, TurnRate보다 크면 TurnRate)로 올라가며 날다가, 표적을 내려다보는 각도가 `TopAttackDiveAngle`(40°) 이상이거나 수평 거리가 `TopAttackMinDistance`(150m) 안이면 내리꽂는다 — 그때부터는 원래 유도 방식(직접/예측/비례항법, 비례항법 시선 기준은 새로 잡음). 올라가는 동안은 시커 각도 제한을 보지 않는다(표적이 옆·아래에 있어도 놓치지 않는다, 대응탄 판정은 그대로). 미사일 프리팹에서 켠다(AGM 모듈의 탄체).
- **콜드 런치(수직 발사)** (`ColdLaunchTime` > 0): 발사 후 N초 동안 엔진을 켜지 않고 `ColdLaunchSpeed`(30m/s) 사출 속도 + 중력(`ColdLaunchGravity` 배수, 기본 1)의 **탄도 궤적**으로 날아간다(수직 30m/s 사출 시 1초 후 약 25m 상승, 상승 속도 20m/s). 점화 순간의 속력을 이어받아 기수 방향으로 가속한다. `ColdLaunchTurnRate`(기본 0)를 주면 콜드 중 **기수만** 타겟 쪽으로 돌고 이동 벡터는 바뀌지 않으며, 점화하는 순간부터 기수 방향으로 날기 시작한다. 이 동안과 **점화 후 타겟이 처음 시커 각도 안에 들어올 때까지**(최대 2초)는 각도로 락온을 잃지 않는다(360°). N초 직후 바로 각도 검사를 켜면 수직 기수에서 낮은 타겟을 향해 돌아서는 동안 놓친다. 대응탄 판정은 그대로 적용된다. 시뮬레이션: 시커 45°로 고도 300m 타겟에 수직 발사 시 일반/360° 유지 없음은 0.5~1.1초에 놓치고, 360° 유지는 명중. 미사일 프리팹은 전투기와 공유되므로 SAM용은 복제해서 따로 설정하고, 런처의 `LaunchPoint` 앞쪽(파란 축)을 위로 향하게 둔다.
- **시커/대응책** — `SAV_AAMController`와 같은 규칙·기본값: 매 프레임 아래 중 하나면 그 프레임은 무력화되어 직진하고, **`UnlockTime`(0.1초) 연속되면 영구히 놓친다**(근접 신관도 꺼짐).
  - 시커 각도 초과 `MaxTrackingAngle`(90°) — 타겟을 지나치면 자연히 걸림
  - 대응탄 — 확률 판정 없이 **켜져 있으면 100% 속는다**(5.6). 타겟 `SaccAirVehicle`의 `NumActiveChaff/Flares/OtherCM`을 런처 `MissileType`(0 레이더→채프, 1 열추적→플레어, 2 기타)으로 읽고, NPC 전투기는 `ACO_NPCFighterAI.IsFlareActive()`
  - 노칭 — SAV L417과 동일, `NotchAngle = 0`이면 비활성(SAV 기본값)
- **미사일 접근 경고**: 발사 시 타겟 `SaccAirVehicle`의 `MissilesIncomingRadar/Heat/Other` +1, 상실·폭발·회수 시 −1, `VehicleAnimator`의 `missilesincoming` 갱신(SAV L237~246, L495~503, L677~684). 조종 중일 때만. 카운터는 로컬 값이지만 모든 클라이언트가 같은 미사일을 재현하므로 조종 중인 플레이어의 값이 맞게 유지된다(SAV도 같은 구조).
- 접근 경고는 `SaccAirVehicle`이 있는 타겟(=플레이어 기체)에만 적용된다. 대응탄은 NPC 전투기 플레어에도 적용된다(5.6).
- **데미지 대상**: NPC는 루트에 SaccEntity와 SaccTarget이 둘 다 있으므로 **SaccTarget을 우선**한다(SaccEntity로 가면 2.4의 함정으로 데미지 0). 플레이어 기체는 SaccEntity.

**기총(`ACO_NPCGun`)** — 조준 체류 시간 방식
- **탄은 시각 전용 파티클**(충돌 끔, 2.5-1). 데미지는 **타겟으로 지정된 기체의 오너만** 계산한다 — 그 클라이언트가 자기 화면에서 본 상황이 판정 기준이고 계산자가 한 명이라 중복 데미지가 없다. 타겟이 아닌 기체는 검사하지 않는다(유탄 피해 없음).
- 판정 흐름:
  1. 리드 조준점과 기수가 **A = `FireStartAngle`(6°)** 이내 → 발포 시작(시각 효과, 모든 클라이언트가 로컬 판단 → 트래픽 0)
  2. **B = `EffectiveAngle`(3°)** 이내에 **N = `DwellTime`(0.5초)** 연속 머물면 데미지 시작. 한 번이라도 벗어나면 리셋 — 이 N초가 플레이어에게 회피할 틈을 주는 경고 구간이다
  3. 데미지 1회분(`DamagePerTick` 4)은 고정, **주기가 거리에 비례**: 코앞 `MinDamageInterval`(0.1초) ↔ 최대 사거리 `MaxDamageInterval`(0.5초) 선형 보간 → 가까울수록 빠르게 깎인다
  4. 연사 휴식(`BurstCooldown`) 중엔 트레이서가 안 나가므로 데미지도 멈춘다(체류 시간은 유지). 지형에 가리면 무효
- 각도는 **리드 조준점 기준**으로 잰다 — 타겟의 현재 위치로 재면 트레이서는 타겟 뒤로 빗나가는데 데미지가 들어가는 화면이 나온다. 리드는 `|D + V·t| = s·t`의 정확한 해(쏜 기체 속도 상속 포함). 고정점 반복 근사는 측면 교차에서 조준점이 5~7m 어긋났다.
- AI는 기총 사거리(`MaxRange` 800m) 안에서 이 리드 조준점으로 기체를 돌린다. 미사일용 요격점(내 기체 속력 기준)을 향하면 훨씬 빠른 탄이 타겟 앞을 지나간다.
- 기본값 기준 SAV 전투기(체력 54) 격추 시간 — 사격 중 시간 / 연사 휴식 포함:

  | 거리 | 데미지 주기 | 초당 | 격추 |
  |---|---|---|---|
  | 100m | 0.15초 | 26.7 | 2.6초 / 약 4.3초 |
  | 200m | 0.20초 | 20.0 | 3.3초 / 약 5.5초 |
  | 400m | 0.30초 | 13.3 | 4.7초 / 약 7.8초 |
  | 800m | 0.50초 | 8.0 | 7.5초 / 약 12.5초 |

- 시각 파티클은 프리팹 빌더가 기총의 탄속·사거리·`Spread`(시각 전용)로 만든다(월드 시뮬레이션, Transform 기준 방출기 속도, 속도 상속 Initial).

**플레이어 미사일(`ACO_DFUNC_Missile`)** — SAV `DFUNC_AAM` + `SAV_AAMController` 대체. SaccEntity의 `Dial_Functions_L/R`에 넣는 다이얼 기능이고, 미사일 본체는 `ACO_Missile`을 그대로 쓴다.
- **표적**: `TargetLayers`(기본 29 적 항공기 + 30 적 지상)에 있는 씬 오브젝트를 빌드 시 `ACO_SceneBaker`가 `Targets`에 굽는다(SAV의 AAMTargets 규칙과 같고 자기 기체 안은 뺀다). SaccEntity의 `AAMTargetsLayer`와 무관하다. 0.1초마다 전체 후보를 보고 기준 방향에 가장 가까운 표적을 고르며, 지금 표적이 아직 `LockAngle` 안이면 유지한다(SAV는 프레임당 1개씩 돌아 표적이 많으면 락온이 느리다). `LockTime` 동안 각도 안에 두면 락온. 지형(`ObstacleLayers`)에 0.25초 넘게 가리면 놓는다.
- **장전(에이스컴뱃 방식)**: 슬롯 `SlotCount`개. 장전된 슬롯마다 한 발씩 쏠 수 있고(트리거 한 번에 한 발), 쏜 슬롯은 예비탄이 있으면 `SlotReloadTime` 뒤 다시 장전된다. 예비탄이 없으면 그 슬롯은 빈다. 표시 잔탄 = 예비탄 + 장전(중)인 탄. 슬롯은 번갈아 쓴다(좌우 파일런, `LaunchPoints[slot % 길이]`). 락온 없이 쏘면 직진(`AllowUnguidedFire`). 보급 지점은 한 틱에 총 탄수의 10%씩 채운다.
- **성장 요소**: 피해·최고속도·가속·선회·유도 방식·항법 상수·근접 신관·락온 거리/각도/시간·슬롯 수·장전 시간·총 탄수가 모두 public 필드다. 외부 스크립트가 바꾸면 다음 발사부터 적용(슬롯 수·총 탄수는 `ResetAmmo()`). 비행 성능은 발사 이벤트(`NetLaunch`, 파라미터 8개)에 실어 보내므로 다른 클라이언트도 같은 궤적을 그린다. 피해는 명중을 판정하는 쏜 사람의 값만 쓴다.
- **명중 판정은 쏜 사람**(`ACO_Missile.ShooterJudgesHit`) — SAV와 같다. 격추 점수·작전 기록이 `SaccTarget.LastHitByPlayer`(=데미지를 보낸 사람)로 매겨지기 때문. NPC 미사일은 지금처럼 피격자가 판정한다.
- `ACO_Missile`은 발사하는 쪽이 `EnableWeapon` 전에 `LaunchTarget`·`LaunchMissileType`·`LaunchInheritSpeed`·`PoolParent`·`ShooterJudgesHit`·`IsShooter`를 넣는다(특정 런처 타입에 묶이지 않는다).
- **HUD 슬롯 막대**: `SlotBarTemplate`(꺼 둔 원본) 하나를 슬롯 수만큼 복제해 `SlotBarSpacing` 간격으로 늘어놓는다(`ResetAmmo`마다 다시 만들므로 슬롯 수 강화에도 맞는다). 자식 `Fill`의 X 스케일 = 장전 진행도(빈 슬롯 0, 장전 완료 1), 자식 `Ready`(선택)는 장전 완료 시 켜진다. `Fill`은 피벗이 왼쪽 끝이어야 왼쪽부터 찬다.
- **선택 시 표시** (`SelectedObjects`, `SlotBarsOnlyWhenSelected`): `SelectedObjects`는 이 무장을 선택했을 때만 켜지고 선택 해제·하차·특수무장 교체 시 꺼진다(시작 시 꺼 둔다, 조종사 화면 전용). `SlotBarsOnlyWhenSelected`(기본 켬)면 슬롯 장전 막대도 선택했을 때만 보인다(끄면 예전처럼 장착 중이면 늘 보임). 특수무장 슬롯은 선택을 장착된 모듈에 넘기므로 모듈마다 설정한다.
- 외부 조회: `IsLocked()`, `GetLockProgress()`, `GetAmmo()`, `GetSlotProgress(i)`, 슬롯별 `GetLockSlotCount()/GetLockObject(k)/IsLockedAt(k)/GetLockProgressAt(k)`.
- **교체 도구** (`ACO_PlayerMissileSetup`, **기체 셋업** 1단계 — `ACO → Tools → 기체 셋업 (한 번에)` / `ACO/Player Vehicle/Set Up Selected Vehicles`): 선택한 SAV 기체의 DFUNC_AAM 옆에 ACO_DFUNC_Missile을 붙이고 참조(SAVControl·HUDControl·조준 기준·소리·표적 표시·잔탄 텍스트·애니메이터·락온 각도/시간/거리)를 옮긴다. 발사 지점은 기존 것 + 기체 좌우 대칭 위치(`AAMLaunchPoint_Mirror`). SaccEntity 다이얼 배열의 DFUNC_AAM 자리를 바꾸고 DFUNC_AAM은 끈다(지우지 않음). 씬 인스턴스만 고친다(SAV 프리팹 에셋은 그대로).
- **HUD 막대 생성**: 잔탄 텍스트(SF-2: `HUDController_static/bigstuff/HudCanvas/AAMs`, 캔버스 단위 약 271×98, 아래로 AGM·폭탄·로켓이 약 73 간격) 오른쪽에 `ACO_MissileSlotBars`를 만든다. 막대 170×14, 간격 8, 같은 캔버스·같은 HUDText 머티리얼·같은 색(가산 블렌딩이라 알파 = 밝기: 배경 0.18, 장전 중 0.5, 완료 1). HUDText 셰이더는 텍스처 없는 Image도 색 그대로 그린다. ⚠ HUDText는 한 번 그린 픽셀의 스텐실을 지운다(Ref 1, Comp Equal, Op Zero — 가산 HUD가 겹쳐 과하게 밝아지지 않게). 그래서 겹친 자리엔 **먼저 그린 것만** 남으므로 형제 순서를 밝은 것부터 `Ready → Fill → Back`으로 둔다(배경이 먼저면 Fill/Ready가 전혀 안 보인다 — 실제로 겪음). 런타임도 원본의 순서를 이렇게 다시 맞춘다. 다시 만들기: `ACO/Player Vehicle/Rebuild HUD Slot Bars on Selected Vehicles`.

**다중 유도 미사일** (`ACO_DFUNC_Missile.MaxLockTargets` ≥ 2, 4AAM 류): 락온 상태가 칸 배열(`MaxLockTargets`개)이다. 칸마다 잡은 표적이 각도 안이면 유지하고, 빈 칸은 아직 안 잡은 표적 중 기준 방향에 가장 가까운 것으로 채워 칸마다 따로 락온 시간을 잰다. 트리거 한 번에 일제 발사한다: `OneMissilePerTarget`(기본 켬)이면 락온한 표적마다 한 발씩(표적 2개면 2발, 나머지 슬롯은 남는다), 끄면 장전된 슬롯을 모두 쏴 표적들에 차례로 나눠 준다(표적이 1개면 전부 그 표적에). 장전은 일반 미사일과 같은 슬롯 방식(쏜 슬롯만 `SlotReloadTime` 뒤 다시 장전). 칸이 1이면 일반 미사일 — 단, 탐색 원 가운데(기준 방향)에 확실히 더 가까운 표적이 생기면 그쪽으로 옮겨 잡는다(`CenterSwitchMargin` 1.5° 또는 지금 표적 각도의 25% 중 큰 만큼 더 가까울 때, 0.1초마다 확인, 락온 시간은 새로 잰다 — 두 표적 사이에서 깜빡이지 않게. 0이면 예전처럼 각도 밖으로 나갈 때까지 유지). 다중 락온은 그대로(잡은 칸 유지). 주 표적 = 처음으로 채워진 칸(SAV 표적 표시·단일 조회용) — 칸을 옮기지 않아 마커 연출이 끊기지 않는다. 일제 발사는 한 프레임에 슬롯 수만큼 `NetLaunch`를 보내므로 `[NetworkCallable(20)]`. 표적 마커는 칸마다 깜빡임·이름표·마름모(풀 `LockDiamonds`, `MAX_LOCKS` 8)를 따로 그린다.

**키보드 단축키**: SAV는 데스크톱 키를 `SAV_KeyboardControls`(`Lfunc1~10`/`Rfunc1~10` = 기능 ↔ 키)로 연결한다. 기능을 바꿔 끼우면 키가 꺼진 옛 기능을 가리키므로 Bake(`ACO_PlayerMissileSetup.FixKeyboardBindings`, 셋업 메뉴들도 Bake를 부른다)가 옮긴다 — DFUNC_AAM → 같은 오브젝트의 ACO_DFUNC_Missile, 특수무장 슬롯 모듈(SAV 폭탄/로켓)이나 특수무장 자리에서 빠져 꺼진 옛 기능 → 특수무장 슬롯. 다이얼에 있는 기능·켜져 있는 키보드 전용 기능은 건드리지 않는다. 셋업 검사가 남은 옛 키를 경고한다.

**특수무장 슬롯 / 격납고** (`ACO_DFUNC_Special`, `ACO_Loadout`, `ACO_HangarButton`, 프리팹 `ACO_Hangar`)
- 슬롯: SaccEntity 다이얼 한 칸(오른쪽 다이얼 Element 2, 데스크톱 키 숫자 3)에 고정으로 두는 다이얼 기능. 실제 무장은 자식의 모듈(`Modules`, 지금은 ACO_DFUNC_Missile)이고 `ModuleIds[i]`가 무장 ID다. 다이얼 배열은 건드리지 않고, SaccEntity가 슬롯에 보내는 이벤트(선택/탑승/폭발/보급…)를 장착된 모듈에 넘긴다. 모듈은 다이얼 배열에 없으므로 `EntityControl`/`LeftDial`/`DialPosition` 주입과 `SFEXT_L_EntityStart`도 슬롯이 대신 한다. 탄약 초기화(폭발·리스폰)는 모든 모듈에 보낸다(바꿔 끼웠을 때 가득 차 있게).
- 장착(플레이어별): 조종석에 앉으면(`SFEXT_O_PilotEnter`) 격납고에서 고른 무장 ID로 모듈을 골라 내 화면은 바로 바꾸고 다른 클라이언트에 `NetEquip`으로 알린다. 늦게 들어온 플레이어에게는 조종사가 다시 보낸다. 바꿀 때는 지금 상태(탑승/조종/선택)를 옛 모듈에서 내리고 새 모듈에 다시 올린다. 내린 모듈은 `SetEquipped(false)` — 입력·락온·소리·HUD 막대를 쉬고 잔탄 텍스트를 쓰지 않는다(모듈들이 텍스트를 함께 쓴다). 모듈별 표시 오브젝트 — 기체 외형(`ModuleVisuals`), HUD 탄종(`ModuleHudLabels`), MFD 탄종(`ModuleMfdLabels`), 다이얼 아이콘(`ModuleDialIcons`) — 은 장착된 모듈 것만 켜고 나머지는 끈다(시작할 때 모두 끄고 장착된 것만 켠다 — 씬에 켜 둔 채여도 된다). 장착된 무장이 없으면 `EmptyObjects`(빈 파일런, "NO WPN" 등)만 켠다. 인스펙터(`ACO_DFUNC_SpecialEditor`)는 모듈마다 한 칸(번호·무장 ID·모듈 이름 아래 네 오브젝트)으로 보여 주고, 배열 길이를 모듈 수에 맞춘다. 여러 오브젝트는 빈 부모로 묶어 부모를 넣는다. 카탈로그가 모듈을 넣고 뺄 때 네 배열을 같은 칸으로 맞춘다.
- 격납고(로컬 전용): 무장 목록 `SpecialIds/Names/Descriptions`(같은 순서), 버튼이 목록 번호로 고르고 고른 무장의 이름·설명을 패널에 띄운다. 고른 값은 이 클라이언트에만 있다. 무장을 더하면 목록 기본값을 늘리고 격납고 프리팹을 다시 만든다. Bake가 기체의 슬롯을 씬의 첫 격납고에 연결한다.
- SAV 무장 모듈: 폭탄(UGB)·로켓(RKT)은 SAV `DFUNC_Bomb`을 그대로 모듈로 쓴다(SF-2의 로켓도 DFUNC_Bomb을 로켓 설정으로 쓴다 — 오브젝트 이름으로 가른다). (예전 방식 — 이 도구는 없앴다. 기체 셋업이 같은 ID의 SAV 모듈을 찾으면 ACO 모듈로 바꾼다.) SAV 기능은 탑승자 수를 세므로(PilotEnter/Exit 짝) 슬롯이 시작할 때 억지로 끄지 않고, 장착/해제 신호(`_ACO_Equip`)도 ACO 모듈에만 보낸다(`GetUdonTypeName`으로 가른다). 탄약 초기화를 모든 모듈에 보낼 때 장착된 모듈을 마지막에 보낸다(잔탄 텍스트를 함께 쓰므로 장착된 값이 남게). SAV 무장엔 우리 슬롯 장전·강화 필드·장전 막대가 없고 보급 때만 채워진다 — 에이스컴뱃식 낙하 지점 표시나 유도 폭탄이 필요하면 ACO 모듈로 따로 만든다.
- **폭발 범위 표시** (`ACO_Missile`·`ACO_Ordnance`의 `SplashVisual`): 터질 때 이 오브젝트(반투명 구 메시 등 — ExplosionObject 안에 두면 함께 켜지고 꺼진다)의 크기를 범위 피해 반경(강화 반영)에 맞춘다 — 스케일 = 반경 / `SplashVisualRadius`(스케일 1일 때 반지름, Unity 기본 구 0.5) / 부모 크기. `SplashVisualGrowTime`(0.25초) 동안 5%에서 반경까지 커진다(0이면 바로). 범위 피해가 없으면(반경 0) 건드리지 않는다. 반경은 발사 이벤트에 실어 보낸다(쏜 사람의 강화 반영 값 — 모든 화면에서 같은 크기): `NetLaunch(target, slot, maxSpeed, acceleration, turnRate, proximity, splash)` — 유도 방식·비례항법 상수는 강화로 바뀌지 않아 빼고 받는 쪽 무장 값을 쓴다. `NetLaunchOrdnance(slot, maxSpeed, acceleration, splash)`. 미사일의 범위 피해는 쏜 사람 화면에만(`SplashRadius`), 표시는 모든 화면(`VisualSplashRadius`). 폭탄·로켓은 모든 화면에 반경을 넣지만 범위 피해는 쏜 사람만(`IsShooter`).
- **ACO 폭탄/로켓** (`ACO_Ordnance`, 프리팹 `ACO_Bomb`/`ACO_Rocket`, 모듈 UGB/RKT): 발사는 ACO_DFUNC_Missile이 그대로 한다(슬롯 장전·강화·장전 막대) — 탄체가 ACO_Ordnance면 모기체 **속도 벡터**를 이어받고(`LaunchVelocity`) 비행 성능·범위 피해를 넣는다. 락온은 안 한다(`TargetLayers` 0, `AllowUnguidedFire`). `HoldToFire`면 누르고 있는 동안 `FireInterval`마다 연사. 탄체는 매 프레임 이동 구간 Linecast(`HitLayers` = Default·Water·Environment·Walkthrough)로 지형/유닛에 닿으면 터지고(`ArmTime` 동안은 충돌 안 함, 내 기체 콜라이더는 무시), `SplashRadius` 안의 SaccTarget마다 한 번씩 피해 = `Damage` × (1 → `MinSplashFraction`, 콜라이더 경계까지 거리에 선형). 호위 대상(ACO_EscortUnit)은 피해를 받지 않는다. 피해는 쏜 사람만(격추 점수). SAV 폭탄은 범위 피해를 리지드바디가 있는 대상에만 줘서(`attachedRigidbody`) NPC 지상 유닛엔 직격만 들어갔다 — 그래서 따로 만들었다. 기본값: UGB 슬롯 2 / 24발 / 4초, 0.3초 간격 투하, 중심 150·반경 35m. RKT 슬롯 8 / 72발 / 3초, 0.08초 연사, 450m/s, 중심 45·반경 12m. 모듈은 무장 카탈로그(6-22)의 UGB/RKT 정의로 기체 셋업이 넣는다 — 발사 지점·발사음·잔탄 텍스트는 SAV 폭탄/로켓(DFUNC_Bomb)에서 가져오고 그 SAV 기능은 빼서 끈다(앞서 SAV 기능을 UGB/RKT 모듈로 넣었으면 바꿔 끼운다). 장전 막대 묶음 이름은 모듈마다 다르다(`ACO_MissileSlotBars_Module_UGB` 등).
  - **발사 동기화 (SAV 방식)**: 폭탄/로켓은 전용 이벤트 `NetLaunchOrdnance(슬롯, 최고 속력, 가속)` 9바이트만 보낸다. 받은 쪽은 자기 화면의 기체 발사 지점과 `SaccAirVehicle.CurrentVel`(다른 클라이언트에선 SAV_SyncScript가 채우는 동기화 속도)에서 쏜다 — SAV 폭탄과 같다. 이벤트가 오는 동안 기체가 앞으로 간 만큼(폭탄 ~20m, 로켓 수십 m) 다른 사람 화면의 궤적이 어긋나지만 연출만이고, 피해는 쏜 사람 화면 기준이라 판정엔 영향이 없다(쏜 사람의 위치·속도·시각을 보내 맞추는 방법은 대역폭 대비 이득이 작아 쓰지 않기로 했다). 로켓 연사가 초당 10발을 넘으므로 `[NetworkCallable(60)]`. (6-41부터 유도 폭탄·로켓을 위해 표적 번호·선회율·시커 범위가 더해져 `NetLaunchOrdnance(슬롯, 최고 속력, 가속, 반경, 표적, 선회율, 시커)` 7칸 — 무유도는 표적 없음.)
  - **CCIP**(폭탄 낙하 지점, `CcipReticle`): 추진 없는 탄체를 고르면 `CcipInterval`(0.05초)마다 다음 발사 슬롯의 발사 지점에서 기체 속도로 폭탄과 같은 식(중력 → 공기 저항 → 이동)을 굴려 지형/물에 닿는 점을 찾는다(처음 3초는 0.1초, 그 뒤 0.25초 스텝, 최대 `CcipMaxTime` 40초, 유닛 히트박스 17은 빼고 지형만). 표시는 표적 마커와 같은 방식(머리에서 그 방향으로 30m, 고정 화면 크기 `CcipAngle` 3°, ACO/MarkerOverlay, `PostLateUpdate`). 표시 오브젝트(원 테두리 + 가운데 점)는 폭탄 모듈을 만들 때 모듈 아래에 생긴다(`ACO_HUDBuilder.CreateCcipReticle`). UI 레이어 5라서 관전 카메라에 찍히지 않는다.
  - **예측 = 실제(정확히 같은 궤적)**: 폭탄·로켓(`ACO_Ordnance`)은 프레임 속도와 상관없이 고정 간격 `FLIGHT_STEP`(0.02초)으로 계산한다(추진 → 중력 → 공기 저항 → 이동, 화면에는 남은 시간만큼 속도로 앞으로 그려 90fps에서도 매끄럽게). CCIP는 같은 점화식 v(n+1) = a·(v(n) + G), x(n+1) = x(n) + h·v(n+1)의 닫힌 꼴로 n번째 위치를 바로 구해 표본점(처음 3초 0.1초, 그 뒤 0.24초 간격) 사이를 선으로 검사한다 — 예전엔 예측만 0.1/0.25초 간격으로 따로 적분해 고도 따라 탄착점이 20~30m 짧게 찍혔다. `Drag` 0이면 저항 없는 식(차이는 게임 감각뿐 — 비용·정확도는 같다).
  - 범위 피해는 지형을 무시한다(산·기둥 뒤 유닛도 반경 안이면 피해).
- 패널 UI: 작전 콘솔·격납고 모두 월드 공간 Canvas(Default 레이어, 1단위 = 1mm, `CanvasScaler`+`GraphicRaycaster`+`VRCUiShape` — VRChat SDK가 만드는 UI와 같다) + UI 버튼. 버튼의 `onClick` → 버튼 오브젝트의 U# 스크립트에 `SendCustomEvent("Click")`(VRChat UI 이벤트 허용 목록). 콜라이더가 있으면 Interact로도 눌린다.
- 설치: **기체 셋업** 4~5단계 — 오른쪽 다이얼 Element 2 자리에 빈 슬롯을 넣고 그 기능은 끈 뒤, 무장 카탈로그(6-22)의 모든 정의(4AAM·UGB·RKT 포함)를 모듈로 복제해 넣는다(소리·HUD·발사 지점·미사일은 일반 미사일에서, 잔탄 텍스트는 바꿔 낀 기능의 것(SF-2: `HudCanvas/Bombs`)을 모듈들이 같이 쓰고 그 옆에 모듈마다 장전 막대). 데스크톱 키는 숫자 3으로 연결한다(SAV_KeyboardControls — 슬롯을 가리키는 칸이 있으면 그 키를, 없으면 빈 칸에 넣고, 같은 키를 쓰던 다른 칸은 비운다).
- 4AAM 기본값: 공중 표적(29)만, 4칸 동시 락온, 표적당 한 발, 슬롯 4 / 총 16발, 쏜 슬롯마다 12초 뒤 재장전, 락온 3.5km·25°·1.2초, 비례항법, 락온 없이는 발사 안 함.

### 4.4 데미지 판정 · 격추 · 부활 ✅

**누가 명중을 판정하는가**

| 무기 | 판정자 |
|---|---|
| `ACO_Missile` (NPC가 쏨) | **피격 대상의 체력 소유자**(NPC → 그 SaccTarget의 오너, 플레이어 → 그 플레이어 자신). 비행 중 오너십이 바뀔 수 있어 명중 순간에 확인한다 |
| `ACO_NPCGun` (NPC가 쏨) | **타겟으로 지정된 기체의 오너만** 조준 체류 시간으로 계산 |
| SAV 미사일/총 (플레이어가 쏨) | SAV 규칙 그대로(쏜 쪽) |

자체 미사일을 만든 이유가 이것이다: 미사일은 위치를 동기화하지 않고 각자 재현하므로, 쏜 쪽이 판정하면 디싱크로 **"내 화면엔 안 맞았는데 데미지를 입는"** 일이 생긴다. 피격자가 자기 화면 기준으로 판정하면 "내가 본 것 = 내가 입는 데미지"가 항상 일치한다. 트레이드오프: 제3자 시점의 미세한 시각적 불일치는 허용. 판정 후 데미지 전파는 SaccTarget/SaccEntity의 기존 경로(로컬 적용 + 0.2초 묶음 전파)를 그대로 쓴다.

**격추**: SaccTarget → `ExplodeOther`의 AI/SAM에 `Explode()` → AI 정지 + `HideOnDeath`/`DisableCollidersOnDeath` 처리. 락온 마커 끄기와 폭발 연출은 SaccTarget의 애니메이터가 한다(2.4).

**부활**: 프리팹의 `SaccTarget.RespawnDelay = 0`(자동 부활 안 함 — SAV 문서가 안내하는 방식). 스폰/웨이브 매니저가 유닛의 **`Respawn()`** 하나만 호출하면 전역 전파 → 오너가 `SaccTarget.RespawnTarget()`으로 체력 복구(한 명만 해야 소유권 쟁탈이 없다) + 모든 클라이언트가 위치 리셋·표시 복구·AI 재개. SaccTarget엔 부활 통지 이벤트가 없어서 이 방식으로 폴링을 없앴다.

### 4.5 네트워킹

**구현됨 ✅ — 전투기 기동 동기화 (오차 기반 데드 레커닝)**
- 구조: 동기화는 `ACO_NPCFighterNet`이 맡는다(AI와 같은 오브젝트 — 소유권이 같아야 한다). AI와는 모드/웨이포인트/표적 조회·적용, 소유권 변경 통지, 상태 변경 표시(`MarkStateDirty`)로만 주고받는다. 매 프레임 호출은 AI의 Update가 한다(비행 계산 → 운동 측정 → 전송 순서를 고정하려고). 네트워크 인스펙터 값(전송 기준, 보정 시간, `DebugNetwork`)은 Net 컴포넌트에 있다.
- **교전 상대에게 소유권 넘기기** (`HandoffToEngagedPlayer`): 오너는 같은 플레이어와 `HandoffHoldTime`(2초) 이상 교전(추격/이탈)하면 그 조종사에게 NPC 소유권을 넘긴다(`Networking.SetOwner`). 그 플레이어 화면에서는 NPC가 직접 계산되어 튀지 않으므로 리드 사격·미사일 예측이 정확하다. 조종사 = 표적 엔티티가 `Occupied`일 때 그 엔티티의 오너(SAV는 탑승 시 기체 소유권을 가져간다). 빈 기체·호위 대상(NPC)은 넘기지 않는다. 소유권이 바뀐 뒤 `HandoffCooldown`(5초) 동안은 다시 넘기지 않는다. 표적은 놓침 거리 이탈·격추·우선 표적 등장 때만 바뀌므로 자주 넘어가지 않는다. 넘겨도 그대로 동작하는 이유: NPC 무기 명중은 피격자가 판정하고, 플레이어가 NPC를 맞힌 데미지는 SaccTarget이 Self+Others로 보내 오너가 사수여도 적용되며, CombatDirector는 오너가 여럿이어도 동작하게 짜여 있다. 루트에 있는 SaccTarget 체력 소유권도 함께 넘어간다. 플레이어가 나가면 VRChat이 소유권을 마스터에게 돌려준다.
- 오너만 AI와 비행을 계산한다. **원격은 AI를 재생하지 않는다** — 추격 중엔 클라이언트마다 보는 타겟 위치가 달라 재생 궤적이 금방 갈라지기 때문(이전 방식의 튐 원인).
- 동기화 필드(52바이트): 모드, 웨이포인트, 타겟 인덱스, 위치, **기수 방향**, 속력, 가속도, **기수 방향 각속도**, 서버 시각. **롤은 보내지 않는다.**
- 원격 예측 모델: "기수가 같은 각속도로 계속 돈다". 선회 중 월드 각속도는 거의 일정하므로 직선 외삽과 달리 **원호를 그대로** 따라간다. 오너는 `(f0×f1)/dt`로 기수 방향 각속도를 잰다.
- 롤: 원격이 예측 선회율에서 정상선회 뱅크각을 역산(`tan(bank) = 선회율·속력 / (TurnG·g)`)하고 `MaxRollRate`로 따라간다.
- 전송 조건: 오너가 원격과 **같은 예측(ghost)을 돌려 보고**, 실제와 `PositionErrorThreshold`(4m + 가장 가까운 다른 플레이어 거리 1km당 `ThresholdPerKm` 10m) 또는 `DirectionErrorThreshold`(5°) 이상 벌어질 때, 모드/타겟/웨이포인트가 바뀔 때, `HeartbeatInterval`(4초)마다. 최소 간격 `MinSendInterval`(0.3초). (6m/10°/0.5초에서는 기동 중 패킷마다 기수가 평균 12°씩 다시 맞춰져 튀어 보였다. 그때 전체 전송량은 초당 2.3회라 여유가 있다.)
- 전송 경로: 상태는 **파라미터 네트워크 이벤트**(`NetState`, `[NetworkCallable]`)로 보낸다 — SAV_SyncScript가 위치를 보내는 방식과 같다. 동기화 변수로 보냈을 때는 Manual인데도 VRChat이 0.25초마다 다시 직렬화했고, 새 상태가 평균 1.5초 늦게 도착해(전송 구간 자체는 0.1초) 기체가 튀고 미사일이 빗나갔다. 풀에서 꺼낼 때 수십 초 전 직렬화 상태가 다시 넘어와 28km씩 튀는 문제도 있어서 AI의 동기화 변수는 없앴다. 늦게 들어온 플레이어에게는 들어올 때 이벤트를 한 번 보내고, 놓쳐도 4초 하트비트가 맞춘다. 상태 시각으로 순서를 가려 뒤바뀐 옛 상태는 버린다. 이벤트 파라미터는 8개까지라 모드·웨이포인트·표적(+1)을 int 하나에 묶는다.
- 혼잡 제어: `Networking.IsClogged` 동안 위 기준·간격을 최대 `MaxCongestionScale`(4)배까지 늘리고, 풀리면 천천히 되돌린다.
- 수신 시: 이미 적용한 것보다 옛 상태는 버린다(격추된 NPC는 마지막 상태가 반복 전달됨). 보낸 시각부터 흐른 시간(≤2초)을 1/30초 스텝으로 따라잡고, 화면과의 차이는 위치·방향 오프셋으로 들고 있다가 임계 감쇠 스프링(w = 2/`SmoothTime`, 0.5초면 약 1초에 90% 흡수)으로 없앤다. 받을 때 **위치만 잇고 보정 속도는 0에서 다시 시작**한다. 지수 감쇠는 받는 순간 옆으로 미는 속도(오차/시간)가 갑자기 생겨 기동 중 '통통' 튀어 보였고, 받기 직전의 화면 속도까지 잇는 스프링(이전 시도)은 틀린 움직임이 다음 보정으로 넘어가 오차가 누적됐다. `HardSnapDistance`(200m) 초과는 즉시 이동.
- **실측 교훈 (2클라이언트 로컬 테스트, 18기)**: 롤까지 4° 기준으로 보던 1차 버전은 초당 약 50회를 보냈고(97%가 롤 때문), VRChat은 초당 약 22회만 전달했다 → 큐가 밀려 지연 0.35초→2.4초, 보정 오차 중앙값 142m, 순간이동 562회. **NPC 전체 전송량은 초당 20회보다 충분히 낮게** 유지해야 한다.
- 시뮬레이션(도그파이트 + 잔기동, 편도 250ms): 위 기본값에서 NPC당 약 1.0회/초, 원격 위치 오차 평균 12m / 95% 25m, 프레임 간 튐 최대 약 2m.
- 오너 쪽 잔떨림 제거: 타겟 속도를 위치 미분 대신 `SaccAirVehicle.CurrentVel`(SAV가 동기화·평활)이나 NPC 비행 컨트롤러 값에서 직접 받고(0.25초 평활), 분리 벡터도 검사 주기마다 계단식으로 바꾸지 않고 0.3초에 걸쳐 따라간다. 원격 플레이어 위치를 미분한 속도는 네트워크 보간 때문에 흔들려 리드 조준점(속도 × 최대 3초)과 NPC 기수를 떨게 했다.
- 3차 실측(2클라이언트, 18기): 전송 28회/초, 혼잡 없음, 지연 중앙값 0.12초, 순간이동은 첫 수신뿐. "통통 튐"의 주원인은 네트워크가 아니라 지면 근처의 상승/하강 반복이었다(5.1 지형 회피 참고). 4차 실측에서는 같은 전송량에도 전달량이 12.6회/초로 떨어져 지연 2~3초·순간이동이 재발했고 `IsClogged`는 한 번도 켜지지 않았다 → 세션마다 망 여유가 다르므로, 계속 문제면 **묶음 전송 + 보간** 방식(매니저 1개가 전 NPC를 0.5초마다 1패킷으로, 원격은 스냅샷 사이를 위치·속도 에르미트 보간; 시뮬레이션 오차 최대 0.3m, 대가는 원격 화면 0.6~1초 지연)으로 전환한다.
- `DebugNetwork`: 켜면 `[ACO_NET]` 로그(전송 이유·오차·혼잡도 / 수신 간격·지연·보정량 / 프레임 튐 / 외부 이동)를 남긴다. 로그 위치: `%LOCALAPPDATA%Low\VRChat\VRChat\output_log_*.txt`.
- 기총: 원격은 예측 자세로 발포를 판단하므로 피격자(타겟 오너)는 **자기 화면에 보이는 NPC 기수 방향대로** 맞는다.
- 오너가 바뀌면 화면에 보이던 자세에서 AI를 이어받는다(순찰이면 패턴 새로 선택). 부활 시 0.5초 뒤 한 번 더 보내 이벤트/패킷 순서 역전을 덮는다.
- 시간 기준은 `Networking.GetServerTimeInMilliseconds()`(int). `double` 동기화는 SAV에 선례가 없어 피했다. int 뺄셈이라 오버플로가 나도 짧은 구간 경과 시간은 정상이다.
- 타겟을 **인덱스로** 동기화한다 — `AAMTargets`는 빌드 때 베이크되어 모든 클라이언트가 같은 배열을 갖기 때문.
- SAM은 타겟 인덱스만 동기화하고, 포탑 각도는 각 클라이언트가 동기화된 타겟 위치로 로컬 계산한다.
- **발사**는 런처의 전역 이벤트 1회, **투사체는 위치 동기화하지 않는다.**

**설계만 있음 ⏳**
- **LOD**: 원거리 유닛의 AI 틱/동기화 빈도 낮추기, `behaviour.enabled = false`.
- **오너십 분산**: 접속 플레이어들에게 NPC 소유를 라운드로빈 배분, `OnPlayerLeft` 시 재분배. 현재는 기본 오너(마스터)가 전부 담당.
- **지상 유닛 스테이지 재배치**: 매니저의 `[UdonSynced] byte CurrentStage` 하나로 전 유닛을 로컬 재배치. ⚠ `SendCustomNetworkEvent`는 **늦게 들어온 플레이어에게 도달하지 않으므로** 위치는 이벤트가 아니라 synced 상태로 들고 있어야 한다.

---

## 5. NPC 전투기 ✅

### 5.1 비행 모델 (`ACO_NPCFlightController`)

- **뱅크-투-턴**: 방위 오차에 비례한 목표 뱅크각(`BankPerYawError` 2.5, 최대 `MaxBankAngle` 75°)으로 먼저 눕고, 선회는 **정상선회 공식** `선회율 = TurnG·9.81·tan(뱅크) / 속도`에서 나온다. 롤이 먼저 들어가고 기수가 따라 돌며, 빠를수록 선회 반경이 커진다. 수평 비행으로 돌아오면 목표 뱅크가 0이 되어 롤이 저절로 펴진다.
- **제어 축은 월드 기준으로 분리한다**: 방위 오차(지평선 위) → 뱅크 → 선회 / 상승각 오차 → **지평선 기준 축**으로 피치.
  - 이유: 기울어진 기체 좌표에서 피치 오차를 재면 옆의 타겟이 "위"로 잡혀 선회할 때마다 당긴 힘이 고도로 새고, 아래 타겟도 선회 중엔 위로 보여 내려가지 못한다(도그파이트에서 상승만 하던 원인).
  - 시뮬레이션: 선회 타겟 25초 추격 시 기존 +304m 상승·최근접 96m → 분리 후 고도 유지·최근접 3m.
- Unity 회전 부호(검증): 로컬 +Z = 왼쪽 롤, 월드 +Y = 우선회, 수평 우측 축 + = 기수 하강.
- **지형 회피**: `MinAltitude`(80m)는 절대 Y가 아니라 **아래 지형까지의 여유 고도**. `ProbeInterval`(0.15초)마다 아래 Raycast로 지형 높이를, 전방 SphereCast(속도 × `LookAheadTime` 3초, 반경 15m)로 산·구조물을 잰다. 하강 중엔 전방 캐스트를 **수평**으로 쏜다(기수 방향으로 쏘면 아래 평지가 장애물로 잡혀 상승/하강을 반복). 회피는 원하는 방향을 덮어쓰지 않고 **상승각에 연속 바닥을 건다**: `바닥 각 = asin(((MinAltitude - (여유고도 + 수직속도 x SinkLeadTime 0.8초)) / AltitudeSettleTime 2초) / 속도)`, 전방 장애물은 긴급도만큼 바닥 각을 `MaxAvoidClimbAngle` 쪽으로 보간(긴급도는 즉시 상승, 천천히 해제). ⚠ `max(바닥 각, MaxAvoidClimbAngle x 긴급도)`로 합치면 장애물이 없을 때 그 항이 0도라 바닥 각이 0도로 고정되어, 고도 5km에서도 수평 아래로 내려가지 못했다(진단 로그로 발견). 높을 땐 자유 하강, 최소 고도에 가까울수록 0도로 수렴해 바닥에 붙어 수평 비행한다. 예전 스위치식(80m 미만이면 25~55도 상승으로 덮어쓰기)은 낮은 타겟 추격 시 상승/하강을 반복해 "통통" 튀었다(시뮬레이션 32초 동안 전환 8회 → 0회). 아래에 아무것도 없으면 `GroundHeight`를 바닥으로 쓴다. 매 프레임 캐스트하지 않는 건 유닛 수만큼 비용이 늘기 때문. 저고도 급강하를 꺼리면 `LookAheadTime`을 1.5~2초로 줄인다.

### 5.2 상태머신 (`ACO_NPCFighterAI`)

| 모드 | 동작 | 전이 |
|---|---|---|
| **Patrol** | 웨이포인트 순회 → 대기 지점(`HoldPoint`) 선회 → 자유 기동 패턴(직진/좌·우선회/상승/하강/위브) 순으로 우선 | 타겟 탐지 → 자리가 있으면 Pursuit, 없으면 Wait |
| **Pursuit** | 요격점 조준(5.3) + 우회 슬롯(5.4)으로 추격, 기총 사거리 안에선 탄속 리드로 조준(4.3), 교전 조건이면 런처로 조준/발사 | 타겟이 앞에 있고 `BreakOffDistance`(350m) 이내 → Extend / `MaxEngageTime` 경과 + 대기자 있음 → Wait(교대) / 자기 방어 후 정원 초과 → Wait / 타겟 상실 → Patrol |
| **Extend** | 타겟 반대 방향으로 이탈, 고도는 `타겟 고도 + ExtendAltitudeAboveTarget`(300m)으로 수렴 | `ReEngageDistance`(1400m)까지 벌어지면 → Pursuit (`MaxExtendDuration` 15초는 안전장치). 이탈 중에도 공격 자리는 유지 |
| **Wait** | 공격 정원이 차서 타겟 중심 `WaitOrbitRadius`(2500m, `LoseTargetRange`의 80% 이하) 원을 `타겟 고도 + WaitAltitudeAboveTarget`(400m)에서 선회. 궤도에서 1.3배 넘게 벗어나면 최대 속도. 미사일/기총 사용 안 함 | 차례가 오면(재탐색 주기마다 확인) → Pursuit / 타겟이 `DefendDistance`(800m) 안 → 즉시 Pursuit(자기 방어) / 다른 타겟에 자리가 나면 갈아탐 |

- 이탈을 **시간이 아니라 거리로** 끝내는 이유: 시간 기반이면 지상 타겟 위에서 거리를 못 벌린 채 재진입해 선회만 반복한다.
- **타겟이 앞에 있을 때만** 이탈한다 — 뒤에 붙은 적을 두고 직진 이탈하면 그게 도망이다. 뒤에 있으면 선회전을 계속한다.
- 이탈 고도를 타겟 기준으로 둔 이유: 고정 상승각이면 이탈할 때마다 고도가 래칫처럼 쌓인다.
- 교전 조건: 거리 300~2000m, 기수와 타겟 사이 15° 이내. 조건 밖이면 `ReleaseAim()`으로 락온 감쇠.

### 5.2-1 공격 정원 (`ACO_CombatDirector`)

정원이 없으면 탐지 범위 안의 NPC가 전부 한 플레이어에게 몰린다(네트워크 테스트 로그: 원격 수신 상태의 91%가 추격 모드). 씬에 디렉터 1개를 두면:

- **정원**: 타겟당 동시 공격(Pursuit+Extend) `MaxAttackersPerTarget`(2)기. 나머지는 Wait로 선회.
- **대기 순서**: 먼저 기다린 NPC부터 투입(`GetWaitStartTime`). 순서 없이 "빈자리를 먼저 본 NPC"가 가져가게 하면 시뮬레이션에서 600초 동안 한 번도 공격 못 한 NPC가 생겼다.
- **교대**: `MaxEngageTime`(30초) 넘게 공격했고 **바로 투입 가능한** 대기자가 있으면 자리를 넘기고 `YieldCooldown`(10초) 동안 재신청하지 않는다. 쿨다운 중인 대기자는 "대기자"로 세지 않는다 — 세면 넘긴 자리를 아무도 못 받아 공격이 빈다.
- **자기 방어**: 대기 중 타겟이 `DefendDistance`(800m) 안이면 정원과 무관하게 교전. 위협이 지나가면 **가장 늦게 합류한** 공격자 한 명이 Wait로 돌아간다(모두가 동시에 빠지지 않게 합류 순서로 고름).
- **타겟 분산**: 타겟 선택 시 정원이 찬 타겟은 `FullTargetPenalty`(1500m)만큼 먼 것으로 본다. 대기 중에도 재탐색해 자리가 난 다른 타겟으로 갈아탄다.
- **동기화 없음**: 판단은 각 NPC 오너가 로컬로 하고, 근거(다른 NPC의 모드/타겟)는 이미 동기화된다. 대기 시작 시각·공격 시간처럼 동기화하지 않는 값은 **같은 오너의 NPC끼리만** 비교한다. 오너가 여럿이면 같은 순간 두 오너가 자리를 잡아 잠깐 정원+1이 될 수 있다.
- 설정은 모두 디렉터 인스펙터에서 일괄 조정한다: 정원·교대(`MaxAttackersPerTarget`, `MaxEngageTime`, `YieldCooldown`, `FullTargetPenalty`)와 대기 선회·자기 방어(`WaitOrbitRadius`, `WaitAltitudeAboveTarget`, `DefendDistance`). NPC별 차이는 `LoseTargetRange`에 따른 선회 반경 자동 축소뿐이다.
- 디렉터가 없으면(`Director` 비어 있음) 예전처럼 정원 없이 모두 공격한다.
- **시각화** (`ShowGizmos`, `ShowLabels`): 플레이 모드에서 씬 뷰에 NPC→타겟 선(빨강 추격 / 주황 이탈 / 하늘색 점선 대기), 타겟별 "공격 n/정원 · 대기 n" 라벨, 대기 선회 원, 자기 방어 구, NPC별 "추격 12s (교대까지 18s)" / "대기 #2 · 쿨다운 6s" 라벨을 그린다. 실제 로직은 Udon이 돌리므로 각 NPC의 UdonBehaviour 변수(`_mode`, `_target`, `_engageTimer` 등)를 이름으로 읽는다 — AI에서 이 필드 이름을 바꾸면 기즈모 스크립트의 상수도 바꿔야 한다. 편집 모드에선 디렉터를 선택하면 연결된 NPC로 점선을 그린다.
- 상태 전환 시뮬레이션(900초): 정원 초과 0%, 공격자 0명 구간 0%, 18기 vs 1명에서 NPC별 공격 시간 90~124초(공평 기대치 100초). 대기 중 위협이 잦은 경우 위협이 지난 뒤 정원 초과가 최장 약 10초 남는다(이탈 중인 공격자는 재진입 시 정리).

### 5.3 추격 조준 — 요격 해

`|D + V·t| = s·t`를 t에 대해 푼다(D = 타겟−나, V = 타겟 속도, s = 내 속력). 가장 이른 양의 해를 쓰고 `MaxLeadTime`(3초)으로 상한, 해가 없으면(따라잡을 수 없으면) 순수 추적.

- 이렇게 바꾼 이유: 이전의 `거리 / 내 속도`는 접근 속도를 무시해서, **정면 접근 시 플레이어가 더 빠르면 조준점이 NPC 뒤에 찍혀 반대로 선회**했다(검증: 플레이어 200 vs NPC 140, 2000m 정면 → 기존 lead 14.3초로 조준점이 NPC 뒤 857m).
- 타겟 속도는 위치 차분으로 추정하되, 0.2초 넘게 끊긴 샘플은 버린다(이탈 후 재추격 시 몇 초 전 위치로 계산해 수만 m/s가 나오던 문제).

### 5.4 편대 간격 — 우회 접근 슬롯 + 분리

같은 타겟을 같은 비행 모델로 순수 추격하면 전원이 같은 점으로 수렴해 겹친다. 두 장치를 쓴다.

**① 우회 접근 슬롯 (예방)**
- NPC마다 **슬롯 각도**(0~360°) 하나를 갖는다. 나→타겟 시선을 축으로 하는 원판 위의 방향이다: 0° = 시선의 오른쪽, 90° = 위, 180° = 왼쪽, 270° = 아래.
- 추격 조준점을 그 방향으로 `ApproachOffsetDistance`(600m)만큼 비킨다. 위아래 성분은 절반만 쓴다(아래로 크게 돌면 지형과 겹치므로).
- 비키는 정도는 거리에 따라 줄어든다: `가중치 = clamp01((거리 − ApproachConvergeDistance) / ApproachFadeRange)` — 2300m 이상이면 전부, 1500m에서 0. 즉 **멀리서는 각자 다른 방향으로 펼쳐져 들어오다가 사거리 안에서 타겟으로 모인다.** 결과적으로 여러 방향에서 동시에 들어오는 협공처럼 보인다.
- 비키는 방향이 매 프레임 "지금 시선" 기준으로 다시 계산되므로, NPC가 움직이면 기준축도 함께 돈다. 그래서 고정 좌표로 우회하는 게 아니라 **타겟을 중심으로 슬롯 방향의 사면(斜面)을 타고 들어가는** 궤적이 된다.
- 슬롯 각도는 `ApproachSlotAngle`(음수 = 자동). 자동이면 **배치 위치의 정수 해시**로 정한다: `(round(x)·73856093 ⊕ round(y)·19349663 ⊕ round(z)·83492791) & 0x7fffffff mod 360`.
  - 정수 해시를 쓰는 이유: 슬롯은 원격 클라이언트도 똑같이 계산해야 하는데(동기화하지 않음), 삼각함수 기반 해시는 클라이언트마다 부동소수점이 비트 단위로 달라 큰 수를 곱하면 각도가 완전히 달라질 수 있다. 반올림한 정수 연산은 모든 클라이언트에서 같다.
  - 배치 위치를 쓰는 이유: 씬에 미리 배치(풀링)하므로 모든 클라이언트에서 같고, 별도 설정 없이 NPC마다 다르다.
  - 한계: 같은 자리에 겹쳐 배치한 NPC는 슬롯도 같아진다 → `ApproachSlotAngle`을 직접 지정한다. 해시라 두 NPC의 각도가 우연히 가까울 수도 있는데, 그건 ②가 처리한다.

**② 분리 (안전망)**
- `SeparationRadius`(250m) 안의 다른 NPC마다 `멀어지는 방향 × (1 − 거리/반경)`을 더해, `SeparationWeight`(1.5)를 곱해 목표 방향에 섞는다. 가까울수록 강하다. 모든 모드에서 동작한다.
- `NeighborCheckInterval`(0.2초)마다 계산해 캐시한다.

- 둘 다 위치만으로 계산하는 결정론적 로직이라 **추가 동기화가 없다.**
- **공중 충돌 방지**: 플레이어 기체와 NPC 전투기는 서로 통과한다(충돌 데미지로 한 번에 추락하지 않게). 레이어·충돌 매트릭스는 건드리지 않고 `Physics.IgnoreCollision`으로 콜라이더 쌍만 끈다 — 기총(파티클 충돌)·미사일(근접신관)·범위 피해·지형/항모 충돌·NPC끼리·플레이어끼리는 그대로. 충돌 데미지는 기체 오너(조종사) 화면에서만 계산하므로(SAV는 `!IsOwner`면 무시, NPC는 SaccTarget이라 충돌 데미지 없음) **각 클라이언트는 자기가 조종 중인 기체만** 건다. Bake가 양쪽의 충돌 콜라이더(트리거·WheelCollider 제외)를 굽는다: NPC `BodyColliders`/`PlayerVehicles`, 플레이어 기체 확장(`ACO_VehicleUpgrades`) `BodyColliders`/`NPCFighters`. 거는 때: 탑승할 때 내 기체 × NPC 전부(2프레임 뒤부터 NPC 한 대씩 프레임마다 — SAV가 콜라이더 레이어를 바꾼 다음), NPC가 켜질 때(OnEnable)·부활할 때(바로 + 0.5초 뒤) 그 NPC × 내가 조종 중인 기체(`IsLocalPilot`, 조종 중이 아니면 안 함) — 무시 상태는 콜라이더가 꺼졌다 켜지면 풀릴 수 있어서. 비용은 거는 순간뿐(한 번에 내 기체 콜라이더 × NPC 한 대 콜라이더 — 보통 수십 회, Bake 로그에 나온다)이고 그 뒤 물리 비용은 없다. **다른 공중 유닛**(폭격기·수송기·아군 공격헬기 = 항로 비행 `ACO_EscortUnit` MOVE_AIR, 적 헬기 = `ACO_MobileUnit` KIND_HELICOPTER)도 같은 규칙: Bake가 유닛 루트에 `ACO_AirCollisionIgnore`를 붙이고(콜라이더·플레이어 기체를 채운다) 플레이어 기체 확장 `OtherAirUnits`에 넣는다 — 탑승할 때 전투기 다음에 한 대씩, 유닛이 켜질 때(OnEnable), 그리고 1초마다 내 기체가 2.5km 안이면 다시(이 유닛들은 격파·부활 때 콜라이더를 껐다 켜 무시 상태가 풀릴 수 있다 — 가까운 몇 대만이라 가볍다).
- 주변 NPC 목록(`Neighbors`)은 **빌드 시 `ACO_SceneBaker`가 베이크**한다(SAV의 `IVRCSDKBuildRequestedCallback`, SAV 다음 순서 11, 비활성 풀 포함) — 런타임 탐색 비용 0.
- 시뮬레이션(3기가 40~60m 간격으로 뭉쳐 출발, 선회 타겟 30초 추격): 미적용 최소 간격 6m·100m 이내 체류 100% → 적용 최소 88m·체류 1.4%.

### 5.5 프리팹 구성 (`NPC_Fighter`)

```
NPC_Fighter (레이어 17)   BoxCollider(히트박스), Animator(Target.controller),
                          SaccEntity(AAMTargetsLayer=25, Extension=[AI]),
                          SaccTarget(Health 100, RespawnDelay 0, ExplodeOther=[AI]),
                          ACO_NPCFlightController, ACO_NPCFighterAI, ACO_NPCFighterNet
├ Model (레이어 17)        비주얼 — AI.HideOnDeath
├ AAMTarget (레이어 29)    트리거 — 플레이어 락온 마커 (사망 시 애니메이터가 끔)
├ Effects                 폭발 연출 (사망 시 애니메이터가 켬)
├ MissileLauncher         ACO_MissileLauncher (열추적, LockTime 1.5초, 모기체 속도 상속)
│ └ LaunchPoint_L / _R
└ Gun (기수)               ACO_NPCGun
  └ Tracers               시각 전용 파티클(충돌 끔)
```

---

### 5.6 플레어 (`ACO_FlareDispenser`, `ACO_Missile`) — 에이스컴뱃식: 켜져 있으면 100% 회피 (NPC·플레이어 공통)

- **모듈화**: 플레어 규칙·연출은 **플레어 장치 `ACO_FlareDispenser`** 하나가 맡는다 — NPC 전투기·수송기·폭격기가 같은 부품을 루트에 붙인다(미사일은 표적 마커에서 `GetComponentInParent<ACO_FlareDispenser>`로 찾는다). NPC 전투기 AI는 `Flares`로 장치를 잇고 **등급별 값만 넣는다**: `ApplySkill`에서 `FlareChance` = `RookieFlareChance`/`VeteranFlareChance`/`AceFlareChance`, `ReactDistanceMultiplier` = 신병 0.7 / 베테랑 1 / 에이스 1.3, 부활 때 `ResetSalvos`. 수송기·폭격기는 고정 확률 0.5·간격 4초·12개 방출. 아래의 횟수·활성 시간·반응 거리·중력·연출(이미터 방식 포함) 필드는 모두 장치 쪽에 있다(예전에는 AI 안에 있었다). 예전 프리팹(장치 없음)은 셋업 검사가 알리고 [고치기]가 장치를 붙인다(이름에 flare가 들어간 파티클을 잇는다). 슈퍼에이스 기만체는 `AI.Flares.HasIncomingWithin`으로 접근 미사일을 본다.

- **접근 감지**: `ACO_Missile`이 발사될 때 표적이 NPC 전투기면(`GetComponentInParent<ACO_NPCFighterAI>`) 그 AI에 `RegisterIncoming`으로 등록하고, 표적을 놓치거나 터지거나 회수될 때 `UnregisterIncoming`으로 뺀다. 모든 클라이언트가 등록한다(소유권이 넘어가도 이어지게).
- **투하 판단(오너)**: 0.2초마다 등록된 미사일 중 아직 쫓고 있고(`IsTracking`) `FlareReactDistance`(1.5km, 신병 ×0.7·에이스 ×1.3) 안에 들어온 것에 대해 **미사일당 한 번** 숙련도별 확률(`RookieFlareChance` 0.1 / `VeteranFlareChance` 0.5 / `AceFlareChance` 0.9)을 굴린다. 성공하면 `NetFlare`를 모두에게 보내고 `FlareCooldown`(3초) 동안 다시 안 뿌린다. 출격당 `FlareSalvos`(6)회.
- **효과(100%)**: `NetFlare`(모든 클라이언트)가 `FlareActiveTime`(2.5초)를 시작한다. 그동안 이 NPC를 쫓는 미사일은(켜진 동안 새로 쏜 것 포함) 다음 추적 프레임에 표적을 놓치고(`_decoyed`) 플레어 지점을 직접 추적하다가 근접 신관 반경 안에 들어오면 피해 없이 터진다. 플레어가 꺼지면 그대로 직진한다. 플레어 지점 = 뿌린 자리 + 플레어 속도(기수 방향 기체 속력 × 0.8 − 25m/s) × t + ½·g·`FlareGravity`(0.35)·t² — 연출 파티클과 같은 값으로 맞췄다.
- 각 클라이언트가 같은 이벤트로 같은 시각에 켜므로 결과도 같다(확률은 오너가 한 번만 굴린다). 명중 판정은 쏜 사람 화면이라 그 화면의 결과가 기준이다.
- 연출: `FlareParticles`(프리팹 빌더가 꼬리 아래에 만든다 — 월드 시뮬레이션, 기체 속도 상속, 약한 중력)에서 `FlareParticleCount`(8)개를 한 번에 방출, `FlareSound`.
- 횟수는 모든 클라이언트가 같이 센다(`NetFlare`가 모두에게서 줄이고, `Init`·부활 때 채운다).
- **플레이어 기체(SAV 대응탄)도 같다**: NPC·SAM이 쏜 `ACO_Missile`은 표적 기체의 대응탄 수(미사일 `MissileType`에 맞는 `NumActiveChaff/Flares/OtherCM`)가 0보다 크면 100% 속는다. SAV `DFUNC_Flares`는 투하를 모두에게 보내고 각자 `FlareActiveTime`(4초) 동안 개수를 올려 두므로 어느 화면에서나 같은 시각에 켜진다. 끌려가는 지점은 속은 순간의 기체 위치에서 기체 속도 × 0.8로 날며 중력 × 0.5로 떨어지는 점(SAV 플레어는 리지드바디라 정확히 같진 않다 — 연출만), 대응탄 수가 0이 되면 직진. 예전 SAV식 확률 판정(`FlareEffect`)은 쓰지 않는다. 채프는 레이더 미사일(MissileType 0)만 속인다(SAV 규칙 그대로).
- **대응탄 종류 무시(기본)**: `ACO_Missile.AnyCountermeasure`(기본 켬) — 표적 기체가 채프/플레어/기타 중 무엇이든 켜면 속는다(에이스컴뱃식). SAV는 종류를 따로 세서 레이더 미사일(`MissileType` 0 — 빌더의 NPC 전투기·SAM 기본값)엔 채프만 통하므로, 플레어만 있는 기체(SF-2 `DFUNC_Flares.FlareType` 1)로는 NPC 미사일을 피할 수 없었다. 끄면 SAV처럼 종류가 맞아야 통한다.
- **플레어 연출 방식**(`ACO_FlareDispenser.FlareUseEmitter`, 연출만 — 미사일이 속는 시간은 `FlareActiveTime` 그대로): 끔 = 한 번에 `FlareParticleCount`개, 켬 = 파티클 시스템 Emission을 `FlareEmitDuration` 동안 켰다 끈다(양·간격은 파티클 시스템의 Rate/Bursts — 간격을 두고 뿌리려면 Bursts에 간격, 평소엔 꺼 둔다, 다시 뿌리면 끄는 시각이 늦춰진다).

## 6. SAM 포대 ✅

- 탐지: `AAMTargets` 순회 + `DetectRange`(4000m) + 레이더 콘(`RadarConeAngle`, 180이면 전방위) + 최소 고도(20m, 지면 클러터) + LOS. 기존 타겟은 `LoseTargetRange`(5000m)까지 유지해 락온 진행을 버리지 않는다.
- 교전: 조건을 만족하는 동안 런처에 `AimAt` → `IsLocked()`이면 발사, 이후 `PostFireDelay`(2초) 대기. 조건이 깨지면 `ReleaseAim`.
- 포탑: `TurretYaw`/`TurretPitch`를 각 클라이언트가 로컬 계산(추가 트래픽 0). 추적할 타겟이 없어지면 `ReturnToRestDelay`(2초) 뒤 회전 속도 한계를 지키며 시작 자세로 돌아간다(대공포도 같음). 부활 시엔 즉시 시작 자세.
- 프리팹 `NPC_SAM`: 루트(레이어 17, BoxCollider, Animator, SaccEntity, SaccTarget Health 200, ACO_SAMSite) + `Model` + `AAMTarget`(레이어 30) + `Effects` + `TurretYaw/TurretPitch/Rail/MissileLauncher`(레이더, 4발, LockTime 3초 — 회피 기회를 준다). 사망 시 `Model`과 포탑을 숨긴다.

### 6-1. 대공포 ✅ (`ACO_AAGunSite`)

- 구조는 SAM과 같다: `AAMTargets` 순회 탐지(`DetectRange` 2500m, `LoseTargetRange` 3000m, 최소 고도 20m, LOS), 타겟 인덱스만 동기화, 포탑은 각 클라이언트가 로컬 계산, `Explode`/`Respawn`.
- 사격은 **ACO_NPCGun 재사용**(4.3 체류 방식, 타겟 오너가 판정). 포대는 매 프레임 모든 클라이언트에서 `GunTick`을 부른다 — 피격자는 자기 화면에서 포신이 자신을 향할 때만 맞는다.
- **회피 설계**: 포탑은 `TrackingLag`(0.6초)만큼 늦게 따라가는 타겟 속도로 리드를 잡고(+ `AimError` 0.8° 흔들림), 기총의 명중 판정은 **현재** 속도로 잡은 리드와 비교한다. 직진/완만한 선회는 둘이 일치해 맞고, 급기동하면 어긋나 빗나간다. 포탑 회전 한계(`YawRate` 70°/s, `PitchRate` 50°/s) 때문에 근거리 고속 통과도 못 따라간다.
- 기총 값(빌더): 사거리 2000m, 탄속 1000m/s, 발포각 8°(넓게 — 피하는 중에도 계속 쏴서 위협이 보이게), 유효각 2.5°, 체류 0.6초, 연사 2.5초/휴식 1.5초, 데미지 3, 주기 0.15~0.6초.
- 시뮬레이션(40초, 250m/s, 체력 54):

  | 기동 | 데미지 | 발포 시간 |
  |---|---|---|
  | 직진 통과 | 54~66 (한 번 통과에 거의 격추) | 9~10초 |
  | 완만한 선회 6°/s | 51 | 10초 |
  | 근처에서 꾸준한 급선회 20°/s | 75 (머무르면 위험) | 25초 |
  | 지그재그 ±25°, 3초 주기 | 3 | 10초 |
  | 지그재그 ±40°, 2초 주기 | 0 | 10초 |

  `TrackingLag`를 줄이면 기동에 강해지고(어려워짐), 늘리면 피하기 쉬워진다. 지그재그도 맞게 하려면 기총의 `EffectiveAngle`을 키운다.
- 프리팹 `NPC_AAGun`: 루트(레이어 17, BoxCollider, Animator, SaccEntity, SaccTarget Health 120, ACO_AAGunSite) + `Model` + `AAMTarget`(레이어 30) + `Effects` + `TurretYaw/TurretPitch/Barrel/Gun(Tracers)`. 사망 시 `Model`과 포탑을 숨긴다.
- SAM/대공포 공통 격추 연출 필드: `HideOnDeath`(숨김), `ShowOnDeath`(켬 — 화재/잔해 등), `DisableCollidersOnDeath`. 빌더는 대공포 수치를 `GunSpec`으로 받아 함포/CIWS에도 같은 생성 코드를 쓴다(지상 대공포 값은 위 그대로).

### 6-3. 전투기 숙련도 ✅

- **등급별 외형**: `ACO_NPCFighterAI`의 `RookieVisuals`/`VeteranVisuals`/`AceVisuals`에 그 등급의 오브젝트(모델 통째로, 도색, 표식, 트레일)를 넣으면 그 등급의 것만 켜진다(`ApplyTierVisuals`, 모든 클라이언트 — 생성기가 등급을 동기화). 모델들은 `HideOnDeath`에 넣은 공통 부모 아래에 두는 걸 권하지만, 모델을 `HideOnDeath`에 직접 넣어도 부활 직후 등급 외형을 다시 적용하므로 다른 등급 모델이 같이 켜지지 않는다.
- 편대 단위로 신병(0)/베테랑(1)/에이스(2)를 정한다. 인스펙터에 맞춰 둔 전투기 값이 **베테랑(1.0)** 기준이고, 생성기의 단계별 배율 표 `[신병, 베테랑, 에이스]`를 곱한다(기본):

  | 항목 | 신병 | 베테랑 | 에이스 |
  |---|---|---|---|
  | `TurnG` (`TierTurnGMultiplier`) | 0.8 | 1.0 | 1.25 |
  | `MaxSpeed`/`CruiseSpeed` (`TierSpeedMultiplier`) | 0.9 | 1.0 | 1.1 |
  | 런처 `LockTime` (`TierLockTimeMultiplier`) | 1.4 | 1.0 | 0.6 |
  | 기총 `DwellTime` (`TierGunDwellMultiplier`) | 1.5 | 1.0 | 0.6 |
  | `MissileFireConeAngle` (`TierMissileConeMultiplier`) | 0.7 | 1.0 | 1.3 |
  | 최대 체력 (`TierHealthMultiplier` × AI `BaseHealth`) | 0.66 (99) | 1.0 (150) | 1.33 (199.5) |

- **체력 배율**: 전투기·공격기 `BaseHealth`(베테랑) 150 → 신병 99 / 베테랑 150 / 에이스 199.5. AI `BaseHealth`(비었으면 Bake가 SaccTarget 체력으로 채우고, 일반 전투기의 예전 기본 100은 150으로)에 배율을 곱해 `SaccTarget.FullHealth`에 넣는다. FullHealth는 동기화되지 않고 SaccTarget.Start가 시작 체력으로 덮으므로 숙련도 적용 2프레임 뒤 모든 클라이언트에서 넣고(체력 바 비율이 화면마다 같다), 부활 체력 채우기(오너, `RespawnTarget`)도 이 값을 쓴다. 공격기 포함, 슈퍼에이스는 배율 없이 `BaseHealth`.

- 미션 시작/일반 증원 확률은 생성기의 `VeteranChance`(0.35)/`AceChance`(0.05). 웨이브는 미션 관리자가 확률을 정한다(웨이브가 거듭될수록 상승).
- 단계는 전투기별로 동기화하고 **모든 클라이언트**에서 적용한다 — 비행/락온은 오너가, 기총 체류 시간은 피격자(타겟 오너)가 쓰기 때문. AI는 처음 적용할 때 인스펙터 값을 저장해 두고 배율을 곱하므로 여러 번 적용해도 누적되지 않는다. 스폰 전에 적용해야 순항 속도가 반영된다.
- `VeteranVisuals`/`AceVisuals`(AI 인스펙터): 해당 단계일 때만 켜는 오브젝트(전용 도색, 트레일 등).

### 6-8. 부위 파괴형 함선 ✅ (`ACO_Warship`, 프리팹 `NPC_Warship` 전함 / `NPC_Destroyer` 구축함)

| | 전함 `BB` (`HighValue` 켬) | 구축함 `DD` (`HighValue` 끔) |
|---|---|---|
| 풀 | 고가치 표적 — 타격 페이즈 목표 | 지상 유닛 — 일반 유닛 |
| 본체 | 120m, 체력 1000, 장갑 4(미사일 직격 25%), 1500점 | 80m, 체력 500, 장갑 2.5(직격 40%), 500점 |
| 부위 | VLS 2, 함포 1, CIWS 2 — 부위당 본체 12% | VLS 1, CIWS 1 — 부위당 본체 20% |
| 순항 | 반경 2.5km, 12m/s, 호위 간격 500m | 반경 1.5km, 14m/s, 호위 간격 350m |

- **구조**: 루트(SaccTarget + 선체/함교 BoxCollider + 락온 마커 + Animator) 아래 `Body`(모델 `Model`, 부위들, 격침 연출 `DeathEffects`). 부위는 `ACO_SAMSite`(VLS: 포탑 없음, 셀이 위를 보고 콜드런치 미사일 `ACO_Missile_VLS`) / `ACO_AAGunSite`(함포, CIWS)이고, 부위마다 자기 SaccTarget/SaccEntity/히트박스/락온 마커가 있어 **따로 락온·파괴**된다. SaccTarget 규칙("자식 콜라이더 금지")상 본체 콜라이더는 루트에, 부위 콜라이더는 각 부위 루트에.
- **본체 판정**: SAV 미사일 데미지 = `AAMDamage × FullHealth ÷ ArmorStrength`라 장갑이 미사일 몇 발에 가라앉을지를 정한다. 기총은 `NoDamageBelow`에 걸려 선체엔 안 들어간다. **부위가 파괴될 때마다 본체가 최대 체력의 `PartHullDamage`를 잃는다** — 본체 오너가 SaccTarget 체력을 깎고 `SendNetworkUpdate`로 SAV 동기화 경로를 탄다. 부위는 "살아 있는 걸 본 뒤" 파괴됐을 때만 센다. 마지막 공격자는 모든 클라이언트에서 본체 기록(`LastHitByPlayer`)으로 옮겨 작전판이 격침자를 맞게 쓴다.
- **격침**: 함선은 그 자리에 멈추고(순항 중지), 남은 부위는 `Silence()`(교전 중지, 히트박스 끔, 모델 그대로 — 점수 없음, 마커에서 빠짐 `IsUnitSuppressed`). `HideOnDeath`/`ShowOnDeath`(연출용, 프리팹은 `Body/DeathEffects`)/`DisableCollidersOnDeath`. **가라앉는 연출은 본체 Animator의 격침 애니메이션**으로 한다 — `Body`를 움직일 것(루트는 스크립트가 순항시키므로 애니메이션으로 루트 위치를 건드리면 안 된다). SAM/대공포에도 `ShowOnDeath`가 있다.
- **순항**: 스폰 지점을 중심으로 반경 `CruiseRadius` 원을 `CruiseSpeed`로 돈다. 위치 = f(서버 시간)이라 **스폰 후 동기화 0**. 시작 각도와 도는 방향은 스폰 방위각에서(생성기가 함선엔 정수+0.5도 방위). `BobAngle`/`BobPeriod`로 약하게 흔들린다. 씬에 직접 둔 함선은 그 자리를 중심으로 바로 돈다.
- **편대(호위)**: 호위함의 배치 위치/회전은 기함과 **같은 값**(순항 중심/방위)이고, 편대 슬롯은 생성기 동기화 배열 `_groundLeader`(기함코드 × 16 + 슬롯)로 받는다. 호위함은 기함의 반경/속도로 같은 궤도를 계산하고 슬롯(기함 `EscortSpacing` 기준: 좌후·우후·정후·정전·…)만큼 떨어져 간다 — 결정적이라 추가 동기화가 없고, 기함이 격침돼도 호위함은 같은 궤도를 계속 돈다.
- **스폰**:
  - 전함: 타격 페이즈 후보로 뽑히면 바다(순항 원 + 편대 폭 `EscortSpacing × 2` + `SeaClearance`)에 놓고 **호위 구축함 `EscortCount`(2)척**을 붙인다. 근처에 바다가 없으면 그 함선은 건너뛰고 다음 후보(육상 표적).
  - 구축함: 미션 시작(전체 배치) 때 **함대 `BackgroundShipGroups`(2)개**를 공역 아무 곳 바다에 — 함대 = 기함 1 + 호위 0~2(`ShipGroupSizeMin~Max` 1~3척). 지상 그룹/방공 링/페이즈 지상 목표/증원 웨이브에는 함선을 쓰지 않는다.
  - 함대 후보 = 쓰지 않던 함선 → 파괴된 함선. 살아 있는 함선은 건드리지 않는다. 권장 풀: 구축함 = 배경 함대 최대 6 + 전함 호위 2 × 전함 수.
- **바다 판정**(생성기 "해상 배치"): 해수면 `SeaLevel`, 지면 Raycast가 `SeaLevel − MinSeaDepth`보다 깊거나 `SeaSurfaceLayers`(물 콜라이더)에 맞거나 아무것도 안 맞으면(`NoHitIsSea`) 바다. 순항 원 위 `SeaCheckPoints`(12)점도 전부 바다·안전지대 밖이어야 한다.
- **육상 판정**: 지면이 맞아도 `SeaLevel + LandMinAboveSea`(1m)보다 낮거나 물 콜라이더에 먼저 맞으면 육상 유닛을 놓지 않는다(`RejectBelowSeaLevel`, 기본 켬). 육각 지형은 해안 칸 경사면(스커트)이 바닷속까지 내려가 완만한 지면으로 찍히므로 꼭 필요하다. 전차 순찰로/매복 줄(`PathClear`)도 150m마다 같은 검사를 한다.
- **부위 카테고리**: 생성기 `CAT_PART`(3) — 전함 부위(소속 = 고가치 표적)와 구축함 부위(소속 = 지상 유닛) 모두. 배치/배치 번호는 소속 함선, 생사는 부위 SaccTarget(`PartOwner/PartOwnerCategory`). 관리자는 부위 격추를 지상 격추로 센다(VLS 300, 함포 250, CIWS 150 × 페이즈 배율). 마커/작전판에 `VLS`, `GUN`, `CIWS`로 따로 나온다. 구축함 본체는 지상 유닛이라 교착 판단(적 전력)에도 들어간다. 순항하는 함선은 전투기 엄호 임무 대상에서 뺀다(배치 위치 ≠ 실제 위치).
- 부위용 진입점(SAM/대공포 공통): `AttachedSpawn()`(부모가 `MissionRevive`를 넣고 부른다 — 위치는 안 건드림), `Silence()`. 원거리 잠재우기(2.9)는 부위에 그대로 적용된다.

### 6-9. 호위 ✅ (`ACO_EscortUnit`, 프리팹 `NPC_Transport`, 페이즈 ESCORT)

- **호위 대상**(`ACO_EscortUnit`): 미션 생성기가 정한 항로(경유점 `EscortRoutePoints` 2~6개)를 **서버 시간으로** 따라간다 — 위치 = f(항로, 출발 시각, 경과 시간 × `Speed`)라 출발 후 동기화가 없고 늦게 들어와도 같은 곳에 있다. 경유점 사이는 Catmull-Rom 곡선(구간별 길이를 미리 재서 일정한 속도), 공중이면 선회율에 비례해 뱅크(`BankPerTurnRate`, 최대 `MaxBank`). 도착 판정도 시간 함수라 모든 클라이언트가 같은 순간에 도착한다. 체력은 같은 오브젝트의 SaccTarget.
- **확장(이동 방식 `MoveMode`)**: 0 공중(항로 고도 그대로) / 1 해상(해수면, 흔들림) / 2 지상(0.2초마다 지면 Raycast를 따라감). 생성기는 이동 방식에 맞는 항로를 만든다 — 공중 = 지형 + `EscortTerrainClearance` 이상의 `EscortAltitude`, 해상 = 경유점과 그 사이가 모두 바다(6-8 판정), 지상 = 지면. **지금 ESCORT 페이즈는 공중 수송기를 쓰고, 해상 선단/지상 콘보이는 같은 유닛·항로에 프리팹만 바꾸면 된다** — 다만 적 NPC가 배/차량을 공격하는 대함·대지 공격 AI는 아직 없어서(지금 적 전투기는 공중 표적 위주), 그 AI가 생기면 페이즈 종류만 더하면 된다.
- **적이 노리게**: 락온 마커(`AAMTarget`)를 **아군 항공기 레이어 25**에 둬서, 빌드 때 적 SaccEntity의 `AAMTargets`에 자동으로 들어간다(적 전투기/SAM/대공포가 기존 로직 그대로 탐지·공격). 적 전투기는 `AttackPriority`(3000m)만큼 이 표적을 가깝게 보고, **교전 중이라도 더 높은 우선순위 표적이 탐지 거리에 들어오면 다시 고른다**(표적이 처음 활성으로 보일 때 부모의 ACO_EscortUnit을 찾아 캐시). 공격 정원(5.2-1)은 그대로라 한 번에 2기가 수송기를, 나머지는 플레이어를 노린다.
- **오사 방지**: 플레이어 미사일은 29/30만 락온해 수송기를 노리지 않는다. 기총은 SaccTarget `DisableBulletHitEvent`로 막는다 — NPC 기총/미사일은 파티클이 아니라 직접 판정(`WeaponDamageTarget`)이라 영향이 없다.
- **페이즈 진행**(관리자 `UpdateEscort`):
  - 항로: **목적지에서 거꾸로** 만든다. 지형 장식에 도시/비행장이 있으면(`EscortDestinationChance` 1) 그중 하나가 최종 목적지 — 수송기는 그 상공에서, 콘보이는 그 가장자리 `EscortDestinationMargin`(500m) 바깥에서 멈추고 바깥에서 도시 쪽(±60°)으로 들어온다. 없으면(또는 시도의 마지막 1/4) 공역 아무 곳. 출발점은 목적지에서 항로 길이만큼 거슬러 올라간 곳 — **플레이어 위치와 무관하게 공역 어디든**(경유점은 옆으로 `EscortRouteLateral` 4km까지 흩뜨림, 공역 안). 목적지가 장식이면 페이즈 배너에 **"DEST: 이름"**.
  - **도착 연출**(생성기가 도착 방식 `_escortArrival`을 동기화, 위치·속도는 전부 서버 시간 함수라 모든 클라이언트가 같다):
    - **투하**(수송기 → 도시/아무 곳): 마지막 구간에서 목적지 지면 위 `EscortDropAltitude`(600m)로 내려와(내려오는 길에 산이 있으면 순항 고도 그대로), 목적지 상공에서 꼬리 램프로 **보급 상자 + 공수부대 낙하산**(파티클 `DropEffects`, 0.5초마다 2개씩 6번, 종단 속도 6m/s, 땅에 닿으면 사라짐)을 떨구고 수평으로 계속 날며 `DepartClimbRate`(15m/s)로 상승. 방출이 끝나면(`DropDetachDelay` 4초) 연출을 월드에 떼어 놓아 기체가 거둬져도 낙하산이 끝까지 내려간다(다음 출발 때 다시 붙인다).
    - **착륙**(수송기 → 비행장): 경유점 6개 — 출발 → 중간 → 중간 접근점(최종 접근 시작점에서 `LandingIntermediateDistance` 8km 앞, 활주로 위 1000m+, 활주로 축에서 ±40°) → 최종 접근 시작점(접지점에서 `LandingApproachDistance` 6km, 활주로 위 `LandingApproachHeight` 350m) → 접지점(활주로 시작에서 200m) → 정지점(활주로 가운데 너머 300m). 착륙 방향은 활주로 양쪽 중 무작위, 접근 구간이 지형에 걸리면 다른 방향/목적지. 최종 접근 시작점부터 **등감속**(120m/s → 접지 약 50m/s → 정지), 활주로 위에서는 `GearHeight`(3m) 높이로 기수 수평. 멈춘 순간이 도착.
    - **정차**(콘보이): 끝에서 `StopDistance`(150m)에 걸쳐 감속해 도시 앞에 선다. 행렬은 같은 시각에 감속해 간격을 유지한 채 선두 뒤로 줄지어 멈춘다.
  - 성공 후 `EscortLingerTime`(30초) 동안 그대로 두었다가 거둔다(투하 후 날아가는 모습/활주로 정지/정차가 보인다, 미션이 끝난 뒤에도). 새 미션의 전체 배치는 바로 거둔다. 항로를 따라 `EscortSites`(2)곳에 방공 시설(SAM/대공포 `EscortSiteSize` 3대 링)을 항로에서 0.8~2.5km 옆 **지면이 있는 곳에만** 놓는다(바다 위 구간은 건너뜀, 목표 아님).
  - 요격: 진행률 5%~80% 사이에 `EscortInterceptWaves`(3)회, 수송기보다 `EscortInterceptLead`(12%) 앞 항로 위 지점에 적 편대(`EscortInterceptBase` 2 + `PerPhase` × (페이즈−1)기)를 보낸다. 편대는 그 지점에서 7~11km 떨어져 나타나 **그 지점 고도**로 기다린다(목표 편대는 기준점이 공중이면 그 고도로 온다 — 제공권/보스 편대도 플레이어 고도로 온다).
  - 도착: 클리어 + `PhaseClearBonus` × 배율 + `EscortBonus`(1000) × 남은 체력 비율 × 배율. 수송기는 도착하면 이탈(거둬들임).
  - 격추: **그 즉시 미션 실패**(`FAIL_VIP_LOST`). HUD 배너 "MISSION FAILED / ESCORT TARGET SHOT DOWN", 디브리핑 "(VIP LOST)". (예전에는 보너스 없이 다음 페이즈였다.) 아군 격추는 점수/적 전력에 들어가지 않는다(카테고리 `CAT_ESCORT` 4는 관리자 격추 집계에서 제외).
  - HUD 페이즈 줄: `PHASE 2/4  ESCORT  VIP 74%  12.3km`(30% 이하 빨강).
  - 조회: `GetActiveEscort()`, `GetLastPhaseFailed()`, 수송기 `GetProgress/GetRemainingDistance/GetHealthFraction/PointAtProgress/IsArrived`.
- **격추 연출**: `HideOnDeath`/`ShowOnDeath`(프리팹 `DeathEffects`)/`DisableCollidersOnDeath`. 공중이면 `CrashFallTime`(8초) 동안 관성 + 중력으로 떨어진 뒤 `HideAfterCrash`(모델)를 숨긴다(연출 전용, 클라이언트마다 로컬).
- **생성기**: 풀 `EscortUnits`(베이커가 `ACO_EscortUnit` 수집), 동기화 배열 `_escortUsed/_escortSerial/_escortTimeMs/_escortTag/_escortRouteCount/_escortRoute`(유닛당 6칸). 후보 = 쓰지 않던 것 → 파괴된 것. `RequestDeployEscort(태그, 기준점, 항로 길이, 방공 시설 수)`, `RequestReleaseTag`는 호위 대상도 거둬들인다.

### 6-10. 전차 / 지상 호위 ✅ (`ACO_MobileUnit`, `NPC_Tank` / `NPC_ConvoyTruck`, 페이즈 CONVOY)

- **전차**(`ACO_MobileUnit`, 지상 풀): 미션 생성기가 준 **지상 항로를 서버 시간으로** 따라간다(경유점 최대 6, Catmull-Rom, `Speed` 10m/s). 위치 = f(항로, 배치 시각, 경과 시간)라 동기화가 없다. 지면은 0.2초마다 Raycast(`GroundLayers`)로 재서 따라가고, 로컬 플레이어가 `LodDistance`(12km)보다 멀면 자세를 `LodInterval`(1초)마다만 갱신한다 — 쏘고 맞는 쪽(가까운 플레이어) 화면에선 매 프레임 정확하다.
  - **포탑/사격 = 같은 오브젝트의 `ACO_AAGunSite`**(재사용): 표적 레이어 = **아군 지상 26**(SaccEntity `AAMTargetsLayer`도 26 — 빌드 시 콘보이/플레이어 지상 차량 마커로 채워짐), `MinTargetAltitude` 해제, 포탑 30°/s·10°/s, 고각 −8~20°. 사격은 `ACO_NPCGun`의 체류 판정을 단발포처럼(체류 1.5초 → 0.25초 사격, 한 발 35, 휴식 4초). 판정은 피격자(타겟 오너). 원거리 잠재우기(2.9)도 그대로 — 콘보이(SaccEntity 없는 표적)가 가까이 오면 깬다. `RequireLineOfSight`라 지형에 가리면 쏘지 않는다.
  - 체력 SaccTarget(150, 장갑 2 → 미사일 직격 50%)은 포탑과 공유, `ExplodeOther` = 포탑 + 전차. 격파되면 멈추고 포탑만 숨긴다(차체 = 잔해, `DeathEffects` 켬).
  - 생성기 배치: 지상 그룹/방공 링/증원에는 쓰지 않는다(함선과 같은 "스스로 움직이는 지상 유닛").
    - **순찰 소대** `BackgroundTankPlatoons`(2) × `TankGroupSize`(3): 공역 아무 곳 지면에 반경 `TankPatrolRadius`(700m) 사각 고리(네 점 모두 지면) — 소대원은 같은 고리를 `TankSpacing`(40m) 간격으로 계속 돈다.
    - **매복조**(지상 호위 페이즈): 호위 항로를 따라 고르게, 항로 옆 `AmbushDistanceMin~Max`(0.4~0.9km) 매복 지점까지 `AmbushApproachDistance`(1.5km) 바깥에서 달려와 멈춘다. 지면이 있는 곳만.
  - 동기화: 전차 칸(`_mobileSlot`, 지상 풀 순서로 정해짐)마다 항로 `_mobileRoute`(6칸)/`_mobileRouteCount`/`_mobileLoop`/`_mobileOffset`(소대 안 앞선 거리). 출발 시각 = 지상 유닛 배치 시각. 적용 시 `SetRoute` → `SetStartTime` → `MissionSpawn`.
- **아군 콘보이**(`NPC_ConvoyTruck` = `ACO_EscortUnit` 지상 이동 방식, `Speed` 20m/s, 락온 마커 **레이어 26**, 체력 250, 플레이어 기총 오사 방지): `ConvoySize`(3)대가 같은 지상 항로를 `EscortConvoySpacing`(60m) 간격 행렬로 간다(선두가 앞 — 유닛별 `_escortOffset`). 짧은 항로는 옆 흩뜨림을 길이의 15%로 줄인다.
  - **지상 항로 만들기**: 목적지(도시/비행장 가장자리, 없으면 아무 곳)에서 거꾸로 직선 위에 경유점 `EscortRoutePoints`(4)개(중간 경유점은 옆으로 흩뜨림). ① 경유점과 그 사이 3곳을 빠르게 확인해 일찍 버리고, ② **차량이 실제로 달리는 Catmull-Rom 곡선**(ACO_EscortUnit과 같은 식)을 `RouteCheckSpacing`(150m)마다 다시 확인한다 — 각 점이 지상 배치 조건(공역, 안전지대, 표면 경사 `MaxGroundSlope`) + **마른 지면**(물 콜라이더 `SeaSurfaceLayers`에 먼저 맞거나 해수면 + `RouteWaterMargin`(1m)보다 낮으면 버림, `AvoidWaterOnGroundRoutes`), 이웃 확인점 사이 **오르내림 경사 `MaxRouteGrade`(18°) 이하**(절벽/급한 둑). 한 점이라도 걸리면 다른 방향으로 다시(`MaxPlacementAttempts` 40회). 해상 항로도 같은 곡선 확인(전부 바다).
  - 확인은 무작위를 쓰지 않아 항로 씨앗 재현(6-4)에 영향이 없다. 비용은 6km 항로 한 번에 Raycast 약 80개 × 시도 수 — 페이즈 시작 때 한 번(사전 확인 + 배치)뿐이다.
  - 항로 옆 매복 전차/방공 시설 자리도 마른 지면만 쓴다. 도로는 모른다(지형만 보고 거의 직선) — 도로를 따라가게 하려면 도로 웨이포인트 지정(⏳)이 필요하다.
- **페이즈 CONVOY**(관리자 `UpdateConvoy`): 살아 있는 차량이 모두 도착하면 클리어 + `PhaseClearBonus` + `ConvoyBonus`(1200) × 생존 비율 × 배율, 전멸이면 그 즉시 미션 실패(`FAIL_CONVOY_LOST`, "CONVOY DESTROYED" / 디브리핑 "(CONVOY LOST)"). 위협 = 매복 전차(콘보이 포격) + 방공 시설(플레이어). 적 전투기는 공중 표적(25)만 노려 콘보이엔 오지 않는다. HUD: `PHASE 2/4  CONVOY  CVY 3/3  4.2km`(가장 뒤처진 차량 기준, 절반 이하 빨강). 조회: `GetConvoyAlive/GetConvoyTotal/GetConvoyRemainingDistance`.

### 6-29. 공격 헬리콥터 / 곡사포 (ACO_MobileUnit.Kind — 지상 풀)

- **공통**: 전차와 같은 이동 유닛 스크립트(`ACO_MobileUnit`, 예전 이름 ACO_Tank)를 쓴다 — 서버 시간 항로(동기화 없음), 생성기 계약, 지상 풀(Bake가 ACO_MobileUnit로 모은다). `Kind` 0 전차 / 1 헬리콥터 / 2 곡사포. 생성기는 종류별 후보(`CollectFreeMobilesOfKind`)로 나눠 놓는다.
- **헬리콥터**(`NPC_Helicopter`): 높이만 '지면 또는 해수면(`SeaLevel`) + `HoverHeight`(70m)'. 0.2초마다 바로 아래와 `HeliLookAhead`(120m) 앞의 지면 중 높은 쪽을 따라 절벽 앞에서 미리 오르고, 아래 지형이 없거나 해수면보다 낮으면 해수면 기준 — 바다·절벽 위로 다닌다. 지면 + 높이의 절반 아래로는 내려가지 않는다. 전진 중 기수 `HoverPitch` 숙임. 격추되면 돌며 중력으로 떨어져 지면/해수면에서 멈춘다(늦게 들어온 플레이어는 바닥 잔해). 턱 밑 기관포(ACO_AAGunSite)가 아군 지상·공중(26·25)을 쏜다. 락온 마커는 적 공중(29) — 공대공 미사일로 잡고, 표적 마커도 항공기 모양.
  - 생성기: 순찰 소대를 `HelicopterPlatoonShare` 확률로 헬리콥터 편대(`HelicopterGroupSize`, 고리 반경 2배, 바다·급경사 허용 — `SampleHeliPoint`, 길 막힘 검사 없음), 호위 매복조를 `HelicopterAmbushShare` 확률로 헬리콥터.
- **곡사포**(`NPC_Artillery` + 포탄 `ACO_ArtilleryShell`): 대공포 스크립트 대신 `ACO_ArtilleryGun`. 표적 = 생성기 호위 대상 중 아군 지상·해상(콘보이·함선, 공중 수송기 제외), `MinRange`~`MaxRange`의 가장 가까운 것. 탄착 예고 없음(플레이어가 아니라 호위 대상을 노린다).
  - 오너가 표적 진행 방향 × 속도로 비행 시간만큼 앞을 예측해(3회 반복) 고각 탄도(tanθ = (v² + √(v⁴ − g(gx² + 2yv²))) / gx)를 풀고, `FireInterval`(+지터)마다 `Spread` 안 무작위 탄착점으로 `NetFire(초속도)`를 모두에게 보낸다. 모든 클라이언트가 같은 초속도로 포탄(ACO_Ordnance, 중력 × `ShellGravityScale` 3 — 4km 비행 약 27초)을 쏜다.
  - 범위 피해는 오너 화면에서만, `ACO_Ordnance.HitFriendlies` — 아군 호위 대상만 맞고 적 유닛은 맞지 않는다(플레이어 폭탄은 반대로 아군을 건너뛴다).
  - 생성기: 매복조를 `ArtilleryAmbushShare` 확률로 곡사포(지상·해상 호위만) — 항로 옆 `ArtilleryStandoffMin`~`Max`(2.5~4.5km)의 마른 지면, 출발점은 `ArtilleryApproachDistance`(400m) 뒤 → 짧게 와서 멈춘 뒤에만 쏜다(`ACO_MobileUnit.IsStopped`). 순찰에는 쓰지 않는다.
  - Bake가 곡사포의 `Generator`를 연결한다.

### 6-43. 공격기 (지상·해상 호위 전용 적기)

- **프리팹** `NPC_Attacker`(프리팹 생성 창 → 공격기): 일반 NPC 전투기(`CreateFighterObject`)의 바리에이션. 아군 지상 레이어(26 — 콘보이 차량·LCAC·호위함)만 노리고(`TargetLayers`·`AAMTargetsLayer`), 대지 미사일(`ACO_Missile_AGM`, 없으면 AAM, 4발·재장전 30초·락온 2.5초)과 기총(틱 피해 8)으로 치고 빠진다(이탈 300m → 재진입 1.8km, 탐지 6km). 플레이어는 노리지 않지만 락온 마커는 적 항공기(29)라 잡을 수 있다. 체력 150, 순항 120·최고 220m/s(전투기보다 느리다), 플레어 2번. 모델은 올리브색 상자 + 직선 날개(바꿔 끼운다).
- **배치**: `ACO_NPCFighterAI.GroundAttacker`가 켜진 기체는 생성기가 일반 배치(초기 CAP·증원·편대 목표·호위 요격·슈퍼에이스 호위)에 쓰지 않는다(슈퍼에이스와 같은 예약). 새 목표 종류 `OBJ_ATTACK`으로만 2기 편대로 나온다.
  - **지상 호위**(`ACO_PhaseConvoy`)와 **상륙전**(`ACO_PhaseAmphibious`)이 시작할 때 `AttackersBase`(2) + `AttackersPerPhase`(1) × (페이즈 번호 − 1)기를 부른다 — 콘보이는 항로 중심, 상륙전은 상륙 지점(항구·해안선)에서 `AttackerDistanceMin~Max`(6~9km) 떨어진 곳에서 들어온다. 풀에 공격기가 없으면 부르지 않는다.
  - 생성기 풀(UnitPoolRoot) 아래에 4~6기 두면 Bake가 전투기 목록에 넣는다.
- 태그 없는 배치(공격기·호위 요격 편대·폭격기 호위)는 이제 지금 페이즈 목표의 보호(`_protectTag`)를 풀지 않는다(예전엔 0으로 덮어 증원이 파괴된 목표 유닛을 다시 쓸 수 있었다).
- 아군 CIWS(호위함 대공포, 6-40)는 공격기의 대지 미사일도 막는다(아군 지상 26을 지킨다).

### 6-67. 부위 락온 고침

- **엔진·냉각기가 안 잠기던 원인**: SAV `Target.controller`의 `TargetExplode` 클립이 자식 `AAMTarget`의 활성 상태를 다루고 Write Defaults가 켜져 있어, 애니메이터가 시작할 때 값(꺼짐)을 매 프레임 되돌렸다. 처음에 꺼 두고 스크립트로 켜는 부위 마커는 이름을 `LockMarker`로 둔다(`PartMarkerName`). 이미 있는 프리팹은 Bake가 이름만 고친다. 코어 마커는 `Core` 자식이라 클립 경로에 걸리지 않는다.
- **락온 표시가 본체 마커로 옮겨 가던 원인**: `ACO_TargetMarkers.FindUnitOf`가 첫 조상을 골라, 부위를 잠그면 먼저 등록된 본체(요새·함선) 마커가 그 부위 자리로 갔다. 가장 가까운 조상을 고른다 — 부위를 잠그면 그 부위 마커가 잠긴 표시가 된다.

### 6-69. 아군 기함 VLS 풀 분리

- **문제**: Bake가 풀의 SAM을 지상 풀에 넣을 때 `ACO_Warship` 부모만 걸러, `ACO_EscortUnit`인 아군 기함의 VLS 2기가 적 지상 유닛으로 등록됐다. 적 마커가 붙고, 생성기가 일반 SAM처럼 숨기거나 다른 자리로 옮길 수 있었다.
- **처리**: 대공포와 같이 호위 유닛 아래의 SAM도 지상 풀에서 뺀다. 씬은 다시 Bake해야 적용된다.

### 6-70. 보스 포대 공용 프리팹

- `NPC_Turret_Flak` / `NPC_Turret_CIWS` / `NPC_Turret_VLS`(`ACO_TurretPrefabs`): 공중 요새·랜스 포대의 방공 부위는 이 프리팹의 중첩 인스턴스다. 체력·사거리·요격 수치·모델은 프리팹 하나만 고치면 두 보스에 함께 적용된다.
- 인스턴스 값은 자리·회전·크기, 요새의 피치 범위(윗면·배), 요새의 락온 레이어(29)뿐이다. 재질도 공용 프리팹의 것 하나다(랜스 포대 포대의 강철 텍스처는 쓰지 않는다).
- `BaseHealth`와 요격 판 번호를 프리팹에 넣어 둬 Bake가 씬에 덮어쓰지 않는다. 프리팹 체력을 바꾸면 Bake가 `BaseHealth`만 맞춘다.
- 먼저 만든 랜스 포대는 Bake(`EnsureRailgunTurrets`)가 자리를 유지한 채 인스턴스로 바꾼다. 소형 레일건은 연출을 공유해 프리팹으로 빼지 않았다.
- 배 쪽 포대는 뒤집어 단다(루트 Z 180도). `ACO_AAGunSite`가 시작할 때 뒤집힘을 보고 방위축을 아래로 잡아, 머리가 되돌아 눕지 않는다. `MinPitch`·`MaxPitch`는 뒤집어도 월드 고각(배 쪽 -85~20)이다.
- 프리팹 창에 넣지 않았다: 같은 경로로 다시 만들면 중첩 인스턴스 연결이 끊어진다. 없을 때만 만든다.

### 6-71. 적 CIWS 요격 약화

- 사거리 1200 → 900m, 격추 0.5 → 1.0초, 재교전 대기 `InterceptCooldown` 0 → 1.5초(반응 0.5초 유지). 아군 CIWS는 그대로다.
- AAM 최고 속도 600m/s면 사거리를 1.5초에 지나 CIWS 한 문이 겨우 맞출 수 있는 값이다. 느린 미사일(짧은 거리 발사)은 여전히 잘 막히고, 일제 사격은 뚫는다. 시뮬레이션(`Tools/aco_ciws_sim.py`, 요새 배치) 4발 일제 명중: 이전 0발 → 600m/s 평균 약 2발(부위별 0.4~4발).
- `InterceptDefaultsApplied`는 한 번만 넣는 값이라, 먼저 만든 CIWS는 판 번호(`InterceptTuneVersion`, `ACO_SceneBaker.CiwsTuneVersion`)로 Bake가 한 번 더 넣는다.

### 6-79. 대공포 탄 도착 판정

- **문제**: 조준 체류 판정이 쏘는 순간 끝나, 탄이 날아가는 동안(Flak 2km면 2초 넘게) 기체가 꺾어도 데미지가 들어갔다. 예광탄은 직진 예측 자리로 날아가 빗나가는데 체력만 깎였다.
- **처리**(`ACO_NPCGun.ShellHitRadius`): 데미지 주기마다 바로 맞히지 않고, 그 순간의 조준점(직진 예측)에 탄속 뒤 도착하는 탄을 표적 오너 화면에 띄운다. 도착할 때 표적이 반경 안에 있어야 맞는다. 체류·유효 각도·주기는 그대로라 직진 비행의 피해량은 같고, 데미지가 탄 도착 뒤에 들어온다.
- 반경: CIWS 20m, 그 밖의 포 45m(`ApplyShellRadius`). 공용 포대는 프리팹에, 다른 유닛은 씬에 Bake가 넣는다. 전투기 기총은 0(기존 즉시 판정)이다.
- 빗나가는 거리 ≈ ½ × 기체 횡가속 × 탄 비행 시간². 먼 거리에서 꺾을수록 잘 피하고, 가까우면 탄이 금방 닿아 맞는다.

### 6-84. 갑판 요원 (`ACO_CatapultCrew`, 프리팹 `NPC_CatapultCrew`)

- **절차**: 대기 → 유도(유도요원이 전진·좌우 선회·감속·정지) → 점검(고정 뒤 점검요원 3명이 허리를 숙인 채 기체를 돌고 홀드백 요원이 들고 있던 펜던트를 앞바퀴에 건다. 모두 제자리로 물러나 무릎을 꿇고, 팔을 수평에서 50도쯤 든 채 손목을 꺾어 엄지가 하늘을 보게 한다. 엄지는 사출이 끝날 때까지 유지) → 준비(유도요원이 앉고 사출장교가 스로틀 업 신호) → 애프터버너 → 지적확인(사출장교가 요원 5명을 한 명씩 돌아보며 가리킨다, `PointEachTime` 0.6초씩) → 발함 신호 2초(무릎을 꿇고 고개를 돌려 전방 확인 뒤 팔을 뻗는다) → 사출. 신호 중 애프터버너를 끄면 준비로 돌아간다. 점검 중 먼저 켜 두면 점검이 끝나는 대로 지적확인으로 간다.
- **유도 판단**: 캐터펄트는 방향이 15도 안이어야 걸리므로 트리거를 바로 겨누게 하지 않는다. 축에서 벗어난 거리의 `LineupRatio`(1.5)배 앞 축 위 지점을 향하게 해 먼저 축에 올려놓고, 그 방향에서 `AlignAngle`(5도) 넘게 벗어나면 선회 신호를 준다. 옆에서 가까이 들어오면 뒤로 돌아가는 신호가 나온다. 트리거 앞 12m까지 유도한다. 앞 기체를 점검하는 동안 뒤 기체에는 정지 신호를 주고, 점검이 끝나면 앉는다.
- **사출 연동**: `ACO_DFUNC_Catapult`는 물린 트리거의 요원을 찾아, 그 요원이 내 화면에 켜져 있으면 `LaunchApproved`로만 사출한다(풀스로틀 유지·트리거 사출은 쉰다). 요원을 끄면 기존 풀스로틀 유지 사출로 돌아간다.
- **동기화**: 새 동기화 변수 없음. 기체 위치·`OnCatapult`·`AfterburnerOn`·`LaunchStarted`(`LaunchCatapult` 이벤트에서 설정)로 각 클라이언트가 스스로 계산한다. 사출 판단은 조종사 쪽만 쓴다.
- **홀드백 펜던트**: `Pendant`는 평소 요원 손의 `PendantGrip` 아래에 있고, 마지막 경유점에서 `PendantPoint`로 옮긴다. `LaunchStarted`에서 숨기고 절차가 끝나면 다시 손으로 돌린다. 기본은 원기둥이며 모델은 프리팹에서 바꾼다.
- **로컬 토글**: HUD 설정 `DECK CREW`(`PlayerData` `aco_hud_deck_crew`). 끄면 내 화면에서 사라진다.
- **애니메이션**: Synty에 클립이 없어 `ACO_CrewBuilder`가 만든다. 자세는 뼈 방향 표로 적고 모델에 입혀 `HumanPoseHandler`로 Humanoid 값을 얻는다(손가락만 근육 값 직접). 애니메이터 정수 `Signal` = `SIG_*`, AnyState 전이. 교체는 `Assets/ACO_Addon/animation/Crew/ACO_Crew.controller`의 상태 클립만 바꾸면 된다. 표를 고치면 `CrewClipVersion`을 올린다 — Bake가 `Crew_*.anim`을 다시 만들고 컨트롤러에 없는 상태만 더한다(버전은 `Crew_Idle.anim` 메타의 userData).
- **JBD**: 항모의 `JBD 1·2`를 가까운 캐터펄트에 잇는다(`BakeJbd`, 경첩 = 판의 사출 방향 쪽 끝, 정적 배칭에서 뺀다). 고정되면 뒤쪽 끝이 `JbdAngle`(55도)까지 올라오고 사출이 끝나면 내려간다. 요원 표시를 꺼도 움직이고, 콜라이더도 함께 움직인다.
- **발함지휘소**: `ACO_LaunchOperator`(프리팹 `NPC_LaunchOperator`, 모델 White)가 지휘소 바닥에 서서 평소 패널을 조작하고, 발함 신호부터 사출까지 버튼을 누른다. 캐터펄트 2개를 함께 본다. 첫 Bake 때만 놓는다.
- **모델**: EEI DeckCrewSet — 유도 Yellow, 사출장교 Shooter, 점검 FinalChecker 2명, 추가 점검·홀드백 Green. 없으면 Synty 조종사. lilToon이 없으면 머티리얼이 분홍색이라 같은 색·노멀 텍스처로 Standard 머티리얼을 `Generated/CrewMaterials`에 만들어 끼운다(lilToon을 넣으면 원본 그대로). 클립은 Humanoid라 그대로 쓴다(`EnsureCatapultCrewModels`).
- **배치**: `BakeCatapultCrew`가 씬의 트리거(레이어 24, 이름 Trigger, 기체 밖)마다 `ACO_DeckCrew` 아래에 한 벌 놓는다. 이미 있으면 자리를 건드리지 않는다. 요원에게 콜라이더는 없다. 내가 600m 밖이면 끈다.

### 6-95. 소형 레일건 프리팹

- **프리팹**: `NPC_Turret_RailgunMini` 하나를 고치면 네 문이 함께 바뀐다. 체력, 사거리, 사격 값, 모델, 레이저, 소리가 들어 있다. 자리와 크기만 인스턴스 값이다. 탄·폭발 파티클은 랜스 포대의 `MiniFx`를 함께 쓴다.
- **사격 순서**: 조준 레이저 `AimTime` 1.5초 → 표적의 `LeadTime` 3초 뒤 자리에 포신 고정 → 탄이 고정 3초 뒤에 닿도록 늦춰 발사(`ShellSpeed` 5000m/s). 탄이 1초 걸리면 고정 2초 뒤에 쏜다.
- **최소 폭발 거리**: `MinRange` 1km. 고정한 자리가 그 안이어도 쏘지만, 안에서는 터지지 않고 직격(`DirectHitRadius` 15m, `DirectDamage` 60)만 맞는다. 빗나간 탄은 1km에서 터져 폭발 지역을 연다. 직격은 맞은 사람이 판정하고, 그 사람의 화면에서만 폭발이 취소된다.
- **회피**: 고정된 레이저가 3초 동안 폭발 자리를 보여 준다. 직진하면 맞고, 방향을 틀면 피한다.

### 6-100. 포대 모델 교체 (Flak, CIWS, VLS)

- **파일**: `Models/Lance/ACO_Turrets.fbx`. `Tools/aco_lance_model.py`가 함께 만든다. 메시 크기는 프리팹 로컬 크기다.
- **적용**: Setup Check `프리팹 수치`의 `turret-model-1`. 프리팹을 제자리에서 고친다. 이 포대를 쓰는 공중 요새도 함께 바뀐다. 팩 모델(`Turret_0*`) 자식은 프리팹에서 지운다. 팩 파일은 프로젝트에 남는다.
- **Flak**: 쌍열포. 받침(`Model`), 포탑(`YawModel`), 포신(`pitchModel`). 포구는 두 포신 가운데의 `Gun` 하나라 예광탄도 가운데서 나간다.
- **CIWS**: 회전 포신. 구조는 Flak과 같다. `TurretYaw`를 (0, 2, 0)으로, `TurretPitch`를 원점으로 옮겼고, 포구 `Gun`은 z 3.45다. 포신은 돌지 않는다.
- **고각**: 두 포 모두 -20도에서 85도까지 포탑과 받침에 닿지 않는다.
- **VLS**: 몸체(`Model`), 경고띠, 덮개 넷(`Hatch_0~3`). `ACO_MissileLauncher.Hatches`에 경첩을 넣으면 락온 경고나 발사 때 모두 열리고, 마지막 이벤트 4초 뒤 닫힌다. 이벤트가 모든 클라이언트로 가므로 따로 동기화하지 않는다.
- **표기**: 멀리서만 보이므로 문구와 기호는 넣지 않았다.

### 6-108. TEL 모델

- **만들기**: `Tools/aco_tel_model.py`(Blender)가 `Models/TEL/ACO_TEL.fbx`와 `ACO_TEL_Albedo.png`를 만들고, `ACO_TelModel.cs`가 프리팹에 넣는다(`tel-model-1`). 예전 팩 모델은 끈다. 재질 하나, 텍스처 2048. 원거리에서 보는 유닛이라 트레드, 볼트, 난간, 사다리, 창틀 같은 작은 부품은 넣지 않았다.
- **모양**: 전방에 운전석이 좌우로 갈라진 8×8 트럭. 누운 탄도탄의 기수가 두 운전석 사이로 들어간다. 판정(18×5×6m)에 맞춰 실물보다 약 1.5배 크다. 공중에서 보이도록 키운 값이다.
- **이렉터**: 차체 뒤쪽 축(`PIVOT`)에서 90도까지 일어난다. 꼬리 받침은 뚫린 고리라 배기가 막히지 않는다. 날개는 + 배치로 이렉터 보 사이를 지나간다.
- **유압 실린더**: 3단이고 `ACO_TelUnit.AimRam`이 두 축 사이에 놓는다. 윗단은 이렉터, 아랫단과 가운데 단은 차체 소속이다.
- **발사 자리**: 비행용 탄도탄은 다 일어선 탄도탄과 같은 자리(차체 뒤 `TelFlyingTail`)에서 시작한다. 공중 판정, 공중 표적 마커, 공중 폭발, 궤적을 그 자리로 옮겼다. 불꽃과 궤적의 간격은 프리팹 값을 지킨다.
- **늦은 접속**: 발사 뒤에 들어오면 이렉터가 다 선 자세로 나온다(예전에는 누운 채였다).
- **표기**: 번호와 탄도탄 글자는 텍스처에 칠했다. 옆면은 좌우 띠가 따로라 어느 쪽에서 봐도 글자가 바로 읽힌다.
- **치수 변경**: `aco_tel_model.py`의 `PIVOT`, `RAM_*`, `CRADLE_Y`, `MISSILE_*`와 `ACO_TelModel.cs`의 같은 값을 함께 바꾼다.

### 6-112. 해상 유닛 부위 범위 피해 감소

폭탄 하나가 갑판에 떨어지면 반경 안의 포탑·스커트·궤도가 한꺼번에 깎여 부위가 너무 빨리 사라졌다. 해상 유닛 부위는 직접 맞지 않으면 범위 피해를 적게 받는다.

- **배율**(`ACO_Warship.PartSplashScale`): 본체(`TargetHealth`)가 아닌 같은 계층의 SaccTarget(부위)이 받는 범위 피해 배율. 해상 유닛(함선, 항공모함, 구축함, 육상전함) 0.2(`ACO_PrefabBuilder.SeaPartSplash`), 공중 요새·랜스 포대 1. 무기가 `GetComponentInParent<ACO_Warship>().GetSplashScale(target)`로 묻는다.
- **직격 부위**: 탄이 실제로 닿은 콜라이더의 부위 하나는 배율 없이 그대로 받는다. 거리(콜라이더 경계 상자)로 정하지 않는다 — 비스듬하거나 긴 부위는 상자가 실제보다 커서 닿지 않아도 거리 0이 된다.
  - 미사일(`ACO_Missile`): 락온 표적에 명중하면 그 표적(이미 직격 피해를 받아 범위 피해에서 빠진다), 땅 선 검사에 부딪히면 그 콜라이더의 표적.
  - 폭탄·로켓(`ACO_Ordnance`): 선 검사가 처음 닿은 콜라이더의 표적, 유도 근접 신관이면 유도 표적.
  - 집속탄 자탄(`ACO_Dispenser`): 자탄마다 떨어진 자리의 콜라이더 표적(`SubmunitionHit(point, direct)`).
  - 본체 갑판처럼 부위가 아닌 곳에 맞으면 직격 부위가 없어 주변 부위는 모두 배율을 받는다. 닿은 콜라이더가 유닛 히트박스(17)일 때만 직접 맞은 것으로 친다 — 선체(Environment) MeshCollider는 본체 SaccTarget으로 이어져, 갑판에 맞은 폭발이 코어를 "직접" 때린 것으로 잡히던 것을 막는다.
- **예외 무기**(`ACO_DFUNC_Missile.FullPartSplash`): 켜면 부위에도 범위 피해를 그대로 준다. 쏠 때 탄(`ACO_Missile`·`ACO_Ordnance`)에 넘긴다 — 탄 프리팹이 아니라 무장에 두는 이유는 LASM과 LAGM이 같은 탄 프리팹을 쓸 수 있어서다. 지금은 LASM만(카탈로그 `ApplyLASM`, Setup Check `sea-part-splash-1`이 `Module_LASM`을 켠다).
- 직격 피해, 본체 피해, 기관포는 바뀌지 않는다.
- **코어 범위 피해 면역**(`ACO_Warship.CoreSplashImmune` — 보스 셋(육상전함·공중 요새·랜스 포대), Setup Check `landship-core-splash-1`·`boss-core-splash-1`): 본체(코어)는 범위 피해를 받지 않는다. 예외 무기(LASM)도 마찬가지 — LASM 연사로 배기구가 몰아서 깨지자마자 코어가 열리고 날아오던 범위 피해에 곧바로 녹아 코어 단계가 순식간에 끝났다. 코어를 락온해 맞히거나(미사일 직격) 폭탄이 코어 히트박스에 닿아야(직접 닿은 표적) 깎인다.

### 6-118. 같은 함선 부위끼리 락온 진행도 잇기

- 부위가 촘촘한 함선(육상전함 — 500m 안에 락온 지점 수십 곳)에서 기수가 조금만 흔들려도 시커 원 가운데에 더 가까운 부위가 생겨 표적이 옮겨 갔고(`CenterSwitchMargin`), 옮길 때마다 락온 시간이 처음부터 다시 시작돼 락온을 끝내기 어려웠다.
- `ACO_DFUNC_Missile`: 표적(락온 마커)마다 속한 함선(`ACO_Warship` — 처음 볼 때 부모를 거슬러 찾아 기억)을 안다. 같은 함선의 부위끼리 옮겨 가면 락온 진행도(시간·완료)를 그대로 잇는다 — 가운데 쪽으로 옮길 때, 부위가 시커 원 밖으로 나가거나 부서지거나 가려져 칸이 비었다가 1초(`CARRY_TIME`) 안에 같은 함선의 다른 부위로 채워질 때 모두. 함선 부위가 잠깐 가린 동안(`OBSCURED_GRACE` 안)은 진행도를 지우지 않고 멈춰 둔다. 다른 유닛으로 옮기거나 무장을 바꾸면(`ClearLock`) 예전처럼 처음부터.
- 발사는 그 순간 칸에 있는 부위(가운데에 가장 가까운 부위)로 간다. 육상전함뿐 아니라 함선·공중 요새·랜스 포대 부위에도 똑같이 적용된다.

### 6-128. 락온 제한 (적 공통 부품) — `ACO_LockGate`
- 락온을 막아야 하는 적의 루트(락온 마커의 부모 쪽)에 붙인다. 플레이어 무장(`ACO_DFUNC_Missile`)은 시작할 때 표적 마커마다 부모 쪽으로 이 부품을 찾아 두고, 락온 시간을 쌓을 때마다 `CanBeLockedFrom(내 위치)`를 묻는다(거짓이면 락온 시간이 0으로 돌아간다). 무장은 어떤 적인지 모른다.
- 값: `Active`(그 적의 스크립트가 켜고 끈다 — 꺼져 있으면 언제나 락온), `Mode`(0 = 정면 원뿔 안에서만 — ECM, 1 = 어디서도 안 됨, 2 = 뒤쪽 원뿔 안에서만), `HalfAngle`(원뿔 반각), `Facing`(정면 기준, 비우면 자기).
- 락온 단계만 막는다 — 이미 쏜 미사일·기총·로켓·폭탄은 그대로. 쓰는 곳: 레이저 에이스 2단계 ECM(정면 ±15도).
