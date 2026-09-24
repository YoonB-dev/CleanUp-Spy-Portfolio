using UnityEngine;
using UnityEngine.UI;

public class RoomMenuController : SceneSingleton<RoomMenuController>
{
    [SerializeField] private GameObject roomSettingsPanel; // 방 설정 UI 전체가 담긴 부모 오브젝트
    [SerializeField] private GameObject roomBasicPanel; // 기본 UI, 설정 UI가 열리면 닫힘
    [SerializeField] private Button closeButton; // 마우스로 설정 UI를 닫는 버튼

    private bool isMenuOpen = false;

    private void Start()
    {
        roomSettingsPanel.SetActive(false);
        roomBasicPanel.SetActive(true);

        if (closeButton != null) closeButton.onClick.AddListener(CloseMenu);
    }

    // H 키로 열고 닫는다
    public void SetMenuOpen() => SetOpen(!isMenuOpen);

    // 닫기 버튼에서 호출. 이미 닫혀 있으면 아무것도 하지 않는다
    public void CloseMenu()
    {
        Debug.Log("asdasd");
        if (!isMenuOpen) return;
        SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        isMenuOpen = open;
        roomSettingsPanel.SetActive(isMenuOpen);
        roomBasicPanel.SetActive(!isMenuOpen);

        Cursor.lockState = isMenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isMenuOpen;

        // 창이 열려 있는 동안 이동/시점/클릭 등 플레이어 행동을 막는다 (H 키, 채팅, 일시정지는 그대로)
        var player = LocalPlayerInput.Local;
        if (player != null && player.TryGetComponent(out PlayerActionGate gate))
        {
            gate.SetUIOpen(isMenuOpen);
        }
    }
}
