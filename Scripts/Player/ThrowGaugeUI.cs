using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class ThrowGaugeUI : MonoBehaviour
{
    [SerializeField] private Image gaugeFillImage; // 반원 Filled 이미지
    [SerializeField] private GameObject gaugeRootObject; // 평소엔 꺼둘 게이지 부모 오브젝트

    private PlayerInteraction _localPlayer;

    private void Start()
    {
        gaugeRootObject.SetActive(false); // 처음엔 꺼둠
        _localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject?.GetComponent<PlayerInteraction>();
    }

    private void Update()
    {
        if (_localPlayer == null)
        {
            // 내 로컬 캐릭터가 스폰되면 참조 가져오기
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localObj != null) _localPlayer = localObj.GetComponent<PlayerInteraction>();
            }

            if (gaugeRootObject != null) gaugeRootObject.SetActive(false);
            return;
        }

        float gauge = _localPlayer.CurrentThrowGauge;

        if (gauge > 0f)
        {
            if (gaugeRootObject != null && !gaugeRootObject.activeSelf) gaugeRootObject.SetActive(true);
            if (gaugeFillImage != null) gaugeFillImage.fillAmount = gauge;
        }
        else
        {
            if (gaugeRootObject != null && gaugeRootObject.activeSelf) gaugeRootObject.SetActive(false);
        }
    }
}