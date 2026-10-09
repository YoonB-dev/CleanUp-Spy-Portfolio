using Steamworks;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어의 데이터를 관리하는 스크립트, player 프리펩에 붙어있음.
/// +) 플레이어 스폰 관리.
/// +) SteamId / 닉네임 동기화.
/// </summary>
public class PlayerData : NetworkBehaviour
{
    private CharacterController _characterController;

    private readonly NetworkVariable<ulong> _steamId = new NetworkVariable<ulong>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<FixedString128Bytes> _displayName = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public ulong SteamId => _steamId.Value;
    public event System.Action DisplayNameChanged;

    public NetworkVariable<int> CleanCount { get; } = new(0, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> TrashCount { get; } = new(0, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> ItemsOrganized { get; } = new(0, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> ItemsMessUp { get; } = new(0, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        if (_characterController == null)
        {
            _characterController = GetComponent<CharacterController>();
        }
    }

    public override void OnNetworkSpawn()
    {
        // 스폰할때 플레이어 점수를 ScoreManager에 연결
        ScoreManager scoreManager = FindAnyObjectByType<ScoreManager>();
        if (scoreManager != null)
        {
            scoreManager.InitScoreText();
        }

        _displayName.OnValueChanged += OnDisplayNameChanged;

        if (IsOwner)
        {
            // 친구가 아닌 플레이어의 닉네임은 Steam이 모르므로 각자 자기 닉네임을 직접 보낸다
            FixedString128Bytes displayName = default;
            displayName.CopyFromTruncated(SteamClient.Name);
            SubmitProfileServerRpc(SteamClient.SteamId.Value, displayName);
        }

        // 이미 값이 세팅된 채로 늦게 스폰(중간 참가 등)된 경우 강제 1회 알림
        if (_displayName.Value.Length > 0)
        {
            DisplayNameChanged?.Invoke();
        }

        if (!IsServer)
        {
            return;
        }
    }

    public override void OnNetworkDespawn()
    {
        _displayName.OnValueChanged -= OnDisplayNameChanged;

        if (!IsServer)
        {
            return;
        }
    }

    [ServerRpc]
    private void SubmitProfileServerRpc(ulong steamId, FixedString128Bytes displayName)
    {
        // TMP 태그로 남의 화면 글자를 바꾸지 못하게
        FixedString128Bytes safeName = default;
        safeName.CopyFromTruncated(displayName.ToString().Replace("<", "&#60;"));

        _steamId.Value = steamId;
        _displayName.Value = safeName;
        RoomSettings.Instance?.SetPlayerName(OwnerClientId, safeName);
    }

    private void OnDisplayNameChanged(FixedString128Bytes previousValue, FixedString128Bytes newValue)
    {
        DisplayNameChanged?.Invoke();
    }

    /// <summary>
    /// Steam 닉네임을 가져온다. 소유자가 아직 보내지 않았으면 빈 문자열이 반환될 수 있음.
    /// </summary>
    public string GetDisplayName() => _displayName.Value.ToString();

    public void AddStatsTo(PlayStats stats)
    {
        stats.totalCleanCount += CleanCount.Value;
        stats.totalTrashCount += TrashCount.Value;
        stats.totalItemsOrganized += ItemsOrganized.Value;
        stats.totalItemsMessUp += ItemsMessUp.Value;
    }

    public void SetServerSpawnPosition(Vector3 spawnPosition)
    {
        if (!IsServer)
        {
            return;
        }

        _characterController.enabled = false;
        transform.position = spawnPosition;
        _characterController.enabled = true;
        _characterController.Move(Vector3.down * 0.01f);
    }
}