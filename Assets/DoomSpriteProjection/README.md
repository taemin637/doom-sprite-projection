# DOOM 스프라이트 투영과 맵 배치 예제

이 예제는 **이동·회전에 반응하는 스프라이트 투영**과 **벽·바닥·문·시작점·캐릭터를 편집하는 맵 제작 도구**로 구성된다. 실행 중에는 평면 좌표·높이로 화면 위치와 크기를 직접 계산하고, `Screen Space Overlay` Canvas로 결과를 표시한다. 기본 테스트 로봇은 DOOM 원본 에셋이 아니다.

## 실행과 조작

1. `Assets/Scenes/MapEditorDemo.unity`를 연다. 오른쪽 방과 꺾인 통로가 있는 편집 예제다. `SchoolHallway.unity`는 단순 복도, `SampleScene.unity`는 벽이 없는 투영 설명용 씬이다.
2. **Play**를 누르고 **Game 뷰를 클릭**한다. 마우스가 고정되어 좌우·상하 시점을 조절한다. **Esc**로 마우스를 풀고, 다시 클릭하면 시점 조작을 재개한다.
3. 아래 키로 이동·회전한다. `Sprite Projection Demo`의 Inspector에서 플레이어 위치·눈높이·FOV·Mouse Sensitivity도 바꿀 수 있다. 실행 화면에는 안내 텍스트를 표시하지 않는다.

| 키 | 실행 중 동작 |
| --- | --- |
| W / S 또는 ↑ / ↓ | 전진 / 후진 |
| A / D | 왼쪽 / 오른쪽 횡이동 |
| 마우스 이동 | 좌우 회전 / 위아래 보기(±89도) |
| Esc / Game 뷰 클릭 | 마우스 해제 / 시점 조작 재개 |
| Q / E 또는 ← / → | 왼쪽 / 오른쪽 회전 |
| Page Up / Page Down | 위 / 아래 보기(키보드 조작) |
| R | 실행 시작 시 플레이어 위치·좌우/상하 방향·눈높이로 복귀 |

앞으로 가면 물체가 커지고, 뒤로 가면 작아진다. 횡이동·회전에 따라 화면 위치가 바뀌며, 뒤쪽 물체는 표시하지 않는다. 여러 물체가 겹치면 앞방향 깊이가 큰 물체부터 그려 가까운 이미지가 위에 표시되도록 한다.

이동 속도는 기존의 두 배다. 학교 복도는 2.8 → 5.6, 기본 씬은 3 → 6으로 변경했다. 상하 시점은 시선만 기울이며 이동은 계속 지도 XY 평면에서 이루어진다. 마우스 이동량에는 감도만 곱하고, 키보드 이동·회전에는 프레임 시간을 곱한다.

## 제공한 이미지로 만든 학교 복도

`Assets/Scenes/SchoolHallway.unity`를 열고 **Play → Game 뷰 클릭**으로 실행한다. 위 조작키를 그대로 사용한다. 폭 8, 길이 24인 복도에 교실 문과 무당이·무한이를 각각 1개씩 배치했다. 벽·바닥·문도 저장된 격자로 편집할 수 있다. 닫힌 문 타일은 벽처럼 이동을 막는다.

6개 이미지의 프로젝트 사본은 `Assets/DoomSpriteProjection/SchoolAssets`에 있다. 캐릭터 PNG는 원본 픽셀을 유지하고 **Sprite Editor의 잘라내기 영역**으로 투명 여백을 제외했다. 무당이는 220×316, 무한이는 689×1256 영역이며, 발끝이 영역의 밑변에 닿는다. 밑변 중앙 Pivot과 Base Height = 0으로 설정했다. 이 예제의 투영은 Sprite Pivot 대신 잘라낸 사각형의 밑변 중앙을 기준으로 계산한다.

캐릭터를 옮기거나 추가하려면 Hierarchy의 **School Sprite Map**을 선택한다. 새 캐릭터는 SchoolAssets의 Moodang/Moohan PNG를 펼쳐 나온 **하위 Sprite**를 Placement Sprite에 넣는다. 기존 배치물은 하위 오브젝트를 선택해 이동한다. Scene 뷰의 밝은 초록색 바닥 칸에 놓는다.

