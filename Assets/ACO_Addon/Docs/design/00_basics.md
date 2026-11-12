# 기초 — 구현 현황·핵심 결정·SAV 분석·파일 구조·셋업·다음 단계

> 설계 문서의 한 분야. 목차·작성 규칙은 `Docs/DESIGN.md`. 절 번호는 예전 DESIGN.md 그대로(코드 주석의 "DESIGN.md 6-xx"가 가리키는 번호).

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
| 전차(지상 항로 순찰/매복, 아군 지상 표적 포격) | ✅ | `ACO_MobileUnit` + `ACO_AAGunSite`(포탑) |
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
      ACO_MissionManager.cs        미션 진행: 페이즈 1~5개(페이즈 모듈 중 무작위), 증원 웨이브(페이즈 시작/교착), 팀 점수·랭크, 이벤트 방송
      ACO_PhaseModule.cs           페이즈 모듈 기반(추상): 고르기·시작·판정·종료·표시·대사 — 관리자 자식에 두면 Bake가 목록에 넣는다(6-32~6-38)
      ACO_PhaseDestroy.cs 외        페이즈 모듈: Destroy(타격·SEAD·제공권·보스), Quota, TimeSensitive, Escort, Convoy, Intercept, Amphibious, Twins(보너스)
    AirUnits/
      ACO_NPCFighterAI.cs          전투기 두뇌: 순찰/추격/이탈/대기 상태머신, 요격 조준, 편대 간격, 격추/부활, 숙련도
      ACO_NPCFighterNet.cs         전투기 동기화: 오차 기반 전송, 원격 예측/보정, 소유권. Update 없음(AI가 OwnerTick/RemoteTick 호출)
      ACO_NPCFlightController.cs   비행 모델: 뱅크-투-턴, 월드축 분리 제어, 지형 회피. Update 없음(AI가 FlightTick 호출)
    GroundUnits/
      ACO_SAMSite.cs               SAM 포대(함선 VLS 겸용): 탐지, 포탑 조준, 원거리 잠재우기, 격추/부활
      ACO_AAGunSite.cs             대공포(함포/CIWS 겸용): 늦은 속도 리드 + 조준 흔들림, ACO_NPCGun 사격, 원거리 잠재우기, 격추/부활, CIWS 미사일 요격(6-40)
      ACO_StaticTarget.cs          고가치 표적(공장/벙커): 무장 없음, 체력은 SaccTarget, 잔해 전환, 미션 계약
      ACO_MobileUnit.cs                  이동 유닛(전차·헬리콥터·곡사포, Kind): 서버 시간 지상 항로(순찰 고리/매복), 지면 추종, 거리 LOD, 포탑은 같은 오브젝트의 ACO_AAGunSite
      ACO_EscortUnit.cs            호위 대상(아군): 서버 시간 항로(Catmull-Rom), 이동 방식 공중/해상/지상, 적 우선순위, 격추 연출
      ACO_Warship.cs               부위 파괴형 함선(전함/구축함): 본체 체력, 부위 파괴 → 본체 피해, 서버 시간 원 순항, 편대 호위, 격침 연출 필드
    Weapons/
      ACO_MissileLauncher.cs       공용 런처: 락온 누적·피락온 경고·탄약/재장전, SAV 미사일 덕 타이핑 계약. Update 없음
      ACO_Missile.cs               자체 미사일: 유도 3종, 콜드런치, 대응책/접근 경고(SAV 호환), 피격자/사수 판정, 후연, 피격 충격,
      ACO_Dispenser.cs             산탄 탄두(SCM): 자탄 파티클을 원뿔로 뿌리고, 지면에 닿는 시각마다 폭발·범위 피해(ACO_Missile.SubmunitionHit)
      ACO_FlyingFortress.cs        공중 요새: 궤도·돌진 비행, 단계 표현(엔진·코어 드러내기), 방공 수리
      ACO_LandShip.cs              육상전함: 지상 항로 이동(선체 길이로 지면에 맞춘 자세), 부위 노출(스커트 → 궤도 → 배기구), 코어 셔터
      ACO_FortressPart.cs          공중 요새 엔진(드러나기 전엔 락온·피해 없음)
      ACO_TelUnit.cs               TEL(시한 표적 전용): 발사 준비(지상 표적) → 탄도탄 상승(공중 표적), 서버 시간 궤적
      ACO_PhaseFleet.cs            함대전 지원 페이즈: 항모 격침 / 기함 사수, 함재기 출격, 상황별 대사
      ACO_PhaseFortress.cs         공중 요새 보스 페이즈(단계 판정, 궤도·돌진 기준값 동기화)
      ACO_PhaseLandShip.cs         육상전함 보스 페이즈(장갑·궤도·배기구 → 코어, 항로·속도 기준값 동기화)
                                   특수 동작(대응책 무시·아무 적 근접 신관·재표적·투하·시스키밍), CIWS 요격 판정, 땅·물 폭발 연출
      ACO_DFUNC_Missile.cs         플레이어 미사일 다이얼 기능(DFUNC_AAM 대체): 레이어 표적, 슬롯 장전, 강화 가능한 성능, 특수 동작 칸
      ACO_DFUNC_Special.cs         특수무장 슬롯 다이얼 기능: 격납고에서 고른 무장 모듈을 장착하고 이벤트를 넘긴다
      ACO_Ordnance.cs              폭탄/로켓 탄체: 중력/추진 비행, 충돌 폭발, 범위 피해(아군 제외), 쏜 사람 판정, 근접 신관(FLAK), 유도(LGB·LGR), 땅·물 폭발 연출
      ACO_NPCGun.cs                NPC 기총: 시각 전용 파티클 + 타겟 오너의 조준 체류 판정. Update 없음
    UI/
      ACO_ScoreHUD.cs              머리를 따라오는 점수/시간/페이즈/웨이브 패널, 페이즈·결과 안내(로컬)
      ACO_TargetMarkers.cs         표적 마커: 항상 같은 화면 크기, 항공기 사각형/지상 육각형, 페이즈 목표 TGT, 시선 정보 + 체력 바(로컬)
      ACO_MissionLog.cs            작전판: 격추·페이즈 기록(스크롤), 조종사 집계, 끝나면 디브리핑(로컬)
      ACO_LogScrollButton.cs       작전판 스크롤 버튼: NEWER · OLDER · LATEST(콜라이더 Interact)
      ACO_MissionStartButton.cs    작전 콘솔 버튼: Interact → 페이즈 수를 정해 미션 시작
      ACO_Loadout.cs               격납고: 내 특수무장 선택(로컬), 조종석에 앉을 때 특수무장 슬롯이 읽는다
      ACO_HangarButton.cs          격납고 버튼: 특수무장 선택·구매·쪽 넘김 / 상점 탭·줄 고르기·구매
      ACO_Shop.cs                  격납고 강화 상점: 줄을 고르고 구매 버튼으로 단계·소모품 구매, 보유 크레딧·지난 보상 표시(로컬)
      ACO_SpectatorCam.cs          관전 패널: 큰 스크린에 플레이어 기체를 리플레이식 컷(추적·플라이바이·망원·나란히·조종석·미사일·표적)으로 실시간 관전, 감독 AI(로컬)
      ACO_SpectatorButton.cs       관전 패널 버튼: POWER / 대상 < > / 컷 < >
      ACO_FlightHud.cs             비행 HUD(SAV HUD 대신): 기체 앞 고정 — 헤딩·속도·스로틀·고도·피치 래더·속도 벡터·상태·잔탄·HP·장전 막대·조준 표시(기총 피퍼)
      ACO_Units.cs                 거리 단위(m/km ↔ NM) — 비행 HUD·표적 마커·페이즈 진행 글자가 같이 쓴다
      ACO_TacName.cs               TAC 네임(콜사인): 입력 패널, 플레이어 영구 저장(aco_tac_name), 중복이면 뒤에 숫자 — 아군기 테두리 위 글자
      ACO_DecalStudio.cs           데칼 스튜디오(격납고): 이미지 URL 받기(캐시·대기열 — 모든 데칼 공용), 샘플 기체에 데칼 2개 배치·크기·회전·뒤집기·대칭, 자리 저장
      ACO_DecalGun.cs              데칼 건(VRC Pickup): 샘플 기체를 겨눠 미리보기, 방아쇠로 확정
      ACO_DecalButton.cs           데칼 스튜디오 패널 버튼
  Shaders/ACO_MarkerOverlay.shader 단색, ZTest Always(항상 맨 위)
  Shaders/ACO_UnderwaterBackdrop.shader 물속 물빛 벽(구 안쪽 면 = 지금 안개 색, 스카이박스 가리기)
  Shaders/ACO_SkyFog.shader 하늘 안개 돔(원평면, 하늘 픽셀에만 지금 안개)
  Shaders/ACO_CloudLayer.shader 비구름층·안개층 판(월드 고정 얼룩, 디더 페이드)
  Shaders/ACO_CloudVeil.shader 구름 속 물안개 막(월드 고정 3D 노이즈)
  Shaders/ACO_CanopyRain.shader 캐노피 빗방울(기체 자세의 머리 둘레 구, 느리면 맺혀 흘러내리고 빠르면 위로 늘어난다)
  Shaders/ACO_Lightning.shader 낙뢰 줄기(엇갈린 리본, 더하기 블렌드)
  Shaders/ACO_Decal.shader 데칼(Unity 프로젝터 — 표면에 비추기, 크기·뒤집기는 머티리얼 값, 마주 보는 면·깊이만)
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
                                   NPC_Warship, NPC_Destroyer, NPC_Tank, NPC_ConvoyTruck, NPC_Transport, NPC_Attacker, ACO_HexColumn, ACO_HUD, ACO_MissionBoard, ACO_MissionConsole
  SpecialWeapons/                  특수무장 정의 프리팹(ACO_Special_* — 예제 16종 + 복제해 만든 무장). Bake가 격납고·기체에 맞춘다
