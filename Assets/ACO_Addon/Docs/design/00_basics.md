# 기초 — 구현 현황·핵심 결정·SAV 분석·파일 구조·셋업·다음 단계

> 설계 문서의 한 분야. 목차·작성 규칙은 `Docs/DESIGN.md`. 절 번호는 예전 DESIGN.md 그대로(코드 주석의 "DESIGN.md 6-xx"가 가리키는 번호).

## 0. 구현 현황

> 2026-11-19 기준. 자세한 내용은 각 절(6-NNN) — 표는 큰 줄기만 적는다.

| 기능 | 상태 | 파일 |
|---|---|---|
| NPC 전투기 비행 모델 (뱅크-투-턴, 월드축 분리 제어, 지형 회피) | ✅ | `ACO_NPCFlightController` |
| NPC 전투기 AI (순찰/추격/이탈, 요격 조준, 편대 간격) | ✅ | `ACO_NPCFighterAI` |
| 기동 동기화 (오차 기반 데드 레커닝) | ✅ | `ACO_NPCFighterNet` |
| SAM 포대 (탐지, 포탑, 레이더 콘/LOS) | ✅ | `ACO_SAMSite` |
| 미사일 런처 (락온·피락온 경고·탄약/재장전, SAV 미사일 호환) | ✅ | `ACO_MissileLauncher` |
| 자체 미사일 (직접 추적, 대응책, 접근 경고, 피격자 판정) | ✅ | `ACO_Missile` |
| NPC 기총 (시각 전용 파티클 + 타겟 오너의 조준 체류 판정) | ✅ | `ACO_NPCGun` |
| 격추/부활 (체력은 SaccTarget, 명시적 `Respawn()`), 격추는 오너 기준 | ✅ | AI/SAM |
| 프리팹 생성, 주변 NPC 목록 빌드 시 베이크 | ✅ | `ACO_PrefabBuilder`, `ACO_SceneBaker` (6-94) |
| CombatDirector — 공격 정원·대기 선회·교대·자기 방어 | ✅ | `ACO_CombatDirector`, AI |
| CombatDirector — LOD, 오너십 분산 | ⏳ | — (8절) |
| 미션 생성기 (풀 숨김 → 미션 시작 일괄 배치, 증원, 전투기 임무 엄호/CAP, NPC 작전 지역 25km) | ✅ | `ACO_MissionGenerator` |
| 고가치 표적 (공장/벙커, 방공 호위 링) | ✅ | `ACO_StaticTarget` |
| 미션 진행 — 페이즈 모듈(6-32: 타격 · SEAD · 제공권 · 스코어 어택 · 시한 표적 · 호위 · 콘보이 · 요격 · 상륙전 · 함대전 · 정밀 타격 · 터널 · 보스), 웨이브, 이벤트 | ✅ | `ACO_MissionManager`, `ACO_Phase*` |
| 호위 대상(아군 수송기/콘보이/LCAC — 서버 시간 항로, 적 우선 공격, 이동 방식 공중/해상/지상/공기부양, 행렬) | ✅ | `ACO_EscortUnit` (해상은 상륙전 LCAC · 함대전) |
| 전차 · 헬리콥터 · 곡사포(지상 항로 순찰/매복, 아군 지상 표적 포격) | ✅ | `ACO_MobileUnit` + `ACO_AAGunSite` / `ACO_ArtilleryGun` |
| 공격기 (지상·해상 호위 전용 적기) | ✅ | `NPC_Attacker` (6-43) |
| 육각 기둥 지형(씨앗 생성, 미션마다 새 지형, 경사 치마로 차량 통행) + 지형 장식(도시 · 마을 · 터널 · 항구 등) | ✅ | `ACO_HexTerrain` + `ACO_HexColumn`, `ACO_TerrainFeatures` |
| 게임 진행 — 참가 명단 · READY · 일제 출격 · 과반 자동 시작 | ✅ | `ACO_GameFlow` (6-15, 6-89) |
| 전투기 숙련도 (신병/베테랑/에이스, 편대 단위) | ✅ | 생성기 + AI |
| 팀 공통 점수(페이즈 배율), 제한 시간 실패, 결과 랭크(팀 격추 수 — 6-297) | ✅ | `ACO_MissionManager` |
| 점수 HUD(머리 고정), 표적 마커(사각형/육각형, TGT, 시선 정보) | ✅ | `ACO_ScoreHUD`, `ACO_TargetMarkers` |
| 작전판(격추 기록 + 디브리핑, 로컬) | ✅ | `ACO_MissionLog` |
| 무전 채터 — 자막 + 음성(ElevenLabs로 만든 클립, 한 · 영 · 일 자막) | ✅ | `ACO_Chatter` (6-19, 6-175) |
| 대공포 (포탑 리드 조준, 급기동 회피 가능, NPC 기총 판정 재사용) | ✅ | `ACO_AAGunSite` |
| 지상 유닛 원거리 잠재우기 (조종 중인 플레이어 기체 기준) | ✅ | `ACO_SAMSite`, `ACO_AAGunSite` |
| 부위 파괴형 함선 — 전함/구축함/항모/기함, 순항, 격침 연출 | ✅ | `ACO_Warship` + SAM/대공포 부위 |
| CIWS 미사일 요격 (구역 방어, 시간 방식, LASM 면역) | ✅ | `ACO_AAGunSite`, `ACO_Missile` (6-40, 6-186) |
| 보스 — 공중 요새 · 육상전함 · 잠수함 · 에이스(형제 · 레이저 · SHADE · LUNA · MR. S) | ✅ | `03_bosses.md` |
| 재화 · 강화 · 특수무장 · 상점 · 지원 호출(벙커버스터 · 급유 드론 · 가디언 드론 · 아군 편대) | ✅ | `ACO_PlayerProgress`, `ACO_VehicleUpgrades`, `ACO_Shop`, `ACO_SupportManager` (6-14, 6-22, 6-142, 6-143, 6-273) |
| CIC 도구 — 작전 테이블 웨이포인트 · 지역 편대 · 정찰 UAV · 고글 · 순항미사일 | ✅ | `ACO_Cic*` (6-275 ~ 6-293) |
| 비행 HUD(SAV HUD 대체) · HUD 설정 · 레이더 · RWR · 작전 테이블 | ✅ | `ACO_FlightHud`, `ACO_HudSettings`, `ACO_HudRadar`, `ACO_RWR`, `ACO_TacticalMap` (6-51, 6-17, 6-30, 6-20, 6-16) |
| 날씨 · 구름 · 비 · 낙뢰 · 물 연출 · BGM | ✅ | `ACO_Weather`, `ACO_CloudField`, `ACO_Rain`, `ACO_Lightning`, `ACO_WaterFx`, `ACO_Bgm` |
| 번역(한 · 영 · 일, VRChat 언어 따름) | ✅ | `ACO_Localization`, `Localization/ACO_Strings.tsv` (6-166) |
| 관전 · 데칼 · 라운델 · TAC 네임 · 튜토리얼 · 크레딧 / 업데이트 노트 | ✅ | `ACO_SpectatorCam`, `ACO_DecalStudio`, `ACO_TacName`, `ACO_TutorialPopup`, `ACO_CreditBoard` |
| 항모 함내 격실 · 캐터펄트 갑판 요원 | ✅ | `ACO_InteriorBuilder`, `ACO_CatapultCrew` (`07_interior.md`) |
| 이전 버전 인스턴스 알림 | ✅ | `ACO_VersionGuard` (6-296) |

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

