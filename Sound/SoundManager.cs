using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 사운드 재생의 유일한 창구. 씬이 바뀌어도 살아남으며, 처음 접근할 때 자동으로 만들어진다.
/// 볼륨 조절은 SettingsManager가 믹서에 반영하므로, 여기서는 소리를 알맞은 믹서 그룹(BGM/SFX)으로 내보내기만 한다.
/// 네트워크는 모른다. 모두가 들어야 하는 소리는 호출하는 쪽의 ClientRpc에서 각자 이 매니저를 부른다.
/// </summary>
public class SoundManager : PersistentSingleton<SoundManager>
{
    private const string MASTER_GROUP_NAME = "Master";
    private const string BGM_GROUP_NAME = "BGM";
    private const string SFX_GROUP_NAME = "SFX";
    private const float DEFAULT_BGM_FADE = 1f;

    private BgmPlayer _bgm;
    private SfxPlayer _sfx;

    protected override void Awake()
    {
        base.Awake();
        if (Existing != this) return;   // 중복 생성분은 파괴 대기 중

        AudioMixer mixer = SettingsManager.Instance != null ? SettingsManager.Instance.Mixer : null;
        _bgm = new BgmPlayer(transform, FindGroup(mixer, BGM_GROUP_NAME));
        _sfx = new SfxPlayer(transform, FindGroup(mixer, SFX_GROUP_NAME));
    }

    private void Update()
    {
        // 일시정지(timeScale 0) 중에도 페이드가 멈추지 않도록 unscaled 사용
        _bgm?.Tick(Time.unscaledDeltaTime);
    }

    // BGM ------------------------------------------------------------------

    public void PlayBGM(SoundData bgm, float fadeDuration = DEFAULT_BGM_FADE)
    {
        if (bgm == null) return;
        _bgm.Play(bgm.FirstClip, bgm.Volume, fadeDuration);
    }

    public void PlayBGM(AudioClip clip, float volume = 1f, float fadeDuration = DEFAULT_BGM_FADE)
    {
        _bgm.Play(clip, volume, fadeDuration);
    }

    public void StopBGM(float fadeDuration = DEFAULT_BGM_FADE)
    {
        _bgm.Stop(fadeDuration);
    }

    // SFX ------------------------------------------------------------------

    /// <summary>위치 없이(2D) 재생합니다. UI, 내 캐릭터 소리 등</summary>
    /// <param name="pitchScale">SoundData의 피치에 곱할 배율 (슬라이더 값에 따라 음 높이를 바꿀 때 등)</param>
    public void PlaySFX(SoundData sfx, float pitchScale = 1f)
    {
        if (sfx == null) return;
        _sfx.Play(sfx.GetRandomClip(), sfx.Volume, sfx.GetRandomPitch() * pitchScale, null, sfx.MinDistance, sfx.MaxDistance);
    }

    public void PlaySFX(AudioClip clip, float volume = 1f)
    {
        _sfx.Play(clip, volume, 1f, null, 1f, 20f);
    }

    /// <summary>월드 위치에서(3D) 재생합니다. 거리에 따라 작아집니다</summary>
    public void PlaySFXAt(SoundData sfx, Vector3 position)
    {
        if (sfx == null) return;
        _sfx.Play(sfx.GetRandomClip(), sfx.Volume, sfx.GetRandomPitch(), position, sfx.MinDistance, sfx.MaxDistance);
    }

    public void PlaySFXAt(AudioClip clip, Vector3 position, float volume = 1f)
    {
        _sfx.Play(clip, volume, 1f, position, 1f, 20f);
    }

    public void StopAllSFX()
    {
        _sfx.StopAll();
    }

    // 내부 -----------------------------------------------------------------

    private static AudioMixerGroup FindGroup(AudioMixer mixer, string groupName)
    {
        if (mixer != null)
        {
            foreach (AudioMixerGroup group in mixer.FindMatchingGroups(MASTER_GROUP_NAME))
            {
                if (group.name == groupName) return group;
            }
        }

        // 그룹이 없어도 소리는 나야 한다. 다만 설정 창 볼륨이 적용되지 않는다
        Debug.LogWarning($"[SoundManager] 믹서 그룹 '{groupName}'을 찾지 못했습니다. 볼륨 설정이 적용되지 않습니다.");
        return null;
    }
}
