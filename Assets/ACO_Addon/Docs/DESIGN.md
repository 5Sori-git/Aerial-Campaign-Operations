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
      ACO_PhaseDestroy.cs 외        페이즈 모듈: Destroy(타격·SEAD·제공권·보스), Quota, TimeSensitive, Escort, Convoy, Intercept, Amphibious, SuperAce(보너스)
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
      ACO_FortressPart.cs          공중 요새 엔진(드러나기 전엔 락온·피해 없음)
      ACO_TelUnit.cs               TEL(시한 표적 전용): 발사 준비(지상 표적) → 탄도탄 상승(공중 표적), 서버 시간 궤적
      ACO_PhaseFleet.cs            함대전 지원 페이즈: 항모 격침 / 기함 사수, 함재기 출격, 상황별 대사
      ACO_PhaseFortress.cs         공중 요새 보스 페이즈(단계 판정, 궤도·돌진 기준값 동기화)
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

### 6-11b. 구름 + 비 ✅ (`ACO_CloudField`, `ACO_Rain`, 셰이더 `ACO/CloudPuffs`·`ACO/Rain`, 빌더 `ACO_SkyBuilder`)

- **만들기**: 도구 창 "구름 + 비"(`ACO → Environment → Create Clouds and Rain`) — 씬 루트 `ACO_Sky`(ACO_CloudField) 아래 구름 풀 `Clouds`(64개, 꺼 둔 채)와 `Rain`(ACO_Rain), 격납고 반사 프로브 크기의 비 가림 상자 `RainShelter_Hangar`. 메시는 `Generated/Clouds/ACO_Cloud_00~09`(모양 10종 — 작은/중간/큰 적운, 납작한 층적운)와 `Generated/ACO_RainDrops`(빗방울 4,000). 다시 만들면 메시·풀만 새로 만들고 머티리얼·스크립트 값은 유지. 퍼프 텍스처는 `Tools/aco_cloud_tex.py`(2×2 아틀라스: A = 모양, R = 두께).
- **구름 메시**: 타원체 3~6개를 합친 덩어리(바닥 평평)의 겉껍질(깊이 0 ~ 퍼프 크기의 0.7)에 퍼프 45~95개를 겹치지 않게 뿌린다(반지름 70~210m, 위쪽일수록 크게). 퍼프 = 사각형 하나, 정점 4개가 모두 중심에 겹침 — UV0 = 모서리, UV1 = (반지름, 회전각, 무작위, 아틀라스 칸), 법선 = 덩어리 바깥 방향(깊이 기울기), 정점 색 r = 차폐(위 밝고 아래·안쪽 어둡게), g = 아래쪽 정도. 바운드는 펼친 크기만큼 넓힌다.
- **구름 셰이더**(`ACO/CloudPuffs`): 정점 셰이더가 퍼프를 **두 눈의 가운데**를 향해 펼친다(구형 빌보드 — 눈마다 다르면 VR에서 지글거림). 픽셀에서 UV로 구 법선을 복원해 덩어리 바깥 방향과 섞고(`_PuffRound`), 감싸는 조명 + 두꺼운 곳 어둡게 + 하늘 환경광 + 아래쪽 색 + 차폐, 해를 등지면 얇은 가장자리 은빛 테두리(`_Silver`), 날씨 어둡게(`_Darken`). 가장자리는 **알파 투 커버리지**(가장자리를 1~2픽셀로 날카롭게 → MSAA로 안티에일리어싱, 정렬 불필요·깊이 기록, MSAA 없으면 알파 테스트), 텍스처 밉맵은 커버리지 보존. 가까이(`_NearFadeEnd` 160m → `_NearFadeStart` 40m) 디더링으로 사라지고(완전히 사라진 퍼프는 접어서 안 그림), 멀리(7 → 22km) 퍼프를 `_LodKeep`(40%)만 남기고 키운다. 그림자 패스는 빛 방향으로 펼친다(렌더러 Cast Shadows 켤 때만). 부드러운 반투명 모드는 셰이더 머리 주석 참고.
- **배치**(`ACO_CloudField`, 동기화 없음): 육각 지형 시드(동기화됨, 1초마다 확인)가 바뀌면 `Random.InitState(시드)`로 풀의 앞 `Coverage`(0.5) 비율을 켜서 반지름 `FieldRadius`(지형 반경 × 0.9) 원판, 고도 1,300~2,600m에 놓는다(크기 0.75~1.4배, 가로·세로 비율 흔들기). 모든 클라이언트가 같은 자리. 지형이 없으면 `FallbackSeed`.
- **구름 속 안개**: 0.1초마다 내 머리를 각 구름의 속 타원체(퍼프 중심 범위 × 0.85, 빌더가 굽는다) 좌표로 바꿔 `FogOuter`(1.25) → `FogInner`(0.7) 사이면 안개를 `InsideFogDensity`(0.0035, Exp²)·하얀색으로 섞는다(초당 `FogSpeed` 2.5). 나오면 기본 안개로 되돌린다. 씬 안개가 켜져 있어야 한다(조명 설정).
- **비**(`ACO_Rain`, 로컬): 빗방울 메시를 셰이더가 카메라 둘레 `_Box`(44m) 상자에 반복 — 방울은 월드에 붙어 `_FallSpeed`(9m/s) + 바람으로 떨어지고, 빗줄기는 카메라 기준 상대 속도 방향으로 `상대 속도 × 0.03초`(0.7~7m) 늘어난다 → 비행 중엔 앞에서 비스듬히. 스크립트는 프레임마다 머리를 따라가며 머리 속도(순간이동 무시, 부드럽게)를 `_CamVel`로 넘긴다. `Intensity`(0~1, `FadeTime` 3초)만큼 방울을 그린다(0이면 렌더러·소리 끔). 조종석 안(2.5m)·상자 가장자리는 숨김. `ShelterBoxes`(트랜스폼 단위 상자, 물리 없음) 안과 `MaxAltitude`(1,500m) 위에서는 그친다. 빗소리 `RainSound`(선택).
- **날씨**(`ACO_Weather`, `ACO_Sky`에 같이, 동기화 없음): 육각 지형 시드(동기화됨, 1초마다 확인)를 정수 해시해 `Weights`(4 / 3 / 1.5 / 1.5) 비율로 **맑음(CLEAR) / 구름 많음(CLOUDY) / 안개(FOG) / 비(RAIN)** 중 하나를 고른다 — 모두 같은 날씨, 리롤·미션 뒤 새 지형마다 바뀐다(폭풍은 없음). "
 "날씨별 값(인스펙터 배열, WX_* 순서): 구름 양 0.25/0.85/0.4/1(`ACO_CloudField.SetCoverage`), 구름 어둡기 0/0.2/0.1/0.65(`SetDarkness`), 안개 = 씬 안개 밀도 × 1/2.2/18/7, 색 × 색조(`SetBaseFog` — 구름 속 안개는 그 위에), "
 "비 0/0/0/0.85(`ACO_Rain.SetIntensity`), 태양 세기 × 1/0.75/0.6/0.45(`Sun` — Lighting의 Sun 또는 가장 밝은 디렉셔널 라이트). 바뀔 때 `TransitionTime`(5초) 동안 서서히(처음엔 바로). "
 "`ForcedWeather`(−1 = 무작위)로 고정해 테스트. 작전 콘솔 지형 카드와 격납고 명단 글자에 "WEATHER: RAIN". 게임플레이 영향(락온·탐지 거리 등)은 아직 없음.

### 6-11. 육각 기둥 지형 — 섬/고원/산맥/협곡 프리셋 ✅ (`ACO_HexTerrain`, 프리팹 `ACO_HexColumn`)

- **새 프리셋 4종**(8~11, 기본 비율 1/1/0.7/1 — 예전 8칸 비율 배열은 Bake가 늘리고, 게임 진행 지형 이름도 늘린다): 가운데(항모)를 피해 **정점은 가장자리 쪽**. **8 해안**: 무작위 방향 한쪽이 큰 땅(반평면 — 해안선이 가운데에서 `CoastLandStart` 0.28R, 내륙으로 `CoastRamp` 0.45R에 걸쳐 높아짐), 해안 쪽 평야 + 내륙 구릉·산(`CoastHillHeight` 750m), 앞바다 작은 섬 `CoastIslets` 3. **9 화산섬**: 가운데에서 `VolcanoOffset` 0.58R에 원뿔 섬(반지름 0.36R), 테두리 `VolcanoHeight` 1700m, 꼭대기 분화구(반지름 화산의 14%, 깊이 350m), 비탈에 방사형 골짜기(노이즈), 곁에 작은 섬 1~2. **10 암초 군도**: 가운데 `ReefInner` 0.25R 안을 비우고 바깥 고리에 바위섬 28~40개(반지름 500~1300m = 1~3칸, 높이 90m) — 바다가 넓은 해상전 맵(작은 섬이 많아 `MAX_BLOBS` 48). **11 분지**: 가운데에서 `BasinOffset` 0.55R에 산맥 고리(반지름 0.27R, 최고 1100m, 폭 32%)로 둘러싼 저지대(바닥 40m), 고개 `BasinPasses` 3곳(폭 1.6km, 고리가 12%로 낮아짐) — 첫 고개는 늘 가운데(항모) 쪽을 향한다.
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
- **모델**: 6-104를 본다. `ACO → Create Prefabs...`의 "지형 장식"이나 Setup Check `deco-model-1`이 만든다. 화면 0.4% 아래로 작아지면 그리지 않는다(LODGroup).
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
  | MISSILE | 피해 +8% / 탄수 +15% / 선회 +6% / 락온 속도 +10% / 락온 거리 +6% / **시커 범위 +6%** / **재장전 속도 +8%** | 2,000 / 1,500… / **3,000** | 일반 미사일(특수무장 슬롯 밖) |
  | SP. AAM | 위와 같은 7개 | 2,500 / 2,000… / **3,500** | 특수 대공 미사일(4AAM 등) |
  | SP. AGM | 피해 +8% / 탄수 +15% / 폭발 반경 +6% / **재장전 속도 +8%** (유도 성능은 강화 없음) | 2,500 / 2,000 / 2,000 / **3,500** | 특수 대지 미사일(AAM·4AAM 모듈의 표적만 지상으로 바꾼 것, `MissileSplash` 켬) |
  | SP. BOMB | 피해 +8% / 탄수 +15% / 폭발 반경 +6% / **재장전 속도 +8%** | 2,500 / 2,000 / 2,000 / **3,500** | 특수 폭탄·로켓(UGB, RKT) |

  - 대지 미사일 범위 피해: `ACO_DFUNC_Missile.MissileSplash`를 켜면 유도 미사일(`ACO_Missile`)도 터진 자리(명중·지형 충돌)에 `SplashRadius` 범위 피해를 준다 — 폭탄/로켓과 같은 규칙(SaccTarget마다 가장 가까운 콜라이더 거리로 한 번, 중심 `Damage` → 가장자리 `MinSplashFraction`, 쏜 기체·호위 대상 제외, 정통으로 맞은 표적은 중복 없음, 레이어 `ACO_Missile.SplashLayers` 17). 쏜 사람 화면에서만 판정한다. 대지 계열인데 꺼져 있으면 Bake가 경고한다.
  - 적용 수치: 피해 `Damage`, 탄수 `MaxAmmo`, 선회 `TurnRate`, 락온 속도 = `LockTime ÷ 배수`, 락온 거리 `LockRange`, **시커 범위** = 미사일 추적 각도 `ACO_Missile.MaxTrackingAngle` × 배수(락온 각도 `LockAngle`은 강화하지 않는다 — 예전 "락온 범위" 강화는 락온만 넓어져 시커 밖 표적을 놓쳤다. 발사 이벤트 `NetLaunch`의 8번째 값으로 보내고, 락온 범위보다 좁지 않게 = max(기본 × 배수, LockAngle), 최대 180°. 저장 ID `msl_seeker`·`aam_seeker`, 이름 "SEEKER CONE"), 폭발 반경 `SplashRadius`, 재장전 = `SlotReloadTime ÷ 배수`(10단계 ×2 → 장전 시간 절반, 장전 중인 슬롯은 다음 장전부터). 저장 ID `msl_reload`·`aam_reload`·`agm_reload`·`bomb_reload`. 씬 진행도의 이름·배수·가격·단계 가격 배열이 항목 수·최대 단계와 다르면 Bake가 새 기본값으로 되돌린다(`MigrateUpgradeArrays`).
  - 무장 계열: 무장 모듈 인스펙터의 `ACO_DFUNC_Missile.UpgradeFamily` 드롭다운(enum `ACO_UpgradeFamily`: Missile / SpecialAAM / SpecialAGM / SpecialBomb / None, 기본 Missile)으로 **직접 고른다** — Bake는 레이어를 보지 않는다. None이면 강화를 받지 않는다. 셋업 메뉴의 4AAM 프리셋은 SpecialAAM, UGB/RKT 프리셋은 SpecialBomb을 넣는다. SpecialAGM인데 `MissileSplash`가 꺼져 있으면 Bake가 경고한다.
  - 적용(`ACO_VehicleUpgrades`, 기체의 `ExtensionUdonBehaviours`): 탑승할 때 내 강화로, 내릴 때 원래대로 — **조종사 화면에서만** 바꾼다(비행·SAV 체력은 오너 계산, 미사일 비행 성능은 발사 이벤트에 실림, 피해는 쏜 사람 값). 다음 탑승부터 적용.
  - 미사일 잔탄은 늘어난 총 탄수만큼만 더하고 뺀다(`ACO_DFUNC_Missile.ApplyUpgrades`) — 다시 채우지 않는다. 못 뺀 만큼은 빚으로 남겨 다음 탑승 때 먼저 갚는다(다 쏘고 내렸다 타서 공짜로 채우지 못하게). 폭발/리스폰의 `ResetAmmo`가 빚을 지운다.
  - SAV 무장(DFUNC_Bomb 등)과 기관포는 강화하지 않는다(기관포 탄수는 SAV 내부 private 값이 얽혀 있다).
- **소모품**(상점 6번째 탭 SUPPLY, `ACO_PlayerProgress.CONS_*`): 크레딧을 꾸준히 쓰게 하는 1회용 품목 — 종류마다 1개까지 사 두고, **출격한 미션이 끝나 보상을 받을 때 모두 사라진다**(그 미션 동안은 격추·재출격해도 유지, 출격하지 않은 미션이 끝나면 그대로 남는다). 강화와 곱해진다. 카운트다운 중에는 못 산다.

  | 소모품(ID) | 기본 효과 | 가격 | 적용 |
  |---|---|---|---|
  | ARMOR PLATE (`armor`) | 체력 ×1.25 | 1,500 | `SaccAirVehicle.FullHealth`(강화 배수에 곱함) |
  | CM POD (`countermeasure`) | 플레어 ×1.5 | 800 | SAV `DFUNC_Flares.FullFlares` → `ReInitNumFlares()`(가득 채움, 내릴 때 원래 수로) — Bake가 기체의 플레어 기능을 `ACO_VehicleUpgrades.Flares`에 굽는다 |
  | EXTRA AMMO (`ammo`) | 특수무장 탄수 ×1.5 | 1,200 | UpgradeFamily가 SpecialAAM/AGM/Bomb인 무장의 탄수 배수에 곱함(일반 미사일 제외) |
  | FUEL TANK (`fuel_tank`) | 연료 용량 ×1.5 | 600 | `SaccAirVehicle.FullFuel`(남은 연료도 같은 비율 — 탑승 때 가득이면 가득) |

  **외형**: 기체 강화 확장(`ACO_VehicleUpgrades.ConsumableVisuals`, CONS_* 순서)에 넣은 오브젝트를 사 둔 소모품으로 탑승하면 켠다 — 조종사가 모든 클라이언트에 알리고(`NetConsumableVisuals(mask)`, 늦게 들어온 사람에게 다시), 내리면(`SFEXT_G_PilotExit`) 끈다. 이 때문에 강화 확장의 동기화 모드가 NoVariableSync(네트워크 이벤트). 격납고 전시 기체는 `ACO_Shop.ConsumablePreviewVisuals`(같은 순서) — 사 둔 것만 켠다(로컬).
  **플레어 연사 연출**(`ACO_VehicleUpgrades`): SAV 플레어가 나갈 때마다(`SFEXT_G_LaunchFlare`) `FlareBurstDuration`(1.5초) 동안 `FlareBurstInterval`(0.12초)마다 플레어 파티클을 더 뿌린다(`FlareBurstEmitters`, 비우면 SAV의 `FlareParticles`) — 보이기만, 대응탄 수·효과는 SAV 그대로.
  저장 `aco_cons_` + ID(int 1/0). 이름·효과 문구·배수·가격은 진행도 인스펙터(`ConsumableNames/Effects/Factors/Prices`)에서 바꿀 수 있다. 상점 줄: `이름  효과  READY`(산 것, 밝은 녹색) 또는 가격. 아이콘 hull / cm_pod / ammo / fuel, 탭 아이콘 tab_supply.
- **특수무장 선택 화면**(격납고 왼쪽, 목록형): 줄 5개 — 왼쪽 아이콘, 오른쪽 무장 이름 + 상태(EQUIPPED 초록 / OWNED / 가격 노랑) + 잠긴 무장 자물쇠. 위의 `<` `>`로 **분류**를 돌아가며 넘긴다: 공대공(AIR-TO-AIR) → 공대지(AIR-TO-GROUND) → 폭탄·로켓(BOMBS / ROCKETS) → 기타(OTHER) → 공대공(`ACO_Loadout.CAT_*`, 이름 `CategoryNames`, 가운데에 "분류 이름 n / 4"). 한 분류가 5개를 넘으면 그 분류의 다음 쪽 → 다음 분류. 빈 분류는 "NO WEAPONS IN THIS CATEGORY YET". 장착·해금하면 그 무장이 있는 분류로 옮겨 보여 준다. "
 "분류는 무장 정의의 `Category`(드롭다운: Auto / AirToAir / AirToGround / Bomb / Other) — Auto면 모듈의 강화 계열(SpecialAAM → 공대공, SpecialAGM → 공대지, SpecialBomb → 폭탄, 그 밖 → 기타). Bake(무장 목록 맞추기)가 `SpecialCategories`에 넣는다.
- **특수무장 해금**: 격납고 `ACO_Loadout.SpecialPrices`(**4AAM 6,000**, UGB 8,000, RKT 8,000 — 모두 잠긴 채 시작). 잠긴 무장을 누르면 설명·가격만 보이고 BUY 버튼으로 사면 바로 장착. 마지막으로 고른 무장은 저장된다. 해금한 무장이 없으면 **특수무장 없이 출격**(슬롯 비움, `NetEquip(255)`). 고른 무장이 기체에 없으면 해금된 첫 모듈을 쓴다.
- **격납고 패널**(`ACO_Hangar` 2.8 × 1.5m): 위 = 크레딧·지난 보상, 왼쪽 = 특수무장(선택/구매), 오른쪽 = 분류 탭 5개 + 강화 줄 6칸(`이름 [##---] +20% 4,000 CR`, 살 수 있으면 녹색·모자라면 회색·최대면 밝은 녹색, 남는 줄은 숨김).
- **아이콘**(HUD 라인 — 흰 선 + 알파, Image 색으로 물든다): `Textures/Icons/aco_icon_*.png` — 기체 강화 5(hull, mobility, engine, fuel, stability), 능력치 8(damage, ammo, turn, lock_speed, lock_range, seeker, blast, reload — 미사일/특수 계열이 같이 쓴다), 탭 5(tab_*), 특수무장(weapon_4aam/ugb/rkt/none/4agm/lagm/laam/sasm/qaam/hpaa/hvaa/flak/ccaa/adam/lasm/lgb/lgr/scm), lock, credits. 프리팹 빌더가 Sprite(밉맵, 256)로 가져오게 설정해 붙인다: 강화 줄 왼쪽(`ACO_Shop.RowIcons` + 강화 ID별 `UpgradeIcons` — 탭에 따라 바뀜), 탭 왼쪽, 특수무장 버튼 위(ID로 고름, 모르면 미사일 그림) + 잠긴 무장 구석 자물쇠(`ACO_Loadout.LockIcons`), 크레딧 옆. 다시 그리려면 `Tools/aco_icons.py <출력 폴더> <미리보기.png>`(표준 파이썬만) 후 격납고 프리팹을 다시 만든다.
- Bake가 진행도를 관리자·격납고·상점·작전판·기체 강화 확장에 연결하고, 강화 확장의 일반 미사일/특수무장/SaccAirVehicle을 굽고, `ExtensionUdonBehaviours`에 없으면 넣는다. 확장이 없는 플레이어 기체는 경고한다.
- ⚠ 저장 값은 각자 컴퓨터(클라이언트)에서 계산한다 — PvE라 조작 방지는 하지 않는다.

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

### 6-13. 페이즈 음성 → 무전 채터(6-19)로 합쳐짐 (`ACO_MissionVoice` 삭제 — 씬에 남은 컴포넌트는 셋업 검사 "프로그램이 빠진 UdonBehaviour" [고치기]로 지운다)

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
- **HUD 설정**(RWR 카드): `SHOW`(켜기/끄기 — 링과 경보음 모두), `RING SIZE`(링 반경 배율 0.5~2 — 미사일이 놓이는 안쪽·바깥 반경과 락온 호 반경, `SizeScale`), `MISSILE MARK SIZE`(쉐브론 크기 0.5~2.5, `ChevronScale`), `LOCK ARC SIZE`(락온 호 반경 0.9~2, `LockArcScale` — 바깥 반경 + 0.8° 안으로는 안 들어옴). 저장 키 `aco_hud_rwr`, `aco_hud_rwr_size`, `aco_hud_rwr_missile_size`, `aco_hud_rwr_lock_size`. 항목이 늘면 HUD 설정 패널 프리팹을 다시 만들어야 새 줄이 보인다(예전 프리팹도 오류 없이 동작, 새 항목만 안 보임).
- **색**: 먼 미사일 `FarColor` = HUD 초록 → 가까울수록 `CloseColor` 빨강(`UseColor`), 락온 호 `LockColor` 초록(호마다 머티리얼 복사본에 매번 넣는다). 예전 기본색(노랑)인 RWR은 Bake가 초록으로 바꾼다(직접 바꾼 색은 그대로).
- **HUD 설정 패널 배치**: 가로 2.8 × 1.25m, 3열 분류 카드 — SCORE PANEL(좌우·상하·거리·크기) / RESULT·NOTICE TEXT(좌우·상하·크기) | RADIO SUBTITLES(좌우·상하·크기) / TARGET MARKERS(테두리 크기·정보 각도) + RESET ALL | CROSSHAIR(켜기·크기·거리(+RWR)) / RWR(켜기·링·미사일 표시·락온 호). 카드 제목은 노랑, 줄 이름은 카드 안에서 짧게.
- 연결: Bake(`BakeRwr`)가 씬의 RWR을 모든 `ACO_MissileLauncher`·`ACO_DFUNC_Missile`·`ACO_HudSettings`에 넣는다.
- 나중에: 적외선 미사일 늦게 감지(MAWS 거리), RWR 성능 강화(감지 거리·오차) — 미사일 종류는 이미 알고 있어 `AddMissile`에 넘기기만 하면 된다.

### 6-21. 좌석 눈높이 재조정 ✅ (`ACO_SeatRecenter`, SAV 확장, 로컬 전용)
- VR에서 탑승 중 **점프 버튼 한 번 = 좌석 재조정**, **두 번(0.3초 안) = 내리기**(SAV `SaccEntity.DoubleTapToExit`). 한 번 누른 뒤 0.3초 동안 두 번째 입력이 없을 때만 재조정한다.
- 재조정 = SAV `SaccVehicleSeat`가 탈 때 하는 보정 그대로(높이·앞뒤를 `TargetEyePosition`에, VR이면 방향을 `RotationCalibrationTarget`에, 1~2초). SAV 파일은 그대로 — private `CalibratedY/Z/Yaw`, `AdjustTime`을 `SetProgramVariable`로 되돌리고 `SeatAdjustment()`를 부른다. 탄 직후 보정이 아직 돌고 있으면 겹치지 않게 무시(안내만). SAV가 변수 이름을 바꾸면 경고 한 번 남기고 아무것도 안 한다.
- 안내: 점수 HUD의 `NoticeText`(`ACO_ScoreHUD.ShowNotice`, 미션 상태와 상관없이 2.5초) — "SEAT RECENTERING / SIT UPRIGHT AND LOOK STRAIGHT AHEAD", 보정 중이면 "SEAT ADJUSTING...".
- 셋업: Bake가 강화 확장이 있는 기체(플레이어 기체)마다 확장을 넣어 `ExtensionUdonBehaviours`에 등록하고 `DoubleTapToExit`를 켠다, 점수 HUD에 안내 글자가 없으면 만들고 연결한다. 셋업 검사에도 항목. SAV 메뉴의 Double Tap To Exit 토글을 연결해 뒀다면 플레이어가 끌 수 있다(끄면 점프 한 번에 내린다 — 재조정은 동작 안 함).
- 에디터(ClientSim)에서는 SAV 좌석 보정 자체가 돌지 않는다 — VRChat VR에서 확인.