> 2026-11-19에 폴더를 다시 훑어 고쳤다. 스크립트마다 자세한 동작은 각 절(6-NNN)과 스크립트 머리 주석에 있다.

```
Assets/ACO_Addon/
  Docs/DESIGN.md                   설계 목차 · 작성 규칙 (분야별 절은 Docs/design/*.md)
  Docs/TEST_CHECKLIST.md           아직 테스트하지 않은 항목(7-NNN)
  Scripts/
    Core/                          진행 · 생성 · 공통
      ACO_CombatDirector           전장 지휘: 타겟당 공격 정원, 대기 순서, 교대. 동기화·Update 없음
      ACO_MissionGenerator         미션 생성: 풀 유닛 무작위 배치(지상/바다/공역 밖), 페이즈 목표 배치·태그, 함대, 결과 배열만 동기화
      ACO_MissionManager           미션 진행: 페이즈 모듈 고르기, 증원 웨이브, 팀 점수·랭크, 이벤트 방송
      ACO_GameFlow                 게임 진행: 참가 명단 · READY · 일제 출격 · 과반 자동 시작(6-15)
      ACO_PhaseModule              페이즈 모듈 기반(추상): 고르기·시작·판정·종료·표시·대사 — 관리자 자식에 두면 Bake가 목록에 넣는다(6-32~6-38)
      ACO_Phase*                   페이즈 모듈: Destroy(타격·SEAD·제공권) · Quota · TimeSensitive · Escort · Convoy · Intercept · Amphibious ·
                                   Fleet · Precision · Tunnel · Boss · Fortress · LandShip · Submarine · Railgun · Borderless · Twins · Shade · Luna · MrS
      ACO_HexTerrain               육각 기둥 지형: 씨앗 노이즈 높이 → 기둥 배치(런타임 Instantiate 1회 후 재사용, 프레임 분할)
      ACO_TerrainFeatures          지형 장식 배치(도시 블록 · 마을 · 터널 · 항구 · 등대 · 폐허 등)
      ACO_PlayerProgress           재화 · 보상 · 영구 저장(PlayerData, 고정 키)
      ACO_VehicleUpgrades          기체 강화 적용
      ACO_SupportManager           지원 호출(벙커버스터 · 급유 드론 · 가디언 드론 · 아군 편대) · CIC 무전
      ACO_Localization             번역 장치(문장 표 · VRChat 언어)
      ACO_VersionGuard             이전 버전 인스턴스 알림(6-296)
      ACO_LockGate                 락온 제한(적 공통 부품 — 켜져 있으면 플레이어 무장이 못 잠근다)
      ACO_SeatRecenter             VR 좌석 눈높이 재조정(점프 한 번)
      ACO_UrlStore                 플레이어별 URL 영구 저장소(Player Object 16칸, 6-153)
    AirUnits/
      ACO_NPCFighterAI             전투기 두뇌: 순찰/추격/이탈/대기 상태머신, 요격 조준, 편대 간격, 격추/부활, 숙련도
      ACO_NPCFighterNet            전투기 동기화: 오차 기반 전송, 원격 예측/보정, 소유권. Update 없음(AI가 호출)
      ACO_NPCFlightController      비행 모델: 뱅크-투-턴, 월드축 분리 제어, 지형 회피. Update 없음(AI가 호출)
      ACO_AceBase · ACO_Twins · ACO_Borderless · ACO_ShadeAce · ACO_LunaAce · ACO_MrS   에이스 보스 행동(03_bosses)
      ACO_CrossManeuver            교차 기동 · 편대 집결(에이스 공통 부품)
      ACO_AirWreck                 에이스 추락 연출(6-145)
      ACO_Wingman                  아군 편대(지원 호출 WINGMEN, 6-273)
      ACO_AirCollisionIgnore       공중 유닛과 내 조종 기체 충돌 무시
    GroundUnits/
      ACO_SAMSite                  SAM 포대(함선 VLS 겸용): 탐지, 포탑 조준, 원거리 잠재우기, 격추/부활
      ACO_AAGunSite                대공포(함포/CIWS 겸용): 리드 + 조준 흔들림, ACO_NPCGun 사격, CIWS 미사일 요격(6-40)
      ACO_StaticTarget             고가치 표적(공장/벙커): 무장 없음, 체력은 SaccTarget, 잔해 전환
      ACO_MobileUnit               이동 유닛(전차·헬리콥터·곡사포, Kind): 서버 시간 지상 항로, 지면 추종, 거리 LOD
      ACO_ArtilleryGun             곡사포 포탑(호위 대상 포격)
      ACO_EscortUnit               호위 대상(아군): 서버 시간 항로, 이동 방식 공중/해상/지상/공기부양, 적 우선순위, 격추 연출
      ACO_Warship                  부위 파괴형 함선: 본체 체력, 부위 → 본체 피해, 서버 시간 순항, 격침 연출
      ACO_CityVehicle              도시 도로 차량(민간 · 지휘 차량, 6-270)
      ACO_TelUnit                  TEL: 발사 준비(지상 표적) → 탄도탄 상승(공중 표적), 서버 시간 궤적
      ACO_FlyingFortress · ACO_FortressPart · ACO_FortressBlast   공중 요새 보스
      ACO_LandShip                 육상전함 보스
      ACO_Submarine · ACO_Slcm     잠수함 보스 · SLCM(6-202)
      ACO_Railgun · ACO_MiniRailgun   레일건 보스 · 소형 레일건 포대
      ACO_IcbmSite · ACO_OrbitalStrike   레이저 에이스 사일로 · LUNA 궤도 폭격 연출(피해 없음)
      ACO_FlareDispenser           NPC · 호위 대상 플레어/연막
    Weapons/
      ACO_MissileLauncher          공용 런처: 락온 누적·피락온 경고·탄약/재장전, SAV 미사일 덕 타이핑 계약. Update 없음
      ACO_Missile                  자체 미사일: 유도 3종, 콜드런치, 대응책/접근 경고(SAV 호환), 피격자/사수 판정, 특수 동작, CIWS 요격 판정
      ACO_Dispenser                산탄 탄두(SCM)
      ACO_Ordnance                 폭탄/로켓 탄체: 중력/추진, 범위 피해(아군 제외), 근접 신관(FLAK), 유도(LGB·LGR)
      ACO_DFUNC_Missile            플레이어 미사일 다이얼(DFUNC_AAM 대체): 레이어 표적, 슬롯 장전, 강화 성능, 특수 동작
      ACO_DFUNC_Special · ACO_SpecialWeaponDef   특수무장 슬롯 · 무장 정의(6-22)
      ACO_DFUNC_Support            SUPPORT 다이얼(지원 호출, 6-142)
      ACO_NPCGun · ACO_NPCLaser    NPC 기총(시각 파티클 + 조준 체류 판정) · 레이저 모드
      ACO_TankerDrone              급유 드론 연출
      ACO_GunDamageBonus           기총 피해 강화
      ACO_BulletFlyby              나를 노린 총탄 스치는 소리
    Vehicle/                       플레이어 기체 · 항모 갑판
      ACO_VehicleAutomation        기체 자동화(자동 캐노피 등, SaccEntity 확장)
      ACO_VehicleMarks · ACO_VehicleDecal   헐넘버 · 콜사인 · 킬마크 · 데칼
      ACO_DFUNC_Catapult · ACO_CatapultCrew · ACO_LaunchOperator   캐터펄트 · 갑판 요원 · 발함 지휘소 요원
      ACO_PilotOnlyColliders       탑승자 전용 충돌(6-259)
    Environment/
      ACO_Weather · ACO_CloudField · ACO_Rain · ACO_Lightning   날씨 · 구름 · 비 · 낙뢰
      ACO_WaterFx · ACO_Underwater 물보라 · 항적 · 물속 연출
      ACO_InteriorZone             함내 구역(비 · 물속 · 마커 끔, 안전지대)
      ACO_WallClock                벽시계(로컬 시각)
    UI/
      ACO_ScoreHUD · ACO_TargetMarkers · ACO_FlightHud · ACO_HudSettings(+Button) · ACO_Units   HUD · 표적 마커 · 비행 HUD · HUD 설정 · 거리 단위
      ACO_HudRadar · ACO_RadarContacts · ACO_RWR · ACO_VoiceWarning   레이더 · 접촉 목록 · RWR · 조종석 음성 경고
      ACO_TacticalMap · ACO_CockpitMap   CIC 작전 테이블 · 브리핑실 벽 지도/조종석 지도
      ACO_MissionLog · ACO_LogScrollButton · ACO_MissionStartButton · ACO_FlowButton   작전판 · 작전 콘솔 · 게임 진행 버튼
      ACO_Chatter                  무전 채터(자막 + 음성, 6-19)
      ACO_Cic*                     CIC 도구(Console · Strike · CruiseMissile · DroneSync · Goggles · GogglesPanel, 6-275)
      ACO_Loadout · ACO_HangarButton · ACO_Shop   격납고 특수무장 · 상점
      ACO_SpectatorCam · ACO_SpectatorButton   관전 패널
      ACO_DecalStudio · ACO_DecalGun · ACO_DecalButton · ACO_TacName   데칼 · 라운델 · 콜사인
      ACO_TutorialPopup · ACO_CreditBoard · ACO_CreditScrollButton   튜토리얼 · 크레딧/업데이트 노트 보드
      ACO_Bgm · ACO_BgmPanel · ACO_VideoAudioToggle · ACO_UiSound   BGM · 영상 소리 · UI 소리
      ACO_ScreenFader · ACO_Teleporter · ACO_DebugPanel · ACO_DebugButton   암전 · 이동 · 디버그(AllowedPlayers)
  Shaders/                         ACO/* 셰이더 — 마커(MarkerOverlay) · 지형/나무 · 바다 · 하늘 안개 · 구름(Puffs · Layer · Veil · Volume) · 비 ·
                                   캐노피 빗방울 · 낙뢰 · 데칼 · 표시 글자/이미지(MarkText · MarkImage) · 파티클(FxParticle) · 레이저 · 경계 벽 ·
                                   지도 정점색 · 고글 화면 · 함내(Interior) 등
  Generated/                       빌더가 만든 메시·머티리얼·텍스처(HUD 선 · 마커 · 보드 · 구름 · 터널 · 장식 등) — 손으로 고치지 않는다
  Localization/ACO_Strings.tsv     번역 문장 표(키 · 한 · 영 · 일) — 고친 뒤 ACO_LocalizationSetup.ApplyTable → Bake
  Credits/                         크레딧 · 업데이트 노트(ko · en · ja)
  Audio/ · Models/ · Textures/ · Fonts/ · animation/
  Udon/                            U# Program Asset (스크립트당 1개 — 셋업 검사가 빠진 것을 만든다)
  Prefabs/                         빌더가 생성: NPC_* (적 · 아군 유닛 · 보스 · 포탑), ACO_Missile_* , ACO_* (HUD · 패널 · 보드 · 지형 장식 · 연출)
  SpecialWeapons/                  특수무장 정의 프리팹(ACO_Special_*). Bake가 격납고·기체에 맞춘다
  Editor/
    ACO_ToolsWindow                ACO → Tools...: 셋업 도구 모음
    ACO_SetupValidator(.Tuning)    셋업 검사 · [고치기] · 프리팹 수치 맞춤 표시
    ACO_SceneBaker(.*)             Bake: 씬 참조 연결(빌드 시 자동) — Aircraft · Cic · Create · Environment · Game · Localization · Support · Ui · Units
    ACO_PrefabBuilder(.*) · ACO_PrefabBuilderWindow   NPC · 미사일 · 표적 프리팹 생성
    ACO_PrefabVersions             프리팹 판 번호(판이 오르면 셋업 검사가 다시 만들기를 권한다)
    ACO_PrefabTuning               프리팹 · 씬 수치 맞춤 항목(예전 값과 같을 때만 바꾼다, ACO_AppliedTuning.txt에 기록)
    ACO_HUDBuilder(.Cic · .Tutorial · .Version)   HUD · 비행 HUD · HUD 설정 · 작전판 · 콘솔 · 튜토리얼 · 버전 알림
    ACO_RadarBuilder               레이더 · 브리핑 지도(PlanMap) · 작전 테이블 격자
    *Builder · *Model              유닛 · 보스 · 장식 · 함내 · 터널 · 하늘 · 물 연출 빌더(Fleet · Fortress · LandShip · Submarine · Railgun · Luna · MrS ·
                                   Borderless · Tel · CityVehicle · Wingman · Support · Crew · Interior · Tunnel · Deco · Sky · WaterFx · Fx · Boundary 등)
    ACO_LocalizationSetup · ACO_VoiceLines   문장 표 적용 · 무전 대사 목록
    ACO_UdonPrograms · ACO_NetworkIDFixer · ACO_SyncModeFixer   프로그램 에셋 · 네트워크 ID · 동기화 모드 정리
    ACO_CombatDirectorGizmo · ACO_MissionGeneratorGizmo   씬 뷰 시각화
Tools/ (프로젝트 루트)
  aco_compile_check.py             Unity 컴파일러로 ACO 스크립트만 컴파일해 C# 오류 확인
  aco_elevenlabs.py · aco_voice_lines.json · aco_make_voice.py · aco_radio_filter.py   무전 음성 생성(ElevenLabs) · 대사 목록 · 무전 필터
  aco_*_model.py                   Blender 모델 스크립트(터널 · 잠수함 · 육상전함 · 에이스 기체 · TEL · 도시 차량 · 함내 등)
  aco_make_*.py · aco_sfx_*.py     효과음 합성(스침 · 레이저 · 천둥 등)
  aco_*_tex.py · aco_icons.py      텍스처 · 아이콘 생성
  aco_sync_sim.py · aco_ciws_sim.py   페이즈 동기화(6-34) · CIWS 요격(6-40) 시뮬레이션
  aco_interior_check.py · aco_interior_bake.py   함내 검사 · 굽기
```

