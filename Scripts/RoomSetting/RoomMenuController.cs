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
    }
}