# 플레이어 — 재화·강화·특수무장·기체 기능·외형

> 설계 문서의 한 분야. 목차·작성 규칙은 `Docs/DESIGN.md`. 절 번호는 예전 DESIGN.md 그대로(코드 주석의 "DESIGN.md 6-xx"가 가리키는 번호).

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
  | AIRFRAME | 체력 +10% / 기동 +5% / 추력 +4% / 연료 효율 +8% / 안정성 +6% | 2,000 / 2,000 / 2,000 / 1,500 / 1,500 | `SaccAirVehicle.FullHealth`(남은 체력도 같은 비율) / `Pitch·Yaw·RollStrength` / `ThrottleStrength`(+`ThrottleStrengthAB`) / `FuelConsumption` ÷ 배수(+`FuelConsumptionAB`, 5단계 ×1.4 → 약 29% 덜 씀) / 양력: `Lift` × 배수 + `ExtraVelLift`에 (배수 − 1)을 더함(플랩이 같은 값을 더하고 빼므로 곱하지 않고 더한 만큼만 되돌린다) — 저속에서 쳐들어도 버티고 기수가 덜 처진다 | **바뀜(6-141)**: 안정성은 기동성에 합치고, 그 자리에 대응탄 수(단계마다 +1발, 1,500).
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
  | EXTRA AMMO (`ammo`) | 특수무장 탄수 ×1.5 | 1,200 | UpgradeFamily가 SpecialAAM/AGM/Bomb인 무장의 탄수 배수에 곱함(일반 미사일 제외). 바뀜: OTHER 무장 중 MMP·RGP도 받는다(`TakesExtraAmmo` — 16 → 24발, 강화 트리는 여전히 없음). ECM은 1회 충전식이라 제외 |
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
  - 특전 무기: MMP · RGP 50,000(2026-11-12 — 일반 최상위 18,000과 떼어 놓은 장기 목표, Setup Check `premium-price-1`. 구매는 한 번뿐이라 이미 산 사람은 그대로).
  - 폭탄·로켓: UGB(CCIP, 범위 200m) 8,000 / LGB(유도 폭탄, 6-41) 11,000 / RKT 8,000 / LGR(유도 로켓, 6-41) 11,000.
  - 대지는 특수 대지 탭, 대공은 특수 대공 탭, 폭탄·로켓은 특수 폭탄 탭 강화를 받는다. 미사일 풀은 2발만 미리 만든다(모자라면 늘어난다).
- **설치**: ① 격납고(`ACO_Loadout`) 목록 — **Bake마다** 없는 ID는 목록 순서로 끝에 더하고, 이름·설명·가격·아이콘은 정의 값으로 맞춘다(바뀐 게 없으면 기록 안 함). ② 기체 모듈 — **기체 셋업**(고른 기체)과 셋업 검사 [고치기](씬 전체)가 슬롯에 같은 ID 모듈이 없으면 정의 모듈을 복제해 넣는다(있으면 건너뜀 — 기체별로 고친 값 보존. 정의를 고친 뒤 다시 반영하려면 그 기체 모듈을 지우고 기체 셋업. 같은 ID가 예전 SAV 폭탄/로켓 모듈이면 ACO 모듈로 바꾼다). 기체에 묶인 칸은 설치가 채운다: SAVControl·HUD·조준 기준·발사 키·장애물 레이어는 일반 미사일에서, 발사 지점·발사음은 유도탄이면 일반 미사일·폭탄/로켓이면 슬롯의 기존 폭탄/로켓 모듈에서, 락온음은 유도탄만, 잔탄 글자는 슬롯의 다른 모듈과 공유, HUD 슬롯 막대를 만든다. ③ Bake(표적·강화·RWR 등).
- **새 무장 만들기**: 예제 프리팹 복제(Ctrl+D) → 루트 Id(**한 번 정하면 바꾸지 않는다** — 해금 저장 키 `aco_unlock_<Id>`)·이름·설명·가격·아이콘 → 자식 Module 성능 → 셋업 검사 [고치기] 또는 기체 셋업. 아이콘은 `Textures/Icons/aco_icon_weapon_<id 소문자>.png`가 있으면 자동(정의 Icon이 우선).
- **격납고 페이지**: 왼쪽 특수무장 = 한 페이지 2줄 × 4칸 + `< PREV` / `PAGE n / m` / `NEXT >`(무장이 8개를 넘을 때만 보인다, 끝에서 넘기면 돈다). 칸의 이름·아이콘·자물쇠·색은 `ACO_Loadout`이 페이지에 맞게 바꾼다(`ButtonLabels/ButtonIcons/LockIcons`, 아이콘은 `SpecialIcons`). 칸 버튼의 Index = 페이지 안 칸 번호(`ACO_HangarButton` 4 = 페이지 넘김). 장착·불러오기 때 그 무장이 있는 페이지로 간다.

### 6-26. 캐터펄트 — 풀스로틀 유지 사출 ✅ (`ACO_DFUNC_Catapult`, SAV DFUNC_Catapult 대체, `ACO → Player Vehicle → Replace Catapult on Selected Vehicles`)

- SAV `DFUNC_Catapult`를 복사해 같은 방식(물리는 지점 = 이 오브젝트, BoxCollider 트리거, 캐터펄트 레이어 24, 정렬·묶기·네트워크 이벤트·애니메이터 `launch`/`oncatapult`·"speed=" 이름 규칙·사출 속도/각속도)으로 동작한다.
- **풀스로틀 사출**: 캐터펄트에 걸린 채 `ThrottleInput` ≥ `FullThrottleThreshold`(0.95)를 `FullThrottleHoldTime`(2초) 유지하면 사출(중간에 내리면 다시 센다). SAV의 AutoLaunch_FullThrottle은 정확히 1만 보고 트리거 사출을 껐다 — 여기서는 `AllowTriggerLaunch`(다이얼 선택 후 트리거·데스크톱 키)도 함께 된다.
- **표시(선택, 조종사 화면)**: `AttachedObjects`(걸려 있는 동안), `HoldObjects`(풀스로틀 유지 중), `HoldFill`(X 스케일 = 진행도, 피벗 왼쪽 끝).
- **교체 메뉴**: 선택한 기체마다 DFUNC_Catapult가 있는 오브젝트에 새 컴포넌트를 붙여 값(SAVControl·다이얼 표시·소리·각도·레이어·기어·애니 트리거·정렬)을 옮기고, SaccEntity의 Dial_Functions_L/R·ExtensionUdonBehaviours에서 바꾼 뒤 옛 컴포넌트를 지운다. SAV AutoLaunch_FullThrottle이었으면 그 지연을 유지 시간으로(최소 1초). SAV 폴더는 건드리지 않는다.

### 6-27. 기체 자동화(자동 캐노피 / 기어·후크 연동 / 리스폰 복구) ✅ (`ACO_VehicleAutomation` — `Scripts/Vehicle`, 셋업 메뉴 `Editor/ACO_VehicleAutomationSetup`)

- 한 번 지웠다가 다시 들였다(내용 그대로). 자동 캐노피(움직이거나 캐터펄트면 닫고, 지상에서 멈추면 2초 뒤 열기), 기어·후크 연동(공중에서 기어를 따라 후크, 지면 3초면 후크 올림), 리스폰 복구(다시 나타난 뒤 폭발 클립이 끈 오브젝트를 켜고 `Animator.Rebind()` — 기체 애니메이터에 리스폰 애니메이션을 넣었다면 `RebindOnRespawn`을 꺼도 된다). 셋업: `ACO → Player Vehicle → Add Auto Canopy + Gear-Hook Link to Selected Vehicles`(기체 아래 오브젝트 + 확장 목록, 캐노피·후크를 다이얼에서 뺄지 선택). 셋업 스크립트는 UnityEditor를 쓰므로 반드시 Editor 폴더에 둔다(빌드 실패 방지).

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
- **SAV 리미터 권장값**(DFUNC_Limits — `Limits`가 하중 방향 피치 입력과 요 입력을 줄인다): **바뀜(6-141)**: 기동성 강화가 받음각·하중 한계를 최대 +5°·+5G 올린다(안정성 강화는 기동성에 합쳤다).
  - 받음각: `AoALimiter`(완전히 막는 각) ≤ 실속각 `MaxAngleOfAttackPitch` × 0.65, `AoALimiter_Begin` = 그보다 5~6° 낮게. SF-1(실속 25°)이면 Begin 11 / Limit 16(기본 10/14도 안전). 기동 강화로 피치가 세지면 순간 넘침이 커지므로 구간(Limit − Begin)을 좁히지 말 것.
  - 하중: `GLimiter_Pos` ≈ `MaxGs` × 0.9, `GLimiter_Begin_Pos` = 그보다 6~8 낮게(SF-1 MaxGs 40 → 28 / 36). 음의 G는 -1(=양과 같게) 그대로.
  - `DefaultLimitsOn` 켜기, 저속 실속은 리미터가 못 막는다(받음각을 줄이면 양력이 모자라 가라앉는다) — 안정성 강화(`Lift` ×1.5)가 실속 속도를 낮춘다.

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
  - 닿은 콜라이더가 물 레이어면 물, 유닛 히트박스(17)면 공중과 같은 연출, 그 밖이면 땅. `ExplosionObject`는 어디서 터지든 켜고 땅·물이면 그 칸의 오브젝트를 더 켠다(파티클 수를 아끼려고 공통 부분을 나눠 쓴다). 소리 칸이 비면 `ExplosionSounds`.
  - 땅·물 연출 오브젝트는 위(+Y)를 닿은 면의 법선으로 돌린다(물기둥·흙먼지가 똑바로 선다). 탄의 자식으로 두고 꺼 둔다(발사·회수 때 끈다).
  - 미사일은 이제 물 레이어에도 닿으면 터진다(예전엔 지형 레이어만 — 바다에 떨어지면 물속으로 들어갔다).
  - **물은 트리거까지 본다**: 씬 바다(`Water`, 레이어 4)는 SAV 기체가 뜨도록 **트리거 상자**(수면 0 ~ -200m)다(SAV_FloatScript도 트리거까지 검사). 그래서 땅·유닛은 트리거를 빼고(락온 마커 등), 물(`WaterLayers`)은 일반 콜라이더와 트리거를 모두 **따로** 검사해 가까운 쪽을 쓴다 — 물 레이어엔 바다만 있어 다른 트리거는 안 걸린다. 적용: 미사일 충돌(`ACO_Missile.CheckHit`), 폭탄·로켓·포탄 충돌(`ACO_Ordnance.Step`), CCIP 낙하 지점(`ACO_DFUNC_Missile.ComputeCcip`), 물보라 수면 찾기(`ACO_WaterFx.Probe`). 예전엔 모두 트리거를 빼서 수면을 지나쳐 해저에서 터지고 물보라가 안 나왔다. (생성기의 물 판정은 해수면 높이 검사가 같이 있어 그대로 동작한다.)
- **새 무장 자동 설치**: Bake(`ACO_SpecialWeaponCatalog.InstallForBake`)가 ① 코드에 새로 더해진 예제 정의(파일이 없고 씬의 어느 격납고 목록에도 없는 ID)를 만들고 ② 기체 특수무장 슬롯에 없는 모듈을 넣고 ③ 격납고 목록을 맞춘다. 일부러 지운 예제는 격납고 목록에 ID가 남아 다시 만들지 않는다. 메뉴 `ACO → Bake NPC References`가 다시 보인다(예전엔 메뉴 표시가 매개변수 있는 함수에 붙어 있어 메뉴가 등록되지 않았다).
- **상점 구매 버튼**(`ACO_Shop`): 강화·소모품 줄을 누르면 고르기만 하고(줄 강조), 아래 구매 버튼(`BuyButton`, 버튼 동작 5 = 상점 구매)에 "BUY 이름 LV3  4,000 CR"이 뜬다 — 눌러야 산다. 고른 줄은 남아 있어 다음 단계는 구매 버튼만 다시 누르면 된다. 탭을 바꾸면 고른 줄이 풀린다. 구매 버튼이 없는 예전 격납고 프리팹이면 줄을 누르자마자 산다(그대로 동작).

### 6-55. SCM (Stand-off Cluster Missile) — 클러스터 미사일

- **역할**: 넓은 지역 제압. 호송대·기지·상륙 지점처럼 모인 표적용. 단일 표적엔 LAGM이 낫다. 격납고 가격 14000, 정렬 58(LASM 다음), 강화는 특수 대지 탭.
- **성능**(`ApplySCM`): 락온 6km·30도·2초, 표적 1개, 슬롯 2 / 8발 / 14초, 느리다(최고 550m/s, 선회 60, 수명 30초), 시커 180도(올라가는 동안 표적을 놓치지 않게). 자탄마다 피해 110 / 범위 30m(가장자리 40%) — 무장 `Damage`·`SplashRadius`라 강화가 자탄마다 적용된다.
- **비행**: 탄체 `ACO_Missile_SCM`(빌더가 LAGM 프리팹을 복사해 만든다 — `ACO_PrefabBuilder.BuildScmMissile`, 프리팹이 없으면 카탈로그가 만든다). 톱어택 — 표적 위 700m를 향해 올라가다 내려다보는 각이 55도가 되면 내리꽂는다.
- **산개**(`ACO_Dispenser`, 미사일 프리팹 자식, 동기화 없음): 살아 있는 표적까지 `BurstDistance`(420m) 안이면 탄체가 터지고(`_dispensed` — 공중에서는 범위 피해·범위 표시 없음) 표적 쪽으로 반각 `ConeAngle`(12도) 원뿔에 자탄 `Count`(16, 최대 32)발을 뿌린다. 자탄은 오브젝트가 아니라 파티클(`Bomblets`, 곧게 `BombletSpeed` 260m/s). 떨어지는 자리는 원뿔 안 황금각 나선 방향으로 지면까지 레이캐스트(모든 화면이 같다, 바다는 트리거라 트리거도 맞힌다, `MaxReach` 1.5km 안에 안 닿으면 그 자탄은 없다) — 내리꽂는 각도 덕에 지면에 약 220 × 180m 타원으로 깔린다.
- **착탄**: 닿는 시각(거리 / 탄속)마다 폭발 파티클(`Blasts`)을 그 자리로 옮겨 방출, `SoundEvery`(3)발마다 폭발음(소스 3개를 돌려 쓴다), `ACO_Missile.SubmunitionHit`으로 범위 피해 — 쏜 사람 화면에서만(폭탄·로켓과 같다). 프레임마다 그때 닿은 자탄만 처리해 판정이 한 프레임에 몰리지 않는다. 한 표적이 자탄 여러 발을 맞을 수 있다.
- **풀 회수**: 자탄이 다 떨어지고 파티클이 사라질 때까지(`Dispenser.GetDuration`) 탄체를 켜 둔다(자식이라 먼저 끄면 같이 사라진다).
- **예외**: 표적이 먼저 죽거나 플레어·시커 이탈로 놓치면 직진하다가, 앞쪽 땅이나 바다가 `BurstDistance`(420m) 안에 들어올 때 비행 방향으로 뿌린다(`HasGroundAhead`). 수명이 다할 때까지 땅을 만나지 않으면 공중에서 터지고 자탄은 없다. CIWS 요격에 걸리면 뿌리기 전에 터진다.
- **값을 바꿀 때**: `BombletSpeed`는 자탄 파티클 시작 속도와 같아야 폭발 시각이 맞다 — 다르게 하려면 프리팹의 Bomblets 파티클도 고친다. 탄체 값(Dispenser·TopAttack)은 프리팹 `ACO_Missile_SCM`, 무장 값은 `ACO_Special_SCM`.

### 6-73. 발사 후 락 유지

- `ACO_Missile.LockHoldTime`(프리팹 옵션)·`ACO_DFUNC_Missile.LockHoldTime`(무장 옵션, 0보다 크면 프리팹 값을 덮어쓴다): 발사 후 이 시간 동안 시커 판정(`IsSeekerDefeated`)을 건너뛴다 — 시커 범위 초과·노칭으로 표적을 놓치지 않는다. 기본 0.
- 플레어·채프는 막지 않는다(`IgnoreCountermeasures`가 따로 있다). 표적이 격추되거나 사라지면 그대로 끝난다. 시간이 끝나면 일반 시커로 돌아가, 그때 범위 밖이면 놓친다.
- 무장 값은 강화로 바뀌지 않아 이벤트로 보내지 않고 받는 쪽 무장 값을 쓴다. 유도 폭탄·로켓(`ACO_Ordnance`)에는 적용되지 않는다.

### 6-81. 플레이어 ECM(전자전 포드)

- **장착**: 특수무장 `ECM`(가격 15000, 격납고 기타 분류). `ACO_DFUNC_Missile.EcmMode`로 만들어 슬롯·다이얼·격납고·HUD를 무장과 같이 쓴다. 해금 키는 ID `ECM`로 고정한다.
- **동작**: 트리거로 켜면 10초 재밍, 끝나면 20초 재충전. 반경 5km(`ACO_RadarContacts.EcmRange`). 비행 HUD 잔탄 칸에 `ON 12` / `+8` / `RDY`.
- **효과**(적만, 켠 사람 주변): 적 미사일은 구역 안에 1초 머물면 표적을 놓친다. NPC 락온은 구역 안 표적에 쌓이지 않고 풀린다. 적 대공포는 조준 흔들림 3배, CIWS 요격 시간 1.5배. 적기 기총은 방해하지 않는다. 플레이어 미사일과 아군 함선·헬기는 영향이 없다.
- **판정**: 미사일 차단은 쓸모없는 계산이 없게 한 번 끊고 끝낸다(유도 약화보다 가볍다). 켜는 이벤트를 모두에게 보내고 끝은 서버 시각으로 계산한다. 켠 ECM이 없으면 `IsJammed`가 바로 false, 있으면 미사일·런처·포대가 0.25초마다 거리만 잰다.
- **표시**: 켜진 ECM의 구역을 HUD 레이더와 조종석 지도에 초록 테두리 원으로 그린다(화면 밖 호는 뺀다). 내 기체가 구역 안이면 비행 HUD에 `ECM SUPPORT`, 소리 `ACO_Ecm_Loop`(레일건 충전 같은 전자음 + 약한 지직거림, `Tools/aco_make_sfx.py`)가 나온다. 소리는 Bake가 `ACO_RadarContacts` 자식 `EcmSound`로 만든다.

