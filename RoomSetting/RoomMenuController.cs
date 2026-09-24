using UnityEngine;

public class RoomMenuController : SceneSingleton<RoomMenuController>
{
    [SerializeField] private GameObject roomSettingsPanel; // 방 설정 UI 전체가 담긴 부모 오브젝트
    [SerializeField] private GameObject roomBasicPanel; // 기본 UI, 설정 UI가 열리면 닫힘

    private bool isMenuOpen = false;

    private void Start()
    {
        roomSettingsPanel.SetActive(false);
        roomBasicPanel.SetActive(true);
    }

    public void SetMenuOpen()
    {
        isMenuOpen = !isMenuOpen;
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