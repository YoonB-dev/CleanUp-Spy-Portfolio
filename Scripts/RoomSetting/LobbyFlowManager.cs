using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;

public class LobbyFlowManager : NetworkBehaviour
{
    public static LobbyFlowManager Instance { get; private set; }
    [SerializeField] private GameObject lobbyCharacterPrefab; // 로비에서 보여줄 캐릭터 프리팹
    [SerializeField] private GameObject roomSettingsPrefab;
    private string lobbySceneName = "LobbyScene"; // 로비 씬 이름
    private string playSceneName = "TestPlayScene";
    public void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }


    public override void OnNetworkSpawn()
    {
        // 씬 로딩 완료 및 클라이언트 관리는 오직 '서버(호스트)'만 제어합니다.
        if (IsServer)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += ServerOnLobbySceneEvent;

            // 게임이 끝나고 대기방에 돌아왔을 때도 방 설정을 유지하도록 씬에 두지 않고 한 번만 스폰한다
            if (RoomSettings.Instance == null)
            {
                Instantiate(roomSettingsPrefab).GetComponent<NetworkObject>().Spawn(destroyWithScene: false);
            }
        }
    }
    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= ServerOnLobbySceneEvent;
        }
    }

    private void ServerOnLobbySceneEvent(SceneEvent sceneEvent)
    {
        if (!IsServer) return;

        // 누군가 '로비 씬' 로딩을 완료했을 때만 로비 캐릭터를 만듭니다.
        if (sceneEvent.SceneEventType == SceneEventType.LoadComplete && sceneEvent.SceneName == lobbySceneName)
        {
            SpawnLobbyCharacter(sceneEvent.ClientId);
        }
    }

    private void SpawnLobbyCharacter(ulong clientId)
    {
        Vector3 randomSpawnPos = Vector3.zero;
        if (PlayerSpawnManager.Instance != null)
        {
            randomSpawnPos = PlayerSpawnManager.Instance.GetRandomSpawnPosition();
        }

        GameObject playerObj = Instantiate(lobbyCharacterPrefab, randomSpawnPos, Quaternion.identity);

        NetworkObject netObj = playerObj.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(clientId, destroyWithScene: true);
    }

    public void TryStartGame()
    {
        // 임시로 비활성화
        // if (!NetworkManager.Singleton.IsHost) return;

        // int total = RoomSettings.Instance.AllPlayers.Count;
        // int targetCount = RoomSettings.Instance.PlayerCount.Value; // 설정된 정원

        // if (total < targetCount) return;

        // int readyCount = 0;

        // for (int i = 0; i < RoomSettings.Instance.AllPlayers.Count; i++)
        // {
        //     if (RoomSettings.Instance.AllPlayers[i].IsReady)
        //     {
        //         readyCount++;
        //     }
        // }
        // if (total == 0 || readyCount != total) return;

        // Debug.Log("[LobbyFlowManager] 모든 인원 준비 완료. 인게임 씬으로 전환합니다.");
        
        // 그 뒤에 다음 씬 로드 진행
        var status = NetworkManager.Singleton.SceneManager.LoadScene(playSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);

        // 씬 전환 실패 시 로그 출력
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[LobbyFlowManager] 씬 전환 실패 상태 코드: {status}");
        }
        else
        {
            // 씬 이동하면서 본인 삭제
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
        }
    }
}