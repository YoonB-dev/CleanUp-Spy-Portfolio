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
        if (!other.TryGetComponent<PickupItem>(out PickupItem item)) return;
        // PlaceableBox를 직접 아는 대신, Category만 확인 (Trash가 아니면 무시).
        // 새 아이템 종류가 늘어나도 이 파일은 건드릴 필요 없음 - 프리팹의 Category 값만 지정하면 됨.
        if (item.Category != PickupCategory.Trash)
        {
            return;
        }

        ProcessItemDisposal(item);
    }

    private void ProcessItemDisposal(PickupItem item)
    {
        if (item.TryGetComponent<TrashObject>(out TrashObject trashObj) && trashObj.Data != null)
        {
            TrashData data = trashObj.Data;

            // 2. SO 데이터 기반 점수 추가
            ScoreManager.Instance?.AddTrashScore(data.score);

            // 3. 연출 처리 (서버 -> 모든 클라이언트 RPC 전파)
            PlayDisposalFXClientRpc(item.transform.position, data.trashID);
        }
        else
        {
            // TrashData를 못 찾았을 때 예외 처리용 기본 점수
            ScoreManager.Instance?.AddTrashScore(10);
        }

        // 4. 네트워크 오브젝트 디스폰
        if (item.NetworkObject != null && item.NetworkObject.IsSpawned)
        {
            item.NetworkObject.Despawn(true);
        }
    }

    [ClientRpc]
    private void PlayDisposalFXClientRpc(Vector3 position, string trashID)
    {
        // 나중에 여기 사운드 넣을 꺼임. -> 쓰레기 처리하느 사운드
    }
}
