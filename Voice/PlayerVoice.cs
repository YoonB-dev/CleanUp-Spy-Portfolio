using System.Collections.Generic;
using System.IO;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public enum VoiceMode { PushToTalk, Auto }

/// <summary>
/// 근접 음성 채팅. 대상은 서버가 거리로 정하고, 재생은 말하는 사람의 AudioSource에서 나온다.
/// </summary>
public class PlayerVoice : NetworkBehaviour
{
    private const float BUFFER_SECONDS = 0.5f;              // 큐 상한. 넘치면 오래된 것부터 버려 지연이 쌓이지 않게 한다
    private const float JITTER_SECONDS = 0.1f;              // 이만큼 모인 뒤 내보내야 패킷이 늦어도 안 끊긴다
    private const int MAX_PACKET = 1000;                    // 언릴라이어블 RPC는 MTU를 넘으면 터진다
    private const float HEAR_RANGE = 30f;                   // MAX_DISTANCE보다 넉넉해야 경계에서 뚝뚝 끊기지 않는다
    private const float MIN_DISTANCE = 2f;
    private const float MAX_DISTANCE = 25f;
    private const float HANGOVER = 0.4f;                    // 음량이 내려가도 이만큼 더 보내야 말끝이 안 잘린다
    private const float EAR_HEIGHT = 1.4f;
    private const float OPEN_CUTOFF = 22000f;
    private const float WALL_CUTOFF = 700f;
    private const float WALL_VOLUME = 0.55f;
    private const float BLEND_TIME = 0.25f;

    private static readonly string[] OCCLUDER_LAYERS = { "Wall", "Ground", "Floor" };

    [SerializeField] private VoiceMode mode = VoiceMode.PushToTalk;
    [SerializeField, Range(0f, 0.2f)] private float activationLevel = 0.02f;

    private readonly List<ulong> _targets = new();
    private readonly MemoryStream _decompressed = new();

    private AudioSource _source;
    private AudioLowPassFilter _lowPass;
    private int _occluderMask;
    private AudioClip _clip;
    private float _speakUntil;
    private float _occlusion;
    private int _sampleRate;
    private int _jitterSamples;

    // 오디오 스레드와 공유한다
    private readonly object _queueLock = new();
    private float[] _queue;
    private int _read;
    private int _count;
    private bool _priming = true;

    private void Awake() => enabled = false;

