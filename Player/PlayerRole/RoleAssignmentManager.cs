using System.Collections.Generic;
using UnityEngine;

public class RoleAssignmentManager : SceneSingleton<RoleAssignmentManager>
{
    private readonly List<RoleManager> registeredPlayers = new();

    public void RegisterPlayer(RoleManager roleManager)
    {
        if (roleManager == null || registeredPlayers.Contains(roleManager))
        {
            return;
        }

        PlayerRole assignedRole = registeredPlayers.Count == 1
            ? PlayerRole.Mafia
            : PlayerRole.Citizen;

        registeredPlayers.Add(roleManager);
        roleManager.AssignRole(assignedRole);
    }

    public void UnregisterPlayer(RoleManager roleManager)
    {
        if (roleManager == null)
        {
            return;
        }

        registeredPlayers.Remove(roleManager);

        for (int index = registeredPlayers.Count - 1; index >= 0; index--)
        {
            if (registeredPlayers[index] != null)
            {
                continue;
            }

            registeredPlayers.RemoveAt(index);
        }
    }

    public bool IsMafia(ulong clientId)
    {
        foreach (var roleManager in registeredPlayers)
        {
            if (roleManager.OwnerClientId == clientId && roleManager.CurrentRole == PlayerRole.Mafia)
            {
                return true;
            }
        }

        return false;
    }
}