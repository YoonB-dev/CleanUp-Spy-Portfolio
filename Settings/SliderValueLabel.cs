using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 슬라이더 옆에 지금 값을 보여준다. 슬라이더를 직접 보므로 설정 창이 따로 챙길 필요가 없다.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class SliderValueLabel : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private float multiplier = 1f;   // 0~1 값을 백분율로 보여줄 때 쓴다
    [SerializeField] private string format = "0.00";
    [SerializeField] private string suffix = "";

    private TMP_Text _text;

    private void Awake() => slider.onValueChanged.AddListener(Show);

    private void OnEnable() => Show(slider.value);

    private void Show(float value)
    {
        _text ??= GetComponent<TMP_Text>();
        _text.text = (value * multiplier).ToString(format) + suffix;
    }
}