### 6-22. 특수무장 카탈로그 / 격납고 페이지 ✅ (`ACO_SpecialWeaponDef`, `ACO_SpecialWeaponCatalog`, `ACO → Special Weapons`)

- **탄종 글자 자동 생성**(특수무장 슬롯 인스펙터 아래 버튼, 또는 메뉴 `ACO → Player Vehicle → Build Special Weapon HUD-MFD Labels on Selected Vehicles`): 기체 안의 원본 글자 `SP.W`(HUD, UI Text)·`MFD_SP.W`(MFD, TextMeshPro)를 모듈마다 같은 부모에 복제(`SP.W_<ID>`, 원본 바로 뒤)하고 글자의 "SP.W"를 무장 ID로 바꿔 `ModuleHudLabels`/`ModuleMfdLabels`에 넣는다. 다시 실행하면 있는 복제본의 글자만 고친다(ID가 바뀌었으면). 복제본은 에디터에서 꺼 둔다(슬롯이 장착 모듈 것만 켠다). 원본은 기본으로 `EmptyObjects`에 넣어 무장이 없을 때만 보인다.
- **무장 정의 = 프리팹 하나**(`Assets/ACO_Addon/SpecialWeapons/ACO_Special_<ID>.prefab`): 루트 `ACO_SpecialWeaponDef`(Id·이름·설명·가격·아이콘·목록 순서) + 자식 `Module`(`ACO_DFUNC_Missile` — 표적 레이어·다중 락온·슬롯·탄수·장전·락온 거리/각도/시간·피해·속도·선회·근접 신관·범위 피해·강화 계열 등 성능 전부). `Missile`을 비우면 그 기체의 일반 미사일 모델을 쓴다(폭탄·로켓은 ACO_Bomb/ACO_Rocket). 정의는 일반 MonoBehaviour라 **씬에 두지 않는다**(셋업 검사가 알린다).
- **예제 16종**(프리팹 생성 창 → **플레이어 특수무장** — 있는 예제도 다시 만든다. 새 예제는 Bake가 알아서 만들고 설치한다, 6-42): 성능·미사일 모델 값은 F/A-18(`Assets/Pit-bool/FA-18E Main_ACO.prefab`)에 맞춘 것이다(6-39).
  - 공대공: 4AAM(4표적) 6,000 / FLAK(무유도 근접 신관 로켓, 6-39) 9,000 / LAAM(10km) 12,000 / SASM(근접 신관 45m + 범위 50m) 10,000 / QAAM(선회 360°/s) 15,000 / HVAA(1900m/s) 13,000 / CCAA(대응책 무시, 6-39) 16,000 / HPAA(피해 400, 근접 신관 20m) 14,000 / ADAM(투하 후 가장 가까운 적, 범위 120m, 6-39) 18,000.
  - 공대지: 4AGM(4표적) 9,000 / LAGM(6.5km, 범위 100m) 10,000 / LASM(대함 시스키밍, CIWS에 격추되지 않음, 6-40) 12,000.
  - 폭탄·로켓: UGB(CCIP, 범위 200m) 8,000 / LGB(유도 폭탄, 6-41) 11,000 / RKT 8,000 / LGR(유도 로켓, 6-41) 11,000.
  - 대지는 특수 대지 탭, 대공은 특수 대공 탭, 폭탄·로켓은 특수 폭탄 탭 강화를 받는다. 미사일 풀은 2발만 미리 만든다(모자라면 늘어난다).
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
- **보너스 페이즈**: 마지막 페이즈를 클리어하면 페이즈 수별 확률(`BonusChanceByPhases` 5·10·25·50·100%, 비우면 `BonusPhaseChance`·`BonusMinPhases`)로 한 페이즈가 더 붙는다(풀에 쉬는 슈퍼에이스가 있을 때, 미션당 한 번 굴림). 슈퍼에이스 1기(목표 태그) + 호위 에이스 `BonusEscorts`(2)기(목표 아님). 배율 `BonusScoreMultiplier`(×2.5), 격추하면 `BonusClearBonus`(15,000) × 배율. `BonusTimeLimit`(240초) 안에 못 잡으면 슈퍼에이스가 물러나고(태그 회수) 보너스만 없다 — 본 미션은 이미 클리어라 실패하지 않는다(미션 제한 시간도 보지 않는다, HUD 시간은 보너스 남은 시간). 랭크 기준(배율 합)과 시간 보너스는 본 미션 기준(보너스 페이즈 제외, 본 미션 클리어 시각 `_mainClearMs`). 배너 `BONUS - <이름>`, 페이즈 수가 하나 늘어난다(PHASE 4/4). 페이즈 시작 웨이브·교착 웨이브 없음.
- **생성기**: 슈퍼에이스(`ACO_NPCFighterAI.SuperAce`가 있는 전투기)는 일반 배치(전체 배치·증원·목표 편대·호위 요격)에 쓰지 않는다. `OBJ_SUPER_ACE`만 쉬는(또는 격추된) 슈퍼에이스를 내보낸다. 동기화·부활·격추 판정은 일반 전투기와 같다.
- **슈퍼에이스**(`ACO_SuperAce`, 기체 자식 — AI·비행·동기화는 일반 NPC 그대로, 체력 6배·플레어 12번·에이스 등급):
  - **다연장**: `SalvoCooldown`(25초, 첫 발은 등장 12초 뒤)마다 `SalvoRange`(5km) 안 플레이어를 가까운 순으로 `SalvoMaxTargets`(4)명 골라 `SalvoWarnTime`(1.5초) 동안 락온 경보(0.5초마다, RWR 호도 뜬다) → 모두에게 동시에 발사(전용 런처 `SalvoLauncher` — 연사 간격 0, 8발, 다 쓰면 40초 재장전). AI 오너만 판단, 발사는 런처가 모두에게 알린다(`WarnTarget`·`FireAt`).
  - **기만체**: 나를 노린 플레이어 미사일이 `DecoyReactDistance`(2.5km) 안이면(`DecoyCooldown` 18초, 출격당 `DecoyCharges` 4번) 가짜 표적 3개를 뿌린다(`DecoyLifetime` 12초). 기만체 = 플레이어 락온 레이어(29)의 빈 오브젝트, 처음 켜질 때 기체에서 떼어 낸다. **편대 비행**: 기체 기준 자리(`DecoyOffsets` — 좌우 180m, 위·뒤 220m)에 붙어 기체의 기동을 그대로 따라 난다(`DecoySpreadTime` 1.2초 동안 기체 자리에서 벌어지고, `DecoySway` 25m만큼 자리마다 다른 박자로 흔들린다). 각 클라이언트가 자기 화면의 기체 위치로 계산하므로 위치 동기화가 없다. HUD 마커는 진짜와 똑같다(이름·목표 색·체력 바 — 같은 유닛으로 등록). 락온해 쏜 미사일은 기만체 근처에서 터지고 피해는 없다(기만체엔 SaccTarget이 없다). 기체가 거둬지면(비활성) 기만체도 사라진다.
  - **무전**: 등장·다연장·기만체·체력 절반·격추 때 자막(`ACO_Chatter.Say` — 대사 표 밖, 확률·쿨타임 없이, 이름 색 `SpeakerColor`). 대사는 인스펙터(`LineSpawn` 등).
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

### 6-26. 캐터펄트 — 풀스로틀 유지 사출 ✅ (`ACO_DFUNC_Catapult`, SAV DFUNC_Catapult 대체, `ACO → Player Vehicle → Replace Catapult on Selected Vehicles`)

- SAV `DFUNC_Catapult`를 복사해 같은 방식(물리는 지점 = 이 오브젝트, BoxCollider 트리거, 캐터펄트 레이어 24, 정렬·묶기·네트워크 이벤트·애니메이터 `launch`/`oncatapult`·"speed=" 이름 규칙·사출 속도/각속도)으로 동작한다.
- **풀스로틀 사출**: 캐터펄트에 걸린 채 `ThrottleInput` ≥ `FullThrottleThreshold`(0.95)를 `FullThrottleHoldTime`(2초) 유지하면 사출(중간에 내리면 다시 센다). SAV의 AutoLaunch_FullThrottle은 정확히 1만 보고 트리거 사출을 껐다 — 여기서는 `AllowTriggerLaunch`(다이얼 선택 후 트리거·데스크톱 키)도 함께 된다.
- **표시(선택, 조종사 화면)**: `AttachedObjects`(걸려 있는 동안), `HoldObjects`(풀스로틀 유지 중), `HoldFill`(X 스케일 = 진행도, 피벗 왼쪽 끝).
- **교체 메뉴**: 선택한 기체마다 DFUNC_Catapult가 있는 오브젝트에 새 컴포넌트를 붙여 값(SAVControl·다이얼 표시·소리·각도·레이어·기어·애니 트리거·정렬)을 옮기고, SaccEntity의 Dial_Functions_L/R·ExtensionUdonBehaviours에서 바꾼 뒤 옛 컴포넌트를 지운다. SAV AutoLaunch_FullThrottle이었으면 그 지연을 유지 시간으로(최소 1초). SAV 폴더는 건드리지 않는다.

### 6-27. 기체 자동화(자동 캐노피 / 기어·후크 연동 / 리스폰 복구) ✅ (`ACO_VehicleAutomation` — `Scripts/Vehicle`, 셋업 메뉴 `Editor/ACO_VehicleAutomationSetup`)

- 한 번 지웠다가 다시 들였다(내용 그대로). 자동 캐노피(움직이거나 캐터펄트면 닫고, 지상에서 멈추면 2초 뒤 열기), 기어·후크 연동(공중에서 기어를 따라 후크, 지면 3초면 후크 올림), 리스폰 복구(다시 나타난 뒤 폭발 클립이 끈 오브젝트를 켜고 `Animator.Rebind()` — 기체 애니메이터에 리스폰 애니메이션을 넣었다면 `RebindOnRespawn`을 꺼도 된다). 셋업: `ACO → Player Vehicle → Add Auto Canopy + Gear-Hook Link to Selected Vehicles`(기체 아래 오브젝트 + 확장 목록, 캐노피·후크를 다이얼에서 뺄지 선택). 셋업 스크립트는 UnityEditor를 쓰므로 반드시 Editor 폴더에 둔다(빌드 실패 방지).

### 6-28. 플레이 테스트 개선 묶음 (2026-11)

- **아군 유닛 격추 점수**: NPC 무기(ACO_NPCGun, NPC 런처의 ACO_Missile — `FromNpc`)가 SaccTarget에 주는 피해의 무기 종류에 `ACO_Missile.NPC_WEAPON_OFFSET`(100)을 더한다. 작전판은 그 값이면 조종사 칸에 파란 `ALLY UNIT`, 개인 점수·조종사 집계·격추 대사 없음(팀 점수는 관리자가 그대로 센다). 플레이어 기체(SaccEntity)에 주는 피해는 SAV 킬피드 때문에 원래 값 그대로.
- **텔레포트 속도 0**: 게임 진행·텔레포터·화면 암전의 TeleportTo 직후 `SetVelocity(0)` + 다음 프레임 한 번 더(방금 내린 항공기 속도로 튕기지 않게).
- **함선 격침 → 부위 파괴**: 원인 — 격침 때 부위에 보내던 `Silence`는 교전만 잠깐 멈췄고, 부위 SaccTarget 체력이 남아 락온 마커가 살아 있었으며 `_disabled`가 체력(> 0)으로 다시 계산되며 사격이 되살아났다. 이제 `Silence`가 부위 SaccTarget을 터뜨린다(오너, `SaccTarget.Explode`). 본체 부활(AttachedSpawn/NetRespawn) 때 부위도 함께 부활한다.
- **대사 분리**(EV_COUNT 21): `EV_ALLY_FLARE`(아군 플레어 — 장치의 `Friendly`, 호위 유닛이 출발할 때 `!Hostile`), `EV_CONVOY_HIT`/`EV_CONVOY_LOST`(호위 대상 중 지상·해상 = 콘보이, 공중 = 수송기 EV_ESCORT_*), `EV_ALLY_SMOKE`. 새 상황 기본 대사는 Bake가 씬의 대사 표 끝에 더한다(확률·쿨타임 배열도 늘림). 슈퍼에이스는 대사별 음성(`ClipSpawn/Salvo/Decoy/Half/Down`) — 있으면 `SayVoice`. **미사일 빗나감 대사**는 터질 때가 아니라 살아 있는 표적을 시커가 놓친 순간(UnlockTime 연속 무력화·시커 범위 밖)에, 플레어에 속으면 기존 DECOYED.
- **연막**(`ACO_FlareDispenser.SmokeMode`, 지상·해상 호위 유닛): 같은 반응 규칙으로 뿌리고, 미사일이 끌려가는 지점이 뿌린 자리에 머문다. 켜진 동안(`FlareActiveTime` — 6~8초 권장) `IsObscured()` — 적 NPC 기총(ACO_NPCGun)은 조준·사격·피해를 멈추고 NPC 미사일 런처는 락온을 잃는다. FlareParticles에 연막 파티클을 넣는다. **적 기총에 조준당할 때도** 뿌린다: 적 NPC 기총(ACO_NPCGun)이 이 유닛을 유효 각도 안에 넣는 순간(조준 체류 시작 — 조준 한 번에 한 번, 피해는 체류 0.5초 뒤부터라 그 전에) `ReportGunThreat` → 오너가 다음 확인(0.2초) 때 `GunSmokeChance`(0.5)로 굴린다. 횟수·간격(FlareSalvos·FlareCooldown)은 미사일 대응과 같이 쓴다. 기총 피해 판정이 원래 표적 오너 화면에서만 돌고 표적 오너 = 연막을 정하는 쪽이라 네트워크가 더 필요 없다(피해 감지 방식보다 가볍고, 맞기 전에 반응한다). 플레어(SmokeMode 꺼짐)는 기총에 반응하지 않는다.
- **기총 피해 강화**(진행도 `UP_GUN` = 기체 탭 6번째, 저장 ID `gun_damage`, 단계당 +8%, 28항목 — Bake가 27항목 배열을 저장 ID로 옮긴다): SAV 기총 피해는 탄 오브젝트에 묶여 플레이어별로 바꿀 수 없어서, 기총 탄 파티클(충돌 메시지를 보내는 것 — 조종사 화면에서만 충돌)에 `ACO_GunDamageBonus`를 붙여 같은 충돌에 '10 ÷ 표적 장갑'(또는 'd:값')의 (배수 − 1)만큼 덤 피해를 준다. Bake가 플레이어 기체의 탄 파티클에 붙이고 강화 확장(`GunBonuses`)에 연결한다.
- **아군기 표시**(`ACO_TargetMarkers.Flow`, Bake 연결): 다른 플레이어가 탄 참가자 기체에 파란 테두리(아군 항공기 모양) + `WINGMAN 칸 번호`(`WingmanLabel`), 목표처럼 멀리서도 보인다(`ShowWingmen`).

### 6-29. 공격 헬리콥터 / 곡사포 (ACO_MobileUnit.Kind — 지상 풀)

- **공통**: 전차와 같은 이동 유닛 스크립트(`ACO_MobileUnit`, 예전 이름 ACO_Tank)를 쓴다 — 서버 시간 항로(동기화 없음), 생성기 계약, 지상 풀(Bake가 ACO_MobileUnit로 모은다). `Kind` 0 전차 / 1 헬리콥터 / 2 곡사포. 생성기는 종류별 후보(`CollectFreeMobilesOfKind`)로 나눠 놓는다.
- **헬리콥터**(`NPC_Helicopter`): 높이만 '지면 또는 해수면(`SeaLevel`) + `HoverHeight`(70m)'. 0.2초마다 바로 아래와 `HeliLookAhead`(120m) 앞의 지면 중 높은 쪽을 따라 절벽 앞에서 미리 오르고, 아래 지형이 없거나 해수면보다 낮으면 해수면 기준 — 바다·절벽 위로 다닌다. 지면 + 높이의 절반 아래로는 내려가지 않는다. 전진 중 기수 `HoverPitch` 숙임. 격추되면 돌며 중력으로 떨어져 지면/해수면에서 멈춘다(늦게 들어온 플레이어는 바닥 잔해). 턱 밑 기관포(ACO_AAGunSite)가 아군 지상·공중(26·25)을 쏜다. 락온 마커는 적 공중(29) — 공대공 미사일로 잡고, 표적 마커도 항공기 모양.
  - 생성기: 순찰 소대를 `HelicopterPlatoonShare` 확률로 헬리콥터 편대(`HelicopterGroupSize`, 고리 반경 2배, 바다·급경사 허용 — `SampleHeliPoint`, 길 막힘 검사 없음), 호위 매복조를 `HelicopterAmbushShare` 확률로 헬리콥터.
- **곡사포**(`NPC_Artillery` + 포탄 `ACO_ArtilleryShell`): 대공포 스크립트 대신 `ACO_ArtilleryGun`. 표적 = 생성기 호위 대상 중 아군 지상·해상(콘보이·함선, 공중 수송기 제외), `MinRange`~`MaxRange`의 가장 가까운 것. 탄착 예고 없음(플레이어가 아니라 호위 대상을 노린다).
  - 오너가 표적 진행 방향 × 속도로 비행 시간만큼 앞을 예측해(3회 반복) 고각 탄도(tanθ = (v² + √(v⁴ − g(gx² + 2yv²))) / gx)를 풀고, `FireInterval`(+지터)마다 `Spread` 안 무작위 탄착점으로 `NetFire(초속도)`를 모두에게 보낸다. 모든 클라이언트가 같은 초속도로 포탄(ACO_Ordnance, 중력 × `ShellGravityScale` 3 — 4km 비행 약 27초)을 쏜다.
  - 범위 피해는 오너 화면에서만, `ACO_Ordnance.HitFriendlies` — 아군 호위 대상만 맞고 적 유닛은 맞지 않는다(플레이어 폭탄은 반대로 아군을 건너뛴다).
  - 생성기: 매복조를 `ArtilleryAmbushShare` 확률로 곡사포(지상·해상 호위만) — 항로 옆 `ArtilleryStandoffMin`~`Max`(2.5~4.5km)의 마른 지면, 출발점은 `ArtilleryApproachDistance`(400m) 뒤 → 짧게 와서 멈춘 뒤에만 쏜다(`ACO_MobileUnit.IsStopped`). 순찰에는 쓰지 않는다.
  - Bake가 곡사포의 `Generator`를 연결한다.

### 6-30. 레이더 — HUD 레이더 / 조종석 지도 (로컬 전용, 네트워크 없음)

- **접촉 목록**(`ACO_RadarContacts`, 표적 마커 아래 `RADAR`, 씬에 하나): 생성기 풀(전투기·지상·고가치 표적·호위 대상) + 참가자 기체를 처음에 한 번 훑고, 0.5초마다 배치·생존·종류(적/목표/아군 × 공중/지상 — 지상 풀 헬리콥터는 공중)를 다시 본다. 위치는 그리는 쪽이 트랜스폼을 바로 읽는다. NPC 미사일은 런처(`ACO_MissileLauncher.Radar`, Bake 연결)가 쏠 때 `AddMissile` — 날고 있는 동안만(`ACO_Missile.IsFlying`). `LocalVehicle` = 내가 탄 참가자 기체(`SaccEntity.InVehicle`). 아무 기체에도 안 탔고 미리보기도 아니면 쉰다.
- **HUD 레이더**(`ACO_HudRadar`): 기체에 탔을 때 시야 왼쪽 아래(`HeadOffset`, 점수 패널과 같은 거리)에 원형 스코프. 반경 `Range`(표시 거리 — HUD 설정 RANGE로 2~10km, 1km 단위, 최대 `MaxRange` 10km) 안의 **적(노랑)·목표(빨강)**·아군(파랑)·적 미사일(흰색 — 진행 방향으로 긴 막대. 예전 주황은 노랑 적과 헷갈려 바꿨다, Bake가 예전 주황만 바꾼다), 나(흰 쉐브론). 항공기는 진행 방향 **쉐브론**(뒤가 V로 파인 화살촉 — 삼각형은 앞·옆이 헷갈려서 바꿨다, 정점 4개라 점 한 칸 그대로), 지상·함선은 사각형. 점 크기 `BlipSize` 스코프 지름의 4.5%(예전 7% — 10km 스코프에서 점 하나가 1.4km라 편대·지상 유닛이 뭉쳐 보였다), 미사일 막대 길이 `MissileSize` 3.5%(폭은 30%), HUD 설정 BLIP SIZE로 ×0.5~2(`BlipScale`, 북쪽 표시는 그대로). 색을 바꾼 건 목표가 가장 눈에 띄게(빨강) — Bake가 예전 기본값(적 빨강·목표 노랑, 점 7%·미사일 5%)만 바꾸고, 조종석 지도(`ACO_CockpitMap`) 색도 같이 바꾼다. **바라보는 방향이 위** — 점은 북쪽 기준으로 그리고 판(Spin)을 머리 방위만큼 매 프레임 돌린다(테두리 N 표시 = 북쪽, `HeadingFromVehicle`이면 기체 기수). 점 메시는 0.1초마다 정점만 다시 채운다(오브젝트 수 고정). 항상 맨 위(ZTest Always).
  - HUD 설정 패널 `RADAR` 카드(항목 19~23, 저장 ID `radar`/`radar_size`/`radar_x`/`radar_y`/`radar_alpha`): 켜기, 크기, 좌우, 상하, 불투명도(20~100% — 배경·고리·점 머티리얼 알파에 곱한다, `Renderers`). 패널 판이 2.8m × 1.75m로 커졌다(프리팹 다시 만들기).
  - **HUD 따라오는 지연**(SCORE PANEL 카드 FOLLOW DELAY, 항목 24, 저장 ID `hud_follow`): 0~0.5초(0 = 바로 붙음). 점수 패널·결과 글자·무전 자막·HUD 레이더의 `FollowSharpness` = 1 ÷ 지연. 기본 = 점수 패널 값(10 → 0.1초).
- **조종석 지도**(`ACO_CockpitMap`, 기체마다, 메뉴 `ACO → Player Vehicle → Add Cockpit Map to Selected Vehicles`): **북쪽이 위인 고정 방위**로 공역 전체 — 바다 바탕 + 육각 지형 윗면(높이대 색, 처음 탔을 때·지형이 바뀔 때 프레임마다 나눠 만든다) + 공역 테두리 + 적·목표·아군 + 나. 내가 이 기체에 타고 있을 때만 화면(Screen)을 켜고 0.25초마다 갱신한다(다른 기체 화면은 꺼짐 — 테두리만). 깊이 검사(조종석에 가려진다), 겹침 순서는 렌더 큐. 위치는 조종석 의자 앞 대략 자리에 만들어지니 계기판 자리로 옮긴다.
- 셰이더 `ACO/RadarVertexColor`(정점 색 × _Color, `_ZTest`), 메시·머티리얼 `Generated/ACO_Radar*`.

### 6-31. 강화 10단계 / 시커 범위 이름 정리

- **최대 단계 10**(`LevelCostScale` 길이). 단계당 값 = (최대 배수 − 1) ÷ 10:
  - ×2.5: 피해, 락온 거리, 폭발 반경, 기총 피해, 연료 효율. ×2: 탄수, 재장전(둘이 곱해지면 화력이 너무 커서 한 단계 낮춤), 추력.
  - ×1.5(성능에 민감): 미사일 선회, 락온 속도, 시커 범위. 체력 ×4. 기동성 ×1.3 — 롤은 다 받고 피치·요는 `ACO_VehicleUpgrades.PitchYawShare`(0.5)만큼만(×1.15). 안정성 ×1.5.
  - 가격: n단계 = 1단계 가격 × `LevelCostScale[n−1]` = 1, 1.4, 1.9, 2.6, 3.5, 4.6, 6, 7.8, 10, 13(합 ≈ 51.8배). 민감한 항목(기동·선회·락온 속도·시커)은 1단계 가격부터 높다.
  - Bake(`MigrateUpgradeArrays`)가 씬 진행도의 예전(5단계) 배열을 새 기본값으로 바꾼다. 산 단계(저장 값)는 그대로, 효과만 새 단계당 값으로.
  - 상점 단계 칸은 10칸이라 작게·고정 폭(`<size=78%><mspace=0.5em>`).
