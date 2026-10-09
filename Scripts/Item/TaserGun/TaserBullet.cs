using Unity.Netcode;
using UnityEngine;

public class TaserBullet : NetworkBehaviour
{
    [SerializeField] private float speed = 25f;
    [SerializeField] private float lifeTime = 3f;
    [SerializeField] private float slowDuration = 3f; // 감전 지속 시간
    [SerializeField] private float slowFactor = 0.3f;   // 이동 속도 비율 (30%로 감속)

    private ulong _shooterNetId;

    public void Init(ulong shooterNetId)
    {
        _shooterNetId = shooterNetId;
    }

    private void Start()
    {
        if (IsServer)
        {
            Destroy(gameObject, lifeTime);
        }
    }

    private void Update()
    {
        if (!IsServer) return;
        // 전방으로 직진 이동
        transform.position += transform.forward * (speed * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        // 충돌한 대상의 최상위(Root) NetworkObject를 가져옵니다 (자식 콜라이더 감지 대비)
        NetworkObject hitNetObj = other.transform.root.GetComponent<NetworkObject>();

        // 1. 쏜 본인의 최상위 NetworkObject와 일치하면 무시
        if (hitNetObj != null && hitNetObj.NetworkObjectId == _shooterNetId)
        {
            return;
        }

        // 2. 플레이어 본체(PlayerInteraction) 탐색 (부모 컴포넌트 포함)
        PlayerInteraction targetPlayer = other.GetComponentInParent<PlayerInteraction>();
        if (targetPlayer == null)
        {
            targetPlayer = other.GetComponent<PlayerInteraction>();
        }

        if (targetPlayer != null)
        {
            Debug.Log($"[TaserBullet] SUCCESS Hit player: {targetPlayer.name} (IsHost: {targetPlayer.IsHost})");

            // 대상 플레이어에게 감전(이동 속도 저하) 적용
            if (targetPlayer.TryGetComponent<PlayerMovement>(out var movement))
            {
                movement.ApplySlowServer(slowDuration, slowFactor);
            }

            // 맞은 지점에서 가장 가까운 본에 탄이 박힌 연출 (감전이 풀리면 PlayerMovement가 제거)
            if (targetPlayer.TryGetComponent<PlayerTaserStuck>(out var stuck))
            {
                stuck.AttachServer(other.ClosestPoint(transform.position), transform.rotation);
            }

            // 총알 제거
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn();
            }
            return;
        }

        // 3. 지형/벽 충돌 시 제거
        if (!other.isTrigger && ((1 << other.gameObject.layer) & LayerMask.GetMask("Default", "Environment")) != 0)
        {
            Debug.Log("[TaserBullet] Hit Environment!");
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn();
            }
        }
    }
}