### 6-83. 스로틀 위치 표시, 지상 기어 잠금, 갑판 택싱 제한

- **스로틀 위치**: 엔진 출력 막대 왼쪽의 `>`가 `ThrottleInput`(목표 출력)을 가리킨다. 막대는 실제 출력이라 `>`를 뒤따라간다. 조종사에게만 보인다.
- **지상 기어 잠금**: `ACO_VehicleAutomation`이 `Taxiing` 동안 `DFUNC_Gear.DisableGearToggle`을 올린다. 갑판뿐 아니라 모든 지면에 적용한다. SAV의 `AllowToggleGrounded`를 끄면 동체 착륙 뒤 기어를 내릴 수 없어(지면 레이 검사) 쓰지 않았다.
- **갑판 택싱 제한**: 갑판(`ACO_PlayerProgress.SafeZones`) 위에 닿아 있고 `DeckTaxiSpeed`(2.6m/s, 약 5kt — 사용자 요청으로 낮춤, 예전 10m/s · 약 19kt, Setup Check `deck-taxi-5kt-1`) 이하이면 제한이 걸려 `FixedUpdate`에서 수평 속도를 그 값으로 묶는다. FA-18의 충돌 사망 속도(30m/s) 아래이고, 스로틀은 건드리지 않아 `>`와 조종사 입력이 그대로 남는다. `ThrottleOverride`는 `PlayerThrottle`까지 덮어써서 쓰지 않았다.
- **걸리지 않는 경우**: 캐터펄트에 걸린 동안, 제한보다 빠른 채로 갑판에 닿은 경우(착함 실패 뒤 재이륙), 비행장 활주로. 갑판에서 활주 이륙은 할 수 없고 캐터펄트로만 뜬다.

### 6-87. 기체 표시 — 헐넘버·콜사인·킬마크 (`ACO_VehicleMarks`)

- **프로젝터를 쓰지 않는 이유**: 프로젝터는 틀에 걸친 메시를 통째로 다시 그린다(FA-18은 렌더러가 잘게 나뉘어 비싸다). 표시는 기체에 붙인 판 항목당 1드로우다. 데칼 프로젝터도 안 보일 때는 오브젝트째 끈다.
- **Z파이팅**: 셰이더 `ACO/MarkText`가 정점을 카메라 쪽으로 당긴다(`_PullPercent` 거리의 0.5% + `_PullMeters` 5cm, 값이 클수록 많이). 화면 자리는 그대로다. 앞의 물체를 뚫어 보이면 두 값을 줄인다.
- **보이는 거리**: 헐넘버·콜사인·킬마크 모두 1500m까지 그대로 보이고 1800m에서 사라진다(`ACO_MarkText.mat`, `ACO_MarkIcons.mat` — 관전 망원 최대 약 1.5km). 실제 기체는 거리로 오브젝트를 끄지 않는다 — 관전 카메라는 내 몸과 멀리 떨어져 있어 내 위치로는 판단할 수 없다. `ShowRange`(400m)는 격납고 샘플에만 쓴다.
- **데이터**: 새 동기화 없음. 조종사의 `PlayerData`를 읽는다 — 헐넘버 `aco_hull_no`(콜사인 패널 아래 HULL NUMBER, A-Z 0-9, 최대 6자), 콜사인 `aco_tac_name`(비어 있으면 표시하지 않는다), 최고 격추수 `aco_best_kills`(`ACO_PlayerProgress.AddMissionKill`). 두 키는 절대 바꾸지 않는다.
- **킬마크**: 한 미션에서 내가 쓰러뜨린 표적 수(공중·지상 모두)의 최고 기록. 100·10·1 단위 아이콘(날개 달린 별·전투기 3기 편대·전투기, 검은색 `IconColor`)으로 줄인다. 폰트에 기호가 없어 `Tools/aco_mark_icons.py`의 거리값 아틀라스 `Textures/Marks/ACO_KillMarks.png`로 메시를 만든다. 한 줄 `IconColumns`(10)칸, 최대 `MaxIcons`(30)개.
- **집계**: 최고 격추수는 미션이 끝날 때(`GrantReward`, 성공·실패 모두) 저장한다. 도중에 나가면 기록되지 않는다.
- **격납고 미리보기**: 데칼 스튜디오 `SampleRoot` 아래 `ACO_Marks`는 `EntityControl`이 비어 있어 탑승자 대신 내 값을 보여 준다. 다른 사람에게도 각자 자기 값이 보인다. 별도 체크 칸을 두지 않는다 — 예전 `PreviewMode`가 기체 프리팹에 켜져 있어 모든 기체가 보는 사람의 값을 그렸다.
- **설치**: `BakeVehicleMarks`가 플레이어 기체 프리팹의 `SaccEntity` 아래(FA-18은 자식 `F/A-18E`)에 `ACO_Marks`를 넣는다(이미 있으면 건너뜀). 자리는 FA-18 어림값이라 프리팹의 `ACO_Marks/Marks` 아래에서 맞추고, Bake가 같은 자리를 미리보기로 복사한다. 킬마크는 실행 중에 메시를 만들므로 에디터용 메시 `Generated/ACO_KillMarkPreview.asset`(199킬 모양)을 `Kills_L/R`에 넣어 두고, 실행하면 실제 값으로 바뀐다.
- **폰트**: Black Ops One(OFL, `Fonts/BlackOpsOne-OFL.txt`). Bake가 A-Z 0-9 - 공백만 담은 고정 SDF 폰트 `Fonts/ACO_MarkFont.asset`을 만든다 — 콜사인·헐넘버 입력 규칙이 같아 한글 폰트가 필요 없고, 동적 폰트를 쓰지 않으며, 빌드에 폰트 파일이 들어가지 않는다.

### 6-96. 격납고 OTHER 탭

- **탭**: 강화 탭에 OTHER를 더해 GUN DAMAGE를 AIRFRAME에서 옮겼다. 탭은 AIRFRAME, MISSILE, SP. AAM, SP. AGM, SP. BOMB, OTHER, ITEMS 순이다. **바뀜(6-142)**: 안정성을 빼 AIRFRAME에 자리가 나서 GUN DAMAGE를 AIRFRAME(6번째 줄)으로 되돌리고, OTHER 자리는 SUPPORT 탭(지원 호출권 구매, `ACO_Shop.TAB_SUPPORT` = `CAT_SUPPORT` 5, 강화 항목 0개)이 됐다.
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
- **텍스처 고침**: (1) 둘레(약 2.42m) + 여백이 텍스처 높이 1024px를 넘어(420px/m) 왼쪽 어깨(NO STEP·HOIST POINT 줄)가 잘리고 반대편 텍스처가 비쳤다 → 400px/m. (2) 아랫면 글자(FWD·JETTISON)가 거울 글자였다 — 옆면과 뒤집는 방향이 달라 `BOTTOM_SIDE` = 'L', 화살표는 돌리지 않아도 기수를 가리킨다. 장착 자세(받침이 위)로 렌더해 옆면·아랫면 모두 바로 읽히는 것을 확인했다.

### 6-102. 대응탄 포드 외형

- **용도**: 소모품 `CONS_CM`의 외형이다. 효과는 그대로 플레어 1.5배이고, 플레어는 기존 방출 지점에서 나간다. 모델은 외형만 맡는다.
- **파일**: `Models/FlarePod/ACO_FlarePod_FA18E.fbx`(한 메시, 좌우 한 쌍, 864삼각형)와 `ACO_FlarePod_Albedo.png`. `Tools/aco_flare_pod.py`가 만든다. 원점은 항공기 원점이라 FA-18E 아래에 Transform 0으로 넣으면 제자리다. 칸은 `ConsumableVisuals[1]`이다.
- **자리**: 엔진 나셀 아랫면, 바퀴 수납부 뒤다. 표면에 평면을 맞췄고 밑면은 표면 안에 묻힌다.
- **모양**: 앞이 낮은 경사로이고, 3 × 6칸 발사 격자가 있다. 톱니 판은 기수 쪽을 향해 장갑판과 맞춘다. 색은 포드 자리의 본체 도장에서 읽는다.

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

### 6-124. MMP(마이크로미사일 포드) 특수무장

- **무장**(`ACO_Special_MMP`, `ApplyMMP`): 공중·지상 표적(레이어 29·30)을 8개까지 락온(0.8초씩, 3.5km·40도). 쏘면 장전된 16발이 `SalvoInterval` 0.07초 간격으로 락온한 표적들에 차례로 나뉘어 나간다(`OneMissilePerTarget` 끔 — 8개면 2발씩, 1개면 16발). 한 발은 피해 60, 범위 8m, 근접 8m, 속력 800, 선회 140. 16슬롯 / 64발 / 15초. 락온 없이는 쏘지 않는다. 강화를 받지 않고(`UpgradeFamily` None — 16발이 모두 강화되면 너무 세다) 격납고 분류는 OTHER(`mmp-other-1`).
- **사출**: `DropTime` 0.35초 동안 엔진 없이 떨어진다. 모기체 속도에 발사 지점 기준 `EjectDirection`(아래, 조금 앞) × `EjectSpeed` 14m/s를 더하고(`ACO_Missile.ColdLaunchKick`), 떨어지는 동안 `DropTurnRate` 200도/초로 표적 쪽으로 기수를 돌린 뒤 점화한다. 새 칸은 모두 기본 0(다른 무장은 그대로).
- **비틀거림**(새 — 곧게 나가 보는 재미가 없었다): 유도가 노리는 방향을 표적 쪽에서 노이즈 각도만큼 비튼다(`ACO_Missile.WanderAngle` 최대 30도, `WanderFrequency` 0.9Hz) — 미사일은 선회 한계(140)로 그 흔들리는 방향을 쫓아 술 취한 듯 구불구불 표적 쪽으로 간다(바뀜 — 처음엔 유도 뒤에 기수를 초당 110도로 더 돌렸는데, 유도 선회가 더 빨라 매 프레임 바로잡혀 거의 곧았다). 표적 `WanderFadeDistance` 400m 안에서 줄어 200m부터는 곧게 꽂힌다(명중률 그대로). 노이즈는 발사 뒤 시각과 씨앗(`WanderSeed` = 슬롯·표적 번호, 발사 이벤트에 이미 있는 값)으로만 정해져 모든 화면이 같은 모양을 그린다(새 동기화 없음). 다른 미사일은 0(곧게). 이미 만든 탄체는 Setup Check `mmp-wander-2`.
- **연사**: `ACO_DFUNC_Missile`이 쏠 슬롯·표적을 미리 골라 두고 `_SalvoStep`으로 한 발씩 보낸다(네트워크 이벤트 초당 약 14회 — `NetLaunch` 한도 20 안). 연사 중엔 다시 쏘지 않고, 내리거나 격추되면 멈춘다.
- **포드 외형**(`Models/MMP/ACO_MMP.fbx`, `Tools/aco_mmp_pod.py`): 납작한 육각 단면, 같은 비율로 좁아지는 기수(살짝 뭉툭한 끝면)와 꼬리(평평한 끝면), 길이 2.0m. 아랫면에 VLS처럼 덮개 달린 셀 8개(2열 × 4) — 아래·조금 앞으로만 사출한다(옆으로 쏘면 같은 날개의 다른 파일런에 걸린다). 양옆 아래 경사면에 사출 가스 벤트(루버) 4개씩. 날개 없음. 삼각형 2124개, 텍스처 4096×2048(표기는 모두 가공). 재질 `Generated/ACO_MMP.mat`.
- **발사 지점**: 포드는 기체 프리팹의 파일런 아래 Transform 0으로 직접 놓는다(SCM과 같다). 셋업 검사 [고치기](`InstallMmpLaunchPoints`)가 포드마다 셀 자리에 `MMP_Launch_0~7`(앞 = 기수, 위 = 위)을 만들고 MMP 모듈 `LaunchPoints`에 포드를 번갈아 넣는다(연사가 양쪽 날개에서 번갈아). 자리는 `ACO_MmpModel.MmpLaunch` — 스크립트의 LP 줄과 같아야 한다. 포드가 없으면 날개 파일런에서 나간다.
- **탄체**(`ACO_Missile_MMP`): SRAAM 프리팹 복사. 모델은 맥주캔 같은 원통(`Models/MMP/ACO_MMP_Missile.fbx`, `Tools/aco_mmp_missile.py` — 지름 9.5cm·길이 26cm, 말린 테두리, 좁아지는 목, 유리 시커 돔, 오목한 바닥 안 노즐, 펼친 X자 날개 4장, 삼각형 1104개, 표기는 가공). SRAAM 메시는 끄고 캔을 탄체 축 그대로 `Model` 아래에 둔다. 엔진 불꽃(`Effects`)과 궤적(`Trail`)은 노즐 끝(z -0.135)으로 옮기고 불꽃 메시는 0.35배. 궤적 폭 45%, 폭발 크기 0.6배(`ACO_FxBuilder`가 이름 MMP로). 이미 만든 탄체는 Setup Check `mmp-missile-model-1`이 다시 만든다.
- **SCM 포드 걸이**: 앞 걸이(y 0.781) 자리는 몸통 윗면이 기수 쪽으로 내려가 TOP보다 1~1.7cm 낮아 받침이 떠 보였다. 받침 아래를 그 자리 윗면까지 내려 박았다(`aco_scm_missile.py` — 파일런 외형·비행 모델 둘 다).

### 6-126. RGP(레일건 포드) 특수무장
- **무장**: 초고속 단발탄. 탄속 4000m/s(10km를 2.5초), 16발, 한 발 쏠 때마다 8초 재충전(슬롯 1). 공중·지상 모두(레이어 29·30). 피해 350(전투기 한 발), 범위 6m. 강화 없음(`UpgradeFamily` None), 격납고 OTHER.
- **락온**: 좁고(2.5도) 즉시(0.05초), 10km. 락온하고 쏘면 발사 순간 탄의 기수를 표적의 예상 자리(지금 속도로 만나는 요격점 — 표적 속도는 SAV `CurrentVel`·NPC 비행값)로 돌린다(`ACO_DFUNC_Missile.SnapAimAtLaunch` → `ACO_Missile.SnapAimAtLaunch`/`AimAtIntercept`). 날아가는 동안은 리드 유도로 6도/초만 보정한다 — 급기동하면 빗나간다. 락온이 없으면 기수 정면으로 곧게(근접 신관이 곁을 지나는 적기에 터진다 — `ProximityAnyEnemy`). 플레어·채프 무시.
- **처음 속력**: 모듈 `MuzzleSpeed`(4000)가 발사마다 탄 `LaunchSpeed`를 덮는다(0이면 프리팹 값).
- **연출**: 소형 레일건과 같게, 공중 폭발 없이. 궤적은 연기 대신 0.5초 남는 청백색 빛줄기(가산), 날아간 거리 180m마다 진행 방향을 보는 충격파 고리(지름 36m — 소형 레일건 고리와 같은 재질·색, `Fx_RailRings`, 궤적처럼 발사마다 지운다). 명중은 청백색 섬광·불티만(불덩이·연기 끔). 포구 섬광(`RGP_Flash` — 섬광·앞으로 튀는 불티·충격파, 모듈 `LaunchFlash`가 발사마다 재생)과 발사음(포구 AudioSource, 소형 레일건 발사음). 충전 연출은 없다 — 충전 표시창·레일 안쪽 판은 늘 켜진 빛나는 재질.
- **탄**(`ACO_Missile_RGP`): SRAAM 프리팹을 복사해 모델을 빛나는 육각 쌍뿔 탄체(길이 0.9m, `Generated/ACO_RGP_Slug.asset`)로 바꾸고 엔진 불꽃·불빛·소리를 끈다(`ACO_PrefabBuilder.BuildRgpSlug`, 카탈로그가 없으면 만든다).
- **외형**: 파일런 두 개 — 왼쪽 파일런 축전기 포드(`Models/RGP/ACO_RGP_Cap.fbx`, 길이 2.76m), 오른쪽 파일런 레일건 포드(`ACO_RGP_Rail.fbx`, 길이 4.25m — 사각 몸통이 덮개로 매끈하게 모여 가는 배럴로 이어진다, 위아래 레일과 가로 슬롯, 배럴이 날개 앞전보다 앞으로 나온다). 모서리를 깎은 사각 단면, 생성기 `Tools/aco_rgp_pod.py`(1.5배로 키운다 — 윗면 높이와 걸이 자리는 그대로). 파일런 로컬 x는 기체 x와 반대라 전원 연결구는 각 포드의 동체 쪽(레일건 +x, 축전기 -x)에 있다.
- **설치**: 포드는 기체 프리팹의 파일런 아래에 Transform 0으로 손으로 놓는다(MMP와 같이 모듈 외형 묶음 `ModuleVisuals`로). Setup Check [고치기](`InstallRgpLaunchPoints`)가 레일건 포드 포구(파일런 로컬 0, 2.805, -0.625)에 `RGP_Muzzle`(발사음·섬광)를 만들어 모듈 `LaunchPoints`·`LaunchSound`·`LaunchFlash`에 넣고, 비어 있는 포드 재질(`Generated/ACO_RGP_Rail.mat`·`ACO_RGP_Cap.mat`·`ACO_RGP_Glow.mat`)을 넣는다. 기체 프리팹 파일을 고친다.

