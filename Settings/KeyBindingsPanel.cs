using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

/// <summary>
/// 액션 목록을 읽어 리바인딩 행을 만든다. 설정 창의 조작 탭에 붙인다.
/// </summary>
public class KeyBindingsPanel : MonoBehaviour
{
    [System.Serializable]
    private struct ActionLabel
    {
        public string action;
        public string key;
    }

    [SerializeField] private RebindEntryUI entryPrefab;
    [SerializeField] private Transform content;
    [SerializeField] private Button resetButton;
    [SerializeField] private string mapName = "Player";
    [SerializeField] private string[] excludedActions = { "Look" };   // 마우스 이동은 다시 지정할 것이 없다
    [SerializeField] private ActionLabel[] labels;

    private readonly List<RebindEntryUI> _entries = new();

    private void Awake()
    {
        if (resetButton != null) resetButton.onClick.AddListener(ResetAll);
    }

    private void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        Build();
    }

    private void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

    // 행을 만들 때 문구를 박아 넣으므로 언어가 바뀌면 다시 찍어야 한다
    private void OnLocaleChanged(Locale locale) => Build();

    public void Build()
    {
        foreach (RebindEntryUI entry in _entries)
        {
            if (entry != null) Destroy(entry.gameObject);
        }

        _entries.Clear();

        InputActionAsset asset = SettingsManager.Instance != null ? SettingsManager.Instance.InputActions : null;
        InputActionMap map = asset != null ? asset.FindActionMap(mapName) : null;

        if (map == null)
        {
            Debug.LogWarning($"[KeyBindingsPanel] 액션 맵 {mapName}을 찾지 못했습니다");
            return;
        }

        foreach (InputAction action in map.actions)
        {
            if (System.Array.IndexOf(excludedActions, action.name) >= 0) continue;

            for (int i = 0; i < action.bindings.Count; i++)
            {
                // 합성 바인딩의 머리는 실제 키가 아니라 묶음 이름이다
                if (action.bindings[i].isComposite) continue;

                RebindEntryUI entry = Instantiate(entryPrefab, content);
                entry.Setup(action, i, LabelOf(action, i));
                _entries.Add(entry);
            }
        }
    }

    public void ResetAll()
    {
        SettingsManager.Instance.ResetKeyBindings();
        Build();
    }

    private string LabelOf(InputAction action, int bindingIndex)
    {
        string name = action.name;
        foreach (ActionLabel item in labels)
        {
            if (item.action != action.name) continue;

            name = SettingsText.Translate(item.key);
            break;
        }

        // WASD처럼 묶여 있는 것은 방향까지 보여줘야 구분된다
        string part = action.bindings[bindingIndex].name;
        return string.IsNullOrEmpty(part) ? name : $"{name} ({SettingsText.Translate($"part_{part}")})";
    }
}
