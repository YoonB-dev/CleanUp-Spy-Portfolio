using TMPro;
using UnityEngine;

/// <summary>
/// 설정 창 오른쪽에서 지금 가리키는 항목이 뭘 하는지 보여준다.
/// </summary>
public class SettingsDescriptionView : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;

    private void Start() => Clear();

    public void Show(string title, string body)
    {
        titleText.text = title;
        bodyText.text = body;
    }

    public void Clear()
    {
        titleText.text = string.Empty;
        bodyText.text = SettingsText.Translate("desc_hint");
    }
}
