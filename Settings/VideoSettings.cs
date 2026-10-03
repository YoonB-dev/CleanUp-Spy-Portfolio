using UnityEngine;

/// <summary>
/// 모니터와 사양에 따라 달라지는 설정이라 Steam 클라우드로 동기화하지 않고 이 기기에만 저장한다
/// </summary>
[System.Serializable]
public class VideoSettings
{
    private const string KEY = "VideoSettings";

    public FullScreenMode screenMode = FullScreenMode.FullScreenWindow;
    public int resolutionIndex = -1;    // 음수면 현재 해상도를 건드리지 않는다
    public int qualityLevel = -1;       // 음수면 프로젝트 기본 품질을 쓴다
    public int frameRateLimit = 0;      // 0이면 무제한
    public bool vSync = true;
    public float shadowDistance = 50f;
    public int antiAliasing = 2;        // MSAA 배수. 1이면 끔
    public float brightness = 1f;
    public float uiScale = 1f;

    public VideoSettings Clone() => JsonUtility.FromJson<VideoSettings>(JsonUtility.ToJson(this));

    public bool SameAs(VideoSettings other) => JsonUtility.ToJson(this) == JsonUtility.ToJson(other);

    public static VideoSettings Load()
    {
        string json = PlayerPrefs.GetString(KEY, string.Empty);
        if (string.IsNullOrEmpty(json)) return new VideoSettings();

        return JsonUtility.FromJson<VideoSettings>(json) ?? new VideoSettings();
    }

    public void Save()
    {
        PlayerPrefs.SetString(KEY, JsonUtility.ToJson(this));
        PlayerPrefs.Save();
    }
}