- **추력 ×2 = 최고 속도 거의 ×2**: SAV 공기 저항은 속도에 비례(`AirFriction × 속도`)라 최고 속도가 추력에 비례한다(음속 부근 `SoundBarrierStrength` 혹만 예외). 양력은 속도² × `Lift`이지만 `MaxLift`에서 막혀서 빨라져도 하중(G)은 그 위로 안 오르고 선회율(도/초)은 오히려 준다.
- **특수 대지(SP. AGM) 탭 6항목**: 피해·탄수·폭발 반경·**락온 속도(×1.5)·락온 거리(×2.5)**·재장전 — 대공 계열처럼 락온 시간 ÷ 배수, `LockRange` × 배수(`ACO_VehicleUpgrades`가 이미 모든 계열에 넘기던 값 — 대지 계열은 항목이 없어 1이었다). 새 저장 ID `agm_lock_speed`·`agm_lock_range`(1단계 2,500·1,500), 항목 수 28 → 30(특수 폭탄 탭은 26~29로 밀린다 — 저장은 고정 ID라 산 단계는 그대로). Bake: 진행도 강화 배열 길이가 다르면 새 기본값으로(`MigrateUpgradeArrays`), 상점 강화 아이콘 수가 다르면 다시 채운다. 선회·시커 범위는 특수 대지에 넣지 않았다. 특수 폭탄(LGB·LGR 포함)은 그대로 4항목.
- **시커 범위 ≠ 락온 각도**: 강화 `SEEKER CONE`(상수 `STAT_SEEKER`)은 발사된 미사일의 추적 각도 `ACO_Missile.MaxTrackingAngle`을 키운다. 무장의 `LockAngle`(HUD 탐색 원 — 락온이 걸리는 각도)은 강화하지 않는다. 저장 ID `msl_seeker`/`aam_seeker`(예전 `*_lock_angle`에서 바꿨다 — 그 항목에 산 단계는 0부터 다시), 아이콘 `aco_icon_seeker`.
- **SAV 리미터 권장값**(DFUNC_Limits — `Limits`가 하중 방향 피치 입력과 요 입력을 줄인다):
  - 받음각: `AoALimiter`(완전히 막는 각) ≤ 실속각 `MaxAngleOfAttackPitch` × 0.65, `AoALimiter_Begin` = 그보다 5~6° 낮게. SF-1(실속 25°)이면 Begin 11 / Limit 16(기본 10/14도 안전). 기동 강화로 피치가 세지면 순간 넘침이 커지므로 구간(Limit − Begin)을 좁히지 말 것.
  - 하중: `GLimiter_Pos` ≈ `MaxGs` × 0.9, `GLimiter_Begin_Pos` = 그보다 6~8 낮게(SF-1 MaxGs 40 → 28 / 36). 음의 G는 -1(=양과 같게) 그대로.
  - `DefaultLimitsOn` 켜기, 저속 실속은 리미터가 못 막는다(받음각을 줄이면 양력이 모자라 가라앉는다) — 안정성 강화(`Lift` ×1.5)가 실속 속도를 낮춘다.

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
    - **LCAC** `NPC_LCAC`(새 이동 방식 `MOVE_HOVER` — 바다 위는 `HoverSeaLevel`, 해변에 올라서면 지면, `ROLE_VIP` = 지켜야 하는 대상, 30m/s, 체력 450) `LandingCraft`(3) — 상륙함 자리에서 횡대(`LcacSpacing` 70m)로 바다를 건너 해변(상륙 지점 안쪽 `LcacBeachInland` 60m)에 올라가 멈춘다.
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

### 6-36. 보너스 페이즈 모듈

> `ReplacesBuiltin`·변환 메뉴 언급은 중간 단계(6-38에서 지움).

- 모듈의 `BonusOnly`를 켜면 보너스 전용: 일반 무작위 선택에서 빠지고, 본 미션 클리어 뒤 보너스(관리자 `BonusPhaseChance`, `BonusMinPhases`)로만 나온다.
- 보너스 종류 고르기(관리자 `PickBonusType`, 오너): 후보 = 기본 슈퍼에이스(대신하는 모듈이 없고 풀에 쉬는 슈퍼에이스가 있으면 가중치 1) + 보너스 모듈(`Enabled`, `MinPhase` ≤ 본 미션 페이즈 수, 가중치 `Weight`). 가중치로 뽑고 모듈은 `IsFeasible`로 확인해 안 되면 빼고 다시. 고른 종류는 동기화 값 `_bonusType`에 남고 다음 페이즈 시작 때 그 종류로 시작한다.
- 보너스 공통 규칙은 관리자가 본다: 모듈 `BonusTimeLimit`(초)이 지나면 물러남(태그 철수, 보너스 없이 미션 클리어 — 배너 "PHASE n FAILED / 모듈 FailText", 무전 페이즈 실패 대사), 모듈이 `RESULT_FAIL`을 돌려줘도 물러남, `RESULT_REPLACE`면 건너뜀. 성공 보너스 = (`PhaseClearBonus` + `GetClearBonus()`) × `BonusScoreMultiplier`. 보너스 페이즈엔 시작 증원 웨이브·교착 웨이브가 없다. HUD 남은 시간은 모듈 `BonusTimeLimit`.
- 슈퍼에이스 보너스 = `ACO_PhaseSuperAce`(동기화 없음, `ReplacesBuiltin` = PHASE_BONUS) — 변환 메뉴가 관리자 `BonusEscorts`/`BonusTimeLimit`/`BonusClearBonus`와 무전 보너스 대사(시작·성공·페이즈 실패 칸)를 옮겨 만든다. 이름 `BONUS - {ACE}`(슈퍼에이스 이름으로 바뀐다).
- 새 보너스 종류는 `ACO_PhaseModule`을 상속해 `BonusOnly`를 켜고 관리자 아래에 두면 된다(Bake가 모듈 목록에 넣는다). 다음 후보: 에이스 편대, 스텔스·전자전 에이스, 공중 요새·포대 요새 등 슈퍼웨폰.

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

### 6-39. 특수 대공 무장 3종 — FLAK · CCAA · ADAM

- **FLAK**(무유도 근접 신관 대공 로켓, 9000): 로켓 탄체(`ACO_Rocket`, `ACO_Ordnance`)를 누르고 있는 동안 0.12초마다 연사한다(슬롯 6 / 60발 / 3초, 피해 90, 범위 20m, 900m/s). 락온이 없어 앞을 겨눠 쏜다. 탄이 지나가는 구간에서 적 항공기 락온 마커(레이어 29) 중심이 12m 안에 들면 가장 가까이 지나는 점에서 터진다. 몸체에 닿아도 터진다. 강화는 특수 대공 탭(피해·탄수·장전).
- **CCAA**(Counter-Countermeasure AAM, 16000): 플레어·채프에 속지 않고 노칭에도 표적을 놓치지 않는다(`IgnoreCountermeasures`). 대신 락온이 느리고(2.5초) 좁고(30도) 덜 돈다(70도/초). 슬롯 2 / 12발 / 12초, 피해 200.
- **ADAM**(Area Denial Air Munition, 18000): 파일런에서 투하(0.8초 — 모기체 속도 + 중력, 엔진 꺼짐)한 뒤 점화해 느리게(최고 300m/s, 적기 최고 260m/s보다 조금 빠름) 쫓는다. 락온했으면 그 표적, 아니면 4km 안에서 가장 가까운 적을 방향과 상관없이 고른다(전방위 시커 180도). 적 항공기가 35m 안을 지나면 표적이 아니어도 터지고, 범위 피해 120m(피해 400, 가장자리 50%)로 편대를 한꺼번에 친다. 날아가는 중 표적이 격추되면 그 자리에서 가장 가까운 적으로 옮긴다. 플레어에 속아도 플레어 곁에서 터지므로 범위 안의 표적은 맞는다. 슬롯 1 / 6발 / 18초, 수명 35초.
- **예제 기본값 = F/A-18 설정**: 예제 13종의 성능·미사일 모델은 `Assets/Pit-bool/FA-18E Main_ACO.prefab`의 특수무장 모듈 값을 옮겨 적은 것이다(`ACO_SpecialWeaponCatalog` 프리셋, 4AAM·UGB·RKT는 `ACO_PlayerMissileSetup`). 유도는 모두 직접 추적(0), 미사일 모델은 4AAM = MRAAM, SASM·QAAM = SRAAM, 4AGM = AGM, LAGM = LAGM, 나머지 대공 = AAM이다. FLAK·CCAA·ADAM은 F/A-18에 없어 같은 기준(피해 200~400, 속력 900, 락온 2km·45도·1.5초)에 맞췄다. 기체에서 값을 다시 맞추면 프리셋도 고친다.

- **구현** — 무장 모듈(`ACO_DFUNC_Missile`) [특수 동작] 칸. 강화와 무관해 발사 이벤트로 보내지 않고, 받는 화면마다 자기 무장 값을 쓴다. 풀은 무장마다 따로라서 발사마다 넣어도 다른 무장에 섞이지 않는다.
  - `IgnoreCountermeasures` → `ACO_Missile`이 플레어·채프 판정과 노칭을 건너뛴다.
  - `ProximityAnyEnemy` → 미사일과 로켓 모두 '아무 적 항공기' 근접 신관. 이동 구간 가운데에 구 하나(`OverlapSphereNonAlloc`, 반경 = 신관 + 구간 절반)를 두고 레이어 29 트리거를 모은다. 그다음 마커 중심까지의 선분 최단 거리로 판정한다. 마커 부모의 `SaccTarget`이 살아 있는 적만 세고, 아군 호위 대상과 떼어 낸 슈퍼에이스 기만체는 뺀다. 미사일은 표적을 놓친 뒤에도 동작한다. 피해는 범위 피해로 준다(유도 미사일은 `MissileSplash`).
  - `AutoTargetNearest` → 락온이 없으면 가장 가까운 표적 번호를 정해 보낸다(모든 화면이 같은 표적). 표적이 격추되면 `RetargetPool`(무장의 Targets)에서 다시 고른다 — 화면마다 따로 고르지만 피해는 쏜 사람 화면만 준다.
  - `DropTime` → 콜드 런치(사출 속도 0, 기수 고정, 중력 1). `SeekerAngle` → 시커 범위 기본값(강화 SEEKER CONE이 키운다), `MissileLifetime` → 수명.
- 기존 근접 신관(락온 표적 중심까지 거리, 표적을 놓치면 꺼짐)은 그대로다. 예제 정의는 프리팹 생성 창 "플레이어 특수무장"(예제를 모두 다시 만든다) 또는 새 메뉴 `ACO → Weapons → Add Missing Special Weapon Examples`(없는 것만)로 만든다. 아이콘은 `aco_icon_weapon_flak/ccaa/adam`.

### 6-40. CIWS 미사일 요격(구역 방어, 시간 방식) + LASM

- **대상**: 유도 미사일(`ACO_Missile`)만. 폭탄·로켓(`ACO_Ordnance`)은 막지 않는다. 표적을 놓친(플레어·빗나감) 미사일도 위협이 아니라 막지 않는다.
- **판정은 미사일 쪽**: 플레이어 미사일은 위치를 동기화하지 않고 화면마다 재현되므로, CIWS가 미사일을 찾지 않고 미사일이 CIWS를 확인한다.
  - 미사일은 0.1초마다 런처가 넘겨준 요격 CIWS 목록(`InterceptSites`)에서 자기를 덮는 CIWS를 고른다(`ACO_AAGunSite.CanIntercept`). 덮는다 = 살아 있음, 지키는 레이어(`InterceptProtectLayers`)의 표적, 미사일이 요격 사거리 안, 표적이 자기 유닛이거나 방어 반경(`DefenseRadius`) 안(함대 방어).
  - 매 프레임 맡은 CIWS 하나에 `InterceptTick`. CIWS는 한 번에 한 발만 상대한다(다른 미사일을 쏘는 중이면 거절 → 미사일은 다음 CIWS). 필요 시간 = (1초 넘게 쉬었으면 반응 시간 +) 격추 시간, 자기 유닛이 아닌 표적이면 × `OtherDefenseFactor`. 다 차면 `Intercept()` — 피해·범위 피해 없이 터진다.
  - 동기화 0개: 미사일 궤적·함선 위치(서버 시간 순항)·CIWS 생사(SaccTarget 체력)가 모든 화면에서 같다. 결과는 피해 판정자 화면 — 플레이어 미사일 = 쏜 사람, NPC 미사일 = 맞는 쪽.
  - CIWS 포탑은 요격 중이면 비행기보다 미사일을 먼저 겨누고 예광탄만 쏜다(`ACO_NPCGun.InterceptFire`, 피해 없음). 잠들어 있어도 깬다(LAGM 사거리 6.5km > 잠재우기 거리).
- **기본값**(`ACO_SceneBaker.ApplyInterceptDefaults` — 프리팹 빌더와 Bake가 한 번 넣고, 그 뒤 직접 바꾼 값은 그대로):
  - 적 CIWS(함선 부위, MarkerLabel "CIWS"): 적 항공기·지상(29, 30)을 노린 미사일, 사거리 1.2km, 방어 반경 600m, 반응 0.5 + 격추 0.5초, 다른 함선 ×2.
  - 아군 CIWS(아군 호위 유닛의 대공포, 이름에 "AAGun" — 상륙전 호위함 2문): 아군 항공기·지상(25, 26)을 노린 적 미사일, 사거리·반경 1.5km, 배수 1.
  - Bake가 요격 CIWS 목록을 모든 무장(`ACO_DFUNC_Missile`)·NPC 런처(`ACO_MissileLauncher`)에 굽는다.
- **시뮬레이션**(`Tools/aco_ciws_sim.py`, 400회): LAGM 1발 → 전함 0/1 명중, 4AGM → 전함 2/4, → 구축함 3/4, HVAA(1900m/s) 1/1, LASM 2/2. 혼자 쏘면 모든 화면의 명중 수가 같다(일제 발사에서 어느 한 발이 떨어지는지만 다르다 — 같은 미사일이라 티가 안 난다). 여러 명이 동시에 같은 함선을 쏘면 구경꾼 화면의 명중 수가 실제(판정자 기준)와 다를 수 있다(피해는 맞다). '표적에 가까운 미사일부터' 순서도 시험했지만 차이가 없어 쓰지 않았다.
- **무전**: `EV_MISSILE_INTERCEPTED`(내 미사일이 요격됨, 쏜 사람), `EV_CIWS_SAVE`(나를 노린 미사일을 아군 CIWS가 막음). Bake가 씬 무전 표에 대사를 더한다.
- **LASM**(대함 미사일, 12000): LAGM과 같은 사거리·피해(6.5km, 400), 범위 피해가 작다(10m — LAGM 100m). 표적이 함선(`ACO_Warship` — 부위를 노려도)이면 시스키밍: 해수면(함선 기준점) + 8m를 겨눠 최대 12도로 완만히 내려가고(600m 앞 목표 고도를 겨눠 해면에 꽂히지 않는다), 앞쪽 지형이 높으면 40m 위로 넘는다. 수평 500m 안에서 보통 유도로 꽂힌다. 함선을 노리는 동안 격추되지 않지만 CIWS는 그대로 쏜다 — 사거리 안에 있는 동안 CIWS 한 문을 붙잡으므로(`InterceptTick`의 `noKill`), 그 틈에 다른 미사일이 들어간다. 무장 칸 `SeaSkim`·`SkimAltitude`·`SkimTerminalDistance`.

### 6-41. 유도 폭탄 LGB · 유도 로켓 LGR

- 무유도 UGB·RKT와 같은 탄체(`ACO_Bomb`, `ACO_Rocket` — `ACO_Ordnance`)와 같은 피해·연사·탄수. 차이는 셋: 지상 표적(레이어 30) 락온으로 유도된다, 장전이 느리다, 범위 피해가 좁다.
  - **LGB**(11000): 락온 5km·45도·1초, 선회 초당 10도, 장전 10초(UGB 4초), 범위 60m(UGB 200m), 표적 근접 폭발 5m. CCIP 표시는 그대로(락온 없이 떨어뜨리면 UGB처럼 자유 낙하).
    - **락온 기준 = CCIP**(`ACO_DFUNC_Missile.LockFromCcip`, 기본 켬): CCIP가 보이는 동안 락온 탐색 원의 가운데가 기수가 아니라 **눈 → CCIP 지점 방향** — 폭탄이 떨어질 자리 둘레(45°) 표적을 잡는다(내려다보며 수평 비행해도 앞쪽 먼 표적이 아니라 떨어질 곳의 표적). CCIP가 안 보이면(지상·계산 실패) 기수 방향. CCIP가 없는 무장(미사일·로켓)은 영향 없음.
  - **LGR**(11000): 락온 3km·30도·1초, 선회 초당 30도, 0.15초 연사(누르고 있으면 락온한 한 표적에 연달아), 장전 8초(RKT 3초), 범위 15m(RKT 50m), 표적 근접 폭발 4m. 락온 없이 쏘면 RKT처럼 곧게.
- **유도 방식**(`ACO_Ordnance.Steer`): 계산 간격(0.02초)마다 속도 방향을 표적 쪽으로 `GuideTurnRate`만큼 돌린다(속력은 그대로 — 로켓은 추진, 폭탄은 중력이 계속 더한다). 속도 방향과 표적 방향이 시커 범위(무장 `GetSeekerAngle`, 기본 90도)보다 벌어지거나 표적이 사라지면 유도를 끊는다. 표적 마커 중심이 이동 구간에서 근접 반경 안이면 거기서 터진다(범위 피해로 맞힌다).
- **동기화**: 발사 이벤트 `NetLaunchOrdnance`에 표적 번호·선회율·시커 범위를 더했다(7칸). 모든 화면이 같은 표적을 쫓고, 피해는 쏜 사람 화면만. 무유도 무장(UGB·RKT·FLAK)은 락온이 없어 표적 없음으로 보낸다(그대로 무유도).
- 폭탄·로켓이라 CIWS 요격 대상이 아니다(6-40 — 유도 미사일만). 강화는 특수 폭탄 탭(피해·탄수·폭발 반경·장전).

### 6-42. 지면·수면 폭발 연출, 새 무장 자동 설치, 상점 구매 버튼

- **지면·수면 폭발**(`ACO_Missile`, `ACO_Ordnance` — 플레이어·NPC 미사일, 폭탄·로켓, 곡사포탄 모두): 탄 프리팹의 [지면·수면 폭발] 칸 `GroundExplosionObject`·`GroundExplosionSounds`, `WaterExplosionObject`·`WaterExplosionSounds`, `WaterLayers`(기본 Water 4).
  - 닿은 콜라이더가 물 레이어면 물, 유닛 히트박스(17)면 공중과 같은 연출, 그 밖이면 땅. 표적 명중·근접 폭발·공중 폭발(수명)·CIWS 요격은 늘 `ExplosionObject`. 칸이 비면 `ExplosionObject`·`ExplosionSounds`(예전 그대로).
  - 땅·물 연출 오브젝트는 위(+Y)를 닿은 면의 법선으로 돌린다(물기둥·흙먼지가 똑바로 선다). 탄의 자식으로 두고 꺼 둔다(발사·회수 때 끈다).
  - 미사일은 이제 물 레이어에도 닿으면 터진다(예전엔 지형 레이어만 — 바다에 떨어지면 물속으로 들어갔다).
  - **물은 트리거까지 본다**: 씬 바다(`Water`, 레이어 4)는 SAV 기체가 뜨도록 **트리거 상자**(수면 0 ~ -200m)다(SAV_FloatScript도 트리거까지 검사). 그래서 땅·유닛은 트리거를 빼고(락온 마커 등), 물(`WaterLayers`)은 일반 콜라이더와 트리거를 모두 **따로** 검사해 가까운 쪽을 쓴다 — 물 레이어엔 바다만 있어 다른 트리거는 안 걸린다. 적용: 미사일 충돌(`ACO_Missile.CheckHit`), 폭탄·로켓·포탄 충돌(`ACO_Ordnance.Step`), CCIP 낙하 지점(`ACO_DFUNC_Missile.ComputeCcip`), 물보라 수면 찾기(`ACO_WaterFx.Probe`). 예전엔 모두 트리거를 빼서 수면을 지나쳐 해저에서 터지고 물보라가 안 나왔다. (생성기의 물 판정은 해수면 높이 검사가 같이 있어 그대로 동작한다.)
- **새 무장 자동 설치**: Bake(`ACO_SpecialWeaponCatalog.InstallForBake`)가 ① 코드에 새로 더해진 예제 정의(파일이 없고 씬의 어느 격납고 목록에도 없는 ID)를 만들고 ② 기체 특수무장 슬롯에 없는 모듈을 넣고 ③ 격납고 목록을 맞춘다. 일부러 지운 예제는 격납고 목록에 ID가 남아 다시 만들지 않는다. 메뉴 `ACO → Bake NPC References`가 다시 보인다(예전엔 메뉴 표시가 매개변수 있는 함수에 붙어 있어 메뉴가 등록되지 않았다).
- **상점 구매 버튼**(`ACO_Shop`): 강화·소모품 줄을 누르면 고르기만 하고(줄 강조), 아래 구매 버튼(`BuyButton`, 버튼 동작 5 = 상점 구매)에 "BUY 이름 LV3  4,000 CR"이 뜬다 — 눌러야 산다. 고른 줄은 남아 있어 다음 단계는 구매 버튼만 다시 누르면 된다. 탭을 바꾸면 고른 줄이 풀린다. 구매 버튼이 없는 예전 격납고 프리팹이면 줄을 누르자마자 산다(그대로 동작).

### 6-43. 공격기 (지상·해상 호위 전용 적기)

- **프리팹** `NPC_Attacker`(프리팹 생성 창 → 공격기): 일반 NPC 전투기(`CreateFighterObject`)의 바리에이션. 아군 지상 레이어(26 — 콘보이 차량·LCAC·호위함)만 노리고(`TargetLayers`·`AAMTargetsLayer`), 대지 미사일(`ACO_Missile_AGM`, 없으면 AAM, 4발·재장전 30초·락온 2.5초)과 기총(틱 피해 8)으로 치고 빠진다(이탈 300m → 재진입 1.8km, 탐지 6km). 플레이어는 노리지 않지만 락온 마커는 적 항공기(29)라 잡을 수 있다. 체력 150, 순항 120·최고 220m/s(전투기보다 느리다), 플레어 2번. 모델은 올리브색 상자 + 직선 날개(바꿔 끼운다).
- **배치**: `ACO_NPCFighterAI.GroundAttacker`가 켜진 기체는 생성기가 일반 배치(초기 CAP·증원·편대 목표·호위 요격·슈퍼에이스 호위)에 쓰지 않는다(슈퍼에이스와 같은 예약). 새 목표 종류 `OBJ_ATTACK`으로만 2기 편대로 나온다.
  - **지상 호위**(`ACO_PhaseConvoy`)와 **상륙전**(`ACO_PhaseAmphibious`)이 시작할 때 `AttackersBase`(2) + `AttackersPerPhase`(1) × (페이즈 번호 − 1)기를 부른다 — 콘보이는 항로 중심, 상륙전은 상륙 지점(항구·해안선)에서 `AttackerDistanceMin~Max`(6~9km) 떨어진 곳에서 들어온다. 풀에 공격기가 없으면 부르지 않는다.
  - 생성기 풀(UnitPoolRoot) 아래에 4~6기 두면 Bake가 전투기 목록에 넣는다.
- 태그 없는 배치(공격기·호위 요격 편대·폭격기 호위)는 이제 지금 페이즈 목표의 보호(`_protectTag`)를 풀지 않는다(예전엔 0으로 덮어 증원이 파괴된 목표 유닛을 다시 쓸 수 있었다).
- 아군 CIWS(호위함 대공포, 6-40)는 공격기의 대지 미사일도 막는다(아군 지상 26을 지킨다).

### 6-44. 물 연출 — 저공 물보라, 함선 항적, 물속 시야

- **설치**: 메뉴 `ACO → Environment → Add Water Effects (Spray, Wake, Underwater)`(`ACO_WaterFxBuilder` — 다시 눌러도 없는 것만 더한다). 프리팹은 내용을 열어 고치므로 바꿔 끼운 모델은 그대로다. 프리팹 빌더도 새로 만드는 미사일·전투기·함선에 같은 것을 붙인다.
- **저공 물보라**(`ACO_WaterFx` MODE_SPRAY, 자식 `ACO_WaterSpray`): 미사일 프리팹(수면 15m 안, 100m/s 이상 — 시스키밍 LASM은 내내), NPC 전투기·공격기·슈퍼에이스 프리팹과 씬의 플레이어 기체(수면 30m 안, 50m/s 이상).
  - **프리팹으로 일괄 설정**: 물보라는 `Prefabs/ACO_WaterSpray`(기준 — 기체용: 수면 30m·50m/s, 초당 최대 320개)의 인스턴스이고, 미사일은 그 변형 `ACO_WaterSpray_Missile`(수면 15m·100m/s, 초당 최대 240개, 작은 크기만 다르다)을 쓴다. 기준 프리팹에서 파티클 모양·색·수명 등을 고치면 기체·미사일 물보라가 모두 바뀐다(변형이 따로 바꾼 칸은 빼고). 예전 방식(프리팹이 아닌 물보라)은 메뉴를 다시 누르면 프리팹으로 바꾼다.
  - 0.15초마다 아래로 짧게 Raycast(물 레이어 4 + 지형) — 물이 먼저 맞을 때만. 방출기를 매 프레임 부모 바로 아래 수면으로 옮겨 위로 세우고, 세기 = 높이(가까울수록) × 속력. 파티클은 월드 공간, 위로 튀었다 떨어진다.
