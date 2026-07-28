using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteraction : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float interactDistance = 3f;
    private BoxPlacementPreview _placementPreview; // 박스 배치 프리뷰를 관리하는 컴포넌트(스크립트)
    private PlayerInventory _inventory; // 플레이어의 인벤토리 인터페이스
    public PlayerInventory Inventory => _inventory; // 외부에서 인벤토리 접근용
    private PickupItem hoveredItem; // 플레이어가 현재 바라보고 있는 아이템
    // 두꺼비집 관련 컴포넌트
    private LightInteraction _lightInteraction;
    // ======== 아이템 던지기(강하게) 관련 변수 ========
    private float gaugeChargeTime = 1.5f; // 게이지가 최대치까지 충전되는 시간
    private float minThrowForce = 4f; // 최소 던지기 힘 -> 0.3초에서 시작
    private float maxThrowForce = 15f; // 최대 던지기 힘 -> 2초에서 최대
    private float _dropKeyPressTime; // 키를 누르기 시작한 시간
    private bool _isChargingThrow = false;
    private float _currentThrowGauge = 0f; // 0 ~ 1 사이의 UI용 게이지 값
    public float CurrentThrowGauge => _currentThrowGauge; // UI에서 접근할 프로퍼티
    [Header("Throw Rotation Settings")]
    [SerializeField] private float minThrowTorque = 1f;  // 살짝 던졌을 때의 회전력
    [SerializeField] private float maxThrowTorque = 8f;  // 풀차징으로 던졌을 때의 회전력
    [SerializeField] private InputActionReference dropActionRef;
    private void Awake()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }
        _placementPreview = GetComponent<BoxPlacementPreview>();
        _lightInteraction = GetComponent<LightInteraction>();
        _inventory = GetComponent<PlayerInventory>();
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        UpdateHoveredItem();
        if (_placementPreview != null)
        {
            _placementPreview.UpdatePreview(GetCurrentHeldItem());
        }

        // 던지기 게이지 충전
        if(_isChargingThrow)
        {
            float holdDuration = Time.time - _dropKeyPressTime;

            // 0초부터 바로 오르기 시작하며, 2초가 지나도 1f 상태를 유지합니다.
            _currentThrowGauge = Mathf.Clamp01(holdDuration / gaugeChargeTime);
        }
    }

    /// <summary>
    /// [줍기 전용 키] 기존 OnInteract의 줍기 로직만 상속받음
    /// </summary>
    public void OnPickupInput(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started) return;

        // 이미 아이템을 들고 있다면 줍기 스킵 (중복 방지)
        if (IsHoldingItem())
        {
            if (_placementPreview != null && _placementPreview.IsPreviewValid)
            {
                // 프리뷰가 올바른 상태이므로 서버에 박스 배치 요청
                TryPlaceBoxServerRpc(_placementPreview.CurrentPreviewPosition);
                _placementPreview.ClearPreview();
            }
            else
            {
                Debug.Log("상자를 들고 있지만 배치가 불가능한 영역입니다.");
            }
            return; // 배치를 시도했으므로 아래 줍기 로직은 타지 않음
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

    /// <summary>
    /// [버리기 전용 키] 기존 OnInteract의 버리기 및 박스 배치 로직만 상속받음
    /// </summary>
    public void OnDropInput(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        if (_inventory != null && _inventory.CurrentSlot == 4)
        {
            return;
        }

        // 손에 든 아이템이 있어야만 버릴 수 있음
        if (context.started)
        {
            if (!IsHoldingItem()) return;

            _dropKeyPressTime = Time.time;
            _isChargingThrow = true;
            _currentThrowGauge = 0f;
        }
        else if (context.canceled)
        {
            // 예외 방어: 충전 중이 아니었다면 리턴
            if (!_isChargingThrow) return;

            _isChargingThrow = false;
            float holdDuration = Time.time - _dropKeyPressTime;

            if (playerCamera != null)
            {
                Vector3 lookDirection = playerCamera.transform.forward;
                // 클라이언트는 조준 방향과 "누르고 있던 시간"만 전달하고 처리는 서버에 전임합니다.
                RequestDropOrThrowServerRpc(lookDirection, holdDuration);
            }

            // 로컬 수치 초기화
            _currentThrowGauge = 0f;
            if (_placementPreview != null) _placementPreview.ClearPreview();
        }
    }

    [ServerRpc]
    public void TryPickupServerRpc(NetworkObjectReference pickupReference)
    {
        PickupLogicalServer(pickupReference);
    }

    /// <summary>
    /// 실제 서버에서 아이템 줍기를 처리하는 핵심 비즈니스 로직 (RPC가 아니므로 서버 내부에서 자유롭게 호출 가능)
    /// </summary>
    public void PickupLogicalServer(NetworkObjectReference pickupReference, bool forcePickup = false)
    {
        if (!IsServer) return; // 서버 측 방어 코드

        if (IsHoldingItem() || !pickupReference.TryGet(out NetworkObject pickupNetworkObject))
        {
            return;
        }

        if (!pickupNetworkObject.TryGetComponent<PickupItem>(out PickupItem pickupItem))
        {
            return;
        }

        if (!forcePickup && !pickupItem.CanBePickedUpBy(this))
        {
            return;
        }

        if (_inventory.TryAddItem(pickupItem))
        {
            // 인벤토리에 들어갔으므로 줍기 실행 (부모 설정 및 RPC 전달)
            pickupItem.Pickup(this);
            _inventory.RefreshInventoryVisuals();
        }
        else
        {
            Debug.Log("인벤토리가 가득 찼습니다!");
        }
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

        if (hoveredItem != null)
        {
            // 1. 페인트 총(4번)을 들고 있을 때는 '백팩(1~3번)이 꽉 찼는지'가 기준이 된다.
            if (_inventory != null && _inventory.CurrentSlot == 4)
            {
                if (_inventory.IsBackpackFull()) return; // 꽉 찼으면 하이라이트 안 켬
            }
            // 2. 일반 슬롯(0~3번)일 때는 현재 손에 무언가 들고 있다면 하이라이트 안 켬
            else
            {
                if (IsHoldingItem()) return;
            }

            // 위의 줍기 불가 조건을 모두 통과했다면 하이라이트를 킨다.
            SetHoveredHighlight(true);
        }
    }

    public override void OnNetworkDespawn()
    {
        SetHoveredHighlight(false);
        hoveredItem = null;

        // 혹시 인벤토리에 들고 있는 아이템이 있었으면 다 한번에 내려놓게 하기

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

    /// <summary>
    /// 들고 있던 아이템을 그 자리에 떨어뜨립니다. 피격처럼 플레이어 의사와 무관하게 놓칠 때 호출합니다. [서버 전용]
    /// </summary>
    public void ServerDropHeldItem()
    {
        DropHeldItemStandard();
    }

    // 중복 코드를 줄이기 위한 내부 실제 드롭 처리 함수
    private void DropHeldItemStandard()
    {
        if (!IsServer) return;

        // 인벤토리의 현재 슬롯에 구현된 아이템의 Drop을 호출합니다.
        if (_inventory != null)
        {
            _inventory.DropCurrentItemDirectServer();
        }
    }

    public bool IsHoldingItem()
    {
        if (_inventory == null) return false;

        // 인벤토리에서 현재 들고 있는 아이템이 null이 아니면 true
        return _inventory.GetCurrentEquippedItem() != null;
    }
    /// <summary>
    /// 네트워크 변수로부터 현재 들고 있는 PickupItem 컴포넌트를 안전하게 긁어옵니다.
    /// </summary>
    public PickupItem GetCurrentHeldItem()
    {
        if (_inventory == null) return null;
        return _inventory.GetCurrentEquippedItem();
    }

    // 청소 도구
    public void OnClean(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (!IsHoldingItem()) return;
        // '청소 도구'인지 확인
        var heldItem = GetCurrentHeldItem();
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
        var heldItem = GetCurrentHeldItem();
        if (heldItem == null || !heldItem.TryGetComponent<PolaroidCamera>(out var cameraTool)) return;
        if (context.performed) cameraTool.Aim(true);
        else if (context.canceled) cameraTool.Aim(false);
    }

    public void OnCapture(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (!context.performed) return;
        if (!IsHoldingItem()) return;
        var heldItem = GetCurrentHeldItem();
        if (heldItem == null || !heldItem.TryGetComponent<PolaroidCamera>(out var cameraTool)) return;

        cameraTool.Capture();
    }
    // 두꺼비집 동작 콜백
    public void OnToggleLight(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (IsHoldingItem()) return;
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

    #region [아이템 던지기 관련 로직]

    [ServerRpc]
    private void RequestDropOrThrowServerRpc(Vector3 direction, float holdDuration)
    {
        if (!IsServer) return;

        PickupItem currentHeldItem = GetCurrentHeldItem();
        if (currentHeldItem == null) return;

        // 1. 공통 처리: 먼저 아이템 인벤토리 관계 해제 및 가시성 회복
        currentHeldItem.SetVisibility(true);
        if (_inventory != null)
        {
            _inventory.ClearItemFromSlots(currentHeldItem);
        }

        // 던지는 플레이어 id구함
        ulong throwerNetId = this.NetworkObjectId;
        // 2. 누른 시간에 따라 분기 처리
        if (holdDuration < 0.3f)
        {
            // 0.3초 미만: 그냥 앞에 툭 떨어뜨리기 (힘 0)
            currentHeldItem.ThrowFromServer(direction, 0f, Vector3.zero, throwerNetId);
        }
        else
        {
            // 0.3초 이상: 서버에서 안전하게 Force를 연산하여 물리 발사
            float clampedProgress = Mathf.Clamp01(holdDuration / gaugeChargeTime);
            float finalForce = Mathf.Lerp(minThrowForce, maxThrowForce, clampedProgress);
            float finalTorqueMagnitude = Mathf.Lerp(minThrowTorque, maxThrowTorque, clampedProgress);
            Vector3 randomTorque = Random.insideUnitSphere.normalized * finalTorqueMagnitude;
            currentHeldItem.ThrowFromServer(direction, finalForce, randomTorque, throwerNetId);
        }
    }

    #endregion
}