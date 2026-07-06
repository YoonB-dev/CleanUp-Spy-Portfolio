using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkConnect : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    private void Awake()
    {
        // Host 버튼을 누르면 실행될 로직
        hostButton.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartHost();
            HideButtons();
            Debug.Log("Host started");
        });

        // Client 버튼을 누르면 실행될 로직
        clientButton.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartClient();
            HideButtons();
            Debug.Log("Client started");
        });
    }

    // 접속 성공 후 화면을 깔끔하게 하기 위해 UI를 숨기는 함수
    private void HideButtons()
    {
        hostButton.gameObject.SetActive(false);
        clientButton.gameObject.SetActive(false);
    }
}