- **함선 항적**(MODE_WAKE, 자식 `ACO_Wake`): 함선 프리팹(전함·구축함, 해상·공기부양 호위 유닛 — 상륙함·호위함·LCAC). 선미 거품(오래 남아 퍼진다, 수면에 눕는 파티클) + 선수 물결. **거리 비례 방출**이라 움직일 때만 생기고 스크립트가 할 일이 없다 — 순간이동(풀 배치·부활)할 때 옛 자리에서 그어지는 줄만 지운다(한 프레임 60m 넘게 움직이면 Clear). 위치·폭은 루트 콜라이더 경계(루트 = 해수면).
- **물속**(`ACO_Underwater`, 씬에 하나, 로컬 전용): 내 머리가 수면 아래로 0.15m 넘게 들어가면 — 걸어 들어가든 기체가 가라앉든.
  - 수면 높이 = **화면의 너울 높이**: 너울 원판(`ACO_OceanSwell`, Bake가 렌더러를 넣는다)이 있으면 바다 셰이더와 같은 게르스트너 파도 4개(방향·파장·높이·날카로움, `_SwellScale`, `_WaveSpeed`, `_SeaY` — 시작할 때 머티리얼에서 읽는다)를 머리 자리에서 계산한다. 시간은 셰이더 `_Time.y`와 같은 레벨 로드 후 시간. 파도는 수면 점을 옆으로도 옮기므로 머리 아래로 옮겨져 온 원래 점을 두 번 되짚어 그 높이를 쓴다(검증: 기본 너울에서 평균 0.6cm, 최대 9cm 차이 — 안 되짚으면 최대 1.35m). 너울이 없으면 평평한 해수면(Bake = 미션 생성기 `SeaLevel`).
  - 시야: 물빛 안개(밀도 0.03 — 약 50m) + 머리를 감싸는 옅은 물빛 구(`Tint`, `Generated/ACO_UnderwaterTint.mat`, ACO/MarkerOverlay, 큐 3990, 알파 0.2 — HUD(4000 이상)는 또렷).
  - **물빛 벽**(`Backdrop`, 셰이더 `ACO/UnderwaterBackdrop`, `Generated/ACO_UnderwaterBackdrop.mat`): 안개는 물체만 칠하므로 하늘(스카이박스)이 그대로 보이던 것을 막는다. 머리를 중심으로 안쪽 면만 그리는 구를 **지금 안개가 99% 덮는 거리**(Exponential Squared → 반경 = 2.15 / 밀도, 0.03이면 약 72m, 20~3000m로 자른다)에 두고 **지금 안개 색**(`unity_FogColor`) 한 색으로 칠한다. 불투명(Geometry+100, 깊이 씀)이라 그 뒤 하늘·먼 물체는 안 그려지고, 앞의 물체는 안개에 섞여 벽 색으로 이어진다. 물속일 때만 켠다. 하늘을 가려야 하므로 UI 레이어로 옮기지 않는다(Default).
  - **너울 뒷면**: 바다 셰이더에 `_Cull`(평소 Back)과 `_UnderColor`(뒷면 빛)를 두고, 물속 연출이 **내 화면의 너울 원판 머티리얼 복사본**만 물속일 때 `_Cull` = Off로 바꾼다(다른 사람·평소 화면은 그대로). 뒷면(VFACE < 0)은 조명·반사 없이 `_UnderColor`로 스스로 빛난다 — 바로 위일수록 밝고 물결·거품이 조금 비친다. 너울 반경(1200m)이 벽보다 훨씬 커서 평평한 바다는 따로 바꾸지 않는다.
  - 안개는 **구름 필드 하나가 씬 안개를 쓴다**: `ACO_CloudField.SetUnderwater` — 날씨 기본 → 구름 속 → 물속 순서로 겹친다(따로 쓰면 날씨 전환·구름 속 안개와 서로 덮는다). 구름 필드가 없으면 물속 연출이 직접 쓰고 나올 때 되돌린다.
  - 소리: 물속 소리(`UnderwaterLoop`, 반복, 선택)만 켠다. 다른 소리를 먹먹하게 하는 건 뺐다(씬 소리마다 필터를 붙여야 해서) — 그 사이 메뉴가 붙였던 필터(꺼진 채 22000Hz)는 메뉴를 다시 누르면 뗀다.
  - 수면에서 깜빡이지 않게 들어갈 땐 0.15m 더, 나올 땐 수면 위면 된다. 들고 날 때 섞는다(초당 6).

### 6-45. 적 미사일 스쳐 지나가는 소리 (나를 노리고 빗나간 미사일)

- **동작**(`ACO_Missile.PlayFlybyIfNear`, 로컬): 나(내가 조종하는 기체)를 락온해 쏜 적 미사일(`_targetIsMe` — 발사할 때 표적 기체를 이 화면이 조종 중, NPC 미사일만)이 **나를 놓치는 순간**(`LoseTarget` — 지나쳐 시커 범위를 벗어남, 플레어에 속음) 미사일이 내 머리에서 `FlybyRadius`(150m) 안이면 미사일 자식의 3D 소리(`FlybySounds` 중 하나, 음높이 ±)를 한 번 낸다. 매 프레임 계산이 없다 — 놓치는 순간 한 번만 본다. 내가 격추돼서 놓친 경우(표적이 사라짐)와 맞힌 경우(폭발음)는 소리가 없다.
  - 미사일은 음속보다 빨라 지나간 뒤에 들린다 — 그래서 놓친 뒤에 내고, 소리도 지나간 뒤 부분이다.
- **소리**(`Tools/aco_make_flyby.py` 합성 → `Audio/Generated/ACO_Missile_Flyby_1`(15m·520m/s — 짧고 날카롭다) / `_2`(35m·430m/s — 길게 우르르), 1.5초): 직선으로 지나간 미사일의 거리 r(t)로 세기·밝기를 정한다 — 시작하자마자 초음속 충격음(N파 + 밝은 잡음) → 멀어지는 로켓 굉음(중저역 잡음 + 지글거림, 차단 주파수 3500 → 450Hz, 세기 1/r) + 도플러로 낮아진 엔진 음. 소리에 도플러가 들어 있어 AudioSource 도플러는 0.

- **설치**: 메뉴 `ACO → Weapons → Add Missile Flyby Sounds`(Prefabs 폴더의 미사일 프리팹마다 자식 `FlybySound_1·2`, 이미 있으면 건너뜀). 프리팹 빌더가 새로 만드는 미사일에도 붙인다. 플레이어 무장도 같은 미사일 프리팹을 쓰지만 소리는 나를 노린 적 미사일에서만 난다.

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

### 6-48. 하늘 안개 돔 · 비구름층 · 낮은 안개층 · 구름 속 물안개 막

문제: (1) 비가 머리 위에 구름이 없어도 내린다(고도만 본다). (2) 구름에 들어가면 퍼프(빌보드)가 디더링으로 사라지는 게 그대로 보여 판을 뚫는 느낌 — 안개는 더 안쪽(속 타원체)에서야 짙어진다. (3) Unity 안개는 물체만 칠해 짙은 안개·구름 속에서도 틈새 하늘(스카이박스)이 원래 색.

- **하늘 안개 돔**(`ACO_Sky/SkyFog`, 셰이더 `ACO/SkyFog`, `Generated/ACO_SkyFog.mat`): 머리를 감싸는 구를 **가장 먼 깊이(원평면)**에 그려 하늘 픽셀에만 덮는다(지형·구름·바다가 그려진 곳은 깊이에 막힌다). 큐 Transparent-400(스카이박스 다음, 바다 전).
  - 색 = 지금 안개 색, 진하기 = 지금 안개 밀도(`unity_FogParams`)에서 **맑은 날 밀도**(`_BaseDensity`, Bake = 씬 안개)를 뺀 만큼 — 맑은 날은 스카이박스 그대로. 스크립트 없이 날씨·구름 속·층·물속 안개를 그대로 따른다.
  - 시선이 지나는 공기 길이 = 2000m / sin(올려다본 각)(최대 30km): 수평선 쪽은 진하고 머리 위는 옅다. 예: 흐림(×2.2) 수평선 64%·고도각 10° 14%, 구름 속·물속은 하늘 전체가 안개 색.
- **구름층**(셰이더 `ACO/CloudLayer` — 80km 평면, 머리 아래·위로 따라다니고 무늬는 월드에 고정, 큰 얼룩 노이즈가 바람 따라 천천히 흐른다, 불투명). 날씨 `CloudLayer[]`(맑음 0, 흐림 0, 안개 2, 비 1) → `ACO_CloudField.SetLayer`. 바뀔 때 5초 동안 디더링으로 나타나고 사라진다.
  - **비구름층**(LAYER_DECK, 1300~1800m — 바닥은 지금 구름이 시작하는 높이라 그 아래 전투 공간은 그대로, 두께 500m라 몇 초면 뚫는다): 아랫면 판(아래에서만 보이는 짙은 회색 천장)·윗면 판(위에서만 보이는 흰 구름바다). 층 속은 회색 안개(0.001 — 시정 약 2km), **층 위는 맑은 하늘**(날씨 안개를 씬 기본으로 되돌린다). 1800m 위 뭉게구름은 구름바다 위로 솟는다. 비는 층 윗면 아래에서만(Bake가 `ACO_Rain.MaxAltitude` = `DeckTop`).
  - **낮은 안개층**(LAYER_FOG, 400m 아래 안개(0.001 — 830m에서 반쯤, 2.1km면 거의 안 보인다), 400→600m 동안 옅어지고 위는 맑다): 윗면 판(위에서만 보이는 흰 안개 바다)이 땅·바다를 덮고, 400m보다 높은 산봉우리만 솟는다(지형 최고 900m). 지상 목표를 치려면 안개 속으로 내려가야 한다(마커는 보인다).
  - 안개 겹치기 순서(구름 필드 하나가 씬 안개를 쓴다): 날씨 기본 → 층 위(맑음) → 비구름층 속 → 낮은 안개층 속 → 구름 속 → 물속.
  - **부드러운 경계**: 판이 산 사면·뭉게구름·함선을 자르는 선을 흐리게 — 판 셰이더가 깊이 텍스처로 판 뒤 물체의 월드 자리를 되살려 **판과의 높이 차**가 `_SoftDistance`(120m, 얼룩 노이즈로 ×0.6~1.4 흔들어 들쭉날쭉)보다 작으면 투명하게 한다(판 뒤 물체가 안개 사이로 비친다). 시선 방향 거리로 재던 처음 방식은 층 위를 비스듬히(수평으로) 볼 때 시선이 판을 지나 멀리 산에 닿아 차이가 금방 커져 경계가 딱딱하게 남았다. 시선 방향으로 `_SoftRayDistance`(2.5km) 넘게 뒤의 물체는 높이가 비슷해도 덮는다(멀리 낮은 언덕이 비치지 않게). 거의 투명한 자리(알파 0.02 아래)는 깊이를 안 쓴다.
- **구름 속 진입**: 구름 속 안개를 더 일찍·짙게(속 타원체 ×1.6에서 시작해 ×0.9에서 최대, 뭉게구름 속만 밀도 0.006 — 140m에서 반쯤, 250m 90%, 360m 거의 다 가려 퍼프가 사라지는 40~160m를 덮는다. 비구름층·안개층 속은 답답하지 않게 0.001(시정 약 2km) — Bake가 예전 기본값만 바꾼다). 여기에 **물안개 막**(`ACO_Sky/CloudVeil`, 셰이더 `ACO/CloudVeil`, 머리 둘레 반경 1.5m 구): 시선 방향 세 거리의 **월드 고정 3D 노이즈**를 겹쳐 그려, 날면 안개 줄기가 앞에서 다가와 스쳐 간다(VR에서도 거리감이 맞다). 진하기 = 구름 속 정도(뭉게구름·비구름층 속 — 낮은 안개층·물속에선 안 쓴다) × 최대 0.55(비구름층 속은 × `DeckVeilStrength` 0.55로 옅게), 색 = 지금 안개 색. 깊이 검사로 1.5m보다 가까운 조종석은 덮지 않고, HUD는 그 위에 또렷하다.
- 설치: Bake가 ACO_Sky 아래에 없는 것(SkyFog·CloudDeckBottom·CloudDeckTop·FogLayerTop·CloudVeil)을 만든다(`ACO_SkyBuilder.EnsureSkyParts` — Create Clouds and Rain도 같이). 판 색·얼룩·흐름은 각 머티리얼(`Generated/ACO_CloudDeckBottom/Top.mat`, `ACO_FogLayerTop.mat`)에서.

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

### 6-50. 비 보강(캐노피 빗방울·바다 파문·젖은 땅) + 폭풍 날씨(번개·천둥)

- **캐노피 빗방울 — 지금은 끔**(`ACO_Rain.CanopyDrops` 기본 false: 모양이 나빠 쓰지 않는다. 켜면 아래대로)(`ACO_Rain.CanopyRenderer`, 셰이더 `ACO/CanopyRain`, 반경 0.9m 구 — FX 레이어): 기체에 탔을 때(`Contacts.LocalVehicle`) 비 맞는 곳이면 `FadeTime`으로 맺히고, 지붕 아래·비구름층 위로 가면 `CanopyDryTime`(6초) 동안 마른다. 구는 머리 위치(PostLateUpdate — 빠른 기체에서 머리가 구 밖으로 안 나가게) + **기체 자세**로 돌려 방울이 캐노피에 붙어 있다. 좌표 = 기체 기준 좌우 각 × 위아래 각(앞 가운데에 특이점 없음), 칸마다 방울 하나 × 두 겹. 느리면 맺혔다 사라지며 천천히 흘러내리고, 빠를수록(기수 방향 속도 / `_StreakSpeed` 120m/s) 위로 6배까지 늘어나 빠르게 넘어간다(풍압에 밀려 올라가는 물줄기). 앞쪽 반구만, 0.9m보다 가까운 조종석은 덮지 않는다.
- **바다 빗방울 파문**(`ACO/Ocean`): 전역 `_UdonACORain`(날씨 비 세기를 `FadeTime`으로 섞은 값 — 내가 지붕 아래·구름 위여도 날씨 값)이 0보다 크면 300m 안 바다에 격자(1.7m·2.9m)마다 퍼지는 고리 두 겹을 노멀에 더하고 반짝임을 조금 거칠게.
- **젖은 땅**(`ACO/HexTerrain`·`ACO/HexTrees`): 전역 `_UdonACOWet` — 비가 오면 `WetTime`(25초) 동안 젖고 그치면 `DryTime`(60초) 동안 마른다. 지면 32% 어둡게 + 윗면 물기 광택(0.3), 나무 20% 어둡게, 눈은 그대로.
- **폭풍**(`ACO_Weather.WX_STORM` = 4, 비율 0.8 — 비보다 드물게): 비구름층(비와 같은 높이) + 더 어두운 천장(`CloudDarkness` 0.9 → 구름층 아랫면 `_Darken`) + 비 세기 1 + 안개 ×9 + 태양 ×0.3 + **번개**(`LightningAmount` 1 → `ACO_Lightning.SetStorm`). 디버그 패널 WEATHER에도 STORM. Bake가 예전 4칸 날씨 배열에 폭풍 값을 붙인다.
- **번개**(`ACO_Lightning`, ACO_Sky/Lightning — 동기화 없음):
  - 일정: 서버 시간을 `SlotMs`(1.5초) 칸으로 나눠 칸마다 정수 해시로 칠지(`StrikeChance` 0.8)·칸 안 시각·자리(`FieldRadius` 원판)를 정한다 — **모든 클라이언트가 같은 때 같은 자리**에 본다. 지도 전체에 1.5초마다라 내 6km 안에도 십수 초마다 친다. 폭풍이 반 넘게 켜져야(`FadeTime` 5초) 친다.
  - 섬광 — **조명을 새로 켜지 않는다**(포워드 렌더링에선 픽셀 조명 하나가 그 빛을 받는 물체를 모두 한 번 더 그린다 — 번쩍이는 순간 드로우콜이 거의 두 배): 이미 있는 값만 0.5초 동안 펄스 셋(0초·0.11초·0.24초, 둘째·셋째 세기는 번개마다)으로 — 태양 세기 × (1 + 2.5 × 섬광), 뭉게구름 `_Flash`(두꺼운 속이 더 밝게), 구름층 판 `_Flash`·`_FlashPos`(번개 자리 둘레 3.5km가 안에서 빛나듯, 윗면은 60%), 안개 색(`FlashFogColor` 쪽으로 45% — 먼 곳·하늘 안개 돔도). 세기는 거리로(3km 안 최대 → 15km에서 0). **비구름층 위**(머리가 `DeckTop` 위로 `AboveDeckFade` 300m에 걸쳐): 번개는 구름 아래라 태양 섬광·낙뢰 줄기를 끄고 발밑 구름이 안에서 빛나는 것만(구름 섬광 × (1 − `AboveDeckDim` 0.4)).
  - 낙뢰 줄기(`Bolt`, `Generated/ACO_LightningBolt` — 높이 0~1의 지그재그 본 줄기 18마디 + 곁가지 3개, X·Z로 엇갈린 리본 두 장, 더하기 블렌드, 안개를 받지 않는다): 20km 안이면 번개 자리 땅(레이캐스트, 없으면 해수면)에서 비구름층 아랫면까지 늘려 섬광 동안 켠다(방향·폭은 번개마다). 구름층 위에선 천장 판에 가려 안 보인다.
  - 천둥: 9km 안이면 거리 / 343m/s 뒤에 — 2km 안은 가까운 천둥(찢어지는 충격 + 폭음 + 우르릉), 밖은 먼 천둥(낮은 우르릉). 세기 = (1 − 거리/9km)^0.7, 좌우 = 번개 방향(2D 소스 2개를 돌려 쓴다, 대기열 6). 소리는 `Tools/aco_make_thunder.py`로 합성(`Audio/Generated/ACO_Thunder_Near_1·2`, `Far_1·2`) — 갖고 있는 소리로 `NearThunder`·`FarThunder`를 바꿔 끼우면 된다.
- 뺀 것: HUD 지직거림·무전 잡음, 난기류(기체 흔들림).
- 설치: Bake(또는 Create Clouds and Rain)가 빠진 캐노피 빗방울 구(Rain 아래)·번개(Lightning — 줄기 + 천둥 소스)를 만들고(`ACO_SkyBuilder.EnsureStormParts`), 번개 ↔ 구름 필드·태양·해수면·지면 레이어, 비 ↔ 레이더 접촉 목록(내 기체)을 잇는다.

### 6-51. 비행 HUD — SAV HUD 대체 (`ACO_FlightHud`)

- 왜: SAV HUD는 멀리 띄운 판을 캐노피 스텐실로 보는 방식이라 각도에 따라 잘리고, 헤딩이 원형이며 쓸데없는 정보(마하·대지속도 등)가 많다.
- **기체 앞 고정(시준)**: 루트를 매 프레임(PostLateUpdate) 내 머리 위치 + 기체 자세에 두고 `Distance`(20m) 앞 판(Canvas)에 그린다. 판의 1단위 = 시야각 1° × 크기(`BaseScale` 0.5 × `Scale` — HUD 설정 FLIGHT HUD SIZE 0.6~1.6, 예전 ×1.0이 너무 커서 기본을 절반으로). 글자는 빌더에서 × `FlightTextScale` 1.5(배치도 키운 글자에 맞췄다). 글꼴 **Electronic Highway Sign**(TMP 예제 `Examples & Extras/.../Electronic Highway Sign SDF` — 없으면 LiberationSans), 머티리얼 `Generated/ACO_FlightHudTextOverlay.mat`(그 글꼴 아틀라스 + `TextMeshPro/Distance Field Overlay` — 글꼴 기본 머티리얼은 깊이 검사를 해서 조종석 구조물에 가려진다). 예제 글꼴은 동적 아틀라스(글자 20개, 빌드 때 비움)라 빌더가 출력 가능한 ASCII를 넣고 정적으로 굳힌다. 속도 벡터·기총 피퍼는 실제 방향을 판에 **접평면 투영**(기체 기준 x/z, y/z)해 바깥 풍경과 정확히 겹친다. **피치 래더**는 같은 투영 자리를 가운데에서의 각도로 바꿔 1° = `LadderScale`(0.5) 판 단위로 놓는다 — HUD 크기와 상관없이 10°마다 5단위(창 ±10.5에 약 ±21°)로 좁고, 기수를 들어도 천천히 움직인다(실제 수평선과는 안 겹친다 — 겹치게 하려면 1 / (BaseScale × Scale)). 선은 `ACO/MarkerOverlay`(맨 위, 스텐실 없음), 글자는 TMP 오버레이, 모두 UI 레이어(사진에 안 찍힌다). 조종 중일 때만(`Contacts.LocalVehicle.Piloting`).
- **배치**(단위 = °): 위 헤딩 테이프(y 12, 5° 눈금 13개, 10°마다 두 자리 숫자, 가운데 상자에 세 자리 방위) / 왼쪽 속도 테이프(x −13, 10kt 눈금, 50kt마다 숫자, 상자에 지금 속도 — **kt**) + 그 옆 **스로틀**(x −19.6, 높이 8 — 채움 = 엔진 출력, 선 = 애프터버너 시작점, 아래 `THR %`/`AB`) / 오른쪽 고도 테이프(x 13, 100ft 눈금, 500ft마다 숫자, 상자에 지금 고도 — **ft**, 해수면 = SAV `SeaLevel`) + 전파고도 `R ft`(2500ft 아래 — 0.1초마다 아래로 레이캐스트, 바다는 트리거라 트리거도 맞힌다) / 피치 래더(−90~+90°, 10°마다 — ±90 = 천정·천저, 루프로 넘어갈 때 빈 구간을 메운다 — **숫자는 래더 양쪽 끝**, 위 = 실선 + 끝이 아래로 꺾임, 아래 = 점선 + 끝이 위로, 0° = 긴 수평선, 가운데 창(±11 × ±10.5) 밖 칸은 숨긴다) / 속도 벡터(원 + 양 날개 + 위 꼬리 — SAV HUD와 같은 꼴, 창 밖이면 가장자리에 붙는다). **래더 기준 방위** = 기수의 수평 성분 — 수직 가까이(|기수 y| 0.94 → 0.995, 약 70° → 84°)에선 기수의 수평 성분이 0에 가까워 조금만 흔들려도 방위가 빙글 돌므로 기체 위쪽을 빼(fwd − up × fwd.y) 배 쪽 방위로 옮긴다 — 수직에서 머물거나 롤해도 래더가 돌지 않고, 루프 꼭대기를 넘으면 약 73°에서 한 번 뒤집힌다.
  - 왼쪽 아래: G, 받음각(랜딩기어를 내렸을 때만), 상태(GEAR UP/DN · FLAP ON/OFF · HOOK ON/OFF — 켜진 것은 밝게, 꺼진 것은 흐리게, 에어브레이크를 펴면(공중 + 브레이크 입력) **AIRBRAKE** 깜빡임).
  - 오른쪽 아래: 잔탄 **한 줄씩** — `GUN`(남은 초 × 초당 발사 수 = 탄 수), `MSL`(ACO 기본 미사일), `SP 무장ID 탄`(특수무장 슬롯 — SAV 모듈이면 `-`), `FLR` — 고른 무장 앞에 `>`. 그 아래 `HP %` + 막대.
  - 오른쪽 아래, 잔탄 글자 왼쪽: 고른 ACO 무장(기본 미사일·특수무장 ACO 모듈)의 **장전 슬롯 막대**(최대 8개 — 세로 0.34 × 2.6의 얇은 막대를 `SlotSpacing` 0.55 간격으로 잔탄 글자 바로 왼쪽부터 왼쪽으로 늘어놓고, 아래 → 위로 찬다). 잔탄은 세 칸(고른 무장 쉐브론 `>` | 이름 GUN·MSL·특수무장 ID·FLR | 수 — 오른쪽 맞춤)으로 줄을 맞춘다, 글자 10(약 1°), HP 9. 기총 수 = 남은 초 × 파티클 초당 발사 수. SAV HUD를 숨기면 예전 막대도 같이 사라진다.
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