### 6-141. 기동성 강화에 안정성 합치기 + 리미터 한계가 같이 오름
- **이유**: 기동성(피치·요·롤 힘)만 올리면 받음각이 순간 튀어 리미터를 지나 실속하기 쉬웠고(그래서 피치·요는 절반만 받았다), 안정성(양력)이 그걸 메우는 짝이라 따로 사면 함정이었다.
- **안정성 강화 뺌**: 저장 키 `stability`는 다시 쓰지 않는다(안정 ID 규칙). 테스트 단계라 산 단계는 돌려주지 않는다.
- **4번 자리 = 대응탄 수(COUNTERMEASURES)**: 새 저장 키 `countermeasure`, **단계마다 한 발씩**(`ACO_PlayerProgress.CountermeasurePerLevel` 1 — 10단계 +10발, 상점에 "+3"처럼 발 수로 표시), 1단계 1,500. SAV 플레어 최대 수(DFUNC_Flares.FullFlares) = (원래 수 + 강화 발 수) × 대응탄 포드(소모품) 배수 — 탈 때 채우고 내릴 때 원래 수로. 아이콘은 대응탄 포드와 같은 `cm_pod`. 이미 있는 씬은 Setup Check `cm-upgrade-1`(진행도의 4번 이름·배수·가격) 뒤 Bake(상점 아이콘 — Bake가 항목 내용이 바뀌어도 아이콘을 다시 채운다).
- **기동성 강화가 함께 올리는 것**(강화 비율 t = 단계 / 최대 단계, `ACO_VehicleUpgrades`):
  - 회전: 롤 ×(1 + 0.03·단계), 피치·요는 그 `PitchYawShare`(0.5)만큼 — 그대로.
  - 양력: Lift × (1 + `LiftBonus`·t), ExtraVelLift + `LiftBonus`·t — 10단계 ×1.5(예전 안정성 10단계와 같다).
  - **리미터 한계**: SAV `DFUNC_Limits`의 받음각 시작·한계를 `LimiterAoABonus`·t(최대 +5°), 하중 시작·한계를 `LimiterGBonus`·t(최대 +5G) 올린다. 시작점과 한계를 같이 올린다(리미터가 구간 폭을 시작할 때만 잰다). 받음각 한계는 실속각의 80%(`LimiterAoAMaxRatio`), 하중 한계는 MaxGs − 2를 넘지 않는다.
  - FA-18E(실속 25°, MaxGs 50, 리미터 10/14°·35/40G) 10단계: 받음각 15/19°(실속의 76%), 하중 40/45G. 내릴 때 원래 값으로.
- 리미터 켜고 끄기(SAV 다이얼)는 그대로다.

### 6-142. 지원 호출권 — 벙커버스터 폭격 · 급유 드론
- **개요**: 상점에서 사 모으는 **호출권**(산 만큼 쌓임, 종류마다 최대 9장, 미션이 끝나도 남는다). 격납고에서 이번 출격에 가져갈 지원을 **하나** 고르고(없음 / 폭격 / 급유), 조종석 다이얼의 **SUPPORT 칸**으로 부른다. 쿨타임은 플레이어마다(폭격 120초, 급유 180초 — 진행도 인스펙터 값). 플레이어 기체는 FA-18E(`Pit-bool/FA-18E Main_ACO.prefab`) 하나 — 프리팹 에셋을 고친다.
- **저장**(새 키 — 바꾸거나 다시 쓰지 않는다): 장수 `aco_call_strike`, `aco_call_tanker`(int), 고른 지원 `aco_call_pick`(0 없음 / 1 폭격 / 2 급유). 진행도 `ACO_PlayerProgress`에 `CALL_STRIKE = 0`, `CALL_TANKER = 1`, 이름·효과 문구·가격·쿨타임·최대 장수 배열(`CallNames/CallEffects/CallPrices/CallCooldowns/CallMaxCount`).
- **상점**(**SUPPORT 탭** — 예전 OTHER 자리, 탭 아이콘 `operation`): 두 줄 `BUNKER BUSTER  LASER STRIKE 500M x3  10,000 CR`, `TANKER DRONE  FULL RESUPPLY x1  4,800 CR`(최대면 MAX). 카운트다운 중에는 못 산다(소모품과 같다). 아이콘은 폭탄 탭(`tab_sp_bomb`)·연료(`fuel`) — Bake가 `CallIcons`에. 탭 설명 "SUPPORT CALLS - KEPT UNTIL USED, PICK ONE UNDER SUPPORT (LEFT)". 소모품(ITEMS) 탭은 소모품 4줄만.
- **격납고**: 특수무장 목록 아래 한 줄 `SUPPORT  < BUNKER BUSTER ×3 >` — `<` `>`로 없음/폭격/급유를 돌린다. 장수 0인 것도 고를 수 있지만 부르면 "NO CALLS LEFT". 고른 지원은 저장된다.
- **SUPPORT 다이얼 칸**(`ACO_DFUNC_Support`, 새 스크립트): 기체 SaccEntity의 **오른쪽 다이얼 4번째 칸**(`Dial_Functions_R[3]`). Setup Check [고치기]가 프리팹에 넣는다(그 칸에 다른 기능이 있으면 경고만 — 직접 옮긴다). HUD 잔탄 줄에 `BB`/`TNK` + 장수(쿨타임 중엔 `85s`, 안내는 잠깐 `NO SPOT` 등). **MFD**: 오른쪽 다이얼 표시(StickDisplayR)의 왼쪽 칸에 `MFD_Support`(기총 글자 MFD_Gun 복제 — 다이얼 칸 순서 위·오른쪽·아래·왼쪽이라 Element 3 = 왼쪽) — 첫 줄 `BB x3`, 쿨타임·안내는 줄바꿈해 둘째 줄. 고른 지원이 없으면 `SUP - N/A`, 불러도 아무 일도 없다.

#### 벙커버스터 폭격(`CALL_STRIKE`)
- **조준**: SUPPORT 칸을 고르면 기체의 **타게팅 포드**(AN/ASQ-228 — 프리팹에 이미 있음, 렌즈 자리 `TGP_Laser` 점을 Setup Check가 만든다)에서 **바라보는 방향**(머리 방향 — VR은 머리, 데스크톱은 시선)으로 레이저가 나간다. 지면·물·유닛에 닿는 점(최대 10km)에 원형 지역 표시(반경 = 폭발 범위). 각도 제약 없음. 닿는 곳이 없으면 표시 없이 `NO SPOT` — 트리거를 당겨도 무시.
  - 고르기 전·조준 중 레이저는 **로컬**(머리 방향을 매 프레임 보내지 않는다).
- **투하**: 트리거를 당기면 그 지점으로 확정 — 호출권 1장 사용, 쿨타임 시작, AWACS 응답 무전. **지역 지정형**(락온 없음, 확정 뒤에는 기동해도 된다). 확정부터 탄착까지 포드 → 지점 레이저를 **모든 화면**에 보인다(포드 쪽 끝은 내 기체를 따라간다).
- **폭격기**: 확정 지점 상공(지점 높이 + 2,500m), 호출한 사람의 진행 방향으로 3km 앞에서 아군 폭격기 한 대가 나타나 지점 위를 지난다(220m/s). 투하점에서 벙커버스터 한 발을 떨어뜨리고 상승·이탈, 화면 밖에서 사라진다. 무적, 적이 노리지 않고 HUD 표적이 아니다. 모델은 **프리팹 `ACO_SupportBomber`**(바뀜 — 예전엔 풀마다 씬 복사본): 없을 때 NPC_Bomber의 `Model` 겉모양만 복사해 만든다(스크립트·충돌·파티클 없음). 풀 4칸은 이 프리팹의 인스턴스라 `Model`을 바꾸면 모두 바뀐다(진행 방향 +Z).
- **벙커버스터**: `ACO_Ordnance`(유도 폭탄)에 **고정 지점 유도**(표적 대신 위치)를 더해 쓴다. 모델은 `Pit-bool/FA-18/prefab/weapon/GBU-24` 메시(원본 프리팹 루트 회전 -90도 X를 모델에 그대로 둬야 기수가 진행 방향 — 바뀜: 예전엔 회전을 0으로 둬 옆으로 누운 채 날았다, Setup Check `strike-bomb-forward-1`). 낙하 약 12초. 피해 중심 1,500 → 가장자리 30%, **범위 500m**(진행도 값), 큰 폭발(`ACO_FortressBlast` 작은 크기 + 지면 충격파). 피해·격추 점수는 **호출한 사람**(다른 폭탄과 같은 규칙 — 피해는 그 화면만). 호위 대상(아군)은 피해 없음.
- 확정부터 탄착까지 약 20초(폭격기 접근 + 낙하).
- **동기화**: 호출한 사람이 `지점·진행 방향·시각`을 네트워크 이벤트로 보내면 모든 화면이 폭격기·폭탄·폭발을 각자 재현(늦게 들어온 사람은 진행 중인 폭격을 못 봐도 된다). 장면 관리자 `ACO_SupportManager`(씬에 하나)가 폭격기·폭탄 풀(4세트)을 가진다 — 여러 명이 동시에 불러도 된다.

#### 급유 드론(`CALL_TANKER`)
- **호출**: SUPPORT 칸에서 트리거 — 쿨타임 중이 아니고 장수가 있으면 출발(이때는 호출권을 쓰지 않는다). AWACS 응답.
- **드론**: 호출한 사람 뒤쪽 위 2km에서 나타나 **호출한 사람 앞쪽 위**로 와서 속도를 맞춘다(약 12초). **편대 추종**(LUNA UAV와 같은 방식): 드론은 월드 좌표로 움직이며 매 프레임 기체 속도만큼 같이 가고 자리 쪽으로 지수 보간한다(대기·호스 풀기 `FollowSharpness` 2.5, 연결 이후 `ContactSharpness` 8). 접근은 상대 속력 최대 `ApproachSpeed` 220m/s, 남은 거리 × 0.8로 감속. 자세는 진행 방향(속도) + 플레이어 뱅크의 `BankFollow`(절반)만 — 기체에 매달린 것처럼 같이 기울지 않는다. 자리·출현 오프셋은 진행 방향 프레임(뱅크 무시). 이탈은 앞쪽 위로 가속(`LeaveAccel`). 꽂힌 동안 드론이 살짝 늦어 생기는 차이는 호스가 곧게 당겨지며 받는다(드로그는 프로브에 그대로).
- **대기**: 자리에 오면 호출한 사람이 `SteadyTime`(1초) 동안 이탈 조건에 안 걸려야 호스를 푼다(`NetTankerExtend`). 그 전엔 HUD/MFD에 STEADY, `WaitTimeout`(20초) 동안 안정되지 않으면 돌아간다(호출권 그대로, 쿨타임). **이미 뱅크·G가 걸린 상태에서 불러도 접근·대기 중에는 끊지 않는다.** 바뀜 — **최저 속도**: 드론은 `MinSpeedKt`(180kt)보다 느리게 날 수 없다(기체가 그보다 느리면 진행 방향으로 최저 속도를 지킨다). 그래서 착륙·저속 중에 부르면 드론이 자리를 지나쳐 버리고(오버슈트) 급유 실패 — "BREAKAWAY / TOO SLOW - MIN 180KT", 무전 break, 호출권 그대로, 쿨타임 `TankerFailCooldown`(15초) — 로 앞·위로 이탈한다. 자리에 온 뒤 느려졌으면 `SlowGraceTime`(2초) 동안 HUD STEADY(SPEED UP)로 기다리고 그래도 느리면 같은 실패. 예전엔 착륙한 기체 앞에 그냥 떠 있었다. 아래에 **버디 포드**(`EEIproducts/AirCraftSupportSet/BuddyPod` — 호스 본 `Drogue_Base → Drogue_001~025 → Drogue_Head`)가 달려 있다. 드론 몸체는 **자리 표시 큐브**(길이 8m 정도 — 사용자가 모델로 바꾼다. 몸체는 `Model` 자식 하나라 통째로 바꾸면 된다).
- **연결 연출**(모든 화면):
  1. 드론이 자리에 오면 기체 애니메이터 bool **`PROBE`**를 켠다(F-18 프로브가 펴진다 — `Prove/Prove.1` 끝이 프로브 끝). 끄면 `OFF` 상태(같은 클립 역재생)로 접힌다 — `FA-18F.controller`의 DEPLOY → OFF 전환 오프셋을 1로 바꿔(펴진 끝부터 거꾸로) 2초에 걸쳐 들어간다(Pit-bool 에셋 — git 밖이라 다시 받으면 되돌려야 한다).
  2. 버디 포드 호스가 풀려나온다(약 3초) — 바뀜: 드로그가 포드 출구에서 **프로브 앞 대기 자리**(프로브 끝에서 기수 방향으로 `PreContactGap` 3m — 프로브 축 위)까지 곧게 나온다. 예전엔 뒤로 아래로 늘어졌다가 프로브로 수직으로 올라와 꽂혀 어색했다.
  3. 드로그(`Drogue_Head`)가 대기 자리에서 프로브 끝으로 들어가 꽂힌다(`ConnectTime` 1.5초) — 코드가 본 사슬을 포드 → 드로그 2차 베지어로 매 프레임 맞춘다. 바뀜: 조절점은 드로그 높이에서 기수 방향으로 출구까지 수평 거리 × `FlowBend`(0.5) — 호스가 출구에서 뒤·아래로 나와 드로그 쪽에선 기수와 나란해져 바스켓이 뒤를 보고 프로브가 축으로 들어간다(예전 처짐 곡선은 끝이 아래에서 올라와 바스켓이 위로 들려 프로브보다 위에 찍혔다). 감겨 있을 때도 모든 본이 출구에서 뒤를 본다(예전엔 감긴 동안 본 회전을 두지 않아 풀리기 시작·다 감길 때 드로그 자세가 튀었다).
     - 본 자세(바뀜 — 드로그가 틀어져 보였다): 본마다 펼친 포드의 원래 자세를 처음에 기억해 두고, 원래 사슬 축(다음 본 쪽)만 곡선 접선으로 돌린다(최소 회전 — 본의 원래 비틀림·기울기 유지). 예전엔 모든 본을 "포드 자세 + 위(+Y) = 접선"으로 놓아, 원래 자세가 포드와 다른 본(바스켓)이 틀어졌다.
     - 꽂으러 가는 동안(`ConnectTime`)도 목표점에 그대로 둔다(예전엔 호스 길이로 잘라 두어 꽂히는 순간 튀었다).
     - 드론·가디언 위치는 `PostLateUpdate`에서 놓는다(바뀜 — Update에선 기체가 그 뒤에 움직여 고속에서 드로그가 프로브와 한 프레임씩 어긋나 뚝뚝 끊겨 보였다).
  4. 꽂힌 채 **3초** 지나면 **연료·체력·모든 무장이 즉시 가득**(SAV 보급을 한 번에 — 연료·체력 = 최대, SAV 무장 각자 가득, ACO 미사일·특수무장 모듈 가득). **이때 호출권 1장을 쓴다.** 무전 "transfer complete".
  5. 드로그가 빠지고(바뀜 — `DisconnectTime` 0.8초 동안 프로브에서 프로브 앞 대기 자리로 물러난다. 예전엔 아래 늘어진 자리로 꺼졌다가 당겨졌다. 빠지기 시작 자리는 기체 기준으로 기억한다 — 드론 기준이던 때는 호출한 사람 화면에서 한 프레임 비행 거리만큼 튀었다) 호스가 같은 선을 따라 출구로 감기고(`RetractTime` 2초 — 풀다 만 상태면 그때 드로그 자리에서부터) `PROBE` 끔, 드론은 앞으로 상승·이탈하고 사라진다.
- **급유는 호출한 사람만**(드론·호스는 모두 보이지만 보급은 호출한 사람 기체만 — 보급 구역 트리거를 쓰지 않는다).
- **급유 조건**(관리자 값 — 대기 중엔 맞을 때까지 기다리고, 호스를 푼 뒤 어기면 이탈): 뒤집힘 없음, 뱅크 `MaxBank` 45도 안, **피치 `MaxPitch` ±20도 안**, 하중 0.3~2.5G, 애프터버너 끔, **속도 `MinSpeedKt`~`MaxSpeedKt` 180~1000kt**(HUD 속도와 같은 대기 속도. 바뀜 — 최고 400kt가 빡빡해 넉넉하게, Setup Check `tanker-max-speed-1`. 0이면 그쪽은 보지 않음). **HUD 경고**: 조건이 안 맞으면 비행 HUD 왼쪽 상태 줄(GEAR·FLAP 아래)에 두 줄로 깜빡인다 — 대기 중 `TANKER` / `BANK UNDER 45`, 이탈하면 `BREAKAWAY` / 이유(3초). 잔탄 줄·MFD 둘째 줄은 짧게(`STEADY`·`BRK`). 호출 실패도 같은 자리(`SUPPORT` / `COOLDOWN 85s`, `NO CALLS - BUY IN ITEMS`, `STRIKE` / `NO SPOT - LOOK AT GROUND` 등).
- **이탈 조건**(호스를 풀기 시작한 뒤부터 완료 전까지, 관리자 값): 하중 0.3G 아래 또는 2.5G 위, 뱅크 45도 위, 애프터버너, 내리거나 격추. 하나라도 걸리면 무전 "breakaway", 호스를 감고 돌아간다 — **호출권은 쓰지 않고 쿨타임은 15초**(바뀜 — 실패해도 정해진 쿨타임 전부였다. 관리자 `TankerFailCooldown` — 이탈·CANNOT JOIN·NOT STABLE·기체가 사라짐 모두. 성공하면 진행도의 급유 쿨타임 그대로).
- **쿨타임 초기화**(바뀜 — 모든 지원 호출): 작전(미션)이 시작되면 폭격·급유·가디언 쿨타임이 모두 0이 된다(`ACO_PlayerProgress`가 미션 시작 시각이 바뀐 것을 보고 지운다). 작전 사이 대기 중에 쓴 쿨타임이 다음 작전으로 이어지지 않는다.
- 이미 가득이어도 부를 수 있다(그대로 쓴다 — 막지 않는다).
- **동기화**: 호출·연결·완료·이탈을 네트워크 이벤트로. 드론은 각 화면이 호출한 사람 기체(SAV가 동기화하는 위치)를 따라가며 그린다. 보급은 호출한 사람 화면(기체 오너)만.

#### 무전(AWACS, `Tools/aco_voice_lines.json`의 새 줄)
- 폭격: 호출 응답("Copy your mark. Bomber inbound."), 투하("Bomb away."), 탄착 확인("Impact. Good effect on target.").
- 급유: 호출 응답("Tanker drone en route. Hold steady."), 연결("Contact."), 완료("Transfer complete. Clear to break."), 이탈("Breakaway, breakaway. Tanker is returning.").
- 문구는 키로 고르고(언어 바꾸기 대비), 음성은 AWACS 목소리(eleven_v4).

