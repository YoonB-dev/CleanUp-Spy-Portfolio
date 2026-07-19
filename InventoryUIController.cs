using UnityEngine;
using UnityEngine.UI;

public class InventoryUIController : SceneSingleton<InventoryUIController>
{
    [Header("UI 슬롯 이미지 컴포넌트 (1~3번)")]
    [SerializeField] private Image slot1Image;
    [SerializeField] private Image slot2Image;
    [SerializeField] private Image slot3Image;
    [SerializeField] private Image slot4Image; // 4번 슬롯은 마피아 페인트 총으로 고정임 (마피아일 경우)

    [Header("상태별 슬롯 색상")]
    [SerializeField] private Color selectedColor = Color.yellow;   // 현재 선택됨
    [SerializeField] private Color hasItemColor = Color.green;     // 아이템 보유 중 (미선택)
    [SerializeField] private Color emptyColor = Color.white;       // 빈 슬롯

    private PlayerInventory _targetInventory;

    public void BindInventory(PlayerInventory inventory)
    {
        _targetInventory = inventory;
        Debug.Log($"[UI] 인벤토리 바인딩 성공! NetObjectID: {inventory.NetworkObjectId}");
    }

    public void Start()
    {
        if (PlayerInventory.LocalInstance != null)
        {
            BindInventory(PlayerInventory.LocalInstance);
            UpdateUI();
        }
    }

    public void Slot4SetActive(bool isActive)
    {
        if (slot4Image != null)
        {
            slot4Image.gameObject.SetActive(isActive);
        }
    }

    public void UpdateUI()
    {
        if (_targetInventory == null) return;

        // 각 슬롯에 아이템이 채워져 있는지 유효성 검사 (TryGet 활용)
        bool hasItem1 = _targetInventory.Slot1.Value.TryGet(out _);
        bool hasItem2 = _targetInventory.Slot2.Value.TryGet(out _);
        bool hasItem3 = _targetInventory.Slot3.Value.TryGet(out _);

        int currentActive = _targetInventory.CurrentSlot;

        // 1번 슬롯 색상 결정
        slot1Image.color = (currentActive == 1) ? selectedColor : (hasItem1 ? hasItemColor : emptyColor);

        // 2번 슬롯 색상 결정
        slot2Image.color = (currentActive == 2) ? selectedColor : (hasItem2 ? hasItemColor : emptyColor);

        // 3번 슬롯 색상 결정
        slot3Image.color = (currentActive == 3) ? selectedColor : (hasItem3 ? hasItemColor : emptyColor);

        // 4번 슬롯은
        if(slot4Image != null && slot4Image.gameObject.activeSelf)
        {
            slot4Image.color = (currentActive == 4) ? selectedColor : hasItemColor; // 마피아 총은 항상 아이템이 있으므로 hasItemColor로 처리
        }
    }
}