- **이미지 = URL만**(VRChat 이미지 로딩 `VRCImageDownloader`): 스튜디오 URL 입력 칸(`VRCUrlInputField` — VRChat 키보드)에 붙여 넣고 LOAD. VRChat 제약: 스크립트가 문자열로 URL을 만들 수 없어 **URL은 저장 불가 — 접속마다 다시 넣는다**. 허용 호스트(Imgur·GitHub 등) 밖은 보는 사람이 "신뢰하지 않는 URL 허용"을 켜야 보인다. PNG·JPG, 최대 2048. 월드 전체 5초에 1장 — 스튜디오 하나가 **모든 데칼의 받기를 맡는다**(캐시 16칸 — 같은 URL은 한 번, 대기열을 `DownloadGap` 5.5초 간격으로). 실패하면 그 데칼은 끈다(스튜디오에 이유 표시).
- **이미지 2개**: LOAD는 지금 고른 데칼의 이미지를 바꾼다. 이미지를 넣지 않은 데칼은 다른 데칼의 이미지를 쓰고, MIRROR는 이미지도 복사한다. 실기체는 URL 두 개를 동기화한다(`_url`, `_url1`).
- **데칼 셰이더**(`ACO/Decal`, Unity 프로젝터 — 기체 UV와 상관없이 표면에 비춘다): 직교 정사각 틀 6m(데칼 최대 크기), 깊이 0.6m. 크기·좌우 뒤집기·켜기는 **머티리얼 값**(`_Size`·`_Flip`·`_Visible`)이라 Udon은 프로젝터 컴포넌트를 건드리지 않고 위치와 머티리얼만 바꾼다(프로젝터마다 머티리얼이 따로 — `Generated/Decals`). 프로젝터를 마주 보는 면만·비스듬할수록 흐리게(비추는 방향 = `unity_ProjectorClip` 첫 줄(깊이 축)과 노멀로 판정 — 처음엔 투영 행렬 z 부호로 판정해 부호가 어긋나면 모든 면이 버려져 데칼이 통째로 안 보였다), 깊이 끝에서 흐리게 → 날개 반대쪽·갑판엔 안 묻는다. 비추지 않는 레이어: 물·UI·PlayerLocal·UiMenu·MirrorReflection·InternalUI·FX. 비용: 비추는 범위의 기체 메시를 한 번 더 그린다(데칼 하나 = 기체 한 번).
- **배치(격납고 샘플 기체)**: 프리팹 `ACO_DecalStudio`(패널 + 데칼 건 + 미리보기 프로젝터 2개). 씬에서 `SampleRoot`(샘플 기체 기준점 — **실기체 SaccEntity 루트와 같은 자리·같은 모델**이어야 상대 자리가 그대로 옮겨진다)와 `DecalKey`(기종)를 넣고 Bake. 데칼 건(VRC Pickup, 로컬 도구 — 각자 자기 화면)을 들고 샘플을 겨누면 겨누는 선이 맞은 곳까지 늘어나고, **방아쇠를 누르는 순간** 맞은 표면에 고른 데칼이 바로 찍히고 저장된다(건의 위쪽 = 데칼 위쪽, 표면 노멀 방향으로 비춘다 — 겨누는 동안 따라다니는 미리보기는 없다. 플레이어 데이터를 받기 전 접속 직후엔 화면에만 찍힌다). 미리보기 프로젝터는 SampleRoot 기준 상대값을 월드 자리로 바꿔 놓는다(부모가 SampleRoot가 아니어도 샘플에 찍힌다). 놓으면 받침대로 돌아간다. 패널: DECAL 1/2(편집할 데칼), SIZE ±(×1.15, 0.2~6m), ROT ±(15°), FLIP(좌우), **MIRROR 1 > 2**(1을 샘플 기준 X = 0 면 대칭 자리로 — 위치 x 반대, 회전 거울 반사(qx, −qy, −qz, qw), 좌우 뒤집기 반대 → 반대쪽에서도 밖에서 바로 읽힌다), CLEAR(고른 데칼 끄기), HIDE OTHERS.
- **저장**(플레이어 영구 저장, 기종별): `aco_decal_<DecalKey>_<0|1>_on/px/py/pz/rx/ry/rz/rw/size/flip` — 샘플 기준 상대값(= 실기체 기준점 기준). 다른 사람 숨기기 `aco_decal_hide_others`. 키는 바꾸지 않는다.
- **실기체**(`ACO_VehicleDecal`, Bake가 플레이어 기체 SaccEntity 루트 아래 `ACO_Decals` + 프로젝터 2개, 수동 동기화 — 오너 = 조종사): 조종석에 앉으면(`ACO_VehicleUpgrades` → `LocalPilotEnter`) 내 URL(이번 접속)과 내 저장 자리 2개(그 기체 `DecalKey`)를 동기화 값으로 보낸다 — URL이 없거나 자리가 없으면 끈다. 내리면 끈다. 모든 클라이언트가 받은 자리에 프로젝터를 놓고 스튜디오에 이미지를 부탁해 받으면 켠다(0.5초마다 확인). 늦게 들어온 사람도 받는다. 다른 사람 데칼은 HIDE OTHERS면 내 화면에서 숨긴다(바꾸면 바로 — 스튜디오 `VehicleDecals`).
- 이미지: VRChat 이미지 로딩은 **PNG·JPG 직접 링크만**(WebP·HTML 페이지 링크는 실패 — 예: 나무위키 `.webp` 주소는 404). 확인 필요: VRChat에서 URL 입력 칸 키보드.

### 6-55. SCM (Stand-off Cluster Missile) — 클러스터 미사일

- **역할**: 넓은 지역 제압. 호송대·기지·상륙 지점처럼 모인 표적용. 단일 표적엔 LAGM이 낫다. 격납고 가격 14000, 정렬 58(LASM 다음), 강화는 특수 대지 탭.
- **성능**(`ApplySCM`): 락온 6km·30도·2초, 표적 1개, 슬롯 2 / 8발 / 14초, 느리다(최고 550m/s, 선회 60, 수명 30초), 시커 180도(올라가는 동안 표적을 놓치지 않게). 자탄마다 피해 110 / 범위 30m(가장자리 40%) — 무장 `Damage`·`SplashRadius`라 강화가 자탄마다 적용된다.
- **비행**: 탄체 `ACO_Missile_SCM`(빌더가 LAGM 프리팹을 복사해 만든다 — `ACO_PrefabBuilder.BuildScmMissile`, 프리팹이 없으면 카탈로그가 만든다). 톱어택 — 표적 위 700m를 향해 올라가다 내려다보는 각이 55도가 되면 내리꽂는다.
- **산개**(`ACO_Dispenser`, 미사일 프리팹 자식, 동기화 없음): 살아 있는 표적까지 `BurstDistance`(420m) 안이면 탄체가 터지고(`_dispensed` — 공중에서는 범위 피해·범위 표시 없음) 표적 쪽으로 반각 `ConeAngle`(12도) 원뿔에 자탄 `Count`(16, 최대 32)발을 뿌린다. 자탄은 오브젝트가 아니라 파티클(`Bomblets`, 곧게 `BombletSpeed` 260m/s). 떨어지는 자리는 원뿔 안 황금각 나선 방향으로 지면까지 레이캐스트(모든 화면이 같다, 바다는 트리거라 트리거도 맞힌다, `MaxReach` 1.5km 안에 안 닿으면 그 자탄은 없다) — 내리꽂는 각도 덕에 지면에 약 220 × 180m 타원으로 깔린다.
- **착탄**: 닿는 시각(거리 / 탄속)마다 폭발 파티클(`Blasts`)을 그 자리로 옮겨 방출, `SoundEvery`(3)발마다 폭발음(소스 3개를 돌려 쓴다), `ACO_Missile.SubmunitionHit`으로 범위 피해 — 쏜 사람 화면에서만(폭탄·로켓과 같다). 프레임마다 그때 닿은 자탄만 처리해 판정이 한 프레임에 몰리지 않는다. 한 표적이 자탄 여러 발을 맞을 수 있다.
- **풀 회수**: 자탄이 다 떨어지고 파티클이 사라질 때까지(`Dispenser.GetDuration`) 탄체를 켜 둔다(자식이라 먼저 끄면 같이 사라진다).
- **예외**: 표적이 먼저 죽거나 플레어·시커 이탈로 놓치면 직진하다가, 앞쪽 땅이나 바다가 `BurstDistance`(420m) 안에 들어올 때 비행 방향으로 뿌린다(`HasGroundAhead`). 수명이 다할 때까지 땅을 만나지 않으면 공중에서 터지고 자탄은 없다. CIWS 요격에 걸리면 뿌리기 전에 터진다.
- **값을 바꿀 때**: `BombletSpeed`는 자탄 파티클 시작 속도와 같아야 폭발 시각이 맞다 — 다르게 하려면 프리팹의 Bomblets 파티클도 고친다. 탄체 값(Dispenser·TopAttack)은 프리팹 `ACO_Missile_SCM`, 무장 값은 `ACO_Special_SCM`.

### 6-56. 공중 요새 (거대 보스)

- **흐름**(페이즈 모듈 `ACO_PhaseFortress`, 보너스 8000): 요새가 도시 둘레를 돌다 도시로 돌진한다. 방공망 → 엔진 → 코어 3단계. 코어를 부수면 성공, 도시에 닿으면 미션 실패(CITY DESTROYED). 시작할 때 미션 제한 시간에 `ExtraTime`(10분)을 더한다(`Manager.AddModuleTime`). 최대 4명 기준.
- **언제 나오나**: 페이즈별 가중치(아래) 기본값 {0, 0, 0, 0, 3} — 5번째 페이즈에서만, 다른 종류(가중치 1)의 3배. 5페이즈보다 짧은 미션엔 나오지 않는다.
- **페이즈별 가중치**(`ACO_PhaseModule.PhaseWeights` — 모든 페이즈 모듈 공통): 칸 0 = 1페이즈, 칸 1 = 2페이즈 …, 비우면 모든 페이즈에 `Weight`, 칸보다 뒤의 페이즈는 마지막 칸 값. 보너스 전용 모듈은 본 미션 페이즈 수 기준. `MinPhase`·`Enabled`도 그대로 본다. 표로 고치고 확률(%)을 보는 창: 메뉴 ACO → Missions → Phase Weights(`ACO_PhaseWeightWindow`). 직전과 같은 종류·배치 불가 종류는 뽑을 때 빠지므로 표의 %는 어림값이다.
- **디버그 1페이즈 강제**: 디버그 패널의 미션 가중치 ± 를 없애고 모듈마다 P1 버튼을 뒀다(가중치는 에디터 표에서 정한다). 누르면 관리자 `DebugFirstModule`이 모든 클라이언트에 들어가고, 다음 미션부터 첫 페이즈가 그 모듈이다(가중치·MinPhase·Enabled·BonusOnly 무시, 다시 누르면 해제). 배치할 수 없으면(도시 없는 지형 등) 경고하고 평소대로 고른다. 늦게 들어온 클라이언트는 값이 없지만 페이즈를 고르는 건 관리자 오너다 — 오너가 바뀌면 풀릴 수 있다(디버그용).
- **대사**: 상황마다 관제(`Line*`)·요새 지휘관(`Enemy*`, 이름 `EnemyName`) 줄과 음성 칸 — 등장, 엔진 단계, 엔진 파괴(`{left}`), 방공 수리, 코어 단계, 코어 절반, 도시 근접(`CityCloseDistance`), 격추. 비우면 안 나온다. 관제의 시작·성공·실패는 공통 칸. 동기화된 값(단계, `_repairs`)과 부위 체력으로 모든 클라이언트가 각자 재생하고(0.5초마다 `CheckLines`), 늦게 들어온 클라이언트는 지난 대사를 듣지 않는다.
  - 1단계 방공망: 대공포 8·CIWS 6·VLS 4(18곳)를 모두 부순다. 엔진·코어는 잠기지도 맞지도 않는다.
  - 2단계 엔진: 엔진 4개(각 1200)를 부순다. 부서질수록 느려지고(궤도 속도 100% → 45%) 부서진 쪽으로 기운다.
  - 3단계 코어: 덮개가 사라지고 코어(4000)가 드러나며 도시로 돌진한다(60m/s, 최소 110초).
  - 수리: 2·3단계에 `RepairInterval`(40초)마다 부서진 방공 부위 `RepairCount`(3곳)가 다시 돈다. 다시 부수지 않아도 단계는 돌아가지 않는다. 단계가 바뀔 때 요새 둘레에 적 전투기 `ReinforceFighters`(4기).
- **이동**(`ACO_FlyingFortress`, 위치 동기화 없음): 자리 = 동기화된 기준값 + 서버 시각. 궤도: 도시 중심 반경 7km, 60m/s, 고도 2400m ± 50m(비구름층 위), 안쪽으로 4도 기운다. 엔진이 부서지면 그 순간 각도로 기준을 다시 잡아 속도만 바뀐다(자리가 이어진다). 돌진: 궤도에서 안쪽으로 반경 `TurnRadius`(2km) 선회해 도시를 향한 뒤 곧게 들어간다(궤도 7km 기준 8.5km, 약 2분 30초). 선회 시작부터 일정한 비율(평균 약 15도)로 내려가고 기체는 `GlidePitch`(6도)까지만 숙인다. **튐 방지**: 돌진은 `DiveLead`(1.5초) 뒤로 예약되고 시작 자리는 그 시각의 궤도 자리라 모든 클라이언트가 같은 자리에서 전환한다. 시작 방향은 궤도 접선과 같고, 속도는 `DiveAccelTime`(20초) 동안 궤도 속도에서 `DiveSpeed`까지 오르며, 기울기는 경로 위치의 함수(궤도 4도 → 선회 8도 → 0도)다.
- **끝맺음**: 격추되면 낙하하며 동체가 연쇄 폭발하고, 진행 방향 레이캐스트(`ImpactLayers` — 지형·수면)로 충돌을 찾아 `ACO_FortressBlast`를 재생한다(땅: 화염구·파편·연기 기둥, 바다: 물기둥·물안개, 공통: 섬광·충격파·조명, 소리는 음속만큼 지연). 연출 전용이라 피해가 없고, 각 클라이언트 화면의 충돌 자리에서 재생한다. 동체는 부위 스폰 순서를 지키려 끄지 않고 지하로 옮긴다. 도시에 닿으면 도시 중심에서 1.5배로 재생한다(도시 모델은 그대로). 결과는 격추 충돌 `CrashClearDelay`(4초) 뒤(최대 `CrashHoldMax` 30초), 도시 충돌 `CityFailDelay`(6초) 뒤에 나온다. 기다리는 동안은 `HoldsResult`로 제한 시간 실패를 막고, 격추를 기다린 시간은 `AddModuleTime`으로 돌려준다.
- **대사**: 모듈의 상황별 대사 표(`LineEvent`·`LineSpeaker`·`LineText`·`LineClip`, 같은 순서의 배열 — `ACO_PhaseFortressEditor`가 한 줄씩 보여 준다). 같은 상황은 위에서 아래로 차례로 나오므로 적 줄 다음에 AWACS 줄을 둔다. AWACS 음성은 이 미션 전용이라 공용 채터가 아니라 이 표의 칸에 넣는다(지금은 비어 있어 자막만). 도시 접근(5·3·1km)과 충돌 대사는 `Chatter.ClearQueue`로 밀린 대사보다 먼저 나온다.
- **동기화**(`ACO_PhaseFortress`, Manual): 단계, 궤도 기준 각도·시각·속도 배율, 돌진 시작 시각·자리·방위 — 단계가 바뀌거나 엔진이 부서질 때만 보낸다. 판정은 관리자 오너. 페이즈 도장으로 지난 값을 무시하고, 값을 못 받은 새 오너는 지금 자리에서 1단계로 이어 간다(단계는 부위 상태로 다시 올라간다). 배치 직후 `ObjectiveSettleTime` 동안은 단계를 판정하지 않는다(부위 체력이 부활 동기화 전이라 0으로 보인다).
- **기존 체계 재사용**: 요새는 부위가 달린 함선이다 — 같은 오브젝트에 `ACO_Warship`(HighValue, `ExternalPose` — 순항하지 않고 `PoseOwner`에 스폰·철수를 알린다, `PartHullDamage` 0)이 있어 고가치 표적 풀·부위 목록(`CAT_PART`)·표적 마커·레이더·점수를 그대로 쓴다. 생성기 `Fortress`로 일반 표적 배치에서 뺀다(`IsFortressUnit` — `CollectFree`·`PlaceHighValueTargets`). 배치는 `OBJ_FORTRESS`(focus = 도시, tier = 시작 방위). 성공 판정은 본체(코어) 태그 세기(`CountTaggedObjectives`).
- **피해 경계**: 코어 SaccTarget은 루트에 있어야 해서(함선 규칙), 선체 콜라이더는 자식 `Hull`에 두고 자체 SaccTarget(체력 100만, 기총 이벤트 끔)이 맞게 한다 — 범위 피해가 `GetComponentInParent`로 코어까지 번지지 않는다. 코어 히트박스·락온 마커는 3단계까지 꺼 둔다. 엔진(`ACO_FortressPart`)은 드러나기 전 마커·히트박스를 끄고 표적 마커도 숨긴다(`IsUnitSuppressed`).
- **공대공 전용**: 요새는 공중 표적이다. 부위·엔진·코어의 락온 마커를 모두 적 항공기 레이어(29)에 둬 공대공 무장만 잠기고, 공대지 미사일(레이어 30)은 잠기지 않는다. 표적 마커·레이더도 공중 표적으로 그린다(`IsFortressTarget`). 범위 피해·기총·로켓은 레이어가 아니라 히트박스로 맞으므로 드러난 부위엔 들어간다. 체력은 공대공 직격 200 기준(엔진 6발, 코어 20발). 이전 프리팹은 Bake가 다시 만든다(`IsFortressOutdated`).
- **포대**: 함선 부위 그대로(`ACO_AAGunSite`, `ACO_SAMSite`, 3~3.5배 크기). 포탑은 월드 기준으로 조준하므로 요새가 기울어도 된다. 배 쪽 포대는 고각 제한만 다르다(−85 ~ +20도). CIWS는 플레이어 미사일을 요격한다.
- **모델**(`ACO_FortressBuilder.cs`, 외부 에셋 없음): 전익기 꼴 선체(폭 600m, 길이 415m)를 단면 9개로 잇고, 함교·꼬리날개·장갑판을 한 메시로 합친다(그림 한 번). 엔진·덮개 메시 따로. `Generated/ACO_Fortress_*`. 빌더는 씨앗 고정이라 다시 만들어도 같다.
- **설치**: Bake가 프리팹이 없으면 만들고 유닛 풀에 넣으며, 페이즈 모듈 `Phase_Fortress`도 만들어 잇는다(무전도). 이전 기본값으로 만든 모듈은 한 번 옮긴다. 도시 자리가 없는 지형이면 나오지 않는다(`IsFeasible`).

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

### 6-59. 기총 탄과 수면

- 수면은 트리거(씬 `Water`, 레이어 4, 깊이 200m)라 파티클 Collision 모듈에 막히지 않아 탄이 물속으로 들어갔다. Bake(`BakeGunWater`)가 NPC 예광탄(`ACO_NPCGun.VisualParticle`)과 플레이어 기총 탄(`ACO_GunDamageBonus`가 붙은 파티클)의 Trigger 모듈에 이 상자를 넣는다(안에 들면 Kill). 프레임마다 위치만 보므로 상자가 얇으면 빠른 탄이 건너뛴다 — 깊이 50m 이상이어야 한다. 씬 값이라 SAV 프리팹 파일은 바뀌지 않는다. 물보라는 없다.

### 6-60. 구름 겹침·디더 점

- **뭉게구름과 비구름층**: 뭉게구름 고도(1300~2600m)가 비구름층(1300~1800m)과 겹쳐, 층의 부드러운 경계가 판 가까이 있는 뭉게구름 자리를 투명하게 만들어 윤곽이 비쳤다. 비구름층이 있는 동안 층 윗면보다 낮은 퍼프를 셰이더에서 숨긴다(`_HideBelow` = `DeckTop`, `_HideAmount` = 층 페이드 — 그림자·깊이 패스 포함). 높이로 자르므로 날씨가 바뀌는 `LayerFadeTime` 동안만 디더링한다. 윗면보다 높은 구름은 층 위로 솟아 보인다. 층 윗면 아래에서는 뭉게구름 속 안개를 쓰지 않는다. 층운이 있는 날씨(비구름층·안개층)엔 보이는 뭉게구름이 층 위라 흰 층 윗면과 맞게 날씨 어둡기를 빼고 줄어든 태양을 되돌린다(`ApplyCloudLight` — `_Darken`, `_SunGain`, 층 페이드를 따라 서서히).
- **화면 고정 점**: `clip(페이드 - 디더 - 0.001)`은 페이드가 1이어도 디더 값이 0.999를 넘는 픽셀(약 0.1%)을 지웠고, 디더가 화면 좌표라 점이 화면에 고정됐다. 디더에 0.998을 곱해 페이드 1이면 아무 픽셀도 지우지 않는다(`ACO_CloudLayer`, `ACO_CloudPuffs`).

### 6-61. 관전 패널: 슬롯 선택·비

- **슬롯 선택**: TARGET 버튼은 AUTO → 슬롯 1 → 2 … 순으로 돌고 비어 있는 슬롯도 고른다. 조종사가 없으면 화면은 꺼지고 `WAITING FOR PILOT`, 타면 찍기 시작한다(자기 슬롯을 골라 두고 타러 갈 수 있다 — 세워 둔 기체는 보관 장소에 있어 찍지 않는다). 글자는 슬롯 이름 + 명단의 참가자. Bake가 `Vehicles`를 게임 흐름의 칸 순서로 넣는다.
- **비**: 비는 머리를 따라다니는 FX 레이어(28)라 관전 카메라가 그리지 않았고, 머리가 격납고·구름 위면 꺼졌다. 카메라 자식 `SpectatorRain`(같은 메시·머티리얼, 레이어 18 — Bake가 만든다)을 따로 두고, 구름 위·지붕 아래를 카메라 자리로 판단한다. 빗줄기는 카메라 속도로 늘어난다.

### 6-62. 격납고 가시성

- **보유 크레딧**: 위쪽 금색 테두리 판에 동전 아이콘 + 큰 금색 숫자(`ACO_Shop.CreditsText`), 그 아래 지난 미션 보상(`LastRewardText` — 비면 예전처럼 둘째 줄).
- **구획**: 특수무장·강화·출격을 옅은 카드로 나누고, 제목 띠에 아이콘을 붙였다. 아이콘 hangar(제목), tab_missile(특수무장), upgrade(강화), roster(명단), ready·launch(버튼), credits(구매 버튼), 무장 분류는 `ACO_Loadout.PageIcon`이 분류에 맞게 바꾼다.
- 새 아이콘은 `Tools/aco_icons.py`. 프리팹 생성 창에서 격납고를 다시 만들어야 반영된다(`CreateHangarPrefab`).

### 6-63. 콘솔·관전 패널·작전판 아이콘

- **페이즈 난이도**: 콘솔의 큰 숫자 옆에 새 아이콘(`phase_1~5` — 병아리, 참새, 매, 독수리, 왕관 쓴 독수리). 숫자·아이콘·난이도 글자가 초록 → 빨강으로 함께 바뀐다(`ACO_GameFlow.PhaseLevelColors`, `PhaseLevelNames` EASY ~ ACE — 표시만, 실제 난이도 값을 바꾸지 않는다). 아이콘은 이름(`FlowPhaseIconName`)으로 Bake가 찾는다.
- **콘솔**: 상태 배너, 작전·페이즈·지형·명단 제목, NEW TERRAIN(순환 화살표 + 산), 슬롯 카드, 내 행동 버튼, LEAVE. **관전 패널**: POWER, HD/FHD, TARGET(이름을 SLOT으로), SHOT. **작전판**: 제목(지도), SCORE(우승컵, 월드 판이라 SpriteRenderer — `MakeWorldIcon`).
- 아이콘은 프리팹 빌더가 붙인다 — 프리팹 생성 창에서 작전 콘솔·관전 패널·작전판을 다시 만들어야 반영된다.

### 6-64. 난이도 곡선

- **페이즈 수**: 5로 유지한다. 페이즈 가중치는 페이즈 번호 기준이라 함대전·요새는 페이즈를 많이 고른 판에서만 나온다(난이도 선택의 보상).
- **임무 순서**: 기본 모듈의 `PhaseWeights`를 앞은 타격·제압·제공권, 중간은 호위·콘보이·상륙전, 4는 함대전, 5는 요새 위주로 둔다. 비어 있는 모듈에만 Bake가 넣는다(`ApplyDefaultWeights` — ACO → Missions → Phase Weights에서 직접 바꾼 값은 유지).
- **숙련도**: `ACO_MissionGenerator.PhaseVeteranChance`·`PhaseAceChance`(1~5: 베테랑 15→50%, 에이스 0→25%)를 전투기 편대와 지상 유닛이 함께 쓴다. 웨이브 증원은 웨이브 값과 페이즈 값 중 높은 쪽.
- **지상 유닛**: 유닛마다 숙련도를 굴려(`RollUnitTier` — 배치의 시드 난수를 건드리지 않는 해시) `_groundTier`·`_hvtTier`로 동기화하고, 스폰할 때 `ApplyUnitTier`가 넣는다. 함선은 부위마다, 전차·곡사포는 포마다. 기준은 프리팹 값(베테랑 1.0)이고 신병은 약해지고 에이스는 강해진다: 포 조준·체류·사격 간격, SAM·VLS 락온·재장전, 곡사포 간격·산포, 최대 체력(신병 ×0.9, 에이스 ×1.3 — 플레이어 피해 강화와 맞물린다, `Ground*Multiplier`). 체력 기준은 Bake가 채운 `BaseHealth`이고, 값은 스폰 2프레임 뒤 `SaccTarget.FullHealth`에 넣는다(전투기와 같은 방식 — 부활이 새 체력으로 채운다). 건물·TEL·공중 요새 코어·엔진은 체력 대상이 아니다(요새 포대는 대상). 아군 함선·호위 유닛은 아무것도 대상이 아니다.
- **보너스**: 페이즈를 많이 고를수록 보너스 페이즈가 잘 붙는다(1~5페이즈: 5·10·25·50·100%, `BonusChanceByPhases`). 디버그 패널의 ACE·VETERAN·BONUS 버튼은 표 대신 고정 값을 쓰게 만든다(`DebugFixedChance`, `DebugBonusChance` — 표시는 그전까지 BY PHASE).