벽·문·천장·바닥은 `SchoolCorridorBackground.cs`가 광선과 격자 벽·평면의 교점을 직접 계산하여 그린다. 3D 원근 카메라나 메시를 사용하지 않는다. 캐릭터는 `SpriteProjection.cs`로 원근 투영한다. 맵 크기·타일·천장 높이는 **Map Layout**에서, 배경 해상도는 **Sprite Projection Demo → School Corridor Background → Render Width**에서 조절한다. 가로 480픽셀이 기본이며 높이면 계산량이 증가한다.

맵은 같은 바닥·천장 높이를 사용하는 직교 격자 방식이다. 방·기둥·꺾인 통로, 벽 충돌, 벽 뒤 스프라이트의 부분 가림을 지원한다. 문 열기, 대각선 벽, 계단과 층별 높이 차이는 포함하지 않는다.

## 벽·바닥으로 직접 맵 만들기

1. **Play를 종료**하고 `MapEditorDemo.unity`의 Hierarchy에서 **Map Layout**을 선택한다. 찾기 어려우면 **Tools → DOOM Sprite Map → Edit Walls and Floors**를 누른다. 빈 씬에서도 이 메뉴로 맵과 표시기를 만들 수 있다.
2. **맵 편집 시작**을 누른다. Scene 뷰가 2D 지도로 바뀐다. **Gizmos**를 켠다.
3. **바닥 (방·통로)** 브러시를 선택하고 격자를 클릭·드래그한다. 밝은 초록색 칸이 걸을 수 있는 공간이다. 브러시 크기를 3~5칸으로 키우면 넓은 방을 빨리 만들 수 있다.
4. **벽**으로 막을 곳을 칠한다. 벽이나 문을 없애려면 그 칸을 **바닥**으로 다시 칠한다. **문**은 주황색의 닫힌 문 타일이다.
5. **시작 위치**로 바닥을 클릭한다. 시작 방향은 Inspector의 **시작 방향**에서 정한다. `0`은 지도 위(+Y), `90`은 오른쪽(+X)이다.
6. **Esc → Ctrl+S → Play**로 확인한다. **Ctrl+Z**는 한 번의 드래그 칠하기를 되돌린다.

처음부터 만들려면 **새 맵 크기**를 펼쳐 칸 수·한 칸 크기를 정한 뒤 **이 크기로 새 맵 만들기**를 누른다. 모두 벽으로 시작하므로 바닥을 칠해 방과 통로를 파내는 방식이다. 기존 캐릭터는 유지되므로 새 바닥으로 옮기거나 삭제한다. 이 초기화도 Ctrl+Z로 되돌릴 수 있다. 제작한 맵을 별도로 보관하려면 Unity의 **File → Save As**로 새 씬 이름을 사용한다.

Map Layout에서 벽·문·바닥·천장의 이미지와 천장 높이를 변경할 수 있다. 배경 이미지는 CPU에서 읽기 때문에 Texture Import Settings의 **Read/Write**를 켜야 한다. 제공한 학교 이미지에는 이미 설정되어 있다.

## 맵에 캐릭터 배치하기

Map Layout의 **캐릭터 배치** 버튼으로 아래 도구로 전환한다. 캐릭터 도구의 **벽·바닥 편집** 버튼으로 돌아올 수 있다.

1. **Play를 종료한 편집 모드**에서 Hierarchy의 **Sprite Map**을 선택한다. 찾기 어려우면 **Tools → DOOM Sprite Map → Create or Select Map**을 사용한다.
2. Inspector의 **Placement Sprite**, **Placement Height**, **Placement Base Height**로 새 배치물의 이미지·공간상 높이·밑변 높이를 지정한다. Sprite를 비우면 실행 시 테스트 이미지가 표시된다.
3. 필요하면 **Snap To Grid**를 켜고 **Grid Size**를 정한다. 이 설정은 클릭으로 새로 배치할 때 적용된다.
4. Scene 뷰의 **Gizmos**를 켜고 **배치 시작**을 누른다. XY 평면의 2D 맵으로 바뀌면 바닥 위를 연속 클릭한다. 벽·문 위에는 새 캐릭터가 배치되지 않는다. 플레이어는 청록색 화살표다.
5. **Esc** 또는 **배치 종료**를 누른다. 이후 배치물을 선택해 **W** 이동 도구로 옮기거나 **Ctrl+D**로 복제하고 **Delete**로 삭제한다. **Ctrl+Z**로 배치를 취소할 수 있다.
6. **씬을 저장**하고 **Play**를 눌러 배치 결과를 확인한다. 편집 모드에서 저장한 물체는 다음 실행에도 유지된다.

