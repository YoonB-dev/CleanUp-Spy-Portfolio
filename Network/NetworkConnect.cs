using System.Collections;
using Netcode.Transports.Facepunch;
using Steamworks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkConnect : MonoBehaviour
{
    public static NetworkConnect Instance { get; private set; }
    public string RoomCode { get; private set; }
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;   // 접속은 JoinRoomModal이 시작한다. 여기서는 숨기고 되살리는 용도
    [SerializeField] private string lobbySceneName = "LobbyScene";
    [Min(0f)]
    public int MaxPlayers { get; set; } = 4;
    // Steam Relay 관련
    [SerializeField] private FacepunchTransport transport;

    // 접속 실패 부분
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private float autoResetDelay = 3.0f; // 튕긴 후 재시도까지 대기 시간 (3초)

    private void Awake()
    {
        // --- 싱글톤 및 DontDestroyOnLoad 설정 ---
        if (Instance != null && Instance != this)
        {
            Instance.TakeOverUI(this);   // 살아남은 쪽이 파괴된 버튼을 붙들지 않게
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        //서버로 동작할 때만 실제로 호출되므로 host/client 구분 없이 항상 등록해도 무방
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;

        BindHostButton();

        // 클라이언트가 서버와 연결이 끊겼을 때 호출되는 콜백 등록
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // 트랜스포트는 살아남은 것을 그대로 쓴다
    private void TakeOverUI(NetworkConnect fresh)
    {
        hostButton = fresh.hostButton;
        clientButton = fresh.clientButton;
        statusText = fresh.statusText;

        BindHostButton();
    }

    private void BindHostButton()
    {
        if (hostButton == null) return;

        hostButton.onClick.RemoveListener(StartHosting);
        hostButton.onClick.AddListener(StartHosting);
    }

    private void StartHosting()
    {
        if (!NetworkManager.Singleton.StartHost())
        {
            StartCoroutine(ResetConnectionUI(SettingsText.Translate("join_failed")));
            return;
        }

        // 로컬 트랜스포트로 테스트할 때는 Steam이 초기화되지 않는다
        RoomCode = SteamClient.IsValid ? InviteCode.FromSteamId(SteamClient.SteamId.Value) : "LOCAL";
        HideButtons();

        Debug.Log("Host started. Room code: " + RoomCode);
        NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    /// <summary>
    /// 초대 코드로 접속을 시작한다. 코드 형식이 틀리면 아무것도 하지 않고 false를 준다.
    /// 접속 자체가 실패하는 경우는 상태 텍스트로 알린다.
    /// </summary>
    public bool TryJoin(string roomCode)
    {
        if (!InviteCode.TryParse(roomCode, out ulong hostSteamId)) return false;

        transport.targetSteamId = hostSteamId;

        if (!NetworkManager.Singleton.StartClient())
        {
            StartCoroutine(ResetConnectionUI(SettingsText.Translate("join_failed")));
            return true;
        }

        HideButtons();
        Debug.Log("Client started. Target: " + hostSteamId);

        return true;
    }

    // 접속 성공 후 화면을 깔끔하게 하기 위해 UI를 숨기는 함수
    private void HideButtons()
    {
        if (hostButton != null) hostButton.gameObject.SetActive(false);
        if (clientButton != null) clientButton.gameObject.SetActive(false);
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
            rejectReason = SettingsText.Translate("join_disconnected");
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
        if (hostButton != null) hostButton.gameObject.SetActive(true);
        if (clientButton != null) clientButton.gameObject.SetActive(true);

        if (statusText != null)
        {
            statusText.text = SettingsText.Translate("main_status_idle");
        }
    }
}
