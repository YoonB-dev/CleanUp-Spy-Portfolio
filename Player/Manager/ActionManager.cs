using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ActionManager : NetworkBehaviour
{
    public static ActionManager Instance { get; private set; }
    [SerializeField] private GameObject trashPrefab;
    [SerializeField] private float skillCoolTime = 10f; // 공인 쿨타임 10초

    // 서버가 철저하게 감시하는 유저별 스킬 가능 시간 (Time.time 기준)
    private readonly Dictionary<ulong, float> _nextAllowedTimes = new();

    private void Awake() => Instance = this;

    public void ExecuteSpawnTrash(ulong senderId)
    {
        if (!IsServer) return;

        // 진짜 마피아가 맞는지 검사
        if (!RoleAssignmentManager.Instance.IsMafia(senderId)) return;

        // 서버 시간 기준으로 진짜 쿨타임이 지났는지 검사
        if (_nextAllowedTimes.TryGetValue(senderId, out float allowedTime) && Time.time < allowedTime)
        {
            Debug.LogWarning($"[서버] 유저 {senderId}가 쿨타임을 조작하여 RPC를 보냈습니다. 요청 거부.");
            return;
        }
        // 해당 플레이어의 PlayerInteraction 스크립트를 가져오기. -> 쓰레기 생성 후 바로 들고 있게 하기 위함.
        var playerObj = NetworkManager.Singleton.ConnectedClients[senderId].PlayerObject;
        if (playerObj == null || !playerObj.TryGetComponent<PlayerInteraction>(out var playerInteraction)) return;
        
        if (playerInteraction.IsHoldingItem.Value)
        {
            Debug.Log($"[서버] 유저 {senderId}는 이미 아이템을 들고 있어 쓰레기를 생성할 수 없습니다.");
            return;
        }

        // ==== 이제 진짜로 쓰레기 생성 및 스폰 ====
        // 서버 기준 다음 AllowedTime 갱신(쿨타임)
        _nextAllowedTimes[senderId] = Time.time + skillCoolTime;

        // 쓰레기 생성 및 스폰
        Vector3 spawnPosition = playerObj.transform.position + playerObj.transform.forward * 1.0f + Vector3.up * 1.0f; // 기본 백업 위치 (캐릭터 앞)
        Quaternion spawnRotation = playerObj.transform.rotation;
        GameObject trash = Instantiate(trashPrefab, spawnPosition, spawnRotation);
        trash.GetComponent<NetworkObject>().Spawn(true);

        // 쓰레기를 생성한 유저의 손에 들고 있게 해야함.
        if (trash.TryGetComponent<PickupItem>(out var pickupItem))
        {
            pickupItem.Pickup(playerInteraction);
            // PlayerInteraction의 내부 상태를 서버가 직접 셋팅
            playerInteraction.ForceSetHeldItem(pickupItem);
        }

        // 해당 클라이언트에게 "성공했으니 네 화면 UI 쿨타임 돌려라" 하고 Rpc를 내림.
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { senderId } }
        };
        StartCooldownClientRpc(skillCoolTime, clientRpcParams);
    }

    [ClientRpc]
    private void StartCooldownClientRpc(float cooldownDuration, ClientRpcParams clientRpcParams = default)
    {
        // 내 캐릭터의 MafiaAction 컴포넌트를 찾아서 로컬 UI 쿨타임을 시작하라고 명령.
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayer != null && localPlayer.TryGetComponent<MafiaActionTrash>(out var mafiaAction))
        {
            mafiaAction.StartLocalCooldown(cooldownDuration);
        }
    }
}