물체 위치는 Transform의 **월드 XY 좌표**에서 읽는다. 물체의 높이와 크기는 각 `Sprite Map Object`의 **Base Height**, **Object Height**로 설정한다. 물체 Transform의 Z·회전·Scale은 이미지의 투영 높이·방향·크기를 정하는 데 사용하지 않는다. 배치 후 위치 수정은 Unity의 기본 이동 도구를 사용하며, 자체 배치 격자가 이동 도구에 자동 적용되는 것은 아니다.

브러시 설정을 바꿔도 기존 물체는 바뀌지 않는다. 기존 배치물을 수정하려면 해당 물체를 선택해 `Sprite Map Object`의 값을 바꾼다. Play 중 값 변경은 화면에 반영되지만, 실행을 종료하면 편집 모드 값으로 돌아간다.

## 원하는 스프라이트 사용

1. 원하는 PNG를 `Assets`에 넣는다. 배경이 투명한 이미지를 권장한다.
2. PNG Inspector에서 **Texture Type → Sprite (2D and UI)**로 지정한다.
3. 이미지 한 장이면 **Sprite Mode → Single**, 시트라면 **Multiple**로 설정하고 Sprite Editor에서 잘라낸 뒤 **Apply**한다.
4. 새로 배치할 이미지는 `Sprite Map`의 **Placement Sprite**, 기존 물체의 이미지는 해당 `Sprite Map Object`의 **Source Sprite**에 드래그한다. Multiple이면 잘라낸 Sprite 하나를 넣는다.
5. 픽셀 아트는 필요에 따라 **Filter Mode → Point**, **Compression → None**으로 설정한다.

폭은 `objectHeight × sprite.rect.width / sprite.rect.height`로 계산하므로 이미지 교체 후에도 높이는 유지되고 가로세로 비율이 반영된다. 투영 기준점은 항상 이미지 사각형의 **밑변 중앙**이다. 원본 Sprite의 Pivot과 Pixels Per Unit은 투영 계산에 사용하지 않으며, 투명 여백도 이미지 크기에 포함된다.

## 다른 코드에서 사용

`DoomProjection` 네임스페이스의 `SpriteMap` 참조를 `map`, 그 맵을 표시하는 `SpriteProjectionDemo` 참조를 `demo`, 사용할 Sprite를 `mySprite`라고 할 때:

```csharp
SpriteMapObject item = map.AddObject(new Vector2(2f, 6f)); // 현재 브러시 설정으로 추가
item.SetSprite(mySprite);                                // 이 배치물의 이미지 교체
item.SetMapPosition(new Vector2(2f, 6f), 1f);             // 지도 위치와 밑변 높이 지정
item.objectHeight = 2f;                                  // 공간상의 이미지 높이
demo.RefreshMap();                                      // 새 물체의 화면 이미지 생성
```

이미 등록된 배치물은 `item.SetSprite(...)`, `item.SetMapPosition(...)` 또는 Inspector 수정만으로 다음 Update에 반영된다. `item.SetSprite(null)`은 기본 테스트 이미지로 돌아간다. 실행 중 물체를 추가하거나 맵 구성을 바꾼 뒤에는 `demo.RefreshMap()`을 호출해 표시 목록을 다시 만든다. `SetMapPosition()`은 받은 좌표를 그대로 적용하며, 격자 맞춤이 필요하면 `map.SnapPosition(...)`을 사용한다.

기존 단일 물체 버전의 `demo.SetSprite()`·`demo.SetObjectPosition()` 대신 이제 각 배치물의 함수를 사용한다. `demo.ResetView()`는 계속 플레이어를 시작 시점으로 되돌린다. `Vector2`에는 `UnityEngine` 네임스페이스가 필요하다.

## 계산과 배치 흐름

실행 흐름: `입력 → 플레이어 이동·회전 → 각 배치물의 상대 좌표 → 시선 기준 좌표 → 원근 투영 → UI 위치·크기 적용 → 깊이순으로 표시`

편집 흐름: `Scene 클릭 → Unity 기능으로 XY 평면 좌표 구하기 → 격자에 맞추기 → 배치물 생성 → Undo 등록·씬 변경 표시 → 씬 저장 → Play에서 맵 읽기`

맵 구조 편집 흐름: `바닥·벽·문 브러시 선택 → Scene 클릭·드래그 → 격자 셀 값 변경 → Undo 등록·씬 저장 → 실행 시 DDA로 벽 탐색 → 배경 깊이와 스프라이트 깊이 비교`

