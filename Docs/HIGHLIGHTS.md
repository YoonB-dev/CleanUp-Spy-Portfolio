# 주요 작업

멀티플레이 마피아 게임에서 제가 맡은 작업 중 먼저 봐 주셨으면 하는 세 가지입니다.
각 항목은 **문제 → 판단 → 대안 → 한계** 순서로 적었습니다.

---

## 1. 페인트·오염도 시스템

마피아가 벽과 바닥을 칠하고 시민이 지우며, 칠해진 양이 오염도 점수가 되는 시스템입니다.

### 문제

첫 구현은 레이가 맞은 지점의 UV 좌표에 브러시를 찍는 방식이었고, 두 가지 문제가 있었습니다.

- **표면마다 자국 크기가 달랐습니다.** 클라이언트가 `hit.textureCoord`(UV)를 보내고 브러시 반경도 UV 단위였기 때문에, 같은 반경이라도 UV가 넓게 펼쳐진 메시와 좁게 펼쳐진 메시에서 실제 크기가 달라졌습니다. 메시 크기와 텍스처 타일링으로 반경을 보정하고 0.001~0.5로 잘라 썼지만, 메시마다 UV 배치가 달라 근사에 그쳤습니다. `textureCoord`는 볼록하지 않은 MeshCollider에서만 값이 나와 콜라이더 설정에도 제약이 있었습니다.
- **점수가 공정하지 않았습니다.** 오염도를 표면별 칠해진 픽셀 비율의 평균으로 계산해서, 넓은 바닥은 많이 칠해도 비율이 거의 오르지 않고 작은 기둥은 조금만 칠해도 크게 올랐습니다.

### 판단

