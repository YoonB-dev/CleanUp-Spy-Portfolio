using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomPlayerEntryUI : MonoBehaviour
{
    [SerializeField] private Image playerPictureImage;
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private GameObject playerReadyIndicator;
    [SerializeField] private GameObject hostIndicator;
    [SerializeField] private string emptyLabel = "빈 자리";
    [SerializeField] private Color emptyColor = new(0.118f, 0.118f, 0.102f, 0.3f);

    public void SetPlayerInfo(string playerName, Sprite playerPicture, bool isReady, bool isHost)
    {
        playerNameText.text = playerName;
        playerPictureImage.sprite = playerPicture;
        playerReadyIndicator.SetActive(isReady);
        hostIndicator.SetActive(isHost);
    }

    /// <summary>아직 아무도 안 들어온 자리</summary>
    public void SetEmpty()
    {
        playerNameText.text = emptyLabel;
        playerNameText.color = emptyColor;
        playerPictureImage.color = emptyColor;
        playerReadyIndicator.SetActive(false);
        hostIndicator.SetActive(false);
    }

    public void SetReadyStatus(bool isReady)
    {
        playerReadyIndicator.SetActive(isReady);
    }
}
