using UnityEngine;
using Steamworks;

public class SteamManager : MonoBehaviour
{
    public static bool IsInitialized { get; private set; }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

        try
        {
            SteamClient.Init(480);
            IsInitialized = true;
            Debug.Log($"Steam 초기화 성공. SteamID: {SteamClient.SteamId}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Steam 초기화 실패: {e.Message}");
            IsInitialized = false;
        }
    }

    private void OnApplicationQuit()
    {
        if (IsInitialized)
            SteamClient.Shutdown();
    }

    private void Update()
    {
        if (IsInitialized)
            SteamClient.RunCallbacks();
    }
}