- 레이어 상수는 별도 파일 없이 `ACO_PrefabBuilder`에 있다(25 아군 항공기 / 29 적 항공기 / 30 적 지상 / 17 히트박스). 런타임은 각 유닛의 `TargetLayers` 필드를 쓴다.
- 업데이트 비용을 줄이려고 Update를 가진 스크립트를 최소화했다: 전투기는 AI 1개만, 비행 컨트롤러와 런처는 Update가 없다. 지상 유닛은 원거리 잠재우기로 플레이어 근처 유닛만 계산한다(2.9).
- 새 U# 스크립트는 코드만 만들면 프로그램 에셋이 없다 — 셋업 검사 [고치기]가 만든다. static 필드는 쓰지 않는다. 플레이 모드 중이거나 자동 새로고침이 꺼져 있으면 Assets → Refresh(Ctrl+R)가 돌아야 인식된다(안 그러면 CS0246).
- `SaccFlightAndVehicles.Runtime.asmdef`가 `autoReferenced: true`라 ACO 스크립트는 별도 asmdef 없이 SAV를 참조한다. SAV 폴더는 고치지 않는다(허용된 SaccTarget 패치만 — 6-274).

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
15. 재화/강화: `ACO → Create Player Progress`(씬에 하나), 플레이어 기체 프리팹(씬 인스턴스가 아니라)을 골라 `ACO → Player Vehicle → Set Up Selected Vehicles`(미사일 다이얼 교체 · 강화 확장 · 좌석 재조정 · 특수무장 슬롯 · 카탈로그 · 키보드 단축키 — 여러 번 해도 된다), 격납고 프리팹을 다시 만든 뒤 Bake.
16. 게임 진행 · 무전 · HUD 설정 · 작전 테이블 · 구름과 비 등 나머지 씬 오브젝트는 `ACO → Tools...`의 "씬 오브젝트 만들기"에서 만들고, 셋업 검사가 빠진 것을 알려 준다.

