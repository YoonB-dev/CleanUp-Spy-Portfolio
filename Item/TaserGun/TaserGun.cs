using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PickupItem))]
public class TaserGun : NetworkBehaviour, IUsableItem
{
    [Header("Gun Settings")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float fireRate = 0.5f;
    [SerializeField] private int maxAmmo = 3;
    [SerializeField] private float spawnForwardOffset = 0.6f; // 카메라 앞쪽 스폰 거리

    private readonly NetworkVariable<int> _currentAmmo = new(
        3,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int CurrentAmmo => _currentAmmo.Value;

    private PickupItem _thisPickupItem;
    private float _nextFireTime;
    private float _serverNextFireTime;

    private void Awake()
    {
        _thisPickupItem = GetComponent<PickupItem>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _currentAmmo.Value = maxAmmo;
        }
    }

    // PlayerInteraction으로부터 context와 본인의 playerCamera를 넘겨받음
    public void OnUse(InputAction.CallbackContext context, Camera playerCamera)
    {
        if (!IsOwner || !_thisPickupItem.IsHeld) return;

        if (context.performed)
        {
            Fire(playerCamera);
        }
    }

    private void Fire(Camera playerCamera)
    {
        if (Time.time < _nextFireTime) return;
        if (_currentAmmo.Value <= 0) return;

        // PlayerInteraction에서 안전하게 전달된 카메라 검증
        if (playerCamera == null)
        {
            Debug.LogWarning("[TaserGun] PlayerCamera가 전달되지 않았습니다.");
            return;
        }

        // 해당 클라이언트 플레이어 전용 카메라 기준 화면 중앙(0.5, 0.5) 레이 계산
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));

        Vector3 spawnPos = ray.origin + ray.direction * spawnForwardOffset;
        Vector3 shootDirection = ray.direction;

        _nextFireTime = Time.time + fireRate;

        // 계산된 화면 중앙 스폰 위치와 방향을 서버로 전송
        RequestFireServerRpc(spawnPos, shootDirection);
    }

    [ServerRpc]
    private void RequestFireServerRpc(Vector3 spawnPos, Vector3 shootDirection, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        if (_currentAmmo.Value <= 0 || !_thisPickupItem.IsHeld) return;
        // 1. 프리팹 및 총알 수량 기본 검증
        if (bulletPrefab == null)
        {
            Debug.LogError("[TaserGun] bulletPrefab이 인스펙터에 할당되지 않았습니다!");
            return;
        }
        // 서버 측 패킷 연사 검증
        if (Time.time < _serverNextFireTime) return;
        _serverNextFireTime = Time.time + fireRate;

        _currentAmmo.Value--;

        // 서버에서 총알 생성 및 네트워크 스폰
        Quaternion spawnRot = Quaternion.LookRotation(shootDirection);
        GameObject bulletObj = Instantiate(bulletPrefab, spawnPos, spawnRot);

        if (!bulletObj.TryGetComponent<NetworkObject>(out var bulletNetObj))
        {
            Debug.LogError("[TaserGun] bulletPrefab에 NetworkObject가 없습니다.");
            Destroy(bulletObj);
            return;
        }

        if (bulletObj.TryGetComponent<TaserBullet>(out var bullet))
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out var client) && client.PlayerObject != null)
            {
                bullet.Init(client.PlayerObject.NetworkObjectId);
            }
            else
            {
                // 예외 케이스 처리: TaserGun 자체의 NetworkObjectId 사용
                bullet.Init(NetworkObject.NetworkObjectId);
            }
        }
        bulletNetObj.Spawn();
    }
}