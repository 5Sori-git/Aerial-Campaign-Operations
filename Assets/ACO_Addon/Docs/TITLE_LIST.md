# 칭호 목록 (초안 — 확인용)

- 칭호 ID는 저장값(`aco_title`)이라 정하면 바꾸지 않는다. 이름 · 기준 수치는 언제든 바꿀 수 있다.
- 저장은 분류마다 누적 값 하나. 단계를 나중에 더해도 저장은 그대로(칭호만 는다).
- 비행 시간: 조종석에서 떠 있는 시간(자유 비행 포함, 지상 활주 제외). 누적 격추: CIC만 쓴 미션의 격추도 더한다.
- 보스 칭호는 **보스 페이즈 클리어**로 판정한다(격추 수가 아니다). 한 번도 클리어하지 않은 보스의 칭호는 기록판에 `???`.

## 레벨 (강화 단계 합)

| ID | 조건 | 한국어 | English | 日本語 |
|---|---|---|---|---|
| `level_10` | Lv 10 | 강화 입문 | TUNED | チューンド |
| `level_50` | Lv 50 | 개조 매니아 | MODDED | 改造マニア |
| `level_100` | Lv 100 | 하이 스펙 | HIGH SPEC | ハイスペック |
| `level_200` | Lv 200 | 풀 커스텀 | FULL CUSTOM | フルカスタム |
| `level_300` | Lv 300 | 마개조 | OVERTUNED | 魔改造 |

## 전문 분야 (2026-11-21 추가 — 저장값 없음, 지금 강화 단계 · 해금으로 판정, 금색)

| ID | 조건 | 한국어 | English | 日本語 |
|---|---|---|---|---|
| `role_fighter` | 특수 대공 7항목 모두 최대 | 전투기 파일럿 | FIGHTER PILOT | 戦闘機パイロット |
| `role_attack` | 특수 대지 6항목 모두 최대 | 공격기 파일럿 | ATTACK PILOT | 攻撃機パイロット |
| `role_bomber` | 특수 폭탄 · 로켓 4항목 모두 최대 | 폭격기 파일럿 | BOMBER PILOT | 爆撃機パイロット |
| `role_test` | 기체 7항목 모두 최대 | 테스트 파일럿 | TEST PILOT | テストパイロット |
| `role_interceptor` | 일반 미사일 7항목 모두 최대 | 인터셉터 | INTERCEPTOR | インターセプター |
| `role_multirole` | 특수 대공 + 특수 대지 모두 최대 | 멀티롤 파일럿 | MULTIROLE PILOT | マルチロールパイロット |
| `role_arsenal` | 특수무장 모두 해금 | 무기고 | ARSENAL | アーセナル |

## 미션 성공

| ID | 조건 | 한국어 | English | 日本語 |
|---|---|---|---|---|
| `clears_1` | 1회 | 신참 | ROOKIE | ルーキー |
| `clears_10` | 10회 | 정규 조종사 | REGULAR | レギュラー |
| `clears_50` | 50회 | 베테랑 | VETERAN | ベテラン |
| `clears_100` | 100회 | 역전의 용사 | BATTLE-HARDENED | 歴戦の勇士 |
| `clears_300` | 300회 | 전설 | LEGEND | レジェンド |
| `clears_1000` | 1,000회 | 불멸 | IMMORTAL | 不滅 |

## 5페이즈 미션 성공

| ID | 조건 | 한국어 | English | 日本語 |
|---|---|---|---|---|
| `clears5_1` | 1회 | 장기전 | THE LONG HAUL | 長期戦 |
| `clears5_10` | 10회 | 끝까지 간다 | ALL THE WAY | 最後まで |
| `clears5_50` | 50회 | 강철 체력 | IRON ENDURANCE | 鋼の持久力 |
| `clears5_100` | 100회 | 끝없는 전쟁 | ENDLESS WAR | 終わらない戦争 |

## 누적 격추

| ID | 조건 | 한국어 | English | 日本語 |
|---|---|---|---|---|
| `kills_100` | 100기 | 에이스 | ACE | エース |
| `kills_500` | 500기 | 격추왕 | KILL LEADER | 撃墜王 |
| `kills_1000` | 1,000기 | 천 개의 빛 | THOUSAND POINTS OF LIGHT | 千の光 |
| `kills_5000` | 5,000기 | 죽음의 날개 | WINGS OF DEATH | 死の翼 |
| `kills_10000` | 10,000기 | 재앙 | CALAMITY | 災厄 |

