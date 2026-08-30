# CleanUp-Spy-Portfolio

Unity 기반 멀티플레이어 추리 게임 프로젝트로, 로비 구성, 역할 배정, 아이템 상호작용, 동기화된 입력 처리, 네트워크 기반 게임 루프를 포함합니다.

## Project Overview

- 멀티플레이어 로비와 방 생성/입장 흐름
- 역할 기반 게임 로직: Citizen / Mafia
- 아이템 픽업, 드래그, 배치, 던지기, 사용 기능
- 서버 중심 상태 동기화 및 클라이언트 입력 검증
- 로컬 AI 실험용 에디터 툴 추가 구조

## Key Systems

### Role and match flow
- `Scripts/Player/PlayerRole/RoleManager.cs`
- `Scripts/Player/PlayerRole/RoleAssignmentManager.cs`
- `Scripts/Player/PlayerRole/RoleNameTag.cs`
- 역할 상태를 서버에서 관리하고, 클라이언트는 상태를 표시하는 구조로 구성되었습니다.

### Item interaction
- `Scripts/Item/PickupItem.cs`
- `Scripts/Item/PlacementValidator.cs`
- `Scripts/Item/PlaceableBox.cs`
- `Scripts/Item/Interface/`
- 아이템에 대한 공통 인터페이스를 분리해, 줍기/드래그/배치/사용 동작을 재사용 가능하게 설계했습니다.

### Player control and action flow
- `Scripts/Player/PlayerInteraction.cs`
- `Scripts/Player/PlayerInventory.cs`
- `Scripts/Player/PlayerMovement.cs`
- 입력 처리, 행동 가능 여부 체크, 장비 상태 관리가 별도 모듈로 분리되어 있습니다.

### Match and UI flow
- `Scripts/Manager/`
- `Scripts/RoomSetting/`
- `Scripts/Settings/`
- 게임 타이머, 점수, 로비 UI, 설정 UI, 대기/시작 상태 관리가 별도 계층으로 구성됩니다.

### AI experimental tools
- `Scripts/AI/ComfyUIController.cs`
- `Scripts/AI/AIAssetGenerator.cs`
- 로컬 ComfyUI 기반 실험용 에디터 도구이며, 런타임 게임 로직이 아니라 에셋 생성 프로토타입으로 구분해 사용됩니다.

## Directory Summary

- `Scripts/AI/` : 로컬 AI 생성 및 에디터 워크플로우 관련 실험 코드
- `Scripts/Item/` : 아이템, 배치, 드래그, 도구, 픽업 시스템
- `Scripts/Item/Interface/` : 인터페이스 기반 상호작용 계약 정의
- `Scripts/Player/` : 플레이어 이동, 상호작용, 역할, 인벤토리, 라그돌 기반 동작
- `Scripts/Manager/` : 게임 진행 상태, 타이머, 점수 관리
- `Scripts/Network/` : 방 생성/연결, 코드 입력, 네트워크 연결 로직
- `Scripts/RoomSetting/` : 로비 및 방 설정 관리
- `Scripts/Settings/` : 설정 UI와 입력 바인딩 관리
- `Scripts/Chat/` : 채팅 메시지 전송 및 수신 처리
- `Scripts/Generic/` : 공통 싱글톤 및 유틸리티
- `Scripts/Editor/` : 에디터 전용 툴 및 도구 코드

## Notes

- 주요 공개 포인트는 인터페이스 기반 아이템 설계, 역할 기반 게임 로직, 서버 중심 상태 관리입니다.
- AI 관련 코드는 실험적 프로토타입으로 분류되며, 게임 시스템의 핵심 기능과 별개로 관리됩니다.
