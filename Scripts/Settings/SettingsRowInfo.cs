using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 설정 항목 한 줄. 가리키면 오른쪽 설명을 갈아 끼우고 줄을 강조한다.
/// </summary>
public class SettingsRowInfo : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private SettingsDescriptionView view;
    [SerializeField] private Image highlight;
    [SerializeField] private string titleKey;
    [SerializeField] private string descriptionKey;
    [SerializeField] private Color hoverColor = new(0.847f, 0.525f, 0.184f, 0.12f);

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (view != null) view.Show(SettingsText.Translate(titleKey), SettingsText.Translate(descriptionKey));
        if (highlight != null) highlight.color = hoverColor;
    }

    // 설명은 마지막 것을 남겨 둔다. 줄을 벗어날 때마다 비면 읽다 만다
    public void OnPointerExit(PointerEventData eventData)
    {
        if (highlight != null) highlight.color = Color.clear;
    }
}