---

## 8. 다음 단계와 미해결 항목

> 2026-11-19 정리. 끝난 항목(무전 음성 6-19 · 공격기 6-43 · 전차 · CIWS 요격 6-40 · DFUNC_AAM 대체 `ACO_DFUNC_Missile`)은 지웠다. 테스트 못 한 항목은 `Docs/TEST_CHECKLIST.md`.

- **CombatDirector 확장** ⏳ (공격 정원은 ✅ 5.2-1):
  - 교전 LOD — 대기/원거리 NPC의 AI 틱·동기화 빈도 낮추기(네트워크 예산의 안전장치)
  - 오너십 분산 — 1인 최악 부하 완화. 디렉터는 이미 다중 오너를 견디도록 만들었다
- **미션 진행 확장** ⏳ — 추가 실패 조건(기지 방어 페이즈), 클리어 후 다음 미션 자동 시작, 전투기 편대 유지 비행(편대장 추종).
- **전차 확장** ⏳ — 지금은 정해진 항로만 따라간다(결정적, 동기화 0). 표적을 쫓아 방향을 트는 기동은 위치 동기화가 필요하다.
- **튜닝 값** — 동기화 빈도, 편대 간격, 지형 회피 거리 등은 실제 맵·유닛 수로 프로파일링 후 확정.
- **플레이어 락온 스윕** — 락온은 `ACO_DFUNC_Missile`로 바뀌었다. 표적 수가 늘면 스윕 비용(2.7)과 지상 유닛 수 제약을 다시 본다.
- **언어 전환** — 패널 · 자막은 번역 표(6-166)로 바뀐다. 새 글자는 템플릿 · 키로 넣고 글상자를 넉넉히 둔다.
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