| 파일 | 역할 |
| --- | --- |
| `SpriteProjection.cs` | `TryProject()`로 화면 사각형·앞방향 깊이·표시 가능 여부 계산 |
| `SpriteProjectionDemo.cs` | 이동·회전 입력, 맵 읽기, 배치물별 투영, UI 표시·깊이 정렬 |
| `SpriteMapObject.cs` | 배치물 하나의 지도 위치·Sprite·밑변 높이·크기 |
| `SpriteMap.cs` | 배치 브러시 설정, 격자 맞춤, 물체 추가·목록 조회 |
| `Editor/SpriteMapEditor.cs` | Inspector 버튼, Scene 클릭 배치, 미리보기, Undo·씬 저장 연동 |
| `ProjectionLevel.cs` | 벽·바닥·문 셀 저장, DDA 광선 검사, 이동 충돌, 시작점 |
| `Editor/ProjectionLevelEditor.cs` | 맵 칠하기·시작점 브러시, 드래그 Undo, 새 맵 생성 |
| `SchoolCorridorBackground.cs` | 맵 배경과 픽셀별 깊이를 직접 계산 |
| `SpriteWorldDepth.shader` | 이미 계산된 깊이를 비교해 벽 뒤 스프라이트 픽셀만 숨김 |

지도는 평면 XY이고 높이는 별도 값이다. `yaw = 0°`는 +Y, `90°`는 +X를 바라본다. 아래 식의 `dx, dy`는 물체 위치에서 플레이어 위치를 뺀 값이며, `θ`는 좌우 시선 각도, `φ`는 상하 시선 각도의 라디안 값이다. 양수 pitch는 위를 본다. 화면 좌표의 원점은 왼쪽 아래다.

```text
horizontal = dx × cosθ − dy × sinθ
flatDepth  = dx × sinθ + dy × cosθ
heightDiff = 물체 밑변 높이 − 눈높이
depth      = flatDepth × cosφ + heightDiff × sinφ
vertical   = heightDiff × cosφ − flatDepth × sinφ
focal      = 화면 너비 / (2 × tan(수평 FOV / 2))
scale      = focal / depth
centerX    = 화면 너비 / 2 + horizontal × scale
bottomY    = 화면 높이 / 2 + vertical × scale
width      = 물체 폭 × scale
height     = 물체 높이 × scale
```

`depth`는 직선거리가 아닌 **시선 앞방향의 깊이**다. 깊이가 0.05보다 작거나 사각형 전체가 화면 밖이면 숨긴다. 일부만 화면에 걸쳐 있으면 표시한다.

학교 복도 배경도 같은 yaw·pitch 회전을 적용한 광선과 벽·바닥·천장의 교점으로 그린다. 수평선만 옮기는 흉내가 아니라 실제 시선 회전을 계산한다. 캐릭터는 발 기준점을 이 시점으로 투영하고 화면을 향하는 2D 사각형으로 표시하므로, 위에서 본 별도 캐릭터 그림이나 3D 모델로 바뀌지는 않는다. pitch = 0이면 기존 수평 시점의 계산과 같다.

## 과제의 두 제작 기능 설명

**기능 1 — 이동·회전에 반응하는 스프라이트 원근 투영**

플레이어와 물체의 위치·높이를 이용해 2D 이미지의 화면 위치와 크기를 직접 계산하는 기능이다. 플레이어가 이동하거나 회전하면 배치된 이미지들이 실시간으로 변하고, 깊이에 따라 확대·축소와 표시 순서가 달라진다.

**기능 2 — Scene 뷰를 이용한 맵 제작 도구**

Scene 뷰의 격자에 바닥·벽·문을 칠해 방과 통로를 만들고, 시작 위치와 캐릭터를 배치해 씬에 저장하는 기능이다. 드래그 칠하기와 실행 취소를 지원하며, 실행 시 편집한 구조가 렌더링과 이동 충돌에 반영된다. 캐릭터는 Unity의 기본 편집 기능으로 이동·복제·삭제할 수 있다.

## 직접 촬영할 영상과 코드 범위

영상은 사용자가 직접 녹화한다. Game 뷰의 크기를 유지하고 마우스·키 입력과 결과가 함께 보이도록 촬영한다.

