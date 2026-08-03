using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomUIController : MonoBehaviour
{
    public static RoomUIController Instance { get; private set; }

    [Header("호스트 전용")]

    [Header("플레이어 수 조정")]
    [SerializeField] private Button playerCountUpButton;
    [SerializeField] private Button playerCountDownButton;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private TMP_InputField playerCountInputField;
    [Header("마피아 수 조정")]
    [SerializeField] private Button mafiaCountUpButton;
    [SerializeField] private Button mafiaCountDownButton;
    [SerializeField] private TMP_Text mafiaCountText;
    [SerializeField] private TMP_InputField mafiaCountInputField;

    [Header("플레이 시간 조정")]
    [SerializeField] private Button playTimeUpButton;
    [SerializeField] private Button playTimeDownButton;
    [SerializeField] private TMP_Text playTimeText;
    [SerializeField] private TMP_InputField playTimeInputField;

    [Header("Ready / 게임 시작")]
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyButtonText;
    [SerializeField] private Button startGameButton;      // 호스트 전용
    [SerializeField] private TMP_Text startGameButtonText; // "게임 시작 (3/4)"
    [Header("참여자 수 표시")]
    [SerializeField] private TMP_Text participantCountText;
    [SerializeField] private Transform contentContainer; // 참여자 리스트 스크롤뷰의 Content
    [SerializeField] private GameObject playerEntryPrefab; // 참여자 리스트 항목 프리팹
    [Header("방 ID")]
    [SerializeField] private TMP_Text steamRoomIDText;

    private void Awake() => Instance = this;

    private void Start()
    {
        bool isHost = NetworkManager.Singleton.IsHost;

        // 호스트 전용 버튼: 호스트만 조작 가능하도록 설정
        SetHostButtonsInteractable(isHost);
        if (isHost)
        {
            // 플레이어 수 증감
            playerCountUpButton.onClick.AddListener(() => RoomSettings.Instance.PlayerCountUp());
            playerCountDownButton.onClick.AddListener(() => RoomSettings.Instance.PlayerCountDown());
            // 마피아 수 증감
            mafiaCountUpButton.onClick.AddListener(() => RoomSettings.Instance.MafiaCountUp());
            mafiaCountDownButton.onClick.AddListener(() => RoomSettings.Instance.MafiaCountDown());
            // 플레이 시간 증감
            playTimeUpButton.onClick.AddListener(() => RoomSettings.Instance.PlayTimeUp());
            playTimeDownButton.onClick.AddListener(() => RoomSettings.Instance.PlayTimeDown());

            // InputField 직접 입력 처리
            playerCountInputField.onEndEdit.AddListener(OnPlayerCountInputChanged);
            mafiaCountInputField.onEndEdit.AddListener(OnMafiaCountInputChanged);
            playTimeInputField.onEndEdit.AddListener(OnPlayTimeInputChanged);
        }

        if (isHost)
        {
            startGameButton.onClick.AddListener(() => LobbyFlowManager.Instance.TryStartGame());
        }

        // 참여자(호스트 포함 누구나)는 Ready 버튼으로 자신의 상태를 토글할 수 있음.
        // 실제 값 변경은 PlayerReady -> ServerRpc -> RoomSettings.SetPlayerReady()로만 이루어짐.
        readyButton.gameObject.SetActive(!isHost); // 호스트는 Ready 버튼이 필요 없음
        // Start()가 여러 번 호출될 가능성을 대비해 중복 등록 방지.
        readyButton.onClick.RemoveAllListeners();
        readyButton.onClick.AddListener(() =>
        {
            Debug.Log($"[RoomUIController] Ready 버튼 클릭됨. PlayerReady.LocalInstance is null? {PlayerReady.LocalInstance == null}");
            PlayerReady.LocalInstance?.ToggleReady();
        });

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientCountChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientCountChanged;

        // 처음 시작 시 방 ID 텍스트 업데이트
        steamRoomIDText.text = NetworkConnect.Instance?.RoomCode;

        // Start() 시점에는 아직 아무 이벤트도 발생하지 않았을 수 있으므로
        // 현재 상태를 UI에 최초 1회 강제로 반영해준다.
        // (RoomSettings가 이 프레임에 아직 스폰 전일 수 있어 한 프레임 대기 후 시도)
        StartCoroutine(InitialRefreshRoutine());
    }

    private System.Collections.IEnumerator InitialRefreshRoutine()
    {
        // RoomSettings.Instance가 아직 준비 안 됐다면 준비될 때까지 대기 (최대 몇 프레임이면 충분)
        yield return new WaitUntil(() => RoomSettings.Instance != null);
        Refresh();
    }

    private void OnClientCountChanged(ulong clientId) => Refresh();
    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientCountChanged;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientCountChanged;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void SetHostButtonsInteractable(bool isHost)
    {
        startGameButton.gameObject.SetActive(isHost);

        playerCountUpButton.interactable = isHost;
        playerCountDownButton.interactable = isHost;
        mafiaCountUpButton.interactable = isHost;
        mafiaCountDownButton.interactable = isHost;
        playTimeUpButton.interactable = isHost;
        playTimeDownButton.interactable = isHost;

        // 호스트 전용 버튼은 호스트가 아니면 비활성화
        playerCountUpButton.gameObject.SetActive(isHost);
        playerCountDownButton.gameObject.SetActive(isHost);
        mafiaCountUpButton.gameObject.SetActive(isHost);
        mafiaCountDownButton.gameObject.SetActive(isHost);
        playTimeUpButton.gameObject.SetActive(isHost);
        playTimeDownButton.gameObject.SetActive(isHost);

        // 인풋 필드 호스트만 조작 가능하도록 설정
        playerCountInputField.interactable = isHost;
        mafiaCountInputField.interactable = isHost;
        playTimeInputField.interactable = isHost;
    }

    // RoomSettings / PlayerReady 값이 바뀔 때마다 호출됨
    public void Refresh()
    {
        if (RoomSettings.Instance == null) return;

        playerCountText.text = $"{RoomSettings.Instance.PlayerCount.Value}";
        mafiaCountText.text = $"{RoomSettings.Instance.MafiaCount.Value}";
        playTimeText.text = $"{RoomSettings.Instance.PlayTimeMinutes.Value}";

        // InputField에 편집 중이 아닐 때만 갱신 (편집 중에 강제로 덮어쓰지 않기 위함)
        if (playerCountInputField != null && !playerCountInputField.isFocused) playerCountInputField.text = RoomSettings.Instance.PlayerCount.Value.ToString();
        if (mafiaCountInputField != null && !mafiaCountInputField.isFocused) mafiaCountInputField.text = RoomSettings.Instance.MafiaCount.Value.ToString();
        if (playTimeInputField != null && !playTimeInputField.isFocused) playTimeInputField.text = RoomSettings.Instance.PlayTimeMinutes.Value.ToString();

        // 참여자 수 표시
        if (participantCountText != null)
        {
            int currentCount = RoomSettings.Instance.AllPlayers.Count;
            int maxCount = RoomSettings.Instance.PlayerCount.Value;
            participantCountText.text = $"{currentCount} / {maxCount}";
        }
        
        // 참여자 리스트 동적 계산
        if (contentContainer != null && playerEntryPrefab != null)
        {
            // 기존 생성되어 있던 스크롤 뷰 내 항목들 일괄 정리
            foreach (Transform child in contentContainer)
            {
                Destroy(child.gameObject);
            }

            // 최신 동기화 리스트 순회하며 목록 재구현
            foreach (var playerInfo in RoomSettings.Instance.AllPlayers)
            {
                GameObject entryObj = Instantiate(playerEntryPrefab, contentContainer);
                if (entryObj.TryGetComponent<RoomPlayerEntryUI>(out var entryUI))
                {
                    // ClientId 0 혹은 서버 자체를 호스트 권한으로 식별
                    bool isHost = (playerInfo.ClientId == 0);
                    entryUI.SetPlayerInfo(playerInfo.PlayerName.ToString(), null, playerInfo.IsReady, isHost);
                }
            }
        }

        int total = RoomSettings.Instance.AllPlayers.Count;
        int targetCount = RoomSettings.Instance.PlayerCount.Value; // 설정된 정원
        int readyCount = 0;
        bool localIsReady = false;
        ulong localClientId = NetworkManager.Singleton.LocalClientId;

        Debug.Log("참여자 카운트 : " + total);

        for (int i = 0; i < RoomSettings.Instance.AllPlayers.Count; i++)
        {
            var playerInfo = RoomSettings.Instance.AllPlayers[i];

            Debug.Log($"[RoomUIController] AllPlayers[{i}] ClientId={playerInfo.ClientId}, IsReady={playerInfo.IsReady} / localClientId={localClientId}");

            if (playerInfo.IsReady)
            {
                readyCount++;
            }

            if (playerInfo.ClientId == localClientId)
            {
                Debug.Log($"[RoomUIController] 내 플레이어 정보 발견: ClientId={playerInfo.ClientId}, IsReady={playerInfo.IsReady}");
                localIsReady = playerInfo.IsReady;
            }
        }

        Debug.Log($"[RoomUIController] Refresh 결과: localClientId={localClientId}, localIsReady={localIsReady}, readyCount={readyCount}/{total}");

        if (readyButtonText != null)
        {
            readyButtonText.text = localIsReady ? "READY Cancel" : "READY";
        }

        if (startGameButton != null && startGameButton.gameObject.activeSelf)
        {
            startGameButtonText.text = $"GameStart ({readyCount}/{targetCount})";
            startGameButton.interactable = total == targetCount && readyCount == total;
        }
    }

    // ======================== InputField 직접 입력 처리 ========================
    private void OnPlayerCountInputChanged(string text)
    {
        if (int.TryParse(text, out int value))
        {
            RoomSettings.Instance.SetPlayerCount(value);
        }
        // 값이 유효하지 않거나, 유효해도 NetworkVariable이 변경 안 됐을 수 있으니
        // 항상 현재 값 기준으로 텍스트를 다시 맞춰준다 (되돌리기 효과)
        Refresh();
    }

    private void OnMafiaCountInputChanged(string text)
    {
        if (int.TryParse(text, out int value))
        {
            RoomSettings.Instance.SetMafiaCount(value);
        }
        Refresh();
    }

    private void OnPlayTimeInputChanged(string text)
    {
        if (int.TryParse(text, out int value))
        {
            RoomSettings.Instance.SetPlayTimeMinutes(value);
        }
        Refresh();
    }
}