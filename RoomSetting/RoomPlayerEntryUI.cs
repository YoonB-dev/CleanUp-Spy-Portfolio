using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomPlayerEntryUI : MonoBehaviour
{
    [SerializeField] private Image playerPictureImage;
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private GameObject playerReadyIndicator;
    [SerializeField] private GameObject hostIndicator;

    public void SetPlayerInfo(string playerName, Sprite playerPicture, bool isReady, bool isHost)
    {
        playerNameText.text = playerName;
        playerPictureImage.sprite = playerPicture;
        playerReadyIndicator.SetActive(isReady);
        hostIndicator.SetActive(isHost);
    }

    public void SetReadyStatus(bool isReady)
    {
        playerReadyIndicator.SetActive(isReady);
    }
}
