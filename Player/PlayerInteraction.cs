using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteraction : NetworkBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private Camera playerCamera;
    public Camera PlayerCamera => playerCamera; // 외부에서 접근 가능하도록 프로퍼티 제공
    [SerializeField] private float interactDistance = 5f;
    [SerializeField] private float pickupRadius = 0.2f;    // 줍기 판정 구체 반지름 (작은 아이템 보정용)
    [SerializeField] private LayerMask pickupLayerMask = ~0;

    private BoxPlacementPreview _placementPreview; // 박스 배치 프리뷰를 관리하는 컴포넌트(스크립트)
    private PlayerInventory _inventory; // 플레이어의 인벤토리 인터페이스
    public PlayerInventory Inventory => _inventory; // 외부에서 인벤토리 접근용
    private PickupItem hoveredItem; // 플레이어가 현재 바라보고 있는 아이템
    private DraggableObject hoveredDraggable; // 플레이어가 현재 바라보고 있는 드래그 가능한 오브젝트
    // 두꺼비집 관련 컴포넌트
    private LightInteraction _lightInteraction;
    private PlayerActionGate _gate;

    // ======== 아이템 던지기(강하게) 관련 변수 ========
    private float gaugeChargeTime = 1.5f; // 게이지가 최대치까지 충전되는 시간
    private float minThrowForce = 2f; // 최소 던지기 힘 -> 0.3초에서 시작
    private float maxThrowForce = 10f; // 최대 던지기 힘 -> 2초에서 최대
    private float _dropKeyPressTime; // 키를 누르기 시작한 시간
    private bool _isChargingThrow = false;
    private float _currentThrowGauge = 0f; // 0 ~ 1 사이의 UI용 게이지 값
    public float CurrentThrowGauge => _currentThrowGauge; // UI에서 접근할 프로퍼티

    [Header("Throw Rotation Settings")]
    [SerializeField] private float minThrowTorque = 1f;  // 살짝 던졌을 때의 회전력
    [SerializeField] private float maxThrowTorque = 8f;  // 풀차징으로 던졌을 때의 회전력
    
    public RagdollPoser playerRagDollPoser; // 인스펙터에서 연결
    private DraggableObject _activeDragableObject; // 현재 끌고 있는 드래그 오브젝트(예: 이동식 분쇄기)
    [SerializeField] private LayerMask interactLayerMask = ~0;
    private void Awake()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }
        _placementPreview = GetComponent<BoxPlacementPreview>();
        _lightInteraction = GetComponent<LightInteraction>();
        _inventory = GetComponent<PlayerInventory>();
        _gate = PlayerActionGate.GetOrAdd(gameObject);
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
    /// 구체 내부에서 가장 조준선(카메라 정면)에 가까운 PickupItem을 선별해서 가져옵니다.
    /// </summary>
    private PickupItem GetTargetPickupItem()
    {
        if (playerCamera == null) return null;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        // 1. 먼저 정밀한 Raycast 시도 (작은 아이템이라도 조준점에 정확히 걸리면 최우선)
        if (Physics.Raycast(ray, out RaycastHit rayHit, interactDistance, pickupLayerMask))
        {
            if (rayHit.collider.TryGetComponent<PickupItem>(out PickupItem exactItem))
            {
                return exactItem;
            }
        }

        // 2. Raycast 실패 시 SphereCastAll로 주변 영역의 모든 히트 오브젝트 탐색
        RaycastHit[] hits = Physics.SphereCastAll(ray, pickupRadius, interactDistance, pickupLayerMask);

        PickupItem bestItem = null;
        float closestDistance = float.MaxValue;

        foreach (var hit in hits)
        {
            // 플레이어 자신 또는 자식 콜라이더는 제외
            if (hit.collider.transform.IsChildOf(transform)) continue;

            if (hit.collider.TryGetComponent<PickupItem>(out PickupItem item))
            {
                // 가장 가까운 거리에 있는 아이템 선택
                if (hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    bestItem = item;
                }
            }
        }

        return bestItem;
    }

    /// <summary>
    /// [줍기 전용 키] 기존 OnInteract의 줍기 로직만 상속받음
    /// </summary>
    public void OnPickupInput(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started) return;

        // 이미 파쇄기를 끌고 있다면 줍기 스킵 (중복 방지)
        if (_activeDragableObject != null)
        {
            RequestStopDragServerRpc(new NetworkObjectReference(_activeDragableObject.NetworkObject));
            GetComponent<PlayerMovement>()?.SetDraggleObject(null);
            // 래그돌 손 뻗기 자세 해제
            if (playerRagDollPoser != null)
            {
                playerRagDollPoser.SetCarryRequested(false);
                playerRagDollPoser.SetCarryTarget(null);
            }

            _activeDragableObject = null;
            return;
        }

        // 이미 아이템을 들고 있다면 줍기 스킵 (중복 방지)
        if (IsHoldingItem())
        {
            if (!_gate.CanDo(PlayerAction.PlaceBox)) return;

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

        // 3. 손에 아무것도 없고, 바라보는 곳에 파쇄기가 있는지 확인
        if (playerCamera != null && Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, interactDistance, interactLayerMask))
        {
            if (hit.collider.TryGetComponent<DraggableObject>(out var draggable))
            {
                // 다른 사람이 끌고 있지 않고, 서버 검증과 같은 게이트를 통과할 때만 시작 (거절돼서 서버와 상태가 어긋나는 것 방지)
                if (!draggable.IsBeingDragged && _gate.CanDo(PlayerAction.DragObject))
                {
                    draggable.SetHighlighted(true);
                    _activeDragableObject = draggable;
                    RequestStartDragServerRpc(new NetworkObjectReference(_activeDragableObject.NetworkObject));
                    GetComponent<PlayerMovement>()?.SetDraggleObject(_activeDragableObject);
                    return;
                }
            }
        }

        // 4. 손에 아무것도 없고, 바라보는 곳에 줍기 가능한 아이템이 있는지 확인
        if (!_gate.CanDo(PlayerAction.Pickup)) return;

        // 통합 탐색 함수 사용
        PickupItem targetItem = GetTargetPickupItem();
        if (targetItem != null)
        {
            TryPickupServerRpc(new NetworkObjectReference(targetItem.NetworkObject));
        }
    }

    /// <summary>
    /// [버리기 전용 키] 기존 OnInteract의 버리기 및 박스 배치 로직만 상속받음
    /// </summary>
    public void OnDropInput(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // 손에 든 아이템이 있어야만 버릴 수 있음
        if (context.started)
        {
            if (!_gate.CanDo(PlayerAction.DropItem)) return;

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

    /// <summary>
    /// 파쇄기 등 DraggableObject 끌기 시작 요청. 호출자가 소유한 PlayerInteraction 위에서 RPC를 받아
    /// 서버에서 직접 대상 오브젝트의 로직을 호출한다 (대상 오브젝트는 호출자 소유가 아니므로 이렇게 우회해야 함).
    /// </summary>
    [ServerRpc]
    private void RequestStartDragServerRpc(NetworkObjectReference draggableReference)
    {
        // 상호 배타 규칙 서버 재검증(치트 방어). 이미 무언가 끌고 있으면 게이트(RESTRAINED ⊃ DraggingObject)가 중복 시작도 막는다
        if (!_gate.CanDo(PlayerAction.DragObject)) return;

        if (!draggableReference.TryGet(out NetworkObject draggableNetworkObject)
            || !draggableNetworkObject.TryGetComponent<DraggableObject>(out var draggable))
        {
            return;
        }

        // 클라이언트가 지정한 대상을 믿지 않고, 서버에서 실제 상호작용 거리 안인지 확인
        if (!IsWithinDragRange(draggable)) return;

        draggable.StartDrag(NetworkObjectId);

        // 이동 둔화는 서버의 PlayerMovement가 판정하므로, 서버 쪽 인스턴스에도 끌고 있는 대상을 알려줘야 한다
        if (draggable.GrabberPlayerId == NetworkObjectId)
        {
            GetComponent<PlayerMovement>()?.SetDraggleObject(draggable);
        }
    }

    // 조준 지연으로 정상 요청이 튕기지 않도록 interactDistance에 여유를 둔다 (PickupItem.CanBePickedUpBy와 같은 방식)
    private const float DRAG_RANGE_TOLERANCE = 1f;

    private bool IsWithinDragRange(DraggableObject draggable)
    {
        Vector3 origin = playerCamera != null ? playerCamera.transform.position : transform.position;
        float maxDistance = interactDistance + DRAG_RANGE_TOLERANCE;

        // 큰 오브젝트라 중심점이 아니라 콜라이더 경계 기준으로 거리를 잰다 (bounds는 비볼록 MeshCollider에도 안전)
        Collider[] colliders = draggable.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            return Vector3.Distance(origin, draggable.transform.position) <= maxDistance;
        }

        foreach (Collider col in colliders)
        {
            if (Vector3.Distance(origin, col.bounds.ClosestPoint(origin)) <= maxDistance)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// DraggableObject 끌기 해제 요청. 위와 동일한 이유로 호출자 소유 오브젝트를 거쳐 처리한다.
    /// </summary>
    [ServerRpc]
    private void RequestStopDragServerRpc(NetworkObjectReference draggableReference)
    {
        if (draggableReference.TryGet(out NetworkObject draggableNetworkObject)
            && draggableNetworkObject.TryGetComponent<DraggableObject>(out var draggable)
            && draggable.GrabberPlayerId == NetworkObjectId) // 내가 끌고 있는 것만 해제 가능 (참고로 NetworkObjectId는 요청을 보낸 플레이어의 ID임)
        {
            draggable.StopDrag();
            GetComponent<PlayerMovement>()?.SetDraggleObject(null);
        }
    }

    [ServerRpc]
    public void TryPickupServerRpc(NetworkObjectReference pickupReference)
    {
        // 상호 배타 규칙 서버 재검증(치트 방어). 마피아 쓰레기 생성은 별도 규칙이라 여기를 타지 않는다
        if (!_gate.CanDo(PlayerAction.Pickup)) return;

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
        // 줍기와 같은 탐색 함수를 사용해서 "하이라이트가 뜨면 반드시 주울 수 있음"을 보장
        PickupItem newHoveredItem = GetTargetPickupItem();
        DraggableObject newHoveredDraggable = null;

        if (playerCamera != null && Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, interactDistance))
        {
            hit.collider.TryGetComponent<DraggableObject>(out newHoveredDraggable);
        }

        // 2. DraggableObject 하이라이트 갱신 로직
        if (newHoveredDraggable != hoveredDraggable)
        {
            // 이전 Draggable 하이라이트 끄기
            if (hoveredDraggable != null)
            {
                hoveredDraggable.SetHighlighted(false);
            }

            hoveredDraggable = newHoveredDraggable;

            // 새로운 Draggable 하이라이트 켜기 (끌기 시작 가능한 조건일 때만)
            if (hoveredDraggable != null && !hoveredDraggable.IsBeingDragged)
            {
                if (_gate.CanDo(PlayerAction.DragObject))
                {
                    hoveredDraggable.SetHighlighted(true);
                }
            }
        }

        if (newHoveredItem == hoveredItem)
        {
            return;
        }

        SetHoveredHighlight(false);

        hoveredItem = newHoveredItem;

        if (hoveredItem != null)
        {
            // 1. 줍기 규칙(붙잡힘, 손 점유 등)은 게이트가 판정
            if (!_gate.CanDo(PlayerAction.Pickup)) return;

            // 2. 페인트 총(4번)을 들고 있을 때는 백팩(1~3번)에 자리가 있어야 함
            if (_inventory != null
                && _inventory.CurrentSlot == PlayerActionGate.PAINT_GUN_SLOT
                && _inventory.IsBackpackFull())
            {
                return;
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
        if (!_gate.CanDo(PlayerAction.PlaceBox)) return;

        PickupItem currentHeldItem = GetCurrentHeldItem();
        if (!IsHoldingItem() || currentHeldItem == null) return;

        // 손이 닿지 않는 먼 거리에 배치 요청하는 것 방지
        // 클라이언트 프리뷰와 같은 기준(카메라 → 배치 위치 3D 거리)으로 검사해서 다른 층 구역에 놓는 것도 막는다 (네트워크 지연만큼 여유)
        Vector3 origin = playerCamera != null ? playerCamera.transform.position : transform.position;
        if (Vector3.Distance(origin, requestedPosition) > _placementPreview.MaxPlacementDistance + 0.5f)
        {
            DropHeldItemStandard();
            return;
        }

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
        // 끄는 입력은 항상 통과시켜야 상태가 켜진 채로 남지 않는다
        if (context.performed && !_gate.CanDo(PlayerAction.UseTool)) return;
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
        if (context.performed && !_gate.CanDo(PlayerAction.UseTool)) return;
        if (!IsHoldingItem()) return;
        var heldItem = GetCurrentHeldItem();
        if (heldItem == null || !heldItem.TryGetComponent<IZoomTool>(out var zoomToolTool)) return;
        if (context.performed) zoomToolTool.Aim(true);
        else if (context.canceled) zoomToolTool.Aim(false);
    }

    public void OnCapture(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (!context.performed) return;
        if (!_gate.CanDo(PlayerAction.UseTool)) return;
        var heldItem = GetCurrentHeldItem();
        if (heldItem == null || !heldItem.TryGetComponent<PolaroidCamera>(out var cameraTool)) return;

        cameraTool.Capture();
    }
    // 두꺼비집 동작 콜백
    public void OnToggleLight(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (_lightInteraction == null) return;

        if (context.started)
        {
            if (!_gate.CanDo(PlayerAction.ToggleLight)) return;
            if (playerCamera == null) return;

            // 앞에 스위치가 있는지 레이캐스트 검사만 수행
            if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, interactDistance, interactLayerMask))
            {
                if (hit.collider.TryGetComponent<LightSwitch>(out LightSwitch lightSwitch))
                {
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
        if (!_gate.CanDo(PlayerAction.DropItem)) return;

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

    #region 아이템 사용 통합 처리 (IUsableItem 인터페이스) -> 흡입기, 테이저건
    /// <summary>
    /// 마우스 좌클릭 통합 입력 콜백 (Input Action: UseItem 에 바인딩)
    /// </summary>
    public void OnUseItem(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // 1. 행동 가능 여부(게이트) 검사
        if (context.performed && !_gate.CanDo(PlayerAction.UseTool)) return;

        // 2. PlayerInventory를 통해 현재 장착한 아이템 가져오기
        PickupItem currentItem = _inventory.GetCurrentEquippedItem();
        if (currentItem == null) return;

        // 3. IUsableItem 인터페이스가 존재하면 OnUse 호출! (테이저건, 흡입기, 카메라 등 일률 적용)
        if (currentItem.TryGetComponent<IUsableItem>(out var usableItem))
        {
            usableItem.OnUse(context, playerCamera);
        }
    }
    #endregion
}