#### 구현
- **스크립트**: `ACO_DFUNC_Support`(다이얼 — 조준·호출·HUD 글자), `ACO_SupportManager`(씬에 하나 — 폭격·드론 시간표, 범위 피해, 보급, 이탈 판정, 무전), `ACO_TankerDrone`(드론 한 대 — 호스 본을 포드 출구 → 드로그 목표 2차 베지어에 늘어놓음, 길이는 펼친 포드 본 간격 합).
- **벙커버스터 탄도**: `ACO_Ordnance`를 쓰지 않고 관리자가 정해진 시간표로 그린다(모든 화면이 같은 모양 — 수평 속도는 폭격기 그대로, 높이는 등가속으로 BombFallTime 뒤 지점). 출현 → 1,500m 날아 투하(약 6.8초) → 12초 낙하. 피해는 `ACO_Ordnance.ApplySplash`와 같은 규칙(관리자 오브젝트의 `EntityControl`에 호출한 기체를 넣어 격추 점수가 호출한 사람).
- **보급(완료)**: 오너 화면이 연료·체력을 최대로 + SAV `SFEXT_G_ReFuel` 한 번(저연료 해제 알림 한 번) + 모든 화면이 `SFEXT_G_ReArm`을 `RearmRepeats`(12)번 — SAV 보급 이벤트를 연달아 부르면 오너가 매번 네트워크 이벤트를 보내서 나눴다.
- **드론 자리**: 포드 출구 = 프로브 끝 + (위 0.15, 앞 0.95) × 호스 길이(기체 기준). 드로그는 프로브 앞 대기 자리까지 풀리고, 연결하면 1.5초에 프로브로 들어가 꽂힌다(바뀜 — 예전엔 늘어진 드로그가 프로브 근처 아래에 왔다). 이탈 판정은 SAV `VertGs`·`AfterburnerOn`·기체 자세.
- **Setup Check**: 플레이어 기체 "SUPPORT 다이얼"(`ACO_PrefabBuilder.InstallSupportSlots` — 프리팹 에셋에, `Dial_Functions_R[3]`이 비어 있을 때만, 키 4는 `BindDialKey`), "지원 관리자"(`EnsureSupportManager` — 풀 4세트·드론 4대, 드론 프리팹은 없을 때만 만든다). 격납고 프리팹 판 5(SUPPORT 줄).
- **Bake**(`BakeSupport`): 다이얼 번호(`SupportIndex` = 관리자 `Supports` 순서 — 네트워크 이벤트가 이 번호로 기체를 가리킨다)·진행도·관리자·무전 연결, 빈 무전 음성 칸을 `Audio/AWACSVoiceLines/Support/`로, 상점 `CallIcons`·격납고 `SupportIcons`. 비행 HUD `Supports`.
- **무전 음성**: `Tools/aco_voice_lines.json`의 `support_*` 7줄(AWACS, eleven_v4).

### 6-143. 지원 호출 — 가디언 드론
- **개요**: 세 번째 지원 호출(6-142 구조 그대로 — 상점 SUPPORT 탭에서 호출권을 사고, 격납고 SUPPORT 줄에서 고르고, SUPPORT 다이얼로 부른다). 요격 드론 **한 대**가 내 기체 **옆 살짝 앞**(조종석에서 잘 보이는 자리)에 붙어 일정 시간 동안 **나를 노린 미사일**을 레이저로 떨어뜨린다. 공격(벙커버스터)·유지(급유)에 이어 방어 카드.
- **저장**: `CALL_GUARDIAN = 2`, 저장 키 `aco_call_guardian`(새 키), 고른 지원 값 3. 이름 `GUARDIAN DRONE`, 효과 `MISSILE DEFENSE 90s`, HUD 이름 `GD`.
- **값**(진행도 `CallPrices/CallCooldowns`, 관리자 `GuardianDuration`): 가격 8,000, 유지 90초, 쿨타임 150초(드론이 돌아간 뒤부터), 호출권은 부를 때 1장.
- **자리**: 진행 방향 프레임에서 옆 18m · 위 2m · 앞 12m(조종석에서 고개를 조금 돌리면 보이는 곳). **왼쪽/오른쪽은 부를 때마다 랜덤**(호출한 사람이 정해 이벤트에 싣는다 — 모든 화면이 같은 쪽). 급유 드론과 같은 편대 추종(기체 속도만큼 같이 가고 자리로 지수 보간, 뱅크는 따라 기움). 출현은 뒤쪽 위에서 따라붙고, 끝나면 위로 빠져 사라진다.
- **요격**: 함선 CIWS·LUNA UAV와 같은 방식 — 드론이 대공포(`ACO_AAGunSite`, 비행기는 노리지 않음 `TargetLayers` 0)이고 판정은 미사일이 한다(`InterceptProtectLayers` = 아군 항공기 25, 방어 반경 작게 — 호출한 기체 근처만). 한 번에 한 발, 반응 0.4초 + 격추 0.8초, 한 발 떨어뜨린 뒤 2초 쉼 — 일제 발사·연속 발사는 일부 뚫린다(회피·플레어는 여전히 필요). 바뀜(6-258): 반응 0.1 · 격추 0.45 · 쉼 0 · 사거리 1500m — 거의 미사일 면역, 레일건 탄은 못 막음. 레이저는 기총 레이저 부품(`ACO_NPCLaser`) — 아군 색(초록 계열). 폭탄·로켓은 막지 않는다(CIWS 규칙 그대로).
- **내구**: 무적·락온 불가(적이 노리지 않고 HUD 표적이 아니다). 그래서 SaccEntity·SaccTarget·표적 Animator가 없다(바뀜 — 대공포 바탕에서 뺀다, 요격은 `ACO_AAGunSite`만으로 된다. 이미 있는 프리팹은 Setup Check "가디언 드론 프리팹에 SaccEntity·SaccTarget이 남아 있음" [고치기]).
- **HUD 아군 표시**(6-142 폭격기·급유 드론과 같이): 나와 있는 동안 아군기처럼 파란 사각 테두리 + 이름(`ACO_TargetMarkers.Support` — Bake가 씬의 지원 관리자를 넣는다). 이름 `SUPPORT BOMBER`·`TANKER DRONE`·`GUARDIAN DRONE`(마커 칸 `Support*Label`). `SupportMinDistance`(150m)보다 가까우면 그리지 않는다 — 내 옆에 붙은 가디언·급유 중인 드론이 시야를 가리지 않게. 목표처럼 멀리서도 보인다(`ObjectiveMaxDistance`).
- **끝**: 시간이 다 되거나 호출한 사람이 내리거나 격추되면 돌아간다.
- **표시**: HUD 잔탄 줄 `GD x2`, 떠 있는 동안 `ON 64s`(남은 시간). MFD 둘째 줄도 같다. 부를 때·돌아갈 때 AWACS 무전.
- **요격 무전 없음**: 가디언이 떨어뜨린 미사일은 아군 CIWS 요격 대사(`EV_CIWS_SAVE` "아군 CIWS가 막음")를 내지 않는다 — 대공포에 `SilentIntercept` 표시를 두고, 미사일이 요격될 때 그 CIWS가 표시돼 있으면 무전을 건너뛴다(함선 CIWS는 그대로).
- **모델**: **프리팹 `ACO_GuardianDrone`**(바뀜 — 예전엔 풀마다 씬에서 만든 오브젝트): 요격 대공포 + `Model` 아래 자리 표시 상자(사용자가 바꾼다 — 레이저 포탑 `TurretYaw` 자리도 함께). 없을 때만 만든다. 풀 `Guardians/Guardian_0~3`은 이 프리팹의 인스턴스. 이미 있는 씬은 Setup Check "지원 관리자의 폭격기·가디언 드론이 프리팹이 아님" [고치기](`ConvertSupportPoolsToPrefabs` — 같은 자리·이름으로 바꾼다).
- **동기화**: 변수 동기화 없이 이벤트만.
  - `NetGuardianStart(support, caller, side, seconds)`(전원): 기체 번호·호출한 사람·쪽(±1)·남은 시간. 각 화면이 받은 순간부터 남은 시간을 세고, 드론 자리는 각 화면이 호출한 기체(SAV가 동기화하는 위치)를 따라 같은 편대 공식으로 그린다 — 화면마다 수십 cm 차이는 있어도 같은 자리.
  - `NetGuardianEnd(support)`(전원): 호출한 사람이 내리거나 격추되면 일찍 끝낸다(시간이 다 된 끝은 각 화면이 센다).
  - 늦게 들어온 사람: 호출한 사람이 `OnPlayerJoined`에서 그 사람에게 남은 시간으로 `NetGuardianStart`를 다시 보낸다.
  - 요격: CIWS 규칙 그대로 — 미사일이 모든 화면에서 같은 궤적으로 날고 각 화면이 요격을 계산하지만, **피해는 판정자(NPC 미사일 = 맞는 쪽 = 호출한 사람) 화면 결과만**. 드론 위치 차이로 드물게 다른 화면에서만 요격돼 보일 수 있다(연출 차이 — 피해에는 영향 없음).
  - 호출권·쿨타임은 호출한 사람의 진행도(PlayerData)만.
- **구현**: 관리자 `ACO_SupportManager`의 가디언 풀 `Guardians`(4대, `Guardians/Guardian_0~3` — `CreateGuardianDrone`: LUNA UAV와 같은 대공포, 판정 상자·락온 마커 없음, 초록 레이저, `SilentIntercept`). 자리 `GuardianOffset`(18, 2, 25 — 바뀜: 앞 12m였는데 더 앞으로) — x에 쪽(±1), 진행 방향 + 뱅크 프레임(뱅크를 다 따라 조종석 옆에 보인다). 출현 `GuardianStartOffset`(80, 120, −600)에서 급유 드론과 같은 접근(`ApproachSpeed` 등)으로 다가와 `GuardianJoinDistance`(25m) 안이면 `GuardianSharpness`(4)로 붙는다. 돌아갈 때 위·바깥으로 가속 `GuardianLeaveTime`(6초). **저속 선회**(추가): 드론은 `MinSpeedKt`(180kt)보다 느리게 날 수 없어, 기체가 그보다 느리면(착륙·이륙 직후·저속) 옆 자리를 떠나 기체 위 `GuardianOrbitHeight`(300m)에서 반경 `GuardianOrbitRadius`(600m)로 최저 속도 선회하며 엄호한다 — 선회 각도는 서버 시각이라 모든 화면이 같은 자리, 도는 방향은 붙는 쪽, 원심 가속만큼 기운다. 선회 중엔 방어 반경을 `GuardianOrbitDefense`(900m)로 넓혀 머리 위에서도 내 기체를 노리는 미사일을 요격한다(옆 자리로 오면 원래 60m). 기체가 `MinSpeedKt × GuardianRejoinFactor`(1.1 — 약 198kt)를 넘으면 지금 속도를 이어받아 옆 자리로 다시 붙는다. 시간(90초)은 그대로 흐르고, 선회 중에 끝나면 그대로 위로 빠진다. 판정은 모든 화면이 같은 방법(위치 차로 잰 기체 속도). Bake: 대공포 요격 목록(`BakeCiws`)에 들어가 NPC 미사일이 가디언을 본다. 지원 관리자 아래 대공포는 아군이라 ECM 대상이 아니다(`IsFriendlyUnit`). 무전 `support_guardian_call/end`.

### 6-154. 내 기체 피격음 — 금속판에 총알 4종
- SaccFlight 기본 `Hit.wav` 하나 대신 ElevenLabs 효과음(`Tools/aco_sfx_gen.py` — 얇은 금속판에 맞는 짧고 밝은 깡 + 작은 덜그럭, 조종석에서 듣는 느낌) 4개(`Audio/SFX/ACO_BulletHit_0~3`, 같은 설명으로 네 번 뽑은 변형). `SAV_SoundController.BulletHit`에 4개를 넣으면 SAV가 맞을 때마다 하나를 무작위로, 음높이 0.8~1.2배로 튼다.
- 설치: Setup Check `bullet-hit-sound-1` — 기체 프리팹(Pit-bool)의 첫 피격음 오브젝트를 본떠 3개를 복제하고 클립을 넣는다. 씬 인스턴스가 BulletHit 배열을 덮어쓰면 경고(Revert).

### 6-179. 하중 · 경고음 정리와 기동성 강화 하이알파 해금
- **G 손상 끔**: 플레이어 기체 SAV `GDamage` 0(몇 G든 손상 · 파괴 없음), `MaxGs` 52(이제 강화 하중 상한 계산에만 — `MaxGs − 2` = 50). 최대 50G는 리미터가 막는다. 리미터는 조종석에서 끌 수 없다(`DFUNC_Limits`가 다이얼 · 키보드에 없다 — 늘 켜짐).
- **G 리미터**: 시작 = 한계 − 10G(30 → 40G). 예전 5G 폭(35 → 40)은 고속에서 입력이 0 ↔ 100%를 오가며 걸렸다 풀렸다 했다. 기동성 강화 하중 보너스 +10G(`LimiterGBonus`) — 최대 단계 40 → 50G. 밀기(-1G)는 그대로.
- **경고음**: 조종석 실속음(`StallTone`) · 받음각 삐(`AoAWarning`) · G 손상음(`GDamageWarning`)은 AudioSource를 꺼 둔다. G 경고음은 ±30G를 넘으면 끊김 없이(예전엔 20 ~ 40G 경고음 / 40G부터 손상음이라 40G 근처에서 둘이 번갈아 났다). 원본 클립(`Pit-bool/FA-18/Animations/FA-18F_Gs.anim` — 다른 FA-18 프리팹도 쓴다)은 두고 복사본 `Generated/ACO_FA18_Gs.anim`을 오버라이드 컨트롤러 `Generated/ACO_FA18.overrideController`로 이 기체에만. STALL 음성 경고(`ACO_VoiceWarning`, 실속각의 90%)는 그대로 — 하이알파 중에도 PSM 경고로 울린다. HUD는 ACO 자체 HUD라 상관없다.
- **하이알파 해금**(`ACO_VehicleUpgrades` — 칸 배열 `HighAlphaLevels` · `HighAlphaAoALimit` · `HighAlphaStall` · `HighAlphaControl` · `HighAlpha{Pitch,Yaw,Roll}Tvc`, 그 단계 이상이면 그 칸, 내리면 되돌린다):

  | 단계 | 리미터 받음각 | SAV 실속각 | 실속 뒤 피치 조종력 | TVC 피치 / 요 / 롤 |
  |---|---|---|---|---|
  | 1 ~ 7 | 지금처럼(+5° × t, 실속각 80%까지) | 25° | 0.2 | 0 / 0 / 0 |
  | 8 | 40° | 30° | 0.4 | 0.3 / 0 / 0 |
  | 9 | 55° | 33° | 0.5 | 0.45 / 0.2 / 0.15 |
  | 10 | 제한 끔(`HighAlphaAoALimit` 0 — 하중 제한은 그대로) | 35° | 0.6 | 0.6 / 0.4 / 0.3 |

  리미터 한계가 실속각보다 커서 8단계부터 실속 영역까지 당길 수 있다(양력 · 조종력이 줄어 속도가 떨어진다 — PSM의 대가, STALL 음성은 실속각 90%부터). 실속각을 리미터만큼 올리지 않는 건 고받음각에서도 속도가 안 떨어지는 "공짜 선회"를 막으려고. 롤 TVC는 세운 채 몸을 굴려 방향을 트는 기동용(롤은 리미터에 안 걸리고 기동성 강화로 이미 세지므로 낮게).
  SAV TVC(`PitchThrustVecMulti` 등)는 추력 크기가 아니라 엔진이 켜져 있으면 속도 · 받음각과 상관없이 남는 최소 조종력이다. SAV는 엔진을 켤 때 TVC를 `*Start` 값으로 되돌리므로 그 값도 같이 바꾼다(`SetProgramVariable`). 수치는 출발점 — 비행해 보며 맞춘다.
- 강화 설명(`prog.updesc.mobility`, 한 · 영 · 일)에 "후반 단계에서 PSM이 가능해진다"를 두루뭉술하게 적었다(정확한 단계 · 각도는 쓰지 않는다).
- **상세 칸**(격납고 · 브리핑실 강화 탭, 기동성 줄): `NOW +21% > NEXT +24%` 아래에 `받음각 한계 17.5° > 40°`, `최대 하중 47G > 48G` 두 줄(최대 단계면 지금 값만, 10단계는 `제한 없음 (PSM)`). 받음각 한계가 크게 뛰는 단계(하이알파 · 해제)는 다음 값을 노랑으로. 값은 상점에 적어 두지 않고 기체 강화 스크립트(`ACO_VehicleUpgrades.GetAoALimitAt` · `GetGLimitAt` — 적용과 같은 계산)로 계산한다 — Bake가 `ACO_Shop.MobilityPreview`에 씬의 첫 플레이어 기체를 넣는다. 번역 키 `shop.detail.aoa` · `aoa1` · `glimit` · `glimit1` · `aoaoff`.
- Setup Check `g-limit-psm-1`이 기체 프리팹 값 · 소리 · 오버라이드 컨트롤러를 넣는다(씬 인스턴스 덮어쓰기는 지운다).

