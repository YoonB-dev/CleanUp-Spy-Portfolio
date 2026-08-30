using UnityEngine;

/// <summary>
/// 공개용 포트폴리오에서 강조할 아이템 공통 인터페이스의 예시.
/// 실제 프로젝트에서는 PickupItem이 이 책임을 구현하는 형태로 확장되었다.
/// </summary>
public interface IPickupObject
{
    bool IsHeld { get; }
    bool CanBePickedUpBy(Component actor);
    void OnPickedUp();
    void OnDropped();
    void SetHighlighted(bool highlighted);
}