- **기능 1 영상:** 기본 씬을 Play한 뒤 W/S로 접근·후퇴, A/D로 횡이동, Q/E로 회전하고 R로 복귀한다. 여러 물체의 위치·크기와 겹침 순서가 바뀌는 모습을 보여준다.
- **기능 2 영상:** Map Layout에서 바닥을 드래그해 방과 꺾인 통로를 만들고 벽·문·시작 위치를 지정한다. 캐릭터 배치로 전환해 스프라이트를 놓는다. 씬을 저장하고 Play해 직접 만든 길을 이동하며 벽 충돌과 캐릭터 표시를 보여준다.

코드 캡처는 아래 함수들을 기능별로 나눠 사용한다. 긴 함수는 설명하는 단계의 주석과 코드만 나눠 찍어도 된다.

| 기능 | 캡처할 파일·함수 | 설명할 내용 |
| --- | --- | --- |
| 1 | `SpriteProjection.cs`의 `TryProject()` 앞부분 | 상대 위치를 시선 기준 좌우 좌표·깊이로 변환하고 너무 가까운 물체 제외 |
| 1 | 같은 함수의 원근 투영 부분 | 깊이로 배율 계산, 위치·높이·크기를 화면 사각형으로 변환 |
| 1 | `SpriteProjectionDemo.cs`의 `ReadLook()`, `ReadMovement()`와 `Update()` | 마우스·키보드 입력으로 플레이어 상태를 바꾸고 매 프레임 투영을 갱신 |
| 2 | `Editor/SpriteMapEditor.cs`의 `OnSceneGUI()`와 `PlaceObject()` | Scene 클릭을 배치 좌표로 바꾸고 Undo·씬 변경 상태 등록 |
| 2 | `SpriteMap.cs`의 `SnapPosition()`와 `AddObject()` | 격자 맞춤과 배치물 데이터 생성 |
| 2 | `Editor/ProjectionLevelEditor.cs`의 `OnSceneGUI()`, `PaintAt()`, `PaintCell()` | 클릭·드래그를 격자 좌표로 바꾸고 셀 종류를 저장하며 Undo 등록 |
| 2 | `ProjectionLevel.cs`의 `SetCell()`, `WorldToCell()`, `CellCenter()` | 벽·바닥·문 데이터와 지도 좌표의 관계 |
| 1·2 연결 | `ProjectionLevel.cs`의 `Raycast()`, `Move()`와 `SchoolCorridorBackground.cs`의 `RenderRoom()` | 제작한 맵을 실행 화면과 이동 충돌에 반영 |
| 1·2 연결 | `SpriteProjectionDemo.cs`의 `RefreshMap()`와 `UpdateProjection()` | 배치물을 읽어 UI 이미지를 만들고 각각 투영·깊이 정렬해 표시 |

## 원작과의 관계 및 범위

참고한 공식 공개 코드는 id Software의 **Linux DOOM 1.10**이다. 2024 개선판의 코드나 엔진 전체를 재현한 것은 아니다.

