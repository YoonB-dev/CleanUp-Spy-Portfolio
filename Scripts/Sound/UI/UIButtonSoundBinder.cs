using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캔버스 루트에 붙이면 시작할 때 자기 아래의 모든 버튼에 Default 클릭 소리를, 슬라이더에 틱 소리(UISliderSound)를 연결한다.
/// UIButtonSound가 붙은 버튼(역할을 따로 지정한 버튼)과 UISliderSound가 이미 붙은 슬라이더는 건너뛴다.
/// 시작 이후에 생성되는 버튼/슬라이더는 찾지 못하므로, 그런 프리팹에는 UIButtonSound/UISliderSound를 직접 붙일 것.
/// </summary>
public class UIButtonSoundBinder : MonoBehaviour
{
    [Tooltip("캔버스 아래 슬라이더에도 틱 소리(UISliderSound)를 자동으로 붙일지")]
    [SerializeField] private bool bindSliders = true;

    private void Awake()
    {
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            // 역할을 따로 지정한 버튼은 UIButtonSound가 처리
            if (button.TryGetComponent<UIButtonSound>(out _)) continue;
            if (!IsClosestBinder(button)) continue;

            button.onClick.AddListener(PlayDefaultSound);
        }

        if (!bindSliders) return;

        foreach (Slider slider in GetComponentsInChildren<Slider>(true))
        {
            // 이미 붙어 있으면 그 설정(틱 간격 등)을 그대로 사용
            if (slider.TryGetComponent<UISliderSound>(out _)) continue;
            if (!IsClosestBinder(slider)) continue;

            slider.gameObject.AddComponent<UISliderSound>();
        }
    }

    // 캔버스 안에 Binder가 또 있으면 가장 가까운 Binder만 연결 (소리 두 번 나는 것 방지)
    private bool IsClosestBinder(Component target) => target.GetComponentInParent<UIButtonSoundBinder>(true) == this;

    private static void PlayDefaultSound()
    {
        UISoundProfile.Play(UISoundType.Default);
    }
}
