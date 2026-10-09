using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 키 하나를 보여주고 눌러서 다시 지정한다. KeyBindingsPanel이 찍어 낸다.
/// </summary>
public class RebindEntryUI : MonoBehaviour
{
    [SerializeField] private TMP_Text actionLabel;
    [SerializeField] private TMP_Text keyLabel;
    [SerializeField] private Button rebindButton;
    [SerializeField] private string waitingText = "입력 대기...";
    [SerializeField] private string emptyText = "없음";

    private static int _endedFrame = -1;

    public static bool IsRebinding { get; private set; }

    /// <summary>
    /// 리바인딩이 ESC를 쓰는 동안에는 일시정지 메뉴가 열리면 안 된다.
    /// 끝난 프레임까지 막아야 취소한 ESC가 메뉴로 흘러가지 않는다.
    /// </summary>
    public static bool ConsumesEscape => IsRebinding || Time.frameCount == _endedFrame;

    private string EffectivePath => _action.bindings[_bindingIndex].effectivePath;

    private InputAction _action;
    private int _bindingIndex;
    private bool _wasEnabled;
    private InputActionRebindingExtensions.RebindingOperation _operation;

    public void Setup(InputAction action, int bindingIndex, string label)
    {
        _action = action;
        _bindingIndex = bindingIndex;

        actionLabel.text = label;
        rebindButton.onClick.AddListener(StartRebind);
        Refresh();
    }

    public void Refresh()
    {
        string path = EffectivePath;

        keyLabel.text = string.IsNullOrEmpty(path)
            ? emptyText
            : InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
    }

    private void StartRebind()
    {
        if (IsRebinding) return;

        IsRebinding = true;
        keyLabel.text = waitingText;

        // 리바인딩 중에는 그 액션이 꺼져 있어야 한다. 원래 상태는 끝나고 되돌린다
        _wasEnabled = _action.enabled;
        _action.Disable();

        _operation = _action.PerformInteractiveRebinding(_bindingIndex)
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnComplete(_ => Finish())
            .OnCancel(_ => Finish())
            .Start();
    }

    private void Finish()
    {
        Dispose();

        if (_wasEnabled) _action.Enable();

        Refresh();
        SettingsManager.Instance.SaveKeyBindings();
    }

    // 설정 창을 닫아 행이 사라져도 대기 상태가 남지 않게 한다
    private void OnDestroy()
    {
        if (_operation == null) return;

        Dispose();
        if (_wasEnabled) _action.Enable();
    }

    private void Dispose()
    {
        _operation?.Dispose();
        _operation = null;

        IsRebinding = false;
        _endedFrame = Time.frameCount;
    }
}
