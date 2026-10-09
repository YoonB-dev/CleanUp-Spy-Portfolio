# 작성자 구분 (Authorship)

이 레포의 `Scripts/`는 팀 프로젝트의 스크립트 폴더를 그대로 가져온 것입니다(`git subtree`). 팀원의 코드도 함께 들어 있으므로, 어떤 코드를 제가 작성했는지 이 문서에 구분해 둡니다.

- 팀 구성: 클라이언트 프로그래머 2명(이윤형, 김도환), 모델러 1명
- 기준 시점: 메인 레포 split 커밋 `c8a2c15`

## 구분 방법

- 모든 `.cs` 파일에 `git blame -w -M -C`를 실행해 줄 단위 작성자를 확인했습니다. 공백만 바뀐 줄과 다른 위치·파일에서 옮겨 온 줄은 원래 작성자로 계산됩니다.
- GitHub squash merge 커밋은 PR 작성자 한 명에게 몰리므로, 커밋 본문에 남은 원본 커밋 목록을 원본 커밋의 작성자와 대조했습니다.
- 파일을 처음 만든 사람을 기준으로 나누고, 상대방이 추가한 부분은 기능 단위로 따로 적었습니다.
- 현재 코드에 제가 쓴 줄이 없거나, 남아 있는 줄이 `using` 선언이나 필드 몇 줄뿐인 파일은 **제가 작성하지 않은 파일**로 분류했습니다.

## 1. 제가 만든 파일

아래 파일들은 제가 만들고 작성했습니다. 팀원(김도환)이 기능을 추가한 파일은 그 부분을 오른쪽에 적었습니다.

