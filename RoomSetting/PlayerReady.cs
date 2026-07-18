using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 각 플레이어 NetworkObject에 부착되는 컴포넌트.
/// - 서버: 스폰/디스폰 시 RoomSettings.AllPlayers에 등록/제거
/// - 클라이언트(Owner): Ready 버튼 클릭 시 ServerRpc로 토글 요청
/// 실제 IsReady 값의 "정답"은 항상 RoomSettings.AllPlayers 안에만 존재합니다.
/// (서버만 쓰기 가능한 NetworkList이므로 변조 걱정 없이 모든 클라이언트가 읽을 수 있음)
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerReady : NetworkBehaviour
{
    // 로컬(내) 플레이어의 PlayerReady를 다른 스크립트(UI 등)에서 쉽게 찾기 위한 정적 참조
    public static PlayerReady LocalInstance { get; private set; }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            LocalInstance = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && LocalInstance == this)
        {
            LocalInstance = null;
        }
    }

    /// <summary>
    /// UI(Ready 버튼)에서 호출. 소유자만 실제로 요청을 보냄.
    /// </summary>
    public void ToggleReady()
    {
        if (!IsOwner) return;
        ToggleReadyServerRpc();
    }

    // 소유자 클라이언트만 이 RPC를 호출할 수 있음
    [ServerRpc]
    private void ToggleReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (RoomSettings.Instance != null && RoomSettings.Instance.TryGetPlayerReady(senderId, out bool currentReady))
        {
            RoomSettings.Instance.SetPlayerReady(senderId, !currentReady);
        }
    }

    public void OnOpenMenu(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started)
        {
            return;
        }

        RoomMenuController.Instance.SetMenuOpen();
    }
}