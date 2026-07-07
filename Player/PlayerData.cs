using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어의 데이터를 관리하는 스크립트, player 프리펩에 붙어있음.
/// +) 플레이어 스폰 관리.
/// </summary>
public class PlayerData : NetworkBehaviour
{
    private CharacterController _characterController;
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
            scoreManager.initScoreText();
        }
        
        if (!IsServer)
        {
            return;
        }

        PlayerSpawnManager.Instance?.RegisterPlayer(this);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            return;
        }

        PlayerSpawnManager.Instance?.UnregisterPlayer(this);
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
