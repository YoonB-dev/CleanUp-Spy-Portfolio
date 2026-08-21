using UnityEngine;
using UnityEngine.InputSystem;

public interface IUsableItem
{
    /// <summary>
    /// 아이템 사용 입력 처리 (Input System의 context 전달)
    /// 마우스 좌클릭으로 동작하는 것들 대상
    /// </summary>
    void OnUse(InputAction.CallbackContext context, Camera playerCamera);
}