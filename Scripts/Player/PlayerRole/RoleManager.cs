using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어의 역할을 담당하는 클래스, Player 프리펩에 붙는다.
/// </summary>
public class RoleManager : NetworkBehaviour
{
    private readonly NetworkVariable<PlayerRole> currentRole = new(
        PlayerRole.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public PlayerRole CurrentRole => currentRole.Value;
    public event Action<PlayerRole> RoleChanged;

    public override void OnNetworkSpawn()
    {
        currentRole.OnValueChanged += OnCurrentRoleChanged;

        if (IsServer)
        {
            RoleAssignmentManager.Instance?.RegisterPlayer(this);
            // 임시로 처음이면 마피아, 두번째로 접속하면 시민으로 설정
            if (RoleAssignmentManager.Instance?.GetPlayerCount() == 1)
            {
                AssignRole(PlayerRole.Mafia);
            }
            else
            {
                AssignRole(PlayerRole.Citizen);
            }
            //RoleAssignmentManager.Instance?.ForceAssignRole(clientId: OwnerClientId, PlayerRole.Mafia);
        }

        if (IsLocalPlayer)
        {
            InventoryUIController.Instance?.Slot4SetActive(CurrentRole == PlayerRole.Mafia);
        }

        RoleChanged?.Invoke(CurrentRole);
    }

    public override void OnNetworkDespawn()
    {
        currentRole.OnValueChanged -= OnCurrentRoleChanged;

        if (IsServer)
        {
            RoleAssignmentManager.Instance?.UnregisterPlayer(this);
        }
    }

    public void AssignRole(PlayerRole role)
    {
        if (!IsServer)
        {
            return;
        }

        currentRole.Value = role;
        Debug.Log($"Role assigned to {role} for player {OwnerClientId}");
    }

    private void OnCurrentRoleChanged(PlayerRole previousRole, PlayerRole newRole)
    {
        RoleChanged?.Invoke(newRole);
        if (IsLocalPlayer)
        {
            InventoryUIController.Instance?.Slot4SetActive(newRole == PlayerRole.Mafia);
        }
    }
}