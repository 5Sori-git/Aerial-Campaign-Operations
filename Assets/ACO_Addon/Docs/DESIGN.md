# ACO Addon 설계 문서 — NPC 전투기 / SAM / 전투 시스템

> 대상: VRChat 월드 (U# / UdonSharp), 기반 프레임워크: SaccFlightAndVehicles (SAV)
> 원칙: `Assets/SaccFlightAndVehicles/` 내부 파일은 **일체 수정하지 않는다.** 모든 기능은 `Assets/ACO_Addon/`에 애드온으로 구현하고, SAV의 공개 필드/이벤트/프리팹을 조합해서 쓴다.
> 이 문서는 **현재 구현 기준**이다. 각 절의 ✅는 구현됨, ⏳는 설계만 있고 미구현.

---

## 0. 구현 현황

| 기능 | 상태 | 파일 |
|---|---|---|
| NPC 전투기 비행 모델 (뱅크-투-턴, 월드축 분리 제어, 지형 회피) | ✅ | `ACO_NPCFlightController` |
| NPC 전투기 AI (순찰/추격/이탈, 요격 조준, 편대 간격) | ✅ | `ACO_NPCFighterAI` |
| 기동 동기화 (오차 기반 데드 레커닝) | ✅ | `ACO_NPCFighterNet` |
| SAM 포대 (탐지, 포탑, 레이더 콘/LOS) | ✅ | `ACO_SAMSite` |
| 미사일 런처 (락온·피락온 경고·탄약/재장전, SAV 미사일 호환) | ✅ | `ACO_MissileLauncher` |
| 자체 미사일 (직접 추적, 대응책, 접근 경고, 피격자 판정) | ✅ | `ACO_Missile` |
| NPC 기총 (시각 전용 파티클 + 타겟 오너의 조준 체류 판정) | ✅ | `ACO_NPCGun` |
| 격추/부활 (체력은 SaccTarget, 명시적 `Respawn()`) | ✅ | AI/SAM |
| 프리팹 생성, 주변 NPC 목록 빌드 시 베이크 | ✅ | `ACO_PrefabBuilder`, `ACO_SceneBaker` |
| CombatDirector — 공격 정원·대기 선회·교대·자기 방어 | ✅ | `ACO_CombatDirector`, AI |
| CombatDirector — LOD, 오너십 분산 | ⏳ | — |
| 미션 생성기 (풀 숨김 → 미션 시작 일괄 배치, 증원, 전투기 임무 엄호/CAP) | ✅ | `ACO_MissionGenerator` |
| 고가치 표적 (공장/벙커, 방공 호위 링) | ✅ | `ACO_StaticTarget` |
| 미션 진행 — 페이즈 1~5개(타격/방공망 제압/점수 할당량/시한 표적/제공권/보스/호위/지상 호위, 무작위·연속 중복 없음), 웨이브, 이벤트 | ✅ | `ACO_MissionManager` |
| 호위 대상(아군 수송기/콘보이 — 서버 시간 항로, 적 우선 공격, 이동 방식 공중/해상/지상, 행렬) | ✅ | `ACO_EscortUnit` (해상 호위 페이즈는 ⏳) |
| 전차(지상 항로 순찰/매복, 아군 지상 표적 포격) | ✅ | `ACO_Tank` + `ACO_AAGunSite`(포탑) |
| 육각 기둥 지형(씨앗 생성, 미션마다 새 지형, 경사 치마로 차량 통행) | ✅ | `ACO_HexTerrain` + `ACO_HexColumn` |
| 작전 콘솔(페이즈 수 선택 버튼) | ✅ | `ACO_MissionStartButton` |
| 전투기 숙련도 (신병/베테랑/에이스, 편대 단위) | ✅ | 생성기 + AI |
| 팀 공통 점수(페이즈 배율), 제한 시간 실패, 결과 랭크 | ✅ | `ACO_MissionManager` |
| 점수 HUD(머리 고정), 표적 마커(사각형/육각형, TGT, 시선 정보) | ✅ | `ACO_ScoreHUD`, `ACO_TargetMarkers` |
| 작전판(격추 기록 + 디브리핑, 로컬) | ✅ | `ACO_MissionLog` |
| 무전 음성 | ⏳ | — |
| 대공포 (포탑 리드 조준, 급기동 회피 가능, NPC 기총 판정 재사용) | ✅ | `ACO_AAGunSite` |
| 지상 유닛 원거리 잠재우기 (조종 중인 플레이어 기체 기준) | ✅ | `ACO_SAMSite`, `ACO_AAGunSite` |
| 부위 파괴형 함선 — 전함(고가치 표적)/구축함(일반, 함대·호위), 순항, 격침 연출 필드 | ✅ | `ACO_Warship` + SAM/대공포 부위 |
| CIWS 미사일 요격 | ⏳ 보류 | 설계만(8절) |

---

## 1. 핵심 설계 결정

1. **NPC는 진짜 SAV 컴포넌트를 가진다.** 가짜 체력 시스템을 병행하지 않는다. 그래야 플레이어의 SAV 무기가 NPC에게 그대로 작동한다. 체력은 **`SaccTarget`**, 타겟 목록/런처 계약은 **`SaccEntity`**가 맡는다(2.4절).
2. **경량화 — NPC는 `SaccAirVehicle`을 쓰지 않는다.** 조종 입력·탑승석·연료·사운드까지 든 "완전체" 스크립트는 수십 기 운용에 과하고 synced 필드도 많다. 비행은 NPC 전용 경량 스크립트가 맡는다. 플레이어는 SAV를 그대로 쓰는 이원화 구조다.
3. **PvE, IFF는 고정 레이어 + 인스펙터 설정.** 사람은 플레이어뿐이고 NPC 간 교전은 가능하다. 소속 레이어는 런타임에 바뀌지 않으며, "누구를 적으로 보는가"는 각 유닛의 `TargetLayers` 인스펙터 필드로 정한다(4.1절).

---

## 2. SAV 분석 — 검증된 사실과 함정

> 모두 SAV 코드/프로젝트 설정을 직접 열어 확인한 내용이다(파일·줄 번호 표기). 구현이 이 제약들을 전제로 한다.

### 2.1 SAV 구조 요약 (`Assets/SaccFlightAndVehicles/Scripts/`)

| 항목 | 위치 | 핵심 |
|---|---|---|
| `SaccEntity` | `SaccEntity.cs` | `public LayerMask AAMTargetsLayer = 1 << 25`(L31, 다중 레이어 가능), `public int OutsideVehicleLayer = 17`(L94), `public GameObject[] AAMTargets`(L99, 빌드 시 자동 생성), `WeaponDamageVehicle()`, `ExtensionUdonBehaviours[]`, `GetExtention(typeName)`, `InVehicle`(로컬 유저 탑승 여부) |
| `SaccTarget` | `Other/SaccTarget.cs` | `[UdonSynced] Health`, `FullHealth`(=시작 Health), `RespawnDelay`, `ArmorStrength`, `ExplodeOther[]`(격추 시 `"Explode"` 전송), `WeaponDamageTarget()`, `RespawnTarget()` |
| 미사일 | `SaccAirVehicle/Weapons/SAV_AAMController.cs` | 런처를 `public UdonSharpBehaviour AAMLauncherControl`(L13)로만 참조하고 값은 전부 `GetProgramVariable()`로 읽음 → 덕 타이핑으로 런처 교체 가능 |
| 락온(플레이어 런처) | `DFUNC/DFUNC_AAM.cs` | `EntityControl.AAMTargets`를 FixedUpdate당 1개씩 순회(L427~500), `Target.transform.parent.GetComponent<SaccAirVehicle>()`로 비행기 여부 판정(L188) |
| 빌드 자동 셋업 | `SaccFlightEditorScripts/SaccFlightMenu.cs` | `OnBuildRequested` → `SetEntityTargets()`가 씬을 스캔해 각 SaccEntity의 `AAMTargets`를 채움(L151~185) |
| 참조 프리팹 | `Prefabs/Accessories/AAMDummyTarget.prefab`, `Prefabs/Accessories/Target.prefab` | 비행기가 아닌 락온 대상의 공식 패턴 / SaccTarget 공식 패턴 |

### 2.2 레이어 규약과 배정

SAV 규약 레이어(`Instructions.txt` "Custom Layers")와 이 프로젝트의 배정. `SaccFlight → RenameLayers`는 **실행됨**(TagManager 확인).

| 레이어 | SAV 규약 | 이 프로젝트 |
|---|---|---|
| 17 Walkthrough | 차체 콜라이더(`OutsideVehicleLayer`), 락온 LOS Raycast 대상 | **모든 유닛의 히트박스**(아군/적 공통) |
| 23 / 24 / 27 / 28 / 31 | Hook / Catapult / ReSupply / Racing / OnBoardVehicleLayer | 사용 안 함 |
| 25 AAMTargets | 미사일 락온 마커 | **아군 항공기 락온 마커** (플레이어 기체 포함) |
| 26 AGMTargets | 공대지 락온 | **아군 지상 차량 락온 마커** — AGM 미사용이라 재활용. AGM 도입 시 `DFUNC_AGM.AGMTargetsLayer`를 옮긴다 |
| 29 (빈 레이어) | — | **적 항공기 락온 마커** |
| 30 (빈 레이어) | — | **적 지상 유닛 락온 마커** |
| 22 (빈 레이어) | — | 예비 |

> 29/30은 아직 이름이 비어 있다. 동작엔 지장 없지만 인스펙터 가독성을 위해 이름을 붙여두면 좋다.

### 2.3 락온 대상 = 트리거 마커, 그리고 빌드 시 베이크 → 풀링 필수

- `AAMDummyTarget.prefab` 구조: **루트(레이어 25, 트리거)** = 락온 마커 / **자식(레이어 17, 솔리드)** = 실제 히트박스.
- `AAMTargets`는 **빌드할 때마다** 씬의 모든 GameObject를 레이어로 훑어 각 SaccEntity의 `AAMTargetsLayer`에 맞는 것을 모은다(콜라이더 유무 무관, 자기 자식 제외).
- 결과:
  1. 유닛마다 **락온 마커(소속 레이어 트리거) + 히트박스(레이어 17)**가 둘 다 필요하다.
  2. **런타임 `Instantiate`한 NPC는 락온할 수 없다**(베이크된 배열에 없음) → NPC는 **씬에 미리 배치하고 활성/비활성으로 풀링**한다. `DFUNC_AAM`이 비활성 타겟을 건너뛰도록 되어 있어(L477, L526) 이게 SAV가 의도한 패턴이다.
  3. NPC의 `SaccEntity.AAMTargetsLayer`를 25로 두면 빌드 시 그 NPC의 `AAMTargets`가 **아군 기체 마커로 자동으로 채워진다** → NPC 탐지 목록을 따로 만들 필요가 없다.

### 2.4 NPC 체력은 `SaccEntity`가 아니라 `SaccTarget`이 소유한다

증상: "락온·비행은 되는데 데미지가 안 들어간다." `SAV_AAMController`의 데미지 분기:

| 경로 | 조건 |
|---|---|
| 직접 충돌(L609~645) | `other.gameObject`의 `SaccEntity` **+ 그 엔티티의 `SaccAirVehicle`**이 있어야 데미지(L630). 없으면 `SaccTarget` 분기로 |
| 근접 폭발(L688~720) | `SaccAirVehicle`이 없으면 `GetComponentInParent<SaccTarget>()`(L707) |

데미지 계산에 `FullHealth`가 필요한데 이를 `SaccAirVehicle` 또는 `SaccTarget`에서만 읽는다 → **`SaccEntity`만 있는 NPC는 데미지 0.**

NPC 구성 규칙:
- **`SaccTarget`**: 체력·데미지·방어력. 격추 시 `ExplodeOther[]`에 `"Explode"`를 보낸다(모든 클라이언트) — AI/SAM을 여기 등록한다.
- **`SaccEntity`**: 체력과 무관하게 유지 — `AAMTargets` 베이크(2.3)와 런처 계약(2.5)에 필요.
- ⚠ **`SaccTarget`은 같은 오브젝트에 `Animator`가 필수**(null 체크 없이 사용, `dead`/`healthpc`/`hit` 파라미터). SAV의 `AssetFiles/Target/Target.controller`를 쓴다.
- ⚠ **직접 충돌 분기는 부모를 타지 않는다** → 히트박스 콜라이더와 `SaccTarget`을 **같은 GameObject(루트)**에 둔다. 루트 레이어는 17(락온 LOS Raycast가 이 레이어를 요구, `DFUNC_AAM` L467). 락온 마커만 자식.
- ⚠ **사망 애니메이션이 처리하는 범위** — `TargetExplode.anim`의 바인딩을 해독한 결과:

  | 대상(경로 CRC32) | 컴포넌트 | 동작 |
  |---|---|---|
  | 루트("") | MeshCollider(typeID 64) | 끔 |
  | 루트("") | MeshRenderer(typeID 23) | 끔 |
  | `Effects`(2276853046) | GameObject | 켬 — 폭발 |
  | `AAMTarget`(520773193) | GameObject | 끔 — 재락온 방지 |

  즉 SAV는 **메시·콜라이더가 루트에 있는 구조**를 전제한다. 실제 모델은 보통 여러 자식 메시라서, 자식 모델과 BoxCollider는 AI/SAM의 **`HideOnDeath` / `DisableCollidersOnDeath`**로 직접 끄고 켠다. 자식 이름 `Effects`/`AAMTarget`은 애니메이션이 찾으므로 그대로 써야 한다.

### 2.5 SAV 미사일 런처 계약 (덕 타이핑)과 함정

`SAV_AAMController`가 런처에서 `GetProgramVariable()`로 읽는 변수: `EntityControl`, `SAVControl`, `InEditor`, `MissileType`, `PitBullIndicator`, `AAMMaxTargetDistance`, `IsOwner`, `AAMTarget`, `AAMTargets`. 타입은 지상 런처 선례인 `SaccAAGunController`를 따른다 — 특히 **`AAMTarget`은 `int`**(미사일이 `(int)`로 언박싱). `ACO_MissileLauncher`가 이 계약을 그대로 노출하므로 SAV 미사일도 발사할 수 있다(기본은 자체 미사일 `ACO_Missile`).

- ⚠ **`NoSaccEntity = true`를 쓰면 안 된다**: L262가 `TargetEntityControl`이 있을 때 `EntityControl.CenterOfMass`를 역참조하는데(SAV 버그 — 변수를 잘못 씀), 플레이어 기체는 SaccEntity가 있으므로 즉시 NullReference. 런처는 반드시 실제 SaccEntity를 제공해야 한다(SAM 포함). `CenterOfMass`는 SaccEntity가 미할당 시 자기 transform으로 채운다(L415).
- 발사 절차는 `DFUNC_AAM.LaunchAAM()`과 동일: 풀(런처의 자식)에서 꺼냄 → 부모 분리 → 위치/회전 → `SetActive(true)` → `SendCustomEvent("EnableWeapon")`. 미사일은 `MoveBackToPool()`에서 런처의 자식으로 스스로 돌아온다.

### 2.5-1 SAV 기총은 "충돌이 난 클라이언트"가 무조건 데미지를 준다

`SaccEntity.OnParticleCollision`(L427)은 파티클이 콜라이더에 닿은 클라이언트에서, 파티클 시스템 자식 이름 `d:데미지`/`t:무기타입`을 읽어 **조건 없이** `WeaponDamageVehicle`을 호출한다(기본 10 / ArmorStrength, 타입 1). 그래서 `DFUNC_Gun`은 충돌이 켜진 `GunDamageParticle`을 **쏜 사람(오너)에게서만** 켜고, 다른 클라이언트는 시각용 파티클만 본다.
→ NPC 기총에 충돌 파티클을 쓰면, 특정 클라이언트에서만 켜더라도 **그 화면에서 날아간 탄이 옆의 다른 기체에 닿으면 그 클라이언트 기준으로 엉뚱한 기체가 맞는다.** 모든 기체가 레이어 17을 공유해 파티클 충돌 대상을 "한 기체"로 제한할 방법도 없다. 그래서 NPC 기총은 충돌 파티클을 쓰지 않는다(4.3).

### 2.6 NPC에 `SaccAirVehicle`이 없을 때 플레이어 락온에 미치는 영향

`DFUNC_AAM`은 NPC를 정적 타겟처럼 취급한다: 스로틀 기반 락온 시간 보정 없음, 열추적의 "엔진 꺼지면 락온 불가"·`HighAspectPreventLock` 우회 → **NPC가 플레이어 기체보다 락온하기 쉽다.** 수용하기로 결정됨.

### 2.7 규모 상한 — 플레이어 락온 스윕

`DFUNC_AAM`은 FixedUpdate당 `AAMTargets` 1개씩 돈다. 한 바퀴 = **전체 엔트리 수 × 1틱**(50Hz 기준 70개 ≈ 1.4초). **비활성 풀도 순번을 차지한다.** CPU 비용은 늘지 않고 반응 지연만 는다 → **전체 풀 80개 이하 권장.** 함선은 본체 + 부위마다 락온 마커가 있어 전함 1척 = 6개, 구축함 1척 = 3개로 센다. 플레이어 쪽 `DFUNC_AAM`은 추후 자체 스크립트로 대체할 예정이라(8절) 이 제약은 그때 다시 본다.

### 2.8 이동은 `Update()`에서

`Instructions.txt`: 미사일은 Update에서 추적하도록 설계됐으니 움직이는 타겟도 Update에서 움직이라고 명시. → NPC 이동은 **`Update()`에서 Transform 직접 이동**. Rigidbody/FixedUpdate 이동은 피한다.

### 2.9 권장 규모

비용을 결정하는 건 개수보다 **위치 동기화 여부**다.

| 분류 | 비용 | 권장 |
|---|---|---|
| 정지 지상 유닛 | 위치 동기화 0, 포탑 조준은 로컬 계산. 매 프레임 Update가 모든 클라이언트에서 돈다 → 원거리 잠재우기(아래) | 잠재우기 없이 50~60기, 잠재우기 + 플레이어 AAM 레이어에서 30 제외 시 100~120기 |
| NPC 전투기 | 패턴 동기화(4.5절) 시 기체당 약 0.3Hz | 20기 가능 |
| 전체 풀 | 락온 스윕 지연(2.7) | 80개 이하 |

**원거리 잠재우기**(`ACO_SAMSite`/`ACO_AAGunSite`): **조종 중인**(`SaccEntity.Occupied`) 플레이어 기체가 `max(DetectRange, LoseTargetRange) + SleepMargin`(3km) 안에 하나도 없으면, 타겟을 놓고 포탑이 기본 자세로 돌아간 뒤 잠든다 — 탐지/조준/기총 계산을 건너뛰고 `SleepCheckInterval`(1초)마다 깨울지만 본다(확인 시점은 유닛마다 흩뜨림). 기준 목록은 그 유닛이 쏘는 대상과 같은 베이크된 `AAMTargets`라 세워 둔 빈 기체나 걸어 다니는 플레이어는 깨우지 않는다. 깨우는 거리가 탐지 거리보다 넉넉해 탐지 타이밍은 그대로이고, 잠든 동안에도 피격/파괴/마커/점수는 정상(체력은 SaccTarget 소관). 모든 클라이언트가 같은 기준(동기화되는 Occupied)으로 판단한다. `SleepMargin` ≤ 0이면 끈다. 플레이어 `DFUNC_AAM`의 `AAMTargetsLayer`에서 30(적 지상)을 빼면 지상 유닛 수가 락온 스윕(2.7)에도 영향을 주지 않는다(AGM은 SphereCast라 무관).

⚠ 오너십 분산이 없는 현재는 **1인 인스턴스가 최악의 부하**(모든 NPC를 한 클라이언트가 돌림). 1인 기준으로 성립하는 수치를 기준선으로 잡는다.

---

## 3. 파일 구조 (실제)

```
Assets/ACO_Addon/
  Docs/DESIGN.md                   본 문서
  Scripts/
    Core/
      ACO_CombatDirector.cs        전장 지휘: 타겟당 공격 정원, 대기 순서, 교대. 동기화·Update 없음
      ACO_MissionGenerator.cs      미션 생성: 오너가 풀 유닛을 무작위 배치(지상/바다/공역 밖), 페이즈 목표 배치·태그, 함대, 결과 배열만 동기화
      ACO_HexTerrain.cs            육각 기둥 지형: 씨앗 노이즈(정수 해시) 높이 → 기둥 배치(런타임 Instantiate 1회 후 재사용, 프레임 분할)
      ACO_MissionManager.cs        미션 진행: 페이즈 1~5개(무작위 종류), 목표 판정, 증원 웨이브(페이즈 시작/교착), 팀 점수·랭크, 이벤트 방송
    AirUnits/
      ACO_NPCFighterAI.cs          전투기 두뇌: 순찰/추격/이탈/대기 상태머신, 요격 조준, 편대 간격, 격추/부활, 숙련도
      ACO_NPCFighterNet.cs         전투기 동기화: 오차 기반 전송, 원격 예측/보정, 소유권. Update 없음(AI가 OwnerTick/RemoteTick 호출)
      ACO_NPCFlightController.cs   비행 모델: 뱅크-투-턴, 월드축 분리 제어, 지형 회피. Update 없음(AI가 FlightTick 호출)
    GroundUnits/
      ACO_SAMSite.cs               SAM 포대(함선 VLS 겸용): 탐지, 포탑 조준, 원거리 잠재우기, 격추/부활
      ACO_AAGunSite.cs             대공포(함포/CIWS 겸용): 늦은 속도 리드 + 조준 흔들림, ACO_NPCGun 사격, 원거리 잠재우기, 격추/부활
      ACO_StaticTarget.cs          고가치 표적(공장/벙커): 무장 없음, 체력은 SaccTarget, 잔해 전환, 미션 계약
      ACO_Tank.cs                  전차: 서버 시간 지상 항로(순찰 고리/매복), 지면 추종, 거리 LOD, 포탑은 같은 오브젝트의 ACO_AAGunSite
      ACO_EscortUnit.cs            호위 대상(아군): 서버 시간 항로(Catmull-Rom), 이동 방식 공중/해상/지상, 적 우선순위, 격추 연출
      ACO_Warship.cs               부위 파괴형 함선(전함/구축함): 본체 체력, 부위 파괴 → 본체 피해, 서버 시간 원 순항, 편대 호위, 격침 연출 필드
    Weapons/
      ACO_MissileLauncher.cs       공용 런처: 락온 누적·피락온 경고·탄약/재장전, SAV 미사일 덕 타이핑 계약. Update 없음
      ACO_Missile.cs               자체 미사일: 유도 3종, 콜드런치, 대응책/접근 경고(SAV 호환), 피격자/사수 판정, 후연, 피격 충격
      ACO_DFUNC_Missile.cs         플레이어 미사일 다이얼 기능(DFUNC_AAM 대체): 레이어 표적, 슬롯 장전, 강화 가능한 성능
      ACO_DFUNC_Special.cs         특수무장 슬롯 다이얼 기능: 격납고에서 고른 무장 모듈을 장착하고 이벤트를 넘긴다
      ACO_Ordnance.cs              무유도 폭탄/로켓 탄체: 중력/추진 비행, 충돌 폭발, 범위 피해(아군 제외), 쏜 사람 판정
      ACO_NPCGun.cs                NPC 기총: 시각 전용 파티클 + 타겟 오너의 조준 체류 판정. Update 없음
    UI/
      ACO_ScoreHUD.cs              머리를 따라오는 점수/시간/페이즈/웨이브 패널, 페이즈·결과 안내(로컬)
      ACO_TargetMarkers.cs         표적 마커: 항상 같은 화면 크기, 항공기 사각형/지상 육각형, 페이즈 목표 TGT, 시선 정보 + 체력 바(로컬)
      ACO_MissionLog.cs            작전판: 격추·페이즈 기록, 조종사 집계, 끝나면 디브리핑(로컬)
      ACO_MissionStartButton.cs    작전 콘솔 버튼: Interact → 페이즈 수를 정해 미션 시작
      ACO_Loadout.cs               격납고: 내 특수무장 선택(로컬), 조종석에 앉을 때 특수무장 슬롯이 읽는다
      ACO_HangarButton.cs          격납고 버튼: 특수무장 선택 / 특수무장 구매 / 강화 구매
      ACO_Shop.cs                  격납고 강화 상점: 항목별 단계 구매, 보유 크레딧·지난 보상 표시(로컬)
  Shaders/ACO_MarkerOverlay.shader 단색, ZTest Always(항상 맨 위)
  Generated/                       HUD/작전판 빌더가 만든 메시·머티리얼(마커 테두리, 체력 바, TMP 오버레이, 보드 배경)
  Editor/
    ACO_PrefabBuilderWindow.cs     메뉴 ACO → Create Prefabs... : 프리팹 생성 창(항목별 체크, 이미 있는 것은 기본 해제 + 덮어쓰기 확인)
    ACO_PrefabBuilder.cs           NPC/표적 프리팹 생성(미사일 AAM/VLS, 전투기, SAM, 대공포, 공장, 벙커, 전함, 구축함, 전차, 아군 콘보이 차량, 아군 수송기)
    ACO_HUDBuilder.cs              ACO_HUD(점수 패널 + 마커 64개 + 체력 바), ACO_MissionBoard(작전판), ACO_MissionConsole(작전 콘솔)
    ACO_SceneBaker.cs              빌드 시 자동 + 메뉴 ACO → Bake NPC References: 주변 NPC 목록, CombatDirector, 생성기 풀(전투기/지상/고가치 표적),
                                   관리자·HUD·마커·작전판·콘솔 연결. 메뉴 ACO → Create Mission Manager / Mission Generator / Combat Director
    ACO_CombatDirectorGizmo.cs     씬 뷰 시각화: 플레이 모드에서 NPC별 모드/타겟/대기 순번/교대 시간, 타겟별 공격 수·대기 궤도·자기 방어 거리
    ACO_MissionGeneratorGizmo.cs   씬 뷰 시각화: 공역, 안전지대, CAP 지점, 사용 중인 유닛
  Udon/                            U# Program Asset (스크립트당 1개, UdonSharp 필수)
  Prefabs/                         빌더가 생성: ACO_Missile_AAM, ACO_Missile_VLS, NPC_Fighter, NPC_SAM, NPC_AAGun, NPC_Factory, NPC_Bunker,
                                   NPC_Warship, NPC_Destroyer, NPC_Tank, NPC_ConvoyTruck, NPC_Transport, ACO_HexColumn, ACO_HUD, ACO_MissionBoard, ACO_MissionConsole
```

- 레이어 상수는 별도 파일 없이 `ACO_PrefabBuilder`에 있다(25 아군 항공기 / 29 적 항공기 / 30 적 지상 / 17 히트박스). 런타임은 각 유닛의 `TargetLayers` 필드를 쓴다.
- 업데이트 비용을 줄이려고 Update를 가진 스크립트를 최소화했다: 전투기는 AI 1개만, 비행 컨트롤러와 런처는 Update가 없다. 지상 유닛은 원거리 잠재우기로 플레이어 근처 유닛만 계산한다(2.9).
- 새 U# 스크립트를 추가하면 Unity에서 Assets → Refresh(Ctrl+R)가 돌아야 프로그램 에셋이 인식된다 — 안 그러면 UdonSharp가 다른 스크립트를 먼저 컴파일하면서 "type could not be found"(CS0246)를 낸다(플레이 모드 중이거나 자동 새로고침이 꺼져 있을 때).
- `SaccFlightAndVehicles.Runtime.asmdef`가 `autoReferenced: true`라 ACO 스크립트는 별도 asmdef 없이 SAV를 참조한다.

---

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
- **ACO 폭탄/로켓** (`ACO_Ordnance`, 프리팹 `ACO_Bomb`/`ACO_Rocket`, 모듈 UGB/RKT): 발사는 ACO_DFUNC_Missile이 그대로 한다(슬롯 장전·강화·장전 막대) — 탄체가 ACO_Ordnance면 모기체 **속도 벡터**를 이어받고(`LaunchVelocity`) 비행 성능·범위 피해를 넣는다. 락온은 안 한다(`TargetLayers` 0, `AllowUnguidedFire`). `HoldToFire`면 누르고 있는 동안 `FireInterval`마다 연사. 탄체는 매 프레임 이동 구간 Linecast(`HitLayers` = Default·Water·Environment·Walkthrough)로 지형/유닛에 닿으면 터지고(`ArmTime` 동안은 충돌 안 함, 내 기체 콜라이더는 무시), `SplashRadius` 안의 SaccTarget마다 한 번씩 피해 = `Damage` × (1 → `MinSplashFraction`, 콜라이더 경계까지 거리에 선형). 호위 대상(ACO_EscortUnit)은 피해를 받지 않는다. 피해는 쏜 사람만(격추 점수). SAV 폭탄은 범위 피해를 리지드바디가 있는 대상에만 줘서(`attachedRigidbody`) NPC 지상 유닛엔 직격만 들어갔다 — 그래서 따로 만들었다. 기본값: UGB 슬롯 2 / 24발 / 4초, 0.3초 간격 투하, 중심 150·반경 35m. RKT 슬롯 8 / 72발 / 3초, 0.08초 연사, 450m/s, 중심 45·반경 12m. 모듈은 무장 카탈로그(6-22)의 UGB/RKT 정의로 기체 셋업이 넣는다 — 발사 지점·발사음·잔탄 텍스트는 SAV 폭탄/로켓(DFUNC_Bomb)에서 가져오고 그 SAV 기능은 빼서 끈다(앞서 SAV 기능을 UGB/RKT 모듈로 넣었으면 바꿔 끼운다). 장전 막대 묶음 이름은 모듈마다 다르다(`ACO_MissileSlotBars_Module_UGB` 등).
  - **발사 동기화 (SAV 방식)**: 폭탄/로켓은 전용 이벤트 `NetLaunchOrdnance(슬롯, 최고 속력, 가속)` 9바이트만 보낸다. 받은 쪽은 자기 화면의 기체 발사 지점과 `SaccAirVehicle.CurrentVel`(다른 클라이언트에선 SAV_SyncScript가 채우는 동기화 속도)에서 쏜다 — SAV 폭탄과 같다. 이벤트가 오는 동안 기체가 앞으로 간 만큼(폭탄 ~20m, 로켓 수십 m) 다른 사람 화면의 궤적이 어긋나지만 연출만이고, 피해는 쏜 사람 화면 기준이라 판정엔 영향이 없다(쏜 사람의 위치·속도·시각을 보내 맞추는 방법은 대역폭 대비 이득이 작아 쓰지 않기로 했다). 로켓 연사가 초당 10발을 넘으므로 `[NetworkCallable(60)]`.
  - **CCIP**(폭탄 낙하 지점, `CcipReticle`): 추진 없는 탄체를 고르면 `CcipInterval`(0.05초)마다 다음 발사 슬롯의 발사 지점에서 기체 속도로 폭탄과 같은 식(중력 → 공기 저항 → 이동)을 굴려 지형/물에 닿는 점을 찾는다(처음 3초는 0.1초, 그 뒤 0.25초 스텝, 최대 `CcipMaxTime` 40초, 유닛 히트박스 17은 빼고 지형만). 표시는 표적 마커와 같은 방식(머리에서 그 방향으로 30m, 고정 화면 크기 `CcipAngle` 3°, ACO/MarkerOverlay, `PostLateUpdate`). 표시 오브젝트(원 테두리 + 가운데 점)는 폭탄 모듈을 만들 때 모듈 아래에 생긴다(`ACO_HUDBuilder.CreateCcipReticle`).
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

### 6-2. 미션 생성기 ✅ (`ACO_MissionGenerator`)

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
- **지상**: 공역 안 무작위 중심 → `RaycastHeight`에서 아래로 Raycast(`GroundLayers`). 공역 밖·안전지대(콜라이더 바운딩 박스 + `SafeZoneMargin`)·급경사(`MaxGroundSlope`)·너무 낮음(`MinGroundHeight`)이면 다시. 그룹 4~8대: 콘보이(진행 방향 일렬, `ConvoySpacing`, 같은 방향) 또는 부대(반경 `ClusterRadius`, 최소 간격 `MinUnitSpacing`, 무작위 방향). 유닛마다 지면을 다시 잰다. 자리를 못 찾으면 3번 더 시도 후 남은 유닛은 이번 미션에서 끈다.
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

- 미션 시작/일반 증원 확률은 생성기의 `VeteranChance`(0.35)/`AceChance`(0.05). 웨이브는 미션 관리자가 확률을 정한다(웨이브가 거듭될수록 상승).
- 단계는 전투기별로 동기화하고 **모든 클라이언트**에서 적용한다 — 비행/락온은 오너가, 기총 체류 시간은 피격자(타겟 오너)가 쓰기 때문. AI는 처음 적용할 때 인스펙터 값을 저장해 두고 배율을 곱하므로 여러 번 적용해도 누적되지 않는다. 스폰 전에 적용해야 순항 속도가 반영된다.
- `VeteranVisuals`/`AceVisuals`(AI 인스펙터): 해당 단계일 때만 켜는 오브젝트(전용 도색, 트레일 등).

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

  생성기 조회: `CountFreeLandGround/CountFreeHvt(ships)/CountFreeFighterUnits/CountFreeTankUnits/CountFreeEscorts(moveMode)`, `HasGroundNear/HasShipSiteNear/CanBuildEscortRoute`("쓸 수 있는" = 쓰지 않던 것 + 파괴된 것).
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

### 6-6. HUD / 표적 마커 ✅ (로컬 전용, 동기화 없음)

- **점수 HUD** (`ACO_ScoreHUD`): 셋째 줄은 페이즈(`PHASE 2/4  SEAD  3/5`, 할당량은 점수, 시한 표적은 남은 시간, 페이즈 사이엔 `NEXT PHASE 7s`), 점수 옆에 지금 배율. 페이즈 시작/클리어 때 가운데에 `PhaseBannerTime`(5초) 안내. 머리 기준 `HeadOffset`(왼쪽 위 앞 0.9m)에 패널. 회전만 `FollowSharpness`로 부드럽게 따라가고 **위치는 매 프레임 머리에 고정**(위치까지 늦게 따라가면 300m/s 비행 중 패널이 수십 m 뒤처진다). 4Hz로 갱신: `SCORE  x배율` / `TIME`(남은 시간, 1분 미만 빨강; 제한 없으면 `ELAPSED`) `WAVE` / 페이즈 줄. 끝나면 가운데에 `MISSION COMPLETE / RANK S / 보너스` 또는 `MISSION FAILED / RANK F`를 `ResultDisplayTime`(8초) 동안 띄우고 숨긴다(자세한 결과는 작전판). 미션 전(대기)에는 숨김(`ShowWhenIdle`).
- **표적 마커** (`ACO_TargetMarkers`): 미션 생성기 풀(전투기/지상/고가치 표적/함선 부위) 중 활성이고 살아 있는(SaccTarget 체력 > 0) 유닛을 `MaxDistance`(10km) 안에서 표시. 격침된 함선의 남은 부위는 빠진다.
  - **화면 크기**: 머리에서 유닛 방향으로 `DisplayDistance`(30m)에 놓고, 기본 크기 = `2·30·tan(MarkerAngle/2)`(2.5°). **원근감**: `NearDistance`(1km)까지 1배, `MaxDistance`(10km)에서 `FarScale`(0.45)배, 사이는 log(거리)에 비례해 줄인다 — 거리와 무관하게 같은 크기면 원근감이 애매해서. 셰이더가 ZTest Always라 지형/구름/기체 뒤에서도 보인다. 머리 기울기(롤)에 맞춰 세운다.
  - **반투명**: 테두리 알파 0.55(목표 0.75), 마커 글자 0.9, 점수 HUD 글자 `TextAlpha`(0.85). 테두리 색은 `Generated/ACO_Marker*.mat`의 `_Color`에서 바로 바꿀 수 있다.
  - **아군(호위 대상)**: 파란 테두리(`FriendlyMaterial`, `Generated/ACO_MarkerFriendly.mat`) + 위에 이름(`VIP`), 목표처럼 `ObjectiveMaxDistance`까지 보인다. 예전 HUD 프리팹처럼 머티리얼 칸이 비어 있으면 Bake가 채운다(아군/경유점/목적지).
  - **호위 항로**(`ShowEscortRoute`, 공중 호위/지상 호위 페이즈): 호위 대상이 아직 지나지 않은 경유점 = 옅은 파란 마름모(`WaypointMaterial`, 크기 `WaypointScale` 0.7) + "WP n", 목적지 = 큰 하늘색 마름모(`DestinationMaterial`, `ObjectiveScale`) + "DEST 거리". 목적지에서 `WaypointMinGap`(1.5km) 안의 경유점(착륙 접지점 등)은 생략. 마름모 = 사각형 메시를 45° 돌린 것, 마커 풀을 같이 쓴다(유닛 다음).
  - 모양: 항공기 = 사각형, 지상 = 육각형. **지금 페이즈 목표**(관리자 `GetActiveObjectiveTag`와 태그가 같은 유닛 — 전투기 목표면 빨간 사각형) = 빨간 테두리 + 위에 빨간 `TGT`(`ObjectiveScale` 1.3배), 목표는 `ObjectiveMaxDistance`(60km)까지 보인다. 관리자가 연결되지 않았으면 예전처럼 고가치 표적이 목표.
  - **시선 정보**: 머리 정면에서 `LookAngle`(4°) 안의 유닛 중 가장 가운데 하나에 `종류  거리`(예: `ACE  4.2km`, `SAM  850m`). 전투기는 숙련도 `ACE/VET/NOV`, 지상/표적은 각 유닛의 `MarkerLabel`(SAM, AAG, FAC, BNK, BB, DD, VLS, GUN, CIWS).
  - **체력 바**: 시선 정보 아래에 남은 체력(`SaccTarget.Health / FullHealth`) 막대. 비율 > `HealthMidThreshold`(0.5) 녹색, 그 이하 노랑, `HealthLowThreshold`(0.25) 이하 빨강. 끄려면 `ShowHealthBar`. 머티리얼 `Generated/ACO_Health*.mat`(배경 큐 4010, 채움 4011 — 둘 다 ZTest Always라 큐로 그리는 순서를 정한다). 원격 NPC 체력은 오너의 SaccTarget 동기화를 따라 약간 늦게 줄 수 있다.
  - **락온 표시**: 내가 조종 중이고 고른 `ACO_DFUNC_Missile`(`Weapons`, Bake가 채움)의 표적을 읽는다. 락온을 시도하는 동안(표적이 잡혔고 아직 락온 전) 그 테두리가 `LockBlinkRate`(8회/초) 일정한 속도로 계속 깜빡이고(락온되면 멈추고 빨갛게 켜진다), 시선 정보(이름표 `종류 거리` + 체력 바)가 시선 대신 **락온 대상에 고정**된다. 조준이 시작될 때마다 무작위 방향을 골라 마름모(`LockDiamond`, 사각 테두리 메시를 45° 회전)가 마커 크기 × `DiamondStartDistance`(2.5)에서 출발해 락온 진행도만큼 중심으로 다가온다(남은 거리 = (1−진행도)², 막바지에 천천히 붙는다). 락온되면 마름모가 테두리에 겹쳐지고(`DiamondScale` 0.8 — 0.71이면 꼭짓점이 변 가운데에 닿는다) 마름모와 테두리가 `LockedMaterial`(빨강)로 바뀐다. 진행도가 0으로 떨어지면(조준 이탈) 마름모를 숨기고 다음 조준은 새 방향에서 시작한다. 예전 HUD 프리팹엔 마름모·머티리얼이 없으므로 Bake가 없으면 만든다(`ACO_HUDBuilder.EnsureLockVisuals`).
  - **특수무장은 십자**: 고른 무기의 `CrossLockIndicator`가 켜져 있으면(특수무장 슬롯 안의 모듈 — Bake가 켠다, 4AAM 프리셋도) 락온 진행 표시를 마름모 대신 세워진 십자(+, 가운데가 빈 팔 4개, `LockCrossMesh`, 크기 `CrossScale`)로 그린다. 같은 표시 오브젝트의 메시만 바꿔 쓴다. 메시는 HUD 빌더/Bake(`EnsureLockVisuals`)가 만든다.
  - 마커 풀 64개(빌더 상수). 상태가 바뀔 때만 SetActive/메시/머티리얼을 바꾼다. 갱신은 `PostLateUpdate`(VRChat에서 머리 위치가 확정된 뒤).
- **프리팹**: `ACO → Create Prefabs...`의 HUD → `Prefabs/ACO_HUD`. 메시(`ACO_MarkerSquare/Hexagon`), 머티리얼(녹색 항공기/지상, 빨강 목표), TMP 오버레이 머티리얼(`TextMeshPro/Distance Field Overlay`)은 `Generated/`에 저장(다시 만들면 내용만 덮어써 참조 유지). 씬에 하나 두면 빌드 시(또는 Bake) 생성기/관리자가 자동 연결된다.
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

### 6-10. 전차 / 지상 호위 ✅ (`ACO_Tank`, `NPC_Tank` / `NPC_ConvoyTruck`, 페이즈 CONVOY)

- **전차**(`ACO_Tank`, 지상 풀): 미션 생성기가 준 **지상 항로를 서버 시간으로** 따라간다(경유점 최대 6, Catmull-Rom, `Speed` 10m/s). 위치 = f(항로, 배치 시각, 경과 시간)라 동기화가 없다. 지면은 0.2초마다 Raycast(`GroundLayers`)로 재서 따라가고, 로컬 플레이어가 `LodDistance`(12km)보다 멀면 자세를 `LodInterval`(1초)마다만 갱신한다 — 쏘고 맞는 쪽(가까운 플레이어) 화면에선 매 프레임 정확하다.
  - **포탑/사격 = 같은 오브젝트의 `ACO_AAGunSite`**(재사용): 표적 레이어 = **아군 지상 26**(SaccEntity `AAMTargetsLayer`도 26 — 빌드 시 콘보이/플레이어 지상 차량 마커로 채워짐), `MinTargetAltitude` 해제, 포탑 30°/s·10°/s, 고각 −8~20°. 사격은 `ACO_NPCGun`의 체류 판정을 단발포처럼(체류 1.5초 → 0.25초 사격, 한 발 35, 휴식 4초). 판정은 피격자(타겟 오너). 원거리 잠재우기(2.9)도 그대로 — 콘보이(SaccEntity 없는 표적)가 가까이 오면 깬다. `RequireLineOfSight`라 지형에 가리면 쏘지 않는다.
  - 체력 SaccTarget(150, 장갑 2 → 미사일 직격 50%)은 포탑과 공유, `ExplodeOther` = 포탑 + 전차. 격파되면 멈추고 포탑만 숨긴다(차체 = 잔해, `DeathEffects` 켬).
  - 생성기 배치: 지상 그룹/방공 링/증원에는 쓰지 않는다(함선과 같은 "스스로 움직이는 지상 유닛").
    - **순찰 소대** `BackgroundTankPlatoons`(2) × `TankGroupSize`(3): 공역 아무 곳 지면에 반경 `TankPatrolRadius`(700m) 사각 고리(네 점 모두 지면) — 소대원은 같은 고리를 `TankSpacing`(40m) 간격으로 계속 돈다.
    - **매복조**(지상 호위 페이즈): 호위 항로를 따라 고르게, 항로 옆 `AmbushDistanceMin~Max`(0.4~0.9km) 매복 지점까지 `AmbushApproachDistance`(1.5km) 바깥에서 달려와 멈춘다. 지면이 있는 곳만.
  - 동기화: 전차 칸(`_tankSlot`, 지상 풀 순서로 정해짐)마다 항로 `_tankRoute`(6칸)/`_tankRouteCount`/`_tankLoop`/`_tankOffset`(소대 안 앞선 거리). 출발 시각 = 지상 유닛 배치 시각. 적용 시 `SetRoute` → `SetStartTime` → `MissionSpawn`.
- **아군 콘보이**(`NPC_ConvoyTruck` = `ACO_EscortUnit` 지상 이동 방식, `Speed` 20m/s, 락온 마커 **레이어 26**, 체력 250, 플레이어 기총 오사 방지): `ConvoySize`(3)대가 같은 지상 항로를 `EscortConvoySpacing`(60m) 간격 행렬로 간다(선두가 앞 — 유닛별 `_escortOffset`). 짧은 항로는 옆 흩뜨림을 길이의 15%로 줄인다.
  - **지상 항로 만들기**: 목적지(도시/비행장 가장자리, 없으면 아무 곳)에서 거꾸로 직선 위에 경유점 `EscortRoutePoints`(4)개(중간 경유점은 옆으로 흩뜨림). ① 경유점과 그 사이 3곳을 빠르게 확인해 일찍 버리고, ② **차량이 실제로 달리는 Catmull-Rom 곡선**(ACO_EscortUnit과 같은 식)을 `RouteCheckSpacing`(150m)마다 다시 확인한다 — 각 점이 지상 배치 조건(공역, 안전지대, 표면 경사 `MaxGroundSlope`) + **마른 지면**(물 콜라이더 `SeaSurfaceLayers`에 먼저 맞거나 해수면 + `RouteWaterMargin`(1m)보다 낮으면 버림, `AvoidWaterOnGroundRoutes`), 이웃 확인점 사이 **오르내림 경사 `MaxRouteGrade`(18°) 이하**(절벽/급한 둑). 한 점이라도 걸리면 다른 방향으로 다시(`MaxPlacementAttempts` 40회). 해상 항로도 같은 곡선 확인(전부 바다).
  - 확인은 무작위를 쓰지 않아 항로 씨앗 재현(6-4)에 영향이 없다. 비용은 6km 항로 한 번에 Raycast 약 80개 × 시도 수 — 페이즈 시작 때 한 번(사전 확인 + 배치)뿐이다.
  - 항로 옆 매복 전차/방공 시설 자리도 마른 지면만 쓴다. 도로는 모른다(지형만 보고 거의 직선) — 도로를 따라가게 하려면 도로 웨이포인트 지정(⏳)이 필요하다.
- **페이즈 CONVOY**(관리자 `UpdateConvoy`): 살아 있는 차량이 모두 도착하면 클리어 + `PhaseClearBonus` + `ConvoyBonus`(1200) × 생존 비율 × 배율, 전멸이면 그 즉시 미션 실패(`FAIL_CONVOY_LOST`, "CONVOY DESTROYED" / 디브리핑 "(CONVOY LOST)"). 위협 = 매복 전차(콘보이 포격) + 방공 시설(플레이어). 적 전투기는 공중 표적(25)만 노려 콘보이엔 오지 않는다. HUD: `PHASE 2/4  CONVOY  CVY 3/3  4.2km`(가장 뒤처진 차량 기준, 절반 이하 빨강). 조회: `GetConvoyAlive/GetConvoyTotal/GetConvoyRemainingDistance`.

### 6-11. 육각 기둥 지형 — 섬/고원/산맥/협곡 프리셋 ✅ (`ACO_HexTerrain`, 프리팹 `ACO_HexColumn`)

- **자연 머티리얼**(셰이더 `ACO/HexTerrain`, 머티리얼 `Generated/ACO_HexTerrainNatural.mat`, 텍스처 `Textures/Terrain` — `Tools/aco_terrain_tex.py`가 만드는 타일링 노이즈): 색을 기둥별 머티리얼이 아니라 **월드 좌표**로 정해 육각 경계에서 끊기지 않는다. 높이(해수면 `_SeaLevel` 기준): 젖은 모래·모래(`_SandHeight` 30) → 풀(초원/마른 풀 `_DryAmount`, 숲 `_ForestAmount` — 매크로 노이즈 분포) → 흙(`_DirtHeight` 480) → 바위(`_RockHeight` 700) → 눈(`_SnowHeight` 1000, 평평한 곳만). 경계는 매크로·디테일 노이즈로 ±`_BlendNoise`(120m) 흔든다. 경사: 수직 벽 = 절벽(`_StrataScale` 40m 지층 줄무늬), 경사 치마 = 약간 흙빛(`_SlopeDirt`). 디테일은 가까이 40m·멀리 420m 두 배율(반복 무늬 숨김), 잔결은 400~3000m에서 줄인다. 노멀: 두 배율 디테일 노멀 + 치마 음영을 위쪽으로 눕힌다(`_SlopeFlatten` 0.6 — 윗면/치마 꺾임 = 육각 윤곽이 덜 보임). 메시에 UV·탄젠트가 없어 정점 함수가 투영 축으로 탄젠트를 만든다(기둥은 회전 없이 놓인다). 적용: Tools 창 "육각 지형 자연 머티리얼" 또는 `ACO → Terrain → Apply Natural Terrain Material` — 기둥 프리팹 머티리얼 교체 + 씬 지형 `HeightMaterials` 비움(높이대 교체 끔) + `_SeaLevel` = 지형 `SeaLevel`. 새 지형/기둥 프리팹은 처음부터 자연 머티리얼. 셋업 검사가 예전 단색이면 알린다(정보).
- **나무**(셰이더 `ACO/HexTrees`, 기둥 자식 `Trees`, 메시 `Generated/ACO_TreePatch.asset` 공유, 머티리얼 `Generated/ACO_HexTrees.mat`): 기둥 윗면마다 나무 자리 500개(최소 간격 다트 던지기, 그루당 정점 26 — 수관 고리 3개 + 줄기)를 깔아 두고 정점 셰이더가 그루마다 정한다. 지면 셰이더와 **같은 숲 분포**(매크로 B + 디테일 A, 흙 높이 ×0.85 아래, 해안 모래 위)면 세우고(`_ForestDensity` 0.9, 숲 가장자리는 작고 성기게), 숲 밖 풀밭엔 `_LoneTrees`(4%) 외딴 나무, 아니면 크기 0. 무작위(크기 ±30%·회전·폭·자리 흔들기 ±7m·종류)는 월드 밑동 위치 해시라 기둥마다 달라 보인다. 종류: `_ConiferStart` 150m → `_ConiferFull` 420m 사이에서 침엽수 비율이 늘어난다(한 정점 구조를 침엽수 모양 = 정점, 활엽수 모양 = UV2/UV3 사이에서 바꾼다 — 따로 메시가 없다). 카메라에서 `_FadeStart` 1.8km → `_FadeEnd` 2.6km에 걸쳐 작아져 사라지고, LOD Group(화면 높이 0.12 — VR 기준 약 3km)이 그 너머 기둥의 나무를 아예 안 그린다. 그림자·충돌 없음, 텍스처 없이 정점 색 + 색 속성, GPU 인스턴싱. 장식이 놓인 칸(도시·마을·비행장·등대·레이더 — `ACO_TerrainFeatures._taken`)은 장식이 끝나면 `ACO_HexTerrain.SetCellTrees(i, false)`로 끄고, 새 지형마다 기둥을 놓을 때 다시 켠다. 숲 분포 값(해수면·높이·숲 양·텍스처·배율)은 [적용]이 지면 머티리얼에서 나무 머티리얼로 복사한다 — 지면 값을 바꾸면 다시 적용. 부하 어림: 3km 안 보이는 기둥 약 30~40개 × 13k 정점 ≈ 50만 정점(대부분 크기 0으로 접힘), 지형 생성 때는 기둥마다 자식 하나 더(Instantiate).

- **기둥**(`ACO_HexColumn`, 빌더가 메시 생성): 평평한 육각 윗면(꼭짓점 반지름 400m — 이웃 중심 간격 약 690m, 윗면끼리 빈틈없이 맞물림) + 바깥으로 수평 800m 이어지는 **15° 경사 치마**(약 214m 내려감) + 그 아래 2500m 수직 벽. 이웃과 높이 차가 214m 이하면 높은 기둥의 치마가 낮은 기둥 위를 덮어 **차량이 오르내리는 비탈**(콘보이 `MaxRouteGrade` 18° 이하)이 되고, 그보다 크면 절벽(항로 검사가 피함). MeshCollider(비볼록, 같은 메시), 레이어 Default. Bake가 씬 지형의 `HexRadius`를 프리팹 값으로 맞추고, 기둥 프리팹 메시 크기(윗면 반지름 = 메시 X 범위 / 2.866)가 칸과 다르면 경고한다(프리팹을 다시 만들 것). 빌더는 메시 에셋을 지우고 새로 만든다(덮어 복사하면 편집기 메모리와 어긋나 크기가 안 바뀌어 보일 수 있었다).
- **지형 프리셋**(씨앗마다 `PresetWeights` 비율로 하나, `ForcedPreset` 0~7이면 고정): 땅 모양은 원(blob, 중심에서 `1 − (거리/반지름)²`)과 능선 선분(같은 식, 선분까지 거리)의 최댓값, 협곡은 따로 "깎는 선"으로 높이만 낮춘다.

  | # | 프리셋 | 땅 모양 | 높이 |
  |---|---|---|---|
  | 0 | 큰 섬 | 반지름 `BigIslandRadius`(60% ±15%) 섬, 중심이 가운데에서 `BigIslandOffset`(40%)까지 벗어남 + 작은 섬 ≤`SatelliteIslands`(3, 범위 밖이면 반대편) | 산(아래) |
  | 1 | 군도 | 섬 `IslandCountMin~Max`(4~8)개를 `ArchipelagoSpread`(85%) 안에, 중심 간격 ≥ (반지름 합) × `IslandSpacing`(0.8) — 겹치면 자리를 최대 6번 다시 찾음 | 산 |
  | 2 | 쌍둥이 섬 | 무작위 축 양쪽 큰 섬 둘(반지름 34~40%), 사이 해협 약 2~6km, 해협 끝 작은 섬 | 산 |
  | 3 | 환초 | 고리(`AtollRadius` 55%) 위 섬 12개(몇 개 빠져 물길), 안쪽 석호는 바다, 가끔 가운데 작은 섬 | 산 × 0.45 |
  | 4 | 열도 | 범위를 가로지르는 휜 곡선(베지어)을 따라 섬 7~11개, 가운데가 큼 | 산 × 1.15 |
  | 5 | 고원 | 넓은 땅(`PlateauRadius` 70% + 혹 2~3개) | **단 지형**: 안쪽·노이즈 높을수록 `PlateauTiers`(4)단 × `PlateauTierHeight`(160m, 이웃 단은 비탈로 이어짐), 단 위는 평평. 첫 단은 `PlateauCliff`(0.6) 비율만큼 한 단 더 높아 해안에서 **단애**로 솟고 나머지가 오르막 길목 |
  | 6 | 산맥 | 범위를 가로지르는 구불구불한 능선(꺾은선 4마디, 반폭 `RangeWidth` 34%) + `RangeBranchChance`(0.5)로 곁가지 능선 + 작은 섬 0~2개 | 능선 가까이 `RangeMaxHeight`(1500m) × 안쪽^1.8 × 능선 노이즈(두 배 촘촘) — 둘레 구릉, 봉우리와 고개, 이웃 경계 약 25%가 절벽 |
  | 7 | 협곡 | 넓은 대지(`CanyonRadius` 80%)를 끝에서 끝까지(양쪽 바다로 열림) 구불구불한 협곡 하나가 가름 + `CanyonBranchChance`(0.5)로 곁가지 | 대지 `CanyonHeight`(520m, 해안에서 곧 솟음), 협곡 바닥(폭 `CanyonWidth` 2.4km)은 저지대 높이, 벽은 `CanyonWallWidth`(1.2km)에 걸쳐 올라가는 절벽 — 저공 협곡 비행/바닥 콘보이 길 |

  - 섬 프리셋의 산 = 해안에서 안쪽으로 갈수록 `MaxHeight`(900m) × 안쪽 정도 × 산 노이즈^`HeightExponent`(1.6).
  - 모든 프리셋 공통으로 × **범위 가장자리 감쇠**(바깥 `EdgeFalloff` 12%에 걸쳐 0 — 작전 지역 바깥 바다와 자연스럽게 이어짐) × **제외 구역 감쇠**(항공모함 등 `ExcludeZones` 바운딩 박스에서 `ExcludeMargin`(1.5km) 안은 바다, 그 바깥 `ExcludeFalloff`(4km)에 걸쳐 원래대로) × **바닷길**(`CarveSeaLane` 켜면 각 제외 구역에서 작전 지역 밖까지 폭 `SeaLaneWidth`(4km) 바다, 가장자리 `SeaLaneFalloff`(3km) — 스폰 지점에서 항상 밖으로 나갈 수 있다).
  - 바닷길은 일자가 아니라 **가장 낮은 지형을 따라** 굽이친다: 범위를 덮는 격자(`SeaLaneGridSize` 1.2km, 20km면 38×38칸)의 칸마다 바닷길 없이 잰 높이로 비용(바다 10, 땅 10 + `SeaLaneLandCost`(30) × (1 + 높이/100m))을 매기고, 구역 중심에서 범위 밖 칸까지 다익스트라(8방향, 정수 비용 — 클라이언트마다 같은 길)로 가장 싼 길을 찾는다 → 12점 정도로 줄이고 모서리를 한 번 깎아(Chaikin) 매끄러운 꺾은선. 섬 사이 바다를 먼저 타고, 땅을 지나야 하면 해협/저지대/산맥의 고개/협곡 바닥을 따라 판다(협곡 바닥을 타면 피오르처럼 된다).
  - 만들기는 3단계로 프레임에 나눈다: 격자 비용 재기(프레임당 `ColumnsPerFrame`칸) → 길 찾기(프레임당 그 2배 칸) → 기둥 세우기.
  - 미리보기(파이썬 이식, 20km, 씨앗 3개) 땅 비율: 큰 섬 23~26%, 군도 10~20%, 쌍둥이 18~22%, 환초 19~22%, 열도 9~12%, 고원 32~45%, 산맥 34~38%, 협곡 40~52%. 땅이 넓은 고원/협곡/산맥은 함선 목표를 둘 바다가 모자랄 수 있는데, 페이즈 사전 확인이 불가능하면 다른 유형으로 바꾼다.
- **높이 공통**: 해안값 = 땅 모양 + (노이즈 − 0.5) × `CoastRoughness`(0.4, 해안선 흔들림). `CoastThreshold`(0.3)보다 낮으면 바다 — **기둥을 만들지 않는다**(씬의 바다가 보인다). 높으면 해안에서 안쪽 정도(0~1)로 위 표의 높이 → `HeightStep`(20m) 단위로 끊고, 해안 칸도 반드시 해수면 위. 노이즈는 정수 해시 값 노이즈 옥타브 합(`NoiseScale` 6km, `Octaves` 4). 높이대 머티리얼 4종(모래/풀/바위/눈, `HeightThresholds` 40/300/650m, GPU 인스턴싱 — 예전 방식, 아래 자연 머티리얼을 쓰면 끈다). `GetPreset()`/`GetPresetName()`으로 현재 프리셋을 알 수 있고 완성 로그에도 이름이 찍힌다.
- **범위와 비용**: `FieldRadius`(20km — 약 3,000칸, 이 중 섬 칸만 기둥이 생긴다, 보통 1,000~1,500개). 기둥은 처음에 한 번 `Instantiate`하고 씨앗이 바뀌면 다시 쓴다(바다가 된 칸의 기둥은 끈다). 프레임당 `ColumnsPerFrame`(200)개씩.
- **동기화 = 씨앗 하나**(`_seed`, `_generation`): 오너가 정해 보내면 모든 클라이언트가 같은 지형을 로컬로 만든다(프리셋 선택/땅 모양/노이즈 모두 정수 해시, 높이 끊기).
- **미션 연동**: 생성기 `HexTerrain`(Bake가 자동 연결) + `RegenerateTerrainEachMission`이면 미션 시작 때 새 씨앗으로 다시 만들고, 생성기 오너 화면에서 다 세워진 뒤 유닛을 배치한다(`TerrainWaitTimeout` 30초). 지상 목표/항로는 섬 위에, 함선은 섬 사이 바다에 놓인다(바다 판정 `NoHitIsSea` — 기둥이 없는 곳).
- 주의: 페이즈 목표 거리(`TargetDistanceMin~Max` 12~30km)는 플레이어(항공모함) 기준이므로, 항공모함을 범위 가장자리 바깥 바다에 두면 섬이 그 거리 안에 들어온다. 해수면(`SeaLevel`)은 지형과 생성기 둘 다 같게.

### 6-12. 지형 장식 — 도시/마을/비행장/랜드마크 ✅ (`ACO_TerrainFeatures`, 프리팹 `ACO_CityBlock_*` 등)

- **바다 셰이더**(`ACO/Ocean`, 머티리얼 `Generated/ACO_Ocean.mat`, 텍스처 `Textures/Water` — `Tools/aco_water_tex.py`: 물결 노멀(바람 쪽 정수 파수 사인파 + 잔물결 노이즈), 거품 R·큰 얼룩 G): 모든 무늬를 월드 XZ로 깔아 바다 평면(정점 4개 쿼드도 됨)의 크기·UV와 상관없다. 물결 노멀 세 배율(28m / 140m / 1.1km)을 다른 방향·속도로 흘리고, 6km 배율 큰 얼룩으로 물결 세기·색을 흔들어 고공에서 반복 무늬가 안 보인다. 가까운 잔물결은 250m → 2.5km에서 줄인다. 반사·햇빛 반짝임은 Standard 조명(스카이박스/리플렉션 프로브), 멀수록 거칠게(8km에서 0.82 — 반짝임 깜빡임 방지). 색은 내려다보면 `_DeepColor`, 비스듬하면 `_HorizonColor`. 마루(물결 기울기) × 거품 무늬 × 큰 얼룩으로 흰 파도(`_FoamAmount`). 선택 `Use depth texture`(`_ACO_DEPTH`): 물 밑 지형까지 시선 거리로 얕은 물 색(25m)·투명(6m)·해안 거품(4m) — VRChat은 그림자를 켠 실시간 방향광이 있어야 깊이 텍스처가 생긴다. **가까운 너울**(정점 이동): 원판 메시 `ACO_OceanSwell`(고리 72 x 둘레 160 ≈ 정점 1.15만, 가운데 약 1m · 가장자리 약 30m 간격, 경계 상자 아주 크게 — 컬링 안 됨, 충돌 없음)에 너울 머티리얼(`Generated/ACO_OceanSwell.mat` = 바다 머티리얼 복사 + `_SwellMode` Mesh). 정점 셰이더가 원판을 카메라(VR은 두 눈 가운데) XZ를 `_SwellSnap`(4m) 격자에 맞춘 자리 + 해수면 `_SeaY`로 옮기고 게르스트너 파도 4개(`_SwellWave1~4` = 방향·파장·높이·날카로움, 기본 180m 1.4m / 110m 0.8m / 68m 0.45m / 37m 0.22m, 깊은 바다 속도)로 출렁인다. 반경 `_SwellRadius`(1.2km)의 50~92%에서 0으로 잦아들고, 카메라가 해수면 위 `_SwellFadeHeight`(900m)의 60%→100%에서 잦아든다. 바다 평면 머티리얼은 Hole 역할로 같은 원 안을 버리고(clip), 원판은 원 밖을 버려 경계에서 정확히 맞물린다(가장자리는 평평해 이음매 없음). 너울 마루에 거품을 조금 더. [적용]이 원판·너울 머티리얼을 만들고 값을 복사(값은 바다 머티리얼에서 바꾸고 다시 적용), 끄기는 `ACO → Terrain → Remove Ocean Swell`. 콜라이더는 평평한 그대로(착수·함선 위치는 해수면 기준). 큐 Transparent-100 + 깊이 씀, 그림자 패스 없음(깊이 텍스처에 바다가 안 들어가게). 적용: Tools 창 "바다 셰이더" / `ACO → Terrain → Apply Ocean Material` — 물 레이어(4)의 가로 5km 이상 렌더러에 넣고 그림자 끔(콜라이더·레이어 그대로). 120km보다 작으면 키우라고 알린다.
- **조명 설정**(`ACO_LightingSetup`, Tools 창 "조명 설정 (한 번에)" / `ACO → Lighting → Set Up Lighting`, 굽기 = `Bake Lighting`): 어두웠던 원인 = 방향광이 Baked 모드(굽지 않으면 런타임에 생기는 지형·기체에 직사광이 없음) + 회색 환경광 + 얇은 대기. 태양: Mixed(직사광·그림자 실시간, 간접광만 굽기), (1, 0.95, 0.87) 1.25, 부드러운 그림자, 고도가 25~75° 밖이면 50°. 하늘: 절차적 스카이박스 `Generated/ACO_Sky.mat`(대기 0.95, 노출 1.25, 해 원반), 환경광 3색(하늘 0.58/0.68/0.82 · 지평 · 땅), 기본 반사 = 스카이박스 256, 안개 지수² 0.000028(공중 원근감). 굽기: `Generated/ACO_LightingSettings.lighting` — 실시간 GI 끔, Baked Indirect, GPU 라이트매퍼, 4텍셀/m, AO 1.5m, 2048. 항모(이름 HZI_CVN…): 렌더러를 Contribute GI·반사 프로브 정적으로, UV2 없는 모델은 Generate Lightmap UVs. 격납고: HangarPoint(없으면 BriefingPoint) 눈높이에서 여섯 방향 레이캐스트로 방 상자를 재고 `ACO_HangarLighting` = 천장 구운 점광원 약 10m 격자(최대 30, 따뜻한 흰빛 1.6) + 라이트 프로브 격자(바닥 0.5m·2m·천장 아래) + 상자 투영 구운 반사 프로브 — 실행 중 비용 없음, 기체·아바타는 프로브로 밝아진다. 포스트 프로세싱(Post Processing 패키지가 있을 때만 — `UNITY_POST_PROCESSING_STACK_V2`): 전역 볼륨 `ACO_PostProcess`(`Generated/ACO_PostProcess.asset`: 블룸 1.2/임계 1.1, ACES + 노출 +0.45, 색온도 +4, 채도·대비 +8) + 씬 디스크립터 레퍼런스 카메라에 PostProcessLayer(볼륨 레이어 Default, AA 없음 — VR MSAA).
- **크레딧 보드**(`ACO_CreditBoard`, 프리팹 `ACO_CreditBoard`, Tools 창 패널 "크레딧 보드"): 2.4 × 1.9m 판, 본문 = `Credits/ACO_Credits.txt`(TextAsset — 실행할 때 읽어 넣으므로 파일만 고치면 된다, TMP 서식 태그 가능, 기본 폰트라 영문). 본문 크기 20~34 자동. 셋업 검사가 없으면 알린다(정보).

- **언제**: 육각 지형이 다 세워지면(`ACO_HexTerrain.Features`) 이번 씨앗의 칸 높이로 자리를 골라 놓는다. 지형을 다시 만들기 시작하면 숨기고 자리를 비운다. 생성기의 지형 대기(`IsBuilt`)가 장식까지 기다린다.
- **동기화 없음**: 자리는 지형 씨앗의 정수 해시와 끊긴 칸 높이로만 정하므로 모든 클라이언트(늦게 들어와도)가 같은 자리에 같은 장식을 놓는다. 자리 번호도 같아서 페이즈 작전 지역은 번호(`_phaseSite`) 하나만 동기화한다.
- **칸 분석**: 칸마다 같은 높이(`FlatTolerance` 20m) 육지 이웃 수, 바다 이웃 수, 가장 높은 이웃과의 차(**덮임** — 더 높은 이웃 기둥의 경사 치마가 이 칸 위로 내려와 건물을 묻으므로 `MaxOverhang` 20m/마을·유적 `VillageMaxOverhang` 40m 넘으면 피한다), 봉우리 여부.
- **자리 고르기**(종류마다 점수 최고 칸을 하나씩, 프레임당 하나 — 점수 = 조건 + 씨앗 해시):

  | 종류 | 조건 | 놓는 것 |
  |---|---|---|
  | 도시 `CityCount`(2) | 해발 `LowlandMaxHeight`(300m) 이하, 같은 높이 이웃 3+, 해안이면 가산 | 가운데 + 같은 높이 이웃 최대 `CityMaxBlocks`(5)칸에 블록(도심/주거/공업 중 무작위, 60° 단위 회전). 블록 칸이 바다에 닿으면 그 변 바깥 경사가 해수면에 닿는 곳에 **항구** |
  | 비행장 `AirfieldCount`(1) | 정확히 같은 높이 칸 4개 일렬(약 2.8km 평지, 3방향 중) | 활주로 2.4km + 유도로/계류장/격납고/관제탑(시설 쪽은 해시로) |
  | 마을 `VillageCount`(8) | 해발 500m 이하, 같은 높이 이웃 1+, 바다 이웃 3 이하 | 집 10~16채 + 교회 + 밭 |
  | 등대 `LighthouseCount`(3) | 바다 이웃 3+(곶), 해발 200m 이하 | 바다 쪽 가장자리에 바다를 보고 |
  | 레이더/통신탑 `HighlandCount`(3) | 모든 이웃보다 높은 봉우리, 해발 `HighlandMinHeight`(350m)+ | 반반 |
  | 유적 `RuinsCount`(1) | 해발 250m+, 같은 높이 이웃 2+ | 돌기둥 고리 + 오벨리스크 |

  간격: 도시/마을/비행장끼리 `SettlementSpacing`(5km), 같은 종류 랜드마크끼리 `LandmarkSpacing`(4km), 그 밖 `MinSiteGap`(1.5km). 미리보기(씨앗 3 × 프리셋 8): 섬/고원/협곡은 도시 2, 마을 5~8, 비행장 0~1, 등대 3. 산맥은 평지가 거의 없어 마을이 0~1(황무지), 낮은 섬은 봉우리가 350m에 못 미쳐 레이더가 없을 수 있다.
- **이름**: 자리마다 두 음절 조합(예: KELHAVEN, TORVIK) — 비행장 "… AIRBASE", 항구 "… HARBOR", 레이더 "… RADAR STATION", 통신탑 "… RELAY TOWER", 등대 "… LIGHTHOUSE", 유적 "RUINS OF …". 음절(`NamePrefixes` 60개 × `NameSuffixes` 48개)과 꼬리말(`AirfieldLabel` 등)은 `ACO_TerrainFeatures` 인스펙터에서 바꾼다(기본 TMP 폰트라 영문 대문자). 목록을 바꾸면 같은 씨앗이라도 이름이 달라진다(저장 값과 무관). 목록이 비면 "SITE n".
- **충돌**: 모든 건물에 상자/구 콜라이더(레이어 Default — 지형 기둥과 같아 기체가 부딪히면 추락). 바닥판/밭/표지선 같은 납작한 것은 충돌 없음.
- **생성기 연동**:
  - `SampleGround`가 건물 자리(`IsBlocked`)를 버린다 — 지붕 위 배치, 콘보이 항로가 시가지 관통하는 것을 막는다. 전차 순찰 고리/매복 이동 직선도 건물 자리를 지나면 다른 자리를 찾는다(지면을 따르는 전차가 지붕에 올라타지 않게).
  - `SettlementPreference`(0.6) 확률로 지상 그룹/육상 고가치 표적/페이즈 목표 자리를 **주둔지**(도시/마을/비행장/레이더/통신탑 가장자리에서 `GarrisonDistanceMin~Max` 300~1500m)에서 고른다. 페이즈 목표는 거리 조건(`TargetDistanceMin~Max`)에 맞는 자리만.
  - 페이즈 목표 자리에서 `SiteNameRadius`(3km) 안에 장식이 있으면 페이즈 배너에 **"AO: 이름"**.
- **임시 모델**(`ACO → Create Prefabs...`의 "지형 장식"): 기본 상자/원기둥/구를 머티리얼별로 합쳐 프리팹 하나 = 메시 하나(드로우콜 = 머티리얼 수, 10종 머티리얼 GPU 인스턴싱), 화면 0.4% 아래면 LOD로 안 그린다. 원점 규칙만 지키면 직접 만든 모델로 바꿔 끼워도 된다(도시/마을/유적/레이더/통신탑 = 칸 윗면 가운데, 항구 = 해수면 해안선 +Z 바다, 비행장 = 활주로 가운데 Z축·시설 +X, 등대 = 바닥 +Z 바다).
- **비용**: 칸 분석 `CellsPerFrame`(300), 자리 고르기 프레임당 1곳(약 20프레임), 놓기 `ObjectsPerFrame`(8). 장식은 처음 한 번 `Instantiate`하고 다음 지형부터 다시 쓴다(최대 192개).

### 6-14. 재화 / 강화 / 특수무장 해금 ✅ (`ACO_PlayerProgress`, `ACO_VehicleUpgrades`, `ACO_Shop`, 로컬 전용 + 플레이어 영구 저장)

- **저장**: VRChat `PlayerData`(플레이어 × 월드, VRChat 서버 — 어느 인스턴스에서든 같은 값, 월드를 다시 올려도 남는다, 한도 약 100KB). **항목 번호가 아니라 고정 ID로 저장한다** — 강화 `aco_up_` + ID(`hull`, `mobility`, `engine`, `fuel`, `stability`, `msl_damage`… `bomb_blast`, 코드의 `_upgradeKeys`), 해금 `aco_unlock_` + 무장 ID(bool), 마지막 선택 `aco_special_id`(무장 ID), 크레딧 `aco_credits`, 형식 버전 `aco_ver`(=1). **규칙: 한 번 쓴 ID는 절대 바꾸거나 다른 항목에 재사용하지 않는다** — 새 항목은 아무 자리에나 새 ID로 넣고(순서·탭과 무관), 빼면 저장 값이 쓰이지 않고 남을 뿐이다. 효과 수치·가격·표시 이름은 언제든 바꿔도 된다(저장하는 건 산 단계뿐). ID 배열은 인스펙터에 두지 않는다(실수로 바뀌지 않게). `OnPlayerRestored` 전에는 아무것도 쓰지 않는다(빈 값으로 덮어쓰지 않게) — 그 사이 받은 보상은 불러온 뒤 더한다. 진행도는 씬에 하나(`ACO → Create Player Progress`).
- **보상**(미션이 끝나면 한 번, `RewardDelay` 2초 뒤): `(팀 점수 × TeamShare 0.5 + 개인 점수 × PersonalShare 1.0) × 보정`.
  - 개인 점수 = 내가 격추한 표적의 점수 — 작전판(`ACO_MissionLog`)이 `LastHitByPlayer`로 판정해 `AddPersonalScore`로 넘긴다(작전판이 없으면 개인 점수 0).
  - 보정: 격추당할 때마다 −`DeathPenalty`(45%). **미션 실패** 또는 **`MinimumRewardDeaths`(2)번 이상 격추**면 최소 보상 `MinimumRewardFraction`(10%). 격추 = 조종 중 기체 폭발(추락 포함, `SFEXT_G_Explode` + 내가 조종사).
  - 이번 미션에 한 번도 출격(조종석 탑승)하지 않았으면 0(`RequireSortie`). 늦게 들어와도 끝나기 전에 출격하면 받는다(팀 점수 전체, 개인 점수는 들어온 뒤 것). 미션 도중 나가면 그 미션 보상은 없다.
  - **안전지대 밖 탈출 = 격추**: 미션 중 스스로 조종석을 내렸을 때(폭발로 내려진 경우는 빼고) 기체 위치가 진행도 `SafeZones` 콜라이더의 바운딩 박스(축에 맞춘 상자) 밖이면 격추 1회로 센다 — 추락 직전 탈출로 페널티를 피하지 못하게. VRChat 메뉴 리스폰으로 좌석을 벗어나도 같다. 비우면 페널티 없음(Bake 경고). 미션이 끝난 뒤 강제로 내리는 건 세지 않는다.
  - 디브리핑(작전판 머리줄)과 격납고 상점에 "REWARD +N CR (TEAM a + YOU b) x55% SHOT DOWN 1"을 띄운다.
- **단계식 강화**(항목마다 0~5단계, 효과 = 1 + `UpgradeSteps` × 단계, n단계 가격 = `UpgradeBaseCosts` × `LevelCostScale[n-1]`(1, 2, 3.5, 5.5, 8)). 상점 탭(분류 `CAT_*`) 5개, 항목 27개(상점 줄은 가장 긴 탭 7줄):

  | 분류(탭) | 항목 (단계당) | 1단계 가격 | 적용 대상 |
  |---|---|---|---|
  | AIRFRAME | 체력 +10% / 기동 +5% / 추력 +4% / 연료 효율 +8% / 안정성 +6% | 2,000 / 2,000 / 2,000 / 1,500 / 1,500 | `SaccAirVehicle.FullHealth`(남은 체력도 같은 비율) / `Pitch·Yaw·RollStrength` / `ThrottleStrength`(+`ThrottleStrengthAB`) / `FuelConsumption` ÷ 배수(+`FuelConsumptionAB`, 5단계 ×1.4 → 약 29% 덜 씀) / 양력: `Lift` × 배수 + `ExtraVelLift`에 (배수 − 1)을 더함(플랩이 같은 값을 더하고 빼므로 곱하지 않고 더한 만큼만 되돌린다) — 저속에서 쳐들어도 버티고 기수가 덜 처진다 |
  | MISSILE | 피해 +8% / 탄수 +15% / 선회 +6% / 락온 속도 +10% / 락온 거리 +6% / 락온 범위 +6% / **재장전 속도 +8%** | 2,000 / 1,500… / **3,000** | 일반 미사일(특수무장 슬롯 밖) |
  | SP. AAM | 위와 같은 7개 | 2,500 / 2,000… / **3,500** | 특수 대공 미사일(4AAM 등) |
  | SP. AGM | 피해 +8% / 탄수 +15% / 폭발 반경 +6% / **재장전 속도 +8%** (유도 성능은 강화 없음) | 2,500 / 2,000 / 2,000 / **3,500** | 특수 대지 미사일(AAM·4AAM 모듈의 표적만 지상으로 바꾼 것, `MissileSplash` 켬) |
  | SP. BOMB | 피해 +8% / 탄수 +15% / 폭발 반경 +6% / **재장전 속도 +8%** | 2,500 / 2,000 / 2,000 / **3,500** | 특수 폭탄·로켓(UGB, RKT) |

  - 대지 미사일 범위 피해: `ACO_DFUNC_Missile.MissileSplash`를 켜면 유도 미사일(`ACO_Missile`)도 터진 자리(명중·지형 충돌)에 `SplashRadius` 범위 피해를 준다 — 폭탄/로켓과 같은 규칙(SaccTarget마다 가장 가까운 콜라이더 거리로 한 번, 중심 `Damage` → 가장자리 `MinSplashFraction`, 쏜 기체·호위 대상 제외, 정통으로 맞은 표적은 중복 없음, 레이어 `ACO_Missile.SplashLayers` 17). 쏜 사람 화면에서만 판정한다. 대지 계열인데 꺼져 있으면 Bake가 경고한다.
  - 적용 수치: 피해 `Damage`, 탄수 `MaxAmmo`, 선회 `TurnRate`, 락온 속도 = `LockTime ÷ 배수`, 락온 거리 `LockRange`, 락온 범위 `LockAngle`, 폭발 반경 `SplashRadius`, 재장전 = `SlotReloadTime ÷ 배수`(5단계 ×1.4 → 장전 시간 약 29% 짧게, 장전 중인 슬롯은 다음 장전부터). 저장 ID `msl_reload`·`aam_reload`·`agm_reload`·`bomb_reload`. 항목이 늘면 씬 진행도의 이름·배수·가격 배열을 Bake가 저장 ID 기준으로 새 배치에 옮긴다(직접 바꾼 값 유지, `MigrateUpgradeArrays`).
  - 무장 계열: 무장 모듈 인스펙터의 `ACO_DFUNC_Missile.UpgradeFamily` 드롭다운(enum `ACO_UpgradeFamily`: Missile / SpecialAAM / SpecialAGM / SpecialBomb / None, 기본 Missile)으로 **직접 고른다** — Bake는 레이어를 보지 않는다. None이면 강화를 받지 않는다. 셋업 메뉴의 4AAM 프리셋은 SpecialAAM, UGB/RKT 프리셋은 SpecialBomb을 넣는다. SpecialAGM인데 `MissileSplash`가 꺼져 있으면 Bake가 경고한다.
  - 적용(`ACO_VehicleUpgrades`, 기체의 `ExtensionUdonBehaviours`): 탑승할 때 내 강화로, 내릴 때 원래대로 — **조종사 화면에서만** 바꾼다(비행·SAV 체력은 오너 계산, 미사일 비행 성능은 발사 이벤트에 실림, 피해는 쏜 사람 값). 다음 탑승부터 적용.
  - 미사일 잔탄은 늘어난 총 탄수만큼만 더하고 뺀다(`ACO_DFUNC_Missile.ApplyUpgrades`) — 다시 채우지 않는다. 못 뺀 만큼은 빚으로 남겨 다음 탑승 때 먼저 갚는다(다 쏘고 내렸다 타서 공짜로 채우지 못하게). 폭발/리스폰의 `ResetAmmo`가 빚을 지운다.
  - SAV 무장(DFUNC_Bomb 등)과 기관포는 강화하지 않는다(기관포 탄수는 SAV 내부 private 값이 얽혀 있다).
- **특수무장 해금**: 격납고 `ACO_Loadout.SpecialPrices`(**4AAM 6,000**, UGB 8,000, RKT 8,000 — 모두 잠긴 채 시작). 잠긴 무장을 누르면 설명·가격만 보이고 BUY 버튼으로 사면 바로 장착. 마지막으로 고른 무장은 저장된다. 해금한 무장이 없으면 **특수무장 없이 출격**(슬롯 비움, `NetEquip(255)`). 고른 무장이 기체에 없으면 해금된 첫 모듈을 쓴다.
- **격납고 패널**(`ACO_Hangar` 2.8 × 1.5m): 위 = 크레딧·지난 보상, 왼쪽 = 특수무장(선택/구매), 오른쪽 = 분류 탭 5개 + 강화 줄 6칸(`이름 [##---] +20% 4,000 CR`, 살 수 있으면 녹색·모자라면 회색·최대면 밝은 녹색, 남는 줄은 숨김).
- **아이콘**(HUD 라인 — 흰 선 + 알파, Image 색으로 물든다): `Textures/Icons/aco_icon_*.png` — 기체 강화 5(hull, mobility, engine, fuel, stability), 능력치 8(damage, ammo, turn, lock_speed, lock_range, lock_angle, blast, reload — 미사일/특수 계열이 같이 쓴다), 탭 5(tab_*), 특수무장 4(weapon_4aam/ugb/rkt/none), lock, credits. 프리팹 빌더가 Sprite(밉맵, 256)로 가져오게 설정해 붙인다: 강화 줄 왼쪽(`ACO_Shop.RowIcons` + 강화 ID별 `UpgradeIcons` — 탭에 따라 바뀜), 탭 왼쪽, 특수무장 버튼 위(ID로 고름, 모르면 미사일 그림) + 잠긴 무장 구석 자물쇠(`ACO_Loadout.LockIcons`), 크레딧 옆. 다시 그리려면 `Tools/aco_icons.py <출력 폴더> <미리보기.png>`(표준 파이썬만) 후 격납고 프리팹을 다시 만든다.
- Bake가 진행도를 관리자·격납고·상점·작전판·기체 강화 확장에 연결하고, 강화 확장의 일반 미사일/특수무장/SaccAirVehicle을 굽고, `ExtensionUdonBehaviours`에 없으면 넣는다. 확장이 없는 플레이어 기체는 경고한다.
- ⚠ 저장 값은 각자 컴퓨터(클라이언트)에서 계산한다 — PvE라 조작 방지는 하지 않는다.

### 6-15. 게임 진행 — 참가 명단 / 출격 준비 / 일제 출격 ✅ (`ACO_GameFlow`, `ACO_FlowButton`, `ACO_Teleporter`, `ACO → Create Game Flow`)

- **장소**: 대기실(브리핑 + 격납고 한 방, VRChat 스폰 — 작전 콘솔·격납고 패널·작전 테이블을 함께 둔다) → 필드(항모). 이동은 자유 — `ACO_Teleporter` 버튼(UI 또는 콜라이더)으로 언제든 오간다. 격납고를 따로 두고 싶으면 `HangarPoint`를 넣으면 된다(예전 방식).
- **참가 명단**(수동 동기화, 칸 = 기체 `Aircraft[k]`, 최대 8, 기본 4): 로비 콘솔의 JOIN/LEAVE. 명단 밖은 관전자. 미션 도중에도 빈 칸이면 JOIN(난입). **미션이 끝나면 명단·준비를 비운다.** 나간 플레이어는 지운다. 요청은 `NetworkCalling.CallingPlayer`로 오너가 처리.
- **시작**: 로비 콘솔 페이즈 버튼은 다음 미션의 페이즈 수만 고른다(`ACO_MissionStartButton.Flow`, 게임 진행이 없으면 예전처럼 바로 시작). 무장·강화 후 READY → **명단 전원이 READY면 `AllReadyDelay`(10초) 출격 카운트다운**(`_launchEndMs`, 동기화) — 그동안 리롤·페이즈 변경을 잠그고(버튼은 안내 메시지), 누가 READY를 풀거나 새로 들어오면(전원 준비가 깨지면) 취소 → 끝나면 오너가 미션을 시작하고 `NetLaunchAll`로 일제 출격.
- **출격**(각자 자기 화면): 내 칸 기체의 오너 → SAV가 오너를 알아채면(`EntityControl.IsOwner`) 갑판의 내 칸 위치(`DeckPoints[k]`)로 순간이동(`ShouldTeleport`, `SetNoGsFor`, `SFEXT_O_VehicleTeleported` — SAV `SaccVehicleTeleporter`와 같은 방식) → 조종석(`SaccVehicleSeat.Interact`). 격추되면 SAV가 원래 위치(= 보관 장소)로 리스폰 → 격납고의 LAUNCH로 재출격(RESPAWNING 중엔 대기).
- **지형 리롤**: 브리핑(미션 전)에 명단에 있는 사람이 로비 콘솔의 REROLL TERRAIN으로 지형을 새로 만든다(`RerollCooldown` 30초 — 지형 + 병력 미리 배치라 무겁다, 리롤하면 READY가 풀린다). 명단 제목에 지형 프리셋(`TerrainNames`, 세우는 중이면 GENERATING). 미션은 **지금 지형 그대로** 시작한다 — 게임 진행이 시작할 때 생성기의 `RegenerateTerrainEachMission`을 끄고, 생성기는 지형이 다 세워질 때까지 기다렸다 배치한다. 미션이 끝나면 참가자 기체가 모두 내린 뒤 새 지형(`NewTerrainAfterMission`, 비행 중에 섬이 바뀌지 않게).
- **보관**: 참가자 기체의 원래 위치는 안 보이는 보관 장소. 내려서 비어 있는 기체가 보관 장소 밖에 `IdleReturnTime`(30초) 있으면 그 기체의 오너가 `MoveToSpawn`.
- **관전자 탑승 차단**: 참가자 기체의 파일럿 좌석(`SaccVehicleSeat.IsPilotSeat`)은 시작할 때 `DisableInteractive` — 눌러서는 못 타고 LAUNCH(좌석 `Interact()`를 코드로 직접 부름)로만 탄다. 비행 중 명단이 비워져도 내리지 않는다.
- **패널**: 로비 콘솔(`ACO_MissionConsole` 2.2 × 1.5m) = 페이즈 버튼 + 명단 + JOIN/LEAVE + REROLL TERRAIN. 격납고(`ACO_Hangar` 2.8 × 1.9m) 아래 = 명단·준비 + READY + LAUNCH. 명단 글자(`ACO_FlowStatus`)와 버튼 글자는 Bake가 게임 진행에 연결.
- **화면 암전**(`ACO_ScreenFader`, `ACO → Create Screen Fader`, 씬에 하나): 머리를 감싸는 검은 구(`ACO/MarkerOverlay`, 렌더 큐 4900 — 마커·HUD보다 위)의 알파를 0.3초 올리고 → 순간이동 → 0.4초 유지 → 0.5초 내린다. `ACO_Teleporter`, 관전, 브리핑실 귀환, 출격(탑승 전 어두워지고 오너 대기 중엔 유지)이 거친다.
- **귀환**: 미션이 끝나면(성공/실패) `ReturnDelay`(8초) 뒤 모든 클라이언트가 각자 브리핑실(`BriefingPoint`)로 — 참가자 기체에 타고 있으면 먼저 내린다(좌석에선 순간이동이 안 된다).
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

### 6-18. 셋업 검사 창 ✅ (`ACO_SetupValidator`, `ACO → Setup Check...`)

- 씬을 훑어 빠진 셋업을 오류(동작 안 함)/경고(일부 기능 꺼짐)/정보(선택 기능)로 보여 준다. [선택]은 그 오브젝트를 고르고, [고치기]는 기존 메뉴(Create ..., 프리팹 다시 만들기·씬에 놓기, Add Upgrade Extension, ExtensionUdonBehaviours 등록, 빈 지점 만들기, MissionSplash 켜기 등)를 부른 뒤 Bake + 다시 검사. [자동 수정 모두]는 하나씩 고치고 다시 검사(고쳐지지 않으면 멈춤). 창에 포커스가 오면 다시 검사.
- 검사 항목: 관리자·생성기(풀)·디렉터·진행도(안전지대)·화면 암전 / 게임 진행(기체, 갑판 위치 수, Briefing·Hangar·Spectate 지점, 기체 원래 위치가 갑판 근처인지, 장소 이동 버튼 Target) / HUD(크로스헤어)·작전 콘솔·격납고(옛 버전 여부)·작전판·작전 테이블(HomeBase)·HUD 설정 패널 / 플레이어 기체(ACO 미사일, 특수무장 슬롯, 강화 확장과 등록, 탄체·TargetLayers·AGM MissileSplash).

### 6-19. 무전 자막 ✅ (`ACO_Radio`, `ACO → Create Radio (Subtitles)`, 로컬 전용, 네트워크 없음)

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

### 6-13. 페이즈 음성 ✅ (`ACO_MissionVoice`, `ACO → Create Mission Voice`)

- 로컬 전용(동기화 없음): 관리자의 동기화 상태(페이즈 번호/클리어 수/유형/실패 여부/미션 상태)를 `PollInterval`(0.25초)마다 읽어 바뀐 순간에 튼다. 처음 본 상태(늦게 들어온 클라이언트)는 기준만 잡는다.
- 클립: 유형별 `StartClips`/`SuccessClips`/`FailClips`(칸 순서 = 1 타격, 2 SEAD, 3 점수 할당, 4 시한 표적, 5 제공권, 6 보스, 7 공중 호위, 8 지상 호위), 비면 공통 `DefaultStart/Success/Fail`, 미션 결과 `MissionComplete/MissionFailed`(선택).
- 언제: 페이즈 시작(배치 실패로 유형이 바뀌면 새 유형의 시작만), 페이즈 끝 — 실패(`GetLastPhaseFailed`, 호위 대상 격추/콘보이 전멸/시한 표적 도주)면 실패, 아니면 성공(시작 때 본 유형 기준). 마지막 페이즈 성공 뒤 `MissionComplete`, 제한 시간 실패면 `MissionFailed`.
- 무전 효과(선택): `PttStart`(PTT 누름 소리)를 먼저 틀고 `PttLead`(음수 = 그 소리 길이) 뒤에 음성, 음성이 끝나면 `PttEnd`(PTT 놓음 소리). 크기 `PttVolume`(음성 Volume에 곱함).
- 겹치면 줄을 세워 앞 음성이 끝나고 `Gap`(0.3초) 뒤 차례로, `MaxQueueDelay`(10초)보다 밀린 음성은 버린다. AudioSource는 2D(무전처럼 어디서나 같은 크기) — 메뉴가 만든다, Bake가 관리자/AudioSource를 연결.

### 6-20. RWR — 위협 방향/거리 링 + 경보음 ✅ (`ACO_RWR`, Bake가 표적 마커 아래에 만든다, 로컬 전용, 네트워크 없음)
- **미사일**: 크로스헤어 둘레에 오는 방향으로 쉐브론(꼭짓점이 가운데). 방향 = 머리(시선) 기준 수평 방위(위 = 보는 쪽, 아래 = 뒤). 거리 = 반경 — `MaxRange`(10km) 이상 바깥 반경 `OuterAngle`, `CloseRange`(300m) 이하 안쪽 반경 `InnerAngle`(크로스헤어 바로 바깥), 사이는 로그 눈금(가까울수록 빨리 다가온다). 가까울수록 빨갛고(`UseColor`) `CloseScale`배까지 커지며, `BlinkRange`(1.5km) 안이면 경보음 박자에 맞춰 깜빡인다. 가까운 순으로 `MaxShown`(4)발.
- **락온**: 락온 중인 적 방향 바깥(`LockArcAngle`)에 얇은 호. 런처 경보(1초 간격)가 끊기고 `LockHoldTime` 뒤 사라진다. 새 락온이면 그 방향에서 락온음 한 번(SAV 기체 자체 락온음은 따로 난다).
- **경보음**: 가장 가까운 미사일 방향(머리에서 1m)에서 3D로. 간격 = 멀면 `MaxBeepInterval`(1초) → 가까우면 `MinBeepInterval`(0.1초). 클립은 Generated의 사인파 WAV(AudioSource 클립만 바꾸면 된다).
- **데이터**: 미사일은 모든 클라이언트가 로컬로 재현하므로 위치를 그대로 읽는다. `ACO_Missile`이 경고를 켤 때(표적 기체에 이 클라이언트가 탐) `AddMissile`, 끌 때 `RemoveMissile`. `ACO_MissileLauncher.LockWarningEvent`가 피해자 클라이언트에서 `ReportLock`. 터짐·회수·하차는 매 프레임 정리(안전장치).
- **비용**: 위협이 없으면 PostLateUpdate가 바로 돌아간다. 있을 때도 최대 8발 + 락온 6개의 거리·각도 계산과 Transform 몇 개 — 표적 마커(64개)보다 훨씬 가볍다. 쉐브론 머티리얼은 시작할 때 8개만 복제한다.
- **HUD 설정**: `RWR`(켜기/끄기 — 링과 경보음 모두), `RWR SIZE`(링 크기 배율 0.5~2). 저장 키 `aco_hud_rwr`, `aco_hud_rwr_size`. 항목이 늘어 HUD 설정 패널 프리팹을 다시 만들어야 새 줄이 보인다(예전 프리팹도 오류 없이 동작, 새 항목만 안 보임).
- 연결: Bake(`BakeRwr`)가 씬의 RWR을 모든 `ACO_MissileLauncher`·`ACO_DFUNC_Missile`·`ACO_HudSettings`에 넣는다.
- 나중에: 적외선 미사일 늦게 감지(MAWS 거리), RWR 성능 강화(감지 거리·오차) — 미사일 종류는 이미 알고 있어 `AddMissile`에 넘기기만 하면 된다.

### 6-21. 좌석 눈높이 재조정 ✅ (`ACO_SeatRecenter`, SAV 확장, 로컬 전용)
- VR에서 탑승 중 **점프 버튼 한 번 = 좌석 재조정**, **두 번(0.3초 안) = 내리기**(SAV `SaccEntity.DoubleTapToExit`). 한 번 누른 뒤 0.3초 동안 두 번째 입력이 없을 때만 재조정한다.
- 재조정 = SAV `SaccVehicleSeat`가 탈 때 하는 보정 그대로(높이·앞뒤를 `TargetEyePosition`에, VR이면 방향을 `RotationCalibrationTarget`에, 1~2초). SAV 파일은 그대로 — private `CalibratedY/Z/Yaw`, `AdjustTime`을 `SetProgramVariable`로 되돌리고 `SeatAdjustment()`를 부른다. 탄 직후 보정이 아직 돌고 있으면 겹치지 않게 무시(안내만). SAV가 변수 이름을 바꾸면 경고 한 번 남기고 아무것도 안 한다.
- 안내: 점수 HUD의 `NoticeText`(`ACO_ScoreHUD.ShowNotice`, 미션 상태와 상관없이 2.5초) — "SEAT RECENTERING / SIT UPRIGHT AND LOOK STRAIGHT AHEAD", 보정 중이면 "SEAT ADJUSTING...".
- 셋업: Bake가 강화 확장이 있는 기체(플레이어 기체)마다 확장을 넣어 `ExtensionUdonBehaviours`에 등록하고 `DoubleTapToExit`를 켠다, 점수 HUD에 안내 글자가 없으면 만들고 연결한다. 셋업 검사에도 항목. SAV 메뉴의 Double Tap To Exit 토글을 연결해 뒀다면 플레이어가 끌 수 있다(끄면 점프 한 번에 내린다 — 재조정은 동작 안 함).
- 에디터(ClientSim)에서는 SAV 좌석 보정 자체가 돌지 않는다 — VRChat VR에서 확인.

### 6-22. 특수무장 카탈로그 / 격납고 페이지 ✅ (`ACO_SpecialWeaponDef`, `ACO_SpecialWeaponCatalog`, `ACO → Special Weapons`)
- **무장 정의 = 프리팹 하나**(`Assets/ACO_Addon/SpecialWeapons/ACO_Special_<ID>.prefab`): 루트 `ACO_SpecialWeaponDef`(Id·이름·설명·가격·아이콘·목록 순서) + 자식 `Module`(`ACO_DFUNC_Missile` — 표적 레이어·다중 락온·슬롯·탄수·장전·락온 거리/각도/시간·피해·속도·선회·근접 신관·범위 피해·강화 계열 등 성능 전부). `Missile`을 비우면 그 기체의 일반 미사일 모델을 쓴다(폭탄·로켓은 ACO_Bomb/ACO_Rocket). 정의는 일반 MonoBehaviour라 **씬에 두지 않는다**(셋업 검사가 알린다).
- **예제 10종**(프리팹 생성 창 → **플레이어 특수무장** — 폭탄·로켓 탄체와 함께. 기체 셋업은 카탈로그가 비어 있으면 없는 것만 만든다): 4AAM(대공 4표적) 6,000 / UGB(폭탄, CCIP) 8,000 / RKT(로켓) 8,000 / 4AGM(대지 4표적, 범위 20m) 9,000 / LAGM(대지 8km, 범위 30m) 10,000 / SASM(Short-range Aerial Suppression Missile — 근거리 대공 2.5km, 근접 신관 45m + 범위 피해 50m로 편대 옆 기체까지, 시커 25°·락온 0.8초) 10,000 / LAAM(대공 9km, 레이더) 12,000 / QAAM(시커 40°, 선회 260°/s) 15,000 / HVAA(800m/s, 레이더) 13,000 / HPAA(피해 260, 근접 신관 20m) 14,000. 대지는 특수 대지 탭, 대공은 특수 대공 탭 강화를 받는다. 미사일 풀은 2발만 미리 만든다(모자라면 늘어난다).
- **설치**: ① 격납고(`ACO_Loadout`) 목록 — **Bake마다** 없는 ID는 목록 순서로 끝에 더하고, 이름·설명·가격·아이콘은 정의 값으로 맞춘다(바뀐 게 없으면 기록 안 함). ② 기체 모듈 — **기체 셋업**(고른 기체)과 셋업 검사 [고치기](씬 전체)가 슬롯에 같은 ID 모듈이 없으면 정의 모듈을 복제해 넣는다(있으면 건너뜀 — 기체별로 고친 값 보존. 정의를 고친 뒤 다시 반영하려면 그 기체 모듈을 지우고 기체 셋업. 같은 ID가 예전 SAV 폭탄/로켓 모듈이면 ACO 모듈로 바꾼다). 기체에 묶인 칸은 설치가 채운다: SAVControl·HUD·조준 기준·발사 키·장애물 레이어는 일반 미사일에서, 발사 지점·발사음은 유도탄이면 일반 미사일·폭탄/로켓이면 슬롯의 기존 폭탄/로켓 모듈에서, 락온음은 유도탄만, 잔탄 글자는 슬롯의 다른 모듈과 공유, HUD 슬롯 막대를 만든다. ③ Bake(표적·강화·RWR 등).
- **새 무장 만들기**: 예제 프리팹 복제(Ctrl+D) → 루트 Id(**한 번 정하면 바꾸지 않는다** — 해금 저장 키 `aco_unlock_<Id>`)·이름·설명·가격·아이콘 → 자식 Module 성능 → 셋업 검사 [고치기] 또는 기체 셋업. 아이콘은 `Textures/Icons/aco_icon_weapon_<id 소문자>.png`가 있으면 자동(정의 Icon이 우선).
- **격납고 페이지**: 왼쪽 특수무장 = 한 페이지 2줄 × 4칸 + `< PREV` / `PAGE n / m` / `NEXT >`(무장이 8개를 넘을 때만 보인다, 끝에서 넘기면 돈다). 칸의 이름·아이콘·자물쇠·색은 `ACO_Loadout`이 페이지에 맞게 바꾼다(`ButtonLabels/ButtonIcons/LockIcons`, 아이콘은 `SpecialIcons`). 칸 버튼의 Index = 페이지 안 칸 번호(`ACO_HangarButton` 4 = 페이지 넘김). 장착·불러오기 때 그 무장이 있는 페이지로 간다.

### 6-23. 패널 버튼 상태 표시 / 효과음 ✅ (`ACO_UiSound`, Bake가 씬에 하나 만들고 연결, 로컬 전용)
- **회색 = 지금 누를 수 없음**(그래도 누르면 이유를 안내하고 거절음). 게임 진행이 0.5초마다 `ACO_FlowButton.SetState(글자, 가능)`·`ACO_MissionStartButton.SetState(가능, 선택)`로 칠한다(`Buttons`/`PhaseButtons`는 Bake가 모은다, 배경 Image도 채운다).
  - JOIN/LEAVE: 비행 중 LEAVE 불가, 명단이 차면 `ROSTER FULL`. READY: 명단·미션 전·모두 착륙·브리핑 준비 완료일 때(준비 중이면 `PREPARING...`, 준비했으면 `CANCEL READY`). LAUNCH: 미션 중 내 기체가 쉬고 있을 때(`IN FLIGHT`/`RESPAWNING`/`IN USE`), 명단 밖이면 `SPECTATE`.
  - **REROLL: 쿨타임 중엔 `REROLL (23s)`처럼 남은 시간**, 지형을 세우는 중 `GENERATING...`, 출격 카운트다운 중 `TERRAIN LOCKED`.
  - 페이즈 버튼: 고른 수는 밝은 녹색, 미션 중·시작 중·출격 카운트다운 중엔 회색.
  - 격납고: 크레딧이 모자라면 BUY 버튼 회색(이미 있던 강화 줄 회색·잠긴 무장 색과 같은 규칙).
- **효과음**(누른 버튼 자리에서 3D): CLICK = 받아들인 일반 조작(탭·페이지·잠긴 무장 보기·값 조정·리롤·페이즈·장소 이동), CONFIRM = 결정(JOIN/LEAVE·READY·LAUNCH·구매·장착·HUD RESET), DENY = 거절(조건이 안 맞아 안내만 뜸, 크레딧 부족, 최대 단계, 값이 끝에 닿음). 클립은 Generated/`ACO_UiClick`·`ACO_UiConfirm`·`ACO_UiDeny`.wav(사인파 — 클립만 바꿔 끼우면 된다). 결과는 각 동작이 돌려준다(`ACO_GameFlow.ButtonAction/RequestPhases` → bool, `ACO_Loadout.Select/BuyPreview/ChangePage`·`ACO_Shop.BuyRow` → 효과음 종류, `ACO_HudSettings.Adjust` → 바뀌었나).

### 6-24. 슈퍼에이스 / 보너스 페이즈 ✅ (`ACO_SuperAce`, 프리팹 `NPC_SuperAce`, 미션 관리자 `PHASE_BONUS`, 생성기 `OBJ_SUPER_ACE`)
- **보너스 페이즈**: 마지막 페이즈를 클리어하면 `BonusPhaseChance`(30%) 확률로 한 페이즈가 더 붙는다(`BonusMinPhases` 2페이즈 이상 미션, 풀에 쉬는 슈퍼에이스가 있을 때, 미션당 한 번 굴림). 슈퍼에이스 1기(목표 태그) + 호위 에이스 `BonusEscorts`(2)기(목표 아님). 배율 `BonusScoreMultiplier`(×2.5), 격추하면 `BonusClearBonus`(15,000) × 배율. `BonusTimeLimit`(240초) 안에 못 잡으면 슈퍼에이스가 물러나고(태그 회수) 보너스만 없다 — 본 미션은 이미 클리어라 실패하지 않는다(미션 제한 시간도 보지 않는다, HUD 시간은 보너스 남은 시간). 랭크 기준(배율 합)과 시간 보너스는 본 미션 기준(보너스 페이즈 제외, 본 미션 클리어 시각 `_mainClearMs`). 배너 `BONUS - <이름>`, 페이즈 수가 하나 늘어난다(PHASE 4/4). 페이즈 시작 웨이브·교착 웨이브 없음.
- **생성기**: 슈퍼에이스(`ACO_NPCFighterAI.SuperAce`가 있는 전투기)는 일반 배치(전체 배치·증원·목표 편대·호위 요격)에 쓰지 않는다. `OBJ_SUPER_ACE`만 쉬는(또는 격추된) 슈퍼에이스를 내보낸다. 동기화·부활·격추 판정은 일반 전투기와 같다.
- **슈퍼에이스**(`ACO_SuperAce`, 기체 자식 — AI·비행·동기화는 일반 NPC 그대로, 체력 6배·플레어 12번·에이스 등급):
  - **다연장**: `SalvoCooldown`(25초, 첫 발은 등장 12초 뒤)마다 `SalvoRange`(5km) 안 플레이어를 가까운 순으로 `SalvoMaxTargets`(4)명 골라 `SalvoWarnTime`(1.5초) 동안 락온 경보(0.5초마다, RWR 호도 뜬다) → 모두에게 동시에 발사(전용 런처 `SalvoLauncher` — 연사 간격 0, 8발, 다 쓰면 40초 재장전). AI 오너만 판단, 발사는 런처가 모두에게 알린다(`WarnTarget`·`FireAt`).
  - **기만체**: 나를 노린 플레이어 미사일이 `DecoyReactDistance`(2.5km) 안이면(`DecoyCooldown` 18초, 출격당 `DecoyCharges` 4번) 가짜 표적 3개를 뿌린다(`DecoyLifetime` 12초). 기만체 = 플레이어 락온 레이어(29)의 빈 오브젝트, 처음 켜질 때 기체에서 떼어 낸다. **편대 비행**: 기체 기준 자리(`DecoyOffsets` — 좌우 180m, 위·뒤 220m)에 붙어 기체의 기동을 그대로 따라 난다(`DecoySpreadTime` 1.2초 동안 기체 자리에서 벌어지고, `DecoySway` 25m만큼 자리마다 다른 박자로 흔들린다). 각 클라이언트가 자기 화면의 기체 위치로 계산하므로 위치 동기화가 없다. HUD 마커는 진짜와 똑같다(이름·목표 색·체력 바 — 같은 유닛으로 등록). 락온해 쏜 미사일은 기만체 근처에서 터지고 피해는 없다(기만체엔 SaccTarget이 없다). 기체가 거둬지면(비활성) 기만체도 사라진다.
  - **무전**: 등장·다연장·기만체·체력 절반·격추 때 자막(`ACO_Radio.Say` — 대사 표 밖, 확률·쿨타임 없이, 이름 색 `SpeakerColor`). 대사는 인스펙터(`LineSpawn` 등).
  - HUD 마커 이름 = `AceName`(등급 ACE 대신).
- **셋업**: 프리팹 생성 창 → **슈퍼에이스** → `NPC_SuperAce`를 생성기 풀(UnitPoolRoot) 아래에 하나 둔다(셋업 검사 [고치기]가 놓아 준다). Bake가 AI ↔ 슈퍼에이스, 무전을 연결하고 풀에 없으면 경고. 모델은 빨간 상자 — 직접 만든 모델로 바꿔 끼운다(`HideOnDeath`도 함께).
- 나중에: 종류 추가(ECM — 재밍·락 끊기·미사일 교란, 장거리·고기동 미사일), 체력 단계별 행동(이탈 후 복귀), 보너스 등장 조건 다양화.

### 6-25. 폭격기 요격 페이즈 ✅ (`PHASE_INTERCEPT`, 프리팹 `NPC_Bomber`, `ACO_EscortUnit.Hostile`)
- **흐름**: 호위 페이즈를 뒤집은 것. 적 폭격기 `InterceptBombersBase`(3) + `InterceptBombersPerPhase`(0.5) × 페이즈 번호 대가 **삼각 대형**으로 도시 상공을 향해 날아온다(선두 뒤로 줄마다 좌우 한 대씩 — 생성기 `BomberFormationBack` 90m 뒤, `BomberFormationSide` 80m 옆; 유닛별 동기화 `_escortLateral`, `ACO_EscortUnit`이 진행 방향 오른쪽으로 그만큼 비켜 난다. 콘보이는 그대로 한 줄)(항로 `InterceptRouteLength` 18km, 폭격기 110m/s → 약 2분 40초). 처음 폭격기가 보이면 선두 둘레에 적 호위 전투기 `InterceptEscortsBase`(2) + `InterceptEscortsPerPhase`(1) × 페이즈 번호 기(목표 아님). **전부 격추하면 클리어**(페이즈 클리어 보너스), **한 대라도 도시 상공(항로 끝)에 닿아 폭탄을 떨어뜨리면 그 즉시 미션 실패**(`GetFailReason` = `FAIL_CITY_BOMBED` → HUD "THE CITY HAS BEEN BOMBED", 디브리핑 "(CITY BOMBED)"). 실패 사유 글자는 `ACO_MissionManager.GetFailReasonText`가 정한다. **실패 조건이 있는 페이즈(호위·지상 호위·시한 표적·요격)는 모두 실패하면 미션 자체가 실패한다** — 보너스 없이 넘어가는 실패는 보너스 페이즈 시간 초과(슈퍼에이스가 물러남, 미션은 이미 클리어)뿐.
- **폭격기** = 호위 대상과 같은 항로 비행 유닛(`ACO_EscortUnit`)에 `Hostile` 켬: 락온 마커는 적 항공기 레이어(29), 기총 막기 없음, 플레이어 범위 피해를 받는다(아군 호위 대상만 범위 피해에서 빠진다). 격추 점수 `BomberKillScore`(400) × 배율, 전투기 격추 수에 센다. HUD 마커 "BMR"(목표 = 빨강), 작전 테이블은 적 항공기 색, 무전의 호위 대사 대상 아님.
- **항로**: 생성기 `RequestDeployEscort`에 `MOVE_AIR | ESCORT_HOSTILE`(네트워크 인자 8개 제한이라 이동 방식에 표시를 더한다) → 적 폭격기만 고르고, 목적지는 **도시만**(늘 도시로 — 없으면 공역 아무 곳), 비행장 착륙·저공 투하 없음(순항 고도에서 투하). 아군 호위는 이제 적 폭격기를 고르지 않는다(`CountFreeEscorts`도 구분). 사전 확인(`CanBuildEscortRoute`)도 같은 표시로 같은 항로.
- **HUD**: 페이즈 줄 "INTERCEPT  BMR 남은/전체  CITY 6.3km"(5km 아래면 빨강), 배너 지역 "DEFEND: 도시 이름", 항로 표시선이 폭격기 항로(도시까지).
- **페이즈 번호**: `PHASE_INTERCEPT` = 9(무작위 선택 대상, 가중치 배열 9번째 — 씬 배열이 짧으면 1), 보너스는 `PHASE_BONUS` = 10으로 옮김.
- **방어**: 폭격기·수송기 모두 **플레어**(`ACO_FlareDispenser` — NPC 전투기 AI 플레어와 같은 규칙: 나를 쫓는 미사일이 1.5km 안이면 미사일마다 한 번 50% 확률로 뿌리고, 켜진 2.5초 동안 나를 쫓는 미사일은 100% 속는다, 출격당 6번, 4초 간격. 오너가 판단해 모두에게 `NetFlare`, 미사일은 표적 마커에서 부모 쪽으로 장치를 찾는다 — `ACO_Missile`이 AI 플레어처럼 다룬다). 수송기 플레어는 적 미사일을 막아 준다. 폭격기는 **방어 기총 2문**(상부 전방위·꼬리 후방, `ACO_EscortUnit.DefenseGuns` = 작은 `ACO_AAGunSite`: 사거리 1.2km, 약한 연사, −60°까지 내려 쏜다) — 자체 콜라이더·락온 마커·체력 판정 없음(맞히면 폭격기가 맞는다), 폭격기가 출격하면 `AttachedSpawn`으로 같이 살아나고 격추·회수되면 멈춘다. Bake는 호위 대상/폭격기에 붙은 기총을 지상 풀에 넣지 않는다.
- **셋업**: 프리팹 생성 창 → **적 폭격기** → `NPC_Bomber`를 생성기 풀 아래에 4~6대(셋업 검사 [고치기]가 4대 놓는다). 수송기 플레어는 **아군 수송기** 프리팹을 다시 만들면 붙는다. 모델은 상자 — 바꿔 끼운다.

## 7. 셋업 체크리스트

0. **ACO → Tools...** — 모든 셋업 도구를 설명과 함께 모은 창(시작하기: 셋업 검사·프리팹 생성·Bake / 씬 오브젝트 만들기 / 패널 놓기 / 선택한 기체에 적용 / 유지보수). 그 안의 **셋업 검사(ACO → Setup Check...)** 로 빠진 셋업을 한눈에 확인하고 [고치기]로 채운다(아래 항목 대부분을 검사한다).
1. ✅ `SaccFlight → RenameLayers` (실행됨)
2. **ACO → Create Prefabs...** → 창에서 필요한 프리팹(미사일, 전투기, SAM, 대공포, 공장, 벙커, 전함, 구축함, HUD, 작전판, 작전 콘솔)을 체크해 생성. 이미 있는 프리팹은 기본으로 체크가 꺼져 있고, 체크하면 덮어쓰기 확인 후 초기화된다. 전투기/SAM은 미사일이 없으면 같이 만든다.
3. 플레이어 기체들의 `SaccEntity.AAMTargetsLayer` → **29+30** (2.2, 4.1)
4. NPC를 씬에 **미리 배치**(런타임 생성 불가, 2.3)
5. **ACO → Create Combat Director**로 씬에 디렉터를 1개 만든다(공격 정원). 없으면 정원 없이 동작한다.
6. 빌드하면 `AAMTargets`와 `Neighbors`·`Director`가 자동 베이크된다. 에디터 테스트 전에는 수동으로:
   - `SaccFlight → Debug_OnBuild_SetReferences` (AAMTargets)
   - `ACO → Bake NPC References` (Neighbors, Director)
7. 격추된 NPC는 `Respawn()`을 호출하기 전까지 그대로 남는다(`RespawnDelay = 0`). 테스트 중엔 버튼으로 `Respawn()`을 호출하거나 `RespawnDelay`를 잠시 양수로 둔다.
8. 빌더가 만드는 `Effects`(NPC)와 `Explosion`(미사일)은 **빈 오브젝트**다. 파티클/사운드를 그 아래에 넣어야 폭발이 보인다(애니메이터/스크립트는 켜고 끄기만 한다). 모델(`Model`, 미사일의 캡슐)도 임시 도형이니 실제 모델로 교체한다 — 교체 후에도 사망 시 숨기려면 `HideOnDeath`에 들어 있어야 한다.
9. 플레이어 미사일(SAV) 위력: `AAMDamage` 기본 1은 "타겟 FullHealth의 100%"라 한 발에 격추된다. 여러 발을 견디게 하려면 미사일 프리팹의 `AAMDamage`를 낮추거나 `AAMDamage_AbsoluteMode`를 켠다. 함선 본체는 `ArmorStrength`로 나눠서(전함 4 → 25%) 기본값으로도 여러 발이 든다.
10. 함선을 쓰면: 생성기의 `SeaLevel`(해수면), 물 콜라이더 레이어(`SeaSurfaceLayers`), `NoHitIsSea`를 맵에 맞춘다(6-8). 격침 애니메이션은 루트가 아니라 `Body`를 움직이게 만들고 함선 SaccTarget의 Animator 컨트롤러에 넣는다. 연출은 `Body/DeathEffects`(ShowOnDeath) 아래에.
11. 호위: `NPC_Transport`(1기 이상), 지상 호위: `NPC_ConvoyTruck`(3대 이상), 전차: `NPC_Tank`(15대 정도)를 생성기 풀 아래에 두고 Bake. 플레이어 기체의 `AAMTargetsLayer`에 25가 없어야(3번) 플레이어가 수송기를 락온하지 않는다. 파란 마커를 쓰려면 HUD 프리팹을 다시 만든다.
12. 육각 지형(선택): `ACO → Create Hex Terrain`(기둥 프리팹이 없으면 같이 만든다) → 위치를 지형 중심(높이 = 해수면)에, 항공모함을 `ExcludeZones`에(범위 가장자리 근처 바다 권장), 생성기 공역/해수면을 맞춘 뒤 Bake.
13. 지형 장식(선택, 육각 지형 필요): `ACO → Create Terrain Features`(장식 프리팹이 없으면 같이 만든다) — 지형/생성기와 자동 연결. 비우면 Bake가 빌더 프리팹으로 채운다.
14. 미션 UI: `ACO_HUD`, `ACO_MissionBoard`(작전판), `ACO_MissionConsole`(페이즈 수 선택)을 씬에 두고 `ACO → Bake NPC References`(빌드 시 자동)로 관리자/생성기를 연결한다. 작전판/콘솔은 파란 화살표(+Z)가 관객을 보게 놓는다.
15. 재화/강화: `ACO → Create Player Progress`(씬에 하나), 플레이어 기체에 `ACO → Player Missile → Add Upgrade Extension to Selected Vehicles`(DFUNC_AAM 교체 메뉴는 자동으로 넣는다), 격납고 프리팹을 다시 만든 뒤 Bake.

---

## 8. 다음 단계와 미해결 항목

- **CombatDirector 확장** ⏳ (공격 정원은 ✅ 5.2-1):
  - 교전 LOD — 대기/원거리 NPC의 AI 틱·동기화 빈도 낮추기(네트워크 예산의 안전장치)
  - 오너십 분산 — 1인 최악 부하 완화. 디렉터는 이미 다중 오너를 견디도록 만들었다
- **무전 음성** ⏳ (결과/점수 UI는 ✅ 6-6, 6-7) — 미션 관리자 이벤트(`EventListeners`)와 조회 함수로 붙인다.
- **미션 진행 확장** ⏳ — 추가 실패 조건(기지 방어), 클리어 후 다음 미션 자동 시작, 전투기 편대 유지 비행(편대장 추종).
- **해상 호위, 기지 방어, 적 공격기** ⏳ — 지상 호위는 ✅(6-10, 위협은 전차). 해상 선단은 같은 호위 유닛(`MoveMode` 해상)에 프리팹만 바꾸면 되지만 배를 공격할 적이 필요하다: 적 NPC 공격기의 대지·대함 공격 행동(진입 → 폭격/미사일 → 이탈, 표적 레이어 26), 그 페이즈 종류.
- **전차 확장** ⏳ — 지금은 정해진 항로만 따라간다(결정적, 동기화 0). 표적을 쫓아 방향을 트는 기동은 위치 동기화가 필요하다.
- **탱크** ⏳ (대공포는 ✅ 6-1) — 지상 이동은 Update 기반 웨이포인트+직선 이동이 Udon에서 안전(NavMesh 런타임 길찾기는 제약). 대공포는 SAV `AAGun_AIGUN` 재사용 여부 검토.
- **튜닝 값** — 동기화 빈도, 편대 간격, 지형 회피 거리 등은 실제 맵·유닛 수로 프로파일링 후 확정.
- **CIWS 미사일 요격** ⏳ 보류(사용자 결정) — 설계: SAV 미사일은 발사 45m 뒤 레이어 17 콜라이더가 켜지므로 함선당 1회 `OverlapSphereNonAlloc`(0.2초, 잠든 동안 생략)로 찾고, 포신 체류 시간을 채우면 SAV의 공개 `Explode()`(AAM/AGM/폭탄 공통 — DirectHit/SplashHit이 꺼진 채라 피해 없음)를 부른다. 요격 CIWS는 깨우는 거리를 미사일 사거리(~10km)로. 남은 결정: 요격 대상(AAM/AGM/폭탄), 성공 확률 유무.
- **플레이어 락온(`DFUNC_AAM`) 대체** ⏳ — 자체 스크립트로 교체 예정. 그때 락온 스윕(2.7)과 지상 유닛 수 제약을 다시 본다.
- 범위 밖: 포획/배신(전투 중 소속 변경), NPC 측 대응탄 사용.