팀원(김도환)이 행동 게이트 통합(#33)과 플레이 통계(#52)를 작업하면서 여러 파일에 연결 코드를 몇 줄씩 넣었습니다(`_gate.CanDo(...)` 검사, 통계 카운트 증가 등). 이런 한두 줄짜리 연결 코드는 표에 따로 적지 않았습니다.

| 시스템 | 파일 | 팀원(김도환)이 추가한 부분 |
|---|---|---|
| 페인트·청소 | `Item/PaintGun/PaintableSurface.cs`, `PaintSurfaceManager.cs`, `PaintCleaner.cs`, `Player/Mafia/MafiaPaintAction.cs`, `UI/PaintGaugeUI.cs` | |
| 에디터 툴 | `Editor/PaintableSurfaceEditor.cs` (Surface ID 정리), `AI/ComfyUIController.cs`, `AI/AIAssetGenerator.cs` (AI 에셋 생성) | |
| 아이템 공통 구조 | `Item/Interface/` (`IUsableItem`, `PickupContracts`, `ZoomableItem`, `DraggableObject`, `CarryGripPoints`), `Item/PickupItem.cs`, `Item/PickupHighlight.cs`, `Item/ItemData.cs` | |
| 상호작용·인벤토리 | `Player/PlayerInteraction.cs`, `Player/PlayerInventory.cs`, `InventoryUIController.cs`, `Player/ThrowGaugeUI.cs` | |
| 도구 아이템 | `Item/TaserGun/` (`TaserGun`, `TaserBullet`, `PlayerTaserStuck`), `Item/MagnetAttractor.cs` (흡입기), `PolaroidCamera/` (`PolaroidCamera`, `Photo`) | |
| 쓰레기·배치 | `Trash/` (`TrashData`, `TrashObject`), `Item/TrashCan/TrashCan.cs`, `Player/Mafia/MafiaActionTrash.cs`, `Item/PlaceableBox.cs`, `BoxPlacementPreview.cs`, `PlacementValidator.cs`, `PlacementZone.cs` | |
| 점수·역할 | `Manager/ScoreManager.cs`, `Manager/GameTimerManager.cs`, `Player/PlayerRole/` (`PlayerRole`, `RoleManager`, `RoleAssignmentManager`, `RoleNameTag`), `Player/Manager/PlayerSpawnManager.cs` | |
| | `Player/PlayerData.cs` | Steam 닉네임 동기화, 플레이 통계 변수 |
| 매치 진행 | `Manager/InGameManager.cs` | 제한 시간 종료 시 승패 판정·결과 표시·전적 저장·로비 복귀 (`EndGame`, `GameOverClientRpc`, `RecordResult`, `ReturnToLobby`) |
| | `Manager/InGameUIController.cs` | 역할 스플래시 연출, 결과 화면 (`ShowResult`) |
| 로비·방 설정 | `RoomSetting/PlayerReady.cs`, `RoomMenuController.cs`, `RoomPlayerInfo.cs` | |
| | `RoomSetting/RoomSettings.cs` | 방 코드 공유, 플레이어 이름 설정 |
| | `RoomSetting/RoomUIController.cs` | 대기방 UI 개편 (빈 자리 표시, 준비 배지, 키 입력으로 준비 토글, 입력 필드) |
| | `RoomSetting/LobbyFlowManager.cs` | `RoomSettings`를 씬에 두지 않고 한 번만 스폰하도록 변경 |
| | `RoomSetting/RoomPlayerEntryUI.cs` | 빈 자리 표시 (`SetEmpty`) |
| 네트워크 연결 | `Network/NetworkConnect.cs` | 메인 화면 호스트 시작과 초대 코드 접속 (`StartHosting`, `TryJoin`), 씬 재진입 시 UI 인계, 방 코드 생성 |
| | `Network/NetworkConnectTest.cs` (테스트용) | |
| 이동·시점 | `Player/PlayerMovement.cs` | 래그돌 소유 등록, 붙잡힌 상태 이동, 점프 버퍼·코요테 타임·낙하 가속, 채팅 중 입력 비우기 |
| | `Player/FirstPersonLook.cs` | 시점 회전 계산과 서버 전송, 목 기준 카메라 공전, 감도·Y축 반전·시야각 설정 적용, 1인칭 자기 몸 레이어 숨김 |
| 사운드 | `Sound/` 전체 (`SoundManager`, `BgmPlayer`, `SfxPlayer`, `SoundData`, `SceneBgm`, `ImpactSound`, `Footstep/`, `UI/`) | |
| 기타 | `Light/LightSwitch.cs`, `Player/LightInteraction.cs` (두꺼비집), `Generic/Singleton.cs`, `SceneSingleton.cs`, `PersistentSingleton.cs`, `UI/UIPropertyGauge.cs` | |

팀원(김도환)이 추가한 부분이 큰 파일에서 제가 작성한 부분은 다음과 같습니다.
- `InGameManager.cs`: 모든 클라이언트가 인게임 씬을 다 불러온 뒤 플레이어를 한꺼번에 스폰, 로딩 중 이탈한 클라이언트 처리, 타이머 시작
- `InGameUIController.cs`: 남은 시간 표시, 준비 → 시작 단계에 맞춘 역할 공개 패널
- `NetworkConnect.cs`: 연결 승인(게임 중 입장 거부, 정원 초과 거부), 연결 끊김 처리와 UI 복구, Steam Relay 접속 대상 설정
- `PlayerMovement.cs`: 서버 권위 이동 구조(입력 ServerRpc → 서버에서 이동·점프 처리), 테이저 감전 둔화, 끌기 상태, UI가 열렸을 때 이동 입력 차단
- `FirstPersonLook.cs`: 1인칭 카메라 기본 구조(소유자만 카메라·오디오 활성), 다른 클라이언트에 시선 상하 각도 복제(들기 자세용), 조준 시 Near Clip 전환

## 2. 팀원(김도환)이 만든 파일에 제가 추가한 부분

아래 파일들의 구조와 대부분의 코드는 팀원(김도환)이 작성했습니다. 제가 작성한 부분은 오른쪽에 적은 것뿐입니다.

| 파일 | 제가 작업한 부분 |
|---|---|
| `Player/Ragdoll/RagdollPoser.cs` | 양손 들기 자세: 양손 사이 `CarryAnchor` 생성과 시선을 따라가는 매 프레임 갱신, 물건 그립 폭에 맞춘 팔 벌림 보정(`BuildCarryOffset`), 들기 중 전용 관절 스프링 블렌딩, 외부 API(`SetCarryRequested`, `SetCarryTarget`, `SetCarryAnchorOffset`). 시선 상하 각도에 따른 가슴 젖힘 비율과 상하 각도 제한 |
| `Player/Ragdoll/RagdollDriver.cs` | 들기 대상의 그립 폭 계산 (`SetCarryTarget`, `TryGetCarryHalfWidth`), 시선 상하에 따른 골반 앵커 기울임 |
| `Player/Ragdoll/RagdollNetworkSync.cs` | 외부에서 래그돌에 접근하는 조회 API (`Poser`, `BoneCount`, `GetBone`) — 들기 자세와 테이저 박힘 연출에서 사용 |
| `Player/PlayerActionGate.cs` | 끌기(Drag) 규칙과 상태 판정, UI가 열렸을 때 이동·시점을 포함한 모든 행동 차단 (`SetUIOpen`) |
| `Player/PlayerPunch.cs` | 휘두르기·타격 사운드: 서버가 타격을 판정한 뒤 ClientRpc로 재생, 맞은 물체별 소리 선택 |

## 3. 제가 작성하지 않은 파일

아래 파일에는 제가 작성한 코드가 없습니다. 프로젝트 구조를 이해하는 데 필요해 함께 두었습니다. 모두 팀원이 작성했습니다.

| 시스템 | 파일 |
|---|---|
| 액티브 래그돌 | `Player/Ragdoll/` 중 `QuaternionCompression`, `RagdollAnimationRig`, `RagdollGroundContact`, `IgnoreRagdollCollision`, `ConfigurableJointExtensions`, `Editor/ActiveRagdollTools`, `Editor/RagdollTuner` / `Editor/RagdollTransplant.cs` |
| 전투·물리 상호작용 | `Player/PlayerGrab.cs`, `PlayerKnockdown.cs`, `PlayerDive.cs` |
| 설정·메인 화면 | `Settings/` 전체, `Network/InviteCode.cs`, `CodeInputField.cs`, `JoinRoomModal.cs`, `Generic/ClickCatcher.cs` |
| Steam | `Network/SteamManager.cs` — 파일은 제가 처음 만들었지만 팀원이 다시 작성해서 지금은 제 코드가 남아 있지 않습니다 |
| 채팅·음성 | `Chat/` (`ChatManager`, `ChatUIController`), `Voice/PlayerVoice.cs` |
| 저장·기록 | `Save/` (`PlayerSave`, `PlayerSaveData`), `UI/RecordsModal.cs`, `UI/RecordRow.cs` |

## 직접 확인하는 방법

```bash
# 파일별 작성자 줄 수
git blame -w -M -C --line-porcelain Scripts/<경로> | grep '^author ' | sort | uniq -c
```

- subtree 특성상 이 레포에서 `git log -- Scripts/<경로>`를 실행하면 add 커밋 하나만 보입니다. 줄 단위 작성자는 `git blame`으로 확인할 수 있습니다.
- 채팅·음성 PR(#36, #38)의 squash 커밋은 GitHub 머지 과정에서 작성자 이름은 김도환인데 이메일이 제 GitHub noreply 주소로 기록돼 있습니다. squash 전 원본 커밋이 모두 김도환 작성이라 김도환 작업으로 분류했습니다.
