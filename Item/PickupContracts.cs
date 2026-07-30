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
/// 아이템의 대분류. TrashCan처럼 "이 아이템을 어떻게 처리해야 하는지" 판단이 필요한
/// 범용 시스템에서 구체 클래스 대신 이 값만 확인하도록 함.
/// </summary>
public enum PickupCategory
{
    Trash,  // 쓰레기통에 버릴 수 있는 일반 소모성 아이템
    Box,    // 배치용 박스 (쓰레기통에 안 들어감)
    Tool    // 도구 (페인트건, 카메라, 청소도구 등 - 쓰레기통에 안 들어감, 보통 커스텀 캐리 로직을 가짐)
}