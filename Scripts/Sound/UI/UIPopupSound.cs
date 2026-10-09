using UnityEngine;

/// <summary>
/// 팝업/패널 루트에 붙이면 켜질 때 열림 소리, 꺼질 때 닫힘 소리를 낸다 (SetActive로 여닫는 팝업용).
/// SetActive 없이 애니메이션(CanvasGroup 등)으로 여닫는 팝업은 감지되지 않으므로
/// 여는 코드에서 UISoundProfile.Play(UISoundType.PopupOpen)을 직접 호출할 것.
/// </summary>
public class UIPopupSound : MonoBehaviour
{
    [SerializeField] private UISoundType openSound = UISoundType.PopupOpen;
    [SerializeField] private UISoundType closeSound = UISoundType.PopupClose;

    // 씬이 막 로드될 때 초기화로 켜고 끄는 것(Awake/Start에서 패널 숨기기 등)은 소리를 내지 않도록 무시하는 시간
    private const float SCENE_START_IGNORE_TIME = 0.1f;

    private void OnEnable()
    {
        if (IsSceneStarting) return;

        UISoundProfile.Play(openSound);
    }

    private void OnDisable()
    {
        // 초기화나 게임 종료/씬 전환으로 꺼지는 건 팝업을 닫은 게 아니므로 무시
        if (IsSceneStarting) return;
        if (SingletonShutdown.IsQuitting) return;
        if (!gameObject.scene.isLoaded) return;

        UISoundProfile.Play(closeSound);
    }

    private static bool IsSceneStarting => Time.timeSinceLevelLoad < SCENE_START_IGNORE_TIME;
}
