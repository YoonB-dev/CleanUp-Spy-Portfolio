using UnityEngine;

/// <summary>
/// 설정 값 묶음. PlayerPrefs에 JSON 한 덩어리로 저장한다.
/// 필드를 추가해도 예전 저장본은 그대로 읽히고 새 필드만 기본값이 된다.
/// </summary>
[System.Serializable]
public class GameSettings
{
    private const string KEY = "GameSettings";

    // 게임플레이
    public float mouseSensitivity = 0.1f;
    public bool invertY = false;
    public float fieldOfView = 60f;

    // 그래픽
    public FullScreenMode screenMode = FullScreenMode.FullScreenWindow;
    public int resolutionIndex = -1;    // 음수면 현재 해상도를 건드리지 않는다
    public int qualityLevel = -1;       // 음수면 프로젝트 기본 품질을 쓴다
    public int frameRateLimit = 0;      // 0이면 무제한
    public bool vSync = true;
    public float shadowDistance = 50f;
    public int antiAliasing = 2;        // MSAA 배수. 1이면 끔
    public float brightness = 1f;

    // 사운드
    public float masterVolume = 1f;
    public float bgmVolume = 0.7f;
    public float sfxVolume = 1f;
    public float voiceVolume = 1f;
    public VoiceMode voiceMode = VoiceMode.PushToTalk;
    public float voiceActivation = 0.02f;

    // 일반
    public string localeCode = "";      // 비어 있으면 시스템 언어를 따른다
    public float uiScale = 1f;

    /// <summary>설정 창이 편집할 사본. 적용을 누르기 전까지 원본을 건드리지 않는다</summary>
    public GameSettings Clone() => JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(this));

    public bool SameAs(GameSettings other) => JsonUtility.ToJson(this) == JsonUtility.ToJson(other);

    public static GameSettings Load()
    {
        string json = PlayerPrefs.GetString(KEY, string.Empty);
        if (string.IsNullOrEmpty(json)) return new GameSettings();

        return JsonUtility.FromJson<GameSettings>(json) ?? new GameSettings();
    }

    public void Save()
    {
        PlayerPrefs.SetString(KEY, JsonUtility.ToJson(this));
        PlayerPrefs.Save();
    }
}
