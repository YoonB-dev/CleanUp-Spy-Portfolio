using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인벤토리 슬롯 UI.
/// - 현재 들고 있는 슬롯만 색으로 강조
/// - 아이템 보유 여부는 각 슬롯의 ItemImage(아이콘) 활성화로 표시
/// </summary>
public class InventoryUIController : SceneSingleton<InventoryUIController>
{
    [Header("UI 슬롯 배경 이미지 (1~4번)")]
    [SerializeField] private Image slot1Image;
    [SerializeField] private Image slot2Image;
    [SerializeField] private Image slot3Image;
    [SerializeField] private Image slot4Image; // 4번 슬롯은 마피아 페인트 총으로 고정임 (마피아일 경우)

    [Header("슬롯 아이템 아이콘 (비워두면 각 슬롯의 자식 'ItemImage'를 자동으로 찾음)")]
    [SerializeField] private Image[] slotItemImages = new Image[4];

    [Header("슬롯 색상")]
    [SerializeField] private Color selectedColor = Color.yellow;   // 현재 들고 있는 슬롯
    [SerializeField] private Color normalColor = Color.white;      // 나머지 슬롯

    private const string ItemImageChildName = "ItemImage";

    private PlayerInventory _targetInventory;
    private Image[] _slotBackgrounds;

    protected override void Awake()
    {
        base.Awake();
        CacheSlotImages();
    }

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
        if (_slotBackgrounds == null) CacheSlotImages();

        int currentActive = _targetInventory.CurrentSlot;

        // 1~3번: 선택 색상 + 아이템 아이콘
        SetSlotItem(1, _targetInventory.Slot1.Value);
        SetSlotItem(2, _targetInventory.Slot2.Value);
        SetSlotItem(3, _targetInventory.Slot3.Value);

        for (int slot = 1; slot <= _slotBackgrounds.Length; slot++)
        {
            Image background = _slotBackgrounds[slot - 1];
            if (background == null) continue;
            background.color = (currentActive == slot) ? selectedColor : normalColor;
        }
        // 4번(마피아 페인트 총)은 항상 아이템이 있으므로 아이콘은 프리팹에 설정된 그대로 둠
    }

    /// <summary>
    /// 슬롯에 들어있는 아이템의 아이콘을 표시하고, 비어있으면 아이콘을 끕니다.
    /// </summary>
    private void SetSlotItem(int slot, NetworkObjectReference slotRef)
    {
        Image itemImage = slotItemImages[slot - 1];
        if (itemImage == null) return;

        ItemData data = null;
        bool hasItem = slotRef.TryGet(out NetworkObject netObj);
        if (hasItem && netObj.TryGetComponent<PickupItem>(out var item))
        {
            data = item.ItemData;
        }

        // 아이콘이 아직 없는 아이템은 프리팹에 설정된 기본 이미지를 그대로 보여줌
        if (data != null && data.icon != null)
        {
            itemImage.sprite = data.icon;
        }
        itemImage.gameObject.SetActive(hasItem);
    }

    /// <summary>
    /// 슬롯 배경/아이콘 이미지 배열을 구성합니다. 아이콘은 인스펙터에서 비워두면 자식 'ItemImage'를 찾아 사용합니다.
    /// </summary>
    private void CacheSlotImages()
    {
        _slotBackgrounds = new[] { slot1Image, slot2Image, slot3Image, slot4Image };

        if (slotItemImages == null || slotItemImages.Length != _slotBackgrounds.Length)
        {
            System.Array.Resize(ref slotItemImages, _slotBackgrounds.Length);
        }

        for (int i = 0; i < _slotBackgrounds.Length; i++)
        {
            if (slotItemImages[i] != null || _slotBackgrounds[i] == null) continue;

            Transform child = _slotBackgrounds[i].transform.Find(ItemImageChildName);
            if (child != null) slotItemImages[i] = child.GetComponent<Image>();
        }
    }
}
