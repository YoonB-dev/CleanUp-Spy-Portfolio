using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 효과음 재생 담당. AudioSource를 풀로 들고 있다가 짧은 소리를 동시에 여러 개 낸다.
/// 2D(위치 없음, UI 등)와 3D(월드 위치) 재생을 모두 처리하며 SFX 믹서 그룹으로 출력한다.
/// </summary>
public class SfxPlayer
{
    private const int INITIAL_POOL_SIZE = 8;
    private const int MAX_POOL_SIZE = 32;   // 이보다 많이 겹치면 가장 오래된 소리부터 끊고 재사용

    private readonly Transform _root;
    private readonly AudioMixerGroup _outputGroup;
    private readonly List<AudioSource> _sources = new List<AudioSource>();
    private int _nextStealIndex;

    public SfxPlayer(Transform root, AudioMixerGroup outputGroup)
    {
        _root = root;
        _outputGroup = outputGroup;

        for (int i = 0; i < INITIAL_POOL_SIZE; i++)
        {
            CreateSource();
        }
    }

    /// <summary>
    /// 클립을 재생합니다. position이 있으면 그 위치에서 3D로, 없으면 2D로 재생합니다.
    /// </summary>
    public void Play(AudioClip clip, float volume, float pitch, Vector3? position, float minDistance, float maxDistance)
    {
        if (clip == null) return;

        AudioSource source = GetAvailableSource();
        bool is3D = position.HasValue;

        source.transform.position = is3D ? position.Value : _root.position;
        source.spatialBlend = is3D ? 1f : 0f;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.Play();
    }

    public void StopAll()
    {
        foreach (AudioSource source in _sources)
        {
            source.Stop();
        }
    }

    private AudioSource GetAvailableSource()
    {
        foreach (AudioSource source in _sources)
        {
            if (!source.isPlaying) return source;
        }

        if (_sources.Count < MAX_POOL_SIZE) return CreateSource();

        // 전부 재생 중이면 순서대로 돌아가며 끊고 재사용
        AudioSource stolen = _sources[_nextStealIndex];
        _nextStealIndex = (_nextStealIndex + 1) % _sources.Count;
        stolen.Stop();
        return stolen;
    }

    private AudioSource CreateSource()
    {
        GameObject sourceObject = new GameObject($"SFX_{_sources.Count}");
        sourceObject.transform.SetParent(_root, false);

        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = _outputGroup;   // 설정 창의 SFX 볼륨이 여기에 걸린다
        source.rolloffMode = AudioRolloffMode.Linear;  // maxDistance에서 정확히 0이 되도록 (음성 채팅과 같은 방식)
        source.dopplerLevel = 0f;

        _sources.Add(source);
        return source;
    }
}
