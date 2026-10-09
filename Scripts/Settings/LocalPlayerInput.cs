using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// UI가 키를 가져갈 때 로컬 플레이어 조작을 끊고 되돌린다.
/// 채팅창과 일시정지 메뉴가 같이 쓴다.
/// </summary>
public static class LocalPlayerInput
{
    public static NetworkObject Local =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;

    /// <summary>끌 때는 남아 있던 이동과 시점 입력도 함께 비운다</summary>
    public static void SetEnabled(bool value)
    {
        NetworkObject player = Local;
        if (player == null) return;

        if (player.TryGetComponent(out PlayerInput input))
        {
            if (value) input.ActivateInput();
            else input.DeactivateInput();
        }

        if (player.TryGetComponent(out FirstPersonLook look)) look.SetLookSuspended(!value);
        if (!value && player.TryGetComponent(out PlayerMovement movement)) movement.ClearMoveInput();
    }
}
