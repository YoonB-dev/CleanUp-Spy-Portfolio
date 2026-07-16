using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PickupHighlight))]
public class PickupItem : NetworkBehaviour
{
    [Header("Category")]
    [Tooltip("TrashCan 등 범용 시스템이 이 값만 보고 처리 방식을 결정함")]
    [SerializeField] private PickupCategory category = PickupCategory.Trash;
    public PickupCategory Category => category;

    private Rigidbody _itemRigidbody;
    private Collider itemCollider;
    private PickupHighlight pickupHighlight;
    private float carryDistance = 1.5f; public float CarryDistance => carryDistance;
    private float carryHeight = -0.3f; public float CarryHeight => carryHeight;
    private PlayerInteraction _holder;
    public PlayerInteraction Holder => _holder;

    // 외부(예: PolaroidCamera)에서 홀더가 들고 있는지 편하게 확인하기 위한 프로퍼티
    public bool IsHeld => _holder != null;

    private MeshRenderer[] _renderers;

    private ICustomCarryTransform _customCarry;
    private IPickupListener _pickupListener;
    private Vector3 _originalLocalScale;
    private NetworkTransform _networkTransform;
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isVisible.OnValueChanged += OnVisibilityChanged;
        UpdateActualVisibility(_isVisible.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isVisible.OnValueChanged -= OnVisibilityChanged;
        base.OnNetworkDespawn();
        gameObject.SetActive(false);
    }

    private readonly NetworkVariable<bool> _isVisible = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        if (_itemRigidbody == null) _itemRigidbody = GetComponent<Rigidbody>();
        if (itemCollider == null) itemCollider = GetComponent<Collider>();
        if (pickupHighlight == null) pickupHighlight = GetComponent<PickupHighlight>();

        if (_itemRigidbody != null)
        {
            _itemRigidbody.isKinematic = false;
            _itemRigidbody.useGravity = true;
        }
        GetComponent<NetworkObject>().AutoObjectParentSync = false; // 부모 동기화는 직접 RPC로 처리 -> 이걸로 플레이어 프리펩에 따라다니게 하려고 하기 위함
        _originalLocalScale = transform.localScale;
        _renderers = GetComponentsInChildren<MeshRenderer>();
        TryGetComponent(out _customCarry);
        TryGetComponent(out _pickupListener);
        TryGetComponent(out _networkTransform);
    }

    public void SetHighlighted(bool highlighted)
    {
        if (pickupHighlight != null)
        {
            pickupHighlight.SetHighlighted(highlighted);
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
        NetworkObject.ChangeOwnership(playerInteraction.OwnerClientId);

        // RPC를 통해 모든 클라이언트(특히 소유자 로격 클라이언트)에서 물리적 자식화를 수행합니다.
        AttachToHolderClientRpc(playerInteraction.NetworkObjectId);

        // 자식 상태가 완전히 완료된 후 리스너 실행
        _pickupListener?.OnPickedUp();
    }

    public void Drop()
    {
        if (!IsServer || _holder == null)
        {
            return;
        }
        _pickupListener?.OnDropped();
        _holder = null;
        NetworkObject.RemoveOwnership();

        // RPC를 통해 모든 클라이언트에서 자식 관계를 해제하고 월드로 내보냅니다.
        DetachFromHolderClientRpc();
    }

    [ClientRpc]
    private void AttachToHolderClientRpc(ulong holderNetId)
    {
        // 네트워크 ID를 통해 Player 오브젝트를 가져옵니다.
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(holderNetId, out var holderNetObj))
        {
            if (_networkTransform != null) _networkTransform.enabled = false;

            // 물리 계산
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


            Transform targetParent = holderNetObj.transform;

            if (holderNetObj.TryGetComponent<FirstPersonLook>(out var player))
            {
                // PlayerCamera의 부모인 'CameraPivot' 트랜스폼을 찾습니다.
                if (player.PlayerCameraTransform != null && player.PlayerCameraTransform.parent != null)
                {
                    // targetParent를 NetworkObject가 부착된 CameraPivot으로 설정!
                    targetParent = player.PlayerCameraTransform.parent;
                }
                else
                {
                    targetParent = player.PlayerCameraTransform;
                }
            }

            // 1. 물리적 부모를 NetworkObject가 부착된 CameraPivot으로 설정 (에러 해결!)
            transform.SetParent(targetParent, false);

            // 2. CameraPivot의 자식(로컬 좌표계)이 되었으므로, 기준점 계산이 매우 단순하고 정확해집니다.
            if (_customCarry != null && _customCarry.TryGetCarryTransform(targetParent, out Vector3 customPos, out Quaternion customRot))
            {
                // 조준 상태일 때: 해당 기준점의 로컬 좌표 변환 적용
                transform.localPosition = targetParent.InverseTransformPoint(customPos);
                transform.localRotation = Quaternion.Inverse(targetParent.rotation) * customRot;
            }
            else
            {
                // CameraPivot의 정면(Z축)으로 carryDistance, 아래(Y축)로 carryHeight만큼 배치
                transform.localPosition = new Vector3(0, carryHeight, carryDistance);
                transform.localRotation = Quaternion.identity;
            }

            Vector3 parentScale = targetParent.lossyScale;
            transform.localScale = new Vector3(
                _originalLocalScale.x / parentScale.x,
                _originalLocalScale.y / parentScale.y,
                _originalLocalScale.z / parentScale.z
            );
        }
    }

    [ClientRpc]
    private void DetachFromHolderClientRpc()
    {
        transform.SetParent(null); // 부모 연결을 해제해 독립된 월드 오브젝트로 전환
        transform.localScale = _originalLocalScale; // 원래 스케일로 복원
        if (_networkTransform != null) _networkTransform.enabled = true;
        // 물리 계산
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

    public void SetVisibility(bool visible)
    {
        if (!IsServer) return;

        if (_isVisible.Value != visible)
        {
            _isVisible.Value = visible;
        }
    }

    private void OnVisibilityChanged(bool previousValue, bool newValue)
    {
        UpdateActualVisibility(newValue);
    }

    private void UpdateActualVisibility(bool visible)
    {
        if (_renderers != null)
        {
            foreach (var rdr in _renderers)
            {
                if (rdr != null) rdr.enabled = visible;
            }
        }

        if (itemCollider != null && _holder == null)
        {
            itemCollider.enabled = visible;
        }

        if (!visible)
        {
            SetHighlighted(false);
        }
    }
}