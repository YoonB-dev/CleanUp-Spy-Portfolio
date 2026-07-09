using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PickupHighlight))]
public class PickupItem : NetworkBehaviour
{
    private Rigidbody _itemRigidbody;
    private Collider itemCollider;
    private PickupHighlight pickupHighlight;
    private float carryDistance = 1.6f;
    private float carryHeight = -0.5f;

    private PlayerInteraction _holder;
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        // 서버에서 연결이 끊기면 모든 클라이언트에서 아이템을 비활성화 -> 바로 삭제되는 방식은 문제 발생할 수 있다고 해서 약간 방어적으로 작성함.
        gameObject.SetActive(false);
    }

    private void Awake()
    {
        if (_itemRigidbody == null)
        {
            _itemRigidbody = GetComponent<Rigidbody>();
        }

        if (itemCollider == null)
        {
            itemCollider = GetComponent<Collider>();
        }

        if (pickupHighlight == null)
        {
            pickupHighlight = GetComponent<PickupHighlight>();
        }

        if (_itemRigidbody != null)
        {
            _itemRigidbody.isKinematic = false;
            _itemRigidbody.useGravity = true;
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        if (pickupHighlight != null)
        {
            pickupHighlight.SetHighlighted(highlighted);
        }
    }

    private void Update()
    {
        if (_holder == null || (!IsServer && !_holder.IsOwner))
        {
            return;
        }

        // 돌진중이면 스킵
        if (_holder.TryGetComponent<MafiaDashSkill>(out var dashSkill) && dashSkill.isDashing)
        {
            return;
        }
        if (_holder.TryGetComponent<FirstPersonLook>(out var player))
        {
            Transform camTransform = player.PlayerCameraTransform;

            if (camTransform != null)
            {
                // 2. 카메라의 정면(forward) 방향으로 carryDistance만큼 띄우고, 
                // 카메라 기준의 정중앙에 위치시키기 위해 약간 아래나 위로 조절하고 싶다면 camTransform.up을 활용.
                // (가운데 딱 맞추려면 Vector3.up * carryHeight 대신 살짝만 내리거나 0으로 두면 됨ㅇㅇ)
                Vector3 followPosition = camTransform.position + camTransform.forward * carryDistance + camTransform.up * carryHeight;

                transform.position = followPosition;

                // 3. 아이템의 회전도 카메라가 바라보는 회전과 일치시킨다.
                transform.rotation = camTransform.rotation;
            }
        }
        else
        {
            // 만약 플레이어 스크립트를 못 찾았다면 기존 백업 로직 수행
            Transform holderTransform = _holder.transform;
            Vector3 followPosition = holderTransform.position + holderTransform.forward * carryDistance + Vector3.up * carryHeight;
            transform.position = followPosition;
            transform.rotation = holderTransform.rotation;
        }
    }

    public bool CanBePickedUpBy(PlayerInteraction playerInteraction)
    {
        if (!IsServer || playerInteraction == null || _holder != null)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, playerInteraction.transform.position);
        return distance <= 3.5f;
    }

    public void Pickup(PlayerInteraction playerInteraction)
    {
        if (!IsServer || playerInteraction == null || _holder != null)
        {
            return;
        }

        _holder = playerInteraction;
        SetHighlighted(false);

        if (TryGetComponent<PlaceableBox>(out var placeableBox))
        {
            placeableBox.OnPickedUp(); // 이 안에서 Demolish()가 돌며 아랫장 연쇄 물리 연산 시동
        }

        if (_itemRigidbody != null)
        {
            _itemRigidbody.linearVelocity = Vector3.zero;
            _itemRigidbody.angularVelocity = Vector3.zero;
            _itemRigidbody.isKinematic = true;
            _itemRigidbody.useGravity = false;
        }

        if (itemCollider != null)
        {
            itemCollider.enabled = false;
        }
    }

    public void Drop()
    {
        if (!IsServer || _holder == null)
        {
            return;
        }

        _holder = null;

        if (itemCollider != null)
        {
            itemCollider.enabled = true;
        }

        if (_itemRigidbody != null)
        {
            _itemRigidbody.isKinematic = false;
            _itemRigidbody.useGravity = true;
            _itemRigidbody.linearVelocity = Vector3.zero;
            _itemRigidbody.angularVelocity = Vector3.zero;
        }
    }
}