### 6-274. SAV 원본 패치 기록 — SaccTarget 늦은 접속 멈춤
SAV 폴더(`Assets/SaccFlightAndVehicles/`)는 손대지 않는 게 원칙이고, `Other/SaccTarget.cs`만 사용자 승인으로 최소한 고친다. SAV 폴더는 git에 올라가지 않으므로 **SAV를 새로 받거나 덮어쓰면 아래 패치를 다시 넣어야 한다**. 모든 패치 줄 끝에는 `// ACO PATCH` 표시가 있다(`grep -n "ACO PATCH"`로 확인).

- **증상**: 진행 중인 방에 늦게 들어갈 때마다 콘솔에 `EXTERN to 'VRCSDKBaseVRCPlayerApi.__IsOwner__UnityEngineGameObject__SystemBoolean' … NullReferenceException`이 2~4번 나고, 그 NPC의 SaccTarget이 그 사람 화면에서 멈췄다(피격 · 파괴 표시가 안 바뀜). 2026-11-17 로그에서 접속할 때마다 반복됨.
- **원인**: 접속 직후 다른 사람이 쏜 피격 네트워크 이벤트(`SendDamageEvent`)가 그 오브젝트의 `Start`보다 먼저 도착한다. `localPlayer`는 `Start`에서 채우므로 아직 null이고, `localPlayer.IsOwner(gameObject)`에서 멈춘다. `KillEvent`도 같은 구조(`localPlayer.playerId`).
- **패치**: 두 네트워크 이벤트 맨 앞에 `if (localPlayer == null) { return; }// ACO PATCH (6-274): …`를 넣었다.
  - `localPlayer`를 그 자리에서 채우지 않고 건너뛰는 이유: `Start` 전에는 `FullHealth`도 0이라, 드물게 접속자가 이미 오너면 `Health = Min(Health - dmg, FullHealth)`가 0 이하가 되어 바로 격파된다. 접속 중 피격 한두 번을 건너뛰는 편이 안전하다(체력은 오너 동기화로 곧 맞는다).
  - 피해 계산 자체는 그대로다.

