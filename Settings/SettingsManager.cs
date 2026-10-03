using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 설정 값을 들고 있다가 실제 시스템에 반영한다. 씬이 바뀌어도 살아남는다.
/// 어느 씬에서 실행을 시작하든 되도록 Resources/SettingsManager 프리팹을 자동으로 띄운다.
/// </summary>
public class SettingsManager : MonoBehaviour
{
    private const string PREFAB_PATH = "SettingsManager";

    [Header("오디오")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private AudioMixerGroup voiceGroup;
    [SerializeField] private string masterParam = "MasterVolume";
    [SerializeField] private string bgmParam = "BGMVolume";
    [SerializeField] private string sfxParam = "SFXVolume";
    [SerializeField] private string voiceParam = "VoiceVolume";

    [Header("입력")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("그래픽")]
    [SerializeField] private VolumeProfile globalVolumeProfile;   // 밝기 조절용. ColorAdjustments가 들어 있어야 한다

    private static SettingsManager _instance;
    private static Resolution[] _resolutions;

    /// <summary>없으면 프리팹에서 만들어 낸다</summary>
    public static SettingsManager Instance
    {
        get
        {
            if (_instance != null) return _instance;

            GameObject prefab = Resources.Load<GameObject>(PREFAB_PATH);
            if (prefab != null)
            {
                Instantiate(prefab);   // Awake에서 _instance가 채워진다
                return _instance;
            }

            // 프리팹이 없어도 값 자체는 돌아가야 한다. 믹서와 액션 참조만 비어 있다
            Debug.LogWarning($"[SettingsManager] Resources/{PREFAB_PATH} 프리팹이 없어 기본값으로 동작합니다");
            new GameObject(nameof(SettingsManager)).AddComponent<SettingsManager>();
            return _instance;
        }
    }

    /// <summary>이미 만들어진 것만 본다. 종료 중에 다시 만들지 않으려는 쪽에서 쓴다</summary>
    public static SettingsManager Existing => _instance;

    public AudioMixer Mixer => mixer;   // SoundManager가 BGM/SFX 그룹을 찾을 때 쓴다
    public GameSettings Settings
    {
        get => PlayerSave.Data.settings;
        private set => PlayerSave.Data.settings = value;
    }

    public VideoSettings Video { get; private set; }
    public AudioMixerGroup VoiceGroup => voiceGroup;
    public InputActionAsset InputActions => inputActions;

    /// <summary>값이 바뀔 때마다. 카메라나 캔버스처럼 씬에 있는 쪽이 받아서 다시 읽는다</summary>
    public event Action Changed;

    /// <summary>같은 해상도가 주사율만 다르게 여러 번 나오므로 크기 기준으로 추린다</summary>
    public static Resolution[] Resolutions
    {
        get
        {
            if (_resolutions != null) return _resolutions;

            List<Resolution> list = new();
            foreach (Resolution resolution in Screen.resolutions)
            {
                if (list.Count > 0)
                {
                    Resolution last = list[^1];
                    if (last.width == resolution.width && last.height == resolution.height)
                    {
                        list[^1] = resolution;   // 같은 크기면 더 높은 주사율로 갱신
                        continue;
                    }
                }

                list.Add(resolution);
            }

            _resolutions = list.ToArray();
            return _resolutions;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        Video = VideoSettings.Load();
        LoadKeyBindings();

        // 시작할 때 저장까지 하면 클라우드를 아직 못 받은 기기가 기본값으로 덮어쓴다
        ApplyGraphics();
        ApplyAudio();
        ApplyGeneral();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>설정 창에서 편집한 사본을 받아 실제로 반영한다</summary>
    public void Adopt(GameSettings settings, VideoSettings video)
    {
        if (settings == null || video == null) return;

        Settings = settings;
        Video = video;
        ApplyAll();
    }

    public void ApplyAll()
    {
        ApplyGraphics();
        ApplyAudio();
        ApplyGeneral();
        Notify();
    }

    /// <summary>값을 바꾼 쪽이 호출한다. 저장까지 함께 한다</summary>
    public void Notify()
    {
        PlayerSave.Save();
        Video.Save();
        Changed?.Invoke();
    }

    public void ResetToDefault()
    {
        Settings = new GameSettings();
        Video = new VideoSettings();
        ResetKeyBindings();
        ApplyAll();
    }

    // 그래픽 ---------------------------------------------------------------

    public void ApplyGraphics()
    {
        if (Video.qualityLevel >= 0)
        {
            QualitySettings.SetQualityLevel(Mathf.Min(Video.qualityLevel, QualitySettings.names.Length - 1), true);
        }

        QualitySettings.vSyncCount = Video.vSync ? 1 : 0;

        // 수직동기화가 켜져 있으면 targetFrameRate는 무시되므로 굳이 걸지 않는다
        Application.targetFrameRate = Video.vSync || Video.frameRateLimit <= 0 ? -1 : Video.frameRateLimit;

        // 품질 프리셋을 바꾸면 URP 에셋이 통째로 갈리므로 세부 값은 그 뒤에 덮는다
        UniversalRenderPipelineAsset urp = CurrentUrpAsset;
        if (urp != null)
        {
            urp.shadowDistance = Video.shadowDistance;
            urp.msaaSampleCount = Mathf.Max(1, Video.antiAliasing);
        }

        ApplyResolution();
        ApplyBrightness();
    }

    private static UniversalRenderPipelineAsset CurrentUrpAsset =>
        QualitySettings.renderPipeline as UniversalRenderPipelineAsset
        ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

    private void ApplyResolution()
    {
        if (Video.resolutionIndex < 0)
        {
            if (Screen.fullScreenMode != Video.screenMode) Screen.fullScreenMode = Video.screenMode;
            return;
        }

        Resolution target = Resolutions[Mathf.Min(Video.resolutionIndex, Resolutions.Length - 1)];
        if (Screen.width == target.width && Screen.height == target.height && Screen.fullScreenMode == Video.screenMode)
        {
            return;
        }

        Screen.SetResolution(target.width, target.height, Video.screenMode);
    }

    // URP에는 감마 슬라이더가 없어서 전역 볼륨의 노출값으로 대신한다
    private void ApplyBrightness()
    {
        if (globalVolumeProfile == null) return;
        if (!globalVolumeProfile.TryGet(out ColorAdjustments colorAdjustments)) return;

        colorAdjustments.postExposure.overrideState = true;
        colorAdjustments.postExposure.value = (Video.brightness - 1f) * 2f;
    }

    // 사운드 ---------------------------------------------------------------

    public void ApplyAudio()
    {
        SetMixerVolume(masterParam, Settings.masterVolume);
        SetMixerVolume(bgmParam, Settings.bgmVolume);
        SetMixerVolume(sfxParam, Settings.sfxVolume);
        SetMixerVolume(voiceParam, Settings.voiceVolume);
    }

    // 슬라이더는 0~1이지만 믹서는 데시벨이라 로그로 바꿔 준다. 0은 -80dB로 떨군다
    private void SetMixerVolume(string parameter, float value)
    {
        if (mixer == null || string.IsNullOrEmpty(parameter)) return;

        mixer.SetFloat(parameter, value <= 0.0001f ? -80f : Mathf.Log10(value) * 20f);
    }

    // 일반 -----------------------------------------------------------------

    public void ApplyGeneral()
    {
        StartCoroutine(ApplyLocaleRoutine(Settings.localeCode));
    }

    private IEnumerator ApplyLocaleRoutine(string localeCode)
    {
        yield return LocalizationSettings.InitializationOperation;

        Locale locale = string.IsNullOrEmpty(localeCode)
            ? LocalizationSettings.ProjectLocale
            : LocalizationSettings.AvailableLocales.GetLocale(localeCode);

        if (locale != null) LocalizationSettings.SelectedLocale = locale;
    }

    // 키 바인딩 ------------------------------------------------------------

    public void SaveKeyBindings()
    {
        if (inputActions == null) return;

        PlayerSave.Data.keyBindings = inputActions.SaveBindingOverridesAsJson();
        PlayerSave.Save();
    }

    public void LoadKeyBindings()
    {
        if (inputActions == null) return;

        string json = PlayerSave.Data.keyBindings;
        if (!string.IsNullOrEmpty(json)) inputActions.LoadBindingOverridesFromJson(json);
    }

    public void ResetKeyBindings()
    {
        if (inputActions == null) return;

        inputActions.RemoveAllBindingOverrides();
        PlayerSave.Data.keyBindings = "";
        PlayerSave.Save();
    }
}
