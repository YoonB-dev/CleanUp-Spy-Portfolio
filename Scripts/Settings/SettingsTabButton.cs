using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정 창 탭 하나. 선택되면 본문과 이어 붙은 것처럼 색이 바뀌고 살짝 내려앉는다.
/// </summary>
public class SettingsTabButton : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private Image border;
    [SerializeField] private TMP_Text label;

    [SerializeField] private Color selectedBackground = new(0.847f, 0.525f, 0.184f);
    [SerializeField] private Color selectedBorder = new(0.173f, 0.165f, 0.118f);
    [SerializeField] private Color selectedText = Color.white;

    [SerializeField] private Color normalBackground = new(0.804f, 0.765f, 0.627f);
    [SerializeField] private Color normalBorder = new(0.710f, 0.659f, 0.475f);
    [SerializeField] private Color normalText = new(0.420f, 0.373f, 0.247f);

    [SerializeField] private float selectedDrop = 2f;   // 본문 위로 겹쳐 내려앉는 만큼

    private RectTransform _rect;
    private float _baseY;

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _baseY = _rect.anchoredPosition.y;
    }

    public void SetSelected(bool selected)
    {
        if (_rect == null) Awake();

        background.color = selected ? selectedBackground : normalBackground;
        border.color = selected ? selectedBorder : normalBorder;
        label.color = selected ? selectedText : normalText;
        label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;

        Vector2 position = _rect.anchoredPosition;
        position.y = selected ? _baseY - selectedDrop : _baseY;
        _rect.anchoredPosition = position;
    }
}