### 6-189. 기체 프리팹의 non-convex MeshCollider 정리
- **문제**: 플레이 모드마다 "Non-convex MeshCollider with non-kinematic Rigidbody" 오류(`ACO_Marks/Marks/Kills_R`). FA-18E 프리팹에 히트박스(동체 · 날개 convex 9개, 레이어 17) 말고 non-convex MeshCollider가 37개 있었다 — 특수무장 외형(`WeaponVis` 파일런 · 포드) 32, 기체 표시(`ACO_Marks` 이름 · 선체번호 · 킬마크) 5. 데칼 스튜디오 미리보기 외형(데칼 건 조준용 콜라이더가 붙어 있다)을 복사해 넣으면서 따라 들어온 것.
- **영향**: 내 기체(오너, non-kinematic)에선 Unity가 무시하고 오류만 남긴다. 남의 기체(SAV가 kinematic으로 둔다)에선 실제 충돌체가 되어 화면마다 기체 충돌 모양이 달랐다(대부분 레이어 0 — 레이어 0을 보는 레이캐스트에 걸릴 수 있었다).
- **처리**: 지웠다(히트박스는 그대로). 다시 복사해 넣어도 Setup Check가 경고하고 [고치기]가 지운다(`ACO_PrefabTuning.PlayerAircraftNonConvexColliders` · `RemovePlayerAircraftNonConvexColliders` — 씬 플레이어 기체의 프리팹에서 Rigidbody 아래 non-convex MeshCollider). 데칼 스튜디오 샘플(`PreviewRoot`)의 콜라이더는 그대로.
- 같이 확인한 것: 플레이 시작 때 한 번 나는 `IsNetworkingSupported cannot be changed` 예외는 ClientSim 자기 오브젝트(`InputController`)가 SDK의 씬 초기화보다 먼저 초기화돼서 난다 — 씬 UdonBehaviour는 모두 네트워크 설정이 켜진 뒤라(2,750개 확인) 월드에는 영향이 없고 VRChat에선 나지 않는다. 고치지 않는다.

### 6-190. 플랩 헐넘버
- 실기(호넷)처럼 뒷전 플랩 윗면에 헐넘버를 쓴다 — 플랩을 내리면 뒤 · 옆에서 보이고, 올리면 위에서 보인다. 동체 헐넘버(6-87)와 같은 글자(PlayerData `aco_hull_no`, 최대 6자), 같은 폰트 · 재질(ACO/MarkText), 같은 표시 규칙(탑승자가 있을 때만, 1500~1800m에서 사라짐). 동기화 없음.
- **자리**: FA-18 스킨 메시의 플랩 본(`Armature/F-18/Flap.L` · `Flap.R`) 아래 `ACO_FlapHull` — 본과 함께 움직인다(플랩 메시 정점이 이 본에 묶여 있다). 본 좌표 x = 윗면(오른쪽 본은 반대), y = 바깥쪽, z = 앞(경첩). 판 2.3 × 0.7m, 바깥 끝이 본에서 2.0m(플랩 끝 2.15~2.3m), 가운데가 경첩 뒤 0.6m, 윗면 위 0.11m.
- **방향 · 맞춤**: 글자는 윗면에 눕고 위쪽이 경첩(앞)을 본다 — 뒤에서 바로 읽힌다. 바깥쪽 맞춤: 뒤에서 볼 때 왼쪽 플랩은 왼쪽 정렬, 오른쪽 플랩은 오른쪽 정렬(사용자 요청 — 실기 사진처럼). 글자 크기 5, 길면 3까지 줄어든다(6자 "VFA-41"도 들어간다).
- **코드**: `ACO_VehicleMarks.FlapTexts`(헐넘버를 같이 쓰고, 글자 묶음 `Root` 밖이라 켜고 끄기를 따로). 생성 `ACO_HUDBuilder.EnsureFlapMarks`(새 기체 `AddVehicleMarks`, 있는 기체 `EnsureVehicleMarks`, 격납고 샘플 `EnsurePreviewMarks` — 샘플은 프리팹 판의 자리 · 크기를 복사한다), Setup Check `flap-hull-1`. 플랩 본이 없는 기체는 건너뛴다.
- 자리를 옮기려면 기체 프리팹의 `ACO_FlapHull`을 옮긴다 — Bake가 격납고 샘플에 따라 복사한다.

### 6-192. 헐넘버 곡면 맞춤 (VR)
- **문제**: 헐넘버 글자는 평평한 TMP 판이라 둥근 기수 · 기운 플랩 위에서 떠 있거나 묻혔다(기수 최대 6cm 뜸, 플랩 8cm 뜸 · 9cm 묻힘). 카메라 쪽 당김(MarkText)은 시선 방향으로만 움직여 양 눈 시차가 그대로라 VR에서도 제자리에 보이지만, 판 자체가 표면과 떨어진 거리는 VR 시차로 드러난다. 라운델(표면에 맞춰 구운 판) · 콜사인은 1cm 아래라 그대로.
- **처리**: 표면 메시를 복제해 글자를 얹는 대신(글자가 조종사마다 바뀌어 실행 중 글자 메시를 따로 만들어야 한다), 글자 셰이더가 정점을 표면 모양으로 휜다. 편집기(`ACO_MarkSurfaceFit`)가 판 아래 기체 표면(쉬는 자세 스킨 메시에 CPU 광선, 가장 긴 6자 글자 자리 + 5%, 11 × 7점)을 재서 판 좌표 3차 곡면 z = f(x, y)로 맞추고, 판마다 머티리얼(`Generated/Marks/ACO_MarkText_Fit_Hull_L/R · FlapHull_L/R`)의 `_BendA · _BendB · _BendC`에 넣는다(표면 위 4mm). `ACO/MarkText`가 오브젝트 공간에서 휘므로 배칭을 끈다(`DisableBatching`). 계수 0이면 평판 그대로(콜사인 · 킬마크).
- **결과(맞춘 뒤 최대 어긋남)**: 플랩 0.5cm(본에 붙어 있어 플랩을 내려도 그대로), 기수 1.8 ~ 1.9cm(6자일 때 첫 글자 위 모서리 — 기수가 좁아지며 말려 들어가는 곳, 3자는 더 작다). 격납고 샘플도 같은 머티리얼.
- **설치**: Setup Check `mark-fit-1`, 판을 옮기거나 새로 만들면 Setup Check 경고 [고치기] · 메뉴 `ACO/Fit Marks To Surface`로 다시 맞춘다.

### 6-194. 자유 비행 빈 기체에 마지막 조종사 표시 · 특수무장 장착 알림
- **배경**: 자유 비행은 내려도 사양을 남긴다(데칼 · 라운델 · 소모품 · 특수무장 · 강화). 그런데 헐넘버 · 콜사인 · 킬마크 · 플랩 헐넘버만 탑승자가 있을 때만 보여 반쯤 남은 기체가 됐다.
- **표시 유지**: `ACO_VehicleDecal`이 앉을 때 "조종사 사양 있음"(플래그 64)과 조종사 번호(`_pilotId`, 동기화)를 보낸다 — 데칼이 없어도. 미션에서 내리거나 폭발 · 리스폰 · 회수되면 지운다(데칼과 같은 때). `ACO_VehicleMarks`는 탑승자가 없어도 이 값이 있으면 그 사람의 PlayerData(헐넘버 · 콜사인 · 최고 격추수)로 계속 보이고, **그 사람이 월드를 나갔으면 숨긴다**(소유권이 다른 사람에게 넘어가 엉뚱한 이름이 보이지 않게 — 데칼 · 라운델은 남긴다, 사용자 결정). 격추(dead) 중엔 숨김. 표시 → 데칼 연결은 생성 코드(`LinkMarksDecal`) · Setup Check `marks-keep-1`.
- **특수무장 장착 알림**: 조종사가 앉을 때 무장이 바뀌었을 때만 모두에게 알려, 빈 기체일 때 들어온 사람은 같은 무장으로 다시 타도 끝내 틀린 외형을 봤다(미션에서도). 이제 앉을 때마다 늘 알린다(`ACO_DFUNC_Special.SFEXT_O_PilotEnter`).
- 하지 않은 것: 빈 기체 상태에서 늦게 들어온 사람에게 소모품 · 특수무장 외형을 다시 보내기 — 누가 다시 타면 맞춰지므로(사용자 결정).

### 6-195. FA-18E 캐노피 앞 프레임 가늘게
- 조종석 앞을 가로지르는 띠 = 캐노피 앞 프레임(`Canopy.E`, 폭 5~7cm, 거울 5개) + 동체 앞유리 아치(`Main.E`, 약 8cm)가 맞닿아 약 15cm로 보였다(사용자 요청: 조금 얇게).
- `ACO_CanopyThin`: 두 띠의 이음선(캐노피 앞끝 = 아치 뒤끝, 높이별 z) 쪽으로 기수 방향 거리를 모은다 — 이음선 뒤(캐노피 앞 프레임) ×0.65, 이음선 앞(앞유리 아치) ×0.45(사용자 요청 "앞아치도" → 0.3까지 했다가 "너무 얇다" → ×1.5 = 0.45). 띠 반폭 8.5cm 안은 그대로 모으고 바깥 10cm에서 0으로, 아치 다리 아래 문턱(2.62 → 2.72m)에서 0으로. 붙어 있는 캐노피 유리 앞끝 · 앞유리 뒤끝도 같은 변위라 틈이 없다(유리는 조금 늘어남). 거울은 프레임과 함께 옮겨진다. 옮긴 정점: Canopy.E 930(거울 포함) · Main.E 266, 최대 약 6cm.
- **안쪽 깊이 줄이기**(사용자 요청 "유리와 연결부가 매끄럽게"): 띠의 바깥면(유리와 맞닿는 면)은 그대로, 캐노피 단면 중심(x 0, y 2.70)에서 잰 안쪽 깊이를 ×0.4(`DepthKeep`) — 안쪽 면이 유리 쪽으로 올라가 단차가 줄어든다(아치 3~4cm · 캐노피 앞 프레임 6~7cm → 약 40%). 바깥 모습은 그대로. 유리는 건드리지 않고, 거울(작은 덩어리)은 붙은 자리 변위로 통째로 옮겨 납작해지지 않는다.
- **높이 올리기**(사용자 요청 — 실기 조종석처럼 앞이 높게 열려 보이게): 문턱(2.78m) 위로 갈수록 비례해 올려 꼭대기 +5cm(`Lift`). 앞뒤는 아치 부근(z 4.2 ~ 5.2)만 다 올리고 캐노피 뒤(3.2) · 기수 쪽(6.0)으로 갈수록 0, 옆은 |x| 0.5 → 0.7에서 0 — 캐노피 · 앞유리가 꺾이지 않고 이어진다(옆모습은 아치 부근이 살짝 높아짐).
- **옆모습 이음부 매끄럽게**(사용자 요청 "이부분이 매끄럽게 안이어지잖아" — 원본 모델부터 있던 꺾임): ① 앞유리 쪽 올리기 — 앞유리 뒤끝이 캐노피 유리 앞끝보다 낮아 윤곽이 계단처럼 꺾였다. 이음선에서 아치 앞끝(+8cm)까지 0 → 3.5cm(`StepFix`)로 올리고 앞유리 앞쪽(z 5.9)으로 갈수록 0(문턱 위로 갈수록 비례) — 꼭대기에서 앞유리 · 캐노피 유리 선이 한 직선(차이 1mm 안). ② 이음선 홈 메우기 — 캐노피 앞 프레임과 아치가 맞닿는 줄이 양쪽 테두리보다 꼭대기에서 1.6cm 파여 V자로 보였다. 두 메시에서 같은 자리 정점으로 이음선을 찾아(각도별 z, 아치 부근만) 그 줄을 1.6cm(`NotchDepth`) 바깥으로 밀고 뒤 테두리(2.1cm) · 앞 테두리(아치 폭 = 원래 8cm × `FactorFront`, 3.6cm)까지 직선으로 0 — 옆으로 35°에서 0. 띠 안쪽 면 · 밑에 깔린 유리 끝도 같은 거리로 따라가 두께 · 틈 그대로.
- **아치 안쪽 깊이 맞추기**(사용자 요청 — 조종석에서 보면 아치 안쪽이 캐노피 프레임보다 깊어(25° ~ 45°에서 2 ~ 2.5cm) 그 턱의 옆면이 어두운 띠로 보였다): 각도마다 이음선 뒤 2.5cm 안의 캐노피 앞 프레임(거울 같은 작은 덩어리 제외) 면을 그 각도 평면으로 잘라 가장 안쪽 거리를 재고(정점이 각도마다 드문드문이라 칸으로 재면 들쭉날쭉해 면으로), 그보다 깊은 아치(Main.E, 이음선 앞 4cm 띠, 바깥면에서 10cm 안) 정점만 그 거리까지 바깥으로 올린다(`ArchMatch`) — 닫았을 때 두 조각 안쪽 면이 같은 높이로 이어진다. 바깥면 · 유리 · 옆모습 선은 그대로, 아치가 이미 얕은 곳(꼭대기 · 65° 아래 다리)은 그대로, 문턱 쪽은 띠 가중치로 0.
- **아치 안쪽 볼트**(사용자 요청 — 실기 조종석처럼 앞유리 아치 안쪽에 볼트가 줄지어 박힌 디테일, 색은 프레임과 같게): 아치(Main.E) 복사본에 볼트 정점 · 면을 더한다(`Bolts`). 각도마다 아치를 잘라 안쪽 면 가운데 선을 찾고(0.5° 간격, 정점 줄과 겹쳐 못 찾는 칸은 3°까지 건너뜀), 꼭대기 가운데는 꺾인 면이라 비우고 양쪽 반 간격부터 문턱 위 y 2.86m까지 고른 간격으로 박는다. 간격은 6cm(`BoltSpacing`) 근처 ±1cm에서 모든 볼트가 평평한 자리에 앉는 값을 고른다(`PlaceSide` — 현재 6.2cm, 한쪽 11개 · 모두 22개). 볼트마다 가운데 아래 면을 찾아 축을 그 면에 수직으로 잡고(단면 방향과 35° 안), 그 축으로 와셔 테두리 8곳 아래 높이 차가 1mm(`SeatSpread`) 안일 때만 가장 높은 곳에 앉힌다 — 안 되면 선을 따라 ±5mm, 옆으로 ±3 · 6mm 옮겨 본다. 사용자 지적으로 고친 것: "붕 떠 있다"(좁은 면 조각을 안쪽 면으로 잡아 2cm 뜬 볼트), "혼자만 따로 떠 보인다"(아치가 꺾이는 부품 이음매에서 접힌 선에 걸쳐 기울어 앉음), "저기만 간격이 많이 떠서 이상하다"(꺾이는 구간 6.5cm는 면이 단면 방향에서 17 ~ 22° 기울고 와셔보다 좁아 건너뛰었다 → 축을 면 방향으로, 옆으로도 옮겨 앉힘). 볼트 = 8각 와셔(지름 1.6cm, 면 속 2mm 묻힘) + 육각 머리(지름 1.2cm, 높이 3.6mm, 윗모서리 깎음). 안 보이는 밑면 없음, 면마다 노멀 따로. UV · 본 가중치는 바로 옆 아치 정점 것 — 새 오브젝트 · 머티리얼 · 드로우콜 없음(정점 약 2,100개 추가). 메시 저장은 정점 · 면을 직접 옮겨 담는다(`CopySerialized`는 정점 수가 바뀐 메시를 옮기지 못했다). 되돌리기 메뉴로 원본에 돌리면 볼트도 없어진다.
- **미리보기 모델**(사용자 요청): 격납고 미리보기 기체(`PreviewRoot` 아래 — 프리팹이 아니라 씬에 직접 놓인 FA-18)에도 같은 복사본 메시를 끼운다(같은 FBX 리그라 메시만 바꾸면 된다). 적용 · 되돌리기 메뉴가 같이 바꾸고, Setup Check `canopy-thin-preview-1`. 격납고 소품 기체(FA-18F (Prop) 3대)는 원본 그대로.
- 원본 FBX(`Pit-bool/FA-18/FBX/FA-18E.fbx`)는 그대로, 복사본(`Generated/Canopy/*_ACOThin.asset`)을 기체 프리팹 스킨 메시에 끼운다 — 본 · 가중치 · 노멀 그대로(캐노피 여닫기 그대로). Setup Check `canopy-thin-1`, 되돌리기 메뉴 `ACO/FA-18E Canopy Frame/Restore Original`. 배율은 `ACO_CanopyThin.Factor` · `FactorFront`.
- **바뀜(2026-11-14, Setup Check `canopy-thin-2`)**: 사용자 지적 "아치 너트쪽 검은부분이 HUD를 가려", "앞유리가 안맞아서 떨어져보여". ① 볼트 옆면이 거의 검어(옆을 보는 면이라 조종석 조명을 못 받음) 반투명 HUD 글자(Overlay 셰이더 — 렌더 순서로는 가려지지 않는다) 뒤로 진한 육각 테두리가 비쳐 글자를 덮은 것처럼 보였다 → 볼트의 모든 정점 노멀을 그 자리 아치 면 방향으로(아치와 같은 밝기, 감김 확인은 따로 둔 실제 면 방향). ② 띠를 얇게 · 얕게 하며 프레임 안쪽 면을 바깥으로 올렸는데 프레임 속에 묻혀 있던 유리 끝(submesh 1)은 그대로라 아치 안쪽 가장자리를 따라 옅은 띠로 드러났다 → `TuckGlass`: 단면 중심에서 유리 정점 쪽 광선이 처음 닿는 프레임 면(거울 · 볼트 제외)보다 유리가 안쪽이면 그 면 바로 바깥(1.5mm)으로 — Main.E 유리 정점 21개. 렌더(조종석 눈 위치)로 옅은 띠가 사라진 것과 볼트 테두리가 HUD 글자 뒤로 비치지 않는 것을 확인.
- **바뀜(같은 날, `canopy-thin-3`)**: 사용자 "오른쪽에는 볼트가 박히지가 않았는데.. 양쪽 대칭으로", "검은색말고 약간 짙은회색으로 음영 표시". 오른쪽 볼트는 있었지만 아치 텍스처 색(검정)을 빌려 묻혀 보였다 → 볼트를 submesh 2 · 전용 재질 `Generated/Canopy/ACO_CanopyBolt.mat`(짙은 회색 0.32, 금속 0.35, 매끈함 0.45)로, 음영 노멀 = 아치 면 방향 + 실제 면 방향 × 0.7(옆면이 조금 어둡게 — 입체감), 자리는 +x 쪽만 찾고 반대쪽은 x를 뒤집어 그대로(대칭, 한쪽 11개). 기체 재질 칸은 메시 submesh 수에 맞춰 셋째 칸을 붙인다(`FitMaterials` — 원본으로 되돌리면 두 칸).

