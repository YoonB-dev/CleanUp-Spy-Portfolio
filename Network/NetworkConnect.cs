using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkConnect : MonoBehaviour
{
    public static NetworkConnect Instance { get; private set; }
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private string lobbySceneName = "LobbyScene";
    public int MaxPlayers { get; set; } = 4;
    // 접속 실패 부분
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private float autoResetDelay = 3.0f; // 튕긴 후 재시도까지 대기 시간 (3초)
    private void Awake()
    {
        // --- 싱글톤 및 DontDestroyOnLoad 설정 ---
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        //서버로 동작할 때만 실제로 호출되므로 host/client 구분 없이 항상 등록해도 무방
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
        // Host 버튼을 누르면 실행될 로직
        hostButton.onClick.AddListener(() =>
        {
            bool isHostStarted = NetworkManager.Singleton.StartHost();
            if (!isHostStarted)
            {
                StartCoroutine(ResetConnectionUI("Fail to start host. Please check your network settings."));
                return;
            }
            HideButtons();
            Debug.Log("Host started");
            NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        });

        // Client 버튼을 누르면 실행될 로직
        clientButton.onClick.AddListener(() =>
        {
            bool isClientStarted = NetworkManager.Singleton.StartClient();
            if (!isClientStarted)
            {
                StartCoroutine(ResetConnectionUI("Fail to start client. Please check your network settings."));
                return;
            }
            HideButtons();
            Debug.Log("Client started");
        });

        // 클라이언트가 서버와 연결이 끊겼을 때 호출되는 콜백 등록
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // 접속 성공 후 화면을 깔끔하게 하기 위해 UI를 숨기는 함수
    private void HideButtons()
    {
        hostButton.gameObject.SetActive(false);
        clientButton.gameObject.SetActive(false);
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // 1. 이미 게임이 시작된 방이라면 입장 거부
        if (GameTimerManager.Instance != null && GameTimerManager.Instance.CurrentState.Value != TimerState.None)
        {
            response.Approved = false;
            response.Reason = "이미 게임이 시작된 방입니다.";
            response.Pending = false;
            return;
        }
        int currentConnectedCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        // 2. [실시간 인원 체크] 로비에서 RoomSettings에 의해 갱신된 MaxPlayers를 기준으로 필터링
        if (currentConnectedCount >= MaxPlayers)
        {
            response.Approved = false;
            response.Reason = "방이 가득 찼습니다.";
            response.Pending = false;
            Debug.LogWarning($"[출입 통제] 제한 인원({MaxPlayers}명) 초과로 클라이언트({request.ClientNetworkId}) 컷!");
            return;
        }

        // 인원이 남으면 입장 허가
        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Pending = false;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // 서버(호스트)가 연결 끊긴 것은 방이 터진 것이므로 클라이언트 입장만 체크
        if (NetworkManager.Singleton.IsServer) return;

        // Netcode가 서버로부터 받아온 거절 사유를 가져옵니다.
        string rejectReason = NetworkManager.Singleton.DisconnectReason;

        if (string.IsNullOrEmpty(rejectReason))
        {
            rejectReason = "서버와의 연결이 끊어졌습니다.";
        }

        Debug.LogWarning($"[접속 실패] 사유: {rejectReason}");

        // 3초 뒤에 UI를 원래대로 돌려놓는 코루틴 시작
        StartCoroutine(ResetConnectionUI(rejectReason));
    }

    // UI 복구 및 알림
    private IEnumerator ResetConnectionUI(string reasonMessage)
    {
        if (statusText != null)
        {
            statusText.text = $"{reasonMessage}\n{autoResetDelay}초 후 메인으로 돌아갑니다.";
        }

        // 지정된 시간(3초) 동안 대기
        yield return new WaitForSeconds(autoResetDelay);

        // Netcode 내부 상태 연결 종료 초기화
        NetworkManager.Singleton.Shutdown();

        // UI 버튼들 다시 활성화
        hostButton.gameObject.SetActive(true);
        clientButton.gameObject.SetActive(true);

        if (statusText != null)
        {
            statusText.text = "원하는 모드를 선택하세요.";
        }
    }
}
