using Steamworks;
using Steamworks.Data;
using TMPro;
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

    public ulong SteamId => _steamId.Value;
    public event System.Action<ulong> SteamIdChanged;

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

        _steamId.OnValueChanged += OnSteamIdChanged;

        if (IsOwner)
        {
            // 내 SteamId는 로컬에서만 알 수 있으므로, 서버에 등록 요청
            SubmitSteamIdServerRpc(SteamClient.SteamId.Value);
        }

        // 이미 값이 세팅된 채로 늦게 스폰(중간 참가 등)된 경우 강제 1회 알림
        if (_steamId.Value != 0)
        {
            SteamIdChanged?.Invoke(_steamId.Value);
        }

        if (!IsServer)
        {
            return;
        }
    }

    public override void OnNetworkDespawn()
    {
        _steamId.OnValueChanged -= OnSteamIdChanged;

        if (!IsServer)
        {
            return;
        }
    }

    [ServerRpc]
    private void SubmitSteamIdServerRpc(ulong steamId)
    {
        _steamId.Value = steamId;
    }

    private void OnSteamIdChanged(ulong previousValue, ulong newValue)
    {
        SteamIdChanged?.Invoke(newValue);
    }

    /// <summary>
    /// Steam 닉네임을 가져온다. 캐시가 아직 없으면 빈 문자열이 반환될 수 있음.
    /// </summary>
    public string GetDisplayName()
    {
        if (_steamId.Value == 0) return string.Empty;

        if (_steamId.Value == SteamClient.SteamId.Value)
        {
            return SteamClient.Name;
        }

        return new Friend(_steamId.Value).Name;
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