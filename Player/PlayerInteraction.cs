using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteraction : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float interactDistance = 3f;
    public NetworkVariable<bool> IsHoldingItem => isHoldingItem;
    private readonly NetworkVariable<bool> isHoldingItem = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private PickupItem heldItem;
    private PickupItem hoveredItem;

    private void Awake()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        UpdateHoveredItem();
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started)
        {
            return;
        }

        if (isHoldingItem.Value)
        {
            DropHeldItemServerRpc();
            return;
        }

        if (playerCamera == null)
        {
            return;
        }

        if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, interactDistance))
        {
            if (hit.collider.TryGetComponent<PickupItem>(out PickupItem pickupItem))
            {
                TryPickupServerRpc(new NetworkObjectReference(pickupItem.NetworkObject));
            }
        }
    }

    [ServerRpc]
    private void TryPickupServerRpc(NetworkObjectReference pickupReference)
    {
        if (isHoldingItem.Value)
        {
            return;
        }

        if (!pickupReference.TryGet(out NetworkObject pickupNetworkObject))
        {
            return;
        }

        if (!pickupNetworkObject.TryGetComponent<PickupItem>(out PickupItem pickupItem))
        {
            return;
        }

        if (!pickupItem.CanBePickedUpBy(this))
        {
            return;
        }

        pickupItem.Pickup(this);
        heldItem = pickupItem;
        isHoldingItem.Value = true;
    }

    [ServerRpc]
    private void DropHeldItemServerRpc()
    {
        if (!isHoldingItem.Value || heldItem == null)
        {
            return;
        }

        heldItem.Drop();
        heldItem = null;
        isHoldingItem.Value = false;
    }

    private void UpdateHoveredItem()
    {
        PickupItem newHoveredItem = null;

        if (playerCamera != null && Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, interactDistance))
        {
            hit.collider.TryGetComponent<PickupItem>(out newHoveredItem);
        }

        if (newHoveredItem == hoveredItem)
        {
            return;
        }

        SetHoveredHighlight(false);

        hoveredItem = newHoveredItem;

        if (hoveredItem != null && !isHoldingItem.Value)
        {
            SetHoveredHighlight(true);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            return;
        }

        if (heldItem != null)
        {
            heldItem.Drop();
            heldItem = null;
        }

        SetHoveredHighlight(false);
        hoveredItem = null;
    }

    private void SetHoveredHighlight(bool highlighted)
    {
        if (hoveredItem == null)
        {
            return;
        }

        PickupHighlight pickupHighlight = hoveredItem.GetComponent<PickupHighlight>();
        if (pickupHighlight != null)
        {
            pickupHighlight.SetHighlighted(highlighted);
        }
    }

    [ServerRpc]
    public void RequestSpawnTrashServerRpc()
    {
        if (!IsServer) return;

        // 호스트 서버 컴퓨터에 도착했으므로, 여기서 안전하게 중앙 매니저의 기능을 실행.
        // 내 넷코드 ID(OwnerClientId)를 매니저에게 넘겨줌.
        ActionManager.Instance.ExecuteSpawnTrash(OwnerClientId);
    }

    // 아이템 강제로 들고있게 하기
    public void ForceSetHeldItem(PickupItem item)
    {
        if (!IsServer) return;

        heldItem = item;
        isHoldingItem.Value = true; // NetworkVariable이므로 모든 클라이언트에게 들고 있다는 상태 동기화됨
    }
}