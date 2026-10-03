using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

/// <summary>
/// 설정 창. 사본을 편집하다가 적용을 눌러야 실제 설정이 되고 저장된다.
/// 메인 화면에서는 버튼으로, 로비와 인게임에서는 일시정지 메뉴를 거쳐 연다.
/// </summary>
public class SettingsUIController : SceneSingleton<SettingsUIController>
{
    [Header("공통")]
    [SerializeField] private GameObject root;
    [SerializeField] private Button[] tabButtons;
    [SerializeField] private SettingsTabButton[] tabStyles;
    [SerializeField] private GameObject[] tabPanels;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button dimButton;   // 패널 밖을 눌러도 닫힌다

    [Header("게임플레이")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private Toggle invertYToggle;
    [SerializeField] private Slider fovSlider;

    [Header("그래픽")]
    [SerializeField] private TMP_Dropdown screenModeDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private TMP_Dropdown frameRateDropdown;
    [SerializeField] private TMP_Dropdown antiAliasingDropdown;
    [SerializeField] private Toggle vSyncToggle;
    [SerializeField] private Slider shadowDistanceSlider;
    [SerializeField] private Slider brightnessSlider;

    [Header("사운드")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider voiceSlider;
    [SerializeField] private TMP_Dropdown voiceModeDropdown;
    [SerializeField] private Slider voiceActivationSlider;

    [Header("일반")]
    [SerializeField] private TMP_Dropdown languageDropdown;
    [SerializeField] private Slider uiScaleSlider;

    // 번역이 필요한 것만 키로 둔다. 숫자는 그대로 나온다
    [Header("드롭다운 문구 키")]
    [SerializeField] private string[] screenModeLabels = { "opt_fullscreen", "opt_borderless", "opt_windowed" };
    [SerializeField] private string[] frameRateLabels = { "opt_unlimited", "30", "60", "120", "144", "240" };
    [SerializeField] private string[] antiAliasingLabels = { "opt_off", "2x", "4x", "8x" };
    [SerializeField] private string[] voiceModeLabels = { "opt_push_to_talk", "opt_voice_activity" };

    private static readonly FullScreenMode[] ScreenModes =
    {
        FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed
    };
    private static readonly int[] FrameRates = { 0, 30, 60, 120, 144, 240 };
    private static readonly int[] AntiAliasingSamples = { 1, 2, 4, 8 };

    public bool IsOpen => root.activeSelf;

    private readonly List<string> _localeCodes = new();
    private bool _loading;   // 값을 채워 넣는 동안 콜백이 되돌아오지 않게 한다

    // 창에서 만지는 것은 사본이다. 적용을 눌러야 실제 설정이 된다
    private GameSettings _draft;
    private VideoSettings _videoDraft;
    private GameSettings Settings => _draft ??= SettingsManager.Instance.Settings.Clone();
    private VideoSettings Video => _videoDraft ??= SettingsManager.Instance.Video.Clone();

    protected override void Awake()
    {
        base.Awake();

        BuildDropdowns();
        HookEvents();

        root.SetActive(false);
        SelectTab(0);
    }

    private void Start()
    {
        StartCoroutine(BuildLanguageDropdownRoutine());
    }

    private void OnEnable() => LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;

    private void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

    // 드롭다운 항목 문구는 직접 만든 것이라 언어가 바뀌면 다시 채워야 한다
    private void OnLocaleChanged(Locale locale)
    {
        BuildDropdowns();
        if (IsOpen) Refresh();
    }

    public void Open()
    {
        _draft = SettingsManager.Instance.Settings.Clone();
        _videoDraft = SettingsManager.Instance.Video.Clone();

        root.SetActive(true);
        Refresh();
    }

    /// <summary>적용하지 않은 편집은 버린다</summary>
    public void Close()
    {
        _draft = null;
        _videoDraft = null;
        root.SetActive(false);
    }

    public void Apply()
    {
        SettingsManager.Instance.Adopt(Settings, Video);
        _draft = SettingsManager.Instance.Settings.Clone();
        _videoDraft = SettingsManager.Instance.Video.Clone();

        RefreshApplyState();
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void SelectTab(int index)
    {
        for (int i = 0; i < tabPanels.Length; i++)
        {
            tabPanels[i].SetActive(i == index);
        }

        for (int i = 0; i < tabStyles.Length; i++)
        {
            tabStyles[i].SetSelected(i == index);
        }
    }

    // 초기화 ---------------------------------------------------------------

    private void BuildDropdowns()
    {
        SetOptions(screenModeDropdown, screenModeLabels);
        SetOptions(frameRateDropdown, frameRateLabels);
        SetOptions(antiAliasingDropdown, antiAliasingLabels);
        SetOptions(voiceModeDropdown, voiceModeLabels);
        SetOptions(qualityDropdown, QualitySettings.names);

        List<string> resolutions = new();
        foreach (Resolution resolution in SettingsManager.Resolutions)
        {
            resolutions.Add($"{resolution.width} x {resolution.height}");
        }

        SetOptions(resolutionDropdown, resolutions.ToArray());
    }

    // 표에 없는 값은 키가 아니라 숫자 같은 그대로 쓸 문구다
    private static void SetOptions(TMP_Dropdown dropdown, string[] options)
    {
        if (dropdown == null) return;

        List<string> translated = new(options.Length);
        foreach (string option in options) translated.Add(SettingsText.Translate(option));

        dropdown.ClearOptions();
        dropdown.AddOptions(translated);
    }

    // 로컬라이제이션은 준비되는 데 한 프레임 이상 걸린다
    private IEnumerator BuildLanguageDropdownRoutine()
    {
        if (languageDropdown == null) yield break;

        yield return LocalizationSettings.InitializationOperation;

        List<string> names = new();
        _localeCodes.Clear();

        foreach (Locale locale in LocalizationSettings.AvailableLocales.Locales)
        {
            names.Add(locale.LocaleName);
            _localeCodes.Add(locale.Identifier.Code);
        }

        SetOptions(languageDropdown, names.ToArray());
        if (IsOpen) Refresh();
    }

    private void HookEvents()
    {
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int index = i;
            tabButtons[i].onClick.AddListener(() => SelectTab(index));
        }

        if (applyButton != null) applyButton.onClick.AddListener(Apply);
        if (resetButton != null) resetButton.onClick.AddListener(ResetToDefault);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (dimButton != null) dimButton.onClick.AddListener(Close);

        // 게임플레이
        Bind(sensitivitySlider, value => Settings.mouseSensitivity = value);
        Bind(invertYToggle, value => Settings.invertY = value);
        Bind(fovSlider, value => Settings.fieldOfView = value);

        // 그래픽
        Bind(screenModeDropdown, index => Video.screenMode = ScreenModes[index]);
        Bind(resolutionDropdown, index => Video.resolutionIndex = index);
        Bind(qualityDropdown, index => Video.qualityLevel = index);
        Bind(frameRateDropdown, index => Video.frameRateLimit = FrameRates[index]);
        Bind(antiAliasingDropdown, index => Video.antiAliasing = AntiAliasingSamples[index]);
        Bind(vSyncToggle, value => Video.vSync = value);
        Bind(shadowDistanceSlider, value => Video.shadowDistance = value);
        Bind(brightnessSlider, value => Video.brightness = value);

        // 사운드
        Bind(masterSlider, value => Settings.masterVolume = value);
        Bind(bgmSlider, value => Settings.bgmVolume = value);
        Bind(sfxSlider, value => Settings.sfxVolume = value);
        Bind(voiceSlider, value => Settings.voiceVolume = value);
        Bind(voiceModeDropdown, index => Settings.voiceMode = (VoiceMode)index);
        Bind(voiceActivationSlider, value => Settings.voiceActivation = value);

        // 일반
        Bind(languageDropdown, index => Settings.localeCode = _localeCodes[index]);
        Bind(uiScaleSlider, value => Video.uiScale = value);
    }

    private void Bind(Slider slider, System.Action<float> apply)
    {
        if (slider == null) return;

        slider.onValueChanged.AddListener(value =>
        {
            if (_loading) return;

            apply(value);
            ApplyAndRefresh();
        });
    }

    private void Bind(Toggle toggle, System.Action<bool> apply)
    {
        if (toggle == null) return;

        toggle.onValueChanged.AddListener(value =>
        {
            if (_loading) return;

            apply(value);
            ApplyAndRefresh();
        });
    }

    private void Bind(TMP_Dropdown dropdown, System.Action<int> apply)
    {
        if (dropdown == null) return;

        dropdown.onValueChanged.AddListener(index =>
        {
            if (_loading) return;

            apply(index);
            ApplyAndRefresh();
        });
    }

    // 반영 -----------------------------------------------------------------

    // 값은 사본에만 쌓이고 반영은 적용 버튼이 한다
    private void ApplyAndRefresh()
    {
        RefreshApplyState();
    }

    private void ResetToDefault()
    {
        _draft = new GameSettings();
        _videoDraft = new VideoSettings();
        Refresh();
    }

    private void RefreshApplyState()
    {
        if (applyButton != null) applyButton.interactable = !Settings.SameAs(SettingsManager.Instance.Settings) || !Video.SameAs(SettingsManager.Instance.Video);
    }

    /// <summary>설정 값을 위젯에 다시 채운다</summary>
    private void Refresh()
    {
        _loading = true;

        GameSettings settings = Settings;
        VideoSettings video = Video;

        SetValue(sensitivitySlider, settings.mouseSensitivity);
        SetValue(invertYToggle, settings.invertY);
        SetValue(fovSlider, settings.fieldOfView);

        SetValue(screenModeDropdown, System.Array.IndexOf(ScreenModes, video.screenMode));
        SetValue(resolutionDropdown, video.resolutionIndex >= 0 ? video.resolutionIndex : CurrentResolutionIndex());
        SetValue(qualityDropdown, video.qualityLevel >= 0 ? video.qualityLevel : QualitySettings.GetQualityLevel());
        SetValue(frameRateDropdown, System.Array.IndexOf(FrameRates, video.frameRateLimit));
        SetValue(antiAliasingDropdown, System.Array.IndexOf(AntiAliasingSamples, video.antiAliasing));
        SetValue(vSyncToggle, video.vSync);
        SetValue(shadowDistanceSlider, video.shadowDistance);
        SetValue(brightnessSlider, video.brightness);

        SetValue(masterSlider, settings.masterVolume);
        SetValue(bgmSlider, settings.bgmVolume);
        SetValue(sfxSlider, settings.sfxVolume);
        SetValue(voiceSlider, settings.voiceVolume);
        SetValue(voiceModeDropdown, (int)settings.voiceMode);
        SetValue(voiceActivationSlider, settings.voiceActivation);

        string localeCode = string.IsNullOrEmpty(settings.localeCode) && LocalizationSettings.SelectedLocale != null
            ? LocalizationSettings.SelectedLocale.Identifier.Code
            : settings.localeCode;
        SetValue(languageDropdown, Mathf.Max(0, _localeCodes.IndexOf(localeCode)));
        SetValue(uiScaleSlider, video.uiScale);

        _loading = false;

        RefreshApplyState();
    }


    private static int CurrentResolutionIndex()
    {
        Resolution[] list = SettingsManager.Resolutions;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].width == Screen.width && list[i].height == Screen.height) return i;
        }

        return list.Length - 1;
    }

    private static void SetValue(Slider slider, float value)
    {
        if (slider != null) slider.value = value;
    }

    private static void SetValue(Toggle toggle, bool value)
    {
        if (toggle != null) toggle.isOn = value;
    }

    private static void SetValue(TMP_Dropdown dropdown, int index)
    {
        if (dropdown == null || dropdown.options.Count == 0) return;

        dropdown.value = Mathf.Clamp(index, 0, dropdown.options.Count - 1);
        dropdown.RefreshShownValue();
    }
}
