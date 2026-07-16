using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(LightInteraction))]
public class PlayerInteraction : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float interactDistance = 3f;
    private BoxPlacementPreview _placementPreview; // 박스 배치 프리뷰를 관리하는 컴포넌트(스크립트)
    private RoleManager _roleManager; // 플레이어 역할 관리 컴포넌트
    private readonly NetworkVariable<NetworkObjectReference> _networkHeldItemRef = new(
        new NetworkObjectReference(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 마피아 페인트 총을 들고 있는지 여부를 나타내는 네트워크 변수
    public readonly NetworkVariable<bool> IsHoldingPaintGun = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private PickupItem hoveredItem;
    private PickupItem _localCachedHeldItem; // 클라이언트 측에서 들고 있는 아이템을 캐싱하여 빠르게 접근할 수 있도록 함.
    private void Awake()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }
        _placementPreview = GetComponent<BoxPlacementPreview>();
        _roleManager = GetComponent<RoleManager>();
        _lightInteraction = GetComponent<LightInteraction>();
    }

    // 두꺼비집 관련 컴포넌트
    private LightInteraction _lightInteraction;
    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        UpdateHoveredItem();
        if (_placementPreview != null)
        {
            _placementPreview.UpdatePreview(GetSafeHeldItem());
        }
    }
    public override void OnNetworkSpawn()
    {
        // 네트워크 변수가 변경되었을 때 클라이언트가 즉각 반응하도록 콜백을 등록합니다.
        _networkHeldItemRef.OnValueChanged += OnHeldItemChanged;
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started)
        {
            return;
        }

        if (IsHoldingPaintGun.Value)
        {
            Debug.Log("페인트 총을 들고 있는 상태에서는 아이템을 조작할 수 없습니다.");
            return;
        }

        if (IsHoldingItem())
        {
            if (_placementPreview != null && _placementPreview.IsPreviewValid)
            {
                // 박스라면 정렬 배치 시스템 가동 (실패하면 알아서 그냥 떨어짐)
                TryPlaceBoxServerRpc(_placementPreview.CurrentPreviewPosition);
                _placementPreview.ClearPreview();
            }
            else
            {
                // 일반 쓰레기 아이템이라면 기존처럼 그냥 그 자리에 툭 떨어뜨리기
                DropHeldItemServerRpc();
            }
            return;
        }

        if (playerCamera == null) return;
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
        if (IsHoldingItem())
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
        _networkHeldItemRef.Value = pickupReference;
    }

    private void UpdateHoveredItem()
    {
        PickupItem newHoveredItem = null;

        if (IsHoldingPaintGun.Value)
        {
            SetHoveredHighlight(false);
            hoveredItem = null;
            return;
        }
        
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

        if (hoveredItem != null && !IsHoldingItem())
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

        PickupItem currentHeldItem = GetCurrentHeldItem();
        if (IsHoldingItem())
        {
            currentHeldItem.Drop();
            _networkHeldItemRef.Value = new NetworkObjectReference();
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
        if (IsHoldingPaintGun.Value)
        {
            return;
        }
        // 호스트 서버 컴퓨터에 도착했으므로, 여기서 안전하게 중앙 매니저의 기능을 실행.
        // 내 넷코드 ID(OwnerClientId)를 매니저에게 넘겨줌.
        ActionManager.Instance.ExecuteSpawnTrash(OwnerClientId);
    }

    // 아이템 강제로 들고있게 하기
    public void ForceSetHeldItem(PickupItem item)
    {
        if (!IsServer) return;

        if (item == null)
        {
            _networkHeldItemRef.Value = new NetworkObjectReference();
        }
        else
        {
            _networkHeldItemRef.Value = new NetworkObjectReference(item.NetworkObject);
        }
    }

    /// <summary>
    /// 들고 있는 박스를 정렬 영역이나 기존 박스 위에 정렬 배치 시도
    /// </summary>
    [ServerRpc]
    private void TryPlaceBoxServerRpc(Vector3 requestedPosition)
    {
        PickupItem currentHeldItem = GetCurrentHeldItem();
        if (!IsHoldingItem() || currentHeldItem == null) return;

        // 클라이언트가 보낸 좌표 근처 발밑에 진짜 바닥 영역(PlacementZone)이 여전히 존재하는지 확인하는 코드임ㅇㅇ
        PlacementZone targetZone = null;
        int zoneLayerMask = LayerMask.GetMask("PlacementZone");

        // 요청된 위치 혹은 그 약간 아래에서 레이를 쏘아 바닥 구역을 역추적함.
        if (Physics.Raycast(requestedPosition + Vector3.up * 0.1f, Vector3.down, out RaycastHit groundHit, 50f, zoneLayerMask))
        {
            targetZone = groundHit.collider.GetComponent<PlacementZone>();
        }

        // 바닥이 존재하고, 가로세로 영역 내에 있으며, '서버 시점'에서도 그 자리가 완벽히 비어있는지 확인.
        if (targetZone != null && PlacementValidator.IsValidPlacement(requestedPosition, targetZone, _placementPreview.gBoxSize, currentHeldItem.gameObject))
        {
            if (currentHeldItem.TryGetComponent<PlaceableBox>(out var placeableBox))
            {
                _networkHeldItemRef.Value = new NetworkObjectReference();
                currentHeldItem.Drop();
                placeableBox.PlaceAt(requestedPosition, Quaternion.identity);
                return;
            }
        }
        // 실패하면 그냥 들고 있던 아이템을 그대로 떨어뜨리기
        DropHeldItemStandard();
    }
    
    // 기존에 사용하시던 일반 드롭 ServerRpc (일반 쓰레기용)
    [ServerRpc]
    private void DropHeldItemServerRpc()
    {
        DropHeldItemStandard();
    }

    // 중복 코드를 줄이기 위한 내부 실제 드롭 처리 함수
    private void DropHeldItemStandard()
    {
        PickupItem currentHeldItem = GetCurrentHeldItem();
        if (!IsServer || currentHeldItem == null) return;
        _networkHeldItemRef.Value = new NetworkObjectReference();
        currentHeldItem.Drop();
    }

    public bool IsHoldingItem()
    {
        // 네트워크 참조에 아무것도 등록되지 않은 상태(기본값)인지 확인합니다.
        return _networkHeldItemRef.Value.NetworkObjectId != 0;
    }
    /// <summary>
    /// 네트워크 변수로부터 현재 들고 있는 PickupItem 컴포넌트를 안전하게 긁어옵니다.
    /// </summary>
    public PickupItem GetCurrentHeldItem()
    {
        if (!IsHoldingItem())
        {
            return null;
        }

        if (_networkHeldItemRef.Value.TryGet(out NetworkObject netObj))
        {
            return netObj.GetComponent<PickupItem>();
        }

        return null;
    }
    // 클라이언트 측에서 들고 있는 아이템을 캐싱하여 빠르게 접근할 수 있도록 함.
    private PickupItem GetSafeHeldItem()
    {
        if (!IsHoldingItem())
        {
            _localCachedHeldItem = null;
            return null;
        }

        if (_localCachedHeldItem != null)
        {
            return _localCachedHeldItem;
        }

        _localCachedHeldItem = GetCurrentHeldItem();
        return _localCachedHeldItem;
    }
    /// <summary>
    /// 네트워크 변수가 동기화 완료되었을 때 클라이언트 측 프리뷰를 즉시 갱신해주는 콜백
    /// </summary>
    private void OnHeldItemChanged(NetworkObjectReference previous, NetworkObjectReference current)
    {
        if (!IsOwner || _placementPreview == null)
        {
            return;
        }
        // 1. 이전 아이템을 내려놓았을 때: 이전 아이템의 콜라이더를 다시 켜줍니다.
        if (previous.TryGet(out NetworkObject prevNetObj))
        {
            if (prevNetObj != null && prevNetObj.TryGetComponent<Collider>(out var prevCollider))
            {
                prevCollider.enabled = true; // 콜라이더 복구
            }
        }

        // 2. 새로운 아이템을 주웠을 때: 내 눈앞을 가리지 않도록 클라이언트 로컬에서도 콜라이더를 끕니다.
        if (current.TryGet(out NetworkObject currentNetObj))
        {
            if (currentNetObj != null && currentNetObj.TryGetComponent<Collider>(out var currentCollider))
            {
                currentCollider.enabled = false; // 클라이언트에서도 콜라이더 강제 정지! -> 이거 때문에 설치 오류 발생했음 슈발
            }
        }

        if (!IsOwner || _placementPreview == null)
        {
            return;
        }

        _placementPreview.UpdatePreview(GetSafeHeldItem());
    }


    // 청소 도구
    public void OnClean(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (!IsHoldingItem()) return;
        // '청소 도구'인지 확인
        var heldItem = GetSafeHeldItem();
        if (heldItem != null && heldItem.TryGetComponent<PaintCleaner>(out var cleaner))
        {
            if (context.performed) cleaner.SetCleaningInput(true);
            else if (context.canceled) cleaner.SetCleaningInput(false);
        }
    }

    // 폴라로이드 카메라
    public void OnAim(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (!IsHoldingItem()) return;
        var heldItem = GetSafeHeldItem();
        if (heldItem == null || !heldItem.TryGetComponent<PolaroidCamera>(out var cameraTool)) return;
        if (context.performed) cameraTool.Aim(true);
        else if (context.canceled) cameraTool.Aim(false);
    }

    public void OnCapture(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (!context.performed) return;
        if (!IsHoldingItem()) return;
        var heldItem = GetSafeHeldItem();
        if (heldItem == null || !heldItem.TryGetComponent<PolaroidCamera>(out var cameraTool)) return;

        cameraTool.Capture();
    }
    // 두꺼비집 동작 콜백
    public void OnToggleLight(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (IsHoldingPaintGun.Value || IsHoldingItem()) return;
        if (_lightInteraction == null) return;

        if (context.started)
        {
            if (playerCamera == null) return;

            // 앞에 스위치가 있는지 레이캐스트 검사만 수행
            if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, interactDistance))
            {
                if (hit.collider.TryGetComponent<LightSwitch>(out LightSwitch lightSwitch))
                {
                    // 구체적인 처리는 새 컴포넌트에게 전임!
                    _lightInteraction.StartLightInteraction(lightSwitch);
                }
            }
        }
        else if (context.canceled)
        {
            // 손 떼면 취소하라고 신호만 보냄
            _lightInteraction.CancelLightInteraction();
        }
    }
}