### 6-196. FA-18E 조종석 UFC(스크래치패드) · 연료계 화면 다시 그리기
- 사용자 요청("스크래치패드와 연료계 텍스쳐 깔끔해 보이게", "둥근 사각 말고 각진 사각 테두리"): 원본 조종석 텍스처(`Pit-bool/FA-18/material/cockpit/cockpit_Base_color.png` · `cockpit_Emissive.png`, 4096)에 손으로 그린 그림이라 키 칸이 비뚤고 선이 굵거나 울퉁불퉁했다.
- `ACO_CockpitTexture`(메뉴 `ACO/FA-18E Cockpit Displays/Redraw`, 되돌리기 `Restore Original`): 원본은 그대로, 복사본(`Generated/Cockpit/cockpit_Base_color_ACO.png` · `cockpit_Emissive_ACO.png`)에 두 영역만 새로 그리고 머티리얼 복사본 `cockpit_ACO.mat`을 기체 프리팹(`FA-18E Main_ACO`)의 `cockpit.E`에만 끼운다(원본 머티리얼은 원래 FA-18E/F 프리팹 · 테스트 씬도 씀). 금속 · 매끄러움 맵(원본 발광 텍스처를 같이 씀)은 원본 그대로. 복사본 텍스처는 비등방 필터 4(비스듬히 볼 때 작은 글자 덜 뭉개짐), 크기 · 압축은 원본과 같게.
- 그리기: 도형(각진 사각 테두리 · 선 · 삼각형)은 4배 슈퍼샘플 마스크에, 글자는 TMP(Pretendard)를 임시 카메라로 렌더해 합치고 내려 뽑는다. 두 영역은 텍스처 안에서 90° 돌아가 있어 같은 방향으로 붙인다. 판 · 화면 위치는 원본 그림에서 찾는다(녹색 판 / 검은 화면 픽셀 상자).
- **UFC**: 배치 · 글자는 원본 그대로(숫자 3×4 — 1 2 3 / 4 5 6 / 7 8 9 / CLR 0 ENT, 옵션 2×5 — CPL T/R · CH12 122.500, BALT · ROLL, RALT · GTRK, FPAH · HDG, CNI · CH 6). 반듯한 격자에 같은 크기 각진 사각 테두리(가는 선), 글자 크기 통일(숫자 / CLR · ENT / 옵션 / 두 줄 옵션). 배경 어두운 녹색, 빛나는 건 테두리 · 글자만. UFC 면은 UV가 사다리꼴(원본 그림이 비뚤었던 이유)이라 픽셀마다 면 삼각형으로 실제 자리(기체 좌표, 14.3 × 10.6cm)를 구해 3D에서 반듯하게 그린다.
- **연료계(RPM · FUEL)**: 움직이는 RPM · 연료 막대(SAV 메시)가 겹치므로 선 자리는 원래 그대로 — 가로 구분선 · 세로 구분선(RPM | FUEL) · BINGO 상자(+안쪽 막대)를 가늘고 반듯하게, 글자(B▶, BINGO 4.5, INT 7.2, RPM, L, R, FUEL) 한 글꼴로 줄 맞춤. 화면 둥근 모서리 바깥(회색)은 그대로.
- Setup Check `cockpit-display-1`.

### 6-216. 갑판 택싱 10노트 (`ACO_VehicleAutomation.DeckTaxiSpeed` 5.14, Setup Check `deck-taxi-10kt-1`)
- 사용자 요청("항모 택싱속도 10노트로 바꿔주고"). 6-83의 갑판 택싱 제한 2.6m/s(약 5노트) → 5.14m/s(10노트). 스크립트 기본값 · 튜닝 상수(`ACO_PrefabTuning.DeckTaxiSpeed`)도 같이. 플레이어 기체 프리팹(FA-18E Main_ACO)에 넣었고 씬 기체 4대가 따라온다(덮어쓴 값 없음).

### 6-229. 갑판 점검요원 걸음 — 디딤 순서 · 템포 · 돌고 나서 걷기 (`ACO_CatapultCrew`, `ACO_CrewBuilder` 클립 판 9)
- 사용자 요청("데크크루들 움직임이 어색한데 네가 보고 한번 고쳐줘봐").
- **디딤 순서**: 걷기 클립의 지나가는 자세(2 · 4번째 키)가 거꾸로였다 — 왼발을 앞에 디딘 뒤 그 왼발로 서야 하는데 오른발로 서고 왼발이 흔들려, 디딘 발이 미끄러졌다. `WalkPose(true,false)` · `WalkPose(false,false)` 순서로 바로잡았다(플레이에서 발 뼈를 재 확인 — 디딘 발은 낮게 뒤로, 흔드는 발은 들려 앞으로).
- **템포**: 몸은 `CheckWalkSpeed` 3.2m/s로 가는데 클립의 디딘 발은 약 1.75m/s로 뒤로 가 발이 미끄러졌다. 빠르기를 2.2m/s로 내리고(Setup Check `crew-walk-1`), 걸음을 두 걸음 0.68 → 0.54초로 빨리 해 디딘 발도 약 2.2m/s(날개 밑을 숙여 바삐 걷는 느낌).
- **덜 뻣뻣하게**(사용자: "모션이 뒷걸음질 치는것같고, 전체적으로 뻣뻣해" — 뒷걸음 느낌은 위 디딤 순서 탓): 숙임 38 → 26도(무릎은 굽힌 채 — 날개 밑을 지난다), 두 팔을 앞으로 내민 채 굳어 있던 것을 다리와 엇갈려 흔든다(앞으로 간 팔은 팔꿈치를 더 굽힌다 — 손이 약 0.3m 오간다), 디딜 때 가슴을 8도 같이 비튼다(`CrewPose.Twist`). 클립 판 9.
- **돌고 나서 걷기**: 경유점마다 방향이 크게 바뀌는데 돌면서 제 빠르기로 움직여 옆 · 뒤로 미끄러졌다. 가야 할 쪽에서 25도 안이면 제 빠르기, 80도 넘게 틀어져 있으면 제자리에서 돌기만 하고, 그 사이는 비례해 줄인다(`Walk`).

### 6-230. 갑판 요원 앉기 · 슈터 신호를 덜 딱딱하게 (`ACO_CrewBuilder` 클립 판 10)
- 사용자 요청("앉을때랑 슈터의 움직임도 너무딱딱해").
- **전환 시간**: 모든 상태로 0.2초였다 — 서 있다 무릎을 꿇으면 툭 떨어졌다. 꿇기 · 앉기 · 엄지 0.45초, 대기로 돌아갈 때 0.4초, 발함 신호 0.3초, 걷기 0.15초, 나머지 0.25초(`CrewTransition`). 컨트롤러에 이미 있는 전환도 시간만 맞춘다(클립을 바꿔 끼운 것은 그대로).
- **앉아 있기**(유도요원 Sit): 굳은 한 자세 → 5.2초 주기로 숨쉬듯 들썩이며 고개와 가슴을 좌우로 천천히 돌려 둘러본다.
- **슈터 스로틀 업**: 오른팔만 돌던 것 → 몸이 원을 따라 숙이고 비틀며, 왼팔은 기체 쪽으로 반쯤 든다. **지적확인**: 가리킬 때 몸 · 고개가 같이 끄덕이고 가슴이 가리키는 쪽으로 조금 돈다.
- **발함 신호**: 서 있다 바로 꿇던 것 → 반쯤 숙인 자세(0.3초)를 거쳐 꿇고(0.65초), 고개 ±38도 · 가슴 ±12도로 크게 살핀 뒤, 팔을 뒤로 당겼다(2.0초) 뻗으며 앞으로 깊이 숙였다가(24도) 조금 돌아와(16도) 버틴다. 클립 3.0초(사출 신호 SignalTime 2초 뒤 사출 — 숙이는 동안 사출된다).
- **엄지**: 올린 채 굳던 것 → 살짝 더 올렸다 돌아오며 유지.

### 6-231. 슈터 발함 신호 = 런지 (`ACO_CrewBuilder.LungePose`, 클립 판 11)
- 사용자 요청(실제 슈터 사진 3장 — "자세를 이런느낌으로 좀더 역동적이게"). 6-230의 무릎 꿇기 신호를 바꾼다.
- 순서(3.0초, 사출은 신호 2초 뒤): 서서 고개 ±42도 · 가슴 ±14도로 앞뒤를 살핌(0.35 · 0.75초) → 앞 보기(1.05) → 런지로 낮게 내려가 오른손으로 앞 갑판을 짚음(1.45, 숙임 50도) → 오른팔을 사출 방향으로 곧게 뻗으며 쏠림(1.8, 36도) → 버팀(2.15~3.0, 28도, 고개를 들어 기체를 본다).
- 런지: 앞(오른)다리 허벅지를 거의 수평으로 굽히고 정강이는 세운다, 뒷(왼)다리는 뒤로 길게 뻗는다. 바뀜(6-233 — 사용자: "런지 더 깊게 거의 완전히 앉듯이"): 앞 허벅지를 수평보다 조금 아래까지, 무릎이 발보다 앞으로 나오게, 뒷다리는 더 낮게 길게 — 엉덩이가 거의 갑판 가까이 내려간다. 왼손은 앞 무릎 위, 가슴은 오른 어깨가 앞으로 12도 비튼다.

### 6-232. 점검요원 버티기 · 유도요원 쪼그려 앉기 — 후폭풍 대비 (`BracePose` · `SquatPose`, 클립 판 12)
- 사용자 요청(실제 갑판 사진 4장 — "다른 크루도 자연스럽게 … 이런느낌으로 앉거나 후폭풍에 대비하게").
- **점검요원 엄지**(점검 뒤 제자리에서 사출까지): 무릎 꿇고 팔을 50도 드는 자세 → 오른다리를 깊게 굽히고 왼다리를 옆뒤로 길게 뻗어 낮게 버티며, 왼손은 갑판을 짚고 오른팔은 곧게 위로 뻗어 엄지를 든다. 몸을 40도 숙이고 고개를 들어 기체를 본다. 꿇은 자세에서 0.45초에 내려가고 2초 동안 한 번 들썩인 뒤 버틴다.
- **유도요원 앉기**(준비 ~ 사출): 무릎 꿇기 → 두 발을 벌리고 낮게 쪼그려 팔뚝을 무릎에 얹고 몸을 앞으로 숙여 기체를 본다. 6-230의 숨쉬기 · 둘러보기는 그대로.

### 6-233. 런지 · 버티기를 실제로 낮게 — 아바타 다리 한계에 맞춘 다리 모양 (클립 판 21)
- 사용자 요청("런지 더 깊게 거의 완전히 앉듯이"). 다리 방향만 더 숙여서는 엉덩이가 0.75~0.9m에서 안 내려갔다 — 플레이에서 뼈 높이를 재 원인을 찾았다.
- **원인**: 갑판 요원 아바타(DCS, 기본 근육 한계)는 허벅지를 뒤로 약 36도까지만 뻗는다. 뒷다리를 곧게 길게 뻗게 하면 뒷발이 먼저 갑판에 닿아 몸 전체를 들어 올렸다. 골반을 앞으로 기울이면(`CrewPose.HipPitch` — 남겨 둠, 지금은 0) 앞 허벅지 굽힘 한계를 먼저 써 버려 오히려 높아졌다. 아바타 한계를 늘리는 것은 산 모델(EEI)의 아바타 설정을 바꾸고 모든 동작 값이 함께 변해 하지 않았다.
- **슈터 런지**(`LungePose`): 앞다리는 쪼그려 앉기처럼 — 허벅지를 조금 벌려 수평에 가깝게, 무릎이 발보다 앞(정강이 방향은 무릎 → 발이라 z가 음수). 뒷허벅지는 한계까지 뒤로, 뒷정강이는 거의 수평으로 뒤로 눕혀 무릎이 낮게. 엉덩이 약 0.69m, 앞발은 갑판에.
- **점검요원 버티기**(`BracePose`): 슈터 런지를 좌우로 뒤집은 다리(왼다리 앞, 오른다리 뒤 — 참고 사진 2의 녹색 요원), 몸을 55~59도 숙여 왼손이 갑판 가까이(0.4m), 오른팔은 위로 엄지. 엉덩이 약 0.69m. 예전 판(왼다리를 옆으로 뻗기)은 다리 벌림 한계로 0.9m에서 멈췄다.
- **유도요원 쪼그려 앉기**: 엉덩이 약 0.53m(바뀌지 않음).

### 6-234. 유도요원 앉기 = 런지 (`SitLungePose`, 클립 판 22)
- 사용자 요청("유도요원도 런지하듯 앉는걸로하자"). 6-232 · 6-233의 쪼그려 앉기를 바꾼다.
- 다리는 슈터 런지와 같다(오른다리 앞 깊게, 왼다리 뒤로 길게 — 엉덩이 약 0.69m). 오른팔뚝은 앞 무릎에 얹고 왼손은 옆 갑판을 짚으며, 몸을 34도 숙이고 고개를 들어 기체를 본다. 숨쉬기 · 둘러보기(6-230)는 그대로.

### 6-235. 슈터 지적확인 — 다리는 서 있고 상체 · 고개 · 손짓만 (`ACO_CatapultCrew.PostLateUpdate`, 클립 판 23)
- 사용자 요청("슈터 지적하는것도 너무딱딱해, 자연스럽게 다리를 살짝벌리고 서서 고개랑 상체 손짓으로만 움직이는게 더 자연스럽지 않아?").
- 예전: 요원을 한 명씩 가리킬 때마다 스크립트가 몸 전체(발까지)를 그 요원 쪽으로 돌렸다 — 제자리에서 빙빙 도는 것처럼 보였다.
- 이제: 몸(루트)은 지적확인 전 바라보던 조종석 쪽 그대로. 애니메이션이 자세를 쓴 뒤(`PostLateUpdate`) 허리 30% · 가슴 40% · 고개 30%로 나눠 요원 쪽으로 비튼다(최대 `PointTwistMax` 110도, `PointTwistRate` 300도/초 — 끝나면 같은 빠르기로 정면으로). 뻗은 팔은 가슴에 달려 있어 같이 따라간다. 동기화 없음(각자 화면에서 같은 계산).
- 동작(`CrewPoint`): 다리를 어깨너비로 벌리고 무릎을 살짝 굽혀 선다, 오른팔을 굽혔다가 뻗으며 끊어 가리키고(0.16초) 몸 · 고개가 같이 끄덕인다. 0.6초(요원 한 명 PointEachTime)마다 되풀이.

### 6-242. 캐터펄트 JBD가 올라간 채로 남던 것
- 사용자 지적("캐터펄트의 JBD가 올라간 채로 보이는 경우가 있다"): JBD는 그 캐터펄트에 걸린 기체(`OnCatapult`)가 있는 동안 올라간다. 걸린 채 폭발하면 SFEXT_O_Explode(조종사 화면)만 걸림을 풀어, 다른 사람 화면에서는 계속 걸림 → JBD가 올라간 채. 사출 끝의 풀림 알림(CatapultLockOff)을 놓쳐도 같다.
- 고침:
  - `ACO_DFUNC_Catapult`: SFEXT_G_Explode · SFEXT_G_RespawnButton(모든 화면)에서 걸려 있으면 CatapultLockOff.
  - `ACO_CatapultCrew.UpdateJbd`: 사출 시작 뒤 `JbdLaunchHold`(5초)가 지나거나, 사출 전에 기체가 걸림 반경 × 2(16m)보다 멀어지면(텔레포트 · 리스폰) 알림이 없어도 내린다. 정상 사출은 그대로(알림이 먼저 와서 내려간다).

### 6-243. 특수무장이 기총과 같이 발사되던 것
- 사용자 지적("특수무장이 기총이랑 동시 발사되는 경우가 있다"): 기총(DFUNC_Gun)과 특수무장 슬롯(`ACO_DFUNC_Special`)은 같은 오른쪽 다이얼. SAV는 내릴 때 DFUNC_Deselected를 보내지 않아(다이얼 선택만 비운다), 특수무장을 선택한 채 내리면 슬롯의 `_selected`가 남았다. 격납고에서 특수무장을 바꿔 다시 타면 탑승 때 `Equip()`이 그 값을 보고 새 모듈에 DFUNC_Selected를 보내 — 다이얼은 기총인데 특수무장도 선택된 채 → 방아쇠 한 번에 둘 다.
- 고침: 내릴 때(SFEXT_O_PilotExit) 선택돼 있으면 모듈에 DFUNC_Deselected를 보내고 `_selected = false`, 탈 때(SFEXT_O_PilotEnter)도 `_selected = false`로 시작 — 선택은 다이얼이 보내는 DFUNC_Selected만 따른다.

### 6-253. JBD 충돌 없앰 — 갑판에 파인 자리는 고정 바닥으로
- 사용자 요청("JBD 콜라이더 없애 주고 갑판 위를 이동하는 데 문제없게"): JBD 판(항모 모델 `JBD 1` · `JBD 2`)의 MeshCollider를 지운다 — JBD는 모양만 움직이고(6-242), 올라가 있어도 기체 · 사람이 지나간다.
- 함정: 항모 갑판 충돌(모델 `甲板` 메시)은 JBD 자리가 약 1m 파여 있었다(갑판 21.41 · 바닥 20.43, 약 13.4 × 6.2m — 지금까지는 JBD 판 충돌(윗면 21.44)이 덮고 있었다). 충돌만 지우면 바퀴 · 발이 빠진다. 그래서 갑판 높이에 맞춘 납작한 상자 충돌(`JBD 1 Floor` · `JBD 2 Floor`, 두께 0.3m, 가장자리 0.1m씩 갑판에 걸침, 갑판과 같은 레이어)을 JBD와 같은 부모(항모 모델)에 둔다 — 항모와 함께 움직인다. 내려간 JBD는 바닥보다 3cm 위에 그려져 겉보기는 그대로.
- 적용: Setup Check '갑판 요원'이 "JBD 충돌 남음" · "JBD 자리 바닥 없음"을 보고 [고치기](`ACO_SceneBaker.InstallCatapultCrew` → `BakeJbd` → `FixJbdCollision`): 바닥이 없으면 JBD 충돌을 잠깐 끄고 판 둘레(판 크기 + 1.5m)를 0.1m 간격으로 내려 쏴 둘레 갑판보다 0.3m 넘게 낮은 칸을 감싸는 상자를 만든 뒤 JBD와 자식의 충돌을 지운다. 바닥을 못 만들면 충돌을 남긴다(빠지는 것보다 낫다). 여러 번 눌러도 같다.