이번 상하 시점은 원작의 기능에 추가한 확장이다. 원작은 수평 시점을 전제로 벽을 세로줄 단위로 그리고, 스프라이트의 깊이는 평면 좌표에서 구한 뒤 높이를 별도로 화면 좌표로 바꾼다. 이 구조는 계산을 단순하게 하지만, 실제 상하 회전을 지원하려면 높이가 섞인 깊이 계산과 벽의 투영·가림 처리 등을 바꿔야 한다. 소스 구조에서 판단할 때 수평 시점 제약은 계산 비용을 줄이는 데 유리한 설계다. 높이 정보가 없어서 원천적으로 불가능했던 것은 아니다. [벽 렌더링](https://github.com/id-Software/DOOM/blob/master/linuxdoom-1.10/r_segs.c#L187), [스프라이트 투영](https://github.com/id-Software/DOOM/blob/master/linuxdoom-1.10/r_things.c#L419)

원작은 적의 높이에 맞춰 총알의 상하 발사 방향을 자동 보정하므로, 위아래를 직접 조준하지 않아도 높은 곳과 낮은 곳의 적을 공격할 수 있다. [자동 조준 코드](https://github.com/id-Software/DOOM/blob/master/linuxdoom-1.10/p_pspr.c#L564)

- [R_ProjectSprite: 상대 좌표, 깊이, 배율, 좌우 경계](https://github.com/id-Software/DOOM/blob/master/linuxdoom-1.10/r_things.c#L420)
- [R_DrawVisSprite: 높이를 화면 좌표로 변환](https://github.com/id-Software/DOOM/blob/master/linuxdoom-1.10/r_things.c#L393)
- [R_ExecuteSetViewSize: 화면 중심과 투영값](https://github.com/id-Software/DOOM/blob/master/linuxdoom-1.10/r_main.c#L653)
- [Unity Editor.OnSceneGUI: Scene 뷰 편집 처리](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Editor.OnSceneGUI.html)

원작의 깊이에 따른 확대·축소 원리를 적용하되, 고정소수점 계산을 float와 Unity UI로 단순화했다. 맵 구조는 편집하기 쉬운 직교 격자이며 DDA로 벽을 찾는다. 원작 DOOM의 BSP·섹터 시스템이나 8방향 스프라이트 전환을 재현한 것은 아니다. 기본 SampleScene의 바닥·천장 색과 수평선은 시각적 기준용 배경이다. 편집 가능한 맵에서는 원과 벽 사각형으로 이동 충돌을 검사하고 픽셀별 깊이로 벽 뒤 스프라이트를 가린다.

맵 제작 도구는 이 투영 예제의 벽·바닥·문과 캐릭터를 편집하는 **Unity 에디터 확장**이다. 클릭 좌표 변환, 선택·이동·복제·삭제, Undo와 씬 저장에 Unity 기능을 사용한다.

## 검증 기록

2026-11-08, Unity 6000.3.10f1에서 개발 단계별로 아래 검증을 통과했다. 격자 맵을 추가한 현재 버전은 마지막 세 항목(총 79개)으로 데이터·실제 편집 입력·실행 화면을 확인했다.

- 투영 수학: 16개 검증. `Logs/SpriteProjectionChecks.txt`
- 실제 Play Mode: 38개 검증. 입력, 이미지 교체, 다중 투영, 깊이 정렬, 비활성·삭제·목록 갱신. `Logs/SpriteProjectionPlayChecks.txt`
- 맵 편집 데이터: 22개 검증. 브러시·격자, Undo/Redo, 이동·복제·삭제, 씬 저장·재열기. `Logs/SpriteMapEditorChecks.txt`
- 실제 Scene GUI 이벤트: 15개 검증. 클릭 좌표, 연속 배치, 선택 유지, Undo, Esc 후 배치 종료. `Logs/SpriteMapSceneChecks.txt`
- 상하 시점 추가 단계의 학교 복도 실제 Play Mode: 47개 검증. 크롭·발 위치, 속도 5.6, 마우스 클릭·좌우/상하 이동·Esc 해제, ±89도 제한과 R 복원. `Logs/SchoolHallwayPlayChecks.txt`. 마우스 잠금은 일반 Unity 편집기 실행에서 확인했다.
- 격자 맵 데이터: 37개 검증. DDA와 독립 교점 비교, 원형 충돌·벽 슬라이딩·빠른 이동, 셀 편집·시작점 Undo/Redo, 씬 저장·재열기. `Logs/ProjectionLevelChecks.txt`
- 실제 맵 편집 GUI: 23개 검증. 클릭·드래그 칠하기, 벽·문·시작점, 스트로크 Undo/Redo, Alt 이동, Esc 종료. `Logs/ProjectionLevelSceneChecks.txt`
- 실제 맵 실행·GPU 화면: 19개 검증. 편집 시 배경 갱신, 실제 이동 충돌, ±89도, 전체 벽 가림·절반 벽 가림·상하 시점의 깊이 방향. `Logs/ProjectionLevelPlayChecks.txt`

현재 격자 맵은 `Logs/MapEditorDemo-preview.png`의 실제 실행 화면으로 확인했다. `Logs/LevelMap-open.png`, `LevelMap-blocked.png`, `LevelMap-partial.png`에서는 검증용 단색 스프라이트의 실제 픽셀을 읽어 전체·부분 가림을 확인했다. 검증용 스프라이트는 실행 중에만 사용하며 제작 씬에는 저장하지 않았다. 안내 텍스트 없이 무당이·무한이를 각각 1개씩 표시한다. 이미지 가져오기·크롭 영역·초기 배치 결과는 `Logs/SchoolHallwaySetup.txt`에 기록했다.

기본 맵·접근·이미지 교체의 Play 화면도 캡처해 확인했다. `Editor` 폴더의 `*Verification.cs`는 검증용이며 보고서에서 구현 원리를 설명하기 위한 코드 캡처 대상은 아니다. 과제에 첨부할 두 영상은 위 촬영 순서에 따라 직접 녹화한다.