Tools/ (프로젝트 루트)
  aco_compile_check.py             Unity 컴파일러로 ACO 스크립트만 컴파일해 C# 오류 확인
  aco_sync_sim.py                  페이즈 동기화 몬테카를로(6-34)
  aco_ciws_sim.py                  CIWS 요격 시뮬레이션(6-40)
  aco_icons.py                     HUD·격납고 라인 아이콘 생성
  aco_make_flyby.py                적 미사일 스쳐 지나가는 소리 합성(6-45)
```

> 이 목록은 핵심 스크립트 위주다 — 레이더·무전·HUD 설정·생성기 보조 등 나머지는 각 절(6-xx)에 적었다.

- 레이어 상수는 별도 파일 없이 `ACO_PrefabBuilder`에 있다(25 아군 항공기 / 29 적 항공기 / 30 적 지상 / 17 히트박스). 런타임은 각 유닛의 `TargetLayers` 필드를 쓴다.
- 업데이트 비용을 줄이려고 Update를 가진 스크립트를 최소화했다: 전투기는 AI 1개만, 비행 컨트롤러와 런처는 Update가 없다. 지상 유닛은 원거리 잠재우기로 플레이어 근처 유닛만 계산한다(2.9).
- 새 U# 스크립트를 추가하면 Unity에서 Assets → Refresh(Ctrl+R)가 돌아야 프로그램 에셋이 인식된다 — 안 그러면 UdonSharp가 다른 스크립트를 먼저 컴파일하면서 "type could not be found"(CS0246)를 낸다(플레이 모드 중이거나 자동 새로고침이 꺼져 있을 때).
- `SaccFlightAndVehicles.Runtime.asmdef`가 `autoReferenced: true`라 ACO 스크립트는 별도 asmdef 없이 SAV를 참조한다.

---

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

### 6-94. Bake·프리팹 생성·Setup Check 역할

- **Bake**: 참조 연결과 씬에서 매번 다시 계산되는 값. VRChat 업로드 때도 자동으로 돌기 때문에 구조나 수치를 바꾸지 않는다. 코드는 `ACO_SceneBaker.*.cs`로 나눴다.
- **Create Prefabs**: 없는 프리팹·에셋을 새로 만든다. 기존 프리팹을 고치는 일은 맡지 않는다(다시 만들면 직접 고친 내용이 사라진다).
- **Setup Check**: 구조 변경과 일회성 변경. `Manual` 항목은 `자동 수정 모두`에서 빠진다.
- **프리팹 수치**: 수치만 바꾸는 일은 씬이 아니라 프리팹에 넣는다. `ACO_PrefabTuning.Entries`에 ID로 더하면 Setup Check에 나타나고, 고치기는 프리팹을 저장한 뒤 씬 인스턴스의 같은 값 오버라이드를 지운다. 적용·무시한 ID는 `Editor/ACO_AppliedTuning.txt`에 남는다. 수치를 또 바꾸면 새 ID를 쓴다.
- **작전판 스크롤 버튼**: 프리팹을 만들 때 넣는 월드 UI 버튼이다(레이저로 누른다). Bake의 자동 추가는 없앴고, 씬에만 남은 옛 버튼은 Setup Check가 알려 준다.
- **패널·격납고**: 콘솔, 작전판, 격납고, 관전, 콜사인, HUD 설정, 비행 HUD의 레이아웃은 프리팹 생성 코드에만 있다. 기존 프리팹을 고치던 `Ensure*` 패치는 지웠다.
- **유닛 풀**: 요새, TEL 3대, 기함, 항모, 레일건은 Setup Check가 넣는다(`MissingPoolUnits`, `AddPoolUnits`).
- **갑판 요원**: 설치는 Setup Check(`InstallCatapultCrew`), 연결은 Bake가 맡는다. 애니메이션 표를 바꾸면 Setup Check에 나타난다.
- **특수무장·ECM**: `ACO/Weapons/Install Special Weapons`와 Setup Check가 모듈과 ECM 구역 소리를 함께 설치한다. Bake는 격납고 목록(`SyncLoadouts`)과 ECM 연결만 맡는다.
- **씬 부품**: 보스 페이즈 모듈, 물속 부품, 버튼 효과음, 총탄 스침 소리, 공중 충돌 방지, 데칼 샘플 콜라이더는 Setup Check가 넣는다. 지형 표, 무전 대사, 강화 표도 코드가 늘면 Setup Check에 나온다. 강화 표는 값을 기본값으로 덮어써 `Manual`이다.
- **기체 설치**: 기체 표시, 데칼, 기총 강화, 좌석 재조정은 `SetupVehicle`이 한다. 빠지면 Setup Check 항목에 나온다.
- **프리팹 생성**: 카운트다운 판(콘솔·격납고), HUD 레이더, 관전 카메라 비는 프리팹을 만들 때 넣는다. 씬에 남은 옛 복사본과 겹치면 Setup Check `패널`에 나온다.
- **보스 프리팹**: 요새·레일건을 고치던 `Ensure*` 패치를 지웠다. 생성 코드에 같은 내용이 있다.
- **패널 변경 규칙**: 패널을 고칠 때는 씬이나 기존 프리팹을 패치하지 않는다. 생성 코드를 고치고 `ACO_PrefabVersions.Entries`의 판을 올린다. Setup Check `프리팹 판`이 구버전을 알려 주고 [고치기]가 다시 만든다. 직접 고친 내용이 사라지므로 `자동 수정 모두`에서 빠진다.
- **Bake에 남긴 값**: 참조 목록, HexRadius, 비 고도, SAV HUD 숨김, UI·FX 레이어처럼 씬에서 매번 다시 맞추는 값만 둔다.
