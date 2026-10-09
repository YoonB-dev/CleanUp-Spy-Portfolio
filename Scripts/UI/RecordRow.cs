using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;

public class RecordRow : MonoBehaviour
{
    [SerializeField] private LocalizeStringEvent label;
    [SerializeField] private TMP_Text value;

    public void Set(string labelKey, string valueText)
    {
        label.StringReference.TableEntryReference = labelKey;
        value.text = valueText;
    }
}
