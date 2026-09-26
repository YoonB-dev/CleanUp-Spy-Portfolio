using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 배경음악 재생 담당. AudioSource 2개를 번갈아 쓰며 곡이 바뀔 때 크로스페이드한다.
/// BGM 믹서 그룹으로 출력한다. 페이드는 SoundManager의 Update에서 Tick으로 진행한다.
/// </summary>
public class BgmPlayer
{
    private readonly AudioSource[] _sources = new AudioSource[2];
    private readonly float[] _startVolumes = new float[2];
    private readonly float[] _targetVolumes = new float[2];
    private int _activeIndex;

    private float _fadeDuration;
    private float _fadeTimer;
    private bool _isFading;

    public BgmPlayer(Transform root, AudioMixerGroup outputGroup)
    {
        for (int i = 0; i < _sources.Length; i++)
        {
            GameObject sourceObject = new GameObject($"BGM_{i}");
            sourceObject.transform.SetParent(root, false);

            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = outputGroup;   // 설정 창의 BGM 볼륨이 여기에 걸린다
            _sources[i] = source;
        }
    }

    /// <summary>
    /// 곡을 재생합니다. 이미 같은 곡이 나오고 있으면 처음부터 다시 틀지 않고 볼륨만 맞춥니다.
    /// </summary>
    public void Play(AudioClip clip, float volume, float fadeDuration)
    {
        if (clip == null)
        {
            Stop(fadeDuration);
            return;
        }

        AudioSource active = _sources[_activeIndex];
        if (active.isPlaying && active.clip == clip)
        {
            StartFade(fadeDuration, volume, 0f);
            return;
        }

        // 다른 쪽 소스로 새 곡을 틀고 교차로 페이드
        _activeIndex = 1 - _activeIndex;
        AudioSource next = _sources[_activeIndex];

        // 막 페이드아웃 중이던 같은 곡으로 되돌아온 경우엔 이어서 재생
        if (!(next.isPlaying && next.clip == clip))
        {
            next.clip = clip;
            next.volume = 0f;
            next.Play();
        }

        StartFade(fadeDuration, volume, 0f);
    }

    public void Stop(float fadeDuration)
    {
        StartFade(fadeDuration, 0f, 0f);
    }

    public void Tick(float deltaTime)
    {
        if (!_isFading) return;

        _fadeTimer += deltaTime;
        ApplyFade(_fadeDuration > 0f ? Mathf.Clamp01(_fadeTimer / _fadeDuration) : 1f);
    }

    private void StartFade(float duration, float activeTargetVolume, float inactiveTargetVolume)
    {
        for (int i = 0; i < _sources.Length; i++)
        {
            _startVolumes[i] = _sources[i].volume;
        }
        _targetVolumes[_activeIndex] = activeTargetVolume;
        _targetVolumes[1 - _activeIndex] = inactiveTargetVolume;

        _fadeDuration = Mathf.Max(0f, duration);
        _fadeTimer = 0f;
        _isFading = true;

        if (_fadeDuration <= 0f) ApplyFade(1f);
    }

    private void ApplyFade(float t)
    {
        for (int i = 0; i < _sources.Length; i++)
        {
            _sources[i].volume = Mathf.Lerp(_startVolumes[i], _targetVolumes[i], t);
        }

        if (t < 1f) return;

        // 페이드가 끝나면 소리가 0이 된 소스는 정지
        _isFading = false;
        for (int i = 0; i < _sources.Length; i++)
        {
            if (_targetVolumes[i] <= 0f) _sources[i].Stop();
        }
    }
}