### 6-254. LAUNCH 직후 기체가 앞으로 밀려 나가던 것 — 엔진 출력 · 속도 지우기, 기어 즉시 내리기
- 사용자 지적("물에 빠진 비행기를 같은 사람이 LAUNCH로 다시 탔을 때 스폰하자마자 앞으로 날아간다 … 공중에서 가속을 넣은 채 내린 뒤에도 생길 수 있다"): SAV는 내릴 때(EngineOffOnExit) 엔진만 끄고 스로틀 입력만 0으로 한다 — 엔진 출력(`EngineOutput`)은 꺼진 뒤 천천히 줄어(F/A-18: AccelerationResponse 2.5 × EngineSpoolDownSpeedMulti 0.1 → 반감기 약 4초) 그동안 추력이 남는다. LAUNCH(`ACO_GameFlow._LaunchMove`)는 SAV 리스폰을 거치지 않고 갑판으로 옮겨 속도만 0으로 하므로 남은 출력이 그대로 — 격추 · 탈출 5초 뒤 격납고로 와서 바로 누르면 출력이 수십 %. 탈 때 SAV가 엔진을 다시 켜면 그 출력에서 줄어들며 밀어낸다. 또 LAUNCH는 기어를 내리지 않아, 기어를 올린 채 내린 기체는 기어가 접힌 채 갑판에 놓였다(SAV는 기어가 올라가 있으면 땅 감지를 꺼 공중으로 보고, ACO 갑판 택싱 제한도 걸리지 않는다).
- 고침(`ACO_VehicleAutomation.SFEXT_O_VehicleTeleported` — LAUNCH가 옮긴 직후 보내는 신호, 오너 화면): 조종사가 타고 있지 않으면 엔진 출력 · 스로틀 입력 · 조종사 스로틀 0, SAV `CurrentVel` · `LastFrameVel` 0(탈 때 SAV가 기체 속도를 CurrentVel로 덮어쓴다), 강체 속도 · 회전 속도 0, 애프터버너가 켜져 있으면 모두의 화면에서 끔(`SetAfterburnerOff`), 기어가 올라가 있으면 모두의 화면에서 즉시 내림(DFUNC_Gear의 리스폰 처리 — `SetGearDown` + `instantgeardown`). 연료 · 체력 · 탄약은 그대로(LAUNCH는 리스폰이 아니다). 엔진 출력은 동기화 값이라 다른 화면의 소리 · 불꽃도 따라온다.

### 6-257. 기총 탄약 강화 — 최대 5배
- 사용자 요청("기총 탄약량 강화도 넣어 줘, 최대 5배 정도"): 상점 기체(AIRFRAME) 탭에 기관포 탄약량 · GUN AMMO CAPACITY(저장 ID `gun_ammo`, 항목 6 — 기총 피해 바로 뒤)를 더한다. 단계당 +40%, 10단계 ×5. 1단계 1200크레딧(기총 피해 1500보다 조금 싸게), 단계 가격은 다른 항목과 같은 배수(LevelCostScale).
- 항목 번호가 하나씩 밀린다(일반 미사일 7~13, 특수 대공 14~20, 특수 대지 21~26, 특수 폭탄 27~30, UPGRADE_COUNT 31). 저장은 고정 ID라 기존 기록은 그대로. 씬의 강화 배열(이름 · 배수 · 가격)은 Setup Check가 수 불일치를 보고 기본값으로 다시 넣고, Bake가 상점 아이콘을 다시 채운다(기총 탄약 = 새 `aco_icon_gun_ammo` — 탄띠로 묶인 기관포탄 세 발 + 더하기, `Tools/aco_icons.py`의 `i_gun_ammo`. 미사일 탄수 ammo 아이콘은 미사일 세 발이라 헷갈려서 따로 그렸다). 기체 탭은 7줄 — 상점 줄 수(MAX_CATEGORY_LENGTH 7) 안이라 패널은 그대로.
- 적용(`ACO_VehicleUpgrades`, 조종사 화면 — 탑승할 때 · 내릴 때): SAV 기총(DFUNC_Gun — Bake가 `Guns`에 넣는다)의 사격 가능 시간(`FullGunAmmoInSeconds`, F/A-18 12초)을 × 배수. 남은 탄은 같은 비율로(가득이면 가득), 내리면 원래 시간으로. SAV가 시작할 때 계산해 두는 재보급 속도(`reloadspeed`)와 탄 막대 눈금(`FullGunAmmoDivider`)도 같이. 리스폰 · 격추 · 급유 드론 재보급은 SAV가 늘어난 최대치로 채운다. HUD 기총 탄 수(사격 시간 × 발사 속도)도 그대로 따라 늘어난다.

### 6-258. 가디언 드론 요격 강화 — 거의 미사일 면역, 레일건은 못 막음
- 사용자 지적("가디언 드론 성능을 좀 높이자, 거의 미사일 면역이 될 정도로. 유료치곤 성능이 별로 … 레일건 탄은 아주 빠르니까 레일건은 요격 못 할 정도로"): 예전(6-143)은 반응 0.4초 + 격추 0.8초 뒤 2초 쉼이라 약 2.8초에 한 발 — 연달아 두 발이면 둘째가 뚫렸다.
- 값(가디언 프리팹의 `ACO_AAGunSite` — Setup Check 튜닝 guardian-intercept-2, 빌더 `ACO_PrefabBuilder.Guardian*`(ACO_SupportBuilder.cs)도): 반응 0.1초, 격추 0.45초, 쉼 0초, 사거리 1200 → 1500m(미사일이 CIWS를 찾는 거리 1700m 안). 방어 반경 60m · 가격 8,000 · 90초 · 쿨타임 150초는 그대로.
- 레일건은 따로 막지 않는다 — 속도로 갈린다: 레일건 탄(LUNA · MR. S, 4000m/s)은 사거리 1500m를 0.375초에 지나 격추(0.45초, 쉬다 처음이면 0.55초)를 못 채운다. 일반 미사일 600m/s는 사거리 안 2.5초 동안 5발, VLS 900m/s 3발, VLS+ 1200m/s 2발까지. 랜스 소형 레일건은 포탄(요격 대상 아님)이라 원래 못 막는다. 폭탄 · 로켓 · 기총도 그대로 못 막는다.

### 6-259. 발함 지휘소 뚜껑 충돌 없앰
- 사용자 지적("캐터펄트 사이 조작하는 장소에 콜라이더가 있어서 걸리면 공중에 뜬 걸로 인식돼 10노트 이상으로 가속된다"): 두 캐터펄트 사이 발함 지휘소(항모 모델 `発艦指揮所`, MeshCollider)는 갑판 위로 테두리 턱 · 돔이 약 0.95m 솟아, 바퀴가 걸려 뜨면 SAV가 공중(Taxiing 아님)으로 보고 갑판 택싱 제한(6-216 — 땅에 닿아 있을 때만)이 풀렸다. 풀린 뒤 제한 속도보다 빨라진 채 내려앉으면 다시 걸리지 않는다(착함 실패 뒤 다시 뜨게 둔 규칙).
- 사용자 결정("뚜껑 쪽만 콜라이더 지워 줘"): 충돌 메시를 둘레 갑판 높이(21.41)에서 잘라 아래(구덩이 벽)만 남긴 메시(`Generated/ACO_LaunchBubbleCollider.asset`)로 바꾼다 — 원본 FBX는 그대로. 겉모습(돔)은 그대로라 기체가 돔을 통과해 지나간다.
- 적용: Setup Check '갑판 요원'이 "발함 지휘소 뚜껑 충돌 남음"을 보고 [고치기](`ACO_SceneBaker.InstallCatapultCrew` → `RemoveLaunchBubbleLid`). 걸치는 삼각형은 갑판 높이에서 자른다(한 평면 Sutherland–Hodgman).
- 구덩이 바닥(사용자: "워크스루 레이어가 아니라 디폴트 레이어로 덮자"): 뚜껑이 없어져 열린 지휘소 아래 구덩이(약 2 × 4.5m · 깊이 3m)를 갑판 높이의 납작한 상자 충돌(`発艦指揮所 Floor`, 두께 0.3m, 가장자리 0.1m씩 갑판에 걸침, 갑판과 같은 Default 레이어 — JBD 자리 바닥 6-253과 같은 방식)로 덮는다. 워크스루 레이어는 SAV 땅 감지 레이(Default · Environment)에 안 잡혀 그 위에선 공중으로 판정되므로 쓰지 않았다. Setup Check '갑판 요원' "발함 지휘소 구덩이 바닥 없음" → [고치기](`FillLaunchBubblePit`).
- 탑승자 전용(사용자: "기체에 탑승한 유저만 디폴트 레이어로 보이게"): 바닥에 `ACO_PilotOnlyColliders`(동기화 없음 — 화면마다)를 붙여, 기체를 조종 중인 사람 화면에서만 충돌을 켠다. 기체 물리는 소유자(조종사) 화면에서만 계산되므로 다른 화면은 바닥이 없어도 기체가 빠지지 않는다. 걸어 다니는 사람은 구덩이(지휘소 자리)로 내려간다.
  - 탈 때 · 내릴 때는 `ACO_VehicleAutomation`이 이벤트로 바로 알린다(`SetLocalPiloting`). 걸어 다니는 동안만 0.5초마다(내린 순간에는 바로) 내가 소유한 플레이어 기체가 15m 안에 있는지 보고, 있으면 켜 둔다 — 지휘소 위에서 내린 빈 기체가 내 화면에서 빠지지 않게(그동안은 나만 구덩이에 못 들어간다).
  - Setup Check "발함 지휘소 바닥이 탑승자 전용이 아님" → [고치기](`MakeLaunchBubbleFloorPilotOnly`), 기체 자동화 · 플레이어 기체 목록 연결은 Bake(`BakePilotOnly`).

### 6-260. 로켓(RKT) 밸런스 — 탄수 72 · 재장전 4초
- 사용자 요청("로켓은 밸런스 어때? … 3번으로"): 풀업(폭탄 계열 강화 — 피해 ×2.5 · 탄수 ×2 · 재장전 ×2) RKT는 한 발 250 · 128발 · 슬롯 8개 1.5초 재장전이라 지속 화력이 초당 약 1330으로 다른 공대지 무장(170~750)의 2~3배였다. 곧게 날아가 큰 표적에 맞히기 쉽고 CIWS에 요격되지도 않아, LEVIATHAN을 부상 45초 중 약 9초 만에 혼자 격침했다.
- 값(F/A-18 프리팹 Module_RKT — Setup Check 튜닝 rkt-balance-3, 프리셋 `ACO_PlayerMissileSetup.RocketAmmo · RocketSlotReload`도): 탄수 64 → 72, 슬롯 재장전 3 → 4초. 한 번에 8발(0.08초 간격) 연사는 그대로. 풀업 지속 화력 약 1000/초 · 144발 · 총 36,000.
  - 거쳐 온 값: 처음 탄수 48 · 재장전 6초(약 670/초 · 24,000) → 사용자 "DPS 자체는 로켓이 폭탄보다 높으면 좋겠다, 범위가 훨씬 좁잖아"로 재장전 4초(1000/초) → "총 피해도 UGB랑 비슷하거나 같았으면"으로 탄수 72(36,000 = UGB와 같음).
  - 구도: 총량은 UGB와 같고, 범위가 좁은 대신(37m vs 500m) 화력이 높다(1000 vs 750/초). LEVIATHAN 혼자 약 12초(UGB 직격 약 16초). 기본 RKT는 약 200/초 · 총 7,200.
- 설명 글자 "탄창이 크고 재장전이 빠릅니다"를 "한 번에 8발씩 몰아 쏩니다"로(wpn.RKT, 카탈로그).
- 같이 본 다른 공대지 무장(풀업, 직격 · 요격 제외): 4AGM 약 500/초 · 36,000, LGB 350 · 31,500, LASM 310 · 24,000, LAGM 300 · 24,000, LGR 500 · 27,000, 일반 미사일 170 · 30,000 — 고르게 모여 그대로. SCM은 넓은 표적(항모 · 요새)에 강한 편이지만 요격되는 미사일이라 그대로. MMP · RGP는 강화 계열이 없어 풀업 시대엔 1/8~1/10(약 3,800 · 3,700) — 보류.

### 6-261. LGB · LGR 밸런스 — 특징에 맞게
- 사용자 요청("LGR이랑 LGB도 특징에 맞게 조정해 줘"): 풀업 LGB는 반경 375m로 UGB만큼 넓은데 유도라 늘 직격 — "범위가 좁다"는 설명과 달리 UGB의 상위 호환이었다. LGR은 다 맞는데 총 피해(27,000)가 무유도 RKT(6-260 뒤 24,000)보다 컸다.
- LGB(한 발이 가장 센 정밀 폭탄): 피해 350 → 500, 범위 150 → 50m, 탄수 18 → 12(장전 10초 그대로). 풀업 1250 · 125m · 24발 · 약 500/초 · 총 30,000 — 구축함 · 순양함 직격 1발, 항모 4발, 여러 척을 함께 깎지는 못한다.
- LGR(확실히 맞는 정밀 연사): 탄수 54 → 60, 재장전 8 → 6초(피해 100 · 범위 15m 그대로). 풀업 250 · 37m · 120발 · 약 667/초 · 총 30,000. 처음엔 40발 · 8초(500/초 · 20,000)였다가 사용자 "LGB랑 LGR도 컨셉에 맞게"(RKT를 UGB와 총량 같게 · 화력 높게 맞춘 뒤)로 바꿨다.
- 구도(짝): 무유도형 UGB · RKT 총 36,000 — 폭탄은 범위(500m · 750/초), 로켓은 화력(37m · 1000/초). 유도형 LGB · LGR 총 30,000(약 5/6 — 확실히 맞는 값) — 폭탄은 한 방(1250 · 125m · 500/초), 로켓은 화력(250 · 37m · 667/초). 짝 안에선 로켓이 폭탄보다 화력이 높고 총량은 같다.
- 적용: Setup Check 튜닝 lgb-lgr-balance-2(F/A-18 프리팹 Module_LGB · Module_LGR, 씬이 덮어쓴 값은 지움), 카탈로그 프리셋(`ACO_SpecialWeaponCatalog.Lgb* · LgrAmmo · LgrReload`), 설명 글자(wpn.LGB · wpn.LGR — "폭발은 좁지만 한 발이 가장 강합니다", "확실히 맞는 대신 탄수가 적고 재장전이 느립니다").

### 6-262. 공대공 밸런스 — LAAM · HVAA 컨셉 나누기, CCAA 대에이스 상향
- 배경: 에이스는 1,950m(1500 × 1.3) 안에 들어온 미사일마다 90%로 플레어를 뿌리고, 켜진 2.5초 동안 쫓는 미사일은 모두 속는다(쿨 3초, 12번). 그래서 에이스 상대 락온 미사일은 확률 실패 · 0.5초 틈 · 플레어 소진 · 대응책 무시로만 맞는다. 대응책을 무시하는 CCAA가 정체성이 맞는데 락온 2.5초 · 30도 · 선회 70이라 기동하는 에이스를 잡기 어려웠다.
- 유도는 모두 직접 추적 그대로 — 비례항법은 동기화 탓에 오히려 덜 맞는다(사용자). HVAA가 에이스를 잘 못 잡는 것도 그대로 둔다(사용자 "hvaa는 냅두자").
- LAAM(장거리 요격 — 느리게 출발해 멀리서 가장 빠름): 가속 320 → 200, 최고 속도 1200 → 2400(HVAA 1900보다 빠름), 선회 90 → 110. 락온 7km는 일부러 둔 값(락온 거리 풀업 17.5km). 비행 시간(기체 250m/s 발사, 멈춘 표적): 2km 2.7 → 3.1초, 7km 6.8 → 6.9초, 17.5km 15.6 → 11.8초. 최고 속도에서 선회 반경 약 1.25km — 직진하는 폭격기 · 수송기 요격용. 수명 20초에 약 37km.
- HVAA(단거리 순간 가속): 가속 700 → 1500(약 1초 만에 1900). 1km 1.3 → 1.0초, 2km 2.0 → 1.5초. 선회 70 · 피해 200 그대로.
- CCAA(대에이스): 락온 2.5 → 1.5초(풀업 1.0초), 선회 70 → 90(풀업 135). 대가는 좁은 락온 각도 30도와 장전 12초. 플레어 · MR. S 태양 · SHADE 코브라 미끼에 안 속는 것은 그대로.
- 적용: Setup Check 튜닝 aam-balance-1(F/A-18 프리팹 Module_LAAM · HVAA · CCAA와 특수무장 정의 프리팹 — 새 기체에 설치할 때 복제되는 원본 · 설명, 씬이 덮어쓴 MaxSpeed · Acceleration · TurnRate · LockTime은 지움), 카탈로그 프리셋(`ACO_SpecialWeaponCatalog.Laam* · HvaaAccel · Ccaa*` — 세 무장의 다른 값도 실제 기체 값에 맞춤: LAAM 락온 7km, HVAA 피해 200 · 24발, CCAA 20발), 설명 글자(wpn.LAAM · wpn.HVAA · wpn.CCAA).

