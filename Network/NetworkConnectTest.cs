using Steamworks;
using Netcode.Transports.Facepunch;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkConnectTest : MonoBehaviour
{
    public static NetworkConnectTest Instance { get; private set; }
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private FacepunchTransport transport;
    [SerializeField] private TMP_InputField roomCodeInputField;
    [SerializeField] private TMP_Text roomCodeDisplayText;

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
            roomCodeDisplayText.text = SteamClient.SteamId.Value.ToString();
            HideButtons();
            Debug.Log("Host started. Room code: " + SteamClient.SteamId.Value);
        });

        clientButton.onClick.AddListener(() =>
        {
            if (!ulong.TryParse(roomCodeInputField.text, out ulong hostSteamId))
            {
                Debug.LogError("잘못된 방 코드입니다.");
                return;
            }

            transport.targetSteamId = hostSteamId;
            NetworkManager.Singleton.StartClient();
            HideButtons();
            Debug.Log("Client started. Target: " + hostSteamId);
        });
    }

    private void HideButtons()
    {
        hostButton.gameObject.SetActive(false);
        clientButton.gameObject.SetActive(false);
        roomCodeInputField.gameObject.SetActive(false);
    }
}