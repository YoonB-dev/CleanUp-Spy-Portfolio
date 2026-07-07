using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PickupHighlight))]
public class PickupItem : NetworkBehaviour
{
    private Rigidbody itemRigidbody;
    private Collider itemCollider;
    private PickupHighlight pickupHighlight;
    private float carryDistance = 1.25f;
    private float carryHeight = -1.0f;

    private PlayerInteraction holder;
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        // 서버에서 연결이 끊기면 모든 클라이언트에서 아이템을 비활성화 -> 바로 삭제되는 방식은 문제 발생할 수 있다고 해서 약간 방어적으로 작성함.
        gameObject.SetActive(false);
    }

    private void Awake()
    {
        if (itemRigidbody == null)
        {
            itemRigidbody = GetComponent<Rigidbody>();
        }

        if (itemCollider == null)
        {
            itemCollider = GetComponent<Collider>();
        }

        if (pickupHighlight == null)
        {
            pickupHighlight = GetComponent<PickupHighlight>();
        }

        if (itemRigidbody != null)
        {
            itemRigidbody.isKinematic = false;
            itemRigidbody.useGravity = true;
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
        if (!IsServer || holder == null)
        {
            return;
        }

        Transform holderTransform = holder.transform;
        Vector3 followPosition = holderTransform.position + holderTransform.forward * carryDistance + Vector3.up * carryHeight;
        transform.position = followPosition;
        transform.rotation = holderTransform.rotation;
    }

    public bool CanBePickedUpBy(PlayerInteraction playerInteraction)
    {
        if (!IsServer || playerInteraction == null || holder != null)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, playerInteraction.transform.position);
        return distance <= 3.5f;
    }

    public void Pickup(PlayerInteraction playerInteraction)
    {
        if (!IsServer || playerInteraction == null || holder != null)
        {
            return;
        }

        holder = playerInteraction;
        SetHighlighted(false);

        if (itemRigidbody != null)
        {
            itemRigidbody.linearVelocity = Vector3.zero;
            itemRigidbody.angularVelocity = Vector3.zero;
            itemRigidbody.isKinematic = true;
            itemRigidbody.useGravity = false;
        }

        if (itemCollider != null)
        {
            itemCollider.enabled = false;
        }
    }

    public void Drop()
    {
        if (!IsServer || holder == null)
        {
            return;
        }

        holder = null;

        if (itemCollider != null)
        {
            itemCollider.enabled = true;
        }

        if (itemRigidbody != null)
        {
            itemRigidbody.isKinematic = false;
            itemRigidbody.useGravity = true;
            itemRigidbody.linearVelocity = Vector3.zero;
            itemRigidbody.angularVelocity = Vector3.zero;
        }
    }
}