using UnityEngine;

/// <summary>
/// UI 소리 종류(UISoundType)별로 재생할 소리를 정하는 설정 (버튼 클릭, 팝업 열기/닫기 등).
/// Resources/UISoundProfile 에 하나만 두면 모든 UI가 자동으로 이 설정을 쓴다.
/// </summary>
[CreateAssetMenu(fileName = "UISoundProfile", menuName = "CleanUpMafia/UI Sound Profile")]
public class UISoundProfile : ScriptableObject
{
    private const string RESOURCES_PATH = "UISoundProfile";

    [System.Serializable]
    private struct Entry
    {
        public UISoundType type;
        public SoundData sound;
    }

    [Tooltip("종류별 소리. 목록에 없는 종류는 Default 소리를 쓰고, 목록에 있지만 비워둔 종류는 소리를 내지 않음")]
    [SerializeField] private Entry[] entries =
    {
        new Entry { type = UISoundType.Default },
        new Entry { type = UISoundType.Confirm },
        new Entry { type = UISoundType.Cancel },
        new Entry { type = UISoundType.Tab },
        new Entry { type = UISoundType.Toggle },
        new Entry { type = UISoundType.PopupOpen },
        new Entry { type = UISoundType.PopupClose },
        new Entry { type = UISoundType.SliderTick },
    };

    private static UISoundProfile _loaded;
    private static bool _loadAttempted;

    /// <summary>Resources에서 한 번만 불러와 캐싱. 없으면 null (경고 1회)</summary>
    public static UISoundProfile Current
    {
        get
        {
            if (_loadAttempted) return _loaded;

            _loadAttempted = true;
            _loaded = Resources.Load<UISoundProfile>(RESOURCES_PATH);
            if (_loaded == null)
            {
                Debug.LogWarning($"[UISoundProfile] Resources/{RESOURCES_PATH} 에셋이 없어 버튼 소리가 나지 않습니다.");
            }
            return _loaded;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _loaded = null;
        _loadAttempted = false;
    }

    /// <summary>
    /// 종류에 맞는 소리를 찾습니다.
    /// 목록에 없는 종류(새로 추가하고 아직 등록 안 한 종류)는 Default 소리, 목록에 있지만 비워둔 종류와 None은 null(무음)
    /// </summary>
    public SoundData GetSound(UISoundType type)
    {
        if (type == UISoundType.None) return null;

        SoundData defaultSound = null;
        foreach (Entry entry in entries)
        {
            if (entry.type == type) return entry.sound;   // 비워뒀으면 의도적으로 무음 (예: 닫기 버튼과 팝업 닫힘 소리가 겹칠 때)
            if (entry.type == UISoundType.Default) defaultSound = entry.sound;
        }
        return defaultSound;
    }

    /// <summary>종류에 맞는 UI 소리를 재생합니다 (2D). 코드에서 직접 부를 때도 사용</summary>
    /// <param name="pitchScale">피치 배율 (슬라이더 틱처럼 값에 따라 음 높이를 바꿀 때)</param>
    public static void Play(UISoundType type, float pitchScale = 1f)
    {
        UISoundProfile profile = Current;
        if (profile == null) return;

        SoundManager.Instance?.PlaySFX(profile.GetSound(type), pitchScale);
    }
}