    public override void OnNetworkSpawn()
    {
        // 트랜스포트가 접속할 때 Steam을 켠다. 씬에 별도 매니저가 없으므로 직접 확인한다
        enabled = SteamClient.IsValid;
        if (!enabled)
        {
            Debug.LogWarning("[PlayerVoice] Steam이 준비되지 않아 음성을 끕니다");
            return;
        }

        // 24000 같은 값을 박아두면 Steam 출력과 어긋나 목소리가 느리거나 빨라진다
        _sampleRate = (int)SteamUser.OptimalSampleRate;
        SteamUser.SampleRate = (uint)_sampleRate;
        _jitterSamples = Mathf.RoundToInt(_sampleRate * JITTER_SECONDS);

        if (IsOwner) SetMode(mode);
        else SetupAudio();   // 내 목소리는 내가 듣지 않는다
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && SteamClient.IsValid) SteamUser.VoiceRecord = false;
    }

    public void OnPushToTalk(InputAction.CallbackContext context)
    {
        if (!IsOwner || !SteamClient.IsValid || mode != VoiceMode.PushToTalk) return;

        if (context.started) SteamUser.VoiceRecord = true;
        else if (context.canceled) SteamUser.VoiceRecord = false;
    }

    public void SetMode(VoiceMode value)
    {
        if (!IsOwner || !SteamClient.IsValid) return;

        mode = value;
        SteamUser.VoiceRecord = mode == VoiceMode.Auto;
        _speakUntil = 0f;
    }

    private void Update()
    {
        if (IsOwner && SteamClient.IsValid) CaptureAndSend();

        if (_source != null) ApplyOcclusion();
    }

    // 볼륨만 줄이면 벽 너머가 아니라 멀리 있는 소리로 들린다
    private void ApplyOcclusion()
    {
        NetworkObject listener = NetworkManager.LocalClient?.PlayerObject;
        if (listener == null) return;

        Vector3 from = listener.transform.position + Vector3.up * EAR_HEIGHT;
        Vector3 to = transform.position + Vector3.up * EAR_HEIGHT;
        bool blocked = Physics.Linecast(from, to, _occluderMask);

        _occlusion = Mathf.MoveTowards(_occlusion, blocked ? 1f : 0f, Time.deltaTime / BLEND_TIME);
        _lowPass.cutoffFrequency = Mathf.Lerp(OPEN_CUTOFF, WALL_CUTOFF, _occlusion);
        _source.volume = Mathf.Lerp(1f, WALL_VOLUME, _occlusion);
    }

    private void SetupAudio()
    {
        _queue = new float[Mathf.RoundToInt(_sampleRate * BUFFER_SECONDS)];
        _clip = AudioClip.Create("Voice", _sampleRate, 1, _sampleRate, true, OnAudioRead);

        _source = gameObject.AddComponent<AudioSource>();
        _source.clip = _clip;
        _source.loop = true;
        _source.spatialBlend = 1f;
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = MIN_DISTANCE;
        _source.maxDistance = MAX_DISTANCE;

        _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
        _lowPass.cutoffFrequency = OPEN_CUTOFF;

        _occluderMask = LayerMask.GetMask(OCCLUDER_LAYERS);
        _source.Play();
    }

    // 오디오 스레드가 호출한다. 큐가 비면 무음을 낸다
    private void OnAudioRead(float[] output)
    {
        lock (_queueLock)
        {
            if (_priming && _count < _jitterSamples)
            {
                System.Array.Clear(output, 0, output.Length);
                return;
            }

            _priming = false;
            int taken = Mathf.Min(output.Length, _count);

            for (int i = 0; i < taken; i++)
            {
                output[i] = _queue[_read];
                _read = (_read + 1) % _queue.Length;
            }

            System.Array.Clear(output, taken, output.Length - taken);
            _count -= taken;

            if (_count == 0) _priming = true;
        }
    }

    private void CaptureAndSend()
    {
        if (!SteamUser.HasVoiceData) return;

        byte[] data = SteamUser.ReadVoiceDataBytes();
        if (data == null || data.Length == 0) return;

        if (data.Length > MAX_PACKET)
        {
            Debug.LogWarning($"[PlayerVoice] 음성 패킷이 커서 버림: {data.Length}바이트");
            return;
        }

        if (mode == VoiceMode.Auto && !IsSpeaking(data)) return;

        SubmitVoiceRpc(data);
    }

    private bool IsSpeaking(byte[] data)
    {
        float[] samples = Decode(data);

        float sum = 0f;
        foreach (float sample in samples)
        {
            sum += sample * sample;
        }

        float level = samples.Length > 0 ? Mathf.Sqrt(sum / samples.Length) : 0f;
        if (level >= activationLevel) _speakUntil = Time.time + HANGOVER;

        return Time.time < _speakUntil;
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void SubmitVoiceRpc(byte[] data)
    {
        _targets.Clear();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (clientId != OwnerClientId && InRange(clientId)) _targets.Add(clientId);
        }

        if (_targets.Count == 0) return;

        PlayVoiceRpc(data, RpcTarget.Group(_targets, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
    private void PlayVoiceRpc(byte[] data, RpcParams rpcParams) => Play(data);

    private void Play(byte[] data)
    {
        if (_clip == null) return;

        float[] samples = Decode(data);
        if (samples.Length > 0) Enqueue(samples);
    }

    private bool InRange(ulong clientId)
    {
        NetworkObject listener = NetworkManager.ConnectedClients[clientId].PlayerObject;

        return listener != null
            && (listener.transform.position - transform.position).sqrMagnitude <= HEAR_RANGE * HEAR_RANGE;
    }

    private float[] Decode(byte[] data)
    {
        _decompressed.SetLength(0);
        SteamUser.DecompressVoice(data, _decompressed);

        byte[] pcm = _decompressed.GetBuffer();
        float[] samples = new float[(int)_decompressed.Length / 2];

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
        }

        return samples;
    }

    private void Enqueue(float[] samples)
    {
        lock (_queueLock)
        {
            foreach (float sample in samples)
            {
                if (_count == _queue.Length)
                {
                    _read = (_read + 1) % _queue.Length;
                    _count--;
                }

                _queue[(_read + _count) % _queue.Length] = sample;
                _count++;
            }
        }
    }
}
