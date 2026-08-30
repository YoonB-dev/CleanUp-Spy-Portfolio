using UnityEngine;

/// <summary>
/// 드래그 가능한 오브젝트를 공통 인터페이스로 다루기 위한 예시.
/// 실제 프로젝트에서는 DraggableObject가 이와 유사한 책임을 담당한다.
/// </summary>
public interface IDraggableItem
{
    bool IsBeingDragged { get; }
    Transform HandlePoint { get; }
    void SetHighlighted(bool highlighted);
    void RequestStartDrag(Component source);
    void RequestStopDrag();
}
