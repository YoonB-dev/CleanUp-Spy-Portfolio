using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 슬라이더를 움직일 때 눈금마다 짧은 틱 소리를 낸다 (천천히 끌면 "틱.. 틱..", 빠르게 끌면 "드드득").
/// 사용자가 직접 조작할 때(드래그 중이거나 키보드/패드로 선택된 상태)만 소리를 내고,
/// 코드로 값을 넣을 때(설정 창 Refresh 등)는 조용하다.
/// UIButtonSoundBinder가 캔버스 아래 슬라이더에 자동으로 붙여준다.
/// </summary>
[RequireComponent(typeof(Slider))]
public class UISliderSound : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private UISoundType soundType = UISoundType.SliderTick;

    [Tooltip("전체 범위 대비 이만큼 움직일 때마다 틱 (0.05 = 5%). Whole Numbers 슬라이더는 값이 1 바뀔 때마다 틱")]
    [Range(0.01f, 0.5f)]
    [SerializeField] private float tickStep = 0.05f;

    [Tooltip("아무리 빠르게 끌어도 이 간격(초)보다 자주 재생하지 않음")]
    [SerializeField] private float minInterval = 0.04f;

    [Tooltip("값이 최소일 때 / 최대일 때의 피치 배율 (값이 클수록 음이 높아짐). 둘 다 1이면 피치 변화 없음")]
    [SerializeField] private Vector2 pitchRange = new Vector2(0.9f, 1.2f);

    private Slider _slider;
    private bool _isPointerDown;
    private float _lastTickValue;
    private float _lastTickTime = float.NegativeInfinity;

    private void Awake()
    {
        _slider = GetComponent<Slider>();
        _slider.onValueChanged.AddListener(OnValueChanged);
    }

    private void OnEnable()
    {
        _isPointerDown = false;
        _lastTickValue = _slider.value;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPointerDown = true;
        _lastTickValue = _slider.value;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPointerDown = false;
    }

    private void OnValueChanged(float value)
    {
        // 코드로 값을 바꾼 경우엔 기준값만 갱신하고 소리는 내지 않음
        if (!IsUserInteracting)
        {
            _lastTickValue = value;
            return;
        }

        if (!HasMovedOneTick(value)) return;
        if (Time.unscaledTime - _lastTickTime < minInterval) return;

        _lastTickValue = value;
        _lastTickTime = Time.unscaledTime;
        UISoundProfile.Play(soundType, Mathf.Lerp(pitchRange.x, pitchRange.y, _slider.normalizedValue));
    }

    private bool IsUserInteracting =>
        _isPointerDown
        || (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject);

    private bool HasMovedOneTick(float value)
    {
        if (_slider.wholeNumbers) return !Mathf.Approximately(value, _lastTickValue);

        float range = _slider.maxValue - _slider.minValue;
        if (range <= 0f) return false;

        return Mathf.Abs(value - _lastTickValue) / range >= tickStep;
    }
}