### 6-263. FLAK 피해 상향 · 특수무장 정의 프리팹을 기체 값으로
- FLAK: 피해 30 → 50(사용자 "50으로 가자"). 신관(적기 마커 중심 100m)이 반경 안에 드는 첫 순간 터지므로 거의 늘 범위(150m) 가장자리 약 95m에서 터져 표시 피해의 약 절반이 들어간다(MinSplashFraction 0.2). 기본 약 25/발 — 전투기(200) 8발, 에이스(600) 24발, 총 약 1,350(54발, ADAM 1,200과 비슷), 지속 약 50/초. 풀업 약 62/발 · 108발 · 약 250/초 — 무유도라 기동하는 에이스엔 실제로 절반쯤이어서 CCAA(풀업 약 167/초, 플레어 무시) 근처, 대에이스 주력은 CCAA로 남는다. 플레어와 무관한 것은 그대로.
- 특수무장 정의 프리팹(`SpecialWeapons/ACO_Special_*` — 새 기체에 설치할 때 복제되는 원본): 그동안 튜닝 항목이 기체(F/A-18) 프리팹만 바꿔 정의에 옛 값이 남아 있었다(19개 무장, 예: HVAA 최고 속도 800 · 피해 130, FLAK 피해 90 · 신관 12m, LGB 150 · 24발). 사용자 "수정사항들 무장 프리팹에도 적용해 줘"로 정의의 숫자 · 켜기 · 선택 값을 기체 모듈과 같게 맞췄다(참조 · 레이어 마스크는 그대로). 기준은 기체 값.
- 적용: Setup Check 튜닝 flak-damage-1(기체 Module_FLAK, 씬 덮어쓴 값 지움), weapon-defs-sync-1(`ACO_PrefabTuning.SyncWeaponDefsFromAircraft` — 다시 맞출 땐 새 ID로 같은 함수), 카탈로그 FLAK 프리셋을 실제 값으로(50 · 54발 · 신관 100m · 범위 150m · 가장자리 0.2).
- 남은 것(6-263): 카탈로그 프리셋(예제를 다시 만들 때 쓰는 값)은 오늘 손본 무장(RKT · LGB · LGR · LAAM · HVAA · CCAA · FLAK) 말고는 아직 옛 값이다 — 예제를 다시 만들면 정의가 옛 값으로 돌아가므로 다시 만든 뒤엔 weapon-defs-sync를 새 ID로 한 번 더.

### 6-264. 캐노피 앞 프레임 손잡이 · 거울 붙이기(6-195 보완)
- 사용자 지적: 조종석 캐노피 앞 프레임 안쪽의 손잡이(좌우 C자 손잡이)가 공중에 떠 있다. 원본 모델에선 손잡이 팔 끝이 프레임 안쪽 면에 박혀 있다.
- 원인: 6-195의 프레임 안쪽 깊이 줄이기(×0.4)가 안쪽 면을 바깥쪽(유리 쪽)으로 크게 옮기는데, 작은 덩어리(손잡이 2 · 거울 3)는 자기 가장 바깥 정점 — 프레임 속에 박힌 끝이라 바깥면 근처, 변위가 작다 — 의 변위로 옮겨져 안쪽 면과 벌어졌다.
- 고침(`ACO_CanopyThin.GlueParts`, Canopy.E만, 프레임 면에 3mm 안으로 직접 닿은 덩어리만 — 손잡이 2 · 옆 거울 2. 가운데 거울은 면에서 약 7mm 떨어져 매달린 모양이라 붙이면 오히려 벌어져 예전 방식 그대로): 덩어리 정점 둘레 2cm 안의 프레임 면 점 중 안쪽 면(단면 중심에서 가장 가까운 점 + 5mm)의 변위(모으기 · 올리기 · 깊이 줄이기 · 홈 메우기 모두 넣은 값) 평균으로 덩어리를 통째로 옮긴다 — 박힌 깊이가 원본과 같다. 바깥면을 고르지 않는 것은 박힌 끝에서 가장 가까운 면이 바깥면일 수 있어서.
- 적용: Setup Check 튜닝 canopy-thin-4(메시 다시 만들기 — 기체 프리팹 · 격납고 미리보기).

### 6-272. 플레이어 기체끼리 기총 피해 · 충돌 끄기
- 사용자 요청("건이나 무유도무기로 팀킬가능해?" → "막아줘, 그리고 가능하다면 기체끼리 충돌도 끄고싶은데").
- 조사: 무유도 폭탄 · 로켓(`ACO_Ordnance`)은 직격 · 범위 모두 `SaccTarget`(NPC 체력)에만 주고, 유도 미사일은 락온 후보(`Targets`)에 플레이어 기체가 없어 원래 팀킬이 없다. 기총만 SAV 방식(탄 파티클 → 맞은 기체 `SaccEntity.OnParticleCollision`)이라 아군 기체에 들어갔다.
- **기총**: 플레이어 기체(F/A-18E 프리팹)의 `SaccEntity.DisableBulletHitEvent`를 켠다(튜닝 `player-gun-friendly-off-1`). 플레이어 기체가 파티클로 받는 피해는 아군 기총뿐이었다 — 적 기총 · 대공포(`ACO_NPCGun`) · 레일건 · 미사일은 `WeaponDamageVehicle` 직접 피해라 그대로, 보급(`SaccResupplyTrigger`)은 트리거라 영향 없음. 적 전차 탄 파티클(보이기용)이 기체에 닿으면 SAV 기본 피해 10이 한 번 더 들어가던 것도 같이 사라진다.
- **충돌**: `ACO_VehicleUpgrades`가 탑승 중 `PlayerIgnoreInterval`(1초)마다 `PlayerIgnoreDistance`(1500m) 안의 다른 플레이어 기체(`OtherPlayerVehicles`, Bake가 채운다)와 충돌 콜라이더 쌍을 `Physics.IgnoreCollision`으로 끈다. NPC 공중 유닛과 같은 규칙 — 충돌 데미지는 조종사(오너) 화면에서 계산하므로 각자 자기 기체만 건다. 주기적으로 다시 거는 건 상대가 격추 · 부활하며 콜라이더를 껐다 켜면 무시 상태가 풀리기 때문. 기총 · 미사일 · 지형 · 갑판 충돌은 그대로.

### 6-273. 지원 호출 — 아군 편대(WINGMEN)
- 사용자 요청("아군 ai기체를 부르는 지원권 만들수있을까?" → 역할 둘 다(공대공 먼저, 없으면 지상), 2기 120초, 격추될 수 있음, 점수는 부른 사람에게 전부).
- **호출권**: `CALL_WINGMEN = 3`(저장 ID `wingmen` — 바꾸지 않는다), 5,000 CR(처음 8,000 — 바뀜 2026-11-17, 튜닝 `wingmen-8-180-1`), 쿨타임 30초(편대가 돌아간 뒤부터 — 처음 180초, 바뀜 2026-11-18, 튜닝 `wingmen-cd-30-1`), 최대 9장. 상점 SUPPORT 탭 4번째 줄 · 격납고 SUPPORT 줄 · 다이얼 HUD 이름 `WM`(떠 있으면 `ON 64s`). 아이콘 `aco_icon_sup_wingmen`(앞 테두리 기체 + 뒤 양옆 채운 기체). 저장된 배열이 3칸이면 튜닝 `wingmen-call-1`이 늘린다 — 그 전에도 가격 · 이름은 코드 기본값(공짜로 사지 않게).
- **부르기**(`ACO_DFUNC_Support.TryWingmen` → `ACO_SupportManager.CallWingmen`): 작전 중에만(`NO OPERATION IN PROGRESS`), 같은 기체로 이미 떠 있으면 `ALREADY ON STATION`, 빈 편대가 없으면 `NO FLIGHT AVAILABLE`(호출권 그대로). 부르면 호출권 1장, 무전 "Wingmen inbound. Forming up on you."
- **풀**: 지원 관리자 아래 `Wingmen` 8기(두 기씩 한 편대 — 동시에 네 사람 = 플레이어 기체 4대 모두. 처음 4기 — 바뀜 2026-11-17, 사용자 "아군기 총8대로해서 모두다 부르게"). 편대 수는 풀 크기 / 2로 정해져 코드는 그대로. 프리팹 `NPC_Wingman` = `NPC_Fighter` 복사본 — 노리는 대상 적 공중(29) · 지상(30)(`AAMTargetsLayer`, SAV가 빌드 때 328개를 굽는다), 락온 마커는 아군 공중(25 — 적 전투기 · 대공포 · SAM이 노리고 플레이어 미사일은 잠그지 않는다), 체력 400, 플레이어 기총은 맞지 않는다(`SaccTarget.DisableBulletHitEvent`), 회청색 도색(`Generated/ACO_Wingman_*.mat`), 소유권 넘기기 끔. Setup Check가 없으면 만든다.
- **시작**(모든 화면 — 이벤트 `NetWingmenStart`(편대 · 다이얼 · 부른 사람 · 남은 초 · 자리 · 방향 · 새로 부름)): 부른 사람 기체 뒤 2.5km · 위 200m에 좌우 80m로 나타난다. 부른 사람이 두 기의 소유권을 가져가 계산한다(`ACO_NPCFighterNet`가 위치를 보낸다). 늦게 들어온 사람에겐 부른 사람이 남은 시간으로 다시 알린다(위치 · 생사는 동기화가 맞춘다).
- **전투**(`ACO_NPCFighterAI` 그대로 — 새 옵션은 기본 꺼짐이라 적 AI는 그대로): `AirTargetBonus` 3000m(공중 표적을 그만큼 가깝게 본다 — 3km 안의 적 전투기가 어떤 지상 표적보다 앞선다), `SkipCityVehicles`(시가지 정밀 타격의 도시 차량은 적 · 민간 모두 노리지 않는다), 공격 정원(적 디렉터) 밖, 적과 서로 간격 유지 목록이 따로(아군 편대끼리만). 체력 0인 표적은 쫓지 않는다(모든 NPC 공통 — 잔해 마커를 쫓지 않게).
- **편대 · 이탈**(`ACO_Wingman`, 오너): 표적이 없으면 부른 사람 기체 뒤쪽 옆(오른쪽 · 왼쪽 60m, 뒤 70m)으로 따라가고, 4km 넘게 멀어지면 교전을 멈추고 돌아온다. 부른 사람이 기체에 없으면(내림 · 격추 · 다른 사람이 탐) 마지막 자리 주변에서 싸운다. 180초(처음 120 — 바뀜 2026-11-17, 사용자 "생각보다 강하진 않더라")가 지나거나 작전이 끝나면 위로 빠져 12초 뒤 꺼진다. 귀환 무전 "Wingmen are bingo fuel. RTB."(시간 끝일 때만).
- **점수**: 기총(`ACO_NPCGun.CreditOwner`) · 미사일(`ACO_MissileLauncher.CreditOwner` → 쏜 기체의 오너가 판정, NPC 무기 표시 없음)의 명중을 부른 사람 화면이 판정해 `LastHitByPlayer` = 부른 사람 — 팀 점수 · 개인 점수 · 작전 기록 격파 모두 직접 잡은 것과 같다. 미사일 무전은 내지 않는다.
- **격추**: 적이 노리고 맞힐 수 있다(SaccTarget이 `ExplodeOther`로 `ACO_Wingman.Explode`). 부른 사람에게 "Wingman down!", 둘 다 떨어지면 편대 종료(쿨타임 시작).
- **아군 오폭 없음**: 플레이어 폭탄 · 로켓(`ACO_Ordnance` 범위 · 직격 · 근접 신관), 미사일 범위 · 근접(`ACO_Missile`), 벙커버스터 범위(`ACO_SupportManager`)가 아군 편대를 건너뛴다. 아군 편대와 플레이어 기체는 서로 통과(6-272와 같은 IgnoreCollision 목록).
- **표시**: HUD 마커 아군 테두리 + `WINGMAN`(`ACO_TargetMarkers.SupportWingmanLabel`, 150m보다 가까우면 안 그린다), 레이더 아군 공중(`ACO_RadarContacts.Support` — Bake).
- **무전**(AWACS, 새 음성 `Audio/AWACSVoiceLines/Support/Support_Wingmen{Call,Down,End}.mp3`, 번역 `radio.supportmanager.10~12`), 상점 문구 `prog.call/callfx/calldesc/callnote.wingmen`.
- **디버그 검토 수정**(2026-11-17, 편대 자리 배정 문제는 그대로 둠):
  - 이탈 중 사격 금지: `ACO_NPCFighterAI.HoldFire`(NonSerialized, 모든 화면) — 켜지면 표적을 놓고 미사일 · 기총을 쏘지 않는다. `StartLeave`가 켜고 `Begin` · `Finish`가 끈다.
  - 복귀 히스테리시스: 4km 넘으면 복귀를 시작하고, `ReturnFraction` 0.6(2.4km) 안으로 들어와야 다시 교전한다.
  - 격추 후 재사용: 락온 마커(아군 공중 25 층)를 `HideOnDeath`에 넣어 격추되면 꺼진다(부활 때 다시 켜진다). AI가 살아 있으면 `_dead`를 되돌린다. 새로 부르면 `MissileLauncher.ResetAmmo()`. `Finish`가 관리자 `OnWingmanGone`보다 먼저 돈다.
  - 소유권: `SendWingmenStart`는 두 기의 소유권을 먼저 가져온 뒤 이벤트를 보낸다. `_RespawnTargetHealth`는 그 사이 소유권이 넘어갔으면 다시 뺏지 않고 값만 맞춘다.
  - 동시 호출: 3초 안에 같은 편대를 두 사람이 부르면 플레이어 번호가 낮은 쪽이 갖고, 진 쪽은 남은 편대로 다시 부르거나 없으면 호출권을 돌려받는다(`ACO_PlayerProgress.RefundCall`) + `WINGMEN NO FLIGHT AVAILABLE` 경고.
  - 시작 직후 2초는 작전 진행 여부를 보지 않는다(상태 동기화가 늦게 와도 바로 끝나지 않게).
  - 기총 점수: `ACO_NPCGun` 오너 판정이 `LastHitByPlayer` · `LastHitWeaponType`을 먼저 적는다(미사일과 같다).
  - 상위 표적 재탐색(`HigherPriorityInRange`)이 체력 0인 표적을 건너뛴다(모든 NPC).
  - 적 디렉터 `Fighters`에서 아군 편대를 뺀다(Bake). 빌더: 미사일 락온 경고 끔(`SendLockWarning`), 플레어가 있으면 `Friendly`, 이미 칠한 `ACO_Wingman_*` 재질은 다시 칠하지 않는다.
  - 저장: 호출권 저장 키 범위 확인(`c < _callKeys.Length`).
- **전용 미사일**(2026-11-17 사용자 결정 — "전용 미사일로 가자"): `ACO_Missile_Wingman` = `ACO_Missile_AAM`의 프리팹 변형(겉모습 · 소리 같음). 피해 100 · 선회 60°/초 · 최고 속도 900 · 근접신관 9m(플레이어 기본 MSL, 강화 전), 플레어에는 그대로 속는다, 미사일 접근 경고 없음. 적 전투기는 신참 · 베테랑 2발, 에이스 3발. 발사 조건(4발 · 1.5초 · 재장전 20초 · 300~2000m · 15°)과 점수는 그대로. 빌더 `BuildWingmanMissile`이 없으면 만들고, 있으면 값만 다시 넣는다(GUID 유지).
- **기체 모델 · 도장**(2026-11-17): 사용자가 프리팹 모델을 `Jet_02`(CartoonMilitaryModelPack)로 바꿨고, 등급별 모델 목록은 비웠다(항상 같은 모습). 원본 FBX · 텍스처는 그대로 두고 `Tools/aco_wingman_jet.py`(Blender)가 만든다:
  - `bake`: UV를 다시 편다(Smart UV, 섬 크기 = 3D 면적 비례). 위치 · 법선 · 원본색 · AO · 면 구분(캐노피 / 흡입구 · 노즐 — 원본 텍스처의 면 가운데 색)을 굽고, 메시를 `Models/Wingman/ACO_WingmanJet_Mesh.json`으로 쓴다.
  - `paint`: 3D 위치 기준으로 칠한다(섬 경계에서 끊기지 않음) → `ACO_WingmanJet_Albedo.png`(2048). 윗면 회청 2색 위장 · 아랫면 옅은 회색, 레이돔 · 눈부심 방지 판, 패널 라인(동체 이음매 · 점검 해치 · 날개 앞전 플랩 · 플랩/에일러론 경첩 · 리브 · 방향타 — 날개 · 꼬리 앞전 · 뒷전은 직선으로 맞춤), 날개 끝 · 꼬리 끝 남색 띠, 편대등, 원형 표식(왼날개 윗면 · 오른날개 아랫면 — 가공), 수직꼬리 바깥 `WM`, 기수 옆 `201`, 구조 표시, 캐노피 유리 · 테, 흡입구 · 노즐, AO. 표식은 모두 가공.
  - Unity: `BuildWingmanPrefab`이 모델 아래 `Jet_02_Body`를 찾아 JSON으로 메시 에셋 `ACO_WingmanJet.asset`(x 반전 · 0.1배 · 감기 뒤집기 — 원본 꼭짓점과 오차 0)과 재질 `ACO_Wingman_Jet.mat`(Standard, 회청색 곱하기 제외)을 넣는다. 삼각형 수는 원본과 같다(902).
- **플레어**(2026-11-17, 사용자 "윙맨에 플레어좀 붙여줘라" → 확률 60%): 적 전투기의 플레어 장치는 Setup Check가 씬에서 붙이는데 아군 편대 풀은 그 검사 밖이라 플레어가 없었다. 빌더가 `NPC_Wingman` 프리팹에 `ACO_FlareDispenser`를 넣는다 — 아군 표시(`Friendly`, 무전은 아군 플레어 대사), 출격마다 6회(AI 스폰 때 `ResetSalvos`), 확률 60%(아군 편대는 항상 베테랑이라 `VeteranFlareChance` — 신참 · 에이스 값도 0.6), 반응 1,500m · 쿨타임 3초(장치 기본값), 나오는 자리 `Flares` 파티클을 Jet_02 꼬리 배 아래(로컬 0, −1, −7.5)로. 이탈 중에도 뿌린다.
- **그대로 두기로 한 것**(사용자 결정):
  - 레일건 포대(`ACO_MiniRailgun`)는 가장 가까운 표적을 따라 아군 편대도 겨누지만, 피해는 플레이어 기체만 받는다(아군 편대는 맞지 않음).
  - 대공포 · SAM은 아군 편대만으로는 깨어나지 않는다(사람이 탄 기체만 깨운다).
  - 6-272 충돌 끄기는 세워 둔 빈 기체에도 적용된다.
