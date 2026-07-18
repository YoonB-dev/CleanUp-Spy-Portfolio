using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class RoleAssignmentManager : NetworkBehaviour
{
    public static RoleAssignmentManager Instance { get; private set; }
    private readonly List<RoleManager> registeredPlayers = new();
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void RegisterPlayer(RoleManager roleManager)
    {
        if (roleManager == null || registeredPlayers.Contains(roleManager))
        {
            return;
        }

        registeredPlayers.Add(roleManager);

        if (IsServer)
        {
            CheckAndAssignRoles();
        }
    }

    public void UnregisterPlayer(RoleManager roleManager)
    {
        if (roleManager == null) return;
        registeredPlayers.Remove(roleManager);
    }

    /// <summary>
    /// RoomSettings의 설정을 읽어와 등록된 플레이어들에게 무작위로 직업을 배정합니다. (서버 전용)
    /// </summary>
    private void CheckAndAssignRoles()
    {
        if (!IsServer) return;

        // 1. 방 설정 데이터와 현재 스폰된 인원이 일치하는지 검사
        if (RoomSettings.Instance == null)
        {
            Debug.LogError("[RoleAssignmentManager] RoomSettings 인스턴스를 찾을 수 없습니다!");
            return;
        }

        int targetCount = RoomSettings.Instance.AllPlayers.Count;

        // 아직 인게임 씬에 모든 캐릭터가 스폰되어 등록되지 않았다면, 마지막 인원이 올 때까지 대기합니다.
        if (registeredPlayers.Count < targetCount)
        {
            Debug.Log($"[RoleAssignmentManager] 플레이어 등록 대기 중... ({registeredPlayers.Count} / {targetCount})");
            return;
        }

        Debug.Log("[RoleAssignmentManager] 모든 플레이어가 등록되었습니다. 무작위 직업 배정을 시작합니다.");

        // 2. 먼저 모든 플레이어를 '시민(Citizen)'으로 초기화합니다.
        foreach (var p in registeredPlayers)
        {
            p.AssignRole(PlayerRole.Citizen);
        }

        // 3. RoomSettings에서 설정된 마피아 숫자를 가져옵니다.
        int targetMafiaCount = RoomSettings.Instance.MafiaCount.Value;

        // 안전장치: 마피아 수가 현재 플레이어 수보다 많아지는 기현상 방지
        targetMafiaCount = Mathf.Clamp(targetMafiaCount, 1, registeredPlayers.Count - 1);

        // 4. 무작위 셔플 및 마피아 선출을 위한 인덱스 리스트 생성
        List<int> playerIndices = new List<int>();
        for (int i = 0; i < registeredPlayers.Count; i++)
        {
            playerIndices.Add(i);
        }

        // 5. 설정된 마피아 수만큼 무작위 인덱스를 뽑아 마피아로 지정합니다.
        for (int i = 0; i < targetMafiaCount; i++)
        {
            int randomIndex = Random.Range(0, playerIndices.Count);
            int selectedPlayerIndex = playerIndices[randomIndex];

            // 선택된 플레이어에게 마피아 권한 부여
            registeredPlayers[selectedPlayerIndex].AssignRole(PlayerRole.Mafia);
            Debug.Log($"[RoleAssignmentManager] 마피아 당첨 -> ClientId: {registeredPlayers[selectedPlayerIndex].OwnerClientId}");

            // 한 번 뽑힌 인덱스는 후보 목록에서 제거 (중복 당첨 방지)
            playerIndices.RemoveAt(randomIndex);
        }
    }

    /// <summary>
    /// 특정 ClientId가 마피아인지 외부에서 판별하는 함수
    /// </summary>
    public bool IsMafia(ulong clientId)
    {
        foreach (var roleManager in registeredPlayers)
        {
            if (roleManager != null && roleManager.OwnerClientId == clientId)
            {
                return roleManager.CurrentRole == PlayerRole.Mafia;
            }
        }
        return false;
    }
}