## 비행 시간

| ID | 조건 | 한국어 | English | 日本語 |
|---|---|---|---|---|
| `flight_1h` | 1시간 | 이륙 완료 | WHEELS UP | テイクオフ |
| `flight_5h` | 5시간 | 순항 고도 | CRUISING ALTITUDE | 巡航高度 |
| `flight_10h` | 10시간 | 하늘의 주민 | SKY DWELLER | 空の住人 |
| `flight_50h` | 50시간 | 구름 위의 삶 | ABOVE THE CLOUDS | 雲の上の暮らし |
| `flight_100h` | 100시간 | 창공의 지배자 | MASTER OF THE SKY | 蒼空の覇者 |

## CIC 한 미션 최고 격추

| ID | 조건 | 한국어 | English | 日本語 |
|---|---|---|---|---|
| `cicbest_5` | 5기 | 레이더 감시관 | RADAR WATCH | レーダー監視員 |
| `cicbest_15` | 15기 | 전술 장교 | TACTICAL OFFICER | 戦術士官 |
| `cicbest_30` | 30기 | CIC 사령관 | CIC COMMANDER | CIC司令 |

## CIC 누적 격추

| ID | 조건 | 한국어 | English | 日本語 |
|---|---|---|---|---|
| `cickills_50` | 50기 | 작전 장교 | OPS OFFICER | 作戦将校 |
| `cickills_300` | 300기 | 함대 참모 | FLEET STAFF | 艦隊参謀 |
| `cickills_1000` | 1,000기 | 제독 | ADMIRAL | 提督 |

## 보스별 (10 × 4)

| 종류 | 조건 | 한국어 틀 | English | 日本語 |
|---|---|---|---|---|
| `kill` | 보스 페이즈 클리어 | {0} 사냥꾼 | {0} HUNTER | {0}ハンター |
| `s` | 위 + 미션 S랭크 클리어 | {0} 정복자 | {0} CONQUEROR | {0}の征服者 |
| `solo` | 위 + 출격 1명(나) | {0} 처형인 | {0} EXECUTIONER | {0}の処刑人 |
| `party` | 위 + 출격 4명 | {0} 공략대 | {0} STRIKE TEAM | {0}攻略隊 |

| 보스 ID | 분류 | 한국어 | English | 日本語 |
|---|---|---|---|---|
| `borderless` | 네임드 에이스 | BORDERLESS | BORDERLESS | BORDERLESS |
| `luna` | 네임드 에이스 | LUNA | LUNA | LUNA |
| `twins` | 네임드 에이스 | 쌍둥이 | TWINS | 双子 |
| `shade` | 네임드 에이스 | SHADE | SHADE | SHADE |
| `mrs` | 네임드 에이스 | MISTER.S | MISTER.S | MISTER.S |
| `fortress` | 거대 보스 | 아크엔젤 | ARCHANGEL | アークエンジェル |
| `landship` | 거대 보스 | 육상전함 | LAND DREADNOUGHT | 陸上戦艦 |
| `lance` | 거대 보스 | 랜스 | LANCE | ランス |
| `submarine` | 거대 보스 | 레비아탄 | LEVIATHAN | リヴァイアサン |
| `caliburn` | 거대 보스 | 칼리번 | CALIBURN | カリバーン |

### 펼친 목록

