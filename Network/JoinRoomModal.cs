using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 합류 버튼을 누르면 뜨는 방 코드 입력 창. 실제 접속은 NetworkConnect가 한다.
/// </summary>
public class JoinRoomModal : MonoBehaviour
{
    [SerializeField] private GameObject root;         // 딤 배경까지 포함한 묶음
    [SerializeField] private Button openButton;       // 메인 화면의 합류 버튼
    [SerializeField] private CodeInputField codeInput;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button dimButton;        // 바깥을 눌러도 닫힌다
    [SerializeField] private TMP_Text errorText;
    [SerializeField] private string shortMessageKey = "join_too_short";
    [SerializeField] private string invalidMessageKey = "join_invalid";

    public bool IsOpen => root.activeSelf;

    private void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (dimButton != null) dimButton.onClick.AddListener(Close);

        confirmButton.onClick.AddListener(Confirm);
        cancelButton.onClick.AddListener(Close);
        codeInput.Submitted += Confirm;

        root.SetActive(false);
    }

    public void Open()
    {
        root.SetActive(true);

        codeInput.Clear();
        ShowError(string.Empty);
        codeInput.Focus();
    }

    public void Close()
    {
        codeInput.Release();
        root.SetActive(false);
    }

    public void Confirm()
    {
        if (!codeInput.IsComplete)
        {
            ShowError(SettingsText.Translate(shortMessageKey));
            codeInput.Focus();
            return;
        }

        // 형식이 틀린 코드는 창을 닫지 않고 그 자리에서 알려 준다
        if (NetworkConnect.Instance == null || !NetworkConnect.Instance.TryJoin(codeInput.Value))
        {
            ShowError(SettingsText.Translate(invalidMessageKey));
            codeInput.Focus();
            return;
        }

        Close();
    }

    // 엔터는 CodeInputField가 Submitted로 넘겨 준다
    private void Update()
    {
        if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
    }

    private void ShowError(string message)
    {
        errorText.text = message;
        errorText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }
}