### 6-65. 데칼 스튜디오 개선

- **아이콘**: 제목·LOAD·DECAL 1/2·RESET GUN·SIZE·ROT·FLIP·MIRROR·CLEAR·HIDE OTHERS.
- **찍는 소리·이미지 창**: 데칼을 찍으면 건에서 스프레이 소리(`ACO_Decal_Spray.wav`, 합성 — 건의 `StampSound`, 로컬). 패널 오른쪽 아래 작은 창(`LoadedImage`)에 불러온 이미지를 비율대로 보인다(받는 동안엔 이전 이미지, 없으면 NO IMAGE).
- **데칼 건**: `VRCPickup.orientation = Gun`(`ExactGun` = Grip)이라 집으면 총구가 앞을 본다. 놓은 자리에 그대로 떠 있고(자동으로 돌아가지 않는다), 패널 RESET GUN이 시작 자리로 돌려놓는다(`ResetToHome` — 들고 있으면 먼저 놓는다, 로컬).
- **이미지 받기 고침**: (1) 이미 받은 URL을 다시 LOAD하면 받기 응답이 없어 LOADING에 멈추고 미리보기도 안 바뀌었다 → 바로 적용. (2) 실패한 URL은 캐시에 남아 다시 받지 않았다 → 자동 1회 재시도, LOAD로 다시 받기. (3) 응답이 안 오면 대기열 전체가 멈췄다 → `DownloadTimeout`(30초). (4) 칸이 다 차면 가장 앞 칸을 지워 패널이 쓰는 이미지나 대기 중인 요청이 사라졌다 → 실패 → 받아 둠 → 대기 순으로 돌려 쓰고 패널 이미지는 지운 칸에서 뺀다. (5) 패널에서 고른 이미지가 대기열 맨 앞(5초에 1장 제한 안내 표시).
- **잡는 방향**: Gun 방향 픽업의 `ExactGun`은 +X가 총구, +Z가 총의 위쪽이다(SAV 그래플링 훅 프리팹과 같다). 회전 없이 두면 총구가 위를 봐서, `Grip`을 Euler(-90, -90, 0)으로 돌렸다.

### 6-66. 랜스 포대(지상 보스)

- **역할**: 공중 요새(공대공·이동)의 반대 — 공대지가 주력이고 표적은 고정, 압박은 카운트다운이다. 5페이즈 보스(가중치 3, 요새와 같다). 평평한 땅만 있으면 되고 도시가 필요 없다(아군 기지는 지평선 너머, HUD에 `BASE 3/3`로만 나온다).
- **주기**(`ACO_PhaseRailgun`): 충전 90초 → 발사(기지 내구도 1칸) → 재장전 10초를 처음부터 끝까지 되풀이. 내구도 3칸이 다 없어지면 6초 뒤 미션 실패. 위치 동기화 없이 주기 시각·충전 시간만 보내 모든 클라이언트가 같은 시각에 쏜다.
- **단계**: (1) 방공망 16곳 파괴(소형 레일건 4문 포함) (2) 냉각기 4기 파괴 — 하나마다 충전 +15초 (3) 코어 — 충전 마지막 25초 + 재장전 동안만 열린다. 2·3단계는 40초마다 방공 2곳 수리, 단계가 바뀔 때 적기 4기.
- **사선**: 발사 5초 전부터 붉은 레이저(`WarnBeam`). 발사 순간 사선 반경 160m 안의 내 기체가 큰 피해(`BeamDamage` 120 — 맞는 쪽 판정). 포구가 플레이어 쪽을 보며 서므로 사선을 피해야 한다.
- **지평선 착탄**: 발사 0.5초 뒤 사격 방향 15km 지평선에서 흰 섬광 + 오래 남는 주황 빛(`ACO/HorizonFlash` — 안개를 받지 않고 산 뒤면 가려진다, 깊이 패스 없음) + 구름·안개가 잠깐 밝아지고(`ACO_CloudField.SetFlash` — 번개와 같은 값) 4초 뒤 먼 폭음(`ACO_Distant_Boom`, 2D). 프리팹이 이미 있으면 Bake가 이 부분만 더한다.
- **소형 레일건**(`ACO_MiniRailgun`, 방공 부위 4문 — 1단계 목표, 수리됨): 표적이 `LeadTime`(3초) 뒤에 있을 자리를 예측해 7초마다 쏜다. 쏘기 `AimTime`(1.5초) 전부터 붉은 레이저가 예측 자리를 따라다니고(`NetAim`), 쏘는 순간 그 자리에 굵게 고정돼 탄이 닿을 때까지 3초 남는다 — 플레이어가 보고 피한다. 쏠 때 표적을 잃으면 쏘지 않는다. 탄은 그 시간에 맞춰 날아가 터지고, `ZoneTime`(3.5초) 동안 반경 170m 폭발 지역이 남아 0.5초마다 피해 20. 직진하면 폭발 속으로 들어가고 쏜 뒤 방향을 틀면 피한다. 탐지·조준·체력은 `ACO_AAGunSite`(기총 없음, 표적의 앞을 겨눈다 — `AimLeadTime`), 오너가 쏠 자리를 `NetFire`로 알리고 피해는 맞는 사람이 판정한다. 첫 사격에 AWACS 경고(`SIT_MINI_FIRE`). 이미 있는 프리팹엔 Bake가 이 부분만 더한다.
- **포탑**: 수평으로 돌지 않는다. 포신만 충전에 맞춰 3도에서 16도까지 들린다.
- **장갑·탑어택**: 좌우 장갑은 벽과 지붕이 한 덩어리라 바깥으로 80도 누우면 코어 위가 트인다(뒤판도 눕는다). 닫힌 동안만 `ArmorColliders`가 코어를 덮고, 열리면 코어 히트박스·락온 마커(지상 레이어 30)가 켜져 공대지·탑어택 무장이 들어간다.
- **끝맺음**: 코어가 터지면 연쇄 폭발 4초 → 대폭발(`ACO_FortressBlast`) → 4초 뒤 성공. 요새와 같이 `HoldsResult`로 제한 시간을 막고 기다린 시간은 돌려준다.
- **모델 교체**: 로직은 Transform(`BarrelPivot`, `Muzzle`, `ArmorPanels`, `Core`)과 Hull 충돌 상자만 쓴다. 모델은 그 아래의 `Visual_*` 자식이라 메시만 갈아 끼우면 된다. 크기가 다르면 Hull 상자와 자리를 맞추고, 장갑 회전은 `ArmorOpenEuler`.
- **제작**: 메시는 `ACO_RailgunBuilder.cs`(MeshBag + 평면 UV), 텍스처는 `Tools/aco_railgun_tex.py`(강철 패널·노멀맵·콘크리트·경고 줄무늬), 소리는 `Tools/aco_make_sfx.py`. 보스 공통(대사 표 재생)은 `ACO_PhaseBoss`, 인스펙터 표는 `ACO_PhaseFortressEditor`가 함께 쓴다.

### 6-67. 부위 락온 고침

- **엔진·냉각기가 안 잠기던 원인**: SAV `Target.controller`의 `TargetExplode` 클립이 자식 `AAMTarget`의 활성 상태를 다루고 Write Defaults가 켜져 있어, 애니메이터가 시작할 때 값(꺼짐)을 매 프레임 되돌렸다. 처음에 꺼 두고 스크립트로 켜는 부위 마커는 이름을 `LockMarker`로 둔다(`PartMarkerName`). 이미 있는 프리팹은 Bake가 이름만 고친다. 코어 마커는 `Core` 자식이라 클립 경로에 걸리지 않는다.
- **락온 표시가 본체 마커로 옮겨 가던 원인**: `ACO_TargetMarkers.FindUnitOf`가 첫 조상을 골라, 부위를 잠그면 먼저 등록된 본체(요새·함선) 마커가 그 부위 자리로 갔다. 가장 가까운 조상을 고른다 — 부위를 잠그면 그 부위 마커가 잠긴 표시가 된다.

### 6-68. 공중 요새·랜스 포대 충돌 뒤 정리

- **문제**: 도시 충돌(`Impact`)은 동체를 도시 아래 3km로 옮겨 숨기기만 한다. 격추와 달리 체력이 남아 부위가 침묵하지 않아, 실패 6초 + 거두기 대기 `EscortLingerTime` 30초, 최대 36초 동안 표적 마커·레이더·대공 무장이 지하에서 살아 있었다.
- **처리**: `ACO_PhaseFortress`가 충돌 `CityHideDelay`(1초) 뒤 `RequestReleaseTag`로 바로 풀에 거둔다(폭발은 부모에서 떨어져 있어 계속 보인다). 1초를 두는 건 모든 클라이언트가 폭발을 재생한 뒤 끄기 위해서다. 실패 판정은 그대로 `CityFailDelay`(6초) 뒤.
- **격추도 거둔다**: 격추 성공은 태그를 거두지 않아, 지하 3km로 옮긴 동체가 다음 전체 배치까지 켜진 채 남았다. 요새·랜스 포대 모두 `RESULT_CLEAR` 직전에 `RequestReleaseTag`로 거둔다. 킬 점수는 파괴 때 이미 집계되고, 충돌 폭발은 부모에서 떨어져 계속 보인다.
- **판정 순서**: 격추 → 도시 충돌 → 배치 대기. 거둔 뒤 `total`이 0이 돼도 배치 실패(`RESULT_REPLACE`)로 읽히지 않고, 격추 뒤 돌진 진행이 1까지 차도 실패로 읽히지 않는다.
- **대체 시작 방어**: `PickFallbackType`은 `IsFeasible`을 거치지 않아 `_planSite`가 -1일 수 있다. `Begin`이 다시 확인하고, 안 되면 배치 없이 돌아가 `RESULT_REPLACE`로 넘긴다(`GetSitePosition(-1)`은 지형 스크립트를 멈춘다).

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

### 6-72. 공중 요새 Archangel 모델

- `Assets/ARCANGEL/model/archangel.prefab`을 `Model`로 넣는다(중첩 인스턴스). 원본 길이 1490m·폭 820m, 배율 `FortressScale` 0.6(길이 약 890m·폭 약 490m). 원본은 기수가 +X라 Y -90도 돌린다. 이 폴더는 git에서 제외돼 있어 다른 환경에는 따로 있어야 한다 — 없으면 Bake는 있는 프리팹을 그대로 쓴다.
- **부위 자리**: FBX에서 잰 모델 좌표를 `FortressPoint`로 바꾼다. 포대는 윗면·배 표면, 엔진 4는 후면 노즐 묶음(한쪽 9개를 안쪽 4·바깥쪽 5로 나눔), 코어는 격납고 한가운데(모델 x -250)로 후면 해치 통로의 축 위다.
- **격납고 문**: `HatchOpenStage`의 단계부터 `HatchOpenTime`(4초) 동안 연다 — 측면 `ExportHatch_L/R`은 엔진 단계, 후면 `backHatch`는 코어 단계. 열린 회전은 `ARCHANGEL_hub`의 열린 자세 값이다.
- **충돌**: `archangelcollidor` 메시를 `Hull`의 MeshCollider로 쓴다(격납고가 뚫려 있고 `fuselage`와 같은 좌표계). 해치는 충돌 메시에 없어, 닫힌 동안 상자(`HatchBlockers`)로 기총을 막는다. 기총·미사일·락온 모두 외피에 막힌다(6-80).
- 먼저 만든 프리팹은 `Hatches`가 비어 있어 Bake가 다시 만든다. 레이더 돔 회전은 모델의 Animator(`Armature.controller`)가 맡는다.

### 6-73. 발사 후 락 유지

- `ACO_Missile.LockHoldTime`(프리팹 옵션)·`ACO_DFUNC_Missile.LockHoldTime`(무장 옵션, 0보다 크면 프리팹 값을 덮어쓴다): 발사 후 이 시간 동안 시커 판정(`IsSeekerDefeated`)을 건너뛴다 — 시커 범위 초과·노칭으로 표적을 놓치지 않는다. 기본 0.
- 플레어·채프는 막지 않는다(`IgnoreCountermeasures`가 따로 있다). 표적이 격추되거나 사라지면 그대로 끝난다. 시간이 끝나면 일반 시커로 돌아가, 그때 범위 밖이면 놓친다.
- 무장 값은 강화로 바뀌지 않아 이벤트로 보내지 않고 받는 쪽 무장 값을 쓴다. 유도 폭탄·로켓(`ACO_Ordnance`)에는 적용되지 않는다.

### 6-74. 공중 요새 측면 해치 출격

- 엔진 단계·코어 단계가 시작되면 `ReinforceFighters`(4기)가 측면 해치에서 나온다. 그 단계의 대사가 끝난 뒤(엔진 단계는 `LaunchDelay` 4초 이상) 좌우 번갈아 `LaunchSpacing`(2초)마다 한 기씩.
- 자리는 프리팹의 `Launch_L/R`(`ACO_FlyingFortress.LaunchPoints`) — 측면 해치 문 가운데에서 앞쪽 바깥 45도로 나간다. 옮기면 그대로 따라간다. 그 순간의 해치 자리·요새 고도에서 나오고, `HatchLaunchRun`(1500m) 곧게 간 뒤 교전한다.
- 코어가 부서지거나 도시에 닿으면 남은 기체는 나오지 않는다. 오너가 바뀌면 남은 기체도 나오지 않는다(지역 상태). 먼저 만든 프리팹은 Bake가 출격 자리만 더한다. `LaunchPoints`가 비면 예전처럼 요새 둘레 800~1800m에 나온다.
- NPC 전투기는 지형만 피하고 선체와 충돌하지 않아 문 안쪽에서 시작해도 된다.

### 6-75. 함대전 대사 표·항로 검사

- **대사**: `ACO_PhaseFleet`이 `ACO_PhaseBoss`를 물려받아 요새·랜스 포대와 같은 상황별 표를 쓴다(상황 5가지, 적 줄 뒤에 아군 줄). 화자는 셋이다: 적(`EnemyName` "CARRIER GROUP"), AWACS(관제 이름·색), 함대장(`AllyName` "FLAGSHIP"·`AllyColor`, 아군 무전 채널). 기함 입장의 교전 시작·기함 피해 줄은 함대장, 함재기 출격·항모 피해 줄은 AWACS가 말한다. 예전 개별 필드(`LineContact` 등)는 없앴다.
- **섬 통과 원인**: `FindFleetLine`이 가운데·아군 시작·적 시작 세 곳의 바다만 재고, 다가가는 길은 재지 않았다.
- **처리**: 양쪽 함대가 지나는 길을 대열 폭 전체로 `FleetLaneStep`(200m) 간격으로 잰다(`LaneIsSea`, 앞뒤·좌우 여유 `FleetLaneMargin` 200m). 폭은 호위함·순양함·구축함 수로 계산하고, 적 쪽 길은 구축함이 먼저 나가는 `FleetSpacing`까지 포함한다. 멈춘 뒤 두 함대 사이는 지나지 않으므로 섬이 있어도 된다.
- 검사가 엄격해진 만큼 자리 하나에서 방향을 4개(45도 간격) 시험한다. 그래도 못 찾으면 함대전은 그 페이즈에 나오지 않는다.

### 6-76. 공중 요새 증원 대사 순서

- **문제**: 단계가 바뀌는 순간 단계 대사와 증원 대사가 한꺼번에 들어가, 밀려서 `VoiceQueueDelay`를 넘긴 줄이 버려졌다.
- **순서**: 방공망 전멸 → 곧바로 엔진 단계(엔진 공개, `SIT_ENGINES` 대사, 측면 해치 열림) → 그 대사가 끝난 뒤 `_reinforceMs`에 증원 대사와 출격. 코어 단계도 같다(돌진은 바로 시작, 돌진 대사 뒤 증원).
- 시각은 대사 길이로 정한다(`ACO_PhaseBoss.TableTime`, `ACO_Chatter.GetLineTime` — 음성·자막 길이, `Show`와 같은 계산 + `StageLineGap` 1초). 음성을 바꾸면 따라간다. `_reinforceMs`는 동기화되어 모든 클라이언트가 같은 때 증원 대사를 낸다.

### 6-77. 랜스 포대 기초 확장·방공 무리·모델 개선

- **기초**: 반경 210 → 530m(약 2.5배). 방공 12곳은 반경 165m 한 줄에서 363·462m 안팎 두 줄로 엇갈려 벌렸다(간격 약 200m). 포신·코어·냉각기·소형 레일건 자리는 그대로다.
- **평지 검사**: 기초 절반 반경 8곳 + 둘레 12곳을 재서 높이 차 `RailgunMaxRelief`(80m) 안이어야 한다. 지형이 20m 단위 육각 기둥이라 완전한 평지는 드물어, 기초를 재어 본 곳 중 가장 높은 땅에 얹고 낮은 쪽은 기초 옆면(90m)이 가린다 — 포대가 땅에 묻히지 않는다. 자리를 못 찾으면 그 페이즈에는 나오지 않는다.
- **방공 무리**: 포대 둘레 `RailgunDefenseDistance`(1200m)에 `RailgunDefenseGroups`(3) 무리, 무리마다 `RailgunDefenseSize`(4)대를 놓는다. 페이즈 목표가 아니고 일반 방공망처럼 남는다.
- **소형 레일건 레이저**: 탄착 지점에서 끊기지 않고 같은 방향으로 `LaserReach`(15km) 뻗는다. 폭발 자리는 그대로다.
- **모델**: 코일을 매끄러운 원환 3개 + 이음봉으로(법선을 직접 넣어 이음매가 없다). 냉각기 방열판은 가운데 기둥·네 귀 지지대에 끼웠고 배기구를 기둥에 얹었다. 축전기는 받침·고정 띠·칸막이·모선으로 이었다.
- **이전**: `LayoutVersion`이 낮으면 Bake(`EnsureRailgunLayout`)가 메시 자산을 같은 자리에서 고치고, 포대를 벌리고, 충돌 상자를 넓힌다. 충돌은 기초에 내접하는 정사각형 둘을 45도 엇갈린 것이다(모서리가 기초 밖으로 나가지 않게). 씬의 옛 반경 값은 `BakeRailgun`이 고친다.

### 6-78. 폭발음 다시 만들기

- **문제**: `ACO_Explosion_Big/Small`이 합성 잡음 + 무작위 찰칵(`crackle`) + 강한 `softclip`이라 지지직거렸다. 고역이 튀고(Small은 1.6kHz 위 13%, SAV 녹음은 1~3%) 저역이 파형째 깎였다.
- **처리**: SAV 폭발 녹음(MIT 라이선스, 읽기만 한다)을 가공한다. Small = 약 1km에서 터진 미사일: `MissileExplode` 0.92배속, 2.4kHz 저역 통과, 앞머리 번짐, 메아리 7개, 3.4초 — SAV 소리와 구별되게. Big = `MissileExplode` 0.75배속(충격) + `PlaneExplode` 0.6배속(몸통) + 부드러운 저음 울림, 9초. softclip 대신 앞을 미리 보는 느린 음량 조절(`level`)을 쓴다.
- 만들기: `python Tools/aco_make_sfx.py <폴더>` 뒤 두 파일만 `Audio/Generated`에 복사한다(.meta는 그대로라 연결이 유지된다). `ACO_Railgun_Fire`(4초)는 `Audio/SFX/Explode_small.ogg`를 디코딩한 `Tools/aco_explode_small_src.wav`를 낮춰 겹치고 링 변조·맑은 방전 스윕·콘덴서 험·메아리를 얹는다. 소형 레일건은 전용 `ACO_Railgun_MiniFire`(1초)를 쓴다(`LayoutVersion` 2에서 Bake가 바꾼다).
- **가까운 천둥**: 합성은 마이크에 바람 부는 소리처럼 들려 실제 녹음으로 바꿨다. BigSoundBank Thunder #2(3113)·#4(3115), CC0, Joseph SARDIN & Axeline T. `Tools/aco_thunder_rec.py`가 앞의 조용한 구간을 자르고 끝을 닫는다(각 약 3.8초·8초). 원본은 저장소에 없다.
- **미사일 스침음**(합성 유지): `crackle`과 `softclip`을 뺐다. 스침음은 쉭 소리를 1.5~5kHz로 낮추고 충격음을 다듬었다(6.5kHz 저역 통과, 6kHz 위 성분 22% → 5%). 음량 조절 `level`은 `aco_sfx_lib`로 옮겼다. 먼 천둥은 그대로다.

### 6-79. 대공포 탄 도착 판정

- **문제**: 조준 체류 판정이 쏘는 순간 끝나, 탄이 날아가는 동안(Flak 2km면 2초 넘게) 기체가 꺾어도 데미지가 들어갔다. 예광탄은 직진 예측 자리로 날아가 빗나가는데 체력만 깎였다.
- **처리**(`ACO_NPCGun.ShellHitRadius`): 데미지 주기마다 바로 맞히지 않고, 그 순간의 조준점(직진 예측)에 탄속 뒤 도착하는 탄을 표적 오너 화면에 띄운다. 도착할 때 표적이 반경 안에 있어야 맞는다. 체류·유효 각도·주기는 그대로라 직진 비행의 피해량은 같고, 데미지가 탄 도착 뒤에 들어온다.
- 반경: CIWS 20m, 그 밖의 포 45m(`ApplyShellRadius`). 공용 포대는 프리팹에, 다른 유닛은 씬에 Bake가 넣는다. 전투기 기총은 0(기존 즉시 판정)이다.
- 빗나가는 거리 ≈ ½ × 기체 횡가속 × 탄 비행 시간². 먼 거리에서 꺾을수록 잘 피하고, 가까우면 탄이 금방 닿아 맞는다.

### 6-80. 공중 요새 외피 차폐와 코어 공략

- **공략**: 코어는 격납고 안에 있고, 열린 후면 해치 통로의 축에서 정확히 겨눠야 잠기고 맞는다. 스케일 0.6 기준 통로는 약 48×23m, 입구에서 코어까지 약 180m라 축에서 좌우 약 7.5도·상하 약 3.5도 안이어야 한다.
- **외피 레이어**: `Hull`을 Environment(11)로 옮겼다. 마스크 2049를 쓰는 것이 모두 막는다: 플레이어 미사일(닿으면 터진다), 락온 시야, 포대·VLS 시야와 탄 차폐, NPC 비행 회피, 폭탄·로켓. 포대는 선체 너머를 쏘지 않고, 엔진은 뒤쪽에서, 반대편 포대는 그쪽으로 돌아가야 잠긴다.
- **Default가 아닌 이유**: 땅을 재는 레이가 위에서 내려오므로 선체를 땅으로 읽어 전차가 선체 위로 뛰어오른다. 지형은 Default만 쓰고 Environment는 비어 있어, `BakeFortress`가 생성기·이동 유닛·호위 유닛의 `GroundLayers`에서 11을 뺀다.
- 해치 막이도 격침 때 끈다(추락 판정이 자기 막이에 걸리지 않게). 번개는 선체에 떨어질 수 있다.

### 6-81. 플레이어 ECM(전자전 포드)

- **장착**: 특수무장 `ECM`(가격 15000, 격납고 기타 분류). `ACO_DFUNC_Missile.EcmMode`로 만들어 슬롯·다이얼·격납고·HUD를 무장과 같이 쓴다. 해금 키는 ID `ECM`로 고정한다.
- **동작**: 트리거로 켜면 10초 재밍, 끝나면 20초 재충전. 반경 5km(`ACO_RadarContacts.EcmRange`). 비행 HUD 잔탄 칸에 `ON 12` / `+8` / `RDY`.
- **효과**(적만, 켠 사람 주변): 적 미사일은 구역 안에 1초 머물면 표적을 놓친다. NPC 락온은 구역 안 표적에 쌓이지 않고 풀린다. 적 대공포는 조준 흔들림 3배, CIWS 요격 시간 1.5배. 적기 기총은 방해하지 않는다. 플레이어 미사일과 아군 함선·헬기는 영향이 없다.
- **판정**: 미사일 차단은 쓸모없는 계산이 없게 한 번 끊고 끝낸다(유도 약화보다 가볍다). 켜는 이벤트를 모두에게 보내고 끝은 서버 시각으로 계산한다. 켠 ECM이 없으면 `IsJammed`가 바로 false, 있으면 미사일·런처·포대가 0.25초마다 거리만 잰다.
- **표시**: 켜진 ECM의 구역을 HUD 레이더와 조종석 지도에 초록 테두리 원으로 그린다(화면 밖 호는 뺀다). 내 기체가 구역 안이면 비행 HUD에 `ECM SUPPORT`, 소리 `ACO_Ecm_Loop`(레일건 충전 같은 전자음 + 약한 지직거림, `Tools/aco_make_sfx.py`)가 나온다. 소리는 Bake가 `ACO_RadarContacts` 자식 `EcmSound`로 만든다.

