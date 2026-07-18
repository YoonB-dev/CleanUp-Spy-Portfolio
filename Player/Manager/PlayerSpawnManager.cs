using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 스폰 위치를 관리하는 클래스, Scene의 Manager오브젝트에 붙는다.
/// </summary>
public class PlayerSpawnManager : SceneSingleton<PlayerSpawnManager>
{

    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Vector2 spawnRange = new(1f, 1f);
    [SerializeField] private float spawnHeight = 2f;

    public void SetRandomPosition(Transform targetTransform)
    {
        if (targetTransform == null) return;

        Vector3 spawnPosition = GetRandomSpawnPosition();
        if (targetTransform.TryGetComponent<Unity.Netcode.Components.NetworkTransform>(out var netTransform))
        {
            targetTransform.position = spawnPosition;
        }
        else
        {
            targetTransform.position = spawnPosition;
        }
    }

    public Vector3 GetRandomSpawnPosition()
    {
        float randomX = Random.Range(-spawnRange.x, spawnRange.x);
        float randomZ = Random.Range(-spawnRange.y, spawnRange.y);
        Vector3 centerPosition = spawnPoint != null ? spawnPoint.position : transform.position;
        return new Vector3(centerPosition.x + randomX, spawnHeight, centerPosition.z + randomZ);
    }
}