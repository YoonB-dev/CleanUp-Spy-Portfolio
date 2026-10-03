using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 쓰레기통(TrashCan) 스크립트
/// PickupItem이 쓰레기통에 들어오면 ScoreManager에서 점수 올리는 함수 호출. (충돌 검사용 스크립트에 가까움)
/// 쓰레기(Trash)가 아닌 아이템이 들어오면 위쪽으로 튕겨낸다.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TrashCan : NetworkBehaviour
{
    [Header("Reject Bounce Settings")]
    [Tooltip("쓰레기가 아닌 아이템을 위로 튕겨내는 속도")]
    [SerializeField] private float rejectUpSpeed = 5.0f;
    [Tooltip("통 바깥쪽(수평)으로 밀어내는 속도")]
    [SerializeField] private float rejectOutwardSpeed = 1.5f;
    [Tooltip("바깥쪽 방향에 섞을 랜덤 각도 (좌우로 ±값)")]
    [SerializeField] private float rejectRandomAngle = 30.0f;
    [Tooltip("튕길 때 가하는 랜덤 회전 속도")]
    [SerializeField] private float rejectSpinSpeed = 5.0f;
    [Tooltip("같은 아이템을 다시 튕겨내기까지의 대기 시간 (통 안에서 덜덜 떨리는 것 방지)")]
    [SerializeField] private float rejectCooldown = 0.3f;

    [Header("Sound")]
    [Tooltip("쓰레기를 버릴 때 공통 소리 (TrashData에 cleanSFX가 있으면 그게 우선)")]
    [SerializeField] private SoundData disposeSound;
    [Tooltip("쓰레기가 아닌 아이템을 튕겨낼 때 소리")]
    [SerializeField] private SoundData rejectSound;

    // 아이템별 마지막으로 튕겨낸 시간 (서버 전용)
    private readonly Dictionary<PickupItem, float> _lastRejectTime = new Dictionary<PickupItem, float>();

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!other.TryGetComponent<PickupItem>(out PickupItem item)) return;
        // PlaceableBox를 직접 아는 대신, Category만 확인 (Trash가 아니면 튕겨냄).
        // 새 아이템 종류가 늘어나도 이 파일은 건드릴 필요 없음 - 프리팹의 Category 값만 지정하면 됨.
        if (item.Category != PickupCategory.Trash)
        {
            TryRejectItem(item, other.attachedRigidbody);
            return;
        }

        ProcessItemDisposal(item);
    }

    /// <summary>
    /// 약하게 튕겨서 다시 떨어지거나 통 안에 머무는 경우를 위해 Stay에서도 튕겨냄 (쿨다운으로 간격 조절)
    /// </summary>
    private void OnTriggerStay(Collider other)
    {
        if (!IsServer) return;
        if (!other.TryGetComponent<PickupItem>(out PickupItem item)) return;
        if (item.Category == PickupCategory.Trash) return;

        TryRejectItem(item, other.attachedRigidbody);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;
        if (other.TryGetComponent<PickupItem>(out PickupItem item))
        {
            _lastRejectTime.Remove(item);
        }
    }

    /// <summary>
    /// 쓰레기가 아닌 아이템을 위쪽 + 통 바깥쪽(약간 랜덤)으로 튕겨냅니다.
    /// 들고 있는 아이템, 고정된(kinematic) 아이템은 제외합니다.
    /// </summary>
    private void TryRejectItem(PickupItem item, Rigidbody rb)
    {
        if (item.IsHeld || item.Holder != null) return;
        if (rb == null || rb.isKinematic) return;

        if (_lastRejectTime.TryGetValue(item, out float lastTime) && Time.time - lastTime < rejectCooldown) return;
        _lastRejectTime[item] = Time.time;

        // 통 중심에서 아이템 쪽으로 향하는 수평 방향 (거의 중앙이면 랜덤 방향)
        Vector3 outward = rb.position - transform.position;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            Vector2 random = Random.insideUnitCircle.normalized;
            outward = new Vector3(random.x, 0f, random.y);
        }
        outward = Quaternion.AngleAxis(Random.Range(-rejectRandomAngle, rejectRandomAngle), Vector3.up) * outward.normalized;

        // 기존 속도(떨어지던 속도 등)를 무시하고 튕겨내는 속도로 덮어씀 → 질량과 무관하게 일정하게 튐
        rb.linearVelocity = (Vector3.up * rejectUpSpeed) + (outward * rejectOutwardSpeed);
        rb.angularVelocity = Random.onUnitSphere * rejectSpinSpeed;

        PlayRejectFXClientRpc(rb.position);
    }

    private void ProcessItemDisposal(PickupItem item)
    {
        if (item.TryGetComponent<TrashObject>(out TrashObject trashObj) && trashObj.Data != null)
        {
            TrashData data = trashObj.Data;

            // 2. SO 데이터 기반 점수 추가
            ScoreManager.Instance?.AddTrashScore(data.score);

            // 3. 연출 처리 (서버 -> 모든 클라이언트 RPC 전파)
            PlayDisposalFXClientRpc(item.transform.position, data.itemID);
        }
        else
        {
            // TrashData를 못 찾았을 때 예외 처리용 기본 점수
            ScoreManager.Instance?.AddTrashScore(10);

            // 연출은 공통 소리로 처리
            PlayDisposalFXClientRpc(item.transform.position, string.Empty);
        }

        // 4. 네트워크 오브젝트 디스폰
        if (item.NetworkObject != null && item.NetworkObject.IsSpawned)
        {
            if (item.NetworkObject.InScenePlaced)
            {
                // 씬 배치 오브젝트는 파괴하면 NGO 씬 관리가 꼬이므로 디스폰 후 비활성화만 한다.
                // Despawn(false)는 클라 쪽을 숨기지 않으니 디스폰 메시지보다 먼저 숨김 RPC를 보낸다.
                HideSceneItemClientRpc(item.NetworkObject);
                item.NetworkObject.Despawn(false);
                item.gameObject.SetActive(false);
            }
            else
            {
                item.NetworkObject.Despawn(true);
            }
        }
    }

    [ClientRpc]
    private void HideSceneItemClientRpc(NetworkObjectReference itemRef)
    {
        if (itemRef.TryGet(out NetworkObject itemObj))
        {
            itemObj.gameObject.SetActive(false);
        }
    }

    [ClientRpc]
    private void PlayDisposalFXClientRpc(Vector3 position, string itemID)
    {
        // 쓰레기 종류별 소리(TrashData.cleanSFX)가 있으면 그걸, 없으면 공통 소리
        // (쓰레기 오브젝트는 곧 디스폰되므로 itemID로 데이터를 찾음)
        if (ItemData.TryGetById(itemID, out TrashData data) && data.cleanSFX != null)
        {
            SoundManager.Instance?.PlaySFXAt(data.cleanSFX, position);
        }
        else
        {
            SoundManager.Instance?.PlaySFXAt(disposeSound, position);
        }
    }

    [ClientRpc]
    private void PlayRejectFXClientRpc(Vector3 position)
    {
        SoundManager.Instance?.PlaySFXAt(rejectSound, position);
    }
}