- **브러시를 월드 좌표 기준으로 바꿨습니다.** 표면 메시를 UV 위치에 펼쳐 렌더 텍스처에 그리고, 각 픽셀이 메시의 어느 월드 지점인지 알 수 있으므로 셰이더가 브러시 중심과의 월드 거리로 칠할지 정합니다. 어떤 메시든 반경(미터)이 같으면 같은 크기 자국이 나고, 모서리에 찍으면 양쪽 면에 걸쳐 칠해지며, 콜라이더 종류와도 무관해졌습니다. ([DrawAt](../Scripts/Item/PaintGun/PaintSurfaceManager.cs#L149), [PaintBrush.shader Pass 0](../Shaders/Paint/PaintBrush.shader#L49))
- **점수를 실제 칠해진 면적(m²)으로 바꿨습니다.** 칠해진 픽셀 수에 "픽셀 하나가 덮는 월드 면적"을 곱합니다. 이 값은 표면을 등록할 때 GPU로 한 번 측정합니다. 같은 방식으로 메시를 펼쳐 그리면서 `ddx`/`ddy`로 옆·아래 픽셀과의 월드 위치 차이를 구하고 두 벡터의 외적 크기를 기록합니다. 빌드에서는 메시 Read/Write가 꺼져 있어 스크립트로 정점을 읽을 수 없기 때문입니다. ([MeasureAreaPerPixel](../Scripts/Item/PaintGun/PaintSurfaceManager.cs#L79), [ScoreManager](../Scripts/Manager/ScoreManager.cs#L297), [PaintBrush.shader Pass 1](../Shaders/Paint/PaintBrush.shader#L77))
- **네트워크로는 텍스처가 아니라 그리기 명령만 보냅니다.** 클라이언트는 표면 ID와 맞은 지점만 보내고, 서버는 역할·행동 가능 여부·게이지·발사 간격·사거리를 확인한 뒤 서버에 설정된 반경으로 모든 클라이언트에 전파합니다. 각 클라이언트는 자기 텍스처에 같은 셰이더로 그립니다. ([RequestPaintServerRpc](../Scripts/Player/Mafia/MafiaPaintAction.cs#L302), [RequestCleanServerRpc](../Scripts/Item/PaintGun/PaintCleaner.cs#L93))
- **점수용 집계는 서버에서만 합니다.** `AsyncGPUReadback`으로 마스크를 읽어 칠해진 픽셀을 세고, 같은 표면에 읽기 요청이 진행 중이면 새 요청을 보내지 않아 연사 중에도 전체 픽셀 순회가 겹쳐 쌓이지 않습니다. ([RequestContaminationReadback](../Scripts/Item/PaintGun/PaintSurfaceManager.cs#L194))
- 원본 머테리얼은 건드리지 않고 같은 메시의 투명 오버레이를 자동으로 만들며, 마스크 해상도는 표면 크기에 따라 512~4096으로 정합니다. ([CreateOverlayRenderer](../Scripts/Item/PaintGun/PaintableSurface.cs#L96), [PaintOverlayURP.shader](../Shaders/Paint/PaintOverlayURP.shader), [CalculateDynamicResolution](../Scripts/Item/PaintGun/PaintableSurface.cs#L68))
- 표면의 네트워크 식별자(Surface ID)는 에디터 메뉴 하나로 씬 전체를 다시 매깁니다. ([ReassignAllSurfaceIds](../Scripts/Editor/PaintableSurfaceEditor.cs#L25))

### 대안

- **UV 방식의 보정식을 계속 다듬기:** 보정값은 메시 하나에 하나인데, UV가 펼쳐진 비율은 같은 메시 안에서도 UV 조각마다 다릅니다. 숫자 하나로는 한 메시의 넓은 면과 좁은 면을 동시에 맞출 수 없어서, 보정 대신 UV 배치와 무관한 월드 거리 판정으로 바꿨습니다.
- **칠해진 텍스처 자체를 동기화:** 표면 하나의 마스크가 512²~4096²(R8 기준 256KB~16MB)라, 칠할 때마다 주고받기에는 데이터가 너무 많습니다.
- **픽셀당 면적을 C#에서 메시 삼각형으로 계산:** 빌드에서 스크립트로 정점을 읽으려면 메시의 Read/Write를 켜야 하고, 그러면 칠할 수 있는 모든 벽·바닥 메시가 CPU 메모리에도 한 벌씩 더 남습니다. 대신 GPU가 메시를 그리면서 픽셀마다 면적을 계산하게 했습니다.

### 한계

- **집계 비용을 아직 측정하지 못했습니다.** 집계 콜백은 표면 마스크 전체(최대 4096², 약 1,677만 픽셀)를 메인 스레드에서 순회합니다. Profiler로 표면 크기별 비용을 재는 것이 다음 작업입니다.
- 읽기 요청이 진행 중일 때 들어온 칠하기는 집계를 다시 요청하지 않아서, 연사를 멈춘 직후 마지막 몇 획이 다음 칠하기 전까지 점수에 반영되지 않을 수 있습니다.
- 마스크는 R8인데 RGBA32로 읽어 GPU→CPU 전송량이 필요보다 4배 큽니다.
- Surface ID는 오브젝트 이름순으로 매기고 수동으로 실행합니다. 이름을 바꾸거나 표면을 추가하면 ID가 밀리고, 다시 실행하지 않으면 런타임에 중복 경고만 남습니다.
- 그리기 기록을 저장하지 않으므로 게임 도중 들어온 클라이언트는 이전 칠을 볼 수 없습니다. 현재는 접속 단계에서 게임 중 입장을 거부합니다(아래 "그 밖의 문제 해결").

---

## 2. 필요한 만큼만 맞추는 동기화

협동 위주의 파티 게임이라 모든 클라이언트 화면이 프레임 단위로 똑같을 필요는 없다고 보고, 동기화 정도를 필요에 따라 나눴습니다.

- **게임 판정에 쓰이는 값**(점수, 아이템 소유, 역할, 페인트 게이지 등)은 서버가 정하고, 클라이언트 요청은 서버가 다시 검증합니다.
- **화면에 보이기만 하는 것**은 위치를 계속 보내지 않습니다. "들었다", "놓았다", "맞았다"처럼 일어난 일만 한 번 알리고, 그다음은 각 클라이언트가 이미 맞춰져 있는 정보로 알아서 그립니다.

### 문제

모든 것을 매 프레임 동기화하면 주고받는 데이터가 계속 늘고, 따로 동기화되는 값끼리 서로 어긋납니다. 예를 들어 손에 든 아이템은 래그돌 손을 따라 움직이는데, 래그돌 포즈와 아이템 위치를 각각 보내면 두 값이 따로 도착해 아이템이 손과 어긋날 수 있습니다.

### 판단

- **손에 든 아이템: "들었다"와 "놓았다"만 알립니다.** ([PickupItem](../Scripts/Item/PickupItem.cs#L112))
  - 주우면 서버가 소유권을 넘기고 누가 들었는지만 알립니다. 각 클라이언트는 아이템을 그 플레이어의 손 앵커에 직접 붙이고, 들고 있는 동안에는 아이템 위치를 주고받지 않습니다(NGO 자동 부모 동기화와 `NetworkTransform`을 끔). ([L84](../Scripts/Item/PickupItem.cs#L84), [AttachToHolderCoroutine](../Scripts/Item/PickupItem.cs#L163))
  - 놓거나 던질 때는 던진 사람의 손 앞 좌표에 아이템을 고정하고, 물리를 먼저 켠 뒤 **1프레임 기다렸다가** `NetworkTransform`을 다시 켜고 서버가 `Teleport`로 위치를 확정합니다. `NetworkTransform`을 바로 켜면 들기 전 위치로 되돌아가 순간이동처럼 보이던 문제를 이 순서로 해결했습니다. ([DetachRoutine](../Scripts/Item/PickupItem.cs#L246))
- **페인트: "어디를 칠했다"만 알립니다.** 텍스처 대신 표면 ID와 지점만 보냅니다(1번).
- **테이저 탄 박힘: "어느 뼈의 어디에 박혔다"만 알립니다.** 서버가 맞은 지점에서 가장 가까운 래그돌 뼈와 그 뼈 기준 위치·회전을 한 번 계산해 보내고, 각 클라이언트는 네트워크 오브젝트가 아닌 시각용 탄을 그 뼈에 붙입니다. 뼈는 래그돌 포즈 동기화로 이미 움직이므로 탄도 따라 움직입니다. ([AttachServer](../Scripts/Item/TaserGun/PlayerTaserStuck.cs#L57), [AttachClientRpc](../Scripts/Item/TaserGun/PlayerTaserStuck.cs#L169))
- **발소리: 아무것도 보내지 않습니다.** 각 클라이언트가 이미 동기화된 플레이어 위치로 이동 거리를 재서, 일정 거리마다 발밑 표면에 맞는 소리를 냅니다. ([PlayerFootsteps.Update](../Scripts/Sound/Footstep/PlayerFootsteps.cs#L86))

### 대안

- **보이는 것까지 모두 계속 동기화하기:** 손에 든 아이템에 `NetworkTransform`을 켜 두고, 박힌 탄을 네트워크 오브젝트로 만들고, 발소리를 RPC로 알리는 방식입니다. 주고받는 데이터가 늘고, 아이템처럼 다른 동기화 값(래그돌 손)을 따라가야 하는 것은 오히려 어긋납니다.
- **NGO 자동 부모 동기화로 아이템을 손에 붙이기:** NetworkObject는 다른 NetworkObject 아래로만 부모를 바꿀 수 있는데, 손 앵커는 래그돌 안의 일반 Transform이라 쓸 수 없습니다.

### 한계

- 들고 있는 아이템의 위치는 클라이언트마다 각자 계산하므로 래그돌 포즈 보간 차이만큼 조금씩 다를 수 있습니다. 판정에는 쓰이지 않는 위치라 허용했습니다.
- 던질 때 클라이언트가 먼저 힘을 줘 바로 날아가 보이고, 이후 서버 위치로 맞춰지므로 짧은 위치 보정이 생길 수 있습니다.
- 박힌 탄은 ClientRpc 한 번으로 전달하므로 그 뒤에 들어온 클라이언트는 받지 못합니다(게임 중 입장은 거부).
- 던지기 힘을 정하는 "누른 시간"은 클라이언트 값을 받습니다. 서버가 최대 힘으로 제한하지만, 충전 없이 최대 힘으로 던지는 요청은 막지 못합니다.

---

## 3. 동작 단위로 나눈 아이템 인터페이스

아이템은 테이저건, 흡입기, 폴라로이드 카메라, 상자, 쓰레기처럼 여러 종류지만, 플레이어가 아이템에 하는 동작은 줍기·놓기, 사용, 조준, 촬영 정도로 정해져 있습니다. 그래서 아이템 종류가 아니라 **동작 단위로 인터페이스를 나눴습니다.**

### 문제

처음에는 플레이어 입력 코드(`PlayerInteraction`)가 아이템 종류를 직접 알았습니다. 예를 들어 흡입기 전용 입력 함수, 지금 켜져 있는 흡입기 참조, 흡입기를 켜고 끄는 ServerRpc가 모두 `PlayerInteraction` 안에 있었고, 서버는 "요청한 오브젝트가 지금 들고 있는 아이템인지"를 직접 비교했습니다. 도구가 하나 늘 때마다 입력 함수와 RPC, 검증 코드가 플레이어 코드에 함께 늘어나는 구조였습니다.

### 판단

- **동작마다 인터페이스를 두고, 아이템은 자기가 할 수 있는 동작만 구현합니다.** ([PickupContracts](../Scripts/Item/Interface/PickupContracts.cs), [IUsableItem](../Scripts/Item/Interface/IUsableItem.cs))

  | 동작 | 인터페이스 | 구현한 아이템 |
  |---|---|---|
  | 사용 (좌클릭) | `IUsableItem` | 테이저건, 흡입기, 청소 도구 |
  | 조준 | `IZoomTool` | 조준 가능한 아이템 공용 컴포넌트(`ZoomableItem`) |
  | 촬영 | `ICaptureTool` | 폴라로이드 카메라 |
  | 줍기·놓기 때의 반응 | `IPickupListener` | 상자, 폴라로이드 카메라, `ZoomableItem` |

- **플레이어 입력 코드는 인터페이스만 봅니다.** 들고 있는 아이템이 해당 인터페이스를 구현했는지 확인하고 호출할 뿐이라, 흡입기 코드는 `PlayerInteraction`에서 빠져 흡입기 자신에게 들어갔습니다(`feat: IUsableItem 인터페이스 공통화`). 나중에 청소 도구와 카메라 촬영도 같은 방식으로 옮겨서, 지금 `PlayerInteraction`은 도구 클래스를 하나도 직접 참조하지 않습니다. ([OnUseItem](../Scripts/Player/PlayerInteraction.cs#L608), [OnCapture](../Scripts/Player/PlayerInteraction.cs#L528), [MagnetAttractor.OnUse](../Scripts/Item/MagnetAttractor.cs#L81), [PaintCleaner.OnUse](../Scripts/Item/PaintGun/PaintCleaner.cs#L69))
- **네트워크 처리도 아이템이 직접 갖습니다.** 주우면 서버가 든 사람에게 소유권을 넘기고(`ChangeOwnership`) 놓으면 회수합니다. NGO의 ServerRpc는 소유자만 호출할 수 있으므로, 아이템 자신의 ServerRpc는 지금 든 사람만 부를 수 있습니다. 그래서 플레이어 코드가 "요청한 것이 든 아이템인지" 비교하지 않아도 됩니다. ([PickupItem](../Scripts/Item/PickupItem.cs#L123), [MagnetAttractor.SetMagnetStateServerRpc](../Scripts/Item/MagnetAttractor.cs#L93))
- 쓰레기통 같은 공용 시스템도 상자나 도구 클래스를 모르고, 아이템의 분류 값(`PickupCategory`)만 봅니다. ([TrashCan](../Scripts/Item/TrashCan/TrashCan.cs#L40))

### 대안

- **공통 부모 클래스 상속:** 폴라로이드 카메라처럼 촬영과 줍기 반응을 함께 가진 아이템도 있고, 사용만 하는 아이템도 있습니다. C#은 클래스를 여러 개 상속할 수 없어서 한 줄 상속 계층으로는 이런 조합을 나타내기 어렵기 때문에, 동작별 인터페이스를 골라 붙이는 쪽을 택했습니다.

### 한계

- 상자 배치는 상자만 하는 동작이라 인터페이스로 나누지 않고 `PlaceableBox`를 직접 찾습니다. ([TryPlaceBoxServerRpc](../Scripts/Player/PlayerInteraction.cs#L433))
- **아이템마다 서버 검증 수준이 다릅니다.** 청소 도구는 요청한 사람이 든 사람인지 명시적으로 확인하지만, 테이저건은 소유자 검사에 맡기고 탄 생성 위치도 클라이언트 값을 씁니다. ([RequestFireServerRpc](../Scripts/Item/TaserGun/TaserGun.cs#L89))
- 줍거나 놓은 직후 소유권이 아직 넘어오지 않은 짧은 순간에는 요청이 거부되므로, 클라이언트가 그 순간엔 보내지 않도록 막고 있습니다. ([ZoomableItem](../Scripts/Item/Interface/ZoomableItem.cs#L70))

---

## 그 밖의 문제 해결

- **파쇄기가 클라이언트에서 끌리지 않던 문제** (`fix: draggable 오브젝트 권한 오류 수정`): 끌기 오브젝트는 줍지 않으므로 소유권이 서버에 있는데, 클라이언트가 그 오브젝트의 ServerRpc를 직접 부르고 끄는 사람 ID도 직접 보내고 있었습니다. ServerRpc는 소유자만 호출할 수 있어 호스트에서만 끌렸습니다. 요청을 플레이어가 소유한 `PlayerInteraction`의 ServerRpc로 받아 서버가 대상의 로직을 호출하도록 바꿨고, 끄는 사람 ID도 요청을 받은 플레이어 오브젝트의 ID를 씁니다. 서버는 행동 가능 여부와 거리(큰 오브젝트라 콜라이더 경계 기준)를 다시 확인하고, 끄는 동안의 위치(계단 오르내림, 낭떠러지 낙하)도 서버가 계산합니다. ([RequestStartDragServerRpc](../Scripts/Player/PlayerInteraction.cs#L228), [DraggableObject.FixedUpdate](../Scripts/Item/Interface/DraggableObject.cs#L173))

- **게임 시작 시 캐릭터가 원점에 보였다가 이동하던 문제** (`fix: Player 생성 동기화 이슈 해결`): 호스트는 즉시, 클라이언트는 각자 씬 로드를 마친 순간 개별로 스폰하면서 캐릭터를 원점에 만든 뒤 옮기고 있었습니다. 캐릭터를 처음부터 스폰 위치에 생성하고, 방 인원 전원이 씬 로드를 마치면 한 번에 스폰한 뒤 타이머를 시작하도록 바꿨습니다. 로딩 중 이탈하면 남은 인원으로 다시 검사하고, 일괄 스폰은 한 번만 합니다. 씬 로드를 끝내지 못한 채 연결만 유지되는 클라이언트를 기다리는 타임아웃은 아직 없습니다. ([CheckAndSpawnAllPlayers](../Scripts/Manager/InGameManager.cs#L105), [SpawnPlayerCharacter](../Scripts/Manager/InGameManager.cs#L144))
- **게임 중 입장 거부**: 접속 승인 단계에서 이미 게임이 시작된 방과 정원이 찬 방은 거부합니다. 페인트 그리기 기록이나 박힌 탄처럼 한 번만 전파하는 정보를 중간 입장자에게 다시 보내지 않아도 되는 전제가 됩니다. ([ApprovalCheck](../Scripts/Network/NetworkConnect.cs#L109))
- **폴라로이드 카메라 뷰파인더가 모두 같은 화면을 보여 주던 문제**: 렌더 텍스처 에셋 하나를 모든 카메라가 같이 쓰고 있었습니다. 설정만 복사해 카메라마다 전용 렌더 텍스처를 만들도록 바꿨습니다(`fix: 카메라 RT 복사본으로 사용하게 수정`). ([PolaroidCamera](../Scripts/PolaroidCamera/PolaroidCamera.cs#L61))