| 경우 | 결과 |
|---|---|
| 늦게 들어오는 중 피격 이벤트 도착(오너 아님) | 건너뜀 — 원래도 오너가 아니면 기록만 하고 끝났다. 멈추지 않음 |
| 늦게 들어오는 중 오너인 채로 피격(방장이 나가는 순간 등) | 그 한 번 피해를 건너뜀. 체력 0으로 즉사하지 않음 |
| 접속 중 KillEvent 도착 | 건너뜀 — 킬 알림은 쏜 사람에게만 의미가 있다 |
| Start 뒤 | 바뀐 것 없음 |

### 6-296. 이전 버전 인스턴스 알림 — 버전 확인 (`ACO_VersionGuard`, 프리팹 `ACO_VersionGuard`)
- 바뀜(6-325): 2026-11-19 씬에서 `ACO_VersionGuard` 오브젝트를 꺼 두었다(알림 안 뜸). 다시 켜면 그대로 동작한다.
- 바뀜(6-327): 번호를 기록하는 사람 = 들어온 순간 마스터(예전 "Start에서 오너이고 혼자" — 우르르 들어오면 아무도 기록 못 했다), 확인 기다림 15 → 60초.
- **계기(2026-11-19)**: 일본 사용자 신고 — 업데이트 전에 열린 인스턴스에 나중에 들어갔는데, 연 사람이 나간 뒤부터 페이즈가 시작되자마자 TGT가 "不明"에게 몇 초 만에 파괴돼 클리어됐다. 같은 인스턴스에 두 월드 버전이 섞여 미션 순번(페이즈 · 유닛 풀)을 서로 다르게 읽은 것으로 추정. VRChat은 동기화 구조가 같으면(호환) 옛 인스턴스 입장을 막지 않는다.
- **사용자 결정**: "이전 버전 인스턴스랑 안 섞이게 방지 기능" → 오너가 쏜 값이 없으면 이전 버전으로 본다 → 15초 동안 "확인 중" 알림 → 알림은 로스터를 고르는 작전판에 → 빌드 번호는 분까지 → **"막지는 말고 계속 위에 띄워두자"**(작전판 버튼 · CIC 도구는 막지 않고, 판을 덮는 창 대신 판 위 알림 띠를 계속).
- **빌드 번호**(`BuildStamp`): 빌드(업로드 · Build & Test)할 때 `ACO_SceneBaker.OnBuildRequested`가 Bake 전에 `ACO_HUDBuilder.StampVersionGuards`로 넣는다 — 2020-01-01 UTC부터 지난 분(정수, int 한도까지 수천 년). 문자열 · 날짜 형식을 거치지 않고 정수 같음만 비교한다. 0이면 검사를 끈다(번호가 안 찍힌 빌드에서 모두에게 경고가 뜨지 않게). 같은 분 안에 두 번 올리면 같은 번호(무시).
- **인스턴스 번호**(`_instanceStamp`, Manual 동기화): 시작할 때 오너이고 혼자인 사람(인스턴스를 연 사람)만 자기 번호를 기록한다. 오너가 바뀌어도 새 오너는 0을 채우지 않는다(섞인 인스턴스가 정상처럼 보이지 않게).
- **판정(로컬)**: 번호가 같으면 정상, 다르면 다른 버전. 번호가 없으면 입장 뒤 `CheckTimeout`(15초)까지 확인 중, 그 뒤 다른 버전(이 기능이 없는 옛 버전 인스턴스). 다른 버전 뒤에 늦게 받은 번호가 같으면 정상으로.
- **표시**: 작전판(브리핑 판) 위 가장자리 바로 위 알림 띠(판 너비 · 높이 170 판 단위, 판 앞 1cm, sortingOrder 3). 확인 중 = 노랑 "월드 버전 확인 중..."(정상이면 바로 사라짐), 다른 버전 = 빨강 "이 인스턴스는 다른(이전) 버전의 월드로 열렸습니다. 새 인스턴스를 만들어 주세요."(정상이 될 때까지 계속, 닫기 없음). 클릭을 받지 않아(VRCUiShape · 레이캐스터 없음) 판 버튼을 가리지 않는다. 문구 키 `ver.checking` · `ver.mismatch`(한 · 영 · 일), 언어를 바꾸면 바로 바뀐다.
- **막지 않는다**: 작전판 버튼 · CIC 도구 · 미션 진행은 그대로. 미션 진행 판정이 오너 이탈로 다른 버전 사람에게 넘어가는 것도 VRChat이 정해 막을 수 없다 — 알림으로 새 인스턴스를 유도한다.
- 연결: Setup Check가 없으면 [고치기]로 작전판 자리(튜토리얼과 같은 기준)에 놓는다. `Loc`은 번역 Bake가 넣는다.

| 경우 | 결과 |
|---|---|
| 새 버전으로 새로 연 인스턴스 | 연 사람이 기록, 모두 알림 없음 |
| 새 버전 인스턴스에 나중에 들어옴 | 몇 초 노랑 "확인 중" 뒤 사라짐 |
| 이 기능이 없는 옛 버전 인스턴스에 들어옴 | 15초 뒤 빨강 알림이 계속 |
| 이 기능이 있는 옛 버전 인스턴스에 들어옴 | 번호가 달라 바로 빨강 알림 |
| 옛 버전 오너가 나가 새 버전 사람이 오너가 됨 | 번호 그대로(채우지 않음) — 계속 빨강 |
| 느린 네트워크로 15초 넘어 받음 | 잠깐 빨강 뒤 같으면 사라짐 |
| 모두 나갔다가 다시 염 | 새로 연 사람이 기록 |
| 에디터(ClientSim) | 혼자라 바로 기록, 알림 없음 |
| 빌드 번호 0(빌드 훅이 안 돈 빌드) | 검사 끔 |
