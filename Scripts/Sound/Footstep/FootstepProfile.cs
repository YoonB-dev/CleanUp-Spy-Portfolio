using UnityEngine;

/// <summary>
/// 표면 종류(SurfaceType)별 발소리 설정. PlayerFootsteps에 인스펙터로 연결한다.
/// 발소리는 속도에 따라 커지므로 SoundData 볼륨은 0.5 정도로 여유를 두는 것을 권장 (최종 볼륨은 1을 넘지 않음)
/// </summary>
[CreateAssetMenu(fileName = "FootstepProfile", menuName = "CleanUpMafia/Footstep Profile")]
public class FootstepProfile : ScriptableObject
{
    [System.Serializable]
    private struct Entry
    {
        public SurfaceType surface;
        public SoundData sound;
    }

    [Tooltip("표면별 발소리. 목록에 없거나 비어 있는 표면은 Default 소리를 사용")]
    [SerializeField] private Entry[] entries =
    {
        new Entry { surface = SurfaceType.Default },
        new Entry { surface = SurfaceType.Dirt },
        new Entry { surface = SurfaceType.Wood },
        new Entry { surface = SurfaceType.Stone },
    };

    /// <summary>표면에 맞는 발소리를 찾습니다. 없거나 비어 있으면 Default 소리</summary>
    public SoundData GetSound(SurfaceType surface)
    {
        SoundData defaultSound = null;
        foreach (Entry entry in entries)
        {
            if (entry.surface == surface && entry.sound != null) return entry.sound;
            if (entry.surface == SurfaceType.Default) defaultSound = entry.sound;
        }
        return defaultSound;
    }
}