| ID | 한국어 | English | 日本語 |
|---|---|---|---|
| `boss_borderless_kill` | BORDERLESS 사냥꾼 | BORDERLESS HUNTER | BORDERLESSハンター |
| `boss_borderless_s` | BORDERLESS 정복자 | BORDERLESS CONQUEROR | BORDERLESSの征服者 |
| `boss_borderless_solo` | BORDERLESS 처형인 | BORDERLESS EXECUTIONER | BORDERLESSの処刑人 |
| `boss_borderless_party` | BORDERLESS 공략대 | BORDERLESS STRIKE TEAM | BORDERLESS攻略隊 |
| `boss_luna_kill` | LUNA 사냥꾼 | LUNA HUNTER | LUNAハンター |
| `boss_luna_s` | LUNA 정복자 | LUNA CONQUEROR | LUNAの征服者 |
| `boss_luna_solo` | LUNA 처형인 | LUNA EXECUTIONER | LUNAの処刑人 |
| `boss_luna_party` | LUNA 공략대 | LUNA STRIKE TEAM | LUNA攻略隊 |
| `boss_twins_kill` | 쌍둥이 사냥꾼 | TWINS HUNTER | 双子ハンター |
| `boss_twins_s` | 쌍둥이 정복자 | TWINS CONQUEROR | 双子の征服者 |
| `boss_twins_solo` | 쌍둥이 처형인 | TWINS EXECUTIONER | 双子の処刑人 |
| `boss_twins_party` | 쌍둥이 공략대 | TWINS STRIKE TEAM | 双子攻略隊 |
| `boss_shade_kill` | SHADE 사냥꾼 | SHADE HUNTER | SHADEハンター |
| `boss_shade_s` | SHADE 정복자 | SHADE CONQUEROR | SHADEの征服者 |
| `boss_shade_solo` | SHADE 처형인 | SHADE EXECUTIONER | SHADEの処刑人 |
| `boss_shade_party` | SHADE 공략대 | SHADE STRIKE TEAM | SHADE攻略隊 |
| `boss_mrs_kill` | MISTER.S 사냥꾼 | MISTER.S HUNTER | MISTER.Sハンター |
| `boss_mrs_s` | MISTER.S 정복자 | MISTER.S CONQUEROR | MISTER.Sの征服者 |
| `boss_mrs_solo` | MISTER.S 처형인 | MISTER.S EXECUTIONER | MISTER.Sの処刑人 |
| `boss_mrs_party` | MISTER.S 공략대 | MISTER.S STRIKE TEAM | MISTER.S攻略隊 |
| `boss_fortress_kill` | 아크엔젤 사냥꾼 | ARCHANGEL HUNTER | アークエンジェルハンター |
| `boss_fortress_s` | 아크엔젤 정복자 | ARCHANGEL CONQUEROR | アークエンジェルの征服者 |
| `boss_fortress_solo` | 아크엔젤 처형인 | ARCHANGEL EXECUTIONER | アークエンジェルの処刑人 |
| `boss_fortress_party` | 아크엔젤 공략대 | ARCHANGEL STRIKE TEAM | アークエンジェル攻略隊 |
| `boss_landship_kill` | 육상전함 사냥꾼 | LAND DREADNOUGHT HUNTER | 陸上戦艦ハンター |
| `boss_landship_s` | 육상전함 정복자 | LAND DREADNOUGHT CONQUEROR | 陸上戦艦の征服者 |
| `boss_landship_solo` | 육상전함 처형인 | LAND DREADNOUGHT EXECUTIONER | 陸上戦艦の処刑人 |
| `boss_landship_party` | 육상전함 공략대 | LAND DREADNOUGHT STRIKE TEAM | 陸上戦艦攻略隊 |
| `boss_lance_kill` | 랜스 사냥꾼 | LANCE HUNTER | ランスハンター |
| `boss_lance_s` | 랜스 정복자 | LANCE CONQUEROR | ランスの征服者 |
| `boss_lance_solo` | 랜스 처형인 | LANCE EXECUTIONER | ランスの処刑人 |
| `boss_lance_party` | 랜스 공략대 | LANCE STRIKE TEAM | ランス攻略隊 |
| `boss_submarine_kill` | 레비아탄 사냥꾼 | LEVIATHAN HUNTER | リヴァイアサンハンター |
| `boss_submarine_s` | 레비아탄 정복자 | LEVIATHAN CONQUEROR | リヴァイアサンの征服者 |
| `boss_submarine_solo` | 레비아탄 처형인 | LEVIATHAN EXECUTIONER | リヴァイアサンの処刑人 |
| `boss_submarine_party` | 레비아탄 공략대 | LEVIATHAN STRIKE TEAM | リヴァイアサン攻略隊 |
| `boss_caliburn_kill` | 칼리번 사냥꾼 | CALIBURN HUNTER | カリバーンハンター |
| `boss_caliburn_s` | 칼리번 정복자 | CALIBURN CONQUEROR | カリバーンの征服者 |
| `boss_caliburn_solo` | 칼리번 처형인 | CALIBURN EXECUTIONER | カリバーンの処刑人 |
| `boss_caliburn_party` | 칼리번 공략대 | CALIBURN STRIKE TEAM | カリバーン攻略隊 |

총 78개.
