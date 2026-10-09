using Steamworks;
using UnityEngine;

public class SteamManager : MonoBehaviour
{
    private const uint APP_ID = 480;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Create()
    {
        GameObject steam = new GameObject(nameof(SteamManager));
        DontDestroyOnLoad(steam);
        steam.AddComponent<SteamManager>();
    }

    private void Awake()
    {
        try
        {
            SteamClient.Init(APP_ID, false);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Steam 초기화 실패: {e.Message}");
        }
    }

    private void Update()
    {
        if (SteamClient.IsValid) SteamClient.RunCallbacks();
    }

    // NetworkManager가 OnApplicationQuit에서 연결을 닫으므로 그 뒤에 끈다
    private void OnDestroy()
    {
        SteamClient.Shutdown();
    }
}
