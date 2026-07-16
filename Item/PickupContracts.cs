using UnityEngine;

/// <summary>
/// PickupItem이 특정 아이템(PlaceableBox, PolaroidCamera 등)의 존재를 직접 몰라도 되게
/// 해주는 작은 계약(contract)들. 새 아이템 종류가 늘어나도 PickupItem/TrashCan 등
/// 범용 시스템 코드는 건드릴 필요 없이, 아이템 쪽에서 필요한 인터페이스만 구현하면 됨.
/// </summary>

/// <summary>
/// 아이템이 집혔을 때/내려놓아질 때 자기만의 반응이 필요하면 구현.
/// 예: PlaceableBox가 집히는 순간 아랫장 연쇄 물리 연산을 시동.
/// </summary>
public interface IPickupListener
{
    void OnPickedUp();
    void OnDropped();
}

/// <summary>
/// 아이템이 손에 들려있는 동안 PickupItem의 기본 캐리 위치(carryDistance/carryHeight)
/// 대신 자기만의 위치/회전을 쓰고 싶으면 구현.
/// 예: PolaroidCamera가 조준 중일 때 카메라 눈앞으로 바짝 당겨오는 것.
///
/// TryGetCarryTransform이 false를 반환하면 PickupItem은 평소처럼 기본 캐리 로직을 사용한다.
/// (즉 "이번 프레임엔 내가 특수 위치를 원하지 않는다"는 뜻)
/// </summary>
public interface ICustomCarryTransform
{
    bool TryGetCarryTransform(Transform cameraTransform, out Vector3 position, out Quaternion rotation);
}

/// <summary>
/// 아이템의 대분류. TrashCan처럼 "이 아이템을 어떻게 처리해야 하는지" 판단이 필요한
/// 범용 시스템에서 구체 클래스 대신 이 값만 확인하도록 함.
/// </summary>
public enum PickupCategory
{
    Trash,  // 쓰레기통에 버릴 수 있는 일반 소모성 아이템
    Box,    // 배치용 박스 (쓰레기통에 안 들어감)
    Tool    // 도구 (페인트건, 카메라, 청소도구 등 - 쓰레기통에 안 들어감, 보통 커스텀 캐리 로직을 가짐)
}