### 6-82. 비행 HUD 값 상자 위치

- 속도·고도 값 상자가 흘러가는 테이프 숫자와 겹쳐, 상자를 테이프 바깥(x ±21.6)으로, 스로틀을 x -26으로 옮겼다. 먼저 만든 프리팹은 Bake(`EnsureFlightHudLayout`)가 옮긴다.

### 6-83. 스로틀 위치 표시, 지상 기어 잠금, 갑판 택싱 제한

- **스로틀 위치**: 엔진 출력 막대 왼쪽의 `>`가 `ThrottleInput`(목표 출력)을 가리킨다. 막대는 실제 출력이라 `>`를 뒤따라간다. 조종사에게만 보인다.
- **지상 기어 잠금**: `ACO_VehicleAutomation`이 `Taxiing` 동안 `DFUNC_Gear.DisableGearToggle`을 올린다. 갑판뿐 아니라 모든 지면에 적용한다. SAV의 `AllowToggleGrounded`를 끄면 동체 착륙 뒤 기어를 내릴 수 없어(지면 레이 검사) 쓰지 않았다.
- **갑판 택싱 제한**: 갑판(`ACO_PlayerProgress.SafeZones`) 위에 닿아 있고 `DeckTaxiSpeed`(10m/s, 약 19kt) 이하이면 제한이 걸려 `FixedUpdate`에서 수평 속도를 그 값으로 묶는다. FA-18의 충돌 사망 속도(30m/s) 아래이고, 스로틀은 건드리지 않아 `>`와 조종사 입력이 그대로 남는다. `ThrottleOverride`는 `PlayerThrottle`까지 덮어써서 쓰지 않았다.
- **걸리지 않는 경우**: 캐터펄트에 걸린 동안, 제한보다 빠른 채로 갑판에 닿은 경우(착함 실패 뒤 재이륙), 비행장 활주로. 갑판에서 활주 이륙은 할 수 없고 캐터펄트로만 뜬다.

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

### 6-85. 그늘 밝기와 먼 바다

- **그늘**: 해를 등진 면은 환경광으로만 밝다. 땅 쪽 색 0.28은 감마 값이라 선형 0.06으로 하늘 쪽의 1/5이었다. 메뉴 `ACO → Lighting → Brighten Shadows`가 환경광 3색(하늘 .62/.71/.84, 지평 .62/.66/.70, 땅 .48/.46/.43), 태양 그림자 세기 0.8, 후처리 대비 0을 넣는다. 태양 구성은 건드리지 않는다. 구운 곳(항모·격납고 프로브)은 라이트를 다시 구워야 바뀐다.
- **반복 무늬**: 고정 배율 타일이 먼 곳까지 그대로 보였다. 물결 UV를 저주파 무늬로 비틀고(`_WarpScale`, `_WarpAmount`), 140m 물결은 12km까지 끄고(`_MidFadeStart/End`), 먼 곳은 4300m 배율로 넘긴다(`_LargeScale`). 큰 얼룩은 배율이 다른 두 겹을 섞는다. 텍스처 표본이 3개 늘었다.
- **먼 바다 빛남**: 스치는 각도에서 Fresnel 반사가 1로 가고, 먼 거품 무늬가 뭉개져 희게 떴다. `_FarSmoothness`를 0.62로 낮추고(Unity 반사는 매끄러움이 낮으면 스침 반사도 준다), 거품은 5km에서 끄고(`_FoamFadeStart/End`), `finalcolor`에서 먼 바다를 안개색 × `_FarGlowLimit` 아래로 제한한다(`_FarGlowStart/End`). `finalcolor`를 쓰면 자동 안개가 빠져 포그 날씨에 바다만 선명해졌다 — `nofog`로 끄고 같은 함수에서 전역 안개를 직접 입힌다.

### 6-86. 총탄이 스치는 소리 (`ACO_BulletFlyby`)

- **동작**(로컬): `ACO_NPCGun`이 발사 중이고 표적이 내가 조종하는 기체이며, 탄이 내 기체에서 `MissRadius`(60m) 안으로 지날 만큼 조준이 맞으면 소리를 예약한다. 소리는 탄이 날아오는 시간(거리 ÷ 탄속)만큼 늦게 난다. 맞든 빗나가든 난다.
- **간격**: 총이 몇 문이든 전체 0.12~0.32초 간격. 2D 소스 3개를 돌려 쓰고 쏘는 쪽으로 좌우만 치우친다(기체가 빨라 3D 소리는 재생 중에 뒤로 처진다).
- **소리**: `Tools/aco_make_whiz.py` — SAV 도탄음 `ricochet2.wav`를 빠르기 네 가지로 바꾸고 짧은 공기 잡음을 얹은 `ACO_Bullet_Whiz_1~4`. 교체는 `BulletFlyby`의 `Clips`.
- **설치**: `BakeBulletFlyby`가 `ACO_RadarContacts` 아래에 만들고 아군이 아닌 기총과 잇는다.

### 6-87. 기체 표시 — 헐넘버·콜사인·킬마크 (`ACO_VehicleMarks`)

- **프로젝터를 쓰지 않는 이유**: 프로젝터는 틀에 걸친 메시를 통째로 다시 그린다(FA-18은 렌더러가 잘게 나뉘어 비싸다). 표시는 기체에 붙인 판 항목당 1드로우다. 데칼 프로젝터도 안 보일 때는 오브젝트째 끈다.
- **Z파이팅**: 셰이더 `ACO/MarkText`가 정점을 카메라 쪽으로 당긴다(`_PullPercent` 거리의 0.5% + `_PullMeters` 5cm, 값이 클수록 많이). 화면 자리는 그대로다. 150~300m에서 사라지고, `ShowRange`(400m) 밖에서는 오브젝트를 끈다. 앞의 물체를 뚫어 보이면 두 값을 줄인다.
- **데이터**: 새 동기화 없음. 조종사의 `PlayerData`를 읽는다 — 헐넘버 `aco_hull_no`(콜사인 패널 아래 HULL NUMBER, A-Z 0-9, 최대 6자), 콜사인 `aco_tac_name`(비어 있으면 표시하지 않는다), 최고 격추수 `aco_best_kills`(`ACO_PlayerProgress.AddMissionKill`). 두 키는 절대 바꾸지 않는다.
- **킬마크**: 한 미션에서 내가 쓰러뜨린 표적 수(공중·지상 모두)의 최고 기록. 100·10·1 단위 아이콘(날개 달린 별·전투기 3기 편대·전투기, 검은색 `IconColor`)으로 줄인다. 폰트에 기호가 없어 `Tools/aco_mark_icons.py`의 거리값 아틀라스 `Textures/Marks/ACO_KillMarks.png`로 메시를 만든다. 한 줄 `IconColumns`(10)칸, 최대 `MaxIcons`(30)개.
- **집계**: 최고 격추수는 미션이 끝날 때(`GrantReward`, 성공·실패 모두) 저장한다. 도중에 나가면 기록되지 않는다.
- **격납고 미리보기**: 데칼 스튜디오 `SampleRoot` 아래 `ACO_Marks`(`PreviewMode`)가 탑승자 대신 내 값을 보여 준다. 다른 사람에게도 각자 자기 값이 보인다.
- **설치**: `BakeVehicleMarks`가 플레이어 기체 프리팹의 `SaccEntity` 아래(FA-18은 자식 `F/A-18E`)에 `ACO_Marks`를 넣는다(이미 있으면 건너뜀). 자리는 FA-18 어림값이라 프리팹의 `ACO_Marks/Marks` 아래에서 맞추고, Bake가 같은 자리를 미리보기로 복사한다. 킬마크는 실행 중에 메시를 만들므로 에디터용 메시 `Generated/ACO_KillMarkPreview.asset`(199킬 모양)을 `Kills_L/R`에 넣어 두고, 실행하면 실제 값으로 바뀐다.
- **폰트**: Black Ops One(OFL, `Fonts/BlackOpsOne-OFL.txt`). Bake가 A-Z 0-9 - 공백만 담은 고정 SDF 폰트 `Fonts/ACO_MarkFont.asset`을 만든다 — 콜사인·헐넘버 입력 규칙이 같아 한글 폰트가 필요 없고, 동적 폰트를 쓰지 않으며, 빌드에 폰트 파일이 들어가지 않는다.

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

### 6-90. 작전판 순위와 패널 정리

- **최고 획득 순위**: `ACO_PlayerProgress`가 미션 끝(`GrantReward`)에 그 미션 보상의 최고 기록을 `PlayerData` `aco_best_reward`에 저장한다. 작전판 `BestText`가 3초마다 인스턴스의 사람들 값을 읽어 높은 순으로 8명을 보여 준다. 다른 사람 값은 같은 인스턴스에서만 읽히므로 전 세계 순위가 아니다. 키는 절대 바꾸지 않는다.
- **작전판**: 프리팹 크기 0.7배(2.8 x 1.75m). 제목 칸은 줄바꿈 + 자동 글자 크기로 칸을 넘지 않는다. 조종사 집계는 8명까지. 먼저 만든 프리팹은 `EnsureBoardLayout`.
- **격납고**: 층을 판 < 항목 < 카드로 정했다 — 항목은 카드 안에 파인 어두운 칸이고 왼쪽 끝 가는 선으로 눌리는 곳을 보여 준다. 상태는 채도(보유 초록, 장착 진한 초록, 고름 금색)로만 나타내고 구매 버튼만 밝다. `ApplyHangarStyle`, 먼저 만든 프리팹은 `EnsureHangarStyle`.
- **관전 패널**: 아이콘 버튼의 글자를 아이콘 오른쪽 남은 칸 가운데에 두고 넘치면 줄인다(`SetIconGap`, 간격 18). 다른 패널의 새 버튼도 같다. 제목 아이콘도 띄웠다.

### 6-91. 구름층 카메라 페이드

- `ACO/CloudLayer`: 카메라에서 `_CameraFadeNear`(120m)까지는 지금 안개색, `_CameraFadeFar`(1800m)부터 원래 무늬로 섞는다. 층을 지날 때 판 경계가 코앞에서 보이지 않는다.
- 알파가 아니라 색만 바꾼다: 판이 깊이를 쓰고 바다가 판 뒤에 그려져, 투명하게 하면 바다 자리에 구멍이 난다. 뒤쪽은 안개색으로 가려져 보인다.

### 6-92. 체적 구름층 (`ACO/CloudVolume`)

- **문제**: 층이 한쪽 면만 그려지는 판이고 층 속 안개는 고도로 바뀌어, 경계를 지날 때 판이 사라지며 튀었다.
- **방식**: 판을 끄고, 카메라를 감싸는 구에서 픽셀마다 시선이 층을 지나는 길이로 `1 - exp(-밀도 × 길이)`를 계산한다. 아래·안·위가 같은 식이라 끊기는 곳이 없다. 시선은 깊이 텍스처의 물체와 바다에서 멈춘다.
- **값**: `DeckVolumeDensity` 0.005, `FogVolumeDensity` 0.004, 위·아래 색은 `ACO_CloudField`. 윗면·아랫면 높이는 `_Bump`(120m), 노이즈 3옥타브. 층 위 맑은 공기는 층 전체를 지나는 동안 섞는다. `CloudVolume`을 비우면 예전 판으로 돌아간다.
- **투명 물체**: Standard Fade·파티클은 체적 층보다 뒤에 그려지고 깊이에도 없어 체적 층을 받지 못한다. 그래서 카메라가 층 안에 있으면 전역 안개도 함께 쓴다(`VolumeGlobalFog`, 비구름층 경계 ±200m에서 섞는다). 불투명한 물체는 양쪽을 받는다. 카메라가 층 밖이면 층 속 투명 물체는 전역 기본 안개만 받는다.
- **구름 품질**: HUD 설정 GRAPHICS의 CLOUD QUALITY(`PlayerData` `aco_hud_cloud_quality`, 내 화면만). HIGH = 체적, LOW = 예전 판과 층 속 안개.
- **ACO_Sky는 하나만**: 둘이면 날씨가 따로 돌아 구름층이 남는다. Setup Check가 켜고 끄며, Bake는 켜진 첫 번째만 연결한다.
- **비용**: 층이 있는 날씨에서 전체 화면 패스 하나(깊이 1회 + 노이즈). 층이 없으면 오브젝트를 끈다.
- **가까운 물체**: 깊이 텍스처에는 그림자 패스가 있는 물체만 들어가, 스크린(Unlit)처럼 없는 물체는 그 뒤 거리만큼 안개가 덮여 뚫려 보였다. 구를 카메라에서 `_NearRadius`(40m) 떨어뜨려 실제 깊이 버퍼로 검사하므로 그 안의 불투명한 물체는 덮이지 않고, 진하기는 `_NearFade`(90m)까지 0에서 올라간다. 40m 밖의 깊이 없는 물체는 여전히 덮인다.
- **한계**: 깊이 텍스처가 없는 플레이어(그림자 끔)는 40m 밖의 물체까지 덮인다. 뭉게구름은 그림자 패스가 있어 깊이에 들어간다.

### 6-93. 함대전 함선 교전 조건

- **적 함선**: 최저 고도(20m) 때문에 해수면의 함선 마커가 걸러졌다. `ACO_PrefabTuning.TuneShip`이 프리팹의 VLS·ASM·함포에 `MinTargetAltitude` -100000, 엔티티 `AAMTargetsLayer`에 26을 넣는다. CIWS는 이름으로 구분해 건드리지 않는다.
- **사거리**: 두 함대는 3.5km에서 멈춘다. 탐지·추적 거리가 4.5km 미만인 무장만 5km·6km로 올린다. 직접 정한 ASM 4.5km는 유지한다.
- **교전 시점**: 시작 거리 10km, 양쪽 14m/s라 5km까지 약 3분 걸린다. 그 전에는 쏘지 않는다.
- **하늘**: 전부 꺼지면 `ACO_Weather`가 돌지 않아 맑음에 멈춘다. Setup Check의 하늘 항목으로 확인한다.

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

### 6-95. 소형 레일건 프리팹

- **프리팹**: `NPC_Turret_RailgunMini` 하나를 고치면 네 문이 함께 바뀐다. 체력, 사거리, 사격 값, 모델, 레이저, 소리가 들어 있다. 자리와 크기만 인스턴스 값이다. 탄·폭발 파티클은 랜스 포대의 `MiniFx`를 함께 쓴다.
- **사격 순서**: 조준 레이저 `AimTime` 1.5초 → 표적의 `LeadTime` 3초 뒤 자리에 포신 고정 → 탄이 고정 3초 뒤에 닿도록 늦춰 발사(`ShellSpeed` 5000m/s). 탄이 1초 걸리면 고정 2초 뒤에 쏜다.
- **최소 폭발 거리**: `MinRange` 1km. 고정한 자리가 그 안이어도 쏘지만, 안에서는 터지지 않고 직격(`DirectHitRadius` 15m, `DirectDamage` 60)만 맞는다. 빗나간 탄은 1km에서 터져 폭발 지역을 연다. 직격은 맞은 사람이 판정하고, 그 사람의 화면에서만 폭발이 취소된다.
- **회피**: 고정된 레이저가 3초 동안 폭발 자리를 보여 준다. 직진하면 맞고, 방향을 틀면 피한다.

### 6-96. 격납고 OTHER 탭

- **탭**: 강화 탭에 OTHER를 더해 GUN DAMAGE를 AIRFRAME에서 옮겼다. 탭은 AIRFRAME, MISSILE, SP. AAM, SP. AGM, SP. BOMB, OTHER, ITEMS 순이다.
- **ITEMS**: 소모품 탭의 화면 이름을 SUPPLY에서 바꿨다. 구매한 항목은 EQUIPPED로 표시한다. 코드 상수 `TAB_SUPPLY`는 그대로다.
- **탭 설명**: 탭 아래에 설명 한 줄(`ACO_Shop.TabInfoText`)을 둔다. 강화 탭은 내용을, ITEMS는 한 미션용임을 알린다. 글자는 `ACO_Shop._tabInfo`에 있고, 자리를 내려고 줄 간격을 110에서 100으로 줄였다.
- **저장**: 항목 번호 `UP_GUN` 5와 저장 ID `gun_damage`는 그대로다. `_categoryStarts`, `_categoryLengths`가 탭을 나누므로 강화 레벨과 효과는 바뀌지 않는다.
- **이름**: 탭 이름은 씬 값이 아니라 코드에 둔다. 씬에 옛 5칸 값이 남아 있어도 영향이 없다.

### 6-97. FA-18E 추가 장갑판 외형

- **형태**: 조종석 뒤쪽 판과 흡입구 옆면 판, 좌우 2장씩. 톱니는 모두 높이 35cm, 깊이 10cm로 같고 끝이 기수를 향한다. 판 높이 2.4cm, 가장자리 3mm.
- **제작**: `Tools/aco_armor_kit.py`(Blender 스크립트)가 본체 표면에 판을 투영하고 텍스처와 FBX를 만든다. 판과 동체 사이 거리를 재서 파고드는 곳이 없음을 확인했다.
- **재질**: 판 자리의 본체 도장색을 읽어 14% 어둡게 한다. 패널선과 리벳 줄로 본체 도장에 맞춘다.
- **설치**: `Models/Armor/ACO_ArmorKit_FA18E.fbx`를 기체에 직접 놓고 `ConsumableVisuals[CONS_ARMOR]`에 넣는다. 항공기 원점 기준이고 +Z가 기수, 1단위가 1m다. 격납고 샘플은 `ConsumablePreviewVisuals`에 넣는다.

### 6-98. 외부 무장 컨테이너 외형

- **용도**: 추가 탄약(`CONS_AMMO`) 외형. 안쪽 파일런 `WeaponVis/Weapon/pyronin.L/R` 아래 기존 `Tank` 자리에 넣는다. 평소에는 닫힌 채로 쓴다.
- **메시**: `Models/WeaponPod/ACO_WeaponPod.fbx`에 몸체, 도어 좌우, 내부(벽·레일)가 나뉘어 있다. 도어 원점은 경첩이고, 길이 방향 축으로 서로 반대로 돌리면 바깥으로 열린다. 도어 옆면 끝은 톱니, 아랫면 끝은 일자다.
- **축**: 기존 `Tank`와 같다(+Z 기수, +Y 아래, 원점 = 파일런 바닥 0.28m 아래). `Tank`의 Transform 값을 붙이면 같은 자리다.
- **크기**: 길이 4.55m, 폭 0.82m, 높이 0.64m. 표기는 모두 가공이고 `Tools/aco_weapon_pod.py`에서 고친다.

### 6-102. 대응탄 포드 외형

- **용도**: 소모품 `CONS_CM`의 외형이다. 효과는 그대로 플레어 1.5배이고, 플레어는 기존 방출 지점에서 나간다. 모델은 외형만 맡는다.
- **파일**: `Models/FlarePod/ACO_FlarePod_FA18E.fbx`(한 메시, 좌우 한 쌍, 864삼각형)와 `ACO_FlarePod_Albedo.png`. `Tools/aco_flare_pod.py`가 만든다. 원점은 항공기 원점이라 FA-18E 아래에 Transform 0으로 넣으면 제자리다. 칸은 `ConsumableVisuals[1]`이다.
- **자리**: 엔진 나셀 아랫면, 바퀴 수납부 뒤다. 표면에 평면을 맞췄고 밑면은 표면 안에 묻힌다.
- **모양**: 앞이 낮은 경사로이고, 3 × 6칸 발사 격자가 있다. 톱니 판은 기수 쪽을 향해 장갑판과 맞춘다. 색은 포드 자리의 본체 도장에서 읽는다.

### 6-105. 함내 격실 계획 (구획 확정, 아직 만들지 않음)

