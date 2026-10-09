using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class InGameManager : NetworkBehaviour
{
    public static InGameManager Instance { get; private set; }

    [Header("씬 설정")]
    [SerializeField] private string playSceneName = "TestPlayScene"; // 인게임 씬 이름과 정확히 맞출 것
    [SerializeField] private string lobbySceneName = "LobbyScene";

    [Header("스폰 설정")]
    [SerializeField] private GameObject inGamePlayerPrefab; // 실제 조종할 인게임 캐릭터 프리팹
    private readonly HashSet<ulong> loadedClients = new(); // 씬 로드 완료를 보고한 클라이언트 ID를 저장하는 HashSet -> 이게 다 되어야 캐릭터 스폰함.
    private bool _hasSpawnedPlayers; // 일괄 스폰은 한 번만. 스폰 후 이탈 시 재검사로 전원이 중복 스폰되는 것을 막는다

    [Header("승패 설정")]
    [Tooltip("제한 시간 종료 시 오염도 비율이 이 값 이상이면 마피아 승리")]
    [SerializeField, Range(0f, 1f)] private float mafiaWinRatio = 0.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // 1. 나중에 로드를 완료할 클라이언트들을 위해 이벤트를 등록합니다.
            NetworkManager.Singleton.SceneManager.OnSceneEvent += ServerOnSceneEvent;
            // 로딩 도중 이탈하면 남은 인원 기준으로 다시 검사해야 대기 상태에 갇히지 않는다
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            if (GameTimerManager.Instance != null) GameTimerManager.Instance.OnTimerExpired += EndGame;

            // 2.호스트를 씬 완료 상태로 간주.
            loadedClients.Add(NetworkManager.ServerClientId);

            // 3. 모든 클라이언트가 씬 로드를 완료했는지 확인하고, 완료했다면 스폰을 진행.
            CheckAndSpawnAllPlayers();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= ServerOnSceneEvent;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        if (IsServer && GameTimerManager.Instance != null) GameTimerManager.Instance.OnTimerExpired -= EndGame;
    }
    /// <summary>
    /// 로딩 도중 혹은 로딩 완료 후에 클라이언트가 나갔을 때 실행되는 콜백 함수
    /// </summary>
    private void OnClientDisconnect(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"[InGameManager] 클라이언트 {clientId}번의 연결 해제 감지.");

        // 1. 로딩 완료 목록에 해당 클라이언트가 있다면 지워줍니다.
        if (loadedClients.Contains(clientId))
        {
            loadedClients.Remove(clientId);
            Debug.Log($"[InGameManager] 클라이언트 {clientId}번을 로딩 완료 목록에서 제거했습니다.");
        }

        // 2. 누군가 나갔으므로 목표 인원수나 상태가 변했을 수 있으니, 스폰이 가능한지 다시 검사합니다.
        CheckAndSpawnAllPlayers();
    }

    private void ServerOnSceneEvent(SceneEvent sceneEvent)
    {
        if (!IsServer) return;

        // 인게임 씬이 완전히 로드되었을 때만 감지합니다.
        if (sceneEvent.SceneEventType == SceneEventType.LoadComplete && sceneEvent.SceneName == playSceneName)
        {
            ulong clientId = sceneEvent.ClientId;
            Debug.Log($"[InGameManager] 클라이언트 {clientId}번의 인게임 씬 로딩 완료 감지.");
            // 완료된 클라이언트 목록에 추가
            if (!loadedClients.Contains(clientId))
            {
                loadedClients.Add(clientId);
            }

            // 다 됐는지 검사
            CheckAndSpawnAllPlayers();
        }
    }

    /// <summary>
    /// 로비에 있던 인원 전체가 인게임 씬을 로드했는지 확인하고, 
    /// 모두가 완료되었다면 모든 플레이어 캐릭터를 동시에 일괄 스폰합니다.
    /// </summary>
    private void CheckAndSpawnAllPlayers()
    {
        if (!IsServer || _hasSpawnedPlayers) return;

        // 1. 방 설정(RoomSettings)에 등록된 전체 인원 수 가져오기
        if (RoomSettings.Instance == null)
        {
            Debug.LogWarning("[InGameManager] RoomSettings 인스턴스를 찾을 수 없습니다.");
            return;
        }

        int targetPlayerCount = RoomSettings.Instance.AllPlayers.Count;
        int currentLoadedCount = loadedClients.Count;

        Debug.Log($"[InGameManager] 로딩 체크: ({currentLoadedCount} / {targetPlayerCount})");

        // 2. 전체 인원이 아직 다 로드되지 않았다면 대기합니다.
        if (currentLoadedCount < targetPlayerCount)
        {
            Debug.Log("[InGameManager] 아직 모든 플레이어가 로딩되지 않았습니다. 대기 중...");
            return;
        }

        // 3. 한 명도 유실 없이 메시지를 받을 준비가 되었으므로 모두 한 번에 스폰합니다.
        Debug.Log("[InGameManager] 모든 플레이어 로딩 완료! 일괄 스폰을 시작합니다.");

        // 더 이상 중복 로딩 감지 및 스폰 처리가 일어나는 것을 막기 위해 이벤트를 꺼줍니다.
        _hasSpawnedPlayers = true;
        NetworkManager.Singleton.SceneManager.OnSceneEvent -= ServerOnSceneEvent;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;

        foreach (ulong clientId in loadedClients)
        {
            SpawnPlayerCharacter(clientId);
        }
        // 4. 모든 플레이어 스폰이 끝났다면, 이제 타이머를 시작한다.
        StartTimer();
    }

    private void SpawnPlayerCharacter(ulong clientId)
    {
        // 1. 생성하기 전에 매니저로부터 랜덤 좌표를 먼저 받아옵니다.
        Vector3 randomSpawnPos = Vector3.zero;
        if (PlayerSpawnManager.Instance != null)
        {
            randomSpawnPos = PlayerSpawnManager.Instance.GetRandomSpawnPosition();
        }

        // 2. 애초에 태어날 때부터 랜덤 위치에서 스폰되도록 만듭니다.
        GameObject playerObj = Instantiate(inGamePlayerPrefab, randomSpawnPos, Quaternion.identity);

        NetworkObject netObj = playerObj.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(clientId, destroyWithScene: true);
    }

    // 타이머 
    private void StartTimer()
    {
        if (GameTimerManager.Instance != null)
        {
            int totalSeconds = RoomSettings.Instance.PlayTimeMinutes.Value;

            GameTimerManager.Instance.StartTimer(5, totalSeconds);
            Debug.Log($"[InGameManager] GameTimerManager에 타이머 구동 명령 전달: {totalSeconds}초");
        }
        else
        {
            Debug.LogError("[InGameManager] 씬에 GameTimerManager 인스턴스가 존재하지 않습니다!");
        }
    }

    private void EndGame()
    {
        ScoreManager score = ScoreManager.Instance;
        float max = score.MaxContaminationScore;

        GameOverClientRpc(score.TrashContamination / max, score.BoxContamination / max, score.PaintContamination / max);
    }

    public void ReturnToLobby()
    {
        if (!IsServer) return;
        NetworkManager.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
    }

    [ClientRpc]
    private void GameOverClientRpc(float trash, float box, float paint)
    {
        PlayerRole winner = trash + box + paint >= mafiaWinRatio ? PlayerRole.Mafia : PlayerRole.Citizen;
        Debug.Log($"[InGameManager] 게임 종료. 오염도 {trash + box + paint:P0}, 승리: {winner}");

        NetworkObject player = LocalPlayerInput.Local;
        if (player != null)
        {
            PlayerActionGate.GetOrAdd(player.gameObject).SetUIOpen(true);
            RecordResult(player, winner);
        }

        InGameUIController.Instance?.ShowResult(winner, trash, box, paint, mafiaWinRatio);
    }

    private static void RecordResult(NetworkObject player, PlayerRole winner)
    {
        PlayStats stats = PlayerSave.Data.stats;
        stats.totalGamesPlayed++;
        player.GetComponent<PlayerData>().AddStatsTo(stats);

        if (player.GetComponent<RoleManager>().CurrentRole == PlayerRole.Mafia)
        {
            stats.mafiaPlayed++;
            if (winner == PlayerRole.Mafia) stats.mafiaWins++;
        }
        else
        {
            stats.citizenPlayed++;
            if (winner == PlayerRole.Citizen) stats.citizenWins++;
        }

        PlayerSave.Save();
    }
}
