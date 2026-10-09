# 작성자 구분 (Authorship)

이 레포의 `Scripts/`는 팀 프로젝트의 스크립트 폴더를 그대로 가져온 것입니다(`git subtree`). 팀원의 코드도 함께 들어 있으므로, 제가 작성한 코드를 이 문서에 구분해 둡니다.

- 팀 구성: 클라이언트 프로그래머 2명(이윤형, 김도환), 모델러 1명
- 기준 시점: 메인 레포 split 커밋 `c8a2c15`

## 한눈에 보기

**팀원(김도환) 담당** — 아래 범위를 제외한 시스템은 제가 작성했습니다.

- 액티브 래그돌: 물리 관절로 움직이는 캐릭터, 래그돌 포즈 압축 동기화
- 플레이어 행동 게이트: 행동 간 상호 배타 규칙
- 전투·물리 상호작용: 펀치, 붙잡기, 넉다운, 다이빙
- 설정·메인 화면·초대 코드
- 채팅, 근접 음성 채팅
- Steam 초기화·클라우드 저장·전적 기록, 게임 종료 판정과 결과 화면

**제 담당**

- 페인트·오염도 시스템
- 아이템 상호작용과 인벤토리, 도구 아이템(테이저건·흡입기·폴라로이드 카메라·청소 도구)
- 쓰레기·상자 배치
- 점수·역할 배정·게임 진행 흐름
- 로비·방 설정과 접속 처리
- 플레이어 이동·1인칭 시점의 기본 구조
- 사운드 시스템
- 에디터 툴: ComfyUI 연동 에셋 생성, 페인트 표면 ID 정리

줄 수로는 `Scripts/`의 `.cs` 18,038줄 중 10,753줄(약 60%)을 제가 작성했습니다. 줄 수는 작업량의 근사치일 뿐 중요도를 뜻하지 않습니다.

## 1. 제 작업

