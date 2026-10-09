using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 칸이 나뉜 코드 입력칸. 실제 입력은 투명하게 깔아 둔 TMP_InputField가 받고
/// 글자만 칸으로 흩뿌린다. 덕분에 Ctrl+V와 커서 이동을 그대로 쓸 수 있다.
/// </summary>
public class CodeInputField : MonoBehaviour
{
    [SerializeField] private TMP_InputField input;
    [SerializeField] private TMP_Text[] cellTexts;

    public string Value => input.text;
    public bool IsComplete => input.text.Length >= cellTexts.Length;

    /// <summary>엔터를 쳤을 때. 다 채웠다고 바로 보내지는 않는다</summary>
    public event Action Submitted;

    private void Awake()
    {
        input.characterLimit = cellTexts.Length;
        input.onValidateInput = Validate;
        input.onValueChanged.AddListener(_ => Redraw());
        input.onSubmit.AddListener(_ => Submitted?.Invoke());

        HideInputGraphics();
        Redraw();
    }

    public void Clear()
    {
        input.text = string.Empty;
        Redraw();
    }

    public void Focus()
    {
        input.Select();
        input.ActivateInputField();
        input.caretPosition = input.text.Length;
    }

    public void Release() => input.DeactivateInputField();

    // 알파벳에 없는 글자는 아예 들어오지 못하게 막는다. 붙여넣기도 한 글자씩 이걸 거친다
    private char Validate(string text, int index, char addedChar) => InviteCode.Sanitize(addedChar);

    private void Redraw()
    {
        string value = input.text;

        for (int i = 0; i < cellTexts.Length; i++)
        {
            cellTexts[i].text = i < value.Length ? value[i].ToString() : string.Empty;
        }
    }

    // 진짜 입력칸은 보이면 안 된다. 칸에 그린 글자만 보이게 한다
    private void HideInputGraphics()
    {
        input.textComponent.color = Color.clear;
        input.placeholder.gameObject.SetActive(false);

        input.customCaretColor = true;
        input.caretColor = Color.clear;
        input.selectionColor = Color.clear;
    }
}
