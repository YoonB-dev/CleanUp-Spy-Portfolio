using UnityEngine;

/// <summary>
/// 어느 기기에서 켜도 따라오는 설정. PlayerSaveData에 담겨 Steam 클라우드로 동기화된다.
/// 필드를 추가해도 예전 저장본은 그대로 읽히고 새 필드만 기본값이 된다.
/// </summary>
[System.Serializable]
public class GameSettings
{
    // 게임플레이
    public float mouseSensitivity = 0.1f;
    public bool invertY = false;
    public float fieldOfView = 60f;

    // 사운드
    public float masterVolume = 1f;
    public float bgmVolume = 0.7f;
    public float sfxVolume = 1f;
    public float voiceVolume = 1f;
    public VoiceMode voiceMode = VoiceMode.PushToTalk;
    public float voiceActivation = 0.02f;

    // 일반
    public string localeCode = "";      // 비어 있으면 시스템 언어를 따른다

    /// <summary>설정 창이 편집할 사본. 적용을 누르기 전까지 원본을 건드리지 않는다</summary>
    public GameSettings Clone() => JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(this));

    public bool SameAs(GameSettings other) => JsonUtility.ToJson(this) == JsonUtility.ToJson(other);
}
