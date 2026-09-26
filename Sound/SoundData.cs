using UnityEngine;

/// <summary>
/// 사운드 하나의 재생 설정. 호출하는 쪽에서 인스펙터로 연결해 SoundManager에 넘긴다.
/// BGM/SFX 모두 이 에셋을 쓴다 (BGM은 첫 번째 클립만 사용).
/// </summary>
[CreateAssetMenu(fileName = "SoundData", menuName = "CleanUpMafia/Sound Data")]
public class SoundData : ScriptableObject
{
    [Tooltip("여러 개면 재생할 때마다 무작위로 하나를 고른다 (같은 소리 반복의 단조로움 방지)")]
    [SerializeField] private AudioClip[] clips;

    [Range(0f, 1f)]
    [SerializeField] private float volume = 1f;

    [Tooltip("재생할 때마다 이 범위에서 피치를 무작위로 고른다. BGM은 (1, 1) 권장")]
    [SerializeField] private Vector2 pitchRange = new Vector2(1f, 1f);

    [Header("3D 설정 (PlaySFXAt으로 재생할 때만 사용, BGM에서는 무시됨)")]
    [Tooltip("이 거리 안에서는 최대 볼륨")]
    [SerializeField] private float minDistance = 1f;
    [Tooltip("이 거리 밖에서는 들리지 않음")]
    [SerializeField] private float maxDistance = 20f;

    public float Volume => volume;
    public float MinDistance => minDistance;
    public float MaxDistance => maxDistance;

    /// <summary>BGM처럼 항상 같은 클립이 필요할 때 사용</summary>
    public AudioClip FirstClip => clips != null && clips.Length > 0 ? clips[0] : null;

    public AudioClip GetRandomClip()
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    public float GetRandomPitch() => Random.Range(pitchRange.x, pitchRange.y);
}
