using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 쓰레기통(TrashCan) 스크립트
/// PickupItem이 쓰레기통에 들어오면 ScoreManager에서 점수 올리는 함수 호출. (충돌 검사용 스크립트에 가까움)
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TrashCan : NetworkBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        // 들어온 오브젝트가 '들 수 있는 아이템(PickupItem)'인지 확인
        // 확장성을 고려해서 Tag 대신 컴포넌트 기반으로 판단함.
        if (other.TryGetComponent<PickupItem>(out PickupItem item))
        {
            ProcessItemDisposal(item);
        }
    }

    private void ProcessItemDisposal(PickupItem item)
    {
        ScoreManager scoreManager = FindAnyObjectByType<ScoreManager>();
        if (scoreManager != null)
        {
            scoreManager.AddTrashScore();
        }

        if (item.NetworkObject != null && item.NetworkObject.IsSpawned)
        {
            item.NetworkObject.Despawn(false);
        }
    }
}
