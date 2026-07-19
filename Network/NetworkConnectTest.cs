using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkConnectTest : MonoBehaviour
{
    public static NetworkConnectTest Instance { get; private set; }
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    public void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        hostButton.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartHost();
            HideButtons();
            Debug.Log("Host started");
        });

        clientButton.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartClient();
            HideButtons();
            Debug.Log("Client started");
        });
    }

    private void HideButtons()
    {
        hostButton.gameObject.SetActive(false);
        clientButton.gameObject.SetActive(false);
    }
}