- **기준**: 맵 밖 독립 공간에 만들고 텔레포트로 잇는다. 해수면 위에 둔다(`ACO_Underwater`). 키 150cm와 앉은 사용자를 기준으로 높이를 잡는다.
- **구획**: 브리핑실 7 x 6m(높이 3.2), CIC 7.8 x 5.8m(높이 2.8), 복도 14.4 x 2.2m(높이 2.6), 침실 6.4 x 6.1m(높이 2.6). 브리핑실 뒷벽의 문 2개가 CIC와 복도로, CIC 문이 복도로 이어진다. 복도 끝에 비행갑판 해치, 옆에 격납고 문이 있다.
- **브리핑실**: 앞벽에 평면 지도 1.6 x 1.6m와 작전판 3 x 1.9m(이번 미션), 왼쪽 벽에 작전 콘솔 1.8 x 1.1m(리롤 포함), 오른쪽 벽에 크레딧 보드, 좌석 12개. 등받이는 1.04m라 뒷줄에 앉아도 작전판이 보인다.
- **CIC**: 가운데 전술 테이블(높이 0.65m, 지도 반경 1.1m), 안쪽 벽에 관전 패널, 오른쪽 벽에 무전석, 벽을 따라 PC 책상 4개와 장비 랙 3개. 디버그 패널은 왼쪽 벽 문 옆.
- **복도 움푹한 공간**: 하이스코어판 1.6 x 1.2m + 트로피 진열장. 침실: HUD 설정 패널, 2단 침대 4조, 사물함. 격납고 패널, TAC 네임, 데칼 스튜디오는 격납고에 둔다.
- **평면 지도**: 지형, 공역 테두리, 목표, 적을 표시한다. `ACO_CockpitMap`은 지금 탄 기체에서만 돌아가므로 벽걸이용으로 고쳐야 한다.
- **하이스코어**: 작전판 `BestText`를 별도 판으로 옮긴다. 기준은 한 미션 최고 획득 크레딧(`aco_best_reward`)이다. 격추 패널티가 반영되고 팀 몫이 나뉘어 점수 다툼이 덜하다. 같은 인스턴스 사람들끼리의 순위다.
- **무전**: SAV `SaccRadioZone` 2개(반경 2.85m, 채널 1)를 CIC 좌우에 나란히 둔다. 책상 뒤 구석과 복도 문 앞은 범위 밖이다. SAV 코드는 고치지 않는다.
- **조명**: 천장 칸에 노출형 형광등을 둔다. 브리핑실은 앞칸을 비워 화면을 살리고, CIC는 테이블 위를 비우고 양 끝에만 어두운 등을 둔다. 전부 굽고 라이트 프로브와 반사 프로브를 쓴다. 벽과 천장이 그림자를 드리워야 태양 직사광이 막힌다. `HangarPoint`를 둬야 `ACO_LightingSetup`이 브리핑실에 격납고 조명을 깔지 않는다. `ACO_Rain.ShelterBoxes`에 구획을 넣는다.
- **모듈**: `Tools/aco_interior_models.py`가 `Models/Interior/ACO_Interior.fbx`와 `Textures/Interior`를 만든다. 작업 축은 x 오른쪽, y 앞, z 위이고 메시 이름은 `모듈__재질`이다. 아직 프리팹에 조립하지 않았다.
- **수밀문**: 개구부 1.1 x 1.95m, 큰 둥근 모서리. 틀(`Int_HatchFrame`)과 문짝(`Int_HatchLeaf`, 원점 = 경첩 축)이 나뉘어 열린 자세로도 놓을 수 있다. 가운데 손잡이 바퀴에서 막대가 잠금 레버로 뻗고, 뒷면은 바퀴와 손잡이만 있다.
- **벽시계**: `Int_Clock` 몸체와 `Int_ClockHour`, `Int_ClockMinute`, `Int_ClockSecond` 바늘이 나뉘어 있다. 바늘 메시의 원점이 회전축이고 12시를 가리키며, Unity에서 로컬 Z축으로 돈다. `ACO_WallClock`이 보는 사람 컴퓨터 시각으로 1초마다 돌린다.
- **액자 사진**: 사진마다 `Int_Photo_00~06` 메시와 재질이 따로이고 UV가 0~1을 꽉 채운다. `Textures/Interior/Photos/ACO_Photo_00~06.png`를 3:2 이미지로 바꾸면 된다. 도구를 다시 돌려도 이미 있는 사진은 덮어쓰지 않는다.
- **연결 검사**: `Tools/aco_interior_check.py`가 모듈 안의 끊어진 조각과 구획 안의 뜬 모듈을 4mm 기준으로 찾는다. 파고듦, 겹침 자체는 검사하지 않는다.
- **복도**: x 6.2~20.4, y 1.1~3.3, 높이 2.6. 벽은 1.6m 칸(판 1.4 + 늑골)이고 칸 가운데는 x = 6.9 + 1.6k다. 문은 칸 가운데에 있다: 남쪽 CIC x 10.1, 침실 x 16.5, 북쪽 격납고 x 18.1(닫힘, 텔레포트), 동쪽 끝 비행갑판(닫힘, 텔레포트), 서쪽 끝 브리핑실 y 2.2.
- **게시 공간**: 복도 북쪽 x 11~14, y ~4.4. 뒷벽에 하이스코어 틀 `Int_Frame_120`(1.6×1.2, 가운데 높이 1.75), 그 아래에 트로피 진열장 `Int_TrophyCase`(높이 1.05)와 `Int_Trophy_0~4`를 둔다.
- **침실**: x 14.2~20.4, y -5.3~0.9, 높이 2.6. 벽 칸마다 2단 침대 `Int_Bunk`(머리가 남쪽 벽, 커튼은 걷힌 채 고정, 서랍은 옆면), 서쪽 벽에 사물함 5개, 북쪽 벽에 TV, 동쪽 벽에 HUD 틀 `Int_Frame_Hud`(2.1×0.95, 가운데 높이 1.58). 침대 뒤에는 조명을 두지 않는다.
- **벽 배선**: 벽에 붙는 전원함, 배전함, 분전반의 전선관은 위로 올라간 뒤 벽 속으로 꺾여 들어가고 받침과 나사가 있다. 밸브 묶음의 관도 양 끝이 벽으로 꺾인다. CIC 벽 전선관 묶음은 천장판까지 올라간다. 관은 벽 띠를 뚫지 않도록 벽에서 5cm 띄운다.
- **벽 띠**: 높이 1.05m에만 있다. 2.45m 띠는 뺐다. 격실 껍데기에 들어 있고, 칸마다 양옆 늑골 날개까지 이어진다. 코너에서만 맞은편 띠에 닿지 않게 줄인다.
- **침실 TV**: 북쪽 벽 앞 책상(`Int_TvDesk`, 높이 0.6m) 위에 받침 달린 1.6 x 0.9m TV(`Int_Tv`)를 놓았다. 열린 문짝에 가리지 않게 동쪽으로 붙였다. 화면 `TvScreen`은 합치지 않은 렌더러이고 UV가 0~1을 채운다. 재질은 `_MainTex`가 검은색이고 발광 1이라, 영상 플레이어가 `_MainTex`에 넣으면 조명과 상관없이 밝게 나온다. 식탁은 뺐다.
- **골조**: 늑골마다 천장 보가 벽에서 벽까지 이어진다. 문짝이 앞을 지나가는 늑골만 낮은 `Int_RibFlat_*`이고, 끊긴 띠로 대신하지 않는다. 문짝은 160도로 열어 고정한다. 해치 벽판에도 벽 띠가 이어진다.
- **천장 설비**: 관과 트레이, 덕트는 격실 벽에서 벽까지 칸 길이를 나눠 이어지고 양 끝에 벽 통과 틀(`Int_SealPipe`, `Int_SealSmall`, `Int_SealTray`, `Int_SealDuct`)이 있다. 보 아래를 지난다. 형광등은 봉 둘로 천장에서 0.24m 내려와 보 아래에 달린다.
- **침대**: 지붕과 옆판이 있고, 열린 옆과 발치를 ㄱ자 봉의 커튼으로 가린다. 커튼은 걷혀 있다. 충돌은 침대 전체 상자다.
- **안 보이는 면**: `prune_hidden`이 내보낼 때 어느 배치에서도 눈에 닿지 않는 면을 지운다(벽 뒤, 바닥에 닿는 면, 물체 안쪽). 도는 시계 바늘은 뺀다. 바깥 면이 없으므로 격실마다 그림자 전용 `SunBlock` 상자가 햇빛을 막는다.
- **검사 추가**: `aco_interior_check.py`가 벽 소품과 골조 및 패널 틀의 겹침, 같은 방향 면이 1.2mm 안에서 겹치는 Z파이팅 후보, 방 안에서 쏜 광선이 바깥으로 새거나 뒷면을 맞는 구멍도 찾는다.
- **유리**: `glass` 재질은 빌더가 Standard Transparent로 만든다. 그림자를 드리우지 않는다.
- **프리팹**: `ACO_InteriorBuilder.Build`가 레이아웃을 읽어 `ACO_Interior`를 만든다(프리팹 생성 창의 "함내 격실"). 격실마다 재질별 메시 하나로 합치고, 메시는 `Generated/Interior/ACO_Interior_Meshes.asset`에 모인다. 시계만 따로 두고 `ACO_WallClock`을 붙인다.
- **충돌**: 격실마다 `Col_Walls` 메시 콜라이더(바닥, 천장, 벽판, 해치 벽, 늑골)와 `Col_Furniture` 상자(의자, 책상, 랙, 사물함, 침대, 문턱, 닫힌 문짝). 회전의자와 소품에는 충돌이 없다.
- **조명**: 빛은 전부 굽는다. 형광등마다 구운 점광원(격실별 색과 세기는 `ACO_InteriorBuilder.RoomLights`), 벽판은 라이트맵 20텍셀/m, 바닥과 천장은 Blender로 구운 빛 텍스처, 늑골, 보, 띠, 소품은 라이트 프로브, 격실마다 박스 투영 반사 프로브 256. 조명과 프로브는 프리팹에 들어 있고, 씬에 놓은 뒤 [라이트 굽기]를 해야 밝아진다. 라이트맵 배율 5는 씬 해상도 4텍셀/m를 전제한다. 굽기는 Progressive GPU다. 로그에 `GBuffer ... out of memory`가 찍히면 그 메시가 까맣게 구워지니, 플레이 모드와 GPU를 쓰는 다른 프로그램을 끄고 다시 굽는다. 계속 실패하면 CPU로 바꾼다(`ACO_LightingSetup.BakeBackend`). 등기구는 굽기에서 그림자를 드리우지 않는다. 바닥과 천장은 Unity 라이트맵에서 격실마다 까맣게 구워져(원인 불명, 메시와 UV는 정상) `Tools/aco_interior_bake.py`가 Cycles로 구운 `ACO_Int_BakedLight.exr`(1024, 32텍셀/m)을 두 번째 UV로 읽는다. 형광등이나 가구를 옮기면 다시 돌린다. 밝기는 재질의 `_BakedLightScale`로 맞춘다. 세기는 도구의 `ROOM_LIGHTS`와 빌더의 `RoomLights`를 같게 둔다. [라이트 굽기]는 씬에 라이트 설정 에셋이 없으면 ACO 설정을 만들어 잇는다. 격실 공간은 `ACO_Interior_Rooms.txt`에 있다.
- **셰이더**: 불투명 재질은 `ACO/Interior`다. 라이트맵이나 프로브 + 박스 투영 반사 + 발광만 쓴다. 태양과 안개를 받지 않고 실시간 조명 패스가 없다. 씬 라이트맵에 방향 정보가 없어 노멀맵은 위에서 비추는 빛을 가정해 살린다. 유리만 Standard Transparent다.
- **패널 맞추기**: 메뉴 `ACO/Interior/Fit Panels To Frames`가 씬의 패널을 틀로 옮기고 비율을 유지해 줄인다. 브리핑실 앞벽은 작전 콘솔(3 x 1.9m)과 평면 지도 자리(1.6 x 1.6m), 왼쪽 벽은 작전판, 오른쪽 벽은 격납고 복제 패널 `ACO_Hangar_Briefing`이다(틀은 패널 비율에 맞춰 왼쪽 2.4 x 1.52m, 오른쪽 2.4 x 1.65m). HUD 설정은 침실 동쪽 벽 2.1 x 1.46m다. 관전 패널은 화면을 CIC 안쪽 벽 틀(안쪽 16:9)에 딱 맞추고, 버튼 캔버스를 조작대 앞 틀로 옮긴다. 패널 크기는 1로 둔다. 디버그 패널은 비공식 기능이라 격실에 두지 않는다. 라이트를 굽기 전에 맞춰 본다.
- **격납고 복제 패널**: 원본과 같은 프리팹이다. 강화와 소모품은 `Progress` 판 번호로 저절로 맞고, 고른 특수무장은 `ACO_Loadout`이 저장 값을 따라가 패널끼리 맞춘다. 패널을 새로 만든 뒤 Bake를 해야 연결된다.
- **기능부**: 프리팹의 `Systems` 자식이 구역, 문, 무전을 맡는다. `ACO_InteriorBuilder.UpdateSystems`가 이 자식만 다시 만들어 메시와 구운 빛이 그대로다.
- **구역**: `ACO_InteriorZone`의 상자가 격실을 덮는다. 안에서는 비가 오지 않고, 해수면 아래여도 물속 연출이 없고, 표적 마커와 크로스헤어를 그리지 않고, 안전지대로 친다(미션이 끝나도 격실에 남는다). 격실이 해수면 아래(y -2500)에 있어 필요하다.
- **문**: 복도의 닫힌 문 둘은 `ACO_Teleporter`다. 끝 문은 갑판, 옆벽 문은 격납고로 간다. 돌아오는 문은 `ACO_ShipHatch_Hangar`, `ACO_ShipHatch_Deck` 프리팹이고 함선 벽에 손으로 놓는다. Bake가 양쪽 Target을 서로의 도착 지점에 잇는다. 도착하면 문을 등지고 1m 앞에 선다.
- **CIC 무전**: SAV 구역은 구라서 하나로 네모난 방을 맞출 수 없다. 3 x 2칸으로 나눈 `SaccRadioZone` 6개를 깔고, 벽에서 0.2m 뺀 칸의 대각선 절반(약 1.83m)을 반지름으로 쓴다. 몸이 닿는 바닥을 다 덮고 다른 격실에는 닿지 않는다. 채널 1. Bake가 `SaccRadioBase.RadioZones`에 더하고 무전기 구역은 그대로 둔다. 구역 사이를 지나면 SAV가 구역을 다시 확인할 때까지 잠깐 끊길 수 있다.
- **관전 조작대 콜라이더**: 조작대 앞면에 관전 패널 버튼이 붙는다. 상자 콜라이더는 버튼 뒤에서 끝난다(`SpectatorDeskDepth`). 메시 범위 그대로 두면 버튼을 덮어 레이저를 막는다.
- **작전 테이블 북쪽**: `MapRoot`의 +Z가 북쪽이고, 그쪽 가장자리에 평면 지도와 같은 초록 삼각형(`NorthMark`)을 눕혀 둔다. 기존 지도는 Setup Check가 넣는다.
- **바다와 프로브**: 바다 평면과 너울 원판은 라이트 프로브와 반사 프로브를 쓰지 않고 하늘만 비춘다(`ACO_OceanLook.UseSkyOnly`). 원판 메시는 카메라를 따라가려고 범위가 위아래 50km라서, 프로브를 켜면 y -2500의 격실 프로브까지 잡아 경계에서 색이 갈린다.
- **크레딧 보드**: 격실 도구가 배치하지 않는다. 침실 빈자리에 손으로 놓는다.
- **평면 지도**: `ACO_PlanMap`은 `ACO_CockpitMap`의 `WallMode`다. 기체 없이 12m 안에 내가 있을 때만 갱신하고 나 표시가 없다. 접촉 목록을 `Wake`로 깨운다.
- **작전판**: 기록 왼쪽 세로 막대에 겹친 쉐브론(최신으로), 위, 아래 버튼이 있다. 기록은 최근 것이 위다. 스코어 칸 아래 `MyResultText`가 이번 미션의 내 격추 수와 크레딧을 보여 준다. 진행 중에는 지금 성공으로 끝날 때의 예상 값(~)이고 보상이 나오면 확정 값이다.
- **하이스코어판**: `ACO_HiScoreBoard`는 글자만 있는 판이고, 순위는 작전판(`ACO_MissionLog.BestText`)이 쓴다. Bake가 잇는다. 작전판에서는 최고 획득 순위를 뺐다.
- **전술 테이블**: 씬에 없으면 `Fit Panels To Frames`가 받침 없는 지도를 만들어 CIC 테이블 유리판 위에 놓는다. 반경 1.1m, 6m 안에서 갱신한다.
- **아직 없는 것**: 씬 배치, 텔레포트, 비 가림(`ACO_Rain.ShelterBoxes`), 라디오 구역.
- **의자**: 등받이 9도 뒤로 기울임. 양옆 뚫린 알루미늄 틀, 분리 쿠션, 접이식 메모판, 등 뒤 직책 패치(텍스처에 직접 그림) 12종.
- **분위기**: 밝은 타공 벽, 늑골, 짙은 녹색 광택 바닥과 철문, 노출 배관과 케이블 트레이, 갈색 가죽 의자. 일반적인 군함 양식만 참고하고 특정 게임의 배치나 문장은 쓰지 않는다.

### 6-104. 지형 장식 모델

- **파일**: `Models/Deco/ACO_Deco.fbx`, `ACO_Deco_Colliders.txt`, `Textures/Deco/ACO_Deco_*.png`. `Tools/aco_deco_models.py`가 만든다. 메시 이름은 `프리팹__재질`이다.
- **프리팹**: `ACO_DecoBuilder`가 재질마다 자식 렌더러 하나로 만든다. 경로가 같아 GUID가 유지되므로 씬 참조는 그대로다. 예전 메시와 `ACO_Deco_*.mat`는 쓰지 않지만 `Generated/Deco`에 남아 있다.
- **도시 격자**: 가로 86.6m, 세로 100m다. 육각 칸 간격(692.8, 346.4, 600m)의 정수 배라서 칸끼리 도로가 그대로 이어진다. 그래서 `AddCity`는 칸을 0도와 180도로만 돌린다. 60도로 돌리면 도로가 어긋난다.
- **비탈 큰길**: 칸 메시에 들어 있고 생성기 처리는 없다. 지형 치마 면(15도) + 0.45m에 놓여 낮은 이웃 쪽에서만 보이고, 같은 높이나 높은 이웃 쪽에서는 지형에 덮인다. ±X 변은 가로 큰길, 빗변은 세로 큰길만 쓴다. 수평 224m까지 내려간 뒤 끊긴다. 주변이 많이 낮으면 비탈 중간에서 끝난다.
- **파묻힘**: 높은 이웃의 비탈이 낮은 칸 가장자리를 덮는다. 단차 20m에 약 75m, 40m에 약 150m다. 칸 모델은 이웃 높이를 모르므로 그대로 둔다.
- **충돌**: 건물마다 상자 콜라이더이고 레이어는 Default다. 주택은 한 줄을 상자 하나로 막는다. 도시 칸당 약 90개다. 활주로 윗면은 전처럼 0.5m다. 바닥, 도로, 밭, 나무, 통신탑 지선에는 충돌이 없다.
- **항구 크레인**: 문형 다리 4개, 꼭대기 틀, 트러스 붐, 강봉, 트롤리와 스프레더로 된 부두 크레인이다. 콜라이더는 부재마다 따로 있고, 비스듬한 부재는 `strut` 줄로 콜라이더 목록에 들어간다. 문형 틀 안은 앞뒤 폭 23m, 옆 폭 27m, 높이 30m로 비어 있어 기체가 지나갈 수 있다. 호이스트 줄에는 충돌이 없다.
- **창문 불빛**: `glass`, `office`, `plaster`, `brick` 재질에 이미션 마스크가 있다(창의 16~18%). 타일 하나에 창이 수백 개라 불 켜진 창이 되풀이되어 보이지 않는다. `ACO_Weather.WindowGlow`(비 1, 폭풍 1.3)가 머티리얼 에셋의 `_EmissionColor`를 바꾼다. Bake가 `WindowMaterials`를 연결한다. 낮밤 순환은 없다. 에디터 플레이 중 바뀐 값은 머티리얼 에셋에 남고, 다음 플레이의 첫 날씨에서 다시 정해진다.
- **비용**: 도시 칸 3,300~6,400삼각형, 재질 9~10개, 랜드마크 350~1,900삼각형이다. 납작한 바닥 재질은 그림자를 드리우지 않는다.

### 6-103. SCM 파일런 모델

- **용도**: `WeaponVis/SCM/Weapons/pyronout.L·R` 아래의 임시 모델(`F135/AALD_TER` 6.5배)을 대체한다.
- **파일**: `Models/SCM/ACO_SCM.fbx`(908삼각형)와 `ACO_SCM_Albedo.png`(4096 × 2048). `Tools/aco_scm_missile.py`가 만든다.
- **좌표**: 파일런 로컬이다. +Y 기수, +Z 위라서 `pyronout` 아래에 Transform 0으로 넣으면 제자리다. 좌우 같은 메시를 쓴다.
- **모양**: 길이 4.1m, 가장 굵은 곳 폭 0.53m, 높이 0.4m. 꼬리로 갈수록 폭 0.22m까지 가늘어지고 윗면은 평평하다. 모서리는 세 번 꺾은 베벨이고, 몸통과 흡입구, 배기구는 부드러운 음영이다. 시커 창, 꼬리날개 6장, 배 쪽 흡입구, 꼬리 배기구가 있다.
- **걸이**: 고리 두 개로, 구멍은 좌우로 뚫려 있다. 파일런 갈고리 위치(y 0.781, 0.292)에 맞춰 갈고리 끝이 구멍에 들어간다.
- **날개**: 활공폭탄처럼 길고 좁은 날개다. 등 위 좌우 축(x ±0.16, y -0.02)에 하나씩 달려 같은 높이로 나란히 뒤로 접힌다. 왼쪽은 오른쪽의 거울상이고, 길이 2.05m, 뿌리 폭 0.3m다.
- **UV**: 몸통을 아래, 위, 좌, 우 네 띠로 따로 펴고, 접힌 날개 두 장도 따로 편다. 1m = 910픽셀로 같다. 띠 경계는 모서리 베벨 가운데에 있다.
- **편 날개**: `build(deployed=True)`는 날개를 55도 펴 뒤로 35도 젖힌 자세를 만든다. 날개 끝 폭은 3.7m다.
- **비행 모델**: `Models/SCM/ACO_SCM_Flight.fbx`는 날개를 편 탄체 모델이다. 처음부터 펴진 자세이고 접히는 움직임은 없다. Setup Check `scm-model-1`이 `ACO_Missile_SCM`의 `Model/Mesh` 아래에 넣고 예전 모델을 끈다. 재질 `Generated/ACO_SCM.mat`을 파일런 모델과 같이 쓴다. 배기구는 트레일 자리(z -1.9)에 맞췄다.
- **표기**: 텍스처에 직접 그린다. 이음매와 리벳, 노란 탄두 띠, 양옆의 이름·실탄·인양·연료 표기, 배의 흡입구 위험·진행 방향, 날개의 NO STEP이다. 문구는 모두 가공이고 좌우 모두 바로 읽힌다.

### 6-99. 랜스 포대 모델 교체

- **파일**: `Models/Lance/ACO_Lance.fbx`(메시 14개, 9,096삼각형)와 `Textures/Railgun/ACO_Lance_*.png`. `Tools/aco_lance_model.py`가 둘 다 만든다. 기초(Pad)만 코드 메시로 남는다.
- **적용**: Setup Check `프리팹 수치`의 `railgun-model-1`. `ACO_LanceModel.cs`의 `InstallLanceModel`이 프리팹을 제자리에서 고친다. `NPC_Railgun`은 씬이 안쪽 오브젝트까지 참조해서, 다시 만들면 오브젝트 ID가 바뀌고 씬 참조가 끊긴다. 프리팹 생성도 같은 Apply 함수를 쓴다.
- **코어**: 눕힌 원통 원자로. 노심(`CoreSphere`, 주황 발광)과 구조물(`Visual_Frame`)만 나뉘어 있다. 코어 히트박스는 44 × 44 × 88m다.
- **장갑**: 좌우 회전축을 (∓60.3, 28.5, -68)로 옮겼다. 열려도 받침 아래로 내려가지 않고, 닫히면 가운데 틈이 1m다. 뒤 장갑은 고정이라 `ArmorPanels`에서 뺐다.
- **부위 위치**: 냉각기는 (±135, 8, ±135), 앞쪽 소형 레일건은 경사로를 피해 (±58, 8, 176)이다.
- **소형 레일건**: 받침(`Model`), 방위 포크(`TurretYaw/Yoke`), 고각 포신(`TurretPitch/Barrel`)으로 나뉜다. 포신 원점이 고각 축이라 -5도에서 85도까지 받침과 포크에 닿지 않는다. 메시는 인스턴스 크기 3을 뺀 크기다.
- **표면 재질**: 강철·포신·장갑 재질은 Apply가 값을 다시 쓴다. 손으로 바꾼 값은 적용할 때 사라진다.
- **장식 충돌**: 기초의 방벽, 조명탑, 폭풍 방지벽(`Visual_BaseDetail`)에는 충돌이 없다.

### 6-100. 포대 모델 교체 (Flak, CIWS, VLS)

- **파일**: `Models/Lance/ACO_Turrets.fbx`. `Tools/aco_lance_model.py`가 함께 만든다. 메시 크기는 프리팹 로컬 크기다.
- **적용**: Setup Check `프리팹 수치`의 `turret-model-1`. 프리팹을 제자리에서 고친다. 이 포대를 쓰는 공중 요새도 함께 바뀐다. 팩 모델(`Turret_0*`) 자식은 프리팹에서 지운다. 팩 파일은 프로젝트에 남는다.
- **Flak**: 쌍열포. 받침(`Model`), 포탑(`YawModel`), 포신(`pitchModel`). 포구는 두 포신 가운데의 `Gun` 하나라 예광탄도 가운데서 나간다.
- **CIWS**: 회전 포신. 구조는 Flak과 같다. `TurretYaw`를 (0, 2, 0)으로, `TurretPitch`를 원점으로 옮겼고, 포구 `Gun`은 z 3.45다. 포신은 돌지 않는다.
- **고각**: 두 포 모두 -20도에서 85도까지 포탑과 받침에 닿지 않는다.
- **VLS**: 몸체(`Model`), 경고띠, 덮개 넷(`Hatch_0~3`). `ACO_MissileLauncher.Hatches`에 경첩을 넣으면 락온 경고나 발사 때 모두 열리고, 마지막 이벤트 4초 뒤 닫힌다. 이벤트가 모든 클라이언트로 가므로 따로 동기화하지 않는다.
- **표기**: 멀리서만 보이므로 문구와 기호는 넣지 않았다.

### 6-101. 공중 요새 포대 배치

- **실제 크기**: 코드의 `FortressScale`은 0.6이지만 프리팹은 손으로 고쳐져 있다. `Model` 크기는 1(길이 1,490m)이고, `Parts`·`Core`·`DeathEffects`가 1.667배로 맞춘다. 포대 실제 크기는 Flak·CIWS 5배, VLS 5.83배다. `FortressPoint`로 적은 자리는 어느 쪽에서도 같은 모델 위치를 가리킨다. 다만 프리팹을 다시 만들면 `Model`이 0.6으로 돌아가므로 다시 만들지 않는다.
- **구성**: Flak 4, CIWS 8, VLS 6으로 전체 18곳은 그대로다. Flak 4문을 빼고 배 쪽 VLS 2문과 후면 해치 CIWS 2문을 더했다. CIWS 배 쪽 2문은 가운데 줄에 있고, 나머지는 모두 좌우 대칭이다.
- **자리**: 표는 `ACO_TurretModels.cs`의 `FortressSites`이고, 프리팹 생성과 Setup Check `fortress-turrets-3`이 함께 쓴다. 선체를 광선으로 재고, 발자국의 표면에 평면을 맞춰 포대를 그 법선으로 기울인다. 가장 큰 기울기는 배 쪽 뒤 Flak의 약 19도다.
- **조준**: `ACO_AAGunSite.MountRelativeAim`을 켜면 방위와 고각을 받침 기준으로 잰다. 끄면 예전처럼 월드 수평 기준이라 기운 포대는 포탑이 받침에서 어긋난다. 요새 포대만 켠다. 고각은 받침 기준 -15도에서 85도다.
- **후면 해치**: `CIWS_Rear_0·1`은 격납고 지붕 뒤 끝에 놓는다. 해치 뒤쪽 공간의 위, 정면, 아래 표적이 모두 보이는 자리다.
- **받침 치마**: Flak, VLS, CIWS 받침 아래 치마가 곡면과의 틈을 메운다. 평평한 바닥에서는 묻혀 안 보인다.
- **크기**: `Parts`를 1로 맞춰 포대를 3배로 줄여도 놓을 수 있는 면적은 윗면 Flak 18%→19%, 배 쪽 Flak 0.6%→0.7%로 거의 그대로였다. 그래서 5배를 유지한다.
- **코어**: 랜스 포대 원자로를 세운 모양이다. 격납고 바닥에서 천장까지 37m에 맞춘 노심(`CoreSphere`, 지름 24m, 높이 30m, 주황 발광)과 구조물(`Visual_Frame`)로 나뉜다. 메시는 `ACO_Turrets.fbx`의 `Fort_Core`, `Fort_CoreFrame`이다. 적용은 Setup Check `fortress-core-1`이다.
- **코어 히트박스**: 루트의 `CoreHitbox`다(28 × 30 × 28m). 보이는 코어의 실제 위치에서 다시 잰다. 예전에는 요새 크기를 바꿀 때 따라가지 못해 약 100m 앞에 18m 크기로 있었다.
- **증원 발진 지점**: `Launch_L·R`은 측면 문 출구다. `FortressRootPoint`로 `Model`의 실제 크기를 따른다. 예전에는 60% 축척 자리에 있었다.
- **고친 오류**: 프리팹에서 `Flak_Belly_0`은 뒤집히지 않은 채 15m 떠 있었고, 나머지 배 쪽 Flak은 9.4m 묻혀 있었다.