| 시스템 | 만든 내용 | 파일 |
|---|---|---|
| 페인트·오염도 | 원본 머테리얼을 건드리지 않는 페인트 오버레이를 어떤 메시에든 붙일 수 있게 만들었습니다. 브러시는 월드 좌표·미터 단위로 칠하고 지웁니다. 칠하기 요청은 서버가 검증한 뒤 전파하고, 서버에서만 GPU readback으로 칠해진 실제 면적을 계산합니다 | `Item/PaintGun/PaintableSurface.cs`, `PaintSurfaceManager.cs`, `PaintCleaner.cs`, `Player/Mafia/MafiaPaintAction.cs`, `UI/PaintGaugeUI.cs` |
| 아이템 상호작용·인벤토리 | 줍기·버리기·던지기, 서버 권위 인벤토리 슬롯, 끌기 오브젝트를 만들었습니다. 아이템 사용은 공통 인터페이스(`IUsableItem`)로 묶었습니다 | `Player/PlayerInteraction.cs`, `Player/PlayerInventory.cs`, `Item/PickupItem.cs`, `Item/PickupHighlight.cs`, `Item/ItemData.cs`, `Item/Interface/` (`IUsableItem`, `PickupContracts`, `ZoomableItem`, `DraggableObject`, `CarryGripPoints`), `InventoryUIController.cs`, `Player/ThrowGaugeUI.cs` |
| 도구 아이템 | 테이저건(서버 탄약·발사 간격 검증, 피격 시 감전 둔화와 탄 박힘 연출), 흡입기, 폴라로이드 카메라(뷰파인더 렌더링, 사진 JPG 전송) | `Item/TaserGun/` (`TaserGun`, `TaserBullet`, `PlayerTaserStuck`), `Item/MagnetAttractor.cs`, `PolaroidCamera/` (`PolaroidCamera`, `Photo`) |
| 쓰레기·상자 배치 | ScriptableObject 기반 쓰레기 데이터, 마피아 쓰레기 생성, 쓰레기통 처리, 상자 배치 프리뷰와 서버 재검증 | `Trash/` (`TrashData`, `TrashObject`), `Item/TrashCan/TrashCan.cs`, `Player/Mafia/MafiaActionTrash.cs`, `Item/PlaceableBox.cs`, `BoxPlacementPreview.cs`, `PlacementValidator.cs`, `PlacementZone.cs` |
| 점수·역할·게임 진행 | 쓰레기·상자·페인트 오염도 점수, 서버 역할 배정, 게임 타이머를 만들었습니다. 모든 클라이언트가 인게임 씬을 불러온 뒤 플레이어를 한꺼번에 스폰합니다 | `Manager/ScoreManager.cs`, `Manager/GameTimerManager.cs`, `Player/PlayerRole/` (`PlayerRole`, `RoleManager`, `RoleAssignmentManager`, `RoleNameTag`), `Player/Manager/PlayerSpawnManager.cs`, `Player/PlayerData.cs`\*, `Manager/InGameManager.cs`\*, `Manager/InGameUIController.cs`\* |
| 로비·방 설정·접속 | 방 설정(인원, 마피아 수, 플레이 시간, 오염도 배율)과 준비 상태를 동기화하고, 로비 캐릭터를 스폰합니다. 접속 승인에서는 게임 중 입장과 정원 초과를 거부하고, 연결이 끊기면 UI를 복구합니다 | `RoomSetting/RoomSettings.cs`\*, `RoomUIController.cs`\*, `LobbyFlowManager.cs`\*, `RoomPlayerEntryUI.cs`\*, `PlayerReady.cs`, `RoomMenuController.cs`, `RoomPlayerInfo.cs`, `Network/NetworkConnect.cs`\*, `Network/NetworkConnectTest.cs` (테스트용) |
| 이동·시점 | 서버 권위 이동·점프(입력 ServerRpc → 서버에서 이동 처리)와 1인칭 카메라를 만들었습니다. 시선 상하 각도는 다른 클라이언트에 복제합니다 | `Player/PlayerMovement.cs`\*, `Player/FirstPersonLook.cs`\* |
| 들기 자세 | 아이템을 양손으로 드는 래그돌 자세를 만들었습니다. 물건 그립 폭에 맞춰 팔을 벌리고 시선을 따라 움직입니다. 팀원이 만든 래그돌 위에 구현했습니다 | `Player/Ragdoll/RagdollPoser.cs`, `RagdollDriver.cs`, `RagdollNetworkSync.cs` 일부 (팀원 파일, 3-2 참고) |
| 사운드 | SoundManager, BGM·SFX 재생, 표면별 발소리, UI 사운드, 타격음 | `Sound/` 전체 (`SoundManager`, `BgmPlayer`, `SfxPlayer`, `SoundData`, `SceneBgm`, `ImpactSound`, `Footstep/`, `UI/`), `Player/PlayerPunch.cs` 일부 (팀원 파일, 3-2 참고) |
| 두꺼비집 | 길게 눌러 조명을 켜고 끄는 상호작용. 게이지 UI가 있고 서버에서 검사합니다 | `Light/LightSwitch.cs`, `Player/LightInteraction.cs` |
| 에디터 툴 | ComfyUI와 연동해 2D 이미지 생성 → 수락/거절 → 3D 변환 → 프로젝트 임포트·씬 배치까지 자동화했습니다. 페인트 표면의 네트워크 식별자(Surface ID)를 일괄로 다시 할당하는 툴도 만들었습니다 | `AI/ComfyUIController.cs`, `AI/AIAssetGenerator.cs`, `Editor/PaintableSurfaceEditor.cs` |
| 공용 | 싱글톤 베이스 클래스, 게이지 UI | `Generic/Singleton.cs`, `SceneSingleton.cs`, `PersistentSingleton.cs`, `UI/UIPropertyGauge.cs` |

\* 표시한 파일은 제가 만들었지만 팀원이 기능을 추가했습니다. 어느 부분인지는 3-1에 적었습니다.

