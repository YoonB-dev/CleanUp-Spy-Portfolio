using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInventory : NetworkBehaviour
{
    [Header("Slots (Server Side Sync)")]
    public readonly NetworkVariable<NetworkObjectReference> Slot1 = new();
    public readonly NetworkVariable<NetworkObjectReference> Slot2 = new();
    public readonly NetworkVariable<NetworkObjectReference> Slot3 = new();

    private readonly NetworkVariable<int> _currentSlot = new(1);
    public NetworkVariable<int> CurrentSlotNetworkVariable => _currentSlot;
    public int CurrentSlot => _currentSlot.Value;
    public static PlayerInventory LocalInstance { get; private set; }
    private RoleManager _roleManager;
    [SerializeField] private RagdollPoser _ragdollPoser; // 인스펙터에서 연결
    private void Awake()
    {
        _roleManager = GetComponent<RoleManager>();
    }
    public override void OnNetworkSpawn()
    {
        // 모든 슬롯과 선택 변경 사항을 감지하여 비주얼/UI 업데이트
        _currentSlot.OnValueChanged += OnCurrentSlotChanged;
        Slot1.OnValueChanged += OnSlotDataChanged;
        Slot2.OnValueChanged += OnSlotDataChanged;
        Slot3.OnValueChanged += OnSlotDataChanged;
        if (IsLocalPlayer)
        {
            LocalInstance = this;
            TryBindUI();
        }

        RefreshInventoryVisuals();
    }

    private void Start()
    {
        TryBindUI();
    }
    private void TryBindUI()
    {   
        if (IsLocalPlayer && InventoryUIController.Instance != null)
        {
            InventoryUIController.Instance.BindInventory(this);
            // 바인딩 성공했으니 UI도 바로 한번 갈아주기.
            InventoryUIController.Instance.UpdateUI();
            if (_roleManager != null)
            {
                InventoryUIController.Instance.Slot4SetActive(_roleManager.CurrentRole == PlayerRole.Mafia);
            }
        }
        else
        {
            Debug.LogWarning("[PlayerInventory] UI 바인딩 실패: 로컬 플레이어가 아니거나 InventoryUIController 인스턴스를 찾을 수 없음.");
        }
    }

    public override void OnNetworkDespawn()
    {
        _currentSlot.OnValueChanged -= OnCurrentSlotChanged;
        Slot1.OnValueChanged -= OnSlotDataChanged;
        Slot2.OnValueChanged -= OnSlotDataChanged;
        Slot3.OnValueChanged -= OnSlotDataChanged;
    }

    #region Input System
    public void OnSlot1Input(InputAction.CallbackContext context) 
    {
        if (IsOwner && context.performed)
        {
            int target = (_currentSlot.Value == 1) ? 0 : 1;
            ExecuteSlotChange(target);
        }
    }
    public void OnSlot2Input(InputAction.CallbackContext context)
    {
        if (IsOwner && context.performed)
        {
            int target = (_currentSlot.Value == 2) ? 0 : 2;
            ExecuteSlotChange(target);
        }
    }
    public void OnSlot3Input(InputAction.CallbackContext context)
    {
        if (IsOwner && context.performed)
        {
            int target = (_currentSlot.Value == 3) ? 0 : 3;
            ExecuteSlotChange(target);
        }
    }
    #endregion

    /// <summary>
    /// 현재 손에 '무엇이든' 들고 있는지 체크하는 함수 (PlayerInteraction 등에서 상호작용 제한용으로 사용)
    /// </summary>
    public bool IsHoldingAnything()
    {
        // 1. 마피아가 4번 슬롯을 활성화하여 페인트 총을 든 경우
        if (_currentSlot.Value == 4 && _roleManager != null && _roleManager.CurrentRole == PlayerRole.Mafia)
        {
            return true;
        }

        // 2. 일반 1~3번 슬롯에 아이템이 실재하고 그걸 들고 있는 경우
        return GetCurrentEquippedItem() != null;
    }

    /// <summary>
    /// 로컬 입력과 RPC 요청 사이의 브릿지 역할을 하는 함수
    /// </summary>
    public void ExecuteSlotChange(int targetSlot)
    {
        // 호스트(서버 겸 클라이언트)라면 RPC를 거칠 필요 없이 서버 로직을 즉시 실행 가능합니다.
        if (IsServer)
        {
            ChangeSlotLocalLogical(targetSlot);
        }
        else
        {
            // 순수 클라이언트라면 서버에 소유권을 가진 채로 당당하게 RPC를 호출합니다.
            RequestChangeSlotServerRpc(targetSlot);
        }
    }

    [ServerRpc]
    private void RequestChangeSlotServerRpc(int newSlotIndex)
    {
        ChangeSlotLocalLogical(newSlotIndex);
    }
    /// <summary>
    /// 실제 서버에서 슬롯 상태를 변경하고 Visual을 갱신하는 공통 핵심 로직
    /// </summary>
    private void ChangeSlotLocalLogical(int newSlotIndex)
    {
        if (!IsServer) return; // 서버 측에서만 네트워크 변수를 수정할 수 있도록 방어

        _currentSlot.Value = newSlotIndex; // 이 부분이 반드시 들어가야 슬롯이 바뀝니다!
        RefreshInventoryVisuals();
    }

    public bool TryAddItem(PickupItem item)
    {
        if (!IsServer) return false;

        int assignedSlot = -1;

        if (_currentSlot.Value >= 1 && _currentSlot.Value <= 3 && IsSlotEmpty(_currentSlot.Value))
        {
            assignedSlot = _currentSlot.Value;
            SetSlotValue(_currentSlot.Value, item.NetworkObject);
        }
        else
        {
            for (int i = 1; i <= 3; i++)
            {
                if (IsSlotEmpty(i))
                {
                    assignedSlot = i;
                    SetSlotValue(i, item.NetworkObject);
                    break;
                }
            }
        }

        if (assignedSlot != -1)
        {
            // 방금 아이템이 들어간 슬롯으로 현재 슬롯을 전환시켜 손에 들게 만들기 -> 이거 싫으면 위에서 그냥 리턴하게 하면 됨.
            if (_currentSlot.Value == 0)
            {
                _currentSlot.Value = assignedSlot;
            }

            return true;
        }

        return false;
    }

    private bool IsSlotEmpty(int slotNum)
    {
        return slotNum switch
        {
            1 => !Slot1.Value.TryGet(out _),
            2 => !Slot2.Value.TryGet(out _),
            3 => !Slot3.Value.TryGet(out _),
            _ => true
        };
    }

    private void SetSlotValue(int slotNum, NetworkObject netObj)
    {
        NetworkObjectReference reference = netObj != null ? new NetworkObjectReference(netObj) : new NetworkObjectReference();
        switch (slotNum)
        {
            case 1: Slot1.Value = reference; break;
            case 2: Slot2.Value = reference; break;
            case 3: Slot3.Value = reference; break;
        }
    }

    private void OnCurrentSlotChanged(int previousValue, int newValue)
    {
        RefreshInventoryVisuals();
    }

    private void OnSlotDataChanged(NetworkObjectReference previousValue, NetworkObjectReference newValue)
    {
        // [버그 방지 핵심] 이전 아이템이 해제(Drop 등)되었다면, 강제로 시각화를 켜서 필드에서 보이게 만듭니다.
        if (previousValue.TryGet(out var prevNetObj) && prevNetObj.TryGetComponent<PickupItem>(out var prevItem))
        {
            prevItem.SetVisibility(true);
        }

        RefreshInventoryVisuals();
    }

    public void RefreshInventoryVisuals()
    {
        // 1. 월드 모델링 비주얼 업데이트 (현재 들고 있는 슬롯만 True, 나머지는 False)
        UpdateSlotVisual(1, Slot1.Value);
        UpdateSlotVisual(2, Slot2.Value);
        UpdateSlotVisual(3, Slot3.Value);

        UpdateRagdollCarryState();

        // 2. LocalPlayer인 경우 실시간 UI 업데이트 전파
        if (IsLocalPlayer && InventoryUIController.Instance != null)
        {
            InventoryUIController.Instance.UpdateUI();
        }
    }

    private void UpdateSlotVisual(int slotNum, NetworkObjectReference slotRef)
    {
        if (slotRef.TryGet(out var netObj) && netObj.TryGetComponent<PickupItem>(out var item))
        {
            bool shouldBeVisible = (slotNum == _currentSlot.Value);
            item.SetVisibility(shouldBeVisible);
        }
    }

    /// <summary>
    /// [서버전용] 특정 아이템이 인벤토리 슬롯에 존재한다면 비워줍니다. (Drop 연동용)
    /// </summary>
    public void ClearItemFromSlots(PickupItem item)
    {
        if (!IsServer || item == null) return;

        // 1번부터 3번 슬롯까지 검사하여 버리려는 아이템과 일치하는 슬롯을 찾습니다.
        if (Slot1.Value.TryGet(out var obj1) && obj1.gameObject == item.gameObject) { SetSlotValue(1, null); return; }
        if (Slot2.Value.TryGet(out var obj2) && obj2.gameObject == item.gameObject) { SetSlotValue(2, null); return; }
        if (Slot3.Value.TryGet(out var obj3) && obj3.gameObject == item.gameObject) { SetSlotValue(3, null); return; }
    }

    /// <summary>
    /// [서버 전용 일반 함수] 클라이언트 RPC를 거치지 않고 서버에서 직접 아이템을 드롭 처리합니다.
    /// </summary>
    public void DropCurrentItemDirectServer()
    {
        if (!IsServer) return;
        if (_currentSlot.Value == 4) return;

        // 현재 들고 있는 슬롯의 아이템을 가져옵니다.
        PickupItem currentItem = GetCurrentEquippedItem();

        if (currentItem != null)
        {
            // 아이템 자체의 Drop을 실행 (내부에서 ClearItemFromSlots 등 연쇄 반응)
            currentItem.Drop();
        }
    }

    /// <summary>
    /// 현재 손에 들고 있는(활성화된 슬롯의) PickupItem 컴포넌트를 반환합니다.
    /// </summary>
    public PickupItem GetCurrentEquippedItem()
    {
        NetworkObjectReference currentRef = _currentSlot.Value switch
        {
            1 => Slot1.Value,
            2 => Slot2.Value,
            3 => Slot3.Value,
            _ => new NetworkObjectReference()
        };

        if (currentRef.TryGet(out var netObj))
        {
            return netObj.GetComponent<PickupItem>();
        }
        return null;
    }
    public bool IsBackpackFull()
    {
        return !IsSlotEmpty(1) && !IsSlotEmpty(2) && !IsSlotEmpty(3);
    }

    #region 레그돌 포즈
    /// <summary>
    /// 현재 손에 든 아이템 유무 및 정보에 따라 RagdollPoser의 손 위치/그립을 갱신합니다.
    /// </summary>
    private void UpdateRagdollCarryState()
    {
        // 서버 권위로 래그돌 동작을 제어하므로 서버/호스트에서만 Pose 제어 호출
        if (!IsServer || _ragdollPoser == null) return;

        // 현재 선택된 슬롯의 아이템 가져오기
        PickupItem currentItem = GetCurrentEquippedItem();

        if (currentItem != null && _currentSlot.Value >= 1 && _currentSlot.Value <= 3)
        {
            // 아이템에 설정된 그립 포인트(CarryGripPoints)가 있다면 RagdollPoser에 전달
            if (currentItem.TryGetComponent<CarryGripPoints>(out var gripPoints))
            {
                _ragdollPoser.SetCarryTarget(gripPoints);
            }
            else
            {
                _ragdollPoser.SetCarryTarget(null); // 기본 그립 사용
            }

            // 손을 앞으로 뻗도록 요청
            _ragdollPoser.SetCarryRequested(true);
        }
        else
        {
            // 아무것도 들고 있지 않거나(슬롯 0) 마피아 전용 아이템 등일 때 손을 아래로 내림
            _ragdollPoser.SetCarryTarget(null);
            _ragdollPoser.SetCarryRequested(false);
        }
    }
    #endregion
}