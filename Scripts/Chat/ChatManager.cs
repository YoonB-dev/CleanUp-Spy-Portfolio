using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum ChatChannel { All, Mafia }

/// <summary>
/// 서버 권위 채팅. 채널을 볼 자격이 있는 클라이언트에게만 되쏜다.
/// </summary>
public class ChatManager : NetworkBehaviour
{
    public const int MAX_MESSAGE_LENGTH = 100;

    private const float SEND_INTERVAL = 0.5f;

    public static ChatManager Instance { get; private set; }

    private readonly Dictionary<ulong, float> _lastSendTime = new();
    private readonly List<ulong> _targets = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public void Send(string text, ChatChannel channel)
    {
        FixedString512Bytes payload = default;
        payload.CopyFromTruncated(text);
        SubmitChatRpc(payload, channel);
    }

    [Rpc(SendTo.Server)]
    private void SubmitChatRpc(FixedString512Bytes text, ChatChannel channel, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        // 클라이언트가 보낸 채널과 본문은 믿지 않는다. 정의에 없는 값은 전체로 떨어뜨린다
        bool mafiaOnly = channel == ChatChannel.Mafia;
        if (mafiaOnly && !IsMafia(senderId)) return;
        if (!ConsumeCooldown(senderId)) return;

        FixedString512Bytes body = default;
        body.CopyFromTruncated(Clean(text.ToString()));
        if (body.Length == 0) return;

        FixedString64Bytes senderName = default;
        senderName.CopyFromTruncated(Clean(GetDisplayName(senderId)));

        _targets.Clear();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!mafiaOnly || IsMafia(clientId)) _targets.Add(clientId);
        }

        ChatChannel sendChannel = mafiaOnly ? ChatChannel.Mafia : ChatChannel.All;
        ReceiveChatRpc(sendChannel, senderName, body, RpcTarget.Group(_targets, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ReceiveChatRpc(ChatChannel channel, FixedString64Bytes senderName, FixedString512Bytes text, RpcParams rpcParams)
    {
        ChatUIController.Instance?.AddMessage(channel, senderName.ToString(), text.ToString());
    }

    private bool ConsumeCooldown(ulong clientId)
    {
        float now = Time.unscaledTime;
        if (_lastSendTime.TryGetValue(clientId, out float last) && now - last < SEND_INTERVAL) return false;

        _lastSendTime[clientId] = now;
        return true;
    }

    // 로비 캐릭터에는 RoleManager가 없다
    private static bool IsMafia(ulong clientId)
    {
        NetworkObject player = GetPlayer(clientId);

        return player != null
            && player.TryGetComponent(out RoleManager role)
            && role.CurrentRole == PlayerRole.Mafia;
    }

    private static string GetDisplayName(ulong clientId)
    {
        NetworkObject player = GetPlayer(clientId);
        string name = player != null ? player.GetComponent<PlayerData>().GetDisplayName() : null;

        return string.IsNullOrEmpty(name) ? $"Player {clientId}" : name;
    }

    private static NetworkObject GetPlayer(ulong clientId)
    {
        return NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            ? client.PlayerObject
            : null;
    }

    private static string Clean(string text)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
        if (text.Length > MAX_MESSAGE_LENGTH) text = text.Substring(0, MAX_MESSAGE_LENGTH);

        // TMP 태그로 남의 화면 글자를 바꾸지 못하게
        return text.Replace("<", "&#60;");
    }
}