팀원이 행동 게이트 통합(#33)과 플레이 통계(#52)를 작업하면서 여러 파일에 연결 코드를 몇 줄씩 넣었습니다(`_gate.CanDo(...)` 검사, 통계 카운트 증가 등). 이런 한두 줄짜리 연결 코드는 따로 적지 않았습니다.

## 2. 팀원(김도환) 작업

아래 파일은 팀원이 작성했습니다. 프로젝트 구조를 이해하는 데 필요해 함께 두었습니다. 이 중 제가 기능을 추가한 파일은 3-2에 적었습니다.

| 시스템 | 파일 |
|---|---|
| 액티브 래그돌 | `Player/Ragdoll/` (`RagdollDriver`, `RagdollPoser`, `RagdollNetworkSync`, `QuaternionCompression`, `RagdollAnimationRig`, `RagdollGroundContact`, `IgnoreRagdollCollision`, `ConfigurableJointExtensions`, `Editor/ActiveRagdollTools`, `Editor/RagdollTuner`), `Editor/RagdollTransplant.cs` |
| 행동 게이트 | `Player/PlayerActionGate.cs` |
| 전투·물리 상호작용 | `Player/PlayerPunch.cs`, `PlayerGrab.cs`, `PlayerKnockdown.cs`, `PlayerDive.cs` |
| 설정·메인 화면 | `Settings/` 전체, `Network/InviteCode.cs`, `CodeInputField.cs`, `JoinRoomModal.cs`, `Generic/ClickCatcher.cs` |
| 채팅·음성 | `Chat/` (`ChatManager`, `ChatUIController`), `Voice/PlayerVoice.cs` |
| Steam·저장·기록 | `Network/SteamManager.cs`, `Save/` (`PlayerSave`, `PlayerSaveData`), `UI/RecordsModal.cs`, `UI/RecordRow.cs` |

`Network/SteamManager.cs`는 제가 처음 만들었지만 팀원이 다시 작성해서, 지금은 제 코드가 남아 있지 않습니다.

## 3. 경계가 섞인 파일

### 3-1. 제가 만든 파일에 팀원이 추가한 부분

| 파일 | 제가 작성한 부분 | 팀원(김도환)이 추가한 부분 |
|---|---|---|
| `Player/PlayerData.cs` | Steam ID 동기화, 스폰 시 점수 UI 연결, 서버 측 스폰 위치 지정 | Steam 닉네임 동기화, 플레이 통계 변수 |
| `Manager/InGameManager.cs` | 모든 클라이언트가 인게임 씬을 다 불러온 뒤 플레이어를 한꺼번에 스폰, 로딩 중 이탈한 클라이언트 처리, 타이머 시작 | 제한 시간 종료 시 승패 판정·결과 표시·전적 저장·로비 복귀 (`EndGame`, `GameOverClientRpc`, `RecordResult`, `ReturnToLobby`) |
| `Manager/InGameUIController.cs` | 남은 시간 표시, 준비 → 시작 단계에 맞춘 역할 공개 패널 | 역할 스플래시 연출, 결과 화면 (`ShowResult`) |
| `RoomSetting/RoomSettings.cs` | 방 설정(인원, 마피아 수, 플레이 시간, 오염도 배율) 동기화, 플레이어 목록·준비 상태 관리 | 방 코드 공유, 플레이어 이름 설정 |
| `RoomSetting/RoomUIController.cs` | 방 설정 UI, 호스트 전용 조작, 플레이어 목록·준비 상태 갱신, 설정값 입력 처리 | 대기방 UI 개편 (빈 자리 표시, 준비 배지, 키 입력으로 준비 토글) |
| `RoomSetting/LobbyFlowManager.cs` | 로비 캐릭터 스폰, 인게임 씬 전환 | `RoomSettings`를 씬에 두지 않고 한 번만 스폰하도록 변경 |
| `RoomSetting/RoomPlayerEntryUI.cs` | 플레이어 이름·사진·준비·호스트 표시 | 빈 자리 표시 (`SetEmpty`) |
| `Network/NetworkConnect.cs` | 연결 승인(게임 중 입장 거부, 정원 초과 거부), 연결 끊김 처리와 UI 복구, Steam Relay 접속 대상 설정 | 메인 화면 호스트 시작과 초대 코드 접속 (`StartHosting`, `TryJoin`), 씬 재진입 시 UI 인계, 방 코드 생성 |
| `Player/PlayerMovement.cs` | 서버 권위 이동 구조(입력 ServerRpc → 서버에서 이동·점프 처리), 테이저 감전 둔화, 끌기 상태, UI가 열렸을 때 이동 입력 차단 | 래그돌 소유 등록, 붙잡힌 상태 이동, 점프 버퍼·코요테 타임·낙하 가속, 채팅 중 입력 비우기 |
| `Player/FirstPersonLook.cs` | 1인칭 카메라 기본 구조(소유자만 카메라·오디오 활성), 다른 클라이언트에 시선 상하 각도 복제(들기 자세용), 조준 시 Near Clip 전환 | 시점 회전 계산과 서버 전송, 목 기준 카메라 공전, 감도·Y축 반전·시야각 설정 적용, 1인칭 자기 몸 레이어 숨김 |

### 3-2. 팀원이 만든 파일에 제가 추가한 부분

아래 파일들의 구조와 대부분의 코드는 팀원이 작성했습니다. 제가 작성한 부분은 오른쪽에 적은 것뿐입니다.

| 파일 | 제가 작업한 부분 |
|---|---|
| `Player/Ragdoll/RagdollPoser.cs` | 양손 들기 자세: 양손 사이 `CarryAnchor` 생성과 시선을 따라가는 매 프레임 갱신, 물건 그립 폭에 맞춘 팔 벌림 보정(`BuildCarryOffset`), 들기 중 전용 관절 스프링 블렌딩, 외부 API(`SetCarryRequested`, `SetCarryTarget`, `SetCarryAnchorOffset`). 시선 상하 각도에 따른 가슴 젖힘 비율과 상하 각도 제한 |
| `Player/Ragdoll/RagdollDriver.cs` | 들기 대상의 그립 폭 계산 (`SetCarryTarget`, `TryGetCarryHalfWidth`), 시선 상하에 따른 골반 앵커 기울임 |
| `Player/Ragdoll/RagdollNetworkSync.cs` | 외부에서 래그돌에 접근하는 조회 API (`Poser`, `BoneCount`, `GetBone`) — 들기 자세와 테이저 박힘 연출에서 사용 |
| `Player/PlayerActionGate.cs` | 끌기(Drag) 규칙과 상태 판정, UI가 열렸을 때 이동·시점을 포함한 모든 행동 차단 (`SetUIOpen`) |
| `Player/PlayerPunch.cs` | 휘두르기·타격 사운드: 서버가 타격을 판정한 뒤 ClientRpc로 재생, 맞은 물체별 소리 선택 |

## 구분 방법

- 모든 `.cs` 파일에 `git blame -w -M -C`를 실행해 줄 단위 작성자를 확인했습니다. 공백만 바뀐 줄과 다른 위치·파일에서 옮겨 온 줄은 원래 작성자로 계산됩니다.
- GitHub squash merge 커밋은 PR 작성자 한 명에게 몰리므로, 커밋 본문에 남은 원본 커밋 목록을 원본 커밋의 작성자와 대조했습니다.
- 파일을 처음 만든 사람을 기준으로 나누고, 상대방이 추가한 부분은 기능 단위로 따로 적었습니다.
- 현재 코드에 제가 쓴 줄이 없거나, 남아 있는 줄이 `using` 선언이나 필드 몇 줄뿐인 파일은 팀원 작업으로 분류했습니다.

## 직접 확인하는 방법

```bash
# 파일별 작성자 줄 수
git blame -w -M -C --ignore-revs-file .git-blame-ignore-revs --line-porcelain Scripts/<경로> | grep '^author ' | sort | uniq -c
```

- 공개 전에 주석만 정리한 커밋은 `.git-blame-ignore-revs`에 적어 blame에서 건너뜁니다. GitHub의 Blame 화면에는 자동으로 적용됩니다.
- subtree 특성상 이 레포에서 `git log -- Scripts/<경로>`를 실행하면 add 커밋 하나만 보입니다. 줄 단위 작성자는 `git blame`으로 확인할 수 있습니다.
- 채팅·음성 PR(#36, #38)의 squash 커밋은 GitHub 머지 과정에서 작성자 이름은 김도환인데 이메일이 제 GitHub noreply 주소로 기록돼 있습니다. squash 전 원본 커밋이 모두 김도환 작성이라 김도환 작업으로 분류했습니다.
